# lang: python
# PLANTILLA: Requisición de Compra — versión WebView2 (Python, HTML/CSS real)
#
# Qué hace: EXACTAMENTE lo mismo que PLANTILLA_REQUISICION_FORMS_CSHARP.ctx (la versión
# "Forms") -- crea una Solicitud de Compra real en Comercial -- pero la ventana es una
# página HTML/CSS/JS de verdad (WebView2), no controles de Windows Forms. Mismas opciones
# que la versión Forms (proveedor con RFC, almacén, moneda, condición de pago,
# comentarios, partidas) -- elige la que prefieras, el RESULTADO en Comercial es idéntico.
#
# Cómo funciona el envío de datos: ctx.show_html() normal es de UNA SOLA VÍA -- puede
# mostrar una página, pero la página no puede mandarle nada de vuelta al script. Por eso
# existe ctx.show_html_formulario() (v2.54.0): la página, al presionar "Crear
# Requisición", llama
#     window.chrome.webview.postMessage(JSON.stringify({...datos...}))
# y ESO es lo que ctx.show_html_formulario() regresa como diccionario Python.
#
# timeout: 1800 -- OBLIGATORIO: un formulario real puede tardar varios minutos en llenarse,
# el timeout normal del script (2 min) es para scripts que NO esperan a un humano.
# timeout: 1800

from broslmv import ctx
import json

# ---- Datos iniciales: mismos catálogos que la versión Forms, insertados directo en el
# HTML (como JS) porque la página no puede volver a preguntarle a Python a medio llenado.
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

proveedores_json = json.dumps([
    {"id": p["BusinessEntityID"], "nombre": p["OfficialName"], "rfc": p["RFC"] or "(sin RFC)"}
    for p in proveedores
])
opciones_almacen = "".join('<option value="%d">%s</option>' % (a["DepotID"], a["DepotName"]) for a in almacenes)
opciones_moneda = "".join(
    '<option value="%d"%s>%s - %s</option>' % (m["CurrencyID"], ' selected' if m["CurrencyID"] == 3 else '', m["IntlSymbol"], m["Currency"])
    for m in monedas
)
opciones_condicion = "".join('<option value="%d">%s</option>' % (c["PaymentTermID"], c["PaymentTermName"]) for c in condiciones)
productos_json = json.dumps([
    {"id": p["ProductID"], "key": p["ProductKey"], "nombre": p["ProductName"], "unidad": p["Unit"]}
    for p in productos
])

html = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<style>
  :root { --primary: #2563EB; --border: #DCE3ED; --bg: #EDF2F9; --text: #1F2937; --muted: #667085; }
  * { box-sizing: border-box; }
  body { font-family: "Segoe UI", sans-serif; background: var(--bg); color: var(--text); margin: 0; padding: 20px; }
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
  <h1>Nueva Requisición de Compra</h1>
  <div class="sub">Versión WebView2 -- mismo resultado que la versión Forms (C#), distinta interfaz.</div>

  <div class="card">
    <div class="row">
      <div>
        <label>Proveedor</label>
        <select id="proveedor" onchange="mostrarRFC()"></select>
      </div>
      <div>
        <label>RFC</label>
        <input id="rfc" readonly>
      </div>
    </div>
    <div class="row">
      <div>
        <label>Almacén</label>
        <select id="almacen">__OPCIONES_ALMACEN__</select>
      </div>
      <div>
        <label>Moneda</label>
        <select id="moneda">__OPCIONES_MONEDA__</select>
      </div>
      <div>
        <label>Condición de pago</label>
        <select id="condicion">__OPCIONES_CONDICION__</select>
      </div>
    </div>
    <label>Comentarios</label>
    <input id="comentarios" placeholder="Opcional">
  </div>

  <div class="card">
    <div class="row">
      <div>
        <label>Producto</label>
        <select id="producto">__OPCIONES_PRODUCTO__</select>
      </div>
      <div style="flex: 0 0 100px">
        <label>Cantidad</label>
        <input id="cantidad" type="number" value="1" min="0.01" step="0.01">
      </div>
      <div style="flex: 0 0 auto; display:flex; align-items:flex-end; padding-bottom: 12px">
        <button type="button" onclick="agregarPartida()">Agregar</button>
      </div>
    </div>
    <table>
      <thead><tr><th>Clave</th><th>Descripción</th><th>Unidad</th><th>Cantidad</th><th></th></tr></thead>
      <tbody id="partidas"></tbody>
    </table>
    <div id="vacio" class="vacio">No hay partidas agregadas</div>
  </div>

  <div class="toolbar">
    <button type="button" onclick="cancelar()">Cancelar</button>
    <button type="button" class="primary" onclick="crear()">Crear Requisición</button>
  </div>

<script>
  const PROVEEDORES = __PROVEEDORES_JSON__;
  const PRODUCTOS = __PRODUCTOS_JSON__;
  let partidas = [];

  const selProv = document.getElementById('proveedor');
  PROVEEDORES.forEach(p => selProv.add(new Option(p.nombre, p.id)));
  function mostrarRFC() {
    const p = PROVEEDORES.find(x => x.id == selProv.value);
    document.getElementById('rfc').value = p ? p.rfc : '';
  }
  mostrarRFC();

  function porId(id) { return PRODUCTOS.find(p => p.id == id); }

  function agregarPartida() {
    const id = document.getElementById('producto').value;
    const cant = parseFloat(document.getElementById('cantidad').value) || 0;
    if (!id || cant <= 0) return;
    const p = porId(id);
    const existente = partidas.find(x => x.id == id);
    if (existente) existente.cantidad += cant;
    else partidas.push({ id: p.id, key: p.key, nombre: p.nombre, unidad: p.unidad, cantidad: cant });
    render();
  }

  function quitarPartida(i) { partidas.splice(i, 1); render(); }

  function render() {
    const tbody = document.getElementById('partidas');
    tbody.innerHTML = partidas.map((p, i) =>
      `<tr><td>${p.key}</td><td>${p.nombre}</td><td>${p.unidad}</td><td>${p.cantidad}</td>` +
      `<td><button type="button" onclick="quitarPartida(${i})">Quitar</button></td></tr>`
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
      comentarios: document.getElementById('comentarios').value,
      partidas: partidas
    }));
  }
