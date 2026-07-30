# Caso de humo T4.1 #2: alta de producto headless via Python (BrosLMV.Runner + BrosLMV.Host.exe).
# A diferencia del caso 1 (SQL), este ejercita el canal Python completo (UiPump + host fuera
# de proceso) escribiendo un catalogo real (orgProduct), no solo un SELECT.
# Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_ALTA_PRODUCTO"
$codigoPath = Join-Path $PSScriptRoot "02_alta_producto.codigo.py"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

# 1) Registrar/actualizar el boton de prueba (idempotente).
$codigo = Get-Content $codigoPath -Raw
$codigoEscapado = $codigo -replace "'", "''"
$sqlUpsert = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = '$codigoEscapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$AppKey', 'Humo T4.1 - alta de producto (Python)', '$codigoEscapado', 1, GETDATE());
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

$antes = Get-Date

# 2) Correr el Runner de verdad (dispara BrosLMV.Host.exe por dentro).
$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$exitRunner = $LASTEXITCODE
if ($exitRunner -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit $exitRunner :" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

# 3) Confirmar el resultado real: el producto debe existir en orgProduct (documento dorado
#    minimo: 1 fila con ese ProductKey, no solo "el proceso no trueno").
$qProd = "SELECT COUNT(*) FROM orgProduct WHERE ProductKey = 'HUMO-PROD-001'"
$nProd = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qProd -W 2>&1 | Select-Object -First 1).Trim()
if ($nProd -ne "1") {
    Write-Host "  [ERROR] orgProduct no tiene exactamente 1 fila con ProductKey='HUMO-PROD-001' (encontrado: '$nProd')." -ForegroundColor Red
    exit 1
}

# 4) Confirmar tambien la auditoria (Origen='runner-python', igual que el caso 1 valida runner-sql).
$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-python' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
