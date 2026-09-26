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

# descargar_python.ps1 - C4: prepara CPython embeddable x64 para el payload.
# Descarga el ZIP oficial de python.org y lo deja en instalador\runtimes\python.
# Uso:
#   .\build\descargar_python.ps1
#   .\build\descargar_python.ps1 -Version 3.13.14 -Sha256 <hash-oficial>

param(
    [string]$Version = "3.13.14",
    [string]$Sha256 = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $root "instalador\runtimes\python"
$cache = Join-Path $root ".temp_tests\downloads"
$zipName = "python-$Version-embed-amd64.zip"
$url = "https://www.python.org/ftp/python/$Version/$zipName"
$zip = Join-Path $cache $zipName

Write-Host "==================================================="
Write-Host " Descargar CPython embeddable x64 para BrosLMV"
Write-Host "==================================================="
Write-Host "Version : $Version"
Write-Host "URL     : $url"
Write-Host "Destino : $runtime"

New-Item -ItemType Directory -Force $cache | Out-Null

if ($Force -or !(Test-Path $zip)) {
    Write-Host "1) Descargando ZIP oficial..." -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zip
} else {
    Write-Host "1) Usando ZIP cacheado: $zip" -ForegroundColor DarkGray
}

if ($Sha256) {
    Write-Host "2) Validando SHA256..." -ForegroundColor Cyan
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256.ToLowerInvariant()) {
        throw "SHA256 invalido. Esperado=$Sha256 Actual=$actual"
    }
} else {
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "2) SHA256 calculado (anotalo para releases reproducibles): $actual" -ForegroundColor Yellow
}

Write-Host "3) Extrayendo runtime..." -ForegroundColor Cyan
if (Test-Path $runtime) { Remove-Item $runtime -Recurse -Force }
New-Item -ItemType Directory -Force $runtime | Out-Null
Expand-Archive -Path $zip -DestinationPath $runtime -Force

Write-Host "4) Habilitando import site + Lib\site-packages (pip vendorizado)..." -ForegroundColor Cyan
$pth = Get-ChildItem $runtime -Filter "python*._pth" | Select-Object -First 1
if ($pth) {
    $txt = Get-Content $pth.FullName -Raw
    $txt = $txt -replace '(?m)^#import site\s*$', 'import site'
    if ($txt -notmatch '(?m)^Lib\\site-packages\s*$') {
        $txt = $txt -replace '(?m)^(\.\s*)$', "`$1`r`nLib\site-packages"
    }
    Set-Content -Path $pth.FullName -Value $txt -Encoding ASCII
}

Write-Host "5) Prueba de humo..." -ForegroundColor Cyan
$py = Join-Path $runtime "python.exe"
if (!(Test-Path $py)) { throw "No se encontro python.exe en $runtime" }
& $py -c "import sys; print(sys.version); print(sys.executable)"
if ($LASTEXITCODE -ne 0) { throw "python.exe no pudo ejecutar la prueba de humo." }

Write-Host "6) Bootstrap de pip..." -ForegroundColor Cyan
$getPip = Join-Path $cache "get-pip.py"
if ($Force -or !(Test-Path $getPip)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip
}
& $py $getPip --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar pip en el runtime embebido." }

Write-Host "7) Instalando pythonnet (WinForms puro desde Python, ver DECISIONES.md)..." -ForegroundColor Cyan
& $py -m pip install --no-warn-script-location pythonnet
if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar pythonnet en el runtime embebido." }

Write-Host "8) Prueba de humo pythonnet + WinForms..." -ForegroundColor Cyan
& $py -c "import clr; clr.AddReference('System.Windows.Forms'); from System.Windows.Forms import Form; print('pythonnet OK: Form disponible')"
if ($LASTEXITCODE -ne 0) { throw "pythonnet no pudo cargar System.Windows.Forms en el runtime embebido." }

Write-Host "9) Instalando setuptools/wheel (proxy_tools de pywebview solo trae sdist)..." -ForegroundColor Cyan
& $py -m pip install --no-warn-script-location setuptools wheel
if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar setuptools/wheel en el runtime embebido." }

Write-Host "10) Instalando pywebview (UI en HTML/CSS real, motor WebView2/Edge)..." -ForegroundColor Cyan
& $py -m pip install --no-warn-script-location pywebview
if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar pywebview en el runtime embebido." }

Write-Host "11) Prueba de humo pywebview..." -ForegroundColor Cyan
& $py -c "import webview; print('pywebview OK')"
if ($LASTEXITCODE -ne 0) { throw "pywebview no pudo importarse en el runtime embebido." }

Write-Host "12) Instalando openpyxl (ctx.read_excel / ctx.write_excel, sin depender de Excel instalado)..." -ForegroundColor Cyan
& $py -m pip install --no-warn-script-location openpyxl
if ($LASTEXITCODE -ne 0) { throw "No se pudo instalar openpyxl en el runtime embebido." }

Write-Host "13) Prueba de humo openpyxl..." -ForegroundColor Cyan
& $py -c "import openpyxl; print('openpyxl OK', openpyxl.__version__)"
if ($LASTEXITCODE -ne 0) { throw "openpyxl no pudo importarse en el runtime embebido." }

Write-Host ""
Write-Host "LISTO. CPython embeddable + pythonnet + pywebview + openpyxl quedo en instalador\runtimes\python." -ForegroundColor Green
