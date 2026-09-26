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

                BrosSatDb.ActualizarEstatusCfdi(_conn, _cfdiId, resultado.Estado);
                ValEstatus.Text = resultado.Estado;
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
