# lang: python
# PLANTILLA: Orden de Compra — versión WebView2 (Python, HTML/CSS real)
#
# Qué hace: EXACTAMENTE lo mismo que PLANTILLA_ORDEN_COMPRA_FORMS_CSHARP.ctx (impuesto y
# descuento por partida, totales en vivo con IVA calculado, detalle de producto con
# existencia/listas de precio/precio por proveedor al doble clic) -- pero la ventana es
# una página HTML/CSS/JS real (WebView2), no controles de Windows Forms. Misma página que
# PLANTILLA_ORDEN_COMPRA_WEBVIEW2_CSHARP.ctx.
#
# ⚠️ CORREGIDO (2026-07-31): la primera versión de esta plantilla solo tenía cantidad y
# precio por partida -- sin impuesto, sin descuento, sin totales reales (siempre mostraba
# $0.00) y sin detalle de producto al doble clic. Todo el catálogo de datos que faltaba
# (impuestos con %, existencia por almacén, listas de precio, precios por proveedor) se
# manda precalculado al HTML en la carga inicial, porque ctx.show_html_formulario() solo
# espera UN mensaje de vuelta (no hay ida y vuelta con el servidor mientras el usuario
# captura) -- por eso el detalle de producto NO hace una consulta nueva al hacer doble
# clic, ya viene todo listo desde que se abrió la ventana.
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
# Serie/Folio: SOLO vista previa informativa. NuevoDocumento() ya resuelve y asigna
# Serie/Folio automáticamente en el mismo INSERT (ver Scripting.cs NuevoDocumento --
# llama GetFolioPrefix/GetNextFolio justo antes de crear el documento). MANUAL.md §6.6:
# "NuevoDocumento ya resuelve el folio automáticamente" -- por eso es de solo lectura y
# NO se manda de vuelta al crear el documento (evita chocar con otro doc creado mientras
# el formulario seguía abierto).
depot_default = almacenes[0]["DepotID"] if almacenes else 0
serie_default = ""
folio_default = ""
try:
    serie_default = ctx.erp.GetFolioPrefix(183, depot_default) or ""
    folio_default = ctx.erp.GetNextFolio(183, serie_default, depot_default) or ""
except Exception:
    pass
monedas = ctx.query("SELECT CurrencyID, IntlSymbol, Currency, Rate FROM vwLBSCurrencyList ORDER BY CurrencyID")
condiciones = ctx.query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID")
# Mismo catálogo de impuestos con % real que usa la versión Forms (vwLBSTaxPerc) -- sin
# esto el JS no puede calcular el IVA por partida, solo mostraría el nombre del impuesto.
impuestos = ctx.query("""
    SELECT t.TaxTypeID, t.TaxTypeName, ISNULL(tp.IVA_Perc,0) AS IVA_Perc
    FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID
    ORDER BY t.TaxTypeName
""")
productos = ctx.query("""
    SELECT TOP 300 ProductID, ProductKey, ProductName, ProductDescription, Unit, UnitSale, UnitBuy,
           CostPrice, PriceList, TaxTypeID, Category1, Category2, Category3, Category4
    FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID IS NOT NULL AND TaxTypeID > 0
    ORDER BY ProductName
""")
ids_productos = ",".join(str(p["ProductID"]) for p in productos) or "0"

