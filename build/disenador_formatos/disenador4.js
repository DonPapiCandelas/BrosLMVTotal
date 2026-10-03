// ---------- selector de formato (al abrir el programa sin formato, o con «Abrir…») ----------
async function abrirSelector() {
  const m = $d("dModal"); m.hidden = false;
  m.innerHTML = "<div class=caja><h3>Abrir un formato</h3><div class=cm><div class=vacio>Leyendo los formatos…</div></div></div>";
  let lista = [];
  try { lista = (await call("formatosListado")).formatos; } catch (e) { m.hidden = true; dToast(e.message, "bad"); return; }
  const pint = () => {
    const q = (($d("sBuscar") || {}).value || "").toLowerCase().trim(); let mod = null, h = "";
    lista.filter(f => !q || (f.modulo + " " + f.nombre + " " + f.archivo).toLowerCase().indexOf(q) >= 0).forEach(f => {
      if (f.modulo !== mod) { mod = f.modulo; h += "<div class=g style='padding:8px 10px 2px'>" + dEsc(mod || "(sin módulo)") + "</div>"; }
      h += "<div class=t data-id=" + f.id + "><span>" + dEsc(f.nombre) + (f.def ? " <small style='color:#B45309'>(predeterminado de Comercial)</small>" : "") + "</span><small>" + dEsc(f.archivo.split("\\").pop()) + "</small></div>";
    });
    $d("sLista").innerHTML = h || "<div class=vacio>Sin resultados</div>";
    $d("sLista").querySelectorAll(".t").forEach(t => t.onclick = () => { if (D.id && D.source !== D.base && !confirm("Hay cambios sin guardar en el formato abierto. ¿Abrir otro sin guardarlos?")) return; m.hidden = true; D.base = D.source; abrirDisenador(+t.dataset.id); });
  };
  m.innerHTML = "<div class=caja><h3>Abrir un formato</h3><div class=cm><input type=text id=sBuscar placeholder='Buscar por tipo de documento o nombre…' autocomplete=off><div class=lista id=sLista style='max-height:56vh'></div></div><div class=pie>" + (D.id ? "<button class=ib id=sCancel>Cancelar</button>" : "<button class=ib id=sSalir>Cerrar el Diseñador</button>") + "</div></div>";
  $d("sBuscar").oninput = pint; pint(); $d("sBuscar").focus();
  if ($d("sCancel")) $d("sCancel").onclick = () => { m.hidden = true; };
  if ($d("sSalir")) $d("sSalir").onclick = () => call("cerrarApp");
}

// ---------- de dónde sale una etiqueta (origen y valor con el documento elegido) ----------
async function verInfoEtiqueta(k) {
  const caja = $d("dTagInfo"); if (!caja) return;
  caja.innerHTML = "<div class=tip>Leyendo…</div>";
  const col = D.info && D.info[k];
  if (col) {
    caja.innerHTML = "<b>[" + dEsc(k) + "]</b><div class=tip>Es una <b>columna del documento</b>" + (col.r ? " (cambia en cada renglón)" : "") + ".</div><div class=tip>Sale de: <code style='word-break:break-all'>" + dEsc(col.o || "(expresión interna de Comercial)") + "</code></div><div class=tip>Con este documento vale: <b>" + dEsc(col.v === "" ? "(vacío)" : col.v) + "</b></div>";
    return;
  }
  try {
    const r = await call("refDetalle", { k: k, docId: D.docId });
    caja.innerHTML = "<b>[" + dEsc(k) + "]</b><div class=tip>" + (r.propia ? "Referencia <b>propia</b>." : "Referencia del <b>Diccionario de Comercial</b>.") + (r.desc ? " " + dEsc(r.desc) : "") + "</div>" +
      "<div class=tip>Consulta:</div><pre style='margin:0;background:#0f172a;color:#e2e8f0;border-radius:6px;padding:6px 8px;font-size:11px;white-space:pre-wrap;word-break:break-word;max-height:140px;overflow:auto'>" + dEsc(r.sql || "(vacía)") + "</pre>" +
      "<div class=tip>Con este documento vale: <b>" + dEsc(r.error ? "error: " + r.error : (r.valor === "" ? "(vacío)" : r.valor)) + "</b></div>" +
      (r.propia ? "<button class=ib id=dEditarRef style='margin-top:4px'>Editar esta referencia</button>" : "");
    if ($d("dEditarRef")) $d("dEditarRef").onclick = () => constructorReferencias({ key: k, sql: r.sql, desc: r.desc });
  } catch (e) { caja.innerHTML = "<div class=tip>" + dEsc(e.message) + "</div>"; }
}

