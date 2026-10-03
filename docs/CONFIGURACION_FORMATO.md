# Configuración de formato

Botón **Configuración de formato** (pestaña *Soluciones LMV*, script `ConfiguracionFormato.ctx`). Es el sistema **propio de BrosLMV** para generar y enviar documentos en PDF, independiente del de Comercial:
qué formato HTML usa cada tipo de documento, dónde se guardan los PDF y cómo se llaman, y la cuenta y las plantillas de correo.

## Pestañas

- **Formatos y PDF:** a la izquierda los tipos de documento de tu empresa; a la derecha, para el que elijas: el **formato HTML** (de los que Comercial tiene registrados para ese módulo), la carpeta de destino (acepta rutas de red), el patrón del nombre con `[Etiquetas]`, si pregunta la ruta, si abre el PDF al terminar y la plantilla de correo.
  *Personalizar* le da a un tipo su propia carpeta y patrón; si no, usa los **generales**. **⬇ Instalar formatos BrosLMV** registra los 10 formatos de BrosLMV (no predeterminados) y las referencias propias que necesitan.
- **Correo:** cuenta SMTP propia (Outlook, Gmail o tu dominio; la clave va cifrada) y plantillas de asunto y cuerpo con etiquetas.
- **Diagnóstico:** revisa el motor de PDF, WebView2, las plantillas, las tablas de configuración y los botones.

## Editor de formato

Con **Editar formato…** se abre el editor integrado (antes se abría el archivo con el programa predeterminado, normalmente el navegador, que mostraba las etiquetas sin resolver):

- **Izquierda, el formato** (HTML con `[Etiquetas]`). **Centro, la vista previa** con un **documento real** de ese tipo (elige cuál en «Probar con»; salen los últimos 40). Se actualiza sola medio segundo después de dejar de escribir y **no necesita guardar** para probar.
- **Derecha, las etiquetas** (botón *Etiquetas* para ocultarlas): las **referencias propias** que creaste, las **columnas del documento** (unas 130, las mismas que usa Comercial) y todo el **Diccionario de referencia** (unas 760). Buscador incluido.
  **Clic** inserta `[Etiqueta]`; **Mayús + clic** inserta `[Format(Etiqueta,#,##0.00)]`. *Insertar \<DETAIL\>* envuelve la fila que se repite por renglón.
- **Etiquetas sin valor:** una franja amarilla dice cuáles de las etiquetas del formato salen vacías en ese documento. Es la forma más rápida de ver que una referencia nueva no regresa nada.
- **Guardar** (Ctrl+S) escribe el archivo y, la primera vez en la sesión, deja una **copia de respaldo** junto al original (`…respaldo-AAAAMMDD-HHMMSS`). **Tab** inserta dos espacios; **Esc** cierra (pregunta si hay cambios).
- Si el formato es el **predeterminado de Comercial**, avisa que al guardar también cambia cómo imprime Comercial; si no quieres tocarlo, **duplícalo** y edita la copia.

La vista previa usa **el mismo motor** que *Generar documento (PDF)* y *PDF masivo*: columnas de `vwLBSDocDocumentPrint40-DocumentID`, referencias de `engAddendaFieldRef`, `[Format()]`, imágenes incrustadas, `[QRPayload]` (en un documento timbrado, el QR de verificación del SAT) y `[DesgloseImpuestos]`.
Es una vista del HTML: el PDF respeta los saltos de página del motor, así que puede variar un poco.

## Límites

- La vista previa necesita al menos un documento de ese tipo en la empresa.
- Un documento resuelto de más de 3 MB no se muestra en la vista previa (el PDF sí se genera).
- «Quitar» un formato borra su fila en la tabla de formatos de Comercial (no el archivo); el predeterminado no se deja quitar.

## Para desarrolladores

- El motor de etiquetas está **copiado** en `ConfiguracionFormato.ctx`, `Cotizador.ctx` y `PDF_MASIVO_DOCUMENTOS.ctx`; la prueba de humo compara que den el mismo HTML. Lo ideal a futuro es una sola función del SDK.
- Acciones nuevas del puente: `editorAbrir`, `editorVista`, `editorGuardar`, `editorEtiquetas`.
- Para editarlo sin generar versión nueva: `build/laboratorio/publicar_scripts_lab.ps1 -Scripts ConfiguracionFormato` actualiza el código del botón en `BROSLMV_DESARROLLO` (con su hash).
