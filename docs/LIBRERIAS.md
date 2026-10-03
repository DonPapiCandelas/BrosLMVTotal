# Librerías y componentes que ya tiene BrosLMV

Qué hay **ya instalado** y listo para usar en cualquier script, para qué sirve cada cosa, un ejemplo mínimo de cómo usarla y qué más se podría agregar.
Si vas a construir un botón nuevo, empieza aquí: casi siempre ya tenemos la pieza.

> **Cómo se usa una librería en un script C#.** Se declara con `#r` en las **primeras líneas** del script (antes de los comentarios de cabecera y de los `using`):
> ```csharp
> #r "C:\BrosLMV\lib\ClosedXML.dll"
> using ClosedXML.Excel;
> ```
> El instalador deja todas en `C:\BrosLMV\lib\` (y las de apoyo del propio addon en `C:\BrosLMV\bin\`). En Python no se usan DLL: Python tiene sus propios módulos (ver al final).
> Los scripts corren en .NET Framework 4.8: solo sirven librerías compatibles con `net48` o `netstandard2.0`.

## 1. Lo que ya viene instalado

| Librería | Versión | Licencia | Para qué sirve | Dónde se usa hoy |
|---|---|---|---|---|
| **ClosedXML** (+ ExcelNumberFormat, Irony, XLParser, SixLabors.Fonts, System.IO.Packaging) | 0.102.3 | MIT | **Excel (.xlsx) con formato**: celdas con moneda y fecha reales, colores, combinadas, filtros, paneles fijos, formato condicional, imágenes, impresión | «Estado de cuenta de clientes» y «de proveedores» (libro ejecutivo) |
| **DocumentFormat.OpenXml** (SDK de Open XML) | 2.16 | MIT | Archivos de **Office a bajo nivel**: **Word (.docx)**, **PowerPoint (.pptx)** y Excel. Sirve para generar contratos, cartas de cobranza, propuestas o presentaciones ejecutivas. También es el validador oficial que usan las pruebas | Base de ClosedXML; el humo #42 lo usa para validar los libros |
| **QRCoder** | 1.6 | MIT | **Códigos QR** como imagen (SAT, pagos, enlaces) | PDF de documentos (QR de verificación del SAT) |
| **Newtonsoft.Json** | 13.0.3 | MIT | **JSON**: leer/escribir objetos, consumir APIs | Disponible para scripts |
| **MailKit / MimeKit** (+ BouncyCastle.Cryptography) | 4.18.1 | MIT | **Correo**: enviar y leer (SMTP, IMAP, POP3), HTML, adjuntos, OAuth, TLS moderno. Más robusto que `System.Net.Mail` | Disponible para scripts (estados de cuenta por correo, avisos de cobranza) |
| **PDFsharp** (+ PdfSharp.\* de apoyo) | 6.2.4 | MIT | **PDF por código**: dibujar texto/líneas/imágenes, **unir, partir, reordenar, marcar de agua**, proteger con contraseña | Disponible para scripts |
| **MigraDoc** (DocumentObjectModel, Rendering, RtfRendering) | 6.2.4 | MIT | **Reportes en PDF/RTF con párrafos, tablas, encabezados y pies** sin escribir HTML; trabaja sobre PDFsharp | Disponible para scripts |
| **ZXing.Net** (`zxing`, `zxing.presentation`) | 0.16.11 | Apache-2.0 | **Códigos de barras** (Code128, EAN, ITF…) y lectura de QR/barras en imágenes | Disponible para scripts |
| **CsvHelper** | 33.1.0 | MS-PL / Apache-2.0 | **CSV** grandes: leer y escribir con comillas, separadores y tipos | Disponible para scripts |
| **WebView2** (Core, WinForms, Wpf + `WebView2Loader`) | 1.0.2739 (lib) / 1.0.2792 (bin) | Redistribuible de Microsoft | **Ventanas HTML modernas** (CSS, JavaScript, gráficas en canvas) y **HTML → PDF** | Todas las ventanas de plantillas, Diseñador, «PDF masivo», Cotizador |
| **System.Data.SQLite** | 1.0.118 | Dominio público | Base de datos **local** en un archivo (bitácoras, cachés, datos propios sin tocar SQL Server) | Núcleo de BrosLMV |
| **Roslyn** (Microsoft.CodeAnalysis) | 4.8 | MIT | Compila y ejecuta los scripts C# | Núcleo |
| **ScintillaNET** | 3.6.3 | MIT | Editor de código con colores | Consola de scripts |
| **Google.Protobuf** | 3.28 | BSD-3 | Comunicación con el host de Python | Núcleo |
| `xlsx.bundle.js` (`C:\BrosLMV\lib\dashboard\`) | 1.2.0-beta | Apache-2.0 | Excel desde el navegador embebido (JavaScript). **Ya no se usa en los estados de cuenta** (el Excel se arma en C#), pero sigue disponible para `ctx.dashboard()` | `ctx.dashboard()` |

Todas son compatibles con distribuir BrosLMV bajo GPL-3.0.

### Componentes propios de BrosLMV (programas aparte)

| Componente | Dónde queda | Qué hace |
|---|---|---|
| **BrosLMV.Runner** | `C:\BrosLMV\runner\` | Corre un script **sin ventanas** desde la línea de comandos (`--appkey`, `--bd`); es lo que usan las pruebas y cualquier integración externa. El script debe declarar `// job: safe-offline` |
| **BrosLMV.HtmlToPdf** | `C:\BrosLMV\htmlpdf\` | Convierte HTML a PDF con el motor de Edge (WebView2) |
| **BrosLMV.Disenador** | `C:\BrosLMV\disenador\` | Diseñador visual de formatos; autocontenido (no requiere instalar .NET) |
| **Host v3.0 + Python** | `C:\BrosLMV\host\`, `workers\python\` | Ejecuta los scripts de Python con el módulo `broslmv` |

## 2. Ejemplos listos para copiar

**Excel con formato (ClosedXML).** El generador completo está en `build/saldos/excel.cs.part` (tarjetas, encabezados, gráficas como imagen, impresión); este es el mínimo:
```csharp
#r "C:\BrosLMV\lib\ClosedXML.dll"
using ClosedXML.Excel;

