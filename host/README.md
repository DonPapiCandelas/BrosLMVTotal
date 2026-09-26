# `host/` — BrosLMV.Host (supervisor del canal v3.0)

Proceso **x64 (.NET 8, Windows)** que orquesta la ejecución de scripts **Python fuera de
proceso**. Es el lado **servidor** del Named Pipe; el **addon** C# (dentro de ComercialSP)
es el **cliente**. Diseño en [`../docs/ARQUITECTURA_V3.md`](../docs/ARQUITECTURA_V3.md).

> Contrato del canal: [`../protocol/broslmv.proto`](../protocol/broslmv.proto)
> (se genera a C# en cada build). Diseño: [`../docs/ARQUITECTURA_V3.md`](../docs/ARQUITECTURA_V3.md).

## Compilar y probar

```bash
# Compilar el host
dotnet build host/BrosLMV.Host/BrosLMV.Host.csproj -c Release

# Prueba de humo (x64 + codegen del proto)
dotnet host/BrosLMV.Host/bin/Release/net8.0-windows/BrosLMV.Host.dll

# Abrir el pipe seguro (imprime PIPE=/TOKEN= y escucha)
dotnet ... BrosLMV.Host.dll --serve [--pipe <nombre>] [--token <uuid>]

# Tests locales (viven en /.temp_tests, gitignored):
dotnet run --project .temp_tests/host_c3b/test_c3b.csproj -c Release   # handshake seguro
dotnet run --project .temp_tests/host_c3c/test_c3c.csproj -c Release   # router de sesión
dotnet run --project .temp_tests/host_c3d/test_c3d.csproj -c Release   # Python round-trip
dotnet run --project .temp_tests/host_c4/test_c4.csproj -c Release     # Python embeddable
dotnet run --project .temp_tests/host_c5/test_c5.csproj -c Release     # SDK broslmv ctx minimo
dotnet run --project .temp_tests/host_c5b/test_c5b.csproj -c Release   # SQL gateway fake
dotnet run --project .temp_tests/host_c5c/test_c5c.csproj -c Release   # SQL Server real
dotnet run --project .temp_tests/host_c5d/test_c5d.csproj -c Release   # auditoria + callbacks
```

## Arquitectura actual (C3a-C3d)

```
addon C# (cliente)  ──Named Pipe──►  BrosLMV.Host (servidor)
                                       │
   [4B len BE][Envelope Protobuf]      ├─ Security/PipeAcl  : ACL = solo usuario actual (D6)
                                       ├─ PipeServer        : handshake (token UUID) + sesión
                                       ├─ Protocol/FrameCodec: framing [4B len][protobuf]
                                       ├─ MessageRouter     : despacha el Envelope por oneof
                                       └─ Handlers/IExecutionHandler : ejecuta el script
                                              ├─ EchoExecutionHandler (stub para tests)
                                              └─ PythonExecutionHandler (C3d)
                                                    └─ Workers/PythonProcess
                                                          └─ workers/python/runner.py
```

### Seguridad del canal (dos capas)
1. **ACL del pipe** (`PipeAcl.CurrentUserOnly`): solo el usuario de Windows actual.
2. **Token UUID** por arranque: cada `Envelope.auth_token` se valida en el handshake en
   **comparación de tiempo constante**. Token inválido → `AUTH_DENIED`; primer mensaje que
   no es `Hello` → `BAD_HANDSHAKE`.

### Flujo de una sesión (`PipeServer.ServeSessionAsync`)
1. Cliente conecta → `Hello` (con token) → `HelloResponse`.
2. Bucle: leer `Envelope` → `MessageRouter.RouteAsync` → responder.
   - `ExecuteScript` → `IExecutionHandler` → `ExecutionCompleted`/`ExecutionFailed`.
   - `Heartbeat` → `Heartbeat`. `CancelExecution` → fin de sesión.
   - Otros → `ExecutionFailed { code = UNSUPPORTED }`.

### C3d: primer worker Python

`Program --serve` usa `PythonExecutionHandler`: por cada `ExecuteScript` lanza el runner
`workers/python/runner.py`. Desde C4, `PythonProcess` prefiere el CPython
embeddable empacado (`C:\BrosLMV\runtimes\python\python.exe`) y solo usa `python` del
PATH como fallback de desarrollo. El runner recibe JSON por stdin, ejecuta el codigo con
`exec`, captura stdout/stderr y devuelve JSON. Desde C5a, el paquete `broslmv` expone
`ctx` y el runner puede hacer llamadas remotas al host por `context_call`.

### C5a: SDK Python minimo

Los scripts Python pueden hacer:

```python
from broslmv import ctx
ids = ctx.get_selected_ids()
ctx.msg(f"Seleccionados: {len(ids)}")
```

Implementado por ahora: contexto, seleccion, `msg`, `log`, SQL remoto contra
`IPythonContextGateway` y gateway SQL Server real por defecto en `PythonExecutionHandler`.
El host lee `BROSLMV_SQL_CONN`, `broslmv_cred.dat` DPAPI o `broslmv_conn.txt`.

## Estado y siguiente paso

- ✅ **C3a** esqueleto + codegen · ✅ **C3b** pipe seguro + framing · ✅ **C3c** router ·
  ✅ **C3d** Python round-trip · ✅ **C4** CPython embeddable · ✅ **C5a** SDK minimo ·
  ✅ **C5b** SQL remoto contra gateway · ✅ **C5c** SQL Server real ·
  ✅ **C5d** auditoria (`Audit/`) + callbacks UI/log/progress (`Callbacks/`). **Cierra Bloque C.**
- ⏭️ **C6**: integrar el ADDON como cliente del pipe (ComercialSP lanza el host, abre el
  pipe, manda `ExecuteScript`) y render real de UI Opcion A (`ctx.form`/`ctx.show_html`).
  El `LoggingHostCallbackSink` se reemplaza por el relay real al addon por el pipe.

## Trampas conocidas (para quien continúe)

- **`ExecutionContext` es ambiguo** con `System.Threading.ExecutionContext` cuando hay
  `ImplicitUsings`. Usa el nombre completo **`BrosLMV.Protocol.ExecutionContext`**.
- El **código generado del `.proto`** vive en `obj/` y **no se versiona** (se regenera en
  cada build vía `Grpc.Tools`, `GrpcServices=None`).
- El host es **`net8.0-windows`** (usa ACL de pipes). Al empacar (C4) irá **self-contained
  x64**, así el cliente no necesita .NET instalado.
- Para preparar el runtime: `powershell -ExecutionPolicy Bypass -File build\descargar_python.ps1`.
  La version empacada actual es CPython 3.13.14 x64.
