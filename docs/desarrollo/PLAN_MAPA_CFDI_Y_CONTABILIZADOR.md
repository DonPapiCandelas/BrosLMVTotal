# Plan: Mapa de CFDI por sistema, Descargas sin Comercial y Contabilizador en Excel

> Estado (2026-10-18): plan **aprobado**. **Fase 1 hecha en Descargas 2.7.0** (mapa, historial de pólizas, documento de Comercial con «Ver documento»; lo que quedó pendiente de la Fase 1 está al final de esta nota). **Fase 1B (expediente en Excel) hecha en Descargas 2.8.0. Fase 2 (perfil por empresa, Descargas con o sin CONTPAQi) hecha en 2.9.0**, salvo la investigación de Comercial Premium (falta una base de muestra). Siguiente: Fase 3 (Contabilizador).
>
> Pendiente de la Fase 1: detalle del CFDI con «Abrir en Comercial» (hoy solo vista previa); columnas «chips» por sistema en la lista de CFDI; detectar XML que están en Comercial o el ADD pero no en disco; aclarar el rechazo del ADD a un CFDI de nómina emitido.
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

## 2b. Visión de producto (2026-10-18)

La herramienta vale en **tres capas**, y cada una debe servir sola:

1. **Descargar y entender los XML (sin ningún sistema CONTPAQi).** Es útil por sí misma para preparar una **devolución de impuestos** o responder una **aclaración al SAT**: un expediente en Excel con todos los datos del CFDI, si tiene o no complemento de pago, cuánto se ha pagado, cancelaciones, observaciones.
2. **Interfaz con los sistemas CONTPAQi, cuando existan.** Ahí está todo el potencial:
   - **Comercial Pro**: crear el documento desde el XML (ya existe), **saber si el XML ya tiene documento** y un botón **«Ver documento»**. Después, lo mismo para **Comercial Premium**.
   - **Contabilidad**: saber **en qué pólizas está** cada CFDI. Un CFDI puede estar en varias (provisión en diario, el egreso o ingreso del pago, una cancelación, pagos parciales), así que se guarda un **historial de pólizas**, no un sí/no. Meta: ser **más útil que el propio ADD** (búsqueda, exportaciones, datos relevantes).
3. **Generador de pólizas en Excel**, con asientos muy completos y flexibilidad total antes de exportar (prorrateos, monedas, cuentas, segmentos), que lee impuestos y retenciones locales y complementos (combustibles, etc.) y genera pólizas de **egreso/ingreso a partir de un complemento de pago o de un CFDI** pidiendo la cuenta de bancos.

Datos que sustentan esto (empresa real consultada solo con conteos): **18 % de los CFDI con pólizas están en 2 o más** (1,254 de 7,055; hasta 8 pólizas por CFDI), repartidos en 17 tipos de póliza; y en los XML de la empresa de prueba (muestra de 4,000) los complementos son: nómina 67 %, pagos 11 %, comercio exterior 1.5 %, carta porte 1.1 %, divisas 0.6 %, impuestos locales 0.5 %, estado de cuenta de combustible 0.07 %.

## 3. Fase 1 — Mapa de CFDI por sistema  *(lo inmediato)*

**Qué ve el usuario**: una página nueva **«Mapa de CFDI»** (junto a Cobertura y Conciliación):

- Tarjetas arriba: *SAT · Descargados · Comercial · ADD · Contabilizados · Faltan*.
- Tabla **mes × tipo (Recibidos/Emitidos)** con columnas `SAT | Descargados | Comercial | ADD | Contabilizado | Falta en…`. Una celda con faltantes se pinta en ámbar; clic = lista de UUID (emisor/receptor, fecha, total, estatus).
- Filtros: vigentes/cancelados, tipo de comprobante (I/E/P/N/T), ocultar nómina, rango de fechas.
- Acciones: **Verificar ahora** (con progreso y «verificado hace X»), **Cargar faltantes al ADD**, **Importar faltantes a Comercial**, **Exportar a Excel**.
- En la lista de CFDI: una columna de «chips» por sistema (✓ ADD · ✓ Comercial · pólizas 3).
- **Detalle de un CFDI** (ventana sin bloqueo): todos sus datos, XML, y por sistema: *Comercial* → documento(s) generado(s) con **«Ver documento»**; *Contabilidad* → **historial de pólizas** (tipo, folio, fecha, concepto, importes, rol) con «Ver póliza» y el estado del ADD.
  - «Ver documento»: vista previa de solo lectura leída de SQL (funciona con Comercial cerrado) y, si Comercial está abierto, **«Abrir en Comercial»** con `AbrirDocumento` del addon. *(Interpreto que el botón no debe abrir un modal que bloquee; confirmar.)*

**Cómo**:
- Tabla `CfdiEstadoSistema (UUID, Sistema, Estado, Detalle, FechaVerificado)`; `Sistema` ∈ `SAT, Disco, ComercialPro, ADD, Contabilizado, …`. Los verificadores la llenan; la UI solo lee.
- Verificadores (todos de solo lectura, en lote):
  - **SAT**: `CfdiMetadata` (ya existe).
  - **Disco**: ruta existe (y tamaño); reparar rutas rotas.
  - **Comercial Pro**: `SELECT UUID, Status, DocumentID FROM docDocumentCFDiSAT` → *staged / vinculado a documento / cancelado*.
  - **ADD**: `document_<GuidDSL>_metadata.dbo.Comprobante` (UUID, tipo) + `DocumentStatus` (errores).
  - **Historial de pólizas** (no un sí/no): `ctXXX.dbo.AsocCFDIs` (`GuidRef` + `UUID` + `AppType` = Contabilidad / Bancos) unido a `Polizas.Guid` (la asociación es a la póliza) y a `MovimientosPoliza.Guid` (a un movimiento), con `Doc_App` del ADD como respaldo. Se guarda en `CfdiPoliza (UUID, GuidPoliza, Ejercicio, Periodo, TipoPol, Folio, Fecha, Concepto, Cargos, Abonos, Rol, Origen)`. *Medido en una empresa real*: 7,942 asociaciones a pólizas y 26,796 a movimientos; 2,028 apuntan a otros objetos (Bancos), por eso `AppType`. `Rol` (provisión, pago/cobro, cancelación, ajuste, otro) se deduce del tipo de póliza y del concepto, y se puede corregir.
  - **Comercial Pro: documento generado**: `docDocumentCFDiSAT.DocumentID` → `docDocument` (módulo, folio, fecha, total, cancelado) → tabla `CfdiDocumentoComercial`. Un CFDI puede tener 0, 1 o varios documentos.
