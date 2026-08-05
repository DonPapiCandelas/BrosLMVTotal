# Investigación: ¿pueden las plantillas SQL_PURO invocar XEngine tras sus INSERTs?

Fecha: 2026-08-04. Solo investigación, sin cambios de código.

## Respuesta corta

**SÍ es viable — y de hecho ya existe la infraestructura para hacerlo.** No es "hay que
construir algo nuevo", es "hay que usarlo también en las plantillas SQL_PURO". El addon
BrosLMV corre EMBEBIDO dentro del proceso de `ComercialSP.exe` (igual que AccesoFacil), y
`ctx.erp` ya es un wrapper tipado sobre el mismo `XEngineLib` que usa el script
`RecepcionOC` de Business Conexión, con los métodos exactos que ese script llama a mano vía
`Doc.clsMain`/`gl['main'].XEngineLib`.

## Evidencia

### 1. Arquitectura: el addon SÍ está embebido en el proceso de Comercial (no es un proceso aparte)

`src/ClsMain.cs`:
```
// ClsMain.cs
// COM server que XEngine (CONTPAQi Comercial) instancia via ProgID "BrosLMV.clsMain".
// Se ejecuta de forma autonoma, en proceso, sin servicios de licencia externos.
//
// XEngine: lee ControlExecute "BrosLMV.<AppKey>" -> CreateObject("BrosLMV.clsMain")
//          -> setea XEngineLib/UserID -> llama ExecuteFunction("<AppKey>").
```
`clsMain.XEngineLib` es una propiedad `object` que **XEngine mismo setea** antes de
`ExecuteFunction`. Es decir: BrosLMV recibe el `XEngineLib` COM vivo del proceso de
Comercial exactamente igual que el script Python de AccesoFacil recibe `gl['main'].XEngineLib`
— la diferencia es el mecanismo de invocación (`ControlExecute "BrosLMV.<AppKey>"` vs botón
AccesoFacil), pero el resultado es el mismo objeto COM en el mismo proceso, no una llamada
cross-process ni un archivo/servicio externo.

Confirmado también en el log de diagnóstico (`Com.DiagLog` en `ExecuteFunction`), que imprime
`proceso=" + Process.GetCurrentProcess().ProcessName` — es literalmente el proceso de
Comercial, no un `BrosLMV.Runner.exe`/`BrosLMV.Host.exe` separado (ese `HostClient.cs` existe
para OTRO propósito: un proceso auxiliar aparte para tareas que no requieren XEngine, no es el
camino de ejecución de estas plantillas).

### 2. `ctx.erp` ya es un wrapper sobre XEngine con los métodos exactos que usa RecepcionOC

`src/Scripting.cs`, clase `ErpContext` (líneas ~1421-1519), construida sobre el mismo `_xe`
(el `XEngineLib` COM):

```csharp
public void AffectStockNEW(int documentId)  { Com.Call(_xe, "AffectStockNEW", new object[] { (long)documentId }); }
public void CalcularCostos(int documentId)  { Com.Call(_xe, "CalcularCostos", new object[] { (long)documentId }); }
public void RecalcDocument(int documentId)  { var doc = CrearHelper("Doc.clsMain"); Com.Call(doc, "RecalcDocument", new object[] { (long)documentId }); }
public void RecalcCompleto(int documentId)  { RecalcDocument(documentId); CalcularCostos(documentId); UpdateDocumentPaidInfo(documentId); }
```

`CrearHelper("Doc.clsMain")` es el mismo patrón que `Interaction.CreateObject('Doc.clsMain')`
del script Python: crea el helper COM y le inyecta el mismo `XEngineLib` (ver
`ErpContext.CrearHelper`, que existe justo para esto — confirmado en el catálogo de métodos de
`Consola.cs` línea 241: `"Crea un COM auxiliar (Doc.clsMain, LBS.clsMain) con XEngine."`).

Faltan wrappeados explícitamente `RecalcCostComercial`/`RecalcCostFiscal` (los dos únicos de
la secuencia de 5 llamadas de `RecepcionOC` que `ErpContext` no expone como método propio),
pero el mecanismo genérico ya cubre eso sin necesitar código nuevo:

```
new MetodoCtx("erp.Call", "ctx.erp.Call(metodo, args...) : object",
  "Llama CUALQUIER miembro de XEngine por nombre (los 562). Tú das los argumentos.", ...)
```
— es decir, `ctx.erp.Call("RecalcCostComercial", id)` y `ctx.erp.Call("RecalcCostFiscal", id)`
ya son invocables HOY sin tocar `Scripting.cs`, con el mismo late-binding
(`Type.InvokeMember`) que usa el resto de `ErpContext`.

### 3. El propio código ya documenta la política "usa ctx.erp primero"

`instalador/scripts/PLANTILLA_REQUISICION_SQL_PURO.sql` (líneas 2-30):
```
-- PLANTILLA: Requisición de Compra — SQL puro (INSERT directo, sin ctx.erp)
...
-- estándar general del proyecto es "usa ctx.erp primero, SQL solo para lo que no cubre".
```
Es decir, el proyecto ya reconoce que `ctx.erp` es el camino preferido y que SQL_PURO es la
excepción frágil — la pregunta del usuario encaja exactamente en esa política ya declarada.

