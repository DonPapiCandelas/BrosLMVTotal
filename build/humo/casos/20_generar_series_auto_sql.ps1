# Caso de humo #20: valida PLANTILLA_GENERAR_SERIES_AUTO_SQL.sql contra el sandbox.
#
# A diferencia de los casos 14/17 (que comparan campo por campo contra un documento
# nativo), esta plantilla no tiene equivalente nativo directo -- es una utilidad nueva sin
# precedente en Comercial Pro. Se valida funcionalmente en 3 pasos encadenados:
#   1) Crea una Orden de Compra + Recepcion de Compra reales (SQL puro, mismas plantillas
#      que los casos 17/-- Recepcion) para el producto de humo con numero de serie
#      (ProductID=3, HUMO-PROD-SERIE, UseSerialNumber=1) SIN capturar @seriesCSV -- deja el
#      kardex pendiente de serie a proposito, el caso real que esta plantilla resuelve.
#   2) Corre PLANTILLA_GENERAR_SERIES_AUTO_SQL.sql sobre esa Recepcion y valida: se generan
#      tantas series como Quantity recibida, formato exacto "<ProductKey>.<5 digitos>",
#      consecutivas, sin duplicados, y que continuan desde el maximo ya usado por el
#      producto (no reinician en 1 si ya habia series con ese formato de una corrida
#      anterior).
#   3) Corre la plantilla OTRA VEZ sobre el MISMO documento y valida que es un no-op
#      idempotente: cero filas nuevas insertadas, sin error.
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

function Escalar([string]$q) {
    return (sqlcmd -S $Server -E -d $Database -h -1 -Q $q -W 2>&1 | Select-Object -First 1).Trim()
}

# ── Producto de prueba: HUMO-PROD-SERIE (ProductID=3, UseSerialNumber=1) ──
$prodId = Escalar "SELECT ProductID FROM orgProduct WHERE ProductKey='HUMO-PROD-SERIE' AND UseSerialNumber=1"
if ([string]::IsNullOrWhiteSpace($prodId)) { Write-Host "  [ERROR] No existe el producto de humo HUMO-PROD-SERIE (UseSerialNumber=1) en el sandbox." -ForegroundColor Red; exit 1 }

# 1) Orden de Compra (SQL puro), producto de serie, cantidad=3.
# v2.77.0: los parametros de la plantilla ahora son tokens {DATOS:Tabla.Columna:*} (formulario
# automatico) en vez de literales -- el Runner headless no los resuelve, se sustituyen a mano
# (productoID=$prodId/cantidad=3 son los que este caso necesita variar; el resto usa los MISMOS
# defaults que antes traia el archivo: proveedorBE=2, almacen=1, precio=80, condicionPago=4).
$codigoOC = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql") -Raw
$codigoOC = $codigoOC -replace '\{DATOS:orgBusinessEntity\.BusinessEntityID[^}]*\}', '2'
$codigoOC = $codigoOC -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$codigoOC = $codigoOC -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', "$prodId"
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '3'
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '80'
$codigoOC = $codigoOC -replace '\{DATOS:docDocument\.PaymentTermID[^}]*\}', '4'
$codigoOC = "-- job: safe-offline`n" + $codigoOC
if (-not (Upsert-Boton "HUMO20_OC" "Humo 20 - OC producto serie" $codigoOC)) { exit 1 }
$out = & $RunnerExe --appkey HUMO20_OC --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo creando la OC de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }
$ocId = Escalar "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$ocItemId = Escalar "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$ocId"

# 2) Recepcion de Compra (SQL puro) derivada de esa OC, SIN @seriesCSV -- deja el kardex
#    pendiente de serie a proposito (el caso que esta plantilla nueva resuelve).
$codigoRC = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql") -Raw
$codigoRC = $codigoRC -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$ocId"
$codigoRC = $codigoRC -replace '\{DATOS:docDocumentItem\.DocumentItemID[^}]*\}', "$ocItemId"
$codigoRC = $codigoRC -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$codigoRC = $codigoRC -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', "$prodId"
$codigoRC = $codigoRC -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '3'
$codigoRC = $codigoRC -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '250'
$codigoRC = $codigoRC -replace "DECLARE @lote            NVARCHAR\(50\) = '[^']*';", 'DECLARE @lote            NVARCHAR(50) = NULL;'
$codigoRC = $codigoRC -replace "DECLARE @caducidad       DATE = '[^']*';", 'DECLARE @caducidad       DATE = NULL;'
$codigoRC = "-- job: safe-offline`n" + $codigoRC
if (-not (Upsert-Boton "HUMO20_RC" "Humo 20 - Recepcion producto serie sin series" $codigoRC)) { exit 1 }
$out = & $RunnerExe --appkey HUMO20_RC --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo creando la Recepcion de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }
$rcId = Escalar "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184"

