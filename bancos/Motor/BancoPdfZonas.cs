// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BrosLMV.Bancos.Layouts;

namespace BrosLMV.Bancos.Motor
{
    public static partial class BancoPdf
    {
        private static DateTime? FechaZona(string texto, int anioImp, int mesImp)
        {
            var partes = Regex.Split(texto.Trim(), @"[-/ .]").Where(x => x.Length > 0).ToArray();
            if (partes.Length < 2) return null;
            int dia; if (!int.TryParse(partes[0], out dia)) return null;
            int mes;
            if (!int.TryParse(partes[1], out mes)) { if (!Meses.TryGetValue(partes[1].ToUpperInvariant().Substring(0, Math.Min(3, partes[1].Length)), out mes)) return null; }
            int anio;
            if (partes.Length >= 3 && int.TryParse(partes[2], out anio)) { if (anio < 100) anio += 2000; }
            else anio = mes > mesImp ? anioImp - 1 : anioImp;          // sin anio en la fecha: el de la impresion; un mes posterior al de la impresion es del anio anterior
            try { return new DateTime(anio, mes, dia); } catch { return null; }
        }

        // Lectura por zonas: ver PdfOpciones.
        private static List<M> Zonas(List<List<P>> pags, PdfOpciones o, out string cuenta)
        {
            o = o ?? new PdfOpciones();
            var lineasPrimera = Lineas(pags[0]);
            string t0 = string.Join(" ", lineasPrimera.Select(l => l.Txt));
            cuenta = "";
            if (!string.IsNullOrEmpty(o.CuentaPatron)) { var mc = Regex.Match(t0, o.CuentaPatron); if (mc.Success) cuenta = mc.Groups[mc.Groups.Count > 1 ? 1 : 0].Value; }
            if (cuenta.Length == 0) cuenta = CuentaDe(pags[0]);
            var mi = Regex.Match(t0, @"(\d{2})/(\d{2})/(\d{4})");
            int anioImp = mi.Success ? int.Parse(mi.Groups[3].Value) : DateTime.Today.Year, mesImp = mi.Success ? int.Parse(mi.Groups[2].Value) : 12;
            var rxFila = new Regex(o.FilaInicia, RegexOptions.Compiled);
            Regex rxIgnorar = string.IsNullOrEmpty(o.Ignorar) ? null : new Regex(o.Ignorar, RegexOptions.Compiled);
            var res = new List<M>(); double cC = 0, cA = 0, cS = 0; bool hay = false;
            foreach (var pg in pags)
            {
                var lins = Lineas(pg);
                Func<Lin, string, bool> trae = (l, t) => l.W.Any(w => w.T.Equals(t, StringComparison.OrdinalIgnoreCase));
                var cab = lins.FirstOrDefault(l => trae(l, o.EncCargo) && trae(l, o.EncSaldo));
                if (cab != null)
                {
                    cC = cab.W.First(w => w.T.Equals(o.EncCargo, StringComparison.OrdinalIgnoreCase)).C;
                    var ab = cab.W.FirstOrDefault(w => w.T.Equals(o.EncAbono, StringComparison.OrdinalIgnoreCase)); cA = ab != null ? ab.C : cC;
                    cS = cab.W.First(w => w.T.Equals(o.EncSaldo, StringComparison.OrdinalIgnoreCase)).C; hay = true;
                }
                if (!hay) continue;
                double yCab = cab != null ? cab.Y : -1;
                var filas = lins.Where(l => l.Y > yCab + 4 && l.W[0].L < o.FechaXMax && rxFila.IsMatch(l.W[0].T) && FechaZona(l.W[0].T, anioImp, mesImp).HasValue).ToList();
                if (filas.Count == 0) continue;
                var bloques = filas.Select(f =>
                {
                    var m = new M { F = FechaZona(f.W[0].T, anioImp, mesImp).Value };
                    foreach (var w in f.W.Where(x => Monto.IsMatch(x.T)))
                    {
                        double c = w.C; double dc = Math.Abs(c - cC), da = Math.Abs(c - cA), ds = Math.Abs(c - cS);
                        if (ds <= dc && ds <= da) m.Saldo = Num(w.T); else if (dc <= da) m.Cargo = Num(w.T); else m.Abono = Num(w.T);
                    }
                    return new { M = m, Y = f.Y, L = new List<Lin>() };
                }).ToList();
                foreach (var l in lins.Where(x => x.Y > yCab + 4))
                {
                    if (rxIgnorar != null && rxIgnorar.IsMatch(l.Txt)) continue;
                    bool esFila = filas.Contains(l);
                    var b = bloques.OrderBy(x => Math.Abs(x.Y - l.Y)).First();
                    if (!esFila && Math.Abs(b.Y - l.Y) > o.Alcance) continue;
                    if (esFila) b = bloques[filas.IndexOf(l)];
                    var trozo = new Lin { Y = l.Y, W = l.W.Where(w => w.L >= o.DescXMin && !Monto.IsMatch(w.T) && w.T != "$").ToList() };
                    if (trozo.W.Count > 0) b.L.Add(trozo);
                }
                foreach (var b in bloques) { b.M.D = Desc(b.L.OrderBy(x => x.Y), w => true); res.Add(b.M); }
            }
            return res;
        }
    }
}
