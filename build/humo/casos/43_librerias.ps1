# Caso de humo #43: las librerias externas de instalador\lib (docs\LIBRERIAS.md) cargan y funcionan dentro de un script C# real corrido por BrosLMV.Runner:
#   ClosedXML (xlsx), PDFsharp y MigraDoc (PDF), MailKit/MimeKit (armar un correo con adjunto), ZXing.Net (codigo de barras), CsvHelper (CSV), QRCoder (QR) y Newtonsoft.Json.
# Registra un script temporal que referencia las DLL con #r desde instalador\lib (o C:\BrosLMV\lib con -DesdeInstalado) y devuelve "OK ..." si todo paso.
# Solo corre contra BROSLMV_DESARROLLO. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe"),
    [switch]$DesdeInstalado
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $RunnerExe)) { Fallo "No existe $RunnerExe" }
$lib = if ($DesdeInstalado) { "C:\BrosLMV\lib" } else { (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\instalador\lib")).Path }
$salida = Join-Path $env:TEMP ("lib43_" + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $salida | Out-Null
$salidaFwd = $salida -replace '\\', '/'
$libFwd = $lib -replace '\\', '/'
$AppKey = "HUMO_LIBRERIAS"

$codigo = @"
// job: safe-offline
#r "$libFwd/ClosedXML.dll"
#r "$libFwd/PdfSharp.dll"
#r "$libFwd/MigraDoc.DocumentObjectModel.dll"
#r "$libFwd/MigraDoc.Rendering.dll"
#r "$libFwd/MimeKit.dll"
#r "$libFwd/MailKit.dll"
#r "$libFwd/zxing.dll"
#r "$libFwd/zxing.presentation.dll"
#r "$libFwd/CsvHelper.dll"
#r "$libFwd/QRCoder.dll"
#r "$libFwd/Newtonsoft.Json.dll"
using System; using System.IO; using System.Linq; using System.Globalization; using System.Collections.Generic;
var res = new List<string>(); string dir = "$salidaFwd";
Func<string, string> Fin = t => { File.WriteAllText(dir + "/resultado.txt", t); return t; };

// ClosedXML
using (var wb = new ClosedXML.Excel.XLWorkbook()) { wb.Worksheets.Add("Hoja1").Cell(1, 1).Value = "hola"; wb.SaveAs(dir + "/a.xlsx"); }
if (new FileInfo(dir + "/a.xlsx").Length < 1000) return Fin("FALLA ClosedXML"); res.Add("ClosedXML");

// PDFsharp (dibujo directo)
PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
var pdf = new PdfSharp.Pdf.PdfDocument(); var pag = pdf.AddPage();
using (var g = PdfSharp.Drawing.XGraphics.FromPdfPage(pag)) g.DrawString("Prueba PDFsharp", new PdfSharp.Drawing.XFont("Arial", 18), PdfSharp.Drawing.XBrushes.Black, 50, 80);
pdf.Save(dir + "/a.pdf");
if (!File.ReadAllBytes(dir + "/a.pdf").Take(4).SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 })) return Fin("FALLA PDFsharp"); res.Add("PDFsharp");

// MigraDoc (documento con parrafos y tabla)
var doc = new MigraDoc.DocumentObjectModel.Document(); var sec = doc.AddSection(); sec.AddParagraph("Reporte MigraDoc");
var tabla = sec.AddTable(); tabla.AddColumn("5cm"); tabla.AddColumn("3cm"); var fila = tabla.AddRow(); fila.Cells[0].AddParagraph("Cliente"); fila.Cells[1].AddParagraph("1,234.50");
var rend = new MigraDoc.Rendering.PdfDocumentRenderer { Document = doc }; rend.RenderDocument(); rend.PdfDocument.Save(dir + "/b.pdf");
if (new FileInfo(dir + "/b.pdf").Length < 800) return Fin("FALLA MigraDoc"); res.Add("MigraDoc");

// MailKit / MimeKit (arma el correo con adjunto, sin enviarlo)
var msg = new MimeKit.MimeMessage(); msg.From.Add(new MimeKit.MailboxAddress("BrosLMV", "prueba@ejemplo.com")); msg.To.Add(new MimeKit.MailboxAddress("Cliente", "cliente@ejemplo.com")); msg.Subject = "Estado de cuenta";
var cuerpo = new MimeKit.BodyBuilder { HtmlBody = "<b>Adjunto su estado de cuenta</b>" }; cuerpo.Attachments.Add(dir + "/a.xlsx"); msg.Body = cuerpo.ToMessageBody();
using (var ms = new MemoryStream()) { msg.WriteTo(ms); if (ms.Length < 1000) return Fin("FALLA MimeKit"); }
using (var smtp = new MailKit.Net.Smtp.SmtpClient()) { if (smtp.IsConnected) return Fin("FALLA MailKit"); }
res.Add("MailKit");

// ZXing.Net (codigo de barras)
var bw = new ZXing.BarcodeWriter { Format = ZXing.BarcodeFormat.CODE_128, Options = new ZXing.Common.EncodingOptions { Width = 300, Height = 80 } };
using (var bmp = bw.Write("ABC-12345")) bmp.Save(dir + "/barras.png");
if (new FileInfo(dir + "/barras.png").Length < 300) return Fin("FALLA ZXing"); res.Add("ZXing");

// CsvHelper
using (var rd = new StringReader("Cliente,Total\nACME,10.5\nBeta,20\n")) using (var csv = new CsvHelper.CsvReader(rd, CultureInfo.InvariantCulture))
{ var filas = csv.GetRecords<dynamic>().ToList(); if (filas.Count != 2) return Fin("FALLA CsvHelper"); }
res.Add("CsvHelper");

// QRCoder y Newtonsoft.Json
using (var gen = new QRCoder.QRCodeGenerator()) using (var d = gen.CreateQrCode("https://ejemplo.com", QRCoder.QRCodeGenerator.ECCLevel.Q)) { if (new QRCoder.PngByteQRCode(d).GetGraphic(4).Length < 100) return Fin("FALLA QRCoder"); }
res.Add("QRCoder");
if (Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>("{\"a\":1}")["a"].ToString() != "1") return Fin("FALLA Newtonsoft");
res.Add("Newtonsoft");
return Fin("OK " + string.Join(",", res));
"@
$tmpSql = Join-Path $salida "reg.sql"
"IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey='$AppKey') UPDATE zzBrosScript SET Codigo=N'$($codigo -replace "'", "''")', Activo=1, Modificado=GETDATE() WHERE AppKey='$AppKey' ELSE INSERT INTO zzBrosScript (AppKey,Nombre,Codigo,Activo,Modificado) VALUES ('$AppKey','Humo - librerias',N'$($codigo -replace "'", "''")',1,GETDATE());" | Out-File $tmpSql -Encoding utf8
$o = sqlcmd -S $Server -E -d $Database -x -b -i $tmpSql 2>&1; if ($LASTEXITCODE -ne 0) { Fallo "No se pudo registrar el script de prueba: $o" }
$log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1; $code = $LASTEXITCODE
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null
$txt = ($log -join "`n"); if (Test-Path (Join-Path $salida "resultado.txt")) { $txt += "`n" + (Get-Content (Join-Path $salida "resultado.txt") -Raw) }
Remove-Item $salida -Recurse -Force -ErrorAction SilentlyContinue
if ($code -ne 0 -or $txt -notmatch 'OK ClosedXML,PDFsharp,MigraDoc,MailKit,ZXing,CsvHelper,QRCoder,Newtonsoft') { Write-Host $txt; Fallo "Las librerias no funcionaron juntas (exit $code)." }
Write-Host ("  Librerias desde {0}: ClosedXML, PDFsharp, MigraDoc, MailKit/MimeKit, ZXing.Net, CsvHelper, QRCoder y Newtonsoft cargan y funcionan juntas." -f $lib)
exit 0
