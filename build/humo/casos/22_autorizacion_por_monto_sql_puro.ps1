# Caso de humo #22: valida PLANTILLA_AUTORIZACION_POR_MONTO_SQL_PURO.sql contra el sandbox.
# A diferencia de los casos 14/17 (comparan campo por campo contra un documento nativo), esta
# plantilla no tiene equivalente nativo -- Comercial Pro no ofrece límite de importe por
# firmante de fábrica. En vez de comparar, se prueban los 4 caminos de negocio reales:
#   1) Usuario SIN fila en BrosAutorizaciones -> AUTORIZAR debe rechazar.
#   2) Usuario CON fila pero ImporteMax menor al total del documento -> AUTORIZAR debe rechazar.
#   3) Usuario CON fila y ImporteMax suficiente -> AUTORIZAR debe autorizar (AuthorizedOn/By).
#   4) REVOCAR debe limpiar la autorización, y llamarlo 2 veces seguidas (o AUTORIZAR 2 veces
#      seguidas) NUNCA debe alternar el estado -- es la corrección central sobre el SP
#      original (ZLCAUTODG/ZLCAUTOOP), que hacía TOGGLE con la misma llamada.
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

$usuarioPrueba = 987654321

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

function Exec-Q([string]$q) {
    return (sqlcmd -S $Server -E -d $Database -h -1 -Q $q -W 2>&1 | Select-Object -First 1).Trim()
}

# v2.77.0: @documentoID/@usuarioID/@nivel ahora son tokens {DATOS:Tabla.Columna:*} (formulario
# automatico) -- el Runner headless no los resuelve, se sustituyen a mano. @accion sigue siendo
# un literal hardcodeado en la plantilla (NO se migro, ver comentario en el .sql), asi que su
# reemplazo sigue funcionando igual que antes.
function Build-CodigoAutorizacion([int]$docId, [int]$usuarioId, [int]$nivel, [string]$accion) {
    $plantilla = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_AUTORIZACION_POR_MONTO_SQL_PURO.sql") -Raw
    $plantilla = $plantilla -replace '\{DATOS:docDocument\.DocumentID[^}]*\}', "$docId"
    $plantilla = $plantilla -replace '\{DATOS:engUser\.UserID[^}]*\}', "$usuarioId"
    $plantilla = $plantilla -replace '\{DATOS:docDocument\.ModuleID[^}]*\}', "$nivel"
    $plantilla = $plantilla -replace [regex]::Escape("DECLARE @accion      NVARCHAR(10) = 'AUTORIZAR';"), "DECLARE @accion      NVARCHAR(10) = '$accion';"
    return "-- job: safe-offline`n" + $plantilla
}

# 0) Limpieza previa por si un corrido anterior dejó basura del usuario de prueba.
sqlcmd -S $Server -E -d $Database -Q "IF OBJECT_ID('dbo.BrosAutorizaciones') IS NOT NULL DELETE FROM BrosAutorizaciones WHERE UserID=$usuarioPrueba" -W | Out-Null

