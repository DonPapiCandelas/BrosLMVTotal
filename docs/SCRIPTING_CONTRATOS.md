# BrosLMV — Contrato de scripting (`ctx.*` y `ctx.erp.*`)

> Referencia completa del objeto `ctx` que BrosLMV inyecta a todos los scripts.
> Es el **contrato unificado**: lo que escribas aquí funciona en C# hoy, y será
> el mismo contrato que usará Python (via Named Pipes, v3.0) mañana.
>
> Última actualización: 2026-06-26

---

## 1. El objeto `ctx` (raíz)

El motor Roslyn inyecta `ctx` como variable global. En C# lo usas directamente:

```csharp
// Un script C# mínimo:
var ids = ctx.GetSelectedIds();
ctx.Msg("Seleccionados: " + ids.Count);
```

En Python (v3.0, fuera de proceso) será idéntico:
```python
ids = ctx.get_selected_ids()
ctx.msg(f"Seleccionados: {len(ids)}")
```

---

## 2. ctx — SQL y conexión

Todos los métodos SQL **usan la conexión viva de CONTPAQi** (la misma que usa el grid,
sin credenciales adicionales). Si el grid no está activo, cae al respaldo `broslmv_conn.txt`.

| Método | Descripción |
|--------|-------------|
| `ctx.Scalar(sql)` | Devuelve el primer valor de la primera fila. `null` si vacío. |
| `ctx.Query(sql)` | Devuelve `List<Dictionary<string,object>>`. Clave insensible a mayúsculas. |
| `ctx.NonQuery(sql)` | Ejecuta INSERT/UPDATE/DELETE. Bloqueado en modo `SoloLectura`. Devuelve filas afectadas. |
| `ctx.OpenConn()` | Devuelve un `SqlConnection` abierto (SqlClient). Para transacciones o parámetros tipados. |
| `ctx.JoinIds(ids)` | `string.Join(",", ids)` — útil para `WHERE DocumentID IN ({ctx.JoinIds(ids)})`. |

**Ejemplo — transacción con parámetros tipados:**
```csharp
using (var conn = ctx.OpenConn())
using (var tx = conn.BeginTransaction())
{
    var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "UPDATE docDocument SET UserID=@u WHERE DocumentID=@d";
    cmd.Parameters.AddWithValue("@u", ctx.UserID);
    cmd.Parameters.AddWithValue("@d", ids[0]);
    cmd.ExecuteNonQuery();
    tx.Commit();
}
```

---

## 3. ctx — Contexto y selección

| Propiedad / Método | Descripción |
|-------------------|-------------|
| `ctx.UserID` | ID del usuario activo en CONTPAQi. |
| `ctx.XEngineLib` | Objeto XEngine crudo (COM). Usar `ctx.erp.*` en su lugar cuando sea posible. |
| `ctx.ModuloActivo()` | `ActiveModuleID` del módulo abierto. |
| `ctx.Empresa()` | Nombre de la BD activa (`DB_NAME()`). Identifica la empresa. |
| `ctx.GetSelectedIds()` | `List<long>` con los IDs de las filas seleccionadas en el grid. |
| `ctx.ResolverTokens(template)` | Sustituye tokens en una cadena (ver §4). |
| `ctx.SoloLectura` | `bool`. Si `true`, `ctx.NonQuery` lanza excepción. Útil para scripts de revisión. |
| `ctx.FilasAfectadas` | Acumulado de filas afectadas por `ctx.NonQuery` (auditado por el motor). |

---

## 4. ctx.ResolverTokens — Motor de tokens

```csharp
string sql = ctx.ResolverTokens(
    "SELECT * FROM docDocument WHERE DocumentID = {pID} AND CreatedBy = {pUserID}"
);
```

| Token | Se sustituye por |
|-------|-----------------|
| `{pID}` | Primer ID seleccionado en el grid (o `0`). |
| `{pIDs}` | Todos los IDs seleccionados, separados por coma. |
| `{pUserID}` | `ctx.UserID` — usuario activo. |
| `{pModulo}` | `ActiveModuleID` — módulo activo. |
| `{pEmpresa}` | `ctx.Empresa()` — nombre de la BD. |
| `{DATOS:Campo}` | Valor del campo `Campo` en la **primera fila seleccionada del grid**. |

**Ejemplo con {DATOS:...}:**
```csharp
// Si el usuario tiene seleccionada una remisión:
string cliente = ctx.ResolverTokens("{DATOS:BusinessEntityName}");
string total   = ctx.ResolverTokens("{DATOS:Total}");
ctx.Msg($"Cliente: {cliente} — Total: {total}");
```

