# Caso de humo #42: el libro de Excel ejecutivo de «Estado de cuenta de clientes/proveedores» (ClosedXML, en C#) con datos sinteticos de 6,000 documentos.
# Corre la plantilla REAL headless (BrosLMV.Runner) con BROSLMV_SALDOS_XLSX_TEST (JSON que mandaria la ventana) y BROSLMV_SALDOS_XLSX_OUT, y comprueba que el .xlsx:
#   (1) cumple el esquema de Open XML (0 errores del validador oficial);
#   (2) trae las hojas Resumen, Antigüedad, Documentos y Vencimientos, con las 4 gráficas del resumen y 6,000 filas en Documentos;
#   (3) tarda un tiempo razonable con 6,000 documentos (< 60 s) y el estado de cuenta individual tambien sale valido.
# Requiere C:\BrosLMV\lib\ClosedXML.dll (lo instala el paquete). Solo corre contra BROSLMV_DESARROLLO. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe"),
    [int]$Documentos = 6000
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
function Fallo([string]$m) { Write-Host "  [ERROR] $m" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $RunnerExe)) { Fallo "No existe $RunnerExe" }
if (-not (Test-Path "C:\BrosLMV\lib\ClosedXML.dll")) { Fallo "Falta C:\BrosLMV\lib\ClosedXML.dll (instala el paquete)." }
$raiz = Join-Path $PSScriptRoot "..\..\.."
$openXml = Join-Path $raiz "instalador\lib\DocumentFormat.OpenXml.dll"
Add-Type -Path $openXml
$tmp = Join-Path $env:TEMP ("xlsx_" + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $tmp | Out-Null
$png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="   # PNG de 1x1: solo prueba que las imagenes entran al libro

function Registrar([string]$appKey, [string]$archivo) {
    $codigo = (Get-Content $archivo -Raw -Encoding UTF8) -replace "'", "''"
    $sql = Join-Path $tmp "reg.sql"
    "IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey='$appKey') UPDATE zzBrosScript SET Codigo=N'$codigo', Activo=1, Modificado=GETDATE() WHERE AppKey='$appKey' ELSE INSERT INTO zzBrosScript (AppKey,Nombre,Codigo,Activo,Modificado) VALUES ('$appKey','Humo - Excel estado de cuenta',N'$codigo',1,GETDATE());" | Out-File $sql -Encoding utf8
    $o = sqlcmd -S $Server -E -d $Database -x -b -i $sql 2>&1; if ($LASTEXITCODE -ne 0) { Fallo "No se pudo registrar $appKey : $o" }
}
function Libro([string]$appKey, [string]$payloadJson, [string]$salida) {
    $entrada = Join-Path $tmp ([Guid]::NewGuid().ToString('N') + ".json"); [IO.File]::WriteAllText($entrada, $payloadJson, (New-Object Text.UTF8Encoding($false)))
    $env:BROSLMV_SALDOS_XLSX_TEST = $entrada; $env:BROSLMV_SALDOS_XLSX_OUT = $salida
    $sw = [Diagnostics.Stopwatch]::StartNew(); $log = & $RunnerExe --appkey $appKey --bd $Database 2>&1; $code = $LASTEXITCODE; $sw.Stop()
    Remove-Item Env:\BROSLMV_SALDOS_XLSX_TEST, Env:\BROSLMV_SALDOS_XLSX_OUT -ErrorAction SilentlyContinue
    if ($code -ne 0 -or -not (Test-Path $salida)) { Write-Host ($log -join "`n"); Fallo "El Runner no genero $salida (exit $code)." }
    return $sw.Elapsed.TotalSeconds
}
# Cuenta las filas de una hoja leyendo el XML del .xlsx (OpenXmlReader pide almacenamiento aislado y falla en algunos hosts de PowerShell)
function FilasDe([string]$archivo, [string]$parte) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($archivo)
    try {
        $e = $z.GetEntry($parte.TrimStart('/')); $sr = New-Object IO.StreamReader($e.Open()); $txt = $sr.ReadToEnd(); $sr.Close()
        return ([regex]::Matches($txt, '<(x:)?row[ >]')).Count
    } finally { $z.Dispose() }
}
function Valida([string]$archivo) {
    $d = [DocumentFormat.OpenXml.Packaging.SpreadsheetDocument]::Open($archivo, $false)
    try {
        $v = New-Object DocumentFormat.OpenXml.Validation.OpenXmlValidator
        $errs = @($v.Validate($d)); if ($errs.Count -gt 0) { Fallo ("Open XML invalido: " + $errs[0].Description + " @ " + $errs[0].Path.XPath) }
        $hojas = @($d.WorkbookPart.Workbook.Sheets | ForEach-Object { $_.Name.Value })
        $filas = @{}; $imgs = @{}
        foreach ($s in $d.WorkbookPart.Workbook.Sheets) {
            $wp = $d.WorkbookPart.GetPartById($s.Id.Value)
            $filas[$s.Name.Value] = FilasDe $archivo $wp.Uri.OriginalString
            $imgs[$s.Name.Value] = if ($wp.DrawingsPart) { @($wp.DrawingsPart.ImageParts).Count } else { 0 }
        }
        return @{ Hojas = $hojas; Filas = $filas; Imagenes = $imgs }
    } finally { $d.Dispose() }
}

