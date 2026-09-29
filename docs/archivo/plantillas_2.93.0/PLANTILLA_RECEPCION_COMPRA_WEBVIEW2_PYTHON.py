# lang: python
# PLANTILLA: Recepción de Compra — versión WebView2 (Python, HTML/CSS real)
#
# Qué hace: EXACTAMENTE lo mismo que PLANTILLA_RECEPCION_COMPRA_WEBVIEW2_CSHARP.ctx --
# consolida N Órdenes de Compra del mismo proveedor por producto, captura lote/número de
# serie, crea una Recepción de Compra real (módulo 184, SÍ afecta inventario). Ver esa
# plantilla para la explicación completa del diseño (por qué se precargan los pendientes de
# TODOS los proveedores, consolidación en JS, captura de lote/serie por textarea inline).
#
# timeout: 1800 -- OBLIGATORIO: un formulario real puede tardar varios minutos en llenarse.
# timeout: 1800

from broslmv import ctx
import json

# ---- Datos iniciales ----
proveedores_con_oc = ctx.query("""
    SELECT DISTINCT be.BusinessEntityID, be.OfficialName, ISNULL(m.OfficialNumber,'') AS RFC
    FROM docDocument d
    INNER JOIN docDocumentItem i ON i.DocumentID = d.DocumentID AND i.DeletedOn IS NULL AND i.MustBeDelivered <> 0
    INNER JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
    WHERE d.ModuleID = 183 AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
    ORDER BY be.OfficialName
""")
almacenes = ctx.query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName")

rows = ctx.query("""
    SELECT d.DocumentID, d.BusinessEntityID, d.FolioPrefix, d.Folio, d.DateDocument,
           i.DocumentItemID, i.ProductID, ISNULL(p.ProductKey, i.ProductKey) AS ProductKey,
           ISNULL(p.ProductName, i.Description) AS ProductName, i.Unit, i.TaxTypeID, i.TaxPerc,
           i.UnitPrice, i.DiscountPerc, i.Quantity AS Ordenado,
           ISNULL((SELECT SUM(ri.Quantity) FROM docDocumentItem ri
                   INNER JOIN docDocument rd ON rd.DocumentID = ri.DocumentID
                   WHERE ri.DeliverDocumentItemID = i.DocumentItemID AND ri.DeletedOn IS NULL
                     AND rd.DeletedOn IS NULL AND rd.CancelledOn IS NULL), 0) AS Recibido,
           ISNULL(p.UseLot,0) AS UseLot, ISNULL(p.UseSerialNumber,0) AS UseSerialNumber
    FROM docDocumentItem i
    INNER JOIN docDocument d ON d.DocumentID = i.DocumentID
    LEFT JOIN orgProduct p ON p.ProductID = i.ProductID
    WHERE d.ModuleID = 183 AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
      AND i.DeletedOn IS NULL AND i.MustBeDelivered <> 0
      AND i.ProductID > 0 AND ISNULL(p.ProductIsService,0) = 0 AND ISNULL(p.ProductTypeID,0) <> 4
    ORDER BY d.BusinessEntityID, d.DocumentID, i.LineNumber
""")

por_proveedor = {}
oc_info_por_id = {}
for r in rows:
    ordenado = float(r["Ordenado"] or 0)
    recibido = float(r["Recibido"] or 0)
    pendiente = ordenado - recibido
    if pendiente <= 0.0001:
        continue
    be = r["BusinessEntityID"]
    doc_id = r["DocumentID"]
    por_proveedor.setdefault(be, {}).setdefault(doc_id, []).append(r)
    if doc_id not in oc_info_por_id:
        oc_info_por_id[doc_id] = {"folio": (r["FolioPrefix"] or "") + str(r["Folio"]), "fecha": r["DateDocument"].strftime("%d/%m/%Y")}

