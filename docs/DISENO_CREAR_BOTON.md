# Diseño — Asistente «Crear botón…» (fase C, versión 2.95.0)

> Estado: **diseño para aprobar** (2026-09-29). Parte de [`PLAN_CONSOLA_V3.md`](PLAN_CONSOLA_V3.md). Nada de esto está implementado todavía.

## 1. Cómo guarda Comercial un botón (leído del laboratorio)

| Tabla | Qué es | Campos que importan |
|---|---|---|
| `engRibbonTab` | Pestaña del ribbon | `TabCaption`, `TabOrder`, `ModuleID` (0 = todas), `ShowIfSectionModuleIDIs` |
| `engRibbonGroup` | Sección dentro de una pestaña | `RibbonTabID`, `GroupCaption`, `GroupOrder`, `ToolTipText` |
| `engRibbonControl` | El botón | `ControlCaption`, `ControlDescription`, `ControlExecute` (`BrosLMV.<AppKey>`), `IconFile` |
| `engRibbonMenu` | **Dónde aparece** el botón: una fila por lugar | `RibbonGroupID`, `ControlID`, `ControlOrder`, `ExtraMenuModuleID` (0 = global; otro valor = solo ese módulo), `IfFieldsExist` |

Consecuencias para el diseño:
- «Global» = una fila en `engRibbonMenu` con `ExtraMenuModuleID = 0`; «solo estos módulos» = **una fila por módulo** elegido.
- Una pestaña o sección nueva es solo una fila en `engRibbonTab` / `engRibbonGroup` (aditivo, no rompe lo existente).
- Los íconos son archivos (`.ico`) en `…\Compac\ComercialSP\Icons\`; los nativos usan 369 distintos.
- Estas tablas viven en la **base de cada empresa**: un botón se crea por empresa. La opción «aplicar también a otras empresas» (decisión del plan) escribe las mismas filas en cada base elegida.

## 2. La pantalla (una sola, sin pasos ocultos)

Se abre con clic secundario sobre un script → **«Crear botón…»** (y desde «Más opciones»).

1. **Nombre del botón** — por defecto el nombre visible del script. Vista previa en vivo del botón.
2. **Ícono** — galería con buscador (los `.ico` de Comercial + los de BrosLMV) y «Elegir archivo…» (se copia a la carpeta de íconos).
3. **Dónde aparece**
   - Todos los módulos (global) **o** módulos específicos: lista con casillas y buscador, agrupada por naturaleza (compras, ventas, inventarios…), tomada de `engModuleParameter`, no escrita a mano.
4. **Pestaña** — combo con las existentes (BrosLMV, General, Lista, Reporte…) **o «Nueva pestaña…»** con el nombre que quiera el usuario.
5. **Sección** — combo con las secciones de esa pestaña **o «Nueva sección…»** (Herramientas, Contabilidad, Impresión, Filtro…).
6. **Empresas** — la actual (por defecto) / también en otras empresas de la lista.
7. Vista previa del ribbon con el botón resaltado → **Crear** (o **Quitar de…** si ya existe).

Reglas: solo toca botones `BrosLMV.%` (nunca los nativos); idempotente (si ya existe, lo actualiza en vez de duplicar); antes de cualquier cambio guarda copia de las filas afectadas para poder deshacer; el asistente nunca pide escribir `BrosLMV.xxxx` ni un ID.

## 3. Implementación

- Clase `RibbonAdmin` (C#, en el addon) con `Listar()`, `CrearTab()`, `CrearSeccion()`, `PublicarBoton()`, `QuitarBoton()`, `Deshacer()`; ejecuta todo con `ctx.OpenConn()` en una **transacción** (lección de 2.94.0: no usar `ctx.NonQuery` para lotes).
- Pantalla WinForms con el mismo estilo de la Consola; el mismo motor lo usa `GESTOR_RIBBON.py` (que queda como herramienta avanzada) y luego el SDK (`ctx.ribbon.*`).
- Al terminar llama a `ctx.erp.RefreshRibbon()` si el ribbon se actualiza sin reiniciar; si no, avisa «reinicia Comercial para ver el botón».

## 4. Pruebas previas en el laboratorio (antes de programar la pantalla)

1. ¿`ctx.erp.RefreshRibbon()` muestra el botón nuevo sin reiniciar Comercial?
2. ¿Un `.ico` propio copiado a la carpeta de íconos se ve? ¿Sirve PNG?
3. Un botón con varias filas de `engRibbonMenu` (varios módulos) aparece solo en esos módulos.
4. Una pestaña nueva con secciones nuevas aparece donde se espera (`TabOrder`).
5. Deshacer restaura exactamente las filas anteriores.

## 5. Entrega
Versión **2.95.0** con CHANGELOG, notas de versión, documentación HTML del asistente («Cómo crear un botón») y etiqueta de respaldo previa.
