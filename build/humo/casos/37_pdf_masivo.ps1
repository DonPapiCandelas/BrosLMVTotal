# Caso de humo #37: la plantilla de fabrica PDF_MASIVO_DOCUMENTOS.ctx corriendo headless (BrosLMV.Runner) contra el laboratorio, con el motor
# BrosLMV.HtmlToPdf.exe del repositorio (modo --lote). Registra la PLANTILLA REAL y la corre sin ventanas con BROSLMV_PDFM_TEST (JSON) y BROSLMV_PDFM_OUT.
# Escribe PDF en una carpeta temporal (no toca la base salvo registrar el boton de prueba). Comprueba: PDF sueltos, ZIP, PDF unido, ZIP+unido, patron de nombre,
# que un documento de un modulo sin formato HTML se OMITE sin detener a los demas y que los archivos son PDF validos. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe"),
    [string]$MotorExe = (Join-Path $PSScriptRoot "..\..\..\htmlpdf\bin\Release\net8.0-windows\BrosLMV.HtmlToPdf.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_PDF_MASIVO"
$plantilla = Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PDF_MASIVO_DOCUMENTOS.ctx"
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $MotorExe)) { Write-Host "  [ERROR] No existe $MotorExe -- compila el motor (dotnet build htmlpdf -c Release)." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $plantilla)) { Write-Host "  [ERROR] No existe la plantilla $plantilla" -ForegroundColor Red; exit 1 }
$MotorExe = (Resolve-Path $MotorExe).Path

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Limpiar; exit 1 }
$tmpRoot = Join-Path $env:TEMP ("humo_pdfm_" + [Guid]::NewGuid().ToString('N'))
function Limpiar { if (Test-Path $script:tmpRoot) { Remove-Item $script:tmpRoot -Recurse -Force -ErrorAction SilentlyContinue } }

