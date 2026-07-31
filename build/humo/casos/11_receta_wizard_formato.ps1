# Caso de humo #11: valida que el JSON EXACTO que produce NuevaAccionForm (el asistente
# "Nueva accion" de la Consola) sea ejecutable de verdad. A diferencia del caso 9 (que usa
# "partidas" como lista JSON anidada), el asistente guarda "partidas" como STRING con JSON
# adentro -- porque ese campo es un textbox de texto libre, no un grid editable todavia (ver
# docs/RECETAS_NOCODE.md 2.4). Bug real encontrado y corregido en src/Recetas.cs: sin este
# camino, TODA receta "crear_documento_desde_otro" guardada desde el asistente fallaba con
# "config.partidas debe ser una lista".
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_WIZARD_SIMULADO"

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

$beId = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO-PROV-001'" -W 2>&1 | Select-Object -First 1).Trim()
$prodId = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT ProductID FROM orgProduct WHERE ProductKey='HUMO-PROD-001'" -W 2>&1 | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($beId) -or [string]::IsNullOrWhiteSpace($prodId)) {
    Write-Host "  [ERROR] Faltan los fixtures HUMO-PROV-001/HUMO-PROD-001 en $Database (deberian existir de los casos 2-3)." -ForegroundColor Red
    exit 1
}

$codigoJson = (Get-Content (Join-Path $PSScriptRoot "11_receta_wizard_formato.codigo.json") -Raw) -replace '<BE>', $beId -replace '<PROD>', $prodId
$codigoCompleto = "# job: safe-offline`n# lang: receta`n$codigoJson"
$escapado = $codigoCompleto -replace "'", "''"
$sql = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = '$escapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$AppKey', 'Humo - JSON tal cual lo genera el asistente', '$escapado', 1, GETDATE());
"@
$tmp = Join-Path $env:TEMP ("humo_upsert_" + [Guid]::NewGuid().ToString('N') + ".sql")
$sql | Out-File $tmp -Encoding utf8
$salidaUpsert = sqlcmd -S $Server -E -d $Database -i $tmp -W 2>&1
$exitUpsert = $LASTEXITCODE
Remove-Item $tmp -Force -ErrorAction SilentlyContinue
if ($exitUpsert -ne 0) {
    Write-Host "  [ERROR] sqlcmd fallo al registrar el boton de prueba (exit $exitUpsert):" -ForegroundColor Red
    $salidaUpsert | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$qConteoOC = "SELECT COUNT(*) FROM docDocument WHERE DocumentTypeID=40 AND ModuleID=183"
$nAntes = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteoOC -W 2>&1 | Select-Object -First 1).Trim()

$salida = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] El JSON con forma de asistente fallo (deberia pasar tras la correccion):" -ForegroundColor Red
    $salida | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$nDespues = (sqlcmd -S $Server -E -d $Database -h -1 -Q $qConteoOC -W 2>&1 | Select-Object -First 1).Trim()
if ([int]$nDespues -ne [int]$nAntes + 1) {
    Write-Host "  [ERROR] Se esperaba 1 OC nueva (antes=$nAntes, despues=$nDespues)." -ForegroundColor Red
    exit 1
}

exit 0
