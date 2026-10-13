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

// PaginaRegistro.xaml.cs -- dos vistas del historial: la actividad diaria de XML almacenados (calendario del mes) y la bitacora tecnica,
// que lee los logs que escribe BrosLMV.Descargas.Cola.Bitacora (carpeta fija en %LOCALAPPDATA%). Pedido del usuario 2026-08-14:
// "no me da bitacora o logs" -- todo salia por Console.WriteLine y se perdia con el proceso en segundo plano.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaRegistro : UserControl
    {
        private readonly Sesion _s;
        private DateTime _mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private static readonly CultureInfo Es = new CultureInfo("es-MX");

        public PaginaRegistro(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            foreach (var dia in new[] { "LUN", "MAR", "MIÉ", "JUE", "VIE", "SÁB", "DOM" })
                CabeceraDias.Children.Add(new TextBlock { Text = dia, Style = (Style)FindResource("TextoRotulo"), HorizontalAlignment = HorizontalAlignment.Center });
            LblRuta.Text = Bitacora.CarpetaLogs;
            CargarMeses();
            Loaded += (s, e) => CargarActividad();
        }

        private Brush P(string clave) => (Brush)FindResource(clave);

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool actividad = TabActividad.IsChecked == true;
            VistaActividad.Visibility = AccionesActividad.Visibility = actividad ? Visibility.Visible : Visibility.Collapsed;
            VistaBitacora.Visibility = AccionesBitacora.Visibility = actividad ? Visibility.Collapsed : Visibility.Visible;
            if (!actividad) CargarLog();
        }

        // ---- Actividad diaria ----

        private void CargarActividad()
        {
            var actividad = BrosSatDb.ObtenerActividadDescargas(_s.Conn, _s.Empresa.RFC, _mes, _mes.AddMonths(1));
            var porDia = actividad.ToDictionary(x => x.Fecha.Date, x => x);
            LblMes.Text = Es.TextInfo.ToTitleCase(_mes.ToString("MMMM yyyy", Es));
            LblMesCantidad.Text = actividad.Sum(x => x.Cantidad).ToString("N0");
            LblDiasActivos.Text = actividad.Count.ToString("N0");
            var ultima = actividad.OrderByDescending(x => x.Fecha).FirstOrDefault();
            LblUltima.Text = ultima == null ? "Sin registros" : ultima.Fecha.ToString("dd MMM", Es) + " · " + ultima.Cantidad.ToString("N0") + " XML";
            Calendario.Children.Clear();
            int desplazamiento = ((int)_mes.DayOfWeek + 6) % 7;
            for (int i = 0; i < desplazamiento; i++) Calendario.Children.Add(new Border { Margin = new Thickness(3) });
            for (int dia = 1; dia <= DateTime.DaysInMonth(_mes.Year, _mes.Month); dia++)
            {
                var fecha = new DateTime(_mes.Year, _mes.Month, dia);
                porDia.TryGetValue(fecha, out var dato);
                bool activo = dato != null;
                var contenido = new StackPanel { Margin = new Thickness(10, 8, 8, 8) };
                contenido.Children.Add(new TextBlock { Text = dia.ToString(), FontWeight = FontWeights.SemiBold, Foreground = P(activo ? "PincelTexto" : "PincelTexto2") });
                contenido.Children.Add(new TextBlock { Text = activo ? dato.Cantidad.ToString("N0") + " XML" : "—", FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), Foreground = P(activo ? "PincelOk" : "PincelTextoDeshabilitado") });
                Calendario.Children.Add(new Border
                {
                    MinHeight = 68, Margin = new Thickness(3), CornerRadius = new CornerRadius(9), Child = contenido,
                    Background = P(activo ? "PincelOkFondo" : "PincelSuperficie2"), BorderBrush = P(activo ? "PincelOkBorde" : "PincelBorde"), BorderThickness = new Thickness(1),
                    ToolTip = fecha.ToString("dd/MM/yyyy") + (activo ? ": " + dato.Cantidad.ToString("N0") + " XML descargados" : ": sin XML descargados")
                });
            }
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(-1); CargarActividad(); }
        private void BtnSiguiente_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(1); CargarActividad(); }

        // ---- Bitacora ----

        private void CargarMeses()
        {
            string carpeta = Bitacora.CarpetaLogs;
            string actual = DateTime.Now.ToString("yyyy-MM");
            var meses = Directory.Exists(carpeta)
                ? Directory.GetFiles(carpeta, "broslmv-*.log").Select(f => Path.GetFileNameWithoutExtension(f).Replace("broslmv-", "")).OrderByDescending(m => m).ToList()
                : new List<string>();
            if (!meses.Contains(actual)) meses.Insert(0, actual);
            CmbMes.ItemsSource = meses;
            CmbMes.SelectedItem = actual;
        }

        private void CmbMes_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && TabBitacora.IsChecked == true) CargarLog(); }
        private void BtnActualizarLog_Click(object sender, RoutedEventArgs e) => CargarLog();

        private void CargarLog()
        {
            if (CmbMes.SelectedItem == null) return;
            string archivo = Path.Combine(Bitacora.CarpetaLogs, "broslmv-" + CmbMes.SelectedItem + ".log");
            if (!File.Exists(archivo)) { TxtLog.Text = "(sin actividad registrada este mes todavía)"; return; }
            using (var stream = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var lector = new StreamReader(stream))
                TxtLog.Text = lector.ReadToEnd();
            TxtLog.CaretIndex = TxtLog.Text.Length;
            TxtLog.ScrollToEnd();
        }

        private void BtnAbrirCarpeta_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Bitacora.CarpetaLogs);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Bitacora.CarpetaLogs + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "No se pudo abrir la carpeta:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
