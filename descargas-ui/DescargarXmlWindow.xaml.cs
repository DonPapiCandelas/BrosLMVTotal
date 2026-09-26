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

// DescargarXmlWindow.xaml.cs -- pedido explicito del usuario (2026-08-14): los CFDI ya estan
// descargados (guardados via OrganizadorArchivos, ver Cola/OrganizadorArchivos.cs), esto NO
// vuelve a pedirlos al SAT (no gasta cupo) -- solo copia/empaqueta los XML que ya estan en disco
// hacia una carpeta que el usuario elige, sueltos o en un solo ZIP.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace BrosLMV.DescargasUI
{
    public partial class DescargarXmlWindow : Window
    {
        private readonly List<CfdiRow> _existentes;
        private readonly int _faltantes;

        internal DescargarXmlWindow(List<CfdiRow> cfdis)
        {
            InitializeComponent();

            _existentes = cfdis.Where(c => !string.IsNullOrEmpty(c.RutaArchivoXml) && File.Exists(c.RutaArchivoXml)).ToList();
            _faltantes = cfdis.Count - _existentes.Count;

            LblTitulo.Text = "Descargar " + cfdis.Count + " CFDI";
            LblSubtitulo.Text = _faltantes == 0
                ? "Se copiarán los " + _existentes.Count + " XML ya descargados (esto no vuelve a pedir nada al SAT)."
                : _existentes.Count + " tienen su XML en disco y se pueden copiar. " + _faltantes + " no tienen el archivo disponible y se omitirán.";
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Descargar_Click(object sender, RoutedEventArgs e)
        {
            if (_existentes.Count == 0)
            {
                MessageBox.Show(this, "Ninguno de los CFDI seleccionados tiene su XML disponible en disco.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (RbZip.IsChecked == true) DescargarComoZip();
            else DescargarComoCarpeta();
        }

        private void DescargarComoCarpeta()
        {
            var dlg = new OpenFolderDialog { Title = "Carpeta destino para los XML" };
            if (dlg.ShowDialog() != true) return;

            int copiados = 0;
            var nombresUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _existentes)
            {
                string nombre = NombreUnico(Path.GetFileName(c.RutaArchivoXml), c.UUID, nombresUsados);
                try
                {
                    File.Copy(c.RutaArchivoXml, Path.Combine(dlg.FolderName, nombre), overwrite: true);
                    copiados++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo copiar " + nombre + ":\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            MostrarResumenYCerrar(copiados, "carpeta " + dlg.FolderName);
        }

        private void DescargarComoZip()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar ZIP",
                Filter = "Archivo ZIP (*.zip)|*.zip",
                FileName = "CFDI_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".zip"
            };
            if (dlg.ShowDialog() != true) return;

            int copiados = 0;
            var nombresUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create))
                {
                    foreach (var c in _existentes)
                    {
                        string nombre = NombreUnico(Path.GetFileName(c.RutaArchivoXml), c.UUID, nombresUsados);
                        try
                        {
                            zip.CreateEntryFromFile(c.RutaArchivoXml, nombre);
                            copiados++;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("No se pudo agregar " + nombre + " al ZIP: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo crear el ZIP:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MostrarResumenYCerrar(copiados, "ZIP " + dlg.FileName);
        }

        // Si dos CFDI generan el mismo nombre de archivo (mismo nombre original del SAT, o
        // plantillas antiguas sin UUID), se distingue con un sufijo -- nunca se sobreescribe un
        // XML de otro CFDI dentro del mismo destino.
        private static string NombreUnico(string nombreOriginal, string uuid, HashSet<string> usados)
        {
            string nombre = nombreOriginal;
            if (usados.Contains(nombre))
                nombre = Path.GetFileNameWithoutExtension(nombreOriginal) + "_" + uuid.Substring(0, 8) + Path.GetExtension(nombreOriginal);
            usados.Add(nombre);
            return nombre;
        }

        private void MostrarResumenYCerrar(int copiados, string destino)
        {
            string mensaje = "Se copiaron " + copiados + " de " + _existentes.Count + " XML a " + destino + ".";
            if (_faltantes > 0) mensaje += "\n\n" + _faltantes + " CFDI se omitieron porque no tenían su XML disponible en disco.";
            MessageBox.Show(this, mensaje, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
    }
}
