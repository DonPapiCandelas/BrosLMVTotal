# lang: python
# ==============================================================================
# BOTON BrosLMV: Gestor de Ribbon LMV
# ==============================================================================
#
# Herramienta para organizar el ribbon de Comercial sin escribir SQL a mano cada
# vez: crear pestañas, crear secciones (grupos) dentro de una pestaña, editar un
# botón (nombre / ícono / en qué módulo aparece) y mover un botón a otra sección.
#
# ARQUITECTURA: a diferencia de los reportes (SEGUIMIENTO_OC, CUENTAS_POR_PAGAR/COBRAR)
# que usan ctx.show_html (una sola vía, sin poder recibir clics de vuelta), esta
# herramienta necesita capturar decisiones del usuario y ejecutar SQL en base a eso.
# Por eso usa ctx.form({...}) en un ciclo -- cada llamada bloquea hasta que el
# usuario responde, el script sigue corriendo Python normal entre una llamada y la
# siguiente (puede hacer ctx.query/ctx.execute en medio), y puede volver a llamar
# ctx.form() las veces que haga falta. "Ver estructura" sigue usando ctx.show_html
# porque ahi si es solo lectura.
#
# Contrato real de ctx.form (confirmado contra PLANTILLA_DISENADOR_FORMULARIOS_PYTHON.py
# y PLANTILLA_EJEMPLO_CONTEO_GRID_PYTHON.py, los dos ejemplos reales ya en produccion):
# - Cada campo va como {"name":..., "label":..., "type":..., "options":[{"value":,"label":}]}
#   -- el texto visible es "label", NO "caption" (eso solo aplica a columnas de "grid").
# - Los combos NO soportan "default" (el disenador de formularios nunca lo genera para
#   combo) -- para preseleccionar el valor actual hay que ponerlo primero en "options".
# - r["submitted"] indica si se confirmo. Los campos simples SI llegan bajo
#   r["values"][nombre_del_campo] (NO como r[nombre_del_campo] directo). Los grids
#   editables son la excepcion: llegan aparte, en r["grid_rows"].
#
# SEGURIDAD: crear pestañas/secciones es aditivo (no puede romper nada existente).
# Editar y mover botones se restringe a los que este proyecto genero
# (ControlExecute LIKE 'BrosLMV.%') para no arriesgar tocar botones nativos de
# CONTPAQi por accidente. "Ver estructura" si muestra todo, de solo lectura.
#
# Todo el SQL de este script (INSERT/UPDATE) se valido a mano en una transaccion
# de prueba (BEGIN TRANSACTION ... ROLLBACK) contra una base real antes de
# desplegar -- ver docs/MANUAL.md "Como crear un boton nuevo" en este proyecto.
from broslmv import ctx


def _int(valor, default=0):
    # ctx.query() no siempre devuelve numeros nativos (ver docs/MANUAL.md 5.1).
    if valor is None or valor == "":
        return default
    try:
        return int(float(valor))
    except (TypeError, ValueError):
        return default


def _esc(texto):
    return (texto or "").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def _actual_primero(opciones, valor_actual):
    # ctx.form no soporta "default" en combos -- se preselecciona poniendo el
    # valor actual primero en la lista de opciones.
    valor_actual = str(valor_actual)
    actual = [o for o in opciones if o["value"] == valor_actual]
    resto = [o for o in opciones if o["value"] != valor_actual]
    return actual + resto


