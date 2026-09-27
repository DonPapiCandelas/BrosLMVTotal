# Motor de Asientos Contables — BrosLMV

> **Estado: validado en producción** con más de un cliente real (cobros y pagos
> multi-moneda). Construido 100% como scripts `.ctx` + tablas propias — **no requirió
> ningún cambio al addon** (`ctx.EventoId`, la pieza que lo hace posible, ya existía).
> Esta capacidad es **opcional y por cliente** — no se instala sola con el addon; se
> activa corriendo `instalador/sql/motor_asientos_contables.sql` y publicando los
> scripts que la usan.

## Por qué existe

Comercial genera pólizas solas (`accPoliza`/`accPolizaTransaccion`) al guardar un
documento, usando sus "Definición de asientos" nativas (`accPolizaDefinition*` — ver
`docs/ESTADO.md` y el hallazgo de sesión sobre `AliasAccountNumber`). Eso alcanza para
la mayoría de los casos. **No alcanza cuando la cuenta de un cliente/proveedor debe
variar según la moneda del cobro/pago** — el catálogo de cuentas sí soporta una cuenta
por moneda, pero el mecanismo nativo de segmento (`orgSupplier.Segmento`/
`orgCustomer.Segmento`) solo guarda **un** valor por tercero, sin importar la moneda.

Este motor resuelve exactamente eso: una **fórmula de cuenta con variantes por
moneda**, condiciones por partida, y todo lo demás que hace falta para que el asiento
cuadre solo (fluctuación cambiaria, redondeo, retenciones, IVA a dos tipos de cambio).
No reemplaza el motor nativo — es un motor **paralelo**, que se activa por evento
(`Propiedades > Avanzado > Evento=Guardar`) y escribe en las mismas tablas nativas
(`accPoliza`/`accPolizaTransaccion`) que usaría Comercial, pero calculadas por BrosLMV.

## Arquitectura

```text
Cobro/Pago en Comercial
  -> docFinancialOperation (encabezado: fecha, importe, moneda, banco, tercero)
  -> docDocumentPayment (aplicación a cada factura)
  -> docFinancialOperationTaxDetail (impuestos del cobro/pago)
  -> Motor de Asientos Contables BrosLMV
  -> accPoliza + accPolizaTransaccion + asociaciones (MetodoPago/Comprobante)
  -> sincronización a Contabilidad: la hace Comercial (nativa), NUNCA este motor
```

## Tablas propias

Definidas en `instalador/sql/motor_asientos_contables.sql` (opcional, no se corre
en `provision_empresa.sql`):

| Tabla | Propósito |
|---|---|
| `zzBrosAsientoContable` | Encabezado de cada definición de asiento (nombre, tipo de póliza, fecha, concepto de encabezado). |
| `zzBrosAsientoContableModulo` | Módulo(s) de Comercial donde aplica (ej. 248 = Cobros Cliente, 247 = Pagos Proveedor). |
| `zzBrosAsientoContablePartida` | Las partidas (renglones) del asiento — ver columnas abajo. |
| `zzBrosPolizaCobroEstado` / `zzBrosPolizaPagoEstado` | Huella de idempotencia por operación (una tabla por tipo, nunca compartida). |
| `vwBrosAsientosContablesList` | Vista de listado para una pantalla de administración. |

### Columnas de `zzBrosAsientoContablePartida`

| Columna | Qué es |
|---|---|
| `TipoMovimiento` | `Cargo` o `Abono`. Un importe negativo pasa al lado contrario, salvo `ValorAbsoluto`. |
| `CuentaFormula` | Ver "Sintaxis de cuenta" abajo. |
| `FuenteImporte` | Clave del catálogo `MotFuentes` del motor (`COBRO_IMPORTE`, `DOC_TOTAL_TCF`, etc. — ver tabla completa abajo). |
| `Porcentaje` | 100 = importe completo; permite prorratear una partida. |
| `Concepto` / `Referencia` | Plantillas de texto con variables `[Campo]`, evaluadas por partida (máx. 255 caracteres, límite de `accPolizaTransaccion`). |
| `SuprimirCero` | No genera el renglón si el importe (MXN y M.E.) da 0. |
| `ValorAbsoluto` | Fuerza el importe a positivo antes de decidir Cargo/Abono. |
| `Concentrar` | Une en un solo renglón todas las líneas de la misma partida/cuenta/moneda/TC generadas por distintos documentos. |
| `CondicionesJson` | Lista de reglas `{"campo","operador","valor"}`, todas unidas por Y — la partida se omite si alguna no se cumple. |

## Sintaxis de cuenta (`CuentaFormula`)

