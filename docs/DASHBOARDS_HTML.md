# BrosLMV — Cómo construir un dashboard HTML rápido y portable

> Guía de referencia para cualquier botón que muestre un reporte visual (tabla, KPIs,
> gráficas) dentro de CONTPAQi. Resultado de una sesión de diseño el 2026-07-29, a partir
> de un bug real en producción (`ReporteXVehiculo`, ver §5). Objetivo: que un dashboard
> nuevo **no requiera copiar nada a ninguna terminal**, sea **rápido sin importar el
> volumen histórico del cliente**, y funcione igual en un servidor con 1 terminal RDP o en
> un cliente con 100 terminales físicas.

---

## 1. La regla de oro: agrega en SQL, nunca mandes filas crudas al navegador

El cuello de botella de un dashboard casi nunca es "qué tan rápido dibuja HTML/JS" — es
**cuántas filas cruzan de la base de datos al navegador**. SQL Server agrega millones de
filas más rápido que cualquier lenguaje de aplicación. Lo que debe llegar a
`ctx.dashboard()`/`ctx.show_html()` es el **resultado ya resumido** (totales por mes, top
10 proveedores, KPIs) — unos cuantos KB, sin importar si la tabla origen tiene 500 o 5
millones de filas.

**Para reportes pesados de verdad** (rollups históricos de años): no agregues al vuelo en
cada clic. Pre-calcula en una tabla/vista propia (`zzBros*` o `BRO_*_VW`, ver
`RECETAS_NOCODE.md` §5b) refrescada por un trabajo programado — el candidato natural es
`BrosLMV.Runner` (T3.3 del plan de implementación), corriendo de madrugada. El dashboard
interactivo entonces solo lee la tabla ya resumida: instantáneo, sin importar el volumen
histórico real del cliente.

**Para "quiero ver/auditar todos los movimientos"** (una lista completa, no un dashboard
visual): no uses HTML. Un `DataGridView` nativo (virtualizado, solo renderiza filas
visibles) o exportar directo a Excel (`ctx.erp.ExportQueryToExcel`) manejan volumen real
mucho mejor que una tabla HTML con miles de `<tr>`.

---

## 2. Usa `ctx.dashboard()` — no reinventes el HTML a mano

Para el caso común (tabla de datos con búsqueda, orden por columna, exportar a Excel), usa
el helper del SDK Python en vez de escribir tu propio HTML/CSS/JS:

```python
from broslmv import ctx

filas = ctx.query("SELECT Proveedor, Total, Fecha FROM BRO_ComprasResumen_VW")
ctx.dashboard("Compras por proveedor", filas)
```

Eso ya te da: tabla ordenable (clic en encabezado), buscador en vivo, paginación (50 filas
por página, para que no se ponga lento con datos grandes), botón "Exportar a Excel", y
compresión automática de los datos — todo sin escribir una línea de HTML.

### Columnas explícitas (opcional)

Si quieres controlar el orden/nombre de las columnas (en vez de inferirlas de las llaves
del primer registro):

```python
ctx.dashboard("Compras por proveedor", filas, columns=[
    {"key": "Proveedor", "label": "Proveedor"},
    {"key": "Total", "label": "Total ($)"},
])
```

### Firma completa

```python
ctx.dashboard(title, data, columns=None, width=1000, height=700, modal=True)
```

---

## 3. Por qué es portable: cero assets por script, cero copiar a terminales

`ctx.dashboard()` **no crea ninguna carpeta de assets por script**. Todo lo que necesita
vive en dos lugares, ninguno de los cuales tienes que distribuir tú:

