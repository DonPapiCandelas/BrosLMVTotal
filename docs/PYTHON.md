# Botones en Python (v3.0)

Desde **v2.7.0**, un botón o la consola de BrosLMV pueden ejecutar **Python** (CPython
x64, fuera de proceso) con acceso al contexto vivo de CONTPAQi y a SQL de la empresa
activa. **Verificado en CONTPAQi Comercial PRO con datos reales (2026-06-27).**

> Python **no es un botón, consola ni instalador aparte**: es otro lenguaje para los
> **mismos** botones y la **misma** consola, con el **mismo** instalador.

---

## 1. Cómo se marca un script como Python

Dos formas (cualquiera vale):
- **Archivo `.py`**: `C:\BrosLMV\scripts\<EMPRESA>\<AppKey>.py` (o en `scripts\` raíz).
- **Marcador en la 1ª línea** (para scripts guardados en SQL `zzBrosScript` o en la consola):
  `# lang: python`  ·  `#py`  ·  `# broslmv:python`

Sin marcador y sin `.py`, el script se trata como **C#** (Roslyn), como siempre.

## 2. El contrato `ctx` en Python

```python
from broslmv import ctx
```

| Miembro | Qué hace |
|---|---|
| `ctx.user_id` / `ctx.module_id` / `ctx.empresa` / `ctx.app_key` | Contexto vivo del botón (`user_id` = usuario real de CONTPAQi) |
| `ctx.context()` | Todo el contexto como `dict` |
| `ctx.get_selected_ids()` | IDs seleccionados en el grid → `list[int]` |
| `ctx.query(sql, params=None)` | SELECT → `list[dict]` |
| `ctx.scalar(sql, params=None)` | Un valor |
| `ctx.execute(sql, params=None)` | DML → filas afectadas (`int`) |
| `ctx.msg(text, title="BrosLMV")` | Mensaje al usuario |
| `ctx.log(text)` | Log/auditoría |
| `ctx.progress(text, percent)` | Progreso |

**Valor de retorno:** el script devuelve un resultado asignando la variable global
**`result`** (se muestra al usuario / se registra).

```python
# lang: python
from broslmv import ctx
result = f"Empresa={ctx.empresa}, seleccionados={ctx.get_selected_ids()}"
```

## 2.1 `ctx.erp` desde Python (operaciones de CONTPAQi)

Python puede llamar **`ctx.erp.*`** con el mismo poder que C#: existencias, folios, precios,
recalcular, los ~562 miembros de XEngine. Como Python corre fuera de proceso, la llamada se
**relaya al addon** (que tiene el engine vivo) — igual que el SQL. Los nombres son los **mismos
que en C#** (PascalCase):

```python
# lang: python
from broslmv import ctx
pid = ctx.get_selected_ids()[0]
ex  = ctx.erp.GetProductStock(125, 0)          # existencia (producto, almacén; 0 = todos)
pv  = ctx.erp.GetSalePrice(125)                # precio de venta
letra = ctx.erp.GetTotalLetter(1234.50)        # importe con letra
result = f"stock={ex} precio={pv} | {letra}"
```

- Si el nombre coincide con un wrapper tipado de `ErpContext`, se usa ese (con sus correcciones,
  p. ej. el orden de `GetPriceWithTaxes`). Si no, cae a `ctx.erp.Call(nombre, *args)` (genérico).
- El catálogo completo de métodos es el mismo de C# → ver [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) §7
  y el panel de Referencias (pestaña C#).

## 2.2 Crear documentos desde Python

Hay **dos formas**, ambas verificadas (orden de compra en `Coctel_de_Ideas`):

**A) Helpers de alto nivel (recomendado)** — encapsulan los defaults del módulo:

```python
# lang: python
from broslmv import ctx
doc_id = ctx.erp.NuevoDocumento(183, 1, 162)   # módulo (OC), almacén, proveedor
ctx.erp.AgregarArticulo(doc_id, 1, 3, 100)     # documento, producto, cantidad, precio
ctx.erp.AgregarArticulo(doc_id, 2, 2, 250)
ctx.erp.RecalcCompleto(doc_id)                 # totales + costos (OBLIGATORIO al terminar)
ctx.erp.RefreshGrid()
result = f"OC creada: DocumentID={doc_id}"
```

`NuevoDocumento` crea el encabezado COMPLETO (24 columnas de `docDocument` con `ModuleID`,
`DocumentTypeID`, `DocRecipientID`, `FolioPrefix`, `Folio`, `DateDocument`, `MustBeSynchronized=1`,
`ExportID=1`, fechas universales, `LanguageID=3`, `CurrencyID=3`, `Rate=1`) + las **4 anclas 1:1**
(`docDocumentExt`, `docDocumentExtra`, `docDocumentCFD`, `docDocumentPaymentAgenda`).
**Tú** solo ajustas el perfil por módulo (`PaymentTermID`, `DepotIDFrom`, etc.) y agregas partidas
con `AgregarArticulo`; los **impuestos, kardex y costos** los genera `RecalcCompleto`/`AffectStockNEW`.

**B) Active-record genérico `ctx.nuevo("tabla")`** — para cualquier tabla (cimiento del no-code):

