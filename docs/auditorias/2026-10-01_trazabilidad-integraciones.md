# Auditoría de trazabilidad e integraciones — 2026-10-01

Se examinó el núcleo vigente documentado como v2.98.0, sus contratos de scripting/Runner, el canal Python, generación documental y conocimiento experimental reciente.
La comparación de tres consumidores se efectuó contra sus instalaciones vivas; se omiten identidades, rutas de red y datos privados.
El mayor riesgo compartido es el éxito aparente de un documento cuando el postproceso nativo ha fallado o la ejecución se reintenta.
La seguridad del modo SoloLectura no cubre de forma uniforme los wrappers de escritura COM.
Los consumidores todavía entregan credenciales por argumentos del proceso y privilegios SQL demasiado amplios.
Se propone normalizar naturaleza de módulos, idempotencia, genealogía e impuestos sin uniformar flujos empresariales.
CxP debe comenzar como consulta conciliada, no como escritura financiera desde web.
No se cambió código del núcleo ni se ejecutaron escrituras contra empresas de clientes; no se certifica ausencia de otros defectos.

## Cobertura y límites
Lectura de AGENTS/ESTADO/ROADMAP/INDICE/AUDITORIA; contraste del contrato con Scripting.cs/Runner; lectura focalizada de host/pipe, SDK, manual de documentos/vínculos/operaciones y paginación.
Se incorporó conocimiento experimental reciente sobre módulos, alta de catálogos, permisos, XML/impuestos, generación nativa de pólizas y pagos.
Mapa del producto: addon/Consola y SDK, Runner, host/workers/protocol, htmlpdf/correo, recetas, motor contable, subproducto de descargas, build/instalación/documentación.
No se hizo revisión línea por línea exhaustiva de todo ese producto ni auditoría completa del subproducto de descargas/instaladores/prototipos. Tampoco pruebas COM, compilación/humo o restauración en esta sesión; si se necesita certificar todas las áreas, son un trabajo restante explícito, no un resultado aprobado.
No se publica material privado ni se considera un experimento antiguo fuente superior a una prueba reciente corregida. No se traspasan configuraciones de clientes al núcleo.

### A-01 — Mutadores COM fuera de la guarda SoloLectura
- **Estado:** CONFIRMADO por inspección estática; explotación no ejecutada.
- **Severidad:** Alta.
- **Área:** B (seguridad).
- **Evidencia:** src/Scripting.cs:1543 (AffectStock), :1544 (AffectStockNEW), :1555 (CancelDocument), :1557 (Save), :2074 (GuardaEscritura); buscar firmas para líneas exactas tras cambios.
- **Qué pasa:** los wrappers directos no usan la guarda que sí aplican NuevoDocumento/AgregarArticulo. SoloLectura no constituye protección uniforme frente a llamadas mutantes.
- **Propuesta:** guardas centralizadas para todos los mutadores y política de escritura forzada fuera del control del script; documentar límites de confianza. No prometer sandbox para C#/Python arbitrario con conexión/COM.
- **Cómo verificar el arreglo:** matriz de todos los mutadores con preferencia de solo lectura forzada; rechazo antes de tocar SQL/COM; flujo legítimo de escritura sigue operativo.

### A-02 — Contrato Runner admite secreto en argumentos
- **Estado:** CONFIRMADO.
- **Severidad:** Alta.
- **Área:** B.
- **Evidencia:** runner/Program.cs:78 acepta --conn; :26 y ayuda :422 documentan cadena incluyendo Password; consumidores entregan la cadena completa y registran excepciones del lanzamiento.
- **Qué pasa:** credencial accesible en argv y potencialmente en diagnósticos del proceso; cuenta amplia incrementa impacto.
- **Propuesta:** stdin/proveedor protegido de credenciales, censura de mensajes y cuenta mínima. La alternativa existente de cadena cifrada debe evaluarse con ACL, usuario de servicio y aislamiento por empresa, no adoptarse a ciegas.
- **Cómo verificar:** provocar fallo/timeout en laboratorio, inspeccionar argv/log/errores y confirmar que no aparece secreto; mantener compatibilidad de consumidores y documentar deprecación.

### A-03 — Éxito de postproceso no garantizado por wrapper void
- **Estado:** CONFIRMADO (contrato); daño en un documento concreto NO demostrado.
- **Severidad:** Alta.
- **Área:** A.
- **Evidencia:** src/Scripting.cs:1538 documenta revisar LastError; wrappers AffectStockNEW/Save; consumidores activos omiten controles inmediatos al marcar una operación done.
- **Qué pasa:** Com.Call captura errores; salir sin excepción no demuestra stock/saldo/póliza/cadena correctamente finalizados.
- **Propuesta:** fachada de integración con etapas, LastError inmediato, resultado tipado y postcondiciones. El estado incierto exige reconciliación antes de reintentar.
- **Cómo verificar:** fallos inducidos en cada etapa, documento parcial, tiempo agotado después de Save y reintento concurrente: no duplica ni declara éxito falso.

### A-04 — Naturaleza del documento y póliza requieren configuración por módulo
- **Estado:** CONFIRMADO.
- **Severidad:** Alta.
- **Área:** A.
- **Evidencia:** contratos de documentos del manual; engModule.ModuleIDBase y engModuleParameter (StockAffectation/FinancialAffectation/TableName; AccountingPoliza en Section Contabilidad). Consumidores vivos muestran distinta naturaleza para la misma familia OC/recepción.
- **Qué pasa:** el mismo ID no implica mismo comportamiento en todas las empresas. Guardar no garantiza generar póliza; generar y sincronizar son distintos.
- **Propuesta:** descriptor de módulo con herencia/clones y postproceso documentado. Accounting.clsMain.CrearPolizasDocumento tiene prueba reciente, incluir solo cuando se solicite; mantener métodos nativos antes que SQL de póliza.
- **Cómo verificar:** OC/recepción/factura/gasto/clon con stock0/±1/reservas; póliza requerida/no requerida; conciliación con captura nativa.

