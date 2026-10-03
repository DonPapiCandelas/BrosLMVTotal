// ---------- panel derecho: propiedades del elemento seleccionado ----------
const FUENTES = ["", "Segoe UI", "Arial", "Helvetica", "Calibri", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Courier New", "Consolas", "sans-serif", "serif", "monospace"];
function pintarProps() {
  const c = $d("dDerCuerpo"), el = D.sel;
  if (!el) { c.innerHTML = "<div class=vacio>Selecciona un elemento del documento para cambiar su letra, colores, bordes y tamaño.<br><br>Doble clic en un texto para escribir. Doble clic en un campo para cambiarlo.</div>"; return; }
  const w = el.ownerDocument.defaultView, cs = w.getComputedStyle(el), esChip = el.classList.contains("bros-tag"), esImg = el.tagName === "IMG", enTabla = !!el.closest("table");
  const px = v => Math.round(parseFloat(v) || 0), pt = Math.round((parseFloat(cs.fontSize) || 13) * 0.75 * 2) / 2;
  const fam = (cs.fontFamily || "").split(",")[0].replace(/["']/g, "").trim();
  const on = (a, b) => a === b ? " on" : "";
  const bold = parseInt(cs.fontWeight, 10) >= 600, ital = cs.fontStyle === "italic", und = (cs.textDecorationLine || "").indexOf("underline") >= 0;
  const al = cs.textAlign === "start" ? "left" : cs.textAlign;
  let h = "<div class=tip><b>" + dEsc(esChip ? el.getAttribute("data-tok") : nombreCorto(el)) + "</b></div>";
  if (esChip) {
    const tok = el.getAttribute("data-tok"), mk = /^\[Format\(\s*([A-Za-z_0-9]+)\s*,\s*([^\)]+?)\s*\)\]$/.exec(tok);
    h += "<details open><summary>Campo</summary><button class=ib id=pCambiar style='width:100%'>Cambiar campo…</button>" +
      "<div class=campo style='margin-top:8px'><label>Formato</label><select id=pFmt><option value=''" + (mk ? "" : " selected") + ">Tal cual</option>" + ["#,##0.00", "#,##0", "0.00", "0.0000", "0", "0.00%"].map(f => "<option" + (mk && mk[2] === f ? " selected" : "") + ">" + f + "</option>").join("") + "</select></div>" +
      "<div class=tip>El formato numérico aplica a campos con números (importes, cantidades).</div></details>";
  }
  h += "<details open><summary>Texto</summary>" +
    "<div class=campo><label>Tipo</label><select id=pFam>" + FUENTES.map(f => "<option value='" + dEsc(f) + "'" + (f && fam.toLowerCase() === f.toLowerCase() ? " selected" : "") + ">" + (f || "(heredada)") + "</option>").join("") + (fam && !FUENTES.some(f => f.toLowerCase() === fam.toLowerCase()) ? "<option selected>" + dEsc(fam) + "</option>" : "") + "</select></div>" +
    "<div class=campo><label>Tamaño pt</label><input type=number id=pTam step=0.5 value='" + pt + "'></div>" +
    "<div class=campo><label>Estilo</label><div class=tb><button id=pB class='" + (bold ? "on" : "") + "' title=Negrita><b>N</b></button><button id=pI class='" + (ital ? "on" : "") + "' title=Cursiva><i>K</i></button><button id=pU class='" + (und ? "on" : "") + "' title=Subrayado><u>S</u></button></div></div>" +
    "<div class=campo><label>Alinear</label><div class=tb>" + ["left", "center", "right", "justify"].map(a => "<button data-al=" + a + " class='" + (al === a ? "on" : "") + "'>" + { left: "⇤", center: "↔", right: "⇥", justify: "≡" }[a] + "</button>").join("") + "</div></div>" +
    "<div class=campo><label>Color</label><input type=color id=pColor value='" + hexDe(cs.color) + "'></div>" +
    "<div class=campo><label>Interlineado</label><input type=number id=pLh step=0.1 value='" + (parseFloat(cs.lineHeight) ? Math.round(parseFloat(cs.lineHeight) / parseFloat(cs.fontSize) * 10) / 10 : 1.4) + "'></div></details>";
  if (!esChip) {
    h += "<details><summary>Caja</summary>" +
      "<div class=campo><label>Fondo</label><input type=color id=pBg value='" + hexDe(cs.backgroundColor) + "'><button class=ib id=pBgQ>Sin fondo</button></div>" +
      "<div class=campo><label>Relleno</label><div class=dos>" + ["Top", "Right", "Bottom", "Left"].map(l => "<input type=number data-s='padding" + l + "' value='" + px(cs["padding" + l]) + "' title='" + l + "'>").join("") + "</div></div>" +
      "<div class=campo><label>Margen</label><div class=dos>" + ["Top", "Right", "Bottom", "Left"].map(l => "<input type=number data-s='margin" + l + "' value='" + px(cs["margin" + l]) + "' title='" + l + "'>").join("") + "</div></div>" +
      "<div class=campo><label>Borde</label><div class=dos><input type=number id=pBw value='" + px(cs.borderTopWidth) + "' title=grosor><input type=color id=pBc value='" + hexDe(cs.borderTopColor) + "'></div></div>" +
      "<div class=campo><label>Esquinas</label><input type=number id=pRad value='" + px(cs.borderTopLeftRadius) + "'></div>" +
      "<div class=campo><label>Ancho</label><input type=text id=pW placeholder='auto, 50%, 120px' value='" + dEsc(el.style.width || "") + "'></div>" +
      "<div class=campo><label>Alto</label><input type=text id=pH placeholder='auto, 40px' value='" + dEsc(el.style.height || "") + "'></div></details>";
  }
  if (esImg) h += "<details open><summary>Imagen</summary><div class=campo><label>Ancho máx.</label><input type=text id=pImgW value='" + dEsc(el.style.maxWidth || "") + "' placeholder='180px'></div><div class=campo><label>Alto máx.</label><input type=text id=pImgH value='" + dEsc(el.style.maxHeight || "") + "' placeholder='60px'></div><button class=ib id=pLogoE style='width:100%;margin-top:4px'>Usar el logo de la empresa</button><button class=ib id=pLogoS style='width:100%;margin-top:4px'>Subir otra imagen…</button></details>";
  if (enTabla) h += "<details open><summary>Tabla</summary><div class=tb><button data-t=colIzq>＋ Col. izq.</button><button data-t=colDer>＋ Col. der.</button><button data-t=colDel>✕ Columna</button><button data-t=filaArriba>＋ Fila ↑</button><button data-t=filaAbajo>＋ Fila ↓</button><button data-t=filaDel>✕ Fila</button></div></details>";
  h += "<details open><summary>Estructura</summary><div class=tb><button id=pDup title='Ctrl+D'>⧉ Duplicar</button><button id=pDel title=Supr>🗑 Eliminar</button><button id=pUp title='Alt+↑'>↑</button><button id=pDn title='Alt+↓'>↓</button><button id=pWrap>▭ Envolver</button><button id=pOut>⤴ Sacar</button></div></details>";
  if (!esChip) h += "<details><summary>Avanzado</summary><div class=campo><label>Clase</label><input type=text id=pCls value='" + dEsc(Array.from(el.classList).filter(x => !x.startsWith("bros-")).join(" ")) + "'></div>" +
    "<div class=campo><label>Estilo CSS</label></div><textarea id=pCss rows=4 spellcheck=false style='font-family:ui-monospace,Consolas,monospace;font-size:11px'>" + dEsc(el.getAttribute("style") || "") + "</textarea>" +
    "<label class=sw><input type=checkbox id=pOcultaVacio" + (el.hasAttribute("data-oculta-vacio") ? " checked" : "") + "> Ocultar si su valor está vacío (usa la biblioteca de JavaScript)</label></details>";
  c.innerHTML = h; ligarProps(el, esChip);
}
function ligarProps(el, esChip) {
  const q = id => $d(id), cambio = () => confirmar(!!el.closest("[data-bros-detail]")), est = (p, v) => { el.style[p] = v; };
  if (q("pCambiar")) q("pCambiar").onclick = () => cambiarEtiquetaDialogo(el);
  if (q("pFmt")) q("pFmt").onchange = async () => {
    const tok = el.getAttribute("data-tok"), base = (/^\[(?:Format\(\s*)?([A-Za-z_0-9]+)/.exec(tok) || [])[1], f = q("pFmt").value;
    const n = f ? "[Format(" + base + "," + f + ")]" : "[" + base + "]"; el.setAttribute("data-tok", n); el.title = n; await confirmar(true);
  };
  if (q("pFam")) q("pFam").onchange = () => { est("fontFamily", q("pFam").value); cambio(); };
  if (q("pTam")) q("pTam").onchange = () => { est("fontSize", q("pTam").value + "pt"); cambio(); };
  const alt = (id, prop, a, b) => { if (q(id)) q(id).onclick = () => { const on = q(id).classList.contains("on"); est(prop, on ? b : a); cambio(); }; };
  alt("pB", "fontWeight", "700", "400"); alt("pI", "fontStyle", "italic", "normal"); alt("pU", "textDecoration", "underline", "none");
  document.querySelectorAll("#dDerCuerpo button[data-al]").forEach(b => b.onclick = () => { est("textAlign", b.dataset.al); cambio(); });
  if (q("pColor")) q("pColor").onchange = () => { est("color", q("pColor").value); cambio(); };
  if (q("pLh")) q("pLh").onchange = () => { est("lineHeight", q("pLh").value); cambio(); };
  if (q("pBg")) q("pBg").onchange = () => { est("backgroundColor", q("pBg").value); cambio(); };
  if (q("pBgQ")) q("pBgQ").onclick = () => { est("backgroundColor", ""); cambio(); };
  document.querySelectorAll("#dDerCuerpo input[data-s]").forEach(i => i.onchange = () => { est(i.dataset.s, i.value + "px"); cambio(); });
  const borde = () => { if (+q("pBw").value > 0) { est("borderStyle", "solid"); est("borderWidth", q("pBw").value + "px"); est("borderColor", q("pBc").value); } else est("border", "none"); cambio(); };
  if (q("pBw")) { q("pBw").onchange = borde; q("pBc").onchange = borde; }
  if (q("pRad")) q("pRad").onchange = () => { est("borderRadius", q("pRad").value + "px"); cambio(); };
  if (q("pW")) q("pW").onchange = () => { est("width", q("pW").value); cambio(); };
  if (q("pH")) q("pH").onchange = () => { est("height", q("pH").value); cambio(); };
  if (q("pImgW")) { q("pImgW").onchange = () => { est("maxWidth", q("pImgW").value); cambio(); }; q("pImgH").onchange = () => { est("maxHeight", q("pImgH").value); cambio(); }; }
  if (q("pLogoE")) q("pLogoE").onclick = async () => { el.setAttribute("src", "[LogoEmpresa]"); await confirmar(true); };
  if (q("pLogoS")) q("pLogoS").onclick = async () => { try { const r = await call("disenoLogo"); if (r.cancel) return; D.vals.attrs[r.ruta] = r.vista; el.setAttribute("src", r.ruta); await confirmar(true); } catch (e) { dToast(e.message, "bad"); } };
  document.querySelectorAll("#dDerCuerpo button[data-t]").forEach(b => b.onclick = () => tablaOp(b.dataset.t));
  if (q("pDup")) { q("pDup").onclick = duplicarSel; q("pDel").onclick = eliminarSel; q("pUp").onclick = () => moverSel(-1); q("pDn").onclick = () => moverSel(1); q("pWrap").onclick = envolverSel; q("pOut").onclick = sacarSel; }
  if (q("pCls")) q("pCls").onchange = () => { const k = Array.from(el.classList).filter(x => x.startsWith("bros-")); el.className = (q("pCls").value + " " + k.join(" ")).trim(); cambio(); };
  if (q("pCss")) q("pCss").onchange = () => { if (q("pCss").value.trim()) el.setAttribute("style", q("pCss").value); else el.removeAttribute("style"); cambio(); };
  if (q("pOcultaVacio")) q("pOcultaVacio").onchange = () => { if (q("pOcultaVacio").checked) el.setAttribute("data-oculta-vacio", ""); else el.removeAttribute("data-oculta-vacio"); cambio(); };
}

// ---------- constructor de referencias (traer datos de una tabla, sin escribir SQL) ----------
async function constructorReferencias() {
  const m = $d("dModal"); m.hidden = false;
  m.innerHTML = "<div class=caja><h3>Nueva referencia — traer un dato de otra tabla</h3><div class=cm><div class=vacio>Leyendo las tablas…</div></div></div>";
  let tablas = [];
  try { tablas = (await call("esquemaTablas")).tablas; } catch (e) { m.hidden = true; dToast(e.message, "bad"); return; }
  const E = { tabla: "docDocument", cols: [], rels: [], borrado: false, conds: [] };
  const pop = tablas.filter(t => t.p), resto = tablas.filter(t => !t.p);
  m.innerHTML = "<div class=caja><h3>Nueva referencia — traer un dato de otra tabla</h3><div class=cm>" +
    "<div class=tip>Una referencia es una etiqueta nueva ([MiEtiqueta]) cuyo valor sale de una consulta. Aquí la armas eligiendo, sin escribir SQL. Queda en el Diccionario de referencia y sirve en todos los formatos.</div>" +
    "<div class=fila><div class=f><label>Nombre de la etiqueta</label><input type=text id=rNombre placeholder='ej. NombreVendedor'></div><div class=f><label>Descripción (opcional)</label><input type=text id=rDesc></div></div>" +
    "<div class=fila><div class=f><label>1 · ¿De qué tabla?</label><select id=rTabla>" + "<optgroup label='Las más usadas'>" + pop.map(t => "<option>" + t.t + "</option>").join("") + "</optgroup><optgroup label='Todas'>" + resto.map(t => "<option>" + t.t + "</option>").join("") + "</optgroup></select></div>" +
    "<div class=f><label>2 · ¿Qué dato?</label><select id=rCol></select></div><div class=f><label>¿Cómo?</label><select id=rAgg><option value=''>El primero que encuentre</option><option value=SUM>La suma</option><option value=COUNT>Cuántos hay</option><option value=MAX>El máximo</option><option value=MIN>El mínimo</option><option value=AVG>El promedio</option><option value=TEXTO>Unir todos (texto)</option></select></div></div>" +
    "<div class=f><label>3 · ¿Cómo se relaciona con el documento?</label><select id=rRel></select></div>" +
    "<div class=f><label>4 · Condiciones extra (opcional)</label><div id=rConds></div><button class=ib id=rMasCond>＋ Agregar condición</button></div>" +
    "<div class=fila><div class=f><label>Si hay varios, ordenar por</label><select id=rOrd></select></div><div class=f><label>Orden</label><select id=rDir><option value=ASC>Ascendente</option><option value=DESC>Descendente (el último)</option></select></div></div>" +
    "<div class=f><label>Consulta generada <span style='font-weight:400;color:#64748b'>(puedes editarla si sabes SQL)</span></label><textarea id=rSql rows=4 spellcheck=false style='font-family:ui-monospace,Consolas,monospace;font-size:11.5px'></textarea></div>" +
    "<div id=rRes></div></div><div class=pie><button class=ib id=rProbar>Probar con el documento</button><span style='flex:1'></span><button class=ib id=rCancel>Cancelar</button><button class='ib p' id=rGuardar>Guardar referencia</button></div></div>";
  const OPS = [["=", "es igual a"], ["<>", "es distinto de"], [">", "es mayor que"], ["<", "es menor que"], ["LIKE", "contiene"], ["NULL", "está vacío"], ["NOTNULL", "no está vacío"]];
  let editadoSql = false;
  const sqlLit = v => /^-?\d+(\.\d+)?$/.test(v) ? v : "N'" + String(v).replace(/'/g, "''") + "'";
  const generar = () => {
    if (editadoSql) return; const col = $d("rCol").value, agg = $d("rAgg").value, rel = E.rels[$d("rRel").selectedIndex]; if (!col || !rel) { $d("rSql").value = ""; return; }
    const t = E.tabla, w = [];
    if (rel.via === "doc") w.push("[" + t + "].[" + rel.col + "]={DocumentID}"); else if (rel.via === "item") w.push("[" + t + "].[" + rel.col + "]={DocumentItemID}");
    else if (rel.via === "docCol") w.push("[" + t + "].[" + rel.col + "]=(SELECT [" + rel.col + "] FROM docDocument WHERE DocumentID={DocumentID})");
    else if (rel.via === "itemCol") w.push("[" + t + "].[" + rel.col + "]=(SELECT [" + rel.col + "] FROM docDocumentItem WHERE DocumentItemID={DocumentItemID})");
    if (E.borrado) w.push("[" + t + "].[DeletedOn] IS NULL");
    document.querySelectorAll("#rConds .cond").forEach(r => { const c = r.querySelector(".cc").value, o = r.querySelector(".co").value, v = r.querySelector(".cv").value; if (!c) return;
      if (o === "NULL") w.push("[" + t + "].[" + c + "] IS NULL"); else if (o === "NOTNULL") w.push("[" + t + "].[" + c + "] IS NOT NULL"); else if (o === "LIKE") w.push("[" + t + "].[" + c + "] LIKE N'%" + v.replace(/'/g, "''") + "%'"); else w.push("[" + t + "].[" + c + "] " + o + " " + sqlLit(v)); });
    const ord = $d("rOrd").value ? " ORDER BY [" + t + "].[" + $d("rOrd").value + "] " + $d("rDir").value : "";
    let sel;
    if (agg === "COUNT") sel = "SELECT COUNT(*)"; else if (agg === "TEXTO") sel = "SELECT STRING_AGG(CAST([" + t + "].[" + col + "] AS NVARCHAR(MAX)), N', ')"; else if (agg) sel = "SELECT " + agg + "([" + t + "].[" + col + "])"; else sel = "SELECT TOP 1 [" + t + "].[" + col + "]";
    $d("rSql").value = sel + " FROM [" + t + "]" + (w.length ? " WHERE " + w.join(" AND ") : "") + (agg ? "" : ord);
  };
  const cargarTabla = async () => {
    E.tabla = $d("rTabla").value; editadoSql = false;
    try { const r = await call("esquemaColumnas", { tabla: E.tabla }); E.cols = r.columnas; E.rels = r.relaciones; E.borrado = r.borrado; } catch (e) { dToast(e.message, "bad"); return; }
    $d("rCol").innerHTML = E.cols.map(c => "<option>" + c.n + "</option>").join("");
    $d("rOrd").innerHTML = "<option value=''>(sin orden)</option>" + E.cols.map(c => "<option>" + c.n + "</option>").join("");
    $d("rRel").innerHTML = E.rels.length ? E.rels.map(r => "<option>" + dEsc(r.texto) + "</option>").join("") : "<option>(esta tabla no se relaciona con el documento)</option>";
    $d("rConds").innerHTML = ""; generar();
  };
  $d("rTabla").onchange = cargarTabla; ["rCol", "rAgg", "rRel", "rOrd", "rDir"].forEach(i => $d(i).onchange = generar);
  $d("rMasCond").onclick = () => {
    const d0 = document.createElement("div"); d0.className = "fila cond"; d0.style.marginBottom = "4px";
    d0.innerHTML = "<select class=cc>" + E.cols.map(c => "<option>" + c.n + "</option>").join("") + "</select><select class=co>" + OPS.map(o => "<option value='" + o[0] + "'>" + o[1] + "</option>").join("") + "</select><input type=text class=cv placeholder='valor'><button class=ib>✕</button>";
    $d("rConds").appendChild(d0); d0.querySelectorAll("select,input").forEach(x => x.onchange = generar); d0.querySelector("button").onclick = () => { d0.remove(); generar(); };
  };
  $d("rSql").oninput = () => { editadoSql = true; };
  $d("rProbar").onclick = async () => {
    const r = await call("refProbar", { sql: $d("rSql").value, docId: D.docId });
    $d("rRes").innerHTML = r.error ? "<div class='res mal'>No se pudo: " + dEsc(r.error) + "</div>" : "<div class=res>Con el documento elegido sale: <b>" + dEsc(r.valor === "" ? "(vacío)" : r.valor) + "</b></div>";
  };
  $d("rCancel").onclick = () => { m.hidden = true; };
  $d("rGuardar").onclick = async () => {
    const k = $d("rNombre").value.trim(); if (!k) { dToast("Ponle un nombre a la etiqueta.", "bad"); return; }
    try {
      await call("refGuardar", { key: k, sql: $d("rSql").value, desc: $d("rDesc").value });
      D.etiquetas = (await call("editorEtiquetas")).etiquetas; m.hidden = true; pintarIzq();
      dToast("Referencia [" + k + "] guardada. Ya está en «Referencias propias».", "ok");
    } catch (e) { $d("rRes").innerHTML = "<div class='res mal'>" + dEsc(e.message) + "</div>"; }
  };
  $d("rTabla").value = "docDocument"; await cargarTabla();
}

// ---------- conexión de la ventana ----------
(function () {
  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("#dModos button").forEach(b => b.onclick = () => cambiarModo(b.dataset.m));
    document.querySelectorAll("#dIzqTabs button").forEach(b => b.onclick = () => { D.izq = b.dataset.p; pintarIzq(); });
    $d("dUndo").onclick = deshacer; $d("dRedo").onclick = rehacer; $d("dGuardar").onclick = guardarDisenador; $d("dCerrar").onclick = cerrarDisenador;
    $d("dZoom").onchange = () => { D.zoom = parseFloat($d("dZoom").value); aplicarZoom(); };
    $d("dDoc").onchange = async () => { D.docId = +$d("dDoc").value; $d("dCarga").hidden = false; try { if (D.modo === "diseno") D.source = serializar(); await pedirValores(true); } catch (e) { dToast(e.message, "bad"); } if (D.modo === "diseno") await refrescar(); else if (D.modo === "final") await vistaFinal(); };
    $d("dTexto").addEventListener("input", () => { D.source = $d("dTexto").value; estadoSucio(); });
    document.addEventListener("keydown", teclaGlobal);
  });
})();
