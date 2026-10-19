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

// ContaDatos.cs -- datos del Contabilizador (fase 3.1): copia del catalogo de cuentas de Contabilidad, terceros (clientes/proveedores) y
// SUS CUENTAS POR ROL Y MONEDA. Un proveedor puede tener cuenta de provision en pesos, en dolares y en euros, de anticipos, de gastos...
// Lo que se lee de Contabilidad (solo lectura) lleva Origen = 'Contabilidad'; lo que el usuario escribe es 'Manual' y NUNCA se pisa al sincronizar.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal static class ContaRoles
    {
        public const string Provision = "Provisión";                 // pasivo del proveedor
        public const string Gasto = "Gasto";
        public const string PasivoComplementaria = "Provisión complementaria";
        public const string GastoComplementaria = "Gasto complementaria";
        public const string Anticipo = "Anticipo";
        public const string Cliente = "Cliente";                     // deudor del cliente
        public const string ClienteComplementaria = "Cliente complementaria";
        public const string Ingreso = "Ingreso";
        public static readonly string[] Todos = { Provision, Gasto, PasivoComplementaria, GastoComplementaria, Anticipo, Cliente, ClienteComplementaria, Ingreso };
        public static readonly string[] Monedas = { "MXN", "USD", "EUR", "CAD", "GBP", "JPY" };
        public static bool Valido(string rol) => Todos.Contains(rol);
    }

    internal sealed class ContaCuenta
    {
        public string Codigo, Nombre, Tipo, Moneda;
        public bool Afectable, EsBaja, UsaSegmento;
        public override string ToString() => Codigo + " — " + Nombre + (Moneda != "MXN" ? "  [" + Moneda + "]" : "");
    }

    internal sealed class ContaAsignacion
    {
        public string Rol, Moneda, Cuenta, Origen;
        public string Texto => Rol + " " + Moneda + ": " + Cuenta;
    }

    internal sealed class ContaTerceroFila
    {
        public string Clave, Rfc, Nombre, Origen;
        public bool EsProveedor, EsCliente;
        public string CodigoConta;
        public int NumCfdi;
        public List<ContaAsignacion> Asignaciones = new List<ContaAsignacion>();
        public string Tipo => EsProveedor && EsCliente ? "Cliente y proveedor" : EsProveedor ? "Proveedor" : EsCliente ? "Cliente" : "—";
        public string Resumen(params string[] roles) => string.Join("   ", Asignaciones.Where(a => roles.Contains(a.Rol)).Select(a => a.Cuenta + (a.Moneda != "MXN" ? " (" + a.Moneda + ")" : "")));
        public bool TieneCuentaPrincipal => Asignaciones.Any(a => a.Rol == ContaRoles.Provision || a.Rol == ContaRoles.Cliente);
    }

    internal sealed class ContaSincronizacion
    {
        public int Cuentas, Terceros, Asignaciones, Descubiertos;
        public string Error;
        public bool Ok => string.IsNullOrEmpty(Error);
    }

    internal sealed class ContaImportFila { public string Clave, Rol, Moneda, Cuenta; public bool Borrar; public int Fila; }
    internal sealed class ContaImportResultado
    {
        public int Guardadas, Borradas;
        public List<string> Errores = new List<string>();
        public List<string> Avisos = new List<string>();
    }

    internal static class ContaDatos
    {
        private static readonly HashSet<string> RfcGenericos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "XEXX010101000", "XAXX010101000" };

        // La clave del tercero es su RFC, salvo los genericos (extranjero / publico en general), que se distinguen por nombre.
        public static string ClaveTercero(string rfc, string nombre)
        {
            rfc = (rfc ?? "").Trim().ToUpperInvariant();
            if (rfc.Length > 0 && !RfcGenericos.Contains(rfc)) return rfc;
            return (rfc.Length > 0 ? rfc : "SINRFC") + "|" + NormalizarNombre(nombre);
        }

        public static string NormalizarNombre(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "";
            var d = n.Normalize(NormalizationForm.FormD); var sb = new StringBuilder();
            foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString().ToUpperInvariant(), @"\s+", " ").Trim();
        }

        // ---------- Lectura de lo guardado ----------

        public static List<ContaCuenta> ObtenerCuentas(SqlConnection conn, string rfc, bool soloAfectables = false)
        {
            var l = new List<ContaCuenta>();
            using (var cmd = new SqlCommand("SELECT Codigo, Nombre, Tipo, Moneda, Afectable, EsBaja, UsaSegmento FROM ContaCuentaCache WHERE RFC = @r" + (soloAfectables ? " AND Afectable = 1 AND EsBaja = 0" : "") + " ORDER BY Codigo", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var d = cmd.ExecuteReader())
                    while (d.Read()) l.Add(new ContaCuenta { Codigo = d.GetString(0), Nombre = d.IsDBNull(1) ? "" : d.GetString(1), Tipo = d.IsDBNull(2) ? "" : d.GetString(2), Moneda = d.GetString(3), Afectable = d.GetBoolean(4), EsBaja = d.GetBoolean(5), UsaSegmento = d.GetBoolean(6) });
            }
            return l;
        }

        public static List<ContaTerceroFila> ObtenerTerceros(SqlConnection conn, string rfc)
        {
            var map = new Dictionary<string, ContaTerceroFila>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand("SELECT Clave, TerceroRFC, Nombre, EsProveedor, EsCliente, CodigoConta, Origen, NumCfdi FROM ContaTercero WHERE RFC = @r ORDER BY Nombre", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var d = cmd.ExecuteReader())
                    while (d.Read())
                        map[d.GetString(0)] = new ContaTerceroFila { Clave = d.GetString(0), Rfc = d.IsDBNull(1) ? "" : d.GetString(1), Nombre = d.IsDBNull(2) ? "" : d.GetString(2), EsProveedor = d.GetBoolean(3), EsCliente = d.GetBoolean(4), CodigoConta = d.IsDBNull(5) ? null : d.GetString(5), Origen = d.GetString(6), NumCfdi = d.GetInt32(7) };
            }
            using (var cmd = new SqlCommand("SELECT Clave, Rol, Moneda, Cuenta, Origen FROM ContaTerceroCuenta WHERE RFC = @r ORDER BY Rol, Moneda", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var d = cmd.ExecuteReader())
                    while (d.Read())
                        if (map.TryGetValue(d.GetString(0), out var t)) t.Asignaciones.Add(new ContaAsignacion { Rol = d.GetString(1), Moneda = d.GetString(2), Cuenta = d.GetString(3), Origen = d.GetString(4) });
            }
            return map.Values.OrderBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static DateTime? UltimaSincronizacion(SqlConnection conn, string rfc)
        {
            using (var cmd = new SqlCommand("SELECT MAX(FechaLectura) FROM ContaCuentaCache WHERE RFC = @r", conn)) { cmd.Parameters.AddWithValue("@r", rfc); var v = cmd.ExecuteScalar(); return v == null || v == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(v); }
        }

        // ---------- Edicion ----------

        // Valida una asignacion contra el catalogo guardado. Error = no se puede guardar; aviso = se guarda pero conviene revisar.
        public static (string error, string aviso) Validar(SqlConnection conn, string rfc, string rol, string moneda, string cuenta)
        {
            if (!ContaRoles.Valido(rol)) return ("Rol desconocido: «" + rol + "».", null);
            if (string.IsNullOrWhiteSpace(moneda)) return ("Falta la moneda.", null);
            if (string.IsNullOrWhiteSpace(cuenta)) return ("Falta la cuenta.", null);
            using (var cmd = new SqlCommand("SELECT Moneda, Afectable, EsBaja FROM ContaCuentaCache WHERE RFC = @r AND Codigo = @c", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@c", cuenta.Trim());
                using (var d = cmd.ExecuteReader())
                {
                    if (!d.Read()) return ("La cuenta " + cuenta.Trim() + " no existe en el catálogo de Contabilidad (sincroniza si es nueva).", null);
                    string mon = d.GetString(0);
                    if (d.GetBoolean(2)) return ("La cuenta " + cuenta.Trim() + " está dada de baja.", null);
                    if (!d.GetBoolean(1)) return ("La cuenta " + cuenta.Trim() + " no es afectable (es de mayor o agrupadora).", null);
                    if (!string.Equals(mon, moneda.Trim(), StringComparison.OrdinalIgnoreCase)) return (null, "La cuenta es de moneda " + mon + " y la asignación dice " + moneda.Trim().ToUpperInvariant() + ".");
                }
            }
            return (null, null);
        }

        public static void GuardarAsignacion(SqlConnection conn, string rfc, string clave, string rol, string moneda, string cuenta, string origen = "Manual")
        {
            using (var cmd = new SqlCommand(@"
MERGE ContaTerceroCuenta AS t USING (SELECT @r AS RFC, @k AS Clave, @ro AS Rol, @m AS Moneda) AS s ON t.RFC = s.RFC AND t.Clave = s.Clave AND t.Rol = s.Rol AND t.Moneda = s.Moneda
WHEN MATCHED THEN UPDATE SET Cuenta = @c, Origen = @o, FechaCambio = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (RFC, Clave, Rol, Moneda, Cuenta, Origen) VALUES (@r, @k, @ro, @m, @c, @o);", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@k", clave); cmd.Parameters.AddWithValue("@ro", rol);
                cmd.Parameters.AddWithValue("@m", moneda.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("@c", cuenta.Trim()); cmd.Parameters.AddWithValue("@o", origen);
                cmd.ExecuteNonQuery();
            }
        }

        public static void BorrarAsignacion(SqlConnection conn, string rfc, string clave, string rol, string moneda)
        {
            using (var cmd = new SqlCommand("DELETE FROM ContaTerceroCuenta WHERE RFC = @r AND Clave = @k AND Rol = @ro AND Moneda = @m", conn))
            { cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@k", clave); cmd.Parameters.AddWithValue("@ro", rol); cmd.Parameters.AddWithValue("@m", moneda.Trim().ToUpperInvariant()); cmd.ExecuteNonQuery(); }
        }

        // Aplica filas venidas de Excel. Antes valida todo; con soloValidar = true no escribe nada. Las filas con error se omiten y se reportan.
        public static ContaImportResultado AplicarImportacion(SqlConnection conn, string rfc, IEnumerable<ContaImportFila> filas, bool soloValidar)
        {
            var res = new ContaImportResultado();
            var claves = new HashSet<string>(ObtenerTerceros(conn, rfc).Select(t => t.Clave), StringComparer.OrdinalIgnoreCase);
            foreach (var f in filas)
            {
                string donde = "Fila " + f.Fila + ": ";
                if (string.IsNullOrWhiteSpace(f.Clave) || !claves.Contains(f.Clave.Trim())) { res.Errores.Add(donde + "el tercero «" + f.Clave + "» no existe (no cambies la columna Clave)."); continue; }
                if (f.Borrar)
                {
                    if (!soloValidar) BorrarAsignacion(conn, rfc, f.Clave.Trim(), f.Rol, f.Moneda ?? "MXN");
                    res.Borradas++; continue;
                }
                var (err, aviso) = Validar(conn, rfc, f.Rol, f.Moneda, f.Cuenta);
                if (err != null) { res.Errores.Add(donde + err); continue; }
                if (aviso != null) res.Avisos.Add(donde + aviso);
                if (!soloValidar) GuardarAsignacion(conn, rfc, f.Clave.Trim(), f.Rol, f.Moneda, f.Cuenta);
                res.Guardadas++;
            }
            return res;
        }

        // ---------- Sincronizacion con Contabilidad (solo lectura) ----------

        // baseDatos = ctXXX (misma instancia de SQL). Reemplaza la copia del catalogo y lo que vino de Contabilidad; conserva lo Manual.
        public static ContaSincronizacion Sincronizar(SqlConnection conn, string rfc, string baseDatos, Action<string> progreso = null)
        {
            var r = new ContaSincronizacion();
            try
            {
                if (!Regex.IsMatch(baseDatos ?? "", "^[A-Za-z0-9_]+$")) { r.Error = "Nombre de base inválido."; return r; }
                string ct = "[" + baseDatos + "].dbo.";

                progreso?.Invoke("Leyendo el catálogo de cuentas...");
                var cuentas = new List<ContaCuenta>();
                using (var cmd = new SqlCommand("SELECT c.Codigo, c.Nombre, c.Tipo, ISNULL(m.CodigoSAT, 'MXN'), c.Afectable, c.EsBaja, c.SegNegMovtos FROM " + ct + "Cuentas c LEFT JOIN " + ct + "Monedas m ON m.Id = c.IdMoneda", conn) { CommandTimeout = 120 })
                using (var d = cmd.ExecuteReader())
                    while (d.Read()) cuentas.Add(new ContaCuenta { Codigo = Convert.ToString(d.GetValue(0)).Trim(), Nombre = d.IsDBNull(1) ? "" : Convert.ToString(d.GetValue(1)).Trim(), Tipo = d.IsDBNull(2) ? "" : Convert.ToString(d.GetValue(2)).Trim(), Moneda = Convert.ToString(d.GetValue(3)).Trim().ToUpperInvariant(), Afectable = Convert.ToInt32(d.GetValue(4)) == 1, EsBaja = Convert.ToBoolean(d.GetValue(5)), UsaSegmento = Convert.ToBoolean(d.GetValue(6)) });
                var moneda = cuentas.GroupBy(c => c.Codigo).ToDictionary(g => g.Key, g => g.First().Moneda);

                progreso?.Invoke("Leyendo clientes y proveedores...");
                var terceros = new Dictionary<string, ContaTerceroFila>(StringComparer.OrdinalIgnoreCase);
                void Asigna(ContaTerceroFila t, string rol, string cuenta)
                {
                    cuenta = (cuenta ?? "").Trim();
                    if (cuenta.Length == 0) return;
                    string mon = moneda.TryGetValue(cuenta, out var mm) ? mm : "MXN";
                    if (!t.Asignaciones.Any(a => a.Rol == rol && a.Moneda == mon)) t.Asignaciones.Add(new ContaAsignacion { Rol = rol, Moneda = mon, Cuenta = cuenta, Origen = "Contabilidad" });
                }
                ContaTerceroFila Tercero(string rfcT, string nombre, string codigo)
                {
                    string clave = ClaveTercero(rfcT, nombre);
                    if (!terceros.TryGetValue(clave, out var t)) terceros[clave] = t = new ContaTerceroFila { Clave = clave, Rfc = (rfcT ?? "").Trim().ToUpperInvariant(), Nombre = (nombre ?? "").Trim(), Origen = "Contabilidad", CodigoConta = (codigo ?? "").Trim() };
                    return t;
                }

                using (var cmd = new SqlCommand(@"
SELECT ISNULL(NULLIF(LTRIM(RTRIM(p.RFC)), ''), pr.RFC), ISNULL(NULLIF(LTRIM(RTRIM(p.Nombre)), ''), pr.Nombre), pr.Codigo, pr.CodigoCuenta, pr.CodigoCtaGastos, pr.CodigoCtaComplementaria, pr.CodigoCtaGastosComplementaria
FROM " + ct + "Proveedores pr LEFT JOIN " + ct + "Personas p ON p.Codigo = pr.CodigoPersona", conn) { CommandTimeout = 120 })
                using (var d = cmd.ExecuteReader())
                    while (d.Read())
                    {
                        var t = Tercero(d.IsDBNull(0) ? "" : Convert.ToString(d.GetValue(0)), d.IsDBNull(1) ? "" : Convert.ToString(d.GetValue(1)), Convert.ToString(d.GetValue(2)));
                        t.EsProveedor = true;
                        Asigna(t, ContaRoles.Provision, d.IsDBNull(3) ? "" : Convert.ToString(d.GetValue(3)));
                        Asigna(t, ContaRoles.Gasto, d.IsDBNull(4) ? "" : Convert.ToString(d.GetValue(4)));
                        Asigna(t, ContaRoles.PasivoComplementaria, d.IsDBNull(5) ? "" : Convert.ToString(d.GetValue(5)));
                        Asigna(t, ContaRoles.GastoComplementaria, d.IsDBNull(6) ? "" : Convert.ToString(d.GetValue(6)));
                    }
                using (var cmd = new SqlCommand(@"
SELECT p.RFC, p.Nombre, cl.Codigo, cl.CodigoCuenta, cl.CodigoCtaComplementaria
FROM " + ct + "Clientes cl JOIN " + ct + "Personas p ON p.Codigo = cl.CodigoPersona", conn) { CommandTimeout = 120 })
                using (var d = cmd.ExecuteReader())
                    while (d.Read())
                    {
                        var t = Tercero(d.IsDBNull(0) ? "" : Convert.ToString(d.GetValue(0)), d.IsDBNull(1) ? "" : Convert.ToString(d.GetValue(1)), Convert.ToString(d.GetValue(2)));
                        t.EsCliente = true;
                        Asigna(t, ContaRoles.Cliente, d.IsDBNull(3) ? "" : Convert.ToString(d.GetValue(3)));
                        Asigna(t, ContaRoles.ClienteComplementaria, d.IsDBNull(4) ? "" : Convert.ToString(d.GetValue(4)));
                    }

                // Contrapartes de los CFDI que Contabilidad aun no conoce (para poder asignarles cuenta o crearlas).
                progreso?.Invoke("Buscando terceros en tus CFDI...");
                var cfdi = new Dictionary<string, (string rfc, string nombre, bool prov, bool cli, int n)>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqlCommand(@"
SELECT x.RfcT, x.Nombre, x.Emitido, COUNT(*) FROM (
  SELECT CASE WHEN c.RFCEmisor = @r THEN c.RFCReceptor ELSE c.RFCEmisor END AS RfcT,
         CASE WHEN c.RFCEmisor = @r THEN ISNULL(m.NombreReceptor, '') ELSE ISNULL(c.NombreEmisor, '') END AS Nombre,
         CASE WHEN c.RFCEmisor = @r THEN 1 ELSE 0 END AS Emitido
  FROM CfdiRecibido c LEFT JOIN CfdiMetadata m ON m.UUID = c.UUID
  WHERE (c.RFCEmisor = @r OR c.RFCReceptor = @r) AND ISNULL(c.TipoComprobante, '') <> 'N') x
GROUP BY x.RfcT, x.Nombre, x.Emitido", conn) { CommandTimeout = 120 })
                {
                    cmd.Parameters.AddWithValue("@r", rfc);
                    using (var d = cmd.ExecuteReader())
                        while (d.Read())
                        {
                            string rfcT = d.IsDBNull(0) ? "" : d.GetString(0), nom = d.IsDBNull(1) ? "" : d.GetString(1); bool esEmitido = Convert.ToInt32(d.GetValue(2)) == 1; int n = d.GetInt32(3);
                            string clave = ClaveTercero(rfcT, nom);
                            cfdi.TryGetValue(clave, out var x);
                            cfdi[clave] = (rfcT, string.IsNullOrEmpty(x.nombre) ? nom : x.nombre, x.prov || !esEmitido, x.cli || esEmitido, x.n + n);
                        }
                }
                // Terceros de Contabilidad sin RFC: si el nombre coincide con el de una contraparte de tus CFDI, se les pone ese RFC (y no se duplican).
                var sinRfcPorNombre = terceros.Values.Where(x => x.Rfc.Length == 0 && x.Nombre.Length > 0).GroupBy(x => NormalizarNombre(x.Nombre)).ToDictionary(g => g.Key, g => g.First());
                foreach (var kv in cfdi)
                {
                    if (!terceros.TryGetValue(kv.Key, out var t))
                    {
                        string nn = NormalizarNombre(kv.Value.nombre);
                        if (!RfcGenericos.Contains(kv.Value.rfc) && nn.Length > 0 && sinRfcPorNombre.TryGetValue(nn, out var nativo)) { nativo.Rfc = kv.Value.rfc.ToUpperInvariant(); t = nativo; }
                        else { terceros[kv.Key] = t = new ContaTerceroFila { Clave = kv.Key, Rfc = kv.Value.rfc.ToUpperInvariant(), Nombre = kv.Value.nombre, Origen = "CFDI" }; r.Descubiertos++; }
                    }
                    t.NumCfdi += kv.Value.n;
                    if (t.Origen == "CFDI") { t.EsProveedor |= kv.Value.prov; t.EsCliente |= kv.Value.cli; }
                }

                progreso?.Invoke("Guardando...");
                using (var tx = conn.BeginTransaction())
                {
                    void Sql(string sql, Action<SqlCommand> ps = null) { using (var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 300 }) { cmd.Parameters.AddWithValue("@r", rfc); ps?.Invoke(cmd); cmd.ExecuteNonQuery(); } }
                    Sql("DELETE FROM ContaCuentaCache WHERE RFC = @r");
                    var tc = new DataTable(); foreach (var (n, t) in new[] { ("RFC", typeof(string)), ("Codigo", typeof(string)), ("Nombre", typeof(string)), ("Tipo", typeof(string)), ("Moneda", typeof(string)), ("Afectable", typeof(bool)), ("EsBaja", typeof(bool)), ("UsaSegmento", typeof(bool)), ("FechaLectura", typeof(DateTime)) }) tc.Columns.Add(n, t);
                    var ahora = DateTime.UtcNow;
                    foreach (var c in cuentas.GroupBy(c => c.Codigo).Select(g => g.First())) tc.Rows.Add(rfc, c.Codigo, c.Nombre, c.Tipo, c.Moneda, c.Afectable, c.EsBaja, c.UsaSegmento, ahora);
                    using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "ContaCuentaCache", BulkCopyTimeout = 300 }) { foreach (DataColumn c in tc.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName); bulk.WriteToServer(tc); }

                    Sql("DELETE FROM ContaTerceroCuenta WHERE RFC = @r AND Origen = 'Contabilidad'");
                    Sql("DELETE FROM ContaTercero WHERE RFC = @r AND Origen IN ('Contabilidad', 'CFDI')");
                    var tt = new DataTable(); foreach (var (n, t) in new[] { ("RFC", typeof(string)), ("Clave", typeof(string)), ("TerceroRFC", typeof(string)), ("Nombre", typeof(string)), ("EsProveedor", typeof(bool)), ("EsCliente", typeof(bool)), ("CodigoConta", typeof(string)), ("Origen", typeof(string)), ("NumCfdi", typeof(int)) }) tt.Columns.Add(n, t);
                    var ta = new DataTable(); foreach (var (n, t) in new[] { ("RFC", typeof(string)), ("Clave", typeof(string)), ("Rol", typeof(string)), ("Moneda", typeof(string)), ("Cuenta", typeof(string)), ("Origen", typeof(string)) }) ta.Columns.Add(n, t);
                    var yaManual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SqlCommand("SELECT Clave + '|' + Rol + '|' + Moneda FROM ContaTerceroCuenta WHERE RFC = @r", conn, tx)) { cmd.Parameters.AddWithValue("@r", rfc); using (var d = cmd.ExecuteReader()) while (d.Read()) yaManual.Add(d.GetString(0)); }
                    foreach (var t in terceros.Values)
                    {
                        tt.Rows.Add(rfc, t.Clave, t.Rfc, Recortar(t.Nombre, 300), t.EsProveedor, t.EsCliente, (object)t.CodigoConta ?? DBNull.Value, t.Origen, t.NumCfdi);
                        foreach (var a in t.Asignaciones)
                        {
                            if (yaManual.Contains(t.Clave + "|" + a.Rol + "|" + a.Moneda)) continue;     // lo que escribio el usuario no se pisa
                            ta.Rows.Add(rfc, t.Clave, a.Rol, a.Moneda, a.Cuenta, "Contabilidad"); r.Asignaciones++;
                        }
                    }
                    // Los terceros Manual/ya existentes (con asignaciones manuales) no deben quedar duplicados.
                    var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SqlCommand("SELECT Clave FROM ContaTercero WHERE RFC = @r", conn, tx)) { cmd.Parameters.AddWithValue("@r", rfc); using (var d = cmd.ExecuteReader()) while (d.Read()) existentes.Add(d.GetString(0)); }
                    for (int i = tt.Rows.Count - 1; i >= 0; i--) if (existentes.Contains((string)tt.Rows[i]["Clave"])) tt.Rows.RemoveAt(i);
                    r.Terceros = terceros.Count;
                    using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "ContaTercero", BulkCopyTimeout = 300 }) { foreach (DataColumn c in tt.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName); bulk.WriteToServer(tt); }
                    using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "ContaTerceroCuenta", BulkCopyTimeout = 300 }) { foreach (DataColumn c in ta.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName); bulk.WriteToServer(ta); }
                    tx.Commit();
                }
                r.Cuentas = cuentas.Count;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        private static string Recortar(string s, int max) => s == null ? null : s.Length <= max ? s : s.Substring(0, max);
    }
}
