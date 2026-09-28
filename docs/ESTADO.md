# Estado del proyecto y cómo continuar

> **Punto de entrada al retomar.** Corto a propósito — historia completa versión por versión
> en [`CHANGELOG.md`](CHANGELOG.md), reglas no-negociables en [`../AGENTS.md`](../AGENTS.md).
> Bitácora larga de sesiones anteriores a 2026-09-27 (con contradicciones entre sí, léase con
> cautela): [`ESTADO_ARCHIVO.md`](archivo/ESTADO_ARCHIVO.md).

## REGLA DE ORO: documentar todo, siempre

Cualquiera que retome el proyecto debe poder hacerlo **desde cero** con solo los `.md` + el
código. Cada cambio en `src/` se acompaña, en el mismo commit, de: entrada en
[`CHANGELOG.md`](CHANGELOG.md) + `AssemblyVersion` en `src/ClsMain.cs` + bloque en
`src/assets/notas_version.html` + los `.md` afectados. Verificado por
`build/verificar_regla_de_oro.ps1` (y en CI, `.github/workflows/ci.yml`, en cada push/PR).
Todo cambio real, "gotcha" o límite descubierto va también a `MANUAL.md`, no solo al
CHANGELOG — ver detalle en [`AGENTS.md`](../AGENTS.md).

## ⚠️ TRAMPA: compilar no es instalar

`dist/` no se actualiza solo. Después de cualquier cambio que deba llegar a una instalación
real: `build/generar_instalador.ps1` (mata `ComercialSP.exe` a la fuerza — avisar si hay una
demo en curso) + `build/generar_exes.ps1`. Ya pasó de verdad: se hizo una demo con un cliente
con una versión semanas más vieja que la de GitHub porque nadie regeneró el instalador.

## Estado actual (2026-09-27, addon v2.93.0)

| Pieza | Estado | Dónde leer más |
|---|---|---|
| **Addon** (botones + Consola, C#/Python/SQL en `zzBrosScript`) | En producción en varias empresas. Últimos cambios: correcciones reales salidas del barrido de clientes (2.92.0–2.93.0) | `CHANGELOG.md`, `MANUAL.md` |
| **Python** | En producción desde v2.6.0: host x64 fuera de proceso por Named Pipe + SQL por la conexión viva de Comercial. Pendiente: host persistente (C6d) | `PYTHON.md`, `ARQUITECTURA_V3.md`, `host/README.md` |
| **PDF de documentos + correo** (`htmlpdf/`) | En producción (2.87.0–2.89.0), 10 formatos genéricos. Paginación real con Paged.js disponible (2.91.0), sin aplicar a las plantillas todavía | `PAGINACION_PDF.md` |
| **`BrosLMV.Runner`** (headless) | Funciona, lo usan integraciones externas en producción (colas de documentos) y el instalador lo copia a `C:\BrosLMV\runner` (al menos desde v2.90.0). Pendiente: política de escrituras sin supervisión y reintentos | `MANUAL.md` §12 "Integraciones externas vía BrosLMV.Runner" |
| **Motor de recetas no-code** | MVP construido (2 recetas, pasos encadenados, asistente). Sin trabajo activo | `RECETAS_NOCODE.md` |
| **Motor de Asientos Contables** | Validado en producción (cobros/pagos multi-moneda); en el repo solo está el motor de cálculo y el esquema | `MOTOR_ASIENTOS_CONTABLES.md` |
| **Contabilidad** (SDK y modelo de datos) | Investigado y documentado; el SDK no se ha probado en vivo desde este repo | `SDK_CONTABILIDAD.md`, `CONTABILIDAD_MODELO_DATOS.md` |
| **`BrosLMV.Descargas`** (descarga masiva SAT) | Subproducto independiente, instalador propio v2.1.1 | `descargas/DOCUMENTACION.md` |
| **Punto de Venta** | Prototipo **privado**, fuera del repo público a propósito | `AGENTS.md` §1 |

Conocimiento consolidado recientemente (2026-09-27): el barrido de los proyectos de clientes
terminó y lo genérico ya está en `MANUAL.md` (§10.5 vínculos entre documentos, §10.6
operaciones financieras, §12 advertencias), `DASHBOARDS_HTML.md` y `MIGRAR_BOTONES_RT.md`.
La documentación vieja o superada se movió a `docs/archivo/`.

**Pendiente inmediato:** regenerar el instalador para que 2.92.0/2.93.0 lleguen a
instalaciones nuevas.

## Qué sigue

El plan priorizado vive en [`ROADMAP.md`](ROADMAP.md). Para una revisión externa del proyecto,
ver [`AUDITORIA.md`](AUDITORIA.md).
