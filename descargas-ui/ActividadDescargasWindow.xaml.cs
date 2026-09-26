using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI
{
    // Calendario basado exclusivamente en FechaDescarga: un día verde acredita archivos ya
    // almacenados y evita confundir una solicitud aceptada con una descarga terminada.
    public partial class ActividadDescargasWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly EmpresaRow _empresa;
        private DateTime _mes;

        public ActividadDescargasWindow(SqlConnection conn, EmpresaRow empresa)
        {
            InitializeComponent();
            _conn = conn; _empresa = empresa;
            _mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            LblSubtitulo.Text = empresa.Nombre + "  ·  " + empresa.RFC + "  ·  evidencia diaria de XML almacenados";
            foreach (var dia in new[] { "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom" })
                CabeceraDias.Children.Add(new TextBlock { Text = dia, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), HorizontalAlignment = HorizontalAlignment.Center });
            Loaded += (s, e) => Cargar();
        }

        private void Cargar()
        {
            DateTime siguiente = _mes.AddMonths(1);
            var actividad = BrosSatDb.ObtenerActividadDescargas(_conn, _empresa.RFC, _mes, siguiente);
            var porDia = actividad.ToDictionary(x => x.Fecha.Date, x => x);
            LblMes.Text = _mes.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-MX"));
            LblMesCantidad.Text = actividad.Sum(x => x.Cantidad).ToString("N0");
            LblDiasActivos.Text = actividad.Count.ToString("N0");
            var ultima = actividad.OrderByDescending(x => x.Fecha).FirstOrDefault();
            LblUltima.Text = ultima == null ? "Sin registros" : ultima.Fecha.ToString("dd MMM") + " · " + ultima.Cantidad.ToString("N0") + " XML";
            Calendario.Children.Clear();
            int desplazamiento = ((int)_mes.DayOfWeek + 6) % 7;
            for (int i = 0; i < desplazamiento; i++) Calendario.Children.Add(new Border { Background = Brushes.Transparent, Margin = new Thickness(4) });
            for (int dia = 1; dia <= DateTime.DaysInMonth(_mes.Year, _mes.Month); dia++)
            {
                var fecha = new DateTime(_mes.Year, _mes.Month, dia);
                porDia.TryGetValue(fecha, out var dato);
                bool activo = dato != null;
                var fondo = activo ? Color.FromRgb(231, 246, 238) : Color.FromRgb(248, 250, 252);
                var borde = activo ? Color.FromRgb(167, 227, 186) : Color.FromRgb(226, 232, 240);
                var contenido = new StackPanel { Margin = new Thickness(10, 8, 8, 8) };
                contenido.Children.Add(new TextBlock { Text = dia.ToString(), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(22, 38, 58)) });
                contenido.Children.Add(new TextBlock { Text = activo ? dato.Cantidad.ToString("N0") + " XML" : "Sin descarga", FontSize = 11, Margin = new Thickness(0, 7, 0, 0), Foreground = new SolidColorBrush(activo ? Color.FromRgb(22, 128, 59) : Color.FromRgb(148, 163, 184)) });
                Calendario.Children.Add(new Border { MinHeight = 73, Margin = new Thickness(4), Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(fondo), BorderBrush = new SolidColorBrush(borde), BorderThickness = new Thickness(1), Child = contenido, ToolTip = activo ? fecha.ToString("dd/MM/yyyy") + ": " + dato.Cantidad.ToString("N0") + " XML descargados" : fecha.ToString("dd/MM/yyyy") + ": sin XML descargados" });
            }
        }
        private void BtnAnterior_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(-1); Cargar(); }
        private void BtnSiguiente_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(1); Cargar(); }
    }
}
