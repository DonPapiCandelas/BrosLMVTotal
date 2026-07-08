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

# compilar.ps1 — Compila el proyecto BrosLMV (requiere .NET SDK).
# Deja las DLLs en build\out. No toca el instalador.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent   # C:\MLVTotal
$src  = Join-Path $root "src"
$out  = Join-Path $root "build\out"

Write-Host "Compilando BrosLMV..." -ForegroundColor Cyan
dotnet build (Join-Path $src "BrosLMV.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION" -ForegroundColor Red; exit 1 }

Write-Host "Listo. Salida en: $out" -ForegroundColor Green
Write-Host "(Para actualizar el instalador usa generar_instalador.ps1)"
