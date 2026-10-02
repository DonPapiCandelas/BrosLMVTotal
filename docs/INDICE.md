# Documentación BrosLMV — Índice

> **Antes que nada**, lee [`../AGENTS.md`](../AGENTS.md) — reglas no negociables del repo
> (regla de oro, qué nunca reimplementar, qué nunca se publica), para cualquier IA o persona.
>
> Este índice cubre el addon de Comercial PRO (botones + Consola), `BrosLMV.Runner` y
> `htmlpdf/`. `BrosLMV.Descargas` es un subproducto independiente con su propia
> documentación: [`../descargas/DOCUMENTACION.md`](../descargas/DOCUMENTACION.md).

## Dónde vamos

| Documento | Qué encontrarás |
|---|---|
| [`ESTADO.md`](ESTADO.md) | Estado de cada pieza, hoy. Punto de entrada al retomar |
| [`ROADMAP.md`](ROADMAP.md) | Qué sigue, priorizado, con evidencia y siguiente paso |
| [`AUDITORIA.md`](AUDITORIA.md) | Guía para una revisión externa: qué revisar, reglas, formato del reporte |
| [`CHANGELOG.md`](CHANGELOG.md) | Historial de versiones (consulta puntual, no se lee completo) |

## Usar BrosLMV

| Documento | Qué encontrarás |
|---|---|
| [`../README.md`](../README.md) | Qué es, diagrama general, cómo compilar |
| [`INSTALACION.md`](INSTALACION.md) | Instalar en una empresa/equipo |
| [`MANUAL.md`](MANUAL.md) | **El documento central**: crear botones, API `ctx`/`ctx.erp`, crear documentos (§7), documentos derivados y vínculos (§10), operaciones financieras (§10.6), advertencias y hallazgos reales (§12) |
| [`SDK_REFERENCIA.md`](SDK_REFERENCIA.md) · [`SDK_GUIAS.md`](SDK_GUIAS.md) | **Manual del SDK**: cada función de `ctx`/`ctx.erp` (C#, Python, SQL) con parámetros, ejemplos y avisos; guías paso a paso. Se genera del catálogo `src/assets/sdk_catalogo.json` |
| [`CREAR_BOTON.md`](CREAR_BOTON.md) | Asistente «Crear botón…»: pestañas, módulos, usuarios, íconos, editar botones existentes |
| [`TRAZABILIDAD_DOCUMENTO.md`](TRAZABILIDAD_DOCUMENTO.md) | Plantilla de fábrica «Trazabilidad del documento»: de dónde viene y a dónde fue (los cuatro tipos de vínculo, vínculos manuales) |
| [`ASIGNAR_CENTRO_COSTO.md`](ASIGNAR_CENTRO_COSTO.md) | Plantilla de fábrica: asignar un centro de costo a muchos documentos, con vista previa y deshacer |
| [`PDF_MASIVO_DOCUMENTOS.md`](PDF_MASIVO_DOCUMENTOS.md) | Plantilla de fábrica: PDF de todos los documentos seleccionados (sueltos, ZIP o unidos) y modo lote del motor HtmlToPdf |
| [`SALDOS_ESTADOS_CUENTA.md`](SALDOS_ESTADOS_CUENTA.md) | Plantilla de fábrica: cuentas por cobrar y por pagar con fecha de corte y antigüedad, y estados de cuenta de clientes y proveedores |
| [`CAPACIDADES.md`](CAPACIDADES.md) | Qué se puede construir: reportes, análisis, integraciones |
| [`DASHBOARDS_HTML.md`](DASHBOARDS_HTML.md) | Reportes HTML rápidos y portables (`ctx.dashboard()`, `ctx.show_html`) |

## Escribir scripts

| Documento | Qué encontrarás |
|---|---|
| [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) | Referencia del API `ctx.*` y `ctx.erp.*`: métodos, firmas, ejemplos |
| [`PYTHON.md`](PYTHON.md) | Botones en Python: API, SQL por la conexión viva, límites conocidos |
| [`XENGINE_FUNCIONES.md`](XENGINE_FUNCIONES.md) | Catálogo de `XEngineLib` y comandos nativos (insumo de `ctx.erp`) |
| [`UI_VENTANAS.md`](UI_VENTANAS.md) | Ventanas de los botones: modal vs. modeless, hilos, reglas |
| [`DISENO.md`](DISENO.md) | Tokens visuales y patrones de UI ya construidos |
| [`REQUISICION_SOLICITUD_COMPRA.md`](REQUISICION_SOLICITUD_COMPRA.md) | Caso documentado a fondo: la plantilla de Solicitud de Compra |
| [`RECETAS_NOCODE.md`](RECETAS_NOCODE.md) | Motor de recetas no-code (MVP construido, sin trabajo activo) |
| [`MIGRAR_BOTONES_RT.md`](MIGRAR_BOTONES_RT.md) | Migrar a BrosLMV botones de otra herramienta de scripting (tablas `rt*`) |

## Contabilidad y PDF

| Documento | Qué encontrarás |
|---|---|
| [`MOTOR_ASIENTOS_CONTABLES.md`](MOTOR_ASIENTOS_CONTABLES.md) | Pólizas de cobro/pago multi-moneda (validado en producción) |
| [`SDK_CONTABILIDAD.md`](SDK_CONTABILIDAD.md) | Escribir pólizas directo en CONTPAQi Contabilidad (investigado, sin probar en vivo) |
| [`CONTABILIDAD_MODELO_DATOS.md`](CONTABILIDAD_MODELO_DATOS.md) | Leer Contabilidad: las trampas del esquema, verificadas con datos reales |
| [`PAGINACION_PDF.md`](PAGINACION_PDF.md) | Paginación real en los PDF (Paged.js) |

## Desarrollar el núcleo

| Documento | Qué encontrarás |
|---|---|
| [`DESARROLLO.md`](DESARROLLO.md) | Modificar y recompilar |
| [`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md) | Host de Python fuera de proceso: Named Pipes + Protobuf, seguridad |
| [`../host/README.md`](../host/README.md) · [`../protocol/README.md`](../protocol/README.md) | Detalle del host y del protocolo |
| [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md) | Panel de Referencias de la Consola y método para verificar el API (⚠️ estado de v2.11.1) |
| [`ESPECIFICACION.md`](ESPECIFICACION.md) | Blueprint de reconstrucción (⚠️ describe v2.18.0 — ver aviso dentro) |
| [`PLANTILLA_PROYECTO_SATELITE.md`](PLANTILLA_PROYECTO_SATELITE.md) | Para proyectos de cliente que usan BrosLMV como referencia (`AGENTS.md` §6) |

## Gobernanza (raíz del repo)

[`../LICENSE`](../LICENSE) (GPL-3.0) · [`../CONTRIBUTING.md`](../CONTRIBUTING.md) ·
[`../SECURITY.md`](../SECURITY.md) · [`../CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md)

## Historia

[`archivo/`](archivo/README.md) — documentos que ya cumplieron su función (planes ejecutados,
bitácoras, investigaciones resueltas). No son fuente de verdad.

## Regla de documentación

Cada cambio al código va con su documentación **en el mismo commit**: `AssemblyVersion` en
`src/ClsMain.cs`, entrada en `CHANGELOG.md`, bloque en `src/assets/notas_version.html` y los
`.md` afectados. Todo hallazgo real sobre Comercial (no solo cambios de código) va también a
`MANUAL.md`. Verificado por `build/verificar_regla_de_oro.ps1`.
