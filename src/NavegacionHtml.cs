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
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace BrosLMV
{
    // WebView2.NavigateToString tiene un limite real de ~2 MB: una pagina con miles de documentos incrustados (reportes de saldos, por ejemplo) se queda en blanco
    // sin avisar. Las paginas grandes se escriben en un archivo temporal y se navega a el; la pagina funciona igual (puente postMessage y broslmv.local incluidos).
    internal static class NavegacionHtml
    {
        public const int LimiteCaracteres = 1500000;       // margen bajo el limite de 2 MB (los acentos ocupan mas de un byte)

        // Carga el HTML en el WebView2. Devuelve la ruta del temporal si hubo que usarlo (para borrarlo despues con Borrar), o null.
        public static string Cargar(CoreWebView2 core, string html)
        {
            html = html ?? "";
            if (html.Length <= LimiteCaracteres) { core.NavigateToString(html); return null; }
            string dir = Path.Combine(Path.GetTempPath(), "BrosLMV_html");
            Directory.CreateDirectory(dir);
            string ruta = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(ruta, html, new UTF8Encoding(true));
            core.Navigate(new Uri(ruta).AbsoluteUri);
            return ruta;
        }

        public static void Borrar(string ruta)
        {
            try { if (!string.IsNullOrEmpty(ruta) && File.Exists(ruta)) File.Delete(ruta); } catch { /* limpieza best-effort */ }
        }
    }
}
