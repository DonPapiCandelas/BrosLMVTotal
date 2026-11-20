// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BrosLMV.Bancos.Motor
{
    // Estados de cuenta en PDF de Banamex: MiCuenta (cheques) y tarjeta de credito.
    public static partial class BancoPdf
    {
        private static readonly Regex MontoPesos = new Regex(@"^\$(\d{1,3}(,\d{3})+|\d{1,3})\.\d{2}$", RegexOptions.Compiled);

        private static string SinAcento(string s) { return (s ?? "").ToUpperInvariant().Replace("Ó", "O").Replace("É", "E").Replace("Í", "I").Replace("Á", "A").Replace("Ú", "U"); }

        // El PDF a veces parte un importe en dos palabras pegadas («11,417.8» y «9»): se vuelven a unir.
        private static List<Lin> LineasUnidas(List<P> pagina)
        {
            var lins = Lineas(pagina);
            foreach (var l in lins)
            {
                for (int i = 0; i + 1 < l.W.Count; i++)
                {
                    var a = l.W[i]; var b = l.W[i + 1];
                    if (Regex.IsMatch(a.T, @"^(\d{1,3}(,\d{3})*|\d+)\.\d$") && Regex.IsMatch(b.T, @"^\d$") && b.L - a.R < 2.5)
                    {
                        l.W[i] = new P { T = a.T + b.T, L = a.L, R = b.R, Y = a.Y }; l.W.RemoveAt(i + 1);
                    }
                }
            }
            return lins;
        }

        private static int? MesDe(string t)
        {
            int m; return t != null && t.Length >= 3 && Meses.TryGetValue(SinAcento(t).Substring(0, 3), out m) ? m : (int?)null;
        }

        // ---------- Banamex MiCuenta (cuenta de cheques) ----------
        // Cada movimiento empieza en un renglon con «DD MES» y sigue (concepto en varias lineas) hasta el siguiente; los importes salen en cualquier linea
        // del bloque, debajo de RETIROS / DEPOSITOS / SALDO. Un bloque sin retiro ni deposito (solo saldo, o una exencion de comision) no es movimiento.
        private static List<M> BanamexCuenta(List<List<P>> pags, List<string> notas, out string cuenta)
        {
            string t0 = string.Join(" ", pags.Take(2).SelectMany(p => Lineas(p).Select(l => l.Txt)));
            var mc = Regex.Match(t0, @"(?i)N.mero de cuenta de cheques\s+(\d{6,18})"); cuenta = mc.Success ? mc.Groups[1].Value : "";
            var per = Regex.Match(t0, @"(?i)Per.odo del\s+\d{1,2}\s+de\s+(\w+)\s+al\s+\d{1,2}\s+de\s+(\w+)\s+del\s+(\d{4})");
            int anioFin = per.Success ? int.Parse(per.Groups[3].Value) : DateTime.Today.Year, mesFin = per.Success ? (MesDe(per.Groups[2].Value) ?? 12) : 12;
            var res = new List<M>(); M act = null; var tramos = new List<string>(); int sinImporte = 0; bool fin = false;
            Action cerrar = () =>
            {
                if (act == null) return;
                if (act.Cargo != 0 || act.Abono != 0) { act.D = string.Join(" / ", tramos); res.Add(act); } else sinImporte++;
                act = null; tramos = new List<string>();
            };
            foreach (var pg in pags)
            {
                if (fin) break;
                var lins = LineasUnidas(pg);
                // el encabezado de la tabla: FECHA CONCEPTO RETIROS DEPOSITOS SALDO (cada pagina lo repite)
                var cab = lins.FirstOrDefault(l => l.W.Any(w => SinAcento(w.T) == "RETIROS") && l.W.Any(w => SinAcento(w.T) == "DEPOSITOS") && l.W.Any(w => SinAcento(w.T) == "SALDO"));
                if (cab == null) continue;
                double rR = cab.W.First(w => SinAcento(w.T) == "RETIROS").R, rD = cab.W.First(w => SinAcento(w.T) == "DEPOSITOS").R, rS = cab.W.First(w => SinAcento(w.T) == "SALDO").R;
                foreach (var l in lins.Where(x => x.Y > cab.Y + 4))
                {
                    string txt = l.Txt;
                    if (Regex.IsMatch(txt, @"(?i)^(Domiciliaci.n|\d+\s+Instrucci)")) { fin = true; break; }
                    bool fila = l.W.Count >= 2 && l.W[0].L < 45 && Regex.IsMatch(l.W[0].T, @"^\d{1,2}$") && l.W[1].L < 75 && MesDe(l.W[1].T).HasValue && l.W[1].T.Length == 3;
                    IEnumerable<P> cuerpo = l.W;
                    if (fila)
                    {
                        cerrar();
                        int mes = MesDe(l.W[1].T).Value;
                        try { act = new M { F = new DateTime(mes > mesFin ? anioFin - 1 : anioFin, mes, int.Parse(l.W[0].T)) }; } catch { act = null; continue; }
                        cuerpo = l.W.Skip(2);
                    }
                    var textos = new List<string>();
                    foreach (var w in cuerpo)
                    {
                        if (w.L > 300 && Monto.IsMatch(w.T) && act != null)
                        {
                            double dr = Math.Abs(w.R - rR), dd = Math.Abs(w.R - rD), ds = Math.Abs(w.R - rS), mn = Math.Min(dr, Math.Min(dd, ds));
                            if (mn > 25) { textos.Add(w.T); continue; }
                            if (mn == dr) act.Cargo = Num(w.T); else if (mn == dd) act.Abono = Num(w.T); else act.Saldo = Num(w.T);
                        }
                        else if (w.L >= 70 && w.L < 340) textos.Add(w.T);
                    }
                    if (act != null && textos.Count > 0) tramos.Add(string.Join(" ", textos));
                }
            }
            cerrar();
            // El estado de cuenta declara cuantos depositos y retiros hubo y su importe: si no cuadra, falta algun renglon.
            try
            {
                var lin1 = pags.Take(3).SelectMany(Lineas).ToList(); decimal? tAb = null, tCa = null; int nAb = -1, nCa = -1;
                foreach (var l in lin1)
                {
                    var m1 = Regex.Match(l.Txt, @"^\(\+\)\s*(\d+)\s+Dep.sitos\s+([\d,]+\.\d{2})"); if (m1.Success) { nAb = int.Parse(m1.Groups[1].Value); tAb = Num(m1.Groups[2].Value); }
                    var m2 = Regex.Match(l.Txt, @"^\(-\)\s*(\d+)\s+Retiros.*?([\d,]+\.\d{2})\s*$"); if (m2.Success) { nCa = int.Parse(m2.Groups[1].Value); tCa = Num(m2.Groups[2].Value); }
                }
                if (tAb.HasValue && tCa.HasValue)
                {
                    decimal sAb = res.Sum(m => m.Abono), sCa = res.Sum(m => m.Cargo); int cAb = res.Count(m => m.Abono != 0), cCa = res.Count(m => m.Cargo != 0);
                    if (sAb == tAb.Value && sCa == tCa.Value && cAb == nAb && cCa == nCa) notas.Add("Cuadra con lo que declara el estado de cuenta: " + nAb + " depósitos por " + sAb.ToString("#,##0.00") + " y " + nCa + " retiros por " + sCa.ToString("#,##0.00") + ".");
                    else notas.Add("OJO: el estado de cuenta declara " + nAb + " depósitos por " + tAb.Value.ToString("#,##0.00") + " y " + nCa + " retiros por " + tCa.Value.ToString("#,##0.00") + ", pero se leyeron " + cAb + " por " + sAb.ToString("#,##0.00") + " y " + cCa + " por " + sCa.ToString("#,##0.00") + ". Revisa el detalle.");
                }
            }
            catch { }
            if (sinImporte > 0) notas.Add(sinImporte + " renglón(es) del estado de cuenta no traen retiro ni depósito (saldo anterior, exención de comisión…) y no se cargan.");
            return res;
        }

        // ---------- Banamex tarjeta de credito ----------
        // «dd-mmm-aaaa dd-mmm-aaaa  DESCRIPCION  + $importe»: «+» es cargo (compra) y «-» abono (pago). No trae saldo por renglon.
        private static List<M> BanamexTarjeta(List<List<P>> pags, List<string> notas, out string cuenta)
        {
            string t0 = string.Join(" ", pags.Take(2).SelectMany(p => Lineas(p).Select(l => l.Txt)));
            var mc = Regex.Match(t0, @"(?<!\d)(\d{16})(?!\d)"); cuenta = mc.Success ? mc.Groups[1].Value : "";
            var res = new List<M>(); var rxF = new Regex(@"^\d{2}-[A-Za-z]{3}-\d{4}$", RegexOptions.Compiled);
            foreach (var pg in pags)
                foreach (var l in LineasUnidas(pg))
                {
                    if (l.W.Count < 3 || l.W[0].L > 60 || !rxF.IsMatch(l.W[0].T) || !rxF.IsMatch(l.W[1].T)) continue;
                    var imp = l.W.FirstOrDefault(w => MontoPesos.IsMatch(w.T) && w.L > 400);
                    DateTime f; var d0 = l.W[0].T.Split('-'); int mes = MesDe(d0[1]) ?? 0;
                    if (imp == null || mes == 0) continue;
                    f = new DateTime(int.Parse(d0[2]), mes, int.Parse(d0[0]));
                    // el signo («+» cargo, «-» abono) va justo a la izquierda del importe, a veces una linea arriba o abajo
                    var signo = pg.Where(w => (w.T == "+" || w.T == "-" || w.T == "–" || w.T == "−") && w.R <= imp.L + 2 && w.R >= imp.L - 40 && Math.Abs(w.Y - imp.Y) < 6).OrderBy(w => Math.Abs(w.Y - imp.Y)).FirstOrDefault();
                    decimal v = Num(imp.T.TrimStart('$'));
                    var m = new M { F = f, D = string.Join(" ", l.W.Skip(2).Where(w => w.L < 465 && w.T != "+" && w.T != "-").Select(w => w.T)) };
                    if (signo != null && signo.T != "+") m.Abono = v; else m.Cargo = v;
                    res.Add(m);
                }
            // El estado de cuenta trae «Cargos regulares + $X»: si la suma no coincide, falta algun renglon.
            try
            {
                foreach (var pg in pags.Take(1)) foreach (var l in LineasUnidas(pg))
                {
                    var m1 = Regex.Match(l.Txt, @"Cargos regulares.*?\$([\d,]+\.\d{2})"); if (!m1.Success) continue;
                    decimal declarado = Num(m1.Groups[1].Value), leido = res.Sum(m => m.Cargo);
                    notas.Add(declarado == leido ? "Cuadra con los cargos regulares del estado de cuenta: " + leido.ToString("#,##0.00") + "." : "OJO: el estado de cuenta declara cargos regulares por " + declarado.ToString("#,##0.00") + ", pero se leyeron " + leido.ToString("#,##0.00") + ". Revisa el detalle.");
                    break;
                }
            }
            catch { }
            return res;
        }
    }
}
