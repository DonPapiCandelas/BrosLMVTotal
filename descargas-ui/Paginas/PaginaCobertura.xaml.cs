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

// PaginaCobertura.xaml.cs -- una fila por ano y tipo (Recibidos / Emitidos), una celda por mes. El color sale de los dias que ninguna
// solicitud buena cubre (los mismos rangos y huecos que usa Salud) y el numero es la cantidad de CFDI de ese mes en este equipo.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaCobertura : UserControl
    {
        private readonly Sesion _s;
        private static readonly CultureInfo Es = new CultureInfo("es-MX");

        public PaginaCobertura(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Cargar();
        private Brush P(string clave) => (Brush)FindResource(clave);
        private static Brush Gris(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        private void Cargar()
        {
            var empresa = _s.DatosEmpresa();
            if (empresa == null) return;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                Contenido.Children.Clear();
                var hoy = DateTime.Today;
                int anioIni = Math.Min(empresa.AnioInicioDescargas ?? hoy.Year, hoy.Year);
                using (var cmd = new SqlCommand("SELECT MIN(YEAR(FechaInicial)) FROM SolicitudDescarga WHERE RfcSolicitante=@r", _s.Conn))
                {
                    cmd.Parameters.AddWithValue("@r", empresa.RFC);
                    var v = cmd.ExecuteScalar();
                    if (v != null && v != DBNull.Value) anioIni = Math.Min(anioIni, Convert.ToInt32(v));
                }
                // Como en Salud: hasta las 15:00 el dia en curso no se exige.
                var hasta = DateTime.Now.Hour >= 15 ? hoy : hoy.AddDays(-1);

                foreach (var tipo in new[] { "Recibidos", "Emitidos" })
                {
                    var huecos = Huecos.Todos(BrosSatDb.ObtenerRangosCubiertos(_s.Conn, empresa.RFC, "CFDI", tipo), new DateTime(anioIni, 1, 1), hasta);
                    var conteo = ContarPorMes(empresa.RFC, tipo == "Recibidos", anioIni);

                    var tarjeta = new StackPanel();
                    tarjeta.Children.Add(new TextBlock { Text = tipo, Style = (Style)FindResource("TextoSubtitulo"), Margin = new Thickness(0, 0, 0, 12) });
                    var rejilla = new Grid();
                    rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                    for (int m = 0; m < 12; m++) rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
                    rejilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int m = 1; m <= 12; m++)
                    {
                        var t = new TextBlock { Text = Es.DateTimeFormat.GetAbbreviatedMonthName(m).Replace(".", "").ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)FindResource("TextoRotulo"), Margin = new Thickness(0, 0, 0, 6) };
                        Grid.SetColumn(t, m); rejilla.Children.Add(t);
                    }
                    int fila = 1;
                    for (int anio = hoy.Year; anio >= anioIni; anio--, fila++)
                    {
                        rejilla.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
                        var ta = new TextBlock { Text = anio.ToString(), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                        Grid.SetRow(ta, fila); rejilla.Children.Add(ta);
                        for (int m = 1; m <= 12; m++)
                        {
                            var ini = new DateTime(anio, m, 1);
                            var fin = ini.AddMonths(1).AddDays(-1);
                            conteo.TryGetValue(anio * 100 + m, out int n);
                            Brush color; string estado; int totalDias = 0, sinCubrir = 0;
                            if (ini > hasta) { color = Gris(203, 213, 225); estado = "Aún no transcurre"; }
                            else
                            {
                                var topeMes = fin < hasta ? fin : hasta;
                                totalDias = (int)(topeMes - ini).TotalDays + 1;
                                foreach (var h in huecos)
                                {
                                    var a = h.Desde.Date > ini ? h.Desde.Date : ini;
                                    var b = h.Hasta.Date < topeMes ? h.Hasta.Date : topeMes;
                                    if (a <= b) sinCubrir += (int)(b - a).TotalDays + 1;
                                }
                                if (sinCubrir == 0) { color = P("PincelOk"); estado = "Completo"; }
                                else if (sinCubrir >= totalDias && anio < (empresa.AnioInicioDescargas ?? 0)) { color = Gris(226, 232, 240); estado = "Fuera del periodo configurado (se descarga desde " + empresa.AnioInicioDescargas + ")"; }
                                else if (sinCubrir >= totalDias) { color = P("PincelCritico"); estado = "Sin cubrir"; }
                                else { color = P("PincelAviso"); estado = "Parcial"; }
                            }
                            var celda = new Border
                            {
                                Background = color, CornerRadius = new CornerRadius(7), Margin = new Thickness(2),
                                ToolTip = ini.ToString("MMMM yyyy", Es) + " — " + estado + (totalDias > 0 ? "\n" + (totalDias - sinCubrir) + " de " + totalDias + " días cubiertos" : "") + "\n" + n.ToString("N0") + " CFDI en este equipo",
                                Child = new TextBlock { Text = n > 0 ? n.ToString("N0") : "", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }
                            };
                            Grid.SetRow(celda, fila); Grid.SetColumn(celda, m); rejilla.Children.Add(celda);
                        }
                    }
                    tarjeta.Children.Add(rejilla);
                    Contenido.Children.Add(new Border { Style = (Style)FindResource("Tarjeta"), Margin = new Thickness(0, 0, 0, 16), Child = tarjeta });
                }
            }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        // anio*100+mes -> cantidad de CFDI. Recibidos = los que la empresa recibio; Emitidos = los que emitio.
        private Dictionary<int, int> ContarPorMes(string rfc, bool recibidos, int desdeAnio)
        {
            var r = new Dictionary<int, int>();
            string sql = "SELECT YEAR(FechaEmision), MONTH(FechaEmision), COUNT(*) FROM CfdiRecibido WHERE " +
                         (recibidos ? "RFCReceptor=@r AND RFCEmisor<>@r" : "RFCEmisor=@r") + " AND FechaEmision >= @d GROUP BY YEAR(FechaEmision), MONTH(FechaEmision)";
            using (var cmd = new SqlCommand(sql, _s.Conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                cmd.Parameters.AddWithValue("@d", new DateTime(desdeAnio, 1, 1));
                using (var l = cmd.ExecuteReader())
                    while (l.Read()) r[l.GetInt32(0) * 100 + l.GetInt32(1)] = l.GetInt32(2);
            }
            return r;
        }
    }
}
