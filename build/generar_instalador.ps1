# BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
# Copyright (C) 2026 Cristofer Candelas Garcia
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

# generar_instalador.ps1 — Compila y actualiza el paquete de instalación.
# Resultado: la carpeta instalador\ queda lista para distribuir.
# Requiere .NET SDK.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent          # C:\MLVTotal
$src  = Join-Path $root "src"
$hostProj = Join-Path $root "host\BrosLMV.Host"
$out  = Join-Path $root "build\out"
$bin  = Join-Path $root "instalador\bin"
$hostOut = Join-Path $root "instalador\host"

Write-Host "==================================================="
Write-Host " Generar instalador BrosLMV"
Write-Host "==================================================="

# Si compilas en el mismo equipo donde corre CONTPAQi, cierra ComercialSP para
# que no bloquee la DLL (en un equipo de desarrollo puro, esto no hace nada).
Get-Process ComercialSP -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "1) Compilando addon..." -ForegroundColor Cyan
dotnet build (Join-Path $src "BrosLMV.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION" -ForegroundColor Red; exit 1 }

Write-Host "2) Actualizando instalador\bin..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Join-Path $bin "x86") | Out-Null
Copy-Item (Join-Path $out "*.dll") $bin -Force
Copy-Item (Join-Path $out "x86\SQLite.Interop.dll") (Join-Path $bin "x86") -Force

Write-Host "3) Compilando host v3.0..." -ForegroundColor Cyan
if (Test-Path $hostOut) { Remove-Item $hostOut -Recurse -Force }
dotnet publish (Join-Path $hostProj "BrosLMV.Host.csproj") -c Release -r win-x64 --self-contained true -o $hostOut
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION DEL HOST" -ForegroundColor Red; exit 1 }

Write-Host "4) Copiando workers..." -ForegroundColor Cyan
$workersSrc = Join-Path $root "workers"
$workersDst = Join-Path $root "instalador\workers"
if (Test-Path $workersSrc) {
    if (Test-Path $workersDst) { Remove-Item $workersDst -Recurse -Force }
    Copy-Item $workersSrc $workersDst -Recurse -Force
}

Write-Host "5) Limpiando temporales..." -ForegroundColor Cyan
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $src "obj") -Recurse -Force -ErrorAction SilentlyContinue

$n = (Get-ChildItem (Join-Path $bin "*.dll")).Count
Write-Host ""
Write-Host "LISTO. instalador\ actualizado ($n DLLs + x86\SQLite.Interop.dll + host)." -ForegroundColor Green
Write-Host "Distribuye la carpeta 'instalador' y corre Instalar.ps1 en cada equipo."
