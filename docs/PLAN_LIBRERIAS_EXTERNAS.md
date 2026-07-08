# Plan de ampliación: librerías externas para scripts C#

> **Estado: EJECUTADO (2026-07-07).** Las 4 librerías ya están en `C:\BrosLMV\lib\`,
> validadas por compilación real contra un proyecto de prueba, con scripts de prueba en
> `C:\BrosLMV\scripts\Distribuciones_Candelas\` (`PRUEBA_JSON.ctx`, `PRUEBA_QR.ctx`,
> `PRUEBA_EXCEL.ctx`, `PRUEBA_WEBVIEW2.ctx`) y la plantilla reutilizable en
> `C:\BrosLMV\scripts\PLANTILLA_BASE_CSHARP_WEBVIEW2.ctx`. Runtime de WebView2 ya estaba
> instalado en el servidor (v149.0.4022.98) — no hizo falta empacar el instalador. Ver
> §6 para el detalle de la ejecución y §2/§3 actualizadas con la lista real de
> dependencias (una se adivinó mal en la primera versión de este documento: `RBush.dll`
> no existe, la dependencia real es `XLParser.dll`).

Propuesta técnica original (2026-07-06): dar a los scripts de C# acceso a
> navegador moderno embebido (WebView2), Excel real (ClosedXML), JSON robusto
> (Newtonsoft.Json) y códigos QR (QRCoder), **sin tocar ni recompilar el núcleo**
> (`BrosLMVClsMain.dll`). Ver también [`CAPACIDADES.md`](CAPACIDADES.md) §10 "Usar
> librerías externas" (mecanismo ya documentado, aquí se detalla para estos 4 casos
> concretos) y [`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md) §8 (WebView2 ya estaba anticipado
> ahí para `ctx.show_html` de Python — esta propuesta lo habilita también para C#, antes,
> y sin esperar a esa función nueva de `ctx`).

---

## 0. El hallazgo que cambia todo el plan

La primera versión de esta propuesta asumía que había que **recompilar el núcleo**
(`src/BrosLMV.csproj`, agregar las referencias a `ScriptOptions` en `Scripting.cs`,
regenerar el instalador) para dar acceso a estas librerías. **Eso ya no es necesario.**

`CAPACIDADES.md` §10 documenta que Roslyn ya soporta la directiva `#r` dentro de
cualquier script `.ctx`, que carga un DLL en tiempo de compilación **y** de ejecución sin
tocar `ScriptOptions.WithReferences()`:

```csharp
#r "C:\BrosLMV\lib\Newtonsoft.Json.dll"
using Newtonsoft.Json;
```

