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

// BancoImportarWindow.cs -- cargar un estado de cuenta o un archivo de movimientos: se elige el archivo, se sugiere el banco y la cuenta (el usuario
// decide cuando hay varias), se ven los movimientos tal como se entendieron y se corrigen las columnas si hace falta ANTES de guardar nada.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    internal sealed class BancoImportarWindow : Window
    {
        private sealed class GrupoUi
        {
            public string Key;                     // numero de cuenta que dice el archivo ("" si no dice)
            public List<BancoMov> Movs;
            public ComboBox Combo = new ComboBox { MinHeight = 28 };
            public TextBlock Motivo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
            public decimal? SaldoInicial, SaldoFinal;
            public BancoCuentaFila Cuenta => Combo.SelectedItem as BancoCuentaFila;
        }

        public sealed class Vista
        {
            public string Fecha { get; set; } = "";
            public string Descripcion { get; set; } = "";
            public decimal Cargo { get; set; }
            public decimal Abono { get; set; }
            public decimal? Saldo { get; set; }
            public string Tipo { get; set; } = "";
            public string Contraparte { get; set; } = "";
            public string Cuenta { get; set; } = "";
        }

        private readonly SqlConnection _conn; private readonly string _rfc;
        private List<BancoCuentaFila> _cuentas = new List<BancoCuentaFila>();
        private Dictionary<int, (DateTime fecha, decimal saldo)> _ultimos = new Dictionary<int, (DateTime, decimal)>();

        private string _ruta, _sha; private List<string[]> _filas; private List<BancoXlsx.Hoja> _hojas;
        private BancoDeteccion _det; private BancoMapa _mapa; private BancoResultado _res; private string _perfilUsado;
        private readonly List<GrupoUi> _grupos = new List<GrupoUi>();
        private bool _cargando;
        public int Cargados { get; private set; }

        private readonly TextBlock _archivoTxt = new TextBlock { Foreground = BancoUi.Suave, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _avisoArchivo = new TextBlock { Foreground = BancoUi.Ambar, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        private readonly ComboBox _hoja = new ComboBox { Width = 220, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
        private readonly ComboBox _banco = new ComboBox { MinHeight = 28, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _deteccion = new TextBlock { Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        private readonly StackPanel _panelGrupos = new StackPanel();
        private readonly TextBlock _resumen = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = BancoUi.Tinta };
        private readonly StackPanel _mensajes = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        private readonly StackPanel _cuerpo = new StackPanel { Visibility = Visibility.Collapsed };
        private readonly Expander _exp = new Expander { Header = "Cómo leí el archivo (corregir columnas)", Margin = new Thickness(0, 14, 0, 0) };
        private readonly string[] _nombresCampos = { "Fecha", "Descripción", "Referencia", "Cargo (retiro)", "Abono (depósito)", "Importe con signo", "Columna Cargo/Abono", "Saldo", "Cuenta" };
        private readonly ComboBox[] _combos;
        private readonly TextBox _filaEnc = new TextBox { Width = 60 }, _filaDatos = new TextBox { Width = 60 };
        private readonly CheckBox _tarjeta = new CheckBox { Content = "El importe positivo es un cargo (tarjetas de crédito)", Margin = new Thickness(0, 8, 0, 0) };
        private readonly ComboBox _fmtFecha = new ComboBox { Width = 200 };
        private readonly DataGrid _grid = new DataGrid { Height = 280, IsReadOnly = true, AutoGenerateColumns = false, Margin = new Thickness(0, 8, 0, 0) };
        private readonly CheckBox _recordar = new CheckBox { Content = "Recordar este formato para archivos con los mismos encabezados", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        private readonly Button _importar;

        public BancoImportarWindow(SqlConnection conn, string rfc)
        {
            _conn = conn; _rfc = rfc;
            Title = "Importar estado de cuenta o movimientos"; Width = 1080; Height = 800; MinWidth = 760; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = BancoUi.Fondo;
            BancoUi.AjustarAPantalla(this);
            _combos = _nombresCampos.Select(_ => new ComboBox { MinHeight = 26 }).ToArray();
            _importar = BancoUi.Boton(this, "Importar", "Primary"); _importar.IsEnabled = false;

            var raiz = new DockPanel { Margin = new Thickness(22) };
            var pie = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false }; DockPanel.SetDock(pie, Dock.Bottom);
            var cancelar = BancoUi.Boton(this, "Cancelar", "Ghost"); cancelar.Click += (s, e) => Close();
            _importar.Click += (s, e) => Importar();
            var der = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(der, Dock.Right); der.Children.Add(_importar); der.Children.Add(cancelar);
            DockPanel.SetDock(_recordar, Dock.Left);
            pie.Children.Add(der); pie.Children.Add(_recordar);
            raiz.Children.Add(pie);

            var pila = new StackPanel();
            pila.Children.Add(new TextBlock { Text = "Importar estado de cuenta o movimientos", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta });
            pila.Children.Add(new TextBlock { Text = "Archivos CSV, TXT o Excel (.xlsx) tal como los entrega el banco. Primero ves cómo se entendieron; no se guarda nada hasta que pulses «Importar».", Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });

            var fila = new DockPanel();
            var examinar = BancoUi.Boton(this, "Elegir archivo…", "Ghost"); examinar.Click += (s, e) => Examinar();
            DockPanel.SetDock(examinar, Dock.Left); fila.Children.Add(examinar); fila.Children.Add(_archivoTxt);
            pila.Children.Add(fila);
            var filaHoja = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            filaHoja.Children.Add(_hoja); pila.Children.Add(filaHoja);
            _hoja.SelectionChanged += (s, e) => { if (!_cargando && _hoja.SelectedItem is BancoXlsx.Hoja h) { _filas = h.Filas; Analizar(); } };
            pila.Children.Add(_avisoArchivo);

            _cuerpo.Children.Add(BancoUi.Etiqueta("Banco"));
            _banco.ItemsSource = BancoCatalogo.Bancos;
            _banco.SelectionChanged += (s, e) => { if (!_cargando && IsLoaded) BancoCambio(); };
            _cuerpo.Children.Add(_banco); _cuerpo.Children.Add(_deteccion);

            _cuerpo.Children.Add(new TextBlock { Text = "¿A qué cuenta pertenece?", FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta, Margin = new Thickness(0, 16, 0, 4) });
            _cuerpo.Children.Add(_panelGrupos);
            var nueva = BancoUi.Boton(this, "Nueva cuenta…", "Ghost"); nueva.HorizontalAlignment = HorizontalAlignment.Left; nueva.Margin = new Thickness(0, 8, 0, 0); nueva.Click += (s, e) => NuevaCuenta();
            _cuerpo.Children.Add(nueva);

            _cuerpo.Children.Add(new TextBlock { Text = "Lo que entendí", FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta, Margin = new Thickness(0, 16, 0, 4) });
            _cuerpo.Children.Add(_resumen); _cuerpo.Children.Add(_mensajes);

            ConstruirMapeo();
            _cuerpo.Children.Add(_exp);

            _grid.Columns.Add(Texto("FECHA", "Fecha", 110, "CeldaAtenuada"));
            _grid.Columns.Add(Texto("DESCRIPCIÓN", "Descripcion", 340, "CeldaTexto"));
            _grid.Columns.Add(Numero("CARGO", "Cargo", 100));
            _grid.Columns.Add(Numero("ABONO", "Abono", 100));
            _grid.Columns.Add(Numero("SALDO", "Saldo", 110));
            _grid.Columns.Add(Texto("TIPO", "Tipo", 130, "CeldaAtenuada"));
            _grid.Columns.Add(Texto("CONTRAPARTE", "Contraparte", 220, "CeldaAtenuada"));
            _cuerpo.Children.Add(_grid);
            pila.Children.Add(_cuerpo);
            raiz.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = pila });
            Content = raiz;

            Loaded += (s, e) => { Recargar(); };
            ContentRendered += AlMostrar;
        }

        private bool _primera = true;
        private void AlMostrar(object s, EventArgs e) { if (!_primera) return; _primera = false; if (_ruta == null) Examinar(); if (_ruta == null) Close(); }

        private DataGridTextColumn Texto(string h, string b, double w, string estilo) => new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(b), Width = w, ElementStyle = (Style)FindResource(estilo) };
        private DataGridTextColumn Numero(string h, string b, double w) => new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(b) { StringFormat = "N2" }, Width = w, ElementStyle = (Style)FindResource("CeldaNumero") };

        private void Recargar()
        {
            _cuentas = BancoDatos.ObtenerCuentas(_conn, _rfc);
            _ultimos = BancoDatos.UltimosSaldos(_conn, _rfc);
        }

        // ---------- Archivo ----------
        private void Examinar()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Estado de cuenta o movimientos del banco", Filter = "Estados de cuenta|*.csv;*.txt;*.tsv;*.xlsx;*.xlsm|Todos los archivos|*.*" };
            if (dlg.ShowDialog(this) == true) Abrir(dlg.FileName);
        }

        private void Abrir(string ruta)
        {
            try
            {
                string ext = Path.GetExtension(ruta).ToLowerInvariant();
                if (ext == ".pdf") { Aviso("Los PDF todavía no se pueden leer aquí. En el portal del banco busca «descargar movimientos» o «exportar» (CSV o Excel): trae lo mismo y sin errores de lectura.", true); return; }
                if (ext == ".xls") { Aviso("Los .xls antiguos no se leen. Ábrelo en Excel, guárdalo como .xlsx y elígelo de nuevo.", true); return; }
                byte[] bytes = File.ReadAllBytes(ruta);
                _sha = BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", "");
                _cargando = true;
                _hojas = null; _hoja.Visibility = Visibility.Collapsed;
                if (ext == ".xlsx" || ext == ".xlsm")
                {
                    _hojas = BancoXlsx.Leer(ruta);
                    if (_hojas.Count == 0) { _cargando = false; Aviso("El libro no trae datos.", true); return; }
                    var mayor = _hojas.OrderByDescending(h => h.Filas.Count).First();
                    _hoja.ItemsSource = _hojas; _hoja.SelectedItem = mayor; _filas = mayor.Filas;
                    _hoja.Visibility = _hojas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                }
                else _filas = BancoTexto.LeerTexto(BancoTexto.Decodificar(bytes));
                _cargando = false;
                _ruta = ruta; _archivoTxt.Text = ruta;
                var antes = BancoDatos.ArchivosYaCargados(_conn, _rfc, _sha);
                _avisoArchivo.Text = antes.Count > 0 ? "Este mismo archivo ya se cargó (" + string.Join("; ", antes) + "). Si lo cargas otra vez, los movimientos repetidos se omiten." : "";
                Recargar();
                Analizar();
            }
            catch (Exception ex) { _cargando = false; Aviso("No se pudo leer el archivo:\n\n" + ex.Message, true); }
        }

        private void Aviso(string texto, bool error) => MessageBox.Show(this, texto, Title, MessageBoxButton.OK, error ? MessageBoxImage.Warning : MessageBoxImage.Information);

        private void Analizar()
        {
            _det = BancoArchivo.Detectar(_filas, Path.GetFileName(_ruta));
            var mapa = _det.Mapa; _perfilUsado = null; string bancoPerfil = null;
            var perfil = BancoDatos.BuscarPerfil(_conn, _rfc, mapa.Firma);
            if (perfil != null)
            {
                var re = BancoArchivo.Reubicar(_filas, perfil.Mapa);
                if (re != null) { mapa = re; _perfilUsado = perfil.Nombre; bancoPerfil = perfil.Banco; }
            }
            _mapa = mapa;
            _cargando = true;
            BancoUi.PonTexto(_banco, !string.IsNullOrEmpty(bancoPerfil) ? bancoPerfil : _det.Banco);
            _recordar.IsChecked = false;
            _cargando = false;
            string conf = _det.Confianza >= 3 ? "alta" : _det.Confianza == 2 ? "media" : _det.Confianza == 1 ? "baja" : "ninguna";
            _deteccion.Text = _perfilUsado != null ? "Formato guardado «" + _perfilUsado + "»."
                : _det.Banco.Length > 0 ? "Sugerido por el contenido del archivo (" + _det.Perfil + ", confianza " + conf + "). Cámbialo si no es el correcto."
                : string.Join(" ", _det.Notas);
            _cuerpo.Visibility = Visibility.Visible;
            LlenarMapeo();
            Reinterpretar(true);
        }

        // ---------- Mapeo de columnas ----------
        private void ConstruirMapeo()
        {
            var raiz = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            raiz.Children.Add(new TextBlock { Text = "Si algo salió mal (una columna equivocada, fechas al revés), corrígelo aquí: la tabla de abajo se actualiza al instante. Marca «Recordar este formato» para que el próximo archivo igual se lea solo.", Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            for (int c = 0; c < 3; c++) g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < _combos.Length; i++)
            {
                int fila = i / 3, col = i % 3;
                while (g.RowDefinitions.Count <= fila) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var p = new StackPanel { Margin = new Thickness(0, 0, 12, 6) };
                p.Children.Add(new TextBlock { Text = _nombresCampos[i], FontSize = 11.5, Foreground = BancoUi.Suave, Margin = new Thickness(0, 0, 0, 2) });
                p.Children.Add(_combos[i]);
                Grid.SetRow(p, fila); Grid.SetColumn(p, col); g.Children.Add(p);
                _combos[i].SelectionChanged += (s, e) => { if (!_cargando) MapeoCambio(); };
            }
            raiz.Children.Add(g);
            var f2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            f2.Children.Add(new TextBlock { Text = "Fila del encabezado (0 = no tiene)", VerticalAlignment = VerticalAlignment.Center, Foreground = BancoUi.Suave, Margin = new Thickness(0, 0, 6, 0) }); f2.Children.Add(_filaEnc);
            f2.Children.Add(new TextBlock { Text = "Primera fila de datos", VerticalAlignment = VerticalAlignment.Center, Foreground = BancoUi.Suave, Margin = new Thickness(18, 0, 6, 0) }); f2.Children.Add(_filaDatos);
            f2.Children.Add(new TextBlock { Text = "Fechas", VerticalAlignment = VerticalAlignment.Center, Foreground = BancoUi.Suave, Margin = new Thickness(18, 0, 6, 0) });
            _fmtFecha.ItemsSource = new[] { "día/mes/año (México)", "mes/día/año" }; f2.Children.Add(_fmtFecha);
            raiz.Children.Add(f2); raiz.Children.Add(_tarjeta);
            _filaEnc.LostFocus += (s, e) => { if (!_cargando) MapeoCambio(); }; _filaDatos.LostFocus += (s, e) => { if (!_cargando) MapeoCambio(); };
            _fmtFecha.SelectionChanged += (s, e) => { if (!_cargando) MapeoCambio(); };
            _tarjeta.Click += (s, e) => { if (!_cargando) MapeoCambio(); };
            _exp.Content = raiz;
        }

        private static string Letra(int j) { string s = ""; j++; while (j > 0) { int r = (j - 1) % 26; s = (char)('A' + r) + s; j = (j - 1) / 26; } return s; }

        private void LlenarMapeo()
        {
            _cargando = true;
            int ncol = _filas.Take(200).Select(f => f.Length).DefaultIfEmpty(0).Max();
            var etiquetas = new List<string> { "(ninguna)" };
            for (int j = 0; j < ncol; j++)
            {
                string muestra = "";
                if (_mapa.FilaEncabezado >= 0 && _mapa.FilaEncabezado < _filas.Count && j < _filas[_mapa.FilaEncabezado].Length) muestra = _filas[_mapa.FilaEncabezado][j];
                else if (_mapa.PrimeraFila < _filas.Count && j < _filas[_mapa.PrimeraFila].Length) muestra = _filas[_mapa.PrimeraFila][j];
                if (muestra.Length > 28) muestra = muestra.Substring(0, 28) + "…";
                etiquetas.Add(Letra(j) + (muestra.Length > 0 ? " — " + muestra : ""));
            }
            int[] valores = { _mapa.Fecha, _mapa.Descripcion, _mapa.Referencia, _mapa.Cargo, _mapa.Abono, _mapa.Importe, _mapa.Signo, _mapa.Saldo, _mapa.Cuenta };
            for (int i = 0; i < _combos.Length; i++) { _combos[i].ItemsSource = etiquetas; _combos[i].SelectedIndex = valores[i] + 1 >= 0 && valores[i] + 1 < etiquetas.Count ? valores[i] + 1 : 0; }
            _filaEnc.Text = (_mapa.FilaEncabezado + 1).ToString(); _filaDatos.Text = (_mapa.PrimeraFila + 1).ToString();
            _fmtFecha.SelectedIndex = _mapa.FormatoFecha == "mdy" ? 1 : 0;
            _tarjeta.IsChecked = !_mapa.ImporteNegativoEsCargo;
            _exp.IsExpanded = !_det.MapaPorEncabezado || !_mapa.Completo;
            _cargando = false;
        }

        private void MapeoCambio()
        {
            var m = _mapa.Copiar();
            int V(int i) => _combos[i].SelectedIndex - 1;
            m.Fecha = V(0); m.Descripcion = V(1); m.Referencia = V(2); m.Cargo = V(3); m.Abono = V(4); m.Importe = V(5); m.Signo = V(6); m.Saldo = V(7); m.Cuenta = V(8);
            m.Detalle = m.Detalle.Where(d => d != m.Descripcion && d != m.Referencia).ToList();
            int fe = int.TryParse(_filaEnc.Text, out int a) ? a - 1 : -1; int fd = int.TryParse(_filaDatos.Text, out int b) ? b - 1 : 0;
            m.FilaEncabezado = fe >= 0 && fe < _filas.Count ? fe : -1; m.PrimeraFila = Math.Max(0, fd);
            m.FormatoFecha = _fmtFecha.SelectedIndex == 1 ? "mdy" : "auto";
            m.ImporteNegativoEsCargo = _tarjeta.IsChecked != true;
            m.Firma = m.FilaEncabezado >= 0 ? BancoArchivo.FirmaDe(_filas[m.FilaEncabezado]) : "sin-encabezado|" + _filas.Take(200).Select(f => f.Length).DefaultIfEmpty(0).Max();
            _mapa = m;
            _recordar.IsChecked = true;                 // el usuario corrigio algo: lo normal es querer conservarlo
            Reinterpretar(false);
        }

        // ---------- Resultado ----------
        private void BancoCambio() { if (_res != null) ConstruirGrupos(false); }

        private void Reinterpretar(bool reiniciarCuentas)
        {
            _res = BancoArchivo.Interpretar(_filas, _mapa);
            var r = _res;
            _grid.ItemsSource = r.Movs.Take(400).Select(m => new Vista
            {
                Fecha = m.Fecha.ToString("yyyy-MM-dd"), Descripcion = m.Descripcion, Cargo = m.Cargo, Abono = m.Abono, Saldo = m.Saldo, Tipo = m.Tipo,
                Contraparte = string.Join("  ", new[] { m.NombreContraparte, m.RfcContraparte }.Where(x => !string.IsNullOrEmpty(x))), Cuenta = m.CuentaArchivo
            }).ToList();

            if (r.Movs.Count == 0) _resumen.Text = "No encontré movimientos con esas columnas.";
            else
            {
                string saldo = r.SaldoInicial.HasValue || r.SaldoFinal.HasValue ? " · saldo " + (r.SaldoInicial.HasValue ? r.SaldoInicial.Value.ToString("C2") : "—") + " → " + (r.SaldoFinal.HasValue ? r.SaldoFinal.Value.ToString("C2") : "—") : "";
                _resumen.Text = r.Movs.Count.ToString("N0") + " movimientos del " + r.Desde.Value.ToString("yyyy-MM-dd") + " al " + r.Hasta.Value.ToString("yyyy-MM-dd") + " · cargos " + r.TotalCargos.ToString("C2") + " · abonos " + r.TotalAbonos.ToString("C2") + saldo +
                    (r.Descendente ? " · venían del más nuevo al más viejo (ya los ordené)" : "") + (_grid.Items.Count < r.Movs.Count ? "\nSe muestran los primeros 400." : "");
            }
            _mensajes.Children.Clear();
            if (r.SaldoVerificado && r.RupturasSaldo == 0) Linea("✔ El saldo corrido cuadra en todos los renglones: el archivo está completo y bien leído.", BancoUi.Verde);
            else if (!r.SaldoVerificado && r.Movs.Count > 0) Linea("El archivo no trae saldo por renglón: no puedo comprobar que esté completo.", BancoUi.Suave);
            foreach (var e in r.Errores.Take(8)) Linea("✖ " + e, BancoUi.Rojo);
            if (r.Errores.Count > 8) Linea("… y " + (r.Errores.Count - 8) + " errores más.", BancoUi.Rojo);
            foreach (var a in r.Avisos) Linea("⚠ " + a, BancoUi.Ambar);
            if (r.FilasIgnoradas > 0) Linea(r.FilasIgnoradas + " fila(s) sin fecha o sin importe (encabezados, totales, saldos iniciales) no se tomaron como movimientos.", BancoUi.Suave);
            foreach (var n in _det.Notas.Where(x => !x.StartsWith("No reconocí"))) Linea(n, BancoUi.Suave);

            ConstruirGrupos(reiniciarCuentas);
        }

        private void Linea(string t, Brush color) => _mensajes.Children.Add(new TextBlock { Text = t, Foreground = color, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });

        // ---------- Cuentas ----------
        private void ConstruirGrupos(bool reiniciar)
        {
            var previas = _grupos.ToDictionary(g => g.Key, g => g.Cuenta?.Id);
            _grupos.Clear(); _panelGrupos.Children.Clear();
            if (_res == null || _res.Movs.Count == 0) { ActualizarImportar(); return; }
            var porCuenta = _res.Movs.GroupBy(m => m.CuentaArchivo ?? "").OrderBy(g => g.Key).ToList();
            string banco = BancoUi.Valor(_banco);
            foreach (var gr in porCuenta)
            {
                var g = new GrupoUi { Key = gr.Key, Movs = gr.ToList() };
                var primero = g.Movs[0]; var ultimo = g.Movs[g.Movs.Count - 1];
                g.SaldoInicial = primero.Saldo.HasValue ? primero.Saldo.Value - (_res.SaldoDeuda ? primero.Cargo - primero.Abono : primero.Abono - primero.Cargo) : (decimal?)null;
                g.SaldoFinal = ultimo.Saldo;
                g.Combo.ItemsSource = _cuentas;
                var numeros = new List<string>(); if (g.Key.Length > 0) numeros.Add(g.Key);
                if (porCuenta.Count == 1) numeros.AddRange(_det.CuentasPreambulo);
                var sug = BancoDatos.SugerirCuenta(_cuentas, banco, numeros, Path.GetFileName(_ruta), g.SaldoInicial, _ultimos);
                BancoCuentaFila elegida = null;
                if (!reiniciar && previas.TryGetValue(g.Key, out int? idPrevio) && idPrevio.HasValue) elegida = _cuentas.FirstOrDefault(c => c.Id == idPrevio.Value);
                if (elegida == null) elegida = sug.Cuenta;
                g.Motivo.Text = sug.Motivo; g.Motivo.Foreground = sug.Segura ? BancoUi.Verde : (sug.Cuenta != null ? BancoUi.Ambar : BancoUi.Rojo);
                var panel = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 221, 232)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Background = Brushes.White };
                var sp = new StackPanel();
                string titulo = g.Key.Length > 0 ? "Cuenta que dice el archivo: " + g.Key : "El archivo no dice de qué cuenta es";
                sp.Children.Add(new TextBlock { Text = titulo + "  ·  " + g.Movs.Count.ToString("N0") + " movimientos", FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta, Margin = new Thickness(0, 0, 0, 6) });
                sp.Children.Add(g.Combo); sp.Children.Add(g.Motivo);
                panel.Child = sp; _panelGrupos.Children.Add(panel);
                _grupos.Add(g);
                g.Combo.SelectionChanged += (s, e) => ActualizarGrupo(g, null);
                g.Combo.SelectedItem = elegida;
                if (elegida == null) ActualizarImportar();
                else ActualizarGrupo(g, sug);
            }
            ActualizarImportar();
        }

        private void ActualizarGrupo(GrupoUi g, BancoSugerencia sug)
        {
            var c = g.Cuenta;
            if (c == null) { ActualizarImportar(); return; }
            var detalle = new List<string>();
            if (sug != null && sug.Cuenta == c) detalle.Add(sug.Motivo);
            else if (sug == null) detalle.Add("Elegida por ti.");
            int dup = BancoDatos.ContarDuplicados(_conn, c.Id, g.Movs);
            if (dup > 0) detalle.Add(dup + " de " + g.Movs.Count + " movimientos ya estaban cargados en esta cuenta: se omitirán.");
            var cont = BancoDatos.AvisoDeContinuidad(_conn, c.Id, g.Movs.Min(m => m.Fecha), g.SaldoInicial);
            if (cont != null) detalle.Add("⚠ " + cont);
            if (g.Key.Length > 0 && !BancoArchivo.MismaCuenta(g.Key, c.Numero) && !BancoArchivo.MismaCuenta(g.Key, c.Clabe))
                detalle.Add("⚠ El número que trae el archivo (" + g.Key + ") no coincide con el de esta cuenta (" + c.Numero + "). Verifica que sea la correcta.");
            g.Motivo.Text = string.Join("\n", detalle);
            g.Motivo.Foreground = detalle.Any(d => d.StartsWith("⚠")) ? BancoUi.Ambar : (sug != null && sug.Segura ? BancoUi.Verde : BancoUi.Suave);
            ActualizarImportar();
        }

        private void ActualizarImportar()
        {
            bool ok = _res != null && _res.Movs.Count > 0 && _res.Errores.Count == 0 && _grupos.Count > 0 && _grupos.All(g => g.Cuenta != null);
            _importar.IsEnabled = ok;
            _importar.Content = ok ? "Importar " + _res.Movs.Count.ToString("N0") + " movimientos" : "Importar";
            _importar.ToolTip = ok ? null : _res != null && _res.Errores.Count > 0 ? "Corrige los errores marcados con ✖." : "Elige la cuenta a la que pertenece el archivo.";
        }

        private void NuevaCuenta()
        {
            var c = new BancoCuentaFila { Banco = BancoUi.Valor(_banco) };
            var guess = _grupos.Count == 1 ? (_grupos[0].Key.Length > 0 ? _grupos[0].Key : _det.CuentasPreambulo.FirstOrDefault()) : null;
            if (!string.IsNullOrEmpty(guess)) c.Numero = guess;
            var w = new BancoCuentaEditWindow(c, true) { Owner = this };
            if (w.ShowDialog() != true) return;
            try
            {
                int id = BancoDatos.GuardarCuenta(_conn, _rfc, w.Resultado);
                Recargar();
                ConstruirGrupos(true);
                var nueva = _cuentas.FirstOrDefault(x => x.Id == id);
                var vacio = _grupos.FirstOrDefault(g => g.Cuenta == null);
                if (nueva != null && vacio != null) vacio.Combo.SelectedItem = nueva;
            }
            catch (Exception ex) { Aviso("No se pudo guardar la cuenta:\n\n" + ex.Message, true); }
        }

        // ---------- Importar ----------
        private void Importar()
        {
            if (!_importar.IsEnabled) return;
            try
            {
                int nuevos = 0, dup = 0; var partes = new List<string>();
                string perfil = _perfilUsado ?? _det.Perfil;
                foreach (var g in _grupos)
                {
                    var r = BancoDatos.Importar(_conn, _rfc, g.Cuenta.Id, _ruta, _sha, perfil, g.Movs, g.SaldoInicial, g.SaldoFinal, string.Join(" ", _res.Avisos));
                    nuevos += r.Nuevos; dup += r.Duplicados;
                    partes.Add(g.Cuenta.Alias + ": " + r.Nuevos.ToString("N0") + " nuevos" + (r.Duplicados > 0 ? ", " + r.Duplicados + " ya estaban" : ""));
                }
                if (_recordar.IsChecked == true)
                {
                    string banco = BancoUi.Valor(_banco);
                    BancoDatos.GuardarPerfil(_conn, _rfc, (banco.Length > 0 ? banco : "Formato") + " · " + DateTime.Today.ToString("yyyy-MM-dd"), banco, _mapa);
                }
                Cargados = nuevos;
                MessageBox.Show(this, "Listo.\n\n" + string.Join("\n", partes) + (_recordar.IsChecked == true ? "\n\nQuedó guardado el formato: el próximo archivo igual se leerá solo." : ""), Title, MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex) { Aviso("No se pudo importar:\n\n" + ex.Message, true); }
        }
    }
}
