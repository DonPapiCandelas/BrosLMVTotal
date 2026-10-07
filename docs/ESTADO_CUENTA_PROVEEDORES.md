# Estado de cuenta de proveedores (cuentas por pagar)

Plantilla de fábrica **Plantillas → Reportes → Estado de cuenta de proveedores (cuentas por pagar)** (`ESTADO_CUENTA_PROVEEDORES.ctx`). Cuentas por pagar y el estado de cuenta de cada proveedor, con **fecha de corte**, **antigüedad de saldos**, **calendario de vencimientos** y el **detalle de cada documento**.
Solo lee: no cambia ningún documento.

> **Dos botones separados, a propósito.** Cuentas por cobrar (clientes) y cuentas por pagar (proveedores) las ven usuarios distintos: este botón solo carga **proveedores**. «Estado de cuenta de clientes» es otra plantilla con su propio botón; así cada usuario ve únicamente la información del módulo que le corresponde (y se la puedes dar o quitar con los permisos del botón).

## Cómo usarla

1. En la **Consola** abre la plantilla, guárdala como botón (nombre sugerido `ESTADO_CUENTA_PROVEEDORES`) y ponla en el ribbon con *Crear botón…*.
2. Pulsa el botón. Se abre una ventana **que no bloquea Comercial**: puedes minimizarla y seguir trabajando.
3. Arriba, **tarjetas de resumen**: vencido, vence en 7 días, vence la semana que sigue, vigente, notas de crédito a favor y saldo total.
4. **Fecha de corte.** Por omisión es hoy. Al cambiarla todo se recalcula al instante: ve cuánto se debía en esa fecha, aunque el documento se haya pagado después.
5. **Antigüedad por proveedor.** Una fila por proveedor con su saldo en *Vigente*, *1-30*, *31-60*, *61-90* y *Más de 90* días de vencido, y *A favor* (notas de crédito sin aplicar). Clic en una fila abre su estado de cuenta.
6. **Documentos con saldo.** Cada documento con su vencimiento, días de atraso y saldo. **Clic** (o el botón *Ver*) abre el detalle: partidas, agenda de pago y pagos aplicados; ahí mismo, **Abrir en Comercial** abre el documento y la ventana sigue abierta.
7. **Calendario.** Cada día suma el saldo de los documentos que vencen ese día (rojo = vencido, ámbar = vence en 7 días, verde = vigente). Clic en un día lista sus documentos.
8. **Estado de cuenta.** Elige el proveedor y el periodo: saldo inicial, cada movimiento (documentos como cargos; pagos y notas de crédito como abonos) con saldo corriente, y totales del periodo.
9. **Buscar** (nombre, folio o título), **Solo vencidos** y los botones **Vence esta semana / Vence la semana que sigue** filtran las pestañas. **Exportar a CSV** guarda lo que estás viendo.
10. **Exportar a Excel** arma un **libro ejecutivo** (ver abajo) con todo el corte, sin importar los filtros de pantalla. En la pestaña *Estado de cuenta*, **Excel de este estado de cuenta** genera el estado de cuenta del proveedor elegido, listo para mandárselo.

## El Excel ejecutivo

Se guarda con el cuadro «Guardar como» y, al terminar, ofrece abrirlo. Trae cuatro hojas:

| Hoja | Qué lleva |
|---|---|
| **Resumen** | Encabezado con la empresa y la fecha de corte; seis tarjetas (vencido, vence en 7 días, semana que sigue, vigente, notas de crédito a favor, saldo neto); una **lectura rápida** en tres frases con los números reales; **cuatro gráficas** (antigüedad de saldos, principales proveedores, vencimientos de las próximas 8 semanas y situación del saldo) y la tabla de los 10 mayores saldos con su % del total y % vencido. |
| **Antigüedad** | Una fila por proveedor con sus saldos por antigüedad, % vencido (con escala de color), barra de datos en el saldo total, totales, filtros y paneles fijos. |
| **Documentos** | Todos los documentos con saldo: fechas y moneda reales (se pueden ordenar y filtrar en Excel), días vencido, estatus con color por fila y una **fila de totales que se recalcula con el filtro** (`SUBTOTAL`). |
| **Vencimientos** | Saldo que vence cada semana (8 semanas) y lo ya vencido, con su gráfica. |

