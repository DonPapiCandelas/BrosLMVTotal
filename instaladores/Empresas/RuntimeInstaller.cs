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
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BrosLMV.Empresas
{
    // Instala el RUNTIME de BrosLMV en el equipo (DLLs + COM + icono). NO toca SQL.
    // El payload (DLLs, scripts, broslmv_conn.txt, BrosLMV.ico) va embebido como payload.zip.
    static class RuntimeInstaller
    {
        const string Base = @"C:\BrosLMV";
        const string Clsid = "{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}";

        public static string Install()
        {
            var sb = new StringBuilder();

            foreach (var p in Process.GetProcessesByName("ComercialSP")) { try { p.Kill(); } catch { } }
            System.Threading.Thread.Sleep(1200);

            string tmp = Path.Combine(Path.GetTempPath(), "BrosLMV_pl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            ExtractPayload(tmp);

            foreach (var d in new[] { "bin", @"bin\x86", "host", "workers", "runtimes", "scripts", "logs", "data" })
                Directory.CreateDirectory(Path.Combine(Base, d));

            string srcBin = Path.Combine(tmp, "bin");
            foreach (var f in Directory.GetFiles(srcBin, "*.dll"))
                File.Copy(f, Path.Combine(Base, "bin", Path.GetFileName(f)), true);
            string srcX86 = Path.Combine(srcBin, "x86");
            if (Directory.Exists(srcX86))
                foreach (var f in Directory.GetFiles(srcX86, "*.dll"))
                    File.Copy(f, Path.Combine(Base, "bin", "x86", Path.GetFileName(f)), true);

            CopyDirectoryIfExists(Path.Combine(tmp, "host"), Path.Combine(Base, "host"), true);
            CopyDirectoryIfExists(Path.Combine(tmp, "workers"), Path.Combine(Base, "workers"), true);
            CopyDirectoryIfExists(Path.Combine(tmp, "runtimes"), Path.Combine(Base, "runtimes"), true);

            // Plantilla de conexion de respaldo: solo si no existe (no pisar credenciales).
            string conn = Path.Combine(srcBin, "broslmv_conn.txt");
            string connDst = Path.Combine(Base, "bin", "broslmv_conn.txt");
            if (File.Exists(conn) && !File.Exists(connDst)) File.Copy(conn, connDst);

            // Scripts de ejemplo: solo si no existen.
            string srcScripts = Path.Combine(tmp, "scripts");
            if (Directory.Exists(srcScripts))
                foreach (var f in Directory.GetFiles(srcScripts))
                {
                    string dst = Path.Combine(Base, "scripts", Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }

            sb.Append("Runtime instalado en C:\\BrosLMV. ");
            sb.Append(CopyIcon(Path.Combine(tmp, "BrosLMV.ico")));
            sb.Append(RegisterCom());

            try { Directory.Delete(tmp, true); } catch { }
            return sb.ToString();
        }

        static void ExtractPayload(string dir)
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new Exception("payload.zip no esta embebido en el ejecutable.");
            string zip = Path.Combine(dir, "_p.zip");
            using (var s = asm.GetManifestResourceStream(name))
            using (var fs = File.Create(zip)) s.CopyTo(fs);
            ZipFile.ExtractToDirectory(zip, dir);
            File.Delete(zip);
        }

        static string CopyIcon(string ico)
        {
            if (!File.Exists(ico)) return "";
            string[] roots = { @"C:\Program Files (x86)\Compac\ComercialSP", @"C:\Program Files\Compac\ComercialSP" };
            bool any = false;
            foreach (var root in roots)
                if (Directory.Exists(root))
                {
                    string icons = Path.Combine(root, "Icons");
                    Directory.CreateDirectory(icons);
                    File.Copy(ico, Path.Combine(icons, "BrosLMV.ico"), true);
                    any = true;
                }
            return any ? "Icono copiado a ComercialSP\\Icons. " : "(Aviso: no se encontro ComercialSP para el icono.) ";
        }

        static void CopyDirectoryIfExists(string src, string dst, bool overwrite)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                File.Copy(file, Path.Combine(dst, rel), overwrite);
            }
        }

        static string RegisterCom()
        {
            string regasm = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe";
            string dll = Path.Combine(Base, "bin", "BrosLMVClsMain.dll");
            int rc = Run(regasm, "\"" + dll + "\" /codebase /tlb");
            string ips = "HKLM\\SOFTWARE\\WOW6432Node\\Classes\\CLSID\\" + Clsid + "\\InprocServer32";
            if (Run("reg.exe", "query \"" + ips + "\"") != 0)
                Run("reg.exe", "copy \"HKLM\\SOFTWARE\\Classes\\CLSID\\" + Clsid + "\" \"HKLM\\SOFTWARE\\WOW6432Node\\Classes\\CLSID\\" + Clsid + "\" /s /f");
            Run("reg.exe", "add \"HKLM\\SOFTWARE\\WOW6432Node\\Classes\\BrosLMV.clsMain\\CLSID\" /ve /d \"" + Clsid + "\" /f");
            return rc == 0 ? "COM registrado." : "(Aviso: RegAsm devolvio error " + rc + ".)";
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
