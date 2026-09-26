# Caso de humo #16: valida PLANTILLA_REQUISICION_WEBVIEW2_CSHARP.ctx de punta a punta --
# misma logica que el archivo real, con auto-click de JS inyectado para poder probarla sin
# un humano. Confirma que ctx.ShowHtmlFormulario() (canal directo, sin pipe, agregado para
# C# junto con esta plantilla) crea una Solicitud de Compra real, igual que la version
# Python (caso 13).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_REQUISICION_WEBVIEW2_CSHARP"
$codigoPath = Join-Path $PSScriptRoot "16_requisicion_webview2_csharp.codigo.cs"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

$codigo = Get-Content $codigoPath -Raw
$codigoEscapado = $codigo -replace "'", "''"
$sqlUpsert = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = '$codigoEscapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$AppKey', 'Humo - Requisicion WebView2 (C#)', '$codigoEscapado', 1, GETDATE());
"@
$tmpSql = Join-Path $env:TEMP ("humo_upsert_" + [Guid]::NewGuid().ToString('N') + ".sql")
$sqlUpsert | Out-File $tmpSql -Encoding utf8
$salidaUpsert = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1
$exitUpsert = $LASTEXITCODE
Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($exitUpsert -ne 0) {
    Write-Host "  [ERROR] sqlcmd fallo al registrar el boton de prueba (exit $exitUpsert):" -ForegroundColor Red
    $salidaUpsert | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$qConteo = "SELECT COUNT(*) FROM docDocument WHERE DocumentTypeID=49 AND ModuleID=1040"
$nAntes = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteo -W 2>&1 | Select-Object -First 1).Trim()

$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit distinto de 0:" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$nDespues = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteo -W 2>&1 | Select-Object -First 1).Trim()
if ([int]$nDespues -ne [int]$nAntes + 1) {
    Write-Host "  [ERROR] Se esperaba 1 Requisicion nueva (antes=$nAntes, despues=$nDespues)." -ForegroundColor Red
    exit 1
}

exit 0
