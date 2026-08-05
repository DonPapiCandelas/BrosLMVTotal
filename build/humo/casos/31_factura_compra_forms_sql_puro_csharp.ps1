# Caso de humo #31: valida PLANTILLA_FACTURA_COMPRA_FORMS_SQL_PURO_CSHARP.ctx --
# especificamente el cambio de v2.73.0 (docs/INVESTIGACION_XENGINE_SQL_PURO.md): el .ctx real
# dejo de calcular a mano SubTotal/Total/TotalTax del encabezado y docDocumentTaxSum, y en vez
# de eso llama ctx.erp.RecalcCompleto(doc) tras el COMMIT del INSERT directo -- SIN
# AffectStockNEW/UpdateStatusDelivery (la Factura no afecta inventario) -- y regenera la
# agenda de pago leyendo el Total YA recalculado por el motor. Ninguna de las 6 variantes de
# esta plantilla tenia caso de humo.
#
# LIMITE HONESTO (mismo que 25_adjuntos_documento.ps1): la plantilla real exige seleccionar
# 1+ Ordenes de Compra en el grid nativo de Comercial via ctx.GetSelectedIds() antes de abrir
# la ventana -- no automatizable headless con las herramientas de este proyecto. Este caso
# corre un script companion (31_factura_compra_forms_sql_puro_csharp.codigo.cs) que reproduce
# LITERALMENTE el batch de T-SQL de CrearFacturaSqlPuro() para una sola partida de una sola OC
# de origen, seguido de la MISMA secuencia de recalculo con ctx.erp que usa el .ctx real. El
# resultado se compara CAMPO POR CAMPO -- incluyendo la agenda de pago -- contra un documento
# nativo real (ctx.erp.NuevoDocumento/AgregarArticulo/RecalcCompleto/Save).
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
if (-not (Upsert-Boton "HUMO31_OC1" "Humo 31 - OC 1 (nativa)" $codigoOC)) { exit 1 }
$rOC1 = & $RunnerExe --appkey HUMO31_OC1 --bd $Database 2>&1
Borrar-Boton "HUMO31_OC1"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #1:" -ForegroundColor Red; $rOC1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC1 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC1 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC1"
$beOC1 = Exec-Q "SELECT BusinessEntityID FROM docDocument WHERE DocumentID=$docOC1"

# 2) OC #2 -- fuente del documento companion (Forms SQL puro) bajo prueba.
if (-not (Upsert-Boton "HUMO31_OC2" "Humo 31 - OC 2 (companion)" $codigoOC)) { exit 1 }
$rOC2 = & $RunnerExe --appkey HUMO31_OC2 --bd $Database 2>&1
Borrar-Boton "HUMO31_OC2"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear la OC #2:" -ForegroundColor Red; $rOC2 | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docOC2 = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
$itemOC2 = Exec-Q "SELECT DocumentItemID FROM docDocumentItem WHERE DocumentID=$docOC2"

# 3) Documento de referencia NATIVO (ctx.erp) -- factura la OC #1 completa (producto 1,
#    cantidad 10, precio 100, condicion de pago 4 -- mismos valores default de la plantilla).
$codigoNativo = @"
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(152, 1, $beOC1);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=4, StatusDeliveryID=0, Comments='' WHERE DocumentID=" + doc);
int itemId = ctx.erp.AgregarArticulo(doc, 1, 10, 100, -1, 5, 0);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo: " + ctx.erp.LastError);
ctx.NonQuery("UPDATE docDocumentItem SET SourceDocumentItemID=$itemOC1 WHERE DocumentItemID=" + itemId);
ctx.erp.RecalcCompleto(doc);
ctx.erp.Save(doc);
try {
    double total = Convert.ToDouble(ctx.Scalar("SELECT Total FROM docDocument WHERE DocumentID=" + doc));
    ctx.NonQuery("DELETE FROM docDocumentPaymentAgenda WHERE DocumentID=" + doc);
    ctx.NonQuery("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + doc + ", GETDATE(), 50, " + Math.Round(total * 0.5, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 1, GETDATE(), 0)");
    ctx.NonQuery("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + doc + ", DATEADD(MONTH,3,GETDATE()), 50, " + Math.Round(total * 0.5, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 2, GETDATE(), 0)");
} catch {}
return "doc=" + doc;
"@
if (-not (Upsert-Boton "HUMO31_REFERENCIA_NATIVA" "Humo 31 - referencia nativa" $codigoNativo)) { exit 1 }
$salidaRef = & $RunnerExe --appkey HUMO31_REFERENCIA_NATIVA --bd $Database 2>&1
Borrar-Boton "HUMO31_REFERENCIA_NATIVA"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=152"

# 4) Documento con el companion (misma logica que CrearFacturaSqlPuro() del .ctx real),
#    facturando la OC #2.
$plantillaCompanion = Get-Content (Join-Path $PSScriptRoot "31_factura_compra_forms_sql_puro_csharp.codigo.cs") -Raw
$plantillaCompanion = $plantillaCompanion -replace '__SOURCE_OC__', $docOC2
$plantillaCompanion = $plantillaCompanion -replace '__SOURCE_ITEM_ID__', $itemOC2
if (-not (Upsert-Boton "HUMO31_SQL_PURO_CSHARP" "Humo 31 - Forms SQL puro (companion)" $plantillaCompanion)) { exit 1 }
$salidaPuro = & $RunnerExe --appkey HUMO31_SQL_PURO_CSHARP --bd $Database 2>&1
Borrar-Boton "HUMO31_SQL_PURO_CSHARP"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El companion Forms SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docSqlPuro = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=152"

# 5) Comparar campo por campo.
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment","DateDelivery")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y Forms SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo"
$itemPuro = Exec-Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro"
$ignorarItem = @("DocumentID","DocumentItemID","DateItem","SourceDocumentItemID")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$diffsTaxDetail = Comparar-Tabla "docDocumentTaxDetail" "DocumentID" $docNativo $docSqlPuro @("DocumentID","DocumentTaxDetailID","DocumentItemID")
if ($diffsTaxDetail -ne 0) { Write-Host "  [ERROR] docDocumentTaxDetail tiene $diffsTaxDetail diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

# docDocumentPaymentAgenda tiene 2 filas (parcialidades 1 y 2) -- Comparar-Tabla asume 1 fila
# por pk, asi que aqui se compara explicitamente parcialidad por parcialidad (join por
# PartialityNumber) en vez de reusar ese helper generico.
$diffsPago = Exec-Q "SELECT COUNT(*) FROM docDocumentPaymentAgenda a FULL OUTER JOIN docDocumentPaymentAgenda b ON a.PartialityNumber=b.PartialityNumber AND b.DocumentID=$docSqlPuro WHERE a.DocumentID=$docNativo AND (a.TotalPerc <> b.TotalPerc OR a.Amount <> b.Amount OR a.PartialityNumber IS NULL OR b.PartialityNumber IS NULL)"
if ($diffsPago -ne "0") { Write-Host "  [ERROR] docDocumentPaymentAgenda tiene $diffsPago diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

$kardex = Exec-Q "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID=$docSqlPuro"
if ($kardex -ne "0") { Write-Host "  [ERROR] Se esperaban 0 filas en orgProductKardex, se encontraron $kardex." -ForegroundColor Red; exit 1 }

Write-Host "  [OK] El recalculo via ctx.erp/XEngine (companion Forms SQL puro) coincide campo por campo con el camino 100% nativo (incluyendo agenda de pago)."
exit 0