---

## 5. ctx — UI y utilidades

| Método | Descripción |
|--------|-------------|
| `ctx.Msg(texto)` | `MessageBox` informativo. |
| `ctx.Confirm(texto)` | `MessageBox` Sí/No. Devuelve `bool`. |
| `ctx.Log(texto)` | Escribe en `C:\BrosLMV\logs\Script_YYYYMMDD.txt`. |

---

## 6. ctx — Almacén de scripts (zzBrosScript)

| Método | Descripción |
|--------|-------------|
| `ctx.BrosScriptsDisponible()` | `true` si la empresa tiene la tabla `zzBrosScript`. |
| `ctx.BrosAsegurarTablas()` | Crea `zzBrosScript` y `zzBrosScriptHist` si no existen. |
| `ctx.BrosListar()` | Lista los scripts activos de la empresa. |
| `ctx.BrosCargar(appKey)` | Devuelve el código de un script, o `null`. |
| `ctx.BrosGuardar(appKey, nombre, codigo, modulo)` | Upsert + versión en historial. |
| `ctx.BrosBorrar(appKey)` | Elimina un script. |

---

## 7. ctx.erp — Wrapper tipado de XEngine

`ctx.erp` es el puente entre el script y el **motor de CONTPAQi**. Evita SQL directo
para operaciones que XEngine ya sabe hacer (folios, inventario, costos, timbrado, correo).

> **Regla de oro:** si XEngine tiene una función para algo, úsala via `ctx.erp`.
> Si no, usa `ctx.NonQuery` o `ctx.Query`. Nunca mezcles INSERT crudo + XEngine
> en la misma operación (rompe la consistencia interna de CONTPAQi).

### 7.1 Contexto

```csharp
ctx.erp.UserId                // int  — usuario activo
ctx.erp.UserName              // string
ctx.erp.OwnedBusinessEntityId // int  — empresa propietaria (emisora)
ctx.erp.ActiveModuleId        // int  — módulo activo
ctx.erp.CurrencyId            // int  — moneda del módulo activo
ctx.erp.ComercialRFC          // string — RFC de la empresa configurada
ctx.erp.SoftwareVersion       // string — versión de ComercialSP
```

### 7.2 Operaciones de documento

```csharp
// Afectar inventario (kardex). Usar AffectStockNEW en módulos nuevos.
ctx.erp.AffectStock(documentId);
ctx.erp.AffectStockNEW(documentId);

// Recalcular totales del documento (vía Doc.clsMain).
ctx.erp.RecalcDocument(documentId);

// Recalc completo: totales + costos + saldo pagado.
// Usar SIEMPRE después de crear o modificar partidas.
ctx.erp.RecalcCompleto(documentId);

// Actualizar costos (promedio, PEPS, etc.).
ctx.erp.CalcularCostos(documentId);

// Actualizar estatus de entrega (iconos del grid de remisiones/pedidos).
ctx.erp.UpdateStatusDelivery(documentId);

// Recalcular saldo pagado y balance.
ctx.erp.UpdateDocumentPaidInfo(documentId);

// Actualizar parcialidad (complementos de pago SAT).
ctx.erp.ActualizarParcialidad(documentId);

// Cancelar / Reactivar.
ctx.erp.CancelDocument(documentId);
ctx.erp.ReactivateDocument(documentId);

// Guardar / Eliminar (por ID).
ctx.erp.Save(documentId);
ctx.erp.Delete(documentId);

// Refrescar VISUALMENTE un documento abierto en pantalla (no recalcula).
ctx.erp.RefreshDocumento(documentId);
```

### 7.3 UI de CONTPAQi

```csharp
ctx.erp.RefreshGrid();             // refresca el grid del módulo activo
ctx.erp.RefreshRibbon();           // refresca el ribbon (botones)
ctx.erp.GotoModuleID(moduleId);    // navega al módulo indicado
ctx.erp.OpenModule(moduleId);      // abre el módulo indicado
ctx.erp.OpenBrowser(url);          // abre URL en el browser interno de Comercial
ctx.erp.ShowMessage("texto");      // mensaje en la barra de estatus de Comercial
```

### 7.4 Folio (LBS.clsMain)

```csharp
// Serie (prefijo) configurada para el módulo+almacén.
string serie = ctx.erp.GetFolioPrefix(moduleId, depotId);

// Siguiente folio disponible.
string folio = ctx.erp.GetNextFolio(moduleId, serie, depotId);
```

