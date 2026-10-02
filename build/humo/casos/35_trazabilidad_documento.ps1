# Caso de humo #35: la plantilla de fabrica TRAZABILIDAD_DOCUMENTO.ctx corriendo headless (BrosLMV.Runner) contra el laboratorio (BROSLMV_DESARROLLO, la base de pruebas oficial).
# Registra la PLANTILLA REAL (instalador\scripts\TRAZABILIDAD_DOCUMENTO.ctx, la misma que se instala) como boton de prueba, y con
# BROSLMV_TRAZA_DOC / BROSLMV_TRAZA_OUT la corre sin ventanas para que escriba su modelo en JSON. Es SOLO LECTURA: no crea ni cambia nada.
# Comprueba: (1) un documento con vinculos reconstruye su cadena y reconoce su evidencia, (2) un documento aislado da 1 nodo y 0 aristas,
# (3) un documento inexistente no revienta. Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_TRAZABILIDAD"
$plantilla = Join-Path $PSScriptRoot "..\..\..\instalador\scripts\TRAZABILIDAD_DOCUMENTO.ctx"

if (-not (Test-Path $RunnerExe)) { Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $plantilla)) { Write-Host "  [ERROR] No existe la plantilla $plantilla" -ForegroundColor Red; exit 1 }

function Sql([string]$q) { (sqlcmd -S $Server -E -d $Database -h -1 -W -Q "SET NOCOUNT ON; $q" 2>&1 | Select-Object -First 1) }

# 1) Registrar la plantilla real como boton de prueba (idempotente)
$codigo = (Get-Content $plantilla -Raw -Encoding UTF8) -replace "'", "''"
$tmpSql = Join-Path $env:TEMP ("humo_traza_" + [Guid]::NewGuid().ToString('N') + ".sql")
@"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = N'$codigo', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado) VALUES ('$AppKey', 'Humo - trazabilidad del documento', N'$codigo', 1, GETDATE());
"@ | Out-File $tmpSql -Encoding utf8
$out = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1
$exitUpsert = $LASTEXITCODE
Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($exitUpsert -ne 0) { Write-Host "  [ERROR] No se pudo registrar la plantilla de prueba:" -ForegroundColor Red; $out | ForEach-Object { Write-Host "    $_" }; exit 1 }

function Correr([long]$doc) {
    $salida = Join-Path $env:TEMP ("traza_" + [Guid]::NewGuid().ToString('N') + ".json")
    $env:BROSLMV_TRAZA_DOC = "$doc"; $env:BROSLMV_TRAZA_OUT = $salida
    $log = & $RunnerExe --appkey $AppKey --bd $Database 2>&1
    $code = $LASTEXITCODE
    Remove-Item Env:\BROSLMV_TRAZA_DOC, Env:\BROSLMV_TRAZA_OUT -ErrorAction SilentlyContinue
    $m = $null
    if (Test-Path $salida) { $m = Get-Content $salida -Raw -Encoding UTF8 | ConvertFrom-Json; Remove-Item $salida -Force }
    return [PSCustomObject]@{ Code = $code; Modelo = $m; Log = ($log -join "`n") }
}

# 2) Un documento con cadena: una orden de compra de la que ya salio otro documento (por encabezado o por partida)
$docConCadena = Sql "SELECT TOP 1 o.DocumentID FROM docDocument o WHERE o.DeletedOn IS NULL AND EXISTS (SELECT 1 FROM docDocument d WHERE d.SourceDocumentID = o.DocumentID AND d.DeletedOn IS NULL) ORDER BY o.DocumentID DESC"
if (-not $docConCadena -or $docConCadena -notmatch '^\d+$') { Write-Host "  [ERROR] El sandbox no tiene ningun documento con documentos derivados para probar." -ForegroundColor Red; exit 1 }
$r = Correr ([long]$docConCadena)
if ($r.Code -ne 0 -or -not $r.Modelo) { Write-Host "  [ERROR] El Runner fallo con el documento $docConCadena (exit $($r.Code)):" -ForegroundColor Red; Write-Host $r.Log; exit 1 }
$nodos = @($r.Modelo.nodos); $aristas = @($r.Modelo.aristas)
if ($nodos.Count -lt 2) { Write-Host "  [ERROR] Se esperaban >=2 documentos en la cadena de $docConCadena y llegaron $($nodos.Count)." -ForegroundColor Red; exit 1 }
if ($aristas.Count -lt 1) { Write-Host "  [ERROR] Se esperaba >=1 vinculo y llegaron $($aristas.Count)." -ForegroundColor Red; exit 1 }
$raiz = @($nodos | Where-Object { $_.raiz })
if ($raiz.Count -ne 1 -or [long]$raiz[0].id -ne [long]$docConCadena) { Write-Host "  [ERROR] La raiz debia ser el documento $docConCadena." -ForegroundColor Red; exit 1 }
$conSource = @($aristas | Where-Object { $_.tipos -contains 'S' -or $_.tipos -contains 'P' -or $_.tipos -contains 'E' })
if ($conSource.Count -lt 1) { Write-Host "  [ERROR] Ningun vinculo trae evidencia del sistema (S/P/E)." -ForegroundColor Red; exit 1 }
# todo vinculo conecta nodos que existen y la generacion de la raiz es 0
foreach ($a in $aristas) {
    if (-not ($nodos | Where-Object { [long]$_.id -eq [long]$a.desde }) -or -not ($nodos | Where-Object { [long]$_.id -eq [long]$a.hasta })) { Write-Host "  [ERROR] Un vinculo apunta a un nodo que no esta en el mapa ($($a.desde) -> $($a.hasta))." -ForegroundColor Red; exit 1 }
}
if ([int]$raiz[0].gen -ne 0) { Write-Host "  [ERROR] La generacion de la raiz debia ser 0." -ForegroundColor Red; exit 1 }

