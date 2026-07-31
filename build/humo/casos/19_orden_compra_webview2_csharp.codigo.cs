// job: safe-offline
// timeout: 30
// lang: csharp
// PLANTILLA: Orden de Compra — versión WebView2 (C#, HTML/CSS real)
//
// Qué hace: EXACTAMENTE lo mismo que PLANTILLA_ORDEN_COMPRA_WEBVIEW2_PYTHON.py -- misma
// página HTML, mismas opciones (proveedor con RFC, almacén, moneda, condición de pago,
// fecha de entrega, precio por partida) -- pero corriendo en C#, usando
// ctx.ShowHtmlFormulario() (v2.57.0, mismo canal directo que ya usa la Requisición
// WebView2 C#).
//
// Diferencia real frente a Requisición (ver PLANTILLA_REQUISICION_WEBVIEW2_CSHARP.ctx):
// SÍ compromete un PRECIO por partida, captura FECHA DE ENTREGA, y SÍ llama
// ctx.erp.AffectStockNEW (deja el kardex con Quantity=0 -- "compromete sin mover") +
// ctx.erp.UpdateDocumentPaidInfo (regenera la agenda de pago con montos reales). Ver
// MANUAL.md §7.5.

using System;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════ DATOS INICIALES ═══════════════════
var proveedores = ctx.Query(@"
    SELECT be.BusinessEntityID, be.OfficialName, ISNULL(m.OfficialNumber,'') AS RFC
    FROM orgBusinessEntity be
    INNER JOIN orgSupplier s ON s.BusinessEntityID=be.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID=be.BusinessEntityID
    WHERE be.DeletedOn IS NULL
    ORDER BY be.OfficialName");
var almacenes = ctx.Query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName");
var monedas = ctx.Query("SELECT CurrencyID, IntlSymbol, Currency FROM vwLBSCurrencyList ORDER BY CurrencyID");
var condiciones = ctx.Query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID");

string JsonEsc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

var proveedoresJson = "[" + string.Join(",", proveedores.Select(p =>
    "{\"id\":" + p["BusinessEntityID"] + ",\"nombre\":\"" + JsonEsc(Convert.ToString(p["OfficialName"])) + "\",\"rfc\":\"" + JsonEsc(Convert.ToString(p["RFC"])) + "\"}")) + "]";
var opcionesAlmacen = string.Join("", almacenes.Select(a => "<option value=\"" + a["DepotID"] + "\">" + JsonEsc(Convert.ToString(a["DepotName"])) + "</option>"));
var opcionesMoneda = string.Join("", monedas.Select(m => "<option value=\"" + m["CurrencyID"] + "\"" + (Convert.ToInt32(m["CurrencyID"]) == 3 ? " selected" : "") + ">" + JsonEsc(Convert.ToString(m["IntlSymbol"])) + " - " + JsonEsc(Convert.ToString(m["Currency"])) + "</option>"));
var opcionesCondicion = string.Join("", condiciones.Select(c => "<option value=\"" + c["PaymentTermID"] + "\">" + JsonEsc(Convert.ToString(c["PaymentTermName"])) + "</option>"));

// Productos se mandan al JS como catálogo completo (igual que la versión Python) --
// el <select> se llena en JS, no en el HTML directo, para poder reusar el mismo array
// tanto en el combo como al armar cada partida.
var productos = ctx.Query(@"
    SELECT TOP 300 ProductID, ProductKey, ProductName, Unit
    FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID IS NOT NULL AND TaxTypeID > 0
    ORDER BY ProductName");
var productosJson = "[" + string.Join(",", productos.Select(p =>
    "{\"id\":" + p["ProductID"] + ",\"key\":\"" + JsonEsc(Convert.ToString(p["ProductKey"])) + "\",\"nombre\":\"" + JsonEsc(Convert.ToString(p["ProductName"])) + "\",\"unidad\":\"" + JsonEsc(Convert.ToString(p["Unit"])) + "\"}")) + "]";

// ═══════════════════ HTML (idéntico al de la versión Python) ═══════════════════
string html = @"
<!DOCTYPE html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<style>
  :root { --primary: #2563EB; --border: #DCE3ED; --bg: #EDF2F9; --text: #1F2937; --muted: #667085; }
  * { box-sizing: border-box; }
  body { font-family: ""Segoe UI"", sans-serif; background: var(--bg); color: var(--text); margin: 0; padding: 20px; }
  h1 { font-size: 18px; margin: 0 0 4px; }
  .sub { color: var(--muted); font-size: 13px; margin-bottom: 16px; }
  .card { background: #fff; border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 14px; }
  label { display: block; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
  select, input { width: 100%; padding: 7px 10px; border: 1px solid var(--border); border-radius: 6px; font-size: 13px; margin-bottom: 12px; }
  input[readonly] { background: #F1F4F9; color: var(--muted); }
  .row { display: flex; gap: 12px; }
  .row > div { flex: 1; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th { text-align: left; background: #F1F4F9; color: var(--muted); padding: 6px 8px; }
  td { padding: 6px 8px; border-bottom: 1px solid #F1F4F9; }
  button { border-radius: 6px; padding: 8px 16px; font-size: 13px; font-weight: 600; border: 1px solid var(--border); background: #fff; cursor: pointer; }
  button.primary { background: var(--primary); border-color: var(--primary); color: #fff; }
  .toolbar { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
  .vacio { color: var(--muted); text-align: center; padding: 20px; font-size: 13px; }
</style>
</head>
<body>
  <h1>Nueva Orden de Compra</h1>
  <div class=""sub"">Versión WebView2 (C#) -- mismo resultado que la versión Forms, distinta interfaz.</div>

  <div class=""card"">
    <div class=""row"">
      <div><label>Proveedor</label><select id=""proveedor"" onchange=""mostrarRFC()""></select></div>
      <div><label>RFC</label><input id=""rfc"" readonly></div>
    </div>
    <div class=""row"">
      <div><label>Almacén</label><select id=""almacen"">" + opcionesAlmacen + @"</select></div>
      <div><label>Moneda</label><select id=""moneda"">" + opcionesMoneda + @"</select></div>
      <div><label>Condición de pago</label><select id=""condicion"">" + opcionesCondicion + @"</select></div>
      <div><label>Fecha de entrega</label><input id=""entrega"" type=""date""></div>
    </div>
    <label>Comentarios</label>
    <input id=""comentarios"" placeholder=""Opcional"">
  </div>

  <div class=""card"">
    <div class=""row"">
      <div><label>Producto</label><select id=""producto""></select></div>
      <div style=""flex: 0 0 90px""><label>Cantidad</label><input id=""cantidad"" type=""number"" value=""1"" min=""0.01"" step=""0.01""></div>
      <div style=""flex: 0 0 110px""><label>Precio unit.</label><input id=""precio"" type=""number"" value=""0"" min=""0"" step=""0.01""></div>
      <div style=""flex: 0 0 auto; display:flex; align-items:flex-end; padding-bottom: 12px""><button type=""button"" onclick=""agregarPartida()"">Agregar</button></div>
    </div>
    <table>
      <thead><tr><th>Clave</th><th>Descripción</th><th>Cant.</th><th>Precio</th><th>Importe</th><th></th></tr></thead>
      <tbody id=""partidas""></tbody>
    </table>
    <div id=""vacio"" class=""vacio"">No hay partidas agregadas</div>
  </div>

  <div class=""toolbar"">
    <button type=""button"" onclick=""cancelar()"">Cancelar</button>
    <button type=""button"" class=""primary"" onclick=""crear()"">Crear Orden de Compra</button>
  </div>

<script>
  const PROVEEDORES = " + proveedoresJson + @";
  const PRODUCTOS = " + productosJson + @";
  let partidas = [];

  const selProv = document.getElementById('proveedor');
  PROVEEDORES.forEach(p => selProv.add(new Option(p.nombre, p.id)));
  function mostrarRFC() {
    const p = PROVEEDORES.find(x => x.id == selProv.value);
    document.getElementById('rfc').value = p ? p.rfc : '';
  }
  mostrarRFC();

  const selProd = document.getElementById('producto');
  PRODUCTOS.forEach(p => selProd.add(new Option(p.key + ' - ' + p.nombre, p.id)));

  function porId(id) { return PRODUCTOS.find(p => p.id == id); }

  function agregarPartida() {
    const id = selProd.value;
    const cant = parseFloat(document.getElementById('cantidad').value) || 0;
    const precio = parseFloat(document.getElementById('precio').value) || 0;
    if (!id || cant <= 0) return;
    const p = porId(id);
    partidas.push({ id: p.id, key: p.key, nombre: p.nombre, unidad: p.unidad, cantidad: cant, precio: precio });
    render();
  }

  function quitarPartida(i) { partidas.splice(i, 1); render(); }

  function render() {
    const tbody = document.getElementById('partidas');
    tbody.innerHTML = partidas.map((p, i) =>
      `<tr><td>${p.key}</td><td>${p.nombre}</td><td>${p.cantidad}</td><td>$${p.precio.toFixed(2)}</td><td>$${(p.cantidad*p.precio).toFixed(2)}</td>` +
      `<td><button type=""button"" onclick=""quitarPartida(${i})"">Quitar</button></td></tr>`
    ).join('');
    document.getElementById('vacio').style.display = partidas.length ? 'none' : 'block';
  }

  function cancelar() {
    window.chrome.webview.postMessage(JSON.stringify({ cancelado: true }));
  }

  function crear() {
    if (partidas.length === 0) { alert('Agrega al menos un producto.'); return; }
    window.chrome.webview.postMessage(JSON.stringify({
      businessEntityId: parseInt(selProv.value),
      depotId: parseInt(document.getElementById('almacen').value),
      monedaId: parseInt(document.getElementById('moneda').value),
      condicionId: parseInt(document.getElementById('condicion').value),
      entrega: document.getElementById('entrega').value,
      comentarios: document.getElementById('comentarios').value,
      partidas: partidas
    }));
  }

  // AUTO-CLICK inyectado solo para esta prueba:
  selProv.selectedIndex = 0; mostrarRFC();
  selProd.selectedIndex = 0;
  document.getElementById('cantidad').value = 2;
  document.getElementById('precio').value = 80;
  document.getElementById('entrega').value = '2026-08-20';
  agregarPartida();
  crear();
</script>
</body>
</html>
";

// ═══════════════════ MOSTRAR Y RECIBIR ═══════════════════
var r = ctx.ShowHtmlFormulario(html, "Nueva Orden de Compra (WebView2)", 860, 760);

if (r.ContainsKey("cancelado") || !(bool)r["submitted"])
{
    throw new Exception("Cancelado: " + string.Join(",", r.Keys));
}

int be = Convert.ToInt32(r["businessEntityId"]);
int depot = Convert.ToInt32(r["depotId"]);
int moneda = r.ContainsKey("monedaId") ? Convert.ToInt32(r["monedaId"]) : 3;
int condicion = r.ContainsKey("condicionId") ? Convert.ToInt32(r["condicionId"]) : 0;
string entrega = r.ContainsKey("entrega") ? Convert.ToString(r["entrega"]) : "";
string comentarios = r.ContainsKey("comentarios") ? Convert.ToString(r["comentarios"]) : "";
var partidasRecibidas = r["partidas"] as System.Collections.ArrayList;

if (be == 0 || depot == 0 || partidasRecibidas == null || partidasRecibidas.Count == 0)
{
    throw new Exception("Faltan datos.");
}

// Mismo patrón que MANUAL.md §7.5 (ya probado en el caso 3 del arnés de humo) -- a
// diferencia de Requisición, SÍ lleva precio/costo, TaxTypeID, AffectStockNEW y
// UpdateDocumentPaidInfo.
int doc = ctx.erp.NuevoDocumento(183, depot, be);
string fechaSql = string.IsNullOrEmpty(entrega) ? "GETDATE()" : "'" + entrega.Replace("-", "") + "'";
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, CurrencyID=" + moneda + ", PaymentTermID=" + condicion +
             ", DateDelivery=" + fechaSql + ", DateDocDelivery=" + fechaSql +
             ", Comments='" + comentarios.Replace("'", "''") + "' WHERE DocumentID=" + doc);

foreach (Dictionary<string, object> p in partidasRecibidas)
{
    int productId = Convert.ToInt32(p["id"]);
    double cantidad = Convert.ToDouble(p["cantidad"]);
    double precio = Convert.ToDouble(p["precio"]);
    ctx.erp.AgregarArticulo(doc, productId, cantidad, precio, -1);
}
ctx.NonQuery("UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID=" + doc + " AND DeletedOn IS NULL");

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
try { ctx.erp.UpdateDocumentPaidInfo(doc); } catch { }

return "doc=" + doc;
