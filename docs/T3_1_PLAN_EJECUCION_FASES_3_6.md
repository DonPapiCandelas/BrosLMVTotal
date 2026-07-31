# T3.1 — Plan de ejecución, fases 3-6 (para delegar a otra IA)

> **Qué es este documento.** Instrucciones paso a paso, sin ambigüedad, para terminar el
> motor de recetas no-code (`docs/RECETAS_NOCODE.md`). Las fases 1-2 ya están hechas
> (v2.43.0/v2.44.0) — ver `docs/CHANGELOG.md` para el detalle completo de lo que ya existe.
> Este doc asume que quien lo ejecute **no conoce el proyecto** y no debe inventar
> decisiones de diseño: todas las decisiones ya están tomadas aquí. Si algo no está claro
> en este documento, es un defecto del documento — pregúntale al usuario antes de adivinar.

---

## 0. Reglas obligatorias (leer antes de tocar código)

Estas reglas aplican a **las 4 fases**, no se repiten en cada una:

1. **Nunca inventes con mocks.** Todo se prueba contra la base de datos sandbox real:
   `ComercialSP` en `localhost\compac` (SQL Server, autenticación Windows —
   `sqlcmd -S "localhost\compac" -E -d ComercialSP -Q "..."`). Ya está provisionada
   (`zzBrosInfo.ProvisionVersion=2.41.0`, tablas `zzBros*`).
2. **Regla de oro del proyecto — un cambio de código nunca va sin:**
   - Subir `AssemblyVersion` en `src\ClsMain.cs` (línea ~31, formato `"X.Y.Z.0"`).
   - Entrada nueva en `docs\CHANGELOG.md` (arriba del todo, después del encabezado del
     archivo) con el mismo número de versión `[X.Y.Z]`.
   - Entrada nueva en `src\assets\notas_version.html` (`<div class="ver"><h2>X.Y.Z</h2>...`)
     — **arriba** de la entrada anterior.
   - Todo en el **mismo commit**.
   - Verificar con: `powershell -NoProfile -File "C:\MLVTotal\build\verificar_regla_de_oro.ps1"`
     — debe decir "OK". Si dice error, falta algo de lo anterior.
3. **Todo patrón/gotcha nuevo → también en `docs\MANUAL.md`**, sección que corresponda
   (o una nueva). No basta con el CHANGELOG.
4. **Antes de cada commit, correr el arnés de humo completo y que salga en verde:**
   ```
   cd C:\MLVTotal
   .\build\probar_humo.ps1
   ```
   Si algo sale en rojo, no se comitea hasta arreglarlo (o hasta entender por qué es un
   fallo esperado y ajustar el caso).
5. **Cada pieza ejecutable nueva (una receta, un dispatch nuevo) se agrega como un caso
   nuevo y PERMANENTE del arnés** (`build\humo\casos\NN_nombre.ps1`, siguiente número
   disponible — hoy van 8, el siguiente es `09_...`). No es opcional: es la única forma de
   que quede probado de verdad y de que nadie lo rompa después sin darse cuenta. Copia el
   patrón de un caso existente (p. ej. `build\humo\casos\08_receta_sql_tokens.ps1`) — no
   inventes una estructura nueva.
6. **Compila antes de dar por terminada una fase:**
   ```
   dotnet build src\BrosLMV.csproj -c Release
   dotnet build runner\BrosLMV.Runner.csproj -c Release
   ```
   0 errores en ambos. Si tocaste `src\Consola.cs` o cualquier `.cs` enlazado también en el
   Runner (`Recetas.cs`, `Scripting.cs`, `HostClient.cs`), compila los dos.
7. **Error de compilación `MSB4025` con "--" en un comentario:** es un error real y
   frecuente en este repo. Los archivos `.csproj` son XML — un comentario `<!-- ... -- ... -->`
   con doble guion adentro rompe la carga del proyecto. Revisa cualquier comentario que
   agregues en un `.csproj` y evita `--` literal.
8. **Nunca borres datos de prueba de otros casos del arnés** (`HUMO_PROD-001`,
   `HUMO-PROV-001`, los botones `HUMO_*` en `zzBrosScript`) — son fixtures compartidos que
   otros casos reutilizan. Solo limpia lo que tú creaste específicamente para probar algo
   puntual y que no quede como caso permanente.
9. **Git:** commits normales (no `--amend`, no `--force`), mensaje en español describiendo
   qué y por qué, termina con:
   ```
   Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
   ```
   (o el nombre del modelo que estés usando). Push a `main` directo — este repo no usa PRs
   (verás un aviso de "Bypassed rule violations", es normal, ignóralo).

