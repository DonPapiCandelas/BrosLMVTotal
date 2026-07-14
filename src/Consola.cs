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

// Consola.cs
// Consola de scripts BrosLMV: editor de codigo (Scintilla) + biblioteca de scripts
// + inspector de contexto (ctx) + ayuda integrada + ejecucion segura.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScintillaNET;

namespace BrosLMV
{
    public class BrosConsola : Form
    {
        private readonly ScriptContext _ctx;

        private Scintilla   _editor;
        private TreeView    _tree;
        private RichTextBox _outSalida, _outErrores, _outMensajes;
        private TabControl  _tabsOut;
        private ListView    _lstMetodosCSharp, _lstMetodosPython, _lstMetodosSql, _lstSeleccion;
        private Label        _lblCtx;
        private CheckBox     _chkSoloLectura;
        private ToolStripStatusLabel _status, _statusTiempo, _statusScript, _statusLang, _statusPos, _statusVer;
        private string       _appKey = "";   // AppKey del script abierto (en SQL)

        // Versión del addon (de AssemblyVersion). Se lee de memoria una vez: costo cero.
        // v2.18.0 — 4 anclas + campos universales + partida como nativa (ver CHANGELOG).
        internal static readonly string Version =
            "v" + typeof(BrosConsola).Assembly.GetName().Version.ToString(3);
        private TextBox      _txtBuscar;

        // --- Controles de la refactorización visual ---
        private SplitContainer _splitLeft, _splitMain, _splitEditor;
        private Label        _lblTabName, _lblEstadoDoc, _lblErrCount;
        private ToolTip      _tips;
        private ComboBox     _cboFontSize;
        private int          _fontSize = 11;
        private ListView     _lstCtx;   // Contexto actual en forma de lista (Campo / Valor)

        // --- Buscar en el editor ---
        private Panel        _findBar;
        private TextBox      _txtFind;
        private Label        _lblFindCount;
        private readonly List<int> _findHits = new List<int>();
        private int          _findIdx = -1;
        private const int    IND_FIND = 8;   // indicador de Scintilla para resaltar coincidencias

        // --- Pantalla completa del editor ---
        private bool         _zen;
        private IconButton   _btnZen;

        // ---- Metadata de ctx (ayuda + autocompletado) ----
        private class MetodoCtx
        {
            public string Nombre, Firma, Desc, Ejemplo, Cat;
            public MetodoCtx(string n, string f, string d, string e, string cat = "") { Nombre = n; Firma = f; Desc = d; Ejemplo = e; Cat = cat; }
        }
        // Referencias C#: reflejan el API REAL de ScriptContext (ctx.*) y ErpContext (ctx.erp.*)
        // en src/Scripting.cs. Verificadas 2026-06-27. Cat = grupo en el panel de referencias.
        private static readonly MetodoCtx[] METODOS = new[]
        {
            // ---- ctx base ----
            new MetodoCtx("GetSelectedIds", "ctx.GetSelectedIds() : List<long>", "IDs de los documentos seleccionados en la vista.", "var ids = ctx.GetSelectedIds();", "Selección y datos"),
            new MetodoCtx("GetFilaActiva", "ctx.GetFilaActiva() : Dictionary<string,object>", "Campos de la primera fila seleccionada del grid.", "var fila = ctx.GetFilaActiva();\r\nvar folio = fila[\"Folio\"];", "Selección y datos"),
            new MetodoCtx("JoinIds", "ctx.JoinIds(ids) : string", "Convierte una lista de IDs en \"1,2,3\" (para un IN).", "var lista = ctx.JoinIds(ctx.GetSelectedIds());", "Selección y datos"),

            new MetodoCtx("Scalar", "ctx.Scalar(sql) : object", "Ejecuta SQL y devuelve un solo valor.", "var total = ctx.Scalar(\"SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (\" + ctx.JoinIds(ctx.GetSelectedIds()) + \")\");", "Consultas SQL"),
            new MetodoCtx("Query", "ctx.Query(sql) : List<Dictionary<string,object>>", "Ejecuta SQL y devuelve filas.", "var filas = ctx.Query(\"SELECT Folio, Total FROM docDocument WHERE DocumentID IN (\" + ctx.JoinIds(ctx.GetSelectedIds()) + \")\");", "Consultas SQL"),
            new MetodoCtx("NonQuery", "ctx.NonQuery(sql) : int", "INSERT/UPDATE/DELETE. Devuelve filas afectadas (respeta modo solo lectura).", "int n = ctx.NonQuery(\"UPDATE docDocument SET Referencia='X' WHERE DocumentID IN (\" + ctx.JoinIds(ctx.GetSelectedIds()) + \")\");", "Consultas SQL"),
            new MetodoCtx("EjecutarSql", "ctx.EjecutarSql(sql) : string", "Corre T-SQL crudo (resuelve tokens) y devuelve texto: filas o un OK.", "var txt = ctx.EjecutarSql(\"SELECT Folio, Total FROM docDocument WHERE DocumentID IN ({pIDs})\");\r\nctx.Msg(txt);", "Consultas SQL"),

            new MetodoCtx("ResolverTokens", "ctx.ResolverTokens(plantilla) : string", "Sustituye {pID}, {pIDs}, {pUserID}, {pModulo}, {pEmpresa}, {DATOS:Campo}.", "var sql = ctx.ResolverTokens(\"SELECT * FROM docDocument WHERE DocumentID = {pID}\");", "Tokens"),

            new MetodoCtx("Empresa", "ctx.Empresa() : string", "Nombre de la base de datos de la empresa activa.", "var bd = ctx.Empresa();", "Contexto"),
            new MetodoCtx("ServidorActivo", "ctx.ServidorActivo() : string", "Servidor\\instancia de SQL de la empresa activa.", "var srv = ctx.ServidorActivo();", "Contexto"),
            new MetodoCtx("ModuloActivo", "ctx.ModuloActivo() : int", "ID del módulo activo de CONTPAQi.", "var mod = ctx.ModuloActivo();", "Contexto"),
            new MetodoCtx("UserID", "ctx.UserID : int", "ID de usuario del addon (suele venir 0; usa ctx.erp.UserId).", "var u = ctx.UserID;", "Contexto"),
            new MetodoCtx("SoloLectura", "ctx.SoloLectura : bool", "Si es true, NonQuery se bloquea (modo solo lectura).", "ctx.SoloLectura = true;", "Contexto"),
            new MetodoCtx("FilasAfectadas", "ctx.FilasAfectadas : int", "Acumulado de filas modificadas por NonQuery (auditoría).", "var n = ctx.FilasAfectadas;", "Contexto"),
            new MetodoCtx("DiagConexion", "ctx.DiagConexion() : string", "Diagnóstico de la conexión activa.", "ctx.Msg(ctx.DiagConexion());", "Contexto"),
            new MetodoCtx("XEngineLib", "ctx.XEngineLib : object", "Objeto XEngine crudo (escape hatch a COM).", "var xe = ctx.XEngineLib;", "Contexto"),

            new MetodoCtx("Msg", "ctx.Msg(texto, titulo?) : void", "Muestra un mensaje al usuario.", "ctx.Msg(\"Hola\", \"Aviso\");", "Interacción"),
            new MetodoCtx("Confirm", "ctx.Confirm(texto, titulo?) : bool", "Pregunta Sí/No.", "if (ctx.Confirm(\"¿Seguro?\")) { /* ... */ }", "Interacción"),
            new MetodoCtx("Log", "ctx.Log(texto) : void", "Escribe a la bitácora en C:\\BrosLMV\\logs.", "ctx.Log(\"Proceso terminado\");", "Interacción"),

            // ---- ctx.erp.* (wrapper tipado de XEngine) ----
            new MetodoCtx("erp.UserId", "ctx.erp.UserId : int", "ID real del usuario de CONTPAQi.", "var u = ctx.erp.UserId;", "ERP · Contexto"),
            new MetodoCtx("erp.UserName", "ctx.erp.UserName : string", "Nombre del usuario activo.", "var n = ctx.erp.UserName;", "ERP · Contexto"),
            new MetodoCtx("erp.OwnedBusinessEntityId", "ctx.erp.OwnedBusinessEntityId : int", "ID de la empresa propia (orgBusinessEntity IsOwned=1).", "var e = ctx.erp.OwnedBusinessEntityId;", "ERP · Contexto"),
            new MetodoCtx("erp.ActiveModuleId", "ctx.erp.ActiveModuleId : int", "Módulo activo (equivale a ctx.ModuloActivo()).", "var m = ctx.erp.ActiveModuleId;", "ERP · Contexto"),
            new MetodoCtx("erp.CurrencyId", "ctx.erp.CurrencyId : int", "Moneda activa.", "var c = ctx.erp.CurrencyId;", "ERP · Contexto"),
            new MetodoCtx("erp.ComercialRFC", "ctx.erp.ComercialRFC : string", "RFC de la empresa.", "var rfc = ctx.erp.ComercialRFC;", "ERP · Contexto"),
            new MetodoCtx("erp.SoftwareVersion", "ctx.erp.SoftwareVersion : string", "Versión de CONTPAQi.", "var v = ctx.erp.SoftwareVersion;", "ERP · Contexto"),

            new MetodoCtx("erp.RecalcCompleto", "ctx.erp.RecalcCompleto(documentId) : void", "Recalcula totales + costos + saldo pagado.", "var id = (int)ctx.GetSelectedIds()[0];\r\nctx.erp.RecalcCompleto(id);", "ERP · Documento"),
            new MetodoCtx("erp.RecalcDocument", "ctx.erp.RecalcDocument(documentId) : void", "Recalcula totales (subtotal, IVA, total).", "ctx.erp.RecalcDocument(id);", "ERP · Documento"),
            new MetodoCtx("erp.CalcularCostos", "ctx.erp.CalcularCostos(documentId) : void", "Actualiza costos (promedio, PEPS...).", "ctx.erp.CalcularCostos(id);", "ERP · Documento"),
            new MetodoCtx("erp.AffectStockNEW", "ctx.erp.AffectStockNEW(documentId) : void", "Afecta inventario (kardex) — módulos nuevos.", "ctx.erp.AffectStockNEW(id);", "ERP · Documento"),
            new MetodoCtx("erp.AffectStock", "ctx.erp.AffectStock(documentId) : void", "Afecta inventario (versión clásica).", "ctx.erp.AffectStock(id);", "ERP · Documento"),
            new MetodoCtx("erp.UpdateStatusDelivery", "ctx.erp.UpdateStatusDelivery(documentId) : void", "Actualiza el estatus de entrega del grid.", "ctx.erp.UpdateStatusDelivery(id);", "ERP · Documento"),
            new MetodoCtx("erp.UpdateDocumentPaidInfo", "ctx.erp.UpdateDocumentPaidInfo(documentId) : void", "Recalcula saldo pagado y balance.", "ctx.erp.UpdateDocumentPaidInfo(id);", "ERP · Documento"),
            new MetodoCtx("erp.ActualizarParcialidad", "ctx.erp.ActualizarParcialidad(documentId) : void", "Actualiza parcialidad en complementos de pago SAT.", "ctx.erp.ActualizarParcialidad(id);", "ERP · Documento"),
            new MetodoCtx("erp.CancelDocument", "ctx.erp.CancelDocument(documentId) : void", "Cancela el documento.", "ctx.erp.CancelDocument(id);", "ERP · Documento"),
            new MetodoCtx("erp.ReactivateDocument", "ctx.erp.ReactivateDocument(documentId) : void", "Reactiva un documento cancelado.", "ctx.erp.ReactivateDocument(id);", "ERP · Documento"),
            new MetodoCtx("erp.Save", "ctx.erp.Save(documentId) : void", "Guarda el documento (XEngine).", "ctx.erp.Save(id);", "ERP · Documento"),
            new MetodoCtx("erp.Delete", "ctx.erp.Delete(documentId) : void", "Elimina el documento (XEngine).", "ctx.erp.Delete(id);", "ERP · Documento"),
            new MetodoCtx("erp.AjustarSaldosInsolutos", "ctx.erp.AjustarSaldosInsolutos(documentId) : void", "Ajusta saldos insolutos.", "ctx.erp.AjustarSaldosInsolutos(id);", "ERP · Documento"),
            new MetodoCtx("erp.RefreshDocumento", "ctx.erp.RefreshDocumento(documentId) : void", "Refresca visualmente un documento abierto.", "ctx.erp.RefreshDocumento(id);", "ERP · Documento"),
            new MetodoCtx("erp.NuevoDocumento", "ctx.erp.NuevoDocumento(moduleId, depotId, businessEntityId?) : int", "Crea el encabezado de un documento con los defaults del módulo (folio, tipo, moneda) y devuelve el DocumentID.", "int id = ctx.erp.NuevoDocumento(183, 1, 162); // OC, almacén, proveedor", "ERP · Documento"),
            new MetodoCtx("erp.AgregarArticulo", "ctx.erp.AgregarArticulo(documentId, productId, cantidad?, precio?) : int", "Agrega una partida (lee datos de orgProduct). Tras agregar, llamar RecalcCompleto.", "ctx.erp.AgregarArticulo(id, 1, 3, 100);\r\nctx.erp.RecalcCompleto(id);", "ERP · Documento"),

            new MetodoCtx("erp.RefreshGrid", "ctx.erp.RefreshGrid() : void", "Refresca el grid del módulo.", "ctx.erp.RefreshGrid();", "ERP · UI"),
            new MetodoCtx("erp.RefreshRibbon", "ctx.erp.RefreshRibbon() : void", "Refresca el ribbon.", "ctx.erp.RefreshRibbon();", "ERP · UI"),
            new MetodoCtx("erp.GotoModuleID", "ctx.erp.GotoModuleID(moduleId) : void", "Cambia al módulo indicado.", "ctx.erp.GotoModuleID(183);", "ERP · UI"),
            new MetodoCtx("erp.OpenModule", "ctx.erp.OpenModule(moduleId) : void", "Abre un módulo.", "ctx.erp.OpenModule(183);", "ERP · UI"),
            new MetodoCtx("erp.OpenBrowser", "ctx.erp.OpenBrowser(url) : void", "Abre una URL en el navegador.", "ctx.erp.OpenBrowser(\"https://contpaqi.com\");", "ERP · UI"),
            new MetodoCtx("erp.ShowMessage", "ctx.erp.ShowMessage(msg) : void", "Mensaje nativo de CONTPAQi.", "ctx.erp.ShowMessage(\"Listo\");", "ERP · UI"),

            new MetodoCtx("erp.GetFolioPrefix", "ctx.erp.GetFolioPrefix(moduleId, depotId) : string", "Prefijo (serie) configurado del módulo/almacén.", "var serie = ctx.erp.GetFolioPrefix(183, 1);", "ERP · Folio"),
            new MetodoCtx("erp.GetNextFolio", "ctx.erp.GetNextFolio(moduleId, prefix, depotId) : string", "Siguiente folio disponible.", "var folio = ctx.erp.GetNextFolio(183, \"OC\", 1);", "ERP · Folio"),

            new MetodoCtx("erp.GetProductStock", "ctx.erp.GetProductStock(productId, depotId) : double", "Existencia del producto en el almacén.", "double ex = ctx.erp.GetProductStock(1, 1);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetSalePrice", "ctx.erp.GetSalePrice(productId, businessEntityId?) : double", "Precio de venta (por cliente si se indica).", "double p = ctx.erp.GetSalePrice(1);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetBusinessEntitySalePrice", "ctx.erp.GetBusinessEntitySalePrice(productId, businessEntityId) : double", "Precio de venta específico de un cliente.", "double p = ctx.erp.GetBusinessEntitySalePrice(1, 10);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetBuyPrice", "ctx.erp.GetBuyPrice(productId) : double", "Precio de compra.", "double p = ctx.erp.GetBuyPrice(1);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetCostPrice", "ctx.erp.GetCostPrice(productId) : double", "Costo del producto.", "double c = ctx.erp.GetCostPrice(1);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetPriceWithTaxes", "ctx.erp.GetPriceWithTaxes(price, taxTypeId) : double", "Precio con impuestos incluidos.", "double t = ctx.erp.GetPriceWithTaxes(100, 1);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetCurrencyRate", "ctx.erp.GetCurrencyRate(currencyId) : double", "Tipo de cambio (respecto a MXN).", "double tc = ctx.erp.GetCurrencyRate(2);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetCurrencyRateBanxico", "ctx.erp.GetCurrencyRateBanxico(currencyId) : double", "Tipo de cambio de Banxico.", "double tc = ctx.erp.GetCurrencyRateBanxico(2);", "ERP · Precios y existencias"),
            new MetodoCtx("erp.GetCoefConversion", "ctx.erp.GetCoefConversion(productId, fromUnit, toUnit) : double", "Coeficiente de conversión entre unidades.", "double k = ctx.erp.GetCoefConversion(1, \"PZA\", \"CAJA\");", "ERP · Precios y existencias"),
            new MetodoCtx("erp.ProductIsKit", "ctx.erp.ProductIsKit(productId) : bool", "True si el producto es un kit.", "if (ctx.erp.ProductIsKit(1)) { /* ... */ }", "ERP · Precios y existencias"),

            new MetodoCtx("erp.VerifyCreditLimit", "ctx.erp.VerifyCreditLimit(businessEntityId, amount) : bool", "True si el importe entra en el límite de crédito.", "if (!ctx.erp.VerifyCreditLimit(10, 5000)) ctx.Msg(\"Excede crédito\");", "ERP · Crédito"),
            new MetodoCtx("erp.VerifyCreditLimitOverdue", "ctx.erp.VerifyCreditLimitOverdue(businessEntityId) : bool", "True si la entidad tiene documentos vencidos.", "if (ctx.erp.VerifyCreditLimitOverdue(10)) ctx.Msg(\"Tiene vencidos\");", "ERP · Crédito"),

            new MetodoCtx("erp.GetModuleParameter", "ctx.erp.GetModuleParameter(moduleId, key) : string", "Lee un parámetro del módulo.", "var v = ctx.erp.GetModuleParameter(183, \"MiParam\");", "ERP · Parámetros"),
            new MetodoCtx("erp.SaveModuleParameter", "ctx.erp.SaveModuleParameter(moduleId, key, value) : void", "Guarda un parámetro del módulo.", "ctx.erp.SaveModuleParameter(183, \"MiParam\", \"1\");", "ERP · Parámetros"),
            new MetodoCtx("erp.GetParameter", "ctx.erp.GetParameter(key) : string", "Lee un parámetro global.", "var v = ctx.erp.GetParameter(\"MiParam\");", "ERP · Parámetros"),

            new MetodoCtx("erp.GetTotalLetter", "ctx.erp.GetTotalLetter(amount, currencyId?) : string", "Importe con letra (\"MIL ... PESOS 50/100 M.N.\"). currencyId 0 = moneda activa.", "var letra = ctx.erp.GetTotalLetter(1234.50);", "ERP · Utilidades"),
            new MetodoCtx("erp.GetTotalLetterEN", "ctx.erp.GetTotalLetterEN(amount, currencyId?) : string", "Importe con letra en inglés (currencyId 0 = moneda activa).", "var letra = ctx.erp.GetTotalLetterEN(1234.50);", "ERP · Utilidades"),
            new MetodoCtx("erp.GetBarCode", "ctx.erp.GetBarCode(value, barcodeType?) : string", "Código de barras codificado.", "var bc = ctx.erp.GetBarCode(\"12345\");", "ERP · Utilidades"),
            new MetodoCtx("erp.DecryptString", "ctx.erp.DecryptString(encrypted) : string", "Descifra una cadena de CONTPAQi.", "var s = ctx.erp.DecryptString(enc);", "ERP · Utilidades"),
            new MetodoCtx("erp.EncryptString", "ctx.erp.EncryptString(plain) : string", "Cifra una cadena.", "var enc = ctx.erp.EncryptString(\"texto\");", "ERP · Utilidades"),
            new MetodoCtx("erp.ValidRFC", "ctx.erp.ValidRFC(rfc) : bool", "Valida un RFC.", "if (ctx.erp.ValidRFC(\"XAXX010101000\")) { /* ... */ }", "ERP · Utilidades"),
            new MetodoCtx("erp.FormatCurrency", "ctx.erp.FormatCurrency(amount) : string", "Formatea un número como moneda.", "var s = ctx.erp.FormatCurrency(1234.5);", "ERP · Utilidades"),

            new MetodoCtx("erp.DLookup", "ctx.erp.DLookup(field, table, where?) : object", "Consulta puntual de un campo sin escribir SQL.", "var v = ctx.erp.DLookup(\"Total\", \"docDocument\", \"DocumentID=1\");", "ERP · DLookup"),
            new MetodoCtx("erp.DLookupStr", "ctx.erp.DLookupStr(field, table, where?) : string", "DLookup como string.", "var s = ctx.erp.DLookupStr(\"Folio\", \"docDocument\", \"DocumentID=1\");", "ERP · DLookup"),
            new MetodoCtx("erp.DLookupInt", "ctx.erp.DLookupInt(field, table, where?) : int", "DLookup como int.", "var n = ctx.erp.DLookupInt(\"StatusID\", \"docDocument\", \"DocumentID=1\");", "ERP · DLookup"),

            new MetodoCtx("erp.WriteToLog", "ctx.erp.WriteToLog(message) : void", "Escribe al log de CONTPAQi.", "ctx.erp.WriteToLog(\"Proceso OK\");", "ERP · Bitácora"),
            new MetodoCtx("erp.WriteToTableLog", "ctx.erp.WriteToTableLog(message, detail?) : void", "Escribe a la bitácora en tabla.", "ctx.erp.WriteToTableLog(\"Acción\", \"detalle\");", "ERP · Bitácora"),

            new MetodoCtx("erp.PrintDoc", "ctx.erp.PrintDoc(documentId) : void", "Imprime el documento.", "ctx.erp.PrintDoc(id);", "ERP · Impresión y export"),
            new MetodoCtx("erp.PrintModule", "ctx.erp.PrintModule() : void", "Imprime la vista del módulo.", "ctx.erp.PrintModule();", "ERP · Impresión y export"),
            new MetodoCtx("erp.UpdatePrintedOn", "ctx.erp.UpdatePrintedOn(documentId) : void", "Marca el documento como impreso.", "ctx.erp.UpdatePrintedOn(id);", "ERP · Impresión y export"),
            new MetodoCtx("erp.CreatePDF", "ctx.erp.CreatePDF(documentId, outputPath) : string", "Genera el PDF del documento.", "ctx.erp.CreatePDF(id, @\"C:\\temp\\doc.pdf\");", "ERP · Impresión y export"),
            new MetodoCtx("erp.ExportQueryToExcel", "ctx.erp.ExportQueryToExcel(sql, outputPath?) : void", "Exporta el resultado de un SQL a Excel.", "ctx.erp.ExportQueryToExcel(\"SELECT Folio, Total FROM docDocument\");", "ERP · Impresión y export"),
            new MetodoCtx("erp.ExportJanusToExcel", "ctx.erp.ExportJanusToExcel(outputPath?) : void", "Exporta la vista activa del módulo a Excel.", "ctx.erp.ExportJanusToExcel();", "ERP · Impresión y export"),

            new MetodoCtx("erp.SendMail", "ctx.erp.SendMail(to, subject, body, attachmentPath?) : void", "Envía correo con la config de CONTPAQi.", "ctx.erp.SendMail(\"a@b.com\", \"Asunto\", \"Cuerpo\");", "ERP · Correo"),
            new MetodoCtx("erp.GetEmailTemplateID", "ctx.erp.GetEmailTemplateID(templateKey) : string", "ID de una plantilla de correo.", "var id = ctx.erp.GetEmailTemplateID(\"Factura\");", "ERP · Correo"),

            new MetodoCtx("erp.GetWebContent", "ctx.erp.GetWebContent(url) : string", "Descarga el contenido de una URL.", "var html = ctx.erp.GetWebContent(\"https://contpaqi.com\");", "ERP · Web / sistema"),
            new MetodoCtx("erp.GetHTMLFromURL", "ctx.erp.GetHTMLFromURL(url) : string", "Descarga el HTML de una URL (variante).", "var html = ctx.erp.GetHTMLFromURL(\"https://contpaqi.com\");", "ERP · Web / sistema"),
            new MetodoCtx("erp.IsConnectedToInternet", "ctx.erp.IsConnectedToInternet() : bool", "True si hay conexión a internet.", "if (ctx.erp.IsConnectedToInternet()) { /* ... */ }", "ERP · Web / sistema"),
            new MetodoCtx("erp.RunShellExecute", "ctx.erp.RunShellExecute(path, args?) : void", "Ejecuta un programa (ShellExecute).", "ctx.erp.RunShellExecute(@\"C:\\app.exe\");", "ERP · Web / sistema"),

            new MetodoCtx("erp.AlreadyDocsSigned", "ctx.erp.AlreadyDocsSigned(documentId) : bool", "True si el documento está timbrado y válido.", "if (ctx.erp.AlreadyDocsSigned(id)) { /* ... */ }", "ERP · CFDI"),
            new MetodoCtx("erp.GetStatusPaidID", "ctx.erp.GetStatusPaidID(documentId) : int", "Estado de pago (0=sin, 1=parcial, 2=pagado).", "var st = ctx.erp.GetStatusPaidID(id);", "ERP · CFDI"),

            new MetodoCtx("erp.Call", "ctx.erp.Call(metodo, args...) : object", "Llama CUALQUIER miembro de XEngine por nombre (los 562). Tú das los argumentos.", "var qr = ctx.erp.Call(\"GetQRCode\", \"datos\");\r\nctx.erp.Call(\"RecalcProductStock\", 1);", "ERP · Avanzado"),
            new MetodoCtx("erp.Get", "ctx.erp.Get(propiedad) : object", "Lee CUALQUIER propiedad de XEngine por nombre.", "var rfc = (string)ctx.erp.Get(\"COMERCIAL_RFC\");", "ERP · Avanzado"),
            new MetodoCtx("erp.CrearHelper", "ctx.erp.CrearHelper(progId) : object", "Crea un COM auxiliar (Doc.clsMain, LBS.clsMain) con XEngine.", "var doc = ctx.erp.CrearHelper(\"Doc.clsMain\");", "ERP · Avanzado"),
            new MetodoCtx("erp.XE", "ctx.erp.XE : object", "XEngineLib crudo (casos no cubiertos por ctx.erp.*).", "var xe = ctx.erp.XE;", "ERP · Avanzado"),
        };

