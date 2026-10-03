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

// ---------- conexión de la ventana ----------
(function () {
  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("#dModos button").forEach(b => b.onclick = () => cambiarModo(b.dataset.m));
    document.querySelectorAll("#dIzqTabs button").forEach(b => b.onclick = () => { D.izq = b.dataset.p; pintarIzq(); });
    $d("dOtro").onclick = () => abrirSelector();
    $d("dUndo").onclick = deshacer; $d("dRedo").onclick = rehacer; $d("dGuardar").onclick = guardarDisenador; $d("dCerrar").onclick = cerrarDisenador;
    $d("dZoom").onchange = () => { D.zoom = parseFloat($d("dZoom").value); aplicarZoom(); };
    $d("dDoc").onchange = async () => { D.docId = +$d("dDoc").value; $d("dCarga").hidden = false; try { if (D.modo === "diseno") D.source = serializar(); await pedirValores(true); } catch (e) { dToast(e.message, "bad"); } if (D.modo === "diseno") await refrescar(); else if (D.modo === "final") await vistaFinal(); };
    $d("dTexto").addEventListener("input", () => { D.source = $d("dTexto").value; estadoSucio(); });
    document.addEventListener("keydown", teclaGlobal);
  });
})();