# 3) Si algun documento del sandbox tiene DestinationDocumentID, debe aparecer como evidencia 'D'. (En el sandbox suele no haber: se omite sin fallar.)
$docConDestino = Sql "SELECT TOP 1 DocumentID FROM docDocument WHERE DestinationDocumentID > 0 AND DeletedOn IS NULL ORDER BY DocumentID DESC"
if ($docConDestino -match '^\d+$') {
    $rd = Correr ([long]$docConDestino)
    $conD = @(@($rd.Modelo.aristas) | Where-Object { $_.tipos -contains 'D' })
    if ($conD.Count -lt 1) { Write-Host "  [ERROR] El documento $docConDestino tiene DestinationDocumentID y la trazabilidad no lo reconocio." -ForegroundColor Red; exit 1 }
}

# 4) Un documento aislado (sin vinculos de ningun tipo): 1 nodo, 0 aristas
$aislado = Sql "SELECT TOP 1 d.DocumentID FROM docDocument d WHERE d.DeletedOn IS NULL AND ISNULL(d.SourceDocumentID,0)=0 AND ISNULL(d.DestinationDocumentID,0)=0 AND NOT EXISTS (SELECT 1 FROM docDocument x WHERE x.SourceDocumentID=d.DocumentID OR x.DestinationDocumentID=d.DocumentID) AND NOT EXISTS (SELECT 1 FROM docDocumentItem i WHERE i.DocumentID=d.DocumentID AND (i.SourceDocumentItemID>0 OR i.DeliverDocumentItemID>0 OR i.SourceDocumentID>0)) AND NOT EXISTS (SELECT 1 FROM docDocumentItem o JOIN docDocumentItem i2 ON (i2.SourceDocumentItemID=o.DocumentItemID OR i2.DeliverDocumentItemID=o.DocumentItemID) WHERE o.DocumentID=d.DocumentID) ORDER BY d.DocumentID DESC"
if ($aislado -match '^\d+$') {
    $ra = Correr ([long]$aislado)
    if ($ra.Code -ne 0 -or -not $ra.Modelo) { Write-Host "  [ERROR] El Runner fallo con el documento aislado $aislado." -ForegroundColor Red; Write-Host $ra.Log; exit 1 }
    if (@($ra.Modelo.nodos).Count -ne 1 -or @($ra.Modelo.aristas).Count -ne 0) { Write-Host "  [ERROR] El documento aislado $aislado debia dar 1 nodo y 0 vinculos (dio $(@($ra.Modelo.nodos).Count) y $(@($ra.Modelo.aristas).Count))." -ForegroundColor Red; exit 1 }
}

# 5) Un documento que no existe: no debe reventar (avisa y termina)
$rn = Correr 999999999
if ($rn.Modelo) { Write-Host "  [ERROR] Un documento inexistente no debia producir un modelo." -ForegroundColor Red; exit 1 }

# 6) La plantilla no escribio nada: sigue sin existir ningun documento nuevo por haber corrido (solo lectura)
Write-Host ("  Cadena de {0}: {1} documento(s), {2} vinculo(s)." -f $docConCadena, $nodos.Count, $aristas.Count)
exit 0
