# Plantillas: notas para quien programa

Las guías que ve la persona usuaria («Ver documentación» en la Consola) hablan de la **plantilla**: qué hace, cómo se usa, qué registra y sus límites. 
Todo lo que sirve para **mantener o probar el código** vive aquí, fuera del código y de las guías. Los archivos de `instalador/scripts` se entregan **sin comentarios**.


## TRAZABILIDAD_DOCUMENTO

### Reglas de BrosLMV que cumple

- **Empresa activa:** todas las consultas filtran por `OwnedBusinessEntityID` del contexto.
- **Módulos por naturaleza:** clasifica con los parámetros del módulo y `ModuleIDBase`; funciona con los clones.
- **Eliminados:** un documento o partida con `DeletedOn` no se sigue; un documento **cancelado** sí aparece, marcado.
- **Ventana modeless** (`ctx.ShowHtmlModeless`): abrir un documento no choca con XEngine («the other application is busy»).

### Ver un ejemplo en el laboratorio

En `BROSLMV_DESARROLLO`: los documentos **«DEMO TRAZ»** (tres órdenes de compra → una factura, con el vínculo manual) y los **«DEMO CREAR DOC»** (orden → recepción parcial → factura).
`build/laboratorio/demo_trazabilidad_detalle.sql` les agrega **lotes con caducidad, series, un pedimento y una conversión de unidad** para ver el detalle completo.

### Para desarrolladores

- El motor arma un modelo (`nodos` con sus `partidas`, `pagos`, etapa y carril; `aristas`; `avisos`) con consultas por lotes y luego se dibuja en el navegador embebido; todo lo demás es JavaScript local.
- **Probar sin ventanas:** `BROSLMV_TRAZA_DOC=<DocumentID>` hace que el script no abra la ventana y escriba el modelo JSON en `BROSLMV_TRAZA_OUT` y la página en `BROSLMV_TRAZA_HTML`.
  Corre en `BrosLMV.Runner` (lleva `// job: safe-offline`) y en la prueba de humo `build/humo/casos/35_trazabilidad_documento.ps1`.
- **Editar sin compilar una versión nueva:** `build/laboratorio/publicar_scripts_lab.ps1` publica las plantillas como **scripts** del laboratorio (con su hash, sin avisos de «modificado por fuera»);
  se edita el archivo de `instalador/scripts`, se vuelve a correr y se ejecuta otra vez el botón en Comercial.
- La ventana usa `ctx.ShowHtmlModeless`: el script termina y cada acción (`abrir`, `csv`) llega a un manejador en el hilo de Comercial.


## ASIGNAR_CENTRO_COSTO

### Reglas de BrosLMV que cumple

- **Empresa activa:** el catálogo de centros, los módulos y los documentos salen de `OwnedBusinessEntityID`; nada va escrito a mano.
- **Transacción y bitácora:** nada queda a medias y todo se puede deshacer.
- **Conexión propia** (`ctx.OpenConn`) para escribir por lotes; `RefreshGrid` una sola vez al final.
- **Probada:** `build/humo/casos/36_asignar_centro_costo.ps1` corre la plantilla real contra el laboratorio (`BROSLMV_DESARROLLO`) (vista previa, solo-vacíos, repetir, reemplazar, documento en uso, deshacer en orden inverso, partidas) y lo deja todo como estaba.

### Para desarrolladores

Con la variable de entorno `BROSLMV_CC_TEST` (JSON con `accion` = `previsualizar` | `aplicar` | `deshacer` y los mismos parámetros del formulario) el script no abre ventanas y escribe el resultado en el archivo de
`BROSLMV_CC_OUT`. Así corre en `BrosLMV.Runner` (lleva `// job: safe-offline`).


## PDF_MASIVO_DOCUMENTOS

### Para desarrolladores

**Modo lote del motor (`BrosLMV.HtmlToPdf.exe`).**

```
BrosLMV.HtmlToPdf.exe --lote <manifiesto.txt> [--timeout-doc <seg>] [--unir <salida.pdf>] [--zip <salida.zip>]
```