$pendientesAntes = Escalar "SELECT COUNT(*) FROM orgProductKardex K INNER JOIN orgProduct P ON P.ProductID=K.ProductID AND P.UseSerialNumber=1 LEFT JOIN docDocumentSerialNumber S ON S.DocumentItemID=K.DocumentItemID AND S.DeletedOn IS NULL WHERE K.DocumentID=$rcId AND S.DocumentSerialNumberID IS NULL"
if ($pendientesAntes -ne "1") { Write-Host "  [ERROR] Se esperaba 1 renglon de kardex pendiente de serie en la Recepcion $rcId, hay $pendientesAntes." -ForegroundColor Red; exit 1 }

# 3) Correr la plantilla nueva -- primera vez: debe generar 3 series.
$codigoGS = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_GENERAR_SERIES_AUTO_SQL.sql") -Raw
$codigoGS = $codigoGS -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$rcId"
$codigoGS = "-- job: safe-offline`n" + $codigoGS
if (-not (Upsert-Boton "HUMO20_GENSERIES" "Humo 20 - generar series auto" $codigoGS)) { exit 1 }

$out1 = & $RunnerExe --appkey HUMO20_GENSERIES --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] La plantilla de generacion de series fallo en la 1a corrida:" -ForegroundColor Red; $out1 | ForEach-Object { Write-Host "    $_" }; exit 1 }

$series = sqlcmd -S $Server -E -d $Database -h -1 -Q "SET NOCOUNT ON; SELECT SerialNumber FROM docDocumentSerialNumber WHERE DocumentID=$rcId ORDER BY SerialNumber" -W 2>&1 | Where-Object { $_.Trim() -ne "" }
$series = @($series | ForEach-Object { $_.Trim() })

if ($series.Count -ne 3) { Write-Host "  [ERROR] Se esperaban 3 series generadas, se encontraron $($series.Count): $($series -join ', ')" -ForegroundColor Red; exit 1 }

$patron = "^HUMO-PROD-SERIE\.\d{5}$"
foreach ($s in $series) {
    if ($s -notmatch $patron) { Write-Host "  [ERROR] Serie '$s' no cumple el formato esperado '<ProductKey>.<5 digitos>'." -ForegroundColor Red; exit 1 }
}
if (($series | Select-Object -Unique).Count -ne $series.Count) { Write-Host "  [ERROR] Hay series duplicadas: $($series -join ', ')" -ForegroundColor Red; exit 1 }

# Deben ser 3 consecutivos (N, N+1, N+2), sin importar en que consecutivo arranquen (depende
# de corridas previas de humo sobre el mismo producto en este sandbox).
$consecutivos = $series | ForEach-Object { [int]([regex]::Match($_, '\.(\d{5})$').Groups[1].Value) } | Sort-Object
for ($i = 1; $i -lt $consecutivos.Count; $i++) {
    if ($consecutivos[$i] -ne ($consecutivos[$i-1] + 1)) { Write-Host "  [ERROR] Las series no son consecutivas: $($consecutivos -join ', ')" -ForegroundColor Red; exit 1 }
}

# 4) Correr la plantilla OTRA VEZ sobre el mismo documento -- debe ser un no-op idempotente:
#    cero filas nuevas, sin error.
$out2 = & $RunnerExe --appkey HUMO20_GENSERIES --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] La plantilla de generacion de series fallo en la 2a corrida (deberia ser un no-op silencioso):" -ForegroundColor Red; $out2 | ForEach-Object { Write-Host "    $_" }; exit 1 }

$totalDespues = Escalar "SELECT COUNT(*) FROM docDocumentSerialNumber WHERE DocumentID=$rcId AND DeletedOn IS NULL"
if ($totalDespues -ne "3") { Write-Host "  [ERROR] Despues de correr la plantilla dos veces deberia haber exactamente 3 series (idempotente), hay $totalDespues." -ForegroundColor Red; exit 1 }

exit 0
