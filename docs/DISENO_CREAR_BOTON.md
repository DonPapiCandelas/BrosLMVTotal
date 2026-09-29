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

## 2. La pantalla (una sola, sin pasos ocultos)

Se abre con clic secundario sobre un script → **«Crear botón…»** (y desde «Más opciones»).

1. **Nombre del botón** — por defecto el nombre visible del script. Vista previa en vivo del botón.
2. **Ícono** — el catálogo real es la carpeta `…\Compac\ComercialSP\Icons` (1,765 `.ico`, casi todos en versión 16 y 32 px; se muestra la de 32):
   - **Sugeridos** arriba, según el nombre del botón (p. ej. «XML» o «documentos» → `DocumentGenerator`, `Documents`, `ImportExcel`…) más el de BrosLMV.
   - **Buscador** sobre todos los nombres de archivo y cuadrícula con miniaturas reales.
   - **«Explorar carpeta…»** abre el selector de Windows en esa carpeta y acepta también un `.ico`/`.png` de cualquier lugar (se copia a la carpeta de íconos con prefijo `BrosLMV_`).
   - Técnica: C# lista la carpeta y entrega las miniaturas al HTML como PNG en base64, por páginas y con caché (no se cargan los 1,765 de golpe).
3. **Dónde aparece**
   - Todos los módulos (global) **o** módulos específicos: lista con casillas y buscador, agrupada por naturaleza (compras, ventas, inventarios…), tomada de `engModuleParameter`, no escrita a mano.
4. **Pestaña** — combo con las existentes (BrosLMV, General, Lista, Reporte…) **o «Nueva pestaña…»** con el nombre que quiera el usuario.
5. **Sección** — combo con las secciones de esa pestaña **o «Nueva sección…»** (Herramientas, Contabilidad, Impresión, Filtro…).
6. **Quién lo ve** — todos los usuarios / grupos / usuarios específicos (lista con casillas). Se habilita en el mismo paso; nada de ir usuario por usuario. «Actualizar quién lo ve» sincroniza cuando cambian los grupos.
7. **Empresas** — la actual (por defecto) / también en otras empresas de la lista.
8. Vista previa del ribbon con el botón resaltado → **Crear** (o **Quitar de…** si ya existe).

Reglas: solo toca botones `BrosLMV.%` (nunca los nativos); idempotente (si ya existe, lo actualiza en vez de duplicar); antes de cualquier cambio guarda copia de las filas afectadas para poder deshacer; el asistente nunca pide escribir `BrosLMV.xxxx` ni un ID.

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
