# Caso de humo T4.1 #3: crear Orden de Compra vía ctx.erp de ESCRITURA, headless (BrosLMV.Runner).
# Primera prueba real de escrituras ctx.erp headless -- solo contra el sandbox ComercialSP,
# nunca contra una empresa de cliente real (ver CHANGELOG/PLAN_IMPLEMENTACION: sigue
# pendiente la decision de producto sobre habilitar esto en jobs programados de verdad).
# No es idempotente: cada corrida crea una OC nueva (DocumentTypeID=40, ModuleID=183) --
# se valida contando cuantas hay antes/despues, no buscando una fija.
# Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_CREAR_OC"
$codigoPath = Join-Path $PSScriptRoot "03_crear_oc.codigo.cs"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

# 1) Registrar/actualizar el boton de prueba (idempotente -- el CODIGO se sobreescribe,
#    aunque CORRERLO no lo sea: cada ejecucion crea una OC nueva).
$codigo = Get-Content $codigoPath -Raw
$codigoEscapado = $codigo -replace "'", "''"
$sqlUpsert = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = '$codigoEscapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$AppKey', 'Humo T4.1 - crear OC (C#, ctx.erp escritura)', '$codigoEscapado', 1, GETDATE());
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

# 2) Correr el Runner de verdad -- ESTA es la parte que ejercita ctx.erp de escritura headless.
$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$exitRunner = $LASTEXITCODE
if ($exitRunner -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit $exitRunner :" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

# 3) Confirmar que de verdad se creo UNA OC nueva (no solo que el proceso salio en 0).
$nDespues = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteoOC -W 2>&1 | Select-Object -First 1).Trim()
if ([int]$nDespues -ne [int]$nAntes + 1) {
    Write-Host "  [ERROR] Se esperaba 1 OC nueva (antes=$nAntes, despues=$nDespues)." -ForegroundColor Red
    exit 1
}

# 4) Confirmar tambien la auditoria (Origen='runner-csharp').
$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-csharp' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
