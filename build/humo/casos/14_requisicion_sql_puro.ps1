# Caso de humo #14: valida PLANTILLA_REQUISICION_SQL_PURO.sql contra el sandbox. A
# diferencia de los demas casos, esta plantilla NO se compara solo "crea 1 documento mas" --
# se compara CAMPO POR CAMPO contra un documento de referencia creado por el camino nativo
# (ctx.erp.NuevoDocumento/AgregarArticulo/RecalcCompleto/Save), la misma metodologia que ya
# usaba docs/REQUISICION_SOLICITUD_COMPRA.md (Compare-Documento.ps1). Si alguien modifica la
# plantilla SQL puro y deja de coincidir con el nativo, este caso lo atrapa.
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

# 1) Crear un documento de REFERENCIA por el camino nativo (mismo modulo, mismo producto/cantidad).
$codigoNativo = @'
// job: safe-offline
int doc = ctx.erp.NuevoDocumento(1040, 1, 2);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0 WHERE DocumentID=" + doc);
ctx.erp.AgregarArticulo(doc, 1, 3);
ctx.erp.RecalcCompleto(doc);
ctx.erp.Save(doc);
return "doc=" + doc;
'@
if (-not (Upsert-Boton "HUMO_REQ_REFERENCIA_NATIVA" "Humo 14 - referencia nativa" $codigoNativo)) { exit 1 }

$nAntes = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument" -W 2>&1 | Select-Object -First 1).Trim()
$salidaRef = & $RunnerExe --appkey HUMO_REQ_REFERENCIA_NATIVA --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento de referencia nativo:" -ForegroundColor Red; $salidaRef | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docNativo = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument" -W 2>&1 | Select-Object -First 1).Trim()

# 2) Crear el documento con la plantilla SQL puro real (el archivo que se distribuye).
# v2.77.0: los 4 parametros ahora son tokens {DATOS:Tabla.Columna:*} (formulario automatico,
# ver ResolverFormularioTokens en src/HostClient.cs) en vez de literales hardcodeados -- el
# Runner headless NO resuelve esos tokens (eso solo pasa dentro de Comercial/Consola real), asi
# que aqui se sustituyen a mano por los MISMOS valores default que antes traia el archivo
# (proveedorBE=2, almacen=1, productoID=1, cantidad=3), simulando lo que el formulario hubiera
# capturado.
$codigoSqlPuro = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_REQUISICION_SQL_PURO.sql") -Raw
$codigoSqlPuro = $codigoSqlPuro -replace '\{DATOS:orgBusinessEntity\.BusinessEntityID[^}]*\}', '2'
$codigoSqlPuro = $codigoSqlPuro -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$codigoSqlPuro = $codigoSqlPuro -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', '1'
$codigoSqlPuro = $codigoSqlPuro -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '3'
$codigoSqlPuro = "-- job: safe-offline`n" + $codigoSqlPuro
if (-not (Upsert-Boton "HUMO_REQ_SQL_PURO" "Humo 14 - SQL puro" $codigoSqlPuro)) { exit 1 }

$salidaPuro = & $RunnerExe --appkey HUMO_REQ_SQL_PURO --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] La plantilla SQL puro fallo:" -ForegroundColor Red; $salidaPuro | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docSqlPuro = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentID) FROM docDocument" -W 2>&1 | Select-Object -First 1).Trim()

# 3) Comparar campo por campo (ignorando columnas que SIEMPRE difieren entre 2 documentos distintos).
$ignorarDoc = @("DocumentID","Folio","CreatedOn","DateCost","DateDocDelivery","DateDocument","DateFrom","DateTo","DateLastPayment")
$diffsDoc = Comparar-Tabla "docDocument" "DocumentID" $docNativo $docSqlPuro $ignorarDoc
if ($diffsDoc -ne 0) { Write-Host "  [ERROR] docDocument tiene $diffsDoc diferencia(s) inesperada(s) entre nativo ($docNativo) y SQL puro ($docSqlPuro)." -ForegroundColor Red; exit 1 }

$itemNativo = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docNativo" -W 2>&1 | Select-Object -First 1).Trim()
$itemPuro = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID=$docSqlPuro" -W 2>&1 | Select-Object -First 1).Trim()
$ignorarItem = @("DocumentID","DocumentItemID","DateItem")
$diffsItem = Comparar-Tabla "docDocumentItem" "DocumentItemID" $itemNativo $itemPuro $ignorarItem
if ($diffsItem -ne 0) { Write-Host "  [ERROR] docDocumentItem tiene $diffsItem diferencia(s) inesperada(s)." -ForegroundColor Red; exit 1 }

# 4) Confirmar que NO se afecto inventario (una Solicitud de Compra no debe).
$kardex = (sqlcmd -S $Server -E -d $Database -h -1 -Q "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID=$docSqlPuro" -W 2>&1 | Select-Object -First 1).Trim()
if ($kardex -ne "0") { Write-Host "  [ERROR] Se esperaba 0 filas en orgProductKardex, se encontraron $kardex." -ForegroundColor Red; exit 1 }

exit 0
