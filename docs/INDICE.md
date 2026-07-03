# Documentación BrosLMV — Índice

Orden de lectura sugerido según lo que necesites.

## Estado y continuación (empezar aquí al retomar)

| Documento | Qué encontrarás |
|-----------|-----------------|
| [`ESTADO.md`](ESTADO.md) | **Dónde vamos, pendientes y qué sigue.** Punto de entrada al retomar el proyecto + REGLA DE ORO de documentación |

## Empezar y usar

| # | Documento | Qué encontrarás | Para quién |
|---|-----------|-----------------|-----------|
| 1 | [`../README.md`](../README.md) | Vista general, estructura del proyecto, cómo compilar | Todos |
| 2 | [`INSTALACION.md`](INSTALACION.md) | Cómo instalar en una empresa/equipo | Quien instala |
| 3 | [`MANUAL.md`](MANUAL.md) | Cómo crear y editar botones; API de `ctx`; ejemplos | Quien crea botones |
| 4 | [`CAPACIDADES.md`](CAPACIDADES.md) | Qué se puede construir: reportes HTML, análisis, librerías | Quien diseña soluciones |

## Escribir scripts (C#, Python, SQL)

| # | Documento | Qué encontrarás | Para quién |
|---|-----------|-----------------|-----------|
| 5 | [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) | **Referencia completa del API** `ctx.*` y `ctx.erp.*` (XEngine): métodos, firmas, ejemplos, patrón crear-documento | Quien escribe scripts C# |
| 6 | [`PYTHON.md`](PYTHON.md) | **Botones en Python:** cómo marcar un script Python, API `ctx` en Python, SQL por la conexión viva, esquema real ComercialSP | Quien escribe scripts Python |
| 7 | [`XENGINE_FUNCIONES.md`](XENGINE_FUNCIONES.md) | Catálogo de funciones de `XEngineLib` (insumo de la capa `ctx.erp.*`) | Referencia técnica |
| 7b | [`REFERENCIAS_Y_VERIFICACION.md`](REFERENCIAS_Y_VERIFICACION.md) | El panel de Referencias de la consola, `ctx.erp.Call/Get`, y **cómo verificar el API sin ir una por una** (dump COM + auditoría + lote). Estado y cómo continuar (Python/SQL). | Quien mantiene las referencias |
| 7c | [`UI_VENTANAS.md`](UI_VENTANAS.md) | **Ventanas de los botones:** modal vs. **modeless** (minimizable, conviviendo con Comercial), las dos formas (hilo de Comercial / hilo STA), reglas para que no truene, y el plan hacia Python (doble-render D9) | Quien hace UI/ventanas |
| 7d | [`REQUISICION_SOLICITUD_COMPRA.md`](REQUISICION_SOLICITUD_COMPRA.md) | Documentacion del script `REQUISICION.ctx`: uso de `ctx.erp`, SQL directo restante, evidencia de validacion y checklist | Quien mantenga botones de documentos |

## Desarrollar y entender el núcleo

| # | Documento | Qué encontrarás | Para quién |
|---|-----------|-----------------|-----------|
| 8 | [`DESARROLLO.md`](DESARROLLO.md) | Cómo modificar el código del núcleo y recompilar | Desarrollador |
| 9 | [`ESPECIFICACION.md`](ESPECIFICACION.md) | Blueprint técnico completo: contrato COM, versiones, mecanismos, trampas | Reconstruir desde cero |
| 10 | [`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md) | Diseño multi-lenguaje: host x64 fuera de proceso, Named Pipes + Protobuf, `ctx` remoto, SQL solo-proxy, seguridad | Quien trabaja el host de Python |
| 11 | [`CHANGELOG.md`](CHANGELOG.md) | Historial de versiones y cambios | Seguimiento |

## En diseño

| # | Documento | Qué encontrarás | Para quién |
|---|-----------|-----------------|-----------|
| 12 | [`RECETAS_NOCODE.md`](RECETAS_NOCODE.md) | **Motor de recetas no-code (planeado):** botones sin programar (tokens + acciones + estructuras de documento) | Quien implemente el no-code |

## Gobernanza del proyecto (open-source, en la raíz)

| Documento | Qué encontrarás |
|-----------|-----------------|
| [`../LICENSE`](../LICENSE) | GNU GPL-3.0 — licencia del proyecto |
| [`../CONTRIBUTING.md`](../CONTRIBUTING.md) | Cómo contribuir, reglas del repo, aportar recetas |
| [`../SECURITY.md`](../SECURITY.md) | Reporte privado de vulnerabilidades |
| [`../CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md) | Código de conducta (Contributor Covenant 2.1) |

## Regla de documentación

Cada cambio al código se acompaña de su actualización en la documentación **en el
mismo momento**: nueva entrada en [`CHANGELOG.md`](CHANGELOG.md), `AssemblyVersion` en
`src/ClsMain.cs`, y los `.md` afectados (ver la tabla en [`DESARROLLO.md`](DESARROLLO.md) →
"Mantener la documentación"). **Además:** toda recomendación, patrón o límite que se descubra
(no solo cambios de código) va **también** a [`MANUAL.md`](MANUAL.md), bien explicado — no basta
con dejarlo solo en el `CHANGELOG.md` o mencionarlo en el chat. Ver [`ESTADO.md`](ESTADO.md).
