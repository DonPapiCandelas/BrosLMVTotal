# Migrar botones de las tablas `rt*` a BrosLMV

Varias empresas llegan a BrosLMV con botones hechos en otra herramienta de scripting para
Comercial PRO, que guarda todo en cuatro tablas `rt*` de la propia base de la empresa. Esta
guía resume lo aprendido migrándolos a mano en tres empresas reales (una con 29 botones, otra
con 38, otra con 11): cómo leer esas tablas, a qué equivale cada pieza en BrosLMV y qué errores
de los botones originales **no** hay que migrar.

> Regla del repo: no se nombra la otra herramienta en documentación pública (ver
> `AGENTS.md` §3). Aquí se habla solo de sus tablas.

## 1. Dónde vive cada botón

| Tabla | Qué guarda | Columnas clave |
|---|---|---|
| `rtAppFunction` | El código de cada botón (una o varias funciones por botón) | `AppKey`, `FunctionID`, `ExecutionOrder`, `CommandTypeID`, `StoredProcedure`, `Command`, `EventFunctionID`, `RunOnce` |
| `rtAppData` | Fuentes de datos de los combos/listas del formulario | `AppKey`, `Name`, `DataValue` (vista, p. ej. `vwLBSSupplierList`), `PrimaryKey`, `ValueMember`, `DisplayMember` |
| `rtAppParameter` | Opciones del botón | `AppKey`, `ParameterKey`, `Value` |
| `rtAppControl` | Controles del formulario que la herramienta arma sola | `AppKey`, `ControlCaption`, `TypeControlID`, `DataID`, `LocationX/Y`, `Width/Height` |

**En el ribbon** el botón es una fila normal de `engRibbonControl` con
`ControlExecute = '<ProgID de la herramienta>.<AppKey>'` — la misma convención
`<ClaseCOM>.<Función>` que usa BrosLMV (`BrosLMV.<AppKey>`) y que usa el propio Comercial para
sus comandos nativos (p. ej. `Document.MostrarPoliza`, `Document.SincronizarPoliza`). Migrar el
botón del ribbon es cambiar ese valor a `BrosLMV.<AppKey>` una vez que el script existe en
`zzBrosScript`.

**`CommandTypeID` observado:** `2` = SQL inline en `Command`; `7` = script (IronPython) completo
en `Command`. En una empresa el 97% (37/38) eran scripts completos con su propia UI WinForms; en
otra la mayoría eran SQL inline o llamadas a stored procedures (`StoredProcedure`).

**Varias funciones por `AppKey`** se ejecutan en orden de `ExecutionOrder` (p. ej. paso 1 llena
una tabla puente, paso 2 inserta el documento). En BrosLMV eso es un solo script, o "pasos
encadenados" (`RECETAS_NOCODE.md`).

## 2. Equivalencias

