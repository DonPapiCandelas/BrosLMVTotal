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
    public static class BancoCampos
    {
        private static readonly Regex Rastreo = new Regex(@"(?i)(?:clave\s+de\s+rastreo|cve\.?\s*rast\.?|rastreo)\s*[:=]?\s*([A-Z0-9]{5,40})", RegexOptions.Compiled);
        private static readonly Regex Rfc = new Regex(@"(?i)(?:rfc|r\.f\.c\.)\s*(?:ordenante|beneficiario|emisor|receptor)?\s*[:=]?\s*([A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3})\b", RegexOptions.Compiled);
        private static readonly Regex Clabe = new Regex(@"(?<!\d)(\d{18})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex NombreOrdenante = new Regex(@"(?i)ordenante\s*[:=]\s*(.+?)\s*(?:\||cuenta|rfc|clave|$)", RegexOptions.Compiled);
        private static readonly Regex NombreDel = new Regex(@"(?i)(?:del\s+cliente|transferencia\s+de|de\s+parte\s+de)\s+(.+?)\s*(?:,|\||de\s+la\s+clabe|con\s+rfc|$)", RegexOptions.Compiled);
        private static readonly Regex NombreA = new Regex(@"(?i)\benviado\s+a\s+(?:\d{1,3}-)?[A-Z]+\s+a\s+(.+?)\s+a\s+la\s+(?:cta|clabe)", RegexOptions.Compiled);
        private static readonly Regex BancoInst = new Regex(@"(?i)(?:instituci[oó]n\s+contraparte|banco\s+(?:ordenante|emisor))\s*[:=]\s*(.+?)(?=\s+ordenante|\s*\||,|$)", RegexOptions.Compiled);
        private static readonly Regex BancoSpei = new Regex(@"(?i)spei\s+(?:enviado|recibido)\s+(?:de\s+|a\s+)?([A-Za-zÁÉÍÓÚáéíóúñ]+)", RegexOptions.Compiled);

        // BBVA (movimientos en TXT): "SPEI ENVIADO SANTANDER/0045504497  014 0150626PAGO DE NOMINA" -> banco pegado al tipo, folio, codigo de banco (014), referencia numerica y concepto.
        // Monex: "Nombre Beneficiario: X | Clave de Rastreo: ..." y "Nombre Receptor: BBVA MEXICO |"
        private static readonly Regex NombreBenef = new Regex(@"(?i)nombre\s+beneficiario\s*:\s*(.+?)\s*(?:\||$)", RegexOptions.Compiled);
        private static readonly Regex BancoReceptor = new Regex(@"(?i)nombre\s+receptor\s*:\s*(.+?)\s*(?:\||$)", RegexOptions.Compiled);
        private static readonly Regex BancoBbva =new Regex(@"(?i)spei\s+(?:enviado|recibido)\s*([A-Za-zÁÉÍÓÚáéíóúñ ]+?)\s*/", RegexOptions.Compiled);
        private static readonly Regex FolioBbva = new Regex(@"/\s*(\d{7,12})\s+(\d{3})\s", RegexOptions.Compiled);
        private static readonly Dictionary<string, string> CodigosBanco = new Dictionary<string, string> { { "002", "Banamex" }, { "012", "BBVA" }, { "014", "Santander" }, { "021", "HSBC" }, { "030", "BanBajío" }, { "036", "Inbursa" }, { "044", "Scotiabank" }, { "072", "Banorte" }, { "127", "Azteca" }, { "137", "BanCoppel" }, { "138", "Ualá" }, { "646", "STP" }, { "699", "Fondeadora" }, { "721", "Mercado Pago" } };

        // Con reglas del layout: primero lo que dice el layout (extraer y tipo), y lo general solo donde el layout no dijo nada.
    public static void Extraer(BancoMov m, BancoMapa mapa)
    {
        Extraer(m);
        if (mapa == null) return;
        string t = (m.Descripcion + " " + m.Detalle).Trim();
        if (mapa.Extraer != null)
            foreach (var r in mapa.Extraer)
            {
                try
                {
                    var x = Regex.Match(t, r.Patron, RegexOptions.IgnoreCase);
                    if (!x.Success) continue;
                    string v = (x.Groups.Count > 1 ? x.Groups[1].Value : x.Value).Trim();
                    switch ((r.Campo ?? "").ToLowerInvariant())
                    {
                        case "clavederastreo": case "rastreo": m.ClaveRastreo = v; break;
                        case "rfc": m.RfcContraparte = v.ToUpperInvariant(); break;
                        case "clabe": m.ClabeContraparte = v; break;
                        case "nombre": m.NombreContraparte = v; break;
                        case "banco": m.BancoContraparte = v; break;
                        case "referencia": m.Referencia = v; break;
                    }
                }
                catch (ArgumentException) { }          // patron mal escrito: se ignora (el editor lo avisa)
            }
        if (mapa.Tipos != null)
        {
            string n = BancoTexto.Norm(t);
            foreach (var r in mapa.Tipos)
            {
                string cuando = (r.Cuando ?? "ambos").ToLowerInvariant();
                if (cuando == "cargo" && m.Cargo <= 0) continue;
                if (cuando == "abono" && m.Abono <= 0) continue;
                try { if (Regex.IsMatch(n, r.Patron, RegexOptions.IgnoreCase)) { m.Tipo = r.Tipo; break; } }
                catch (ArgumentException) { }
            }
        }
    }

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
            if (nombre.Length == 0) nombre = Primero(NombreBenef, t);
            m.NombreContraparte = nombre.Length > 120 ? nombre.Substring(0, 120) : nombre;
            string banco = Primero(BancoInst, t);
            if (banco.Length == 0) banco = Primero(BancoBbva, t);
            if (banco.Length == 0) banco = Primero(BancoReceptor, t);
            if (banco.Length == 0) banco = Primero(BancoSpei, t);
            var fb = FolioBbva.Match(t);
            if (fb.Success)
            {
                if (m.Referencia.Length == 0) m.Referencia = fb.Groups[1].Value;
                string nb; if (banco.Length == 0 && CodigosBanco.TryGetValue(fb.Groups[2].Value, out nb)) banco = nb;
            }
            m.BancoContraparte = banco.Length > 60 ? banco.Substring(0, 60) : banco;
            m.Tipo = Clasificar(t, m.Cargo, m.Abono);
        }

        private static string Primero(Regex r, string t) { var x = r.Match(t); return x.Success ? x.Groups[1].Value.Trim() : ""; }

        public static string Clasificar(string texto, decimal cargo, decimal abono)
        {
            string t = BancoTexto.Norm(texto);
            if (Regex.IsMatch(t, @"^comision")) return "Comisión";                                  // Monex: "Comision Por Transferencia ... Retiro de Comisión e IVA"
            if (Regex.IsMatch(t, @"^(cliente (compra|vende) divisas|deposito generado por venta de divisas|retiro para cubrir compra de divisas)")) return "Compraventa de divisas";
            if (Regex.IsMatch(t, @"^en proceso de revision")) return "En revisión";
            if (Regex.IsMatch(t, @"\biva\b")) return "IVA";
            if (Regex.IsMatch(t, @"tasa de desc|sobretasa|bonif tasa|apli tasa")) return "Comisión terminal";
            if (Regex.IsMatch(t, @"ventas? (debito|credito|nal)|deposito ventas del dia|terminales punto de venta")) return "Ventas terminal";
            if (Regex.IsMatch(t, @"comision|com\.|membresia|manejo de cuenta|anualidad|cuota|serv banca internet|serv bca|ops serv|emision libramie|libramiento chq")) return "Comisión";
            if (Regex.IsMatch(t, @"imss|infonavit|afore|sipare")) return "IMSS / Infonavit";
            if (Regex.IsMatch(t, @"cobro automatico recibo|recibo no\.|domiciliacion")) return "Cargo domiciliado";
            if (Regex.IsMatch(t, @"pago seguro|pago vida|seguro")) return "Seguro";
            if (Regex.IsMatch(t, @"disposicion tarjeta de credito|disp\. t\. negocios|pago con cargo en cuenta")) return "Crédito / tarjeta";
            if (Regex.IsMatch(t, @"\btef (recibido|enviado)")) return abono > 0 ? "TEF recibido" : "TEF enviado";
            if (Regex.IsMatch(t, @"secretaria de la hac|\bsat\b|guia:|linea de captura")) return "Pago SAT";
            if (Regex.IsMatch(t, @"nomina|sueldo")) return "Nómina";
            if (Regex.IsMatch(t, @"interes|rendimiento|gat ")) return "Intereses";
            if (Regex.IsMatch(t, @"\bisr\b|retencion|impuesto")) return "Impuestos";
            if (Regex.IsMatch(t, @"cetes|nafin|\bgbm\b|inversion|inversi n|\bfondo\b")) return "Inversión";
            if (Regex.IsMatch(t, @"deposito de tercero")) return "Depósito de tercero";
            if (Regex.IsMatch(t, @"traspaso|trasp ")) return "Traspaso";
            if (Regex.IsMatch(t, @"cheque")) return "Cheque";
            if (Regex.IsMatch(t, @"pago cuenta de tercero")) return "Pago a tercero";
            if (Regex.IsMatch(t, @"spei")) return abono > 0 ? "SPEI recibido" : "SPEI enviado";
            if (Regex.IsMatch(t, @"efectivo|deposito en ventanilla|retiro cajero|cajero")) return "Efectivo";
            if (Regex.IsMatch(t, @"tpv|adq|terminal|venta tarjeta|comercio")) return "Terminal / adquirente";
            if (Regex.IsMatch(t, @"pago de tarjeta|pago tarjeta|pago tdc|tdc")) return "Pago de tarjeta";
            if (Regex.IsMatch(t, @"nomina|sueldo")) return "Nómina";
            if (Regex.IsMatch(t, @"interbancario|transferencia|transf")) return abono > 0 ? "Transferencia recibida" : "Transferencia enviada";
            return "";
        }
    }
}