# ---- Ver estructura (solo lectura, ctx.show_html) ----
def ver_estructura():
    filas = ctx.query("""
        SET NOCOUNT ON;
        SELECT t.RibbonTabID, t.TabCaption, t.ModuleID AS TabModuleID, t.TabOrder,
               g.RibbonGroupID, g.GroupCaption, g.GroupOrder,
               c.ControlID, c.ControlCaption, c.ControlExecute, c.IconFile, c.ModuleID AS BotonModuleID,
               m.ControlOrder
        FROM engRibbonTab t
        JOIN engRibbonGroup g ON g.RibbonTabID = t.RibbonTabID
        JOIN engRibbonMenu m ON m.RibbonGroupID = g.RibbonGroupID
        JOIN engRibbonControl c ON c.ControlID = m.ControlID
        ORDER BY t.TabOrder, t.TabCaption, g.GroupOrder, g.GroupCaption, m.ControlOrder
    """)

    tabs = {}
    for f in filas:
        tab_id = f["RibbonTabID"]
        tabs.setdefault(tab_id, {"caption": f["TabCaption"], "moduleId": _int(f["TabModuleID"]), "grupos": {}})
        grupos = tabs[tab_id]["grupos"]
        grupo_id = f["RibbonGroupID"]
        grupos.setdefault(grupo_id, {"caption": f["GroupCaption"], "botones": {}})
        # Un mismo ControlID puede tener varias filas en engRibbonMenu (instalaciones/
        # actualizaciones previas dejaron duplicados, p.ej. la Consola con 13 filas) --
        # se agrupan por ControlID y solo se cuentan, para no listarlo repetido.
        control_id = _int(f["ControlID"])
        boton = grupos[grupo_id]["botones"].setdefault(control_id, {
            "caption": f["ControlCaption"], "execute": f["ControlExecute"],
            "icon": f["IconFile"], "moduleId": _int(f["BotonModuleID"]),
            "isBros": (f["ControlExecute"] or "").startswith("BrosLMV."), "repeticiones": 0,
        })
        boton["repeticiones"] += 1

    html_tabs = []
    for tab in tabs.values():
        html_grupos = []
        for grupo in tab["grupos"].values():
            botones = list(grupo["botones"].values())
            filas_botones = "".join(
                "<tr class='{cls}'><td>{cap}{rep}</td><td><code>{exe}</code></td><td>{ic}</td><td>{mod}</td></tr>".format(
                    cls="bros" if b["isBros"] else "",
                    cap=_esc(b["caption"]), exe=_esc(b["execute"]), ic=_esc(b["icon"]),
                    mod="Global" if b["moduleId"] == 0 else f"Modulo {b['moduleId']}",
                    rep=f" <span class='chip'>×{b['repeticiones']}</span>" if b["repeticiones"] > 1 else "",
                )
                for b in botones
            )
            html_grupos.append(f"""
                <div class="grupo">
                    <div class="grupoTitulo">{_esc(grupo['caption'])} <span class="chip">{len(botones)} boton(es)</span></div>
                    <table><thead><tr><th>Nombre</th><th>ControlExecute</th><th>Icono</th><th>Aparece en</th></tr></thead>
                    <tbody>{filas_botones}</tbody></table>
                </div>""")
        html_tabs.append(f"""
            <div class="tab">
                <div class="tabTitulo">📁 {_esc(tab['caption'])} {"<span class='chip global'>Global</span>" if tab["moduleId"] == 0 else f"<span class='chip'>Modulo {tab['moduleId']}</span>"}</div>
                {"".join(html_grupos)}
            </div>""")

    html = f"""
    <html><head><meta charset="utf-8"><style>
        body {{ font-family: "Segoe UI", sans-serif; background: #f2f4f7; color: #1c2733; margin: 0; padding: 18px 22px; }}
        h1 {{ font-size: 19px; color: #0a3d6e; margin: 0 0 4px; }}
        .sub {{ font-size: 12px; color: #64748b; margin-bottom: 16px; }}
        .tab {{ background: #fff; border-radius: 10px; padding: 12px 16px; margin-bottom: 14px; box-shadow: 0 1px 3px rgba(0,0,0,.08); }}
        .tabTitulo {{ font-size: 15px; font-weight: 700; color: #0a3d6e; margin-bottom: 8px; }}
        .grupo {{ margin: 10px 0 10px 18px; }}
        .grupoTitulo {{ font-size: 13px; font-weight: 700; color: #334155; margin-bottom: 4px; }}
        .chip {{ font-size: 10px; font-weight: 700; background: #f1f5f9; color: #64748b; padding: 1px 7px; border-radius: 999px; margin-left: 6px; }}
        .chip.global {{ background: #dbeafe; color: #1d4ed8; }}
        table {{ width: 100%; border-collapse: collapse; font-size: 12px; }}
        th, td {{ padding: 5px 8px; border-bottom: 1px solid #eef1f5; text-align: left; }}
        th {{ color: #64748b; font-size: 10.5px; text-transform: uppercase; }}
        tr.bros {{ background: #eff6ff; }}
        code {{ font-size: 11px; color: #7e22ce; }}
    </style></head><body>
        <h1>Estructura del Ribbon</h1>
        <div class="sub">Filas azules = botones de BrosLMV (los únicos que "Editar" y "Mover" pueden tocar). Solo lectura.</div>
        {"".join(html_tabs)}
    </body></html>"""
    ctx.show_html(html, title="Estructura del Ribbon", width=1100, height=850, modal=False)


