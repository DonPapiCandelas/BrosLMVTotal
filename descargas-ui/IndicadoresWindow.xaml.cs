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

namespace BrosLMV.DescargasUI
{
    // Indicadores fiscales: una lista de reglas con su conteo y, al elegir una, los comprobantes que la cumplen.
    // Las reglas predefinidas y las propias del usuario salen de Indicadores.cs; todo se calcula con la base local.
    public partial class IndicadoresWindow : Window
    {
        private readonly SqlConnection _conn;
        private List<EmpresaFila> _empresas = new List<EmpresaFila>();
        private readonly Dictionary<string, DataTable> _resultados = new Dictionary<string, DataTable>();
        private readonly Dictionary<string, string> _errores = new Dictionary<string, string>();
        private List<IndicadorDef> _defs = new List<IndicadorDef>();
        private IndicadorDef _actual;
        private bool _cargando;

        public IndicadoresWindow(SqlConnection conn)
        {
            InitializeComponent();
            _conn = conn;
            Loaded += (s, e) =>
            {
                _empresas = BrosSatDb.ObtenerEmpresas(_conn).Where(x => x.Activa).ToList();
                _cargando = true;
                CmbEmpresa.ItemsSource = _empresas.Select(x => x.Nombre + "  ·  " + x.RFC).ToList();
                if (_empresas.Count > 0) CmbEmpresa.SelectedIndex = 0;
                _cargando = false;
                Recalcular();
            };
        }

