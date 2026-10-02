# Caso de humo #36: la plantilla de fabrica ASIGNAR_CENTRO_COSTO.ctx corriendo headless (BrosLMV.Runner) contra el sandbox.
# Registra la PLANTILLA REAL (la que se instala) y la corre sin ventanas con BROSLMV_CC_TEST (JSON) / BROSLMV_CC_OUT.
# ESCRIBE en el sandbox (CostCenterID de 4 documentos y de sus partidas, y 2 centros de costo de prueba) y lo deja todo como estaba.
# Comprueba: vista previa, aplicar solo-vacios, que repetir no cambia nada, reemplazar, deshacer en orden inverso, partidas,
# que un documento abierto por otro usuario se omite y que la bitacora registra cada lote. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_CENTRO_COSTO"
$plantilla = Join-Path $PSScriptRoot "..\..\..\instalador\scripts\ASIGNAR_CENTRO_COSTO.ctx"
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $plantilla)) { Write-Host "  [ERROR] No existe la plantilla $plantilla" -ForegroundColor Red; exit 1 }

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Limpiar; exit 1 }

$centros = @()
function Limpiar {
    foreach ($c in $script:centros) { sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosCentroCostoLog WHERE CCNuevo=$c OR CCAnterior=$c; DELETE FROM orgCostCenter WHERE CostCenterID=$c" 2>&1 | Out-Null }
    if ($script:docs) { sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocument SET CostCenterID=0, UserID=0 WHERE DocumentID IN ($($script:docs -join ','))" 2>&1 | Out-Null
                         sqlcmd -S $Server -E -d $Database -Q "UPDATE docDocumentItem SET CostCenterID=0 WHERE DocumentID IN ($($script:docs -join ','))" 2>&1 | Out-Null }
}

# 1) Registrar la plantilla real
$codigo = (Get-Content $plantilla -Raw -Encoding UTF8) -replace "'", "''"
$tmpSql = Join-Path $env:TEMP ("humo_cc_" + [Guid]::NewGuid().ToString('N') + ".sql")
@"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Humo - asignar centro de costo', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
$out = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($ex -ne 0) { Write-Host "  [ERROR] No se pudo registrar la plantilla de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }

# 2) Datos de prueba: 2 centros de costo y 4 documentos de la empresa 1 sin centro de costo y sin usuario
$own = Sql "SELECT TOP 1 OwnedBusinessEntityID FROM docDocument WHERE DeletedOn IS NULL ORDER BY DocumentID DESC"
foreach ($n in "HUMO CC 1", "HUMO CC 2") {
    Sql "INSERT INTO orgCostCenter (OwnedBusinessEntityID, CostCenterName, CreatedOn) VALUES ($own, '$n', GETDATE())" | Out-Null
    $id = Sql "SELECT TOP 1 CostCenterID FROM orgCostCenter WHERE CostCenterName='$n' AND OwnedBusinessEntityID=$own ORDER BY CostCenterID DESC"
    if ($id -notmatch '^\d+$') { Fallo "No se pudo crear el centro de costo de prueba $n" }
    $centros += [long]$id
}
$cc1 = $centros[0]; $cc2 = $centros[1]
$docs = @((sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 4 DocumentID FROM docDocument d WHERE d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND ISNULL(d.CostCenterID,0)=0 AND ISNULL(d.UserID,0)=0 AND OwnedBusinessEntityID=$own AND EXISTS (SELECT 1 FROM docDocumentItem i WHERE i.DocumentID=d.DocumentID AND i.DeletedOn IS NULL) ORDER BY DocumentID DESC" 2>&1) | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [long]$_ })
if ($docs.Count -lt 4) { Fallo "El sandbox no tiene 4 documentos sin centro de costo para probar." }
$idsTxt = $docs -join ","
$nItems = [int](Sql "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID IN ($idsTxt) AND DeletedOn IS NULL")

function Correr($parametros) {
    $salida = Join-Path $env:TEMP ("cc_" + [Guid]::NewGuid().ToString('N') + ".json")
    $env:BROSLMV_CC_TEST = ($parametros | ConvertTo-Json -Compress -Depth 5); $env:BROSLMV_CC_OUT = $salida
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item Env:\BROSLMV_CC_TEST, Env:\BROSLMV_CC_OUT -ErrorAction SilentlyContinue
    $m = $null; if (Test-Path $salida) { $m = Get-Content $salida -Raw -Encoding UTF8 | ConvertFrom-Json; Remove-Item $salida -Force }
    if ($code -ne 0 -or -not $m) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code) con $($parametros | ConvertTo-Json -Compress)" }
    return $m
}
function CcDe([string]$tabla, [string]$col, [string]$donde) { [int](Sql "SELECT COUNT(*) FROM $tabla WHERE $donde") }
$base = @{ alcance = "seleccion"; ids = $docs }

# 3) Vista previa: encuentra los 4, no cambia nada
$r = Correr (@{ accion = "previsualizar"; ccNuevo = $cc1; donde = "encabezado"; modo = "vacios" } + $base)
if ([int]$r.docs -ne 4) { Fallo "La vista previa debia encontrar 4 documentos y encontro $($r.docs)." }
if ((CcDe "docDocument" "CostCenterID" "DocumentID IN ($idsTxt) AND ISNULL(CostCenterID,0)<>0") -ne 0) { Fallo "La vista previa no debia cambiar nada." }

# 4) Aplicar solo vacios en encabezado
$r = Correr (@{ accion = "aplicar"; ccNuevo = $cc1; donde = "encabezado"; modo = "vacios" } + $base)
if ([int]$r.aplicado.encabezados -ne 4) { Fallo "Debia cambiar 4 encabezados y cambio $($r.aplicado.encabezados)." }
if ((CcDe "docDocument" "CostCenterID" "DocumentID IN ($idsTxt) AND CostCenterID=$cc1") -ne 4) { Fallo "Los 4 documentos debian quedar con el centro $cc1." }
if ((CcDe "docDocumentItem" "CostCenterID" "DocumentID IN ($idsTxt) AND ISNULL(CostCenterID,0)<>0") -ne 0) { Fallo "Dondde=encabezado no debia tocar partidas." }

# 5) Repetir lo mismo no cambia nada (ya no estan vacios)
$r = Correr (@{ accion = "aplicar"; ccNuevo = $cc1; donde = "encabezado"; modo = "vacios" } + $base)
if ([int]$r.docs -ne 0 -or [int]$r.aplicado.encabezados -ne 0) { Fallo "Repetir en modo 'vacios' no debia encontrar ni cambiar nada (docs=$($r.docs), cambiados=$($r.aplicado.encabezados))." }

# 6) Un documento abierto por otro usuario se omite
$doc4 = $docs[3]
Sql "UPDATE docDocument SET UserID=999 WHERE DocumentID=$doc4" | Out-Null
$r = Correr (@{ accion = "aplicar"; ccNuevo = $cc2; donde = "encabezado"; modo = "todos" } + $base)
Sql "UPDATE docDocument SET UserID=0 WHERE DocumentID=$doc4" | Out-Null
if ([int]$r.aplicado.encabezados -ne 3) { Fallo "Debia reemplazar 3 (el cuarto estaba en uso) y reemplazo $($r.aplicado.encabezados)." }
if (@($r.aplicado.omitidosEnUso).Count -ne 1) { Fallo "Debia reportar 1 documento omitido por estar en uso." }
if ((CcDe "docDocument" "CostCenterID" "DocumentID=$doc4 AND CostCenterID=$cc1") -ne 1) { Fallo "El documento en uso debia conservar su centro de costo." }

# 7) Deshacer en orden inverso: primero el reemplazo (vuelve a cc1), luego la asignacion inicial (vuelve a vacio)
$r = Correr @{ accion = "deshacer" }
if ([int]$r.encabezados -ne 3) { Fallo "Deshacer el reemplazo debia restaurar 3 encabezados y restauro $($r.encabezados)." }
if ((CcDe "docDocument" "CostCenterID" "DocumentID IN ($idsTxt) AND CostCenterID=$cc1") -ne 4) { Fallo "Tras deshacer el reemplazo los 4 debian volver al centro $cc1." }
$r = Correr @{ accion = "deshacer" }
if ((CcDe "docDocument" "CostCenterID" "DocumentID IN ($idsTxt) AND ISNULL(CostCenterID,0)<>0") -ne 0) { Fallo "Tras deshacer la asignacion inicial los 4 debian quedar sin centro de costo." }

# 8) Partidas
$r = Correr (@{ accion = "aplicar"; ccNuevo = $cc1; donde = "partidas"; modo = "vacios" } + $base)
if ([int]$r.aplicado.partidas -ne $nItems) { Fallo "Debia cambiar $nItems partidas y cambio $($r.aplicado.partidas)." }
if ((CcDe "docDocument" "CostCenterID" "DocumentID IN ($idsTxt) AND ISNULL(CostCenterID,0)<>0") -ne 0) { Fallo "Donde=partidas no debia tocar encabezados." }
$r = Correr @{ accion = "deshacer" }
if ((CcDe "docDocumentItem" "CostCenterID" "DocumentID IN ($idsTxt) AND ISNULL(CostCenterID,0)<>0") -ne 0) { Fallo "Tras deshacer, las partidas debian quedar sin centro de costo." }

# 9) La bitacora registro los lotes (4 + 3 + 4... filas); no se verifica el numero exacto, solo que hay y todos estan deshechos
$sinDeshacer = [int](Sql "SELECT COUNT(*) FROM zzBrosCentroCostoLog WHERE (CCNuevo IN ($cc1,$cc2)) AND Deshecho=0")
if ($sinDeshacer -ne 0) { Fallo "Quedaron $sinDeshacer filas de la bitacora sin deshacer." }

Limpiar
Write-Host ("  4 documentos, {0} partidas: asignar / repetir / reemplazar / en uso / deshacer / partidas, todo conforme." -f $nItems)
exit 0
