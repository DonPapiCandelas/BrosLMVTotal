# Caso de humo #44: una venta que ya se facturo NO se cuenta dos veces en «Estado de cuenta de clientes».
# Regla (la misma que usa Comercial en su columna «Facturado», vwLBSDocCustomerSalesTotalInvoiced): al total de un documento que no es factura ni nota de credito se le resta
# lo facturado a partir de el (facturas vigentes cuyo SourceDocumentID es ese documento); la factura lleva la deuda.
# Siembra en BROSLMV_DESARROLLO (clonando una factura de la siembra del laboratorio, con titulo «DEMO VENTA FACT…») tres ventas (modulo 158) y dos facturas:
#   V1 1,000 facturada por completo (F1 1,000)  -> la venta NO debe aparecer; la factura sí, con 1,000.
#   V2 2,000 facturada en parte (F2 500)        -> la venta debe contar 1,500; la factura, 500.
#   V3   800 sin facturar                       -> cuenta 800.
# Despues corre la plantilla real con el Runner (sin ventanas) y revisa el modelo. Al terminar da de baja (DeletedOn) lo sembrado. Solo corre contra BROSLMV_DESARROLLO.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe" -ForegroundColor Red; exit 1 }
function Sql([string]$q) { sqlcmd -S $Server -E -d $Database -x -b -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 }
function Baja { Sql "UPDATE docDocument SET DeletedOn = GETDATE() WHERE Title LIKE 'DEMO VENTA FACT%' AND DeletedOn IS NULL" | Out-Null }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; Baja; sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='HUMO_VENTAFACT'" | Out-Null; exit 1 }

$modelo = (Sql "SELECT TOP 1 DocumentID FROM docDocument WHERE ModuleID=21 AND DeletedOn IS NULL AND Title LIKE 'DEMO CREAR DOC - factura de cliente%' ORDER BY DocumentID DESC" | Select-Object -First 1)
if (-not $modelo -or $modelo -notmatch '^\d+$') { Fallo "No hay una factura de la siembra del laboratorio para clonar (corre build\laboratorio\sembrar_demo_*.ps1 / el humo #39)." }
Baja

# Clona la factura modelo cambiando solo lo necesario (docDocument solo tiene la llave primaria como indice unico)
function Clonar([int]$modulo, [int]$tipo, [string]$titulo, [decimal]$total, [long]$origen, [int]$diasAtras) {
    $q = @"
DECLARE @ov TABLE (n SYSNAME, e NVARCHAR(200));
INSERT @ov VALUES ('ModuleID','$modulo'),('DocumentTypeID','$tipo'),('Title','N''$titulo'''),('Total','$total'),('Balance','$total'),('TotalPaid','0'),('StatusPaidID','3'),
  ('SourceDocumentID','$origen'),('DateDocument','DATEADD(day,-$diasAtras,CAST(GETDATE() AS date))'),('CancelledOn','NULL'),('DeletedOn','NULL');
DECLARE @sel NVARCHAR(MAX), @ins NVARCHAR(MAX);
SELECT @ins = STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id),
       @sel = STRING_AGG(ISNULL(o.e, QUOTENAME(c.name)), ',') WITHIN GROUP (ORDER BY c.column_id)
