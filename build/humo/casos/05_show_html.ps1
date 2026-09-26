# Caso de humo T4.1 #5: ctx.show_html() headless (WebView2 no-modal, no bloquea -- ver el
# comentario en el .codigo.py). Se valida que el Runner responda RAPIDO (bien por debajo del
# timeout de seguridad de 120s) como señal extra de que no se quedó esperando una ventana
# que nadie va a cerrar -- no solo "exit 0".
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_SHOW_HTML"
$codigoPath = Join-Path $PSScriptRoot "05_show_html.codigo.py"
$LimiteSegundos = 30

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
    VALUES ('$AppKey', 'Humo T4.1 - ctx.show_html()', '$codigoEscapado', 1, GETDATE());
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
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
$sw.Stop()
$exitRunner = $LASTEXITCODE
if ($exitRunner -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit $exitRunner :" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}
if ($sw.Elapsed.TotalSeconds -gt $LimiteSegundos) {
    Write-Host "  [ERROR] Tardo $([int]$sw.Elapsed.TotalSeconds)s (limite $LimiteSegundos s) -- probable senal de que se quedo esperando la ventana." -ForegroundColor Red
    exit 1
}

$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-python' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey despues de correr (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
