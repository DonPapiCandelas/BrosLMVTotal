// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrosLMV.BancosUI
{
    public enum Tono { Neutro, Bueno, Aviso, Malo, Acento }

    // Piezas visuales compartidas: mismo lenguaje en todas las ventanas.
    public static class Tema
    {
        public static SolidColorBrush Pincel(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }

        public static readonly Brush Navy = Pincel("#1F2A3C"), Pagina = Pincel("#F4F5F7"), Superficie = Brushes.White, Linea = Pincel("#E2E5EA"), Texto = Pincel("#1B2430"), Texto2 = Pincel("#5B6675"),
            Acento = Pincel("#2D6FE0"), AcentoFondo = Pincel("#E8F0FD"), Hover = Pincel("#F1F3F6");

        public static void Colores(Tono t, out Brush fondo, out Brush texto)
        {
            switch (t)
            {
                case Tono.Bueno: fondo = Pincel("#E3F4EA"); texto = Pincel("#17623B"); break;
                case Tono.Aviso: fondo = Pincel("#FFF1D6"); texto = Pincel("#7A4E00"); break;
                case Tono.Malo: fondo = Pincel("#FCEBEB"); texto = Pincel("#9A2A2A"); break;
                case Tono.Acento: fondo = AcentoFondo; texto = Pincel("#1F55B5"); break;
                default: fondo = Pincel("#EEF0F3"); texto = Texto2; break;
            }
        }

        public static TextBlock Txt(string texto, double tam = 13, bool negrita = false, Brush color = null, TextWrapping ajuste = TextWrapping.NoWrap)
        {
            return new TextBlock { Text = texto, FontSize = tam, FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color ?? Texto, TextWrapping = ajuste, TextTrimming = ajuste == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None, VerticalAlignment = VerticalAlignment.Center };
        }

        public static Border Chip(string texto, Tono tono = Tono.Neutro, double tam = 12)
        {
            Brush f, t; Colores(tono, out f, out t);
            return new Border { Background = f, CornerRadius = new CornerRadius(99), Padding = new Thickness(10, 2, 10, 3), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = texto, FontSize = tam, Foreground = t, FontFamily = new FontFamily("Segoe UI") } };
        }

        public static Button Boton(string texto, RoutedEventHandler clic, bool primario = false, bool solotexto = false)
        {
            var b = new Button { Content = texto };
            b.Style = (Style)Application.Current.Resources[primario ? "BotonPrimario" : solotexto ? "BotonTexto" : "BotonBase"];
            if (clic != null) b.Click += clic;
            return b;
        }

        // Iniciales con un color estable por banco: se reconoce de un vistazo.
        public static Border Avatar(string banco)
        {
            string[] fondos = { "#E8F0FD", "#EDE9FB", "#FCE9E4", "#E3F4EA", "#FFF1D6", "#E4F2F6" }, textos = { "#1F55B5", "#4B3AA8", "#9B3F22", "#17623B", "#7A4E00", "#12627A" };
            int i = string.IsNullOrEmpty(banco) ? 5 : Math.Abs(banco.ToLowerInvariant().GetHashCode()) % fondos.Length;
            string ini = string.IsNullOrWhiteSpace(banco) ? "?" : banco.Trim().Substring(0, Math.Min(2, banco.Trim().Length)).ToUpperInvariant();
            return new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = Pincel(fondos[i]), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = ini, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Pincel(textos[i]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        public static string Dinero(decimal d) { return d.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
