# Caso de humo #46: cobros y pagos con MONEDA EXTRANJERA, PARCIALIDADES y la operacion unica (un folio, un renglon por documento) como lo hace Tesorería.
# Crea con la plantilla CREAR_DOCUMENTO documentos de prueba («DEMO FX ...») en dolares y en pesos, y les aplica cobros y pagos de verdad:
#   cuenta en pesos -> factura en dolares (importe = pesos / tipo de cambio), cuenta en dolares -> factura en dolares, cuenta en dolares -> factura en pesos (importe = dolares x tipo de cambio),
#   parcialidades (en orden y una especifica), varios documentos en UN solo folio, y los rechazos (sin tipo de cambio, combinacion de monedas imposible, parcialidad excedida).
# Comprueba en la base los campos de la operacion (signo, FinancialEntityAmount, Rate, AmountRate), de cada renglon (Rate, AmountPaidCurrency = Amount x Rate, parcialidad, saldos) y el saldo del documento.
# Los documentos y operaciones quedan en BROSLMV_DESARROLLO. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
$scripts = Join-Path $PSScriptRoot "..\..\..\instalador\scripts"
$combinado = Join-Path $env:TEMP "humo_pago_combinado46"
& python (Join-Path $PSScriptRoot "..\..\plantillas_documentos\generar.py") --combinado $combinado | Out-Null
$plantillas = @{ HUMO_FX_DOC = (Join-Path $combinado "CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx"); HUMO_FX_PAGO = (Join-Path $combinado "COBRO_PAGO_CSHARP_WEBVIEW2.ctx") }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' } | Select-Object -First 1) }
function Limpiar { foreach ($k in $plantillas.Keys) { sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='$k'" | Out-Null } }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Limpiar; exit 1 }
foreach ($k in $plantillas.Keys) {
    $codigo = (Get-Content $plantillas[$k] -Raw -Encoding UTF8) -replace "'", "''"
    $tmpSql = Join-Path $env:TEMP ("humo_fx_" + [Guid]::NewGuid().ToString('N') + ".sql")
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
    $salida = Join-Path $env:TEMP ("fx_" + [Guid]::NewGuid().ToString('N') + ".txt")
    Set-Item -Path "Env:\$variableTest" -Value ($ser.Serialize($spec)); Set-Item -Path "Env:\$variableOut" -Value $salida
    $log = & $RunnerExe --appkey $appkey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item "Env:\$variableTest", "Env:\$variableOut" -ErrorAction SilentlyContinue
    if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code)." }
    $t = Get-Content $salida -Raw -Encoding UTF8; Remove-Item $salida -Force
    return $t
}
function Doc($spec) { $t = Correr 'HUMO_FX_DOC' 'BROSLMV_DOC_TEST' 'BROSLMV_DOC_OUT' $spec; if ($t -notmatch '^DOC (\d+)$') { Fallo "No se creo el documento: $t" }; return [long]$Matches[1] }
function Pago($spec) { return Correr 'HUMO_FX_PAGO' 'BROSLMV_PAGO_TEST' 'BROSLMV_PAGO_OUT' $spec }
function Cerca($a, $b, $tol = 0.011) { return ([math]::Abs([double]$a - [double]$b) -le $tol) }
function Lineas($op) { $res = New-Object System.Collections.ArrayList; foreach ($ln in (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; SELECT DocumentID, PartialityNumber, Amount, Rate, AmountPaidCurrency, SaldoAnterior, SaldoInsoluto FROM docDocumentPayment WHERE FinancialOperationID=$op AND DeletedOn IS NULL ORDER BY DocumentPaymentID" 2>&1 | Where-Object { $_ -ne '' })) { [void]$res.Add(($ln -split '\|')) }; return ,$res }
function Op($op) { return ((Sql "SELECT CONCAT(DebitCreditCoef,'|',Amount,'|',CurrencyID,'|',FinancialEntityAmount,'|',Rate,'|',AmountRate,'|',PartialityNumber,'|',DocumentID,'|',Folio) FROM docFinancialOperation WHERE FinancialOperationID=$op") -split '\|') }
function OpDe($texto) { if ($texto -notmatch '(COB|PAG)-(\d+)') { Fallo "El resumen no trae el folio: $texto" }; $folio = $Matches[2]; $pref = $Matches[1]; return [long](Sql "SELECT TOP 1 FinancialOperationID FROM docFinancialOperation WHERE FolioPrefix=N'$pref' AND Folio=N'$folio' ORDER BY FinancialOperationID DESC") }

$hoy = (Get-Date).ToString('yyyy-MM-dd'); $cli = 20025; $alm = 1
$ctaPesos = [int](Sql "SELECT TOP 1 FinancialEntityID FROM orgFinancialEntity WHERE DeletedOn IS NULL AND ISNULL(CurrencyID,0) IN (0,3) ORDER BY FinancialEntityID")
$ctaUsd = [int](Sql "SELECT TOP 1 FinancialEntityID FROM orgFinancialEntity WHERE DeletedOn IS NULL AND CurrencyID = 2 ORDER BY FinancialEntityID")
if ($ctaPesos -le 0 -or $ctaUsd -le 0) { Fallo "El laboratorio necesita una cuenta en pesos y una en dolares (orgFinancialEntity.CurrencyID = 2)." }
$prod = [int](Sql "SELECT TOP 1 ProductID FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID = 5 ORDER BY ProductID")

# 1) Catalogo: cada cuenta trae su moneda; el catalogo trae las monedas con su tipo de cambio; cada documento con saldo, su moneda, parcialidades y aplicaciones
$cat = $ser.DeserializeObject((Pago @{ catalogo = $true }))
$cu = @($cat['cuentas'] | Where-Object { [int]$_['id'] -eq $ctaUsd })[0]
if ([int]$cu['moneda'] -ne 2) { Fallo "La cuenta en dolares debia traer moneda 2 y trajo $($cu['moneda'])." }
if (@($cat['monedas']).Count -lt 2) { Fallo "El catalogo debia traer las monedas." }

# 2) Factura de cliente en DOLARES con dos parcialidades (50%-50%): 100 USD + IVA = 116 USD, tipo de cambio 18.50
$fu = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 4; fecha = $hoy; titulo = 'DEMO FX - factura USD'; moneda = 2; tc = 18.5; partidas = @(@{ id = $prod; cant = 1; precio = 100; desc = 0; imp = 5 }) }
$ag = @(sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; SELECT PartialityNumber, Amount FROM docDocumentPaymentAgenda WHERE DocumentID=$fu AND DeletedOn IS NULL ORDER BY PartialityNumber" 2>&1 | Where-Object { $_ -ne '' })
if ($ag.Count -ne 2) { Fallo "La factura en dolares con condicion 50%-50% debia tener 2 parcialidades y tiene $($ag.Count)." }
$det = $ser.DeserializeObject((Pago @{ detalle = $true; doc = $fu }))
if (@($det['parc']).Count -ne 2 -or [string]$det['simbolo'] -ne 'USD' -or -not (Cerca $det['parc'][0]['saldo'] 58) -or -not (Cerca $det['parc'][1]['saldo'] 58)) { Fallo "El detalle debia traer 2 parcialidades de 58 USD y moneda USD: $($ser.Serialize($det))" }
Write-Host "  Factura USD $fu : 116 USD en 2 parcialidades de 58 (detalle con moneda y parcialidades)."

# 3) Cuenta en PESOS cobrando la factura en DOLARES: 925 MXN a 18.50 = 50 USD, a la parcialidad 1 (en orden)
$r1 = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaPesos; forma = 3; fecha = $hoy; referencia = 'FX-1'; tc = 18.5; aplicaciones = @(@{ doc = $fu; monto = 925 }) }
if ($r1 -notmatch '^OK ') { Fallo "El cobro en pesos a factura USD fallo: $r1" }
$op1 = OpDe $r1; $o = Op $op1
if ($o[0] -ne '1' -or -not (Cerca $o[1] 925) -or $o[2] -ne '3' -or -not (Cerca $o[3] 925) -or -not (Cerca $o[4] 1) -or -not (Cerca $o[5] 925) -or $o[6] -ne '0' -or $o[7] -ne '0') { Fallo "Operacion del cobro en pesos mal guardada: $($o -join '|')" }
$l = Lineas $op1
if ($l.Count -ne 1 -or -not (Cerca $l[0][2] 50) -or -not (Cerca $l[0][3] 18.5) -or -not (Cerca $l[0][4] 925) -or [int]$l[0][1] -ne 1 -or -not (Cerca $l[0][5] 116) -or -not (Cerca $l[0][6] 66)) { Fallo "Renglon del cobro en pesos mal guardado: $($l | ForEach-Object { $_ -join '|' })" }
$b = [double](Sql "SELECT Balance FROM docDocument WHERE DocumentID=$fu"); if (-not (Cerca $b 66)) { Fallo "La factura USD debia quedar con saldo 66 USD y quedo $b." }
Write-Host "  Cobro de 925 MXN a 18.50 = 50 USD (parc. 1): operacion MXN con signo +1, renglon en USD con Rate 18.50 y valor en pesos 925; saldo 66 USD."

# 4) Cuenta en DOLARES cobrando la MISMA factura: 25 USD a 18.40; el reparto sigue en orden (queda 8 en la parc. 1 y 17 en la parc. 2)
$r2 = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaUsd; forma = 3; fecha = $hoy; tc = 18.4; aplicaciones = @(@{ doc = $fu; monto = 25 }) }
if ($r2 -notmatch '^OK ') { Fallo "El cobro en dolares a factura USD fallo: $r2" }
$op2 = OpDe $r2; $o = Op $op2
if ($o[0] -ne '1' -or -not (Cerca $o[1] 25) -or $o[2] -ne '2' -or -not (Cerca $o[3] 25) -or -not (Cerca $o[4] 18.4) -or -not (Cerca $o[5] 460)) { Fallo "Operacion del cobro en dolares mal guardada: $($o -join '|')" }
$l = Lineas $op2
if ($l.Count -ne 2 -or [int]$l[0][1] -ne 1 -or -not (Cerca $l[0][2] 8) -or [int]$l[1][1] -ne 2 -or -not (Cerca $l[1][2] 17) -or -not (Cerca $l[0][3] 18.4) -or -not (Cerca $l[1][4] 312.8)) { Fallo "Renglones del cobro en dolares mal repartidos: $($l | ForEach-Object { $_ -join '|' })" }
$det = $ser.DeserializeObject((Pago @{ detalle = $true; doc = $fu }))
if (@($det['aplic']).Count -ne 3 -or -not (Cerca $det['saldo'] 41) -or -not (Cerca $det['parc'][0]['saldo'] 0) -or -not (Cerca $det['parc'][1]['saldo'] 41)) { Fallo "Tras dos cobros el detalle debia tener 3 aplicaciones, saldo 41 y parc. 1 liquidada: $($ser.Serialize($det))" }
Write-Host "  Cobro de 25 USD a 18.40: operacion en USD (Rate 18.40, valor 460 MXN), reparto en orden 8 (parc. 1, que queda liquidada) + 17 (parc. 2); saldo 41 USD y 3 aplicaciones en el detalle."

# 5) Parcialidad especifica: 10 USD directo a la parcialidad 2; y una parcialidad excedida se rechaza
$r3 = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaUsd; forma = 3; fecha = $hoy; tc = 18.4; aplicaciones = @(@{ doc = $fu; monto = 10; parcialidad = 2 }) }
if ($r3 -notmatch '^OK ') { Fallo "El cobro a la parcialidad 2 fallo: $r3" }
$l = Lineas (OpDe $r3); if ($l.Count -ne 1 -or [int]$l[0][1] -ne 2 -or -not (Cerca $l[0][2] 10)) { Fallo "El renglon debia quedar en la parcialidad 2." }
$mal = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaUsd; forma = 3; fecha = $hoy; tc = 18.4; aplicaciones = @(@{ doc = $fu; monto = 500; parcialidad = 2 }) }
if ($mal -notmatch 'parcialidad 2 solo tiene pendiente|mayor que su saldo') { Fallo "Una parcialidad excedida debia rechazarse: $mal" }
Write-Host "  Parcialidad especifica (10 USD a la parc. 2) y exceso rechazado con mensaje."

