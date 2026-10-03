# Caso de humo #39: la plantilla de fabrica CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx (el nucleo que comparten las cuatro variantes C#/Python x WebView2/WinForms)
# corriendo headless (BrosLMV.Runner) contra el laboratorio. Crea de verdad los seis tipos de documento y los derivados, y comprueba en la base:
# totales (descuento + IVA), perfil del modulo, vinculos por partida, pendientes por surtir de cada tipo derivado, agenda de pago y vinculo del encabezado.
# Los documentos quedan en BROSLMV_DESARROLLO con titulo «DEMO CREAR DOC ...» (el laboratorio los conserva para verlos en Comercial).
# Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
$AppKey = "HUMO_CREAR_DOC"
$plantilla = Join-Path $PSScriptRoot "..\..\..\instalador\scripts\CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx"
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $plantilla)) { Write-Host "  [ERROR] No existe la plantilla $plantilla" -ForegroundColor Red; exit 1 }

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null; exit 1 }

$codigo = (Get-Content $plantilla -Raw -Encoding UTF8) -replace "'", "''"
$tmpSql = Join-Path $env:TEMP ("humo_doc_" + [Guid]::NewGuid().ToString('N') + ".sql")
@"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey') UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Humo - Crear documento', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
$out = sqlcmd -S $Server -E -d $Database -x -b -i $tmpSql -W 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($ex -ne 0) { Write-Host "  [ERROR] No se pudo registrar la plantilla de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }

Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue
function Correr($spec) {
    $salida = Join-Path $env:TEMP ("crear_doc_" + [Guid]::NewGuid().ToString('N') + ".txt")
    $env:BROSLMV_DOC_TEST = $ser.Serialize($spec); $env:BROSLMV_DOC_OUT = $salida
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item Env:\BROSLMV_DOC_TEST, Env:\BROSLMV_DOC_OUT -ErrorAction SilentlyContinue
    if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code)." }
    $t = Get-Content $salida -Raw -Encoding UTF8; Remove-Item $salida -Force
    return $t
}
function Doc($spec) { $t = Correr $spec; if ($t -notmatch '^DOC (\d+)$') { Fallo "No se creo el documento ($($spec.tipo)): $t" }; return [long]$Matches[1] }
function Pendientes($sel) { $t = Correr @{ pendientes = $true; seleccion = @($sel) }; return $ser.DeserializeObject($t) }
function Cerca($a, $b, $tol = 0.02) { return ([math]::Abs([double]$a - [double]$b) -le $tol) }

$prov = 10020; $cli = 2; $alm = 1
$hoy = (Get-Date).ToString('yyyy-MM-dd')

# 1) Catalogos
$cat = $ser.DeserializeObject((Correr @{ catalogo = $true }))
if (@($cat['almacenes']).Count -lt 1 -or @($cat['proveedores']).Count -lt 1 -or @($cat['productos']).Count -lt 3 -or @($cat['impuestos']).Count -lt 1) { Fallo "El catalogo del formulario viene incompleto." }
# Lo que la ventana nueva usa para decidir: saldo, credito, condicion y descuento de cada persona; existencias por almacen; siguiente folio de cada tipo
$c0 = @($cat['clientes'] + $cat['proveedores'])[0]
foreach ($k in 'saldo', 'credito', 'cond', 'desc', 'rfc', 'ultimo') { if (-not $c0.ContainsKey($k)) { Fallo "El catalogo de personas debia traer '$k'." } }
if (-not $cat.ContainsKey('existencias') -or -not $cat.ContainsKey('folios') -or @($cat['folios'].Keys).Count -ne 6) { Fallo "El catalogo debia traer existencias y el folio siguiente de los 6 tipos." }
$pr0 = @($cat['productos'])[0]; foreach ($k in 'barras', 'lote', 'serie', 'servicio') { if (-not $pr0.ContainsKey($k)) { Fallo "El catalogo de productos debia traer '$k'." } }
Write-Host ("  Catalogo: {0} almacenes, {1} proveedores, {2} productos, {3} impuestos." -f @($cat['almacenes']).Count, @($cat['proveedores']).Count, @($cat['productos']).Count, @($cat['impuestos']).Count)

