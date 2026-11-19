// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Bancos.Carga;

namespace BrosLMV.BancosUI
{
    // Revisa el estado de cuenta de Comercial buscando el MISMO movimiento cargado varias veces (por otro cargador, una carga manual repetida...).
    // Solo muestra y, si el usuario lo confirma, da de baja las copias SIN movimiento ligado (como lo hace Comercial, con respaldo y restauracion).
    public class VentanaDuplicados : Window
    {
        private class Fila : INotifyPropertyChanged
        {
            public GrupoDuplicado Grupo;
            private bool _marcar;
            public bool Marcar { get { return _marcar; } set { _marcar = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Marcar")); } }
            public string Cuenta { get; set; }
            public string Fecha { get; set; }
            public string Descripcion { get; set; }
            public string Importe { get; set; }
            public int Copias { get; set; }
            public int Sobran { get; set; }
            public string Nota { get; set; }
            public bool Editable { get; set; }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly Contexto _ctx;
        private readonly DataGrid _grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Single };
        private readonly TextBlock _estado = Tema.Txt("", 12, false, Tema.Texto2, TextWrapping.Wrap);
        private readonly ComboBox _dias = new ComboBox { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
        private Button _btnBaja, _btnBuscar, _btnRestaurar;
        private List<Fila> _filas = new List<Fila>();
        public bool Cambio;       // hubo bajas o restauraciones: quien abrio la ventana debe releer lo ya cargado

        public VentanaDuplicados(Contexto ctx, Window dueno)
        {
            _ctx = ctx; Owner = dueno;
            Title = "Bancos — Renglones repetidos"; Width = 1040; Height = 600; MinWidth = 820; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Tema.Pagina;
            Construir();
            Loaded += async (s, e) => await Buscar();
        }

        private void Construir()
        {
            var raiz = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cab = new StackPanel();
            cab.Children.Add(Tema.Txt("Renglones repetidos en el estado de cuenta", 16, true));
            cab.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Tema.Texto2,
                Text = "Es el mismo movimiento cuando coinciden cuenta, fecha, cargo, abono, saldo y descripción: el saldo distingue dos movimientos iguales del mismo día, así que si el saldo es el mismo, el renglón se cargó dos veces. " +
                       "Se conserva una copia de cada movimiento (la que tiene un movimiento bancario ligado y, si no hay, la más antigua). Los renglones ligados nunca se tocan. Dar de baja es como lo hace Comercial y se puede restaurar."
            });
            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            fila.Children.Add(Tema.Txt("Revisar los últimos", 13));
            foreach (var d in new[] { 30, 60, 90, 180, 365 }) _dias.Items.Add(d + " días");
            _dias.SelectedIndex = 1;
            fila.Children.Add(_dias);
            _btnBuscar = Tema.Boton("Buscar", async (s, e) => await Buscar()); _btnBuscar.Margin = new Thickness(10, 0, 0, 0); fila.Children.Add(_btnBuscar);
            cab.Children.Add(fila);
            Grid.SetRow(cab, 0); raiz.Children.Add(cab);

            Func<string, string, double, DataGridTextColumn> col = (enc, prop, ancho) =>
                new DataGridTextColumn { Header = enc, Binding = new System.Windows.Data.Binding(prop), IsReadOnly = true, Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star) };
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Dar de baja", Binding = new System.Windows.Data.Binding("Marcar") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = 84 });
            _grid.Columns.Add(col("Cuenta", "Cuenta", 120)); _grid.Columns.Add(col("Fecha", "Fecha", 84)); _grid.Columns.Add(col("Descripción", "Descripcion", 0));
            _grid.Columns.Add(col("Importe", "Importe", 100)); _grid.Columns.Add(col("Copias", "Copias", 60)); _grid.Columns.Add(col("Sobran", "Sobran", 60)); _grid.Columns.Add(col("Nota", "Nota", 190));
            var caja = new Border { Margin = new Thickness(0, 12, 0, 12), Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = _grid, Padding = new Thickness(0) };
            Grid.SetRow(caja, 1); raiz.Children.Add(caja);

