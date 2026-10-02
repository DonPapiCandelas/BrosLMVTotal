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

// ReportesSql.cs -- pedido explicito del usuario (2026-08-18): tarjeta de totales por mes,
// facturado/comprado, top proveedores, facturas cobradas (PUE) vs pendientes (PPD sin
// complemento de pago), errores de coherencia MetodoPago/FormaPago, notas de credito
// relacionadas, y desglose por UsoCFDI. Separado de BrosSatDb.cs (que ya es grande) para no
// mezclar el CRUD de la cola de descargas con las consultas de reporteo, que son de solo
// lectura y no tienen nada que ver con el motor de solicitudes.

using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class ResumenMensualFila
    {
        public decimal FacturadoTotal;
        public decimal FacturadoIva;
        public int FacturadoCount;
        public decimal CompradoTotal;
        public decimal CompradoIva;
        public int CompradoCount;
        public decimal CompradoPueTotal;
        public int CompradoPueCount;
        public decimal CompradoPpdTotal;
        public int CompradoPpdCount;
    }

    internal sealed class ProveedorResumenFila
    {
        public string RFC;
        public string Nombre;
        public decimal Total;
        public int Count;
    }

    internal sealed class ErrorCoherenciaFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string Serie;
        public string Folio;
        public string MetodoPago;
        public string FormaPago;
        public string UsoCFDI;
        public decimal? Total;
        public DateTime FechaEmision;
        public string Motivo;
    }

    internal sealed class CedulaImpuestosMensualFila
    {
        public decimal VentasSubtotalPue;
        public decimal VentasIvaPue;
        public decimal VentasSubtotalPpd;
        public decimal VentasIvaPpd;
        public decimal VentasTotal;
        public decimal VentasIvaTotal;

        public decimal ComprasSubtotalPue;
        public decimal ComprasIvaPue;
        public decimal ComprasSubtotalPpd;
        public decimal ComprasIvaPpd;
        public decimal ComprasTotal;
        public decimal ComprasIvaTotal;

        public decimal IvaDiferencialEfectivo => VentasIvaPue - ComprasIvaPue;

        public decimal RetencionesEmitidas;
        public decimal RetencionesRecibidas;
    }

    internal sealed class FacturaRiesgoRepFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string SerieFolio;
        public DateTime FechaEmision;
        public decimal Total;
        public decimal IVA;
        public int DiasTranscurridos;
        public string Riesgo;
    }

    // "Cuentas por pagar" segun el SAT: PPD cuyo Total no esta cubierto todavia por la suma de
    // ImpPagado de sus complementos de pago -- incluye tanto las que no tienen NINGUN
    // complemento como las que ya tienen uno o mas pero quedaron con saldo (pago parcial).
    internal sealed class CuentaPorPagarFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string Serie;
        public string Folio;
        public decimal Total;
        public decimal Pagado;
        public decimal NotaCreditoTotal;
        public decimal Saldo;
        public int NumComplementos;
        public DateTime FechaEmision;
        public int DiasTranscurridos;
    }

    // Espejo de CuentaPorPagarFila pero del lado de ventas (Emitidos) -- RFCReceptor aqui es el
    // cliente, no un proveedor.
    internal sealed class CuentaPorCobrarFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCReceptor;
        public string Serie;
        public string Folio;
        public decimal Total;
        public decimal Pagado;
        public decimal NotaCreditoTotal;
        public decimal Saldo;
        public int NumComplementos;
        public DateTime FechaEmision;
        public int DiasTranscurridos;
    }

    internal sealed class NotaCreditoFila
    {
        public Guid UUIDNotaCredito;
        public string SerieNotaCredito;
        public string FolioNotaCredito;
        public decimal? TotalNotaCredito;
        public DateTime FechaNotaCredito;
        public Guid UUIDOriginal;
        public string SerieOriginal;
        public string FolioOriginal;
        public decimal? TotalOriginal;
        public bool OriginalEnBd;
    }

    internal sealed class UsoCfdiResumenFila
    {
        public string UsoCFDI;
        public int Count;
        public decimal Total;
    }

    internal sealed class CfdiMesFila
    {
        public Guid UUID;
        public string TipoComprobante;
        public string Flujo;
        public string RFCEmisor;
        public string NombreEmisor;
        public string RFCReceptor;
        public string Serie;
        public string Folio;
        public DateTime FechaEmision;
        public decimal? Subtotal;
        public decimal? Descuento;
        public decimal? IVA;
        public decimal? Retenciones;
        public decimal? Total;
        public string Moneda;
        public string FormaPago;
        public string MetodoPago;
        public string EstatusSat;
    }

    internal static class ReportesSql
    {
        public static ResumenMensualFila ObtenerResumenMensual(SqlConnection conn, string rfc, int anio, int mes)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            const string sql = @"
SELECT
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS FacturadoTotal,
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS FacturadoIva,
  ISNULL((SELECT COUNT(*) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS FacturadoCount,
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoTotal,
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoIva,
  ISNULL((SELECT COUNT(*) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoCount,
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoPueTotal,
  ISNULL((SELECT COUNT(*) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoPueCount,
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoPpdTotal,
  ISNULL((SELECT COUNT(*) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0) AS CompradoPpdCount;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    return new ResumenMensualFila
                    {
                        FacturadoTotal = reader.GetDecimal(0),
                        FacturadoIva = reader.GetDecimal(1),
                        FacturadoCount = reader.GetInt32(2),
                        CompradoTotal = reader.GetDecimal(3),
                        CompradoIva = reader.GetDecimal(4),
                        CompradoCount = reader.GetInt32(5),
                        CompradoPueTotal = reader.GetDecimal(6),
                        CompradoPueCount = reader.GetInt32(7),
                        CompradoPpdTotal = reader.GetDecimal(8),
                        CompradoPpdCount = reader.GetInt32(9)
                    };
                }
            }
        }

        public static List<ProveedorResumenFila> ObtenerTopProveedores(SqlConnection conn, string rfc, int anio, int mes, int top = 10)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            string sql = $@"
SELECT TOP ({top}) RFCEmisor, MAX(NombreEmisor) AS Nombre, SUM(Total) AS Total, COUNT(*) AS Cantidad
FROM CfdiRecibido
WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0
  AND FechaEmision >= @Desde AND FechaEmision < @Hasta
GROUP BY RFCEmisor
ORDER BY Total DESC;";

            var resultado = new List<ProveedorResumenFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new ProveedorResumenFila
                        {
                            RFC = reader.GetString(0),
                            Nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Total = reader.GetDecimal(2),
                            Count = reader.GetInt32(3)
                        });
                    }
                }
            }
            return resultado;
        }

        public static List<ProveedorResumenFila> ObtenerTopClientes(SqlConnection conn, string rfc, int anio, int mes, int top = 10)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            string sql = $@"
