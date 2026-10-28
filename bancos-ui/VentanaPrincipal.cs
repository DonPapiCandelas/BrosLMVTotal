// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Bancos.Carga;

namespace BrosLMV.BancosUI
{
    // Marco de la aplicacion: cinta oscura como la de Comercial, dos paginas (Bandeja y Layouts).
    public class VentanaPrincipal : Window
    {
        private readonly Contexto _ctx;
        private readonly PaginaBandeja _bandeja;
        private PaginaLayouts _layouts;
        private readonly ContentControl _host = new ContentControl();
        private Button _tabBandeja, _tabLayouts;

        public PaginaBandeja Bandeja { get { return _bandeja; } }
        public Contexto Ctx { get { return _ctx; } }

        public VentanaPrincipal(Contexto ctx)
        {
            _ctx = ctx;
            Title = "Bancos · " + (ctx.NombreEmpresa.Length > 0 ? ctx.NombreEmpresa : ctx.BaseDeDatos);
            Width = 1120; Height = 780; MinWidth = 900; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Tema.Pagina; FontFamily = new FontFamily("Segoe UI"); Foreground = Tema.Texto;

            _bandeja = new PaginaBandeja(ctx, this);
            _layouts = new PaginaLayouts(ctx, this);

            var raiz = new DockPanel();
            var cinta = new DockPanel { Background = Tema.Navy, Height = 46 };
            DockPanel.SetDock(cinta, Dock.Top);
            var titulo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titulo.Children.Add(Tema.Txt("Bancos", 15, true, Brushes.White));
            titulo.Children.Add(Tema.Txt("   " + (ctx.NombreEmpresa.Length > 0 ? ctx.NombreEmpresa : ctx.BaseDeDatos), 13, false, Tema.Pincel("#AEB8C8")));
            cinta.Children.Add(titulo);

            var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            _tabBandeja = Pestana("Bandeja", () => Mostrar(true)); _tabLayouts = Pestana("Layouts", () => Mostrar(false));
            tabs.Children.Add(_tabBandeja); tabs.Children.Add(_tabLayouts);
            DockPanel.SetDock(tabs, Dock.Right); cinta.Children.Insert(0, tabs);
            raiz.Children.Add(cinta);
            raiz.Children.Add(_host);
            Content = raiz;
            Mostrar(true);
            Loaded += async (s, e) => { await _bandeja.IniciarAsync(); _layouts.Refrescar(); };
        }

        private Button Pestana(string texto, Action clic)
        {
            var b = new Button { Content = texto, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(2, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13, BorderThickness = new Thickness(0) };
            b.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
            b.Click += (s, e) => clic();
            return b;
        }

        private void Mostrar(bool bandeja)
        {
            _host.Content = bandeja ? (UIElement)_bandeja : _layouts;
            Estilo(_tabBandeja, bandeja); Estilo(_tabLayouts, !bandeja);
            if (!bandeja) _layouts.Refrescar();
        }

        private static void Estilo(Button b, bool activa)
        {
            b.Background = activa ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) : Brushes.Transparent;
            b.Foreground = activa ? Tema.Navy : Brushes.White;
        }

        public void AbrirLayouts() { Mostrar(false); }

        // Abre el editor con un archivo de muestra (por ejemplo uno que ningun layout reconocio).
        public void AbrirEditor(AnalisisArchivo muestra)
        {
            Mostrar(false);
            _layouts.NuevoDesdeArchivo(muestra);
        }
    }
}
