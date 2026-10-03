# Caso de humo #41: el motor del Diseñador de formatos (build\disenador_formatos\motor.cs, el mismo que lleva ConfiguracionFormato.ctx) corriendo headless contra el laboratorio.
# Arma un script temporal con el motor + un cuerpo de prueba y lo corre con BrosLMV.Runner. Comprueba que:
#   (1) ResolverFormatoE da EXACTAMENTE el mismo HTML que el motor de PDF masivo para el mismo documento y formato;
#   (2) ValoresDisenoE devuelve el valor de cada etiqueta de la cabecera, los renglones de ejemplo y los atributos con etiquetas ([QRPayload]);
#   (3) con un UUID simulado, el QR es el de verificacion del SAT.
# Devuelve 0 si paso, 1 si fallo. Solo corre contra BROSLMV_DESARROLLO.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
$motor = Join-Path $PSScriptRoot "..\..\disenador_formatos\motor.cs"
$AppKey = "HUMO_DISENADOR"
function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null; exit 1 }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe" -ForegroundColor Red; exit 1 }

# Un documento con formato de BrosLMV: el mas reciente de un modulo con formato HTML asignado
$fila = Sql "SELECT TOP 1 CONCAT(d.DocumentID,'|',f.FileName) FROM docDocument d JOIN zzBrosFormatoConfig c ON c.ModuleID=d.ModuleID AND c.PrintFormatID IS NOT NULL JOIN engModulePrintFormat f ON f.PrintFormatID=c.PrintFormatID WHERE d.DeletedOn IS NULL AND d.ModuleID=21 ORDER BY d.DocumentID DESC"
if (-not $fila) { Fallo "El laboratorio no tiene una factura de cliente con formato BrosLMV asignado." }
$doc = [long](($fila -split '\|')[0]); $archivo = (($fila -split '\|', 2)[1]) -replace '\\', '/'
$salida = Join-Path $env:TEMP ("dis_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $salida | Out-Null
$salidaFwd = $salida -replace '\\', '/'

function Correr([string]$cuerpo) {
    $codigo = "// job: safe-offline`nusing System; using System.IO; using System.Text; using System.Linq; using System.Collections.Generic;`nstring S(object v) => v == null || v is DBNull ? `"`" : Convert.ToString(v);`n" + (Get-Content $motor -Raw -Encoding UTF8) + "`n" + $cuerpo
    $tmp = Join-Path $env:TEMP ("dis_" + [Guid]::NewGuid().ToString('N') + ".sql")
    "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'; INSERT INTO zzBrosScript (AppKey,Nombre,Codigo,Modulo,Activo,Modificado) VALUES ('$AppKey','tmp',N'" + ($codigo -replace "'", "''") + "',0,1,GETDATE());" | Out-File $tmp -Encoding utf8
    $o = sqlcmd -S $Server -E -d $Database -x -b -i $tmp 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    if ($ex -ne 0) { Fallo "No se pudo registrar el script de prueba: $o" }
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host ($log -join "`n"); Fallo "El Runner fallo." }
}

# (1) y (2)
Correr @"
var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
var arch = "$archivo";
string fuente = LeerArchivoFormatoE(arch);
File.WriteAllText("$salidaFwd/resuelto.html", ResolverFormatoE(fuente, $doc, Path.GetDirectoryName(arch)), new UTF8Encoding(false));
File.WriteAllText("$salidaFwd/valores.json", ser.Serialize(ValoresDisenoE(fuente, null, false, $doc, Path.GetDirectoryName(arch))), new UTF8Encoding(false));
return "OK";
"@
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue
$v = $ser.DeserializeObject((Get-Content (Join-Path $salida "valores.json") -Raw -Encoding UTF8))
if (@($v['valores'].Keys).Count -lt 15) { Fallo "Se esperaban al menos 15 etiquetas con valor de cabecera y llegaron $(@($v['valores'].Keys).Count)." }
if ($v['valores']['[EmisorRfc]'] -notmatch '^[A-Z&]{3,4}\d{6}') { Fallo "[EmisorRfc] debia traer un RFC y trajo '$($v['valores']['[EmisorRfc]'])'." }
if (@($v['filas']).Count -lt 1 -or -not $v['filas'][0].ContainsKey('[Cantidad]')) { Fallo "Los renglones de ejemplo debian traer [Cantidad]." }
if (-not $v['attrs'].ContainsKey('[QRPayload]')) { Fallo "El atributo data-qr=[QRPayload] debia resolverse." }
Write-Host ("  Valores de ejemplo: {0} etiquetas de cabecera, {1} renglon(es) de muestra, {2} atributo(s) con etiquetas." -f @($v['valores'].Keys).Count, @($v['filas']).Count, @($v['attrs'].Keys).Count)

# El mismo HTML que PDF masivo
$envPrueba = @{ ids = @($doc); soloHtml = $true; carpeta = (Join-Path $salida "masivo") -replace '\\', '/'; patron = "x"; modo = "sueltos" } | ConvertTo-Json -Compress
$env:BROSLMV_PDFM_TEST = $envPrueba; $env:BROSLMV_PDFM_OUT = (Join-Path $salida "informe.json") -replace '\\', '/'
$null = & $RunnerExe --appkey PDF_MASIVO_DOCUMENTOS --bd $Database 2>&1
Remove-Item Env:\BROSLMV_PDFM_TEST, Env:\BROSLMV_PDFM_OUT -ErrorAction SilentlyContinue
$ref = Join-Path $salida "masivo\d$doc.html"
if (Test-Path $ref) {
    if ((Get-Content $ref -Raw -Encoding UTF8) -ne (Get-Content (Join-Path $salida "resuelto.html") -Raw -Encoding UTF8)) { Fallo "El HTML del motor del diseñador debia ser identico al de PDF masivo." }
    Write-Host "  El HTML resuelto es identico al de PDF masivo."
} else { Write-Host "  (PDF masivo no esta publicado en el laboratorio: se omite la comparacion; corre build\laboratorio\publicar_scripts_lab.ps1)" }

# (3) QR del SAT con un UUID simulado
$antes = Sql "SELECT CONCAT(ISNULL(CFDIFolioFiscal,'~'),'|',ISNULL(CFDISelloDigitalEmisor,'~')) FROM docDocumentCFD WHERE DocumentID=$doc"
sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocumentCFD SET CFDIFolioFiscal='6B0C2F3A-1D4E-4F5A-9B7C-123456789ABC', CFDISelloDigitalEmisor='AAAABBBBCCCCDDDD12345678' WHERE DocumentID=$doc" | Out-Null
Correr @"
var f = CargarFilasE($doc);
File.WriteAllText("$salidaFwd/qr.txt", PayloadQrE(f[0], $doc, 0), new UTF8Encoding(false));
return "OK";
"@
$o = $antes -split '\|'
sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocumentCFD SET CFDIFolioFiscal=$(if ($o[0] -eq '~') { 'NULL' } else { "'$($o[0])'" }), CFDISelloDigitalEmisor=$(if ($o[1] -eq '~') { 'NULL' } else { "'$($o[1])'" }) WHERE DocumentID=$doc" | Out-Null
$qr = Get-Content (Join-Path $salida "qr.txt") -Raw -Encoding UTF8
if ($qr -notmatch '^https://verificacfdi\.facturaelectronica\.sat\.gob\.mx/default\.aspx\?id=6B0C2F3A-1D4E-4F5A-9B7C-123456789ABC&re=.+&rr=.+&tt=\d{10}\.\d{6}&fe=12345678$') { Fallo "El QR de un documento timbrado debia ser el del SAT y fue: $qr" }
Write-Host "  QR de un documento timbrado: URL de verificacion del SAT."
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null
Remove-Item $salida -Recurse -Force -ErrorAction SilentlyContinue
exit 0
