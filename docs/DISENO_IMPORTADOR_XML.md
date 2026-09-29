# Diseño del importador masivo de XML

## Estado

Versión 3 (pestañas), 2026-09-28. Sustituye a la pantalla única de lote y a la primera versión con
ventanas emergentes. Descripción técnica en [`IMPORTADOR_XML_MASIVO.md`](IMPORTADOR_XML_MASIVO.md).

## Decisiones de diseño (pedidas por el usuario)

1. **Velocidad ante todo:** pulsar **Crear documentos** basta. Nada obliga a pasar por ventanas:
   proveedor faltante = se da de alta; producto faltante = renglón sin producto (como los gastos), con
   opción de «crear el producto» o «exigir producto».
2. **Pestañas** — Documentos, Proveedores, Partidas, Impuestos, Resultado — en lugar de ventanas
   emergentes; edición en línea con listas buscables.
3. **El impuesto lo rige el documento**: sale del XML y se empareja exacto con los tipos de impuesto
   existentes. Si no hay coincidencia el documento **no se crea** y se sugiere el tipo de impuesto que
   falta. Cambiar producto o tipo de gasto nunca cambia el impuesto.
4. **Memoria**: mismo proveedor + mismo producto/partida = se sugiere el producto y el tipo de gasto que
   ya se usaron (mapa propio, historial de gastos de Comercial y clave SAT); se aprende al crear.

## Pantalla

Cada CFDI es una fila en **Documentos** (destino, almacén, plazo, estado y avisos por fila; las acciones
globales son solo filtros y «aplicar a marcados»). El detalle de un CFDI muestra sus partidas y permite
ajustar método de pago, forma de pago, uso de CFDI y tipo de impuesto por partida.

## Compatibilidad fiscal

El importador lee `TipoComprobante` de cada CFDI y filtra los destinos permitidos antes de
mostrar el combo:

| Tipo CFDI | Destinos permitidos en esta entrega |
|---|---|
| I — Ingreso recibido | Factura de Compra, Gasto y los módulos de compra compatibles que se habiliten tras validarlos. |
| E — Egreso | Solo destinos de ajuste/nota de crédito ya validados; no se mezcla con ingreso. |
| P — Pago | Solo flujo de pago nativo; no crea una factura ni gasto. |
| T / N | Bloqueado con explicación hasta definir su flujo. |

No es posible elegir un destino incompatible ni cambiar el tipo del XML desde BrosLMV.

## Proveedores y productos

Las pestañas secundarias son paneles de resolución agrupados, no listas repetidas:

- Proveedor: una fila por RFC. Acciones: asociar existente, abrir ficha, y “nuevo proveedor”.
- Concepto: una fila por RFC + clave SAT de producto/servicio + clave del proveedor +
  descripción normalizada. Muestra clave SAT, unidad, objeto de impuesto, impuestos XML,
  producto asociado y tipo de gasto sugerido.
- El alta de producto es un modal con datos obtenidos del XML y campos de catálogo: clave,
  nombre, tipo, categoría, unidad, claves SAT e impuestos. Solo se confirma después de revisar;
  después guarda el producto y la asociación del concepto.

El alta de tercero y producto debe usar la clase COM nativa correspondiente. Mientras esa ruta
no esté validada, el control se muestra deshabilitado con una explicación; no se simula con SQL.

## Resultado y pólizas

El botón fijo **Crear documentos** vive en la pantalla principal y muestra antes un resumen de
creados, pendientes y bloqueados. Solo crea filas listas. Después ofrece abrir documentos y
consulta la póliza que Comercial produjo; no escribe ni sincroniza pólizas directamente.
