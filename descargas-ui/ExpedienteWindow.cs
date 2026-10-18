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
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // Opciones del expediente (periodo, tipo, nomina, cancelados) y generacion del .xlsx. No bloquea la ventana principal mientras trabaja.
    internal sealed class ExpedienteWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc, _nombre;
        private readonly DatePicker _desde = new DatePicker(), _hasta = new DatePicker();
        private readonly ComboBox _direccion = new ComboBox();
        private readonly CheckBox _nomina = new CheckBox { Content = "Incluir nómina (suele ser la mayoría de los XML)" }, _cancelados = new CheckBox { Content = "Incluir cancelados", IsChecked = true };
        private readonly TextBlock _estado = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MinHeight = 38 };
        private readonly Button _generar = new Button { Content = "Generar expediente" };

        public ExpedienteWindow(SqlConnection conn, string rfc, string nombre)
        {
            _conn = conn; _rfc = rfc; _nombre = nombre;
            Title = "Expediente de CFDI"; Width = 560; Height = 470; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "PincelLienzo");
            var p = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
            p.Children.Add(new TextBlock { Text = "Expediente de CFDI", FontSize = 20, FontWeight = FontWeights.Bold });
            var sub = new TextBlock { Text = "Un Excel con todos los datos de los XML del periodo, pagos, IVA por flujo de efectivo y observaciones: para una devolución de impuestos, una aclaración al SAT o una revisión.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "PincelTexto2"); p.Children.Add(sub);

            var hoy = DateTime.Today; _desde.SelectedDate = new DateTime(hoy.Year, 1, 1); _hasta.SelectedDate = hoy;
            var fila = new Grid(); fila.ColumnDefinitions.Add(new ColumnDefinition()); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); fila.ColumnDefinitions.Add(new ColumnDefinition());
            var g1 = Campo("Desde", _desde); var g2 = Campo("Hasta", _hasta); Grid.SetColumn(g2, 2); fila.Children.Add(g1); fila.Children.Add(g2); p.Children.Add(fila);

            _direccion.ItemsSource = new[] { "Ambos (recibidos y emitidos)", "Solo recibidos (gastos / IVA acreditable)", "Solo emitidos (ingresos / IVA trasladado)" }; _direccion.SelectedIndex = 0; _direccion.MinHeight = 30;
            p.Children.Add(Campo("Qué CFDI", _direccion));
            _nomina.Margin = new Thickness(0, 4, 0, 6); _cancelados.Margin = new Thickness(0, 0, 0, 4); p.Children.Add(_nomina); p.Children.Add(_cancelados);
            p.Children.Add(_estado);

            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            _generar.Style = (Style)FindResource("Primary"); _generar.Click += async (s, e) => await GenerarAsync();
            var cerrar = new Button { Content = "Cerrar", Style = (Style)FindResource("Ghost"), Margin = new Thickness(8, 0, 0, 0) }; cerrar.Click += (s, e) => Close();
            botones.Children.Add(_generar); botones.Children.Add(cerrar); p.Children.Add(botones);
            Content = p;
        }

        private static StackPanel Campo(string etiqueta, Control c)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var t = new TextBlock { Text = etiqueta, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }; t.SetResourceReference(TextBlock.ForegroundProperty, "PincelTexto2");
            s.Children.Add(t); s.Children.Add(c); return s;
        }

        private async Task GenerarAsync()
        {
            if (!_desde.SelectedDate.HasValue || !_hasta.SelectedDate.HasValue || _desde.SelectedDate > _hasta.SelectedDate) { Mensaje("Elige un periodo válido (la fecha inicial no puede ser mayor a la final).", true); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "expediente_" + _rfc + "_" + _desde.SelectedDate.Value.ToString("yyyyMMdd") + "_" + _hasta.SelectedDate.Value.ToString("yyyyMMdd") + ".xlsx" };
            if (dlg.ShowDialog(this) != true) return;

            var o = new ExpedienteOpciones
            {
                Rfc = _rfc, Desde = _desde.SelectedDate.Value, Hasta = _hasta.SelectedDate.Value, Direccion = _direccion.SelectedIndex == 1 ? "Recibidos" : _direccion.SelectedIndex == 2 ? "Emitidos" : "Ambos",
                IncluirNomina = _nomina.IsChecked == true, IncluirCancelados = _cancelados.IsChecked == true
            };
            _generar.IsEnabled = false; Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var datos = await Task.Run(() => Expediente.Construir(_conn, o, t => Dispatcher.Invoke(() => Mensaje(t, false))));
                if (datos.Cfdis.Count == 0) { Mensaje("No hay CFDI en ese periodo con esas opciones.", true); return; }
                Mensaje("Armando el Excel (" + datos.Cfdis.Count.ToString("N0") + " CFDI)...", false);
                await Task.Run(() => ExpedienteExcel.Generar(datos, _nombre, dlg.FileName));
                Mensaje("Listo: " + datos.Cfdis.Count.ToString("N0") + " CFDI, " + datos.Pagos.Count.ToString("N0") + " pagos aplicados, " + datos.Observaciones.Count.ToString("N0") + " observaciones" +
                    (datos.XmlNoLeidos > 0 ? " (" + datos.XmlNoLeidos + " XML no se pudieron leer)" : "") + ".\n" + dlg.FileName, false);
                if (MessageBox.Show(this, "Expediente guardado:\n" + dlg.FileName + "\n\n¿Abrirlo ahora?", "Expediente de CFDI", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex) { Mensaje("No se pudo generar: " + ex.Message, true); }
            finally { _generar.IsEnabled = true; Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        private void Mensaje(string texto, bool error)
        {
            _estado.Text = texto;
            _estado.Foreground = error ? System.Windows.Media.Brushes.Firebrick : (System.Windows.Media.Brush)FindResource("PincelTexto2");
        }
    }
}