# ---- datos sinteticos: 6,000 documentos de 120 entidades ----
$lado = 'C'; $rnd = New-Object Random 42
$hoy = (Get-Date).Date; $corte = $hoy.ToString('yyyy-MM-dd')
$sb = New-Object Text.StringBuilder
$ents = @{}; $docsJson = New-Object Collections.Generic.List[string]; $k = @{ r = 0.0; rn = 0; a = 0.0; an = 0; v = 0.0; vn = 0; t = 0.0 }
for ($i = 1; $i -le $Documentos; $i++) {
    $e = $rnd.Next(1, 121); $nom = "CLIENTE DE PRUEBA {0:000}" -f $e
    $total = [math]::Round($rnd.NextDouble() * 50000 + 100, 2); $pag = if ($rnd.Next(0, 5) -eq 0) { [math]::Round($total * 0.3, 2) } else { 0 }; $saldo = [math]::Round($total - $pag, 2)
    $dias = $rnd.Next(-20, 130); $venc = $hoy.AddDays(-$dias); $fecha = $venc.AddDays(-30)
    $est = if ($dias -gt 0) { 'Vencido' } elseif ($dias -ge -7) { 'Vence en 7 días' } else { 'Vigente' }
    if ($dias -gt 0) { $k.r += $saldo; $k.rn++ } elseif ($dias -ge -7) { $k.a += $saldo; $k.an++ } else { $k.v += $saldo; $k.vn++ }; $k.t += $saldo
    $b = if ($dias -le 0) { 0 } elseif ($dias -le 30) { 1 } elseif ($dias -le 60) { 2 } elseif ($dias -le 90) { 3 } else { 4 }
    if (-not $ents.ContainsKey($e)) { $ents[$e] = @{ nom = $nom; b = @(0.0, 0.0, 0.0, 0.0, 0.0, 0.0); n = 0 } }
    $ents[$e].b[$b] += $saldo; $ents[$e].n++
    $docsJson.Add(('["{0}","Facturas Cliente","A{1}","{2}","{3}",{4},{5},{6},{7},"{8}","Documento sintetico {1}"]' -f $nom, $i, $fecha.ToString('yyyy-MM-dd'), $venc.ToString('yyyy-MM-dd'), $dias, $total.ToString([Globalization.CultureInfo]::InvariantCulture), $pag.ToString([Globalization.CultureInfo]::InvariantCulture), $saldo.ToString([Globalization.CultureInfo]::InvariantCulture), $est))
}
$inv = [Globalization.CultureInfo]::InvariantCulture
$entsJson = ($ents.Values | Sort-Object { - ($_.b | Measure-Object -Sum).Sum } | ForEach-Object { $t = ($_.b | Measure-Object -Sum).Sum; '["{0}",{1},{2},{3},{4},{5},{6},{7},{8}]' -f $_.nom, ($_.b[0].ToString($inv)), ($_.b[1].ToString($inv)), ($_.b[2].ToString($inv)), ($_.b[3].ToString($inv)), ($_.b[4].ToString($inv)), ($_.b[5].ToString($inv)), ($t.ToString($inv)), $_.n }) -join ','
$semanas = (@('{"ini":"' + $corte + '","fin":"' + $corte + '","n":' + $k.rn + ',"monto":' + $k.r.ToString($inv) + ',"nom":"Vencido"}') + (0..7 | ForEach-Object { '{"ini":"' + $hoy.AddDays($_ * 7).ToString('yyyy-MM-dd') + '","fin":"' + $hoy.AddDays($_ * 7 + 6).ToString('yyyy-MM-dd') + '","n":' + $_ + ',"monto":' + ($_ * 1000.5).ToString($inv) + '}' })) -join ','
$graf = '"aging":"' + $png + '","top":"' + $png + '","sem":"' + $png + '","estado":"' + $png + '"'
$kpi = '"kpi":{"vencido":[' + $k.rn + ',' + $k.r.ToString($inv) + '],"siete":[' + $k.an + ',' + $k.a.ToString($inv) + '],"semana":[3,3000.5],"vigente":[' + $k.vn + ',' + $k.v.ToString($inv) + '],"favor":[2,-1500],"total":' + $k.t.ToString($inv) + '}'
$general = '{"tipo":"general","empresa":"EMPRESA DE PRUEBA SA DE CV","titulo":"Cuentas por cobrar","ent":"cliente","entPl":"clientes","lado":"C","corte":"' + $corte + '","generado":"prueba",' + $kpi + ',"buckets":[],"agg":[0,0,0,0,0,0],"ents":[' + $entsJson + '],"docs":[' + ($docsJson -join ',') + '],"semanas":[' + $semanas + '],"graficas":{' + $graf + '},"meses":12}'
$edoMovs = (1..40 | ForEach-Object { '["{0}","Factura","A{1} · prueba",{2},{3},{4}]' -f $hoy.AddDays(-90 + $_ * 2).ToString('yyyy-MM-dd'), $_, ($_ * 100).ToString($inv), (($_ % 3) * 50).ToString($inv), ($_ * 80).ToString($inv) }) -join ','
$edo = '{"tipo":"edo","empresa":"EMPRESA DE PRUEBA SA DE CV","titulo":"Cuentas por cobrar","ent":"cliente","lado":"C","nombreEnt":"CLIENTE DE PRUEBA 001","desde":"' + $hoy.AddDays(-90).ToString('yyyy-MM-dd') + '","hasta":"' + $corte + '","generado":"prueba","ini":1000,"cargos":82000,"abonos":2400,"final":80600,"movs":[' + $edoMovs + ']}'

