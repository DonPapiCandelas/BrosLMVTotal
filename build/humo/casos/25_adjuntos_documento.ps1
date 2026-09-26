# Caso de humo T4.1 #25: Adjuntos del Documento -- valida headless (BrosLMV.Runner, sin abrir
# Comercial ni la ventana WinForms) la parte de DATOS de PLANTILLA_ADJUNTOS_DOCUMENTO_FORMS_CSHARP.ctx:
# insertar en engModuleFile y el control de propiedad al eliminar (dueño / no-dueño / admin).
#
# LIMITE HONESTO: la plantilla real es una ventana modal (OpenFileDialog, DataGridView, clics
# de botones) -- no automatizable headless con las herramientas de este proyecto, igual que el
# resto del catalogo de plantillas Forms C# (ninguna tiene smoke test de UI end-to-end). Este
# caso prueba la logica de datos subyacente (INSERT + regla de permiso) invocando un script
# companion (25_adjuntos_documento.codigo.cs) que reproduce EXACTAMENTE la regla del boton
# Eliminar real, apoyandose en --userid del Runner para simular usuarios distintos. La copia
# fisica del archivo, el selector de archivos y los botones solo se validaron por inspeccion
# de codigo (ver docs\CHANGELOG.md [2.72.0]).
#
# Escenario:
#   1) Inserta (via sqlcmd, no via ctx) una fila de prueba en engModuleFile, CreatedBy=DUENO.
#   2) Corre el Runner como OTRO usuario (ni dueno ni admin) -> debe QUEDAR la fila (denegado).
#   3) Corre el Runner como el DUENO -> debe DESAPARECER la fila (permitido, es el dueno).
#   4) Vuelve a insertar la fila (CreatedBy=DUENO).
#   5) Marca a un tercer usuario como AdjuntosAdmin (zzBrosPref) y corre el Runner con el ->
#      debe DESAPARECER la fila (permitido, es administrador aunque no sea el dueno).
#   6) Limpieza: fila de prueba y preferencia AdjuntosAdmin, pase lo que pase.
#
# Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$AppKey = "HUMO_ADJUNTOS_DELETE"
$codigoPath = Join-Path $PSScriptRoot "25_adjuntos_documento.codigo.cs"

$UsuarioDueno = 999901
$UsuarioOtro  = 999902
$UsuarioAdmin = 999903

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

function Exec-Sql([string]$query) {
    return (sqlcmd -S $Server -E -d $Database -h -1 -Q $query -W 2>&1)
}

function Limpiar {
    Exec-Sql "DELETE FROM engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'" | Out-Null
    Exec-Sql "DELETE FROM zzBrosPref WHERE Usuario=$UsuarioAdmin AND Tipo='AdjuntosAdmin'" | Out-Null
}

function LimpiarScriptCompanion {
    # El script companion HUMO_ADJUNTOS_DELETE solo existe para esta prueba -- no debe
    # quedar registrado en zzBrosScript entre corridas (quedaba visible/ejecutable como
    # boton suelto en Comercial, y su ctx.Msg mostraba un MessageBox real e inesperado).
    Exec-Sql "DELETE FROM zzBrosScript WHERE AppKey='$AppKey'" | Out-Null
}

# 0) Registrar/actualizar el script companion (idempotente).
# IMPORTANTE: -Encoding utf8 es obligatorio aqui -- sin especificarlo, Get-Content usa la
# codificacion por defecto del sistema (no UTF-8) y corrompe cualquier acento/enie del
# archivo .codigo.cs antes de guardarlo en zzBrosScript (bug real detectado: "dueño" se
# guardaba y ejecutaba como "dueÃ±o").
$codigo = Get-Content $codigoPath -Raw -Encoding utf8
$codigoEscapado = $codigo -replace "'", "''"
$sqlUpsert = @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$AppKey')
    UPDATE zzBrosScript SET Codigo = '$codigoEscapado', Activo = 1, Modificado = GETDATE() WHERE AppKey = '$AppKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Activo, Modificado)
    VALUES ('$AppKey', 'Humo T4.1 - Adjuntos: ownership al eliminar (engModuleFile)', '$codigoEscapado', 1, GETDATE());
"@
$tmpSql = Join-Path $env:TEMP ("humo_upsert_" + [Guid]::NewGuid().ToString('N') + ".sql")
$sqlUpsert | Out-File $tmpSql -Encoding utf8
$salidaUpsert = sqlcmd -S $Server -E -d $Database -i $tmpSql -W 2>&1
$exitUpsert = $LASTEXITCODE
Remove-Item $tmpSql -Force -ErrorAction SilentlyContinue
if ($exitUpsert -ne 0) {
    Write-Host "  [ERROR] sqlcmd fallo al registrar el script companion (exit $exitUpsert):" -ForegroundColor Red
    $salidaUpsert | ForEach-Object { Write-Host "    $_" }
    exit 1
}

