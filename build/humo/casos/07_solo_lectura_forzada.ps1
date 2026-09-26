# Caso de humo T4.1 #7 (T2.2): SoloLectura forzado por zzBrosPref debe bloquear escrituras
# y dejar pasar lecturas, tanto por SQL (ctx.EjecutarSql) como por ctx.erp (ctx.erp.Save).
# A diferencia de los demas casos, aqui "pasar" significa que la ESCRITURA fue rechazada --
# no que el Runner salga en 0 para el boton que escribe.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP",
    [string]$RunnerExe = (Join-Path $PSScriptRoot "..\..\..\runner\bin\Release\BrosLMV.Runner.exe")
)
$ErrorActionPreference = "Continue"
$UsuarioPrueba = 999999

if (-not (Test-Path $RunnerExe)) {
    Write-Host "  [ERROR] No existe $RunnerExe -- compila el Runner primero (dotnet build runner\BrosLMV.Runner.csproj -c Release)." -ForegroundColor Red
    exit 1
}

# 1) Marcar al usuario de prueba como SoloLectura forzado (zzBrosPref).
$qMarcar = "IF NOT EXISTS (SELECT 1 FROM zzBrosPref WHERE Usuario=$UsuarioPrueba AND Tipo='SoloLectura') INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES ($UsuarioPrueba, 'SoloLectura', '1')"
sqlcmd -S $Server -E -d $Database -Q $qMarcar -W 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [ERROR] No se pudo marcar al usuario de prueba como SoloLectura." -ForegroundColor Red
    exit 1
}

try {
    # 2) Lectura (caso 1, SQL) DEBE seguir funcionando con SoloLectura activo.
    $salidaLectura = & $RunnerExe --appkey HUMO_SQL_SMOKE --bd $Database --userid $UsuarioPrueba 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] Una lectura se bloqueo con SoloLectura activo (no deberia):" -ForegroundColor Red
        $salidaLectura | ForEach-Object { Write-Host "    $_" }
        exit 1
    }

    # 3) Escritura (caso 3, crear OC via ctx.erp) DEBE rechazarse con el mensaje esperado.
    $salidaEscritura = & $RunnerExe --appkey HUMO_CREAR_OC --bd $Database --userid $UsuarioPrueba 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [ERROR] Se creo una OC con SoloLectura activo -- el forzado NO esta bloqueando." -ForegroundColor Red
        exit 1
    }
    if (-not ($salidaEscritura -join "`n" -match "SOLO LECTURA")) {
        Write-Host "  [ERROR] La escritura fallo, pero no por el motivo esperado (SOLO LECTURA):" -ForegroundColor Red
        $salidaEscritura | ForEach-Object { Write-Host "    $_" }
        exit 1
    }
}
finally {
    # 4) Limpieza: SIEMPRE quitar la preferencia de prueba, incluso si algo arriba fallo.
    sqlcmd -S $Server -E -d $Database -Q "DELETE FROM zzBrosPref WHERE Usuario=$UsuarioPrueba AND Tipo='SoloLectura'" -W 2>&1 | Out-Null
}

exit 0