FROM sys.columns c LEFT JOIN @ov o ON o.n = c.name
WHERE c.object_id = OBJECT_ID('docDocument') AND c.is_identity = 0 AND c.is_computed = 0 AND TYPE_NAME(c.user_type_id) <> 'timestamp';
DECLARE @sql NVARCHAR(MAX) = N'INSERT docDocument (' + @ins + N') SELECT ' + @sel + N' FROM docDocument WHERE DocumentID = $modelo; SELECT SCOPE_IDENTITY();';
EXEC sp_executesql @sql;
"@
    $r = Sql $q | Where-Object { $_ -match '^\d+(\.\d+)?$' } | Select-Object -First 1
    if (-not $r) { Fallo "No se pudo sembrar '$titulo'." }
    return [long][double]$r
}
$v1 = Clonar 158 2 'DEMO VENTA FACT - V1 venta facturada completa' 1000 0 40
$f1 = Clonar 21 5 'DEMO VENTA FACT - F1 factura de V1' 1000 $v1 38
$v2 = Clonar 158 2 'DEMO VENTA FACT - V2 venta facturada en parte' 2000 0 40
$f2 = Clonar 21 5 'DEMO VENTA FACT - F2 factura parcial de V2' 500 $v2 38
$v3 = Clonar 158 2 'DEMO VENTA FACT - V3 venta sin facturar' 800 0 40
Write-Host "  Sembrado: ventas $v1 / $v2 / $v3 y facturas $f1 / $f2 (clon de $modelo)."

# Correr la plantilla real sin ventanas y leer el modelo
$codigo = (Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\ESTADO_CUENTA_CLIENTES.ctx") -Raw -Encoding UTF8) -replace "'", "''"
$tmp = Join-Path $env:TEMP ("vf_" + [Guid]::NewGuid().ToString('N') + ".sql")
"IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey='HUMO_VENTAFACT') UPDATE zzBrosScript SET Codigo=N'$codigo', Activo=1, Modificado=GETDATE() WHERE AppKey='HUMO_VENTAFACT' ELSE INSERT INTO zzBrosScript (AppKey,Nombre,Codigo,Activo,Modificado) VALUES ('HUMO_VENTAFACT','Humo - venta facturada',N'$codigo',1,GETDATE());" | Out-File $tmp -Encoding utf8
sqlcmd -S $Server -E -d $Database -x -b -i $tmp 2>&1 | Out-Null; Remove-Item $tmp -Force -ErrorAction SilentlyContinue
$salida = Join-Path $env:TEMP ("vf_" + [Guid]::NewGuid().ToString('N') + ".json")
$env:BROSLMV_SALDOS_TEST = '{"meses":12}'; $env:BROSLMV_SALDOS_OUT = $salida
$log = & $RunnerExe --appkey HUMO_VENTAFACT --bd $Database 2>&1; $code = $LASTEXITCODE
Remove-Item Env:\BROSLMV_SALDOS_TEST, Env:\BROSLMV_SALDOS_OUT -ErrorAction SilentlyContinue
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='HUMO_VENTAFACT'" | Out-Null
if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner fallo (exit $code)." }
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $ser.MaxJsonLength = [int]::MaxValue
$m = $ser.DeserializeObject((Get-Content $salida -Raw -Encoding UTF8)); Remove-Item $salida -Force
Baja
$docs = @{}; foreach ($d in @($m['docs'])) { $docs[[long]$d['id']] = $d }
# el saldo de cada documento sembrado a hoy: Total efectivo (Total - Facturado) menos pagos (no hay)
function Efectivo($id) { if ($docs.ContainsKey($id)) { [double]$docs[$id]['total'] } else { 0.0 } }
$esperado = @{ $v1 = 0.0; $f1 = 1000.0; $v2 = 1500.0; $f2 = 500.0; $v3 = 800.0 }
foreach ($id in $esperado.Keys) {
    $obt = Efectivo $id
    if ([math]::Abs($obt - $esperado[$id]) -gt 0.01) { Fallo ("Documento {0}: se esperaba un total efectivo de {1} y salio {2}." -f $id, $esperado[$id], $obt) }
}
if ($docs[$v1]['fact'] -ne 1000 -or $docs[$v2]['fact'] -ne 500) { Fallo "El modelo debia traer lo facturado (fact) de cada venta." }
if ([double]$docs[$f1]['fact'] -ne 0) { Fallo "Una factura nunca se descuenta por esta regla." }
Write-Host "  V1 facturada completa: total efectivo 0 (no genera saldo); V2 facturada en parte: 1,500; V3 sin facturar: 800; las facturas llevan 1,000 y 500."
exit 0
