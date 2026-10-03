# Caso de humo #41: el programa BrosLMV.Disenador.exe (designer\, modo --prueba sin ventana) contra el laboratorio. Comprueba que:
#   (1) editorVista da EXACTAMENTE el mismo HTML que el motor de PDF masivo para el mismo documento y formato;
#   (2) disenoValores devuelve el valor de cada etiqueta de la cabecera, los renglones de ejemplo y los atributos con etiquetas ([QRPayload]);
#   (3) con un UUID simulado, el QR de la vista es el de verificacion del SAT;
#   (4) etiquetasInfo dice de que tabla y columna sale cada etiqueta, y refEjecutar acepta SELECT con JOIN y rechaza todo lo demas.
# Devuelve 0 si paso, 1 si fallo. Solo corre contra BROSLMV_DESARROLLO. Requiere compilar designer\ (dotnet build -c Release) y PDF_MASIVO publicado en el laboratorio.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe"),
    [string]$DisenadorExe = (Join-Path $PSScriptRoot "..\..\..\designer\bin\Release\net8.0-windows\BrosLMV.Disenador.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Restaurar; exit 1 }
$script:cfdi = $null
function Restaurar {
    if ($script:cfdi) {
        $o = $script:cfdi.Valores -split '\|'
        sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocumentCFD SET CFDIFolioFiscal=$(if ($o[0] -eq '~') { 'NULL' } else { "'$($o[0])'" }), CFDISelloDigitalEmisor=$(if ($o[1] -eq '~') { 'NULL' } else { "'$($o[1])'" }) WHERE DocumentID=$($script:cfdi.Doc)" | Out-Null
        $script:cfdi = $null
    }
}
if (-not (Test-Path $DisenadorExe)) { Write-Host "  [ERROR] No existe $DisenadorExe (compila designer\ en Release)." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe" -ForegroundColor Red; exit 1 }
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue

# Una accion del Disenador sin ventana: devuelve el objeto "data" o aborta con el error
$salida = Join-Path $env:TEMP ("dis_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $salida | Out-Null
function Accion([string]$accion, $carga, [switch]$EsperaError) {
    $id = [Guid]::NewGuid().ToString('N')
    $ent = Join-Path $salida "e_$id.json"; $arch = Join-Path $salida "r_$id.json"
    [IO.File]::WriteAllText($ent, $ser.Serialize(@{ action = $accion; payload = $carga }), (New-Object Text.UTF8Encoding($false)))
    # WinExe: sin Start-Process -Wait el shell no espera al programa
    $null = Start-Process $DisenadorExe -ArgumentList '--bd', $Database, '--entrada', ('"' + $ent + '"'), '--salida', ('"' + $arch + '"') -Wait -PassThru
    if (-not (Test-Path $arch)) { Fallo "El Diseñador no escribio resultado para $accion." }
    $r = $ser.DeserializeObject((Get-Content $arch -Raw -Encoding UTF8))
    if ($EsperaError) { return $r }
    if (-not $r['ok']) { Fallo "$accion fallo: $($r['error'])" }
    return $r['data']
}

# Un documento con formato de BrosLMV: el mas reciente de un modulo con formato HTML asignado
$fila = Sql "SELECT TOP 1 CONCAT(d.DocumentID,'|',c.PrintFormatID) FROM docDocument d JOIN zzBrosFormatoConfig c ON c.ModuleID=d.ModuleID AND c.PrintFormatID IS NOT NULL JOIN engModulePrintFormat f ON f.PrintFormatID=c.PrintFormatID WHERE d.DeletedOn IS NULL AND d.ModuleID=21 ORDER BY d.DocumentID DESC"
if (-not $fila) { Fallo "El laboratorio no tiene una factura de cliente con formato BrosLMV asignado." }
$doc = [long](($fila -split '\|')[0]); $fid = [long](($fila -split '\|')[1])

$ab = Accion "editorAbrir" @{ formatId = $fid }
$html = $ab['html']
if (-not $html -or $html.Length -lt 200) { Fallo "editorAbrir debia traer el HTML del formato." }

# (1) y (2)
$vista = Accion "editorVista" @{ formatId = $fid; docId = $doc; html = $html }
if ($vista['error']) { Fallo "editorVista: $($vista['error'])" }
$v = Accion "disenoValores" @{ formatId = $fid; docId = $doc; html = $html; detalle = $true }
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
    if ((Get-Content $ref -Raw -Encoding UTF8) -ne $vista['html']) { Fallo "El HTML de la vista del Diseñador debia ser identico al de PDF masivo." }
    Write-Host "  La vista del Diseñador es identica al HTML de PDF masivo."
} else { Write-Host "  (PDF masivo no esta publicado en el laboratorio: se omite la comparacion; corre build\laboratorio\publicar_scripts_lab.ps1)" }

# (3) QR del SAT con un UUID simulado
$script:cfdi = @{ Doc = $doc; Valores = (Sql "SELECT CONCAT(ISNULL(CFDIFolioFiscal,'~'),'|',ISNULL(CFDISelloDigitalEmisor,'~')) FROM docDocumentCFD WHERE DocumentID=$doc") }
sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocumentCFD SET CFDIFolioFiscal='6B0C2F3A-1D4E-4F5A-9B7C-123456789ABC', CFDISelloDigitalEmisor='AAAABBBBCCCCDDDD12345678' WHERE DocumentID=$doc" | Out-Null
$vq = Accion "disenoValores" @{ formatId = $fid; docId = $doc; html = $html; detalle = $false }
Restaurar
$qr = [string]$vq['attrs']['[QRPayload]']
if ($qr -notmatch '^https://verificacfdi\.facturaelectronica\.sat\.gob\.mx/default\.aspx\?id=6B0C2F3A-1D4E-4F5A-9B7C-123456789ABC&re=.+&rr=.+&tt=\d{10}\.\d{6}&fe=12345678$') { Fallo "El QR de un documento timbrado debia ser el del SAT y fue: $qr" }
Write-Host "  QR de un documento timbrado: URL de verificacion del SAT."

# (4) origen de cada etiqueta y consultas de solo lectura
$info = Accion "etiquetasInfo" @{ docId = $doc }
$txt = $ser.Serialize($info)
if ($txt -notmatch 'NumeroIdentificacion' -or $txt -notmatch 'docDocumentItem') { Fallo "etiquetasInfo debia indicar el origen de [NumeroIdentificacion] (docDocumentItem.ProductKey)." }
Write-Host "  etiquetasInfo: el origen de cada etiqueta (tabla.columna) viene en la respuesta."
$ok = Accion "refEjecutar" @{ sql = "SELECT TOP 5 d.DocumentID, i.DocumentItemID, p.ProductName FROM docDocument d JOIN docDocumentItem i ON i.DocumentID=d.DocumentID JOIN orgProduct p ON p.ProductID=i.ProductID WHERE d.DocumentID={DocumentID}"; docId = $doc }
if (@($ok['filas']).Count -lt 1) { Fallo "refEjecutar debia devolver filas de un SELECT con dos JOIN." }
foreach ($malo in @("UPDATE docDocument SET Title=1", "SELECT 1; DELETE FROM docDocument", "SELECT * INTO zzx FROM docDocument", "EXEC sp_who")) {
    $r = Accion "refEjecutar" @{ sql = $malo; docId = $doc } -EsperaError
    if ($r['ok']) { Fallo "refEjecutar debia rechazar: $malo" }
}
Write-Host "  refEjecutar: SELECT con JOIN corre; UPDATE, doble consulta, INTO y EXEC se rechazan."
Remove-Item $salida -Recurse -Force -ErrorAction SilentlyContinue
exit 0
