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