- El manifiesto tiene una línea por documento: `<entrada.html><TAB><salida.pdf>`.
- Una sola instancia de WebView2 para todo el lote; un documento que falla no detiene a los demás.
- La salida estándar (UTF-8) trae una línea por documento: `n/total<TAB>OK|ERR<TAB>pdf[<TAB>motivo]`, y al final `UNIDO<TAB>ruta` y/o `ZIP<TAB>ruta` (o `ERRUNIR`/`ERRZIP`).
- Códigos de salida: `0` todo bien · `7` hubo documentos con fallo · `1`, `3`, `5`, `6` como en el modo normal.
- `--soporta-lote` imprime `lote=1` y sale con 0: así una plantilla sabe si el motor instalado admite lotes.
- Unir usa PDFsharp (MIT); comprimir, `System.IO.Compression`.

**Modo de pruebas de la plantilla (sin ventanas).** Con `BROSLMV_PDFM_TEST` (JSON con `ids`, `modo`, `carpeta`, `patron`) y `BROSLMV_PDFM_OUT` (archivo de resultados) corre en `BrosLMV.Runner` (lleva `// job: safe-offline`).
`BROSLMV_HTMLTOPDF_EXE` fuerza qué motor usar. Prueba de humo: `build/humo/casos/37_pdf_masivo.ps1`.


## ESTADO_CUENTA_CLIENTES

### Para desarrolladores

Las dos plantillas se **generan** de una sola fuente: `build/saldos/estado_cuenta.tpl.ctx` con `python build/saldos/generar.py` (no se editan a mano los `.ctx` generados).
**Prueba de humo #42** (`build/humo/casos/42_estado_cuenta_excel.ps1`): genera el libro con 6,000 documentos sintéticos (o los que pidas con `-Documentos`) en las dos plantillas, lo valida con el validador oficial de Open XML y comprueba hojas, gráficas y filas. Variables: `BROSLMV_SALDOS_XLSX_TEST` (JSON que mandaría la ventana) y `BROSLMV_SALDOS_XLSX_OUT`.
**Modo de pruebas (sin ventanas).** Con `BROSLMV_SALDOS_TEST` (JSON, por ejemplo `{"meses":12}`) corre en `BrosLMV.Runner` (lleva `// job: safe-offline`) y escribe el modelo completo (documentos y pagos) en el archivo de `BROSLMV_SALDOS_OUT`
y el HTML de la ventana en `BROSLMV_SALDOS_HTML`. Prueba de humo: `build/humo/casos/38_saldos_estados_cuenta.ps1` (corre las dos plantillas).
Datos de demostración en el laboratorio: `build/laboratorio/sembrar_demo_saldos.ps1` (10 facturas «DEMO SALDOS…» con cobros y pagos aplicados, en `BROSLMV_DESARROLLO`).


## ESTADO_CUENTA_PROVEEDORES

### Para desarrolladores

Las dos plantillas se **generan** de una sola fuente: `build/saldos/estado_cuenta.tpl.ctx` con `python build/saldos/generar.py` (no se editan a mano los `.ctx` generados).
**Prueba de humo #42** (`build/humo/casos/42_estado_cuenta_excel.ps1`): genera el libro con 6,000 documentos sintéticos (o los que pidas con `-Documentos`) en las dos plantillas, lo valida con el validador oficial de Open XML y comprueba hojas, gráficas y filas. Variables: `BROSLMV_SALDOS_XLSX_TEST` (JSON que mandaría la ventana) y `BROSLMV_SALDOS_XLSX_OUT`.
**Modo de pruebas (sin ventanas).** Con `BROSLMV_SALDOS_TEST` (JSON, por ejemplo `{"meses":12}`) corre en `BrosLMV.Runner` (lleva `// job: safe-offline`) y escribe el modelo completo (documentos y pagos) en el archivo de `BROSLMV_SALDOS_OUT`
y el HTML de la ventana en `BROSLMV_SALDOS_HTML`. Prueba de humo: `build/humo/casos/38_saldos_estados_cuenta.ps1` (corre las dos plantillas).
Datos de demostración en el laboratorio: `build/laboratorio/sembrar_demo_saldos.ps1` (10 facturas «DEMO SALDOS…» con cobros y pagos aplicados, en `BROSLMV_DESARROLLO`).


## CREAR_DOC_DESDE_XML

### 7. Pruebas antes de operar (sandbox)