        // Python: API real del paquete `broslmv` (workers/python/broslmv/ctx.py). El SDK Python
        // expone SOLO `ctx` (NO hay `ctx.erp` en Python; eso es exclusivo de C#). El SQL viaja por
        // la conexión viva (relay) con parámetros estilo @nombre + dict. Ver docs/PYTHON.md.
        private static readonly MetodoCtx[] METODOS_PYTHON = new[]
        {
            // Contexto vivo del botón (propiedades)
            new MetodoCtx("ctx.user_id", "ctx.user_id : int", "ID del usuario activo de CONTPAQi.", "usr = ctx.user_id"),
            new MetodoCtx("ctx.module_id", "ctx.module_id : int", "ID del módulo activo.", "mod = ctx.module_id"),
            new MetodoCtx("ctx.empresa", "ctx.empresa : str", "Base de datos de la empresa activa.", "bd = ctx.empresa"),
            new MetodoCtx("ctx.app_key", "ctx.app_key : str", "AppKey del botón en ejecución.", "clave = ctx.app_key"),
            new MetodoCtx("ctx.fila", "ctx.fila : dict", "Campos de la primera fila seleccionada del grid.", "folio = ctx.fila.get(\"Folio\")"),
            new MetodoCtx("ctx.context", "ctx.context() : dict", "Todo el contexto vivo como diccionario.", "info = ctx.context()"),

            // Selección
            new MetodoCtx("ctx.get_selected_ids", "ctx.get_selected_ids() : list[int]", "IDs de los documentos seleccionados en la vista.", "ids = ctx.get_selected_ids()"),

            // SQL por la conexión viva (parámetros con @nombre + dict, igual que el host)
            new MetodoCtx("ctx.query", "ctx.query(sql, params=None) : list[dict]", "Ejecuta SQL y devuelve una lista de diccionarios. Parámetros con @nombre.", "ids = ctx.get_selected_ids()\r\nfilas = ctx.query(\"SELECT Folio, Total FROM docDocument WHERE DocumentID = @id\", {\"id\": ids[0]})"),
            new MetodoCtx("ctx.scalar", "ctx.scalar(sql, params=None) : Any", "Ejecuta SQL y devuelve el primer valor.", "total = ctx.scalar(\"SELECT SUM(Total) FROM docDocument WHERE DeletedOn IS NULL\")"),
            new MetodoCtx("ctx.execute", "ctx.execute(sql, params=None) : int", "Ejecuta INSERT/UPDATE/DELETE. Devuelve filas afectadas.", "n = ctx.execute(\"UPDATE docDocument SET Referencia = @r WHERE DocumentID = @id\", {\"r\": \"X\", \"id\": ctx.get_selected_ids()[0]})"),

            // Interacción
            new MetodoCtx("ctx.msg", "ctx.msg(texto, titulo=\"BrosLMV\")", "Muestra un mensaje al usuario.", "ctx.msg(\"Proceso terminado\", \"Aviso\")"),
            new MetodoCtx("ctx.confirm", "ctx.confirm(texto, titulo=\"Confirmar\") : bool", "Pregunta Sí/No y bloquea hasta que el usuario responda.", "if ctx.confirm(\"¿Continuar?\"):\r\n    ctx.msg(\"Confirmado\")"),
            new MetodoCtx("ctx.log", "ctx.log(texto, nivel=\"INFO\")", "Escribe a la bitácora/auditoría.", "ctx.log(\"Actualizados {} docs\".format(n))"),
            new MetodoCtx("ctx.progress", "ctx.progress(texto=\"\", porcentaje=0)", "Actualiza el progreso de la ejecución.", "ctx.progress(\"Procesando...\", 50)"),
            new MetodoCtx("ctx.form", "ctx.form(spec) : dict", "Formulario con campos y/o grid editable. Ver plantilla PLANTILLA_EJEMPLO_CONTEO_GRID_PYTHON.py.", "r = ctx.form({\r\n    \"title\": \"Datos\",\r\n    \"fields\": [{\"name\": \"nota\", \"label\": \"Nota\", \"type\": \"text\"}],\r\n})\r\nif r[\"submitted\"]:\r\n    ctx.msg(r[\"values\"][\"nota\"])"),
            new MetodoCtx("ctx.show_html", "ctx.show_html(html, titulo=\"BrosLMV\", ancho=800, alto=600, modal=True)", "Ventana con HTML/CSS/JS real (WebView2). Ver PLANTILLA_EJEMPLO_DASHBOARD_VENTAS_PYTHON.py.", "ctx.show_html(\"<h1>Hola</h1>\", \"Reporte\")"),
            new MetodoCtx("ctx.select_file", "ctx.select_file(titulo=\"...\", filtro=\"Excel|*.xlsx\", guardar=False) : str", "Diálogo nativo para elegir archivo. \"\" si canceló.", "ruta = ctx.select_file(\"Elegir Excel\", \"Excel|*.xlsx\")"),
            new MetodoCtx("ctx.select_folder", "ctx.select_folder(titulo=\"...\") : str", "Diálogo nativo para elegir carpeta. \"\" si canceló.", "carpeta = ctx.select_folder(\"Elegir destino\")"),
            new MetodoCtx("ctx.read_excel", "ctx.read_excel(ruta, hoja=None) : list[dict]", "Lee un .xlsx como lista de dict (encabezados = 1ª fila). No requiere Excel instalado.", "filas = ctx.read_excel(ctx.select_file())"),
            new MetodoCtx("ctx.write_excel", "ctx.write_excel(filas, ruta, hoja=\"Hoja1\")", "Escribe una lista de dict a .xlsx.", "ctx.write_excel([{\"Producto\": \"X\", \"Cant\": 10}], r\"C:\\reporte.xlsx\")"),

            // Valor de retorno
            new MetodoCtx("result", "result = <valor>", "Variable global que devuelve el script (se muestra al usuario).", "result = f\"Empresa={ctx.empresa}, seleccionados={ctx.get_selected_ids()}\""),

            // ctx.erp — operaciones de CONTPAQi (relay al addon). Mismos nombres que C#.
            // OJO: las PROPIEDADES se llaman con () en Python: ctx.erp.UserId().
            new MetodoCtx("ctx.erp.UserId", "ctx.erp.UserId() : int", "Usuario real de CONTPAQi (propiedad → con paréntesis).", "u = ctx.erp.UserId()"),
            new MetodoCtx("ctx.erp.ComercialRFC", "ctx.erp.ComercialRFC() : str", "RFC de la empresa (propiedad → con paréntesis).", "rfc = ctx.erp.ComercialRFC()"),
            new MetodoCtx("ctx.erp.GetProductStock", "ctx.erp.GetProductStock(productID, depotID) : float", "Existencia del producto (depot 0 = todos).", "ex = ctx.erp.GetProductStock(125, 0)"),
            new MetodoCtx("ctx.erp.GetSalePrice", "ctx.erp.GetSalePrice(productID) : float", "Precio de venta del producto.", "pv = ctx.erp.GetSalePrice(125)"),
            new MetodoCtx("ctx.erp.GetCostPrice", "ctx.erp.GetCostPrice(productID) : float", "Costo del producto.", "c = ctx.erp.GetCostPrice(125)"),
            new MetodoCtx("ctx.erp.GetPriceWithTaxes", "ctx.erp.GetPriceWithTaxes(precio, taxTypeID) : float", "Precio con impuestos incluidos.", "t = ctx.erp.GetPriceWithTaxes(100, 1)"),
            new MetodoCtx("ctx.erp.GetTotalLetter", "ctx.erp.GetTotalLetter(importe) : str", "Importe con letra (moneda activa).", "letra = ctx.erp.GetTotalLetter(1234.50)"),
            new MetodoCtx("ctx.erp.GetNextFolio", "ctx.erp.GetNextFolio(moduleID, serie, depotID) : str", "Siguiente folio disponible.", "f = ctx.erp.GetNextFolio(183, \"OC\", 1)"),
            new MetodoCtx("ctx.erp.RecalcDocument", "ctx.erp.RecalcDocument(documentID)", "Recalcula totales del documento (escritura).", "ctx.erp.RecalcDocument(ctx.get_selected_ids()[0])"),
            new MetodoCtx("ctx.erp.RecalcCompleto", "ctx.erp.RecalcCompleto(documentID)", "Recalcula totales + costos del documento.", "ctx.erp.RecalcCompleto(doc_id)"),
            new MetodoCtx("ctx.erp.OwnedBusinessEntityId", "ctx.erp.OwnedBusinessEntityId() : int", "Empresa propia (propiedad → con paréntesis).", "be = ctx.erp.OwnedBusinessEntityId()"),
            new MetodoCtx("ctx.erp.NuevoDocumento", "ctx.erp.NuevoDocumento(moduleID, depotID, businessEntityID=0) : int", "Crea el encabezado de un documento con los defaults del módulo y devuelve el DocumentID.", "doc_id = ctx.erp.NuevoDocumento(183, 1, 162)"),
            new MetodoCtx("ctx.erp.AgregarArticulo", "ctx.erp.AgregarArticulo(documentID, productID, cantidad=1, precio=-1) : int", "Agrega una partida (lee orgProduct). Tras agregar, llamar RecalcCompleto.", "ctx.erp.AgregarArticulo(doc_id, 1, 3, 100)\r\nctx.erp.RecalcCompleto(doc_id)"),
            new MetodoCtx("ctx.erp.Timbrar", "ctx.erp.Timbrar(documentID, pruebas=False)", "Timbra el documento (motor nativo de Comercial). Operación fiscal real.", "if ctx.confirm(\"¿Timbrar?\"):\r\n    ctx.erp.Timbrar(doc_id, False)"),
            new MetodoCtx("ctx.erp.RelacionarCFDI", "ctx.erp.RelacionarCFDI(documentID, sourceDocumentID, tipoRelacion)", "Liga un CFDI con otro (NC, devolución, anticipo).", "ctx.erp.RelacionarCFDI(doc_id, oc_id, \"07\")"),
            new MetodoCtx("ctx.erp.Call", "ctx.erp.Call(metodo, *args)", "Llama CUALQUIER miembro de XEngine por nombre.", "qr = ctx.erp.Call(\"GetQRCode\", \"datos\")"),
            new MetodoCtx("ctx.erp.Get", "ctx.erp.Get(propiedad)", "Lee CUALQUIER propiedad de XEngine por nombre.", "rfc = ctx.erp.Get(\"COMERCIAL_RFC\")"),

            // Active-record genérico: ctx.nuevo(tabla) -> registro con guardar()/actualizar()/eliminar()
            new MetodoCtx("ctx.nuevo", "ctx.nuevo(tabla) : Record", "Crea un registro para INSERT en cualquier tabla. set() campos, guardar() devuelve el ID.", "it = ctx.nuevo(\"docDocumentItem\")\r\nit[\"DocumentID\"] = doc_id\r\nit[\"ProductID\"] = 1\r\nit[\"Quantity\"] = 2\r\nit.guardar()"),
            new MetodoCtx("ctx.registro", "ctx.registro(tabla, pk) : Record", "Carga un registro existente por su PK. Modificar campos y actualizar() solo envía los cambios.", "doc = ctx.registro(\"docDocument\", 11556)\r\ndoc[\"Comments\"] = \"Modificado\"\r\ndoc.actualizar()"),
        };

