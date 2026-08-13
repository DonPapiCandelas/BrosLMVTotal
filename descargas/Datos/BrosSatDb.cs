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

// BrosSatDb.cs -- acceso a datos de la BD propia de BrosLMV.Descargas. Todo parametrizado
// (SqlParameter), nunca concatenacion de strings hacia SQL.

using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class SolicitudFila
    {
        public string IdSolicitud;
        public string Tipo;
        public DateTime FechaInicial;
        public DateTime FechaFinal;
        public string Estatus;
        public int? NumeroCFDIs;
        public DateTime FechaSolicitud;
    }

    internal sealed class CfdiFila
    {
        public string RFCEmisor;
        public string NombreEmisor;
        public string TipoComprobante;
        public decimal? Total;
        public DateTime FechaEmision;
        public string EstatusSat;
    }

    internal sealed class SolicitudPendiente
    {
        public int SolicitudDescargaID;
        public string IdSolicitud;
        public string RfcSolicitante;
        public string Estatus;
    }

    internal static class BrosSatDb
    {
        // Solicitudes que el worker todavia necesita atender: las que siguen en proceso en el
        // SAT (hay que preguntar de nuevo), o las que ya terminaron pero les falta descargar
        // algun paquete detectado (VecesDescargado = 0).
        public static List<SolicitudPendiente> ObtenerSolicitudesPendientes(SqlConnection conn)
        {
            const string sql = @"
SELECT DISTINCT s.SolicitudDescargaID, s.IdSolicitud, s.RfcSolicitante, s.Estatus
FROM SolicitudDescarga s
LEFT JOIN SolicitudDescargaPaquete p ON p.SolicitudDescargaID = s.SolicitudDescargaID AND p.VecesDescargado = 0
WHERE s.Estatus IN ('Aceptada', 'EnProceso') OR p.SolicitudDescargaPaqueteID IS NOT NULL;";

            var resultado = new List<SolicitudPendiente>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.Add(new SolicitudPendiente
                    {
                        SolicitudDescargaID = reader.GetInt32(0),
                        IdSolicitud = reader.GetString(1),
                        RfcSolicitante = reader.GetString(2),
                        Estatus = reader.GetString(3)
                    });
                }
            }
            return resultado;
        }

        // Registra los paquetes que VerificaSolicitud acaba de reportar como listos, con
        // VecesDescargado=0 -- es decir "sabemos que existen, todavia no los bajamos". El
        // worker luego los toma de aqui para descargarlos. Idempotente: si ya se conocia el
        // paquete (por ejemplo porque ya se descargo antes), no lo pisa.
        public static void RegistrarPaquetesDetectados(SqlConnection conn, string idSolicitud, IEnumerable<string> idsPaquetes)
        {
            const string sql = @"
DECLARE @SolID INT = (SELECT SolicitudDescargaID FROM SolicitudDescarga WHERE IdSolicitud = @IdSolicitud);
IF @SolID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SolicitudDescargaPaquete WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete)
    INSERT INTO SolicitudDescargaPaquete (SolicitudDescargaID, IdPaquete, VecesDescargado)
    VALUES (@SolID, @IdPaquete, 0);";

            foreach (var idPaquete in idsPaquetes)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                    cmd.Parameters.AddWithValue("@IdPaquete", idPaquete);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<string> ObtenerPaquetesSinDescargar(SqlConnection conn, string idSolicitud)
        {
            const string sql = @"
SELECT p.IdPaquete
FROM SolicitudDescargaPaquete p
JOIN SolicitudDescarga s ON s.SolicitudDescargaID = p.SolicitudDescargaID
WHERE s.IdSolicitud = @IdSolicitud AND p.VecesDescargado = 0;";

            var resultado = new List<string>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) resultado.Add(reader.GetString(0));
            }
            return resultado;
        }

        public static int RegistrarSolicitud(SqlConnection conn, string idSolicitud, string rfcSolicitante,
            string tipo, DateTime desde, DateTime hasta, string origen)
        {
            const string sql = @"
INSERT INTO SolicitudDescarga (IdSolicitud, RfcSolicitante, Tipo, FechaInicial, FechaFinal, Origen)
OUTPUT INSERTED.SolicitudDescargaID
VALUES (@IdSolicitud, @RfcSolicitante, @Tipo, @FechaInicial, @FechaFinal, @Origen);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.Parameters.AddWithValue("@RfcSolicitante", rfcSolicitante);
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@FechaInicial", desde);
                cmd.Parameters.AddWithValue("@FechaFinal", hasta);
                cmd.Parameters.AddWithValue("@Origen", origen);
                return (int)cmd.ExecuteScalar();
            }
        }

        public static void ActualizarEstatusSolicitud(SqlConnection conn, string idSolicitud, string estatus, int? numeroCfdis)
        {
            const string sql = @"
UPDATE SolicitudDescarga
SET Estatus = @Estatus, NumeroCFDIs = @NumeroCFDIs, FechaUltimaVerificacion = SYSUTCDATETIME()
WHERE IdSolicitud = @IdSolicitud;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Estatus", estatus);
                cmd.Parameters.AddWithValue("@NumeroCFDIs", (object)numeroCfdis ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.ExecuteNonQuery();
            }
        }

        public static void RegistrarPaquete(SqlConnection conn, string idSolicitud, string idPaquete)
        {
            const string sql = @"
DECLARE @SolID INT = (SELECT SolicitudDescargaID FROM SolicitudDescarga WHERE IdSolicitud = @IdSolicitud);
IF @SolID IS NULL RETURN;

IF EXISTS (SELECT 1 FROM SolicitudDescargaPaquete WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete)
    UPDATE SolicitudDescargaPaquete
    SET VecesDescargado = VecesDescargado + 1, FechaUltimaDescarga = SYSUTCDATETIME()
    WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete;
ELSE
    INSERT INTO SolicitudDescargaPaquete (SolicitudDescargaID, IdPaquete, VecesDescargado, FechaUltimaDescarga)
    VALUES (@SolID, @IdPaquete, 1, SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.Parameters.AddWithValue("@IdPaquete", idPaquete);
                cmd.ExecuteNonQuery();
            }
        }

        // Idempotente por UUID (UNIQUE) -- si el CFDI ya existia (misma descarga corrida dos
        // veces, o el mismo XML llego por otra solicitud con rango traslapado), no duplica.
        // Regresa true si insertó una fila nueva, false si ya existía.
        public static bool GuardarCfdiRecibido(SqlConnection conn, CfdiParseado c, string rutaArchivoXml)
        {
            const string sql = @"
IF EXISTS (SELECT 1 FROM CfdiRecibido WHERE UUID = @UUID)
    SELECT 0;
ELSE
BEGIN
    INSERT INTO CfdiRecibido
        (UUID, RFCEmisor, NombreEmisor, RFCReceptor, Serie, Folio, TipoComprobante,
         FormaPago, MetodoPago, UsoCFDI, Subtotal, Descuento, IVA, Total, Moneda,
         FechaEmision, RutaArchivoXml)
    VALUES
        (@UUID, @RFCEmisor, @NombreEmisor, @RFCReceptor, @Serie, @Folio, @TipoComprobante,
         @FormaPago, @MetodoPago, @UsoCFDI, @Subtotal, @Descuento, @IVA, @Total, @Moneda,
         @FechaEmision, @RutaArchivoXml);
    SELECT 1;
END";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UUID", c.UUID);
                cmd.Parameters.AddWithValue("@RFCEmisor", (object)c.RFCEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NombreEmisor", (object)c.NombreEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RFCReceptor", (object)c.RFCReceptor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Serie", (object)c.Serie ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Folio", (object)c.Folio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoComprobante", (object)c.TipoComprobante ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FormaPago", (object)c.FormaPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MetodoPago", (object)c.MetodoPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UsoCFDI", (object)c.UsoCFDI ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Subtotal", (object)c.Subtotal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Descuento", (object)c.Descuento ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IVA", (object)c.IVA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Total", (object)c.Total ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Moneda", (object)c.Moneda ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FechaEmision", c.FechaEmision);
                cmd.Parameters.AddWithValue("@RutaArchivoXml", (object)rutaArchivoXml ?? DBNull.Value);
                int insertado = (int)cmd.ExecuteScalar();
                return insertado == 1;
            }
        }

        public static void GuardarRelaciones(SqlConnection conn, CfdiParseado c)
        {
            if (c.Relaciones.Count == 0) return;

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM CfdiRelacion WHERE UUID = @UUID AND UUIDRelacionado = @UUIDRelacionado)
    INSERT INTO CfdiRelacion (UUID, UUIDRelacionado, TipoRelacion)
    VALUES (@UUID, @UUIDRelacionado, @TipoRelacion);";

            foreach (var (uuidRelacionado, tipoRelacion) in c.Relaciones)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UUID", c.UUID);
                    cmd.Parameters.AddWithValue("@UUIDRelacionado", uuidRelacionado);
                    cmd.Parameters.AddWithValue("@TipoRelacion", (object)tipoRelacion ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Solo lectura -- usadas por la UI para pintar la cola/historial. TOP acotado a proposito
        // (esto es un panel de escritorio, no un reporte de auditoria completo).
        public static List<SolicitudFila> ObtenerSolicitudesRecientes(SqlConnection conn, int top = 50)
        {
            string sql = $@"
SELECT TOP ({top}) IdSolicitud, Tipo, FechaInicial, FechaFinal, Estatus, NumeroCFDIs, FechaSolicitud
FROM SolicitudDescarga
ORDER BY FechaSolicitud DESC;";

            var resultado = new List<SolicitudFila>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.Add(new SolicitudFila
                    {
                        IdSolicitud = reader.GetString(0),
                        Tipo = reader.GetString(1),
                        FechaInicial = reader.GetDateTime(2),
                        FechaFinal = reader.GetDateTime(3),
                        Estatus = reader.GetString(4),
                        NumeroCFDIs = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                        FechaSolicitud = reader.GetDateTime(6)
                    });
                }
            }
            return resultado;
        }

        public static List<CfdiFila> ObtenerCfdiRecientes(SqlConnection conn, int top = 100)
        {
            string sql = $@"
SELECT TOP ({top}) RFCEmisor, NombreEmisor, TipoComprobante, Total, FechaEmision, EstatusSat
FROM CfdiRecibido
ORDER BY FechaDescarga DESC;";

            var resultado = new List<CfdiFila>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.Add(new CfdiFila
                    {
                        RFCEmisor = reader.GetString(0),
                        NombreEmisor = reader.IsDBNull(1) ? null : reader.GetString(1),
                        TipoComprobante = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Total = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                        FechaEmision = reader.GetDateTime(4),
                        EstatusSat = reader.GetString(5)
                    });
                }
            }
            return resultado;
        }
    }
}