Un XML por camino: proveedor existente; proveedor nuevo; producto nuevo con cada opción; gasto con
retención de ISR; composición fiscal sin tipo de impuesto (debe bloquear); descuento en partida;
plazo en parcialidades. Comparar contra el mismo documento capturado a mano.

### 8. Registro en la empresa

La plantilla del editor no crea acción por sí sola: se registra con `BrosGuardar` (conserva SHA-256 e
historial en `zzBrosScriptHist`) y la Consola debe recargar su árbol para mostrarla.


## CONFIGURACION_FORMATO

### Para desarrolladores

- El motor de etiquetas está **copiado** en `ConfiguracionFormato.ctx`, `Cotizador.ctx` y `PDF_MASIVO_DOCUMENTOS.ctx`; la prueba de humo compara que den el mismo HTML. Lo ideal a futuro es una sola función del SDK.
- Acciones del puente: `editorAbrir`, `editorVista`, `editorGuardar`, `editorEtiquetas`, `disenoValores`, `disenoLogo`, `esquemaTablas`, `esquemaColumnas`, `refProbar`, `refGuardar`.
- `ConfiguracionFormato.ctx` se **genera**: edita las piezas de `build/disenador_formatos/` (`base.ctx`, `motor.cs`, `acciones.cs`, `disenador.css/.html/1-3.js`) y corre `python build/disenador_formatos/generar.py`. Prueba de humo: `build/humo/casos/41_disenador_formatos.ps1`.
- Para editarlo sin generar versión nueva: `build/laboratorio/publicar_scripts_lab.ps1 -Scripts ConfiguracionFormato` actualiza el código del botón en `BROSLMV_DESARROLLO` (con su hash).


## CREAR_DOCUMENTO

### Qué hace por dentro (el patrón de creación)

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

### Para adaptarlas