# 1) Documento base: una Orden de Compra real vía la plantilla SQL puro existente (total=$185.60
#    con los valores default: cantidad=2, precio=80, IVA 16%).
# v2.77.0: los parametros de la plantilla ahora son tokens {DATOS:Tabla.Columna:*} (formulario
# automatico) -- el Runner headless no los resuelve, se sustituyen a mano por los MISMOS
# valores default que antes traia el archivo.
$codigoOC = Get-Content (Join-Path $PSScriptRoot "..\..\..\instalador\scripts\PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql") -Raw
$codigoOC = $codigoOC -replace '\{DATOS:orgBusinessEntity\.BusinessEntityID[^}]*\}', '2'
$codigoOC = $codigoOC -replace '\{DATOS:orgDepot\.DepotID[^}]*\}', '1'
$codigoOC = $codigoOC -replace '\{DATOS:orgProduct\.ProductID[^}]*\}', '1'
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.Quantity[^}]*\}', '2'
$codigoOC = $codigoOC -replace '\{DATOS:docDocumentItem\.UnitPrice[^}]*\}', '80'
$codigoOC = $codigoOC -replace '\{DATOS:docDocument\.PaymentTermID[^}]*\}', '4'
$codigoOC = "-- job: safe-offline`n" + $codigoOC
if (-not (Upsert-Boton "HUMO_AUTH_OC_BASE" "Humo 20 - OC base" $codigoOC)) { exit 1 }
$salidaOC = & $RunnerExe --appkey HUMO_AUTH_OC_BASE --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] No se pudo crear el documento base:" -ForegroundColor Red; $salidaOC | ForEach-Object { Write-Host "    $_" }; exit 1 }
$docId = Exec-Q "SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID=183"
if ([string]::IsNullOrWhiteSpace($docId)) { Write-Host "  [ERROR] No se encontro el documento base recien creado." -ForegroundColor Red; exit 1 }
$total = Exec-Q "SELECT Total*ISNULL(Rate,1) FROM docDocument WHERE DocumentID=$docId"
Write-Host "  Documento base: DocumentID=$docId, Total=$total"