# Documento real del sandbox para poblar ModuleID/ID (no importa cual, solo debe existir).
$docRow = Exec-Sql "SELECT TOP 1 DocumentID, ModuleID FROM docDocument WHERE DeletedOn IS NULL ORDER BY DocumentID DESC"
$partes = ($docRow | Select-Object -First 1).ToString().Trim() -split '\s+'
if ($partes.Count -lt 2) {
    Write-Host "  [ERROR] No se encontro ningun docDocument en el sandbox para usar como referencia." -ForegroundColor Red
    exit 1
}
$DocId = $partes[0]
$ModId = $partes[1]

Limpiar

try {
    # ── Paso 2: usuario ajeno (ni dueno ni admin) NO debe poder eliminar ──────────────────
    Exec-Sql "INSERT INTO engModuleFile (ModuleID, ID, ItemID, FileName, FilePath, Description, CreatedOn, CreatedBy) VALUES ($ModId, $DocId, 0, N'humo_test.txt', N'C:\Temp\', N'HUMO_ADJUNTOS_TEST', GETDATE(), $UsuarioDueno)" | Out-Null
    $nAntes = (Exec-Sql "SELECT COUNT(*) FROM engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'" | Select-Object -First 1).ToString().Trim()
    if ($nAntes -ne "1") { Write-Host "  [ERROR] No se pudo insertar la fila de prueba (dueno)." -ForegroundColor Red; exit 1 }

    $salida = & $RunnerExe --appkey $AppKey --bd $Database --userid $UsuarioOtro 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] Runner (usuario ajeno) devolvio exit $LASTEXITCODE :" -ForegroundColor Red
        $salida | ForEach-Object { Write-Host "    $_" }
        exit 1
    }
    $nDespues = (Exec-Sql "SELECT COUNT(*) FROM engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'" | Select-Object -First 1).ToString().Trim()
    if ($nDespues -ne "1") {
        Write-Host "  [ERROR] Un usuario ajeno (ID $UsuarioOtro, no dueno, no admin) SI pudo eliminar el adjunto -- control de propiedad roto." -ForegroundColor Red
        exit 1
    }

    # ── Paso 3: el DUENO SI debe poder eliminar su propio adjunto ────────────────────────
    $salida = & $RunnerExe --appkey $AppKey --bd $Database --userid $UsuarioDueno 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] Runner (dueno) devolvio exit $LASTEXITCODE :" -ForegroundColor Red
        $salida | ForEach-Object { Write-Host "    $_" }
        exit 1
    }
    $nDespues = (Exec-Sql "SELECT COUNT(*) FROM engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'" | Select-Object -First 1).ToString().Trim()
    if ($nDespues -ne "0") {
        Write-Host "  [ERROR] El dueno (ID $UsuarioDueno) NO pudo eliminar su propio adjunto." -ForegroundColor Red
        exit 1
    }

    # ── Paso 5: un ADMINISTRADOR (no dueno) SI debe poder eliminar ───────────────────────
    Exec-Sql "INSERT INTO engModuleFile (ModuleID, ID, ItemID, FileName, FilePath, Description, CreatedOn, CreatedBy) VALUES ($ModId, $DocId, 0, N'humo_test.txt', N'C:\Temp\', N'HUMO_ADJUNTOS_TEST', GETDATE(), $UsuarioDueno)" | Out-Null
    Exec-Sql "INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES ($UsuarioAdmin, 'AdjuntosAdmin', '1')" | Out-Null

    $salida = & $RunnerExe --appkey $AppKey --bd $Database --userid $UsuarioAdmin 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] Runner (administrador) devolvio exit $LASTEXITCODE :" -ForegroundColor Red
        $salida | ForEach-Object { Write-Host "    $_" }
        exit 1
    }
    $nDespues = (Exec-Sql "SELECT COUNT(*) FROM engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'" | Select-Object -First 1).ToString().Trim()
    if ($nDespues -ne "0") {
        Write-Host "  [ERROR] El administrador (ID $UsuarioAdmin, no dueno) NO pudo eliminar el adjunto -- override de admin roto." -ForegroundColor Red
        exit 1
    }
}
finally {
    Limpiar
    LimpiarScriptCompanion
}

exit 0
