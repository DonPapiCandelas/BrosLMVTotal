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

// ContabilidadAdd.cs -- carga los XML descargados al ADD (Administrador de Documentos Digitales) de una empresa de
// CONTPAQi Contabilidad, para que Contabilidad los vea y los asocie a polizas (pedido del usuario 2026-10-17).
//
// Como: NUNCA se escribe por SQL en las bases document_<GuidDSL>_*. Se usa el servidor de aplicaciones de CONTPAQi
// (http://127.0.0.1:9080/saci/...) con las DLL de ConvertidorADD (.NET Framework), via CargaAdd.ps1 en Windows
// PowerShell 5.1. Aqui solo se lee por SQL: la lista de empresas de Contabilidad (GeneralesSQL.dbo.ListaEmpresas), su
// GuidDSL / GuidEmpresa (ctXXX.dbo.Parametros) y los UUID que ya estan en el ADD (para no repetir y para confirmar lo
// que de verdad quedo cargado). Detalle en Entrenamiento\contabilidad\ADD_SACI_CARGA_DE_XML.md.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal sealed class EmpresaContabilidadInfo
    {
        public string Nombre;
        public string BaseDatos;   // ctXXX
        public string Ruta;        // E:\Compac\Empresas\ctXXX\
        public string Rfc;
        public string GuidDsl;     // identifica las bases document_<GuidDsl>_metadata / _content
        public string GuidEmpresa;
        public override string ToString() => Nombre + "  (" + Rfc + ")";
    }

    internal sealed class ResultadoContabilidad
    {
        public int Cargados;
        public int YaEstaban;
        public int NoSeCargaron;
        public int SinArchivo;
        public string Error;
        public bool Exito => string.IsNullOrEmpty(Error);
    }

    internal static class ContabilidadAdd
    {
        private static readonly string[] CarpetasDll =
        {
            @"C:\Program Files (x86)\Compac\ConvertidorADD",
            @"C:\Program Files\Compac\ConvertidorADD"
        };

        public static string CarpetaDlls() => CarpetasDll.FirstOrDefault(c => File.Exists(Path.Combine(c, "SaciReferences.dll")));

        // Empresas de Contabilidad de este servidor SQL con su GuidDSL (de la tabla Parametros de cada una).
        public static List<EmpresaContabilidadInfo> ListarEmpresas(SqlConnection conn)
        {
            var lista = new List<EmpresaContabilidadInfo>();
            using (var cmd = new SqlCommand("SELECT Nombre, AliasBDD, RutaResp FROM GeneralesSQL.dbo.ListaEmpresas ORDER BY Nombre", conn))
            using (var l = cmd.ExecuteReader())
                while (l.Read())
                    lista.Add(new EmpresaContabilidadInfo
                    {
                        Nombre = l.IsDBNull(0) ? "" : l.GetString(0).Trim(),
                        BaseDatos = l.IsDBNull(1) ? "" : l.GetString(1).Trim(),
                        Ruta = l.IsDBNull(2) ? "" : l.GetString(2).Trim()
                    });

            var validas = new List<EmpresaContabilidadInfo>();
            foreach (var e in lista)
            {
                if (!Regex.IsMatch(e.BaseDatos, "^[A-Za-z0-9_]+$")) continue;
                try
                {
                    using (var cmd = new SqlCommand("SELECT TOP 1 RFC, CONVERT(varchar(36), GuidDSL), CONVERT(varchar(36), GuidEmpresa) FROM [" + e.BaseDatos + "].dbo.Parametros", conn))
                    using (var l = cmd.ExecuteReader())
                        if (l.Read())
                        {
                            e.Rfc = l.IsDBNull(0) ? "" : l.GetString(0).Trim();
                            e.GuidDsl = l.IsDBNull(1) ? "" : l.GetString(1).ToLowerInvariant();
                            e.GuidEmpresa = l.IsDBNull(2) ? "" : l.GetString(2).ToUpperInvariant();
                        }
                }
                catch { continue; } // base no accesible o sin Parametros: no es una empresa usable
                if (!string.IsNullOrEmpty(e.GuidDsl)) validas.Add(e);
            }
            return validas;
        }

        public static EmpresaContabilidadInfo BuscarEmpresa(SqlConnection conn, string baseDatos) =>
            ListarEmpresas(conn).FirstOrDefault(e => string.Equals(e.BaseDatos, baseDatos, StringComparison.OrdinalIgnoreCase));

        public static string DescifrarClave(ContabilidadConfig cfg) =>
            cfg.ClaveCifrada == null || cfg.ClaveCifrada.Length == 0 ? "" : DpapiHelper.Descifrar(cfg.ClaveCifrada);

        // Solo inicia y cierra sesion: confirma que el usuario/clave de Contabilidad sirven y que el servicio esta arriba.
        public static async Task<string> ProbarAccesoAsync(SqlConnection conn, ContabilidadConfig cfg)
        {
            var emp = BuscarEmpresa(conn, cfg.BaseDatos);
            if (emp == null) return "No se encontró la empresa de Contabilidad «" + cfg.BaseDatos + "» en este servidor.";
            var (_, error) = await EjecutarScriptAsync(emp, cfg.Usuario, DescifrarClave(cfg), new List<(Guid, string, bool)>(), null).ConfigureAwait(false);
            return error;
        }

        // Carga al ADD todo lo descargado de esta empresa que aun no este alli. Idempotente.
        public static async Task<ResultadoContabilidad> CargarAsync(SqlConnection conn, string rfc, ContabilidadConfig cfg, Action<string> progreso = null, int maximo = 0, ICollection<Guid> soloUuids = null)
        {
            var r = new ResultadoContabilidad();
            try
            {
                var emp = BuscarEmpresa(conn, cfg.BaseDatos);
                if (emp == null) { r.Error = "No se encontró la empresa de Contabilidad «" + cfg.BaseDatos + "»."; return r; }
                if (!string.Equals(emp.Rfc, rfc, StringComparison.OrdinalIgnoreCase))
                { r.Error = "El RFC de la empresa de Contabilidad (" + emp.Rfc + ") no coincide con el de Descargas (" + rfc + ")."; return r; }

                var enAdd = UuidsEnAdd(conn, emp);
                var cfdis = BrosSatDb.ObtenerCfdiConArchivo(conn, rfc);
                var porCargar = new List<(Guid uuid, string ruta, bool emitido)>();
                var yaCargados = new List<Guid>();
                foreach (var (uuid, ruta, emitido) in cfdis)
                {
                    if (soloUuids != null && !soloUuids.Contains(uuid)) continue;
                    if (enAdd.Contains(uuid.ToString().ToUpperInvariant())) { r.YaEstaban++; yaCargados.Add(uuid); continue; }
                    if (!File.Exists(ruta)) { r.SinArchivo++; continue; }
                    porCargar.Add((uuid, ruta, emitido));
                }
                if (yaCargados.Count > 0) BrosSatDb.MarcarCargadoContabilidad(conn, rfc, yaCargados);
                if (maximo > 0 && porCargar.Count > maximo) porCargar = porCargar.Take(maximo).ToList();
                if (porCargar.Count == 0) return r;

                progreso?.Invoke("Cargando " + porCargar.Count + " XML al ADD de " + emp.Nombre + "...");
                var (_, error) = await EjecutarScriptAsync(emp, cfg.Usuario, DescifrarClave(cfg), porCargar, progreso).ConfigureAwait(false);

                // Lo que cuenta es lo que de verdad quedo en el ADD, no lo que el servicio dice.
                var despues = UuidsEnAdd(conn, emp);
                var nuevos = porCargar.Where(x => despues.Contains(x.uuid.ToString().ToUpperInvariant())).Select(x => x.uuid).ToList();
                r.Cargados = nuevos.Count;
                r.NoSeCargaron = porCargar.Count - nuevos.Count;
                if (nuevos.Count > 0) BrosSatDb.MarcarCargadoContabilidad(conn, rfc, nuevos);
                if (error != null && nuevos.Count == 0) r.Error = error;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        // Despues de cada descarga (servicio automatico y «Revisar pendientes»): si la empresa tiene Contabilidad configurada con carga
        // automatica, sube lo nuevo al ADD. Nunca lanza: un problema con Contabilidad no debe frenar la descarga del SAT.
        public static async Task CargarAutomaticoAsync(SqlConnection conn, string rfc, string nombre)
        {
            try
            {
                var cfg = BrosSatDb.ObtenerContabilidad(conn, rfc);
                if (cfg == null || !cfg.CargaAutomatica) return;
                var r = await CargarAsync(conn, rfc, cfg, null).ConfigureAwait(false);
                if (!r.Exito) { Bitacora.EscribirError("  " + nombre + ": no se pudo cargar a Contabilidad: " + r.Error); return; }
                if (r.Cargados > 0 || r.NoSeCargaron > 0)
                    Bitacora.Escribir("  " + nombre + ": " + r.Cargados + " XML cargados al ADD de Contabilidad, " + r.YaEstaban + " ya estaban" +
                        (r.NoSeCargaron > 0 ? ", " + r.NoSeCargaron + " rechazados por CONTPAQi." : "."));
            }
            catch (Exception ex) { Bitacora.EscribirError("  " + nombre + ": error al cargar a Contabilidad: " + ex.Message); }
        }

        internal static HashSet<string> UuidsEnAdd(SqlConnection conn, EmpresaContabilidadInfo emp)
        {
            var set = new HashSet<string>();
            using (var cmd = new SqlCommand("SELECT UUID FROM [document_" + emp.GuidDsl + "_metadata].dbo.Comprobante WHERE UUID IS NOT NULL", conn) { CommandTimeout = 120 })
            using (var l = cmd.ExecuteReader())
                while (l.Read()) set.Add(Convert.ToString(l.GetValue(0)).Trim().ToUpperInvariant());
            return set;
        }

        // Devuelve (lineas, error). error null = el script llego a "OK|fin".
        private static async Task<(List<string>, string)> EjecutarScriptAsync(EmpresaContabilidadInfo emp, string usuario, string clave,
            List<(Guid uuid, string ruta, bool emitido)> archivos, Action<string> progreso)
        {
            string dlls = CarpetaDlls();
            if (dlls == null) return (null, "No se encontró «ConvertidorADD» de CONTPAQi. Esta máquina necesita tener instalado CONTPAQi Contabilidad con su servidor de aplicaciones.");

            string rutaScript = ExtraerScript();
            // El servidor de CONTPAQi corre como LocalSystem: los lotes temporales van en ProgramData, no en el perfil del usuario.
            string tmp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "add_tmp");
            Directory.CreateDirectory(tmp);

            string json = JsonSerializer.Serialize(new
            {
                carpetaDlls = dlls, carpetaTemporal = tmp, usuario, clave,
                empresaNombre = emp.Nombre, empresaRuta = emp.Ruta, guidEmpresa = emp.GuidEmpresa, rfc = emp.Rfc, guidDsl = emp.GuidDsl,
                archivos = archivos.Select(a => new { uuid = a.uuid.ToString(), ruta = a.ruta, emitido = a.emitido }).ToArray()
            });

            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + rutaScript + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var lineas = new List<string>(); string error = null; bool ok = false;
            using (var p = Process.Start(psi))
            {
                await p.StandardInput.WriteAsync(json).ConfigureAwait(false);
                p.StandardInput.Close();
                var errTask = p.StandardError.ReadToEndAsync();
                string linea;
                while ((linea = await p.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    lineas.Add(linea);
                    if (linea.StartsWith("ERR|")) error = linea.Substring(4);
                    else if (linea.StartsWith("OK|")) ok = true;
                    else if (linea.StartsWith("PROG|")) { var t = linea.Split('|'); progreso?.Invoke("Cargados " + t[1] + " de " + t[2] + "..."); }
                }
                p.WaitForExit();
                string stderr = await errTask.ConfigureAwait(false);
                if (error == null && !ok) error = string.IsNullOrWhiteSpace(stderr) ? "El cargador terminó sin confirmar (código " + p.ExitCode + ")." : stderr.Trim();
            }
            return (lineas, error);
        }

        private static string ExtraerScript()
        {
            string destino = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrosLMV", "Descargas", "CargaAdd.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            var asm = typeof(ContabilidadAdd).Assembly;
            string recurso = asm.GetManifestResourceNames().First(n => n.EndsWith("CargaAdd.ps1", StringComparison.OrdinalIgnoreCase));
            using (var s = asm.GetManifestResourceStream(recurso))
            using (var f = File.Create(destino)) s.CopyTo(f);
            return destino;
        }
    }
}
