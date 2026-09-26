# lang: python
# PLANTILLA: Factura de Compra — versión WebView2 (Python, HTML/CSS real)
#
# Qué hace: EXACTAMENTE lo mismo que PLANTILLA_FACTURA_COMPRA_WEBVIEW2_CSHARP.ctx --
# transforma 1+ Órdenes de Compra ya seleccionadas en el grid nativo de Comercial en una
# Factura de Compra real (módulo 152). Mismo origen (ctx.get_selected_ids()), mismo cálculo
# de pendientes por SourceDocumentItemID, mismo perfil de encabezado y la misma corrección
# del bug real de la plantilla comunitaria vieja (el vínculo con la OC origen se pone con un
# UPDATE aparte después de AgregarArticulo, NO con su 8º parámetro -- ver
# PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx para el detalle completo).
#
# timeout: 1800 -- OBLIGATORIO: un formulario real puede tardar varios minutos en llenarse.
# timeout: 1800

from broslmv import ctx
import json

# ---- Validación temprana de la selección ----
ids_seleccionados = ctx.get_selected_ids()
if not ids_seleccionados:
    ctx.msg("Selecciona una o varias Órdenes de Compra en la lista (Ctrl+clic) antes de usar este botón.", "Sin selección")
    result = "Cancelado: sin selección."
