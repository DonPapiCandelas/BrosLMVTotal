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

# Instalar.ps1
# Instala BrosLMV en este equipo. Ejecutar como ADMINISTRADOR (PowerShell).
#   Clic derecho en PowerShell -> "Ejecutar como administrador", luego:
#   cd <carpeta de este paquete> ;  .\Instalar.ps1

$ErrorActionPreference = "Stop"
$pkg  = $PSScriptRoot
$base = "C:\BrosLMV"
$regasm = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"

Write-Host "==================================================="
Write-Host " Instalador BrosLMV - Botones para CONTPAQi"
Write-Host "==================================================="

# 0) Verificar privilegios de administrador
$esAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) {
    Write-Host "ERROR: ejecuta este script como ADMINISTRADOR (RegAsm escribe en HKLM)." -ForegroundColor Red
    exit 1
}

# 1) Cerrar CONTPAQi si esta abierto (bloquea la DLL)
Get-Process ComercialSP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# 2) Crear carpetas
New-Item -ItemType Directory -Force "$base\bin","$base\bin\x86","$base\scripts","$base\logs","$base\data" | Out-Null
Write-Host "Carpetas listas en $base"

# 3) Copiar DLLs (host + Roslyn + Scintilla + SQLite + dependencias)
Copy-Item "$pkg\bin\*.dll" "$base\bin" -Force
# Nativo de SQLite (x86) que System.Data.SQLite carga aparte
Copy-Item "$pkg\bin\x86\*.dll" "$base\bin\x86" -Force
$nDll = (Get-ChildItem "$base\bin\*.dll").Count
Write-Host "DLLs copiadas: $nDll"

# 4) Copiar scripts de ejemplo (sin sobrescribir los que ya existan)
Get-ChildItem "$pkg\scripts\*.ctx","$pkg\scripts\*.csx" -ErrorAction SilentlyContinue | ForEach-Object {
    $dst = Join-Path "$base\scripts" $_.Name
    if (-not (Test-Path $dst)) { Copy-Item $_.FullName $dst }
}

# 5) Cadena de conexion de respaldo: copiar plantilla solo si no existe.
#    NOTA: la conexion es automatica (reutiliza la de CONTPAQi); este archivo
#    solo se usa como respaldo y normalmente no hace falta editarlo.
if (-not (Test-Path "$base\bin\broslmv_conn.txt")) {
    Copy-Item "$pkg\bin\broslmv_conn.txt" "$base\bin\broslmv_conn.txt"
}

# 5b) Copiar el icono del boton a la carpeta Icons de ComercialSP (SIEMPRE).
#     Comercial busca ahi el icono que referencia el ribbon (IconFile='BrosLMV.ico').
$icoSrc = Join-Path $pkg "assets\BrosLMV.ico"
if (Test-Path $icoSrc) {
    $comercialDirs = @(
        "C:\Program Files (x86)\Compac\ComercialSP\Icons",
        "C:\Program Files\Compac\ComercialSP\Icons"
    )
    $icoCopiado = $false
    foreach ($icoDir in $comercialDirs) {
        $comercialRoot = Split-Path $icoDir -Parent
        if (Test-Path $comercialRoot) {
            New-Item -ItemType Directory -Force $icoDir | Out-Null
            Copy-Item $icoSrc (Join-Path $icoDir "BrosLMV.ico") -Force
            Write-Host "Icono BrosLMV.ico copiado a: $icoDir"
            $icoCopiado = $true
        }
    }
    if (-not $icoCopiado) {
        Write-Host "AVISO: no se encontro ...\Compac\ComercialSP. Copia assets\BrosLMV.ico a la carpeta Icons de Comercial manualmente." -ForegroundColor Yellow
    }
} else {
    Write-Host "AVISO: no se encontro assets\BrosLMV.ico en el paquete." -ForegroundColor Yellow
}

# 6) Registrar el COM (RegAsm de 32 bits)
& $regasm "$base\bin\BrosLMVClsMain.dll" /codebase /tlb
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR al registrar COM" -ForegroundColor Red; exit 1 }

# 7) Asegurar el registro en el hive de 32 bits (WOW6432Node).
#    RegAsm a veces escribe el mapeo ProgID->CLSID solo en el hive de 64 bits,
#    pero ComercialSP.exe es de 32 bits y lo busca en WOW6432Node. Lo forzamos.
$clsid = "{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}"
# Si el arbol del CLSID quedo solo en 64-bit, copialo a WOW6432Node
if (-not (Test-Path "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$clsid\InprocServer32")) {
    cmd /c "reg copy `"HKLM\SOFTWARE\Classes\CLSID\$clsid`" `"HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\$clsid`" /s /f" | Out-Null
}
# Asegurar el mapeo ProgID -> CLSID en 32 bits
New-Item -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\BrosLMV.clsMain\CLSID" -Force | Out-Null
Set-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Classes\BrosLMV.clsMain\CLSID" -Name "(default)" -Value $clsid

# Verificar
$p = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Classes\BrosLMV.clsMain\CLSID" -ErrorAction SilentlyContinue
$ip = Test-Path "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$clsid\InprocServer32"
if ($p -and $ip) { Write-Host "OK. ProgID BrosLMV.clsMain registrado (32-bit): $($p.'(default)')" -ForegroundColor Green }
else { Write-Host "ADVERTENCIA: registro 32-bit incompleto (ProgID=$([bool]$p) InprocServer32=$ip)." -ForegroundColor Yellow }

Write-Host ""
Write-Host "=============== SIGUIENTES PASOS ==================="
Write-Host "1) Provisiona la BD de la empresa (crea tablas zzBros* + el boton):"
Write-Host "      sql\provision_empresa.sql"
Write-Host "2) Reinicia CONTPAQi. En la pestana GENERAL, grupo 'BrosLMV',"
Write-Host "   aparecera el boton 'Consola BrosLMV'."
Write-Host ""
Write-Host "La conexion a la base es AUTOMATICA (reutiliza la de CONTPAQi)."
Write-Host "Si este equipo es una TERMINAL: solo corre este Instalar.ps1; las tablas"
Write-Host "y el boton ya estan en la BD compartida (no se copian scripts)."
Write-Host "==================================================="