        private static Color ColorDe(NivelIndicador n) =>
            n == NivelIndicador.Critico ? Color.FromRgb(200, 40, 40) : n == NivelIndicador.Aviso ? Color.FromRgb(217, 143, 12) : Color.FromRgb(71, 105, 160);

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

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !_cargando) Recalcular(); }
        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Recalcular();

        private void Recalcular()
        {
            int i = CmbEmpresa.SelectedIndex;
            if (i < 0 || i >= _empresas.Count) { Lista.Children.Clear(); return; }
            var empresa = _empresas[i];
            var (desde, hasta) = Periodo();
            Cursor = Cursors.Wait;
            try
            {
                _resultados.Clear(); _errores.Clear();
                _defs = Indicadores.Predefinidos.Concat(Indicadores.Personalizados(_conn)).ToList();
                foreach (var d in _defs)
                {
                    try { _resultados[d.Clave] = Indicadores.Ejecutar(_conn, d, empresa.RFC, desde, hasta); }
                    catch (Exception ex) { _errores[d.Clave] = ex.Message; }
                }
                PintarLista();
                var previo = _actual == null ? null : _defs.FirstOrDefault(x => x.Clave == _actual.Clave);
                if (previo != null) Mostrar(previo); else LimpiarDetalle();
            }
            finally { Cursor = Cursors.Arrow; }
        }

        private void LimpiarDetalle()
        {
            _actual = null; LblTitulo.Text = "Elige un indicador"; LblDescripcion.Text = "";
            Rejilla.ItemsSource = null; BtnExcel.IsEnabled = false; BtnEditar.Visibility = BtnEliminar.Visibility = Visibility.Collapsed;
        }

        private void PintarLista()
        {
            Lista.Children.Clear();
            foreach (var d in _defs.OrderByDescending(x => (_resultados.ContainsKey(x.Clave) ? _resultados[x.Clave].Rows.Count : 0) > 0 ? 1 : 0).ThenByDescending(x => x.Nivel))
            {
                int n = _resultados.ContainsKey(d.Clave) ? _resultados[d.Clave].Rows.Count : -1;
                bool conCasos = n > 0;
                var fila = new DockPanel { Margin = new Thickness(12, 9, 12, 9) };
                var pastilla = new Border
                {
                    Background = new SolidColorBrush(n < 0 ? Color.FromRgb(148, 163, 184) : conCasos ? ColorDe(d.Nivel) : Color.FromRgb(22, 128, 59)),
                    CornerRadius = new CornerRadius(11), MinWidth = 38, Padding = new Thickness(9, 2, 9, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                    Child = new TextBlock { Text = n < 0 ? "!" : n.ToString("N0"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
                };
                DockPanel.SetDock(pastilla, Dock.Right);
                fila.Children.Add(pastilla);
                var textos = new StackPanel();
                textos.Children.Add(new TextBlock { Text = d.Nombre, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)), TextWrapping = TextWrapping.Wrap });
                textos.Children.Add(new TextBlock
                {
                    Text = (d.EsPersonalizado ? "Propio · " : "") + (d.Nivel == NivelIndicador.Critico ? "Revisar primero" : d.Nivel == NivelIndicador.Aviso ? "Conviene revisar" : "Informativo"),
                    FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                });
                fila.Children.Add(textos);
                var cuerpo = new Border
                {
                    Child = fila, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand,
                    Background = _actual != null && _actual.Clave == d.Clave ? new SolidColorBrush(Color.FromRgb(226, 235, 250)) : Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1), Tag = d
                };
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
                Rejilla.ItemsSource = null; BtnExcel.IsEnabled = false;
                LblDescripcion.Text = d.Descripcion + "\n\nNo se pudo calcular: " + err;
                return;
            }
            var t = _resultados[d.Clave];
            Rejilla.ItemsSource = t.DefaultView;
            BtnExcel.IsEnabled = t.Rows.Count > 0;
            if (t.Rows.Count == 0) LblDescripcion.Text = d.Descripcion + "\n\nSin casos en este periodo.";
        }

        private void Rejilla_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyType == typeof(DateTime) && e.Column is DataGridTextColumn tc) tc.Binding.StringFormat = "dd/MM/yyyy";
            else if (e.PropertyType == typeof(decimal) && e.Column is DataGridTextColumn td)
            {
                td.Binding.StringFormat = "N2";
                td.ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
            }
            if (e.PropertyName == "Detalle") { e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); e.Column.MinWidth = 260; }
            if (e.PropertyName == "RFCEmisor" || e.PropertyName == "Serie") { e.Cancel = true; return; }   // siguen en el Excel y en el detalle del CFDI
            if (e.PropertyName == "NombreEmisor") { e.Column.Width = 190; e.Column.Header = "Emisor"; }
            if (e.PropertyName == "FechaEmision") e.Column.Header = "Fecha";
            if (e.PropertyName == "Moneda") e.Column.Width = 76;
            if (e.PropertyName == "UUID") e.Column.Width = 290;
        }

        private void Rejilla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(Rejilla.SelectedItem is DataRowView r) || !r.Row.Table.Columns.Contains("UUID")) return;
            try
            {
                using (var cmd = new SqlCommand("SELECT CfdiID FROM CfdiRecibido WHERE UUID=@u", _conn))
                {
                    cmd.Parameters.AddWithValue("@u", Guid.Parse(Convert.ToString(r["UUID"])));
                    var id = cmd.ExecuteScalar();
                    if (id != null && id != DBNull.Value) new CfdiDetalleWindow(_conn, Convert.ToInt32(id)) { Owner = this }.ShowDialog();
                }
            }
            catch { }
        }

        private void BtnExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_actual == null || !_resultados.TryGetValue(_actual.Clave, out var t) || t.Rows.Count == 0) return;
            var dlg = new SaveFileDialog { Filter = "Libro de Excel (*.xlsx)|*.xlsx", FileName = "Indicador_" + _actual.Clave + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                using (var libro = new ClosedXML.Excel.XLWorkbook())
                {
                    var hoja = libro.Worksheets.Add(t, "Casos");
                    hoja.Columns().AdjustToContents();
                    libro.SaveAs(dlg.FileName);
                }
                if (MessageBox.Show(this, "Se guardó el archivo. ¿Abrirlo ahora?", "Indicadores", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo { FileName = dlg.FileName, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo exportar:\n\n" + ex.Message, "Indicadores", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e) => Editar(null);
        private void BtnEditar_Click(object sender, RoutedEventArgs e) { if (_actual != null && _actual.EsPersonalizado) Editar(_actual); }

        private void Editar(IndicadorDef def)
        {
            int i = CmbEmpresa.SelectedIndex;
            if (i < 0 || i >= _empresas.Count) return;
            var (desde, hasta) = Periodo();
            var ed = new IndicadorEditorWindow(_conn, def, _empresas[i].RFC, desde, hasta) { Owner = this };
            if (ed.ShowDialog() == true) { _actual = _defs.FirstOrDefault(x => x.PersonalizadoID == ed.GuardadoID) ?? _actual; Recalcular(); }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (_actual == null || !_actual.EsPersonalizado) return;
            if (MessageBox.Show(this, "¿Eliminar el indicador «" + _actual.Nombre + "»?", "Indicadores", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Indicadores.EliminarPersonalizado(_conn, _actual.PersonalizadoID);
            _actual = null; Recalcular();
        }
    }

    // Editor de un indicador propio: nombre, importancia y la consulta. «Probar» la corre con la empresa y el periodo elegidos.
    internal sealed class IndicadorEditorWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc; private readonly DateTime _desde, _hasta; private readonly int _id;
        private readonly TextBox _nombre = new TextBox(), _descripcion = new TextBox(), _sql = new TextBox();
        private readonly ComboBox _nivel = new ComboBox(); private readonly TextBlock _estado = new TextBlock();
        public int GuardadoID { get; private set; }

        private const string Ejemplo =
            "-- Parametros disponibles: @rfc (tu empresa), @desde y @hasta (el periodo elegido).\r\n" +
            "-- Devuelve al menos la columna UUID para poder abrir el detalle con doble clic.\r\n" +
            "SELECT c.UUID, c.FechaEmision, c.NombreEmisor, c.Total, c.Moneda\r\n" +
            "FROM CfdiRecibido c\r\n" +
            "WHERE c.RFCReceptor = @rfc AND c.EstatusSat = 'Vigente'\r\n" +
            "  AND c.FechaEmision >= @desde AND c.FechaEmision < @hasta\r\n" +
            "  AND c.Total > 50000\r\n" +
            "ORDER BY c.Total DESC";

        public IndicadorEditorWindow(SqlConnection conn, IndicadorDef def, string rfc, DateTime desde, DateTime hasta)
        {
            _conn = conn; _rfc = rfc; _desde = desde; _hasta = hasta; _id = def?.PersonalizadoID ?? 0;
            Title = def == null ? "Nuevo indicador" : "Editar indicador"; Width = 780; Height = 640; MinWidth = 620; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(244, 246, 249));
            var raiz = new Grid { Margin = new Thickness(22) };
            for (int k = 0; k < 6; k++) raiz.RowDefinitions.Add(new RowDefinition { Height = k == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Etiquetar(raiz, 0, "Nombre", _nombre);
            Etiquetar(raiz, 1, "Qué revisa (opcional)", _descripcion);
            _nivel.ItemsSource = new[] { "Informativo", "Conviene revisar", "Revisar primero" }; _nivel.SelectedIndex = def == null ? 1 : (int)def.Nivel; _nivel.Width = 200; _nivel.HorizontalAlignment = HorizontalAlignment.Left;
            Etiquetar(raiz, 2, "Importancia", _nivel);
            _sql.AcceptsReturn = true; _sql.AcceptsTab = true; _sql.FontFamily = new FontFamily("Consolas"); _sql.FontSize = 13; _sql.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; _sql.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _sql.TextWrapping = TextWrapping.NoWrap; _sql.Text = def?.Sql ?? Ejemplo;
            var cajaSql = new DockPanel();
            var et = new TextBlock { Text = "Consulta SQL (solo lectura)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)) };
            DockPanel.SetDock(et, Dock.Top); cajaSql.Children.Add(et); cajaSql.Children.Add(_sql);
            Grid.SetRow(cajaSql, 3); raiz.Children.Add(cajaSql);
            _estado.TextWrapping = TextWrapping.Wrap; _estado.Margin = new Thickness(0, 8, 0, 8); _estado.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            Grid.SetRow(_estado, 4); raiz.Children.Add(_estado);
            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var probar = new Button { Content = "Probar", Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 0) }; probar.Click += (s, e) => Probar();
            var guardar = new Button { Content = "Guardar", Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 0, 8, 0) }; guardar.Click += (s, e) => Guardar();
            var cancelar = new Button { Content = "Cancelar", Style = (Style)FindResource("Ghost") }; cancelar.Click += (s, e) => DialogResult = false;
            botones.Children.Add(probar); botones.Children.Add(guardar); botones.Children.Add(cancelar);
            Grid.SetRow(botones, 5); raiz.Children.Add(botones);
            Content = raiz;
            if (def != null) { _nombre.Text = def.Nombre; _descripcion.Text = def.Descripcion; }
        }

        private static void Etiquetar(Grid g, int fila, string texto, Control c)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            p.Children.Add(new TextBlock { Text = texto, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)) });
            p.Children.Add(c); Grid.SetRow(p, fila); g.Children.Add(p);
        }

        private void Probar()
        {
            string motivo = Indicadores.ValidarSql(_sql.Text);
            if (motivo != null) { _estado.Text = "✗ " + motivo; return; }
            try
            {
                var t = Indicadores.Ejecutar(_conn, new IndicadorDef { Clave = "PRUEBA", Nombre = "Prueba", Sql = _sql.Text, PersonalizadoID = 0 }, _rfc, _desde, _hasta);
                _estado.Text = "✓ La consulta corre: " + t.Rows.Count.ToString("N0") + " fila(s) en el periodo elegido" + (t.Columns.Contains("UUID") ? "." : ". (Sin columna UUID no se podrá abrir el detalle.)");
            }
            catch (Exception ex) { _estado.Text = "✗ " + ex.Message; }
        }

        private void Guardar()
        {
            try
            {
                GuardadoID = Indicadores.GuardarPersonalizado(_conn, _id, _nombre.Text, _descripcion.Text, _sql.Text, (NivelIndicador)Math.Max(0, _nivel.SelectedIndex));
                DialogResult = true;
            }
            catch (Exception ex) { _estado.Text = "✗ " + ex.Message; }
        }
    }
}
