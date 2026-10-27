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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    internal static class BancoUi
    {
        public static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(22, 38, 58));
        public static readonly Brush Suave = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        public static readonly Brush Fondo = new SolidColorBrush(Color.FromRgb(244, 246, 249));
        public static readonly Brush Ambar = new SolidColorBrush(Color.FromRgb(161, 98, 7));
        public static readonly Brush Rojo = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        public static readonly Brush Verde = new SolidColorBrush(Color.FromRgb(21, 128, 61));

        public static Button Boton(FrameworkElement dueno, string texto, string estilo) => new Button { Content = texto, Style = (Style)dueno.FindResource(estilo), Margin = new Thickness(0, 0, 8, 0) };
        public static TextBlock Etiqueta(string t) => new TextBlock { Text = t, Foreground = Suave, FontSize = 12, Margin = new Thickness(0, 8, 0, 3) };

        // Una ventana nunca mas grande que el area de trabajo (pantallas chicas o con escala alta): el contenido se desplaza.
        public static void AjustarAPantalla(Window w)
        {
            var area = SystemParameters.WorkArea;
            w.MaxWidth = area.Width; w.MaxHeight = area.Height;
            w.Width = Math.Min(w.Width, area.Width); w.Height = Math.Min(w.Height, area.Height);
        }

        // Listas cerradas de texto (banco, moneda). Un valor que no esta en la lista (cuenta antigua, banco nuevo) se agrega para no perderlo.
        public static void PonTexto(ComboBox c, string texto)
        {
            var items = ((c.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Select(o => o as string).Where(o => o != null) ?? Enumerable.Empty<string>()).ToList();
            if (string.IsNullOrWhiteSpace(texto)) { c.SelectedIndex = -1; return; }
            string hallado = items.FirstOrDefault(o => string.Equals(o, texto, StringComparison.OrdinalIgnoreCase));
            if (hallado == null) { items.Add(texto.Trim()); c.ItemsSource = items; hallado = texto.Trim(); }
            c.SelectedItem = hallado;
        }

        public static string Valor(ComboBox c) => (c.SelectedItem as string ?? "").Trim();
    }

    // Alta y edicion de una cuenta bancaria (o tarjeta). El numero es lo que permite reconocer de que cuenta es cada archivo.
    internal sealed class BancoCuentaEditWindow : Window
    {
        private readonly TextBox _alias = new TextBox(), _numero = new TextBox(), _clabe = new TextBox { MaxLength = 18 }, _contable = new TextBox();
        private readonly ComboBox _banco = new ComboBox(), _moneda = new ComboBox(), _tipo = new ComboBox();
        private readonly TextBlock _msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = BancoUi.Rojo, MinHeight = 20, Margin = new Thickness(0, 8, 0, 0) };
        private readonly BancoCuentaFila _c;
        public BancoCuentaFila Resultado { get; private set; }

        public BancoCuentaEditWindow(BancoCuentaFila c, bool nueva)
        {
            _c = c;
            Title = nueva ? "Nueva cuenta bancaria" : "Cuenta bancaria";
            Width = 520; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = BancoUi.Fondo; ResizeMode = ResizeMode.NoResize;
            BancoUi.AjustarAPantalla(this);
            var raiz = new StackPanel { Margin = new Thickness(24) };
            raiz.Children.Add(new TextBlock { Text = Title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta });
            raiz.Children.Add(new TextBlock { Text = "Registra cada cuenta por separado, aunque sean del mismo banco: así cada estado de cuenta se carga a la suya.", Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });

            _banco.ItemsSource = BancoCatalogo.Bancos; BancoUi.PonTexto(_banco, c.Banco); _moneda.ItemsSource = new[] { "MXN", "USD", "EUR" }; BancoUi.PonTexto(_moneda, c.Moneda);
            _tipo.ItemsSource = new[] { "Cuenta", "Tarjeta de crédito" }; _tipo.SelectedItem = c.Tipo == "Tarjeta" ? "Tarjeta de crédito" : "Cuenta";
            _alias.Text = c.Alias; _numero.Text = c.Numero; _clabe.Text = c.Clabe; _contable.Text = c.CuentaContable;

            raiz.Children.Add(BancoUi.Etiqueta("Nombre para reconocerla (por ejemplo «Banorte nómina»)")); raiz.Children.Add(_alias);
            raiz.Children.Add(BancoUi.Etiqueta("Banco")); raiz.Children.Add(_banco);
            raiz.Children.Add(BancoUi.Etiqueta("Número de cuenta o de tarjeta (con los últimos 4 dígitos basta)")); raiz.Children.Add(_numero);
            raiz.Children.Add(BancoUi.Etiqueta("CLABE (opcional, 18 dígitos)")); raiz.Children.Add(_clabe);
            var fila = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            fila.ColumnDefinitions.Add(new ColumnDefinition()); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); fila.ColumnDefinitions.Add(new ColumnDefinition());
            var izq = new StackPanel(); izq.Children.Add(BancoUi.Etiqueta("Moneda")); izq.Children.Add(_moneda);
            var der = new StackPanel(); der.Children.Add(BancoUi.Etiqueta("Tipo")); der.Children.Add(_tipo); Grid.SetColumn(der, 2);
            fila.Children.Add(izq); fila.Children.Add(der);
            raiz.Children.Add(fila);
            raiz.Children.Add(BancoUi.Etiqueta("Cuenta contable (opcional; se usará para conciliar contra Contabilidad)")); raiz.Children.Add(_contable);
            raiz.Children.Add(_msg);

            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var guardar = BancoUi.Boton(this, "Guardar", "Primary"); guardar.Click += (s, e) => Guardar();
            var cancelar = BancoUi.Boton(this, "Cancelar", "Ghost"); cancelar.Click += (s, e) => Close();
            botones.Children.Add(guardar); botones.Children.Add(cancelar);
            raiz.Children.Add(botones);
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = raiz };
        }

        private void Guardar()
        {
            var c = new BancoCuentaFila
            {
                Id = _c.Id, Activa = true, Alias = _alias.Text.Trim(), Banco = BancoUi.Valor(_banco), Numero = _numero.Text.Trim(), Clabe = _clabe.Text.Trim(),
                Moneda = (BancoUi.Valor(_moneda) is string mo && mo.Length > 0 ? mo : "MXN").ToUpperInvariant(), Tipo = (_tipo.SelectedItem as string) == "Tarjeta de crédito" ? "Tarjeta" : "Cuenta", CuentaContable = _contable.Text.Trim()
            };
            string err = BancoDatos.ValidarCuenta(c);
            if (err != null) { _msg.Text = err; return; }
            Resultado = c; DialogResult = true;
        }
    }

    // Todas las cuentas bancarias de la empresa.
    internal sealed class BancoCuentasWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc;
        private readonly ListView _lista = new ListView();
        private readonly TextBlock _msg = new TextBlock { Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, MinHeight = 20, Margin = new Thickness(0, 8, 0, 0) };
        public bool Cambio { get; private set; }

        public BancoCuentasWindow(SqlConnection conn, string rfc)
        {
            _conn = conn; _rfc = rfc;
            Title = "Cuentas bancarias"; Width = 880; Height = 520; MinWidth = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = BancoUi.Fondo;
            BancoUi.AjustarAPantalla(this);
            var raiz = new DockPanel { Margin = new Thickness(22) };
            var encabezado = new StackPanel(); DockPanel.SetDock(encabezado, Dock.Top);
            encabezado.Children.Add(new TextBlock { Text = "Cuentas bancarias", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta });
            encabezado.Children.Add(new TextBlock { Text = "Si tienes varias cuentas del mismo banco, regístralas todas con su número: así cada estado de cuenta se asigna a la correcta (y si el archivo no dice de cuál es, tú eliges).", Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
            raiz.Children.Add(encabezado);

            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(botones, Dock.Bottom);
            var nueva = BancoUi.Boton(this, "Nueva cuenta", "Primary"); nueva.Click += (s, e) => Editar(new BancoCuentaFila(), true);
            var editar = BancoUi.Boton(this, "Editar", "Ghost"); editar.Click += (s, e) => { if (_lista.SelectedItem is BancoCuentaFila c) Editar(c, false); };
            var quitar = BancoUi.Boton(this, "Quitar", "Ghost"); quitar.Click += (s, e) => Quitar();
            var cerrar = BancoUi.Boton(this, "Cerrar", "Ghost"); cerrar.Click += (s, e) => Close();
            botones.Children.Add(nueva); botones.Children.Add(editar); botones.Children.Add(quitar); botones.Children.Add(cerrar);
            var pie = new StackPanel(); DockPanel.SetDock(pie, Dock.Bottom); pie.Children.Add(_msg); pie.Children.Add(botones);
            raiz.Children.Add(pie);

            var gv = new GridView();
            void Col(string h, string b, double w) => gv.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new System.Windows.Data.Binding(b) });
            Col("Nombre", "Alias", 190); Col("Banco", "Banco", 110); Col("Cuenta", "Numero", 120); Col("Moneda", "Moneda", 64); Col("Tipo", "Tipo", 70); Col("Movimientos", "Movimientos", 90); Col("Último", "UltimaTexto", 90);
            _lista.View = gv; _lista.MouseDoubleClick += (s, e) => { if (_lista.SelectedItem is BancoCuentaFila c) Editar(c, false); };
            raiz.Children.Add(_lista);
            Content = raiz;
            Cargar();
        }

        private void Cargar() => _lista.ItemsSource = BancoDatos.ObtenerCuentas(_conn, _rfc);

        private void Editar(BancoCuentaFila c, bool nueva)
        {
            var w = new BancoCuentaEditWindow(c, nueva) { Owner = this };
            if (w.ShowDialog() != true) return;
            try { BancoDatos.GuardarCuenta(_conn, _rfc, w.Resultado); Cambio = true; Cargar(); _msg.Text = "Guardada."; _msg.Foreground = BancoUi.Verde; }
            catch (Exception ex) { _msg.Text = "No se pudo guardar: " + ex.Message; _msg.Foreground = BancoUi.Rojo; }
        }

        private void Quitar()
        {
            if (!(_lista.SelectedItem is BancoCuentaFila c)) { _msg.Text = "Elige primero una cuenta de la lista."; _msg.Foreground = BancoUi.Suave; return; }
            if (MessageBox.Show(this, "¿Quitar la cuenta «" + c.Alias + "»?\n\nSi ya tiene movimientos cargados no se borran: la cuenta solo se da de baja.", "Cuentas bancarias", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            bool borrada = BancoDatos.EliminarOBaja(_conn, _rfc, c.Id);
            Cambio = true; Cargar();
            _msg.Text = borrada ? "Cuenta borrada." : "La cuenta tiene movimientos: se dio de baja (ya no aparece, sus movimientos se conservan)."; _msg.Foreground = BancoUi.Suave;
        }
    }

    // Historial de estados de cuenta cargados, con la opcion de deshacer una carga.
    internal sealed class BancoCargasWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc;
        private readonly ListView _lista = new ListView();
        private readonly TextBlock _msg = new TextBlock { Foreground = BancoUi.Suave, TextWrapping = TextWrapping.Wrap, MinHeight = 20, Margin = new Thickness(0, 8, 0, 0) };
        public bool Cambio { get; private set; }

        public BancoCargasWindow(SqlConnection conn, string rfc)
        {
            _conn = conn; _rfc = rfc;
            Title = "Estados de cuenta cargados"; Width = 1000; Height = 520; MinWidth = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = BancoUi.Fondo;
            BancoUi.AjustarAPantalla(this);
            var raiz = new DockPanel { Margin = new Thickness(22) };
            var enc = new TextBlock { Text = "Estados de cuenta cargados", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = BancoUi.Tinta, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(enc, Dock.Top); raiz.Children.Add(enc);
            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var deshacer = BancoUi.Boton(this, "Deshacer la carga seleccionada", "Ghost"); deshacer.Click += (s, e) => Deshacer();
            var cerrar = BancoUi.Boton(this, "Cerrar", "Ghost"); cerrar.Click += (s, e) => Close();
            botones.Children.Add(deshacer); botones.Children.Add(cerrar);
            var pie = new StackPanel(); DockPanel.SetDock(pie, Dock.Bottom); pie.Children.Add(_msg); pie.Children.Add(botones); raiz.Children.Add(pie);

            var gv = new GridView();
            void Col(string h, string b, double w, string fmt = null) => gv.Columns.Add(new GridViewColumn { Header = h, Width = w, DisplayMemberBinding = new System.Windows.Data.Binding(b) { StringFormat = fmt } });
            Col("Cargado", "Fecha", 130, "yyyy-MM-dd HH:mm"); Col("Cuenta", "Cuenta", 150); Col("Archivo", "Archivo", 210); Col("Periodo", "Periodo", 170);
            Col("Leídos", "Leidos", 60); Col("Nuevos", "Nuevos", 60); Col("Ya estaban", "Duplicados", 80); Col("Saldo inicial", "SaldoInicial", 100, "N2"); Col("Saldo final", "SaldoFinal", 100, "N2");
            _lista.View = gv; raiz.Children.Add(_lista);
            Content = raiz;
            Cargar();
        }

        private void Cargar() => _lista.ItemsSource = BancoDatos.Importaciones(_conn, _rfc);

        private void Deshacer()
        {
            if (!(_lista.SelectedItem is BancoImportacionFila f)) { _msg.Text = "Elige primero una carga de la lista."; _msg.Foreground = BancoUi.Suave; return; }
            if (MessageBox.Show(this, "Se borrarán los " + f.Nuevos + " movimientos que trajo «" + f.Archivo + "» a la cuenta " + f.Cuenta + ".\n\nEl archivo original no se toca; puedes volver a cargarlo. ¿Continuar?", "Deshacer carga", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var r = BancoDatos.DeshacerImportacion(_conn, _rfc, f.Id);
            if (!r.ok) { _msg.Text = r.error; _msg.Foreground = BancoUi.Rojo; return; }
            Cambio = true; Cargar(); _msg.Text = r.borrados + " movimientos borrados."; _msg.Foreground = BancoUi.Verde;
        }
    }
}
