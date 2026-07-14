# Estado del proyecto y cómo continuar

> **Punto de entrada al retomar.** Resume dónde vamos, qué quedó pendiente y qué sigue. Se
> mantiene al día con cada cambio. Detalle por versión en [`CHANGELOG.md`](CHANGELOG.md).

## REGLA DE ORO: documentar todo, siempre
Cualquiera que retome el proyecto debe poder hacerlo **desde cero** con solo los `.md` + el
código, sin depender de contexto que no esté escrito. Por eso **se documenta TODO, siempre, en
el momento del cambio**: entrada en
[`CHANGELOG.md`](CHANGELOG.md) + `AssemblyVersion` en `src/ClsMain.cs` + entrada en
`src/assets/notas_version.html` (lo que ve el usuario en *Acerca de*) + los `.md` afectados +
commit atómico. **No avanzar sin documentar.** Pruebas/temporales van a `/.temp_tests` (gitignored).

### Regla añadida (2026-07-01): toda recomendación/mejora va al MANUAL, bien explicada
No basta con arreglar algo y anotarlo solo en el `CHANGELOG.md` (eso es historial técnico) o
mencionarlo solo en el chat con el usuario. **Cada vez que se descubra un patrón, límite,
"gotcha" o buena práctica** (p. ej. "esto truena si haces X, hazlo así en vez de asá"; "en Python
esto es automático, en C# no"), se documenta **también** en [`MANUAL.md`](MANUAL.md), en la
sección que corresponda (o una nueva si no encaja), **bien explicada**: qué pasa, por qué pasa, y
qué hacer — para que quien escriba scripts la encuentre y la use, no solo quien lee el código
fuente o el historial de versiones. Ejemplo real: la sección 10 "Ventanas WinForms: modeless"
(agregada al documentar por qué C# necesita `try/catch` manual y Python no). Ver también la tabla
de "Mantener la documentación" en [`DESARROLLO.md`](DESARROLLO.md) §7.

## ⚠️ TRAMPA: el commit NO instala solo — hay que regenerar el instalador
**Pasó de verdad (2026-07-01):** el usuario reinstaló para una demo con un cliente y le apareció
la v2.17.0 **sin las plantillas nuevas**, porque `dist/BrosLMV-Instalador.exe` llevaba semanas sin
regenerarse (el código en GitHub ya iba en v2.18.x, pero el `.exe` distribuido seguía empacando
v2.17.0). **Comittear no actualiza el instalador — son pasos separados.**
**Regla:** después de cualquier cambio que deba llegar a una instalación nueva, correr SIEMPRE:
```
build\generar_instalador.ps1   # addon Release -> instalador\bin, host -> instalador\host, workers
build\generar_exes.ps1         # payload.zip + compila dist\BrosLMV-Instalador.exe/Desinstalador.exe
```
`generar_instalador.ps1` **mata ComercialSP a la fuerza** (para poder sobreescribir el DLL) — avisar
antes si hay una demo en curso. Además, `generar_exes.ps1` ahora toma la versión del `.exe` desde
el addon YA EMPACADO (`/p:Version=` dinámico) — antes estaba fija a mano en los `.csproj` de
`instaladores/Empresas` y `Desinstalador`, y se quedaba obsoleta (se descubrió en 2.14.0 mientras
el addon ya iba en 2.18.1). Si algún día hace falta compilar esos `.csproj` a mano, el `<Version>`
fijo ahí es solo un respaldo — desactualízalo si quieres, no es la fuente de verdad.

## Estás aquí (2026-07-14, v2.32.0)

- **Historial de git unificado.** El repo tuvo dos líneas de trabajo divergentes (dos raíces
  de historial sin ancestro común, cada una fruto de un squash independiente) que se
  reconciliaron a mano con `git merge --allow-unrelated-histories`. Si algo de lo que sigue
  te suena repetido o contradictorio contra un commit viejo, este merge es la explicación.
- **Timbrado CFDI real:** `ctx.erp.Timbrar(documentId, pruebas)` — COM directo a
  `CFDI3.clsMain` (el mismo componente que usa el propio módulo de facturación de Comercial),
  sin depender del SDK oficial de CONTPAQi.
- **Grid editable en `ctx.form()`:** columnas texto/número/decimal/fecha/bool/combo, precarga
  de filas, agregar/quitar renglones — de punta a punta (`RelayingCallbackSink` + `HostClient`
  con un `DataGridView` real). Reemplaza cientos de líneas de WinForms a mano.
- **Traceback completo en errores de Python.** Antes solo se veía `mensaje [CODIGO]`; ahora se
  ve línea, función y la cadena completa de llamadas — el dato ya se capturaba desde el diseño
  original (`runner.py`), pero se descartaba en el addon.
- **`ctx.confirm()` / `ctx.select_file()` / `ctx.select_folder()`** — existían en el protocolo
  y en C#, pero nunca se conectaron del lado de Python (causaban `AttributeError` reportado
  por la comunidad).
- **`ctx.read_excel()` / `ctx.write_excel()`** — con `openpyxl`, sin automatizar Excel vía COM.
- **Instalador "Empresas" con versión por empresa** (`zzBrosInfo`), estado "Actualizar
  disponible" distinto de "Pendiente"/"Ya instalado".
- **4 plantillas nuevas y cortas** en el menú de ejemplos, mostrando las capacidades de arriba.
- Ver [`CHANGELOG.md`](CHANGELOG.md) [2.25.0]–[2.32.0] para el detalle línea por línea de cada
  pieza.

## Estás aquí (2026-07-11, v2.24.0)

- **`ctx.show_html()` — ventana HTML/WebView2 embebida (Python), primer caso real verificado.**
  Se probó contra una empresa real (GGV_DE_MEXICO, botón `ReporteXVehiculo.py`): un dashboard
  de flota completo (HTML+CSS+JS+datos) armado en Python y mostrado embebido dentro de
  CONTPAQi vía WebView2, sin escribir ningún archivo compartido ni depender del navegador
  externo. Ver [`CHANGELOG.md`](CHANGELOG.md) [2.24.0] para el detalle completo del protocolo
  (`ctx.py` → `PythonProcess.HandleShowHtml` → `IHostCallbackSink.ShowHtml` →
  `HostClient.RenderUiHtml`) y del hilo STA dedicado que evita el deadlock/`RPC_E_CHANGED_MODE`
  confirmado en pruebas reales. **Límite real descubierto y documentado:** `NavigateToString`
  tope ~2MB — se resuelve comprimiendo el payload con gzip+base64 y descomprimiendo con
  `DecompressionStream` nativo (sin vendorizar librerías). Efecto colateral: el bug de
  U+2028/U+2029 rompiendo `<script>` queda eliminado por construcción (el payload es base64,
  sin `<`/`>`). Camino de despliegue verificado en vivo: `dotnet build src\BrosLMV.csproj` +
  `dotnet publish host\BrosLMV.Host` + sincronizar las **2 copias reales** de
  `workers\python\broslmv\ctx.py` que carga el runtime (`C:\BrosLMV\workers\python\` y
  `C:\BrosLMV\host\workers\python\` — esta última es la que el host de verdad usa). **Nuevo
  ejemplo:** `PLANTILLA_DISENADOR_FORMULARIOS_PYTHON.py`, diseñador visual no-code de
  formularios construido sobre `ctx.show_html`.

## Estás aquí (2026-07-03, v2.23.0)

- **Investigación de un SDK alterno de CONTPAQi — completa, sin cambios de código.** Se evaluó si
  un mecanismo alterno de automatización (distinto al de reflexión que ya usa `src/Scripting.cs`)
  aportaba algo para hacer BrosLMV más robusto. Se probó a fondo contra una base de datos de
  pruebas y se confirmó que el patrón que ya usamos para lote/serie e impuestos por partida en
  Recepción/Factura de Compra es correcto. **Conclusión: no se integró nada al código de
  producción** — el código actual ya es robusto y validado, y no había una mejora real que
  justificara una dependencia nueva.

- **v2.22.0 / v2.23.0 — Documentos derivados: Recepción de Compra y Factura de Compra.** El
  usuario pidió transformar Órdenes de Compra en Recepción (con lote/serie) y en Factura, ambas
  soportando N OC → 1 documento. Antes de escribir código se investigó a fondo (siguiendo la
  regla del proyecto): se verificó contra una base de datos de pruebas el perfil real de ambos
  documentos, lo que evitó adivinar el encabezado y corrigió 3 errores reales que ya se habían
  escrito a ciegas (costo=precio unitario, tabla `docDocumentLot` en vez de
  `docDocumentItemLot`, `StatusID` faltante en series). Ver [`MANUAL.md`](MANUAL.md) §10.4 para
  el patrón de documentos derivados (cada uno
  usa su propia columna de vínculo por partida: `DeliverDocumentItemID` para Recepción,
  `SourceDocumentItemID` para Factura — no hay vista nativa que soporte N OC → 1 documento, así
  que ambas plantillas calculan pendientes con SQL propio). También se encontró y corrigió un
  bug real de WinForms (refrescar un grid dentro de su propio evento de checkbox → excepción
  reentrante) y un bug de negocio de CONTPAQi (el `PaymentAgenda` que genera `Save()` en la
  Factura queda con montos en $0 si se cambió `PaymentTermID` por SQL antes — hay que
  regenerarlo a mano con `engPaymentTermDetail`). **Pendiente:** confirmación del usuario
  probando ambas plantillas en Comercial real.

- **Botones modeless** (v2.19.0/2.19.1, verificado por el usuario): C# con `frm.Show()` +
  `try/catch` en manejadores con SQL; Python ya no bloquea Comercial (`UiPump`, ver
  [`UI_VENTANAS.md`](UI_VENTANAS.md) §5). **Plantillas base** nuevas (`PLANTILLA_BASE_CSHARP_WINFORMS.ctx`,
  `PLANTILLA_BASE_PYTHON_WINFORMS.py`) para arrancar ventanas nuevas ya con las reglas aplicadas.
  Todo documentado en [`MANUAL.md`](MANUAL.md) §10 "Ventanas WinForms: modeless".
- **Segundo par "Ejemplo Premium": Orden de Compra** (módulo 183, C# y Python) — el usuario
  probó v2.20.0 y encontró 3 bugs reales: impuesto no aplicado, sin columna de descuento, y
  "Estatus de entrega: No Aplica". **v2.20.1 los corrigió en la raíz**:
  `ErpContext.AgregarArticulo` ahora guarda `TaxPerc` (resuelto de `vwLBSTaxPerc`, antes se
  guardaba `TaxTypeID` pero el % quedaba en 0) y acepta `taxTypeIdOverride`/`descuentoPerc` —
  beneficia también a la Requisición. `ctx.erp.UpdateStatusDelivery(doc)` agregado tras `Save`
  (no lo calcula `RecalcCompleto`, hay que pedirlo aparte). Todo documentado en
  [`MANUAL.md`](MANUAL.md) §6.2/6.3.
- **v2.21.0**: el usuario probó v2.20.1 (funcionó) y pidió dos cosas más sobre la misma Orden de
  Compra: (1) apartado de **Totales** (Subtotal/Descuento/Impuestos/Total + Total en letra,
  calculado partida por partida con el `TaxPerc` real); (2) **doble clic en una partida** abre el
  **detalle del producto** (datos generales, clasificaciones, existencia por almacén, listas de
  precios, precios por proveedor). Aplicado igual en C# y Python. Documentado en
  [`MANUAL.md`](MANUAL.md) §10.3. Verificado offline: C# con `ScriptRunner.Compilar` (0 errores);
  Python con `.temp_tests/smoke_test_oc_python3.py` (arma una partida con descuento e impuesto y
  confirma los 4 totales + que el detalle de producto no truena con datos simulados).
- **v2.21.1**: el usuario probó v2.21.0 en Comercial — Totales y detalle de producto funcionaron,
  pero la ventana de detalle se veía "amontonada". Se rediseñó el layout (700×800, pares
  etiqueta/valor en columnas, tablas más grandes con más aire) — mismo cambio aplicado a C# y
  Python, reverificado con `ScriptRunner.Compilar` y `smoke_test_oc_python3.py`.
- **v2.21.2**: el usuario reportó que el botón Python de Orden de Compra tarda muchísimo y
  termina en el diálogo nativo "the other application is busy" (XEngine), sin que Retry lo
  resuelva. Se descartó bloqueo de SQL Server y que la consulta de impuestos de v2.21.0 fuera
  lenta (1 ms medido). No reproducible fuera de Comercial real, así que se agregó una traza
  (`logs\PythonErp_AAAAMMDD.txt`) de cada llamada `ctx.erp`/`ctx.query` desde Python, antes/
  después + tiempo.
- **v2.21.3 → v2.21.9** (primera mitad de la saga; el problema #2 de abajo NO quedó resuelto
  todavía en v2.21.9 — el arreglo real llegó en v2.21.10): ver [`CHANGELOG.md`](CHANGELOG.md)
  para el detalle completo, aquí el resumen.
  - *Causa raíz #1 (el "busy")*: `Consola.Ejecutar()` corría Python **síncrono**, bloqueando el
    hilo de Comercial mientras la ventana estuviera abierta (el botón del ribbon ya usaba
    `Task.Run`+`UiPump` desde v2.19.0 — por eso solo pasaba desde la Consola). Corregido en
    **v2.21.4**, junto con una guardia (`GuardiaEjecucion`) contra ejecuciones encimadas por
    clics repetidos. (En el camino se probó y se revirtió una caché estática de la conexión ADO
    — ver nota abajo, causó el problema #2.)
  - *Causa raíz #2 ("objeto cerrado" en `NuevoDocumento`, solo como botón, nunca desde la
    Consola)*: `Conexion.ObtenerAdo` prefería la conexión ligada al grid activo
    (`janusGrid.ADORecordset.ActiveConnection`), que se puede CERRAR si el grid se refresca
    mientras una ventana interactiva sigue abierta minutos — un botón de ribbon típicamente
    corre con un grid visible; la Consola casi nunca. **v2.21.5** agregó el detalle real de
    `Com.LastError` a los mensajes de error (indispensable para diagnosticar esto — antes solo
    decían "no se pudo crear"). **v2.21.6** invirtió el orden: `DataLayer` primero (no depende
    de ningún grid). **v2.21.7** hizo `ScriptContext.Ado()` auto-sanador (revalida con
    `SELECT 1` antes de cada uso). **v2.21.8** corrigió que esa validación NUNCA cerraba el
    recordset de prueba — al llamarse ahora en cada `ctx.query`/`ctx.erp` (v2.21.7), se
    acumulaban recordsets sin cerrar hasta agotar el límite de ADO y manifestarse como "objeto
    cerrado" — este fue el fix que de verdad lo resolvió.
  - *Efecto colateral descubierto al confirmar (**v2.21.9**)*: un botón guardado desde la Consola
    (Plantillas → Ejemplo Premium) mostraba "?" en vez de acentos/emoji. Causa:
    `File.ReadAllText()` en `Consola.cs` sin encoding explícito puede caer a ANSI sin BOM en
    .NET Framework. Corregido (UTF-8 explícito) + el script ya dañado se volvió a subir con el
    contenido correcto vía consulta parametrizada.
  - Lección para la próxima: cuando un error solo pasa en un CAMINO de ejecución (botón) y no en
    otro (Consola) con el mismo script, la diferencia casi siempre está en el ENTORNO/estado que
    rodea la ejecución (aquí: qué conexión/grid está activo), no en el código del script mismo.
- **v2.21.10 → v2.21.12** (fin de la saga, **confirmado resuelto por el usuario**: creó una Orden
  de Compra real desde el botón del ribbon Y guardó/cargó un script con acentos y emoji sin
  daño): ver [`CHANGELOG.md`](CHANGELOG.md) para el detalle completo.
  - **v2.21.10**: v2.21.9 no bastó — "objeto cerrado" seguía saliendo en `NuevoDocumento`. El
    usuario probó un workaround con las mismas 4 anclas por SQL directo desde Python (sin pasar
    por `ctx.erp.NuevoDocumento`) y SÍ funcionó — la diferencia: mi SQL usaba
    `BEGIN TRANSACTION`/`COMMIT` explícito, el workaround no. Se quitó el control transaccional
    manual de `NuevoDocumento`/`AgregarArticulo` (hipótesis: el `DataLayer` de CONTPAQi
    administra su propia transacción ambiental y un `BEGIN TRANSACTION` por T-SQL entra en
    conflicto). Se pierde algo de atomicidad, pero esto fue lo que de verdad lo resolvió.
  - **v2.21.11/v2.21.12**: el problema de acentos/emoji como "?" (visto y "arreglado" a medias en
    v2.21.9) reapareció porque el usuario guarda/prueba desde la Consola, no desde archivo. Se
    confirmó que tanto **guardar** (`BrosGuardar`) como **cargar** (`BrosCargar`) un script por
    la conexión viva de CONTPAQi pueden angostar el texto a ANSI — pasa con el texto grande de un
    script, no con SQL de negocio normal. Ambos ahora prefieren una conexión `SqlClient` directa
    y parametrizada (con respaldo automático al camino de siempre si no está disponible). El
    script de prueba ya tenía caracteres irreversiblemente dañados (`U+FFFD`) de antes de estos
    fixes — se reconstruyó a mano comparando contra la plantilla y se volvió a subir.

Los tres lenguajes (C# / Python / SQL) conviven y están alineados con el API real:

- **Referencias de la consola** (panel derecho) fieles al código real en los 3 lenguajes
  (v2.11.x). Ver [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md).
- **C#** (`ctx`/`ctx.erp`): verificado por lotes en CONTPAQi. Fixes: `GetTotalLetter` (currencyId
  int), `GotoModuleID` (prop-put), `GetPriceWithTaxes` (orden de args), `NumDecimales` (quitados).
- **Python**: verificado (contexto, `ctx.fila`, SQL con `@param`+dict, `user_id` real) y **ahora
  con `ctx.erp`** (v2.12.0) — relay al `ErpContext` del addon por el pipe, mismo poder que C#,
  sin copiar terceros. Ver [`PYTHON.md`](PYTHON.md).
- **SQL** directo por la conexión viva (`SELECT`, `EXEC`, tokens `{pID}`/`{pIDs}`/`{pModulo}`/
  `{pEmpresa}`/`{pUserID}`/`{DATOS:Campo}`).
- **Consola modeless** (v2.13.0): se minimiza y convive con Comercial; una sola instancia;
  refresca contexto al reactivar; **guardia de cambio de empresa** (avisa en rojo + confirma
  antes de ejecutar si cambiaste de empresa, porque el motor se captura al abrir). Las ventanas
  modeless de botones (prueba A) quedaron **verificadas** por el usuario. Ver
  [`UI_VENTANAS.md`](UI_VENTANAS.md).
- **Versión visible + Acerca de** (v2.14.0): la consola muestra su versión (encabezado + barra de
  estado) y un **Acerca de** con fecha de compilación y botón a las **notas de versión**
  (`src/assets/notas_version.html`, embebido, se abre en el navegador). **Regla:** cada versión
  actualiza `AssemblyVersion` + `CHANGELOG.md` + `notas_version.html`.
- **Crear documentos C#/Python** (v2.15.0): `ctx.erp.NuevoDocumento` + `AgregarArticulo` +
  `RecalcCompleto`, y el active-record genérico `ctx.nuevo("tabla")` (Python). **Verificado** en
  `Coctel_de_Ideas` (órdenes de compra, F1=C#, F2=Python relay, F3=ctx.nuevo). Además: los scripts
  C# ya muestran su `return` en el panel. Ver [`PYTHON.md`](PYTHON.md) §2.2. Memoria:
  [[broslmv-erp-documentos-plan]].
- **Editar registros existentes** (v2.16.0): `ctx.registro("tabla", pk)` carga un registro por PK,
  permite modificar campos y `.actualizar()` envía **solo los cambios**. `.actualizar()` ahora es
  incremental en ambos casos (nuevo y cargado).
- **Documentos 100% fieles al nativo** (v2.18.0): `NuevoDocumento` ahora crea las **4 anclas**
  (Ext/Extra/CFD/PaymentAgenda) + campos universales (MustBeSynchronized, ExportID, DateCost,
  DateDocDelivery, DateFrom, DateTo, DateLastPayment). `AgregarArticulo` llena la partida como
  el nativo (ApplyGlobalDiscount/DeductiblePerc/IsBusinessOperation/MustBeDelivered=1, DateItem,
  CoefUnit=1, ClaveUnidad/ObjetoImpuesto del producto, CostPrice opcional). **Validado campo por
  campo** en entrada/salida/solicitud (EXP-VAL-*). Ya NO se hace clonAncla ni UPDATE de esos
  campos. `ctx.erp.LastError` expone errores COM. Fix encoding UTF-8 Python. MANUAL.md reescrito
  con API completa de ctx.erp (84 métodos, recetas por tipo).

## ✅ Despliegue v2.15.0 — COMPLETADO (2026-06-27)

> DLL+PDB copiados a `C:\BrosLMV\bin`, instalador regenerado (`build\generar_instalador.ps1` +
> `build\generar_exes.ps1`). EXEs en `dist\`: Instalador 53.4 MB, Desinstalador 0.1 MB.
> Todo con v2.15.0 embebida.

## ✅ Despliegue v2.18.0 — COMPLETADO (2026-06-29)

> DLL+PDB copiados a `C:\BrosLMV\bin`, instalador regenerado. **4 anclas + campos universales +
> partida nativa + fix encoding Python UTF-8 + ctx.erp.LastError.** Validado campo por campo en
> entrada/salida/solicitud (EXP-VAL-*). MANUAL.md reescrito con API completa.
> PR en rama `fix/documentos-anclas-partida-nativo` (commit `876ab15`).

## ✅ Despliegue v2.17.0 — COMPLETADO (2026-06-28)

> DLL+PDB copiados a `C:\BrosLMV\bin` (Comercial cerrado para soltar el lock), instalador
> regenerado (`build\generar_instalador.ps1`) y EXEs (`build\generar_exes.ps1`). Runtime e
> `instalador\bin` en **2.17.0.0**. EXEs en `dist\`: Instalador 53.4 MB, Desinstalador 0.1 MB.

## ✅ Despliegue v2.16.0 — COMPLETADO (2026-06-28)

> DLL+PDB+ctx.py desplegados, instalador+EXEs regenerados. **Verificado** en Coctel_de_Ideas:
> `ctx.registro("docDocument", 11560)` cargó 104 campos, modificó Comments, `.actualizar()` envió
> solo ese campo (1 fila), verificado en BD, restaurado original. Script: `f4_registro_editar.py`.

## Pruebas de creación de documentos (sesión 2026-06-27) — todas OK en Coctel_de_Ideas
DocumentID 11556–11560 (órdenes de compra). Scripts en `/.temp_tests`: `f1_orden_compra.ctx` (C#),
`f2_orden_compra.py` (Python relay), `f3_nuevo_generico.py` (ctx.nuevo), `ejemplo_sql_mas_erp.py`
(SQL crudo + ctx.erp en cadena), `ejemplo_sql_puro.sql` (reporte SQL).

## Frentes abiertos (elegir el de menos tokens)

- ✅ **4 anclas + campos universales + partida nativa** (v2.18.0, HECHO). `NuevoDocumento` y
  `AgregarArticulo` producen documentos campo-por-campo equivalentes al nativo.
- **Transacciones en builders** (P2-a): envolver `NuevoDocumento` + 4 anclas en `SqlTransaction`.
  Hoy 5 INSERT sin rollback.
- **`ctx.msg` Python → UI** (P0-f): relay de callbacks del host al addon vía `UiRequest`.
- **Perfil por módulo sistematizado** (P1-b): leer `engModuleParameter` para automatizar
  `PaymentTermID`, `DepotIDFrom`, `DateDelivery`, agendas por tipo de documento.
- **TaxTypeID por contexto** (P0-a): experimento para inferir regla de decisión del TaxTypeID
  en documentos de compra/venta.
- **Bloque E (UX consola)**: E1 métodos por pestaña (hecho), E2 panel "datos del seleccionado"
  con arrastrar-token, E3 enriquecer CONTEXTO ACTUAL.
- **No-code / recetas** ([`RECETAS_NOCODE.md`](RECETAS_NOCODE.md)): el motor de botones sin
  programar, sobre las tablas propias `zzBrosScript`/`zzBros*` (ver §5b para la convención de
  vistas `BRO_`).

## Recordatorios de entorno

- **PROYECTO** en `C:\MLVTotal` (git + GitHub `DonPapiCandelas/BrosLMVTotal`). `C:\BrosLMV` es
  **solo runtime** (ahí se despliega). No confundir.
- **Compilar addon:** `dotnet build src\BrosLMV.csproj -c Debug`. **Host:**
  `dotnet build host\BrosLMV.Host\BrosLMV.Host.csproj -c Debug`.
- **Desplegar:** addon → `C:\BrosLMV\bin` (BrosLMVClsMain.dll/.pdb). Host → `C:\BrosLMV\host\BrosLMV.Host.dll`.
  Python → `ctx.py`/runner a las **3** copias de `broslmv` (¡el host usa `C:\BrosLMV\host\workers\python\`!).
- **DLL bloqueada:** si Comercial está abierto, el addon DLL no se puede sobrescribir; cerrar
  Comercial y reabrir para tomar la versión nueva.
- **Software libre GPL-3.0.** Cualquier material de referencia de terceros usado durante el
  desarrollo se mantiene fuera del repositorio; nunca se copia código o propiedad intelectual
  ajena — solo se aprende de su comportamiento y se reimplementa desde cero.
- **SQL offline** para inspección: `sqlcmd -S ".\COMPAC2022" -E` (empresas: ComercialSP,
  Coctel_de_Ideas, Alma2020, ...).