**Antes de empezar cualquier fase, lee estos archivos reales del repo (no asumas nada):**
- `docs/RECETAS_NOCODE.md` — el diseño original completo.
- `src/Recetas.cs` — el motor ya construido (fases 1-2), es el patrón a seguir.
- `docs/MANUAL.md` §6 (`ctx.erp`) y §7 (Crear documentos) — el API real de XEngine que vas
  a usar.
- `build/humo/casos/03_crear_oc.ps1` + `03_crear_oc.codigo.cs` — el patrón de caso de humo
  que crea un documento real (lo vas a necesitar como referencia en la fase 4).

---

## Fase 3 — Almacén de estructuras de documento

**Objetivo:** metadatos por tipo de documento destino (¿qué perfil de encabezado necesita?
¿afecta inventario? ¿qué post-proceso de XEngine aplica?) para que la fase 4 (receta
estrella) sea genérica en vez de tener un `if` por cada tipo de documento.

**Por qué existe:** `docs/MANUAL.md` §7.2-7.8 ya documenta a mano, en prosa, cómo crear
cada tipo de documento (Entrada, Salida, OC, Recepción, Solicitud, Factura, Traspaso). Esta
fase convierte esa tabla en datos que el código puede leer, en vez de que cada receta
reimplemente su propio `if (moduleId == 183) { ... } else if (moduleId == 202) { ... }`.

### Archivos a crear

**`src/EstructurasDocumento.cs`** (nuevo). Sigue el estilo de `src/Recetas.cs` (mismo
encabezado de licencia GPL-3.0, mismo namespace `BrosLMV`).

```csharp
namespace BrosLMV
{
    // Metadatos de "cómo se arma" un tipo de documento destino, sacados de MANUAL.md §7.
    // Generaliza el patrón NuevoDocumento -> perfil -> AgregarArticulo x N -> RecalcCompleto
    // -> AffectStockNEW? -> Save -> post-Save fixes, para que una receta no necesite un
    // caso especial por cada ModuleID.
    public class EstructuraDocumento
    {
        public int ModuleId;
        public string Nombre;
        // Perfil de encabezado (aplicado con UPDATE docDocument justo despues de NuevoDocumento):
        public bool DepotIdFromEsDepotId;   // true: DepotIDFrom=DepotID (Entrada/Salida). false: DepotIDFrom=0 (OC/Pedido/...)
        public int PaymentTermId;           // 0 = sin plazo (Entrada/Salida). OC suele usar otro valor -- ver nota en MANUAL.md 7.5.
        public bool RequiereFechaEntrega;   // true: UPDATE DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
        // Partidas:
        public int? TaxTypeIdPartida;       // si no es null, UPDATE docDocumentItem SET TaxTypeID=<valor> despues de AgregarArticulo x N
        // Post-proceso (orden fijo, ver MANUAL.md 7.1 "patron canonico"):
        public bool AfectaInventario;       // true: llama ctx.erp.AffectStockNEW(doc) antes de Save
        public bool GeneraInfoPago;         // true: llama ctx.erp.UpdateDocumentPaidInfo(doc) despues de Save (solo OC/Factura)
    }

    public static class EstructurasRegistro
    {
        private static readonly System.Collections.Generic.Dictionary<int, EstructuraDocumento> Todas =
            new System.Collections.Generic.Dictionary<int, EstructuraDocumento>();

        static EstructurasRegistro()
        {
            // Orden de compra (ModuleID=183) -- MANUAL.md 7.5. Primera estructura real,
            // porque es la que usa la receta estrella de la fase 4.
            Registrar(new EstructuraDocumento
            {
                ModuleId = 183,
                Nombre = "Orden de compra",
                DepotIdFromEsDepotId = false,
                PaymentTermId = 0, // MANUAL.md usa 4 (50%+50% a 3 meses) como ejemplo -- 0 es
                                    // seguro en cualquier empresa (sin depender de que exista
                                    // el PaymentTermID=4 en su catalogo). Documentar en
                                    // MANUAL.md si se cambia el default.
                RequiereFechaEntrega = true,
                TaxTypeIdPartida = 5, // IVA 16% -- ver nota en MANUAL.md 7.5
                AfectaInventario = true, // deja kardex con Qty=0 (compromete sin mover), NO lo salta
                GeneraInfoPago = true,
            });

            // Entrada de almacen (ModuleID=202) -- MANUAL.md 7.3. Segunda estructura, mas
            // simple, para probar que el codigo generico NO esta hardcodeado solo para OC.
            Registrar(new EstructuraDocumento
            {
                ModuleId = 202,
                Nombre = "Entrada de almacen",
                DepotIdFromEsDepotId = true,
                PaymentTermId = 0,
                RequiereFechaEntrega = false,
                TaxTypeIdPartida = null,
                AfectaInventario = true,
                GeneraInfoPago = false,
            });

            // Agregar mas estructuras aqui conforme se necesiten (Salida=203, Recepcion=184,
            // Solicitud=1040, Factura=152, Traspaso=204 -- todas ya documentadas en
            // MANUAL.md 7.4/7.6/7.7/7.8, mismo patron).
        }

        private static void Registrar(EstructuraDocumento e) { Todas[e.ModuleId] = e; }

        public static EstructuraDocumento Buscar(int moduleId)
        { return Todas.TryGetValue(moduleId, out var e) ? e : null; }
    }
}
```

