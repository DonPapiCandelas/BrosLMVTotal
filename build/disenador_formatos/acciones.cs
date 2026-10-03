                else if (action == "editorAbrir")
                {
                    long fid = Convert.ToInt64(p["formatId"]);
                    var fr = ctx.Query("SELECT f.ModuleID, f.FormatName, f.FileName, f.FormatIsDefault, ISNULL(m.ModuleName,'') AS Modulo FROM engModulePrintFormat f LEFT JOIN engModule m ON m.ModuleID=f.ModuleID WHERE f.PrintFormatID=" + fid);
                    if (fr.Count == 0) throw new Exception("El formato ya no existe.");
                    string arch = S(fr[0]["FileName"]); int mid = Convert.ToInt32(fr[0]["ModuleID"]);
                    if (!File.Exists(arch)) throw new Exception("No existe el archivo del formato:\n" + arch);
                    var docsMod = ctx.Query("SELECT TOP 40 d.DocumentID, ISNULL(d.FolioPrefix,'') AS Serie, ISNULL(CAST(d.Folio AS NVARCHAR(30)),'') AS Folio, ISNULL(be.CommercialName, be.OfficialName) AS Entidad, d.DateDocument " +
                        "FROM docDocument d LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID=d.BusinessEntityID WHERE d.ModuleID=" + mid + " AND d.OwnedBusinessEntityID=" + owned + " AND d.DeletedOn IS NULL ORDER BY d.DocumentID DESC")
                        .Select(d => (object)o("id", L(d["DocumentID"]), "etiqueta", (S(d["Serie"]) + S(d["Folio"])).Trim() + " · " + S(d["Entidad"]) + " (ID " + L(d["DocumentID"]) + ")")).ToArray();
                    data = o("nombre", S(fr[0]["FormatName"]), "archivo", arch, "modulo", S(fr[0]["Modulo"]), "esDefault", B(fr[0]["FormatIsDefault"]), "html", LeerArchivoFormatoE(arch), "docs", docsMod);
                }
                else if (action == "editorVista")
                {
                    sinResolverE.Clear();
                    string htmlV = ""; string errV = null;
                    long docV = Convert.ToInt64(p["docId"]);
                    if (docV <= 0) errV = "No hay documentos de este tipo para probar el formato. Crea uno en Comercial y vuelve a abrir el editor.";
                    else
                    {
                        try { string archV = S(ctx.Scalar("SELECT FileName FROM engModulePrintFormat WHERE PrintFormatID=" + Convert.ToInt64(p["formatId"]))); htmlV = ResolverFormatoE(S(p["html"]), docV, Path.GetDirectoryName(archV)); }
                        catch (Exception exv) { errV = exv.Message; }
                        if (errV == null && htmlV.Length > 3000000) { errV = "El documento resuelto es demasiado grande para la vista previa (" + (htmlV.Length / 1024) + " KB)."; htmlV = ""; }
                    }
                    data = o("html", htmlV, "faltan", sinResolverE.OrderBy(x => x).ToArray(), "error", errV);
                }
                else if (action == "editorGuardar")
                {
                    long fid = Convert.ToInt64(p["formatId"]);
                    string arch = S(ctx.Scalar("SELECT FileName FROM engModulePrintFormat WHERE PrintFormatID=" + fid));
                    if (string.IsNullOrEmpty(arch)) throw new Exception("El formato ya no existe.");
                    string respaldo = "";
                    if (File.Exists(arch) && respaldados.Add(arch))
                    {
                        respaldo = arch + ".respaldo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(arch, respaldo, true);
                    }
                    File.WriteAllText(arch, S(p["html"]), new UTF8Encoding(false));
                    data = o("ok", true, "respaldo", respaldo);
                }
                else if (action == "editorEtiquetas")
                {
                    var lst = new List<object>();
                    try
                    {
                        foreach (var c in ctx.Query("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.[vwLBSDocDocumentPrint40-DocumentID]') ORDER BY column_id"))
                            lst.Add(o("k", S(c["name"]), "d", "", "t", "col", "r", false, "c", false));
                    }
                    catch { }
                    foreach (var rf in ctx.Query("SELECT FieldRefKey, ISNULL(Description,'') AS Descr, ISNULL(Custom,0) AS Cus, CASE WHEN SQL LIKE '%{DocumentItemID}%' THEN 1 ELSE 0 END AS Renglon FROM engAddendaFieldRef WHERE DeletedOn IS NULL ORDER BY FieldRefKey"))
                        lst.Add(o("k", S(rf["FieldRefKey"]), "d", S(rf["Descr"]), "t", "ref", "r", Convert.ToInt32(rf["Renglon"]) == 1, "c", B(rf["Cus"])));
                    data = o("etiquetas", lst.ToArray());
                }
                else if (action == "disenoValores")
                {
                    long docD = Convert.ToInt64(p["docId"]);
                    string[] toks = null;
                    if (p.ContainsKey("tokens") && p["tokens"] is System.Collections.IEnumerable le && !(p["tokens"] is string)) toks = le.Cast<object>().Select(x => S(x)).ToArray();
                    string archD = S(ctx.Scalar("SELECT FileName FROM engModulePrintFormat WHERE PrintFormatID=" + Convert.ToInt64(p["formatId"])));
                    sinResolverE.Clear();
                    data = ValoresDisenoE(p.ContainsKey("html") ? S(p["html"]) : "", toks, p.ContainsKey("detalle") && B(p["detalle"]), docD, string.IsNullOrEmpty(archD) ? "" : Path.GetDirectoryName(archD));
                }
                else if (action == "disenoLogo")
                {
                    // Elegir una imagen y copiarla junto a los formatos (subcarpeta Imagenes): el formato la llama con una ruta relativa y el motor la incrusta al generar el PDF
                    using (var dlg = new OpenFileDialog { Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp", Title = "Elige el logo o la imagen" })
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) data = o("cancel", true);
                        else
                        {
                            string dirImg = Path.Combine(carpetaFormatos(), "Imagenes"); Directory.CreateDirectory(dirImg);
                            string nom = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(dlg.FileName), @"[^A-Za-z0-9_\-]", "_"), ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                            string dest = Path.Combine(dirImg, nom + ext); int k = 2;
                            while (File.Exists(dest) && !File.ReadAllBytes(dest).SequenceEqual(File.ReadAllBytes(dlg.FileName))) dest = Path.Combine(dirImg, nom + "_" + (k++) + ext);
                            if (!File.Exists(dest)) File.Copy(dlg.FileName, dest);
                            string mime = ext == ".svg" ? "image/svg+xml" : ext == ".jpg" ? "image/jpeg" : "image/" + ext.TrimStart('.');
                            data = o("ruta", "Imagenes/" + Path.GetFileName(dest), "vista", "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(dest)));
                        }
                    }
                }
                else if (action == "esquemaTablas")
                {
                    var populares = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "docDocument", "docDocumentItem", "docDocumentTaxDetail", "docDocumentPayment", "docDocumentCFD", "docDocumentExt", "docDocumentExtra", "docDocumentPaymentAgenda",
                        "orgBusinessEntity", "orgBusinessEntityMainInfo", "orgCustomer", "orgSupplier", "orgProduct", "orgProductKardex", "orgDepot", "orgCostCenter", "engPaymentTerm", "engTaxType", "engUser", "docFinancialOperation", "docDocumentLot", "docDocumentSerialNumber" };
                    var tb = ctx.Query("SELECT name FROM sys.tables WHERE name NOT LIKE 'zz%' AND name NOT LIKE 'Import%' AND name NOT LIKE '[_]%' AND (name LIKE 'doc%' OR name LIKE 'org%' OR name LIKE 'eng%' OR name LIKE 'acc%') ORDER BY name")
                        .Select(r => (object)o("t", S(r["name"]), "p", populares.Contains(S(r["name"])))).ToArray();
                    data = o("tablas", tb);
                }
                else if (action == "esquemaColumnas")
                {
                    string tabla = S(p["tabla"]);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(tabla, "^[A-Za-z0-9_]+$")) throw new Exception("Nombre de tabla no válido.");
                    var cols = ctx.Query("SELECT c.name, ty.name AS tipo FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID('dbo.[" + tabla + "]') ORDER BY c.column_id");
                    if (cols.Count == 0) throw new Exception("La tabla no existe.");
                    Func<string, HashSet<string>> colsDe = t2 => new HashSet<string>(ctx.Query("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.[" + t2 + "]')").Select(r => S(r["name"])), StringComparer.OrdinalIgnoreCase);
                    var enDoc = colsDe("docDocument"); var enItem = colsDe("docDocumentItem");
                    var nombres = cols.Select(c => S(c["name"])).ToList();
                    var rel = new List<object>();
                    // «Mismo valor que el documento / que la partida»: cualquier columna que la tabla comparta con docDocument o docDocumentItem sirve de enlace
                    foreach (var n in nombres)
                    {
                        if (n == "DocumentID") rel.Add(o("col", n, "via", "doc", "texto", "es de este documento (DocumentID)"));
                        else if (n == "DocumentItemID" && tabla != "docDocumentItem") rel.Add(o("col", n, "via", "item", "texto", "es de esta partida (DocumentItemID)"));
                        else if (n.EndsWith("ID") && enDoc.Contains(n) && n != "CreatedBy" && n != "DeletedBy" && n != "UserID" && !(tabla == "docDocument" && n == "DocumentID")) rel.Add(o("col", n, "via", "docCol", "texto", n + " es el mismo que el del documento"));
                        else if (n.EndsWith("ID") && enItem.Contains(n) && n != "CreatedBy" && n != "DeletedBy" && n != "UserID" && n != "DocumentID" && n != "DocumentItemID") rel.Add(o("col", n, "via", "itemCol", "texto", n + " es el mismo que el de la partida"));
                    }
                    if (tabla == "docDocument") rel.Add(o("col", "DocumentID", "via", "doc", "texto", "es este documento"));
                    if (tabla == "docDocumentItem") rel.Add(o("col", "DocumentItemID", "via", "item", "texto", "es esta partida"));
                    data = o("columnas", cols.Select(c => (object)o("n", S(c["name"]), "t", S(c["tipo"]))).ToArray(), "relaciones", rel.ToArray(), "borrado", nombres.Contains("DeletedOn"));
                }
                else if (action == "refProbar")
                {
                    string sql = S(p["sql"]); long dId = Convert.ToInt64(p["docId"]); long iId = p.ContainsKey("itemId") ? Convert.ToInt64(p["itemId"]) : 0;
                    if (iId == 0) { try { iId = Convert.ToInt64(ctx.Scalar("SELECT ISNULL(MIN(DocumentItemID),0) FROM docDocumentItem WHERE DocumentID=" + dId + " AND DeletedOn IS NULL")); } catch { } }
                    if (System.Text.RegularExpressions.Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|CREATE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new Exception("La referencia solo puede leer datos (SELECT).");
                    string ev = null, er = null;
                    try { var rv = ctx.Scalar(sql.Replace("{DocumentID}", dId.ToString()).Replace("{DocumentItemID}", iId.ToString())); ev = rv == null || rv is DBNull ? "" : (rv is DateTime rd ? rd.ToString("dd/MM/yyyy") : Convert.ToString(rv, ciE)); }
                    catch (Exception exr) { er = exr.Message; }
                    data = o("valor", ev, "error", er);
                }
                else if (action == "refGuardar")
                {
                    string key = S(p["key"]).Trim(), sqlR = S(p["sql"]), desc = S(p["desc"]);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new Exception("El nombre solo puede llevar letras, números y guion bajo, y no empezar con número.");
                    if (System.Text.RegularExpressions.Regex.IsMatch(sqlR, @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|CREATE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new Exception("La referencia solo puede leer datos (SELECT).");
                    int colExiste = Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.[vwLBSDocDocumentPrint40-DocumentID]') AND name=N'" + key.Replace("'", "''") + "'"));
                    if (colExiste > 0) throw new Exception("«" + key + "» ya es una columna del documento: elige otro nombre.");
                    int ya = Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM engAddendaFieldRef WHERE FieldRefKey=N'" + key.Replace("'", "''") + "' AND DeletedOn IS NULL AND ISNULL(Custom,0)=0"));
                    if (ya > 0) throw new Exception("«" + key + "» ya existe en el Diccionario de referencia de Comercial (no es una referencia propia): elige otro nombre.");
                    using (var cn = ctx.OpenConn()) using (var cm = cn.CreateCommand())
                    {
                        cm.CommandText = "IF EXISTS (SELECT 1 FROM engAddendaFieldRef WHERE FieldRefKey=@k AND DeletedOn IS NULL AND Custom=1) UPDATE engAddendaFieldRef SET SQL=@s, Description=@d WHERE FieldRefKey=@k AND DeletedOn IS NULL AND Custom=1 " +
                            "ELSE INSERT INTO engAddendaFieldRef (FieldRefKey,SQL,Description,Custom,CreatedOn,CreatedBy) VALUES (@k,@s,@d,1,GETDATE(),@by)";
                        cm.Parameters.AddWithValue("@k", key); cm.Parameters.AddWithValue("@s", sqlR); cm.Parameters.AddWithValue("@d", desc == "" ? (object)("BrosLMV: " + key) : desc); cm.Parameters.AddWithValue("@by", userId);
                        cm.ExecuteNonQuery();
                    }
                    sqlRefsE = null; cacheRefE.Clear(); refsExistenE.Clear();
                    data = o("ok", true);
                }