# 6) Cuenta en DOLARES cobrando una factura en PESOS: 10 USD a 18.00 = 180 MXN (Rate 1 en el renglon; la operacion guarda el tipo de cambio)
$fp = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 1; fecha = $hoy; titulo = 'DEMO FX - factura MXN'; partidas = @(@{ id = $prod; cant = 1; precio = 500; desc = 0; imp = 5 }) }
$r4 = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaUsd; forma = 3; fecha = $hoy; tc = 18; aplicaciones = @(@{ doc = $fp; monto = 10 }) }
if ($r4 -notmatch '^OK ') { Fallo "El cobro en dolares a factura en pesos fallo: $r4" }
$op4 = OpDe $r4; $o = Op $op4; $l = Lineas $op4
if (-not (Cerca $o[1] 10) -or $o[2] -ne '2' -or -not (Cerca $o[4] 18) -or -not (Cerca $o[5] 180) -or $l.Count -ne 1 -or -not (Cerca $l[0][2] 180) -or -not (Cerca $l[0][3] 1) -or -not (Cerca $l[0][4] 180)) { Fallo "Cobro USD a factura MXN mal guardado: op $($o -join '|') renglon $($l | ForEach-Object { $_ -join '|' })" }
$b = [double](Sql "SELECT Balance FROM docDocument WHERE DocumentID=$fp"); if (-not (Cerca $b 400)) { Fallo "La factura en pesos (580) debia quedar con 400 y quedo $b." }
Write-Host "  Cobro de 10 USD a 18.00 a una factura en pesos: renglon de 180 MXN con Rate 1, operacion en USD; saldo 400."

