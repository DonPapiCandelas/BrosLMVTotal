# BrosLMV — Historial de cambios (CHANGELOG)

Registro de versiones del producto. **Cada cambio al programa debe anotarse aquí**
junto con la actualización de la documentación correspondiente.

Formato: cada versión lista lo **Agregado**, **Cambiado**, **Corregido** o
**Quitado**. La versión va también en `AssemblyVersion` (en `src\ClsMain.cs`).

> Versiones 2.80.0 y anteriores: [`CHANGELOG_ARCHIVO.md`](archivo/CHANGELOG_ARCHIVO.md).

## [2.100.0] — 2026-10-02 — Plantillas de fábrica: trazabilidad

### Agregado
- **Plantilla «Trazabilidad del documento»** (`instalador/scripts/TRAZABILIDAD_DOCUMENTO.ctx`, documentación en `docs/TRAZABILIDAD_DOCUMENTO.md`): muestra de dónde viene y a dónde fue cualquier documento, en las dos direcciones
  y a través de todos los pasos (Solicitud → Orden de compra → Recepción → Factura → Pago; Cotización → Pedido → Remisión → Factura → Cobro), incluidos los módulos clonados (se clasifica por `ModuleIDBase`).
  Junta las **cuatro** formas en que se ligan los documentos: `SourceDocumentID` y `DestinationDocumentID` del encabezado, `SourceDocumentItemID`/`SourceDocumentID` de la partida y `DeliverDocumentItemID`.
  Si un vínculo existe **solo** por `DestinationDocumentID` (el caso de «varias órdenes → una factura», que el sistema no puede expresar con un único `SourceDocumentID`) lo dibuja discontinuo y lo marca como manual.
  Muestra cantidades por partida, pagos/cobros aplicados, saldo y documentos cancelados; clic centra, doble clic abre el documento, exporta a CSV. Solo lee.
- **Prueba de humo #35** (`build/humo/casos/35_trazabilidad_documento.ps1`): corre la plantilla real con `BrosLMV.Runner` contra el sandbox y comprueba la cadena, la raíz, la evidencia y el documento aislado.

### Cambiado
- **Las plantillas ya no se escriben a mano en `Consola.cs`**: una plantilla de fábrica es cualquier script de la carpeta `scripts` cuya cabecera diga `// Plantilla: <nombre>` (en Python `# Plantilla:`, en SQL `-- Plantilla:`),
  con `Categoria:` (carpeta del árbol) y `Documentacion:` opcionales. Agregar una plantilla ya no exige tocar el código del addon.
- **Instaladores** (`Instalar.ps1` y el `.exe`): refrescan en cada instalación todas las plantillas que traen ese marcador. `generar_instalador.ps1` convierte la documentación de cada plantilla desde `docs/<nombre>.md`.
- **Manual (§10.5):** `DestinationDocumentID` **sí se usa**. Medido en dos empresas reales: 97 y 221 documentos la traen, y seis vistas nativas la leen (globalización de ventas y «asignar factura de compra a orden de compra»).
  La nota anterior («no se usa») se corrigió.

## [2.98.0] — 2026-09-29 — Manual y catálogo del SDK

