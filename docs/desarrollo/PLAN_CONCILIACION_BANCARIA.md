# Plan: conciliación bancaria en BrosLMV Bancos

Origen: pedido del dueño (2026-10-27): «un verdadero módulo de conciliaciones bancarias»: que suban el estado de cuenta o los movimientos
(txt, pdf, excel, csv, el formato que den sus bancos), se carguen a Comercial y de ahí partir. Es un programa propio de BrosLMV, solo para
Comercial, que se abre desde un botón en *Estados de cuenta*. Cómo funciona hoy: `docs\BANCOS.md`.

## Lo que ya se sabía (no se partió de cero)
- `Entrenamiento\logical_dll\` (privado): formatos de 13 bancos / 21 layouts (`formatos_bancos_soportados.txt`), cómo importaba y conciliaba Logical
  SmartConnect (README §3 y §7, INVENTARIO §1–2), incidente real de Banorte (columna `MONEDA` nueva = carga rota en silencio).
- Tablas nativas: Comercial `docEdoCtaBanco` / `docFinancialOperation`; Contabilidad `EdoCtaBancos`, `MovtosEdoCtaBancos`, `AsocMovtsConciliacion`,
  `CuentasCheques`, `RelacionMovBancarioCFDI`.

## Fases
| Fase | Contenido | Estado |
|---|---|---|
| **1** | Carga: lectura de CSV, TXT, Excel, `.xls` de texto y PDF de BBVA; layouts como datos con editor; detección de banco y cuenta; validación de saldo; carga a `docEdoCtaBanco` sin duplicados (hora, rastreo, saldo) | **Hecha** (probada en el laboratorio con 95 archivos reales) |
| 1b | Layouts reales de HSBC, Hey Banco, Scotiabank y PDF de otros bancos; zonas de PDF editables; `.xls` binario | en curso |
| 2 | Empate de lectura: sugerencias contra CFDI/REP, Comercial (`docFinancialOperation`) y Contabilidad; tablero con confianza | pendiente |
| 3 | Acciones: ligar, crear cobro/pago en Comercial, póliza por SDK; reglas que aprenden | pendiente |
| 4 | Fiscal y cierre: PPD sin REP, IVA efectivamente pagado, pagos sin CFDI, Excel de conciliación, bloqueo de mes | pendiente |

## Decisiones de la fase 1
- **Se lee por encabezado, no por posición.** Un banco que agrega una columna no rompe la carga. Lo que el motor no deduce lo dice el layout.
- **Los layouts son datos** (JSON, versionados, de fábrica y de la empresa): un banco que cambia su formato se arregla en el editor, sin programar.
- **El banco y la cuenta se sugieren, no se imponen.** Con varias cuentas del mismo banco y ninguna pista, no elige sola; lo sugerido por saldo o por ser la única se confirma.
- **Validación antes de cargar:** el saldo corrido debe cuadrar renglón por renglón; un importe que no es número bloquea; aviso del tope de 500 de BBVA.
- **Cero duplicados:** hora de la transacción, clave de rastreo y saldo antes de contar. Cada carga se puede deshacer.
- **Campos de la contraparte** (clave de rastreo, RFC, CLABE, nombre, banco) y **tipo** (SPEI, comisión, IVA, traspaso…): son la llave para empatar en la fase 2.
- **Todo local**: el estado de cuenta nunca sale del equipo.
- **Hecho en la 3.2.6 (2026-11-11):** «Revisión de duplicados existentes» (botón «Duplicados…», baja guiada que respeta lo ligado, restaurable) y aviso al abrir y tras cargar. Ver `docs\BANCOS.md`, «Cambio desde otro cargador».
