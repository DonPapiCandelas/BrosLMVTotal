# BrosLMV — Historial de cambios (CHANGELOG)

Registro de versiones del producto. **Cada cambio al programa debe anotarse aquí**
junto con la actualización de la documentación correspondiente.

Formato: cada versión lista lo **Agregado**, **Cambiado**, **Corregido** o
**Quitado**. La versión va también en `AssemblyVersion` (en `src\ClsMain.cs`).

---

## [sin cambio de versión del addon] — 2026-07-07 — Carpeta `lib\` para librerías externas en scripts C#

> Cambio de **empaque/instalador únicamente** — no se tocó `src\` ni se recompiló
> `BrosLMVClsMain.dll`, por eso no lleva bump de `AssemblyVersion` (sería falso: el DLL
> desplegado seguiría siendo el mismo binario). Ver
> [`PLAN_LIBRERIAS_EXTERNAS.md`](PLAN_LIBRERIAS_EXTERNAS.md) para el diseño completo.

**Agregado**
- `C:\BrosLMV\lib\` — carpeta nueva para librerías externas de terceros que los scripts
  C# cargan con `#r "C:\BrosLMV\lib\Nombre.dll"` (mecanismo que Roslyn ya soportaba, ver
  `CAPACIDADES.md` §10, simplemente no se había usado en la práctica hasta ahora).
- 4 librerías incluidas de entrada (todas MIT, compatibles con GPL-3.0): Newtonsoft.Json
  13.0.3, QRCoder 1.6.0, ClosedXML 0.102.3 (+ dependencias transitivas), Microsoft.Web.WebView2
  1.0.2739.15 (con el `WebView2Loader.dll` de **32 bits**, porque `ComercialSP.exe` es un
  proceso de 32 bits).
