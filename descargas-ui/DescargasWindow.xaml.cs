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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI
{
    // Vista de consulta completa: separada del tablero para que el trabajo diario conserve foco
    // y el historial pueda verse, filtrarse y exportarse sin quedar reducido a unas pocas filas.
    public partial class DescargasWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly EmpresaRow _empresa;
        private List<CfdiFila> _cfdis = new List<CfdiFila>();

        public DescargasWindow(SqlConnection conn, EmpresaRow empresa)
        {
            InitializeComponent();
            _conn = conn;
            _empresa = empresa;
            LblSubtitulo.Text = empresa.Nombre + "  ·  " + empresa.RFC + "  ·  historial de comprobantes fiscales";
            ChkOcultarNominas.IsChecked = empresa.OcultarNominas;
            Loaded += (s, e) => Cargar();
        }

        private static string TagSeleccionado(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private void CmbRangoFecha_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbRangoFecha == null || DpDesde == null || DpHasta == null) return;
            string tag = (CmbRangoFecha.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            DateTime hoy = DateTime.Today;
            switch (tag)
            {
                case "este_mes":
                    DpDesde.SelectedDate = new DateTime(hoy.Year, hoy.Month, 1);
                    DpHasta.SelectedDate = hoy;
                    break;
                case "mes_anterior":
                    var mesAnt = hoy.AddMonths(-1);
                    DpDesde.SelectedDate = new DateTime(mesAnt.Year, mesAnt.Month, 1);
                    DpHasta.SelectedDate = new DateTime(mesAnt.Year, mesAnt.Month, DateTime.DaysInMonth(mesAnt.Year, mesAnt.Month));
                    break;
                case "anio_actual":
                    DpDesde.SelectedDate = new DateTime(hoy.Year, 1, 1);
                    DpHasta.SelectedDate = hoy;
                    break;
                case "ultimos_30":
                    DpDesde.SelectedDate = hoy.AddDays(-30);
                    DpHasta.SelectedDate = hoy;
                    break;
                case "todos":
                    DpDesde.SelectedDate = null;
                    DpHasta.SelectedDate = null;
                    break;
                case "personalizado":
                    break;
            }
            Cargar();
        }

        private void Fecha_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) Cargar();
        }

        private void Cargar()
        {
            if (_conn.State != System.Data.ConnectionState.Open) return;
            string busqueda = string.IsNullOrWhiteSpace(TxtBuscar.Text) ? null : TxtBuscar.Text.Trim();
            string estatus = TagSeleccionado(CmbEstatus);
            string tipo = TagSeleccionado(CmbTipo);
            bool emitidos = TabEmitidos.IsChecked == true;
            DateTime? fechaDesde = DpDesde?.SelectedDate;
            DateTime? fechaHasta = DpHasta?.SelectedDate.HasValue == true
                ? DpHasta.SelectedDate.Value.Date.AddDays(1).AddTicks(-1)
                : (DateTime?)null;
            bool ocultarNominas = ChkOcultarNominas.IsChecked == true;

            _cfdis = BrosSatDb.ObtenerCfdiRecientes(_conn, _empresa.RFC, 50000,
                string.IsNullOrWhiteSpace(estatus) ? null : estatus,
                string.IsNullOrWhiteSpace(tipo) ? null : tipo, busqueda,
                ChkArchivados.IsChecked == true, emitidos ? "Emitidos" : "Recibidos",
                fechaDesde, fechaHasta, ocultarNominas);

            GridCfdi.ItemsSource = _cfdis.Select(c => ARow(c, emitidos)).ToList();
            LblConteo.Text = _cfdis.Count.ToString("N0") + (_cfdis.Count == 50000 ? "+" : "") + " CFDI encontrados";

            // Cálculo y despliegue del resumen financiero en vivo en la barra inferior
            decimal sumSubtotal = _cfdis.Sum(c => c.Subtotal ?? 0m);
            decimal sumIva = _cfdis.Sum(c => c.IVA ?? 0m);
            decimal sumRet = _cfdis.Sum(c => c.Retenciones ?? 0m);
            decimal sumTotal = _cfdis.Sum(c => c.Total ?? 0m);
            var culture = new CultureInfo("es-MX");

            LblTotalesResumen.Text = $"{_cfdis.Count.ToString("N0", culture)} CFDI   |   " +
                                    $"Subtotal: {sumSubtotal.ToString("C2", culture)}   |   " +
                                    $"IVA: {sumIva.ToString("C2", culture)}   |   " +
                                    $"Retenciones: {sumRet.ToString("C2", culture)}   |   " +
                                    $"Total: {sumTotal.ToString("C2", culture)}";
        }

        private static CfdiRow ARow(CfdiFila c, bool emitidos) => new CfdiRow
        {
            CfdiID = c.CfdiID, UUID = c.UUID.ToString(), RFCEmisor = c.RFCEmisor, NombreEmisor = c.NombreEmisor,
            RFCReceptor = c.RFCReceptor, Contraparte = emitidos ? c.RFCReceptor : c.RFCEmisor,
            NombreContraparte = emitidos ? c.RFCReceptor : (string.IsNullOrWhiteSpace(c.NombreEmisor) ? c.RFCEmisor : c.NombreEmisor),
            TipoComprobante = c.TipoComprobante,
            Serie = c.Serie,
            Folio = c.Folio,
            FechaEmision = c.FechaEmision.ToString("yyyy-MM-dd HH:mm"),
            Subtotal = c.Subtotal.HasValue ? c.Subtotal.Value.ToString("C2", new CultureInfo("es-MX")) : "-",
            Descuento = c.Descuento.HasValue ? c.Descuento.Value.ToString("C2", new CultureInfo("es-MX")) : "-",
            IVA = c.IVA.HasValue ? c.IVA.Value.ToString("C2", new CultureInfo("es-MX")) : "-",
            Retenciones = c.Retenciones.HasValue ? c.Retenciones.Value.ToString("C2", new CultureInfo("es-MX")) : "-",
            Total = c.Total.HasValue ? c.Total.Value.ToString("C2", new CultureInfo("es-MX")) : "-",
            Moneda = c.Moneda ?? "-", TipoCambio = c.TipoCambio?.ToString("N4", CultureInfo.InvariantCulture) ?? "-",
            FormaPago = c.FormaPago ?? "-", MetodoPago = c.MetodoPago ?? "-", EstatusSat = c.EstatusSat,
            RutaArchivoXml = c.RutaArchivoXml, Archivado = c.Archivado, FechaSincronizadoComercial = c.FechaSincronizadoComercial
        };

        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Cargar(); }
        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Cargar();
        private void BtnDetalle_Click(object sender, RoutedEventArgs e) => AbrirDetalle();
        private void GridCfdi_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => AbrirDetalle();
        private void AbrirDetalle()
        {
            if (GridCfdi.SelectedItem is CfdiRow fila)
                new CfdiDetalleWindow(_conn, fila.CfdiID) { Owner = this }.ShowDialog();
        }

        private void BtnArchivar_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridCfdi.SelectedItem is CfdiRow fila))
            {
                MessageBox.Show(this, "Selecciona un CFDI para archivarlo o mostrarlo de nuevo.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            BrosSatDb.ArchivarCfdi(_conn, fila.CfdiID, !fila.Archivado);
            Cargar();
        }

        private void BtnDescargarXml_Click(object sender, RoutedEventArgs e)
        {
            var seleccionados = (GridCfdi.ItemsSource as IEnumerable<CfdiRow>)?.Where(x => x.Seleccionado).ToList();
            if (seleccionados == null || seleccionados.Count == 0)
            {
                MessageBox.Show(this, "Marca uno o más CFDI para copiar sus XML ya descargados.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new DescargarXmlWindow(seleccionados) { Owner = this }.ShowDialog();
        }

        private void BtnExportarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_cfdis.Count == 0)
            {
                MessageBox.Show(this, "No hay CFDI para exportar con los filtros actuales.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool emitidos = TabEmitidos.IsChecked == true;
            var dialogo = new SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"CFDI_{(emitidos ? "Emitidos" : "Recibidos")}_{_empresa.RFC}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dialogo.ShowDialog(this) != true) return;
            try
            {
                CrearExcel(dialogo.FileName);
                var r = MessageBox.Show(this,
                    $"Se creó el reporte de Excel con {_cfdis.Count:N0} CFDI:\n\n{dialogo.FileName}\n\n¿Deseas abrir el archivo ahora?",
                    "Exportación terminada",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialogo.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo crear el archivo de Excel:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CrearExcel(string ruta)
        {
            bool emitidos = TabEmitidos.IsChecked == true;
            using var libro = new XLWorkbook();
            var hoja = libro.Worksheets.Add("CFDI " + (emitidos ? "Emitidos" : "Recibidos"));
            hoja.ShowGridLines = true;

            // Encabezado corporativo
            hoja.Cell("A1").Value = "BrosLMV Descargas · Listado de CFDI " + (emitidos ? "Emitidos" : "Recibidos");
            hoja.Cell("A2").Value = _empresa.Nombre + "  ·  RFC: " + _empresa.RFC;
            hoja.Cell("A3").Value = "Generado: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "  ·  " + _cfdis.Count.ToString("N0") + " registros filtrados";

            hoja.Range("A1:P1").Merge(); hoja.Range("A2:P2").Merge(); hoja.Range("A3:P3").Merge();
            hoja.Range("A1:P1").Style.Fill.BackgroundColor = XLColor.FromHtml("#15324F");
            hoja.Range("A1:P1").Style.Font.FontColor = XLColor.White;
            hoja.Range("A1:P1").Style.Font.Bold = true;
            hoja.Cell("A1").Style.Font.FontSize = 15;
            hoja.Row(1).Height = 30;

            hoja.Range("A2:P2").Style.Fill.BackgroundColor = XLColor.FromHtml("#24558A");
            hoja.Range("A2:P2").Style.Font.FontColor = XLColor.White;
            hoja.Range("A2:P2").Style.Font.Bold = true;
            hoja.Cell("A2").Style.Font.FontSize = 11;
            hoja.Row(2).Height = 22;

            hoja.Range("A3:P3").Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            hoja.Range("A3:P3").Style.Font.FontColor = XLColor.FromHtml("#64748B");
            hoja.Range("A3:P3").Style.Font.Italic = true;
            hoja.Cell("A3").Style.Font.FontSize = 9.5;
            hoja.Row(3).Height = 20;

            string[] encabezados = { "UUID", "Serie", "Folio", "Contraparte", "RFC Contraparte", "Tipo", "Fecha Emisión", "Subtotal", "Descuento", "IVA", "Retenciones", "Total", "Moneda", "Forma Pago", "Método Pago", "Estatus SAT" };
            for (int i = 0; i < encabezados.Length; i++) hoja.Cell(5, i + 1).Value = encabezados[i];
            hoja.Row(5).Height = 24;

            for (int i = 0; i < _cfdis.Count; i++)
            {
                var c = _cfdis[i]; int f = i + 6;
                hoja.Cell(f, 1).Value = c.UUID.ToString();
                hoja.Cell(f, 2).Value = c.Serie ?? "";
                hoja.Cell(f, 3).Value = c.Folio ?? "";
                hoja.Cell(f, 4).Value = emitidos ? c.RFCReceptor : (c.NombreEmisor ?? c.RFCEmisor);
                hoja.Cell(f, 5).Value = emitidos ? c.RFCReceptor : c.RFCEmisor;
                hoja.Cell(f, 6).Value = c.TipoComprobante ?? "";
                hoja.Cell(f, 7).Value = c.FechaEmision;
                hoja.Cell(f, 8).Value = c.Subtotal ?? 0m;
                hoja.Cell(f, 9).Value = c.Descuento ?? 0m;
                hoja.Cell(f, 10).Value = c.IVA ?? 0m;
                hoja.Cell(f, 11).Value = c.Retenciones ?? 0m;
                hoja.Cell(f, 12).Value = c.Total ?? 0m;
                hoja.Cell(f, 13).Value = c.Moneda ?? "MXN";
                hoja.Cell(f, 14).Value = c.FormaPago ?? "";
                hoja.Cell(f, 15).Value = c.MetodoPago ?? "";
                hoja.Cell(f, 16).Value = c.EstatusSat ?? "";
            }

            var tabla = hoja.Range(5, 1, _cfdis.Count + 5, encabezados.Length).CreateTable("CfdiListado");
            tabla.Theme = XLTableTheme.TableStyleMedium2;
            tabla.ShowTotalsRow = true;
            tabla.Field("Subtotal").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabla.Field("Descuento").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabla.Field("IVA").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabla.Field("Retenciones").TotalsRowFunction = XLTotalsRowFunction.Sum;
            tabla.Field("Total").TotalsRowFunction = XLTotalsRowFunction.Sum;

            hoja.Column(7).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            hoja.Columns(8, 12).Style.NumberFormat.Format = "$#,##0.00;($#,##0.00);\"$0.00\"";
            hoja.SheetView.FreezeRows(5);
            hoja.SheetView.FreezeColumns(1);
            hoja.Columns().AdjustToContents();
            hoja.Column(1).Width = 38;
            hoja.Column(4).Width = Math.Min(42, Math.Max(24, hoja.Column(4).Width));
            hoja.Columns(8, 12).Width = 16;
            hoja.RangeUsed().Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            libro.SaveAs(ruta);
        }
    }
}