            var pie = new DockPanel { LastChildFill = true };
            var cerrar = Tema.Boton("Cerrar", (s, e) => Close()); DockPanel.SetDock(cerrar, Dock.Right); pie.Children.Add(cerrar);
            _btnBaja = Tema.Boton("Dar de baja los marcados", async (s, e) => await DarDeBaja(), true); _btnBaja.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(_btnBaja, Dock.Right); pie.Children.Add(_btnBaja);
            _btnRestaurar = Tema.Boton("Restaurar última baja", async (s, e) => await Restaurar()); _btnRestaurar.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(_btnRestaurar, Dock.Right); pie.Children.Add(_btnRestaurar);
            pie.Children.Add(_estado);
            Grid.SetRow(pie, 2); raiz.Children.Add(pie);
            Content = raiz;
        }

        private int Dias() { int d; return int.TryParse((_dias.SelectedItem as string ?? "60").Split(' ')[0], out d) ? d : 60; }

        private async Task Buscar()
        {
            _estado.Text = "Buscando…"; _btnBaja.IsEnabled = false; _btnBuscar.IsEnabled = false;
            try
            {
                var desde = DateTime.Today.AddDays(-Dias());
                List<string> omitidas = new List<string>();
                var grupos = await Task.Run(() => { List<string> om; var g = _ctx.Banco.DuplicadosExistentes(desde, null, out om); omitidas = om; return g; });
                _filas = grupos.Select(g => new Fila
                {
                    Grupo = g, Cuenta = g.Conservar.Cuenta, Fecha = g.Conservar.Fecha.ToString("dd/MM/yyyy"), Descripcion = g.Conservar.Descripcion,
                    Importe = (g.Conservar.Abono != 0 ? "+" + Tema.Dinero(g.Conservar.Abono) : "-" + Tema.Dinero(g.Conservar.Cargo)), Copias = g.Copias, Sobran = g.Sobrantes.Count,
                    Editable = g.Sobrantes.Count > 0, Marcar = g.Sobrantes.Count > 0,
                    Nota = g.Sobrantes.Count == 0 ? "Todas ligadas: revisar a mano" : g.LigadosExtra.Count > 0 ? g.LigadosExtra.Count + " ligada(s) más: no se tocan" : g.Conservar.Ligado ? "Se conserva la ligada" : "Se conserva la más antigua"
                }).ToList();
                _grid.ItemsSource = _filas;
                int sob = _filas.Sum(f => f.Sobran);
                _estado.Text = _filas.Count == 0 ? "No hay renglones repetidos en los últimos " + Dias() + " días." : _filas.Count + " movimientos repetidos · " + sob + " renglones sobrantes.";
                if (omitidas.Count > 0) _estado.Text += " No se revisó " + string.Join(", ", omitidas) + ": su saldo es el del día, no por movimiento, y no distingue un repetido de dos iguales.";
                _btnBaja.IsEnabled = sob > 0;
            }
            catch (Exception ex) { _estado.Text = "No se pudo revisar: " + ex.Message; }
            _btnBuscar.IsEnabled = true;
        }

        private async Task DarDeBaja()
        {
            var ids = _filas.Where(f => f.Marcar && f.Editable).SelectMany(f => f.Grupo.Sobrantes.Select(x => x.EdoCtaId)).ToList();
            if (ids.Count == 0) { _estado.Text = "No hay nada marcado."; return; }
            if (MessageBox.Show(this, "Se darán de baja " + ids.Count + " renglones repetidos (se conserva una copia de cada movimiento y los ligados no se tocan).\n\nSe puede restaurar con «Restaurar última baja». ¿Continuar?",
                    "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _btnBaja.IsEnabled = false;
            try { _estado.Text = await Task.Run(() => _ctx.Banco.DarDeBajaDuplicados(ids)); Cambio = true; }
            catch (Exception ex) { _estado.Text = "No se pudo dar de baja: " + ex.Message; }
            await Buscar();
        }

        private async Task Restaurar()
        {
            if (MessageBox.Show(this, "¿Restaurar los renglones de tu última baja?", "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { _estado.Text = await Task.Run(() => _ctx.Banco.RestaurarUltimaBaja()); Cambio = true; }
            catch (Exception ex) { _estado.Text = "No se pudo restaurar: " + ex.Message; }
            await Buscar();
        }
    }
}
