# `protocol/` — Contrato del canal C# ↔ Python (v3.0)

Aquí vive **`broslmv.proto`**, el contrato Protocol Buffers que define **todos** los
mensajes que cruzan el Named Pipe entre el **addon C#** (en proceso dentro de
ComercialSP) y el **host de Python x64** (fuera de proceso).

> Diseño completo en [`../docs/ARQUITECTURA_V3.md`](../docs/ARQUITECTURA_V3.md).

## Cómo viaja un mensaje

```
[4 bytes longitud (big-endian)][ Envelope serializado en Protobuf ]
```

Sobre Named Pipes, **sin gRPC** (evita la pila HTTP/2 dentro de ComercialSP 32-bit).
Cada mensaje es un `Envelope`; la correlación request/response va por `request_id`, y
cada llamada lleva el `auth_token` UUID del pipe (seguridad, decisión D6).

## Mapa del contrato

| Sección del `.proto` | Qué define |
|---|---|
| `Envelope` | Sobre de todo mensaje (versión, ids, token, `oneof` del tipo) |
| `Value` / `Table` | Sistema de tipos. **Importes monetarios = Decimal exacto (string)**, nunca `double`. NULL = `oneof` sin asignar |
| Handshake / ciclo de vida | `Hello`, `ExecuteScript` (+`ExecutionContext` congelado), `ExecutionCompleted/Failed`, `CancelExecution` |
| `ContextCall` | El script llama de vuelta (`ctx.*`): SQL **solo-proxy** (`SqlRequest`), `BulkInsert`, transacciones, y `ErpCall` (espejo de `ErpContext` / `ctx.erp.*`) |
| `UiRequest` / `UiResponse` | UI **Opción A (D9)**: `UiForm` (WinForms declarativo) y `UiShowHtml` (WebView2). Python describe, el addon C# renderiza |
| Eventos | `ProgressEvent`, `LogEvent`, `ArtifactEvent` (Excel/PDF/HTML generados) |
| Salud | `Heartbeat`, `WorkerStatus` |

El **catálogo de métodos** de `ErpCall.method` es la clase `ErpContext` del addon
(ver [`../docs/SCRIPTING_CONTRATOS.md`](../docs/SCRIPTING_CONTRATOS.md) §7). El contrato
de C# y el de Python derivan de **este mismo archivo**.

## Regenerar el código (cuando se implemente el host, punto C3)

Requiere `protoc` (viene con el paquete NuGet `Google.Protobuf.Tools` o el binario de
[protocolbuffers/protobuf](https://github.com/protocolbuffers/protobuf/releases)).

```powershell
# C# (host / addon)
protoc --proto_path=protocol --csharp_out=<destino> broslmv.proto

# Python (paquete broslmv)
protoc --proto_path=protocol --python_out=<destino> broslmv.proto
```

> En la práctica, el proyecto del host (C3) referenciará `Grpc.Tools`/`Google.Protobuf`
> para generar el C# en cada build, y el empaque del worker generará el `_pb2.py`.

## Estado

- ✅ Contrato definido y **validado con `protoc` 28.3** (compila y genera C# sin errores).
- ⏳ Sin código generado todavía en el repo: la generación se cablea al crear el host (C3).
- El número de versión del protocolo vive en `Envelope.protocol_version` (empieza en 1).
