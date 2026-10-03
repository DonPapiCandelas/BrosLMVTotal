# Trazabilidad del documento

Plantilla de fábrica **Plantillas → Trazabilidad → Trazabilidad del documento** (`TRAZABILIDAD_DOCUMENTO.ctx`). Muestra, para cualquier documento de Comercial, **de dónde viene y a dónde fue**,
en las dos direcciones, ordenado como el flujo real del negocio, y debajo el **detalle del documento que elijas**: sus partidas, lo que viene de dónde, lo que se surtió, lotes, series, pedimentos,
conversiones de unidad y pagos o cobros. Funciona con todos los módulos, incluidos los clonados.

**Solo lee.** No cambia ningún documento ni ninguna tabla.

## Cómo usarla

1. En la **Consola** abre la plantilla y guárdala como botón (nombre sugerido `TRAZABILIDAD_DOCUMENTO`); el asistente *Crear botón…* la pone en el ribbon.
2. En la lista de documentos de Comercial **selecciona un documento** (o ábrelo ya guardado) y pulsa el botón.
3. Se abre una ventana con dos zonas: **arriba el flujo** y **abajo el detalle**. Arrastra la barra del medio para dar más espacio a una o a otra.
   - **Un clic** en una tarjeta elige ese documento y su detalle aparece abajo (no vuelve a consultar nada: es inmediato).
   - **Doble clic** abre el documento en Comercial. La ventana **sigue abierta** para que continúes.
   - **Abrir en Comercial** abre el documento elegido.
   - **Exportar partidas del documento / Exportar toda la cadena** guardan un CSV para Excel con las partidas, de dónde vienen, a dónde van, lotes, series y pedimentos.

## El flujo: ordenado por lo que el documento ES

Las columnas **no** dependen de quién nació de quién, sino del **tipo de documento**, para que el orden sea el del negocio aunque se haya saltado un paso
(por ejemplo, una factura creada directo desde el pedido sigue yendo **después** de la recepción de mercancía):

| Columna | Qué cae ahí |
|---|---|
| **Solicitud / Cotización** | solicitudes de compra, cotizaciones |
| **Pedido / Orden** | pedidos, órdenes de compra, pedidos a consignación |
| **Entrega / Recepción** | remisiones, recepciones, entradas, salidas y traspasos de almacén |
| **Factura** | facturas, notas de cargo, ventas de mostrador, gastos |
| **Nota de crédito / Devolución** | notas de crédito y devoluciones |

Las **filas** (carriles) separan **Venta**, **Compra** e **Inventario y otros**. El tipo y a quién va dirigido se leen de los parámetros del propio módulo (`DocumentTypeID`, `DocRecipient`),
por `ModuleIDBase`: nada va escrito por número de módulo.

Cada **tarjeta** muestra tipo, **folio**, **ID del documento**, fecha, cliente o proveedor, total y su estado de pago (**Pagado**, **Parcial**, **Sin pago** o **Cancelado**). La flecha continua es un vínculo del sistema;
la **discontinua** es un **vínculo manual** (ver abajo). Las flechas del documento elegido se resaltan.

## El detalle del documento elegido

- **Encabezado:** tipo, folio, ID, título, fecha, cliente o proveedor, total, pagado y saldo, y los enlaces **«Viene de»** y **«Va hacia»** (un clic en un enlace elige ese documento).
- **Pestaña Partidas:**
  - clave, descripción, **cantidad con su unidad**, precio, descuento e importe;
  - **conversión de unidad:** si la partida está en otra unidad que la base del producto se ve el equivalente, por ejemplo «5 CAJA = 60 CUBETA (×12)»;
  - **Viene de:** la partida de origen (documento y cantidad); **Va hacia:** a qué documentos pasó y cuánto, con **lo que falta** o **completa**, por separado para la entrega/recepción y para la factura
    (una orden totalmente recibida puede seguir pendiente de facturar);
  - debajo de cada partida, sus **lotes** (con caducidad), **números de serie** y **pedimentos** (número, aduana, fecha, cantidad).
- **Pestaña Pagos y cobros:** fecha, tipo (pago, cobro o nota de crédito aplicada), operación, forma de pago, cuenta y monto aplicado.
- El aviso amarillo de arriba reúne lo que conviene revisar: vínculos manuales, documentos cancelados que siguen ligados y vínculos rotos.

## Cómo se ligan los documentos

Comercial no guarda la relación en un solo lugar. La plantilla lee las cuatro y las junta; **no muestra la «evidencia» técnica**, solo el resultado:

