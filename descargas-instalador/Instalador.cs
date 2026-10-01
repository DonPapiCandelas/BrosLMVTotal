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

// Instalador.cs -- el payload (CLI+UI+servicio self-contained, publicados por
// generar_exes_descargas.ps1) va embebido como payload.zip, mismo patron que
// instaladores\Empresas\RuntimeInstaller.cs. A diferencia de ese instalador, aqui NO hay COM que
// registrar: copiar archivos, crear la BD propia, registrar+arrancar el Servicio de Windows
// "BrosLMV Descargas" (reemplazo de las Tareas Programadas -- pedido explicito del usuario
// 2026-08-19: "quiero todo en segundo plano... debe existir un servicio", tras confirmar que las
// Tareas Programadas abrian una consola visible cada vez que corrian) y dejar accesos directos.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Instalador
{
    internal static class Instalador
    {
        public const string CarpetaBase = @"C:\BrosLMV\Descargas";
        public static readonly string CarpetaCli = Path.Combine(CarpetaBase, "cli");
        public static readonly string CarpetaUi = Path.Combine(CarpetaBase, "ui");
        public static readonly string CarpetaServicio = Path.Combine(CarpetaBase, "servicio");
        public const string NombreServicio = "BrosLMV Descargas";

        public static string Ejecutar(string cadenaConexion, TimeSpan intervaloSolicitar)
        {
            var sb = new StringBuilder();

            string tmp = Path.Combine(Path.GetTempPath(), "BrosLMV_Descargas_pl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            ExtractPayload(tmp);

            // Si ya existia una instalacion previa con el servicio corriendo, hay que pararlo
            // ANTES de sobreescribir sus archivos (el .exe queda en uso mientras el servicio
            // corre) -- reinstalar/actualizar encima de una instalacion viva no debe fallar.
            DetenerServicioSiExiste();

            Directory.CreateDirectory(CarpetaCli);
            Directory.CreateDirectory(CarpetaUi);
            Directory.CreateDirectory(CarpetaServicio);
            CopyDirectory(Path.Combine(tmp, "cli"), CarpetaCli);
            CopyDirectory(Path.Combine(tmp, "ui"), CarpetaUi);
            CopyDirectory(Path.Combine(tmp, "servicio"), CarpetaServicio);
            sb.AppendLine("✓ Aplicación, motor y servicio actualizados en " + CarpetaBase);

            // Mismo formato que Configuracion.cs (JSON simple, {"CadenaConexion": "..."}) -- para
            // que la UI arranque ya conectada, sin volver a pedir la conexion en ConexionWindow.
            string configJson = "{\n  \"CadenaConexion\": \"" + EscaparJson(cadenaConexion) + "\"\n}";
            File.WriteAllText(Path.Combine(CarpetaUi, "config.json"), configJson);
            sb.AppendLine("✓ Conexión conservada/actualizada para la aplicación");

            EsquemaSql.AsegurarBaseDeDatos(cadenaConexion);
            using (var conn = new SqlConnection(cadenaConexion))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);
            }
            sb.AppendLine("✓ Base de datos y esquema listos");

            // Config propia del servicio (%ProgramData%, no junto al exe -- ver
            // ConfigServicio.cs): el servicio corre como LocalSystem y relee este archivo en
            // cada vuelta de su loop, asi que cambiar el intervalo despues (desde
            // "Automatizacion" en la app) no requiere reinstalar ni reiniciar nada.
            new ConfigServicio { CadenaConexion = cadenaConexion, IntervaloSolicitarMinutos = (int)intervaloSolicitar.TotalMinutes }.Guardar();
            sb.AppendLine("✓ Configuración del servicio guardada");

            string rutaServicioExe = Path.Combine(CarpetaServicio, "BrosLMV.Descargas.Servicio.exe");
            var (exitoServicio, errorServicio) = InstalarYArrancarServicio(rutaServicioExe);
            sb.AppendLine(exitoServicio
                ? "✓ Servicio de Windows \"" + NombreServicio + "\" instalado, arrancado y verificado (revisa/descarga cada 10 min, solicita cada " + TextoIntervalo(intervaloSolicitar) + ", verifica estatus ~diario -- todo sin ventanas visibles)"
                : "⚠ No se pudo instalar/arrancar el servicio: " + errorServicio);

            string rutaUiExe = Path.Combine(CarpetaUi, "BrosLMV.DescargasUI.exe");
            try
            {
                CrearAccesoDirecto(rutaUiExe, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs\BrosLMV Descargas.lnk");
                CrearAccesoDirecto(rutaUiExe, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) + @"\BrosLMV Descargas.lnk");
                sb.AppendLine("✓ Accesos directos creados (menú Inicio y Escritorio)");
            }
            catch (Exception ex)
            {
                sb.AppendLine("⚠ No se pudieron crear los accesos directos: " + ex.Message);
            }

            try { Directory.Delete(tmp, true); } catch { }

            sb.AppendLine();
            sb.AppendLine("Listo. Las descargas corren solas en segundo plano, sin necesidad de tener la aplicación abierta.");
            return sb.ToString();
        }

        static void DetenerServicioSiExiste()
        {
            try
            {
                using var sc = new ServiceController(NombreServicio);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                EjecutarSc("delete \"" + NombreServicio + "\"");
            }
            catch { /* no existia -- primera instalacion, nada que detener */ }
        }

        static (bool exito, string error) InstalarYArrancarServicio(string rutaExe)
        {
            // sc.exe es quisquilloso con los espacios: "binPath= valor" (un espacio exacto
            // despues del "=", pegado a la palabra) -- no es un descuido de formato.
            string args = "create \"" + NombreServicio + "\" binPath= \"\\\"" + rutaExe + "\\\"\" " +
                "start= auto DisplayName= \"BrosLMV Descargas (SAT)\"";
            var (codigo, salida) = EjecutarSc(args);
            if (codigo != 0) return (false, "sc create devolvio codigo " + codigo + ": " + salida);

            EjecutarSc("description \"" + NombreServicio + "\" \"Descarga automatica de CFDI del SAT en segundo plano, sin ventanas visibles.\"");
            // Reinicio automatico si el proceso truena (ej. SQL Server no disponible un rato).
            EjecutarSc("failure \"" + NombreServicio + "\" reset= 86400 actions= restart/60000/restart/60000/restart/60000");
            // Sin este flag, Windows solo reinicia el servicio si el proceso se CAE; si termina con un
            // codigo de error de forma "limpia" (el host se detiene por una excepcion no controlada) se
            // quedaba detenido para siempre. Con el flag tambien se reinicia en ese caso.
            EjecutarSc("failureflag \"" + NombreServicio + "\" 1");

            try
            {
                using var sc = new ServiceController(NombreServicio);
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                // Que llegue a Running solo confirma que el proceso nació. Esperamos un momento
                // y refrescamos para detectar de inmediato un servicio que muere al leer su
                // configuración, en vez de declarar una actualización exitosa falsamente.
                System.Threading.Thread.Sleep(3000);
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running)
                    return (false, "el servicio arrancó pero se detuvo inmediatamente (estado: " + sc.Status + "). Revisa la bitácora de BrosLMV Descargas.");
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, "el servicio se creo pero no arranco: " + ex.Message);
            }
        }

        static (int codigo, string salida) EjecutarSc(string args)
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            string salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, salida);
        }

        static string TextoIntervalo(TimeSpan t) =>
            t.TotalMinutes < 60 ? (int)t.TotalMinutes + " min" : (int)t.TotalHours + "h";

        static string EscaparJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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

        static void CopyDirectory(string src, string dst)
        {
            if (!Directory.Exists(src)) throw new Exception("Falta en el payload: " + src);
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                File.Copy(file, Path.Combine(dst, rel), true);
            }
        }

        // .NET no trae una API nativa para crear .lnk -- WScript.Shell (COM, viene con Windows
        // desde siempre) es el mecanismo estandar, sin necesitar ninguna referencia/paquete extra.
        static void CrearAccesoDirecto(string rutaExe, string rutaLnk)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rutaLnk));
            Type tipoShell = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(tipoShell);
            try
            {
                dynamic acceso = shell.CreateShortcut(rutaLnk);
                acceso.TargetPath = rutaExe;
                acceso.WorkingDirectory = Path.GetDirectoryName(rutaExe);
                acceso.IconLocation = rutaExe;
                acceso.Description = "BrosLMV Descargas — descarga automática de CFDI del SAT";
                acceso.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }
}
