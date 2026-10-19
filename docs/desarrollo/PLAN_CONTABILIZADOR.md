# Plan: Contabilizador de BrosLMV (pólizas de Contabilidad a partir de CFDI)

> Estado (2026-10-18): decisiones del dueño tomadas (empezar por **compras y ventas**, luego pagos; reglas editadas **en la app**; **biblioteca de expresiones**; **sin nómina**; importar los asientos nativos). **Fase 3.0 hecha en Descargas 3.3.0** (servicio del SDK probado en vivo). **Fase 3.1 (terceros y cuentas por rol/moneda, sincronización y Excel) hecha en 3.4.0**; falta crear cuentas/terceros por SDK y aprender de pólizas históricas (3.1.1). La importación de los asientos nativos va con las reglas (3.2). Antes: plan para revisión. Sustituye y detalla la «Fase 3» de
> `PLAN_MAPA_CFDI_Y_CONTABILIZADOR.md`. Se apoya en lo que ya existe en Descargas 3.2.0 (descarga del SAT, mapa de CFDI por sistema,
> carga al ADD de Contabilidad y de Premium, expediente en Excel) y en el análisis de cómo guarda CONTPAQi Contabilidad sus propios asientos.

## 1. Objetivo

Que **casi cualquier póliza** se pueda construir a partir de un CFDI —provisión de compra o gasto, venta, pago y cobro (con complemento de pago),
notas de crédito, cancelaciones, anticipos, nómina, retenciones, impuestos locales, otra moneda—, con una **flexibilidad muy por encima** de lo que
ofrece hoy el configurador de asientos de Contabilidad y de Comercial Pro:

- **Reglas por empresa**, con condiciones (moneda, método de pago, tipo de comprobante, concepto, tercero, monto…) y fórmulas de importe.
- **Cuentas por tercero, por rol y por moneda** (provisión en pesos, en dólares, en euros, anticipos, gastos…), no un solo campo.
- **Memoria**: aprende de pólizas ya hechas y, al pagar, **encuentra la provisión original** (qué cuenta de pasivo se usó, a qué tipo de cambio, cuánto falta por pagar).
- **Trabajo masivo en Excel** (libro `.xlsx` sin macros, con fórmulas «vivas»): se revisa, se edita y **solo entonces** se genera la póliza.
- **Seguro**: nunca duplica, valida contra Contabilidad antes de escribir, deja bitácora y dice cómo deshacer.

## 2. Punto de partida: lo que ya tiene CONTPAQi

Revisado en la base de Contabilidad de prueba (solo lectura):

| Pieza nativa | Cómo la guarda | Límite que nos interesa superar |
|---|---|---|
| **Configurador de asientos** | `Asientos` (cabecera: frecuencia, fecha, tipo de póliza, diario, tipo de XML, «es gasto») y `MovimientosAsiento` (por línea: `FormulaCuenta`, `FormulaImporte`, `FormulaImporteME`, referencia, concepto, segmento) | Una línea = una cuenta; sin condiciones («si es dólar usa esta»); sin prorrateos reales; sin memoria de provisión |
| **Importes disponibles** | Variables por CFDI: Total, Subtotal, Descuento, Base y IVA 16/8/0/exento, IEPS, retenciones IVA/ISR, Hospedaje, TUA, Cedular, Millar, Otras erogaciones, «Complementaria»… | Sirve como **lista de variables mínima** que debemos igualar |
| **Cuenta por fórmula** | Nombres como `Gastos_Proveedor`, `Pasivo_Proveedor`, `Por captar`, o segmentos `PV01+TO01+PR01+CP01` | Un tercero tiene **una** cuenta de pasivo y **una** de gastos |
| **Cuentas del tercero** | `Proveedores`/`Clientes`: `IdCuenta`, `CodigoCuenta`, `CodigoCtaGastos`, `CodigoCtaComplementaria`, `CodigoCtaGastosComplementaria`, segmentos `SegContProveedor1..7`, `CodigoAsiento` | No distingue moneda ni anticipos; el campo único es el cuello de botella que describió el dueño |
| **Segmentos** | Clientes (CL), Proveedores (PV), Responsables de gastos (RG), Productos y servicios SAT (PR), Productos (CP), Tipos de operación (TO), Cuentas bancarias (CB), Segmentos de negocio (SN), 7 posiciones cada uno | Hay que **respetarlos**: una cuenta puede salir de pegar segmentos |
| **Cuentas** | `Cuentas`: `Afectable`, `IdMoneda`, `IdSegNeg`, `IdAgrupadorSAT`, `CtaMayor`… | Ya sabemos qué cuentas son afectables y de qué moneda: se puede **detectar** la cuenta correcta |
| **CFDI ↔ póliza** | `AsocCFDIs` (GuidRef = póliza o movimiento, UUID, AppType) | Base de la memoria y de «ya contabilizado» |

