# Caso de humo #34 (v2.99.0): funciones nativas de Comercial agregadas a ctx.erp -- esquema, parametros por empresa,
# fecha/texto, QR, costos y (si el sandbox tiene un cobro con reparto de impuestos) recalculo NO destructivo de cobros.
# LIMITE HONESTO: la prueba destructiva (desarmar un cobro y reconstruirlo) se hizo en bases de laboratorio con datos reales
# (ver docs\SDK_FUNCIONES_NATIVAS.md); aqui el cobro solo se recalcula y se comprueba que no cambia ni lanza errores.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO34_SDK_NATIVO"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

function Upsert-Boton([string]$appKey, [string]$nombre, [string]$codigo) {
    $escapado = $codigo -replace "'", "''"
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

function Borrar-Boton([string]$appKey) {
    sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$appKey'" -W | Out-Null
}

sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosPref WHERE Usuario=999906 AND Tipo='HUMO34_RESULT'" -W | Out-Null

$codigo = Get-Content (Join-Path $PSScriptRoot "34_sdk_funciones_nativas.codigo.cs") -Raw
if (-not (Upsert-Boton $AppKey "Humo 34 - SDK funciones nativas" $codigo)) { exit 1 }
$salidaRunner = (& $RunnerExe --appkey $AppKey --bd $Database 2>&1) -join "`n"
Borrar-Boton $AppKey
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] El script fallo:" -ForegroundColor Red
    $salidaRunner | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$salida = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT Valor FROM zzBrosPref WHERE Usuario=999906 AND Tipo='HUMO34_RESULT'" -W 2>&1 | Select-Object -First 1).ToString().Trim()
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosPref WHERE Usuario=999906 AND Tipo='HUMO34_RESULT'" -W | Out-Null

if ([string]::IsNullOrWhiteSpace($salida)) {
    Write-Host "  [ERROR] No se encontro el resultado en zzBrosPref (el script no llego a escribirlo). Salida del Runner: $salidaRunner" -ForegroundColor Red
    exit 1
}
if ($salida -notmatch "FAIL=0") {
    Write-Host "  [ERROR] Alguna funcion nativa fallo. Resultado: $salida" -ForegroundColor Red
    exit 1
}
if ($salida -notmatch "PASS=(\d+)" -or [int]$Matches[1] -lt 16) {
    Write-Host "  [ERROR] Se esperaban al menos 16 comprobaciones en verde. Resultado: $salida" -ForegroundColor Red
    exit 1
}

Write-Host "  [OK] Funciones nativas de ctx.erp (esquema, parametros, fecha/texto, QR, costos, cobros). Resultado: $salida"
exit 0

