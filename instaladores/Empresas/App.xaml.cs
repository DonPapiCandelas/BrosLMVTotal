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

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BrosLMV.Empresas
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
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "bros_inst_crash.txt"), DateTime.Now + Environment.NewLine + ex); }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Defensa en profundidad: si CUALQUIER paso de aquí truena (recurso faltante,
            // XAML mal formado, lo que sea), se ve un mensaje de error real y la app cierra
            // limpio -- nunca debe quedar una ventana en blanco sin explicación.
            try
            {
                // Instalar el runtime necesita admin (RegAsm/HKLM, C:\BrosLMV). Si no lo
                // somos, relanzamos con UAC y salimos.
                if (!IsAdmin()) { Elevate(); Shutdown(); return; }

                // Pantalla de bienvenida: nada se instala hasta que el usuario lo confirme.
                WelcomeWindow welcome;
                try { welcome = new WelcomeWindow(); }
                catch (Exception ex) { FallarInicio("No se pudo abrir la ventana de bienvenida", ex); return; }

                bool confirmo;
                try { confirmo = welcome.ShowDialog() == true; }
                catch (Exception ex) { FallarInicio("La ventana de bienvenida fallo al mostrarse", ex); return; }
                if (!confirmo) { Shutdown(); return; }

                ProgressWindow splash;
                try { splash = new ProgressWindow(); splash.Show(); }
                catch (Exception ex) { FallarInicio("No se pudo abrir la ventana de progreso", ex); return; }

                Task.Run(() =>
                {
                    string status; bool ok;
                    try { status = RuntimeInstaller.Install(); ok = true; }
                    catch (Exception ex) { status = "No se pudo instalar el runtime: " + ex.Message; ok = false; Log(ex); }

                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            splash.Close();
                            var main = new MainWindow();
                            main.SetEstadoRuntime(status, ok);
                            main.Closed += (s2, e2) => Shutdown();
                            MainWindow = main;
                            main.Show();
                        }
                        catch (Exception ex)
                        {
                            FallarInicio("No se pudo abrir la ventana principal", ex);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                FallarInicio("Error inesperado al iniciar el instalador", ex);
            }
        }

        // Muestra el error real (nunca una ventana en blanco), lo registra en
        // %TEMP%\bros_inst_crash.txt, y cierra la aplicacion de forma limpia.
        void FallarInicio(string contexto, Exception ex)
        {
            Log(ex);
            try
            {
                MessageBox.Show(
                    contexto + ":\n\n" + ex.Message +
                    "\n\nDetalle guardado en: " + Path.Combine(Path.GetTempPath(), "bros_inst_crash.txt"),
                    "BrosLMV - Instalador", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* si hasta el MessageBox falla, al menos ya quedo el log */ }
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
                Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location)
                { Verb = "runas", UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Se necesita ejecutar como administrador para instalar el runtime.",
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
