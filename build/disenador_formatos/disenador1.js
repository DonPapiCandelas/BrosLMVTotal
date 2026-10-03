// =====================================================================================================================
// DISEÑADOR DE FORMATOS (BrosLMV). El formato HTML con [Etiquetas] es la fuente de verdad; el lienzo lo muestra con los valores de un
// documento real. Las etiquetas aparecen como «fichas» que no se pueden romper al escribir; al guardar vuelven a ser [Etiqueta].
// =====================================================================================================================
const TOK_SRC = "\\[(?:Format\\(\\s*[A-Za-z_][A-Za-z0-9_]*\\s*,\\s*[^\\)\\]]+?\\s*\\)|[A-Za-z_][A-Za-z0-9_]*)\\]";
const TOK_G = () => new RegExp(TOK_SRC, "g");
const TOK_1 = new RegExp(TOK_SRC);
const $d = id => document.getElementById(id);
const D = { id: 0, nombre: "", archivo: "", esDefault: false, docs: [], docId: 0, source: "", base: "", vals: { valores: {}, filas: [], nFilas: 0, attrs: {} },
  hist: [], hi: -1, modo: "diseno", izq: "campos", sel: null, etiquetas: [], zoom: 0.75, editando: null, ocupado: false, drag: null };

// ---------- utilidades ----------
function dEsc(s) { return String(s == null ? "" : s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
function dToast(m, k) { try { toast(m, k); } catch (e) { alert(m); } }
function hexDe(c) {
  if (!c) return "#000000"; c = c.trim();
  if (c[0] === "#") { if (c.length === 4) return "#" + c[1] + c[1] + c[2] + c[2] + c[3] + c[3]; return c.slice(0, 7).toLowerCase(); }
  const m = c.match(/rgba?\(\s*(\d+)[ ,]+(\d+)[ ,]+(\d+)(?:[ ,/]+([\d.]+))?/);
  if (!m) return "#000000";
  if (m[4] !== undefined && parseFloat(m[4]) === 0) return "#ffffff";
  return "#" + [m[1], m[2], m[3]].map(n => ("0" + parseInt(n, 10).toString(16)).slice(-2)).join("");
}
function tokensDe(texto) { return Array.from(new Set((texto.match(TOK_G()) || []))); }
function fuenteSinScripts(src) { return src.replace(/<(script|style)\b[\s\S]*?<\/\1>/gi, ""); }
function tokensCabeceraYBanda(src) {
  const s = fuenteSinScripts(src), m = s.match(/<DETAIL>([\s\S]*?)<\/DETAIL>/i);
  const fuera = m ? s.replace(m[0], "") : s;
  return { cab: tokensDe(fuera), banda: m ? tokensDe(m[1]) : [] };
}

// ---------- valores de ejemplo (los pide al servidor: el motor de etiquetas de Comercial) ----------
async function pedirValores(completo) {
  const r = await call("disenoValores", { formatId: D.id, docId: D.docId, html: completo ? D.source : "" });
  D.vals = r;
}
async function asegurarValores() {
  // Solo se piden las etiquetas nuevas (insertadas o cambiadas): no se vuelve a resolver todo el formato
  const t = tokensCabeceraYBanda(D.source), faltaCab = t.cab.filter(k => !(k in D.vals.valores)), faltaBanda = t.banda.filter(k => !(D.vals.filas[0] && k in D.vals.filas[0]));
  const attrsNuevos = [];
  if (faltaCab.length) { const r = await call("disenoValores", { formatId: D.id, docId: D.docId, tokens: faltaCab, detalle: false }); Object.assign(D.vals.valores, r.valores); }
  if (faltaBanda.length) { const r = await call("disenoValores", { formatId: D.id, docId: D.docId, tokens: faltaBanda, detalle: true }); (r.filas || []).forEach((f, i) => { D.vals.filas[i] = Object.assign(D.vals.filas[i] || {}, f); }); }
  // atributos con etiquetas que aún no se resolvieron (p. ej. un logo recién puesto)
  const reA = /([A-Za-z\-]+)\s*=\s*"([^"]*\[[A-Za-z_][^"]*)"/g; let m;
  while ((m = reA.exec(fuenteSinScripts(D.source)))) if (TOK_1.test(m[2]) && !(m[2] in D.vals.attrs)) attrsNuevos.push(m[2]);
  if (attrsNuevos.length) { const r = await call("disenoValores", { formatId: D.id, docId: D.docId, html: "<x " + attrsNuevos.map((a, i) => 'src="' + a + '"').join(" ") + ">" }); Object.assign(D.vals.attrs, r.attrs); }
}

// ---------- construir el DOM de diseño a partir de la fuente ----------
const CSS_EDITOR = ".bros-tag{background:#e6f0ff;border:1px dashed #7aa7f0;border-radius:3px;padding:0 2px;cursor:default;white-space:pre-wrap}" +
  ".bros-tag.vacio{background:#fff3d6;border-color:#e0b050;color:#8a6a1a;font-style:italic;font-size:.85em}.bros-tag.bloque{display:block;background:#eef5ff;padding:2px 4px}" +
  ".bros-hover{outline:1px dashed #2d6fe0!important;outline-offset:-1px}.bros-sel{outline:2px solid #2d6fe0!important;outline-offset:-1px}" +
  ".bros-clone{opacity:.5;pointer-events:none}[contenteditable=true]{outline:2px solid #16a34a!important;outline-offset:-1px;cursor:text}" +
  "img{cursor:default}.bros-drop{outline:2px dashed #16a34a!important}";
function preparar(source) {
  const s = source.replace(/<DETAIL>/gi, "<!--BROS-DETAIL-->").replace(/<\/DETAIL>/gi, "<!--/BROS-DETAIL-->");
  const doc = new DOMParser().parseFromString(s, "text/html");
  doc.querySelectorAll("script").forEach(sc => { sc.setAttribute("data-bros-type", sc.getAttribute("type") || ""); sc.setAttribute("type", "text/bros-inerte"); });
  const attrs = D.vals.attrs || {};
  doc.querySelectorAll("*").forEach(el => {
    Array.from(el.attributes).forEach(a => {
      if (a.name.startsWith("data-bros-") || !TOK_1.test(a.value)) return;
      el.setAttribute("data-bros-a-" + a.name, a.value);
      if (a.name !== "data-qr" && a.value in attrs) el.setAttribute(a.name, attrs[a.value]);
    });
  });
  // banda de renglones: los elementos entre los marcadores son la fila de ejemplo; se muestran copias (solo lectura) de los demás renglones
  const filas = D.vals.filas || [], total = D.vals.nFilas || filas.length;
  const w = doc.createTreeWalker(doc.body, NodeFilter.SHOW_COMMENT), ini = [];
  while (w.nextNode()) if (w.currentNode.data === "BROS-DETAIL") ini.push(w.currentNode);
  ini.forEach(c0 => {
    const band = []; let n = c0.nextSibling, fin = null;
    while (n) { if (n.nodeType === 8 && n.data === "/BROS-DETAIL") { fin = n; break; } if (n.nodeType === 1) band.push(n); n = n.nextSibling; }
    if (!fin) return;
    const clones = [];
    for (let i = 1; i < Math.min(filas.length, 3); i++) band.forEach(el => { const c = el.cloneNode(true); c.classList.add("bros-clone"); c.setAttribute("data-bros-detail", String(i)); clones.push(c); });
    band.forEach(el => el.setAttribute("data-bros-detail", "0"));
    clones.forEach(c => fin.parentNode.insertBefore(c, fin));
  });
  chipificar(doc.body, doc);
  const st = doc.createElement("style"); st.id = "bros-editor-css"; st.textContent = CSS_EDITOR; doc.head.appendChild(st);
  return "<!doctype html>" + doc.documentElement.outerHTML;
}
function valorDe(tok, fila) {
  const v = fila != null ? ((D.vals.filas[fila] || {})[tok]) : D.vals.valores[tok];
  return v == null ? "" : v;
}
function chipificar(root, doc) {
  const w = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, { acceptNode: n => (n.parentElement && /^(SCRIPT|STYLE|TEXTAREA|TITLE)$/.test(n.parentElement.tagName)) ? NodeFilter.FILTER_REJECT : TOK_1.test(n.data) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT });
  const nodos = []; while (w.nextNode()) nodos.push(w.currentNode);
  nodos.forEach(n => {
    const banda = n.parentElement && n.parentElement.closest("[data-bros-detail]");
    const fila = banda ? parseInt(banda.getAttribute("data-bros-detail"), 10) : null;
    const frag = doc.createDocumentFragment(), re = TOK_G(); let ult = 0, m;
    while ((m = re.exec(n.data))) {
      if (m.index > ult) frag.appendChild(doc.createTextNode(n.data.slice(ult, m.index)));
      frag.appendChild(crearChip(doc, m[0], fila)); ult = m.index + m[0].length;
    }
    if (ult < n.data.length) frag.appendChild(doc.createTextNode(n.data.slice(ult)));
    n.parentNode.replaceChild(frag, n);
  });
}
function crearChip(doc, tok, fila) {
  const c = doc.createElement("span"), v = valorDe(tok, fila);
  c.className = "bros-tag"; c.setAttribute("data-tok", tok); c.setAttribute("contenteditable", "false"); c.title = tok;
  if (tok === "[DesgloseImpuestos]") { c.classList.add("bloque"); c.innerHTML = v || "<i>[DesgloseImpuestos]</i>"; }
  else if (v === "") { c.classList.add("vacio"); c.textContent = tok.slice(1, -1).replace(/^Format\(\s*([A-Za-z_0-9]+).*$/, "$1"); }
  else c.textContent = v;
  return c;
}

// ---------- serializar: del lienzo de vuelta al formato con [Etiquetas] ----------
function serializar() {
  const fr = $d("dFrame"); if (!fr.contentDocument || !fr.contentDocument.documentElement) return D.source;
  const root = fr.contentDocument.documentElement.cloneNode(true), od = root.ownerDocument;
  root.querySelectorAll(".bros-clone,#bros-editor-css").forEach(n => n.remove());
  root.querySelectorAll("span.bros-tag").forEach(c => c.replaceWith(od.createTextNode(c.getAttribute("data-tok"))));
  root.querySelectorAll("[data-bros-ce]").forEach(el => { el.removeAttribute("contenteditable"); el.removeAttribute("data-bros-ce"); });
  root.querySelectorAll(".bros-sel,.bros-hover,.bros-drop").forEach(el => { el.classList.remove("bros-sel", "bros-hover", "bros-drop"); if (!el.getAttribute("class")) el.removeAttribute("class"); });
  root.querySelectorAll("*").forEach(el => {
    Array.from(el.attributes).forEach(a => { if (a.name.startsWith("data-bros-a-")) { el.setAttribute(a.name.slice(12), a.value); el.removeAttribute(a.name); } });
    el.removeAttribute("data-bros-detail");
  });
  root.querySelectorAll("script[data-bros-type]").forEach(sc => { const t = sc.getAttribute("data-bros-type"); if (t) sc.setAttribute("type", t); else sc.removeAttribute("type"); sc.removeAttribute("data-bros-type"); });
  let html = "<!doctype html>\n" + root.outerHTML;
  return html.replace(/<!--BROS-DETAIL-->/g, "<DETAIL>").replace(/<!--\/BROS-DETAIL-->/g, "</DETAIL>");
}

// ---------- historial ----------
function histPush(s) {
  if (D.hist[D.hi] === s) return;
  D.hist = D.hist.slice(0, D.hi + 1); D.hist.push(s); if (D.hist.length > 120) D.hist.shift(); D.hi = D.hist.length - 1; botonesHist();
}
function botonesHist() { $d("dUndo").disabled = D.hi <= 0; $d("dRedo").disabled = D.hi >= D.hist.length - 1; }
async function deshacer() { if (D.hi > 0) { D.hi--; D.source = D.hist[D.hi]; botonesHist(); estadoSucio(); await refrescar(); } }
async function rehacer() { if (D.hi < D.hist.length - 1) { D.hi++; D.source = D.hist[D.hi]; botonesHist(); estadoSucio(); await refrescar(); } }
function estadoSucio() { const e = $d("dEstado"), sucio = D.source !== D.base; e.textContent = sucio ? "Sin guardar ●" : "Guardado"; e.className = "estado" + (sucio ? " sucio" : ""); }

// ---------- pintar el lienzo ----------
function rutaDe(el) {
  if (!el) return null; const r = []; const d = $d("dFrame").contentDocument;
  while (el && el !== d.documentElement) {
    const p = el.parentElement; if (!p) break;
    const hijos = Array.from(p.children).filter(x => !x.classList.contains("bros-clone") && x.id !== "bros-editor-css");
    r.unshift(hijos.indexOf(el)); el = p;
  }
  return r;
}
function elDeRuta(ruta) {
  if (!ruta) return null; let el = $d("dFrame").contentDocument.documentElement;
  for (const i of ruta) { const hijos = Array.from(el.children).filter(x => !x.classList.contains("bros-clone") && x.id !== "bros-editor-css"); el = hijos[i]; if (!el) return null; }
  return el;
}
async function refrescar(conservarSel) {
  const ruta = conservarSel ? rutaDe(D.sel) : null;
  $d("dCarga").hidden = false;
  try { await asegurarValores(); } catch (e) { dToast("No se pudieron leer los valores de ejemplo: " + e.message, "bad"); }
  const fr = $d("dFrame");
  await new Promise(res => { fr.onload = () => res(); fr.srcdoc = preparar(D.source); });
  conectarLienzo(); aplicarZoom();
  D.sel = null;
  if (ruta) { const el = elDeRuta(ruta); if (el) seleccionar(el); else pintarProps(); } else pintarProps();
  pintarMigas(); asasConectarFrame();
  $d("dCarga").hidden = true;
}
function aplicarZoom() { const f = $d("dFrame"); f.style.transform = "scale(" + D.zoom + ")"; f.style.width = (100 / D.zoom) + "%"; f.style.height = (100 / D.zoom) + "%"; }

// Una edición ya hecha en el lienzo: se serializa, se guarda en el historial y (si afecta la banda de renglones) se repinta
async function confirmar(repintar) {
  const s = serializar(); D.source = s; histPush(s); estadoSucio();
  if (D.modo === "codigo") $d("dTexto").value = s;
  if (repintar) await refrescar(true); else { pintarProps(); pintarMigas(); asasPosicionar(); }
}

// ---------- interacción dentro del lienzo ----------
function conectarLienzo() {
  const d = $d("dFrame").contentDocument; if (!d) return;
  d.addEventListener("mouseover", e => { const t = objetivo(e.target); d.querySelectorAll(".bros-hover").forEach(x => x.classList.remove("bros-hover")); if (t && t !== d.body && t !== d.documentElement) t.classList.add("bros-hover"); });
  d.addEventListener("mouseout", e => { const t = objetivo(e.target); if (t) t.classList.remove("bros-hover"); });
  d.addEventListener("click", e => { if (D.editando && D.editando.contains(e.target)) return; e.preventDefault(); const t = objetivo(e.target); if (t) { terminarEdicion(); seleccionar(t); } });
  d.addEventListener("dblclick", e => { const t = objetivo(e.target); if (t && !t.classList.contains("bros-tag")) empezarEdicion(t); else if (t) cambiarEtiquetaDialogo(t); });
  d.addEventListener("keydown", teclaLienzo);
  d.addEventListener("dragover", e => { if (D.drag) { e.preventDefault(); const t = objetivo(e.target); d.querySelectorAll(".bros-drop").forEach(x => x.classList.remove("bros-drop")); if (t) t.classList.add("bros-drop"); } });
  d.addEventListener("dragleave", () => d.querySelectorAll(".bros-drop").forEach(x => x.classList.remove("bros-drop")));
  d.addEventListener("drop", e => { if (!D.drag) return; e.preventDefault(); d.querySelectorAll(".bros-drop").forEach(x => x.classList.remove("bros-drop")); soltarEtiqueta(e, D.drag); D.drag = null; });
}
function objetivo(t) { if (!t) return null; if (t.nodeType === 3) t = t.parentElement; return t && t.closest ? (t.closest(".bros-tag") || t) : t; }
function teclaLienzo(e) { teclaGlobal(e); }
function seleccionar(el) {
  const d = $d("dFrame").contentDocument; d.querySelectorAll(".bros-sel").forEach(x => x.classList.remove("bros-sel"));
  D.sel = el; if (el) el.classList.add("bros-sel"); pintarProps(); pintarMigas(); asasPosicionar();
}
function empezarEdicion(el) {
  if (el.closest("[data-bros-detail]") && el.closest(".bros-clone")) return;
  if (/^(IMG|TABLE|TBODY|THEAD|TR|HTML)$/.test(el.tagName)) return;
  terminarEdicion(); D.editando = el; el.setAttribute("contenteditable", "true"); el.setAttribute("data-bros-ce", "1"); el.focus();
  const fin = () => { el.removeEventListener("blur", fin); terminarEdicion(true); }; el.addEventListener("blur", fin);
}
function terminarEdicion(guardar) {
  const el = D.editando; if (!el) return; D.editando = null;
  el.removeAttribute("contenteditable"); el.removeAttribute("data-bros-ce");
  if (guardar !== false) confirmar(!!el.closest("[data-bros-detail]"));
}
function pintarMigas() {
  const m = $d("dMigas"); if (!D.sel) { m.innerHTML = "<span class='muted'>Haz clic en un elemento del documento para seleccionarlo · doble clic para escribir</span>"; return; }
  const d = $d("dFrame").contentDocument, cadena = []; let el = D.sel;
  while (el && el !== d.documentElement) { cadena.unshift(el); el = el.parentElement; }
  m.innerHTML = cadena.map((e, i) => "<button data-i='" + i + "' class='" + (e === D.sel ? "on" : "") + "'>" + dEsc(nombreCorto(e)) + "</button>").join("<i>›</i>");
  m.querySelectorAll("button").forEach(b => b.onclick = () => seleccionar(cadena[+b.dataset.i]));
}
function nombreCorto(e) {
  if (e.classList && e.classList.contains("bros-tag")) return e.getAttribute("data-tok");
  let n = e.tagName.toLowerCase(); const c = Array.from(e.classList || []).filter(x => !x.startsWith("bros-"))[0]; if (c) n += "." + c; return n;
}

// ---------- modos: diseño / código / vista final ----------
async function cambiarModo(m) {
  if (m === D.modo) return;
  if (D.modo === "diseno") { terminarEdicion(); D.source = serializar(); histPush(D.source); }
  else if (D.modo === "codigo") { const t = $d("dTexto").value; if (t !== D.source) { D.source = t; histPush(t); } }
  D.modo = m; estadoSucio();
  $d("dLienzo").hidden = m !== "diseno"; $d("dCodigo").hidden = m !== "codigo"; $d("dFinal").hidden = m !== "final";
  document.querySelectorAll("#dModos button").forEach(b => b.classList.toggle("on", b.dataset.m === m));
  if (m === "diseno") await refrescar(); else if (m === "codigo") { $d("dTexto").value = D.source; $d("dTexto").focus(); } else await vistaFinal();
}
async function vistaFinal() {
  const fr = $d("dFinalFrame"), fa = $d("dFaltan");
  try {
    const r = await call("editorVista", { formatId: D.id, html: D.source, docId: D.docId });
    if (r.error) { fr.srcdoc = "<body style='font:14px sans-serif;padding:30px;color:#b42318'>" + dEsc(r.error) + "</body>"; } else fr.srcdoc = r.html;
    if (r.faltan && r.faltan.length) { fa.hidden = false; fa.innerHTML = "Sin valor en este documento: " + r.faltan.map(x => "<code>[" + dEsc(x) + "]</code>").join(" ") + " — si es una referencia nueva, revisa que su consulta regrese un valor."; } else fa.hidden = true;
  } catch (e) { dToast(e.message, "bad"); }
}

// ---------- abrir / guardar / cerrar ----------
async function abrirDisenador(id) {
  asasIniciar();
  try {
    const r = await call("editorAbrir", { formatId: id });
    Object.assign(D, { id, nombre: r.modulo + " · " + r.nombre, archivo: r.archivo, esDefault: r.esDefault, docs: r.docs, docId: r.docs.length ? r.docs[0].id : 0, modo: "diseno", sel: null, hist: [], hi: -1, editando: null });
    $d("dNombre").textContent = D.nombre; $d("dNombre").title = D.archivo;
    $d("dDoc").innerHTML = r.docs.length ? r.docs.map(x => "<option value=" + x.id + ">" + dEsc(x.etiqueta) + "</option>").join("") : "<option value=0>(no hay documentos de este tipo)</option>";
    const av = $d("dAviso");
    if (r.esDefault) { av.hidden = false; av.textContent = "Este es el formato predeterminado de Comercial: al guardar también cambias cómo imprime Comercial. Se guarda una copia de respaldo del archivo original. Si prefieres no tocarlo, duplícalo desde la lista de formatos."; } else av.hidden = true;
    D.source = r.html; D.vals = { valores: {}, filas: [], nFilas: 0, attrs: {} };
    $d("dis").hidden = false; $d("dCarga").hidden = false;
    document.querySelectorAll("#dModos button").forEach(b => b.classList.toggle("on", b.dataset.m === "diseno"));
    $d("dLienzo").hidden = false; $d("dCodigo").hidden = true; $d("dFinal").hidden = true;
    if (!D.etiquetas.length) { try { D.etiquetas = (await call("editorEtiquetas")).etiquetas; } catch (e) { D.etiquetas = []; } }
    D.info = {}; if (D.docId) { try { D.info = (await call("etiquetasInfo", { docId: D.docId })).columnas || {}; } catch (e) { D.info = {}; } }
    if (D.docId) await pedirValores(true); else dToast("No hay documentos de este tipo: el diseño se verá sin datos de ejemplo.", "bad");
    await refrescar();
    D.source = serializar(); D.base = D.source; D.hist = []; D.hi = -1; histPush(D.source); estadoSucio();   // la fuente normalizada (el navegador ordena el HTML) es el punto de partida
    pintarIzq();
  } catch (e) { $d("dCarga").hidden = true; dToast(e.message, "bad"); }
}
async function guardarDisenador() {
  try {
    if (D.modo === "diseno") { terminarEdicion(false); D.source = serializar(); } else if (D.modo === "codigo") D.source = $d("dTexto").value;
    const r = await call("editorGuardar", { formatId: D.id, html: D.source });
    D.base = D.source; histPush(D.source); estadoSucio();
    dToast(r.respaldo ? "Guardado. Respaldo del original: " + r.respaldo.split("\\").pop() : "Guardado", "ok");
  } catch (e) { dToast(e.message, "bad"); }
}
function cerrarDisenador() {
  if (D.modo === "diseno") D.source = serializar(); else if (D.modo === "codigo") D.source = $d("dTexto").value;
  if (D.source !== D.base && !confirm("Hay cambios sin guardar. ¿Cerrar sin guardarlos?")) return;
  if (typeof STANDALONE !== "undefined") call("cerrarApp"); else $d("dis").hidden = true;
}
function teclaGlobal(e) {
  if ($d("dis").hidden || !$d("dModal").hidden) return;
  const k = e.key.toLowerCase(), c = e.ctrlKey || e.metaKey, tx = e.target && /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName), ed = D.editando;
  if (c && k === "s") { e.preventDefault(); guardarDisenador(); return; }
  if (c && k === "z" && !tx && !ed) { e.preventDefault(); deshacer(); return; }
  if (c && k === "y" && !tx && !ed) { e.preventDefault(); rehacer(); return; }
  if (ed) { if (k === "escape") { e.preventDefault(); terminarEdicion(true); } return; }
  if (tx || D.modo !== "diseno") { if (k === "escape" && D.modo !== "diseno" && !tx) cerrarDisenador(); return; }
  if (k === "delete" || k === "backspace") { if (D.sel && D.sel !== $d("dFrame").contentDocument.body) { e.preventDefault(); eliminarSel(); } }
  else if (c && k === "d") { e.preventDefault(); duplicarSel(); }
  else if (e.altKey && k === "arrowup") { e.preventDefault(); moverSel(-1); }
  else if (e.altKey && k === "arrowdown") { e.preventDefault(); moverSel(1); }
  else if (k === "escape") { seleccionar(null); }
}
