# SDK: funciones nativas de Comercial en `ctx.erp` (v2.99.0)

> Qué se agregó, cómo se probó cada función y qué **no** se agregó (con el motivo). Fichas por función:
> [`SDK_REFERENCIA.md`](SDK_REFERENCIA.md). Código: `src/ErpSdkNativo.cs`.

## 1. Por qué envolver algo que ya se alcanza con `ctx.erp.Call`

Todo el motor de Comercial ya era alcanzable (`ctx.erp.Call("Nombre", …)` en C#, y Python cae a ese camino si no hay wrapper).
Lo que aporta un wrapper es lo que `Call` no da:

- **Firma tipada** y coerción correcta de argumentos (el motor espera `Long`/`Integer`/`Variant` de VB6; pasar el tipo equivocado
  falla en silencio).
- **Una ficha en el Manual** con parámetros, ejemplo y advertencias.
- **Comportamiento probado.** `Com.Call` traga las excepciones COM y deja el motivo solo en `ctx.erp.LastError`: una función mal
  llamada parece "no hacer nada". Ejemplo real encontrado en esta versión: `AjustarSaldosInsolutos` llevaba mucho tiempo fallando así.

Convención: los wrappers conservan el **nombre nativo** del motor (coincide con `XENGINE_FUNCIONES.md` y con `ctx.erp.Call` de
Python) y, ante un error COM, devuelven el valor por omisión y dejan el motivo en `LastError`.

## 2. Cómo se probó

Nada se probó contra producción. Se usaron **copias locales** de bases reales (una de ~30 mil documentos y 1,571 cobros hechos
por la interfaz de Comercial) con `BrosLMV.Runner` y scripts de C# que escriben su salida a archivo. Para las funciones que
escriben se usó siempre el mismo diseño:

1. **Control:** llamar la función sobre datos que Comercial dejó consistentes y comprobar que **no los cambia**.
2. **Desarmar y reconstruir:** borrar o poner en cero lo que la función debe regenerar, llamarla y comparar contra el original.
3. **Atributos:** registrar qué distingue a los casos que sí se reconstruyen de los que no (así se encontró `AltID`, ver §4.1).

Verificación final: 40 comprobaciones sobre la API **tipada** ya compilada, 0 fallas, más el caso de humo #34.

## 3. Funciones agregadas

### 3.1 Esquema y entorno
| Función | Qué hace | Verificado |
|---|---|---|
| `TableExists(tabla)` | ¿existe la tabla en la empresa activa? | sí/no correctos |
| `FieldExistsInTable(campo, tabla)` | ¿existe la columna? **Orden nativo: campo, luego tabla** | sí/no correctos |
| `GetModuleIDDocumentType(tipoDoc, recipiente)` | módulo de fábrica: (5,1)→21, (5,2)→152 | sí |
| `GetModuleDLLName(moduleId)` | DLL del módulo: 21→`Document`, 248→`FinancialOperation` | sí |
| `GetSecurityFunctionality(clave, moduleId)` | permiso del usuario activo (p. ej. `Document.Delete`) | devuelve el permiso nativo |
| `GetUserCanElevatePrivileges()` | ¿administrador de Comercial? | sí |

Para clasificar módulos sigue valiendo `engModule.ModuleIDBase`: en empresas con módulos clonados, los IDs de fábrica no alcanzan.

### 3.2 Parámetros por empresa — `GetDefaultValue` / `SaveDefaultValue`
Son una API sobre la tabla nativa **`engParameter`** (`Key`, `Value`, `Description`, `CountryID`) de la empresa activa (cientos de
claves del propio producto). Probado: escribir, leer, sobrescribir, que otro país no vea la clave, y que una clave inexistente
devuelva `""`. **Escribe en configuración nativa:** usa siempre un prefijo propio (`BROS_…`).

### 3.3 Fecha y texto
`GetLastDayMonth` (28–31, bisiestos correctos) · `DateFromString("2026-10-01","12:30:00")` · `ConvertDateTimeToUTC` (formato de fecha-hora
de Comercial para CFDI, con desfase: `2026-10-01T12:30:00 -06:00`, **con un espacio antes del desfase**) · `GetFormatedDateValue` ·
`Pad(valor, largo, relleno, "L"|"R")` (`"L"`: texto a la izquierda, `"7"→"70000"`; `"R"`: a la derecha, `"7"→"00007"`) ·
`TruncateDouble(n, dec)` (trunca hacia cero: `(-3.999,1)→-3.9`) · `GetSerialNumberPrefix/NumValue` (`ABC00123`→`ABC`/`00123`) ·
`GetFormatedXML` (sangrías) · `GetMaxValueField(campo, tabla, where)` (el filtro es texto SQL; **no** sustituye a un folio atómico).

**`GetQRCode(texto)`** devuelve un **PNG en base64** (firma `89 50 4E 47` verificada): sirve para PDFs y HTML con
`<img src="data:image/png;base64,…">`.

### 3.4 Costos
`GetCostLast(producto)` · `RecalcCostComercial(producto)` · `RecalcCostFiscal(producto)`.
- **Control:** recalcular sobre datos consistentes no cambia nada (15 de 15 productos, comercial y fiscal; 0 errores).
- **Reconstrucción:** `RecalcCostComercial` devolvió `orgProduct.CostPriceComercial` a su valor original en **13 de 15**; en los otros 2
  el recálculo difiere del valor guardado (uno ni se movió de 0; en otro el original estaba desactualizado).
- **No reconstruye el libro por documento** (`orgProductCostComercial`, columnas de salida): eso no lo hace ni esta función ni
  `CalcularCostos` (0 de 15 tras desarmar; además `CalcularCostos` tardó ~74 s por producto en el laboratorio).

### 3.5 Cobros y pagos
| Función | Qué reconstruye | Resultado en laboratorio |
|---|---|---|
| `SaveAllTaxesPayment(operación)` | reparto de impuestos de **un** cobro | Opción **conservadora**: rehízo el reparto en los cobros que lo tenían y **no inventó** reparto en los 22 que nunca lo tuvieron |
| `RecalcPagosDocumento(documento)` | reparto de impuestos **y** saldos insolutos de todos los cobros del documento (`Payment.clsMain.RecalcDocumentPayments`, la rutina que Comercial usa al guardar un cobro) | 107 documentos: 0 excepciones; no altera lo ya guardado; reparto reconstruido en 68 de 71 que lo tenían (**20 de 20 con retenciones**) |
| `AjustarSaldosInsolutos(operación[, pagoConDoc])` | `SaldoAnterior`/`SaldoInsoluto` de un cobro, sin tocar impuestos | **12 de 12** cobros sin timbrar restaurados (**corregida**, ver §5) |

Receta para un cobro insertado por SQL: `INSERT` de `docFinancialOperation` + `docDocumentPayment` → `RecalcPagosDocumento(doc)` (o
`SaveAllTaxesPayment(op)`) → `UpdateDocumentPaidInfo(doc)` → póliza nativa → `RefreshGrid`. Ya **no se escribe a mano**
`docFinancialOperationTaxDetail`.

## 4. Hallazgos que hay que conocer

### 4.1 `AltID`: los pagos ya timbrados no se tocan
`docDocumentPayment.AltID > 0` marca renglones ya incluidos en un REP timbrado. Las rutinas de Comercial **no recalculan sus saldos**
(restauraron 17 de 17 renglones con `AltID = 0` y no tocaron 85 de 88 con `AltID > 0`). Es lo correcto fiscalmente: el saldo de un REP
emitido no debe moverse. Si necesitas cambiar un pago timbrado, no es este el camino.

### 4.2 `RecalcPagosDocumento` agrega reparto donde Comercial nunca lo generó
De 107 documentos, **33** no tenían reparto de impuestos y la función se lo creó; **21 eran método de pago PUE** y la mayoría pagos
de 2020–2022. Por eso: úsala con facturas **PPD**; para completar solo lo que ya existía, usa `SaveAllTaxesPayment`.

### 4.3 Granularidad
`RecalcPagosDocumento` trabaja por documento: si se borró a mano solo el reparto de **una** operación de un documento con varias, puede
fallar con *"Division by zero"* (22 de 25 casos así). Rehacer todo el documento (o usar `SaveAllTaxesPayment` por operación) no falla.

### 4.4 Revisa `LastError`
`Com.Call` no lanza: devuelve el valor por omisión y deja el motivo en `ctx.erp.LastError`.
```csharp
ctx.erp.RecalcPagosDocumento(facturaId);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) ctx.Msg(ctx.erp.LastError);
```

## 5. Corrección: `AjustarSaldosInsolutos`
La versión anterior recibía un `documentId` y llamaba al motor con **un** parámetro; el motor espera dos (`FinancialOperationID`,
`PaymentWithDocumentID`), así que fallaba siempre con `DISP_E_PARAMNOTOPTIONAL`, que `Com.Call` deja solo en `LastError`. Tampoco
bastaba cambiar los argumentos: la versión del propio motor (`XEngineLib`) falla con *"Object variable or With block variable not set"* aun
con los dos parámetros correctos; la que funciona es la de `Payment.clsMain`, que es la que usa ahora. El argumento pasó de documento a
**operación financiera**; es seguro porque antes no tenía ningún efecto.

## 6. Probadas y **no** agregadas
| Función | Motivo |
|---|---|
| `GetRateCurrencyDate`, `GetRateCurrencies` | Se probó con historial en `engCurrencyHistory` y con `engRefCurrencyOfficialRate`: **ignora ambos** y devuelve siempre `engRefCurrency.Rate` (el vigente), ni invierte el sentido. No sirve para tipo de cambio por fecha. |
| `ValidateFinancialEntityNumber` | Error con entradas inválidas y acepta una CLABE con dígito verificador incorrecto. No es confiable. |
| `GetCostPriceComercial` | Falla con *"Object variable … not set"* en el laboratorio; usa `GetCostLast` o consulta `orgProduct.CostPriceComercial`. |
| `GetProportionalAmountPerc` | *"Division by zero"* si el producto no tiene componentes. |
| `GetExcelColumnLetter` | Falla después de la columna ZZ (703 → `"[A"`); trivial de escribir bien en C#. |
| `SetParameter` | Actualiza una clave existente pero **no agrega** una nueva (devuelve `null`). |
| `Payment.RecalcDocument(op)` | *"Division by zero"* si el reparto no existe: re-proporciona, no reconstruye. |
| `Payment.UpdateSaldosInsolutos` | Restauró solo 8 de 12; `AjustarSaldosInsolutos` hace lo mismo y mejor. |
| `AjustarSaldosInsolutosMasivo` | Sin efecto observable en las pruebas. |

## 7. Pendiente
- **Crear** un cobro completo por la vía nativa (`Document.clsPaymentTransactions` + `ApplyPayment` + `Save`): se instancian las clases, pero
  `LoadDocuments` falla con un recordset cerrado en modo sin ventana y no se encontró cómo obtener el objeto de la operación financiera. Sin resolver.
- Moneda extranjera en cobros: la base de pruebas no tenía cobros con tipo de cambio distinto de 1.
- Candidatos con la misma metodología: funciones de módulo de otras DLL de Comercial (documentos, operaciones financieras, CFDI), aún no envueltas.
