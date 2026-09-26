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

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace BrosLMV.Descargas.Instalador
{
    public partial class ResultWindow : Window
    {
        readonly bool _ok;

        public ResultWindow(string resumen, bool ok)
        {
            InitializeComponent();
            _ok = ok;
            LblTitulo.Text = ok ? "Instalación terminada" : "La instalación tuvo problemas";
            LblTitulo.Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x16, 0x26, 0x3A) : Color.FromRgb(0xDC, 0x26, 0x26));
            LblResumen.Text = resumen;
            BtnAbrir.IsEnabled = ok;
        }

        void BtnAbrir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    System.IO.Path.Combine(Instalador.CarpetaUi, "BrosLMV.DescargasUI.exe"))
                { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la aplicación:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Close();
        }

        void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
