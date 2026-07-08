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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace BrosLMV.Empresas
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
            // Mostramos la versión del ADDON que se va a instalar (la que importa para saber
            // qué versión queda en el equipo), leída del payload embebido. Fallback: la del EXE.
            string ver = LeerVersionAddon();
            if (ver == null)
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                ver = v.Major + "." + v.Minor + "." + v.Build;
            }
            lblVer.Text = "BrosLMV v" + ver + "  ·  instalará esta versión";
        }

        // Lee la AssemblyVersion del addon (BrosLMVClsMain.dll) dentro de payload.zip embebido,
        // sin instalar nada: abre el zip en memoria y extrae solo esa DLL a %TEMP% para leerla.
        static string LeerVersionAddon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string res = asm.GetManifestResourceNames()
                                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
                if (res == null) return null;
                using (var s = asm.GetManifestResourceStream(res))
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    var entry = zip.Entries.FirstOrDefault(
                        e => e.FullName.EndsWith("BrosLMVClsMain.dll", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return null;
                    string tmp = Path.Combine(Path.GetTempPath(), "BrosLMV_ver_probe.dll");
                    entry.ExtractToFile(tmp, true);
                    var v = AssemblyName.GetAssemblyName(tmp).Version;
                    try { File.Delete(tmp); } catch { }
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
            }
            catch { return null; }
        }

        void btnInstalar_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
        void btnSalir_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
