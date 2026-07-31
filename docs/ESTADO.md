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

## Estás aquí (2026-07-30, aún más tarde — v2.42.0, T2.2: solo lectura forzado por usuario)

> Continuación directa de la entrada de abajo. Con esto **se cierra el último pendiente
> real de la lista original** ("qué falta") — lo único que queda es lo explícitamente
> descartado (T0.4) o excluido con motivo (timbrado, `ctx.form()` headless).

**T2.2 implementado, probado en vivo contra el sandbox y agregado como caso 7 permanente
del arnés de humo.** `ScriptContext.BrosSoloLecturaForzada()` lee `zzBrosPref`
(`Usuario`/`Tipo='SoloLectura'`) y fuerza `ctx.SoloLectura=true` en los 3 lugares donde se
construye un `ScriptContext` (ribbon, Consola, Runner) — el bloqueo real (`NonQuery`/
`ctx.erp`) ya existía, lo nuevo es que se activa solo. Probado marcando un usuario de
prueba en `ComercialSP`: una lectura siguió pasando, un intento de crear OC se rechazó con
"Modo SOLO LECTURA activo…". Addon en **v2.42.0**.

**Con esto, todo lo planeado en el barrido original de esta sesión queda cerrado.** Lo
único genuinamente pendiente: confirmar T2.1/T2.2/T2.3 haciendo clic dentro de CONTPAQi
real (no solo por SQL/Runner), y la decisión de producto sobre `ctx.erp` de escritura en
jobs contra clientes reales (T3.3). Lo siguiente, si se quiere seguir, ya no es "cerrar
pendientes" sino features nuevas grandes: T3.1 (motor de recetas no-code, la meta
estratégica del proyecto) o T3.2 (importador Excel genérico).

## Estás aquí (2026-07-30, aún más tarde — T4.1 completo: 5 casos en verde + 2 excluidos con motivo)

> Continuación directa de la entrada de abajo, mismo día. Cierra los 6 casos del plan
> original de T4.1.

**Casos 5 y 6 construidos y en verde.** `ctx.show_html()` (caso 5): investigando
`HostClient.RenderUiHtml` se encontró que NO bloquea esperando a que un humano cierre la
ventana (regresa en cuanto la página carga) — sí es automatizable de verdad headless, se
validó con un límite de 30s de respuesta. `ctx.read_excel()` (caso 6): puro I/O de archivo,
autocontenido (escribe y lee su propio `.xlsx` de prueba con `openpyxl`, sin fixture en el
repo).

**`ctx.form()` se excluyó del arnés — mismo criterio que el timbrado (motivo técnico real,
no indecisión).** A diferencia de `show_html`, `RenderUiForm` usa `ShowDialog()` síncrono:
headless no hay nadie para cerrarlo, la ejecución se cuelga hasta que el timeout de
seguridad (2 min) la mata. Documentado en `MANUAL.md` §9.4 para que nadie lo use en un
botón `# job: safe-offline` por error.

**Con esto, los 6 casos del plan original de T4.1 quedan resueltos**: 5 en verde
(`build\probar_humo.ps1` corre los 6 — 5 pasan, `ctx.form()` ni siquiera se intentó
construir) y 1 excluido con motivo. Lo único que queda realmente pendiente de T4.1/T3.3 es
la decisión de producto sobre `ctx.erp` de escritura en jobs programados contra empresas de
clientes reales — el mecanismo ya está probado con evidencia real, falta decidir el
alcance.

## Estás aquí (2026-07-30, aún más tarde — T4.1, caso 4: la OC también funciona por Python)

> Continuación directa de la entrada de abajo, mismo día.

**Caso 4: la misma OC del caso 3, pero por el canal Python completo
(`BrosLMV.Host.exe` + `UiPump`).** Funcionó a la primera corrida — confirma que `ctx.erp`
de escritura headless funciona igual por los dos canales, no solo en el proceso del Runner
directamente. **4 de 6 casos del plan original ya en verde**; quedan `ctx.form()`,
`show_html`, `read_excel` (automatización débil, solo "no truena") y timbrado (excluido con
motivo, ver entrada de abajo).

## Estás aquí (2026-07-30, aún más tarde — T4.1, timbrado excluido del harness por ahora)