# 2) AUTORIZAR sin fila en BrosAutorizaciones -> debe rechazar.
if (-not (Upsert-Boton "HUMO_AUTH_SIN_CONFIG" "Humo 20 - sin config" (Build-CodigoAutorizacion $docId $usuarioPrueba 1 "AUTORIZAR"))) { exit 1 }
$r1 = (& $RunnerExe --appkey HUMO_AUTH_SIN_CONFIG --bd $Database 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en el caso 'sin config':" -ForegroundColor Red; $r1 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r1 -notmatch "no tiene autorizaci") { Write-Host "  [ERROR] Caso 'sin config' no devolvio el mensaje esperado. Salida: $r1" -ForegroundColor Red; exit 1 }
$auth1 = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedOn),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($auth1 -ne "NULL") { Write-Host "  [ERROR] El documento quedo autorizado sin deberlo (caso 'sin config')." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Sin config en BrosAutorizaciones -> rechazado."

# 3) Da de alta al usuario de prueba con un limite BAJO ($50, el doc vale $185.60) -> AUTORIZAR debe rechazar por monto.
sqlcmd -S $Server -E -d $Database -Q "INSERT INTO BrosAutorizaciones (UserID, Nivel, Rol, ImporteMax) VALUES ($usuarioPrueba, 1, 'HUMO_20', 50)" -W | Out-Null
if (-not (Upsert-Boton "HUMO_AUTH_EXCEDE" "Humo 20 - excede limite" (Build-CodigoAutorizacion $docId $usuarioPrueba 1 "AUTORIZAR"))) { exit 1 }
$r2 = (& $RunnerExe --appkey HUMO_AUTH_EXCEDE --bd $Database 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en el caso 'excede limite':" -ForegroundColor Red; $r2 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r2 -notmatch "excede el l") { Write-Host "  [ERROR] Caso 'excede limite' no devolvio el mensaje esperado. Salida: $r2" -ForegroundColor Red; exit 1 }
$auth2 = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedOn),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($auth2 -ne "NULL") { Write-Host "  [ERROR] El documento quedo autorizado excediendo su limite." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Limite insuficiente (`$50 vs `$185.60) -> rechazado."

# 4) Sube el limite a $99999 (suficiente) -> AUTORIZAR debe autorizar de verdad.
sqlcmd -S $Server -E -d $Database -Q "UPDATE BrosAutorizaciones SET ImporteMax=99999 WHERE UserID=$usuarioPrueba AND Nivel=1" -W | Out-Null
if (-not (Upsert-Boton "HUMO_AUTH_OK" "Humo 20 - autoriza OK" (Build-CodigoAutorizacion $docId $usuarioPrueba 1 "AUTORIZAR"))) { exit 1 }
$r3 = (& $RunnerExe --appkey HUMO_AUTH_OK --bd $Database 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en el caso 'autoriza OK':" -ForegroundColor Red; $r3 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r3 -notmatch "AUTORIZADO") { Write-Host "  [ERROR] Caso 'autoriza OK' no devolvio el mensaje esperado. Salida: $r3" -ForegroundColor Red; exit 1 }
$authBy3 = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedBy),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($authBy3 -ne "$usuarioPrueba") { Write-Host "  [ERROR] AuthorizedBy no quedo en $usuarioPrueba (quedo '$authBy3')." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] Limite suficiente -> AUTORIZADO, AuthorizedBy=$usuarioPrueba."

# 4b) Volver a llamar AUTORIZAR (2 veces seguidas) NO debe des-autorizar (defecto del original: toggle).
$r3b = & $RunnerExe --appkey HUMO_AUTH_OK --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en la 2a llamada a AUTORIZAR:" -ForegroundColor Red; $r3b | ForEach-Object { Write-Host "    $_" }; exit 1 }
$authOn3b = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedOn),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($authOn3b -eq "NULL") { Write-Host "  [ERROR] Llamar AUTORIZAR 2 veces seguidas hizo TOGGLE (des-autorizo) -- ese es el bug que esta plantilla debia corregir." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] AUTORIZAR llamado 2 veces seguidas -> sigue autorizado (no hizo toggle)."

# 5) REVOCAR debe limpiar la autorizacion, sin importar el limite de importe (revocar siempre procede).
if (-not (Upsert-Boton "HUMO_AUTH_REVOCA" "Humo 20 - revoca" (Build-CodigoAutorizacion $docId $usuarioPrueba 1 "REVOCAR"))) { exit 1 }
$r4 = (& $RunnerExe --appkey HUMO_AUTH_REVOCA --bd $Database 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en el caso 'revoca':" -ForegroundColor Red; $r4 | ForEach-Object { Write-Host "    $_" }; exit 1 }
if ($r4 -notmatch "REVOCADO") { Write-Host "  [ERROR] Caso 'revoca' no devolvio el mensaje esperado. Salida: $r4" -ForegroundColor Red; exit 1 }
$auth5 = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedOn),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($auth5 -ne "NULL") { Write-Host "  [ERROR] El documento NO quedo revocado." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] REVOCAR -> AuthorizedOn/By limpios."

# 5b) REVOCAR 2 veces seguidas tampoco debe hacer toggle (debe seguir revocado, no volver a autorizar).
$r4b = & $RunnerExe --appkey HUMO_AUTH_REVOCA --bd $Database 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "  [ERROR] El Runner fallo (exit code) en la 2a llamada a REVOCAR:" -ForegroundColor Red; $r4b | ForEach-Object { Write-Host "    $_" }; exit 1 }
$auth5b = Exec-Q "SELECT ISNULL(CONVERT(VARCHAR,AuthorizedOn),'NULL') FROM docDocument WHERE DocumentID=$docId"
if ($auth5b -ne "NULL") { Write-Host "  [ERROR] Llamar REVOCAR 2 veces seguidas volvio a autorizar (toggle) -- bug." -ForegroundColor Red; exit 1 }
Write-Host "  [OK] REVOCAR llamado 2 veces seguidas -> sigue revocado (no hizo toggle)."

# 6) Limpieza: los datos de BrosAutorizaciones del usuario de prueba NO deben quedar
#    permanentes (es basura de prueba, no configuracion real de ningun cliente) -- la TABLA
#    si se queda (es la infraestructura de la plantilla, igual que zzBrosScript queda con los
#    botones HUMO_* de otros casos).
sqlcmd -S $Server -E -d $Database -Q "DELETE FROM BrosAutorizaciones WHERE UserID=$usuarioPrueba" -W | Out-Null

exit 0
