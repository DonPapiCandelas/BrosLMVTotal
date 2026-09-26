# probar_humo.ps1 -- T4.1: bateria de humo automatizada contra la empresa sandbox.
# Corre cada caso en build\humo\casos\*.ps1, cada uno se vale por si mismo (exit 0 = paso,
# exit != 0 = fallo) e imprime su propio detalle en caso de error. Este script solo agrega
# el resumen verde/rojo y decide el exit code final.
#
# Regla del proyecto (docs\PLAN_IMPLEMENTACION.md, T4.1): no se corre generar_exes.ps1 sin
# humo en verde.
#
# Uso:
#   .\build\probar_humo.ps1
#   .\build\probar_humo.ps1 -Server "localhost\compac" -Database "ComercialSP"
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP"
)
$ErrorActionPreference = "Stop"
$casos = Get-ChildItem (Join-Path $PSScriptRoot "humo\casos") -Filter "*.ps1" | Sort-Object Name

Write-Host "=== Humo T4.1 -- sandbox: $Database en $Server ==="
$resultados = @()
foreach ($caso in $casos) {
    Write-Host ""
    Write-Host "-> $($caso.BaseName)"
    & $caso.FullName -Server $Server -Database $Database
    $ok = ($LASTEXITCODE -eq 0)
    $resultados += [PSCustomObject]@{ Caso = $caso.BaseName; Ok = $ok }
    if ($ok) { Write-Host "   OK" -ForegroundColor Green }
    else     { Write-Host "   FALLO" -ForegroundColor Red }
}

Write-Host ""
Write-Host "=== Resumen ==="
# @(...) fuerza arreglo incluso con 1 resultado -- sin esto, Where-Object devuelve un
# objeto suelto cuando hay exactamente un fallido, .Count sale $null, y "$null -gt 0" es
# $false: el resumen dice "todo verde" con un caso en rojo arriba mismo. Gotcha real,
# encontrado probando el harness a proposito con un caso fallido.
$fallidos = @($resultados | Where-Object { -not $_.Ok })
foreach ($r in $resultados) {
    $marca = if ($r.Ok) { "[VERDE]" } else { "[ROJO] " }
    $color = if ($r.Ok) { "Green" } else { "Red" }
    Write-Host "$marca $($r.Caso)" -ForegroundColor $color
}

if ($fallidos.Count -gt 0) {
    Write-Host ""
    Write-Host "$($fallidos.Count) de $($resultados.Count) caso(s) fallaron -- NO corras generar_exes.ps1." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "Todos los casos en verde ($($resultados.Count))." -ForegroundColor Green
exit 0
