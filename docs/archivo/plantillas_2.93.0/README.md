# Plantillas de la versión 2.93.0 (archivadas)

Estas son **todas** las plantillas y ejemplos que traía BrosLMV hasta la 2.93.0 (`PLANTILLA_*`, `EJEMPLO_*`, `PRUEBA_*`, `REQUISICION.ctx`,
`SOLICITUD_COMPRA.ctx`). **No se distribuyen** desde la 2.94.0: usan métodos que después se descubrió que no hacían lo que decían
(`ctx.erp.RefreshGrid()` nunca refrescaba, un documento creado por script quedaba sin póliza, el importe con retenciones, filtros por empresa…) y
dejar tecnología vieja en manos de quien empieza es peligroso.

- La única plantilla vigente es **Crear documentos desde XML** (`instalador/scripts/CREAR_DOC_DESDE_XML.ctx`, documentación en
  `docs/CREAR_DOC_DESDE_XML.md`; en la Consola: clic secundario sobre la plantilla → «Ver documentación»).
- Se irán rehaciendo una por una con lo aprendido (ver `docs/ROADMAP.md`).
- Los casos de humo (`build/humo/casos/`) siguen leyendo estos archivos desde aquí.
- Al actualizar una instalación, el instalador **mueve** las plantillas anteriores que encuentre en `C:\BrosLMV\scripts\` a
  `C:\BrosLMV\scripts\_archivo\plantillas_anteriores_<fecha>\` (no las borra).
- También están en el historial de git (etiqueta `v2.93.0`).
