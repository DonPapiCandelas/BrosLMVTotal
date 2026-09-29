# Plan de implementación — Consola BrosLMV v3

> Objetivo: que un usuario nuevo pueda escribir, guardar, documentar y **publicar como botón** un script sin que nadie le enseñe.
> La Consola es el corazón del sistema: se rediseña como producto, con biblioteca, documentación y un SDK de verdad.
> Estado: **propuesta para aprobar** (2026-09-29). Nada de esto está implementado todavía salvo lo marcado ✅.

## 1. Lo que hoy estorba (visto en pruebas con la 2.94.0)

| # | Problema | Causa de fondo |
|---|----------|----------------|
| 1 | «Ver documentación» dice que no encuentra el HTML | La documentación vive como archivo suelto en `C:\BrosLMV\docs\plantillas`; si el instalador no lo copia (o alguien lo borra), se pierde. |
| 2 | La plantilla se ve como `CREAR_DOC_DESDE_XML` en vez de un nombre de botón; el AppKey no tiene guiones bajos legibles | Se mezclan tres cosas en un solo texto: **nombre visible**, **AppKey técnico** y **archivo**. |
| 3 | Las categorías (ej. CFDI) hay que reescribirlas en cada script | El campo es texto libre; no hay lista de categorías existentes. |
| 4 | Confusión archivos vs. SQL | Hoy hay dos «fuentes»: `zzBrosScript` (SQL, por empresa) y `C:\BrosLMV\scripts` (archivos). No está claro cuál manda. |
| 5 | Se olvida `lang: csharp / python / sql` | El lenguaje se deduce de un comentario en la primera línea. |
| 6 | Las referencias (panel derecho) no explican **cómo** usar cada función | Son una lista de nombre + descripción de una línea, sin ejemplo ni versión. |
| 7 | Crear un botón exige recordar el AppKey, escribir `BrosLMV.xxxx`, el icono y la sección | El gestor de ribbon es una plantilla Python, no una pantalla. |
| 8 | No existe un catálogo real de funciones | La verdad está repartida en `MANUAL.md`, `PYTHON.md`, `XENGINE_FUNCIONES.md` y el código. |

## 2. Decisiones de fondo (propuestas)

1. **SQL es la única fuente de verdad del script del usuario** (`zzBrosScript` + `zzBrosScriptHist`). Los archivos de `C:\BrosLMV\scripts` dejan de ser «la biblioteca»:
   - Son **semillas**: el instalador las importa a SQL si el AppKey no existe y ya no se consultan.
   - Si alguien las borra **no pasa nada** (el script sigue en SQL). Si borra el script en SQL, se recupera del historial o con «Exportar paquete (.bros)».
   - Las plantillas oficiales viajan **dentro de la DLL como recursos** (no como archivos sueltos) y la Consola las ofrece siempre. Eso resuelve el problema #1 de raíz.