# Existencia por almacén, listas de precio y precio por proveedor -- precalculados para
# TODOS los productos del catálogo (idéntico dato al que muestra la ventana "Detalle de
# producto" de la versión Forms), agrupados por ProductID en Python.
exist_rows = ctx.query("""
    SELECT k.ProductID, d.DepotName, SUM(k.Quantity) AS Qty
    FROM orgProductKardex k INNER JOIN orgDepot d ON d.DepotID = k.DepotID
    WHERE k.Cancelled = 0 AND d.DeletedOn IS NULL AND k.ProductID IN (%s)
    GROUP BY k.ProductID, d.DepotName ORDER BY d.DepotName
""" % ids_productos)
lista_rows = ctx.query("""
    SELECT ppl.ProductID, pl.PriceListName, ppl.Price
    FROM orgProductPriceList ppl INNER JOIN orgPriceList pl ON pl.PriceListID = ppl.PriceListID
    WHERE pl.DeletedOn IS NULL AND ppl.ProductID IN (%s)
    ORDER BY pl.PriceListName
""" % ids_productos)
prov_rows = ctx.query("""
    SELECT ps.ProductID, be.OfficialName, ps.CostPrice, ISNULL(ps.RefSupplier,'') AS RefSupplier
    FROM orgProductSupplier ps INNER JOIN orgSupplier s ON s.SupplierID = ps.SupplierID
    INNER JOIN orgBusinessEntity be ON be.BusinessEntityID = s.BusinessEntityID
    WHERE ps.ProductID IN (%s)
    ORDER BY ps.CostPrice
""" % ids_productos)


def agrupar_por_producto(rows):
    out = {}
    for r in rows:
        out.setdefault(r["ProductID"], []).append(r)
    return out


exist_por_producto = agrupar_por_producto(exist_rows)
lista_por_producto = agrupar_por_producto(lista_rows)
prov_por_producto = agrupar_por_producto(prov_rows)

proveedores_json = json.dumps([{"id": p["BusinessEntityID"], "nombre": p["OfficialName"], "rfc": p["RFC"] or ""} for p in proveedores])
opciones_almacen = "".join('<option value="%d">%s</option>' % (a["DepotID"], a["DepotName"]) for a in almacenes)
opciones_moneda = "".join(
    '<option value="%d"%s>%s - %s</option>' % (m["CurrencyID"], ' selected' if m["CurrencyID"] == 3 else '', m["IntlSymbol"], m["Currency"])
    for m in monedas
)
opciones_condicion = "".join('<option value="%d">%s</option>' % (c["PaymentTermID"], c["PaymentTermName"]) for c in condiciones)
moneda_rate_json = json.dumps({str(m["CurrencyID"]): float(m["Rate"] or 1) for m in monedas})


def palabra_moneda(nombre):
    cur = (nombre or "").upper()
    if "PESO" in cur:
        return "PESOS"
    if "DOLAR" in cur or "DÓLAR" in cur:
        return "DÓLARES"
    if "EURO" in cur:
        return "EUROS"
    return (cur + "S") if cur else "PESOS"


moneda_palabra_json = json.dumps({str(m["CurrencyID"]): palabra_moneda(m["Currency"]) for m in monedas})
impuestos_json = json.dumps([{"id": t["TaxTypeID"], "nombre": t["TaxTypeName"], "perc": float(t["IVA_Perc"] or 0)} for t in impuestos])

productos_json = json.dumps([{
    "id": p["ProductID"],
    "key": p["ProductKey"],
    "nombre": p["ProductName"],
    "unidad": p["Unit"],
    "descripcion": p["ProductDescription"] or "",
    "unitSale": p["UnitSale"] or "",
    "unitBuy": p["UnitBuy"] or "",
    "costo": float(p["CostPrice"] or 0),
    "precioLista": float(p["PriceList"] or 0),
    "taxTypeId": p["TaxTypeID"],
    "cat1": p["Category1"] or "",
    "cat2": p["Category2"] or "",
    "cat3": p["Category3"] or "",
    "cat4": p["Category4"] or "",
    "exist": [{"almacen": e["DepotName"], "qty": float(e["Qty"] or 0)} for e in exist_por_producto.get(p["ProductID"], [])],
    "listas": [{"lista": e["PriceListName"], "precio": float(e["Price"] or 0)} for e in lista_por_producto.get(p["ProductID"], [])],
    "proveedores": [{"proveedor": e["OfficialName"], "ref": e["RefSupplier"], "costo": float(e["CostPrice"] or 0)} for e in prov_por_producto.get(p["ProductID"], [])],
} for p in productos])

html = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<style>
  :root { --primary: #2563EB; --border: #DCE3ED; --bg: #EDF2F9; --text: #1F2937; --muted: #667085; --danger: #EF4444; }
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
  tr.partida { cursor: pointer; }
  tr.partida:hover { background: #F8FAFF; }
  button { border-radius: 6px; padding: 8px 16px; font-size: 13px; font-weight: 600; border: 1px solid var(--border); background: #fff; cursor: pointer; }
  button.primary { background: var(--primary); border-color: var(--primary); color: #fff; }
  .toolbar { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
  .vacio { color: var(--muted); text-align: center; padding: 20px; font-size: 13px; }
  .hint { color: var(--muted); font-size: 11px; margin-top: 6px; }
  .totales { display: flex; justify-content: flex-end; gap: 28px; align-items: baseline; }
  .totales div { text-align: right; }
  .totales .lbl { font-size: 12px; color: var(--muted); }
  .totales .val { font-size: 15px; font-weight: 600; }
  .totales .val.desc { color: var(--danger); }
  .totales .total .val { font-size: 22px; color: var(--primary); }
  .son { text-align: right; font-size: 11px; font-style: italic; color: var(--muted); margin-top: 4px; }
  .overlay { display: none; position: fixed; inset: 0; background: rgba(15,23,42,.45); align-items: center; justify-content: center; z-index: 50; }
  .overlay.abierto { display: flex; }
  .modal { background: #fff; border-radius: 10px; width: 640px; max-height: 85vh; overflow-y: auto; padding: 18px 20px; }
  .modal h2 { font-size: 15px; margin: 0 0 12px; }
  .modal h3 { font-size: 12px; color: var(--muted); margin: 14px 0 6px; text-transform: uppercase; letter-spacing: .03em; }
  .modal .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 6px 16px; font-size: 13px; }
  .modal .grid2 b { color: var(--muted); font-weight: 400; }
  .modal table { margin-top: 4px; }
  .modal .cerrar { margin-top: 16px; text-align: right; }
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
      <div><label>Moneda</label><select id="moneda" onchange="render()">__OPCIONES_MONEDA__</select></div>
      <div><label>Condición de pago</label><select id="condicion">__OPCIONES_CONDICION__</select></div>
      <div><label>Fecha de entrega</label><input id="entrega" type="date"></div>
    </div>
    <div class="row">
      <div><label>Fecha</label><input id="fecha" type="date"></div>
      <div><label>Serie</label><input id="serie" value="__SERIE_DEFAULT__" readonly></div>
      <div><label>Folio</label><input id="folio" value="__FOLIO_DEFAULT__" readonly></div>
    </div>
    <div class="hint">(Serie/Folio: vista previa -- CONTPAQi los asigna en automático al crear el documento)</div>
    <label>Comentarios</label>
    <input id="comentarios" placeholder="Opcional">
  </div>

  <div class="card">
    <div class="row">
      <div style="flex: 2"><label>Producto</label><select id="producto" onchange="preseleccionarImpuesto()"></select></div>
      <div style="flex: 0 0 85px"><label>Cantidad</label><input id="cantidad" type="number" value="1" min="0.01" step="0.01"></div>
      <div style="flex: 0 0 100px"><label>Precio unit.</label><input id="precio" type="number" value="0" min="0" step="0.01"></div>
      <div style="flex: 0 0 150px"><label>Impuesto</label><select id="impuesto"></select></div>
      <div style="flex: 0 0 90px"><label>Descuento %</label><input id="descuento" type="number" value="0" min="0" max="100" step="0.01"></div>
      <div style="flex: 0 0 auto; display:flex; align-items:flex-end; padding-bottom: 12px"><button type="button" class="primary" onclick="agregarPartida()">+ Agregar</button></div>
    </div>
    <table>
      <thead><tr><th>Clave</th><th>Descripción</th><th>Cant.</th><th>Precio</th><th>Impuesto</th><th>Desc. %</th><th>Importe</th><th></th></tr></thead>
      <tbody id="partidas"></tbody>
    </table>
    <div id="vacio" class="vacio">No hay partidas agregadas</div>
    <div class="hint">(doble clic en una partida para ver el detalle del producto)</div>
  </div>

  <div class="card">
    <div class="totales">
      <div><div class="lbl">Subtotal</div><div class="val" id="tSubtotal">$0.00</div></div>
      <div><div class="lbl">Descuento</div><div class="val desc" id="tDescuento">-$0.00</div></div>
      <div><div class="lbl">Impuestos</div><div class="val" id="tImpuestos">$0.00</div></div>
      <div class="total"><div class="lbl">TOTAL</div><div class="val" id="tTotal">$0.00</div></div>
    </div>
    <div class="son" id="tSon">SON: CERO PESOS 00/100 M.N.</div>
  </div>

  <div class="toolbar">
    <button type="button" onclick="cancelar()">Cancelar</button>
    <button type="button" class="primary" onclick="crear()">Crear Orden de Compra</button>
  </div>

  <div class="overlay" id="overlay" onclick="if(event.target===this) cerrarDetalle()">
    <div class="modal" id="modalDetalle"></div>
  </div>

<script>
  const PROVEEDORES = __PROVEEDORES_JSON__;
  const PRODUCTOS = __PRODUCTOS_JSON__;
  const IMPUESTOS = __IMPUESTOS_JSON__;
  const MONEDA_RATE = __MONEDA_RATE_JSON__;
  const MONEDA_PALABRA = __MONEDA_PALABRA_JSON__;
  let partidas = [];

  const selProv = document.getElementById('proveedor');
  PROVEEDORES.forEach(p => selProv.add(new Option(p.nombre, p.id)));
  function mostrarRFC() {
    const p = PROVEEDORES.find(x => x.id == selProv.value);
    document.getElementById('rfc').value = p ? p.rfc : '';
  }
  mostrarRFC();
  document.getElementById('fecha').value = new Date().toISOString().slice(0, 10);

  const selProd = document.getElementById('producto');
  PRODUCTOS.forEach(p => selProd.add(new Option(p.key + ' - ' + p.nombre, p.id)));

  const selImp = document.getElementById('impuesto');
  IMPUESTOS.forEach(t => selImp.add(new Option(t.nombre, t.id)));

  function porId(id) { return PRODUCTOS.find(p => p.id == id); }
  function impPorId(id) { return IMPUESTOS.find(t => t.id == id); }

  function preseleccionarImpuesto() {
    const p = porId(selProd.value);
    if (p) selImp.value = p.taxTypeId;
  }
  preseleccionarImpuesto();

  function agregarPartida() {
    const id = selProd.value;
    const cant = parseFloat(document.getElementById('cantidad').value) || 0;
    const precio = parseFloat(document.getElementById('precio').value) || 0;
    const descuento = (parseFloat(document.getElementById('descuento').value) || 0) / 100;
    const taxTypeId = parseInt(selImp.value);
    if (!id || cant <= 0) return;
    const p = porId(id);
    const t = impPorId(taxTypeId);
    partidas.push({
      id: p.id, key: p.key, nombre: p.nombre, unidad: p.unidad,
      cantidad: cant, precio: precio,
      taxTypeId: taxTypeId, taxLabel: t ? t.nombre : '', taxPerc: t ? t.perc : 0,
      descuentoPerc: descuento
    });
    render();
  }

  function quitarPartida(i, ev) { ev.stopPropagation(); partidas.splice(i, 1); render(); }

  // ---- Número a letras (mismo algoritmo que la versión Forms) ----
  const UNIDADES = ['', 'UNO', 'DOS', 'TRES', 'CUATRO', 'CINCO', 'SEIS', 'SIETE', 'OCHO', 'NUEVE', 'DIEZ',
    'ONCE', 'DOCE', 'TRECE', 'CATORCE', 'QUINCE', 'DIECISÉIS', 'DIECISIETE', 'DIECIOCHO', 'DIECINUEVE', 'VEINTE'];
  const DECENAS = ['', '', 'VEINTE', 'TREINTA', 'CUARENTA', 'CINCUENTA', 'SESENTA', 'SETENTA', 'OCHENTA', 'NOVENTA'];
  const VEINTI = ['VEINTIUNO', 'VEINTIDÓS', 'VEINTITRÉS', 'VEINTICUATRO', 'VEINTICINCO', 'VEINTISÉIS', 'VEINTISIETE', 'VEINTIOCHO', 'VEINTINUEVE'];
  const CENTENAS = ['', 'CIENTO', 'DOSCIENTOS', 'TRESCIENTOS', 'CUATROCIENTOS', 'QUINIENTOS', 'SEISCIENTOS', 'SETECIENTOS', 'OCHOCIENTOS', 'NOVECIENTOS'];
  function centenas_(n) {
    if (n === 0) return '';
    if (n === 100) return 'CIEN';
    let s = '';
    const c = Math.floor(n / 100), r = n % 100;
    if (c > 0) s += CENTENAS[c] + ' ';
    if (r > 0) {
      if (r <= 20) s += UNIDADES[r];
      else {
        const d = Math.floor(r / 10), u = r % 10;
        if (d === 2 && u > 0) s += VEINTI[u - 1];
        else { s += DECENAS[d]; if (u > 0) s += ' Y ' + UNIDADES[u]; }
      }
    }
    return s.trim();
  }
  function enLetras(n) {
    if (n === 0) return 'CERO';
    let resultado = '';
    const millones = Math.floor(n / 1000000); n %= 1000000;
    const miles = Math.floor(n / 1000); n %= 1000;
    const resto = n;
    if (millones > 0) resultado += (millones === 1 ? 'UN MILLÓN ' : centenas_(millones) + ' MILLONES ');
    if (miles > 0) resultado += (miles === 1 ? 'MIL ' : centenas_(miles) + ' MIL ');
    if (resto > 0) resultado += centenas_(resto);
    resultado = resultado.trim();
    if (resultado.endsWith('UNO')) resultado = resultado.slice(0, -3) + 'UN';
    return resultado;
  }
  function numeroALetras(valor, moneda) {
    if (valor < 0) valor = 0;
    const entero = Math.floor(valor);
    const centavos = Math.round((valor - entero) * 100);
    return enLetras(entero) + ' ' + moneda + ' ' + String(centavos).padStart(2, '0') + '/100';
  }

  function render() {
    const tbody = document.getElementById('partidas');
    tbody.innerHTML = partidas.map((p, i) =>
      `<tr class="partida" onclick="verDetalle(${p.id})"><td>${p.key}</td><td>${p.nombre}</td><td>${p.cantidad}</td><td>$${p.precio.toFixed(2)}</td>` +
      `<td>${p.taxLabel} (${(p.taxPerc * 100).toFixed(0)}%)</td><td>${(p.descuentoPerc * 100).toFixed(2)}%</td><td>$${(p.cantidad * p.precio).toFixed(2)}</td>` +
      `<td><button type="button" onclick="quitarPartida(${i}, event)">Quitar</button></td></tr>`
    ).join('');
    document.getElementById('vacio').style.display = partidas.length ? 'none' : 'block';

    let subtotal = 0, descuentoTotal = 0, impuestosTotal = 0;
    partidas.forEach(p => {
      const importe = p.cantidad * p.precio;
      const descMonto = importe * p.descuentoPerc;
      const neto = importe - descMonto;
      const impMonto = neto * p.taxPerc;
      subtotal += importe; descuentoTotal += descMonto; impuestosTotal += impMonto;
    });
    const total = subtotal - descuentoTotal + impuestosTotal;
    document.getElementById('tSubtotal').textContent = '$' + subtotal.toFixed(2);
    document.getElementById('tDescuento').textContent = '-$' + descuentoTotal.toFixed(2);
    document.getElementById('tImpuestos').textContent = '$' + impuestosTotal.toFixed(2);
    document.getElementById('tTotal').textContent = '$' + total.toFixed(2);
    const monedaId = document.getElementById('moneda').value;
    const palabra = MONEDA_PALABRA[monedaId] || 'PESOS';
    const sufijo = monedaId == 3 ? ' M.N.' : '';
    document.getElementById('tSon').textContent = 'SON: ' + numeroALetras(total, palabra) + sufijo;
  }

  // ---- Detalle de producto (doble clic / clic en la partida) -- usa los datos que ya
  // vinieron precargados en PRODUCTOS, sin ida y vuelta al servidor. ----
  function verDetalle(pid) {
    const p = porId(pid);
    if (!p) return;
    const existHtml = p.exist.length
      ? '<table><thead><tr><th>Almacén</th><th>Existencia</th></tr></thead><tbody>' +
        p.exist.map(e => `<tr><td>${e.almacen}</td><td>${e.qty.toFixed(2)}</td></tr>`).join('') + '</tbody></table>'
      : '<div class="vacio">(sin almacenes)</div>';
    const listasHtml = p.listas.length
      ? '<table><thead><tr><th>Lista de precios</th><th>Precio</th></tr></thead><tbody>' +
        p.listas.map(e => `<tr><td>${e.lista}</td><td>$${e.precio.toFixed(2)}</td></tr>`).join('') + '</tbody></table>'
      : '<div class="vacio">(sin listas de precio asignadas)</div>';
    const provHtml = p.proveedores.length
      ? '<table><thead><tr><th>Proveedor</th><th>Ref. proveedor</th><th>Costo</th></tr></thead><tbody>' +
        p.proveedores.map(e => `<tr><td>${e.proveedor}</td><td>${e.ref}</td><td>$${e.costo.toFixed(2)}</td></tr>`).join('') + '</tbody></table>'
      : '<div class="vacio">(sin proveedores registrados)</div>';

    document.getElementById('modalDetalle').innerHTML = `
      <h2>Detalle de producto</h2>
      <div class="grid2">
        <div><b>Clave:</b> ${p.key}</div><div><b>Nombre:</b> ${p.nombre}</div>
        <div><b>Descripción:</b> ${p.descripcion || '-'}</div><div></div>
        <div><b>Unidad:</b> ${p.unidad}</div><div><b>U. venta:</b> ${p.unitSale || '-'}</div>
        <div><b>U. compra:</b> ${p.unitBuy || '-'}</div><div><b>Costo:</b> $${p.costo.toFixed(2)}</div>
        <div><b>Precio lista:</b> $${p.precioLista.toFixed(2)}</div><div></div>
        <div><b>Categoría 1:</b> ${p.cat1 || '-'}</div><div><b>Categoría 2:</b> ${p.cat2 || '-'}</div>
        <div><b>Categoría 3:</b> ${p.cat3 || '-'}</div><div><b>Categoría 4:</b> ${p.cat4 || '-'}</div>
      </div>
      <h3>Existencia por almacén</h3>${existHtml}
      <h3>Listas de precios</h3>${listasHtml}
      <h3>Precios por proveedor</h3>${provHtml}
      <div class="cerrar"><button type="button" onclick="cerrarDetalle()">Cerrar</button></div>
    `;
    document.getElementById('overlay').classList.add('abierto');
  }
  function cerrarDetalle() { document.getElementById('overlay').classList.remove('abierto'); }

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
      fecha: document.getElementById('fecha').value,
      comentarios: document.getElementById('comentarios').value,
      partidas: partidas
    }));
  }

  render();
</script>
</body>
</html>
"""

html = (html
    .replace("__OPCIONES_ALMACEN__", opciones_almacen)
    .replace("__OPCIONES_MONEDA__", opciones_moneda)
    .replace("__OPCIONES_CONDICION__", opciones_condicion)
    .replace("__SERIE_DEFAULT__", serie_default.replace('"', "&quot;"))
    .replace("__FOLIO_DEFAULT__", folio_default.replace('"', "&quot;"))
    .replace("__PROVEEDORES_JSON__", proveedores_json)
    .replace("__PRODUCTOS_JSON__", productos_json)
    .replace("__IMPUESTOS_JSON__", impuestos_json)
    .replace("__MONEDA_RATE_JSON__", moneda_rate_json)
    .replace("__MONEDA_PALABRA_JSON__", moneda_palabra_json))

r = ctx.show_html_formulario(html, title="Nueva Orden de Compra (WebView2)", width=1020, height=820)

if r.get("cancelado") or not r.get("submitted"):
    result = "Cancelado, no se creó ningún documento."
else:
    be = r.get("businessEntityId")
    depot = r.get("depotId")
    moneda = r.get("monedaId") or 3
    condicion = r.get("condicionId") or 0
    entrega = r.get("entrega") or ""
    fecha = r.get("fecha") or ""
    partidas = r.get("partidas") or []
    comentarios = r.get("comentarios") or ""

    if not be or not depot or not partidas:
        result = "ERROR: faltan datos (proveedor/almacén/partidas)."
    else:
        supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(be))
        rate = 1.0
        rr = ctx.query("SELECT Rate FROM vwLBSCurrencyList WHERE CurrencyID=" + str(moneda))
        if rr:
            rate = float(rr[0]["Rate"] or 1)

        # Mismo patrón que MANUAL.md §7.5 -- a diferencia de Requisición, SÍ lleva
        # precio/costo, impuesto y descuento por partida, AffectStockNEW y
        # UpdateDocumentPaidInfo.
        doc = ctx.erp.NuevoDocumento(183, depot, be)
        fecha_entrega_sql = "'" + entrega.replace("-", "") + "'" if entrega else "GETDATE()"
        fecha_doc_sql = "'" + fecha.replace("-", "") + "'" if fecha else "GETDATE()"
        ctx.execute(
            "UPDATE docDocument SET DepotIDFrom=0, CurrencyID=" + str(moneda) + ", Rate=" + str(rate) +
            ", PaymentTermID=" + str(condicion) +
            ", DateDocument=" + fecha_doc_sql +
            ", DateDelivery=" + fecha_entrega_sql + ", DateDocDelivery=" + fecha_entrega_sql +
            ", Comments=N'" + comentarios.replace("'", "''") + "' WHERE DocumentID=" + str(doc)
        )
        # Serie/Folio: NO se tocan aquí -- ya quedaron asignados por NuevoDocumento() arriba.
        for p in partidas:
            pid = int(p["id"])
            precio = float(p["precio"])
            tax_type_id = int(p.get("taxTypeId", -1))
            descuento_perc = float(p.get("descuentoPerc", 0))
            ctx.erp.AgregarArticulo(doc, pid, float(p["cantidad"]), precio, -1, tax_type_id, descuento_perc)
            # Vincula/actualiza el precio negociado con el proveedor (orgProductSupplier),
            # igual que la versión Forms.
            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id) + ") " +
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id) + ", " + str(precio) + ", " + str(moneda) + ", 0) " +
                "ELSE UPDATE orgProductSupplier SET CostPrice=" + str(precio) + " WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id)
            )

        ctx.erp.RecalcCompleto(doc)
        ctx.erp.AffectStockNEW(doc)
        ctx.erp.Save(doc)
        try:
            ctx.erp.UpdateStatusDelivery(doc)
        except Exception:
            pass
        try:
            ctx.erp.UpdateDocumentPaidInfo(doc)
        except Exception:
            pass

        result = "Orden de compra " + str(doc) + " creada exitosamente."