        // SQL: T-SQL crudo por la conexión viva (ScriptContext.EjecutarSql). Resuelve los tokens
        // de ResolverTokensCore (Scripting.cs) y corre contra la empresa activa (BD ComercialSP).
        // SELECT/EXEC devuelven filas; DML (INSERT/UPDATE/DELETE/EXEC...) se bloquea en SOLO LECTURA.
        // Esquema real: tablas doc*/org*/eng*, soft delete con DeletedOn IS NULL (ver docs/PYTHON.md).
        private static readonly MetodoCtx[] METODOS_SQL = new[]
        {
            // Sentencias
            new MetodoCtx("SELECT", "SELECT ...", "Consulta de datos (devuelve filas).", "SELECT DocumentID, Folio, Total FROM docDocument\r\nWHERE DocumentID IN ({pIDs}) AND DeletedOn IS NULL"),
            new MetodoCtx("EXEC", "EXEC <sp> @p = ...", "Llama un procedimiento almacenado.", "EXEC NombreDelSP @DocumentID = {pID}"),
            new MetodoCtx("UPDATE", "UPDATE ...", "Actualiza datos (bloqueado en SOLO LECTURA).", "UPDATE docDocument SET Referencia = '{DATOS:Folio}'\r\nWHERE DocumentID = {pID}"),
            new MetodoCtx("INSERT", "INSERT ...", "Inserta filas (bloqueado en SOLO LECTURA).", "INSERT INTO miTabla (DocumentID, UserID) VALUES ({pID}, {pUserID})"),
            new MetodoCtx("DELETE", "DELETE ...", "Borra filas (bloqueado en SOLO LECTURA).", "DELETE FROM miTabla WHERE DocumentID = {pID}"),

            // Tokens (se sustituyen antes de ejecutar — ResolverTokensCore)
            new MetodoCtx("{pID}", "{pID}", "Primer ID seleccionado en el grid (0 si no hay).", "WHERE DocumentID = {pID}"),
            new MetodoCtx("{pIDs}", "{pIDs}", "Todos los IDs seleccionados, separados por coma.", "WHERE DocumentID IN ({pIDs})"),
            new MetodoCtx("{pUserID}", "{pUserID}", "ID del usuario activo.", "SET ModifiedUserID = {pUserID}"),
            new MetodoCtx("{pModulo}", "{pModulo}", "ID del módulo activo.", "WHERE ModuleID = {pModulo}"),
            new MetodoCtx("{pEmpresa}", "{pEmpresa}", "Nombre de la BD de la empresa activa.", "-- empresa activa: {pEmpresa}"),
            new MetodoCtx("{DATOS:x}", "{DATOS:Campo}", "Valor del campo en la fila seleccionada del grid.", "WHERE Folio = '{DATOS:Folio}'"),
        };

        // ---- Plantillas / ejemplos (uno bien hecho por cada lenguaje) ----
        private static readonly KeyValuePair<string, string>[] PLANTILLAS = new[]
        {
            // Bases minimas (ventana modeless en blanco + protecciones): punto de partida
            // recomendado para un script nuevo. Ver MANUAL.md #10 "Ventanas WinForms: modeless".
            new KeyValuePair<string,string>("Base  ·  C# WinForms (modeless, en blanco)",
                CargarPlantillaArchivo("PLANTILLA_BASE_CSHARP_WINFORMS.ctx",
                    "// No se encontró la plantilla PLANTILLA_BASE_CSHARP_WINFORMS.ctx.\r\n")),

            new KeyValuePair<string,string>("Base  ·  Python WinForms (modeless, en blanco)",
                CargarPlantillaArchivo("PLANTILLA_BASE_PYTHON_WINFORMS.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            new KeyValuePair<string,string>("Ejemplo Premium  ·  C# WinForms (Nativo)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_CSHARP_WINFORMS.ctx",
                    "// No se encontró la plantilla PLANTILLA_EJEMPLO_CSHARP_WINFORMS.ctx.\r\n")),

            new KeyValuePair<string,string>("Ejemplo Premium  ·  Python WinForms",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_PYTHON_WINFORMS.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            new KeyValuePair<string,string>("Ejemplo Premium  ·  Python Web (HTML/CSS)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_PYTHON_WEB.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            // Orden de Compra (módulo 183): a diferencia de la Requisición (1040), SÍ
            // captura precio por partida (compromiso real con el proveedor) y fecha de
            // entrega esperada. No afecta inventario (eso es la Recepción, módulo 184).
            new KeyValuePair<string,string>("Ejemplo Premium  ·  C# Orden de Compra",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_ORDEN_COMPRA_CSHARP.ctx",
                    "// No se encontró la plantilla PLANTILLA_EJEMPLO_ORDEN_COMPRA_CSHARP.ctx.\r\n")),

            new KeyValuePair<string,string>("Ejemplo Premium  ·  Python Orden de Compra",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_ORDEN_COMPRA_PYTHON.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            // Recepción de Compra (módulo 184): a diferencia de la Orden de Compra, SÍ afecta
            // inventario (AffectStockNEW) y sus partidas vienen de lo pendiente de 1 o varias
            // Órdenes de Compra del MISMO proveedor (cálculo propio vía DeliverDocumentItemID,
            // no de la vista nativa que solo soporta 1 OC por Recepción).
            new KeyValuePair<string,string>("Ejemplo Premium  ·  C# Recepción de Compra",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_RECEPCION_COMPRA_CSHARP.ctx",
                    "// No se encontró la plantilla PLANTILLA_EJEMPLO_RECEPCION_COMPRA_CSHARP.ctx.\r\n")),

            // Factura de Compra (módulo 152): a diferencia de OC/RC, arranca de ctx.GetSelectedIds()
            // (1+ OC seleccionadas en el grid NATIVO de Comercial, no un buscador dentro de la
            // ventana), NO afecta inventario, y SÍ genera póliza contable (automática en Save).
            new KeyValuePair<string,string>("Ejemplo Premium  ·  C# Factura de Compra",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_FACTURA_COMPRA_CSHARP.ctx",
                    "// No se encontró la plantilla PLANTILLA_EJEMPLO_FACTURA_COMPRA_CSHARP.ctx.\r\n")),

            new KeyValuePair<string,string>("Ejemplo Premium  ·  SQL Dashboard",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_SQL.sql",
                    "-- lang: sql\r\n-- No se encontró la plantilla.\r\n")),

            // A partir de aqui: ejemplos de las capacidades agregadas en v2.24.0-2.31.0
            // (ctx.form con grid, ctx.show_html, ctx.erp.Timbrar, ctx.read_excel/select_file).
            // Deliberadamente CORTOS -- el punto es mostrar que estas funciones nuevas
            // reemplazan cientos de lineas de WinForms a mano por unas cuantas declarativas.
            new KeyValuePair<string,string>("Nuevo  ·  Timbrar CFDI (Python)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_TIMBRAR_PYTHON.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            new KeyValuePair<string,string>("Nuevo  ·  Grid editable — conteo físico (Python)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_CONTEO_GRID_PYTHON.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            new KeyValuePair<string,string>("Nuevo  ·  Dashboard HTML de ventas (Python)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_DASHBOARD_VENTAS_PYTHON.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),

            new KeyValuePair<string,string>("Nuevo  ·  Importar Excel → Requisición (Python)",
                CargarPlantillaArchivo("PLANTILLA_EJEMPLO_IMPORTAR_EXCEL_PYTHON.py",
                    "# lang: python\r\n# No se encontró la plantilla.\r\n")),
        };

        private static string CargarPlantillaArchivo(string nombreArchivo, string fallback)
        {
            foreach (var ruta in new[]
            {
                Path.Combine(Rutas.Scripts, nombreArchivo),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", nombreArchivo),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scripts", nombreArchivo),
            })
            {
                try
                {
                    string full = Path.GetFullPath(ruta);
                    // Encoding.UTF8 explícito: sin BOM, File.ReadAllText(path) puede caer al
                    // codepage ANSI del sistema en .NET Framework, convirtiendo acentos/emoji a
                    // "?" -- eso es justo lo que le pasó al guardar una plantilla como AppKey
                    // (zzBrosScript quedó con "Informaci?n" en vez de "Información").
                    if (File.Exists(full)) return File.ReadAllText(full, System.Text.Encoding.UTF8);
                }
                catch { }
            }
            return fallback;
        }

        public BrosConsola(int userId, object xEngineLib)
        {
            _ctx = new ScriptContext(userId, xEngineLib);
            Rutas.AsegurarCarpetas();
            try { Datos.Inicializar(); } catch { }
            BuildUI();
            ConfigurarEditor();
            CargarArbol();
            CargarMetodos();
            NuevoScript();

            // Al mostrarse: dejar que la ventana TERMINE de pintarse y luego cargar el
            // contexto (BeginInvoke lo encola tras el pintado, evita verla "trabada").
            Shown += (s, e) =>
            {
                _lblCtx.Text = "Cargando contexto…";
                BeginInvoke((Action)(() =>
                {
                    ActualizarContexto();
                    try { _empresaInicial = _ctx.Empresa(); } catch { _empresaInicial = null; }
                    _ctxCargado = true;
                }));
                // Precalentar Roslyn con un retraso, para no competir con la apertura.
                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(600);
                    try { ScriptRunner.Precalentar(); } catch { }
                });
            };