### Agregado
- **Catálogo del SDK como única fuente** (`src/assets/sdk_catalogo.json`, incrustado en la DLL): 170 funciones públicas de `ctx.*` y `ctx.erp.*` (C#), del SDK de Python y de SQL/tokens,
  cada una con firma, descripción y ejemplo; **41 con ficha completa** (parámetros, qué devuelve, ejemplos en C# y Python, avisos y funciones relacionadas): consultas (`Scalar`, `Query`, `NonQuery`,
  `OpenConn`), `NuevoDocumento`, `AgregarArticulo`, `RecalcCompleto`, `Save`, `AffectStockNEW`, `RefreshGrid`, `AbrirDocumento`, `Timbrar`, `ShowHtml`, tokens y más. Se agregaron las 20 funciones
  públicas que no estaban documentadas (`OpenConn`, `ShowHtml`, `ShowHtmlFormulario`, `AbrirDocumento`, `Timbrar`, `RelacionarCFDI`…).
- **Manual del SDK** (HTML formal con buscador, filtro por lenguaje, guías y fichas): *Más opciones → Manual del SDK…*; también `docs/SDK_REFERENCIA.md` para leerlo en GitHub.
- **Guías** (`docs/SDK_GUIAS.md`): primer script en C#/Python/SQL, `Scalar`/`Query`/`NonQuery`/`OpenConn`, crear un documento paso a paso (con inventario, costos, saldos y póliza), refrescar el grid,
  tokens, ventanas y las reglas de BrosLMV para scripts que van a más de un cliente.
- **Panel de referencias de la Consola:** clic derecho sobre una función → *Ver ficha* (abre el manual justo ahí) o *Insertar ejemplo*. El panel ahora se llena del catálogo, no de listas en el código.
- **`build/sdk/verificar_catalogo_sdk.ps1`** (se corre en `generar_instalador.ps1`): el instalador no se genera si una función pública de `ctx`/`ctx.erp` no tiene entrada en el catálogo, o si una referencia
  cruzada está rota. Así el manual no se desactualiza. `build/sdk/generar_referencia_sdk.py` genera el manual desde el catálogo.

### Cambiado
- Para documentar una función nueva ya no se edita `Consola.cs`: se agrega su entrada a `src/assets/sdk_catalogo.json` (regla nueva en `AGENTS.md`).

## [2.97.1] — 2026-09-29 — Las claves no distinguen mayúsculas de minúsculas

### Corregido
- **`broslmv.mi_script`, `BrosLMV.Mi_Script` y `BROSLMV.MI_SCRIPT` son el mismo botón y el mismo script.** Antes la Consola conservaba la escritura tal cual y algunas comparaciones sí distinguían
  mayúsculas (los nombres reservados `CONSOLA`/`PRUEBA` del botón, favoritos y recientes de la Consola, y la lista de la biblioteca), así que dos escrituras de la misma clave podían tratarse como scripts distintos.
  Ahora todas esas comparaciones ignoran mayúsculas. Al guardar, si ya existe una clave igual con otra escritura no se duplica: se pregunta si reemplazarla y se conserva la escritura ya guardada.
  `CONSOLA` y `PRUEBA` quedan reservadas para guardar, sin importar cómo se escriban. El cuadro de guardar lo indica: «(no distingue mayúsculas)».
  (SQL y el sistema de archivos de Windows ya comparaban sin distinguir mayúsculas; por eso el botón siempre corrió el script correcto.)

## [2.97.0] — 2026-09-29 — Lenguaje sin recordar «lang:», clave siempre visible y respaldo de scripts

### Agregado
- **Selector de lenguaje** en la barra (botón *Lenguaje: C#* y clic en el lenguaje de la barra de estado): C#, Python o SQL. Al cambiarlo la Consola escribe (o quita) por ti la
  línea `# lang: python` / `-- lang: sql`; C# no lleva marca. Si el editor está vacío, deja un arranque mínimo (Python: `from broslmv import ctx`). Ya no hay que recordar la marca.
- **Respaldar todos los scripts…** (Más opciones): un archivo `.bros` por script de la empresa activa en la carpeta que elijas (con sus assets). Sirve de respaldo y para llevarlos a otra empresa o equipo.

### Cambiado
- **La clave del script es su nombre, sin nombre «bonito» aparte:** el script se llama igual que su botón (`BrosLMV.<clave>`). Al guardar, el cuadro muestra en vivo
  **«Se guardará como: BrosLMV.<clave>»** (los espacios y símbolos se cambian por `_`; se respetan mayúsculas y minúsculas). Así nadie escribe un nombre que luego no coincide con el botón.
- En la lista de **Plantillas** cada plantilla se muestra con su clave (`CREAR_DOC_DESDE_XML`); el nombre descriptivo queda en la ayuda emergente. El buscador encuentra por clave o por nombre.
- Manual: nueva sección «Dónde viven tus scripts» (SQL es la fuente de verdad; qué pasa si se borran archivos).

## [2.96.0] — 2026-09-29 — Editar botones que ya existen

### Agregado
- **Más opciones → Botones del ribbon…**: lista los botones del ribbon (por defecto los de BrosLMV; con «Incluir los de Comercial y otros productos» también los demás), con
  búsqueda por nombre, descripción o función. Al elegir uno se abre el mismo asistente en modo edición.
- **Cambiar nombre, descripción, ícono y la función que ejecuta** de cualquier botón (por ejemplo pasar uno de otro producto de `OtroProducto.X` a `BrosLMV.X`). En los
  botones que **no** son de BrosLMV solo se cambian esas propiedades (su ubicación y sus módulos no se tocan), se pide confirmación y se guarda copia.
- **Deshacer** restaura en su lugar el nombre, descripción, ícono y función anteriores (el `ControlID` no cambia, porque otras tablas de Comercial lo referencian).
  `zzBrosRibbonHist` guarda ahora el `ControlID` (se agrega la columna a las bases existentes).
- En **Editar botón** de un botón de BrosLMV, sección «Avanzado: función que ejecuta» para apuntarlo a otro script.

### Cambiado
- `RibbonAdmin`: una clave con punto (`Document.AbrirX`, `CFDI3.Y`) se trata como la función completa; una sin punto sigue siendo un script de BrosLMV. «Quitar botón» sigue
  limitado a botones de BrosLMV.

## [2.95.1] — 2026-09-29 — «Crear botón»: solo pestañas que sí se ven

### Corregido
- **El botón no aparecía** cuando se ponía en una pestaña que el módulo elegido nunca muestra. En Comercial cada pestaña se ve solo en cierto tipo de vista: «Lista»
  en las listas, «Contabilidad» solo en el catálogo de cuentas contables, «General» es propia de cada módulo, etc. Por ejemplo, un botón para *XML Recibidos*
  puesto en «Contabilidad» se guardaba bien pero no se veía nunca. El asistente ahora pide primero los **módulos** y **solo ofrece las pestañas que sí se ven ahí**
  (las demás salen desactivadas con la razón); con «En todos» avisa cuando una pestaña solo aparece en un tipo de vista («Lista (solo en las listas)»).
- La pestaña y la sección se resuelven **por módulo** al publicar: si el botón va a varios módulos y la pestaña es propia de cada módulo (como «General»),
  cada módulo recibe su fila en la sección de su propia pestaña (antes se usaba una sola, que solo servía a un módulo).

### Cambiado
- Paso 2 del asistente: primero **Módulos**, después **Pestaña** y **Sección**.
- Guía `docs/CREAR_BOTON.md`: explica qué pestañas se ven en qué módulos.

## [2.95.0] — 2026-09-29 — Asistente «Crear botón…» y catálogo de íconos BrosLMV

### Agregado
- **Asistente «Crear botón…»** (clic secundario sobre un script → *Crear botón…* / *Editar botón…*, o *Más opciones → Nuevo botón…* para crear el botón
  primero y el script después). Tres pasos con vista previa del ribbon: **1 · El botón** (nombre, descripción que sale al pasar el mouse, ícono),
  **2 · Dónde aparece** (pestaña y sección existentes o nuevas, en todos los módulos o solo en algunos) y **3 · Quién lo ve** (todos, grupos o usuarios;
  opcionalmente también en otras empresas). Ya no hay que escribir `BrosLMV.XXXX`, el nombre del ícono ni IDs. Guía: `docs/CREAR_BOTON.md`
  (botón *Ayuda* del asistente); diseño: `docs/DISENO_CREAR_BOTON.md`.
- **Catálogo de íconos BrosLMV:** 2,118 íconos modernos (colección Lucide, licencia ISC) convertidos a `.ico` (16/32/48 px) que el instalador copia a la carpeta de
  íconos de Comercial (`BrosLMV_<nombre>.ico`), con búsqueda en español y sugerencias según el nombre del botón. Junto a los 914 íconos nativos de Comercial y
  «Explorar…» para elegir un `.ico`/`.png` propio (se copia con el prefijo `BrosLMV_`). Licencia en `C:\BrosLMV\iconos\LICENCIA_Lucide.txt`.
- **Deshacer / Quitar:** cada cambio de un botón guarda copia en `zzBrosRibbonHist` (por empresa); el asistente puede restaurar el último estado o quitar el botón.
- Nuevas piezas: `src/RibbonAdmin.cs` (motor sobre `engRibbonTab/Group/Control/Menu`, todo en una transacción por empresa), `src/RibbonUi.cs` (ventana WebView2),
  `src/assets/crear_boton_app.html` (pantalla), `build/iconos/` (generador del catálogo; requiere Node.js al compilar).

### Cambiado
- `generar_instalador.ps1` genera la guía HTML y el catálogo de íconos antes de compilar; `Instalar.ps1` instala los íconos y el catálogo.
- **El instalador `.exe` (`instaladores/Empresas/RuntimeInstaller.cs`) hace ahora lo mismo que `Instalar.ps1`:** instala el catálogo de íconos, refresca
  la plantilla vigente `CREAR_DOC_DESDE_XML.ctx` y **mueve** las plantillas anteriores (PLANTILLA_*, EJEMPLO_*, PRUEBA_*, REQUISICION, SOLICITUD_COMPRA) a
  `scripts\_archivo\plantillas_anteriores_<fecha>\`. Antes solo lo hacía el script `.ps1`, así que quien actualizaba con el `.exe` no recibía esos cambios de la 2.94.0.
  `generar_exes.ps1` incluye la carpeta `iconos` en el paquete embebido.
- Solo se modifican botones `BrosLMV.*`; los nativos de Comercial no se tocan.

### Pendiente de verificar en Comercial
- Que el filtro por usuario (`IfUserIDIs`, una fila por usuario) lo respete el ribbon de Comercial, y si el botón aparece sin reiniciar tras `RefreshRibbon()`.
- Editar botones que no son de BrosLMV (por ejemplo los de otros productos, para cambiar su ícono o su función) queda para una versión posterior.

## [2.94.1] — 2026-09-29 — Consola: documentación siempre disponible y guardado más claro

### Corregido
- **«Ver documentación» ya no falla** con «No se encontró la documentación»: los HTML de las plantillas viajan **dentro de la DLL**
  (antes eran archivos sueltos en `C:\BrosLMV\docs\plantillas` que se perdían si no se copiaban). Si existe un archivo más nuevo en esa carpeta, sigue sirviendo de respaldo.
- Un script **Python sin la línea `# lang: python`** ya se reconoce (si su primera línea de código es `import x` o `from x import y`) en vez de intentar compilarse como C#.

### Cambiado
- **Categorizar** (clic secundario) ahora muestra un combo con las categorías que ya existen en la empresa (o permite escribir una nueva), igual que «Guardar como».
- **Guardar** acepta el nombre del botón con espacios y acentos («Crear docs XML» → `Crear_docs_XML`) y, para una plantilla, propone el nombre recomendado en su cabecera.
- Las plantillas muestran en su ayuda emergente el nombre con el que se guardan como botón (`BrosLMV.<NOMBRE>`).
- Plan de la Consola v3 documentado en `docs/PLAN_CONSOLA_V3.md`.

## [2.94.0] — 2026-09-28 — Documentos desde XML, ágil

### Agregado
- Plantilla C# **Crear documentos desde XML** (`CREAR_DOC_DESDE_XML.ctx`, con
  pestañas: Documentos, Proveedores, Partidas, Impuestos y Resultado). Crea Facturas de Compra o Gastos
  desde los CFDI recibidos con solo pulsar «Crear documentos»: da de alta los proveedores que falten,
  deja las partidas sin producto como renglón descriptivo (o crea el producto / exige uno, a elección) y
  muestra el avance. Detalle en `docs/CREAR_DOC_DESDE_XML.md`; diseño en `docs/DISENO_CREAR_DOC_DESDE_XML.md`.
- **El impuesto lo rige el documento:** se empareja la composición fiscal del XML (IVA, IEPS, retención de
  IVA y de ISR) con los tipos de impuesto existentes. Sin coincidencia exacta el documento no se crea y se
  sugiere el tipo de impuesto que falta; producto y tipo de gasto no cambian el impuesto.
- **Destinos por naturaleza, no por nombre:** ofrece como destino todo módulo cuyo tipo de documento, destinatario y
  recepción de XML coincidan con los de Compra y Gasto de fábrica, incluidos módulos copiados o renombrados.
- **Memoria por proveedor:** sugiere producto y tipo de gasto por proveedor + clave/descripción (mapa
  propio, historial de gastos de Comercial y clave SAT) y los aprende al crear el documento.

### Cambiado
- **Una sola plantilla:** la Consola solo ofrece «Crear documentos desde XML». Las plantillas y ejemplos anteriores (Orden de Compra, Recepción,
  Factura, Requisición, extracción de datos, dashboards, etc.) se retiraron por estar desactualizadas y quedaron archivadas en
  `docs/archivo/plantillas_2.93.0/`. Al actualizar, el instalador las **mueve** de `C:\BrosLMV\scripts\` a
  `scripts\_archivo\plantillas_anteriores_<fecha>\` (no las borra) y refresca la plantilla vigente.
- **Documentación de la plantilla:** clic secundario sobre la plantilla en la Consola → «Ver documentación» (ventana con la documentación
  completa); el código de la plantilla queda corto y legible.

### Corregido
- **`ctx.erp.RefreshGrid()` nunca refrescaba el grid:** llamaba a `XEngine.RefreshGrid` sin parámetros y fallaba en silencio con
  `DISP_E_PARAMNOTOPTIONAL`. Ahora le pasa el grid actual (`janusGrid`) y, como recarga todo y deja la vista al principio, restaura la
  fila que tenías (`Row` + `EnsureVisible` del grid). Todos los scripts que ya lo llaman empiezan a refrescar de verdad. Nuevo estándar:
  todo script que cambie datos visibles en una lista termina refrescando el grid (`AGENTS.md`, `MANUAL.md`).

### Corregido (respecto a la primera versión del importador)
- El alta de proveedor replica ahora las tablas que escribe Comercial al generar desde XML (entidad tipo 1,
  dirección fiscal, clave de identificación, información principal, rol de proveedor); antes escribía tres y
  con tipo de entidad distinto.
- Las fechas del documento usan la de **emisión** del XML (como Comercial); la agenda de pagos se calcula
  con el plazo real sobre esa fecha; método/forma de pago y uso de CFDI viven en `docDocumentCFD`.
- Se eliminaron los identificadores de impuesto fijos y el «tipo parecido»: sin coincidencia exacta se
  bloquea. Las retenciones ya salen del tipo de impuesto y solo se fuerzan si el total no cuadra con el XML.

## [2.93.0] — 2026-09-27 — Vencimientos de Factura de Compra: fórmula de `engPaymentTermDetail` corregida

> El único cambio en `src\` es el bump de `AssemblyVersion`; el arreglo vive en las plantillas
> de fábrica (`instalador\scripts\`). **Regenerar el instalador** para que llegue a instalaciones
> nuevas; las empresas ya provisionadas tienen su propia copia de la plantilla.

### Corregido
- **Las 6 plantillas de Factura de Compra** (`PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP`,
  `..._FORMS_PYTHON`, `..._FORMS_SQL_PURO_CSHARP`, `..._WEBVIEW2_CSHARP`, `..._WEBVIEW2_PYTHON`,
  `PLANTILLA_EJEMPLO_FACTURA_COMPRA_CSHARP`) calculaban las fechas de la agenda de pago leyendo
  `PaymentUnit` como unidad y `PaymentPeriod` como cantidad. Es al revés: **`PaymentPeriodID` es
  la unidad** (1=día, 2=semana, 3=mes, 4=trimestre, 5=semestre, 6=año, catálogo `engRefCombo`
  `PaymentTermPeriod`) y **`PaymentUnit` es la cantidad**; `PaymentPeriod` casi siempre es 0.
  Efecto real: condiciones como "30/60/90 DIAS" quedaban venciendo **el mismo día** del
  documento (solo "2 SEMANAS" salía bien, por coincidencia). Verificado contra las condiciones
  reales de una empresa en producción; hallazgo tomado de un proyecto satélite que ya usaba la
  fórmula correcta. Documentado en `MANUAL.md` §10.4.

## [2.92.0] — 2026-09-27 — Dos bugs reales corregidos: `ctx.select_file`/`select_folder` desde Python y `AgregarSerie` en documentos de entrada

Motivado por el barrido a fondo de un proyecto satélite (migración de otra herramienta de
scripting a BrosLMV, botón por botón, en producción): dos bugs reales del addon compartido,
no de ese cliente, encontrados al migrar un gestor de adjuntos y una Recepción de Compra.

### Corregido
- **`ctx.select_file()`/`ctx.select_folder()` desde un script Python tronaban el proceso
  completo.** `RenderSelectFile`/`RenderSelectFolder` (en `HostClient.cs`) mostraban el
  diálogo de Windows (`OpenFileDialog`/`SaveFileDialog`/`FolderBrowserDialog`, todos WinForms)
  directo en el hilo que atiende el pipe de Python, que no está garantizado en modo STA —
  igual que ya le pasaba a WebView2 antes de que `RenderUiHtml` se moviera a un hilo STA
  dedicado (ver `archivo/CHANGELOG_ARCHIVO.md`), pero esas dos funciones nunca recibieron el mismo
  arreglo. Ahora corren en su propio hilo STA, igual que `RenderUiHtml`. Confirmado en
  producción con `AdjuntarArch`.
- **`ctx.erp.AgregarSerie(...)` fijaba `Quantity=-1` sin importar el tipo de documento.**
  Esa convención es correcta para documentos de SALIDA (Factura, Remisión, Pedido), pero un
  documento de ENTRADA (Recepción de Compra, Factura de Compra) necesita `Quantity=1` — con
  el valor fijo, las series quedaban registradas como si hubieran salido del almacén en vez
  de haber entrado. Confirmado en producción: Recepción de Compra 14768. Ahora acepta un
  parámetro opcional `quantity` (default `-1`, mismo comportamiento que antes para los
  llamadores existentes); `RecepcionOC` debe pasar `quantity=1` explícito.

## [2.91.0] — 2026-09-27 — Paginación real (Paged.js) para `htmlpdf/formatos/`

> El único cambio en `src\` es el bump de `AssemblyVersion`; lo demás vive en
> `htmlpdf\formatos\` y documentación nueva.

### Agregado
- `htmlpdf/formatos/paged.polyfill.min.js` — Paged.js v0.4.3 (MIT), disponible para cualquier
  plantilla que necesite encabezado/pie repetidos en cada hoja y "Página X de Y" real en el PDF
  (Chromium/WebView2 no resuelve `counter(page)` de `@page` sin esto). Ver
  [`PAGINACION_PDF.md`](PAGINACION_PDF.md) para el patrón completo: `string-set`/`string()`,
  spacer flex, el workaround de un bug real de Paged.js 0.4.3 que no repite `<thead>` entre
  hojas, y el contrato `window.__READY_FOR_PDF__` (ya soportado por `htmlpdf/Program.cs` desde
  antes).
- **Ninguna de las 10 plantillas existentes se modificó todavía** — este cambio solo trae la
  pieza lista para usarse; retrofitear una plantilla concreta queda pendiente (ver "Qué falta"
  en `PAGINACION_PDF.md`).

## [2.90.0] — 2026-09-15 — `ctx.erp.NuevoDocumento`/`AgregarArticulo`: Rate, PaymentTermID, CurrencyID, Title, SourceDocumentID, Comments, SourceDocumentItemID

Motivado por la migración de un script de facturación de pedidos desde otra herramienta de scripting a
BrosLMV: crear una Factura de Cliente desde un Pedido necesitaba conservar el tipo de cambio,
la condición de pago y el título del documento origen, enlazar `SourceDocumentID`, y anotar
`Comments`/`SourceDocumentItemID` en cada partida — nada de esto lo cubría el builder.

### Agregado
- `ctx.erp.NuevoDocumento(...)` acepta ahora `rate`, `paymentTermId`, `currencyId`, `title` y
  `sourceDocumentId` (todos opcionales). Los defaults reproducen EXACTO el comportamiento
  anterior (`Rate=1`, `PaymentTermID=1`, `CurrencyID=3`, sin `Title` ni `SourceDocumentID`) —
  las llamadas existentes con 3 argumentos posicionales (integraciones externas, pruebas de POS) no
  cambian de comportamiento.
- `ctx.erp.AgregarArticulo(...)` acepta ahora `comments` y `sourceDocumentItemId` (opcionales,
  mismo patrón de columna condicional que ya usaban `lote`/`serialNumber`/`deliverDocumentItemId`).
- **`ctx.erp.AgregarSerie(documentId, documentItemId, productId, serialNumber, depotId)`** —
  nuevo. Asigna una serie a una partida ya creada; para partidas con varias series se llama
  una vez por serie. Cierra el último hueco de SQL manual que quedaba en "copiar documento
  con series" (antes cada script portado reinventaba el mismo INSERT a mano).

### Cambiado
- Se eliminó una cadena `insertDoc` muerta dentro de `NuevoDocumento` (se construía pero nunca
  se ejecutaba — solo `batch` corría de verdad). No afecta comportamiento.

## [2.89.0] — 2026-09-10 — `.exe` robusto + Diagnóstico + botón Cotizador manual por módulo

> El único cambio en `src\` es el bump de `AssemblyVersion` (para que el instalador empaquete
> esta versión); lo demás vive en `BrosLMV.HtmlToPdf.exe` y en los scripts `.ctx`.

### Agregado
- **`BrosLMV.HtmlToPdf.exe` — robustez**: watchdog `--timeout <seg>` que mata el proceso si se
  cuelga; **códigos de salida** documentados (`0` ok · `1` argumentos · `2` navegación falló ·
  `3` falta WebView2 Runtime · `4` PrintToPdf falló / PDF vacío · `5` timeout · `6` excepción);
  bitácora en `C:\BrosLMV\logs\htmltopdf_YYYYMMDD.txt`; comprobación del **WebView2 Runtime**
  antes de arrancar; validación del HTML (vacío → error; sin `<body>` → aviso); en `--preview`
  el botón "Guardar PDF" espera a que la página esté lista (`window.__READY_FOR_PDF__`) y hace
  un chequeo de tamaño del PDF resultante.
- **Pestaña "Diagnóstico"** en *Configuración de formato*: comprueba motor HTML→PDF, WebView2,
  las 10 plantillas, las tablas `zzBros*`, la cuenta SMTP, los módulos con formato asignado, el
  enganche del botón Cotizador y que los controles nativos del ribbon estén **sanos**. Botón
  **"Reparar controles Cotizador"** (red de seguridad).

### Cambiado
- **Botón "Cotizador" dentro del documento — ahora se activa por módulo, manualmente.** Se quitó
  la acción masiva "Activar botón Cotizador en documentos" (había una versión que llegó a
  sobrescribir el `ControlExecute` de controles **nativos** del ribbon → error 438 al guardar).
  En su lugar, en la ficha de cada módulo de *Configuración de formato* hay un interruptor
  *"Usar el Cotizador de BrosLMV en este módulo"* que sólo toca el parámetro de módulo
  `engModuleParameter.CreateDocQuotationDLLFunction` (el punto de personalización oficial) y se
  niega a pisar un Cotizador personalizado ajeno.

### Corregido
- `repararCotizador` devuelve al ribbon nativo (`ControlExecute='CreateDocQuotation'`) cualquier
  control "Cotizador" que una versión vieja hubiera dejado apuntando a `BrosLMV.Cotizador`.

## [2.88.0] — 2026-09-10 — método COM `Cotizador()` (hook del botón dentro del documento)

### Corregido
- El botón "Cotizador" que aparece **dentro de un documento** se engancha con el parámetro de
  módulo *"Cotizador personalizado (DLL.Function)"* (`engModuleParameter.ParameterKey =
  'CreateDocQuotationDLLFunction'`) = `BrosLMV.Cotizador`. Ahí Comercial **invoca un método por
  su nombre** en el objeto COM (no `ExecuteFunction("Cotizador")` como el ribbon), así que la
  clase `BrosLMV.clsMain` necesitaba exponer `Cotizador()`. Sin él, al presionar el botón salía
  *"CreateQuoteProducts — Object doesn't support this property or method"*. Se agregó
  `public void Cotizador([Optional] object arg1)` que hace `ExecuteFunction("Cotizador")`.

## [2.87.0] — 2026-09-10 — "Generar documento (PDF)" + "Configuración de formato" + 10 formatos genéricos

> El único cambio en `src\` es el bump de `AssemblyVersion` (para que el instalador empaquete
> esta versión); la lógica vive en scripts `.ctx` + `BrosLMV.HtmlToPdf.exe` (proceso aparte).
> Sistema propio de BrosLMV para generar y enviar documentos en PDF, independiente del de
> Comercial (que no deja configurar Outlook, mete un HTML fijo y usa un editor de formatos
> viejo).

### Agregado
- **`BrosLMV.HtmlToPdf.exe`** (WebView2): modo `--preview` (ventana visible con "Guardar PDF" /
  "Imprimir") además del `--pdf` headless. Se instala en `C:\BrosLMV\htmlpdf\`.
- **`Cotizador.ctx`** (AppKey `BrosLMV.Cotizador`, slot dentro del documento abierto): resuelve
  las **mismas etiquetas** que un formato de Comercial —columnas del TVF
  `dbo.[vwLBSDocDocumentPrint40-DocumentID]`, referencias de `engAddendaFieldRef` con
  `{DocumentID}`/`{DocumentItemID}`, y `[Format(campo,#,##0.00)]`— sobre el `.html` real del
  formato, y lo renderiza con WebView2. Botones **Vista previa · Generar PDF · Enviar por correo**.
- **`ConfiguracionFormato.ctx`** (botón "Configuración de formato" en la pestaña *Soluciones LMV*) —
  **UI moderna en WebView2** (HTML/CSS/JS; C# solo hace el acceso a datos, puente por `postMessage`):
  - Árbol de módulos documentales **con los iconos reales de Comercial** (`engModule.Icon` →
    `C:\Program Files (x86)\Compac\ComercialSP\Icons\`, convertidos a PNG base64), agrupados por área,
    con buscador. Nodo "General (por defecto)".
  - Por módulo: **formato HTML** con *Nuevo… / Duplicar / Editar HTML / Quitar* (crea la fila en
    `engModulePrintFormat` + el archivo `.html`, desde copia / import / plantilla BrosLMV) y opción
    "usar también como predeterminado de Comercial"; **plantilla de correo**; **carpeta destino del
    PDF por módulo** (acepta rutas de red `\\SERVIDOR\…` para servidor-cliente); **patrón del nombre**
    con `[Etiquetas]` (incl. `[Modulo]`), chips para insertar y **vista previa en vivo**;
    "personalizar para este módulo" vs heredar de General.
  - Correo: cuenta **SMTP propia** (Outlook/Gmail/dominio; contraseña de aplicación — OAuth de
    navegador queda para una versión próxima) + editor de plantillas (asunto/cuerpo HTML/CC/BCC).
  - Tablas propias: `zzBrosFormatoConfig` (fila `ModuleID=0` = General; filas por módulo con
    `PdfPersonalizado`), `zzBrosCorreoConfig`, `zzBrosPlantillaCorreo` (contraseña SMTP cifrada con
    `ctx.erp.EncryptString`).
- **10 plantillas HTML genéricas** (`htmlpdf\formatos\*.html` → `C:\BrosLMV\formatos\`): Pedido de
  cliente, Remisión, Factura, Nota de crédito, Orden de compra, Recepción de mercancía, Documento
  de gastos (con *tipo de gasto* por partida), Entrada de almacén, Traspaso, Salida de almacén.
  Diseño gris/azul, encabezado con **QR** (`[QRBros]` → `GenerarDocumentoPDF.ctx` lo resuelve con
  XEngine `GetQRCode`), tabla de partidas con `thead` que se repite por página y renglones que no
  se cortan, totales + cantidad con letra, firmas. El botón "Instalar formatos BrosLMV" de
  *Configuración de formato* las copia, crea las filas `engModulePrintFormat`, las asigna por
  módulo y da de alta las referencias `engAddendaFieldRef` que necesitan
  (`PorcentajeIVALinea`, `PorcentajeDescuentoLinea`, `TipoGastoLinea`).
- `GenerarDocumentoPDF.ctx`: detecta si el formato es UTF-8 (BrosLMV) o windows-1252 (Comercial
  viejo) y lo lee bien; resuelve `[QRBros]` y `[Modulo]`.
- Instalador: `generar_instalador.ps1` empaqueta `htmlpdf\` + `formatos\`; **`RuntimeInstaller.cs`
  (el instalador con interfaz)** y `Instalar.ps1` copian `C:\BrosLMV\htmlpdf\`, `C:\BrosLMV\formatos\`
  y las 10 plantillas a la carpeta Formatos de Comercial de cada equipo; los scripts core
  (`Cotizador.ctx`, `ConfiguracionFormato.ctx`) siempre se refrescan.
- `ConfiguracionFormato.ctx`: botón **"Activar botón Cotizador en documentos"** — engancha el PDF
  de BrosLMV por el parámetro de módulo *"Cotizador personalizado (DLL.Function)"*
  (`engModuleParameter.CreateDocQuotationDLLFunction = 'BrosLMV.Cotizador'`), que es el punto
  oficial de personalización y **no toca el ribbon nativo**. Además repara automáticamente
  cualquier control `engRibbonControl` "Cotizador" que una versión anterior de este botón hubiera
  dejado con `ControlExecute='BrosLMV.Cotizador'` (eso rompía el guardado de documentos con el
  error 438 *"Object doesn't support this property or method"* — ver `dist/FIX_botones_cotizador.sql`).
- Resolvedor: protege los bloques `<script>`/`<style>` de la sustitución de etiquetas (la librería
  QR usaba `[x]` de acceso a arreglos). El QR se genera en la plantilla con `qrcode-generator`
  incrustado (XEngine no expone `GetQRCode` de imagen). `BrosLMV.HtmlToPdf.exe` fuerza
  `ShouldPrintBackgrounds=true` para que el PDF salga con los fondos de color.
- **Instalador**: `generar_instalador.ps1` compila `htmlpdf\` a `instalador\htmlpdf\`;
  `Instalar.ps1` lo copia a `C:\BrosLMV\htmlpdf\` y refresca los dos scripts core;
  `provision_empresa.sql` da de alta el botón "Configuración de formato" en el grupo BrosLMV
  (sección 4c).

## [2.86.0] — 2026-09-09 — `ctx.erp.AbrirDocumento`/`ctx.erp.AgregarRenglonExistente` (Document.clsMain)

> Investigando cómo poner un botón "Generar PDF" dentro de un documento abierto, se descubrió
> `Document.clsMain` — un objeto COM real y gratuito (parte del SDK, no de otra herramienta de scripting) que
> permite reabrir un documento **ya guardado** para leerlo/editarlo/mostrarlo en pantalla, algo
> que `ctx.erp.NuevoDocumento` no puede hacer (solo trabaja con documentos creados dentro de la
> misma ejecución). Confirmado con datos reales: se agregó un renglón a un documento existente
> y el total del encabezado se actualizó correctamente. Detalle completo, incluidos los errores
> reales encontrados en el camino y su corrección, en
> `material interno`.

### Agregado
- `ScriptContext.erp.AbrirDocumento(documentId, moduleId)` — abre la ventana real de un
  documento ya guardado, igual que si el usuario lo hubiera abierto desde la lista. Pensado
  para usarse al final de cualquier botón que genere un documento por script (p. ej.
  Requisición→Orden de Compra): el usuario lo ve de inmediato, sin tener que buscarlo.
- `ScriptContext.erp.AgregarRenglonExistente(documentId, moduleId, productId, cantidad, precio)`
  — agrega un renglón a un documento que ya existe (de una ejecución anterior), algo que
  `NuevoDocumento`/`AgregarArticulo` no permiten. Recalcula totales/impuestos internamente vía
  `RecalcCompleto` (ya probado en toda la investigación de Punto de Venta).

## [2.85.0] — 2026-09-02 — `ctx.HashPassword`/`ctx.VerifyPassword` (base de "Usuarios PV")

> Primer paso de construcción del Punto de Venta (ver `puntodeventa/DISENO.md` §9): sin forma
> de replicar cómo Comercial cifra sus propias contraseñas, los usuarios de la caja tendrán su
> propia credencial (`zzBrosPVUsuario`, ligada a `engUser.UserID` pero con su propio password),
> provisionada desde un botón "Usuarios PV" dentro de Comercial. Este cambio solo agrega el
> mecanismo de hash reutilizable — el botón en sí vive como script (`zzBrosScript`), no en el
> addon.

### Agregado
- `ScriptContext.HashPassword(password, out sal, out hash, out iteraciones)` y
  `ScriptContext.VerifyPassword(password, sal, hash, iteraciones)` — expone a cualquier script
  `.ctx` el mismo algoritmo PBKDF2-HMAC-SHA256 (210,000 iteraciones, comparación en tiempo
  constante) ya usado para la contraseña de la Consola (`ConsolaPasswordHash.cs`), sin duplicar
  el algoritmo ni exponer la clase interna. Los scripts Roslyn compilan como ensamblado aparte
  y no pueden ver tipos `internal` del addon — este wrapper es el punto de acceso correcto.

## [2.84.0] — 2026-09-02 — `ctx.Msg`/`ctx.Confirm` seguros en `BrosLMV.Runner` (headless)

> Encontrado validando la primera Venta creada vía SDK sin Comercial abierto (ver
> `puntodeventa/docs/01_primera_venta_sdk.md`): un script `# job: safe-offline` que llamaba
> `ctx.Msg()` corrió **60 veces más lento** (514s vs 8.6s del mismo script sin ese llamado) —
> `ctx.Msg`/`ctx.Confirm` son `MessageBox.Show(...)` sin ningún resguardo para contexto sin
> escritorio interactivo. Confirmado con el log de diagnóstico: todo el trabajo SQL real del
> script termina en <6s; el resto del tiempo es silencio total hasta que el proceso por fin
> continúa. Riesgo real: cualquier script marcado para correr sin supervisión (incluso desde
> Tarea Programada de Windows) que use `ctx.Msg`/`ctx.Confirm` puede colgarse un tiempo
> impredecible, o indefinidamente, esperando un clic que nadie va a dar.

### Corregido
- `ScriptContext.Headless` (nueva propiedad) — `BrosLMV.Runner` la activa al construir el
  contexto. Con `Headless=true`, `ctx.Msg` escribe al log en vez de abrir un diálogo real, y
  `ctx.Confirm` regresa `false` (nunca asume "sí" en un flujo sin supervisión) y también lo
  deja anotado en el log.
- Dentro de Comercial (Consola/botón), el comportamiento no cambia — `Headless` solo se activa
  en el Runner.

## [2.83.0] — 2026-08-24 — Auto-reparación de la biblioteca de scripts (zzBrosScript)

> Encontrado en una empresa migrada de equipo (`COCTEL_DE_IDEAS`): `zzBrosScript` venía de
> una versión vieja del esquema, sin las columnas `Categoria`/`HashSHA256`/`AprobadoPor`/
> `AprobadoEl` que versiones más recientes agregan. `BrosListar()` las pedía en el SELECT,
> tronaba con "Invalid column name", y el error se tragaba silenciosamente (`catch { return
> lista vacía }`) -- la Consola mostraba la carpeta "Scripts — <empresa>" vacía, SIN AVISO,
> como si no hubiera ningún script guardado. La reparación (`BrosAsegurarTablas()`, ya
> existía) solo se disparaba desde Guardar/Categorizar/Historial/Importar paquete -- pero
> esas acciones necesitan ver un script primero, y con el árbol vacío nunca se llegaba a
> disparar: candado cerrado sobre sí mismo.

### Corregido
- `CargarArbol()` ahora corre `BrosAsegurarTablas()` una vez por sesión, antes de listar --
  se autorepara sola la primera vez que se abre la Consola en una empresa con el esquema
  viejo, sin que el usuario tenga que hacer nada.
- Nueva opción "Reparar biblioteca de scripts" en el menú "Más opciones", por si hace falta
  forzarla a mano (Consolas viejas sin este fix, o para confirmar sin cerrar y reabrir).

### Agregado
- Al guardar un script nuevo (o "Guardar como"), el diálogo ahora también pide la
  categoría en el mismo paso -- combo editable: elige una de las que ya existen en la
  empresa o escribe una nueva. Antes había que guardar primero y usar "Categorizar…" como
  paso aparte; ahora el script nace ya dentro de su categoría en el árbol.

## [Instalador sin cambio de versión del addon] — 2026-08-13 — Panel de contraseña de Consola compacto

> Sin cambios en el binario del addon (sigue en `AssemblyVersion 2.82.0`). Rediseño de la
> pantalla del instalador tras feedback real: el panel de contraseña ocupaba demasiado
> espacio (párrafo largo + botón aparte) y empujaba fuera de vista la tabla de empresas.

### Corregido
- Panel de contraseña de la Consola: una sola fila compacta (antes: tarjeta con párrafo
  explicativo + botón "Cambiar contraseña" aparte). Detecta sola si la empresa
  seleccionada ya tiene contraseña activa y pide la actual antes de aceptar la nueva --
  ya no hace falta una ventana separada para cambiarla.
- Ventana `MainWindow` con `MinHeight`/`MinWidth` para que nunca quede la tabla de
  empresas sin espacio visible, sin importar el tamaño de pantalla.

## [2.82.0] — 2026-08-13 — Contraseña de la Consola (candado por empresa)

> La Consola ahora puede pedir contraseña antes de abrir -- pensado para empresas donde
> varios usuarios comparten el equipo y algunos "andan de tentones" moviendo scripts sin
> saber qué hacen. Se activa opcionalmente desde el instalador (checkbox al
> instalar/actualizar): si se activa, escribe un hash salteado (PBKDF2-HMAC-SHA256,
> 210,000 iteraciones -- guía OWASP 2023) en una tabla nueva, `zzBrosConsolaPass`, **nunca
> la contraseña en claro ni cifrado reversible** (no hace falta leerla de vuelta, solo
> compararla). Para cambiarla: correr el instalador de nuevo y usar el botón "Cambiar
> contraseña de una empresa ya instalada..." (pide la actual + la nueva + confirmación). Si
> se olvida, no hay forma de recuperarla -- hay que resetearla directo en SQL:
> `UPDATE zzBrosConsolaPass SET Habilitado=0 WHERE Id=1` (o `DELETE FROM zzBrosConsolaPass`)
> en la base de esa empresa.

### Agregado
- Tabla `zzBrosConsolaPass` (una sola fila, `Id=1` forzado por CHECK) en
  `provision_empresa.sql`, creada/migrada automáticamente al instalar o actualizar.
- `src/ConsolaPasswordHash.cs`: hash/verificación PBKDF2 salteado, compartido (enlazado, no
  copiado) entre el addon y el instalador -- un solo lugar donde vive el algoritmo.
- `src/ConsolaPasswordPromptForm.cs`: diálogo modal que bloquea la apertura de la Consola
  hasta escribir la contraseña correcta, si la empresa activa tiene el candado prendido.
- Instalador (`instaladores/Empresas`): casilla "Proteger la Consola con contraseña" al
  instalar/actualizar (aplica a las empresas seleccionadas en esa corrida), y ventana nueva
  "Cambiar contraseña de la Consola" (exige la contraseña actual antes de aceptar la nueva).

## [2.81.0] — 2026-08-08 — Nueva plantilla: Vincular XML CFDI al documento (Gastos y compras)

> Portado de un script real de otra herramienta de scripting ("XML recibidos por RFC") a BrosLMV nativo.
> El original dependía de una vista propia (`zzXMLRecibidos`) que había que crear a mano
> en cada base nueva. Se investigó su definición real y resultó estar armada enteramente
> sobre tablas nativas de Comercial (`docDocumentCFDiSAT`, `orgBusinessEntity`,
> `orgBusinessEntityMainInfo`) -- esta plantilla mete esa misma lógica inline, sin
> depender de ninguna vista externa: se puede copiar directo a cualquier empresa nueva.

### Agregado
- **`PLANTILLA_VINCULAR_XML_GASTOS_PYTHON.py`** — busca XML CFDI ya cargados en Comercial
  sin vincular a ningún documento (`docDocumentCFDiSAT.DocumentID=0`), filtrados por el
  RFC del proveedor del documento activo, y permite vincular uno o varios al documento en
  curso (actualiza `docDocumentCFD`/`docDocumentCFDiSAT`, con soporte de XML principal +
  secundarios). Pensado para el módulo de Gastos, pero no asume ningún módulo fijo — usa
  `ctx.get_selected_ids()` para trabajar sobre cualquier documento activo.
  Doble clic en un renglón abre un detalle completo del XML en ventana modal (fecha,
  importes, forma/método de pago, UUID, etc.) — funcionalidad nueva que el original no
  tenía. Sin dependencias de empresa (nombre de empresa, ruta de logo) hardcodeadas — el
  logo es opcional y se omite si no existe; el filtro de "empresa propia" usa el
  `OwnedBusinessEntityID` real del documento activo. La tabla auxiliar de auditoría
  (`ZZUuidAsociados`) se autocrea si no existe, mismo patrón que
  `PLANTILLA_AUTORIZACION_POR_MONTO_SQL_PURO.sql` con `BrosAutorizaciones`.

## [Instalador sin cambio de versión del addon] — 2026-08-08 — `BrosLMV.Runner.exe` ahora viaja dentro del instalador

> Sin cambios en el binario del addon (sigue en `AssemblyVersion 2.80.0`). Cambia el
> **empaquetado**: `build\generar_instalador.ps1` y `build\generar_exes.ps1`.

### Cambiado
- **`BrosLMV.Runner.exe` (el puente headless hacia XEngine, T3.3) ahora se compila y se
  incluye dentro de `payload.zip`, junto con `host\`/`workers\`.** Antes era un paso 100%
  manual fuera del instalador (compilar `runner\BrosLMV.Runner.csproj` y copiar el resultado a
  mano a `C:\BrosLMV\runner\` en cada servidor) — cualquier consumidor externo (como
  un CRM externo) tenía que hacerlo aparte, sin que el instalador lo supiera ni lo verificara.
  Ahora `RuntimeInstaller.cs` lo extrae y coloca en `C:\BrosLMV\runner\` como una carpeta más
  del runtime, igual que `host`/`workers`/`runtimes`. `generar_exes.ps1` aborta si
  `runner\BrosLMV.Runner.exe` no quedó en el payload, para no distribuir un instalador
  incompleto por accidente.
- Trae de una vez el fix de `BrosLMV.Runner` v0.3.0 (auto-sanado del ProgID de XEngine de 32
  bits, ver entrada de abajo) — cualquier instalación nueva hecha con este instalador ya lo
  incluye sin pasos aparte.

## [2.81.0] — 2026-08-07 (solo documentación) — 3 gotchas y 1 validación positiva, desde un consumidor externo real (un CRM externo)

> **Sin cambios en el binario del addon**: sigue en `AssemblyVersion 2.80.0` (no requiere
> recompilar ni re-registrar). Los hallazgos vienen de **un CRM externo** (proyecto
> separado), que consume `BrosLMV.Runner.exe` (mismo
> binario de este repo) como puente headless hacia XEngine para encolar y crear documentos
> reales (Entradas/Salidas, Órdenes de Compra, Recepciones de Compra) desde hace varios días
> en producción real, no solo prueba. En el camino se encontraron 3 riesgos/límites y se
> confirmó 1 decisión de producto pendiente — los 4 le pertenecen a BrosLMV/XEngine en
> general, no solo al CRM externo, y quedan documentados aquí. El fix de código del hallazgo de
> despliegue (ProgID de 32 bits) va en la entrada de arriba (`BrosLMV.Runner` v0.3.0).

### Documentado (gotcha — ver `MANUAL.md` §12)
- **Un trigger `AFTER INSERT` sobre `docDocument` (o cualquier tabla que XEngine use para
  crear documentos) corrompe la recuperación del ID recién insertado.** Confirmado por
  el CRM externo con una prueba controlada y reproducible: agregar un trigger que hace su propio
  `INSERT` en otra tabla con columna `IDENTITY` propia hace que `SCOPE_IDENTITY()` (patrón
  `INSERT ...; SELECT SCOPE_IDENTITY()` en un solo batch) devuelva un ID que **no coincide**
  con el real (desviado por 3 en la prueba); sin el trigger, 2/2 corridas correctas. No se
  investigó la causa raíz exacta a nivel de driver (podría ser el driver ODBC/PDO_SQLSRV con
  múltiples result sets cuando el trigger inserta en otra tabla con `IDENTITY` propia), pero
  el riesgo es real y grave: si `ctx.erp.NuevoDocumento()` (o cualquier código de
  BrosLMV/XEngine) obtiene internamente el `DocumentID` por este mismo mecanismo, un trigger
  puesto por CUALQUIER cosa (un cliente, un script de un usuario, una futura feature de
  BrosLMV) podría asociar partidas al documento equivocado, silenciosamente, sin ningún error.
  el CRM externo descartó ese diseño por esto mismo. **Advertencia dura agregada a `MANUAL.md`
  §12** (nueva sección ⚠️): nunca agregar un trigger `AFTER INSERT` sobre `docDocument` ni
  ninguna tabla que XEngine use para crear documentos.
- **Un producto con historial de kardex corrupto puede colgar `ctx.erp.AgregarArticulo`/`Save`
  indefinidamente, sin error ni timeout.** Ya conocido y documentado del lado del CRM externo
  (documentación del CRM externo) para un caso
  puntual (kardex mezclado por INSERTs SQL crudo previos a la integración; descartado que
  fuera retención de impuestos/`TaxTypeID` — se probó con y sin retención, mismo resultado;
  con un producto sin ese historial corrupto, funciona perfecto), pero es un riesgo sistémico
  de cualquier script que use `ctx.erp` de escritura: **no hay ningún timeout/watchdog** si el
  motor se cuelga por datos corruptos de un cliente real — hubo que matar el proceso a mano.
  Registrado en `MANUAL.md` §12 como límite conocido; **mejora futura** (no en esta sesión):
  evaluar un timeout duro en `BrosLMV.Runner`/`ctx.erp` de escritura para no depender de un
  operador matando el proceso a mano.

### Confirmado (decisión de producto — ver `ESTADO.md`)
- **`ctx.erp` de escritura headless (vía `BrosLMV.Runner`) es seguro de usar incluso con
  `ComercialSP.exe` abierto y en uso activo por personal real al mismo tiempo.** Varias
  entradas de `ESTADO.md` marcaban esto como "decisión de producto pendiente". el CRM externo
  aporta la primera evidencia real en producción (no sandbox): el Runner corrió manualmente
  mientras dos instancias de `ComercialSP`/`ComercialSP.bin`/`ComercialSP.mgr` estaban activas,
  sin conflicto, creando Entradas/Salidas de almacén, Órdenes de Compra (con
  impuestos/retenciones/kardex de compromiso) y Recepciones de Compra (con
  `DeliverDocumentItemID` cerrando el compromiso de la OC) reales, validados campo por campo
  contra documentos nativos. Anotado en `ESTADO.md` como el primer caso real de este tipo.

## `BrosLMV.Runner` v0.3.0 — auto-sanado de ProgID `XengineLib.clsMain` de 32 bits (2026-08-07), sin versión de addon

> Bug de despliegue real, encontrado y confirmado (por registro de Windows, no supuesto) en
> un consumidor externo de `BrosLMV.Runner.exe`: **un CRM externo** (proyecto
> separado), que usa el Runner como puente headless hacia XEngine desde
> hace varios días en producción real. Ahí el Runner fallaba **SIEMPRE** con `ERROR al crear
> XEngine standalone: No se encontró "XengineLib.clsMain" registrado en este equipo`, aun con
> Comercial Pro instalado y funcionando con normalidad.

### Corregido
- **Causa raíz confirmada en ese servidor:** el CLSID de `XengineLib.clsMain`
  (`{D5255125-CD90-48A8-BC48-762BE8531B5D}`) sí estaba bien registrado en la vista de 32 bits
  (`HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\{D5255125-...}\InprocServer32` →
  `XEngineLib.dll`), pero el **mapeo ProgID→CLSID** (`XengineLib.clsMain`) solo existía en la
  vista de **64 bits** (`HKLM\SOFTWARE\Classes\XengineLib.clsMain`) — la vista de 32 bits
  (`HKLM\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain`) no existía. `BrosLMV.Runner` es un
  proceso de 32 bits (`PlatformTarget=x86`, correcto — `XEngineLib.dll` solo tiene COM de 32
  bits) y activa XEngine standalone por ProgID (`Type.GetTypeFromProgID("XengineLib.clsMain")`
  en `CrearXEngineStandalone`, `runner\Program.cs`) — sin el mapeo en `WOW6432Node`, la
  activación falla aunque la clase real esté perfectamente registrada. Depende de cómo el
  **instalador de Comercial Pro** registró `XEngineLib.dll` en ese equipo (no de nada que
  controle BrosLMV), así que puede repetirse en cualquier instalación del Runner en cualquier
  servidor/cliente.
- **`AsegurarProgIdXEngine32Bits()`** (nuevo, `runner\Program.cs`), llamado desde
  `CrearXEngineStandalone` antes de `Type.GetTypeFromProgID`: si el ProgID de 32 bits no
  existe pero sí existe el de 64 bits, copia el mapeo (`(default)` + subclave `CLSID`) a
  `WOW6432Node\Classes\XengineLib.clsMain` — mismo patrón que `instalador\Instalar.ps1` (paso
  7) ya usa para el ProgID propio (`BrosLMV.clsMain`). Es *best-effort*: envuelto en
  `try/catch` porque escribir en `HKLM` requiere permisos de administrador (el Runner
  normalmente corre elevado o como SYSTEM vía Tarea Programada, pero si no los tiene, no
  bloquea nada — simplemente deja pasar y el error normal de `GetTypeFromProgID` sale más
  abajo, ahora con un mensaje ampliado que apunta a `MANUAL.md` §12). Si el ProgID no existe en
  ninguna de las dos vistas, no toca nada (no es este bug conocido).
- Mensaje de error de `CrearXEngineStandalone` ampliado con la pista de `MANUAL.md` §12 cuando
  `GetTypeFromProgID` regresa `null`.

### Validado
- Los 33 casos de `build\probar_humo.ps1` (contra el sandbox real, `localhost\compac` /
  `ComercialSP`) siguen en verde después del cambio — incluyendo los que usan `ctx.erp`
  (creación real de documentos vía XEngine standalone), confirmando que el auto-sanado no
  rompe el camino existente.
- **Nota de honestidad, no se ocultó:** en el sandbox de este repo (Windows Server 2025,
  máquina de desarrollo) el ProgID de 32 bits de `XengineLib.clsMain` **tampoco existe** (se
  confirmó leyendo el registro directamente) y sin embargo `CrearXEngineStandalone` funciona
  de todas formas — es decir, esta máquina NO reproduce la falla que sí ocurrió en el servidor
  del CRM externo, probablemente por una diferencia de comportamiento del redirector de
  registro WOW64 entre versiones/configuraciones de Windows que no se investigó a fondo (fuera
  de alcance de esta sesión). El fix se deja de todas formas porque es puramente defensivo
  (no-op cuando no hace falta, confirmado por los 33/33 casos de humo en verde) y ataja
  exactamente la causa raíz confirmada por registro en el servidor real donde SÍ se reprodujo.
- **Pendiente real:** no se pudo probar este fix específico contra el servidor del CRM externo
  (ya se corrigió ahí a mano, por SSH, antes de que existiera este fix) ni contra ningún otro
  servidor con la falla real presente — la próxima vez que se despliegue el Runner en un
  equipo nuevo es la oportunidad de confirmarlo end-to-end.