Esto significa que las 4 librerías de este plan se agregan **solo copiando archivos**
a `C:\BrosLMV\lib\` (crear la carpeta si no existe) — **cero cambios al DLL compilado
que usan todos los botones de todas las empresas.** Se elimina casi por completo el
riesgo que se discutió antes (recompilar y redesplegar `BrosLMVClsMain.dll` con riesgo
de romper botones existentes). El único riesgo que **sigue siendo real** es el de
WebView2 en sí (ver §3.3) — pero ya no depende de tocar el núcleo, depende de cómo se
escriba el script que lo use.

**Conclusión:** este plan es de bajo riesgo. Se puede probar agregando/quitando archivos
de una carpeta, sin recompilar nada, sin reiniciar CONTPAQi salvo para probar el script.

---

## 1. Por qué estas 4 y no otras

| Librería | Para qué | Por qué esta y no otra | Licencia |
|---|---|---|---|
| **Microsoft.Web.WebView2** | Navegador Edge/Chromium embebido en una ventana propia — HTML/CSS/JS moderno **dentro de un script C#**, con puente de comunicación C#↔JavaScript | Es la oficial de Microsoft, ya viene probada porque Python (`pywebview`) la usa hoy mismo en esta misma instalación (`runtimes\python\...\webview\lib\`) | MIT |
| **ClosedXML** | Generar archivos `.xlsx` reales (varias hojas, formato, autofiltro, fórmulas) desde C# sin escribir XML a mano | Es exactamente el problema que ya vivimos: para el Excel del Reporte Ejecutivo en Python tuvimos que armar el `.xlsx` a mano con `zipfile` porque no había ninguna librería de Excel disponible. Esto evita que se repita para C# | MIT |
| **Newtonsoft.Json** | Leer/escribir JSON — necesario si algún script llega a consumir una API externa (SAT, paquetería, WhatsApp/SMS, pasarela de pago) con `ctx.erp.GetWebContent` | Es el estándar de facto en .NET, la más probada, la que todo el mundo espera encontrar | MIT |
| **QRCoder** | Generar códigos QR (para etiquetas, trazabilidad de lote, o pegar en un ticket/documento) | Pequeña, sin dependencias raras, hace una sola cosa bien | MIT |

**Por qué NO EPPlus ni QuestPDF** (alternativas populares a ClosedXML): desde hace
varias versiones cambiaron a licencias que cobran a partir de cierto ingreso del
negocio ("Polyform Noncommercial" / licencia comercial). BrosLMV es software libre
(GPL-3.0) — mejor quedarnos con librerías 100% libres (MIT), sin ambigüedad.

---

## 2. Cómo se instalan (las 3 sin WebView2)

Estructura de carpeta:

```
C:\BrosLMV\lib\
├─ Newtonsoft.Json.dll                         (13.0.3)
├─ QRCoder.dll                                 (1.6.0)
├─ ClosedXML.dll                               (0.102.3)
├─ DocumentFormat.OpenXml.dll                  (dependencia de ClosedXML)
├─ ExcelNumberFormat.dll                       (dependencia de ClosedXML)
├─ XLParser.dll                                (dependencia de ClosedXML, formulas)
├─ Irony.dll                                   (dependencia de XLParser)
├─ SixLabors.Fonts.dll                         (dependencia de ClosedXML)
├─ System.IO.Packaging.dll                     (dependencia de DocumentFormat.OpenXml)
├─ System.Buffers.dll                          (dependencia transitiva, net48)
├─ System.Memory.dll                           (dependencia transitiva, net48)
├─ System.Numerics.Vectors.dll                 (dependencia transitiva, net48)
├─ System.Runtime.CompilerServices.Unsafe.dll  (dependencia transitiva, net48)
├─ Microsoft.Web.WebView2.Core.dll             (1.0.2739.15)
├─ Microsoft.Web.WebView2.WinForms.dll
└─ WebView2Loader.dll                          (nativo, win-x86 — ver §3.1)
```

**Cómo se obtienen (mismo criterio que `instalador\runtimes\python`):** estos `.dll` son
binarios de terceros regenerables desde NuGet — el repo ya tiene la costumbre de NO
versionar ese tipo de binario (`instalador\host\`, `instalador\runtimes\`,
`instalador\workers\` están en `.gitignore` con esa misma justificación). Se agregó
`instalador\lib\` a esa misma regla, y un script nuevo que sigue el patrón de
`build\descargar_python.ps1`:

```powershell
.\build\descargar_librerias_externas.ps1
```

Internamente compila un proyecto `net48` desechable (en `.temp_tests\`, no se versiona)
que referencia las 4 librerías por NuGet — `dotnet build` resuelve automáticamente todas
las dependencias transitivas (sin tener que adivinarlas a mano) — y copia los `.dll`
resultantes a `instalador\lib\`, incluyendo el `WebView2Loader.dll` de **32 bits**
correcto (§3.1). Se corre una sola vez (o cuando se quiera actualizar de versión), no en
cada `generar_instalador.ps1` — igual que `descargar_python.ps1`.

Para agregar una librería nueva en el futuro: agregar su `<PackageReference>` dentro de
`build\descargar_librerias_externas.ps1` (la variable `$csproj`) y volver a correr el
script.

### Ejemplo — Newtonsoft.Json

```csharp
#r "C:\BrosLMV\lib\Newtonsoft.Json.dll"
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona un documento."); return; }

