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
using System.Windows.Input;

namespace BrosLMV.DescargasUI
{
    public partial class PasswordWindow : Window
    {
        public string Password { get; private set; }

        public PasswordWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => PwdBox.Focus();
        }

        private void Entrar_Click(object sender, RoutedEventArgs e) => Aceptar();

        private void PwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Aceptar();
        }

        private void Aceptar()
        {
            Password = PwdBox.Password;
            DialogResult = true;
            Close();
        }
    }
}
