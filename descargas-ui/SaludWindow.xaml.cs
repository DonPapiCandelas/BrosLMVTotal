using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // Pantalla "Salud": un semaforo por empresa con todo lo que podria estar mal (servicio detenido,
    // huecos, XML faltantes, estatus sin verificar, archivos perdidos...). Solo lee la base local.
    public partial class SaludWindow : Window
    {
        private readonly SqlConnection _conn;
        private string _textoPlano = "";

        public SaludWindow(SqlConnection conn)
        {
            InitializeComponent();
            _conn = conn;
            Loaded += (s, e) => Cargar();
        }

        internal static Color ColorDe(NivelSalud n) =>
            n == NivelSalud.Critico ? Color.FromRgb(200, 40, 40) : n == NivelSalud.Aviso ? Color.FromRgb(217, 143, 12) : Color.FromRgb(22, 128, 59);

        private static string Etiqueta(NivelSalud n) => n == NivelSalud.Critico ? "CRÍTICO" : n == NivelSalud.Aviso ? "AVISO" : "OK";

        private void Cargar()
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                Contenido.Children.Clear();
                var texto = new StringBuilder();
                var empresas = BrosSatDb.ObtenerEmpresas(_conn).Where(e => e.Activa).ToList();
                if (empresas.Count == 0)
                {
                    Contenido.Children.Add(new TextBlock { Text = "No hay empresas activas en el catálogo.", Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) });
                    return;
                }
                int criticos = 0, avisos = 0;
                foreach (var empresa in empresas)
                {
                    var hallazgos = Salud.Evaluar(_conn, empresa);
                    var peor = Salud.Peor(hallazgos);
                    if (peor == NivelSalud.Critico) criticos++; else if (peor == NivelSalud.Aviso) avisos++;

                    var panel = new StackPanel();
                    var encabezado = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                    encabezado.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(ColorDe(peor)), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 12, 0),
                        Child = new TextBlock { Text = Etiqueta(peor), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12 }
                    });
                    encabezado.Children.Add(new TextBlock { Text = empresa.Nombre + "  ·  " + empresa.RFC, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)) });
                    panel.Children.Add(encabezado);
                    texto.AppendLine(empresa.Nombre + " (" + empresa.RFC + ") — " + Etiqueta(peor));

                    foreach (var h in hallazgos.OrderByDescending(x => x.Nivel))
                    {
                        var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        var punto = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(ColorDe(h.Nivel)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
                        Grid.SetColumn(punto, 0);
                        var tema = new TextBlock { Text = h.Tema, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)), TextWrapping = TextWrapping.Wrap };
                        Grid.SetColumn(tema, 1);
                        var detalle = new TextBlock { Text = h.Detalle, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), TextWrapping = TextWrapping.Wrap };
                        Grid.SetColumn(detalle, 2);
                        fila.Children.Add(punto); fila.Children.Add(tema); fila.Children.Add(detalle);
                        panel.Children.Add(fila);
                        texto.AppendLine("  [" + Etiqueta(h.Nivel) + "] " + h.Tema + ": " + h.Detalle);
                    }
                    texto.AppendLine();
                    Contenido.Children.Add(new Border
                    {
                        Style = (Style)FindResource("Tarjeta"), Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(20),
                        Child = panel
                    });
                }
                LblResumen.Text = (criticos == 0 && avisos == 0 ? "Todo en orden." : criticos + " empresa(s) en crítico, " + avisos + " con avisos.") +
                    "  Revisado " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + ". Todo se calcula con los datos de este equipo; no se envía nada a ningún lado.";
                _textoPlano = texto.ToString();
            }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnCopiar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_textoPlano)) return;
            Clipboard.SetText(_textoPlano);
            MessageBox.Show(this, "El resumen de salud se copió al portapapeles.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
