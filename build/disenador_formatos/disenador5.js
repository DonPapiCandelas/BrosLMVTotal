// ---------- asas sobre el elemento seleccionado: estirar con el mouse y arrastrar para reordenar ----------
// Las asas viven en la página del Diseñador (encima del lienzo), no dentro del documento: así nunca se guardan en el formato.
function asasPosicionar() {
  const cont = $d("dAsas"); if (!cont) return;
  const fr = $d("dFrame"), el = D.sel;
  if (!el || D.modo !== "diseno" || !fr.contentDocument || !el.isConnected || el.classList.contains("bros-clone") || el.tagName === "HTML" || el.tagName === "BODY" || D.editando) { cont.hidden = true; return; }
  const r = el.getBoundingClientRect(), z = D.zoom || 1;
  const l = r.left * z, t = r.top * z, w = r.width * z, h = r.height * z;
  cont.hidden = false; cont.style.left = l + "px"; cont.style.top = t + "px"; cont.style.width = w + "px"; cont.style.height = h + "px";
  const esCelda = /^(TD|TH)$/.test(el.tagName), esFila = /^(TR|THEAD|TBODY|TABLE)$/.test(el.tagName), esChip = el.classList.contains("bros-tag");
  cont.querySelector(".a-d").style.display = esFila || esChip ? "none" : "";
  cont.querySelector(".a-b").style.display = esCelda || esFila || esChip ? "none" : "";
  cont.querySelector(".a-c").style.display = esCelda || esFila || esChip ? "none" : "";
  cont.querySelector(".a-m").style.display = esCelda && false ? "none" : "";
  cont.querySelector(".a-info").textContent = Math.round(r.width) + " × " + Math.round(r.height);
}
function asasIniciar() {
  const lienzo = $d("dLienzo"); if (!lienzo || $d("dAsas")) return;
  const cont = document.createElement("div"); cont.id = "dAsas"; cont.hidden = true;
  cont.innerHTML = "<span class='a-m' title='Arrastra para mover este elemento a otro lugar'>⠿</span><span class='a-d' title='Estira el ancho'></span><span class='a-b' title='Estira el alto'></span><span class='a-c' title='Estira ancho y alto'></span><span class='a-info'></span>";
  lienzo.appendChild(cont);
  const linea = document.createElement("div"); linea.id = "dLineaDrop"; linea.hidden = true; lienzo.appendChild(linea);
  const win = () => $d("dFrame").contentWindow;

  // --- estirar ---
  const estirar = (asa, ancho, alto) => asa.addEventListener("pointerdown", ev => {
    const el = D.sel; if (!el) return; ev.preventDefault(); asa.setPointerCapture(ev.pointerId);
    const z = D.zoom || 1, r0 = el.getBoundingClientRect(), x0 = ev.clientX, y0 = ev.clientY;
    const celda = /^(TD|TH)$/.test(el.tagName), tabla = celda ? el.closest("table") : null, wTabla = tabla ? tabla.getBoundingClientRect().width : 0;
    if (!celda && win().getComputedStyle(el).display === "inline") el.style.display = "inline-block";
    const mover = e => {
      const dx = (e.clientX - x0) / z, dy = (e.clientY - y0) / z;
      if (ancho) { const nw = Math.max(12, Math.round(r0.width + dx)); el.style.width = celda && wTabla ? (Math.round(nw / wTabla * 1000) / 10) + "%" : nw + "px"; }
      if (alto) el.style.height = Math.max(8, Math.round(r0.height + dy)) + "px";
      asasPosicionar();
    };
    const fin = () => { asa.removeEventListener("pointermove", mover); asa.removeEventListener("pointerup", fin); asa.removeEventListener("pointercancel", fin); confirmar(!!el.closest("[data-bros-detail]")); };
    asa.addEventListener("pointermove", mover); asa.addEventListener("pointerup", fin); asa.addEventListener("pointercancel", fin);
  });
  estirar(cont.querySelector(".a-d"), true, false); estirar(cont.querySelector(".a-b"), false, true); estirar(cont.querySelector(".a-c"), true, true);

  // --- mover (reordenar) ---
  const m = cont.querySelector(".a-m");
  m.addEventListener("pointerdown", ev => {
    const el = D.sel; if (!el) return; ev.preventDefault(); m.setPointerCapture(ev.pointerId);
    const fr = $d("dFrame"), d = fr.contentDocument, z = D.zoom || 1; let destino = null, despues = false, dentro = false;
    const ocultar = () => { linea.hidden = true; d.querySelectorAll(".bros-drop").forEach(x => x.classList.remove("bros-drop")); };
    const mover = e => {
      const fb = fr.getBoundingClientRect(), x = (e.clientX - fb.left) / z, y = (e.clientY - fb.top) / z;
      ocultar(); destino = null;
      let t = d.elementFromPoint(x, y); if (!t) return;
      t = objetivo(t); if (!t || t === el || el.contains(t) || t.tagName === "HTML" || t.closest(".bros-clone")) return;
      if (t.tagName === "BODY") { destino = t; dentro = true; t.classList.add("bros-drop"); return; }
      const r = t.getBoundingClientRect(), vertical = (y - r.top) / Math.max(1, r.height), horizontal = (x - r.left) / Math.max(1, r.width);
      const filaFlex = t.parentElement && /flex|grid/.test(win().getComputedStyle(t.parentElement).display) && win().getComputedStyle(t.parentElement).flexDirection === "row";
      const contenedor = /^(DIV|TD|TH|SECTION|ARTICLE|LI|BODY|HEADER|FOOTER|MAIN)$/.test(t.tagName) && !t.classList.contains("bros-tag");
      destino = t; dentro = false;
      if (contenedor && vertical > 0.3 && vertical < 0.7 && horizontal > 0.15 && horizontal < 0.85) { dentro = true; t.classList.add("bros-drop"); return; }
      despues = filaFlex ? horizontal > 0.5 : vertical > 0.5;
      const lb = $d("dLienzo").getBoundingClientRect(), fx = fb.left - lb.left + $d("dLienzo").scrollLeft, fy = fb.top - lb.top + $d("dLienzo").scrollTop;
      linea.hidden = false;
      if (filaFlex) { linea.style.left = (fx + (despues ? r.right : r.left) * z - 1) + "px"; linea.style.top = (fy + r.top * z) + "px"; linea.style.width = "3px"; linea.style.height = (r.height * z) + "px"; }
      else { linea.style.left = (fx + r.left * z) + "px"; linea.style.top = (fy + (despues ? r.bottom : r.top) * z - 1) + "px"; linea.style.width = (r.width * z) + "px"; linea.style.height = "3px"; }
    };
    const fin = () => {
      m.removeEventListener("pointermove", mover); m.removeEventListener("pointerup", fin); m.removeEventListener("pointercancel", fin);
      const t = destino, enDetalle = !!el.closest("[data-bros-detail]"); ocultar();
      if (!t) return;
      try {
        if (dentro) t.appendChild(el); else if (despues) t.after(el); else t.before(el);
      } catch (e) { dToast("No se puede mover ahí: " + e.message, "bad"); return; }
      el.removeAttribute("data-bros-detail");
      confirmar(true || enDetalle);
    };
    m.addEventListener("pointermove", mover); m.addEventListener("pointerup", fin); m.addEventListener("pointercancel", fin);
  });

  window.addEventListener("resize", asasPosicionar);
  $d("dZoom").addEventListener("change", () => setTimeout(asasPosicionar, 0));
}
function asasConectarFrame() {
  const w = $d("dFrame").contentWindow; if (!w) return;
  w.addEventListener("scroll", asasPosicionar); w.addEventListener("resize", asasPosicionar);
  asasPosicionar();
}
