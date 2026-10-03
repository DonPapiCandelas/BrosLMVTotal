# Saldos y estados de cuenta

Plantilla de fábrica **Plantillas → Reportes → Saldos y estados de cuenta** (`SALDOS_ESTADOS_CUENTA.ctx`). Cuentas por cobrar, cuentas por pagar y el estado de cuenta de cada cliente o proveedor, con **fecha de corte** y **antigüedad de saldos**.
Solo lee: no cambia ningún documento.

## Cómo usarla

1. En la **Consola** abre la plantilla, guárdala como botón (nombre sugerido `SALDOS_ESTADOS_CUENTA`) y ponla en el ribbon con *Crear botón…*.
2. Pulsa el botón. Se abre una ventana con tres pestañas y un selector **Por cobrar (clientes) / Por pagar (proveedores)**.
3. **Fecha de corte.** Por omisión es hoy. Al cambiarla todo se recalcula al instante: ve cuánto se debía en esa fecha, aunque el documento se haya pagado después.
4. **Antigüedad por cliente / proveedor.** Una fila por cliente o proveedor con su saldo en *Vigente*, *1-30*, *31-60*, *61-90* y *Más de 90* días de vencido, y *A favor* (notas de crédito sin aplicar). Clic en una fila abre su estado de cuenta.
5. **Documentos con saldo.** Cada documento con su vencimiento, días de atraso y saldo. **Doble clic** abre el documento en Comercial y cierra la ventana (vuelve a ejecutar el botón para regresar).
6. **Estado de cuenta.** Elige el cliente o proveedor y el periodo: saldo inicial, cada movimiento (documentos como cargos; cobros, pagos y notas de crédito como abonos) con saldo corriente, y totales del periodo.
7. **Buscar** (nombre, folio o título) y **Solo vencidos** filtran las tres pestañas. **Exportar a CSV (Excel)** guarda lo que estás viendo.

## Cómo se calcula

| Pregunta | De dónde sale |
|---|---|
| ¿Qué es cuenta por cobrar y qué por pagar? | `DocRecipient` del módulo: 1 = cliente, 2 = proveedor (se leen los parámetros de `ModuleIDBase`, así los módulos clonados cuentan igual que el original). |
| ¿Qué suma y qué resta? | `FinancialAffectation` (±1). Una factura suma; una **nota de crédito resta** (en clientes y en proveedores). Pedidos y cartas porte no cuentan. |
| Saldo a la fecha de corte | `Total` − pagos vigentes con `DateOperation` ≤ corte − notas de crédito aplicadas (`PaymentWithDocumentID`). **No** usa `docDocument.Balance`: esa columna solo es la foto de hoy. |
| Vencimiento | La última fecha de la agenda de pago (`docDocumentPaymentAgenda`); sin agenda, la fecha del documento. Las notas de crédito no vencen. |
| Antigüedad | Días entre el vencimiento y la fecha de corte. |

Se excluyen los documentos eliminados y cancelados.

## Qué garantiza

- Con corte = hoy, el saldo de cada documento coincide con `docDocument.Balance`; con un corte anterior coincide con un cálculo independiente en SQL (prueba de humo #38).
- Un documento viejo que se pagó hace poco **sí** aparece en un corte donde aún se debía.
- Una nota de crédito aplicada a una factura **no** se cuenta dos veces en el estado de cuenta: ya restó como documento.

## Límites

- Se carga el historial de **los últimos 12 meses** y todo lo que sigue con saldo o tuvo pagos en ese lapso. Una fecha de corte anterior a ese lapso no es confiable (la ventana no deja elegirla).
- Los importes se muestran **en la moneda de cada documento**, sin convertir a pesos. Si manejas dólares, revisa que no se mezclen en un mismo total.
- Un cobro o pago registrado con el módulo de Tesorería de Comercial se lee igual que uno hecho por cualquier otro medio: todo vive en `docDocumentPayment`.
- Los anticipos y pagos **sin aplicar** a un documento no se ven aquí (no están ligados a ningún documento).

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «No se encontraron módulos con afectación financiera» | La empresa no tiene configurados los parámetros `FinancialAffectation`/`DocRecipient` de sus módulos. |
| Un documento no aparece | Está cancelado o eliminado, es anterior a la ventana de 12 meses y sin saldo, o su módulo no afecta saldos (`FinancialAffectation = 0`). |
| El total no coincide con el reporte nativo | Compara la fecha de corte y la moneda; el reporte nativo puede incluir anticipos. |

## Para desarrolladores

**Modo de pruebas (sin ventanas).** Con `BROSLMV_SALDOS_TEST` (JSON, por ejemplo `{"meses":12}`) corre en `BrosLMV.Runner` (lleva `// job: safe-offline`) y escribe el modelo completo (documentos y pagos) en el archivo de `BROSLMV_SALDOS_OUT`
y el HTML de la ventana en `BROSLMV_SALDOS_HTML`. Prueba de humo: `build/humo/casos/38_saldos_estados_cuenta.ps1`.
Datos de demostración en el laboratorio: `build/laboratorio/sembrar_demo_saldos.ps1` (10 facturas «DEMO SALDOS…» con cobros y pagos aplicados, en `BROSLMV_DESARROLLO`).

**Cobros y pagos sembrados.** Comercial no expone una función para aplicar un cobro o pago; la siembra usa la receta SQL de siete tablas de `puntodeventa/lib/AplicarCobro.ctx` (módulo 248 cobro a cliente, 247 pago a proveedor). Es solo para datos de prueba.
