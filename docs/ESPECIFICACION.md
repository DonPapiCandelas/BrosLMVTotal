# BrosLMV — Especificación técnica (blueprint de reconstrucción)

Documento **autosuficiente** para reconstruir BrosLMV **desde cero**. Contiene el
contrato con CONTPAQi, la arquitectura, las versiones exactas, los nombres exactos
de los miembros COM que usamos, los algoritmos, el registro y las trampas
aprendidas. Si tienes solo este documento, puedes volver a crear la herramienta.

- Versión del producto descrita: **2.18.0**
- Para uso/instalación: `MANUAL.md`, `INSTALACION.md`. Para historial: `CHANGELOG.md`.
- API completa de `ctx.erp` (84 métodos) documentada en `MANUAL.md`.

---

## 1. Objetivo y restricciones

**Objetivo:** poner botones personalizados en **CONTPAQi Comercial PRO** que se
ejecuten de forma **totalmente autónoma y bajo control propio**, sin depender de
servidores de licencia de terceros (si un servicio externo de licencias deja de
responder, los botones siguen funcionando).

**Restricciones que condicionan el diseño:**

1. CONTPAQi (`ComercialSP.exe`) es una aplicación **de 32 bits** que hospeda el CLR
   de **.NET Framework** (no .NET Core). Cualquier componente que cargue debe ser
   **.NET Framework** y registrarse como **COM de 32 bits**.
2. CONTPAQi invoca botones mediante **COM por ProgID** (mecanismo de XEngine, ver
   §2). No hay API pública; se descubre por ingeniería inversa de la interfaz COM.
3. Se busca que **editar un botón no requiera recompilar**: pegar texto y ejecutar.
   Solución: hospedar **Roslyn** y ejecutar scripts `.csx`.
4. Multi-empresa sin configurar nada: **reutilizar la conexión viva** que CONTPAQi
   ya tiene abierta a la empresa activa.

---

## 2. Contrato con CONTPAQi (XEngine)

Cada botón del ribbon es un registro en la BD de la empresa
(`engRibbonControl.ControlExecute`) con el texto `Prefijo.AppKey`. Al presionarlo,
XEngine ejecuta **exactamente**:

```
1. Lee ControlExecute               -> "BrosLMV.SUMA"
2. Parte en el PRIMER punto          -> Prefijo="BrosLMV"  AppKey="SUMA"
3. ProgID = Prefijo + ".clsMain"     -> "BrosLMV.clsMain"
4. CreateObject("BrosLMV.clsMain")   -> instancia nuestro objeto COM
5. Setea propiedades públicas en el objeto (al menos XEngineLib; UserID a veces)
6. Llama  obj.ExecuteFunction("SUMA")
```

Por lo tanto, **lo único que CONTPAQi exige de nuestro componente** es:
- Registrarse como COM con **ProgID `<Prefijo>.clsMain`**.
- Exponer propiedades públicas seteables (`XEngineLib`, `UserID`, …).
- Exponer el método público `void ExecuteFunction(string appKey)`.

**Datos que CONTPAQi NO entrega:** los documentos seleccionados (la propiedad
`IDs` llega `null`). Hay que leerlos del grid (ver §8).

---

## 3. Arquitectura y archivos fuente

DLL `BrosLMVClsMain.dll` (+ DLLs de Roslyn, Scintilla y SQLite, ver §11). Archivos C#:

| Archivo | Responsabilidad |
|---------|-----------------|
| `ClsMain.cs` | Clase COM `clsMain` (ProgID/CLSID), `AssemblyResolve`, despachador `ExecuteFunction`, auditoría de cada botón. |
| `Scripting.cs` | `Com` (helpers late-binding), `GridSelection` (selección), `Conexion` (conexión viva), `ScriptContext` (`ctx`, incl. `SoloLectura`/`FilasAfectadas`/`Empresa`/`ModuloActivo`), `ScriptHost`, `ScriptRunner` (Roslyn). |
| `Datos.cs` | Almacenamiento SQLite (`C:\BrosLMV\data\broslmv.db`): auditoría de ejecuciones, recientes, favoritos. |
| `Consola.cs` | Ventana WinForms `BrosConsola` con editor Scintilla, biblioteca de scripts, inspector de `ctx`, ejecución segura e historial. |
| `Rutas.cs` | Rutas fijas `C:\BrosLMV\...` (bin, scripts, logs, data) y lectura del archivo de conexión de respaldo. |

