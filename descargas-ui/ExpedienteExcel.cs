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

// ExpedienteExcel.cs -- vuelca un ExpedienteDatos (ver Expediente.cs) a un libro de Excel con hojas planas, filtradas y con formato, listo para
// anexar a una devolucion de impuestos, una aclaracion al SAT o una revision interna.

using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Descargas.Datos;
using ClosedXML.Excel;

namespace BrosLMV.DescargasUI
{
    internal static class ExpedienteExcel
    {
        private const string Dinero = "#,##0.00;[Red]-#,##0.00";
        private const string Fecha = "yyyy-mm-dd";

        private sealed class Col<T>
        {
            public string Titulo; public Func<T, object> Valor; public string Formato; public double Ancho;
        }
        private static Col<T> C<T>(string titulo, Func<T, object> valor, double ancho = 14, string formato = null) => new Col<T> { Titulo = titulo, Valor = valor, Ancho = ancho, Formato = formato };

        public static void Generar(ExpedienteDatos d, string empresa, string ruta)
        {
            using (var wb = new XLWorkbook())
            {
                Resumen(wb, d, empresa);
                Hoja(wb, "CFDI", d.Cfdis, new[]
                {
                    C<CfdiExp>("UUID", c => c.Uuid.ToString().ToUpperInvariant(), 38), C<CfdiExp>("Fecha", c => c.Fecha, 12, Fecha), C<CfdiExp>("Dirección", c => c.Direccion, 10), C<CfdiExp>("Tipo", c => c.Tipo, 6),
                    C<CfdiExp>("Serie", c => c.Serie, 8), C<CfdiExp>("Folio", c => c.Folio, 10), C<CfdiExp>("RFC emisor", c => c.RfcEmisor, 15), C<CfdiExp>("Emisor", c => c.NombreEmisor, 34),
                    C<CfdiExp>("RFC receptor", c => c.RfcReceptor, 15), C<CfdiExp>("Receptor", c => c.NombreReceptor, 34), C<CfdiExp>("Uso CFDI", c => c.UsoCfdi, 8),
                    C<CfdiExp>("Forma de pago", c => c.FormaPago, 9), C<CfdiExp>("Método de pago", c => c.MetodoPago, 9), C<CfdiExp>("Moneda", c => c.Moneda, 8), C<CfdiExp>("Tipo de cambio", c => c.TipoCambio, 11, "0.0000"),
                    C<CfdiExp>("Subtotal", c => c.Subtotal, 14, Dinero), C<CfdiExp>("Descuento", c => c.Descuento, 12, Dinero), C<CfdiExp>("IVA 16 %", c => c.IvaTasa16, 13, Dinero), C<CfdiExp>("IVA 8 %", c => c.IvaTasa8, 12, Dinero),
                    C<CfdiExp>("Base tasa 0 %", c => c.BaseTasa0, 14, Dinero), C<CfdiExp>("Base exento", c => c.BaseExento, 14, Dinero), C<CfdiExp>("IEPS", c => c.IepsTrasladado, 12, Dinero),
                    C<CfdiExp>("IVA retenido", c => c.IvaRetenido, 13, Dinero), C<CfdiExp>("ISR retenido", c => c.IsrRetenido, 13, Dinero),
                    C<CfdiExp>("Imp. locales traslados", c => c.ImpuestosLocalesTraslados, 13, Dinero), C<CfdiExp>("Imp. locales retenciones", c => c.ImpuestosLocalesRetenciones, 13, Dinero),
                    C<CfdiExp>("Total", c => c.Total, 15, Dinero), C<CfdiExp>("Total en pesos", c => c.TotalMxn, 15, Dinero), C<CfdiExp>("Estatus SAT", c => c.EstatusSat, 11),
                    C<CfdiExp>("Cancelado el", c => c.FechaCancelacion, 12, Fecha), C<CfdiExp>("Lista 69-B (código)", c => c.Efos, 11), C<CfdiExp>("Complementos", c => c.Complementos, 28), C<CfdiExp>("Archivo XML", c => c.Ruta, 60)
                });
                Hoja(wb, "Conceptos", d.Conceptos, new[]
                {
                    C<ConceptoExp>("UUID", c => c.Uuid.ToString().ToUpperInvariant(), 38), C<ConceptoExp>("Folio", c => c.Folio, 10), C<ConceptoExp>("Clave SAT", c => c.ClaveProdServ, 11), C<ConceptoExp>("No. identificación", c => c.NoIdentificacion, 16),
                    C<ConceptoExp>("Descripción", c => c.Descripcion, 55), C<ConceptoExp>("Cantidad", c => c.Cantidad, 11, "#,##0.######"), C<ConceptoExp>("Clave unidad", c => c.ClaveUnidad, 9), C<ConceptoExp>("Unidad", c => c.Unidad, 12),
                    C<ConceptoExp>("Valor unitario", c => c.ValorUnitario, 14, Dinero), C<ConceptoExp>("Importe", c => c.Importe, 14, Dinero), C<ConceptoExp>("Descuento", c => c.Descuento, 12, Dinero), C<ConceptoExp>("Objeto de impuesto", c => c.ObjetoImp, 10),
                    C<ConceptoExp>("Tasa IVA", c => c.TasaIva, 9, "0.00 %"), C<ConceptoExp>("IVA trasladado", c => c.IvaTrasladado, 13, Dinero), C<ConceptoExp>("IVA retenido", c => c.IvaRetenido, 13, Dinero), C<ConceptoExp>("ISR retenido", c => c.IsrRetenido, 13, Dinero)
                });
                Hoja(wb, "Impuestos", d.Impuestos, new[]
                {
                    C<ImpuestoExp>("UUID", i => i.Uuid.ToString().ToUpperInvariant(), 38), C<ImpuestoExp>("Folio", i => i.Folio, 10), C<ImpuestoExp>("Tipo", i => i.Tipo, 16), C<ImpuestoExp>("Impuesto", i => i.Impuesto, 18),
                    C<ImpuestoExp>("Factor", i => i.Factor, 10), C<ImpuestoExp>("Tasa o cuota", i => i.Tasa, 12, "0.000000"), C<ImpuestoExp>("Base", i => i.Base, 15, Dinero), C<ImpuestoExp>("Importe", i => i.Importe, 15, Dinero)
                });
                Hoja(wb, "Pagos (REP)", d.Pagos, new[]
                {
                    C<PagoDrExp>("UUID del REP", p => p.RepUuid.ToString().ToUpperInvariant(), 38), C<PagoDrExp>("Folio REP", p => p.RepFolio, 10), C<PagoDrExp>("Dirección", p => p.Direccion, 10), C<PagoDrExp>("Fecha de pago", p => p.FechaPago, 12, Fecha),
                    C<PagoDrExp>("Forma de pago", p => p.FormaPago, 9), C<PagoDrExp>("Moneda del pago", p => p.MonedaPago, 9), C<PagoDrExp>("Tipo de cambio", p => p.TipoCambioPago, 11, "0.0000"), C<PagoDrExp>("Monto del pago", p => p.Monto, 15, Dinero),
                    C<PagoDrExp>("Cuenta ordenante", p => p.CuentaOrdenante, 20), C<PagoDrExp>("Cuenta beneficiaria", p => p.CuentaBeneficiario, 20),
                    C<PagoDrExp>("UUID del documento", p => p.DrUuid == Guid.Empty ? "" : p.DrUuid.ToString().ToUpperInvariant(), 38), C<PagoDrExp>("Folio del documento", p => p.DrFolio, 14), C<PagoDrExp>("Moneda del documento", p => p.MonedaDr, 10),
                    C<PagoDrExp>("Equivalencia", p => p.EquivalenciaDr, 11, "0.000000"), C<PagoDrExp>("Parcialidad", p => p.Parcialidad, 10), C<PagoDrExp>("Saldo anterior", p => p.SaldoAnterior, 15, Dinero),
                    C<PagoDrExp>("Importe pagado", p => p.ImportePagado, 15, Dinero), C<PagoDrExp>("Saldo insoluto", p => p.SaldoInsoluto, 15, Dinero), C<PagoDrExp>("Objeto de impuesto", p => p.ObjetoImpDr, 9),
                    C<PagoDrExp>("IVA trasladado del pago", p => p.ImpuestosEnElPago ? (object)p.IvaTrasladadoDr : "sin desglose", 15, Dinero), C<PagoDrExp>("IVA retenido del pago", p => p.IvaRetenidoDr, 14, Dinero), C<PagoDrExp>("ISR retenido del pago", p => p.IsrRetenidoDr, 14, Dinero)
                });
                Hoja(wb, "PPD vs REP", d.Ppd, new[]
                {
                    C<PpdExp>("UUID", x => x.Uuid.ToString().ToUpperInvariant(), 38), C<PpdExp>("Fecha", x => x.Fecha, 12, Fecha), C<PpdExp>("Dirección", x => x.Direccion, 10), C<PpdExp>("Folio", x => x.Folio, 12), C<PpdExp>("Cliente o proveedor", x => x.Tercero, 38),
                    C<PpdExp>("Moneda", x => x.Moneda, 8), C<PpdExp>("Total", x => x.Total, 15, Dinero), C<PpdExp>("Pagado (con REP)", x => x.Pagado, 15, Dinero), C<PpdExp>("Saldo", x => x.Saldo, 15, Dinero), C<PpdExp>("REP", x => x.Reps, 6),
                    C<PpdExp>("Último pago", x => x.UltimoPago, 12, Fecha), C<PpdExp>("Estado", x => x.Estado, 26), C<PpdExp>("Estatus SAT", x => x.EstatusSat, 11)
                });
                Flujo(wb, d);
                Hoja(wb, "Complementos", d.Complementos, new[]
                {
                    C<ComplementoExp>("UUID", c => c.Uuid.ToString().ToUpperInvariant(), 38), C<ComplementoExp>("Folio", c => c.Folio, 12), C<ComplementoExp>("Complemento", c => c.Complemento, 28), C<ComplementoExp>("Detalle", c => c.Detalle, 100)
                });
                Hoja(wb, "Observaciones", d.Observaciones, new[]
                {
                    C<ObservacionExp>("Nivel", o => o.Nivel, 10), C<ObservacionExp>("Tema", o => o.Tema, 42), C<ObservacionExp>("Folio", o => o.Folio, 12), C<ObservacionExp>("UUID", o => o.Uuid.HasValue ? o.Uuid.Value.ToString().ToUpperInvariant() : "", 38), C<ObservacionExp>("Detalle", o => o.Detalle, 110)
                });
                wb.SaveAs(ruta);
            }
        }

