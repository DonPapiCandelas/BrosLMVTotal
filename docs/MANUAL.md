# BrosLMV — Manual de uso y programación

> **Versión 2.23.0** — API completa de `ctx` y `ctx.erp` para scripts C#, Python y SQL.
> Software libre (GPL-3.0). Documento de referencia para usuarios de la beta.

---

## Tabla de contenido

1. [Idea general](#1-idea-general)
2. [Cómo se conecta a CONTPAQi](#2-cómo-se-conecta-a-contpaqi)
3. [La Consola de scripts](#3-la-consola-de-scripts)
4. [Cómo crear un botón nuevo](#4-cómo-crear-un-botón-nuevo)
5. [API de `ctx` — SQL, contexto y utilidades](#5-api-de-ctx)
6. [API de `ctx.erp` — Motor de CONTPAQi (documentos, inventario, folios)](#6-api-de-ctxerp)
7. [Crear documentos — recetas por tipo](#7-crear-documentos)
8. [Crear catálogos](#8-crear-catálogos)
9. [Python — paridad y diferencias](#9-python)
10. [Ventanas WinForms: modeless (no bloquear Comercial)](#10-ventanas-winforms-modeless-no-bloquear-comercial)
11. [Ejemplos de scripts](#11-ejemplos-de-scripts)
12. [Advertencias y buenas prácticas](#12-advertencias-y-buenas-prácticas)
13. [Cómo está programado por dentro](#13-cómo-está-programado-por-dentro)
14. [Recompilar el núcleo](#14-recompilar-el-núcleo)
15. [Cheat sheet](#15-cheat-sheet)

---

## 1. Idea general

CONTPAQi permite poner botones en la barra (ribbon). Cada botón tiene un texto
`ControlExecute`. Cuando lo presionas, CONTPAQi crea un componente COM y le pide
ejecutar una función. BrosLMV es ese componente: corre totalmente bajo tu control,
en proceso, sin depender de servicios de licencia externos.

Hay **dos formas** de hacer un botón:

| | **Botón de SCRIPT (.ctx)** ✅ | **Botón del núcleo (C#)** |
|---|---|---|
| Dónde vive | `C:\BrosLMV\scripts\<EMPRESA>\NOMBRE.ctx` | Dentro de la DLL |
| Editar | Abrir el `.ctx`, cambiar, guardar | Editar C# y recompilar |
| ¿Recompilar / reiniciar? | **No** | Sí |
| Para qué | El 95% de los botones | Solo lógica base (la consola) |

**En la práctica: todos tus botones serán scripts `.ctx`.** La consola es lo único
que viene compilado, y es la herramienta con la que creas el resto.

> **Scripts por empresa (desde v2.1.0).** Cada base de datos tiene su propia carpeta:
> `C:\BrosLMV\scripts\<EMPRESA>\` (el nombre = la BD activa). Así un mismo nombre de
> script puede tener **reglas distintas en cada empresa** sin chocar. Al hacer clic en
> un botón, BrosLMV busca el script **primero en la carpeta de la empresa** y, si no
> está, en la **raíz** `scripts\` (scripts **compartidos** por todas). La consola
> abre/guarda en la carpeta de la empresa activa y muestra ambas secciones:
> **"Scripts — <empresa>"** y **"Compartidos (todas)"**.

---

## 2. Cómo se conecta a CONTPAQi

CONTPAQi usa un motor interno llamado **XEngine**. El flujo al presionar un botón:

```
1. Lee ControlExecute            ->  "BrosLMV.SUMA"
2. Parte en el PRIMER punto       ->  Prefijo="BrosLMV"   AppKey="SUMA"
3. Crea el objeto COM             ->  CreateObject("BrosLMV.clsMain")
4. Setea propiedades              ->  obj.XEngineLib = <motor>, obj.UserID = ...
5. Llama                          ->  obj.ExecuteFunction("SUMA")
```

Dentro de `ExecuteFunction`, BrosLMV decide:

```
AppKey = "CONSOLA"   ->  abre la ventana Consola
AppKey = "PRUEBA"    ->  mensaje de prueba
cualquier otro       ->  ejecuta  scripts\<EMPRESA>\<AppKey>.ctx
                          (y si no existe, scripts\<AppKey>.ctx compartido)
```

Por eso, para un botón nuevo solo necesitas:
- un archivo `scripts\<EMPRESA>\SUMA.ctx` (o en `scripts\` si será compartido), y
- un botón con `ControlExecute = BrosLMV.SUMA`.

> **Importante:** CONTPAQi **no** entrega los documentos seleccionados como
> parámetro. BrosLMV los lee del grid visual. Por eso en los scripts usas
> `ctx.GetSelectedIds()`, que ya hace ese trabajo.

---

## 3. La Consola de scripts

El botón **"Consola BrosLMV"** (`BrosLMV.CONSOLA`) abre un entorno de edición con
editor de código (resaltado de C#, números de línea, autocompletado de `ctx.`),
biblioteca de scripts, inspector de contexto y salida con pestañas.

| Acción | Qué hace |
|--------|----------|
| **Ejecutar (F5)** | Compila y ejecuta todo el script |
| **Ejecutar selección** | Ejecuta solo el texto seleccionado |
| **Verificar** | Solo compila; muestra errores con número de línea |
| **Nuevo / Abrir / Guardar / Guardar como / Duplicar** | Manejo de archivos `.ctx` |
| **Historial / Auditoría** | Lista de ejecuciones (fecha, empresa, módulo, usuario, filas, estado) |
| **Modo solo lectura** | Bloquea las escrituras (`ctx.NonQuery`) |

El ciclo de trabajo es ágil:
**escribir → Ejecutar → leer error → corregir → Ejecutar**, sin cerrar CONTPAQi.

> **La pestaña "Errores" muestra el traceback completo** (desde v2.31.0 en Python, ya lo
> hacía C#): no solo el mensaje corto (`'BusinessEntityName'  [KeyError]`), sino también en
> qué línea y función de TU script ocurrió, y toda la cadena de llamadas si el error viene de
> una función que llamó a otra. No hace falta adivinar ni agregar `print()` de más para
> ubicar el problema — el error ya trae el "mapa" completo.

---

## 4. Cómo crear un botón nuevo

### Opción rápida (todo desde la consola)

1. Abre **Consola BrosLMV**.
2. Escribe tu código (usa `ctx`, ver API abajo).
3. **Ejecutar (F5)** para probarlo hasta que quede bien.
4. **Guardar** como, por ejemplo, `SUMA.ctx`.
5. Da de alta el botón en el ribbon con el SQL `plantilla_crear_boton.sql`
   poniendo `@Execute = 'BrosLMV.SUMA'`.
6. Reinicia CONTPAQi.

### La regla de oro del nombre

```
Archivo:               C:\BrosLMV\scripts\SUMA.ctx
Botón ControlExecute:  BrosLMV.SUMA      ← SIN extensión, SIN puntos ni espacios
```

| Archivo | ControlExecute | ¿Funciona? |
|---------|----------------|------------|
| `SUMA.ctx` | `BrosLMV.SUMA` | ✅ |
| `RotacionInv.ctx` | `BrosLMV.RotacionInv` | ✅ |
| `SUMA.ctx` | `BrosLMV.SUMA.ctx` | ❌ |
| `Mi Script.ctx` | `BrosLMV.Mi Script` | ❌ |

> No uses los nombres reservados: `CONSOLA`, `PRUEBA`.

---

## 5. API de `ctx`

Dentro de cualquier script tienes `ctx` con todo esto listo. Ya están **importados**
(no necesitas `using`): `System`, `System.Collections.Generic`, `System.Linq`,
`System.Text`, `System.Data`, `System.Data.SqlClient`, `System.Windows.Forms`,
`System.Drawing`, `BrosLMV`.

### 5.1 SQL y conexión

Todos los métodos SQL **usan la conexión viva de CONTPAQi** (la misma que usa el grid,
sin credenciales adicionales).

> **Nota de rendimiento y confiabilidad (v2.21.5 – v2.21.8, ver CHANGELOG para la saga completa).**
> `Conexion.ObtenerAdo` prueba primero el `DataLayer` de CONTPAQi (conexión de propósito general,
> disponible en cualquier pestaña) y solo si no está usa la conexión ligada al grid activo
> (`janusGrid.ADORecordset.ActiveConnection`) — esa segunda opción se puede **cerrar** si el grid
> se refresca o cambia mientras un script (sobre todo una ventana WinForms interactiva, abierta
> varios minutos) sigue corriendo, y entonces el siguiente `ctx.query`/`ctx.erp` falla con
> *"la operación no está permitida si el objeto está cerrado"*. Por eso `ScriptContext.Ado()` es
> **auto-sanador**: antes de reusar la conexión cacheada de esta ejecución, la revalida con un
> `SELECT 1` trivial (cerrando el recordset de prueba de inmediato); si ya no sirve, la vuelve a
> resolver. Resolver la conexión desde cero puede tardar varios segundos (se midió un caso real
> de 6.6s) porque tocar `janusGrid.ADORecordset` fuerza a CONTPAQi a materializar el recordset del
> grid activo — evita hacer clic en otra cosa mientras un botón "no responde" unos segundos:
> Comercial no bombea mensajes durante esa espera, y Windows puede mostrar el diálogo nativo
> "the other application is busy" si le insistes.

> **Nota sobre transacciones explícitas (v2.21.10).** Evita envolver tu SQL en
> `BEGIN TRANSACTION ... COMMIT TRANSACTION` manual sobre esta conexión viva. Se confirmó en
> vivo que un `BEGIN TRANSACTION` explícito puede dejar la conexión en un estado que luego
> reporta *"la operación no está permitida si el objeto está cerrado"* en la siguiente llamada
> — probablemente porque el `DataLayer` de CONTPAQi administra su propia transacción ambiental y
> un control manual por T-SQL entra en conflicto con eso. `ErpContext.NuevoDocumento` y
> `AgregarArticulo` ya no usan transacción explícita por esto (ver CHANGELOG v2.21.10); si
> necesitas atomicidad real entre varias sentencias, usa `ctx.OpenConn()` (una `SqlConnection`
> propia, con su propio `SqlTransaction`) en vez de la conexión viva.

> **Nota sobre Unicode al guardar/cargar scripts (v2.21.11/v2.21.12).** `BrosGuardar`/`BrosCargar`
> (usados por "Guardar"/al ejecutar un botón desde SQL) intentan primero una conexión `SqlClient`
> directa y parametrizada para el TEXTO del script — la conexión viva de CONTPAQi puede angostar
> a ANSI un texto grande con acentos/emoji (confirmado en vivo). Si esa conexión directa no está
> disponible, cae automáticamente al camino de siempre (nunca falla solo por eso). Un script
> guardado ANTES de v2.21.11 con caracteres dañados (`U+FFFD`, irreversible) no se autorepara —
> hay que volver a guardarlo desde una fuente correcta.

| Método | Qué hace | Devuelve |
|--------|----------|----------|
| `ctx.Scalar(sql)` | Ejecuta SQL y devuelve un valor | `object` (null si vacío) |
| `ctx.Query(sql)` | Ejecuta SQL y devuelve filas | `List<Dictionary<string,object>>` |
| `ctx.NonQuery(sql)` | INSERT/UPDATE/DELETE; bloqueado en solo-lectura | `int` (filas afectadas) |
| `ctx.OpenConn()` | Abre `SqlConnection` propia (transacciones, parámetros) | `SqlConnection` |
| `ctx.JoinIds(ids)` | Lista de IDs → `"1,2,3"` para `IN (...)` | `string` |

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

### 5.2 Contexto y selección

| Propiedad / Método | Descripción |
|--------------------|-------------|
| `ctx.UserID` | ID del usuario activo en CONTPAQi |
| `ctx.ModuloActivo()` | `ActiveModuleID` del módulo abierto |
| `ctx.Empresa()` | Nombre de la BD activa (`DB_NAME()`) |
| `ctx.GetSelectedIds()` | `List<long>` con los IDs seleccionados en el grid |
| `ctx.SoloLectura` | `bool`. Si `true`, `ctx.NonQuery` lanza excepción |
| `ctx.FilasAfectadas` | Acumulado de filas afectadas por `ctx.NonQuery` |
| `ctx.XEngineLib` | Objeto XEngine crudo (COM). **Usar `ctx.erp.*` en su lugar.** |

### 5.3 Tokens

```csharp
string sql = ctx.ResolverTokens(
    "SELECT * FROM docDocument WHERE DocumentID = {pID} AND CreatedBy = {pUserID}"
);
```

| Token | Se sustituye por |
|-------|-----------------|
| `{pID}` | Primer ID seleccionado (o `0`) |
| `{pIDs}` | Todos los IDs seleccionados, separados por coma |
| `{pUserID}` | `ctx.UserID` |
| `{pModulo}` | `ActiveModuleID` |
| `{pEmpresa}` | `ctx.Empresa()` |
| `{DATOS:Campo}` | Valor del campo `Campo` en la primera fila seleccionada |

### 5.4 UI y utilidades

| Método | Descripción |
|--------|-------------|
| `ctx.Msg(texto)` | `MessageBox` informativo |
| `ctx.Msg(texto, titulo)` | `MessageBox` con título |
| `ctx.Confirm(texto)` | `MessageBox` Sí/No → `bool` |
| `ctx.Log(texto)` | Escribe en `C:\BrosLMV\logs\Script_YYYYMMDD.txt` |
| `ctx.DiagConexion()` | Diagnóstico: de dónde sale la conexión → `string` |

---

## 6. API de `ctx.erp`

`ctx.erp` es el puente entre el script y el **motor de CONTPAQi** (XEngine).
Usa `ctx.erp.*` siempre que puedas: evita SQL directo para operaciones que XEngine
ya sabe hacer (folios, inventario, costos, timbrado, correo).

> **Regla de oro:** si XEngine tiene una función para algo, úsala via `ctx.erp`.
> Si no, usa `ctx.NonQuery` o `ctx.Query`. Nunca mezcles INSERT crudo + XEngine
> en la misma operación sin entender las dependencias.

### 6.1 Contexto ERP

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `ctx.erp.UserId` | `int` | Usuario activo |
| `ctx.erp.UserName` | `string` | Nombre del usuario |
| `ctx.erp.OwnedBusinessEntityId` | `int` | Empresa propietaria (emisora) |
| `ctx.erp.ActiveModuleId` | `int` | Módulo activo |
| `ctx.erp.CurrencyId` | `int` | Moneda del módulo activo |
| `ctx.erp.ComercialRFC` | `string` | RFC de la empresa |
| `ctx.erp.SoftwareVersion` | `string` | Versión de ComercialSP |

### 6.2 Crear documentos — `NuevoDocumento` y `AgregarArticulo`

Estos son los **builders principales**. Crean el encabezado, las 4 anclas y las
partidas como lo hace CONTPAQi nativo.

#### `ctx.erp.NuevoDocumento(moduleId, depotId, businessEntityId=0)` → `int DocumentID`

**Qué hace:** crea el encabezado `docDocument` + las **4 filas ancla 1:1** que todo
documento requiere:
- `docDocumentExt` (`IDExtra = DocumentID`)
- `docDocumentExtra` (`DocumentID`)
- `docDocumentCFD` (`FinancialOperationID=0`, `Anexo20Ver='4.0'`)
- `docDocumentPaymentAgenda` (1 parcialidad al 100%)

**Campos que ya setea (no repetir):**
- `ModuleID`, `DocumentTypeID`, `DocRecipientID` (leídos de `engModuleParameter`)
- `OwnedBusinessEntityID`, `BusinessEntityID`, `DepotID`
- `FolioPrefix`, `Folio` (vía `LBS.GetNextFolio`)
- `DateDocument`, `LanguageID=3`, `CurrencyID=3`, `Rate=1`
- `MustBeSynchronized=1`, `ExportID=1`
- `DateCost`, `DateDocDelivery`, `DateFrom`, `DateTo`, `DateLastPayment` = fecha actual
- `CreatedBy`, `CreatedOn`

**Lo que NO setea (el script debe ponerlo según el tipo de documento):**
- `PaymentTermID` (default 1; debe ser 0 en inventario, 3-4-12 en compra/venta)
- `DepotIDFrom` (0 en compra/venta/solicitud, = DepotID en inventario, ≠ DepotID en traspaso)
- `CampaignID`, `CostCenterID`, `ProjectID`
- `StatusDeliveryID`, `StatusPaidID`, `UserID`
- `DateDelivery` (solo en OC, Pedido, RC, Remisión, Traspaso)

**Parámetros:**
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `moduleId` | `int` | requerido | ModuleID (202=Entrada, 203=Salida, 183=OC, 152=FC, 21=FactCli, 967=Pedido, 1040=Solicitud, etc.) |
| `depotId` | `int` | requerido | ID del almacén |
| `businessEntityId` | `int` | `0` | ID de la entidad (cliente/proveedor) |

#### `ctx.erp.AgregarArticulo(documentId, productId, cantidad=1, precioUnitario=-1, costo=-1, taxTypeIdOverride=-1, descuentoPerc=0, deliverDocumentItemId=0, lote=null, serialNumber=null)` → `int DocumentItemID`

**Qué hace:** agrega una partida a un documento, leyendo los datos del producto de
`orgProduct`. Llena la partida como el nativo.

**Campos que ya setea (no repetir):**
- `DocumentID`, `ProductID`, `ProductKey`, `Description`, `Unit`, `TaxTypeID`
- `TaxPerc` — el **%** del impuesto (0.16 = 16%), resuelto de **`vwLBSTaxPerc`** para el
  `TaxTypeID` final. **Importante:** el motor de recálculo usa este valor tal cual guardado, NO
  lo vuelve a calcular a partir de `TaxTypeID` — si `TaxPerc` queda en 0 (como pasaba antes de
  v2.20.1), el impuesto **no se aplica** aunque `TaxTypeID` esté bien.
- `DiscountPerc` — el descuento por partida (fracción; ver parámetro `descuentoPerc`)
- `Quantity`, `UnitPrice`, `Total` (= cantidad × precio; **no** resta el descuento — el nativo
  aplica el descuento aparte, al recalcular el documento completo)
- `ApplyGlobalDiscount=1`, `DeductiblePerc=1`, `IsBusinessOperation=1`, `MustBeDelivered=1`
- `DateItem` = fecha actual, `CoefUnit=1`
- `ClaveUnidad`, `ObjetoImpuesto` (copiados del producto)
- `CostPrice` si `costo >= 0`

**Parámetros:**
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `documentId` | `int` | requerido | ID del documento |
| `productId` | `int` | requerido | ProductID del producto |
| `cantidad` | `double` | `1` | Cantidad |
| `precioUnitario` | `double` | `-1` | Precio unitario (<0 = sin precio, queda 0) |
| `costo` | `double` | `-1` | Costo de entrada (<0 = no setear; >=0 puebla CostPrice) |
| `taxTypeIdOverride` | `int` | `-1` | Impuesto a usar en vez del de `orgProduct.TaxTypeID` (<0 = usar el del producto). Útil para un combo de "Impuesto" editable en la UI — ver "Ejemplo Premium · Orden de Compra". |
| `descuentoPerc` | `double` | `0` | Descuento de la partida, en **fracción** (0.05 = 5%, no "5") |
| `deliverDocumentItemId` | `int` | `0` | (v2.22.0) `DocumentItemID` del documento ORIGEN que esta partida está surtiendo — p. ej., la partida de la Orden de Compra que una Recepción de Compra está recibiendo. 0 = no aplica. Ver "Ejemplo Premium · Recepción de Compra" y MANUAL.md §10.4 — para Factura de Compra el campo real es OTRO (`SourceDocumentItemID`, fijado aparte por SQL, no por este parámetro). |
| `lote` | `string` | `null` | (v2.22.0) Solo si `orgProduct.UseLot=1`. **Ojo:** esto llena el campo simple `docDocumentItem.Lot` — NO es lo mismo que las tablas de detalle `docDocumentLot`/`docDocumentSerialNumber` (que soportan varios lotes/series por partida, con caducidad); para eso, ver el capturador de la plantilla de Recepción de Compra, que hace el INSERT directo a esas tablas. |
| `serialNumber` | `string` | `null` | (v2.22.0) Solo si `orgProduct.UseSerialNumber=1`. Mismo comentario que `lote`: un solo valor aquí, no reemplaza la captura de múltiples series. |

### 6.3 Operaciones de documento (post-creación)

Después de `NuevoDocumento` + `AgregarArticulo` × N, se llama en este orden:

| Método | Qué hace | Tablas que toca |
|--------|----------|-----------------|
| `ctx.erp.RecalcCompleto(documentId)` | Recalcula totales + costos + saldo pagado | `docDocument.Total/SubTotal`, `orgProductCostComercial`, `orgProductCostFiscal` |
| `ctx.erp.AffectStockNEW(documentId)` | Afecta inventario (kardex). Explota paquetes/PT | `orgProductKardex` (± según tipo: entrada +, salida -, traspaso ±) |
| `ctx.erp.Save(documentId)` | Guarda y avanza el folio | `docDocument`, avanza consecutivo |
| `ctx.erp.AffectStock(documentId)` | Versión legacy de afectación. **Preferir `AffectStockNEW`** | `orgProductKardex` |
| `ctx.erp.CalcularCostos(documentId)` | Actualiza costos (promedio, PEPS, etc.) | `orgProductCostComercial` |
| `ctx.erp.RecalcDocument(documentId)` | Recalcula totales (vía Doc.clsMain) | `docDocument` |
| `ctx.erp.UpdateStatusDelivery(documentId)` | Actualiza estatus de entrega | `docDocument.StatusDeliveryID`, `docDocumentDeliveryAgenda` |
| `ctx.erp.UpdateDocumentPaidInfo(documentId)` | Recalcula saldo pagado | `docDocument`, `docDocumentPaymentAgenda` |
| `ctx.erp.ActualizarParcialidad(documentId)` | Actualiza parcialidad (complementos de pago SAT) | `docDocumentPaymentAgenda` |
| `ctx.erp.RefreshDocumento(documentId)` | Refresca VISUALMENTE documento abierto | — (solo UI) |

> ⚠️ **`UpdateStatusDelivery` NO es opcional, aunque no afecte inventario.** `RecalcCompleto`
> **no lo calcula**. Sin llamarlo explícitamente (después de `Save`), el documento queda con
> "Estatus de entrega: No Aplica" en el grid nativo, aunque el documento esté bien creado.
> Orden recomendado para un documento sin inventario (Solicitud, OC): `NuevoDocumento` →
> `AgregarArticulo` × N → `RecalcCompleto` → `Save` → **`UpdateStatusDelivery`** → `RefreshGrid`.

> ⚠️ **`RefreshGrid()` NO "pega" visualmente si se llama mientras tu propia ventana (`frm`)
> sigue al frente** — Comercial no está en foco en ese instante, aunque la llamada no truene
> y el dato en la base ya haya quedado correcto (confirmado en vivo dos veces: `RecepcionOc`
> en Distribuciones_Candelas y `GenOrdenCompra` en Coctel_de_Ideas, ambos con ventana propia
> tipo WinForms). **Patrón correcto**: no llamar `RefreshGrid()` inline después de `Save`;
> engancharlo a `frm.FormClosed` en vez de eso, así corre siempre justo cuando el control
> regresa a Comercial, sin importar por cuál salida se cierre la ventana:
> ```csharp
> frm.FormClosed += (_, __) => { try { ctx.erp.RefreshGrid(); } catch { } };
> ```
> Nota: con "Guardar y Nueva" (la ventana NO se cierra) el grid nativo no se refresca hasta
> que el usuario cierre la ventana — limitación aceptada, no hay forma de "pegar" el refresco
> mientras la ventana propia conserva el foco.

### 6.4 Cancelar / Eliminar

| Método | Qué hace | ⚠️ Advertencia |
|--------|----------|----------------|
| `ctx.erp.CancelDocument(documentId)` | Cancela el documento (marca `CancelledOn`, revierte kardex) | **Preferir este sobre `Delete`** |
| `ctx.erp.Delete(documentId)` | Soft-delete (marca `DeletedOn`) | **NO revierte kardex** — el inventario queda inflado. Solo para docs sin afectación. |
| `ctx.erp.ReactivateDocument(documentId)` | Reactiva un documento cancelado | Inverso de `CancelDocument` |

> ⚠️ **CRÍTICO:** `ctx.erp.Delete()` hace borrado lógico (`DeletedOn`) pero **NO revierte
> el kardex** ni los costos. Un documento de inventario "borrado" sigue sumando existencias.
> Para documentos que afectaron inventario, usa **siempre** `CancelDocument()` en su lugar.
> `ReactivateDocument()` es el inverso de `CancelDocument`.

### 6.5 UI de CONTPAQi

| Método | Descripción |
|--------|-------------|
| `ctx.erp.RefreshGrid()` | Refresca el grid del módulo activo |
| `ctx.erp.RefreshRibbon()` | Refresca el ribbon (botones) |
| `ctx.erp.GotoModuleID(moduleId)` | Navega al módulo indicado |
| `ctx.erp.OpenModule(moduleId)` | Abre el módulo indicado |
| `ctx.erp.OpenBrowser(url)` | Abre URL en el browser interno de Comercial |
| `ctx.erp.ShowMessage("texto")` | Mensaje en la barra de estatus de Comercial |

### 6.6 Folio

```csharp
string serie = ctx.erp.GetFolioPrefix(moduleId, depotId);
string folio = ctx.erp.GetNextFolio(moduleId, serie, depotId);
```

| Método | Descripción |
|--------|-------------|
| `ctx.erp.GetFolioPrefix(moduleId, depotId)` | Serie/prefijo configurada para módulo+almacén |
| `ctx.erp.GetNextFolio(moduleId, prefix, depotId)` | Siguiente folio disponible |

> **Nota:** `NuevoDocumento` ya resuelve el folio automáticamente. Estos métodos son para
> scripts que necesiten el folio sin crear documento.

### 6.7 Existencias, precios y costos

| Método | Descripción |
|--------|-------------|
| `ctx.erp.GetProductStock(productId, depotId)` | Stock en un almacén → `double` |
| `ctx.erp.GetSalePrice(productId)` | Precio de venta (lista general) → `double` |
| `ctx.erp.GetSalePrice(productId, businessEntityId)` | Precio de venta (lista del cliente) → `double` |
| `ctx.erp.GetBusinessEntitySalePrice(productId, beId)` | Igual que arriba, explícito |
| `ctx.erp.GetBuyPrice(productId)` | Precio de compra → `double` |
| `ctx.erp.GetCostPrice(productId)` | Costo actual → `double` |
| `ctx.erp.GetCostPriceComercial(productId)` | Costo comercial → `double` |
| `ctx.erp.GetCostLast(productId)` | Último costo → `double` |
| `ctx.erp.GetPriceWithTaxes(precio, taxTypeId)` | Precio + IVA → `double` |
| `ctx.erp.GetCurrencyRate(currencyId)` | Tipo de cambio → `double` |
| `ctx.erp.GetCurrencyRateBanxico(currencyId)` | Tipo de cambio Banxico → `double` |
| `ctx.erp.GetCoefConversion(productId, "PZA", "CAJA")` | Coeficiente de conversión → `double` |
| `ctx.erp.ProductIsKit(productId)` | ¿Es paquete/kit? → `bool` |

### 6.8 Crédito

| Método | Descripción |
|--------|-------------|
| `ctx.erp.VerifyCreditLimit(beId, importe)` | ¿El cliente tiene crédito suficiente? → `bool` |
| `ctx.erp.VerifyCreditLimitOverdue(beId)` | ¿El cliente tiene vencidos? → `bool` |

### 6.9 Parámetros de módulo y empresa

| Método | Descripción |
|--------|-------------|
| `ctx.erp.GetModuleParameter(moduleId, "ParamKey")` | Lee un parámetro de `engModuleParameter` |
| `ctx.erp.SaveModuleParameter(moduleId, "ParamKey", "valor")` | Guarda un parámetro |
| `ctx.erp.GetParameter("ClaveGlobal")` | Parámetro global de `engParameter` |

Parámetros clave por módulo (`engModuleParameter`):

| ParameterKey | Significado | Ejemplo |
|---|---|---|
| `DocumentTypeID` | Tipo de documento | 16=Entrada, 17=Salida, 40=OC, 5=Factura |
| `DocRecipient` | Destinatario | 1=Cliente, 2=Proveedor, 3=Almacén |
| `StockAffectation` | Dirección del kardex | 1=Entrada(+), -1=Salida(-), 0=Sin afectar |
| `AccountingPoliza` | ¿Genera póliza contable? | 1=FC/FactCli, 0=resto |
| `ItemTaxTypeID` | ¿Usa impuesto de partida? | 1=FactCli/OC/Pedido, 0=Inventario |
| `GenerateDelivery` | ¿Genera agenda de entrega? | 1=OC/Pedido, 0=resto |
| `Payment` | ¿Genera agenda de pago? | 1=FactCli/FactCom/Pedido, 0=resto |
| `AutogenerateNextFolio` | ¿Autogenera folio? | 1=todos los módulos |

### 6.10 DLookup — consultas puntuales sin SQL

| Método | Descripción |
|--------|-------------|
| `ctx.erp.DLookup("Campo", "Tabla", "WHERE")` | Devuelve `object` (primer valor) |
| `ctx.erp.DLookupStr("Campo", "Tabla", "WHERE")` | Devuelve `string` |
| `ctx.erp.DLookupInt("Campo", "Tabla", "WHERE")` | Devuelve `int` |

```csharp
string nombre = ctx.erp.DLookupStr("BusinessEntityName", "orgBusinessEntity", "BusinessEntityID=5");
int total = ctx.erp.DLookupInt("Total", "docDocument", "DocumentID=100");
```

### 6.11 Utilidades de negocio

| Método | Descripción |
|--------|-------------|
| `ctx.erp.GetTotalLetter(1500.50)` | "MIL QUINIENTOS PESOS 50/100 M.N." |
| `ctx.erp.GetTotalLetterEN(1500.50)` | "ONE THOUSAND FIVE HUNDRED..." |
| `ctx.erp.GetBarCode("ABC123")` | Código de barras → `string` |
| `ctx.erp.ValidRFC("XAXX010101000")` | Validar RFC → `bool` |
| `ctx.erp.FormatCurrency(1234.5)` | "$1,234.50" |
| `ctx.erp.EncryptString("texto")` | Encripta con llave de CONTPAQi |
| `ctx.erp.DecryptString("texto")` | Desencripta con llave de CONTPAQi |

### 6.12 Correo, impresión y exportación

| Método | Descripción |
|--------|-------------|
| `ctx.erp.SendMail("a@b.com", "Asunto", "Cuerpo")` | Usa `engUserMailConfig` de CONTPAQi |
| `ctx.erp.SendMail("a@b.com", "Asunto", "Cuerpo", @"C:\doc.pdf")` | Con adjunto |
| `ctx.erp.PrintDoc(documentId)` | Imprime el documento |
| `ctx.erp.PrintModule()` | Imprime la vista del módulo |
| `ctx.erp.UpdatePrintedOn(documentId)` | Marca como impreso (`PrintedOn=GETDATE`) |
| `ctx.erp.CreatePDF(documentId, @"C:\doc.pdf")` | Exporta a PDF → `string` |
| `ctx.erp.ExportQueryToExcel("SELECT ...")` | Exporta consulta a Excel |
| `ctx.erp.ExportJanusToExcel(@"C:\reporte.xlsx")` | Exporta el grid a Excel |

### 6.13 Internet / Web / Shell

| Método | Descripción |
|--------|-------------|
| `ctx.erp.IsConnectedToInternet()` | ¿Hay internet? → `bool` |
| `ctx.erp.GetWebContent("https://api.ejemplo.com/dato")` | GET HTTP → `string` |
| `ctx.erp.RunShellExecute(@"C:\tool.exe", "--arg")` | Ejecuta un programa externo |

### 6.14 CFDI / Timbrado

| Método | Descripción |
|--------|-------------|
| `ctx.erp.AlreadyDocsSigned(documentId)` | ¿Está timbrado? → `bool` |
| `ctx.erp.GetStatusPaidID(documentId)` | 0=sin pago, 1=parcial, 2=pagado → `int` |
| `ctx.erp.Timbrar(documentId, pruebas=False)` | Timbra el documento ante el PAC configurado en la empresa. Lanza excepción si falla (revisar el mensaje: viene del PAC/SAT). `pruebas=True` usa el modo de pruebas del PAC (no genera timbre fiscal real). |
| `ctx.erp.RelacionarCFDI(documentId, sourceDocumentId, tipoRelacion)` | Inserta en `docDocumentCFDIRelacionados` — liga un CFDI con otro (nota de crédito, devolución, aplicación de anticipo...). `tipoRelacion` es el código del catálogo SAT `c_TipoRelacion` como texto (p. ej. `"07"` = aplicación de anticipo). |

> ⚠️ **`Timbrar` es una operación fiscal real.** Antes de llamarla en producción, confirma
> que el documento está completo (partidas, cliente, forma de pago) y que el PAC/CSD de la
> empresa está correctamente configurado. Usa `pruebas=True` para validar el flujo de tu
> script sin generar un timbre real. Internamente usa el mismo componente de timbrado nativo
> de Comercial que usa su propio módulo de facturación — no un PAC ni una firma implementados
> por BrosLMV.

### 6.15 Auditoría / Log

| Método | Descripción |
|--------|-------------|
| `ctx.erp.WriteToLog("Mensaje")` | Escribe en el log de CONTPAQi |
| `ctx.erp.WriteToTableLog("Evento", "detalle")` | Escribe en tabla de log de CONTPAQi |

### 6.16 Escape hatch — COM directo

Para funciones de XEngine no cubiertas por los wrappers:

```csharp
// Llamar cualquier método de XEngine
object resultado = ctx.erp.Call("NombreFuncion", arg1, arg2);

// Leer cualquier propiedad de XEngine
object valor = ctx.erp.Get("NombrePropiedad");

// Crear un helper COM de CONTPAQi (Doc.clsMain, LBS.clsMain, etc.)
var helper = ctx.erp.CrearHelper("Doc.clsMain");
Com.Call(helper, "RecalcDocument", new object[] { id });
```

---

## 7. Crear documentos

### 7.1 Patrón canónico

Todo documento se crea con este flujo:

```
NuevoDocumento → (UPDATE perfil por módulo) → AgregarArticulo × N
→ (INSERT lotes/series) → RecalcCompleto → AffectStockNEW? → Save → (post-Save fixes)
```

**Lo que YA hace el addon (v2.18.0+):**
- `NuevoDocumento` crea las 4 anclas + campos universales
- `AgregarArticulo` llena la partida como el nativo (flags, claves SAT, costo opcional)

**Lo que el script SÍ debe poner (varía por tipo de documento):**
- `PaymentTermID`, `DepotIDFrom`
- `CampaignID`, `CostCenterID`, `ProjectID`
- `TaxTypeID` de partida si el documento maneja importes con IVA
- `DateDelivery` si el módulo requiere agenda de entrega
- Lotes/series si el producto los usa

### 7.2 Tabla de módulos

| Documento | ModuleID | DocumentTypeID | Afecta inventario | Genera póliza | Payment |
|-----------|----------|----------------|-------------------|---------------|---------|
| Entrada almacén | 202 | 16 | Sí (+1) | No | No |
| Salida almacén | 203 | 17 | Sí (-1) | No | No |
| Traspaso | 204 | 18 | Sí (±1) | No | No |
| Orden de compra | 183 | 40 | Sí (qty=0) | No | No |
| Recepción compra | 184 | 3 | Sí (+1) | No | No |
| Solicitud compra | 1040 | 49 | No | No | No |
| Factura compra | 152 | 5 | No | **Sí** | **Sí** |
| Factura cliente | 21 | 5 | No | **Sí** | **Sí** |
| Pedido | 967 | 40 | No (qty=0) | No | **Sí** |
| Remisión | 157 | 3 | Sí (-1) | No | No |

### 7.3 Receta — Entrada de almacén (ModuleID=202)

```csharp
int depot = 5;  // ID del almacén
int doc = ctx.erp.NuevoDocumento(202, depot);
ctx.NonQuery($"UPDATE docDocument SET DepotIDFrom=DepotID, PaymentTermID=0 WHERE DocumentID={doc}");

// Agregar partidas (producto, cantidad, precio=-1, costo)
ctx.erp.AgregarArticulo(doc, 20, 10, -1, 100);   // producto 20, 10 pzas, costo $100
ctx.erp.AgregarArticulo(doc, 25, 5, -1, 200);    // producto 25, 5 pzas, costo $200

// Si el producto usa lotes:
ctx.NonQuery($"INSERT INTO docDocumentLot (DocumentID, DocumentItemID, ProductID, LotNumber, Quantity, ExpirationDate, Unit) VALUES ({doc}, <itemId>, <prodId>, '<lote>', <cant>, '<fecha>', '<unidad>')");

// Si el producto usa series (1 fila por unidad):
ctx.NonQuery($"INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, StatusID) VALUES ({doc}, <itemId>, <prodId>, '<serie>', 1)");

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
return "Entrada creada: doc=" + doc;
```

**Perfil entrada:**
- `DepotIDFrom` = `DepotID`
- `PaymentTermID` = `0`
- `DateDelivery` = NULL (no aplica)
- `StatusPaidID` = `3` (pagado — es el default, no implica pago real)

### 7.4 Receta — Salida de almacén (ModuleID=203)

```csharp
int depot = 1;
int doc = ctx.erp.NuevoDocumento(203, depot);
ctx.NonQuery($"UPDATE docDocument SET DepotIDFrom=DepotID, PaymentTermID=0 WHERE DocumentID={doc}");

ctx.erp.AgregarArticulo(doc, 20, 5);   // 5 pzas, sin costo (usa promedio)

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
return "Salida creada: doc=" + doc;
```

### 7.5 Receta — Orden de compra (ModuleID=183)

```csharp
int depot = 1;
int proveedorBE = 6;  // BusinessEntityID del proveedor
int doc = ctx.erp.NuevoDocumento(183, depot, proveedorBE);

// Perfil OC: PaymentTermID=4 (50%+50% a 3 meses), DateDelivery=fecha, DepotIDFrom=0
ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID=4,
        DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
    WHERE DocumentID={doc}");

// Agregar partidas CON precio y costo
int item1 = ctx.erp.AgregarArticulo(doc, 16, 5, 250, 200);
int item2 = ctx.erp.AgregarArticulo(doc, 3, 10, 100, 80);

// Fijar TaxTypeID de partida (el nativo lo decide por contexto; para compras usar 5=IVA16%)
ctx.NonQuery($"UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID={doc} AND DeletedOn IS NULL");

ctx.erp.RecalcCompleto(doc);
// AffectStockNEW para OC deja kardex con Qty=0 (compromete sin mover)
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);

// Fix post-Save: regenerar PaymentAgenda con montos reales
// (el Save nativo regenera con cache stale; corregir manualmente)
ctx.erp.UpdateDocumentPaidInfo(doc);

ctx.erp.RefreshGrid();
return "OC creada: doc=" + doc;
```

### 7.6 Receta — Solicitud de compra (ModuleID=1040)

```csharp
int depot = 1;
int proveedorBE = 6;
int doc = ctx.erp.NuevoDocumento(1040, depot, proveedorBE);

// Perfil solicitud: DepotIDFrom=0, PaymentTermID=0, sin fechas extra, sin inventario
ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID=0,
        UserID=0, CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL
    WHERE DocumentID={doc}");

int itemId = ctx.erp.AgregarArticulo(doc, 20, 10, -1, 100);

// Relación producto↔proveedor (documentos de compra)
ctx.NonQuery($@"
    INSERT INTO orgProductSupplier (ProductID, BusinessEntityID, CostPrice, CurrencyID)
    VALUES (20, {proveedorBE}, 100, 3)");

ctx.erp.RecalcCompleto(doc);
// SIN AffectStockNEW (solicitud no afecta inventario)
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
return "Solicitud creada: doc=" + doc;
```

### 7.7 Receta — Factura de compra (ModuleID=152)

```csharp
int depot = 1;
int proveedorBE = 6;
int doc = ctx.erp.NuevoDocumento(152, depot, proveedorBE);

ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID=4, StatusPaidID=3
    WHERE DocumentID={doc}");

ctx.erp.AgregarArticulo(doc, 16, 5, 250, 200);
ctx.erp.AgregarArticulo(doc, 3, 10, 100, 80);

ctx.NonQuery($"UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID={doc} AND DeletedOn IS NULL");

ctx.erp.RecalcCompleto(doc);
// SIN AffectStockNEW (factura no mueve inventario)
ctx.erp.Save(doc);

// Post-Save: regenerar PaymentAgenda con montos reales
ctx.erp.UpdateDocumentPaidInfo(doc);

ctx.erp.RefreshGrid();
return "Factura creada: doc=" + doc;
```

> **Nota sobre contabilidad:** La factura de compra **debería** generar póliza contable
> (`accPoliza` + `accPolizaTransaccion` + `accPolizasPorDocumentID`). Verificar que
> `ctx.erp.Save()` la dispare. Si no, consultar la documentación del lab.

### 7.8 Receta — Traspaso entre almacenes (ModuleID=204)

```csharp
int depotOrigen = 1;
int depotDestino = 2;
int doc = ctx.erp.NuevoDocumento(204, depotOrigen);

// Traspaso: DepotID=origen, DepotIDFrom=destino; PaymentTermID=0
ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom={depotDestino}, PaymentTermID=0,
        DateDelivery=GETDATE()
    WHERE DocumentID={doc}");

ctx.erp.AgregarArticulo(doc, 20, 5);  // 5 unidades del producto 20

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);  // Genera kardex: -5 en origen, +5 en destino
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
return "Traspaso creado: doc=" + doc;
```

---

## 8. Crear catálogos

Los catálogos (cliente, proveedor, producto, almacén, proyecto) **no tienen creador
nativo en `ctx.erp`** — se crean con INSERT directo vía `ctx.NonQuery`.

### 8.1 Cliente / Proveedor (modelo entidad/rol)

```csharp
// 1. Crear la entidad base
int beId = (int)(long)ctx.Scalar(@"
    INSERT INTO orgBusinessEntity (BusinessEntityName, BusinessEntityKey, OfficialName,
        FiscalRegimeID, CreatedBy, CreatedOn, UserID)
    OUTPUT INSERTED.BusinessEntityID
    VALUES ('Mi Cliente SA', 'CLI-001', 'Mi Cliente SA de CV', 601,
        " + ctx.UserID + @", GETDATE(), 0)");

// 2. Rol de cliente
ctx.NonQuery("INSERT INTO orgCustomer (BusinessEntityID, CustomerID) VALUES (" + beId + ", " + beId + ")");

// 3. Información principal
ctx.NonQuery("INSERT INTO orgBusinessEntityMainInfo (BusinessEntityID) VALUES (" + beId + ")");

// 4. Datos fiscales
ctx.NonQuery("INSERT INTO orgIdentificationKey (BusinessEntityID, TaxID, TaxName, CURP) VALUES (" + beId + ", 'XAXX010101000', 'Mi Cliente SA de CV', '')");

// 5. Dirección
ctx.NonQuery("INSERT INTO orgAddress (BusinessEntityID) VALUES (" + beId + ")");
ctx.NonQuery("INSERT INTO orgAddressDetail (AddressID) VALUES (" + beId + ")");

// 6. Canal de comunicación
ctx.NonQuery("INSERT INTO orgCommunicationChannel (BusinessEntityID) VALUES (" + beId + ")");

ctx.Msg("Cliente creado: BE=" + beId);
```

### 8.2 Producto

```csharp
int prodId = (int)(long)ctx.Scalar(@"
    INSERT INTO orgProduct (ProductKey, ProductName, ProductTypeID, TaxTypeID,
        Unit, ClaveUnidad, ObjetoImpuesto, ClaveProdServ,
        ProductBuy, ProductSale, ProductInventory, UseLot, UseSerialNumber,
        CreatedBy, CreatedOn, UserID)
    OUTPUT INSERTED.ProductID
    VALUES ('PROD-001', 'Mi Producto', 1, 2,
        'PZA', 'H87', '02', '43231500',
        1, 1, 1, 0, 0,
        " + ctx.UserID + @", GETDATE(), 0)");

// Tablas satélite (dependen del ProductTypeID)
ctx.NonQuery("INSERT INTO orgProductPicture (ProductID) VALUES (" + prodId + ")");
ctx.NonQuery("INSERT INTO orgProductUnitConversion (ProductID) VALUES (" + prodId + ")");

ctx.Msg("Producto creado: ProductID=" + prodId);
```

**ProductTypeID:** 1=producto, 2=producto terminado, 3=paquete, 4=servicio, 7=insumo.
Tablas satélite: producto→Picture+UnitConversion, PT/paquete→orgProductComponent, insumo→orgProductExt, servicio→ninguna.

### 8.3 Almacén

```csharp
int depotId = (int)(long)ctx.Scalar(@"
    INSERT INTO orgDepot (DepotKey, DepotName, CreatedBy, CreatedOn)
    OUTPUT INSERTED.DepotID
    VALUES ('ALM-003', 'Almacén Norte', " + ctx.UserID + @", GETDATE())");

ctx.NonQuery("INSERT INTO orgAddressDetail (AddressID) VALUES (" + depotId + ")");
ctx.Msg("Almacén creado: DepotID=" + depotId);
```

---

## 9. Python

### 9.1 Paridad C# ↔ Python

El contrato es idéntico — **mismos nombres, parámetros y efectos**. La diferencia:
Python corre fuera de proceso y **relaya** `ctx.erp.*` y SQL al addon vía Named Pipes.

| Capacidad | C# | Python |
|-----------|----|--------|
| `ctx.erp.*` (todos los métodos) | ✅ directo | ✅ relay (misma lógica) |
| `ctx.Query/Scalar/NonQuery` | ✅ | ✅ relay |
| `ctx.GetSelectedIds()` | ✅ | ✅ `ctx.get_selected_ids()` |
| `ctx.Msg/Confirm/Log` | ✅ | ✅ |
| `ctx.nuevo(tabla)` / `ctx.registro(tabla, pk)` | ❌ | ✅ active-record (INSERT crudo) |
| Transacciones | ✅ `ctx.OpenConn()` | ❌ |

**Nombres:**
- `ctx.erp.*`: **PascalCase igual que C#** → `ctx.erp.NuevoDocumento(...)`, `ctx.erp.RecalcCompleto(...)`
- `ctx.*` (no erp): snake_case del SDK Python → `ctx.get_selected_ids()`, `ctx.query(...)`, `ctx.msg(...)`

### 9.2 Ejemplo Python

> El encabezado `# lang: python` sigue siendo la forma recomendada de marcar el script (más
> explícito, y funciona aunque el código no importe `ctx` de inmediato). Pero si se te olvida,
> **ya no rompe**: desde v2.29.0, si el código contiene `from broslmv import ctx` en cualquier
> parte, se detecta como Python igual. El marcador solo es indispensable en scripts Python que,
> por algún motivo, no hagan ese import (poco común).

```python
# lang: python
from broslmv import ctx

ids = ctx.get_selected_ids()
if not ids:
    ctx.msg("Selecciona documentos.")
else:
    # Crear entrada de almacén
    doc = ctx.erp.NuevoDocumento(202, 5)  # moduleId=202, depotId=5
    ctx.execute(f"UPDATE docDocument SET DepotIDFrom=DepotID, PaymentTermID=0 WHERE DocumentID={doc}")

    ctx.erp.AgregarArticulo(doc, 20, 10, -1, 100)  # producto, cantidad, precio, costo
    ctx.erp.RecalcCompleto(doc)
    ctx.erp.AffectStockNEW(doc)
    ctx.erp.Save(doc)

    ctx.erp.RefreshGrid()
    result = f"Entrada creada: doc={doc}"
```

### 9.3 Limitaciones conocidas (beta)

- **`ctx.msg` desde Python no muestra MessageBox** — escribe a log. Usar `print()` para debug.
- **`ctx.Confirm` / `ctx.form` / `ctx.show_html`** no disponibles en Python.
- **Sin transacciones** en Python (`ctx.OpenConn` no existe).
- **Encoding:** asegurar UTF-8 en datos con acentos/Ñ.

> ⚠️ **`ctx.nuevo("docDocument")` NO crea un documento válido.** No genera folio, anclas ni defaults.
> Para documentos usar siempre `ctx.erp.NuevoDocumento(...)`. `ctx.nuevo` solo para tablas simples.

---

## 10. Ventanas WinForms: modeless (no bloquear Comercial)

Un botón que abre una ventana (crear un documento, capturar datos, etc.) puede hacerse de dos
formas: **modal** (bloquea Comercial mientras está abierta) o **modeless** (se minimiza, se puede
seguir trabajando en Comercial, y se pueden tener varias ventanas de botones abiertas a la vez).
**Modeless es lo recomendado** para cualquier ventana que no sea un aviso rápido.

### 10.1 Diferencia entre C# y Python

| | C# (Roslyn) | Python (pythonnet) |
|---|---|---|
| ¿Dónde corre? | En el mismo proceso/hilo que Comercial | En su **propio proceso** (`python.exe`) |
| ¿Cómo se hace modeless? | **Tú decides**: `frm.Show()` en vez de `frm.ShowDialog()` | **Ya es automático** desde v2.19.0 — no hay que hacer nada |
| Si algo truena sin protección | Puede **tumbar Comercial completo** (mismo proceso) | Solo se cae esa ventana/proceso — **Comercial nunca corre riesgo** |
| ¿Necesita `try/catch` en los manejadores? | **Sí, importante** | Recomendado (mejor mensaje al usuario), no es cuestión de seguridad |

**Por qué la diferencia:** los scripts C# corren *en proceso*, en el mismo hilo que Comercial —
si usas `Show()` (modeless), tu script "ya terminó" antes de que el usuario haga clic en algo, así
que **ya nadie más atrapa una excepción** que ocurra después. Python corre *fuera de proceso*
(aislado); además, desde v2.19.0 el addon (`UiPump`, ver [`UI_VENTANAS.md`](UI_VENTANAS.md)) ya
no espera bloqueado el intercambio con el host, así que un botón Python nunca congela Comercial,
tenga o no ventana, y sin importar cuánto tarde el usuario en cerrarla.

### 10.2 Reglas para que no truene (C#)

1. **`frm.Show()`, nunca `frm.ShowDialog()`** al final del script.
2. **Sin `Owner`** (o `ShowInTaskbar = true`) → ventana independiente que se minimiza sola.
3. **`try/catch` en TODO manejador que haga SQL o `ctx.erp`** — no solo en el botón de guardar.
   Un `TextChanged`/`Click` que dispare una búsqueda también puede fallar (conexión, timeout).
4. No hace falta guardar la referencia a la ventana a mano: `Application.OpenForms` la mantiene
   viva mientras esté abierta.
5. Se pueden abrir **varias ventanas del mismo botón** a la vez — cada ejecución del script crea
   su propia ventana independiente, sin instancia única (a diferencia de la Consola).

**Diagnóstico si un botón Python "se cuelga" o Comercial muestra el diálogo nativo "the other
application is busy" (título "XEngine")** (v2.21.2): cada llamada `ctx.erp.*` / `ctx.query` /
`ctx.scalar` / `ctx.execute` de un script Python queda registrada en
`C:\BrosLMV\logs\PythonErp_AAAAMMDD.txt`, con una línea "INICIA" **antes** de la llamada
bloqueante y otra "termina en … ms" después. Si el botón se atora, la línea "INICIA" sin su
"termina" correspondiente en ese archivo es la llamada que quedó pendiente — es la pista clave
para diagnosticar la causa raíz (ese log no depende de SQL ni de COM, así que se escribe aunque
la llamada se cuelgue).

### 10.3 Plantillas base (arrancar un script nuevo)

Para no reinventar esto cada vez, hay una plantilla **base mínima** por lenguaje — solo el
esqueleto modeless + las protecciones, sin lógica de negocio — pensada para copiar y pegar como
punto de partida de cualquier ventana nueva:

- **`PLANTILLA_BASE_CSHARP_WINFORMS.ctx`** — ventana modeless en blanco, con un botón de ejemplo
  ya envuelto en `try/catch` (patrón `WireTool`).
- **`PLANTILLA_BASE_PYTHON_WINFORMS.py`** — lo mismo en Python (bootstrap de `pythonnet`, helpers
  `msg()`/`confirmar()` locales, un botón de ejemplo con `try/except`).

Ambas están en **Plantillas** dentro de la consola. Para un ejemplo completo y funcional (con
búsqueda de proveedor/producto, grid de partidas, creación de documento), hay dos pares
C#/Python, mismas reglas, aplicadas a casos reales distintos:

> **Guarda de nuevo si guardaste antes de v2.21.9.** Cargar una plantilla (menú Plantillas) o
> importar un archivo con "Abrir" leía el texto sin especificar codificación; en .NET Framework,
> sin BOM en el archivo, eso puede caer al codepage ANSI del sistema y convertir acentos/emoji a
> "?" al guardarlo en `zzBrosScript` (se vio con un botón guardado como "Informaci?n" en vez de
> "Información", e iconos del ribbon como "?" en vez de "➕"/"❌"). Ya está corregido (UTF-8
> explícito), pero un `AppKey` guardado ANTES de v2.21.9 desde una plantilla puede seguir dañado
> — hay que reabrir esa plantilla y volver a guardarla.

| Plantilla | Módulo | Qué enseña de más |
|-----------|--------|--------------------|
| **"Ejemplo Premium · C#/Python WinForms"** | 1040 (Solicitud/Requisición) | Caso base: proveedor, almacén, partidas (solo cantidad, sin precio) |
| **"Ejemplo Premium · C#/Python Orden de Compra"** | 183 (Orden de Compra) | Partidas **con precio unitario** (compromiso real con el proveedor), **impuesto** (precargado del catálogo, editable) y **descuento %** por partida, **fecha de entrega esperada**, apartado de **Totales** (Subtotal/Descuento/Impuestos/Total + Total en letra) y **detalle de producto** (doble clic en una partida) + `UpdateStatusDelivery` |
| **"Ejemplo Premium · C# Recepción de Compra"** | 184 (Recepción) | **Documento DERIVADO**: N Órdenes de Compra del mismo proveedor → 1 Recepción, con partidas **consolidadas por producto** y **lote (+ caducidad) / número de serie** por partida. SÍ afecta inventario. |
| **"Ejemplo Premium · C# Factura de Compra"** | 152 (Factura) | **Documento DERIVADO** desde 1+ OC ya seleccionadas en el grid nativo (`ctx.GetSelectedIds()`), **impuesto editable por partida**, columnas Importe/Impuesto $/Total. No afecta inventario, SÍ genera póliza contable. |

Solo la Recepción de Compra (184) afecta inventario. Orden de Compra y Factura de Compra no.

### 10.4 Documentos DERIVADOS: N Órdenes de Compra → 1 documento (Recepción / Factura)

Recepción de Compra y Factura de Compra comparten un patrón: sus partidas no se capturan de
cero, vienen de lo **pendiente** de una o varias Órdenes de Compra del mismo proveedor. Dos
cosas importantes que aprender de esto para cualquier documento derivado nuevo:

1. **Cada tipo de documento derivado usa su PROPIA columna de vínculo por partida** — no hay una
   sola convención universal:
   - Recepción de Compra → `docDocumentItem.DeliverDocumentItemID` (apunta a la partida de la OC).
   - Factura de Compra → `docDocumentItem.SourceDocumentItemID` (columna distinta, mismo propósito).
   - Ninguna vista nativa (`vwLBSProductsToDeliver` y similares) soporta bien que **varias** OC
     alimenten un solo documento — solo llevan la cuenta de una OC por documento derivado (vía
     `docDocument.SourceDocumentID`, un solo valor por encabezado). Por eso ambas plantillas
     calculan "cuánto queda pendiente" con SQL propio (self-join por la columna de vínculo de
     cada partida), no con las vistas nativas — así si mezclas 2+ OC en un mismo documento, el
     pendiente de CADA una sigue siendo correcto.
   - `docDocument.SourceDocumentID` sí se rellena (con la primera OC incluida) por compatibilidad
     con reportes nativos que lo esperan, pero es solo informativo — el cálculo real no depende
     de él.
2. **`PaymentAgenda` puede quedar mal si se modifica el documento por SQL después de crearlo.**
   `NuevoDocumento` crea un `PaymentAgenda` placeholder (`Amount=0`); si luego cambias
   `PaymentTermID` por SQL directo (como hacen ambas plantillas, para fijar la condición de pago
   real), `Save()` NO lo corrige — regenera desde el caché interno de XEngine, que todavía tiene
   los valores viejos. La Factura de Compra regenera la agenda a mano después de `Save()`, leyendo
   los porcentajes/plazos reales de `engPaymentTermDetail`. Si tu documento cambia `PaymentTermID`
   o `Total` por SQL después de `NuevoDocumento`, revisa si también necesitas este paso.
3. Antes de escribir un documento derivado nuevo, **verifica el perfil real de encabezado contra
   una base de datos de pruebas** (crea el documento equivalente a mano en Comercial y compara
   los valores que quedan en `docDocument`/`docDocumentItem`) en vez de asumirlo por analogía con
   otro documento. Recepción de Compra y Factura de Compra, por ejemplo, difieren en varios campos
   del encabezado (`StatusDeliveryID`, `DepotIDFrom`, etc.) pese a parecerse mucho en el flujo.

**Totales y total en letra (v2.21.0).** El desglose se calcula partida por partida, no lo
recalcula CONTPAQi al vuelo: por cada partida `neto = importe − importe×descuento%` y
`impuesto = neto×TaxPerc` (el `TaxPerc` real de `vwLBSTaxPerc`, el mismo que ya guarda
`AgregarArticulo` — ver §6.2); el Total en letra usa un conversor número→letras en español
incluido en la propia plantilla (sin dependencias externas) y se recalcula si cambia la moneda
elegida. Es un cálculo **informativo en la ventana**; el total real y definitivo del documento lo
sigue fijando `RecalcCompleto` al guardar.

**Detalle de producto (v2.21.0).** Doble clic en cualquier fila de la tabla de partidas abre una
ventana de solo lectura (hija de la ventana de la Orden de Compra, no bloquea Comercial) con:
datos generales (`orgProduct`: clave, nombre, descripción, unidades, costo, precio de lista, %
de impuesto), clasificaciones (`Category1`-`Category4` del catálogo), existencia por almacén
(`orgProductKardex`), listas de precios asignadas (`orgProductPriceList` + `orgPriceList`) y
precios negociados por proveedor (`orgProductSupplier`). Útil como plantilla para agregar un
"ver detalle" similar en cualquier otro script que liste productos.

---

## 11. Ejemplos de scripts

### Sumar el Total de lo seleccionado — `SUMA.ctx`

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("No hay documentos seleccionados."); return; }

var total = ctx.Scalar(
    "SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ids) + ")");

ctx.Msg("Documentos: " + ids.Count + "\nSuma Total: $" + total, "Resultado");
```

### Listar proveedor y folio — `PROVEEDORES.ctx`

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("No hay documentos seleccionados."); return; }

var filas = ctx.Query(
    "SELECT d.Folio, be.OfficialName AS Proveedor, d.Total " +
    "FROM docDocument d " +
    "LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID " +
    "WHERE d.DocumentID IN (" + ctx.JoinIds(ids) + ")");

var sb = new System.Text.StringBuilder();
foreach (var f in filas)
    sb.AppendLine(f["Folio"] + "  |  " + f["Proveedor"] + "  |  $" + f["Total"]);

ctx.Msg(sb.ToString(), filas.Count + " documento(s)");
```

### Actualizar un campo con confirmación — `MARCAR.ctx`

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("No hay documentos seleccionados."); return; }
if (!ctx.Confirm("¿Marcar " + ids.Count + " documento(s)?")) return;

int n = ctx.NonQuery(
    "UPDATE docDocument SET UserID = " + ctx.UserID + " WHERE DocumentID IN (" + ctx.JoinIds(ids) + ")");

ctx.Log("MARCAR: " + n + " filas (usuario " + ctx.UserID + ")");
ctx.Msg("Documentos actualizados: " + n);
```

### Validar crédito antes de facturar — `CREDITO.ctx`

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona pedidos."); return; }

foreach (long id in ids)
{
    var doc = ctx.Query("SELECT BusinessEntityID, Total FROM docDocument WHERE DocumentID=" + id);
    if (doc.Count == 0) continue;

    int beId = (int)(long)doc[0]["BusinessEntityID"];
    double total = Convert.ToDouble(doc[0]["Total"]);

    if (!ctx.erp.VerifyCreditLimit(beId, total))
        ctx.Msg("Cliente " + beId + " sin crédito para doc " + id + " ($" + total + ")", "Alerta");

    if (ctx.erp.VerifyCreditLimitOverdue(beId))
        ctx.Msg("Cliente " + beId + " tiene documentos vencidos", "Alerta");
}
ctx.Msg("Revisión completada.");
```

### Crear entrada de almacén desde OC — `RECIBIR.ctx`

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona una OC."); return; }

long ocId = ids[0];
var oc = ctx.Query("SELECT * FROM docDocument WHERE DocumentID=" + ocId)[0];
int depotId = (int)(long)oc["DepotID"];
int beId = (int)(long)oc["BusinessEntityID"];

// Crear recepción de compra (ModuleID=184)
int rc = ctx.erp.NuevoDocumento(184, depotId, beId);
ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID={(int)(long)oc["PaymentTermID"]},
        SourceDocumentID={ocId}, DateDelivery=GETDATE()
    WHERE DocumentID={rc}");

// Copiar partidas de la OC
var partidas = ctx.Query(
    "SELECT ProductID, Quantity, UnitPrice, CostPrice FROM docDocumentItem " +
    "WHERE DocumentID=" + ocId + " AND DeletedOn IS NULL");

foreach (var p in partidas)
{
    int prodId = (int)(long)p["ProductID"];
    double qty = Convert.ToDouble(p["Quantity"]);
    double cost = Convert.ToDouble(p["CostPrice"] is DBNull ? 0 : p["CostPrice"]);
    ctx.erp.AgregarArticulo(rc, prodId, qty, -1, cost);
}

ctx.erp.RecalcCompleto(rc);
ctx.erp.AffectStockNEW(rc);
ctx.erp.Save(rc);
ctx.erp.RefreshGrid();
ctx.Msg("Recepción creada: doc=" + rc);
```

---

## 12. Advertencias y buenas prácticas

### ⚠️ `Delete` vs `CancelDocument`
- **`ctx.erp.Delete(doc)`** = soft-delete. Marca `DeletedOn` pero **NO revierte kardex ni costos**.
  El inventario queda inflado. Solo seguro para documentos sin afectación (solicitudes).
- **`ctx.erp.CancelDocument(doc)`** = cancelación. Marca `CancelledOn` y debería revertir kardex.
  **Usar siempre para documentos de inventario.**
- `ctx.erp.ReactivateDocument(doc)` = inverso de `CancelDocument`.

### ⚠️ No duplicar anclas
- `NuevoDocumento` ya crea las 4 anclas (`docDocumentExt`, `docDocumentExtra`, `docDocumentCFD`,
  `docDocumentPaymentAgenda`). **No volver a insertarlas** (causa PK duplicada).

### ⚠️ No duplicar campos de partida
- `AgregarArticulo` ya llena `ApplyGlobalDiscount=1`, `DeductiblePerc=1`, `IsBusinessOperation=1`,
  `MustBeDelivered=1`, `DateItem`, `CoefUnit=1`, `ClaveUnidad`, `ObjetoImpuesto`. **No repetir.**

### ⚠️ Folio
- `NuevoDocumento` resuelve el folio automáticamente. No usar `MAX(Folio)+1` manual.

### ⚠️ Transacciones
- Los builders del addon (`NuevoDocumento` + 4 anclas) ejecutan 5 INSERT sin transacción.
  Si el script es crítico, envolver en `ctx.OpenConn()` + transacción manual.

### ⚠️ SQL directo + XEngine
- No mezclar INSERT crudo de `docDocument` con `ctx.erp.AffectStockNEW` — el builder
  `NuevoDocumento` ya conoce los defaults correctos. Usar SQL directo solo para los
  ajustes de perfil por módulo (UPDATE de `PaymentTermID`, `DepotIDFrom`, etc.).

### Buenas prácticas
- Probar scripts en modo solo-lectura primero (`ctx.SoloLectura`).
- Usar `ctx.Confirm()` antes de operaciones destructivas.
- Loguear con `ctx.Log()` para tener trazabilidad.
- Para catálogos, usar `OUTPUT INSERTED.<PK>` y `ctx.Scalar` (no `SCOPE_IDENTITY()` suelto).
- `ctx.erp.RecalcCompleto()` va **antes** de `AffectStockNEW()`.
- `ctx.erp.AffectStockNEW()` va **antes** de `Save()`.
- Después de `Save()`, `RefreshGrid()` para ver el documento nuevo.

---

## 13. Cómo está programado por dentro

El núcleo está en C# (.NET Framework 4.8) y se compila a la DLL
`BrosLMVClsMain.dll`. Archivos fuente (en `src\`):

| Archivo | Qué contiene |
|---------|--------------|
| `ClsMain.cs` | El COM server `BrosLMV.clsMain` + el despachador (`ExecuteFunction`) + el resolutor de DLLs |
| `Scripting.cs` | El motor Roslyn (`ScriptRunner`), el contexto `ctx` (`ScriptContext`), `ErpContext` (`ctx.erp`) y la lectura del grid (`GridSelection`) |
| `Consola.cs` | La ventana de la consola (WinForms) |
| `Rutas.cs` | Las rutas fijas (`C:\BrosLMV\...`) y la lectura de la conexión |
| `Datos.cs` | Auditoría local (SQLite) |
| `HostClient.cs` | Cliente del pipe para ejecutar Python |

### El flujo de `ctx.erp`

```
Script C# → ctx.erp.NuevoDocumento(...)
  → ErpContext.NuevoDocumento (Scripting.cs:922)
    → GetModuleParameter (XEngine) → DocumentTypeID, DocRecipient
    → GetFolioPrefix + GetNextFolio (XEngine) → Folio
    → INSERT docDocument + 4 anclas (SQL directo)
    → return DocumentID

Script C# → ctx.erp.AgregarArticulo(...)
  → ErpContext.AgregarArticulo (Scripting.cs:974)
    → SELECT orgProduct → ProductKey, Description, Unit, TaxTypeID, claves SAT
    → INSERT docDocumentItem con flags + claves + costo opcional
    → return DocumentItemID
```

---

## 14. Recompilar el núcleo

**Solo** si modificas los archivos C# de `src\` (no para scripts `.ctx`).
Necesitas **.NET SDK**. La guía completa está en [`DESARROLLO.md`](DESARROLLO.md). Resumen:

```powershell
.\build\generar_instalador.ps1   # recompila la DLL → instalador\bin
.\build\generar_exes.ps1         # genera dist\BrosLMV-Instalador-X.Y.Z.exe
```

Datos fijos del componente:
- **ProgID:** `BrosLMV.clsMain`
- **CLSID:** `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`

---

## 15. Cheat sheet

```
──────────────────────────────────────────────────────────
 CREAR UN BOTÓN NUEVO
──────────────────────────────────────────────────────────
 1. Consola BrosLMV → escribir → Ejecutar (F5)
 2. Guardar como  C:\BrosLMV\scripts\NOMBRE.ctx
 3. SQL: plantilla_crear_boton.sql con @Execute='BrosLMV.NOMBRE'
 4. Reiniciar CONTPAQi
──────────────────────────────────────────────────────────
 CREAR UN DOCUMENTO (patrón canónico)
──────────────────────────────────────────────────────────
 int doc = ctx.erp.NuevoDocumento(moduleId, depotId, beId);
 // UPDATE perfil por módulo (PaymentTermID, DepotIDFrom...)
 ctx.erp.AgregarArticulo(doc, prodId, cantidad, precio, costo);
 // ... más partidas si es necesario ...
 // INSERT lotes/series si el producto los usa
 ctx.erp.RecalcCompleto(doc);
 ctx.erp.AffectStockNEW(doc);    // omitir si no afecta inventario
 ctx.erp.Save(doc);
 ctx.erp.RefreshGrid();
──────────────────────────────────────────────────────────
 MÓDULOS FRECUENTES
──────────────────────────────────────────────────────────
 202 = Entrada almacén      152 = Factura compra
 203 = Salida almacén       21  = Factura cliente
 204 = Traspaso              967 = Pedido
 183 = Orden de compra      157 = Remisión
 184 = Recepción compra     1040 = Solicitud compra
──────────────────────────────────────────────────────────
 DATOS FIJOS
──────────────────────────────────────────────────────────
 ProgID   = BrosLMV.clsMain
 CLSID    = {E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}
 DLLs     = C:\BrosLMV\bin
 scripts  = C:\BrosLMV\scripts\<AppKey>.ctx
 logs     = C:\BrosLMV\logs
──────────────────────────────────────────────────────────
```

> **Documentación relacionada:** [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) —
> contrato técnico detallado de `ctx.*` y `ctx.erp.*`.
> [`PYTHON.md`](PYTHON.md) — guía completa de Python.
> [`RECETAS_NOCODE.md`](RECETAS_NOCODE.md) — recetas adicionales.
> [`XENGINE_FUNCIONES.md`](XENGINE_FUNCIONES.md) — catálogo completo de funciones XEngine.