| Forma | Resolución |
|---|---|
| `2960-00-2960` | Literal — busca ese valor en `AliasAccountNumber` o `AccountNumber` del catálogo. |
| `[CuentaBanco]` | Segmento de `orgFinancialEntity` (10 dígitos) formateado como `NNNN-NN-NNNN`. |
| `*1201-01-*CL` | Segmento contable del **cliente** (`orgCustomer.Segmento`, 4 dígitos) insertado en el prefijo — así se logra una cuenta distinta por moneda: `*1201-01-*CL` para MXN, `*1201-02-*CL` para USD, ambas con el mismo segmento de cliente. |
| `*2101-01-*PV` | Igual, pero segmento de **proveedor** (`orgSupplier.Segmento`) — solo en el motor de pagos. |
| `ID:<AccountingCatalogID>` | Cuenta explícita por `AccountingCatalogID` — evita ambigüedad si el catálogo tiene alias repetidos. |

**Esto es lo que resuelve la limitante real de la resolución nativa** (un solo
segmento por tercero, sin distinción de moneda): la variante de moneda no vive en el
segmento del tercero, vive en el **prefijo literal** de la fórmula (`1201-01-` vs
`1201-02-`), y el segmento del tercero solo aporta el sufijo. Cada combinación
prefijo+segmento debe existir como cuenta real en el catálogo de antemano.

## Catálogo de `FuenteImporte` (motor de cobros)

| Clave | Nivel | Qué importe da |
|---|---|---|
| `COBRO_IMPORTE` | cobro | El importe total del cobro/pago (banco). |
| `COBRO_SIN_APLICAR` | cobro | Lo que no se aplicó a ningún documento (saldo a favor / anticipo). |
| `DOC_TOTAL_TCF` / `DOC_TOTAL_TCC` | documento | Total aplicado, a tipo de cambio de factura / de cobro. |
| `DOC_BASE_TCF` / `DOC_BASE_TCC` | documento | Base sin IVA, a TC factura / cobro. |
| `DOC_IVA_TCF` / `DOC_IVA_TCC` | documento | IVA, a TC factura / cobro. |
| `DOC_RET_TCF` / `DOC_RET_TCC` | documento | Retenciones, a TC factura / cobro. |
| `DOC_FLUCT_BASE` / `DOC_FLUCT_IVA` / `DOC_FLUCT_TOTAL` | documento | Fluctuación cambiaria (+utilidad / −pérdida) de la base, IVA o total. |
| `AJUSTE_DIFERENCIA` | ajuste | Cierra un descuadre de redondeo — **tope duro $1.00 MXN**, solo si la operación está totalmente aplicada. Nunca cubre un cobro/pago sin aplicar de verdad. |

El motor de **pagos** agrega el nivel `impuesto` (`IMP_TCF`/`IMP_TCC`/`IMP_FLUCT`, un
renglón por cada impuesto/retención de cada documento — necesario porque un pago puede
traer varios impuestos distintos por factura: IVA 16%, IVA 8%, ISR, IEPS, locales).

**TCF vs TCC**: `TCF` es el tipo de cambio de la factura/provisión; `TCC` el del
cobro/pago. La póliza siempre se presenta en MXN (Debe/Haber); las columnas `M.E.`
llevan el importe en moneda extranjera. La diferencia entre ambos tipos de cambio es
justo la fluctuación cambiaria.

## Condiciones (`CondicionesJson`)

Lista de reglas, todas unidas por Y — ejemplo real:

```json
[{"campo":"MonedaDocumento","operador":"!=","valor":"USD"}]
```

Campos de texto (comparables solo con `=`/`!=`): `MonedaCobro`, `MonedaDocumento`,
`CobroTotalmenteAplicado`, y en pagos también `ClaseImpuesto`/`TipoImpuesto`/
`EsRetencion`. Campos numéricos (admiten `=`,`!=`,`<`,`<=`,`>`,`>=`): `Retencion`,
`Fluctuacion`, `DiferenciaAbsoluta`, `Tasa`. El motor lanza un error claro si una
partida usa un campo de condición que no aplica a su `FuenteImporte`.

## El evento que lo dispara: `ctx.EventoId`

Ya existe en el addon (`src/ClsMain.cs`/`Scripting.cs`), sin necesitar ningún cambio
para este motor. En **Propiedades del módulo → Avanzado → Ejecutar función**, con
Evento = **Guardar**:

```text
BrosLMV.<NombreDelScript>_[FinancialOperationID]
```

Comercial sustituye `[FinancialOperationID]` como texto **antes** de invocar, así que
el AppKey llega literal como `<NombreDelScript>_12345`. `ClsMain.EjecutarScript` no
encuentra ese AppKey exacto, reintenta con el nombre base, y expone el número en
`ctx.EventoId` — el script debe darle prioridad sobre lo que esté seleccionado en el
grid (guardar una ventana minimizada no debe generar la póliza de otro registro).
Funciona igual para cualquier token que Comercial sepa sustituir (`[DocumentID]`,
`[FinancialOperationID]`, etc.), no es exclusivo de este motor.

## Idempotencia y el límite de la sincronización

Cada póliza propia usa una referencia estable en `accPoliza.Reference`:
`BROSLMV_ASIENTO_COBRO_<FinancialOperationID>` / `BROSLMV_ASIENTO_PAGO_<...>`.