# 7) UNA operacion para varios documentos: dos facturas en pesos, un solo folio, dos renglones
$fq = Doc @{ tipo = 'factura_cliente'; almacen = $alm; entidad = $cli; condicion = 1; fecha = $hoy; titulo = 'DEMO FX - factura MXN 2'; partidas = @(@{ id = $prod; cant = 2; precio = 100; desc = 0; imp = 5 }) }
$r5 = Pago @{ tipo = 'cobro'; entidad = $cli; cuenta = $ctaPesos; forma = 3; fecha = $hoy; aplicaciones = @(@{ doc = $fp; monto = 100 }, @{ doc = $fq; monto = 232 }) }
if ($r5 -notmatch '^OK ') { Fallo "El cobro a dos documentos fallo: $r5" }
$op5 = OpDe $r5; $o = Op $op5; $l = Lineas $op5
if ($l.Count -ne 2 -or -not (Cerca $o[1] 332) -or -not (Cerca $o[3] 332) -or -not (Cerca $o[4] 1) -or -not (Cerca $o[5] 332)) { Fallo "Dos documentos debian quedar en UNA operacion de 332: op $($o -join '|') renglones $($l.Count)" }
$n = [int](Sql "SELECT COUNT(DISTINCT FinancialOperationID) FROM docDocumentPayment WHERE DocumentID IN ($fp,$fq) AND FinancialOperationID=$op5"); if ($n -ne 1) { Fallo "Debia ser una sola operacion." }
Write-Host "  Dos documentos en UNA operacion (un folio, dos renglones, 332 MXN)."