string respuesta = ctx.erp.GetWebContent("https://api.ejemplo.com/tipo-cambio/USD");
var json = JObject.Parse(respuesta);
double tipoCambio = (double)json["rate"];

ctx.Msg("Tipo de cambio USD: " + tipoCambio);
```

### Ejemplo — QRCoder (código QR de un documento, como imagen para pegar en un ticket)

```csharp
#r "C:\BrosLMV\lib\QRCoder.dll"
using QRCoder;

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona un documento."); return; }
long docId = ids[0];

var folio = ctx.Scalar("SELECT ISNULL(FolioPrefix,'')+ISNULL(Folio,'') FROM docDocument WHERE DocumentID=" + docId);
using (var qrGenerator = new QRCodeGenerator())
using (var qrData = qrGenerator.CreateQrCode("DOC:" + docId + ":" + folio, QRCodeGenerator.ECCLevel.Q))
{
    var qrCode = new PngByteQRCode(qrData);
    byte[] png = qrCode.GetGraphic(10);
    System.IO.File.WriteAllBytes(@"C:\BrosLMV\temp\qr_" + docId + ".png", png);
    ctx.Msg("QR generado en C:\\BrosLMV\\temp\\qr_" + docId + ".png");
}
```

### Ejemplo — ClosedXML (exportar la selección del grid a un Excel real, con formato)

```csharp
#r "C:\BrosLMV\lib\ClosedXML.dll"
using ClosedXML.Excel;

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona uno o más documentos."); return; }

var filas = ctx.Query(
    "SELECT d.DocumentID, d.Folio, d.DateDocument, be.OfficialName AS Cliente, d.Total " +
    "FROM docDocument d LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID " +
    "WHERE d.DocumentID IN (" + ctx.JoinIds(ids) + ") ORDER BY d.DateDocument");

using (var wb = new XLWorkbook())
{
    var ws = wb.Worksheets.Add("Documentos");
    ws.Cell(1, 1).Value = "ID"; ws.Cell(1, 2).Value = "Folio"; ws.Cell(1, 3).Value = "Fecha";
    ws.Cell(1, 4).Value = "Cliente"; ws.Cell(1, 5).Value = "Total";
    ws.Range("A1:E1").Style.Font.Bold = true;
    ws.Range("A1:E1").Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
    ws.Range("A1:E1").Style.Font.FontColor = XLColor.White;

    int r = 2;
    foreach (var f in filas)
    {
        ws.Cell(r, 1).Value = (long)f["DocumentID"];
        ws.Cell(r, 2).Value = f["Folio"] as string;
        ws.Cell(r, 3).Value = (DateTime)f["DateDocument"];
        ws.Cell(r, 4).Value = f["Cliente"] as string;
        ws.Cell(r, 5).Value = Convert.ToDouble(f["Total"]);
        ws.Cell(r, 5).Style.NumberFormat.Format = "$#,##0.00";
        r++;
    }
    ws.RangeUsed().SetAutoFilter();
    ws.Columns().AdjustToContents();

    string ruta = @"C:\BrosLMV\temp\Documentos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";
    wb.SaveAs(ruta);
    ctx.erp.RunShellExecute(ruta, "");
}
```

Con esto, **cualquier** botón futuro que necesite exportar algo a Excel usa este mismo
patrón — 15 líneas, formato real, sin pelear con XML.

---

## 3. WebView2 — el caso especial

WebView2 es distinto a los otros 3 por una razón física: trae un componente **nativo**
(`WebView2Loader.dll`, no es código .NET) además del wrapper administrado. Eso agrega
dos detalles que si se ignoran, truenan en el peor momento.

### 3.1 Arquitectura: tiene que ser la versión de 32 bits

`ComercialSP.exe` es un proceso de **32 bits** (documentado en
[`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md) §1: *"Inyectar runtimes modernos... dentro
de ComercialSP.exe (32-bit) es inviable..."* — refiriéndose a Python, que por eso corre
aparte; pero el punto de que el proceso host es 32 bits sigue aplicando aquí). El addon
(`BrosLMVClsMain.dll`) es `AnyCPU`, así que dentro de ese proceso corre como 32 bits.