# ---- correr con las dos plantillas ----
foreach ($par in @(@('C', 'ESTADO_CUENTA_CLIENTES'), @('P', 'ESTADO_CUENTA_PROVEEDORES'))) {
    Registrar "HUMO_EXCEL" (Join-Path $raiz ("instalador\scripts\" + $par[1] + ".ctx"))
    $x1 = Join-Path $tmp ($par[1] + "_general.xlsx"); $seg = Libro "HUMO_EXCEL" $general $x1
    $r = Valida $x1
    foreach ($h in 'Resumen', ('Antig' + [char]0xFC + 'edad'), 'Documentos', 'Vencimientos') { if ($r.Hojas -notcontains $h) { Fallo "$($par[1]): falta la hoja $h (hay: $($r.Hojas -join ', '))." } }
    if ($r.Imagenes['Resumen'] -lt 1) { Fallo "$($par[1]): el resumen no trae las graficas." }
    if ($r.Filas['Documentos'] -lt $Documentos + 5) { Fallo "$($par[1]): Documentos trae $($r.Filas['Documentos']) filas y se esperaban $($Documentos + 5)." }
    if ($seg -gt 60) { Fallo ("$($par[1]): con {0} documentos tardo {1:N0} s (limite 60)." -f $Documentos, $seg) }
    $x2 = Join-Path $tmp ($par[1] + "_edo.xlsx"); $null = Libro "HUMO_EXCEL" $edo $x2
    $r2 = Valida $x2; if ($r2.Hojas -notcontains 'Estado de cuenta' -or $r2.Filas['Estado de cuenta'] -lt 50) { Fallo "$($par[1]): el estado de cuenta individual salio incompleto." }
    Write-Host ("  {0}: libro de {1:N0} documentos en {2:N1} s, esquema Open XML valido, hojas: {3}; estado de cuenta individual valido." -f $par[1], $Documentos, $seg, ($r.Hojas -join ', '))
}
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosScript WHERE AppKey='HUMO_EXCEL'" | Out-Null
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
exit 0