proveedores_data = {}
for be, ocs in por_proveedor.items():
    ocs_list = [{"docId": doc_id, "folio": oc_info_por_id[doc_id]["folio"], "fecha": oc_info_por_id[doc_id]["fecha"]} for doc_id in sorted(ocs.keys())]
    partidas_list = []
    for doc_id, items in ocs.items():
        for r in items:
            partidas_list.append({
                "ocDocId": doc_id,
                "pid": r["ProductID"],
                "sourceItemId": r["DocumentItemID"],
                "key": r["ProductKey"],
                "nombre": r["ProductName"],
                "unidad": r["Unit"],
                "taxTypeId": r["TaxTypeID"] or 0,
                "taxPerc": float(r["TaxPerc"] or 0),
                "precio": float(r["UnitPrice"] or 0),
                "descuentoPerc": float(r["DiscountPerc"] or 0),
                "pendiente": float(r["Ordenado"] or 0) - float(r["Recibido"] or 0),
                "useLot": bool(r["UseLot"]),
                "useSerial": bool(r["UseSerialNumber"]),
            })
    proveedores_data[str(be)] = {"ocs": ocs_list, "partidas": partidas_list}

opciones_proveedor = "".join('<option value="%d">%s</option>' % (p["BusinessEntityID"], p["OfficialName"]) for p in proveedores_con_oc)
opciones_almacen = "".join('<option value="%d">%s</option>' % (a["DepotID"], a["DepotName"]) for a in almacenes)
proveedores_json = json.dumps(proveedores_data)

html = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<style>
  :root { --primary: #15803D; --border: #DCE3ED; --bg: #EDF2F9; --text: #1F2937; --muted: #667085; --danger: #EF4444; }
  * { box-sizing: border-box; }
  body { font-family: "Segoe UI", sans-serif; background: var(--bg); color: var(--text); margin: 0; padding: 20px; }
  h1 { font-size: 18px; margin: 0 0 4px; }
  .sub { color: var(--muted); font-size: 13px; margin-bottom: 16px; }
  .card { background: #fff; border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 14px; }
  label { display: block; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
  select, input, textarea { width: 100%; padding: 7px 10px; border: 1px solid var(--border); border-radius: 6px; font-size: 13px; margin-bottom: 12px; font-family: inherit; }
  .row { display: flex; gap: 12px; }
  .row > div { flex: 1; }
  table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
  th { text-align: left; background: #F1F4F9; color: var(--muted); padding: 6px 8px; }
  td { padding: 6px 8px; border-bottom: 1px solid #F1F4F9; vertical-align: top; }
  td input { margin-bottom: 0; padding: 4px 6px; }
  .oc-item { display: flex; align-items: center; gap: 8px; padding: 6px 4px; border-bottom: 1px solid #F1F4F9; }
  button { border-radius: 6px; padding: 8px 16px; font-size: 13px; font-weight: 600; border: 1px solid var(--border); background: #fff; cursor: pointer; }
  button.primary { background: var(--primary); border-color: var(--primary); color: #fff; }
  .toolbar { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
  .vacio { color: var(--muted); text-align: center; padding: 20px; font-size: 13px; }
  .cap-lote, .cap-serie { display: none; margin-top: 4px; }
  .cap-lote.on, .cap-serie.on { display: block; }
  .hint { color: var(--muted); font-size: 10.5px; }
  .badge { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 10.5px; margin-left: 6px; }
  .badge.ok { background: #DCFCE7; color: #15803D; }
  .badge.falta { background: #FEE2E2; color: #B91C1C; }
</style>
</head>
<body>
  <h1>Nueva Recepción de Compra</h1>
  <div class="sub">Versión WebView2 -- consolida N Órdenes de Compra por producto, SÍ afecta inventario.</div>

  <div class="card">
    <div class="row">
      <div><label>Proveedor (con Órdenes de Compra pendientes)</label><select id="proveedor" onchange="cambiarProveedor()"><option value="">-- Selecciona --</option>__OPCIONES_PROVEEDOR__</select></div>
      <div style="flex:0 0 220px"><label>Almacén</label><select id="almacen">__OPCIONES_ALMACEN__</select></div>
      <div style="flex:0 0 160px"><label>Fecha</label><input id="fecha" type="date"></div>
    </div>
    <label>Comentarios</label>
    <input id="comentarios" placeholder="Opcional">
  </div>

  <div class="card" id="cardOcs" style="display:none">
    <label style="margin-bottom:8px">Órdenes de Compra pendientes de recibir (marca una o varias)</label>
    <div id="listaOcs"></div>
  </div>

  <div class="card" id="cardPartidas" style="display:none">
    <table>
      <thead><tr><th>Orden(es)</th><th>Clave</th><th>Descripción</th><th style="width:110px">Pendiente</th><th style="width:110px">Cant. a recibir</th></tr></thead>
      <tbody id="partidas"></tbody>
    </table>
    <div id="vacio" class="vacio" style="display:none">No hay partidas pendientes de recibir en las Órdenes de Compra marcadas.</div>
  </div>

  <div class="toolbar">
    <button type="button" onclick="cancelar()">Cancelar</button>
    <button type="button" class="primary" onclick="crear()">Crear Recepción de Compra</button>
  </div>

<script>
  const PROVEEDORES = __PROVEEDORES_JSON__;
  let ocsMarcadas = new Set();
  let partidasConsolidadas = [];

  function cambiarProveedor() {
    const be = document.getElementById('proveedor').value;
    ocsMarcadas = new Set();
    document.getElementById('cardOcs').style.display = be ? 'block' : 'none';
    document.getElementById('cardPartidas').style.display = 'none';
    if (!be) { document.getElementById('listaOcs').innerHTML = ''; return; }
    const data = PROVEEDORES[be];
    const listaOcs = document.getElementById('listaOcs');
    listaOcs.innerHTML = data.ocs.map(oc => `
      <div class="oc-item">
        <input type="checkbox" data-doc="${oc.docId}" onchange="toggleOc(${oc.docId}, this.checked)">
        <div><b>${oc.folio}</b> <span class="hint">${oc.fecha}</span></div>
      </div>
    `).join('');
    consolidar();
  }

  function toggleOc(docId, marcado) {
    if (marcado) ocsMarcadas.add(docId); else ocsMarcadas.delete(docId);
    consolidar();
  }

  function consolidar() {
    const be = document.getElementById('proveedor').value;
    if (!be) return;
    const data = PROVEEDORES[be];
    const previos = {};
    partidasConsolidadas.forEach(p => previos[p.pid] = p);

    const fuentesPorProducto = {};
    data.partidas.filter(f => ocsMarcadas.has(f.ocDocId)).forEach(f => {
      if (!fuentesPorProducto[f.pid]) fuentesPorProducto[f.pid] = [];
      fuentesPorProducto[f.pid].push(f);
    });

    partidasConsolidadas = Object.keys(fuentesPorProducto).map(pid => {
      const fuentes = fuentesPorProducto[pid].sort((a, b) => a.sourceItemId - b.sourceItemId);
      const primero = fuentes[0];
      const pendiente = fuentes.reduce((s, f) => s + f.pendiente, 0);
      const ocsResumen = [...new Set(fuentes.map(f => 'OC ' + f.ocDocId))].join(', ');
      const prev = previos[pid];
      return {
        pid: primero.pid, key: primero.key, nombre: primero.nombre, unidad: primero.unidad,
        taxTypeId: primero.taxTypeId, taxPerc: primero.taxPerc, precio: primero.precio, descuentoPerc: primero.descuentoPerc,
        useLot: primero.useLot, useSerial: primero.useSerial,
        pendiente: pendiente, qty: prev ? Math.min(prev.qty, pendiente) : pendiente,
        ocsResumen: ocsResumen, fuentes: fuentes,
        lotesTexto: prev ? prev.lotesTexto : '', seriesTexto: prev ? prev.seriesTexto : ''
      };
    });
    renderPartidas();
  }

  function renderPartidas() {
    document.getElementById('cardPartidas').style.display = partidasConsolidadas.length ? 'block' : 'none';
    const tbody = document.getElementById('partidas');
    tbody.innerHTML = partidasConsolidadas.map((p, i) => `
      <tr>
        <td>${p.ocsResumen}</td><td>${p.key}</td><td>${p.nombre}</td>
        <td>${p.pendiente.toFixed(2)}</td>
        <td><input type="number" class="qty" data-i="${i}" value="${p.qty}" min="0" max="${p.pendiente}" step="0.01" oninput="onQtyChange(${i}, this.value)"></td>
      </tr>
      <tr>
        <td colspan="5" style="padding-top:0">
          <div class="cap-lote ${p.useLot ? 'on' : ''}" id="lote_${i}">
            <label>Lotes de <b>${p.key}</b> -- una línea por lote: <code>CANTIDAD LOTE AAAA-MM-DD</code> (la suma debe ser igual a la cantidad a recibir) <span id="loteBadge_${i}"></span></label>
            <textarea rows="2" oninput="onLoteChange(${i}, this.value)">${p.lotesTexto}</textarea>
          </div>
          <div class="cap-serie ${p.useSerial ? 'on' : ''}" id="serie_${i}">
            <label>Números de serie de <b>${p.key}</b> -- uno por línea (deben ser exactamente la cantidad a recibir) <span id="serieBadge_${i}"></span></label>
            <textarea rows="2" oninput="onSerieChange(${i}, this.value)">${p.seriesTexto}</textarea>
          </div>
        </td>
      </tr>
    `).join('');
    document.getElementById('vacio').style.display = partidasConsolidadas.length ? 'none' : 'block';
    partidasConsolidadas.forEach((p, i) => { actualizarBadgeLote(i); actualizarBadgeSerie(i); });
  }

  function onQtyChange(i, val) {
    let v = parseFloat(val) || 0;
    if (v < 0) v = 0;
    if (v > partidasConsolidadas[i].pendiente) v = partidasConsolidadas[i].pendiente;
    partidasConsolidadas[i].qty = v;
    actualizarBadgeLote(i); actualizarBadgeSerie(i);
  }
  function onLoteChange(i, val) { partidasConsolidadas[i].lotesTexto = val; actualizarBadgeLote(i); }
  function onSerieChange(i, val) { partidasConsolidadas[i].seriesTexto = val; actualizarBadgeSerie(i); }

  function parseLotes(texto) {
    return texto.replace(/\\r/g, '').split('\\n').map(l => l.trim()).filter(l => l.length > 0).map(l => {
      const partes = l.split(/\\s+/);
      const cantidad = parseFloat(partes[0]) || 0;
      const fecha = partes.length > 2 ? partes[partes.length - 1] : '';
      const lote = partes.length > 2 ? partes.slice(1, -1).join(' ') : (partes[1] || '');
      return { cantidad, lote, fecha };
    });
  }
  function actualizarBadgeLote(i) {
    const p = partidasConsolidadas[i];
    const el = document.getElementById('loteBadge_' + i);
    if (!el || !p.useLot) return;
    const lotes = parseLotes(p.lotesTexto);
    const suma = lotes.reduce((s, l) => s + l.cantidad, 0);
    const ok = lotes.length > 0 && Math.abs(suma - p.qty) < 0.001 && lotes.every(l => l.lote && l.fecha);
    el.innerHTML = ok ? '<span class="badge ok">OK</span>' : '<span class="badge falta">suma ' + suma.toFixed(2) + ' / necesario ' + p.qty.toFixed(2) + '</span>';
  }
  function actualizarBadgeSerie(i) {
    const p = partidasConsolidadas[i];
    const el = document.getElementById('serieBadge_' + i);
    if (!el || !p.useSerial) return;
    const series = p.seriesTexto.replace(/\\r/g, '').split('\\n').map(s => s.trim()).filter(s => s.length > 0);
    const necesarias = Math.round(p.qty);
    const ok = series.length === necesarias;
    el.innerHTML = ok ? '<span class="badge ok">OK</span>' : '<span class="badge falta">' + series.length + ' / ' + necesarias + '</span>';
  }

  function cancelar() {
    window.chrome.webview.postMessage(JSON.stringify({ cancelado: true }));
  }

  function crear() {
    const be = document.getElementById('proveedor').value;
    if (!be) { alert('Selecciona un proveedor.'); return; }
    const partidas = partidasConsolidadas.filter(p => p.qty > 0);
    if (partidas.length === 0) { alert('Marca al menos una Orden de Compra con partidas pendientes y captura una cantidad.'); return; }

    for (const p of partidas) {
      if (p.useLot) {
        const lotes = parseLotes(p.lotesTexto);
        const suma = lotes.reduce((s, l) => s + l.cantidad, 0);
        if (lotes.length === 0 || Math.abs(suma - p.qty) > 0.001 || lotes.some(l => !l.lote || !l.fecha)) {
          alert('"' + p.nombre + '" requiere lotes cuya suma sea igual a la cantidad a recibir, con lote y fecha de caducidad.'); return;
        }
      }
      if (p.useSerial) {
        const series = p.seriesTexto.replace(/\\r/g, '').split('\\n').map(s => s.trim()).filter(s => s.length > 0);
        if (series.length !== Math.round(p.qty)) {
          alert('"' + p.nombre + '" requiere exactamente ' + Math.round(p.qty) + ' número(s) de serie.'); return;
        }
      }
    }

    const partidasOut = partidas.map(p => ({
      pid: p.pid, cantidad: p.qty, precio: p.precio, taxTypeId: p.taxTypeId, taxPerc: p.taxPerc,
      descuentoPerc: p.descuentoPerc, unidad: p.unidad,
      useLot: p.useLot, useSerial: p.useSerial,
      lotes: p.useLot ? parseLotes(p.lotesTexto) : [],
      series: p.useSerial ? p.seriesTexto.replace(/\\r/g, '').split('\\n').map(s => s.trim()).filter(s => s.length > 0) : [],
      fuentes: p.fuentes.map(f => ({ sourceItemId: f.sourceItemId, pendiente: f.pendiente }))
    }));

    window.chrome.webview.postMessage(JSON.stringify({
      businessEntityId: parseInt(be),
      depotId: parseInt(document.getElementById('almacen').value),
      fecha: document.getElementById('fecha').value,
      comentarios: document.getElementById('comentarios').value,
      partidas: partidasOut
    }));
  }
</script>
</body>
</html>
"""

html = (html
    .replace("__OPCIONES_PROVEEDOR__", opciones_proveedor)
    .replace("__OPCIONES_ALMACEN__", opciones_almacen)
    .replace("__PROVEEDORES_JSON__", proveedores_json))

r = ctx.show_html_formulario(html, title="Nueva Recepción de Compra (WebView2)", width=1080, height=860)

if r.get("cancelado") or not r.get("submitted"):
    result = "Cancelado, no se creó ningún documento."
else:
    be2 = r.get("businessEntityId")
    dep2 = r.get("depotId")
    fecha_txt = r.get("fecha") or ""
    comentarios2 = r.get("comentarios") or ""
    partidas_recibidas = r.get("partidas") or []

    if not partidas_recibidas:
        result = "ERROR: faltan partidas."
    else:
        doc2 = ctx.erp.NuevoDocumento(184, dep2, be2)
        fecha_sql2 = ("'" + fecha_txt.replace("-", "") + "'") if fecha_txt else "GETDATE()"
        ctx.execute(
            "UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0, " +
            "CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL, DateDocument=" + fecha_sql2 + ", " +
            "Comments=N'" + comentarios2.replace("'", "''") + "' WHERE DocumentID=" + str(doc2)
        )

        supplier_id2 = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(be2))

        for p in partidas_recibidas:
            pid = int(p["pid"])
            precio = float(p["precio"])
            tax_type_id = int(p["taxTypeId"])
            descuento_perc = float(p.get("descuentoPerc", 0))
            unidad = p.get("unidad") or ""
            use_lot = bool(p.get("useLot"))
            use_serial = bool(p.get("useSerial"))
            cantidad_total = float(p["cantidad"])
            fuentes = p.get("fuentes") or []
            lotes = p.get("lotes") or []
            series = p.get("series") or []

            restante = cantidad_total
            serie_idx = 0
            lote_queue = [dict(l) for l in lotes]

            for fuente in fuentes:
                if restante <= 0.0001:
                    break
                pendiente = float(fuente["pendiente"])
                a_recibir_de_esta = min(restante, pendiente)
                if a_recibir_de_esta <= 0.0001:
                    continue

                source_item_id = int(fuente["sourceItemId"])
                # Costo = Precio Unitario en documentos de compra.
                item_id = ctx.erp.AgregarArticulo(doc2, pid, a_recibir_de_esta, precio, precio, tax_type_id, descuento_perc, source_item_id)

                if use_serial:
                    n = int(round(a_recibir_de_esta))
                    k = 0
                    while k < n and serie_idx < len(series):
                        sn = str(series[serie_idx]).replace("'", "''")
                        ctx.execute(
                            "INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, Quantity, DepotID, StatusID, CreatedOn, CreatedBy) VALUES (" +
                            str(doc2) + ", " + str(item_id) + ", " + str(pid) + ", N'" + sn + "', 1, " + str(dep2) + ", 1, GETDATE(), " + str(ctx.user_id) + ")"
                        )
                        serie_idx += 1
                        k += 1
                if use_lot:
                    falta_en_este_item = a_recibir_de_esta
                    while falta_en_este_item > 0.0001 and len(lote_queue) > 0:
                        l = lote_queue[0]
                        cant_lote = float(l["cantidad"])
                        usar = min(cant_lote, falta_en_este_item)
                        lote_nombre = str(l["lote"]).replace("'", "''")
                        fecha_lote = str(l.get("fecha") or "")
                        exp_lote = ("'" + fecha_lote.replace("-", "") + "'") if fecha_lote else "NULL"
                        ctx.execute(
                            "INSERT INTO docDocumentLot (DocumentID, DocumentItemID, ProductID, Lot, ExpirationDate, "
                            "Quantity, Unit, BaseUnit, QuantityBaseUnit, DepotID, CreatedOn, CreatedBy) VALUES (" +
                            str(doc2) + ", " + str(item_id) + ", " + str(pid) + ", N'" + lote_nombre + "', " + exp_lote + ", " +
                            str(usar) + ", N'" + unidad.replace("'", "''") + "', N'" + unidad.replace("'", "''") + "', " +
                            str(usar) + ", " + str(dep2) + ", GETDATE(), " + str(ctx.user_id) + ")"
                        )
                        restante_lote = cant_lote - usar
                        l["cantidad"] = restante_lote
                        falta_en_este_item -= usar
                        if restante_lote <= 0.0001:
                            lote_queue.pop(0)

                restante -= a_recibir_de_esta

            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id2) + ") " +
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id2) + ", 0, 3, 0)"
            )

        ctx.erp.RecalcCompleto(doc2)
        # A diferencia de la Orden de Compra: la Recepción de Compra SÍ afecta inventario.
        ctx.erp.AffectStockNEW(doc2)
        ctx.erp.Save(doc2)
        try:
            ctx.erp.UpdateStatusDelivery(doc2)
        except Exception:
            pass
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass

        result = "Recepción de compra " + str(doc2) + " creada exitosamente."