2. **Tres campos distintos por script**: *Nombre visible* (lo que ve el usuario, con espacios y acentos), *AppKey* (técnico, único, se genera solo con guiones bajos a partir del nombre y no se pide) y *Lenguaje* (selector, no comentario).
3. **Lenguaje = selector en la barra del editor** (C# · Python · SQL). El comentario `lang:` se sigue aceptando al importar scripts viejos, pero la Consola lo escribe sola. Quien no elige, ve el lenguaje detectado y puede cambiarlo con un clic.
4. **Categorías = lista editable con combo**: al guardar/categorizar se muestran las existentes (de `zzBrosScript`) y se puede escribir una nueva.
5. **Documentación como producto**: un solo visor HTML formal (mismo estilo claro/oscuro) accesible desde *Ayuda* y desde el clic secundario de cada plantilla/referencia. Los scripts llevan cabecera corta; el detalle vive en el visor.
6. **SDK con nombre propio y estable** (`ctx.erp.*`, `ctx.db.*`, `ctx.doc.*`, `ctx.cat.*`): las operaciones frecuentes se envuelven en funciones con firma clara (`ctx.cat.InsertarProyecto(nombre)`), respetando siempre empresa activa (Owned), afectaciones por módulo y refresco del grid. SQL puro queda como salida de emergencia, no como camino normal.

## 3. Fases

### Fase A — Arreglos inmediatos (2.94.1) — corta
- ✅ Copiar la documentación instalada en el equipo de pruebas.
- Incrustar los HTML de documentación como **recurso de la DLL** (fallback a archivo si existe uno más nuevo). Adiós al error «no se encontró».
- Mostrar el **nombre del botón** con espacios (`Crear documentos desde XML`) en la biblioteca y en el ribbon; el AppKey se deriva y no se escribe a mano.
- Combo de categorías (existentes + nueva) en «Categorizar…» y en «Guardar como».
- Auditar que `ExtraerLenguaje` no falle si falta `lang:` (usa el selector).

### Fase B — Modelo de scripts (2.95)
- Selector de lenguaje en la barra; el archivo/SQL guarda el lenguaje en columna propia (`Lenguaje`) y deja de depender del comentario.
- Semillas: el instalador importa a SQL lo que falte; se documenta «qué pasa si borro archivos» (nada).
- Exportar/Importar paquete `.bros` como respaldo oficial (ya existe: se promociona en la UI).

### Fase C — Publicar como botón (2.96)
Clic secundario en un script → **«Crear botón…»** (asistente de una pantalla):
1. Nombre del botón (por defecto el nombre visible del script).
2. Icono: galería con vista previa (los mismos iconos del ribbon actual) + buscador.
3. Dónde aparece: **Global** o **módulos específicos** (lista con casillas y búsqueda, agrupada por naturaleza: ventas, compras, inventarios…).
4. Sección/menú del ribbon (lista de las existentes o nueva).
5. Vista previa del botón + «Crear», «Quitar de…».
Debajo usa el mismo motor que `GESTOR_RIBBON.py` (que pasa a ser solo lectura/avanzado); no se pide `BrosLMV.xxxx` a mano.

### Fase D — Centro de documentación y catálogo del SDK (2.97)
- Catálogo único generado desde el código: cada función con **firma, parámetros, retorno, ejemplo C#, ejemplo Python, notas** («esto refresca el grid», «respeta Owned») y versión en que apareció. Fuente: atributos/comentarios XML → HTML, para que no se desactualice (falla el compilado si una función pública no tiene doc).
- Panel de referencias: clic en una función abre su ficha (Scalar, Query, NonQuery, `RefreshGrid()`, `Save(doc_id)`…). Doble clic sigue insertando.
- Guías con el estilo del ejemplo que funcionó (código comentado línea por línea): «Crear un documento», «Refrescar el grid», «Póliza», «Tokens» (`{pID}`, `{pIDs}`, `{pUsuario}`…: hoy casi no se usan y no están explicados).
- Manual en HTML + PDF, versionado con cada release.

### Fase E — SDK de alto nivel (continuo, empieza en 2.97)
Funciones «una línea» por catálogo y documento, ordenadas por lo que más piden:
`ctx.cat.InsertarProyecto`, `InsertarProveedor`, `InsertarProducto`, `ctx.doc.Crear(modulo, partidas…)` (con afectaciones, póliza y refresco integrados), `ctx.doc.Cancelar`, etc. Cada una nace con su ficha de documentación y prueba de humo.

### Fase F — Rediseño visual de la Consola (v3.0)
- Nuevo layout: **Biblioteca** (categorías + favoritos + búsqueda), **Editor** con pestañas, **Panel de ayuda** contextual (ficha de la función bajo el cursor) y barra de estado con lenguaje / empresa / módulo.
- Pantalla de **inicio**: plantillas oficiales, «Crear mi primer botón», enlaces a guías.
- Tema claro/oscuro, iconografía coherente, atajos visibles.
- Asistente «Nuevo script»: elegir lenguaje y plantilla en dos clics.

### Fase G — Plantillas descargables (después de v3.0)
- Catálogo en GitHub (`plantillas/index.json` + archivos + HTML de documentación); la Consola lista, descarga e instala con firma/hash.
- Rehacer las plantillas archivadas (`docs/archivo/plantillas_2.93.0`) una por una, ya con los estándares (refresco de grid, póliza, Owned).

## 4. Orden sugerido y por qué
A → C → B → D → E → F. Lo que más duele hoy es **crear botones** y **entender las referencias**; la Fase F (rediseño) se hace al final para no rehacerla dos veces, aunque se puede prototipar en paralelo.

## 5. Reglas que se mantienen
Pruebas en laboratorio; filtrar por empresa activa; nada escrito a mano (módulos por naturaleza); refrescar el grid una vez al final; nuevas funciones públicas nacen con documentación; regla de oro del repo (versión + CHANGELOG + notas).

## 6. Decisiones tomadas (2026-09-29)
1. **Fase A = versión 2.94.1**, se hace de inmediato (ver CHANGELOG).
2. **Los botones se pueden compartir entre empresas**, y se conserva la opción de pasar **solo el script** (paquete `.bros`).
3. **Ubicación flexible del botón en el ribbon (Fase C):** en la pestaña BrosLMV, en «General» o en una lista, o en una **pestaña nueva** con el nombre que elija el usuario (ej. «Reportes»); y dentro de cada pestaña, **secciones nuevas o existentes** (Herramientas, Contabilidad, Impresión, Filtro…).
4. Los nombres del SDK los decido yo según el flujo: `ctx.erp` (documento y ventana actual), `ctx.db` (datos), `ctx.cat` (catálogos: proveedores, productos, proyectos…) y `ctx.doc` (crear/cancelar documentos con afectaciones, póliza y refresco integrados).
