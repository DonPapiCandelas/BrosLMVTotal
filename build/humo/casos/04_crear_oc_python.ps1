# Caso de humo T4.1 #4: crear OC vía ctx.erp de ESCRITURA headless, canal Python.
# Contraparte del caso 3 (mismo flujo, C#) -- confirma que ctx.erp de escritura tambien
# funciona por el canal completo BrosLMV.Host.exe + UiPump, no solo en el proceso del Runner.
# No es idempotente: cada corrida crea una OC nueva. Solo contra el sandbox ComercialSP.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_CREAR_OC_PYTHON"
$codigoPath = Join-Path $PSScriptRoot "04_crear_oc_python.codigo.py"

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
    VALUES ('$AppKey', 'Humo T4.1 - crear OC (Python, ctx.erp escritura)', '$codigoEscapado', 1, GETDATE());
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

$qConteoOC = "SELECT COUNT(*) FROM docDocument WHERE DocumentTypeID=40 AND ModuleID=183"
$nAntes = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteoOC -W 2>&1 | Select-Object -First 1).Trim()
$antes = Get-Date

$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$exitRunner = $LASTEXITCODE
if ($exitRunner -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit $exitRunner :" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$nDespues = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteoOC -W 2>&1 | Select-Object -First 1).Trim()
if ([int]$nDespues -ne [int]$nAntes + 1) {
    Write-Host "  [ERROR] Se esperaba 1 OC nueva (antes=$nAntes, despues=$nDespues)." -ForegroundColor Red
    exit 1
}

$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-python' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