**Ejemplo completo — crear documento con folio correcto:**
```csharp
int moduleId = 157;  // Remisiones
int depotId  = 1;
var ids = ctx.GetSelectedIds();
var pedidoId = ids[0];

// Folio
string serie = ctx.erp.GetFolioPrefix(moduleId, depotId);
string folio = ctx.erp.GetNextFolio(moduleId, serie, depotId);

// INSERT directo (la estructura JSON documenta qué campos van)
ctx.NonQuery($@"
    INSERT INTO docDocument (ModuleID, DocumentTypeID, Folio, FolioPrefix, DepotID,
        BusinessEntityID, SourceDocumentID, CreatedBy, CreatedOn, DateDocument, UserID)
    SELECT {moduleId}, 40, '{folio}', '{serie}', {depotId},
        BusinessEntityID, DocumentID, {ctx.UserID}, GETDATE(), GETDATE(), 0
    FROM docDocument WHERE DocumentID = {pedidoId}");

int nuevoId = (int)(long)ctx.Scalar("SELECT SCOPE_IDENTITY()");

// Post-proceso XEngine (siempre)
ctx.erp.RecalcCompleto(nuevoId);
ctx.erp.AffectStockNEW(nuevoId);
ctx.erp.RefreshGrid();
```

### 7.5 Existencias y precios

```csharp
double stock    = ctx.erp.GetProductStock(productId, depotId);
double precioVenta = ctx.erp.GetSalePrice(productId);
double precioCliente = ctx.erp.GetSalePrice(productId, businessEntityId); // lista específica
double precioCompra  = ctx.erp.GetBuyPrice(productId);
double costo    = ctx.erp.GetCostPrice(productId);
double precioIVA = ctx.erp.GetPriceWithTaxes(precio, taxTypeId);
double tipoCambio = ctx.erp.GetCurrencyRate(currencyId);
double tcBanxico  = ctx.erp.GetCurrencyRateBanxico(currencyId);
double coef = ctx.erp.GetCoefConversion(productId, "PZA", "CAJA");
bool esKit = ctx.erp.ProductIsKit(productId);
```

### 7.6 Crédito

```csharp
bool dentroLimite = ctx.erp.VerifyCreditLimit(businessEntityId, importe);
bool tieneVencidos = ctx.erp.VerifyCreditLimitOverdue(businessEntityId);

if (tieneVencidos)
    ctx.Msg("El cliente tiene documentos vencidos.");
```

### 7.7 Parámetros de módulo

```csharp
// Leer un parámetro de engModuleParameter.
string val = ctx.erp.GetModuleParameter(moduleId, "MiParametro");

// Guardar.
ctx.erp.SaveModuleParameter(moduleId, "MiParametro", "valor");

// Parámetro global.
string global = ctx.erp.GetParameter("ClaveGlobal");
```

### 7.8 DLookup — consultas puntuales sin SQL

```csharp
// equivalente a SELECT campo FROM tabla WHERE filtro (devuelve el primero)
object v   = ctx.erp.DLookup("BusinessEntityName", "orgBusinessEntity", "BusinessEntityID=5");
string nom = ctx.erp.DLookupStr("BusinessEntityName", "orgBusinessEntity", "BusinessEntityID=5");
int    num = ctx.erp.DLookupInt("Total", "docDocument", "DocumentID=100");
```

### 7.9 Utilidades de negocio

```csharp
string letra  = ctx.erp.GetTotalLetter(1500.50);  // "MIL QUINIENTOS PESOS 50/100 M.N."
string barcode = ctx.erp.GetBarCode("ABC123");
bool rfcOk   = ctx.erp.ValidRFC("XAXX010101000");
string fmt   = ctx.erp.FormatCurrency(1234.5);     // "$1,234.50"

// Encriptar/desencriptar con la llave de CONTPAQi.
string enc  = ctx.erp.EncryptString("mi secreto");
string dec  = ctx.erp.DecryptString(enc);
```

### 7.10 Auditoría / Log

```csharp
ctx.erp.WriteToLog("Mensaje de log en el log de CONTPAQi.");
ctx.erp.WriteToTableLog("Evento", "detalle adicional");
```

### 7.11 Impresión / Exportación

```csharp
ctx.erp.PrintDoc(documentId);             // imprime el documento
ctx.erp.PrintModule();                    // imprime la vista del módulo
ctx.erp.UpdatePrintedOn(documentId);      // marca como impreso (PrintedOn=GETDATE)

string pdf = ctx.erp.CreatePDF(documentId, @"C:\Temp\doc.pdf");
ctx.erp.ExportQueryToExcel("SELECT * FROM docDocument WHERE DocumentID=1");
ctx.erp.ExportJanusToExcel(@"C:\Temp\reporte.xlsx");
```

