# BrosLMV — Motor de recetas no-code (diseño)

> La meta más ambiciosa del proyecto: que **quien no programa pueda crear botones**
> eligiendo una **acción preestablecida** y llenando un formulario, donde los campos se
> enlazan con **tokens** (`{pID}`, `{DATOS:campo}`). Se construye en **C#, en proceso**
> (no necesita el host de Python), y por eso **puede llegar antes** que el multilenguaje
> v3.0 y **sin** depender de cerrar el dilema de UI.
>
> Estado: **diseño / planeado**. Se construirá por sesiones, alimentado por la ingeniería
> inversa de lo que Comercial PRO hace al crear/cancelar cada documento (snapshot-diff +
> captura en vivo).

---

## 1. Por qué va en C# en proceso (insight estratégico)

La ventana de "crear documento" necesita cosas que **solo existen dentro de ComercialSP**:
WinForms nativo (grid editable de partidas), la **conexión viva + transacciones**, y
**XEngine** (`AffectStockNEW`, recalcular, refrescar grid). Python fuera de proceso no
tiene nada de eso. Conclusión: **el no-code para no-programadores va primero, en C#, sobre
lo que ya funciona.** El multilenguaje (v3.0) sigue siendo la meta, pero como motor para
*programadores*. Esto **reordena el roadmap**: no-code antes que el host de Python.

---

## 2. Las 5 piezas

### 2.1 Motor de tokens
Resuelve, antes de ejecutar, las variables tomadas del contexto vivo:
- `{pID}` → primer ID seleccionado en el grid.
- `{pIDs}` → todos los IDs seleccionados, separados por coma.
- `{pUserID}` → usuario actual.
- `{pModulo}` → módulo activo.
- `{pEmpresa}` → nombre de la BD activa.
- `{DATOS:Campo}` → cualquier campo de la primera fila seleccionada (p. ej.
  `{DATOS:BusinessEntityName}`, `{DATOS:Total}`).

> **Implementado (2026-06-26):** `ctx.ResolverTokens(template)` en `Scripting.cs`.
> Disponible ya en scripts C#. Python (v3.0) lo recibirá resuelto desde el host.
> Ver API completa en [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) §4.

Es la base de **todo** (recetas y scripts de cualquier lenguaje). El addon ya resuelve la
selección y puede leer la fila activa del grid, así que genera el panel de tokens
(estilo "Referencias" de Acceso Fácil) y hace la sustitución.

### 2.2 Registro de acciones (recetas)
Catálogo de capacidades **parametrizadas**. Cada receta declara dos cosas:
1. **Esquema de configuración** — qué le pregunta al usuario al *crear el botón* (sus
   campos, cuáles aceptan tokens, listas de selección, valores por defecto).
