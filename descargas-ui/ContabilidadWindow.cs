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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // A que empresa de CONTPAQi Contabilidad se cargan los XML de esta empresa, y con que usuario. La clave se guarda cifrada (DPAPI del equipo).
    internal sealed class ContabilidadWindow : Window
    {
        private readonly SqlConnection _conn; private readonly string _rfc;
        private readonly ComboBox _empresa = new ComboBox(); private readonly TextBox _usuario = new TextBox();
        private readonly PasswordBox _clave = new PasswordBox(); private readonly CheckBox _auto = new CheckBox();
        private readonly TextBlock _estado = new TextBlock(); private readonly ContabilidadConfig _actual;
        public bool Cambio { get; private set; }

        public ContabilidadWindow(SqlConnection conn, string rfc, string nombreEmpresa)
        {
            _conn = conn; _rfc = rfc; _actual = BrosSatDb.ObtenerContabilidad(conn, rfc);
            Title = "Contabilidad · " + nombreEmpresa; Width = 560; Height = 470; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(244, 246, 249));
            var raiz = new StackPanel { Margin = new Thickness(24) };
            raiz.Children.Add(new TextBlock { Text = "Cargar los XML al ADD de CONTPAQi Contabilidad", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Tinta, Margin = new Thickness(0, 0, 0, 4) });
            raiz.Children.Add(new TextBlock
            {
                Text = "Elige la empresa de Contabilidad que corresponde a este RFC y el usuario con el que BrosLMV entra. Los XML se cargan igual que con el importador de CONTPAQi, sin abrir Contabilidad.",
                TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), Margin = new Thickness(0, 0, 0, 14)
            });
            _empresa.MinHeight = 30;
            Etiquetar(raiz, "Empresa de Contabilidad", _empresa);
            _usuario.Text = _actual?.Usuario ?? "SUPERVISOR"; _usuario.MinHeight = 28; Etiquetar(raiz, "Usuario de Contabilidad", _usuario);
            _clave.MinHeight = 28; Etiquetar(raiz, "Contraseña (déjala vacía si el usuario no tiene)", _clave);
            if (_actual?.ClaveCifrada != null && _actual.ClaveCifrada.Length > 0) _clave.Password = ContabilidadAdd.DescifrarClave(_actual);
            _auto.Content = "Cargar automáticamente lo nuevo después de cada descarga"; _auto.IsChecked = _actual?.CargaAutomatica ?? true; _auto.Margin = new Thickness(0, 2, 0, 8);
            raiz.Children.Add(_auto);
            _estado.TextWrapping = TextWrapping.Wrap; _estado.MinHeight = 40; _estado.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)); raiz.Children.Add(_estado);

            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var probar = Boton("Probar acceso", "Ghost"); probar.Click += async (s, e) => await ProbarAsync();
            var guardar = Boton("Guardar", "Primary"); guardar.Click += (s, e) => Guardar();
            var quitar = Boton("Quitar", "Subtle"); quitar.IsEnabled = _actual != null; quitar.Click += (s, e) => Quitar();
            var cerrar = Boton("Cerrar", "Ghost"); cerrar.Margin = new Thickness(0); cerrar.Click += (s, e) => { DialogResult = Cambio; };
            botones.Children.Add(quitar); botones.Children.Add(probar); botones.Children.Add(guardar); botones.Children.Add(cerrar);
            raiz.Children.Add(botones);
            Content = raiz;
            Loaded += (s, e) => CargarEmpresas();
        }

        private static readonly Brush Tinta = new SolidColorBrush(Color.FromRgb(22, 38, 58));

        private Button Boton(string texto, string estilo) => new Button { Content = texto, Style = (Style)FindResource(estilo), Margin = new Thickness(0, 0, 8, 0) };

        private static void Etiquetar(Panel p, string texto, Control c)
        {
            p.Children.Add(new TextBlock { Text = texto, FontWeight = FontWeights.SemiBold, Foreground = Tinta, Margin = new Thickness(0, 0, 0, 4) });
            c.Margin = new Thickness(0, 0, 0, 10); p.Children.Add(c);
        }

        private void CargarEmpresas()
        {
            try
            {
                var lista = ContabilidadAdd.ListarEmpresas(_conn);
                _empresa.ItemsSource = lista;
                // Primero la que ya estaba elegida; si no, la que tiene el mismo RFC (lo normal).
                _empresa.SelectedItem = lista.FirstOrDefault(x => _actual != null && string.Equals(x.BaseDatos, _actual.BaseDatos, StringComparison.OrdinalIgnoreCase))
                                      ?? lista.FirstOrDefault(x => string.Equals(x.Rfc, _rfc, StringComparison.OrdinalIgnoreCase));
                if (lista.Count == 0) Estado("✗ No se encontraron empresas de Contabilidad en este servidor SQL.", true);
                else if (_empresa.SelectedItem == null) Estado("Ninguna empresa de Contabilidad tiene el RFC " + _rfc + ". Elige una de la lista.", false);
            }
            catch (Exception ex) { Estado("✗ No se pudo leer la lista de empresas de Contabilidad: " + ex.Message, true); }
        }

        private void Estado(string texto, bool error) { _estado.Text = texto; _estado.Foreground = error ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(71, 85, 105)); }

        private ContabilidadConfig Leer()
        {
            var emp = _empresa.SelectedItem as EmpresaContabilidadInfo;
            if (emp == null) { Estado("✗ Elige la empresa de Contabilidad.", true); return null; }
            if (string.IsNullOrWhiteSpace(_usuario.Text)) { Estado("✗ Escribe el usuario de Contabilidad.", true); return null; }
            return new ContabilidadConfig
            {
                RFC = _rfc, BaseDatos = emp.BaseDatos, Usuario = _usuario.Text.Trim(),
                ClaveCifrada = string.IsNullOrEmpty(_clave.Password) ? null : DpapiHelper.Cifrar(_clave.Password),
                CargaAutomatica = _auto.IsChecked == true
            };
        }

        private async System.Threading.Tasks.Task ProbarAsync()
        {
            var cfg = Leer(); if (cfg == null) return;
            var emp = (EmpresaContabilidadInfo)_empresa.SelectedItem;
            if (!string.Equals(emp.Rfc, _rfc, StringComparison.OrdinalIgnoreCase))
            { Estado("✗ El RFC de esa empresa de Contabilidad (" + emp.Rfc + ") no es el de esta empresa (" + _rfc + ").", true); return; }
            Estado("Probando acceso...", false); Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                string error = await ContabilidadAdd.ProbarAccesoAsync(_conn, cfg);
                if (error == null) Estado("✓ Acceso correcto. Listo para cargar a «" + emp.Nombre + "».", false); else Estado("✗ " + error, true);
            }
            catch (Exception ex) { Estado("✗ " + ex.Message, true); }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        private void Guardar()
        {
            var cfg = Leer(); if (cfg == null) return;
            var emp = (EmpresaContabilidadInfo)_empresa.SelectedItem;
            if (!string.Equals(emp.Rfc, _rfc, StringComparison.OrdinalIgnoreCase))
            { Estado("✗ El RFC de esa empresa de Contabilidad (" + emp.Rfc + ") no es el de esta empresa (" + _rfc + ").", true); return; }
            try { BrosSatDb.GuardarContabilidad(_conn, cfg); Cambio = true; DialogResult = true; }
            catch (Exception ex) { Estado("✗ " + ex.Message, true); }
        }

        private void Quitar()
        {
            if (MessageBox.Show(this, "Se deja de cargar a Contabilidad. Lo ya cargado se queda en el ADD.", "Quitar", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            BrosSatDb.QuitarContabilidad(_conn, _rfc); Cambio = true; DialogResult = true;
        }
    }
}