### Pasos exactos

1. Crear `src/EstructurasDocumento.cs` con el contenido de arriba (cópialo tal cual, no lo
   reinventes).
2. Agregarlo a `src/BrosLMV.csproj`: en el `<ItemGroup>` que ya tiene
   `<Compile Include="Recetas.cs" />`, agrega justo debajo:
   ```xml
   <Compile Include="EstructurasDocumento.cs" />
   ```
3. Agregarlo también a `runner/BrosLMV.Runner.csproj` (lo va a necesitar la fase 4, que sí
   corre por el Runner): en el `<ItemGroup>` que tiene
   `<Compile Include="..\src\Recetas.cs" Link="Recetas.cs" />`, agrega justo debajo:
   ```xml
   <Compile Include="..\src\EstructurasDocumento.cs" Link="EstructurasDocumento.cs" />
   ```
4. Compilar los dos proyectos (ver regla 6 de la sección 0). Esta fase **no tiene ejecución
   propia** (es solo un catálogo de datos, nadie lo llama todavía) — no hace falta un caso
   nuevo del arnés, pero sí correr `probar_humo.ps1` completo para confirmar que no rompiste
   nada existente.
5. Documentar:
   - `CHANGELOG.md`: nueva entrada `## [2.45.0] — <fecha de hoy> — T3.1 fase 3: almacén de
     estructuras de documento`. Explica qué es, por qué (generalizar MANUAL.md §7.2-7.8),
     qué archivos, y dejar explícito: **"sin ejecución propia todavía — lo usa la fase 4"**.
   - `notas_version.html`: entrada nueva (breve, 1-2 líneas, es una pieza interna que el
     usuario final no ve todavía).
   - `docs/RECETAS_NOCODE.md` §2.3: agregar un bloque `> **Implementado (fecha, vX.Y.Z,
     fase 3):** ...` igual que ya existe en §2.1 y §2.2 (cópialo del estilo de §2.2 que
     dejó la fase 2).
   - `docs/PLAN_IMPLEMENTACION.md`: actualizar el banner de T3.1 (busca
     `#### T3.1 — Motor de recetas no-code`) para decir "fases 1-3 de 6 hechas".
   - `docs/ESTADO.md`: nueva entrada "Estás aquí" arriba del todo, mismo estilo que las
     anteriores (mira las últimas 3-4 entradas para copiar el tono).

### Criterio de "terminada"

- Compila 0 errores en `BrosLMV.csproj` y `BrosLMV.Runner.csproj`.
- `probar_humo.ps1` sigue en verde (8/8, esta fase no agrega casos).
- `verificar_regla_de_oro.ps1` dice OK.
- Los 5 archivos de docs de arriba están actualizados.
- Commit + push a `main`.

---

## Fase 4 — Receta estrella: "Crear documento a partir de otro"

**Objetivo:** la receta que justifica todo el proyecto — crear un documento (p. ej. una OC)
sin escribir código, usando `ctx.erp` + la estructura de la fase 3.

**Alcance de ESTA fase:** el motor de ejecución, probado con un JSON de config armado a
mano (igual que la fase 2 probó `sql_tokens` con un JSON a mano). La ventana visual con
grid editable de partidas (lo que ve el usuario final, mostrado en el mockup que ya se le
enseñó) **es la fase 6**, no esta. Aquí solo construyes el backend.

### Diseño de la receta

Nueva clase en `src/Recetas.cs` (agrégala al final del archivo, junto a `RecetaSqlTokens`,
y regístrala en el constructor estático de `RecetasRegistro` con
`Registrar(new RecetaCrearDocumentoDesdeOtro());`).

**Config JSON esperado:**
```json
{
  "receta": "crear_documento_desde_otro",
  "config": {
    "moduloDestino": 183,
    "depotId": 1,
    "businessEntityId": 2,
    "partidas": [
      {"productId": 1, "cantidad": 5, "precio": 250, "costo": 200}
    ]
  }
}
```

