# BrosLMV Bancos

Programa aparte (WPF, .NET 8, autocontenido) que **carga estados de cuenta y movimientos de bancos al estado de cuenta de Comercial** (`docEdoCtaBanco`, lo
que se ve en *Estados de cuenta*) y trae un **editor de layouts** para cuando un banco cambia su formato. Es el primer paso de la conciliación bancaria:
por ahora **solo carga información**; el empate contra cobros, pagos y CFDI viene después (`docs\desarrollo\PLAN_CONCILIACION_BANCARIA.md`).

## Cómo se abre
Solo desde Comercial: botón **Bancos** en *Estados de cuenta* (módulo 1115, junto a los botones nativos). El botón es el script
`instalador\scripts\ABRIR_BANCOS.ctx`, que abre `C:\BrosLMV\Bancos\BrosLMV.BancosUI.exe` con la empresa y el usuario abiertos:

```
BrosLMV.BancosUI.exe --servidor <srv> --bd <base> --empresa <id> --usuario <id> --nombre-empresa "<texto>" --nombre-usuario "<texto>"
```
Sin esos argumentos no abre. Una sola ventana por empresa (si ya está abierta, se trae al frente). La conexión usa la credencial cifrada que BrosLMV ya guarda en
cada terminal (`C:\BrosLMV\bin\broslmv_cred.dat`); sin ella prueba autenticación integrada.

## Instalación (por equipo)
- El instalador de BrosLMV trae un **check «BrosLMV Bancos»** (marcado por defecto). Copia el programa a `C:\BrosLMV\Bancos` en servidor y en cada terminal.
- El botón del ribbon se crea con el aprovisionamiento de la empresa (`provision_empresa.sql`, sección 4c-bis). Una terminal sin el programa instalado muestra un aviso al pulsarlo.
- `build\generar_exes.ps1` corre las pruebas de Bancos, publica el programa autocontenido (≈185 MB descomprimido) y lo mete al `payload.zip`.

## Qué hace
- **Bandeja.** Carpeta vigilada por empresa (se guarda en la base), arrastrar o elegir archivos. Cada archivo se lee solo y queda en un estado: *Listo*, *Revisar*
  (falta elegir o confirmar cuenta, saldo que no cuadra, tope de 500), *Ya cargado*, *Sin layout* o *Error*. Detalle con cuenta, avisos y vista previa de movimientos (repetidos en gris).
- **Carga todo o nada**, un archivo con varias cuentas (Monex por divisa) en una sola transacción. Los archivos de la carpeta pasan a `Procesados\aaaa-mm`. **Deshacer última** da de baja lo cargado (como lo hace Comercial: `DeletedOn`) mientras no tenga movimiento ligado.
- **Formatos:** CSV, TXT, Excel (.xlsx), `.xls` que en realidad es texto (Monex) y PDF de BBVA con texto (impresión de BBVANet, impresión del export y estado de cuenta oficial; se leen con PdfPig, sin OCR).
- **Cuenta:** la elige el layout/archivo por número de cuenta, CLABE, últimos dígitos o divisa; si el archivo dice una cuenta que no existe, avisa y no adivina; con varias cuentas del mismo banco y sin pistas no elige sola. Lo sugerido por saldo o por ser la única del banco **se confirma**.

## Layouts
Un layout es un dato (JSON): cómo **reconocer** el formato y cómo **leerlo**. Los de fábrica van dentro del programa (`bancos\Layouts\Fabrica\*.json`, 19); los de la empresa
viven en `zzBrosBancoLayout` (versionados, con historial y volver a una versión anterior) y **ganan** al de fábrica con el mismo `id`.
- `reconocer`: lista de caminos (O) de condiciones (Y) sobre el `encabezado`, el `texto` de las primeras filas, el nombre del `archivo` o el texto del `pdf`. Cada condición es `contiene`/`empieza` con varios valores (O). Da un **porcentaje** de coincidencia; el layout se usa solo cuando llega a su `umbral` (100 por defecto) y, entre varios, gana la mayor `confianza`.
- `columnas`: campo → nombres de encabezado, o `#3` (columna 3, para archivos sin encabezado). `filaEncabezado` / `primeraFila` si hace falta. Campos: fecha, hora, descripcion, detalle, referencia, cargo, abono, importe, signo, saldo, cuenta, moneda.
- `ajustes`: signo del importe, formato de fecha (`mdy`), cada divisa es una cuenta.
- `tipos`: reglas propias del banco (patrón sobre el texto sin acentos → tipo, solo para cargo, abono o ambos); ganan a las generales. `extraer`: saca referencia, clave de rastreo, RFC, CLABE, nombre o banco del concepto con una expresión regular (grupo 1).
- `pdf.estrategia`: `zonas` (cualquier banco: la fila empieza con una fecha y los importes van a la columna cuyo **título** queda más cerca; se ajusta con los títulos de cargo/abono/saldo, el patrón de la fecha, y el alcance de la descripción), o las lecturas a mano de BBVA `export` y `estadocuenta`. `bbvanet` es `zonas` con los valores de fábrica.
- Lo que el layout no dice lo deduce el motor por encabezado (alias conocidos), por eso un banco que agrega una columna no rompe la carga.

**Editor** (pestaña Layouts → Nuevo layout, o «Abrir en Layouts» desde un archivo sin layout): se abre un archivo de muestra, se marca la fila del encabezado y qué es cada columna, se elige
cómo reconocerlo y se ve el resultado en vivo (movimientos, saldo, errores). Antes de guardar avisa si otro layout dejaría de aplicarse o si algún archivo de la bandeja cambiaría de layout.
Duplicar, exportar/importar `.json`, volver al de fábrica.

