// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrosLMV.Bancos.Motor
{
    public static class BancoPdf
    {
        private sealed class P { public string T; public double L, R, Y; public double C { get { return (L + R) / 2; } } }
        private sealed class Lin { public double Y; public List<P> W = new List<P>(); public string Txt { get { return string.Join(" ", W.Select(x => x.T)); } } }
        private sealed class M { public DateTime F; public string D = ""; public decimal Cargo, Abono; public decimal? Saldo; public double Centro = -1; }

        private static readonly Regex Monto = new Regex(@"^(\d{1,3}(,\d{3})+|\d{1,3})\.\d{2}$", RegexOptions.Compiled);
        private static readonly Dictionary<string, int> Meses = new Dictionary<string, int> { { "ENE", 1 }, { "FEB", 2 }, { "MAR", 3 }, { "ABR", 4 }, { "MAY", 5 }, { "JUN", 6 }, { "JUL", 7 }, { "AGO", 8 }, { "SEP", 9 }, { "OCT", 10 }, { "NOV", 11 }, { "DIC", 12 } };
        private static decimal Num(string s) { return decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture); }

        private static List<List<P>> Paginas(string ruta)
        {
            var res = new List<List<P>>();
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(ruta))
                foreach (var pag in doc.GetPages())
                {
                    var l = new List<P>();
                    foreach (var w in pag.GetWords())
                    {
                        string t = (w.Text ?? "").Trim(); if (t.Length == 0) continue;
                        l.Add(new P { T = t, L = w.BoundingBox.Left, R = w.BoundingBox.Right, Y = pag.Height - w.BoundingBox.Top });
                    }
                    res.Add(l);
                }
            return res;
        }

        private static List<Lin> Lineas(List<P> ws)
        {
            var lins = new List<Lin>();
            foreach (var w in ws.OrderBy(x => x.Y).ThenBy(x => x.L))
            {
                var l = lins.LastOrDefault();
                if (l == null || Math.Abs(w.Y - l.Y) > 2.5) { l = new Lin { Y = w.Y }; lins.Add(l); }
                l.W.Add(w);
            }
            foreach (var l in lins) l.W = l.W.OrderBy(x => x.L).ToList();
            return lins;
        }

        // Devuelve la tabla (preambulo "Cuenta: N", encabezado, renglones) y notas para la persona.
        // Texto de las primeras paginas, el mismo que ven las condiciones de "pdf contiene".
        public static string TextoInicial(string ruta)
        {
            var pags = Paginas(ruta);
            return string.Join(" ", pags.Take(2).SelectMany(p => p.Select(w => w.T)));
        }

        public static List<string[]> Leer(string ruta, List<string> notas)
        {
            BrosLMV.Bancos.Layouts.Layout usado;
            return Leer(ruta, notas, null, out usado);
        }

        // El layout (de fabrica o de la empresa) que coincide con el texto del PDF dice con que estrategia se lee.
        public static List<string[]> Leer(string ruta, List<string> notas, IEnumerable<BrosLMV.Bancos.Layouts.Layout> layouts, out BrosLMV.Bancos.Layouts.Layout usado)
        {
            usado = null;
            var pags = Paginas(ruta);
            if (pags.Count == 0 || pags.All(p => p.Count == 0)) throw new InvalidOperationException("Este PDF no trae texto (parece una imagen escaneada). Pide al banco el archivo de movimientos en CSV, TXT o Excel.");
            string texto = string.Join(" ", pags.Take(2).SelectMany(p => p.Select(w => w.T)));
            var huella = BrosLMV.Bancos.Layouts.Catalogo.HuellaDe(new List<string[]>(), -1, Path.GetFileName(ruta), texto);
            var elegido = BrosLMV.Bancos.Layouts.Catalogo.Elegir(BrosLMV.Bancos.Layouts.Catalogo.Probar(layouts ?? BrosLMV.Bancos.Layouts.Catalogo.DeFabrica(), huella, "pdf"));
            if (elegido == null) throw new InvalidOperationException("No reconozco este PDF. Por ahora se leen los de BBVA (impresión de BBVANet, impresión del export y estado de cuenta oficial); para otro, crea un layout en el editor.");
            usado = elegido.Layout;
            var movs = new List<M>(); string cuenta = "";
            switch ((usado.Pdf != null ? usado.Pdf.Estrategia : "").ToLowerInvariant())
            {
                case "estadocuenta": movs = EstadoCuenta(pags, notas, out cuenta); break;
                case "export": movs = Export(pags, notas, out cuenta); break;
                case "bbvanet": movs = BbvaNet(pags, out cuenta); break;
                default: throw new InvalidOperationException("El layout «" + usado.Nombre + "» no dice como leer el PDF (estrategia).");
            }
            notas.Insert(0, "PDF leído como " + usado.Nombre + ": " + movs.Count + " movimientos.");
            var filas = new List<string[]>();
            if (cuenta.Length > 0) filas.Add(new[] { "Cuenta: " + cuenta, "", "", "", "" });
            filas.Add(new[] { "Fecha", "Concepto / Referencia", "Cargo", "Abono", "Saldo" });
            var ci2 = CultureInfo.InvariantCulture;
            foreach (var m in movs)
                filas.Add(new[] { m.F.ToString("yyyy-MM-dd", ci2), m.D, m.Cargo != 0 ? m.Cargo.ToString("0.00", ci2) : "", m.Abono != 0 ? m.Abono.ToString("0.00", ci2) : "", m.Saldo.HasValue ? m.Saldo.Value.ToString("0.00", ci2) : "" });
            return filas;
        }

        private static string Desc(IEnumerable<Lin> lins, Func<P, bool> incluir)
        {
            var partes = new List<string>();
            foreach (var l in lins) { var t = string.Join(" ", l.W.Where(incluir).Select(w => w.T)); if (t.Length > 0) partes.Add(t); }
            return string.Join(" / ", partes);
        }

        private static string CuentaDe(List<P> pagina)
        {
            string t = string.Join(" ", Lineas(pagina).Select(l => l.Txt));
            var m = Regex.Match(t, @"(?i)cuenta:?\s*(\d{6,18})"); if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(t, @"(?i)No\.\s*(?:de\s+)?Cuenta\s+(\d{6,18})"); return m.Success ? m.Groups[1].Value : "";
        }

        // ---------- A) impresion de BBVANet ----------
        private static List<M> BbvaNet(List<List<P>> pags, out string cuenta)
        {
            cuenta = CuentaDe(pags[0]);
            string t0 = string.Join(" ", Lineas(pags[0]).Select(l => l.Txt));
            var mi = Regex.Match(t0, @"(\d{2})/(\d{2})/(\d{4})"); int anioImp = mi.Success ? int.Parse(mi.Groups[3].Value) : DateTime.Today.Year, mesImp = mi.Success ? int.Parse(mi.Groups[2].Value) : 12;
            var res = new List<M>(); double cC = 0, cA = 0, cS = 0; bool hay = false;
            foreach (var pg in pags)
            {
                var lins = Lineas(pg);
                var cab = lins.FirstOrDefault(l => l.W.Any(w => w.T.Equals("Cargo", StringComparison.OrdinalIgnoreCase)) && l.W.Any(w => w.T.Equals("Saldo", StringComparison.OrdinalIgnoreCase)));
                if (cab != null)
                {
                    cC = cab.W.First(w => w.T.Equals("Cargo", StringComparison.OrdinalIgnoreCase)).C; cA = cab.W.First(w => w.T.Equals("Abono", StringComparison.OrdinalIgnoreCase)).C; cS = cab.W.First(w => w.T.Equals("Saldo", StringComparison.OrdinalIgnoreCase)).C; hay = true;
                }
                if (!hay) continue;
                double yCab = cab != null ? cab.Y : -1;
                var filas = lins.Where(l => l.Y > yCab + 4 && l.W[0].L < 60 && Regex.IsMatch(l.W[0].T, @"^\d{2}-\d{2}$")).ToList();
                if (filas.Count == 0) continue;
                var bloques = filas.Select(f =>
                {
                    var m = new M(); var d = f.W[0].T.Split('-'); int mes = int.Parse(d[1]); int anio = mes > mesImp ? anioImp - 1 : anioImp;
                    m.F = new DateTime(anio, mes, int.Parse(d[0]));
                    foreach (var w in f.W.Where(x => Monto.IsMatch(x.T)))
                    {
                        double c = w.C; double dc = Math.Abs(c - cC), da = Math.Abs(c - cA), ds = Math.Abs(c - cS);
                        if (ds <= dc && ds <= da) m.Saldo = Num(w.T); else if (dc <= da) m.Cargo = Num(w.T); else m.Abono = Num(w.T);
                    }
                    return new { M = m, Y = f.Y, L = new List<Lin>() };
                }).ToList();
                foreach (var l in lins.Where(x => x.Y > yCab + 4))
                {
                    string txt = l.Txt;
                    if (Regex.IsMatch(txt, @"(?i)http|bbvanet|detalle de movimientos|^\d+/\d+$")) continue;
                    bool esFila = filas.Contains(l);
                    var b = bloques.OrderBy(x => Math.Abs(x.Y - l.Y)).First();
                    if (!esFila && Math.Abs(b.Y - l.Y) > 30) continue;
                    if (esFila) b = bloques[filas.IndexOf(l)];
                    var trozo = new Lin { Y = l.Y, W = l.W.Where(w => w.L >= 62 && !Monto.IsMatch(w.T) && w.T != "$").ToList() };
                    if (trozo.W.Count > 0) b.L.Add(trozo);
                }
                foreach (var b in bloques) { b.M.D = Desc(b.L.OrderBy(x => x.Y), w => true); res.Add(b.M); }
            }
            return res;
        }

        // ---------- B) impresion del export (cargo/abono por cadena de saldos) ----------
        private static List<M> Export(List<List<P>> pags, List<string> notas, out string cuenta)
        {
            cuenta = "";
            var res = new List<M>(); M act = null; var toks = new List<P>(); var desc = new List<string>();
            Action cerrar = () =>
            {
                if (act == null) return;
                var mt = toks.Where(w => Monto.IsMatch(w.T)).ToList();
                if (mt.Count >= 2) { act.Saldo = Num(mt[mt.Count - 1].T); var imp = mt[mt.Count - 2]; decimal v = Num(imp.T); act.Centro = imp.C; if (imp.C < 395) act.Cargo = v; else act.Abono = v; }
                else if (mt.Count == 1) { act.Saldo = Num(mt[0].T); }
                act.D = string.Join(" ", toks.Where(w => !Monto.IsMatch(w.T) && w.T != "$").Select(w => w.T));
                res.Add(act); act = null; toks = new List<P>();
            };
            foreach (var pg in pags)
                foreach (var l in Lineas(pg))
                {
                    string txt = l.Txt;
                    if (Regex.IsMatch(txt, @"(?i)bbvanet\.com|^https?:|^D.a\s+Concepto|^\d{1,2}/\d{1,2}/\d{2},")) continue;
                    if (Regex.IsMatch(l.W[0].T, @"^\d{2}-\d{2}-\d{4}$") && l.W[0].L < 60)
                    {
                        cerrar();
                        var d = l.W[0].T.Split('-'); act = new M { F = new DateTime(int.Parse(d[2]), int.Parse(d[1]), int.Parse(d[0])) };
                        toks.AddRange(l.W.Skip(1));
                    }
                    else if (act != null) toks.AddRange(l.W);
                }
            cerrar();
            // El cargo o abono real lo dice el saldo: del mas nuevo al mas viejo, saldo[i] = saldo[i+1] +/- importe[i].
            int corregidos = 0, sinSaldo = 0;
            for (int i = 0; i < res.Count; i++)
            {
                var m = res[i]; decimal imp = m.Cargo + m.Abono; if (imp == 0 || !m.Saldo.HasValue) { sinSaldo++; continue; }
                if (i + 1 >= res.Count || !res[i + 1].Saldo.HasValue) continue;
                decimal dif = m.Saldo.Value - res[i + 1].Saldo.Value;
                if (Math.Abs(dif - imp) < 0.011m && m.Abono == 0) { m.Abono = imp; m.Cargo = 0; corregidos++; }
                else if (Math.Abs(dif + imp) < 0.011m && m.Cargo == 0) { m.Cargo = imp; m.Abono = 0; corregidos++; }
            }
            if (corregidos > 0) notas.Add("En " + corregidos + " renglón(es) el cargo o abono se decidió por el cambio de saldo, porque la columna del PDF no era clara.");
            return res;
        }

        // ---------- C) estado de cuenta oficial ----------
        private static List<M> EstadoCuenta(List<List<P>> pags, List<string> notas, out string cuenta)
        {
            cuenta = CuentaDe(pags[0]);
            string t0 = string.Join(" ", Lineas(pags[0]).Select(l => l.Txt));
            var per = Regex.Match(t0, @"DEL\s+(\d{2})/(\d{2})/(\d{4})\s+AL\s+(\d{2})/(\d{2})/(\d{4})");
            int anioFin = per.Success ? int.Parse(per.Groups[6].Value) : DateTime.Today.Year, mesFin = per.Success ? int.Parse(per.Groups[5].Value) : 12;
            var res = new List<M>(); M act = null; var tramos = new List<string>();
            Action cerrar = () => { if (act != null) { act.D = string.Join(" / ", tramos); res.Add(act); act = null; tramos = new List<string>(); } };
            foreach (var pg in pags)
            {
                var lins = Lineas(pg);
                var op = pg.FirstOrDefault(w => w.T.StartsWith("OPERACI", StringComparison.OrdinalIgnoreCase) && w.L > 450);
                if (op == null) continue;
                var cargos = pg.FirstOrDefault(w => w.T == "CARGOS" && Math.Abs(w.Y - op.Y) < 4 && w.L > 300); var abonos = pg.FirstOrDefault(w => w.T == "ABONOS" && Math.Abs(w.Y - op.Y) < 4 && w.L > 300);
                var liq = pg.FirstOrDefault(w => w.T.StartsWith("LIQUIDACI", StringComparison.OrdinalIgnoreCase) && w.L > 500);
                if (cargos == null || abonos == null) continue;
                double rC = cargos.R, rA = abonos.R, rS = op.R, rL = liq != null ? liq.R : op.R + 68;
                foreach (var l in lins.Where(x => x.Y > op.Y + 6))
                {
                    string txt = l.Txt;
                    if (Regex.IsMatch(txt, @"^(Estado de Cuenta|PAGINA|No\.|Estimado Cliente)")) continue;
                    bool fila = l.W.Count >= 3 && l.W[0].L < 30 && Regex.IsMatch(l.W[0].T, @"^\d{2}/[A-Za-z]{3}$") && Regex.IsMatch(l.W[1].T, @"^\d{2}/[A-Za-z]{3}$");
                    IEnumerable<P> cuerpo;
                    if (fila)
                    {
                        cerrar();
                        var d = l.W[0].T.Split('/'); int mes; if (!Meses.TryGetValue(d[1].ToUpperInvariant(), out mes)) continue;
                        act = new M { F = new DateTime(mes > mesFin ? anioFin - 1 : anioFin, mes, int.Parse(d[0])) };
                        cuerpo = l.W.Skip(2);
                    }
                    else { if (act == null || l.W[0].L < 80) continue; cuerpo = l.W; }
                    var textos = new List<string>();
                    foreach (var w in cuerpo)
                    {
                        if (w.R > 340 && Monto.IsMatch(w.T))
                        {
                            double dc = Math.Abs(w.R - rC), da = Math.Abs(w.R - rA), ds = Math.Abs(w.R - rS), dl = Math.Abs(w.R - rL); double mn = Math.Min(Math.Min(dc, da), Math.Min(ds, dl));
                            if (mn > 14) { textos.Add(w.T); continue; }
                            if (mn == dc) act.Cargo = Num(w.T); else if (mn == da) act.Abono = Num(w.T); else if (mn == ds) act.Saldo = Num(w.T);
                        }
                        else textos.Add(w.T);
                    }
                    if (textos.Count > 0) tramos.Add(string.Join(" ", textos));
                }
            }
            cerrar();
            // El propio estado de cuenta declara cuantos depositos y retiros hubo: si no cuadra, la lectura esta incompleta.
            try
            {
                var lin1 = Lineas(pags[0]);
                int Declarado(string patron) { foreach (var l in lin1) { var m = Regex.Match(l.Txt, patron); if (m.Success) return int.Parse(m.Groups[1].Value); } return -1; }
                int dAb = Declarado(@"(?i)Dep.sitos\s*/\s*Abonos\s*\(\+\)\s+(\d+)"), dCa = Declarado(@"(?i)Retiros\s*/\s*Cargos\s*\(-\)\s+(\d+)");
                int nAb = res.Count(m => m.Abono != 0), nCa = res.Count(m => m.Cargo != 0);
                if (dAb >= 0 && dCa >= 0)
                {
                    if (dAb == nAb && dCa == nCa) notas.Add("Cuadra con lo que declara el estado de cuenta: " + dAb + " depósitos y " + dCa + " retiros.");
                    else notas.Add("OJO: el estado de cuenta declara " + dAb + " depósitos y " + dCa + " retiros, pero se leyeron " + nAb + " y " + nCa + ". Revisa el detalle.");
                }
            }
            catch { }
            return res;
        }
    }
}
