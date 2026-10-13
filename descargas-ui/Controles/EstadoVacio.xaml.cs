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
using System.Windows.Controls;

namespace BrosLMV.DescargasUI.Controles
{
    public partial class EstadoVacio : UserControl
    {
        public EstadoVacio() { InitializeComponent(); }

        public static readonly DependencyProperty GlifoProperty = DependencyProperty.Register("Glifo", typeof(string), typeof(EstadoVacio), new PropertyMetadata(G.Palomita));
        public static readonly DependencyProperty TituloProperty = DependencyProperty.Register("Titulo", typeof(string), typeof(EstadoVacio));
        public static readonly DependencyProperty TextoProperty = DependencyProperty.Register("Texto", typeof(string), typeof(EstadoVacio));

        public string Glifo { get => (string)GetValue(GlifoProperty); set => SetValue(GlifoProperty, value); }
        public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
        public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }
    }
}