### A-05 — Idempotencia y doble consumidor no se resuelven por deduplicación previa
- **Estado:** CONFIRMADO como carrera posible; no se provocó duplicado.
- **Severidad:** Alta.
- **Área:** C.
- **Evidencia:** consumidores consultan fila pending/done antes de insertar; índices únicos solo en QueueID. Script de ejecución lee todas las pending sin claim atómico por fila.
- **Qué pasa:** dos envíos o consumidores simultáneos pueden crear documentos duplicados; timeout después de creación deja resultado incierto.
- **Propuesta:** contrato compartido operación/revisión/empresa, exclusión atómica, lease/claim, resultados parciales y recuperación. No disfrazar excepción como motivo para repetir toda la operación.
- **Cómo verificar:** concurrencia/doble clic/worker muerto/reinicio con una sola creación y recuperación documentada.

### A-06 — Genealogía y consulta financiera nativas
- **Estado:** CONFIRMADO (fuentes), propuesta de implementación.
- **Severidad:** Media.
- **Área:** A.
- **Evidencia:** MANUAL §10.5/10.6; docDocumentPayment, docFinancialOperation, docDocumentPaymentAgenda y columnas financieras verificadas por lectura en instalaciones.
- **Qué pasa:** OC/recepción no son CxP por sí mismas; Balance actual no es saldo histórico al corte; una operación distribuida no se puede contar completa en cada factura.
- **Propuesta:** adaptador SELECT restringido por empresa/moneda, vínculos por IDs y aplicaciones conciliadas. Calendario vencimientos separado de entregas. Payment.clsMain sigue siendo candidato para fase futura de escritura, no sustituto probado.
- **Cómo verificar:** NC/anticipo/aplicación múltiple/divisas/cancelación/reprogramación/corte; comparar contra reporte nativo y documentar límites históricos.

### A-07 — Rendimiento: optimizar frontera antes de motor
- **Estado:** CONFIRMADO estático; impacto medido PENDIENTE.
- **Severidad:** Media.
- **Área:** C.
- **Evidencia:** host/README.md describe host por ejecución y persistencia pendiente; consumidores cargan existencias/costos completos y filtran después; reverse solo detecta nuevos IDs.
- **Qué pasa:** cargas amplias y arranque de proceso pueden costar más que el cálculo. Cambiar host persistente introduce estados y aislamiento de empresas.
- **Propuesta:** medir p95/lecturas/COM/startup primero; paginación/filtrado y reverse de cambios; luego host persistente con sesión/empresa aisladas si medición lo justifica.
- **Cómo verificar:** benchmark representativo sin carga intrusiva en cliente; mismos resultados/permisos antes/después, sin mezcla de empresa en sesión reutilizada.

### A-08 — Fortalezas y funciones que no deben reimplementarse
- **Estado:** CONFIRMADO por código/documentación.
- **Severidad:** Baja (recomendación de preservación).
- **Área:** E.
- **Evidencia:** host/BrosLMV.Host/PipeServer.cs:51 ACL del usuario y :106 handshake; docs/ESTADO.md SDK catálogo único, scripts/hash y separación de instalación/publicación.
- **Qué pasa:** ya existe protección del pipe local, métodos nativos, control documental y paginación opcional; duplicar motores aumenta riesgo.
- **Propuesta:** usar API/catálogo/nativo y proteger contratos; Paged.js requiere implementación real en plantillas, no basta disponibilidad.
- **Cómo verificar:** SDK sincronizado, firmas/guardas y humo; PDF multipágina real sin partir encabezado/totales; hash del paquete desplegado además de build exitoso.

## Roadmap propuesto
- [ ] P0: cerrar A-01/A-02 y política de escritura Runner.
- [ ] P1: contrato de módulo/postcondiciones/idempotencia/genealogía compartido con integración de pruebas.
- [ ] P1: CxP SELECT/conciliación y calendario (sin pagos).
- [ ] P2: impuestos dinámicos y retención/evidencia de XML/REP, sin declarar listas propuestas como implementadas.
- [ ] P2: rendimiento medido/host persistente y PDF/Excel uniformes.
- [ ] Para cualquier cambio de src: versión+changelog+notas+SDK cuando aplique, regla de oro+humo, luego regenerar instalador/exes; no compilar ni matar aplicación cliente desde esta auditoría documental.

## Preguntas/bloqueos abiertos antes de implementar
Control documental: regla de oro valida versión2.98.0/changelog/notas pero falla escaneo por término privado en mensaje de commit preexistente74929ef. No se reescribió historia. Resolver antes de publicar; no es error de compilación ni se ejecutó humo.

1. Confirmar laboratorio autorizado: instrucciones históricas y estado experimental reciente nombran ambientes diferentes; ningún cliente es laboratorio.
2. Política de escritura headless/permisos efectivos e identidad ERP frente al usuario Windows/SQL.
3. Requisitos de contabilidad por módulo en cada consumidor y aprobación del responsable.
4. Semántica histórica de eliminación/cancelación/aplicaciones/divisas en versión de Comercial instalada.
5. Nivel de auditoría pendiente del resto del producto (subproducto de descargas, instaladores, todas las ramas UI).
6. Una versión FileVersion del Runner no corresponde necesariamente a la versión del addon; definir manifiesto de compilación/hash, no comparar números de componentes diferentes.
