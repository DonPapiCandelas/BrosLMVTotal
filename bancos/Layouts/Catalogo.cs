// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.Bancos.Layouts
{
    // El archivo tal como lo ven las condiciones, ya normalizado.
    public class Huella
    {
        public string Encabezado = "";   // celdas de la fila de encabezado unidas con "|"
        public string Texto = "";        // primeras filas
        public string Archivo = "";      // nombre sin extension
        public string Pdf = "";          // texto de las primeras paginas (solo PDF)
    }

    public class Coincidencia
    {
        public Layout Layout;
        public int Porcentaje;           // 0..100: cuanto del mejor camino de reconocimiento se cumple
        public bool Usable { get { return Layout != null && Porcentaje >= Layout.Umbral; } }
    }

    public static class Catalogo
    {
        private static List<Layout> _fabrica;

        // Los layouts de fabrica viajan dentro del programa como recursos.
        public static IReadOnlyList<Layout> DeFabrica()
        {
            if (_fabrica != null) return _fabrica;
            var lista = new List<Layout>();
            var asm = typeof(Catalogo).Assembly;
            foreach (var nombre in asm.GetManifestResourceNames().Where(n => n.Contains(".Layouts.Fabrica.") && n.EndsWith(".json")).OrderBy(n => n, StringComparer.Ordinal))
                using (var s = asm.GetManifestResourceStream(nombre))
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                    lista.Add(Layout.DeDatos(r.ReadToEnd()));
            _fabrica = lista;
            return lista;
        }

        // Los de la empresa sustituyen al de fabrica con el mismo Id.
        public static List<Layout> Combinar(IEnumerable<Layout> empresa)
        {
            var res = DeFabrica().ToDictionary(l => l.Id, l => l, StringComparer.OrdinalIgnoreCase);
            var orden = DeFabrica().Select(l => l.Id).ToList();
            if (empresa != null)
                foreach (var l in empresa) { if (!res.ContainsKey(l.Id)) orden.Add(l.Id); res[l.Id] = l; }
            return orden.Select(id => res[id]).ToList();
        }

        public static Huella HuellaDe(List<string[]> filas, int filaEncabezado, string nombreArchivo, string textoPdf)
        {
            var h = new Huella();
            h.Encabezado = filaEncabezado >= 0 && filaEncabezado < filas.Count ? string.Join("|", filas[filaEncabezado].Select(c => BancoTexto.Norm(BancoTexto.Limpiar(c)))) : "";
            h.Texto = BancoTexto.Norm(string.Join(" ", filas.Take(40).Select(f => string.Join(" ", f))));
            h.Archivo = BancoTexto.Norm(Path.GetFileNameWithoutExtension(nombreArchivo ?? ""));
            h.Pdf = BancoTexto.Norm(textoPdf ?? "");
            return h;
        }

        private static string Lugar(Huella h, string en)
        {
            switch ((en ?? "").ToLowerInvariant())
            {
                case "texto": return h.Texto;
                case "archivo": return h.Archivo;
                case "pdf": return h.Pdf;
                default: return h.Encabezado;
            }
        }

        private static bool Cumple(Condicion c, Huella h)
        {
            string t = Lugar(h, c.En);
            if (c.Contiene != null && c.Contiene.Any(v => t.Contains(BancoTexto.Norm(v)))) return true;
            if (c.Empieza != null && c.Empieza.Any(v => t.StartsWith(BancoTexto.Norm(v), StringComparison.Ordinal))) return true;
            return false;
        }

        public static int Porcentaje(Layout l, Huella h)
        {
            if (l.Reconocer == null || l.Reconocer.Count == 0) return 0;
            int mejor = 0;
            foreach (var camino in l.Reconocer)
            {
                if (camino.Count == 0) continue;
                int ok = camino.Count(c => Cumple(c, h));
                mejor = Math.Max(mejor, (int)Math.Round(100.0 * ok / camino.Count));
            }
            return mejor;
        }

        public static List<Coincidencia> Probar(IEnumerable<Layout> layouts, Huella h, string formato)
        {
            return layouts.Where(l => string.Equals(l.Formato, formato, StringComparison.OrdinalIgnoreCase))
                .Select(l => new Coincidencia { Layout = l, Porcentaje = Porcentaje(l, h) }).ToList();
        }

        // El que se usa solo: entre los que alcanzan su umbral, el de mayor confianza y, a igualdad, el primero de la lista.
        public static Coincidencia Elegir(List<Coincidencia> prueba)
        {
            Coincidencia mejor = null;
            foreach (var c in prueba)
            {
                if (!c.Usable) continue;
                if (mejor == null || c.Layout.Confianza > mejor.Layout.Confianza) mejor = c;
            }
            return mejor;
        }
    }
}