**Lógica de `Ejecutar(config, ctx)`** (sigue el mismo patrón de
`build/humo/casos/03_crear_oc.codigo.cs`, que ya está probado a mano contra el sandbox —
NO reinventes la secuencia de llamadas, cópiala de ahí):

```csharp
public class RecetaCrearDocumentoDesdeOtro : IReceta
{
    public string Id => "crear_documento_desde_otro";
    public string Nombre => "Crear documento a partir de otro";

    public string Ejecutar(Dictionary<string, object> config, ScriptContext ctx)
    {
        if (config == null) return "ERROR: falta config.";
        if (!config.ContainsKey("moduloDestino"))
            return "ERROR: config.moduloDestino es requerido.";

        int moduloDestino = Convert.ToInt32(config["moduloDestino"]);
        var estructura = EstructurasRegistro.Buscar(moduloDestino);
        if (estructura == null)
            return "ERROR: no hay EstructuraDocumento registrada para ModuleID=" + moduloDestino +
                   " (agrégala en src/EstructurasDocumento.cs antes de usar este módulo).";

        int depotId = config.ContainsKey("depotId") ? Convert.ToInt32(config["depotId"]) : 1;
        int businessEntityId = config.ContainsKey("businessEntityId") ? Convert.ToInt32(config["businessEntityId"]) : 0;

        if (!config.ContainsKey("partidas") || !(config["partidas"] is System.Collections.ArrayList partidas) || partidas.Count == 0)
            return "ERROR: config.partidas debe ser una lista con al menos 1 partida.";

        try
        {
            int doc = ctx.erp.NuevoDocumento(moduloDestino, depotId, businessEntityId);

            string depotIdFromExpr = estructura.DepotIdFromEsDepotId ? "DepotID" : "0";
            string fechas = estructura.RequiereFechaEntrega
                ? ", DateDelivery=GETDATE(), DateDocDelivery=GETDATE()" : "";
            ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=" + depotIdFromExpr +
                          ", PaymentTermID=" + estructura.PaymentTermId + fechas +
                          " WHERE DocumentID=" + doc);

            foreach (System.Collections.Hashtable p in partidas)
            {
                int productId = Convert.ToInt32(p["productId"]);
                decimal cantidad = Convert.ToDecimal(p["cantidad"]);
                decimal precio = p.ContainsKey("precio") ? Convert.ToDecimal(p["precio"]) : -1;
                decimal costo = p.ContainsKey("costo") ? Convert.ToDecimal(p["costo"]) : -1;
                ctx.erp.AgregarArticulo(doc, productId, cantidad, precio, costo);
            }

            if (estructura.TaxTypeIdPartida.HasValue)
                ctx.NonQuery("UPDATE docDocumentItem SET TaxTypeID=" + estructura.TaxTypeIdPartida.Value +
                              " WHERE DocumentID=" + doc + " AND DeletedOn IS NULL");

            ctx.erp.RecalcCompleto(doc);
            if (estructura.AfectaInventario) ctx.erp.AffectStockNEW(doc);
            ctx.erp.Save(doc);
            if (estructura.GeneraInfoPago) ctx.erp.UpdateDocumentPaidInfo(doc);

            return "Documento creado: doc=" + doc + " (" + estructura.Nombre + ")";
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }
    }
}
```

> **Nota importante sobre tipos:** `JavaScriptSerializer` (el parser JSON que ya usa
> `RecetasRegistro.Ejecutar`) deserializa listas JSON como `ArrayList` y objetos JSON
> anidados como `Hashtable`, NO como `List<T>`/`Dictionary<string,object>` genéricos como
> el nivel raíz. Por eso el código de arriba usa `ArrayList`/`Hashtable` para `partidas`, a
> diferencia de `RecetaSqlTokens` que solo lee un string plano. **Pruébalo tal cual está
> antes de "mejorarlo"** — si cambias los tipos y no compila o no parsea, vuelve a esta
> versión exacta primero para confirmar que el problema es tuyo, no del código base.

### Pasos exactos

1. Verifica que la fase 3 ya esté hecha (`src/EstructurasDocumento.cs` debe existir y tener
   `EstructurasRegistro.Buscar(183)` devolviendo la estructura de OC). Si no existe, haz la
   fase 3 primero — esta fase depende de ella.
2. Agrega la clase `RecetaCrearDocumentoDesdeOtro` a `src/Recetas.cs` (al final, antes del
   cierre del `namespace BrosLMV`).
