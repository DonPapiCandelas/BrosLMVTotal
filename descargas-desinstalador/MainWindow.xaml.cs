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
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Desinstalador
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        void ChkBorrarBd_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelBd == null) return;
            PanelBd.Visibility = ChkBorrarBd.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        void ChkWindowsAuth_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSqlAuth == null) return;
            bool windowsAuth = ChkWindowsAuth.IsChecked == true;
            PanelSqlAuth.IsEnabled = !windowsAuth;
            PanelSqlAuth.Opacity = windowsAuth ? 0.5 : 1;
        }

        void BtnCancelar_Click(object sender, RoutedEventArgs e) => Close();
        void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();

        async void BtnDesinstalar_Click(object sender, RoutedEventArgs e)
        {
            bool borrarBd = ChkBorrarBd.IsChecked == true;
            string cadenaMaster = null, nombreBase = null;

            if (borrarBd)
            {
                if (string.IsNullOrWhiteSpace(TxtServidor.Text) || string.IsNullOrWhiteSpace(TxtBaseDatos.Text))
                {
                    MessageBox.Show(this, "Escribe el servidor y la base de datos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var csb = new SqlConnectionStringBuilder
                {
                    DataSource = TxtServidor.Text.Trim(),
                    InitialCatalog = "master",
                    TrustServerCertificate = true,
                    ConnectTimeout = 10
                };
                if (ChkWindowsAuth.IsChecked == true) csb.IntegratedSecurity = true;
                else { csb.UserID = TxtUsuario.Text.Trim(); csb.Password = PwdContrasena.Password; }
                cadenaMaster = csb.ConnectionString;
                nombreBase = TxtBaseDatos.Text.Trim();

                var r = MessageBox.Show(this,
                    "Esto borra PERMANENTEMENTE la base de datos \"" + nombreBase + "\" -- todas las empresas, la FIEL cifrada, la cola de solicitudes y el historial de CFDI. No se puede deshacer.\n\n¿Confirmas?",
                    "Confirmar borrado de base de datos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            BtnDesinstalar.IsEnabled = false;
            string resumen;
            try
            {
                resumen = await Task.Run(() => Desinstalador.Ejecutar(borrarBd, cadenaMaster, nombreBase));
            }
            catch (Exception ex)
            {
                resumen = "No se pudo completar la desinstalación:\n\n" + ex.Message;
            }

            PanelConfirmar.Visibility = Visibility.Collapsed;
            PanelResultado.Visibility = Visibility.Visible;
            PanelBotonesConfirmar.Visibility = Visibility.Collapsed;
            PanelBotonesResultado.Visibility = Visibility.Visible;
            LblResumen.Text = resumen;
        }
    }
}
