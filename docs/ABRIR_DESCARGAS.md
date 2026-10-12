# Abrir BrosLMV Descargas

Plantilla de fábrica **Plantillas → Documentos → Abrir BrosLMV Descargas** (`ABRIR_DESCARGAS.ctx`). Pone en la cinta de Comercial un botón que abre **BrosLMV Descargas**, el programa que baja tus CFDI del SAT, para que no sientas que es una herramienta aparte: la abres desde donde ya trabajas.

**No modifica nada** en Comercial: solo abre el programa.

## Cómo usarla

1. Instala **BrosLMV Descargas** (instalador aparte, gratis).
2. En la **Consola** abre la plantilla y guárdala como botón (nombre sugerido `ABRIR_DESCARGAS`); el asistente *Crear botón…* la pone en el ribbon.
3. Pulsa el botón desde cualquier empresa.

## Qué hace al pulsarlo

- Si Descargas **no está abierto**, lo inicia.
- Si ya estaba abierto, o escondido junto al reloj, **lo trae al frente** en vez de abrir otra copia.
- Lee el **RFC de la empresa que tienes abierta** en Comercial y se lo pasa a Descargas: si esa empresa ya está dada de alta allí, muestra directamente su tablero.
- Si Descargas no está instalado, te lo dice y explica dónde conseguirlo.

## Requisitos

- BrosLMV Descargas con la versión que incluye esta función (2.5.0 o posterior). Con una versión anterior el botón también abre el programa, pero no cambia de empresa ni trae al frente la ventana ya abierta.
- Se espera en `C:\BrosLMV\Descargas\ui\`, que es donde lo deja su instalador.

## Para adaptarla

El script es corto. Puedes cambiar la ruta del programa, o agregar otros argumentos. Descargas entiende `--rfc <RFC>` para abrir una empresa concreta.