SELECT TOP ({top}) RFCReceptor, NULL AS Nombre, SUM(Total) AS Total, COUNT(*) AS Cantidad
FROM CfdiRecibido
WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0
  AND FechaEmision >= @Desde AND FechaEmision < @Hasta
GROUP BY RFCReceptor
ORDER BY Total DESC;";

            var resultado = new List<ProveedorResumenFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new ProveedorResumenFila
                        {
                            RFC = reader.GetString(0),
                            Nombre = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Total = reader.GetDecimal(2),
                            Count = reader.GetInt32(3)
                        });
                    }
                }
            }
            return resultado;
        }

        public static List<CfdiMesFila> ObtenerCfdisMes(SqlConnection conn, string rfc, int anio, int mes)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            const string sql = @"
SELECT UUID, TipoComprobante,
       CASE WHEN RFCEmisor = @Rfc THEN 'Emitido' ELSE 'Recibido' END AS Flujo,
       RFCEmisor, NombreEmisor, RFCReceptor, Serie, Folio, FechaEmision,
       Subtotal, Descuento, IVA, Retenciones, Total, Moneda, FormaPago, MetodoPago, EstatusSat
FROM CfdiRecibido
WHERE (RFCEmisor = @Rfc OR RFCReceptor = @Rfc) AND Archivado = 0
  AND FechaEmision >= @Desde AND FechaEmision < @Hasta