**Decisión de diseño:** BrosLMV **no depende** del configurador nativo ni lo modifica. Tiene su propio motor, pero **lo puede leer** para
sembrar reglas (importar `Asientos`/`MovimientosAsiento` y `Proveedores`) y respeta su vocabulario de variables y segmentos.

## 3. Principios

1. **Todo es dato, nada es código por cliente**: reglas, cuentas y prorrateos viven en SQL por empresa (versionados), no en scripts.
2. **Primero se calcula, luego se escribe**: el flujo siempre pasa por un *borrador* editable y validado; Contabilidad solo se toca al final.
3. **Una verdad por cosa**: el catálogo de cuentas y los terceros se leen de Contabilidad; lo que BrosLMV añade (roles, monedas, prorrateos, aprendizaje) vive en sus tablas.
4. **Todo explicable**: cada línea de póliza sabe **qué regla** la generó, **qué variable** y **de dónde salió la cuenta** (manual, tercero, regla, aprendida, patrón).
5. **Idempotente**: el CFDI ya contabilizado (según el mapa y `AsocCFDIs`) no entra otra vez por accidente.
6. **Herramienta local**: sin servidor central; solo habla con SQL local y con CONTPAQi.
7. **Paridad con el mapa**: el rol de cada póliza (provisión, pago, cancelación…) alimenta el mapa de CFDI.

## 4. Arquitectura (capas)

```
 XML (disco / ADD)
      │
 [1] Modelo del CFDI  ── hechos normalizados: importes por concepto y tasa, retenciones, locales,
      │                   complementos, moneda/TC, método y forma de pago, relacionados, pagos (REP)
      ▼
 [2] Motor de reglas  ── elige la regla (ámbito + prioridad) y evalúa condiciones y fórmulas
      │
 [3] Resolutor de cuentas ── cadena de resolución (§6) con memoria; marca lo que falta
      │
 [4] Borrador (SQL)    ── lotes → pólizas → movimientos, con estado y errores por línea
      │
 [5] Excel «vivo»      ── ida (generar) y vuelta (leer cambios)
      │
 [6] Validador         ── contra Contabilidad: cuenta afectable, moneda, ejercicio abierto, tipo/diario/segmento, duplicados
      │
 [7] Registrador (SDK) ── crea póliza + asocia CFDI (póliza y movimiento)
      │
 [8] Bitácora / memoria ── qué se hizo, con qué regla, cómo deshacer; alimenta el mapa y la memoria de provisión
```

Todo en Descargas (net8), con acceso a Contabilidad por **SDK COM** (fuera de proceso, funciona desde 64 bits) para escribir, y por **SQL de solo lectura** para leer catálogos y estado.

## 5. Modelo de datos (tablas nuevas en `BrosLMV_SAT`)

Todas llevan `RFC` (empresa) y se crean solas como el resto del esquema. Prefijo `Conta`.

