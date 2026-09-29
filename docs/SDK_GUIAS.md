# Guías del SDK de BrosLMV

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