            // Modeless: al volver a la ventana (tras minimizar/trabajar en Comercial),
            // refrescar el "contexto actual" porque pudo cambiar de módulo/selección.
            Activated += (s, e) =>
            {
                if (!_ctxCargado) return;
                try { ActualizarContexto(); } catch { }
                AvisarSiCambioEmpresa();
            };
        }

        // Evita refrescar en el primer Activated (que ocurre antes del Shown inicial).
        private bool   _ctxCargado;
        // Empresa activa cuando se abrió la consola. El MOTOR (XEngineLib) se capturó en ese
        // momento; si cambias de empresa en Comercial con la consola abierta, podrías ejecutar
        // contra la empresa equivocada. Por eso se vigila y se avisa/confirma.
        private string _empresaInicial;
        private bool   _avisoEmpresaMostrado;

        // Evita ejecuciones Python superpuestas desde la consola: cada clic de "Ejecutar"
        // lanzaba un BrosLMV.Host.exe nuevo aunque el anterior siguiera corriendo, y todos
        // competían por el mismo hilo de Comercial (UiPump) — eso generaba el diálogo nativo
        // de Windows "the other application is busy" al acumularse ejecuciones encimadas.
        private bool _ejecutandoPython;

        // ¿La empresa activa ahora difiere de la que había al abrir la consola?
        private bool EmpresaCambio()
        {
            if (string.IsNullOrEmpty(_empresaInicial)) return false;
            string actual;
            try { actual = _ctx.Empresa(); } catch { return false; }
            return !string.IsNullOrEmpty(actual)
                && !string.Equals(actual, _empresaInicial, StringComparison.OrdinalIgnoreCase);
        }

        // Avisa UNA vez al reactivar si la empresa cambió (no fastidiar en cada foco).
        private void AvisarSiCambioEmpresa()
        {
            if (EmpresaCambio())
            {
                _lblCtx.ForeColor = AppTheme.Error;
                if (!_avisoEmpresaMostrado)
                {
                    _avisoEmpresaMostrado = true;
                    MessageBox.Show(
                        "La EMPRESA activa cambió desde que abriste la consola.\n\n" +
                        "Abierta en: " + _empresaInicial + "\nActiva ahora: " + SafeEmpresa() + "\n\n" +
                        "La consola sigue ligada al motor de la empresa original. Para evitar " +
                        "ejecutar contra la empresa equivocada, CIÉRRALA y vuelve a abrirla.",
                        "BrosLMV — cambió la empresa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                _lblCtx.ForeColor = AppTheme.TextMuted;
                _avisoEmpresaMostrado = false; // si vuelven a la empresa original, rearmar el aviso
            }
        }

        private string SafeEmpresa()
        {
            try { return _ctx.Empresa(); } catch { return "(?)"; }
        }

        // Diálogo "Acerca de": versión + fecha de compilación + botón a las notas (HTML).
        private void AcercaDe()
        {
            try { using (var dlg = new AcercaForm()) dlg.ShowDialog(this); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Acerca de BrosLMV"); }
        }

        // Extrae las notas de versión (HTML embebido) a %TEMP% y las abre en el navegador
        // del sistema. NO usamos un control web en proceso: abrir fuera = cero peso/latencia.
        internal static void AbrirNotasVersion()
        {
            try
            {
                string dst = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BrosLMV_notas_version.html");
                using (var src = Recurso("notas_version.html"))
                {
                    if (src == null) { MessageBox.Show("No se encontraron las notas de versión embebidas."); return; }
                    using (var fs = System.IO.File.Create(dst)) src.CopyTo(fs);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dst) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("No se pudieron abrir las notas: " + ex.Message); }
        }

        // Fecha de compilación = última escritura de la DLL (un solo stat, instantáneo).
        internal static string FechaCompilacion()
        {
            try { return System.IO.File.GetLastWriteTime(typeof(BrosConsola).Assembly.Location).ToString("yyyy-MM-dd HH:mm"); }
            catch { return "—"; }
        }

        // =====================================================
        //   UI
        // =====================================================
        private void BuildUI()
        {
            Text = "BrosLMV — Consola de scripts";
            Size = new Size(1240, 800);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 660);
            Font = AppTheme.FontMain;
            BackColor = AppTheme.BgMain;
            try { var si = Recurso("BrosLMV.ico"); if (si != null) using (si) Icon = new System.Drawing.Icon(si); } catch { }

            _tips = new ToolTip { InitialDelay = 350, ReshowDelay = 120, AutoPopDelay = 9000, ShowAlways = true };

            // =========================================================
            //   Cabecera: logo + wordmark + subtítulo + estado del doc
            //   (TableLayoutPanel => alineación robusta a cualquier DPI)
            // =========================================================
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = AppTheme.BgChrome };
            pnlHeader.Paint += (s, e) => BordeInferior(e.Graphics, pnlHeader);

            var tlHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(18, 0, 18, 0) };
            tlHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) tlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlHeader.ColumnStyles.Insert(4, new ColumnStyle(SizeType.Percent, 100)); // columna elástica antes del estado

            var picLogo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Width = 34, Height = 34, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 10, 0), BackColor = Color.Transparent };
            try { var sl = Recurso("logo_app.png") ?? Recurso("logo_color.png") ?? Recurso("logo.png"); if (sl != null) using (sl) picLogo.Image = Image.FromStream(sl); } catch { }
            var lblBrand = new Label { Text = "BrosLMV", Font = AppTheme.FontHeader, ForeColor = AppTheme.TextMain, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
            var sepBrand = new Panel { Width = 1, Height = 26, Anchor = AnchorStyles.None, Margin = new Padding(14, 0, 14, 0), BackColor = AppTheme.Border };
            var lblSub = new Label { Text = "Consola de scripts  ·  " + Version, Font = new Font(AppTheme.FontMain.FontFamily, 10.5f), ForeColor = AppTheme.TextMuted, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent };
            _lblEstadoDoc = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Font = AppTheme.FontSmall, ForeColor = AppTheme.Warning, Text = "● Sin guardar", BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 0) };

            tlHeader.Controls.Add(picLogo, 0, 0);
            tlHeader.Controls.Add(lblBrand, 1, 0);
            tlHeader.Controls.Add(sepBrand, 2, 0);
            tlHeader.Controls.Add(lblSub, 3, 0);
            tlHeader.Controls.Add(_lblEstadoDoc, 5, 0);
            pnlHeader.Controls.Add(tlHeader);
            Controls.Add(pnlHeader);

            // =========================================================
            //   Barra de acciones (grupos: Ejecución / Archivo / Tools)
            //   FlowLayoutPanel con botones AutoSize => nunca se enciman.
            // =========================================================
            // WrapContents = true: si la ventana es angosta, la barra reacomoda los botones
            // en una segunda fila en lugar de recortarlos. AutoSize ajusta la altura.
            var pnlToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 52), BackColor = AppTheme.BgChrome, Padding = new Padding(12, 9, 12, 9), WrapContents = true };
            pnlToolbar.Paint += (s, e) => BordeInferior(e.Graphics, pnlToolbar);

            Action<string, string, string, EventHandler, BtnKind, Color> AddTB = (glyph, text, tip, onClick, kind, accent) =>
            {
                var btn = new IconButton { Glyph = glyph, Text = text, Kind = kind, Accent = accent, PadX = 12, MinH = 34, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3, 1, 3, 1) };
                btn.Click += onClick;
                if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(btn, tip);
                pnlToolbar.Controls.Add(btn);
            };
            Action AddSep = () =>
                pnlToolbar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = AppTheme.Border, Margin = new Padding(7, 6, 7, 6) });

            AddTB(Glyph.Play,    "Ejecutar (F5)",       "Ejecutar el script completo (F5)",        (s, e) => Ejecutar(false), BtnKind.Primary, AppTheme.Success);
            AddTB(Glyph.PlaySel, "Ejecutar selección",  "Ejecutar solo el texto seleccionado",     (s, e) => Ejecutar(true),  BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Check,   "Verificar",           "Compilar/verificar sin ejecutar",         (s, e) => Verificar(),     BtnKind.Toolbar, Color.Empty);
            AddSep();
            AddTB(Glyph.New,     "Nuevo",               "Nuevo script",                            (s, e) => NuevoScript(),   BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Open,    "Abrir",               "Importar script desde archivo",           (s, e) => Abrir(),         BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Save,    "Guardar",             "Guardar en la empresa activa",            (s, e) => Guardar(false),  BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.SaveAs,  "Guardar como",        "Guardar con otro nombre (AppKey)",        (s, e) => Guardar(true),   BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Copy,    "Duplicar",            "Duplicar el script actual",               (s, e) => Duplicar(),      BtnKind.Toolbar, Color.Empty);
            AddSep();
            AddTB(Glyph.History, "Historial",            "Ver historial / auditoría de ejecuciones", (s, e) => VerHistorial(),  BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Info,    "Acerca de",            "Versión y notas de cambios",               (s, e) => AcercaDe(),      BtnKind.Toolbar, Color.Empty);
            AddSep();

            _chkSoloLectura = new CheckBox { Text = "Modo solo lectura", AutoSize = true, BackColor = Color.Transparent, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain, Margin = new Padding(8, 9, 0, 0), Cursor = Cursors.Hand };
            _tips.SetToolTip(_chkSoloLectura, "Bloquea operaciones de escritura (UPDATE/DELETE/INSERT)");
            pnlToolbar.Controls.Add(_chkSoloLectura);

            Controls.Add(pnlToolbar);
            pnlToolbar.BringToFront();
            pnlHeader.BringToFront();

            // =========================================================
            //   Barra de estado
            // =========================================================
            var ss = new StatusStrip { BackColor = AppTheme.BgChrome, ForeColor = AppTheme.TextMuted, SizingGrip = false, Padding = new Padding(8, 0, 12, 0), Font = AppTheme.FontMain };
            ss.Renderer = new BordeSuperiorRenderer();
            _status        = new ToolStripStatusLabel("Listo") { TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextMain };
            var sep1       = new ToolStripStatusLabel { Spring = true };
            _statusScript  = new ToolStripStatusLabel("—") { ForeColor = AppTheme.TextMuted };
            _statusLang    = new ToolStripStatusLabel("C#") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusPos     = new ToolStripStatusLabel("Lín 1, Col 1") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusTiempo  = new ToolStripStatusLabel("") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusVer     = new ToolStripStatusLabel(Version) { ForeColor = AppTheme.Primary, IsLink = true, LinkBehavior = LinkBehavior.HoverUnderline, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched, ToolTipText = "Acerca de BrosLMV / notas de versión" };
            _statusVer.Click += (s, e) => AcercaDe();
            ss.Items.AddRange(new ToolStripItem[] { _status, sep1, _statusScript, _statusLang, _statusPos, _statusTiempo, _statusVer });
            Controls.Add(ss);

            // =========================================================
            //   Layout principal (3 zonas redimensionables)
            // =========================================================
            var split1 = _splitLeft = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230, FixedPanel = FixedPanel.Panel1, BorderStyle = BorderStyle.None, SplitterWidth = 6, BackColor = AppTheme.BgMain };
            var split2 = _splitMain = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgMain };
            var split3 = _splitEditor = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgMain };

            // ----- Izquierda: biblioteca de scripts -----
            split1.Panel1.Controls.Add(ConstruirPanelIzquierdo());

            // ----- Centro: editor + salida -----
            split3.Panel1.Controls.Add(ConstruirEditor());
            split3.Panel2.Controls.Add(ConstruirSalida());
            split2.Panel1.Controls.Add(split3);

            // ----- Derecha: inspector de contexto -----
            split2.Panel2.Controls.Add(ConstruirPanelDerecho());

            split1.Panel2.Controls.Add(split2);
            Controls.Add(split1);
            split1.BringToFront();

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { e.Handled = true; Ejecutar(false); }
                else if (e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.B)) { e.Handled = e.SuppressKeyPress = true; MostrarBuscar(); }
                else if (e.KeyCode == Keys.F3) { e.Handled = true; if (_findBar != null && _findBar.Visible) BuscarMover(e.Shift ? -1 : 1); else MostrarBuscar(); }
                else if (e.KeyCode == Keys.F11) { e.Handled = true; ToggleZen(); }
                else if (e.KeyCode == Keys.Escape && _findBar != null && _findBar.Visible) { e.Handled = true; OcultarBuscar(); }
            };

            // El título del documento y el estado se reflejan en la pestaña/cabecera.
            TextChanged += (s, e) => RefrescarEstadoDoc();
            RefrescarEstadoDoc();

            // Ajustar las divisiones cuando los contenedores ya tienen su tamaño real
            // (hacerlo en el constructor falla porque aún miden 0; FixedPanel.Panel2 lo
            // congelaría colapsado). Se ejecuta una sola vez.
            bool layoutDone = false;
            Shown += (s, e) =>
            {
                if (layoutDone) return; layoutDone = true;
                try
                {
                    // Panel izquierdo (FixedPanel) no autoescala con DPI: fijarlo aquí.
                    int izq = Math.Max(LogicalToDeviceUnits(238), (int)(_splitLeft.Width * 0.18));
                    if (_splitLeft.Width > 500) _splitLeft.SplitterDistance = Math.Min(izq, 360);
                }
                catch { }
                try
                {
                    // Ancho del panel derecho robusto a DPI: ~330 px lógicos, pero nunca menos
                    // del 25% ni más de la mitad del área disponible.
                    int derecho = Math.Max(LogicalToDeviceUnits(330), (int)(_splitMain.Width * 0.25));
                    derecho = Math.Min(derecho, _splitMain.Width / 2);
                    if (_splitMain.Width > 600)
                    {
                        _splitMain.SplitterDistance = _splitMain.Width - derecho;
                        _splitMain.Panel2MinSize = LogicalToDeviceUnits(300);
                    }
                }
                catch { }
                try { _splitEditor.SplitterDistance = (int)(_splitEditor.Height * 0.64); } catch { }
                // El árbol no conserva la expansión hecha antes de existir el handle: re-expandir.
                try { _tree.ExpandAll(); if (_tree.Nodes.Count > 0) _tree.Nodes[0].EnsureVisible(); } catch { }
            };
        }

        // Borde inferior/superior de 1px para separar bandas (sustituye sombras pesadas).
        private static void BordeInferior(Graphics g, Control c)
        { using (var p = new Pen(AppTheme.Border)) g.DrawLine(p, 0, c.Height - 1, c.Width, c.Height - 1); }

        // Refleja el nombre del script en la pestaña y el estado "guardado/sin guardar".
        private void RefrescarEstadoDoc()
        {
            bool guardado = !string.IsNullOrEmpty(_appKey);
            if (_lblTabName != null) { _lblTabName.Text = guardado ? _appKey : "Nuevo script"; _lblTabName.Invalidate(); }
            if (_lblEstadoDoc != null)
            {
                _lblEstadoDoc.Text = guardado ? "● Guardado" : "● Sin guardar";
                _lblEstadoDoc.ForeColor = guardado ? AppTheme.Success : AppTheme.Warning;
            }
            if (_statusScript != null) _statusScript.Text = guardado ? _appKey : "(sin guardar)";
        }

        // =========================================================
        //   Panel izquierdo — Biblioteca de scripts
        // =========================================================
        private Panel ConstruirPanelIzquierdo()
        {
            var pnlIzq = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgMain, Padding = new Padding(14, 14, 12, 14) };

            var pnlLibHead = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = AppTheme.BgMain };
            var lblScripts = new Label { Text = "BIBLIOTECA DE SCRIPTS", Dock = DockStyle.Fill, ForeColor = AppTheme.Primary, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.MiddleLeft };
            var btnExpandir = new IconButton { Glyph = Glyph.Down, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 24, Radius = 4, ForeColor = AppTheme.TextMuted };
            _tips.SetToolTip(btnExpandir, "Expandir / contraer todo");
            btnExpandir.Click += (s, e) =>
            {
                bool algunaColapsada = _tree.Nodes.Cast<TreeNode>().Any(n => !n.IsExpanded);
                if (algunaColapsada) _tree.ExpandAll(); else _tree.CollapseAll();
                btnExpandir.Glyph = algunaColapsada ? Glyph.Up : Glyph.Down; btnExpandir.Invalidate();
            };
            pnlLibHead.Controls.Add(lblScripts);
            pnlLibHead.Controls.Add(btnExpandir);

            // Caja de búsqueda con icono y placeholder.
            var pnlBuscar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.BgSurface, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(28, 0, 6, 0) };
            pnlBuscar.Paint += (s, e) =>
            {
                using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, pnlBuscar.Width - 1, pnlBuscar.Height - 1), 6))
                    e.Graphics.DrawPath(p, path);
                TextRenderer.DrawText(e.Graphics, Glyph.Search, AppTheme.FontIconSmall, new Rectangle(8, 0, 18, pnlBuscar.Height), AppTheme.TextMuted, TextFormatFlags.VerticalCenter);
            };
            _txtBuscar = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain };
            _txtBuscar.TextChanged += (s, e) => CargarArbol();
            var pnlBuscarInner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 6, 0, 0) };
            pnlBuscarInner.Controls.Add(_txtBuscar);
            // Placeholder como capa (no toca el .Text): así seleccionar un nodo no reconstruye el árbol.
            AplicarPlaceholder(_txtBuscar, pnlBuscarInner, "Buscar scripts...");
            pnlBuscar.Controls.Add(pnlBuscarInner);

            var espTop = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.BgMain };

            _tree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, BackColor = AppTheme.BgMain, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain, ItemHeight = 26, ShowLines = false, ShowRootLines = true, ShowPlusMinus = true, FullRowSelect = true, Indent = 16 };
            _tree.ImageList = ConstruirIconosArbol();
            _tree.NodeMouseDoubleClick += (s, e) =>
            {
                if (e.Node.Tag is string tag && tag.StartsWith("sql:")) AbrirScript(tag.Substring(4));
                else if (e.Node.Tag is KeyValuePair<string, string> p) InsertarEnEditor(p.Value);
                else { _tree.ExpandAll(); e.Node.EnsureVisible(); }   // doble clic en carpeta: despliega todo
            };
            _tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right && e.Node.Tag is string tg && tg.StartsWith("sql:")) { _tree.SelectedNode = e.Node; TreeMenu(e.Node); } };

            var btnNuevo = new IconButton { Glyph = Glyph.Add, Text = "Nuevo script", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Dock = DockStyle.Bottom, Height = 38, Margin = new Padding(0, 10, 0, 0) };
            btnNuevo.Click += (s, e) => NuevoScript();
            var pnlBtnNuevo = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = AppTheme.BgMain, Padding = new Padding(0, 10, 0, 0) };
            pnlBtnNuevo.Controls.Add(btnNuevo);

            pnlIzq.Controls.Add(_tree);
            pnlIzq.Controls.Add(espTop);
            pnlIzq.Controls.Add(pnlBuscar);
            pnlIzq.Controls.Add(pnlLibHead);
            pnlIzq.Controls.Add(pnlBtnNuevo);
            return pnlIzq;
        }

        private ImageList ConstruirIconosArbol()
        {
            var il = new ImageList { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit };
            il.Images.Add("folder",   ModernUI.GlyphImage(Glyph.Folder,   18, AppTheme.Primary, 11f));
            il.Images.Add("script",   ModernUI.GlyphImage(Glyph.Script,   18, AppTheme.TextMuted, 11f));
            il.Images.Add("template", ModernUI.GlyphImage(Glyph.Template, 18, AppTheme.TextMuted, 11f));
            il.Images.Add("muted",    ModernUI.GlyphImage(Glyph.Script,   18, AppTheme.Border, 11f));
            return il;
        }

        // =========================================================
        //   Centro — Editor con pestaña y controles
        // =========================================================
        private Panel ConstruirEditor()
        {
            var pnlEditorHost = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };

            // --- Franja de pestañas del editor ---
            var pnlTabEditor = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = AppTheme.BgMain };
            pnlTabEditor.Paint += (s, e) => BordeInferior(e.Graphics, pnlTabEditor);

            // Pestaña activa (chip).
            var tabChip = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = AppTheme.BgSurface };
            tabChip.Paint += (s, e) =>
            {
                using (var b = new SolidBrush(AppTheme.Primary)) e.Graphics.FillRectangle(b, 0, 0, tabChip.Width, 2);
                using (var p = new Pen(AppTheme.Border)) { e.Graphics.DrawLine(p, tabChip.Width - 1, 2, tabChip.Width - 1, tabChip.Height); }
            };
            _lblTabName = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontMain, ForeColor = AppTheme.TextMain, Padding = new Padding(12, 0, 0, 0), BackColor = AppTheme.BgSurface, Text = "Nuevo script" };
            var btnCerrarTab = new IconButton { Glyph = Glyph.Close, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 30, Font = AppTheme.FontIconSmall, Radius = 4 };
            _tips.SetToolTip(btnCerrarTab, "Cerrar / nuevo script");
            btnCerrarTab.Click += (s, e) => NuevoScript();
            tabChip.Controls.Add(_lblTabName);
            tabChip.Controls.Add(btnCerrarTab);

            var btnMasTab = new IconButton { Glyph = Glyph.Add, Kind = BtnKind.Ghost, Dock = DockStyle.Left, Width = 34, Radius = 4 };
            _tips.SetToolTip(btnMasTab, "Nuevo script");
            btnMasTab.Click += (s, e) => NuevoScript();

            // Controles discretos a la derecha (tamaño de fuente, ajuste, buscar, pantalla completa).
            var pnlEdTools = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.BgMain, Padding = new Padding(0, 4, 8, 0) };
            _btnZen = new IconButton { Glyph = Glyph.Full, Kind = BtnKind.Ghost, Width = 30, Height = 28, Radius = 4, Margin = new Padding(2, 0, 0, 0) };
            _tips.SetToolTip(_btnZen, "Pantalla completa del editor (F11)");
            _btnZen.Click += (s, e) => ToggleZen();
            var btnBuscarEd = new IconButton { Glyph = Glyph.Search, Kind = BtnKind.Ghost, Width = 30, Height = 28, Radius = 4, Margin = new Padding(2, 0, 0, 0) };
            _tips.SetToolTip(btnBuscarEd, "Buscar en el editor (Ctrl+F)");
            btnBuscarEd.Click += (s, e) => MostrarBuscar();
            var chkWrap = new CheckBox { Text = "Ajuste", AutoSize = true, Appearance = Appearance.Normal, ForeColor = AppTheme.TextMuted, BackColor = Color.Transparent, Font = AppTheme.FontSmall, Margin = new Padding(8, 6, 4, 0), Cursor = Cursors.Hand };
            _tips.SetToolTip(chkWrap, "Ajuste de línea");
            chkWrap.CheckedChanged += (s, e) => { try { _editor.WrapMode = chkWrap.Checked ? WrapMode.Word : WrapMode.None; } catch { } };
            _cboFontSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, Font = AppTheme.FontSmall, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 4, 4, 0) };
            _cboFontSize.Items.AddRange(new object[] { "11 px", "12 px", "13 px", "14 px", "16 px", "18 px" });
            _cboFontSize.SelectedIndex = 0;
            _tips.SetToolTip(_cboFontSize, "Tamaño de fuente del editor");
            _cboFontSize.SelectedIndexChanged += (s, e) =>
            {
                var t = (_cboFontSize.SelectedItem as string ?? "11").Split(' ')[0];
                if (int.TryParse(t, out int pt)) AplicarTamanoFuente(pt);
            };
            pnlEdTools.Controls.Add(_cboFontSize);
            pnlEdTools.Controls.Add(chkWrap);
            pnlEdTools.Controls.Add(btnBuscarEd);
            pnlEdTools.Controls.Add(_btnZen);

            pnlTabEditor.Controls.Add(pnlEdTools);
            pnlTabEditor.Controls.Add(btnMasTab);
            pnlTabEditor.Controls.Add(tabChip);

            _editor = new Scintilla { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
            pnlEditorHost.Controls.Add(_editor);
            pnlEditorHost.Controls.Add(ConstruirBarraBuscar());   // oculta hasta Ctrl+F
            pnlEditorHost.Controls.Add(pnlTabEditor);
            return pnlEditorHost;
        }

        // Barra de búsqueda incremental del editor (oculta por defecto).
        private Panel ConstruirBarraBuscar()
        {
            _findBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = AppTheme.BgMain, Padding = new Padding(10, 5, 10, 5), Visible = false };
            _findBar.Paint += (s, e) => BordeInferior(e.Graphics, _findBar);

            var caja = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = AppTheme.BgSurface, Padding = new Padding(8, 5, 8, 0) };
            caja.Paint += (s, e) => { using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, caja.Width - 1, caja.Height - 1), 5)) e.Graphics.DrawPath(p, path); };
            _txtFind = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain };
            _txtFind.TextChanged += (s, e) => BuscarResaltar();
            _txtFind.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; BuscarMover(e.Shift ? -1 : 1); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; OcultarBuscar(); }
            };
            caja.Controls.Add(_txtFind);

            var btnPrev = new IconButton { Glyph = Glyph.Up, Kind = BtnKind.Ghost, Dock = DockStyle.Left, Width = 30, Radius = 4, Margin = new Padding(6, 0, 0, 0) };
            _tips.SetToolTip(btnPrev, "Anterior (Shift+Enter / Shift+F3)");
            btnPrev.Click += (s, e) => BuscarMover(-1);
            var btnNext = new IconButton { Glyph = Glyph.Down, Kind = BtnKind.Ghost, Dock = DockStyle.Left, Width = 30, Radius = 4 };
            _tips.SetToolTip(btnNext, "Siguiente (Enter / F3)");
            btnNext.Click += (s, e) => BuscarMover(1);
            _lblFindCount = new Label { Dock = DockStyle.Left, AutoSize = false, Width = 110, TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextMuted, Font = AppTheme.FontSmall, Padding = new Padding(8, 0, 0, 0), Text = "" };
            var btnCerrar = new IconButton { Glyph = Glyph.Close, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 30, Radius = 4, Font = AppTheme.FontIconSmall };
            _tips.SetToolTip(btnCerrar, "Cerrar (Esc)");
            btnCerrar.Click += (s, e) => OcultarBuscar();

            // Orden de docking (izq→der): caja, prev, next, contador; cerrar a la derecha.
            _findBar.Controls.Add(_lblFindCount);
            _findBar.Controls.Add(btnNext);
            _findBar.Controls.Add(btnPrev);
            _findBar.Controls.Add(caja);
            _findBar.Controls.Add(btnCerrar);
            return _findBar;
        }

        // =========================================================
        //   Centro inferior — Consola de salida
        // =========================================================
        private Panel ConstruirSalida()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };
            host.Paint += (s, e) => { using (var p = new Pen(AppTheme.Border)) e.Graphics.DrawLine(p, 0, 0, host.Width, 0); };

            // Barra de herramientas de la consola (contador + limpiar + copiar).
            var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.BgMain };
            bar.Paint += (s, e) => BordeInferior(e.Graphics, bar);
            _lblErrCount = new Label { Dock = DockStyle.Left, AutoSize = false, Width = 160, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted, Padding = new Padding(10, 0, 0, 0), Text = "Sin errores" };
            var btnCopiar = new IconButton { Glyph = Glyph.Copy, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 32, Radius = 4 };
            _tips.SetToolTip(btnCopiar, "Copiar salida");
            btnCopiar.Click += (s, e) => CopiarSalida();
            var btnLimpiar = new IconButton { Glyph = Glyph.Clear, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 32, Radius = 4 };
            _tips.SetToolTip(btnLimpiar, "Limpiar salida");
            btnLimpiar.Click += (s, e) => LimpiarSalida();
            bar.Controls.Add(_lblErrCount);
            bar.Controls.Add(btnLimpiar);
            bar.Controls.Add(btnCopiar);

            _tabsOut = new TabControl { Dock = DockStyle.Fill, DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed, ItemSize = new Size(96, 28), Padding = new Point(16, 4), SizeMode = TabSizeMode.Fixed };
            _tabsOut.DrawItem += DrawFlatTab;
            _outSalida   = NuevoOut();
            _outErrores  = NuevoOut();
            _outMensajes = NuevoOut();
            _tabsOut.TabPages.Add(NuevaTab("Salida", _outSalida));
            _tabsOut.TabPages.Add(NuevaTab("Errores", _outErrores));
            _tabsOut.TabPages.Add(NuevaTab("Mensajes", _outMensajes));

            host.Controls.Add(_tabsOut);
            host.Controls.Add(bar);
            return host;
        }

        // =========================================================
        //   Panel derecho — Contexto y referencias
        // =========================================================
        private Panel ConstruirPanelDerecho()
        {
            var pnlDer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgMain, Padding = new Padding(12, 14, 14, 14) };

            // --- Tarjeta de contexto ---
            var pnlCtxCard = new Panel { Dock = DockStyle.Top, Height = 214, BackColor = AppTheme.BgSurface, Padding = new Padding(14, 12, 14, 12) };
            pnlCtxCard.Paint += (s, e) => BordeTarjeta(e.Graphics, pnlCtxCard);

            var pnlCtxHead = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = AppTheme.BgSurface };
            var btnCtxRefresh = new IconButton { Glyph = Glyph.Refresh, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 28, Radius = 4 };
            _tips.SetToolTip(btnCtxRefresh, "Actualizar contexto");
            btnCtxRefresh.Click += (s, e) => ActualizarContexto();
            _lblCtx = new Label { Dock = DockStyle.Right, AutoSize = false, Width = 134, TextAlign = ContentAlignment.MiddleRight, ForeColor = AppTheme.TextMuted, Font = AppTheme.FontSmall, Text = "", Margin = new Padding(0, 0, 4, 0) };
            var lblCtxT = new Label { Text = "Contexto actual", Dock = DockStyle.Fill, ForeColor = AppTheme.Primary, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.MiddleLeft };
            // Orden: primero los anclados a la derecha, el título (Fill) al final ocupa el resto.
            pnlCtxHead.Controls.Add(btnCtxRefresh);
            pnlCtxHead.Controls.Add(_lblCtx);
            pnlCtxHead.Controls.Add(lblCtxT);

            // Contexto en forma de lista: Campo / Valor. Valores largos (la Vista/SELECT)
            // se ven completos con tooltip y se pueden copiar con doble clic.
            _lstCtx = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = AppTheme.FontMain, ShowItemToolTips = true };
            _lstCtx.Columns.Add("Campo", 88);
            _lstCtx.Columns.Add("Valor", 230);
            foreach (var campo in new[] { "Empresa", "Usuario", "Módulo", "Owner", "Vista", "Selección" })
            {
                var it = new ListViewItem(campo);
                it.SubItems.Add("—");
                _lstCtx.Items.Add(it);
            }
            _lstCtx.DoubleClick += (s, e) =>
            {
                if (_lstCtx.SelectedItems.Count > 0 && _lstCtx.SelectedItems[0].SubItems.Count > 1)
                    try { Clipboard.SetText(_lstCtx.SelectedItems[0].SubItems[1].Text); _status.Text = "Copiado: " + _lstCtx.SelectedItems[0].Text; } catch { }
            };
            _tips.SetToolTip(_lstCtx, "Doble clic en una fila para copiar su valor");
            var pnlCtxBody = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 6, 0, 0) };
            pnlCtxBody.Controls.Add(_lstCtx);

            pnlCtxCard.Controls.Add(pnlCtxBody);
            pnlCtxCard.Controls.Add(pnlCtxHead);

            var espCtx = new Panel { Dock = DockStyle.Top, Height = 14, BackColor = AppTheme.BgMain };

            // --- Tarjeta de referencias ---
            var lblMet = new Label { Text = "REFERENCIAS  ·  doble clic para insertar", Dock = DockStyle.Top, Height = 26, ForeColor = AppTheme.Primary, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6) };

            var pnlRefsCard = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };
            pnlRefsCard.Paint += (s, e) => BordeTarjeta(e.Graphics, pnlRefsCard);

            // Pestañas en TableLayoutPanel de 4 columnas iguales: nunca se recortan ni se enciman.
            var pnlTabs = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 4, RowCount = 1, BackColor = AppTheme.BgMain };
            for (int i = 0; i < 4; i++) pnlTabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            pnlTabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pnlTabs.Paint += (s, e) => BordeInferior(e.Graphics, pnlTabs);
            var pnlHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };

            _lstMetodosCSharp = NuevaListaRef("Método", "Descripción");
            _lstMetodosCSharp.DoubleClick += (s, e) => { if (_lstMetodosCSharp.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosCSharp.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            pnlHost.Controls.Add(_lstMetodosCSharp);

            _lstMetodosPython = NuevaListaRef("Método", "Descripción");
            _lstMetodosPython.DoubleClick += (s, e) => { if (_lstMetodosPython.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosPython.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            pnlHost.Controls.Add(_lstMetodosPython);

            _lstMetodosSql = NuevaListaRef("Token/SQL", "Descripción");
            _lstMetodosSql.DoubleClick += (s, e) => { if (_lstMetodosSql.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosSql.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            pnlHost.Controls.Add(_lstMetodosSql);

            _lstSeleccion = NuevaListaRef("Campo", "Valor");
            _lstSeleccion.DoubleClick += (s, e) =>
            {
                if (_lstSeleccion.SelectedItems.Count > 0)
                {
                    string campo = _lstSeleccion.SelectedItems[0].Text;
                    string codigo = _editor.Text;
                    if (HostClient.EsPython(codigo)) InsertarEnEditor("ctx.fila[\"" + campo + "\"]");
                    else if (HostClient.EsSql(codigo)) InsertarEnEditor("{DATOS:" + campo + "}");
                    else InsertarEnEditor("fila[\"" + campo + "\"]");
                }
            };
            pnlHost.Controls.Add(_lstSeleccion);

            var btnTabs = new IconButton[4];
            var listas = new Control[] { _lstMetodosCSharp, _lstMetodosPython, _lstMetodosSql, _lstSeleccion };
            Action<Control, IconButton> ActivarTab = (c, b) =>
            {
                foreach (var lv in listas) lv.Visible = (lv == c);
                c.BringToFront();
                foreach (var bb in btnTabs) { bb.Kind = BtnKind.Ghost; bb.ForeColor = AppTheme.TextMuted; bb.Invalidate(); }
                b.Kind = BtnKind.Outline; b.Accent = AppTheme.Primary; b.Invalidate();
            };
            Func<string, IconButton> NuevoTab = (txt) =>
                new IconButton { Text = txt, Kind = BtnKind.Ghost, Dock = DockStyle.Fill, Margin = new Padding(0), Radius = 0, ForeColor = AppTheme.TextMuted };

            var btnC = btnTabs[0] = NuevoTab("C#");
            var btnP = btnTabs[1] = NuevoTab("Python");
            var btnS = btnTabs[2] = NuevoTab("SQL");
            var btnD = btnTabs[3] = NuevoTab("Datos");
            btnC.Click += (s, e) => ActivarTab(_lstMetodosCSharp, btnC);
            btnP.Click += (s, e) => ActivarTab(_lstMetodosPython, btnP);
            btnS.Click += (s, e) => ActivarTab(_lstMetodosSql, btnS);
            btnD.Click += (s, e) => ActivarTab(_lstSeleccion, btnD);
            pnlTabs.Controls.Add(btnC, 0, 0); pnlTabs.Controls.Add(btnP, 1, 0);
            pnlTabs.Controls.Add(btnS, 2, 0); pnlTabs.Controls.Add(btnD, 3, 0);

            pnlRefsCard.Controls.Add(pnlHost);
            pnlRefsCard.Controls.Add(pnlTabs);
            ActivarTab(_lstMetodosCSharp, btnC);

            var btnRefresca = new IconButton { Glyph = Glyph.Refresh, Text = "Actualizar contexto", Kind = BtnKind.Outline, Accent = AppTheme.Primary, Dock = DockStyle.Bottom, Height = 38, Margin = new Padding(0, 10, 0, 0) };
            btnRefresca.Click += (s, e) => ActualizarContexto();
            var pnlBtnRef = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = AppTheme.BgMain, Padding = new Padding(0, 10, 0, 0) };
            pnlBtnRef.Controls.Add(btnRefresca);

            pnlDer.Controls.Add(pnlRefsCard);
            pnlDer.Controls.Add(lblMet);
            pnlDer.Controls.Add(espCtx);
            pnlDer.Controls.Add(pnlCtxCard);
            pnlDer.Controls.Add(pnlBtnRef);
            return pnlDer;
        }

        private ListView NuevaListaRef(string col1, string col2)
        {
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = AppTheme.FontMain, ShowItemToolTips = true };
            lv.Columns.Add(col1, 120); lv.Columns.Add(col2, 230);
            return lv;
        }

        private static void BordeTarjeta(Graphics g, Control c)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, c.Width - 1, c.Height - 1), 8))
                g.DrawPath(p, path);
        }

        // Placeholder como CAPA encima del TextBox: nunca modifica el .Text del control,
        // por lo que cambiar el foco (p. ej. al hacer clic en un nodo del árbol) no dispara
        // un filtrado/reconstrucción del árbol. El clic en la capa enfoca el TextBox.
        private void AplicarPlaceholder(TextBox t, Panel host, string hint)
        {
            var ph = new Label { Text = hint, Dock = DockStyle.Fill, BackColor = t.BackColor, ForeColor = AppTheme.TextMuted, Font = t.Font, TextAlign = ContentAlignment.MiddleLeft };
            ph.Click += (s, e) => t.Focus();
            host.Controls.Add(ph);
            ph.BringToFront();
            Action upd = () => ph.Visible = (t.Text.Length == 0 && !t.Focused);
            t.TextChanged += (s, e) => upd();
            t.GotFocus    += (s, e) => upd();
            t.LostFocus   += (s, e) => upd();
            upd();
        }

        private void CopiarSalida()
        {
            var box = _tabsOut.SelectedIndex == 1 ? _outErrores : _tabsOut.SelectedIndex == 2 ? _outMensajes : _outSalida;
            try { if (!string.IsNullOrEmpty(box.Text)) Clipboard.SetText(box.Text); } catch { }
        }
        private void LimpiarSalida()
        {
            var box = _tabsOut.SelectedIndex == 1 ? _outErrores : _tabsOut.SelectedIndex == 2 ? _outMensajes : _outSalida;
            box.Clear();
            if (box == _outErrores) ActualizarContadorErrores();
        }
        private void ActualizarContadorErrores()
        {
            if (_lblErrCount == null) return;
            int n = _outErrores.TextLength == 0 ? 0 : _outErrores.Lines.Count(l => l.StartsWith("["));
            _lblErrCount.Text = n == 0 ? "Sin errores" : (n + (n == 1 ? " error" : " errores"));
            _lblErrCount.ForeColor = n == 0 ? AppTheme.TextMuted : AppTheme.Error;
        }

        // =========================================================
        //   Buscar en el editor (Ctrl+F / Ctrl+B, F3, resaltado)
        // =========================================================
        private void MostrarBuscar()
        {
            if (_findBar == null) return;
            // Si hay texto seleccionado, úsalo como término inicial.
            string sel = _editor.SelectedText;
            _findBar.Visible = true;
            if (!string.IsNullOrEmpty(sel) && !sel.Contains("\n")) _txtFind.Text = sel;
            _txtFind.Focus();
            _txtFind.SelectAll();
            BuscarResaltar();
        }

        private void OcultarBuscar()
        {
            if (_findBar == null) return;
            _findBar.Visible = false;
            try { _editor.IndicatorCurrent = IND_FIND; _editor.IndicatorClearRange(0, _editor.TextLength); } catch { }
            _findHits.Clear(); _findIdx = -1;
            _editor.Focus();
        }

        // Resalta todas las coincidencias y deja lista la navegación.
        private void BuscarResaltar()
        {
            _findHits.Clear(); _findIdx = -1;
            try
            {
                _editor.Indicators[IND_FIND].Style = IndicatorStyle.RoundBox;
                _editor.Indicators[IND_FIND].ForeColor = AppTheme.Warning;
                _editor.Indicators[IND_FIND].Alpha = 70;
                _editor.Indicators[IND_FIND].OutlineAlpha = 120;
                _editor.IndicatorCurrent = IND_FIND;
                _editor.IndicatorClearRange(0, _editor.TextLength);
            }
            catch { }

            string q = _txtFind.Text;
            if (string.IsNullOrEmpty(q)) { if (_lblFindCount != null) _lblFindCount.Text = ""; return; }

            string txt = _editor.Text;
            int from = 0;
            while (true)
            {
                int idx = txt.IndexOf(q, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                _findHits.Add(idx);
                try { _editor.IndicatorFillRange(idx, q.Length); } catch { }
                from = idx + Math.Max(1, q.Length);
            }

            if (_findHits.Count == 0) { _lblFindCount.Text = "Sin coincidencias"; _lblFindCount.ForeColor = AppTheme.Error; return; }
            _lblFindCount.ForeColor = AppTheme.TextMuted;
            // Posiciona en la 1a coincidencia a partir del cursor.
            int caret = _editor.CurrentPosition;
            _findIdx = _findHits.FindIndex(p => p >= caret);
            if (_findIdx < 0) _findIdx = 0;
            SeleccionarHit(q.Length);
        }

        private void BuscarMover(int dir)
        {
            if (_findHits.Count == 0) { BuscarResaltar(); if (_findHits.Count == 0) return; }
            _findIdx = (_findIdx + dir + _findHits.Count) % _findHits.Count;
            SeleccionarHit(_txtFind.Text.Length);
        }

        private void SeleccionarHit(int len)
        {
            if (_findIdx < 0 || _findIdx >= _findHits.Count) return;
            int start = _findHits[_findIdx];
            _editor.SetSelection(start, start + len);
            _editor.ScrollCaret();
            if (_lblFindCount != null) _lblFindCount.Text = (_findIdx + 1) + " de " + _findHits.Count;
        }

        // =========================================================
        //   Pantalla completa del editor (zen): colapsa los paneles.
        // =========================================================
        private void ToggleZen()
        {
            _zen = !_zen;
            try
            {
                _splitLeft.Panel1Collapsed = _zen;     // biblioteca
                _splitMain.Panel2Collapsed = _zen;     // contexto
                _splitEditor.Panel2Collapsed = _zen;   // salida
                _btnZen.Glyph = _zen ? Glyph.Restore : Glyph.Full;
                _btnZen.Invalidate();
                _tips.SetToolTip(_btnZen, _zen ? "Salir de pantalla completa (F11)" : "Pantalla completa del editor (F11)");
            }
            catch { }
        }

        private void AplicarTamanoFuente(int pt)
        {
            _fontSize = pt;
            try { EstilizarEditor(pt); } catch { }
        }

        private RichTextBox NuevoOut()
        {
            return new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMono, WordWrap = false, Padding = new Padding(8) };
        }
        private TabPage NuevaTab(string titulo, Control c) { var t = new TabPage(titulo) { BackColor = AppTheme.BgSurface }; c.Dock = DockStyle.Fill; t.Controls.Add(c); return t; }

        private void DrawFlatTab(object sender, DrawItemEventArgs e)
        {
            var tab = (TabControl)sender;
            var page = tab.TabPages[e.Index];
            var rect = e.Bounds;
            
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            e.Graphics.FillRectangle(new SolidBrush(AppTheme.BgMain), rect);
            
            if (isSelected)
            {
                e.Graphics.FillRectangle(new SolidBrush(AppTheme.BgSurface), rect);
                e.Graphics.FillRectangle(new SolidBrush(AppTheme.Primary), new Rectangle(rect.X, rect.Y, rect.Width, 2));
            }
            
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var brush = new SolidBrush(isSelected ? AppTheme.Primary : AppTheme.TextMuted);
            e.Graphics.DrawString(page.Text, AppTheme.FontMain, brush, rect, format);
        }

        // Lee un recurso embebido del addon (logo, icono) por terminacion del nombre.
        private static System.IO.Stream Recurso(string endsWith)
        {
            var asm = Assembly.GetExecutingAssembly();
            var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            return n == null ? null : asm.GetManifestResourceStream(n);
        }

        // =====================================================
        //   Editor Scintilla
        // =====================================================
        private void ConfigurarEditor()
        {
            var ed = _editor;
            EstilizarEditor(_fontSize);

            ed.SetKeywords(0, "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while async await dynamic");
            ed.SetKeywords(1, "List Dictionary StringBuilder DateTime Math Convert Console MessageBox Color Form ctx");

            // Posición del cursor en la barra de estado.
            ed.UpdateUI += (s, e) =>
            {
                if (_statusPos == null) return;
                int line = ed.CurrentLine + 1;
                int col = ed.CurrentPosition - ed.Lines[ed.CurrentLine].Position + 1;
                _statusPos.Text = "Lín " + line + ", Col " + col;
            };
            ed.TextChanged += (s, e) => DetectarLenguajeStatus();

            // Atajos dentro del editor (Scintilla es nativo y a veces captura las teclas
            // antes que KeyPreview del formulario, así que los atendemos aquí también).
            ed.KeyDown += (s, e) =>
            {
                if (e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.B)) { e.SuppressKeyPress = true; MostrarBuscar(); }
                else if (e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; if (_findBar != null && _findBar.Visible) BuscarMover(e.Shift ? -1 : 1); else MostrarBuscar(); }
                else if (e.KeyCode == Keys.F11) { e.SuppressKeyPress = true; ToggleZen(); }
                else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; Ejecutar(false); }
            };

            // Autocompletado de ctx.
            ed.CharAdded += (s, e) =>
            {
                if (e.Char != '.') return;
                int pos = ed.CurrentPosition;
                int dotPos = pos - 1;
                int ini = ed.WordStartPosition(dotPos, true);
                string w = ed.GetTextRange(ini, dotPos - ini);
                if (w == "ctx")
                {
                    var lista = string.Join(" ", METODOS.Select(m => m.Nombre).OrderBy(x => x).ToArray());
                    ed.AutoCShow(0, lista);
                }
            };
        }

        // Aplica colores, fuente y márgenes del editor para un tamaño dado (reutilizable).
        private void EstilizarEditor(int size)
        {
            var ed = _editor;
            ed.Lexer = Lexer.Cpp;
            ed.Styles[Style.Default].Font = AppTheme.FontMono.Name;
            ed.Styles[Style.Default].Size = size;
            ed.Styles[Style.Default].BackColor = AppTheme.BgSurface;
            ed.Styles[Style.Default].ForeColor = AppTheme.TextMain;
            ed.StyleClearAll();

            ed.Styles[Style.Cpp.Comment].ForeColor = Color.FromArgb(0, 128, 0);
            ed.Styles[Style.Cpp.CommentLine].ForeColor = Color.FromArgb(0, 128, 0);
            ed.Styles[Style.Cpp.CommentLineDoc].ForeColor = Color.FromArgb(128, 128, 128);
            ed.Styles[Style.Cpp.Number].ForeColor = Color.FromArgb(9, 134, 88);
            ed.Styles[Style.Cpp.String].ForeColor = Color.FromArgb(163, 21, 21);
            ed.Styles[Style.Cpp.Character].ForeColor = Color.FromArgb(163, 21, 21);
            ed.Styles[Style.Cpp.Word].ForeColor = Color.FromArgb(0, 0, 255);
            ed.Styles[Style.Cpp.Word2].ForeColor = Color.FromArgb(43, 145, 175);
            ed.Styles[Style.Cpp.Operator].ForeColor = Color.FromArgb(100, 100, 100);
            ed.Styles[Style.Cpp.Preprocessor].ForeColor = Color.FromArgb(128, 128, 128);

            // Gutter de números con fondo claro propio.
            ed.Styles[Style.LineNumber].BackColor = AppTheme.BgSubtle;
            ed.Styles[Style.LineNumber].ForeColor = AppTheme.TextMuted;
            ed.Margins[0].Type = MarginType.Number;
            ed.Margins[0].Width = 46;
            ed.Margins[0].BackColor = AppTheme.BgSubtle;
            ed.Margins[1].Width = 8;
            ed.Margins[1].BackColor = AppTheme.BgSurface;

            ed.CaretLineVisible = true;
            ed.CaretLineBackColor = Color.FromArgb(244, 247, 251);
            ed.CaretForeColor = AppTheme.TextMain;
            ed.SetSelectionBackColor(true, AppTheme.PrimarySelected);
            ed.ExtraDescent = 3; // mejor interlineado
        }

        // Refleja el lenguaje detectado en la barra de estado.
        private void DetectarLenguajeStatus()
        {
            if (_statusLang == null) return;
            string c = _editor.Text;
            _statusLang.Text = HostClient.EsPython(c) ? "Python" : HostClient.EsSql(c) ? "SQL" : "C#";
        }

        // =====================================================
        //   Biblioteca de scripts (en SQL: zzBrosScript, por empresa)
        // =====================================================
        private void CargarArbol()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            string filtro = (_txtBuscar.Text ?? "").Trim().ToLower();

            string emp = "";
            try { emp = _ctx.Empresa(); } catch { }
            bool disponible = false;
            try { disponible = _ctx.BrosScriptsDisponible(); } catch { }

            var nScripts = new TreeNode("Scripts — " + (string.IsNullOrEmpty(emp) ? "(sin empresa)" : emp)) { ImageKey = "folder", SelectedImageKey = "folder" };
            if (!disponible)
            {
                nScripts.Nodes.Add(new TreeNode("(sin conexión o empresa no provisionada)") { ForeColor = AppTheme.TextMuted, ImageKey = "muted", SelectedImageKey = "muted" });
            }
            else
            {
                try
                {
                    foreach (var r in _ctx.BrosListar())
                    {
                        string ak = Convert.ToString(r["AppKey"]);
                        if (filtro != "" && !ak.ToLower().Contains(filtro)) continue;
                        nScripts.Nodes.Add(new TreeNode(ak) { Tag = "sql:" + ak, ImageKey = "script", SelectedImageKey = "script" });
                    }
                }
                catch { }
            }
            _tree.Nodes.Add(nScripts);

            var nPlant = new TreeNode("Plantillas") { ImageKey = "folder", SelectedImageKey = "folder" };
            foreach (var p in PLANTILLAS)
                if (filtro == "" || p.Key.ToLower().Contains(filtro))
                    nPlant.Nodes.Add(new TreeNode(p.Key) { Tag = p, ImageKey = "template", SelectedImageKey = "template" });
            _tree.Nodes.Add(nPlant);

            _tree.ExpandAll();
            _tree.EndUpdate();
        }

        // Menú contextual sobre un script (en SQL).
        private void TreeMenu(TreeNode nodo)
        {
            if (nodo == null || !(nodo.Tag is string tag) || !tag.StartsWith("sql:")) return;
            string ak = tag.Substring(4);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, (s, e) => AbrirScript(ak));
            menu.Items.Add("Eliminar…", null, (s, e) =>
            {
                if (MessageBox.Show("¿Eliminar el script \"" + ak + "\" de esta empresa?", "BrosLMV",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try { _ctx.BrosBorrar(ak); } catch (Exception ex) { ctxError(ex.Message); }
                    CargarArbol();
                }
            });
            menu.Show(_tree, _tree.PointToClient(Cursor.Position));
        }

        private void VerHistorial()
        {
            var datos = Datos.UltimasEjecuciones(200);
            var frm = new Form { Text = "Historial / Auditoría de ejecuciones", Size = new Size(900, 500), StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9f) };
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lv.Columns.Add("Fecha", 130);
            lv.Columns.Add("Empresa", 150);
            lv.Columns.Add("Mód.", 45);
            lv.Columns.Add("Usr", 40);
            lv.Columns.Add("Script", 150);
            lv.Columns.Add("Origen", 60);
            lv.Columns.Add("ms", 55);
            lv.Columns.Add("Filas", 50);
            lv.Columns.Add("Estado", 60);
            lv.Columns.Add("Error", 150);
            foreach (var r in datos)
            {
                var it = new ListViewItem(Convert.ToString(r["fecha"]));
                it.SubItems.Add(Convert.ToString(r["empresa"]));
                it.SubItems.Add(Convert.ToString(r["modulo"]));
                it.SubItems.Add(Convert.ToString(r["usuario"]));
                it.SubItems.Add(Convert.ToString(r["script"]));
                it.SubItems.Add(Convert.ToString(r["origen"]));
                it.SubItems.Add(Convert.ToString(r["duracion_ms"]));
                it.SubItems.Add(Convert.ToString(r["filas"]));
                it.SubItems.Add(Convert.ToString(r["estado"]));
                it.SubItems.Add(Convert.ToString(r["error"]));
                if (Convert.ToString(r["estado"]) == "ERROR") it.ForeColor = Color.Firebrick;
                lv.Items.Add(it);
            }
            frm.Controls.Add(lv);
            frm.ShowDialog(this);
        }

        private void CargarMetodos()
        {
            // C#: agrupado por categoría (son ~75 métodos; los grupos lo hacen navegable).
            _lstMetodosCSharp.Items.Clear();
            _lstMetodosCSharp.Groups.Clear();
            _lstMetodosCSharp.ShowGroups = true;
            var grupos = new Dictionary<string, ListViewGroup>();
            foreach (var m in METODOS)
            {
                string cat = string.IsNullOrEmpty(m.Cat) ? "General" : m.Cat;
                if (!grupos.TryGetValue(cat, out var g))
                {
                    g = new ListViewGroup(cat) { HeaderAlignment = HorizontalAlignment.Left };
                    grupos[cat] = g;
                    _lstMetodosCSharp.Groups.Add(g);
                }
                var it = new ListViewItem(m.Nombre, g) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosCSharp.Items.Add(it);
            }
            _lstMetodosCSharp.ShowItemToolTips = true;

            _lstMetodosPython.Items.Clear();
            foreach (var m in METODOS_PYTHON)
            {
                var it = new ListViewItem(m.Nombre) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosPython.Items.Add(it);
            }
            _lstMetodosPython.ShowItemToolTips = true;

            _lstMetodosSql.Items.Clear();
            foreach (var m in METODOS_SQL)
            {
                var it = new ListViewItem(m.Nombre) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosSql.Items.Add(it);
            }
            _lstMetodosSql.ShowItemToolTips = true;

            Alternar(_lstMetodosPython); Alternar(_lstMetodosSql);   // C# usa grupos, sin zebra
            _lstMetodosCSharp.BringToFront();   // pestaña C# activa al inicio
        }

        private void ActualizarContexto()
        {
            string emp = "-", mod = "-", sel = "-", pk = "DocumentID", source = "-", ownerInfo = "-";
            try { emp = _ctx.Empresa(); } catch { }
            try { mod = _ctx.ModuloActivo().ToString(); } catch { }
            
            try 
            { 
                var ids = _ctx.GetSelectedIds(); 
                sel = ids.Count + (ids.Count > 0 ? "  (" + string.Join(",", ids.Take(8)) + (ids.Count > 8 ? "…" : "") + ")" : ""); 
            } catch { }

            try
            {
                pk = GridSelection.LlaveDeModulo(_ctx.XEngineLib, null);
                
                object jg = Com.GetProp(_ctx.XEngineLib, "janusGrid");
                if (jg != null)
                {
                    object rs = Com.GetProp(jg, "ADORecordset");
                    if (rs != null)
                    {
                        object s = Com.GetProp(rs, "Source");
                        if (s != null) source = s.ToString();
                    }
                }

                int ownerId = _ctx.erp.OwnedBusinessEntityId;
                ownerInfo = ownerId.ToString();
                if (ownerId > 0)
                {
                    var res = _ctx.Query("SELECT NombreOrganizacion FROM orgOrganizacion WHERE OrganizacionID = " + ownerId);
                    if (res.Count > 0 && res[0].ContainsKey("NombreOrganizacion"))
                        ownerInfo = ownerId + " - " + Convert.ToString(res[0]["NombreOrganizacion"]);
                }
            } catch { }

            // Usuario activo: el ID del constructor suele venir en 0; preferir el de XEngine + nombre.
            string usuario = _ctx.UserID.ToString();
            try
            {
                int uid = _ctx.erp.UserId;
                string uname = _ctx.erp.UserName;
                if (uid <= 0) uid = _ctx.UserID;
                usuario = uid + (string.IsNullOrEmpty(uname) ? "" : " - " + uname);
            }
            catch { }

            // Rellenar la lista de contexto (con tooltip al valor completo).
            SetCtx("Empresa", emp);
            SetCtx("Usuario", usuario);
            SetCtx("Módulo", mod + "   (PK: " + pk + ")");
            SetCtx("Owner", ownerInfo);
            SetCtx("Vista", NombreVista(source));   // solo el nombre de la vista/tabla, no el SELECT completo
            SetCtx("Selección", sel);
            Alternar(_lstCtx);
            if (_lblCtx != null) _lblCtx.Text = "Actualizado " + DateTime.Now.ToString("HH:mm:ss");

            // Llenar pestaña de selección
            _lstSeleccion.Items.Clear();
            var fila = _ctx.GetFilaActiva();
            if (fila != null)
            {
                foreach (var kvp in fila)
                {
                    var it = new ListViewItem(kvp.Key);
                    it.SubItems.Add(Convert.ToString(kvp.Value));
                    _lstSeleccion.Items.Add(it);
                }
            }
            Alternar(_lstSeleccion);
        }

        // Extrae el nombre de la vista/tabla que se está consultando (lo que sigue al primer FROM),
        // en vez de mostrar el SELECT completo. Quita corchetes y esquema (dbo.).
        private static string NombreVista(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source == "-") return source;
            var m = Regex.Match(source, @"\bFROM\s+\[?(?<n>[A-Za-z0-9_\.\]\[]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return source.Trim();
            string n = m.Groups["n"].Value.Replace("[", "").Replace("]", "");
            int dot = n.LastIndexOf('.');
            if (dot >= 0 && dot < n.Length - 1) n = n.Substring(dot + 1);  // quitar esquema dbo.
            return n;
        }

        // Asigna el valor de una fila de la lista de contexto (con tooltip del valor completo).
        private void SetCtx(string clave, string valor)
        {
            if (_lstCtx == null) return;
            valor = string.IsNullOrEmpty(valor) ? "—" : valor;
            foreach (ListViewItem it in _lstCtx.Items)
                if (it.Text == clave) { it.SubItems[1].Text = valor; it.ToolTipText = clave + ": " + valor; break; }
        }

        // Filas alternadas muy sutiles en una lista de referencias.
        private void Alternar(ListView lv)
        {
            for (int i = 0; i < lv.Items.Count; i++)
                lv.Items[i].BackColor = (i % 2 == 0) ? AppTheme.BgSurface : AppTheme.BgSubtle;
        }

        // =====================================================
        //   Acciones de script (en SQL, por empresa)
        // =====================================================
        private void NuevoScript()
        {
            _appKey = "";
            _editor.Text = "";
            Text = "BrosLMV — Consola de scripts — (sin guardar)";
            _status.Text = "Nuevo script";
        }

        // Abre un script de la empresa activa (desde zzBrosScript).
        private void AbrirScript(string appKey)
        {
            try
            {
                string codigo = _ctx.BrosCargar(appKey);
                if (codigo == null) { ctxError("No se encontró el script: " + appKey); return; }
                _editor.Text = codigo;
                _appKey = appKey;
                Text = "BrosLMV — " + appKey;
                _status.Text = "Abierto: " + appKey;
            }
            catch (Exception ex) { ctxError("No se pudo abrir: " + ex.Message); }
        }

        // Importar desde archivo (.ctx/.csx): carga el contenido al editor; con Guardar
        // queda registrado en la empresa. Sirve para migrar scripts viejos a SQL.
        private void Abrir()
        {
            using (var dlg = new OpenFileDialog { InitialDirectory = Rutas.Scripts, Filter = "Scripts BrosLMV (*.ctx;*.csx)|*.ctx;*.csx|Todos|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        _editor.Text = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                        _appKey = Path.GetFileNameWithoutExtension(dlg.FileName);
                        Text = "BrosLMV — (importado) " + _appKey;
                        _status.Text = "Importado de archivo: usa Guardar para registrarlo en la empresa";
                    }
                    catch (Exception ex) { ctxError("No se pudo abrir: " + ex.Message); }
                }
        }

        private void Guardar(bool comoNuevo)
        {
            string ak = _appKey;
            if (comoNuevo || string.IsNullOrEmpty(ak))
            {
                ak = PedirTexto("Nombre del script (AppKey, sin espacios).\nEl botón usará  BrosLMV.<AppKey>:",
                                string.IsNullOrEmpty(_appKey) ? "MI_SCRIPT" : _appKey);
                if (string.IsNullOrEmpty(ak)) return;
                ak = ak.Trim().Replace(" ", "_");
            }
            try
            {
                _ctx.BrosAsegurarTablas();   // crea zzBros* si faltan (requiere conexión viva)
                _ctx.BrosGuardar(ak, ak, _editor.Text, SafeModulo());
                _appKey = ak;
                Text = "BrosLMV — " + ak;
                _status.Text = "Guardado en la empresa: " + ak;
                CargarArbol();
            }
            catch (Exception ex)
            {
                ctxError("No se pudo guardar: " + ex.Message +
                    "\r\n\r\nVerifica que CONTPAQi tenga abierta esta empresa (conexión viva). " +
                    "Ejecuta DIAGNOSTICO para revisar la conexión.");
            }
        }

        private void Duplicar()
        {
            _appKey = "";
            Text = "BrosLMV — Consola de scripts — (copia sin guardar)";
            _status.Text = "Copia: usa Guardar para nombrarla";
        }

        private int SafeModulo() { try { return _ctx.ModuloActivo(); } catch { return 0; } }

        // Diálogo moderno para pedir un texto (el nombre/AppKey del script).
        private string PedirTexto(string prompt, string valor)
        {
            using (var f = new Form { Text = "BrosLMV", Width = 480, Height = 210, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = AppTheme.BgSurface, Font = AppTheme.FontMain })
            {
                var pnlHead = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.Primary };
                var lbl = new Label { Text = prompt, Left = 22, Top = 22, Width = 430, Height = 48, ForeColor = AppTheme.TextMain, BackColor = Color.Transparent };
                var txt = new TextBox { Left = 22, Top = 76, Width = 430, Text = valor ?? "", BorderStyle = BorderStyle.FixedSingle, Font = AppTheme.FontMain };
                var ok = new IconButton { Text = "Aceptar", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Left = 296, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.OK };
                var ca = new IconButton { Text = "Cancelar", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, Left = 382, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, ca, pnlHead });
                f.AcceptButton = ok; f.CancelButton = ca;
                txt.SelectAll(); txt.Focus();
                return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        private void InsertarEnEditor(string texto)
        {
            _editor.InsertText(_editor.CurrentPosition, texto);
            _editor.CurrentPosition += texto.Length;
            _editor.Focus();
        }

        // =====================================================
        //   Ejecución segura
        // =====================================================
        private static readonly Regex RX_PELIGRO = new Regex(@"\b(DELETE|UPDATE|INSERT|DROP|TRUNCATE|ALTER|MERGE|EXEC)\b", RegexOptions.IgnoreCase);

        private void Verificar()
        {
            string codigo = _editor.Text;
            if (HostClient.EsPython(codigo) || HostClient.EsSql(codigo))
            {
                Salida("Script " + (HostClient.EsPython(codigo) ? "Python" : "SQL") +
                       ": la verificación se realiza al ejecutar (F5).", Color.FromArgb(120, 180, 220));
                _tabsOut.SelectedIndex = 0; _status.Text = HostClient.EsPython(codigo) ? "Python" : "SQL";
                return;
            }
            var errores = ScriptRunner.Compilar(codigo);
            if (errores.Count == 0) { Salida("Compila correctamente. Sin errores.", AppTheme.Success); _tabsOut.SelectedIndex = 0; Estado("Verificado: OK", AppTheme.Success); }
            else { Errores(string.Join("\r\n", errores.ToArray())); _tabsOut.SelectedIndex = 1; Estado("Verificado: " + errores.Count + " error(es)", AppTheme.Error); }
        }

        private void Ejecutar(bool soloSeleccion)
        {
            string codigo = soloSeleccion && _editor.SelectedText.Length > 0 ? _editor.SelectedText : _editor.Text;

            // Guardia de empresa: si cambió desde que se abrió la consola, el motor (XEngineLib)
            // sigue ligado a la empresa original → confirmar para no ejecutar en la equivocada.
            if (EmpresaCambio())
            {
                if (MessageBox.Show(
                        "¡OJO! La empresa cambió desde que abriste la consola.\n\n" +
                        "Abierta en: " + _empresaInicial + "\nActiva ahora: " + SafeEmpresa() + "\n\n" +
                        "La consola ejecuta contra el motor de la empresa ORIGINAL (" + _empresaInicial +
                        "). Lo recomendable es cerrarla y reabrirla.\n\n¿Ejecutar de todos modos?",
                        "BrosLMV — cambió la empresa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    != DialogResult.Yes) return;
            }

            // Protección: detectar operaciones de escritura
            if (RX_PELIGRO.IsMatch(codigo) && !_chkSoloLectura.Checked)
            {
                if (MessageBox.Show("El script contiene operaciones que pueden MODIFICAR datos (UPDATE/DELETE/INSERT/…).\n\n¿Ejecutar de todos modos?",
                    "Confirmar ejecución", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            bool esPython = HostClient.EsPython(codigo);

            // Ejecuciones Python superpuestas (dos clics de "Ejecutar" antes de que la primera
            // termine) generaban un host nuevo por clic, todos compitiendo por el mismo hilo de
            // Comercial vía UiPump — eso es lo que disparaba el "busy" nativo de Windows/XEngine
            // cuando se acumulaban. Un guardia simple evita el problema de raíz.
            if (esPython && _ejecutandoPython)
            {
                MessageBox.Show("Ya hay un script Python en ejecución desde esta consola. Espera a que termine antes de volver a ejecutar.",
                    "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _ctx.SoloLectura = _chkSoloLectura.Checked;
            Salida("Ejecutando" + (soloSeleccion ? " (selección)" : "") + "…", AppTheme.TextMuted);
            Estado("Ejecutando…", AppTheme.Warning);
            Application.DoEvents();

            int filasAntes = _ctx.FilasAfectadas;
            var sw = Stopwatch.StartNew();

            if (esPython)
            {
                // Python corre en un proceso aparte (BrosLMV.Host.exe); si se ejecuta SINCRONO
                // aqui, el hilo de Comercial (el mismo de esta consola) queda bloqueado todo el
                // tiempo que la ventana Python este abierta -- Comercial deja de bombear
                // mensajes y Windows puede mostrar "the other application is busy". Por eso se
                // lanza en Task.Run, igual que el boton del ribbon (ClsMain.EjecutarPython):
                // Comercial y la consola quedan libres mientras Python corre en segundo plano.
                _ejecutandoPython = true;
                var hctx = new HostClient.Contexto
                {
                    AppKey      = string.IsNullOrEmpty(_appKey) ? "(consola)" : _appKey,
                    Empresa     = _ctx.Empresa(),
                    Servidor    = _ctx.ServidorActivo(),
                    BaseDatos   = _ctx.Empresa(),
                    UserId      = _ctx.UserIdReal(),
                    ModuleId    = _ctx.ModuloActivo(),
                    Language    = "python",
                    SelectedIds = _ctx.GetSelectedIds().ToArray(),
                    FilaActiva  = _ctx.GetFilaActiva(),
                };
                int timeoutMs = HostClient.TimeoutMsFromHeader(codigo);
                var sqlRunner = new CtxSqlRunner(_ctx);
                var erpRunner = new CtxErpRunner(_ctx);

                System.Threading.Tasks.Task.Run(() =>
                {
                    HostClient.Resultado r;
                    try { r = HostClient.EjecutarPython(codigo, hctx, timeoutMs: timeoutMs, sqlRunner: sqlRunner, erpRunner: erpRunner); }
                    catch (Exception ex) { r = new HostClient.Resultado { Exito = false, CodigoError = "CONSOLA_PYTHON_ERROR", MensajeError = ex.Message, Detalle = ex.StackTrace ?? "" }; }

                    if (IsDisposed) { _ejecutandoPython = false; return; }
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            string res = r.Exito ? "" : HostClient.FormatearError(r);
                            if (r.Exito && !string.IsNullOrEmpty(r.Valor)) Salida(r.Valor, Color.Gainsboro);
                            TerminarEjecucion("consola-python", sw, filasAntes, res);
                            _ejecutandoPython = false;
                        }));
                    }
                    catch { _ejecutandoPython = false; } // la consola se cerró mientras Python corría
                });
                return; // el resto (auditoría, Salida/Estado final) sigue en TerminarEjecucion
            }

            // C# (Roslyn, en proceso) y SQL crudo: rápidos, sin proceso externo — se quedan
            // síncronos como siempre.
            string resSync;
            string tipoAudit = "consola";
            if (HostClient.EsSql(codigo))
            {
                tipoAudit = "consola-sql";
                try { Salida(_ctx.EjecutarSql(codigo), Color.Gainsboro); resSync = ""; }
                catch (Exception ex) { resSync = ex.Message; }
            }
            else
            {
                string salida;
                resSync = ScriptRunner.EjecutarConValor(codigo, _ctx, out salida);
                if (resSync == "" && !string.IsNullOrEmpty(salida)) Salida(salida, Color.Gainsboro);
            }
            TerminarEjecucion(tipoAudit, sw, filasAntes, resSync);
        }

        // Cierre común de una ejecución (síncrona o al terminar la Task de Python): registra
        // auditoría y actualiza la salida/estado de la consola. Debe llamarse en el hilo de la UI.
        private void TerminarEjecucion(string tipoAudit, Stopwatch sw, int filasAntes, string res)
        {
            sw.Stop();
            _statusTiempo.Text = sw.ElapsedMilliseconds + " ms";

            string nombre = string.IsNullOrEmpty(_appKey) ? "(sin guardar)" : _appKey;
            try
            {
                Datos.RegistrarEjecucion(_ctx.Empresa(), _ctx.ModuloActivo(), _ctx.UserID, nombre,
                    tipoAudit, sw.ElapsedMilliseconds, _ctx.FilasAfectadas - filasAntes,
                    res == "" ? "OK" : "ERROR", res);
                if (!string.IsNullOrEmpty(_appKey)) { Datos.AgregarReciente(nombre); }
            }
            catch { }

            if (res == "")
            {
                Salida("Ejecución terminada correctamente.  (" + sw.ElapsedMilliseconds + " ms)", AppTheme.Success);
                _tabsOut.SelectedIndex = 0;
                Estado("OK", AppTheme.Success);
            }
            else
            {
                Errores(res);
                _tabsOut.SelectedIndex = 1;
                Estado("Error en ejecución", AppTheme.Error);
            }
            ActualizarContexto();
        }

        // =====================================================
        //   Salida
        // =====================================================
        private void Salida(string t, Color c)
        {
            _outSalida.SelectionColor = AppTheme.TextMuted;
            _outSalida.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
            _outSalida.SelectionColor = Legible(c);
            _outSalida.AppendText(t + "\r\n");
            _outSalida.ScrollToCaret();
        }
        private void Errores(string t)
        {
            _outErrores.SelectionColor = AppTheme.TextMuted;
            _outErrores.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "]\r\n");
            _outErrores.SelectionColor = AppTheme.Error;
            _outErrores.AppendText(t + "\r\n\r\n");
            _outErrores.ScrollToCaret();
            ActualizarContadorErrores();
        }
        private void ctxError(string t) { Errores(t); _tabsOut.SelectedIndex = 1; }

        // Actualiza el texto de la barra de estado con un color semántico.
        private void Estado(string texto, Color color)
        {
            if (_status == null) return;
            _status.Text = texto;
            _status.ForeColor = color;
        }

        // Asegura contraste de un color de salida sobre fondo claro (verde/azul/rojo legibles, gris→texto).
        private static Color Legible(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max - min < 24) return AppTheme.TextMain;          // gris neutro
            var col = c; int guard = 0;
            while (col.GetBrightness() > 0.45f && guard++ < 12) col = AppTheme.Darken(col, 0.15f);
            return col;
        }
    }

    // StatusStrip plano con borde superior suave (sin relieve clásico de Windows).
    internal class BordeSuperiorRenderer : ToolStripProfessionalRenderer
    {
        public BordeSuperiorRenderer() : base(new LightColorTable()) { }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(AppTheme.BgChrome)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            using (var p = new Pen(AppTheme.Border)) e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    // =====================================================
    //   Diálogo "Acerca de" (versión + notas de cambios)
    // =====================================================
    // Nativo y ligero: se construye solo al hacer clic, no toca el arranque de la consola.
    // El detalle largo (historial completo) vive en notas_version.html, que se abre en el
    // navegador del sistema para no cargar un control web dentro del proceso.
    internal sealed class AcercaForm : Form
    {
        public AcercaForm()
        {
            Text = "Acerca de BrosLMV";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, 248);
            BackColor = AppTheme.BgSurface;
            Font = AppTheme.FontMain;

            var pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(28, 26, 56, 56), BackColor = Color.Transparent };
            try { var sl = ResLogo(); if (sl != null) using (sl) pic.Image = Image.FromStream(sl); } catch { }

            var lblBrand = new Label { Text = "BrosLMV", Font = AppTheme.FontHeader, ForeColor = AppTheme.TextMain, AutoSize = true, Location = new Point(104, 30) };
            var lblVer   = new Label { Text = "Versión " + BrosConsola.Version, Font = AppTheme.FontTitle, ForeColor = AppTheme.Primary, AutoSize = true, Location = new Point(106, 62) };
            var lblBuild = new Label { Text = "Compilado: " + BrosConsola.FechaCompilacion(), Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted, AutoSize = true, Location = new Point(106, 84) };

            var lblDesc  = new Label
            {
                Text = "Consola de scripts para CONTPAQi Comercial (C# · Python · SQL).\n" +
                       "Para ver el detalle de cambios de cada versión, abre las notas.",
                Font = AppTheme.FontMain, ForeColor = AppTheme.TextMain, AutoSize = false,
                Bounds = new Rectangle(28, 120, 404, 44)
            };

            var btnNotas = new Button
            {
                Text = "Ver notas de versión", AutoSize = false, Bounds = new Rectangle(28, 196, 180, 30),
                FlatStyle = FlatStyle.Flat, BackColor = AppTheme.Primary, ForeColor = Color.White,
                Font = AppTheme.FontMain, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            btnNotas.FlatAppearance.BorderSize = 0;
            btnNotas.Click += (s, e) => BrosConsola.AbrirNotasVersion();

            var btnCerrar = new Button
            {
                Text = "Cerrar", AutoSize = false, Bounds = new Rectangle(352, 196, 80, 30),
                FlatStyle = FlatStyle.Flat, BackColor = AppTheme.BgChrome, ForeColor = AppTheme.TextMain,
                Font = AppTheme.FontMain, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            btnCerrar.FlatAppearance.BorderColor = AppTheme.Border;
            btnCerrar.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { pic, lblBrand, lblVer, lblBuild, lblDesc, btnNotas, btnCerrar });
            AcceptButton = btnCerrar;
            CancelButton = btnCerrar;
        }

        private static System.IO.Stream ResLogo()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in new[] { "logo_app.png", "logo_color.png", "logo.png", "logo_blanco.png" })
            {
                var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(name, StringComparison.OrdinalIgnoreCase));
                if (n != null) return asm.GetManifestResourceStream(n);
            }
            return null;
        }
    }

    // =====================================================
    //   Tema central (colores, tipografía e iconografía)
    // =====================================================
    public static class AppTheme
    {
        public static Color BgMain = Color.FromArgb(237, 242, 249);     // panel/fondo (tinte azul suave para dar profundidad)
        public static Color BgSurface = Color.FromArgb(255, 255, 255);  // #FFFFFF tarjetas/paneles
        public static Color BgSubtle = Color.FromArgb(238, 243, 250);   // gutter / fila alterna
        public static Color Border = Color.FromArgb(220, 227, 237);     // #DCE3ED borde
        public static Color BorderSoft = Color.FromArgb(232, 238, 246); // borde muy suave
        public static Color TextMain = Color.FromArgb(31, 41, 55);      // #1F2937 texto principal
        public static Color TextMuted = Color.FromArgb(102, 112, 133);  // #667085 texto secundario
        public static Color Primary = Color.FromArgb(37, 99, 235);      // #2563EB azul principal
        public static Color PrimaryHover = Color.FromArgb(29, 78, 216); // #1D4ED8 azul hover
        public static Color PrimarySelected = Color.FromArgb(234, 242, 255); // #EAF2FF selección suave
        public static Color PrimarySoft = Color.FromArgb(225, 235, 252);      // chip/acento azul tenue
        public static Color Success = Color.FromArgb(22, 163, 74);      // #16A34A verde ejecución
        public static Color SuccessHover = Color.FromArgb(21, 128, 61);
        public static Color Error = Color.FromArgb(220, 38, 38);        // #DC2626 rojo error
        public static Color Warning = Color.FromArgb(217, 119, 6);      // #D97706 amarillo aviso
        public static Color Hover = Color.FromArgb(225, 232, 243);      // hover (visible sobre el chrome tintado)
        public static Color BgChrome = Color.FromArgb(232, 238, 246);   // barra superior / estado (no blanco puro)

        public static Font FontMain, FontSmall, FontTitle, FontHeader, FontMono, FontIcon, FontIconSmall;

        static AppTheme()
        {
            string ui   = PickFont("Inter", "Segoe UI Variable Text", "Segoe UI", "Aptos");
            string mono = PickFont("Cascadia Code", "JetBrains Mono", "Consolas");
            string icon = PickFont("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
            FontMain      = new Font(ui, 9f);
            FontSmall     = new Font(ui, 8.25f);
            FontTitle     = new Font(ui, 9.75f, FontStyle.Bold);
            FontHeader    = new Font(ui, 13.5f, FontStyle.Bold);
            FontMono      = new Font(mono, 10.5f);
            FontIcon      = new Font(icon, 11f);
            FontIconSmall = new Font(icon, 9f);
        }

        // Devuelve la primera familia instalada; si ninguna existe, la última como respaldo seguro.
        private static string PickFont(params string[] names)
        {
            foreach (var n in names)
            {
                try { using (var f = new Font(n, 9f)) if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n; }
                catch { }
            }
            return names[names.Length - 1];
        }

        public static Color Darken(Color c, float amt = 0.08f)
            => Color.FromArgb(c.A, (int)(c.R * (1 - amt)), (int)(c.G * (1 - amt)), (int)(c.B * (1 - amt)));
    }

    // Glifos Segoe MDL2 / Fluent usados en la interfaz.
    internal static class Glyph
    {
        public const string Play     = "";  // Play
        public const string PlaySel  = "";  // Play (ejecutar selección)
        public const string Check    = "";  // CheckMark (verificar)
        public const string New      = "";  // Page (nuevo)
        public const string Open     = "";  // OpenFile (abrir)
        public const string Save     = "";  // Save (guardar)
        public const string SaveAs   = "";  // SaveAs (guardar como)
        public const string Copy     = "";  // Copy (duplicar)
        public const string History  = "";  // History (auditoría)
        public const string Refresh  = "";  // Refresh (actualizar)
        public const string Search   = "";  // Search (buscar)
        public const string Folder   = "";  // FolderHorizontal
        public const string Script   = "";  // Code (script)
        public const string Template = "";  // Document (plantilla)
        public const string Close    = "";  // ChromeClose (cerrar pestaña)
        public const string Add      = "";  // Add (+)
        public const string Lock     = "";  // Lock (solo lectura)
        public const string Clear    = "";  // Delete (limpiar salida)
        public const string FontIco  = "";  // FontSize
        public const string Info     = "";  // Info
        public const string Warn     = "";  // Warning
        public const string ErrorIco = "";  // ErrorBadge
        public static readonly string Down    = ((char)0xE70D).ToString(); // ChevronDown (buscar siguiente)
        public static readonly string Up      = ((char)0xE70E).ToString(); // ChevronUp (buscar anterior)
        public static readonly string Full    = ((char)0xE740).ToString(); // FullScreen
        public static readonly string Restore = ((char)0xE73F).ToString(); // BackToWindow
        public const string Dot      = "●"; // indicador sin guardar
    }

    // Fábrica de controles y utilidades de dibujo modernas (esquinas suaves, estados).
    internal static class ModernUI
    {
        public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); p.CloseFigure(); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Renderiza un glifo a un bitmap (para ImageList del árbol, etc.).
        public static Bitmap GlyphImage(string glyph, int size, Color color, float fontSize = 0)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var f = new Font(AppTheme.FontIcon.FontFamily, fontSize > 0 ? fontSize : size * 0.62f))
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }

    // Botón plano moderno: icono (glifo) + texto, esquinas suaves y estados hover/pressed/disabled.
    internal enum BtnKind { Ghost, Primary, Outline, Toolbar }
    internal class IconButton : Button
    {
        public string Glyph = "";
        public BtnKind Kind = BtnKind.Ghost;
        public Color Accent = Color.Empty;
        public int Radius = 6;
        public int PadX = 14;          // padding horizontal interno
        public int MinH = 32;          // alto mínimo
        private bool _hover, _down;

        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Font = AppTheme.FontMain;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _down = false; Invalidate(); };
            MouseDown  += (s, e) => { _down = true; Invalidate(); };
            MouseUp    += (s, e) => { _down = false; Invalidate(); };
        }

        // Medición DPI-correcta: la hace el motor de layout (TextRenderer sin gráfico usa
        // el DC de pantalla a la escala actual). Así nunca se encima ni se recorta el texto.
        public override Size GetPreferredSize(Size proposedSize)
        {
            int gap = (!string.IsNullOrEmpty(Glyph) && !string.IsNullOrEmpty(Text)) ? 7 : 0;
            Size ico = string.IsNullOrEmpty(Glyph) ? Size.Empty
                : TextRenderer.MeasureText(Glyph, AppTheme.FontIcon, Size.Empty, TextFormatFlags.NoPadding);
            Size txt = string.IsNullOrEmpty(Text) ? Size.Empty
                : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int w = PadX * 2 + ico.Width + gap + txt.Width;
            int h = Math.Max(MinH, Math.Max(ico.Height, txt.Height) + 12);
            return new Size(w, h);
        }

        // Color de fondo real bajo el botón (el del contenedor), para que NUNCA queden
        // residuos de texto: limpiamos el área completa en cada repintado.
        private Color FondoBase()
        {
            if (BackColor != Color.Transparent) return BackColor;
            var p = Parent;
            while (p != null) { if (p.BackColor != Color.Transparent) return p.BackColor; p = p.Parent; }
            return AppTheme.BgSurface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(FondoBase())) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // Re-limpiar por seguridad (algunos contenedores no invocan OnPaintBackground).
            using (var bb = new SolidBrush(FondoBase())) g.FillRectangle(bb, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, fg, border = Color.Empty;
            Color baseC = Accent == Color.Empty ? AppTheme.Primary : Accent;
            if (!Enabled)
            {
                fill = Kind == BtnKind.Primary ? AppTheme.Border : Color.Transparent;
                fg = AppTheme.TextMuted;
            }
            else if (Kind == BtnKind.Primary)
            {
                fill = _down ? AppTheme.Darken(baseC, 0.16f) : (_hover ? AppTheme.Darken(baseC) : baseC);
                fg = Color.White;
            }
            else if (Kind == BtnKind.Outline)
            {
                fill = _down ? AppTheme.PrimarySelected : (_hover ? AppTheme.Hover : AppTheme.BgSurface);
                fg = baseC; border = AppTheme.Border;
            }
            else if (Kind == BtnKind.Toolbar)
            {
                // Botón blanco con borde sobre la barra tintada: se lee claramente como botón.
                fill = _down ? AppTheme.PrimarySelected : (_hover ? AppTheme.PrimarySoft : AppTheme.BgSurface);
                fg = _hover || _down ? AppTheme.PrimaryHover : AppTheme.TextMain;
                border = _hover || _down ? AppTheme.Primary : AppTheme.Border;
            }
            else // Ghost
            {
                fill = _down ? AppTheme.Border : (_hover ? AppTheme.Hover : Color.Transparent);
                fg = AppTheme.TextMain;
            }

            using (var path = ModernUI.Round(r, Radius))
            {
                if (fill != Color.Transparent) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            // Medir icono + texto y centrar el grupo
            int gap = string.IsNullOrEmpty(Glyph) || string.IsNullOrEmpty(Text) ? 0 : 7;
            Size szIco = string.IsNullOrEmpty(Glyph) ? Size.Empty
                : TextRenderer.MeasureText(g, Glyph, AppTheme.FontIcon, Size.Empty, TextFormatFlags.NoPadding);
            Size szTxt = string.IsNullOrEmpty(Text) ? Size.Empty
                : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int totalW = szIco.Width + gap + szTxt.Width;
            int x = (Width - totalW) / 2;
            int midY = Height / 2;
            if (!string.IsNullOrEmpty(Glyph))
            {
                TextRenderer.DrawText(g, Glyph, AppTheme.FontIcon, new Rectangle(x, midY - szIco.Height / 2, szIco.Width, szIco.Height), fg, TextFormatFlags.NoPadding);
                x += szIco.Width + gap;
            }
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, midY - szTxt.Height / 2, szTxt.Width, szTxt.Height), fg, TextFormatFlags.NoPadding);
        }
    }

    public class LightColorTable : ProfessionalColorTable
    {
        public override Color ToolStripBorder => AppTheme.Border;
        public override Color ToolStripGradientBegin => AppTheme.BgSurface;
        public override Color ToolStripGradientEnd => AppTheme.BgSurface;
        public override Color ToolStripPanelGradientBegin => AppTheme.BgSurface;
        public override Color ToolStripPanelGradientEnd => AppTheme.BgSurface;
        public override Color MenuStripGradientBegin => AppTheme.BgSurface;
        public override Color MenuStripGradientEnd => AppTheme.BgSurface;
        public override Color ButtonSelectedHighlight => AppTheme.PrimarySelected;
        public override Color ButtonSelectedGradientBegin => AppTheme.PrimarySelected;
        public override Color ButtonSelectedGradientEnd => AppTheme.PrimarySelected;
        public override Color ButtonSelectedBorder => AppTheme.PrimarySelected;
        public override Color ButtonPressedHighlight => AppTheme.Primary;
        public override Color ButtonPressedGradientBegin => AppTheme.Primary;
        public override Color ButtonPressedGradientEnd => AppTheme.Primary;
        public override Color ButtonPressedBorder => AppTheme.Primary;
        public override Color MenuItemSelected => AppTheme.PrimarySelected;
        public override Color MenuItemSelectedGradientBegin => AppTheme.PrimarySelected;
        public override Color MenuItemSelectedGradientEnd => AppTheme.PrimarySelected;
        public override Color MenuItemBorder => AppTheme.PrimarySelected;
    }
}
