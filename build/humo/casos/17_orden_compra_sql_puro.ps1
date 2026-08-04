# Caso de humo #17: valida PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql contra el sandbox. Igual que
# el caso 14 (Requisicion SQL puro), compara CAMPO POR CAMPO contra un documento nativo de
# referencia -- esta vez incluyendo impuestos (docDocumentTax/TaxDetail/TaxSum) y kardex
# comprometido (orgProductKardex), que Requisicion no tiene porque su Total siempre es $0.
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

# 1) Documento de REFERENCIA nativo: mismo producto/cantidad/precio que el SQL puro usa por
#    default (productoID=1, cantidad=2, precio=80) para que el kardex/impuestos coincidan.
$codigoNativo = @'
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(183, 1, 2);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, PaymentTermID=4, DateDelivery=GETDATE(), DateDocDelivery=GETDATE() WHERE DocumentID=" + doc);
// taxTypeIdOverride=5 vía AgregarArticulo (no un UPDATE crudo despues) -- asi TaxPerc se
// recalcula junto con TaxTypeID (Scripting.cs AgregarArticulo consulta vwLBSTaxPerc para el
// override); un UPDATE manual de solo TaxTypeID deja TaxPerc desincronizado (en 0).
ctx.erp.AgregarArticulo(doc, 1, 2, 80, -1, 5);
ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
try { ctx.erp.UpdateDocumentPaidInfo(doc); } catch {}
// MANUAL.md #6.3: obligatorio despues de Save para un documento sin inventario (Solicitud,
// OC) -- RecalcCompleto NO lo calcula. Sin esto StatusDeliveryID se queda en 0 en vez de 3.
try { ctx.erp.UpdateStatusDelivery(doc); } catch {}
return "doc=" + doc;
'@
if (-not (Upsert-Boton "HUMO_OC_REFERENCIA_NATIVA" "Humo 17 - referencia nativa" $codigoNativo)) { exit 1 }

$salidaRef = & $RunnerExe --appkey HUMO_OC_REFERENCIA_NATIVA --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183" -W 2>&1 | Select-Object -First 1).Trim()

# 2) Documento con la plantilla SQL puro real.
$codigoSqlPuro = "-- job: safe-offline`n" + (Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql") -Raw)
if (-not (Upsert-Boton "HUMO_OC_SQL_PURO" "Humo 17 - SQL puro" $codigoSqlPuro)) { exit 1 }

$salidaPuro = & $RunnerExe --appkey HUMO_OC_SQL_PURO --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] La plantilla SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docSqlPuro = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183" -W 2>&1 | Select-Object -First 1).Trim()

# 3) Comparar campo por campo (ignorando columnas que SIEMPRE difieren entre 2 documentos,
#    y DateDelivery/DateDocDelivery -- el SQL puro usa hoy+15 dias a proposito, no GETDATE()).
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment","DateDelivery")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo" -W 2>&1 | Select-Object -First 1).Trim()
$itemPuro = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro" -W 2>&1 | Select-Object -First 1).Trim()
$ignorarItem = @("DocumentID","DocumentItemID","DateItem")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsTaxDetail = Comparar-Tabla "docDocumentTaxDetail" "DocumentID" $docNativo $docSqlPuro @("DocumentID","DocumentTaxDetailID","DocumentItemID")
if ($diffsTaxDetail -ne 0) { Write-Host "  [ERROR] docDocumentTaxDetail tiene $diffsTaxDetail diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsKardex = Comparar-Tabla "orgProductKardex" "DocumentID" $docNativo $docSqlPuro @("DocumentID","StockTransactionID","DocumentItemID","DateTransaction")
if ($diffsKardex -ne 0) { Write-Host "  [ERROR] orgProductKardex tiene $diffsKardex diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

exit 0
