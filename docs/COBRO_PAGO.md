# Cobro a cliente / Pago a proveedor (plantillas avanzadas, no nativas)

> ⚠ **Plantilla avanzada.** Comercial **no ofrece ninguna función** para aplicar un cobro o un pago (se buscó en el SDK y en el motor; véase MANUAL §10.5). Estas plantillas escriben **directo en las tablas de Tesorería**.
> Funcionan y se validaron contra el laboratorio, pero **no generan la póliza contable** del cobro/pago y no sustituyen a la pantalla nativa de Tesorería. **Pruébalas primero en una base de pruebas**, no en producción.

Cuatro plantillas de fábrica (Consola → Plantillas → Tesorería), la misma ventana y la misma lógica:

| Plantilla | Lenguaje | Ventana |
|---|---|---|
| **Cobro a cliente / Pago a proveedor (C# · ventana HTML)** — `COBRO_PAGO_CSHARP_WEBVIEW2.ctx` | C# | HTML (WebView2) |
| **… (C# · ventana Windows Forms)** — `COBRO_PAGO_CSHARP_WINFORMS.ctx` | C# | Windows Forms |
| **… (Python · ventana HTML)** — `COBRO_PAGO_PYTHON_WEBVIEW2.py` | Python | HTML (WebView2) |
| **… (Python · ventana Windows Forms)** — `COBRO_PAGO_PYTHON_WINFORMS.py` | Python | Windows Forms (pythonnet) |

## La ventana: una estación de tesorería

La versión **HTML** (C# y Python) y la de **Windows Forms** de C# capturan lo mismo y comparten el núcleo, cada una con su diseño. La de Windows Forms usa el estilo clásico de un documento de Comercial, el mismo de «Crear documento» (ver [`CREAR_DOCUMENTO.md`](CREAR_DOCUMENTO.md)): cinta oscura, grupos numerados con la etiqueta arriba de cada campo, **antigüedad de saldos por tramos con barras**, documentos marcables con la columna **Aplicar** editable y resumen de lo que quedaría. Estas son las funciones:

- **Cinta de acciones:** **Registrar** (F5; «Registrar cobro» o «Registrar pago»), **Registrar y nuevo** (F6, para capturar varios seguidos), **Limpiar** y **Cancelar** (Esc). Junto a ellas, la **información del movimiento**: fecha, folio probable (`COB-n` / `PAG-n`) y cuenta (la predeterminada ya viene elegida).
- **1 · Cliente o proveedor** con búsqueda por nombre o RFC (F2); **las personas con saldo salen primero** y cada una muestra cuánto debe y en cuántos documentos. Al elegirla ves su **saldo pendiente, lo vencido, número de documentos, límite y crédito disponible, su último cobro o pago** y su **antigüedad de saldos** (vigente, 1-30, 31-60, 61-90 y más de 90 días).
- **2 · Datos del movimiento:** forma de pago, referencia o número de rastreo y **monto recibido o a pagar**. **«Distribuir»** reparte ese monto entre los documentos **más antiguos primero**; también puedes **marcar todos**, **marcar vencidos** o ajustar cada documento a mano.
- **3 · Documentos con saldo** con su estado (vencido N días, vence en N días, vigente), total, saldo y cuánto aplicar.
- **Resumen:** documentos marcados, saldo de la persona, **cuánto quedaría**, monto recibido y **total a aplicar**, con avisos (sobra o falta monto, fecha posterior a hoy) y los **últimos cobros o pagos** de la persona.
- **No bloquea Comercial:** se puede minimizar.

La versión **HTML** consulta a Comercial **en vivo** (últimos movimientos de la persona y saldos ya actualizados después de registrar, sin cerrar la ventana) con `ctx.ShowHtmlModeless` y respuestas `__JS__…`; la de **Python en HTML** abre la misma página con lo precargado (sin consultas en vivo ni «Registrar y nuevo»); la de **Windows Forms** de C# trae el mismo diseño con controles nativos. La de **Python en Windows Forms** sigue con la ventana sencilla de antes.

## Lo que solo una ventana web puede dar (WebView2)

Las plantillas **WebView2** (C# y Python) consultan a Comercial en vivo y suman:

- **Cartera completa** (por cobrar o por pagar): **dona de antigüedad** (vigente, 1-30, 31-60, 61-90, más de 90), porcentaje vencido y **principales deudores o acreedores** con barras (un clic abre a la persona).
- **Pronóstico de vencimientos** de las próximas 8 semanas (de la persona o de toda la cartera) con lo ya vencido aparte.
- **Comportamiento de pago de la persona** (en vivo): barras de los últimos 12 meses, **días promedio de pago**, **atraso promedio** frente al vencimiento y **% de pagos a tiempo**.
- **Cinco estrategias para repartir el monto:** más antiguos primero, solo vencidos, mayor saldo primero, menor saldo primero (liquida más documentos) y proporcional; cada documento muestra una barra con cuánto de su saldo se aplica.
- **Revisión previa** con avisos con sentido fiscal: documentos **PPD** (recuerda emitir —o pedir al proveedor— el **complemento de pago**), pago en efectivo mayor a $2,000 (no deducible, LISR art. 27), falta de referencia para conciliar, monto que sobra o falta, fecha futura.
- **Paleta de comandos (Ctrl+K)** para acciones, personas y **folios de documentos** (abre a la persona y lo marca), **recibo o comprobante imprimible (Ctrl+P)** con importe en letra, **copiar los documentos a Excel**, **deshacer/rehacer**, **borrador automático** y **tema oscuro**.

## Cómo usarlas

1. En la **Consola** abre la plantilla que prefieras, guárdala como botón (nombre sugerido `COBRO_PAGO`) y ponla en el ribbon con *Crear botón…*.
2. Pulsa el botón. Elige **Cobro a cliente** o **Pago a proveedor**.
3. Escribe el **cliente o proveedor** y elígelo de la lista: aparecen **todos sus documentos con saldo** (los vencidos, en rojo).
4. Elige la **cuenta** (banco o caja donde entra o de donde sale el dinero), la **forma de pago**, la **fecha** y, si quieres, una **referencia o número de rastreo**.
5. **Marca** los documentos y ajusta el **monto** de cada uno (por omisión, todo su saldo): puedes liquidar uno, abonar a otro y dejar los demás.
   También puedes capturar el **monto recibido** y pulsar **Distribuir**: se reparte entre los más antiguos primero. Lo que sobre **no se aplica** (los anticipos no se manejan aquí) y la ventana lo avisa.
6. **Registrar** (F5). Un aviso resume los folios creados y lo que quedó pendiente de cada documento; los saldos se refrescan. Con **Registrar y nuevo** (F6) la ventana queda lista para el siguiente.

## Qué escribe (la receta de siete tablas)

Por **cada documento** marcado se crea **una operación** (con su propio folio) en **una sola transacción**:

| Tabla | Qué guarda |
|---|---|
| `docFinancialOperation` | La operación: módulo **248** (cobro) o **247** (pago), cliente o proveedor, cuenta, forma de pago, fecha, importe y folio (`COB-n` / `PAG-n`). |
| `docDocumentPayment` | La aplicación al documento: importe, **saldo anterior** y **saldo insoluto**. |
| `docDocumentPaymentEspejo` | El espejo de la aplicación (mismo id; la columna no es identity). |
| `docBankTransfer` | La transferencia bancaria con la referencia (**solo** si la forma de pago no es efectivo). |
| `docFinancialOperationTaxDetail` | El **reparto proporcional de impuestos**: lo aplicado entre el total del documento, aplicado a cada renglón de impuesto. |
| `docDocument` | `TotalPaid`, `Balance` y `StatusPaidID` (1 = pagado, 2 = parcial). |

- **Folio sin choques:** el siguiente folio se calcula con un **candado de transacción** (`sp_getapplock`), así dos cobros simultáneos nunca reciben el mismo.
- **Saldo revalidado:** dentro de la transacción se vuelve a comprobar el saldo; si alguien más aplicó algo mientras capturabas, **no se aplica de más** y se explica.
- **Reglas:** solo se aplica a documentos que **suman saldo** (facturas, notas de cargo, gastos; las notas de crédito no se cobran ni se pagan) del cliente/proveedor elegido; no se puede aplicar más que el saldo.
- **Qué documentos cuentan** como cuentas por cobrar o por pagar se lee de los parámetros del módulo (`DocRecipient`, `FinancialAffectation`), sin lista escrita a mano; los módulos clonados cuentan igual.

## Qué garantiza

- Atómico por documento: o se escriben las siete tablas o ninguna.
- Si hay varios documentos y falla uno, el mensaje dice **cuáles ya quedaron aplicados** (los anteriores) y cuál falló; no se aplica nada al fallido.
- Validado con la plantilla [«Estado de cuenta de clientes»](ESTADO_CUENTA_CLIENTES.md) y [«de proveedores»](ESTADO_CUENTA_PROVEEDORES.md): el saldo que reconstruye desde los pagos coincide con `Balance` después de aplicar.

## Límites

- **No genera póliza contable.** La póliza del cobro/pago la genera el Motor de Asientos al contabilizar (véase `MOTOR_ASIENTOS_CONTABLES.md`); revisa con tu contador cómo contabilizas esos movimientos.
- **Sin multimoneda:** la aplicación usa tipo de cambio 1. Para documentos en moneda extranjera usa la pantalla nativa.
- **Sin anticipos, sin cobros por remesa, sin complemento de pago (CFDI de pagos), sin pagos con cambio.** Solo aplica importes a documentos concretos.
- **Sin deshacer.** Para revertir un cobro o pago usa la pantalla nativa de Tesorería (cancelar la operación); no borres filas a mano.
- **Una operación por documento:** si cobras 3 facturas de golpe verás 3 folios.
- Lista hasta **30,000** documentos con saldo por lado (los más antiguos primero).

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «Elige la cuenta bancaria o caja…» | La empresa no tiene cuentas dadas de alta en Tesorería (`orgFinancialEntity`). Da de alta al menos una. |
| El cliente o proveedor no aparece | No está dado de alta como cliente/proveedor, o está eliminado. |
| No aparece un documento | Está cancelado/eliminado, ya no tiene saldo, o su módulo no suma saldo (`FinancialAffectation` ≠ ±1). |
| «El saldo del documento cambió mientras se capturaba» | Otra persona o proceso aplicó un cobro/pago al mismo documento; vuelve a capturar con el saldo actualizado. |
| «No se pudo obtener el candado del folio» | Otro cobro/pago se está registrando en este momento; espera unos segundos y reintenta. |

## Para desarrolladores

Los archivos de `instalador/scripts` se **generan** (como las plantillas de [«Crear documento»](CREAR_DOCUMENTO.md)): el núcleo (`nucleo_pagos.cs.part` / `.py.part`) y el formulario (`formulario_pagos.html.part`) viven una sola vez en `build/plantillas_documentos/`;
`python build/plantillas_documentos/generar.py` arma las cuatro variantes. **Edita las piezas y regenera.**

**Pruebas.**

- C#: `build/humo/casos/40_cobro_pago.ps1` crea documentos nuevos con la plantilla de «Crear documento» y les aplica **de verdad** cobros y pagos en `BROSLMV_DESARROLLO`: parcial por transferencia (comprueba las siete tablas, el IVA proporcional y el estatus parcial),
  liquidación en efectivo (sin transferencia), un pago a dos facturas, un sobrepago y un documento ajeno (rechazados sin cambios) y las validaciones. Variables: `BROSLMV_PAGO_TEST` (JSON con el movimiento o `{"catalogo":true}`), `BROSLMV_PAGO_OUT` (archivo de resultado), `BROSLMV_PAGO_HTML` (escribe la página y no abre ventana).
- Python: `python build/plantillas_documentos/prueba_python_pagos.py [plantilla.py]` simula `broslmv` (lee el laboratorio de verdad, **solo registra** el SQL) y comprueba catálogos, la receta (piezas y saldo nuevo), efectivo sin transferencia, cobro a cliente, los rechazos sin escribir y que la ventana se arma.
  **Los clics de las ventanas se prueban a mano dentro de Comercial.** La página HTML se probó en el navegador integrado.