# 1) Registrar la plantilla real
$codigo = (Get-Content $plantilla -Raw -Encoding UTF8) -replace "'", "''"
$tmpSql = Join-Path $env:TEMP ("humo_pdfm_" + [Guid]::NewGuid().ToString('N') + ".sql")
@"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Humo - PDF masivo', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
$out = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($ex -ne 0) { Write-Host "  [ERROR] No se pudo registrar la plantilla de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }

# 2) Documentos: los que tienen formato HTML (se esperan OK) + uno de un modulo sin formato HTML (se espera OMITIDO)
$conFormato = @((sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 5 d.DocumentID FROM docDocument d WHERE d.DeletedOn IS NULL AND EXISTS (SELECT 1 FROM engModulePrintFormat f WHERE f.ModuleID=d.ModuleID AND (f.FileName LIKE '%.html' OR f.FileName LIKE '%.htm')) ORDER BY d.DocumentID DESC" 2>&1) | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [long]$_ })
$sinFormato = Sql "SELECT TOP 1 d.DocumentID FROM docDocument d WHERE d.DeletedOn IS NULL AND NOT EXISTS (SELECT 1 FROM engModulePrintFormat f WHERE f.ModuleID=d.ModuleID AND (f.FileName LIKE '%.html' OR f.FileName LIKE '%.htm')) ORDER BY d.DocumentID DESC"
if ($conFormato.Count -lt 3) { Fallo "El laboratorio no tiene 3 documentos con formato HTML para probar." }
$ids = @($conFormato)
$esperadosOmitidos = 0
if ($sinFormato -match '^\d+$') { $ids += [long]$sinFormato; $esperadosOmitidos = 1 }
$esperadosOk = $conFormato.Count
Write-Host ("  Documentos: {0} con formato + {1} sin formato." -f $conFormato.Count, $esperadosOmitidos)

function Correr($modo, $carpeta, $patron) {
    $salida = Join-Path $env:TEMP ("pdfm_" + [Guid]::NewGuid().ToString('N') + ".json")
    $env:BROSLMV_PDFM_TEST = (@{ ids = $ids; modo = $modo; carpeta = $carpeta; patron = $patron } | ConvertTo-Json -Compress -Depth 5)
    $env:BROSLMV_PDFM_OUT = $salida; $env:BROSLMV_HTMLTOPDF_EXE = $MotorExe
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item Env:\BROSLMV_PDFM_TEST, Env:\BROSLMV_PDFM_OUT, Env:\BROSLMV_HTMLTOPDF_EXE -ErrorAction SilentlyContinue
    $m = $null; if (Test-Path $salida) { $m = Get-Content $salida -Raw -Encoding UTF8 | ConvertFrom-Json; Remove-Item $salida -Force }
    if ($code -ne 0 -or -not $m) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code) en modo $modo." }
    return $m
}
function EsPdf([string]$ruta) { if (-not (Test-Path $ruta)) { return $false }; $b = [System.IO.File]::ReadAllBytes($ruta); return ($b.Length -gt 500 -and [System.Text.Encoding]::ASCII.GetString($b, 0, 5) -eq '%PDF-') }
function Paginas([string]$ruta) { $t = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($ruta)); return ([regex]::Matches($t, '/Type\s*/Page[^s]')).Count }

# 3) PDF sueltos con un patron propio
$c1 = Join-Path $tmpRoot "sueltos"
$r = Correr "sueltos" $c1 "[Modulo] - [Folio]"
if ([int]$r.ok -ne $esperadosOk) { Fallo "Sueltos: debian salir $esperadosOk PDF y salieron $($r.ok)." }
if ([int]$r.omitidos -ne $esperadosOmitidos) { Fallo "Sueltos: debian omitirse $esperadosOmitidos documento(s) y se omitieron $($r.omitidos)." }
if ([int]$r.errores -ne 0) { Fallo "Sueltos: no debia haber errores y hubo $($r.errores)." }
$pdfs = @(Get-ChildItem $c1 -Filter *.pdf -ErrorAction SilentlyContinue)
if ($pdfs.Count -ne $esperadosOk) { Fallo "Sueltos: debia haber $esperadosOk archivos y hay $($pdfs.Count)." }
foreach ($f in $pdfs) { if (-not (EsPdf $f.FullName)) { Fallo "El archivo $($f.Name) no es un PDF valido." } }
if (@($pdfs | Select-Object -ExpandProperty Name | Sort-Object -Unique).Count -ne $pdfs.Count) { Fallo "Los nombres de los PDF deben ser unicos." }

# 4) ZIP: queda solo el ZIP, con todos los PDF dentro
$c2 = Join-Path $tmpRoot "zip"
$r = Correr "zip" $c2 "[Modulo] - [Folio]"
$zip = @(Get-ChildItem $c2 -Filter *.zip -ErrorAction SilentlyContinue)
if ($zip.Count -ne 1) { Fallo "ZIP: debia haber un archivo .zip y hay $($zip.Count)." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$entradas = [System.IO.Compression.ZipFile]::OpenRead($zip[0].FullName); $nZip = $entradas.Entries.Count; $entradas.Dispose()
if ($nZip -ne $esperadosOk) { Fallo "ZIP: debia traer $esperadosOk PDF y trae $nZip." }
if (@(Get-ChildItem $c2 -Filter *.pdf -ErrorAction SilentlyContinue).Count -ne 0) { Fallo "ZIP: no debian quedar PDF sueltos." }

# 5) PDF unido: un solo archivo con al menos una pagina por documento
$c3 = Join-Path $tmpRoot "unido"
$r = Correr "unido" $c3 "[Modulo] - [Folio]"
$unido = @(Get-ChildItem $c3 -Filter *.pdf -ErrorAction SilentlyContinue)
if ($unido.Count -ne 1) { Fallo "Unido: debia haber un solo PDF y hay $($unido.Count)." }
if (-not (EsPdf $unido[0].FullName)) { Fallo "El PDF unido no es valido." }
if ((Paginas $unido[0].FullName) -lt $esperadosOk) { Fallo "El PDF unido debia tener al menos $esperadosOk paginas y tiene $(Paginas $unido[0].FullName)." }

# 6) ZIP y unido a la vez
$c4 = Join-Path $tmpRoot "ambos"
$r = Correr "zip+unido" $c4 "[Modulo] - [Folio]"
if (@(Get-ChildItem $c4 -Filter *.zip).Count -ne 1 -or @(Get-ChildItem $c4 -Filter *.pdf).Count -ne 1) { Fallo "ZIP+unido: debia haber un ZIP y un PDF." }

Limpiar
Write-Host ("  {0} PDF por lote (+{1} omitido): sueltos / ZIP / unido / ZIP+unido, todo conforme." -f $esperadosOk, $esperadosOmitidos)
exit 0