Al ejecutar el script:

1. Busca una póliza vigente con esa referencia.
2. Calcula una huella (`InputHash`) del origen (documento, aplicaciones, impuestos,
   definición del asiento y cuentas resueltas) y la compara contra la guardada en
   `zzBrosPolizaCobroEstado`/`PagoEstado`.
3. Si no cambió: no reconstruye nada. Solo abre la póliza existente.
4. Si cambió **y la póliza no está sincronizada**: borra solo sus propios movimientos
   y asociaciones, conserva encabezado y `PolizaID`, y los vuelve a escribir.
5. **Si `accPoliza.SynchronizedOn IS NOT NULL`, la póliza es intocable** — el motor la
   abre pero nunca la borra ni reconstruye. Un cambio posterior exige decisión
   contable (corrección/cancelación en Contabilidad), nunca "regenerar" esperando que
   sustituya lo ya enviado.

**Este motor nunca sincroniza a Contabilidad.** Genera/actualiza la póliza *local* en
Comercial; el envío lo hace Comercial por su botón/proceso nativo o por la
configuración de sincronización automática de la empresa (la misma pantalla
"Opciones → Contabilidad → Sincronizar con contabilidad" que ya existe en Comercial).

## Reglas de operación (obligatorias)

1. **Nunca insertar una póliza propia directo por SQL** — además del encabezado y
   movimientos, necesita las asociaciones a pago/cobro (`accPolizaTransaccionMetodoPago`)
   y comprobante (`accPolizaTransaccionComprobante`) para que los listados nativos de
   Comercial la relacionen bien con la operación.
2. **Nunca editar ni borrar una póliza con `SynchronizedOn` distinto de `NULL`.**
3. **Nunca crear registros de `docFinancialOperationTaxDetail` con SQL** — depende de
   parcialidades, proporciones, monedas y tipos de cambio; debe recalcularse desde
   Comercial/SDK. (Un pago guardado desde Comercial puede regenerar este detalle solo
   — si el motor corrió antes de que existiera, la póliza sale incompleta; volver a
   guardar el pago en Comercial y regenerar la póliza después.)
4. **El motor de pagos usa `LEFT JOIN`, no `JOIN`**, contra `docFinancialOperationTaxDetail`
   — si un `JOIN` interno se reintroduce, cualquier aplicación sin detalle fiscal
   desaparece silenciosamente de la póliza (solo queda el renglón de banco,
   descuadrada). Ver hallazgo real documentado más abajo.
5. Antes de una corrección masiva: respaldo de la base + corrida de auditoría, lotes
   pequeños, revisar el resultado antes de seguir.
6. Publicar cambios a un script por el mecanismo normal de BrosLMV (`BrosGuardar`,
   respaldo automático en `zzBrosScriptHist`) — nunca editar `zzBrosScript` a mano.

## Hallazgo real documentado: pagos sin detalle fiscal

En una validación de producción se encontraron **222 aplicaciones de 158 pagos** sin
registro en `docFinancialOperationTaxDetail` (198 de esas facturas sí tenían impuestos
de origen). Con un `JOIN` estricto, el motor no encontraba el documento aplicado y
generaba solo la partida de banco — póliza incompleta/descuadrada. Confirmado con un
caso real: un pago generó solo Banco por $31,900; al volver a guardarlo en Comercial se
recreó el detalle fiscal (IVA base $27,500 + $4,400) y la póliza regenerada quedó
cuadrada a $36,300. **Corrección:** `LEFT JOIN` en el motor de pagos (ya aplicado en el
código de este repo) + nunca insertar el detalle fiscal con SQL, siempre recalcularlo
desde Comercial.

## Pendiente conocido: editor visual

Existe un diseño (no una decisión de implementación cerrada) para un editor de
asientos con constructor de texto/variables, tabla de partidas y validación previa al
guardar, con hallazgos reales sobre la versión inicial del editor: el campo `Concepto`
de partida en algunas versiones no llegaba a influir en la póliza generada (el motor
usaba texto fijo por tipo de partida), el guardado no era transaccional, y varios
campos se mostraban como editables sin que el motor los usara todavía. **Regla de oro
para cuando se construya:** no publicar una pantalla que permita guardar una opción que
el motor de cálculo todavía ignore.

## Qué falta para que esto sea un producto BrosLMV completo

- Generalizar el nombre de los scripts (`GenerarPolizaCobro`, `VisualizarPolizaCobro`,
  `EditarAsientoContable`, `NuevoAsientoContable`, y su espejo de pagos) como
  plantillas reusables en `instalador/scripts/`, parametrizadas por módulo — hoy solo
  existe el motor de cálculo (`instalador/scripts/motor/`) de forma genérica.
- Construir el editor visual descrito arriba, respetando su regla de oro.
- Documentar en `MANUAL.md` el patrón `ctx.EventoId` como capacidad general del addon
  (no solo de este motor) — pendiente independiente, ver `docs/PLAN_IMPLEMENTACION.md`.
