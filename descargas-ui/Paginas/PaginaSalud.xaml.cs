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

// PaginaSalud.xaml.cs -- un semaforo por empresa con todo lo que podria estar mal (servicio detenido, huecos, XML faltantes, estatus sin
// verificar, e.firma por vencer, archivos perdidos...). Solo lee la base local. La empresa activa aparece primero.

using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaSalud : UserControl
    {
        private readonly Sesion _s;
        private string _textoPlano = "";

        public PaginaSalud(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private Brush P(string clave) => (Brush)FindResource(clave);
        private static string Etiqueta(NivelSalud n) => n == NivelSalud.Critico ? "Crítico" : n == NivelSalud.Aviso ? "Aviso" : "Todo bien";
        private string EstiloPastilla(NivelSalud n) => n == NivelSalud.Critico ? "PastillaCritica" : n == NivelSalud.Aviso ? "PastillaAviso" : "PastillaOk";
        private Brush ColorNivel(NivelSalud n) => P(n == NivelSalud.Critico ? "PincelCritico" : n == NivelSalud.Aviso ? "PincelAviso" : "PincelOk");

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) { Cargar(); _s.CalcularResumen(); }

        private void Cargar()
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                Contenido.Children.Clear();
                var texto = new StringBuilder();
                var activas = BrosSatDb.ObtenerEmpresas(_s.Conn).Where(e => e.Activa).OrderByDescending(e => _s.Empresa != null && e.EmpresaID == _s.Empresa.EmpresaID).ToList();
                if (activas.Count == 0)
                {
                    Resumen.Style = (Style)FindResource("BandaNeutra"); Resumen.HorizontalAlignment = HorizontalAlignment.Stretch;
                    LblResumen.Text = "No hay empresas activas en el catálogo.";
                    return;
                }
                int criticos = 0, avisos = 0;
                foreach (var empresa in activas)
                {
                    var hallazgos = Salud.Evaluar(_s.Conn, empresa);
                    var peor = Salud.Peor(hallazgos);
                    if (peor == NivelSalud.Critico) criticos++; else if (peor == NivelSalud.Aviso) avisos++;

                    var panel = new StackPanel();
                    var encabezado = new Grid { Margin = new Thickness(22, 16, 22, 12) };
                    encabezado.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    encabezado.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var nombre = new StackPanel { Orientation = Orientation.Horizontal };
                    nombre.Children.Add(new TextBlock { Text = empresa.Nombre, Style = (Style)FindResource("TextoSubtitulo"), VerticalAlignment = VerticalAlignment.Center });
                    nombre.Children.Add(new TextBlock { Text = empresa.RFC, Style = (Style)FindResource("TextoMono"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
                    var pastilla = new Border { Style = (Style)FindResource(EstiloPastilla(peor)), Child = new TextBlock { Text = Etiqueta(peor) } };
                    Grid.SetColumn(pastilla, 1);
                    encabezado.Children.Add(nombre); encabezado.Children.Add(pastilla);
                    panel.Children.Add(encabezado);
                    panel.Children.Add(new Border { Height = 1, Background = P("PincelBorde") });
                    texto.AppendLine(empresa.Nombre + " (" + empresa.RFC + ") — " + Etiqueta(peor));

                    var filas = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                    foreach (var h in hallazgos.OrderByDescending(x => x.Nivel))
                    {
                        var fila = new Grid { Margin = new Thickness(22, 7, 22, 7) };
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        var punto = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = ColorNivel(h.Nivel), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
                        var tema = new TextBlock { Text = h.Tema, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                        var detalle = new TextBlock { Text = h.Detalle, Style = (Style)FindResource("TextoSecundario") };
                        Grid.SetColumn(tema, 1); Grid.SetColumn(detalle, 2);
                        fila.Children.Add(punto); fila.Children.Add(tema); fila.Children.Add(detalle);
                        filas.Children.Add(fila);
                        texto.AppendLine("  [" + Etiqueta(h.Nivel) + "] " + h.Tema + ": " + h.Detalle);
                    }
                    panel.Children.Add(filas);
                    texto.AppendLine();
                    Contenido.Children.Add(new Border { Style = (Style)FindResource("Tarjeta"), Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(0, 0, 0, 0), Child = panel });
                }
                bool todoBien = criticos == 0 && avisos == 0;
                Resumen.Style = (Style)FindResource(criticos > 0 ? "BandaCritica" : avisos > 0 ? "BandaAviso" : "BandaOk"); Resumen.HorizontalAlignment = HorizontalAlignment.Stretch;
                LblResumen.Text = (todoBien ? "Todo en orden." : criticos + " empresa(s) en crítico, " + avisos + " con avisos.") + "  Revisado " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + ".";
                _textoPlano = texto.ToString();
            }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        private void BtnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_textoPlano)) return;
            Clipboard.SetText(_textoPlano);
            MessageBox.Show(Window.GetWindow(this), "El resumen de salud se copió al portapapeles.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
