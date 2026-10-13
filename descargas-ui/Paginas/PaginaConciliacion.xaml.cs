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

// PaginaConciliacion.xaml.cs -- lo que el SAT reporta (Metadata) por mes contra los XML descargados, para la empresa activa.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaConciliacion : UserControl
    {
        private readonly Sesion _s;
        private List<ConciliacionFila> _filas = new List<ConciliacionFila>();

        public PaginaConciliacion(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private void Cargar()
        {
            _filas = Conciliacion.Calcular(_s.Conn, _s.Empresa.RFC);
            GridConciliacion.ItemsSource = _filas;
            Vacio.Visibility = _filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int faltan = _filas.Sum(f => f.Faltan);
            int sinMeta = _filas.Count(f => !f.HayMetadata && f.Descargados > 0);
            bool bien = faltan == 0;
            Aviso.Style = (Style)FindResource(bien ? (sinMeta == 0 ? "BandaOk" : "BandaInfo") : "BandaAviso");
            Aviso.HorizontalAlignment = HorizontalAlignment.Stretch;
            LblResumen.Text = _filas.Count == 0 ? "Sin meses por conciliar todavía."
                : bien ? (sinMeta == 0 ? "Todo conciliado: no falta ningún CFDI vigente que el SAT reporte." : "Sin faltantes en los meses con Metadata; " + sinMeta + (sinMeta == 1 ? " mes todavía no tiene" : " meses todavía no tienen") + " Metadata del SAT para verificar.")
                : "Faltan " + faltan.ToString("N0") + " CFDI vigentes que el SAT reporta; se vuelven a pedir por mes de forma automática.";
        }

        private void BtnCsv_Click(object sender, RoutedEventArgs e)
        {
            var ventana = Window.GetWindow(this);
            if (_filas.Count == 0) { MessageBox.Show(ventana, "No hay filas para exportar.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "conciliacion_" + _s.Empresa.RFC + "_" + DateTime.Today.ToString("yyyyMMdd") + ".csv" };
            if (dlg.ShowDialog(ventana) != true) return;
            System.IO.File.WriteAllText(dlg.FileName, Conciliacion.ACsv(_filas), new System.Text.UTF8Encoding(true));
            MessageBox.Show(ventana, "Archivo guardado:\n" + dlg.FileName, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
