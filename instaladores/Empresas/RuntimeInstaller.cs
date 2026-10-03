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
using System.Collections.Generic;
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

            foreach (var d in new[] { "bin", @"bin\x86", "host", "runner", "workers", "runtimes", "scripts", "logs", "data" })
                Directory.CreateDirectory(Path.Combine(Base, d));

            string srcBin = Path.Combine(tmp, "bin");
            foreach (var f in Directory.GetFiles(srcBin, "*.dll"))
                File.Copy(f, Path.Combine(Base, "bin", Path.GetFileName(f)), true);
            string srcX86 = Path.Combine(srcBin, "x86");
            if (Directory.Exists(srcX86))
                foreach (var f in Directory.GetFiles(srcX86, "*.dll"))
                    File.Copy(f, Path.Combine(Base, "bin", "x86", Path.GetFileName(f)), true);

            // --- SCRIPTS primero: es lo critico para que los botones funcionen; no debe
            //     quedar bloqueado por una copia opcional (htmlpdf/formatos) que falle. ---
            // 2.94.0: la UNICA plantilla es CREAR_DOC_DESDE_XML.ctx. Las anteriores (PLANTILLA_*, EJEMPLO_*, PRUEBA_*, REQUISICION, SOLICITUD_COMPRA) se
            // MUEVEN (no se borran) a scripts\_archivo\plantillas_anteriores_<fecha>\ para no perder cambios propios. Solo la raiz de scripts\
            // (los scripts de cada empresa viven en scripts\<EMPRESA>\ y no se tocan).
            try
            {
                string raizScripts = Path.Combine(Base, "scripts");
                var obsoletas = new List<string>();
                foreach (var patron in new[] { "PLANTILLA_*", "EJEMPLO_*", "PRUEBA_*", "REQUISICION.ctx", "SOLICITUD_COMPRA.ctx", "SALDOS_ESTADOS_CUENTA.ctx", "COBRO_PAGO_*", "CREAR_DOCUMENTO_*" })       // 3.0.0: «Saldos y estados de cuenta» se separó en ESTADO_CUENTA_CLIENTES y ESTADO_CUENTA_PROVEEDORES
                    obsoletas.AddRange(Directory.GetFiles(raizScripts, patron));
                if (obsoletas.Count > 0)
                {
                    string arch = Path.Combine(raizScripts, "_archivo", "plantillas_anteriores_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Directory.CreateDirectory(arch);
                    foreach (var f in obsoletas)
                        try { File.Move(f, Path.Combine(arch, Path.GetFileName(f))); } catch { }
                    sb.Append("Plantillas anteriores retiradas (" + obsoletas.Count + "). ");
                }
            }
            catch { }

            // CORE fijos + toda plantilla de fabrica del paquete (cabecera «Plantilla: <nombre>» con //, # o --): se refrescan siempre.
            var coreScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cotizador.ctx", "ConfiguracionFormato.ctx" };
            string srcScripts = Path.Combine(tmp, "scripts");
            if (Directory.Exists(srcScripts))
                foreach (var f in Directory.GetFiles(srcScripts))
                {
                    try
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".ctx" && ext != ".csx" && ext != ".py" && ext != ".sql") continue;
                        string cab = File.ReadAllText(f);
                        if (cab.Length > 3000) cab = cab.Substring(0, 3000);
                        if (System.Text.RegularExpressions.Regex.IsMatch(cab, @"^\s*(?://|#|--)\s*Plantilla\s*:", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            coreScripts.Add(Path.GetFileName(f));
                    }
                    catch { }
                }
            if (Directory.Exists(srcScripts))
                foreach (var f in Directory.GetFiles(srcScripts))
                {
                    string nombre = Path.GetFileName(f);
                    string dst = Path.Combine(Base, "scripts", nombre);
                    // los CORE siempre se refrescan; los de ejemplo solo si no existen
                    if (coreScripts.Contains(nombre) || !File.Exists(dst))
                        try { File.Copy(f, dst, true); } catch { }
                }

            // Cada bloque opcional en su propio try/catch: si uno falla (p.ej. un .exe en uso),
            // el resto de la instalacion sigue.
            try { CopyDirectoryIfExists(Path.Combine(tmp, "host"), Path.Combine(Base, "host"), true); } catch { }
            // Runner headless (T3.3): lo invoca un CRM externo y cualquier otro consumidor
            // externo por linea de comandos. Viaja en el mismo payload que host/workers.
            try { CopyDirectoryIfExists(Path.Combine(tmp, "runner"), Path.Combine(Base, "runner"), true); } catch { }
            try { CopyDirectoryIfExists(Path.Combine(tmp, "workers"), Path.Combine(Base, "workers"), true); } catch { }
            try { CopyDirectoryIfExists(Path.Combine(tmp, "runtimes"), Path.Combine(Base, "runtimes"), true); } catch { }

            // BrosLMV.HtmlToPdf.exe (motor HTML->PDF) + las 10 plantillas genericas: siempre se
            // refrescan. Las plantillas van a C:\BrosLMV\formatos (fuente del boton "Instalar
            // formatos BrosLMV") y tambien a la carpeta Formatos de Comercial de este equipo.
            // BrosLMV.Disenador.exe (editor visual de formatos, programa aparte, autocontenido): siempre se refresca.
            try { CopyDirectoryIfExists(Path.Combine(tmp, "disenador"), Path.Combine(Base, "disenador"), true); } catch { }
            try { CopyDirectoryIfExists(Path.Combine(tmp, "htmlpdf"), Path.Combine(Base, "htmlpdf"), true); } catch { }
            try { CopyDirectoryIfExists(Path.Combine(tmp, "formatos"), Path.Combine(Base, "formatos"), true); } catch { }
            string srcFmt = Path.Combine(tmp, "formatos");
            if (Directory.Exists(srcFmt))
                foreach (var fd in new[] {
                    @"C:\Compac\ComercialSP\Formatos",
                    @"C:\Program Files (x86)\Compac\ComercialSP\Formatos",
                    @"C:\Program Files\Compac\ComercialSP\Formatos" })
                {
                    try
                    {
                        string padre = Path.GetDirectoryName(fd.TrimEnd('\\'));
                        if (padre == null || !Directory.Exists(padre)) continue;
                        Directory.CreateDirectory(fd);
                        foreach (var f in Directory.GetFiles(srcFmt, "*.html"))
                            File.Copy(f, Path.Combine(fd, Path.GetFileName(f)), true);
                    }
                    catch { }
                }

            // 2.95.0: catalogo de iconos BrosLMV (BrosLMV_*.ico) a la carpeta Icons de Comercial (ahi los lee el ribbon) + catalogo y licencia a C:\BrosLMV\iconos.
            try
            {
                string srcIco = Path.Combine(tmp, "iconos");
                if (Directory.Exists(srcIco))
                {
                    string dstIco = Path.Combine(Base, "iconos");
                    Directory.CreateDirectory(dstIco);
                    foreach (var n in new[] { "iconos.json", "LICENCIA_Lucide.txt" })
                        if (File.Exists(Path.Combine(srcIco, n))) File.Copy(Path.Combine(srcIco, n), Path.Combine(dstIco, n), true);
                    int copiados = 0;
                    foreach (var root in new[] { @"C:\Program Files (x86)\Compac\ComercialSP", @"C:\Program Files\Compac\ComercialSP" })
                    {
                        if (!Directory.Exists(root)) continue;
                        string icons = Path.Combine(root, "Icons");
                        Directory.CreateDirectory(icons);
                        foreach (var f in Directory.GetFiles(srcIco, "BrosLMV_*.ico"))
                            try { File.Copy(f, Path.Combine(icons, Path.GetFileName(f)), true); copiados++; } catch { }
                    }
                    sb.Append("Iconos BrosLMV instalados (" + copiados + "). ");
                }
            }
            catch { }

            // Plantilla de conexion de respaldo: solo si no existe (no pisar credenciales).
            try
            {
                string conn = Path.Combine(srcBin, "broslmv_conn.txt");
                string connDst = Path.Combine(Base, "bin", "broslmv_conn.txt");
                if (File.Exists(conn) && !File.Exists(connDst)) File.Copy(conn, connDst);
            }
            catch { }

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
