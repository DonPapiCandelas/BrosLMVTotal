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

// MainWindow.xaml.cs -- ventana unica: menu lateral + pagina activa. La conexion a la base local, la lista de empresas y la empresa
// activa viven en Sesion y se comparten con todas las paginas. Cerrar la ventana la manda a la bandeja del sistema (el servicio de
// Windows sigue descargando por su cuenta). La contrasena de la FIEL de cada empresa se descifra (DPAPI) solo en memoria, al usarla.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using BrosLMV.DescargasUI.Controles;
using BrosLMV.DescargasUI.Paginas;
using Microsoft.Data.SqlClient;
using Forms = System.Windows.Forms;

namespace BrosLMV.DescargasUI
{
    internal static class VersionApp
    {
        // Misma version que el instalador (descargas-instalador\*.csproj); se cambia junto con ella.
        public const string Actual = "2.5.1";
    }

    public partial class MainWindow : Window
    {
        private Configuracion _config;
        private SqlConnection _conn;
        private Sesion _sesion;
        private readonly Dictionary<string, UserControl> _paginas = new Dictionary<string, UserControl>();
        private string _claveActual = "panel";
        private Forms.NotifyIcon _iconoBandeja;
        private bool _saliendoDeVerdad;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            ConfigurarBandeja();
            LblVersion.Text = "BrosLMV Descargas · v" + VersionApp.Actual;
        }

        // ---- Bandeja del sistema (pedido 2026-08-19: al cerrar, seguir en segundo plano) ----