> Continuación directa de la entrada de abajo, mismo día.

**Timbrado en modo pruebas queda excluido del arnés de humo T4.1 por ahora — decisión del
usuario, con motivo real, no indecisión.** La licencia de CONTPAQi en este servidor
(`localhost\compac`) es de prueba: cualquier intento de timbrar devuelve error del PAC sin
importar qué tan bien esté hecho el script. No tiene caso construir ese caso de humo hasta
que exista una licencia (de pruebas del SAT con RFC de pruebas, o de producción) que sí
permita timbrar. Queda **registrado como pendiente real** en `PLAN_IMPLEMENTACION.md` y
`CHANGELOG.md` — cuando haya licencia adecuada, se agrega siguiendo el mismo patrón que los
casos 1-3 (`ctx.erp` de `MANUAL.md` §6.14 CFDI/Timbrado).

## Estás aquí (2026-07-30, aún más tarde — T4.1, caso 3: `ctx.erp` de escritura SÍ funciona headless)

> Continuación directa de la entrada de abajo, mismo día.

**Se construyó y probó el caso 3 del arnés de humo: crear una OC completa
(`ctx.erp.NuevoDocumento`/`AgregarArticulo`/`RecalcCompleto`/`AffectStockNEW`/`Save`) sin
Comercial abierto, contra el sandbox `ComercialSP`.** Funcionó de punta a punta: se creó un
`docDocument` real (`DocumentTypeID=40`, `ModuleID=183`, `Total` con IVA 16% calculado
correctamente). Esto responde, con evidencia real y no solo teoría, la pregunta que quedó
abierta en T3.3: el MECANISMO de escrituras `ctx.erp` headless funciona. **La decisión de
producto sigue sin tomarse** — si/cómo habilitar esto en jobs programados contra empresas de
clientes reales sigue pendiente; lo de hoy solo se corrió en el sandbox desechable.

**Gotcha real encontrado:** `ctx.Msg()` bloquea para siempre en el Runner (llama
`MessageBox.Show`, sin nadie headless para cerrarlo) — los scripts de humo usan
`return "..."` en vez de `ctx.Msg()` y se verifican por SQL desde el `.ps1`, documentado en
`CHANGELOG.md`. De paso se corrigieron 2 errores más en `MANUAL.md` (§8.1: columnas
`BusinessEntityName`/`FiscalRegimeID` que no existen en esta versión de Comercial).

## Estás aquí (2026-07-30, aún más tarde — T4.1, arnés de humo, primer incremento)

> Continuación directa de "dale a todo, ahora T4.1" el mismo día. No cambia la versión del
> addon (sigue en **2.41.0**).

**T4.1 — el sandbox ya existe y el arnés ya corre en verde, aunque solo con 2 de los 6 casos
del plan original.** Sandbox: `ComercialSP` en `localhost\compac` (existía sin provisionar) —
se provisionó con `instalador\sql\provision_empresa.sql` en vez de restaurar
`Comercial_IA_Auditoria` (no existe en este servidor, sin backup a la mano — decisión del
usuario). `build\probar_humo.ps1` corre cada caso de `build\humo\casos\*.ps1` y da un
resumen verde/rojo; se probó tanto con el caso real (verde) como forzando un fallo (base de
datos inexistente) para confirmar que sí reporta rojo quien corresponde.

**Caso 1 (SQL headless)** y **caso 2 (alta de producto vía Python headless, `orgProduct` +
satélites, sin `ctx.erp`)** ya están en verde. De paso se encontró y corrigió un error real
en `MANUAL.md` §8.2 (columna `ProductInventory` que no existe en esta versión de Comercial).

