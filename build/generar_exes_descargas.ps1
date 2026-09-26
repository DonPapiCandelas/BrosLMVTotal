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

# generar_exes_descargas.ps1 -- contraparte de generar_exes.ps1 para BrosLMV.Descargas (el
# descargador de CFDI, proyecto separado del addon de CONTPAQi). Publica CLI+UI+servicio
# self-contained, los empaqueta en payload.zip dentro de descargas-instalador\, y compila el
# instalador y el desinstalador a dist\.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent          # C:\MLVTotal
$cli = Join-Path $root "descargas"
$ui = Join-Path $root "descargas-ui"
$svc = Join-Path $root "descargas-servicio"
$pInst = Join-Path $root "descargas-instalador"
$pDes = Join-Path $root "descargas-desinstalador"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host "1) Publicando CLI self-contained..." -ForegroundColor Cyan
$plCli = Join-Path $env:TEMP "bros_descargas_payload\cli"
if (Test-Path $plCli) { Remove-Item $plCli -Recurse -Force }
dotnet publish (Join-Path $cli "BrosLMV.Descargas.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $plCli
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR publicando CLI" -ForegroundColor Red; exit 1 }

Write-Host "2) Publicando UI self-contained..." -ForegroundColor Cyan
$plUi = Join-Path $env:TEMP "bros_descargas_payload\ui"
if (Test-Path $plUi) { Remove-Item $plUi -Recurse -Force }
dotnet publish (Join-Path $ui "BrosLMV.DescargasUI.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $plUi
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR publicando UI" -ForegroundColor Red; exit 1 }

Write-Host "2b) Publicando Servicio de Windows self-contained..." -ForegroundColor Cyan
$plSvc = Join-Path $env:TEMP "bros_descargas_payload\servicio"
if (Test-Path $plSvc) { Remove-Item $plSvc -Recurse -Force }
dotnet publish (Join-Path $svc "BrosLMV.Descargas.Servicio.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $plSvc
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR publicando Servicio" -ForegroundColor Red; exit 1 }

Write-Host "3) payload.zip (CLI+UI+servicio embebidos en el instalador)..." -ForegroundColor Cyan
$zip = Join-Path $pInst "payload.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $plCli, $plUi, $plSvc -DestinationPath $zip -Force
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "   payload.zip: $zipMb MB" -ForegroundColor DarkCyan

# La version se toma del propio instalador (unico <Version> fijo en este par de proyectos, no
# hay un "addon" separado del que heredarla como en generar_exes.ps1).
$version = ([xml](Get-Content (Join-Path $pInst "BrosLMV.Descargas.Instalador.csproj"))).Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { $version = "1.0.0" }
Write-Host "   Version: $version" -ForegroundColor DarkCyan

Remove-Item (Join-Path $dist "BrosLMV-Descargas-Instalador*.exe"), (Join-Path $dist "BrosLMV-Descargas-Desinstalador*.exe") -Force -ErrorAction SilentlyContinue
$instaladorExe = Join-Path $dist "BrosLMV-Descargas-Instalador-$version.exe"
$desinstaladorExe = Join-Path $dist "BrosLMV-Descargas-Desinstalador-$version.exe"

# A diferencia de instaladores\Empresas (net48, "dotnet build" ya produce un .exe administrado
# completo), en net8.0-windows "dotnet build" solo genera un apphost nativo que necesita el
# Escritorio de .NET 8 instalado en el equipo del cliente -- hace falta "dotnet publish
# --self-contained true -p:PublishSingleFile=true" para un .exe de verdad autocontenido.
Write-Host "4) Publicando BrosLMV-Descargas-Instalador.exe (self-contained)..." -ForegroundColor Cyan
$pubInst = Join-Path $env:TEMP "bros_descargas_pub_inst"
if (Test-Path $pubInst) { Remove-Item $pubInst -Recurse -Force }
# IncludeNativeLibrariesForSelfExtract=true: sin esto, WPF truena en tiempo de ejecucion con
# "System.DllNotFoundException" (PresentationNative_*.dll no queda dentro del bundle single-file
# por default) -- confirmado real 2026-08-19, el instalador ni abria.
dotnet publish (Join-Path $pInst "BrosLMV.Descargas.Instalador.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$version -o $pubInst
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR compilando Instalador" -ForegroundColor Red; exit 1 }
Copy-Item (Join-Path $pubInst "BrosLMV-Descargas-Instalador.exe") $instaladorExe -Force

Write-Host "5) Publicando BrosLMV-Descargas-Desinstalador.exe (self-contained)..." -ForegroundColor Cyan
$pubDes = Join-Path $env:TEMP "bros_descargas_pub_des"
if (Test-Path $pubDes) { Remove-Item $pubDes -Recurse -Force }
dotnet publish (Join-Path $pDes "BrosLMV.Descargas.Desinstalador.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$version -o $pubDes
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR compilando Desinstalador" -ForegroundColor Red; exit 1 }
Copy-Item (Join-Path $pubDes "BrosLMV-Descargas-Desinstalador.exe") $desinstaladorExe -Force

Write-Host "6) Verificando tamano (evita instaladores con el payload olvidado)..." -ForegroundColor Cyan
# Con -p:PublishSingleFile=true el .exe final es un bundle autoextraible -- ya no es una
# ensamblado IL "puro" que Assembly.LoadFile pueda reflejar (a diferencia de instaladores\Empresas
# en net48), asi que en vez de listar recursos embebidos se verifica el tamano: el payload.zip
# (CLI+UI+servicio self-contained) + el runtime de .NET 8 propio del instalador deberian dejarlo
# bien por encima de 100 MB -- si sale mucho mas chico, algo no se empaqueto.
$instMb = [math]::Round((Get-Item $instaladorExe).Length / 1MB, 1)
$desMb = [math]::Round((Get-Item $desinstaladorExe).Length / 1MB, 1)
$okInstalador = $instMb -gt 100
$okDesinstalador = $desMb -gt 20
Write-Host "   $(Split-Path $instaladorExe -Leaf): $instMb MB $(if ($okInstalador) {'OK'} else {'SOSPECHOSAMENTE CHICO'})" -ForegroundColor $(if ($okInstalador) {'DarkGreen'} else {'Red'})
Write-Host "   $(Split-Path $desinstaladorExe -Leaf): $desMb MB $(if ($okDesinstalador) {'OK'} else {'SOSPECHOSAMENTE CHICO'})" -ForegroundColor $(if ($okDesinstalador) {'DarkGreen'} else {'Red'})
if (-not $okInstalador -or -not $okDesinstalador) {
    Write-Host ""
    Write-Host "ABORTADO: algun .exe salio mas chico de lo esperado -- revisa antes de distribuir." -ForegroundColor Red
    exit 1
}

Write-Host "7) Limpiando temporales..." -ForegroundColor Cyan
foreach ($d in @((Join-Path $pInst "obj"), (Join-Path $pInst "bin"), (Join-Path $pDes "obj"), (Join-Path $pDes "bin"),
                 (Join-Path $env:TEMP "bros_descargas_payload"), $pubInst, $pubDes)) {
    Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "LISTO. Ejecutables verificados en: $dist" -ForegroundColor Green
Get-ChildItem $dist -Filter "BrosLMV-Descargas-*.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