**Permisos.** Todos pueden usar la bandeja. Quién edita layouts: *todos* (predeterminado), *solo administradores* (grupo Admin de Comercial) o *usuarios elegidos*; solo un administrador cambia esa regla. Se guarda en `zzBrosBancoConfig`.

## Cero duplicados
Mismo importe y misma fecha pueden ser dos movimientos. Para cada renglón del archivo se compara contra lo ya cargado (por BrosLMV, por Logical o a mano) con, en este orden: **hora de la
transacción** (cuando el archivo la trae), **clave de rastreo**, **saldo después del movimiento**. Dos renglones que se contradicen en cualquiera de esas cosas nunca son el mismo; solo si no hay
con qué distinguirlos se empareja por conteo. Se hace en dos vueltas para que el conteo no le robe la pareja a uno que sí tiene saldo.
Lo propio de BrosLMV (hora, rastreo, saldo, layout, hash del archivo) se guarda en `zzBrosEdoCtaCarga`, ligado a cada renglón; `docEdoCtaBanco` no se ensancha.

## Cambio desde otro cargador (duplicados que ya existen)
Si el estado de cuenta ya lo carga otra herramienta, **no dejes las dos cargando a la vez** y revisa antes de cambiar. Lo aprendido de un incidente real (un cargador anterior que, tras una actualización, volvió a insertar los mismos movimientos del último día en cada ejecución: decenas de copias del mismo renglón):
- **Qué cubre Bancos:** al cargar compara cada renglón del archivo contra lo que ya hay (cargado por cualquiera) con hora, rastreo y saldo, y empareja **uno a uno**: un archivo no agrega un renglón que ya existe aunque haya varias copias. **No limpia** las copias que otro cargador ya dejó.
- **Cómo se reconoce un duplicado ya existente:** misma cuenta, fecha, cargo, abono, **saldo después del movimiento** y descripción. El saldo corrido distingue dos movimientos iguales del mismo día; el mismo saldo es el mismo movimiento cargado dos veces. El `OrderNumber` no sirve: cada re-carga lo asigna nuevo.
- **Antes del cambio:** (1) correr la consulta de revisión de abajo; (2) dar de baja los sobrantes **sin pago ligado** como lo hace Comercial (`DeletedOn`/`DeletedBy`, con respaldo y reversa; nunca `DELETE`), conservando el más antiguo; (3) los que ya tienen pago ligado se revisan a mano; (4) apagar la carga del otro cargador y solo entonces usar Bancos.
- **Después:** repetir la consulta unos días; si aparecen sobrantes, algo más sigue cargando.
- **Revisión de duplicados existentes (desde la 3.2.6):** botón **«Duplicados…»** en la bandeja. Busca los renglones repetidos (criterio de arriba) en los últimos 30 a 365 días, marca los sobrantes y los da de baja **solo si no tienen movimiento ligado**, conservando una copia de cada movimiento (la ligada, o la más antigua). La baja es como la de Comercial (`DeletedOn`, nunca `DELETE`), guarda copia completa en `zzBrosEdoCtaBaja` y se restaura con «Restaurar última baja». Al abrir y después de cargar, la barra inferior avisa si ya hay repetidos. Antes de dar de baja revisa de nuevo el estado de cuenta (si alguien ya borró o ligó la copia que se conservaba, no se toca ese movimiento) y «Restaurar» solo devuelve lo que sigue como lo dejó la baja (no revive lo que alguien borró después a mano). Una cuenta cuyo saldo es el del cierre del día (no por movimiento) se omite y se avisa. **Continuidad de saldo:** al analizar un archivo se compara su saldo inicial con lo último que Comercial tiene de la cuenta; si no coincide el archivo queda en *Revisar* («El saldo no continúa») hasta «Cargar así». **Otro cargador:** si hay renglones recientes con la marca `TransactionSyncFyID` que no cargó Bancos, la barra inferior lo avisa.

```sql
-- Renglones repetidos desde una fecha (rn > 1 = sobrante)
WITH x AS (SELECT EdoCtaID, FinancialEntityID, DateAffectation, FinancialOperationID,
   ROW_NUMBER() OVER (PARTITION BY FinancialEntityID, DateAffectation, Debit, Credit, Balance, CONVERT(nvarchar(300), Description)
                      ORDER BY CASE WHEN ISNULL(FinancialOperationID,0)<>0 THEN 0 ELSE 1 END, EdoCtaID) rn
   FROM docEdoCtaBanco WHERE DeletedOn IS NULL AND DateAffectation >= '2026-01-01')
SELECT FinancialEntityID, DateAffectation, COUNT(*) AS sobrantes,
       SUM(CASE WHEN ISNULL(FinancialOperationID,0)<>0 THEN 1 ELSE 0 END) AS ligados_a_pago
FROM x WHERE rn > 1 GROUP BY FinancialEntityID, DateAffectation ORDER BY 2 DESC;
```

## Pruebas
`dotnet test bancos-pruebas` (53 pruebas): regresión contra los 95 archivos reales de `Entrenamiento\Bancos` (privado, fuera del repo; se omite si no está; `BROSLMV_BANCOS_ACTUALIZAR_GOLDEN=1` acepta una mejora a propósito), layouts, anti-duplicados,
analizador y, contra el laboratorio `BROSLMV_DESARROLLO`, carga, deshacer, layouts versionados, configuración y permisos (se omiten sin SQL Server).

## Pendiente
Probar el botón dentro de Comercial en una terminal real; `.xls` binario antiguo; PDF escaneado (sin OCR); empate contra cobros, pagos y CFDI (fases 2 a 4 del plan).
