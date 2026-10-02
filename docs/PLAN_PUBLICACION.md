# Plan hacia la publicación — plantillas para todos

> Creado 2026-10-02. Es un plan, no una promesa de fechas: cada fase dice **qué se entrega, cómo se sabe que está
> terminado y qué hay que verificar primero en el laboratorio**. Complementa a [`ROADMAP.md`](ROADMAP.md) (que sigue
> siendo el listado general) y respeta [`AGENTS.md`](../AGENTS.md): regla de oro (CHANGELOG + versión + notas + `.md`
> en el mismo commit), sin nombres de clientes ni datos privados, nada escrito a mano que dependa de una empresa.

## 1. Qué significa «sacarlo al público de verdad»

Hoy BrosLMV funciona muy bien **para quien lo conoce**. Para que un distribuidor o un contador lo instale solo, hacen falta tres cosas:

1. **Que traiga resuelto lo que todos piden** (trazabilidad, PDF masivo, centros de costo, reportes de saldos, ejemplos de cada documento), sin tener que mandar a hacer un script a medida.
2. **Que esas plantillas sean confiables en cualquier empresa**, no solo en la del autor.
3. **Que el paquete inspire confianza**: instalador firmado, documentación por plantilla, una forma de actualizar sin perder lo personalizado.

## 2. Estándar de «plantilla para todos» (la regla que decide si algo entra o no)

Una plantilla entra a la fábrica solo si cumple **todo** esto. Es la lista que ya aprendimos con el importador de XML, hecha explícita:

| # | Criterio | Cómo se comprueba |
|---|---|---|
| 1 | **Nada escrito a mano por empresa**: ni `ModuleID`, ni almacenes, ni series, ni formas de pago, ni nombres. Todo sale del catálogo de la empresa o se elige en una ventana | Se corre en dos empresas distintas del laboratorio sin tocar el código |
| 2 | **Módulos por naturaleza, no por número**: clasifica con `engModule.ModuleIDBase` y `engModuleParameter` (`XMLRecibido`, `StockAffectation`, `TableName`…), así funciona con los **clones** de módulo (series, sucursales) | Prueba con un módulo clonado |
| 3 | **Empresa activa**: filtra siempre por `OwnedBusinessEntityID` | Revisión de cada consulta |
| 4 | **Solo lectura por defecto**; lo que escribe pide confirmación, es **idempotente** y avisa antes de tocar muchos documentos | Se ejecuta dos veces: no duplica ni daña |
| 5 | **Usa las vías nativas** (`NuevoDocumento`, `Save`, póliza nativa, `RefreshGrid` al final) antes que SQL directo, conforme a la escala de 13 niveles del manual | Captura nativa contra réplica (BEFORE/AFTER) en `BROSLMV_DESARROLLO` |
| 6 | **Configurable sin editar código**: opciones en ventana o en las preferencias de la empresa (`zzBrosPref`) | Un usuario sin programar la adapta |
| 7 | **Falla con mensajes claros** y deja bitácora; nunca a medias en silencio | Pruebas de error inducido |
| 8 | **Documentada**: una ficha `.md` (qué hace, qué toca, qué no, cómo deshacer) + entrada en el catálogo del SDK si agrega funciones | `build/verificar_catalogo_sdk.ps1` y regla de oro |
| 9 | **Probada**: una prueba de humo automatizada en el laboratorio por plantilla (`build/probar_humo.ps1`) | Pasa antes de generar el instalador |
| 10 | **Se actualiza sin pisar lo del usuario**: la fábrica refresca la plantilla vigente; la copia personalizada se conserva | Probar actualizar sobre una plantilla editada |

## 3. Lo que ya existe y se reutiliza

