// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrosLMV.Bancos.Motor
{
    public static class BancoTexto
    {
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d) { if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c); }
            string r = sb.ToString().ToLowerInvariant().Replace(' ', ' ').Replace("\"", "").Replace("'", "");
            return Regex.Replace(r, @"\s+", " ").Trim();
        }

        public static string Limpiar(string s)
        {
            if (s == null) return "";
            s = s.Replace(' ', ' ').Trim();
            if (s.Length > 1 && s[0] == '\'') s = s.Substring(1);          // Santander y Banorte anteponen ' para que Excel no los vuelva numero
            if (s.Length > 1 && s[s.Length - 1] == '\'' && s.IndexOf('\'') == s.Length - 1) s = s.Substring(0, s.Length - 1);
            return s.Trim();
        }

        public static string Decodificar(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1252).GetString(b);                                        // los bancos viejos exportan en Windows-1252
            }
        }

        public static List<string[]> Leer(string ruta)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return LeerTexto(Decodificar(System.IO.File.ReadAllBytes(ruta)));
        }

        public static List<string[]> LeerTexto(string contenido)
        {
            var lineas = new List<string>();
            var actual = new StringBuilder(); int comillas = 0;
            foreach (string l in contenido.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                // Un campo entre comillas puede traer saltos de linea: se une con la siguiente hasta que las comillas cierren.
                if (actual.Length > 0) actual.Append(' ');
                actual.Append(l); comillas += l.Count(c => c == '"');
                if (comillas % 2 == 0) { lineas.Add(actual.ToString()); actual.Clear(); comillas = 0; }
            }
            if (actual.Length > 0) lineas.Add(actual.ToString());
            while (lineas.Count > 0 && string.IsNullOrWhiteSpace(lineas[lineas.Count - 1])) lineas.RemoveAt(lineas.Count - 1);

            char d = Delimitador(lineas);
            return lineas.Select(l => Dividir(l, d)).ToList();
        }

        // El delimitador que reparte mas parejo las primeras lineas con contenido (coma, tabulador, barra vertical o punto y coma).
        public static char Delimitador(IList<string> lineas)
        {
            var muestra = lineas.Where(l => !string.IsNullOrWhiteSpace(l)).Take(40).ToList();
            char mejor = ','; double mejorPuntos = -1;
            foreach (char c in new[] { '\t', '|', ';', ',' })
            {
                var cuentas = muestra.Select(l => FueraDeComillas(l, c)).ToList();
                if (cuentas.Count == 0) continue;
                int moda = cuentas.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
                if (moda == 0) continue;
                double puntos = cuentas.Count(x => x == moda) * 10.0 + moda;
                if (puntos > mejorPuntos) { mejorPuntos = puntos; mejor = c; }
            }
            return mejor;
        }

        private static int FueraDeComillas(string l, char d)
        {
            int n = 0; bool q = false;
            foreach (char c in l) { if (c == '"') q = !q; else if (c == d && !q) n++; }
            return n;
        }

        public static string[] Dividir(string linea, char d)
        {
            var celdas = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < linea.Length; i++)
            {
                char c = linea[i];
                if (c == '"') { if (q && i + 1 < linea.Length && linea[i + 1] == '"') { sb.Append('"'); i++; } else q = !q; }
                else if (c == d && !q) { celdas.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            celdas.Add(sb.ToString());
            return celdas.Select(x => x.Trim()).ToArray();
        }
    }
}