**Consecuencia práctica:** hay que usar el `WebView2Loader.dll` de la carpeta
`runtimes\win-x86\native\` del paquete NuGet — **no** el de `win-x64` (ese es el que
usa Python, porque `python.exe` sí corre en 64 bits, en su propio proceso separado). Si
se pone el de 64 bits por error, la carga del componente nativo falla silenciosamente o
con un `BadImageFormatException`.

```
C:\BrosLMV\lib\
├─ Microsoft.Web.WebView2.Core.dll
├─ Microsoft.Web.WebView2.WinForms.dll
└─ WebView2Loader.dll        <- la de runtimes\win-x86\native\ del paquete NuGet
```

### 3.2 El runtime de WebView2 tiene que estar instalado en el servidor

Windows 11 lo trae de fábrica. Este servidor es Windows Server 2022 — hay que
verificar si ya está (viene con Edge en instalaciones con "Desktop Experience") o
empacar el instalador "Evergreen Standalone" / "Fixed Version" (~150-200MB, cabe sobrado
en el margen de 1-2GB ya autorizado, ver `ARQUITECTURA_V3.md` §7) dentro del instalador
de BrosLMV para no depender de que el servidor tenga internet.

### 3.3 El patrón seguro de inicialización (esto es lo que de verdad importa)

`EnsureCoreWebView2Async()` es asíncrono. Los scripts de C# corren **síncronos, en el
mismo hilo de Comercial** — bloquear ese hilo esperando a que termine (con
`.GetAwaiter().GetResult()`, el error más común) puede **congelar Comercial completo**,
no solo la ventana del script, porque WebView2 necesita que ese mismo hilo siga
respondiendo mensajes de Windows para poder terminar de inicializarse. Es el mismo tipo
de regla que ya existe para ventanas modeless en general (`UI_VENTANAS.md`): nunca
bloquear el hilo de Comercial.

**Patrón correcto — inicializar por evento, nunca esperar bloqueando:**

```csharp
#r "C:\BrosLMV\lib\Microsoft.Web.WebView2.Core.dll"
#r "C:\BrosLMV\lib\Microsoft.Web.WebView2.WinForms.dll"
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("Selecciona un documento."); return; }
long docId = ids[0];

var frm = new Form { Text = "Vista previa", Size = new Size(900, 700), StartPosition = FormStartPosition.CenterScreen };
var webView = new WebView2 { Dock = DockStyle.Fill };
frm.Controls.Add(webView);

// Carpeta propia para el perfil/caché de WebView2 -- si no se especifica, intenta usar
// la carpeta del ejecutable (Comercial, sin permiso de escritura) y falla.
var envOptions = new CoreWebView2EnvironmentOptions();
var carpetaDatos = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrosLMV", "WebView2");

frm.Load += async (s, e) => {
    try {
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: carpetaDatos);
        await webView.EnsureCoreWebView2Async(env);

        string html = "<html><body style='font-family:Segoe UI'><h1>Documento " + docId + "</h1></body></html>";
        webView.CoreWebView2.NavigateToString(html);

        // Puente JS -> C#: en la pagina, window.chrome.webview.postMessage(...)
        webView.CoreWebView2.WebMessageReceived += (s2, e2) => {
            try {
                string mensaje = e2.WebMessageAsJson;
                ctx.Log("WebView2 mensaje recibido: " + mensaje);
            } catch (Exception ex) { ctx.Log("Error en WebMessageReceived: " + ex.Message); }
        };
    } catch (Exception ex) {
        ctx.Msg("Error al iniciar WebView2: " + ex.Message, "Error");
        frm.Close();
    }
};