# 2) Orden de compra con descuento e IVA: 10 x 120 con 10% + 5 x 85.5  ->  subtotal 1507.50, IVA 241.20, total 1748.70
$oc = Doc @{ tipo = 'orden_compra'; almacen = $alm; entidad = $prov; condicion = 3; fecha = $hoy; entrega = $hoy; titulo = 'DEMO CREAR DOC - orden de compra'
             partidas = @(@{ id = 2; cant = 10; precio = 120; desc = 10; imp = 5 }, @{ id = 3; cant = 5; precio = 85.5; desc = 0; imp = 5 }) }
$f = (Sql "SELECT CONCAT(ModuleID,'|',Total,'|',PaymentTermID,'|',Title) FROM docDocument WHERE DocumentID=$oc") -split '\|'
if ($f[0] -ne '183' -or -not (Cerca $f[1] 1748.70) -or $f[2] -ne '3' -or $f[3] -notlike 'DEMO CREAR DOC*') { Fallo "OC $oc : modulo/total/condicion/titulo incorrectos ($($f -join ' | ')); se esperaba 183 | 1748.70 | 3." }
if ([int](Sql "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$oc AND DeletedOn IS NULL") -ne 2) { Fallo "La OC debia tener 2 partidas." }
Write-Host "  Orden de compra $oc : total 1,748.70 correcto (descuento + IVA)."

# 3) Recepcion parcial desde la OC (4 de 10 y 5 de 5): vinculo por partida (DeliverDocumentItemID) y pendientes
$p = @(Pendientes @($oc))
if ($p.Count -ne 1) { Fallo "Pendientes: debia reconocerse 1 documento de origen." }
$pr = @($p[0]['partidasPor']['recepcion']); $pf = @($p[0]['partidasPor']['factura_compra'])
if ($pr.Count -ne 2 -or $pf.Count -ne 2) { Fallo "La OC recien creada debia tener 2 partidas pendientes de recibir y 2 de facturar." }
$rec = Doc @{ tipo = 'recepcion'; almacen = $alm; entidad = $prov; fecha = $hoy; entrega = $hoy; titulo = 'DEMO CREAR DOC - recepcion parcial'; origenes = @($oc)
              partidas = @(@{ id = $pr[0]['id']; cant = 4; precio = $pr[0]['precio']; desc = $pr[0]['desc']; imp = $pr[0]['imp']; origenItem = $pr[0]['origenItem'] }, @{ id = $pr[1]['id']; cant = 5; precio = $pr[1]['precio']; desc = $pr[1]['desc']; imp = $pr[1]['imp']; origenItem = $pr[1]['origenItem'] }) }
if ([int](Sql "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$rec AND DeliverDocumentItemID IN ($($pr[0]['origenItem']),$($pr[1]['origenItem']))") -ne 2) { Fallo "La recepcion $rec debia ligar sus 2 partidas con DeliverDocumentItemID." }
if ((Sql "SELECT SourceDocumentID FROM docDocument WHERE DocumentID=$rec") -ne "$oc") { Fallo "La recepcion debia guardar la OC como SourceDocumentID." }
if ([int](Sql "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID=$rec") -lt 1) { Fallo "La recepcion afecta inventario (StockAffectation 3): debia dejar movimientos de kardex." }
$p = @(Pendientes @($oc)); $pr2 = @($p[0]['partidasPor']['recepcion']); $pf2 = @($p[0]['partidasPor']['factura_compra'])
if ($pr2.Count -ne 1 -or -not (Cerca $pr2[0]['cant'] 6 0.001)) { Fallo "Tras recibir 4 de 10 debian faltar 6 por recibir (queda 1 partida)." }
if ($pf2.Count -ne 2) { Fallo "Recibir no debe reducir lo pendiente de FACTURAR." }
Write-Host "  Recepcion $rec : partidas ligadas, kardex afectado, quedan 6 por recibir y 2 partidas por facturar."

# 4) Factura de compra desde la misma OC: vinculo por partida (SourceDocumentItemID) y agenda de pago
$fc = Doc @{ tipo = 'factura_compra'; almacen = $alm; entidad = $prov; condicion = 3; fecha = $hoy; titulo = 'DEMO CREAR DOC - factura de compra'; origenes = @($oc)
             partidas = @($pf2 | ForEach-Object { @{ id = $_['id']; cant = $_['cant']; precio = $_['precio']; desc = $_['desc']; imp = $_['imp']; origenItem = $_['origenItem'] } }) }
