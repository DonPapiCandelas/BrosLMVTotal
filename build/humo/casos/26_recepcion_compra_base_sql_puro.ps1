# Caso de humo #26: valida el camino BASE (primera recepcion, sin consolidacion) de
# PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql -- comparacion CAMPO POR CAMPO contra un documento
# de referencia creado por el camino nativo (ctx.erp.NuevoDocumento/AgregarArticulo/
# RecalcCompleto/AffectStockNEW/Save), misma metodologia que el caso 14 (Requisicion) y el
# caso 17 (Orden de Compra).
#
# Por que hace falta: el caso 23 YA prueba la consolidacion multipartida (v2.70.0) de esta
# misma plantilla, pero arranca corriendo la plantilla dos veces sobre la MISMA OC -- nunca
# compara el resultado del camino base (una sola corrida, sin Recepcion previa) contra un
# documento nativo real. Este caso llena ese hueco: UNA sola corrida, UNA sola partida,
# ninguna Recepcion previa para la OC de origen.
#
# Se crean DOS Ordenes de Compra distintas (una para el documento nativo de referencia, otra
# para la plantilla SQL puro bajo prueba) -- si se reusara la MISMA OC para ambas, la segunda
# corrida (SQL puro) encontraria una Recepcion ya existente con SourceDocumentID=OC y
# CONSOLIDARIA en vez de crear un documento nuevo (justo el comportamiento que el caso 23 ya
# cubre, no el que este caso quiere probar).
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

