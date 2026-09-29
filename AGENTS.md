# AGENTS.md — contexto para agentes IA (Claude, ChatGPT, Gemini, o quien sea)

Archivo de guardarraíl. Léelo completo antes de tocar cualquier cosa. Contiene solo lo que
no se infiere del código; para detalle, los documentos de `docs/` (índice en `docs/INDICE.md`).

## 1. Qué es este repo

BrosLMV: familia de herramientas para **CONTPAQi Comercial PRO** — addon con Consola de
scripts (C#/Python/SQL), `BrosLMV.Runner` (headless), generación de PDF/correo (`htmlpdf/`),
y `BrosLMV.Descargas` (subproducto independiente, CFDI del SAT). Software libre GPL-3.0,
autor único (Cristofer Candelas).

**Carpetas privadas — NUNCA se suben ni se mencionan por nombre en nada que se publique**
(código, docs, commits, issues, releases): `Entrenamiento/` (ingeniería inversa de
herramientas de terceros y bases de clientes reales, material de aprendizaje), `pruebas/`
(experimentos de sesión), `puntodeventa/` y `puntodeventa-app/` (prototipo en construcción,
deliberadamente fuera del repo público todavía). Todas están en `.gitignore` o
`.git/info/exclude`. Si vas a crear una carpeta nueva de contexto/memoria para IA, **no la
llames `entrenamiento`** — ese nombre ya está tomado por la carpeta privada de arriba y
mezclarlas es como se filtran nombres de clientes al repo público (ya pasó, ya se limpió).

## 2. Arranque obligatorio, en este orden

1. Este archivo, completo.
2. [`docs/ESTADO.md`](docs/ESTADO.md) — estado de cada pieza, hoy.
3. [`docs/ROADMAP.md`](docs/ROADMAP.md) — qué sigue y por qué.
4. Para algo puntual, [`docs/INDICE.md`](docs/INDICE.md) te manda al documento correcto.
5. Si vienes a **auditar**, además [`docs/AUDITORIA.md`](docs/AUDITORIA.md).

**No uses como fuente de verdad** nada de `docs/archivo/` (historia, con contradicciones).

**Nunca cargues `docs/CHANGELOG.md` completo.** Es historial de versiones para consulta
puntual (buscar una versión o un tema), no contexto de arranque — son miles de líneas.

## 3. Reglas no negociables

1. **Regla de oro.** Cualquier cambio en `src/` sube `AssemblyVersion` en `src/ClsMain.cs` +
   entrada en `docs/CHANGELOG.md` + bloque en `src/assets/notas_version.html`, y pasa
   `build/verificar_regla_de_oro.ps1` + `build/probar_humo.ps1` (33 casos, todos en verde)
   antes de darse por bueno. **Compilar no es lo mismo que instalar**: después de cualquier
   cambio que deba llegar a una instalación real, corre `build/generar_instalador.ps1` +
   `build/generar_exes.ps1` — `dist/` no se actualiza solo (pasó de verdad: se hizo una demo
   con una versión vieja porque nadie regeneró el instalador).
2. **Nunca reimplementar lo que CONTPAQi ya hace** — ni el cifrado/validación de
   contraseñas, ni el motor de pólizas, ni la generación de PDF nativa, ni nada similar.
   Se llama al método real vía COM (`Type.GetTypeFromProgID` + `InvokeMember`, o
   `ctx.erp.*`) y se usa su resultado. Ejemplos ya probados: `ctx.erp.EncryptString` para
   contraseñas, `Document.clsMain.OpenForm` / `AccPoliza.clsMain.OpenForm` para abrir
   documentos/pólizas, `AccPoliza.clsMain.SincronizarPolizas` para generarlas.
3. **Contraseñas**: solo por stdin o captura interactiva. Nunca en texto plano en archivos,
   nunca en logs, nunca hardcodeadas ni siquiera en scripts de prueba.
4. **Solo se prueba contra el sandbox** `Distribuciones_Candelas` (`localhost\compac`).
   **Nunca** contra una base de un cliente o de producción. Antes de cualquier operación
   destructiva sobre el sandbox (DELETE masivo, reset, etc.), `BACKUP DATABASE` primero.
5. **Nunca nombrar terceros** (herramientas de otros desarrollistas, nombres de clientes,
   IPs/rutas de red reales) en nada que se vaya a commitear. La lista de términos
   prohibidos vive en `.terminos_prohibidos.local` (raíz, no versionado);
   `build/publicar_release.ps1` escanea el contenido de los instaladores contra ella antes
   de publicar.
6. **Todo script que cambie datos que se ven en una lista de Comercial (crear, modificar, cancelar, timbrar, vincular…) termina
   refrescando el grid** con `ctx.erp.RefreshGrid()` — una vez, después del último cambio. Desde v2.94.0 conserva la fila y la vista.
   Detalle y la función para versiones anteriores: `docs/MANUAL.md`, «Refrescar el grid (estándar)».
7. **GPL-3.0**: todo lo que se aporte se publica bajo la misma licencia.

## 4. Mapa del repo

| Carpeta | Qué es |
|---|---|
| `src/` | Addon principal (servidor COM, Consola, motor Roslyn) |
| `runner/` | `BrosLMV.Runner` — ejecución headless sin Comercial abierto |
| `host/` + `workers/` + `protocol/` | Canal de Python: host x64 fuera de proceso por Named Pipe (en producción desde v2.6.0; pendiente host persistente) |
| `htmlpdf/` | PDF de documentos (WebView2), Cotizador, Configuración de formato, 10 formatos |
| `descargas*/` | `BrosLMV.Descargas` — subproducto independiente. Doc propia: `descargas/DOCUMENTACION.md` |
| `instalador/` + `instaladores/` | Insumos y fuente de los `.exe` instaladores del addon |
| `build/` | Compilar, empaquetar, probar (`probar_humo.ps1`, `verificar_regla_de_oro.ps1`) y publicar (`publicar_release.ps1`) |
| `docs/` | Toda la documentación — empieza en `docs/INDICE.md` |

## 5. Trabajando con varias IAs

- Antes de aceptar un cambio propuesto por otra IA (ChatGPT, Gemini, lo que sea): correr
  `build/verificar_regla_de_oro.ps1` + `build/probar_humo.ps1` sobre ese cambio. Si algo no
  compila o rompe un caso, no se acepta, sin excepción.
- A cada IA se le entrega: este archivo + el o los documentos puntuales de `docs/` que
  apliquen a la tarea + el archivo concreto que se va a tocar. Nunca `docs/` completo, nunca
  el historial de otra conversación.
- División de trabajo real (no por jerarquía, por lo que cada quien puede hacer sin
  depender de los otros): ejecución contra el sistema real (SQL del sandbox, COM,
  compilar) la hace quien tenga las herramientas conectadas ahí mismo — hoy, Claude Code.
  ChatGPT sirve bien como segunda opinión sobre un diff puntual (revisión de código, sin
  tocar nada vivo). Gemini sirve bien para trabajo de texto en volumen (resumir/reorganizar
  documentación larga) que no necesite ejecutar nada contra el sistema real.

## 6. Proyectos satélite (clientes) que usan BrosLMV como referencia/entrenamiento

Esto YA pasó una vez y se perdió dos meses de trabajo real (el Motor de Asientos
Contables, construido en un proyecto de cliente aparte, nunca llegó aquí hasta que se
rescató a mano). **Este repositorio es el único cerebro compartido entre todas las IAs
y todos los proyectos.** Ningún trabajo relacionado con BrosLMV está terminado hasta
que vive aquí — sin excepción, sin importar en qué carpeta, con qué IA, o para qué
cliente se hizo.

**Si estás trabajando en OTRO proyecto** (típicamente `C:\ProyectosLMV\<algo>`) que
usa BrosLMV/Comercial/CONTPAQi como base:

1. Antes de nada, lee este archivo (`C:\MLVTotal\AGENTS.md`) — aplica igual ahí.
2. Cualquier capacidad, corrección o hallazgo **genérico** de BrosLMV que construyas o
   descubras ahí (no específico de ese cliente — ej. un patrón nuevo de `ctx.erp`, una
   tabla propia reusable, un bug real del addon, una forma de resolver algo que
   Comercial no hace solo) se anota en un archivo `PENDIENTE_BACKPORT_BROSLMV.md` en
   la raíz de ESE proyecto — una entrada por hallazgo, con suficiente contexto para
   traerlo después sin haber visto la conversación original.
3. Nunca es aceptable que algo así se quede solo documentado en el proyecto satélite
   "porque ya funciona ahí" — si es genérico, pertenece a `C:\MLVTotal`.
4. Lo específico del cliente (nombres, cuentas contables reales, rutas de red, bases de
   datos) **nunca** se trae tal cual — se generaliza antes de integrarse aquí (mismo
   criterio que la sección 1 y 5 de este archivo). Ver `docs/MOTOR_ASIENTOS_CONTABLES.md`
   como ejemplo ya hecho de esta operación completa.

**Si estás trabajando aquí y el usuario menciona que algo se hizo "en otro proyecto"
o "para entrenar" con un cliente:** pregunta la ruta y ofrece analizarla y traer lo
genérico — no asumas que ya lo sabes. El procedimiento (ya probado una vez): leer toda
la documentación de ese proyecto, identificar qué es genérico vs. específico del
cliente, sanitizar nombres/rutas/servidores, escribirlo en `docs/` de este repo,
verificar con `build/verificar_regla_de_oro.ps1` (incluye el escaneo de términos
prohibidos) antes de commitear.

**Plantilla de arranque para un proyecto satélite nuevo:** copiar
`docs/PLANTILLA_PROYECTO_SATELITE.md` a la raíz de ese proyecto como su propio
`AGENTS.md` (o pegarlo al inicio del chat) para que, desde el primer mensaje, la IA
que trabaje ahí ya sepa esta regla.
