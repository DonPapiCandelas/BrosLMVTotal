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

// Vinculacion.cs -- sugerencias de vinculo entre un CFDI de la lista de XML de Comercial y el documento
// que le corresponde, y validaciones cruzadas. SOLO LEE la base de Comercial: no vincula nada (decision
// de producto del usuario: «no vincula automaticamente»). El vinculo real lo hace la persona en la
// ventana de Comercial; aqui se le ahorra buscar.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class CandidatoDocumento
    {
        public int DocumentID;
        public int ModuleID;
        public string Folio;
        public decimal Total;
        public DateTime Fecha;
        public string Motivo; // "RFC + total + fecha" / "total + fecha (otro RFC)"
    }

    internal sealed class CfdiSinDocumento
    {
        public Guid UUID;
        public string RfcContraparte;
        public string Nombre;
        public decimal Total;
        public DateTime Fecha;
        public bool Recibido;
        public List<CandidatoDocumento> Candidatos = new List<CandidatoDocumento>();
        public string MejorCandidato => Candidatos.Count == 0 ? "Sin candidato" :
            "Doc " + Candidatos[0].DocumentID + " · " + Candidatos[0].Folio + " · " + Candidatos[0].Total.ToString("N2") + " (" + Candidatos[0].Motivo + ")" +
            (Candidatos.Count > 1 ? " y " + (Candidatos.Count - 1) + " mas" : "");
    }

    internal sealed class DiferenciaDeTotal
    {
        public Guid UUID;
        public int DocumentID;
        public string Folio;
        public decimal TotalCfdi;
        public decimal TotalDocumento;
        public decimal Diferencia => TotalCfdi - TotalDocumento;
    }

    internal sealed class ResultadoVinculacion
    {
        public List<CfdiSinDocumento> SinDocumento = new List<CfdiSinDocumento>();
        public List<DiferenciaDeTotal> Diferencias = new List<DiferenciaDeTotal>();
    }

    internal static class Vinculacion
    {
        public static async Task<ResultadoVinculacion> AnalizarAsync(string conexionComercial, int meses = 6, int limite = 300)
        {
            var r = new ResultadoVinculacion();
            DateTime desde = DateTime.Today.AddMonths(-meses);
            using (var cn = new SqlConnection(conexionComercial))
            {
                await cn.OpenAsync().ConfigureAwait(false);

                // 1) CFDI vigentes de la lista de XML que no estan vinculados a ningun documento
                var filas = new List<CfdiSinDocumento>();
                using (var cmd = new SqlCommand(@"
SELECT TOP (@lim) UUID, RFCEmisor, RFCReceptor, RazonSocial, Total, FechaEmision, TipoEmisionID
FROM docDocumentCFDiSAT
WHERE DocumentID = 0 AND Status = 'Vigente' AND FechaEmision >= @desde AND DeletedOn IS NULL
  AND ISNULL(TipoComprobante, '') NOT LIKE 'P%' AND ISNULL(TipoComprobante, '') NOT LIKE 'N%' AND ISNULL(TipoComprobante, '') NOT LIKE 'T%'
ORDER BY FechaEmision DESC;", cn))
                {
                    cmd.Parameters.AddWithValue("@lim", limite);
                    cmd.Parameters.AddWithValue("@desde", desde);
                    using (var l = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                        while (await l.ReadAsync().ConfigureAwait(false))
                        {
                            bool emitido = !l.IsDBNull(6) && Convert.ToInt32(l.GetValue(6)) == 1;
                            filas.Add(new CfdiSinDocumento
                            {
                                UUID = Guid.Parse(l.GetString(0)),
                                Recibido = !emitido,
                                RfcContraparte = emitido ? l.GetString(2) : l.GetString(1),
                                Nombre = l.IsDBNull(3) ? "" : l.GetString(3),
                                Total = Convert.ToDecimal(l.GetValue(4)),
                                Fecha = l.GetDateTime(5)
                            });
                        }
                }

                foreach (var f in filas)
                {
                    // Candidatos: documentos de un modulo que acepta XML (recibido/emitido segun el caso), aun sin XML
                    // vinculado, con el mismo total (+-0.02) y fecha a menos de 45 dias. Primero los del mismo RFC.
                    using (var cmd = new SqlCommand(@"
SELECT TOP 5 d.DocumentID, d.ModuleID, ISNULL(d.FolioPrefix,'') + CAST(d.Folio AS NVARCHAR(20)), d.Total, d.DateDocument,
       CASE WHEN EXISTS (SELECT 1 FROM orgIdentificationKey k WHERE k.BusinessEntityID = d.BusinessEntityID
                         AND k.IdentificationTypeID = 1 AND k.IdentificationValue = @rfc) THEN 1 ELSE 0 END AS MismoRfc
FROM docDocument d
WHERE ABS(d.Total - @total) <= 0.02 AND ABS(DATEDIFF(DAY, d.DateDocument, @fecha)) <= 45
  AND NOT EXISTS (SELECT 1 FROM docDocumentCFDiSAT x WHERE x.DocumentID = d.DocumentID)
  AND d.ModuleID IN (SELECT ModuleID FROM engModuleParameter WHERE ParameterKey = @param AND Value = '1')
ORDER BY MismoRfc DESC, ABS(DATEDIFF(DAY, d.DateDocument, @fecha));", cn))
                    {
                        cmd.Parameters.AddWithValue("@rfc", f.RfcContraparte);
                        cmd.Parameters.AddWithValue("@total", f.Total);
                        cmd.Parameters.AddWithValue("@fecha", f.Fecha);
                        cmd.Parameters.AddWithValue("@param", f.Recibido ? "XMLRecibido" : "XMLEmitido");
                        using (var l = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                            while (await l.ReadAsync().ConfigureAwait(false))
                                f.Candidatos.Add(new CandidatoDocumento
                                {
                                    DocumentID = l.GetInt32(0), ModuleID = l.GetInt32(1), Folio = l.IsDBNull(2) ? "" : l.GetString(2),
                                    Total = Convert.ToDecimal(l.GetValue(3)), Fecha = l.GetDateTime(4),
                                    Motivo = Convert.ToInt32(l.GetValue(5)) == 1 ? "RFC + total + fecha" : "total + fecha, otro RFC"
                                });
                    }
                    r.SinDocumento.Add(f);
                }

                // 2) Documentos ya vinculados cuyo total no coincide con el del CFDI
                using (var cmd = new SqlCommand(@"
SELECT TOP (@lim) s.UUID, d.DocumentID, ISNULL(d.FolioPrefix,'') + CAST(d.Folio AS NVARCHAR(20)), s.Total, d.Total
FROM docDocumentCFDiSAT s JOIN docDocument d ON d.DocumentID = s.DocumentID
WHERE s.DocumentID <> 0 AND s.Status = 'Vigente' AND s.FechaEmision >= @desde AND ABS(s.Total - d.Total) > 0.02
ORDER BY ABS(s.Total - d.Total) DESC;", cn))
                {
                    cmd.Parameters.AddWithValue("@lim", limite);
                    cmd.Parameters.AddWithValue("@desde", desde);
                    using (var l = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                        while (await l.ReadAsync().ConfigureAwait(false))
                            r.Diferencias.Add(new DiferenciaDeTotal
                            {
                                UUID = Guid.Parse(l.GetString(0)), DocumentID = l.GetInt32(1), Folio = l.IsDBNull(2) ? "" : l.GetString(2),
                                TotalCfdi = Convert.ToDecimal(l.GetValue(3)), TotalDocumento = Convert.ToDecimal(l.GetValue(4))
                            });
                }
            }
            return r;
        }
    }
}
