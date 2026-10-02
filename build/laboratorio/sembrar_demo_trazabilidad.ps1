# Siembra en el laboratorio (BROSLMV_DESARROLLO) los datos de demostración de la plantilla «Trazabilidad del documento»:
# 3 órdenes de compra -> 1 factura de compra consolidada, con el vínculo de encabezado único (SourceDocumentID de la primera OC) y el
# DestinationDocumentID escrito en cada OC (el caso «varios orígenes -> un destino»). Los documentos se encuentran en Comercial por su título «DEMO TRAZ…».
# Idempotente: si ya existen, no crea nada. Solo escribe en el laboratorio; se niega a correr contra otra base.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "Esta siembra solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $RunnerExe)) { Write-Host "No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red; exit 1 }

$AppKey = "LAB_SEMBRAR_TRAZABILIDAD"
$codigo = (Get-Content (Join-Path $PSScriptRoot "demo_trazabilidad.codigo.cs") -Raw -Encoding UTF8) -replace "'", "''"
$tmp = Join-Path $env:TEMP ("lab_traz_" + [Guid]::NewGuid().ToString('N') + ".sql")
@"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey') UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Laboratorio - sembrar demo de trazabilidad', N'$codigo', 1, GETDATE());
"@ | Out-File $tmp -Encoding utf8
sqlcmd -S $Server -E -d $Database -i $tmp -W | Out-Null
Remove-Item $tmp -Force -ErrorAction SilentlyContinue

$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$code = $LASTEXITCODE
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null
if ($code -ne 0) { Write-Host "El Runner fallo (exit $code):" -ForegroundColor Red; $salida | ForEach-Object { Write-Host "  $_" }; exit 1 }
$salida | ForEach-Object { Write-Host $_ }
sqlcmd -S $Server -E -d $Database -W -s"|" -Q "SET NOCOUNT ON; SELECT DocumentID, ModuleID, SourceDocumentID, DestinationDocumentID, Total, Title FROM docDocument WHERE Title LIKE 'DEMO TRAZ%' AND DeletedOn IS NULL ORDER BY DocumentID"
exit 0