if ([int](Sql "SELECT COUNT(*) FROM docDocumentItem WHERE DocumentID=$fc AND SourceDocumentItemID IN ($($pf2[0]['origenItem']),$($pf2[1]['origenItem']))") -ne 2) { Fallo "La factura de compra $fc debia ligar sus 2 partidas con SourceDocumentItemID." }
if (-not (Cerca (Sql "SELECT Total FROM docDocument WHERE DocumentID=$fc") 1748.70)) { Fallo "La factura de compra debia sumar 1,748.70 (la misma OC completa)." }
if ((Sql "SELECT COUNT(*) FROM docDocumentPaymentAgenda WHERE DocumentID=$fc AND DeletedOn IS NULL AND Amount > 0") -lt 1) { Fallo "La factura de compra debia tener agenda de pago con importe." }
$p = @(Pendientes @($oc)); if (@($p[0]['partidasPor']['factura_compra']).Count -ne 0) { Fallo "Tras facturar la OC completa no debia quedar nada por facturar." }
Write-Host "  Factura de compra $fc : partidas ligadas, agenda de pago con importe, nada pendiente de facturar."

# 5) Factura de cliente (sin inventario) y pedido (compromiso de inventario) hacia el cliente
$fcl = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 3; fecha = $hoy; titulo = 'DEMO CREAR DOC - factura de cliente'; partidas = @(@{ id = 4; cant = 2; precio = 300; desc = 0; imp = 5 }) }
if (-not (Cerca (Sql "SELECT Total FROM docDocument WHERE DocumentID=$fcl") 696.00)) { Fallo "La factura de cliente $fcl debia sumar 696.00." }
if ([int](Sql "SELECT COUNT(*) FROM orgProductKardex WHERE DocumentID=$fcl") -ne 0) { Fallo "La factura de cliente no debe mover inventario." }
$ped = Doc @{ tipo = 'pedido'; almacen = $alm; entidad = $cli; condicion = 4; fecha = $hoy; entrega = $hoy; titulo = 'DEMO CREAR DOC - pedido'; partidas = @(@{ id = 4; cant = 3; precio = 300; desc = 0; imp = 5 }, @{ id = 2; cant = 1; precio = 150; desc = 0; imp = 5 }) }
if ((Sql "SELECT StatusDeliveryID FROM docDocument WHERE DocumentID=$ped") -ne '3') { Fallo "El pedido debia quedar con StatusDeliveryID=3 (por surtir)." }
# Condicion 50%-50% (a la entrega y a 3 meses): dos parcialidades que suman el total
$ag = @((sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; SELECT CONCAT(TotalPerc,'|',Amount,'|',CONVERT(varchar(10),DatePayment,23)) FROM docDocumentPaymentAgenda WHERE DocumentID=$ped AND DeletedOn IS NULL ORDER BY PartialityNumber" 2>&1) | Where-Object { $_ -match '\|' })
$totPed = [double](Sql "SELECT Total FROM docDocument WHERE DocumentID=$ped")
if ($ag.Count -ne 2) { Fallo "La condicion 50%-50% debia dar 2 parcialidades y dio $($ag.Count)." }
$suma = ($ag | ForEach-Object { [double](($_ -split '\|')[1]) } | Measure-Object -Sum).Sum
if (-not (Cerca $suma $totPed 0.011)) { Fallo "Las parcialidades suman $suma y el pedido $totPed." }
if ((($ag[1] -split '\|')[2]) -le (($ag[0] -split '\|')[2])) { Fallo "La segunda parcialidad debia vencer despues de la primera." }
Write-Host "  Factura de cliente $fcl (696.00, sin inventario) y pedido $ped creados."
# Datos fiscales del CFDI, moneda extranjera con tipo de cambio y centro de costo: deben quedar tal cual en el documento
$cat2 = $ser.DeserializeObject((Correr @{ catalogo = $true }))
foreach ($k in 'monedas', 'formas', 'metodos', 'usos', 'centros') { if (@($cat2[$k]).Count -lt 1) { Fallo "El catalogo debia traer '$k'." } }
$cc = [int](Sql "SELECT TOP 1 CostCenterID FROM orgCostCenter WHERE DeletedOn IS NULL ORDER BY CostCenterID")
$ffi = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 1; fecha = $hoy; titulo = 'DEMO CREAR DOC - factura con datos fiscales'; moneda = 2; tc = 18.5; centro = $cc; uso = 'G01'; forma = '03'; metodo = 'PUE'; partidas = @(@{ id = 4; cant = 1; precio = 100; desc = 0; imp = 5 }) }
$cfd = Sql "SELECT ReceptorUsoCFDI + '/' + FormaPago + '/' + MetodoPago FROM docDocumentCFD WHERE DocumentID=$ffi"
if ($cfd -ne 'G01/03/PUE') { Fallo "docDocumentCFD debia quedar G01/03/PUE y quedo '$cfd'." }
$enc = Sql "SELECT CAST(CurrencyID AS varchar(5)) + '/' + CAST(CAST(Rate AS decimal(18,2)) AS varchar(20)) + '/' + CAST(ISNULL(CostCenterID,0) AS varchar(10)) FROM docDocument WHERE DocumentID=$ffi"
if ($enc -ne "2/18.50/$cc") { Fallo "El encabezado debia quedar moneda 2, tipo de cambio 18.50 y centro $cc, y quedo '$enc'." }
Write-Host "  Factura $ffi con datos fiscales: $cfd, moneda USD a 18.50 y centro de costo $cc."


# 6) Remision desde el pedido (vinculo por encabezado): pendientes por producto
$p = @(Pendientes @($ped)); $pend = @($p[0]['partidasPor']['remision'])
if ($pend.Count -ne 2) { Fallo "El pedido debia tener 2 productos pendientes de remitir." }
# La ventana pide en vivo los pendientes de la persona (sin haber seleccionado nada en la lista de Comercial)
$pv = @($ser.DeserializeObject((Correr @{ pendientesDe = $true; entidad = $cli; tipo = 'remision' })))
if (@($pv | Where-Object { [long]$_['id'] -eq $ped }).Count -ne 1) { Fallo "PendientesDe: el pedido $ped debia aparecer como pendiente de remitir para su cliente." }
$ult = @($ser.DeserializeObject((Correr @{ ultimos = $true; entidad = $cli })))
if ($ult.Count -lt 1 -or -not $ult[0].ContainsKey('folio')) { Fallo "UltimosDe: debia regresar los ultimos documentos del cliente." }
Write-Host ("  Consultas en vivo: {0} pendiente(s) de remitir y {1} documento(s) recientes del cliente." -f $pv.Count, $ult.Count)
$rem = Doc @{ tipo = 'remision'; almacen = $alm; entidad = $cli; fecha = $hoy; entrega = $hoy; titulo = 'DEMO CREAR DOC - remision'; origenes = @($ped)
              partidas = @($pend | ForEach-Object { @{ id = $_['id']; cant = $_['cant']; precio = $_['precio']; desc = $_['desc']; imp = $_['imp']; origenItem = $_['origenItem'] } }) }
if ((Sql "SELECT SourceDocumentID FROM docDocument WHERE DocumentID=$rem") -ne "$ped") { Fallo "La remision debia guardar el pedido como SourceDocumentID." }
$p = @(Pendientes @($ped)); if (@($p[0]['partidasPor']['remision']).Count -ne 0) { Fallo "Tras remitir todo el pedido no debia quedar nada pendiente." }
Write-Host "  Remision $rem : ligada al pedido por encabezado, pedido completamente surtido."

# 7) Validaciones: un documento sin partidas o con cantidad cero no se crea
$t = Correr @{ tipo = 'orden_compra'; almacen = $alm; entidad = $prov; partidas = @() }
if ($t -notmatch 'Agrega al menos una partida') { Write-Host "  (salida: $t)"; Fallo "Un documento sin partidas debia rechazarse con un mensaje claro." }
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null
Write-Host "  Validacion de partidas vacias: rechazada con mensaje."
exit 0
