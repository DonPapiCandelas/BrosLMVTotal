# Caso de humo #21: valida PLANTILLA_CANCELAR_DOCUMENTO_SQL_PURO.sql contra el sandbox.
# A diferencia de los casos que crean documentos, este valida un flujo completo de 3 pasos:
# 1) crea una Orden de Compra real (PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql), 2) crea una
# Recepción de Compra derivada de ella (PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql, con
# SourceDocumentID apuntando a la OC -- una cadena real de 2 documentos), y 3) cancela la
# cadena completa apuntando SOLO al documento raíz (la OC) con la plantilla nueva --
# confirma que AMBOS documentos quedan cancelados y AMBAS filas de kardex quedan marcadas
# Cancelled=1 (no solo la del documento raíz, que es justo el defecto que tenía el SP de
# referencia zLCCANCELADOC -- ver entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"

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

# 1) Crear una OC real con la plantilla SQL puro (módulo 183).
# v2.77.0: los parametros de la plantilla ahora son tokens {DATOS:Tabla.Columna:*} (formulario
# automatico) -- el Runner headless no los resuelve, se sustituyen a mano por los MISMOS
# valores default que antes traia el archivo.
$codigoOC = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql") -Raw
$codigoOC = $codigoOC -replace '\{DATOS:orgBusinessEntity\.BusinessEntityID[^}]*\}', '2'
$codigoOC = $codigoOC -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$codigoOC = $codigoOC -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', '1'
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '2'
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '80'
$codigoOC = $codigoOC -replace '\{DATOS:docDocument\.PaymentTermID[^}]*\}', '4'
$codigoOC = "-- job: safe-offline`n" + $codigoOC
if (-not (Upsert-Boton "HUMO20_OC" "Humo 20 - OC" $codigoOC)) { exit 1 }
$salidaOC = & $RunnerExe --appkey HUMO20_OC --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC:" -ForegroundColor Red; $salidaOC | ForEach-Object { Write-Host "    $_" }; Borrar-Boton "HUMO20_OC"; exit 1 }
$docOC = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183" -W 2>&1 | Select-Object -First 1).Trim()
$itemOC = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docOC" -W 2>&1 | Select-Object -First 1).Trim()

# 2) Crear una Recepción derivada de esa OC (módulo 184) -- cadena real de 2 documentos.
$plantillaRecepcion = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql") -Raw
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$docOC"
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:docDocumentItem\.DocumentItemID[^}]*\}', "$itemOC"
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', '1'
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '7'
$plantillaRecepcion = $plantillaRecepcion -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '250'
$plantillaRecepcion = $plantillaRecepcion -replace "DECLARE @lote\s+NVARCHAR\(50\) = 'LOTE-HUMO-01';", "DECLARE @lote            NVARCHAR(50) = NULL;"
$plantillaRecepcion = $plantillaRecepcion -replace "DECLARE @caducidad\s+DATE = '2027-12-31';", "DECLARE @caducidad       DATE = NULL;"
$codigoRecepcion = "-- job: safe-offline`n" + $plantillaRecepcion
if (-not (Upsert-Boton "HUMO20_RECEPCION" "Humo 20 - Recepcion" $codigoRecepcion)) { Borrar-Boton "HUMO20_OC"; exit 1 }
$salidaRec = & $RunnerExe --appkey HUMO20_RECEPCION --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la Recepcion:" -ForegroundColor Red; $salidaRec | ForEach-Object { Write-Host "    $_" }; Borrar-Boton "HUMO20_OC"; Borrar-Boton "HUMO20_RECEPCION"; exit 1 }
$docRec = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184 AND SourceDocumentID=$docOC" -W 2>&1 | Select-Object -First 1).Trim()

# 3) Cancelar SOLO el documento raiz (la OC) con la plantilla nueva -- debe cascadear a la Recepcion.
$plantillaCancelar = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_CANCELAR_DOCUMENTO_SQL_PURO.sql") -Raw
$plantillaCancelar = $plantillaCancelar -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$docOC"
$plantillaCancelar = $plantillaCancelar -replace '\{DATOS:docDocument\.CancellationReasonID[^}]*\}', '1'
$codigoCancelar = "-- job: safe-offline`n" + $plantillaCancelar
if (-not (Upsert-Boton "HUMO20_CANCELAR" "Humo 20 - Cancelar" $codigoCancelar)) { Borrar-Boton "HUMO20_OC"; Borrar-Boton "HUMO20_RECEPCION"; exit 1 }
$salidaCancel = & $RunnerExe --appkey HUMO20_CANCELAR --bd $Database 2>&1
$exitCancel = $LASTEXITCODE
Borrar-Boton "HUMO20_OC"; Borrar-Boton "HUMO20_RECEPCION"; Borrar-Boton "HUMO20_CANCELAR"
if ($exitCancel -ne 0) { Write-Host "  [ERROR] La plantilla de cancelacion fallo:" -ForegroundColor Red; $salidaCancel | ForEach-Object { Write-Host "    $_" }; exit 1 }

# 4) Confirmar: AMBOS documentos cancelados, AMBAS filas de kardex marcadas Cancelled=1.
$cancelados = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT COUNT(*) FROM docDocument WHERE DocumentID IN ($docOC,$docRec) AND CancelledOn IS NOT NULL" -W 2>&1 | Select-Object -First 1).Trim()
if ($cancelados -ne "2") { Write-Host "  [ERROR] Se esperaba que 2 documentos quedaran cancelados (OC $docOC + Recepcion $docRec), se encontraron $cancelados." -ForegroundColor Red; exit 1 }

$kardexActivo = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID IN ($docOC,$docRec) AND Cancelled=0" -W 2>&1 | Select-Object -First 1).Trim()
if ($kardexActivo -ne "0") { Write-Host "  [ERROR] Se esperaba 0 filas de kardex activas (Cancelled=0) tras cancelar, se encontraron $kardexActivo." -ForegroundColor Red; exit 1 }

$kardexTotal = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID IN ($docOC,$docRec)" -W 2>&1 | Select-Object -First 1).Trim()
if ($kardexTotal -ne "2") { Write-Host "  [ERROR] Se esperaban 2 filas de kardex en total (1 por documento), se encontraron $kardexTotal." -ForegroundColor Red; exit 1 }

exit 0
