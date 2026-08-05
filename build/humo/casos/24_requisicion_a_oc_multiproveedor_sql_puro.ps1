# Caso de humo #24: valida PLANTILLA_REQUISICION_A_OC_MULTIPROVEEDOR_SQL_PURO.sql (v2.71.0)
# contra el sandbox -- el patron "Requisicion -> N Ordenes de Compra, una por proveedor",
# inspirado en el SP de produccion ZLCGENERA_OC (ver
# entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md, seccion "ZLCGENERA_OC").
#
# Escenario: una Requisicion con 2 renglones de 2 proveedores DISTINTOS (via
# docDocumentItem.SupplierBusinessEntityID por renglon). Se confirma que:
#   1) Sin ValidatedOn, la plantilla RECHAZA (guard clause de aprobacion).
#   2) Validada, la plantilla genera EXACTAMENTE 2 OC (una por proveedor), cada una con
#      SourceDocumentID=Requisicion y SOLO el renglon del proveedor correspondiente.
#   3) Corrida una 2a vez sobre la MISMA Requisicion, NO duplica (dedup por
#      SourceDocumentID+BusinessEntityID) -- ambas OC se reportan "ya existia".
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

# v2.77.0: @requisicionID ahora es un token {DATOS:docDocument.DocumentID:*} (formulario
# automatico) -- el Runner headless no lo resuelve, se sustituye a mano.
function Build-CodigoPlantilla([int]$requisicionID) {
    $plantilla = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_REQUISICION_A_OC_MULTIPROVEEDOR_SQL_PURO.sql") -Raw
    $plantilla = $plantilla -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$requisicionID"
    return "-- job: safe-offline`n" + $plantilla
}

# 0) Asegura 2 proveedores de prueba distintos (BusinessEntityID) -- idempotente, mismo patron
#    que build\humo\casos\03_crear_oc.codigo.cs usa para su proveedor de humo.
$sqlProveedores = @"
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO24-PROV-A')
BEGIN
    DECLARE @beA INT;
    INSERT INTO orgBusinessEntity (CommercialName, BusinessEntityKey, OfficialName, CreatedBy, CreatedOn, UserID)
    VALUES ('Proveedor humo 24 A', 'HUMO24-PROV-A', 'Proveedor humo 24 A SA de CV', 0, GETDATE(), 0);
    SET @beA = SCOPE_IDENTITY();
    INSERT INTO orgSupplier (BusinessEntityID) VALUES (@beA);
END
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO24-PROV-B')
BEGIN
    DECLARE @beB INT;
    INSERT INTO orgBusinessEntity (CommercialName, BusinessEntityKey, OfficialName, CreatedBy, CreatedOn, UserID)
    VALUES ('Proveedor humo 24 B', 'HUMO24-PROV-B', 'Proveedor humo 24 B SA de CV', 0, GETDATE(), 0);
    SET @beB = SCOPE_IDENTITY();
    INSERT INTO orgSupplier (BusinessEntityID) VALUES (@beB);
END
"@
sqlcmd -S $Server -E -d $Database -Q $sqlProveedores -W | Out-Null
$proveedorA = Exec-Q "SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO24-PROV-A'"
$proveedorB = Exec-Q "SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey='HUMO24-PROV-B'"
if (-not $proveedorA -or -not $proveedorB) { Write-Host "  [ERROR] No se pudieron crear/leer los 2 proveedores de prueba." -ForegroundColor Red; exit 1 }
Write-Host "  Proveedores de prueba: A=$proveedorA, B=$proveedorB"

# 1) Crear una Requisicion (modulo 1040) con 2 renglones -- producto 1 -> proveedor A,
#    producto 2 -> proveedor B (via docDocumentItem.SupplierBusinessEntityID). No hay
#    plantilla existente para "Requisicion multi-proveedor" -- se arma directo por SQL, mismo
#    patron de cabecera que PLANTILLA_REQUISICION_SQL_PURO.sql.
$sqlRequisicion = @"
SET NOCOUNT ON;
DECLARE @ahora DATETIME = GETDATE();
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID=1040),0)+1;
DECLARE @docId INT;
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized,
    ExportID, TotalLetter, CreatedOn, CreatedBy, UserID
)
VALUES (
    1040, 49, 2, 1, $proveedorA,
    1, 0, '', CAST(@folio AS NVARCHAR), @ahora, @ahora, @ahora, @ahora, @ahora,
    3, 3, 1, 0, @ahora, 1,
    1, 'CERO PESOS 00/100 M.N.', @ahora, 0, 0
);
SET @docId = SCOPE_IDENTITY();
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 1, @ahora, 0);

INSERT INTO docDocumentItem (DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad, ObjetoImpuesto, TaxTypeID, TaxPerc, LineNumber, MustBeDelivered, ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem, SupplierBusinessEntityID)
SELECT @docId, 5, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad, '', ISNULL(p.TaxTypeID,0), 0.16, 1, 1, 1, 1, 1, 1, @ahora, $proveedorA
FROM orgProduct p WHERE p.ProductID=1;

INSERT INTO docDocumentItem (DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad, ObjetoImpuesto, TaxTypeID, TaxPerc, LineNumber, MustBeDelivered, ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem, SupplierBusinessEntityID)
SELECT @docId, 3, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad, '', ISNULL(p.TaxTypeID,0), 0.16, 2, 1, 1, 1, 1, 1, @ahora, $proveedorB
FROM orgProduct p WHERE p.ProductID=2;