### 4. El hueco real: las plantillas SQL_PURO (y sus híbridos Forms+SQL_PURO) hoy NO llaman a
   `ctx.erp` después del INSERT

Grep de `ctx.erp.RecalcDocument|CalcularCostos|AffectStockNEW|Call` sobre
`PLANTILLA_RECEPCION_COMPRA_FORMS_SQL_PURO_CSHARP.ctx` (que sí es C#, con acceso pleno a
`ctx.erp`, a diferencia de las `.sql` puras que corren como texto T-SQL vía
`ScriptContext.EjecutarSql`) no encontró ninguna llamada — confirmando que hoy el cálculo de
kardex/costos/totales en esa plantilla se sigue haciendo a mano por SQL
(`PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql` calcula `@totalCost`, hace `UPDATE ... TotalCost =
TotalCost + @totalCost` e inserta directo en `orgProductKardex`, con comentarios propios
advirtiendo sobre el signo de `QuantityToBeDelivered` y sobre que `CostPrice se queda en 0`).

### 5. Matiz importante: las `.sql` puras no tienen `ctx.erp`, los `.ctx` (C#) sí

Las plantillas `PLANTILLA_*_SQL_PURO.sql` (extensión `.sql`) se ejecutan como texto T-SQL
crudo vía `ScriptContext.EjecutarSql()` (ver `Scripting.cs` línea ~508) — ese camino NO tiene
acceso a `ctx.erp` porque no hay C#/Python de por medio, solo SQL. Para poder llamar a
`ctx.erp.RecalcDocument`/`AffectStockNEW`/`CalcularCostos` después del INSERT, el script tiene
que ser un `.ctx`/`.csx` (C#) que use `ctx.NonQuery`/`ctx.Query` para los INSERTs (en vez de,
o encapsulando, el SQL puro) y luego `ctx.erp.*` para el recalculo — que es exactamente lo que
ya hacen los híbridos `PLANTILLA_*_FORMS_SQL_PURO_CSHARP.ctx` existentes, solo que hoy se
detienen en el INSERT y no dan el paso final.

No se realizó experimento en vivo contra el sandbox (no fue necesario: `ctx.erp` ya está
verificado en producción — es el mismo mecanismo que usan las plantillas
`PLANTILLA_RECEPCION_COMPRA_FORMS_CSHARP.ctx` no-SQL-puro, que según el historial de commits
(`v2.64.0 - Recepcion de Compra COMPLETA: las 6 variantes`) ya están validadas y funcionando
contra `localhost\compac` para afectar kardex/costos correctamente).

## Cómo se vería (esbozo, sin implementar)

En los `.ctx` híbridos Forms+SQL_PURO (los únicos con acceso a `ctx.erp`), después del
`COMMIT` de la transacción de INSERTs (mismo patrón que `RecepcionOC`: SQL primero, motor
después, fuera de la transacción SQL):

```csharp
// ... INSERTs vía ctx.NonQuery dentro de BEGIN TRAN/COMMIT TRAN (como ya hacen hoy) ...
ctx.erp.RecalcDocument(nuevoDocId);        // totales: subtotal, IVA, total
ctx.erp.AffectStockNEW(nuevoDocId);        // kardex (afecta inventario)
ctx.erp.CalcularCostos(nuevoDocId);        // costo promedio/PEPS
ctx.erp.Call("RecalcCostComercial", nuevoDocId);
ctx.erp.Call("RecalcCostFiscal", nuevoDocId);
if (ctx.erp.LastError != null) { /* log + fallback al cálculo manual ya existente */ }
```

Esto reemplazaría (o dejaría como fallback verificable, comparando resultados) los bloques que
hoy calculan `@totalCost`, hacen `UPDATE ... TotalCost = TotalCost + @totalCost` e insertan a
mano en `orgProductKardex` con el signo corregido "a ojo". `ctx.erp.LastError` (ya expuesto)
serviría para detectar si alguna llamada de XEngine falló y decidir si abortar o caer al
cálculo manual como red de seguridad.

Las plantillas `.sql` puras (sin C#) no podrían adoptar esto directamente — seguirían
necesitando el cálculo manual documentado en
`entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md`, salvo que se conviertan a variante
`.ctx` (que ya existe como opción paralela para cada documento, según el catálogo de 6
variantes mencionado en el commit `v2.64.0`).

## Si no fuera viable (alternativa, no aplica aquí pero se deja documentado)

De no existir `ctx.erp`/acceso a XEngine desde el proceso del addon, la alternativa sería
seguir calculando a mano por SQL pero con las fórmulas corregidas — documentadas en
`entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md` (signo de
`QuantityToBeDelivered`, `TotalCost` no debe quedar en 0, etc.). Esa alternativa YA está en
uso hoy en las `.sql` puras y en los híbridos `.ctx` que aún no llaman a `ctx.erp` tras el
INSERT; queda como camino necesario únicamente para las plantillas `.sql` puras que no pueden
volverse C#.
