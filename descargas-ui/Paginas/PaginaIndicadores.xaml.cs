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

// PaginaIndicadores.xaml.cs -- una lista de reglas con su conteo y, al elegir una, los comprobantes que la cumplen. Las reglas
// predefinidas y las propias del usuario salen de Indicadores.cs; todo se calcula con la base local, para la empresa activa.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaIndicadores : UserControl
    {
        private readonly Sesion _s;
        private readonly Dictionary<string, DataTable> _resultados = new Dictionary<string, DataTable>();
        private readonly Dictionary<string, string> _errores = new Dictionary<string, string>();
        private List<IndicadorDef> _defs = new List<IndicadorDef>();
        private IndicadorDef _actual;
        private string _claveInicial;

        public PaginaIndicadores(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Recalcular();
        }

        private Window Ventana => Window.GetWindow(this);
        private Brush P(string clave) => (Brush)FindResource(clave);

        private (DateTime desde, DateTime hasta) Periodo()
        {
            var hoy = DateTime.Today;
            switch ((CmbPeriodo.SelectedItem as ComboBoxItem)?.Tag as string)
            {
                case "mes": return (new DateTime(hoy.Year, hoy.Month, 1), hoy.AddDays(1));
                case "12m": return (hoy.AddMonths(-12), hoy.AddDays(1));
                case "anioant": return (new DateTime(hoy.Year - 1, 1, 1), new DateTime(hoy.Year, 1, 1));
                case "todo": return (new DateTime(2000, 1, 1), hoy.AddDays(1));
                default: return (new DateTime(hoy.Year, 1, 1), hoy.AddDays(1));
            }
        }

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Recalcular(); }
        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Recalcular();

        private void Recalcular()
        {
            var empresa = _s.Empresa;
            var (desde, hasta) = Periodo();
            Cursor = Cursors.Wait;
            try
            {
                _resultados.Clear(); _errores.Clear();
                _defs = Indicadores.Predefinidos.Concat(Indicadores.Personalizados(_s.Conn)).ToList();
                foreach (var d in _defs)
                {
                    try { _resultados[d.Clave] = Indicadores.Ejecutar(_s.Conn, d, empresa.RFC, desde, hasta); }
                    catch (Exception ex) { _errores[d.Clave] = ex.Message; }
                }
                var previo = _actual == null ? null : _defs.FirstOrDefault(x => x.Clave == _actual.Clave);
                if (previo == null) previo = _defs.OrderByDescending(x => CasosDe(x) > 0 ? 1 : 0).ThenByDescending(x => x.Nivel).FirstOrDefault();
                _actual = previo;
                PintarLista();
                if (_actual != null) Mostrar(_actual); else LimpiarDetalle();
            }
            finally { Cursor = Cursors.Arrow; }
        }

        private int CasosDe(IndicadorDef d) => _resultados.ContainsKey(d.Clave) ? _resultados[d.Clave].Rows.Count : 0;

        private void LimpiarDetalle()
        {
            _actual = null; LblTitulo.Text = "Elige un indicador"; LblDescripcion.Text = "";
            Rejilla.ItemsSource = null; BtnExcel.IsEnabled = false; BtnEditar.Visibility = BtnEliminar.Visibility = Visibility.Collapsed;
        }

        private void PintarLista()
        {
            Lista.Children.Clear();
            foreach (var d in _defs.OrderByDescending(x => CasosDe(x) > 0 ? 1 : 0).ThenByDescending(x => x.Nivel).ThenBy(x => x.Nombre))
            {
                int n = _resultados.ContainsKey(d.Clave) ? _resultados[d.Clave].Rows.Count : -1;
                bool conCasos = n > 0;
                bool activo = _actual != null && _actual.Clave == d.Clave;

                string estilo = n < 0 ? "PastillaNeutra" : !conCasos ? "PastillaOk" : d.Nivel == NivelIndicador.Critico ? "PastillaCritica" : d.Nivel == NivelIndicador.Aviso ? "PastillaAviso" : "PastillaInfo";
                var pastilla = new Border { Style = (Style)FindResource(estilo), MinWidth = 38, Child = new TextBlock { Text = n < 0 ? "!" : n.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Center } };
                pastilla.HorizontalAlignment = HorizontalAlignment.Right;

                var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                textos.Children.Add(new TextBlock { Text = d.Nombre, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                textos.Children.Add(new TextBlock
                {
                    Text = (d.EsPersonalizado ? "Propio · " : "") + (d.Nivel == NivelIndicador.Critico ? "Revisar primero" : d.Nivel == NivelIndicador.Aviso ? "Conviene revisar" : "Informativo"),
                    Style = (Style)FindResource("TextoTenue"), Margin = new Thickness(0, 1, 0, 0)
                });

                var fila = new Grid { Margin = new Thickness(14, 11, 14, 11) };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(pastilla, 1); pastilla.Margin = new Thickness(10, 0, 0, 0);
                fila.Children.Add(textos); fila.Children.Add(pastilla);

                var cuerpo = new Border
                {
                    Child = fila, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, Tag = d,
                    Background = activo ? P("PincelAcentoSuave") : P("PincelSuperficie"),
                    BorderBrush = activo ? P("PincelAcentoBorde") : P("PincelBorde"), BorderThickness = new Thickness(1)
                };
                cuerpo.MouseEnter += (s, e) => { if (!(_actual != null && _actual.Clave == ((IndicadorDef)((Border)s).Tag).Clave)) ((Border)s).Background = P("PincelSuperficieHover"); };
                cuerpo.MouseLeave += (s, e) => { if (!(_actual != null && _actual.Clave == ((IndicadorDef)((Border)s).Tag).Clave)) ((Border)s).Background = P("PincelSuperficie"); };
                cuerpo.MouseLeftButtonUp += (s, e) => Mostrar((IndicadorDef)((Border)s).Tag);
                Lista.Children.Add(cuerpo);
            }
        }

        private void Mostrar(IndicadorDef d)
        {
            _actual = d;
            PintarLista();
            LblTitulo.Text = d.Nombre;
            LblDescripcion.Text = d.Descripcion;
            BtnEditar.Visibility = BtnEliminar.Visibility = d.EsPersonalizado ? Visibility.Visible : Visibility.Collapsed;
            if (_errores.TryGetValue(d.Clave, out var err))
            {
                Rejilla.ItemsSource = null; BtnExcel.IsEnabled = false; Vacio.Visibility = Visibility.Visible;
                Vacio.Glifo = ""; Vacio.Titulo = "No se pudo calcular"; Vacio.Texto = err;
                return;
            }
            var t = _resultados[d.Clave];
            Rejilla.ItemsSource = t.DefaultView;
            BtnExcel.IsEnabled = t.Rows.Count > 0;
            Vacio.Visibility = t.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Vacio.Glifo = ""; Vacio.Titulo = "Sin casos en este periodo"; Vacio.Texto = "Ningún comprobante cumple esta regla.";
        }

        private void Rejilla_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "RFCEmisor" || e.PropertyName == "Serie" || e.PropertyName == "Moneda" || e.PropertyName == "UUID") { e.Cancel = true; return; }   // siguen en el Excel y en el detalle del CFDI (doble clic)
            if (e.PropertyType == typeof(DateTime) && e.Column is DataGridTextColumn tc) { tc.Binding.StringFormat = "dd/MM/yyyy"; tc.ElementStyle = (Style)FindResource("CeldaTexto"); }
            else if (e.PropertyType == typeof(decimal) && e.Column is DataGridTextColumn td) { td.Binding.StringFormat = "N2"; td.ElementStyle = (Style)FindResource("CeldaNumero"); }
            else if (e.Column is DataGridTextColumn tx) tx.ElementStyle = (Style)FindResource(e.PropertyName == "UUID" ? "CeldaAtenuadaMono" : "CeldaTexto");
            if (e.PropertyName == "Detalle") { e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); e.Column.MinWidth = 260; }
            if (e.PropertyName == "NombreEmisor") { e.Column.Width = 200; e.Column.Header = "EMISOR"; }
            if (e.PropertyName == "FechaEmision") { e.Column.Header = "FECHA"; e.Column.Width = 104; }
            if (e.PropertyName == "Tipo") e.Column.Width = 92;
            if (e.PropertyName == "Folio") e.Column.Width = 130;
            if (e.PropertyName == "Total") e.Column.Width = 120;
            if (e.Column.Header is string h && e.PropertyName != "NombreEmisor" && e.PropertyName != "FechaEmision") e.Column.Header = h.ToUpperInvariant();
        }

        private void Rejilla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(Rejilla.SelectedItem is DataRowView r) || !r.Row.Table.Columns.Contains("UUID")) return;
            try
            {
                using (var cmd = new SqlCommand("SELECT CfdiID FROM CfdiRecibido WHERE UUID=@u", _s.Conn))
                {
                    cmd.Parameters.AddWithValue("@u", Guid.Parse(Convert.ToString(r["UUID"])));
                    var id = cmd.ExecuteScalar();
                    if (id != null && id != DBNull.Value) new CfdiDetalleWindow(_s.Conn, Convert.ToInt32(id)) { Owner = Ventana }.ShowDialog();
                }
            }
            catch { }
        }

        private void BtnExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_actual == null || !_resultados.TryGetValue(_actual.Clave, out var t) || t.Rows.Count == 0) return;
            var dlg = new SaveFileDialog { Filter = "Libro de Excel (*.xlsx)|*.xlsx", FileName = "Indicador_" + _actual.Clave + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx" };
            if (dlg.ShowDialog(Ventana) != true) return;
            try
            {
                using (var libro = new ClosedXML.Excel.XLWorkbook())
                {
                    var hoja = libro.Worksheets.Add(t, "Casos");
                    hoja.Columns().AdjustToContents();
                    libro.SaveAs(dlg.FileName);
                }
                if (MessageBox.Show(Ventana, "Se guardó el archivo. ¿Abrirlo ahora?", "Indicadores", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo { FileName = dlg.FileName, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo exportar:\n\n" + ex.Message, "Indicadores", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e) => Editar(null);
        private void BtnEditar_Click(object sender, RoutedEventArgs e) { if (_actual != null && _actual.EsPersonalizado) Editar(_actual); }

        private void Editar(IndicadorDef def)
        {
            var (desde, hasta) = Periodo();
            var ed = new IndicadorEditorWindow(_s.Conn, def, _s.Empresa.RFC, desde, hasta) { Owner = Ventana };
            if (ed.ShowDialog() == true) { _actual = _defs.FirstOrDefault(x => x.PersonalizadoID == ed.GuardadoID) ?? _actual; Recalcular(); if (ed.GuardadoID > 0) { _actual = _defs.FirstOrDefault(x => x.PersonalizadoID == ed.GuardadoID) ?? _actual; if (_actual != null) Mostrar(_actual); } _s.CalcularResumen(); }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (_actual == null || !_actual.EsPersonalizado) return;
            if (MessageBox.Show(Ventana, "¿Eliminar el indicador «" + _actual.Nombre + "»?", "Indicadores", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Indicadores.EliminarPersonalizado(_s.Conn, _actual.PersonalizadoID);
            _actual = null; Recalcular();
        }
    }
}
