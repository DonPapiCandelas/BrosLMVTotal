# Caso de humo #38: las plantillas de fabrica ESTADO_CUENTA_CLIENTES.ctx y ESTADO_CUENTA_PROVEEDORES.ctx corriendo headless (BrosLMV.Runner) contra el laboratorio.
# Cada una solo carga SU lado (clientes = por cobrar, proveedores = por pagar): se comprueba que no se mezclan y se juntan para validar los saldos.
# Registra la PLANTILLA REAL, la corre sin ventanas con BROSLMV_SALDOS_TEST y BROSLMV_SALDOS_OUT, y comprueba que el saldo que se reconstruye
# desde los hechos (Total - pagos vigentes <= corte - notas de credito aplicadas) coincide, documento por documento, con:
#   (a) docDocument.Balance cuando el corte es hoy, y
#   (b) un calculo independiente en SQL cuando el corte es 25 dias atras (antes de un abono de la siembra «DEMO SALDOS»).
# Requiere la siembra: build\laboratorio\sembrar_demo_saldos.ps1. Solo LEE (registra el boton de prueba). Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_SALDOS"
$plantillas = [ordered]@{ C = (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\ESTADO_CUENTA_CLIENTES.ctx"); P = (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\ESTADO_CUENTA_PROVEEDORES.ctx") }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero." -ForegroundColor Red; exit 1 }
foreach ($pl in $plantillas.Values) { if (-not (Test-Path $pl)) { Write-Host "  [ERROR] No existe la plantilla $pl" -ForegroundColor Red; exit 1 } }

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 | Where-Object { $_ -ne '' }) }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; exit 1 }
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue

# 1) y 2) Registrar cada plantilla real, correrla sin ventanas y leer su modelo; despues se juntan los dos lados
$docs = @(); $pagos = @(); $hoy = $null
foreach ($lado in $plantillas.Keys) {
    $codigo = (Get-Content $plantillas[$lado] -Raw -Encoding UTF8) -replace "'", "''"
    $tmpSql = Join-Path $env:TEMP ("humo_saldos_" + [Guid]::NewGuid().ToString('N') + ".sql")
    @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Humo - Saldos', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
    $out = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
    if ($ex -ne 0) { Write-Host "  [ERROR] No se pudo registrar la plantilla de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }
    $salida = Join-Path $env:TEMP ("saldos_" + [Guid]::NewGuid().ToString('N') + ".json"); $htmlP = Join-Path $env:TEMP ("saldos_" + [Guid]::NewGuid().ToString('N') + ".html")
    $env:BROSLMV_SALDOS_TEST = '{"meses":12}'; $env:BROSLMV_SALDOS_OUT = $salida; $env:BROSLMV_SALDOS_HTML = $htmlP
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1; $code = $LASTEXITCODE
    Remove-Item Env:\BROSLMV_SALDOS_TEST, Env:\BROSLMV_SALDOS_OUT, Env:\BROSLMV_SALDOS_HTML -ErrorAction SilentlyContinue
    if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner fallo con la plantilla del lado $lado (exit $code)." }
    $m = $ser.DeserializeObject((Get-Content $salida -Raw -Encoding UTF8)); Remove-Item $salida -Force
    $dl = @($m['docs']); $pl = @($m['pagos']); $hoy = [string]$m['hoy']
    # Cada plantilla solo trae SU lado: ni un documento del otro modulo
    $ajenos = @($dl | Where-Object { $_['lado'] -ne $lado })
    if ($ajenos.Count -gt 0) { Fallo "La plantilla del lado $lado trajo $($ajenos.Count) documento(s) del otro lado." }
    $h = Get-Content $htmlP -Raw -Encoding UTF8; Remove-Item $htmlP -Force
    if ($h -notmatch 'LADO=.' + $lado) { Fallo "El HTML del lado $lado no declara su lado." }
    if ($h -match 'segLado|setLado') { Fallo "El HTML ya no debe traer el selector cobrar/pagar." }
    foreach ($necesario in 'vistaCal', 'verDoc', 'cargaExcel', 'Exportar a Excel') { if ($h -notmatch [regex]::Escape($necesario)) { Fallo "Al HTML del lado $lado le falta: $necesario." } }
    Write-Host ("  Lado {0}: {1} documentos, {2} pagos; ningun documento del otro lado." -f $lado, $dl.Count, $pl.Count)
    $docs += $dl; $pagos += $pl
}
Write-Host ("  Modelo conjunto: {0} documentos, {1} pagos." -f $docs.Count, $pagos.Count)
$demo = @($docs | Where-Object { $_['titulo'] -like 'DEMO SALDOS*' })
if ($demo.Count -lt 10) { Fallo "Faltan los documentos de la siembra (hay $($demo.Count), se esperaban 10). Corre build\laboratorio\sembrar_demo_saldos.ps1." }

# 3) Saldo reconstruido a una fecha de corte
function SaldoA($d, [string]$corte) {
    $ap = 0.0
    foreach ($p in $pagos) {
        if ([string]$p['fecha'] -gt $corte) { continue }
        if ([long]$p['doc'] -eq [long]$d['id'] -or [long]$p['nc'] -eq [long]$d['id']) { $ap += [double]$p['monto'] }
    }
    return [double]$d['total'] - $ap
}
$hoy = [string]$m['hoy']
$corteAnt = ([datetime]::ParseExact($hoy, 'yyyy-MM-dd', $null)).AddDays(-25).ToString('yyyy-MM-dd')

# (a) corte = hoy: debe coincidir con docDocument.Balance de cada documento sembrado
foreach ($d in $demo) {
    $esperado = [double](Sql "SELECT Balance FROM docDocument WHERE DocumentID=$($d['id'])" | Select-Object -First 1)
    $calc = SaldoA $d $hoy
    if ([math]::Abs($calc - $esperado) -gt 0.01) { Fallo ("Documento {0}: saldo reconstruido {1} <> Balance {2}." -f $d['id'], $calc, $esperado) }
}
Write-Host "  Corte = hoy: los $($demo.Count) documentos coinciden con docDocument.Balance."

# (b) corte anterior: calculo independiente en SQL
$ids = ($demo | ForEach-Object { $_['id'] }) -join ','
$tabla = Sql "SELECT d.DocumentID, d.Total - ISNULL((SELECT SUM(p.Amount) FROM docDocumentPayment p WHERE (p.DocumentID=d.DocumentID OR p.PaymentWithDocumentID=d.DocumentID) AND p.DeletedOn IS NULL AND CONVERT(date,p.DateOperation) <= '$corteAnt'),0) FROM docDocument d WHERE d.DocumentID IN ($ids) AND CONVERT(date,d.DateDocument) <= '$corteAnt'"
$n = 0
foreach ($linea in $tabla) {
    $c = $linea -split '\|'; if ($c.Count -lt 2 -or $c[0] -notmatch '^\d+$') { continue }
    $d = $demo | Where-Object { [long]$_['id'] -eq [long]$c[0] }
    $calc = SaldoA $d $corteAnt
    if ([math]::Abs($calc - [double]$c[1]) -gt 0.01) { Fallo ("Corte {0}, documento {1}: reconstruido {2} <> SQL {3}." -f $corteAnt, $c[0], $calc, $c[1]) }
    $n++
}
if ($n -lt 8) { Fallo "El calculo independiente solo cubrio $n documentos." }
# El abono del 50% de la factura de cliente de 40 dias fue hace 20 dias: a un corte de 25 dias atras debia deber el Total completo
$f2 = $demo | Where-Object { $_['titulo'] -like '*40 dias, abono*' -or $_['titulo'] -like '*40 d?as, abono*' -or $_['titulo'] -match '40 d.as, abono' } | Select-Object -First 1
if ($f2) { $s = SaldoA $f2 $corteAnt; if ([math]::Abs($s - [double]$f2['total']) -gt 0.01) { Fallo "El corte anterior al abono debia mostrar el Total completo." } }
Write-Host "  Corte = $corteAnt : $n documentos coinciden con el calculo independiente en SQL."

# 4) Clasificacion: facturas de cliente = por cobrar (C), de compra = por pagar (P), notas de credito restan
$mal = @($demo | Where-Object { ($_['tipo'] -like 'Facturas Cliente*' -and $_['lado'] -ne 'C') -or ($_['tipo'] -like 'Facturas Compra*' -and $_['lado'] -ne 'P') })
if ($mal.Count -gt 0) { Fallo "Hay $($mal.Count) documentos mal clasificados en por cobrar / por pagar." }
$nc = @($docs | Where-Object { $_['tipo'] -like 'Notas de Cr*' })
foreach ($d in $nc) { if ([int]$d['s'] -ge 0) { Fallo "La nota de credito $($d['id']) debia restar (s = -1) y trae s = $($d['s'])." } }
Write-Host ("  Clasificacion correcta ({0} nota(s) de credito restan)." -f $nc.Count)
exit 0