| Pieza | Dónde | Sirve para |
|---|---|---|
| Matriz de vínculos entre documentos (genealogía) | `MANUAL.md` §10.5 | Base de la trazabilidad |
| Saldos a fecha de corte (sin usar la «foto del presente») | `MANUAL.md` §10.5 | CxC / CxP / estados de cuenta |
| Generación de PDF desde HTML, 10 formatos, correo | `htmlpdf/` (`GenerarPDF.ctx`, `GenerarDocumentoPDF.ctx`, `ConfiguracionFormato.ctx`) | PDF masivo |
| `CREAR_DOC_DESDE_XML.ctx` (la plantilla modelo) | `instalador/scripts/` | Patrón de ventana + validación + lote |
| Estructuras de documento por módulo | `src/EstructurasDocumento.cs` | Crear documentos |
| Asistente de botones, catálogo de íconos, respaldo `.bros` | Consola | Instalar y compartir plantillas |
| Catálogo único del SDK (170 funciones) + manual | `src/assets/sdk_catalogo.json` | Documentar cada función nueva |
| Laboratorio de pruebas con capturas nativas | `BROSLMV_DESARROLLO` | Verificar antes de publicar |

**Dos hechos medidos hoy** que condicionan el plan: (a) los dos botones de PDF solo procesan el **primer** documento seleccionado (`ids[0]`); (b) las plantillas «Ejemplo Premium» de compras (OC → Recepción / Factura) quedaron fuera de la fábrica al dejar una sola plantilla en la 2.94.0, así que hay que **recuperarlas y generalizarlas**, no empezar de cero.

## 4. Fases

Tamaño: **S** ≈ días, **M** ≈ 1–2 semanas, **L** ≈ más. Son estimaciones de esfuerzo, no compromisos.

### Fase 0 — Cerrar lo pendiente y fijar el estándar (S)
- Cerrar la rama de Descargas: push, PR, mezclar, release del instalador de Descargas (hoy está **sin push**, v2.4.0 probándose).
- Revisar y comitear/descartar el trabajo suelto del árbol (runner, `src/`, documentos de seguridad del Runner).
- Publicar este plan, el estándar del §2 y la **galería de plantillas** (una carpeta por plantilla con su ficha).
- **Listo cuando:** `main` limpio, estándar aprobado, lista de plantillas con dueño y orden.

### Fase 1 — Trazabilidad por defecto (M) · prioridad pedida
**Qué es:** la genealogía completa de cualquier documento, usando de verdad `SourceDocumentID` y, aunque el nativo no lo use, también `DestinationDocumentID` y los vínculos por partida (`SourceDocumentItemID` / `DeliverDocumentItemID`), de modo que **si alguien los usa, funcionen**.
- **1a. Visor «Trazabilidad del documento»** (botón sobre el documento seleccionado): árbol origen → destinos en ambos sentidos (búsqueda en anchura por las tres columnas, filtrando `DeletedOn`/`CancelledOn` en cada salto, clasificando por `ModuleIDBase`), con: cantidades surtidas/pendientes por partida, pagos aplicados (`docDocumentPayment`), CFDI (`docDocumentCFD`/`docDocumentCFDiSAT`) y póliza. Exporta a Excel/PDF.
- **1b. Escritura consistente**: una función del SDK (`ctx.erp.Vincular(origen, destino, partidas)`) que deja el vínculo **de los dos lados** (`Source…` en el nuevo, `Destination…` en el origen) y respeta la columna correcta por tipo (Recepción usa `DeliverDocumentItemID`; el resto `SourceDocumentItemID`). Todas las plantillas de creación la usan.
- **1c. «Completar vínculos faltantes»**: recorre documentos existentes y rellena `DestinationDocumentID` donde ya hay `Source…`. Siempre con vista previa y deshacer.
- **Verificar primero en el laboratorio (antes de escribir una línea):** que llenar `DestinationDocumentID` / `DestinationDocumentItemID` **no altera** cancelaciones, pendientes, vistas nativas ni impresión. Si altera algo, la escritura del destino queda **opcional** y el visor igual funciona (solo lee).
- **Listo cuando:** el visor reconstruye la cadena Cotización → Pedido → Remisión → Factura → Cobro y Orden de Compra → Recepción → Factura → Pago en **dos empresas**, incluidos módulos clonados, y la escritura es idempotente.