        private void ConfigurarBandeja()
        {
            _iconoBandeja = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath),
                Text = "BrosLMV Descargas",
                Visible = false
            };
            _iconoBandeja.DoubleClick += (s, e) => RestaurarDesdeBandeja();

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir BrosLMV Descargas", null, (s, e) => RestaurarDesdeBandeja());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => { _saliendoDeVerdad = true; Close(); });
            _iconoBandeja.ContextMenuStrip = menu;
        }

        // Llamado por la segunda instancia (boton de Comercial): trae la ventana al frente y, si viene un RFC dado de alta, abre esa empresa.
        public void MostrarYAbrir(string rfc)
        {
            RestaurarDesdeBandeja();
            Topmost = true; Topmost = false;
            if (string.IsNullOrWhiteSpace(rfc) || _sesion == null) return;
            if (_sesion.Seleccionar(rfc)) Navegar("panel");
        }

        private void RestaurarDesdeBandeja()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _iconoBandeja.Visible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_saliendoDeVerdad)
            {
                e.Cancel = true;
                Hide();
                _iconoBandeja.Visible = true;
                _iconoBandeja.ShowBalloonTip(3000, "BrosLMV Descargas",
                    "Sigue corriendo en segundo plano. Doble clic aquí para volver a abrirla.", Forms.ToolTipIcon.Info);
                return;
            }
            _iconoBandeja.Visible = false;
            _iconoBandeja.Dispose();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _conn?.Dispose();
            base.OnClosed(e);
        }

        // ---- Arranque ----

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _config = Configuracion.Cargar();
            if (_config == null)
            {
                var cw = new ConexionWindow { Owner = this };
                if (cw.ShowDialog() != true) { Close(); return; }
                _config = cw.Resultado;
            }

            try
            {
                EsquemaSql.AsegurarBaseDeDatos(_config.CadenaConexion);
                _conn = new SqlConnection(_config.CadenaConexion);
                _conn.Open();
                EsquemaSql.Asegurar(_conn);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo conectar a la base de datos:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            LblConexion.Text = _conn.DataSource + " · " + _conn.Database;
            PuntoConexion.Fill = (Brush)FindResource("PincelOk");

            _sesion = new Sesion(_conn, this);
            _sesion.EmpresasRecargadas += PintarSelectorEmpresa;
            _sesion.EmpresaCambiada += () => { _paginas.Clear(); PintarSelectorEmpresa(); ActualizarInsignias(); Navegar(_claveActual); ProgramarResumen(); };
            _sesion.ResumenActualizado += ActualizarInsignias;
            _sesion.RecargarEmpresas(string.IsNullOrEmpty(App.RfcInicial) ? null : App.RfcInicial);
            PintarSelectorEmpresa();
            Navegar(_sesion.Empresas.Count == 0 ? "empresas" : "panel");
            ProgramarResumen();
        }

        // El resumen (salud, indicadores, cobertura) se calcula despues de pintar la ventana para que abra de inmediato.
        private void ProgramarResumen()
        {
            if (_sesion?.Empresa == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Cursor = System.Windows.Input.Cursors.AppStarting;
                try { _sesion.CalcularResumen(); } catch { } finally { Cursor = System.Windows.Input.Cursors.Arrow; }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Insignias del menu: casos por revisar en Indicadores y alerta en Salud.
        private void ActualizarInsignias()
        {
            var r = _sesion?.Resumen;
            int revisar = r == null ? 0 : r.CasosCriticos + r.CasosAviso;
            Ui.SetInsignia(NavIndicadores, revisar > 0 ? (revisar > 999 ? "999+" : revisar.ToString()) : null);
            Ui.SetNivelInsignia(NavIndicadores, r != null && r.CasosCriticos > 0 ? "critico" : "aviso");
            var peor = r == null ? NivelSalud.Ok : r.PeorSalud;
            Ui.SetInsignia(NavSalud, peor == NivelSalud.Ok ? null : "!");
            Ui.SetNivelInsignia(NavSalud, peor == NivelSalud.Critico ? "critico" : "aviso");
        }

        // ---- Selector de empresa ----

        private T Parte<T>(string nombre) where T : class
        {
            BtnEmpresa.ApplyTemplate();
            return BtnEmpresa.Template.FindName(nombre, BtnEmpresa) as T;
        }

        private static string Iniciales(string nombre)
        {
            var partes = (nombre ?? "").Split(new[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 2 || p.All(char.IsLetter)).Take(2).ToList();
            if (partes.Count == 0) return "—";
            return string.Concat(partes.Select(p => char.ToUpperInvariant(p[0]))) ;
        }

        private void PintarSelectorEmpresa()
        {
            var emp = _sesion?.Empresa;
            var txtIni = Parte<TextBlock>("TxtIniciales"); var txtNom = Parte<TextBlock>("TxtEmpresaNombre"); var txtRfc = Parte<TextBlock>("TxtEmpresaRfc");
            if (txtIni != null) txtIni.Text = emp == null ? "—" : Iniciales(emp.Nombre);
            if (txtNom != null) txtNom.Text = emp == null ? "Sin empresas" : emp.Nombre;
            if (txtRfc != null) txtRfc.Text = emp == null ? "Agrega una en «Empresas»" : emp.RFC;

            ListaEmpresas.Children.Clear();
            foreach (var x in _sesion?.Empresas ?? new List<EmpresaRow>())
            {
                var activa = emp != null && x.EmpresaID == emp.EmpresaID;
                var fila = new Button
                {
                    Style = (Style)FindResource("Subtle"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 7, 10, 7), Tag = x,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = x.Nombre, FontWeight = activa ? FontWeights.SemiBold : FontWeights.Normal, Foreground = (Brush)FindResource("PincelTexto"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 },
                            new TextBlock { Text = x.RFC, FontSize = 11.5, FontFamily = (FontFamily)FindResource("FuenteMono"), Foreground = (Brush)FindResource("PincelTexto3") }
                        }
                    }
                };
                if (activa) fila.Background = (Brush)FindResource("PincelAcentoSuave");
                fila.Click += (s, e) => { BtnEmpresa.IsChecked = false; _sesion.Seleccionar((EmpresaRow)((Button)s).Tag); };
                ListaEmpresas.Children.Add(fila);
            }
        }

        private void BtnAdministrarEmpresas_Click(object sender, RoutedEventArgs e)
        {
            BtnEmpresa.IsChecked = false;
            NavEmpresas.IsChecked = true;
        }

        // ---- Navegación ----

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_sesion == null) return;           // el RadioButton inicial se marca antes de conectar
            Navegar((string)((RadioButton)sender).Tag);
        }

        private RadioButton ItemNav(string clave) => LogicalTreeHelperFind<RadioButton>(this).FirstOrDefault(r => (r.Tag as string) == clave);

        private static IEnumerable<T> LogicalTreeHelperFind<T>(DependencyObject raiz) where T : DependencyObject
        {
            foreach (var hijo in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
            {
                if (hijo is T t) yield return t;
                foreach (var nieto in LogicalTreeHelperFind<T>(hijo)) yield return nieto;
            }
        }

        public void Navegar(string clave)
        {
            if (_sesion == null) return;
            _claveActual = clave;
            var item = ItemNav(clave);
            if (item != null && item.IsChecked != true) { item.IsChecked = true; return; }   // dispara Nav_Checked -> vuelve a entrar aqui ya marcado

            bool requiereEmpresa = clave != "empresas" && clave != "automatizacion" && clave != "salud";
            if (requiereEmpresa && _sesion.Empresa == null) { Contenido.Content = PaginaSinEmpresa(); return; }

            if (!_paginas.TryGetValue(clave, out var pagina))
            {
                pagina = CrearPagina(clave);
                if (pagina == null) return;
                _paginas[clave] = pagina;
            }
            Contenido.Content = pagina;
        }

        private UserControl CrearPagina(string clave)
        {
            switch (clave)
            {
                case "panel": return new PaginaPanel(_sesion, Navegar);
                case "cfdi": return new PaginaCfdi(_sesion);
                case "descargas": return new PaginaDescargas(_sesion);
                case "indicadores": return new PaginaIndicadores(_sesion);
                case "cobertura": return new PaginaCobertura(_sesion);
                case "conciliacion": return new PaginaConciliacion(_sesion);
                case "cancelaciones": return new PaginaCancelaciones(_sesion);
                case "vinculos": return new PaginaVinculos(_sesion);
                case "reportes": return new PaginaReportes(_sesion);
                case "salud": return new PaginaSalud(_sesion);
                case "automatizacion": return new PaginaAutomatizacion();
                case "registro": return new PaginaRegistro(_sesion);
                case "empresas": return new PaginaEmpresas(_sesion, Navegar);
                default: return null;
            }
        }

        private UserControl PaginaSinEmpresa()
        {
            var boton = new Button { Style = (Style)FindResource("Primary"), Content = "Agregar una empresa", Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            ui_Glifo(boton, G.Agregar);
            boton.Click += (s, e) => NavEmpresas.IsChecked = true;
            var contenido = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420 };
            contenido.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("FuenteIconos"), FontSize = 40, Foreground = (Brush)FindResource("PincelBordeFuerte"), HorizontalAlignment = HorizontalAlignment.Center });
            contenido.Children.Add(new TextBlock { Text = "Aún no hay una empresa", Style = (Style)FindResource("TextoTitulo"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            contenido.Children.Add(new TextBlock { Text = "Agrega tu e.firma para descargar los CFDI de una empresa y empezar a trabajar con ellos.", Style = (Style)FindResource("TextoSecundario"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            contenido.Children.Add(boton);
            return new UserControl { Content = contenido };
        }

        private static void ui_Glifo(DependencyObject d, string glifo) => Ui.SetGlifo(d, glifo);
    }
}
