// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.BancosUI
{
    // Crear o ajustar un layout con un archivo de muestra: se marca el encabezado y que es cada columna, se ve el resultado en vivo
    // y se prueba contra lo que ya esta en la bandeja antes de guardar.
    public class EditorLayoutVentana : Window
    {
        private static readonly string[][] Campos =
        {
            new[] { "", "—" }, new[] { "fecha", "Fecha" }, new[] { "hora", "Hora" }, new[] { "descripcion", "Descripción" }, new[] { "detalle", "Detalle" }, new[] { "referencia", "Referencia" },
            new[] { "cargo", "Cargo" }, new[] { "abono", "Abono" }, new[] { "importe", "Importe" }, new[] { "signo", "Signo cargo/abono" }, new[] { "saldo", "Saldo" }, new[] { "cuenta", "Cuenta" }, new[] { "moneda", "Divisa" }
        };

        private readonly Contexto _ctx;
        private readonly Layout _original;
        private readonly bool _puedeGuardar;
        private readonly List<Layout> _todos;
        private AnalisisArchivo _muestra;
        private HojaArchivo _hoja;
        private bool _esPdf;
        private int _filaEnc = -1;
        private string[] _campo = new string[0];
        private List<List<Condicion>> _otrosCaminos = new List<List<Condicion>>();
        private bool _construyendo;

        private readonly Grid _tabla = new Grid();
        private readonly TextBox _nombre = new TextBox(), _textoArchivo = new TextBox(), _nombreArchivo = new TextBox(), _textoPdf = new TextBox();
        private readonly ComboBox _banco = new ComboBox { IsEditable = true }, _fecha = new ComboBox(), _estrategia = new ComboBox();
        private readonly CheckBox _negCargo = new CheckBox { Content = "Importe negativo es cargo", IsChecked = true }, _divisa = new CheckBox { Content = "Cada divisa es una cuenta" }, _sinEnc = new CheckBox { Content = "El archivo no trae encabezado" };
        private readonly WrapPanel _condEnc = new WrapPanel();
        private readonly StackPanel _estadoPanel = new StackPanel();
        private readonly ContentControl _vista = new ContentControl();
        private readonly TextBlock _aviso = Tema.Txt("", 12, false, Tema.Pincel("#7A4E00"), TextWrapping.Wrap);
        private Button _guardar;

        public EditorLayoutVentana(Contexto ctx, Layout layout, AnalisisArchivo muestra, bool puedeGuardar, List<Layout> todos)
        {
            _ctx = ctx; _puedeGuardar = puedeGuardar; _todos = todos; _muestra = muestra;
            _original = layout != null ? Layout.DeDatos(layout.ADatos()) : new Layout { Origen = "empresa", Nombre = "", Reconocer = new List<List<Condicion>>() };
            Title = layout == null ? "Nuevo layout" : "Layout · " + layout.Nombre;
            Width = 1180; Height = 800; MinWidth = 980; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Tema.Pagina; FontFamily = new FontFamily("Segoe UI");
            Construir();
            if (_muestra != null && _muestra.Hoja != null) UsarMuestra(_muestra.Hoja, _muestra.Nombre);
            else PintarSinMuestra();
        }

        // ---------- estructura ----------
        private void Construir()
        {
            var raiz = new Grid { Margin = new Thickness(18) };
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // izquierda: la muestra
            var izq = new DockPanel();
            var barra = new DockPanel { Margin = new Thickness(14, 10, 14, 6) };
            var abrir = Tema.Boton("Otro archivo de muestra…", (s, e) => AbrirMuestra(), false, true); DockPanel.SetDock(abrir, Dock.Right); barra.Children.Add(abrir);
            barra.Children.Add(Tema.Txt("Muestra", 12, true, Tema.Texto2));
            DockPanel.SetDock(barra, Dock.Top); izq.Children.Add(barra);
            _sinEnc.Margin = new Thickness(14, 0, 14, 8); _sinEnc.Click += (s, e) => { if (_construyendo) return; _filaEnc = _sinEnc.IsChecked == true ? -1 : 0; ReconstruirTabla(); Recalcular(); };
            DockPanel.SetDock(_sinEnc, Dock.Top); izq.Children.Add(_sinEnc);
            izq.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tabla, Margin = new Thickness(14, 0, 14, 12) });
            var cajaIzq = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = izq };
            Grid.SetColumn(cajaIzq, 0); Grid.SetRow(cajaIzq, 0); raiz.Children.Add(cajaIzq);

            // derecha: el layout
            var der = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            der.Children.Add(Etiqueta("Nombre")); _nombre.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; der.Children.Add(_nombre);
            der.Children.Add(Etiqueta("Banco"));
            foreach (var b in BancoCatalogo.Bancos.Where(x => x != "Otro")) _banco.Items.Add(b);
            _banco.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => { if (!_construyendo) Recalcular(); }));
            der.Children.Add(_banco);
            var fila = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var colF = new StackPanel(); colF.Children.Add(Etiqueta("Fecha")); _fecha.Items.Add("Día/mes (México)"); _fecha.Items.Add("Mes/día"); _fecha.SelectedIndex = 0; _fecha.SelectionChanged += (s, e) => { if (!_construyendo) Recalcular(); }; colF.Children.Add(_fecha);
            var colE = new StackPanel(); colE.Children.Add(Etiqueta("Estrategia de PDF")); _estrategia.Items.Add("Impresión de BBVANet"); _estrategia.Items.Add("Impresión del export"); _estrategia.Items.Add("Estado de cuenta oficial"); _estrategia.SelectedIndex = 0; _estrategia.SelectionChanged += (s, e) => { if (!_construyendo) Recalcular(); }; colE.Children.Add(_estrategia);
            Grid.SetColumn(colF, 0); Grid.SetColumn(colE, 2); fila.Children.Add(colF); fila.Children.Add(colE);
            der.Children.Add(fila);
            _negCargo.Margin = new Thickness(0, 12, 0, 0); _negCargo.Click += (s, e) => Recalcular(); _divisa.Margin = new Thickness(0, 6, 0, 0); _divisa.Click += (s, e) => Recalcular();
            der.Children.Add(_negCargo); der.Children.Add(_divisa);
            der.Children.Add(Etiqueta("Reconocer cuando el encabezado trae")); _condEnc.Margin = new Thickness(0, 0, 0, 2); der.Children.Add(_condEnc);
            der.Children.Add(Etiqueta("El texto del archivo contiene")); _textoArchivo.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; der.Children.Add(_textoArchivo);
            der.Children.Add(Etiqueta("El nombre del archivo contiene")); _nombreArchivo.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; der.Children.Add(_nombreArchivo);
            der.Children.Add(Etiqueta("El PDF contiene")); _textoPdf.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; der.Children.Add(_textoPdf);
            var cajaDer = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = der } };
            Grid.SetColumn(cajaDer, 2); Grid.SetRow(cajaDer, 0); Grid.SetRowSpan(cajaDer, 3); raiz.Children.Add(cajaDer);

            // abajo: el resultado en vivo
            var abajo = new DockPanel();
            _estadoPanel.Orientation = Orientation.Horizontal; _estadoPanel.Margin = new Thickness(14, 10, 14, 6);
            DockPanel.SetDock(_estadoPanel, Dock.Top); abajo.Children.Add(_estadoPanel);
            _aviso.Margin = new Thickness(14, 0, 14, 6); DockPanel.SetDock(_aviso, Dock.Top); abajo.Children.Add(_aviso);
            abajo.Children.Add(_vista);
            var cajaAbajo = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = abajo };
            Grid.SetColumn(cajaAbajo, 0); Grid.SetRow(cajaAbajo, 2); raiz.Children.Add(cajaAbajo);

            var pie = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _guardar = Tema.Boton("Guardar", (s, e) => Guardar(), true); DockPanel.SetDock(_guardar, Dock.Right); pie.Children.Add(_guardar);
            var cancelar = Tema.Boton("Cancelar", (s, e) => { DialogResult = false; }); cancelar.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(cancelar, Dock.Right); pie.Children.Add(cancelar);
            pie.Children.Add(Tema.Txt(_puedeGuardar ? "" : "No tienes permiso para guardar layouts", 12, false, Tema.Texto2));
            Grid.SetRow(pie, 3); Grid.SetColumnSpan(pie, 3); raiz.Children.Add(pie);
            Content = raiz;
        }

        private static TextBlock Etiqueta(string t) { var x = Tema.Txt(t, 12, false, Tema.Texto2); x.Margin = new Thickness(0, 12, 0, 4); return x; }

        // ---------- muestra ----------
        private void AbrirMuestra()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Archivo de muestra", Filter = "Estados de cuenta y movimientos|*.csv;*.txt;*.xlsx;*.xlsm;*.xls;*.pdf|Todos|*.*" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                var notas = new List<string>();
                var hoja = Lector.Leer(d.FileName, notas, _todos)[0];
                UsarMuestra(hoja, Path.GetFileName(d.FileName));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void PintarSinMuestra()
        {
            _tabla.Children.Clear(); _tabla.RowDefinitions.Clear(); _tabla.ColumnDefinitions.Clear();
            _tabla.Children.Add(new StackPanel { Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Children = { Tema.Txt("Abre un archivo de muestra para empezar", 14, true), Tema.Boton("Elegir archivo…", (s, e) => AbrirMuestra()) } });
            ((StackPanel)_tabla.Children[0]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 12, 0, 0));
            _guardar.IsEnabled = false;
        }

        private string _nombreMuestra = "";

        private void UsarMuestra(HojaArchivo hoja, string nombre)
        {
            _hoja = hoja; _nombreMuestra = nombre;
            _esPdf = hoja.LayoutPdf != null || nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            _construyendo = true;
            try
            {
                int ncol = Math.Min(14, hoja.Filas.Take(60).Max(f => f.Length));
                _campo = new string[ncol];
                var l = _original;
                // encabezado y columnas: lo que dice el layout; si no, lo que dedujo el motor
                BancoMapa mapa = null;
                if (_muestra != null && _muestra.Deteccion != null && string.Equals(_muestra.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) mapa = _muestra.Deteccion.Mapa;
                else { try { mapa = BancoArchivo.Detectar(hoja.Filas, nombre, _todos).Mapa; } catch { } }
                _filaEnc = mapa != null ? mapa.FilaEncabezado : 0;
                if (l.Columnas != null && l.Columnas.Count > 0)
                {
                    if (l.FilaEncabezado > 0) _filaEnc = l.FilaEncabezado - 1;
                    var celdas = _filaEnc >= 0 && _filaEnc < hoja.Filas.Count ? hoja.Filas[_filaEnc].Select(c => BancoTexto.Norm(BancoTexto.Limpiar(c))).ToArray() : new string[0];
                    foreach (var kv in l.Columnas)
                        foreach (var nom in kv.Value)
                        {
                            int j = nom.StartsWith("#") && int.TryParse(nom.Substring(1), out int p) ? p - 1 : Array.IndexOf(celdas, BancoTexto.Norm(nom));
                            if (j >= 0 && j < ncol && string.IsNullOrEmpty(_campo[j])) _campo[j] = kv.Key.ToLowerInvariant();
                        }
                }
                else if (mapa != null)
                {
                    Action<int, string> pon = (j, k) => { if (j >= 0 && j < ncol && string.IsNullOrEmpty(_campo[j])) _campo[j] = k; };
                    pon(mapa.Fecha, "fecha"); pon(mapa.Hora, "hora"); pon(mapa.Descripcion, "descripcion"); foreach (var d in mapa.Detalle) pon(d, "detalle"); pon(mapa.Referencia, "referencia");
                    pon(mapa.Cargo, "cargo"); pon(mapa.Abono, "abono"); pon(mapa.Importe, "importe"); pon(mapa.Signo, "signo"); pon(mapa.Saldo, "saldo"); pon(mapa.Cuenta, "cuenta"); pon(mapa.Moneda, "moneda");
                }
                _sinEnc.IsChecked = _filaEnc < 0;
                _nombre.Text = l.Nombre; _banco.Text = l.Banco;
                if (l.Banco.Length == 0 && _muestra != null && _muestra.Deteccion != null && string.Equals(_muestra.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) _banco.Text = _muestra.Deteccion.Banco;
                _fecha.SelectedIndex = l.Ajustes != null && l.Ajustes.FormatoFecha == "mdy" ? 1 : 0;
                _negCargo.IsChecked = l.Ajustes == null || !l.Ajustes.ImporteNegativoEsCargo.HasValue || l.Ajustes.ImporteNegativoEsCargo.Value;
                _divisa.IsChecked = l.Ajustes != null && l.Ajustes.CuentaDesdeDivisa;
                _estrategia.SelectedIndex = l.Pdf != null && l.Pdf.Estrategia == "export" ? 1 : l.Pdf != null && l.Pdf.Estrategia == "estadocuenta" ? 2 : 0;
                CargarReconocer(l);
                _estrategia.IsEnabled = _esPdf; _textoPdf.IsEnabled = _esPdf; _fecha.IsEnabled = !_esPdf; _negCargo.IsEnabled = !_esPdf; _divisa.IsEnabled = !_esPdf; _sinEnc.IsEnabled = !_esPdf;
            }
            finally { _construyendo = false; }
            ReconstruirTabla(); ReconstruirCondEnc(); Recalcular();
        }

        private HashSet<string> _marcadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void CargarReconocer(Layout l)
        {
            _marcadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _textoArchivo.Text = _nombreArchivo.Text = _textoPdf.Text = "";
            _otrosCaminos = new List<List<Condicion>>();
            if (l.Reconocer == null || l.Reconocer.Count == 0) return;
            foreach (var c in l.Reconocer[0])
            {
                string valores = string.Join(", ", c.Contiene ?? new List<string>());
                switch ((c.En ?? "").ToLowerInvariant())
                {
                    case "texto": _textoArchivo.Text = valores; break;
                    case "archivo": _nombreArchivo.Text = valores; break;
                    case "pdf": _textoPdf.Text = valores; break;
                    default: foreach (var v in c.Contiene ?? new List<string>()) _marcadas.Add(v); break;
                }
            }
            _otrosCaminos = l.Reconocer.Skip(1).ToList();
        }

        // ---------- tabla de muestra ----------
        private void ReconstruirTabla()
        {
            _tabla.Children.Clear(); _tabla.RowDefinitions.Clear(); _tabla.ColumnDefinitions.Clear();
            if (_hoja == null) return;
            int ncol = _campo.Length, nfil = Math.Min(_hoja.Filas.Count, 40);
            _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            for (int j = 0; j < ncol; j++) _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(j == 1 || j == 2 ? 190 : 122) });
            _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < nfil; i++) _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int j = 0; j < ncol; j++)
            {
                int col = j;
                var combo = new ComboBox { Margin = new Thickness(2, 0, 2, 6), FontSize = 12 };
                foreach (var c in Campos) combo.Items.Add(c[1]);
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(Campos, c => c[0] == (_campo[col] ?? "")));
                if (!string.IsNullOrEmpty(_campo[col])) combo.Background = Tema.AcentoFondo;
                combo.SelectionChanged += (s, e) => { _campo[col] = Campos[combo.SelectedIndex][0]; if (!_construyendo) { ReconstruirCondEnc(); Recalcular(); } };
                Grid.SetRow(combo, 0); Grid.SetColumn(combo, j + 1); _tabla.Children.Add(combo);
            }
            for (int i = 0; i < nfil; i++)
            {
                int fila = i;
                bool esEnc = i == _filaEnc, antes = _filaEnc >= 0 && i < _filaEnc;
                var num = new Button { Content = (i + 1).ToString(), FontSize = 11, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Fila de encabezado" };
                num.Style = (Style)Application.Current.Resources["BotonTexto"];
                num.Foreground = esEnc ? Tema.Acento : Tema.Texto2; num.FontWeight = esEnc ? FontWeights.Bold : FontWeights.Normal;
                num.Click += (s, e) => { _filaEnc = fila; _sinEnc.IsChecked = false; ReconstruirTabla(); ReconstruirCondEnc(); Recalcular(); };
                Grid.SetRow(num, i + 1); Grid.SetColumn(num, 0); _tabla.Children.Add(num);
                for (int j = 0; j < ncol; j++)
                {
                    string t = j < _hoja.Filas[i].Length ? _hoja.Filas[i][j] : "";
                    var celda = new Border
                    {
                        BorderBrush = Tema.Linea, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 4, 6, 4),
                        Background = esEnc ? Tema.Pincel("#EDEFF3") : (!string.IsNullOrEmpty(_campo[j]) && !antes && !esEnc ? Tema.Pincel("#F5F9FF") : Brushes.Transparent),
                        Child = new TextBlock { Text = t, FontSize = 12, FontWeight = esEnc ? FontWeights.SemiBold : FontWeights.Normal, Foreground = antes ? Tema.Pincel("#9AA3B0") : Tema.Texto, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t }
                    };
                    Grid.SetRow(celda, i + 1); Grid.SetColumn(celda, j + 1); _tabla.Children.Add(celda);
                }
            }
        }

        private void ReconstruirCondEnc()
        {
            _condEnc.Children.Clear();
            if (_hoja == null || _esPdf || _filaEnc < 0 || _filaEnc >= _hoja.Filas.Count) { _condEnc.Children.Add(Tema.Txt(_esPdf ? "No aplica a PDF" : "Sin encabezado", 12, false, Tema.Texto2)); return; }
            var fila = _hoja.Filas[_filaEnc];
            bool automatico = _marcadas.Count == 0 && _original.Reconocer.Count == 0;      // layout nuevo: se proponen las columnas ya marcadas
            for (int j = 0; j < Math.Min(fila.Length, _campo.Length); j++)
            {
                string t = BancoTexto.Limpiar(fila[j]); if (t.Length == 0) continue;
                string n = BancoTexto.Norm(t);
                bool marcado = _marcadas.Contains(n) || (automatico && !string.IsNullOrEmpty(_campo[j]));
                var cb = new CheckBox { Content = t, IsChecked = marcado, Margin = new Thickness(0, 0, 12, 4), FontSize = 12 };
                cb.Click += (s, e) => { if (cb.IsChecked == true) _marcadas.Add(n); else _marcadas.Remove(n); Recalcular(); };
                if (marcado) _marcadas.Add(n);
                _condEnc.Children.Add(cb);
            }
        }

        // ---------- del formulario al layout ----------
        private static List<string> Lista(string t) { return (t ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); }

        private static string Slug(string s)
        {
            string n = Regex.Replace(BancoTexto.Norm(s ?? ""), @"[^a-z0-9]+", "-").Trim('-');
            return n.Length == 0 ? "layout" : n;
        }

        private Layout ConstruirLayout()
        {
            var l = Layout.DeDatos(_original.ADatos());
            l.Nombre = _nombre.Text.Trim(); l.Banco = (_banco.Text ?? "").Trim();
            if (string.IsNullOrEmpty(l.Id)) l.Id = Slug(l.Banco + " " + l.Nombre);
            l.Origen = "empresa"; l.Formato = _esPdf ? "pdf" : "tabla";
            var camino = new List<Condicion>();
            if (!_esPdf && _filaEnc >= 0 && _marcadas.Count > 0) foreach (var m in _marcadas.OrderBy(x => x)) camino.Add(new Condicion { En = "encabezado", Contiene = new List<string> { m } });
            var texto = Lista(_textoArchivo.Text); if (texto.Count > 0) camino.Add(new Condicion { En = "texto", Contiene = texto });
            var arch = Lista(_nombreArchivo.Text); if (arch.Count > 0) camino.Add(new Condicion { En = "archivo", Contiene = arch });
            var pdf = Lista(_textoPdf.Text); if (_esPdf && pdf.Count > 0) camino.Add(new Condicion { En = "pdf", Contiene = pdf });
            l.Reconocer = new List<List<Condicion>>(); if (camino.Count > 0) l.Reconocer.Add(camino); l.Reconocer.AddRange(_otrosCaminos);
            if (_esPdf) { l.Pdf = new PdfOpciones { Estrategia = _estrategia.SelectedIndex == 1 ? "export" : _estrategia.SelectedIndex == 2 ? "estadocuenta" : "bbvanet" }; l.Columnas = null; }
            else
            {
                l.Pdf = null;
                var cols = new Dictionary<string, List<string>>();
                var celdas = _filaEnc >= 0 && _hoja != null && _filaEnc < _hoja.Filas.Count ? _hoja.Filas[_filaEnc] : new string[0];
                bool todoPorNombre = _filaEnc >= 0;
                var nombresUsados = new List<string>();
                for (int j = 0; j < _campo.Length; j++)
                {
                    if (string.IsNullOrEmpty(_campo[j])) continue;
                    string nom = j < celdas.Length ? BancoTexto.Limpiar(celdas[j]) : "";
                    bool repetido = nom.Length == 0 || celdas.Count(c => BancoTexto.Norm(BancoTexto.Limpiar(c)) == BancoTexto.Norm(nom)) > 1;
                    if (repetido) todoPorNombre = false;
                    string valor = (_filaEnc >= 0 && !repetido) ? nom : "#" + (j + 1);
                    List<string> lst; if (!cols.TryGetValue(_campo[j], out lst)) cols[_campo[j]] = lst = new List<string>();
                    lst.Add(valor);
                }
                l.Columnas = cols.Count > 0 ? cols : null;
                l.FilaEncabezado = _filaEnc >= 0 && !todoPorNombre ? _filaEnc + 1 : 0;
                l.PrimeraFila = _filaEnc < 0 ? 1 : 0;
                l.Ajustes = new Ajustes { FormatoFecha = _fecha.SelectedIndex == 1 ? "mdy" : null, ImporteNegativoEsCargo = _negCargo.IsChecked == true ? (bool?)null : false, CuentaDesdeDivisa = _divisa.IsChecked == true };
            }
            l.Confianza = 3; l.Umbral = 100;
            return l;
        }

        // ---------- resultado en vivo ----------
        private List<Layout> ConLayout(Layout nuevo)
        {
            var lista = _todos.Where(x => !string.Equals(x.Id, nuevo.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            lista.Add(nuevo); return lista;
        }

        private bool _valido;

        private void Recalcular()
        {
            _estadoPanel.Children.Clear(); _aviso.Text = ""; _valido = false;
            if (_hoja == null) return;
            var l = ConstruirLayout();
            string problema = null;
            if (l.Nombre.Length == 0) problema = "Falta el nombre";
            else if (l.Reconocer.Count == 0 || l.Reconocer[0].Count == 0) problema = "Falta cómo reconocer el archivo";
            else if (!_esPdf && !_campo.Contains("fecha")) problema = "Falta marcar la columna de fecha";
            else if (!_esPdf && !(_campo.Contains("cargo") || _campo.Contains("abono") || _campo.Contains("importe"))) problema = "Falta marcar cargo, abono o importe";
            try
            {
                var notas = new List<string>();
                var propios = ConLayout(l);
                var filas = _hoja.Filas;
                if (_esPdf) { var hoja = Lector.Leer(_muestra != null ? _muestra.Ruta : "", notas, propios); filas = hoja[0].Filas; }
                var det = BancoArchivo.Detectar(filas, _nombreMuestra, new[] { l });
                var res = BancoArchivo.Interpretar(filas, det.Mapa);
                bool coincide = det.Layout != null && string.Equals(det.Layout.Id, l.Id, StringComparison.OrdinalIgnoreCase);
                if (_esPdf) coincide = true;
                _estadoPanel.Children.Add(Tema.Chip(res.Movs.Count + " movimientos", res.Movs.Count > 0 ? Tono.Bueno : Tono.Malo));
                if (res.SaldoVerificado) { var c = Tema.Chip(res.RupturasSaldo == 0 ? "El saldo cuadra" : res.RupturasSaldo + " renglones donde el saldo no cuadra", res.RupturasSaldo == 0 ? Tono.Bueno : Tono.Aviso); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                if (res.Errores.Count > 0) { var c = Tema.Chip(res.Errores[0], Tono.Malo); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                if (res.Desde.HasValue) { var c = Tema.Chip(res.Desde.Value.ToString("dd/MM/yyyy") + " – " + res.Hasta.Value.ToString("dd/MM/yyyy"), Tono.Neutro); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                if (!coincide && problema == null) problema = "Este layout no reconoce el archivo de muestra";
                if (problema == null)
                {
                    var todos = BancoArchivo.Detectar(filas, _nombreMuestra, ConLayout(l));
                    if (!_esPdf && todos.Layout != null && !string.Equals(todos.Layout.Id, l.Id, StringComparison.OrdinalIgnoreCase))
                        _aviso.Text = "Con este reconocimiento el archivo seguiría leyéndose con «" + todos.Layout.Nombre + "». Agrega condiciones más específicas.";
                }
                var vista = res.Movs.Take(40).Select(m => new { Fecha = m.Fecha.ToString("dd/MM/yyyy"), Hora = m.Hora.HasValue ? m.Hora.Value.ToString(@"hh\:mm") : "", Descripcion = m.Descripcion, Cargo = m.Cargo != 0 ? Tema.Dinero(m.Cargo) : "", Abono = m.Abono != 0 ? Tema.Dinero(m.Abono) : "", Saldo = m.Saldo.HasValue ? Tema.Dinero(m.Saldo.Value) : "", Tipo = m.Tipo }).ToList();
                var dg = new DataGrid { ItemsSource = vista };
                Action<string, string, double, bool> col = (enc, prop, ancho, der) =>
                {
                    var c = new DataGridTextColumn { Header = enc, Binding = new System.Windows.Data.Binding(prop), Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star) };
                    if (der) { var st = new Style(typeof(TextBlock)); st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right)); c.ElementStyle = st; }
                    dg.Columns.Add(c);
                };
                col("Fecha", "Fecha", 84, false); col("Hora", "Hora", 52, false); col("Descripción", "Descripcion", 0, false); col("Cargo", "Cargo", 96, true); col("Abono", "Abono", 96, true); col("Saldo", "Saldo", 104, true); col("Tipo", "Tipo", 130, false);
                _vista.Content = dg;
            }
            catch (Exception ex) { _estadoPanel.Children.Add(Tema.Chip(ex.Message, Tono.Malo)); _vista.Content = null; problema = problema ?? "No se pudo leer con este layout"; }
            if (problema != null) _aviso.Text = problema;
            _valido = problema == null;
            _guardar.IsEnabled = _valido && _puedeGuardar;
        }

        // ---------- guardar ----------
        private void Guardar()
        {
            if (!_valido || !_puedeGuardar) return;
            var l = ConstruirLayout();
            // Prueba contra lo que ya esta en la bandeja: si algun archivo cambiaria de layout, se avisa antes de guardar.
            var cambios = new List<string>();
            var antes = _todos; var despues = ConLayout(l);
            foreach (var a in _ctx_Items())
            {
                try
                {
                    if (a.Hoja == null || a.Ruta.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    var d0 = BancoArchivo.Detectar(a.Hoja.Filas, a.Nombre, antes); var d1 = BancoArchivo.Detectar(a.Hoja.Filas, a.Nombre, despues);
                    string id0 = d0.Layout != null ? d0.Layout.Id : "", id1 = d1.Layout != null ? d1.Layout.Id : "";
                    if (id0 != id1 && !(a.Nombre == _nombreMuestra)) cambios.Add(a.Nombre + ": «" + (d0.Layout != null ? d0.Layout.Nombre : "sin layout") + "» pasaría a «" + (d1.Layout != null ? d1.Layout.Nombre : "sin layout") + "»");
                }
                catch { }
            }
            if (cambios.Count > 0 && MessageBox.Show(this, "Con este cambio, otros archivos de la bandeja se leerían con otro layout:\n\n" + string.Join("\n", cambios.Take(8)) + "\n\n¿Guardar de todos modos?", "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _ctx.Banco.GuardarLayout(l); DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private IEnumerable<AnalisisArchivo> _ctx_Items()
        {
            var v = Owner as VentanaPrincipal;
            return v == null ? new List<AnalisisArchivo>() : v.Bandeja.Archivos;
        }
    }
}
