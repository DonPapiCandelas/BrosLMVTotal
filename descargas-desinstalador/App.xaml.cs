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
using System.Security.Principal;
using System.Windows;

namespace BrosLMV.Descargas.Desinstalador
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
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "bros_descargas_desinst_crash.txt"), DateTime.Now + Environment.NewLine + ex); }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                if (!IsAdmin()) { Elevate(); Shutdown(); return; }

                var main = new MainWindow();
                main.Closed += (s, e2) => Shutdown();
                MainWindow = main;
                main.Show();
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show("Error inesperado:\n\n" + ex.Message, "BrosLMV Descargas - Desinstalador", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
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
                MessageBox.Show("Se necesita ejecutar como administrador para desinstalar BrosLMV.Descargas.",
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