**Configuración de la empresa**
- `ContaEmpresa`: base de Contabilidad, usuario/clave (DPAPI), ejercicio, tipos de póliza por defecto, diario, opciones (afectar o no, fecha de póliza, redondeo).
- `ContaCuentaCache`: copia del catálogo (código, nombre, moneda, afectable, segmento de negocio, rubro, agrupador SAT, baja) con fecha de lectura; se **refresca** con un botón y antes de validar.
- `ContaRolCuenta`: cuentas generales por **rol** (IVA acreditable pagado, IVA acreditable pendiente, IVA trasladado cobrado/pendiente, retención ISR/IVA, ISH, TUA, ingresos 16 %/0 %/exento, descuentos, diferencia cambiaria ganada/perdida, redondeo, bancos…) con **condición de moneda** y vigencia.

**Terceros y cuentas (el nivel que pide el dueño)**
- `ContaTercero`: RFC, nombre, tipo (cliente / proveedor / ambos / extranjero), código en Contabilidad, origen (leído / creado por BrosLMV), segmentos CL/PV/RG…
- `ContaTerceroCuenta`: **una fila por tercero + rol + moneda**: rol (*Provisión/Pasivo*, *Gasto*, *Anticipo*, *Cliente*, *Ingreso*, *Complementaria*, *Retenciones*…), moneda (MXN/USD/EUR/cualquiera), cuenta, segmentos, **origen** (manual · leída de Contabilidad · aprendida de pólizas · creada con patrón), confianza, vigencia. Así un proveedor puede tener cuenta en pesos, en dólares, en euros y de anticipos.
- `ContaClasificacion`: reglas **por concepto** para la cuenta de gasto o ingreso: clave de producto/servicio SAT, palabras de la descripción, tercero, importe → cuenta + segmento + prioridad (una factura con varios conceptos puede repartirse en varias cuentas).
- `ContaProrrateo` / `ContaProrrateoDetalle`: un gasto repartido por % entre cuentas y segmentos de negocio; por tercero, concepto o regla; con vigencia.

**Reglas de póliza**
- `ContaRegla`: cabecera: nombre, **ámbito** (tipo de comprobante I/E/P/N/T, emitido/recibido, método PUE/PPD, moneda, complemento, rol de la póliza), **prioridad**, frecuencia (una por CFDI · diaria · diaria por tercero · por periodo), fecha (del XML / del pago / de contabilización), tipo de póliza, diario, plantilla de concepto, activa, versión.
- `ContaReglaLinea`: orden, lado (cargo/abono), **cuenta por fórmula** (rol + moneda + condiciones, segmentos, o cuenta fija), **importe por fórmula** (§7), condición para incluir la línea, segmento de negocio, referencia, concepto, concentrar movimientos, referencia a prorrateo.
- `ContaVariable`: catálogo de variables disponibles para fórmulas (las del CFDI, las de pago y las derivadas).

**Borradores y resultado**
- `ContaLote` → `ContaBorradorPoliza` → `ContaBorradorMovimiento`: lo que se muestra en Excel; guarda la regla/versión que lo generó, el origen de cada cuenta y los errores.
- `ContaPolizaGenerada`: lo que se escribió: UUID, rol, GUID de póliza y de movimiento, tipo, ejercicio, periodo, número, usuario, fecha, resultado, lote.
- `ContaProvision`: **memoria de provisión** (§8): UUID → póliza de provisión, cuenta de pasivo usada, moneda, TC, importe provisionado, importe pagado, saldo.
- `ContaBitacora`: eventos (generar, editar, validar, escribir, error, deshacer).

## 6. Resolución de cuentas (cadena con memoria)

Para cada línea que necesita cuenta, se prueba **en este orden** y se guarda de dónde salió:

1. **Edición manual** en el borrador (siempre gana).
2. **Prorrateo** activo para esa línea.
3. **Tercero + rol + moneda** (`ContaTerceroCuenta`).
4. **Clasificación por concepto** (`ContaClasificacion`).
5. **Regla** (cuenta fija o por segmentos) y **rol general** (`ContaRolCuenta`) según moneda.
6. **Cuenta nativa de Contabilidad** del tercero (`CodigoCuenta`, `CodigoCtaGastos`…) como sugerencia.
7. **Aprendida**: la cuenta usada la última vez con ese tercero (y concepto) en pólizas asociadas (`AsocCFDIs` → movimientos).
8. **No encontrada** → la línea queda en rojo y se ofrece: elegir cuenta, **crear la cuenta** con una cuenta *patrón* (SDK) o **crear el proveedor/cliente** en Contabilidad.

