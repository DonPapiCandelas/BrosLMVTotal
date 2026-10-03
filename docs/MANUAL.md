# BrosLMV — Manual de uso y programación

> **Documento vivo** — API de `ctx` y `ctx.erp` para scripts C#, Python y SQL, y todo lo aprendido de Comercial en producción.
> La versión vigente del producto está en [`ESTADO.md`](ESTADO.md); qué función llegó en qué versión, en [`CHANGELOG.md`](CHANGELOG.md).
> Catálogo de funciones con parámetros y ejemplos: [`SDK_REFERENCIA.md`](SDK_REFERENCIA.md). Software libre (GPL-3.0).

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
10. [Ventanas, plantillas y documentos derivados](#10-ventanas-plantillas-y-documentos-derivados)
    - 10.1–10.3 Ventanas WinForms modeless y plantillas base
    - [10.4 Documentos derivados (N OC → 1 documento)](#104-documentos-derivados-n-órdenes-de-compra--1-documento-recepción--factura)
    - [10.5 Mapa de vínculos entre documentos, saldos y existencias a una fecha, ventas y cobranza](#105-mapa-de-vínculos-entre-documentos-genealogía)
    - [10.6 Operaciones financieras, conciliación bancaria y el candado de edición](#106-operaciones-financieras-conciliación-bancaria-y-el-candado-de-edición)
11. [Ejemplos de scripts](#11-ejemplos-de-scripts)
12. [Advertencias y buenas prácticas](#12-advertencias-y-buenas-prácticas) — incluye los hallazgos reales de producción: causas de "Division by zero", campos extra por empresa, gaps entre los builders y lo nativo, reglas para integraciones con el Runner
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
| **Referencias → pestaña "Tokens"** (desde v2.43.0) | `{pID}`, `{pIDs}`, `{pUserID}`, `{pModulo}`, `{pEmpresa}` + campos de la fila seleccionada — doble clic inserta el snippet correcto según el lenguaje del script |

El ciclo de trabajo es ágil:
**escribir → Ejecutar → leer error → corregir → Ejecutar**, sin cerrar CONTPAQi.

> **La pestaña "Errores" muestra el traceback completo** (desde v2.31.0 en Python, ya lo
> hacía C#): no solo el mensaje corto (`'BusinessEntityName'  [KeyError]`), sino también en
> qué línea y función de TU script ocurrió, y toda la cadena de llamadas si el error viene de
> una función que llamó a otra. No hace falta adivinar ni agregar `print()` de más para
> ubicar el problema — el error ya trae el "mapa" completo.

### 3.1 El árbol de scripts (desde v2.39.0)
- **★ Favoritos** (si tienes alguno marcado): clic derecho sobre un script → "☆ Marcar
  como favorito". Es preferencia **de este equipo** — no se comparte entre terminales ni
  entre empresas.
- **🕐 Recientes**: los últimos 8 scripts que abriste desde esta Consola, en este equipo.
- **Scripts — &lt;empresa&gt;**, agrupados por **Categoría** — un texto libre que tú le
  pones a cada script con "Categorizar…" (clic derecho). Ponle el nombre que quieras
  ("Reportes", "Compras", lo que te haga sentido) — sin categoría cae en "Sin categoría".
  Los favoritos, además de aparecer arriba en su propio grupo, se marcan con "★ " en su
  nombre aquí también.
- **Plantillas**, al final.

**Todo el árbol empieza contraído** cada vez que abres la Consola — Favoritos, Recientes,
Scripts y Plantillas. Tú decides qué desplegar. El cuadro de búsqueda de arriba filtra
dentro de todos los grupos a la vez y los expande automáticamente mientras hay texto (si
no, los resultados quedarían escondidos dentro de un grupo cerrado).

> Se probó agrupar por módulo de Comercial (v2.38.0) primero y no sirvió — el módulo no
> refleja cómo se organizan los botones en la práctica. La categoría manual reemplazó esa
> idea.

Un paquete `.bros` (§ "Paquetes .bros" más abajo) lleva la categoría consigo al
exportarlo — si ya clasificaste el script en la empresa de origen, no hay que volver a
categorizarlo en la empresa destino.

---

## 4. Cómo crear un botón nuevo

### Opción recomendada — asistente «Crear botón…» (desde v2.95.0)

En la Consola: clic secundario sobre el script → **Crear botón…** (o **Más opciones → Nuevo botón…** para empezar por el botón). Eliges nombre, descripción,
ícono, pestaña/sección, módulos y quién lo ve, con vista previa del ribbon; el asistente da de alta el botón sin SQL ni claves a mano. Guía completa:
[`CREAR_BOTON.md`](CREAR_BOTON.md). Las opciones de abajo (SQL a mano, Gestor de Ribbon) siguen funcionando pero ya no son necesarias.

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

### Dónde viven tus scripts (y qué pasa si borro algo)

- **La fuente de verdad es SQL.** Cada script se guarda en la tabla `zzBrosScript` de la empresa (con su historial en `zzBrosScriptHist`), por eso todas las terminales de esa
  empresa ven lo mismo. Los archivos de `C:\BrosLMV\scripts` **no son la biblioteca**: son plantillas y semillas que instala BrosLMV. Si se borran, **no pasa nada** con tus scripts.
- **El nombre del script es su clave:** el script `crear_doc_desde_xml` lo ejecuta el botón `BrosLMV.crear_doc_desde_xml`. Al guardar, la Consola muestra la clave que va a usar.
  **No importan las mayúsculas:** `broslmv.crear_doc_desde_xml`, `BrosLMV.Crear_Doc_Desde_XML` y `BROSLMV.CREAR_DOC_DESDE_XML` son el mismo botón y el mismo script.
- **El lenguaje** (C#, Python o SQL) se elige con el botón *Lenguaje* de la barra; la Consola escribe la línea `lang:` por ti.
- **Respaldo:** *Más opciones → Respaldar todos los scripts…* crea un `.bros` por script; con clic secundario sobre uno, *Exportar paquete (.bros)…* exporta solo ese.
  Un `.bros` se importa con *Importar paquete…* en cualquier empresa o equipo.
- **Si se borra un script en SQL:** se recupera del historial de versiones (clic secundario → *Historial de versiones…*) o importando su `.bros`.

### Opción visual — Gestor de Ribbon (desde v2.41.0, sin SQL a mano)

El paso 5 de arriba (`plantilla_crear_boton.sql`) sigue funcionando, pero para no tener que
tocar SQL cada vez hay un botón propio: **Gestor de Ribbon** (junto a "Consola BrosLMV" en
la pestaña "Soluciones LMV"). Con eso puedes, sin escribir una línea de SQL:

- **Ver estructura del ribbon** — todas las pestañas/secciones/botones, resaltando en azul
  los que son de BrosLMV (los únicos que "Editar" y "Mover" pueden tocar — nunca toca
  botones nativos de CONTPAQi).
- **Crear una pestaña o sección nueva** para organizar tus botones.
- **Editar un botón** ya creado: nombre, ícono, en qué módulo aparece.
- **Mover un botón** a otra sección.

Después de cualquier cambio hay que **reiniciar CONTPAQi** para verlo reflejado en el
ribbon (los cambios ya quedaron guardados en la base — solo falta que Comercial vuelva a
leer la estructura).

### Opción sin código — "Nueva acción" (desde v2.44.0/2.48.0, asistente)

Las dos opciones de arriba requieren escribir código (C#/Python/SQL) o al menos saber SQL.
**"Nueva acción"** no requiere ninguna de las dos cosas — eliges qué debe hacer el botón de
una lista y llenas un formulario. Vive en la Consola, botón **"Más opciones" → "Nueva
acción"**.

Hoy hay 2 acciones disponibles ("recetas"):

| Receta | Qué hace | Cuándo usarla |
|---|---|---|
| **Ejecutar SQL con tokens** | Corre una consulta que tú escribes, sustituyendo tokens como `{pID}` por el documento seleccionado | Reportes rápidos, consultas puntuales — lo más simple posible |
| **Crear documento a partir de otro** | Crea un documento nuevo (ej. una Orden de Compra) con encabezado y partidas, usando el mismo motor que Comercial | Automatizar un flujo tipo "de esta Requisición, genera la OC" |

#### Ejemplo 1 — "avísame el total de un documento" (con "Ejecutar SQL con tokens")

El ejemplo más simple posible, para entender el mecanismo:

1. Abre la Consola → **Más opciones → Nueva acción**.
2. En "Tipo de acción" elige **"Ejecutar SQL con tokens"**. Debajo aparece una explicación
   de qué hace.
3. Clic en **"Llenar con este ejemplo"** — llena el campo SQL con
   `SELECT Folio, Total FROM docDocument WHERE DocumentID = {pID}`. Ese `{pID}` se
   sustituye solo por el documento que tengas seleccionado en Comercial cuando el botón se
   ejecute — no lo escribes tú cada vez, es automático.
4. En "Nombre visible" pon algo como `Ver folio y total`. En "Clave interna (AppKey)" pon
   `VER_FOLIO_TOTAL` (sin espacios).
5. **Guardar acción**.
6. Ve al **Gestor de Ribbon** (ver arriba) y da de alta el botón `BrosLMV.VER_FOLIO_TOTAL`
   donde quieras que aparezca (o créalo con el SQL de siempre, `plantilla_crear_boton.sql`
   — "Nueva acción" solo guarda el contenido del botón, no lo pone en el ribbon).
7. Reinicia CONTPAQi, selecciona un documento, haz clic en tu botón nuevo.

#### Ejemplo 2 — "crear una OC desde una Requisición" (con "Crear documento a partir de otro")

Más avanzado, pero sigue sin escribir código:

1. **Antes de empezar, consigue 3 datos con una consulta SQL** (esto sí requiere saber el
   ID de las cosas, no hay forma de evitarlo sin un buscador visual — trabajo futuro):
   - El **ID del módulo destino**: `183` para Orden de compra (ver la tabla completa en
     §7.2 más abajo).
   - El **ID del almacén**: `SELECT DepotID, DepotName FROM orgDepot`.
   - El **ID del proveedor**:
     `SELECT BusinessEntityID, OfficialName FROM orgBusinessEntity WHERE ... `.
2. Abre la Consola → **Más opciones → Nueva acción** → elige **"Crear documento a partir
   de otro"**.
3. Clic en **"Llenar con este ejemplo"** para ver el formato exacto que espera cada campo
   — en particular, el campo **"Partidas"** espera JSON escrito a mano:
   `[{"productId": 1, "cantidad": 5, "precio": 250, "costo": 200}]` (puedes poner varias
   partidas separadas por coma dentro de los corchetes). **Esto es una limitación conocida
   de esta primera versión** — no hay todavía una tabla visual para llenar partidas, se
   escribe el JSON a mano. Ver `docs/RECETAS_NOCODE.md` §2.4 para el detalle.
4. Reemplaza los valores del ejemplo por tus IDs reales (módulo, almacén, proveedor,
   productos).
5. Nombre visible + AppKey, **Guardar acción**, dar de alta en el ribbon (Gestor de
   Ribbon), reiniciar CONTPAQi.

> **Si el módulo que necesitas no es 183 (OC) ni 202 (Entrada de almacén):** la receta te
> va a avisar con un error claro ("no hay EstructuraDocumento registrada para ModuleID=X")
> en vez de fallar en silencio o crear un documento a medias. Agregar un módulo nuevo
> requiere tocar código (`src/EstructurasDocumento.cs`) — pídeselo a quien mantenga el
> proyecto.

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
| `costo` | `double` | `-1` | Costo de entrada (<0 = no setear; >=0 puebla CostPrice). **Ojo:** "no setear" NO significa "Comercial lo calcula después" — la columna `CostPrice` de `docDocumentItem` tiene DEFAULT `0`, así que dejarlo en `-1` (el default de este parámetro) deja el costo real en `0` igual que si hubieras pasado `0` a propósito, con el mismo riesgo de "Division by zero" nativo si el producto no tiene costo por otra vía (ver "💥 Division by zero" en §12, causas 1 y 3). Si el producto puede quedar sin costo real, resuelve uno explícito antes de llamar (`ctx.erp.GetCostPriceComercial` — ver §6.7) en vez de confiar en el default. |
| `taxTypeIdOverride` | `int` | `-1` | Impuesto a usar en vez del de `orgProduct.TaxTypeID` (<0 = usar el del producto). Útil para un combo de "Impuesto" editable en la UI — ver "Ejemplo Premium · Orden de Compra". |
| `descuentoPerc` | `double` | `0` | Descuento de la partida, en **fracción** (0.05 = 5%, no "5") |
| `deliverDocumentItemId` | `int` | `0` | (v2.22.0) `DocumentItemID` del documento ORIGEN que esta partida está surtiendo — p. ej., la partida de la Orden de Compra que una Recepción de Compra está recibiendo. 0 = no aplica. Ver "Ejemplo Premium · Recepción de Compra" y MANUAL.md §10.4 — para Factura de Compra el campo real es OTRO (`SourceDocumentItemID`, fijado aparte por SQL, no por este parámetro). |
| `lote` | `string` | `null` | (v2.22.0) Solo si `orgProduct.UseLot=1`. **Ojo:** esto llena el campo simple `docDocumentItem.Lot` — NO es lo mismo que las tablas de detalle `docDocumentLot`/`docDocumentSerialNumber` (que soportan varios lotes/series por partida, con caducidad); para eso, ver el capturador de la plantilla de Recepción de Compra, que hace el INSERT directo a esas tablas. |
| `serialNumber` | `string` | `null` | (v2.22.0) Solo si `orgProduct.UseSerialNumber=1`. Mismo comentario que `lote`: un solo valor aquí, no reemplaza la captura de múltiples series. |

#### `ctx.erp.AgregarSerie(documentId, documentItemId, productId, serialNumber, depotId, quantity=-1)` — asignar una serie a una partida ya creada

(v2.90.0, `quantity` agregado en v2.91.0) Inserta directo en `docDocumentSerialNumber` — para
partidas con varias series se llama una vez por serie. **`quantity` importa según la dirección
del documento:** `-1` (el default) es la convención correcta para documentos de SALIDA
(Factura, Remisión, Pedido — la serie "sale" del almacén); un documento de ENTRADA (Recepción
de Compra, Factura de Compra) necesita `quantity=1`, o la serie queda registrada como si
hubiera salido del almacén cuando en realidad entró (hallazgo real de producción, corregido en
v2.91.0 — antes `quantity` estaba fijo en `-1` sin importar el tipo de documento).

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

> 🔄 **Todo script que cambie lo que se ve en un grid termina con `ctx.erp.RefreshGrid()`** — ver **«Refrescar el grid (estándar)»**
> en la sección de advertencias. Desde v2.94.0 `RefreshGrid()` sí refresca y conserva tu fila; antes fallaba en silencio.

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

> **Nota sobre contabilidad:** la factura de compra genera póliza al guardarla **en la ventana** de Comercial, pero un documento creado por script
> **no** la genera: pídesela al motor nativo después de `Save` (`Accounting.clsMain.CrearPolizasDocumento`, ver §12 «Un documento creado por script no genera póliza»).

### 7.8 Receta — Traspaso entre almacenes (ModuleID=204)

```csharp
int depotOrigen = 1;
int depotDestino = 2;
int doc = ctx.erp.NuevoDocumento(204, depotOrigen);

// Captura nativa (laboratorio): DepotID = origen, DepotIDFrom = origen, DepotIDTo = destino, PaymentTermID = 0 y AMBAS fechas de entrega con valor.
ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom={depotOrigen}, DepotIDTo={depotDestino}, PaymentTermID=0,
        DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
    WHERE DocumentID={doc}");

ctx.erp.AgregarArticulo(doc, 20, 5);  // 5 unidades del producto 20

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);  // kardex: -5 en origen, +5 en destino (2 filas por partida)
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
return "Traspaso creado: doc=" + doc;
```

> ⚠️ **Sin `DateDelivery`/`DateDocDelivery` el traspaso queda «en tránsito»**: solo genera el kardex de **salida** del origen y nada entra al destino.
> Confirmado por captura nativa en el laboratorio (un traspaso nativo genera 2 kardex por partida; la réplica sin esas fechas generó 1). Un caso de producción
> reportó que el módulo «solo restaba en el origen»: muy probablemente era este mismo caso de traspaso en tránsito (no se comprobó la causa en ese caso).
> **Verifica siempre** en tu empresa que salgan **2** filas de `orgProductKardex` por partida. Si aun con las fechas solo hay una, usa una Salida (203) en el
> origen + una Entrada (202) en el destino — ver §12 «Gaps reales entre los builders y el comportamiento nativo».

---

## 8. Crear catálogos

Los catálogos (cliente, proveedor, producto, almacén, proyecto) **no tienen creador
nativo en `ctx.erp`** — se crean con INSERT directo vía `ctx.NonQuery`.

### 8.1 Cliente / Proveedor (modelo entidad/rol)

> **Corregido (2026-07-30, harness T4.1):** `BusinessEntityName` y `FiscalRegimeID` **no
> existen** en `orgBusinessEntity` en la versión de Comercial instalada en
> `localhost\compac` (confirmado creando un proveedor real vía `ctx.erp` headless para el
> caso de humo "crear OC" — `Invalid column name 'BusinessEntityName'`). El nombre va en
> `CommercialName`; no hay columna de régimen fiscal en esta tabla en esta versión. Igual
> que en 8.2: revisa `sys.columns` de tu instancia antes de asumir el esquema de abajo tal
> cual.

```csharp
// 1. Crear la entidad base
int beId = (int)(long)ctx.Scalar(@"
    INSERT INTO orgBusinessEntity (CommercialName, BusinessEntityKey, OfficialName,
        CreatedBy, CreatedOn, UserID)
    OUTPUT INSERTED.BusinessEntityID
    VALUES ('Mi Cliente SA', 'CLI-001', 'Mi Cliente SA de CV',
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

> **Corregido (2026-07-30, harness T4.1):** la columna `ProductInventory` de abajo **no
> existe** en `orgProduct` en la versión de Comercial instalada en `localhost\compac`
> (confirmado con `sys.columns`; `ComercialSP` lo rechaza con `Invalid column name
> 'ProductInventory'`). Se quitó del INSERT. Si tu versión de Comercial sí la trae, revisa
> `sys.columns` antes de asumir que la receta de abajo aplica tal cual — el esquema de
> `orgProduct` varía entre versiones.

```csharp
int prodId = (int)(long)ctx.Scalar(@"
    INSERT INTO orgProduct (ProductKey, ProductName, ProductTypeID, TaxTypeID,
        Unit, ClaveUnidad, ObjetoImpuesto, ClaveProdServ,
        ProductBuy, ProductSale, UseLot, UseSerialNumber,
        CreatedBy, CreatedOn, UserID)
    OUTPUT INSERTED.ProductID
    VALUES ('PROD-001', 'Mi Producto', 1, 2,
        'PZA', 'H87', '02', '43231500',
        1, 1, 0, 0,
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

- **Sin transacciones** en Python (`ctx.OpenConn` no existe).
- **Encoding:** asegurar UTF-8 en datos con acentos/Ñ.

### 9.4 `ctx.show_html`, `ctx.show_html_formulario` y `ctx.dashboard` (solo Python)

> **C#:** además de `ctx.ShowHtml` y `ctx.ShowHtmlFormulario` existe `ctx.ShowHtmlModeless(html, titulo, ancho, alto, alMensaje)`: ventana HTML que **no bloquea** (el script termina y la ventana sigue viva; los mensajes de la página llegan a `alMensaje` en el hilo de Comercial). Úsala si la ventana debe seguir abierta mientras se abren documentos o se llama a `ctx.erp`: con `ShowHtmlFormulario` (bloquea) abrir un documento da el aviso de XEngine «the other application is busy».

- `ctx.show_html(html, title="BrosLMV", width=800, height=600, modal=True)` — ventana con
  HTML/CSS/JS real (WebView2), embebida en CONTPAQi. Desde v2.24.0. **De una sola vía**:
  la ventana se muestra pero no hay forma de que le mande datos de vuelta al script.
- `ctx.show_html_formulario(html, title="BrosLMV", width=900, height=700, timeout_ms=600000)
  → dict` — **desde v2.54.0**, el mismo WebView2 pero de **2 vías**: bloquea el script
  hasta que la página llama `window.chrome.webview.postMessage(JSON.stringify({...}))`
  (o el usuario cierra la ventana sin enviar nada). Regresa ese diccionario con
  `"submitted"` agregado (`True` si se envió algo, `False` si se cerró sin enviar).
  Pensado para formularios HTML reales que crean/guardan algo, no solo muestran —
  la alternativa nativa es `ctx.form()` (WinForms), que ya hacía esto pero sin HTML/CSS
  libre.

  ```python
  r = ctx.show_html_formulario("""
      <input id="n" placeholder="Nombre">
      <button onclick="window.chrome.webview.postMessage(JSON.stringify({nombre: n.value}))">
          Guardar
      </button>
  """)
  if r["submitted"]:
      ctx.msg("Recibido: " + r["nombre"])
  ```

  > ⚠️ **Si el formulario puede tardar más de 2 minutos en llenarse (lo normal para un
  > humano), agrega `# timeout: 1800` (segundos) en las primeras líneas del script.** El
  > `timeout_ms` de `show_html_formulario` solo controla CUÁNTO ESPERA LA VENTANA; el
  > timeout general del script completo (2 min por default) es independiente y puede
  > tronar primero si no lo amplías — confirmado en pruebas reales contra el sandbox.
- `ctx.dashboard(title, data, columns=None, width=1000, height=700, modal=True)` — dashboard
  completo (tabla ordenable, buscador, paginación, exportar a Excel) a partir de una lista
  de dict, sin escribir HTML/CSS/JS ni crear carpeta de assets por script. Desde v2.34.0.
  Guía completa: [`DASHBOARDS_HTML.md`](DASHBOARDS_HTML.md).

> **`ctx.form()` vs `ctx.show_html()` en `BrosLMV.Runner` (headless, T4.1, 2026-07-30):**
> **`ctx.form()` SIEMPRE bloquea** (`RenderUiForm` en `src\HostClient.cs` usa
> `frm.ShowDialog()` síncrono) — headless no hay nadie para cerrarlo, así que la ejecución
> se queda colgada hasta que la revienta el timeout de seguridad (2 min por default, o el
> que se haya puesto con `# timeout: N`). **No uses `ctx.form()` en un botón `# job:
> safe-offline`.** `ctx.show_html()` es distinto: `RenderUiHtml` regresa en cuanto la
> página termina de CARGAR, no espera a que se cierre la ventana (queda abierta en su
> propio hilo en segundo plano) — sí es seguro headless, confirmado con el caso de humo
> `build\humo\casos\05_show_html.ps1`.

> ⚠️ **`ctx.nuevo("docDocument")` NO crea un documento válido.** No genera folio, anclas ni defaults.
> Para documentos usar siempre `ctx.erp.NuevoDocumento(...)`. `ctx.nuevo` solo para tablas simples.

---

## 10. Ventanas, plantillas y documentos derivados

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
   **Confirmado independientemente:** una integración externa que no conocía
   `engPaymentTermDetail` tuvo que parsear los DÍAS de crédito a mano desde el texto del
   nombre de la condición de pago (`engPaymentTerm.PaymentTermName`, p. ej. "30 DIAS"/
   "2 SEMANAS"/"12 Meses" — `engPaymentTerm` no tiene columna de días) — confirma que
   `engPaymentTermDetail` es la fuente estructurada correcta y evita ese parseo frágil.
   **Cómo se lee `engPaymentTermDetail` (confirmado contra el catálogo nativo `engRefCombo`
   `CboGroupName='PaymentTermPeriod'` y contra los nombres reales de las condiciones):** una fila
   por parcialidad; `PaymentPerc` = % del total; **`PaymentPeriodID` = la UNIDAD** (1=día,
   2=semana, 3=mes, 4=trimestre, 5=semestre, 6=año); **`PaymentUnit` = cuántas de esa unidad**.
   `PaymentPeriod` **no** es el plazo (casi siempre vale 0). Ejemplos reales: "30 DIAS" =
   `PaymentUnit=1, PaymentPeriodID=3` (+1 mes); "60 DIAS" = `2, 3`; "2 SEMANAS" = `2, 2`; "50%-50%"
   = dos filas `0, 1` (hoy) y `3, 3` (+3 meses). Hasta v2.92.0 las 6 plantillas de Factura de
   Compra de fábrica leían `PaymentUnit` como unidad y `PaymentPeriod` como cantidad — dejaban
   "30/60/90 DIAS" venciendo el mismo día del documento (corregido en v2.93.0).
   **Ojo en JavaScript (ventanas WebView2):** para armar `YYYY-MM-DD` de un vencimiento no uses
   `toISOString()` — convierte a UTC y en México (UTC-6) recorre la fecha un día hacia atrás;
   arma la cadena con `getFullYear()/getMonth()/getDate()` locales.
3. Antes de escribir un documento derivado nuevo, **verifica el perfil real de encabezado contra
   una base de datos de pruebas** (crea el documento equivalente a mano en Comercial y compara
   los valores que quedan en `docDocument`/`docDocumentItem`) en vez de asumirlo por analogía con
   otro documento. Recepción de Compra y Factura de Compra, por ejemplo, difieren en varios campos
   del encabezado (`StatusDeliveryID`, `DepotIDFrom`, etc.) pese a parecerse mucho en el flujo.
4. **Facturación PARCIAL (por cantidad, no todo-o-nada) necesita revalidar justo antes de crear.**
   Si la ventana deja elegir cuánto de cada partida facturar (no solo cuáles), el pendiente real
   pudo cambiar mientras el usuario tenía la ventana abierta (otra persona facturó la misma OC
   mientras tanto). Patrón validado: recalcular el pendiente de cada partida elegida con la MISMA
   consulta SQL que armó la ventana, justo antes del `NuevoDocumento`/`AgregarArticulo`, y abortar
   sin crear nada si alguna cantidad pedida ya no cabe en lo pendiente — en vez de crear parcial o
   dejar que CONTPAQi lo acepte silenciosamente por encima de lo ordenado.

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

### 10.5 Mapa de vínculos entre documentos (genealogía)

Para reconstruir la cadena de un documento (qué lo originó y qué salió de él) — reportes de
trazabilidad, "cuánto falta por recibir/facturar", validaciones antes de cancelar — estas son
las columnas que Comercial usa de verdad. Verificado contra datos de producción (compras) y
contra las capturas nativas de `Entrenamiento` (ventas):

| Relación | Nivel | Columna | Evidencia |
|---|---|---|---|
| Recepción de Compra (184) → OC (183) | encabezado | `docDocument.SourceDocumentID` = OC | 51/51 recepciones en producción |
| Recepción de Compra (184) → OC (183) | partida | `docDocumentItem.DeliverDocumentItemID` = partida de la OC (`SourceDocumentItemID` queda en 0) | 99/99 partidas |
| Factura de Compra (152) → OC (183) | partida | `docDocumentItem.SourceDocumentItemID` = partida de la OC | 4/4 partidas |
| Factura de Compra (152) → OC | encabezado | `SourceDocumentID` **en 0 en el nativo**; las plantillas de BrosLMV ponen la primera OC (solo informativo) | ambos coexisten sin problema |
| Remisión (157) → Pedido (967) | encabezado | `docDocument.SourceDocumentID` = Pedido | capturas nativas |
| Remisión (157) → Pedido (967) | partida | `SourceDocumentItemID` = partida del Pedido | 24,080 partidas en producción (*) |
| Pedido (967) → Cotización (155) | encabezado + partida | `SourceDocumentID` / `SourceDocumentItemID` → Cotización | ~6,800 pedidos / ~20,000 partidas |
| Venta/Ticket (158 y clones) → Cotización (155) | encabezado + partida | `SourceDocumentID` / `SourceDocumentItemID` | ~5,600 tickets |
| Factura de cliente (21 y clones) → Venta/Ticket (158) | encabezado + partida | `SourceDocumentID` / `SourceDocumentItemID` → Ticket (factura de un ticket) | ~19,500 facturas / ~50,000 partidas |
| Nota de crédito (142 y clones) → Factura (21) o Devolución (159) | encabezado + partida | `SourceDocumentID` / `SourceDocumentItemID` | ~1,300 notas |
| Devolución (159) → Remisión (157) | partida | `SourceDocumentItemID` (encabezado → Pedido) | ~730 partidas |
| Nota de crédito de proveedor (187) → Factura de Compra (152) | partida | `SourceDocumentItemID` | (pocas) |
| Entrada/Salida (202/203), OC, Cotización | encabezado | `SourceDocumentID` = 0 | capturas nativas + producción |
| Cualquiera | encabezado | `docDocument.DestinationDocumentID` — el origen apunta a su destino. **Comercial la LEE:** seis vistas nativas la consultan (globalización de ventas a factura global, `vwLBSAssignarFacturaCompraAOrdenDeCompra`…) y esas mismas vistas existen en una base creada de fábrica, así que son del propio producto. **Quién la ESCRIBE de forma nativa no está demostrado:** en dos empresas de clientes (que tienen scripts propios) 97 y 221 documentos la traen (OC → Factura de compra con el vínculo en ambos lados, y varias Recepciones → una sola venta) y no se puede distinguir si lo escribió una función nativa o un script. Es el único vínculo de encabezado que admite **varios orígenes → un destino** (el `SourceDocumentID` guarda solo uno). Léela en ambos lados; no la escribas sin necesidad. Plantilla que la toma en cuenta: `TRAZABILIDAD_DOCUMENTO` | vistas nativas (base de fábrica) + datos de clientes (2026-10-02) |
| Pago/cobro → documento | aplicación | `docDocumentPayment` (`DocumentID`, `FinancialOperationID`, `PaymentWithDocumentID`, `Amount`, `AmountPaidCurrency`, `SaldoAnterior`, `SaldoInsoluto`) | esquema; ver `MOTOR_ASIENTOS_CONTABLES.md` |

Reglas para recorrerla:
- Recorre en ambos sentidos por las tres columnas (`SourceDocumentID`, `SourceDocumentItemID`,
  `DeliverDocumentItemID`) — una búsqueda en anchura desde el documento que te interesa.
- Filtra `DeletedOn IS NULL` en **cada** salto (documento y partida) y considera
  `CancelledOn` aparte: un documento eliminado sigue ahí con su vínculo, y además pierde su
  `docDocumentCFD` (ver §12 "`Delete` vs `CancelDocument`").
- Una Recepción nativa solo admite un `SourceDocumentID`: para saber de qué OC viene cada
  partida cuando se recibieron varias, usa `DeliverDocumentItemID`, no el encabezado.
- **Regla general, confirmada con ~130,000 partidas de dos empresas en producción:** toda
  conversión de un documento en otro liga la partida con `SourceDocumentItemID`; **la única
  excepción es la Recepción de Compra**, que usa `DeliverDocumentItemID` (la "entrega" de la
  OC). (*) En la empresa medida, parte de las Remisiones se crearon con un script propio que
  escribe `SourceDocumentItemID` — consistente con el resto de las conversiones nativas, pero
  la evidencia de esa fila no es 100% nativa.
- **Clasifica por `engModule.ModuleIDBase`, no por `ModuleID`.** Las empresas clonan módulos
  nativos para separar series, formas de pago o sucursales (vistos: 4 clones de Factura de
  cliente por forma de pago, 2 clones de Venta/Ticket, 4 de Nota de crédito, 3 de OC, clones
  del módulo de Cobro por banco). Cada clon trae su propio `ModuleID` pero comparte
  `ModuleIDBase` con el nativo (21, 158, 142, 183…) y se comporta igual. Un reporte o script
  que filtre `ModuleID = 21` se pierde todas las facturas de los clones.

**Saldos a una fecha de corte (cuentas por pagar/cobrar históricas).** `docDocument.Balance`,
`TotalPaid` y `StatusPaidID` son una foto **del presente**: no pueden responder "¿cuánto se
debía al 30 de agosto?". Para eso reconstruye el saldo desde los hechos: `docDocument.Total`
menos la suma de `docDocumentPayment.Amount` (vigentes) con `DateOperation <= corte`, y los
vencimientos desde `docDocumentPaymentAgenda.DatePayment`. Confirmado en un reporte de CxP en
producción: con corte = hoy, el cálculo coincide factura por factura con `Balance`/`TotalPaid`.
Consecuencia práctica: para un reporte con fecha de corte **no filtres por `Balance <> 0`** —
una factura pagada hoy pudo tener saldo en la fecha de corte; carga el periodo completo
(medido: ~2,000 facturas y ~2,000 pagos en 12 meses caben sin problema en `ctx.show_html`
comprimido, ver `DASHBOARDS_HTML.md`) y calcula el saldo en el navegador para que cambiar la
fecha de corte sea instantáneo. Y en cuentas por pagar **incluye Gastos (módulo 242)** además
de Facturas de Compra (152): es un módulo nativo con la misma agenda de pago y los mismos pagos
aplicados.

**Existencias a una fecha** (el equivalente de inventario): `SUM(orgProductKardex.Quantity)`
con `DateTransaction <= corte`, `orgProductKardex.Cancelled = 0`, documento vigente y no
cancelado (`DeletedOn`/`CancelledOn IS NULL`), partida vigente, y sin servicios
(`ProductTypeID <> 4`). Inventario inicial / entradas / salidas de un periodo salen del mismo
`SUM` separando por fecha y por signo de `Quantity`. Las filas de kardex de una OC traen
`Quantity = 0` (solo compromiso, `QuantityToBeDelivered`), así que no alteran la existencia.

**Notas de crédito, devoluciones y cobranza (clientes).** Confirmado en una empresa con
~19,000 facturas de cliente al año:
- Tipos por `DocumentTypeID` (con `DocRecipientID=1`): **5 = Factura**, **6 = Nota de
  Crédito**, **4 = Devolución**. Las Devoluciones no se "aplican" como las NC (su `Balance`
  nunca llega a 0) — trátalas como un concepto aparte.
- **Una NC aplicada a una factura es una fila de `docDocumentPayment` con
  `PaymentWithDocumentID` = `DocumentID` de la NC.** Para la factura cuenta exactamente igual
  que un pago en efectivo (mismo `Amount`, misma fecha) — su saldo se calcula igual sin
  importar si la "paga" dinero o una NC, y puede mezclar ambos. El saldo pendiente de aplicar
  de la propia NC = `NC.Total − SUM(Amount WHERE PaymentWithDocumentID = NC.DocumentID)`, que
  coincide exacto con `NC.Balance` (reconciliado 15/15).
- Las NC traen una fila de agenda de pago como cualquier documento, pero **no tienen
  vencimiento real** — no les apliques semáforo de vencido/por vencer.

**Totales, costo y margen de un documento de venta:**
- `docDocument.SubTotal` es **bruto, antes de descuento**: `SubTotal − TotalDiscount + TotalTax
  = Total` (verificado en 6,499 documentos de venta; en compras resta además
  `TotalRetention`). La venta neta es `SubTotal − TotalDiscount`.
- Costo real de lo vendido = `docDocumentItem.CostPrice × Quantity` (costo capturado en la
  partida al momento de la venta). Si la empresa arrastra existencias negativas históricas,
  los costos del catálogo (`orgProduct.CostPrice`/`CostPriceComercial`) y el libro nativo
  quedan contaminados — un reporte de margen serio puede necesitar su propio cálculo PEPS.
- Para un costo "de referencia" por producto, toma Recepciones de Compra + Entradas de
  Almacén; **no mezcles Facturas de Compra (152)**: a veces facturan en otra unidad que la de
  inventario (tonelada vs. bulto), lo que da costos unitarios absurdos.
- **El sistema no guarda con qué lista de precios se vendió cada partida** — no hay forma de
  reconstruir "cuánto se vendió a precio de mayoreo vs. público" desde `docDocumentItem`.

**No cuentes dos veces la misma venta.** Una venta puede pasar por Pedido → Ticket (158) →
Factura (21), o facturarse directo. En un reporte de ventas cuenta solo el **documento
terminal** de cada cadena Ticket↔Factura (el que no tiene un documento hijo vigente cuyo
`SourceDocumentID` apunte a él) — así un ticket ya facturado no aparece también por
separado. Y verifica en la empresa si la Remisión es un paso hacia la factura o un documento
de surtido en paralelo (en la empresa medida nunca era origen de una factura, se excluía del
conteo).

### 10.6 Operaciones financieras, conciliación bancaria y el candado de edición

**Perfil real de `docFinancialOperation`** (cobros, pagos, traspasos) — medido sobre ~105,000
operaciones de una empresa en producción. Identifica el tipo de operación por
`DocRecipientID` + `DocumentTypeID` + `DebitCreditCoef` (o por `ModuleIDBase`), nunca solo por
`ModuleID`: la empresa medida tenía el módulo de Cobro clonado por banco y por forma de pago.

| Operación | ModuleID nativo | DocRecipientID | DocumentTypeID | DebitCreditCoef |
|---|---|---|---|---|
| Cobro a cliente (y sus clones, p. ej. por banco / REP) | 248 | 1 | 31 | +1 |
| Pago a proveedor | 247 | 2 | 32 | −1 |
| Traspaso entre cuentas | 362 | 0 | 28 | **dos filas**: −1 origen y +1 destino |
| Pago a empleados | 842 | 7 | 42 | −1 |
| Otros ingresos / otros egresos | 1161 / 1162 | 9 | 80 / 81 | +1 / −1 |

- La **aplicación** de un cobro/pago a documentos vive en `docDocumentPayment`
  (`FinancialOperationID` → `DocumentID`, ver §10.5); >99% de los cobros y pagos medidos tenían
  al menos una aplicación. Los traspasos y "otros ingresos" nunca la tienen.
- **No crees un pago insertando solo `docFinancialOperation`.** Se vio en producción un script
  que lo hacía (el encabezado con `ModuleID`/`DocumentTypeID`/`Amount`/fechas, nada más): el
  pago queda sin aplicar a facturas, sin actualizar `Balance`/`StatusPaidID` de los documentos
  y sin póliza. **Desde 2.99.0** el reparto de impuestos y los saldos insolutos ya no se calculan a mano:
  después del `INSERT` de `docFinancialOperation` + `docDocumentPayment` llama a `ctx.erp.RecalcPagosDocumento(doc)`
  (o `SaveAllTaxesPayment(operación)`) y luego a `UpdateDocumentPaidInfo(doc)`; ver el §3.5 y los avisos de
  [`SDK_FUNCIONES_NATIVAS.md`](SDK_FUNCIONES_NATIVAS.md) (PPD vs PUE, pagos ya timbrados). Crear el cobro completo por la
  vía nativa sigue sin resolverse: el `INSERT` sigue siendo SQL.
- **Conciliación bancaria:** `docEdoCtaBanco` guarda los movimientos del estado de cuenta
  (`FinancialEntityID`, `DateAffectation`, `Debit`, `Credit`, `Balance`, `Reference`,
  `Description`) y se liga a la operación con `docEdoCtaBanco.FinancialOperationID` — ahí se
  ve qué movimiento del banco corresponde a qué cobro/pago (50,000+ movimientos en la empresa
  medida).

**`docDocument.UserID` es el candado de "documento en uso".** Comercial lo llena con el usuario
que tiene el documento abierto y lo vuelve a `NULL` al cerrarlo (visible en la captura nativa
de la OC: `UPDATE docDocument SET UserID=NULL WHERE UserID=@u AND DocumentID=@doc` al cerrar).
Si Comercial se cae con documentos abiertos, se quedan "en uso por otro usuario". Para
liberarlos: `UPDATE docDocument SET UserID=NULL WHERE DocumentID=@doc AND UserID=@usuario` —
**uno por uno y confirmando que ese usuario ya no lo tiene abierto**. Se vio en producción un
botón que liberaba **todos** los documentos de golpe (`UPDATE docDocument SET UserID=0 WHERE
UserID>0`): libera también los que otra persona está editando en ese momento, y dos personas
terminan guardando el mismo documento. Por la misma razón, los builders (`NuevoDocumento`) y
los perfiles de §7 dejan `UserID` en 0/NULL al crear.

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

### 🔔 Enganchar un botón a un evento nativo de Comercial (`ctx.EventoId`)
Además de invocarse desde el ribbon o la Consola, cualquier AppKey se puede enganchar a
un evento nativo del módulo (Guardar, Actualizar, Seleccionar, Imprimir, Eliminar, Doble
clic) desde **Propiedades del módulo → pestaña Avanzado → "Ejecutar función"**, eligiendo
el Evento y escribiendo en Función:

```text
BrosLMV.<AppKey>_[Token]
```

Donde `[Token]` es cualquier campo que Comercial sepa sustituir por su valor real antes
de invocar (`[DocumentID]`, `[FinancialOperationID]`, etc. — el mismo mecanismo que usa
`Concepto` en una definición de asiento contable). Comercial sustituye el token como
**texto** antes de llamar, así que tu script recibe el AppKey literal como
`<AppKey>_12345`. `ClsMain.EjecutarScript` ya maneja esto solo: si no encuentra ese
AppKey exacto, reintenta con el nombre base y expone el número en `ctx.EventoId`
(`long?`) — tu script debe darle **prioridad sobre lo que esté seleccionado en el
grid**, para que guardar una ventana minimizada no dispare la lógica de otro registro:

```csharp
long id = ctx.EventoId.HasValue && ctx.EventoId.Value > 0
    ? ctx.EventoId.Value
    : ctx.GetSelectedIds().FirstOrDefault();
```

Es la base de cualquier automatización "al guardar" — por ejemplo, el Motor de Asientos
Contables (`docs/MOTOR_ASIENTOS_CONTABLES.md`) lo usa para generar la póliza justo al
guardar un Cobro/Pago, sin que el usuario tenga que darle clic a nada aparte.

### 👁️ Solo lectura forzado por usuario (desde v2.42.0, T2.2)
Cualquier script (SQL, C#, Python) ya respeta `ctx.SoloLectura` — bloquea
`NonQuery`/escrituras SQL y los builders de `ctx.erp` (`NuevoDocumento`, `AgregarArticulo`,
`Save`, etc.). Desde v2.42.0 puedes **forzar** ese modo para un usuario, sin que dependa de
que alguien marque una casilla:

```sql
INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES (<UserID>, 'SoloLectura', '1');
```

Con eso activo, ese usuario:
- **En el ribbon:** cualquier botón que intente escribir truena con "Modo SOLO LECTURA
  activo…" — no hay forma de saltárselo desde ahí, no hay casilla que desmarcar.
- **En la Consola:** la casilla "Modo solo lectura" de la barra de herramientas aparece
  **marcada y deshabilitada** — no es el valor por default, literalmente no se puede quitar.
- **En `BrosLMV.Runner`** (si el job programado corre con `--userid` de ese usuario): igual
  de forzado que en el ribbon.

Útil para un almacenista que debe poder correr reportes pero no `RecepcionOc`, o para
cualquier usuario al que quieras dar acceso de solo consulta sin tocar los permisos nativos
de Comercial. Quita la fila de `zzBrosPref` (o pon `Valor='0'`) para revertirlo.

### 💥 "Division by zero" nativo al Guardar/Visualizar: tres causas raíz confirmadas (y una descartada)
`XEngineLib` truena con "Division by zero" al guardar o visualizar un documento en tres
escenarios reales, ninguno obvio desde el mensaje de error (la 2 se creyó causa y se descartó):

1. **Causa raíz real: el producto estaba dado de alta como Paquete (`ProductTypeID=3`)
   cuando debía ser Producto Terminado (`ProductTypeID=2`)** — confirmado y corregido en
   producción. Un Paquete se costea por sus componentes (`orgProductComponent`); si se
   mueve por una Remisión sin pasar por ese camino de costeo (p. ej. nunca tuvo una entrada
   formal de almacén), `orgProductCostComercial` (el libro real de costeo) nunca se siembra
   para ese producto, y el recálculo de costos (`RecalcCostComercial`/`RecalcCostFiscal`)
   divide entre un costo que no existe. **La corrección real es cambiar el tipo de producto
   a Producto Terminado**, no parchear `CostPrice` por SQL — llenar `CostPrice` a mano en
   `docDocumentItem` (lo que se probó primero) es un síntoma tratado, no la causa arreglada,
   y puede quedar corto si el documento vuelve a pasar por un recálculo de costos real.
   Si ves este error, primero revisa `orgProduct.ProductTypeID` del producto involucrado
   antes de tocar nada por SQL.
2. ~~**`docDocumentItem.MustBeDelivered` en `1` en una partida que no es de OC/Pedido.**~~
   **Descartada (2026-09-28).** Se creyó causa porque se corrigió al mismo tiempo que la
   causa real (el producto era Paquete, causa 1). Evidencia en contra: una **Factura de
   Compra capturada a mano en Comercial deja sus partidas con `MustBeDelivered=1`**
   ("pendiente de recepción") y guarda y visualiza sin error; la misma factura generada desde
   XML la deja en `0`. `MustBeDelivered` controla la agenda de entrega/recepción pendiente,
   no el costeo. No hace falta forzarla a `0`.
3. **`orgProduct.CostPriceComercial` (columna del CATÁLOGO, no `docDocumentItem.CostPrice`
   de la partida) en `0`.** Comercial **recalcula y sobreescribe `docDocumentItem.CostPrice`
   al Guardar** usando `orgProduct.CostPriceComercial ÷ tipo de cambio` — así que llenar
   `CostPrice` a mano en la partida no protege contra esta causa: si el catálogo tiene
   `CostPriceComercial=0` para ese producto, el recálculo nativo vuelve a dividir entre cero
   sin importar lo que el script haya escrito. Confirmado como causa distinta a la 1
   (después de corregir `ProductTypeID` y seguir viendo el error para otro producto). Hay
   wrapper de solo lectura para consultarlo antes de crear el documento:
   `ctx.erp.GetCostPriceComercial(productId)` (ver §6.7) — revísalo si vas a facturar/mover
   un producto que nunca ha tenido movimientos de costeo reales.
4. **`orgProduct.ProductVolume` en `0`.** Causa distinta e independiente de las tres
   anteriores — confirmada con `CostPrice`/`CostPriceComercial` ya corregidos (no-cero) y
   el error seguía saliendo al dar **Visualizar** (no solo Guardar). Se confirmó por SQL
   que el producto de la partida tenía `ProductVolume=0` (coincide con "Volumen 0.00" en
   el panel "Origen" de la pantalla nativa) — la plantilla nativa de Visualizar usa el
   volumen como divisor en algún cálculo interno (costo/flete por m³, no confirmable sin
   acceso al binario cerrado de `XEngineLib`). Un volumen mínimo no-cero (p. ej. `0.001`)
   en el catálogo es inofensivo si el negocio no factura por volumen, y evita el crash.
   Igual que la causa 1, **se corrige el catálogo (`orgProduct`), no el documento** — el
   volumen es dato del producto, beneficia a cualquier documento futuro con ese producto.

La causa 3 se confirmó **descartando primero candidatos más obvios** (`CostPrice` en 0 en
todo el catálogo, `SourceDocumentID` en 0) antes de dar con la causa real; la causa 4 se
confirmó después, en un caso donde las anteriores ya estaban corregidas y el error seguía
saliendo. Si te topas con este error, no asumas la primera causa que se te ocurra: revisa
las tres confirmadas (1, 3 y 4), en orden, antes de seguir buscando — y ten presente que
**pueden coexistir varias a la vez** para el mismo producto.

### ⚖️ Reglas de afectación por módulo: qué mueve cada tipo de documento
Cada módulo de documento trae en *Propiedades → Parámetros* (tabla `engModuleParameter`,
`Section='Parámetro'`) **las reglas que usa Comercial para decidir qué afecta**. Es la fuente
de verdad para cualquier script que cree o mueva documentos: **no decidas por el tipo de
documento ni por `MustBeDelivered`, lee el parámetro del módulo** (`engModule.ModuleID` del
documento; los módulos clonados tienen los suyos).

| `ParameterKey` | En pantalla | Valores |
|---|---|---|
| `StockAffectation` | Afectación Inventario | `0` no afecta · `1` existencia presente · `2` disponible · `3` ambos (quita del disponible y agrega al presente). **Signo positivo = entra al almacén, negativo = sale** (texto de ayuda nativo) |
| `CostAffectation` | Afectación Costo Fiscal | `1` entrada al costeo fiscal · `-1` salida · `0` no afecta |
| `CostAffectationComercial` | Afectación Costo Comercial | igual, para el costeo comercial |
| `FinancialAffectation` | Afectación en Banco | signo del saldo/flujo del documento: `1` cargo (cuenta por cobrar, cobro) · `-1` abono (cuenta por pagar, pago) · `0` no genera saldo *(interpretación por los valores de todos los módulos; sin texto de ayuda nativo)* |
| `DocRecipient` | Recipiente | `1` cliente · `2` proveedor · `3` almacén · `4` entre almacenes · `7` empleado · `9`/`10` otros (bancos, préstamos) *(deducido por los módulos que usan cada valor)* |
| `DocumentTypeID` | — | tipo de documento para CFDI/reportes |

Valores de fábrica de los módulos más usados (`ModuleID` = base):

| Módulo | Inventario | Costo fiscal | Costo comercial | Saldo | Recipiente |
|---|---|---|---|---|---|
| 21 Facturas Cliente | 0 | -1 | 0 | 1 | 1 |
| 158 Ventas | -1 | 0 | -1 | 1 | 1 |
| 157 Entregas/Remisiones | -3 | 0 | -1 | 0 | 1 |
| 967 Pedidos | -2 | 0 | 0 | 1 | 1 |
| 142 Notas de Crédito Cliente | 0 | 1 | 0 | -1 | 1 |
| 159 Devoluciones (cliente) | 1 | 0 | 1 | 0 | 1 |
| 1193 Facturas a consignación | -4 | -1 | 0 | 1 | 1 |
| 183 Órdenes de Compra | 2 | 0 | 0 | 0 | 2 |
| 184 Recepciones de Compra | 3 | 0 | 1 | 0 | 2 |
| 152 Facturas Compra | 0 | 1 | 0 | -1 | 2 |
| 185 Devoluciones (proveedor) | -1 | 0 | -1 | 0 | 2 |
| 187 Notas de Crédito Proveedor | 0 | -1 | 0 | 1 | 2 |
| 242 Gastos | 0 | 0 | 0 | -1 | 2 |
| 202 Entrada de Almacén | 1 | 0 | 1 | 0 | 3 |
| 203 Salida de Almacén | -1 | 0 | -1 | 0 | 3 |
| 204 Movimiento Entre Almacenes | -1 | 0 | -1 | 0 | 4 |
| 205 Ajuste de Inventario | 1 | 0 | 1 | 0 | 3 |
| 400 Salida de Insumos / 401 Entrada de Prod. Terminados | -1 / 1 | 0 / 1 | -1 / 1 | 0 | 3 |

Consecuencias confirmadas en una empresa de fábrica (2026-09-28): la **Factura de Compra no
mete existencias** (`StockAffectation=0`) pero **sí registra la entrada en el costeo fiscal**
(`CostAffectation=1` → `orgProductCostFiscal`); las existencias las mete la **Recepción de
Compra** (`3`). La Factura de Cliente tampoco mueve inventario (lo hace la Remisión o la Venta).
El valor `-4` de Facturas a consignación no está en el texto de ayuda (sin confirmar).
**Regla para scripts de BrosLMV:** mover kardex solo si `StockAffectation <> 0` para el
módulo del documento, con el signo que indica; igual para costeo y saldo con sus parámetros.
Consulta: `SELECT ParameterKey, Value FROM engModuleParameter WHERE ModuleID=@m AND
Section='Parámetro'`.

### 🔒 Nunca ocultar en silencio un CFDI/XML ya asociado a otro documento
Un patrón de bug real (encontrado dos veces, en herramientas de asociación de XML distintas):
filtrar la lista de XML disponibles con algo como `WHERE ISNULL(DocumentID,0)=0` para
"esconder" los que ya están vinculados a otro documento. El problema: el usuario nunca ve que
ese XML existe y puede reasociarlo por error a un segundo documento (doble asociación fiscal,
error grave). El patrón correcto: **mostrar TODOS los XML del proveedor**, marcar
visualmente los ya asociados (deshabilitados para selección) e indicar en qué documento están
usados — nunca esconderlos.

### 🏢 No hardcodear la empresa propia (`OwnedBusinessEntityID`) ni su nombre
Bug real encontrado dos veces: un filtro tipo `WHERE FILTRO_EMPRESA = 'Nombre Empresa A'` (o
un `HAVING OwnedBusinessEntityID = 1`) escrito a mano para una sola razón social, en un
cliente con **más de una empresa propia** en la misma base (`orgBusinessEntity.IsOwned=1`
con varios `BusinessEntityID`). El síntoma es sutil: todo funciona bien para la empresa que sí
coincide con el filtro, y silenciosamente no aparece nada (o no promueve/asocia nada) para la
otra. Siempre deriva `OwnedBusinessEntityID` **del documento/contexto actual**, nunca de un
literal — aunque hoy el cliente solo use una empresa, el catálogo puede crecer.

### 🎯 `ctx.erp.ActiveModuleId` no es confiable para distinguir "partidas seleccionadas" de "documento completo"
Si un script necesita saber si el usuario seleccionó partidas sueltas (p. ej. desde un
submódulo de detalle) o el documento completo, no asumas el módulo activo — verifica
directamente si los IDs que llegan de `ctx.GetSelectedIds()` existen como `DocumentItemID`
en la tabla de detalle. Es más robusto que inferirlo por `ActiveModuleId`, que resultó no
serlo en un caso real.

### 🔑 `ctx.GetSelectedIds()` SÍ puede traer `DocumentItemID`, no solo IDs de documento
No asumas que la selección del grid siempre es a nivel documento. Algunos módulos
(confirmado uno vía `engModuleParameter.PrimaryKey`) tienen `PrimaryKey=DocumentItemID` —
en esos, `ctx.GetSelectedIds()` regresa IDs de PARTIDA, y el script puede operar selección
de partidas de varios documentos distintos a la vez. Verifica el `PrimaryKey` real del
módulo (`engModuleParameter`) antes de asumir qué tipo de ID te va a llegar.

### 📦 Escribir una Orden de Compra nativa por SQL: hallazgos de ingeniería inversa (BEFORE/AFTER + Extended Events)
Confirmado por una integración externa que replica el guardado nativo de OC (módulo 183) por
SQL directo, capturando el flujo real con Extended Events sobre la sesión XEngine — útil para
cualquier script que escriba `orgProductKardex`/`docDocumentDeliveryAgenda` a mano o agregue
campos extra:

- **`orgProductKardex` usa un patrón negar-insertar-limpiar para idempotencia**, no un simple
  INSERT: `UPDATE orgProductKardex SET DocumentItemID=-abs(DocumentItemID) WHERE DocumentID=N`
  (marca negativos los existentes) → INSERT de las filas nuevas → `DELETE WHERE
  DocumentItemID<0 AND DocumentID=N` (limpia los marcados). En alta nueva los pasos 1 y 3
  afectan 0 filas; en un re-guardado, borra y reescribe el kardex desde cero. Mismo patrón
  clean-slate que ya usa `docDocumentTaxDetail` (`DELETE` + re-INSERT), aplicado al inventario.
- **`docDocumentDeliveryAgenda` (agenda de entrega) solo se llena para partidas con
  `MustBeDelivered=True`** — confirma por qué existe esa bandera en primer lugar: en una OC
  nativa, un producto físico la trae en `True` (genera kardex + entrada en esta agenda); un
  servicio la trae en `False` y **no** genera ninguna de las dos. (La bandera **no** causa
  "Division by zero": esa sospecha se descartó, ver 💥 causa 2. En una Factura de Compra
  nativa también viene en `1`.) Qué documentos afectan inventario lo decide el parámetro del
  módulo, no esta bandera — ver "Reglas de afectación por módulo" abajo.
- **Los "campos extra" del usuario (Contabilidad Electrónica / campos personalizados) son
  columnas físicas agregadas directamente a la tabla existente** (`docDocumentExt` a nivel
  documento, `docDocumentItem` a nivel partida) — CONTPAQi **no** usa un modelo EAV
  (Entity-Attribute-Value). El motor nativo simplemente incluye esas columnas en el INSERT
  normal (el de `docDocumentItem` pasó de 81 a 82 parámetros al agregar un campo extra). Si tu
  script escribe a mano una tabla con campos extra conocidos, inclúyelos como columnas
  normales del mismo INSERT — no hay tabla ni JOIN aparte que buscar.
  **Peligro real confirmado:** un campo extra existe **por empresa**, no por instalación de
  Comercial — no asumas que porque una empresa lo tiene configurado, todas lo tienen. Escribir
  a una columna extra que esa empresa nunca configuró revienta con `Invalid column name` y
  aborta **todo el documento a medio crear** (incidente real: 2 Recepciones de Compra quedaron
  atoradas por esto). Verifica primero con `COL_LENGTH('docDocumentItem', 'Proyecto')` — pero
  **léelo con `ctx.Query` + revisar si la fila trae `null`, no con `ctx.Scalar`**, que resultó
  no distinguir de forma confiable "la columna no existe" (NULL real) de otros casos. Guarda el
  resultado una sola vez por corrida del script, no por partida.
  **Cómo auditar todo lo no-nativo de una empresa antes de escribirle scripts:** compara su
  base contra una empresa nativa de la misma instancia (`ComercialSP`) con consultas
  cruzadas a `sys.tables`/`sys.columns`/`sys.views`/`sys.foreign_keys`/`sys.triggers`
  (`WHERE name NOT IN (SELECT name FROM ComercialSP.sys.tables)`, etc.) más
  `SELECT ModuleID, ModuleName, DLL FROM engModule WHERE Custom = 1` para los módulos
  clonados/personalizados. Dos precauciones confirmadas: (1) el `ComercialSP` de un servidor
  con BrosLMV ya provisionado no es de fábrica pura (trae las tablas `zzBros*` base); (2)
  antes de declarar una columna "personalizada", descarta que sea **drift de versión** de
  Comercial — se vieron columnas que existían en una empresa y no en `ComercialSP` de la misma
  instancia y que tenían toda la pinta de venir de una actualización (p. ej.
  `accPolizaDefinitionItem.CondicionPersonalizada`, `accPoliza.AccountingConditionError`), no
  de un integrador. Un conteo de `sys.triggers` que cambia de un mes a otro es señal de
  revisar qué se instaló — cualquier trigger nuevo sobre tablas donde el motor inserta es un
  riesgo (ver "🔌 Integraciones externas…" más abajo).

### 🚚 Gaps reales entre los builders (`NuevoDocumento`/`AgregarArticulo`) y el comportamiento nativo en Orden de Compra
Confirmado por una integración externa en producción que procesa una cola de documentos
(patrón: tabla de encolado + un `zzBrosScript` disparado por el consumidor, en vez de un botón
de usuario) — los builders del SDK **no replican automáticamente** varias cosas que sí hace el
motor nativo al capturar una OC a mano:

- **`docDocumentDeliveryAgenda` no se crea sola** ni siquiera pasando por
  `ctx.erp.AgregarArticulo` — hay que insertarla a mano por cada partida física (no servicios),
  igual que confirma la ingeniería inversa de la OC nativa (ver arriba). El builder no lo hace
  por ti.
- **`orgProductSupplier` tampoco se crea/actualiza sola** — upsert a mano (`UPDATE` si ya existe
  el par ProductID+SupplierID, `INSERT` si no) después de `AgregarArticulo`.
- **`docDocumentPaymentAgenda` con condición de pago a varias parcialidades (p. ej. 50%-50%)
  sale mal** también pasando por los builders normales (`NuevoDocumento`+`AgregarArticulo`), no
  solo escribiendo SQL directo: queda 1 sola fila al 100% con `Amount=0`. Hay que borrarla y
  reconstruirla a mano con los porcentajes/fechas reales (mismo patrón ya documentado arriba
  con `engPaymentTermDetail`, aquí con los datos ya calculados del lado de quien encola).
- **`ctx.erp.UpdateStatusDelivery(sourceDocumentId)` no cubre el 100% de los casos** al recibir
  una OC — llámalo igual (puede actualizar algo más, p. ej. el ícono del grid), pero **calcula
  tú el valor final de `StatusDeliveryID`** comparando `SUM(Quantity)` de la OC origen
  (`MustBeDelivered=1`) contra `SUM(Quantity)` de las partidas de Recepción que la referencian
  vía `DeliverDocumentItemID` — nada recibido → `3`, parcial → `2`, completo → `1` (valores
  confirmados con datos reales en los 3 casos).
- **El módulo nativo de Traspaso entre Almacenes (204) puede ser unidireccional según la
  configuración de la empresa** — revisa `engModuleParameter.StockAffectation` para ese
  módulo; en `-1` solo resta existencia en origen y **nunca suma en destino**. Si te topas con
  esto, el workaround confiable es **no usar el módulo 204**: crea una Salida de Almacén (203)
  en el origen + una Entrada de Almacén (202) en el destino, con la cantidad ya confirmada por
  quien recibe (nunca la "cantidad enviada", para que el kardex siempre refleje lo que de
  verdad pasó sin necesitar corrección después).

### 🔌 Integraciones externas vía `BrosLMV.Runner`: reglas confirmadas en producción
Del consumidor real del Runner (una app web que sincroniza en ambos sentidos con Comercial
Pro, en producción en más de un cliente):

- **Nunca invoques el Runner desde un proceso de servidor web** (en ese caso `php -S`/
  `artisan serve` atendiendo una petición HTTP): falla siempre con "no encontrado
  registrado"; el mismo comando desde un proceso de consola, un servicio de Windows o una
  Tarea Programada funciona siempre. La causa raíz no se determinó (el patrón es 100%
  reproducible). Arquitectura que sí funciona: la petición web **solo encola** (tabla propia,
  `Status='pending'`) y un proceso persistente (servicio de Windows con un ciclo de ~3 s) es el
  único que dispara el Runner sobre toda la cola pendiente — más una Tarea Programada de
  respaldo por si el proceso persistente muere.
- **No pongas un trigger `AFTER INSERT` en `docDocument`** para detectar documentos nuevos
  capturados en Comercial: se confirmó con pruebas reales que corrompe el ID que el motor lee
  de vuelta justo después de su INSERT — riesgo de que XEngine asocie partidas al documento
  equivocado en **todo** Comercial Pro, no solo en tu flujo. (Consistente con que el motor lea
  `@@IDENTITY`, que a diferencia de `SCOPE_IDENTITY()` sí lo altera un trigger que inserte en
  otra tabla con identidad.) Para detectar documentos nativos nuevos usa **polling con marca de
  agua** (`DocumentID > último visto`, por módulo). La misma precaución aplica a cualquier
  trigger sobre una tabla en la que el motor inserte y luego lea la identidad generada.
- **Deduplica antes de encolar** (mismo tipo de documento + referencia de origen), pero con una
  salida explícita para forzar un reenvío cuando el documento anterior ya se borró/canceló en
  Comercial — si no, la deduplicación bloquea en silencio el reenvío y devuelve un
  `DocumentID` muerto.
- **Reconciliación de borrados:** revisa periódicamente `docDocument.DeletedOn` de los
  documentos ya vinculados — el sistema externo debe reaccionar a un borrado/cancelación hecho
  en Comercial, nunca iniciarlo.
- **Estado de entrega de una OC:** Comercial muestra Entregado/Parcial/No entregado a partir de
  `docDocument.StatusDeliveryID`, **no** de `docDocumentDeliveryAgenda.QtyDelivered` (confirmado
  que esa columna nunca se usa).
- **Una Recepción de Compra nativa solo admite un `SourceDocumentID`** — esta integración genera
  una Recepción nativa por cada OC involucrada cuando se reciben varias a la vez. (Las
  plantillas de BrosLMV, §10.4, juntan N OC en una sola Recepción con `SourceDocumentID` = la
  primera OC, informativo; ambos enfoques funcionan, pero el de una-por-OC deja la trazabilidad
  nativa completa.)
- **Costo real de existencias:** no uses `orgProduct.CostPrice` (dato de catálogo que se queda
  viejo) — el costo vigente por PEPS está en `orgProductCostFiscal` (vista
  `vwLBSProductCostFiscalList`), una fila por movimiento de entrada; toma la más reciente por
  producto+almacén.
- **Columnas reales de `docDocumentTaxDetail`:** `TaxName`, `TaxTypeName`, `Retention`,
  `RegionalTaxID`, `IVASobreIEPS` — no `Name`/`Classification`/`IsRetention` (un INSERT con esos
  nombres truena).

### ⏳ Cuelgue indefinido (sin error ni timeout) al crear un documento: historial de kardex corrupto
Confirmado en producción por un consumidor real de `BrosLMV.Runner`: crear una Recepción de
Compra para un producto específico **se colgaba indefinidamente** — sin excepción, sin timeout,
hubo que matar el proceso. Aislado con pruebas controladas: no era el impuesto ni la retención
(se colgaba igual con y sin retención); con un producto **sin historial de kardex** el mismo
flujo funcionaba perfecto. Causa real: ese producto tenía su historial de costo/kardex corrupto
por pruebas previas que **mezclaron capturas nativas con INSERTs de SQL crudo** al kardex. Si un
documento se cuelga solo con un producto concreto, sospecha primero de su historial de kardex,
no de XEngine — y no mezcles SQL crudo al kardex con capturas nativas sobre el mismo producto.

De paso, confirmado en ese mismo consumidor: XEngine **sí genera solo** las filas correctas de
`docDocumentTaxDetail` para impuestos con retención (p. ej. IVA 16% + retención) con solo poner
el `TaxTypeID` en la partida — no hay que calcular retenciones a mano cuando se usa el SDK.

### 🔒 Integridad de scripts: hash y aprobación (desde v2.35.0)
Cada vez que guardas un script desde la Consola (**Guardar**/**Guardar como**), BrosLMV
calcula un hash SHA-256 del código y lo guarda junto con él (`zzBrosScript.HashSHA256`).
Al ejecutar un botón desde el **ribbon**, se recalcula el hash del código que se va a
correr y se compara contra el guardado:

- **Coinciden** (lo normal) → corre sin avisos.
- **No coinciden** → alguien modificó `Codigo` por fuera de la Consola (p. ej. un `UPDATE`
  manual desde SSMS). Aparece una advertencia, pero **el script igual se ejecuta**
  (primera versión: solo avisa, no bloquea) — revisa con el responsable si no reconoces
  el cambio.
- **Scripts guardados antes de v2.35.0** (`HashSHA256` vacío) no disparan la advertencia
  — no hay una versión anterior con la que comparar. Se activa automáticamente la
  próxima vez que lo guardes desde la Consola.

**Modo estricto opcional — exigir aprobación:** si quieres que un botón no corra hasta
que alguien lo revise explícitamente, marca la preferencia por usuario:

```sql
INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES (<UserID>, 'ExigirAprobacion', '1');
```

Con eso activo, cualquier botón sin aprobar (`AprobadoEl IS NULL`) se bloquea al
ejecutarse desde el ribbon con un mensaje claro. Se aprueba con un clic desde la Consola
(botón **"Aprobar"** en la barra de herramientas) — registra quién y cuándo
(`AprobadoPor`/`AprobadoEl`).

> **`BrosLMV.Runner` (prototipo interno, T3.3, aún no distribuido):** el runner headless
> aplica esta misma verificación pero **más estricta** — como no hay un usuario mirando
> la pantalla para decidir si sigue, un hash que no coincide o una aprobación pendiente
> **detienen la ejecución** en vez de solo avisar.

### 📦 Paquetes `.bros`: mover un botón entre empresas o equipos (desde v2.37.0)
Un botón vive en dos partes: el **script** (guardado en `zzBrosScript`, en la base de datos
de la empresa) y sus **assets** (imágenes, plantillas HTML, etc. — si el botón usa
`ctx.show_html`/`ctx.dashboard` con archivos propios, viven en disco: `C:\BrosLMV\scripts\
<EMPRESA>\<AppKey>_assets\`). El script se comparte automáticamente entre todas las
terminales de una empresa (vive en SQL), pero los **assets no** — solo existen en el equipo
donde se guardaron. Un paquete `.bros` empaca ambas partes para moverlas juntas.

**Exportar:** en el árbol de scripts de la Consola, clic derecho sobre el botón →
**"Exportar paquete (.bros)…"**. Genera un archivo `.bros` (es un `.zip`) con el código, sus
assets (si tiene) y un manifiesto con metadatos (nombre, módulo, versión de BrosLMV con la
que se exportó).

**Importar:** botón **"Importar paquete…"** en la barra de herramientas de la Consola.
Selecciona el `.bros` — se guarda en la **empresa activa** (la que tenga abierta Comercial
en ese momento), con su historial (`zzBrosScriptHist`) intacto si ya existía un script con
ese `AppKey`. Si la empresa destino está en una versión de BrosLMV más vieja que la que
exportó el paquete, avisa (no bloquea) por si faltan columnas o funciones nuevas.

**El botón del ribbon NO se crea solo.** Después de importar, la Consola ofrece copiar al
portapapeles el SQL para darlo de alta (mismo patrón que
`instalador\sql\plantilla_crear_boton.sql`) — se corre a propósito, no automático, porque
toca tablas nativas de Comercial (`engRibbonControl`/`engRibbonMenu`).

### 🕓 Historial de versiones: diff y restaurar (desde v2.40.0)
Cada vez que guardas un script desde la Consola, BrosLMV respalda la versión ANTERIOR en
`zzBrosScriptHist` **automáticamente** (esto ya pasaba desde hace mucho) — lo nuevo es la
pantalla para verlo y usarlo.

**Clic derecho sobre un botón → "Historial de versiones…"**: lista de versiones anteriores
(fecha, quién la guardó, etiqueta si tiene, tamaño) a la izquierda; a la derecha, un
**diff línea por línea** contra el código de HOY — verde = línea que solo está en la
versión de hoy (se perdería si restauras), rojo = línea que solo está en la versión vieja
(volvería si restauras).

Desde ahí puedes:
- **Restaurar esta versión** — la versión que tenías justo antes de restaurar queda
  respaldada a su vez (es el mismo mecanismo de siempre), así que restaurar **nunca pierde
  nada** y se puede deshacer restaurando de nuevo.
- **Exportar (.bros)…** — empaca esa versión vieja específica como paquete `.bros` (no la
  actual), por si la quieres guardar aparte o mandarla a otra empresa.
- **Etiquetar…** — ponle una nota como "Antes del refactor de precios" para encontrarla
  después sin adivinar por fecha.
- **Purgar versiones viejas…** — borra las versiones sin etiqueta de más de N días para que
  el historial no crezca sin límite. **Las versiones etiquetadas nunca se borran**, sin
  importar qué tan viejas estén — etiquetar una versión es la forma de decirle a BrosLMV
  "esta no la toques".

> Antes de modificar un botón que te importa, la forma más rápida de tener una red de
> seguridad es simplemente **guardar** — el respaldo ya queda solo. Si además quieres
> encontrarlo fácil después o protegerlo de una purga futura, etiquétalo.

### 📋 Auditoría central (`zzBrosAuditoria`, desde v2.36.0)
Cada ejecución (botón o consola) queda registrada en **dos lugares**: el archivo local
`C:\BrosLMV\data\broslmv.db` (por equipo, como siempre) **y** la tabla `zzBrosAuditoria`
de la empresa activa — quién (`Usuario`), desde qué equipo (`Equipo`), qué botón
(`AppKey`), desde dónde (`Origen`: `boton`/`boton-python`/`boton-sql`/`consola`/
`integridad`), cuánto tardó y si terminó bien. A diferencia del archivo local, esto es
**visible desde cualquier terminal** de la empresa — útil para control interno o una
auditoría fiscal. Es *best-effort*: si la empresa no está provisionada o no hay permiso
de escritura, la ejecución del script **nunca** falla por esto, simplemente no queda
ese registro central. Consulta directa mientras no exista una pantalla en la Consola:
```sql
SELECT TOP 50 * FROM zzBrosAuditoria ORDER BY id DESC;
```

### ⚠️ `Delete` vs `CancelDocument`
- **`ctx.erp.Delete(doc)`** = soft-delete. Marca `DeletedOn` pero **NO revierte kardex ni costos**.
  El inventario queda inflado. Solo seguro para documentos sin afectación (solicitudes).
- **`ctx.erp.CancelDocument(doc)`** = cancelación. Marca `CancelledOn` y debería revertir kardex.
  **Usar siempre para documentos de inventario.**
- `ctx.erp.ReactivateDocument(doc)` = inverso de `CancelDocument`.
- **Eliminar un documento (nativo) borra físicamente su fila de `docDocumentCFD`**, aunque
  `docDocument` solo quede marcado con `DeletedOn`. Confirmado en producción: 136/136 documentos
  vigentes con su `docDocumentCFD`, 30/30 eliminados sin ella. Consecuencias: (1) un documento
  sin `docDocumentCFD` es firma de "fue eliminado", **no** de que `NuevoDocumento` no la haya
  creado (sí la crea siempre — un proyecto satélite lo diagnosticó mal por mirar documentos de
  prueba ya borrados); (2) si un script toca `docDocumentCFD` de un documento que pudo haberse
  eliminado, verifica que la fila exista antes de un `UPDATE`, y revisa cuántas filas afectó.

### ⚠️ No duplicar anclas
- `NuevoDocumento` ya crea las 4 anclas (`docDocumentExt`, `docDocumentExtra`, `docDocumentCFD`,
  `docDocumentPaymentAgenda`). **No volver a insertarlas** (causa PK duplicada).

### ⚠️ Campos fiscales de CFDI viven en `docDocumentCFD`, no en `docDocument`
- `docDocument` no almacena Método de Pago, Forma de Pago ni Uso de CFDI. Estos datos residen
  exclusivamente en `docDocumentCFD`:
  - `MetodoPago` (`nvarchar(3)`: `PUE`, `PPD`).
  - `FormaPago` (`nvarchar(10)`: `03`, `01`, `99`...).
  - `ReceptorUsoCFDI` (`nvarchar(5)`: `G03`, `G01`, `CP01`...).
  - `CFDFormOfPayment` (`nvarchar(255)`).
- Si creas un documento desde un XML preexistente mediante `NuevoDocumento` y solo asignas el UUID
  y el archivo XML, Comercial PRO mostrará `99 - Por definir` y Uso en blanco. Es mandatorio
  actualizar estas columnas directamente en `docDocumentCFD`.

### ⚠️ Agenda de pagos (`docDocumentPaymentAgenda`) en documentos con fecha distinta a hoy
- `ctx.erp.NuevoDocumento` inserta la fila inicial de `docDocumentPaymentAgenda` calculada a
  partir de `GETDATE()`. Si el documento tiene una fecha distinta (por ejemplo, importación de
  facturas CFDI emitidas en días anteriores), cambiar `docDocument.DateDocument` por SQL **no**
  recalcula la agenda. Comercial PRO mantendrá la fecha de creación como fecha de vencimiento,
  provocando plazos de crédito erróneos (ej. 14 días en lugar de 30).
- **Regla de regeneración:** si el documento tiene fecha histórica o plazo de crédito, consulta
  `engPaymentTermDetail` (`PaymentPeriodID` = unidad, `PaymentUnit` = cantidad) y regenera las filas
  de `docDocumentPaymentAgenda` aplicando `DATEADD` a partir de `DateDocument` (la fecha real de la
  factura), actualiza `docDocument.DateLastPayment` con el vencimiento final, y fija
  `DateDocDelivery = NULL` en compras para evitar marcas indebidas de recepción.

### ⚠️ Un documento creado por script no genera póliza: pídesela al motor nativo
- `NuevoDocumento` → … → `Save` (vía `Doc.clsMain`) **no dispara la póliza**, aunque el módulo tenga `AccountingPoliza = 1`. Se la pides
  tú, **después de `Save`**, a `Accounting.clsMain` (el motor que la genera al guardar en la ventana):
  ```csharp
  var acc = ctx.erp.CrearHelper("Accounting.clsMain");   // asigna XEngineLib
  var generadas = acc.GetType().InvokeMember("CrearPolizasDocumento", BindingFlags.InvokeMethod, null, acc,
                                             new object[] { docId, false, "" });   // DocumentID, ShowResult, sAdvertencias → devuelve cuántas generó
  ```
  **Confirmado en pruebas** (devuelve `1` y `accPoliza` pasa de 0 a 1). `CrearPolizasDocumentos("1,2,3", false)` es la versión por lote y
  `CrearPolizasFinancialOperation(id, false, "")` la de cobros/pagos. No envía nada a CONTPAQi Contabilidad (eso es «Sincronizar Póliza»).
- **No funcionaron** (devuelven `null` y no generan): `Document.clsMain.ExecuteFunction("RefreshAllPolizasPorDocumentID")` y
  `LBS.clsMain` `AllIDs` + `ExecuteFunction("RegenerarPolizasPorID")` (el botón «Volver a generar Pólizas seleccionadas» sí lo hace desde la lista,
  pero no con esa llamada).
- ¿El módulo genera póliza? Lee `engModuleParameter.AccountingPoliza`, que vive en `Section = 'Contabilidad'` (**no** en `'Parámetro'`).
- No reimplementes el asiento con `INSERT`.

### 🔄 Refrescar el grid (ESTÁNDAR: todo script que cambie datos lo hace)
Si un script crea, modifica, cancela, timbra o vincula algo que aparece en una lista de Comercial, **debe refrescar el grid al terminar**;
si no, el usuario tiene que pulsar «Actualizar» para ver el cambio (el ID del documento creado, estatus, saldos…), a diferencia de lo que
hacen los botones nativos.

**Regla:**
- Llama `ctx.erp.RefreshGrid();` **una sola vez, después del último cambio** (si creas N documentos, no lo llames por cada uno: solo al final).
- Desde **v2.94.0** `ctx.erp.RefreshGrid()` refresca el grid actual **y conserva la fila y la vista** (lee `Row` del grid Janus antes y lo
  restaura después con `EnsureVisible`). En versiones anteriores llevaba siempre roto (fallaba en silencio con `DISP_E_PARAMNOTOPTIONAL`).
- Si el script tiene una ventana propia que se queda abierta, refresca también al cerrarla; si se cierra sola al terminar, basta con el
  refresco de al final.
- **Nunca** pases `Redraw = false` a `XEngine.RefreshGrid` (trabó el sistema). No uses `ExecuteFunction("GridRefresh")`, `MustRefreshGrid`
  sola, ocultar/minimizar la ventana ni `SendKeys F5`: en pruebas no refrescan.

**Por qué:** `XEngine.RefreshGrid(jgd, [Redraw])` exige el grid como primer parámetro (`ctx.erp.Get("janusGrid")`); recarga todo y deja la
vista al principio, por eso se restaura la fila. El grid es un Janus GridEX (`GridEX20.ocx`: `Row` get/put, `RowCount`, `EnsureVisible`,
`MoveToBookmark`, `MoveToRowIndex`, `Rebind`).

**Compatible con versiones anteriores a 2.94.0** (copia esta función en el script):
```csharp
void RefrescarGrid()
{
    try
    {
        var g = ctx.erp.Get("janusGrid");
        if (g == null) return;
        int fila = -1;
        try { fila = Convert.ToInt32(g.GetType().InvokeMember("Row", System.Reflection.BindingFlags.GetProperty, null, g, null)); } catch { }
        ctx.erp.Call("RefreshGrid", g);
        if (fila > 0)
        {
            g.GetType().InvokeMember("Row", System.Reflection.BindingFlags.SetProperty, null, g, new object[] { fila });
            g.GetType().InvokeMember("EnsureVisible", System.Reflection.BindingFlags.InvokeMethod, null, g, new object[] { fila, Type.Missing });
        }
    }
    catch { }
}
```
Plantilla de referencia: `instalador/scripts/CREAR_DOC_DESDE_XML.ctx` (función `RefrescarGridNativo`).

### ⚠️ Filtra SIEMPRE por la empresa activa (`OwnedBusinessEntityID`)
- Varias tablas guardan una copia por empresa: `engRefExpense` (tipos de gasto: `-1` = plantilla de fábrica, `1`, `2`…
  cada empresa), `orgDepot`, `orgSupplier`, `orgCustomer`, `orgCostCenter`, `docDocument`, `docDocumentCFDiSAT`.
  Un `SELECT` sin `OwnedBusinessEntityID = ctx.erp.OwnedBusinessEntityId` mezcla empresas (tres veces cada tipo de gasto).
- La persona (`orgBusinessEntity`) es compartida entre empresas; lo que es de la empresa es su rol
  (`orgSupplier`/`orgCustomer`). Si el RFC ya existe, reutiliza la entidad y agrega solo el rol.

### ⚠️ Un lote de alta (varios INSERT) va por `ctx.OpenConn()`, no por `ctx.Scalar`/`ctx.NonQuery`
- Por el puente COM de Comercial, un lote con varios `INSERT`/transacción **se ejecuta pero su resultado no se puede
  leer**: `ctx.Scalar` con `soloLectura = true` (por omisión) **reintenta** el SQL y duplica el alta; con `false`, y
  también `ctx.NonQuery` (que añade `SELECT @@ROWCOUNT`; un `RETURN;` se lo salta), lanzan «el SQL se ejecutó por COM
  pero la conexión murió leyendo el resultado» **aunque sí corrió**.
- Regla: el lote va con `using (var cn = ctx.OpenConn()) using (var cmd = cn.CreateCommand()) { cmd.CommandText = sql;
  var id = cmd.ExecuteScalar(); }` (termina en `SELECT @id`), con `SET NOCOUNT ON;` y **idempotente**
  (`IF EXISTS (…) BEGIN SELECT id; RETURN; END` antes de insertar).

### ⚠️ Plazo de pago del proveedor (`orgSupplier.PaymentTermID`)
- El plazo de crédito de compras no vive en `orgBusinessEntity`, sino en `orgSupplier.PaymentTermID`.
  Al construir documentos de compra o asistentes de alta, consulta `orgSupplier` para preasignar
  los días de crédito del proveedor en vez de degradar al plazo predeterminado del sistema.

### ⚠️ No duplicar campos de partida
- `AgregarArticulo` ya llena `ApplyGlobalDiscount=1`, `DeductiblePerc=1`, `IsBusinessOperation=1`,
  `MustBeDelivered=1`, `DateItem`, `CoefUnit=1`, `ClaveUnidad`, `ObjetoImpuesto`. **No repetir.**

### ⚠️ Folio
- `NuevoDocumento` resuelve el folio automáticamente. No usar `MAX(Folio)+1` manual.
- **Excepción real confirmada:** esto depende de que el módulo tenga filas configuradas en
  `engDocumentFolio` — no es garantizado en una base de cliente real. Se confirmó un caso con
  **`engDocumentFolio` casi vacía en toda la base (3 filas totales) y ninguna para el módulo**
  que se estaba usando (Facturas de Cliente). Si te topas con esto, `MAX(Folio)+1` manual es
  la única opción — pero hazlo **agrupado por el `FolioPrefix` exacto** (el string completo,
  no solo `ModuleID`+`OwnedBusinessEntityID`): se confirmó folios históricos mezclados bajo el
  mismo módulo+entidad con 4-5 series de prefijo distintas (variantes como `BCD40`/`BCD4.0`/
  `BCD`, más series especiales como anticipos), que colisionarían si el `MAX` no filtra por el
  prefijo exacto que vas a usar. Y como `MAX+1` **no es atómico** (confirmado: folios
  duplicados con 2 procesos en paralelo), si hay capturistas o procesos concurrentes en ese
  módulo, serializa con `sp_getapplock` (recurso = módulo+serie) dentro de la misma transacción
  que inserta el documento.

### ⚠️ Transacciones
- Los builders del addon (`NuevoDocumento` + 4 anclas) ejecutan 5 INSERT sin transacción.
  Si el script es crítico, envolver en `ctx.OpenConn()` + transacción manual.

### ⚠️ SQL directo + XEngine
- No mezclar INSERT crudo de `docDocument` con `ctx.erp.AffectStockNEW` — el builder
  `NuevoDocumento` ya conoce los defaults correctos. Usar SQL directo solo para los
  ajustes de perfil por módulo (UPDATE de `PaymentTermID`, `DepotIDFrom`, etc.).

### ⚠️ ProgID `XengineLib.clsMain` faltante en 32 bits (despliegue de `BrosLMV.Runner`)
Encontrado en un servidor real de un consumidor externo (**un CRM externo**, ver
`CHANGELOG.md` — entrada `BrosLMV.Runner` v0.3.0): `BrosLMV.Runner.exe` fallaba SIEMPRE con
`ERROR al crear XEngine standalone: No se encontró "XengineLib.clsMain" registrado en este
equipo`, aun con Comercial Pro instalado y funcionando con normalidad.

- **Causa confirmada por registro:** el CLSID real (`{D5255125-CD90-48A8-BC48-762BE8531B5D}`,
  `InprocServer32` → `XEngineLib.dll`) estaba bien registrado en la vista de 32 bits
  (`HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\...`), pero el **mapeo ProgID→CLSID**
  (`XengineLib.clsMain`) solo existía en la vista de **64 bits**
  (`HKLM\SOFTWARE\Classes\XengineLib.clsMain`) — la vista de 32 bits
  (`HKLM\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain`) no existía. El Runner es un
  proceso de 32 bits que activa XEngine por ProgID (`Type.GetTypeFromProgID`), así que la
  búsqueda solo mira la vista de 32 bits y no lo encuentra, aunque la clase sí esté bien
  registrada ahí.
- **Depende del instalador de Comercial Pro**, no de nada que controle BrosLMV — puede
  repetirse en cualquier servidor/cliente donde `XEngineLib.dll` haya quedado registrado
  así.
- **Auto-sanado desde v0.3.0 del Runner:** `runner\Program.cs` (`AsegurarProgIdXEngine32Bits`)
  detecta este caso exacto (ProgID ausente en 32 bits, presente en 64 bits) y copia el mapeo
  antes de activar XEngine — best-effort, no bloquea si no hay permisos de administrador.
  Si el Runner sigue fallando con este mismo mensaje, el arreglo manual es:
  ```powershell
  New-Item -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain" -Force
  New-Item -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain\CLSID" -Force
  Set-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain" -Name "(default)" -Value "XengineLib.clsMain"
  Set-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\XengineLib.clsMain\CLSID" -Name "(default)" -Value "{D5255125-CD90-48A8-BC48-762BE8531B5D}"
  ```

### ⚠️ NUNCA un trigger `AFTER INSERT` sobre `docDocument` (ni ninguna tabla que XEngine use para crear documentos)
Encontrado y confirmado por un consumidor externo (**un CRM externo**, ver `CHANGELOG.md`
[2.81.0]) con una prueba controlada y reproducible: un trigger `AFTER INSERT` en
`docDocument` que hace su propio `INSERT` en otra tabla con columna `IDENTITY` propia
**corrompe la recuperación del ID recién insertado**.

- **Con el trigger activo:** `SCOPE_IDENTITY()` (patrón estándar `INSERT ...; SELECT
  SCOPE_IDENTITY()` en un solo batch) devolvió un ID que **no coincidía** con el ID real
  insertado (desviado por 3 en la prueba).
- **Con el trigger desactivado:** correcto, 2 de 2 veces.
- No se investigó la causa raíz exacta a nivel de driver/protocolo (hipótesis: algo del
  driver ODBC/PDO_SQLSRV con múltiples result sets cuando un trigger inserta en otra tabla
  con `IDENTITY` propia), pero el riesgo es real y grave: **si algún mecanismo interno de
  XEngine/BrosLMV obtiene el `DocumentID` (o cualquier PK) recién creado con este mismo
  patrón (`INSERT` + `SCOPE_IDENTITY()`), un trigger puesto por CUALQUIER cosa** — un
  cliente, un script de un usuario, una futura feature de BrosLMV — **puede hacer que se
  asocien partidas al documento equivocado, silenciosamente, sin ningún error visible.**
- **Regla:** no agregues un trigger `AFTER INSERT` (ni de ningún otro tipo que inserte en
  una tabla con `IDENTITY` propia) sobre `docDocument` ni sobre ninguna tabla que XEngine
  use para crear documentos. Si necesitas reaccionar a la creación de un documento, hazlo
  **después** (polling, un job periódico, o un trigger que NO inserte en una tabla con
  `IDENTITY` propia), nunca en el mismo INSERT.

### ⚠️ Un producto con kardex corrupto puede colgar `ctx.erp` de escritura sin error ni timeout
Encontrado por un consumidor externo (**un CRM externo**, ver `CHANGELOG.md` [2.81.0]):
un producto con historial de kardex corrupto (mezclado por INSERTs SQL crudo previos a
usar `ctx.erp`) hizo que `ctx.erp.AgregarArticulo`/`Save` en una Recepción de Compra se
**colgara indefinidamente, sin error ni timeout** — hubo que matar el proceso a mano. Se
descartó que fuera la retención de impuestos o el `TaxTypeID` (probado con y sin
retención, mismo resultado); con un producto sin ese historial corrupto, funciona
perfecto.

- **Riesgo sistémico:** cualquier script que use `ctx.erp` de escritura headless (vía
  `BrosLMV.Runner` o el ribbon) **no tiene ningún timeout/watchdog** — un solo producto
  con datos corruptos de un cliente real puede colgar el proceso completo sin aviso.
- **Mejora futura (no implementada todavía):** evaluar un timeout duro alrededor de las
  llamadas de escritura de `ctx.erp` en `BrosLMV.Runner`, para no depender de que un
  operador mate el proceso a mano al detectar el colgado.

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
 2. Guardar (la Consola muestra la clave: BrosLMV.<clave>)
 3. Clic derecho sobre el script -> Crear botón… (asistente)
 4. Reiniciar CONTPAQi para ver el botón
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
> [`DASHBOARDS_HTML.md`](DASHBOARDS_HTML.md) — cómo construir un dashboard rápido y
> portable (`ctx.dashboard()`, agregación en SQL, patrón de assets incrustados).
