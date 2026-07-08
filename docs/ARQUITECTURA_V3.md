# Arquitectura BrosLMV — multi-lenguaje (host de Python)

> Diseño técnico de la capa multi-lenguaje: cómo BrosLMV ejecuta **Python** (y a futuro
> otros runtimes) fuera del proceso de CONTPAQi sin poner en riesgo el ERP, manteniendo
> el mismo `ctx` y la conexión viva. Insumo de referencia: el catálogo XEngine
> ([`XENGINE_FUNCIONES.md`](XENGINE_FUNCIONES.md)).
>
> **Estado:** la base ya está **en producción** — C# (en proceso), Python (host x64 por
> Named Pipes + Protobuf) y SQL directo conviven en el mismo botón/consola (ver
> [`CHANGELOG.md`](CHANGELOG.md), versiones 2.5–2.10). La interfaz para Python (UI nativa
> `ctx.form` + HTML `ctx.show_html`) es trabajo en curso.

---

## 1. Principio fundamental: fuera de proceso (OOP)

Inyectar runtimes modernos (Python 3.x, Node) dentro de `ComercialSP.exe` (32-bit) es
**inviable y peligroso**: un fallo nativo de una librería (numpy, un driver) **tumbaría el
ERP completo**. Por eso BrosLMV se divide en dos capas:

