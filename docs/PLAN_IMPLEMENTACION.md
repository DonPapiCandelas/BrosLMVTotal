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
  genéricos — construido y shipped (2.87.0–2.89.0).
- **Motor de pólizas propio**: investigación completa de cómo genera Comercial sus pólizas
  y cómo generarlas/abrirlas por COM sin reimplementar nada — **sin decisión de producto**
  sobre construir el motor propio (resolvería proveedor+moneda, que el nativo no soporta).
  Ver `docs/ESTADO.md`.
- **Punto de Venta** (privado, prototipo) — login revisado, sigue en pruebas.
- **Canal C# ↔ Python v3.0** (`host/`) — en desarrollo, reemplaza al canal v1 cuando esté
  listo.
- **Firmar digitalmente los `.exe`** — pendiente real, no cosmético: un antivirus ya marcó
  `BrosLMV.Host.exe` como falso positivo de ransomware en un cliente.
