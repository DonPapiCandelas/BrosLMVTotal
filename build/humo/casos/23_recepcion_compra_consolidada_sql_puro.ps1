# Caso de humo #23: valida la consolidación por SourceDocumentID que agregó v2.70.0 a
# PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql -- el patrón "si ya existe una Recepción para esta
# OC origen, reutilízala y solo agrega el renglón nuevo" (inspirado en LCCREAResepcioncompra1,
# ver entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md), pero usando SourceDocumentID
# como llave de correlación dedicada -- NO el campo Custom1 sobrecargado del SP original.
#
# Escenario: una OC con 2 partidas (2 productos distintos). Se corre la plantilla de
# Recepción DOS veces, una por partida, apuntando a la MISMA OC origen. Se confirma que:
#   1) Queda UN SOLO docDocument de Recepción (no dos) con SourceDocumentID=OC.
#   2) Ese único documento tiene AMBOS renglones (2 docDocumentItem).
#   3) Los totales de cabecera (SubTotal/Total/TotalTax) son la SUMA de ambas partidas, no
#      solo de la última (confirma que el UPDATE de totales tras consolidar funciona).
#   4) El guard clause anti-duplicado rechaza una 3a corrida con los mismos parámetros de la
#      2a partida (no debe crear un tercer renglón para la misma partida de OC).
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

function Exec-Q([string]$q) {
    return (sqlcmd -S $Server -E -d $Database -h -1 -Q $q -W 2>&1 | Select-Object -First 1).Trim()
}

# v2.77.0: los parametros de la plantilla ahora son tokens {DATOS:Tabla.Columna:*} (formulario
# automatico) -- el Runner headless no los resuelve, se sustituyen a mano.
function Build-CodigoRecepcion([int]$sourceOC, [int]$sourceItemId, [int]$productoID, [decimal]$cantidad, [decimal]$precio) {
    $plantilla = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql") -Raw
    $plantilla = $plantilla -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$sourceOC"
    $plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.DocumentItemID[^}]*\}', "$sourceItemId"
    $plantilla = $plantilla -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
    $plantilla = $plantilla -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', "$productoID"
    $plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', "$cantidad"
    $plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', "$precio"
    $plantilla = $plantilla -replace "DECLARE @lote\s+NVARCHAR\(50\) = 'LOTE-HUMO-01';", "DECLARE @lote            NVARCHAR(50) = NULL;"
    $plantilla = $plantilla -replace "DECLARE @caducidad\s+DATE = '2027-12-31';", "DECLARE @caducidad       DATE = NULL;"
    return "-- job: safe-offline`n" + $plantilla
}

# 1) Crear una OC real con la plantilla SQL puro (módulo 183) -- producto 1, cantidad 2, precio 80.
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
if (-not (Upsert-Boton "HUMO23_OC" "Humo 23 - OC" $codigoOC)) { exit 1 }
$salidaOC = & $RunnerExe --appkey HUMO23_OC --bd $Database 2>&1
Borrar-Boton "HUMO23_OC"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC:" -ForegroundColor Red; $salidaOC | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC1 = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docOC"
Write-Host "  OC creada: DocumentID=$docOC, partida 1 (producto 1) DocumentItemID=$itemOC1"

# 2) Agregar una SEGUNDA partida a esa MISMA OC directamente por SQL (la plantilla de OC solo
#    soporta una partida por corrida -- para este caso de humo basta con que la partida exista
#    y pertenezca a la OC, que es justo lo único que valida la plantilla de Recepción).
$sqlSegundaPartida = @"
INSERT INTO docDocumentItem (DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad, ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered, ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem)
SELECT $docOC, 3, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad, '', 5, 0.16, 50, 0, 150, 2, 1, 1, 1, 1, 1, GETDATE()
FROM orgProduct p WHERE p.ProductID = 2;
"@
sqlcmd -S $Server -E -d $Database -Q $sqlSegundaPartida -W | Out-Null
$itemOC2 = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docOC"
if ($itemOC2 -eq $itemOC1) { Write-Host "  [ERROR] No se pudo insertar la segunda partida de la OC." -ForegroundColor Red; exit 1 }
Write-Host "  Segunda partida agregada a la OC (producto 2): DocumentItemID=$itemOC2"

# 3) Primera corrida de la plantilla de Recepción -- partida 1 (producto 1, cantidad 2, precio 80).
#    No existe Recepción previa para esta OC -> debe CREAR documento nuevo.
if (-not (Upsert-Boton "HUMO23_REC1" "Humo 23 - Recepcion partida 1" (Build-CodigoRecepcion $docOC $itemOC1 1 2 80))) { exit 1 }
$r1 = (& $RunnerExe --appkey HUMO23_REC1 --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO23_REC1"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo la 1a corrida de Recepcion:" -ForegroundColor Red; $r1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r1 -notmatch "creada") { Write-Host "  [ERROR] La 1a corrida no respondio 'creada' como se esperaba. Salida: $r1" -ForegroundColor Red; exit 1 }
$docRec = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184 AND SourceDocumentID=$docOC"
Write-Host "  [OK] 1a corrida creo la Recepcion DocumentID=$docRec (partida 1)."

