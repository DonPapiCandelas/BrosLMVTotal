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
using System.Text;
using System.Threading;

namespace BrosLMV.Desinstalador
{
    // Quita el RUNTIME de BrosLMV del equipo: des-registra el COM, borra el icono de
    // ComercialSP y elimina por completo C:\BrosLMV (para no dejar las DLLs).
    static class RuntimeUninstaller
    {
        const string Base = @"C:\BrosLMV";
        const string Clsid = "{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}";

        public static string Remove()
        {
            var sb = new StringBuilder();

            foreach (var p in Process.GetProcessesByName("ComercialSP")) { try { p.Kill(); } catch { } }
            Thread.Sleep(1200);

            // 1) Des-registrar el COM
            string regasm = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe";
            string dll = Path.Combine(Base, "bin", "BrosLMVClsMain.dll");
            if (File.Exists(dll)) Run(regasm, "\"" + dll + "\" /unregister");
            Run("reg.exe", "delete \"HKLM\\SOFTWARE\\WOW6432Node\\Classes\\BrosLMV.clsMain\" /f");
            Run("reg.exe", "delete \"HKLM\\SOFTWARE\\WOW6432Node\\Classes\\CLSID\\" + Clsid + "\" /f");
            Run("reg.exe", "delete \"HKLM\\SOFTWARE\\Classes\\CLSID\\" + Clsid + "\" /f");
            sb.Append("COM des-registrado. ");

            // 2) Borrar el icono de ComercialSP
            foreach (var root in new[] { @"C:\Program Files (x86)\Compac\ComercialSP", @"C:\Program Files\Compac\ComercialSP" })
            {
                try { string ico = Path.Combine(root, "Icons", "BrosLMV.ico"); if (File.Exists(ico)) File.Delete(ico); }
                catch { }
            }

            // 3) Eliminar por completo C:\BrosLMV (DLLs, scripts, datos, logs)
            try
            {
                if (Directory.Exists(Base)) Directory.Delete(Base, true);
                sb.Append("Carpeta C:\\BrosLMV eliminada.");
            }
            catch (Exception ex)
            {
                sb.Append("No se pudo borrar C:\\BrosLMV (" + ex.Message + "). Cierra CONTPAQi y reintenta.");
            }
            return sb.ToString();
        }

        static int Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                var p = Process.Start(psi);
                p.StandardError.ReadToEnd(); p.StandardOutput.ReadToEnd(); p.WaitForExit();
                return p.ExitCode;
            }
            catch { return -1; }
        }
    }
}