### 7.12 Correo (usa engUserMailConfig de CONTPAQi)

```csharp
// Enviar email con la config del usuario activo en Comercial.
ctx.erp.SendMail("cliente@ejemplo.com", "Asunto", "Cuerpo del mensaje");
ctx.erp.SendMail("cliente@ejemplo.com", "Con adjunto", "Cuerpo", @"C:\Temp\doc.pdf");
```

### 7.13 Internet / Web / Shell

```csharp
bool net = ctx.erp.IsConnectedToInternet();
string html = ctx.erp.GetWebContent("https://api.ejemplo.com/dato");
ctx.erp.RunShellExecute(@"C:\BrosLMV\MiHerramienta.exe", "--arg valor");
```

### 7.14 CFDI / Timbrado

```csharp
bool timbrado = ctx.erp.AlreadyDocsSigned(documentId);
int estadoPago = ctx.erp.GetStatusPaidID(documentId); // 0=sin pago, 1=parcial, 2=pagado
```

### 7.15 Escape hatch — COM directo

Para funciones de XEngine no cubiertas por `ctx.erp.*`:
```csharp
// Opción A: XE crudo + Com.Call
Com.Call(ctx.erp.XE, "NombreFuncion", new object[] { arg1, arg2 });

// Opción B: crear un COM helper de CONTPAQi (Doc, LBS, etc.)
var helper = ctx.erp.CrearHelper("Doc.clsMain");
Com.Call(helper, "RecalcDocument", new object[] { id });
```

---

## 8. Patrón completo: crear un documento desde otro

> **Desde v2.15.0** este patrón está encapsulado en `ctx.erp.NuevoDocumento(moduleId, depotId,
> businessEntityId)` + `ctx.erp.AgregarArticulo(docId, productId, cantidad, precio)` +
> `ctx.erp.RecalcCompleto(docId)` (y, si afecta inventario, `AffectStockNEW`). Úsalos en lugar del
> INSERT manual salvo que necesites control fino. Para tablas arbitrarias: `ctx.nuevo("tabla")`.
> El INSERT crudo de abajo sigue siendo válido y muestra qué campos van.

### 8.0 Plantillas visibles en la consola

La carpeta `C:\BrosLMV\scripts` guarda scripts ejecutables y compartidos, pero la sección
**Plantillas** de la consola no enumera todos esos archivos automáticamente. La lista visible se
define en `src\Consola.cs`, arreglo `PLANTILLAS`.

El patrón recomendado para plantillas comunitarias es:

1. Guardar el archivo versionado en `instalador\scripts\<NOMBRE>.ctx`.
2. Copiarlo al runtime en `C:\BrosLMV\scripts\<NOMBRE>.ctx`.
3. Agregar una entrada en `PLANTILLAS` que llame `CargarPlantillaArchivo("<NOMBRE>.ctx", fallback)`.
4. Recompilar y desplegar `BrosLMVClsMain.dll`.
5. Cerrar y abrir de nuevo la consola, porque la lista de plantillas se carga al crear la ventana.

Primera plantilla comunitaria:

| Plantilla | Archivo | Uso |
|---|---|---|
| Documento - Requisición de compra | `PLANTILLA_REQUISICION_COMPRA.ctx` | Abre una ventana WinForms para elegir proveedor, almacén y productos; crea una solicitud de compra módulo 1040 usando `ctx.erp.NuevoDocumento`, `ctx.erp.AgregarArticulo`, `ctx.erp.RecalcCompleto` y `ctx.erp.Save`. |
| Documento - Requisición de compra (Python) | `PLANTILLA_REQUISICION_COMPRA_PY.py` | Ejemplo seguro y documentado de creación del mismo documento desde Python. No abre WinForms; usa `ctx.query`, `ctx.execute` y `ctx.erp.*`. |

Este patrón concentra todo el conocimiento acumulado del motor de documentos.
Es la base de la **receta estrella** del motor no-code:

