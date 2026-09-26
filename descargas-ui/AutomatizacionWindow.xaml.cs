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

// AutomatizacionWindow.xaml.cs -- pedido explicito del usuario 2026-08-19: "cada 2 horas o algo
// que me permita configurar", y mas tarde "debe existir un servicio". Ya no toca el Programador
// de Tareas de Windows (esa version abria una consola visible cada corrida) -- ahora edita
// servicio.json, que lee el Servicio de Windows "BrosLMV Descargas" en cada vuelta de su loop
// (cada 1 min) -- el cambio de intervalo aplica solo, sin reiniciar nada.

using System;
using System.Linq;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI
{
    public partial class AutomatizacionWindow : Window
    {
        public const string NombreServicio = "BrosLMV Descargas";

        public AutomatizacionWindow()
        {
            InitializeComponent();
            Loaded += AutomatizacionWindow_Loaded;
        }

        private void AutomatizacionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LblServicio.Text = "Servicio \"" + NombreServicio + "\": " + EstadoServicioTexto();

            var config = ConfigServicio.Cargar();
            int minutosActuales = config != null ? Math.Max(15, config.IntervaloSolicitarMinutos) : 120;
            LblEstado.Text = config == null
                ? "Sin configuración todavía (se creará al guardar)."
                : "Intervalo actual: cada " + TextoMinutos(minutosActuales) + ".";

            var item = CmbIntervalo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == minutosActuales.ToString());
            CmbIntervalo.SelectedItem = item ?? CmbIntervalo.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "120");
        }

        private static string EstadoServicioTexto()
        {
            try
            {
                using var sc = new ServiceController(NombreServicio);
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => "activo ✓",
                    ServiceControllerStatus.Stopped => "detenido",
                    _ => sc.Status.ToString()
                };
            }
            catch
            {
                return "no instalado (usa el instalador de BrosLMV.Descargas)";
            }
        }

        private static string TextoMinutos(int minutos)
        {
            if (minutos < 60) return minutos + " minutos";
            int horas = minutos / 60;
            return horas + (horas == 1 ? " hora" : " horas");
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

        private void Guardar_Click(object sender, RoutedEventArgs e)
        {
            if (CmbIntervalo.SelectedItem is not ComboBoxItem item)
            {
                MessageBox.Show(this, "Selecciona un intervalo.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int minutos = int.Parse((string)item.Tag);
            BtnGuardar.IsEnabled = false;
            try
            {
                var config = ConfigServicio.Cargar() ?? new ConfigServicio();
                config.IntervaloSolicitarMinutos = minutos;
                try
                {
                    config.Guardar();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo guardar la configuración (¿permisos de administrador?):\n\n" + ex.Message,
                        "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show(this, "Listo — el servicio va a solicitar periodos nuevos cada " + TextoMinutos(minutos) + " (aplica solo, sin reiniciar nada).",
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            finally
            {
                BtnGuardar.IsEnabled = true;
            }
        }
    }
}
