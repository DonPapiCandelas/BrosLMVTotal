// ---------- panel izquierdo: Campos / Elementos / Documento ----------
function pintarIzq() {
  document.querySelectorAll("#dIzqTabs button").forEach(b => b.classList.toggle("on", b.dataset.p === D.izq));
  const c = $d("dIzqCuerpo");
  if (D.izq === "campos") pintarCampos(c); else if (D.izq === "elementos") pintarElementos(c); else pintarDocumento(c);
}
function pintarCampos(c) {
  const q = (c.querySelector("#dBuscar") || {}).value || "";
  const ql = q.toLowerCase().trim(), vistos = {}, cols = [], refs = [], propias = [], info = D.info || {};
  D.etiquetas.forEach(t => { if (vistos[t.k]) return; vistos[t.k] = 1; const inf = info[t.k]; if (ql && (t.k + " " + t.d + " " + (inf ? inf.o + " " + inf.v : "")).toLowerCase().indexOf(ql) < 0) return; (t.t === "col" ? cols : (t.c ? propias : refs)).push(t); });
  const fila = t => { const inf = info[t.k], v = inf ? String(inf.v) : ""; return "<div class='tag" + (t.c ? " nueva" : "") + "' draggable=true data-k='" + dEsc(t.k) + "' title='" + dEsc((inf ? inf.o : t.d) || t.k) + "'><span>[" + dEsc(t.k) + "]</span>" +
    (inf ? "<span class=val>" + dEsc(v.length > 22 ? v.slice(0, 22) + "…" : v) + "</span>" : "") + ((inf ? inf.r : t.r) ? "<small>renglón</small>" : "") + "<i class=inf data-i='" + dEsc(t.k) + "' title='De dónde sale y cuánto vale'>ⓘ</i></div>"; };
  const g = (tit, l) => l.length ? "<div class=g>" + tit + " (" + l.length + ")</div>" + l.slice(0, 250).map(fila).join("") : "";
  c.innerHTML = "<input type=text id=dBuscar placeholder='Buscar por nombre, valor u origen…' value='" + dEsc(q) + "' autocomplete=off><button class=ib style='width:100%;margin:8px 0' id=dNuevaRef>＋ Nueva referencia (tablas, vistas o SQL)</button>" +
    "<div id=dTagInfo class=tip style='background:#f8fafc;border:1px solid var(--line);border-radius:8px;padding:6px 8px;display:" + (D.infoSel ? "block" : "none") + "'></div>" +
    "<div class=tip>Arrastra un campo al documento, o selecciona un elemento y haz clic. <b>Mayús+clic</b>: con formato numérico. <b>ⓘ</b>: de dónde sale y cuánto vale con el documento elegido.</div>" +
    (g("Referencias propias", propias) + g("Columnas del documento", cols) + g("Diccionario de referencia", refs) || "<div class=vacio>Sin resultados</div>");
  const bq = c.querySelector("#dBuscar"); bq.oninput = () => { const pos = bq.selectionStart; pintarCampos(c); const n = c.querySelector("#dBuscar"); n.focus(); n.selectionStart = n.selectionEnd = pos; };
  c.querySelector("#dNuevaRef").onclick = () => constructorReferencias();
  c.querySelectorAll(".tag").forEach(t => {
    t.addEventListener("dragstart", e => { D.drag = t.dataset.k; e.dataTransfer.setData("text/plain", "[" + t.dataset.k + "]"); e.dataTransfer.effectAllowed = "copy"; });
    t.addEventListener("dragend", () => { D.drag = null; });
    t.addEventListener("click", e => { if (e.target.classList.contains("inf")) { D.infoSel = e.target.dataset.i; $d("dTagInfo").style.display = "block"; verInfoEtiqueta(D.infoSel); return; } insertarEtiquetaEnSel(e.shiftKey ? "[Format(" + t.dataset.k + ",#,##0.00)]" : "[" + t.dataset.k + "]"); });
  });
  if (D.infoSel) verInfoEtiqueta(D.infoSel);
}
function pintarElementos(c) {
  c.innerHTML = "<div class=campo><label>Insertar</label><div class='seg' id=dDonde><button data-v=despues class=on>Después</button><button data-v=dentro>Dentro</button></div></div>" +
    "<div class=pal>" +
    "<button data-e=texto><span>T</span>Texto</button><button data-e=titulo><span>H</span>Título</button>" +
    "<button data-e=caja><span>▭</span>Caja con borde</button><button data-e=dos><span>◫</span>Dos columnas</button>" +
    "<button data-e=linea><span>―</span>Línea</button><button data-e=espacio><span>↕</span>Espacio</button>" +
    "<button data-e=logo><span>◈</span>Logo de la empresa</button><button data-e=imagen><span>🖼</span>Subir imagen…</button>" +
    "<button data-e=datos><span>≣</span>Fila etiqueta + valor</button><button data-e=total><span>Σ</span>Fila de total</button>" +
    "<button data-e=tabla><span>▦</span>Tabla 2×2</button><button data-e=salto><span>⤓</span>Salto de página</button>" +
    "</div><div class=tip style='margin-top:10px'>Para agregar una fila de totales (por ejemplo ISR): selecciona la fila de IVA, usa <b>Duplicar</b> en Propiedades, cambia el texto con doble clic y la etiqueta con doble clic sobre ella.</div>";
  c.querySelectorAll("#dDonde button").forEach(b => b.onclick = () => { c.querySelectorAll("#dDonde button").forEach(x => x.classList.remove("on")); b.classList.add("on"); });
  c.querySelectorAll(".pal button").forEach(b => b.onclick = () => insertarElemento(b.dataset.e, (c.querySelector("#dDonde .on") || {}).dataset.v || "despues"));
}

