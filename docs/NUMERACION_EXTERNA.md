# Numeración reservada por el consumidor

Estado: código candidato2.98.1; validación nativa, humo y empaquetado pendientes. No release publicado.
Verificaciones 2026-10-01: addon y Runner compilados con cero errores/advertencias; consumidor candidato compilado con el compilador de scripts, sin ejecutarlo ni abrir COM/SQL; catálogo SDK y regla de oro aprobados. Esto no confirma el comportamiento nativo. No instalado en clientes.

NuevoDocumento(..., folioPrefixOverride: "SER", folioOverride: "123") crea el encabezado usando esos valores desde el primer INSERT, sin llamar GetFolioPrefix/GetNextFolio. Ambos parámetros deben llegar juntos; serie vacía es válida; número positivo de hasta18 dígitos. Omitir ambos conserva compatibilidad.

La serie debe ser exclusiva del consumidor. Reservar atómicamente por empresa/módulo/serie; seed desde historial incluyendo eliminados/cancelados y reservas no enviadas. No consumir desde el último ID ni reutilizar números tras cancelación/fallo. El sistema puede dejar huecos auditables; no prometer continuidad sin huecos.
Si faltan series configuradas o el historial contiene identidades vacías/nulas, no elegir un consecutivo arbitrario ni heredar automáticamente una serie de otro consumidor. Acordar la serie explícita por concepto, conciliar históricos y bloquear la activación mientras falte esa configuración. El formato visual del folio interno no sustituye la identidad estructurada enviada al motor.

La comprobación previa de identidad existente no reemplaza control de concurrencia. Serializar consumidores, persistir identidad antes de enviar, registrar resultado parcial y reconciliar tras timeout. Un documento existente es motivo de revisión, NO de renumerarlo ni completarlo ciegamente. No cambiar folio después de crear y no modificar contador nativo para fingir numeración externa.

Aceptación pendiente: tres llamadas legacy; externos con serie vacía/no vacía; parámetros incompletos; cero/negativo/inyección/largo; identidad duplicada histórica; dos consumidores; timeout luego del encabezado; Save mantiene identidad y vínculos/impuestos; recuperación por identidad con SqlYaEjecutadoException. Nunca probar documentos contra producción sin autorización expresa.
