# Caso de humo T3.1 fase 4 / T4.1 #9: receta estrella "crear documento a partir de otro".
# Prueba la ejecucion de la receta (headless, sin UI de captura) y valida que se inserte el
# documento correctamente utilizando ctx.erp, reutilizando la EstructuraDocumento (fase 3).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_RECETA_CREAR_DOC"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red
    exit 1
}

$idProv = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO-PROV-001'" -W 2>&1 | Select-Object -First 1).Trim()
$idProd = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT ProductID FROM orgProduct WHERE ProductKey='HUMO-PROD-001'" -W 2>&1 | Select-Object -First 1).Trim()

if (-not $idProv -or -not $idProd) {
    Write-Host "  [ERROR] No se encontraron los IDs de prueba HUMO-PROV-001 o HUMO-PROD-001." -ForegroundColor Red
    exit 1
}

$codigoJson = @"
{"receta":"crear_documento_desde_otro","config":{"moduloDestino":183,"depotId":1,"businessEntityId":$idProv,"partidas":[{"productId":$idProd,"cantidad":5,"precio":250,"costo":200}]}}
"@

function Upsert-Boton([string]$appKey, [string]$nombre, [string]$json) {
    $codigoCompleto = "# job: safe-offline`n# lang: receta`n$json"
    $escapado = $codigoCompleto -replace "'", "''"
    $sql = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$appKey')
    UPDATE zzBrosScript SET Codigo = '$escapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$appKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$appKey', '$nombre', '$escapado', 1, GETDATE());
"@
    $tmp = Join-Path $env:TEMP ("humo_upsert_" + [Guid]::NewGuid().ToString('N') + ".sql")
    $sql | Out-File $tmp -Encoding utf8
    $salida = sqlcmd -S $Server -E -d $Database -i $tmp -W 2>&1
    $exit = $LASTEXITCODE
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    if ($exit -ne 0) { Write-Host "  [ERROR] sqlcmd fallo registrando $appKey (exit $exit):" -ForegroundColor Red; $salida | ForEach-Object { Write-Host "    $_" }; return $false }
    return $true
}

if (-not (Upsert-Boton $AppKey "Humo T3.1 - crear OC via receta" $codigoJson)) { exit 1 }

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

$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-receta' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
