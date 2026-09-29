# lang: python
# -*- coding: utf-8 -*-
# PLANTILLA_DISENADOR_FORMULARIOS_PYTHON.py
# Disenador visual de formularios BrosLMV: una ventana HTML/CSS/JS (ctx.show_html, WebView2)
# 100% "no-code" -- se arrastran/click campos, se ve la vista previa real del formulario y
# se copia el codigo Python (ctx.form({...})) listo para pegar en un script.
#
# No hay canal de datos de regreso (ctx.show_html es de una sola via), asi que el
# resultado se entrega por portapapeles (codigo Python o JSON del diseno). El JSON
# tambien se puede volver a pegar en "Importar diseno" para seguir editando otro dia.
#
# v2.78.0 -- agrega el "Modo B": pestana nueva "Datos {DATOS}" para armar tokens tipados
# {DATOS:Tabla.Columna} (motor de v2.75.0/2.76.0/2.77.0, ver HostClient.cs
# ResolverFormularioTokens/MapDataTypeAFieldType) navegando el ESQUEMA REAL de la base
# en vivo -- catalogo de tablas/columnas, sin tener que escribir los nombres de memoria.
#
# DECISION DE ARQUITECTURA (ver docs/CHANGELOG.md 2.78.0 para el detalle): se extiende
# ESTE MISMO archivo (2 modos, 1 sola ventana) en vez de crear un archivo nuevo -- mejor
# experiencia (el usuario no tiene que saber cual de 2 herramientas abrir) y no hay
# ninguna limitacion tecnica real que obligue a separarlos.
#
# ctx.show_html_formulario (bidireccional) BLOQUEA hasta que la pagina mande UN SOLO
# postMessage -- la ventana se CIERRA en cuanto llega ese mensaje (confirmado leyendo
# RenderUiHtml en HostClient.cs: el handler de WebMessageReceived llama frm.Close()
# de inmediato). No sirve para "navegar" el catalogo de tablas/columnas en vivo mientras
# la ventana sigue abierta (eso requeriria N mensajes de ida y vuelta, uno por click).
# Por eso el Modo B usa el MISMO patron ya establecido en PLANTILLA_ORDEN_COMPRA_
# WEBVIEW2_PYTHON.py: TODO el catalogo (tablas de INFORMATION_SCHEMA.TABLES + columnas de
# INFORMATION_SCHEMA.COLUMNS, con su DATA_TYPE real) se consulta UNA vez en Python ANTES
# de abrir la ventana y se embebe comprimido (gzip+base64, mismo truco que ctx.dashboard
# / dashboard_base.html, con DecompressionStream nativo del lado JS -- sin librerias
# externas) -- toda la busqueda/filtro/seleccion de tabla y columna pasa a ser 100% del
# lado del navegador embebido, instantanea, sin una sola consulta nueva mientras el
# usuario interactua. El resultado (los tokens armados) se entrega igual que el Modo A:
# por portapapeles (ctx.show_html sigue siendo de una sola via) -- Modo B tampoco necesita
# el canal bidireccional, porque no hay nada que "guardar" en Comercial, solo texto que
# el usuario copia y pega en su propio script.

from broslmv import ctx