using (var wb = new XLWorkbook())
{
    var ws = wb.Worksheets.Add("Ventas");
    ws.Cell(1, 1).Value = "Cliente";  ws.Cell(1, 2).Value = "Total";
    ws.Cell(2, 1).Value = "ACME";     ws.Cell(2, 2).Value = 1234.5;
    ws.Range("A1:B1").Style.Font.Bold = true;
    ws.Range("A1:B1").Style.Fill.BackgroundColor = XLColor.FromHtml("#15324F");
    ws.Range("A1:B1").Style.Font.FontColor = XLColor.White;
    ws.Column(2).Style.NumberFormat.Format = "\"$\"#,##0.00";
    ws.Columns().AdjustToContents();
    wb.SaveAs(@"C:\BrosLMV\temp\ventas.xlsx");
}
```
> Trampa conocida de ClosedXML 0.102: `celda.Value` no acepta `object` (convierte a `DateTime`, `double` o `string` antes) y los estilos no se encadenan después de `SetFontSize(...)` (pon cada propiedad en su propia instrucción).

**Código QR (QRCoder).**
```csharp
#r "C:\BrosLMV\lib\QRCoder.dll"
using QRCoder;
using (var gen = new QRCodeGenerator())
using (var datos = gen.CreateQrCode("https://ejemplo.com", QRCodeGenerator.ECCLevel.Q))
{
    byte[] png = new PngByteQRCode(datos).GetGraphic(8);      // bytes de una imagen PNG
    System.IO.File.WriteAllBytes(@"C:\BrosLMV\temp\qr.png", png);
}
```

**JSON (Newtonsoft).**
```csharp
#r "C:\BrosLMV\lib\Newtonsoft.Json.dll"
using Newtonsoft.Json;
var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>("{\"a\":1}");
string txt = JsonConvert.SerializeObject(obj, Formatting.Indented);
```

**Word (.docx) con el SDK de Open XML.** Más verboso que ClosedXML pero permite todo; para documentos con formato fijo conviene partir de una plantilla `.docx` y reemplazar marcadores:
```csharp
#r "C:\BrosLMV\lib\DocumentFormat.OpenXml.dll"
#r "C:\BrosLMV\lib\System.IO.Packaging.dll"
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using (var doc = WordprocessingDocument.Create(@"C:\BrosLMV\temp\carta.docx", WordprocessingDocumentType.Document))
{
    var main = doc.AddMainDocumentPart();
    main.Document = new Document(new Body(new Paragraph(new Run(new Text("Estimado cliente: …")))));
    main.Document.Save();
}
```

**Enviar un correo con adjunto (MailKit).** La cuenta y la clave no van escritas en el script: se leen de la configuración (en «Configuración de formato → Correo» ya existe una cuenta SMTP propia con la clave cifrada).
```csharp
#r "C:\BrosLMV\lib\MimeKit.dll"
#r "C:\BrosLMV\lib\MailKit.dll"
#r "C:\BrosLMV\lib\BouncyCastle.Cryptography.dll"
using MimeKit; using MailKit.Net.Smtp; using MailKit.Security;

