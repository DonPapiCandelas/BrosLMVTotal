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
- `ajustes`: signo del importe, formato de fecha (`mdy`), cada divisa es una cuenta. `pdf.estrategia`: `bbvanet`, `export` o `estadocuenta`.
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

## Pruebas
`dotnet test bancos-pruebas` (29 pruebas): regresión contra los 95 archivos reales de `Entrenamiento\Bancos` (privado, fuera del repo; se omite si no está; `BROSLMV_BANCOS_ACTUALIZAR_GOLDEN=1` acepta una mejora a propósito), layouts, anti-duplicados,
analizador y, contra el laboratorio `BROSLMV_DESARROLLO`, carga, deshacer, layouts versionados, configuración y permisos (se omiten sin SQL Server).

## Pendiente
Registrar el botón con el instalador real y probarlo dentro de Comercial en una terminal; generar el instalador nuevo (decidir la versión); editor de reglas de tipo y de extracción de campos del concepto; zonas de PDF editables
para bancos que no sean BBVA; `.xls` binario antiguo; PDF escaneado (sin OCR); empate contra cobros, pagos y CFDI (fases 2 a 4 del plan).