```python
# lang: python
from broslmv import ctx
it = ctx.nuevo("docDocumentItem")
it["DocumentID"] = doc_id
it["ProductID"]  = 1
it["Quantity"]   = 2
it["UnitPrice"]  = 100
new_id = it.guardar()        # INSERT; devuelve el ID (también en it.id)
# it.actualizar()            # UPDATE por la PK ya conocida
# it.eliminar()              # borrado lógico (DeletedOn = ahora)
```

`guardar()` detecta sola la columna identidad y respeta el modo **solo-lectura**. Útil tanto para
documentos como para catálogos.

> Módulos que **afectan inventario** (remisiones, recepciones): además de `RecalcCompleto`, llamar
> `ctx.erp.AffectStockNEW(doc_id)`. Una orden de compra **no** afecta inventario.

**C) SQL crudo + `ctx.erp` en cadena** — control total: tú escribes los INSERT y al final llamas
el recálculo, todo **secuencial en un mismo botón**. La clave: captura el ID con `OUTPUT INSERTED`.

```python
# lang: python
from broslmv import ctx
folio = ctx.erp.GetNextFolio(183, ctx.erp.GetFolioPrefix(183, 1), 1)   # folio: solo lo da el ERP
# Paso 1: INSERT encabezado y CAPTURA el id con OUTPUT INSERTED (via ctx.scalar)
doc_id = ctx.scalar("INSERT INTO docDocument (ModuleID, DocumentTypeID, DepotID, Folio, "
                    "LanguageID, CurrencyID, Rate, CreatedBy, CreatedOn, DateDocument, UserID) "
                    "OUTPUT INSERTED.DocumentID "
                    "VALUES (183, 40, 1, @f, 3, 3, 1, @u, GETDATE(), GETDATE(), 0)",
                    {"f": folio, "u": ctx.erp.UserId()})
# Paso 2: INSERT partidas (ctx.execute) usando doc_id
ctx.execute("INSERT INTO docDocumentItem (DocumentID, ProductID, Quantity, UnitPrice, Total, "
            "LineNumber, Date) VALUES (@d, 1, 3, 100, 300, 1, GETDATE())", {"d": doc_id})
# Paso 3: el ERP cierra (totales/impuestos). Va AL FINAL, ya con encabezado + partidas.
ctx.erp.RecalcCompleto(doc_id)
ctx.erp.RefreshGrid()
```

### Qué lenguaje usar (3 niveles)
| Necesitas… | Lenguaje |
|---|---|
| Reporte, lectura masiva, corrección de datos, `EXEC` de un SP, función de BD | **SQL** (pestaña SQL) |
| Crear/recalcular documentos, folios, precios, kardex, o mezclar SQL + lógica | **Python o C#** |

> **SQL puro NO ve `ctx.erp` ni el SDK** (`ctx.nuevo`): son objetos .NET/COM del addon, no viven en
> SQL Server. La cadena SQL→ERP **solo existe dentro de un botón Python o C#** (es Python/C# quien
> llama a SQL, nunca al revés).

## 3. SQL desde Python

El SQL corre por la **conexión viva de CONTPAQi** (relay al addon): mismo motor que el
C#, contra la **empresa activa**, **sin credenciales** en el host. Un `SELECT`/`EXEC`
funciona tal cual. Parámetros con `@nombre`:

```python
# lang: python
from broslmv import ctx
docs = ctx.query("SELECT * FROM docDocument WHERE BusinessEntityID=@be AND DeletedOn IS NULL",
                 {"be": 163})
result = f"{len(docs)} documentos del cliente"
```

**Importante (esquema real ComercialSP):**
- Tablas con prefijo **`doc*`** (documentos, p. ej. `docDocument`), **`org*`**
  (productos/clientes, p. ej. `orgProduct`), **`eng*`** (motor). **No** existe `adm*`.
- **Soft delete:** filtra `WHERE DeletedOn IS NULL` para ver solo lo vigente.
- PKs: `BIGINT IDENTITY`, nombradas `[Tabla]ID`. 0 FK / 0 triggers (integridad en la app).
- El esquema completo tiene ~500 tablas, vistas y procedimientos almacenados. Antes de escribir
  SQL nuevo, inspecciona la base de datos objetivo (`INFORMATION_SCHEMA` o el catálogo de
  Comercial) en vez de asumir nombres de columna por analogía.

## 4. Qué se necesita instalado

El instalador único deja en `C:\BrosLMV`: `bin\` (addon + `Google.Protobuf.dll`),
`host\` (BrosLMV.Host.exe x64), `runtimes\python\` (CPython embeddable) y `workers\`
(paquete `broslmv` + runner). El addon lanza el host por un Named Pipe seguro (ACL + token).

## 5. Límites actuales (en construcción)

- **UI durante la ejecución:** `ctx.msg`/`ctx.progress` se registran y el `result` se
  muestra al final; falta el relay para que se vean *mientras* corre el script
  (punto **C6c-UI**). `ctx.form` / `ctx.show_html` (UI moderna, decisión D9) vendrán después.
- **Arranque:** hoy se lanza un host por ejecución (≈1 s la 1ª vez). El host persistente
  es el punto **C6d**.

Ver el diseño del host en [`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md) y el historial en
[`CHANGELOG.md`](CHANGELOG.md).