var msg = new MimeMessage();
msg.From.Add(new MailboxAddress("Mi empresa", "cobranza@miempresa.com"));
msg.To.Add(new MailboxAddress("Cliente", "cliente@ejemplo.com"));
msg.Subject = "Estado de cuenta";
var cuerpo = new BodyBuilder { HtmlBody = "<p>Adjunto su estado de cuenta.</p>" };
cuerpo.Attachments.Add(@"C:\BrosLMV\temp\estado.xlsx");
msg.Body = cuerpo.ToMessageBody();
using (var smtp = new SmtpClient())
{
    smtp.Connect("smtp.office365.com", 587, SecureSocketOptions.StartTls);
    smtp.Authenticate(usuario, clave);           // de la configuración, nunca escritos a mano
    smtp.Send(msg); smtp.Disconnect(true);
}
```

**PDF por código (PDFsharp y MigraDoc).**
```csharp
#r "C:\BrosLMV\lib\PdfSharp.dll"
#r "C:\BrosLMV\lib\MigraDoc.DocumentObjectModel.dll"
#r "C:\BrosLMV\lib\MigraDoc.Rendering.dll"
PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;      // usar las fuentes de Windows (obligatorio en esta versión)

// Reporte con párrafos y tabla (MigraDoc)
var doc = new MigraDoc.DocumentObjectModel.Document(); var sec = doc.AddSection();
sec.AddParagraph("Reporte de cobranza");
var t = sec.AddTable(); t.AddColumn("6cm"); t.AddColumn("3cm");
var f = t.AddRow(); f.Cells[0].AddParagraph("Cliente"); f.Cells[1].AddParagraph("1,234.50");
var r = new MigraDoc.Rendering.PdfDocumentRenderer { Document = doc }; r.RenderDocument(); r.PdfDocument.Save(@"C:\BrosLMV\temp\reporte.pdf");

// Unir dos PDF (PDFsharp)
var salida = new PdfSharp.Pdf.PdfDocument();
foreach (var ruta in new[] { @"C:\a.pdf", @"C:\b.pdf" })
    foreach (PdfSharp.Pdf.PdfPage pag in PdfSharp.Pdf.IO.PdfReader.Open(ruta, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).Pages) salida.AddPage(pag);
salida.Save(@"C:\unido.pdf");
```

**Código de barras (ZXing.Net).**
```csharp
#r "C:\BrosLMV\lib\zxing.dll"
#r "C:\BrosLMV\lib\zxing.presentation.dll"
var w = new ZXing.BarcodeWriter { Format = ZXing.BarcodeFormat.CODE_128, Options = new ZXing.Common.EncodingOptions { Width = 300, Height = 80 } };
using (var bmp = w.Write("ABC-12345")) bmp.Save(@"C:\BrosLMV\temp\barras.png");
```

**CSV (CsvHelper).**
```csharp
#r "C:\BrosLMV\lib\CsvHelper.dll"
using (var lector = new System.IO.StreamReader(@"C:\datos.csv"))
using (var csv = new CsvHelper.CsvReader(lector, System.Globalization.CultureInfo.InvariantCulture))
    foreach (var fila in csv.GetRecords<dynamic>()) { /* fila.Cliente, fila.Total … */ }
