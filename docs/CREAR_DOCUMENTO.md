# Crear documento (plantillas de ejemplo)

Cuatro plantillas de fábrica, **la misma ventana y la misma lógica** en cuatro sabores, para que tomes la que más se parezca a lo que sabes programar:

| Plantilla (Consola → Plantillas → Documentos) | Lenguaje | Ventana |
|---|---|---|
| **Crear documento (C# · ventana HTML)** — `CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx` | C# | HTML (WebView2) |
| **Crear documento (C# · ventana Windows Forms)** — `CREAR_DOCUMENTO_CSHARP_WINFORMS.ctx` | C# | Windows Forms |
| **Crear documento (Python · ventana HTML)** — `CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py` | Python | HTML (WebView2) |
| **Crear documento (Python · ventana Windows Forms)** — `CREAR_DOCUMENTO_PYTHON_WINFORMS.py` | Python | Windows Forms (pythonnet) |

Crean los seis documentos más comunes: **factura de cliente, pedido, remisión, factura de compra, orden de compra y recepción de compra**.
Son ejemplos **funcionales**: sirven tal cual, y sirven para copiar. Nada va escrito a mano por empresa (almacenes, clientes, proveedores, condiciones, impuestos y productos salen de tu base).

## La ventana: una estación de captura

La versión **HTML** (C# y Python) y la de **Windows Forms** de C# tienen **diseños propios**: la HTML aprovecha la web (tarjetas, consultas en vivo), y la de Windows Forms toma el estilo clásico de un documento de Comercial (cinta oscura con acciones e información del documento, grupos numerados, la etiqueta arriba de cada campo para que nada se encime ni se corte, tabla de partidas editable y totales con importe en letra). Las dos capturan lo mismo y comparten el mismo núcleo:

- **Cinta de acciones** (arriba): **Guardar y abrir** (F5), **Guardar y nuevo** (F6, para capturar varios seguidos sin cerrar la ventana), **Limpiar** y **Cancelar** (Esc). Junto a ellas, la **información del documento**: fecha, folio probable y almacén.
- **Tipo de documento** en dos grupos, **Ventas** y **Compras** (cada grupo con su color).
- **1 · Cliente o proveedor** con búsqueda por nombre, RFC o clave (F2). Al elegirlo ves su **RFC, saldo abierto, límite de crédito, descuento habitual y último documento**; se propone su **condición de pago**; y se aplica su descuento habitual a las partidas nuevas de una venta.
- **Moneda, centro de costo y datos fiscales:** **moneda** con su **tipo de cambio** del catálogo (editable; con pesos queda fijo en 1), **centro de costo** y, en la **factura de cliente**, los datos del **CFDI**: **uso del CFDI** (el habitual del cliente), **forma de pago** y **método de pago** (con crédito propone PPD y forma 99; de contado, PUE y efectivo; si eliges PPD la forma pasa a 99, como exige el SAT). Se guardan en el documento (`docDocumentCFD`).
- **2 · Partir de un documento ya existente**: los pendientes de surtir **de esa persona** salen solos al elegirla (no hace falta seleccionar nada antes en la lista; lo seleccionado se sigue marcando automáticamente).
- **3 · Partidas** con búsqueda por nombre, clave o **código de barras** (escanea y Enter, F3), **existencia del almacén** en cada producto y marcas de lote o serie. Cantidad, precio, descuento e impuesto editables; el total se recalcula al instante.
- **Resumen** (partidas, piezas, subtotal, descuento, impuestos y total) con **avisos**: excede el límite de crédito, piden más de la existencia, precio en cero. También **barra de crédito** y los **últimos documentos** de la persona (clic para abrirlos en Comercial).
- **Siempre se abre el documento nativo de Comercial** al guardar. Si no se pudiera abrir, la ventana lo dice y te da el folio para buscarlo.
- **No bloquea Comercial**: la ventana se puede minimizar y se sigue trabajando.

La versión **HTML** consulta a Comercial **en vivo** mientras capturas (pendientes y últimos documentos de la persona) mediante `ctx.ShowHtmlModeless` y respuestas `__JS__…` (ver [`UI_VENTANAS.md`](UI_VENTANAS.md)); la versión de **Python en HTML** abre la misma página pero con lo precargado (no tiene consultas en vivo ni «Guardar y nuevo»). La versión **Windows Forms** de C# es no modal y trae además el botón **Historial** (últimos documentos de la persona; doble clic abre el documento en Comercial). La de **Python en Windows Forms** sigue con la ventana sencilla de antes.

## Lo que solo una ventana web puede dar (WebView2)

Las dos plantillas **WebView2** (C# y Python) hablan **en vivo** con Comercial y traen herramientas de una aplicación moderna; todo está en el mismo formulario y lo hacen igual con C# o con Python (con Python, ver [`UI_VENTANAS.md`](UI_VENTANAS.md) §5):

- **Inteligencia de la persona** (consulta en vivo al elegirla): **gráfica de los últimos 12 meses** (barras SVG con promedio), total y ticket promedio, **días promedio de pago**, y los **productos que más maneja con su último precio**: un clic en «+» los agrega con ese precio y descuento. En la búsqueda de productos aparece «últ.» con el último precio de esa persona.
- **Repetir último:** carga las partidas del último documento de ese tipo de la persona (con sus precios), con confirmación.
- **Margen estimado en vivo** (utilidad y % sobre la venta neta con el costo del catálogo, y cuántas partidas van bajo costo) y **Revisión previa** con marcas de «todo en orden / por corregir»: persona, partidas, precios en cero, existencia, límite de crédito, datos del CFDI (con PPD la forma debe ser 99, con PUE no puede serlo), formato del RFC, fecha futura y tipo de cambio.
- **Paleta de comandos (Ctrl+K):** una sola caja para ejecutar acciones, cambiar de tipo de documento, **buscar personas y productos** (Enter los elige o los agrega).
- **Pegar desde Excel (Ctrl+Shift+V)** o **arrastrar un CSV/TXT** a la ventana: clave o código de barras, cantidad y precio (opcional); avisa cuáles no encontró.
- **Vista previa imprimible (Ctrl+P)** con el importe en letra, lista para imprimir.
- **Deshacer y rehacer (Ctrl+Z / Ctrl+Y)** sobre las partidas.
- **Borrador automático:** lo capturado se guarda en la carpeta local de datos (`%LOCALAPPDATA%\BrosLMV\borradores`); si la ventana se cierra o Comercial se cae, al abrirla te ofrece **recuperarlo** (7 días). Al guardar el documento se borra.
- **Tema claro u oscuro** (se recuerda) y atajos de teclado en todo.

## Cómo usarlas

1. En la **Consola** abre la plantilla que prefieras, guárdala como botón (nombre sugerido `CREAR_DOCUMENTO`) y ponla en el ribbon con *Crear botón…*.
2. Pulsa el botón. Elige el **tipo de documento** arriba.
3. Escribe el **cliente o proveedor** (nombre, RFC o clave; F2), elige almacén, condición de pago, fechas y, si quieres, título y comentarios.
4. Agrega **partidas**: busca el producto por nombre o clave y presiona Enter. Edita cantidad, precio, descuento (%) e impuesto de cada partida; el total estimado se actualiza solo.
5. **Guardar y abrir** (F5). Se crea, se refresca la lista y el documento se abre en Comercial. Con **Guardar y nuevo** (F6) se crea, se abre y la ventana queda lista para el siguiente.
6. Si algo falla, la ventana **no se pierde lo capturado** (HTML) o se queda abierta (Windows Forms) y el mensaje dice qué corregir.

### Documentos derivados (partir de otro documento)

Antes de pulsar el botón **selecciona en la lista** uno o varios documentos de origen:

| Quieres crear… | Selecciona antes… | Se carga… |
|---|---|---|
| Recepción de compra | una o varias **órdenes de compra** | lo que falta por **recibir** de cada partida |
| Factura de compra | una o varias **órdenes de compra** | lo que falta por **facturar** de cada partida |
| Remisión | un **pedido** | lo que falta por **remitir**, por producto |

En la ventana aparece el recuadro **«Partir de un documento ya existente»** con tus selecciones; márcalas y se cargan las partidas pendientes (la cantidad no puede pasar de lo pendiente).
Puedes combinar **varias órdenes de compra del mismo proveedor** en **un solo** documento: cada partida queda ligada a **su** partida de origen. Es el caso «varias OC → una factura» (véase [`TRAZABILIDAD_DOCUMENTO.md`](TRAZABILIDAD_DOCUMENTO.md)).
Lo pendiente se cuenta **por tipo**: una orden de compra recibida completa sigue apareciendo como pendiente de facturar.

## Qué hace por dentro (el patrón de creación)

```
NuevoDocumento → UPDATE del perfil del módulo → AgregarArticulo × N → RecalcCompleto → AffectStockNEW (solo si el módulo lo pide)
→ Save → agenda de pago → UpdateDocumentPaidInfo → UpdateStatusDelivery (derivados)
```

| Tipo | Módulo | Perfil que se pone al encabezado | Notas |
|---|---|---|---|
| Factura de cliente | 21 | `DepotIDFrom=0, StatusDeliveryID=0` | Condición de pago; no mueve inventario. |
| Pedido de cliente | 967 | `DepotIDFrom=0, StatusDeliveryID=3, StatusPaidID=0` | Condición de pago y fecha de entrega; **compromete** inventario. |
| Remisión | 157 | `DepotIDFrom=0, PaymentTermID=0, StatusDeliveryID=0` | Sale de inventario; costo = promedio del producto; liga al pedido por `SourceDocumentID`. |
| Factura de compra | 152 | `DepotIDFrom=0, StatusPaidID=3` | Cada partida liga su origen en `SourceDocumentItemID`. |
| Orden de compra | 183 | `DepotIDFrom=0, UserID=0` | Condición de pago y fecha de entrega; compromete inventario. |
| Recepción de compra | 184 | `DepotIDFrom=0, UserID=0, PaymentTermID=0, StatusDeliveryID=0` | Entra a inventario; cada partida liga su origen en `DeliverDocumentItemID`. |

- **Inventario:** lo decide el **módulo** (`engModuleParameter.StockAffectation ≠ 0`), no el tipo de documento ni una lista escrita en el script (véase MANUAL §7).
- **Agenda de pago:** `NuevoDocumento` deja una parcialidad «de relleno» con importe 0. Después de guardar se **rehace** con el total real: cada renglón de la condición (`engPaymentTermDetail`) vence en *fecha + unidades × días del periodo* y lleva su porcentaje; la última parcialidad absorbe el redondeo.
  Una condición «50%-50% a 3 meses» da dos parcialidades que suman el total.
- **Descuento:** se captura en %, por partida; Comercial aplica el descuento al recalcular. El total que muestra la ventana es un **estimado**.
- **Precio sugerido:** ventas → lista de precios del producto; compras → su costo. Siempre editable.
- **Póliza contable:** un documento creado por script **no** genera póliza sola; si la necesitas, véase MANUAL §12.

## Para adaptarlas

- **Un botón de un solo tipo** (por ejemplo solo «Orden de compra»): en la tabla `TIPOS` (arriba del código) deja esa fila y borra las demás. Nada más depende de la tabla.
- **Agregar otro tipo:** copia una fila de `TIPOS` y ajusta módulo, perfil y vínculo; revisa en tu base qué perfil espera ese módulo (MANUAL §7).
- **Cambiar los campos de la ventana:** el formulario HTML es **idéntico** en las dos variantes HTML (la de C# y la de Python); las de Windows Forms replican lo mismo con controles.
- Las funciones `CrearDocumento` / `crear_documento` son **independientes de la ventana**: puedes llamarlas desde tu propio script con un diccionario de datos (`tipo`, `almacen`, `entidad`, `condicion`, `fecha`, `entrega`, `titulo`, `comentarios`, `origenes`, `partidas`).

## Qué garantiza

- Valida antes de crear (sin partidas, sin almacén, cantidad cero, descuento fuera de 0–100) y explica qué corregir.
- Si algo falla **después** de crear el documento, el mensaje dice el número del documento incompleto para que lo elimines o canceles; nunca queda un borrador «perdido».
- Un documento creado se **abre** y la lista se **refresca** (regla de BrosLMV: todo script que crea un documento lo abre).

## Límites

- Catálogo de productos: hasta **30,000** productos se cargan en la ventana. Con más, usa un filtro propio.
- No maneja lotes, series, monedas distintas a la de la empresa, descuentos globales ni retenciones por partida (se usa el impuesto del catálogo).
- La remisión por **pedido** cuenta lo surtido por producto (no por partida), como lo hace el flujo nativo.
- Un cliente o proveedor solo aparece si existe como tal (`orgCustomer` / `orgSupplier`).

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| El cliente (o proveedor) no aparece | No está dado de alta como cliente/proveedor, o está eliminado. |
| «Los documentos de origen son de entidades distintas» | Seleccionaste órdenes de compra de proveedores distintos; deja las de un solo proveedor. |
| «Quedó un documento incompleto (id N)» | Falló un paso después de crearlo (por ejemplo un producto sin impuesto). Elimina o cancela ese borrador y corrige la causa del mensaje. |
| La recepción/factura no muestra nada pendiente | La orden de compra ya se recibió/facturó completa (los pendientes se cuentan por tipo). |
| No se abre la ventana (Python) | Revisa `C:\BrosLMV\logs\PythonErp_AAAAMMDD.txt`; la línea «INICIA» sin su «termina» marca la llamada atorada. |

## Para desarrolladores

Los archivos `.ctx` / `.py` de `instalador/scripts` se **generan**: el núcleo (tipos, catálogos, pendientes, creación) y el formulario HTML viven **una sola vez** en `build/plantillas_documentos/`
y `python build/plantillas_documentos/generar.py` los ensambla en las cuatro variantes. **Edita las piezas y regenera**; no edites a mano los archivos generados.

**Pruebas.**

- C#: `build/humo/casos/39_crear_documento.ps1` corre la plantilla real con `BrosLMV.Runner` contra el laboratorio (`BROSLMV_DESARROLLO`) y **crea de verdad** los seis tipos (orden de compra con descuento e IVA, recepción parcial, factura de compra, factura de cliente, pedido con condición 50%-50% y remisión), comprobando totales, vínculos por partida,
  pendientes por tipo, kardex y agenda de pago. Los documentos quedan en el laboratorio con título «DEMO CREAR DOC…».
  Variables de modo de pruebas: `BROSLMV_DOC_TEST` (JSON con el documento, o `{"pendientes":true,"seleccion":[ids]}` o `{"catalogo":true}`), `BROSLMV_DOC_OUT` (archivo de resultado) y `BROSLMV_DOC_HTML` (escribe la página HTML y no abre ventana).
- Python: no hay un Runner de Python; `python build/plantillas_documentos/prueba_python.py [plantilla.py]` simula el módulo `broslmv` (lee el laboratorio de verdad, **solo registra** lo que se escribiría) y comprueba catálogos, pendientes, la secuencia exacta de llamadas de creación (incluida la agenda) y que el cuerpo de la ventana se arma.
  **Los clics de las ventanas (HTML y Windows Forms) se prueban a mano dentro de Comercial.**
- La página HTML se probó en el navegador integrado: tipos, búsqueda, partidas, totales (descuento + IVA), envío del documento y cambio de tipo.