        // Una hoja plana: encabezado oscuro, filtro, fila fija y formatos. Los valores se escriben por celda (ClosedXML aguanta decenas de miles de filas).
        private static void Hoja<T>(XLWorkbook wb, string nombre, IList<T> filas, Col<T>[] cols)
        {
            var ws = wb.AddWorksheet(nombre);
            for (int j = 0; j < cols.Length; j++) { ws.Cell(1, j + 1).Value = cols[j].Titulo; ws.Column(j + 1).Width = cols[j].Ancho; if (cols[j].Formato != null) ws.Column(j + 1).Style.NumberFormat.Format = cols[j].Formato; }
            for (int i = 0; i < filas.Count; i++)
                for (int j = 0; j < cols.Length; j++)
                {
                    object v = cols[j].Valor(filas[i]);
                    if (v == null) continue;
                    ws.Cell(i + 2, j + 1).Value = XLCellValue.FromObject(v);
                }
            Encabezado(ws, cols.Length);
            if (filas.Count > 0) ws.Range(1, 1, filas.Count + 1, cols.Length).SetAutoFilter();
        }

        private static void Encabezado(IXLWorksheet ws, int columnas)
        {
            var f = ws.Range(1, 1, 1, columnas);
            f.Style.Font.Bold = true; f.Style.Font.FontColor = XLColor.White; f.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2B4A");
            f.Style.Alignment.WrapText = true; f.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; ws.Row(1).Height = 32;
            ws.SheetView.FreezeRows(1);
        }

