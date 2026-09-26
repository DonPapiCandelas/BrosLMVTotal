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

// BitacoraWindow.xaml.cs -- lee los logs que escribe BrosLMV.Descargas.Cola.Bitacora (ubicacion
// fija en %LOCALAPPDATA%, ver Bitacora.cs). Pedido explicito del usuario 2026-08-14: "no me da
// bitacora o logs" -- todo salia por Console.WriteLine, que se pierde cuando el proceso corre en
// segundo plano via Tarea Programada.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace BrosLMV.DescargasUI
{
    public partial class BitacoraWindow : Window
    {
        public BitacoraWindow()
        {
            InitializeComponent();
            LblRuta.Text = BrosLMV.Descargas.Cola.Bitacora.CarpetaLogs;
            CargarListaDeMeses();
        }

        private void CargarListaDeMeses()
        {
            string carpeta = BrosLMV.Descargas.Cola.Bitacora.CarpetaLogs;
            string mesActual = DateTime.Now.ToString("yyyy-MM");

            var meses = Directory.Exists(carpeta)
                ? Directory.GetFiles(carpeta, "broslmv-*.log")
                    .Select(f => Path.GetFileNameWithoutExtension(f).Replace("broslmv-", ""))
                    .OrderByDescending(m => m)
                    .ToList()
                : new System.Collections.Generic.List<string>();

            if (!meses.Contains(mesActual)) meses.Insert(0, mesActual);

            CmbMes.ItemsSource = meses;
            CmbMes.SelectedItem = mesActual;
        }

        private void CmbMes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => CargarLog();

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => CargarLog();

        private void CargarLog()
        {
            if (CmbMes.SelectedItem == null) return;
            string archivo = Path.Combine(BrosLMV.Descargas.Cola.Bitacora.CarpetaLogs, "broslmv-" + CmbMes.SelectedItem + ".log");
            if (!File.Exists(archivo))
            {
                TxtLog.Text = "(sin actividad registrada este mes todavía)";
                return;
            }

            using (var stream = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var lector = new StreamReader(stream))
                TxtLog.Text = lector.ReadToEnd();

            TxtLog.CaretIndex = TxtLog.Text.Length;
            TxtLog.ScrollToEnd();
        }

        private void BtnAbrirCarpeta_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(BrosLMV.Descargas.Cola.Bitacora.CarpetaLogs);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + BrosLMV.Descargas.Cola.Bitacora.CarpetaLogs + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la carpeta:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
