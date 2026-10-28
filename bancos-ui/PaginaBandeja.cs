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
using System.Windows.Threading;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.BancosUI
{
    // La bandeja: los archivos que llegan (carpeta vigilada, arrastrar o elegir), con su estado, y la carga al estado de cuenta de Comercial.
    public class PaginaBandeja : UserControl
    {
        private readonly Contexto _ctx;
        private readonly VentanaPrincipal _dueno;
        private List<CuentaBanco> _cuentas = new List<CuentaBanco>();
        private List<Layout> _layouts = Catalogo.Combinar(null);
        private readonly System.Threading.Tasks.TaskCompletionSource<bool> _iniciado = new System.Threading.Tasks.TaskCompletionSource<bool>();
        private readonly List<AnalisisArchivo> _items = new List<AnalisisArchivo>();
        private readonly HashSet<string> _enAnalisis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AnalisisArchivo _sel;
        private string _carpeta = "";
        private FileSystemWatcher _vigilante;
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        private string _mensaje = "";

        private readonly Grid _raiz = new Grid();
        private readonly StackPanel _lista = new StackPanel();
        private readonly ContentControl _detalle = new ContentControl();
        private readonly TextBlock _resumen = Tema.Txt("", 12, false, Tema.Texto2);
        private readonly TextBlock _carpetaTxt = Tema.Txt("", 12, false, Tema.Texto2);
        private Button _btnCargar;

        public PaginaBandeja(Contexto ctx, VentanaPrincipal dueno)
        {
            _ctx = ctx; _dueno = dueno;
            Content = _raiz;
            AllowDrop = true;
            Drop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) AgregarArchivos(f); };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Escanear(); };
            Construir();
        }

        // ---------- estructura ----------
        private void Construir()
        {
            _raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 130 });
            _raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star), MinHeight = 160 });
            _raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var barra = new DockPanel { Margin = new Thickness(20, 14, 20, 10), LastChildFill = true };
            var agregar = Tema.Boton("Agregar archivos", (s, e) => ElegirArchivos());
            DockPanel.SetDock(agregar, Dock.Right); barra.Children.Add(agregar);
            var cambiar = Tema.Boton("Carpeta…", (s, e) => ElegirCarpeta(), false, true);
            DockPanel.SetDock(cambiar, Dock.Right); barra.Children.Add(cambiar);
            barra.Children.Add(_carpetaTxt);
            Grid.SetRow(barra, 0); _raiz.Children.Add(barra);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _lista, Margin = new Thickness(20, 0, 20, 0) };
            var caja = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = scroll, Margin = new Thickness(20, 0, 20, 0) };
            scroll.Margin = new Thickness(0);
            Grid.SetRow(caja, 1); _raiz.Children.Add(caja);

            var cajaDet = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(20, 12, 20, 0), Child = _detalle };
            Grid.SetRow(cajaDet, 2); _raiz.Children.Add(cajaDet);

            var pie = new DockPanel { Margin = new Thickness(20, 12, 20, 14), LastChildFill = true };
            _btnCargar = Tema.Boton("Cargar", (s, e) => CargarListos(), true);
            DockPanel.SetDock(_btnCargar, Dock.Right); pie.Children.Add(_btnCargar);
            var deshacer = Tema.Boton("Deshacer última", (s, e) => DeshacerUltima());
            deshacer.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(deshacer, Dock.Right); pie.Children.Add(deshacer);
            pie.Children.Add(_resumen);
            Grid.SetRow(pie, 3); _raiz.Children.Add(pie);
        }

        // ---------- arranque ----------
        public async Task IniciarAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    _cuentas = _ctx.Banco.Cuentas();
                    _layouts = Catalogo.Combinar(_ctx.Banco.LayoutsEmpresa());
                    _carpeta = _ctx.Banco.Config("carpeta", "");
                });
            }
            catch (Exception ex) { _mensaje = "No se pudo leer la empresa: " + ex.Message; }
            _iniciado.TrySetResult(true);
            PonerCarpeta(_carpeta, false);
            Pintar();
            Escanear();
        }

        public void RecargarLayouts()
        {
            Task.Run(() => _ctx.Banco.LayoutsEmpresa()).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                Dispatcher.Invoke(() => { _layouts = Catalogo.Combinar(t.Result); foreach (var a in _items.ToList()) Reanalizar(a); });
            });
        }

        public List<Layout> Layouts { get { return _layouts; } }
        public List<CuentaBanco> Cuentas { get { return _cuentas; } }
        public AnalisisArchivo Seleccionado { get { return _sel; } }
        public IEnumerable<AnalisisArchivo> Archivos { get { return _items.ToList(); } }

        // ---------- carpeta vigilada ----------
        private void PonerCarpeta(string carpeta, bool guardar)
        {
            _carpeta = carpeta ?? "";
            _carpetaTxt.Text = _carpeta.Length == 0 ? "Elige la carpeta donde llegan los archivos del banco" : _carpeta;
            if (_vigilante != null) { _vigilante.Dispose(); _vigilante = null; }
            if (_carpeta.Length > 0 && Directory.Exists(_carpeta))
            {
                try
                {
                    _vigilante = new FileSystemWatcher(_carpeta) { IncludeSubdirectories = false, EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => { _debounce.Stop(); _debounce.Start(); }));
                    _vigilante.Created += h; _vigilante.Changed += h; _vigilante.Deleted += h; _vigilante.Renamed += (s, e) => h(s, e);
                }
                catch { }
            }
            if (guardar) { var c = _carpeta; Task.Run(() => _ctx.Banco.PonerConfig("carpeta", c)); }
        }

        private void ElegirCarpeta()
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta donde llegan los archivos del banco" };
            if (_carpeta.Length > 0 && Directory.Exists(_carpeta)) d.InitialDirectory = _carpeta;
            if (d.ShowDialog(_dueno) == true) { PonerCarpeta(d.FolderName, true); Escanear(); }
        }

        private void ElegirArchivos()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Archivos del banco", Filter = "Estados de cuenta y movimientos|*.csv;*.txt;*.xlsx;*.xlsm;*.xls;*.pdf|Todos|*.*" };
            if (d.ShowDialog(_dueno) == true) AgregarArchivos(d.FileNames);
        }

        private void Escanear()
        {
            if (_carpeta.Length == 0 || !Directory.Exists(_carpeta)) return;
            string[] archivos;
            try { archivos = Directory.GetFiles(_carpeta).Where(Lector.EsArchivoDeBanco).ToArray(); } catch { return; }
            // lo que ya no esta en la carpeta sale de la lista (si venia de ella)
            foreach (var a in _items.Where(x => EnCarpeta(x.Ruta) && !File.Exists(x.Ruta)).ToList()) { _items.Remove(a); if (_sel == a) _sel = null; }
            AgregarArchivos(archivos);
            Pintar();
        }

        private bool EnCarpeta(string ruta)
        {
            try { return _carpeta.Length > 0 && string.Equals(Path.GetDirectoryName(Path.GetFullPath(ruta)), Path.GetFullPath(_carpeta).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }

        // ---------- analisis ----------
        public void AgregarArchivos(IEnumerable<string> rutas)
        {
            foreach (var r in rutas.Where(x => File.Exists(x) && Lector.EsArchivoDeBanco(x)))
            {
                if (_items.Any(i => string.Equals(i.Ruta, r, StringComparison.OrdinalIgnoreCase)) || !_enAnalisis.Add(r)) continue;
                string ruta = r;
                _items.Add(new AnalisisArchivo { Ruta = ruta, Nombre = Path.GetFileName(ruta), Estado = EstadoArchivo.Revisar, Chip = "Leyendo…" });
                Task.Run(() => { _iniciado.Task.Wait(); EsperarArchivo(ruta); return Analizador.Analizar(ruta, _layouts, _cuentas, (c, d, h) => _ctx.Banco.Existentes(c, d, h)); }).ContinueWith(t =>
                    Dispatcher.Invoke(() =>
                    {
                        _enAnalisis.Remove(ruta);
                        var viejo = _items.FirstOrDefault(i => string.Equals(i.Ruta, ruta, StringComparison.OrdinalIgnoreCase));
                        int pos = viejo == null ? -1 : _items.IndexOf(viejo);
                        var nuevo = t.IsFaulted ? new AnalisisArchivo { Ruta = ruta, Nombre = Path.GetFileName(ruta), Estado = EstadoArchivo.Error, Chip = "No se pudo leer", Mensaje = t.Exception.GetBaseException().Message } : t.Result;
                        if (pos >= 0) _items[pos] = nuevo; else _items.Add(nuevo);
                        if (_sel == viejo) _sel = nuevo;
                        if (_sel == null) _sel = nuevo;
                        Pintar();
                    }));
            }
            Pintar();
        }

        private static void EsperarArchivo(string ruta)
        {
            // un archivo que se esta copiando todavia no se puede abrir
            for (int i = 0; i < 12; i++)
            {
                try { using (File.Open(ruta, FileMode.Open, FileAccess.Read, FileShare.Read)) return; } catch (IOException) { System.Threading.Thread.Sleep(500); }
            }
        }

        private void Reanalizar(AnalisisArchivo a)
        {
            string ruta = a.Ruta;
            _items.Remove(a); if (_sel == a) _sel = null;
            AgregarArchivos(new[] { ruta });
        }

        // ---------- pintar ----------
        private void Pintar()
        {
            _lista.Children.Clear();
            if (_items.Count == 0)
            {
                _lista.Children.Add(new StackPanel
                {
                    Margin = new Thickness(0, 46, 0, 46), HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { Tema.Txt(_carpeta.Length == 0 ? "Elige una carpeta para empezar" : "Sin archivos por cargar", 15, true, Tema.Texto), Tema.Txt("Suelta aquí los archivos del banco", 12, false, Tema.Texto2) }
                });
            }
            foreach (var a in _items) _lista.Children.Add(Fila(a));
            _detalle.Content = _sel == null ? null : Detalle(_sel);
            int listos = _items.Count(i => i.Estado == EstadoArchivo.Listo);
            int movs = _items.Where(i => i.Estado == EstadoArchivo.Listo).Sum(i => i.TotalNuevos);
            int revisar = _items.Count(i => i.Estado == EstadoArchivo.Revisar && i.Chip != "Leyendo…");
            int sin = _items.Count(i => i.Estado == EstadoArchivo.SinLayout || i.Estado == EstadoArchivo.Error);
            var partes = new List<string>();
            if (_mensaje.Length > 0) partes.Add(_mensaje);
            else
            {
                partes.Add(listos + " listos");
                if (revisar > 0) partes.Add(revisar + " por revisar");
                if (sin > 0) partes.Add(sin + " sin layout o con error");
            }
            _resumen.Text = string.Join(" · ", partes);
            _btnCargar.Content = listos > 0 ? "Cargar " + movs.ToString("#,##0") + " movimientos" : "Cargar";
            _btnCargar.IsEnabled = listos > 0;
        }

        private static Tono TonoDe(AnalisisArchivo a)
        {
            switch (a.Estado) { case EstadoArchivo.Listo: return Tono.Bueno; case EstadoArchivo.Revisar: return a.Chip == "Leyendo…" ? Tono.Neutro : Tono.Aviso; case EstadoArchivo.YaCargado: return Tono.Neutro; default: return Tono.Malo; }
        }

        private UIElement Fila(AnalisisArchivo a)
        {
            var g = new Grid { Margin = new Thickness(0), Cursor = Cursors.Hand };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var av = Tema.Avatar(a.Banco); av.Margin = new Thickness(0, 0, 12, 0); g.Children.Add(av);
            var meta = new List<string>();
            if (a.Banco.Length > 0) meta.Add(a.Banco);
            var cta = a.Grupos.Count == 1 && a.Grupos[0].Cuenta != null ? a.Grupos[0].Cuenta.Nombre : (a.Grupos.Count > 1 ? a.Grupos.Count + " cuentas" : "");
            if (cta.Length > 0) meta.Add(cta);
            if (a.Periodo.Length > 0) meta.Add(a.Periodo);
            if (a.TotalMovs > 0) meta.Add(a.TotalMovs.ToString("#,##0") + " mov.");
            if (meta.Count == 0 && a.Mensaje.Length > 0) meta.Add(a.Mensaje);
            var texto = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texto.Children.Add(Tema.Txt(a.Nombre, 13, false));
            texto.Children.Add(Tema.Txt(string.Join(" · ", meta), 12, false, Tema.Texto2));
            Grid.SetColumn(texto, 1); g.Children.Add(texto);
            var chip = Tema.Chip(a.Chip, TonoDe(a)); Grid.SetColumn(chip, 2); g.Children.Add(chip);
            var fila = new Border { Padding = new Thickness(16, 10, 16, 10), BorderBrush = Tema.Linea, BorderThickness = new Thickness(0, 0, 0, 1), Background = a == _sel ? Tema.AcentoFondo : Brushes.Transparent, Child = g };
            fila.MouseEnter += (s, e) => { if (a != _sel) fila.Background = Tema.Hover; };
            fila.MouseLeave += (s, e) => { if (a != _sel) fila.Background = Brushes.Transparent; };
            fila.MouseLeftButtonUp += (s, e) => { _sel = a; Pintar(); };
            return fila;
        }

        private sealed class FilaVista
        {
            public string Fecha { get; set; } public string Hora { get; set; } public string Descripcion { get; set; } public string Cargo { get; set; } public string Abono { get; set; } public string Saldo { get; set; } public string Tipo { get; set; } public string Estado { get; set; }
        }

        private UIElement Detalle(AnalisisArchivo a)
        {
            var panel = new DockPanel { Margin = new Thickness(0) };
            var cab = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
            DockPanel.SetDock(cab, Dock.Top); panel.Children.Add(cab);

            var fila1 = new DockPanel { LastChildFill = true };
            var acciones = new StackPanel { Orientation = Orientation.Horizontal };
            if (a.Estado == EstadoArchivo.SinLayout || a.Estado == EstadoArchivo.Error || a.Estado == EstadoArchivo.Revisar)
                acciones.Children.Add(Tema.Boton("Abrir en Layouts", (s, e) => _dueno.AbrirEditor(a), false, true));
            acciones.Children.Add(Tema.Boton("Quitar", (s, e) => { _items.Remove(a); if (_sel == a) _sel = _items.FirstOrDefault(); Pintar(); }, false, true));
            DockPanel.SetDock(acciones, Dock.Right); fila1.Children.Add(acciones);
            string lay = a.Layout != null ? a.Layout.Nombre + (a.Layout.Origen == "empresa" ? " · v" + a.Layout.Version : "") : "Sin layout";
            fila1.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Tema.Txt("Layout", 12, false, Tema.Texto2), Tema.Txt("  " + lay, 13, true) } });
            cab.Children.Add(fila1);

            foreach (var g in a.Grupos)
            {
                var gg = g;
                var f = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
                string etiqueta = a.Grupos.Count > 1 ? (g.CuentaArchivo.Length > 0 ? g.CuentaArchivo : "Sin cuenta") + " · " + g.Movs.Count + " mov." : "Cuenta";
                var lbl = Tema.Txt(etiqueta, 12, false, Tema.Texto2); lbl.Width = a.Grupos.Count > 1 ? 130 : 56; DockPanel.SetDock(lbl, Dock.Left); f.Children.Add(lbl);
                var combo = new ComboBox { MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = _cuentas, SelectedItem = g.Cuenta, MaxWidth = 460 };
                combo.SelectionChanged += (s, e) =>
                {
                    var c = combo.SelectedItem as CuentaBanco;
                    if (c == null || c == gg.Cuenta && gg.Confirmada) return;
                    Analizador.Asignar(a, gg, c, (cc, d, h) => _ctx.Banco.Existentes(cc, d, h));
                    Pintar();
                };
                f.Children.Add(combo);
                if (g.Cuenta != null && !g.Confirmada)
                {
                    var conf = Tema.Boton("Confirmar", (s, e) => { gg.Confirmada = true; Analizador.Reevaluar(a, (cc, d, h) => _ctx.Banco.Existentes(cc, d, h)); Pintar(); }, false, true);
                    conf.Margin = new Thickness(8, 0, 0, 0); f.Children.Add(conf);
                }
                cab.Children.Add(f);
                if (!string.IsNullOrEmpty(g.Sugerencia.Motivo) && (g.Cuenta == null || !g.Confirmada))
                    cab.Children.Add(new TextBlock { Text = (g.Cuenta == null ? "" : "Sugerida: ") + g.Sugerencia.Motivo, FontSize = 12, Foreground = Tema.Texto2, Margin = new Thickness(a.Grupos.Count > 1 ? 130 : 56, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            }

            var notas = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var n in a.Notas.Concat(a.Mensaje.Length > 0 ? new[] { a.Mensaje } : new string[0]).Where(x => !x.StartsWith("No reconocí el banco")).Take(4))
            {
                bool aviso = n.StartsWith("OJO") || n.Contains("no cuadra") || n.Contains("500") || n.Contains("tránsito") || n.Contains("revisión") || a.Estado == EstadoArchivo.Error;
                var chip = Tema.Chip(n.Length > 110 ? n.Substring(0, 107) + "…" : n, aviso ? Tono.Aviso : Tono.Neutro); chip.Margin = new Thickness(0, 0, 6, 6); chip.ToolTip = n; notas.Children.Add(chip);
            }
            if (notas.Children.Count > 0) cab.Children.Add(notas);

            var filas = new List<FilaVista>();
            foreach (var g in a.Grupos)
                foreach (var m in g.Movs.Take(400))
                    filas.Add(new FilaVista
                    {
                        Fecha = m.Fecha.ToString("dd/MM/yyyy"), Hora = m.Hora.HasValue ? m.Hora.Value.ToString(@"hh\:mm") : "", Descripcion = m.Descripcion + (m.Detalle.Length > 0 ? " · " + m.Detalle : ""),
                        Cargo = m.Cargo != 0 ? Tema.Dinero(m.Cargo) : "", Abono = m.Abono != 0 ? Tema.Dinero(m.Abono) : "", Saldo = m.Saldo.HasValue ? Tema.Dinero(m.Saldo.Value) : "", Tipo = m.Tipo, Estado = g.Repetidos.Contains(m) ? "Repetido" : "Nuevo"
                    });
            var dg = new DataGrid { ItemsSource = filas, Margin = new Thickness(0) };
            Func<string, string, double, bool, DataGridTextColumn> col = (enc, prop, ancho, der) =>
            {
                var c = new DataGridTextColumn { Header = enc, Binding = new System.Windows.Data.Binding(prop), Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star) };
                if (der) { var st = new Style(typeof(TextBlock)); st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right)); c.ElementStyle = st; }
                return c;
            };
            dg.Columns.Add(col("Fecha", "Fecha", 84, false)); dg.Columns.Add(col("Hora", "Hora", 52, false)); dg.Columns.Add(col("Descripción", "Descripcion", 0, false));
            dg.Columns.Add(col("Cargo", "Cargo", 96, true)); dg.Columns.Add(col("Abono", "Abono", 96, true)); dg.Columns.Add(col("Saldo", "Saldo", 104, true));
            dg.Columns.Add(col("Tipo", "Tipo", 130, false)); dg.Columns.Add(col("", "Estado", 78, false));
            var rs = new Style(typeof(DataGridRow));
            var tr = new DataTrigger { Binding = new System.Windows.Data.Binding("Estado"), Value = "Repetido" }; tr.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Gray));
            rs.Triggers.Add(tr); dg.RowStyle = rs;
            panel.Children.Add(new Border { BorderBrush = Tema.Linea, BorderThickness = new Thickness(0, 1, 0, 0), Child = dg });
            return panel;
        }

        // ---------- cargar ----------
        private async void CargarListos()
        {
            var listos = _items.Where(i => i.Estado == EstadoArchivo.Listo).ToList();
            if (listos.Count == 0) return;
            _btnCargar.IsEnabled = false; _mensaje = "Cargando…"; Pintar();
            int total = 0, archivos = 0; string error = null;
            await Task.Run(() =>
            {
                foreach (var a in listos)
                {
                    try
                    {
                        var grupos = a.Grupos.Select(g => new KeyValuePair<long, IEnumerable<BancoMov>>(g.Cuenta.Id, g.MovsNuevos.ToList())).Where(k => k.Value.Any()).ToList();
                        if (grupos.Count == 0) continue;
                        total += _ctx.Banco.CargarGrupos(grupos, a.Nombre, a.Hash, a.Layout != null ? a.Layout.Id : null).Cargados; archivos++;
                        MoverAProcesados(a);
                    }
                    catch (Exception ex) { error = a.Nombre + ": " + ex.Message; break; }
                }
            });
            _mensaje = error != null ? "No se cargó: " + error : "Cargados " + total.ToString("#,##0") + " movimientos de " + archivos + (archivos == 1 ? " archivo" : " archivos");
            try { _cuentas = await Task.Run(() => _ctx.Banco.Cuentas()); } catch { }
            foreach (var a in _items.Where(i => i.Estado == EstadoArchivo.Listo && !File.Exists(i.Ruta)).ToList()) { _items.Remove(a); if (_sel == a) _sel = null; }
            foreach (var a in _items.ToList()) if (a.Estado != EstadoArchivo.SinLayout && a.Estado != EstadoArchivo.Error) Reanalizar(a);
            Pintar();
        }

        private void MoverAProcesados(AnalisisArchivo a)
        {
            if (!EnCarpeta(a.Ruta)) return;
            try
            {
                string dir = Path.Combine(_carpeta, "Procesados", DateTime.Now.ToString("yyyy-MM")); Directory.CreateDirectory(dir);
                string destino = Path.Combine(dir, a.Nombre); int n = 1;
                while (File.Exists(destino)) destino = Path.Combine(dir, Path.GetFileNameWithoutExtension(a.Nombre) + " (" + (++n) + ")" + Path.GetExtension(a.Nombre));
                File.Move(a.Ruta, destino);
            }
            catch { }
        }

        private async void DeshacerUltima()
        {
            if (MessageBox.Show(_dueno, "¿Deshacer tu última carga?", "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { _mensaje = await Task.Run(() => _ctx.Banco.DeshacerUltima()); } catch (Exception ex) { _mensaje = "No se pudo deshacer: " + ex.Message; }
            foreach (var a in _items.ToList()) Reanalizar(a);
            Pintar();
        }
    }
}
