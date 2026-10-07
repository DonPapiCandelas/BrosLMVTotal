# Cobro a cliente

Registra el **cobro de un cliente** y lo aplica a una o varias de sus facturas, con una ventana de tesorería: ves lo que te debe, cuánto está vencido, eliges las facturas, y al registrar el saldo de cada una se actualiza.

**Categoría en la Consola:** Cuentas por cobrar. Es una plantilla **separada de los pagos a proveedores a propósito**: quien lleva las cuentas por cobrar no ve nada de las cuentas por pagar. Para pagos existe [«Pago a proveedor»](PAGO_PROVEEDOR.md); para consultar saldos, [«Estado de cuenta de clientes»](ESTADO_CUENTA_CLIENTES.md).

> ⚠ **Plantilla avanzada, no nativa.** Comercial no ofrece una función para aplicar cobros desde fuera de su pantalla de Tesorería; esta plantilla registra el cobro por su cuenta. Funciona y está probada, pero **no genera la póliza contable** del cobro (se genera al contabilizar, como siempre) y no sustituye a la pantalla de Tesorería. **Pruébala primero en una base de pruebas.**

## Qué versión elegir

Cuatro versiones con las mismas funciones: **C# · HTML** (la más completa, recomendada), **Python · HTML**, **C# · Windows Forms** y **Python · Windows Forms** (ventana clásica de Comercial).

## Cómo se usa

1. Pulsa el botón de la plantilla.
2. Escribe el **cliente** (nombre o RFC; tecla F2) y elígelo. Las personas **con saldo salen primero** y cada una muestra cuánto debe. Verás su saldo, lo vencido, límite y crédito disponible, su último cobro y su **antigüedad de saldos** (vigente, 1-30, 31-60, 61-90 y más de 90 días).
3. Elige la **cuenta** donde entra el dinero (la predeterminada ya viene elegida), la **forma de pago**, la fecha y, si quieres, una **referencia o número de rastreo**.
4. **Marca las facturas** que se cobran y ajusta cuánto se aplica a cada una (por omisión, todo su saldo). Puedes liquidar unas y abonar a otras.
   También puedes escribir el **monto recibido** y pulsar **Distribuir**: se reparte entre las facturas **más antiguas primero**. Lo que sobre **no se aplica** (no se manejan anticipos) y la ventana lo avisa.
5. Pulsa **Registrar** (F5). Un aviso resume el folio del cobro y lo que quedó pendiente de cada factura, y los saldos se refrescan. Con **Registrar y nuevo** (F6) capturas otro enseguida.

## Qué ves de cada factura

La lista muestra, por factura: su **estado** (vencida, por vencer, vigente), el **total y el saldo**, **cuánto lleva pagado y con qué** (por ejemplo «pagado 40 %», «1 cobro», «1 nota de crédito»), su **parcialidad** y cuánto se aplica. Un clic en el folio (ventana HTML) o **Ver detalle…** / doble clic (Windows Forms) abre el **detalle**: las parcialidades con vencimiento, importe, pagado y saldo, y cada aplicación anterior (cobro, pago o nota de crédito) con su folio, fecha, parcialidad y tipo de cambio.

## Parcialidades

Si la factura se pactó en parcialidades, cada cobro se aplica a **una parcialidad**: por omisión **en orden** («Automático», se llena la más antigua con saldo) o a la que elijas, y la ventana propone el saldo de esa parcialidad. Si una parcialidad no alcanza, el resto pasa a la siguiente (en automático) o se avisa (si la elegiste).

## Cobros en dólares

La cuenta tiene su moneda y cada factura la suya. **Lo que escribes en «Aplicar» va siempre en la moneda de la cuenta** (lo que entra al banco). La ventana **solo pide el tipo de cambio cuando interviene una moneda extranjera** (sugiere el del día) y te dice, en cada factura, **cuánto baja su saldo en su propia moneda**.

| La cuenta está en… | La factura está en… | Qué pasa |
|---|---|---|
| Pesos | Pesos | Sin tipo de cambio. |
| Pesos | Dólares | Los pesos se dividen entre el tipo de cambio: 925 MXN a 18.50 bajan 50 USD. |
| Dólares | Dólares | 1 a 1; el tipo de cambio fija su valor en pesos. |
| Dólares | Pesos | Los dólares se multiplican por el tipo de cambio: 10 USD a 18 bajan 180 MXN. |
| Otras (por ejemplo euros contra dólares) | | No se permite aquí: regístralo en la pantalla de Tesorería de Comercial. |

Una sola operación no mezcla facturas en dos monedas extranjeras distintas. La cartera, la antigüedad y el saldo de la persona se muestran **en pesos**.

## Qué registra en Comercial

Un cobro crea **una sola operación con un solo folio** (`COB-n`), con un renglón por factura y parcialidad, igual que la pantalla de Tesorería: queda en el historial de cobros de la factura, se actualiza su saldo y estatus (pagada o parcial), se guarda la **transferencia bancaria** con la referencia (no en efectivo) y el reparto de impuestos del cobro. Todo ocurre **de una sola vez**: o se registra completo o no se registra nada.

- **Dos cobros al mismo tiempo** no se pisan: cada uno recibe su propio folio.
- **El saldo se revisa al registrar**: si alguien más aplicó algo a la factura mientras capturabas, no se aplica de más y se te explica.
- Solo se cobran **facturas y documentos que suman saldo** del cliente elegido; no se puede aplicar más que el saldo.

## Extras de la ventana HTML

- **Cartera completa** con gráfica de antigüedad, porcentaje vencido y **principales deudores** (un clic abre a la persona).
- **Pronóstico de vencimientos** de las próximas 8 semanas.
- **Comportamiento de pago del cliente:** últimos 12 meses, días promedio de pago, atraso promedio y porcentaje de pagos a tiempo.
- **Cinco formas de repartir el monto:** más antiguos primero, solo vencidos, mayor saldo, menor saldo (liquida más facturas) y proporcional.
- **Revisión previa** con avisos: facturas PPD (recuerda emitir el **complemento de pago**), falta de referencia, monto que sobra o falta, fecha futura.
- **Paleta de comandos (Ctrl+K)**, **recibo imprimible (Ctrl+P)** con el importe en letra, **copiar a Excel**, **deshacer y rehacer**, **borrador automático** y tema claro u oscuro.

## Límites

- **No genera póliza contable** (se genera al contabilizar); revisa con tu contador cómo contabilizas estos cobros.
- Sin anticipos, sin cobros por remesa, sin complemento de pago (CFDI de pagos) y sin pagos con cambio.
- **Las notas de crédito se muestran pero no se aplican aquí.**
- **No hay deshacer:** para revertir un cobro, cancélalo en la pantalla de Tesorería de Comercial.
- Se listan hasta 30,000 facturas con saldo (las más antiguas primero).

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «Elige la cuenta bancaria o caja…» | La empresa no tiene cuentas dadas de alta en Tesorería. |
| El cliente no aparece | No está dado de alta como cliente, o está eliminado. |
| No aparece una factura | Está cancelada, ya no tiene saldo o no es un documento que suma saldo. |
| «Captura el tipo de cambio» | La cuenta o una factura está en moneda extranjera: escribe el tipo de cambio del día. |
| «Esa combinación solo se puede registrar en la pantalla nativa» | La cuenta y la factura están en monedas extranjeras distintas. |
| «El saldo del documento cambió mientras se capturaba» | Otra persona aplicó un cobro a la misma factura; captura de nuevo con el saldo actualizado. |
| «No se pudo obtener el candado del folio» | Otro cobro se está registrando en este momento; espera unos segundos. |