```

**Ventana HTML que no bloquea Comercial.** `ctx.ShowHtmlModeless(html, titulo, ancho, alto, alMensaje)`; el patrón completo está en cualquier plantilla de fábrica (por ejemplo `ESTADO_CUENTA_CLIENTES.ctx`). Las páginas de más de ~1.5 MB se cargan desde un archivo temporal automáticamente.

**Pruebas sin ventanas.** Cualquier script marcado `// job: safe-offline` corre con `BrosLMV.Runner.exe --appkey X --bd BASE`; las plantillas de fábrica traen un «modo de pruebas» por variables de entorno (ver su documentación y `build/humo/casos`).

## 3. Python
Los scripts de Python corren en el host con el módulo `broslmv` (`ctx.query`, `ctx.erp`, `ctx.show_html`…). Python incluye su biblioteca estándar (`csv`, `json`, `sqlite3`, `datetime`…). Ver [`PYTHON.md`](PYTHON.md).

## 4. Qué más se podría agregar (candidatas)

Ninguna está instalada todavía. Antes de agregar una: licencia compatible con GPL-3.0, que sea `net48`/`netstandard2.0`, y que valga el peso (cada una engorda el instalador).

| Necesidad | Candidata | Licencia | Comentario |
|---|---|---|---|
| **Gráficas interactivas en ventanas HTML** | **Chart.js** o **Apache ECharts** (JavaScript) | MIT / Apache-2.0 | Se cargarían desde `C:\BrosLMV\lib\dashboard\` como `xlsx.bundle.js`; sin DLL |
| **Leer facturas escaneadas** | **Tesseract (OCR)** | Apache-2.0 | Pesado; solo con un caso concreto |
| **Excel de solo lectura muy grandes** | **ExcelDataReader** | MIT | Importar archivos de miles de filas |
| **Plantillas de Word con marcadores** | **OpenXmlPowerTools** o **DocX** | MIT | Cartas y contratos sin tocar XML |

> **Aviso de convivencia.** Los scripts corren dentro de Comercial (un proceso de 32 bits que ya tiene cargadas otras bibliotecas). Las DLL de apoyo (`System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`…) vienen en una sola versión coherente en `lib\`. El humo #43 prueba las librerías juntas con el Runner; **la primera vez que uses una nueva dentro de Comercial, pruébala allí** (un conflicto de versiones se vería como «no se puede cargar el ensamblado»).

### Power BI
Power BI no necesita una DLL en BrosLMV; hay tres caminos, de más a menos práctico:
1. **Conexión directa a SQL Server** desde Power BI Desktop. Lo ideal es dejar **vistas con nombres claros** (por ejemplo `vwBrosCuentasPorCobrar`, `vwBrosCuentasPorPagar`, `vwBrosInventario`) para que quien arma tableros no tenga que conocer las tablas de Comercial. *Pendiente de hacer: las vistas y su documentación.*
2. **Archivos en una carpeta**: un botón deja un `.xlsx` o `.csv` en una ruta fija (por ejemplo `C:\BrosLMV\Exportaciones\`) y Power BI la refresca. Sirve a quien no tiene acceso a SQL.
3. Un archivo `.pbix` **no** se puede generar por código; solo se conecta a las fuentes anteriores.

## 5. Cómo agregar una librería nueva

1. Agrégala a la lista de `build/descargar_librerias_externas.ps1` y córrelo (requiere .NET SDK e internet): deja los DLL (con sus dependencias) en `instalador\lib`.
2. Comprueba la **licencia** y, si no es MIT/BSD/Apache, no la incluyas sin preguntar. Anota el aviso de terceros junto a ella (como `lib\dashboard\NOTICE.md`).
3. Prueba que un script con su `#r` compila y corre con `BrosLMV.Runner` (ver el humo #42 como modelo).
4. Regenera el instalador (`build\generar_instalador.ps1` y `generar_exes.ps1`; el instalador copia `lib\` completo a `C:\BrosLMV\lib\`).
5. **Documéntala aquí** (tabla de la sección 1, con un ejemplo) y anótala en el CHANGELOG.
