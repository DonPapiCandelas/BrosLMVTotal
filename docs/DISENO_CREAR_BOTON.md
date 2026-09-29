# Diseño — Asistente «Crear botón…» (fase C, versión 2.95.0)

> Estado: **diseño para aprobar** (2026-09-29). Parte de [`PLAN_CONSOLA_V3.md`](PLAN_CONSOLA_V3.md). Nada de esto está implementado todavía.

## 1. Cómo guarda Comercial un botón (leído del laboratorio)

| Tabla | Qué es | Campos que importan |
|---|---|---|
| `engRibbonTab` | Pestaña del ribbon | `TabCaption`, `TabOrder`, `ModuleID` (0 = todas), `ShowIfSectionModuleIDIs` |
| `engRibbonGroup` | Sección dentro de una pestaña | `RibbonTabID`, `GroupCaption`, `GroupOrder`, `ToolTipText` |
| `engRibbonControl` | El botón | `ControlCaption`, `ControlDescription`, `ControlExecute` (`BrosLMV.<AppKey>`), `IconFile` |
| `engRibbonMenu` | **Dónde aparece** el botón: una fila por lugar | `RibbonGroupID`, `ControlID`, `ControlOrder`, `ExtraMenuModuleID` (0 = global; otro valor = solo ese módulo), `IfFieldsExist`, `IfUserIDIs` (0 = todos; un `UserID` = solo ese usuario) |

