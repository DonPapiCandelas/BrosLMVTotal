# Caso de humo #29: valida PLANTILLA_ORDEN_COMPRA_FORMS_SQL_PURO_CSHARP.ctx -- especificamente
# el cambio de v2.73.0 (docs/INVESTIGACION_XENGINE_SQL_PURO.md): el .ctx real dejo de calcular
# a mano orgProductKardex, docDocumentTaxDetail/TaxSum y SubTotal/Total/TotalTax del
# encabezado, y en vez de eso llama ctx.erp.RecalcCompleto(doc) + ctx.erp.AffectStockNEW(doc)
# + ctx.erp.UpdateStatusDelivery(doc) tras el COMMIT del INSERT directo. Ninguna de las 6
# variantes de esta plantilla tenia caso de humo.
#
# LIMITE HONESTO (mismo que 25_adjuntos_documento.ps1): la plantilla real es una ventana
# WinForms modeless con buscador de proveedor/producto, impuesto y descuento por partida,
# totales en vivo -- no automatizable headless con las herramientas de este proyecto. Este
# caso corre un script companion (29_orden_compra_forms_sql_puro_csharp.codigo.cs) que
# reproduce LITERALMENTE el batch de T-SQL de CrearOrdenCompraSqlPuro() para una sola partida
# sin descuento con parametros fijos, seguido de la MISMA secuencia de recalculo con ctx.erp
# que usa el .ctx real. El resultado se compara CAMPO POR CAMPO -- incluyendo impuestos y
# kardex comprometido -- contra un documento nativo real (ctx.erp.NuevoDocumento/
# AgregarArticulo/RecalcCompleto/AffectStockNEW/Save). Si el recalculo via XEngine dejara algo
# distinto de lo que produce el camino 100% nativo, este caso lo atrapa.
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

# 1) Documento de referencia NATIVO (ctx.erp) -- mismo producto/cantidad/precio/impuesto que
#    el companion Forms SQL puro usa por default (mismos valores que el caso 17 usa para la
#    plantilla .sql, asi los 3 caminos son comparables entre si).
$codigoNativo = @'
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(183, 1, 2);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, PaymentTermID=1, DateDelivery=GETDATE(), DateDocDelivery=GETDATE(), Comments='' WHERE DocumentID=" + doc);
ctx.erp.AgregarArticulo(doc, 1, 2, 80, -1, 5);
ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);
try { ctx.erp.UpdateDocumentPaidInfo(doc); } catch {}
try { ctx.erp.UpdateStatusDelivery(doc); } catch {}
return "doc=" + doc;
'@
if (-not (Upsert-Boton "HUMO29_REFERENCIA_NATIVA" "Humo 29 - referencia nativa" $codigoNativo)) { exit 1 }
$salidaRef = & $RunnerExe --appkey HUMO29_REFERENCIA_NATIVA --bd $Database 2>&1
Borrar-Boton "HUMO29_REFERENCIA_NATIVA"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"

# 2) Documento con el companion (misma logica que CrearOrdenCompraSqlPuro() del .ctx real).
$codigoCompanion = Get-Content (Join-Path $PSScriptRoot "29_orden_compra_forms_sql_puro_csharp.codigo.cs") -Raw
if (-not (Upsert-Boton "HUMO29_SQL_PURO_CSHARP" "Humo 29 - Forms SQL puro (companion)" $codigoCompanion)) { exit 1 }
$salidaPuro = & $RunnerExe --appkey HUMO29_SQL_PURO_CSHARP --bd $Database 2>&1
Borrar-Boton "HUMO29_SQL_PURO_CSHARP"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El companion Forms SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docSqlPuro = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"

# 3) Comparar campo por campo (igual que el caso 17, incluyendo DateDelivery/DateDocDelivery
#    -- ambos caminos usan hoy+7 dias, no GETDATE(), asi que aqui SI deben coincidir).
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment","DateDelivery")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y Forms SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo"
$itemPuro = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro"
$ignorarItem = @("DocumentID","DocumentItemID","DateItem")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsTaxDetail = Comparar-Tabla "docDocumentTaxDetail" "DocumentID" $docNativo $docSqlPuro @("DocumentID","DocumentTaxDetailID","DocumentItemID")
if ($diffsTaxDetail -ne 0) { Write-Host "  [ERROR] docDocumentTaxDetail tiene $diffsTaxDetail diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsKardex = Comparar-Tabla "orgProductKardex" "DocumentID" $docNativo $docSqlPuro @("DocumentID","StockTransactionID","DocumentItemID","DateTransaction")
if ($diffsKardex -ne 0) { Write-Host "  [ERROR] orgProductKardex tiene $diffsKardex diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

Write-Host "  [OK] El recalculo via ctx.erp/XEngine (companion Forms SQL puro) coincide campo por campo con el camino 100% nativo (incluyendo impuestos y kardex)."
exit 0
