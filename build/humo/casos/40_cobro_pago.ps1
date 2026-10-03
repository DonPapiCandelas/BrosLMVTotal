# Caso de humo #40: la plantilla de fabrica COBRO_PAGO_CSHARP_WEBVIEW2.ctx (nucleo compartido por las cuatro variantes) corriendo headless (BrosLMV.Runner) contra el laboratorio.
# Crea con la plantilla CREAR_DOCUMENTO documentos nuevos («DEMO COBRO ...») y les aplica cobros y pagos de verdad: parcial por transferencia, liquidacion en efectivo, un pago
# a dos documentos a la vez y un sobrepago que debe rechazarse. Comprueba en la base las siete tablas de la receta, el saldo y el estatus del documento.
# Los documentos y operaciones quedan en BROSLMV_DESARROLLO. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
$scripts = Join-Path $PSScriptRoot "..\..\..\instalador\scripts"
$plantillas = @{ HUMO_COBRO_DOC = (Join-Path $scripts "CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx"); HUMO_COBRO_PAGO = (Join-Path $scripts "COBRO_PAGO_CSHARP_WEBVIEW2.ctx") }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
foreach ($p in $plantillas.Values) { if (-not (Test-Path $p)) { Write-Host "  [ERROR] No existe $p" -ForegroundColor Red; exit 1 } }

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Limpiar { foreach ($k in $plantillas.Keys) { sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$k'" | Out-Null } }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Limpiar; exit 1 }

foreach ($k in $plantillas.Keys) {
    $codigo = (Get-Content $plantillas[$k] -Raw -Encoding UTF8) -replace "'", "''"
    $tmpSql = Join-Path $env:TEMP ("humo_pago_" + [Guid]::NewGuid().ToString('N') + ".sql")
    @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$k') UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$k';
ELSE INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$k', 'Humo - $k', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
    $out = sqlcmd -S $Server -E -d $Database -x -b -i $tmpSql -W 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
    if ($ex -ne 0) { Write-Host "  [ERROR] No se pudo registrar ${k}:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }
}

Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue
function Correr($appkey, $variableTest, $variableOut, $spec) {
    $salida = Join-Path $env:TEMP ("pago_" + [Guid]::NewGuid().ToString('N') + ".txt")
    Set-Item -Path "Env:\$variableTest" -Value ($ser.Serialize($spec)); Set-Item -Path "Env:\$variableOut" -Value $salida
    $log = & $RunnerExe --appkey $appkey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item "Env:\$variableTest", "Env:\$variableOut" -ErrorAction SilentlyContinue
    if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code)." }
    $t = Get-Content $salida -Raw -Encoding UTF8; Remove-Item $salida -Force
    return $t
}
function Doc($spec) { $t = Correr 'HUMO_COBRO_DOC' 'BROSLMV_DOC_TEST' 'BROSLMV_DOC_OUT' $spec; if ($t -notmatch '^DOC (\d+)$') { Fallo "No se creo el documento: $t" }; return [long]$Matches[1] }
function Pago($spec) { return Correr 'HUMO_COBRO_PAGO' 'BROSLMV_PAGO_TEST' 'BROSLMV_PAGO_OUT' $spec }
function Cerca($a, $b, $tol = 0.011) { return ([math]::Abs([double]$a - [double]$b) -le $tol) }
function Fila($doc) { return ((Sql "SELECT CONCAT(Total,'|',Balance,'|',TotalPaid,'|',StatusPaidID) FROM docDocument WHERE DocumentID=$doc") -split '\|') }

$hoy = (Get-Date).ToString('yyyy-MM-dd'); $cli = 2; $prov = 10020; $alm = 1

# 1) Catalogo de la plantilla: cuentas, formas de pago y documentos con saldo
$cat = $ser.DeserializeObject((Pago @{ catalogo = $true }))
if (@($cat['cuentas']).Count -lt 1) { Fallo "El laboratorio no tiene una cuenta bancaria (orgFinancialEntity); corre build\laboratorio\sembrar_demo_saldos.ps1." }
if (@($cat['formas']).Count -lt 3) { Fallo "El catalogo de formas de pago viene incompleto." }
$cuenta = [int]$cat['cuentas'][0]['id']
# Lo que la ventana nueva usa para dar contexto: credito, ultimo cobro/pago de cada persona, cuenta predeterminada y folio siguiente de cada tipo
$c0 = @($cat['clientes'] + $cat['proveedores'])[0]
foreach ($k in 'credito', 'ultFecha', 'ultMonto', 'rfc') { if (-not $c0.ContainsKey($k)) { Fallo "El catalogo de personas debia traer '$k'." } }
if (-not $cat['cuentas'][0].ContainsKey('def')) { Fallo "Las cuentas debian indicar cual es la predeterminada." }
if (-not $cat.ContainsKey('folios') -or @($cat['folios'].Keys).Count -ne 2) { Fallo "El catalogo debia traer el folio siguiente de cobro y pago." }
Write-Host ("  Catalogo: {0} cuentas, {1} formas de pago, {2} documentos por cobrar y {3} por pagar con saldo." -f @($cat['cuentas']).Count, @($cat['formas']).Count, @($cat['docsC']).Count, @($cat['docsP']).Count)

# 2) Cobro parcial por transferencia a una factura de cliente nueva (2 x 300 + IVA = 696.00)
$fc = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 3; fecha = $hoy; titulo = 'DEMO COBRO - factura de cliente'; partidas = @(@{ id = 4; cant = 2; precio = 300; desc = 0; imp = 5 }) }
$f = Fila $fc
if (-not (Cerca $f[0] 696) -or -not (Cerca $f[1] 696) -or $f[3] -ne '3') { Fallo "La factura $fc debia nacer con total 696 y saldo 696 sin pagar ($($f -join ' | '))." }
$r = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $cuenta; forma = 3; fecha = $hoy; referencia = 'RASTREO-HUMO'; aplicaciones = @(@{ doc = $fc; monto = 200 }) }
if ($r -notmatch '^OK ') { Fallo "El cobro parcial fallo: $r" }
$f = Fila $fc
if (-not (Cerca $f[1] 496) -or -not (Cerca $f[2] 200) -or $f[3] -ne '2') { Fallo "Tras cobrar 200 debia quedar saldo 496, pagado 200, estatus 2 ($($f -join ' | '))." }
# Consultas en vivo de la ventana: ultimos cobros de la persona y documentos con saldo ya actualizados
$mv = @($ser.DeserializeObject((Pago @{ movimientos = $true; entidad = $cli; tipo = 'cobro' })))
if ($mv.Count -lt 1 -or -not (Cerca $mv[0]['monto'] 200) -or [string]$mv[0]['folio'] -notlike 'COB-*') { Fallo "MovimientosDe: el ultimo cobro del cliente debia ser COB-n por 200." }
$dc = @($ser.DeserializeObject((Pago @{ docs = $true; lado = 'C' })) | Where-Object { [long]$_['id'] -eq $fc })
if ($dc.Count -ne 1 -or -not (Cerca $dc[0]['saldo'] 496)) { Fallo "DocsConSaldo: la factura $fc debia salir con saldo 496 despues del cobro." }
Write-Host ("  Consultas en vivo: ultimo cobro {0} por {1:N2} y saldo actualizado de la factura ({2:N2})." -f $mv[0]['folio'], $mv[0]['monto'], $dc[0]['saldo'])
$op = Sql "SELECT TOP 1 FinancialOperationID FROM docFinancialOperation WHERE DocumentID=$fc ORDER BY FinancialOperationID DESC"
$cabOp = (Sql "SELECT CONCAT(ModuleID,'|',DocRecipientID,'|',DocumentTypeID,'|',Amount,'|',PaymentMethodID,'|',FolioPrefix) FROM docFinancialOperation WHERE FinancialOperationID=$op") -split '\|'
if ($cabOp[0] -ne '248' -or $cabOp[1] -ne '1' -or $cabOp[2] -ne '31' -or -not (Cerca $cabOp[3] 200) -or $cabOp[4] -ne '3' -or $cabOp[5] -ne 'COB') { Fallo "La operacion financiera del cobro no trae los datos esperados ($($cabOp -join ' | '))." }
if ((Sql "SELECT CONCAT(Amount,'|',SaldoAnterior,'|',SaldoInsoluto) FROM docDocumentPayment WHERE FinancialOperationID=$op") -notmatch '^200(\.0+)?\|696(\.0+)?\|496(\.0+)?$') { Fallo "La aplicacion al documento debia ser 200 sobre 696 dejando 496." }
if ([int](Sql "SELECT COUNT(*) FROM docDocumentPaymentEspejo WHERE FinancialOperationID=$op") -ne 1) { Fallo "Debia existir el espejo de la aplicacion." }
if ((Sql "SELECT TrackingNumber FROM docBankTransfer WHERE FinancialOperationID=$op") -ne 'RASTREO-HUMO') { Fallo "La transferencia bancaria debia guardar la referencia." }
$tax = [double](Sql "SELECT ISNULL(SUM(Amount),0) FROM docFinancialOperationTaxDetail WHERE FinancialOperationID=$op")
if (-not (Cerca $tax (96 * 200 / 696) 0.02)) { Fallo "El IVA proporcional del cobro debia ser $([math]::Round(96*200/696,2)) y fue $tax." }
Write-Host "  Cobro parcial de 200 a la factura $fc : saldo 496, estatus parcial, operacion COB, espejo, transferencia con referencia e IVA proporcional ($([math]::Round($tax,2)))."

