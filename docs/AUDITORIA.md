# Guía para auditar BrosLMV

Para quien haga una revisión externa del proyecto (otra IA o una persona). Léela completa
antes de empezar. Complementa a [`../AGENTS.md`](../AGENTS.md), que sigue aplicando en todo.

## 1. Qué es BrosLMV (en un párrafo)

Una familia de herramientas, software libre GPL-3.0 de un solo autor, que extiende **CONTPAQi
Comercial PRO** (ERP mexicano) sin tocar su código: un **addon COM** que vive dentro de
`ComercialSP.exe` y agrega botones programables (C#, Python o SQL guardados en la tabla
`zzBrosScript` de cada empresa) más una Consola para escribirlos; `ctx`/`ctx.erp`, la API que
esos scripts usan para leer la base y para crear documentos **con el propio motor de Comercial
(XEngine)** en lugar de INSERTs a mano; `BrosLMV.Runner`, que corre esos mismos scripts sin
Comercial abierto; `htmlpdf/` para PDF de documentos y correo; y `BrosLMV.Descargas`, un
subproducto independiente para la descarga masiva de CFDI del SAT. Diagrama general en
[`../README.md`](../README.md); estado de cada pieza en [`ESTADO.md`](ESTADO.md).

La meta del repo es ser **la referencia verificada de cómo hablar con Comercial PRO** — por
eso casi todo lo documentado viene de pruebas reales (sandbox, capturas nativas o datos de
producción de clientes). Una afirmación sin evidencia es un defecto.

## 2. Qué leer primero (y qué no)

1. [`../AGENTS.md`](../AGENTS.md) — reglas no negociables.
2. [`ESTADO.md`](ESTADO.md) → [`ROADMAP.md`](ROADMAP.md) → [`INDICE.md`](INDICE.md).
3. Según el área que audites, el documento que `INDICE.md` indique.

**No leas completos:** `CHANGELOG.md` (consúltalo por versión o tema) ni nada en
`docs/archivo/` (historia, con contradicciones conocidas — no es fuente de verdad).

## 3. Reglas del auditor

1. **No inventar.** Cada hallazgo lleva evidencia verificable: archivo y línea, salida de un
   comando, o consulta SQL y su resultado. Si no pudiste verificarlo, repórtalo como
   **SOSPECHA**, nunca como hecho.
2. **La fuente de verdad es el código**, luego la documentación vigente. Si un documento
   contradice al código, eso es un hallazgo (y el documento es el que probablemente está mal
   — pero verifica).
3. **Cómo funciona Comercial por dentro:** antes de afirmar algo, búscalo en `docs/`
   (sobre todo `MANUAL.md` §10–§12) y, **si tienes acceso local al repo**, en la carpeta
   privada de ingeniería inversa que describe `AGENTS.md` §1 (empieza por su `README.md`;
   tiene capturas nativas BEFORE/AFTER de cada documento). Si solo tienes GitHub, esa carpeta
   no está publicada: marca el hallazgo "requiere verificar contra la carpeta privada de
   ingeniería inversa".
4. **No ejecutes nada contra bases de clientes.** Pruebas solo contra el sandbox (ver §5).
   Nada destructivo sin `BACKUP DATABASE` antes.
5. **No cambies código productivo como parte de la auditoría.** Propón el cambio en el
   reporte (o en un PR aparte, claramente separado, si el autor lo pide).
6. **No nombres terceros** (herramientas de otros desarrolladores, clientes, IPs, rutas de
   red) en nada que vaya a quedar en el repo — ni en el reporte. Usa "un cliente", "otra
   herramienta de scripting (tablas `rt*`)".
7. **Propón, no decidas.** Las propuestas de roadmap van al reporte; el autor decide qué pasa
   a `ROADMAP.md`.

## 4. Qué revisar, en orden de prioridad