        private static void Flujo(XLWorkbook wb, ExpedienteDatos d)
        {
            var ws = wb.AddWorksheet("Flujo de efectivo (IVA)");
            ws.Cell(1, 1).Value = "IVA por flujo de efectivo"; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = "IVA de los CFDI pagados en una exhibicion (PUE) en la fecha del CFDI, más el IVA de cada pago (complemento de pago) en la fecha del pago, menos las notas de crédito. " +
                                  "Recibidos = IVA acreditable; Emitidos = IVA trasladado efectivamente cobrado. Solo cuenta los CFDI y los pagos que están en el periodo elegido: amplía el periodo para incluir pagos de facturas anteriores. " +
                                  "«Estimado» = el complemento de pago no desglosa impuestos y se calculó proporcionalmente al pago.";
            ws.Range(2, 1, 2, 8).Merge().Style.Alignment.WrapText = true; ws.Row(2).Height = 62;
            string[] cab = { "Mes", "Dirección", "IVA de CFDI pagados (PUE)", "IVA de pagos (REP)", "de los cuales estimado", "Notas de crédito (resta)", "IVA por flujo de efectivo" };
            for (int j = 0; j < cab.Length; j++) { ws.Cell(4, j + 1).Value = cab[j]; ws.Column(j + 1).Width = j < 2 ? 12 : 20; }
            var h = ws.Range(4, 1, 4, cab.Length); h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2B4A"); h.Style.Alignment.WrapText = true; ws.Row(4).Height = 32;
            int r = 5;
            foreach (var f in d.Flujo.OrderBy(x => x.Anio).ThenBy(x => x.Mes).ThenBy(x => x.Direccion))
            {
                ws.Cell(r, 1).Value = f.MesTexto; ws.Cell(r, 2).Value = f.Direccion;
                ws.Cell(r, 3).Value = f.IvaPue; ws.Cell(r, 4).Value = f.IvaPagos; ws.Cell(r, 5).Value = f.IvaPagosEstimado; ws.Cell(r, 6).Value = f.IvaEgresos; ws.Cell(r, 7).Value = f.Total;
                r++;
            }
            if (r > 5)
            {
                ws.Range(5, 3, r - 1, 7).Style.NumberFormat.Format = Dinero;
                foreach (var dir in new[] { "Recibidos", "Emitidos" })
                {
                    var del = d.Flujo.Where(x => x.Direccion == dir).ToList();
                    ws.Cell(r, 1).Value = "Total"; ws.Cell(r, 2).Value = dir;
                    ws.Cell(r, 3).Value = del.Sum(x => x.IvaPue); ws.Cell(r, 4).Value = del.Sum(x => x.IvaPagos); ws.Cell(r, 5).Value = del.Sum(x => x.IvaPagosEstimado);
                    ws.Cell(r, 6).Value = del.Sum(x => x.IvaEgresos); ws.Cell(r, 7).Value = del.Sum(x => x.Total);
                    ws.Range(r, 1, r, 7).Style.Font.Bold = true; ws.Range(r, 3, r, 7).Style.NumberFormat.Format = Dinero; ws.Range(r, 1, r, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EFFD");
                    r++;
                }
            }
            ws.SheetView.FreezeRows(4);
        }

        private static void Resumen(XLWorkbook wb, ExpedienteDatos d, string empresa)
        {
            var o = d.Opciones;
            var ws = wb.AddWorksheet("Resumen");
            ws.Column(1).Width = 40; for (int j = 2; j <= 8; j++) ws.Column(j).Width = 18;
            int r = 1;
            ws.Cell(r, 1).Value = "Expediente de CFDI"; ws.Cell(r, 1).Style.Font.Bold = true; ws.Cell(r, 1).Style.Font.FontSize = 18; r++;
            ws.Cell(r, 1).Value = empresa + " · " + o.Rfc; ws.Cell(r, 1).Style.Font.FontSize = 12; r++;
            ws.Cell(r, 1).Value = "Periodo: " + o.Desde.ToString("yyyy-MM-dd") + " al " + o.Hasta.ToString("yyyy-MM-dd") + " · " + o.Direccion + (o.IncluirNomina ? " · con nómina" : " · sin nómina") + (o.IncluirCancelados ? " · con cancelados" : " · sin cancelados"); r++;
            ws.Cell(r, 1).Value = "Generado el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " con BrosLMV Descargas. Los datos salen de los XML descargados del SAT."; ws.Cell(r, 1).Style.Font.FontColor = XLColor.Gray; r += 2;

            void Titulo(string t) { ws.Cell(r, 1).Value = t; ws.Range(r, 1, r, 8).Style.Font.Bold = true; ws.Range(r, 1, r, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2B4A"); ws.Range(r, 1, r, 8).Style.Font.FontColor = XLColor.White; r++; }
            void Fila(string t, object v, string fmt = null) { ws.Cell(r, 1).Value = t; ws.Cell(r, 2).Value = XLCellValue.FromObject(v); if (fmt != null) ws.Cell(r, 2).Style.NumberFormat.Format = fmt; r++; }

            Titulo("Comprobantes");
            Fila("CFDI en el periodo", d.Cfdis.Count, "#,##0");
            Fila("  vigentes", d.Cfdis.Count(c => c.EstatusSat != "Cancelado"), "#,##0"); Fila("  cancelados", d.Cfdis.Count(c => c.EstatusSat == "Cancelado"), "#,##0");
            Fila("XML que no se pudieron leer", d.XmlNoLeidos, "#,##0"); r++;
            ws.Cell(r, 1).Value = "Por tipo de comprobante"; ws.Cell(r, 2).Value = "Recibidos"; ws.Cell(r, 3).Value = "Total recibidos"; ws.Cell(r, 4).Value = "Emitidos"; ws.Cell(r, 5).Value = "Total emitidos"; ws.Range(r, 1, r, 5).Style.Font.Bold = true; r++;
            foreach (var (t, n) in new[] { ("I", "Ingreso"), ("E", "Egreso (nota de crédito)"), ("P", "Pago (REP)"), ("N", "Nómina"), ("T", "Traslado") })
            {
                var rec = d.Cfdis.Where(c => c.Tipo == t && c.Direccion == "Recibido" && c.EstatusSat != "Cancelado").ToList(); var emi = d.Cfdis.Where(c => c.Tipo == t && c.Direccion == "Emitido" && c.EstatusSat != "Cancelado").ToList();
                if (rec.Count + emi.Count == 0) continue;
                ws.Cell(r, 1).Value = n; ws.Cell(r, 2).Value = rec.Count; ws.Cell(r, 3).Value = rec.Sum(c => c.TotalMxn); ws.Cell(r, 4).Value = emi.Count; ws.Cell(r, 5).Value = emi.Sum(c => c.TotalMxn);
                ws.Range(r, 3, r, 3).Style.NumberFormat.Format = Dinero; ws.Range(r, 5, r, 5).Style.NumberFormat.Format = Dinero; r++;
            }
            r++;
            Titulo("Impuestos de los CFDI vigentes (ingresos y egresos, en pesos)");
            ws.Cell(r, 2).Value = "Recibidos"; ws.Cell(r, 3).Value = "Emitidos"; ws.Range(r, 1, r, 3).Style.Font.Bold = true; r++;
            void Imp(string t, Func<CfdiExp, decimal> f)
            {
                decimal Suma(string dir) => d.Cfdis.Where(c => c.Direccion == dir && c.EstatusSat != "Cancelado" && (c.Tipo == "I" || c.Tipo == "E")).Sum(c => c.Signo * f(c) * c.Factor);
                ws.Cell(r, 1).Value = t; ws.Cell(r, 2).Value = Suma("Recibido"); ws.Cell(r, 3).Value = Suma("Emitido"); ws.Range(r, 2, r, 3).Style.NumberFormat.Format = Dinero; r++;
            }
            Imp("IVA trasladado (16 % + 8 %)", c => c.IvaTasa16 + c.IvaTasa8); Imp("Base a tasa 0 %", c => c.BaseTasa0); Imp("Base exenta", c => c.BaseExento); Imp("IEPS", c => c.IepsTrasladado);
            Imp("IVA retenido", c => c.IvaRetenido); Imp("ISR retenido", c => c.IsrRetenido); Imp("Impuestos locales trasladados", c => c.ImpuestosLocalesTraslados); Imp("Impuestos locales retenidos", c => c.ImpuestosLocalesRetenciones);
            r++;
            Titulo("Pagos a plazo (PPD)");
            Fila("CFDI PPD en el periodo", d.Ppd.Count, "#,##0");
            foreach (var e in new[] { "Pagado", "Pago parcial", "Sin complemento de pago", "Cancelado" }) Fila("  " + e, d.Ppd.Count(x => x.Estado == e), "#,##0");
            Fila("Saldo pendiente de los PPD vigentes (sin convertir moneda)", d.Ppd.Where(x => x.EstatusSat != "Cancelado").Sum(x => x.Saldo), Dinero); r++;
            Titulo("IVA por flujo de efectivo (en pesos)");
            ws.Cell(r, 2).Value = "Recibidos"; ws.Cell(r, 3).Value = "Emitidos"; ws.Range(r, 1, r, 3).Style.Font.Bold = true; r++;
            ws.Cell(r, 1).Value = "IVA acreditable / trasladado cobrado"; ws.Cell(r, 2).Value = d.Flujo.Where(f => f.Direccion == "Recibidos").Sum(f => f.Total); ws.Cell(r, 3).Value = d.Flujo.Where(f => f.Direccion == "Emitidos").Sum(f => f.Total);
            ws.Range(r, 2, r, 3).Style.NumberFormat.Format = Dinero; r++;
            ws.Cell(r, 1).Value = "  del cual estimado (REP sin desglose)"; ws.Cell(r, 2).Value = d.Flujo.Where(f => f.Direccion == "Recibidos").Sum(f => f.IvaPagosEstimado); ws.Cell(r, 3).Value = d.Flujo.Where(f => f.Direccion == "Emitidos").Sum(f => f.IvaPagosEstimado);
            ws.Range(r, 2, r, 3).Style.NumberFormat.Format = Dinero; r += 2;
            Titulo("Observaciones");
            foreach (var g in d.Observaciones.GroupBy(x => x.Nivel + "|" + x.Tema).OrderBy(g => g.Key.StartsWith("Revisar") ? 0 : g.Key.StartsWith("Aviso") ? 1 : 2).ThenBy(g => g.Key))
            { var p = g.Key.Split('|'); ws.Cell(r, 1).Value = p[1]; ws.Cell(r, 2).Value = g.Count(); ws.Cell(r, 3).Value = p[0]; if (p[0] == "Revisar") ws.Cell(r, 3).Style.Font.FontColor = XLColor.Red; r++; }
            if (d.Observaciones.Count == 0) { ws.Cell(r, 1).Value = "Sin observaciones."; r++; }
            r++; ws.Cell(r, 1).Value = "Hojas: CFDI · Conceptos · Impuestos · Pagos (REP) · PPD vs REP · Flujo de efectivo (IVA) · Complementos · Observaciones"; ws.Cell(r, 1).Style.Font.FontColor = XLColor.Gray;
        }
    }
}
