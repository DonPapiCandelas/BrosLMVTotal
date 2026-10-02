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

// ExcelReporteEjecutivo.cs -- Generador corporativo de reportes ejecutivos en Excel (.xlsx)
// utilizando ClosedXML. Diseñado para ofrecer una presentación ejecutiva impecable:
// formato numérico y monetario real (sin advertencias de texto de Excel), fórmulas nativas
// (=SUM), tablas con autofiltro y totales automáticos, paneles congelados y paleta corporativa
// coherente (#15324F / #2D6FE0 / #16A34A / #DC2626).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI
{
    public static class ExcelReporteEjecutivo
    {
        private const string FormatoMoneda = "$#,##0.00;($#,##0.00);\"$0.00\"";
        private const string FormatoEntero = "#,##0";
        private const string FormatoPorcentaje = "0.0%";
        private const string FormatoFecha = "yyyy-MM-dd";
        private const string FormatoFechaHora = "yyyy-MM-dd HH:mm";

        private static readonly XLColor ColorAzulOscuro = XLColor.FromHtml("#15324F");
        private static readonly XLColor ColorAzulMedio = XLColor.FromHtml("#24558A");
        private static readonly XLColor ColorAzulPrimario = XLColor.FromHtml("#2D6FE0");
        private static readonly XLColor ColorFondoGris = XLColor.FromHtml("#F8FAFC");
        private static readonly XLColor ColorBordeGris = XLColor.FromHtml("#CBD5E1");
        private static readonly XLColor ColorTextoOscuro = XLColor.FromHtml("#16263A");
        private static readonly XLColor ColorTextoMuted = XLColor.FromHtml("#64748B");
        private static readonly XLColor ColorVerdeExito = XLColor.FromHtml("#16A34A");
        private static readonly XLColor ColorRojoAlerta = XLColor.FromHtml("#DC2626");

        // Diccionario c_UsoCFDI para descripciones claras en reportes ejecutivos
        private static readonly Dictionary<string, string> DescripcionesUso = new Dictionary<string, string>
        {
            ["G01"] = "Adquisición de mercancías",
            ["G02"] = "Devoluciones, descuentos o bonificaciones",
            ["G03"] = "Gastos en general",
            ["I01"] = "Construcciones",
            ["I02"] = "Mobiliario y equipo de oficina",
            ["I03"] = "Equipo de transporte",
            ["I04"] = "Equipo de cómputo",
            ["I05"] = "Dados, troqueles, moldes, matrices",
            ["I06"] = "Comunicaciones telefónicas",
            ["I07"] = "Comunicaciones satelitales",
            ["I08"] = "Otra maquinaria y equipo",
            ["D01"] = "Honorarios médicos y hospitalarios",
            ["D02"] = "Gastos médicos por incapacidad",
            ["D03"] = "Gastos funerales",
            ["D04"] = "Donativos",
            ["D05"] = "Intereses por créditos hipotecarios",
            ["D06"] = "Aportaciones al SAR",
            ["D07"] = "Primas seguro gastos médicos",
            ["D08"] = "Transportación escolar",
            ["D09"] = "Cuentas ahorro para el retiro",
            ["D10"] = "Colegiaturas",
            ["S01"] = "Sin efectos fiscales",
            ["CP01"] = "Pagos",
            ["CN01"] = "Nómina",
            ["P01"] = "Por definir"
        };

        /// <summary>
        /// Genera el libro ejecutivo completo con todas las pestañas financieras y de auditoría.
        /// </summary>
        public static void GenerarReporteCompleto(
            string rutaArchivo,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            int anio,
            int mes)
        {
            var cultureMx = new CultureInfo("es-MX");
            var fechaPeriodo = new DateTime(anio, mes, 1);
            string periodoTexto = fechaPeriodo.ToString("MMMM yyyy", cultureMx).ToUpper();

            using var libro = new XLWorkbook();

            // 1. Resumen Ejecutivo
            ConstruirHojaResumen(libro, conn, rfc, nombreEmpresa, anio, mes, periodoTexto);

            // 2. Auditoría Fiscal & IVA (Cédula de conciliación impositiva y riesgo REP)
            ConstruirHojaAuditoriaFiscal(libro, conn, rfc, nombreEmpresa, anio, mes, periodoTexto);

            // 3. Cuentas por Cobrar (CxC)
            ConstruirHojaCuentasPorCobrar(libro, conn, rfc, nombreEmpresa, periodoTexto);

            // 4. Cuentas por Pagar (CxP)
            ConstruirHojaCuentasPorPagar(libro, conn, rfc, nombreEmpresa, periodoTexto);

            // 5. Inconsistencias Fiscales SAT
            ConstruirHojaInconsistencias(libro, conn, rfc, nombreEmpresa, periodoTexto);

            // 6. Notas de Crédito
            ConstruirHojaNotasCredito(libro, conn, rfc, nombreEmpresa, periodoTexto);

            // 7. Detalle de CFDIs del Mes
            ConstruirHojaCfdisMes(libro, conn, rfc, nombreEmpresa, anio, mes, periodoTexto);

            libro.SaveAs(rutaArchivo);
        }

        private static void AplicarEncabezadoCorporativo(
            IXLWorksheet ws,
            string titulo,
            string subtitulo,
            string nombreEmpresa,
            string rfc,
            string periodo,
            int totalColumnas)
        {
            ws.ShowGridLines = true;

            // Fila 1: Título de Marca y Reporte
            ws.Cell("A1").Value = "BrosLMV Descargas · " + titulo;
            ws.Range(1, 1, 1, totalColumnas).Merge();
            var estiloFila1 = ws.Range(1, 1, 1, totalColumnas).Style;
            estiloFila1.Fill.BackgroundColor = ColorAzulOscuro;
            estiloFila1.Font.FontColor = XLColor.White;
            estiloFila1.Font.Bold = true;
            estiloFila1.Font.FontSize = 15;
            estiloFila1.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 32;

            // Fila 2: Empresa y RFC
            ws.Cell("A2").Value = nombreEmpresa + "  ·  RFC: " + rfc;
            ws.Range(2, 1, 2, totalColumnas).Merge();
            var estiloFila2 = ws.Range(2, 1, 2, totalColumnas).Style;
            estiloFila2.Fill.BackgroundColor = ColorAzulMedio;
            estiloFila2.Font.FontColor = XLColor.White;
            estiloFila2.Font.Bold = true;
            estiloFila2.Font.FontSize = 11;
            estiloFila2.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(2).Height = 22;

            // Fila 3: Metadatos y Período
            ws.Cell("A3").Value = subtitulo + "  |  Período: " + periodo + "  |  Generado: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            ws.Range(3, 1, 3, totalColumnas).Merge();
            var estiloFila3 = ws.Range(3, 1, 3, totalColumnas).Style;
            estiloFila3.Fill.BackgroundColor = ColorFondoGris;
            estiloFila3.Font.FontColor = ColorTextoMuted;
            estiloFila3.Font.Italic = true;
            estiloFila3.Font.FontSize = 9.5;
            estiloFila3.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            estiloFila3.Border.BottomBorder = XLBorderStyleValues.Thin;
            estiloFila3.Border.BottomBorderColor = ColorBordeGris;
            ws.Row(3).Height = 20;

            ws.Row(4).Height = 12; // Espacio visual
        }

        private static void ConstruirHojaResumen(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            int anio,
            int mes,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Resumen Ejecutivo");
            AplicarEncabezadoCorporativo(ws, "Informe Financiero Ejecutivo", "Resumen de operaciones, compras y balance fiscal", nombreEmpresa, rfc, periodoTexto, 8);

            var r = ReportesSql.ObtenerResumenMensual(conn, rfc, anio, mes);
            decimal balanceNeto = r.FacturadoTotal - r.CompradoTotal;
            decimal balanceIva = r.FacturadoIva - r.CompradoIva;

            // Bloque KPI en Fila 5 a 10
            // Card 1: Facturación Emitida
            ws.Range("A5:B5").Merge().Value = "FACTURACIÓN (VENTAS)";
            ws.Range("A5:B5").Style.Font.Bold = true;
            ws.Range("A5:B5").Style.Font.FontSize = 10;
            ws.Range("A5:B5").Style.Font.FontColor = ColorTextoMuted;

            ws.Range("A6:B6").Merge().Value = r.FacturadoTotal;
            ws.Range("A6:B6").Style.NumberFormat.Format = FormatoMoneda;
            ws.Range("A6:B6").Style.Font.Bold = true;
            ws.Range("A6:B6").Style.Font.FontSize = 17;
            ws.Range("A6:B6").Style.Font.FontColor = ColorTextoOscuro;

            ws.Cell("A7").Value = "IVA Trasladado:";
            ws.Cell("A7").Style.Font.FontSize = 9;
            ws.Cell("A7").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("B7").Value = r.FacturadoIva;
            ws.Cell("B7").Style.NumberFormat.Format = FormatoMoneda;
            ws.Cell("B7").Style.Font.FontSize = 9;

            ws.Cell("A8").Value = "Comprobantes:";
            ws.Cell("A8").Style.Font.FontSize = 9;
            ws.Cell("A8").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("B8").Value = r.FacturadoCount;
            ws.Cell("B8").Style.NumberFormat.Format = FormatoEntero;
            ws.Cell("B8").Style.Font.FontSize = 9;

            ws.Range("A5:B8").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range("A5:B8").Style.Border.OutsideBorderColor = ColorAzulPrimario;
            ws.Range("A5:B8").Style.Fill.BackgroundColor = XLColor.FromHtml("#F0F6FF");

            // Card 2: Compras y Gastos Recibidos
            ws.Range("C5:D5").Merge().Value = "COMPRAS Y GASTOS";
            ws.Range("C5:D5").Style.Font.Bold = true;
            ws.Range("C5:D5").Style.Font.FontSize = 10;
            ws.Range("C5:D5").Style.Font.FontColor = ColorTextoMuted;

            ws.Range("C6:D6").Merge().Value = r.CompradoTotal;
            ws.Range("C6:D6").Style.NumberFormat.Format = FormatoMoneda;
            ws.Range("C6:D6").Style.Font.Bold = true;
            ws.Range("C6:D6").Style.Font.FontSize = 17;
            ws.Range("C6:D6").Style.Font.FontColor = ColorTextoOscuro;

            ws.Cell("C7").Value = "IVA Acreditable:";
            ws.Cell("C7").Style.Font.FontSize = 9;
            ws.Cell("C7").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("D7").Value = r.CompradoIva;
            ws.Cell("D7").Style.NumberFormat.Format = FormatoMoneda;
            ws.Cell("D7").Style.Font.FontSize = 9;

            ws.Cell("C8").Value = "Comprobantes:";
            ws.Cell("C8").Style.Font.FontSize = 9;
            ws.Cell("C8").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("D8").Value = r.CompradoCount;
            ws.Cell("D8").Style.NumberFormat.Format = FormatoEntero;
            ws.Cell("D8").Style.Font.FontSize = 9;

            ws.Range("C5:D8").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range("C5:D8").Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            ws.Range("C5:D8").Style.Fill.BackgroundColor = ColorFondoGris;

            // Card 3: Balance Operativo Neto
            ws.Range("E5:F5").Merge().Value = "BALANCE OPERATIVO NETO";
            ws.Range("E5:F5").Style.Font.Bold = true;
            ws.Range("E5:F5").Style.Font.FontSize = 10;
            ws.Range("E5:F5").Style.Font.FontColor = ColorTextoMuted;

            ws.Range("E6:F6").Merge().Value = balanceNeto;
            ws.Range("E6:F6").Style.NumberFormat.Format = FormatoMoneda;
            ws.Range("E6:F6").Style.Font.Bold = true;
            ws.Range("E6:F6").Style.Font.FontSize = 17;
            ws.Range("E6:F6").Style.Font.FontColor = balanceNeto >= 0 ? ColorVerdeExito : ColorRojoAlerta;

            ws.Cell("E7").Value = "Diferencial IVA:";
            ws.Cell("E7").Style.Font.FontSize = 9;
            ws.Cell("E7").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("F7").Value = balanceIva;
            ws.Cell("F7").Style.NumberFormat.Format = FormatoMoneda;
            ws.Cell("F7").Style.Font.FontSize = 9;
            ws.Cell("F7").Style.Font.FontColor = balanceIva >= 0 ? ColorVerdeExito : ColorRojoAlerta;

            ws.Cell("E8").Value = "Situación:";
            ws.Cell("E8").Style.Font.FontSize = 9;
            ws.Cell("E8").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("F8").Value = balanceNeto >= 0 ? "Superávit mensual" : "Déficit mensual";
            ws.Cell("F8").Style.Font.Bold = true;
            ws.Cell("F8").Style.Font.FontSize = 9;
            ws.Cell("F8").Style.Font.FontColor = balanceNeto >= 0 ? ColorVerdeExito : ColorRojoAlerta;

            ws.Range("E5:F8").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range("E5:F8").Style.Border.OutsideBorderColor = balanceNeto >= 0 ? ColorVerdeExito : ColorRojoAlerta;
            ws.Range("E5:F8").Style.Fill.BackgroundColor = balanceNeto >= 0 ? XLColor.FromHtml("#F0FDF4") : XLColor.FromHtml("#FEF2F2");

            // Card 4: Modalidad de Pago de Compras (PUE vs PPD)
            ws.Range("G5:H5").Merge().Value = "CONDICIÓN DE PAGO (COMPRAS)";
            ws.Range("G5:H5").Style.Font.Bold = true;
            ws.Range("G5:H5").Style.Font.FontSize = 10;
            ws.Range("G5:H5").Style.Font.FontColor = ColorTextoMuted;

            ws.Cell("G6").Value = "Contado (PUE):";
            ws.Cell("G6").Style.Font.FontSize = 9.5;
            ws.Cell("H6").Value = r.CompradoPueTotal;
            ws.Cell("H6").Style.NumberFormat.Format = FormatoMoneda;
            ws.Cell("H6").Style.Font.Bold = true;
            ws.Cell("H6").Style.Font.FontSize = 10;

            ws.Cell("G7").Value = "Crédito (PPD):";
            ws.Cell("G7").Style.Font.FontSize = 9.5;
            ws.Cell("H7").Value = r.CompradoPpdTotal;
            ws.Cell("H7").Style.NumberFormat.Format = FormatoMoneda;
            ws.Cell("H7").Style.Font.Bold = true;
            ws.Cell("H7").Style.Font.FontSize = 10;

            decimal totalCompradoBase = (r.CompradoPueTotal + r.CompradoPpdTotal);
            decimal pctPpd = totalCompradoBase > 0 ? (r.CompradoPpdTotal / totalCompradoBase) : 0m;
            ws.Cell("G8").Value = "% A Crédito:";
            ws.Cell("G8").Style.Font.FontSize = 9;
            ws.Cell("G8").Style.Font.FontColor = ColorTextoMuted;
            ws.Cell("H8").Value = pctPpd;
            ws.Cell("H8").Style.NumberFormat.Format = FormatoPorcentaje;
            ws.Cell("H8").Style.Font.FontSize = 9;

            ws.Range("G5:H8").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range("G5:H8").Style.Border.OutsideBorderColor = ColorBordeGris;
            ws.Range("G5:H8").Style.Fill.BackgroundColor = ColorFondoGris;

            // Tabla 1: Top Proveedores del Mes
            int filaInicioProveedores = 11;
            ws.Cell(filaInicioProveedores, 1).Value = "TOP PROVEEDORES DEL MES (MAYOR IMPACTO DE COMPRA)";
            ws.Range(filaInicioProveedores, 1, filaInicioProveedores, 5).Merge().Style.Font.Bold = true;
            ws.Cell(filaInicioProveedores, 1).Style.Font.FontSize = 11;
            ws.Cell(filaInicioProveedores, 1).Style.Font.FontColor = ColorAzulOscuro;

            int filaProvH = filaInicioProveedores + 1;
            string[] headersProv = { "RFC Proveedor", "Nombre / Razón Social", "Facturas", "Importe Total", "% Compras" };
            for (int i = 0; i < headersProv.Length; i++)
                ws.Cell(filaProvH, i + 1).Value = headersProv[i];

            var topProv = ReportesSql.ObtenerTopProveedores(conn, rfc, anio, mes, top: 10);
            int currRow = filaProvH + 1;
            for (int i = 0; i < topProv.Count; i++)
            {
                var p = topProv[i];
                ws.Cell(currRow, 1).Value = p.RFC;
                ws.Cell(currRow, 2).Value = p.Nombre ?? "(Sin nombre en XML)";
                ws.Cell(currRow, 3).Value = p.Count;
                ws.Cell(currRow, 3).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(currRow, 4).Value = p.Total;
                ws.Cell(currRow, 4).Style.NumberFormat.Format = FormatoMoneda;
                decimal pct = r.CompradoTotal > 0 ? (p.Total / r.CompradoTotal) : 0m;
                ws.Cell(currRow, 5).Value = pct;
                ws.Cell(currRow, 5).Style.NumberFormat.Format = FormatoPorcentaje;
                currRow++;
            }

            if (topProv.Count > 0)
            {
                var tablaProv = ws.Range(filaProvH, 1, currRow - 1, 5).CreateTable("TopProveedores");
                tablaProv.Theme = XLTableTheme.TableStyleMedium2;
            }

            // Tabla 2: Top Clientes del Mes
            int filaInicioClientes = currRow + 2;
            ws.Cell(filaInicioClientes, 1).Value = "TOP CLIENTES DEL MES (FACTURACIÓN EMITIDA)";
            ws.Range(filaInicioClientes, 1, filaInicioClientes, 5).Merge().Style.Font.Bold = true;
            ws.Cell(filaInicioClientes, 1).Style.Font.FontSize = 11;
            ws.Cell(filaInicioClientes, 1).Style.Font.FontColor = ColorAzulOscuro;

            int filaCliH = filaInicioClientes + 1;
            string[] headersCli = { "RFC Cliente", "Razón Social", "Facturas", "Importe Facturado", "% Ventas" };
            for (int i = 0; i < headersCli.Length; i++)
                ws.Cell(filaCliH, i + 1).Value = headersCli[i];

            var topCli = ReportesSql.ObtenerTopClientes(conn, rfc, anio, mes, top: 10);
            currRow = filaCliH + 1;
            for (int i = 0; i < topCli.Count; i++)
            {
                var c = topCli[i];
                ws.Cell(currRow, 1).Value = c.RFC;
                ws.Cell(currRow, 2).Value = c.Nombre ?? c.RFC;
                ws.Cell(currRow, 3).Value = c.Count;
                ws.Cell(currRow, 3).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(currRow, 4).Value = c.Total;
                ws.Cell(currRow, 4).Style.NumberFormat.Format = FormatoMoneda;
                decimal pct = r.FacturadoTotal > 0 ? (c.Total / r.FacturadoTotal) : 0m;
                ws.Cell(currRow, 5).Value = pct;
                ws.Cell(currRow, 5).Style.NumberFormat.Format = FormatoPorcentaje;
                currRow++;
            }

            if (topCli.Count > 0)
            {
                var tablaCli = ws.Range(filaCliH, 1, currRow - 1, 5).CreateTable("TopClientes");
                tablaCli.Theme = XLTableTheme.TableStyleMedium2;
            }

            // Tabla 3: Resumen por Uso de CFDI
            int filaInicioUsos = currRow + 2;
            ws.Cell(filaInicioUsos, 1).Value = "DISTRIBUCIÓN DE COMPRAS POR USO DE CFDI (SAT)";
            ws.Range(filaInicioUsos, 1, filaInicioUsos, 5).Merge().Style.Font.Bold = true;
            ws.Cell(filaInicioUsos, 1).Style.Font.FontSize = 11;
            ws.Cell(filaInicioUsos, 1).Style.Font.FontColor = ColorAzulOscuro;

            int filaUsoH = filaInicioUsos + 1;
            string[] headersUso = { "Clave", "Descripción SAT", "Facturas", "Total Compras", "% Participación" };
            for (int i = 0; i < headersUso.Length; i++)
                ws.Cell(filaUsoH, i + 1).Value = headersUso[i];

            var usos = ReportesSql.ObtenerUsosCfdiResumen(conn, rfc, anio, mes);
            currRow = filaUsoH + 1;
            for (int i = 0; i < usos.Count; i++)
            {
                var u = usos[i];
                string desc = DescripcionesUso.TryGetValue(u.UsoCFDI, out var d) ? d : "(Otro concepto)";
                ws.Cell(currRow, 1).Value = u.UsoCFDI;
                ws.Cell(currRow, 2).Value = desc;
                ws.Cell(currRow, 3).Value = u.Count;
                ws.Cell(currRow, 3).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(currRow, 4).Value = u.Total;
                ws.Cell(currRow, 4).Style.NumberFormat.Format = FormatoMoneda;
                decimal pct = r.CompradoTotal > 0 ? (u.Total / r.CompradoTotal) : 0m;
                ws.Cell(currRow, 5).Value = pct;
                ws.Cell(currRow, 5).Style.NumberFormat.Format = FormatoPorcentaje;
                currRow++;
            }

            if (usos.Count > 0)
            {
                var tablaUso = ws.Range(filaUsoH, 1, currRow - 1, 5).CreateTable("DistribucionUsos");
                tablaUso.Theme = XLTableTheme.TableStyleLight9;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 18;
            ws.Column(2).Width = Math.Min(45, Math.Max(28, ws.Column(2).Width));
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 16;
            ws.Column(6).Width = 20;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 20;
            ws.SheetView.FreezeRows(3);
        }

        private static void ConstruirHojaAuditoriaFiscal(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            int anio,
            int mes,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Auditoría Fiscal e IVA");
            AplicarEncabezadoCorporativo(ws, "Cédula de Auditoría Fiscal e Impuestos", "Conciliación de flujo efectivamente erogado/cobrado e identificación de riesgos fiscales", nombreEmpresa, rfc, periodoTexto, 9);

            var c = ReportesSql.ObtenerCedulaImpuestos(conn, rfc, anio, mes);
            var riesgoRep = ReportesSql.ObtenerFacturasRiesgoRep(conn, rfc, diasMinimos: 60);

            // Bloque 1: Resumen Impositivo
            ws.Cell("A5").Value = "1. CONCILIACIÓN DE IVA Y FLUJO DE EFECTIVO DEL MES";
            ws.Range("A5:I5").Merge();
            ws.Range("A5:I5").Style.Font.Bold = true;
            ws.Range("A5:I5").Style.Fill.BackgroundColor = ColorAzulMedio;
            ws.Range("A5:I5").Style.Font.FontColor = XLColor.White;

            string[] headersConcil = { "Concepto Impositivo", "Base Gravable PUE (Efectivo)", "IVA 16% (Cobrado/Pagado)", "Base PPD (Crédito)", "IVA PPD (Diferido)", "Total Comprobantes", "IVA Total Declarado", "Retenciones ISR", "Retenciones IVA" };
            int fH = 6;
            for (int i = 0; i < headersConcil.Length; i++)
            {
                ws.Cell(fH, i + 1).Value = headersConcil[i];
                ws.Cell(fH, i + 1).Style.Font.Bold = true;
                ws.Cell(fH, i + 1).Style.Fill.BackgroundColor = ColorFondoGris;
            }

            // Fila Ventas
            ws.Cell(7, 1).Value = "Ventas Emitidas (Ingresos)";
            ws.Cell(7, 2).Value = c.VentasSubtotalPue;
            ws.Cell(7, 3).Value = c.VentasIvaPue;
            ws.Cell(7, 4).Value = c.VentasSubtotalPpd;
            ws.Cell(7, 5).Value = c.VentasIvaPpd;
            ws.Cell(7, 6).Value = c.VentasTotal;
            ws.Cell(7, 7).Value = c.VentasIvaTotal;
            ws.Cell(7, 8).Value = c.RetencionesEmitidas;
            ws.Cell(7, 9).Value = 0m;

            // Fila Compras
            ws.Cell(8, 1).Value = "Compras Recibidas (Egresos)";
            ws.Cell(8, 2).Value = c.ComprasSubtotalPue;
            ws.Cell(8, 3).Value = c.ComprasIvaPue;
            ws.Cell(8, 4).Value = c.ComprasSubtotalPpd;
            ws.Cell(8, 5).Value = c.ComprasIvaPpd;
            ws.Cell(8, 6).Value = c.ComprasTotal;
            ws.Cell(8, 7).Value = c.ComprasIvaTotal;
            ws.Cell(8, 8).Value = c.RetencionesRecibidas;
            ws.Cell(8, 9).Value = 0m;

            // Fila Diferencial
            ws.Cell(9, 1).Value = "Diferencial de Flujo Efectivo (A cargo / A favor)";
            ws.Cell(9, 1).Style.Font.Bold = true;
            ws.Cell(9, 2).FormulaA1 = "=B7-B8";
            ws.Cell(9, 3).FormulaA1 = "=C7-C8";
            ws.Cell(9, 3).Style.Font.Bold = true;
            ws.Cell(9, 4).FormulaA1 = "=D7-D8";
            ws.Cell(9, 5).FormulaA1 = "=E7-E8";
            ws.Cell(9, 6).FormulaA1 = "=F7-F8";
            ws.Cell(9, 7).FormulaA1 = "=G7-G8";
            ws.Cell(9, 8).FormulaA1 = "=H7-H8";
            ws.Cell(9, 9).FormulaA1 = "=I7-I8";

            ws.Range("B7:I9").Style.NumberFormat.Format = FormatoMoneda;
            ws.Range("A6:I9").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range("A6:I9").Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // Bloque 2: Facturas en riesgo de pérdida de acreditamiento (PPD sin REP > 60 días)
            int filaRiesgoInicio = 12;
            ws.Cell(filaRiesgoInicio, 1).Value = "2. FACTURAS RECIBIDAS A CRÉDITO SIN COMPLEMENTO DE PAGO (REP PENDIENTE > 60 DÍAS)";
            ws.Range(filaRiesgoInicio, 1, filaRiesgoInicio, 9).Merge();
            ws.Range(filaRiesgoInicio, 1, filaRiesgoInicio, 9).Style.Font.Bold = true;
            ws.Range(filaRiesgoInicio, 1, filaRiesgoInicio, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#DC2626");
            ws.Range(filaRiesgoInicio, 1, filaRiesgoInicio, 9).Style.Font.FontColor = XLColor.White;

            int filaRiesgoHeader = filaRiesgoInicio + 1;
            string[] headersRiesgo = { "UUID", "RFC Proveedor", "Proveedor", "Serie / Folio", "Fecha Emisión", "Total Factura", "IVA en Riesgo", "Días Venc.", "Nivel de Riesgo" };
            for (int i = 0; i < headersRiesgo.Length; i++)
            {
                ws.Cell(filaRiesgoHeader, i + 1).Value = headersRiesgo[i];
                ws.Cell(filaRiesgoHeader, i + 1).Style.Font.Bold = true;
                ws.Cell(filaRiesgoHeader, i + 1).Style.Fill.BackgroundColor = ColorFondoGris;
            }

            int curr = filaRiesgoHeader + 1;
            for (int i = 0; i < riesgoRep.Count; i++)
            {
                var rItem = riesgoRep[i];
                ws.Cell(curr, 1).Value = rItem.UUID.ToString();
                ws.Cell(curr, 2).Value = rItem.RFCEmisor;
                ws.Cell(curr, 3).Value = rItem.NombreEmisor ?? "";
                ws.Cell(curr, 4).Value = rItem.SerieFolio;
                ws.Cell(curr, 5).Value = rItem.FechaEmision;
                ws.Cell(curr, 5).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(curr, 6).Value = rItem.Total;
                ws.Cell(curr, 6).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(curr, 7).Value = rItem.IVA;
                ws.Cell(curr, 7).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(curr, 7).Style.Font.Bold = true;
                ws.Cell(curr, 7).Style.Font.FontColor = ColorRojoAlerta;
                ws.Cell(curr, 8).Value = rItem.DiasTranscurridos;
                ws.Cell(curr, 8).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(curr, 9).Value = rItem.Riesgo;
                curr++;
            }

            if (riesgoRep.Count > 0)
            {
                var tablaRiesgo = ws.Range(filaRiesgoHeader, 1, curr - 1, headersRiesgo.Length).CreateTable("FacturasRiesgoREP");
                tablaRiesgo.Theme = XLTableTheme.TableStyleMedium3;
                tablaRiesgo.ShowTotalsRow = true;
                tablaRiesgo.Field("Total Factura").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tablaRiesgo.Field("IVA en Riesgo").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }
            else
            {
                ws.Cell(curr, 1).Value = "Sin facturas PPD con más de 60 días sin REP detectadas.";
                ws.Range(curr, 1, curr, 9).Merge();
                ws.Cell(curr, 1).Style.Font.Italic = true;
                ws.Cell(curr, 1).Style.Font.FontColor = ColorVerdeExito;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 38;
            ws.Column(3).Width = Math.Min(40, Math.Max(24, ws.Column(3).Width));
            ws.SheetView.FreezeRows(filaRiesgoHeader);
        }

        private static void ConstruirHojaCuentasPorCobrar(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Cuentas por Cobrar");
            AplicarEncabezadoCorporativo(ws, "Cuentas por Cobrar (Cartera Pendiente)", "Facturas emitidas a crédito (PPD) con saldo pendiente de cobro según complementos recibidos", nombreEmpresa, rfc, periodoTexto, 10);

            var lista = ReportesSql.ObtenerCuentasPorCobrar(conn, rfc);

            int filaHeader = 5;
            string[] headers = { "RFC Cliente", "Serie/Folio", "Fecha Emisión", "Días Venc.", "Antigüedad", "Total Facturado", "Cobrado", "Nota Crédito", "Saldo Pendiente", "Estado" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = headers[i];

            int currRow = filaHeader + 1;
            for (int i = 0; i < lista.Count; i++)
            {
                var c = lista[i];
                string antiguedad = c.DiasTranscurridos <= 30 ? "0 a 30 días" :
                                    c.DiasTranscurridos <= 60 ? "31 a 60 días" :
                                    c.DiasTranscurridos <= 90 ? "61 a 90 días" : "+90 días";
                string estado = c.NumComplementos == 0 ? "Sin complemento" : $"Parcial ({c.NumComplementos})";

                ws.Cell(currRow, 1).Value = c.RFCReceptor;
                ws.Cell(currRow, 2).Value = (c.Serie ?? "") + " " + (c.Folio ?? "");
                ws.Cell(currRow, 3).Value = c.FechaEmision;
                ws.Cell(currRow, 3).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(currRow, 4).Value = c.DiasTranscurridos;
                ws.Cell(currRow, 4).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(currRow, 5).Value = antiguedad;
                ws.Cell(currRow, 6).Value = c.Total;
                ws.Cell(currRow, 6).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 7).Value = c.Pagado;
                ws.Cell(currRow, 7).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 8).Value = c.NotaCreditoTotal;
                ws.Cell(currRow, 8).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 9).Value = c.Saldo;
                ws.Cell(currRow, 9).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 9).Style.Font.Bold = true;
                ws.Cell(currRow, 9).Style.Font.FontColor = ColorRojoAlerta;
                ws.Cell(currRow, 10).Value = estado;
                currRow++;
            }

            if (lista.Count > 0)
            {
                var tabla = ws.Range(filaHeader, 1, currRow - 1, headers.Length).CreateTable("CuentasPorCobrar");
                tabla.Theme = XLTableTheme.TableStyleMedium2;
                tabla.ShowTotalsRow = true;
                tabla.Field("Total Facturado").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Cobrado").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Nota Crédito").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Saldo Pendiente").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 16;
            ws.Column(2).Width = 14;
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 12;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 18;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 18;
            ws.Column(9).Width = 18;
            ws.Column(10).Width = 18;
            ws.SheetView.FreezeRows(filaHeader);
        }

        private static void ConstruirHojaCuentasPorPagar(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Cuentas por Pagar");
            AplicarEncabezadoCorporativo(ws, "Cuentas por Pagar (Pasivo Exigible)", "Facturas recibidas a crédito (PPD) con saldo pendiente según complementos y notas de crédito", nombreEmpresa, rfc, periodoTexto, 11);

            var lista = ReportesSql.ObtenerCuentasPorPagar(conn, rfc);

            int filaHeader = 5;
            string[] headers = { "RFC Emisor", "Proveedor", "Serie/Folio", "Fecha Emisión", "Días Venc.", "Antigüedad", "Total Factura", "Pagado", "Nota Crédito", "Saldo Pendiente", "Estado" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = headers[i];

            int currRow = filaHeader + 1;
            for (int i = 0; i < lista.Count; i++)
            {
                var c = lista[i];
                string antiguedad = c.DiasTranscurridos <= 30 ? "0 a 30 días" :
                                    c.DiasTranscurridos <= 60 ? "31 a 60 días" :
                                    c.DiasTranscurridos <= 90 ? "61 a 90 días" : "+90 días";
                string estado = c.NumComplementos == 0 ? "Sin complemento" : $"Parcial ({c.NumComplementos})";

                ws.Cell(currRow, 1).Value = c.RFCEmisor;
                ws.Cell(currRow, 2).Value = c.NombreEmisor ?? "(Sin nombre)";
                ws.Cell(currRow, 3).Value = (c.Serie ?? "") + " " + (c.Folio ?? "");
                ws.Cell(currRow, 4).Value = c.FechaEmision;
                ws.Cell(currRow, 4).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(currRow, 5).Value = c.DiasTranscurridos;
                ws.Cell(currRow, 5).Style.NumberFormat.Format = FormatoEntero;
                ws.Cell(currRow, 6).Value = antiguedad;
                ws.Cell(currRow, 7).Value = c.Total;
                ws.Cell(currRow, 7).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 8).Value = c.Pagado;
                ws.Cell(currRow, 8).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 9).Value = c.NotaCreditoTotal;
                ws.Cell(currRow, 9).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 10).Value = c.Saldo;
                ws.Cell(currRow, 10).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 10).Style.Font.Bold = true;
                ws.Cell(currRow, 10).Style.Font.FontColor = ColorRojoAlerta;
                ws.Cell(currRow, 11).Value = estado;
                currRow++;
            }

            if (lista.Count > 0)
            {
                var tabla = ws.Range(filaHeader, 1, currRow - 1, headers.Length).CreateTable("CuentasPorPagar");
                tabla.Theme = XLTableTheme.TableStyleMedium2;
                tabla.ShowTotalsRow = true;
                tabla.Field("Total Factura").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Pagado").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Nota Crédito").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Saldo Pendiente").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 16;
            ws.Column(2).Width = Math.Min(45, Math.Max(24, ws.Column(2).Width));
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 14;
            ws.Column(5).Width = 12;
            ws.Column(6).Width = 14;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 18;
            ws.Column(9).Width = 18;
            ws.Column(10).Width = 18;
            ws.Column(11).Width = 18;
            ws.SheetView.FreezeRows(filaHeader);
        }

        private static void ConstruirHojaInconsistencias(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Inconsistencias Fiscales");
            AplicarEncabezadoCorporativo(ws, "Auditoría de Inconsistencias Fiscales (SAT)", "Comprobantes que infringen el estándar Anexo 20 (PUE con 99, PPD != 99, o clave P01 derogada)", nombreEmpresa, rfc, periodoTexto, 9);

            var lista = ReportesSql.ObtenerErroresCoherenciaPago(conn, rfc);

            int filaHeader = 5;
            string[] headers = { "Fecha Emisión", "RFC Emisor", "Proveedor", "Serie/Folio", "Método", "Forma Pago", "Uso CFDI", "Total", "Motivo de Inconsistencia (Regla SAT)" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = headers[i];

            int currRow = filaHeader + 1;
            for (int i = 0; i < lista.Count; i++)
            {
                var err = lista[i];
                ws.Cell(currRow, 1).Value = err.FechaEmision;
                ws.Cell(currRow, 1).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(currRow, 2).Value = err.RFCEmisor;
                ws.Cell(currRow, 3).Value = err.NombreEmisor ?? "(Sin nombre)";
                ws.Cell(currRow, 4).Value = (err.Serie ?? "") + " " + (err.Folio ?? "");
                ws.Cell(currRow, 5).Value = err.MetodoPago;
                ws.Cell(currRow, 6).Value = err.FormaPago;
                ws.Cell(currRow, 7).Value = err.UsoCFDI ?? "-";
                ws.Cell(currRow, 8).Value = err.Total ?? 0m;
                ws.Cell(currRow, 8).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 9).Value = err.Motivo;
                ws.Cell(currRow, 9).Style.Font.FontColor = ColorRojoAlerta;
                currRow++;
            }

            if (lista.Count > 0)
            {
                var tabla = ws.Range(filaHeader, 1, currRow - 1, headers.Length).CreateTable("InconsistenciasPago");
                tabla.Theme = XLTableTheme.TableStyleLight11;
                tabla.ShowTotalsRow = true;
                tabla.Field("Total").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = Math.Min(40, Math.Max(22, ws.Column(3).Width));
            ws.Column(4).Width = 14;
            ws.Column(5).Width = 10;
            ws.Column(6).Width = 12;
            ws.Column(7).Width = 12;
            ws.Column(8).Width = 18;
            ws.Column(9).Width = 55;
            ws.SheetView.FreezeRows(filaHeader);
        }

        private static void ConstruirHojaNotasCredito(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("Notas de Crédito");
            AplicarEncabezadoCorporativo(ws, "Notas de Crédito Relacionadas", "Comprobantes de Egreso (Tipo E) vinculados a facturas originales de compras", nombreEmpresa, rfc, periodoTexto, 6);

            var lista = ReportesSql.ObtenerNotasCreditoRelacionadas(conn, rfc);

            int filaHeader = 5;
            string[] headers = { "Fecha NC", "Serie/Folio NC", "Importe NC", "Serie/Folio Original", "Importe Original", "¿Original en BD?" };
            for (int i = 0; i < headers.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = headers[i];

            int currRow = filaHeader + 1;
            for (int i = 0; i < lista.Count; i++)
            {
                var nc = lista[i];
                ws.Cell(currRow, 1).Value = nc.FechaNotaCredito;
                ws.Cell(currRow, 1).Style.DateFormat.Format = FormatoFecha;
                ws.Cell(currRow, 2).Value = (nc.SerieNotaCredito ?? "") + " " + (nc.FolioNotaCredito ?? "");
                ws.Cell(currRow, 3).Value = nc.TotalNotaCredito ?? 0m;
                ws.Cell(currRow, 3).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 4).Value = nc.OriginalEnBd ? ((nc.SerieOriginal ?? "") + " " + (nc.FolioOriginal ?? "")) : "(No descargada)";
                ws.Cell(currRow, 5).Value = nc.OriginalEnBd ? (nc.TotalOriginal ?? 0m) : 0m;
                ws.Cell(currRow, 5).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 6).Value = nc.OriginalEnBd ? "Sí" : "No";
                currRow++;
            }

            if (lista.Count > 0)
            {
                var tabla = ws.Range(filaHeader, 1, currRow - 1, headers.Length).CreateTable("NotasCredito");
                tabla.Theme = XLTableTheme.TableStyleMedium2;
                tabla.ShowTotalsRow = true;
                tabla.Field("Importe NC").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Importe Original").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 22;
            ws.Column(5).Width = 18;
            ws.Column(6).Width = 16;
            ws.SheetView.FreezeRows(filaHeader);
        }

        private static void ConstruirHojaCfdisMes(
            XLWorkbook libro,
            SqlConnection conn,
            string rfc,
            string nombreEmpresa,
            int anio,
            int mes,
            string periodoTexto)
        {
            var ws = libro.Worksheets.Add("CFDIs del Mes");
            AplicarEncabezadoCorporativo(ws, "Detalle de Comprobantes del Mes", "Listado consolidado de comprobantes emitidos y recibidos con desglose fiscal", nombreEmpresa, rfc, periodoTexto, 16);

            var lista = ReportesSql.ObtenerCfdisMes(conn, rfc, anio, mes);

            int filaHeader = 5;
            string[] headers = {
                "UUID", "Flujo", "Tipo", "RFC Emisor", "Nombre Emisor", "RFC Receptor",
                "Serie", "Folio", "Fecha Emisión", "Subtotal", "Descuento",
                "IVA", "Retenciones", "Total", "Moneda", "Estatus SAT"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = headers[i];

            int currRow = filaHeader + 1;
            for (int i = 0; i < lista.Count; i++)
            {
                var c = lista[i];
                ws.Cell(currRow, 1).Value = c.UUID.ToString();
                ws.Cell(currRow, 2).Value = c.Flujo;
                ws.Cell(currRow, 3).Value = c.TipoComprobante;
                ws.Cell(currRow, 4).Value = c.RFCEmisor;
                ws.Cell(currRow, 5).Value = c.NombreEmisor ?? "";
                ws.Cell(currRow, 6).Value = c.RFCReceptor;
                ws.Cell(currRow, 7).Value = c.Serie ?? "";
                ws.Cell(currRow, 8).Value = c.Folio ?? "";
                ws.Cell(currRow, 9).Value = c.FechaEmision;
                ws.Cell(currRow, 9).Style.DateFormat.Format = FormatoFechaHora;
                ws.Cell(currRow, 10).Value = c.Subtotal ?? 0m;
                ws.Cell(currRow, 10).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 11).Value = c.Descuento ?? 0m;
                ws.Cell(currRow, 11).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 12).Value = c.IVA ?? 0m;
                ws.Cell(currRow, 12).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 13).Value = c.Retenciones ?? 0m;
                ws.Cell(currRow, 13).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 14).Value = c.Total ?? 0m;
                ws.Cell(currRow, 14).Style.NumberFormat.Format = FormatoMoneda;
                ws.Cell(currRow, 15).Value = c.Moneda ?? "MXN";
                ws.Cell(currRow, 16).Value = c.EstatusSat;
                currRow++;
            }

            if (lista.Count > 0)
            {
                var tabla = ws.Range(filaHeader, 1, currRow - 1, headers.Length).CreateTable("CfdiDetalleMes");
                tabla.Theme = XLTableTheme.TableStyleMedium2;
                tabla.ShowTotalsRow = true;
                tabla.Field("Subtotal").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Descuento").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("IVA").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Retenciones").TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field("Total").TotalsRowFunction = XLTotalsRowFunction.Sum;
            }

            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 38;
            ws.Column(2).Width = 12;
            ws.Column(3).Width = 8;
            ws.Column(4).Width = 16;
            ws.Column(5).Width = Math.Min(35, Math.Max(20, ws.Column(5).Width));
            ws.Column(6).Width = 16;
            ws.Column(7).Width = 10;
            ws.Column(8).Width = 12;
            ws.Column(9).Width = 17;
            ws.Column(10).Width = 16;
            ws.Column(11).Width = 14;
            ws.Column(12).Width = 14;
            ws.Column(13).Width = 14;
            ws.Column(14).Width = 18;
            ws.Column(15).Width = 10;
            ws.Column(16).Width = 14;
            ws.SheetView.FreezeRows(filaHeader);
        }

        /// <summary>
        /// Exporta una pestaña individual a Excel con estilo corporativo y totales numéricos.
        /// </summary>
        public static void GenerarExportacionPestana(
            string rutaArchivo,
            string tituloPestana,
            string nombreEmpresa,
            string rfc,
            string periodo,
            string[] encabezados,
            Action<IXLWorksheet, int> llenarFilas)
        {
            using var libro = new XLWorkbook();
            var ws = libro.Worksheets.Add(tituloPestana);
            AplicarEncabezadoCorporativo(ws, tituloPestana, "Exportación directa de consulta", nombreEmpresa, rfc, periodo, encabezados.Length);

            int filaHeader = 5;
            for (int i = 0; i < encabezados.Length; i++)
                ws.Cell(filaHeader, i + 1).Value = encabezados[i];

            llenarFilas(ws, filaHeader + 1);

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(filaHeader);
            libro.SaveAs(rutaArchivo);
        }
    }
}