SELECT @docId;
"@
$docReq = Exec-Q $sqlRequisicion
if (-not $docReq) { Write-Host "  [ERROR] No se pudo crear la Requisicion multi-proveedor de prueba." -ForegroundColor Red; exit 1 }
Write-Host "  Requisicion creada: DocumentID=$docReq (renglon 1 -> proveedor A, renglon 2 -> proveedor B)"

# 2) SIN validar: la plantilla debe RECHAZAR (guard clause ValidatedOn IS NULL).
if (-not (Upsert-Boton "HUMO24_SINVALIDAR" "Humo 24 - sin validar" (Build-CodigoPlantilla $docReq))) { exit 1 }
$rSinValidar = (& $RunnerExe --appkey HUMO24_SINVALIDAR --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO24_SINVALIDAR"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) sin validar (deberia responder ERROR, no fallar el proceso):" -ForegroundColor Red; $rSinValidar | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($rSinValidar -notmatch "ERROR.*validada") { Write-Host "  [ERROR] La plantilla no rechazo la Requisicion sin validar como se esperaba. Salida: $rSinValidar" -ForegroundColor Red; exit 1 }
$ocPrevias = Exec-Q "SELECT COUNT(*) FROM docDocument WHERE SourceDocumentID=$docReq AND ModuleID=183"
if ($ocPrevias -ne "0") { Write-Host "  [ERROR] Se genero(aron) $ocPrevias OC pese a que la Requisicion no estaba validada." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Rechazada correctamente sin ValidatedOn (0 OC generadas)."

# 3) Validar la Requisicion (mecanismo directo -- Comercial Pro no expone otro camino SQL puro
#    para esto; ver advertencia de precedente de diseno en la propia plantilla).
sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocument SET ValidatedOn=GETDATE(), ValidatedBy=0 WHERE DocumentID=$docReq" -W | Out-Null

# 4) Primera corrida (validada): debe generar EXACTAMENTE 2 OC, una por proveedor.
if (-not (Upsert-Boton "HUMO24_GEN1" "Humo 24 - generacion 1" (Build-CodigoPlantilla $docReq))) { exit 1 }
$r1 = (& $RunnerExe --appkey HUMO24_GEN1 --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO24_GEN1"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo la 1a corrida (ya validada):" -ForegroundColor Red; $r1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
Write-Host "  Salida 1a corrida: $r1"

$totalOC = Exec-Q "SELECT COUNT(*) FROM docDocument WHERE SourceDocumentID=$docReq AND ModuleID=183 AND DeletedOn IS NULL AND CancelledOn IS NULL"
if ($totalOC -ne "2") { Write-Host "  [ERROR] Se esperaban 2 OC generadas, se encontraron $totalOC." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Se generaron exactamente 2 OC (una por proveedor)."

$docOC_A = Exec-Q "SELECT DocumentID FROM docDocument WHERE SourceDocumentID=$docReq AND ModuleID=183 AND BusinessEntityID=$proveedorA"
$docOC_B = Exec-Q "SELECT DocumentID FROM docDocument WHERE SourceDocumentID=$docReq AND ModuleID=183 AND BusinessEntityID=$proveedorB"
if (-not $docOC_A -or -not $docOC_B) { Write-Host "  [ERROR] No se encontro una OC para cada proveedor (A=$docOC_A, B=$docOC_B)." -ForegroundColor Red; exit 1 }

# 5) Confirmar que cada OC tiene SOLO el renglon de SU proveedor (1 renglon cada una, producto correcto).
$renglonesA = Exec-Q "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$docOC_A"
$productoA  = Exec-Q "SELECT ProductID FROM docDocumentItem WHERE DocumentID=$docOC_A"
$renglonesB = Exec-Q "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$docOC_B"
$productoB  = Exec-Q "SELECT ProductID FROM docDocumentItem WHERE DocumentID=$docOC_B"
if ($renglonesA -ne "1" -or $productoA -ne "1") { Write-Host "  [ERROR] OC del proveedor A (doc=$docOC_A) deberia tener 1 renglon de producto 1 -- renglones=$renglonesA, producto=$productoA." -ForegroundColor Red; exit 1 }
if ($renglonesB -ne "1" -or $productoB -ne "2") { Write-Host "  [ERROR] OC del proveedor B (doc=$docOC_B) deberia tener 1 renglon de producto 2 -- renglones=$renglonesB, producto=$productoB." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] OC proveedor A (doc=$docOC_A) -> producto 1 unicamente. OC proveedor B (doc=$docOC_B) -> producto 2 unicamente."

# 6) Segunda corrida sobre la MISMA Requisicion: NO debe duplicar (dedup por
#    SourceDocumentID+BusinessEntityID).
if (-not (Upsert-Boton "HUMO24_GEN2" "Humo 24 - generacion 2 (dedup)" (Build-CodigoPlantilla $docReq))) { exit 1 }
$r2 = (& $RunnerExe --appkey HUMO24_GEN2 --bd $Database 2>&1) -join "`n"
Borrar-Boton "HUMO24_GEN2"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] Fallo la 2a corrida (dedup):" -ForegroundColor Red; $r2 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r2 -notmatch "ya exist") { Write-Host "  [ERROR] La 2a corrida no reporto 'ya existia' para ambos proveedores. Salida: $r2" -ForegroundColor Red; exit 1 }

$totalOCFinal = Exec-Q "SELECT COUNT(*) FROM docDocument WHERE SourceDocumentID=$docReq AND ModuleID=183 AND DeletedOn IS NULL AND CancelledOn IS NULL"
if ($totalOCFinal -ne "2") { Write-Host "  [ERROR] Tras la 2a corrida se esperaban seguir siendo 2 OC (sin duplicar), se encontraron $totalOCFinal." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] La 2a corrida no duplico -- siguen siendo exactamente 2 OC (dedup funcionando)."

exit 0