3. En `RecetasRegistro` (mismo archivo), dentro del constructor estático
   `static RecetasRegistro()`, agrega:
   ```csharp
   Registrar(new RecetaCrearDocumentoDesdeOtro());
   ```
   justo debajo de `Registrar(new RecetaSqlTokens());`.
4. Compila (`src/BrosLMV.csproj` y `runner/BrosLMV.Runner.csproj`) — 0 errores.
5. **Prueba real contra el sandbox** (copia el patrón exacto de
   `build/humo/casos/03_crear_oc.ps1`, que ya inserta un botón, corre el Runner, y verifica
   por SQL). El JSON de prueba debe usar los fixtures que ya existen en `ComercialSP`:
   - Proveedor: `SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO-PROV-001'`
   - Producto: `SELECT ProductID FROM orgProduct WHERE ProductKey='HUMO-PROD-001'`

   El AppKey de prueba: `HUMO_RECETA_CREAR_DOC`. Config JSON (sustituye los IDs reales que
   consultes arriba):
   ```json
   {"receta":"crear_documento_desde_otro","config":{"moduloDestino":183,"depotId":1,"businessEntityId":<ID_PROVEEDOR>,"partidas":[{"productId":<ID_PRODUCTO>,"cantidad":5,"precio":250,"costo":200}]}}
   ```
   Corre con `BrosLMV.Runner.exe --appkey HUMO_RECETA_CREAR_DOC --bd ComercialSP` y confirma
   por SQL (`SELECT COUNT(*) FROM docDocument WHERE DocumentTypeID=40 AND ModuleID=183`)
   que aumentó en 1, exactamente como valida `build/humo/casos/03_crear_oc.ps1`.
6. Cuando funcione a mano, conviértelo en **caso 9 permanente del arnés**:
   `build/humo/casos/09_receta_crear_documento.ps1` +
   `09_receta_crear_documento.codigo.json` — copia la estructura de
   `build/humo/casos/03_crear_oc.ps1` (conteo antes/después de `docDocument`) combinada con
   el patrón de upsert de `build/humo/casos/08_receta_sql_tokens.ps1` (marcador
   `# lang: receta`). El script `.ps1` debe resolver `businessEntityId`/`productId`
   dinámicamente por SQL (no hardcodear IDs numéricos — pueden cambiar si el sandbox se
   reprovisiona).
7. Corre `probar_humo.ps1` completo — debe quedar en 9/9 verde.
8. Documentar (mismos 5 archivos que la fase 3, mismo criterio):
   - `CHANGELOG.md`: `## [2.46.0] — <fecha> — T3.1 fase 4: receta estrella "crear documento
     a partir de otro"`. Detalla el config JSON esperado, qué reusa (fase 3), qué prueba
     real se hizo, y dejar explícito: **"la ventana visual con grid editable es la fase 6,
     no esta — aquí solo el motor de ejecución, probado con JSON armado a mano"**.
   - `notas_version.html`, `RECETAS_NOCODE.md` §2.4, `PLAN_IMPLEMENTACION.md` (T3.1: fases
     1-4 de 6), `ESTADO.md`.

### Criterio de "terminada"