**Detección de cuentas existentes:** por RFC y nombre se busca al tercero en Contabilidad; por código/segmento y moneda se busca la cuenta. Una cuenta de **otra moneda** no se acepta para una línea en esa moneda (se avisa). El **catálogo** se compara siempre contra `ContaCuentaCache` fresca.

**Ventana de terceros** (estilo «asignar cuentas», pero por rol y moneda): lista de proveedores/clientes de los CFDI del periodo con una columna por rol/moneda, filtro «sin cuenta», botones *Detectar*, *Crear cuenta*, *Crear tercero*, *Copiar de otro*, y la **misma tabla editable en Excel** (exportar → editar masivamente → importar con validación).

## 7. Lenguaje de reglas (condiciones y fórmulas)

- **Variables** (por CFDI y por concepto): `Total`, `Subtotal`, `Descuento`, `BaseIVA16/8/0/Exento`, `IVA16`, `IVA8`, `IEPS`, `RetIVA`, `RetISR`, `ISH`, `TUA`, `ImpuestoLocalRetenido`, `ImpuestoLocalTrasladado`, `Complementaria`, `TipoCambio`, `Moneda`, `ImporteMN`, `ImporteME`, y de pago: `PagadoEnPago`, `ProporcionPago`, `IVAProporcional`, `SaldoAnterior`, `SaldoInsoluto`, `DiferenciaCambiaria`; más `Cuadre` (lo que falta para cuadrar).
- **Operaciones**: `+ − × ÷`, `SI(cond, a, b)`, `MAX/MIN/REDONDEAR`, `PRORRATEO('nombre')`, comparaciones por moneda, tercero, método de pago, forma de pago, tasa, clave SAT…
- **Cuentas por fórmula**: rol (`Provision`, `Gasto`, `IVAAcreditablePendiente`…), con alternativas por moneda: `SI(Moneda='USD', rol 'Provision USD', rol 'Provision MXN')`, o fija `'60101001'`, o por segmentos (`PV01+TO01+'000'`).
- **Implementación**: un evaluador de expresiones propio (pequeño, sin ejecutar código arbitrario) o una biblioteca de expresiones permisiva (se decide en la fase 3.2 tras revisar `docs\LIBRERIAS.md`); las mismas fórmulas se **traducen a fórmulas de Excel** para el libro vivo.
- **Plantillas de fábrica** (editables, copiables): compra/gasto (PUE y PPD), venta, nota de crédito recibida/emitida, pago a proveedor y cobro de cliente desde REP, anticipo, cancelación (reversa), nómina, gasto con retenciones, importación/otra moneda.

## 8. Memoria de provisión (pagos y cobros)

Cuando el CFDI es un **pago/cobro** (REP, o PUE con forma de pago bancaria), el contabilizador debe **buscar la provisión** original:

1. Por cada documento relacionado (UUID) del REP, buscar su póliza de provisión: primero en `ContaProvision`; si no, en Contabilidad por `AsocCFDIs` (UUID → póliza → movimientos).
2. Leer **la cuenta de pasivo (o de cliente) que se usó** y su moneda/TC; esa cuenta se **cancela** en el pago (cargo al pasivo / abono al cliente), aunque hoy la cuenta del proveedor sea otra.
3. Calcular **IVA proporcional** al pago (pendiente → pagado), **retenciones** proporcionales y **diferencia cambiaria** (TC de la provisión contra TC del pago).
4. Llevar el **saldo**: parcialidades, pago de varios CFDI en un solo movimiento bancario, pago mayor al saldo, anticipos aplicados.
5. Pedir la **cuenta de bancos** (por forma de pago, por cuenta ordenante/beneficiaria del REP cruzada con las cuentas bancarias de Contabilidad, o la que elija el usuario).
6. Si no hay provisión encontrada: la línea queda marcada («sin provisión»), con opción de usar la cuenta del tercero o un anticipo.

