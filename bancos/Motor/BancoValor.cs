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
    public static class BancoValor
    {
        private static readonly Dictionary<string, int> Meses = new Dictionary<string, int>
        {
            ["enero"] = 1, ["ene"] = 1, ["febrero"] = 2, ["feb"] = 2, ["marzo"] = 3, ["mar"] = 3, ["abril"] = 4, ["abr"] = 4, ["mayo"] = 5, ["may"] = 5,
            ["junio"] = 6, ["jun"] = 6, ["julio"] = 7, ["jul"] = 7, ["agosto"] = 8, ["ago"] = 8, ["septiembre"] = 9, ["sept"] = 9, ["sep"] = 9,
            ["octubre"] = 10, ["oct"] = 10, ["noviembre"] = 11, ["nov"] = 11, ["diciembre"] = 12, ["dic"] = 12,
            ["january"] = 1, ["jan"] = 1, ["february"] = 2, ["april"] = 4, ["apr"] = 4, ["june"] = 6, ["july"] = 7, ["august"] = 8, ["aug"] = 8,
            ["september"] = 9, ["october"] = 10, ["november"] = 11, ["december"] = 12, ["dec"] = 12
        };
        private static readonly Regex Hora = new Regex(@"[\sT]+\d{1,2}:\d{2}(:\d{2}(\.\d+)?)?(\s*[ap]\.?\s?m\.?)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Palabra = new Regex(@"[a-z]+", RegexOptions.Compiled);

        // dd/MM/yyyy, dd/MM/yy, dd-MM-yyyy, yyyy/MM/dd, yyyy-MM-dd, "08 Jul 2026", "10-Dic-2025", "02/Agosto/2026 11:35:00", 02122025, 20251202.
        // Ambiguo (los dos primeros <= 12) se lee dia/mes, como en Mexico, salvo que el mapa diga "mdy".
        public static bool Fecha(string texto, string formato, out DateTime fecha)
        {
            fecha = default(DateTime);
            string s = BancoTexto.Norm(BancoTexto.Limpiar(texto));
            if (s.Length < 6) return false;
            s = Hora.Replace(s, "").Trim();
            s = Palabra.Replace(s, m => Meses.TryGetValue(m.Value, out int n) ? n.ToString(CultureInfo.InvariantCulture) : m.Value);
            if (Regex.IsMatch(s, @"[a-z]")) return false;
            int a, b, c;
            var compacta = Regex.Match(s, @"^(\d{8})$");
            if (compacta.Success)
            {
                string t = compacta.Value; int primero = int.Parse(t.Substring(0, 4), CultureInfo.InvariantCulture);
                // yyyyMMdd y ddMMyyyy se confunden ("20032026" = 20/03/2026 empieza con 2003): se prueba yyyyMMdd y, si no es fecha valida, ddMMyyyy.
                if (primero >= 1990 && primero <= 2100 && Armar(primero, int.Parse(t.Substring(4, 2)), int.Parse(t.Substring(6, 2)), out fecha)) return true;
                return Armar(int.Parse(t.Substring(4, 4)), int.Parse(t.Substring(2, 2)), int.Parse(t.Substring(0, 2)), out fecha);
            }
            var m3 = Regex.Match(s, @"^(\d{1,4})[\s/\-\.](\d{1,2})[\s/\-\.](\d{1,4})$");
            if (!m3.Success) return false;
            string sa = m3.Groups[1].Value, sb2 = m3.Groups[2].Value, sc = m3.Groups[3].Value;
            a = int.Parse(sa, CultureInfo.InvariantCulture); b = int.Parse(sb2, CultureInfo.InvariantCulture); c = int.Parse(sc, CultureInfo.InvariantCulture);
            if (sa.Length == 4) return Armar(a, b, c, out fecha);                                       // yyyy-MM-dd
            int anio = sc.Length == 4 ? c : 2000 + c;
            bool mdy = formato == "mdy" || (b > 12 && a <= 12);
            return mdy ? Armar(anio, a, b, out fecha) : Armar(anio, b, a, out fecha);
        }

        private static bool Armar(int anio, int mes, int dia, out DateTime fecha)
        {
            fecha = default(DateTime);
            if (anio < 1990 || anio > 2100 || mes < 1 || mes > 12 || dia < 1 || dia > 31) return false;
            try { fecha = new DateTime(anio, mes, dia); return true; } catch { return false; }
        }

        // "$1,234.56", "-$150.00", "(1,234.56)", "1234.56-", "-", "" -> numero. Vacio y "-" valen 0 (asi vienen los bancos cuando no hay cargo o abono).
        public static bool Monto(string texto, out decimal valor)
        {
            valor = 0; string s = BancoTexto.Limpiar(texto);
            if (s.Length == 0 || s == "-" || s == "_" || s == "—" || s == "–") return true;
            bool neg = false;
            if (s.StartsWith("(") && s.EndsWith(")")) { neg = true; s = s.Substring(1, s.Length - 2); }
            s = s.Replace("−", "-").Replace("–", "-");
            if (s.EndsWith("-")) { neg = true; s = s.Substring(0, s.Length - 1); }
            if (s.StartsWith("-")) { neg = true; s = s.Substring(1); }
            if (s.StartsWith("+")) s = s.Substring(1);
            s = Regex.Replace(s, @"(?i)\b(mxn|usd|eur|mn|dll?s?)\b", "");
            s = s.Replace("$", "").Replace(" ", "").Replace(" ", "");
            if (s.Length == 0) return false;
            if (s.Contains(",") && s.Contains("."))
            {
                if (s.LastIndexOf(',') > s.LastIndexOf('.')) s = s.Replace(".", "").Replace(',', '.');      // 1.234,56
                else s = s.Replace(",", "");                                                                // 1,234.56
            }
            else if (s.Contains(","))
            {
                if (Regex.IsMatch(s, @"^\d{1,3}(,\d{3})+$")) s = s.Replace(",", "");                        // 1,234
                else if (Regex.IsMatch(s, @"^\d+,\d{1,2}$")) s = s.Replace(',', '.');                       // 1234,56
                else return false;
            }
            if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor)) return false;
            if (neg) valor = -valor;
            return true;
        }

        public static bool EsMontoNoVacio(string s)
        {
            string l = BancoTexto.Limpiar(s);
            return l.Length > 0 && l != "-" && Monto(l, out _);
        }
    }

    // Lo que el texto de la descripcion revela de la otra parte: clave de rastreo, RFC, CLABE, nombre, banco. Sirve despues para empatar contra CFDI.
}