- Compila 0 errores.
- Documento real creado contra `ComercialSP` (verificado por SQL, no solo "el Runner salió
  en 0") — mismo doble-check que ya usan los casos 3/4 del arnés.
- Caso 9 del arnés en verde, `probar_humo.ps1` da 9/9.
- 5 archivos de docs actualizados, regla de oro verde.
- Commit + push.

---

## Fase 5 — Pasos encadenados

**Objetivo:** un botón puede ejecutar varios pasos en orden (p. ej. validar con SQL → crear
documento → notificar), donde cada paso es una receta ya registrada.

### Diseño

El JSON de un botón "pasos encadenados" tiene una forma DISTINTA a la de una receta simple
— en vez de `{"receta": "...", "config": {...}}`, es:
```json
{"pasos": [
  {"receta": "sql_tokens", "config": {"sql": "SELECT COUNT(*) AS N FROM docDocument"}},
  {"receta": "crear_documento_desde_otro", "config": {"moduloDestino": 183, "depotId": 1, "businessEntityId": 2, "partidas": [{"productId": 1, "cantidad": 1}]}}
]}
```

`RecetasRegistro.Ejecutar` debe detectar si el JSON raíz trae `"pasos"` (lista) en vez de
`"receta"` (string), y si es así, ejecutar cada paso EN ORDEN, deteniéndose en el primer
error (no seguir con el paso 3 si el paso 2 falló — los efectos de los pasos anteriores que
ya se ejecutaron NO se revierten, no hay transacción entre pasos, esto debe quedar
documentado como limitación conocida, no resuelto en esta fase).

### Pasos exactos

1. Abre `src/Recetas.cs`. Dentro de `RecetasRegistro`, **extrae** la lógica actual de
   `Ejecutar(string codigoConMarcador, ScriptContext ctx)` que ejecuta UNA receta (parsear
   JSON, buscar por id, llamar `.Ejecutar(config, ctx)`) a un método privado nuevo:
   ```csharp
   private static string EjecutarUnaReceta(Dictionary<string, object> raiz, ScriptContext ctx)
   {
       if (raiz == null || !raiz.ContainsKey("receta"))
           return "ERROR: el JSON no trae la clave \"receta\" (id).";
       string id = Convert.ToString(raiz["receta"]);
       var receta = Buscar(id);
       if (receta == null) return "ERROR: receta desconocida \"" + id + "\" (no esta en RecetasRegistro).";
       var config = raiz.ContainsKey("config") && raiz["config"] is Dictionary<string, object> d
           ? d : new Dictionary<string, object>();
       return receta.Ejecutar(config, ctx);
   }
   ```
2. Reescribe el método público `Ejecutar(string codigoConMarcador, ScriptContext ctx)` para
   que, después de parsear el JSON raíz, revise si trae `"pasos"`:
   ```csharp
   public static string Ejecutar(string codigoConMarcador, ScriptContext ctx)
   {
       try
       {
           string json = QuitarMarcador(codigoConMarcador);
           var serializer = new JavaScriptSerializer();
           var raiz = serializer.Deserialize<Dictionary<string, object>>(json);

           if (raiz != null && raiz.ContainsKey("pasos") && raiz["pasos"] is System.Collections.ArrayList pasos)
           {
               var resultados = new System.Text.StringBuilder();
               for (int i = 0; i < pasos.Count; i++)
               {
                   // Cada paso llega como Hashtable (igual que "partidas" en la fase 4) --
                   // convertirlo a Dictionary<string,object> para reusar EjecutarUnaReceta.
                   var pasoDict = new Dictionary<string, object>();
                   foreach (System.Collections.DictionaryEntry kv in (System.Collections.Hashtable)pasos[i])
                       pasoDict[(string)kv.Key] = kv.Value;

                   string r = EjecutarUnaReceta(pasoDict, ctx);
                   if (r != null && r.StartsWith("ERROR"))
                       return "ERROR: paso " + (i + 1) + " de " + pasos.Count + " fallo -- " + r;
                   resultados.AppendLine("Paso " + (i + 1) + ": " + r);
               }
               return resultados.ToString().TrimEnd();
           }

           return EjecutarUnaReceta(raiz, ctx);
       }
       catch (Exception ex)
       {
           return "ERROR: JSON de receta invalido -- " + ex.Message;
       }
   }
   ```
3. Compila. Los casos 8 y 9 del arnés (recetas simples, sin `"pasos"`) deben seguir
   pasando exactamente igual que antes — si algo se rompió, revisa que
   `EjecutarUnaReceta` haga EXACTAMENTE lo mismo que hacía el código viejo.
4. **Prueba real contra el sandbox**, dos casos:
   - Cadena de 2 pasos exitosos (dos `sql_tokens` inofensivos, p. ej. dos `SELECT COUNT`
     distintos) → debe correr ambos y el resultado debe mencionar "Paso 1" y "Paso 2".
   - Cadena donde el paso 1 pasa y el paso 2 falla a propósito (receta desconocida) → debe
     regresar `ERROR: paso 2 de 2 fallo -- ...`, y el resultado del paso 1 (que sí corrió)
     debe quedar mencionado en el mensaje o al menos no debe haber tronado antes de tiempo.
5. Agregar como **caso 10 permanente del arnés**:
   `build/humo/casos/10_receta_pasos_encadenados.ps1` — mismo patrón que el caso 8, con dos
   botones de prueba (cadena exitosa, cadena con falla a propósito).
6. `probar_humo.ps1` → 10/10 verde.
7. Documentar (mismos 5 archivos):
   - `CHANGELOG.md`: `## [2.47.0] — <fecha> — T3.1 fase 5: pasos encadenados`. Explica el
     formato `{"pasos": [...]}`, que se detiene en el primer error, y **la limitación
     conocida**: no hay transacción entre pasos (si el paso 2 de 3 falla, los efectos del
     paso 1 ya se aplicaron y NO se revierten) — anótalo también en `MANUAL.md` como
     advertencia real, no lo escondas.
   - `notas_version.html`, `RECETAS_NOCODE.md` §2.5, `PLAN_IMPLEMENTACION.md` (fases 1-5 de
     6), `ESTADO.md`.

### Criterio de "terminada"

- Compila 0 errores.
- Los casos 8-9 (recetas simples, sin pasos) siguen pasando sin cambios de comportamiento.
- Caso 10 (pasos encadenados, éxito + falla a propósito) en verde.
- `probar_humo.ps1` → 10/10.
- Docs + regla de oro + commit + push.

---

## Fase 6 — Modo asistente en la Consola

**Objetivo:** lo que el usuario ve — un wizard "Nueva acción" donde elige una receta de una
lista, llena un formulario (con botón de insertar token, reusando el panel de la fase 1), y
guarda el botón sin escribir código. Es el mockup que ya se le mostró y aprobó al usuario
antes de empezar T3.1.

> ⚠️ **Esta es la fase más grande y la única que es 100% interfaz visual de Windows Forms
> (no hay forma de verificarla con capturas de pantalla ni pruebas automatizadas de UI en
> este entorno).** Antes de empezar a programar, si tienes cualquier duda de diseño
> (colores, texto exacto de botones, dónde vive el botón "Nueva acción" en la Consola),
> **pregúntale al usuario o revisa el mockup que ya se generó en la conversación anterior**
> — no inventes decisiones visuales nuevas sin confirmar.

### Diseño

**1. Extender `IReceta` con un esquema de configuración** (`src/Recetas.cs`):

```csharp
public class CampoReceta
{
    public string Nombre;      // clave en el JSON de config, p. ej. "sql"
    public string Etiqueta;    // texto visible, p. ej. "SQL (con tokens opcionales)"
    public string Tipo;        // "texto" | "numero" | "texto_multilinea"
    public bool PermiteTokens; // si true, el campo muestra el boton "Insertar token" (fase 1)
    public bool Requerido;
}
```

Agrega `List<CampoReceta> EsquemaConfig { get; }` a la interfaz `IReceta`, y en cada receta
existente:

- `RecetaSqlTokens.EsquemaConfig`:
  ```csharp
  new List<CampoReceta> {
      new CampoReceta { Nombre="sql", Etiqueta="SQL (con tokens opcionales)", Tipo="texto_multilinea", PermiteTokens=true, Requerido=true }
  }
  ```
- `RecetaCrearDocumentoDesdeOtro.EsquemaConfig`: campos `moduloDestino` (numero),
  `depotId` (numero), `businessEntityId` (texto, PermiteTokens=true) — **las partidas NO
  entran en el formulario simple de esta fase** (un grid editable de partidas es su propio
  problema de UI; para la primera versión del asistente, deja `partidas` como un campo de
  texto multilínea donde el usuario pega el JSON del arreglo a mano, con una nota clara en
  la etiqueta: "Partidas (JSON, ej: [{\"productId\":1,\"cantidad\":5}])". Mejorarlo a un
  grid visual real es trabajo futuro, no de esta fase — no te compliques intentando
  construir un `DataGridView` dinámico si no te lo pidieron explícitamente).

**2. Nuevo diálogo `NuevaAccionForm`** (nuevo archivo `src/NuevaAccionForm.cs`, o una clase
anidada dentro de `Consola.cs` si prefieres seguir la convención de que la UI vive ahí —
revisa cómo está organizado `Consola.cs` primero y sigue el mismo criterio):

- **Paso 1:** `ComboBox` con `RecetasRegistro` (necesitas exponer una forma de listar TODAS
  las recetas registradas, no solo buscar por id — agrega
  `public static IEnumerable<IReceta> Listar() => Todas.Values;` a `RecetasRegistro`).
- **Paso 2:** formulario generado dinámicamente a partir de `receta.EsquemaConfig` — un
  `Label` + `TextBox`/`NumericUpDown` por campo, según `Tipo`. Si `PermiteTokens`, agrega un
  botón pequeño junto al campo que abra el mismo panel de chips de la fase 1 (revisa
  `TOKENS_FIJOS` en `Consola.cs`) e inserte el token elegido en el `TextBox` en vez de en el
  editor de código.
- **Paso 3 (Guardar):** pide un `AppKey` (texto, validar que no esté vacío ni ya exista en
  `zzBrosScript` salvo que el usuario confirme sobreescribir) y un `Nombre` visible.
  Construye el JSON: `{"receta": "<id de la receta elegida>", "config": {<cada campo del
  formulario>}}`, antepone el marcador `# lang: receta`, y guarda con el mismo mecanismo que
  ya usa la Consola para guardar cualquier botón (busca cómo `BrosGuardar` se invoca desde
  `Consola.cs` hoy — reusa esa función, no reescribas el INSERT/UPDATE de `zzBrosScript` a
  mano).

**3. Punto de entrada:** un botón "Nueva acción" en la barra de herramientas de la Consola
(al lado de "Nuevo"/"Abrir"/"Guardar" — revisa cómo están armados esos botones en
`Consola.cs` y sigue el mismo patrón de `IconButton`/`AppTheme`).

### Pasos exactos

1. Lee `src/Consola.cs` completo antes de tocarlo — es un archivo grande (2600+ líneas),
   necesitas entender los patrones existentes (`IconButton`, `AppTheme`, cómo se guarda un
   script, cómo se construyen los paneles) antes de agregar UI nueva. **No copies patrones
   de otros proyectos de WinForms — usa exactamente los que ya existen aquí.**
2. Agrega `EsquemaConfig` a `IReceta` y a las 2 recetas existentes (`RecetaSqlTokens`,
   `RecetaCrearDocumentoDesdeOtro`) y a `RecetasRegistro.Listar()`.
3. Construye `NuevaAccionForm` (o la clase equivalente) siguiendo el diseño de arriba.
4. Conecta el botón "Nueva acción" en la barra de herramientas de la Consola.
5. Compila `src/BrosLMV.csproj` — 0 errores. (Este archivo NO se enlaza en el Runner, es
   pura UI de la Consola — no hace falta tocar `runner/BrosLMV.Runner.csproj`.)
6. **Prueba indirecta** (no puedes verificar visualmente, pero SÍ puedes verificar que lo
   que el wizard PRODUCIRÍA es ejecutable): simula a mano el JSON que el formulario armaría
   para la receta `sql_tokens` con un campo `sql` de prueba, insértalo en `zzBrosScript`
   exactamente como si el wizard lo hubiera guardado, y confirma que
   `BrosLMV.Runner.exe --appkey <ese AppKey> --bd ComercialSP` lo ejecuta bien. Esto prueba
   que el FORMATO que genera el wizard es compatible con el motor — no prueba que los
   botones de Windows Forms se vean bien.
7. Documentar (mismos 5 archivos):
   - `CHANGELOG.md`: `## [2.48.0] — <fecha> — T3.1 fase 6: modo asistente "Nueva acción"
     en la Consola`. Deja **muy explícito** en la sección "Pendiente": *"Confirmar
     visualmente dentro de CONTPAQi real — es UI de Windows Forms, compila pero no hay
     forma de verificar el render sin abrir la Consola de verdad. Con esto, T3.1 (el MVP de
     3 recetas planeado originalmente) queda... "* (completa según cuántas recetas reales
     terminaron existiendo — al final de esta fase probablemente solo hay 2 recetas reales,
     `sql_tokens` y `crear_documento_desde_otro`; si el plan original pedía 3, anota
     explícitamente cuál falta y por qué, no lo escondas).
   - `notas_version.html`, `RECETAS_NOCODE.md` §2.5 (modo asistente), `PLAN_IMPLEMENTACION.md`
     (T3.1: 6 de 6 fases hechas — pero con la salvedad de "pendiente confirmación visual" y
     cualquier alcance recortado, como el grid de partidas simplificado a JSON a mano),
     `ESTADO.md`.

