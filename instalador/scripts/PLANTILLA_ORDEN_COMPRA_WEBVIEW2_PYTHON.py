# lang: python
# PLANTILLA: Orden de Compra — versión WebView2 (Python, HTML/CSS real)
#
# Qué hace: EXACTAMENTE lo mismo que PLANTILLA_ORDEN_COMPRA_FORMS_CSHARP.ctx (la versión
# "Forms") -- crea una Orden de Compra real en Comercial -- pero la ventana es una página
# HTML/CSS/JS de verdad (WebView2), no controles de Windows Forms.
#
# Diferencia real frente a Requisición (ver PLANTILLA_REQUISICION_WEBVIEW2_PYTHON.py): una
# Orden de Compra SÍ compromete un PRECIO por partida (aquí sí hay campo de precio) y
# captura la FECHA DE ENTREGA esperada. También SÍ llama ctx.erp.AffectStockNEW (deja el
# kardex con Quantity=0 -- "compromete sin mover", no lo salta como si no aplicara) y
# ctx.erp.UpdateDocumentPaidInfo (regenera la agenda de pago con montos reales -- el Save
# nativo la deja con cache vieja, hay que corregirla a mano después). Ver MANUAL.md §7.5.
#
# Cómo funciona el envío de datos: igual que Requisición, usa ctx.show_html_formulario()
# (v2.54.0) -- ctx.show_html() normal es de una sola vía, no puede recibir datos de vuelta.
#
# timeout: 1800 -- OBLIGATORIO: un formulario real puede tardar varios minutos en llenarse.
# timeout: 1800

from broslmv import ctx
import json

# ---- Datos iniciales ----
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
opciones_moneda = "".join(
    '<option value="%d"%s>%s - %s</option>' % (m["CurrencyID"], ' selected' if m["CurrencyID"] == 3 else '', m["IntlSymbol"], m["Currency"])
    for m in monedas
)
opciones_condicion = "".join('<option value="%d">%s</option>' % (c["PaymentTermID"], c["PaymentTermName"]) for c in condiciones)
productos_json = json.dumps([{"id": p["ProductID"], "key": p["ProductKey"], "nombre": p["ProductName"], "unidad": p["Unit"]} for p in productos])

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
  <h1>Nueva Orden de Compra</h1>
  <div class="sub">Versión WebView2 -- mismo resultado que la versión Forms (C#), distinta interfaz.</div>

  <div class="card">
    <div class="row">
      <div><label>Proveedor</label><select id="proveedor" onchange="mostrarRFC()"></select></div>
      <div><label>RFC</label><input id="rfc" readonly></div>
    </div>
    <div class="row">
      <div><label>Almacén</label><select id="almacen">__OPCIONES_ALMACEN__</select></div>
      <div><label>Moneda</label><select id="moneda">__OPCIONES_MONEDA__</select></div>
      <div><label>Condición de pago</label><select id="condicion">__OPCIONES_CONDICION__</select></div>
      <div><label>Fecha de entrega</label><input id="entrega" type="date"></div>
    </div>
    <label>Comentarios</label>
    <input id="comentarios" placeholder="Opcional">
  </div>

  <div class="card">
    <div class="row">
      <div><label>Producto</label><select id="producto"></select></div>
      <div style="flex: 0 0 90px"><label>Cantidad</label><input id="cantidad" type="number" value="1" min="0.01" step="0.01"></div>
      <div style="flex: 0 0 110px"><label>Precio unit.</label><input id="precio" type="number" value="0" min="0" step="0.01"></div>
      <div style="flex: 0 0 auto; display:flex; align-items:flex-end; padding-bottom: 12px"><button type="button" onclick="agregarPartida()">Agregar</button></div>
    </div>
    <table>
      <thead><tr><th>Clave</th><th>Descripción</th><th>Cant.</th><th>Precio</th><th>Importe</th><th></th></tr></thead>
      <tbody id="partidas"></tbody>
    </table>
    <div id="vacio" class="vacio">No hay partidas agregadas</div>
  </div>

  <div class="toolbar">
    <button type="button" onclick="cancelar()">Cancelar</button>
    <button type="button" class="primary" onclick="crear()">Crear Orden de Compra</button>
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
      entrega: document.getElementById('entrega').value,
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
    .replace("__PROVEEDORES_JSON__", proveedores_json)
    .replace("__PRODUCTOS_JSON__", productos_json))

r = ctx.show_html_formulario(html, title="Nueva Orden de Compra (WebView2)", width=860, height=760)

if r.get("cancelado") or not r.get("submitted"):
    result = "Cancelado, no se creó ningún documento."
else:
    be = r.get("businessEntityId")
    depot = r.get("depotId")
    moneda = r.get("monedaId") or 3
    condicion = r.get("condicionId") or 0
    entrega = r.get("entrega") or ""
    partidas = r.get("partidas") or []
    comentarios = r.get("comentarios") or ""

    if not be or not depot or not partidas:
        result = "ERROR: faltan datos (proveedor/almacén/partidas)."
    else:
        # Mismo patrón que MANUAL.md §7.5 (ya probado en el caso 3 del arnés de humo, C#) --
        # a diferencia de Requisición, SÍ lleva precio/costo, TaxTypeID, AffectStockNEW y
        # UpdateDocumentPaidInfo.
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

        result = "Orden de compra " + str(doc) + " creada exitosamente."
