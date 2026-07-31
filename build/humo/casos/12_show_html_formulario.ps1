# Caso de humo #12: valida el canal de 2 vias de ctx.show_html_formulario() de punta a
# punta -- proto (esperar_respuesta/html_response) -> HostClient.RenderUiHtml (addon) ->
# RelayingCallbackSink (host) -> PythonProcess -> ctx.py. La pagina HTML se auto-envia via
# JS (window.chrome.webview.postMessage), sin necesitar un humano.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_SHOW_HTML_FORMULARIO"
$codigoPath = Join-Path $PSScriptRoot "12_show_html_formulario.codigo.py"

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
    VALUES ('$AppKey', 'Humo - ctx.show_html_formulario()', '$codigoEscapado', 1, GETDATE());
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
$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] Runner devolvio exit distinto de 0:" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKey' AND Origen='runner-python' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoAudit = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoAudit -ne "OK") {
    Write-Host "  [ERROR] No se encontro fila de auditoria OK para $AppKey (encontrado: '$estadoAudit')." -ForegroundColor Red
    exit 1
}

exit 0
