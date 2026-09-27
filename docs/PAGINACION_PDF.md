# Paginación real en PDFs (Paged.js)

> Técnica opcional para plantillas `htmlpdf/formatos/*.html` que necesitan encabezado/pie
> **repetidos en cada hoja** con "Página X de Y" real (no estimado) y tablas de detalle que
> se parten entre hojas sin perder el encabezado de columnas. Las 10 plantillas actuales NO la
> usan todavía (ver "Qué falta" al final) — este documento describe el patrón, listo para
> aplicarse plantilla por plantilla cuando haga falta.

## Por qué no basta con CSS puro

`@page` + `counter(page)`/`counter(pages)` es CSS estándar, pero **Chromium (WebView2) no lo
soporta al imprimir a PDF** — solo lo pintan motores con soporte de paginación CSS real (impresoras
de libro, algunos navegadores con flags experimentales). `BrosLMV.HtmlToPdf.exe` usa
`WebView2.PrintToPdfAsync`, que es Chromium puro: sin ayuda extra, un `@page { @bottom-right {
content: counter(page) } }` se ignora en el PDF resultante.

**Paged.js** (librería JS, MIT, ejecuta en el propio HTML antes de imprimir) sí implementa esa
paginación: mide el contenido, lo corta en "hojas" reales (`<div class="pagedjs_page">`) e inyecta
el `@page`/`string-set`/`counter()` como HTML+CSS ya resuelto. El PDF que genera Chromium a partir
de eso sí tiene encabezado/pie corridos y números de página correctos.

## Piezas del patrón

### 1. La librería

`htmlpdf/formatos/paged.polyfill.min.js` (mismo lugar que `_qrcode.min.js`) — Paged.js v0.4.3,
licencia MIT, sin dependencias, ~500 KB minificado. Se incluye tal cual, sin build.

### 2. `@page` con `string-set` / `string()` / `counter()`

```css
@page {
    size: letter;
    margin: 16mm 11mm 15mm 11mm;
    @top-left    { content: string(emisor); font: 7.5pt Arial, sans-serif; color: #9a9a9a; }
    @top-right   { content: "Folio " string(folio); font: 7.5pt Arial, sans-serif; color: #9a9a9a; }
    @bottom-right{ content: "P\00e1gina " counter(page) " de " counter(pages); font: 7.5pt Arial; }
}

/* en el elemento que trae el dato real, una sola vez en el HTML: */
.empresa-nombre { string-set: emisor content(text); }
.folio-num      { string-set: folio content(text); }
```

`string-set` "graba" el texto de ese elemento (normalmente algo que solo aparece en la 1ª hoja,
como el nombre del emisor o el folio) en una variable con nombre; `string()` la reproduce en
cualquier `@top-*`/`@bottom-*` de cualquier hoja. `counter(page)`/`counter(pages)` los resuelve
Paged.js automáticamente.

### 3. Llenar la hoja aunque haya pocas partidas (spacer flex)

```css
.doc { display: flex; flex-direction: column; min-height: 241mm; }  /* carta 279.4mm - márgenes */
.spacer { flex: 1 1 auto; }
```

```html
<div class="doc">
  ...encabezado, tabla de detalle...
  <div class="spacer">&nbsp;</div>
  ...cierre (observaciones + totales)...
</div>
```

El `min-height` se calcula por debajo del área útil real de la hoja (nunca igual o más, o
redondeos de fuente pueden desbordar a una 2ª hoja falsa). El `.spacer` flexible absorbe el resto
y empuja el bloque de cierre al fondo — igual que un documento impreso de verdad, no una tabla
flotando a media hoja.

### 4. Repetir el `<thead>` cuando la tabla se parte entre hojas

CSS estándar ya cubre esto (`thead { display: table-header-group }`), pero **Paged.js 0.4.3 tiene
un bug real: no repite el `<thead>` en los fragmentos de continuación de una tabla partida entre
hojas.** Se resuelve con un `Paged.Handler` propio que clona el `<thead>` visto la primera vez y lo
reinserta en cada fragmento donde falte:

```js
(function () {
    if (!window.Paged || !window.Paged.registerHandlers || !window.Paged.Handler) return;
    var theadGuardado = null;
    class RepetirEncabezado extends window.Paged.Handler {
        constructor(chunker, polisher, caller) { super(chunker, polisher, caller); }
        afterPageLayout(pageEl) {
            try {
                var tablas = pageEl.querySelectorAll('table.tabla-detalle');   // ajustar selector
                for (var i = 0; i < tablas.length; i++) {
                    var t = tablas[i];
                    var th = t.querySelector('thead');
                    if (th && th.children.length) { if (!theadGuardado) theadGuardado = th.cloneNode(true); continue; }
                    if (!th && theadGuardado) {
                        var c = theadGuardado.cloneNode(true);
                        c.setAttribute('data-repeated-header', '1');
                        t.insertBefore(c, t.firstChild);
                    }
                }
            } catch (e) {}
        }
    }
    window.Paged.registerHandlers(RepetirEncabezado);
})();
```

Debe registrarse **después de cargar la librería, antes de que pagine** (Paged.js pagina solo al
cargar si `window.PagedConfig.auto = true`, ver punto 6).

Importante: la tabla de detalle debe ir envuelta en su propio `<div>`, **no directamente dentro
de un contenedor flex** (como `.doc`) — si la tabla es hija directa de un flex container, Paged.js
no la trata como elemento partible entre hojas y el `<thead>` nunca se repite porque nunca se
generan fragmentos.

### 5. Empujar el cierre al fondo también en la 3ª hoja y siguientes

