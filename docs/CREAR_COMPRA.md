# Crear documento de compra

Captura **órdenes de compra, recepciones y facturas de compra** desde una ventana pensada para capturar rápido, sin tocar la pantalla nativa de Comercial. Al guardar, el documento queda creado de verdad y se **abre en Comercial** para que lo revises como siempre.

**Categoría en la Consola:** Compras. Es una plantilla **separada de las ventas a propósito**: quien captura compras solo ve proveedores y documentos de compra, y nada de clientes. Para ventas existe [«Crear documento de venta»](CREAR_VENTA.md).

## Qué versión elegir

Hay cuatro versiones, **con las mismas funciones**: elige la que prefieras o la que mejor se lleve con tu equipo.

| Versión | Cuándo conviene |
|---|---|
| **C# · ventana HTML** | La más completa y moderna (gráficas, paleta de comandos, borrador automático). Recomendada. |
| **Python · ventana HTML** | Lo mismo que la anterior, para quien trabaja en Python. |
| **C# · Windows Forms** | Ventana clásica, con el estilo de un documento de Comercial. |
| **Python · Windows Forms** | Igual que la anterior, para quien trabaja en Python. |

## Qué documentos crea

| Documento | Se usa para |
|---|---|
| **Orden de compra** | Pedir mercancía al proveedor, con fecha de entrega |
| **Recepción de compra** | Recibir mercancía de una orden de compra, total o parcialmente |
| **Factura de compra** | Registrar la factura del proveedor, con su agenda de pago |

## Cómo se usa

1. Pulsa el botón de la plantilla. Arriba elige el **tipo de documento**.
2. Escribe el **proveedor** (nombre, RFC o clave; tecla F2) y elígelo de la lista. Verás su RFC, saldo abierto y último documento, y se propone su condición de pago.
3. Elige **almacén**, **condición de pago** y fechas. Si lo necesitas, pon título y comentarios.
4. Agrega **partidas**: busca el producto por nombre, clave o **código de barras** (escanea y Enter; tecla F3). Edita cantidad, precio de compra, descuento (%) e impuesto; el total se recalcula al instante.
5. Pulsa **Guardar y abrir** (F5): se crea el documento, se refresca la lista de Comercial y el documento se abre. Con **Guardar y nuevo** (F6) se crea y la ventana queda lista para el siguiente.

Si algo falla, **no pierdes lo capturado** y el mensaje dice qué corregir.

## Partir de una orden de compra

Para crear una **recepción** o una **factura de compra** a partir de órdenes de compra, elige al proveedor: sus órdenes con pendientes aparecen en el recuadro **«Partir de un documento ya existente»** (también se marcan las que hayas seleccionado en la lista de Comercial antes de pulsar el botón).

| Quieres crear… | Se carga… |
|---|---|
| Recepción de compra | lo que falta por **recibir** de cada partida |
| Factura de compra | lo que falta por **facturar** de cada partida |

Puedes combinar **varias órdenes del mismo proveedor** en **un solo** documento: cada partida queda ligada a su partida de origen. Lo pendiente se cuenta **por tipo**: una orden recibida completa sigue pendiente de facturar. La cantidad no puede pasar de lo pendiente.

## Dólares y pesos

Elige la **moneda** del documento. Con pesos el tipo de cambio queda fijo en 1; con otra moneda se sugiere el **tipo de cambio** del día (puedes cambiarlo) y es obligatorio que sea mayor a cero. Los costos del catálogo están en pesos: al elegir dólares se **convierten con el tipo de cambio**, y si cambias de moneda se recalculan. Los precios que escribiste a mano se convierten proporcionalmente y los que vienen de una orden de compra se respetan. La ventana te muestra el **equivalente en pesos** del documento.

## Controles antes de guardar

El documento **no se crea** si algo no cuadra, y el mensaje explica cómo corregirlo: sin partidas, cantidad en cero, descuento fuera de 0 a 100, almacén o proveedor que no existen, producto eliminado, condición de pago que no aplica a compras, fecha de entrega anterior a la del documento, o moneda extranjera sin tipo de cambio. Además verás avisos (sin impedir guardar) cuando hay un **precio en cero**.

Si algo falla ya creado el documento, el mensaje te dice su número para que lo elimines o canceles: nunca queda un borrador perdido.

## Extras de la ventana HTML

- **Inteligencia del proveedor:** gráfica de sus últimos 12 meses, días promedio de pago y los productos que más le compras con su último precio (un clic los agrega).
- **Repetir último:** carga las partidas del último documento de ese tipo del proveedor.
- **Revisión previa** con lo que falta o está mal.
- **Paleta de comandos (Ctrl+K)** para acciones, personas y productos.
- **Pegar desde Excel (Ctrl+Shift+V)** o arrastrar un CSV: clave o código de barras, cantidad y precio.
- **Vista previa imprimible (Ctrl+P)** con el importe en letra.
- **Deshacer y rehacer (Ctrl+Z / Ctrl+Y)** sobre las partidas.
- **Borrador automático:** si la ventana se cierra o Comercial se cae, al volver te ofrece recuperar lo capturado (7 días).
- **Tema claro u oscuro.**

Las versiones de Windows Forms traen el botón **Historial** con los últimos documentos del proveedor (doble clic los abre en Comercial).

## Qué conviene saber

- **No bloquea Comercial:** puedes minimizar la ventana y seguir trabajando.
- Un documento creado así **no genera póliza contable por sí solo**; la póliza se genera como siempre al contabilizar.
- El inventario lo afecta el módulo de Comercial según su configuración (la recepción de compra entra al almacén).

## Límites

- No maneja descuentos globales ni retenciones por partida; usa el impuesto del catálogo del producto.
- Los productos con lote o serie se marcan, pero los lotes y series se asignan en Comercial.
- Se cargan hasta 30,000 productos en la ventana.
- Un proveedor solo aparece si está dado de alta como proveedor y no eliminado.

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| El proveedor no aparece | No está dado de alta como proveedor, o está eliminado. |
| «Los documentos de origen son de entidades distintas» | Seleccionaste órdenes de proveedores distintos; deja las de uno solo. |
| Pide el tipo de cambio | La moneda elegida no es pesos: captura el tipo de cambio del día. |
| «Quedó un documento incompleto (id N)» | Falló un paso después de crearlo (por ejemplo un producto sin impuesto). Elimina o cancela ese borrador y corrige lo que dice el mensaje. |
| La recepción o factura no muestra nada pendiente | La orden de compra ya se recibió o facturó por completo. |
| No se abre la ventana (Python) | Revisa `C:\BrosLMV\logs\PythonErp_AAAAMMDD.txt`. |