# ---- Crear pestaña ----
def crear_pestana():
    r = ctx.form({
        "title": "Nueva pestaña",
        "fields": [
            {"name": "caption", "label": "Nombre de la pestaña", "type": "text"},
        ],
        "ok_label": "Crear", "cancel_label": "Cancelar",
    })
    if not r["submitted"]:
        return
    caption = (r["values"]["caption"] or "").strip()
    if not caption:
        return
    nuevo_orden = _int(ctx.scalar("SELECT ISNULL(MAX(TabOrder),0)+1 FROM engRibbonTab"))
    ctx.execute("""
        INSERT INTO engRibbonTab (RibbonTabIDBase, ProductID, ModuleID, TabCaption, TabOrder,
            DRL, Color, ContextCaption, ExtraMenuModuleID, ShowIfSectionModuleIDIs, ResID, IfUserIDIs)
        VALUES (0, 1, 0, @caption, @orden, NULL, 0, NULL, 0, 0, 0, 0)
    """, {"caption": caption, "orden": nuevo_orden})
    ctx.msg(f"Pestaña \"{caption}\" creada. Reinicia Comercial para verla.", "Listo")


# ---- Crear sección (grupo) ----
def crear_seccion():
    tabs = ctx.query("SELECT RibbonTabID, TabCaption FROM engRibbonTab ORDER BY TabCaption")
    if not tabs:
        ctx.msg("No hay pestañas registradas.", "Aviso")
        return
    opciones = [{"value": str(t["RibbonTabID"]), "label": t["TabCaption"]} for t in tabs]
    r = ctx.form({
        "title": "Nueva sección",
        "fields": [
            {"name": "tab", "label": "Pestaña", "type": "combo", "options": opciones},
            {"name": "caption", "label": "Nombre de la sección", "type": "text"},
        ],
        "ok_label": "Crear", "cancel_label": "Cancelar",
    })
    if not r["submitted"]:
        return
    caption = (r["values"]["caption"] or "").strip()
    if not caption:
        return
    tab_id = _int(r["values"]["tab"])
    nuevo_orden = _int(ctx.scalar("SELECT ISNULL(MAX(GroupOrder),0)+1 FROM engRibbonGroup WHERE RibbonTabID=@t", {"t": tab_id}))
    ctx.execute("""
        INSERT INTO engRibbonGroup (RibbonGroupIDBase, RibbonTabID, GroupCaption, GroupOrder,
            ShowOptionButton, ToolTipText, IconFile, ExtraMenuModuleID, IfFieldsExist, ResID, IfUserIDIs)
        VALUES (0, @tab, @caption, @orden, 0, NULL, NULL, 0, NULL, 0, 0)
    """, {"tab": tab_id, "caption": caption, "orden": nuevo_orden})
    ctx.msg(f"Sección \"{caption}\" creada. Reinicia Comercial para verla.", "Listo")


def _elegir_boton_broslmv(titulo):
    botones = ctx.query("""
        SELECT ControlID, ControlCaption, ControlExecute, IconFile, ModuleID
        FROM engRibbonControl WHERE ControlExecute LIKE 'BrosLMV.%' ORDER BY ControlCaption
    """)
    if not botones:
        ctx.msg("No hay botones de BrosLMV registrados todavía.", "Aviso")
        return None
    opciones = [{"value": str(_int(b["ControlID"])), "label": f"{b['ControlCaption']} ({b['ControlExecute']})"} for b in botones]
    r = ctx.form({
        "title": titulo,
        "fields": [{"name": "boton", "label": "Botón", "type": "combo", "options": opciones}],
        "ok_label": "Siguiente", "cancel_label": "Cancelar",
    })
    if not r["submitted"]:
        return None
    control_id = _int(r["values"]["boton"])
    return next((b for b in botones if _int(b["ControlID"]) == control_id), None)


