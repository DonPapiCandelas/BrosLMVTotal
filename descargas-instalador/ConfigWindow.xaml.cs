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
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Instalador
{
    public partial class ConfigWindow : Window
    {
        public string CadenaConexion { get; private set; }
        public TimeSpan IntervaloSolicitar { get; private set; }

        public ConfigWindow()
        {
            InitializeComponent();
            CargarConfiguracionExistente();
        }

        // Una actualización no debe obligar a reconstruir la conexión ni, peor aún, dejar el
        // servicio apuntando a otra base por aceptar los valores de ejemplo. servicio.json es la
        // fuente de verdad compartida por la app y el servicio instalado.
        private void CargarConfiguracionExistente()
        {
            var existente = ConfigServicio.Cargar();
            if (existente == null || string.IsNullOrWhiteSpace(existente.CadenaConexion)) return;
            try
            {
                var csb = new SqlConnectionStringBuilder(existente.CadenaConexion);
                TxtServidor.Text = csb.DataSource;
                TxtBaseDatos.Text = csb.InitialCatalog;
                bool windows = csb.IntegratedSecurity;
                ChkWindowsAuth.IsChecked = windows;
                if (!windows)
                {
                    TxtUsuario.Text = csb.UserID;
                    PwdContrasena.Password = csb.Password;
                }
                int minutos = Math.Max(15, existente.IntervaloSolicitarMinutos);
                CmbIntervalo.SelectedItem = CmbIntervalo.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => (string)i.Tag == minutos.ToString())
                    ?? CmbIntervalo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == "120");
                LblEstadoConexion.Text = "Configuración actual cargada. Confirma o ajusta y continúa.";
                LblEstadoConexion.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x5A));
            }
            catch
            {
                // Un archivo viejo/corrupto no bloquea una instalación nueva: se conservan los
                // valores predeterminados y el usuario puede capturar la conexión otra vez.
            }
        }

        void ChkWindowsAuth_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSqlAuth == null) return;
            bool windowsAuth = ChkWindowsAuth.IsChecked == true;
            PanelSqlAuth.IsEnabled = !windowsAuth;
            PanelSqlAuth.Opacity = windowsAuth ? 0.5 : 1;
        }

        // Se conecta a "master" (no a la BD final, que todavia no existe la primera vez) --
        // mismo motivo que EsquemaSql.AsegurarBaseDeDatos: SQL Server no deja abrir una conexion
        // contra una base inexistente.
        string ArmarCadenaMaster()
        {
            var csb = new SqlConnectionStringBuilder
            {
                DataSource = TxtServidor.Text.Trim(),
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };
            if (ChkWindowsAuth.IsChecked == true) csb.IntegratedSecurity = true;
            else { csb.UserID = TxtUsuario.Text.Trim(); csb.Password = PwdContrasena.Password; }
            return csb.ConnectionString;
        }

        string ArmarCadenaFinal()
        {
            var csb = new SqlConnectionStringBuilder(ArmarCadenaMaster()) { InitialCatalog = TxtBaseDatos.Text.Trim() };
            return csb.ConnectionString;
        }

        void BtnProbar_Click(object sender, RoutedEventArgs e)
        {
            if (!Validar()) return;
            try
            {
                using (var cn = new SqlConnection(ArmarCadenaMaster())) cn.Open();
                LblEstadoConexion.Text = "✓ Conexión exitosa.";
                LblEstadoConexion.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x5A));
            }
            catch (Exception ex)
            {
                LblEstadoConexion.Text = "✕ " + ex.Message;
                LblEstadoConexion.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }
        }

        bool Validar()
        {
            if (string.IsNullOrWhiteSpace(TxtServidor.Text) || string.IsNullOrWhiteSpace(TxtBaseDatos.Text))
            {
                MessageBox.Show(this, "Escribe el servidor y la base de datos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (ChkWindowsAuth.IsChecked != true && string.IsNullOrWhiteSpace(TxtUsuario.Text))
            {
                MessageBox.Show(this, "Escribe el usuario SQL, o marca autenticación de Windows.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        void BtnCancelar_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

        void BtnInstalar_Click(object sender, RoutedEventArgs e)
        {
            if (!Validar()) return;
            if (CmbIntervalo.SelectedItem is not ComboBoxItem item)
            {
                MessageBox.Show(this, "Selecciona cada cuánto solicitar periodos nuevos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CadenaConexion = ArmarCadenaFinal();
            IntervaloSolicitar = TimeSpan.FromMinutes(int.Parse((string)item.Tag));
            DialogResult = true;
        }
    }
}