else:
    ids_csv = ",".join(str(i) for i in ids_seleccionados)
    ocs_seleccionadas = ctx.query("""
        SELECT d.DocumentID, d.BusinessEntityID, d.DepotID, d.FolioPrefix, d.Folio, be.OfficialName,
               ISNULL(m.OfficialNumber,'') AS RFC
        FROM docDocument d
        INNER JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID
        LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
        WHERE d.DocumentID IN (%s) AND d.ModuleID = 183
          AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
    """ % ids_csv)

    proveedores_distintos = list({o["BusinessEntityID"] for o in ocs_seleccionadas})

    if not ocs_seleccionadas:
        ctx.msg("Ninguna de las filas seleccionadas es una Orden de Compra activa (módulo 183).", "Selección inválida")
        result = "Cancelado: selección inválida."
    elif len(proveedores_distintos) > 1:
        ctx.msg("Las Órdenes de Compra seleccionadas son de proveedores distintos. Selecciona Órdenes de Compra de UN SOLO proveedor a la vez.", "Selección inválida")
        result = "Cancelado: proveedores mixtos."
    else:
        proveedor_be = proveedores_distintos[0]
        proveedor_nombre = ocs_seleccionadas[0]["OfficialName"]
        proveedor_rfc = ocs_seleccionadas[0]["RFC"]
        depot_origen = ocs_seleccionadas[0]["DepotID"] or 1
        ocs_resumen_texto = ", ".join((o["FolioPrefix"] or "") + str(o["Folio"]) for o in ocs_seleccionadas)

        # ---- Datos iniciales ----
        condiciones = ctx.query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID")
        impuestos = ctx.query("""
            SELECT t.TaxTypeID, t.TaxTypeName, ISNULL(tp.IVA_Perc,0) AS IVA_Perc
            FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID
            ORDER BY t.TaxTypeName
        """)

        rows = ctx.query("""
            SELECT d.DocumentID, d.FolioPrefix, d.Folio,
                   i.DocumentItemID, i.ProductID, ISNULL(p.ProductKey, i.ProductKey) AS ProductKey,
                   ISNULL(p.ProductName, i.Description) AS ProductName, i.Unit, i.TaxTypeID, i.TaxPerc,
                   i.UnitPrice, i.DiscountPerc, i.Quantity AS Ordenado,
                   ISNULL((SELECT SUM(fi.Quantity) FROM docDocumentItem fi
                           INNER JOIN docDocument fd ON fd.DocumentID = fi.DocumentID
                           WHERE fi.SourceDocumentItemID = i.DocumentItemID AND fi.DeletedOn IS NULL
                             AND fd.DeletedOn IS NULL AND fd.CancelledOn IS NULL), 0) AS YaFacturado
            FROM docDocumentItem i
            INNER JOIN docDocument d ON d.DocumentID = i.DocumentID
            LEFT JOIN orgProduct p ON p.ProductID = i.ProductID
            WHERE d.DocumentID IN (%s)
              AND i.DeletedOn IS NULL AND i.ProductID > 0
            ORDER BY d.DocumentID, i.LineNumber
        """ % ids_csv)

        oc_folio_por_id = {o["DocumentID"]: (o["FolioPrefix"] or "") + str(o["Folio"]) for o in ocs_seleccionadas}

        por_producto = {}
        for r in rows:
            ordenado = float(r["Ordenado"] or 0)
            ya_facturado = float(r["YaFacturado"] or 0)
            pendiente = ordenado - ya_facturado
            if pendiente <= 0.0001:
                continue
            pid = r["ProductID"]
            doc_id = r["DocumentID"]
            fuente = {"sourceItemId": r["DocumentItemID"], "ocFolio": oc_folio_por_id.get(doc_id, "Doc %d" % doc_id), "pendiente": pendiente}
            if pid not in por_producto:
                por_producto[pid] = {
                    "pid": pid, "key": r["ProductKey"], "nombre": r["ProductName"], "unidad": r["Unit"],
                    "taxTypeId": r["TaxTypeID"] or 0, "precio": float(r["UnitPrice"] or 0),
                    "descuentoPerc": float(r["DiscountPerc"] or 0), "fuentes": []
                }
            por_producto[pid]["fuentes"].append(fuente)

        partidas = []
        for pc in por_producto.values():
            pendiente_total = sum(f["pendiente"] for f in pc["fuentes"])
            ocs_resumen = ", ".join(dict.fromkeys(f["ocFolio"] for f in pc["fuentes"]))
            partidas.append({
                "pid": pc["pid"], "key": pc["key"], "nombre": pc["nombre"], "unidad": pc["unidad"],
                "taxTypeId": pc["taxTypeId"], "precio": pc["precio"], "descuentoPerc": pc["descuentoPerc"],
                "pendiente": pendiente_total, "ocsResumen": ocs_resumen, "fuentes": pc["fuentes"]
            })

        opciones_condicion = "".join(
            '<option value="%d"%s>%s</option>' % (c["PaymentTermID"], ' selected' if c["PaymentTermID"] == 4 else '', c["PaymentTermName"])
            for c in condiciones
        )
        impuestos_json = json.dumps([{"id": t["TaxTypeID"], "nombre": t["TaxTypeName"], "perc": float(t["IVA_Perc"] or 0)} for t in impuestos])
        partidas_json = json.dumps(partidas)

        html = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<style>
  :root { --primary: #B45309; --border: #DCE3ED; --bg: #EDF2F9; --text: #1F2937; --muted: #667085; --danger: #EF4444; }
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
  table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
  th { text-align: left; background: #F1F4F9; color: var(--muted); padding: 6px 8px; }
  td { padding: 5px 8px; border-bottom: 1px solid #F1F4F9; vertical-align: middle; }
  td input, td select { margin-bottom: 0; padding: 4px 6px; }
  button { border-radius: 6px; padding: 8px 16px; font-size: 13px; font-weight: 600; border: 1px solid var(--border); background: #fff; cursor: pointer; }
  button.primary { background: var(--primary); border-color: var(--primary); color: #fff; }
  .toolbar { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
  .vacio { color: var(--muted); text-align: center; padding: 20px; font-size: 13px; }
  .totales { display: flex; justify-content: flex-end; gap: 28px; align-items: baseline; }
  .totales div { text-align: right; }
  .totales .lbl { font-size: 12px; color: var(--muted); }
  .totales .val { font-size: 15px; font-weight: 600; }
  .totales .val.desc { color: var(--danger); }
  .totales .total .val { font-size: 22px; color: var(--primary); }
  .son { text-align: right; font-size: 11px; font-style: italic; color: var(--muted); margin-top: 4px; }
</style>
</head>
<body>
  <h1>Nueva Factura de Compra</h1>
  <div class="sub">Versión WebView2 -- transforma las Órdenes de Compra seleccionadas en una Factura.</div>

  <div class="card">
    <div class="row">
      <div><label>Proveedor</label><input readonly value="__PROVEEDOR__"></div>
      <div><label>RFC</label><input readonly value="__RFC__"></div>
      <div><label>Órdenes origen</label><input readonly value="__OCS_RESUMEN__"></div>
    </div>
    <div class="row">
      <div style="flex:0 0 200px"><label>Condición de pago</label><select id="condicion">__OPCIONES_CONDICION__</select></div>
      <div><label>Comentarios</label><input id="comentarios" placeholder="Opcional"></div>
    </div>
  </div>

  <div class="card">
    <table>
      <thead><tr><th>Orden(es)</th><th>Clave</th><th>Descripción</th><th>Cant.</th><th>Precio</th><th>Impuesto</th><th>Desc. %</th><th>Importe</th></tr></thead>
      <tbody id="partidas"></tbody>
    </table>
    <div id="vacio" class="vacio" style="display:none">No hay partidas pendientes de facturar en las Órdenes de Compra seleccionadas.</div>
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
    <button type="button" class="primary" onclick="crear()">Crear Factura de Compra</button>
  </div>

<script>
  const PARTIDAS = __PARTIDAS_JSON__;
  const IMPUESTOS = __IMPUESTOS_JSON__;

  function impPorId(id) { return IMPUESTOS.find(t => t.id == id); }

  const UNIDADES = ['', 'UNO', 'DOS', 'TRES', 'CUATRO', 'CINCO', 'SEIS', 'SIETE', 'OCHO', 'NUEVE', 'DIEZ',
    'ONCE', 'DOCE', 'TRECE', 'CATORCE', 'QUINCE', 'DIECISÉIS', 'DIECISIETE', 'DIECIOCHO', 'DIECINUEVE', 'VEINTE'];
  const DECENAS = ['', '', 'VEINTE', 'TREINTA', 'CUARENTA', 'CINCUENTA', 'SESENTA', 'SETENTA', 'OCHENTA', 'NOVENTA'];
  const VEINTI = ['VEINTIUNO', 'VEINTIDÓS', 'VEINTITRÉS', 'VEINTICUATRO', 'VEINTICINCO', 'VEINTISÉIS', 'VEINTISIETE', 'VEINTIOCHO', 'VEINTINUEVE'];
  const CENTENAS = ['', 'CIENTO', 'DOSCIENTOS', 'TRESCIENTOS', 'CUATROCIENTOS', 'QUINIENTOS', 'SEISCIENTOS', 'SETECIENTOS', 'OCHOCIENTOS', 'NOVECIENTOS'];
  function centenas_(n) {
    if (n === 0) return ''; if (n === 100) return 'CIEN';
    let s = ''; const c = Math.floor(n / 100), r = n % 100;
    if (c > 0) s += CENTENAS[c] + ' ';
    if (r > 0) { if (r <= 20) s += UNIDADES[r]; else { const d = Math.floor(r / 10), u = r % 10; if (d === 2 && u > 0) s += VEINTI[u - 1]; else { s += DECENAS[d]; if (u > 0) s += ' Y ' + UNIDADES[u]; } } }
    return s.trim();
  }
  function enLetras(n) {
    if (n === 0) return 'CERO';
    let resultado = ''; const millones = Math.floor(n / 1000000); n %= 1000000; const miles = Math.floor(n / 1000); n %= 1000; const resto = n;
    if (millones > 0) resultado += (millones === 1 ? 'UN MILLÓN ' : centenas_(millones) + ' MILLONES ');
    if (miles > 0) resultado += (miles === 1 ? 'MIL ' : centenas_(miles) + ' MIL ');
    if (resto > 0) resultado += centenas_(resto);
    resultado = resultado.trim();
    if (resultado.endsWith('UNO')) resultado = resultado.slice(0, -3) + 'UN';
    return resultado;
  }
  function numeroALetras(valor) {
    if (valor < 0) valor = 0;
    const entero = Math.floor(valor); const centavos = Math.round((valor - entero) * 100);
    return enLetras(entero) + ' PESOS ' + String(centavos).padStart(2, '0') + '/100';
  }

  const tbody = document.getElementById('partidas');
  PARTIDAS.forEach((p, i) => {
    const tr = document.createElement('tr');
    const impOpts = IMPUESTOS.map(t => `<option value="${t.id}" ${t.id==p.taxTypeId?'selected':''}>${t.nombre}</option>`).join('');
    tr.innerHTML = `
      <td>${p.ocsResumen}</td><td>${p.key}</td><td>${p.nombre}</td>
      <td><input type="number" class="qty" data-i="${i}" value="${p.pendiente}" min="0" max="${p.pendiente}" step="0.01" style="width:75px"></td>
      <td><input type="number" class="precio" data-i="${i}" value="${p.precio}" min="0" step="0.01" style="width:80px"></td>
      <td><select class="imp" data-i="${i}" style="width:120px">${impOpts}</select></td>
      <td><input type="number" class="desc" data-i="${i}" value="${(p.descuentoPerc*100).toFixed(2)}" min="0" max="100" step="0.01" style="width:65px"></td>
      <td class="importe" style="text-align:right">$0.00</td>
    `;
    tbody.appendChild(tr);
  });
  document.getElementById('vacio').style.display = PARTIDAS.length ? 'none' : 'block';

  function recalcular() {
    let subtotal = 0, descuentoTotal = 0, impuestosTotal = 0;
    document.querySelectorAll('#partidas tr').forEach((tr, i) => {
      const qty = parseFloat(tr.querySelector('.qty').value) || 0;
      const precio = parseFloat(tr.querySelector('.precio').value) || 0;
      const descPerc = (parseFloat(tr.querySelector('.desc').value) || 0) / 100;
      const taxTypeId = parseInt(tr.querySelector('.imp').value);
      const t = impPorId(taxTypeId);
      const taxPerc = t ? t.perc : 0;
      const importe = qty * precio;
      const descMonto = importe * descPerc;
      const neto = importe - descMonto;
      const impMonto = neto * taxPerc;
      tr.querySelector('.importe').textContent = '$' + importe.toFixed(2);
      subtotal += importe; descuentoTotal += descMonto; impuestosTotal += impMonto;
    });
    const total = subtotal - descuentoTotal + impuestosTotal;
    document.getElementById('tSubtotal').textContent = '$' + subtotal.toFixed(2);
    document.getElementById('tDescuento').textContent = '-$' + descuentoTotal.toFixed(2);
    document.getElementById('tImpuestos').textContent = '$' + impuestosTotal.toFixed(2);
    document.getElementById('tTotal').textContent = '$' + total.toFixed(2);
    document.getElementById('tSon').textContent = 'SON: ' + numeroALetras(total) + ' M.N.';
  }
  document.querySelectorAll('#partidas input, #partidas select').forEach(el => el.addEventListener('input', recalcular));
  document.querySelectorAll('#partidas select').forEach(el => el.addEventListener('change', recalcular));
  recalcular();

  function cancelar() {
    window.chrome.webview.postMessage(JSON.stringify({ cancelado: true }));
  }

  function crear() {
    const partidas = [];
    document.querySelectorAll('#partidas tr').forEach((tr, i) => {
      const p = PARTIDAS[i];
      const qty = parseFloat(tr.querySelector('.qty').value) || 0;
      if (qty <= 0) return;
      const precio = parseFloat(tr.querySelector('.precio').value) || 0;
      const descPerc = (parseFloat(tr.querySelector('.desc').value) || 0) / 100;
      const taxTypeId = parseInt(tr.querySelector('.imp').value);
      partidas.push({ pid: p.pid, cantidad: qty, precio: precio, taxTypeId: taxTypeId, descuentoPerc: descPerc, fuentes: p.fuentes });
    });
    if (partidas.length === 0) { alert('Marca al menos una partida con cantidad mayor a 0.'); return; }
    window.chrome.webview.postMessage(JSON.stringify({
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
            .replace("__PROVEEDOR__", proveedor_nombre)
            .replace("__RFC__", proveedor_rfc)
            .replace("__OCS_RESUMEN__", ocs_resumen_texto)
            .replace("__OPCIONES_CONDICION__", opciones_condicion)
            .replace("__PARTIDAS_JSON__", partidas_json)
            .replace("__IMPUESTOS_JSON__", impuestos_json))

        r = ctx.show_html_formulario(html, title="Nueva Factura de Compra (WebView2)", width=1100, height=780)

        if r.get("cancelado") or not r.get("submitted"):
            result = "Cancelado, no se creó ningún documento."
        else:
            cond_id = r.get("condicionId") or 4
            comentarios = r.get("comentarios") or ""
            partidas_recibidas = r.get("partidas") or []

            if not partidas_recibidas:
                result = "ERROR: faltan partidas."
            else:
                doc = ctx.erp.NuevoDocumento(152, depot_origen, proveedor_be)
                ctx.execute(
                    "UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=" + str(cond_id) + ", StatusDeliveryID=0, " +
                    "CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL, DateDelivery=GETDATE(), " +
                    "Comments=N'" + comentarios.replace("'", "''") + "' WHERE DocumentID=" + str(doc)
                )

                supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(proveedor_be))

                for p in partidas_recibidas:
                    pid = int(p["pid"])
                    cantidad = float(p["cantidad"])
                    precio = float(p["precio"])
                    tax_type_id = int(p["taxTypeId"])
                    descuento_perc = float(p.get("descuentoPerc", 0))
                    fuentes = p.get("fuentes") or []

                    restante = cantidad
                    for fuente in fuentes:
                        if restante <= 0.0001:
                            break
                        pendiente = float(fuente["pendiente"])
                        a_facturar_de_esta = min(restante, pendiente)
                        if a_facturar_de_esta <= 0.0001:
                            continue
                        # Sin 8vo parametro -- ese escribe DeliverDocumentItemID (Recepcion),
                        # no SourceDocumentItemID (Factura). El vinculo real va abajo.
                        item_id = ctx.erp.AgregarArticulo(doc, pid, a_facturar_de_esta, precio, -1, tax_type_id, descuento_perc)
                        source_item_id = int(fuente["sourceItemId"])
                        ctx.execute("UPDATE docDocumentItem SET SourceDocumentItemID=" + str(source_item_id) + " WHERE DocumentItemID=" + str(item_id))
                        restante -= a_facturar_de_esta

                    ctx.execute(
                        "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id) + ") " +
                        "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id) + ", 0, 3, 0)"
                    )

                ctx.erp.RecalcCompleto(doc)
                ctx.erp.Save(doc)

                # PaymentAgenda: NuevoDocumento crea un placeholder Amount=0 que Save() no
                # corrige -- se regenera a mano con los porcentajes reales de
                # engPaymentTermDetail. Ver PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx.
                try:
                    total = float(ctx.scalar("SELECT Total FROM docDocument WHERE DocumentID=" + str(doc)) or 0)
                    detalle = ctx.query("SELECT PaymentPerc, PaymentUnit, PaymentPeriod FROM engPaymentTermDetail WHERE PaymentTermID=" + str(cond_id) + " ORDER BY PaymentTermDetailID")
                    if detalle:
                        ctx.execute("UPDATE docDocumentPaymentAgenda SET DeletedOn=GETDATE() WHERE DeletedOn IS NULL AND DocumentID=" + str(doc))
                        numero = 1
                        for d in detalle:
                            perc = float(d["PaymentPerc"] or 0)
                            unidad = int(d["PaymentUnit"] or 1)
                            periodo = int(d["PaymentPeriod"] or 0)
                            if unidad == 3:
                                expr = "DATEADD(MONTH,%d,GETDATE())" % periodo
                            elif unidad == 2:
                                expr = "DATEADD(DAY,%d,GETDATE())" % (periodo * 7)
                            else:
                                expr = "DATEADD(DAY,%d,GETDATE())" % periodo
                            monto = total * perc / 100.0
                            ctx.execute(
                                "INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" +
                                str(doc) + ", " + expr + ", " + str(perc) + ", " + str(monto) + ", " + str(numero) + ", GETDATE(), " + str(ctx.user_id) + ")"
                            )
                            numero += 1
                except Exception:
                    pass

                try:
                    ctx.erp.RefreshGrid()
                except Exception:
                    pass

                result = "Factura de compra " + str(doc) + " creada exitosamente."