Consecuencias para el diseño:
- «Global» = una fila en `engRibbonMenu` con `ExtraMenuModuleID = 0`; «solo estos módulos» = **una fila por módulo** elegido.
- Una pestaña o sección nueva es solo una fila en `engRibbonTab` / `engRibbonGroup` (aditivo, no rompe lo existente).
- Los íconos son archivos (`.ico`) en `…\Compac\ComercialSP\Icons\` (1,765 archivos; los botones nativos usan 369 distintos). `IconFile` guarda solo el nombre del archivo.
- **Quién lo ve:** `IfUserIDIs` admite un solo usuario por fila. «Todos» = una fila con 0; «estos usuarios/grupos» = **una fila por usuario** (los grupos, `engUserGroup`, se expanden a sus usuarios al crear). Hoy ningún botón del laboratorio usa `IfUserIDIs`, así que hay que comprobar que Comercial lo respeta (prueba 6). Los permisos nativos de Comercial (`cpSecurityPermissions`, por grupo y menú) son otro mecanismo y no se tocan.
- Estas tablas viven en la **base de cada empresa**: un botón se crea por empresa. La opción «aplicar también a otras empresas» (decisión del plan) escribe las mismas filas en cada base elegida.

## 2. La pantalla: tres pasos, sin saturar

Se abre con clic secundario sobre un script → **«Crear botón…»** (y desde «Más opciones»). Tres pestañas cortas y una vista previa del ribbon siempre a la derecha (con el globo de la descripción al pasar el mouse); **Crear** está disponible desde el primer paso, porque todo lo demás trae un valor razonable por defecto.

| Paso | Qué se pide | Por defecto |
|---|---|---|
| **1 · El botón** | **Nombre**; **Descripción** (opcional: es el texto que aparece al pasar el mouse por el botón); **Ícono** | nombre del script; sin descripción; ícono sugerido |
| **2 · Dónde aparece** | **Pestaña** (existente o «Nueva pestaña…»); **Sección** (existente o «Nueva sección…»); **Módulos**: «En todos» o «Solo en algunos…» | pestaña BrosLMV; en todos los módulos |
| **3 · Quién lo ve** | Todos / Grupos / Usuarios; en «Avanzado», crearlo también en otras empresas | todos; empresa activa |

Íconos (paso 1): dos catálogos con el mismo selector y buscador —
- **BrosLMV**: catálogo propio, moderno y que crece con cada versión (ver §2.1). Es el que se muestra primero.
- **Comercial**: la carpeta `…\Compac\ComercialSP\Icons` (1,765 `.ico`, en 16 y 32 px; se muestra el de 32), con buscador y miniaturas reales.
- **«Explorar…»**: selector de Windows abierto en esa carpeta; acepta un `.ico`/`.png` de cualquier lugar (se copia con prefijo `BrosLMV_`).
- Técnica: C# lista la carpeta y entrega las miniaturas al HTML como PNG en base64, por páginas y con caché (no se cargan los 1,765 de golpe).

Qué campos del cuadro nativo «Nuevo menú» de Comercial reemplaza (el usuario solo llenaba cuatro: Nombre, Ejecutar, Ícono y a veces Descripción):

| Campo nativo | En el asistente |
|---|---|
| Nombre | Paso 1 · Nombre |
| Ejecutar | Se genera solo (`BrosLMV.<CLAVE>`); nunca se escribe |
| Ícono | Paso 1 · Ícono (galería; ya no hay que teclear «XML.ico») |
| Descripción | Paso 1 · Descripción (`ControlDescription`, el globo del botón) |
| ModuleID (vacío = todos los módulos) | Paso 2 · Módulos: «En todos» / «Solo en algunos…» — técnicamente `ExtraMenuModuleID = 0` o una fila por módulo |
| ID Usuarios | Paso 3 · Quién lo ve (usuarios/grupos, sin escribir IDs) |
| Campos (`IfFieldsExist`), Acceso rápido, Tipo, Orden | No se piden: valores por defecto (botón, al final de la sección); quedan en un «Opciones avanzadas» del futuro |

Reglas: solo toca botones `BrosLMV.%` (nunca los nativos); idempotente (si ya existe, lo actualiza en vez de duplicar); antes de cualquier cambio guarda copia de las filas afectadas para poder deshacer.

### 2.1 Catálogo propio de íconos (recomendación aceptada para estudiar)
Los íconos de Comercial son antiguos y muchos innecesarios; tener un catálogo propio permite botones con aspecto actual. Propuesta:
- Partir de un conjunto de **licencia abierta compatible con GPL-3.0** (por ejemplo Lucide, ISC, o Tabler Icons, MIT; se cita la licencia en `docs/` y en Acerca de), **sin descargar nada sin tu autorización** — te propongo la fuente y el peso antes.
- Se convierten en el build a `.ico` (16/24/32/48 px) con un color de acento BrosLMV y variante «sobre fondo oscuro», y viajan en `instalador\iconos\`; el instalador los copia a la carpeta de íconos de Comercial con el prefijo `BrosLMV_`.
- Nombres en español por tema (Documentos, Bancos, Reportes, Inventario…) para el buscador.
- Se pueden ir agregando íconos propios de la marca sin tocar código.

## 3. Tecnología de la pantalla: WebView2 (HTML)

La pantalla será **HTML dentro de WebView2**, con puente de mensajes (`postMessage`) hacia C#: es la misma técnica del importador de XML (ya probada en las terminales) y de `ctx.ShowHtml`; permite galería de íconos, vista previa viva del ribbon y tema claro/oscuro sin pelear con controles WinForms. WebView2 corre fuera del proceso de Comercial (no gasta sus 2 GB). Maqueta navegable: [`mockups/crear_boton.html`](mockups/crear_boton.html).

## 4. Implementación

- Clase `RibbonAdmin` (C#, en el addon) con `Listar()`, `CrearTab()`, `CrearSeccion()`, `PublicarBoton()`, `QuitarBoton()`, `Deshacer()`; ejecuta todo con `ctx.OpenConn()` en una **transacción** (lección de 2.94.0: no usar `ctx.NonQuery` para lotes).
- Pantalla HTML en WebView2 (ver §3), con el estilo de la Consola; el mismo motor lo usa `GESTOR_RIBBON.py` (que queda como herramienta avanzada) y luego el SDK (`ctx.ribbon.*`).
- Al terminar llama a `ctx.erp.RefreshRibbon()` si el ribbon se actualiza sin reiniciar; si no, avisa «reinicia Comercial para ver el botón».

## 5. Pruebas previas en el laboratorio (antes de programar la pantalla)

1. ¿`ctx.erp.RefreshRibbon()` muestra el botón nuevo sin reiniciar Comercial?
2. ¿Un `.ico` propio copiado a la carpeta de íconos se ve? ¿Sirve PNG?
3. Un botón con varias filas de `engRibbonMenu` (varios módulos) aparece solo en esos módulos.
4. Una pestaña nueva con secciones nuevas aparece donde se espera (`TabOrder`).
5. Deshacer restaura exactamente las filas anteriores.
6. `IfUserIDIs`: una fila por usuario muestra el botón solo a ese usuario (con otra sesión de usuario distinto).

## 6. Entrega
Versión **2.95.0** con CHANGELOG, notas de versión, documentación HTML del asistente («Cómo crear un botón») y etiqueta de respaldo previa.
