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
using System.Windows.Markup;

namespace BrosLMV.DescargasUI.Controles
{
    // Encabezado estandar de toda pagina: titulo, descripcion de una linea y, a la derecha, las acciones de la pagina. Se arma en codigo
    // (sin XAML propio) a proposito: asi los botones que una pagina pone dentro como acciones pueden llevar x:Name en el XAML de esa pagina.
    [ContentProperty("Acciones")]
    public class EncabezadoPagina : UserControl
    {
        private readonly TextBlock _titulo = new TextBlock();
        private readonly TextBlock _subtitulo = new TextBlock { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        private readonly ContentPresenter _acciones = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };

        public EncabezadoPagina()
        {
            _titulo.Style = (Style)Application.Current.FindResource("TextoDisplay");
            _titulo.TextTrimming = TextTrimming.CharacterEllipsis;
            _subtitulo.Style = (Style)Application.Current.FindResource("TextoSecundario");
            var texto = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texto.Children.Add(_titulo);
            texto.Children.Add(_subtitulo);
            var rejilla = new Grid();
            rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_acciones, 1);
            rejilla.Children.Add(texto);
            rejilla.Children.Add(_acciones);
            Content = rejilla;
        }

        public static readonly DependencyProperty TituloProperty = DependencyProperty.Register("Titulo", typeof(string), typeof(EncabezadoPagina),
            new PropertyMetadata(null, (d, e) => ((EncabezadoPagina)d)._titulo.Text = (string)e.NewValue));
        public static readonly DependencyProperty SubtituloProperty = DependencyProperty.Register("Subtitulo", typeof(string), typeof(EncabezadoPagina),
            new PropertyMetadata(null, (d, e) =>
            {
                var c = (EncabezadoPagina)d; var t = (string)e.NewValue;
                c._subtitulo.Text = t; c._subtitulo.Visibility = string.IsNullOrEmpty(t) ? Visibility.Collapsed : Visibility.Visible;
            }));
        public static readonly DependencyProperty AccionesProperty = DependencyProperty.Register("Acciones", typeof(object), typeof(EncabezadoPagina),
            new PropertyMetadata(null, (d, e) => ((EncabezadoPagina)d)._acciones.Content = e.NewValue));

        public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
        public string Subtitulo { get => (string)GetValue(SubtituloProperty); set => SetValue(SubtituloProperty, value); }
        public object Acciones { get => GetValue(AccionesProperty); set => SetValue(AccionesProperty, value); }
    }
}