### Fase 2 — PDF masivo (M)
Hoy: un documento por clic. Meta: **N documentos seleccionados**.
- Procesar toda la selección (`ctx.GetSelectedIds()`), con **barra de progreso, cancelar y reporte de errores** (cuáles fallaron y por qué).
- Salida: una carpeta con el **patrón de nombre configurable** que ya existe (`[Etiquetas]`), opción de **un solo PDF unido**, opción **ZIP**, y **envío por correo** por documento con la configuración de correo existente.
- Reutilizar **una sola instancia del motor** para no pagar el arranque por documento; límite de paralelismo para no agotar memoria (Comercial es de 32 bits).
- Aplicar la **paginación real (Paged.js)** a los formatos, empezando por Orden de Compra (ROADMAP 1.5), para que el PDF masivo salga con «Página X de Y» y encabezado repetido.
- **Listo cuando:** 200 documentos seleccionados generan 200 PDF (o uno unido) sin bloquear Comercial, con reporte de fallos, y el patrón de nombre/correo respeta la configuración.

### Fase 3 — Centros de costo masivos (S–M)
La petición tiene dos lecturas; propongo **ambas**, en orden:
- **3a. Alta masiva de centros de costo** (catálogo `orgCostCenter`) pegando una lista o desde Excel/CSV, con validación de duplicados y vista previa.
- **3b. Asignación masiva a documentos/partidas** por selección o por filtro (fechas, módulo, proveedor/cliente, categoría de producto).
- **Verificar primero:** cómo asigna el nativo el centro de costo — encabezado (`CostCenterID`) frente a la tabla por partida (`docDocumentItemCostCenter`) — con una captura BEFORE/AFTER; la plantilla debe escribir **exactamente lo mismo** que la pantalla nativa.
- **Listo cuando:** la asignación masiva produce el mismo resultado que hacerlo a mano en pantalla y los reportes nativos por centro de costo (`vwLBSDocCustomerInvoicedPerCostCenterList`) lo reflejan.

### Fase 4 — Reportes de saldos (M)
**CxC, CxP, estado de cuenta de clientes y de proveedores**, como plantillas HTML (patrón de `DASHBOARDS_HTML.md`) exportables a Excel y PDF:
- Saldos **a fecha de corte** reconstruidos desde `docDocument.Total` y `docDocumentPayment` (no desde `Balance`, que es una foto del presente); CxP **incluye Gastos** (módulo 242 por naturaleza, no por número).
- **Antigüedad de saldos**, multimoneda, filtros por cliente/proveedor/vendedor/fecha, y detalle de cada documento con su trazabilidad (Fase 1).
- Estado de cuenta imprimible con el formato de la empresa y envío por correo.
- **Listo cuando:** los totales coinciden con los reportes nativos de Comercial al mismo corte en dos empresas.

