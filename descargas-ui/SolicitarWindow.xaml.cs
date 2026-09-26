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
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Cola;

namespace BrosLMV.DescargasUI
{
    public partial class SolicitarWindow : Window
    {
        public bool EsRecibidos { get; private set; }

        public List<(DateTime Desde, DateTime Hasta)> Chunks { get; private set; }

        public SolicitarWindow()
        {
            InitializeComponent();
            FechaDesde.SelectedDate = DateTime.Today.AddDays(-1);
            FechaHasta.SelectedDate = DateTime.Today.AddDays(-1);
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Solicitar_Click(object sender, RoutedEventArgs e)
        {
            if (FechaDesde.SelectedDate == null || FechaHasta.SelectedDate == null)
            {
                MessageBox.Show(this, "Selecciona ambas fechas.", "Falta informacion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (FechaDesde.SelectedDate > FechaHasta.SelectedDate)
            {
                MessageBox.Show(this, "La fecha \"Desde\" no puede ser posterior a \"Hasta\".", "Rango invalido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var desde = FechaDesde.SelectedDate.Value;
            var hasta = FechaHasta.SelectedDate.Value;
            Chunks = SolicitudChunker.PartirEnMeses(desde, hasta);
            EsRecibidos = RbRecibidos.IsChecked == true;

            int totalReal = Chunks.Count * 2; // cada mes = 1 solicitud CFDI + 1 Metadata
            string detalle = Chunks.Count == 1
                ? "1 mes -> 2 solicitudes reales (CFDI + Metadata)."
                : Chunks.Count + " meses -> " + totalReal + " solicitudes reales (CFDI + Metadata de cada uno).";
            var r = MessageBox.Show(this,
                "Este rango se va a partir en " + detalle + "\n\n¿Confirmas que quieres gastar " + totalReal + " solicitud(es) de tu cupo diario?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            DialogResult = true;
            Close();
        }
    }
}