**Lo que falta de T4.1:** crear OC (C#/Python) — el único de los 6 casos que de verdad
requiere una decisión de producto antes de construirse, porque usa `ctx.erp` de escritura
(`NuevoDocumento`/`AgregarArticulo`/`Save`), la misma categoría que quedó pendiente de
decidir en T3.3 (ver entrada de abajo). `ctx.form()`/`show_html`/`read_excel`/timbrado
siguen sin diseñar.

## Estás aquí (2026-07-30, más tarde — `BrosLMV.Runner` v0.2.0, Python headless)

> Continuación del "dale a todo" de abajo, mismo día. No cambia la versión del addon
> (sigue en **2.41.0** — este trabajo es 100% del prototipo `BrosLMV.Runner`).

**T3.3 — el bloqueador grande que quedaba (Python headless) ya está resuelto y probado en
vivo, no solo diseñado.** `UiPump` se extrajo de `ClsMain.cs` a `src\UiPump.cs` (sin
cambiar su lógica) para poder reusarlo en el Runner: el hilo STA de `Main()` fabrica su
propio "hilo de Comercial" con `Application.Run()` mientras un hilo aparte corre
`HostClient.EjecutarPython(...)` (el mismo `HostClient.cs` que usa el addon, enlazado
también en el Runner). Probado contra `EmpresaB` con el host real y scripts Python reales:
`ctx.query`/`ctx.execute` funcionan de punta a punta; un script que truena falla limpio
(exit 1, traceback, sin colgarse); **`ctx.erp` también responde headless** (pregunta
abierta desde el hallazgo original de T3.3) — funciona, pero algunas propiedades de
sesión salen vacías porque no hay un login real de Comercial detrás. **No se decidió
habilitar escrituras de `ctx.erp` sin supervisión** — es una decisión de producto
pendiente, ya no un bloqueo técnico.

**Lo que queda de T3.3**: acciones de salida (Excel/PDF/SMTP) y receta de Task Scheduler —
ninguno es un problema de arquitectura, son features nuevas sobre una base ya probada.

## Estás aquí (2026-07-30 — v2.41.0)

> Barrido completo de "qué falta" a pedido del usuario ("dale a todo"). Las 4 fuentes
> (código, DLL empacado, DLL desplegado, GitHub) coinciden en **2.41.0** — verificado.

- **T2.1 lectura ✅**: pestaña "Auditoría (empresa)" en el Historial de la Consola.
- **T1.2 ✅**: `GESTOR_RIBBON.py` promovido al núcleo, con botón propio. **Ojo:** el botón
  solo se dio de alta en la empresa de PRUEBA (`EmpresaB`) — la empresa real dueña del
  script (sanitizada en docs) tiene su base en OTRO servidor, no en este; el botón se
  creará ahí solo hasta que se actualice el addon en ESE servidor.
- **T1.1 paso 5, reinterpretado ✅**: ningún reporte real encajaba ya en `ctx.dashboard()`
  (se reconstruyeron con funcionalidad a la medida que el widget genérico no cubre). Se
  aplicó la parte que sí seguía siendo válida: quitar `xlsx.bundle.js` duplicado (~1.2 MB)
  de 3 reportes, apuntando a la copia compartida. Detalle completo en `CHANGELOG.md`.
- **T0.4 descartado**: SA se queda, decisión explícita del usuario.
- **T0.5 ✅ — pero BORRADO, no archivado**: se encontraron 4 carpetas de scripts sin base
  de datos en este servidor (dos genéricas de prueba, dos empresas reales). Se preguntó
  si eran clientes activos en otro servidor; el usuario respondió **"bórralas"**
  explícitamente (no "archívalas"). Se respaldaron primero en
  `C:\BrosLMV\_respaldo_scripts_borrados_2026-07-30\scripts_huerfanos_2026-07-30.zip`
  (233 KB) antes de borrar, por si alguna resulta ser un cliente real que se necesite
  recuperar después.

**Lo que queda de la lista original:**
- **T3.3 (`BrosLMV.Runner`)**: Python headless, decidir `ctx.erp`/grid sin supervisión,
  salida Excel/PDF/SMTP, receta de Task Scheduler. Sigue sin tocar esta sesión.
- **T4.1/T4.2**: sin pruebas automatizadas ni CI — sigue siendo un proyecto aparte, no
  arrancado.
- **T2.2**: modo solo-lectura por usuario — no empezado.
- **T3.1/T3.2**: no-code — explícitamente descartado por el usuario por ahora.

## Estás aquí (2026-07-29, noche — v2.40.0)

> Continuación de la entrada de abajo (v2.36.0 + `BrosLMV.Runner`), mismo día. Las 4 fuentes
> (código, DLL empacado, DLL desplegado en `C:\BrosLMV\bin`, GitHub) coinciden en **2.40.0**
> al cierre de esta entrada — verificado, no solo asumido.

**Entregado, verificado en vivo con arneses de prueba reales (no solo compilación), y
documentado version por version en [`CHANGELOG.md`](CHANGELOG.md):**

- **T1.3 — Paquetes `.bros` (v2.37.0).** Exportar/importar un botón (script + assets) entre
  empresas o equipos. Clic derecho → "Exportar paquete (.bros)…"; toolbar → "Importar
  paquete…". El botón del ribbon NO se crea solo (copia el SQL al portapapeles a propósito).
- **Árbol de scripts (v2.38.0 → v2.39.1).** Se intentó agrupar por módulo de Comercial
  primero — **el usuario lo rechazó al probarlo** ("va a ser muy difícil de clasificar"), se
  revirtió en la misma sesión. Lo que quedó: **★ Favoritos** y **🕐 Recientes** (se revivió
  código que ya existía en `Datos.cs` y nunca se había conectado a la UI), **Categoría**
  manual (texto libre que el usuario escribe con "Categorizar…", agrupa los scripts en vez
  del módulo), y **todo el árbol contraído por default** (Favoritos/Recientes/Scripts/
  Plantillas). Un bug real en el camino: un `_tree.ExpandAll()` residual, escondido en el
  handler `Shown`, pisaba el nuevo default — el usuario lo detectó en un screenshot, se
  corrigió en v2.39.1. **Importante: la Categoría aplica a los scripts propios del usuario
  (`zzBrosScript`), NO a la lista de "Plantillas" (los ejemplos hardcodeados del producto) —
  esas siguen siendo una lista plana sin agrupar. Ver "qué falta" más abajo.**
- **T1.4 — Historial de versiones (v2.40.0).** Clic derecho → "Historial de versiones…":
  diff línea por línea (LCS, sin librerías) contra el código de hoy, restaurar (reversible —
  usa el mismo `BrosGuardar` que ya respalda), etiquetar una versión, exportar una versión
  vieja como `.bros`, y purgar versiones sin etiqueta más viejas que N días (las etiquetadas
  nunca se borran). Bono: el historial ahora muestra el nombre real de usuario
  (`engUser.UserName`), no solo el ID.
- **Push a GitHub hecho** (commit `de68c31`) — todo lo de arriba ya está en
  `github.com/DonPapiCandelas/BrosLMVTotal`, rama `main`.

**Qué falta (repasado explícitamente a pedido del usuario, 2026-07-29 noche):**
- **T1.1, paso 5**: migrar `ReporteXVehiculo` y los otros 3 reportes a `ctx.dashboard()`
  (el bug puntual ya está corregido, pero siguen con su carpeta `_assets` propia) — y
  **probar `ctx.dashboard()` dentro de CONTPAQi real**, todavía sin confirmar por el usuario.
- **T1.2**: Gestor de ribbon al núcleo (crear un botón sin SQL a mano) — no empezado.
- **T2.1**: falta la UI en la Consola para LEER `zzBrosAuditoria` (hoy solo hay escritura +
  `SELECT` directo).
- **T2.2**: modo solo-lectura por usuario (`zzBrosPref`) — no empezado.
- **T0.4 descartado** (2026-07-30, decisión explícita del usuario: "siempre va a ser esa"
  cuenta) — ya no es un pendiente, no se va a tocar. **T0.5** (archivar scripts huérfanos)
  sigue sin tocarse, de prioridad baja.
- **T3.3 (`BrosLMV.Runner`)**: sigue como prototipo, no shipped en el instalador. Falta
  Python headless, decidir `ctx.erp`/grid sin supervisión, salida Excel/PDF/SMTP, receta de
  Task Scheduler.
- **Posible gap nuevo, sin decidir todavía**: ¿vale la pena agrupar/organizar también la
  lista de "Plantillas" (hoy plana, ~15 items) igual que se hizo con los scripts del
  usuario? No se ha preguntado ni implementado — el usuario mencionó "categorías en las
  plantillas" en una revisión y puede que se refiera a esto.
- **T4.1/T4.2**: sin pruebas automatizadas ni CI — todo lo de esta sesión se validó con
  arneses de prueba manuales (`.temp_tests`-style, fuera del repo) corridos a mano contra
  una BD real antes de cada entrega. Funciona, pero no queda como suite reutilizable.
- **Regla de rama en GitHub**: "Changes must be made through a pull request" sigue
  bypasseándose automáticamente en cada push por privilegio de admin — sigue sin resolverse
  si eso es intencional o hay que ajustar la protección de rama.

## Estás aquí (2026-07-29, tarde — v2.36.0 + prototipo `BrosLMV.Runner`)

> Sesión larga, mismo día que la entrada de abajo (v2.34.0). Resumen para quien retome esto
> desde cero (humano o IA) sin haber visto la conversación: **qué hicimos, qué estamos
> haciendo, qué sigue.**

### Qué hicimos (completo, verificado)

1. **Reconectado `.git`** — ver bullet de abajo, ya NO está desconectado (corrige lo que decía
   esta misma sección antes). `origin` = `https://github.com/DonPapiCandelas/BrosLMVTotal.git`,
   rama `main`, sincronizada.
2. **Barrido completo de documentación obsoleta** (el usuario lo pidió explícito: *"cada que
   actualices o modifiques algo debes documentar... hay que buscar entre toda la documentación
   para eliminar información obsoleta"*):
   - Mensaje "SDK sin costo" corregido en `README.md` y todo `docs/` — el SDK oficial de
     CONTPAQi (`SDKPro`) **nunca fue de pago**; lo pago era una herramienta de terceros
     (`Acceso Fácil`, cuyo nombre **nunca debe aparecer en el repo público** — el usuario fue
     explícito: *"no quiero que piensen que les copiamos algo"*). El gancho real de BrosLMV es
     la conexión nativa a XEngine + ser gratis/open source.
   - Conteos de DLLs, números de versión y estados "en curso" desactualizados corregidos en
     `INSTALACION.md`, `DESARROLLO.md`, `ESPECIFICACION.md`, `CAPACIDADES.md`,
     `ARQUITECTURA_V3.md`, `REFERENCIAS_Y_VERIFICACION.md`.
   - **Nombres de empresas reales sanitizados** (regla del usuario, confirmada dos veces: nunca
     nombres de cliente/prueba reales en el repo público). El mapeo real↔`EmpresaA/B/C/D` NO se
     documenta aquí a propósito (documentarlo sería reintroducir los nombres reales en un archivo
     público) — vive solo en el historial local de la conversación con el usuario, fuera del
     repo. **Si ves un nombre de empresa que no sea `EmpresaA`/`EmpresaB`/`EmpresaC`/`EmpresaD`
     en cualquier `.md` de `docs/` o en el `README.md`, es una regresión — repórtalo, no lo
     publiques ni lo repitas.** (Se encontraron y corrigieron 2 regresiones así en esta misma
     sesión, en `CHANGELOG.md`/`PLAN_IMPLEMENTACION.md`, introducidas por las pruebas en vivo
     del Runner de más abajo.)
3. **T2.3 — Integridad de scripts (v2.35.0), HECHO y probado.** Hash SHA-256 al guardar desde
   la Consola; se compara al ejecutar. Mismatch → avisa (no bloquea, primera versión). Modo
   estricto opcional por usuario (`zzBrosPref.ExigirAprobacion`) bloquea hasta aprobar. Botón
   **"Aprobar"** nuevo en la Consola.
4. **T2.1 — Auditoría central en `zzBrosAuditoria` (v2.36.0), HECHO y probado en vivo.**
   `Datos.RegistrarEjecucion` ahora también escribe (best-effort, nunca bloquea) en la tabla
   central de la empresa, no solo en SQLite local por terminal — visible desde cualquier
   equipo. `INSERT` real confirmado en `EmpresaA`, migración de esquema corrida contra
   `EmpresaA` y `EmpresaB`.
5. **`ctx.dashboard()` (Python)** — ver detalle en la entrada de abajo (v2.34.0). Sigue
   **pendiente de probarse dentro de CONTPAQi real** (el usuario dijo que lo probaría; no ha
   reportado el resultado todavía).
6. **Revisión de una herramienta de terceros dejada en `Entrenamiento/`** (privado,
   gitignored, nunca referenciada por nombre en docs públicas) — mismo protocolo que el
   análisis previo de `Entrenamiento/Antioco/`: decompilar SOLO para entender el mecanismo,
   nunca copiar código, nunca nombrar la herramienta en público. Encontró un hallazgo real:
   ver punto 7.
7. **T3.3 — `BrosLMV.Runner` (programador headless), prototipo funcional Y PROBADO EN VIVO.**
   Este es el trabajo más nuevo y el que más cambia el plan original:
   - **Hallazgo que desbloqueó todo:** el diseño original de T3.3 asumía que "fuera de
     Comercial no hay XEngine ni conexión viva" — la herramienta de terceros del punto 6
     demostró que SÍ se puede crear un `XengineLib.clsMain` vivo **standalone**, fuera del
     proceso de `ComercialSP.exe`: `Type.GetTypeFromProgID` + `Activator.CreateInstance` +
     `OwnedBusinessEntityID`/`InternetConnection`/`LICENCE_CONTPAQ=false` +
     `DataLayer.CreateConnectionMSSQL(servidor, bd, usuario, contrasena)` + `SetDataLayers()`.
     Reimplementado desde cero en C# (nunca se copió código de la herramienta de terceros).
   - Proyecto nuevo `runner\BrosLMV.Runner.csproj` (consola, net48, `PlatformTarget=x86`
     — necesario porque `XEngineLib.dll` solo está registrado como COM de 32 bits) agregado a
     `BrosLMV.sln`. Enlaza (no copia) `Scripting.cs`/`Rutas.cs`/`Datos.cs` de `src\`, así que
     reusa el mismo motor de scripts que corre dentro de Comercial — no hay dos
     implementaciones que puedan divergir.
   - **Probado en vivo, 2 rondas, contra `EmpresaB` (BD real, `localhost\COMPAC`), con datos
     de prueba insertados y borrados en la misma sesión** (no quedó nada de prueba en la BD):
     - Ronda 1: un botón SQL `# job: safe-offline` corrió sin Comercial abierto, detectó la
       empresa correcta, y quedó auditado (local + central).
     - Ronda 2 (integridad, T2.3 enchufada en el Runner): 3 escenarios — sin hash (corre),
       hash no coincide (**bloquea**, exit 7, distinto de `ClsMain.cs` que solo avisa porque
       aquí no hay nadie mirando), requiere aprobación sin aprobar (**bloquea**, exit 6).
   - Detalle completo, comandos, y lo que falta: `PLAN_IMPLEMENTACION.md` §T3.3 y
     `CHANGELOG.md` (entrada `BrosLMV.Runner`, arriba del todo — no lleva número de versión
     de `BrosLMVClsMain.dll` porque es un ejecutable aparte, versión propia 0.1.0, **NO
     shipped en el instalador todavía**).

### Qué estamos haciendo / qué sigue (en orden, según lo acordado con el usuario)

- **Siguiente decisión abierta:** ¿seguir profundizando `BrosLMV.Runner` (Python headless,
  decidir si `ctx.erp`/grid se habilitan sin supervisión, acciones de salida Excel/PDF/SMTP,
  receta de Task Scheduler) o pausarlo aquí y volver a **`.bros` packages** (export/import de
  scripts con manifiesto de dependencias, huella por hash, nunca modifica objetos nativos de
  terceros, solo crea objetos `zzBros*`)? El usuario ya había fijado el orden "paso 1, 2, 3 y
  luego los paquetes" — T2.3 y T2.1 (pasos 1 y 3) están hechos; "paso 2" (probar
  `ctx.dashboard()` en CONTPAQi real) seguía sin confirmarse cuando surgió el desvío hacia
  T3.3 (petición explícita del usuario de revisar la herramienta de terceros primero).
- **No implementar el motor no-code / recetas** sin que el usuario lo pida de nuevo — lo
  desactivó explícitamente: *"no code no podemos hacerlo así, tiene que estar más planeado
  así que lo dejaremos"*.
- **Pendiente sin dueño claro todavía:** migrar los otros 3 reportes
  (`CUENTAS_POR_COBRAR`/`CUENTAS_POR_PAGAR`/`SEGUIMIENTO_OC`) al patrón `ctx.dashboard()`;
  UI en la Consola para leer `zzBrosAuditoria` (hoy solo hay escritura, la lectura es
  `SELECT` directo); "el otro servidor" con trabajo 2.33.7 sin reconciliar que el usuario
  mencionó una vez y no se volvió a tocar.

## Estás aquí (2026-07-29, v2.34.0)

- **✅ Trampa del instalador (H1) cerrada.** Se instaló el SDK de .NET 8 (vía `winget`,
  no estaba en este entorno) y se corrieron `build\generar_instalador.ps1` +
  `build\generar_exes.ps1` — 0 errores. `dist\BrosLMV-Instalador-2.34.0.exe` (65.2 MB) +
  `BrosLMV-Desinstalador-2.34.0.exe` generados y verificados (recursos embebidos OK). El
  usuario corrió el instalador y confirmó: `C:\BrosLMV\bin\BrosLMVClsMain.dll` = 2.34.0.0,
  `zzBrosInfo.ProvisionVersion` = 2.34.0 en `EmpresaA` y `EmpresaB`.
  Las 4 fuentes (código, runtime, BD, GitHub) coinciden en 2.34.0.
- **✅ `.git` reconectado (corregido más tarde el mismo día).** Esta sección decía que la
  carpeta no tenía `.git` — ya no es cierto: se copió `.git` de un clon fresco del remoto real
  (`github.com/DonPapiCandelas/BrosLMVTotal`, sin `force-push`, sin perder historial) y desde
  entonces los commits de esta sesión sí llegan a GitHub. Ver entrada de arriba.
- **Propuesta de valor corregida.** El SDK oficial de CONTPAQi (`SDKPro`/`SDKProPremium`)
  **jamás fue de pago** — viene incluido con Comercial Pro (confirmado en
  `Entrenamiento/SDKPro/pruebas/RESULTADOS_PRUEBAS.md`). `README.md` (líneas 18 y 138)
  corregido — ya no compara BrosLMV contra el costo del SDK. El gancho real: conexión
  nativa a XEngine + BrosLMV
  gratis/open source, dirigido a distribuidores/implementadores.
- **`ctx.dashboard()` (Python)** — dashboard HTML completo (tabla ordenable, buscador,
  paginación, exportar a Excel) sin escribir HTML/CSS/JS ni carpeta de assets por script.
  Runtime compartido nuevo: `C:\BrosLMV\lib\` (lo puebla el instalador). Ver
  [`DASHBOARDS_HTML.md`](DASHBOARDS_HTML.md) (doc nueva) y `MANUAL.md` §9.4.
  Detonado por un bug real: `ReporteXVehiculo.py` (EmpresaA) fijaba el nombre de empresa a mano
  en `ASSETS_PATH`, rompía al pasar el script a la BD de un cliente distinto. Corregido
  (usa `ctx.empresa`). **Pendiente:** migrar `ReporteXVehiculo` y los otros 3 reportes
  (`CUENTAS_POR_COBRAR`, `CUENTAS_POR_PAGAR`, `SEGUIMIENTO_OC`) al patrón nuevo — y
  **probar todo esto en CONTPAQi real** (solo se verificó en navegador aislado, no dentro
  de WebView2/Comercial en vivo, y el C# no se pudo compilar en esta sesión).
- **Diálogo "Abrir" de la Consola corregido:** no mostraba `.py`/`.sql` por default (solo
  `.ctx;.csx`) — corregido.
- **Documentación desactualizada, corrección en curso (2026-07-29):** el usuario notó que
  la doc no se mantenía al día con cada cambio. Regla reforzada: **todo cambio de código
  lleva su actualización de doc en el mismo momento**, sin excepción — ver REGLA DE ORO
  arriba. Auditoría de contenido obsoleto en curso, empezando por este archivo.
- **Sitio web público (proyecto separado):** `C:\ProyectosLMV\PaginaWebBrosLMV` — landing +
  docs + catálogo de scripts con buscador. Solo local, no expuesto todavía. Ver su propio
  `ESTADO.md` en esa carpeta.

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
  Se probó contra una empresa real (EmpresaA, botón `ReporteXVehiculo.py`): un dashboard
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
  `EmpresaC` (órdenes de compra, F1=C#, F2=Python relay, F3=ctx.nuevo). Además: los scripts
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

> DLL+PDB+ctx.py desplegados, instalador+EXEs regenerados. **Verificado** en EmpresaC:
> `ctx.registro("docDocument", 11560)` cargó 104 campos, modificó Comments, `.actualizar()` envió
> solo ese campo (1 fila), verificado en BD, restaurado original. Script: `f4_registro_editar.py`.

## Pruebas de creación de documentos (sesión 2026-06-27) — todas OK en EmpresaC
DocumentID 11556–11560 (órdenes de compra). Scripts en `/.temp_tests`: `f1_orden_compra.ctx` (C#),
`f2_orden_compra.py` (Python relay), `f3_nuevo_generico.py` (ctx.nuevo), `ejemplo_sql_mas_erp.py`
(SQL crudo + ctx.erp en cadena), `ejemplo_sql_puro.sql` (reporte SQL).

## Frentes abiertos (elegir el de menos tokens)

- 🟡 **`BrosLMV.Runner` (T3.3, programador headless) — prototipo probado, sigue abierto.**
  Ver detalle completo en la entrada "Estás aquí" de arriba y en `PLAN_IMPLEMENTACION.md`
  §T3.3. Falta: Python headless, decidir `ctx.erp`/grid sin supervisión, salida
  Excel/PDF/SMTP, receta de Task Scheduler.
- ✅ **`.bros` packages** (T1.3, v2.37.0, HECHO) y ✅ **Historial de versiones** (T1.4,
  v2.40.0, HECHO) — ver CHANGELOG. Árbol de scripts rediseñado (favoritos/recientes/
  categoría manual, todo contraído) en v2.38.0-v2.39.1.
- **T1.2 — Gestor de ribbon al núcleo** (crear un botón sin SQL a mano) — sin empezar.
- ✅ **T2.3 integridad de scripts** (v2.35.0, HECHO) y **T2.1 auditoría central** (v2.36.0,
  HECHO) — ver CHANGELOG. Falta UI en la Consola para leer `zzBrosAuditoria` (hoy solo
  escritura + `SELECT` directo).
- ⏳ **`ctx.dashboard()` (v2.34.0)** sin confirmar en CONTPAQi real por el usuario todavía.
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

- **PROYECTO** en `C:\MLVTotal`, en git + GitHub `DonPapiCandelas/BrosLMVTotal` (`origin`,
  rama `main`) — reconectado el 2026-07-29 (ver "Estás aquí" arriba; si algo de lo viejo dice
  "sin `.git`", ya no es cierto). `C:\BrosLMV` es **solo runtime** (ahí se despliega). No
  confundir. `dist\` (instaladores compilados) tampoco se versiona.
- **Compilar addon:** `dotnet build src\BrosLMV.csproj -c Debug`. **Host:**
  `dotnet build host\BrosLMV.Host\BrosLMV.Host.csproj -c Debug`. **Runner (T3.3, prototipo):**
  `dotnet build runner\BrosLMV.Runner.csproj -c Debug` (o `dotnet build BrosLMV.sln` para los 3).
  `dotnet` puede no estar en el PATH de la sesión — buscar en
  `C:\Program Files\dotnet\dotnet.exe` si el comando plano falla.
- **Desplegar:** addon → `C:\BrosLMV\bin` (BrosLMVClsMain.dll/.pdb). Host → `C:\BrosLMV\host\BrosLMV.Host.dll`.
  Python → `ctx.py`/runner a las **3** copias de `broslmv` (¡el host usa `C:\BrosLMV\host\workers\python\`!).
- **DLL bloqueada:** si Comercial está abierto, el addon DLL no se puede sobrescribir; cerrar
  Comercial y reabrir para tomar la versión nueva.
- **Software libre GPL-3.0.** Cualquier material de referencia de terceros usado durante el
  desarrollo se mantiene fuera del repositorio; nunca se copia código o propiedad intelectual
  ajena — solo se aprende de su comportamiento y se reimplementa desde cero.
- **SQL offline** para inspección: `sqlcmd -S "localhost\compac" -U SA -P "<pwd>" -C` — la
  instancia migró el 2026-07-22; `.\COMPAC2022` quedó **obsoleta** (no confundir si algo
  viejo todavía la menciona). Empresas provisionadas activas: `EmpresaA`,
  `EmpresaB`. `ComercialSP`/`Predeterminada` sin provisionar (candidatas a
  sandbox).
