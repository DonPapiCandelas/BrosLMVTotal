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

using System.Windows;
using Microsoft.Win32;

namespace BrosLMV.DescargasUI
{
    public partial class ConfigWindow : Window
    {
        internal Configuracion Resultado { get; private set; }

        internal ConfigWindow(Configuracion existente = null)
        {
            InitializeComponent();
            if (existente != null)
            {
                TxtCer.Text = existente.RutaCer;
                TxtKey.Text = existente.RutaKey;
                TxtRfc.Text = existente.Rfc;
                TxtConn.Text = existente.CadenaConexion;
            }
        }

        private void ExaminarCer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Certificado (*.cer)|*.cer|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true) TxtCer.Text = dlg.FileName;
        }

        private void ExaminarKey_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Llave privada (*.key)|*.key|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true) TxtKey.Text = dlg.FileName;
        }

        private void Guardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtCer.Text) || string.IsNullOrWhiteSpace(TxtKey.Text) ||
                string.IsNullOrWhiteSpace(TxtRfc.Text) || string.IsNullOrWhiteSpace(TxtConn.Text))
            {
                MessageBox.Show(this, "Completa todos los campos.", "Falta informacion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Resultado = new Configuracion
            {
                RutaCer = TxtCer.Text.Trim(),
                RutaKey = TxtKey.Text.Trim(),
                Rfc = TxtRfc.Text.Trim(),
                CadenaConexion = TxtConn.Text.Trim()
            };
            Resultado.Guardar();
            DialogResult = true;
            Close();
        }
    }
}
