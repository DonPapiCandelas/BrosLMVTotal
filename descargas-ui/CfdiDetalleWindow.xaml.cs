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

// CfdiDetalleWindow.xaml.cs -- ventana de solo lectura con TODOS los campos de un CFDI (el
// historial de MainWindow solo muestra columnas resumidas). Tambien consulta CfdiVinculo -- esa
// tabla ya existe en el esquema pero nada la escribe todavia (punto 4 del roadmap, pendiente de
// diseño); aqui solo se LEE, para poder decir "vinculado" o "sin vincular" cuando esa pieza exista.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    public sealed class ConceptoRow
    {
        public string ClaveProdServ { get; set; }
        public string Descripcion { get; set; }
        public string Cantidad { get; set; }
        public string ValorUnitario { get; set; }
        public string Importe { get; set; }
    }

    public sealed class PagoDoctoRow
    {
        public string UUIDRelacionado { get; set; }
        public string SerieFolio { get; set; }
        public string FechaPago { get; set; }
        public string FormaDePago { get; set; }
        public string NumParcialidad { get; set; }
        public string ImpSaldoAnt { get; set; }
        public string ImpPagado { get; set; }
        public string ImpSaldoInsoluto { get; set; }
    }

    public partial class CfdiDetalleWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly int _cfdiId;
        private string _rutaArchivoXml;

        public CfdiDetalleWindow(SqlConnection conn, int cfdiId)
        {
            InitializeComponent();
            _conn = conn;
            _cfdiId = cfdiId;

            var c = BrosSatDb.ObtenerCfdiDetalle(conn, cfdiId);
            if (c == null)
            {
                MessageBox.Show(this, "Este CFDI ya no existe en la base de datos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _rutaArchivoXml = c.RutaArchivoXml;

            LblTitulo.Text = (c.NombreEmisor ?? c.RFCEmisor) + (c.Archivado ? "  (archivado)" : "");
            LblUuid.Text = c.UUID.ToString();

            ValEmisor.Text = c.RFCEmisor + (c.NombreEmisor != null ? " — " + c.NombreEmisor : "");
            ValReceptor.Text = c.RFCReceptor;
            ValFolio.Text = (string.IsNullOrEmpty(c.Serie) ? "" : c.Serie + " ") + (c.Folio ?? "-");
            ValTipo.Text = DescribirTipo(c.TipoComprobante);
            ValUsoCfdi.Text = c.UsoCFDI ?? "-";
            ValFormaPago.Text = c.FormaPago ?? "-";
            ValMetodoPago.Text = c.MetodoPago ?? "-";
            ValFechaEmision.Text = c.FechaEmision.ToString("yyyy-MM-dd HH:mm");

            ValSubtotal.Text = Moneda(c.Subtotal);
            ValDescuento.Text = Moneda(c.Descuento);
            ValIva.Text = Moneda(c.IVA);
            ValRetenciones.Text = Moneda(c.Retenciones);
            ValTotal.Text = Moneda(c.Total);
            ValMoneda.Text = (c.Moneda ?? "-") + (c.TipoCambio.HasValue ? "  (T.C. " + c.TipoCambio.Value.ToString("N4", CultureInfo.InvariantCulture) + ")" : "");
            ValEstatus.Text = c.EstatusSat;
            ValFechaDescarga.Text = c.FechaDescarga.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            var vinculos = BrosSatDb.ObtenerVinculos(conn, c.UUID);
            ValVinculo.Text = vinculos.Count == 0
                ? "Sin vincular"
                : string.Join("; ", vinculos.Select(v => v.TipoVinculo + " — Documento #" + v.DocumentID + " (" + v.FechaVinculo.ToLocalTime().ToString("yyyy-MM-dd") + ")"));

            BtnAbrirCarpeta.IsEnabled = !string.IsNullOrEmpty(_rutaArchivoXml) && File.Exists(_rutaArchivoXml);
            CargarSistemas(c.UUID, c.RFCEmisor, c.RFCReceptor);

            bool esPago = string.Equals(c.TipoComprobante, "P", StringComparison.OrdinalIgnoreCase);
            PanelPartidas.Visibility = esPago ? Visibility.Collapsed : Visibility.Visible;
            PanelDocumentosPagados.Visibility = esPago ? Visibility.Visible : Visibility.Collapsed;

            if (esPago)
            {
                var pagos = BrosSatDb.ObtenerPagosDocumentos(conn, c.CfdiID);
                GridDocumentosPagados.ItemsSource = pagos.Select(p => new PagoDoctoRow
                {
                    UUIDRelacionado = p.UUIDRelacionado.ToString(),
                    SerieFolio = (string.IsNullOrEmpty(p.Serie) ? "" : p.Serie + " ") + (p.Folio ?? "-"),
                    FechaPago = p.FechaPago?.ToString("yyyy-MM-dd") ?? "-",
                    FormaDePago = p.FormaDePago ?? "-",
                    NumParcialidad = p.NumParcialidad?.ToString() ?? "-",
                    ImpSaldoAnt = Moneda(p.ImpSaldoAnt),
                    ImpPagado = Moneda(p.ImpPagado),
                    ImpSaldoInsoluto = Moneda(p.ImpSaldoInsoluto)
                }).ToList();
            }
            else
            {
                var conceptos = BrosSatDb.ObtenerConceptos(conn, c.CfdiID);
                GridConceptos.ItemsSource = conceptos.Select(co => new ConceptoRow
                {
                    ClaveProdServ = co.ClaveProdServ ?? "-",
                    Descripcion = co.Descripcion ?? "-",
                    Cantidad = co.Cantidad?.ToString("N2", CultureInfo.InvariantCulture) ?? "-",
                    ValorUnitario = Moneda(co.ValorUnitario),
                    Importe = Moneda(co.Importe)
                }).ToList();
            }
        }

        private static string DescribirTipo(string t) => t switch
        {
            "I" => "Ingreso",
            "E" => "Egreso",
            "N" => "Nómina",
            "T" => "Traslado",
            "P" => "Pago",
            _ => t ?? "-"
        };

        private static string Moneda(decimal? valor) => valor?.ToString("N2", CultureInfo.InvariantCulture) ?? "-";

        // ---- En los sistemas (lo que dejo la ultima verificacion de «Mapa de CFDI») ----

        private string _cadenaComercial;

        private void CargarSistemas(Guid uuid, string rfcEmisor, string rfcReceptor)
        {
            try
            {
                var empresa = BrosSatDb.ObtenerEmpresas(_conn).FirstOrDefault(e => e.RFC == rfcEmisor || e.RFC == rfcReceptor);
                if (empresa == null) { PanelSistemas.Visibility = Visibility.Collapsed; return; }
                var verif = MapaCfdi.ObtenerVerificaciones(_conn, empresa.RFC);
                if (verif.Count == 0) { ValSistemas.Text = "Todavía no se ha verificado dónde está este CFDI. Usa «Verificar ahora» en «Mapa de CFDI»."; PanelDocsComercial.Visibility = Visibility.Collapsed; PanelPolizas.Visibility = Visibility.Collapsed; return; }

                var est = MapaCfdi.ObtenerEstados(_conn, empresa.RFC, uuid);
                string Linea(string sistema)
                {
                    verif.TryGetValue(sistema, out var v);
                    if (v == null || !v.Configurado) return SistemaCfdi.Nombre(sistema) + ": sin configurar";
                    if (!v.Ok) return SistemaCfdi.Nombre(sistema) + ": no se pudo verificar";
                    return SistemaCfdi.Nombre(sistema) + ": " + (est.TryGetValue(sistema, out var e) ? e.estado : "no está");
                }
                ValSistemas.Text = string.Join("   ·   ", new[] { SistemaCfdi.Disco, SistemaCfdi.Comercial, SistemaCfdi.Premium, SistemaCfdi.Add, SistemaCfdi.Polizas }.Select(Linea));

                // Comercial: documentos generados + «Ver documento».
                _cadenaComercial = string.IsNullOrWhiteSpace(empresa.ComercialConexionSql) ? null : DpapiHelper.DescifrarConexionSql(empresa.ComercialConexionSql);
                var docs = MapaCfdi.ObtenerDocumentosComercial(_conn, empresa.RFC, uuid);
                GridDocsComercial.ItemsSource = docs;
                var conDif = docs.FirstOrDefault(d => d.Diferencia.HasValue);
                LblDiferencia.Visibility = conDif == null ? Visibility.Collapsed : Visibility.Visible;
                if (conDif != null) LblDiferencia.Text = "El total del documento (" + conDif.Total.ToString("N2") + ") no coincide con el del CFDI (" + conDif.TotalCfdi.Value.ToString("N2") + "): diferencia de " + conDif.DiferenciaTexto +
                    ". Suele ser una retención, un descuento o un impuesto que falta en el documento.";
                PanelDocsComercial.Visibility = _cadenaComercial == null && !docs.Any(d => d.Sistema == "Premium") ? Visibility.Collapsed : Visibility.Visible;
                if (docs.Count > 0) GridDocsComercial.SelectedIndex = 0;
                if (docs.Count > 0 && ValVinculo.Text.StartsWith("Sin vincular"))
                    ValVinculo.Text = string.Join("; ", docs.Select(d => (d.Sistema == "Premium" ? "Premium " : "Documento ") + d.Folio + (d.Cancelado ? " (cancelado)" : "")));

                // Contabilidad: historial de polizas.
                var polizas = MapaCfdi.ObtenerPolizas(_conn, empresa.RFC, uuid);
                GridPolizas.ItemsSource = polizas;
                PanelPolizas.Visibility = BrosSatDb.ObtenerContabilidad(_conn, empresa.RFC) == null ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) { ValSistemas.Text = "No se pudo leer el estado en los sistemas: " + ex.Message; }
        }

        private void GridDocsComercial_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => BtnVerDocumento.IsEnabled = GridDocsComercial.SelectedItem is DocumentoComercialCfdi d && d.Sistema == "Comercial" && _cadenaComercial != null;
        private void GridDocsComercial_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => AbrirDocumentoSeleccionado();
        private void BtnVerDocumento_Click(object sender, RoutedEventArgs e) => AbrirDocumentoSeleccionado();

        // No es modal: se pueden ver varios documentos y seguir usando Descargas.
        private void AbrirDocumentoSeleccionado()
        {
            if (!(GridDocsComercial.SelectedItem is DocumentoComercialCfdi d) || d.Sistema != "Comercial" || _cadenaComercial == null) return;   // Premium: solo se informa, no hay vista previa
            new DocumentoComercialWindow(_cadenaComercial, d.DocumentId) { Owner = this }.Show();
        }

        private async void BtnVerificarEstatus_Click(object sender, RoutedEventArgs e)
        {
            var c = BrosSatDb.ObtenerCfdiDetalle(_conn, _cfdiId);
            bool esPago = string.Equals(c?.TipoComprobante, "P", System.StringComparison.OrdinalIgnoreCase);
            if (c == null || (!esPago && c.Total == null))
            {
                MessageBox.Show(this, "No se puede verificar: falta el Total del CFDI (necesario para armar la consulta).", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnVerificarEstatus.IsEnabled = false;
            try
            {
                var resultado = await SatSoapClient.ConsultarEstatusCfdiAsync(c.UUID.ToString(), c.RFCEmisor, c.RFCReceptor, BrosSatDb.TotalParaVerificarEstatus(c));
                if (!resultado.Exito)
                {
                    MessageBox.Show(this, "No se pudo verificar el estatus:\n\n" + resultado.Error, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                BrosSatDb.ActualizarEstatusCfdi(_conn, _cfdiId, resultado.Estado, resultado.EstatusCancelacion, "Consulta", resultado.ValidacionEFOS);
                if (resultado.Estado == "Vigente" || resultado.Estado == "Cancelado") ValEstatus.Text = resultado.Estado;
                MessageBox.Show(this, "Estatus del SAT: " + resultado.Estado +
                    (string.IsNullOrEmpty(resultado.EstatusCancelacion) ? "" : "\nCancelación: " + resultado.EstatusCancelacion),
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                BtnVerificarEstatus.IsEnabled = true;
            }
        }

        private void BtnAbrirCarpeta_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _rutaArchivoXml + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la carpeta:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