- `PLANTILLA_BASE_CSHARP_WEBVIEW2.ctx` (en `scripts\`, compartida) — patrón de
  inicialización asíncrona segura para WebView2 (por evento, nunca bloqueante) para que
  cualquier script futuro lo copie en vez de reinventarlo.
- 4 scripts de prueba en `scripts\Distribuciones_Candelas\`: `PRUEBA_JSON.ctx`,
  `PRUEBA_QR.ctx`, `PRUEBA_EXCEL.ctx`, `PRUEBA_WEBVIEW2.ctx`.
- `build\descargar_librerias_externas.ps1` (mismo patrón que `descargar_python.ps1`):
  compila un proyecto NuGet desechable y deja los `.dll` en `instalador\lib\` — no se
  versionan en git (binarios de terceros regenerables, igual criterio que
  `instalador\runtimes\`/`host\`/`workers\`, ver `.gitignore`).
- `Instalar.ps1`: crea `$base\lib` y copia `instalador\lib\*.dll` ahí (paso 3b).
- `build\generar_instalador.ps1`: verifica que `instalador\lib` no esté vacío antes de
  empacar (paso 5) y avisa si hace falta correr `descargar_librerias_externas.ps1`.
- `PRUEBA_JSON.ctx`, `PRUEBA_QR.ctx`, `PRUEBA_EXCEL.ctx`, `PRUEBA_WEBVIEW2.ctx` también
  agregados a `instalador\scripts\` como ejemplos genéricos (mismo criterio que
  `EJEMPLO_suma.csx`).

**Nota de validación:** antes de dejar los scripts de prueba, se compiló un proyecto
`net48` desechable (`C:\Compac\Backups\nuget_fetch\`, fuera del repo) contra las mismas
16 DLLs para confirmar que la API usada en los scripts compila sin errores — 0 errores.
Encontró y corrigió un error propio: se había anotado que ClosedXML dependía de
`RBush.dll`; la dependencia real es `XLParser.dll`.

---

## [2.22.0 → 2.23.0] — 2026-07-03 — Ejemplo Premium: Recepción de Compra y Factura de Compra (C#)

> Dos documentos nuevos, ambos DERIVADOS de una o varias Órdenes de Compra del mismo proveedor
> ("N OC → 1 documento"). Investigados primero contra una base de datos de pruebas antes de
> escribir una sola línea de SQL — siguiendo la regla del proyecto de no reinventar sin verificar
> el perfil real del documento.

### v2.22.0 — Recepción de Compra (módulo 184)
**Agregado**
- `PLANTILLA_EJEMPLO_RECEPCION_COMPRA_CSHARP.ctx`: busca proveedor con Órdenes de Compra
  activas, marca 1+ OC pendientes, **consolida partidas por producto** si el mismo aparece en
  varias OC (la cantidad se reparte automáticamente entre las OC de origen al guardar, la más
  antigua primero), y captura **lote (con fecha de caducidad) o número de serie** solo en los
  productos que lo requieren (`orgProduct.UseLot`/`UseSerialNumber`) — doble clic en la celda
  abre un capturador dedicado (textarea para pegar series, grid para varios lotes).
- `ErpContext.AgregarArticulo` (usado por los 3 lenguajes) ahora acepta `deliverDocumentItemId`
  para vincular la partida de la Recepción a su partida de OC origen — cálculo PROPIO de
  "cuánto falta recibir" por partida (`docDocumentItem.DeliverDocumentItemID`), necesario porque
  la vista nativa (`vwLBSProductsToDeliver`) solo soporta una OC por Recepción.
- A diferencia de la Orden de Compra, la Recepción **sí afecta inventario** (`AffectStockNEW`)
  y el costo se guarda igual al precio unitario (regla confirmada: en documentos de compra,
  costo = precio unitario).
- Series/lotes se guardan en `docDocumentSerialNumber`/`docDocumentLot` (confirmado contra una
  captura real con XEvents en `EXP-DOC-recepcion_compra_001`) — no en el campo simple
  `docDocumentItem.Lot`/`SerialNumber`, y NO hay método COM del SDK para esto (confirmado:
  el propio laboratorio ya lo investigó y concluyó que el INSERT directo es el patrón aceptado).
- Se excluyen del cálculo de pendientes las partidas de servicio (`orgProduct.ProductTypeID=4`)
  — un servicio no se recibe en inventario. Ojo: `ProductIsService` casi nunca está bien puesto,
  hay que revisar `ProductTypeID`.

**Corregido en el camino**
- Bug real de WinForms: refrescar un `DataGridView` DENTRO del mismo evento de su propio
  checkbox lanza `InvalidOperationException` (llamada reentrante a
  `SetCurrentCellAddressCore`) — se resuelve difiriendo el refresco con `BeginInvoke`.

### v2.23.0 — Factura de Compra (módulo 152)
**Agregado**
- `PLANTILLA_EJEMPLO_FACTURA_COMPRA_CSHARP.ctx`: a diferencia de Recepción de Compra, arranca
  de `ctx.GetSelectedIds()` (1+ Órdenes de Compra ya seleccionadas en el grid NATIVO de
  Comercial, no un buscador dentro de la ventana) — valida que todas sean del mismo proveedor.
  Muestra las partidas pendientes de facturar con **impuesto editable por partida** y columnas
  de Importe/Impuesto $/Total por línea, además del apartado de Totales general.
- Vínculo OC→Factura: cálculo propio con `docDocumentItem.SourceDocumentItemID` (columna
  distinta a `DeliverDocumentItemID` de la Recepción — cada tipo de documento derivado usa la
  suya). No había vista nativa equivalente para facturación; se construyó desde cero.
- La Factura de Compra **no afecta inventario** (sin `AffectStockNEW`) pero **sí genera la
  póliza contable automáticamente** al hacer `Save()` (`accPoliza`/`accPolizaTransaccion`) — no
  requiere código adicional.
- **Bug real encontrado y corregido en la plantilla:** `NuevoDocumento` crea un `PaymentAgenda`
  placeholder con `Amount=0` que `Save()` no corrige (regenera desde el caché interno de
  XEngine, no desde la BD) — la plantilla regenera la agenda de pago a mano después de `Save()`
  usando los porcentajes/plazos reales de `engPaymentTermDetail` según la condición de pago
  elegida. Confirmado contra la captura real en `EXP-DOC-factura_compra_001`.

---

## [2.21.10 → 2.21.12] — 2026-07-03 — Fin de la saga: sin transacción SQL explícita + Unicode al guardar/cargar scripts

> Continuación directa de la saga [2.21.3 → 2.21.9](#221-3--221-9--2026-07-02--saga-completa-busy-de-xengine--objeto-cerrado-en-botones-python).
> **Confirmado resuelto por el usuario** creando una Orden de Compra completa desde el botón del
> ribbon (v2.21.10) y guardando/cargando un script con acentos y emoji sin daño (v2.21.12).

### v2.21.10 — Quitar BEGIN TRANSACTION/COMMIT explícito de NuevoDocumento/AgregarArticulo
Después de v2.21.9, "la operación no está permitida si el objeto está cerrado" seguía saliendo en
`ctx.erp.NuevoDocumento` — pero el usuario probó un workaround propio (las mismas 4 anclas por
SQL directo desde Python, sin pasar por `ctx.erp.NuevoDocumento`) y **sí funcionó**. La diferencia
real entre ambos: mi SQL envolvía el INSERT del encabezado + las 4 anclas en un
`BEGIN TRANSACTION ... COMMIT TRANSACTION` explícito (con `TRY/CATCH`+`RAISERROR` para poder
hacer rollback); el workaround del usuario ejecutaba las mismas sentencias sueltas, sin
transacción manual.
- **Hipótesis (no confirmable sin acceso al código fuente de CONTPAQi):** el `DataLayer`/la
  conexión que expone CONTPAQi administra su propia transacción ambiental (patrón común en ERPs
  legado VB6/COM+, MTS-style), y un `BEGIN TRANSACTION` manual por T-SQL entra en conflicto con
  eso — el objeto queda en un estado inconsistente que luego se reporta como "cerrado".
- **Corregido**: se quitó el `BEGIN TRANSACTION`/`COMMIT`/`TRY-CATCH`/`RAISERROR` de
  `ErpContext.NuevoDocumento` y `AgregarArticulo`; ahora son sentencias `INSERT` sueltas en el
  mismo batch (un solo viaje de ida y vuelta, pero sin control transaccional). Se pierde
  atomicidad total (si una de las 4 anclas fallara, el documento quedaría a medias) — una
  concesión aceptada porque la alternativa era que la creación de documentos no funcionara en
  absoluto. **Confirmado por el usuario**: creó una Orden de Compra real desde el botón.

### v2.21.11 y v2.21.12 — Unicode al guardar y cargar scripts en zzBrosScript
Efecto colateral que reapareció después de resolver lo anterior: un botón guardado desde la
Consola volvía a mostrar acentos y emoji como "?" (ya se había visto y "arreglado" en v2.21.9,
arreglando solo la LECTURA de archivos locales — pero el usuario guarda y prueba directo en la
Consola, sin pasar por archivo). Investigado a fondo:
- **v2.21.11**: se confirmó que **guardar** un script por la conexión viva de CONTPAQi
  (`Com.Call(dataLayerOComoSeLlame, "Execute", sqlConTextoDelScript)`) puede angostar el texto a
  ANSI — pasa con el texto grande de un script (muchos comentarios/strings con acentos y emoji),
  no con SQL de negocio normal (de ahí que nadie lo hubiera notado antes). **Corregido**:
  `BrosGuardar` ahora prefiere una conexión `SqlClient` directa y parametrizada (Unicode perfecto
  garantizado por `SqlParameter` con `NVARCHAR(MAX)`); si no logra abrirla (por ejemplo, una
  instalación que solo acepta login SQL con contraseña y no la tiene guardada), cae automático al
  camino de siempre — nunca debe fallar solo por no tener la vía directa disponible. También se
  agregó a `ResolverCadena()` un intento de autenticación integrada de Windows usando
  servidor+base del `DataLayer` de CONTPAQi (confirmado que funciona en campo), para no depender
  de tener credenciales SQL guardadas en `broslmv_conn.txt`.
- **v2.21.12**: el fix de v2.21.11 no bastó — el mismo botón, ya guardado bien, se seguía viendo
  con acentos como "?" o "♦" (un carácter distinto, señal de que esta vez el daño lo hacía la
  LECTURA, no la escritura). Causa: `BrosCargar` (usado por `ExecuteFunction` para traer el
  código del script antes de ejecutarlo) leía por `Query()` → `Ado()` → la misma conexión viva de
  CONTPAQi, que TAMBIÉN puede angostar el texto al leer, no solo al escribir. **Corregido**: igual
  patrón que `BrosGuardar` — `BrosCargar` prefiere una conexión `SqlClient` directa, con el mismo
  respaldo automático si no está disponible.
- **Nota importante:** ninguno de estos dos fixes puede reparar texto que YA se guardó dañado
  antes de que existieran. El script de prueba del usuario (`NvaSolicitud`) tenía caracteres de
  reemplazo (`U+FFFD`, irreversibles — no simples "?") guardados desde antes de v2.21.11; se
  reconstruyeron a mano las ~26 palabras afectadas comparando contra
  `PLANTILLA_EJEMPLO_ORDEN_COMPRA_PYTHON.py` y se volvieron a subir por conexión directa. Si
  algún otro `AppKey` guardado antes de v2.21.12 muestra símbolos raros, hay que volver a
  guardarlo (ahora sí quedará bien) o pedir que se revise/repare igual que este caso.

---

## [2.21.3 → 2.21.9] — 2026-07-02 — Saga completa: "busy" de XEngine + "objeto cerrado" en botones Python

> Esta fue una investigación larga con varios callejones sin salida — se documenta completa
> (con lo que NO funcionó) porque cada paso enseña algo real del motor de CONTPAQi. Dos
> síntomas reportados por el usuario, en dos botones distintos, con DOS causas raíz separadas:
> 1. El botón Python de Orden de Compra "tarda muchísimo" y termina en el diálogo nativo de
>    Windows/COM *"the other application is busy"* (título XEngine), sin que "Retry" lo resuelva.
> 2. Una vez que ese ya no salía, `ctx.erp.NuevoDocumento` fallaba con **"La operación no está
>    permitida si el objeto está cerrado"** — pero SOLO como botón del ribbon, no ejecutando el
>    mismo script desde la Consola.

### Causa raíz #1 — el "busy": Consola.Ejecutar corría Python síncrono (v2.21.4)
La traza de v2.21.2 (`PythonErp_AAAAMMDD.txt`) mostró que ninguna llamada `ctx.erp`/`ctx.query`
individual tardaba más de unos ms — y `host-audit.jsonl` confirmó que el script SIEMPRE terminaba
"OK", nunca se colgaba de verdad. La causa real: `Consola.Ejecutar()` (botón "Ejecutar" de la
consola de scripts) llamaba a `HostClient.EjecutarPython(...)` **directo, bloqueando el hilo de
Comercial** durante TODO el tiempo que la ventana Python interactiva estuviera abierta (minutos).
El botón del ribbon (`ClsMain.EjecutarPython`) ya usaba `Task.Run` + `UiPump` desde v2.19.0 —
por eso solo pasaba ejecutando desde la Consola. Además, el log de auditoría mostró ejecuciones
con `ExecutionId` distintos y ventanas de tiempo **encimadas** (el usuario reintentaba mientras
la anterior seguía corriendo), cada una compitiendo por el mismo hilo — así que también se agregó
una guardia (`GuardiaEjecucion`) que bloquea un segundo clic mientras el mismo botón/script ya
está en ejecución (ribbon y Consola).
- *Intento fallido y revertido en la misma versión:* antes de encontrar esto se probó cachear la
  conexión ADO de forma estática (para evitar repetir un recorrido COM lento). Se revirtió porque
  causó el problema #2 más abajo — la caché reutilizaba una conexión que podía dañarse.

### Causa raíz #2 — "objeto cerrado": la conexión ligada al grid se cierra (v2.21.6 – v2.21.8)
Con el "busy" resuelto, apareció "La operación no está permitida si el objeto está cerrado" al
hacer Guardar — **solo como botón**, nunca desde la Consola con el mismo script. La diferencia:
`Conexion.ObtenerAdo` prefería `XEngineLib.janusGrid.ADORecordset.ActiveConnection` (la conexión
ligada a la lista de documentos visible) sobre el `DataLayer` (una conexión de propósito general,
disponible en cualquier pestaña). Un botón de ribbon corre típicamente con el grid del módulo
activo visible; la Consola casi nunca. Una ventana Python interactiva puede quedar abierta varios
minutos — tiempo de sobra para que el grid se refresque o cierre y deje la conexión inservible.
Las consultas de arranque (proveedores, catálogos, folio) ya habían corrido cuando la conexión
seguía viva; el INSERT de `NuevoDocumento`, minutos después al hacer clic en Guardar, ya la
encontraba cerrada.
- **v2.21.5**: primer paso indispensable — `NuevoDocumento`/`AgregarArticulo` solo decían "no se
  pudo crear el encabezado/partida" sin la causa real, porque `Com.Call()` atrapa la excepción de
  COM y la deja en `Com.LastError` sin que nadie la lea. Se agregó el detalle real al mensaje —
  sin este paso, **nunca se hubiera visto** el mensaje real de ADO para diagnosticar lo demás.
- **v2.21.6**: se invirtió el orden de preferencia — `DataLayer` primero, `janusGrid` de respaldo.
- **v2.21.7**: no bastaba con elegir mejor una vez; `ScriptContext.Ado()` cacheaba la conexión
  por TODA la ejecución (podía durar minutos). Se hizo auto-sanadora: revalida con un `SELECT 1`
  antes de cada uso y, si ya no sirve, se vuelve a resolver.
- **v2.21.8**: el fix de v2.21.7 en realidad EMPEORÓ las cosas al principio — la validación
  `PuedeEjecutar()` nunca cerraba el recordset del `SELECT 1` de prueba. Al llamarse ahora en
  CADA `ctx.query`/`ctx.erp` (antes, solo una vez por ejecución), se acumulaban recordsets
  abiertos sin cerrar en la misma conexión hasta agotar el límite de resultados simultáneos de
  ADO (sin MARS) — terminaba manifestándose exactamente como "objeto cerrado". Cerrar ese
  recordset de prueba fue el fix que de verdad lo resolvió (confirmado por el usuario creando una
  Orden de Compra completa desde el botón del ribbon).

### v2.21.9 — Encoding: acentos/emoji se guardaban como "?" en zzBrosScript
Efecto colateral descubierto al confirmar el fix anterior: el botón de prueba ("NvaSolicitud",
guardado desde la Consola con Plantillas → Ejemplo Premium) mostraba signos de interrogación en
vez de acentos y emoji ("Informaci?n", los iconos "➕/❌" del ribbon). Causa: `Consola.cs` leía
archivos con `File.ReadAllText(ruta)` **sin especificar encoding** — en .NET Framework, sin BOM
en el archivo, puede caer al codepage ANSI del sistema en vez de UTF-8, convirtiendo cualquier
carácter fuera de ese codepage a "?". Esto pasa al cargar una plantilla (`CargarPlantillaArchivo`,
usado por el menú Plantillas) o al usar "Abrir" para importar un `.ctx`/`.csx`.
- **Corregido**: ambos `File.ReadAllText` ahora especifican `Encoding.UTF8` explícito.
- El script `NvaSolicitud` ya dañado se volvió a subir a `zzBrosScript` con el contenido correcto
  (UTF-8, vía consulta parametrizada — evita el mismo problema por la vía de la terminal/sqlcmd).
  Si algún otro `AppKey` guardado antes de esta versión muestra "?", hay que volver a guardarlo
  ahora que el fix ya está.

---

## [2.21.2] — 2026-07-02 — Diagnóstico: traza de llamadas ctx.erp/ctx.query desde Python

> El usuario reportó que el botón Python de Orden de Compra "tarda muchísimo" y termina en un
> diálogo nativo de Windows/COM: *"This action cannot be completed because the other application
> is busy"* (título "XEngine"), y que "Retry" no lo resuelve — se repite indefinidamente.
> Se investigó a fondo antes de tocar código: se descartó que fuera un bloqueo de SQL Server
> (`sys.dm_exec_requests` sin sesiones bloqueadas), se descartó que la nueva consulta de impuestos
> agregada en v2.21.0 fuera lenta (1 ms medido directo contra la base), y se confirmó que la
> plantilla C# de Orden de Compra (que llama exactamente los mismos métodos `GetFolioPrefix`/
> `GetNextFolio` de `ctx.erp`, pero **directo en el hilo de Comercial**, sin pasar por `UiPump`)
> **sí funciona**. Esto apunta a algo específico de la llamada marshalada Python→`UiPump`→COM
> para este módulo, pero no fue reproducible fuera de una sesión real de Comercial abierta —
> no se pudo diagnosticar la causa raíz exacta en esta sesión.

**Agregado (solo diagnóstico, sin cambiar comportamiento)**
- Nueva traza `C:\BrosLMV\logs\PythonErp_AAAAMMDD.txt`: cada llamada `ctx.erp.*` o `ctx.query`/
  `ctx.scalar`/`ctx.execute` que hace un script Python (vía `CtxErpRunner`/`CtxSqlRunner` en
  `HostClient.cs`) ahora escribe una línea **antes** de la llamada bloqueante (método + argumentos)
  y otra **después** con cuánto tardó. Como es solo un archivo de texto (no depende de SQL ni de
  COM), si una llamada se cuelga, el log ya tiene escrita la línea "INICIA" de esa llamada — así
  la próxima vez que pase esto se puede ver exactamente cuál método quedó atorado.
- **Siguiente paso si vuelve a pasar:** reproducir el problema y revisar las últimas líneas de
  ese archivo de log (buscar una línea "INICIA" sin su "termina en ... ms" correspondiente).

---

## [2.21.1] — 2026-07-02 — Fix: ventana de detalle de producto amontonada

> El usuario probó v2.21.0 y el detalle de producto funcionaba, pero la ventana se veía "toda
> amontonada" (secciones muy pegadas, texto encimado con el borde de los grupos). Se rediseñó el
> layout con más espacio.

**Cambiado**
- Ventana de detalle de producto: de 620×560 a 700×800 (alto ajustado dinámicamente al final según
  cuánto contenido haya). Cada grupo ("Datos generales", "Clasificaciones") ahora usa pares
  etiqueta/valor en columnas en vez de texto corrido con "·", con más aire entre líneas.
  Las 3 tablas (existencias, listas de precio, proveedores) crecieron de ~70-80px a 100-120px de
  alto (más filas visibles sin scroll) y ahora tienen borde propio (`FixedSingle`) para separarse
  mejor del fondo. Separación entre secciones de 20-24px en vez de quedar pegadas.

---

## [2.21.0] — 2026-07-02 — Totales y detalle de producto en Orden de Compra

> El usuario pidió, tras probar v2.20.1: (1) un apartado de totales con Subtotal, Descuento,
> Impuestos, Total y Total en letra; y (2) que al hacer doble clic en una partida se muestre el
> detalle del producto (datos generales, clasificaciones, existencias, listas de precio y
> precios por proveedor). Ambas plantillas de ejemplo de Orden de Compra (C# y Python) se
> actualizaron igual, para que sigan siendo espejo una de otra.

**Agregado**
- Grupo **"5. Totales"** en ambas plantillas de Orden de Compra: Subtotal, Descuento, Impuestos,
  Total (calculados partida por partida: `neto = importe - importe×descuento%`,
  `impuesto = neto×TaxPerc`) y **Total en letra** ("SON: SEISCIENTOS VEINTISÉIS PESOS 40/100 M.N.")
  mediante un conversor número→letras en español (incluido en la propia plantilla, sin
  dependencias externas). El total en letra se recalcula también si el usuario cambia la moneda.
- **Detalle de producto**: doble clic en cualquier partida de la tabla abre una ventana con
  datos generales (clave, nombre, descripción, unidades, costo, precio de lista, % de impuesto),
  clasificaciones (Categoría 1-4 del catálogo), existencia por almacén (`orgProductKardex`),
  listas de precios asignadas (`orgProductPriceList` + `orgPriceList`) y precios negociados por
  proveedor (`orgProductSupplier`). Es una ventana de solo lectura, propia de esta plantilla (no
  bloquea Comercial: es hija de la ventana ya modeless de la Orden de Compra).

**Detalles técnicos**
- El % de impuesto de cada partida (`Item.TaxPerc`/`tax_perc`) ya se resolvía en
  `ErpContext.AgregarArticulo` (desde v2.20.1) pero no se guardaba en la ventana; ahora también
  se consulta `vwLBSTaxPerc` al construir el combo de impuestos y se guarda por partida, para
  poder calcular el desglose de totales sin tener que volver a preguntar a la base de datos.
- Verificado offline antes de pedir prueba en Comercial: C# compiló con `ScriptRunner.Compilar`
  (0 errores); Python se validó con una prueba de humo (`.temp_tests/smoke_test_oc_python3.py`)
  que arma 1 partida (4 pzas × $150, 10% descuento, IVA 16%) y confirma Subtotal=$600.00,
  Descuento=-$60.00, Impuestos=$86.40, Total=$626.40, y que `mostrar_detalle_producto` no lanza
  excepciones con datos simulados de existencias/listas/proveedores.

---

## [2.20.1] — 2026-07-01 — Fix: impuesto, descuento y estatus de entrega en Orden de Compra

> El usuario probó v2.20.0 y encontró 3 problemas reales: el impuesto no se aplicaba, no había
> columna de descuento, y el documento quedaba con "Estatus de entrega: No Aplica". Los tres se
> confirmaron con datos reales (snapshot de una OC nativa capturado antes en esta sesión) y se
> corrigieron en la raíz — no solo en la plantilla, sino en `ErpContext.AgregarArticulo`, así que
> **benefician también a la Requisición** y a cualquier script futuro que la use.

### Corregido — causa raíz (`ErpContext.AgregarArticulo`, afecta C# y Python por igual)
- **El impuesto no se aplicaba de verdad:** `AgregarArticulo` sí guardaba `TaxTypeID`, pero
  **nunca guardaba `TaxPerc`** (el % que usa el motor de recálculo) — quedaba en 0 aunque
  `TaxTypeID` estuviera bien. Confirmado contra un documento nativo real: `TaxTypeID=5,
  TaxPerc=0.16`. Ahora `AgregarArticulo` resuelve el `%` desde **`vwLBSTaxPerc`** (la misma vista
  que usa LBS) y lo guarda junto con `TaxTypeID`.
- **Nuevo parámetro `taxTypeIdOverride`** (opcional, por defecto usa el de `orgProduct`): permite
  que un script (o la UI) **cambie el impuesto** en vez de aceptar siempre el del catálogo.
- **Nuevo parámetro `descuentoPerc`** (fracción, 0.05 = 5%): puebla `docDocumentItem.DiscountPerc`,
  que antes nunca se llenaba (columna que ya existía en la tabla, simplemente no se usaba).

### Corregido — plantillas de Orden de Compra (C# y Python)
- **Combo de Impuesto**: se precarga con el impuesto del producto (`orgProduct.TaxTypeID`) al
  seleccionarlo, y el usuario **lo puede cambiar** antes de agregar la partida. Nueva columna
  **Impuesto** en el grid (informativa).
- **Campo y columna de Descuento %**: captura por partida, editable directo en el grid.
- **`ctx.erp.UpdateStatusDelivery(doc)`**: se llama después de `Save`. Sin esto, el documento
  quedaba con "Estatus de entrega: No Aplica" en el grid nativo aunque estuviera bien creado —
  `RecalcCompleto` no lo calcula, hay que pedirlo aparte.
- **Verificado offline** antes de pedir prueba en Comercial: C# compiló con Roslyn real; Python
  pasó una prueba de humo que confirma precarga de impuesto, cambio de descuento (10%), y that el
  grid refleja Impuesto/Desc%/Importe correctamente — incluye reverificar que el combo de
  Impuesto (`DropDownList` nuevo) no cae en la trampa de pythonnet ya documentada (objetos Python
  en `Items`); usa el mismo patrón de strings + lista paralela.

## [2.20.0] — 2026-07-01 — Ejemplo Premium: Orden de Compra (C# y Python)

> Segundo par de plantillas completas (además de la Requisición): **Orden de Compra**
> (módulo 183), verificada previamente esta sesión creando documentos reales en
> `Coctel_de_Ideas` (F1-F3, DocumentID 11556-11560). Reutiliza el mismo patrón modeless +
> protecciones de la Requisición, con las diferencias reales de una OC.

### Agregado
- **`PLANTILLA_EJEMPLO_ORDEN_COMPRA_CSHARP.ctx`** y **`PLANTILLA_EJEMPLO_ORDEN_COMPRA_PYTHON.py`**
  ("Ejemplo Premium · C#/Python Orden de Compra" en el menú Plantillas). A diferencia de la
  Requisición (módulo 1040, solo cantidad), una Orden de Compra:
  - Captura **precio unitario por partida** (`ctx.erp.AgregarArticulo(doc, pid, qty, precio)`) —
    es un compromiso de compra real con el proveedor, no solo una solicitud interna.
  - Captura **fecha de entrega esperada** (`DateDelivery`), además de la fecha del documento.
  - Muestra **subtotal estimado en vivo** (informativo; el total real con impuestos lo calcula
    `RecalcCompleto`) y una columna **Importe** calculada (cantidad × precio) en el grid.
  - Actualiza `orgProductSupplier.CostPrice` con el precio realmente negociado (antes, en la
    Requisición, quedaba en 0 porque no había precio que registrar).
  - **No afecta inventario** (módulo 183; eso ocurre hasta la Recepción de Compra, módulo 184) —
    por eso tampoco llama `AffectStockNEW`, igual que la Requisición.
- **Verificado offline** antes de pedir prueba en Comercial: C# compiló con el motor Roslyn real
  (`ScriptRunner.Compilar`, arnés en `.temp_tests/harness_pythonui`); Python pasó una prueba de
  humo que ejercita búsqueda + selección + captura de precio/cantidad + cálculo de importe.
- **`MANUAL.md`**: tabla comparativa de las dos plantillas premium (Requisición vs Orden de
  Compra) en la sección 10.3.

## [2.19.1] — 2026-07-01 — Plantillas base modeless + MANUAL.md + regla de documentación

> Verificado por el usuario que v2.19.0 funciona ("quedó perfecto"). Esta versión consolida el
> aprendizaje: **plantillas de arranque** para no repetir las reglas de memoria, documentación
> completa en `MANUAL.md`, y una **regla nueva** para que esto siempre se documente ahí.

### Agregado
- **`PLANTILLA_BASE_CSHARP_WINFORMS.ctx`** y **`PLANTILLA_BASE_PYTHON_WINFORMS.py`**: ventana
  modeless en blanco (sin lógica de negocio) con las protecciones ya aplicadas — punto de partida
  para cualquier script nuevo con ventana. Registradas en el menú **Plantillas** de la consola
  como "Base · C# WinForms" / "Base · Python WinForms".
- **`MANUAL.md` §10 "Ventanas WinForms: modeless (no bloquear Comercial)"** (nueva, renumera
  §10-14 → §11-15): tabla comparativa C# vs Python, reglas para que no truene, y dónde están las
  plantillas base. `MANUAL.md` es lo que usa quien escribe scripts — antes esto solo vivía en
  `UI_VENTANAS.md` (doc técnico interno) y en el `CHANGELOG.md`.

### Nota de proceso — regla nueva
- **Toda recomendación/patrón/límite descubierto** (no solo cambios de código) se documenta
  **también** en `MANUAL.md`, bien explicado (qué pasa, por qué, qué hacer) — no basta con el
  `CHANGELOG.md` ni con mencionarlo solo en el chat. Ver `ESTADO.md` → REGLA DE ORO,
  `DESARROLLO.md` §7 y `INDICE.md` → "Regla de documentación".

## [2.19.0] — 2026-07-01 — Botones modeless: C# ya no bloquea Comercial; Python tampoco

> Objetivo del usuario: poder minimizar la ventana de un botón (p. ej. crear una requisición) y
> **seguir trabajando en Comercial**, incluso con **varios botones abiertos a la vez**.

### Cambiado (C# — nivel 1)
- **Plantilla "Ejemplo Premium · C# WinForms"**: `frm.ShowDialog()` → `frm.Show()` (modeless,
  mismo patrón ya probado en la consola desde v2.13.0). Se pueden abrir varias ventanas del mismo
  botón a la vez; cada una es independiente.
- **Blindaje agregado**: `Buscar()` (hace SQL) no tenía `try/catch`. Con `ShowDialog()` un error
  ahí solo rompía el script; con `Show()` (modeless) podía **tumbar Comercial** porque ya nadie
  atrapa la excepción. Ahora un error de búsqueda solo muestra un mensaje, sin afectar Comercial.
- **Mismo blindaje en la plantilla "Ejemplo Premium · Python WinForms"** (`buscar()`): aquí un
  error nunca puede tumbar Comercial (Python corre en su propio proceso, aislado), pero sin
  `try/except` la ventana se quedaba "muda" sin explicar el error al usuario.

### Agregado (Python — nivel 2, cambio de fondo)
- **Los botones Python ya NO bloquean a Comercial** mientras su ventana está abierta. Antes,
  `ClsMain.EjecutarPython` esperaba (síncrono) el intercambio completo con el host — con una
  ventana interactiva abierta minutos, Comercial se congelaba entero.
- **`UiPump`** (nuevo, en `ClsMain.cs`): un control invisible creado una vez en el hilo de
  Comercial. `ClsMain.EjecutarPython` ahora lanza el intercambio con el host en segundo plano
  (`Task.Run`) y regresa de inmediato — Comercial sigue respondiendo. Las llamadas reales de
  `ctx.query`/`ctx.erp` (en `CtxSqlRunner`/`CtxErpRunner`, `HostClient.cs`) se remiten con
  `UiPump.Invoke(...)` de vuelta al hilo de Comercial: **sigue siendo el único hilo que toca el
  COM de XEngine** (evita el riesgo real de corrupción por acceso concurrente desde dos hilos),
  solo que Comercial ya no se queda esperando bloqueado mientras tanto.
- Permite **varios botones Python simultáneos**: cada uno corre en su propio `Task`, y sus
  llamadas a `ctx.*` se sirven una por una (en orden) desde el mismo hilo — sin bloquearse entre
  sí más que el tiempo real de cada llamada SQL/COM (milisegundos).
- **Verificado offline** (sin Comercial): arnés de pruebas (`.temp_tests/harness_pythonui/TestUiPump.cs`)
  simula el hilo de Comercial con su propio *message loop* y confirma, con reflexión sobre el
  addon compilado, que `UiPump.Invoke` siempre ejecuta en ese hilo (nunca en el de fondo), sin
  *deadlock*, incluso con 8 llamadas concurrentes ("botones" simultáneos).

### Nota de proceso
- Este cambio **no toca** los scripts Python en sí (`ctx.py`/plantillas): el `frm.ShowDialog()`
  dentro del proceso Python (pythonnet) no necesitaba cambiar — ese proceso ya corre aparte y su
  propia ventana ya podía minimizarse independientemente; lo que congelaba Comercial era la
  espera del lado del addon, ya resuelta.

## [2.18.1] — 2026-07-01 — Diagnóstico real de errores del host + plantilla Python WinForms

> Se detectó que el **instalador distribuido llevaba v2.17.0** (varias versiones atrás), por
> eso una reinstalación mostraba menos de lo esperado y sin las plantillas nuevas. Esta versión
> consolida los pendientes y **regenera el instalador completo**.

### Corregido
- **`HostClient.EjecutarPython`** ya no oculta el error real: cuando el intercambio con el host
  fallaba, la excepción quedaba envuelta en una `AggregateException` cuyo mensaje genérico
  ("Se han producido uno o varios errores.") no decía nada útil. Ahora se desenvuelve la
  excepción real y, si el host emitió algo a su stderr, se anexa al mensaje de error.
- Se ELIMINÓ un riesgo de bloqueo: el proceso del host redirigía su stdout/stderr sin que nadie
  los leyera nunca (un pipe sin drenar se puede llenar y colgar al proceso hijo); ahora se drenan
  de forma asíncrona.

### Agregado
- **Plantilla "Ejemplo Premium · Python WinForms"** terminada y verificada offline (arnés de
  pruebas + smoke tests): la misma ventana de Requisición de Compra que la versión C#, pero
  100% en Python vía `pythonnet` (WinForms real, mismo .NET Framework del equipo). Documenta un
  límite real de pythonnet: los `ComboBox`/`ListBox` con `Items` de objetos Python pierden la
  selección al crear el control nativo (hay que usar strings + listas paralelas).

### Nota de proceso
- El instalador (`dist/BrosLMV-Instalador.exe`) se regeneró completo con esta versión: addon +
  host + **todas** las plantillas (`instalador/scripts`). **Recordatorio:** regenerar el
  instalador (`build/generar_instalador.ps1` + `build/generar_exes.ps1`) después de CADA cambio
  que deba llegar a una instalación nueva — no basta con el commit.

## [Plantilla] — 2026-07-01 — Requisición de compra (C# WinForms) operativa

> Cambio de **asset** (plantilla `instalador/scripts/PLANTILLA_EJEMPLO_CSHARP_WINFORMS.ctx`), **sin
> cambio de binario** (no se toca `AssemblyVersion`). Rehecha la plantilla "Ejemplo Premium · C#
> WinForms" (Solicitud de Compra, módulo 1040) para que sea funcional de punta a punta.

### Cambiado / Corregido (plantilla requisición)
- **Crear = botón Guardar de la cinta** (F5). Se quitaron los botones del pie (Crear/Cancelar);
  el pie solo muestra "Elaboró" + nota. **Botones de la cinta operativos** (Guardar, Guardar y
  Nueva, Cancelar, Limpiar); Vista previa / Imprimir muestran "en proceso". **Quitado** el botón
  de Ayuda (y Adjuntar/Comentarios, para dar espacio; Comentarios sigue como sección propia).
- **Moneda y condición de pago se leen del sistema** (`vwLBSCurrencyList`, `vwLBSPaymentTermList`
  con `Buys=1`) y se aplican al crear (`CurrencyID`+`Rate`+`PaymentTermID`).
- **Proveedor con búsqueda incremental** por nombre o RFC (combo editable filtrado).
- **Búsqueda de productos**: el botón 🔍 ahora busca (o abre catálogo si el texto está vacío) y
  avisa cuando no hay resultados.
- **Folio editable**: precargado con el consecutivo (`GetNextFolio` mód. 1040) pero modificable;
  se aplica al guardar. Serie también editable. **Fecha visible** (se movió el panel de info a la
  derecha de la cinta, sin encimarse con los botones).
- **Cinta superior en gris oscuro** (slate) para diferenciarla de los paneles blancos.

## [2.18.0] — 2026-06-29 — 4 anclas + campos universales + partida nativa completa

> **Salto mayor de fidelidad.** `NuevoDocumento` ahora crea las 4 anclas 1:1 (Ext/Extra/CFD/
> PaymentAgenda) + los campos universales de docDocument. `AgregarArticulo` llena la partida
> como el nativo. **Ya no se necesita clonAncla ni UPDATE de esos campos.** Validado campo
> por campo contra documentos manuales (entrada, salida, solicitud). Fix encoding Python UTF-8.
> MANUAL.md reescrito con API completa de ctx.erp (84 métodos, recetas por tipo).

### Agregado
- **4 anclas automáticas en `NuevoDocumento`:** ahora se insertan `docDocumentExt` (IDExtra=
  DocumentID), `docDocumentExtra`, `docDocumentCFD` (FinancialOperationID=0, Anexo20Ver='4.0')
  y `docDocumentPaymentAgenda` (1 parcialidad 100%). Antes solo se creaba `docDocumentExt`.
- **Campos universales de docDocument:** `NuevoDocumento` ahora llena `MustBeSynchronized=1`,
  `ExportID=1`, `DateCost`, `DateDocDelivery`, `DateFrom`, `DateTo`, `DateLastPayment` (=
  fecha actual). Igual que el nativo.
- **Partida nativa en `AgregarArticulo`:** ahora llena `ApplyGlobalDiscount=1`,
  `DeductiblePerc=1`, `IsBusinessOperation=1`, `MustBeDelivered=1`, `DateItem`=fecha,
  `CoefUnit=1`, `ClaveUnidad` y `ObjetoImpuesto` copiados de `orgProduct`.
- **`ctx.erp.LastError`:** propiedad que expone el último error de late-binding COM.
  Revisar después de operaciones críticas (AffectStockNEW, Save, CancelDocument, Delete).
- **`Com.LastError`:** la clase `Com` ahora captura y expone el mensaje de excepción de
  cada llamada COM fallida (antes se tragaba en silencio).
- **Transacciones en builders:** `NuevoDocumento` y `AgregarArticulo` ahora envuelven sus
  INSERT en `BEGIN TRANSACTION` / `COMMIT`. Si cualquier ancla falla, se revierte todo.
  Evita documentos huérfanos con anclas parciales.
- **`ctx.msg` Python → UI:** los callbacks de Python (`ctx.msg`) ahora se relayan al addon
  vía `UiRequest` por el pipe y muestran un `MessageBox` real en Comercial. Antes solo
  escribían a `host-callbacks.log`. Nuevo `RelayingCallbackSink` + `AtenderUiRequest` en
  el addon.
- **MANUAL.md reescrito:** documentación completa de `ctx` y `ctx.erp` (84 métodos),
  fichas por función, recetas por tipo de documento (entrada, salida, OC, FC, solicitud,
  traspaso), catálogos, advertencias (Delete vs CancelDocument), y cheat sheet.

- **Primera plantilla comunitaria de documentos:** `PLANTILLA_REQUISICION_COMPRA.ctx`
  documenta linea por linea como crear una solicitud de compra (modulo 1040) usando
  `ctx.erp.NuevoDocumento`, `AgregarArticulo`, `RecalcCompleto` y `Save`. La consola carga esta
  plantilla desde `C:\BrosLMV\scripts` mediante una entrada explicita en `PLANTILLAS`.

### Corregido
- **Encoding UTF-8 en Python** (`PythonProcess.cs`): el `ProcessStartInfo` ahora configura
  `StandardOutputEncoding=UTF8` + `StandardErrorEncoding=UTF8`. El payload JSON se escribe
  como bytes UTF-8 explícitos. Corrige el bug Ñ→Ã en scripts Python con datos en español.
- **`LastError` en `Com`:** `GetProp`, `GetIndexed`, `SetProp`, `Call` ahora limpian y
  asignan `LastError` en cada operación. Expuesto vía `ctx.erp.LastError`.

### Notas
- `AssemblyVersion` → **2.18.0.0**. Rama `fix/documentos-anclas-partida-nativo`.
- **Rompe compatibilidad si el script hacía `clonAncla` o UPDATE de campos universales:**
  eliminar esas líneas (causarían PK duplicada o redundancia).
- `ctx.erp.LastError` es `null` si la última llamada COM fue exitosa. Si no es null,
  la operación falló y debe tratarse el error.

---

## [2.17.0] — 2026-06-28 — Documentos más fieles al nativo (docDocumentExt + costo de entrada)

> Dos mejoras aditivas en `ctx.erp` para crear documentos idénticos al comportamiento nativo de
> CONTPAQi Comercial v11.00, validadas contra una base de datos de pruebas. Sin romper llamadas
> existentes.

### Agregado
- **`NuevoDocumento` crea la fila ancla en `docDocumentExt`.** Todo documento nativo de CONTPAQi
  inserta una fila en `docDocumentExt` (relación 1:1 implícita: `IDExtra = docDocument.DocumentID`;
  `IDExtra` es bigint **no identidad**). Es el ancla de los campos extra del documento. Antes la
  consola no la creaba, así que sus documentos no eran 100% fieles al nativo y se romperían si la
  empresa definía campos extra. Ahora, tras el INSERT de `docDocument`, se ejecuta
  `INSERT INTO docDocumentExt (IDExtra) VALUES (<nuevoDocumentID>)`.
- **`AgregarArticulo` acepta `costo` y puebla `CostPrice`.** Nuevo parámetro opcional
  `costo` (default `-1` = no setear): si `costo >= 0`, el INSERT de `docDocumentItem` incluye
  `CostPrice` (costo de **entrada** de inventario, distinto de `UnitPrice`). Sin esto, las entradas
  de almacén/recepciones/traspasos entraban a costo 0 y la valuación (`orgProductCostComercial`,
  costo promedio) quedaba en 0. Debe poblarse **antes** de `RecalcCompleto`/`CalcularCostos`/
  `AffectStockNEW`, que es lo que costea. Firma nueva:
  `AgregarArticulo(documentId, productId, cantidad=1, precioUnitario=-1, costo=-1)`.

---

## [2.16.0] — 2026-06-27 — Editar registros existentes (ctx.registro)

> Un script Python ya puede **cargar un registro existente** de cualquier tabla, modificar campos
> y guardar — solo se envían los campos que cambiaron. Complementa `ctx.nuevo` (INSERT) con la
> pieza que faltaba: **cargar→modificar→guardar**.

### Agregado
- **`ctx.registro(tabla, pk)`** en el SDK Python (`workers/python/broslmv/ctx.py`): carga un
  registro existente por su PK (columna identidad), devuelve un `_Record` con todos los campos
  poblados. Después se puede `.set()` o asignar campos, y `.actualizar()` envía **solo los cambios**
  (diff contra el estado original). `.eliminar()` también funciona (borrado lógico).
- Referencia de `ctx.registro` en el panel de la consola (`Consola.cs`).

### Cambiado
- **`.actualizar()` ahora es incremental:** tanto en registros nuevos (`ctx.nuevo`) como cargados
  (`ctx.registro`), solo envía al UPDATE los campos que difieren del estado original. Antes enviaba
  todos los campos excepto la PK.

---

## [2.15.0] — 2026-06-27 — Crear documentos desde Python y C# (+ active-record)

> Un botón ya puede **crear documentos** (cotización, pedido, orden de compra…) en pocas líneas,
> con el mismo poder en **C# y Python**. Reusa `ErpContext` y el relay; **sin copiar código de
> terceros**. El set de campos y el flujo salieron del esquema real de la base de datos.
> Verificado en una base de pruebas creando órdenes de compra y comparando campo a campo contra
> lo que genera Comercial de forma nativa.

### Agregado
- **`ctx.erp.NuevoDocumento(moduleId, depotId, businessEntityId=0)`** → crea el encabezado
  (`docDocument`) con los defaults correctos del módulo (DocumentType/DocRecipient vía
  `GetModuleParameter`, `OwnedBusinessEntityID`, folio vía LBS, LanguageID=3, CurrencyID=3, Rate=1)
  y devuelve el `DocumentID`.
- **`ctx.erp.AgregarArticulo(documentId, productId, cantidad=1, precio=-1)`** → agrega una partida
  (`docDocumentItem`) leyendo los datos del producto de `orgProduct`; tras agregar, `RecalcCompleto`.
  Ambos en `ErpContext` (C#), expuestos a Python por el relay (reflexión, sin código de host nuevo).
- **Active-record genérico `ctx.nuevo("tabla")`** en el SDK Python (`workers/python/broslmv/ctx.py`):
  registro dict-like con `.set(**campos)`, **`.guardar()`** (INSERT con `OUTPUT INSERTED.<PK>`,
  detecta la columna identidad), **`.actualizar()`** y **`.eliminar()`** (borrado lógico). Respeta
  el modo solo-lectura. Cimiento del futuro motor no-code.
- Referencias de la consola: nuevas entradas en C# y Python (`NuevoDocumento`, `AgregarArticulo`,
  `RecalcCompleto`, `OwnedBusinessEntityId`, `ctx.nuevo`).

### Corregido
- **Los scripts C# ya muestran su valor de retorno** en el panel de Salida: la consola ignoraba el
  `return` de los scripts C# (solo decía "Ejecución terminada"). `ScriptRunner.EjecutarConValor`
  captura el `ReturnValue` y la consola lo muestra (como Python). Beneficia a TODO script C#.
- `AgregarArticulo` puebla el `Total` de la partida (= cantidad × precio), como en un documento real.

## [2.14.0] — 2026-06-27 — Versión visible + control de versiones (Acerca de)

> La consola ahora **muestra su versión** (encabezado y barra de estado) y tiene un **Acerca de**
> con la versión, la fecha de compilación y un botón a las **notas de versión** (HTML). Así, en
> cualquier instalación se sabe **qué versión está corriendo** y qué cambió.

### Agregado
- **Versión visible** en la consola: en el subtítulo del encabezado (`Consola de scripts · v2.14.0`)
  y como **link en la barra de estado**. Se lee de `AssemblyVersion` en memoria (costo cero).
- **Diálogo "Acerca de"** (botón en la barra + clic en la versión de la barra de estado): logo,
  versión, **fecha de compilación** (última escritura de la DLL) y botón **"Ver notas de versión"**.
- **Notas de versión** (`src/assets/notas_version.html`, **recurso embebido**): historial legible
  por versión. Al abrirlas, se extraen a `%TEMP%` y se abren en el **navegador del sistema**
  (`Process.Start`) — **no** se usa un control web dentro del proceso, para no penalizar velocidad.
- **Versión en el instalador**: la pantalla de bienvenida muestra **la versión del addon que va a
  instalar**, leída del `payload.zip` embebido (`BrosLMVClsMain.dll`), no la del propio EXE. Así
  cada instalación queda identificada. Versiones de `Empresas.csproj`/`Desinstalador.csproj`
  sincronizadas a la del addon (estaban en 2.3.0).
- **Bienvenida del instalador más clara y honesta**: explica que **todo es autocontenido en
  `C:\BrosLMV`** y **no descarga nada de internet**. Detalla qué se instala — la consola/addon
  (COM), **Python 3.13 portátil (~21 MB)** que **no toca el Python ni el PATH del sistema**, el
  **motor .NET incluido (~82 MB)** que **no instala .NET aparte**, y el icono en CONTPAQi. Aclara
  que **fuera de `C:\BrosLMV` solo** registra el COM y copia el icono, y que el Desinstalador lo
  quita por completo. Nota servidor (provisión SQL) vs terminal y requisitos (admin + .NET FW 4.8).

### Nota de proceso
- **Regla nueva:** cada versión debe (1) reflejarse en `AssemblyVersion`, (2) sumar una entrada
  en este CHANGELOG, y (3) sumar una entrada en `src/assets/notas_version.html` (lo que ve el
  usuario en *Acerca de*). Toda instalación queda así identificada por su versión.

## [2.13.0] — 2026-06-27 — Consola modeless (minimizable, convive con Comercial)

> La consola de scripts ya **no es modal**: se puede **minimizar y seguir trabajando en
> Comercial** sin cerrarla, igual que las ventanas modeless de los botones (ver
> [`UI_VENTANAS.md`](UI_VENTANAS.md)). Antes (`ShowDialog`) bloqueaba Comercial y deshabilitaba
> cualquier otra ventana abierta desde un script.

### Cambiado
- **`ClsMain.ExecuteFunction` (caso `CONSOLA`)**: de `ShowDialog()` a `Show()` (modeless). Una
  sola instancia viva (`static BrosConsola _consola`): si ya está abierta, se restaura desde
  minimizado y se trae al frente en vez de abrir otra. Se auto-libera al cerrar (`FormClosed`
  limpia la referencia; modeless dispone la forma al cerrar, por eso ya no se usa `using`).
- **`BrosConsola`**: refresca el "contexto actual" en `Activated` (al volver a la ventana tras
  trabajar en Comercial pudo cambiar módulo/selección). Guardia `_ctxCargado` para no refrescar
  antes de la carga inicial de `Shown`.

### Agregado
- **Guardia de cambio de empresa** (consola modeless). Como la consola **captura el motor
  `XEngineLib` al abrirse**, si cambias de empresa en Comercial con la consola abierta, seguiría
  ejecutando contra la empresa **original**. Para evitar escribir en la BD equivocada:
  - Se guarda `_empresaInicial` al cargar el contexto la primera vez.
  - Al **reactivar** la ventana, si la empresa difiere → el indicador de contexto se pinta en
    **rojo** y se avisa **una vez** (se rearma si vuelves a la empresa original).
  - **Antes de ejecutar** cualquier script, si la empresa cambió → **pide confirmación**
    explícita (muestra "abierta en X / activa ahora Y"). Recomendación: cerrar y reabrir.

## [2.12.0] — 2026-06-27 — Python gana `ctx.erp` (relay al addon)

> Python ya puede usar **`ctx.erp.*`** — el mismo poder que C# (existencias, folios, precios,
> recalcular, crear/cancelar documentos, los ~562 miembros de XEngine). Como Python corre fuera
> de proceso, las llamadas se **relayan al `ErpContext` del addon** por el pipe, igual que el SQL
> (C6c). **Sin copiar código de terceros**: reusa nuestro propio `ErpContext`. La forma de la API
> (qué espera un usuario Python) salió de analizar herramientas similares del mercado como
> referencia conceptual, no de copiar su código.

### Agregado
- **`ctx.erp` en el SDK Python** (`workers/python/broslmv/ctx.py`): proxy dinámico; cada atributo
  es un método de XEngine con los **mismos nombres que C#** (`ctx.erp.GetProductStock(125, 0)`,
  `ctx.erp.GetSalePrice(1)`, `ctx.erp.GetTotalLetter(1234.5)`, ...).
- **Relay ERP en 3 capas:**
  - Proto: ya existía `ErpCall` (method + args) en `ContextCall`.
  - Host: `IPythonContextGateway.Erp` + `PipeRelayGateway.Erp` reenvían la llamada al addon;
    `PythonProcess` despacha el método `erp`.
  - Addon: `HostClient.IErpRunner` + `CtxErpRunner` resuelven el método por **reflexión** sobre
    `ErpContext` (usa los wrappers tipados con sus fixes, p. ej. el orden de `GetPriceWithTaxes`);
    si no hay wrapper, cae a `ctx.erp.Call(...)`. `EjecutarPython` recibe el `erpRunner` (botón y consola).

### Verificado en CONTPAQi (consola Python)
- `ctx.erp.UserId()`=1, `ComercialRFC()`=MULTI (propiedades), `GetProductStock`=960,
  `GetCostPrice`=1500, `GetSalePrice`=0 (sin precio capturado), **`GetPriceWithTaxes(100,1)`=116**
  (el fix de C# también aplica desde Python), `GetTotalLetter` OK. Tras verificar, se agregaron
  las entradas `ctx.erp.*` al panel de Referencias Python.
- **Ruteo de genéricos:** `ctx.erp.Get("PROP")` y `ctx.erp.Call("Metodo", ...)` ahora se enrutan
  a `ErpContext.Get/Call` (antes el fallback los trataba como miembros de XEngine y daban None).

## [2.11.3] — 2026-06-27 — Python: `user_id` real + verificación

> Verificación de las referencias Python en CONTPAQi real (consola, modo Python). Todo funciona
> (contexto, selección, `ctx.fila`, SQL con `@param`+dict, retorno por `result`). Dos ajustes.

### Corregido
- **`ctx.user_id` en Python** ahora trae el usuario **real** (antes 0). El COM pasa `UserID=0`;
  el verdadero está en `ctx.erp.UserId` (que Python no tiene). Nuevo helper
  `ScriptContext.UserIdReal()` (`src/Scripting.cs`) usado al armar el contexto que viaja al host,
  en los dos sitios: botón (`src/ClsMain.cs`) y consola (`src/Consola.cs`).

### Quitado
- **`ctx.execution_id` de las referencias Python** (`METODOS_PYTHON`): el método existe en el SDK
  pero el host **no lo incluye** en `context()` todavía (el GUID vive solo a nivel de sobre). Se
  reactivará cuando se cablee en proto + host + runner. Honestidad de las referencias.

### Verificado en CONTPAQi (consola Python)
- `ctx.module_id` (152), `ctx.empresa` (Coctel_de_Ideas), `ctx.app_key`, `ctx.fila` (dict
  completo del documento), `ctx.context()`, `ctx.get_selected_ids()` ([11555]),
  `ctx.scalar(...)` (956) y `ctx.query(sql, {"id": ...})` con `@param` → **OK**.

## [2.11.2] — 2026-06-27 — Fix `GetPriceWithTaxes` (orden de args) + verificación por lotes

> Verificación por lotes en CONTPAQi real (módulos de documentos con producto real). Casi
> todo `ctx.erp.*` quedó confirmado; los ceros previos eran **datos faltantes**, no bugs.

### Corregido
- **`ctx.erp.GetPriceWithTaxes`** (`src/Scripting.cs`): XEngine espera **`(taxTypeId, price)`**,
  no `(price, taxTypeId)`. Con `(100, 1)` devolvía `1.16` (tomaba `1` como precio); ahora se
  invierte el orden hacia COM y `GetPriceWithTaxes(100, 1)` → `116`. La firma pública sigue
  siendo `(price, taxTypeId)`. Verificado: `(1,100)=116`, `(1,200)=232`.

### Verificado en CONTPAQi (no eran bugs)
- `GetProductStock` (10 / 960), `GetCostPrice` (1500), `GetCurrencyRate`, `ProductIsKit`,
  `GetNextFolio`, `VerifyCreditLimit/Overdue`, `DLookup`/`DLookupStr`, `GetTotalLetter` y todo
  el contexto ERP: **OK**.
- `GetSalePrice`/`GetBuyPrice` = 0 → el producto de prueba **no tiene precio capturado**
  (`orgProductPriceList` sin filas); el 0 es correcto.
- `GetFolioPrefix` vacío → los documentos de prueba traen `DepotID = 0`; necesita un **almacén
  real** para responder. No es bug.

## [2.11.1] — 2026-06-27 — Referencias Python y SQL reales

> Continuación de 2.11.0: ahora las pestañas **Python** y **SQL** del panel de Referencias
> reflejan el **API real**, igual que ya se hizo con C#. Guía:
> [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md).

### Corregido
- **Referencias Python** (`METODOS_PYTHON` en `src/Consola.cs`) reescritas contra el SDK real
  `workers/python/broslmv/ctx.py`:
  - **Quitado `ctx.erp.UserId`**: en Python **no existe `ctx.erp`** (es exclusivo de C#); era
    una entrada inventada.
  - **Corregidos los ejemplos de `ctx.query/scalar/execute`**: los parámetros van como
    **dict con placeholders `@nombre`** (`ctx.query(sql, {"id": 5})`), no como kwargs
    (`id=...`) — que el host no soporta.
  - **Agregadas las propiedades de contexto reales**: `ctx.user_id`, `ctx.module_id`,
    `ctx.empresa`, `ctx.app_key`, `ctx.execution_id`, `ctx.fila`, `ctx.context()` y la
    variable de retorno `result`.
- **Referencias SQL** (`METODOS_SQL`) corregidas contra `ScriptContext.EjecutarSql` y los
  tokens de `ResolverTokensCore` (`src/Scripting.cs`):
  - **Agregados los tokens faltantes** `{pModulo}` y `{pEmpresa}` (los 6 reales quedan
    completos: `{pID}`, `{pIDs}`, `{pUserID}`, `{pModulo}`, `{pEmpresa}`, `{DATOS:Campo}`).
  - **Agregado `EXEC`** (llamada a procedimiento almacenado) e `INSERT`/`DELETE`; ejemplos
    con esquema real (`docDocument`, `DeletedOn IS NULL`) y nota del bloqueo en SOLO LECTURA.

## [2.11.0] — 2026-06-27 — Referencias C# reales + `ctx.erp` genérico + verificación por dump

> Las **referencias** de la consola (panel derecho, pestaña C#) se reescribieron para que
> reflejen el **API real** de `ctx.*` y `ctx.erp.*` (antes eran 12 entradas escritas a mano e
> incompletas). Se agregó **acceso genérico** a todo XEngine y se estableció un **método de
> verificación** eficiente (no una-por-una). Guía completa: [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md).

### Agregado
- **Referencias C# completas (~75)**, agrupadas por categoría (`ListView.Groups`): todo
  `ctx.*` (Selección/SQL/Tokens/Contexto/Interacción) y todo `ctx.erp.*` (Documento, UI,
  Folio, Precios, Crédito, Parámetros, Utilidades, DLookup, Bitácora, Impresión, Correo,
  Web, CFDI, Avanzado). Cada una con firma exacta, descripción y ejemplo insertable.
- **`ctx.erp.Call(metodo, args...)` y `ctx.erp.Get(propiedad)`** en `ErpContext`
  (`src/Scripting.cs`): acceso genérico late-bound a **cualquiera de los ~562 miembros de
  XEngine** sin tener que envolverlos uno por uno. `Call` para métodos, `Get` para propiedades.
- Wrappers tipados extra: `GetBusinessEntitySalePrice`, `GetTotalLetterEN`, `GetHTMLFromURL`.

### Corregido
- **`GetTotalLetter` / `GetTotalLetterEN`**: el 2º parámetro es **`currencyId` (int)**, no un
  código string (con `"MXN"` devolvía vacío). Ahora usa int y, si se omite, la **moneda activa**.
  Verificado en CONTPAQi real.
- **`GotoModuleID`**: es una **propiedad-put**, no un método. Ahora usa `Com.SetProp`
  (detectado por auditoría contra el dump de tipos COM).
- **Quitados** `NumDecimalesMoneda/PrecioUnit/Conceptos`: devolvían NULL en CONTPAQi real
  (siguen accesibles por `ctx.erp.Call(...)`).

### Método de verificación (establecido en esta versión)
- **Dump de tipos COM** de `XengineLib.clsMain` (método vs propiedad + nº de params por
  miembro) → **auditoría estática** que cruza cada `Com.Call/GetProp` de `ErpContext` contra
  el dump (caza método-vs-propiedad y args de más, sin abrir CONTPAQi).
- **Verificador por lotes** (script C# de solo lectura): descubre IDs reales y prueba ~30
  getters de un jalón. Las operaciones de **escritura** se verifican individualmente solo
  cuando se usan, sobre un documento desechable.

### Notas
- `AssemblyVersion` → **2.11.0**. Sin cambios en la lógica de ejecución ni en el protocolo.
- **Pendiente:** mismo trabajo para las referencias de **Python** (`METODOS_PYTHON`) y **SQL**
  (`METODOS_SQL`) en `src/Consola.cs`. Ver [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md) → "Cómo continuar".

---

## [2.10.0] — 2026-06-27 — Rediseño visual de la Consola (modo claro profesional)

> La Consola de scripts se rediseñó por completo a un **modo claro, moderno y profesional**
> (estilo Visual Studio / Azure Data Studio / JetBrains Rider en tema claro), conservando
> **toda** la funcionalidad, los nombres de controles y los eventos existentes. El cambio es
> visual, de distribución y de experiencia de usuario; no toca la lógica de ejecución, el
> contexto ni la integración con Comercial PRO.

### Agregado
- **Sistema de tema central** (`AppTheme`): paleta clara (fondo `#F7F9FC`, superficies blancas,
  azul `#2563EB`, verde `#16A34A`, etc.), tipografías con respaldo (Inter→Segoe UI, Cascadia→Consolas)
  e iconografía vectorial **Segoe Fluent / MDL2** sin archivos extra.
