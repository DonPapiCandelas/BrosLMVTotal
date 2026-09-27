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
2. [`docs/ESTADO.md`](docs/ESTADO.md) — qué se está haciendo ahora mismo.
3. Para algo puntual, [`docs/INDICE.md`](docs/INDICE.md) te manda al documento correcto.

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
6. **GPL-3.0**: todo lo que se aporte se publica bajo la misma licencia.

## 4. Mapa del repo

| Carpeta | Qué es |
|---|---|
| `src/` | Addon principal (servidor COM, Consola, motor Roslyn) |
| `runner/` | `BrosLMV.Runner` — ejecución headless sin Comercial abierto |
| `host/` + `workers/` + `protocol/` | Canal C# ↔ Python v3.0 (en desarrollo) |
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
