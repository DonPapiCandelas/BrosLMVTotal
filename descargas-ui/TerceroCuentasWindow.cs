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
    // Las cuentas de UN tercero: una fila por rol y moneda (provision en pesos, en dolares, anticipo, gasto...). Lo que se agrega aqui es «Manual».
    internal sealed class TerceroCuentasWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc; private readonly ContaTerceroFila _t;
        private readonly List<ContaCuenta> _cuentas;
        private readonly ListView _lista = new ListView();
        private readonly ComboBox _rol = new ComboBox(), _moneda = new ComboBox { IsEditable = true }, _cuenta = new ComboBox { IsEditable = true };
        private readonly TextBlock _msg = new TextBlock();
        public bool Cambio { get; private set; }

        private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(22, 38, 58));
        private static readonly Brush Suave = new SolidColorBrush(Color.FromRgb(71, 85, 105));

        public TerceroCuentasWindow(SqlConnection conn, string rfc, ContaTerceroFila t)
        {
            _conn = conn; _rfc = rfc; _t = t; _cuentas = ContaDatos.ObtenerCuentas(conn, rfc, true);
            Title = "Cuentas de " + (string.IsNullOrWhiteSpace(t.Nombre) ? t.Rfc : t.Nombre);
            Width = 760; Height = 560; MinWidth = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(244, 246, 249));
            var raiz = new StackPanel { Margin = new Thickness(22) };
            raiz.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(t.Nombre) ? "(sin nombre)" : t.Nombre, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Tinta });
            raiz.Children.Add(new TextBlock { Text = (string.IsNullOrEmpty(t.Rfc) ? "Sin RFC" : t.Rfc) + " · " + t.Tipo + " · " + t.NumCfdi + " CFDI" + (t.Origen == "CFDI" ? " · no está en Contabilidad" : ""), Foreground = Suave, Margin = new Thickness(0, 2, 0, 12) });

            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Rol", Width = 170, DisplayMemberBinding = new System.Windows.Data.Binding("Rol") });
            gv.Columns.Add(new GridViewColumn { Header = "Moneda", Width = 70, DisplayMemberBinding = new System.Windows.Data.Binding("Moneda") });
            gv.Columns.Add(new GridViewColumn { Header = "Cuenta", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding("Cuenta") });
            gv.Columns.Add(new GridViewColumn { Header = "Nombre de la cuenta", Width = 190, DisplayMemberBinding = new System.Windows.Data.Binding("NombreCuenta") });
            gv.Columns.Add(new GridViewColumn { Header = "Origen", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("Origen") });
            _lista.View = gv; _lista.Height = 190; _lista.SelectionChanged += (s, e) => Seleccionada();
            raiz.Children.Add(_lista);

            raiz.Children.Add(new TextBlock { Text = "Agregar o cambiar una cuenta", FontWeight = FontWeights.SemiBold, Foreground = Tinta, Margin = new Thickness(0, 14, 0, 6) });
            _rol.ItemsSource = ContaRoles.Todos; _rol.SelectedIndex = t.EsCliente && !t.EsProveedor ? Array.IndexOf(ContaRoles.Todos, ContaRoles.Cliente) : 0; _rol.MinHeight = 28;
            _moneda.ItemsSource = ContaRoles.Monedas; _moneda.SelectedItem = "MXN"; _moneda.MinHeight = 28;
            _cuenta.ItemsSource = _cuentas; _cuenta.MinHeight = 28; _cuenta.IsTextSearchEnabled = false;
            var fila = new Grid();
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_moneda, 1); Grid.SetColumn(_cuenta, 2);
            _rol.Margin = new Thickness(0, 0, 8, 0); _moneda.Margin = new Thickness(0, 0, 8, 0);
            fila.Children.Add(_rol); fila.Children.Add(_moneda); fila.Children.Add(_cuenta);
            raiz.Children.Add(fila);
            raiz.Children.Add(new TextBlock { Text = "Escribe el código de la cuenta (o parte del nombre) y elígela de la lista. Debe existir, ser afectable y de la misma moneda.", Foreground = Suave, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
            _cuenta.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Filtrar()));

            _msg.TextWrapping = TextWrapping.Wrap; _msg.MinHeight = 38; _msg.Foreground = Suave; raiz.Children.Add(_msg);
            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var guardar = Boton("Guardar cuenta", "Primary"); guardar.Click += (s, e) => Guardar();
            var borrar = Boton("Quitar la seleccionada", "Ghost"); borrar.Click += (s, e) => Quitar();
            var cerrar = Boton("Cerrar", "Ghost"); cerrar.Click += (s, e) => Close();
            botones.Children.Add(borrar); botones.Children.Add(guardar); botones.Children.Add(cerrar);
            raiz.Children.Add(botones);
            Content = raiz;
            Refrescar();
        }

        private Button Boton(string texto, string estilo) => new Button { Content = texto, Style = (Style)FindResource(estilo), Margin = new Thickness(0, 0, 8, 0) };

        public sealed class Fila { public string Rol { get; set; } public string Moneda { get; set; } public string Cuenta { get; set; } public string NombreCuenta { get; set; } public string Origen { get; set; } }

        private void Refrescar()
        {
            var nombres = _cuentas.ToDictionary(c => c.Codigo, c => c.Nombre);
            _lista.ItemsSource = _t.Asignaciones.OrderBy(a => a.Rol).ThenBy(a => a.Moneda).Select(a => new Fila { Rol = a.Rol, Moneda = a.Moneda, Cuenta = a.Cuenta, NombreCuenta = nombres.TryGetValue(a.Cuenta, out var n) ? n : "(no está en el catálogo)", Origen = a.Origen }).ToList();
        }

        private void Seleccionada()
        {
            if (!(_lista.SelectedItem is Fila f)) return;
            _rol.SelectedItem = f.Rol; _moneda.Text = f.Moneda;
            _cuenta.SelectedItem = _cuentas.FirstOrDefault(c => c.Codigo == f.Cuenta);
        }

        private bool _filtrando;
        private void Filtrar()
        {
            if (_filtrando) return;
            string txt = (_cuenta.Text ?? "").Trim();
            if (txt.Length < 2 || _cuenta.SelectedItem != null && _cuenta.SelectedItem.ToString() == txt) return;
            _filtrando = true;
            try
            {
                string u = txt.ToUpperInvariant();
                var coin = _cuentas.Where(c => c.Codigo.StartsWith(u) || c.Nombre.ToUpperInvariant().Contains(u)).Take(80).ToList();
                _cuenta.ItemsSource = coin; _cuenta.Text = txt; _cuenta.IsDropDownOpen = coin.Count > 0;
                var tb = (TextBox)_cuenta.Template.FindName("PART_EditableTextBox", _cuenta); if (tb != null) tb.CaretIndex = txt.Length;
            }
            finally { _filtrando = false; }
        }

        private string CodigoElegido()
        {
            if (_cuenta.SelectedItem is ContaCuenta c) return c.Codigo;
            string t = (_cuenta.Text ?? "").Trim(); int i = t.IndexOf(" — ", StringComparison.Ordinal);
            return i > 0 ? t.Substring(0, i).Trim() : t;
        }

        private void Guardar()
        {
            string rol = _rol.SelectedItem as string, mon = (_moneda.Text ?? "").Trim().ToUpperInvariant(), cuenta = CodigoElegido();
            var (err, aviso) = ContaDatos.Validar(_conn, _rfc, rol, mon, cuenta);
            if (err != null) { _msg.Text = err; _msg.Foreground = Brushes.Firebrick; return; }
            ContaDatos.GuardarAsignacion(_conn, _rfc, _t.Clave, rol, mon, cuenta);
            _t.Asignaciones.RemoveAll(a => a.Rol == rol && a.Moneda == mon);
            _t.Asignaciones.Add(new ContaAsignacion { Rol = rol, Moneda = mon, Cuenta = cuenta, Origen = "Manual" });
            Cambio = true; Refrescar();
            _msg.Text = "Guardada." + (aviso != null ? " Aviso: " + aviso : ""); _msg.Foreground = aviso != null ? Brushes.DarkGoldenrod : Brushes.SeaGreen;
        }

        private void Quitar()
        {
            if (!(_lista.SelectedItem is Fila f)) { _msg.Text = "Elige primero una fila de la lista."; _msg.Foreground = Suave; return; }
            ContaDatos.BorrarAsignacion(_conn, _rfc, _t.Clave, f.Rol, f.Moneda);
            _t.Asignaciones.RemoveAll(a => a.Rol == f.Rol && a.Moneda == f.Moneda);
            Cambio = true; Refrescar();
            _msg.Text = f.Origen == "Contabilidad" ? "Quitada. Si la cuenta viene de Contabilidad, volverá al sincronizar (para cambiarla de verdad, cámbiala en Contabilidad o escribe otra aquí)." : "Quitada.";
            _msg.Foreground = Suave;
        }
    }
}
