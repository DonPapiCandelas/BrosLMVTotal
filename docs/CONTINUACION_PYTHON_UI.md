# Continuacion: UI real en Python (pythonnet -> pywebview) + fix de timeout

> Sesion: 2026-06-30. Rama base: `main` @ `fd19aa2` (antes de esta sesion). Este documento
> es autocontenido a proposito: en paralelo habia trabajo en curso sobre
> `Scripting.cs`, `ClsMain.cs`, `Consola.cs`, `docs/*.md`, `notas_version.html`
> (ver seccion "Estado del working tree" abajo). No se toco nada de eso ni se commited
> nada todavia. Este archivo documenta SOLO el trabajo de esta sesion para que se
> pueda retomar despues sin tener que re-derivarlo de cero.

## Objetivo de la sesion

El usuario queria que los scripts Python de BrosLMV pudieran tener UI propia, con diseno
libre y estetico ("lo mejor, sin cosas raras"), en vez de `ctx.form(spec_json)` (el
renderer generico de WinForms del addon, que se veia feo). Se evaluaron dos caminos y se
eligio uno.

## Decision final: pywebview (HTML/CSS), no WinForms

**Camino descartado: pythonnet + WinForms puro.**
Se probo que un script Python (CPython normal, fuera de proceso) puede crear objetos
`System.Windows.Forms` reales via `pythonnet` (el modulo `clr` para CPython, distinto de
IronPython). Funciono, incluyendo el roundtrip sincrono de `ctx.erp.*` dentro de un click
handler mientras la ventana seguia viva (hilo STA + bomba de mensajes). Se llego a
reescribir `PLANTILLA_REQUISICION_COMPRA_PY.py` con esto y paso pruebas de humo. **Se
descarto por decision explicita del usuario** ("necesito lo mejor... ya no necesito
windows forms") porque construir algo esteticamente bueno en WinForms exige posicionar
`Point`/`Size` a mano control por control (tedioso, dificil que se vea moderno) y ademas
el azucar sintactico de IronPython (`Label(Text=..., Parent=self)`) **no existe en
pythonnet** (hubo que crear helpers `_lbl`/`_btn` para lograr controles).

**Camino elegido: pywebview (ventana nativa + HTML/CSS/JS real).**
`pywebview` es una libreria Python madura que crea una ventana nativa con un navegador
embebido (en Windows usa WebView2/Edge Chromium, que ya viene instalado en Windows 10/11).
La UI se escribe en HTML/CSS moderno (flexbox, sombras, gradientes, tipografia — sin
posicionar pixeles), y la comunicacion Python<->JS es `window.pywebview.api.*` (expone
metodos Python que JS llama como funciones async). El SDK nativo (`ctx.erp.*`) no cambia
en nada: la UI solo captura datos, la creacion del documento sigue siendo el mismo patron
de siempre (`NuevoDocumento` -> `AgregarArticulo` -> `RecalcCompleto` -> `Save`).

## Cambios concretos hechos esta sesion

### 1. Fix de timeout (bug real, no relacionado a UI pero descubierto por ella)
Una UI interactiva necesita mas de los 120s que el host mata por default. Se encontro que
`HostClient.EjecutarPython(..., timeoutMs)` **nunca mandaba `timeoutMs` al host** (el
campo `ExecuteScript.TimeoutMs` del proto se quedaba en 0 siempre). Arreglado:

- `src/HostClient.cs`: nuevo `HostClient.TimeoutMsFromHeader(codigo, defaultMs=120000)` —
  lee una directiva opcional `# timeout: 1800` (segundos) en las primeras lineas del
  script. Si no esta, sigue siendo 120s (no se debilito el limite para scripts normales).
  `Intercambio(...)` ahora recibe `timeoutMs` y lo pone en `ExecuteScript.TimeoutMs`
  (antes se perdia).
- `src/ClsMain.cs` y `src/Consola.cs`: ambos puntos de llamada a `EjecutarPython` ahora
  hacen `int timeoutMs = HostClient.TimeoutMsFromHeader(codigo);` antes de invocar.
- Compilado y verificado: `dotnet build src/BrosLMV.csproj -c Debug` -> 0 errores, 0
  warnings.
- **Uso:** cualquier script Python con UI interactiva debe llevar `# timeout: 1800` (o el
  valor que necesite) en las primeras lineas, junto a `# lang: python`.

### 2. Runtime embebido: pythonnet + pywebview instalados de fabrica
`build/descargar_python.ps1` (el script que descarga y prepara el CPython 3.13
embeddable que se distribuye en `instalador/runtimes/python`) ahora, ademas de lo que ya
hacia, automaticamente:

1. Habilita `Lib\site-packages` en el `._pth` (antes solo tenia `import site`).
2. Bootstrapea `pip` (descarga `get-pip.py` de `bootstrap.pypa.io`, cacheado en
   `.temp_tests\downloads` igual que el ZIP de Python).
3. Instala `pythonnet` (se dejo instalado aunque el camino final sea pywebview, porque
   pywebview lo usa como dependencia en Windows, y por si algun script quisiera WinForms
   nativo puntual).
4. Instala `setuptools` + `wheel` (necesario porque una dependencia chiquita de
   pywebview, `proxy_tools`, solo trae `.tar.gz` sin wheel — sin esto pip no la puede
   construir en el runtime embebido).
5. Instala `pywebview`.
6. Corre pruebas de humo de cada paso (`import clr; ...Form...` y `import webview`) que
   **rompen el build (`throw`) si algo falla**.

Verificado corriendo `powershell -File build\descargar_python.ps1 -Force` (regenera todo
desde cero, incluye descarga fresca del ZIP de Python) — salio limpio de punta a punta,
dos veces (antes y despues de agregar pywebview).

**Nota:** `instalador/runtimes/python` esta en `.gitignore` (linea 30) — nunca se
commitea. Lo unico persistente es `build/descargar_python.ps1`. Si alguien clona el repo
y corre ese script, el runtime sale ya listo con pythonnet + pywebview.

### 3. Script de ejemplo reescrito: `instalador/scripts/PLANTILLA_REQUISICION_COMPRA_PY.py`
Version final en pywebview. Estructura:

- Cabecera `# lang: python` + `# timeout: 1800`.
- Funciones puras de negocio (sin UI): `buscar_proveedores`, `buscar_almacenes`,
  `buscar_productos`, `asegurar_producto_proveedor`, `crear_requisicion_doc` — estas
  hacen exactamente lo mismo que la version C# ya validada de solicitud de compra
  (modulo 1040): `NuevoDocumento(1040, depot, proveedorBE)` -> UPDATE de perfil
  (DepotIDFrom=0, UserID=0, CampaignID/CostCenterID/ProjectID=NULL, PaymentTermID=0) ->
  `AgregarArticulo` por partida -> `orgProductSupplier` -> `RecalcCompleto` -> `Save`.
  Sin `AffectStockNEW` (una solicitud de compra no afecta inventario).
- Clase `Api` expuesta a JS (`window.pywebview.api.*`): `buscar_proveedores`,
  `buscar_almacenes`, `buscar_productos`, `crear_requisicion`, `cerrar`.
- HTML/CSS/JS embebido en un string (`HTML`): header con gradiente verde, tarjetas para
  proveedor/almacen/partidas, autocompletado simple con lista de resultados clicables,
  tabla de partidas con boton "Quitar" por fila, banner de mensajes (no `alert()`), y
  `confirm()` nativo antes de crear el documento.
- Arranque: `webview.create_window(...)` + `webview.start()` — **no necesita el hilo STA
  manual que si necesitaba la version WinForms**; pywebview maneja su propio threading
  internamente.

### 4. Verificacion hecha (que se probo y como)
Todo esto se probo con un `ctx` simulado (`sys.modules["broslmv"] = FakeCtx`) porque no
hay conexion al host real fuera de Comercial:

- **Bridge minimo**: boton HTML -> `window.pywebview.api.*` -> roundtrip sincrono
  stdin/stdout (el mismo patron de `_bridge.StdioBridge`/`runner.py`) -> funciono limpio.
- **Logica de negocio pura**: se llamo `Api().crear_requisicion(6, 5, [...])`
  directamente (sin ventana) y goleo exactamente `NuevoDocumento(1040,5,6)` ->
  `AgregarArticulo(777,3,4.0)` -> `orgProductSupplier` -> `RecalcCompleto` -> `Save`, con
  `product_id` normalizado de float (como llega de JS) a int correctamente.
- **Apertura de ventana con el HTML/CSS real completo**: aqui aparecio un problema (ver
  siguiente seccion) — no se pudo confirmar limpio en esta sandbox.

## Problema abierto: pywebview inestable en ESTA sandbox (no confirmado en maquina real)

Al abrir la ventana con el HTML/CSS grande (980x800, con `position: fixed`, tablas,
scrolling) aparece un log masivo de pywebview:

```
[pywebview] Error while processing window.native.AccessibilityObject...
   ... "CoreWebView2Controller members can only be accessed from the UI thread" ...
   maximum recursion depth exceeded
```

**Se aislo exhaustivamente:**
- Ocurre incluso SIN ninguna automatizacion JS mia — solo abriendo la pagina real y
  esperando (ver `bisect3.py` del historial de chat, ya no existe en disco, era un
  script temporal de prueba).
- No ocurre con una pagina HTML minima (un boton, poco CSS) — esa se probo limpia varias
  veces.
- Se probo bajar pywebview de 6.2.1 a 5.4: aparecio un bug DISTINTO (`Rectangle` /
  marshalling), no el mismo. Osea, hay fragilidad en mas de una version.
- Esta VM de prueba corre sobre una **GPU virtual QXL** (`wmic path win32_videocontroller`
  -> `Red Hat QXL controller`), tipica de entornos KVM/QEMU sin aceleracion real — un
  entorno donde WebView2 y las APIs de accesibilidad de Windows son conocidas por
  comportarse mal.

**Hipotesis de trabajo (no confirmada):** el bug es un artefacto de esta sandbox
virtualizada (GPU sin aceleracion), no un defecto real de pywebview ni del script. La
maquina real donde corre Comercial (la del usuario) tiene GPU normal.

**Que hacer al retomar esto:**
1. Correr `PLANTILLA_REQUISICION_COMPRA_PY.py` desde la consola de BrosLMV dentro de
   Comercial, en la maquina real del usuario (no en esta sandbox).
2. Si abre limpio: pywebview queda confirmado como la via definitiva, cerrar este
   pendiente.
3. Si tambien falla ahi: es un problema real de pywebview/pythonnet en este stack, no de
   la sandbox. En ese caso, opciones a evaluar (no descartadas del todo):
   - Actualizar a una version de WebView2 Runtime mas reciente en la maquina real.
   - Investigar si el problema es especifico de ventanas grandes/CSS complejo (probar
     reduciendo tamano de ventana o simplificando el CSS por partes).
   - Revisar si hay un fix/issue conocido en el repo de pywebview para
     "CoreWebView2Controller... UI thread" (no se busco en GitHub por falta de acceso a
     internet amplio en esta sesion mas alla de pip).
   - Como ultimo recurso, reconsiderar pythonnet+WinForms (ya probado que SI funciona
     limpio en esta misma sandbox) — quedaria como Plan B documentado, no borrado del
     historial de decisiones aunque el archivo ya no lo use.

## Estado del working tree del addon (`C:\MLVTotal`) al cierre de esta sesion

**No se hizo ningun commit.** Motivo: al momento de escribir esto, `git status` muestra
modificados por OTRA sesion concurrente (no esta): `docs/ARQUITECTURA_V3.md`,
`docs/CHANGELOG.md`, `docs/ESPECIFICACION.md`, `docs/ESTADO.md`, `docs/INDICE.md`,
`docs/MANUAL.md`, `docs/PYTHON.md`, `docs/RECETAS_NOCODE.md`,
`docs/REFERENCIAS_Y_VERIFICACION.md`, `docs/SCRIPTING_CONTRATOS.md`,
`host/BrosLMV.Host/Callbacks/IHostCallbackSink.cs`,
`host/BrosLMV.Host/Handlers/PythonExecutionHandler.cs`,
`host/BrosLMV.Host/Workers/IAddonChannel.cs`, `src/assets/notas_version.html`,
`workers/python/broslmv/ctx.py` (el `ctx.form`/`RenderUiForm` de WinForms declarativo, ver
sesion anterior), y archivos nuevos sin trackear: `docs/REQUISICION_SOLICITUD_COMPRA.md`,
`host/BrosLMV.Host/Callbacks/RelayingCallbackSink.cs`,
`instalador/scripts/PLANTILLA_REQUISICION_COMPRA.ctx`, `instalador/scripts/REQUISICION.ctx`,
`instalador/scripts/SOLICITUD_COMPRA.ctx`.

Esa otra sesion construyo, en paralelo, una **plantilla de requisicion distinta en C#
puro con WinForms** (`PLANTILLA_REQUISICION_COMPRA.ctx`) y la registro en el arreglo
`PLANTILLAS` de `src/Consola.cs` para que aparezca en la seccion "Plantillas" de la
consola (ver `docs/REQUISICION_SOLICITUD_COMPRA.md` para el detalle completo de ese
trabajo). Es un ejemplo paralelo al mio, no conflictivo, pero **mi
`PLANTILLA_REQUISICION_COMPRA_PY.py` NO esta registrado en `PLANTILLAS`** todavia — si se
quiere que aparezca en la lista de la consola igual que el C#, hay que agregarlo ahi
(y probablemente copiarlo a `C:\BrosLMV\scripts\` para pruebas en vivo, igual que se hizo
con el .ctx segun ese documento).

**Archivos tocados por ESTA sesion (los que hay que commitear cuando se consolide todo):**
- `build/descargar_python.ps1` (pip + pythonnet + setuptools/wheel + pywebview)
- `src/HostClient.cs`, `src/ClsMain.cs`, `src/Consola.cs` (fix de timeout). **Ya se
  verifico con `git diff` que el cambio en `ClsMain.cs`/`Consola.cs` es minimo y aislado**
  (solo agrega `int timeoutMs = HostClient.TimeoutMsFromHeader(codigo);` antes de la
  llamada a `EjecutarPython` en los 2 puntos de invocacion) — no pisa nada de la sesion
  anterior que toco esos mismos archivos para otras cosas (anclas de documento, etc.).
  `src/Scripting.cs` **no** se toco en esta sesion (solo en la sesion anterior).
- `instalador/scripts/PLANTILLA_REQUISICION_COMPRA_PY.py` (nuevo/reescrito, no trackeado
  todavia por git al momento de este documento)
- `docs/CONTINUACION_PYTHON_UI.md` (este archivo)

## Siguiente paso recomendado

1. Probar `PLANTILLA_REQUISICION_COMPRA_PY.py` en Comercial real (ver seccion de arriba).
2. Una vez resuelto eso, hacer `git diff` cuidadoso de `src/HostClient.cs`,
   `src/ClsMain.cs`, `src/Consola.cs` para separar el fix de timeout (esta sesion) de
   cualquier cambio de la sesion concurrente antes de commitear.
3. Decidir si `PLANTILLA_REQUISICION_COMPRA_PY.py` se registra en `PLANTILLAS`
   (`src/Consola.cs`) igual que la version C#, o si los scripts Python se listan por otro
   mecanismo.
4. Bump de version + entrada en `docs/CHANGELOG.md` / `notas_version.html` cuando se
   consolide (coordinar con lo que ya haya puesto ahi la otra sesion).