### Fase 5 — Plantillas «cómo crear cada documento» (L), en dos olas
**Diseño para no escribir 32 copias a mano:** un **descriptor por documento** (módulo por naturaleza, campos requeridos, pasos nativos) y dos motores delgados (C# y Python) que lo leen; y **dos «cascarones» de interfaz** reutilizables, **WinForms** y **WebView2**. Cada documento queda disponible en las 2 lenguas × 2 interfaces generando desde el mismo descriptor, y cada ejemplo termina con `ctx.erp.AbrirDocumento(...)`.
- **Ola 1 — compras y ventas (recetas ya demostradas):** Factura de cliente, Factura de compra, Pedido, Orden de compra, Recepción, Remisión. Recuperar los «Ejemplo Premium» archivados y generalizarlos. Todos usan `Vincular` (Fase 1).
- **Ola 2 — pago al proveedor y cobro al cliente:** dependen del **wrapper del SDK de cobros/pagos** (ROADMAP 1.1), porque hoy las integraciones terminan insertando `docFinancialOperation` a mano (sin aplicar, sin saldos ni póliza). **No se publican hasta tener la vía nativa**; hasta entonces se documenta el límite.
- **Listo cuando:** cada documento de la lista se crea desde las cuatro variantes, con inventario/saldos/póliza correctos según la naturaleza del módulo y verificado contra una captura nativa.

### Fase 6 — Lanzamiento (M)
- **Firmar los `.exe`** (ROADMAP 0.3): un antivirus ya marcó un componente como ransomware; sin firma, el público lo va a ver.
- **Galería de plantillas dentro de la Consola** (ver, instalar, actualizar sin pisar copias personalizadas) y, después, descarga desde GitHub.
- **Documentación pública:** ficha por plantilla, capturas/GIF, guía de primer uso, preguntas frecuentes, política de soporte. (El sitio web se pospuso a propósito; esto no depende de él.)
- **Pruebas de humo por plantilla** en CI y release con notas versionadas.
- **Listo cuando:** una persona ajena instala, activa cinco plantillas y las usa sin ayuda.

## 5. Qué más se me ocurre (ordenado por valor para un lanzamiento)

**Entran en el lanzamiento (alto valor, esfuerzo contenido):**
1. **Cancelación segura:** antes de cancelar un documento, usa la genealogía (Fase 1) para avisar de recepciones, facturas, pagos o pólizas dependientes.
2. **Pendientes por surtir / recibir / facturar:** el tablero que todos terminan pidiendo, sobre las mismas relaciones.
3. **Importador desde Excel con mapeo visual** para catálogos (productos, clientes, proveedores, precios) — ROADMAP 2.2; es lo primero que un cliente nuevo necesita.
4. **Actualización masiva de precios y costos** con vista previa y deshacer (mismo patrón que centros de costo).
5. **Conciliación de CFDI contra documentos** (la que ya hicimos en Descargas) integrada como plantilla: lee el XML del CFDI y sugiere el documento.
6. **Reporte de ventas y utilidad** por producto, cliente y vendedor, con margen real.
7. **Existencias valuadas y kardex** a fecha de corte.
8. **Diagnóstico inicial** (`DIAGNOSTICO.csx` ya existe): que revise el equipo, la empresa y los módulos y diga qué plantillas aplican y cuáles no.

**Después del lanzamiento:** timbrado masivo con reporte, cierre de periodo (lista de verificación), pólizas faltantes, bitácora de cambios por usuario, puente de solo lectura a Power BI.

**Transversal y no negociable:** el principio de herramienta local (nada de telemetría ni conexión a clientes) aplica a todo lo de arriba; los avisos y reportes son locales.

## 6. Orden recomendado y por qué

`Fase 0 → 1 → 2 → 3 → 4 → 5 (ola 1) → 6`, con la ola 2 de la Fase 5 en cuanto exista el wrapper de pagos.

- La **trazabilidad va primero** porque la usan casi todas las demás (cancelación segura, pendientes, estados de cuenta, plantillas de creación con `Vincular`).
- **PDF masivo y centros de costo** son rápidos y de valor inmediato, así que dan resultados visibles pronto.
- **Los reportes** dependen del método de saldos ya documentado, por eso van antes de las plantillas de creación.
- **Pagos y cobros** se separan porque dependen de una pieza que aún no existe.

## 7. Riesgos

| Riesgo | Mitigación |
|---|---|
| Escribir `DestinationDocumentID` altera algo nativo que no vemos | Prueba en laboratorio primero; si hay efectos, queda opcional |
| Plantillas que «funcionan en mi empresa» y fallan en otra | Criterios 1–3 del estándar y prueba en dos empresas |
| Pagos/cobros sin vía nativa se publican a medias | Se bloquean hasta el wrapper (ROADMAP 1.1) |
| Comercial (32 bits) se queda sin memoria con lotes grandes (PDF masivo) | Una instancia del motor, paralelismo limitado, reinicio sugerido, lotes con progreso |
| Muchas plantillas, mucho mantenimiento (un solo autor) | Descriptor + motores delgados en lugar de copias; ficha y prueba de humo por plantilla |
| Antivirus bloquea el instalador | Firma de código antes de publicar |

## 8. Decisiones que necesito de ti

1. **Centros de costo:** ¿priorizo el **alta masiva del catálogo**, la **asignación masiva a documentos**, o ambos (propuesto: ambos, en ese orden)?
2. **`DestinationDocumentID`:** ¿se escribe **por defecto** si el laboratorio demuestra que es inocuo, o **solo opcional**?
3. **Pagos y cobros:** ¿esperamos el wrapper nativo (propuesto) o los publicamos con una advertencia de límite?
4. **Orden:** ¿confirmas `Trazabilidad → PDF masivo → Centros de costo → Reportes → Plantillas de creación`?