- **Un botón de un solo tipo** (por ejemplo solo «Orden de compra»): en la tabla `TIPOS` (arriba del código) deja esa fila y borra las demás. Nada más depende de la tabla.
- **Agregar otro tipo:** copia una fila de `TIPOS` y ajusta módulo, perfil y vínculo; revisa en tu base qué perfil espera ese módulo (MANUAL §7).
- **Cambiar los campos de la ventana:** el formulario HTML es **idéntico** en las dos variantes HTML (la de C# y la de Python); las de Windows Forms replican lo mismo con controles.
- Las funciones `CrearDocumento` / `crear_documento` son **independientes de la ventana**: puedes llamarlas desde tu propio script con un diccionario de datos (`tipo`, `almacen`, `entidad`, `condicion`, `fecha`, `entrega`, `titulo`, `comentarios`, `origenes`, `partidas`).

### Para desarrolladores

Los archivos `.ctx` / `.py` de `instalador/scripts` se **generan**: el núcleo (tipos, catálogos, pendientes, creación) y el formulario HTML viven **una sola vez** en `build/plantillas_documentos/`
y `python build/plantillas_documentos/generar.py` los ensambla en las cuatro variantes. **Edita las piezas y regenera**; no edites a mano los archivos generados.

**Pruebas.**

- C#: `build/humo/casos/39_crear_documento.ps1` corre la plantilla real con `BrosLMV.Runner` contra el laboratorio (`BROSLMV_DESARROLLO`) y **crea de verdad** los seis tipos (orden de compra con descuento e IVA, recepción parcial, factura de compra, factura de cliente, pedido con condición 50%-50% y remisión), comprobando totales, vínculos por partida,
  pendientes por tipo, kardex y agenda de pago. Los documentos quedan en el laboratorio con título «DEMO CREAR DOC…».
  Variables de modo de pruebas: `BROSLMV_DOC_TEST` (JSON con el documento, o `{"pendientes":true,"seleccion":[ids]}` o `{"catalogo":true}`), `BROSLMV_DOC_OUT` (archivo de resultado) y `BROSLMV_DOC_HTML` (escribe la página HTML y no abre ventana).
- Python: no hay un Runner de Python; `python build/plantillas_documentos/prueba_python.py [plantilla.py]` simula el módulo `broslmv` (lee el laboratorio de verdad, **solo registra** lo que se escribiría) y comprueba catálogos, pendientes, la secuencia exacta de llamadas de creación (incluida la agenda) y que el cuerpo de la ventana se arma.
  **Los clics de las ventanas (HTML y Windows Forms) se prueban a mano dentro de Comercial.**
- La página HTML se probó en el navegador integrado: tipos, búsqueda, partidas, totales (descuento + IVA), envío del documento y cambio de tipo.


## COBRO_PAGO

### Qué escribe (la receta de siete tablas)

Se crea **una sola operación** (un folio) con **un renglón por documento y parcialidad**, todo en **una sola transacción** (igual que la pantalla nativa de Tesorería):

| Tabla | Qué guarda |
|---|---|
| `docFinancialOperation` | La operación: módulo **248** (cobro) o **247** (pago), cliente o proveedor, cuenta, forma de pago, fecha, importe total y folio (`COB-n` / `PAG-n`), con su **moneda, tipo de cambio** (`Rate`, `AmountRate`), el **signo** (`DebitCreditCoef`: +1 cobro, −1 pago), el importe en la moneda de la cuenta (`FinancialEntityAmount`), la descripción, el importe en letra y el nombre de la persona. Como en lo nativo, `DocumentID`, `PartialityNumber` y `PartialityTotal` van en 0. |
| `docDocumentPayment` | **Un renglón por documento y parcialidad:** importe **en la moneda del documento**, su tipo de cambio (`Rate`), el valor en pesos (`AmountPaidCurrency` = importe × tipo de cambio), el número de **parcialidad**, **saldo anterior** y **saldo insoluto**. |
| `docDocumentPaymentEspejo` | El espejo de la aplicación (mismo id; la columna no es identity). |
| `docBankTransfer` | La transferencia bancaria con la referencia (**solo** si la forma de pago no es efectivo). |
| `docFinancialOperationTaxDetail` | El **reparto proporcional de impuestos**: lo aplicado entre el total del documento, aplicado a cada renglón de impuesto. |
| `docDocument` | `TotalPaid`, `Balance` y `StatusPaidID` (1 = pagado, 2 = parcial). |

- **Folio sin choques:** el siguiente folio se calcula con un **candado de transacción** (`sp_getapplock`), así dos cobros simultáneos nunca reciben el mismo.
- **Saldo revalidado:** dentro de la transacción se vuelve a comprobar el saldo; si alguien más aplicó algo mientras capturabas, **no se aplica de más** y se explica.
- **Reglas:** solo se aplica a documentos que **suman saldo** (facturas, notas de cargo, gastos; las notas de crédito no se cobran ni se pagan) del cliente/proveedor elegido; no se puede aplicar más que el saldo.
- **Qué documentos cuentan** como cuentas por cobrar o por pagar se lee de los parámetros del módulo (`DocRecipient`, `FinancialAffectation`), sin lista escrita a mano; los módulos clonados cuentan igual.

### Para desarrolladores

Los archivos de `instalador/scripts` se **generan** (como las plantillas de [«Crear documento»](CREAR_DOCUMENTO.md)): el núcleo (`nucleo_pagos.cs.part` / `.py.part`) y el formulario (`formulario_pagos.html.part`) viven una sola vez en `build/plantillas_documentos/`;
`python build/plantillas_documentos/generar.py` arma las cuatro variantes. **Edita las piezas y regenera.**

**Pruebas.**

- C#: `build/humo/casos/40_cobro_pago.ps1` crea documentos nuevos con la plantilla de «Crear documento» y les aplica **de verdad** cobros y pagos en `BROSLMV_DESARROLLO`: parcial por transferencia (comprueba las siete tablas, el IVA proporcional y el estatus parcial),
  liquidación en efectivo (sin transferencia), un pago a dos facturas, un sobrepago y un documento ajeno (rechazados sin cambios) y las validaciones. Variables: `BROSLMV_PAGO_TEST` (JSON con el movimiento o `{"catalogo":true}`), `BROSLMV_PAGO_OUT` (archivo de resultado), `BROSLMV_PAGO_HTML` (escribe la página y no abre ventana).
- Python: `python build/plantillas_documentos/prueba_python_pagos.py [plantilla.py]` simula `broslmv` (lee el laboratorio de verdad, **solo registra** el SQL) y comprueba catálogos, la receta (piezas y saldo nuevo), efectivo sin transferencia, cobro a cliente, los rechazos sin escribir y que la ventana se arma.
  **Los clics de las ventanas se prueban a mano dentro de Comercial.** La página HTML se probó en el navegador integrado.