Todas las hojas traen configuración de impresión (horizontal, ajustada al ancho, encabezado y pie con número de página). El libro se arma en C# con ClosedXML (necesita `C:\BrosLMV\lib\ClosedXML.dll`, que pone el instalador); las gráficas se dibujan en la ventana y viajan como imagen, y sus números están en las tablas del libro.

**Volumen.** El libro no depende de cuántos documentos haya: con **30,000 documentos** se genera en unos 30 segundos (6,000 en unos 15) y las gráficas siempre resumen (principales 10, 8 semanas). En pantalla la pestaña *Documentos con saldo* muestra los primeros **1,000** (con un aviso); el Excel lleva todos. Las páginas de más de ~1.5 MB se cargan desde un archivo temporal, porque WebView2 corta `NavigateToString` en 2 MB.

## Cómo se calcula

| Pregunta | De dónde sale |
|---|---|
| ¿Qué módulos entran? | Solo los de proveedores: `DocRecipient = 2` del módulo (se leen los parámetros de `ModuleIDBase`, así los módulos clonados cuentan igual que el original). El otro lado no se consulta. |
| ¿Qué suma y qué resta? | `FinancialAffectation` (±1). Una factura suma; una **nota de crédito resta**. Pedidos y cartas porte no cuentan. |
| Saldo a la fecha de corte | `Total` − pagos vigentes con `DateOperation` ≤ corte − notas de crédito aplicadas (`PaymentWithDocumentID`). **No** usa `docDocument.Balance`: esa columna solo es la foto de hoy. |
| Vencimiento | La última fecha de la agenda de pago (`docDocumentPaymentAgenda`); sin agenda, la fecha del documento. Las notas de crédito no vencen. |
| Antigüedad | Días entre el vencimiento y la fecha de corte. «Vence en 7 días» = vence entre mañana y los próximos 7 días respecto al corte. |

Se excluyen los documentos eliminados y cancelados.

**Documentos que se convierten en factura** (una venta, por ejemplo): la venta **no** se cuenta otra vez cuando ya se facturó, porque la deuda la lleva la factura. La regla es la misma que usa Comercial en su columna «Facturado» (`vwLBSDocCustomerSalesTotalInvoiced`): se suman las facturas vigentes cuyo `SourceDocumentID` es ese documento y al total del documento se le resta lo facturado (sin bajar de cero). Una venta facturada por completo desaparece del reporte y la factura la sustituye; si se facturó una parte, solo cuenta lo que falta por facturar. En el detalle del documento aparece un aviso cuando esto ocurre. Las facturas y notas de crédito nunca se descuentan por esta regla.

## Qué garantiza

- Con corte = hoy, el saldo de cada documento coincide con `docDocument.Balance`; con un corte anterior coincide con un cálculo independiente en SQL (prueba de humo #38).
- Este botón **nunca** trae documentos del otro lado (la prueba de humo lo comprueba).
- Un documento viejo que se pagó hace poco **sí** aparece en un corte donde aún se debía.
- Una nota de crédito aplicada a una factura **no** se cuenta dos veces en el estado de cuenta: ya restó como documento.

## Límites

- Se carga el historial de **los últimos 12 meses** y todo lo que sigue con saldo o tuvo pagos en ese lapso. Una fecha de corte anterior a ese lapso no es confiable (la ventana no deja elegirla).
- Las **partidas y la agenda** se cargan solo de los documentos que **hoy** tienen saldo; los ya liquidados muestran sus pagos pero no sus partidas.
- Los importes se muestran **en la moneda de cada documento**, sin convertir a pesos. Si manejas dólares, revisa que no se mezclen en un mismo total.
- Los anticipos y pagos **sin aplicar** a un documento no se ven aquí (no están ligados a ningún documento).

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «No se encontraron módulos de proveedores con afectación financiera» | La empresa no tiene configurados los parámetros `FinancialAffectation`/`DocRecipient` de sus módulos. |
| Un documento no aparece | Está cancelado o eliminado, es anterior a la ventana de 12 meses y sin saldo, o su módulo no afecta saldos (`FinancialAffectation = 0`). |
| El total no coincide con el reporte nativo | Compara la fecha de corte y la moneda; el reporte nativo puede incluir anticipos. |
| «No se pudo generar el Excel» | Falta `C:\BrosLMV\lib\ClosedXML.dll` (la pone el instalador; reinstala BrosLMV) o el archivo está abierto en Excel. |
