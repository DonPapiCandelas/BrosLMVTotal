// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.BancosUI
{
    // Los layouts agrupados por banco. Si hay un archivo seleccionado en la bandeja, cada layout muestra cuanto coincide con el.
    public class PaginaLayouts : UserControl
    {
        private readonly Contexto _ctx;
        private readonly VentanaPrincipal _dueno;
        private readonly StackPanel _lista = new StackPanel();
        private readonly StackPanel _permisos = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly TextBlock _estado = Tema.Txt("", 12, false, Tema.Texto2);
        private Layout _sel;
        private bool _puedeEditar = true, _esAdmin;
        private List<ComercialBanco.Usuario> _usuarios = new List<ComercialBanco.Usuario>();
        private string _modo = "todos", _elegidos = "";
        private Button _btnEditar, _btnDuplicar, _btnExportar, _btnQuitar, _btnNuevo, _btnImportar;

        public PaginaLayouts(Contexto ctx, VentanaPrincipal dueno)
        {
            _ctx = ctx; _dueno = dueno;
            var raiz = new Grid();
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caja = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(20, 16, 20, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _lista } };
            Grid.SetRow(caja, 0); raiz.Children.Add(caja);

            var perm = new DockPanel { Margin = new Thickness(20, 12, 20, 0) };
            perm.Children.Add(Tema.Txt("Quién puede editar layouts", 12, false, Tema.Texto2));
            _permisos.Margin = new Thickness(12, 0, 0, 0); perm.Children.Add(_permisos);
            Grid.SetRow(perm, 1); raiz.Children.Add(perm);

            var pie = new DockPanel { Margin = new Thickness(20, 12, 20, 14), LastChildFill = true };
            _btnNuevo = Tema.Boton("Nuevo layout", (s, e) => Editar(null, null), true);
            DockPanel.SetDock(_btnNuevo, Dock.Right); pie.Children.Add(_btnNuevo);
            _btnImportar = Tema.Boton("Importar", (s, e) => Importar()); _btnImportar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_btnImportar, Dock.Right); pie.Children.Add(_btnImportar);
            _btnExportar = Tema.Boton("Exportar", (s, e) => Exportar()); _btnExportar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_btnExportar, Dock.Right); pie.Children.Add(_btnExportar);
            _btnDuplicar = Tema.Boton("Duplicar", (s, e) => Duplicar()); _btnDuplicar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_btnDuplicar, Dock.Right); pie.Children.Add(_btnDuplicar);
            _btnQuitar = Tema.Boton("Volver al de fábrica", (s, e) => Quitar()); _btnQuitar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_btnQuitar, Dock.Right); pie.Children.Add(_btnQuitar);
            _btnEditar = Tema.Boton("Editar", (s, e) => Editar(_sel, null)); _btnEditar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(_btnEditar, Dock.Right); pie.Children.Add(_btnEditar);
            pie.Children.Add(_estado);
            Grid.SetRow(pie, 2); raiz.Children.Add(pie);
            Content = raiz;
        }

        public async void Refrescar()
        {
            try
            {
                await Task.Run(() =>
                {
                    _puedeEditar = _ctx.Banco.PuedeEditarLayouts(_ctx.Usuario);
                    _esAdmin = _ctx.Banco.EsAdministrador(_ctx.Usuario);
                    _modo = _ctx.Banco.Config("layouts.permiso", "todos");
                    _elegidos = _ctx.Banco.Config("layouts.usuarios", "");
                    _usuarios = _ctx.Banco.Usuarios();
                });
            }
            catch { }
            Pintar();
        }

        // ---------- pintar ----------
        private void Pintar()
        {
            _lista.Children.Clear();
            var muestra = _dueno.Bandeja.Seleccionado;
            Huella huella = null; Dictionary<string, Coincidencia> coincide = null; Coincidencia elegido = null;
            if (muestra != null && muestra.Hoja != null)
            {
                bool pdf = muestra.Hoja.LayoutPdf != null || muestra.Ruta.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                try
                {
                    string textoPdf = pdf ? TextoPdf(muestra) : null;
                    int fila = muestra.Deteccion != null ? muestra.Deteccion.Mapa.FilaEncabezado : -1;
                    huella = Catalogo.HuellaDe(muestra.Hoja.Filas, fila, muestra.Nombre, textoPdf);
                    var pruebas = Catalogo.Probar(_dueno.Bandeja.Layouts, huella, pdf ? "pdf" : "tabla");
                    coincide = pruebas.ToDictionary(c => c.Layout.Id, c => c, StringComparer.OrdinalIgnoreCase);
                    elegido = Catalogo.Elegir(pruebas);
                }
                catch { }
                var banda = new Border { Background = Tema.AcentoFondo, Padding = new Thickness(18, 10, 18, 10), CornerRadius = new CornerRadius(12, 12, 0, 0) };
                banda.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tema.Txt("Probado con", 12, false, Tema.Texto2), Tema.Txt("  " + muestra.Nombre, 13, true) } };
                _lista.Children.Add(banda);
            }

            foreach (var grupo in _dueno.Bandeja.Layouts.GroupBy(l => l.Banco.Length == 0 ? "Varios bancos" : l.Banco).OrderBy(g => g.Key == "Varios bancos" ? "zzz" : g.Key))
            {
                _lista.Children.Add(new Border { Padding = new Thickness(18, 12, 18, 4), Child = Tema.Txt(grupo.Key.ToUpperInvariant(), 11, true, Tema.Texto2) });
                foreach (var l in grupo)
                {
                    var layout = l;
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var nombre = new StackPanel { Orientation = Orientation.Horizontal };
                    nombre.Children.Add(Tema.Txt(l.Nombre, 13, false));
                    var origen = Tema.Chip(l.Origen == "empresa" ? "v" + l.Version + " · empresa" : "de fábrica", l.Origen == "empresa" ? Tono.Acento : Tono.Neutro, 11); origen.Margin = new Thickness(10, 0, 0, 0); nombre.Children.Add(origen);
                    var fmt = Tema.Chip(l.Formato == "pdf" ? "PDF" : "Tabla", Tono.Neutro, 11); fmt.Margin = new Thickness(6, 0, 0, 0); nombre.Children.Add(fmt);
                    g.Children.Add(nombre);
                    Coincidencia c;
                    if (coincide != null && coincide.TryGetValue(l.Id, out c))
                    {
                        var barra = new Grid { Width = 90, Height = 5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                        barra.Children.Add(new Border { Background = Tema.Pincel("#E8EAEE"), CornerRadius = new CornerRadius(3) });
                        barra.Children.Add(new Border { Background = Tema.Acento, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 90.0 * c.Porcentaje / 100 });
                        Grid.SetColumn(barra, 1); g.Children.Add(barra);
                        var pct = Tema.Txt(c.Porcentaje + "%", 12, false, Tema.Texto2); pct.Width = 36; pct.TextAlignment = TextAlignment.Right; Grid.SetColumn(pct, 2); g.Children.Add(pct);
                        if (elegido != null && elegido.Layout == l) { var se = Tema.Chip("Se usa", Tono.Bueno); se.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(se, 3); g.Children.Add(se); }
                    }
                    var fila = new Border { Padding = new Thickness(18, 9, 18, 9), BorderBrush = Tema.Linea, BorderThickness = new Thickness(0, 0, 0, 1), Background = l == _sel ? Tema.AcentoFondo : Brushes.Transparent, Child = g, Cursor = Cursors.Hand };
                    fila.MouseLeftButtonUp += (s, e) => { _sel = layout; Pintar(); if (e.ClickCount > 1) Editar(layout, null); };
                    fila.MouseLeftButtonDown += (s, e) => { if (e.ClickCount > 1) Editar(layout, null); };
                    _lista.Children.Add(fila);
                }
            }

            _btnEditar.IsEnabled = _sel != null; _btnDuplicar.IsEnabled = _sel != null && _puedeEditar; _btnExportar.IsEnabled = _sel != null;
            _btnQuitar.IsEnabled = _sel != null && _sel.Origen == "empresa" && _puedeEditar && Catalogo.DeFabrica().Any(f => f.Id == _sel.Id);
            _btnNuevo.IsEnabled = _puedeEditar; _btnImportar.IsEnabled = _puedeEditar;
            PintarPermisos();
        }

        private static string TextoPdf(AnalisisArchivo a)
        {
            var notas = new List<string>();
            try { return BancoPdf.TextoInicial(a.Ruta); } catch { return ""; }
        }

        private void PintarPermisos()
        {
            _permisos.Children.Clear();
            var modo = new ComboBox { Width = 170, IsEnabled = _esAdmin };
            modo.Items.Add("Todos"); modo.Items.Add("Solo administradores"); modo.Items.Add("Usuarios elegidos");
            modo.SelectedIndex = _modo == "admin" ? 1 : _modo == "usuarios" ? 2 : 0;
            modo.SelectionChanged += (s, e) =>
            {
                string nuevo = modo.SelectedIndex == 1 ? "admin" : modo.SelectedIndex == 2 ? "usuarios" : "todos";
                if (nuevo == _modo) return;
                _modo = nuevo; Task.Run(() => _ctx.Banco.PonerConfig("layouts.permiso", nuevo)); PintarPermisos();
            };
            _permisos.Children.Add(modo);
            if (_modo == "usuarios")
            {
                var ids = _elegidos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                foreach (var u in _usuarios.Where(u => ids.Contains(u.Id.ToString())))
                {
                    var usuario = u;
                    var chip = Tema.Chip(u.Nombre + (_esAdmin ? "  ×" : ""), Tono.Neutro); chip.Margin = new Thickness(8, 0, 0, 0); chip.Cursor = _esAdmin ? Cursors.Hand : Cursors.Arrow;
                    if (_esAdmin) chip.MouseLeftButtonUp += (s, e) => CambiarElegidos(ids.Where(x => x != usuario.Id.ToString()));
                    _permisos.Children.Add(chip);
                }
                if (_esAdmin)
                {
                    var disp = _usuarios.Where(u => !u.Administrador && !ids.Contains(u.Id.ToString())).ToList();
                    if (disp.Count > 0)
                    {
                        var mas = new ComboBox { Width = 160, Margin = new Thickness(8, 0, 0, 0), ItemsSource = disp, Text = "Agregar usuario" };
                        mas.SelectionChanged += (s, e) => { var u = mas.SelectedItem as ComercialBanco.Usuario; if (u != null) CambiarElegidos(ids.Concat(new[] { u.Id.ToString() })); };
                        _permisos.Children.Add(mas);
                    }
                }
            }
            if (!_esAdmin) { var n = Tema.Txt("Solo un administrador lo cambia", 12, false, Tema.Texto2); n.Margin = new Thickness(10, 0, 0, 0); _permisos.Children.Add(n); }
        }

        private void CambiarElegidos(IEnumerable<string> ids)
        {
            _elegidos = string.Join(",", ids.Distinct()); string v = _elegidos;
            Task.Run(() => _ctx.Banco.PonerConfig("layouts.usuarios", v)); PintarPermisos();
        }

        // ---------- acciones ----------
        public void NuevoDesdeArchivo(AnalisisArchivo muestra) { Editar(null, muestra); }

        private void Editar(Layout l, AnalisisArchivo muestra)
        {
            if (!_puedeEditar && l == null) { MessageBox.Show(_dueno, "No tienes permiso para crear layouts. Pide a un administrador que te lo dé.", "Bancos", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var ventana = new EditorLayoutVentana(_ctx, l, muestra ?? _dueno.Bandeja.Seleccionado, _puedeEditar, _dueno.Bandeja.Layouts) { Owner = _dueno };
            if (ventana.ShowDialog() == true) { _dueno.Bandeja.RecargarLayouts(); _sel = null; Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(() => { Pintar(); })); _estado.Text = "Layout guardado"; }
        }

        private void Duplicar()
        {
            if (_sel == null) return;
            var copia = Layout.DeDatos(_sel.ADatos());
            copia.Id = ""; copia.Nombre = _sel.Nombre + " (copia)"; copia.Origen = "empresa"; copia.Version = 1;
            Editar(copia, null);
        }

        private void Exportar()
        {
            if (_sel == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Layout de BrosLMV (*.json)|*.json", FileName = _sel.Id + ".json" };
            if (d.ShowDialog(_dueno) == true) { File.WriteAllText(d.FileName, _sel.ADatos(), new System.Text.UTF8Encoding(false)); _estado.Text = "Exportado"; }
        }

        private async void Importar()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Layout de BrosLMV (*.json)|*.json", Multiselect = true };
            if (d.ShowDialog(_dueno) != true) return;
            int n = 0; string error = null;
            foreach (var f in d.FileNames)
            {
                try
                {
                    var l = Layout.DeDatos(File.ReadAllText(f));
                    if (string.IsNullOrWhiteSpace(l.Id) || l.Reconocer == null || l.Reconocer.Count == 0) throw new InvalidOperationException("no es un layout válido");
                    await Task.Run(() => _ctx.Banco.GuardarLayout(l)); n++;
                }
                catch (Exception ex) { error = Path.GetFileName(f) + ": " + ex.Message; }
            }
            _estado.Text = error ?? (n + (n == 1 ? " layout importado" : " layouts importados"));
            _dueno.Bandeja.RecargarLayouts();
            await Task.Delay(400); Pintar();
        }

        private async void Quitar()
        {
            if (_sel == null) return;
            if (MessageBox.Show(_dueno, "¿Volver al layout de fábrica «" + _sel.Nombre + "»? Tus cambios quedan en el historial.", "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string id = _sel.Id;
            await Task.Run(() => _ctx.Banco.QuitarLayout(id));
            _dueno.Bandeja.RecargarLayouts(); _sel = null; await Task.Delay(400); Pintar();
        }
    }
}
