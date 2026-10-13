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

// PaginaVinculos.xaml.cs -- cruza los XML de Comercial con sus documentos (solo lee): CFDI sin documento, con documento sugerido, y
// documentos vinculados cuyo total no coincide con el del CFDI. Requiere la conexion a Comercial de la empresa activa.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaVinculos : UserControl
    {
        private readonly Sesion _s;
        private bool _analizado;

        public class FilaSin
        {
            public string FechaTexto { get; set; } public string Tipo { get; set; } public string Rfc { get; set; }
            public string Nombre { get; set; } public string TotalTexto { get; set; } public string UUID { get; set; } public string Sugerido { get; set; }
        }

        public class FilaDif
        {
            public string Folio { get; set; } public string TotalCfdiTexto { get; set; } public string TotalDocTexto { get; set; }
            public string DiferenciaTexto { get; set; } public string UUID { get; set; }
        }

        public PaginaVinculos(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Cargar();
        private void Tab_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) MostrarTab(); }

        private void EstiloAviso(string estilo) { Aviso.Style = (Style)FindResource(estilo); Aviso.HorizontalAlignment = HorizontalAlignment.Stretch; }

        private void MostrarTab()
        {
            bool sin = TabSin.IsChecked == true;
            GridSinDocumento.Visibility = sin ? Visibility.Visible : Visibility.Collapsed;
            GridDiferencias.Visibility = sin ? Visibility.Collapsed : Visibility.Visible;
            int n = ((sin ? GridSinDocumento.ItemsSource : GridDiferencias.ItemsSource) as System.Collections.ICollection)?.Count ?? 0;
            Vacio.Visibility = n == 0 && _analizado ? Visibility.Visible : Visibility.Collapsed;
            Vacio.Titulo = sin ? "Todos los CFDI tienen documento" : "Todos los totales coinciden";
            Vacio.Texto = sin ? "No hay CFDI vigentes de los últimos 6 meses sin vincular a un documento." : "Los documentos vinculados traen el mismo total que su CFDI.";
        }

        private async void Cargar()
        {
            var emp = _s.Empresa;
            if (string.IsNullOrWhiteSpace(emp.ComercialConexionSql))
            {
                EstiloAviso("BandaAviso");
                LblResumen.Text = "Esta empresa no tiene configurada la conexión a Comercial. Configúrala en «Empresas y e.firma» → Editar.";
                return;
            }
            EstiloAviso("BandaNeutra");
            LblResumen.Text = "Analizando Comercial…";
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var r = await Vinculacion.AnalizarAsync(DpapiHelper.DescifrarConexionSql(emp.ComercialConexionSql));
                GridSinDocumento.ItemsSource = r.SinDocumento.Select(x => new FilaSin
                {
                    FechaTexto = x.Fecha.ToString("dd/MM/yyyy"), Tipo = x.Recibido ? "Recibido" : "Emitido", Rfc = x.RfcContraparte, Nombre = x.Nombre,
                    TotalTexto = x.Total.ToString("N2"), UUID = x.UUID.ToString(), Sugerido = x.MejorCandidato
                }).ToList();
                GridDiferencias.ItemsSource = r.Diferencias.Select(x => new FilaDif
                {
                    Folio = x.Folio, TotalCfdiTexto = x.TotalCfdi.ToString("N2"), TotalDocTexto = x.TotalDocumento.ToString("N2"),
                    DiferenciaTexto = x.Diferencia.ToString("N2"), UUID = x.UUID.ToString()
                }).ToList();
                int conSugerencia = r.SinDocumento.Count(x => x.Candidatos.Count > 0);
                bool todoBien = r.SinDocumento.Count == 0 && r.Diferencias.Count == 0;
                EstiloAviso(todoBien ? "BandaOk" : "BandaAviso");
                LblResumen.Text = todoBien ? "Todo vinculado: no hay CFDI sin documento ni totales distintos."
                    : r.SinDocumento.Count + " CFDI vigente(s) de los últimos 6 meses sin documento (" + conSugerencia + " con documento sugerido); " + r.Diferencias.Count + " documento(s) vinculado(s) con un total distinto al del CFDI.";
                TabSin.Content = "CFDI sin documento (" + r.SinDocumento.Count + ")";
                TabDif.Content = "Totales que no coinciden (" + r.Diferencias.Count + ")";
                _analizado = true; MostrarTab();
            }
            catch (Exception ex)
            {
                EstiloAviso("BandaCritica");
                LblResumen.Text = "No se pudo leer Comercial: " + ex.Message;
            }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }
    }
}