| Qué | Dónde vive | Quién lo pone ahí |
|---|---|---|
| El script (tu código Python) | `zzBrosScript` (SQL Server) | Tú, al Guardar desde la consola — ya está disponible en las 5/100 terminales de inmediato, porque todas comparten la misma base |
| La plantilla + librería compartida (`dashboard_base.html`, `dashboard.css`, `xlsx.bundle.js`) | `C:\BrosLMV\lib\dashboard\` | **El instalador**, una sola vez por terminal, la misma vez que ya instalas/actualizas BrosLMV — no por script, no por cliente |

Ningún dato de un reporte específico se queda en disco. Los datos viajan **dentro del
HTML que genera el script**, comprimidos — no hay archivo intermedio que copiar.

> **Para quien mantiene el runtime compartido (no un script individual):** la fuente
> versionada en git de `dashboard_base.html`/`dashboard.css`/`xlsx.bundle.js` vive en
> `instalador\assets\dashboard\` — **no** en `instalador\lib\dashboard\`.
> `instalador\lib\` está en `.gitignore` ("binarios regenerables", ahí solo van DLLs
> restauradas por NuGet) — cualquier archivo agregado directo ahí se pierde al hacer
> commit. `build\generar_instalador.ps1` (paso 5b) copia de `assets\` a `lib\` en cada
> build; si agregas una librería compartida nueva, ponla en `instalador\assets\dashboard\`.

> **Nota sobre RDP:** si el cliente corre Comercial PRO en un servidor con Terminal
> Services/RDP, no hay "5 terminales" — hay un solo `C:\BrosLMV\`, compartido por todas las
> sesiones. Ahí este problema no existe ni con el patrón viejo. Diseña pensando en el caso
> de terminales físicas/VMs independientes (el que sí lo necesita).

---

## 4. Si necesitas algo a la medida (no encaja en tabla+Excel)

`ctx.dashboard()` cubre el caso común. Si tu reporte necesita gráficas (Chart.js/D3) o un
layout muy distinto, usa `ctx.show_html()` directo — pero sigue estas reglas para que
también sea portable:

1. **Tus assets propios (HTML/CSS/JS específicos de ESE reporte) van incrustados dentro
   del script**, no en una carpeta aparte. Son casos chicos (unos KB) — sin razón técnica
   para vivir en disco. Ejemplo:
   ```python
   PLANTILLA = """<html>...<style>...</style><script>...</script></html>"""
   html = PLANTILLA.replace("{{DATOS}}", json.dumps(filas))
   ctx.show_html(html, title="Mi Reporte")
   ```
2. **Librerías compartidas y pesadas** (Chart.js, D3, cualquier `.js`/`.css` de terceros
   que uses en varios reportes) van en `C:\BrosLMV\lib\<tu_carpeta>\`, agregadas por el
   instalador (habla con quien mantiene `instalador\lib\` para sumarlas ahí) — nunca
   copiadas a mano por script. Referéncialas como
   `https://broslmv.local/<tu_carpeta>/archivo.js` (el host ya mapea `C:\BrosLMV\lib\` a
   ese host virtual — ver `HostClient.cs` `RenderUiHtml`).
3. **Nunca fijes el nombre de la empresa/base de datos en una ruta.** Si por algún motivo
   sí necesitas leer/escribir un archivo por empresa, usa `ctx.empresa` para construir la
   ruta (`os.path.join(r"C:\BrosLMV\scripts", ctx.empresa, "...")`) — nunca lo escribas
   literal. Ver §5 para el bug real que causó esta regla.
4. **Datos grandes: comprime.** `NavigateToString` (lo que usa `ctx.show_html`) tiene un
   límite real de ~2MB de contenido total. Comprime con gzip nivel 9 + base64 y
   descomprime en el navegador con la API nativa `DecompressionStream` (Chromium la trae
   desde 2020, no hace falta ninguna librería):
   ```python
   import base64, gzip, json
   crudo = json.dumps(filas, default=str).encode("utf-8")
   datos_b64 = base64.b64encode(gzip.compress(crudo, compresslevel=9)).decode("ascii")
   ```
   ```js
   async function descomprimir(b64) {
     const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
     const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
     const buf = await new Response(stream).arrayBuffer();
     return JSON.parse(new TextDecoder().decode(buf));
   }
   ```
   Si aun comprimido no cabe (reportes verdaderamente enormes), escribe el HTML a un
   archivo temporal y usa `Navigate()` en vez de `NavigateToString()` — no tiene ese
   límite. (`ctx.dashboard()` no necesita esto hoy: 3,000 filas típicas comprimen a decenas
   de KB, muy por debajo del límite.)

---

## 5. Caso real que originó esta guía

`ReporteXVehiculo.py` (GGV) fijaba la ruta de sus assets con el nombre de la empresa
escrito a mano: `r"C:\BrosLMV\scripts\GGV_DE_MEXICO\ReporteXVehiculo_assets"`. Al pasar el
script a la base de datos del cliente (`GGV_DE_MEXICO_2025`), el reporte cargaba a medias
sin importar dónde se copiara la carpeta de assets — la ruta nunca miraba la empresa activa
real. **Corregido (2026-07-29):** ahora usa `os.path.join(r"C:\BrosLMV\scripts", ctx.empresa,
"ReporteXVehiculo_assets")`. Este es exactamente el error que la regla 3 de la §4 evita.

**Pendiente (no bloqueante):** migrar `ReporteXVehiculo`/`CUENTAS_POR_COBRAR`/
`CUENTAS_POR_PAGAR`/`SEGUIMIENTO_OC` a `ctx.dashboard()` o al patrón de assets incrustados
de §4, para dejar de depender de una carpeta `_assets\` por reporte. Migrar uno primero
como piloto (`ReporteXVehiculo`) antes de tocar los otros tres.

---

## 6. Resumen — checklist para un dashboard nuevo

- [ ] Los datos que le mando al HTML ya están agregados en SQL (no filas crudas).
- [ ] Si el histórico es pesado, hay una tabla/vista pre-calculada refrescada por trabajo
      programado, no un cálculo al vuelo cada clic.
- [ ] Uso `ctx.dashboard()` si el caso es tabla+buscador+Excel (el 90% de los casos).
- [ ] Si necesito algo a la medida, mis assets propios van incrustados en el script — cero
      carpeta `_assets\` nueva.
- [ ] Ninguna ruta tiene el nombre de la empresa escrito a mano — uso `ctx.empresa`.
- [ ] Si los datos son grandes, van comprimidos (gzip+base64).