// ---------- ajustes del documento (hoja «bros-ajustes» al final del head) ----------
const PAPELES = { Letter: [216, 279], A4: [210, 297], Legal: [216, 356], Half: [140, 216] };
function leerAjustes() {
  const m = D.source.match(/\/\*BROS-AJUSTES (\{[\s\S]*?\}) \*\//);
  const base = { papel: "", orient: "portrait", mt: "", mr: "", mb: "", ml: "", fuente: "", tam: "", color: "", repetir: false, noCortar: false, marca: "", marcaOp: 8 };
  if (m) { try { return Object.assign(base, JSON.parse(m[1])); } catch (e) { } }
  return base;
}
function escribirAjustes(a) {
  let css = "/*BROS-AJUSTES " + JSON.stringify(a) + " */\n";
  const sz = a.papel ? PAPELES[a.papel] : null;
  if (a.papel || a.mt !== "" || a.mr !== "" || a.mb !== "" || a.ml !== "") {
    css += "@page{" + (a.papel ? "size:" + a.papel + " " + a.orient + ";" : "") + (a.mt !== "" || a.mr !== "" || a.mb !== "" || a.ml !== "" ? "margin:" + [a.mt, a.mr, a.mb, a.ml].map(v => (v === "" ? 12 : v) + "mm").join(" ") + ";" : "") + "}\n";
  }
  if (sz) css += "@media screen{body{width:" + (a.orient === "landscape" ? sz[1] : sz[0]) + "mm!important;min-height:" + (a.orient === "landscape" ? sz[0] : sz[1]) + "mm!important}}\n";
  if (a.fuente || a.tam || a.color) css += "body{" + (a.fuente ? "font-family:" + a.fuente + ";" : "") + (a.tam ? "font-size:" + a.tam + "pt;" : "") + (a.color ? "color:" + a.color + ";" : "") + "}\n";
  if (a.repetir) css += "thead{display:table-header-group}\n";
  if (a.noCortar) css += "tr{page-break-inside:avoid}\n";
  if (a.marca) css += "body::before{content:'" + a.marca.replace(/['\\]/g, "") + "';position:fixed;top:38%;left:8%;font-size:96px;font-weight:700;color:rgba(0,0,0," + (a.marcaOp / 100) + ");transform:rotate(-28deg);pointer-events:none;z-index:9999;white-space:nowrap}\n";
  let s = D.source.replace(/<style id="bros-ajustes">[\s\S]*?<\/style>\s*/i, "");
  s = s.replace(/<\/head>/i, '<style id="bros-ajustes">' + css + "</style>\n</head>");
  return s;
}
async function aplicarAjustes(a) { D.source = escribirAjustes(a); histPush(D.source); estadoSucio(); await refrescar(true); }

function pintarDocumento(c) {
  const a = leerAjustes(), colores = coloresDelFormato();
  const op = (v, t, sel) => "<option value='" + v + "'" + (sel === v ? " selected" : "") + ">" + t + "</option>";
  c.innerHTML = "<details open><summary>Página</summary>" +
    "<div class=campo><label>Papel</label><select id=aPapel>" + op("", "(el del formato)", a.papel) + op("Letter", "Carta", a.papel) + op("A4", "A4", a.papel) + op("Legal", "Oficio", a.papel) + op("Half", "Media carta", a.papel) + "</select></div>" +
    "<div class=campo><label>Orientación</label><select id=aOrient>" + op("portrait", "Vertical", a.orient) + op("landscape", "Horizontal", a.orient) + "</select></div>" +
    "<div class=campo><label>Márgenes mm</label><div class=dos><input type=number id=aMt placeholder='arriba' value='" + a.mt + "'><input type=number id=aMr placeholder='der.' value='" + a.mr + "'><input type=number id=aMb placeholder='abajo' value='" + a.mb + "'><input type=number id=aMl placeholder='izq.' value='" + a.ml + "'></div></div>" +
    "<div class=campo><label>Marca de agua</label><input type=text id=aMarca placeholder='COPIA, BORRADOR…' value='" + dEsc(a.marca) + "'></div></details>" +
    "<details><summary>Letra del documento</summary>" +
    "<div class=campo><label>Tipo</label><select id=aFuente>" + op("", "(la del formato)", a.fuente) + ["Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Courier New", "Consolas"].map(f => op(f, f, a.fuente)).join("") + "</select></div>" +
    "<div class=campo><label>Tamaño pt</label><input type=number id=aTam step=0.5 value='" + a.tam + "'></div>" +
    "<div class=campo><label>Color</label><input type=color id=aColor value='" + (a.color || "#1f2a37") + "'><button class=ib id=aColorQ>Quitar</button></div></details>" +
    "<details><summary>Impresión</summary><label class=sw><input type=checkbox id=aRep" + (a.repetir ? " checked" : "") + "> Repetir el encabezado de la tabla en cada página</label>" +
    "<label class=sw><input type=checkbox id=aNoc" + (a.noCortar ? " checked" : "") + "> No cortar renglones entre páginas</label></details>" +
    "<details open><summary>Colores del formato</summary><div class=tip>Cambia un color y se cambia en todo el formato.</div>" +
    colores.map((k, i) => "<div class=sw><input type=color data-c='" + i + "' value='" + k.hex + "'><span>" + k.hex + " · " + k.n + " uso(s)</span></div>").join("") + (colores.length ? "" : "<div class=tip>No se encontraron colores.</div>") + "</details>" +
    "<details><summary>CSS avanzado</summary><div class=tip>La primera hoja de estilos del formato. Se aplica al salir del cuadro.</div><textarea id=aCss rows=12 spellcheck=false style='font-family:ui-monospace,Consolas,monospace;font-size:11.5px'>" + dEsc(cssPrincipal()) + "</textarea></details>" +
    "<details><summary>JavaScript</summary><div class=tip>Funciones que se ejecutan al generar el PDF (se guardan en el formato). Biblioteca:</div>" +
    "<div class=tb style='margin-bottom:6px'><button data-js=vacios title='Oculta los elementos con data-oculta-vacio cuyo valor está vacío'>Ocultar vacíos</button><button data-js=ceros title='Oculta filas de totales con importe cero'>Ocultar totales en 0</button><button data-js=fecha title='Pone la fecha de impresión en los elementos con data-hoy'>Fecha de hoy</button></div>" +
    "<textarea id=aJs rows=10 spellcheck=false style='font-family:ui-monospace,Consolas,monospace;font-size:11.5px'>" + dEsc(jsUsuario()) + "</textarea></details>";
  const ap = () => aplicarAjustes(Object.assign(leerAjustes(), { papel: $d("aPapel").value, orient: $d("aOrient").value, mt: $d("aMt").value, mr: $d("aMr").value, mb: $d("aMb").value, ml: $d("aMl").value, marca: $d("aMarca").value, fuente: $d("aFuente").value, tam: $d("aTam").value, repetir: $d("aRep").checked, noCortar: $d("aNoc").checked }));
  ["aPapel", "aOrient", "aMt", "aMr", "aMb", "aMl", "aMarca", "aFuente", "aTam", "aRep", "aNoc"].forEach(i => $d(i).onchange = ap);
  $d("aColor").onchange = () => aplicarAjustes(Object.assign(leerAjustes(), { color: $d("aColor").value }));
  $d("aColorQ").onclick = () => aplicarAjustes(Object.assign(leerAjustes(), { color: "" }));
  c.querySelectorAll("input[data-c]").forEach(inp => inp.onchange = () => cambiarColor(colores[+inp.dataset.c], inp.value));
  $d("aCss").onchange = () => { cambiarCssPrincipal($d("aCss").value); };
  $d("aJs").onchange = () => { cambiarJsUsuario($d("aJs").value); };
  c.querySelectorAll("button[data-js]").forEach(b => b.onclick = () => { $d("aJs").value = ($d("aJs").value ? $d("aJs").value + "\n" : "") + SNIPPETS[b.dataset.js]; cambiarJsUsuario($d("aJs").value); });
}
const SNIPPETS = {
  vacios: "document.querySelectorAll('[data-oculta-vacio]').forEach(function(e){ if(!e.textContent.trim()) e.style.display='none'; });",
  ceros: "document.querySelectorAll('.tot .row').forEach(function(r){ var v=r.querySelector('.v'); if(v && /^[-$\\s]*0(\\.0+)?$/.test(v.textContent.trim())) r.remove(); });",
  fecha: "document.querySelectorAll('[data-hoy]').forEach(function(e){ e.textContent=new Date().toLocaleDateString('es-MX'); });"
};
// colores: se buscan en las hojas de estilo y en los atributos style; se reemplazan en la fuente completa
function coloresDelFormato() {
  const mapa = {};
  const re = /#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?\b|rgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)/g;
  const zonas = (D.source.match(/<style[\s\S]*?<\/style>/gi) || []).concat(D.source.match(/style\s*=\s*"[^"]*"/gi) || []);
  zonas.join("\n").replace(re, f => { const h = hexDe(f); (mapa[h] = mapa[h] || { hex: h, formas: new Set(), n: 0 }); mapa[h].formas.add(f); mapa[h].n++; return f; });
  return Object.values(mapa).sort((a, b) => b.n - a.n).slice(0, 24).map(x => ({ hex: x.hex, formas: Array.from(x.formas), n: x.n }));
}
async function cambiarColor(c, nuevo) {
  let s = D.source;
  c.formas.forEach(f => { const esc = f.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); s = s.replace(new RegExp(esc, "gi"), nuevo); });
  D.source = s; histPush(s); estadoSucio(); await refrescar(true); pintarDocumento($d("dIzqCuerpo"));
}
function cssPrincipal() { const m = D.source.match(/<style(?![^>]*bros-ajustes)[^>]*>([\s\S]*?)<\/style>/i); return m ? m[1].trim() : ""; }
async function cambiarCssPrincipal(css) {
  const m = D.source.match(/<style(?![^>]*bros-ajustes)[^>]*>[\s\S]*?<\/style>/i);
  D.source = m ? D.source.replace(m[0], m[0].replace(/>[\s\S]*<\/style>/i, ">\n" + css.replace(/\$/g, "$$$$") + "\n</style>")) : D.source.replace(/<\/head>/i, "<style>\n" + css + "\n</style>\n</head>");
  histPush(D.source); estadoSucio(); await refrescar(true);
}
function jsUsuario() { const m = D.source.match(/<script id="bros-user"[^>]*>([\s\S]*?)<\/script>/i); return m ? m[1].trim() : ""; }
async function cambiarJsUsuario(js) {
  let s = D.source.replace(/<script id="bros-user"[^>]*>[\s\S]*?<\/script>\s*/i, "");
  if (js.trim()) s = s.replace(/<\/body>/i, '<script id="bros-user">\n' + js.replace(/\$/g, "$$$$") + "\n<" + "/script>\n</body>");
  D.source = s; histPush(s); estadoSucio(); await refrescar(true);
}

// ---------- operaciones sobre la selección ----------
function celdaOCuerpo() { const d = $d("dFrame").contentDocument; return D.sel || d.body; }
function nuevoNodo(html) { const t = $d("dFrame").contentDocument.createElement("template"); t.innerHTML = html.trim(); return t.content.firstElementChild; }
async function insertarElemento(tipo, donde) {
  const d = $d("dFrame").contentDocument; let html = "", sel = D.sel || d.body;
  const flex = "display:flex;justify-content:space-between;gap:12px;padding:2px 0";
  switch (tipo) {
    case "texto": html = "<p style='margin:6px 0'>Escribe aquí el texto</p>"; break;
    case "titulo": html = "<h2 style='margin:10px 0 4px;font-size:14pt'>Título</h2>"; break;
    case "caja": html = "<div style='border:1px solid #d7dee8;border-radius:6px;padding:8px 10px;margin:6px 0'>Contenido de la caja</div>"; break;
    case "dos": html = "<div style='display:flex;gap:12px;margin:6px 0'><div style='flex:1'>Columna izquierda</div><div style='flex:1'>Columna derecha</div></div>"; break;
    case "linea": html = "<hr style='border:0;border-top:1px solid #cbd5e1;margin:8px 0'>"; break;
    case "espacio": html = "<div style='height:16px'></div>"; break;
    case "salto": html = "<div style='page-break-after:always;break-after:page'></div>"; break;
    case "datos": html = "<div style='" + flex + "'><span style='color:#64748b'>Etiqueta</span><span>[Folio]</span></div>"; break;
    case "total": html = "<div style='" + flex + ";font-weight:700'><span>Total</span><span>$[Format(Total,#,##0.00)]</span></div>"; break;
    case "tabla": html = "<table style='width:100%;border-collapse:collapse;margin:6px 0'><tbody><tr><td style='border:1px solid #cbd5e1;padding:6px'>Celda</td><td style='border:1px solid #cbd5e1;padding:6px'>Celda</td></tr><tr><td style='border:1px solid #cbd5e1;padding:6px'>Celda</td><td style='border:1px solid #cbd5e1;padding:6px'>Celda</td></tr></tbody></table>"; break;
    case "logo": html = "<img src='[LogoEmpresa]' alt='Logo' style='max-height:60px;max-width:180px'>"; break;
    case "imagen": {
      try { const r = await call("disenoLogo"); if (r.cancel) return; html = "<img src='" + r.ruta + "' alt='' style='max-height:70px;max-width:200px'>"; D.vals.attrs[r.ruta] = r.vista; } catch (e) { dToast(e.message, "bad"); return; }
      break;
    }
  }
  const n = nuevoNodo(html); if (!n) return;
  colocar(sel, n, donde); await confirmar(true); const el = elDeRutaNuevo(n); if (el) seleccionar(el);
}
let _ultimoNuevo = null;
function colocar(sel, n, donde) {
  _ultimoNuevo = n; const d = $d("dFrame").contentDocument;
  if (sel === d.body || donde === "dentro") sel.appendChild(n);
  else if (sel.closest("[data-bros-detail]") && /^(TR|TD|TH)$/.test(sel.tagName)) sel.parentNode.insertBefore(n, sel.nextSibling);
  else sel.parentNode.insertBefore(n, sel.nextSibling);
}
function elDeRutaNuevo(n) { return null; }   // tras repintar la selección se restaura sola; el elemento nuevo queda a un clic
async function eliminarSel() { const s = D.sel; if (!s) return; const p = s.parentElement; s.remove(); D.sel = p; await confirmar(true); }
async function duplicarSel() { const s = D.sel; if (!s) return; const c = s.cloneNode(true); s.parentNode.insertBefore(c, s.nextSibling); await confirmar(true); }
async function moverSel(dir) {
  const s = D.sel; if (!s) return; const h = s.parentElement; if (!h) return;
  if (dir < 0 && s.previousElementSibling) h.insertBefore(s, s.previousElementSibling);
  else if (dir > 0 && s.nextElementSibling) h.insertBefore(s.nextElementSibling, s); else return;
  await confirmar(true);
}
async function envolverSel() { const s = D.sel; if (!s) return; const w = nuevoNodo("<div style='border:1px solid #d7dee8;border-radius:6px;padding:8px 10px'></div>"); s.parentNode.insertBefore(w, s); w.appendChild(s); await confirmar(true); }
async function sacarSel() { const s = D.sel; if (!s || !s.parentElement || s.parentElement === $d("dFrame").contentDocument.body) return; const p = s.parentElement; while (p.firstChild) p.parentNode.insertBefore(p.firstChild, p); p.remove(); D.sel = null; await confirmar(true); }
async function insertarEtiquetaEnSel(tok) {
  const d = $d("dFrame").contentDocument;
  if (D.editando) { const r = d.getSelection(); if (r && r.rangeCount) { insertarChipEn(r.getRangeAt(0), tok); await confirmar(false); return; } }
  const dest = D.sel && D.sel !== d.body ? D.sel : null;
  if (!dest) { dToast("Selecciona primero dónde va el campo (un clic en el documento) o arrástralo.", "bad"); return; }
  if (/^(TABLE|TBODY|THEAD|TR|IMG)$/.test(dest.tagName)) { dToast("Selecciona una celda o un texto para poner el campo.", "bad"); return; }
  dest.appendChild(d.createTextNode(" ")); const sp = d.createElement("span"); sp.textContent = tok; dest.appendChild(sp); sp.replaceWith(d.createTextNode(tok));
  await confirmar(true);
}
function insertarChipEn(rango, tok) {
  const d = $d("dFrame").contentDocument, c = crearChip(d, tok, null);
  rango.deleteContents(); rango.insertNode(c); const sp = d.createTextNode("\u200b"); c.after(sp); rango.setStartAfter(sp); rango.collapse(true);
  const s = d.getSelection(); s.removeAllRanges(); s.addRange(rango);
  asegurarValores().then(() => { c.replaceWith(crearChip(d, tok, c.closest("[data-bros-detail]") ? 0 : null)); confirmar(false); });
}
async function soltarEtiqueta(e, k) {
  const d = $d("dFrame").contentDocument, tok = (e.shiftKey ? "[Format(" + k + ",#,##0.00)]" : "[" + k + "]");
  let r = d.caretRangeFromPoint ? d.caretRangeFromPoint(e.clientX, e.clientY) : null;
  const t = objetivo(e.target);
  if (r && r.startContainer && !(r.startContainer.parentElement && r.startContainer.parentElement.closest(".bros-tag")) && t && !/^(TABLE|TBODY|THEAD|TR|HTML)$/.test(t.tagName)) {
    const c = crearChip(d, tok, null); r.insertNode(c);
  } else if (t && !/^(TABLE|TBODY|THEAD|TR|IMG|HTML)$/.test(t.tagName)) { t.appendChild(d.createTextNode(tok)); }
  else { dToast("Suelta el campo sobre un texto o una celda.", "bad"); return; }
  const s = await (async () => { D.source = serializar(); await refrescar(true); histPush(D.source); estadoSucio(); })();
}

// operaciones de tabla
async function tablaOp(op) {
  const s = D.sel; if (!s) return; const cel = s.closest("td,th"), tabla = s.closest("table"), fila = s.closest("tr"); if (!tabla) return;
  const idx = cel ? cel.cellIndex : 0;
  if (op === "colIzq" || op === "colDer") {
    tabla.querySelectorAll("tr").forEach(tr => { const c = tr.cells[idx]; if (!c) return; const n = c.cloneNode(false); n.removeAttribute("data-bros-a-colspan"); n.textContent = c.tagName === "TH" ? "Nueva" : ""; tr.insertBefore(n, op === "colIzq" ? c : c.nextSibling); });
  } else if (op === "colDel") { tabla.querySelectorAll("tr").forEach(tr => { if (tr.cells[idx]) tr.cells[idx].remove(); }); }
  else if (op === "filaArriba" || op === "filaAbajo") { if (!fila) return; const n = fila.cloneNode(true); n.querySelectorAll("span.bros-tag").forEach(x => x.remove()); n.querySelectorAll("td,th").forEach(c => { c.textContent = ""; }); fila.parentNode.insertBefore(n, op === "filaArriba" ? fila : fila.nextSibling); }
  else if (op === "filaDel") { if (fila) fila.remove(); }
  await confirmar(true);
}

// cambiar la etiqueta de una ficha (selector con búsqueda)
function cambiarEtiquetaDialogo(chip) {
  const m = $d("dModal"); m.hidden = false;
  const actual = chip.getAttribute("data-tok");
  m.innerHTML = "<div class=caja><h3>Cambiar etiqueta</h3><div class=cm><div class=tip>Ahora: <b>" + dEsc(actual) + "</b></div><input type=text id=mBuscar placeholder='Buscar etiqueta…' autocomplete=off><div class=lista id=mLista></div></div><div class=pie><button class=ib id=mCancel>Cancelar</button></div></div>";
  const pint = () => {
    const q = $d("mBuscar").value.toLowerCase().trim(), vistos = {}, items = [];
    D.etiquetas.forEach(t => { if (vistos[t.k]) return; vistos[t.k] = 1; if (!q || (t.k + " " + t.d).toLowerCase().indexOf(q) >= 0) items.push(t); });
    $d("mLista").innerHTML = items.slice(0, 200).map(t => "<div class=t data-k='" + dEsc(t.k) + "'>[" + dEsc(t.k) + "]<small>" + dEsc((t.d || "").slice(0, 50)) + (t.r ? " · renglón" : "") + "</small></div>").join("") || "<div class=vacio>Sin resultados</div>";
    $d("mLista").querySelectorAll(".t").forEach(x => x.onclick = async () => {
      const mk = /^\[Format\(\s*[A-Za-z_0-9]+\s*,\s*([^\)]+?)\s*\)\]$/.exec(actual); const nuevo = mk ? "[Format(" + x.dataset.k + "," + mk[1] + ")]" : "[" + x.dataset.k + "]";
      chip.setAttribute("data-tok", nuevo); chip.title = nuevo; m.hidden = true; await confirmar(true);
    });
  };
  $d("mBuscar").oninput = pint; pint(); $d("mBuscar").focus(); $d("mCancel").onclick = () => { m.hidden = true; };
}
