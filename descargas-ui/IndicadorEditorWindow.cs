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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
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