| Dónde | Columna | Qué significa |
|---|---|---|
| Encabezado | `docDocument.SourceDocumentID` | El documento de origen que escribe el sistema al convertir un documento en otro. Solo admite **un** origen. |
| Encabezado | `docDocument.DestinationDocumentID` | El documento destino. Comercial la **lee** (globalización de ventas, «asignar factura de compra a orden de compra»); quién la escribe de forma nativa **no está demostrado**. |
| Partida | `docDocumentItem.SourceDocumentItemID` (y `SourceDocumentID`) | La partida nueva apunta a la partida del origen; admite **varios** orígenes. |
| Partida | `docDocumentItem.DeliverDocumentItemID` | La «entrega» de una orden de compra: así se liga la **Recepción** a su orden. |

Si un documento (por ejemplo una remisión hecha desde un pedido) solo está ligado por el encabezado, las partidas se enlazan **por producto** y se marcan con **≈** (aproximado).

### Vínculos manuales (varios orígenes → un solo destino)

Cuando de **varias** órdenes de compra sale **una sola** factura, el destino solo puede nombrar a una en `SourceDocumentID`. Quien lo necesita suele escribir en **cada origen** el `DestinationDocumentID`.
Si un vínculo existe **únicamente** por `DestinationDocumentID`, la flecha va **discontinua**, se rotula como *vínculo manual* y se avisa. Es información, no un error.

## Qué NO hace

- No muestra la póliza contable ni el UUID del CFDI.
- No escribe vínculos: no crea ni corrige nada. Es una vista.
- Las partidas se cargan hasta **3,000** en total; en una cadena enorme, los documentos más alejados muestran solo su ficha y se avisa. Se corta en 300 documentos.
- Las cantidades en otra unidad solo se desglosan cuando el documento guarda un coeficiente distinto de 1.

## Reglas de BrosLMV que cumple

- **Empresa activa:** todas las consultas filtran por `OwnedBusinessEntityID` del contexto.
- **Módulos por naturaleza:** clasifica con los parámetros del módulo y `ModuleIDBase`; funciona con los clones.
- **Eliminados:** un documento o partida con `DeletedOn` no se sigue; un documento **cancelado** sí aparece, marcado.
- **Ventana modeless** (`ctx.ShowHtmlModeless`): abrir un documento no choca con XEngine («the other application is busy»).

## Ver un ejemplo en el laboratorio

En `BROSLMV_DESARROLLO`: los documentos **«DEMO TRAZ»** (tres órdenes de compra → una factura, con el vínculo manual) y los **«DEMO CREAR DOC»** (orden → recepción parcial → factura).
`build/laboratorio/demo_trazabilidad_detalle.sql` les agrega **lotes con caducidad, series, un pedimento y una conversión de unidad** para ver el detalle completo.

## Para desarrolladores

- El motor arma un modelo (`nodos` con sus `partidas`, `pagos`, etapa y carril; `aristas`; `avisos`) con consultas por lotes y luego se dibuja en el navegador embebido; todo lo demás es JavaScript local.
- **Probar sin ventanas:** `BROSLMV_TRAZA_DOC=<DocumentID>` hace que el script no abra la ventana y escriba el modelo JSON en `BROSLMV_TRAZA_OUT` y la página en `BROSLMV_TRAZA_HTML`.
  Corre en `BrosLMV.Runner` (lleva `// job: safe-offline`) y en la prueba de humo `build/humo/casos/35_trazabilidad_documento.ps1`.
- **Editar sin compilar una versión nueva:** `build/laboratorio/publicar_scripts_lab.ps1` publica las plantillas como **scripts** del laboratorio (con su hash, sin avisos de «modificado por fuera»);
  se edita el archivo de `instalador/scripts`, se vuelve a correr y se ejecuta otra vez el botón en Comercial.
- La ventana usa `ctx.ShowHtmlModeless`: el script termina y cada acción (`abrir`, `csv`) llega a un manejador en el hilo de Comercial.

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «Selecciona un documento…» | No había nada seleccionado en la lista ni un documento abierto ya guardado. |
| «El documento… no existe, está eliminado o es de otra empresa» | El documento fue eliminado o no pertenece a la empresa activa. |
| La cadena se ve incompleta | Un paso intermedio se hizo **sin** vincular (p. ej. un documento capturado a mano, sin partida origen). No hay nada que leer. |
| «un vínculo apunta a un documento eliminado» | El `SourceDocumentID` o `DestinationDocumentID` de algún documento quedó apuntando a uno ya eliminado. |
| «El detalle de partidas… no se cargó» | La cadena es muy grande; ejecuta la trazabilidad desde ese documento. |
