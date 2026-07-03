# Referencias de la consola y verificación del API `ctx` / `ctx.erp`

> **Documento de continuación.** Explica el sistema de "Referencias" de la consola, el API real
> que reflejan, cómo se exponen TODAS las funciones de XEngine y el **método para verificarlas
> sin ir una por una**. Pensado para que cualquier agente/persona retome el trabajo.
>
> Estado al 2026-06-27 (v2.11.1): **C#, Python y SQL** alineados con el API real. C# además
> **verificado** en CONTPAQi; falta verificar Python/SQL con un script de solo lectura.

---

## 1. Qué son las "Referencias"

En la consola (panel derecho) hay un panel **Referencias** con pestañas **C# · Python · SQL ·
Datos**. Lista los miembros de `ctx` disponibles en cada lenguaje; **doble clic inserta** el
ejemplo en el editor.

En el código son **catálogos escritos a mano** en `src/Consola.cs`:
- `METODOS` (C#), `METODOS_PYTHON`, `METODOS_SQL` — arreglos de `MetodoCtx(Nombre, Firma,
  Desc, Ejemplo, Cat)`. `Cat` agrupa visualmente (solo C# usa grupos hoy).
- Se muestran en `CargarMetodos()`. El autocompletado de `ctx.` también sale de `METODOS`.

**Problema que resuelve este trabajo:** al ser listas a mano, **se desincronizan** del API real
y tenían métodos faltantes, mal escritos o inventados. Hay que mantenerlas fieles al código.

## 2. API canónica (la fuente de verdad) = `src/Scripting.cs`

- **`class ScriptContext`** = el `ctx` base. Miembros: props `UserID`, `XEngineLib`,
  `SoloLectura`, `FilasAfectadas`; métodos `GetSelectedIds`, `Scalar`, `Query`, `NonQuery`,
  `JoinIds`, `Msg`, `Confirm`, `Log`, `Empresa`, `ServidorActivo`, `ModuloActivo`,
  `EjecutarSql`, `DiagConexion`, `ResolverTokens`, `GetFilaActiva`.
  (Ojo: `ctx.UserID` suele venir **0**; el real es `ctx.erp.UserId`.)
- **`class ErpContext`** = `ctx.erp.*` — wrapper tipado sobre XEngine (~60 métodos envueltos).
  **Para firmas exactas, leer `Scripting.cs` — no inventar.**

## 3. Acceso genérico a TODO XEngine: `ctx.erp.Call` / `ctx.erp.Get`

XEngine (`XengineLib.clsMain`) expone **~562 miembros**; `ctx.erp` solo envuelve los más usados.
Para el resto, sin adivinar firmas:

```csharp
var qr  = ctx.erp.Call("GetQRCode", "datos");     // método por nombre (late-bound)
var rfc = (string)ctx.erp.Get("COMERCIAL_RFC");   // propiedad por nombre
```

`Call` = métodos (`INVOKE_FUNC`), `Get` = propiedades (`INVOKE_PROPERTYGET`). Una propiedad-put
se asigna con `Com.SetProp` desde el wrapper.

## 4. Recursos de XEngine

> Estos recursos son de la instalación local de CONTPAQi, no del repositorio.

- **XEngineLib** expone alrededor de 562 miembros. Puede enumerarse por reflexión COM (tipo,
  `INVOKE_FUNC` vs `INVOKE_PROPERTYGET/PUT`, número de parámetros) sin necesidad de tener
  CONTPAQi abierto — así se auditan firmas antes de usarlas desde un script.
- El esquema real de la base de datos (tablas/vistas/SPs) se consulta directamente contra la
  instancia SQL Server de la empresa objetivo.

## 5. Cómo verificar las funciones (sin ir una por una)

Tres niveles, de barato a caro:

### 5.1 Auditoría estática (sin CONTPAQi)
Cruza cada `Com.Call(_xe,"X",…)` / `Com.GetProp(_xe,"X")` de `ErpContext` contra el dump COM:
detecta **método-vs-propiedad** y **args de más**. Script: `.temp_tests/audit_erp.py`
(`python .temp_tests/audit_erp.py`). Así se halló que `GotoModuleID` era propiedad, no método.

> Limitación: el dump da tipo + nº de params, **no los tipos** de cada parámetro. Un error de
> tipo (p. ej. pasar string donde va un int) NO lo caza la auditoría — eso lo caza el lote (5.2).

### 5.2 Verificador por lotes (solo lectura, en la consola)
Un único script C# que descubre IDs reales de la empresa y prueba **decenas de getters de un
jalón**, imprimiendo `[valor]/[NULL]/[ERROR]`. Patrón:

```csharp
var sb = new System.Text.StringBuilder();
Action<string, Func<object>> p = (n, f) => {
    try { var v = f(); sb.AppendLine(n + " = [" + (v==null?"NULL":v.ToString()) + "]"); }
    catch (Exception ex) { sb.AppendLine(n + " = ERROR: " + ex.Message); }
};
long doc = ctx.GetSelectedIds().Count>0 ? ctx.GetSelectedIds()[0] : 0;
p("erp.UserId",        () => ctx.erp.UserId);
p("DLookup(Total)",    () => ctx.erp.DLookup("Total","docDocument","DocumentID="+doc));
p("Call(GetQRCode)",   () => ctx.erp.Call("GetQRCode","12345"));
// ... agregar los que se quieran verificar ...
ctx.Msg(sb.ToString(), "Verificador");
```

Interpretación: `[valor]` = OK; `[NULL]`/`[]` = revisar firma (tipo/args) **o** simplemente no
hay dato (producto sin precio, módulo sin folio, etc.); `[ERROR]` = corregir/quitar.

### 5.3 Operaciones de ESCRITURA: una por una
`Save`, `CancelDocument`, `AffectStockNEW`, `Delete`, `RecalcCompleto`, etc. **modifican datos**:
no se prueban en lote. Verificar cada una **a propósito sobre un documento desechable**, y solo
el día que se vaya a usar.

## 6. Hallazgos verificados en CONTPAQi (2026-06-27, v2.11.0)

- Contexto de XEngine llega bien: `ctx.erp.UserId=1` (real, no 0), `UserName=Admin`,
  `SoftwareVersion=11.0.0`, `CurrencyId=3` (=MXN). `Call`/`Get` genéricos funcionan
  (`Call("GetQRCode")` → PNG base64; `DLookup` total real = 73080).
- **`GetTotalLetter(amount, currencyId)`** — 2º param es CurrencyId **int** (`3`→pesos,
  `1`→EUR); con string daba vacío. Corregido (default = moneda activa).
- **`GotoModuleID`** — propiedad-put; corregido a `Com.SetProp`.
- **`NumDecimales*`** — devolvían NULL; quitados (usar `Call` o SQL si se necesitan).
- Precios/stock dieron 0 porque el producto de prueba era placeholder y se estaba en el módulo
  menú; **no** son bugs. Para confirmarlos: correr el lote dentro de un módulo de documento con
  un producto que tenga existencia/precio.
- Menores pendientes de afinar cuando se usen: `GetBarCode` (2º param quizá int) y `GetParameter`
  (necesita >1 arg).

### 6.1 Verificación por lotes (2026-06-27, v2.11.2)
Con el script `.temp_tests/verif_lote_csharp.ctx` (auto-descubre doc/producto/almacén/cliente
con `docDocument JOIN docDocumentItem` y prueba en SOLO LECTURA):
- **Corregido `GetPriceWithTaxes`**: XEngine espera **`(taxTypeId, price)`**, no `(price,taxTypeId)`.
  `(100,1)` daba `1.16`; tras invertir el orden a COM, `(100,1)=116`. Firma pública intacta.
  Sonda: `(1,100)=116`, `(1,200)=232`, `(100,0)=0`.
- **OK (con dato real):** `GetProductStock` (10/960), `GetCostPrice` (1500), `GetCurrencyRate`,
  `ProductIsKit`, `GetNextFolio`, `VerifyCreditLimit/Overdue`, `DLookup/DLookupStr`,
  `GetTotalLetter`, contexto ERP completo.
- **`GetSalePrice`/`GetBuyPrice` = 0** → producto sin precio capturado (`orgProductPriceList`
  vacío para ese ProductID). El 0 es correcto, no bug.
- **`GetFolioPrefix` vacío** → los docs de prueba traen `DepotID=0`; necesita un almacén real.

## 7. Estado por lenguaje y cómo verificar

| Lenguaje | Catálogo (`src/Consola.cs`) | Fuente de verdad | Estado |
|----------|-----------------------------|------------------|--------|
| C#       | `METODOS` | `src/Scripting.cs` (`ScriptContext`/`ErpContext`) + dump COM | Alineado **y verificado** en CONTPAQi (v2.11.0) |
| Python   | `METODOS_PYTHON` | `workers/python/broslmv/ctx.py` | Alineado (v2.11.1) — **falta verificar** con script |
| SQL      | `METODOS_SQL` | `ScriptContext.EjecutarSql` + `ResolverTokensCore` | Alineado (v2.11.1) — **falta verificar** con script |

### Correcciones hechas en v2.11.1 (y actualizadas en v2.12.0+)
- **Python:** `ctx.erp.*` **SÍ existe en Python** (desde v2.12.0, relay al addon vía pipe). Los 84
  métodos de `ctx.erp` están disponibles con los mismos nombres PascalCase que en C#. Ver `PYTHON.md`.
  Los ejemplos de `query/scalar/execute` usan **dict + placeholders `@nombre`** (no kwargs); se agregaron
  las propiedades reales (`user_id`, `module_id`, `empresa`, `app_key`, `fila`, `context()`) y la
  variable de retorno `result`.
- **SQL:** se agregaron los tokens faltantes `{pModulo}` y `{pEmpresa}` (los 6 reales:
  `{pID}`, `{pIDs}`, `{pUserID}`, `{pModulo}`, `{pEmpresa}`, `{DATOS:Campo}`), más `EXEC`/`INSERT`/
  `DELETE` con esquema real (`docDocument`, `DeletedOn IS NULL`) y la nota de SOLO LECTURA.

### Verificación pendiente (Python/SQL)
Igual que en C#: un **script de solo lectura** en la consola que pruebe los miembros de un jalón.
- Python: un `.py` que lea `ctx.user_id/empresa/fila/get_selected_ids()` y un `ctx.query(...)`
  con `@param`, y devuelva todo en `result`.
- SQL: un `SELECT ... WHERE DocumentID IN ({pIDs})` y un `SELECT '{pEmpresa}', {pModulo}`.

Tras cada cambio: subir `AssemblyVersion`, entrada en `CHANGELOG.md`, y desplegar la DLL a
`C:\BrosLMV\bin` (la consola se carga en CONTPAQi; **reiniciar Comercial PRO** para tomar la DLL
nueva; si está abierto, el archivo queda bloqueado).