// ---------- constructor de referencias: armar sin SQL (varias tablas y vistas) o escribir la consulta y ejecutarla ----------
async function constructorReferencias(prefill) {
  const m = $d("dModal"); m.hidden = false;
  m.innerHTML = "<div class=caja><h3>Referencia — traer datos de tablas o vistas</h3><div class=cm><div class=vacio>Leyendo las tablas…</div></div></div>";
  let tablas = [];
  try { tablas = (await call("esquemaTablas")).tablas; } catch (e) { m.hidden = true; dToast(e.message, "bad"); return; }
  const ES = D.esquema = D.esquema || {};
  const esq = async t => { if (!ES[t]) ES[t] = await call("esquemaColumnas", { tabla: t }); return ES[t]; };
  const opTablas = sel => "<optgroup label='Las más usadas'>" + tablas.filter(t => t.p).map(t => "<option" + (t.t === sel ? " selected" : "") + ">" + t.t + "</option>").join("") + "</optgroup><optgroup label='Tablas'>" + tablas.filter(t => !t.p && !t.v).map(t => "<option" + (t.t === sel ? " selected" : "") + ">" + t.t + "</option>").join("") + "</optgroup><optgroup label='Vistas'>" + tablas.filter(t => !t.p && t.v).map(t => "<option" + (t.t === sel ? " selected" : "") + ">" + t.t + "</option>").join("") + "</optgroup>";
  const B = { tab: prefill ? "sql" : "armar", fuentes: [{ t: "docDocument", alias: "a0" }], conds: [], manual: !!prefill, sql: prefill ? prefill.sql : "" };
  const OPS = [["=", "es igual a"], ["<>", "es distinto de"], [">", "es mayor que"], ["<", "es menor que"], ["LIKE", "contiene"], ["NULL", "está vacío"], ["NOTNULL", "no está vacío"]];
  const colsDe = i => (ES[B.fuentes[i].t] || { columnas: [] }).columnas.map(c => c.n);
  const todasCols = () => B.fuentes.flatMap((f, i) => colsDe(i).map(c => ({ v: f.alias + "." + c, t: f.alias + " · " + f.t + " · " + c })));
  const sqlLit = v => /^-?\d+(\.\d+)?$/.test(v) ? v : "N'" + String(v).replace(/'/g, "''") + "'";
  const rels = () => B.fuentes.flatMap(f => ((ES[f.t] || { relaciones: [] }).relaciones || []).map(r => ({ f, r })));

  function generar() {
    const col = ($d("bDato") || {}).value, agg = ($d("bAgg") || {}).value; const rel = rels()[($d("bRel") || {}).selectedIndex];
    if (!col) return "";
    const w = [];
    if (rel) {
      const a = rel.f.alias, c = rel.r.col;
      if (rel.r.via === "doc") w.push(a + ".[" + c + "]={DocumentID}"); else if (rel.r.via === "item") w.push(a + ".[" + c + "]={DocumentItemID}");
      else if (rel.r.via === "docCol") w.push(a + ".[" + c + "]=(SELECT [" + c + "] FROM docDocument WHERE DocumentID={DocumentID})");
      else if (rel.r.via === "itemCol") w.push(a + ".[" + c + "]=(SELECT [" + c + "] FROM docDocumentItem WHERE DocumentItemID={DocumentItemID})");
    }
    B.fuentes.forEach(f => { if ((ES[f.t] || {}).borrado) w.push(f.alias + ".[DeletedOn] IS NULL"); });
    document.querySelectorAll("#bConds .cond").forEach(r => {
      const c = r.querySelector(".cc").value, o = r.querySelector(".co").value, v = r.querySelector(".cv").value; if (!c) return; const [al, cn] = c.split(".");
      if (o === "NULL") w.push(al + ".[" + cn + "] IS NULL"); else if (o === "NOTNULL") w.push(al + ".[" + cn + "] IS NOT NULL"); else if (o === "LIKE") w.push(al + ".[" + cn + "] LIKE N'%" + v.replace(/'/g, "''") + "%'"); else w.push(al + ".[" + cn + "] " + o + " " + sqlLit(v));
    });
    const [al0, cn0] = col.split("."), dato = al0 + ".[" + cn0 + "]";
    let sel; if (agg === "COUNT") sel = "SELECT COUNT(*)"; else if (agg === "TEXTO") sel = "SELECT STRING_AGG(CAST(" + dato + " AS NVARCHAR(MAX)), N', ')"; else if (agg) sel = "SELECT " + agg + "(" + dato + ")"; else sel = "SELECT TOP 1 " + dato;
    let desde = " FROM [" + B.fuentes[0].t + "] AS " + B.fuentes[0].alias;
    B.fuentes.slice(1).forEach(f => { if (f.j && f.j.de && f.j.col) desde += " " + f.j.tipo + " [" + f.t + "] AS " + f.alias + " ON " + f.j.de.split(".")[0] + ".[" + f.j.de.split(".")[1] + "]=" + f.alias + ".[" + f.j.col + "]"; });
    const ordV = ($d("bOrd") || {}).value;
    return sel + desde + (w.length ? " WHERE " + w.join(" AND ") : "") + (agg || !ordV ? "" : " ORDER BY " + ordV.split(".")[0] + ".[" + ordV.split(".")[1] + "] " + $d("bDir").value);
  }
  function sincronizarSql() { if (!B.manual) { B.sql = generar(); } const t = $d("bSql"); if (t && document.activeElement !== t) t.value = B.sql; const p = $d("bSqlVista"); if (p) p.textContent = B.sql || "(elige el dato y cómo se relaciona)"; }

  function pintar() {
    const dato = ($d("bDato") || {}).value, agg = ($d("bAgg") || {}).value, relI = ($d("bRel") || {}).selectedIndex, ord = ($d("bOrd") || {}).value, dir = ($d("bDir") || {}).value;
    const prevConds = Array.from(document.querySelectorAll("#bConds .cond")).map(r => [r.querySelector(".cc").value, r.querySelector(".co").value, r.querySelector(".cv").value]);
    const nombre = ($d("bNombre") || {}).value || (prefill ? prefill.key : ""), desc = ($d("bDesc") || {}).value || (prefill ? prefill.desc : "");
    const cols = todasCols(), rl = rels();
    m.innerHTML = "<div class=caja><h3>Referencia — traer datos de tablas o vistas</h3><div class=cm>" +
      "<div class=fila><div class=f><label>Nombre de la etiqueta</label><input type=text id=bNombre placeholder='ej. NombreVendedor' value='" + dEsc(nombre) + "'" + (prefill ? " readonly" : "") + "></div><div class=f><label>Descripción (opcional)</label><input type=text id=bDesc value='" + dEsc(desc) + "'></div></div>" +
      "<div class=seg id=bTabs style='margin:6px 0'><button data-t=armar class='" + (B.tab === "armar" ? "on" : "") + "'>Armarla sin SQL</button><button data-t=sql class='" + (B.tab === "sql" ? "on" : "") + "'>Escribir SQL y ejecutarlo</button></div>" +
      "<div id=bArmar" + (B.tab === "armar" ? "" : " hidden") + ">" +
      "<div class=f><label>1 · ¿De dónde sale? <span style='font-weight:400;color:#64748b'>(tablas y vistas; puedes unir varias)</span></label><div id=bFuentes></div><button class=ib id=bMas>＋ Unir otra tabla o vista</button></div>" +
      "<div class=fila><div class=f><label>2 · ¿Qué dato?</label><select id=bDato>" + cols.map(c => "<option value='" + c.v + "'" + (c.v === dato ? " selected" : "") + ">" + dEsc(c.t) + "</option>").join("") + "</select></div>" +
      "<div class=f><label>¿Cómo?</label><select id=bAgg><option value=''>El primero que encuentre</option><option value=SUM>La suma</option><option value=COUNT>Cuántos hay</option><option value=MAX>El máximo</option><option value=MIN>El mínimo</option><option value=AVG>El promedio</option><option value=TEXTO>Unir todos (texto)</option></select></div></div>" +
      "<div class=f><label>3 · ¿Cómo se relaciona con el documento?</label><select id=bRel>" + (rl.length ? rl.map((r, i) => "<option>" + dEsc(r.f.alias + " · " + r.f.t + " — " + r.r.texto) + "</option>").join("") : "<option>(ninguna de estas tablas se relaciona con el documento: une una que sí, o escribe el SQL)</option>") + "</select></div>" +
      "<div class=f><label>4 · Condiciones extra (opcional)</label><div id=bConds></div><button class=ib id=bMasCond>＋ Agregar condición</button></div>" +
      "<div class=fila><div class=f><label>Si hay varios, ordenar por</label><select id=bOrd><option value=''>(sin orden)</option>" + cols.map(c => "<option value='" + c.v + "'" + (c.v === ord ? " selected" : "") + ">" + dEsc(c.t) + "</option>").join("") + "</select></div><div class=f><label>Orden</label><select id=bDir><option value=ASC>Ascendente</option><option value=DESC>Descendente (el último)</option></select></div></div>" +
      "<div class=f><label>Consulta que se generó</label><pre id=bSqlVista></pre></div></div>" +
      "<div id=bSqlCaja" + (B.tab === "sql" ? "" : " hidden") + "><div class=f><label>Consulta SQL <span style='font-weight:400;color:#64748b'>— una sola consulta de lectura. Usa <b>{DocumentID}</b> y <b>{DocumentItemID}</b> para el documento y la partida. Para una referencia debe regresar un solo valor.</span></label>" +
      "<textarea id=bSql rows=9 spellcheck=false style='font-family:ui-monospace,Consolas,monospace;font-size:11.5px'></textarea></div>" +
      "<div class=fila><div class=f><label>Explorar una tabla o vista</label><select id=bExpl><option value=''>(elige)</option>" + opTablas("") + "</select></div><div class=f><label>Columnas (clic para insertar)</label><div id=bExplCols style='max-height:84px;overflow:auto;border:1px solid var(--line);border-radius:7px;padding:4px 6px;font-family:ui-monospace,Consolas,monospace;font-size:11px'>—</div></div></div></div>" +
      "<div id=bRes></div></div><div class=pie><button class=ib id=bProbar>Probar el valor</button><button class=ib id=bFilas>Ver filas</button><span style='flex:1'></span><button class=ib id=bCancel>Cancelar</button><button class='ib p' id=bGuardar>Guardar referencia</button></div></div>";
    // fuentes
    $d("bFuentes").innerHTML = B.fuentes.map((f, i) => {
      const prev = B.fuentes.slice(0, i).flatMap((g, gi) => colsDe(gi).map(c => g.alias + "." + c)), mias = colsDe(i);
      return "<div class='fila' style='margin-bottom:4px;align-items:center' data-i=" + i + "><select class=ft style='flex:2'>" + opTablas(f.t) + "</select>" +
        (i === 0 ? "<span class=tip style='flex:1'>tabla principal</span>" : "<select class=fj style='flex:1.2'><option value='INNER JOIN'" + (f.j && f.j.tipo === "INNER JOIN" ? " selected" : "") + ">solo si coincide</option><option value='LEFT JOIN'" + (f.j && f.j.tipo === "LEFT JOIN" ? " selected" : "") + ">aunque no coincida</option></select>" +
          "<select class=fd style='flex:2'>" + prev.map(c => "<option" + (f.j && f.j.de === c ? " selected" : "") + ">" + c + "</option>").join("") + "</select><span>=</span><select class=fc style='flex:1.5'>" + mias.map(c => "<option" + (f.j && f.j.col === c ? " selected" : "") + ">" + c + "</option>").join("") + "</select><button class='ib fq'>✕</button>") + "</div>";
    }).join("");
    const nombresIgual = (i) => { const prev = B.fuentes.slice(0, i).flatMap((g, gi) => colsDe(gi).map(c => ({ de: g.alias + "." + c, c }))); return mias => { for (const c of mias) { const hit = prev.find(p => p.c === c); if (hit) return { de: hit.de, col: c }; } return null; }; };
    $d("bFuentes").querySelectorAll("[data-i]").forEach(row => {
      const i = +row.dataset.i, f = B.fuentes[i];
      row.querySelector(".ft").onchange = async e => { f.t = e.target.value; await esq(f.t); if (i > 0) { const s = nombresIgual(i)(colsDe(i)); f.j = s ? { tipo: "INNER JOIN", de: s.de, col: s.col } : { tipo: "INNER JOIN", de: "", col: "" }; } B.manual = false; pintar(); };
      if (i > 0) {
        const act = () => { f.j = { tipo: row.querySelector(".fj").value, de: row.querySelector(".fd").value, col: row.querySelector(".fc").value }; B.manual = false; sincronizarSql(); };
        ["fj", "fd", "fc"].forEach(c => row.querySelector("." + c).onchange = act); if (!f.j || !f.j.de) act();
        row.querySelector(".fq").onclick = () => { B.fuentes.splice(i, 1); B.fuentes.forEach((g, k) => g.alias = "a" + k); B.manual = false; pintar(); };
      }
    });
    $d("bMas").onclick = async () => {
      const t = "orgBusinessEntity"; await esq(t); const i = B.fuentes.length; B.fuentes.push({ t, alias: "a" + i });
      const s = nombresIgual(i)(colsDe(i)); B.fuentes[i].j = s ? { tipo: "INNER JOIN", de: s.de, col: s.col } : { tipo: "INNER JOIN", de: "", col: "" }; B.manual = false; pintar();
    };
    // condiciones
    const agregarCond = (v) => {
      const d0 = document.createElement("div"); d0.className = "fila cond"; d0.style.marginBottom = "4px";
      d0.innerHTML = "<select class=cc style='flex:2'>" + cols.map(c => "<option value='" + c.v + "'>" + dEsc(c.t) + "</option>").join("") + "</select><select class=co>" + OPS.map(o => "<option value='" + o[0] + "'>" + o[1] + "</option>").join("") + "</select><input type=text class=cv placeholder='valor'><button class=ib>✕</button>";
      if (v) { d0.querySelector(".cc").value = v[0]; d0.querySelector(".co").value = v[1]; d0.querySelector(".cv").value = v[2]; }
      $d("bConds").appendChild(d0); d0.querySelectorAll("select,input").forEach(x => x.onchange = () => { B.manual = false; sincronizarSql(); }); d0.querySelector("button").onclick = () => { d0.remove(); B.manual = false; sincronizarSql(); };
    };
    prevConds.forEach(agregarCond); $d("bMasCond").onclick = () => agregarCond();
    if (agg) $d("bAgg").value = agg; if (relI > 0) $d("bRel").selectedIndex = relI; if (dir) $d("bDir").value = dir;
    ["bDato", "bAgg", "bRel", "bOrd", "bDir"].forEach(i => $d(i).onchange = () => { B.manual = false; sincronizarSql(); });
    $d("bTabs").querySelectorAll("button").forEach(b => b.onclick = () => { if (b.dataset.t === "sql" && !B.manual) B.sql = generar(); B.tab = b.dataset.t; $d("bArmar").hidden = B.tab !== "armar"; $d("bSqlCaja").hidden = B.tab !== "sql"; $d("bTabs").querySelectorAll("button").forEach(x => x.classList.toggle("on", x === b)); sincronizarSql(); });
    $d("bSql").oninput = () => { B.manual = true; B.sql = $d("bSql").value; };
    $d("bExpl").onchange = async () => { const t = $d("bExpl").value; if (!t) return; const r = await esq(t); $d("bExplCols").innerHTML = r.columnas.map(c => "<span class=colx data-c='" + dEsc(c.n) + "' style='cursor:pointer;margin-right:8px' title='" + dEsc(c.t) + "'>" + dEsc(c.n) + "</span>").join(""); $d("bExplCols").querySelectorAll(".colx").forEach(x => x.onclick = () => { const ta = $d("bSql"), a = ta.selectionStart, b = ta.selectionEnd; ta.value = ta.value.slice(0, a) + "[" + t + "].[" + x.dataset.c + "]" + ta.value.slice(b); B.manual = true; B.sql = ta.value; ta.focus(); }); };
    $d("bCancel").onclick = () => { m.hidden = true; };
    const sqlActual = () => B.tab === "sql" || B.manual ? ($d("bSql").value || B.sql) : generar();
    $d("bProbar").onclick = async () => { try { const r = await call("refProbar", { sql: sqlActual(), docId: D.docId }); $d("bRes").innerHTML = r.error ? "<div class='res mal'>No se pudo: " + dEsc(r.error) + "</div>" : "<div class=res>Con el documento elegido el valor es: <b>" + dEsc(r.valor === "" ? "(vacío)" : r.valor) + "</b></div>"; } catch (e) { $d("bRes").innerHTML = "<div class='res mal'>" + dEsc(e.message) + "</div>"; } };
    $d("bFilas").onclick = async () => {
      try {
        const r = await call("refEjecutar", { sql: sqlActual(), docId: D.docId, max: 50 });
        if (r.error) { $d("bRes").innerHTML = "<div class='res mal'>No se pudo: " + dEsc(r.error) + "</div>"; return; }
        $d("bRes").innerHTML = "<div class=res>" + r.total + " fila(s)" + (r.total > r.filas.length ? " (se muestran " + r.filas.length + ")" : "") + "</div><div style='max-height:220px;overflow:auto;border:1px solid var(--line);border-radius:8px'><table style='border-collapse:collapse;width:100%;font-size:11.5px'><thead><tr>" + r.columnas.map(c => "<th style='text-align:left;padding:4px 8px;background:#f1f5f9;border-bottom:1px solid var(--line);position:sticky;top:0'>" + dEsc(c) + "</th>").join("") + "</tr></thead><tbody>" +
          r.filas.map(f => "<tr>" + f.map(v => "<td style='padding:3px 8px;border-bottom:1px solid #eef2f7;white-space:nowrap'>" + (v == null ? "<i style='color:#94a3b8'>NULL</i>" : dEsc(v)) + "</td>").join("") + "</tr>").join("") + "</tbody></table></div>";
      } catch (e) { $d("bRes").innerHTML = "<div class='res mal'>" + dEsc(e.message) + "</div>"; }
    };
    $d("bGuardar").onclick = async () => {
      const k = $d("bNombre").value.trim(); if (!k) { dToast("Ponle un nombre a la etiqueta.", "bad"); return; }
      try { await call("refGuardar", { key: k, sql: sqlActual(), desc: $d("bDesc").value }); D.etiquetas = (await call("editorEtiquetas")).etiquetas; m.hidden = true; pintarIzq(); dToast("Referencia [" + k + "] guardada. Ya está en «Referencias propias».", "ok"); }
      catch (e) { $d("bRes").innerHTML = "<div class='res mal'>" + dEsc(e.message) + "</div>"; }
    };
    sincronizarSql();
  }
  await esq("docDocument"); pintar();
}