ORDER BY FechaEmision DESC;";

            var resultado = new List<CfdiMesFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new CfdiMesFila
                        {
                            UUID = reader.GetGuid(0),
                            TipoComprobante = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Flujo = reader.GetString(2),
                            RFCEmisor = reader.GetString(3),
                            NombreEmisor = reader.IsDBNull(4) ? null : reader.GetString(4),
                            RFCReceptor = reader.GetString(5),
                            Serie = reader.IsDBNull(6) ? null : reader.GetString(6),
                            Folio = reader.IsDBNull(7) ? null : reader.GetString(7),
                            FechaEmision = reader.GetDateTime(8),
                            Subtotal = reader.IsDBNull(9) ? (decimal?)null : reader.GetDecimal(9),
                            Descuento = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10),
                            IVA = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                            Retenciones = reader.IsDBNull(12) ? (decimal?)null : reader.GetDecimal(12),
                            Total = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                            Moneda = reader.IsDBNull(14) ? null : reader.GetString(14),
                            FormaPago = reader.IsDBNull(15) ? null : reader.GetString(15),
                            MetodoPago = reader.IsDBNull(16) ? null : reader.GetString(16),
                            EstatusSat = reader.IsDBNull(17) ? null : reader.GetString(17)
                        });
                    }
                }
            }
            return resultado;
        }

        // Regla real del SAT (Anexo 20): MetodoPago=PUE debe traer una FormaPago especifica
        // (nunca "99 - Por definir", porque se paga de contado y ya se sabe como). MetodoPago=PPD
        // debe traer FormaPago="99" siempre (el pago real se declara despues via complemento de
        // pago) -- cualquier otro valor en PPD es una factura mal emitida por el proveedor.
        // Ademas: UsoCFDI=P01 (Por definir) ya fue derogado en CFDI 4.0 por el SAT.
        public static List<ErrorCoherenciaFila> ObtenerErroresCoherenciaPago(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT CfdiID, UUID, RFCEmisor, NombreEmisor, Serie, Folio, MetodoPago, FormaPago, Total, FechaEmision, UsoCFDI
FROM CfdiRecibido
WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0
  AND (
        (MetodoPago = 'PUE' AND FormaPago = '99')
     OR (MetodoPago = 'PPD' AND FormaPago IS NOT NULL AND FormaPago <> '99')
     OR (UsoCFDI = 'P01')
      )
ORDER BY FechaEmision DESC;";

            var resultado = new List<ErrorCoherenciaFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string metodoPago = reader.IsDBNull(6) ? null : reader.GetString(6);
                        string formaPago = reader.IsDBNull(7) ? null : reader.GetString(7);
                        string usoCfdi = reader.IsDBNull(10) ? null : reader.GetString(10);
                        string motivo;
                        if (usoCfdi == "P01")
                            motivo = "Uso CFDI P01 (Por definir): clave derogada e inválida en CFDI 4.0 según catálogo SAT";
                        else if (metodoPago == "PUE")
                            motivo = "PUE con Forma 99 (Violación Anexo 20: no deducible, debe declarar la forma de pago real de contado)";
                        else
                            motivo = "PPD con Forma " + formaPago + " (Violación Anexo 20: debe ser 99; el medio de pago se declara en el Complemento REP)";

                        resultado.Add(new ErrorCoherenciaFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            NombreEmisor = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Serie = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Folio = reader.IsDBNull(5) ? null : reader.GetString(5),
                            MetodoPago = metodoPago,
                            FormaPago = formaPago,
                            UsoCFDI = usoCfdi,
                            Total = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                            FechaEmision = reader.GetDateTime(9),
                            Motivo = motivo
                        });
                    }
                }
            }
            return resultado;
        }

        // Auditoría fiscal de facturas a crédito recibidas (PPD) con más de N días sin Complemento de Pago (REP).
        // Sin el REP del proveedor, el acreditamiento de IVA queda desprotegido ante revisiones del SAT.
        public static List<FacturaRiesgoRepFila> ObtenerFacturasRiesgoRep(SqlConnection conn, string rfc, int diasMinimos = 60)
        {
            const string sql = @"
SELECT c.CfdiID, c.UUID, c.RFCEmisor, c.NombreEmisor, ISNULL(c.Serie + ' ', '') + ISNULL(c.Folio, '') AS SerieFolio,
       c.FechaEmision, c.Total, ISNULL(c.IVA, 0) AS IVA, DATEDIFF(day, c.FechaEmision, GETDATE()) AS Dias
FROM CfdiRecibido c
WHERE c.RFCReceptor = @Rfc AND c.TipoComprobante = 'I' AND c.MetodoPago = 'PPD' AND c.Archivado = 0
  AND DATEDIFF(day, c.FechaEmision, GETDATE()) >= @DiasMinimos
  AND NOT EXISTS (SELECT 1 FROM CfdiPagoDocto p WHERE p.UUIDRelacionado = c.UUID)
ORDER BY c.FechaEmision ASC;";

            var resultado = new List<FacturaRiesgoRepFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@DiasMinimos", diasMinimos);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int dias = reader.GetInt32(8);
                        string riesgo = dias >= 180 ? "Crítico (> 180 días sin REP)" :
                                        dias >= 90 ? "Alto (> 90 días sin REP)" : "Moderado (> 60 días sin REP)";
                        resultado.Add(new FacturaRiesgoRepFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            NombreEmisor = reader.IsDBNull(3) ? null : reader.GetString(3),
                            SerieFolio = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                            FechaEmision = reader.GetDateTime(5),
                            Total = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                            IVA = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                            DiasTranscurridos = dias,
                            Riesgo = riesgo
                        });
                    }
                }
            }
            return resultado;
        }

        // Cédula fiscal y de conciliación de impuestos para auditoría: desglosa ventas y compras en PUE y PPD,
        // IVA cobrado vs efectivamente pagado, y retenciones.
        public static CedulaImpuestosMensualFila ObtenerCedulaImpuestos(SqlConnection conn, string rfc, int anio, int mes)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            const string sql = @"
