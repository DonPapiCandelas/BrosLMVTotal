# Crear documento de venta

Captura **facturas de cliente, pedidos y remisiones** desde una ventana pensada para capturar rápido, sin tocar la pantalla nativa de Comercial. Al guardar, el documento queda creado de verdad y se **abre en Comercial** para que lo revises, lo timbres o lo imprimas como siempre.

**Categoría en la Consola:** Ventas. Es una plantilla **separada de las compras a propósito**: quien captura ventas solo ve clientes y documentos de venta, y nada de proveedores. Para compras existe [«Crear documento de compra»](CREAR_COMPRA.md).

## Qué versión elegir

Hay cuatro versiones, **con las mismas funciones**: elige la que prefieras o la que mejor se lleve con tu equipo.

| Versión | Cuándo conviene |
|---|---|
| **C# · ventana HTML** | La más completa y moderna (gráficas, paleta de comandos, borrador automático). Recomendada. |
| **Python · ventana HTML** | Lo mismo que la anterior, para quien trabaja en Python. |
| **C# · Windows Forms** | Ventana clásica, con el estilo de un documento de Comercial. |
| **Python · Windows Forms** | Igual que la anterior, para quien trabaja en Python. |

## Qué documentos crea

| Documento | Se usa para | Con crédito |
|---|---|---|
| **Factura de cliente** | Vender y facturar, con los datos del CFDI | Sí |
| **Pedido de cliente** | Apartar mercancía, con fecha de entrega | Sí |
| **Remisión (entrega)** | Entregar un pedido, total o parcialmente | No |

## Cómo se usa

1. Pulsa el botón de la plantilla. Arriba elige el **tipo de documento**.
2. Escribe el **cliente** (nombre, RFC o clave; tecla F2) y elígelo de la lista. Verás su RFC, su saldo abierto, su límite de crédito, su descuento habitual y su último documento. Se propone su condición de pago y se aplica su descuento a las partidas nuevas.
3. Elige **almacén**, **condición de pago** y fechas. Si lo necesitas, pon título y comentarios.
4. En una **factura de cliente** revisa los **datos fiscales**: uso del CFDI (el habitual del cliente), forma de pago y método de pago. De contado se propone PUE y efectivo; a crédito, PPD con forma 99, como exige el SAT.
5. Agrega **partidas**: busca el producto por nombre, clave o **código de barras** (escanea y Enter; tecla F3). Verás la **existencia del almacén** de cada producto. Edita cantidad, precio, descuento (%) e impuesto; el total se recalcula al instante.
6. Pulsa **Guardar y abrir** (F5): se crea el documento, se refresca la lista de Comercial y el documento se abre. Con **Guardar y nuevo** (F6) se crea y la ventana queda lista para el siguiente.

Si algo falla, **no pierdes lo capturado** y el mensaje dice qué corregir.

## Dólares y pesos

Elige la **moneda** del documento. Con pesos el tipo de cambio queda fijo en 1; con otra moneda se sugiere el **tipo de cambio** del día (puedes cambiarlo) y es obligatorio que sea mayor a cero. Los precios del catálogo están en pesos: al elegir dólares se **convierten con el tipo de cambio**, y si cambias de moneda se recalculan. Los precios que escribiste tú a mano se convierten proporcionalmente, y los que vienen de otro documento se respetan. La ventana te muestra el **equivalente en pesos** del documento.

## Partir de otro documento

Para crear una **remisión** a partir de un **pedido**, elige a la persona: sus pedidos con mercancía pendiente de remitir aparecen en el recuadro **«Partir de un documento ya existente»** (también se marcan los que hayas seleccionado en la lista de Comercial antes de pulsar el botón). Se cargan las partidas pendientes y la cantidad no puede pasar de lo que falta. Lo remitido se cuenta por producto, como lo hace Comercial.

## Controles antes de guardar

El documento **no se crea** si algo no cuadra, y el mensaje explica cómo corregirlo: sin partidas, cantidad en cero, descuento fuera de 0 a 100, almacén o cliente que no existen, producto eliminado, condición de pago que no aplica a ventas, fecha de entrega anterior a la del documento, o moneda extranjera sin tipo de cambio. Además verás avisos (sin impedir guardar) cuando el documento **excede el límite de crédito**, piden **más de la existencia** o hay un **precio en cero**.

Si algo falla ya creado el documento, el mensaje te dice su número para que lo elimines o canceles: nunca queda un borrador perdido.

## Extras de la ventana HTML

- **Inteligencia del cliente:** gráfica de sus últimos 12 meses, ticket promedio, días promedio de pago y los productos que más compra con su último precio (un clic los agrega).
- **Repetir último:** carga las partidas del último documento de ese tipo del cliente.
- **Margen estimado** en vivo y **revisión previa** con lo que falta o está mal.
- **Paleta de comandos (Ctrl+K)** para acciones, personas y productos.
- **Pegar desde Excel (Ctrl+Shift+V)** o arrastrar un CSV: clave o código de barras, cantidad y precio.
- **Vista previa imprimible (Ctrl+P)** con el importe en letra.
- **Deshacer y rehacer (Ctrl+Z / Ctrl+Y)** sobre las partidas.
- **Borrador automático:** si la ventana se cierra o Comercial se cae, al volver te ofrece recuperar lo capturado (7 días).
- **Tema claro u oscuro.**

Las versiones de Windows Forms traen el botón **Historial** con los últimos documentos del cliente (doble clic los abre en Comercial).

## Qué conviene saber

- **No bloquea Comercial:** puedes minimizar la ventana y seguir trabajando.
- Un documento creado así **no genera póliza contable por sí solo**; la póliza se genera como siempre al contabilizar.
- El inventario lo afecta el módulo de Comercial según su configuración, no la plantilla.

## Límites

- No maneja descuentos globales ni retenciones por partida; usa el impuesto del catálogo del producto.
- Los productos con lote o serie se marcan, pero los lotes y series se asignan en Comercial.
- Se cargan hasta 30,000 productos en la ventana.
- Un cliente solo aparece si está dado de alta como cliente y no eliminado.

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| El cliente no aparece | No está dado de alta como cliente, o está eliminado. |
| Pide el tipo de cambio | La moneda elegida no es pesos: captura el tipo de cambio del día. |
| «Quedó un documento incompleto (id N)» | Falló un paso después de crearlo (por ejemplo un producto sin impuesto). Elimina o cancela ese borrador y corrige lo que dice el mensaje. |
| La remisión no muestra nada pendiente | El pedido ya está remitido por completo. |
| No se abre la ventana (Python) | Revisa `C:\BrosLMV\logs\PythonErp_AAAAMMDD.txt`. |