# ---- Editar botón (nombre / ícono / módulo) ----
def editar_boton():
    boton = _elegir_boton_broslmv("Editar botón — elige cuál")
    if not boton:
        return

    iconos = ctx.query("SELECT DISTINCT IconFile FROM engRibbonControl WHERE IconFile IS NOT NULL ORDER BY IconFile")
    opciones_iconos = _actual_primero(
        [{"value": i["IconFile"], "label": i["IconFile"]} for i in iconos],
        boton["IconFile"] or "",
    )
    modulos = ctx.query("SELECT ModuleID, ModuleName FROM engModule WHERE ModuleName IS NOT NULL ORDER BY ModuleName")
    opciones_modulos = _actual_primero(
        [{"value": "0", "label": "(Global — todos los módulos)"}] +
        [{"value": str(_int(m["ModuleID"])), "label": m["ModuleName"]} for m in modulos],
        _int(boton["ModuleID"]),
    )

    r = ctx.form({
        "title": f"Editar: {boton['ControlCaption']}",
        "fields": [
            {"name": "caption", "label": "Nombre del botón", "type": "text", "default": boton["ControlCaption"]},
            {"name": "icono", "label": "Ícono (el actual queda primero en la lista)", "type": "combo", "options": opciones_iconos},
            {"name": "modulo", "label": "Aparece en (el actual queda primero)", "type": "combo", "options": opciones_modulos},
        ],
        "ok_label": "Guardar", "cancel_label": "Cancelar",
    })
    if not r["submitted"]:
        return
    caption = (r["values"]["caption"] or "").strip()
    if not caption:
        return

    ctx.execute("""
        UPDATE engRibbonControl SET ControlCaption=@caption, ControlDescription=@caption, IconFile=@icono, ModuleID=@modulo
        WHERE ControlID=@id
    """, {"caption": caption, "icono": r["values"]["icono"], "modulo": _int(r["values"]["modulo"]), "id": _int(boton["ControlID"])})
    ctx.msg("Botón actualizado. Reinicia Comercial para ver los cambios.", "Listo")


# ---- Mover botón a otra sección ----
def mover_boton():
    boton = _elegir_boton_broslmv("Mover botón — elige cuál")
    if not boton:
        return

    grupos = ctx.query("""
        SELECT g.RibbonGroupID, g.GroupCaption, t.TabCaption
        FROM engRibbonGroup g JOIN engRibbonTab t ON t.RibbonTabID = g.RibbonTabID
        ORDER BY t.TabCaption, g.GroupCaption
    """)
    opciones = [{"value": str(_int(g["RibbonGroupID"])), "label": f"{g['TabCaption']} > {g['GroupCaption']}"} for g in grupos]
    r = ctx.form({
        "title": f"Mover: {boton['ControlCaption']}",
        "fields": [{"name": "grupo", "label": "Nueva sección", "type": "combo", "options": opciones}],
        "ok_label": "Mover", "cancel_label": "Cancelar",
    })
    if not r["submitted"]:
        return

    # Actualiza TODAS las apariciones de este boton (algunos, como la Consola,
    # tienen varias filas en engRibbonMenu por instalaciones/actualizaciones previas).
    filas = ctx.execute("UPDATE engRibbonMenu SET RibbonGroupID=@grupo WHERE ControlID=@id",
                        {"grupo": _int(r["values"]["grupo"]), "id": _int(boton["ControlID"])})
    ctx.msg(f"Botón movido ({filas} referencia(s) actualizada(s)). Reinicia Comercial para verlo.", "Listo")


# ---- Menu principal ----
try:
    while True:
        r = ctx.form({
            "title": "Gestor de Ribbon LMV",
            "fields": [
                {"name": "accion", "label": "¿Qué quieres hacer?", "type": "combo", "options": [
                    {"value": "ver", "label": "Ver estructura del ribbon"},
                    {"value": "tab", "label": "Crear nueva pestaña"},
                    {"value": "grupo", "label": "Crear nueva sección"},
                    {"value": "editar", "label": "Editar un botón (nombre / ícono / módulo)"},
                    {"value": "mover", "label": "Mover un botón a otra sección"},
                ]},
            ],
            "ok_label": "Continuar", "cancel_label": "Salir",
        })
        if not r["submitted"]:
            break

        accion = r["values"]["accion"]
        if accion == "ver":
            ver_estructura()
        elif accion == "tab":
            crear_pestana()
        elif accion == "grupo":
            crear_seccion()
        elif accion == "editar":
            editar_boton()
        elif accion == "mover":
            mover_boton()

    result = ""
except Exception as error:
    ctx.msg(f"Error en el Gestor de Ribbon:\n{error}", "Error")
    result = ""