# 3) Liquidacion en efectivo: sin transferencia bancaria
$r = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $cuenta; forma = 1; fecha = $hoy; aplicaciones = @(@{ doc = $fc; monto = 496 }) }
if ($r -notmatch '^OK ') { Fallo "La liquidacion en efectivo fallo: $r" }
$f = Fila $fc
if (-not (Cerca $f[1] 0) -or -not (Cerca $f[2] 696) -or $f[3] -ne '1') { Fallo "Tras liquidar debia quedar saldo 0, pagado 696, estatus 1 ($($f -join ' | '))." }
$op2 = Sql "SELECT TOP 1 FinancialOperationID FROM docFinancialOperation WHERE DocumentID=$fc ORDER BY FinancialOperationID DESC"
if ([int](Sql "SELECT COUNT(*) FROM docBankTransfer WHERE FinancialOperationID=$op2") -ne 0) { Fallo "El efectivo no debia dejar transferencia bancaria." }
if ([int](Sql "SELECT COUNT(DISTINCT Folio) FROM docFinancialOperation WHERE DocumentID=$fc") -ne 2) { Fallo "Las dos operaciones debian tener folios distintos." }
Write-Host "  Liquidacion en efectivo: saldo 0, estatus pagado, sin transferencia, folios distintos."

# 4) Sobrepago: se rechaza y no cambia nada
$r = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $cuenta; forma = 3; fecha = $hoy; aplicaciones = @(@{ doc = $fc; monto = 1 }) }
if ($r -notmatch '^ERROR .*mayor que su saldo') { Fallo "Un cobro mayor al saldo debia rechazarse con un mensaje claro (salida: $r)." }
if ((Fila $fc)[1] -ne '0' -and -not (Cerca (Fila $fc)[1] 0)) { Fallo "El rechazo no debia cambiar el saldo." }
# Documento de otro cliente
$r = Pago @{ tipo = 'cobro'; entidad = 3; cuenta = $cuenta; forma = 3; fecha = $hoy; aplicaciones = @(@{ doc = $fc; monto = 1 }) }
if ($r -notmatch '^ERROR ') { Fallo "Aplicar a un documento de otro cliente debia rechazarse." }
Write-Host "  Sobrepago y documento ajeno: rechazados con mensaje, sin cambios."

