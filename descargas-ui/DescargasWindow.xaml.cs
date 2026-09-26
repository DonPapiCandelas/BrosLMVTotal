using System;
using System.Collections.Generic;
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
            LblSubtitulo.Text = empresa.Nombre + "  ·  " + empresa.RFC + "  ·  historial descargado";
            Loaded += (s, e) => Cargar();
        }

        private static string TagSeleccionado(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private void Cargar()
        {
            if (_conn.State != System.Data.ConnectionState.Open) return;
            string busqueda = string.IsNullOrWhiteSpace(TxtBuscar.Text) ? null : TxtBuscar.Text.Trim();
            string estatus = TagSeleccionado(CmbEstatus);
            string tipo = TagSeleccionado(CmbTipo);
            bool emitidos = TabEmitidos.IsChecked == true;
            _cfdis = BrosSatDb.ObtenerCfdiRecientes(_conn, _empresa.RFC, 50000,
                string.IsNullOrWhiteSpace(estatus) ? null : estatus,
                string.IsNullOrWhiteSpace(tipo) ? null : tipo, busqueda,
                ChkArchivados.IsChecked == true, emitidos ? "Emitidos" : "Recibidos");

            GridCfdi.ItemsSource = _cfdis.Select(c => ARow(c, emitidos)).ToList();
            LblConteo.Text = _cfdis.Count.ToString("N0") + (_cfdis.Count == 50000 ? "+" : "") + " CFDI encontrados";
        }

        private static CfdiRow ARow(CfdiFila c, bool emitidos) => new CfdiRow
        {
            CfdiID = c.CfdiID, UUID = c.UUID.ToString(), RFCEmisor = c.RFCEmisor, NombreEmisor = c.NombreEmisor,
            RFCReceptor = c.RFCReceptor, Contraparte = emitidos ? c.RFCReceptor : c.RFCEmisor,
            NombreContraparte = emitidos ? c.RFCReceptor : c.NombreEmisor, TipoComprobante = c.TipoComprobante,
            FechaEmision = c.FechaEmision.ToString("yyyy-MM-dd HH:mm"),
            Subtotal = c.Subtotal?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
            Descuento = c.Descuento?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
            IVA = c.IVA?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
            Retenciones = c.Retenciones?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
            Total = c.Total?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
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
            var dialogo = new SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = "CFDI_" + _empresa.RFC + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };
            if (dialogo.ShowDialog(this) != true) return;
            try
            {
                CrearExcel(dialogo.FileName);
                MessageBox.Show(this, "Se creó el reporte de Excel con " + _cfdis.Count.ToString("N0") + " CFDI.\n\n" + dialogo.FileName,
                    "Exportación terminada", MessageBoxButton.OK, MessageBoxImage.Information);
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
            hoja.Cell("A1").Value = "BrosLMV Descargas";
            hoja.Cell("A2").Value = "Listado de CFDI " + (emitidos ? "emitidos" : "recibidos");
            hoja.Cell("A3").Value = _empresa.Nombre + " · " + _empresa.RFC;
            hoja.Cell("A4").Value = "Generado el " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " · " + _cfdis.Count.ToString("N0") + " registros";
            hoja.Range("A1:N1").Merge(); hoja.Range("A2:N2").Merge(); hoja.Range("A3:N3").Merge(); hoja.Range("A4:N4").Merge();
            hoja.Range("A1:N1").Style.Fill.BackgroundColor = XLColor.FromHtml("#15324F");
            hoja.Range("A1:N1").Style.Font.FontColor = XLColor.White; hoja.Range("A1:N1").Style.Font.Bold = true; hoja.Cell("A1").Style.Font.FontSize = 16;
            hoja.Range("A2:N2").Style.Font.Bold = true; hoja.Cell("A2").Style.Font.FontSize = 13;
            hoja.Range("A3:N4").Style.Font.FontColor = XLColor.FromHtml("#475569");
            string[] encabezados = { "UUID", "Contraparte", "RFC contraparte", "Tipo", "Fecha emisión", "Subtotal", "Descuento", "IVA", "Retenciones", "Total", "Moneda", "Forma pago", "Método pago", "Estatus SAT" };
            for (int i = 0; i < encabezados.Length; i++) hoja.Cell(6, i + 1).Value = encabezados[i];
            for (int i = 0; i < _cfdis.Count; i++)
            {
                var c = _cfdis[i]; int f = i + 7;
                hoja.Cell(f, 1).Value = c.UUID.ToString(); hoja.Cell(f, 2).Value = emitidos ? c.RFCReceptor : c.NombreEmisor;
                hoja.Cell(f, 3).Value = emitidos ? c.RFCReceptor : c.RFCEmisor; hoja.Cell(f, 4).Value = c.TipoComprobante;
                hoja.Cell(f, 5).Value = c.FechaEmision; hoja.Cell(f, 6).Value = c.Subtotal; hoja.Cell(f, 7).Value = c.Descuento;
                hoja.Cell(f, 8).Value = c.IVA; hoja.Cell(f, 9).Value = c.Retenciones; hoja.Cell(f, 10).Value = c.Total;
                hoja.Cell(f, 11).Value = c.Moneda; hoja.Cell(f, 12).Value = c.FormaPago; hoja.Cell(f, 13).Value = c.MetodoPago; hoja.Cell(f, 14).Value = c.EstatusSat;
            }
            var tabla = hoja.Range(6, 1, _cfdis.Count + 6, encabezados.Length).CreateTable("CfdiListado");
            tabla.Theme = XLTableTheme.TableStyleMedium2;
            hoja.Column(5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            hoja.Columns(6, 10).Style.NumberFormat.Format = "$#,##0.00";
            hoja.SheetView.FreezeRows(6); hoja.SheetView.FreezeColumns(1);
            hoja.Columns().AdjustToContents();
            hoja.Column(1).Width = 38; hoja.Column(2).Width = Math.Min(42, Math.Max(22, hoja.Column(2).Width));
            hoja.Columns(6, 10).Width = 15;
            hoja.RangeUsed().Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            libro.SaveAs(ruta);
        }
    }
}