```csharp
// --- Script: Crear remisión desde pedido ---
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona un pedido."); return; }
long pedidoId = ids[0];

// Validar
var pedido = ctx.Query($"SELECT * FROM docDocument WHERE DocumentID={pedidoId}")[0];
if (pedido["CancelledOn"] != null) { ctx.Msg("El pedido está cancelado."); return; }

int moduleId = 157; // Remisiones (obtener de engModuleParameter si es configurable)
int depotId  = (int)(long)pedido["DepotID"];

// Folio (siempre via LBS, nunca MAX()+1)
string serie = ctx.erp.GetFolioPrefix(moduleId, depotId);
string folio = ctx.erp.GetNextFolio(moduleId, serie, depotId);

// Crear encabezado (INSERT directo con la estructura documentada)
ctx.NonQuery($@"
    INSERT INTO docDocument (ModuleID, DocumentTypeID, FolioPrefix, Folio, DepotID,
        BusinessEntityID, SourceDocumentID, OwnedBusinessEntityID,
        LanguageID, CurrencyID, Rate, PaymentTermID,
        CreatedBy, CreatedOn, DateDocument, UserID)
    VALUES ({moduleId}, 40, '{serie}', '{folio}', {depotId},
        {pedido["BusinessEntityID"]}, {pedidoId}, {ctx.erp.OwnedBusinessEntityId},
        3, 3, 1, {pedido["PaymentTermID"]},
        {ctx.UserID}, GETDATE(), GETDATE(), 0)");

int remId = (int)(long)ctx.Scalar("SELECT SCOPE_IDENTITY()");

// Copiar partidas
ctx.NonQuery($@"
    INSERT INTO docDocumentItem (DocumentID, ProductID, ProductKey, Description,
        Unit, Quantity, UnitPrice, TaxTypeID, LineNumber)
    SELECT {remId}, ProductID, ProductKey, Description,
        Unit, Quantity, UnitPrice, TaxTypeID, LineNumber
    FROM docDocumentItem WHERE DocumentID={pedidoId} AND DeletedOn IS NULL");

// Post-proceso XEngine (OBLIGATORIO — en este orden)
ctx.erp.RecalcDocument(remId);
ctx.erp.AffectStockNEW(remId);
ctx.erp.CalcularCostos(remId);
ctx.erp.UpdateStatusDelivery((int)pedidoId);
ctx.erp.RefreshGrid();
ctx.Msg($"Remisión {serie}{folio} creada correctamente.");
```

---

## 9. Python (REAL desde v2.12.0)

El contrato es idéntico — **mismos nombres, parámetros y efectos**. La diferencia es el transporte:
Python corre fuera de proceso y **relaya** `ctx.erp.*` y el SQL al addon (que tiene engine/conexión
vivos), vía Named Pipes + Protobuf. **Los nombres son PascalCase, iguales que en C#** (no snake_case):
`ctx.erp.RecalcCompleto(...)`, `ctx.erp.GetSalePrice(...)`. Los métodos propios de `ctx` (no `erp`)
sí son snake_case del SDK Python: `ctx.get_selected_ids()`, `ctx.query(...)`, `ctx.msg(...)`.

Desde **v2.15.0** Python crea documentos con los mismos helpers que C# (`NuevoDocumento`,
`AgregarArticulo`) **y** con el active-record genérico `ctx.nuevo("tabla")`:

```python
# lang: python
from broslmv import ctx
ids = ctx.get_selected_ids()
if not ids:
    ctx.msg("Selecciona un pedido.")
else:
    pedido = ctx.query(f"SELECT * FROM docDocument WHERE DocumentID={ids[0]}")[0]
    rem_id = ctx.erp.NuevoDocumento(157, int(pedido["DepotID"]), int(pedido["BusinessEntityID"]))
    # ... copiar partidas con ctx.erp.AgregarArticulo(...) o ctx.nuevo("docDocumentItem") ...
    ctx.erp.RecalcCompleto(rem_id)
    ctx.erp.AffectStockNEW(rem_id)   # remisión SÍ afecta inventario
    ctx.erp.RefreshGrid()
    ctx.msg(f"Remisión creada: {rem_id}")
```

Detalle y ejemplos completos en [`PYTHON.md`](PYTHON.md) §2.1 y §2.2.

---

## 10. Lo que NO hace ctx.erp (y no debe hacer)

| Acción | Por qué no |
|--------|-----------|
| Timbrar/cancelar ante el SAT | Usa webservices + certificados. Hacerlo via `ctx.erp.XE` + método nativo de XEngine o Cybernovus.Funciones. |
| Abrir pantallas nativas de Comercial | Pertenece a la UI del addon; no al script. |
| Acceder a tablas de otras empresas | El script solo tiene acceso a la empresa activa. |
| Ejecutar migraciones (`AplicarMigracion*`) | Internas de CONTPAQi; no son para scripts. |
