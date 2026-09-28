# BrosLMV — Roadmap

> Qué sigue, en qué orden y por qué. Cada punto dice su **evidencia** (de dónde sale la
> necesidad — nada aquí es "estaría bonito" sin respaldo) y su **siguiente paso concreto**.
> Lo ya hecho está en [`CHANGELOG.md`](CHANGELOG.md); el estado de cada pieza en
> [`ESTADO.md`](ESTADO.md). El plan original de 2026-07-22 (casi todo cumplido) quedó en
> [`archivo/PLAN_IMPLEMENTACION_ARCHIVO.md`](archivo/PLAN_IMPLEMENTACION_ARCHIVO.md).
>
> Prioridades: **P0** = bloquea que lo ya hecho llegue a los clientes o hay un riesgo activo;
> **P1** = siguiente trabajo con valor real demostrado; **P2** = útil, sin urgencia;
> **Ideas** = sin compromiso ni evidencia suficiente todavía.
> Última revisión: 2026-09-27 (v2.93.0).

## P0 — Ahora

| # | Qué | Evidencia | Siguiente paso |
|---|---|---|---|
| ~~0.1~~ | ~~Regenerar el instalador y publicar release 2.93.0~~ | **Hecho 2026-09-28** — [v2.93.0](https://github.com/DonPapiCandelas/BrosLMVTotal/releases/tag/v2.93.0) | — |
| 0.2 | **Primera auditoría externa** | El proyecto creció rápido y con varias IAs; la documentación se consolidó el 2026-09-27 | Seguir [`AUDITORIA.md`](AUDITORIA.md) |
| 0.3 | **Firmar digitalmente los `.exe`** | Un antivirus en un cliente marcó `BrosLMV.Host.exe` como ransomware (falso positivo) | Conseguir certificado de firma de código y firmar en `build/generar_exes.ps1` |

## P1 — Siguiente

| # | Qué | Evidencia | Siguiente paso |
|---|---|---|---|
| 1.1 | **Wrapper del SDK para cobros/pagos** (`Payment.clsMain`) | Es el hueco más grande de `ctx.erp`: dos integraciones distintas terminaron insertando `docFinancialOperation` a mano (pago sin aplicar, sin saldos, sin póliza — `MANUAL.md` §10.6) | Explorar `Payment.clsMain` con el mismo método que `AccPoliza.clsMain` (dump COM + prueba en sandbox) |
| 1.2 | **`BrosLMV.Runner`: escrituras sin supervisión + reintentos** | Ya lo usan integraciones en producción (colas de 80+ OC y 60+ recepciones sin errores) y ya viaja en el instalador (`C:\BrosLMV\runner`); hubo timeouts reales contra Comercial en una de ellas | Decidir política de escritura headless; agregar reintento/telemetría (reglas ya documentadas en `MANUAL.md` §12 "Integraciones externas…") |
| 1.3 | **Motor de Asientos Contables como producto** | Validado en producción con más de un cliente, pero en el repo solo está el motor de cálculo | (a) Confirmar antes si `accPolizaDefinitionItem.CondicionPersonalizada` es una función nativa de renglones condicionales (se cruza con nuestras condiciones propias); (b) generalizar los scripts wrapper como plantillas; (c) editor visual con su regla de oro (`MOTOR_ASIENTOS_CONTABLES.md`) |
| 1.4 | **Abrir/sincronizar pólizas por la vía nativa** | Existen `Document.MostrarPoliza` y `Document.SincronizarPoliza` como comandos del ribbon (`XENGINE_FUNCIONES.md`) | Probarlos desde `ctx.erp.Call` en el sandbox; si funcionan, envolverlos en `ctx.erp` |
| 1.5 | **Aplicar paginación real (Paged.js) a los 10 formatos PDF** | Los formatos actuales no repiten encabezado/pie ni numeran "Página X de Y"; el patrón ya está probado en producción | Una plantilla a la vez, empezando por Orden de Compra (`PAGINACION_PDF.md` "Qué falta") |
| 1.6 | **Python: host persistente (C6d)** | Hoy cada ejecución lanza un host (~1 s la primera vez) | Ver `host/README.md` y `ARQUITECTURA_V3.md` |
| 1.7 | **Plantillas multi-empresa** | En un cliente había 12 scripts casi idénticos, duplicados por empresa propia; dos bugs reales vinieron de filtrar la empresa a mano | Revisar las plantillas de fábrica: que tomen `OwnedBusinessEntityID` del documento/contexto, nunca de una constante |

## P2 — Después

| # | Qué | Evidencia | Siguiente paso |
|---|---|---|---|
| 2.1 | **SDK de Contabilidad como capacidad real** | Documentado y confirmado contra material oficial, nunca corrido desde este repo | Puente x86 propio (patrón del Runner), prueba en vivo, enums completos (`SDK_CONTABILIDAD.md`) |
| 2.2 | **Importador Excel con mapeo visual** | Petición típica de cualquier ERP; `ctx.read_excel()` ya existe | Diseñar el mapeo columna→campo sin código |
| 2.3 | **Confirmar con captura 100% nativa** el vínculo de partida Remisión→Pedido | La evidencia actual (`MANUAL.md` §10.5) mezcla documentos nativos con otros creados por un script | Captura BEFORE/AFTER en el sandbox |
| 2.4 | **Reducir el "bus factor"** | Un solo autor; el pipeline de Python y los componentes COM están explicados en varios lugares | `docs/PIPELINE_PYTHON.md` (diagrama de secuencia) + glosario único de componentes COM |
| 2.5 | **Punto de Venta fuera de privado** | Prototipo en pruebas | Terminar pruebas; decidir si entra al repo público |
| 2.6 | **Metadatos formales de script** (`@lenguaje/@permisos/@timeout`) | Hoy solo marcadores sueltos (`# lang: python`, `# timeout:`) | Esquema en `zzBrosScript` o cabecera declarativa (`ARQUITECTURA_V3.md` §8) |
| 2.7 | **Auto-referenciar `C:\BrosLMV\lib\` en scripts C#** | Las 4 librerías ya se instalan ahí, pero cada script necesita su `#r` (`ScriptRunner.BuildOptions` en `src/Scripting.cs` solo referencia ensamblados fijos) | Agregar las DLL de esa carpeta a `WithReferences` + prueba de humo |
| 2.8 | **Limpiar historial viejo en GitHub** | El historial se reescribió el 2026-09-27; los SHAs viejos pueden seguir en caché de GitHub y en forks | Ticket a GitHub Support para purgar SHAs sin referencia y borrar el PR #3 (cerrado; su encabezado conserva el nombre viejo de la rama, que tenía un nombre de cliente — reemplazado por el PR #4) |

## Deuda de documentación

- `ESPECIFICACION.md` describe la v2.18.0 y `REFERENCIAS_Y_VERIFICACION.md` la 2.11.1 — marcados
  como desactualizados; actualizarlos o reducirlos a lo que siga siendo cierto.
- `MANUAL.md` pasa de 2,000 líneas: valorar dividir §10–§12 (documentos, operaciones y
  advertencias) en documentos propios.

## Ideas (sin compromiso)

Librería comunitaria de scripts/recetas (los paquetes `.bros` ya existen) · suite CFDI
(relacionar CFDI tipo 07, timbrado masivo con reporte) · asistente IA en la Consola
(opt-in, sobre `MANUAL.md`/`XENGINE_FUNCIONES.md`) · puente de solo lectura Power BI/REST
sobre el Runner · sincronizar preferencias entre terminales vía `zzBrosPref` · más recetas
no-code · sitio web del proyecto (pospuesto a propósito por el autor).
