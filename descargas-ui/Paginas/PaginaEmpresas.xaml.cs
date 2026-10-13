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

// PaginaEmpresas.xaml.cs -- catalogo de empresas: alta, edicion, baja, respaldo cifrado y la vigencia de cada e.firma.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaEmpresas : UserControl
    {
        private readonly Sesion _s;
        private readonly Action<string> _navegar;

        // Fila de la tabla: la empresa y lo que se muestra de ella ya calculado.
        public sealed class EmpresaVista
        {
            public EmpresaRow Fila { get; set; }
            public string TextoFirma { get; set; }
            public Style EstiloFirma { get; set; }
            public string TextoComercial { get; set; }
            public string TextoCfdi => Fila.TotalCfdi.ToString("N0");
            public Visibility VisibleActiva { get; set; }
        }

        public PaginaEmpresas(Sesion sesion, Action<string> navegar)
        {
            InitializeComponent();
            _s = sesion; _navegar = navegar;
            Loaded += (s, e) => Pintar();
        }

        private Window Ventana => Window.GetWindow(this);

        private void Pintar()
        {
            var filas = _s.Empresas.Select(Vista).ToList();
            GridEmpresas.ItemsSource = filas;
            Vacio.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CardEmpresas.Text = filas.Count.ToString("N0");
            CardCfdi.Text = filas.Sum(f => f.Fila.TotalCfdi).ToString("N0");
        }

        private EmpresaVista Vista(EmpresaRow e)
        {
            string texto; string estilo;
            try
            {
                if (string.IsNullOrWhiteSpace(e.RutaCer) || !File.Exists(e.RutaCer)) { texto = "Falta el archivo .cer"; estilo = "PastillaAviso"; }
                else
                {
                    using (var cer = new X509Certificate2(e.RutaCer))
                    {
                        int dias = (int)Math.Floor((cer.NotAfter - DateTime.Now).TotalDays);
                        if (dias < 0) { texto = "Venció el " + cer.NotAfter.ToString("dd/MM/yyyy"); estilo = "PastillaCritica"; }
                        else if (dias <= 7) { texto = "Vence en " + dias + (dias == 1 ? " día" : " días"); estilo = "PastillaCritica"; }
                        else if (dias <= 30) { texto = "Vence en " + dias + " días"; estilo = "PastillaAviso"; }
                        else { texto = "Vigente · " + cer.NotAfter.ToString("dd/MM/yyyy"); estilo = "PastillaOk"; }
                    }
                }
            }
            catch { texto = "No se pudo leer"; estilo = "PastillaAviso"; }

            string comercial = !string.IsNullOrWhiteSpace(e.ComercialConexionSql) ? "Conectada (directo)"
                : (!string.IsNullOrWhiteSpace(e.ComercialCarpetaXmlRecibidos) || !string.IsNullOrWhiteSpace(e.ComercialCarpetaXmlEmitidos)) ? "Solo carpetas" : "Sin configurar";
            return new EmpresaVista
            {
                Fila = e, TextoFirma = texto, EstiloFirma = (Style)FindResource(estilo), TextoComercial = comercial,
                VisibleActiva = _s.Empresa != null && _s.Empresa.EmpresaID == e.EmpresaID ? Visibility.Visible : Visibility.Collapsed
            };
        }

        private EmpresaRow Seleccionada() => (GridEmpresas.SelectedItem as EmpresaVista)?.Fila;

        private void GridEmpresas_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var sel = Seleccionada();
            if (sel == null) return;
            _s.Seleccionar(sel);
            _navegar("panel");
        }

        private void BtnUsar_Click(object sender, RoutedEventArgs e)
        {
            _s.Seleccionar((EmpresaRow)((Button)sender).Tag);
            _navegar("panel");
        }

        private void BtnAgregarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EmpresaWindow(_s.Conn) { Owner = Ventana };
            if (dlg.ShowDialog() == true) { _s.RecargarEmpresas(); Pintar(); }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            var fila = (EmpresaRow)((Button)sender).Tag;
            var dlg = new EmpresaWindow(_s.Conn, Sesion.AFila(fila)) { Owner = Ventana };
            if (dlg.ShowDialog() == true) { _s.RecargarEmpresas(); Pintar(); }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var fila = (EmpresaRow)((Button)sender).Tag;
            var r = MessageBox.Show(Ventana, "¿Eliminar «" + fila.Nombre + "» del catálogo?\n\nNo se borran los CFDI ya descargados; solo se quita la e.firma guardada.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            BrosSatDb.EliminarEmpresa(_s.Conn, fila.EmpresaID);
            _s.RecargarEmpresas(); Pintar();
        }

        // Respaldo cifrado de la configuracion de las empresas (e.firma, contrasenas, carpetas, conexion a Comercial).
        private void BtnRespaldo_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(Ventana,
                "¿Qué quieres hacer?\n\nSí = EXPORTAR un respaldo cifrado de todas las empresas.\nNo = IMPORTAR un respaldo (por ejemplo, en un servidor nuevo).\n\n" +
                "El archivo queda protegido con una contraseña que tú eliges y no se envía a ningún lado.",
                "Respaldo de empresas", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            try
            {
                if (r == MessageBoxResult.Yes)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Respaldo BrosLMV (*.brosbk)|*.brosbk", FileName = "empresas_" + DateTime.Today.ToString("yyyyMMdd") + ".brosbk" };
                    if (dlg.ShowDialog(Ventana) != true) return;
                    var pw = new ContrasenaRespaldoWindow(true, "Contraseña del respaldo") { Owner = Ventana };
                    if (pw.ShowDialog() != true) return;
                    int n = Respaldo.Exportar(_s.Conn, dlg.FileName, pw.Contrasena);
                    MessageBox.Show(Ventana, n + " empresa(s) respaldadas en:\n" + dlg.FileName + "\n\nGuarda el archivo y la contraseña en lugares distintos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (r == MessageBoxResult.No)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Respaldo BrosLMV (*.brosbk)|*.brosbk" };
                    if (dlg.ShowDialog(Ventana) != true) return;
                    var pw = new ContrasenaRespaldoWindow(false, "Contraseña del respaldo") { Owner = Ventana };
                    if (pw.ShowDialog() != true) return;
                    string destino = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "fiel");
                    int n = Respaldo.Importar(_s.Conn, dlg.FileName, pw.Contrasena, destino);
                    _s.RecargarEmpresas(); Pintar();
                    MessageBox.Show(Ventana, n + " empresa(s) restauradas. Los archivos de la e.firma quedaron en:\n" + destino, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex) { MessageBox.Show(Ventana, ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
