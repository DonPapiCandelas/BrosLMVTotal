# Manual del SDK de BrosLMV

> Generado por `build/sdk/generar_referencia_sdk.py` desde `src/assets/sdk_catalogo.json` y `docs/SDK_GUIAS.md`. **No se edita a mano**: cambia el catálogo y regenera.
> 220 funciones, 49 con ficha completa. La versión navegable con buscador está en la Consola (Más opciones → Manual del SDK…).

## Guías

Estas guías explican **cómo se usan** las funciones del SDK con ejemplos que se pueden copiar. Cada función tiene además su ficha en el manual (firma, parámetros, ejemplos y notas).
Los ejemplos están en C# y Python; SQL puro tiene su propia guía (tokens). Regla de oro para todos: **filtra siempre por la empresa activa** y **no escribas a mano** IDs de módulos, almacenes o catálogos: léelos de la empresa.

## 1. Tu primer script

Un script es un archivo de texto que Comercial ejecuta desde un botón. Elige el lenguaje con el botón *Lenguaje* de la Consola (C#, Python o SQL).

**C#** (el predeterminado; no lleva marca):

```
var ids = ctx.GetSelectedIds();                        // lo que el usuario seleccionó en la lista
if (ids.Count == 0) { ctx.Msg("Selecciona un documento."); return; }
var total = ctx.Scalar("SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ids) + ")");
ctx.Msg("Total de la selección: " + total);
```

**Python** (la Consola escribe `# lang: python` por ti):

```
from broslmv import ctx

ids = ctx.get_selected_ids()
total = ctx.scalar("SELECT SUM(Total) FROM docDocument WHERE DocumentID IN ({})".format(",".join(map(str, ids))))
result = f"Total de la selección: {total}"   # lo que asignes a result se muestra al terminar
```

**SQL puro** (`-- lang: sql`): los tokens `{pID}` y `{pIDs}` se sustituyen solos.

```
SELECT SUM(Total) AS Total FROM docDocument WHERE DocumentID IN ({pIDs})
```

## 2. Leer y cambiar datos: Scalar, Query, NonQuery y OpenConn

| Necesito… | C# | Python |
|---|---|---|
| Un solo valor (conteo, total) | `ctx.Scalar(sql)` | `ctx.scalar(sql)` |
| Varias filas | `ctx.Query(sql)` | `ctx.query(sql)` |
| Insertar, actualizar o borrar | `ctx.NonQuery(sql)` | `ctx.execute(sql)` |
| Un lote de varios INSERT o una transacción | `ctx.OpenConn()` | (usa un script C# para el lote) |

**Scalar** devuelve la primera columna de la primera fila (o `null`). **Query** devuelve una lista de filas; cada fila es un diccionario `columna → valor`:

```
var filas = ctx.Query("SELECT TOP 5 Folio, Total FROM docDocument ORDER BY DocumentID DESC");
foreach (var f in filas) ctx.Log(f["Folio"] + " → " + f["Total"]);
```

```
filas = ctx.query("SELECT TOP 5 Folio, Total FROM docDocument WHERE ModuleID = @m", {"m": 152})
for f in filas:
    ctx.log(f"{f['Folio']} → {f['Total']}")
```

**Cuidados:**

- En C# no hay parámetros: escapa las comillas simples duplicándolas (`'` → `''`). En Python usa `@nombre` y un diccionario.
- `NonQuery`/`execute` se bloquean en *Modo solo lectura*. Un `UPDATE` o `DELETE` sin `WHERE` afecta toda la tabla.
- **Varios INSERT en un solo texto: usa `ctx.OpenConn()`**, no `Scalar`/`NonQuery`: por el puente COM se pierde el resultado y puede reintentar y duplicar.

```
using (var cn = ctx.OpenConn())
using (var tx = cn.BeginTransaction())
using (var cmd = cn.CreateCommand())
{
    cmd.Transaction = tx;
    cmd.CommandText = "SET NOCOUNT ON; INSERT INTO miTabla(Nombre) VALUES(@n); SELECT SCOPE_IDENTITY();";
    cmd.Parameters.AddWithValue("@n", "Ejemplo");
    var id = cmd.ExecuteScalar();
    tx.Commit();                       // o queda todo, o no queda nada
}
```

## 3. Crear un documento (paso a paso)

Este ejemplo crea una Factura de compra con una partida, la guarda, refresca la lista y la abre. Cada línea explica su porqué.

**C#:**

```
// 1) El proveedor, el módulo y el almacén salen de la empresa: no los escribas a mano si el script es para varios clientes.
int moduloId = 152, almacenId = 1, proveedorId = 162, productoId = 1;

int doc = ctx.erp.NuevoDocumento(moduloId, almacenId, proveedorId);   // encabezado con folio automático
ctx.erp.AgregarArticulo(doc, productoId, 2, 100);                     // producto, cantidad 2, precio 100
ctx.erp.RecalcCompleto(doc);                                          // totales e impuestos (sin esto la partida queda en 0)
ctx.erp.Save(doc);                                                    // guarda el documento
if (ctx.erp.LastError != null) { ctx.Msg(ctx.erp.LastError); return; }

ctx.erp.RefreshGrid();                                                // UNA vez, al final
ctx.erp.AbrirDocumento(doc, moduloId);                                // el usuario lo ve de inmediato
```

**Python:**

```
from broslmv import ctx

proveedores = [162, 170, 205]     # ejemplo
producto = 1
creados = []

for proveedor in proveedores:
    doc_id = ctx.erp.NuevoDocumento(152, 1, proveedor)   # Factura de compra, almacén 1
    ctx.erp.AgregarArticulo(doc_id, producto, 1, 100)    # producto, cantidad, precio
    ctx.erp.RecalcCompleto(doc_id)                       # totales e impuestos
    ctx.erp.Save(doc_id)                                 # guarda
    creados.append(doc_id)

ctx.erp.RefreshGrid()    # una vez, ya con todo creado (no dentro del ciclo)
result = f"Se crearon {len(creados)} documento(s): {creados}"
```

**Según lo que afecte el módulo** (se lee de `engModuleParameter`, no del tipo de documento):

- Si afecta **inventario**: `ctx.erp.AffectStockNEW(doc)`.
- Si afecta **costos**: `ctx.erp.CalcularCostos(doc)`.
- Si afecta **saldos** (cuentas por pagar/cobrar): `ctx.erp.UpdateDocumentPaidInfo(doc)`.
- Si maneja **entregas**: `ctx.erp.UpdateStatusDelivery(doc)`.

**La póliza no se crea sola** cuando el documento nace por script. Se genera con el motor nativo:

```
var acc = ctx.erp.CrearHelper("Accounting.clsMain");
acc.GetType().InvokeMember("CrearPolizasDocumento", System.Reflection.BindingFlags.InvokeMethod, null, acc, new object[] { (long)doc, false, "" });
```

## 4. Refrescar el grid (estándar)

Todo script que cambie datos que se ven en una lista termina con **una sola** llamada a `ctx.erp.RefreshGrid()`, **después del último cambio** (nunca dentro de un ciclo).
Refresca la lista y regresa a la misma fila que tenía el usuario, sin mandar la vista al principio. Es la misma llamada en C# y en Python. Requiere BrosLMV 2.94.0 o superior.

## 5. Tokens: {pID}, {pIDs} y compañía

Los tokens son marcas que se sustituyen por valores del contexto actual.

| Token | Valor |
|---|---|
| `{pID}` | Primer ID seleccionado en la lista (0 si no hay) |
| `{pIDs}` | Todos los IDs seleccionados, separados por coma |
| `{pUserID}` | Usuario activo |
| `{pModulo}` | Módulo activo |
| `{pEmpresa}` | Nombre de la base de datos activa |
| `{DATOS:Campo}` | Valor de ese campo en la primera fila seleccionada |

- En **SQL puro** se sustituyen solos.
- En **C#** usa `ctx.ResolverTokens("… {pIDs} …")` (o `ctx.EjecutarSql(...)` para correr SQL con tokens y recibir texto).
- En **Python** no hay sustitución de texto: usa `ctx.get_selected_ids()`, `ctx.user_id`, `ctx.module_id`, `ctx.empresa` y `ctx.fila`.

## 6. Mostrar ventanas al usuario

| Quiero… | C# | Python |
|---|---|---|
| Un aviso | `ctx.Msg("Listo")` | `ctx.msg("Listo")` |
| Preguntar Sí/No | `if (ctx.Confirm("¿Seguro?")) {…}` | `if ctx.confirm("¿Seguro?"): …` |
| Escribir en la bitácora | `ctx.Log("texto")` | `ctx.log("texto")` |
| Una página HTML (reporte, tablero) | `ctx.ShowHtml(html, "Título")` | `ctx.show_html(html, "Título")` |
| Un formulario que devuelva datos | `ctx.ShowHtmlFormulario(html)` | `ctx.form({…})` |

Para elegir archivos en Python: `ctx.select_file(...)` y `ctx.select_folder(...)`; para Excel: `ctx.read_excel(...)` y `ctx.write_excel(...)`.

## 7. Reglas de BrosLMV para scripts que van a más de un cliente

1. **Empresa activa:** las tablas con `OwnedBusinessEntityID` (almacenes, proveedores, clientes, centros de costo, tipos de gasto, documentos, pólizas) guardan una copia por empresa: filtra siempre por `ctx.erp.OwnedBusinessEntityId`.
2. **Nada escrito a mano:** los módulos se identifican por su naturaleza en `engModuleParameter` (tipo de documento, destinatario, si recibe XML, qué afecta), no por un `ModuleID` fijo; catálogos y valores fiscales se leen de la empresa.
3. **Lotes de altas por `ctx.OpenConn()`**, con `SET NOCOUNT ON` y SQL que no duplique si se repite.
4. **Refrescar el grid una vez al final** con `ctx.erp.RefreshGrid()` y, si creaste un documento, abrirlo con `ctx.erp.AbrirDocumento(...)`.
5. **Confirma lo irreversible** (timbrar, cancelar, borrar) con `ctx.Confirm(...)`.


## Referencia de C#

#### Selección y datos

### `GetSelectedIds` (C#)

```
ctx.GetSelectedIds() : List<long>
```

IDs de los documentos seleccionados en la vista.

Devuelve los DocumentID (o el ID de la fila) de lo que el usuario tiene seleccionado en la LISTA de Comercial. Es lo primero que casi todo botón hace.

**Devuelve:** List<long> con los IDs; vacía si no hay selección.

**Ojo**
- Lee la selección de la lista de fondo: dentro de un documento abierto no sirve; usa ctx.ObtenerIdDeVentanaActiva().
- Los IDs sirven directo en un IN de SQL con ctx.JoinIds(ids).

Ejemplo:

```
var ids = ctx.GetSelectedIds();
```

Avisar si no hay selección (C#):

```
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona al menos un documento."); return; }
```


### `GetFilaActiva` (C#)

```
ctx.GetFilaActiva() : Dictionary<string,object>
```

Campos de la primera fila seleccionada del grid.

Devuelve las columnas de la primera fila seleccionada, tal como las muestra la lista (por su nombre de columna).

**Devuelve:** Dictionary<string,object> con el nombre de cada columna como clave.

**Ojo**
- Los nombres son los de la vista de la lista; no siempre coinciden con columnas reales de la tabla. Para un dato exacto, consulta SQL con el ID.

Ejemplo:

```
var fila = ctx.GetFilaActiva();
var folio = fila["Folio"];
```

Leer folio y total (C#):

```
var fila = ctx.GetFilaActiva();
ctx.Msg("Folio " + fila["Folio"] + " por " + fila["Total"]);
```


### `JoinIds` (C#)

```
ctx.JoinIds(ids) : string
```

Convierte una lista de IDs en "1,2,3" (para un IN).

Convierte una lista de IDs en el texto "1,2,3" para usarlo dentro de un IN de SQL.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `ids` | IEnumerable<long> | Los IDs a unir. |

**Devuelve:** string, por ejemplo "12,15,20".

Ejemplo:

```
var lista = ctx.JoinIds(ctx.GetSelectedIds());
```

Sumar lo seleccionado (C#):

```
var total = ctx.Scalar("SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");
```


#### Consultas SQL

### `Scalar` (C#)

```
ctx.Scalar(sql) : object
```

Ejecuta SQL y devuelve un solo valor.

Ejecuta una consulta y devuelve solo la primera columna de la primera fila. Ideal para totales, conteos y buscar un dato.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | string | La consulta. Escapa las comillas simples duplicándolas (''). |

**Devuelve:** object (null si no hay filas). Conviértelo: Convert.ToDecimal(x), Convert.ToInt32(x).

**Ojo**
- Usa la conexión viva de Comercial; si esa conexión falla, cae sola a una conexión propia.
- Para varios INSERT en un solo texto NO uses Scalar/NonQuery: usa ctx.OpenConn() (a veces se reintenta y duplica).
- Filtra siempre por la empresa activa cuando la tabla tiene OwnedBusinessEntityID (ctx.erp.OwnedBusinessEntityId).

Ejemplo:

```
var total = ctx.Scalar("SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");
```

Cuántos documentos hay (C#):

```
int n = Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM docDocument"));
ctx.Msg("Documentos: " + n);
```

Lo mismo en Python (Python):

```
n = ctx.scalar("SELECT COUNT(*) FROM docDocument")
ctx.msg(f"Documentos: {n}")
```


### `Query` (C#)

```
ctx.Query(sql) : List<Dictionary<string,object>>
```

Ejecuta SQL y devuelve filas.

Ejecuta una consulta y devuelve todas las filas. Cada fila es un diccionario: columna → valor.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | string | La consulta SELECT. |

**Devuelve:** List<Dictionary<string,object>>; vacía si no hay filas. Los nombres de columna no distinguen mayúsculas.

**Ojo**
- Trae solo lo que necesitas (TOP, columnas concretas): todo se carga en memoria.

Ejemplo:

```
var filas = ctx.Query("SELECT Folio, Total FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");
```

Recorrer filas (C#):

```
var filas = ctx.Query("SELECT TOP 5 Folio, Total FROM docDocument ORDER BY DocumentID DESC");
foreach (var f in filas)
    ctx.Log(f["Folio"] + " → " + f["Total"]);
```

Lo mismo en Python (con parámetros) (Python):

```
filas = ctx.query("SELECT TOP 5 Folio, Total FROM docDocument WHERE ModuleID = @m", {"m": 152})
for f in filas:
    ctx.log(f"{f['Folio']} → {f['Total']}")
```


### `NonQuery` (C#)

```
ctx.NonQuery(sql) : int
```

INSERT/UPDATE/DELETE. Devuelve filas afectadas (respeta modo solo lectura).

Ejecuta INSERT, UPDATE o DELETE y devuelve cuántas filas cambió.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | string | La sentencia de modificación. |

**Devuelve:** int: filas afectadas.

**Ojo**
- Se bloquea en «Modo solo lectura» (lanza una excepción).
- Para lotes de varios INSERT usa ctx.OpenConn(): por el puente COM se pierde el resultado y puede duplicar.
- Un UPDATE/DELETE sin WHERE afecta toda la tabla: revísalo dos veces.

Ejemplo:

```
int n = ctx.NonQuery("UPDATE docDocument SET Referencia='X' WHERE DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");
```

Actualizar una referencia (C#):

```
int n = ctx.NonQuery("UPDATE docDocument SET Referencia = 'Revisado' WHERE DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");
ctx.Msg("Actualizados: " + n);
```


### `EjecutarSql` (C#)

```
ctx.EjecutarSql(sql) : string
```

Corre T-SQL crudo (resuelve tokens) y devuelve texto: filas o un OK.

Corre T-SQL tal cual, resolviendo los tokens ({pID}, {pIDs}…), y regresa el resultado como texto.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sqlCrudo` | string | SQL con tokens opcionales. |

**Devuelve:** string: las filas si es SELECT, o un OK si es una modificación.

Ejemplo:

```
var txt = ctx.EjecutarSql("SELECT Folio, Total FROM docDocument WHERE DocumentID IN ({pIDs})");
ctx.Msg(txt);
```


#### Tokens

### `ResolverTokens` (C#)

```
ctx.ResolverTokens(plantilla) : string
```

Sustituye {pID}, {pIDs}, {pUserID}, {pModulo}, {pEmpresa}, {DATOS:Campo}.

Sustituye los tokens de una plantilla de texto por los valores del contexto actual.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `plantilla` | string | Texto con tokens. |

**Devuelve:** string con los valores ya puestos.

| Token | Valor |
|---|---|
| `{pID}` | Primer ID seleccionado en la lista (0 si no hay) |
| `{pIDs}` | Todos los IDs seleccionados separados por coma |
| `{pUserID}` | Usuario activo |
| `{pModulo}` | Módulo activo |
| `{pEmpresa}` | Nombre de la base de datos activa |
| `{DATOS:Campo}` | Valor de ese campo en la primera fila seleccionada |

**Ojo**
- En scripts SQL puro los tokens se resuelven solos; en C# y Python usa esta función o las equivalentes (ctx.GetSelectedIds(), ctx.UserIDReal()…).

Ejemplo:

```
var sql = ctx.ResolverTokens("SELECT * FROM docDocument WHERE DocumentID = {pID}");
```

SQL con selección (C#):

```
var sql = ctx.ResolverTokens("SELECT Folio, Total FROM docDocument WHERE DocumentID IN ({pIDs})");
var filas = ctx.Query(sql);
```


#### Contexto

### `Empresa` (C#)

```
ctx.Empresa() : string
```

Nombre de la base de datos de la empresa activa.

Nombre de la base de datos de la empresa activa (por ejemplo MI_EMPRESA).

**Devuelve:** string.

**Ojo**
- Para filtrar por la empresa dentro de las tablas que guardan una copia por empresa usa ctx.erp.OwnedBusinessEntityId (OwnedBusinessEntityID).

Ejemplo:

```
var bd = ctx.Empresa();
```


### `ServidorActivo` (C#)

```
ctx.ServidorActivo() : string
```

Servidor\instancia de SQL de la empresa activa.

Ejemplo:

```
var srv = ctx.ServidorActivo();
```


### `ModuloActivo` (C#)

```
ctx.ModuloActivo() : int
```

ID del módulo activo de CONTPAQi.

Ejemplo:

```
var mod = ctx.ModuloActivo();
```


### `UserID` (C#)

```
ctx.UserID : int
```

ID de usuario del addon (suele venir 0; usa ctx.erp.UserId).

Ejemplo:

```
var u = ctx.UserID;
```


### `SoloLectura` (C#)

```
ctx.SoloLectura : bool
```

Si es true, NonQuery se bloquea (modo solo lectura).

Ejemplo:

```
ctx.SoloLectura = true;
```


### `FilasAfectadas` (C#)

```
ctx.FilasAfectadas : int
```

Acumulado de filas modificadas por NonQuery (auditoría).

Ejemplo:

```
var n = ctx.FilasAfectadas;
```


### `DiagConexion` (C#)

```
ctx.DiagConexion() : string
```

Diagnóstico de la conexión activa.

Ejemplo:

```
ctx.Msg(ctx.DiagConexion());
```


### `XEngineLib` (C#)

```
ctx.XEngineLib : object
```

Objeto XEngine crudo (escape hatch a COM).

Ejemplo:

```
var xe = ctx.XEngineLib;
```


#### Interacción

### `Msg` (C#)

```
ctx.Msg(texto, titulo?) : void
```

Muestra un mensaje al usuario.

Muestra un mensaje al usuario y espera a que lo cierre.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `texto` | string | Lo que se muestra. |
| `titulo` | string | Título de la ventana (opcional). |

**Devuelve:** nada.

**Ojo**
- En ejecución sin pantalla (Runner) no abre nada: revisa ctx.Headless.

Ejemplo:

```
ctx.Msg("Hola", "Aviso");
```


### `Confirm` (C#)

```
ctx.Confirm(texto, titulo?) : bool
```

Pregunta Sí/No.

Pregunta Sí/No al usuario.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `texto` | string | La pregunta. |
| `titulo` | string | Título (opcional). |

**Devuelve:** true si eligió Sí.

Ejemplo:

```
if (ctx.Confirm("¿Seguro?")) { /* ... */ }
```

Confirmar antes de borrar (C#):

```
if (!ctx.Confirm("¿Borrar los documentos seleccionados?")) return;
```


### `Log` (C#)

```
ctx.Log(texto) : void
```

Escribe a la bitácora en C:\BrosLMV\logs.

Escribe una línea en la bitácora diaria de BrosLMV (C:\BrosLMV\logs). Útil para depurar sin molestar al usuario.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `texto` | string | La línea. |

**Devuelve:** nada.

Ejemplo:

```
ctx.Log("Proceso terminado");
```


#### ERP · Contexto

### `erp.UserId` (C#)

```
ctx.erp.UserId : int
```

ID real del usuario de CONTPAQi.

Ejemplo:

```
var u = ctx.erp.UserId;
```


### `erp.UserName` (C#)

```
ctx.erp.UserName : string
```

Nombre del usuario activo.

Ejemplo:

```
var n = ctx.erp.UserName;
```


### `erp.OwnedBusinessEntityId` (C#)

```
ctx.erp.OwnedBusinessEntityId : int
```

ID de la empresa propia (orgBusinessEntity IsOwned=1).

ID de la empresa propia (orgBusinessEntity con IsOwned=1). Es el valor de OwnedBusinessEntityID para filtrar tablas que guardan una copia por empresa.

**Devuelve:** int.

**Ojo**
- Filtra SIEMPRE por este valor en almacenes, proveedores, clientes, centros de costo, tipos de gasto, documentos y pólizas: si no, verás datos duplicados de otras empresas.

Ejemplo:

```
var e = ctx.erp.OwnedBusinessEntityId;
```


### `erp.ActiveModuleId` (C#)

```
ctx.erp.ActiveModuleId : int
```

Módulo activo (equivale a ctx.ModuloActivo()).

Ejemplo:

```
var m = ctx.erp.ActiveModuleId;
```


### `erp.CurrencyId` (C#)

```
ctx.erp.CurrencyId : int
```

Moneda activa.

Ejemplo:

```
var c = ctx.erp.CurrencyId;
```


### `erp.ComercialRFC` (C#)

```
ctx.erp.ComercialRFC : string
```

RFC de la empresa.

Ejemplo:

```
var rfc = ctx.erp.ComercialRFC;
```


### `erp.SoftwareVersion` (C#)

```
ctx.erp.SoftwareVersion : string
```

Versión de CONTPAQi.

Ejemplo:

```
var v = ctx.erp.SoftwareVersion;
```


#### ERP · Documento

### `erp.RecalcCompleto` (C#)

```
ctx.erp.RecalcCompleto(documentId) : void
```

Recalcula totales + costos + saldo pagado.

Recalcula totales, impuestos, costos y saldo pagado del documento. Llámalo después de crear o cambiar partidas.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |

**Devuelve:** nada.

Ejemplo:

```
var id = (int)ctx.GetSelectedIds()[0];
ctx.erp.RecalcCompleto(id);
```


### `erp.RecalcDocument` (C#)

```
ctx.erp.RecalcDocument(documentId) : void
```

Recalcula totales (subtotal, IVA, total).

Ejemplo:

```
ctx.erp.RecalcDocument(id);
```


### `erp.CalcularCostos` (C#)

```
ctx.erp.CalcularCostos(documentId) : void
```

Actualiza costos (promedio, PEPS...).

Actualiza los costos del documento (promedio, PEPS…). Se usa junto con AffectStockNEW cuando el módulo afecta costos.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |

**Devuelve:** nada.

Ejemplo:

```
ctx.erp.CalcularCostos(id);
```


### `erp.AffectStockNEW` (C#)

```
ctx.erp.AffectStockNEW(documentId) : void
```

Afecta inventario (kardex) — módulos nuevos.

Afecta el inventario (kardex) del documento con el motor nuevo. Úsalo solo si el módulo afecta inventario.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |

**Devuelve:** nada.

**Ojo**
- Decide según los parámetros del módulo (engModuleParameter: StockAffectation), no por el tipo de documento ni por un módulo escrito a mano.
- Ya se conoce y funciona en Recepción, Factura y Gasto.

Ejemplo:

```
ctx.erp.AffectStockNEW(id);
```


### `erp.AffectStock` (C#)

```
ctx.erp.AffectStock(documentId) : void
```

Afecta inventario (versión clásica).

Ejemplo:

```
ctx.erp.AffectStock(id);
```


### `erp.UpdateStatusDelivery` (C#)

```
ctx.erp.UpdateStatusDelivery(documentId) : void
```

Actualiza el estatus de entrega del grid.

Ejemplo:

```
ctx.erp.UpdateStatusDelivery(id);
```


### `erp.UpdateDocumentPaidInfo` (C#)

```
ctx.erp.UpdateDocumentPaidInfo(documentId) : void
```

Recalcula saldo pagado y balance.

Recalcula el saldo pagado y el balance del documento. Úsalo si el módulo afecta saldos (cuentas por pagar/cobrar).

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |

**Devuelve:** nada.

Ejemplo:

```
ctx.erp.UpdateDocumentPaidInfo(id);
```


### `erp.ActualizarParcialidad` (C#)

```
ctx.erp.ActualizarParcialidad(documentId) : void
```

Actualiza parcialidad en complementos de pago SAT.

Ejemplo:

```
ctx.erp.ActualizarParcialidad(id);
```


### `erp.CancelDocument` (C#)

```
ctx.erp.CancelDocument(documentId) : void
```

Cancela el documento.

Ejemplo:

```
ctx.erp.CancelDocument(id);
```


### `erp.ReactivateDocument` (C#)

```
ctx.erp.ReactivateDocument(documentId) : void
```

Reactiva un documento cancelado.

Ejemplo:

```
ctx.erp.ReactivateDocument(id);
```


### `erp.Save` (C#)

```
ctx.erp.Save(documentId) : void
```

Guarda el documento (XEngine).

Guarda el documento con el motor de Comercial.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |

**Devuelve:** nada.

**Ojo**
- Revisa ctx.erp.LastError después de guardar.

Ejemplo:

```
ctx.erp.Save(id);
```


### `erp.Delete` (C#)

```
ctx.erp.Delete(documentId) : void
```

Elimina el documento (XEngine).

Ejemplo:

```
ctx.erp.Delete(id);
```


#### ERP · Cobros y pagos

### `erp.AjustarSaldosInsolutos` (C#)

```
ctx.erp.AjustarSaldosInsolutos(financialOperationId, paymentWithDocumentId = 0) : void
```

Recalcula saldo anterior/insoluto (complemento de pago) de un cobro.

Recalcula SaldoAnterior y SaldoInsoluto (complemento de pago) de los renglones de UN cobro con Payment.clsMain, sin tocar impuestos. El argumento es la OPERACIÓN FINANCIERA (cobro/pago), no el documento.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `financialOperationId` | int | La operación financiera (FinancialOperationID). |
| `paymentWithDocumentId` | int | 0 para un cobro normal; el DocumentID de la nota de crédito si el 'pago' es una NC aplicada. |

**Devuelve:** nada.

**Ojo**
- CORREGIDA en 2.99.0: antes recibía un documentId, llamaba al motor con un solo parámetro y fallaba siempre en silencio (DISP_E_PARAMNOTOPTIONAL).
- Pruebas (12 cobros sin timbrar): restauró los saldos en 12 de 12. No cambia los renglones ya timbrados en un REP (AltID > 0).

Ejemplo:

```
ctx.erp.AjustarSaldosInsolutos(operacionId);
```


#### ERP · Documento

### `erp.RefreshDocumento` (C#)

```
ctx.erp.RefreshDocumento(documentId) : void
```

Refresca visualmente un documento abierto.

Ejemplo:

```
ctx.erp.RefreshDocumento(id);
```


### `erp.NuevoDocumento` (C#)

```
ctx.erp.NuevoDocumento(moduleId, depotId, businessEntityId?, rate?, paymentTermId?, currencyId?, title?, sourceDocumentId?) : int
```

Crea el encabezado de un documento con los defaults del módulo (folio, tipo, moneda) y devuelve el DocumentID.

Crea el encabezado de un documento nuevo (folio automático) y devuelve su DocumentID. Después se le agregan partidas con AgregarArticulo.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `moduleId` | int | Módulo del documento (por ejemplo Facturas de compra). |
| `depotId` | int | Almacén. |
| `businessEntityId` | int | Cliente o proveedor (0 = ninguno). |
| `rate` | double | Tipo de cambio (1 por omisión). |
| `paymentTermId` | int | Condición de pago. |
| `currencyId` | int | Moneda. |
| `title` | string | Título opcional. |
| `sourceDocumentId` | int | Documento de origen (0 = ninguno). |

**Devuelve:** int: DocumentID del nuevo documento.

**Ojo**
- El documento existe solo dentro de esta ejecución hasta que lo guardas y recalculas; para uno que ya existe usa AgregarRenglonExistente.
- Un documento creado por script NO genera póliza solo: ver la guía «Crear un documento».
- Toma el módulo y el almacén de la empresa (no los escribas a mano en scripts para varios clientes).

Ejemplo:

```
int id = ctx.erp.NuevoDocumento(183, 1, 162); // OC, almacén, proveedor
```

Factura de compra con una partida (C#):

```
int doc = ctx.erp.NuevoDocumento(152, 1, 162);      // módulo, almacén, proveedor
ctx.erp.AgregarArticulo(doc, 1, 2, 100);            // producto 1, cantidad 2, precio 100
ctx.erp.RecalcCompleto(doc);                        // totales e impuestos
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
```

Lo mismo en Python (Python):

```
doc = ctx.erp.NuevoDocumento(152, 1, 162)
ctx.erp.AgregarArticulo(doc, 1, 2, 100)
ctx.erp.RecalcCompleto(doc)
ctx.erp.Save(doc)
ctx.erp.RefreshGrid()
```


### `erp.AgregarArticulo` (C#)

```
ctx.erp.AgregarArticulo(documentId, productId, cantidad?, precioUnitario?, costo?, taxTypeIdOverride?, descuentoPerc?, deliverDocumentItemId?, lote?, serialNumber?, comments?, sourceDocumentItemId?) : int
```

Agrega una partida (lee datos de orgProduct). Tras agregar, llamar RecalcCompleto.

Agrega una partida (producto) a un documento recién creado; toma los datos del producto de orgProduct.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |
| `productId` | int | El producto. |
| `cantidad` | double | Cantidad (1). |
| `precioUnitario` | double | Precio (-1 = sin precio). |
| `costo` | double | Costo de entrada (-1 = no poner). |
| `taxTypeIdOverride` | int | Sustituye el impuesto del producto (-1 = el del producto). |
| `descuentoPerc` | double | Descuento como fracción (0.05 = 5 %). |
| `lote / serialNumber` | string | Solo si el producto usa lote o serie. |
| `comments` | string | Observaciones de la partida. |
| `sourceDocumentItemId` | int | Partida de origen. |

**Devuelve:** int: DocumentItemID de la partida.

**Ojo**
- Al terminar de agregar partidas llama a RecalcCompleto: sin eso la partida queda con total 0 y sin impuesto.
- Se crea en una transacción atómica.

Ejemplo:

```
ctx.erp.AgregarArticulo(id, 1, 3, 100);
ctx.erp.RecalcCompleto(id);
```


#### ERP · UI

### `erp.RefreshGrid` (C#)

```
ctx.erp.RefreshGrid() : void
```

Refresca el grid del módulo.

Refresca la lista de Comercial para que se vean los cambios, y regresa a la misma fila que tenía el usuario.

**Devuelve:** nada.

**Ojo**
- Llámalo UNA sola vez, al final del script, después del último cambio (no en cada documento).
- Desde la 2.94.0 conserva la fila y no manda la vista al principio; en versiones anteriores no refrescaba.
- En Python es la misma llamada: ctx.erp.RefreshGrid().

Ejemplo:

```
ctx.erp.RefreshGrid();
```

Crear varios y refrescar una vez (Python):

```
from broslmv import ctx

creados = []
for proveedor in [162, 170, 205]:
    doc_id = ctx.erp.NuevoDocumento(152, 1, proveedor)
    ctx.erp.AgregarArticulo(doc_id, 1, 1, 100)
    ctx.erp.RecalcCompleto(doc_id)
    ctx.erp.Save(doc_id)
    creados.append(doc_id)

ctx.erp.RefreshGrid()   # UNA vez, ya con todo creado
result = f"Se crearon {len(creados)} documento(s): {creados}"
```


### `erp.RefreshRibbon` (C#)

```
ctx.erp.RefreshRibbon() : void
```

Refresca el ribbon.

Vuelve a leer los botones del ribbon de Comercial.

**Devuelve:** nada.

**Ojo**
- Si un botón nuevo no aparece, cierra y vuelve a abrir Comercial.

Ejemplo:

```
ctx.erp.RefreshRibbon();
```


### `erp.GotoModuleID` (C#)

```
ctx.erp.GotoModuleID(moduleId) : void
```

Cambia al módulo indicado.

Ejemplo:

```
ctx.erp.GotoModuleID(183);
```


### `erp.OpenModule` (C#)

```
ctx.erp.OpenModule(moduleId) : void
```

Abre un módulo.

Ejemplo:

```
ctx.erp.OpenModule(183);
```


### `erp.OpenBrowser` (C#)

```
ctx.erp.OpenBrowser(url) : void
```

Abre una URL en el navegador.

Ejemplo:

```
ctx.erp.OpenBrowser("https://contpaqi.com");
```


### `erp.ShowMessage` (C#)

```
ctx.erp.ShowMessage(msg) : void
```

Mensaje nativo de CONTPAQi.

Ejemplo:

```
ctx.erp.ShowMessage("Listo");
```


#### ERP · Folio

### `erp.GetFolioPrefix` (C#)

```
ctx.erp.GetFolioPrefix(moduleId, depotId) : string
```

Prefijo (serie) configurado del módulo/almacén.

Ejemplo:

```
var serie = ctx.erp.GetFolioPrefix(183, 1);
```


### `erp.GetNextFolio` (C#)

```
ctx.erp.GetNextFolio(moduleId, prefix, depotId) : string
```

Siguiente folio disponible.

Ejemplo:

```
var folio = ctx.erp.GetNextFolio(183, "OC", 1);
```


#### ERP · Precios y existencias

### `erp.GetProductStock` (C#)

```
ctx.erp.GetProductStock(productId, depotId) : double
```

Existencia del producto en el almacén.

Ejemplo:

```
double ex = ctx.erp.GetProductStock(1, 1);
```


### `erp.GetSalePrice` (C#)

```
ctx.erp.GetSalePrice(productId, businessEntityId?) : double
```

Precio de venta (por cliente si se indica).

Ejemplo:

```
double p = ctx.erp.GetSalePrice(1);
```


### `erp.GetBusinessEntitySalePrice` (C#)

```
ctx.erp.GetBusinessEntitySalePrice(productId, businessEntityId) : double
```

Precio de venta específico de un cliente.

Ejemplo:

```
double p = ctx.erp.GetBusinessEntitySalePrice(1, 10);
```


### `erp.GetBuyPrice` (C#)

```
ctx.erp.GetBuyPrice(productId) : double
```

Precio de compra.

Ejemplo:

```
double p = ctx.erp.GetBuyPrice(1);
```


### `erp.GetCostPrice` (C#)

```
ctx.erp.GetCostPrice(productId) : double
```

Costo del producto.

Ejemplo:

```
double c = ctx.erp.GetCostPrice(1);
```


### `erp.GetPriceWithTaxes` (C#)

```
ctx.erp.GetPriceWithTaxes(price, taxTypeId) : double
```

Precio con impuestos incluidos.

Ejemplo:

```
double t = ctx.erp.GetPriceWithTaxes(100, 1);
```


### `erp.GetCurrencyRate` (C#)

```
ctx.erp.GetCurrencyRate(currencyId) : double
```

Tipo de cambio (respecto a MXN).

Ejemplo:

```
double tc = ctx.erp.GetCurrencyRate(2);
```


### `erp.GetCurrencyRateBanxico` (C#)

```
ctx.erp.GetCurrencyRateBanxico(currencyId) : double
```

Tipo de cambio de Banxico.

Ejemplo:

```
double tc = ctx.erp.GetCurrencyRateBanxico(2);
```


### `erp.GetCoefConversion` (C#)

```
ctx.erp.GetCoefConversion(productId, fromUnit, toUnit) : double
```

Coeficiente de conversión entre unidades.

Ejemplo:

```
double k = ctx.erp.GetCoefConversion(1, "PZA", "CAJA");
```


### `erp.ProductIsKit` (C#)

```
ctx.erp.ProductIsKit(productId) : bool
```

True si el producto es un kit.

Ejemplo:

```
if (ctx.erp.ProductIsKit(1)) { /* ... */ }
```


#### ERP · Crédito

### `erp.VerifyCreditLimit` (C#)

```
ctx.erp.VerifyCreditLimit(businessEntityId, amount) : bool
```

True si el importe entra en el límite de crédito.

Ejemplo:

```
if (!ctx.erp.VerifyCreditLimit(10, 5000)) ctx.Msg("Excede crédito");
```


### `erp.VerifyCreditLimitOverdue` (C#)

```
ctx.erp.VerifyCreditLimitOverdue(businessEntityId) : bool
```

True si la entidad tiene documentos vencidos.

Ejemplo:

```
if (ctx.erp.VerifyCreditLimitOverdue(10)) ctx.Msg("Tiene vencidos");
```


#### ERP · Parámetros

### `erp.GetModuleParameter` (C#)

```
ctx.erp.GetModuleParameter(moduleId, key) : string
```

Lee un parámetro del módulo.

Lee un parámetro de un módulo (engModuleParameter). Así se sabe cómo se comporta el módulo sin escribir su ID a mano.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `moduleId` | int | El módulo. |
| `key` | string | El nombre del parámetro. |

**Devuelve:** string (o null).

Ejemplo:

```
var v = ctx.erp.GetModuleParameter(183, "MiParam");
```


### `erp.SaveModuleParameter` (C#)

```
ctx.erp.SaveModuleParameter(moduleId, key, value) : void
```

Guarda un parámetro del módulo.

Ejemplo:

```
ctx.erp.SaveModuleParameter(183, "MiParam", "1");
```


### `erp.GetParameter` (C#)

```
ctx.erp.GetParameter(key) : string
```

Lee un parámetro global.

Ejemplo:

```
var v = ctx.erp.GetParameter("MiParam");
```


#### ERP · Utilidades

### `erp.GetTotalLetter` (C#)

```
ctx.erp.GetTotalLetter(amount, currencyId?) : string
```

Importe con letra ("MIL ... PESOS 50/100 M.N."). currencyId 0 = moneda activa.

Ejemplo:

```
var letra = ctx.erp.GetTotalLetter(1234.50);
```


### `erp.GetTotalLetterEN` (C#)

```
ctx.erp.GetTotalLetterEN(amount, currencyId?) : string
```

Importe con letra en inglés (currencyId 0 = moneda activa).

Ejemplo:

```
var letra = ctx.erp.GetTotalLetterEN(1234.50);
```


### `erp.GetBarCode` (C#)

```
ctx.erp.GetBarCode(value, barcodeType?) : string
```

Código de barras codificado.

Ejemplo:

```
var bc = ctx.erp.GetBarCode("12345");
```


### `erp.DecryptString` (C#)

```
ctx.erp.DecryptString(encrypted) : string
```

Descifra una cadena de CONTPAQi.

Ejemplo:

```
var s = ctx.erp.DecryptString(enc);
```


### `erp.EncryptString` (C#)

```
ctx.erp.EncryptString(plain) : string
```

Cifra una cadena.

Ejemplo:

```
var enc = ctx.erp.EncryptString("texto");
```


### `erp.ValidRFC` (C#)

```
ctx.erp.ValidRFC(rfc) : bool
```

Valida un RFC.

Ejemplo:

```
if (ctx.erp.ValidRFC("XAXX010101000")) { /* ... */ }
```


### `erp.FormatCurrency` (C#)

```
ctx.erp.FormatCurrency(amount) : string
```

Formatea un número como moneda.

Ejemplo:

```
var s = ctx.erp.FormatCurrency(1234.5);
```


#### ERP · DLookup

### `erp.DLookup` (C#)

```
ctx.erp.DLookup(field, table, where?) : object
```

Consulta puntual de un campo sin escribir SQL.

Ejemplo:

```
var v = ctx.erp.DLookup("Total", "docDocument", "DocumentID=1");
```


### `erp.DLookupStr` (C#)

```
ctx.erp.DLookupStr(field, table, where?) : string
```

DLookup como string.

Ejemplo:

```
var s = ctx.erp.DLookupStr("Folio", "docDocument", "DocumentID=1");
```


### `erp.DLookupInt` (C#)

```
ctx.erp.DLookupInt(field, table, where?) : int
```

DLookup como int.

Ejemplo:

```
var n = ctx.erp.DLookupInt("StatusID", "docDocument", "DocumentID=1");
```


#### ERP · Bitácora

### `erp.WriteToLog` (C#)

```
ctx.erp.WriteToLog(message) : void
```

Escribe al log de CONTPAQi.

Ejemplo:

```
ctx.erp.WriteToLog("Proceso OK");
```


### `erp.WriteToTableLog` (C#)

```
ctx.erp.WriteToTableLog(message, detail?) : void
```

Escribe a la bitácora en tabla.

Ejemplo:

```
ctx.erp.WriteToTableLog("Acción", "detalle");
```


#### ERP · Impresión y export

### `erp.PrintDoc` (C#)

```
ctx.erp.PrintDoc(documentId) : void
```

Imprime el documento.

Ejemplo:

```
ctx.erp.PrintDoc(id);
```


### `erp.PrintModule` (C#)

```
ctx.erp.PrintModule() : void
```

Imprime la vista del módulo.

Ejemplo:

```
ctx.erp.PrintModule();
```


### `erp.UpdatePrintedOn` (C#)

```
ctx.erp.UpdatePrintedOn(documentId) : void
```

Marca el documento como impreso.

Ejemplo:

```
ctx.erp.UpdatePrintedOn(id);
```


### `erp.CreatePDF` (C#)

```
ctx.erp.CreatePDF(documentId, outputPath) : string
```

Genera el PDF del documento.

Ejemplo:

```
ctx.erp.CreatePDF(id, @"C:\temp\doc.pdf");
```


### `erp.ExportQueryToExcel` (C#)

```
ctx.erp.ExportQueryToExcel(sql, outputPath?) : void
```

Exporta el resultado de un SQL a Excel.

Ejemplo:

```
ctx.erp.ExportQueryToExcel("SELECT Folio, Total FROM docDocument");
```


### `erp.ExportJanusToExcel` (C#)

```
ctx.erp.ExportJanusToExcel(outputPath?) : void
```

Exporta la vista activa del módulo a Excel.

Ejemplo:

```
ctx.erp.ExportJanusToExcel();
```


#### ERP · Correo

### `erp.SendMail` (C#)

```
ctx.erp.SendMail(to, subject, body, attachmentPath?) : void
```

Envía correo con la config de CONTPAQi.

Ejemplo:

```
ctx.erp.SendMail("a@b.com", "Asunto", "Cuerpo");
```


### `erp.GetEmailTemplateID` (C#)

```
ctx.erp.GetEmailTemplateID(templateKey) : string
```

ID de una plantilla de correo.

Ejemplo:

```
var id = ctx.erp.GetEmailTemplateID("Factura");
```


#### ERP · Web / sistema

### `erp.GetWebContent` (C#)

```
ctx.erp.GetWebContent(url) : string
```

Descarga el contenido de una URL.

Ejemplo:

```
var html = ctx.erp.GetWebContent("https://contpaqi.com");
```


### `erp.GetHTMLFromURL` (C#)

```
ctx.erp.GetHTMLFromURL(url) : string
```

Descarga el HTML de una URL (variante).

Ejemplo:

```
var html = ctx.erp.GetHTMLFromURL("https://contpaqi.com");
```


### `erp.IsConnectedToInternet` (C#)

```
ctx.erp.IsConnectedToInternet() : bool
```

True si hay conexión a internet.

Ejemplo:

```
if (ctx.erp.IsConnectedToInternet()) { /* ... */ }
```


### `erp.RunShellExecute` (C#)

```
ctx.erp.RunShellExecute(path, args?) : void
```

Ejecuta un programa (ShellExecute).

Ejemplo:

```
ctx.erp.RunShellExecute(@"C:\app.exe");
```


#### ERP · CFDI

### `erp.AlreadyDocsSigned` (C#)

```
ctx.erp.AlreadyDocsSigned(documentId) : bool
```

True si el documento está timbrado y válido.

Ejemplo:

```
if (ctx.erp.AlreadyDocsSigned(id)) { /* ... */ }
```


### `erp.GetStatusPaidID` (C#)

```
ctx.erp.GetStatusPaidID(documentId) : int
```

Estado de pago (0=sin, 1=parcial, 2=pagado).

Ejemplo:

```
var st = ctx.erp.GetStatusPaidID(id);
```


#### ERP · Avanzado

### `erp.Call` (C#)

```
ctx.erp.Call(metodo, args...) : object
```

Llama CUALQUIER miembro de XEngine por nombre (los 562). Tú das los argumentos.

Ejemplo:

```
var qr = ctx.erp.Call("GetQRCode", "datos");
ctx.erp.Call("RecalcProductStock", 1);
```


### `erp.Get` (C#)

```
ctx.erp.Get(propiedad) : object
```

Lee CUALQUIER propiedad de XEngine por nombre.

Ejemplo:

```
var rfc = (string)ctx.erp.Get("COMERCIAL_RFC");
```


### `erp.CrearHelper` (C#)

```
ctx.erp.CrearHelper(progId) : object
```

Crea un COM auxiliar (Doc.clsMain, LBS.clsMain) con XEngine.

Crea un objeto COM auxiliar de Comercial (por ejemplo Doc.clsMain o Accounting.clsMain) con el motor ya asignado. Para llamar funciones nativas que ctx.erp aún no envuelve.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `progId` | string | El ProgID del componente. |

**Devuelve:** object COM (llámalo con Com.Call / InvokeMember).

**Ojo**
- Ejemplo real: la póliza de un documento creado por script se genera con Accounting.clsMain.CrearPolizasDocumento(documentId, false, "").

Ejemplo:

```
var doc = ctx.erp.CrearHelper("Doc.clsMain");
```

Generar la póliza de un documento (C#):

```
var acc = ctx.erp.CrearHelper("Accounting.clsMain");
var n = acc.GetType().InvokeMember("CrearPolizasDocumento", System.Reflection.BindingFlags.InvokeMethod, null, acc, new object[] { (long)docId, false, "" });
```


### `erp.XE` (C#)

```
ctx.erp.XE : object
```

XEngineLib crudo (casos no cubiertos por ctx.erp.*).

Ejemplo:

```
var xe = ctx.erp.XE;
```


#### Consultas SQL

### `OpenConn` (C#)

```
ctx.OpenConn() : SqlConnection
```

Conexión SQL propia (ya abierta), independiente de la de Comercial. Para lotes de altas y transacciones.

Abre una conexión SQL propia, independiente de la de Comercial. Es el camino correcto para lotes de altas, transacciones y cualquier cosa con varios INSERT.

**Devuelve:** SqlConnection ya abierta; ciérrala con using.

**Ojo**
- Usa siempre SET NOCOUNT ON al inicio del lote y haz el SQL idempotente (que repetirlo no duplique).
- Con una transacción (cn.BeginTransaction()) o queda todo o no queda nada.

Ejemplo:

```
using (var cn = ctx.OpenConn())
using (var cmd = cn.CreateCommand())
{
    cmd.CommandText = "SET NOCOUNT ON; SELECT COUNT(*) FROM docDocument";
    var n = cmd.ExecuteScalar();
}
```

Insertar y leer el ID (C#):

```
using (var cn = ctx.OpenConn())
using (var cmd = cn.CreateCommand())
{
    cmd.CommandText = "SET NOCOUNT ON; INSERT INTO miTabla(Nombre) VALUES(@n); SELECT SCOPE_IDENTITY();";
    cmd.Parameters.AddWithValue("@n", "Ejemplo");
    var id = cmd.ExecuteScalar();
}
```


#### Interacción

### `ShowHtml` (C#)

```
ctx.ShowHtml(html, titulo?, ancho?, alto?, modal?) : void
```

Muestra una ventana con HTML/CSS/JS real (WebView2).

Abre una ventana con una página HTML real (WebView2): reportes, tableros, formularios bonitos. Con modal=false no bloquea a Comercial.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `html` | string | La página completa. |
| `titulo` | string | Título de la ventana. |
| `ancho / alto` | int | Tamaño en píxeles. |
| `modal` | bool | true = espera a que la cierren. |

**Devuelve:** nada.

**Ojo**
- Para una página con formulario que devuelva datos usa ShowHtmlFormulario.

Ejemplo:

```
ctx.ShowHtml("<h1>Hola</h1>", "Reporte", 800, 600);
```


### `ShowHtmlFormulario` (C#)

```
ctx.ShowHtmlFormulario(html, titulo?, ancho?, alto?, timeoutMs?) : Dictionary<string,object>
```

Ventana HTML que devuelve lo que la página envíe con postMessage (formularios propios).

Como ShowHtml pero espera a que la página mande datos con window.chrome.webview.postMessage(JSON.stringify({...})) y los devuelve.

**Devuelve:** Dictionary con lo que envió la página más "submitted" (true si envió algo; false si cerró la ventana).

Ejemplo:

```
var r = ctx.ShowHtmlFormulario(html, "Datos");
if ((bool)r["submitted"]) { /* usa r["campo"] */ }
```


### `ShowHtmlModeless` (C#)

```
ctx.ShowHtmlModeless(html, titulo?, ancho?, alto?, alMensaje?) : void
```

Ventana HTML que no bloquea: el script termina y cada mensaje de la página llega a tu función en el hilo de Comercial.

Úsala cuando la ventana deba seguir abierta mientras se abren documentos o se llama a ctx.erp: ShowHtmlFormulario bloquea el hilo de Comercial y XEngine contesta «the other application is busy». La función alMensaje recibe los campos del mensaje (window.chrome.webview.postMessage(JSON.stringify({...}))) y regresa null (nada), "__CERRAR__" (cerrar la ventana) o un HTML nuevo (repinta). Maneja tus propios errores como en cualquier ventana modeless (MANUAL §10.2). Límite de ~2 MB de HTML.

**Devuelve:** Nada: la ventana queda abierta y el script termina.

Ejemplo:

```
ctx.ShowHtmlModeless(html, "Mi reporte", 1100, 760, m =>
{
    if ((string)m["accion"] == "abrir") ctx.erp.AbrirDocumento(Convert.ToInt32(m["id"]), Convert.ToInt32(m["modulo"]));
    return null; // null = no hacer nada; "__CERRAR__" = cerrar; otro texto = HTML nuevo
});
```


#### ERP · Documento

### `erp.AbrirDocumento` (C#)

```
ctx.erp.AbrirDocumento(documentId, moduleId) : void
```

Abre un documento YA guardado en su ventana real, como si el usuario lo abriera de la lista.

Abre un documento ya guardado en su ventana real, igual que si el usuario lo abriera desde la lista. Es la forma de terminar un botón que crea un documento.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |
| `moduleId` | int | Su módulo. |

**Devuelve:** nada.

Ejemplo:

```
ctx.erp.AbrirDocumento(docId, 152);
```


### `erp.AgregarRenglonExistente` (C#)

```
ctx.erp.AgregarRenglonExistente(documentId, moduleId, productId, cantidad, precio) : void
```

Agrega una partida a un documento que ya existe (de una ejecución anterior) y recalcula.

Agrega una partida a un documento que YA existía (guardado en otra ejecución) y recalcula al final.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |
| `moduleId` | int | Su módulo. |
| `productId` | int | El producto. |
| `cantidad` | double | Cantidad. |
| `precio` | double | Precio. |

**Devuelve:** nada.

Ejemplo:

```
ctx.erp.AgregarRenglonExistente(docId, 152, 1, 2, 100);
```


### `erp.AgregarSerie` (C#)

```
ctx.erp.AgregarSerie(documentId, documentItemId, productId, serialNumber, depotId, quantity?) : void
```

Registra un número de serie en una partida (quantity -1 = salida, 1 = entrada).

Ejemplo:

```
ctx.erp.AgregarSerie(docId, itemId, prodId, "SN-001", 1, 1);
```


#### ERP · CFDI

### `erp.RelacionarCFDI` (C#)

```
ctx.erp.RelacionarCFDI(documentId, sourceDocumentId, tipoRelacion) : void
```

Relaciona el CFDI de un documento con otro (catálogo SAT c_TipoRelacion: "01", "03", "07"…).

Ejemplo:

```
ctx.erp.RelacionarCFDI(docId, anticipoId, "07");
```


### `erp.Timbrar` (C#)

```
ctx.erp.Timbrar(documentId, pruebas?) : void
```

Timbra el documento con el motor nativo de Comercial (CFDI3). pruebas=true usa el modo de pruebas del PAC.

Timbra un documento con el motor nativo de Comercial (el mismo del módulo de facturación).

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento. |
| `pruebas` | bool | true = modo de pruebas del PAC (sin timbre fiscal real). |

**Devuelve:** nada; lanza una excepción con el detalle del PAC/SAT si falla.

**Ojo**
- Confirma con el usuario antes de timbrar: un timbre real no se deshace (solo se cancela).

Ejemplo:

```
ctx.erp.Timbrar(docId, false);
```

Timbrar con confirmación (C#):

```
if (ctx.Confirm("¿Timbrar el documento?")) ctx.erp.Timbrar(docId, false);
```


#### ERP · Avanzado

### `erp.Set` (C#)

```
ctx.erp.Set(propiedad, valor) : bool
```

Asigna una propiedad del motor XEngine (escape a COM).

Ejemplo:

```
ctx.erp.Set("MustRefreshGrid", true);
```


### `erp.LastError` (C#)

```
ctx.erp.LastError : string
```

Último error de una llamada COM al motor (null si la última fue bien). Revísalo tras Save, AffectStockNEW, CancelDocument…

Último error de una llamada COM al motor de Comercial; null si la última fue bien. Revísalo después de Save, AffectStockNEW, CancelDocument, Delete…

**Devuelve:** string o null.

Ejemplo:

```
ctx.erp.Save(docId);
if (ctx.erp.LastError != null) ctx.Msg(ctx.erp.LastError);
```


#### Contexto

### `erp` (C#)

```
ctx.erp : ErpContext
```

Acceso tipado al motor de Comercial (documentos, precios, folios, UI…). Solo en C#; en Python se usa ctx.erp.Método().

Ejemplo:

```
var rfc = ctx.erp.ComercialRFC;
```


### `UserIdReal` (C#)

```
ctx.UserIdReal() : int
```

ID real del usuario (ctx.UserID suele venir 0).

Ejemplo:

```
var u = ctx.UserIdReal();
```


### `NombreUsuario` (C#)

```
ctx.NombreUsuario(userId) : string
```

Nombre del usuario (engUser.UserName) o "" si no existe. Nunca lanza.

Ejemplo:

```
var n = ctx.NombreUsuario(ctx.UserIdReal());
```


#### Selección y datos

### `ObtenerIdDeVentanaActiva` (C#)

```
ctx.ObtenerIdDeVentanaActiva() : long?
```

ID del documento GUARDADO que está abierto en pantalla (GetSelectedIds no sirve dentro de un documento). null si no aplica.

Ejemplo:

```
var id = ctx.ObtenerIdDeVentanaActiva();
if (id == null) { ctx.Msg("Guarda el documento primero."); return; }
```


#### Contexto

### `Headless` (C#)

```
ctx.Headless : bool
```

true cuando el script corre sin pantalla (Runner): Msg/Confirm no abren diálogos.

Ejemplo:

```
if (!ctx.Headless) ctx.Msg("Listo");
```


### `EventoId` (C#)

```
ctx.EventoId : long?
```

ID que trae un evento nativo (Función = "BrosLMV.<Script>_[DocumentID]"); null si se ejecutó por botón o consola.

Ejemplo:

```
var doc = ctx.EventoId;
```


### `CadenaSql` (C#)

```
ctx.CadenaSql() : string
```

Cadena de conexión SqlClient resuelta. Para abrir conexiones desde otro hilo (no llames COM desde ahí).

Ejemplo:

```
var cs = ctx.CadenaSql();
```


### `VersionProvisionada` (C#)

```
ctx.VersionProvisionada() : string
```

Versión de BrosLMV con que se provisionó la empresa ("" si no).

Ejemplo:

```
var v = ctx.VersionProvisionada();
```


#### Utilidades

### `HashPassword` (C#)

```
ctx.HashPassword(password, out sal, out hash, out iteraciones) : void
```

Hash de contraseña PBKDF2-HMAC-SHA256 (el mismo de la Consola).

Ejemplo:

```
ctx.HashPassword("clave", out var sal, out var hash, out var it);
```


### `VerifyPassword` (C#)

```
ctx.VerifyPassword(password, sal, hash, iteraciones) : bool
```

Verifica una contraseña contra su hash.

Ejemplo:

```
bool ok = ctx.VerifyPassword("clave", sal, hash, it);
```


#### ERP · Esquema y entorno

### `erp.TableExists` (C#)

```
ctx.erp.TableExists(tableName) : bool
```

¿Existe la tabla en la base de la empresa activa?

Pregunta al motor de Comercial si una tabla existe en la base de la empresa activa. Sirve para que un script funcione igual en empresas con distinta versión o con tablas propias sin provisionar.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `tableName` | string | Nombre de la tabla, sin esquema. |

**Devuelve:** true si existe; false si no.

**Ojo**
- Probada: true para tablas existentes y false para inexistentes.
- No distingue vistas de tablas del motor (usa SQL contra sys.objects si necesitas ese detalle).

Ejemplo:

```
if (!ctx.erp.TableExists("zzMiTabla")) { /* crearla */ }
```


### `erp.FieldExistsInTable` (C#)

```
ctx.erp.FieldExistsInTable(fieldName, tableName) : bool
```

¿Existe la columna en la tabla? (orden nativo: campo, tabla)

Comprueba si una columna existe en una tabla. Úsalo antes de leer o escribir columnas que cambian entre versiones de Comercial.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `fieldName` | string | Nombre de la columna. |
| `tableName` | string | Nombre de la tabla. |

**Devuelve:** true si existe; false si no.

**Ojo**
- OJO: el orden es el nativo del motor (primero el CAMPO, luego la TABLA), al revés de lo que se esperaría.

Ejemplo:

```
bool hay = ctx.erp.FieldExistsInTable("Folio", "docDocument");
```


### `erp.GetModuleIDDocumentType` (C#)

```
ctx.erp.GetModuleIDDocumentType(documentTypeId, docRecipientId) : int
```

ModuleID de fábrica para un tipo de documento y recipiente.

**Ojo**
- Devuelve el módulo de fábrica: en empresas con módulos clonados (series, formas de pago) hay que clasificar por engModule.ModuleIDBase.

Ejemplo:

```
int modulo = ctx.erp.GetModuleIDDocumentType(5, 1); // factura de cliente = 21
```


### `erp.GetModuleDLLName` (C#)

```
ctx.erp.GetModuleDLLName(moduleId) : string
```

Nombre de la DLL que atiende un módulo.

Ejemplo:

```
string dll = ctx.erp.GetModuleDLLName(248); // "FinancialOperation"
```


### `erp.GetSecurityFunctionality` (C#)

```
ctx.erp.GetSecurityFunctionality(functionalityKey, moduleId) : bool
```

¿El usuario activo tiene permiso sobre esa funcionalidad del módulo?

**Ojo**
- Consulta los permisos nativos de Comercial (engSecurity*) del usuario con sesión: úsala para respetar los permisos del sistema en botones que borran o cancelan.

Ejemplo:

```
bool puede = ctx.erp.GetSecurityFunctionality("Document.Delete", 21);
```


### `erp.GetUserCanElevatePrivileges` (C#)

```
ctx.erp.GetUserCanElevatePrivileges() : bool
```

¿El usuario activo es administrador de Comercial?

Ejemplo:

```
if (!ctx.erp.GetUserCanElevatePrivileges()) { ctx.Msg("Solo un administrador."); return; }
```


#### ERP · Parámetros

### `erp.GetDefaultValue` (C#)

```
ctx.erp.GetDefaultValue(key, countryId = 1) : string
```

Lee un parámetro de engParameter (clave + país).

**Ojo**
- Devuelve "" si la clave no existe. Lee de la tabla nativa engParameter (Key, Value, Description, CountryID) de la empresa activa.

Ejemplo:

```
string v = ctx.erp.GetDefaultValue("BROS_MI_CLAVE");
```


### `erp.SaveDefaultValue` (C#)

```
ctx.erp.SaveDefaultValue(key, value, description = "", countryId = 1) : void
```

Guarda (inserta o sobrescribe) un parámetro en engParameter.

Almacén de configuración por empresa que ya trae Comercial (engParameter). Si la clave existe la sobrescribe; si no, la crea. Útil para guardar la configuración de un botón sin crear una tabla propia.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `key` | string | Clave. Usa SIEMPRE un prefijo propio (BROS_...). |
| `value` | string | Valor. |
| `description` | string | Descripción opcional. |
| `countryId` | int | País (1 = México). |

**Devuelve:** nada.

**Ojo**
- ESCRIBE en la configuración nativa del sistema: nunca uses una clave de Comercial; engParameter tiene cientos de claves del propio producto.
- Probada: escribir, leer, sobrescribir y que otro país no la vea.

Ejemplo:

```
ctx.erp.SaveDefaultValue("BROS_MI_CLAVE", "valor", "para qué sirve");
```


#### ERP · Fecha y texto

### `erp.GetLastDayMonth` (C#)

```
ctx.erp.GetLastDayMonth(date) : int
```

Último día del mes (28 a 31).

Ejemplo:

```
int d = ctx.erp.GetLastDayMonth(new DateTime(2026, 2, 10)); // 28
```


### `erp.DateFromString` (C#)

```
ctx.erp.DateFromString(datePart, timePart) : DateTime
```

Arma una fecha-hora desde 'yyyy-MM-dd' y 'HH:mm:ss'.

Ejemplo:

```
DateTime f = ctx.erp.DateFromString("2026-10-01", "12:30:00");
```


### `erp.ConvertDateTimeToUTC` (C#)

```
ctx.erp.ConvertDateTimeToUTC(dateTime) : string
```

Fecha-hora con desfase horario, como la usa Comercial en CFDI.

**Ojo**
- El resultado lleva un ESPACIO antes del desfase ("…:00 -06:00"): es el formato del motor; ajústalo si el destino lo exige distinto.

Ejemplo:

```
string s = ctx.erp.ConvertDateTimeToUTC(DateTime.Now); // 2026-10-01T12:30:00 -06:00
```


### `erp.GetFormatedDateValue` (C#)

```
ctx.erp.GetFormatedDateValue(date) : string
```

Fecha como texto 'yyyy-MM-dd HH:mm:ss'.

Ejemplo:

```
string s = ctx.erp.GetFormatedDateValue(DateTime.Today);
```


### `erp.Pad` (C#)

```
ctx.erp.Pad(value, length, fillWith, alignment) : string
```

Rellena un texto a una longitud ('L' izquierda, 'R' derecha).

**Ojo**
- alignment "L": el texto queda a la izquierda y se rellena a la derecha ("7" -> "70000"); "R": el texto queda a la derecha ("7" -> "00007").
- Probada con "L" y "R".

Ejemplo:

```
string s = ctx.erp.Pad("7", 5, "0", "R"); // 00007
```


### `erp.TruncateDouble` (C#)

```
ctx.erp.TruncateDouble(value, decimals) : double
```

Trunca (no redondea) a N decimales.

**Ojo**
- Trunca hacia cero: (-3.999, 1) = -3.9.

Ejemplo:

```
double t = ctx.erp.TruncateDouble(12.98765, 2); // 12.98
```


### `erp.GetSerialNumberPrefix` (C#)

```
ctx.erp.GetSerialNumberPrefix(serialNumber) : string
```

Parte alfabética de un número de serie.

Ejemplo:

```
string p = ctx.erp.GetSerialNumberPrefix("ABC00123"); // ABC
```


### `erp.GetSerialNumberNumValue` (C#)

```
ctx.erp.GetSerialNumberNumValue(serialNumber) : string
```

Parte numérica de un número de serie (conserva ceros).

Ejemplo:

```
string n = ctx.erp.GetSerialNumberNumValue("ABC00123"); // 00123
```


### `erp.GetFormatedXML` (C#)

```
ctx.erp.GetFormatedXML(xml) : string
```

XML con sangrías (legible).

Ejemplo:

```
string bonito = ctx.erp.GetFormatedXML(xml);
```


### `erp.GetMaxValueField` (C#)

```
ctx.erp.GetMaxValueField(fieldName, tableName, where = "") : long
```

Máximo de una columna entera, con filtro opcional.

**Ojo**
- El filtro es texto SQL: no le pases valores de usuario sin validar. Para folios que deben ser únicos con procesos en paralelo usa sp_getapplock (el máximo+1 no es atómico).

Ejemplo:

```
long max = ctx.erp.GetMaxValueField("DocumentID", "docDocument", "ModuleID=21");
```


### `erp.GetQRCode` (C#)

```
ctx.erp.GetQRCode(text) : string
```

Código QR del texto como PNG en base64.

Genera con el motor de Comercial el código QR de un texto y lo devuelve como imagen PNG codificada en base64. Sirve para PDFs y páginas HTML (<img src="data:image/png;base64,...">), por ejemplo el QR del timbre de un CFDI.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `text` | string | Texto a codificar (URL, cadena del SAT...). |

**Devuelve:** Base64 de un PNG (empieza con "iVBORw0KGgo"); "" si falla (revisa LastError).

Ejemplo:

```
string png = ctx.erp.GetQRCode("https://ejemplo.com/x");
```

Insertarlo en HTML (C#):

```
string html = "<img src=\"data:image/png;base64," + ctx.erp.GetQRCode(url) + "\">";
```


#### ERP · Costos

### `erp.GetCostLast` (C#)

```
ctx.erp.GetCostLast(productId) : double
```

Costo de la última compra del producto.

**Ojo**
- Devuelve 0 si el producto no tiene compras.

Ejemplo:

```
double c = ctx.erp.GetCostLast(productoId);
```


### `erp.RecalcCostComercial` (C#)

```
ctx.erp.RecalcCostComercial(productId) : void
```

Recalcula el costo comercial vigente del producto.

**Ojo**
- Recalcula orgProduct.CostPriceComercial desde el libro de costos (13 de 15 productos de prueba volvieron al valor original; 2 difirieron). Es idempotente sobre datos consistentes (15 de 15 sin cambios).
- NO reconstruye las columnas de salida del libro por documento (para costear un documento usa CalcularCostos).

Ejemplo:

```
ctx.erp.RecalcCostComercial(productoId);
```


### `erp.RecalcCostFiscal` (C#)

```
ctx.erp.RecalcCostFiscal(productId) : void
```

Recalcula el costo fiscal vigente del producto.

Ejemplo:

```
ctx.erp.RecalcCostFiscal(productoId);
```


#### ERP · Cobros y pagos

### `erp.SaveAllTaxesPayment` (C#)

```
ctx.erp.SaveAllTaxesPayment(financialOperationId) : void
```

Reconstruye el reparto de impuestos de un cobro/pago (opción conservadora).

Rehace con la rutina de Comercial el reparto de impuestos de un cobro o pago (docFinancialOperationTaxDetail: base, importe, proporción, retenciones y columnas en MXN). Es la opción conservadora: si el cobro ya tenía reparto lo reconstruye, y si Comercial nunca se lo generó (facturas PUE, pagos antiguos) NO lo inventa.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `financialOperationId` | int | La operación financiera (FinancialOperationID) del cobro o pago. |

**Devuelve:** nada.

**Ojo**
- Pruebas en laboratorio (37 cobros reales): rehízo el reparto con el mismo número de filas en 13 de 15 cobros que lo tenían y no creó filas en los 22 que no lo tenían.
- No toca saldos insolutos ni el documento: después llama a UpdateDocumentPaidInfo (y a AjustarSaldosInsolutos si aplica).
- Revisa ctx.erp.LastError: Com.Call traga las excepciones COM.

Ejemplo:

```
ctx.erp.SaveAllTaxesPayment(operacionId);
ctx.erp.UpdateDocumentPaidInfo(documentoId);
```


### `erp.RecalcPagosDocumento` (C#)

```
ctx.erp.RecalcPagosDocumento(documentId) : void
```

Reconstruye reparto de impuestos y saldos insolutos de los cobros de un documento.

Llama a Payment.clsMain.RecalcDocumentPayments, la rutina que Comercial usa al guardar un cobro: reconstruye el reparto de impuestos y los saldos anterior/insoluto de TODOS los cobros y notas de crédito aplicados al documento. Resuelve el hueco de crear un cobro por SQL que queda sin reparto de impuestos.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `documentId` | int | El documento (factura) al que están aplicados los cobros. |

**Devuelve:** nada.

**Ojo**
- Pruebas (107 documentos reales, copia de laboratorio): 0 excepciones; no altera lo que Comercial ya guardó; reconstruyó el reparto en 68 de 71 documentos que lo tenían, incluidos 20 de 20 con retenciones.
- A diferencia de SaveAllTaxesPayment, AGREGA reparto de impuestos a documentos que no lo tenían (en la muestra: 33 de 107, 21 de ellos PUE). Úsala con facturas PPD.
- NO recalcula los saldos de renglones ya incluidos en un REP timbrado (docDocumentPayment.AltID > 0): es lo correcto fiscalmente.
- Trabaja a nivel documento: si borraste a mano solo el reparto de UNA operación de un documento con varias, puede fallar con 'Division by zero' (revisa ctx.erp.LastError).
- No actualiza TotalPaid/Balance/StatusPaidID del documento: llama a UpdateDocumentPaidInfo después.

Ejemplo:

```
ctx.erp.RecalcPagosDocumento(facturaId);
ctx.erp.UpdateDocumentPaidInfo(facturaId);
```

Cobro insertado por SQL: completarlo (C#):

```
// ... INSERT de docFinancialOperation + docDocumentPayment ...
ctx.erp.RecalcPagosDocumento(facturaId);       // reparto de impuestos y saldos insolutos
ctx.erp.UpdateDocumentPaidInfo(facturaId);     // TotalPaid / Balance / StatusPaidID
if (!string.IsNullOrEmpty(ctx.erp.LastError)) ctx.Msg(ctx.erp.LastError);
```


## Referencia de Python

#### Python

### `ctx.user_id` (Python)

```
ctx.user_id : int
```

ID del usuario activo de CONTPAQi.

Ejemplo:

```
usr = ctx.user_id
```


### `ctx.module_id` (Python)

```
ctx.module_id : int
```

ID del módulo activo.

Ejemplo:

```
mod = ctx.module_id
```


### `ctx.empresa` (Python)

```
ctx.empresa : str
```

Base de datos de la empresa activa.

Ejemplo:

```
bd = ctx.empresa
```


### `ctx.app_key` (Python)

```
ctx.app_key : str
```

AppKey del botón en ejecución.

Ejemplo:

```
clave = ctx.app_key
```


### `ctx.fila` (Python)

```
ctx.fila : dict
```

Campos de la primera fila seleccionada del grid.

Ejemplo:

```
folio = ctx.fila.get("Folio")
```


### `ctx.context` (Python)

```
ctx.context() : dict
```

Todo el contexto vivo como diccionario.

Ejemplo:

```
info = ctx.context()
```


### `ctx.get_selected_ids` (Python)

```
ctx.get_selected_ids() : list[int]
```

IDs de los documentos seleccionados en la vista.

IDs seleccionados en la lista de Comercial.

**Devuelve:** list[int].

Ejemplo:

```
ids = ctx.get_selected_ids()
```


### `ctx.query` (Python)

```
ctx.query(sql, params=None) : list[dict]
```

Ejecuta SQL y devuelve una lista de diccionarios. Parámetros con @nombre.

Ejecuta una consulta y devuelve la lista de filas como diccionarios. Acepta parámetros con @nombre y un diccionario.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | str | La consulta. |
| `params` | dict | Valores para @nombre (opcional). |

**Devuelve:** list[dict].

Ejemplo:

```
ids = ctx.get_selected_ids()
filas = ctx.query("SELECT Folio, Total FROM docDocument WHERE DocumentID = @id", {"id": ids[0]})
```

Con parámetros (Python):

```
filas = ctx.query("SELECT TOP 5 Folio, Total FROM docDocument WHERE ModuleID = @m", {"m": 152})
for f in filas:
    print(f["Folio"], f["Total"])
```


### `ctx.scalar` (Python)

```
ctx.scalar(sql, params=None) : Any
```

Ejecuta SQL y devuelve el primer valor.

Ejecuta una consulta y devuelve la primera columna de la primera fila.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | str | La consulta. |
| `params` | dict | Valores para @nombre (opcional). |

**Devuelve:** El valor (o None).

Ejemplo:

```
total = ctx.scalar("SELECT SUM(Total) FROM docDocument WHERE DeletedOn IS NULL")
```


### `ctx.execute` (Python)

```
ctx.execute(sql, params=None) : int
```

Ejecuta INSERT/UPDATE/DELETE. Devuelve filas afectadas.

Ejecuta INSERT/UPDATE/DELETE y devuelve las filas afectadas.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `sql` | str | La sentencia. |
| `params` | dict | Valores para @nombre (opcional). |

**Devuelve:** int.

**Ojo**
- Se bloquea en modo solo lectura.

Ejemplo:

```
n = ctx.execute("UPDATE docDocument SET Referencia = @r WHERE DocumentID = @id", {"r": "X", "id": ctx.get_selected_ids()[0]})
```


### `ctx.msg` (Python)

```
ctx.msg(texto, titulo="BrosLMV")
```

Muestra un mensaje al usuario.

Muestra un mensaje al usuario.

| Parámetro | Tipo | Qué es |
|---|---|---|
| `texto` | str | El mensaje. |
| `titulo` | str | Título (opcional). |

**Devuelve:** None.

Ejemplo:

```
ctx.msg("Proceso terminado", "Aviso")
```


### `ctx.confirm` (Python)

```
ctx.confirm(texto, titulo="Confirmar") : bool
```

Pregunta Sí/No y bloquea hasta que el usuario responda.

Ejemplo:

```
if ctx.confirm("¿Continuar?"):
    ctx.msg("Confirmado")
```


### `ctx.log` (Python)

```
ctx.log(texto, nivel="INFO")
```

Escribe a la bitácora/auditoría.

Ejemplo:

```
ctx.log("Actualizados {} docs".format(n))
```


### `ctx.progress` (Python)

```
ctx.progress(texto="", porcentaje=0)
```

Actualiza el progreso de la ejecución.

Ejemplo:

```
ctx.progress("Procesando...", 50)
```


### `ctx.form` (Python)

```
ctx.form(spec) : dict
```

Formulario con campos y/o grid editable. Ver docs/PYTHON.md.py.

Formulario nativo definido con un diccionario (campos, combos, tablas editables). Devuelve lo que el usuario captura.

**Devuelve:** dict con "submitted" y "values".

**Ojo**
- Los campos simples llegan en r["values"][nombre]; las tablas editables en r["grid_rows"].

Ejemplo:

```
r = ctx.form({
    "title": "Datos",
    "fields": [{"name": "nota", "label": "Nota", "type": "text"}],
})
if r["submitted"]:
    ctx.msg(r["values"]["nota"])
```


### `ctx.show_html` (Python)

```
ctx.show_html(html, titulo="BrosLMV", ancho=800, alto=600, modal=True)
```

Ventana con HTML/CSS/JS real (WebView2). Ver PLANTILLA_EJEMPLO_DASHBOARD_VENTAS_PYTHON.py.

Ventana con HTML/CSS/JS real (WebView2).

**Devuelve:** None.

Ejemplo:

```
ctx.show_html("<h1>Hola</h1>", "Reporte")
```


### `ctx.select_file` (Python)

```
ctx.select_file(titulo="...", filtro="Excel|*.xlsx", guardar=False) : str
```

Diálogo nativo para elegir archivo. "" si canceló.

Ejemplo:

```
ruta = ctx.select_file("Elegir Excel", "Excel|*.xlsx")
```


### `ctx.select_folder` (Python)

```
ctx.select_folder(titulo="...") : str
```

Diálogo nativo para elegir carpeta. "" si canceló.

Ejemplo:

```
carpeta = ctx.select_folder("Elegir destino")
```


### `ctx.read_excel` (Python)

```
ctx.read_excel(ruta, hoja=None) : list[dict]
```

Lee un .xlsx como lista de dict (encabezados = 1ª fila). No requiere Excel instalado.

Ejemplo:

```
filas = ctx.read_excel(ctx.select_file())
```


### `ctx.write_excel` (Python)

```
ctx.write_excel(filas, ruta, hoja="Hoja1")
```

Escribe una lista de dict a .xlsx.

Ejemplo:

```
ctx.write_excel([{"Producto": "X", "Cant": 10}], r"C:\reporte.xlsx")
```


### `result` (Python)

```
result = <valor>
```

Variable global que devuelve el script (se muestra al usuario).

Ejemplo:

```
result = f"Empresa={ctx.empresa}, seleccionados={ctx.get_selected_ids()}"
```


### `ctx.erp.UserId` (Python)

```
ctx.erp.UserId() : int
```

Usuario real de CONTPAQi (propiedad → con paréntesis).

Ejemplo:

```
u = ctx.erp.UserId()
```


### `ctx.erp.ComercialRFC` (Python)

```
ctx.erp.ComercialRFC() : str
```

RFC de la empresa (propiedad → con paréntesis).

Ejemplo:

```
rfc = ctx.erp.ComercialRFC()
```


### `ctx.erp.GetProductStock` (Python)

```
ctx.erp.GetProductStock(productID, depotID) : float
```

Existencia del producto (depot 0 = todos).

Ejemplo:

```
ex = ctx.erp.GetProductStock(125, 0)
```


### `ctx.erp.GetSalePrice` (Python)

```
ctx.erp.GetSalePrice(productID) : float
```

Precio de venta del producto.

Ejemplo:

```
pv = ctx.erp.GetSalePrice(125)
```


### `ctx.erp.GetCostPrice` (Python)

```
ctx.erp.GetCostPrice(productID) : float
```

Costo del producto.

Ejemplo:

```
c = ctx.erp.GetCostPrice(125)
```


### `ctx.erp.GetPriceWithTaxes` (Python)

```
ctx.erp.GetPriceWithTaxes(precio, taxTypeID) : float
```

Precio con impuestos incluidos.

Ejemplo:

```
t = ctx.erp.GetPriceWithTaxes(100, 1)
```


### `ctx.erp.GetTotalLetter` (Python)

```
ctx.erp.GetTotalLetter(importe) : str
```

Importe con letra (moneda activa).

Ejemplo:

```
letra = ctx.erp.GetTotalLetter(1234.50)
```


### `ctx.erp.GetNextFolio` (Python)

```
ctx.erp.GetNextFolio(moduleID, serie, depotID) : str
```

Siguiente folio disponible.

Ejemplo:

```
f = ctx.erp.GetNextFolio(183, "OC", 1)
```


### `ctx.erp.RecalcDocument` (Python)

```
ctx.erp.RecalcDocument(documentID)
```

Recalcula totales del documento (escritura).

Ejemplo:

```
ctx.erp.RecalcDocument(ctx.get_selected_ids()[0])
```


### `ctx.erp.RecalcCompleto` (Python)

```
ctx.erp.RecalcCompleto(documentID)
```

Recalcula totales + costos del documento.

Ejemplo:

```
ctx.erp.RecalcCompleto(doc_id)
```


### `ctx.erp.OwnedBusinessEntityId` (Python)

```
ctx.erp.OwnedBusinessEntityId() : int
```

Empresa propia (propiedad → con paréntesis).

Ejemplo:

```
be = ctx.erp.OwnedBusinessEntityId()
```


### `ctx.erp.NuevoDocumento` (Python)

```
ctx.erp.NuevoDocumento(moduleID, depotID, businessEntityID=0) : int
```

Crea el encabezado de un documento con los defaults del módulo y devuelve el DocumentID.

Crea el encabezado de un documento nuevo y devuelve su ID. Mismos parámetros que en C#.

**Devuelve:** int.

Ejemplo:

```
doc_id = ctx.erp.NuevoDocumento(183, 1, 162)
```


### `ctx.erp.AgregarArticulo` (Python)

```
ctx.erp.AgregarArticulo(documentID, productID, cantidad=1, precio=-1) : int
```

Agrega una partida (lee orgProduct). Tras agregar, llamar RecalcCompleto.

Agrega una partida a un documento nuevo. Después: RecalcCompleto.

**Devuelve:** int (DocumentItemID).

Ejemplo:

```
ctx.erp.AgregarArticulo(doc_id, 1, 3, 100)
ctx.erp.RecalcCompleto(doc_id)
```


### `ctx.erp.Timbrar` (Python)

```
ctx.erp.Timbrar(documentID, pruebas=False)
```

Timbra el documento (motor nativo de Comercial). Operación fiscal real.

Ejemplo:

```
if ctx.confirm("¿Timbrar?"):
    ctx.erp.Timbrar(doc_id, False)
```


### `ctx.erp.RelacionarCFDI` (Python)

```
ctx.erp.RelacionarCFDI(documentID, sourceDocumentID, tipoRelacion)
```

Liga un CFDI con otro (NC, devolución, anticipo).

Ejemplo:

```
ctx.erp.RelacionarCFDI(doc_id, oc_id, "07")
```


### `ctx.erp.Call` (Python)

```
ctx.erp.Call(metodo, *args)
```

Llama CUALQUIER miembro de XEngine por nombre.

Ejemplo:

```
qr = ctx.erp.Call("GetQRCode", "datos")
```


### `ctx.erp.Get` (Python)

```
ctx.erp.Get(propiedad)
```

Lee CUALQUIER propiedad de XEngine por nombre.

Ejemplo:

```
rfc = ctx.erp.Get("COMERCIAL_RFC")
```


### `ctx.nuevo` (Python)

```
ctx.nuevo(tabla) : Record
```

Crea un registro para INSERT en cualquier tabla. set() campos, guardar() devuelve el ID.

Ejemplo:

```
it = ctx.nuevo("docDocumentItem")
it["DocumentID"] = doc_id
it["ProductID"] = 1
it["Quantity"] = 2
it.guardar()
```


### `ctx.registro` (Python)

```
ctx.registro(tabla, pk) : Record
```

Carga un registro existente por su PK. Modificar campos y actualizar() solo envía los cambios.

Ejemplo:

```
doc = ctx.registro("docDocument", 11556)
doc["Comments"] = "Modificado"
doc.actualizar()
```


#### ctx.erp (Comercial)

### `ctx.erp.RefreshGrid` (Python)

```
ctx.erp.RefreshGrid() : None
```

Refresca la lista de Comercial y conserva la fila seleccionada. Una sola vez, al final.

Refresca la lista de Comercial una sola vez, al final, y conserva la fila.

**Devuelve:** None.

Ejemplo:

```
ctx.erp.RefreshGrid()
```


### `ctx.erp.AbrirDocumento` (Python)

```
ctx.erp.AbrirDocumento(documentID, moduleID) : None
```

Abre un documento ya guardado en su ventana.

Ejemplo:

```
ctx.erp.AbrirDocumento(doc_id, 152)
```


### `ctx.erp.Save` (Python)

```
ctx.erp.Save(documentID) : None
```

Guarda el documento.

Ejemplo:

```
ctx.erp.Save(doc_id)
```


### `ctx.erp.AffectStockNEW` (Python)

```
ctx.erp.AffectStockNEW(documentID) : None
```

Afecta inventario (kardex) del documento.

Ejemplo:

```
ctx.erp.AffectStockNEW(doc_id)
```


#### Python

### `ctx.erp.TableExists` (Python)

```
ctx.erp.TableExists(tableName)
```

¿Existe la tabla en la base de la empresa activa?

Ejemplo:

```
if not ctx.erp.TableExists("zzMiTabla"): ...
```


### `ctx.erp.FieldExistsInTable` (Python)

```
ctx.erp.FieldExistsInTable(fieldName, tableName)
```

¿Existe la columna en la tabla? (orden nativo: campo, tabla)

Ejemplo:

```
ctx.erp.FieldExistsInTable("Folio", "docDocument")
```


### `ctx.erp.GetModuleIDDocumentType` (Python)

```
ctx.erp.GetModuleIDDocumentType(documentTypeId, docRecipientId)
```

ModuleID de fábrica para un tipo de documento y recipiente.

Ejemplo:

```
ctx.erp.GetModuleIDDocumentType(5, 2)  # factura de compra = 152
```


### `ctx.erp.GetModuleDLLName` (Python)

```
ctx.erp.GetModuleDLLName(moduleId)
```

Nombre de la DLL que atiende un módulo.

Ejemplo:

```
ctx.erp.GetModuleDLLName(21)  # 'Document'
```


### `ctx.erp.GetSecurityFunctionality` (Python)

```
ctx.erp.GetSecurityFunctionality(functionalityKey, moduleId)
```

¿El usuario activo tiene permiso sobre esa funcionalidad del módulo?

Ejemplo:

```
ctx.erp.GetSecurityFunctionality("Document.Delete", 21)
```


### `ctx.erp.GetUserCanElevatePrivileges` (Python)

```
ctx.erp.GetUserCanElevatePrivileges()
```

¿El usuario activo es administrador de Comercial?

Ejemplo:

```
ctx.erp.GetUserCanElevatePrivileges()
```


### `ctx.erp.GetDefaultValue` (Python)

```
ctx.erp.GetDefaultValue(key, countryId=1)
```

Lee un parámetro de engParameter (clave + país).

Ejemplo:

```
ctx.erp.GetDefaultValue("BROS_MI_CLAVE")
```


### `ctx.erp.SaveDefaultValue` (Python)

```
ctx.erp.SaveDefaultValue(key, value, description='', countryId=1)
```

Guarda (inserta o sobrescribe) un parámetro en engParameter.

Ejemplo:

```
ctx.erp.SaveDefaultValue("BROS_MI_CLAVE", "valor", "para qué sirve")
```


### `ctx.erp.GetLastDayMonth` (Python)

```
ctx.erp.GetLastDayMonth(date)
```

Último día del mes (28 a 31).

Ejemplo:

```
ctx.erp.GetLastDayMonth('2026-02-10')
```


### `ctx.erp.DateFromString` (Python)

```
ctx.erp.DateFromString(datePart, timePart)
```

Arma una fecha-hora desde 'yyyy-MM-dd' y 'HH:mm:ss'.

Ejemplo:

```
ctx.erp.DateFromString("2026-10-01", "12:30:00")
```


### `ctx.erp.ConvertDateTimeToUTC` (Python)

```
ctx.erp.ConvertDateTimeToUTC(dateTime)
```

Fecha-hora con desfase horario, como la usa Comercial en CFDI.

Ejemplo:

```
ctx.erp.ConvertDateTimeToUTC(fecha)
```


### `ctx.erp.GetFormatedDateValue` (Python)

```
ctx.erp.GetFormatedDateValue(date)
```

Fecha como texto 'yyyy-MM-dd HH:mm:ss'.

Ejemplo:

```
ctx.erp.GetFormatedDateValue(fecha)
```


### `ctx.erp.Pad` (Python)

```
ctx.erp.Pad(value, length, fillWith, alignment)
```

Rellena un texto a una longitud ('L' izquierda, 'R' derecha).

Ejemplo:

```
ctx.erp.Pad("7", 5, "0", "R")
```


### `ctx.erp.TruncateDouble` (Python)

```
ctx.erp.TruncateDouble(value, decimals)
```

Trunca (no redondea) a N decimales.

Ejemplo:

```
ctx.erp.TruncateDouble(12.98765, 2)
```


### `ctx.erp.GetSerialNumberPrefix` (Python)

```
ctx.erp.GetSerialNumberPrefix(serialNumber)
```

Parte alfabética de un número de serie.

Ejemplo:

```
ctx.erp.GetSerialNumberPrefix("ABC00123")
```


### `ctx.erp.GetSerialNumberNumValue` (Python)

```
ctx.erp.GetSerialNumberNumValue(serialNumber)
```

Parte numérica de un número de serie (conserva ceros).

Ejemplo:

```
ctx.erp.GetSerialNumberNumValue("ABC00123")
```


### `ctx.erp.GetFormatedXML` (Python)

```
ctx.erp.GetFormatedXML(xml)
```

XML con sangrías (legible).

Ejemplo:

```
ctx.erp.GetFormatedXML(xml)
```


### `ctx.erp.GetMaxValueField` (Python)

```
ctx.erp.GetMaxValueField(fieldName, tableName, where='')
```

Máximo de una columna entera, con filtro opcional.

Ejemplo:

```
ctx.erp.GetMaxValueField("DocumentID", "docDocument", "ModuleID=21")
```


### `ctx.erp.GetQRCode` (Python)

```
ctx.erp.GetQRCode(text)
```

Código QR del texto como PNG en base64.

Ejemplo:

```
png = ctx.erp.GetQRCode("https://ejemplo.com/x")
```


### `ctx.erp.GetCostLast` (Python)

```
ctx.erp.GetCostLast(productId)
```

Costo de la última compra del producto.

Ejemplo:

```
ctx.erp.GetCostLast(producto_id)
```


### `ctx.erp.RecalcCostComercial` (Python)

```
ctx.erp.RecalcCostComercial(productId)
```

Recalcula el costo comercial vigente del producto.

Ejemplo:

```
ctx.erp.RecalcCostComercial(producto_id)
```


### `ctx.erp.RecalcCostFiscal` (Python)

```
ctx.erp.RecalcCostFiscal(productId)
```

Recalcula el costo fiscal vigente del producto.

Ejemplo:

```
ctx.erp.RecalcCostFiscal(producto_id)
```


### `ctx.erp.SaveAllTaxesPayment` (Python)

```
ctx.erp.SaveAllTaxesPayment(financialOperationId)
```

Reconstruye el reparto de impuestos de un cobro/pago (opción conservadora).

Ejemplo:

```
ctx.erp.SaveAllTaxesPayment(operacion_id)
```


### `ctx.erp.RecalcPagosDocumento` (Python)

```
ctx.erp.RecalcPagosDocumento(documentId)
```

Reconstruye reparto de impuestos y saldos insolutos de los cobros de un documento.

Ejemplo:

```
ctx.erp.RecalcPagosDocumento(factura_id)
```


### `ctx.erp.AjustarSaldosInsolutos` (Python)

```
ctx.erp.AjustarSaldosInsolutos(financialOperationId, paymentWithDocumentId=0)
```

Recalcula saldo anterior/insoluto (complemento de pago) de un cobro.

Ejemplo:

```
ctx.erp.AjustarSaldosInsolutos(operacion_id)
```


## Referencia de SQL

#### Sentencias

### `SELECT` (SQL)

```
SELECT ...
```

Consulta de datos (devuelve filas).

Ejemplo:

```
SELECT DocumentID, Folio, Total FROM docDocument
WHERE DocumentID IN ({pIDs}) AND DeletedOn IS NULL
```


### `EXEC` (SQL)

```
EXEC <sp> @p = ...
```

Llama un procedimiento almacenado.

Ejemplo:

```
EXEC NombreDelSP @DocumentID = {pID}
```


### `UPDATE` (SQL)

```
UPDATE ...
```

Actualiza datos (bloqueado en SOLO LECTURA).

Ejemplo:

```
UPDATE docDocument SET Referencia = '{DATOS:Folio}'
WHERE DocumentID = {pID}
```


### `INSERT` (SQL)

```
INSERT ...
```

Inserta filas (bloqueado en SOLO LECTURA).

Ejemplo:

```
INSERT INTO miTabla (DocumentID, UserID) VALUES ({pID}, {pUserID})
```


### `DELETE` (SQL)

```
DELETE ...
```

Borra filas (bloqueado en SOLO LECTURA).

Ejemplo:

```
DELETE FROM miTabla WHERE DocumentID = {pID}
```


#### Tokens

### `{pID}` (SQL)

```
{pID}
```

Primer ID seleccionado en el grid (0 si no hay).

Ejemplo:

```
WHERE DocumentID = {pID}
```


### `{pIDs}` (SQL)

```
{pIDs}
```

Todos los IDs seleccionados, separados por coma.

Ejemplo:

```
WHERE DocumentID IN ({pIDs})
```


### `{pUserID}` (SQL)

```
{pUserID}
```

ID del usuario activo.

Ejemplo:

```
SET ModifiedUserID = {pUserID}
```


### `{pModulo}` (SQL)

```
{pModulo}
```

ID del módulo activo.

Ejemplo:

```
WHERE ModuleID = {pModulo}
```


### `{pEmpresa}` (SQL)

```
{pEmpresa}
```

Nombre de la BD de la empresa activa.

Ejemplo:

```
-- empresa activa: {pEmpresa}
```


### `{DATOS:x}` (SQL)

```
{DATOS:Campo}
```

Valor del campo en la fila seleccionada del grid.

Ejemplo:

```
WHERE Folio = '{DATOS:Folio}'
```