SELECT
  ISNULL((SELECT SUM(Subtotal) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Subtotal) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Retenciones) FROM CfdiRecibido WHERE RFCEmisor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),

  ISNULL((SELECT SUM(Subtotal) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PUE' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Subtotal) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND MetodoPago = 'PPD' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Total) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(IVA) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0),
  ISNULL((SELECT SUM(Retenciones) FROM CfdiRecibido WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0 AND FechaEmision >= @Desde AND FechaEmision < @Hasta), 0);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    return new CedulaImpuestosMensualFila
                    {
                        VentasSubtotalPue = reader.GetDecimal(0),
                        VentasIvaPue = reader.GetDecimal(1),
                        VentasSubtotalPpd = reader.GetDecimal(2),
                        VentasIvaPpd = reader.GetDecimal(3),
                        VentasTotal = reader.GetDecimal(4),
                        VentasIvaTotal = reader.GetDecimal(5),
                        RetencionesEmitidas = reader.GetDecimal(6),

                        ComprasSubtotalPue = reader.GetDecimal(7),
                        ComprasIvaPue = reader.GetDecimal(8),
                        ComprasSubtotalPpd = reader.GetDecimal(9),
                        ComprasIvaPpd = reader.GetDecimal(10),
                        ComprasTotal = reader.GetDecimal(11),
                        ComprasIvaTotal = reader.GetDecimal(12),
                        RetencionesRecibidas = reader.GetDecimal(13)
                    };
                }
            }
        }

        // Cuentas por pagar segun el SAT: para cada factura PPD, suma ImpPagado de todos sus
        // complementos de pago (cruzados por UUIDRelacionado) y TAMBIEN resta el total de
        // cualquier nota de credito (Egreso, TipoRelacion=01 en CfdiRelacion) que la tenga como
        // documento relacionado -- una NC reduce el saldo igual que un pago, aunque no venga con
        // complemento de pago propio. Se queda solo con las que tienen saldo pendiente real:
        // incluye las que nunca recibieron complemento (Pagado=0) Y las que recibieron uno o mas
        // pero quedaron con pago parcial. Pedido explicito del usuario (2026-08-18): "en la de
        // cuentas por pagar puede ser que tambien tenga asociada una nota de credito, esas debes
        // tomarlas en cuenta por si existen".
        public static List<CuentaPorPagarFila> ObtenerCuentasPorPagar(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT * FROM (
  SELECT c.CfdiID, c.UUID, c.RFCEmisor, c.NombreEmisor, c.Serie, c.Folio, c.Total, c.FechaEmision,
         ISNULL((SELECT SUM(p.ImpPagado) FROM CfdiPagoDocto p WHERE p.UUIDRelacionado = c.UUID), 0) AS Pagado,
         (SELECT COUNT(*) FROM CfdiPagoDocto p WHERE p.UUIDRelacionado = c.UUID) AS NumComplementos,
         ISNULL((SELECT SUM(nc.Total) FROM CfdiRelacion r JOIN CfdiRecibido nc ON nc.UUID = r.UUID
                 WHERE r.UUIDRelacionado = c.UUID AND r.TipoRelacion = '01' AND nc.Archivado = 0), 0) AS NotaCreditoTotal
  FROM CfdiRecibido c
  WHERE c.RFCReceptor = @Rfc AND c.TipoComprobante = 'I' AND c.MetodoPago = 'PPD' AND c.Archivado = 0
) t
WHERE (t.Total - t.Pagado - t.NotaCreditoTotal) > 0.01
ORDER BY t.FechaEmision ASC;";

            var resultado = new List<CuentaPorPagarFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var fecha = reader.GetDateTime(7);
                        decimal total = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
                        decimal pagado = reader.GetDecimal(8);
                        decimal notaCredito = reader.GetDecimal(10);
                        resultado.Add(new CuentaPorPagarFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            NombreEmisor = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Serie = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Folio = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Total = total,
                            Pagado = pagado,
                            NotaCreditoTotal = notaCredito,
                            Saldo = total - pagado - notaCredito,
                            NumComplementos = reader.GetInt32(9),
                            FechaEmision = fecha,
                            DiasTranscurridos = (int)(DateTime.Today - fecha.Date).TotalDays
                        });
                    }
                }
            }
            return resultado;
        }

        // Espejo de ObtenerCuentasPorPagar pero del lado de VENTAS (Emitidos, RFCEmisor=rfc) --
        // pedido explicito del usuario 2026-08-18: "en los reportes falta cuentas por cobrar".
        // Misma logica: factura PPD emitida menos lo ya cobrado (complementos de pago) menos
        // notas de credito relacionadas: si queda saldo positivo, sigue pendiente de cobro.
        public static List<CuentaPorCobrarFila> ObtenerCuentasPorCobrar(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT * FROM (
  SELECT c.CfdiID, c.UUID, c.RFCReceptor, c.Serie, c.Folio, c.Total, c.FechaEmision,
         ISNULL((SELECT SUM(p.ImpPagado) FROM CfdiPagoDocto p WHERE p.UUIDRelacionado = c.UUID), 0) AS Pagado,
         (SELECT COUNT(*) FROM CfdiPagoDocto p WHERE p.UUIDRelacionado = c.UUID) AS NumComplementos,
         ISNULL((SELECT SUM(nc.Total) FROM CfdiRelacion r JOIN CfdiRecibido nc ON nc.UUID = r.UUID
                 WHERE r.UUIDRelacionado = c.UUID AND r.TipoRelacion = '01' AND nc.Archivado = 0), 0) AS NotaCreditoTotal
  FROM CfdiRecibido c
  WHERE c.RFCEmisor = @Rfc AND c.TipoComprobante = 'I' AND c.MetodoPago = 'PPD' AND c.Archivado = 0
) t
WHERE (t.Total - t.Pagado - t.NotaCreditoTotal) > 0.01
ORDER BY t.FechaEmision ASC;";

            var resultado = new List<CuentaPorCobrarFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var fecha = reader.GetDateTime(6);
                        decimal total = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
                        decimal pagado = reader.GetDecimal(7);
                        decimal notaCredito = reader.GetDecimal(9);
                        resultado.Add(new CuentaPorCobrarFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCReceptor = reader.GetString(2),
                            Serie = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Folio = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Total = total,
                            Pagado = pagado,
                            NotaCreditoTotal = notaCredito,
                            Saldo = total - pagado - notaCredito,
                            NumComplementos = reader.GetInt32(8),
                            FechaEmision = fecha,
                            DiasTranscurridos = (int)(DateTime.Today - fecha.Date).TotalDays
                        });
                    }
                }
            }
            return resultado;
        }

        // CfdiRelacion.UUID = UUID del propio CFDI que trae el nodo <CfdiRelacionados> (la nota de
        // credito), UUIDRelacionado = el CFDI original al que hace referencia -- asi lo define el
        // estandar CFDI (el documento nuevo es el que declara la relacion, no al reves).
        // TipoRelacion="01" = "Nota de credito de los documentos relacionados" (catalogo c_TipoRelacion
        // del SAT). LEFT JOIN al original porque puede no estar en nuestra BD (p.ej. si la factura
        // original se archivo/elimino, o nunca se descargo).
        public static List<NotaCreditoFila> ObtenerNotasCreditoRelacionadas(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT nc.UUID, nc.Serie, nc.Folio, nc.Total, nc.FechaEmision,
       r.UUIDRelacionado, orig.Serie, orig.Folio, orig.Total, CASE WHEN orig.CfdiID IS NULL THEN 0 ELSE 1 END
FROM CfdiRelacion r
JOIN CfdiRecibido nc ON nc.UUID = r.UUID
LEFT JOIN CfdiRecibido orig ON orig.UUID = r.UUIDRelacionado
WHERE nc.RFCReceptor = @Rfc AND r.TipoRelacion = '01'
ORDER BY nc.FechaEmision DESC;";

            var resultado = new List<NotaCreditoFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new NotaCreditoFila
                        {
                            UUIDNotaCredito = reader.GetGuid(0),
                            SerieNotaCredito = reader.IsDBNull(1) ? null : reader.GetString(1),
                            FolioNotaCredito = reader.IsDBNull(2) ? null : reader.GetString(2),
                            TotalNotaCredito = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                            FechaNotaCredito = reader.GetDateTime(4),
                            UUIDOriginal = reader.GetGuid(5),
                            SerieOriginal = reader.IsDBNull(6) ? null : reader.GetString(6),
                            FolioOriginal = reader.IsDBNull(7) ? null : reader.GetString(7),
                            TotalOriginal = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                            OriginalEnBd = reader.GetInt32(9) == 1
                        });
                    }
                }
            }
            return resultado;
        }

        public static List<UsoCfdiResumenFila> ObtenerUsosCfdiResumen(SqlConnection conn, string rfc, int anio, int mes)
        {
            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            // TipoComprobante='I' -- excluye los complementos de pago (TipoComprobante='P', que
            // siempre traen UsoCFDI='CP01' y no representan un uso real de compra, solo el pago
            // de una factura ya contada aparte) y las notas de credito (Egreso, ya se reportan en
            // su propia pestaña). Pedido explicito del usuario: "no necesitas tomar en cuenta el
            // uso de cfdi de pago".
            const string sql = @"
SELECT ISNULL(UsoCFDI, '(sin dato)') AS Uso, COUNT(*) AS Cantidad, SUM(Total) AS Total
FROM CfdiRecibido
WHERE RFCReceptor = @Rfc AND TipoComprobante = 'I' AND Archivado = 0
  AND FechaEmision >= @Desde AND FechaEmision < @Hasta
GROUP BY UsoCFDI
ORDER BY Total DESC;";

            var resultado = new List<UsoCfdiResumenFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new UsoCfdiResumenFila
                        {
                            UsoCFDI = reader.GetString(0),
                            Count = reader.GetInt32(1),
                            Total = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)
                        });
                    }
                }
            }
            return resultado;
        }
    }
}
