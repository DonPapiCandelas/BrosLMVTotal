# PDF masivo de documentos

Plantilla de fábrica **Plantillas → Documentos → PDF masivo de documentos** (`PDF_MASIVO_DOCUMENTOS.ctx`). Genera el PDF de **todos los documentos que selecciones** en la lista de Comercial, no solo del primero,
con el formato HTML de cada módulo. Antes, los botones de PDF procesaban únicamente el primer documento seleccionado.

## Cómo usarla

1. En la **Consola** abre la plantilla, guárdala como botón (nombre sugerido `PDF_MASIVO_DOCUMENTOS`) y ponla en el ribbon con *Crear botón…*.
2. En la lista de documentos de Comercial **selecciona los documentos** (de uno o de varios tipos) y pulsa el botón.
3. **Formato de cada tipo de documento.** Aparece una fila por cada tipo (Orden de compra, Factura…) con su formato HTML. Viene elegido el que tengas configurado en *Configuración de formato*; si no hay, el predeterminado.
   Un tipo de documento **sin formato HTML** se omite (y se avisa al final).
4. **Vista previa.** Cada tipo de documento tiene su botón **Vista previa**: abre el primer documento de ese tipo ya resuelto con el formato elegido; arriba puedes cambiar de **documento** (◀ ▶ o la lista) y de **formato**, y **Usar este formato** lo deja elegido. Si el formato trae etiquetas que no tienen valor en ese documento (por ejemplo una referencia nueva del Diccionario de referencia de Comercial que no regresa nada), lo avisa en una franja amarilla. Es una vista aproximada: el PDF respeta los saltos de página del motor.
5. **Dónde y cómo se llaman.** La carpeta de destino (por omisión una subcarpeta «Lote …» dentro de la carpeta de PDF de *Configuración de formato*, o en *Documentos\BrosLMV PDF*) y el patrón del nombre
   (el mismo de *Configuración de formato*; `[Modulo]` es el tipo de documento). Si dos documentos darían el mismo nombre, se les agrega « (2)», « (3)»… para que ninguno pise a otro.
6. **Qué quieres obtener:**
   - un PDF por documento, sueltos en la carpeta;
   - un archivo **ZIP** con todos los PDF;
   - un **solo PDF** con todos los documentos juntos;
   - ZIP y además el PDF unido.
   Cuando pides ZIP o unido, los PDF sueltos se quitan **solo si** lo que pediste se creó bien.
7. **Generar.** Una ventana muestra el avance y tiene **Cancelar**. Al terminar ves un reporte: cuántos salieron, cuántos fallaron o se omitieron y **por qué**, y puedes abrir la carpeta.

## Qué garantiza

- **Un documento con problemas no detiene a los demás.** Si el formato de uno falla (archivo faltante, HTML dañado, tiempo agotado), ese queda marcado con su motivo y el lote sigue.
- **Rápido:** un solo motor de PDF para todo el lote. Arrancarlo es lo caro; con un proceso por documento se pagaba ese arranque (1 a 2 segundos) en cada uno.
- **Solo lee Comercial:** no cambia ningún documento. Los archivos temporales se borran al terminar.
- **Mismas etiquetas, formatos y configuración** que *Generar documento (PDF)* y *Configuración de formato*: nada va escrito a mano por empresa. Las **referencias que tú crees en el Diccionario de referencia de Comercial** funcionan en cuanto existen (se leen en cada ejecución).
- **Un tipo de documento personalizado** en *Configuración de formato* (carpeta y patrón de nombre propios) los respeta: con PDF sueltos va a **su** carpeta (en la subcarpeta «Lote …») y con **su** patrón; con ZIP o unido todo va a la carpeta principal.
- **Documento timbrado:** el QR de la representación impresa es el del **SAT** (URL de verificación con UUID, RFC emisor y receptor, total y últimos 8 del sello); en un documento sin timbrar sigue siendo el texto simple con RFC, folio, fecha y total.
- **Más rápido:** el Diccionario de referencia se lee una sola vez por ejecución (antes, una consulta por etiqueta y por renglón: 6 documentos pasaron de unos 37 s a unos 7 s).

## Límites

- Hasta **2,000** documentos por lote.
- Los documentos se **arman en Comercial** (consultas SQL por el puente de Comercial) y el PDF lo hace el motor aparte; mientras arma los documentos, Comercial responde a ratos.
- No envía por correo en esta versión (el correo sigue siendo documento por documento, desde *Generar documento (PDF)*).
- Un formato que no es HTML (`.rpt` u otros) no se usa: solo formatos `.html`/`.htm`.

## Si algo no sale

| Síntoma | Causa probable |
|---|---|
| «El motor de PDF instalado es una versión anterior…» | `BrosLMV.HtmlToPdf.exe` no es el de esta versión. Reinstala BrosLMV. |
| «No se encontró BrosLMV.HtmlToPdf.exe» | No está instalado el motor (`C:\\BrosLMV\\htmlpdf`) o el instalador no lo copió. |
| Un tipo de documento sale «OMITIDO: su módulo no tiene un formato HTML» | Asígnale un formato en *Configuración de formato* o instala los formatos de BrosLMV. |
| Un documento sale «tiempo agotado» | Su formato tarda demasiado en armarse (p. ej. un script de la plantilla). Pruébalo solo con *Generar documento (PDF)*. |
