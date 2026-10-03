# Caso de humo #45: las plantillas Python ("Crear documento" y "Cobro/Pago", ventana HTML y Windows Forms) SIN Comercial: el nucleo contra el laboratorio (catalogos, pendientes, secuencia de creacion,
# receta de siete tablas del cobro) y el servidor HTTP "en vivo" de la ventana HTML de punta a punta (token, latido, consultas con respuesta en JavaScript, inteligencia de la persona, borrador,
# tema, crear con y sin cerrar la ventana, rechazos y cierre con window.close()). Se simula el modulo `broslmv`: lo que escribiria se registra, no se ejecuta; los datos se leen de BROSLMV_DESARROLLO.
# Devuelve 0 si paso, 1 si fallo (o 0 con aviso si no hay Python en este equipo).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO"
)
$ErrorActionPreference = "Continue"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "  Esta prueba solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
if (-not (Get-Command python -ErrorAction SilentlyContinue)) { Write-Host "  AVISO: no hay Python en este equipo; se omite." -ForegroundColor Yellow; exit 0 }
$dir = Join-Path $PSScriptRoot "..\..\plantillas_documentos"
foreach ($p in "prueba_python.py", "prueba_python_pagos.py", "prueba_python_servidor.py") {
    $salida = & python (Join-Path $dir $p) 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host ($salida -join "`n"); Write-Host "  [ERROR] Fallo $p" -ForegroundColor Red; exit 1 }
    $salida | Select-Object -Last 2 | ForEach-Object { Write-Host "  $_" }
}
Write-Host "  Plantillas Python: nucleo y servidor en vivo correctos."
exit 0
