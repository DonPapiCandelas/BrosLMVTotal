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

# descargar_librerias_externas.ps1 - deja en instalador\lib los DLL de terceros
# que los scripts C# cargan con #r. Requiere .NET SDK e internet.
# Uso:
#   .\build\descargar_librerias_externas.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$lib = Join-Path $root "instalador\lib"
$temp = Join-Path $root ".temp_tests\lib_externas_fetch"

Write-Host "==================================================="
Write-Host " Descargar librerias externas para scripts C#"
Write-Host "==================================================="

if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
New-Item -ItemType Directory -Force $temp | Out-Null

$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="QRCoder" Version="1.6.0" />
    <PackageReference Include="ClosedXML" Version="0.102.3" />
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2739.15" />
  </ItemGroup>
</Project>
"@
Set-Content -Path (Join-Path $temp "fetch.csproj") -Value $csproj -Encoding UTF8

Write-Host "1) Restaurando paquetes NuGet..." -ForegroundColor Cyan
dotnet build (Join-Path $temp "fetch.csproj") -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR al restaurar/compilar" -ForegroundColor Red; exit 1 }

Write-Host "2) Copiando DLLs a instalador\lib..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $lib | Out-Null
$out = Join-Path $temp "bin\Release\net48"
Get-ChildItem (Join-Path $out "*.dll") | Where-Object {
    $_.Name -notin @("fetch.dll", "Microsoft.Web.WebView2.Wpf.dll")
} | Copy-Item -Destination $lib -Force
# win-x86: ComercialSP.exe es un proceso de 32 bits.
Copy-Item (Join-Path $out "runtimes\win-x86\native\WebView2Loader.dll") $lib -Force

Write-Host "3) Limpiando temporales..." -ForegroundColor Cyan
Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

$n = (Get-ChildItem (Join-Path $lib "*.dll")).Count
Write-Host ""
Write-Host "LISTO. instalador\lib tiene $n archivo(s)." -ForegroundColor Green
Write-Host "Corre build\generar_instalador.ps1 despues de esto para empacar todo junto."