---

## 4. Entorno y herramientas

| Para... | Necesitas | Verificado con |
|---------|-----------|----------------|
| Ejecutar en producción | **.NET Framework 4.8** + CONTPAQi Comercial PRO | release `528449` |
| Compilar | **.NET SDK** (8+) + internet a nuget.org (1ª vez) | SDK `10.0.300` |
| Registrar COM | `RegAsm.exe` de 32 bits (viene con .NET FW) | `C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe` |

> El destino es `net48`, **no** .NET Core: el componente debe correr en el CLR de
> .NET Framework que hospeda ComercialSP.exe.

---

## 5. El proyecto (.csproj)

SDK-style, target `net48`. Puntos clave y por qué:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>          <!-- corre en el CLR de CONTPAQi -->
    <OutputType>Library</OutputType>
    <AssemblyName>BrosLMVClsMain</AssemblyName>
    <RootNamespace>BrosLMV</RootNamespace>
    <LangVersion>latest</LangVersion>                 <!-- C# moderno (ya no C# 5) -->
    <PlatformTarget>AnyCPU</PlatformTarget>           <!-- AnyCPU carga en proceso 32-bit -->
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo><!-- usamos nuestro [assembly: ...] -->
    <AutoGenerateBindingRedirects>false</AutoGenerateBindingRedirects>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Rutas.cs" /><Compile Include="ClsMain.cs" />
    <Compile Include="Scripting.cs" /><Compile Include="Consola.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.8.0" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="System" /><Reference Include="System.Core" />
    <Reference Include="System.Data" /><Reference Include="System.Drawing" />
    <Reference Include="System.Windows.Forms" /><Reference Include="System.Xml" />
  </ItemGroup>
