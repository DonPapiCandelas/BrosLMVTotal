// ===================== motor de etiquetas (el mismo de «Generar documento (PDF)» y «PDF masivo») =====================
// Resuelve un formato HTML contra un documento: columnas de vwLBSDocDocumentPrint40-DocumentID, referencias del Diccionario de referencia (engAddendaFieldRef) y [Format(col,máscara)].
var ciE = System.Globalization.CultureInfo.InvariantCulture;
var cacheRefE = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
Dictionary<string, string> sqlRefsE = null;
var refsExistenE = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var sinResolverE = new HashSet<string>();
var reFormatE = new System.Text.RegularExpressions.Regex(@"\[Format\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([^\)\]]+?)\s*\)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
var reTagE = new System.Text.RegularExpressions.Regex(@"\[([A-Za-z_][A-Za-z0-9_]*)\]");

List<Dictionary<string, object>> CargarFilasE(long doc)
{
    foreach (var tvf in new[] { "vwLBSDocDocumentPrint40-DocumentID", "vwLBSDocDocumentPrint33-DocumentID", "vwLBSDocDocumentPrint-DocumentID" })
    {
        try { var f = ctx.Query("SELECT * FROM dbo.[" + tvf + "](" + doc + ")"); if (f.Count > 0) return f; } catch { }
    }
    return new List<Dictionary<string, object>>();
}
string ResolverRefE(string clave, long doc, long item)
{
    string ck = clave + "|" + doc + "|" + item;
    if (cacheRefE.TryGetValue(ck, out var hit)) return hit;
    string val = "";
    try
    {
        if (sqlRefsE == null)
        {
            sqlRefsE = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rr in ctx.Query("SELECT FieldRefKey, SQL FROM engAddendaFieldRef WHERE DeletedOn IS NULL")) sqlRefsE[S(rr["FieldRefKey"])] = S(rr["SQL"]);
        }
        if (sqlRefsE.ContainsKey(clave))
        {
            refsExistenE.Add(clave);
            string sql = sqlRefsE[clave];
            if (sql.Trim().Length > 0)
            {
                sql = sql.Replace("{DocumentID}", doc.ToString()).Replace("{DocumentItemID}", item.ToString());
                var ov = ctx.Scalar(sql);
                val = ov == null || ov is DBNull ? "" : (ov is DateTime dref ? dref.ToString("dd/MM/yyyy") : Convert.ToString(ov, ciE));
            }
        }
    }
    catch { val = ""; }
    cacheRefE[ck] = val;
    return val;
}
string FmtValorE(object ov) => ov == null || ov is DBNull ? "" : (ov is DateTime dt ? dt.ToString("dd/MM/yyyy") : Convert.ToString(ov, ciE));
string ResolverE(string txt, Dictionary<string, object> fila, long doc, long item)
{
    txt = reFormatE.Replace(txt, m =>
    {
        string col = m.Groups[1].Value, mask = m.Groups[2].Value.Trim();
        object raw = fila != null && fila.ContainsKey(col) ? fila[col] : (object)ResolverRefE(col, doc, item);
        if (raw == null || raw is DBNull || Convert.ToString(raw) == "") return "";
        double dv;
        if (double.TryParse(Convert.ToString(raw, ciE), System.Globalization.NumberStyles.Any, ciE, out dv))
        {
            try { return dv.ToString(mask, ciE); } catch { return dv.ToString("#,##0.00"); }
        }
        return Convert.ToString(raw);
    });
    txt = reTagE.Replace(txt, m =>
    {
        string tag = m.Groups[1].Value;
        if (tag == "QRBros" || tag == "QRPayload" || tag == "DesgloseImpuestos") return m.Value;
        if (fila != null && fila.ContainsKey(tag)) return System.Net.WebUtility.HtmlEncode(FmtValorE(fila[tag]));
        string viaRef = ResolverRefE(tag, doc, item);
        if (!string.IsNullOrEmpty(viaRef)) return System.Net.WebUtility.HtmlEncode(viaRef);
        if (!refsExistenE.Contains(tag)) sinResolverE.Add(tag);
        return "";
    });
    return txt;
}
string IncrustarImagenesE(string html, string baseDir)
{
    return System.Text.RegularExpressions.Regex.Replace(html, "src\\s*=\\s*\"([^\"]+)\"", m =>
    {
        string src = m.Groups[1].Value;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return m.Value;
        string ruta = src;
        try { if (!Path.IsPathRooted(ruta)) ruta = Path.Combine(baseDir, src); } catch { }
        if (!File.Exists(ruta)) return "src=\"\"";
        string ext = Path.GetExtension(ruta).TrimStart('.').ToLowerInvariant(); if (ext == "jpg") ext = "jpeg";
        try { return "src=\"data:image/" + ext + ";base64," + Convert.ToBase64String(File.ReadAllBytes(ruta)) + "\""; } catch { return "src=\"\""; }
    }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
// Texto que codifica el QR: en un documento TIMBRADO, la URL de verificación del SAT; si no, un texto simple con RFC, folio, fecha y total
string PayloadQrE(Dictionary<string, object> fila0, long doc, long item0)
{
    string payload = System.Net.WebUtility.HtmlDecode(ResolverE("[EmisorRfc]|[Serie][Folio]|[FechaDocumento]|[Format(Total,#,##0.00)] [Moneda]", fila0, doc, item0));
    string uuidQr = fila0.ContainsKey("Uuid") ? S(fila0["Uuid"]).Trim() : "";
    if (uuidQr.Length >= 32)
    {
        string sello = fila0.ContainsKey("selloCFD") ? S(fila0["selloCFD"]).Trim() : ""; double totQr;
        double.TryParse(fila0.ContainsKey("Total") ? S(fila0["Total"]) : "0", System.Globalization.NumberStyles.Any, ciE, out totQr);
        payload = "https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id=" + uuidQr + "&re=" + S(fila0.ContainsKey("EmisorRfc") ? fila0["EmisorRfc"] : "") + "&rr=" + S(fila0.ContainsKey("ReceptorRfc") ? fila0["ReceptorRfc"] : "") +
            "&tt=" + totQr.ToString("0000000000.000000", ciE) + "&fe=" + (sello.Length >= 8 ? sello.Substring(sello.Length - 8) : sello);
    }
    return payload;
}
// Renglones de total por impuesto (IVA, IEPS y retenciones) como filas del bloque de totales
string DesgloseHtmlE(long doc)
{
    var sbtx = new StringBuilder();
    try
    {
        foreach (var t in ctx.Query("SELECT TaxName, CAST(Retention AS INT) AS Ret, SUM(Amount) AS Monto FROM docDocumentTaxDetail WHERE DocumentID=" + doc +
            " GROUP BY TaxName, CAST(Retention AS INT) HAVING ABS(SUM(Amount)) > 0.005 ORDER BY CAST(Retention AS INT), TaxName"))
        {
            bool ret = Convert.ToInt32(t["Ret"]) == 1; double mt = Convert.ToDouble(t["Monto"], ciE);
            sbtx.Append("<div class=\"row keep\"><span class=\"k\">" + (ret ? "Ret. " : "") + System.Net.WebUtility.HtmlEncode(S(t["TaxName"])) + "</span><span class=\"v\">" + (ret ? "-" : "") + "$" + mt.ToString("#,##0.00", ciE) + "</span></div>");
        }
    }
    catch { }
    return sbtx.ToString();
}
// Valores de ejemplo para el DISEÑADOR: cada etiqueta con el valor que tiene en el documento elegido (los renglones, solo los primeros)
Dictionary<string, object> ValoresDisenoE(string html, string[] tokensExtra, bool detalle, long doc, string baseDir)
{
    var reTok = new System.Text.RegularExpressions.Regex(@"\[(?:Format\(\s*[A-Za-z_][A-Za-z0-9_]*\s*,\s*[^\)\]]+?\s*\)|[A-Za-z_][A-Za-z0-9_]*)\]");
    var filas = CargarFilasE(doc);
    var fila0 = filas.Count > 0 ? filas[0] : new Dictionary<string, object>();
    Func<Dictionary<string, object>, long> itemDe = f => f.ContainsKey("DocumentItemID") && f["DocumentItemID"] != null && !(f["DocumentItemID"] is DBNull) ? Convert.ToInt64(f["DocumentItemID"]) : 0;
    var cabecera = new List<string>(); var banda = new List<string>();
    if (html != null && html != "")
    {
        string sinScripts = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)\b[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var mDet = System.Text.RegularExpressions.Regex.Match(sinScripts, @"<DETAIL>(.*?)</DETAIL>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        string fuera = mDet.Success ? sinScripts.Remove(mDet.Index, mDet.Length) : sinScripts;
        foreach (System.Text.RegularExpressions.Match m in reTok.Matches(fuera)) if (!cabecera.Contains(m.Value)) cabecera.Add(m.Value);
        if (mDet.Success) foreach (System.Text.RegularExpressions.Match m in reTok.Matches(mDet.Groups[1].Value)) if (!banda.Contains(m.Value)) banda.Add(m.Value);
    }
    if (tokensExtra != null) foreach (var tk in tokensExtra) { if (detalle) { if (!banda.Contains(tk)) banda.Add(tk); } else if (!cabecera.Contains(tk)) cabecera.Add(tk); }
    Func<string, Dictionary<string, object>, long, long, string> val = (tok, fila, d, it) =>
    {
        string inner = tok.Substring(1, tok.Length - 2);
        if (inner == "QRPayload") return PayloadQrE(fila, d, it);
        if (inner == "DesgloseImpuestos") return DesgloseHtmlE(d);
        return System.Net.WebUtility.HtmlDecode(ResolverE(tok, fila, d, it));
    };
    var valores = new Dictionary<string, object>();
    foreach (var tk in cabecera) valores[tk] = val(tk, fila0, doc, itemDe(fila0));
    var lasFilas = new List<object>();
    foreach (var f in filas.Take(3))
    {
        var m = new Dictionary<string, object>();
        foreach (var tk in banda) m[tk] = val(tk, f, doc, itemDe(f));
        lasFilas.Add(m);
    }
    // Atributos con etiquetas (src="[LogoEmpresa]", style="color:[X]", data-qr="[QRPayload]"): su valor ya resuelto; en src, con la imagen incrustada
    var attrs = new Dictionary<string, object>();
    if (html != null && html != "")
    {
        string sinScripts2 = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)\b[\s\S]*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(sinScripts2, "([A-Za-z\\-]+)\\s*=\\s*\"([^\"]*\\[[A-Za-z_][^\"]*)\""))
        {
            string orig = m.Groups[2].Value; if (attrs.ContainsKey(orig) || !reTok.IsMatch(orig)) continue;
            string r = orig;
            foreach (System.Text.RegularExpressions.Match tm in reTok.Matches(orig)) r = r.Replace(tm.Value, val(tm.Value, fila0, doc, itemDe(fila0)));
            if (m.Groups[1].Value.ToLowerInvariant() == "src")
            {
                string e = IncrustarImagenesE("src=\"" + r + "\"", baseDir ?? "");
                r = e.Substring(5, e.Length - 6);
            }
            attrs[orig] = r;
        }
    }
    return new Dictionary<string, object> { ["valores"] = valores, ["filas"] = lasFilas, ["nFilas"] = filas.Count, ["attrs"] = attrs };
}
// ---------- De dónde sale cada etiqueta del documento ----------
// Se lee la definición de la función de impresión (vwLBSDocDocumentPrint40-DocumentID) y se separa cada «expresión AS Alias» de su lista de columnas:
// así el Diseñador puede decir, por ejemplo, que [NumeroIdentificacion] sale de docDocumentItem.ProductKey.
Dictionary<string, string> OrigenesColumnasE()
{
    var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string def = "";
    try { def = S(ctx.Scalar("SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.[vwLBSDocDocumentPrint40-DocumentID]'))")); } catch { }
    if (def == "") return res;
    int i = System.Text.RegularExpressions.Regex.Match(def, @"\bSELECT\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Index + 6;
    int prof = 0; var item = new StringBuilder(); var items = new List<string>();
    for (; i < def.Length; i++)
    {
        char c = def[i];
        if (c == '\'') { item.Append(c); i++; while (i < def.Length) { item.Append(def[i]); if (def[i] == '\'') { if (i + 1 < def.Length && def[i + 1] == '\'') { i++; item.Append(def[i]); } else break; } i++; } continue; }
        if (c == '-' && i + 1 < def.Length && def[i + 1] == '-') { while (i < def.Length && def[i] != '\n') i++; continue; }
        if (c == '/' && i + 1 < def.Length && def[i + 1] == '*') { int fin = def.IndexOf("*/", i + 2, StringComparison.Ordinal); i = fin < 0 ? def.Length : fin + 1; continue; }
        if (c == '(') prof++; else if (c == ')') prof--;
        if (prof == 0)
        {
            if (c == ',') { items.Add(item.ToString()); item.Clear(); continue; }
            if ((c == 'F' || c == 'f') && i + 5 <= def.Length && string.Compare(def, i, "FROM", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 && (i == 0 || char.IsWhiteSpace(def[i - 1])) && char.IsWhiteSpace(def[i + 4])) { items.Add(item.ToString()); break; }
        }
        item.Append(c);
    }
    foreach (var raw in items)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim(); if (t == "") continue;
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(.*?)\s+AS\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string alias, expr;
        if (m.Success) { expr = m.Groups[1].Value.Trim(); alias = m.Groups[2].Value; }
        else { var m2 = System.Text.RegularExpressions.Regex.Match(t, @"^\[?([A-Za-z0-9_]+)\]?\.\[?([A-Za-z0-9_]+)\]?$"); if (!m2.Success) continue; expr = t; alias = m2.Groups[2].Value; }
        var ms = System.Text.RegularExpressions.Regex.Match(expr, @"^\[?([A-Za-z0-9_]+)\]?\.\[?([A-Za-z0-9_]+)\]?$");
        res[alias] = ms.Success ? ms.Groups[1].Value + "." + ms.Groups[2].Value : (expr.Length > 260 ? expr.Substring(0, 260) + "…" : expr);
    }
    return res;
}
// Para cada columna del documento: de dónde sale, el valor que tiene en este documento y si cambia por renglón
Dictionary<string, object> InfoEtiquetasE(long doc)
{
    var origenes = OrigenesColumnasE(); var filas = CargarFilasE(doc);
    var res = new Dictionary<string, object>();
    if (filas.Count == 0) return new Dictionary<string, object> { ["columnas"] = res, ["hayDatos"] = false };
    foreach (var kv in filas[0])
    {
        string o = origenes.ContainsKey(kv.Key) ? origenes[kv.Key] : "";
        bool porRenglon = filas.Count > 1 && filas.Take(3).Select(f => FmtValorE(f.ContainsKey(kv.Key) ? f[kv.Key] : null)).Distinct().Count() > 1;
        if (!porRenglon && (o.IndexOf("docDocumentItem", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("orgProduct", StringComparison.OrdinalIgnoreCase) >= 0)) porRenglon = true;
        res[kv.Key] = new Dictionary<string, object> { ["o"] = o, ["v"] = FmtValorE(kv.Value), ["r"] = porRenglon };
    }
    return new Dictionary<string, object> { ["columnas"] = res, ["hayDatos"] = true };
}
// HTML ya resuelto de UN documento a partir del TEXTO del formato (así se prueba lo que se está escribiendo, sin guardar)
string ResolverFormatoE(string fuente, long doc, string baseDir)
{
    var protegidos = new List<string>();
    string html = System.Text.RegularExpressions.Regex.Replace(fuente, @"<(script|style)\b[\s\S]*?</\1>", mm => { protegidos.Add(mm.Value); return "@@BROSPROT" + (protegidos.Count - 1) + "@@"; }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var filas = CargarFilasE(doc);
    var fila0 = filas.Count > 0 ? filas[0] : new Dictionary<string, object>();
    Func<Dictionary<string, object>, long> itemDe = f => f.ContainsKey("DocumentItemID") && f["DocumentItemID"] != null && !(f["DocumentItemID"] is DBNull) ? Convert.ToInt64(f["DocumentItemID"]) : 0;
    long item0 = itemDe(fila0);
    var mDet = System.Text.RegularExpressions.Regex.Match(html, @"<DETAIL>(.*?)</DETAIL>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
    var sb = new StringBuilder();
    if (mDet.Success)
    {
        sb.Append(ResolverE(html.Substring(0, mDet.Index), fila0, doc, item0));
        foreach (var f in filas) sb.Append(ResolverE(mDet.Groups[1].Value, f, doc, itemDe(f)));
        sb.Append(ResolverE(html.Substring(mDet.Index + mDet.Length), fila0, doc, item0));
    }
    else sb.Append(ResolverE(html, fila0, doc, item0));
    string outHtml = IncrustarImagenesE(sb.ToString(), baseDir ?? "");
    outHtml = System.Text.RegularExpressions.Regex.Replace(outHtml, "windows-1252", "utf-8", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (outHtml.IndexOf("[QRPayload]", StringComparison.Ordinal) >= 0)
        outHtml = outHtml.Replace("[QRPayload]", System.Net.WebUtility.HtmlEncode(PayloadQrE(fila0, doc, item0)));
    if (outHtml.IndexOf("[QRBros]", StringComparison.Ordinal) >= 0)
        outHtml = System.Text.RegularExpressions.Regex.Replace(outHtml, @"<img[^>]*\[QRBros\][^>]*>", "").Replace("[QRBros]", "");
    if (outHtml.IndexOf("[DesgloseImpuestos]", StringComparison.Ordinal) >= 0)
        outHtml = outHtml.Replace("[DesgloseImpuestos]", DesgloseHtmlE(doc));
    for (int i = 0; i < protegidos.Count; i++) outHtml = outHtml.Replace("@@BROSPROT" + i + "@@", protegidos[i]);
    return outHtml;
}
string LeerArchivoFormatoE(string archivo)
{
    byte[] bytes = File.ReadAllBytes(archivo);
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
    try { return new UTF8Encoding(false, true).GetString(bytes); } catch { return Encoding.GetEncoding(1252).GetString(bytes); }
}
var respaldados = new HashSet<string>();                      // un solo respaldo por archivo y por sesión del editor

