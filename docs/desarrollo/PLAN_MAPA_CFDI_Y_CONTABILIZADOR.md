# Plan: Mapa de CFDI por sistema, Descargas sin Comercial y Contabilizador en Excel

> Estado: **propuesta para aprobar** (2026-10-18). Nada de lo de abajo está implementado salvo la Fase 0.
> Parte de lo que ya existe en BrosLMV.Descargas 2.6.1 (carga al ADD de Contabilidad) y del análisis de una herramienta de
> contabilización en Excel de referencia (`Entrenamiento\contabilidad\ANALISIS_CONTABILIZADOR_EXCEL_v812.md`, fuera de git).

## 1. El problema, con números reales

Hoy, para una misma empresa de prueba (`BPL170810CL4`), cada lugar responde distinto y nadie sabe cuál es «la verdad»:

| Lugar | Cuántos CFDI | Cómo lo sabemos hoy |
|---|---|---|
| SAT (Metadata) | 12,970 (8,793 emitidos + 4,177 recibidos; 109 cancelados) | `CfdiMetadata`; ya hay «Conciliación mensual» |
| Descargas (XML en disco) | 15,737 (10,820 + 4,917) | `CfdiRecibido`. **Más que Metadata**: hay 2,767 XML que Metadata no reporta |
| Comercial Pro (staging) | 15,737 filas en `docDocumentCFDiSAT`, **1** con documento | solo una *bandera* propia (`FechaSincronizadoComercial`) |
| ADD de Contabilidad | 167 (I 75 · E 3 · N 56 · P 33) | solo una bandera (`FechaCargadoContabilidad`) |
| Contabilizados (pólizas) | no se sabe | — |

Las **banderas** solo dicen «yo lo mandé alguna vez», no «está ahí ahora». La carga al ADD ya nos enseñó que el servicio puede decir
«Carga Completa» y no guardar nada: **la verdad se lee del sistema, no se supone**.

## 2. Principios

1. **Descargas es el centro; los sistemas son conectores opcionales.** Comercial Pro, Contabilidad (ADD), Comercial Premium… son «destinos». Ninguno es obligatorio. Un cliente puede tener solo Contabilidad, o solo Comercial Premium, o ninguno (solo baja del SAT).
2. **Verdad por lectura.** Un *verificador* de solo lectura por sistema devuelve qué UUID hay ahí y en qué estado. Las acciones (cargar, importar, contabilizar) se **confirman leyendo después**.
3. **Idempotente y reversible**: repetir una acción no duplica; todo deja bitácora por UUID; cada fase documenta «cómo volver atrás».
4. **Local, sin telemetría** (regla vigente de Descargas).
5. **Excel es un documento, no un motor**: archivos `.xlsx` sin macros generados por BrosLMV; la lógica vive en C#.
6. Pruebas: bases desechables + conectores simulados; pruebas en vivo solo en el laboratorio (`BROSLMV_DESARROLLO` / `ctBROSLMV_DESARROLLO`) y en la empresa de prueba autorizada (ADD).

## 3. Fase 1 — Mapa de CFDI por sistema  *(lo inmediato)*

**Qué ve el usuario**: una página nueva **«Mapa de CFDI»** (junto a Cobertura y Conciliación):

- Tarjetas arriba: *SAT · Descargados · Comercial · ADD · Contabilizados · Faltan*.
- Tabla **mes × tipo (Recibidos/Emitidos)** con columnas `SAT | Descargados | Comercial | ADD | Contabilizado | Falta en…`. Una celda con faltantes se pinta en ámbar; clic = lista de UUID (emisor/receptor, fecha, total, estatus).
- Filtros: vigentes/cancelados, tipo de comprobante (I/E/P/N/T), ocultar nómina, rango de fechas.
- Acciones: **Verificar ahora** (con progreso y «verificado hace X»), **Cargar faltantes al ADD**, **Importar faltantes a Comercial**, **Exportar a Excel**.
- En la lista de CFDI: una columna de «chips» por sistema (✓ ADD · ✓ Comercial · póliza 3-0012).