</Project>
```

- `Microsoft.NETFramework.ReferenceAssemblies` permite compilar `net48` con el SDK
  sin instalar el "targeting pack".
- Compilar: `dotnet build BrosLMV.csproj -c Release -o .\out`.

---

## 6. El COM server (`ClsMain.cs`)

### Atributos COM exactos

```csharp
[Guid("E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E")] // CLSID FIJO (no regenerar)
[ClassInterface(ClassInterfaceType.AutoDual)]   // expone props/métodos por IDispatch
[ProgId("BrosLMV.clsMain")]                      // <Prefijo>.clsMain
[ComVisible(true)]
public class clsMain { ... }
[assembly: AssemblyVersion("1.1.0.0")]
```

- El **GUID es fijo**: si cambia, el registro COM cambia y CONTPAQi deja de
  encontrar la clase.
- Propiedades públicas: `object XEngineLib`, `int UserID`, `int ModuleID`,
  `int BusinessEntityID`, `object IDs`, `bool MustRefreshList` (XEngine setea las
  que quiera; en la práctica solo `XEngineLib` llega confiable).
- Método: `public void ExecuteFunction(string appKey)`.

### Resolutor de ensamblados (CRÍTICO)

```csharp
static clsMain()
{
    AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
    {
        string dir  = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string name = new AssemblyName(e.Name).Name;
        string path = Path.Combine(dir, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    };
}
```

Ver §11 para por qué es indispensable.

### Despachador

```csharp
switch (appKey)
{
    case "CONSOLA": using (var c = new BrosConsola(UserID, XEngineLib)) c.ShowDialog(); break;
    case "PRUEBA":  /* mensaje */ break;
    default:        EjecutarScript(appKey); break;  // C:\BrosLMV\scripts\<appKey>.csx
}
```

`EjecutarScript` corre `Path.Combine(Rutas.Scripts, appKey + ".csx")` vía
`ScriptRunner.EjecutarArchivo`, con un `ScriptContext(UserID, XEngineLib)`.

---

## 7. El motor Roslyn (`Scripting.cs` → `ScriptRunner`)

- Paquete: `Microsoft.CodeAnalysis.CSharp.Scripting` **4.8.0**.
- `ScriptOptions` con **referencias** a los ensamblados ya cargados
  (`typeof(object).Assembly`, `Enumerable`, `DataTable`, `SqlConnection`, `Form`,
  `System.Drawing.Color`, y `typeof(ScriptContext).Assembly`) y **imports**:
  `System`, `System.Collections.Generic`, `System.Linq`, `System.Text`,
  `System.Data`, `System.Data.SqlClient`, `System.Windows.Forms`, `System.Drawing`,
  `BrosLMV`.
- **Globals:** `CSharpScript.Create(codigo, opciones, typeof(ScriptHost))`, donde
  `ScriptHost` tiene un campo público `ScriptContext ctx`. Por eso dentro del
  script `ctx` está disponible directamente.
- `Compilar(codigo)` → `script.Compile()` devuelve diagnósticos; filtramos
  `Severity == Error` y reportamos `línea N: mensaje (CSxxxx)`.
- `Ejecutar(codigo, ctx)` → compila; si no hay errores,
  `script.RunAsync(new ScriptHost{ctx}).GetAwaiter().GetResult()`.

---

## 8. Lectura de la selección del grid (`GridSelection`)

CONTPAQi no pasa los IDs. Se leen del grid visual (control **Janus GridEX**) por
su **ADO Recordset** enlazado. **Cadena exacta de miembros COM** (todo
late-binding sobre `System.__ComObject`):

```
XEngineLib
  .janusGrid                         -> control Janus GridEX
     .ADORecordset                   -> ADODB.Recordset (tiene columna "DocumentID")
     .SelectedItems                  -> colección de filas seleccionadas
        .Count                       -> int
        .Item(i)  (i = 1..Count)     -> item seleccionado
            .RowType                 -> 0 = fila de datos (saltar si != 0)
            .RowIndex                -> 1-based; = AbsolutePosition del recordset
```

**Algoritmo (no mover la selección visible):**

```
rs = janusGrid.ADORecordset
rsUse = rs.Clone()                    // clonar: navegar el clon no mueve el grid
para i en 1..SelectedItems.Count:
    it = SelectedItems.Item(i)
    si it.RowType != 0: saltar
    rowIndex = it.RowIndex
    rsUse.AbsolutePosition = rowIndex
    id = rsUse.Fields.Item(keyCol).Value     // keyCol = llave del módulo (ver abajo)
    agregar id (si > 0 y no duplicado)
rsUse.Close()
```

**Llave primaria por módulo (clave):** la columna a leer **no** es siempre
`DocumentID`. Cada módulo tiene su llave, guardada en la tabla
`engModuleParameter(ModuleID, ParameterKey, Value)`:

| Módulo | PrimaryKey | TableName |
|--------|-----------|-----------|
| Proveedores (76) | `SupplierID` | `orgSupplier` |
| Órdenes/Documentos (183…) | `DocumentID` | `docDocument` |
| Pagos/Cobros | `FinancialOperationID` | … |

`LlaveDeModulo` obtiene el módulo activo (`XEngineLib.ActiveModuleID`) y resuelve
la llave vía `XEngineLib.GetModuleParameter(modulo,"PrimaryKey")` o, si no,
consultando `engModuleParameter`; respaldo `DocumentID`. Se expone como
`ctx.GetSelectedIds()` → `List<long>` (devuelve los IDs de la llave del módulo).

> Nota: `AbsolutePosition = RowIndex` sobre el recordset enlazado da la fila
> correcta. Alternativa más robusta si fallara: usar el `Bookmark` del item.

---

## 9. Conexión automática (`Conexion`)

Reutiliza la conexión ADO que CONTPAQi tiene abierta a la empresa activa. **No**
usa archivo ni credenciales.

**Cómo se obtiene** (`Conexion.ObtenerAdo(xEngineLib)`), en orden:

```
1. janusGrid.ADORecordset.ActiveConnection   -> existe cuando hay una LISTA (grid)
2. XEngineLib.DataLayer                       -> disponible en CUALQUIER pestaña (incl. General)
3. DataLayer.Conexion / .Connection / ...     -> variantes
4. XEngineLib.Datalayers
```

**Validación (importante):** NO usar `.State == 1` para decidir si una conexión
sirve — el `DataLayer` de CONTPAQi tiene `.State` vacío aunque funcione. En su
lugar se prueba ejecutando `SELECT 1`: si `Connection.Execute("SELECT 1")` no
lanza, la conexión es usable. Esto permite conectar desde la pestaña **General**
(sin grid), no solo desde una lista.

**Ejecución de consultas** sobre esa conexión (late-binding):

- `Connection.Execute(sql)` → ADODB.Recordset. Se lee con `Fields.Count`,
  `Fields.Item(i).Name/.Value`, `EOF`, `MoveNext` (clase `Conexion.Leer`).
- `Scalar(sql)`  = primer campo de la primera fila.
- `Query(sql)`   = todas las filas como `List<Dictionary<string,object>>`.
- `NonQuery(sql)` = `Connection.Execute("SET NOCOUNT ON; " + sql + "; SELECT @@ROWCOUNT AS Afectadas")`
  y se lee `Afectadas`.

**Respaldo:** si no hay conexión viva (módulo sin grid), cae al archivo
`C:\BrosLMV\bin\broslmv_conn.txt` usando `SqlClient`. Además `OpenConn()` intenta
`XEngineLib.GetModuleConnectionString(0)` (quitando el prefijo `Provider=...;` para
que `SqlClient` lo acepte).

**Verificación en campo (2026-06-19):** `ActiveConnection` devolvió la conexión
viva; `DiagConexion()` reportó base `GRUPO_AGRIMAC_2021`. `GetModuleConnectionString`
no devolvió cadena (no se usó).

---

## 10. El contexto `ctx` (`ScriptContext`)

Constructor: `ScriptContext(int userId, object xEngineLib)`. Miembros:

| Miembro | Devuelve | Implementación |
|---------|----------|----------------|
| `GetSelectedIds()` | `List<long>` | §8 |
| `Scalar(sql)` | `object` | ADO viva → si no, SqlClient |
| `Query(sql)` | `List<Dictionary<string,object>>` | idem |
| `NonQuery(sql)` | `int` | idem (`@@ROWCOUNT`) |
| `OpenConn()` | `SqlConnection` | `GetModuleConnectionString` → archivo |
| `JoinIds(ids)` | `string` | `string.Join(",", ids)` |
| `Msg/Confirm` | `void`/`bool` | `MessageBox` |
| `Log(texto)` | `void` | `C:\BrosLMV\logs\Script_AAAAMMDD.txt` |
| `DiagConexion()` | `string` | diagnóstico de origen de conexión |
| `UserID` / `XEngineLib` | `int` / `object` | del constructor |

---

## 11. Las DLLs y la trampa de versiones (clave)

`dotnet build` produce y copia **15 DLLs administradas** (que deben viajar
**juntas** en `C:\BrosLMV\bin`) más **1 DLL nativa** de SQLite:

| DLL | Versión / nota |
|-----|----------------|
| `BrosLMVClsMain.dll` | 1.2.0.0 (nuestra) |
| `Microsoft.CodeAnalysis.dll` | 4.8.0.0 (Roslyn) |
| `Microsoft.CodeAnalysis.CSharp.dll` | 4.8.0.0 |
| `Microsoft.CodeAnalysis.Scripting.dll` | 4.8.0.0 |
| `Microsoft.CodeAnalysis.CSharp.Scripting.dll` | 4.8.0.0 |
| `System.Collections.Immutable.dll` | 7.0.0.0 |
| `System.Reflection.Metadata.dll` | 7.0.0.0 |
| `System.Memory.dll` | 4.0.1.2 |
| `System.Buffers.dll` | 4.0.3.0 |
| `System.Numerics.Vectors.dll` | 4.1.4.0 |
| `System.Runtime.CompilerServices.Unsafe.dll` | 6.0.0.0 |
| `System.Text.Encoding.CodePages.dll` | 7.0.0.0 |
| `System.Threading.Tasks.Extensions.dll` | 4.2.0.1 |
| `ScintillaNET.dll` | 3.6.3 — editor de código. Embebe `SciLexer.dll` (x86/x64) y lo autoextrae; **no** se distribuye aparte. |
| `System.Data.SQLite.dll` | 1.0.118 — acceso a SQLite |
| `bin\x86\SQLite.Interop.dll` | **nativo x86** de SQLite. Se carga aparte (no por `AssemblyResolve`); System.Data.SQLite lo busca según `PreLoadSQLite_BaseDirectory` = `C:\BrosLMV\bin` (lo fija `Datos.Inicializar`), por eso va en `bin\x86\`. |

> ⚠️ **Trampa (la lección más importante):** estas `System.*` tienen referencias
> internas a versiones distintas entre sí (p.ej. `System.Memory` pide una versión
> de `System.Runtime.CompilerServices.Unsafe` que no es la presente). En .NET
> Framework eso se arregla con **binding redirects**, pero (a) `dotnet build` con
> `AutoGenerateBindingRedirects=false` no los genera y (b) aunque los generara, el
> `.dll.config` **no** se consulta al hospedarse dentro de `ComercialSP.exe`.
> **Síntoma:** al compilar un script falla con
> `TypeInitializationException` de `PerTypeValues\`1`.
> **Solución:** el `AssemblyResolve` del §6 hace de binding redirect: ante cualquier
> versión solicitada entrega el archivo que esté en la carpeta. **Requisito:** ese
> resolutor se registra al **instanciar `clsMain`**, que es justo lo que hace
> CONTPAQi por COM. (Comprobado: en un `.exe` x86 de prueba, sin instanciar
> `clsMain` falla; instanciándolo compila con 0 errores.)

---

## 12. Instalación en disco

```
C:\BrosLMV\
├── bin\          <- las 15 DLLs administradas + broslmv_conn.txt (respaldo)
│   └── x86\      <- SQLite.Interop.dll (nativo)
├── scripts\      <- los .ctx (cada botón es un archivo); _historial\ (versiones)
├── data\         <- broslmv.db (SQLite: auditoría, recientes, favoritos)
└── logs\         <- bitácoras de ctx.Log
```

Rutas fijas en `Rutas.cs` (`Base = C:\BrosLMV`). Si se cambian, actualizar también
el instalador y la documentación.

---

## 13. Registro COM (32 bits) — con la trampa del hive

1. Registrar con el **RegAsm de 32 bits**:
   `…\Framework\v4.0.30319\RegAsm.exe  C:\BrosLMV\bin\BrosLMVClsMain.dll  /codebase /tlb`
2. **Trampa:** RegAsm suele escribir el mapeo **ProgID→CLSID** solo en el hive de
   64 bits (`HKLM\SOFTWARE\Classes`), pero ComercialSP (32-bit) lo busca en
   **`HKLM\SOFTWARE\WOW6432Node\Classes`**. Hay que asegurar en WOW6432Node:
   - `…\Classes\BrosLMV.clsMain\CLSID` (default) = `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`
   - `…\Classes\CLSID\{GUID}\InprocServer32` con:
     - (default) = `mscoree.dll`
     - `ThreadingModel` = `Both`
     - `Class` = `BrosLMV.clsMain`
     - `Assembly` = `BrosLMVClsMain, Version=1.2.0.0, Culture=neutral, PublicKeyToken=null`
     - `RuntimeVersion` = `v4.0.30319`
     - `CodeBase` = `file:///C:/BrosLMV/bin/BrosLMVClsMain.DLL`

   `Instalar.ps1` ya fuerza esto (crea el mapeo ProgID en WOW6432Node y, si hace
   falta, copia el árbol del CLSID con `reg copy`).

---

## 14. Alta del botón en la BD de la empresa

Tres tablas (todas con **PK IDENTITY** → no especificar el ID, usar
`SCOPE_IDENTITY()`). Lo hace `sql\provision_empresa.sql` de forma **idempotente**;
esta sección describe el mecanismo para reconstruirlo.

### `engRibbonGroup` (el grupo "BrosLMV")
El botón vive en un grupo propio **"BrosLMV"** en la pestaña **General genérica**
(`RibbonTabID = 1`, `ModuleID = 0` → visible en todos los módulos). Si no existe, se
inserta con `GroupCaption='BrosLMV'`, `RibbonGroupIDBase=0`, `GroupOrder=99` y se
toma su `RibbonGroupID` con `SCOPE_IDENTITY()`. Es el grupo destino para el control.

### `engRibbonControl` (definición del botón)
Columnas: `ControlID`(IDENTITY), `ControlIDBase`, `ProductID`, `ModuleID`,
`ControlCaption`, `ControlDescription`, `ControlExecute`, `IconFile`,
`SystemButton`, `SystemButtonOrder`, `SystemButtonBeginGroup`,
`SystemButtonParentID`, `QuickAccessShow`, `QuickAccessSection`,
`QuickAccessCaption`, `QuickAccessOrder`, `Shortcut`, `ResID`, `ResIDDescription`,
`Comments`, `AFP`.
Valores conocidos-buenos: `ControlIDBase=0, ProductID=1, ModuleID=0`, el resto
`0`/`NULL`, `ControlExecute='BrosLMV.<AppKey>'`.

### `engRibbonMenu` (ubicación en el ribbon)
Columnas: `RibbonMenuID`(IDENTITY), `RibbonMenuIDBase`, `RibbonGroupID`,
`ControlID`, `ControlOrder`, `ControlType`, `ExtraMenuModuleID`, `IfFieldsExist`,
`IfUserIDIs`.
Valores: `RibbonMenuIDBase=0, RibbonGroupID=<grupo destino>, ControlID=<el nuevo>,
ControlOrder=<n>, ControlType=1, ExtraMenuModuleID=0, IfFieldsExist=NULL,
IfUserIDIs=0`.

**`RibbonGroupID` destino:** el del grupo propio "BrosLMV" recién creado en la
pestaña General (ver arriba). `plantilla_crear_boton.sql` (para botones extra) usa,
si no se indica, el grupo donde ya viven otros botones `BrosLMV.%` y, como último
recurso, el grupo del ribbon más poblado:
```sql
SELECT TOP 1 RibbonGroupID FROM engRibbonMenu
GROUP BY RibbonGroupID ORDER BY COUNT(*) DESC;
```
Para inspeccionar los grupos existentes hay `sql\0_buscar_grupos.sql` (diagnóstico).
Tras insertar, **reiniciar CONTPAQi** (el ribbon está cacheado).

---

## 15. Procedimiento de reconstrucción desde cero

1. Crear los 5 `.cs` (§3) y el `.csproj` (§5) con namespace `BrosLMV`, ProgID y
   CLSID fijos (§6).
2. Implementar: `Rutas` (§12), `Com`+`GridSelection` (§8), `Conexion`+`ScriptContext`
   (§9-10), `ScriptRunner` (§7), `Datos` (SQLite), `BrosConsola` (editor),
   `clsMain` (§6).
3. `dotnet build -c Release -o .\out` → copiar las DLLs (§11) a `C:\BrosLMV\bin` y
   el nativo `out\x86\SQLite.Interop.dll` a `C:\BrosLMV\bin\x86\`.
4. Registrar COM de 32 bits + asegurar WOW6432Node (§13).
5. Crear `C:\BrosLMV\{scripts,logs}` y (opcional) `broslmv_conn.txt`.
6. Dar de alta el botón `BrosLMV.CONSOLA` en la BD (§14) y reiniciar CONTPAQi.
7. Probar con `DIAGNOSTICO.csx` que la conexión viva (`DiagConexion`) diga "SI".

---

## 16. Resumen de trampas aprendidas

| Trampa | Efecto | Solución |
|--------|--------|----------|
| ComercialSP es 32-bit | El COM debe estar en WOW6432Node | RegAsm 32-bit + asegurar el hive (§13) |
| RegAsm escribe ProgID solo en 64-bit | CONTPAQi no encuentra la clase | Crear el mapeo en WOW6432Node (§13) |
| Roslyn arrastra `System.*` con versiones incompatibles | `TypeInitializationException PerTypeValues` | `AssemblyResolve` por nombre (§11) |
| `AssemblyResolve` solo se registra al instanciar `clsMain` | Falla si no se toca `clsMain` | CONTPAQi lo instancia por COM (OK) |
| XEngine no pasa los IDs seleccionados | `IDs` llega null | Leer del grid (§8) |
| Navegar el recordset mueve la selección visible | El usuario ve otro doc seleccionado | Clonar el recordset (§8) |
| Conexión por empresa es inviable (40 bases) | Config manual masiva | Reutilizar la conexión viva (§9) |
| Compilador `csc` de Windows solo soporta C# 5 | No compila C# moderno | Compilar con **.NET SDK** (`dotnet build`) |
| Tablas del ribbon con PK IDENTITY | `IDENTITY_INSERT OFF` error | No fijar el ID; `SCOPE_IDENTITY()` (§14) |
| Cambiar `AssemblyVersion` | El `InprocServer32` guarda `Assembly=...,Version=X`; si la DLL cambia de versión hay que **re-registrar** (RegAsm) o la activación COM puede fallar | Re-correr RegAsm tras subir versión, o mantenerla estable entre parches |
| Roslyn: 1ª compilación lenta (~2s) y compilar dos veces | La consola tardaba en ejecutar | Compilar 1 vez, **cachear** el script, **precalentar** Roslyn en segundo plano |