1. **Orquestador (C# 32-bit):** el addon COM actual, **en proceso** dentro de CONTPAQi.
   Dueño del contexto vivo (selección, módulo, usuario, conexión, XEngine). Maneja UI
   nativa, ejecuta C# con Roslyn, y despacha lo demás.
2. **Host de lenguajes (x64):** proceso **independiente y persistente** que ejecuta los
   scripts de usuario en un entorno moderno y aislado.

```
┌─────────────────────────────────────────────┐
│ ComercialSP.exe — 32 bits                  │
│ BrosLMV.dll (.NET 4.8)                      │
│  ├─ XEngine / COM / selección del grid     │
│  ├─ empresa, módulo, usuario, conexión     │
│  ├─ ejecución C# (Roslyn) — en proceso     │
│  ├─ UI nativa (WinForms / WebView)         │
│  └─ Context Gateway + cliente de pipe      │
└───────────────────┬─────────────────────────┘
                    │ Named Pipe (ACL + token UUID)
                    │ framing [4B len][Protobuf]
┌───────────────────▼─────────────────────────┐
│ BrosLMV.Host.exe — 64 bits                 │
│  ├─ supervisor de workers (auto-healing)   │
│  ├─ SQL centralizado (solo-proxy)          │
│  ├─ permisos, auditoría, timeouts          │
│  ├─ contrato ctx común                     │
│  └─ Python Worker Pool (CPython 3.13 x64)  │
│       (futuro: Node, WASM)                 │
└─────────────────────────────────────────────┘
```

---

## 2. Decisiones firmes (resumen)

- **D1** Python fuera de proceso, host x64. **D2** IPC = Named Pipes. **D3** Protobuf con
  framing propio (no gRPC en el addon). **D4** SQL **solo-proxy** (el script nunca ve la
  contraseña). **D5** CPython embeddable x64 empacado. **D6** ACL + token UUID en el pipe.
  **D7** Credenciales con DPAPI + login `BrosLMV_Runtime`. **D8** Tipos: C# / Python / SQL.

### Por qué Named Pipes + Protobuf y no gRPC
gRPC (HTTP/2) en .NET Framework 4.8 de 32-bit añade dependencias frágiles **dentro de
ComercialSP**. Un framing mínimo `[4 bytes longitud][mensaje Protobuf]` sobre Named Pipes
da request/response, eventos, callbacks, cancelación, streaming, timeouts y versionado
**sin** la pila HTTP/2. Se evaluó gRPC pero se descartó por el peso de sus dependencias
dentro de un host heredado de 32-bit.

### Por qué SQL solo-proxy
```
Python → ctx.query(...) → BrosLMV.Host → SqlConnection → SQL Server
```
El script nunca conoce la contraseña ni puede imprimir la cadena. El host centraliza
timeouts, transacciones, auditoría y rollback (incluso si el worker muere). La **vía
directa** `pyodbc` (más rápida para cargas masivas/pandas) queda para **v3.1** detrás del
permiso `db.direct`.

---

## 3. El contrato `ctx` (agnóstico al lenguaje)

`ctx` deja de ser una clase C# y pasa a ser un **contrato en `protocol/broslmv.proto`**.
Cada lenguaje implementa un **proxy delgado** que expone métodos naturales. Tres planos:

### 3.1 Contexto congelado (snapshot al pulsar el botón)
`ExecutionId`, `AppKey`, `Empresa`, `Servidor`, `BaseDatos`, `UserID`, `ModuleID`,
`SelectedIds`, `ScriptPath`, `Language`, `RuntimeVersion`, `SoloLectura`, `Capabilities`.
La selección permanece **inmutable** durante la ejecución (refresco explícito con
`ctx.refresh_context()`).

### 3.2 Operaciones de datos (host, solo-proxy)
`ctx.scalar` · `ctx.query` · `ctx.query_one` · `ctx.execute` · `ctx.non_query` ·
`ctx.query_batches(batch_size)` · `ctx.bulk_insert` · `with ctx.transaction() as tx:`
Siempre **parametrizado** (`@param` + dict). Decimales como representación **decimal
exacta** (string), nunca `double`, para importes monetarios.

### 3.3 Operaciones dependientes del ERP (las ejecuta el addon en proceso)
- **UI:** `ctx.msg` · `ctx.confirm` · `ctx.progress` · `ctx.form(...)` · `ctx.show_html` ·
  `ctx.select_file` · `ctx.select_folder`.
- **ERP/XEngine** (ver `XENGINE_FUNCIONES.md`): `ctx.erp.AffectStockNEW(id)` ·
  `ctx.erp.RecalcCompleto(id)` · `ctx.erp.CancelDocument(id)` · `ctx.erp.RefreshGrid()` ·
  `ctx.erp.OpenModule(id)` · helpers directos `ctx.erp.GetTotalLetter(n)` ·
  `ctx.erp.GetProductStock(pid)` · `ctx.erp.GetSalePrice(...)` · `ctx.erp.ValidRFC(...)`.
  **Nombres PascalCase**, iguales que en C#.
- **Conexión viva (avanzado/restringido):** `ctx.live.query/scalar/execute` — el addon
  corre la operación sobre la conexión ADO real de CONTPAQi. Solo casos especiales.

### 3.4 Lo que NO cruza el límite de proceso
`SqlConnection`, `ADOConnection`, `XEngineLib`, `Form`/`Control`, objetos COM, `AppDomain`,
`Assembly`. Pertenecen al proceso .NET; el worker solo recibe **datos y capacidades**.

---

## 4. Protocolo (resumen)

Mensajes enmarcados: `[4 bytes longitud][Protobuf]`. Sobre (`Envelope`):
`protocol_version`, `request_id`, `execution_id`, `message_type`, `timestamp`, `payload`.

Tipos mínimos: `Hello/HelloResponse`, `ExecuteScript/Response`, `ContextCall/Response`,
`UiRequest/Response`, `ProgressEvent`, `LogEvent`, `ArtifactEvent`, `CancelExecution`,
`Heartbeat`, `WorkerStatus`, `ExecutionCompleted`, `ExecutionFailed`.

Tipos de datos preservados: NULL, Boolean, Int32/64, **Decimal (string exacto)**, Double,
String, Bytes, Date/Time/DateTime/Offset, Guid, List, Dictionary, Table.

Resultados SQL grandes: < 5 MB Protobuf normal · 5–50 MB streaming por lotes · > 50 MB
archivo temporal / Apache Arrow (v3.1).

---

## 5. Ciclo de vida y resiliencia (auto-healing)

- El orquestador C# **monitorea el PID** de `BrosLMV.Host.exe`; si muere, lo **relevanta**
  silenciosamente antes de la siguiente ejecución.
- **Timeouts:** toda llamada del addon hacia el host tiene *deadline*. Si un script excede
  X s, se **aborta**, se libera la UI de Comercial, se muestra "Script Timeout" y se
  reinicia el worker.
- **Job Objects** de Windows para limitar memoria/CPU, matar árbol de procesos, evitar
  huérfanos. Límites sugeridos: `timeout 120s`, `memory 1024 MB`, sin procesos hijos.
- **Workers calientes** (v3.1): pool con Python ya inicializado; namespace nuevo por
  ejecución; reciclaje por nº de ejecuciones / memoria / si dejan de responder.

---

## 6. Seguridad

- **Pipe:** ACL que solo permite al usuario actual de Windows + **token UUID** por arranque
  (el addon lo pasa a Python por args; Python lo incluye como metadata en cada llamada; el
  host rechaza peticiones sin token válido).
- **Credenciales:** DPAPI / Credential Manager; login dedicado `BrosLMV_Runtime` en vez de
  SA. El script recibe **capacidades, no secretos**.
- **Permisos (capabilities) por script:** `db.read`, `db.write`, `db.transaction`,
  `ui.dialog`, `ui.progress`, `ui.html`, `erp.selection`, `erp.refresh`,
  `erp.open_document`, `erp.live_connection`, `filesystem.temp`, `network`, `storage.*`.
  *(Nota: Python normal no es sandbox total; para código no confiable haría falta
  AppContainer / token restringido / WASM.)*
- **Correlation IDs:** un ID de ejecución por clic, presente en los logs de C# **y** de
  Python (archivos separados en `C:\BrosLMV\logs\`) para cruzar fallos.

---

## 7. Empaque del runtime

```
C:\BrosLMV\
├─ bin\                      (addon .NET actual)
├─ host\BrosLMV.Host.exe     (x64)
├─ workers\python\           (BrosLMV.PythonWorker.exe + runner.py + paquete broslmv\)
├─ runtimes\python\3.13.x\   (CPython embeddable x64: python.exe, *.dll, Lib\, packages\)
├─ scripts\<EMPRESA>\        (scripts por empresa, ya existe)
├─ data\  └─ logs\
```

Reglas: no depender del Python del usuario · no tocar el PATH global · versiones lado a
lado · validar hashes · firmar ejecutables · fijar versiones de paquetes. Paquetes
separados: **Core** (requests, jinja2, openpyxl, lxml, pillow, reportlab) y **Data**
(numpy, pandas, pyarrow, matplotlib) — para no instalar cientos de MB donde no se usan.

**Presupuesto de tamaño del instalador (autorizado por el usuario, 2026-06-26):** no es
problema hasta **~1–2 GB**. Da margen de sobra para empacar CPython embeddable + el host
self-contained .NET 8 + el paquete **Data** completo (numpy/pandas/pyarrow pesan cientos
de MB). Aun así se mantiene la separación Core/Data para no inflar instalaciones que no
usan análisis pesado.

---

## 8. Lo que falta decidir / construir

- ~~**Modelo de UI**~~ **DECIDIDO (D9, 2026-06-26): Opción A "doble render"** — Python
  describe la UI; el addon C# la renderiza con `ctx.form(...)` (WinForms nativo
  declarativo) + `ctx.show_html(...)` (WebView2). Sin modo in-process.
  **Adelanto (2026-07-06):** no hace falta esperar a `ctx.show_html` para tener WebView2
  disponible — un script de **C#** ya puede usarlo hoy vía `#r`, sin cambios al núcleo.
  Ver [`PLAN_LIBRERIAS_EXTERNAS.md`](PLAN_LIBRERIAS_EXTERNAS.md) §3. Cuando se
  implemente `ctx.show_html` para Python, puede reusar el mismo patrón de
  inicialización (por evento, `userDataFolder` propio) ya validado ahí.
- **`broslmv.proto`** definitivo (empezar por aquí en la implementación → punto C2).
- Forma exacta de **`ctx.form`** (DSL de formularios que el addon renderiza en WinForms).
- Metadatos de script: cabecera declarativa (`@nombre/@lenguaje/@runtime/@permisos/
  @timeout`) y/o columnas en `zzBrosScript`.
- Eventos/hooks (`Al Guardar`, etc.) y sistema de **tokens** (`{pID}`, `{DATOS:Campo}`):
  diseño de almacenamiento y motor de sustitución.

---

## 9. Organización de repo propuesta (cuando empiece v3.0)

```
src/
├─ Addon/    (ClsMain.cs, Scripting.cs, ContextGateway.cs, HostClient.cs, UiDispatcher.cs, Protocol/)
├─ Host/     (BrosLMV.Host.csproj, WorkerSupervisor, ExecutionManager, SqlGateway,
│             TransactionManager, CredentialManager, PermissionManager, ArtifactManager, Protocol/)
├─ Workers/Python/  (BrosLMV.PythonWorker.csproj, PythonProcess.cs, runner.py,
│                    broslmv/{__init__,ctx,database,ui,protocol}.py)
├─ Protocol/ (broslmv.proto, generated/)
└─ Shared/   (Contracts/, Logging/, Security/)
```

---

## 10. Decisiones clave del diseño

- Host + workers por lenguaje, SQL solo-proxy, capabilities, contexto congelado, decimales
  exactos, framing Protobuf sin gRPC, empaque CPython.
- Token UUID de sesión en el pipe, Correlation IDs, auto-reinicio del host, modelo de
  "doble vía" SQL (proxy + directo) — adoptado el proxy ya, el directo en v3.1.
- El hallazgo de que los scripts reales son UI-heavy (cambia la prioridad del modelo de UI)
  y el mapeo del catálogo XEngine a `ctx.erp.*`.