# 4) Segunda corrida -- partida 2 (producto 2, cantidad 3, precio 50). YA existe una Recepción
#    para esta OC -> debe CONSOLIDAR (reutilizar $docRec, no crear un documento nuevo).
if (-not (Upsert-Boton "HUMO23_REC2" "Humo 23 - Recepcion partida 2" (Build-CodigoRecepcion $docOC $itemOC2 2 3 50))) { exit 1 }
$r2 = (& $RunnerExe --appkey HUMO23_REC2 --bd $Database 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo la 2a corrida de Recepcion:" -ForegroundColor Red; $r2 | ForEach-Object { Write-Host "    $_" }; Borrar-Boton "HUMO23_REC2"; exit 1 }
if ($r2 -notmatch "CONSOLIDADA") { Write-Host "  [ERROR] La 2a corrida no respondio 'CONSOLIDADA' como se esperaba -- probablemente creo un documento nuevo en vez de reutilizar. Salida: $r2" -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
Write-Host "  [OK] 2a corrida consolido en el mismo documento (respuesta: $r2)"

# 5) Confirmar: UN SOLO docDocument de Recepcion para esta OC (no dos).
$totalRecepciones = Exec-Q "SELECT COUNT(*) FROM docDocument WHERE ModuleID=184 AND SourceDocumentID=$docOC AND DeletedOn IS NULL AND CancelledOn IS NULL"
if ($totalRecepciones -ne "1") { Write-Host "  [ERROR] Se esperaba 1 sola Recepcion para la OC $docOC, se encontraron $totalRecepciones." -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
Write-Host "  [OK] Un solo docDocument de Recepcion (DocumentID=$docRec) para la OC $docOC."

# 6) Confirmar: ese documento tiene AMBOS renglones (2 docDocumentItem).
$totalRenglones = Exec-Q "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$docRec"
if ($totalRenglones -ne "2") { Write-Host "  [ERROR] Se esperaban 2 renglones en la Recepcion $docRec, se encontraron $totalRenglones." -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
Write-Host "  [OK] La Recepcion $docRec tiene los 2 renglones (uno por partida)."

# 7) Confirmar: los totales de cabecera son la SUMA de ambas partidas.
#    Partida 1: 2 x 80 = 160. Partida 2: 3 x 50 = 150. SubTotal=310, IVA=49.60, Total=359.60.
$subTotalFinal = Exec-Q "SELECT SubTotal FROM docDocument WHERE DocumentID=$docRec"
$totalFinal = Exec-Q "SELECT Total FROM docDocument WHERE DocumentID=$docRec"
$ivaFinal = Exec-Q "SELECT TotalTax FROM docDocument WHERE DocumentID=$docRec"
# Comparacion numerica redondeada a 2 decimales (no string): sqlcmd -h -1 a veces recorta
# ceros finales (ej. "310.0" en vez de "310.00"), y columnas de impuestos como IVA_T son
# FLOAT (no DECIMAL) -- acumulan residuo de punto flotante (ej. "49.600000000000001") aunque
# el valor logico sea correcto. Redondear antes de comparar evita falsos negativos por esto.
if ([Math]::Round([decimal]$subTotalFinal, 2) -ne [decimal]"310.00") { Write-Host "  [ERROR] SubTotal esperado 310.00, se encontro $subTotalFinal." -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
if ([Math]::Round([decimal]$ivaFinal, 2) -ne [decimal]"49.60") { Write-Host "  [ERROR] TotalTax esperado 49.60, se encontro $ivaFinal." -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
if ([Math]::Round([decimal]$totalFinal, 2) -ne [decimal]"359.60") { Write-Host "  [ERROR] Total esperado 359.60, se encontro $totalFinal." -ForegroundColor Red; Borrar-Boton "HUMO23_REC2"; exit 1 }
Write-Host "  [OK] Totales de cabecera acumulados correctamente: SubTotal=$subTotalFinal, IVA=$ivaFinal, Total=$totalFinal."

# 8) Guard clause: una 3a corrida con los MISMOS parametros de la partida 2 debe RECHAZAR
#    (no debe crear un 3er renglon para la misma partida de OC).
$r3 = (& $RunnerExe --appkey HUMO23_REC2 --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO23_REC2"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en la 3a corrida (deberia responder ERROR, no fallar el proceso):" -ForegroundColor Red; $r3 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r3 -notmatch "ERROR.*ya fue recibida") { Write-Host "  [ERROR] La 3a corrida (repetir la misma partida) no fue rechazada como se esperaba. Salida: $r3" -ForegroundColor Red; exit 1 }
$totalRenglonesFinal = Exec-Q "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$docRec"
if ($totalRenglonesFinal -ne "2") { Write-Host "  [ERROR] El guard clause no evito el duplicado -- se encontraron $totalRenglonesFinal renglones (se esperaban 2)." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Guard clause rechazo la 3a corrida (partida ya recibida) sin duplicar el renglon."

exit 0
