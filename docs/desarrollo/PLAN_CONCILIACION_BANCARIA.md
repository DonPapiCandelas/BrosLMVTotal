# Plan: conciliación bancaria en BrosLMV.Descargas

Origen: pedido del dueño (2026-10-27): «un verdadero módulo de conciliaciones bancarias»: que suban el estado de cuenta o los movimientos
(txt, pdf, excel, csv, el formato que den sus bancos), se carguen al sistema y de ahí partir. Con varias cuentas del mismo banco, el banco se
sugiere pero **se puede elegir**, y a veces el archivo no dice de qué cuenta es.

## Lo que ya se sabía (no se partió de cero)
- `Entrenamiento\logical_dll\` (privado): formatos de 13 bancos / 21 layouts (`formatos_bancos_soportados.txt`), cómo importaba y conciliaba Logical
  SmartConnect (README §3 y §7, INVENTARIO §1–2), incidente real de Banorte (columna `MONEDA` nueva = carga rota en silencio).
- Tablas nativas: Comercial `docEdoCtaBanco` / `docFinancialOperation`; Contabilidad `EdoCtaBancos`, `MovtosEdoCtaBancos`, `AsocMovtsConciliacion`,
  `CuentasCheques`, `RelacionMovBancarioCFDI`.
- Lo que Descargas ya tiene y nadie más: CFDI del SAT (emitidos, recibidos, REP), Mapa de CFDI, Contabilizador (terceros y cuentas, SDK de Contabilidad).

## Fases
| Fase | Contenido | Estado |
|---|---|---|
| **1** | Ingesta: cuentas bancarias, lectura por encabezado, detección de banco, validación de saldo, importación sin duplicados | **Hecha en 3.5.0** (CSV, TXT, XLSX) |
| 1b | PDF con texto (PdfPig), catálogo de layouts de HSBC/Hey/Monex/BBVA xlsx con archivos reales | pendiente |
| 2 | Empate de lectura: sugerencias contra CFDI/REP, Comercial (`docFinancialOperation`) y Contabilidad; tablero con confianza | pendiente |
| 3 | Acciones: ligar, crear cobro/pago en Comercial, póliza por SDK con las reglas del Contabilizador; reglas que aprenden | pendiente |
| 4 | Fiscal y cierre: PPD sin REP, IVA efectivamente pagado, pagos sin CFDI, Excel de conciliación, bloqueo de mes | pendiente |

## Decisiones de la fase 1
- **Se lee por encabezado, no por posición.** Un banco que agrega una columna no rompe la carga. El mapa de columnas es un dato: se corrige en
  pantalla y se guarda como perfil (clave = encabezado normalizado), por empresa.
- **El banco se sugiere, no se impone.** Pistas por contenido (encabezado, preámbulo) y por nombre del archivo; el usuario puede cambiarlo.
- **Qué cuenta es** (`BancoDatos.SugerirCuenta`), de más a menos seguro: (1) número de cuenta / CLABE / últimos 4 dígitos que dice el archivo o su
  nombre; (2) el saldo con el que abre el archivo = último saldo cargado de esa cuenta (se preselecciona pero se pide confirmar); (3) única cuenta
  del banco. Con varias cuentas del mismo banco y ninguna pista, **no elige sola**: el botón Importar queda bloqueado hasta que el usuario elija.
  Un archivo con varias cuentas (columna Cuenta) se reparte por grupos, cada uno con su cuenta.
- **Validación antes de guardar:** el saldo corrido debe cuadrar renglón por renglón (prueba orden normal/invertido y saldo de deuda de tarjeta);
  un importe que no es número bloquea; filas sin fecha se reportan; aviso si el saldo inicial no es el último saldo cargado (posible periodo faltante).
- **Sin duplicados:** huella por renglón (fecha, cargo, abono, saldo, descripción, referencia, cuenta + contador de repetidos). Cargar el mismo periodo
  dos veces no duplica; dos renglones idénticos el mismo día siguen siendo dos movimientos. Cada carga se puede **deshacer**.
- **Campos de la contraparte** extraídos del texto (clave de rastreo, RFC, CLABE, nombre, banco) y **tipo** (SPEI, comisión, IVA, traspaso…): son
  la llave para empatar contra CFDI en la fase 2.
- **Todo local**: el estado de cuenta nunca sale del equipo.

## Pendientes conocidos de la fase 1
- Los layouts de Scotiabank (sin encabezado) y de HSBC / Hey Banco / Monex / BBVA-xlsx no se probaron con archivo real: el PDF oficial no trae sus
  columnas. Se leen por inferencia o por encabezado genérico y se pueden corregir y guardar. Con muestras reales se agregan al catálogo de pruebas.
- Probar con la carpeta `C:\ExtractoBancario\Procesado\` del cliente de Logical (oráculo: lo que Logical cargó en su día).
- PDF y `.xls` antiguo no se leen todavía.
