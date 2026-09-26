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

# Desinstalar.ps1
# Quita el registro COM de BrosLMV. Ejecutar como ADMINISTRADOR.
# (No borra C:\BrosLMV ni los botones de la BD; eso se hace aparte si se desea.)

$ErrorActionPreference = "Continue"
$base   = "C:\BrosLMV"
$regasm = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe"

Get-Process ComercialSP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

if (Test-Path "$base\bin\BrosLMVClsMain.dll") {
    & $regasm "$base\bin\BrosLMVClsMain.dll" /unregister
    Write-Host "COM BrosLMV des-registrado."
} else {
    Write-Host "No se encontro $base\bin\BrosLMVClsMain.dll"
}

Write-Host ""
Write-Host "Si quieres borrar tambien archivos y botones:"
Write-Host "  - Carpeta:  Remove-Item -Recurse -Force $base"
Write-Host "  - Botones (en la BD de la empresa):"
Write-Host "      DELETE FROM engRibbonMenu WHERE ControlID IN"
Write-Host "        (SELECT ControlID FROM engRibbonControl WHERE ControlExecute LIKE 'BrosLMV.%');"
Write-Host "      DELETE FROM engRibbonControl WHERE ControlExecute LIKE 'BrosLMV.%';"