2. **Implementación** (C#) — qué hace al *ejecutarse*.

Recetas previstas: *Crear documento a partir de otro* (estrella), *Cambiar estatus*,
*Exportar selección a Excel*, *Enviar correo con PDF*, *Ejecutar SQL con tokens*.

> **Este registro es el contrato extensible = lo que la comunidad aportará** cuando el
> proyecto sea open-source. Una receta nueva = un plugin que implementa el contrato.

### 2.3 Almacén de "estructuras de documento"
Metadatos **por tipo de documento destino**: qué campos de encabezado son obligatorios,
cómo mapear las partidas del origen, si afecta inventario, de dónde sale folio/serie, qué
post-proceso de XEngine aplica. **Esto hace genérica** la receta de "crear documento"
(sirve para Req→OC, Cotización→Pedido, etc.). Se llena con la ingeniería inversa
(metodología) y se guarda como **JSON** (ver formato en esa doc).

### 2.4 Receta estrella: "Crear documento a partir de otro"
Generalización del `Boton ejemplo 1.py` (Req→OC) del cliente:
1. Lee la **estructura** del documento destino.
2. Muestra la ventana: encabezado editable (proveedor, módulo, fecha, moneda...) + grid de
   **partidas editables**.
3. Al guardar, crea el documento vía `ctx.erp.NuevoDocumento` + `AgregarArticulo` (que internamente
   usan SQL directo + XEngine para folios/parámetros) + `RecalcCompleto`/`AffectStockNEW`/
   `Save`, para preservar inventario/contabilidad/folios. El builder ya crea las 4 anclas y los
   campos universales como el nativo (v2.18.0).

### 2.5 Botón no-code + pasos encadenados
Un botón puede guardar una **config de receta** (no código). Y puede enganchar **varios
pasos en orden** (estilo eventos de AF): p. ej. *validar con SQL* → *crear documento* →
*notificar*. Cada paso puede ser una receta, un SQL con tokens, o un script (C#/Python).

> **Objetivo del motor de recetas:** un grid de captura definido solo con datos
> (columnas + una vista SQL de origen) y procesos por pasos secuenciales (p. ej.
> preparar tabla → insertar N filas → lanzar una acción), todo **sin escribir código**.
> Se reimplementa de forma independiente, sin reutilizar binarios ni código de terceros.

---

## 3. Cómo lo vive un no-programador (escena objetivo)

1. En la consola: **Nueva acción → "Crear documento a partir de otro"**.
2. Le pide *Documento origen*: arrastra el token `{pID}`.
3. Le pide *Módulo destino*: lo selecciona de una lista (o escribe el ID).
4. Guarda el botón. **Sin escribir código.**
5. En Comercial: selecciona una requisición → pulsa el botón → sale la ventana con
   proveedor + partidas editables → guarda → **documento creado**.

---

## 4. Almacenamiento

- Un botón no-code = una fila en `zzBrosScript` con `Tipo = 'receta'` y un **JSON de
  config** (qué receta + sus parámetros), en vez de código fuente.
- Las **estructuras de documento** = JSON versionado (en repo y/o tabla `zzBros*`),
  generado por la metodología de ingeniería inversa.
- Convive con los tipos existentes (`csharp`) y futuros (`python`, `sql`).

---

## 5. Relación con el resto

- **Tokens** y **pasos encadenados** sirven también a los scripts de código (todos los
  lenguajes), no solo a las recetas.
- La capa **`ctx.erp.*`** (ver [`XENGINE_FUNCIONES.md`](XENGINE_FUNCIONES.md)) es la que
  las recetas usan para escribir vía motor.
- Las **estructuras de documento** salen de la ingeniería inversa de lo que Comercial PRO
  hace al crear/cancelar cada documento (snapshot-diff + captura en vivo).

---

## 5b. Convención de vistas SQL reutilizables (`BRO_`)

Convención propia para nombrar vistas SQL que crea/usa BrosLMV, de modo que sean
identificables y reutilizables sin chocar con objetos del ERP.

Cuando un script BrosLMV necesite SQL que pueda ser reutilizado por otros scripts o
por el motor de recetas, se crea una **vista** en la base de datos con prefijo `BRO_`:

```sql
-- Ejemplo: vista reutilizable para el motor de recetas
CREATE VIEW BRO_RemisionesPendientesFacturar_VW AS
SELECT r.DocumentID, r.FolioPrefix + r.Folio AS Folio, ...
FROM docDocument r WHERE r.ModuleID = 157 AND ...
```

**Reglas:**
- Prefijo `BRO_` obligatorio para distinguirlas de vistas del sistema y de otros consultores.
- Comentario de versión en el `CREATE VIEW`: autor, fecha, qué resuelve.
- Jerarquía explícita: una vista puede apoyarse en otra `BRO_*` pero no en vistas `ARC_*`
  de terceros (frágiles si el otro consultor las modifica).
- Las vistas que sirven de fuente a un grid declarativo llevan sufijo `_VW`.

## 6. Orden de construcción sugerido

1. **Motor de tokens** (base reutilizable, pieza chica).
2. **Registro de acciones** + tipo de script `receta` + **una receta simple** (p. ej.
   "Ejecutar SQL con tokens") para probar el flujo de punta a punta.
3. **Almacén de estructuras** (formato JSON) — alimentado por la metodología.
4. **Receta estrella** "Crear documento a partir de otro" + su ventana genérica.
5. **Pasos encadenados** / hooks.
6. **Modo asistente** en la consola (configurar botón sin código).
