# BrosLMV — Pendientes reales (reescrito corto, 2026-09-27)

> El plan original (análisis del 2026-07-22, banners actualizados hasta el 2026-07-30) tenía
> 572 líneas, y **~90% de sus tareas ya estaban marcadas `✅ HECHO` o `❌ DESCARTADO`** dentro
> del propio documento — Fases 0, 1 y 2 completas al 100%, Fase 3 con el MVP de recetas
> terminado. Nada de eso se perdió: sigue íntegro en
> [`PLAN_IMPLEMENTACION_ARCHIVO.md`](PLAN_IMPLEMENTACION_ARCHIVO.md). Esto es solo lo que
> de verdad sigue abierto, más lo que salió nuevo después (que el plan original no cubría).
> Reglas de trabajo (regla de oro, no reimplementar, etc.): [`../AGENTS.md`](../AGENTS.md).

## Genuinamente pendiente del plan original

- **Importador Excel genérico con mapeo visual** (`ctx.read_excel()` ya existe; falta el
  mapeo columna→campo sin código). Sigue siendo la petición típica de cualquier ERP.
- **`BrosLMV.Runner`**: decidir si/cómo habilitar escrituras `ctx.erp` headless **sin
  supervisión** (el mecanismo ya funciona y se probó con un consumidor externo real; falta
  la decisión de producto, no la técnica). Acciones de salida (Excel/PDF/SMTP a un
  `--salida`) y receta de Task Scheduler — no construidas.
- **Reducir bus factor**: un `docs/PIPELINE_PYTHON.md` (diagrama de secuencia del pipeline
  `ctx.py` → pipe → `PythonProcess` → `HostClient`) y un glosario único de los componentes
  COM de CONTPAQi (`Document.clsMain`, `AccPoliza.clsMain`, `LBS.clsMain`, `CFDI3.clsMain`,
  `XEngineLib`) — hoy dispersos en varios documentos. Baja prioridad, continuo.

## Backlog de plataforma (ideas, sin empezar)

- Marketplace/librería comunitaria de scripts y recetas (requiere paquetes `.bros`, ya
  construidos).
- Suite CFDI: relacionar CFDI (tipo 07 anticipos), timbrado masivo con reporte.
- Asistente IA en la Consola (RAG sobre `MANUAL.md`/`XENGINE_FUNCIONES.md`), opt-in.
- Puente de solo lectura (Power BI / REST) sobre las empresas, salida natural del Runner.
- Sincronizar preferencias/recientes entre terminales vía `zzBrosPref`.

## Salido después del plan original (no estaba contemplado)

- **PDF de documentos + correo** (`htmlpdf/`), Configuración de formato, 10 formatos
  genéricos — construido y shipped (2.87.0–2.89.0). **Paginación real (Paged.js)** disponible
  desde 2.91.0 (`docs/PAGINACION_PDF.md`, `htmlpdf/formatos/paged.polyfill.min.js`) — patrón
  listo, ninguna de las 10 plantillas retocada todavía (pendiente real, plantilla por
  plantilla).
- **Motor de Asientos Contables** — construido y validado en producción (cobros/pagos
  multi-moneda). Ver `docs/MOTOR_ASIENTOS_CONTABLES.md`. Genuinamente pendiente: los
  scripts wrapper (Generar/Visualizar/Editar/Nuevo) como plantillas reusables en
  `instalador/scripts/` (hoy solo el motor de cálculo está generalizado), y el editor
  visual (diseño ya escrito).
- **Punto de Venta** (privado, prototipo) — login revisado, sigue en pruebas.
- **Canal C# ↔ Python v3.0** (`host/`) — en desarrollo, reemplaza al canal v1 cuando esté
  listo.
- **Firmar digitalmente los `.exe`** — pendiente real, no cosmético: un antivirus ya marcó
  `BrosLMV.Host.exe` como falso positivo de ransomware en un cliente.
- **SDK de CONTPAQi Contabilidad** — investigado y documentado (`docs/SDK_CONTABILIDAD.md`):
  escribir pólizas directo en Contabilidad, sin pasar por Comercial. Falta: construir el
  puente x86 como componente real (hoy solo documentado como patrón), probarlo en vivo, y
  confirmar la lista completa de enums del SDK.
- **Barrido de `C:\ProyectosLMV` — TERMINADO (2026-09-27).** Objetivo: que BrosLMV sea la
  referencia completa de cómo hablar con Comercial (XEngine/SQL/SDK). Lección de método: lo
  que vale está **en producción, en el servidor de cada cliente** (código en `zzBrosScript`,
  tablas `rt*`, datos reales para verificar); las copias locales suelen estar viejas o ser
  duplicados. Integrado: SDK y modelo de datos de Contabilidad, paginación Paged.js, dos bugs
  reales del addon (v2.92.0) y uno de las plantillas de Factura de Compra (v2.93.0), mapa de
  vínculos entre documentos, operaciones financieras, reglas del Runner para integraciones
  externas, causas reales de "Division by zero" y `MIGRAR_BOTONES_RT.md`. Descartados: los de
  Comercial Premium u otro sistema, infraestructura, pruebas y duplicados. Cada proyecto
  revisado tiene su `PENDIENTE_BACKPORT_BROSLMV.md` (local o en el servidor del cliente) con
  lo que quedó sin llevar. Pendientes que salieron del barrido y siguen abiertos: confirmar
  si `accPolizaDefinitionItem.CondicionPersonalizada` es una función nativa de renglones de
  póliza condicionales (se cruza con el Motor de Asientos), probar `Document.MostrarPoliza`/
  `SincronizarPoliza` desde `ctx.erp`, y la columna de partida Remisión→Pedido con captura
  100% nativa.