frm.Show();
```

Puntos clave de este patrón (para copiar siempre, no reinventar cada vez):
1. `frm.Load += async (s, e) => { ... }` — el `async` va en el **manejador de evento**,
   nunca en el cuerpo principal del script. El script termina normal, la ventana queda
   viva, y cuando el evento `Load` dispara, la inicialización corre sin bloquear nada.
2. `try/catch` alrededor de todo — igual que cualquier ventana modeless
   (`UI_VENTANAS.md` regla 2), un error aquí no debe tumbar Comercial.
3. `userDataFolder` explícito en `%AppData%\BrosLMV\WebView2` — carpeta con permiso de
   escritura garantizado, nunca la carpeta de Comercial.
4. Puente JS→C# vía `window.chrome.webview.postMessage(...)` / `WebMessageReceived`, y
   C#→JS vía `webView.CoreWebView2.ExecuteScriptAsync("...")` — es el equivalente
   exacto de `window.pywebview.api.*` que ya se usa en `REPORTE_EJECUTIVO.py`, solo que
   con nombres distintos.

### 3.4 Plantilla recomendada antes de usarlo en cualquier script real

Antes de escribir un script de negocio con WebView2, se debe crear
`PLANTILLA_BASE_CSHARP_WEBVIEW2.ctx` (mismo espíritu que
`PLANTILLA_BASE_CSHARP_WINFORMS.ctx` ya existente) con exactamente el patrón de arriba
ya probado, para que cualquier script nuevo **copie** la plantilla en vez de escribir la
inicialización desde cero cada vez. Así el riesgo de "alguien lo hizo mal" se resuelve
una sola vez, en un solo lugar.

---

## 4. Qué se vuelve posible (ejemplos concretos, no solo teoría)

- **Pantallas de captura modernas**: rehacer la captura de Pedido/OC con búsqueda de
  producto en vivo (autocompletar con imagen y existencia), en vez del
  `TextBox`+`ListBox` actual — como corre en el mismo proceso que Comercial (sin el
  "viaje" por named pipes que sí tiene Python), la reacción mientras se teclea es
  instantánea de verdad.
- **PDF con diseño propio**: `webView.CoreWebView2.PrintToPdfAsync(ruta)` convierte
  cualquier HTML/CSS diseñado a mano en un PDF real — cotizaciones con marca propia,
  reportes imprimibles, etiquetas — sin pelear con un generador de reportes tipo
  Crystal Reports.
- **Excel real desde cualquier botón**: con ClosedXML, cualquier script que hoy hace
  `ctx.Msg(sb.ToString())` con una lista en texto plano puede en cambio ofrecer "Exportar
  a Excel" con formato, autofiltro y colores, en menos de 20 líneas.
- **Integraciones**: con Newtonsoft.Json + `ctx.erp.GetWebContent`, validar RFC contra
  el webservice del SAT, consultar tipo de cambio del día, mandar una notificación a un
  webhook de WhatsApp/Slack cuando se guarda un documento grande.
- **Trazabilidad física**: QRCoder para generar códigos QR que se peguen en las
  recepciones de almacén o en las etiquetas de lote, escaneables luego con el celular.

---

## 5. Plan de implementación (orden recomendado)

1. ~~Crear `C:\BrosLMV\lib\` en esta instalación.~~ **Hecho.**
2. ~~Descargar y copiar Newtonsoft.Json.dll y QRCoder.dll — probar con un script chico
   cada uno.~~ **Hecho** (`PRUEBA_JSON.ctx`, `PRUEBA_QR.ctx`).
3. ~~Descargar ClosedXML + sus dependencias, copiar, probar con el ejemplo de exportar
   selección a Excel de §2.~~ **Hecho** (`PRUEBA_EXCEL.ctx`).
4. ~~WebView2: copiar los 3 archivos (§3.1), crear
   `PLANTILLA_BASE_CSHARP_WEBVIEW2.ctx` con el patrón de §3.3, probarlo primero con un
   HTML de prueba simple, luego con el puente JS↔C#.~~ **Hecho** (`PRUEBA_WEBVIEW2.ctx`,
   plantilla en la raíz de `scripts\`).
5. ~~Verificar que el runtime de WebView2 esté instalado en el servidor (§3.2).~~
   **Hecho** — ya estaba instalado (Edge WebView2 v149.0.4022.98,
   `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`).
   No hizo falta empacar el instalador Fixed Version.
6. **Pendiente:** el usuario ejecuta los 4 scripts de prueba dentro de la Consola real
   (F5) para confirmar en Comercial de verdad — lo que se hizo hasta aquí es la
   preparación de archivos + validación por **compilación real** contra las mismas DLLs
   en un proyecto de prueba aparte (§6), que da alta confianza pero no reemplaza probarlo
   en Comercial.
7. Documentar en `CAPACIDADES.md` §10 y en `MANUAL.md` (regla del proyecto) una vez
   confirmado en Comercial.

Ningún paso de este plan tocó `src/BrosLMV.csproj` ni usó
`build\generar_instalador.ps1` — fue agregar archivos a una carpeta y escribir/probar
scripts `.ctx`, tal como se buscaba.

---

## 6. Validación hecha antes de entregar (compilación real, no solo lectura del código)

Antes de dejar los 4 scripts de prueba listos para que el usuario los corra, se generó
un proyecto `net48` desechable (`C:\Compac\Backups\nuget_fetch\Validar.cs`) que replica
el cuerpo de cada script (con un `ctx` de mentira, mismos nombres de método) y se
compiló con `dotnet build` contra los **mismos `.dll`** que se copiaron a
`C:\BrosLMV\lib\` — **0 errores**. Esto confirma que la API de cada librería (nombres de
clase, métodos, propiedades) coincide exactamente con la versión descargada, no con una
versión distinta que el asistente pudiera recordar mal de memoria. Lo único que esta
validación **no puede probar** es el comportamiento dentro de Comercial en sí (el puente
`ctx`/`ctx.erp` real, el hilo de UI real) — por eso el paso 6 de arriba sigue pendiente
de que el usuario lo corra ahí.

---

## 7. Scripts de prueba entregados

| Archivo | Ubicación | Qué prueba |
|---|---|---|
| `PRUEBA_JSON.ctx` | `scripts\Distribuciones_Candelas\` | Newtonsoft.Json: crear/leer un objeto JSON |
| `PRUEBA_QR.ctx` | `scripts\Distribuciones_Candelas\` | QRCoder: generar un PNG de código QR y abrirlo |
| `PRUEBA_EXCEL.ctx` | `scripts\Distribuciones_Candelas\` | ClosedXML: exportar 20 facturas de venta a un `.xlsx` con formato, y abrirlo |
| `PRUEBA_WEBVIEW2.ctx` | `scripts\Distribuciones_Candelas\` | WebView2: ventana con HTML/CSS moderno + botón que manda un mensaje de JavaScript a C# |
| `PLANTILLA_BASE_CSHARP_WEBVIEW2.ctx` | `scripts\` (compartida, todas las empresas) | Mismo patrón que `PRUEBA_WEBVIEW2.ctx`, como plantilla para copiar en scripts futuros |

---

## 8. Lo que NO cambia

Todo lo que ya construimos para Distribuciones_Candelas (el dashboard, el detalle de
proyecto, la exportación a Excel del reporte) sigue funcionando exactamente igual — está
en Python, que ya tiene su propio acceso a WebView2 a través de `pywebview` desde antes.
Este plan es para dar la misma capacidad a los scripts de **C#**, no para reemplazar
nada de lo ya construido.
