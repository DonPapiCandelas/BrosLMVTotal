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

// PaginaPanel.xaml.cs -- pantalla de inicio de la empresa activa: estado del servicio y de la e.firma, cifras del mes, lo que requiere
// atencion (salud + indicadores) y accesos rapidos. Todo sale del ResumenEmpresa que calcula la Sesion; aqui solo se pinta.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaPanel : UserControl
    {
        private readonly Sesion _s;
        private readonly Action<string> _ir;
        private static readonly CultureInfo Es = new CultureInfo("es-MX");

        public PaginaPanel(Sesion sesion, Action<string> navegar)
        {
            InitializeComponent();
            _s = sesion; _ir = navegar;
            Loaded += (s, e) => { _s.ResumenActualizado += Pintar; Pintar(); };
            Unloaded += (s, e) => _s.ResumenActualizado -= Pintar;
        }

        private Brush P(string clave) => (Brush)FindResource(clave);
        private Brush PincelNivel(NivelSalud n) => P(n == NivelSalud.Critico ? "PincelCritico" : n == NivelSalud.Aviso ? "PincelAviso" : "PincelOk");

        private void Pintar()
        {
            var emp = _s.Empresa; var r = _s.Resumen;
            if (emp == null) return;
            Encabezado.Titulo = emp.Nombre;
            if (r == null) { Encabezado.Subtitulo = emp.RFC + " · calculando el resumen…"; return; }
            Encabezado.Subtitulo = emp.RFC + " · datos al " + r.Calculado.ToString("dd/MM/yyyy HH:mm");

            PintarEstado(emp, r);
            PintarCifras(r);
            PintarAtencion(r);
            PintarSolicitudes(emp);
        }

        // ---- Estado general ----

        private static string AntesDeParentesis(string t) { int i = t.IndexOf(" ("); return (i > 0 ? t.Substring(0, i) : t).TrimEnd('.'); }

        private void PintarEstado(EmpresaRow emp, ResumenEmpresa r)
        {
            var servicio = r.Salud.FirstOrDefault(h => h.Tema == "Servicio");
            if (servicio == null) { PuntoServicio.Fill = P("PincelTexto3"); TxtServicio.Text = "Sin datos"; }
            else
            {
                PuntoServicio.Fill = PincelNivel(servicio.Nivel);
                TxtServicio.Text = servicio.Nivel == NivelSalud.Ok ? "Activo" : servicio.Nivel == NivelSalud.Critico ? "Detenido" : "Sin actividad reciente";
                TxtServicio.ToolTip = servicio.Detalle;
            }

            var firma = r.Salud.FirstOrDefault(h => h.Tema == "e.firma");
            if (firma == null) { PuntoFirma.Fill = P("PincelTexto3"); TxtFirma.Text = "Sin datos"; }
            else
            {
                PuntoFirma.Fill = PincelNivel(firma.Nivel);
                TxtFirma.Text = AntesDeParentesis(firma.Detalle); TxtFirma.ToolTip = firma.Detalle;
            }

            bool directa = !string.IsNullOrWhiteSpace(emp.ComercialConexionSql);
            bool carpetas = !string.IsNullOrWhiteSpace(emp.ComercialCarpetaXmlRecibidos) || !string.IsNullOrWhiteSpace(emp.ComercialCarpetaXmlEmitidos);
            var com = r.Salud.FirstOrDefault(h => h.Tema == "Comercial");
            if (!directa && !carpetas) { PuntoComercial.Fill = P("PincelTexto3"); TxtComercial.Text = "Sin configurar"; TxtComercial.ToolTip = "Configúrala en «Empresas y e.firma»."; }
            else if (com != null && com.Nivel != NivelSalud.Ok) { PuntoComercial.Fill = PincelNivel(com.Nivel); TxtComercial.Text = "Pendiente de pasar"; TxtComercial.ToolTip = com.Detalle; }
            else { PuntoComercial.Fill = P("PincelOk"); TxtComercial.Text = directa ? "Conectada" : "Carpetas al día"; TxtComercial.ToolTip = com?.Detalle; }

            if (!r.UltimoXml.HasValue) { PuntoUltimo.Fill = P("PincelTexto3"); TxtUltimo.Text = "Ninguno todavía"; }
            else
            {
                var t = r.UltimoXml.Value.ToLocalTime();
                var hace = DateTime.Now - t;
                PuntoUltimo.Fill = P(hace.TotalHours <= 48 ? "PincelOk" : "PincelAviso");
                TxtUltimo.Text = t.ToString("dd/MM HH:mm") + " · hace " + (hace.TotalHours < 1 ? (int)hace.TotalMinutes + " min" : hace.TotalDays < 2 ? (int)hace.TotalHours + " h" : (int)hace.TotalDays + " días");
            }
        }

        // ---- Cifras ----

        private void Delta(Border caja, TextBlock txt, int actual, int previo)
        {
            if (previo <= 0 && actual <= 0) { caja.Visibility = Visibility.Collapsed; return; }
            caja.Visibility = Visibility.Visible;
            if (previo <= 0) { txt.Text = "Mes anterior sin facturas"; caja.Style = (Style)FindResource("PastillaNeutra"); return; }
            double pct = (actual - previo) * 100.0 / previo;
            txt.Text = (pct >= 0 ? "▲ " : "▼ ") + Math.Abs(pct).ToString("N0", Es) + " % frente al mes anterior";
            caja.Style = (Style)FindResource("PastillaNeutra");
        }

        private void PintarCifras(ResumenEmpresa r)
        {
            string mes = Es.DateTimeFormat.GetMonthName(DateTime.Today.Month).ToUpperInvariant();
            K1Rotulo.Text = "FACTURAS RECIBIDAS · " + mes;
            K1Valor.Text = r.RecibidosMes.ToString("N0", Es);
            K1Detalle.Text = r.TotalRecibidosMes.ToString("C0", Es) + " en el mes";
            Delta(K1Delta, K1DeltaTxt, r.RecibidosMes, r.RecibidosMesAnterior);

            K2Rotulo.Text = "FACTURAS EMITIDAS · " + mes;
            K2Valor.Text = r.EmitidosMes.ToString("N0", Es);
            K2Detalle.Text = r.TotalEmitidosMes.ToString("C0", Es) + " en el mes";
            Delta(K2Delta, K2DeltaTxt, r.EmitidosMes, r.EmitidosMesAnterior);

            int revisar = r.CasosCriticos + r.CasosAviso;
            K3Valor.Text = revisar.ToString("N0", Es);
            K3Valor.Foreground = P(r.CasosCriticos > 0 ? "PincelCritico" : revisar > 0 ? "PincelAviso" : "PincelOk");
            K3Detalle.Text = revisar == 0 ? "Sin casos pendientes" : r.CasosCriticos + (r.CasosCriticos == 1 ? " crítico · " : " críticos · ") + r.CasosAviso + " por revisar";

            int pct = (int)Math.Floor(r.CoberturaAnio * 100 + 0.0001);
            K4Rotulo.Text = "COBERTURA " + DateTime.Today.Year;
            K4Valor.Text = pct + " %";
            K4Barra.Value = r.CoberturaAnio;
            K4Barra.Foreground = P(r.CoberturaAnio >= 0.98 ? "PincelOk" : r.CoberturaAnio >= 0.8 ? "PincelAviso" : "PincelCritico");
            K4Detalle.Text = r.DiasSinCubrir > 0 ? r.DiasSinCubrir + (r.DiasSinCubrir == 1 ? " día sin cubrir" : " días sin cubrir") : "Sin huecos en el año";
        }

        // ---- Requiere atención ----

        private sealed class Aviso { public int Peso; public NivelSalud Nivel; public string Titulo, Detalle, Destino; }

        private void PintarAtencion(ResumenEmpresa r)
        {
            var avisos = new List<Aviso>();
            foreach (var h in r.Salud.Where(h => h.Nivel != NivelSalud.Ok))
                avisos.Add(new Aviso { Peso = h.Nivel == NivelSalud.Critico ? 0 : 2, Nivel = h.Nivel, Titulo = h.Tema, Detalle = h.Detalle, Destino = "salud" });
            foreach (var (def, casos) in r.Indicadores.Where(i => i.Casos > 0 && i.Def.Nivel != NivelIndicador.Info))
                avisos.Add(new Aviso
                {
                    Peso = def.Nivel == NivelIndicador.Critico ? 1 : 3, Nivel = def.Nivel == NivelIndicador.Critico ? NivelSalud.Critico : NivelSalud.Aviso,
                    Titulo = def.Nombre, Detalle = casos.ToString("N0", Es) + (casos == 1 ? " comprobante en el año" : " comprobantes en el año"), Destino = "indicadores"
                });
            avisos = avisos.OrderBy(a => a.Peso).ToList();

            ListaAtencion.Children.Clear();
            TxtAtencionCuenta.Text = avisos.Count == 0 ? "" : avisos.Count + (avisos.Count == 1 ? " asunto" : " asuntos");
            if (avisos.Count == 0)
            {
                var vacio = new StackPanel { Margin = new Thickness(0, 34, 0, 34), HorizontalAlignment = HorizontalAlignment.Center };
                vacio.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("FuenteIconos"), FontSize = 30, Foreground = P("PincelOk"), HorizontalAlignment = HorizontalAlignment.Center });
                vacio.Children.Add(new TextBlock { Text = "Todo en orden", Style = (Style)FindResource("TextoSubtitulo"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
                vacio.Children.Add(new TextBlock { Text = "El servicio, la e.firma y los indicadores no muestran nada pendiente.", Style = (Style)FindResource("TextoSecundario"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                ListaAtencion.Children.Add(vacio);
                return;
            }
            foreach (var a in avisos.Take(7)) ListaAtencion.Children.Add(FilaAviso(a));
            if (avisos.Count > 7)
                ListaAtencion.Children.Add(new TextBlock { Text = "… y " + (avisos.Count - 7) + " más en Salud e Indicadores", Style = (Style)FindResource("TextoTenue"), Margin = new Thickness(22, 8, 0, 10) });
        }

        private UIElement FilaAviso(Aviso a)
        {
            var g = new Grid { Margin = new Thickness(22, 10, 14, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool crit = a.Nivel == NivelSalud.Critico;
            var icono = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top,
                Background = P(crit ? "PincelCriticoFondo" : "PincelAvisoFondo"),
                Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("FuenteIconos"), FontSize = 15, Foreground = P(crit ? "PincelCritico" : "PincelAviso"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var texto = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            texto.Children.Add(new TextBlock { Text = a.Titulo, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            texto.Children.Add(new TextBlock { Text = a.Detalle, Style = (Style)FindResource("TextoSecundario"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, ToolTip = a.Detalle, Margin = new Thickness(0, 1, 0, 0) });
            var boton = new Button { Style = (Style)FindResource("Subtle"), Content = "Ver", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 4, 12, 4), MinHeight = 30 };
            string destino = a.Destino; boton.Click += (s, e) => _ir(destino);
            Grid.SetColumn(texto, 1); Grid.SetColumn(boton, 2);
            g.Children.Add(icono); g.Children.Add(texto); g.Children.Add(boton);
            return g;
        }

        // ---- Ultimas solicitudes ----

        private void PintarSolicitudes(EmpresaRow emp)
        {
            ListaSolicitudes.Children.Clear();
            List<SolicitudRow> filas;
            try
            {
                filas = BrosSatDb.ObtenerSolicitudesRecientes(_s.Conn, emp.RFC, 5).Select(x => new SolicitudRow
                {
                    IdSolicitud = x.IdSolicitud, Tipo = x.Tipo, TipoSolicitud = x.TipoSolicitud, EstatusSat = x.Estatus, ErrorDescarga = x.UltimoErrorPaquete,
                    Rango = x.FechaInicial.ToString("dd/MM") + " – " + x.FechaFinal.ToString("dd/MM/yy"), Fecha = x.FechaSolicitud.ToLocalTime().ToString("dd/MM HH:mm"), FechaSolicitudUtc = x.FechaSolicitud
                }).ToList();
            }
            catch { filas = new List<SolicitudRow>(); }

            if (filas.Count == 0)
            {
                ListaSolicitudes.Children.Add(new TextBlock { Text = "Todavía no hay solicitudes.", Style = (Style)FindResource("TextoSecundario"), Margin = new Thickness(22, 14, 22, 14) });
                return;
            }
            foreach (var f in filas)
            {
                var g = new Grid { Margin = new Thickness(22, 9, 18, 9) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = f.Tipo + " · " + f.TipoSolicitud, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
                t.Children.Add(new TextBlock { Text = f.Rango + " · pedida " + f.Fecha, Style = (Style)FindResource("TextoTenue") });
                string estilo = f.EstatusCategoria == "Terminada" ? "PastillaOk" : f.EstatusCategoria == "Pendiente" ? "PastillaInfo" : "PastillaCritica";
                var pastilla = new Border { Style = (Style)FindResource(estilo), Child = new TextBlock { Text = f.EstatusCategoria == "Pendiente" ? "En espera" : f.EstatusCategoria == "Terminada" ? "Terminada" : "Con error" } };
                Grid.SetColumn(pastilla, 1);
                g.Children.Add(t); g.Children.Add(pastilla);
                ListaSolicitudes.Children.Add(g);
            }
        }

        // ---- Acciones ----

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            try { _s.CalcularResumen(); } finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }
        private void BtnNuevaDescarga_Click(object sender, RoutedEventArgs e) => _ir("descargas");
        private void BtnVerIndicadores_Click(object sender, RoutedEventArgs e) => _ir("indicadores");
        private void BtnVerCfdi_Click(object sender, RoutedEventArgs e) => _ir("cfdi");
        private void BtnCargar_Click(object sender, RoutedEventArgs e) => _ir("descargas");
        private void BtnReportes_Click(object sender, RoutedEventArgs e) => _ir("reportes");
        private void BtnCobertura_Click(object sender, RoutedEventArgs e) => _ir("cobertura");
    }
}