| En los botones `rt*` | En BrosLMV |
|---|---|
| Placeholder `{pID}` / `{[pID]}` (documento seleccionado) | `ctx.GetSelectedIds()` / `ctx.get_selected_ids()`; o `ctx.EventoId` si el botón se dispara al guardar (`MANUAL.md` §12) |
| `{[pUserID]}`, `gl['main'].UserID` | `ctx.UserID` / `ctx.user_id` |
| `gl['main'].IDs` / `.ID` | `ctx.GetSelectedIds()` |
| `{cCampo}` (valor capturado en el control `Campo` del formulario) | un formulario propio (`ctx.form`, `ctx.show_html_formulario` o WebView2 — `UI_VENTANAS.md`) |
| `{Datos:Campo}` (columna de la fila elegida en la fuente `rtAppData` llamada `Datos`) | la misma consulta a la vista (`DataValue`) desde tu script |
| `gl['conn']` (conexión cruda, transacciones a mano) | `ctx.Query`/`NonQuery` (C#); en Python cada llamada abre su conexión — lo atómico va en **un solo batch T-SQL** con `BEGIN TRY/BEGIN TRAN…COMMIT` + `ROLLBACK; THROW` |
| `Interaction.CreateObject('Doc.clsMain')` + `.XEngineLib = xe` | `ctx.erp.*` (ya trae el motor conectado) |
| `rtAppParameter.AppThrowUserID = 1` | el script recibe el usuario — en BrosLMV siempre está en `ctx` |
| `AppThrowBusinessEntityID` / `AppThrowModuleID` | `ctx.erp.*` de contexto (`OwnedBusinessEntityId`, `ActiveModuleId` — ojo, ver `MANUAL.md` §12) |
| `AppUseItemID = <columna>` | qué columna de la selección es el ID (verifica el `PrimaryKey` real del módulo en `engModuleParameter`) |
| `AppAllowOnlyOneRecord` / `AppAllowDeleteRows` | validaciones de tu propio script |
| `AppSillyExecution = 1` | ejecución sin ventana — en BrosLMV, un script sin UI (o `BrosLMV.Runner`) |

Los placeholders se sustituyen **como texto** antes de ejecutar: un `{cCampo}` metido en SQL
es inyección si el valor lo escribe el usuario. Al migrar, todo valor capturado va como
parámetro.

## 3. Qué NO migrar tal cual (errores reales encontrados en los botones originales)

1. **Tablas o vistas que no existen en esta empresa.** Los botones se copiaban entre clientes
   con referencias a tablas `zz*` de otro cliente. Verifica cada tabla contra `sys.objects`
   antes de portar (`MANUAL.md` §12, "auditar lo no-nativo").
2. **Filtros con la empresa propia escrita a mano** (`FILTRO_EMPRESA = 'Nombre'`,
   `HAVING OwnedBusinessEntityID = 1`) — deriva la empresa del documento (`MANUAL.md` §12).
3. **Un script copiado por cada empresa propia** (mismo botón duplicado para la empresa 1 y la
   300, diferenciado solo por una constante): en una empresa eran 12 archivos de 35-63 KB casi
   idénticos. Migra **uno** parametrizado por `OwnedBusinessEntityID`.
4. **Ocultar en silencio lo ya asociado** (`WHERE ISNULL(DocumentID,0)=0` al listar XML) —
   `MANUAL.md` §12.
5. **Crear documentos con INSERTs crudos** de 80-100 columnas: usa `ctx.erp.NuevoDocumento`/
   `AgregarArticulo` y completa por SQL solo lo que XEngine no hace (`MANUAL.md` §12, "Gaps
   reales entre los builders…"). Si mantienes SQL crudo, `CoefUnit` y `CostPrice` son las
   causas conocidas de "Division by zero" (`MANUAL.md` §12).
6. **"Liberar documentos" en bloque** (`UPDATE docDocument SET UserID=0 WHERE UserID>0`) —
   libera también lo que alguien está editando; se libera uno por uno (`MANUAL.md` §10.6).
7. **Crear pagos/cobros insertando solo `docFinancialOperation`** — quedan sin aplicar, sin
   saldos actualizados y sin póliza (`MANUAL.md` §10.6).
8. **Botones a medio terminar.** Se encontraron botones publicados cuyo guardado nunca se
   implementó (mostraban "fase preliminar"). Confirma con el usuario qué hace realmente cada
   botón antes de migrarlo — en un caso el nombre del botón (`AdjuntarArch`) no correspondía a
   lo que el usuario creía que hacía.

## 4. Proceso recomendado

1. Volcar las cuatro tablas `rt*` (código completo de `rtAppFunction.Command` a archivos) y
   la lista de `engRibbonControl` que apunta a ellas.
2. Por botón: qué hace, qué tablas toca (verificadas), qué errores de §3 trae.
3. Migrar **un botón a la vez**, con el usuario indicando cuál sigue; probar contra un
   documento real y comparar contra una captura nativa.
4. Cambiar `ControlExecute` del botón a `BrosLMV.<AppKey>`.
5. Cuando la empresa ya no use la otra herramienta: respaldar las tablas `rt*` a tablas de
   respaldo en la misma base y después retirar sus filas y sus botones del ribbon — nunca
   borrar sin respaldo.
