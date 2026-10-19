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

// MapaCfdi.cs -- «donde esta cada CFDI»: SAT, XML en disco, Comercial Pro, ADD de Contabilidad y polizas.
//
// Idea central: las banderas «sincronizado» solo dicen que BrosLMV lo mando alguna vez, no que siga ahi. Aqui la verdad se LEE de cada
// sistema (solo lectura, en lote) y se guarda en CfdiEstadoSistema / CfdiPoliza / CfdiDocumentoComercial; la pantalla solo consulta eso.
// Cada verificador es independiente: si uno falla (servidor apagado, sistema sin configurar) los demas siguen y el motivo queda en
// VerificacionSistema. Ninguno escribe en Comercial ni en Contabilidad.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BrosLMV.Descargas.Cola;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal static class SistemaCfdi
    {
        public const string Disco = "Disco";
        public const string Comercial = "Comercial";
        public const string Add = "ADD";
        public const string Polizas = "Polizas";
        public const string Premium = "Premium";
        public static readonly string[] Todos = { Disco, Comercial, Premium, Add, Polizas };
        public static string Nombre(string s) =>
            s == Disco ? "XML en disco" : s == Comercial ? "Comercial Pro" : s == Premium ? "Comercial Premium" : s == Add ? "ADD de Contabilidad" : s == Polizas ? "Pólizas de Contabilidad" : s;
    }

    internal sealed class EstadoCfdi
    {
        public Guid Uuid;
        public string Estado;
        public string Detalle;
    }

    internal sealed class ResultadoVerificacion
    {
        public string Sistema;
        public string Estado;          // Ok / Error / No configurado
        public int Total;              // CFDI de esta empresa que se encontraron en el sistema
        public string Mensaje;
        public DateTime? Fecha;
        public bool Ok => Estado == "Ok";
        public bool Configurado => Estado != "No configurado";
    }

    // PROPIEDADES (no campos): WPF solo resuelve propiedades en las columnas.
    internal sealed class MapaFila
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Tipo { get; set; }
        public int SatTotal { get; set; }
        public int SatVigentes { get; set; }
        public int Descargados { get; set; }
        public int EnDisco { get; set; }
        public int EnComercial { get; set; }
        public int ConDocumento { get; set; }
        public int EnPremium { get; set; }        // CFDI que Premium timbro (emitidos) o cargo a su ADD (recibidos)
        public int EnAdd { get; set; }
        public int ConPoliza { get; set; }
        public int SoloSistemas { get; set; }     // estan en el ADD pero NO en Descargas (faltan por bajar del SAT)
        public int FaltaDisco { get; set; }       // vigentes descargados cuyo archivo ya no esta
        public int FaltaComercial { get; set; }   // vigentes que no estan en la lista de Comercial
        public int FaltaAdd { get; set; }         // vigentes que no estan en el ADD
        public int SinPoliza { get; set; }        // vigentes que no estan en ninguna poliza
        public int TotalDistinto { get; set; }    // CFDI cuyo documento (Comercial/Premium) tiene otro total
        public string MesTexto => new DateTime(Anio, Mes, 1).ToString("yyyy-MM");
    }

    internal sealed class MapaCfdiFila
    {
        public int CfdiId { get; set; }
        public bool Externo => CfdiId == 0;       // no esta en Descargas
        public Guid Uuid { get; set; }
        public DateTime Fecha { get; set; }
        public string Direccion { get; set; }     // Emitido / Recibido
        public string TipoComprobante { get; set; }
        public string Contraparte { get; set; }
        public string SerieFolio { get; set; }
        public decimal Total { get; set; }
        public string Estatus { get; set; }
        public string Disco { get; set; }
        public string Comercial { get; set; }
        public string Premium { get; set; }
        public string Add { get; set; }
        public int Polizas { get; set; }
        public string UuidTexto => Uuid.ToString().ToUpperInvariant();
        public string FechaTexto => Fecha.ToString("yyyy-MM-dd");
    }

    internal sealed class PolizaCfdi
    {
        public string Origen { get; set; }
        public string Nivel { get; set; }
        public string TipoNombre { get; set; }
        public string Folio { get; set; }
        public DateTime? Fecha { get; set; }
        public int? Ejercicio { get; set; }
        public int? Periodo { get; set; }
        public string Concepto { get; set; }
        public decimal Cargos { get; set; }
        public decimal Abonos { get; set; }
        public string Rol { get; set; }
        public string Referencia => string.IsNullOrEmpty(TipoNombre) ? (Origen ?? "") : TipoNombre + " " + Folio + (Ejercicio.HasValue ? " · " + Ejercicio + "-" + (Periodo ?? 0).ToString("00") : "");
        public string FechaTexto => Fecha.HasValue ? Fecha.Value.ToString("yyyy-MM-dd") : "";
    }

    internal static class Diferencias
    {
        public const decimal Tolerancia = 0.01m;
        // CFDI de Descargas (ingreso/egreso en pesos) cuyos documentos vigentes en Comercial/Premium suman un total distinto al del CFDI.
        // Los pagos (total 0) y las monedas extranjeras no se comparan: el total del documento no es comparable sin tipo de cambio.
        public const string Condicion = "td.UUID IS NOT NULL";
        // LEFT JOIN a agregar a las consultas del universo (alias c = UUID del universo): trae una fila por CFDI con total distinto.
        public const string Union = @"
LEFT JOIN (SELECT k.UUID FROM CfdiRecibido k
           JOIN (SELECT d.UUID, d.Sistema, SUM(d.Total) AS T FROM CfdiDocumentoComercial d WHERE d.RFC = @r AND d.Cancelado = 0 GROUP BY d.UUID, d.Sistema) s ON s.UUID = k.UUID
           WHERE k.TipoComprobante IN ('I', 'E') AND k.Total > 0 AND ISNULL(k.Moneda, 'MXN') IN ('MXN', 'XXX') AND ABS(s.T - k.Total) > 0.01
           GROUP BY k.UUID) td ON td.UUID = c.UUID";
    }

    internal sealed class DocumentoComercialCfdi
    {
        public decimal? TotalCfdi { get; set; }      // total del CFDI (null = no comparable: pago, moneda extranjera o sin descargar)
        public decimal? Diferencia => TotalCfdi.HasValue && !Cancelado && Math.Abs(Total - TotalCfdi.Value) > Diferencias.Tolerancia ? Total - TotalCfdi.Value : (decimal?)null;
        public string TotalCfdiTexto => TotalCfdi.HasValue ? TotalCfdi.Value.ToString("N2") : "";
        public string DiferenciaTexto => Diferencia.HasValue ? Diferencia.Value.ToString("+#,##0.00;-#,##0.00") : "";
        public string Sistema { get; set; } = "Comercial";     // Comercial / Premium
        public long DocumentId { get; set; }
        public int? ModuleId { get; set; }
        public string Folio { get; set; }
        public DateTime? Fecha { get; set; }
        public decimal Total { get; set; }
        public bool Cancelado { get; set; }
        public string FechaTexto => Fecha.HasValue ? Fecha.Value.ToString("yyyy-MM-dd") : "";
        public string EstadoTexto => Cancelado ? "Cancelado" : "Vigente";
    }

    internal static class MapaCfdi
    {
        // ===================================================================================
        //  Verificacion (lee cada sistema y guarda el resultado)
        // ===================================================================================

        // Verifica todos los sistemas aplicables a la empresa. «progreso» recibe un texto corto por etapa.
        public static async Task<List<ResultadoVerificacion>> VerificarTodoAsync(SqlConnection conn, EmpresaFila empresa, Action<string> progreso = null)
        {
            var res = new List<ResultadoVerificacion>();
            var uuids = UuidsDeEmpresa(conn, empresa.RFC);
            var perfil = BrosSatDb.ObtenerPerfil(conn, empresa.RFC);

            progreso?.Invoke("Revisando los XML en disco...");
            res.Add(await Ejecutar(conn, empresa.RFC, SistemaCfdi.Disco, () => VerificarDisco(conn, empresa.RFC)));

            progreso?.Invoke("Revisando Comercial...");
            res.Add(await Ejecutar(conn, empresa.RFC, SistemaCfdi.Comercial, () => perfil.ComercialPro ? VerificarComercial(conn, empresa, uuids) : null));

            progreso?.Invoke("Revisando Comercial Premium...");
            res.Add(await Ejecutar(conn, empresa.RFC, SistemaCfdi.Premium, () => perfil.ComercialPremium ? VerificarPremium(conn, empresa.RFC, BrosSatDb.ObtenerPremium(conn, empresa.RFC), uuids) : null));

            var conta = perfil.Contabilidad ? BrosSatDb.ObtenerContabilidad(conn, empresa.RFC) : null;
            progreso?.Invoke("Revisando el ADD de Contabilidad...");
            res.Add(await Ejecutar(conn, empresa.RFC, SistemaCfdi.Add, () => VerificarAdd(conn, empresa.RFC, conta, uuids)));

            progreso?.Invoke("Revisando las pólizas de Contabilidad...");
            res.Add(await Ejecutar(conn, empresa.RFC, SistemaCfdi.Polizas, () => VerificarPolizas(conn, empresa.RFC, conta, uuids)));
            return res;
        }

        // Verifica un solo sistema (despues de cargar al ADD o importar a Comercial).
        public static async Task<ResultadoVerificacion> VerificarUnoAsync(SqlConnection conn, EmpresaFila empresa, string sistema)
        {
            var uuids = UuidsDeEmpresa(conn, empresa.RFC);
            var conta = BrosSatDb.ObtenerContabilidad(conn, empresa.RFC);
            switch (sistema)
            {
                case SistemaCfdi.Disco: return await Ejecutar(conn, empresa.RFC, sistema, () => VerificarDisco(conn, empresa.RFC));
                case SistemaCfdi.Comercial: return await Ejecutar(conn, empresa.RFC, sistema, () => VerificarComercial(conn, empresa, uuids));
                case SistemaCfdi.Premium: return await Ejecutar(conn, empresa.RFC, sistema, () => BrosSatDb.ObtenerPerfil(conn, empresa.RFC).ComercialPremium ? VerificarPremium(conn, empresa.RFC, BrosSatDb.ObtenerPremium(conn, empresa.RFC), uuids) : null);
                case SistemaCfdi.Add: return await Ejecutar(conn, empresa.RFC, sistema, () => VerificarAdd(conn, empresa.RFC, conta, uuids));
                case SistemaCfdi.Polizas: return await Ejecutar(conn, empresa.RFC, sistema, () => VerificarPolizas(conn, empresa.RFC, conta, uuids));
                default: throw new ArgumentException("Sistema desconocido: " + sistema);
            }
        }

        // Cada verificador devuelve (null = «no configurado») o la lista de estados encontrados. Cualquier excepcion queda como Error.
        private static Task<ResultadoVerificacion> Ejecutar(SqlConnection conn, string rfc, string sistema, Func<List<EstadoCfdi>> verificador)
        {
            var r = new ResultadoVerificacion { Sistema = sistema, Fecha = DateTime.Now };
            try
            {
                var estados = verificador();
                if (estados == null) { r.Estado = "No configurado"; GuardarVerificacion(conn, rfc, r); return Task.FromResult(r); }
                GuardarEstados(conn, rfc, sistema, estados);
                r.Estado = "Ok"; r.Total = estados.Count;
            }
            catch (Exception ex) { r.Estado = "Error"; r.Mensaje = ex.Message; }
            GuardarVerificacion(conn, rfc, r);
            return Task.FromResult(r);
        }

        private static HashSet<Guid> UuidsDeEmpresa(SqlConnection conn, string rfc)
        {
            var set = new HashSet<Guid>();
            using (var cmd = new SqlCommand("SELECT UUID FROM CfdiRecibido WHERE RFCEmisor = @r OR RFCReceptor = @r", conn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var l = cmd.ExecuteReader()) while (l.Read()) set.Add(l.GetGuid(0));
            }
            return set;
        }

        private static List<EstadoCfdi> VerificarDisco(SqlConnection conn, string rfc)
        {
            var lista = new List<EstadoCfdi>();
            using (var cmd = new SqlCommand("SELECT UUID, RutaArchivoXml FROM CfdiRecibido WHERE (RFCEmisor = @r OR RFCReceptor = @r) AND RutaArchivoXml IS NOT NULL", conn) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                    {
                        string ruta = l.GetString(1);
                        bool existe; try { existe = File.Exists(ruta); } catch { existe = false; }
                        lista.Add(new EstadoCfdi { Uuid = l.GetGuid(0), Estado = existe ? "Con archivo" : "Sin archivo", Detalle = existe ? null : ruta });
                    }
            }
            return lista;
        }

        // Comercial Pro: lista de XML (docDocumentCFDiSAT) y documentos generados (docDocument). Null = la empresa no tiene Comercial configurado.
        private static List<EstadoCfdi> VerificarComercial(SqlConnection conn, EmpresaFila empresa, HashSet<Guid> uuids)
        {
            if (string.IsNullOrWhiteSpace(empresa.ComercialConexionSql)) return null;
            var estados = new List<EstadoCfdi>();
            var docs = new List<(Guid uuid, long id, int? modulo, string folio, DateTime? fecha, decimal total, bool cancelado)>();
            using (var comercial = new SqlConnection(DpapiHelper.DescifrarConexionSql(empresa.ComercialConexionSql)))
            {
                comercial.Open();
                using (var cmd = new SqlCommand(@"
SELECT s.UUID, s.DocumentID, s.Status, d.ModuleID, d.FolioPrefix, d.Folio, d.DateDocument, d.Total,
       CASE WHEN d.DocumentID IS NULL THEN NULL WHEN d.CancelledOn IS NULL THEN 0 ELSE 1 END
FROM docDocumentCFDiSAT s LEFT JOIN docDocument d ON d.DocumentID = s.DocumentID AND s.DocumentID <> 0", comercial) { CommandTimeout = 180 })
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                    {
                        if (l.IsDBNull(0) || !Guid.TryParse(Convert.ToString(l.GetValue(0)), out var uuid) || !uuids.Contains(uuid)) continue;
                        long docId = l.IsDBNull(1) ? 0 : Convert.ToInt64(l.GetValue(1));
                        string status = l.IsDBNull(2) ? "" : Convert.ToString(l.GetValue(2));
                        bool conDoc = docId != 0 && !l.IsDBNull(3);
                        string detalle = status == "Cancelado" ? "Cancelado en Comercial" : null;
                        if (conDoc)
                        {
                            string folio = (l.IsDBNull(4) ? "" : Convert.ToString(l.GetValue(4))) + (l.IsDBNull(5) ? "" : Convert.ToString(l.GetValue(5)));
                            docs.Add((uuid, docId, l.IsDBNull(3) ? (int?)null : Convert.ToInt32(l.GetValue(3)), folio,
                                l.IsDBNull(6) ? (DateTime?)null : Convert.ToDateTime(l.GetValue(6)), l.IsDBNull(7) ? 0m : Convert.ToDecimal(l.GetValue(7)),
                                !l.IsDBNull(8) && Convert.ToBoolean(l.GetValue(8))));
                            detalle = "Documento " + folio;
                        }
                        estados.Add(new EstadoCfdi { Uuid = uuid, Estado = conDoc ? "Con documento" : "En lista", Detalle = detalle });
                    }
            }
            GuardarDocumentosComercial(conn, empresa.RFC, "Comercial", docs);
            return estados;
        }

        // Comercial Premium: CFDI que timbro o cargo a su ADD (admFoliosDigitales) con su documento. Null = sin base de Premium configurada.
        private static List<EstadoCfdi> VerificarPremium(SqlConnection conn, string rfc, PremiumConfig cfg, HashSet<Guid> uuids)
        {
            if (cfg == null) return null;
            if (!Regex.IsMatch(cfg.BaseDatos ?? "", "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Nombre de base inválido.");
            string ad = "[" + cfg.BaseDatos + "].dbo.";
            var estados = new List<EstadoCfdi>();
            var docs = new List<(Guid uuid, long id, int? modulo, string folio, DateTime? fecha, decimal total, bool cancelado)>();
            using (var cmd = new SqlCommand(@"
SELECT f.CUUID, f.CESTADO, f.CIDDOCTO, d.CIDDOCUMENTODE, m.CDESCRIPCION, d.CSERIEDOCUMENTO, d.CFOLIO, d.CFECHA, d.CTOTAL, d.CCANCELADO, f.CFECHACANC
FROM " + ad + "admFoliosDigitales f LEFT JOIN " + ad + "admDocumentos d ON d.CIDDOCUMENTO = f.CIDDOCTO LEFT JOIN " + ad + "admDocumentosModelo m ON m.CIDDOCUMENTODE = d.CIDDOCUMENTODE WHERE f.CUUID <> ''", conn) { CommandTimeout = 300 })
            using (var l = cmd.ExecuteReader())
                while (l.Read())
                {
                    if (!Guid.TryParse(Convert.ToString(l.GetValue(0)), out var uuid) || !uuids.Contains(uuid)) continue;
                    string estado = ComercialPremium.EstadoTexto(l.IsDBNull(1) ? 0 : Convert.ToInt32(l.GetValue(1)), true);
                    string detalle = null;
                    if (!l.IsDBNull(3))
                    {
                        string tipo = l.IsDBNull(4) ? "Documento" : Convert.ToString(l.GetValue(4)).Trim();
                        string folio = tipo + " " + (l.IsDBNull(5) ? "" : Convert.ToString(l.GetValue(5)).Trim()) + (l.IsDBNull(6) ? "" : Convert.ToInt64(Convert.ToDouble(l.GetValue(6))).ToString());
                        bool cancelado = estado == "Cancelado" || (!l.IsDBNull(9) && Convert.ToInt32(l.GetValue(9)) != 0);
                        docs.Add((uuid, l.IsDBNull(2) ? 0 : Convert.ToInt64(l.GetValue(2)), Convert.ToInt32(l.GetValue(3)), folio, l.IsDBNull(7) ? (DateTime?)null : Convert.ToDateTime(l.GetValue(7)),
                            l.IsDBNull(8) ? 0m : Convert.ToDecimal(l.GetValue(8)), cancelado));
                        detalle = folio;
                    }
                    estados.Add(new EstadoCfdi { Uuid = uuid, Estado = estado, Detalle = detalle });
                }
            GuardarDocumentosComercial(conn, rfc, "Premium", docs);

            // Cargado al ADD de Premium (p. ej. por BrosLMV) pero Premium aun no lo registra en admFoliosDigitales.
            var yaEstan = new HashSet<Guid>(estados.Select(x => x.Uuid));
            var emp = ComercialPremium.BuscarEmpresaAdd(conn, cfg.BaseDatos);
            if (emp != null && !string.IsNullOrEmpty(emp.GuidDsl))
                foreach (var s in ContabilidadAdd.UuidsEnAdd(conn, emp))
                    if (Guid.TryParse(s, out var u) && uuids.Contains(u) && !yaEstan.Contains(u))
                        estados.Add(new EstadoCfdi { Uuid = u, Estado = "En el ADD (sin documento)" });
            return estados;
        }

        // ADD de Contabilidad: UUID presentes en Comprobante. Null = sin Contabilidad configurada.
        private static List<EstadoCfdi> VerificarAdd(SqlConnection conn, string rfc, ContabilidadConfig conta, HashSet<Guid> uuids)
        {
            if (conta == null) return null;
            var emp = ContabilidadAdd.BuscarEmpresa(conn, conta.BaseDatos)
                ?? throw new InvalidOperationException("No se encontró la empresa de Contabilidad «" + conta.BaseDatos + "» en este servidor.");
            if (!Regex.IsMatch(emp.GuidDsl ?? "", "^[0-9a-fA-F-]+$")) throw new InvalidOperationException("GuidDSL inválido.");
            var lista = new List<EstadoCfdi>(); var externos = new List<EstadoExterno>();
            // TODOS los comprobantes de esta empresa que hay en el ADD (no solo los que ya bajamos): los que no estan en Descargas son «externos».
            using (var cmd = new SqlCommand(@"
SELECT UUID, TRY_CONVERT(datetime2, Fecha, 21), TipoComprobante, RFCEmisor, NombreEmisor, RFCReceptor, NombreReceptor, ISNULL(Serie, ''), ISNULL(Folio, ''), Total
FROM [document_" + emp.GuidDsl + "_metadata].dbo.Comprobante WHERE UUID IS NOT NULL AND (RFCEmisor = @r OR RFCReceptor = @r)", conn) { CommandTimeout = 300 })
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                    {
                        if (!Guid.TryParse(Convert.ToString(l.GetValue(0)), out var g)) continue;
                        lista.Add(new EstadoCfdi { Uuid = g, Estado = "En ADD" });
                        if (uuids.Contains(g)) continue;
                        bool emitido = string.Equals(l.IsDBNull(3) ? "" : Convert.ToString(l.GetValue(3)).Trim(), rfc, StringComparison.OrdinalIgnoreCase);
                        externos.Add(new EstadoExterno
                        {
                            Uuid = g, Fecha = l.IsDBNull(1) ? (DateTime?)null : l.GetDateTime(1), Emitido = emitido,
                            Tipo = l.IsDBNull(2) ? null : Convert.ToString(l.GetValue(2)).Trim().Substring(0, 1),
                            Contraparte = emitido ? (l.IsDBNull(6) ? Convert.ToString(l.GetValue(5)) : Convert.ToString(l.GetValue(6))) : (l.IsDBNull(4) ? Convert.ToString(l.GetValue(3)) : Convert.ToString(l.GetValue(4))),
                            SerieFolio = Convert.ToString(l.GetValue(7)) + Convert.ToString(l.GetValue(8)), Total = l.IsDBNull(9) ? 0m : Convert.ToDecimal(l.GetValue(9))
                        });
                    }
            }
            GuardarExternos(conn, rfc, SistemaCfdi.Add, externos);
            return lista;
        }

        private sealed class EstadoExterno
        {
            public Guid Uuid; public DateTime? Fecha; public bool Emitido; public string Tipo, Contraparte, SerieFolio; public decimal Total;
        }

        private static void GuardarExternos(SqlConnection conn, string rfc, string sistema, List<EstadoExterno> filas)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var del = new SqlCommand("DELETE FROM CfdiExterno WHERE RFC = @r AND Sistema = @s", conn, tx)) { del.Parameters.AddWithValue("@r", rfc); del.Parameters.AddWithValue("@s", sistema); del.ExecuteNonQuery(); }
                var t = new DataTable();
                foreach (var (n, ty) in new[] { ("RFC", typeof(string)), ("Sistema", typeof(string)), ("UUID", typeof(Guid)), ("FechaEmision", typeof(DateTime)), ("Direccion", typeof(string)),
                    ("TipoComprobante", typeof(string)), ("Contraparte", typeof(string)), ("SerieFolio", typeof(string)), ("Total", typeof(decimal)) }) t.Columns.Add(n, ty);
                var vistos = new HashSet<Guid>();
                foreach (var f in filas)
                    if (vistos.Add(f.Uuid))
                        t.Rows.Add(rfc, sistema, f.Uuid, (object)f.Fecha ?? DBNull.Value, f.Emitido ? "Emitido" : "Recibido", (object)f.Tipo ?? DBNull.Value, (object)Recortar(f.Contraparte, 300) ?? DBNull.Value, (object)Recortar(f.SerieFolio, 70) ?? DBNull.Value, f.Total);
                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "CfdiExterno", BulkCopyTimeout = 300 })
                {
                    foreach (DataColumn c in t.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                    bulk.WriteToServer(t);
                }
                tx.Commit();
            }
        }

        // Polizas: AsocCFDIs (UUID <-> poliza o movimiento) unido a Polizas / MovimientosPoliza. Un CFDI puede estar en varias.
        private static List<EstadoCfdi> VerificarPolizas(SqlConnection conn, string rfc, ContabilidadConfig conta, HashSet<Guid> uuids)
        {
            if (conta == null) return null;
            if (!Regex.IsMatch(conta.BaseDatos ?? "", "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Nombre de base inválido.");
            string ct = "[" + conta.BaseDatos + "].dbo.";

            string sql = @"
SELECT a.UUID, a.AppType, CAST(p.Guid AS nvarchar(72)) AS GuidPoliza, 'Poliza' AS Nivel, p.Ejercicio, p.Periodo, p.TipoPol, tp.Nombre, tp.IdTipoPolizaSAT,
       CONVERT(nvarchar(30), p.Folio), p.Fecha, p.Concepto, p.Cargos, p.Abonos
FROM " + ct + @"AsocCFDIs a JOIN " + ct + @"Polizas p ON p.Guid = a.GuidRef LEFT JOIN " + ct + @"TiposPolizas tp ON tp.Codigo = p.TipoPol
UNION ALL
SELECT a.UUID, a.AppType, CAST(p.Guid AS nvarchar(72)), 'Movimiento', p.Ejercicio, p.Periodo, p.TipoPol, tp.Nombre, tp.IdTipoPolizaSAT,
       CONVERT(nvarchar(30), p.Folio), p.Fecha, p.Concepto, p.Cargos, p.Abonos
FROM " + ct + @"AsocCFDIs a JOIN " + ct + @"MovimientosPoliza m ON m.Guid = a.GuidRef JOIN " + ct + @"Polizas p ON p.Id = m.IdPoliza
     LEFT JOIN " + ct + @"TiposPolizas tp ON tp.Codigo = p.TipoPol
UNION ALL
SELECT a.UUID, a.AppType, NULL, 'Otro', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
FROM " + ct + @"AsocCFDIs a
WHERE NOT EXISTS (SELECT 1 FROM " + ct + @"Polizas p WHERE p.Guid = a.GuidRef) AND NOT EXISTS (SELECT 1 FROM " + ct + @"MovimientosPoliza m WHERE m.Guid = a.GuidRef)";

            // Solo interesan los CFDI de esta empresa que estan en el ADD (los ya descargados y los «externos»); asi un CFDI de otra empresa no entra.
            var enAdd = new HashSet<Guid>();
            using (var cmdAdd = new SqlCommand("SELECT UUID FROM CfdiEstadoSistema WHERE RFC = @r AND Sistema = 'ADD'", conn) { CommandTimeout = 120 })
            {
                cmdAdd.Parameters.AddWithValue("@r", rfc);
                using (var la = cmdAdd.ExecuteReader()) while (la.Read()) enAdd.Add(la.GetGuid(0));
            }
            // Una fila por (CFDI, poliza): si el CFDI esta ligado a la poliza Y a un movimiento suyo, cuenta una sola vez.
            var polizas = new Dictionary<(Guid, string), PolizaFila>();
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 300 })
            using (var l = cmd.ExecuteReader())
                while (l.Read())
                {
                    if (!Guid.TryParse(Convert.ToString(l.GetValue(0)), out var uuid) || !(uuids.Contains(uuid) || enAdd.Contains(uuid))) continue;
                    string nivel = l.GetString(3); string origen = l.IsDBNull(1) ? null : Convert.ToString(l.GetValue(1));
                    Guid? gp = l.IsDBNull(2) || !Guid.TryParse(l.GetString(2), out var gpv) ? (Guid?)null : gpv;
                    string clave = gp.HasValue ? gp.Value.ToString() : "otro-" + (origen ?? "");
                    if (polizas.TryGetValue((uuid, clave), out var existente)) { if (nivel == "Poliza") existente.Nivel = "Poliza"; continue; }
                    string concepto = l.IsDBNull(11) ? null : Convert.ToString(l.GetValue(11));
                    int? sat = l.IsDBNull(8) ? (int?)null : Convert.ToInt32(l.GetValue(8));
                    polizas[(uuid, clave)] = new PolizaFila
                    {
                        Uuid = uuid, GuidPoliza = gp, Nivel = nivel, Origen = origen ?? "Contabilidad",
                        Ejercicio = l.IsDBNull(4) ? (int?)null : Convert.ToInt32(l.GetValue(4)), Periodo = l.IsDBNull(5) ? (int?)null : Convert.ToInt32(l.GetValue(5)),
                        TipoPol = l.IsDBNull(6) ? (int?)null : Convert.ToInt32(l.GetValue(6)), TipoNombre = l.IsDBNull(7) ? null : Convert.ToString(l.GetValue(7)),
                        Folio = l.IsDBNull(9) ? null : Convert.ToString(l.GetValue(9)), Fecha = l.IsDBNull(10) ? (DateTime?)null : Convert.ToDateTime(l.GetValue(10)),
                        Concepto = concepto, Cargos = l.IsDBNull(12) ? 0m : Convert.ToDecimal(l.GetValue(12)), Abonos = l.IsDBNull(13) ? 0m : Convert.ToDecimal(l.GetValue(13)),
                        Rol = DeducirRol(sat, concepto, nivel)
                    };
                }
            GuardarPolizas(conn, rfc, polizas.Values);

            return polizas.Values.GroupBy(p => p.Uuid).Select(g =>
            {
                int n = g.Count(p => p.Nivel != "Otro");
                int otros = g.Count(p => p.Nivel == "Otro");
                return new EstadoCfdi
                {
                    Uuid = g.Key,
                    Estado = n > 0 ? (n == 1 ? "1 póliza" : n + " pólizas") : "Asociado a " + string.Join(", ", g.Select(p => p.Origen).Distinct()),
                    Detalle = n > 0 ? string.Join(" · ", g.Where(p => p.Nivel != "Otro").OrderBy(p => p.Fecha).Take(8).Select(p => p.TipoNombre + " " + p.Folio)) : null
                };
            }).ToList();
        }

        private sealed class PolizaFila
        {
            public Guid Uuid; public Guid? GuidPoliza; public string Nivel, Origen, TipoNombre, Folio, Concepto, Rol;
            public int? Ejercicio, Periodo, TipoPol; public DateTime? Fecha; public decimal Cargos, Abonos;
        }

        // Rol de la poliza para ese CFDI. Se deduce del tipo SAT de la poliza (1 ingreso, 2 egreso, 3 diario) y del concepto; solo orienta.
        internal static string DeducirRol(int? tipoSat, string concepto, string nivel)
        {
            if (nivel == "Otro") return "Otro";
            if (!string.IsNullOrEmpty(concepto) && Regex.IsMatch(concepto, "cancel", RegexOptions.IgnoreCase)) return "Cancelación";
            switch (tipoSat)
            {
                case 1: return "Cobro / ingreso";
                case 2: return "Pago / egreso";
                case 3: return "Provisión / diario";
                default: return "Otro";
            }
        }

        // ===================================================================================
        //  Almacenamiento
        // ===================================================================================

        internal static void GuardarEstados(SqlConnection conn, string rfc, string sistema, IEnumerable<EstadoCfdi> estados)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var del = new SqlCommand("DELETE FROM CfdiEstadoSistema WHERE RFC = @r AND Sistema = @s", conn, tx) { CommandTimeout = 120 })
                { del.Parameters.AddWithValue("@r", rfc); del.Parameters.AddWithValue("@s", sistema); del.ExecuteNonQuery(); }

                var t = new DataTable();
                t.Columns.Add("RFC", typeof(string)); t.Columns.Add("Sistema", typeof(string)); t.Columns.Add("UUID", typeof(Guid));
                t.Columns.Add("Estado", typeof(string)); t.Columns.Add("Detalle", typeof(string)); t.Columns.Add("FechaVerificado", typeof(DateTime));
                var ahora = DateTime.UtcNow; var vistos = new HashSet<Guid>();
                foreach (var e in estados)
                    if (vistos.Add(e.Uuid)) t.Rows.Add(rfc, sistema, e.Uuid, e.Estado, Recortar(e.Detalle, 400), ahora);
                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "CfdiEstadoSistema", BulkCopyTimeout = 300 })
                {
                    foreach (DataColumn c in t.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                    bulk.WriteToServer(t);
                }
                tx.Commit();
            }
        }

        private static void GuardarPolizas(SqlConnection conn, string rfc, IEnumerable<PolizaFila> filas)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var del = new SqlCommand("DELETE FROM CfdiPoliza WHERE RFC = @r", conn, tx)) { del.Parameters.AddWithValue("@r", rfc); del.ExecuteNonQuery(); }
                var t = new DataTable();
                foreach (var (n, ty) in new[] { ("RFC", typeof(string)), ("UUID", typeof(Guid)), ("GuidPoliza", typeof(Guid)), ("Nivel", typeof(string)), ("Origen", typeof(string)),
                    ("Ejercicio", typeof(int)), ("Periodo", typeof(int)), ("TipoPol", typeof(int)), ("TipoNombre", typeof(string)), ("Folio", typeof(string)), ("Fecha", typeof(DateTime)),
                    ("Concepto", typeof(string)), ("Cargos", typeof(decimal)), ("Abonos", typeof(decimal)), ("Rol", typeof(string)), ("FechaVerificado", typeof(DateTime)) })
                    t.Columns.Add(n, ty);
                var ahora = DateTime.UtcNow;
                foreach (var p in filas)
                    t.Rows.Add(rfc, p.Uuid, (object)p.GuidPoliza ?? DBNull.Value, p.Nivel, p.Origen, (object)p.Ejercicio ?? DBNull.Value, (object)p.Periodo ?? DBNull.Value,
                        (object)p.TipoPol ?? DBNull.Value, (object)Recortar(p.TipoNombre, 60) ?? DBNull.Value, (object)Recortar(p.Folio, 30) ?? DBNull.Value, (object)p.Fecha ?? DBNull.Value,
                        (object)Recortar(p.Concepto, 300) ?? DBNull.Value, p.Cargos, p.Abonos, p.Rol, ahora);
                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "CfdiPoliza", BulkCopyTimeout = 300 })
                {
                    foreach (DataColumn c in t.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                    bulk.WriteToServer(t);
                }
                tx.Commit();
            }
        }

        private static void GuardarDocumentosComercial(SqlConnection conn, string rfc, string sistema, List<(Guid uuid, long id, int? modulo, string folio, DateTime? fecha, decimal total, bool cancelado)> docs)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var del = new SqlCommand("DELETE FROM CfdiDocumentoComercial WHERE RFC = @r AND Sistema = @s", conn, tx)) { del.Parameters.AddWithValue("@r", rfc); del.Parameters.AddWithValue("@s", sistema); del.ExecuteNonQuery(); }
                foreach (var d in docs)
                    using (var ins = new SqlCommand(@"INSERT INTO CfdiDocumentoComercial (RFC, UUID, DocumentID, ModuleID, Folio, Fecha, Total, Cancelado, Sistema) VALUES (@r, @u, @d, @m, @f, @fe, @t, @c, @s)", conn, tx))
                    {
                        ins.Parameters.AddWithValue("@s", sistema);
                        ins.Parameters.AddWithValue("@r", rfc); ins.Parameters.AddWithValue("@u", d.uuid); ins.Parameters.AddWithValue("@d", d.id);
                        ins.Parameters.AddWithValue("@m", (object)d.modulo ?? DBNull.Value); ins.Parameters.AddWithValue("@f", (object)Recortar(d.folio, 60) ?? DBNull.Value);
                        ins.Parameters.AddWithValue("@fe", (object)d.fecha ?? DBNull.Value); ins.Parameters.AddWithValue("@t", d.total); ins.Parameters.AddWithValue("@c", d.cancelado);
                        ins.ExecuteNonQuery();
                    }
                tx.Commit();
            }
        }

        private static void GuardarVerificacion(SqlConnection conn, string rfc, ResultadoVerificacion r)
        {
            using (var cmd = new SqlCommand(@"
MERGE VerificacionSistema AS t USING (SELECT @r AS RFC, @s AS Sistema) AS s ON t.RFC = s.RFC AND t.Sistema = s.Sistema
WHEN MATCHED THEN UPDATE SET Estado = @e, Total = @t, Mensaje = @m, FechaVerificado = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (RFC, Sistema, Estado, Total, Mensaje) VALUES (@r, @s, @e, @t, @m);", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@s", r.Sistema); cmd.Parameters.AddWithValue("@e", r.Estado);
                cmd.Parameters.AddWithValue("@t", r.Total); cmd.Parameters.AddWithValue("@m", (object)Recortar(r.Mensaje, 500) ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public static Dictionary<string, ResultadoVerificacion> ObtenerVerificaciones(SqlConnection conn, string rfc)
        {
            var d = new Dictionary<string, ResultadoVerificacion>();
            using (var cmd = new SqlCommand("SELECT Sistema, Estado, Total, Mensaje, FechaVerificado FROM VerificacionSistema WHERE RFC = @r", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        d[l.GetString(0)] = new ResultadoVerificacion { Sistema = l.GetString(0), Estado = l.GetString(1), Total = l.GetInt32(2), Mensaje = l.IsDBNull(3) ? null : l.GetString(3), Fecha = DateTime.SpecifyKind(l.GetDateTime(4), DateTimeKind.Utc).ToLocalTime() };
            }
            return d;
        }

        private static string Recortar(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max));

        // ===================================================================================
        //  Consultas del mapa
        // ===================================================================================

        // Universo del mapa: los CFDI de Descargas MAS los que estan en un sistema pero no en Descargas («externos»). Direccion = Emitidos / Recibidos.
        private const string UniversoSql = @"
WITH u AS (
  SELECT c.UUID, c.FechaEmision, CASE WHEN c.RFCEmisor = @r THEN 'Emitidos' ELSE 'Recibidos' END AS Direccion, c.TipoComprobante, c.EstatusSat, 0 AS Externo
  FROM CfdiRecibido c WHERE (c.RFCEmisor = @r OR c.RFCReceptor = @r) AND (@nom = 1 OR ISNULL(c.TipoComprobante, '') <> 'N')
  UNION ALL
  SELECT x.UUID, x.FechaEmision, CASE WHEN x.Direccion = 'Emitido' THEN 'Emitidos' ELSE 'Recibidos' END, x.TipoComprobante, 'Vigente', 1
  FROM CfdiExterno x WHERE x.RFC = @r AND (@nom = 1 OR ISNULL(x.TipoComprobante, '') <> 'N') AND NOT EXISTS (SELECT 1 FROM CfdiRecibido c2 WHERE c2.UUID = x.UUID)
)";

        // Una fila por mes y direccion con lo que cada sistema tiene. Los sistemas sin verificar quedan en 0 y la pantalla los muestra como «—».
        public static List<MapaFila> Calcular(SqlConnection conn, string rfc, bool incluirNomina)
        {
            var filas = new Dictionary<(int, int, string), MapaFila>();
            MapaFila Fila(int a, int m, string t)
            {
                if (!filas.TryGetValue((a, m, t), out var f)) filas[(a, m, t)] = f = new MapaFila { Anio = a, Mes = m, Tipo = t };
                return f;
            }

            foreach (var tipo in new[] { "Recibidos", "Emitidos" })
            {
                string cond = tipo == "Recibidos" ? "RFCReceptor = @r" : "RFCEmisor = @r";
                using (var cmd = new SqlCommand(@"
SELECT YEAR(FechaEmision), MONTH(FechaEmision), COUNT(*), SUM(CASE WHEN EstatusSat='Vigente' THEN 1 ELSE 0 END)
FROM CfdiMetadata WHERE Tipo = @t AND " + cond + @" AND FechaEmision IS NOT NULL AND (@nom = 1 OR ISNULL(EfectoComprobante,'') <> 'N')
GROUP BY YEAR(FechaEmision), MONTH(FechaEmision)", conn) { CommandTimeout = 120 })
                {
                    cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@t", tipo); cmd.Parameters.AddWithValue("@nom", incluirNomina ? 1 : 0);
                    using (var l = cmd.ExecuteReader())
                        while (l.Read()) { var f = Fila(l.GetInt32(0), l.GetInt32(1), tipo); f.SatTotal = l.GetInt32(2); f.SatVigentes = l.GetInt32(3); }
                }
            }

            const string sql = UniversoSql + @"
SELECT YEAR(c.FechaEmision), MONTH(c.FechaEmision), c.Direccion,
       SUM(CASE WHEN c.Externo = 0 THEN 1 ELSE 0 END),
       SUM(CASE WHEN di.Estado = 'Con archivo' THEN 1 ELSE 0 END),
       SUM(CASE WHEN co.UUID IS NOT NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN co.Estado = 'Con documento' THEN 1 ELSE 0 END),
       SUM(CASE WHEN ad.UUID IS NOT NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN po.UUID IS NOT NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN c.Externo = 0 AND c.EstatusSat = 'Vigente' AND ISNULL(di.Estado, 'Sin archivo') <> 'Con archivo' THEN 1 ELSE 0 END),
       SUM(CASE WHEN c.Externo = 0 AND c.EstatusSat = 'Vigente' AND co.UUID IS NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN c.EstatusSat = 'Vigente' AND ad.UUID IS NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN c.EstatusSat = 'Vigente' AND po.UUID IS NULL THEN 1 ELSE 0 END),
       SUM(c.Externo),
       SUM(CASE WHEN pr.UUID IS NOT NULL THEN 1 ELSE 0 END),
       SUM(CASE WHEN c.Externo = 0 AND " + Diferencias.Condicion + @" THEN 1 ELSE 0 END)
FROM u c
LEFT JOIN CfdiEstadoSistema pr ON pr.RFC = @r AND pr.Sistema = 'Premium' AND pr.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema di ON di.RFC = @r AND di.Sistema = 'Disco' AND di.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema co ON co.RFC = @r AND co.Sistema = 'Comercial' AND co.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema ad ON ad.RFC = @r AND ad.Sistema = 'ADD' AND ad.UUID = c.UUID
LEFT JOIN (SELECT DISTINCT UUID FROM CfdiPoliza WHERE RFC = @r AND Nivel <> 'Otro') po ON po.UUID = c.UUID
"+Diferencias.Union+@"
WHERE c.FechaEmision IS NOT NULL
GROUP BY YEAR(c.FechaEmision), MONTH(c.FechaEmision), c.Direccion";
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 })
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@nom", incluirNomina ? 1 : 0);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                    {
                        var f = Fila(l.GetInt32(0), l.GetInt32(1), l.GetString(2));
                        f.Descargados = l.GetInt32(3); f.EnDisco = l.GetInt32(4); f.EnComercial = l.GetInt32(5); f.ConDocumento = l.GetInt32(6); f.EnAdd = l.GetInt32(7); f.ConPoliza = l.GetInt32(8);
                        f.FaltaDisco = l.GetInt32(9); f.FaltaComercial = l.GetInt32(10); f.FaltaAdd = l.GetInt32(11); f.SinPoliza = l.GetInt32(12); f.SoloSistemas = l.GetInt32(13); f.EnPremium = l.GetInt32(14); f.TotalDistinto = l.GetInt32(15);
                    }
            }
            return filas.Values.OrderByDescending(f => f.Anio).ThenByDescending(f => f.Mes).ThenBy(f => f.Tipo).ToList();
        }

        // Lista de CFDI para una celda del mapa. filtro: Todos / FaltaAdd / FaltaComercial / SinPoliza / FaltaDisco. anio/mes null = todos los meses.
        public static List<MapaCfdiFila> ObtenerCfdis(SqlConnection conn, string rfc, bool incluirNomina, string filtro, int? anio, int? mes, string tipo, int limite = 5000)
        {
            string extra = filtro == "FaltaAdd" ? "AND c.EstatusSat = 'Vigente' AND ad.UUID IS NULL"
                : filtro == "FaltaComercial" ? "AND c.EstatusSat = 'Vigente' AND co.UUID IS NULL"
                : filtro == "TotalDistinto" ? "AND c.Externo = 0 AND " + Diferencias.Condicion
                : filtro == "FaltaPremium" ? "AND c.EstatusSat = 'Vigente' AND c.Externo = 0 AND pr.UUID IS NULL"
                : filtro == "SinPoliza" ? "AND c.EstatusSat IN ('Vigente', 'Sin descargar') AND po.UUID IS NULL"
                : filtro == "FaltaDisco" ? "AND c.Externo = 0 AND c.EstatusSat = 'Vigente' AND ISNULL(di.Estado, 'Sin archivo') <> 'Con archivo'"
                : filtro == "SoloSistemas" ? "AND c.Externo = 1" : "";
            if (anio.HasValue) extra += " AND YEAR(c.FechaEmision) = @a";
            if (mes.HasValue) extra += " AND MONTH(c.FechaEmision) = @m";
            if (tipo == "Emitidos" || tipo == "Recibidos") extra += " AND c.Direccion = @t";
            if (filtro == "FaltaComercial") extra += " AND c.Externo = 0";

            // El universo trae de Descargas los datos completos; de los «externos» (solo en el ADD) los que guarda CfdiExterno.
            string sql = @"
WITH u AS (
  SELECT c.UUID, c.FechaEmision, CASE WHEN c.RFCEmisor = @r THEN 'Emitidos' ELSE 'Recibidos' END AS Direccion, c.TipoComprobante, c.EstatusSat, 0 AS Externo, c.CfdiID,
         CASE WHEN c.RFCEmisor = @r THEN c.RFCReceptor ELSE ISNULL(c.NombreEmisor, c.RFCEmisor) END AS Contraparte, ISNULL(c.Serie, '') + ISNULL(c.Folio, '') AS SerieFolio, ISNULL(c.Total, 0) AS Total
  FROM CfdiRecibido c WHERE (c.RFCEmisor = @r OR c.RFCReceptor = @r) AND (@nom = 1 OR ISNULL(c.TipoComprobante, '') <> 'N')
  UNION ALL
  SELECT x.UUID, x.FechaEmision, CASE WHEN x.Direccion = 'Emitido' THEN 'Emitidos' ELSE 'Recibidos' END, x.TipoComprobante, 'Sin descargar', 1, 0,
         ISNULL(x.Contraparte, ''), ISNULL(x.SerieFolio, ''), ISNULL(x.Total, 0)
  FROM CfdiExterno x WHERE x.RFC = @r AND (@nom = 1 OR ISNULL(x.TipoComprobante, '') <> 'N') AND NOT EXISTS (SELECT 1 FROM CfdiRecibido c2 WHERE c2.UUID = x.UUID)
)
SELECT TOP (@lim) c.UUID, c.FechaEmision, CASE WHEN c.Direccion = 'Emitidos' THEN 'Emitido' ELSE 'Recibido' END, c.TipoComprobante, c.Contraparte, c.SerieFolio, c.Total, c.EstatusSat,
       di.Estado, co.Estado, ad.Estado, (SELECT COUNT(*) FROM CfdiPoliza p WHERE p.RFC = @r AND p.UUID = c.UUID AND p.Nivel <> 'Otro'), c.CfdiID, pr.Estado
FROM u c
LEFT JOIN CfdiEstadoSistema pr ON pr.RFC = @r AND pr.Sistema = 'Premium' AND pr.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema di ON di.RFC = @r AND di.Sistema = 'Disco' AND di.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema co ON co.RFC = @r AND co.Sistema = 'Comercial' AND co.UUID = c.UUID
LEFT JOIN CfdiEstadoSistema ad ON ad.RFC = @r AND ad.Sistema = 'ADD' AND ad.UUID = c.UUID
LEFT JOIN (SELECT DISTINCT UUID FROM CfdiPoliza WHERE RFC = @r AND Nivel <> 'Otro') po ON po.UUID = c.UUID
"+Diferencias.Union+@"
WHERE c.FechaEmision IS NOT NULL " + extra + @"
ORDER BY c.FechaEmision DESC";
            var lista = new List<MapaCfdiFila>();
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 })
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@nom", incluirNomina ? 1 : 0); cmd.Parameters.AddWithValue("@lim", limite);
                if (tipo == "Emitidos" || tipo == "Recibidos") cmd.Parameters.AddWithValue("@t", tipo);
                if (anio.HasValue) cmd.Parameters.AddWithValue("@a", anio.Value);
                if (mes.HasValue) cmd.Parameters.AddWithValue("@m", mes.Value);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add(new MapaCfdiFila
                        {
                            Uuid = l.GetGuid(0), Fecha = l.GetDateTime(1), Direccion = l.GetString(2), TipoComprobante = l.IsDBNull(3) ? "" : l.GetString(3).Trim(),
                            Contraparte = l.IsDBNull(4) ? "" : l.GetString(4), SerieFolio = l.GetString(5), Total = l.GetDecimal(6), Estatus = l.GetString(7),
                            Disco = l.IsDBNull(8) ? "—" : l.GetString(8), Comercial = l.IsDBNull(9) ? "—" : l.GetString(9), Add = l.IsDBNull(10) ? "—" : l.GetString(10), Polizas = l.GetInt32(11), CfdiId = l.GetInt32(12), Premium = l.IsDBNull(13) ? "—" : l.GetString(13)
                        });
            }
            return lista;
        }

        // ---- Detalle de un CFDI ----

        public static List<PolizaCfdi> ObtenerPolizas(SqlConnection conn, string rfc, Guid uuid)
        {
            var lista = new List<PolizaCfdi>();
            using (var cmd = new SqlCommand("SELECT Origen, Nivel, TipoNombre, Folio, Fecha, Ejercicio, Periodo, Concepto, Cargos, Abonos, Rol FROM CfdiPoliza WHERE RFC = @r AND UUID = @u ORDER BY Fecha, CfdiPolizaID", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@u", uuid);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add(new PolizaCfdi
                        {
                            Origen = l.IsDBNull(0) ? null : l.GetString(0), Nivel = l.GetString(1), TipoNombre = l.IsDBNull(2) ? null : l.GetString(2), Folio = l.IsDBNull(3) ? null : l.GetString(3),
                            Fecha = l.IsDBNull(4) ? (DateTime?)null : l.GetDateTime(4), Ejercicio = l.IsDBNull(5) ? (int?)null : l.GetInt32(5), Periodo = l.IsDBNull(6) ? (int?)null : l.GetInt32(6),
                            Concepto = l.IsDBNull(7) ? null : l.GetString(7), Cargos = l.IsDBNull(8) ? 0m : l.GetDecimal(8), Abonos = l.IsDBNull(9) ? 0m : l.GetDecimal(9), Rol = l.IsDBNull(10) ? null : l.GetString(10)
                        });
            }
            return lista;
        }

        public static List<DocumentoComercialCfdi> ObtenerDocumentosComercial(SqlConnection conn, string rfc, Guid uuid)
        {
            var lista = new List<DocumentoComercialCfdi>();
            using (var cmd = new SqlCommand("SELECT DocumentID, ModuleID, Folio, Fecha, Total, Cancelado, Sistema FROM CfdiDocumentoComercial WHERE RFC = @r AND UUID = @u ORDER BY Fecha", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@u", uuid);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add(new DocumentoComercialCfdi { DocumentId = l.GetInt64(0), ModuleId = l.IsDBNull(1) ? (int?)null : l.GetInt32(1), Folio = l.IsDBNull(2) ? null : l.GetString(2),
                            Fecha = l.IsDBNull(3) ? (DateTime?)null : l.GetDateTime(3), Total = l.IsDBNull(4) ? 0m : l.GetDecimal(4), Cancelado = l.GetBoolean(5), Sistema = l.GetString(6) });
            }
            // Total del CFDI para poder mostrar la diferencia (solo ingreso/egreso en pesos).
            if (lista.Count > 0)
                using (var cmd = new SqlCommand("SELECT TOP 1 Total FROM CfdiRecibido WHERE UUID = @u AND TipoComprobante IN ('I', 'E') AND Total > 0 AND ISNULL(Moneda, 'MXN') IN ('MXN', 'XXX')", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uuid);
                    var t = cmd.ExecuteScalar();
                    if (t != null && t != DBNull.Value) foreach (var d in lista) d.TotalCfdi = Convert.ToDecimal(t);
                }
            return lista;
        }

        // Estado de un CFDI en cada sistema (para el detalle): Sistema -> (Estado, Detalle).
        public static Dictionary<string, (string estado, string detalle)> ObtenerEstados(SqlConnection conn, string rfc, Guid uuid)
        {
            var d = new Dictionary<string, (string, string)>();
            using (var cmd = new SqlCommand("SELECT Sistema, Estado, Detalle FROM CfdiEstadoSistema WHERE RFC = @r AND UUID = @u", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@u", uuid);
                using (var l = cmd.ExecuteReader()) while (l.Read()) d[l.GetString(0)] = (l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2));
            }
            return d;
        }
    }
}