El `.spacer` (punto 3) solo cubre la 1ª hoja (calculado contra `min-height` de `.doc` completo).
Si el documento ya se partió en 2+ hojas, el cierre (observaciones/totales) puede quedar a media
hoja en vez de al fondo. Se corrige después de paginar, midiendo el espacio libre real de esa hoja
específica:

```js
function pegarCierreAlFondo() {
    try {
        var cierre = document.querySelector('.pagedjs_pages .cierre');   // ajustar selector
        if (!cierre) return;
        var box = cierre.closest('.pagedjs_page_content');
        if (!box) return;
        var libre = box.getBoundingClientRect().bottom - cierre.getBoundingClientRect().bottom;
        var seg = 6 * (96 / 25.4);   // 6 mm de colchón de seguridad
        if (libre > seg + 8) {
            var actual = parseFloat(getComputedStyle(cierre).marginTop) || 0;
            cierre.style.marginTop = (actual + libre - seg) + 'px';
        }
    } catch (e) {}
}
```

### 6. El contrato `window.__READY_FOR_PDF__`

`BrosLMV.HtmlToPdf.exe` (`htmlpdf/Program.cs`, `EsperarListoAsync`) espera a que el HTML ponga
`window.__READY_FOR_PDF__ = true` antes de llamar `PrintToPdfAsync` — con timeout y sin bloquear
plantillas viejas que no conocen el flag (ver ese archivo, ya implementado, nada que tocar aquí).
Con Paged.js el flag se activa en el callback `after` de `window.PagedConfig`, que dispara justo
cuando terminó de paginar:

```js
window.__READY_FOR_PDF__ = false;

window.PagedConfig = {
    auto: true,
    after: function () {
        try { document.documentElement.classList.add('pagedjs-ok'); } catch (e) {}
        try { pegarCierreAlFondo(); } catch (e) {}
        window.__READY_FOR_PDF__ = true;
    }
};
```

`window.PagedConfig` **debe declararse antes de cargar `paged.polyfill.min.js`** (la librería lo
lee al arrancar). El handler del punto 4 se registra **después** de cargarla.

### 7. Degradación segura (salvavidas)

Dos escenarios donde Paged.js no corre o no termina, y el exe no debe colgarse esperando un flag
que nunca llega:

```js
(function () {
    function estimarHojas() {
        try {
            var PX_MM = 96 / 25.4;
            var hojaUtilPx = (11 * 96) - (31 * PX_MM);   // carta - márgenes verticales totales
            var doc = document.querySelector('.doc');
            if (!doc) return 1;
            return Math.max(1, Math.ceil(doc.scrollHeight / hojaUtilPx));
        } catch (e) { return 1; }
    }

    var tienePaged = !!(window.Paged || window.PagedPolyfill);

    if (tienePaged) {
        // Paged.js cargó pero por algo no disparó 'after' (documento raro, error interno):
        // no colgar el render, forzar listo a los 12s.
        setTimeout(function () {
            if (!window.__READY_FOR_PDF__) {
                try { document.documentElement.classList.add('pagedjs-ok'); } catch (e) {}
                window.__READY_FOR_PDF__ = true;
            }
        }, 12000);
    } else {
        // Paged.js no cargó (motor viejo de Comercial, u otro renderer sin la librería):
        // ocultar el pie corrido de @page (Chromium sin Paged.js sí pinta counter() en algunos
        // casos) y usar un pie estático con "Página 1 de N" estimado por JS.
        var sf = document.querySelector('.static-footer');
        if (sf) sf.style.display = 'none';
        var np = document.getElementById('np');
        if (np) np.textContent = estimarHojas();
        window.__READY_FOR_PDF__ = true;
    }
})();
```

## Orden de los `<script>` en el HTML

1. `window.PagedConfig = {...}` (punto 6) — **antes** de la librería.
2. `<script src="paged.polyfill.min.js"></script>`.
3. El `Paged.Handler` de repetir encabezado (punto 4) — justo después de la librería.
4. El salvavidas (punto 7) — al final, después de todo lo anterior.

## Diferencia con el pie estático de las 10 plantillas actuales

Las plantillas actuales (`OrdenCompra_BrosLMV.html` y hermanas) usan `@page { margin }` +
`page-break-inside: avoid` en filas/bloques — suficiente para que nada se corte a la mitad, pero
**sin encabezado/pie repetido ni número de página real**: cada hoja adicional sale "en blanco" de
maquetación (sin membrete ni "Página X de Y"). Esto es aceptable para documentos que casi siempre
caben en una hoja; se vuelve un problema visible en documentos largos (órdenes de compra o
facturas con muchas partidas).

## Qué falta (no hecho en este pase)

- **Ninguna de las 10 plantillas existentes fue retocada todavía** — este documento solo trae la
  pieza (librería + patrón documentado) a BrosLMV. Retrofitear una plantilla implica: envolver la
  tabla de detalle en su propio `<div>` fuera de cualquier flex container, agregar el `@page` con
  `string-set`, el spacer, y los 4 bloques de script (config, handler, salvavidas) — no es
  mecánico, cada plantilla tiene su propio layout de cierre/totales que hay que revisar caso por
  caso.
- Sin probar contra un documento real de más de ~14 partidas desde este repo (sí está probado en
  producción en el proyecto de origen, con volúmenes similares).
- El truco de `pegarCierreAlFondo()` asume que el bloque de cierre tiene `page-break-inside: avoid`
  — si no lo tiene, puede partirse entre hojas antes de que el JS lo mueva.
