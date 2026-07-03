# BrosLMV — Capacidades, alcance y poder

Este documento explica **qué puedes lograr** con BrosLMV: hasta dónde llega, qué
tienes disponible dentro de un botón, y ejemplos concretos de reportes, análisis,
automatización e integraciones. Es la guía para imaginar y diseñar botones.

- Para crear botones y la API: `MANUAL.md`.
- Para el detalle técnico interno: `ESPECIFICACION.md`.

---

## Tabla de contenido

1. [La idea de poder: un botón es un programa completo](#1-la-idea-de-poder)
2. [Las cuatro capas que tienes disponibles](#2-las-cuatro-capas)
3. [Catálogo de lo que puedes construir](#3-catálogo-de-lo-que-puedes-construir)
4. [Reportes HTML (con gráficas modernas)](#4-reportes-html)
5. [Análisis de datos](#5-análisis-de-datos)
6. [Exportar a Excel y PDF](#6-exportar-a-excel-y-pdf)
7. [Automatización y acciones masivas](#7-automatización-y-acciones-masivas)
8. [Integraciones (APIs, archivos, servicios)](#8-integraciones)
9. [Interfaces a la medida](#9-interfaces-a-la-medida)
10. [Usar librerías externas](#10-usar-librerías-externas)
11. [Rendimiento](#11-rendimiento)
12. [Límites honestos](#12-límites-honestos)
13. [Extender con Python real (opcional)](#13-extender-con-python-real)
14. [Resumen del alcance](#14-resumen-del-alcance)

---

## 1. La idea de poder

Cada botón de BrosLMV es **un programa completo en C#** que se ejecuta **dentro**
de CONTPAQi, con acceso directo a:

- los **documentos seleccionados** y la **base de datos viva** de la empresa,
- **todo el ecosistema .NET** (la misma plataforma sobre la que corre CONTPAQi),
- **librerías externas** que tú agregues,
- y la capacidad de **mostrar ventanas, reportes y gráficas**.

No es un "macro" limitado: es código real, compilado, con el poder de una
aplicación de escritorio, pero que escribes y editas como texto en segundos.

Mentalidad: *"si lo puede hacer un programa de escritorio en Windows, lo puede
hacer un botón."*

---

## 2. Las cuatro capas

Cuando escribes un script tienes acceso, de menor a mayor alcance, a:

### Capa 1 — El contexto `ctx` (lo que te da CONTPAQi, ya listo)
- `ctx.GetSelectedIds()` — los documentos seleccionados.
- `ctx.Scalar / Query / NonQuery` — leer y escribir en la base de la empresa
  (conexión automática, sin configurar nada).
- `ctx.Msg / Confirm / Log` — interacción y bitácora.
- `ctx.UserID`, `ctx.XEngineLib` — usuario y motor de CONTPAQi.

### Capa 2 — Toda la plataforma .NET Framework 4.8
Sin instalar nada, tienes la biblioteca estándar completa: archivos (`System.IO`),
texto y expresiones regulares, fechas, red e internet (`System.Net.Http`), XML y
HTML, cultura/formato, hilos y tareas (`async`/`await`), `LINQ`, dibujo
(`System.Drawing`), ventanas (`System.Windows.Forms`), etc.

### Capa 3 — Librerías externas (NuGet / DLLs)
Agregas un DLL y lo usas: Excel, PDF, JSON, gráficas, dataframes, estadística,
clientes de API… (ver §10).

### Capa 4 — El motor de CONTPAQi (`ctx.XEngineLib`)
El objeto del motor expone **cientos de funciones** internas de CONTPAQi
(impresión, módulos, tipos de cambio, etc.). Es acceso avanzado, vía late-binding,
para integraciones profundas.

---

## 3. Catálogo de lo que puedes construir

| Categoría | Ejemplos concretos |
|-----------|--------------------|
| **Reportes** | Estados, concentrados, comparativos, trazabilidad, antigüedad de saldos, kardex; en pantalla (HTML), Excel o PDF. |
| **Análisis de datos** | Agrupar por proveedor/cliente/estatus, totales por divisa, tendencias, detección de anomalías, KPIs, pivotes. |
| **Acciones masivas** | Cambiar estatus, asignar centro de costo, llenar campos `Custom`, marcar/validar lotes de documentos seleccionados. |
| **Validaciones** | Revisar reglas antes de un proceso (misma divisa, mismo proveedor, con XML, montos cuadrados). |
| **Generación de documentos** | Crear pagos, traslados, movimientos o documentos derivados a partir de los seleccionados. |
| **Integraciones** | Llamar APIs (tu reporteador, bancos, SAT, servicios web), exportar archivos (CSV/TXT bancarios), enviar correos. |
| **Interfaces a la medida** | Ventanas con formularios, grids, filtros, asistentes paso a paso, tableros. |

Cada una es **un archivo `.csx`** enlazado a un botón. Puedes tener decenas.

---

## 4. Reportes HTML

Puedes generar reportes con **HTML + CSS** (tablas, estilos, logos) y mostrarlos
dentro de CONTPAQi. Dos motores de visualización:

| Motor | Qué soporta | Cuándo usarlo |
|-------|-------------|---------------|
| `WebBrowser` (incluido en WinForms) | HTML/CSS clásico | Reportes simples, sin dependencias |
| `WebView2` (motor de Edge, vía librería) | HTML/CSS/**JavaScript modernos** | Gráficas interactivas (Chart.js, D3), diseño moderno |

### Ejemplo: reporte HTML con tabla, en una ventana

```csharp
// scripts\REPORTE_HTML.csx
var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona documentos."); return; }

var filas = ctx.Query(
    "SELECT d.Folio, be.OfficialName AS Proveedor, d.Total " +
    "FROM docDocument d LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID=d.BusinessEntityID " +
    "WHERE d.DocumentID IN (" + ctx.JoinIds(ids) + ")");

var sb = new System.Text.StringBuilder();
sb.Append("<html><head><meta charset='utf-8'><style>");
sb.Append("body{font-family:Segoe UI;margin:20px} h1{color:#0a3d6e}");
sb.Append("table{border-collapse:collapse;width:100%} th,td{border:1px solid #ccc;padding:8px}");
sb.Append("th{background:#0a3d6e;color:#fff;text-align:left} tr:nth-child(even){background:#f4f7fb}");
sb.Append("</style></head><body><h1>Reporte de documentos</h1><table>");
sb.Append("<tr><th>Folio</th><th>Proveedor</th><th>Total</th></tr>");
decimal total = 0;
foreach (var f in filas) {
    decimal t = Convert.ToDecimal(f["Total"] ?? 0); total += t;
    sb.Append("<tr><td>" + f["Folio"] + "</td><td>" + f["Proveedor"] + "</td><td style='text-align:right'>$" + t.ToString("N2") + "</td></tr>");
}
sb.Append("<tr><th colspan='2'>TOTAL</th><th style='text-align:right'>$" + total.ToString("N2") + "</th></tr>");
sb.Append("</table></body></html>");

// Mostrar en una ventana con navegador embebido
var frm = new System.Windows.Forms.Form { Text = "Reporte", Width = 900, Height = 600 };
var web = new System.Windows.Forms.WebBrowser { Dock = System.Windows.Forms.DockStyle.Fill };
frm.Controls.Add(web);
web.DocumentText = sb.ToString();
frm.ShowDialog();
```

### Gráficas interactivas
Con `WebView2` puedes incrustar **Chart.js** o **D3** en el HTML y tener barras,
pastel, líneas, tableros interactivos. También puedes **guardar el HTML a archivo**
y abrirlo en el navegador, o **exportarlo a PDF**.

---

## 5. Análisis de datos

### Con LINQ (sin librerías)
LINQ permite agrupar, filtrar, sumar y ordenar datos en memoria con muy poco
código:

```csharp
var filas = ctx.Query(
    "SELECT be.OfficialName AS Proveedor, d.Total " +
    "FROM docDocument d JOIN orgBusinessEntity be ON be.BusinessEntityID=d.BusinessEntityID " +
    "WHERE d.DocumentID IN (" + ctx.JoinIds(ctx.GetSelectedIds()) + ")");

// Total por proveedor, ordenado de mayor a menor
var porProveedor = filas
    .GroupBy(f => (string)f["Proveedor"])
    .Select(g => new { Proveedor = g.Key, Total = g.Sum(x => Convert.ToDecimal(x["Total"] ?? 0)) })
    .OrderByDescending(x => x.Total);

var sb = new System.Text.StringBuilder();
foreach (var p in porProveedor) sb.AppendLine(p.Proveedor + ": $" + p.Total.ToString("N2"));
ctx.Msg(sb.ToString(), "Compras por proveedor");
```

### Con librerías especializadas
- **Dataframes** (estilo tabla con operaciones tipo pivote/join) para análisis más
  complejos.
- **Estadística y numérico** (promedios, desviaciones, regresiones, series).

Combinado con los reportes HTML, puedes construir **tableros/KPIs** completos.

---

## 6. Exportar a Excel y PDF

Con la librería adecuada (ver §10):

- **Excel**: generar un `.xlsx` con varias hojas, formatos, fórmulas y gráficas a
  partir de los datos seleccionados, y abrirlo automáticamente.
- **PDF**: generar un documento con tablas, encabezados, logo y paginación; útil
  para reportes que se imprimen o se envían por correo.

Ambos a partir de `ctx.Query(...)`, con el diseño que definas.

---

## 7. Automatización y acciones masivas

Operar sobre **todos** los documentos seleccionados de un golpe, con confirmación
y bitácora:

```csharp
var ids = ctx.GetSelectedIds();
if (ids.Count == 0 || !ctx.Confirm("¿Procesar " + ids.Count + " documentos?")) return;

int n = ctx.NonQuery("UPDATE docDocument SET Custom2='REVISADO' WHERE DocumentID IN (" + ctx.JoinIds(ids) + ")");
ctx.Log("Marcados " + n + " documentos por usuario " + ctx.UserID);
ctx.Msg("Listo: " + n + " documentos actualizados.");
```

Sirve para cambios de estatus, asignaciones, llenado de campos, validaciones
previas a procesos, etc.

---

## 8. Integraciones

Como tienes red e internet disponibles, un botón puede:

- **Llamar a una API** (por ejemplo, un reporteador propio u otro servicio) con
  `HttpClient`, enviar los datos seleccionados y mostrar la respuesta.
- **Generar archivos** para terceros: layouts bancarios (TXT/CSV), archivos para
  contabilidad, exportaciones a otros sistemas.
- **Consultar servicios web** (tipos de cambio, validaciones, catálogos).
- **Enviar correos** con el reporte adjunto.

Ejemplo (POST a una API con los IDs seleccionados):

```csharp
using (var http = new System.Net.Http.HttpClient()) {
    var ids = ctx.GetSelectedIds();
    var json = "{\"ids\":[" + ctx.JoinIds(ids) + "]}";
    var resp = http.PostAsync("https://mi-reporteador/api/analizar",
        new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"))
        .GetAwaiter().GetResult();
    ctx.Msg(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(), "Respuesta API");
}
```

---

## 9. Interfaces a la medida

Tienes **WinForms** completo: un botón puede abrir su propia ventana con campos,
listas, grids editables, calendarios, pestañas, asistentes paso a paso, barras de
progreso, etc. La propia **consola de scripts** es un ejemplo de lo que se puede
construir. Es el mismo nivel de UI que una aplicación de escritorio.

Las ventanas pueden ser **modales** (`ShowDialog`) o **modeless** (`Show`): una ventana
modeless se **minimiza y convive con Comercial** (sigues trabajando en el ERP con la ventana
abierta) y puede consultar el documento seleccionado en vivo. Cómo hacerlo bien (y sin que
truene): [`UI_VENTANAS.md`](UI_VENTANAS.md).

---

## 10. Usar librerías externas

Roslyn permite referenciar DLLs desde un script con la directiva `#r`:

```csharp
#r "C:\BrosLMV\lib\Newtonsoft.Json.dll"
using Newtonsoft.Json;
// ... usar JsonConvert ...
```

### Cómo agregar una librería
1. Consigue el DLL de la librería **compatible con .NET Framework 4.8**
   (net48 o netstandard2.0).
2. Déjalo en `C:\BrosLMV\lib\` (o en `C:\BrosLMV\bin\` si trae dependencias, para
   que el cargador las encuentre).
3. En el script: `#r "C:\BrosLMV\lib\NombreDeLaLibreria.dll"`.

### Librerías típicas por necesidad
| Necesidad | Tipo de librería |
|-----------|------------------|
| JSON / consumir APIs | serialización JSON, cliente HTTP |
| Excel (.xlsx) | generación de hojas de cálculo |
| PDF | generación de documentos |
| Gráficas como imagen | librería de charts |
| Análisis tipo dataframe | dataframe / numérico |
| Navegador moderno (Chart.js/D3) | control de navegador basado en Edge |

> **Mejora planeada (ver `MANUAL.md`):** una carpeta `C:\BrosLMV\lib\` que el motor
> **auto-referencie**, para que cualquier DLL puesto ahí esté disponible en todos
> los scripts **sin** escribir `#r`. Es un cambio pequeño en el núcleo.

---

## 11. Rendimiento

Los scripts se **compilan a código nativo intermedio (IL)** y se ejecutan a
velocidad de aplicación compilada — no son interpretados. Esto importa cuando:

- procesas **miles de filas** (análisis, reportes grandes),
- haces **cálculos intensivos** (estadística, agregaciones),
- generas documentos pesados.

La primera ejecución de un script paga una pequeña compilación; las siguientes son
inmediatas.

---

## 12. Límites honestos

- **Compatibilidad de librerías:** la DLL externa debe ser compatible con **.NET
  Framework 4.8** (net48 o netstandard2.0). Algunas librerías muy nuevas solo
  existen para versiones de .NET posteriores y no cargarán; casi siempre hay una
  versión compatible.
- **Dependencias de librerías:** si una librería trae sus propios DLLs, hay que
  copiarlos junto a ella para que cargue.
- **Procesos muy largos:** la UI de CONTPAQi se bloquea mientras el script corre
  (igual que cualquier acción del programa). Para procesos de minutos conviene
  mostrar progreso o ejecutarlos en lotes.
- **Acceso al motor (`XEngineLib`):** es potente pero por late-binding (sin
  autocompletado); requiere conocer los nombres de las funciones internas.

Ninguno de estos límites impide los casos del §3; solo conviene tenerlos presentes.

---

## 13. Extender con Python real (opcional)

Si algún análisis específico justifica el **stack científico de Python**
(`pandas`, `numpy`, `matplotlib`), se puede integrar **sin perder nada** de lo
anterior. C# se encarga de la integración con CONTPAQi (selección, conexión, UI) y
delega el cálculo pesado a Python.

| Enfoque | Qué es | Poder | Complejidad |
|---------|--------|-------|-------------|
| **Llamar a `python.exe`** (recomendado) | El botón lanza un script Python, le pasa datos (JSON/archivo/BD) y recibe el resultado (HTML/PNG/PDF) | Stack completo de Python | Media |
| **Incrustar CPython en proceso** | Hospedar Python 3 dentro del mismo proceso | Stack completo, más acoplado | Media-alta |

Arquitectura recomendada (desacoplada):

```
Botón C# (BrosLMV)
   ├── lee selección + datos de CONTPAQi
   ├── invoca  python.exe analisis.py  ──►  pandas / numpy / matplotlib
   │                                          └── devuelve HTML / PNG / PDF
   └── muestra el resultado (WebView2 / imagen / PDF)
```

Esto se documenta y se implementa solo si un caso lo amerita; **no es necesario**
para reportes, análisis con LINQ/dataframes, Excel, PDF ni integraciones.

---

## 14. Resumen del alcance

| Dimensión | Alcance |
|-----------|---------|
| Lenguaje | C# moderno, compilado |
| Datos | Base de la empresa (lectura/escritura) + selección del grid, sin configurar conexión |
| Plataforma | Toda la biblioteca de .NET Framework 4.8 |
| Librerías | Cualquiera compatible con .NET Framework (Excel, PDF, JSON, gráficas, dataframes…) |
| Reportes | HTML (con gráficas interactivas), Excel, PDF |
| UI | Ventanas a la medida (nivel aplicación de escritorio) |
| Integraciones | APIs, archivos, correo, servicios web |
| Ampliable a | Python científico (opcional) |
| Edición | Texto, sin recompilar, ciclo de segundos |
| Autonomía | En proceso, bajo tu control, sin servicios externos |

En una frase: **un botón puede hacer prácticamente cualquier cosa que haría una
aplicación de escritorio en Windows**, con acceso directo a los datos de CONTPAQi y
editándose como texto.