| # | Área | Qué buscar | Dónde |
|---|---|---|---|
| A | **Documentación vs. código de la API** | Cada método, parámetro y default de `ctx`/`ctx.erp` descrito en `MANUAL.md` §5–§7 y `SCRIPTING_CONTRATOS.md` existe con esa firma y ese comportamiento en `src/Scripting.cs` (y en Python, `workers/`). Métodos que existen en código y no están documentados | `src/Scripting.cs`, `src/HostClient.cs`, `workers/` |
| B | **Seguridad** | SQL armado por concatenación con datos que puede escribir el usuario; manejo de contraseñas y credenciales (`AGENTS.md` §3.3); ACL del Named Pipe del host; que el modo solo-lectura (`ctx.SoloLectura`) bloquee de verdad toda escritura; que nada de `.gitignore`/carpetas privadas se haya colado al repo o a los instaladores | `src/`, `host/`, `instalador/`, `build/publicar_release.ps1` |
| C | **Plantillas de fábrica** (`instalador/scripts/`) | Que compilen y que respeten las reglas de `MANUAL.md` §12 (empresa propia nunca fija, `MustBeDelivered`, `CostPrice`, folio, `DeliverDocumentItemID` vs `SourceDocumentItemID`, agenda de pago). Ya se encontró un bug real así en v2.93.0 — puede haber más | `instalador/scripts/`, `MANUAL.md` §10–§12 |
| D | **Coherencia de la documentación** | Contradicciones entre documentos, afirmaciones vencidas (versión, "pendiente" que ya está hecho), enlaces rotos, documentos huérfanos, duplicados | `docs/`, `README.md`, `AGENTS.md` |
| E | **Build, pruebas y CI** | Que `build/verificar_regla_de_oro.ps1`, `build/probar_humo.ps1` y `.github/workflows/ci.yml` cubran lo que dicen cubrir; qué partes del código no tienen ninguna prueba | `build/`, `.github/` |
| F | **Roadmap** | Si las prioridades de `ROADMAP.md` tienen sentido con la evidencia; qué falta; qué sobra | `ROADMAP.md` |

Fuera de alcance: `docs/archivo/`, `BrosLMV.Descargas` (salvo que se pida — tiene su propia
documentación en `descargas/DOCUMENTACION.md`), y el Punto de Venta (privado, no está en el
repo).

## 5. Cómo compilar y probar

```powershell
.\build\compilar.ps1                  # addon -> build\out\BrosLMVClsMain.dll
.\build\verificar_regla_de_oro.ps1    # versión vs CHANGELOG/notas + términos prohibidos
.\build\probar_humo.ps1               # casos de build\humo\casos contra el sandbox
```

- `probar_humo.ps1` necesita una instancia SQL con una empresa de Comercial (por defecto
  `localhost\compac`, parámetros `-Server`/`-Database`); sin ella no corre.
- El escaneo de términos prohibidos necesita `.terminos_prohibidos.local` (no versionado); si
  no existe, se salta con aviso — eso no es un error.
- CI (`.github/workflows/ci.yml`) solo verifica la regla de oro y compila addon, Runner y
  host; no corre las pruebas de humo.

## 6. Formato del reporte

Un archivo nuevo `docs/auditorias/AAAA-MM-DD_<auditor>.md`, con un resumen de 5-10 líneas
arriba y después un hallazgo por bloque:

```markdown
### A-01 — <título corto>
- **Estado:** CONFIRMADO | SOSPECHA
- **Severidad:** Alta (rompe algo o riesgo de seguridad/datos) | Media (comportamiento
  incorrecto o documentación que induce a error) | Baja (cosmético, orden, claridad)
- **Área:** A–F (tabla de §4)
- **Evidencia:** `ruta/archivo:línea`, comando o consulta + resultado
- **Qué pasa:** descripción concreta
- **Propuesta:** qué cambiar (y dónde documentarlo)
- **Cómo verificar el arreglo:** prueba concreta
```

Al final: **propuestas de roadmap** (con evidencia, no deseos) y **preguntas abiertas** para el
autor — lo que no se pudo resolver sin él.

Después de la auditoría, cada hallazgo CONFIRMADO que se corrija sigue la regla de oro
normal (código + `CHANGELOG.md` + documentos afectados, en el mismo commit), y el reporte se
actualiza marcando qué se corrigió y en qué versión.
