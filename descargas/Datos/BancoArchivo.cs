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

// BancoArchivo.cs -- estados de cuenta y movimientos bancarios: lectura de archivos, deteccion del banco y de las columnas,
// interpretacion de los renglones y validacion del saldo corrido. SIN base de datos y SIN interfaz: solo texto adentro, movimientos afuera.
//
// Idea central: no se lee "por posicion" sino POR ENCABEZADO. Un banco que agrega una columna (Banorte agrego MONEDA en 2026 y la carga
// de Logical se descompuso en silencio) no rompe nada, y un banco nuevo casi nunca necesita codigo: el mapa de columnas es un dato
// que se puede corregir en pantalla y guardar como perfil.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrosLMV.Descargas.Datos
{
    // Que columna de la tabla del archivo es cada cosa. Todo es -1 si el archivo no la trae.
    internal sealed class BancoMapa
    {
        public int FilaEncabezado { get; set; } = -1;     // -1: el archivo no trae encabezado
        public int PrimeraFila { get; set; }              // primera fila de datos
        public int Fecha { get; set; } = -1;
        public int Descripcion { get; set; } = -1;
        public List<int> Detalle { get; set; } = new List<int>();    // columnas que amplian la descripcion (descripcion larga, concepto...)
        public int Referencia { get; set; } = -1;
        public int Cargo { get; set; } = -1;
        public int Abono { get; set; } = -1;
        public int Importe { get; set; } = -1;            // columna unica con signo (o con Signo aparte)
        public int Signo { get; set; } = -1;              // columna que dice Cargo/Abono ("-", "+", "Cargo", "Abono")
        public int Saldo { get; set; } = -1;
        public int Cuenta { get; set; } = -1;
        public int Moneda { get; set; } = -1;
        public bool ImporteNegativoEsCargo { get; set; } = true;     // en tarjetas como Amex el importe positivo es la compra
        public string FormatoFecha { get; set; } = "auto";           // auto (dd/mm) | mdy
        public string Firma { get; set; } = "";                      // encabezado normalizado: identifica el formato para recordarlo

        public BancoMapa Copiar() { var m = (BancoMapa)MemberwiseClone(); m.Detalle = new List<int>(Detalle); return m; }
        public bool Completo => Fecha >= 0 && (Cargo >= 0 || Abono >= 0 || Importe >= 0);
    }

    internal sealed class BancoDeteccion
    {
        public string Banco = "";            // "" = no se pudo saber
        public string Perfil = "";
        public int Confianza;                // 0 nada, 1 baja, 2 media, 3 alta
        public BancoMapa Mapa = new BancoMapa();
        public List<string> CuentasPreambulo = new List<string>();   // numeros de cuenta/CLABE que el archivo menciona antes de la tabla
        public List<string> Notas = new List<string>();
        public bool MapaPorEncabezado;       // false: se infirmo mirando el contenido (confianza baja)
    }

    internal sealed class BancoMov
    {
        public DateTime Fecha;
        public int Orden;
        public int FilaOrigen;
        public string Descripcion = "", Detalle = "", Referencia = "", CuentaArchivo = "", Moneda = "";
        public decimal Cargo, Abono;
        public decimal? Saldo;
        public string ClaveRastreo = "", RfcContraparte = "", NombreContraparte = "", ClabeContraparte = "", BancoContraparte = "", Tipo = "";
        public string Hash = "";
        public decimal Importe => Abono - Cargo;      // + entra dinero, - sale
    }

    internal sealed class BancoResultado
    {
        public List<BancoMov> Movs = new List<BancoMov>();
        public List<string> Errores = new List<string>();       // bloquean la importacion
        public List<string> Avisos = new List<string>();
        public int FilasIgnoradas;
        public DateTime? Desde, Hasta;
        public decimal? SaldoInicial, SaldoFinal;
        public int RupturasSaldo;                               // renglones donde saldo anterior +/- movimiento no da el saldo
        public bool SaldoVerificado;                            // el archivo trae saldo y se pudo revisar
        public bool SaldoDeuda;                                 // el saldo sube con los cargos (tarjeta de credito)
        public bool Descendente;                                // el archivo venia del mas nuevo al mas viejo
        public decimal TotalCargos => Movs.Sum(m => m.Cargo);
        public decimal TotalAbonos => Movs.Sum(m => m.Abono);
        public IEnumerable<string> Cuentas => Movs.Select(m => m.CuentaArchivo).Where(c => !string.IsNullOrEmpty(c)).Distinct();
    }

    internal static class BancoCatalogo
    {
        public static readonly string[] Bancos = { "Afirme", "American Express", "Banamex", "BanBajío", "Banorte", "Banregio", "BBVA", "HSBC", "Hey Banco", "Kapital", "Monex", "Santander", "Scotiabank", "Otro" };
    }

    internal static class BancoTexto
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

    internal static class BancoValor
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
                if (primero >= 1990 && primero <= 2100) { a = primero; b = int.Parse(t.Substring(4, 2)); c = int.Parse(t.Substring(6, 2)); return Armar(a, b, c, out fecha); }
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
    internal static class BancoCampos
    {
        private static readonly Regex Rastreo = new Regex(@"(?i)(?:clave\s+de\s+rastreo|cve\.?\s*rast\.?|rastreo)\s*[:=]?\s*([A-Z0-9]{5,40})", RegexOptions.Compiled);
        private static readonly Regex Rfc = new Regex(@"(?i)(?:rfc|r\.f\.c\.)\s*(?:ordenante|beneficiario|emisor|receptor)?\s*[:=]?\s*([A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3})\b", RegexOptions.Compiled);
        private static readonly Regex Clabe = new Regex(@"(?<!\d)(\d{18})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex NombreOrdenante = new Regex(@"(?i)ordenante\s*[:=]\s*(.+?)\s*(?:\||cuenta|rfc|clave|$)", RegexOptions.Compiled);
        private static readonly Regex NombreDel = new Regex(@"(?i)(?:del\s+cliente|transferencia\s+de|de\s+parte\s+de)\s+(.+?)\s*(?:,|\||de\s+la\s+clabe|con\s+rfc|$)", RegexOptions.Compiled);
        private static readonly Regex NombreA = new Regex(@"(?i)\benviado\s+a\s+(?:\d{1,3}-)?[A-Z]+\s+a\s+(.+?)\s+a\s+la\s+(?:cta|clabe)", RegexOptions.Compiled);
        private static readonly Regex BancoInst = new Regex(@"(?i)(?:instituci[oó]n\s+contraparte|banco\s+(?:ordenante|emisor))\s*[:=]\s*(.+?)(?=\s+ordenante|\s*\||,|$)", RegexOptions.Compiled);
        private static readonly Regex BancoSpei = new Regex(@"(?i)spei\s+(?:enviado|recibido)\s+(?:de\s+|a\s+)?([A-Za-zÁÉÍÓÚáéíóúñ]+)", RegexOptions.Compiled);

        public static void Extraer(BancoMov m)
        {
            string t = (m.Descripcion + " " + m.Detalle).Trim();
            if (t.Length == 0) return;
            m.ClaveRastreo = Primero(Rastreo, t);
            string rfc = Primero(Rfc, t).ToUpperInvariant();
            m.RfcContraparte = rfc == "ND" ? "" : rfc;
            m.ClabeContraparte = Primero(Clabe, t);
            string nombre = Primero(NombreOrdenante, t);
            if (nombre.Length == 0) nombre = Primero(NombreDel, t);
            if (nombre.Length == 0) nombre = Primero(NombreA, t);
            m.NombreContraparte = nombre.Length > 120 ? nombre.Substring(0, 120) : nombre;
            string banco = Primero(BancoInst, t);
            if (banco.Length == 0) banco = Primero(BancoSpei, t);
            m.BancoContraparte = banco.Length > 60 ? banco.Substring(0, 60) : banco;
            m.Tipo = Clasificar(t, m.Cargo, m.Abono);
        }

        private static string Primero(Regex r, string t) { var x = r.Match(t); return x.Success ? x.Groups[1].Value.Trim() : ""; }

        public static string Clasificar(string texto, decimal cargo, decimal abono)
        {
            string t = BancoTexto.Norm(texto);
            if (Regex.IsMatch(t, @"\biva\b")) return "IVA";
            if (Regex.IsMatch(t, @"comision|com\.|membresia|manejo de cuenta|anualidad|cuota")) return "Comisión";
            if (Regex.IsMatch(t, @"interes|rendimiento|gat ")) return "Intereses";
            if (Regex.IsMatch(t, @"isr|retencion|impuesto")) return "Impuestos";
            if (Regex.IsMatch(t, @"traspaso|trasp ")) return "Traspaso";
            if (Regex.IsMatch(t, @"cheque")) return "Cheque";
            if (Regex.IsMatch(t, @"spei")) return abono > 0 ? "SPEI recibido" : "SPEI enviado";
            if (Regex.IsMatch(t, @"efectivo|deposito en ventanilla|retiro cajero|cajero")) return "Efectivo";
            if (Regex.IsMatch(t, @"tpv|adq|terminal|venta tarjeta|comercio")) return "Terminal / adquirente";
            if (Regex.IsMatch(t, @"pago de tarjeta|pago tarjeta|pago tdc|tdc")) return "Pago de tarjeta";
            if (Regex.IsMatch(t, @"nomina|sueldo")) return "Nómina";
            if (Regex.IsMatch(t, @"interbancario|transferencia|transf")) return abono > 0 ? "Transferencia recibida" : "Transferencia enviada";
            return "";
        }
    }

    internal static class BancoArchivo
    {
        // ---------- Encabezados ----------
        private static readonly string[] AFecha = { "fecha", "fecha movimiento", "fecha (dd/mm/aa)", "fecha y hora", "dia", "fecha operacion", "fecha de operacion", "fecha valor", "fecha de movimiento" };
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
        public static BancoDeteccion Detectar(List<string[]> filas, string nombreArchivo)
        {
            var det = new BancoDeteccion();
            int tope = Math.Min(filas.Count, 80);
            for (int i = 0; i < tope; i++)
            {
                var m = MapaDeEncabezado(filas[i], i);
                if (m != null) { det.Mapa = m; det.MapaPorEncabezado = true; break; }
            }
            if (!det.MapaPorEncabezado) { det.Mapa = InferirSinEncabezado(filas); det.Notas.Add("El archivo no trae encabezado reconocible: las columnas se infirieron por su contenido. Revísalas."); }

            // Cuentas mencionadas arriba de la tabla (preambulo) o en una columna de cuenta.
            int hasta = det.Mapa.FilaEncabezado >= 0 ? det.Mapa.FilaEncabezado : Math.Min(filas.Count, 12);
            var vistos = new HashSet<string>();
            for (int i = 0; i < hasta && i < filas.Count; i++)
            {
                string linea = string.Join(",", filas[i]);
                foreach (Match x in Regex.Matches(linea, @"(?i)\b(?:cuenta|cta\.?|clabe|contrato)\b\W{0,4}(\d{6,18})\b"))
                    if (vistos.Add(x.Groups[1].Value)) det.CuentasPreambulo.Add(x.Groups[1].Value);
            }
            for (int i = 0; i < hasta && i < filas.Count; i++)
            {
                if (!Regex.IsMatch(string.Join(",", filas[i]), @"(?i)conecta\s*banbajio")) continue;      // BanBajio: la cuenta es la celda numerica de la primera linea
                var num = filas[i].FirstOrDefault(x => Regex.IsMatch(x, @"^\d{6,12}$"));
                if (num != null && vistos.Add(num)) det.CuentasPreambulo.Add(num);
            }

            Banco(filas, det, nombreArchivo);
            if (det.Banco == "American Express") det.Mapa.ImporteNegativoEsCargo = false;
            return det;
        }

        private static void Banco(List<string[]> filas, BancoDeteccion det, string nombreArchivo)
        {
            string cab = det.Mapa.FilaEncabezado >= 0 ? string.Join("|", NormFila(filas[det.Mapa.FilaEncabezado])) : "";
            string todo = BancoTexto.Norm(string.Join(" ", filas.Take(40).Select(f => string.Join(" ", f))));
            string arch = BancoTexto.Norm(System.IO.Path.GetFileNameWithoutExtension(nombreArchivo ?? ""));

            void Pon(string banco, int conf, string perfil) { if (conf > det.Confianza) { det.Banco = banco; det.Confianza = conf; det.Perfil = perfil; } }

            if (cab.Contains("no. secuencia") && cab.Contains("codigo")) Pon("Afirme", 3, "Afirme CSV");
            if (cab.Contains("fecha de compra")) Pon("American Express", 3, "American Express CSV");
            if (todo.Contains("detalle de movimientos - depositos y retiros") || todo.Contains("movimiento de cuenta cheques")) Pon("Banamex", 3, "Banamex cheques CSV");
            if (cab.Contains("pagos y depositos") && cab.Contains("compras")) Pon("Banamex", 2, "Tarjeta de crédito TXT");
            if (cab.StartsWith("#|fecha movimiento|hora|recibo") || todo.Contains("conecta banbajio")) Pon("BanBajío", 3, "BanBajío TXT");
            if (cab.Contains("cod. transac") || (cab.Contains("fecha de operacion") && cab.Contains("descripcion detallada"))) Pon("Banorte", 3, "Banorte cuenta");
            if (cab.Contains("tarjeta") && cab.Contains("fecha de operacion") && cab.Contains("importe")) Pon("Banorte", 2, "Banorte tarjeta");
            if (todo.Contains("banregio") || cab.Contains("clasificacion")) Pon("Banregio", cab.Contains("clasificacion") ? 3 : 2, "Banregio CSV");
            if (cab.Contains("referencia ampliada") || cab.Contains("concepto / referencia")) Pon("BBVA", 3, "BBVA TXT");
            if (cab.Contains("cta ordenante") || cab.Contains("banco participante") || (cab.Contains("cargo/abono") && cab.Contains("clave de rastreo"))) Pon("Santander", 3, "Santander CSV");
            if (cab.Contains("cuenta/movimiento") && cab.Contains("estado")) Pon("Kapital", 3, "Kapital CSV");
            foreach (var par in new[] { ("afirme", "Afirme"), ("amex", "American Express"), ("american", "American Express"), ("banamex", "Banamex"), ("citibanamex", "Banamex"), ("bajio", "BanBajío"), ("banorte", "Banorte"),
                ("banregio", "Banregio"), ("bbva", "BBVA"), ("bancomer", "BBVA"), ("hsbc", "HSBC"), ("hey", "Hey Banco"), ("kapital", "Kapital"), ("monex", "Monex"), ("santander", "Santander"), ("scotia", "Scotiabank") })
                if (arch.Contains(par.Item1)) Pon(par.Item2, 1, "Por nombre del archivo");
            if (det.Banco.Length == 0) det.Notas.Add("No reconocí el banco por el contenido: elígelo en la lista (si el formato es nuevo, revisa las columnas y guárdalo como perfil).");
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
            int ignoradasConImporte = 0; var muestraIgnoradas = new List<string>();

            for (int i = mapa.PrimeraFila; i < filas.Count; i++)
            {
                var f = filas[i]; int n = i + 1;
                if (f.All(string.IsNullOrWhiteSpace)) continue;
                if (!BancoValor.Fecha(Celda(f, mapa.Fecha), mapa.FormatoFecha, out DateTime fecha))
                {
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
                    if (cargo < 0) { abono += -cargo; cargo = 0; }          // algunos bancos muestran el retiro en negativo dentro de la columna de cargos
                    if (abono < 0) { cargo += -abono; abono = 0; }
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
                if (cargo == 0 && abono == 0) { r.FilasIgnoradas++; continue; }          // renglones informativos (saldo inicial, totales)

                decimal? saldo = null;
                if (mapa.Saldo >= 0) { string cs = Celda(f, mapa.Saldo); if (BancoValor.EsMontoNoVacio(cs) && BancoValor.Monto(cs, out decimal sd)) saldo = sd; }

                var mov = new BancoMov
                {
                    Fecha = fecha, FilaOrigen = n, Cargo = cargo, Abono = abono, Saldo = saldo,
                    Descripcion = Celda(f, mapa.Descripcion), Referencia = Celda(f, mapa.Referencia),
                    CuentaArchivo = Celda(f, mapa.Cuenta), Moneda = Celda(f, mapa.Moneda).ToUpperInvariant(),
                    Detalle = string.Join(" | ", mapa.Detalle.Select(d => Celda(f, d)).Where(x => x.Length > 0 && x != "-"))
                };
                if (mov.Descripcion.Length == 0 && mov.Detalle.Length > 0) { mov.Descripcion = mov.Detalle; mov.Detalle = ""; }
                crudos.Add(mov);
            }
            if (ignoradasConImporte > 0) r.Avisos.Add(ignoradasConImporte + " fila(s) con importe se ignoraron por no tener una fecha válida (" + string.Join("; ", muestraIgnoradas) + "). Si son movimientos, revisa el formato de fecha.");
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
            if (!estable.SequenceEqual(movs)) { movs.Clear(); movs.AddRange(estable); if (conSaldo) { r.RupturasSaldo = Rupturas(movs, deuda); } }
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
}