# 5) Pago a proveedor: dos facturas de compra en una sola operacion de captura (una completa y una parcial)
$pa = Doc @{ tipo = 'factura_compra'; almacen = $alm; entidad = $prov; condicion = 3; fecha = $hoy; titulo = 'DEMO COBRO - factura de compra A'; partidas = @(@{ id = 2; cant = 10; precio = 120; desc = 10; imp = 5 }) }
$pb = Doc @{ tipo = 'factura_compra'; almacen = $alm; entidad = $prov; condicion = 3; fecha = $hoy; titulo = 'DEMO COBRO - factura de compra B'; partidas = @(@{ id = 3; cant = 5; precio = 85.5; desc = 0; imp = 5 }) }
$ta = [double](Fila $pa)[0]; $tb = [double](Fila $pb)[0]
$r = Pago @{ tipo = 'pago'; entidad = $prov; cuenta = $cuenta; forma = 3; fecha = $hoy; referencia = 'SPEI-HUMO'; aplicaciones = @(@{ doc = $pa; monto = $ta }, @{ doc = $pb; monto = 100 }) }
if ($r -notmatch '^OK ') { Fallo "El pago a dos documentos fallo: $r" }
$fa = Fila $pa; $fb = Fila $pb
if (-not (Cerca $fa[1] 0) -or $fa[3] -ne '1') { Fallo "La factura A debia quedar liquidada ($($fa -join ' | '))." }
if (-not (Cerca $fb[1] ($tb - 100)) -or $fb[3] -ne '2') { Fallo "La factura B debia quedar con saldo $($tb - 100) y estatus parcial ($($fb -join ' | '))." }
$cabP = (Sql "SELECT CONCAT(ModuleID,'|',DocRecipientID,'|',DocumentTypeID,'|',FolioPrefix) FROM docFinancialOperation WHERE DocumentID=$pa") -split '\|'
if ($cabP[0] -ne '247' -or $cabP[1] -ne '2' -or $cabP[2] -ne '32' -or $cabP[3] -ne 'PAG') { Fallo "La operacion del pago debia ser modulo 247 / proveedor / tipo 32 / PAG ($($cabP -join ' | '))." }
if ([int](Sql "SELECT COUNT(DISTINCT FinancialOperationID) FROM docFinancialOperation WHERE DocumentID IN ($pa,$pb)") -ne 2) { Fallo "Debian crearse dos operaciones (una por documento)." }
Write-Host "  Pago a proveedor a dos facturas ($pa liquidada, $pb parcial): operaciones 247 / PAG, una por documento."

# 6) Validaciones
foreach ($caso in @(@{ spec = @{ tipo = 'pago'; entidad = $prov; cuenta = 0; forma = 3; fecha = $hoy; aplicaciones = @(@{ doc = $pb; monto = 1 }) }; texto = 'cuenta' },
                    @{ spec = @{ tipo = 'pago'; entidad = $prov; cuenta = $cuenta; forma = 3; fecha = $hoy; aplicaciones = @() }; texto = 'al menos un documento' })) {
    $r = Pago $caso.spec
    if ($r -notmatch ('^ERROR .*' + $caso.texto)) { Fallo "Validacion esperada ('$($caso.texto)') no se dio: $r" }
}
Limpiar
Write-Host "  Validaciones de cuenta y de documentos vacios: rechazadas con mensaje."
exit 0