El **rol** de cada póliza (provisión, pago, cancelación, nota de crédito) ya lo entiende el mapa; aquí se vuelve dato duradero.

## 9. El Excel «vivo» (ida y vuelta)

Libro `.xlsx` generado por Descargas (ClosedXML), **sin macros**, con hojas:

| Hoja | Para qué |
|---|---|
| **Resumen** | Lote, regla(s) usadas, totales, pólizas por tipo, errores y advertencias |
| **CFDI** | Una fila por CFDI del lote con sus variables (importes por tasa, retenciones, moneda, TC, método de pago, relacionados) |
| **Pólizas** | Vista previa editable: grupo, fecha, tipo, diario, concepto, cuenta, cargo/abono (MXN y ME), segmento, referencia, UUID, origen de la cuenta, estado; filas con error en rojo; **cuadre por póliza con fórmulas** |
| **Prorrateos** | Porcentajes por cuenta/segmento; al cambiarlos, las pólizas se recalculan |
| **Terceros** | RFC → cuentas por rol y moneda (editable en masa) |
| **Bancos** | Cuenta de bancos por forma de pago / cuenta bancaria |
| **Catálogo** | Cuentas de Contabilidad (afectables, moneda) para listas desplegables |
| **Bitácora** | Qué se hizo, cuándo, por quién |

- Los importes de **Pólizas** son **fórmulas** sobre `CFDI` y `Prorrateos` (base × %, cuadre, sumas iguales): cambiar un porcentaje o una cuenta recalcula sin macros. Descargas lee los **valores** finales.
- **Vuelta**: al leer, se compara con el borrador guardado (hash/versión) para detectar celdas cambiadas, se **revalida** todo y se informa fila por fila.
- **Configuración también en Excel**: reglas, terceros-cuenta y clasificaciones se pueden **exportar, editar masivamente e importar** (con validación y vista de diferencias antes de aplicar).
- **Mismo libro para «simular»** sin escribir nada.

## 10. Validación y registro en Contabilidad

**Validar antes de escribir** (contra SQL fresco): cuenta existe, **afectable**, activa y de la **moneda** correcta; tipo de póliza y diario existen; segmento de negocio válido; **ejercicio y periodo abiertos**; póliza cuadrada (también en ME); número no repetido; el CFDI **está en el ADD** (requisito para asociar) y **no está ya contabilizado** en ese rol; fecha dentro del periodo.

**Registrar (SDK)**: por póliza → crear con sus movimientos → **asociar el UUID a la póliza y a cada movimiento** → releer de la base para **confirmar** (no confiar en el mensaje del SDK) → escribir resultado en bitácora, en el libro y en el mapa. Lotes pequeños con progreso, continuar tras error, reporte final.

**Reversa**: se confirma en laboratorio si el SDK permite **eliminar** una póliza; si no, el «deshacer» es una **póliza de reversa** o instrucciones manuales documentadas. Antes del primer uso se recomienda respaldo y se etiqueta cada lote.

## 11. Interfaz (en Descargas)

Pantalla **Contabilizador** con pasos (no modal; se puede minimizar):
1. **Empresa y conexión** (reutiliza la configuración de Contabilidad).
2. **Catálogo** (sincronizar cuentas, tipos de póliza, diarios, segmentos, monedas).
3. **Terceros y cuentas** (§6).
4. **Reglas** (lista de plantillas de fábrica y propias; editor con vista previa sobre un CFDI real; importar desde el configurador nativo).
5. **Generar** (periodo, tipo, regla, solo no contabilizados según el mapa) → borrador.
6. **Revisar** (en la app y/o en Excel), corregir, simular.
7. **Contabilizar** (validación final, registro, resultados).
8. **Historial** (qué lote creó qué pólizas, con regla y versión).

## 12. Fases, entregables y pruebas

Cada fase es una versión de Descargas, con changelog, docs y commit explicativo. Pruebas en **`ctBROSLMV_DESARROLLO`** (laboratorio), nunca en bases de clientes.