**Cómo**:
- Tabla `CfdiEstadoSistema (UUID, Sistema, Estado, Detalle, FechaVerificado)`; `Sistema` ∈ `SAT, Disco, ComercialPro, ADD, Contabilizado, …`. Los verificadores la llenan; la UI solo lee.
- Verificadores (todos de solo lectura, en lote):
  - **SAT**: `CfdiMetadata` (ya existe).
  - **Disco**: ruta existe (y tamaño); reparar rutas rotas.
  - **Comercial Pro**: `SELECT UUID, Status, DocumentID FROM docDocumentCFDiSAT` → *staged / vinculado a documento / cancelado*.
  - **ADD**: `document_<GuidDSL>_metadata.dbo.Comprobante` (UUID, tipo) + `DocumentStatus` (errores).
  - **Contabilizado**: `ctXXX.dbo.AsocCFDIs` (UUID ↔ póliza/movimiento) y `Doc_App` del ADD (tipo, ejercicio, periodo, número).
- Interfaz común `IDestinoCfdi` (`ObtenerEstadoAsync`, `CargarAsync`, capacidades) para que Fase 2 agregue más sistemas sin tocar la UI.
- Casos a resolver: cancelados (SAT vs sistemas), nómina oculta, mayúsculas del UUID, ADD de otro RFC, XML que está en el sistema pero no en disco, y **por qué hay más XML en disco que Metadata** (Metadata incompleta; sigue pendiente de confirmar).
- **Aceptación**: los totales de la empresa de prueba coinciden con las consultas directas de la tabla de arriba; «Cargar faltantes al ADD» lleva «Falta» a 0 y se confirma releyendo.
- Esfuerzo: medio (≈ 2–3 sesiones). Sin cambios de comportamiento en lo existente.

## 4. Fase 2 — Descargas sin Comercial Pro (y Comercial Premium)

- **Perfil por empresa**: casillas «Comercial Pro · Contabilidad · Comercial Premium · Solo descargar». Cada uno activa su conector y sus páginas; lo que no se usa **no aparece** (hoy «Vínculos con Comercial», «Crear documentos» y los avisos de Comercial asumen Comercial Pro).
- Mover la integración de Comercial (hoy columnas sueltas en `Empresa`) a una tabla genérica `EmpresaDestino (RFC, Tipo, ConfigCifrada, Activo)` con migración que conserva lo existente.
- Alta de empresa guiada: se elige qué sistemas usa y se configura cada uno ahí mismo (ya está hecho para Contabilidad en 2.6.1).
- **Comercial Premium**: *no hay base de muestra en el laboratorio*; es otra arquitectura (SDK `MGW_SDK`, ya descartado como referencia para Pro). Antes de prometer nada: **investigación** con una empresa de muestra (dónde guarda UUID/XML, si usa el ADD). Mientras tanto, la carga al **ADD** ya sirve a quien lo comparte con Contabilidad.
- Documentar «qué incluye cada perfil» y probar la instalación en una máquina sin Comercial.
- Esfuerzo: medio; la investigación de Premium depende de que haya una base de muestra.

## 5. Fase 3 — Contabilizador en Excel

