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
$runnerOut = Join-Path $root "instalador\runner"
$lib  = Join-Path $root "instalador\lib"

Write-Host "==================================================="
Write-Host " Generar instalador BrosLMV"
Write-Host "==================================================="

# Si compilas en el mismo equipo donde corre CONTPAQi, cierra ComercialSP para
# que no bloquee la DLL (en un equipo de desarrollo puro, esto no hace nada).
Get-Process ComercialSP -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "0) Documentacion HTML de las plantillas (se incrusta en la DLL; clic secundario -> Ver documentacion)..." -ForegroundColor Cyan
$docsPlant = Join-Path $root "instalador\docs\plantillas"
New-Item -ItemType Directory -Force $docsPlant | Out-Null
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    # Cada plantilla de fabrica declara «Documentacion: X.html» en su cabecera; se genera desde docs\X.md (sin lista fija).
    foreach ($f in Get-ChildItem (Join-Path $root "instalador\scripts") -File | Where-Object { $_.Extension -in ".ctx",".csx",".py",".sql" }) {
        $cab = (Get-Content $f.FullName -TotalCount 40) -join "`n"
        if ($cab -match '(?im)^\s*(//|#|--)\s*Documentaci.n\s*:\s*([A-Za-z0-9_]+)\.html') {
            $md = Join-Path $root ("docs\" + $Matches[2] + ".md")
            if (Test-Path $md) { & python (Join-Path $root "build\md_a_html.py") $md (Join-Path $docsPlant ($Matches[2] + ".html")) }
            else { Write-Host ("   AVISO: la plantilla {0} declara documentacion {1}.html pero no existe docs\{1}.md" -f $f.Name, $Matches[2]) -ForegroundColor Yellow }
        }
    }
    & python (Join-Path $root "build\md_a_html.py") (Join-Path $root "docs\CREAR_BOTON.md") (Join-Path $docsPlant "CREAR_BOTON.html")
    & python (Join-Path $root "build\sdk\generar_referencia_sdk.py")
} else {
    Write-Host "   AVISO: no hay Python; se usa el HTML ya versionado en instalador\docs\plantillas." -ForegroundColor Yellow
}

Write-Host "0b) Catalogo de iconos BrosLMV (.ico, desde Lucide/ISC; requiere Node.js)..." -ForegroundColor Cyan
$iconosSrc = Join-Path $root "build\iconos"
$iconosOut = Join-Path $root "instalador\iconos"
$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
    Push-Location $iconosSrc
    try {
        if (-not (Test-Path (Join-Path $iconosSrc "node_modules\lucide-static"))) { & npm install --no-audit --no-fund 2>&1 | Out-Null }
        if (Test-Path $iconosOut) { Remove-Item $iconosOut -Recurse -Force }
        & node generar_iconos.js $iconosOut
        if ($LASTEXITCODE -ne 0) { Write-Host "AVISO: no se pudo generar el catalogo de iconos (el instalador saldra sin el)." -ForegroundColor Yellow }
    } finally { Pop-Location }
} else {
    Write-Host "   AVISO: no hay Node.js; el instalador saldra sin el catalogo de iconos BrosLMV." -ForegroundColor Yellow
}

Write-Host "1) Compilando addon..." -ForegroundColor Cyan
dotnet build (Join-Path $src "BrosLMV.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION" -ForegroundColor Red; exit 1 }

Write-Host "1b) Verificando que el catalogo del SDK cubra toda funcion publica..." -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build\sdk\verificar_catalogo_sdk.ps1") -Dll (Join-Path $out "BrosLMVClsMain.dll")
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: el catalogo del SDK no cubre todas las funciones publicas (ver arriba)." -ForegroundColor Red; exit 1 }

Write-Host "2) Actualizando instalador\bin..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Join-Path $bin "x86") | Out-Null
Copy-Item (Join-Path $out "*.dll") $bin -Force
Copy-Item (Join-Path $out "x86\SQLite.Interop.dll") (Join-Path $bin "x86") -Force
# WebView2Loader.dll (nativo, x86 porque el addon corre EN PROCESO dentro de Comercial,
# que es de 32 bits) -- lo necesita ctx.show_html. A diferencia de SQLite.Interop.dll,
# el loader de WebView2 busca en el mismo directorio que la DLL que lo invoca (no en un
# subdirectorio x86\), asi que va directo en $bin junto a BrosLMVClsMain.dll.
Copy-Item (Join-Path $out "runtimes\win-x86\native\WebView2Loader.dll") $bin -Force

Write-Host "3) Compilando host v3.0..." -ForegroundColor Cyan
if (Test-Path $hostOut) { Remove-Item $hostOut -Recurse -Force }
dotnet publish (Join-Path $hostProj "BrosLMV.Host.csproj") -c Release -r win-x64 --self-contained true -o $hostOut
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION DEL HOST" -ForegroundColor Red; exit 1 }

