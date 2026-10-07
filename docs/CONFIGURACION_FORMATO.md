# Configuración de formato

Botón **Configuración de formato** (pestaña *Soluciones LMV*, script `ConfiguracionFormato.ctx`). Es el sistema **propio de BrosLMV** para generar y enviar documentos en PDF, independiente del de Comercial:
qué formato HTML usa cada tipo de documento, dónde se guardan los PDF y cómo se llaman, y la cuenta y las plantillas de correo.

## Pestañas

- **Formatos y PDF:** a la izquierda los tipos de documento de tu empresa; a la derecha, para el que elijas: el **formato HTML** (de los que Comercial tiene registrados para ese módulo), la carpeta de destino (acepta rutas de red), el patrón del nombre con `[Etiquetas]`, si pregunta la ruta, si abre el PDF al terminar y la plantilla de correo.
  *Personalizar* le da a un tipo su propia carpeta y patrón; si no, usa los **generales**. **⬇ Instalar formatos BrosLMV** registra los 10 formatos de BrosLMV (no predeterminados) y las referencias propias que necesitan.
- **Correo:** cuenta SMTP propia (Outlook, Gmail o tu dominio; la clave va cifrada) y plantillas de asunto y cuerpo con etiquetas.
- **Diagnóstico:** revisa el motor de PDF, WebView2, las plantillas, las tablas de configuración y los botones.

## Diseñador de formatos

Con **Diseñar formato…** se abre el Diseñador como **programa aparte** (`C:\BrosLMV\disenador\BrosLMV.Disenador.exe`): una ventana propia que puedes **minimizar y mover** mientras sigues usando Comercial (Comercial no se bloquea). Habla directo con la base de la empresa; también se puede abrir sin formato y elegir uno de la lista con **Abrir…**. Es un editor **visual** para cambiar un formato sin saber HTML (y, si sabes, con el código a un clic). El formato HTML con `[Etiquetas]` sigue siendo la fuente de verdad: lo que haces en pantalla se escribe en el archivo.

**Tres modos** (arriba): **Diseño** (el documento con valores reales), **Código** (el HTML) y **Vista final** (el documento tal como lo resuelve el motor de PDF). *Probar con* elige el documento real que se usa; **Deshacer/Rehacer** (Ctrl+Z / Ctrl+Y) y **Guardar** (Ctrl+S, con respaldo del original la primera vez).

### Modo Diseño

- **El lienzo** muestra el documento con los valores del documento elegido. Cada **etiqueta es una ficha** (azul = tiene valor, amarilla = sale vacía en este documento) que no se rompe al escribir. La **fila de renglones** (`<DETAIL>`) se muestra repetida con los primeros renglones de ese documento.
- **Clic** selecciona; la barra de **migas** muestra dónde estás (`body › div.lower › div.tot › div.row`) y permite subir al contenedor. **Doble clic** en un texto escribe sobre él; **doble clic en una ficha** cambia el campo. **Supr** elimina, **Ctrl+D** duplica, **Alt+↑/↓** mueve.
- **Izquierda, Campos:** las ~890 etiquetas (referencias propias, columnas del documento y Diccionario de referencia), con buscador. **Arrástralas** al documento o selecciona un elemento y haz clic (Mayús+clic: con formato numérico). Abajo, **＋ Nueva referencia**.
- **Izquierda, Elementos:** texto, título, caja, dos columnas, línea, espacio, **logo de la empresa** (`[LogoEmpresa]`), **subir una imagen** (se copia junto a los formatos, subcarpeta `Imagenes`), fila etiqueta+valor, fila de total, tabla, salto de página. Se insertan *después* del elemento seleccionado o *dentro* de él.
- **Izquierda, Documento:** papel y orientación, márgenes, **marca de agua**, letra base, repetir encabezado de tabla en cada página, no cortar renglones, **colores del formato** (cambias uno y cambia en todo el formato), **CSS avanzado** y **JavaScript** con una pequeña biblioteca de comportamientos.
- **Derecha, Propiedades** del elemento seleccionado: **letra** (tipo, tamaño, negrita, cursiva, subrayado, alineación, color, interlineado), **caja** (fondo, relleno, margen, borde, esquinas, ancho y alto), **tabla** (agregar o quitar columnas y filas), **imagen** (tamaño, logo de la empresa o subir otra), **estructura** (duplicar, eliminar, mover, envolver, sacar) y **avanzado** (clase, CSS en línea, «ocultar si está vacío»). En una ficha: **cambiar campo** y **formato numérico** (`#,##0.00`, `0`, `0.00%`…).

