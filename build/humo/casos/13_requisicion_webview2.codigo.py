# lang: python
# job: safe-offline
# timeout: 60
# Humo #13: valida PLANTILLA_REQUISICION_WEBVIEW2_PYTHON.py de punta a punta -- MISMA
# logica que el archivo real (instalador/scripts/PLANTILLA_REQUISICION_WEBVIEW2_PYTHON.py),
# con un auto-click de JS inyectado al final para poder probarla sin un humano (la
# plantilla real que se distribuye NO tiene ese auto-click, requiere que el usuario llene
# el formulario y presione "Crear Requisicion").
from broslmv import ctx
import json

proveedores = ctx.query("""
    SELECT be.BusinessEntityID, be.OfficialName
    FROM orgBusinessEntity be
    INNER JOIN orgSupplier s ON s.BusinessEntityID = be.BusinessEntityID
    WHERE be.DeletedOn IS NULL
    ORDER BY be.OfficialName
""")
almacenes = ctx.query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName")
productos = ctx.query("""
    SELECT TOP 300 ProductID, ProductKey, ProductName, Unit
    FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID IS NOT NULL AND TaxTypeID > 0
    ORDER BY ProductName
""")

opciones_proveedor = "".join(
    '<option value="%d">%s</option>' % (p["BusinessEntityID"], p["OfficialName"]) for p in proveedores
)
opciones_almacen = "".join(
    '<option value="%d">%s</option>' % (a["DepotID"], a["DepotName"]) for a in almacenes
)
productos_json = json.dumps([
    {"id": p["ProductID"], "key": p["ProductKey"], "nombre": p["ProductName"], "unidad": p["Unit"]}
    for p in productos
])

html = """
<!DOCTYPE html><html><head><meta charset="utf-8"></head><body>
<select id="proveedor">__OPCIONES_PROVEEDOR__</select>
<select id="almacen">__OPCIONES_ALMACEN__</select>
<input id="comentarios" value="caso 13 del arnes de humo">
<select id="producto">__OPCIONES_PRODUCTO__</select>
<input id="cantidad" type="number" value="1">
<script>
  const PRODUCTOS = __PRODUCTOS_JSON__;
  let partidas = [];
  function porId(id) { return PRODUCTOS.find(p => p.id == id); }
  function agregarPartida() {
    const id = document.getElementById('producto').value;
    const cant = parseFloat(document.getElementById('cantidad').value) || 0;
    if (!id || cant <= 0) return;
    const p = porId(id);
    partidas.push({ id: p.id, key: p.key, nombre: p.nombre, unidad: p.unidad, cantidad: cant });
  }
  function crear() {
    window.chrome.webview.postMessage(JSON.stringify({
      businessEntityId: parseInt(document.getElementById('proveedor').value),
      depotId: parseInt(document.getElementById('almacen').value),
      comentarios: document.getElementById('comentarios').value,
      partidas: partidas
    }));
  }
  document.getElementById('cantidad').value = 5;
  agregarPartida();
  crear();
</script>
</body></html>
"""

html = (html
    .replace("__OPCIONES_PROVEEDOR__", opciones_proveedor)
    .replace("__OPCIONES_ALMACEN__", opciones_almacen)
    .replace("__OPCIONES_PRODUCTO__", "".join('<option value="%d">%s</option>' % (p["ProductID"], p["ProductKey"]) for p in productos))
    .replace("__PRODUCTOS_JSON__", productos_json))

r = ctx.show_html_formulario(html, title="Humo 13 - Requisicion WebView2", width=760, height=680, timeout_ms=15000)

if r.get("cancelado") or not r.get("submitted"):
    raise ValueError("Se esperaba submitted=True, se recibio: " + str(r))

be = r.get("businessEntityId")
depot = r.get("depotId")
partidas = r.get("partidas") or []
if not be or not depot or not partidas:
    raise ValueError("Faltan datos: " + str(r))

doc = ctx.erp.NuevoDocumento(1040, depot, be)
ctx.execute("UPDATE docDocument SET DepotIDFrom=0, Comments=N'" + r["comentarios"].replace("'", "''") + "' WHERE DocumentID=" + str(doc))
for p in partidas:
    ctx.erp.AgregarArticulo(doc, int(p["id"]), float(p["cantidad"]))
ctx.erp.RecalcCompleto(doc)
ctx.erp.Save(doc)

result = "Requisicion (WebView2) creada: doc=" + str(doc)
