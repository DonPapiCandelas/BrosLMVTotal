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

// SdkCatalogo.cs  (v2.98.0)
// Catálogo del SDK (ctx.* y ctx.erp.*): UNA sola fuente, src\assets\sdk_catalogo.json (incrustado en la DLL). De ahí salen el panel de referencias de la
// Consola, el manual HTML (build\sdk\generar_referencia_sdk.py -> SDK_REFERENCIA.html, también incrustado) y docs\SDK_REFERENCIA.md.
// build\sdk\verificar_catalogo_sdk.ps1 falla la compilación del instalador si una función pública de ScriptContext/ErpContext no tiene entrada.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BrosLMV
{
    internal class EntradaSdk
    {
        public string Id, Lang, Cat, Nombre, Firma, Resumen, Ejemplo;
        public bool Interno;
    }

    internal static class SdkCatalogo
    {
        private static List<EntradaSdk> _todas;

        private static string Recurso(string termina)
        {
            var asm = Assembly.GetExecutingAssembly();
            string n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(termina, StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
            using (var s = asm.GetManifestResourceStream(n)) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        private static string T(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : ""; }

        // Todas las entradas del catálogo (carga una vez). Si el recurso no está, lista vacía: la Consola sigue funcionando sin panel de referencias.
        public static List<EntradaSdk> Todas()
        {
            if (_todas != null) return _todas;
            var lista = new List<EntradaSdk>();
            try
            {
                string json = Recurso("sdk_catalogo.json");
                if (json != null)
                {
                    var j = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
                    foreach (Dictionary<string, object> e in (ArrayList)j["entradas"])
                        lista.Add(new EntradaSdk
                        {
                            Id = T(e, "id"), Lang = T(e, "lang"), Cat = T(e, "cat"), Nombre = T(e, "nombre"), Firma = T(e, "firma"),
                            Resumen = T(e, "resumen"), Ejemplo = T(e, "ejemplo"), Interno = e.ContainsKey("interno") && Convert.ToBoolean(e["interno"])
                        });
                }
            }
            catch { }
            _todas = lista;
            return _todas;
        }

        // Ancla de una entrada en el manual HTML (misma regla que build\sdk\generar_referencia_sdk.py).
        public static string Slug(string id) { return Regex.Replace((id ?? "").Replace(':', '-').Replace('.', '-'), "[^A-Za-z0-9_-]", "-"); }

        // Manual del SDK (HTML incrustado), o null si no está.
        public static string ManualHtml() { return Recurso("doc_SDK_REFERENCIA.html"); }
    }
}