function Comparar-Tabla([string]$tabla, [string]$pk, [int]$docA, [int]$docB, [string[]]$ignorar) {
    $ignorarSql = ($ignorar | ForEach-Object { "'$_'" }) -join ","
    $q = @"
DECLARE @a NVARCHAR(MAX), @b NVARCHAR(MAX);
SELECT @a = STRING_AGG(CONVERT(NVARCHAR(MAX),'SELECT '''+name+''' AS Col, ISNULL(CONVERT(NVARCHAR(MAX),['+name+']),''<NULL>'') AS V FROM $tabla WHERE $pk=$docA'),' UNION ALL ') FROM sys.columns WHERE object_id=OBJECT_ID('$tabla') AND name NOT IN ($ignorarSql);
SELECT @b = STRING_AGG(CONVERT(NVARCHAR(MAX),'SELECT '''+name+''' AS Col, ISNULL(CONVERT(NVARCHAR(MAX),['+name+']),''<NULL>'') AS V FROM $tabla WHERE $pk=$docB'),' UNION ALL ') FROM sys.columns WHERE object_id=OBJECT_ID('$tabla') AND name NOT IN ($ignorarSql);
DECLARE @sql NVARCHAR(MAX) = N'WITH A AS ('+@a+N'), B AS ('+@b+N') SELECT COUNT(*) FROM A JOIN B ON A.Col=B.Col WHERE A.V <> B.V';
EXEC sp_executesql @sql;
"@
    $n = (sqlcmd -S $Server -E -d $Database -h -1 -Q $q -W 2>&1 | Select-Object -First 1).Trim()
    return [int]$n
}

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

# 1) OC #1 -- fuente del documento NATIVO de referencia.
if (-not (Upsert-Boton "HUMO26_OC1" "Humo 26 - OC 1 (nativa)" $codigoOC)) { exit 1 }
$rOC1 = & $RunnerExe --appkey HUMO26_OC1 --bd $Database 2>&1
Borrar-Boton "HUMO26_OC1"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #1:" -ForegroundColor Red; $rOC1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC1 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC1 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC1"
$beOC1 = Exec-Q "SELECT BusinessEntityID FROM docDocument WHERE DocumentID=$docOC1"
Write-Host "  OC #1 (nativa): DocumentID=$docOC1, partida=$itemOC1, proveedor=$beOC1"

# 2) OC #2 -- fuente del documento SQL PURO bajo prueba.
if (-not (Upsert-Boton "HUMO26_OC2" "Humo 26 - OC 2 (SQL puro)" $codigoOC)) { exit 1 }
$rOC2 = & $RunnerExe --appkey HUMO26_OC2 --bd $Database 2>&1
Borrar-Boton "HUMO26_OC2"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #2:" -ForegroundColor Red; $rOC2 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC2 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC2 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC2"
Write-Host "  OC #2 (SQL puro): DocumentID=$docOC2, partida=$itemOC2"

# 3) Documento de referencia NATIVO (ctx.erp) -- recibe la OC #1 completa: producto 1,
#    cantidad 2, precio 80 (mismos valores que usa la OC generada por la plantilla).
$codigoNativo = @"
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(184, 1, $beOC1);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0, SourceDocumentID=$docOC1 WHERE DocumentID=" + doc);
int itemId = ctx.erp.AgregarArticulo(doc, 1, 2, 80, 80, 5, 0, $itemOC1);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo: " + ctx.erp.LastError);
ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
try { ctx.erp.UpdateStatusDelivery(doc); } catch {}
return "doc=" + doc;
"@
if (-not (Upsert-Boton "HUMO26_REFERENCIA_NATIVA" "Humo 26 - referencia nativa" $codigoNativo)) { exit 1 }
$salidaRef = & $RunnerExe --appkey HUMO26_REFERENCIA_NATIVA --bd $Database 2>&1
Borrar-Boton "HUMO26_REFERENCIA_NATIVA"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184"
Write-Host "  Recepcion nativa de referencia: DocumentID=$docNativo"

# 4) Documento con la plantilla SQL puro real (camino BASE -- primera y unica corrida para
#    la OC #2, sin Recepcion previa que consolidar).
$plantilla = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql") -Raw
$plantilla = $plantilla -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$docOC2"
$plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.DocumentItemID[^}]*\}', "$itemOC2"
$plantilla = $plantilla -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$plantilla = $plantilla -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', '1'
$plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '2'
$plantilla = $plantilla -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '80'
$plantilla = $plantilla -replace "DECLARE @lote\s+NVARCHAR\(50\) = 'LOTE-HUMO-01';", "DECLARE @lote            NVARCHAR(50) = NULL;"
$plantilla = $plantilla -replace "DECLARE @caducidad\s+DATE = '2027-12-31';", "DECLARE @caducidad       DATE = NULL;"
$codigoSqlPuro = "-- job: safe-offline`n" + $plantilla
if (-not (Upsert-Boton "HUMO26_SQL_PURO" "Humo 26 - SQL puro" $codigoSqlPuro)) { exit 1 }
$salidaPuro = (& $RunnerExe --appkey HUMO26_SQL_PURO --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO26_SQL_PURO"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] La plantilla SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($salidaPuro -notmatch "creada") { Write-Host "  [ERROR] Se esperaba 'creada' (camino base, sin consolidar). Salida: $salidaPuro" -ForegroundColor Red; exit 1 }
$docSqlPuro = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184 AND SourceDocumentID=$docOC2"
Write-Host "  Recepcion SQL puro (camino base): DocumentID=$docSqlPuro"

# 5) Comparar campo por campo (ignorando columnas que SIEMPRE difieren entre 2 documentos
#    distintos, y SourceDocumentID -- apuntan a OC distintas a proposito).
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment","DateDelivery","SourceDocumentID")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo"
$itemPuro = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro"
$ignorarItem = @("DocumentID","DocumentItemID","DateItem","DeliverDocumentItemID")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsTaxDetail = Comparar-Tabla "docDocumentTaxDetail" "DocumentID" $docNativo $docSqlPuro @("DocumentID","DocumentTaxDetailID","DocumentItemID")
if ($diffsTaxDetail -ne 0) { Write-Host "  [ERROR] docDocumentTaxDetail tiene $diffsTaxDetail diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsKardex = Comparar-Tabla "orgProductKardex" "DocumentID" $docNativo $docSqlPuro @("DocumentID","StockTransactionID","DocumentItemID","DateTransaction")
if ($diffsKardex -ne 0) { Write-Host "  [ERROR] orgProductKardex tiene $diffsKardex diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

Write-Host "  [OK] Recepcion SQL puro (camino base) coincide campo por campo con la referencia nativa."
exit 0
