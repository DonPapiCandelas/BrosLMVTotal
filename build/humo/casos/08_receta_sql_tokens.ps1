# Caso de humo T3.1 fase 2 / T4.1 #8: motor de recetas no-code, receta "Ejecutar SQL con
# tokens" (RecetaSqlTokens en src\Recetas.cs). Valida el camino completo: boton guardado
# como "# lang: receta" + JSON -> deteccion (HostClient.EsReceta) -> RecetasRegistro.Ejecutar
# -> delega a ctx.EjecutarSql -- Y el camino de error (receta desconocida en el JSON).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKeyOk  = "HUMO_RECETA_SQL"
$AppKeyMal = "HUMO_RECETA_MALA"
$codigoOk  = Get-Content (Join-Path $PSScriptRoot "08_receta_sql_tokens.codigo.json") -Raw

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

function Upsert-Boton([string]$appKey, [string]$nombre, [string]$codigoJson) {
    $codigoCompleto = "# job: safe-offline`n# lang: receta`n$codigoJson"
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

if (-not (Upsert-Boton $AppKeyOk "Humo T3.1 - receta sql_tokens (OK)" $codigoOk)) { exit 1 }
if (-not (Upsert-Boton $AppKeyMal "Humo T3.1 - receta desconocida (debe fallar)" '{"receta":"no_existe","config":{}}')) { exit 1 }

$antes = Get-Date

# 1) Caso exito: la receta debe correr el SQL de verdad.
$salidaOk = & $RunnerExe --appkey $AppKeyOk --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] La receta sql_tokens valida fallo (deberia pasar):" -ForegroundColor Red
    $salidaOk | ForEach-Object { Write-Host "    $_" }
    exit 1
}
$qAudit = "SELECT TOP 1 Estado FROM zzBrosAuditoria WHERE AppKey='$AppKeyOk' AND Origen='runner-receta' AND Fecha >= '$($antes.ToString('yyyy-MM-dd HH:mm:ss'))' ORDER BY id DESC"
$estadoOk = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qAudit -W 2>&1 | Select-Object -First 1).Trim()
if ($estadoOk -ne "OK") {
    Write-Host "  [ERROR] No se encontro auditoria OK para $AppKeyOk (encontrado: '$estadoOk')." -ForegroundColor Red
    exit 1
}

# 2) Caso error: receta desconocida DEBE rechazarse con el mensaje esperado, no tronar feo.
$salidaMal = & $RunnerExe --appkey $AppKeyMal --bd $Database 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [ERROR] La receta con id desconocido no debio pasar (exit 0)." -ForegroundColor Red
    exit 1
}
if (-not ($salidaMal -join "`n" -match "receta desconocida")) {
    Write-Host "  [ERROR] Fallo, pero no por el motivo esperado (receta desconocida):" -ForegroundColor Red
    $salidaMal | ForEach-Object { Write-Host "    $_" }
    exit 1
}

exit 0