# 8) Pago a proveedor (cuenta en dolares, factura de compra en dolares) y rechazos
$prov = 10020
$fc = Doc @{ tipo = 'factura_compra'; almacen = $alm; entidad = $prov; condicion = 1; fecha = $hoy; titulo = 'DEMO FX - factura compra USD'; moneda = 2; tc = 18.5; partidas = @(@{ id = $prod; cant = 1; precio = 200; desc = 0; imp = 5 }) }
$r6 = Pago @{ tipo = 'pago'; entidad = $prov; cuenta = $ctaUsd; forma = 3; fecha = $hoy; tc = 18.6; aplicaciones = @(@{ doc = $fc; monto = 100 }) }
if ($r6 -notmatch '^OK ') { Fallo "El pago en dolares a factura de compra USD fallo: $r6" }
$o = Op (OpDe $r6); if ($o[0] -ne '-1' -or -not (Cerca $o[1] 100) -or -not (Cerca $o[3] -100) -or -not (Cerca $o[4] 18.6) -or -not (Cerca $o[5] 1860)) { Fallo "Pago en dolares mal guardado (signo -1): $($o -join '|')" }
$sin = Pago @{ tipo = 'pago'; entidad = $prov; cuenta = $ctaPesos; forma = 3; fecha = $hoy; aplicaciones = @(@{ doc = $fc; monto = 100 }) }
if ($sin -notmatch 'tipo de cambio') { Fallo "Sin tipo de cambio debia rechazarse: $sin" }
Write-Host "  Pago de 100 USD a 18.60: operacion con signo -1 y valor 1,860 MXN; sin tipo de cambio se rechaza."
Limpiar
Write-Host "  Moneda, parcialidades y operacion unica: correctos."
exit 0
