// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// App.xaml.cs -- mismo patron defensivo que instaladores\Empresas\App.xaml.cs: elevacion manual
// (no manifest requireAdministrator, para poder mostrar un mensaje amigable si el usuario
// cancela el UAC) y cada paso envuelto en try/catch propio para nunca dejar una ventana en
// blanco sin explicacion.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BrosLMV.Descargas.Instalador
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                MessageBox.Show("Error: " + e.Exception.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
        }

        static void Log(Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "bros_descargas_inst_crash.txt"), DateTime.Now + Environment.NewLine + ex); }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Copiar a C:\BrosLMV\Descargas y crear Tareas Programadas necesita admin.
                if (!IsAdmin()) { Elevate(); Shutdown(); return; }

                WelcomeWindow welcome;
                try { welcome = new WelcomeWindow(); }
                catch (Exception ex) { FallarInicio("No se pudo abrir la ventana de bienvenida", ex); return; }

                bool confirmo;
                try { confirmo = welcome.ShowDialog() == true; }
                catch (Exception ex) { FallarInicio("La ventana de bienvenida fallo al mostrarse", ex); return; }
                if (!confirmo) { Shutdown(); return; }

                ConfigWindow config;
                try { config = new ConfigWindow(); }
                catch (Exception ex) { FallarInicio("No se pudo abrir la ventana de configuración", ex); return; }

                bool configOk;
                try { configOk = config.ShowDialog() == true; }
                catch (Exception ex) { FallarInicio("La ventana de configuración fallo al mostrarse", ex); return; }
                if (!configOk) { Shutdown(); return; }

                string cadenaConexion = config.CadenaConexion;
                TimeSpan intervalo = config.IntervaloSolicitar;

                ProgressWindow splash;
                try { splash = new ProgressWindow(); splash.Show(); }
                catch (Exception ex) { FallarInicio("No se pudo abrir la ventana de progreso", ex); return; }

                Task.Run(() =>
                {
                    string resumen; bool ok;
                    try { resumen = Instalador.Ejecutar(cadenaConexion, intervalo); ok = true; }
                    catch (Exception ex) { resumen = "No se pudo completar la instalación:\n\n" + ex.Message; ok = false; Log(ex); }

                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            splash.Close();
                            var final = new ResultWindow(resumen, ok);
                            final.Closed += (s2, e2) => Shutdown();
                            MainWindow = final;
                            final.Show();
                        }
                        catch (Exception ex)
                        {
                            FallarInicio("No se pudo abrir la ventana final", ex);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                FallarInicio("Error inesperado al iniciar el instalador", ex);
            }
        }

        void FallarInicio(string contexto, Exception ex)
        {
            Log(ex);
            try
            {
                MessageBox.Show(
                    contexto + ":\n\n" + ex.Message +
                    "\n\nDetalle guardado en: " + Path.Combine(Path.GetTempPath(), "bros_descargas_inst_crash.txt"),
                    "BrosLMV Descargas - Instalador", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            Shutdown();
        }

        static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        void Elevate()
        {
            try
            {
                // Assembly.Location devuelve "" en un publish single-file (advertencia IL3000) --
                // Environment.ProcessPath SI da la ruta real del .exe corriendo, con o sin bundle.
                Process.Start(new ProcessStartInfo(Environment.ProcessPath)
                { Verb = "runas", UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Se necesita ejecutar como administrador para instalar BrosLMV.Descargas.",
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
