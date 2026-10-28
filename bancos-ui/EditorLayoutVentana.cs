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
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.BancosUI
{
    // Ver o ajustar un layout. A la derecha siempre esta su definicion completa (como se reconoce, que columnas lee, que reglas tiene), con o sin archivo de
    // muestra; a la izquierda, la muestra y el resultado en vivo para comprobar que lee bien. Antes de guardar avisa si algun archivo de la bandeja cambiaria de layout.
    public class EditorLayoutVentana : Window
    {
        private static readonly string[][] Campos =
        {
            new[] { "", "—" }, new[] { "fecha", "Fecha" }, new[] { "hora", "Hora" }, new[] { "descripcion", "Descripción" }, new[] { "detalle", "Detalle (texto extra)" }, new[] { "referencia", "Referencia" },
            new[] { "cargo", "Cargo" }, new[] { "abono", "Abono" }, new[] { "importe", "Importe (con signo)" }, new[] { "signo", "Signo cargo/abono" }, new[] { "saldo", "Saldo" }, new[] { "cuenta", "Cuenta" }, new[] { "moneda", "Divisa" }
        };

        private static readonly object[][] Clases =
        {
            new object[] { "encabezado", false, "El encabezado trae" }, new object[] { "encabezado", true, "El encabezado empieza con" }, new object[] { "texto", false, "El texto del archivo trae" },
            new object[] { "archivo", false, "El nombre del archivo trae" }, new object[] { "pdf", false, "El PDF trae" }
        };

        private sealed class ColFila { public string Campo = "", Nombre = ""; }
        private sealed class CondFila { public string En = "encabezado"; public bool Empieza; public List<string> Valores = new List<string>(); }

        private readonly Contexto _ctx;
        private readonly Layout _original;
        private readonly bool _puedeGuardar, _esNuevo;
        private readonly List<Layout> _todos;
        private AnalisisArchivo _muestra;
        private HojaArchivo _hoja;
        private string _nombreMuestra = "", _rutaMuestra = "";
        private bool _esPdf, _construyendo, _valido;
        private int _filaEnc = -1;                          // fila de encabezado de la muestra (0 = primera); -1 = sin encabezado
        private BancoMapa _deducido;                        // lo que el motor lee solo de la muestra
        private List<ColFila> _cols = new List<ColFila>();
        private bool _automatico;                           // el layout no dice columnas: el programa las reconoce por sus titulos
        private List<CondFila> _conds = new List<CondFila>();
        private List<List<Condicion>> _otros = new List<List<Condicion>>();
        private List<ReglaTipo> _tipos = new List<ReglaTipo>();
        private List<ReglaExtraer> _extraer = new List<ReglaExtraer>();

        private readonly Grid _tabla = new Grid();
        private readonly TextBox _nombre = new TextBox(), _zFila = new TextBox(), _zCargo = new TextBox(), _zAbono = new TextBox(), _zSaldo = new TextBox(), _zFechaX = new TextBox(), _zDescX = new TextBox(), _zAlcance = new TextBox();
        private readonly ComboBox _banco = new ComboBox { IsEditable = true }, _fecha = new ComboBox(), _estrategia = new ComboBox();
        private readonly CheckBox _negCargo = new CheckBox { Content = "Un importe negativo es un cargo", IsChecked = true }, _divisa = new CheckBox { Content = "Cada divisa del archivo es una cuenta distinta" }, _sinEnc = new CheckBox { Content = "El archivo no trae encabezado" };
        private readonly StackPanel _panelConds = new StackPanel(), _panelCols = new StackPanel(), _panelTipos = new StackPanel(), _panelExtraer = new StackPanel(), _estadoPanel = new StackPanel();
        private Border _cardCols, _cardOpc, _cardPdf; private readonly Border _cardZonas = new Border();
        private readonly ContentControl _vista = new ContentControl();
        private readonly TextBlock _aviso = Tema.Txt("", 12, false, Tema.Pincel("#7A4E00"), TextWrapping.Wrap);
        private readonly TextBlock _coincide = Tema.Txt("", 12, false, Tema.Texto2);
        private Button _guardar;
        private ComboBox _claseNueva; private TextBox _valorNuevo;
        private Expander _expTipos, _expExtraer;

        public EditorLayoutVentana(Contexto ctx, Layout layout, AnalisisArchivo muestra, bool puedeGuardar, List<Layout> todos)
        {
            _ctx = ctx; _puedeGuardar = puedeGuardar; _todos = todos; _muestra = muestra; _esNuevo = layout == null || string.IsNullOrEmpty(layout.Id);
            _original = layout != null ? Layout.DeDatos(layout.ADatos()) : new Layout { Origen = "empresa", Nombre = "", Reconocer = new List<List<Condicion>>() };
            Title = _esNuevo ? "Nuevo layout" : "Layout · " + _original.Nombre;
            Width = 1240; Height = 820; MinWidth = 1000; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Tema.Pagina; FontFamily = new FontFamily("Segoe UI"); Foreground = Tema.Texto;
            CargarDelLayout();
            Construir();
            if (_muestra != null && _muestra.Hoja != null) UsarMuestra(_muestra.Hoja, _muestra.Nombre, _muestra.Ruta);
            else { PintarSinMuestra(); Refrescar(); }
        }

        // ---------- del layout al editor ----------
        private void CargarDelLayout()
        {
            var l = _original;
            _esPdf = string.Equals(l.Formato, "pdf", StringComparison.OrdinalIgnoreCase);
            _conds = new List<CondFila>();
            if (l.Reconocer != null && l.Reconocer.Count > 0)
                foreach (var c in l.Reconocer[0])
                {
                    bool emp = c.Empieza != null && c.Empieza.Count > 0;
                    _conds.Add(new CondFila { En = (c.En ?? "encabezado").ToLowerInvariant(), Empieza = emp, Valores = new List<string>(emp ? c.Empieza : (c.Contiene ?? new List<string>())) });
                }
            _otros = (l.Reconocer ?? new List<List<Condicion>>()).Skip(1).ToList();
            _cols = new List<ColFila>();
            if (l.Columnas != null) foreach (var kv in l.Columnas) foreach (var n in kv.Value) _cols.Add(new ColFila { Campo = kv.Key.ToLowerInvariant(), Nombre = n });
            _automatico = !_esPdf && _cols.Count == 0 && !_esNuevo;
            _tipos = (l.Tipos ?? new List<ReglaTipo>()).Select(x => new ReglaTipo { Patron = x.Patron, Tipo = x.Tipo, Cuando = x.Cuando }).ToList();
            _extraer = (l.Extraer ?? new List<ReglaExtraer>()).Select(x => new ReglaExtraer { Campo = x.Campo, Patron = x.Patron }).ToList();
        }

        // ---------- estructura ----------
        private static Border Tarjeta(string titulo, UIElement contenido, string ayuda = null)
        {
            var p = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            p.Children.Add(Tema.Txt(titulo, 13, true));
            if (!string.IsNullOrEmpty(ayuda)) { var a = Tema.Txt(ayuda, 12, false, Tema.Texto2, TextWrapping.Wrap); a.Margin = new Thickness(0, 2, 0, 0); p.Children.Add(a); }
            var c = (FrameworkElement)contenido; c.Margin = new Thickness(0, 10, 0, 0); p.Children.Add(c);
            return new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 12), Child = p };
        }

        private static TextBlock Etiqueta(string t) { var x = Tema.Txt(t, 12, false, Tema.Texto2); x.Margin = new Thickness(0, 8, 0, 4); return x; }
        private static Button Izq(Button b) { b.HorizontalAlignment = HorizontalAlignment.Left; return b; }

        private void Construir()
        {
            var raiz = new Grid { Margin = new Thickness(18) };
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- izquierda: muestra ----
            var izq = new DockPanel();
            var barra = new DockPanel { Margin = new Thickness(16, 12, 16, 6) };
            var abrir = Tema.Boton("Abrir otro archivo…", (s, e) => AbrirMuestra(), false, true); DockPanel.SetDock(abrir, Dock.Right); barra.Children.Add(abrir);
            barra.Children.Add(Tema.Txt("Archivo de muestra", 13, true));
            DockPanel.SetDock(barra, Dock.Top); izq.Children.Add(barra);
            _sinEnc.Margin = new Thickness(16, 0, 16, 8); _sinEnc.Click += (s, e) => { if (_construyendo) return; _filaEnc = _sinEnc.IsChecked == true ? -1 : 0; ReconstruirTabla(); Recalcular(); };
            DockPanel.SetDock(_sinEnc, Dock.Top); izq.Children.Add(_sinEnc);
            izq.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tabla, Margin = new Thickness(16, 0, 16, 12) });
            var cajaIzq = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = izq };
            Grid.SetColumn(cajaIzq, 0); Grid.SetRow(cajaIzq, 0); raiz.Children.Add(cajaIzq);

            // ---- derecha: definicion ----
            var der = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            if (!_puedeGuardar) der.Children.Add(new Border { Background = Tema.Pincel("#FFF1D6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 12), Child = Tema.Txt("Solo lectura: no tienes permiso para guardar layouts.", 12, false, Tema.Pincel("#7A4E00")) });
            else if (!_esNuevo && _original.Origen != "empresa")
                der.Children.Add(new Border { Background = Tema.AcentoFondo, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 12), Child = Tema.Txt("Este layout es de fábrica. Al guardar se crea tu versión; el de fábrica no se pierde.", 12, false, Tema.Pincel("#1F55B5"), TextWrapping.Wrap) });

            // datos
            var datos = new StackPanel();
            datos.Children.Add(Etiqueta("Nombre")); _nombre.Text = _original.Nombre; _nombre.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; datos.Children.Add(_nombre);
            datos.Children.Add(Etiqueta("Banco"));
            foreach (var b in BancoCatalogo.Bancos.Where(x => x != "Otro")) _banco.Items.Add(b);
            _banco.Text = _original.Banco; _banco.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => { if (!_construyendo) Recalcular(); }));
            datos.Children.Add(_banco);
            var tipo = Tema.Txt(_esPdf ? "Formato: PDF con texto" : "Formato: tabla (CSV, TXT o Excel)", 12, false, Tema.Texto2); tipo.Margin = new Thickness(0, 8, 0, 0); datos.Children.Add(tipo);
            der.Children.Add(Tarjeta("Datos", datos));

            // como se reconoce
            var reco = new StackPanel(); reco.Children.Add(_panelConds);
            var nueva = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var agregar = Tema.Boton("Agregar", (s, e) => AgregarCondicion()); DockPanel.SetDock(agregar, Dock.Right); nueva.Children.Add(agregar);
            _claseNueva = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 8, 0) }; foreach (var c in Clases) _claseNueva.Items.Add((string)c[2]); _claseNueva.SelectedIndex = 0; DockPanel.SetDock(_claseNueva, Dock.Left); nueva.Children.Add(_claseNueva);
            _valorNuevo = new TextBox { Margin = new Thickness(0, 0, 8, 0), ToolTip = "Separa con comas las opciones: basta con que aparezca una" }; nueva.Children.Add(_valorNuevo);
            reco.Children.Add(nueva);
            _coincide.Margin = new Thickness(0, 8, 0, 0); reco.Children.Add(_coincide);
            der.Children.Add(Tarjeta("Cómo se reconoce este formato", reco, "Todo lo de la primera lista debe cumplirse. Si alguna de las formas «O bien» se cumple completa, también se reconoce."));

            // columnas
            var cols = new StackPanel(); cols.Children.Add(_panelCols);
            _cardCols = Tarjeta("Columnas que lee", cols, "Qué columna del archivo es cada dato. Usa el título de la columna o su posición (#3 es la tercera)."); der.Children.Add(_cardCols);

            // opciones
            var opc = new StackPanel();
            opc.Children.Add(Etiqueta("Formato de la fecha")); _fecha.Items.Add("Día/mes (México)"); _fecha.Items.Add("Mes/día"); _fecha.SelectedIndex = _original.Ajustes != null && _original.Ajustes.FormatoFecha == "mdy" ? 1 : 0;
            _fecha.SelectionChanged += (s, e) => { if (!_construyendo) Recalcular(); }; opc.Children.Add(_fecha);
            _negCargo.IsChecked = _original.Ajustes == null || !_original.Ajustes.ImporteNegativoEsCargo.HasValue || _original.Ajustes.ImporteNegativoEsCargo.Value;
            _negCargo.Margin = new Thickness(0, 10, 0, 0); _negCargo.Click += (s, e) => Recalcular(); opc.Children.Add(_negCargo);
            _divisa.IsChecked = _original.Ajustes != null && _original.Ajustes.CuentaDesdeDivisa; _divisa.Margin = new Thickness(0, 6, 0, 0); _divisa.Click += (s, e) => Recalcular(); opc.Children.Add(_divisa);
            _cardOpc = Tarjeta("Cómo se lee", opc); der.Children.Add(_cardOpc);

            // pdf
            var pdf = new StackPanel();
            pdf.Children.Add(Etiqueta("Cómo leer el PDF"));
            _estrategia.Items.Add("Impresión de BBVANet"); _estrategia.Items.Add("Impresión del export de BBVA"); _estrategia.Items.Add("Estado de cuenta oficial de BBVA"); _estrategia.Items.Add("Otro banco (por títulos de columna)");
            var po = _original.Pdf ?? new PdfOpciones();
            _estrategia.SelectedIndex = po.Estrategia == "export" ? 1 : po.Estrategia == "estadocuenta" ? 2 : po.Estrategia == "zonas" ? 3 : (_esPdf && _original.Pdf == null ? 3 : 0);
            _estrategia.SelectionChanged += (s, e) => { if (_construyendo) return; _cardZonas.Visibility = _estrategia.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed; Recalcular(); };
            pdf.Children.Add(_estrategia);
            var zonas = new StackPanel();
            Func<string, TextBox, string, UIElement> campo = (t, tb, ayuda) => { tb.TextChanged += (s, e) => { if (!_construyendo) Recalcular(); }; tb.ToolTip = ayuda; var g = new StackPanel { Margin = new Thickness(0, 0, 8, 4) }; g.Children.Add(Etiqueta(t)); g.Children.Add(tb); return g; };
            _zCargo.Text = po.EncCargo; _zAbono.Text = po.EncAbono; _zSaldo.Text = po.EncSaldo; _zFila.Text = po.FilaInicia;
            _zFechaX.Text = po.FechaXMax.ToString(System.Globalization.CultureInfo.InvariantCulture); _zDescX.Text = po.DescXMin.ToString(System.Globalization.CultureInfo.InvariantCulture); _zAlcance.Text = po.Alcance.ToString(System.Globalization.CultureInfo.InvariantCulture);
            zonas.Children.Add(campo("Título de la columna de cargos", _zCargo, "Tal como sale en el PDF")); zonas.Children.Add(campo("Título de la columna de abonos", _zAbono, "Tal como sale en el PDF")); zonas.Children.Add(campo("Título de la columna de saldo", _zSaldo, "Tal como sale en el PDF"));
            zonas.Children.Add(campo("La fecha del renglón se reconoce con (patrón)", _zFila, @"Expresión regular. Ejemplos: ^\d{2}-\d{2}$ para 25-10, ^\d{2}/\d{2}/\d{4}$ para 25/10/2026"));
            zonas.Children.Add(campo("La fecha está a la izquierda, hasta la posición", _zFechaX, "Puntos desde el borde izquierdo de la página")); zonas.Children.Add(campo("La descripción empieza en la posición", _zDescX, "Puntos desde el borde izquierdo"));
            zonas.Children.Add(campo("La descripción se busca hasta (puntos arriba o abajo)", _zAlcance, "Qué tan lejos del renglón de la fecha puede estar su descripción"));
            _cardZonas.Child = zonas; _cardZonas.Visibility = _estrategia.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed; pdf.Children.Add(_cardZonas);
            _cardPdf = Tarjeta("Cómo se lee el PDF", pdf); der.Children.Add(_cardPdf);

            // reglas
            _expTipos = new Expander { Header = "Tipo de movimiento (reglas propias)", Margin = new Thickness(0, 0, 0, 4), IsExpanded = _tipos.Count > 0, Foreground = Tema.Texto };
            var pt = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; pt.Children.Add(Tema.Txt("Si el movimiento trae este texto, se le pone este tipo. Ganan a las reglas generales.", 12, false, Tema.Texto2, TextWrapping.Wrap)); pt.Children.Add(_panelTipos);
            pt.Children.Add(Izq(Tema.Boton("+ Regla de tipo", (s, e) => { _tipos.Add(new ReglaTipo()); PintarReglas(); }, false, true))); _expTipos.Content = pt;
            _expExtraer = new Expander { Header = "Datos que se sacan del concepto", Margin = new Thickness(0, 0, 0, 4), IsExpanded = _extraer.Count > 0, Foreground = Tema.Texto };
            var pe = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; pe.Children.Add(Tema.Txt("Saca un dato del texto del movimiento, por ejemplo la referencia o el RFC. Si el patrón tiene paréntesis, se usa lo de adentro.", 12, false, Tema.Texto2, TextWrapping.Wrap)); pe.Children.Add(_panelExtraer);
            pe.Children.Add(Izq(Tema.Boton("+ Dato", (s, e) => { _extraer.Add(new ReglaExtraer { Campo = "referencia" }); PintarReglas(); }, false, true))); _expExtraer.Content = pe;
            var reglas = new StackPanel(); reglas.Children.Add(_expTipos); reglas.Children.Add(_expExtraer);
            der.Children.Add(new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 12), Child = reglas });

            var cajaDer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = der };
            Grid.SetColumn(cajaDer, 2); Grid.SetRow(cajaDer, 0); Grid.SetRowSpan(cajaDer, 3); raiz.Children.Add(cajaDer);

            // ---- abajo: resultado en vivo ----
            var abajo = new DockPanel();
            _estadoPanel.Orientation = Orientation.Horizontal; _estadoPanel.Margin = new Thickness(16, 10, 16, 6);
            DockPanel.SetDock(_estadoPanel, Dock.Top); abajo.Children.Add(_estadoPanel);
            _aviso.Margin = new Thickness(16, 0, 16, 6); DockPanel.SetDock(_aviso, Dock.Top); abajo.Children.Add(_aviso);
            abajo.Children.Add(_vista);
            var cajaAbajo = new Border { Background = Tema.Superficie, BorderBrush = Tema.Linea, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = abajo };
            Grid.SetColumn(cajaAbajo, 0); Grid.SetRow(cajaAbajo, 2); raiz.Children.Add(cajaAbajo);

            var pie = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _guardar = Tema.Boton("Guardar", (s, e) => Guardar(), true); DockPanel.SetDock(_guardar, Dock.Right); pie.Children.Add(_guardar);
            var cancelar = Tema.Boton("Cerrar", (s, e) => { DialogResult = false; }); cancelar.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(cancelar, Dock.Right); pie.Children.Add(cancelar);
            pie.Children.Add(new TextBlock());
            Grid.SetRow(pie, 3); Grid.SetColumnSpan(pie, 3); raiz.Children.Add(pie);
            Content = raiz;
            AplicarFormato();
        }

        private static void Estilar(Border b)
        {
            b.Background = Tema.Superficie; b.BorderBrush = Tema.Linea; b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(12); b.Margin = new Thickness(0, 0, 0, 12);
        }

        private void AplicarFormato()
        {
            _cardCols.Visibility = _esPdf ? Visibility.Collapsed : Visibility.Visible; _cardOpc.Visibility = _esPdf ? Visibility.Collapsed : Visibility.Visible; _cardPdf.Visibility = _esPdf ? Visibility.Visible : Visibility.Collapsed;
            _sinEnc.Visibility = _esPdf ? Visibility.Collapsed : Visibility.Visible;
            var kinds = _esPdf ? Clases.Where(c => (string)c[0] != "encabezado").ToArray() : Clases.Where(c => (string)c[0] != "pdf").ToArray();
            _claseNueva.Items.Clear(); foreach (var c in kinds) _claseNueva.Items.Add((string)c[2]); _claseNueva.SelectedIndex = 0;
        }

        // ---------- condiciones de reconocimiento ----------
        private static string Frase(CondFila c)
        {
            string clase = (string)Clases.First(k => (string)k[0] == c.En && (bool)k[1] == c.Empieza)[2];
            return clase + " " + string.Join(" o ", c.Valores.Select(v => "«" + v + "»"));
        }

        private void PintarCondiciones()
        {
            _panelConds.Children.Clear();
            if (_conds.Count == 0) _panelConds.Children.Add(Tema.Txt("Todavía no tiene condiciones: agrega al menos una.", 12, false, Tema.Pincel("#7A4E00")));
            foreach (var c in _conds.ToList())
            {
                var cond = c;
                var fila = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var quitar = Tema.Boton("×", (s, e) => { _conds.Remove(cond); PintarCondiciones(); Recalcular(); }, false, true); DockPanel.SetDock(quitar, Dock.Right); fila.Children.Add(quitar);
                fila.Children.Add(new Border { Background = Tema.Pincel("#F1F3F6"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Child = Tema.Txt(Frase(cond), 12, false, Tema.Texto, TextWrapping.Wrap) });
                _panelConds.Children.Add(fila);
            }
            foreach (var camino in _otros.ToList())
            {
                var otro = camino;
                var fila = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var quitar = Tema.Boton("×", (s, e) => { _otros.Remove(otro); PintarCondiciones(); Recalcular(); }, false, true); DockPanel.SetDock(quitar, Dock.Right); fila.Children.Add(quitar);
                var frases = otro.Select(c => new CondFila { En = (c.En ?? "encabezado").ToLowerInvariant(), Empieza = c.Empieza != null && c.Empieza.Count > 0, Valores = new List<string>(c.Empieza != null && c.Empieza.Count > 0 ? c.Empieza : (c.Contiene ?? new List<string>())) }).Select(Frase);
                var caja = new StackPanel(); caja.Children.Add(Tema.Txt("O bien", 11, true, Tema.Texto2)); caja.Children.Add(Tema.Txt(string.Join("  y  ", frases), 12, false, Tema.Texto, TextWrapping.Wrap));
                fila.Children.Add(new Border { Background = Tema.Pincel("#F1F3F6"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Child = caja });
                _panelConds.Children.Add(fila);
            }
        }

        private void AgregarCondicion()
        {
            var valores = (_valorNuevo.Text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (valores.Count == 0) return;
            string etiqueta = (string)_claseNueva.SelectedItem; var clase = Clases.First(k => (string)k[2] == etiqueta);
            _conds.Add(new CondFila { En = (string)clase[0], Empieza = (bool)clase[1], Valores = valores });
            _valorNuevo.Text = ""; PintarCondiciones(); Recalcular();
        }

        // ---------- columnas ----------
        private static string Norm(string s) { return BancoTexto.Norm(BancoTexto.Limpiar(s ?? "")); }

        private void PintarColumnas()
        {
            _panelCols.Children.Clear();
            if (_automatico)
            {
                _panelCols.Children.Add(Tema.Txt("Este layout no fija las columnas: el programa las reconoce por sus títulos (Fecha, Descripción, Cargo, Abono, Importe, Saldo…).", 12, false, Tema.Texto2, TextWrapping.Wrap));
                if (_deducido != null && _hoja != null)
                {
                    _panelCols.Children.Add(Etiqueta("Con el archivo de muestra lee:"));
                    foreach (var d in DeducidasComoFilas()) _panelCols.Children.Add(Tema.Txt(NombreCampo(d.Campo) + "  ←  " + (d.Nombre.StartsWith("#") ? "columna " + d.Nombre.Substring(1) : "«" + d.Nombre + "»"), 12));
                }
                var definir = Izq(Tema.Boton("Definir las columnas yo", (s, e) => { Materializar(); Refrescar(); }, false, true));
                definir.IsEnabled = _hoja != null; definir.ToolTip = _hoja == null ? "Abre un archivo de muestra para poder definirlas" : null; definir.Margin = new Thickness(0, 6, 0, 0);
                _panelCols.Children.Add(definir);
                return;
            }
            foreach (var c in _cols.ToList())
            {
                var col = c;
                var fila = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var quitar = Tema.Boton("×", (s, e) => { _cols.Remove(col); Refrescar(); }, false, true); DockPanel.SetDock(quitar, Dock.Right); fila.Children.Add(quitar);
                var campo = new ComboBox { Width = 170 }; foreach (var k in Campos) campo.Items.Add(k[1]);
                campo.SelectedIndex = Math.Max(0, Array.FindIndex(Campos, k => k[0] == col.Campo));
                campo.SelectionChanged += (s, e) => { if (_construyendo) return; col.Campo = Campos[campo.SelectedIndex][0]; ReconstruirTabla(); Recalcular(); };
                DockPanel.SetDock(campo, Dock.Left); fila.Children.Add(campo);
                var nombre = new TextBox { Text = col.Nombre, Margin = new Thickness(8, 0, 6, 0), ToolTip = "Título de la columna, o #3 para la tercera" };
                nombre.TextChanged += (s, e) => { col.Nombre = nombre.Text.Trim(); if (!_construyendo) { ReconstruirTabla(); Recalcular(); } };
                fila.Children.Add(nombre); _panelCols.Children.Add(fila);
            }
            _panelCols.Children.Add(Izq(Tema.Boton("+ Columna", (s, e) => { _cols.Add(new ColFila()); Refrescar(); }, false, true)));
        }

        private static string NombreCampo(string campo) { var k = Campos.FirstOrDefault(x => x[0] == campo); return k != null ? k[1] : campo; }

        private List<ColFila> DeducidasComoFilas()
        {
            var lista = new List<ColFila>();
            if (_deducido == null || _hoja == null) return lista;
            var celdas = _filaEnc >= 0 && _filaEnc < _hoja.Filas.Count ? _hoja.Filas[_filaEnc] : new string[0];
            Action<int, string> pon = (j, campo) =>
            {
                if (j < 0) return;
                string nom = j < celdas.Length ? BancoTexto.Limpiar(celdas[j]) : "";
                bool unico = nom.Length > 0 && celdas.Count(c => Norm(c) == Norm(nom)) == 1;
                lista.Add(new ColFila { Campo = campo, Nombre = _filaEnc >= 0 && unico ? nom : "#" + (j + 1) });
            };
            var m = _deducido;
            pon(m.Fecha, "fecha"); pon(m.Hora, "hora"); pon(m.Descripcion, "descripcion"); foreach (var d in m.Detalle) pon(d, "detalle"); pon(m.Referencia, "referencia");
            pon(m.Cargo, "cargo"); pon(m.Abono, "abono"); pon(m.Importe, "importe"); pon(m.Signo, "signo"); pon(m.Saldo, "saldo"); pon(m.Cuenta, "cuenta"); pon(m.Moneda, "moneda");
            return lista;
        }

        private void Materializar() { _cols = DeducidasComoFilas(); _automatico = false; }

        // ---------- muestra ----------
        private void AbrirMuestra()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Archivo de muestra", Filter = "Estados de cuenta y movimientos|*.csv;*.txt;*.xlsx;*.xlsm;*.xls;*.pdf|Todos|*.*" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                var notas = new List<string>();
                var hoja = Lector.Leer(d.FileName, notas, _todos)[0];
                UsarMuestra(hoja, Path.GetFileName(d.FileName), d.FileName);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void PintarSinMuestra()
        {
            _tabla.Children.Clear(); _tabla.RowDefinitions.Clear(); _tabla.ColumnDefinitions.Clear();
            var p = new StackPanel { Margin = new Thickness(0, 70, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(Tema.Txt("Sin archivo de muestra", 14, true));
            var t = Tema.Txt("No hace falta para ver o editar la definición de la derecha. Abre uno si quieres comprobar cómo lee este layout.", 12, false, Tema.Texto2, TextWrapping.Wrap); t.Margin = new Thickness(0, 4, 0, 12); t.TextAlignment = TextAlignment.Center; t.MaxWidth = 360; p.Children.Add(t);
            p.Children.Add(Tema.Boton("Elegir archivo…", (s, e) => AbrirMuestra()));
            _tabla.Children.Add(p); _sinEnc.Visibility = Visibility.Collapsed;
        }

        private void UsarMuestra(HojaArchivo hoja, string nombre, string ruta)
        {
            _hoja = hoja; _nombreMuestra = nombre; _rutaMuestra = ruta ?? "";
            bool pdf = hoja.LayoutPdf != null || nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            _construyendo = true;
            try
            {
                if (_esNuevo && _cols.Count == 0 && _conds.Count == 0) { _esPdf = pdf; AplicarFormato(); }
                var propio = LayoutActual();
                BancoDeteccion det = null;
                try { det = BancoArchivo.Detectar(hoja.Filas, nombre, new[] { propio }); } catch { }
                _deducido = det != null ? det.Mapa : null;
                int fila = _deducido != null ? _deducido.FilaEncabezado : 0;
                if (!_esPdf && _cols.Count > 0) { int f = BancoArchivo.FilaEncabezadoDe(propio, hoja.Filas); if (f != int.MinValue) fila = f; }
                _filaEnc = fila;
                _sinEnc.IsChecked = _filaEnc < 0; _sinEnc.Visibility = _esPdf ? Visibility.Collapsed : Visibility.Visible;
                if (_esNuevo && !_esPdf && _cols.Count == 0) Materializar();
                if (_esNuevo && _conds.Count == 0 && !_esPdf) DefaultCondiciones();
                if (_esNuevo && _esPdf && _conds.Count == 0) { var t = BancoPdfTextoClave(ruta); if (t.Length > 0) _conds.Add(new CondFila { En = "pdf", Valores = new List<string> { t } }); }
                if (_esNuevo && (_banco.Text ?? "").Length == 0 && det != null) _banco.Text = det.Banco;
            }
            finally { _construyendo = false; }
            Refrescar();
        }

        // PDF nuevo: se propone reconocerlo por una linea distintiva del texto (la primera con letras de la primera pagina).
        private static string BancoPdfTextoClave(string ruta)
        {
            try
            {
                string t = BancoPdf.TextoInicial(ruta);
                var palabras = t.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 5 && w.All(char.IsLetter)).Take(2).ToList();
                return string.Join(" ", palabras);
            }
            catch { return ""; }
        }

        // Layout nuevo: se propone reconocerlo por los titulos de las columnas que se leen.
        private void DefaultCondiciones()
        {
            if (_hoja == null || _filaEnc < 0 || _filaEnc >= _hoja.Filas.Count) return;
            foreach (var c in _cols.Where(x => !x.Nombre.StartsWith("#") && x.Nombre.Length > 0).Select(x => x.Nombre).Distinct())
                _conds.Add(new CondFila { En = "encabezado", Valores = new List<string> { c } });
        }

        private void Refrescar()
        {
            _construyendo = true;
            try { PintarCondiciones(); PintarColumnas(); PintarReglas(); ReconstruirTabla(); }
            finally { _construyendo = false; }
            Recalcular();
        }

        // ---------- tabla de muestra ----------
        private string CampoDeColumna(int j)
        {
            var celdas = _filaEnc >= 0 && _hoja != null && _filaEnc < _hoja.Filas.Count ? _hoja.Filas[_filaEnc] : new string[0];
            var fuente = _automatico ? DeducidasComoFilas() : _cols;
            foreach (var c in fuente)
            {
                if (string.IsNullOrEmpty(c.Campo)) continue;
                bool es = c.Nombre.StartsWith("#") ? (int.TryParse(c.Nombre.Substring(1), out int n) && n - 1 == j) : (j < celdas.Length && c.Nombre.Length > 0 && Norm(c.Nombre) == Norm(celdas[j]));
                if (es) return c.Campo;
            }
            return "";
        }

        private void ReconstruirTabla()
        {
            _tabla.Children.Clear(); _tabla.RowDefinitions.Clear(); _tabla.ColumnDefinitions.Clear();
            if (_hoja == null) { PintarSinMuestra(); return; }
            int ncol = Math.Min(14, _hoja.Filas.Take(60).Max(f => f.Length)), nfil = Math.Min(_hoja.Filas.Count, 40);
            _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            for (int j = 0; j < ncol; j++) _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(j == 1 || j == 2 ? 190 : 122) });
            _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < nfil; i++) _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var campoDe = new string[ncol];
            for (int j = 0; j < ncol; j++) campoDe[j] = CampoDeColumna(j);
            for (int j = 0; j < ncol; j++)
            {
                int col = j;
                var combo = new ComboBox { Margin = new Thickness(2, 0, 2, 6), FontSize = 12, IsEnabled = !_esPdf };
                foreach (var c in Campos) combo.Items.Add(c[1]);
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(Campos, c => c[0] == campoDe[col]));
                if (!string.IsNullOrEmpty(campoDe[col])) combo.Background = Tema.AcentoFondo;
                combo.SelectionChanged += (s, e) =>
                {
                    if (_construyendo) return;
                    string nuevo = Campos[combo.SelectedIndex][0];
                    if (_automatico) Materializar();
                    QuitarColumna(col);
                    if (nuevo.Length > 0) _cols.Add(new ColFila { Campo = nuevo, Nombre = NombreParaColumna(col) });
                    Refrescar();
                };
                Grid.SetRow(combo, 0); Grid.SetColumn(combo, j + 1); _tabla.Children.Add(combo);
            }
            for (int i = 0; i < nfil; i++)
            {
                int fila = i;
                bool esEnc = i == _filaEnc, antes = _filaEnc >= 0 && i < _filaEnc;
                var num = new Button { Content = (i + 1).ToString(), FontSize = 11, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Marcar como fila de encabezado", IsEnabled = !_esPdf };
                num.Style = (Style)Application.Current.Resources["BotonTexto"];
                num.Foreground = esEnc ? Tema.Acento : Tema.Texto2; num.FontWeight = esEnc ? FontWeights.Bold : FontWeights.Normal;
                num.Click += (s, e) => { _filaEnc = fila; _construyendo = true; try { _sinEnc.IsChecked = false; } finally { _construyendo = false; } Refrescar(); };
                Grid.SetRow(num, i + 1); Grid.SetColumn(num, 0); _tabla.Children.Add(num);
                for (int j = 0; j < ncol; j++)
                {
                    string t = j < _hoja.Filas[i].Length ? _hoja.Filas[i][j] : "";
                    var celda = new Border
                    {
                        BorderBrush = Tema.Linea, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 4, 6, 4),
                        Background = esEnc ? Tema.Pincel("#EDEFF3") : (!string.IsNullOrEmpty(campoDe[j]) && !antes ? Tema.Pincel("#F5F9FF") : Brushes.Transparent),
                        Child = new TextBlock { Text = t, FontSize = 12, FontWeight = esEnc ? FontWeights.SemiBold : FontWeights.Normal, Foreground = antes ? Tema.Pincel("#9AA3B0") : Tema.Texto, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t }
                    };
                    Grid.SetRow(celda, i + 1); Grid.SetColumn(celda, j + 1); _tabla.Children.Add(celda);
                }
            }
        }

        private void QuitarColumna(int j)
        {
            var celdas = _filaEnc >= 0 && _hoja != null && _filaEnc < _hoja.Filas.Count ? _hoja.Filas[_filaEnc] : new string[0];
            _cols.RemoveAll(c => c.Nombre.StartsWith("#") ? (int.TryParse(c.Nombre.Substring(1), out int n) && n - 1 == j) : (j < celdas.Length && c.Nombre.Length > 0 && Norm(c.Nombre) == Norm(celdas[j])));
        }

        private string NombreParaColumna(int j)
        {
            var celdas = _filaEnc >= 0 && _hoja != null && _filaEnc < _hoja.Filas.Count ? _hoja.Filas[_filaEnc] : new string[0];
            string nom = j < celdas.Length ? BancoTexto.Limpiar(celdas[j]) : "";
            bool unico = nom.Length > 0 && celdas.Count(c => Norm(c) == Norm(nom)) == 1;
            return _filaEnc >= 0 && unico ? nom : "#" + (j + 1);
        }

        // ---------- reglas ----------
        private static bool PatronValido(string p)
        {
            if (string.IsNullOrEmpty(p)) return true;
            try { new Regex(p); return true; } catch (ArgumentException) { return false; }
        }

        private void PintarReglas()
        {
            _panelTipos.Children.Clear(); _panelExtraer.Children.Clear();
            foreach (var r in _tipos.ToList())
            {
                var regla = r;
                var fila = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var quitar = Tema.Boton("×", (s, e) => { _tipos.Remove(regla); PintarReglas(); Recalcular(); }, false, true); DockPanel.SetDock(quitar, Dock.Right); fila.Children.Add(quitar);
                var cuando = new ComboBox { Width = 78 }; cuando.Items.Add("ambos"); cuando.Items.Add("cargo"); cuando.Items.Add("abono"); cuando.SelectedItem = string.IsNullOrEmpty(regla.Cuando) ? "ambos" : regla.Cuando;
                cuando.SelectionChanged += (s, e) => { regla.Cuando = (string)cuando.SelectedItem; Recalcular(); }; DockPanel.SetDock(cuando, Dock.Right); fila.Children.Add(cuando);
                var tipo = new TextBox { Text = regla.Tipo, Width = 120, Margin = new Thickness(6, 0, 6, 0), ToolTip = "Tipo que se le pone al movimiento" }; tipo.TextChanged += (s, e) => { regla.Tipo = tipo.Text; if (!_construyendo) Recalcular(); }; DockPanel.SetDock(tipo, Dock.Right); fila.Children.Add(tipo);
                var patron = new TextBox { Text = regla.Patron, ToolTip = "Texto o patrón (sin acentos ni mayúsculas) que debe traer el movimiento" }; patron.TextChanged += (s, e) => { regla.Patron = patron.Text; patron.BorderBrush = PatronValido(patron.Text) ? Tema.Pincel("#CDD2DA") : Brushes.IndianRed; if (!_construyendo) Recalcular(); };
                fila.Children.Add(patron); _panelTipos.Children.Add(fila);
            }
            foreach (var r in _extraer.ToList())
            {
                var regla = r;
                var fila = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var quitar = Tema.Boton("×", (s, e) => { _extraer.Remove(regla); PintarReglas(); Recalcular(); }, false, true); DockPanel.SetDock(quitar, Dock.Right); fila.Children.Add(quitar);
                var campo = new ComboBox { Width = 120 };
                foreach (var c in new[] { "referencia", "claveRastreo", "rfc", "clabe", "nombre", "banco" }) campo.Items.Add(c);
                campo.SelectedItem = regla.Campo; campo.SelectionChanged += (s, e) => { regla.Campo = (string)campo.SelectedItem; Recalcular(); };
                DockPanel.SetDock(campo, Dock.Left); fila.Children.Add(campo);
                var patron = new TextBox { Text = regla.Patron, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Patrón: se usa lo que quede entre paréntesis" }; patron.TextChanged += (s, e) => { regla.Patron = patron.Text; patron.BorderBrush = PatronValido(patron.Text) ? Tema.Pincel("#CDD2DA") : Brushes.IndianRed; if (!_construyendo) Recalcular(); };
                fila.Children.Add(patron); _panelExtraer.Children.Add(fila);
            }
        }

        // ---------- del editor al layout ----------
        private static string Slug(string s)
        {
            string n = Regex.Replace(BancoTexto.Norm(s ?? ""), @"[^a-z0-9]+", "-").Trim('-');
            return n.Length == 0 ? "layout" : n;
        }

        private Layout LayoutActual()
        {
            var l = Layout.DeDatos(_original.ADatos());
            l.Nombre = _nombre.Text.Trim(); l.Banco = (_banco.Text ?? "").Trim();
            if (string.IsNullOrEmpty(l.Id)) l.Id = Slug(l.Banco + " " + l.Nombre);
            l.Origen = "empresa"; l.Formato = _esPdf ? "pdf" : "tabla";
            var camino = _conds.Where(c => c.Valores.Count > 0).Select(c => c.Empieza ? new Condicion { En = c.En, Empieza = new List<string>(c.Valores) } : new Condicion { En = c.En, Contiene = new List<string>(c.Valores) }).ToList();
            l.Reconocer = new List<List<Condicion>>(); if (camino.Count > 0) l.Reconocer.Add(camino); l.Reconocer.AddRange(_otros);
            l.Tipos = _tipos.Where(x => !string.IsNullOrWhiteSpace(x.Patron) && !string.IsNullOrWhiteSpace(x.Tipo)).Select(x => new ReglaTipo { Patron = x.Patron, Tipo = x.Tipo.Trim(), Cuando = x.Cuando }).ToList();
            l.Extraer = _extraer.Where(x => !string.IsNullOrWhiteSpace(x.Patron) && !string.IsNullOrWhiteSpace(x.Campo)).Select(x => new ReglaExtraer { Campo = x.Campo, Patron = x.Patron }).ToList();
            if (l.Tipos.Count == 0) l.Tipos = null; if (l.Extraer.Count == 0) l.Extraer = null;
            if (_esPdf)
            {
                var po = new PdfOpciones { Estrategia = _estrategia.SelectedIndex == 1 ? "export" : _estrategia.SelectedIndex == 2 ? "estadocuenta" : _estrategia.SelectedIndex == 3 ? "zonas" : "bbvanet" };
                if (po.Estrategia == "zonas")
                {
                    po.FilaInicia = _zFila.Text.Trim(); po.EncCargo = _zCargo.Text.Trim(); po.EncAbono = _zAbono.Text.Trim(); po.EncSaldo = _zSaldo.Text.Trim();
                    double d; var inv = System.Globalization.CultureInfo.InvariantCulture; var est = System.Globalization.NumberStyles.Float;
                    if (double.TryParse(_zFechaX.Text, est, inv, out d)) po.FechaXMax = d;
                    if (double.TryParse(_zDescX.Text, est, inv, out d)) po.DescXMin = d;
                    if (double.TryParse(_zAlcance.Text, est, inv, out d)) po.Alcance = d;
                }
                l.Pdf = po; l.Columnas = null;
            }
            else
            {
                l.Pdf = null;
                var cols = new Dictionary<string, List<string>>();
                foreach (var c in _automatico ? new List<ColFila>() : _cols)
                {
                    if (string.IsNullOrEmpty(c.Campo) || string.IsNullOrWhiteSpace(c.Nombre)) continue;
                    List<string> lst; if (!cols.TryGetValue(c.Campo, out lst)) cols[c.Campo] = lst = new List<string>();
                    lst.Add(c.Nombre.Trim());
                }
                l.Columnas = cols.Count > 0 ? cols : null;
                if (l.Columnas != null && _hoja != null)
                {
                    bool todoPorNombre = _filaEnc >= 0 && cols.Values.SelectMany(v => v).All(n => !n.StartsWith("#"));
                    l.FilaEncabezado = _filaEnc >= 0 && !todoPorNombre ? _filaEnc + 1 : 0;
                    l.PrimeraFila = _filaEnc < 0 ? 1 : _original.PrimeraFila;
                }
                l.Ajustes = new Ajustes { FormatoFecha = _fecha.SelectedIndex == 1 ? "mdy" : null, ImporteNegativoEsCargo = _negCargo.IsChecked == true ? (bool?)null : false, CuentaDesdeDivisa = _divisa.IsChecked == true };
            }
            return l;
        }

        // ---------- resultado en vivo ----------
        private List<Layout> ConLayout(Layout nuevo)
        {
            var lista = _todos.Where(x => !string.Equals(x.Id, nuevo.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            lista.Add(nuevo); return lista;
        }

        private void Recalcular()
        {
            if (_construyendo) return;
            _estadoPanel.Children.Clear(); _aviso.Text = ""; _coincide.Text = ""; _valido = false;
            var l = LayoutActual();
            string problema = null;
            if (l.Nombre.Length == 0) problema = "Falta el nombre";
            else if (l.Reconocer.Count == 0 || l.Reconocer[0].Count == 0) problema = "Falta al menos una condición para reconocer el formato";
            else if (_tipos.Concat<object>(_extraer).Any(r => !PatronValido(r is ReglaTipo ? ((ReglaTipo)r).Patron : ((ReglaExtraer)r).Patron))) problema = "Hay una regla con un patrón mal escrito";
            else if (_esPdf && _estrategia.SelectedIndex == 3 && (_zFila.Text.Trim().Length == 0 || !PatronValido(_zFila.Text))) problema = "El patrón de la fecha del PDF está mal escrito";
            else if (!_esPdf && !_automatico && (l.Columnas == null || !l.Columnas.ContainsKey("fecha"))) problema = "Falta indicar la columna de la fecha";
            else if (!_esPdf && !_automatico && !(l.Columnas.ContainsKey("cargo") || l.Columnas.ContainsKey("abono") || l.Columnas.ContainsKey("importe"))) problema = "Falta indicar cargo, abono o importe";
            _vista.Content = null;
            if (_hoja != null)
            {
                try
                {
                    var notas = new List<string>(); var propios = ConLayout(l); var filas = _hoja.Filas;
                    if (_esPdf) filas = Lector.Leer(_rutaMuestra, notas, propios)[0].Filas;
                    var det = BancoArchivo.Detectar(filas, _nombreMuestra, new[] { l });
                    var res = BancoArchivo.Interpretar(filas, det.Mapa);
                    int pct = det.Coincidencias.Count > 0 ? det.Coincidencias[0].Porcentaje : 0;
                    if (_esPdf) { try { pct = Catalogo.Porcentaje(l, Catalogo.HuellaDe(new List<string[]>(), -1, _nombreMuestra, BancoPdf.TextoInicial(_rutaMuestra))); } catch { pct = 100; } }
                    _coincide.Text = "Con el archivo de muestra coincide al " + pct + " %" + (pct >= 100 ? "" : ": con menos de 100 % este layout no se usaría solo.");
                    _coincide.Foreground = pct >= 100 ? Tema.Pincel("#17623B") : Tema.Pincel("#7A4E00");
                    _estadoPanel.Children.Add(Tema.Chip(res.Movs.Count + " movimientos", res.Movs.Count > 0 ? Tono.Bueno : Tono.Malo));
                    if (res.SaldoVerificado) { var c = Tema.Chip(res.RupturasSaldo == 0 ? "El saldo cuadra" : res.RupturasSaldo + " renglones donde el saldo no cuadra", res.RupturasSaldo == 0 ? Tono.Bueno : Tono.Aviso); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                    if (res.Errores.Count > 0) { var c = Tema.Chip(res.Errores[0], Tono.Malo); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                    if (res.Desde.HasValue) { var c = Tema.Chip(res.Desde.Value.ToString("dd/MM/yyyy") + " – " + res.Hasta.Value.ToString("dd/MM/yyyy"), Tono.Neutro); c.Margin = new Thickness(8, 0, 0, 0); _estadoPanel.Children.Add(c); }
                    if (pct < 100 && problema == null) problema = "Este layout no reconoce el archivo de muestra: revisa las condiciones de arriba";
                    if (problema == null)
                    {
                        var todos = BancoArchivo.Detectar(filas, _nombreMuestra, ConLayout(l));
                        if (!_esPdf && todos.Layout != null && !string.Equals(todos.Layout.Id, l.Id, StringComparison.OrdinalIgnoreCase))
                            _aviso.Text = "Con este reconocimiento el archivo seguiría leyéndose con «" + todos.Layout.Nombre + "». Agrega condiciones más específicas.";
                    }
                    var vista = res.Movs.Take(40).Select(m => new { Fecha = m.Fecha.ToString("dd/MM/yyyy"), Hora = m.Hora.HasValue ? m.Hora.Value.ToString(@"hh\:mm") : "", Descripcion = m.Descripcion, Cargo = m.Cargo != 0 ? Tema.Dinero(m.Cargo) : "", Abono = m.Abono != 0 ? Tema.Dinero(m.Abono) : "", Saldo = m.Saldo.HasValue ? Tema.Dinero(m.Saldo.Value) : "", Tipo = m.Tipo }).ToList();
                    var dg = new DataGrid { ItemsSource = vista };
                    Action<string, string, double, bool> col = (enc, prop, ancho, derecha) =>
                    {
                        var c = new DataGridTextColumn { Header = enc, Binding = new System.Windows.Data.Binding(prop), Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star) };
                        if (derecha) { var st = new Style(typeof(TextBlock)); st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right)); c.ElementStyle = st; }
                        dg.Columns.Add(c);
                    };
                    col("Fecha", "Fecha", 84, false); col("Hora", "Hora", 52, false); col("Descripción", "Descripcion", 0, false); col("Cargo", "Cargo", 96, true); col("Abono", "Abono", 96, true); col("Saldo", "Saldo", 104, true); col("Tipo", "Tipo", 130, false);
                    _vista.Content = dg;
                }
                catch (Exception ex) { _estadoPanel.Children.Add(Tema.Chip(ex.Message, Tono.Malo)); problema = problema ?? "No se pudo leer la muestra con este layout"; }
            }
            else _estadoPanel.Children.Add(Tema.Txt("Abre un archivo de muestra para ver cómo lee este layout.", 12, false, Tema.Texto2));
            if (problema != null) _aviso.Text = problema;
            _valido = problema == null;
            _guardar.IsEnabled = _valido && _puedeGuardar;
        }

        // ---------- guardar ----------
        private void Guardar()
        {
            if (!_valido || !_puedeGuardar) return;
            var l = LayoutActual();
            var cambios = new List<string>(); var antes = _todos; var despues = ConLayout(l);
            var owner = Owner as VentanaPrincipal;
            foreach (var a in owner == null ? new List<AnalisisArchivo>() : owner.Bandeja.Archivos)
            {
                try
                {
                    if (a.Hoja == null || a.Ruta.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    var d0 = BancoArchivo.Detectar(a.Hoja.Filas, a.Nombre, antes); var d1 = BancoArchivo.Detectar(a.Hoja.Filas, a.Nombre, despues);
                    string id0 = d0.Layout != null ? d0.Layout.Id : "", id1 = d1.Layout != null ? d1.Layout.Id : "";
                    if (id0 != id1) cambios.Add(a.Nombre + ": «" + (d0.Layout != null ? d0.Layout.Nombre : "sin layout") + "» pasaría a «" + (d1.Layout != null ? d1.Layout.Nombre : "sin layout") + "»");
                }
                catch { }
            }
            if (cambios.Count > 0 && MessageBox.Show(this, "Con este cambio, archivos de la bandeja se leerían con otro layout:\n\n" + string.Join("\n", cambios.Take(8)) + "\n\n¿Guardar de todos modos?", "Bancos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _ctx.Banco.GuardarLayout(l); DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