| Fase | Entrega | Cómo se prueba |
|---|---|---|
| **3.0 Laboratorio SDK** | Servicio `ContabilidadSdk`: sesión, catálogos, crear **una** póliza con CFDI asociado, releer, y averiguar si se puede eliminar | Script de laboratorio; enums y comportamiento documentados en `Entrenamiento\contabilidad\` |
| **3.1 Catálogo y terceros** | Tablas `ContaEmpresa/CuentaCache/Tercero/TerceroCuenta/RolCuenta`; sincronización; ventana de terceros con detección, creación de cuentas/terceros; **exportar/importar a Excel** | Empresa de prueba: detectar los 1,225 proveedores; crear cuenta nueva y verla en Contabilidad |
| **3.2 Modelo del CFDI y motor** | Normalizador de variables (3.3/4.0, REP, nómina, locales, complementos) y motor de reglas con condiciones; plantillas compra/venta; **sin escribir** | Comparar la salida contra **pólizas generadas por el configurador nativo** (mismos XML, mismas cuentas) y contra pólizas hechas a mano |
| **3.3 Excel vivo** | Libro de ida y vuelta con fórmulas, prorrateos, validación al volver | Libros editados a propósito con errores; el cuadre y los recálculos se verifican |
| **3.4 Registro** | Validación final, creación de pólizas, asociación de CFDI, bitácora, actualización del mapa, reversa | Lote de 5 → 50 → 500; reintento sin duplicar |
| **3.5 Pagos y cobros** | Memoria de provisión, REP, parcialidades, IVA proporcional, bancos, diferencia cambiaria, anticipos | Casos reales por tipo; multi-moneda con TC distinto |
| **3.6 Resto** | Notas de crédito, cancelaciones, nómina, impuestos locales/complementos, prorrateos avanzados, importar del configurador nativo | Casos reales |
| **4 Sin Excel** | Misma lógica con pantalla directa (opcional) | — |

Orden sugerido: 3.0 → 3.1 → 3.2 → 3.3 → 3.4 (primer valor completo: compras y ventas PUE) → 3.5 → 3.6.

## 13. Riesgos y cómo se cubren

| Riesgo | Cobertura |
|---|---|
| El SDK tiene comportamientos no documentados (enums, redondeos, periodos) | Fase 3.0 de laboratorio antes de diseñar sobre él; confirmación siempre por lectura SQL |
| Duplicar pólizas | Idempotencia por UUID + rol (mapa y `AsocCFDIs`), verificación antes y después |
| Reglas demasiado complejas para el usuario | Plantillas de fábrica, vista previa sobre un CFDI real, importar del configurador nativo, explicación por línea |
| Cuentas en varias monedas mal usadas | `ContaTerceroCuenta` por moneda; el validador rechaza cuenta de otra moneda |
| Cambios en el catálogo mientras se trabaja | `ContaCuentaCache` con fecha y relectura obligatoria antes de validar/registrar |
| Usuario trabajando en Contabilidad al mismo tiempo | Lotes pequeños, reintento, mensajes claros; no se bloquea nada |
| El ADD no tiene el CFDI | El mapa y el botón de carga existentes; el validador lo exige y ofrece cargarlo |
| Mezcla de datos entre empresas | Todo por RFC; credenciales cifradas por equipo (DPAPI) |

## 14. Decisiones que necesito del dueño

1. **Primer corte**: ¿empezamos por **compras/gastos y ventas** (provisión) y luego pagos/cobros, o lo primero que necesitas en producción son los **pagos desde REP**?
2. **Reglas**: ¿el formato de edición principal es **dentro de la app** (con Excel para masivos) o **solo Excel**? (Se propone app + Excel.)
3. **Evaluador de fórmulas**: ¿propio y acotado (más seguro, menos potente) o una biblioteca de expresiones? (Se propone propio con un conjunto de funciones definido.)
4. **Prioridad de datos de prueba**: casos reales que quieras que cubra primero (dólares, retenciones, ISH/TUA, nómina…).
5. **Importar del configurador nativo**: ¿lo incluimos desde 3.1 (semilla de reglas) o después?
