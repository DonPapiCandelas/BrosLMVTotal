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
    public static class BancoArchivo
    {
        // ---------- Encabezados ----------
        private static readonly string[] AFecha = { "fecha de liquidacion", "fecha","fecha movimiento", "fecha (dd/mm/aa)", "fecha y hora", "dia", "fecha operacion", "fecha de operacion", "fecha valor", "fecha de movimiento" };
        private static readonly string[] ADescripcion = { "descripcion", "concepto / referencia", "concepto", "detalle", "descripcion del movimiento" };
        private static readonly string[] ADetalle = { "descripcion detallada", "descripcion larga", "concepto", "referencia ampliada", "detalle", "descripcion corta" };
        private static readonly string[] AReferencia = { "referencia", "recibo", "referencia numerica", "num. referencia" };
        private static readonly string[] ACargo = { "cargo", "cargos", "retiros", "retiro", "compras", "debito", "debitos", "salidas" };
        private static readonly string[] AAbono = { "abono", "abonos", "depositos", "deposito", "pagos y depositos", "credito", "creditos", "entradas", "pagos" };
        private static readonly string[] AImporte = { "importe", "monto", "importe movimiento" };
        private static readonly string[] ASigno = { "cargo/abono", "tipo de movimiento", "cargo / abono", "tipo movimiento" };
        private static readonly string[] ASaldo = { "saldo", "saldos", "saldo final" };
        private static readonly string[] ACuenta = { "cuenta", "cuenta/movimiento", "no. cuenta", "numero de cuenta", "num. cuenta", "no cuenta" };
        private static readonly string[] AMoneda = { "moneda", "divisa" };

        private static int Buscar(string[] celdas, string[] alias, HashSet<int> usadas)
        {
            int mejor = -1, prioridad = int.MaxValue;
            for (int i = 0; i < celdas.Length; i++)
            {
                if (usadas.Contains(i)) continue;
                int p = Array.IndexOf(alias, celdas[i]);
                if (p >= 0 && p < prioridad) { prioridad = p; mejor = i; }
            }
            return mejor;
        }

        private static string[] NormFila(string[] fila) => fila.Select(c => BancoTexto.Norm(BancoTexto.Limpiar(c))).ToArray();

        // Mapa de columnas leyendo el encabezado de UNA fila. Devuelve null si la fila no parece encabezado de movimientos.
        private static BancoMapa MapaDeEncabezado(string[] fila, int indice)
        {
            var c = NormFila(fila); var usadas = new HashSet<int>();
            var m = new BancoMapa { FilaEncabezado = indice, PrimeraFila = indice + 1 };
            int Tomar(string[] alias) { int i = Buscar(c, alias, usadas); if (i >= 0) usadas.Add(i); return i; }
            m.Fecha = Tomar(AFecha);
            m.Saldo = Tomar(ASaldo);
            m.Signo = Tomar(ASigno);
            m.Cargo = Tomar(ACargo);
            m.Abono = Tomar(AAbono);
            m.Importe = Tomar(AImporte);
            m.Descripcion = Tomar(ADescripcion);
            m.Referencia = Tomar(AReferencia);
            m.Cuenta = Tomar(ACuenta);
            m.Moneda = Tomar(AMoneda);
            for (int i = 0; i < c.Length; i++) if (!usadas.Contains(i) && ADetalle.Contains(c[i])) { m.Detalle.Add(i); usadas.Add(i); }
            if (m.Fecha < 0 || (m.Cargo < 0 && m.Abono < 0 && m.Importe < 0)) return null;
            if (m.Signo >= 0 && m.Importe < 0) { m.Signo = -1; }                          // "Cargo/Abono" sin importe aparte no sirve como signo
            m.Firma = string.Join("|", c.Where(x => x.Length > 0));
            return m;
        }

        public static string FirmaDe(string[] filaEncabezado) => string.Join("|", NormFila(filaEncabezado).Where(x => x.Length > 0));

        // Un perfil guardado se acomoda a un archivo nuevo: busca la fila cuyo encabezado es el de la firma (el preambulo puede tener otro largo).
        // Sin encabezado (firma "sin-encabezado|n") se usa tal cual. Devuelve null si el archivo ya no trae ese encabezado.
        public static BancoMapa Reubicar(List<string[]> filas, BancoMapa guardado)
        {
            var m = guardado.Copiar();
            if (guardado.FilaEncabezado < 0) return m;
            int salto = guardado.PrimeraFila - guardado.FilaEncabezado;
            for (int i = 0; i < Math.Min(filas.Count, 120); i++)
                if (FirmaDe(filas[i]) == guardado.Firma) { m.FilaEncabezado = i; m.PrimeraFila = i + salto; return m; }
            return null;
        }

        // ---------- Deteccion ----------
        public static BancoDeteccion Detectar(List<string[]> filas, string nombreArchivo, IEnumerable<BrosLMV.Bancos.Layouts.Layout> layouts = null)
        {
            var det = new BancoDeteccion();
            int tope = Math.Min(filas.Count, 80);
            for (int i = 0; i < tope; i++)
            {
                var m = MapaDeEncabezado(filas[i], i);
                if (m != null) { det.Mapa = m; det.MapaPorEncabezado = true; break; }
            }
            if (!det.MapaPorEncabezado) { det.Mapa = InferirSinEncabezado(filas); det.Notas.Add("El archivo no trae encabezado reconocible: las columnas se infirieron por su contenido. Revísalas."); }

            Banco(filas, det, nombreArchivo, layouts ?? BrosLMV.Bancos.Layouts.Catalogo.DeFabrica());

            // Cuentas mencionadas arriba de la tabla (preambulo) o en una columna de cuenta.
            int hasta = det.Mapa.FilaEncabezado >= 0 ? det.Mapa.FilaEncabezado : Math.Min(filas.Count, 12);
            var vistos = new HashSet<string>();
            for (int i = 0; i < hasta && i < filas.Count; i++)
            {
                string linea = string.Join(",", filas[i]);
                foreach (Match x in Regex.Matches(linea, @"(?i)\b(?:cuenta|cta\.?|clabe|contrato)\b\W{0,4}(\d{6,18})\b"))
                    if (vistos.Add(x.Groups[1].Value)) det.CuentasPreambulo.Add(x.Groups[1].Value);
                // Tarjetas: "Número de Tarjeta: ****12677" (el banco enmascara el número; basta con los últimos dígitos)
                foreach (Match x in Regex.Matches(linea, @"(?i)\btarjeta\b[^0-9]{0,12}(\d{4,16})\b"))
                    if (vistos.Add(x.Groups[1].Value)) det.CuentasPreambulo.Add(x.Groups[1].Value);
            }
            for (int i = 0; i < hasta && i < filas.Count; i++)
            {
                if (!Regex.IsMatch(string.Join(",", filas[i]), @"(?i)conecta\s*banbajio")) continue;      // BanBajio: la cuenta es la celda numerica de la primera linea
                var num = filas[i].FirstOrDefault(x => Regex.IsMatch(x, @"^\d{6,12}$"));
                if (num != null && vistos.Add(num)) det.CuentasPreambulo.Add(num);
            }

            if (det.Banco == "American Express") det.Mapa.ImporteNegativoEsCargo = false;
            return det;
        }

        // Los layouts (de fabrica o de la empresa) deciden el banco y el perfil. Si ninguno coincide, quedan las pistas del nombre del archivo.
        private static readonly (string, string)[] PistasDeNombre = { ("afirme", "Afirme"), ("amex", "American Express"), ("american", "American Express"), ("banamex", "Banamex"), ("citibanamex", "Banamex"), ("bajio", "BanBajío"), ("banorte", "Banorte"),
            ("banregio", "Banregio"), ("bbva", "BBVA"), ("bancomer", "BBVA"), ("hsbc", "HSBC"), ("hey", "Hey Banco"), ("kapital", "Kapital"), ("monex", "Monex"), ("santander", "Santander"), ("scotia", "Scotiabank") };

        private static void Banco(List<string[]> filas, BancoDeteccion det, string nombreArchivo, IEnumerable<BrosLMV.Bancos.Layouts.Layout> layouts)
        {
            // Cada layout se prueba con SU fila de encabezado (si trae columnas propias) o con la que dedujo el motor.
            var filaDe = new Dictionary<BrosLMV.Bancos.Layouts.Layout, int>();
            det.Coincidencias = new List<BrosLMV.Bancos.Layouts.Coincidencia>();
            foreach (var l in layouts.Where(x => string.Equals(x.Formato, "tabla", StringComparison.OrdinalIgnoreCase)))
            {
                int fila = det.Mapa.FilaEncabezado;
                if (l.Columnas != null && l.Columnas.Count > 0) { int f = FilaEncabezadoDe(l, filas); if (f != int.MinValue) fila = f; }
                filaDe[l] = fila;
                var huella = BrosLMV.Bancos.Layouts.Catalogo.HuellaDe(filas, fila, nombreArchivo, null);
                det.Coincidencias.Add(new BrosLMV.Bancos.Layouts.Coincidencia { Layout = l, Porcentaje = BrosLMV.Bancos.Layouts.Catalogo.Porcentaje(l, huella) });
            }
            var elegido = BrosLMV.Bancos.Layouts.Catalogo.Elegir(det.Coincidencias);
            if (elegido != null)
            {
                var l = elegido.Layout;
                det.Layout = l;
                det.Banco = l.Banco; det.Confianza = l.Confianza; det.Perfil = l.Nombre;
                det.Notas.AddRange(l.Notas);
                if (l.Columnas != null && l.Columnas.Count > 0)
                {
                    var m = MapaDeLayout(l, filas, filaDe[l], det.Mapa);
                    if (m != null) { det.Mapa = m; det.MapaPorEncabezado = m.FilaEncabezado >= 0; det.Notas.RemoveAll(x => x.StartsWith("El archivo no trae encabezado reconocible")); }
                }
                if (l.Ajustes != null)
                {
                    if (l.Ajustes.ImporteNegativoEsCargo.HasValue) det.Mapa.ImporteNegativoEsCargo = l.Ajustes.ImporteNegativoEsCargo.Value;
                    if (!string.IsNullOrEmpty(l.Ajustes.FormatoFecha)) det.Mapa.FormatoFecha = l.Ajustes.FormatoFecha;
                    if (l.Ajustes.CuentaDesdeDivisa) det.Mapa.Cuenta = det.Mapa.Moneda;
                }
            }
            if (det.Confianza == 0)
            {
                string arch = BancoTexto.Norm(System.IO.Path.GetFileNameWithoutExtension(nombreArchivo ?? ""));
                foreach (var par in PistasDeNombre)
                    if (arch.Contains(par.Item1)) { det.Banco = par.Item2; det.Confianza = 1; det.Perfil = "Por nombre del archivo"; break; }
            }
            if (det.Banco.Length == 0) det.Notas.Add("No reconocí el banco por el contenido: elígelo en la lista (si el formato es nuevo, revisa las columnas y guárdalo como perfil).");
        }

        // Fila (0-based) de encabezado de un layout con columnas propias: la que dice FilaEncabezado o la primera que trae todos los nombres. MinValue = no esta.
        private static int FilaEncabezadoDe(BrosLMV.Bancos.Layouts.Layout l, List<string[]> filas)
        {
            if (l.FilaEncabezado > 0) return l.FilaEncabezado - 1 < filas.Count ? l.FilaEncabezado - 1 : int.MinValue;
            var nombres = l.Columnas.Values.SelectMany(v => v).Where(n => !n.StartsWith("#")).Select(n => BancoTexto.Norm(n)).Where(n => n.Length > 0).Distinct().ToList();
            if (nombres.Count == 0) return -1;                                   // todo por posicion: la tabla no tiene encabezado
            for (int i = 0; i < Math.Min(filas.Count, 120); i++)
            {
                var celdas = NormFila(filas[i]);
                if (nombres.All(n => celdas.Contains(n))) return i;
            }
            return int.MinValue;
        }

        private static int ColumnaDe(List<string> nombres, string[] celdas)
        {
            foreach (var n in nombres ?? new List<string>())
            {
                if (n.StartsWith("#") && int.TryParse(n.Substring(1), out int pos)) return pos - 1;
                int i = Array.IndexOf(celdas, BancoTexto.Norm(n));
                if (i >= 0) return i;
            }
            return -1;
        }

        // Mapa de columnas que dicta el layout; lo que el layout no menciona se conserva de lo que dedujo el motor.
        private static BancoMapa MapaDeLayout(BrosLMV.Bancos.Layouts.Layout l, List<string[]> filas, int fila, BancoMapa deducido)
        {
            var celdas = fila >= 0 && fila < filas.Count ? NormFila(filas[fila]) : new string[0];
            var m = deducido.FilaEncabezado == fila ? deducido.Copiar() : new BancoMapa { FilaEncabezado = fila };
            if (deducido.FilaEncabezado != fila) { m.Fecha = m.Descripcion = m.Referencia = m.Cargo = m.Abono = m.Importe = m.Signo = m.Saldo = m.Cuenta = m.Moneda = -1; m.Detalle = new List<int>(); }
            m.FilaEncabezado = fila;
            m.PrimeraFila = l.PrimeraFila > 0 ? l.PrimeraFila - 1 : fila + 1;
            foreach (var kv in l.Columnas)
            {
                string campo = kv.Key.ToLowerInvariant();
                if (campo == "detalle") { m.Detalle = kv.Value.Select(n => ColumnaDe(new List<string> { n }, celdas)).Where(i => i >= 0).ToList(); continue; }
                int idx = ColumnaDe(kv.Value, celdas);
                switch (campo)
                {
                    case "fecha": m.Fecha = idx; break;
                    case "descripcion": m.Descripcion = idx; break;
                    case "referencia": m.Referencia = idx; break;
                    case "cargo": m.Cargo = idx; break;
                    case "abono": m.Abono = idx; break;
                    case "importe": m.Importe = idx; break;
                    case "signo": m.Signo = idx; break;
                    case "saldo": m.Saldo = idx; break;
                    case "cuenta": m.Cuenta = idx; break;
                    case "moneda": m.Moneda = idx; break;
                }
            }
            if (fila >= 0 && fila < filas.Count) m.Firma = FirmaDe(filas[fila]); else m.Firma = "sin-encabezado|" + l.Id;
            return m;
        }

        // Sin encabezado: la columna de fechas es la que casi siempre parsea como fecha, las de importes las que parsean como monto.
        private static BancoMapa InferirSinEncabezado(List<string[]> filas)
        {
            var m = new BancoMapa();
            int ncol = filas.Take(200).Select(f => f.Length).DefaultIfEmpty(0).Max();
            var candidatas = filas.Where(f => f.Any(c => BancoValor.Fecha(c, "auto", out _))).Take(200).ToList();
            if (candidatas.Count == 0 || ncol == 0) return m;
            m.PrimeraFila = filas.IndexOf(candidatas[0]);
            double Tasa(int col, Func<string, bool> ok) => candidatas.Count(f => col < f.Length && ok(f[col])) / (double)candidatas.Count;
            for (int j = 0; j < ncol; j++) if (Tasa(j, c => BancoValor.Fecha(c, "auto", out _)) >= 0.8) { m.Fecha = j; break; }
            var palabrasSigno = new HashSet<string> { "cargo", "abono", "-", "+", "c", "a", "debito", "credito" };
            for (int j = 0; j < ncol; j++) if (j != m.Fecha && Tasa(j, c => palabrasSigno.Contains(BancoTexto.Norm(c))) >= 0.8) { m.Signo = j; break; }
            var numericas = new List<int>();
            for (int j = 0; j < ncol; j++)
                if (j != m.Fecha && j != m.Signo && Tasa(j, c => BancoValor.EsMontoNoVacio(c) && Regex.IsMatch(c, @"[.,]\d{1,2}\s*$")) >= 0.6) numericas.Add(j);
            if (m.Signo >= 0) { if (numericas.Count > 0) m.Importe = numericas[0]; if (numericas.Count > 1) m.Saldo = numericas[1]; }
            else if (numericas.Count >= 3) { m.Cargo = numericas[0]; m.Abono = numericas[1]; m.Saldo = numericas[2]; }
            else if (numericas.Count == 2) { m.Importe = numericas[0]; m.Saldo = numericas[1]; }
            else if (numericas.Count == 1) m.Importe = numericas[0];
            int mejor = -1; double largo = 0;
            for (int j = 0; j < ncol; j++)
            {
                if (j == m.Fecha || j == m.Signo || numericas.Contains(j)) continue;
                double l = candidatas.Average(f => j < f.Length ? f[j].Length : 0);
                if (l > largo) { largo = l; mejor = j; }
            }
            m.Descripcion = mejor;
            m.Firma = "sin-encabezado|" + ncol;
            return m;
        }

        // ---------- Interpretacion ----------
        public static BancoResultado Interpretar(List<string[]> filas, BancoMapa mapa)
        {
            var r = new BancoResultado();
            if (!mapa.Completo) { r.Errores.Add("Faltan columnas: se necesita al menos la fecha y el cargo, el abono o el importe."); return r; }
            string Celda(string[] f, int i) => i >= 0 && i < f.Length ? BancoTexto.Limpiar(f[i]) : "";
            var crudos = new List<BancoMov>();
            int ignoradasConImporte = 0, enTransito = 0, infoMonedaExtranjera = 0; var muestraIgnoradas = new List<string>(); string tarjetaSeccion = "";

            for (int i = mapa.PrimeraFila; i < filas.Count; i++)
            {
                var f = filas[i]; int n = i + 1;
                if (f.All(string.IsNullOrWhiteSpace)) continue;
                if (BancoTexto.Norm(Celda(f, 0)).StartsWith("ordenes de pago")) break;          // Monex: despues de los movimientos viene otra tabla (ordenes de pago pendientes o canceladas)
                if (!BancoValor.Fecha(Celda(f, mapa.Fecha), mapa.FormatoFecha, out DateTime fecha))
                {
                    var sec = Regex.Match(Celda(f, mapa.Fecha), @"^(?i:(titular|adicional|digital|virtual))\s*\*?\s*(\d{4})$");   // renglon que abre la seccion de una tarjeta del mismo plastico/cuenta
                    if (sec.Success) { tarjetaSeccion = char.ToUpperInvariant(sec.Groups[1].Value[0]) + sec.Groups[1].Value.Substring(1).ToLowerInvariant() + " *" + sec.Groups[2].Value; continue; }
                    r.FilasIgnoradas++;
                    bool conImporte = new[] { mapa.Cargo, mapa.Abono, mapa.Importe }.Any(c => c >= 0 && BancoValor.EsMontoNoVacio(Celda(f, c)) && BancoValor.Monto(Celda(f, c), out decimal x) && x != 0);
                    if (conImporte && crudos.Count > 0 && Celda(f, mapa.Fecha).Length > 0) { ignoradasConImporte++; if (muestraIgnoradas.Count < 5) muestraIgnoradas.Add("fila " + n + ": «" + Celda(f, mapa.Fecha) + "»"); }
                    continue;
                }

                decimal cargo = 0, abono = 0; bool ok = true;
                if (mapa.Cargo >= 0 || mapa.Abono >= 0)
                {
                    if (!BancoValor.Monto(Celda(f, mapa.Cargo), out cargo)) { r.Errores.Add("Fila " + n + ": el cargo «" + Celda(f, mapa.Cargo) + "» no es un número."); ok = false; }
                    if (!BancoValor.Monto(Celda(f, mapa.Abono), out abono)) { r.Errores.Add("Fila " + n + ": el abono «" + Celda(f, mapa.Abono) + "» no es un número."); ok = false; }
                    if (cargo < 0) cargo = -cargo;                           // BBVA muestra el cargo con signo menos dentro de la columna de cargos: sigue siendo cargo
                    if (abono < 0) abono = -abono;                          // tarjetas BBVA: el pago a la tarjeta viene como abono con signo menos
                }
                else
                {
                    if (!BancoValor.Monto(Celda(f, mapa.Importe), out decimal imp)) { r.Errores.Add("Fila " + n + ": el importe «" + Celda(f, mapa.Importe) + "» no es un número."); ok = false; }
                    else
                    {
                        bool? esCargo = null;
                        if (mapa.Signo >= 0)
                        {
                            string sg = BancoTexto.Norm(Celda(f, mapa.Signo));
                            if (sg.StartsWith("cargo") || sg == "-" || sg == "c" || sg.StartsWith("debito") || sg.StartsWith("retiro")) esCargo = true;
                            else if (sg.StartsWith("abono") || sg == "+" || sg == "a" || sg.StartsWith("credito") || sg.StartsWith("deposito")) esCargo = false;
                        }
                        if (esCargo == null) esCargo = mapa.ImporteNegativoEsCargo ? imp < 0 : imp > 0;
                        if (esCargo == true) cargo = Math.Abs(imp); else abono = Math.Abs(imp);
                    }
                }
                if (!ok) continue;
                if (cargo == 0 && abono == 0)                                              // renglones informativos (saldo inicial, totales, importe en moneda extranjera)
                {
                    r.FilasIgnoradas++;
                    if (Regex.IsMatch(Celda(f, mapa.Descripcion), @"^\d{1,2}/\d{1,2}/\d{2,4}\s+[\d.,]+\s+[A-Z]{3}$")) infoMonedaExtranjera++;
                    continue;
                }

                decimal? saldo = null;
                if (mapa.Saldo >= 0)
                {
                    string cs = Celda(f, mapa.Saldo);
                    if (BancoValor.EsMontoNoVacio(cs) && BancoValor.Monto(cs, out decimal sd)) saldo = sd;
                    else if (BancoTexto.Norm(cs).Contains("transito")) enTransito++;      // aun no esta aplicado: sin saldo
                }

                var mov = new BancoMov
                {
                    Fecha = fecha, FilaOrigen = n, Cargo = cargo, Abono = abono, Saldo = saldo,
                    Descripcion = Celda(f, mapa.Descripcion), Referencia = Celda(f, mapa.Referencia),
                    CuentaArchivo = Celda(f, mapa.Cuenta), Moneda = Celda(f, mapa.Moneda).ToUpperInvariant(),
                    Detalle = string.Join(" | ", mapa.Detalle.Select(d => Celda(f, d)).Where(x => x.Length > 0 && x != "-"))
                };
                if (mov.Descripcion.Length == 0 && mov.Detalle.Length > 0) { mov.Descripcion = mov.Detalle; mov.Detalle = ""; }
                if (tarjetaSeccion.Length > 0) mov.Detalle = (mov.Detalle.Length > 0 ? mov.Detalle + " | " : "") + tarjetaSeccion;
                crudos.Add(mov);
            }
            if (ignoradasConImporte > 0) r.Avisos.Add(ignoradasConImporte + " fila(s) con importe se ignoraron por no tener una fecha válida (" + string.Join("; ", muestraIgnoradas) + "). Si son movimientos, revisa el formato de fecha.");
            int enRevision = crudos.Count(x => BancoTexto.Norm(x.Descripcion).StartsWith("en proceso de revision"));
            if (enRevision > 0) r.Avisos.Add(enRevision + " movimiento(s) están «En proceso de revisión» en el banco: aún no son definitivos; conviene cargarlos cuando se apliquen.");
            if (infoMonedaExtranjera > 0) r.Avisos.Add(infoMonedaExtranjera + " renglón(es) solo informan el importe original en moneda extranjera (sin importe en pesos) y no se cargan: el cargo en pesos es el renglón de la compra.");
            if (crudos.Count == 500) r.Avisos.Add("El archivo trae exactamente 500 movimientos: el banco limita la exportación a 500 y puede faltar el inicio o el final del periodo. Descárgalo por periodos más cortos.");
            if (enTransito > 0) r.Avisos.Add(enTransito + " movimiento(s) están «En tránsito» (el banco aún no los aplica): se cargan sin saldo y no se vuelven a cargar cuando aparezcan aplicados.");
            if (crudos.Count == 0) { if (r.Errores.Count == 0) r.Errores.Add("No encontré ningún movimiento con fecha e importe. Revisa las columnas elegidas."); return r; }

            Ordenar(crudos, r);
            var vistos = new Dictionary<string, int>();
            for (int k = 0; k < crudos.Count; k++)
            {
                var m = crudos[k]; m.Orden = k + 1;
                BancoCampos.Extraer(m);
                string clave = m.Fecha.ToString("yyyyMMdd") + "|" + m.Cargo.ToString("0.00", CultureInfo.InvariantCulture) + "|" + m.Abono.ToString("0.00", CultureInfo.InvariantCulture) + "|" +
                    (m.Saldo.HasValue ? m.Saldo.Value.ToString("0.00", CultureInfo.InvariantCulture) : "") + "|" + BancoTexto.Norm(m.Descripcion) + "|" + BancoTexto.Norm(m.Referencia) + "|" + m.CuentaArchivo;
                vistos.TryGetValue(clave, out int veces); vistos[clave] = veces + 1;
                m.Hash = Sha1(clave + "#" + veces);      // dos renglones identicos el mismo dia son dos movimientos: el contador los distingue
            }
            r.Movs = crudos;
            r.Desde = crudos.Min(x => x.Fecha); r.Hasta = crudos.Max(x => x.Fecha);
            var primero = crudos[0]; var ultimo = crudos[crudos.Count - 1];
            if (primero.Saldo.HasValue) r.SaldoInicial = primero.Saldo.Value - (r.SaldoDeuda ? primero.Cargo - primero.Abono : primero.Abono - primero.Cargo);
            if (ultimo.Saldo.HasValue) r.SaldoFinal = ultimo.Saldo;
            var cuentas = r.Cuentas.ToList();
            if (cuentas.Count > 1) r.Avisos.Add("El archivo trae movimientos de " + cuentas.Count + " cuentas distintas: " + string.Join(", ", cuentas) + ".");
            return r;
        }

        // Pone los movimientos en orden cronologico y revisa que el saldo corrido cuadre renglon por renglon. Prueba las cuatro combinaciones
        // (orden del archivo o invertido, saldo normal o de deuda) y se queda con la que menos se rompe.
        private static void Ordenar(List<BancoMov> movs, BancoResultado r)
        {
            bool conSaldo = movs.Count(m => m.Saldo.HasValue) >= 2 && movs.Count(m => m.Saldo.HasValue) >= movs.Count * 0.9;
            bool desc = movs.Count > 1 && movs[0].Fecha > movs[movs.Count - 1].Fecha;
            bool deuda = false;
            if (conSaldo)
            {
                // Con varias cuentas mezcladas en el mismo archivo el saldo corrido solo tiene sentido por cuenta.
                int mejor = int.MaxValue;
                foreach (bool inv in new[] { false, true })
                    foreach (bool d in new[] { false, true })
                    {
                        int rot = Rupturas(inv ? Enumerable.Reverse(movs).ToList() : movs, d);
                        bool preferida = inv == desc && !d;                                  // a igualdad, el orden que dicen las fechas y saldo normal
                        if (rot < mejor || (rot == mejor && preferida)) { mejor = rot; desc = inv; deuda = d; }
                    }
                r.RupturasSaldo = mejor; r.SaldoVerificado = true;
            }
            if (desc) movs.Reverse();
            // El archivo puede mezclar fechas fuera de orden por cuenta; se ordena estable por fecha (el orden del archivo manda dentro del dia).
            var estable = movs.Select((m, i) => (m, i)).OrderBy(x => x.m.Fecha).ThenBy(x => x.i).Select(x => x.m).ToList();
            // Con saldo, el orden que cuadra manda sobre la fecha: BBVA lista un movimiento del sabado (aplicado el lunes) entre los del lunes.
            if (!estable.SequenceEqual(movs))
            {
                int rotEstable = conSaldo ? Rupturas(estable, deuda) : 0;
                if (!conSaldo || rotEstable < r.RupturasSaldo) { movs.Clear(); movs.AddRange(estable); if (conSaldo) r.RupturasSaldo = rotEstable; }
            }
            r.Descendente = desc; r.SaldoDeuda = deuda;
            if (conSaldo && r.RupturasSaldo > 0)
                r.Avisos.Add("El saldo no cuadra en " + r.RupturasSaldo + " renglón(es): falta algún movimiento, el archivo está incompleto o se mezclaron cuentas.");
            else if (conSaldo && deuda) r.Avisos.Add("El saldo sube con los cargos: se trata de una tarjeta de crédito (saldo de deuda).");
        }

        private static int Rupturas(List<BancoMov> movs, bool deuda)
        {
            int n = 0;
            for (int i = 1; i < movs.Count; i++)
            {
                if (!movs[i].Saldo.HasValue || !movs[i - 1].Saldo.HasValue) continue;
                if (movs[i].CuentaArchivo != movs[i - 1].CuentaArchivo) continue;
                decimal esperado = movs[i - 1].Saldo.Value + (deuda ? movs[i].Cargo - movs[i].Abono : movs[i].Abono - movs[i].Cargo);
                if (Math.Abs(esperado - movs[i].Saldo.Value) > 0.011m) n++;
            }
            return n;
        }

        private static string Sha1(string s)
        {
            using (var h = SHA1.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "");
        }

        // ---------- Cuenta del archivo contra las cuentas registradas ----------
        // Dos numeros "son la misma cuenta" si uno termina con el otro (cuenta de 10 digitos vs CLABE de 18 que la contiene vs ultimos 4).
        public static bool MismaCuenta(string a, string b)
        {
            string x = Regex.Replace(a ?? "", @"\D", "").TrimStart('0'), y = Regex.Replace(b ?? "", @"\D", "").TrimStart('0');
            if (x.Length < 4 || y.Length < 4) return false;
            return x == y || (x.Length >= 6 && y.Contains(x)) || (y.Length >= 6 && x.Contains(y)) || (x.Length == 4 && y.EndsWith(x)) || (y.Length == 4 && x.EndsWith(y));
        }
    }

    // ======================================================================================================================
    //  PDF de BBVA -> misma tabla de texto que un CSV (PdfPig lee las palabras con su posicion; no hay OCR: el PDF debe traer texto)
    //    A) impresion de BBVANet "Detalle de movimientos" (fecha dd-MM, cargo/abono/saldo por columna, descripcion arriba y abajo del renglon)
    //    B) impresion del export de BBVANet (tabla HTML en PDF: las columnas se mueven, el cargo/abono se decide por el saldo)
    //    C) estado de cuenta oficial (OPER/LIQ/COD., columnas fijas, saldo solo al cierre de algunos dias; se cuadra con los totales que declara)
    // ======================================================================================================================
}