</script>
</body>
</html>
"""

html = (html
    .replace("__OPCIONES_ALMACEN__", opciones_almacen)
    .replace("__OPCIONES_MONEDA__", opciones_moneda)
    .replace("__OPCIONES_CONDICION__", opciones_condicion)
    .replace("__OPCIONES_PRODUCTO__", "".join('<option value="%d">%s</option>' % (p["ProductID"], p["ProductKey"] + " - " + p["ProductName"]) for p in productos))
    .replace("__PROVEEDORES_JSON__", proveedores_json)
    .replace("__PRODUCTOS_JSON__", productos_json))

r = ctx.show_html_formulario(html, title="Nueva Requisición de Compra (WebView2)", width=820, height=720)

if r.get("cancelado") or not r.get("submitted"):
    result = "Cancelado, no se creó ningún documento."
else:
    be = r.get("businessEntityId")
    depot = r.get("depotId")
    moneda = r.get("monedaId") or 3
    condicion = r.get("condicionId") or 0
    partidas = r.get("partidas") or []
    comentarios = r.get("comentarios") or ""

    if not be or not depot or not partidas:
        result = "ERROR: faltan datos (proveedor/almacén/partidas)."
    else:
        # Mismo patrón canónico que la versión Forms (MANUAL.md 7.1) y el mismo módulo
        # (1040 = Solicitud de Compra) -- el resultado en Comercial es indistinguible de
        # una requisición creada con la versión C#.
        doc = ctx.erp.NuevoDocumento(1040, depot, be)
        ctx.execute(
            "UPDATE docDocument SET DepotIDFrom=0, CurrencyID=" + str(moneda) +
            ", PaymentTermID=" + str(condicion) +
            ", Comments=N'" + comentarios.replace("'", "''") + "' WHERE DocumentID=" + str(doc)
        )
        supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(be))
        for p in partidas:
            pid = int(p["id"])
            ctx.erp.AgregarArticulo(doc, pid, float(p["cantidad"]))
            # Registra al proveedor como fuente de este producto si todavía no lo era
            # (mismo patrón que la versión Forms).
            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id) + ") "
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id) + ", 0, " + str(moneda) + ", 0)"
            )
        ctx.erp.RecalcCompleto(doc)
        ctx.erp.Save(doc)
        # NO opcional aunque la Solicitud no afecte inventario -- RecalcCompleto no lo
        # calcula; sin esto el documento queda con "Estatus de entrega: No Aplica" en el
        # grid nativo (MANUAL.md §6.3).
        try:
            ctx.erp.UpdateStatusDelivery(doc)
        except Exception:
            pass

        result = "Requisición de compra " + str(doc) + " creada exitosamente."
