# lang: python
# job: safe-offline
# timeout: 60
# Humo #18: valida PLANTILLA_ORDEN_COMPRA_WEBVIEW2_PYTHON.py de punta a punta -- misma
# logica que el archivo real, con auto-click de JS inyectado para poder probarla sin un
# humano.
from broslmv import ctx
import json

proveedores = ctx.query("""
    SELECT be.BusinessEntityID, be.OfficialName, ISNULL(m.OfficialNumber,'') AS RFC
    FROM orgBusinessEntity be
    INNER JOIN orgSupplier s ON s.BusinessEntityID = be.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
    WHERE be.DeletedOn IS NULL
    ORDER BY be.OfficialName
""")
almacenes = ctx.query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName")
monedas = ctx.query("SELECT CurrencyID, IntlSymbol, Currency FROM vwLBSCurrencyList ORDER BY CurrencyID")
condiciones = ctx.query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID")
productos = ctx.query("""
    SELECT TOP 300 ProductID, ProductKey, ProductName, Unit
    FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID IS NOT NULL AND TaxTypeID > 0
    ORDER BY ProductName
""")

proveedores_json = json.dumps([{"id": p["BusinessEntityID"], "nombre": p["OfficialName"], "rfc": p["RFC"] or ""} for p in proveedores])
opciones_almacen = "".join('<option value="%d">%s</option>' % (a["DepotID"], a["DepotName"]) for a in almacenes)
opciones_moneda = "".join('<option value="%d">%s</option>' % (m["CurrencyID"], m["Currency"]) for m in monedas)
opciones_condicion = "".join('<option value="%d">%s</option>' % (c["PaymentTermID"], c["PaymentTermName"]) for c in condiciones)
productos_json = json.dumps([{"id": p["ProductID"], "key": p["ProductKey"], "nombre": p["ProductName"], "unidad": p["Unit"]} for p in productos])

html = """
<!DOCTYPE html><html><head><meta charset="utf-8"></head><body>
<select id="proveedor"></select>
<select id="almacen">__OPCIONES_ALMACEN__</select>
<select id="moneda">__OPCIONES_MONEDA__</select>
<select id="condicion">__OPCIONES_CONDICION__</select>
<input id="entrega" type="date" value="2026-08-15">
<input id="comentarios" value="caso 18 del arnes de humo">
<select id="producto"></select>
<input id="cantidad" type="number" value="1">
<input id="precio" type="number" value="0">
<script>
  const PROVEEDORES = __PROVEEDORES_JSON__;
  const PRODUCTOS = __PRODUCTOS_JSON__;
  let partidas = [];
  const selProv = document.getElementById('proveedor');
  PROVEEDORES.forEach(p => selProv.add(new Option(p.nombre, p.id)));
  const selProd = document.getElementById('producto');
  PRODUCTOS.forEach(p => selProd.add(new Option(p.key, p.id)));
  function porId(id) { return PRODUCTOS.find(p => p.id == id); }
  function agregarPartida() {
    const id = selProd.value;
    const cant = parseFloat(document.getElementById('cantidad').value) || 0;
    const precio = parseFloat(document.getElementById('precio').value) || 0;
    const p = porId(id);
    partidas.push({ id: p.id, key: p.key, nombre: p.nombre, unidad: p.unidad, cantidad: cant, precio: precio });
  }
  function crear() {
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
  document.getElementById('cantidad').value = 3;
  document.getElementById('precio').value = 150;
  agregarPartida();
  crear();
</script>
</body></html>
"""

html = (html
    .replace("__OPCIONES_ALMACEN__", opciones_almacen)
    .replace("__OPCIONES_MONEDA__", opciones_moneda)
    .replace("__OPCIONES_CONDICION__", opciones_condicion)
    .replace("__PROVEEDORES_JSON__", proveedores_json)
    .replace("__PRODUCTOS_JSON__", productos_json))

r = ctx.show_html_formulario(html, title="Humo 18 - OC webview2 python", width=760, height=680, timeout_ms=15000)

if not r.get("submitted"):
    raise ValueError("No submitted: " + str(r))

be = r.get("businessEntityId")
depot = r.get("depotId")
moneda = r.get("monedaId") or 3
condicion = r.get("condicionId") or 0
entrega = r.get("entrega") or ""
partidas = r.get("partidas") or []
comentarios = r.get("comentarios") or ""

if not be or not depot or not partidas:
    raise ValueError("Faltan datos: " + str(r))

doc = ctx.erp.NuevoDocumento(183, depot, be)
fecha_sql = "'" + entrega.replace("-", "") + "'" if entrega else "GETDATE()"
ctx.execute(
    "UPDATE docDocument SET DepotIDFrom=0, CurrencyID=" + str(moneda) +
    ", PaymentTermID=" + str(condicion) +
    ", DateDelivery=" + fecha_sql + ", DateDocDelivery=" + fecha_sql +
    ", Comments=N'" + comentarios.replace("'", "''") + "' WHERE DocumentID=" + str(doc)
)
for p in partidas:
    ctx.erp.AgregarArticulo(doc, int(p["id"]), float(p["cantidad"]), float(p["precio"]), -1)
ctx.execute("UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID=" + str(doc) + " AND DeletedOn IS NULL")
ctx.erp.RecalcCompleto(doc)
ctx.erp.AffectStockNEW(doc)
ctx.erp.Save(doc)
try:
    ctx.erp.UpdateDocumentPaidInfo(doc)
except Exception:
    pass

result = "Orden de compra (WebView2) creada: doc=" + str(doc)