**Ejemplo, agregar el ISR a los totales:** selecciona la fila de Subtotal o IVA → **Duplicar** → doble clic en su texto y escribe «ISR retenido» → doble clic en su ficha y elige `[Retencion_ISR]` (o el campo que quieras).

### Constructor de referencias (traer datos de una tabla sin escribir SQL)

**＋ Nueva referencia** crea una etiqueta nueva (`[MiEtiqueta]`) cuyo valor sale de una consulta que armas eligiendo: **1** la tabla (las más usadas primero, o cualquiera de las de Comercial), **2** el dato y cómo (el primero, la suma, cuántos hay, el máximo, el mínimo, el promedio, o unir todos), **3** cómo se relaciona con el documento
(es de este documento, de esta partida, o comparte un campo con ellos, por ejemplo el mismo cliente o producto), **4** condiciones extra y el orden. Muestra la consulta generada (editable si sabes SQL), la **prueba con el documento** y la guarda en el **Diccionario de referencia** de Comercial como referencia propia: queda disponible en todos los formatos.
Solo se permiten consultas de lectura.

### Estirar y mover con el mouse

Al seleccionar un elemento aparece su **marco** con asas: la de la derecha **estira el ancho**, la de abajo el **alto** y la de la esquina ambos (en una celda de tabla el ancho se guarda en porcentaje de la tabla); un indicador muestra el tamaño en píxeles. El asa **⠿** (arriba a la izquierda) **mueve** el elemento: arrástrala sobre otro elemento y suéltala; una línea verde indica si quedará antes o después, y un borde verde punteado si quedará **dentro** de ese contenedor. Todo se puede deshacer con Ctrl+Z.

### Qué trae cada etiqueta (origen y valor)

En el panel **Campos**, cada etiqueta muestra su **valor real** en el documento elegido y su **origen**: por ejemplo `[NumeroIdentificacion]` → `docDocumentItem.ProductKey`. El botón **ⓘ** abre el detalle: la tabla y columna de donde sale (o la consulta, si es una referencia) y el valor que daría. En una referencia propia hay **Editar esta referencia**.

### Constructor: vistas, uniones y SQL ejecutable

- **Unión guiada:** «＋ Unir otra tabla o vista» propone las tablas que se pueden unir a lo que ya elegiste (las que comparten una columna `…ID`, por ejemplo `docDocument.BusinessEntityID` con `orgBusinessEntity`) y deja la unión armada.
- La pestaña **Armarla sin SQL** admite **tablas y vistas**, y **uniones** (INNER o LEFT) entre varias fuentes eligiendo las columnas que las relacionan; el SQL generado se ve al instante.
- La pestaña **Escribir SQL y ejecutarlo** tiene un explorador de tablas y columnas, **Probar el valor** (con el documento elegido) y **Ver filas** (hasta 50 filas en una cuadrícula). Solo se aceptan `SELECT` o `WITH`, una sola consulta, sin palabras de escritura, máximo 5000 filas. Puedes usar `{DocumentID}` y `{DocumentItemID}`.

### Notas

- El Diseñador **normaliza el HTML** (ordena atributos y comillas, agrega las etiquetas de tabla que falten); el formato se ve igual, pero el archivo cambia de forma. Por eso el aviso de respaldo.
- Los `<script>` del formato no se ejecutan dentro del lienzo (para que no cambien el diseño); sí se ejecutan al generar el PDF.
- La vista de diseño usa el motor de etiquetas de Comercial; la **Vista final** es lo más cercano al PDF (el PDF respeta los saltos de página del motor).

## Límites

- La vista previa necesita al menos un documento de ese tipo en la empresa.
- Un documento resuelto de más de 3 MB no se muestra en la vista previa (el PDF sí se genera).
- «Quitar» un formato borra su fila en la tabla de formatos de Comercial (no el archivo); el predeterminado no se deja quitar.
