# Caso de humo T4.1 #1: BrosLMV.Runner ejecutando SQL headless contra el sandbox.
# Verifica el camino completo: registra el boton en zzBrosScript, lo corre con el Runner
# SIN Comercial abierto, y confirma que quedo auditado en zzBrosAuditoria (Origen='runner-sql').
# Devuelve 0 si paso, 1 si fallo. No imprime resumen "verde/rojo" -- eso lo hace probar_humo.ps1.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
# "Continue" (no "Stop"): este script valida exit codes a mano, no depende de excepciones --
# con "Stop", el stderr de sqlcmd/Runner (comandos nativos) se promueve a error terminante
# en PowerShell 5.1 y aborta el caso antes de poder reportarlo como rojo.
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_SQL_SMOKE"
$codigoPath = Join-Path $PSScriptRoot "01_sql_headless.codigo.sql"

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
    VALUES ('$AppKey', 'Humo T4.1 - SQL headless', '$codigoEscapado', 1, GETDATE());
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

# 2) Marca de tiempo antes de correr, para distinguir la fila de auditoria nueva de una vieja.
$antes = Get-Date

# 3) Correr el Runner de verdad, sin Comercial abierto.
$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$exitRunner = $LASTEXITCODE

if ($exitRunner -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit $exitRunner :" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

# 4) Confirmar que quedo auditado (no solo que el proceso salio en 0).
$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-sql' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()

if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
