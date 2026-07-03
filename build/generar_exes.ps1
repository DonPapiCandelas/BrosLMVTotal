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

# generar_exes.ps1 — Compila los ejecutables autocontenidos a dist\:
#   BrosLMV-Instalador.exe   : instala el runtime (DLLs+COM+icono) y abre el GUI de provisión.
#   BrosLMV-Desinstalador.exe: quita BrosLMV de empresas (SQL) y/o de este equipo.
# Requiere .NET SDK. Asume instalador\bin con las DLLs (corre generar_instalador.ps1
# antes si cambiaste el código del addon).

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent          # C:\MLVTotal
$inst = Join-Path $root "instalador"
$pInst = Join-Path $root "instaladores\Empresas"
$pDes  = Join-Path $root "instaladores\Desinstalador"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

function Assets($proj) {
    New-Item -ItemType Directory -Force (Join-Path $proj "assets") | Out-Null
    Copy-Item (Join-Path $inst "assets\logo_blanco.png") (Join-Path $proj "assets") -Force
    Copy-Item (Join-Path $inst "assets\logo_mono.png")   (Join-Path $proj "assets") -Force
    Copy-Item (Join-Path $inst "assets\app_icon.png")    (Join-Path $proj "assets") -Force
    Copy-Item (Join-Path $inst "assets\BrosLMV.ico")     (Join-Path $proj "app.ico") -Force
}

Write-Host "1) Assets..." -ForegroundColor Cyan
Assets $pInst
Assets $pDes
Copy-Item (Join-Path $inst "sql\provision_empresa.sql")    (Join-Path $pInst "assets") -Force
Copy-Item (Join-Path $inst "sql\desprovision_empresa.sql") (Join-Path $pDes  "assets") -Force

Write-Host "2) payload.zip (runtime embebido en el instalador)..." -ForegroundColor Cyan
$pl = Join-Path $env:TEMP "bros_payload"
if (Test-Path $pl) { Remove-Item $pl -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $pl "bin\x86"),(Join-Path $pl "scripts") | Out-Null
Copy-Item (Join-Path $inst "bin\*.dll")     (Join-Path $pl "bin") -Force
Copy-Item (Join-Path $inst "bin\x86\*.dll") (Join-Path $pl "bin\x86") -Force
Copy-Item (Join-Path $inst "bin\broslmv_conn.txt") (Join-Path $pl "bin") -Force
Copy-Item (Join-Path $inst "scripts\*")     (Join-Path $pl "scripts") -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $inst "host")) {
    Copy-Item (Join-Path $inst "host") (Join-Path $pl "host") -Recurse -Force
}
if (Test-Path (Join-Path $inst "workers")) {
    Copy-Item (Join-Path $inst "workers") (Join-Path $pl "workers") -Recurse -Force
}
if (Test-Path (Join-Path $inst "runtimes")) {
    Copy-Item (Join-Path $inst "runtimes") (Join-Path $pl "runtimes") -Recurse -Force
}
Copy-Item (Join-Path $inst "assets\BrosLMV.ico") (Join-Path $pl "BrosLMV.ico") -Force
$zip = Join-Path $pInst "payload.zip"; if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pl "*") -DestinationPath $zip -Force

# La version de los .exe se toma del addon empacado (fuente de verdad unica), NO de un
# <Version> fijo en los .csproj -- eso es lo que se desincronizo la ultima vez.
$addonVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $inst "bin\BrosLMVClsMain.dll")).Version.ToString(3)
Write-Host "   Version tomada del addon empacado: $addonVersion" -ForegroundColor DarkCyan

Write-Host "3) Compilando BrosLMV-Instalador.exe..." -ForegroundColor Cyan
dotnet build (Join-Path $pInst "Empresas.csproj") -c Release -o $dist "/p:Version=$addonVersion"
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR compilando Instalador" -ForegroundColor Red; exit 1 }

Write-Host "4) Compilando BrosLMV-Desinstalador.exe..." -ForegroundColor Cyan
dotnet build (Join-Path $pDes "Desinstalador.csproj") -c Release -o $dist "/p:Version=$addonVersion"
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR compilando Desinstalador" -ForegroundColor Red; exit 1 }

Write-Host "5) Limpiando temporales..." -ForegroundColor Cyan
foreach ($d in @((Join-Path $pInst "obj"),(Join-Path $pInst "bin"),(Join-Path $pDes "obj"),(Join-Path $pDes "bin"))) {
    Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "LISTO. Ejecutables en: $dist" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