HTML = r"""
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<style>
  :root {
    --bg: #0A1930; --panel: #0E2540; --panel-2: #122B47; --card: #14304F;
    --line: #1D3A5C; --line-soft: #17324F; --text: #E9F0FA; --muted: #8FA6C4;
    --accent: #2D6FE0; --accent-2: #4C8AFF; --good: #37C97A; --bad: #E0563D;
    --radius: 10px; --shadow: 0 4px 16px rgba(0,0,0,.25);
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text);
    height: 100vh; overflow: hidden; display: flex; flex-direction: column; font-size: 13px;
  }
  ::-webkit-scrollbar { width: 9px; height: 9px; }
  ::-webkit-scrollbar-thumb { background: #1F4670; border-radius: 6px; }
  ::-webkit-scrollbar-track { background: transparent; }

  /* ---------- barra superior ---------- */
  .topbar {
    display: flex; align-items: center; gap: 14px; padding: 10px 18px;
    background: var(--panel); border-bottom: 1px solid var(--line); flex: none;
  }
  .topbar .marca { display: flex; flex-direction: column; margin-right: 6px; }
  .topbar .marca b { font-size: 14px; color: #fff; }
  .topbar .marca span { font-size: 10.5px; color: var(--muted); }
  .tabs { display: flex; gap: 4px; background: var(--panel-2); padding: 3px; border-radius: 8px; }
  .tabs button {
    border: none; background: transparent; color: var(--muted); padding: 7px 14px; border-radius: 6px;
    cursor: pointer; font-size: 12.5px; font-weight: 600; transition: all .15s ease;
  }
  .tabs button.activa { background: var(--accent); color: #fff; }
  .topbar .acciones { margin-left: auto; display: flex; gap: 8px; }
  .btn {
    border: 1px solid var(--line); background: var(--panel-2); color: var(--text); padding: 7px 12px;
    border-radius: 7px; font-size: 12px; cursor: pointer; display: inline-flex; align-items: center; gap: 6px;
    transition: all .15s ease;
  }
  .btn:hover { background: #1B3E63; border-color: #2A5A8C; }
  .btn.primario { background: var(--accent); border-color: var(--accent); font-weight: 600; }
  .btn.primario:hover { background: var(--accent-2); }
  .btn.peligro:hover { background: #4A2420; border-color: #7A392F; color: #FFB3A3; }
  .btn svg { width: 14px; height: 14px; flex: none; }

  /* ---------- layout principal ---------- */
  .main { flex: 1; display: flex; min-height: 0; }
  .vista { display: none; flex: 1; min-height: 0; }
  .vista.activa { display: flex; }

  .col { overflow-y: auto; }
  .col-paleta { width: 210px; background: var(--panel); border-right: 1px solid var(--line); padding: 14px; flex: none; }
  .col-lienzo { flex: 1; padding: 18px 22px; background: var(--bg); }
  .col-ajustes { width: 260px; background: var(--panel); border-left: 1px solid var(--line); padding: 16px; flex: none; }

  h2.titulo-col { font-size: 11px; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); margin: 0 0 10px; }

  /* ---------- paleta de campos (arrastrable) ---------- */
  .tipo {
    display: flex; align-items: center; gap: 10px; width: 100%; text-align: left; margin-bottom: 7px;
    padding: 9px 10px; background: var(--panel-2); color: var(--text); border: 1px solid var(--line);
    border-radius: 8px; cursor: grab; font-size: 12.5px; transition: all .15s ease;
  }
  .tipo:hover { background: #1B3E63; border-color: var(--accent-2); transform: translateX(2px); }
  .tipo .ic { width: 26px; height: 26px; border-radius: 6px; background: #0B1E33; display: flex; align-items: center; justify-content: center; flex: none; }
  .tipo .ic svg { width: 15px; height: 15px; stroke: var(--accent-2); }
  .ayuda-paleta { font-size: 11px; color: var(--muted); line-height: 1.5; margin-top: 14px; padding-top: 14px; border-top: 1px dashed var(--line); }

  /* ---------- ajustes del formulario ---------- */
  .campo-ajuste { margin-bottom: 12px; }
  .campo-ajuste label { display: block; font-size: 11px; color: var(--muted); margin-bottom: 4px; }
  .fila2 { display: flex; gap: 8px; }
  .fila2 > div { flex: 1; }
  .switch-linea { display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-top: 1px solid var(--line-soft); margin-top: 6px; }
  .switch-linea span { font-size: 11.5px; color: var(--muted); }

  input[type=text], input[type=number], input[type=date], select, textarea {
    width: 100%; padding: 7px 9px; background: #0B1E33; color: var(--text);
    border: 1px solid var(--line); border-radius: 6px; font-size: 12.5px; font-family: inherit; outline: none;
    transition: border-color .15s ease;
  }
  input:focus, select:focus, textarea:focus { border-color: var(--accent-2); }
  input:disabled, select:disabled, textarea:disabled { opacity: .55; cursor: not-allowed; }

  .toggle { position: relative; width: 34px; height: 19px; flex: none; }
  .toggle input { opacity: 0; width: 0; height: 0; }
  .toggle .rail { position: absolute; inset: 0; background: #24507D; border-radius: 20px; cursor: pointer; transition: .15s; }
  .toggle .rail::before { content: ""; position: absolute; width: 15px; height: 15px; left: 2px; top: 2px; background: #fff; border-radius: 50%; transition: .15s; }
  .toggle input:checked + .rail { background: var(--accent); }
  .toggle input:checked + .rail::before { transform: translateX(15px); }

  /* ---------- lienzo / tarjetas de campo ---------- */
  .lienzo-drop { min-height: 100%; border-radius: var(--radius); }
  .lienzo-drop.sobre { outline: 2px dashed var(--accent-2); outline-offset: 4px; background: rgba(45,111,224,.06); }
  .vacio {
    color: var(--muted); font-size: 13px; text-align: center; margin-top: 60px; border: 1.5px dashed var(--line);
    border-radius: var(--radius); padding: 40px 20px;
  }
  .vacio b { color: var(--text); display: block; margin-bottom: 4px; font-size: 14px; }

  .campo {
    background: var(--card); border: 1px solid var(--line); border-radius: var(--radius); margin-bottom: 10px;
    box-shadow: var(--shadow); overflow: hidden; transition: border-color .15s ease;
  }
  .campo.arrastrando { opacity: .35; }
  .campo.sobre-drop { border-color: var(--accent-2); }
  .campo .cab {
    display: flex; align-items: center; gap: 8px; padding: 9px 10px; cursor: grab; user-select: none;
    background: linear-gradient(180deg, #17385C, #14304F);
  }
  .campo .cab .agarre { color: #4E6E93; font-size: 13px; letter-spacing: 1px; }
  .campo .cab .ic { width: 24px; height: 24px; border-radius: 6px; background: #0B1E33; display: flex; align-items: center; justify-content: center; flex: none; }
  .campo .cab .ic svg { width: 13px; height: 13px; stroke: var(--accent-2); }
  .campo .cab .nombre-corto { flex: 1; font-weight: 600; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .campo .cab .req { color: var(--bad); font-weight: 700; }
  .campo .cab .badge { font-size: 9.5px; text-transform: uppercase; letter-spacing: .05em; color: var(--muted); }
  .campo .acciones { display: flex; gap: 2px; }
  .campo .acciones button {
    background: none; border: none; color: var(--muted); cursor: pointer; padding: 5px; border-radius: 5px;
    display: flex; align-items: center; justify-content: center;
  }
  .campo .acciones button:hover { background: #0B1E33; color: var(--text); }
  .campo .acciones button.borrar:hover { color: var(--bad); }
  .campo .acciones svg { width: 14px; height: 14px; }

  .campo .cuerpo { padding: 12px; display: none; }
  .campo.abierto .cuerpo { display: block; }
  .campo .cuerpo .fila { display: flex; gap: 8px; margin-bottom: 8px; }
  .campo .cuerpo .fila > div { flex: 1; }
  .campo .cuerpo label { display: block; font-size: 10.5px; color: var(--muted); margin-bottom: 3px; }
  .detalle-tecnico { display: none; }
  body.mostrar-tecnico .detalle-tecnico { display: block; }

  .chk { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #C7D6EA; margin-top: 6px; }
  .chk input { width: auto; }

  .opciones-lista { display: flex; flex-direction: column; gap: 6px; margin-top: 4px; }
  .opciones-lista .op { display: flex; gap: 6px; align-items: center; }
  .opciones-lista .op input { flex: 1; }
  .opciones-lista .op button { background: none; border: none; color: var(--muted); cursor: pointer; padding: 4px; }
  .opciones-lista .op button:hover { color: var(--bad); }
  .btn-mini {
    align-self: flex-start; margin-top: 4px; font-size: 11px; padding: 5px 9px; background: var(--panel-2);
    border: 1px dashed var(--line); border-radius: 6px; color: var(--muted); cursor: pointer;
  }
  .btn-mini:hover { color: var(--text); border-color: var(--accent-2); }

  /* ---------- vista previa ---------- */
  .previa-wrap { flex: 1; display: flex; align-items: flex-start; justify-content: center; padding: 30px; overflow-y: auto; background: repeating-linear-gradient(45deg, #0A1930, #0A1930 12px, #0B1E33 12px, #0B1E33 24px); }
  .previa-ventana { width: 460px; background: #F3F6FB; color: #1A2436; border-radius: 10px; box-shadow: 0 12px 40px rgba(0,0,0,.45); overflow: hidden; }
  .previa-titulo { background: #E4E9F2; padding: 10px 16px; font-weight: 600; font-size: 13px; border-bottom: 1px solid #D4DBE8; color: #29354A; }
  .previa-cuerpo { padding: 16px; max-height: 480px; overflow-y: auto; }
  .previa-campo { margin-bottom: 12px; }
  .previa-campo label { display: block; font-size: 12px; font-weight: 600; color: #29354A; margin-bottom: 4px; }
  .previa-campo label .req { color: #D24444; }
  .previa-campo input, .previa-campo select, .previa-campo textarea {
    width: 100%; padding: 6px 8px; border: 1px solid #C4CEDD; border-radius: 5px; font-size: 12.5px;
    background: #fff; color: #1A2436; font-family: inherit;
  }
  .previa-campo input:disabled, .previa-campo select:disabled, .previa-campo textarea:disabled { background: #EDEFF3; color: #7A879C; }
  .previa-botones { display: flex; justify-content: flex-end; gap: 8px; padding: 12px 16px; background: #E9EDF4; border-top: 1px solid #D4DBE8; }
  .previa-botones button { padding: 7px 16px; border-radius: 6px; font-size: 12.5px; border: 1px solid #C4CEDD; background: #fff; cursor: default; }
  .previa-botones button.ok { background: var(--accent); border-color: var(--accent); color: #fff; }
  .previa-vacio { color: var(--muted); text-align: center; margin-top: 60px; }

  /* ---------- Modo B: navegador de esquema {DATOS:Tabla.Columna} ---------- */
  .col-tablas { width: 280px; background: var(--panel); border-right: 1px solid var(--line); padding: 14px; flex: none; display: flex; flex-direction: column; min-height: 0; }
  .buscador { position: relative; margin-bottom: 10px; flex: none; }
  .buscador input { width: 100%; padding: 8px 10px; background: #0B1E33; color: var(--text); border: 1px solid var(--line); border-radius: 7px; font-size: 12.5px; outline: none; }
  .buscador input:focus { border-color: var(--accent-2); }
  .contador-tablas { font-size: 10.5px; color: var(--muted); margin-bottom: 8px; flex: none; }
  .lista-tablas { flex: 1; overflow-y: auto; min-height: 0; }
  .fila-tabla {
    display: flex; align-items: center; gap: 8px; width: 100%; text-align: left; padding: 7px 9px;
    background: var(--panel-2); color: var(--text); border: 1px solid transparent; border-radius: 7px;
    cursor: pointer; font-size: 12px; margin-bottom: 4px; transition: all .12s ease;
  }
  .fila-tabla:hover { border-color: var(--accent-2); }
  .fila-tabla.activa { background: var(--accent); border-color: var(--accent); font-weight: 600; }
  .fila-tabla .esq { font-size: 10px; color: var(--muted); }
  .fila-tabla.activa .esq { color: #D7E4FF; }
  .sin-resultados { color: var(--muted); font-size: 12px; text-align: center; padding: 20px 8px; }

  .col-columnas { flex: 1; padding: 18px 22px; overflow-y: auto; }
  .col-canasta { width: 320px; background: var(--panel); border-left: 1px solid var(--line); padding: 16px; flex: none; overflow-y: auto; display: flex; flex-direction: column; }
  .titulo-tabla-activa { font-size: 15px; font-weight: 700; color: #fff; margin-bottom: 2px; }
  .subt-tabla-activa { font-size: 11.5px; color: var(--muted); margin-bottom: 14px; }
  .tabla-cols { width: 100%; border-collapse: collapse; }
  .tabla-cols th { text-align: left; font-size: 10.5px; text-transform: uppercase; letter-spacing: .05em; color: var(--muted); padding: 6px 8px; border-bottom: 1px solid var(--line); }
  .tabla-cols td { padding: 7px 8px; border-bottom: 1px solid var(--line-soft); font-size: 12.5px; vertical-align: middle; }
  .tabla-cols tr.marcada { background: rgba(45,111,224,.10); }
  .nombre-col-sql { font-family: Consolas, monospace; font-size: 12px; }
  .tipo-sql-real { font-family: Consolas, monospace; font-size: 10.5px; color: var(--muted); }
  .pill-tipo { display: inline-flex; align-items: center; gap: 4px; font-size: 10px; text-transform: uppercase; letter-spacing: .03em; background: #0B1E33; color: var(--accent-2); border-radius: 20px; padding: 2px 8px; }
  .vacio-tablas { color: var(--muted); text-align: center; margin-top: 60px; }

  .item-canasta { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 9px 10px; margin-bottom: 8px; }
  .item-canasta .tit { display: flex; align-items: center; gap: 6px; margin-bottom: 6px; }
  .item-canasta .tit .tok { font-family: Consolas, monospace; font-size: 11px; color: var(--accent-2); flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .item-canasta .tit button { background: none; border: none; color: var(--muted); cursor: pointer; padding: 2px; }
  .item-canasta .tit button:hover { color: var(--bad); }
  .item-canasta input[type=text] { width: 100%; margin-bottom: 5px; padding: 5px 7px; font-size: 11.5px; }
  .canasta-vacia { color: var(--muted); font-size: 11.5px; text-align: center; padding: 30px 10px; border: 1.5px dashed var(--line); border-radius: 8px; }

  .snippet-tabs { display: flex; gap: 4px; margin-bottom: 10px; }
  .snippet-tabs button { border: 1px solid var(--line); background: var(--panel-2); color: var(--muted); padding: 6px 12px; border-radius: 7px 7px 0 0; font-size: 12px; cursor: pointer; }
  .snippet-tabs button.activa { background: #08172A; color: var(--text); border-color: var(--accent-2); font-weight: 600; }
  .snippet-bloque { display: none; }
  .snippet-bloque.activa { display: block; }

  /* ---------- codigo ---------- */
  .col-codigo { flex: 1; display: flex; padding: 18px; gap: 16px; }
  .codigo-principal { flex: 1; display: flex; flex-direction: column; min-width: 0; }
  .codigo-lateral { width: 300px; display: flex; flex-direction: column; gap: 14px; flex: none; }
  .panel { background: var(--panel-2); border: 1px solid var(--line); border-radius: var(--radius); padding: 14px; }
  .panel h3 { margin: 0 0 8px; font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: .05em; }
  pre#codigo {
    flex: 1; background: #08172A; color: #B7E37B; font-family: Consolas, 'Courier New', monospace; font-size: 12px;
    line-height: 1.5; padding: 14px; border-radius: var(--radius); overflow: auto; white-space: pre-wrap;
    margin: 0 0 10px; border: 1px solid var(--line);
  }
  .tok-kw { color: #79B8FF; } .tok-str { color: #B7E37B; } .tok-num { color: #F2C868; } .tok-com { color: #5E7CA0; font-style: italic; }
  #json-import { width: 100%; height: 90px; resize: vertical; font-family: Consolas, monospace; font-size: 11px; }
  .aviso { font-size: 11.5px; text-align: center; margin-top: 6px; height: 14px; color: var(--good); }
</style>
</head>
<body class="mostrar-tecnico">
<script>window.__esquemaB64 = "__BROSLMV_ESQUEMA_B64__";</script>

  <div class="topbar">
    <div class="marca"><b>Disenador de Formularios</b><span>BrosLMV -- ctx.form()</span></div>
    <div class="tabs">
      <button class="activa" data-tab="disenar" onclick="cambiarTab('disenar')">Disenar (ctx.form)</button>
      <button data-tab="datos" onclick="cambiarTab('datos')">Datos {DATOS}</button>
      <button data-tab="previa" onclick="cambiarTab('previa')">Vista previa</button>
      <button data-tab="codigo" onclick="cambiarTab('codigo')">Codigo</button>
    </div>
    <div class="acciones">
      <button class="btn" onclick="alternarTecnico()" id="btn-tecnico">Ocultar detalles tecnicos</button>
      <button class="btn peligro" onclick="nuevoFormulario()">Nuevo</button>
    </div>
  </div>

  <div class="main">

    <!-- ===================== DISENAR ===================== -->
    <div class="vista activa" id="vista-disenar">
      <div class="col col-paleta">
        <h2 class="titulo-col">Agregar campo</h2>
        <div id="paleta"></div>
        <div class="ayuda-paleta">Arrastra un tipo de campo hacia el lienzo, o dale clic para agregarlo al final.</div>
      </div>

      <div class="col col-lienzo">
        <h2 class="titulo-col">Lienzo del formulario</h2>
        <div class="lienzo-drop" id="lienzo">
          <div id="lista-campos"></div>
          <div id="vacio" class="vacio"><b>Aun no hay campos</b>Arrastra o haz clic en un tipo de campo desde la izquierda.</div>
        </div>
      </div>

      <div class="col col-ajustes">
        <h2 class="titulo-col">Ajustes del formulario</h2>
        <div class="campo-ajuste"><label>Titulo de la ventana</label>
          <input type="text" id="f-title" value="Datos" oninput="actualizarCodigo()"></div>
        <div class="fila2">
          <div class="campo-ajuste"><label>Boton aceptar</label><input type="text" id="f-ok" value="Aceptar" oninput="actualizarCodigo()"></div>
          <div class="campo-ajuste"><label>Boton cancelar</label><input type="text" id="f-cancel" value="Cancelar" oninput="actualizarCodigo()"></div>
        </div>
        <div class="fila2">
          <div class="campo-ajuste"><label>Ancho (px)</label><input type="number" id="f-width" value="720" oninput="actualizarCodigo()"></div>
          <div class="campo-ajuste"><label>Alto (px)</label><input type="number" id="f-height" value="520" oninput="actualizarCodigo()"></div>
        </div>
        <div class="ayuda-paleta">Estos valores controlan el tamano y los textos de los botones de la ventana que vera el usuario al ejecutar tu script.</div>
      </div>
    </div>

    <!-- ===================== DATOS {DATOS:Tabla.Columna} ===================== -->
    <div class="vista" id="vista-datos">
      <div class="col col-tablas">
        <h2 class="titulo-col">Tablas de la base</h2>
        <div class="buscador"><input type="text" id="buscar-tabla" placeholder="Buscar tabla..." oninput="renderListaTablas()"></div>
        <div class="contador-tablas" id="contador-tablas"></div>
        <div class="lista-tablas" id="lista-tablas"></div>
      </div>

      <div class="col col-columnas" id="col-columnas">
        <div class="vacio-tablas"><b>Elige una tabla</b><br>Busca y elige una tabla a la izquierda para ver sus columnas reales.</div>
      </div>

      <div class="col col-canasta">
        <h2 class="titulo-col">Columnas elegidas</h2>
        <div id="lista-canasta"></div>
        <div class="ayuda-paleta">Marca columnas de una o varias tablas; arma la etiqueta y si es obligatoria. Los tokens <code>{DATOS:Tabla.Columna}</code> resultantes se arman abajo en la pestana "Codigo".</div>
      </div>
    </div>

    <!-- ===================== VISTA PREVIA ===================== -->
    <div class="vista" id="vista-previa">
      <div class="previa-wrap"><div id="previa-render"></div></div>
    </div>

    <!-- ===================== CODIGO ===================== -->
    <div class="vista" id="vista-codigo">
      <div class="col-codigo">
        <div class="codigo-principal">
          <h2 class="titulo-col">Codigo Python (ctx.form) -- Modo A</h2>
          <pre id="codigo"></pre>
          <button class="btn primario" onclick="copiarCodigo()">Copiar codigo Python</button>
          <div class="aviso" id="aviso-codigo"></div>

          <div id="bloque-snippets-datos" style="margin-top:22px;">
            <h2 class="titulo-col">Snippets tokens {DATOS:Tabla.Columna} -- Modo B</h2>
            <div class="snippet-tabs" id="snippet-tabs"></div>
            <div id="snippet-cuerpos"></div>
            <button class="btn primario" onclick="copiarSnippet()">Copiar snippet</button>
            <div class="aviso" id="aviso-snippet"></div>
          </div>
        </div>
        <div class="codigo-lateral">
          <div class="panel">
            <h3>Guardar tu diseno</h3>
            <p style="font-size:11.5px;color:var(--muted);margin:0 0 10px;">La ventana no conserva datos entre ejecuciones. Copia este JSON y guardalo en un .txt si quieres seguir editando el formulario otro dia.</p>
            <button class="btn" style="width:100%;justify-content:center;" onclick="copiarJson()">Copiar diseno (JSON)</button>
            <div class="aviso" id="aviso-json"></div>
          </div>
          <div class="panel">
            <h3>Importar diseno</h3>
            <textarea id="json-import" placeholder="Pega aqui un JSON exportado antes..."></textarea>
            <button class="btn" style="width:100%;justify-content:center;margin-top:8px;" onclick="importarJson()">Cargar diseno</button>
            <div class="aviso" id="aviso-import"></div>
          </div>
        </div>
      </div>
    </div>

  </div>

<script>
  /* ---------------- definicion de tipos de campo ---------------- */
  const TIPOS = {
    text:    { etiqueta: 'Texto',            icon: 'text' },
    number:  { etiqueta: 'Numero entero',    icon: 'number' },
    decimal: { etiqueta: 'Decimal / importe', icon: 'decimal' },
    date:    { etiqueta: 'Fecha',            icon: 'date' },
    bool:    { etiqueta: 'Si / No',          icon: 'bool' },
    combo:   { etiqueta: 'Lista desplegable', icon: 'combo' },
    memo:    { etiqueta: 'Texto largo',      icon: 'memo' },
  };

  const ICONOS = {
    text:   '<path d="M4 5h16M4 12h10M4 19h7"/>',
    number: '<path d="M6 4v16M14 4l-3 16M9 9h11M8 15h11"/>',
    decimal:'<circle cx="12" cy="12" r="9"/><path d="M9 9h.01M15 15h.01M9 15l6-6"/>',
    date:   '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/>',
    bool:   '<rect x="3" y="7" width="18" height="10" rx="5"/><circle cx="8" cy="12" r="3.2"/>',
    combo:  '<path d="M4 6h16M4 12h16M4 18h10"/><path d="M18 15l2 3 2-3" transform="translate(-4,0)"/>',
    memo:   '<path d="M4 4h16v16H4z"/><path d="M7 8h10M7 12h10M7 16h6"/>',
  };
  function svgIcono(tipo) {
    return '<svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + (ICONOS[tipo] || ICONOS.text) + '</svg>';
  }

  let fields = [];
  let contador = 0;
  let tabActual = 'disenar';
  let dragIndex = null;

  /* ---------------- Modo B: esquema en vivo + canasta de tokens {DATOS:...} ---------------- */
  let schemaTablas = [];        // [{schema, tabla, clave}] -- clave = "schema.tabla"
  let schemaColumnas = {};      // clave -> [{columna, tipo, maxLen}]
  let schemaListo = false;
  let tablaActiva = null;       // clave de la tabla elegida en el navegador
  let canasta = [];             // [{tabla, columna, tipoSql, maxLen, fieldType, label, required}]
  let snippetActivo = 'sql';

  async function descomprimir(b64) {
    const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
    const buf = await new Response(stream).arrayBuffer();
    return JSON.parse(new TextDecoder().decode(buf));
  }

  // Replica EXACTA de MapDataTypeAFieldType (HostClient.cs) -- el mismo mapeo que usa el
  // motor real de {DATOS:Tabla.Columna} al ejecutar, para que la vista previa del Modo B
  // no mienta sobre que tipo de control le va a tocar al usuario.
  function mapDataType(dataType, maxLen) {
    const t = String(dataType || '').trim().toLowerCase();
    if (['int', 'bigint', 'smallint', 'tinyint'].includes(t)) return 'number';
    if (['decimal', 'numeric', 'money', 'smallmoney', 'float', 'real'].includes(t)) return 'decimal';
    if (['date', 'datetime', 'datetime2', 'smalldatetime'].includes(t)) return 'date';
    if (t === 'bit') return 'bool';
    if (['text', 'ntext', 'xml'].includes(t)) return 'memo';
    if (['nvarchar', 'varchar', 'char', 'nchar'].includes(t)) {
      if (maxLen === null || maxLen === undefined || maxLen < 0) return 'memo';
      return 'text';
    }
    return 'text';
  }

  async function cargarEsquema() {
    if (schemaListo) return;
    try {
      const datos = await descomprimir(window.__esquemaB64 || '');
      schemaTablas = datos.tablas || [];
      schemaColumnas = datos.columnas || {};
      schemaListo = true;
    } catch (e) {
      schemaTablas = [];
      schemaColumnas = {};
    }
    renderListaTablas();
    if (tablaActiva) renderColumnas();
  }

  function renderListaTablas() {
    const cont = document.getElementById('lista-tablas');
    const contadorEl = document.getElementById('contador-tablas');
    if (!schemaListo) {
      cont.innerHTML = '<div class="sin-resultados">Cargando catalogo de tablas...</div>';
      contadorEl.textContent = '';
      return;
    }
    const q = (document.getElementById('buscar-tabla').value || '').trim().toLowerCase();
    const filtradas = q
      ? schemaTablas.filter(t => t.tabla.toLowerCase().includes(q) || t.clave.toLowerCase().includes(q))
      : schemaTablas;
    contadorEl.textContent = filtradas.length + ' de ' + schemaTablas.length + ' tablas';
    if (!filtradas.length) {
      cont.innerHTML = '<div class="sin-resultados">Sin resultados para "' + escapeHtml(q) + '".</div>';
      return;
    }
    cont.innerHTML = filtradas.slice(0, 400).map(t => `
      <div class="fila-tabla ${t.clave === tablaActiva ? 'activa' : ''}" onclick="seleccionarTabla('${escapeAttr(t.clave)}')">
        <span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(t.tabla)}</span>
        <span class="esq">${escapeHtml(t.schema)}</span>
      </div>`).join('');
  }

  function seleccionarTabla(clave) {
    tablaActiva = clave;
    renderListaTablas();
    renderColumnas();
  }

  function enCanasta(tabla, columna) {
    return canasta.findIndex(c => c.tabla === tabla && c.columna === columna);
  }

  function renderColumnas() {
    const cont = document.getElementById('col-columnas');
    if (!tablaActiva) {
      cont.innerHTML = '<div class="vacio-tablas"><b>Elige una tabla</b><br>Busca y elige una tabla a la izquierda para ver sus columnas reales.</div>';
      return;
    }
    const tabla = tablaActiva.includes('.') ? tablaActiva.split('.').slice(1).join('.') : tablaActiva;
    const cols = schemaColumnas[tablaActiva] || [];
    const filas = cols.map(c => {
      const idx = enCanasta(tabla, c.columna);
      const ft = mapDataType(c.tipo, c.maxLen);
      const tipoTxt = c.maxLen && c.maxLen > 0 ? `${c.tipo}(${c.maxLen})` : (c.maxLen === -1 ? `${c.tipo}(MAX)` : c.tipo);
      return `
        <tr class="${idx >= 0 ? 'marcada' : ''}">
          <td><input type="checkbox" ${idx >= 0 ? 'checked' : ''} onchange="toggleColumna('${escapeAttr(tabla)}','${escapeAttr(c.columna)}','${escapeAttr(c.tipo)}',${c.maxLen === null || c.maxLen === undefined ? 'null' : c.maxLen})"></td>
          <td class="nombre-col-sql">${escapeHtml(c.columna)}</td>
          <td class="tipo-sql-real">${escapeHtml(tipoTxt)}</td>
          <td><span class="pill-tipo">${svgIcono(TIPOS[ft].icon)}${TIPOS[ft].etiqueta}</span></td>
        </tr>`;
    }).join('');
    cont.innerHTML = `
      <div class="titulo-tabla-activa">${escapeHtml(tabla)}</div>
      <div class="subt-tabla-activa">${cols.length} columnas -- marca las que quieras convertir en token {DATOS:Tabla.Columna}</div>
      <table class="tabla-cols">
        <thead><tr><th></th><th>Columna</th><th>Tipo SQL real</th><th>Control que vera el usuario</th></tr></thead>
        <tbody>${filas || '<tr><td colspan="4" class="sin-resultados">Esta tabla no tiene columnas visibles.</td></tr>'}</tbody>
      </table>`;
  }

  function toggleColumna(tabla, columna, tipoSql, maxLen) {
    const idx = enCanasta(tabla, columna);
    if (idx >= 0) {
      canasta.splice(idx, 1);
    } else {
      canasta.push({
        tabla, columna, tipoSql, maxLen,
        fieldType: mapDataType(tipoSql, maxLen),
        label: columna, required: false,
      });
    }
    renderColumnas();
    renderCanasta();
  }

  function quitarDeCanasta(i) { canasta.splice(i, 1); renderColumnas(); renderCanasta(); }
  function actualizarItemCanasta(i, prop, valor) { canasta[i][prop] = valor; renderCanasta(); }

  function renderCanasta() {
    const cont = document.getElementById('lista-canasta');
    if (!canasta.length) {
      cont.innerHTML = '<div class="canasta-vacia">Aun no hay columnas elegidas. Marca columnas desde la tabla en el centro.</div>';
      generarSnippets();
      return;
    }
    cont.innerHTML = canasta.map((c, i) => `
      <div class="item-canasta">
        <div class="tit">
          <span class="tok">${escapeHtml(tokenPara(c))}</span>
          <button onclick="quitarDeCanasta(${i})" title="Quitar">&times;</button>
        </div>
        <input type="text" value="${escapeAttr(c.label)}" placeholder="Etiqueta visible"
          oninput="actualizarItemCanasta(${i},'label',this.value)">
        <div class="chk"><input type="checkbox" ${c.required ? 'checked' : ''}
          onchange="actualizarItemCanasta(${i},'required',this.checked)"> Obligatorio</div>
      </div>`).join('');
    generarSnippets();
  }

  // Arma el texto EXACTO del token, con la misma gramatica que entiende RxDatosTipado en
  // HostClient.cs: {DATOS:Tabla.Columna}, {DATOS:Tabla.Columna:Etiqueta},
  // {DATOS:Tabla.Columna:*} (obligatorio sin etiqueta), {DATOS:Tabla.Columna:Etiqueta:*}.
  function tokenPara(c) {
    const limpia = s => String(s || '').replace(/[:}]/g, '').trim();
    const etiqueta = limpia(c.label);
    const conEtiqueta = etiqueta && etiqueta !== c.columna;
    if (conEtiqueta && c.required) return `{DATOS:${c.tabla}.${c.columna}:${etiqueta}:*}`;
    if (conEtiqueta) return `{DATOS:${c.tabla}.${c.columna}:${etiqueta}}`;
    if (c.required) return `{DATOS:${c.tabla}.${c.columna}:*}`;
    return `{DATOS:${c.tabla}.${c.columna}}`;
  }

  function agruparPorTabla(items) {
    const out = {};
    items.forEach(c => { (out[c.tabla] = out[c.tabla] || []).push(c); });
    return out;
  }

  function generarSnippets() {
    const porTabla = agruparPorTabla(canasta);
    const tablas = Object.keys(porTabla);
    let sql, py, cs;
    if (!tablas.length) {
      sql = '-- Marca columnas en la pestana "Datos {DATOS}" para generar este ejemplo.';
      py = '# Marca columnas en la pestana "Datos {DATOS}" para generar este ejemplo.';
      cs = '// Marca columnas en la pestana "Datos {DATOS}" para generar este ejemplo.';
    } else {
      const bloquesSql = tablas.map(t => {
        const cols = porTabla[t];
        const sets = cols.map(c => `    ${c.columna} = ${tokenPara(c)}`).join(',\n');
        return `-- Tabla: ${t}\nUPDATE ${t}\nSET\n${sets}\nWHERE ${t}ID = {pID};`; // {pID} = fila activa en Comercial
      });
      sql = bloquesSql.join('\n\n');

      const bloquesPy = tablas.map(t => {
        const cols = porTabla[t];
        const sets = cols.map(c => `    ${c.columna} = ${tokenPara(c)}`).join(',\n');
        return `# Tabla: ${t}\nctx.execute('''\n    UPDATE ${t}\n    SET\n${sets}\n    WHERE ${t}ID = {pID}\n''')`;
      });
      py = `# lang: python\nfrom broslmv import ctx\n\n${bloquesPy.join('\n\n')}\n\nctx.msg("Datos actualizados correctamente.")`;

      const bloquesCs = tablas.map(t => {
        const cols = porTabla[t];
        const sets = cols.map(c => `    ${c.columna} = ${tokenPara(c)}`).join(',\n');
        return `// Tabla: ${t}\nctx.NonQuery(@"\n    UPDATE ${t}\n    SET\n${sets}\n    WHERE ${t}ID = {pID}\n");`;
      });
      cs = `// lang: csharp\n${bloquesCs.join('\n\n')}\n\nctx.Msg("Datos actualizados correctamente.");`;
    }
    window.__snippets = { sql, python: py, csharp: cs };
    renderSnippetTabs();
  }

  function renderSnippetTabs() {
    const tabsCont = document.getElementById('snippet-tabs');
    const cuerposCont = document.getElementById('snippet-cuerpos');
    if (!tabsCont) return;
    const nombres = { sql: 'SQL', python: 'Python', csharp: 'C#' };
    tabsCont.innerHTML = Object.keys(nombres).map(k =>
      `<button class="${k === snippetActivo ? 'activa' : ''}" onclick="cambiarSnippet('${k}')">${nombres[k]}</button>`).join('');
    cuerposCont.innerHTML = Object.keys(nombres).map(k =>
      `<pre id="snippet-${k}" class="snippet-bloque ${k === snippetActivo ? 'activa' : ''}" style="background:#08172A;color:#B7E37B;font-family:Consolas,'Courier New',monospace;font-size:12px;line-height:1.5;padding:14px;border-radius:10px;overflow:auto;white-space:pre-wrap;margin:0;border:1px solid var(--line);">${escapeHtml((window.__snippets || {})[k] || '')}</pre>`).join('');
  }

  function cambiarSnippet(k) { snippetActivo = k; renderSnippetTabs(); }

  function copiarSnippet() {
    const aviso = document.getElementById('aviso-snippet');
    const texto = (window.__snippets || {})[snippetActivo] || '';
    navigator.clipboard.writeText(texto).then(() => {
      aviso.textContent = 'Copiado al portapapeles.';
      setTimeout(() => aviso.textContent = '', 2200);
    }).catch(() => { aviso.textContent = 'No se pudo copiar.'; aviso.style.color = 'var(--bad)'; });
  }

  /* ---------------- paleta ---------------- */
  function pintarPaleta() {
    const cont = document.getElementById('paleta');
    cont.innerHTML = Object.keys(TIPOS).map(t => `
      <div class="tipo" draggable="true" ondragstart="onPaletaDragStart(event,'${t}')" onclick="agregarCampo('${t}')">
        <span class="ic">${svgIcono(TIPOS[t].icon)}</span>
        <span>${TIPOS[t].etiqueta}</span>
      </div>`).join('');
  }

  function onPaletaDragStart(ev, tipo) {
    ev.dataTransfer.setData('text/tipo-nuevo', tipo);
    ev.dataTransfer.effectAllowed = 'copy';
  }

  /* ---------------- pestanas ---------------- */
  function cambiarTab(t) {
    tabActual = t;
    document.querySelectorAll('.tabs button').forEach(b => b.classList.toggle('activa', b.dataset.tab === t));
    document.querySelectorAll('.vista').forEach(v => v.classList.remove('activa'));
    document.getElementById('vista-' + t).classList.add('activa');
    if (t === 'previa') renderPrevia();
    if (t === 'codigo') { actualizarCodigo(); renderSnippetTabs(); }
    if (t === 'datos') cargarEsquema();
  }

  function alternarTecnico() {
    const activo = document.body.classList.toggle('mostrar-tecnico');
    document.getElementById('btn-tecnico').textContent = activo ? 'Ocultar detalles tecnicos' : 'Mostrar detalles tecnicos';
  }

  function nuevoFormulario() {
    if (fields.length && !confirm('Esto borra todos los campos actuales. Continuar?')) return;
    fields = []; contador = 0;
    document.getElementById('f-title').value = 'Datos';
    document.getElementById('f-ok').value = 'Aceptar';
    document.getElementById('f-cancel').value = 'Cancelar';
    document.getElementById('f-width').value = 720;
    document.getElementById('f-height').value = 520;
    renderCampos();
  }

  /* ---------------- utilidades de texto ---------------- */
  function slugify(s) {
    return String(s || '').toLowerCase()
      .normalize('NFD').replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '') || 'campo';
  }
  function nombreUnico(base, exceptoIndice) {
    let n = base, i = 2;
    while (fields.some((f, idx) => f.name === n && idx !== exceptoIndice)) { n = base + '_' + i; i++; }
    return n;
  }
  function escapeHtml(s) { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
  function escapeAttr(s) { return escapeHtml(s).replace(/"/g,'&quot;'); }
  function pyStr(s) { return '"' + String(s).replace(/\\/g,'\\\\').replace(/"/g,'\\"') + '"'; }

  /* ---------------- CRUD de campos ---------------- */
  function agregarCampo(tipo, enIndice) {
    contador++;
    const base = { id: 'c' + Date.now() + contador, type: tipo, label: TIPOS[tipo].etiqueta + ' ' + contador,
      required: false, readOnly: false, defaultVal: '', defaultBool: false, nameManual: false, abierto: true,
      options: tipo === 'combo' ? [{ value: 'opcion_1', label: 'Opcion 1' }, { value: 'opcion_2', label: 'Opcion 2' }] : [] };
    base.name = nombreUnico(slugify(base.label));
    fields.forEach(f => f.abierto = false);
    if (enIndice === undefined || enIndice === null) fields.push(base);
    else fields.splice(enIndice, 0, base);
    renderCampos();
  }

  function borrarCampo(i) { fields.splice(i, 1); renderCampos(); }
  function duplicarCampo(i) {
    const copia = JSON.parse(JSON.stringify(fields[i]));
    copia.id = 'c' + Date.now();
    copia.name = nombreUnico(copia.name + '_copia');
    copia.abierto = true;
    fields.splice(i + 1, 0, copia);
    renderCampos();
  }
  function alternarAbierto(i) { fields[i].abierto = !fields[i].abierto; renderCampos(); }

  function actualizarLabel(i, valor) {
    const f = fields[i];
    f.label = valor;
    if (!f.nameManual) f.name = nombreUnico(slugify(valor), i);
    renderCampos();
  }
  function actualizarNombre(i, valor) {
    fields[i].nameManual = true;
    fields[i].name = nombreUnico(slugify(valor) , i);
    renderCampos();
  }
  function actualizarCampo(i, prop, valor) { fields[i][prop] = valor; actualizarCodigo(); }

  function agregarOpcion(i) {
    fields[i].options.push({ value: 'opcion_' + (fields[i].options.length + 1), label: 'Opcion ' + (fields[i].options.length + 1) });
    renderCampos();
  }
  function borrarOpcion(i, j) { fields[i].options.splice(j, 1); renderCampos(); }
  function actualizarOpcion(i, j, prop, valor) {
    fields[i].options[j][prop] = valor;
    if (prop === 'label' && fields[i].options[j].valorManual !== true) fields[i].options[j].value = slugify(valor);
    actualizarCodigo();
  }

  /* ---------------- arrastrar para reordenar ---------------- */
  function onCampoDragStart(ev, i) { dragIndex = i; ev.currentTarget.classList.add('arrastrando'); ev.dataTransfer.effectAllowed = 'move'; }
  function onCampoDragEnd(ev) { ev.currentTarget.classList.remove('arrastrando'); }
  function onCampoDragOver(ev, i) { ev.preventDefault(); ev.currentTarget.classList.add('sobre-drop'); }
  function onCampoDragLeave(ev) { ev.currentTarget.classList.remove('sobre-drop'); }
  function onCampoDrop(ev, i) {
    ev.preventDefault(); ev.currentTarget.classList.remove('sobre-drop');
    const tipoNuevo = ev.dataTransfer.getData('text/tipo-nuevo');
    if (tipoNuevo) { agregarCampo(tipoNuevo, i); return; }
    if (dragIndex === null || dragIndex === i) return;
    const [mov] = fields.splice(dragIndex, 1);
    fields.splice(i, 0, mov);
    dragIndex = null;
    renderCampos();
  }
  function onLienzoDragOver(ev) { ev.preventDefault(); document.getElementById('lienzo').classList.add('sobre'); }
  function onLienzoDrop(ev) {
    ev.preventDefault(); document.getElementById('lienzo').classList.remove('sobre');
    const tipoNuevo = ev.dataTransfer.getData('text/tipo-nuevo');
    if (tipoNuevo) agregarCampo(tipoNuevo);
  }

  /* ---------------- render: lista de campos ---------------- */
  function renderCampos() {
    const cont = document.getElementById('lista-campos');
    const vacio = document.getElementById('vacio');
    vacio.style.display = fields.length ? 'none' : 'block';
    cont.innerHTML = fields.map((f, i) => {
      let defaultInput = '';
      if (f.type === 'bool') {
        defaultInput = `<div class="chk"><input type="checkbox" ${f.defaultBool ? 'checked' : ''}
          onchange="actualizarCampo(${i},'defaultBool',this.checked)"> Marcado por defecto</div>`;
      } else if (f.type === 'date') {
        defaultInput = `<div><label>Valor por defecto</label>
          <input type="date" value="${escapeAttr(f.defaultVal)}" oninput="actualizarCampo(${i},'defaultVal',this.value)"></div>`;
      } else if (f.type !== 'combo' && f.type !== 'memo') {
        defaultInput = `<div><label>Valor por defecto</label>
          <input type="text" value="${escapeAttr(f.defaultVal)}" oninput="actualizarCampo(${i},'defaultVal',this.value)"></div>`;
      }

      let opcionesBlock = '';
      if (f.type === 'combo') {
        const filas = f.options.map((o, j) => `
          <div class="op">
            <input type="text" placeholder="Etiqueta visible" value="${escapeAttr(o.label)}" oninput="actualizarOpcion(${i},${j},'label',this.value)">
            <button onclick="borrarOpcion(${i},${j})" title="Quitar opcion">&times;</button>
          </div>`).join('');
        opcionesBlock = `<div><label>Opciones de la lista</label><div class="opciones-lista">${filas}</div>
          <button class="btn-mini" onclick="agregarOpcion(${i})">+ Agregar opcion</button></div>`;
      }

      return `
      <div class="campo ${f.abierto ? 'abierto' : ''}" draggable="true"
        ondragstart="onCampoDragStart(event,${i})" ondragend="onCampoDragEnd(event)"
        ondragover="onCampoDragOver(event,${i})" ondragleave="onCampoDragLeave(event)" ondrop="onCampoDrop(event,${i})">
        <div class="cab" onclick="alternarAbierto(${i})">
          <span class="agarre">::</span>
          <span class="ic">${svgIcono(TIPOS[f.type].icon)}</span>
          <span class="nombre-corto">${escapeHtml(f.label || '(sin etiqueta)')}${f.required ? ' <span class="req">*</span>' : ''}</span>
          <span class="badge">${TIPOS[f.type].etiqueta}</span>
          <span class="acciones" onclick="event.stopPropagation()">
            <button onclick="duplicarCampo(${i})" title="Duplicar"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="8" y="8" width="12" height="12" rx="2"/><rect x="4" y="4" width="12" height="12" rx="2"/></svg></button>
            <button class="borrar" onclick="borrarCampo(${i})" title="Eliminar"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 7h16M9 7V5a1 1 0 011-1h4a1 1 0 011 1v2m-8 0l1 13a1 1 0 001 1h6a1 1 0 001-1l1-13"/></svg></button>
          </span>
        </div>
        <div class="cuerpo">
          <div class="fila">
            <div><label>Etiqueta visible para el usuario</label>
              <input type="text" value="${escapeAttr(f.label)}" oninput="actualizarLabel(${i},this.value)"></div>
          </div>
          <div class="fila detalle-tecnico">
            <div><label>Nombre interno (identificador, sin espacios)</label>
              <input type="text" value="${escapeAttr(f.name)}" oninput="actualizarNombre(${i},this.value)"></div>
          </div>
          ${opcionesBlock}
          ${defaultInput}
          <div class="chk"><input type="checkbox" ${f.required ? 'checked' : ''}
            onchange="actualizarCampo(${i},'required',this.checked)"> Obligatorio</div>
          <div class="chk detalle-tecnico"><input type="checkbox" ${f.readOnly ? 'checked' : ''}
            onchange="actualizarCampo(${i},'readOnly',this.checked)"> Solo lectura</div>
        </div>
      </div>`;
    }).join('');

    actualizarCodigo();
  }

  /* ---------------- vista previa real ---------------- */
  function renderPrevia() {
    const cont = document.getElementById('previa-render');
    if (!fields.length) { cont.innerHTML = '<div class="previa-vacio">Agrega campos en la pestana "Disenar" para ver la vista previa.</div>'; return; }
    const title = document.getElementById('f-title').value || 'Formulario';
    const ok = document.getElementById('f-ok').value || 'Aceptar';
    const cancel = document.getElementById('f-cancel').value || 'Cancelar';

    const camposHtml = fields.map(f => {
      const req = f.required ? ' <span class="req">*</span>' : '';
      const dis = f.readOnly ? 'disabled' : '';
      let control = '';
      if (f.type === 'text') control = `<input type="text" ${dis} value="${escapeAttr(f.defaultVal)}">`;
      else if (f.type === 'number') control = `<input type="number" step="1" ${dis} value="${escapeAttr(f.defaultVal)}">`;
      else if (f.type === 'decimal') control = `<input type="number" step="0.01" ${dis} value="${escapeAttr(f.defaultVal)}">`;
      else if (f.type === 'date') control = `<input type="date" ${dis} value="${escapeAttr(f.defaultVal)}">`;
      else if (f.type === 'memo') control = `<textarea rows="3" ${dis}>${escapeHtml(f.defaultVal)}</textarea>`;
      else if (f.type === 'bool') control = `<label style="display:flex;align-items:center;gap:6px;font-weight:400;"><input type="checkbox" style="width:auto;" ${f.defaultBool ? 'checked' : ''} ${dis}> ${escapeHtml(f.label)}</label>`;
      else if (f.type === 'combo') control = `<select ${dis}>${f.options.map(o => `<option>${escapeHtml(o.label)}</option>`).join('')}</select>`;
      if (f.type === 'bool') return `<div class="previa-campo">${control}</div>`;
      return `<div class="previa-campo"><label>${escapeHtml(f.label)}${req}</label>${control}</div>`;
    }).join('');

    cont.innerHTML = `
      <div class="previa-ventana">
        <div class="previa-titulo">${escapeHtml(title)}</div>
        <div class="previa-cuerpo">${camposHtml}</div>
        <div class="previa-botones"><button>${escapeHtml(cancel)}</button><button class="ok">${escapeHtml(ok)}</button></div>
      </div>`;
  }

  /* ---------------- generacion de codigo Python ---------------- */
  function generarCodigo() {
    const title = document.getElementById('f-title').value || 'Formulario';
    const ok = document.getElementById('f-ok').value || 'Aceptar';
    const cancel = document.getElementById('f-cancel').value || 'Cancelar';
    const width = parseInt(document.getElementById('f-width').value) || 720;
    const height = parseInt(document.getElementById('f-height').value) || 520;

    const lineasCampos = fields.map(f => {
      const partes = [];
      partes.push(`"name": ${pyStr(f.name)}`);
      partes.push(`"label": ${pyStr(f.label || f.name)}`);
      partes.push(`"type": ${pyStr(f.type)}`);
      if (f.required) partes.push('"required": True');
      if (f.readOnly) partes.push('"read_only": True');

      if (f.type === 'bool') partes.push(`"default": ${f.defaultBool ? 'True' : 'False'}`);
      else if (f.type === 'number') { if (f.defaultVal !== '') partes.push(`"default": ${parseInt(f.defaultVal) || 0}`); }
      else if (f.type === 'decimal') { if (f.defaultVal !== '') partes.push(`"default": ${parseFloat(f.defaultVal) || 0}`); }
      else if (f.type !== 'combo' && f.defaultVal) partes.push(`"default": ${pyStr(f.defaultVal)}`);

      if (f.type === 'combo' && f.options.length) {
        const opts = f.options.map(o => `{"value": ${pyStr(o.value)}, "label": ${pyStr(o.label)}}`).join(', ');
        partes.push(`"options": [${opts}]`);
      }
      return '        {' + partes.join(', ') + '},';
    }).join('\n');

    return `from broslmv import ctx

r = ctx.form({
    "title": ${pyStr(title)},
    "fields": [
${lineasCampos || '        # agrega campos desde la pestana "Disenar"...'}
    ],
    "ok_label": ${pyStr(ok)},
    "cancel_label": ${pyStr(cancel)},
    "width": ${width},
    "height": ${height},
})

if r["submitted"]:
    valores = r["values"]
    # TODO: usa valores["${fields[0] ? fields[0].name : 'campo'}"], etc.
    ctx.msg("Datos capturados correctamente.")
`;
  }

  function resaltar(codigo) {
    let h = escapeHtml(codigo);
    h = h.replace(/(#.*)$/gm, '<span class="tok-com">$1</span>');
    h = h.replace(/"(?:[^"\\]|\\.)*"/g, m => `<span class="tok-str">${m}</span>`);
    h = h.replace(/\b(from|import|if|True|False|None)\b/g, '<span class="tok-kw">$1</span>');
    h = h.replace(/(?<![\w"])(\d+(\.\d+)?)(?![\w"])/g, '<span class="tok-num">$1</span>');
    return h;
  }

  function actualizarCodigo() {
    const codigo = generarCodigo();
    window.__codigoGenerado = codigo;
    document.getElementById('codigo').innerHTML = resaltar(codigo);
    if (tabActual === 'previa') renderPrevia();
  }

  function copiarCodigo() {
    const aviso = document.getElementById('aviso-codigo');
    navigator.clipboard.writeText(window.__codigoGenerado || '').then(() => {
      aviso.textContent = 'Copiado al portapapeles.';
      setTimeout(() => aviso.textContent = '', 2200);
    }).catch(() => { aviso.textContent = 'No se pudo copiar.'; aviso.style.color = 'var(--bad)'; });
  }

  /* ---------------- exportar / importar diseno (JSON) ---------------- */
  function copiarJson() {
    const aviso = document.getElementById('aviso-json');
    const diseno = {
      title: document.getElementById('f-title').value,
      ok_label: document.getElementById('f-ok').value,
      cancel_label: document.getElementById('f-cancel').value,
      width: parseInt(document.getElementById('f-width').value) || 720,
      height: parseInt(document.getElementById('f-height').value) || 520,
      fields
    };
    navigator.clipboard.writeText(JSON.stringify(diseno, null, 2)).then(() => {
      aviso.textContent = 'JSON copiado.';
      setTimeout(() => aviso.textContent = '', 2200);
    }).catch(() => { aviso.textContent = 'No se pudo copiar.'; });
  }

  function importarJson() {
    const aviso = document.getElementById('aviso-import');
    try {
      const diseno = JSON.parse(document.getElementById('json-import').value);
      document.getElementById('f-title').value = diseno.title || 'Datos';
      document.getElementById('f-ok').value = diseno.ok_label || 'Aceptar';
      document.getElementById('f-cancel').value = diseno.cancel_label || 'Cancelar';
      document.getElementById('f-width').value = diseno.width || 720;
      document.getElementById('f-height').value = diseno.height || 520;
      fields = Array.isArray(diseno.fields) ? diseno.fields : [];
      renderCampos();
      aviso.textContent = 'Diseno cargado.';
      setTimeout(() => aviso.textContent = '', 2200);
    } catch (e) {
      aviso.textContent = 'JSON invalido.'; aviso.style.color = 'var(--bad)';
    }
  }

  /* ---------------- arranque ---------------- */
  document.getElementById('lienzo').addEventListener('dragover', onLienzoDragOver);
  document.getElementById('lienzo').addEventListener('dragleave', () => document.getElementById('lienzo').classList.remove('sobre'));
  document.getElementById('lienzo').addEventListener('drop', onLienzoDrop);

  pintarPaleta();
  renderCampos();
  renderCanasta();
</script>
</body>
</html>
"""