Write-Host "3b) Compilando Runner headless (T3.3 -- lo invoca un CRM externo y cualquier otro consumidor externo por linea de comandos)..." -ForegroundColor Cyan
if (Test-Path $runnerOut) { Remove-Item $runnerOut -Recurse -Force }
dotnet build (Join-Path $root "runner\BrosLMV.Runner.csproj") -c Release -o $runnerOut
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION DEL RUNNER" -ForegroundColor Red; exit 1 }

Write-Host "3c) Compilando BrosLMV.Disenador (editor visual de formatos, programa aparte)..." -ForegroundColor Cyan
$disOut = Join-Path $root "instalador\disenador"
if (Test-Path $disOut) { Remove-Item $disOut -Recurse -Force }
dotnet publish (Join-Path $root "designer\BrosLMV.Disenador.csproj") -c Release -o $disOut
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION DEL DISENADOR" -ForegroundColor Red; exit 1 }

Write-Host "3d) Compilando BrosLMV.HtmlToPdf (HTML -> PDF con WebView2)..." -ForegroundColor Cyan
$htmlpdfOut = Join-Path $root "instalador\htmlpdf"
if (Test-Path $htmlpdfOut) { Remove-Item $htmlpdfOut -Recurse -Force }
dotnet build (Join-Path $root "htmlpdf\BrosLMV.HtmlToPdf.csproj") -c Release -o $htmlpdfOut
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR DE COMPILACION DE HtmlToPdf" -ForegroundColor Red; exit 1 }
# Scripts CORE de la funcion PDF/formato (compartidos, no por empresa) -- se refrescan siempre.
Copy-Item (Join-Path $root "htmlpdf\GenerarDocumentoPDF.ctx") (Join-Path $root "instalador\scripts\Cotizador.ctx") -Force
Copy-Item (Join-Path $root "htmlpdf\ConfiguracionFormato.ctx") (Join-Path $root "instalador\scripts\ConfiguracionFormato.ctx") -Force
# 10 plantillas HTML genericas (gris/azul, con QR)
$fmtDst = Join-Path $root "instalador\formatos"
New-Item -ItemType Directory -Force $fmtDst | Out-Null
Copy-Item (Join-Path $root "htmlpdf\formatos\*.html") $fmtDst -Force

Write-Host "4) Copiando workers..." -ForegroundColor Cyan
$workersSrc = Join-Path $root "workers"
$workersDst = Join-Path $root "instalador\workers"
if (Test-Path $workersSrc) {
    if (Test-Path $workersDst) { Remove-Item $workersDst -Recurse -Force }
    Copy-Item $workersSrc $workersDst -Recurse -Force
}

Write-Host "5) Verificando instalador\lib (librerias externas para #r)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $lib | Out-Null
$nLib = (Get-ChildItem (Join-Path $lib "*.dll") -ErrorAction SilentlyContinue).Count
if ($nLib -eq 0) {
    Write-Host "   AVISO: instalador\lib esta vacio. Corre primero:" -ForegroundColor Yellow
    Write-Host "     .\build\descargar_librerias_externas.ps1" -ForegroundColor Yellow
} else {
    Write-Host "   $nLib archivo(s) en instalador\lib listos para copiarse."
}

Write-Host "5b) Copiando runtime compartido de dashboards (ctx.dashboard)..." -ForegroundColor Cyan
# La FUENTE versionada vive en instalador\assets\dashboard\ (NO en instalador\lib\, que
# esta en .gitignore como "binarios regenerables" -- ver DASHBOARDS_HTML.md). Aqui se
# copia al destino que si empaqueta el instalador.
$dashboardSrc = Join-Path $root "instalador\assets\dashboard"
$dashboardDst = Join-Path $lib "dashboard"
if (Test-Path $dashboardSrc) {
    New-Item -ItemType Directory -Force $dashboardDst | Out-Null
    Copy-Item (Join-Path $dashboardSrc "*") $dashboardDst -Recurse -Force
    Write-Host "   instalador\lib\dashboard actualizado."
} else {
    Write-Host "   AVISO: no se encontro instalador\assets\dashboard -- ctx.dashboard() no funcionara." -ForegroundColor Yellow
}

Write-Host "6) Limpiando temporales..." -ForegroundColor Cyan
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $src "obj") -Recurse -Force -ErrorAction SilentlyContinue

$n = (Get-ChildItem (Join-Path $bin "*.dll")).Count
$runnerOk = Test-Path (Join-Path $runnerOut "BrosLMV.Runner.exe")
Write-Host ""
Write-Host "LISTO. instalador\ actualizado ($n DLLs + x86\SQLite.Interop.dll + host + runner$(if(-not $runnerOk){' [FALTA]'}) + $nLib lib)." -ForegroundColor Green
Write-Host "Distribuye la carpeta 'instalador' y corre Instalar.ps1 en cada equipo."
