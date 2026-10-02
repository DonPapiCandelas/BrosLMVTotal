# Trazabilidad del documento

Plantilla de fábrica **Plantillas → Trazabilidad → Trazabilidad del documento** (`TRAZABILIDAD_DOCUMENTO.ctx`). Muestra, para cualquier documento de Comercial,
**de dónde viene y a dónde fue**, en las dos direcciones y a través de todos los pasos: Solicitud de compra → Orden de compra → Recepción → Factura de compra → Pago, o
Cotización → Pedido → Remisión → Factura → Cobro, incluidos los módulos clonados de tu empresa.

**Solo lee.** No cambia ningún documento ni ninguna tabla.

## Cómo usarla

1. En la **Consola** abre *Plantillas → Trazabilidad → Trazabilidad del documento* y guárdala como botón (clic secundario sobre ella → *Insertar en el editor*, luego *Guardar*; el asistente
   *Crear botón…* la pone en el ribbon). El nombre sugerido es `TRAZABILIDAD_DOCUMENTO`.
2. En la lista de documentos de Comercial **selecciona un documento** (o ábrelo ya guardado) y pulsa el botón.
3. Se abre una ventana con el mapa. Desde ahí:
   - **Un clic** en otro documento centra la trazabilidad en él.
   - **Doble clic** lo abre en Comercial.
   - **Exportar a CSV** guarda los vínculos y las cantidades por partida.

## Cómo leer el mapa

- Cada **tarjeta** es un documento: tipo, folio, fecha, cliente o proveedor y total. Las etiquetas indican si está **cancelado**, si tiene **pagos o cobros** aplicados o si queda **saldo**.
- Las **columnas** van de izquierda (origen) a derecha (destino). El documento que elegiste está resaltado.
- Una **flecha continua** es un vínculo que el sistema reconoce. Una **flecha discontinua** es un **vínculo manual** (ver abajo).
- Debajo del mapa, la tabla **Vínculos y partidas** dice con qué evidencia existe cada flecha y, cuando la hay, la cantidad de cada producto en el origen y en el destino.
- El recuadro **Avisos** señala lo que conviene revisar: vínculos manuales, documentos cancelados que siguen ligados y vínculos rotos.

## Las cuatro formas en que se ligan los documentos

Comercial no guarda la relación en un solo lugar. La plantilla lee las cuatro y las junta:

| Evidencia | Columna | Qué significa |
|---|---|---|
| **Source** | `docDocument.SourceDocumentID` | El documento de origen. Es lo que escribe el sistema al convertir un documento en otro. Solo admite **un** origen por documento. |
| **Destination** | `docDocument.DestinationDocumentID` | El documento destino. Comercial la **lee** en la **globalización de ventas** y en **«asignar factura de compra a orden de compra»**; además muchos scripts y personas la llenan a mano. |
| **Partida** | `docDocumentItem.SourceDocumentItemID` (y `docDocumentItem.SourceDocumentID`) | La partida del documento nuevo apunta a la partida del origen. Es el vínculo que sí admite **varios** orígenes para un mismo destino. |
| **Entrega** | `docDocumentItem.DeliverDocumentItemID` | La «entrega» de una orden de compra: así se liga la **Recepción** a su orden. |

### Vínculos manuales (varios orígenes → un solo destino)

El sistema solo guarda **un** `SourceDocumentID` por documento. Cuando de **varias** órdenes de compra sale **una sola** factura (o de varias facturas una consolidada), el destino solo puede
nombrar a una. Quien lo necesita suele escribir en **cada origen** el `DestinationDocumentID` del documento nuevo: así se puede ir de cada origen a su destino.

La plantilla lo toma en cuenta: si un vínculo existe **únicamente** por `DestinationDocumentID`, lo dibuja con **línea discontinua**, lo marca como *manual* en la tabla y lo avisa, para que sepas que
el documento destino no reconoce a ese origen como suyo. Es información, no un error.

## Qué NO hace (todavía)

- No calcula «cuánto falta por recibir o facturar» por producto; sí muestra las cantidades de cada partida en el origen y en el destino.
- No muestra la póliza contable ni el UUID del CFDI (sí el estado del timbrado, internamente).
- No escribe vínculos: no crea ni corrige `DestinationDocumentID`. Es una vista.
- Una cadena de más de 300 documentos se corta y se avisa.

## Reglas de BrosLMV que cumple

- **Empresa activa:** todas las consultas filtran por `OwnedBusinessEntityID` del contexto.
- **Módulos por naturaleza:** clasifica con `engModule.ModuleIDBase`; funciona con los clones (series, sucursales, formas de pago).
- **Eliminados:** un documento o partida con `DeletedOn` no se sigue; un documento **cancelado** (`CancelledOn`) sí aparece, marcado.
- **Sin nada escrito a mano:** ni números de módulo ni nombres de tu empresa.

## Ver un ejemplo en el laboratorio

En la empresa de laboratorio **`BROSLMV_DESARROLLO`** hay datos de demostración (títulos que empiezan con **«DEMO TRAZ»**): **tres órdenes de compra** del mismo proveedor y **una sola factura de compra** que las factura juntas.
La factura guarda un solo `SourceDocumentID` (la primera orden, que es lo único que el sistema puede guardar) y cada orden guarda `DestinationDocumentID` = la factura. Selecciona cualquiera de los cuatro y pulsa el botón:
verás las tres órdenes a la izquierda y la factura a la derecha. Se crean con `build/laboratorio/sembrar_demo_trazabilidad.ps1` (idempotente; solo corre contra el laboratorio).

## Para desarrolladores

- El motor arma un modelo (`nodos`, `aristas`, `avisos`) con consultas por lotes (400 documentos por consulta, hasta 25 niveles y 300 documentos) y luego lo dibuja.
- **Modo de pruebas sin ventanas:** con la variable de entorno `BROSLMV_TRAZA_DOC=<DocumentID>` el script no abre la ventana y escribe el modelo en JSON en el archivo de `BROSLMV_TRAZA_OUT`.
  Así corre en `BrosLMV.Runner` (el script lleva `// job: safe-offline`) y en la prueba de humo `build/humo/casos/35_trazabilidad.ps1`.
- La ventana usa `ctx.ShowHtmlFormulario`; el clic y el doble clic mandan un mensaje (`centrar` / `abrir`) y el script vuelve a mostrar la ventana.

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «Selecciona un documento…» | No había nada seleccionado en la lista ni un documento abierto ya guardado. |
| «El documento… no existe, está eliminado o es de otra empresa» | El documento fue eliminado o no pertenece a la empresa activa. |
| La cadena se ve incompleta | Un paso intermedio se hizo **sin** vincular (p. ej. un documento capturado a mano, sin partida origen). No hay nada que leer; la relación solo existiría en la cabeza de quien lo capturó. |
| Avisa «un vínculo apunta a un documento eliminado» | El `SourceDocumentID` o `DestinationDocumentID` de algún documento quedó apuntando a uno ya eliminado. |