# ---- Modo B: esquema real de la base (tablas + columnas), consultado UNA vez aqui,
# antes de abrir la ventana -- ver comentario de cabecera sobre por que (ctx.show_html_
# formulario cierra la ventana en el primer postMessage, no sirve para navegar en vivo).
# Comprimido con gzip+base64 (mismo truco que ctx.dashboard) para no arriesgar el limite
# real de ~2MB de WebView2 NavigateToString -- la base tiene ~500 tablas / ~18k columnas.
import base64
import gzip
import json

try:
    _tablas_rows = ctx.query("""
        SELECT TABLE_SCHEMA, TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE'
        ORDER BY TABLE_NAME
    """)
    _cols_rows = ctx.query("""
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        ORDER BY TABLE_NAME, ORDINAL_POSITION
    """)
except Exception:
    _tablas_rows = []
    _cols_rows = []

_tablas = [
    {"schema": r["TABLE_SCHEMA"], "tabla": r["TABLE_NAME"], "clave": r["TABLE_SCHEMA"] + "." + r["TABLE_NAME"]}
    for r in _tablas_rows
]
_columnas = {}
for r in _cols_rows:
    _clave = r["TABLE_SCHEMA"] + "." + r["TABLE_NAME"]
    _maxlen = r.get("CHARACTER_MAXIMUM_LENGTH")
    _columnas.setdefault(_clave, []).append({
        "columna": r["COLUMN_NAME"],
        "tipo": r["DATA_TYPE"],
        "maxLen": None if _maxlen is None else int(_maxlen),
    })

_esquema_json = json.dumps({"tablas": _tablas, "columnas": _columnas}, default=str).encode("utf-8")
_esquema_b64 = base64.b64encode(gzip.compress(_esquema_json, compresslevel=9)).decode("ascii")

HTML_FINAL = HTML.replace("__BROSLMV_ESQUEMA_B64__", _esquema_b64)

ctx.show_html(HTML_FINAL, title="Disenador de Formularios - BrosLMV", width=1280, height=820, modal=True)
