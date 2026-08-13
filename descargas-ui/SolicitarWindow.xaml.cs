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
using System.Windows;

namespace BrosLMV.DescargasUI
{
    public partial class SolicitarWindow : Window
    {
        public DateTime Desde { get; private set; }
        public DateTime Hasta { get; private set; }
        public bool EsRecibidos { get; private set; }

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

            Desde = FechaDesde.SelectedDate.Value;
            Hasta = FechaHasta.SelectedDate.Value.AddHours(23).AddMinutes(59).AddSeconds(59);
            EsRecibidos = RbRecibidos.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
