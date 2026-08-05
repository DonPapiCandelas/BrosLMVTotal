# Caso de humo #33: HostClient.ExisteColumnaReal (v2.79.0) -- metodo publico extraido de
# ResolverFormularioTokens y reutilizado por Consola.cs (doble clic en el panel de
# referencias, pestana SQL) para decidir si una columna del grid activo es una columna REAL
# de docDocument (token nuevo con formulario) o un alias de vista sin respaldo (token viejo
# de solo lectura). Corre el mismo metodo contra el sandbox real, sin duplicar el SQL.
#
# LIMITE HONESTO: el doble clic real sobre el ListView _lstSeleccion de Consola.cs es
# interaccion de WinForms, no automatizable headless con las herramientas de este proyecto
# (mismo limite documentado en los casos 25 y 32) -- se valida por inspeccion de codigo que
# arma el string correcto en cada rama, y aqui se prueba de punta a punta el metodo del que
# depende esa decision.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO33_EXISTE_COLUMNA"

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

sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosPref WHERE Usuario=999905 AND Tipo='HUMO33_RESULT'" -W | Out-Null

$codigo = Get-Content (Join-Path $PSScriptRoot "33_existe_columna_real.codigo.cs") -Raw
if (-not (Upsert-Boton $AppKey "Humo 33 - ExisteColumnaReal" $codigo)) { exit 1 }
$salidaRunner = (& $RunnerExe --appkey $AppKey --bd $Database 2>&1) -join "`n"
Borrar-Boton $AppKey
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] El script fallo:" -ForegroundColor Red
    $salidaRunner | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$salida = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT Valor FROM zzBrosPref WHERE Usuario=999905 AND Tipo='HUMO33_RESULT'" -W 2>&1 | Select-Object -First 1).ToString().Trim()
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosPref WHERE Usuario=999905 AND Tipo='HUMO33_RESULT'" -W | Out-Null

if ([string]::IsNullOrWhiteSpace($salida)) {
    Write-Host "  [ERROR] No se encontro el resultado en zzBrosPref (el script no llego a escribirlo). Salida del Runner: $salidaRunner" -ForegroundColor Red
    exit 1
}
if ($salida -notmatch "Title=True") {
    Write-Host "  [ERROR] Se esperaba Title=True (docDocument.Title existe). Resultado: $salida" -ForegroundColor Red
    exit 1
}
if ($salida -notmatch "DataType=nvarchar|DataType=varchar|DataType=char|DataType=nchar") {
    Write-Host "  [ERROR] Se esperaba un DataType de cadena para docDocument.Title. Resultado: $salida" -ForegroundColor Red
    exit 1
}
if ($salida -notmatch "Falsa=False") {
    Write-Host "  [ERROR] Se esperaba Falsa=False (columna inexistente). Resultado: $salida" -ForegroundColor Red
    exit 1
}

Write-Host "  [OK] ExisteColumnaReal: docDocument.Title -> True (tipo correcto), columna inexistente -> False. Resultado: $salida"
exit 0