- **Componentes reutilizables**: `IconButton` (icono + texto con estados hover/pressed/disabled y
  estilos Ghost/Primary/Outline/Toolbar), `ModernUI` (esquinas suaves, glifo→bitmap), `Glyph`,
  `BordeSuperiorRenderer`.
- **Logo**: se embebe `assets/logo_app.png` y la cabecera muestra el isotipo + el wordmark **BrosLMV**
  con subtítulo "Consola de scripts" e indicador de estado del documento (Sin guardar / Guardado).
- **Editor**: pestaña con nombre del script, columna de números de línea, línea activa resaltada,
  selección azul, y controles discretos (tamaño de fuente, ajuste de línea).
- **Buscar en el editor** (`Ctrl+F` / `Ctrl+B`): barra incremental con **siguiente/anterior**
  (`Enter` / `Shift+Enter` / `F3`), contador "N de M" y **resaltado de todas las coincidencias**.
- **Pantalla completa del editor** (`F11` o botón): colapsa biblioteca, contexto y consola.
- **Contexto actual en forma de lista** (Campo / Valor): la **Vista** muestra solo el nombre de la
  vista consultada (no el `SELECT` completo); el **Usuario** toma el id/nombre real de XEngine;
  doble clic copia el valor.
- **Plantillas/ejemplos**: un ejemplo bien hecho por lenguaje (**C#**, **Python**, **SQL**) con el
  esquema real (`docDocument`) y tokens.
- **Consola de salida**: botones para limpiar/copiar, contador de errores y colores por estado.
- **Barra de estado**: estado con color (OK verde / error rojo), script, lenguaje, posición del cursor
  y tiempo de ejecución.
- **Biblioteca de scripts**: búsqueda con placeholder en capa, iconos por tipo (carpeta/script/plantilla)
  y botón para **expandir/contraer todo** (además de doble clic en una carpeta).

### Cambiado
- Cabecera, barra de acciones y barra de estado con un **tinte suave** (ya no blanco puro); los botones
  de la barra superior se muestran como botones (estilo `Toolbar`) y **Ejecutar** se destaca en verde.

### Corregido
- **Robustez a DPI** (125–150 %): cabecera con `TableLayoutPanel`, botones con `GetPreferredSize`
  (ya no se enciman ni se recortan) y anchos de los paneles fijos calculados con `LogicalToDeviceUnits`.
- **Fantasmeo de texto** en botones personalizados: ahora limpian su fondo en cada repintado.
- **Árbol de scripts**: el placeholder de búsqueda ya no se guardaba como texto (filtraba todo y ocultaba
  scripts/plantillas); ahora es una capa que no reconstruye el árbol al seleccionar un nodo.
- Pestañas de referencias en `TableLayoutPanel` de columnas iguales (no se recortan a alto DPI).

### Notas
- `AssemblyVersion` → **2.10.0**. Sin cambios en la lógica de ejecución, el protocolo ni el host.

---

## [2.9.0] — 2026-06-27 — Mejoras en UX de la Consola (Bloque E)

> La consola ahora cuenta con pestañas por lenguaje para referencias rápidas, muestra información detallada sobre el contexto (Owner activo, nombre de vista, PK) y permite insertar dinámicamente datos de la fila seleccionada (`ctx.fila["x"]`, `{DATOS:x}`).

### Agregado
- **Pestañas de Referencia**: Se reemplazó la lista de métodos general con un `TabControl` que contiene pestañas para referencias rápidas de C#, Python y SQL. Al hacer doble clic se inserta un snippet válido para el lenguaje.
- **Pestaña de Selección**: Muestra todos los campos y valores de la fila actual del grid en un `ListView`. El doble clic inserta dinámicamente el valor/token correcto (`ctx.fila`, `fila`, `{DATOS:}`).
- **Contexto Enriquecido**: Ahora el panel de contexto actual extrae y muestra el `Owner` seleccionado resolviendo su nombre desde `orgOrganizacion`, muestra el nombre de la vista (`ADORecordset.Source`), y la llave primaria (PK) del módulo.
- **Soporte en Python SDK**: Expuesto `ctx.fila` como diccionario inmutable con los datos del documento/fila activa.
- **Protocolo Actualizado**: `broslmv.proto` (`ExecutionContext`) y `HostClient` incluyen y serializan los datos de la fila activa a través del `NamedPipe` hacia el worker de Python.

---

## [2.8.0] — 2026-06-27 — Tipo de script `sql` directo (B3)

> Un botón o la consola pueden ser **T-SQL crudo**, sin Python ni C#. Un
> `SELECT * FROM docDocument`, un `EXEC sp_...` o cualquier T-SQL corre **tal cual**.

### Agregado
- **`ScriptContext.EjecutarSql(sqlCrudo)`** — resuelve tokens (`{pID}`, `{DATOS:..}`, …),
  ejecuta por la **conexión viva** y devuelve texto: las filas si es SELECT/SP, o un OK si
  es DML. Respeta el modo solo-lectura (bloquea escrituras). Tope de 500 filas mostradas.
- **Dispatch del tipo `sql`** en `ClsMain.EjecutarScript` y `Consola.Ejecutar`: se reconoce
  por extensión **`.sql`** o marcador en la 1ª línea (`-- lang: sql`, `--sql`, `# lang: sql`,
  `broslmv:sql`). Auditoría `boton-sql` / `consola-sql`.
- `HostClient.EsSql(codigo)` (detección compartida). Ejemplo `instalador/scripts/EJEMPLO_sql.sql`.

### Notas
- Es **en proceso** (conexión viva del addon), no usa el host de Python. Tres lenguajes
  ya conviven en el mismo botón/consola: **C#** (.ctx/.csx), **Python** (.py), **SQL** (.sql).
- `AssemblyVersion` → **2.8.0**.

---

## [2.7.0] — 2026-06-27 — SQL desde Python por la conexión viva (C6c relay)

> Un `SELECT * FROM docDocumento`, un `EXEC sp_...` o cualquier T-SQL desde Python
> **funciona tal cual**, contra la empresa activa y **sin credenciales en el host**.

### Agregado
- **Relay SQL host→addon (C6c).** El host ya **no** conecta a SQL por su cuenta: reenvía
  cada `ctx.query/scalar/execute` al addon por el MISMO pipe (`IAddonChannel`), y el addon
  lo corre en la **conexión viva de CONTPAQi** (igual que el C# de la consola). Sin
  contraseñas guardadas; siempre la empresa activa.
  - Host: `Workers/IAddonChannel.cs` (+`PipeAddonChannel`), `Workers/PipeRelayGateway.cs`,
    `Protocol/ValueCodec.cs`. El gateway por defecto del handler pasa a ser el relay
    (el `SqlServerPythonContextGateway` directo queda como alternativa opcional).
  - Addon: `HostClient` atiende los `ContextCall` de SQL y los ejecuta vía
    `CtxSqlRunner` (sobre `ScriptContext.Query/Scalar/NonQuery`). Parámetros `@x` se
    sustituyen como literales SQL seguros (string escapado, decimal/num sin comillas).
- `ctx.query/scalar/execute` desde Python ahora devuelven datos reales del ERP.

### Cambiado
- `IExecutionHandler.ExecuteAsync` y `MessageRouter.RouteAsync` reciben un `IAddonChannel`
  para los callbacks durante la ejecución.

### Validación
- `/.temp_tests/host_c6c`: **4/4** (query+scalar+execute por el relay + parámetro `@id`).
  **Regresión completa: 61 tests verdes** (c3c…c6c). `AssemblyVersion` → **2.7.0**.

### Verificado en CONTPAQi (2026-06-27)
- Probado por el usuario en Comercial PRO real: `SELECT * FROM docDocument` desde Python
  en la consola devolvió **5 facturas reales en 335 ms**. Stack v3.0 completo funcionando.
- Guía de uso de Python: [`PYTHON.md`](PYTHON.md). Esquema real de la BD `ComercialSP`:
  tablas `doc*`/`org*`/`eng*` (p. ej. `docDocument`), soft delete `DeletedOn IS NULL`.
```python
# lang: python
from broslmv import ctx
filas = ctx.query("SELECT TOP 5 * FROM docDocument WHERE DeletedOn IS NULL")
result = f"{len(filas)} documentos"
```

---

## [2.6.2] — 2026-06-27 — Contexto vivo completo para Python (servidor + empresa)

> Corrección encontrada en la primera prueba real en CONTPAQi: el `execution_context`
> llegaba con `Empresa=""`. Python ya corría OK en botón y consola; faltaba el contexto.

### Corregido
- **`ScriptContext.Empresa()`** ahora cae al `Initial Catalog`/`Database` del DataLayer de
  CONTPAQi si `SELECT DB_NAME()` por la conexión viva no responde (antes devolvía "").
- **`ScriptContext.ServidorActivo()`** (nuevo): saca la instancia SQL (`Data Source`) del
  DataLayer. El **host es un proceso aparte y NO puede reusar la conexión viva** de
  CONTPAQi, así que el addon ahora le pasa **servidor + base** en el contexto.
- `ClsMain.EjecutarPython` y `Consola.Ejecutar` rellenan `Contexto.Servidor`.

### Validación
- `AssemblyVersion` → **2.6.2**. Requiere cerrar CONTPAQi para actualizar el DLL en
  `C:\BrosLMV\bin` (queda bloqueado mientras Comercial está abierto).

### Nota sobre SQL desde Python
- El host necesita **su propia credencial SQL** (no ve la conexión viva). La migración
  DPAPI (B2) dejó `broslmv_conn.txt` como aviso y sin `broslmv_cred.dat`, así que el host
  no tiene cadena. Se resuelve con el **relay SQL vía la conexión viva (C6c)** o
  configurando `BROSLMV_SQL_CONN` / regenerando la credencial. Ver C6c en el plan.

---

## [2.6.1] — 2026-06-26 — Python también en la consola (C6b)

> Mismo motor, mismo instalador: Python **no es un botón aparte**, es otro lenguaje para
> los **mismos botones y la misma consola**.

### Agregado
- **La consola ejecuta Python.** `Consola.Ejecutar` detecta Python (`HostClient.EsPython`)
  y lo corre por el host con el contexto vivo de la consola (empresa, usuario, módulo,
  selección); muestra el `result` en la salida. C# (Roslyn) sigue igual para lo demás.
- `Verificar` (F5 de verificación) avisa que en Python la validación es al ejecutar
  (no aplica el compilador de Roslyn).
- Auditoría de consola Python como tipo `consola-python`.

### Cambiado
- La detección de lenguaje se unificó en **`HostClient.EsPython`** (la usan el dispatch de
  botón y la consola; antes estaba duplicada en `ClsMain`).

### Aclaración de arquitectura (a raíz de una duda)
- **Un solo instalador:** `BrosLMV-Instalador.exe` ya empaca y despliega host + runtime
  (CPython) + workers a `C:\BrosLMV` (`generar_exes.ps1` → `payload.zip`;
  `RuntimeInstaller` los copia). No hay instalador aparte para Python.
- **No hay botón ni consola "de Python" separados:** el mismo botón / la misma consola
  corren C# o Python según el script.

### Validación
- `AssemblyVersion` → **2.6.1**. `/.temp_tests/host_c6a` 5/5 y `host_c6b` 9/9 verdes.

---

## [2.6.0] — 2026-06-26 — Botones en Python (C6b)

> Sub-punto **C6b** del plan de trabajo. Un botón de CONTPAQi
> ya puede ejecutar **Python** (fuera de proceso) con el contexto vivo.

### Agregado
- **Dispatch de botón Python** en `ClsMain.EjecutarScript`: detecta scripts Python por
  **extensión `.py`** (archivo) o por **marcador** en la 1ª línea de un script en SQL
  (`#py`, `# lang: python`, `#lang:python`, o `broslmv:python`). Si es Python, llama a
  `HostClient.EjecutarPython` con el contexto vivo (`UserID`, `ModuloActivo`, `Empresa`/
  BD activa, `GetSelectedIds`) y muestra el resultado o el error.
- Auditoría local del botón Python (`Datos.RegistrarEjecucion`, tipo `boton-python`).

### Cómo se usa
- **Archivo:** `C:\BrosLMV\scripts\<EMPRESA>\<AppKey>.py` (o en la raíz `scripts\`).
- **En SQL** (`zzBrosScript`): primera línea con un marcador, p. ej. `# lang: python`.
- El script usa `from broslmv import ctx` (`ctx.query/scalar/execute`, `ctx.msg`,
  `ctx.get_selected_ids`, ...). Devuelve un valor asignando la variable global `result`.

### Despliegue
- El addon ahora depende de **`Google.Protobuf.dll`**, que debe estar en `C:\BrosLMV\bin`
  junto a `BrosLMVClsMain.dll` (lo copia `generar_instalador.ps1`; añadido a `instalador/bin`).
- Requiere el **host** provisionado en `C:\BrosLMV\host\BrosLMV.Host.exe` (instalador).

### Validación
- `/.temp_tests/host_c6b`: **9/9** (detección Python vs C#/SQL). El round-trip real de
  ejecución está cubierto por C6a (5/5). **Falta probar un botón `.py` dentro de CONTPAQi.**
- `AssemblyVersion` → **2.6.0**.

### Pendiente (C6c)
- Relay de `ctx.msg/form/show_html` del host al addon para UI visible en Comercial (hoy el
  resultado se muestra al final; los callbacks intermedios van al log del host).

---

## [Sin versión] — 2026-06-26 — Cliente del pipe en el addon (C6a)

> Sub-punto **C6a** del plan de trabajo. El addon se vuelve
> cliente del canal v3.0. Sin cambio de comportamiento del addon en producción todavía
> (el dispatch de botón Python llega en C6b), así que no sube `AssemblyVersion`.

### Agregado
- **`src/HostClient.cs`** — lado cliente del canal dentro del addon (net48): lanza
  `BrosLMV.Host.exe` (`--serve --pipe --token`), abre el Named Pipe, hace el handshake
  con token, manda `ExecuteScript` con el contexto vivo y devuelve el resultado
  (Completed/Failed). Framing `[4B len][protobuf]` propio, igual que el host.
- **`src/BrosLMV.csproj`** — el addon ahora genera el C# de `protocol/broslmv.proto`
  (`Google.Protobuf` + `Grpc.Tools`, `GrpcServices=None`); habla el mismo contrato que el host.

### Validación
- Test local (`/.temp_tests/host_c6a`, net48 como el addon): **5/5 OK** — lanza el host
  real y corre Python de punta a punta: valor de retorno, `ctx.get_selected_ids()` por el
  pipe usando el contexto vivo, y propagación de error. El addon de producción sigue
  compilando 0/0.

### Siguiente
- C6b: detectar y despachar botones Python en `ClsMain` con el contexto vivo (prueba en CONTPAQi).

---

## [Sin versión] — 2026-06-26 — Auditoría + callbacks UI/log/progress (C5d)

> Sub-punto **C5d** del plan de trabajo. Cierra el Bloque C
> (host Python). Sin código de producto del addon.

### Agregado
- **Auditoría de ejecuciones** — `Audit/IExecutionAuditSink` + `FileExecutionAuditSink`
  (una línea JSON por ejecución en `C:\BrosLMV\logs\host-audit.jsonl`): execution_id,
  app_key, empresa, usuario, módulo, status OK/ERROR, elapsed, filas, error. Lo registra
  `PythonExecutionHandler` al terminar cada script.
- **Callbacks host→addon** — `Callbacks/IHostCallbackSink` + `LoggingHostCallbackSink`.
  `ctx.msg` / `ctx.log` / `ctx.progress` dejan de ser no-op: el host los entrega al sink
  con el `execution_id` (seam hacia el addon; hoy a `host-callbacks.log`, mañana como
  `UiRequest`/`LogEvent`/`ProgressEvent` por el pipe).
- **`Logging/LogPaths`** — carpeta de logs (`C:\BrosLMV\logs`, redirigible con
  `BROSLMV_LOG_DIR`).
- **SDK Python:** `ctx.progress(text, percent)` y `ctx.execution_id`; el `execution_id`
  viaja en el contexto que recibe el script.

### Cambiado
- `Msg`/`Log` salieron de `IPythonContextGateway` (su lugar es el callback sink; el gateway
  queda enfocado en contexto + SQL).
- `PythonProcess.ExecuteAsync` acepta `executionId` y enruta `msg`/`log`/`progress` al sink.

### Validación
- Test local (`/.temp_tests/host_c5d`): **10/10 OK** — callbacks con execution_id +
  auditoría OK y ERROR. Regresión C3d/C4/C5/C5b/C5c sigue verde.

---

## [Sin versión] — 2026-06-26 — Gateway SQL Server real para Python (C5c)

> Sub-punto **C5c** del plan de trabajo. `ctx.query`,
> `ctx.scalar` y `ctx.execute` ya tienen backend SQL Server real cuando hay credencial.

### Agregado
- **`SqlConnectionResolver`** — lee `BROSLMV_SQL_CONN`, `broslmv_cred.dat` DPAPI
  compatible con `Rutas` o `broslmv_conn.txt` heredado; el `ExecutionContext` puede
  sobreescribir servidor/base activa.
- **`SqlServerPythonContextGateway`** — ejecuta `Query`, `Scalar` y `Execute` con
  `Microsoft.Data.SqlClient`, parametros nombrados, timeout controlado y normalizacion
  JSON para Python.

### Cambiado
- **`PythonExecutionHandler`** usa el gateway SQL Server por defecto.
- **`IPythonContextGateway`** recibe el `ExecutionContext` en llamadas SQL para aplicar
  servidor/base de la empresa activa.

### Validacion
- Test local (`/.temp_tests/host_c5c`): **7/7 OK** — resolucion por entorno/archivo,
  override de contexto y error controlado sin credencial.
- C5 y C5b siguen pasando: **4/4 OK** y **5/5 OK**.

### Siguiente
- C5d: auditoria de SQL/filas afectadas/errores y callbacks UI/log al addon.

## [Sin versión] — 2026-06-26 — SQL remoto Python contra gateway (C5b)

> Sub-punto **C5b** del plan de trabajo. Transporte SQL Python
> cerrado con gateway inyectable; aun sin gateway SQL Server real.

### Agregado
- **`IPythonContextGateway`** — contrato del host para contexto, seleccion, UI minima y
  SQL (`Query`, `Scalar`, `Execute`) consumido por `PythonProcess`.
- **`DefaultPythonContextGateway`** — implementacion offline por defecto: contexto,
  seleccion, `msg/log` no-op; SQL falla con error claro si no se inyecta gateway.
- **`PythonProcess`** enruta `ctx.query/scalar/execute` al gateway y convierte argumentos
  JSON de Python a tipos .NET (`Dictionary<string, object?>`, listas, numeros, strings).

### Validación
- Test local (`/.temp_tests/host_c5b`): **5/5 OK** — script Python llama
  `ctx.query`, `ctx.scalar`, `ctx.execute`; gateway fake recibe SQL/parametros y devuelve
  filas/escalar/filas afectadas.
- C5 sigue pasando: sin gateway SQL, el error es controlado y explicito.

### Siguiente
- C5c: gateway SQL Server real con credenciales DPAPI, timeouts y auditoria.

## [Sin versión] — 2026-06-26 — SDK Python `broslmv` minimo (C5a)

> Sub-punto **C5a** del plan de trabajo. Primer paquete Python
> usable desde scripts; sin cambios en el addon productivo.

### Agregado
- **`workers/python/broslmv/`** — paquete Python minimo. Los scripts ya pueden usar
  `from broslmv import ctx`.
- **`ctx` Python minimo** — `context()`, `app_key`, `user_id`, `module_id`, `empresa`,
  `get_selected_ids()`, `msg()` y `log()` hacen round-trip real al host.
- **Puente runner-host** — `runner.py` emite `context_call` por stdout real (`sys.__stdout__`)
  y espera respuesta por stdin; el stdout normal del script se sigue capturando.
- **`PythonProcess`** responde llamadas de contexto desde el `ExecutionContext` congelado.
  `ctx.query/scalar/execute` existen en el SDK; C5b los conecta al gateway del host.

### Validación
- Test local (`/.temp_tests/host_c5`): **4/4 OK** — importa `broslmv`, lee contexto,
  obtiene selección, ejecuta `msg/log`, y SQL falla con mensaje controlado si no hay
  gateway configurado.

### Siguiente
- C5b/C5c completados; siguiente C5d: auditoria y callbacks UI/log al addon.

## [Sin versión] — 2026-06-26 — CPython embeddable empacado (C4)

> Sub-punto **C4** del plan de trabajo. Sin cambios en el
> addon productivo; no cambia `AssemblyVersion`.

### Agregado
- **`build/descargar_python.ps1`** — descarga el ZIP oficial de CPython embeddable x64,
  valida SHA256 si se proporciona, extrae a `instalador\runtimes\python`, habilita
  `import site` y ejecuta prueba de humo.
- **Runtime empacado:** CPython **3.13.14** x64 en `instalador/runtimes/python`.
  SHA256 del ZIP oficial descargado:
  `90b4e5b9898b72d744650524bff92377c367f44bd5fbd09e3148656c080ad907`.
- **`PythonProcess`** ahora resuelve Python en orden: `BROSLMV_PYTHON_EXE`, runtime junto
  al host, `C:\BrosLMV\runtimes\python\python.exe`, y fallback `python` solo desarrollo.

### Cambiado
- **`build/generar_instalador.ps1`** ahora compila el host v3.0 y copia `workers/` al
  staging del instalador. El host se publica **self-contained win-x64**, por lo que el
  cliente no necesita tener .NET 8 instalado.
- **`build/generar_exes.ps1`** incluye `host/`, `workers/` y `runtimes/` en `payload.zip`.
- **`RuntimeInstaller`** despliega `host`, `workers` y `runtimes` bajo `C:\BrosLMV`.

### Validación
- `build\descargar_python.ps1`: descarga y humo OK con `python.exe` embeddable.
- `build\generar_instalador.ps1`: addon + host self-contained + workers OK.
- Test local (`/.temp_tests/host_c4`): **3/3 OK** — `PythonProcess` ejecuta con el
  runtime embeddable cuando está disponible.

### Siguiente
- C5: paquete Python `broslmv` y `ctx` remoto real sobre el contrato del canal.

## [Sin versión] — 2026-06-26 — Primer round-trip Python del host (C3d)

> Sub-punto **C3d** del plan de trabajo. Sin cambios en el
> addon productivo; no cambia `AssemblyVersion`.

### Agregado
- **`host/BrosLMV.Host/Workers/PythonProcess.cs`** — lanza un proceso Python, envia una
  peticion JSON por stdin al runner y convierte la respuesta a `ExecutionCompleted` o
  `ExecutionFailed`. Respeta `ExecuteScript.timeout_ms`, mata el arbol de proceso en
  timeout y permite configurar `BROSLMV_PYTHON_EXE` para desarrollo local.
- **`host/BrosLMV.Host/Handlers/PythonExecutionHandler.cs`** — implementacion real de
  `IExecutionHandler` para `ExecuteScript`.
- **`workers/python/runner.py`** — runner minimo de C3d: ejecuta codigo Python con `exec`,
  captura stdout/stderr, expone un `ctx` provisional como diccionario y devuelve JSON.
- **`Program --serve`** ahora usa `PythonExecutionHandler`; `EchoExecutionHandler` queda
  como stub inyectable para pruebas.

### Validación
- Test local (`/.temp_tests/host_c3d`): **7/7 OK** — handshake, ejecucion Python real,
  `ExecutionCompleted` con `result`, excepcion Python mapeada a `ExecutionFailed`, cierre
  de sesion por `CancelExecution`.
- Build del host: **0 warnings / 0 errores**.

### Siguiente
- C4: empacar CPython embeddable x64 y dejar de depender del Python instalado en el PATH.

## [Sin versión] — 2026-06-26 — Router de sesión del host (C3c)

> Sub-punto **C3c** del plan de trabajo. Enrutado de mensajes
> del canal. Sin código de producto del addon.

### Agregado
- **`host/BrosLMV.Host/MessageRouter.cs`** — despacha el `Envelope` por `oneof`:
  `ExecuteScript`→`IExecutionHandler`, `Heartbeat`→eco, `CancelExecution`→fin de sesión,
  resto→`ExecutionFailed { UNSUPPORTED }`. Tipo `RouteResult` (respuesta + fin de sesión).
- **`host/BrosLMV.Host/Handlers/IExecutionHandler.cs`** — *seam* de ejecución, con stub
  `EchoExecutionHandler` (responde sin lanzar Python; el real llega en C3d).
- **`PipeServer.ServeSessionAsync`** — handshake + **bucle de sesión** (lee→enruta→responde)
  hasta cierre o `Cancel`. El handshake ahora devuelve `bool accepted`.
- **`host/README.md`** — build/run, arquitectura del host, seguridad y trampas conocidas
  (p. ej. `ExecutionContext` ambiguo → usar `BrosLMV.Protocol.ExecutionContext`).

### Validación
- Test local (`/.temp_tests/host_c3c`): **7/7 OK** — handshake, `ExecuteScript`→
  `ExecutionCompleted` (stub), `Heartbeat`→`Heartbeat`, no soportado→`UNSUPPORTED`,
  `Cancel`→fin de sesión (EOF). Sesión completa sobre una sola conexión.

### Siguiente
- C3d: `IExecutionHandler` real que lanza el proceso Python (`workers/python/runner.py`).

---

## [Sin versión] — 2026-06-26 — Pipe seguro + framing del host (C3b)

> Sub-punto **C3b** del plan de trabajo. Primera capa de
> seguridad del canal. Sin código de producto del addon.

### Agregado
- **`host/BrosLMV.Host/Security/PipeAcl.cs`** — ACL que restringe el Named Pipe al
  **usuario de Windows actual** (decisión D6, capa 1).
- **`host/BrosLMV.Host/PipeServer.cs`** — servidor del pipe. Valida el **token UUID**
  por arranque (capa 2) en **comparación de tiempo constante**; handshake
  `Hello`→`HelloResponse` (rechaza con `AUTH_DENIED` token inválido y `BAD_HANDSHAKE`
  si el primer mensaje no es `Hello`). `ServeOneAsync`/`ServeLoopAsync`.
- **`host/BrosLMV.Host/Protocol/FrameCodec.cs`** — framing `[4B longitud BE][Protobuf]`
  con lectura exacta y tope de 64 MB por frame.
- **`Program.cs`** — modo `--serve [--pipe N] [--token T]`: abre el pipe e imprime
  `PIPE=`/`TOKEN=` (en producción el addon los genera y pasa por args).
- **`csproj`** → `net8.0-windows` (APIs Windows sin warnings) + paquete
  `System.IO.Pipes.AccessControl`.

### Validación
- Test local (`/.temp_tests/host_c3b`, gitignored): **6/6 OK** — token correcto aceptado,
  token inválido `AUTH_DENIED`, primer mensaje no-`Hello` `BAD_HANDSHAKE`. Ejercita el
  framing end-to-end entre un cliente y el servidor.

### Siguiente
- C3c: `MessageRouter` que despacha todos los tipos del `Envelope` (`ContextCall`,
  `UiRequest`, ...).

---

## [Sin versión] — 2026-06-26 — Esqueleto del host v3.0 (C3a)

> Sub-punto **C3a** del plan de trabajo. Proyecto del host
> Python x64. Sin código de producto (no cambia `AssemblyVersion` del addon).

### Agregado
- **`host/BrosLMV.Host/`** — proyecto **.NET 8, x64** del proceso supervisor del canal.
  Usa `Google.Protobuf` + `Grpc.Tools` (`GrpcServices=None`) para **generar el C# de
  `protocol/broslmv.proto` en cada build** (el código generado no se versiona).
  - `BrosLMV.Host.csproj`, `Program.cs` (prueba de humo del contrato).
- **`.gitignore`** ignora `host/*/bin` y `host/*/obj` (incluye el código generado).

### Validación
- Compila **0 warnings / 0 errores**. Ejecuta como **proceso x64** y hace round-trip de
  un `Envelope` (serializa + parsea), confirmando que el codegen quedó bien enlazado.

### Siguiente
- C3b: servidor de Named Pipe (ACL + token UUID) con eco `Hello`→`HelloResponse`.

---

## [Sin versión] — 2026-06-26 — Contrato del canal v3.0 (C2)

> Punto **C2** del plan de trabajo. Diseño del contrato
> Protobuf C# ↔ Python. Sin código de producto (no cambia `AssemblyVersion`).

### Agregado
- **`protocol/broslmv.proto`** — contrato completo del canal Named Pipes:
  `Envelope` + framing `[4B len][msg]`; sistema de tipos `Value`/`Table` con **Decimal
  exacto (string)** para importes; ciclo de vida (`Hello`, `ExecuteScript` +
  `ExecutionContext` congelado, `ExecutionCompleted/Failed`, `CancelExecution`);
  `ContextCall` con SQL **solo-proxy** (`SqlRequest`, `BulkInsert`, `QueryBatches`,
  transacciones), `ErpCall` (espejo de `ErpContext`) y `LiveSql`; UI **Opción A (D9)**
  (`UiForm` declarativo con grid editable + `UiShowHtml` WebView2, `FormResult`);
  eventos (`ProgressEvent`/`LogEvent`/`ArtifactEvent`); salud (`Heartbeat`/`WorkerStatus`).
- **`protocol/README.md`** — explica el contrato, el framing y cómo regenerar el código.

### Validación
- Compilado con **`protoc` 28.3**: descriptor sin errores **y** generación de C#
  correcta (543 KB). El contrato es válido de punta a punta.

### Pendiente
- La generación de código (C#/Python) se cableará al crear el host (punto C3).

---

## [Sin versión] — 2026-06-26 — Apertura como código libre (GPL-3.0)

> Preparación para el lanzamiento open-source. Solo gobernanza del proyecto, sin cambio
> de código ni de `AssemblyVersion`.

### Agregado
- **`LICENSE`** — GNU GPL-3.0 (texto canónico). El proyecto pasa a ser **software libre
  con copyleft**: las versiones modificadas que se distribuyan deben seguir siendo libres.
- **`CONTRIBUTING.md`** — flujo de trabajo, reglas del repo (doc total, versionado
  atómico, tests en `/.temp_tests`), cómo aportar una receta no-code.
- **`SECURITY.md`** — reporte privado de vulnerabilidades + áreas sensibles (DPAPI, SQL,
  host Python v3.0).
- **`CODE_OF_CONDUCT.md`** — Contributor Covenant 2.1 (adaptado al español).
- **`.github/`** — plantillas de issue (bug / feature) y de pull request (con checklist
  de las reglas del repo).
- **README** — badges de licencia/estado, secciones *Contribuir* y *Licencia*.
- **Encabezado GPL en los 18 archivos fuente** (`src/*.cs`, `instaladores/**/*.cs`,
  `build/*.ps1`, `instalador/*.ps1`) — bloque estándar FSF con copyright + aviso de
  garantía. Solo comentarios: compila sin cambios (0 errores).

### Limpieza del repositorio
- **`.gitignore` excluye material de referencia local** que no debe entrar al repo.

---

## [2.5.1] — 2026-06-26 — Tokens probables y SQL-safe (D1)

> Punto **D1** del plan de trabajo. Refactor del motor de
> tokens para poder probarlo **offline** (sin CONTPAQi) + corrección de un bug latente.

### Cambiado
- **`ResolverTokens()` refactorizado** en `Scripting.cs`: la sustitución pura se extrae
  a `public static ResolverTokensCore(template, ids, userId, modulo, empresa, fila)`,
  sin dependencias COM. `ResolverTokens()` solo recolecta el contexto vivo y delega.
  Permite tests unitarios offline.

### Corregido
- **Decimales en SQL (locale):** `{DATOS:Campo}` ahora convierte valores con
  `CultureInfo.InvariantCulture`. Antes, en un Windows en español, un total `1234.5`
  se volvía `"1234,5"` y rompía el SQL generado. Ahora siempre usa punto decimal.

### Pruebas
- Nuevo `/.temp_tests/test_tokens.ps1` (local, no versionado): 17 casos que cargan la
  DLL y ejercitan `ResolverTokensCore` por llamada directa — incluye forzar cultura
  `es-ES` para verificar el decimal invariante. **17/17 OK.**

### Compatibilidad
- COM sin cambios. `AssemblyVersion` → **2.5.1**. Solo reemplazar la DLL.

---

## [2.5.0] — 2026-06-26 — Credenciales cifradas (DPAPI)

> Punto **B2** del plan de trabajo. La contraseña de SQL
> deja de estar en texto plano.

### Agregado
- **Capa DPAPI en `Rutas.cs`** — la cadena de conexión (con la contraseña) se cifra
  con la API de protección de datos de Windows, ámbito **`LocalMachine`** + una
  **entropía** propia de BrosLMV. El secreto queda atado a la máquina (no se puede
  copiar `broslmv_cred.dat` a otra PC y leerlo) y lo comparten todos los usuarios de
  Windows de esa terminal CONTPAQi.
  - `Rutas.GuardarCredencial(connString)` — cifra y guarda en `bin\broslmv_cred.dat`.
  - `Rutas.LeerCredencial()` — descifra; `""` si no existe o no se puede descifrar.
  - `Rutas.BorrarCredencial()` — para desinstalar/reconfigurar.
- **Nueva constante** `Rutas.CredFile` = `C:\BrosLMV\bin\broslmv_cred.dat`.

### Cambiado
- **`Rutas.ConnStr()`** ahora resuelve en orden: (1) `broslmv_cred.dat` cifrado
  (preferente); (2) `broslmv_conn.txt` en texto plano (compat heredada).
- **Migración automática:** si existe un `broslmv_conn.txt` con una cadena **real**
  (no la plantilla), en el primer uso se **cifra a `.dat`** y se **limpia el `.txt`**
  (se reemplaza por un aviso, sin contraseña). Transparente: instalaciones existentes
  siguen funcionando y se aseguran solas.
- `ctx.DiagConexion()` ahora reporta el estado de la credencial cifrada DPAPI.

### Seguridad
- Validado round-trip DPAPI `LocalMachine` + entropía en esta plataforma: el cifrado
  no contiene la contraseña en claro y no descifra sin el salt correcto.
- **Pendiente (no en esta versión):** login dedicado `BrosLMV_Runtime` con permisos
  mínimos en lugar de `SA`.

### Compatibilidad
- COM (ProgID/CLSID) sin cambios: solo reemplazar la DLL. `AssemblyVersion` → **2.5.0**.
- El desinstalador (proyecto aparte) debe llamar `Rutas.BorrarCredencial()` al limpiar.

---

## [2.4.0] — 2026-06-26

### Agregado
- **`ctx.erp.*`** — clase `ErpContext` en `Scripting.cs` (~280 líneas): wrapper tipado
  sobre `XEngineLib` y los COM auxiliares de CONTPAQi. Disponible en cualquier script C#
  como `ctx.erp.<método>`. Elimina la necesidad de usar reflexión cruda (`Com.Call`)
  para llamar a XEngine. Cubre:
  - *Propiedades de contexto:* `UserId`, `UserName`, `OwnedBusinessEntityId`,
    `ActiveModuleId`, `CurrencyId`, `ComercialRFC`, `SoftwareVersion`.
  - *Operaciones de documento:* `AffectStock`, `AffectStockNEW`, `RecalcDocument`
    (vía `Doc.clsMain`), `RecalcCompleto` (recalc + costos + saldo), `CalcularCostos`,
    `UpdateStatusDelivery`, `UpdateDocumentPaidInfo`, `ActualizarParcialidad`,
    `CancelDocument`, `ReactivateDocument`, `Save`, `Delete`, `RefreshDocumento`.
  - *UI de CONTPAQi:* `RefreshGrid`, `RefreshRibbon`, `GotoModuleID`, `OpenModule`,
    `OpenBrowser`, `ShowMessage`.
  - *Folio* (vía `LBS.clsMain`)*:* `GetFolioPrefix`, `GetNextFolio`.
  - *Existencias y precios:* `GetProductStock`, `GetSalePrice`, `GetBuyPrice`,
    `GetCostPrice`, `GetPriceWithTaxes`, `GetCurrencyRate`, `GetCurrencyRateBanxico`,
    `GetCoefConversion`, `ProductIsKit`.
  - *Crédito:* `VerifyCreditLimit`, `VerifyCreditLimitOverdue`.
  - *Parámetros:* `GetModuleParameter`, `SaveModuleParameter`, `GetParameter`.
  - *DLookup:* `DLookup`, `DLookupStr`, `DLookupInt`.
  - *Utilidades:* `GetTotalLetter`, `GetBarCode`, `DecryptString`, `EncryptString`,
    `ValidRFC`, `FormatCurrency`, `NumDecimalesMoneda`, `NumDecimalesPrecioUnit`,
    `NumDecimalesConceptos`.
  - *Log:* `WriteToLog`, `WriteToTableLog`.
  - *Impresión / exportación:* `PrintDoc`, `PrintModule`, `UpdatePrintedOn`,
    `CreatePDF`, `ExportQueryToExcel`, `ExportJanusToExcel`.
  - *Correo:* `SendMail` (usa `engUserMailConfig` de CONTPAQi + `DecryptString`).
  - *Internet / Shell:* `IsConnectedToInternet`, `GetWebContent`, `RunShellExecute`.
  - *CFDI:* `AlreadyDocsSigned`, `GetStatusPaidID`.
  - *Escape hatch:* `CrearHelper(progId)` (crea cualquier COM con `XEngineLib` seteado),
    `XE` (acceso al objeto XEngine crudo para funciones no cubiertas).
- **`ctx.ResolverTokens(template)`** — motor de tokens en `ScriptContext`: sustituye
  `{pID}`, `{pIDs}`, `{pUserID}`, `{pModulo}`, `{pEmpresa}` y `{DATOS:Campo}`
  (campo de la primera fila seleccionada del grid, leído por reflexión sobre el
  `ADORecordset` de `janusGrid`). Base del motor de recetas no-code.
- **`docs/SCRIPTING_CONTRATOS.md`** — referencia completa del API `ctx.*` y `ctx.erp.*`:
  tabla de todos los métodos con descripción, ejemplos de código, patrón de "crear
  documento" completo, y equivalente Python (v3.0).

### Compatibilidad
- El COM (ProgID/CLSID) **no cambia**: no requiere re-registrar ni re-provisionar.
  Solo reemplazar la DLL en `C:\BrosLMV\bin`. `AssemblyVersion` sube a **2.4.0**.
- `ctx.XEngineLib` sigue disponible (el escape hatch directo sigue funcionando).

---

## [Sin versión] — 2026-06-22 — Documentación de diseño multi-lenguaje

Trabajo de **diseño** (no toca el programa; producción seguía en 2.3.0). Inicio del
planteamiento **multi-lenguaje**.

### Agregado
- **`docs/ARQUITECTURA_V3.md`** — diseño técnico del host x64 fuera de proceso (Named
  Pipes + Protobuf sin gRPC, `ctx` remoto, SQL solo-proxy, seguridad, empaque).
- **`docs/XENGINE_FUNCIONES.md`** — catálogo de funciones de `XEngineLib` (insumo de la
  capa `ctx.erp.*`).
- **`docs/RECETAS_NOCODE.md`** — diseño del motor de recetas no-code (botones sin
  programar: tokens + acciones + estructuras de documento), en C# in-process.

---

## [2.3.0] — 2026-06-21

Conexión robusta de la consola y versión dinámica en el instalador.

### Corregido
- **Conexión de la consola desde la pestaña General.** Cuando CONTPAQi no entrega una
  conexión viva ejecutable, la consola ahora **combina** el servidor y la base de la
  **empresa activa** (que sí expone `XEngineLib.DataLayer.ConnectionString`) con las
  **credenciales** guardadas en `C:\BrosLMV\bin\broslmv_conn.txt`. Una sola credencial
  sirve para todas las empresas (la base la pone el DataLayer). Antes caía al respaldo
  con la plantilla sin rellenar → error 25 y **lentitud** (timeout en cada operación);
  ahora detecta la plantilla y resuelve rápido.
- `Conexion.DataLayerConnString()`, `ParseVal()` y `EsPlantilla()` nuevos para esto.

### Agregado
- **El instalador guarda las credenciales:** al "Probar conexión" en el GUI escribe
  `broslmv_conn.txt` con servidor + usuario + contraseña, para que la consola pueda
  conectar a SQL en ese equipo.
- **Versión dinámica:** el instalador/desinstalador muestran la versión real del
  ejecutable (lee el ensamblado), ya no un texto fijo.

---

## [2.2.0] — 2026-06-21

Scripts almacenados en la base de datos de cada empresa.

### Cambiado
- **Scripts en SQL (tabla `zzBrosScript`) por empresa**, en vez de archivos por carpeta.
  Quedan en la BD de la empresa → **compartidos automáticamente entre todas las
  terminales** (no hay que copiar archivos por equipo) y aislados por empresa. El
  despachador lee **SQL primero** y, por compatibilidad, cae a archivo
  (`scripts\<EMPRESA>\` y raíz).
- La consola **crea las tablas `zzBros*` automáticamente al guardar** si faltan
  (`BrosAsegurarTablas`), así no hay que provisionar la empresa solo para guardar un
  script. Mensaje de error más claro si no hay conexión.

### Agregado
- `ScriptContext.BrosGuardar/BrosCargar/BrosScriptsDisponible/BrosAsegurarTablas`.

---

## [2.1.0] — 2026-06-21

Scripts separados por empresa (base de datos).

### Agregado
- **Carpeta de scripts por empresa:** cada base de datos usa
  `C:\BrosLMV\scripts\<EMPRESA>\` (el nombre = la BD activa, `ctx.Empresa()`). Así un
  mismo nombre de script (p. ej. `EnviarOrdenCompra`) puede tener **reglas distintas en
  cada empresa** sin chocar. `Rutas.ScriptsDe(empresa)` crea/devuelve esa carpeta.
- La consola muestra dos secciones en la biblioteca: **"Scripts — <empresa>"** y
  **"Compartidos (todas)"** (los de la raíz). Abre/guarda en la carpeta de la empresa
  activa; el `_historial` queda junto al script.

### Cambiado
- El despachador de botones (`ClsMain.EjecutarScript`) y la consola resuelven el script
  **primero en la carpeta de la empresa** y, si no está, en la **raíz** (compartidos).

### Compatibilidad
- Retro-compatible: los scripts que ya estaban en la raíz siguen funcionando como
  **compartidos** (fallback). El COM (ProgID/CLSID) **no cambia**. `AssemblyVersion`
  sube a **2.1.0**; reinstala con `BrosLMV-Instalador.exe` para desplegar el addon nuevo.

---

## [2.0.0] — 2026-06-20

Instaladores gráficos `.exe` autocontenidos, desinstalador y consola con la nueva
estética. `AssemblyVersion` del addon sube a **2.0.0**.

### Agregado
- **`BrosLMV-Instalador.exe`** (C# WPF, un solo archivo autocontenido): pantalla de
  **bienvenida** → al confirmar instala el **runtime** (despliega las DLLs embebidas
  como `payload.zip` a `C:\BrosLMV`, copia el icono a `…\ComercialSP\Icons`, registra
  el COM con UAC) → abre el **GUI de provisión**. En una terminal sin SQL, basta cerrar
  el GUI. Sustituye al `Instalar.ps1` manual.
- **GUI de provisión por empresa:** ventana (servidor\instancia + usuario/contraseña),
  lista las **empresas Comercial Start/Pro** detectadas por esquema, con estado
  (Pendiente/Ya instalado), buscador, filtro y selección por casillas. Provisiona las
  marcadas (idempotente).
- **`BrosLMV-Desinstalador.exe`** (C# WPF): **Quitar de empresas** (botón + grupo +
  tablas `zzBros*` vía `desprovision_empresa.sql`) y **Quitar de este equipo**
  (des-registra el COM, borra el icono y **elimina por completo `C:\BrosLMV`** para no
  dejar las DLLs en el equipo).
- **Icono del botón:** `BrosLMV.ico` se copia a `…\ComercialSP\Icons` y el botón del
  ribbon lo usa (`IconFile='BrosLMV.ico'`).
- **Consola con estética BrosLMV:** cabecera con gradiente navy + **logo** + título +
  icono de ventana (logo/icono embebidos en la DLL). Misma funcionalidad.
- **Build:** `build\generar_exes.ps1` (empaqueta y compila los `.exe`); guía de
  compilación paso a paso en `DESARROLLO.md`.

### Cambiado
- **`provision_empresa.sql` ahora es genérico/adaptable:** las columnas de
  `engRibbonControl` varían entre versiones de Comercial (unas tienen `Comments`/`AFP`,
  otras no). Los INSERT al ribbon se arman dinámicamente con **solo las columnas que
  existen** (vía `FOR XML PATH`, compatible con SQL Server 2005+). Corrige el fallo
  *"Invalid column name 'Comments'/'AFP'"* en empresas sin esas columnas.
- `INSTALACION.md` reescrito para el flujo de los `.exe` (instalar/provisionar/desinstalar).
- `DESARROLLO.md` reescrito: requisitos, estructura del repo y **compilación paso a
  paso** (núcleo + instaladores).

### Notas
- El COM **no cambia** (ProgID/CLSID iguales): instalar la 2.0.0 sobre una instalación
  previa no requiere re-provisionar las empresas existentes.
- Validado: provisión y des-provisión probadas con rollback en empresas reales
  (esquemas de 19 y 21 columnas); ambos `.exe` compilan y abren sin error.

---

## [1.2.1] — 2026-06-20 (solo documentación)

Sincronización y limpieza de la documentación. **Sin cambios en el binario**: el
componente sigue en `AssemblyVersion 1.2.0` (no requiere recompilar ni re-registrar).

### Cambiado
- **`INSTALACION.md` reescrito y al día** (en `docs\` e `instalador\`, idénticos):
  separa **componente por equipo** vs **provisión por empresa**, usa
  `provision_empresa.sql`, nombra el botón **"Consola BrosLMV"** y el grupo
  "BrosLMV" en la pestaña General, refleja las **15 DLLs** + `x86\SQLite.Interop.dll`
  y añade la sección **servidor + terminales**.
- `ESPECIFICACION.md`: §14 ahora documenta las **3** tablas del ribbon (incluye
  `engRibbonGroup` "BrosLMV" en `RibbonTabID=1`); §15 corrige a **5** `.cs`
  (incluye `Datos.cs`); versión del manifiesto COM corregida a `1.2.0.0`.
- `DESARROLLO.md`: enlaces internos corregidos (eran relativos a la ubicación
  anterior del archivo).
- **Portada del repo:** el "empieza aquí" se consolidó en **`README.md`** (convención
  de GitHub) reemplazando a `LEEME.md`; enlaces de `INDICE.md` y `DESARROLLO.md`
  actualizados a `../README.md`.
- `MANUAL.md`: nombre del botón unificado a **"Consola BrosLMV"**.
- `sql\0_buscar_grupos.sql`: reetiquetado como **diagnóstico opcional** (la
  instalación normal no lo necesita; `provision_empresa.sql` crea el grupo y el botón).

### Quitado
- Referencias a archivos inexistentes (`1_crear_boton_consola.sql`) y a la carpeta
  antigua `instalacion\` en la documentación vigente (se conservan solo en las
  entradas históricas de este CHANGELOG).

---

## [1.2.0] — 2026-06-19

Rediseño de la consola (editor de código real) y almacenamiento local SQLite.

### Agregado
- **Editor de código Scintilla** en la consola: resaltado de C#, números de línea,
  línea actual, autocompletado al escribir `ctx.` (`ScintillaNET.dll`; el nativo
  `SciLexer.dll` va embebido y se autoextrae).
- **Consola rediseñada** (3 paneles): biblioteca de scripts (izquierda), editor
  (centro), inspector de contexto + métodos de `ctx` (derecha), salida en pestañas
  (Salida/Errores/Mensajes) y barra de estado con tiempo de ejecución.
- **Ejecución segura:** Ejecutar todo / Ejecutar selección / Verificar; detección
  de operaciones de escritura (UPDATE/DELETE/INSERT/DROP/TRUNCATE/…) con
  confirmación; **Modo solo lectura** (bloquea `ctx.NonQuery`); cronómetro.
- **Ayuda integrada:** lista de métodos de `ctx` con firma y descripción; doble
  clic inserta el ejemplo.
- **Almacenamiento local SQLite** (`Datos.cs` → `C:\BrosLMV\data\broslmv.db`, sin
  servidor, a nivel de equipo):
  - **Auditoría** de cada ejecución (consola y botón): fecha, empresa, módulo,
    usuario, script, origen, duración, filas afectadas, estado, error.
  - **Recientes** y **Favoritos** en la biblioteca (clic derecho para marcar).
  - Visor **Historial / Auditoría** en la consola.
- `ScriptContext.SoloLectura` y `ScriptContext.FilasAfectadas`;
  `ctx.Empresa()`, `ctx.ModuloActivo()`.

### Cambiado
- Extensión de scripts ahora **`.ctx`** (los botones buscan `.ctx` y, por
  compatibilidad, `.csx`). La consola guarda en `.ctx` y archiva la versión
  anterior en `scripts\_historial\`.
- Paquete: ahora **15 DLLs** + el nativo `bin\x86\SQLite.Interop.dll`.
  `Instalar.ps1` copia el nativo y crea `C:\BrosLMV\data`.

### Rendimiento
- Se eliminó la **compilación doble** al ejecutar (Verificar y Ejecutar comparten
  el mismo script compilado).
- **Caché** de scripts compilados por código: re-ejecutar el mismo código es
  instantáneo (~0 ms).
- **Precalentado** de Roslyn en segundo plano al abrir la consola (el warmup de
  ~2s ya no bloquea). La ventana aparece de inmediato (el contexto/SQL se carga
  en el evento `Shown`).
- `ctx.Empresa()` cacheado (evita un round-trip SQL por refresco de contexto).
- Medido: 1ª ejecución ~190 ms (antes ~1400-3000), repetición ~0 ms.

### Corregido
- **Apertura de la consola colgada ~14s** al abrir desde la pestaña General: sin
  grid no había conexión viva y `Empresa()` caía al respaldo SqlClient con cadena
  inválida → timeout de 15s. Ahora `Empresa()` usa SOLO la conexión viva (sin
  colgarse), el respaldo SqlClient falla en 4s, y la ventana se pinta completa
  antes de cargar el contexto (`BeginInvoke` en `Shown`).
- `GetModuleConnectionString` ahora se intenta con el módulo activo (conexión
  potencialmente válida sin grid). `DiagConexion()` ampliado para inspeccionar
  `DataLayer` y el módulo activo.

### Pendiente (pulido visual)
- Tema más plano/moderno, anchos de columnas del inspector y splitters.

### Conexión y selección genéricas (clave para multi-terminal)
- **Conexión independiente de la pestaña:** `ctx` obtiene la conexión del grid
  (en listas) o de **`XEngineLib.DataLayer`** (disponible en cualquier pestaña,
  incluida General). Se valida ejecutando `SELECT 1` (más fiable que `.State`, que
  en el DataLayer de CONTPAQi no es 1). Antes solo conectaba desde una lista.
- **Llave primaria dinámica por módulo:** `GetSelectedIds()` ya no lee fijo
  `DocumentID`; lee la columna llave del módulo activo desde
  `engModuleParameter (ModuleID, ParameterKey='PrimaryKey')` →
  Proveedores=`SupplierID`, Documentos=`DocumentID`, Pagos/Cobros=`FinancialOperationID`,
  con respaldo a `DocumentID`. Antes, seleccionar un proveedor no devolvía nada.

---

## [1.1.0] — 2026-06-19

Conexión automática a la empresa activa (sin archivo por empresa).

### Agregado
- **Conexión automática:** `ctx` reutiliza la conexión que CONTPAQi ya tiene
  abierta a la empresa actual.
  Se obtiene de `XEngineLib.DataLayer` o de `janusGrid.ADORecordset.ActiveConnection`
  (`Scripting.cs` → clase `Conexion`). Ya **no hace falta `broslmv_conn.txt` por
  empresa**: el mismo botón funciona en las 40 empresas sin configurar nada.
- `ctx.DiagConexion()` — diagnóstico que indica de dónde sale la conexión.
- Script `DIAGNOSTICO.csx` para verificar la conexión tras instalar.

### Cambiado
- `ctx.Scalar` / `ctx.Query` / `ctx.NonQuery` ahora usan la conexión viva de
  CONTPAQi por ADO; si no hubiera (módulo sin grid), caen al archivo de respaldo.
- `ctx.OpenConn()` (SqlClient) resuelve la cadena con
  `XEngineLib.GetModuleConnectionString` y, si no, con el archivo.
- `broslmv_conn.txt` pasa de **obligatorio** a **respaldo opcional**.
- Helpers de late-binding COM unificados en la clase interna `Com`.

### Quitado
- Propiedad `ctx.ConnStr` (ya no aplica; la conexión es automática).

### Corregido
- `Instalar.ps1` ahora asegura el registro COM en el hive de **32 bits**
  (`WOW6432Node`): RegAsm a veces escribe el mapeo ProgID→CLSID solo en el hive de
  64 bits y ComercialSP (32-bit) no lo encontraba. Se fuerza el mapeo y, si hace
  falta, se copia el árbol del CLSID.

### Verificado en CONTPAQi real (2026-06-19)
- Instalado junto a otro addon (coexisten). `DIAGNOSTICO.csx` mostró
  "Conexión ADO viva de CONTPAQi: SI" con la base activa. Cadena completa
  validada: carga COM 32-bit + AssemblyResolve + Roslyn + conexión automática +
  consola.

### Documentación
- Nueva **`ESPECIFICACION.md`**: blueprint técnico completo para reconstruir la
  herramienta desde cero (contrato COM, versiones exactas, nombres exactos de
  miembros COM del grid y la conexión, registro, trampas aprendidas).
- Nueva **`CAPACIDADES.md`**: alcance y poder de la herramienta (reportes HTML con
  gráficas, análisis de datos, Excel/PDF, automatización, integraciones, uso de
  librerías externas, extensión opcional a Python) con ejemplos.
- La doc del prototipo `DOCUMENTACION.md` se redujo a una **nota
  histórica** para no inducir a error; la fuente de verdad es `instalacion\`.
- Auditada toda la documentación: sin referencias obsoletas (conexión por archivo,
  C# 5, csc.exe) salvo donde se describe el cambio.
- Documentación reescrita como **herramienta autónoma**: el producto se describe
  por sí mismo, sin comparaciones ni referencias a productos de terceros. El SQL de
  alta de botones ya no depende de botones externos (elige el grupo más poblado).

---

## [1.0.0] — 2026-06-19

Primera versión del producto BrosLMV (genérico, instalable en cualquier empresa).

### Agregado
- COM server `BrosLMV.clsMain` (CLSID `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`),
  autónomo y en proceso, sin servicios de licencia externos.
- Motor de scripts **Roslyn**: botones como archivos `.csx` que se compilan al
  vuelo, sin recompilar la DLL (`Scripting.cs` → `ScriptRunner`).
- Contexto inyectado `ctx` (`ScriptContext`) con: `GetSelectedIds`, `Scalar`,
  `Query`, `NonQuery`, `OpenConn`, `JoinIds`, `Msg`, `Confirm`, `Log`, `UserID`.
- **Consola de scripts** (botón `BrosLMV.CONSOLA`): editar, Verificar, Ejecutar
  (F5), Abrir, Guardar (`Consola.cs`).
- Lectura de la selección del grid vía `XEngineLib.janusGrid.ADORecordset`
  clonado (`Scripting.cs` → `GridSelection`).
- Resolutor `AssemblyResolve` en el constructor estático de `clsMain` para cargar
  las DLLs de Roslyn por nombre (sustituye binding redirects).
- Paquete de instalación: `Instalar.ps1`, `Desinstalar.ps1`, plantilla de
  conexión `broslmv_conn.txt`, las 13 DLLs en `bin\`.
- SQL: `0_buscar_grupos.sql`, `1_crear_boton_consola.sql`,
  `plantilla_crear_boton.sql` (alta de botones portable a cualquier empresa).
- Scripts de ejemplo: `EJEMPLO_suma.csx`, `EJEMPLO_proveedores.csx`.
- Documentación: `README.md`, `INSTALACION.md`, `MANUAL.md`, `src\README.md`,
  este `CHANGELOG.md`.

### Notas técnicas
- Núcleo en C# / .NET Framework 4.8, compilado con .NET SDK (`net48`).
- Instala en `C:\BrosLMV\` (`bin`, `scripts`, `logs`).
- Verificado en proceso x86 limpio: scripts correctos compilan con 0 errores;
  scripts con error reportan línea exacta. Falta confirmar arranque dentro de
  CONTPAQi (carga COM real + consola).

---

## Plantilla para la próxima versión (copiar y llenar)

```
## [X.Y.Z] — AAAA-MM-DD

### Agregado
- ...
### Cambiado
- ...
### Corregido
- ...
### Quitado
- ...
```

### Criterio de versión (semver simple)
- **X (mayor):** cambios que rompen compatibilidad (ProgID, rutas, API de `ctx`).
- **Y (menor):** funciones nuevas compatibles (nuevo método en `ctx`, mejoras).
- **Z (parche):** correcciones que no cambian la forma de usarlo.
