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
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Desinstalador
{
    internal static class Desinstalador
    {
        public const string CarpetaBase = @"C:\BrosLMV\Descargas";
        public const string NombreServicio = "BrosLMV Descargas";

        public static string Ejecutar(bool borrarBd, string cadenaConexionMaster, string nombreBase)
        {
            var sb = new StringBuilder();

            sb.AppendLine(DetenerYQuitarServicio() ? "✓ Servicio de Windows eliminado" : "(el servicio ya no existía)");

            foreach (var p in Process.GetProcessesByName("BrosLMV.DescargasUI"))
            {
                try { p.CloseMainWindow(); p.WaitForExit(3000); if (!p.HasExited) p.Kill(); } catch { }
            }
            Thread.Sleep(500);

            try
            {
                string menuInicio = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs\BrosLMV Descargas.lnk";
                string escritorio = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) + @"\BrosLMV Descargas.lnk";
                if (File.Exists(menuInicio)) File.Delete(menuInicio);
                if (File.Exists(escritorio)) File.Delete(escritorio);
                sb.AppendLine("✓ Accesos directos eliminados");
            }
            catch (Exception ex)
            {
                sb.AppendLine("⚠ No se pudieron quitar los accesos directos: " + ex.Message);
            }

            try
            {
                if (Directory.Exists(CarpetaBase))
                {
                    Directory.Delete(CarpetaBase, true);
                    sb.AppendLine("✓ " + CarpetaBase + " eliminado");
                }
                else
                {
                    sb.AppendLine("(" + CarpetaBase + " ya no existía)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("⚠ No se pudo borrar " + CarpetaBase + ": " + ex.Message +
                    " (cierra BrosLMV.DescargasUI si sigue abierto y reintenta)");
            }

            if (borrarBd)
            {
                try
                {
                    using (var cn = new SqlConnection(cadenaConexionMaster))
                    {
                        cn.Open();
                        using (var cmd = cn.CreateCommand())
                        {
                            // ALTER...SET SINGLE_USER fuerza el cierre de conexiones abiertas
                            // antes del DROP -- si no, un candado sp_getapplock activo (u otra
                            // conexion de una Tarea Programada a medias) tumba el DROP.
                            cmd.CommandText =
                                "DECLARE @q sysname = QUOTENAME(@nombre);\n" +
                                "IF DB_ID(@nombre) IS NOT NULL BEGIN\n" +
                                "  EXEC('ALTER DATABASE ' + @q + ' SET SINGLE_USER WITH ROLLBACK IMMEDIATE');\n" +
                                "  EXEC('DROP DATABASE ' + @q);\n" +
                                "END";
                            cmd.Parameters.AddWithValue("@nombre", nombreBase);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    sb.AppendLine("✓ Base de datos " + nombreBase + " eliminada");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("⚠ No se pudo borrar la base de datos: " + ex.Message);
                }
            }
            else
            {
                sb.AppendLine("(Base de datos NO tocada -- no se marcó la casilla)");
            }

            try
            {
                string configServicio = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "servicio.json");
                if (File.Exists(configServicio)) File.Delete(configServicio);
            }
            catch { /* no critico -- ya no queda servicio que la lea */ }

            return sb.ToString();
        }

        static bool DetenerYQuitarServicio()
        {
            try
            {
                using (var sc = new ServiceController(NombreServicio))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
            }
            catch { return false; /* no existia */ }

            var psi = new ProcessStartInfo("sc.exe", "delete \"" + NombreServicio + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
            }
            return true;
        }
    }
}
