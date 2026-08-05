# Caso de humo #30: valida PLANTILLA_RECEPCION_COMPRA_FORMS_SQL_PURO_CSHARP.ctx --
# especificamente el cambio de v2.73.0 (docs/INVESTIGACION_XENGINE_SQL_PURO.md): el .ctx real
# dejo de calcular a mano orgProductKardex y SubTotal/Total/TotalTax/TotalCost del encabezado,
# y en vez de eso llama ctx.erp.RecalcCompleto(doc) + ctx.erp.AffectStockNEW(doc) +
# ctx.erp.UpdateStatusDelivery(doc) tras el COMMIT del INSERT directo. Ninguna de las 6
# variantes de esta plantilla tenia caso de humo.
#
# LIMITE HONESTO (mismo que 25_adjuntos_documento.ps1): la plantilla real es una ventana
# WinForms modeless con grid de OC pendientes y captura de lote/numero de serie -- no
# automatizable headless con las herramientas de este proyecto. Este caso corre un script
# companion (30_recepcion_compra_forms_sql_puro_csharp.codigo.cs) que reproduce LITERALMENTE
# el batch de T-SQL de CrearRecepcionSqlPuro() para una sola partida de una sola OC de origen
# (sin lote ni serie), seguido de la MISMA secuencia de recalculo con ctx.erp que usa el .ctx
# real. El resultado se compara CAMPO POR CAMPO -- incluyendo impuestos y kardex real -- contra
# un documento nativo real (ctx.erp.NuevoDocumento/AgregarArticulo/RecalcCompleto/
# AffectStockNEW/Save).
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
if (-not (Upsert-Boton "HUMO30_OC1" "Humo 30 - OC 1 (nativa)" $codigoOC)) { exit 1 }
$rOC1 = & $RunnerExe --appkey HUMO30_OC1 --bd $Database 2>&1
Borrar-Boton "HUMO30_OC1"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #1:" -ForegroundColor Red; $rOC1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC1 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC1 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC1"
$beOC1 = Exec-Q "SELECT BusinessEntityID FROM docDocument WHERE DocumentID=$docOC1"

# 2) OC #2 -- fuente del documento companion (Forms SQL puro) bajo prueba.
if (-not (Upsert-Boton "HUMO30_OC2" "Humo 30 - OC 2 (companion)" $codigoOC)) { exit 1 }
$rOC2 = & $RunnerExe --appkey HUMO30_OC2 --bd $Database 2>&1
Borrar-Boton "HUMO30_OC2"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #2:" -ForegroundColor Red; $rOC2 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC2 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC2 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC2"

# 3) Documento de referencia NATIVO (ctx.erp) -- recibe la OC #1 completa: producto 1,
#    cantidad 2, precio 80.
$codigoNativo = @"
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(184, 1, $beOC1);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0, SourceDocumentID=$docOC1, Comments='' WHERE DocumentID=" + doc);
int itemId = ctx.erp.AgregarArticulo(doc, 1, 2, 80, 80, 5, 0, $itemOC1);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo: " + ctx.erp.LastError);
ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
try { ctx.erp.UpdateStatusDelivery(doc); } catch {}
return "doc=" + doc;
"@
if (-not (Upsert-Boton "HUMO30_REFERENCIA_NATIVA" "Humo 30 - referencia nativa" $codigoNativo)) { exit 1 }
$salidaRef = & $RunnerExe --appkey HUMO30_REFERENCIA_NATIVA --bd $Database 2>&1
Borrar-Boton "HUMO30_REFERENCIA_NATIVA"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184"

# 4) Documento con el companion (misma logica que CrearRecepcionSqlPuro() del .ctx real),
#    recibiendo la OC #2.
$plantillaCompanion = Get-Content (Join-Path $PSScriptRoot "30_recepcion_compra_forms_sql_puro_csharp.codigo.cs") -Raw
$plantillaCompanion = $plantillaCompanion -replace '__SOURCE_OC__', $docOC2
$plantillaCompanion = $plantillaCompanion -replace '__SOURCE_ITEM_ID__', $itemOC2
$plantillaCompanion = $plantillaCompanion -replace '__PRODUCTO_ID__', "1"
$plantillaCompanion = $plantillaCompanion -replace '__CANTIDAD__', "2"
$plantillaCompanion = $plantillaCompanion -replace '__PRECIO__', "80"
if (-not (Upsert-Boton "HUMO30_SQL_PURO_CSHARP" "Humo 30 - Forms SQL puro (companion)" $plantillaCompanion)) { exit 1 }
$salidaPuro = & $RunnerExe --appkey HUMO30_SQL_PURO_CSHARP --bd $Database 2>&1
Borrar-Boton "HUMO30_SQL_PURO_CSHARP"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El companion Forms SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docSqlPuro = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=184 AND SourceDocumentID=$docOC2"

# 5) Comparar campo por campo (ignorando SourceDocumentID -- apuntan a OC distintas a proposito).
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment","DateDelivery","SourceDocumentID")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y Forms SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo"
$itemPuro = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro"
$ignorarItem = @("DocumentID","DocumentItemID","DateItem","DeliverDocumentItemID")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsTaxDetail = Comparar-Tabla "docDocumentTaxDetail" "DocumentID" $docNativo $docSqlPuro @("DocumentID","DocumentTaxDetailID","DocumentItemID")
if ($diffsTaxDetail -ne 0) { Write-Host "  [ERROR] docDocumentTaxDetail tiene $diffsTaxDetail diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsKardex = Comparar-Tabla "orgProductKardex" "DocumentID" $docNativo $docSqlPuro @("DocumentID","StockTransactionID","DocumentItemID","DateTransaction")
if ($diffsKardex -ne 0) { Write-Host "  [ERROR] orgProductKardex tiene $diffsKardex diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

Write-Host "  [OK] El recalculo via ctx.erp/XEngine (companion Forms SQL puro) coincide campo por campo con el camino 100% nativo (incluyendo impuestos y kardex real)."
exit 0