### Criterio de "terminada"

- Compila 0 errores.
- El JSON que el wizard generaría (probado a mano, insertado directo en `zzBrosScript`) se
  ejecuta correctamente vía el Runner.
- `probar_humo.ps1` sigue en verde (esta fase no agrega casos nuevos del arnés en sí misma
  — es UI, no ejecución headless — pero no debe romper ninguno de los 10 existentes).
- Docs actualizados, con el pendiente de verificación visual dejado explícito.
- Commit + push.
- **Avisar al usuario que esta fase, a diferencia de las anteriores, necesita que él la
  abra en Comercial real para confirmar que se ve y funciona como el mockup aprobado.**

---

## Resumen de versiones (guía, no rígida — usa la siguiente disponible si el orden cambió)

| Fase | Versión sugerida | Qué agrega |
|---|---|---|
| 3 | 2.45.0 | Almacén de estructuras de documento (sin ejecución propia) |
| 4 | 2.46.0 | Receta estrella "crear documento a partir de otro" (motor, sin UI) |
| 5 | 2.47.0 | Pasos encadenados (`{"pasos": [...]}`) |
| 6 | 2.48.0 | Modo asistente visual en la Consola |

Antes de empezar cualquier fase, revisa `src\ClsMain.cs` línea ~31 para confirmar cuál es
la versión actual real — si alguien ya adelantó trabajo, la tabla de arriba puede haber
quedado desactualizada.
