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

// PaginaAutomatizacion.xaml.cs -- pedido del usuario 2026-08-19: "cada 2 horas o algo que me permita configurar", y despues "debe existir
// un servicio". Edita servicio.json, que lee el Servicio de Windows "BrosLMV Descargas" en cada vuelta de su loop (cada 1 min): el cambio
// de intervalo aplica solo, sin reiniciar nada.

using System;
using System.Linq;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaAutomatizacion : UserControl
    {
        public const string NombreServicio = "BrosLMV Descargas";

        public PaginaAutomatizacion()
        {
            InitializeComponent();
            Loaded += (s, e) => Cargar();
        }

        private void Cargar()
        {
            string estado = EstadoServicio(out string estilo);
            LblServicio.Text = estado;
            PastillaServicio.Style = (Style)FindResource(estilo);

            var config = ConfigServicio.Cargar();
            int minutos = config != null ? Math.Max(15, config.IntervaloSolicitarMinutos) : 120;
            LblEstado.Text = config == null ? "Todavía sin configuración propia (se crea al guardar)." : "Hoy pide periodos nuevos cada " + TextoMinutos(minutos) + ".";
            var item = CmbIntervalo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == minutos.ToString());
            CmbIntervalo.SelectedItem = item ?? CmbIntervalo.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "120");
        }

        private static string EstadoServicio(out string estilo)
        {
            try
            {
                using (var sc = new ServiceController(NombreServicio))
                {
                    switch (sc.Status)
                    {
                        case ServiceControllerStatus.Running: estilo = "PastillaOk"; return "Activo";
                        case ServiceControllerStatus.Stopped: estilo = "PastillaCritica"; return "Detenido";
                        default: estilo = "PastillaAviso"; return sc.Status.ToString();
                    }
                }
            }
            catch { estilo = "PastillaAviso"; return "No instalado"; }
        }

        private static string TextoMinutos(int minutos)
        {
            if (minutos < 60) return minutos + " minutos";
            int horas = minutos / 60;
            return horas + (horas == 1 ? " hora" : " horas");
        }

        private void Guardar_Click(object sender, RoutedEventArgs e)
        {
            var ventana = Window.GetWindow(this);
            if (!(CmbIntervalo.SelectedItem is ComboBoxItem item)) { MessageBox.Show(ventana, "Selecciona un intervalo.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int minutos = int.Parse((string)item.Tag);
            BtnGuardar.IsEnabled = false;
            try
            {
                var config = ConfigServicio.Cargar() ?? new ConfigServicio();
                config.IntervaloSolicitarMinutos = minutos;
                try { config.Guardar(); }
                catch (Exception ex)
                {
                    MessageBox.Show(ventana, "No se pudo guardar la configuración (¿permisos de administrador?):\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show(ventana, "Listo: el servicio pedirá periodos nuevos cada " + TextoMinutos(minutos) + ". Aplica solo, sin reiniciar nada.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                Cargar();
            }
            finally { BtnGuardar.IsEnabled = true; }
        }
    }
}
