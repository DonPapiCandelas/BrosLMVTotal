# Cargar estado de cuenta del banco

Script `instalador\scripts\CARGAR_ESTADO_CUENTA_BANCO.ctx` (C#, ventana Windows Forms que no bloquea Comercial). Botón sugerido: **Tesorería › Cuentas Bancarias**
(módulo 90): si hay una cuenta seleccionada en la rejilla, ya viene elegida como destino.

## Qué hace
- Lee CSV, TXT y Excel (.xlsx) del banco **por encabezado, no por posición**: si el banco agrega una columna, la carga no se descompone.
- Sugiere el banco, pero se puede cambiar. Sugiere la cuenta por número de cuenta/CLABE/terminación que traiga el archivo, o porque el saldo con el que abre
  el archivo es el último saldo cargado de esa cuenta. Con varias cuentas del mismo banco y sin pistas **no elige sola**: el botón queda bloqueado.
- Un archivo con varias cuentas se reparte por grupos, cada uno con su cuenta.
- Valida el saldo corrido renglón por renglón; un importe que no es número bloquea la carga.
- Escribe en el estado de cuenta **nativo** (`docEdoCtaBanco`, el que lee `vwLBSEdoCtaList`). No hay tablas de movimientos propias.
- Sin duplicados: no vuelve a cargar lo que ya está (misma cuenta, fecha, cargo y abono; cuenta los repetidos), venga de BrosLMV, de Logical o capturado a mano.
- **Deshacer mi última carga** da de baja los renglones de esa carga que sigan sin movimiento bancario ligado.
- Recuerda el formato del archivo (perfil por encabezado) en `zzBrosBancoPerfil`; la bitácora de cargas está en `zzBrosEdoCtaCarga` (ambas se crean solas).
- Todo es local: el archivo no sale del equipo.

## Pendiente
Registrar el botón en el ribbon (Crear botón), espejo en Python, PDF y `.xls` antiguo, y las fases siguientes (empate contra CFDI/cobros/pagos): ver
`docs\desarrollo\PLAN_CONCILIACION_BANCARIA.md`. Probado en el laboratorio BROSLMV_DESARROLLO (importar, ver en `vwLBSEdoCtaList`, duplicados y deshacer).

## Formatos probados con archivo real
- **BBVA, movimientos en TXT** (`Entrenamiento\Bancos\BBVA Bancomer`): tabuladores, encabezado `Día / Concepto / Referencia / cargo / Abono / Saldo`, fecha `dd-MM-yyyy`, del más nuevo al más viejo, saldo con comas. No trae número de cuenta ni saldo inicial (se deduce del primer renglón): la cuenta sale del nombre de archivo, del saldo o de que sea la única de BBVA. Del concepto se extrae el banco de la contraparte (pegado al tipo o por código 014, 699…), el folio como referencia y el tipo (Comisión, IVA, Pago SAT, Nómina, SPEI, Pago a tercero).
- **BBVA cuenta de cheques en Excel** (`BBVA Movimientos Octubre.xlsx`): preámbulo `Cuenta: 2654755545` (la cuenta sale de ahí), encabezado en la fila 4, y el cargo viene **con signo menos dentro de la columna CARGO** (sigue siendo cargo). Si el archivo dice la cuenta y no hay una dada de alta con ese número, **no se adivina**: avisa que la des de alta o elijas.
- **BBVA tarjeta de crédito en Excel** (`BBVA Plat Movimientos Octubre.xlsx`): `Número de Tarjeta: ****12677` (se liga por los últimos dígitos); renglones que abren sección (`Digital *7428`, `Titular *2677`, `Adicional *2693`) se guardan en el detalle de cada movimiento; el pago a la tarjeta viene como **abono con signo menos**; los recientes dicen `En tránsito` en lugar de saldo (se cargan sin saldo y se avisa).
- **BBVA Oro** (tarjeta, sin saldo): columnas `Pagos y depósitos / Compras`, fecha `22 Oct 2026`, con renglones informativos de moneda extranjera sin importe (`10/19/26 73.90 PEN`), que se omiten con aviso. El encabezado no dice el banco (la documentación lo atribuía a Banamex): **se elige banco y tarjeta**.
- **BBVA Perfiles** (cuenta, sin número): `Depósitos / Retiros / Saldo` con `$`. Sin pista de cuenta, se elige.
- «Única cuenta del banco» solo se usa si el banco se reconoció por el contenido o lo eligió la persona, no por el nombre del archivo.
- Las tarjetas (`orgFinancialEntity.FinancialEntityTypeID = 2`) también son destino válido.