- Interfaz común `IDestinoCfdi` (`ObtenerEstadoAsync`, `CargarAsync`, capacidades) para que Fase 2 agregue más sistemas sin tocar la UI.
- Casos a resolver: cancelados (SAT vs sistemas), nómina oculta, mayúsculas del UUID, ADD de otro RFC, XML que está en el sistema pero no en disco, y **por qué hay más XML en disco que Metadata** (Metadata incompleta; sigue pendiente de confirmar).
- **Aceptación**: los totales de la empresa de prueba coinciden con las consultas directas de la tabla de arriba; «Cargar faltantes al ADD» lleva «Falta» a 0 y se confirma releyendo.
- Esfuerzo: medio (≈ 2–3 sesiones). Sin cambios de comportamiento en lo existente.

## 3b. Fase 1B — Expediente en Excel (devoluciones y aclaraciones)  *(valor inmediato, sin CONTPAQi)*

Botón **«Exportar expediente»** (por empresa, periodo y tipo): un `.xlsx` (ClosedXML, ya incluido) con:
- **CFDI**: todos los campos del comprobante, emisor/receptor, uso, método y forma de pago, moneda y TC, estatus SAT y fecha de cancelación, UUID, ruta del XML.
- **Conceptos** (clave SAT, unidad, cantidad, importe, objeto de impuesto) e **Impuestos** (traslados y retenciones por tasa, **locales**).
- **Pagos**: por cada CFDI PPD, si **tiene complemento de pago**, cuántos REP, cuánto está pagado, saldo y fechas; y por cada REP, los documentos relacionados con su parcialidad, saldo anterior/insoluto, impuestos del pago y cuentas ordenante/beneficiaria.
- **Relacionados y cancelaciones** (egresos, sustituciones, cancelados con fecha).
- **Complementos** según lo que traiga el XML: nómina, comercio exterior, carta porte, divisas, impuestos locales, estado de cuenta de combustible…
- **Resumen por periodo**: IVA trasladado y acreditable, retenciones, y **por flujo de efectivo** (solo lo efectivamente pagado, con su REP) — base típica de una devolución de IVA.
- **Observaciones**: PPD sin REP, forma de pago 99, proveedores en lista 69-B (ya lo hacen los indicadores fiscales), duplicados, cancelados con operación.

Hoy ya se guardan conceptos, pagos y relaciones; **falta leer y guardar los complementos** (se añade al lector de XML por orden de importancia: pagos e impuestos locales primero porque alimentan pólizas y devoluciones). Esfuerzo: medio; **no depende de ningún sistema**.

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

### 5.2b Lo que el Excel debe poder hacer (visión del dueño)
- **Impuestos y retenciones locales** (`implocal`, ISH, TUA…) y **complementos** (combustibles, etc.) como líneas con su propia cuenta y reglas.
- **Pólizas de egreso/ingreso desde un complemento de pago o desde un CFDI**: se pide la **cuenta de bancos** (por forma de pago o por cuenta; el REP trae cuenta ordenante/beneficiaria, que se intentará cruzar con las cuentas bancarias de Contabilidad), y se arma el asiento por cada documento relacionado con su IVA proporcional al pago (IVA pendiente → acreditable/trasladado) y sus retenciones.
- **Varias pólizas por CFDI según su rol**: provisión (diario) en la fecha del CFDI, egreso/ingreso en el pago (uno por parcialidad), cancelación (póliza de reversa), notas de crédito. El mapa muestra qué rol ya existe y cuál falta.
- **Flexibilidad total antes de exportar**: **prorrateos** (un gasto repartido por % entre cuentas y segmentos de negocio), **monedas** (importe en ME y TC), cambio de **cuentas** y **segmentos** por línea o por regla, diario. Todo editable.
- **Excel «vivo» sin macros**: el libro se genera con **fórmulas** (importes = base × %, cuadre por póliza, sumas iguales), de modo que al cambiar un porcentaje o una cuenta se recalcula solo; Descargas lee los valores finales. Hojas: *Resumen · CFDI · Pólizas · Prorrateos · Terceros · Bancos · Catálogo · Bitácora*. Se revisa y **solo entonces** se genera la póliza.

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
1. **Fase 1** (mapa + historial de pólizas + documento de Comercial) → verificable en la empresa de prueba en días, no semanas. **Fase 1B** (expediente en Excel) a continuación: aporta valor aun sin CONTPAQi y casi todos sus datos ya existen.
2. **Fase 2** en paralelo cuando haya base de Premium; el perfil por empresa puede ir ya.
3. **Fase 3.1 → 3.5**.
4. **Fase 4**.

Versionado: cada fase = versión de Descargas (2.7.0, 2.8.0, …) con changelog, docs y commit explicativo; el addon de Comercial no cambia.