### 5.1 Lo que se aprendió de la referencia
Flujo: *XML (carpeta o ADD) → listado → padrón proveedor/cliente→cuenta → plantilla de asiento → vista previa editable con validaciones → registrar con el SDK → asociar CFDI a póliza y movimiento*. Detalle y debilidades en el análisis de `Entrenamiento\contabilidad\`.

Hallazgos que cambian el diseño:
- **El SDK de Contabilidad funciona desde 64 bits** (COM fuera de proceso): no hace falta el puente x86 del prototipo.
- **Contabilizado se puede leer** (`AsocCFDIs`, `Doc_App`): evita contabilizar dos veces, cosa que la referencia no revisa.
- Asociar CFDI↔póliza **requiere que el CFDI esté en el ADD**: por eso Fase 1 va primero y la carga al ADD es prerrequisito.

### 5.2 Diseño
1. **Servicio `ContabilidadSdk`** (en Descargas, COM directo): sesión, catálogo de cuentas (con *afectable*), tipos de póliza, diarios, segmentos de negocio, ejercicios abiertos, proveedores/clientes; crear póliza; asociar CFDI. Probado primero en `ctBROSLMV_DESARROLLO`: 1 póliza + asociación + lectura de vuelta.
2. **Motor de reglas de asiento** propio, guardado en SQL (JSON) por empresa — no hojas de Excel:
   - Cabecera: frecuencia (por XML · diaria · diaria por tercero · por periodo), fecha (del XML / de contabilización), tipo de póliza, concepto.
   - Líneas: cuenta (del tercero, del gasto, IVA acreditable/trasladado, retenciones…, **por moneda**), cargo/abono, **importe base** (neto − desc., por tasa, IVA, IEPS, retenciones ISR/IVA, ISH, TUA, total, «diferencia de cuadre»), referencia, concepto, UUID.
   - Mejoras sobre la referencia: IVA **por tasa real del XML** (16 %, 8 %, 0 %, exento) y no deducido por división; **cuenta de gasto por concepto** (clave SAT/ reglas), egresos con signo, REP con IVA al pago, anticipos/relacionados, multi-moneda con tipo de cambio; nómina después.
   - Reutilizar ideas del *Motor de asientos* de BrosLMV para Comercial (fórmula de cuenta, condiciones, fuentes de importe) y evaluar compartir el núcleo.
3. **Catálogo tercero → cuenta**: padrón por RFC, sugerencias desde el histórico (pólizas y ADD), alta de cuentas nuevas con cuenta patrón (SDK: proveedor/cliente).
4. **El Excel** (`.xlsx` con ClosedXML, sin macros), botón **«Contabilizador»** en Descargas:
   - Eliges periodo, tipo y regla → solo entran CFDI **no contabilizados** (según el mapa).
   - Hojas: *Resumen* · *CFDI* · *Pólizas* (vista previa editable: cuenta, importes, concepto, diario, segmento; listas desplegables desde el catálogo; filas con error en rojo; cuadre por póliza) · *Terceros* (RFC→cuenta, editable) · *Catálogo* · *Bitácora*.
   - Editas en Excel, guardas y pulsas **«Contabilizar»** en Descargas: se **relee** el libro, se **revalida contra Contabilidad** (cuenta existe y es afectable, tipo/diario/segmento, ejercicio abierto, póliza no repetida, CFDI no contabilizado), se crean las pólizas, se asocian los CFDI a póliza y movimiento y se **escribe el resultado** (número de póliza) en el libro, en la bitácora y en el mapa.
   - «Simular» (solo vista previa) siempre disponible.
5. **Seguridad y reversa**: nunca duplica (verifica antes y confirma después); bitácora por UUID/póliza; averiguar si el SDK permite **eliminar** pólizas (para «deshacer») y documentarlo; respaldo recomendado antes del primer uso.

### 5.3 Fases internas (cada una entregable)
| | Entrega | Prueba |
|---|---|---|
| 3.1 | `ContabilidadSdk` + catálogos + 1 póliza de prueba con CFDI asociado | `ctBROSLMV_DESARROLLO` |
| 3.2 | Motor de reglas + vista previa (sin escribir) | compras y ventas de la empresa de prueba contra pólizas hechas a mano |
| 3.3 | Excel de ida (generar) y vuelta (leer y validar) | libros editados a propósito con errores |
| 3.4 | Registrar + asociar + bitácora + mapa | lote pequeño → lote grande |
| 3.5 | REP, egresos, multi-moneda, nómina | casos reales por tipo |

## 6. Fase 4 — Pólizas sin Excel (después)
Contabilizar **automático** lo que las reglas cubren sin ambigüedad, justo después de la carga al ADD, y dejar a revisión (en Excel) lo dudoso. Reportes de lo contabilizado y de lo que falta.

## 7. Riesgos y preguntas abiertas
- **Comercial Premium**: ¿hay una base de muestra o instalación para investigar? Sin eso Fase 2 se queda en «perfil + ADD».
- **ADD lento**: ~50 XML por minuto; las cargas grandes deben ser incrementales y en segundo plano (ya lo son).
- **Nómina emitida rechazada** por el ADD (UUID `0A1A3D7B…`): sin motivo en el log; se aclara con la carga manual de ejemplos que ofreciste.
- **Metadata del SAT** incompleta (explica diferencias SAT vs disco): pendiente histórico.
- **Versiones de Contabilidad**: probado en 19.3.1; el servidor de aplicaciones y el SDK deben estar en el equipo.
- **Propiedad intelectual**: la herramienta de referencia se usa solo como estudio funcional; **no se copia código ni se nombra** en `docs\`.

## 8. Orden propuesto
1. **Fase 1** (mapa) → verificable en la empresa de prueba en días, no semanas.
2. **Fase 2** en paralelo cuando haya base de Premium; el perfil por empresa puede ir ya.
3. **Fase 3.1 → 3.5**.
4. **Fase 4**.

Versionado: cada fase = versión de Descargas (2.7.0, 2.8.0, …) con changelog, docs y commit explicativo; el addon de Comercial no cambia.
