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

# verificar_catalogo_sdk.ps1 -- el catalogo del SDK (src\assets\sdk_catalogo.json) debe cubrir TODA funcion publica de ctx.* y ctx.erp.*.
# Falla (exit 1) si una funcion publica de ScriptContext/ErpContext no tiene entrada, si una referencia cruzada (equiv/ver) apunta a algo inexistente
# o si una entrada no trae nombre, firma y resumen. Asi el manual y el panel de referencias no se desactualizan.
# Uso: powershell -File build\sdk\verificar_catalogo_sdk.ps1 [-Dll C:\ruta\BrosLMVClsMain.dll]

param([string]$Dll = "")
$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Dll) { $Dll = Join-Path $root "build\out\BrosLMVClsMain.dll" }
if (-not (Test-Path $Dll)) { Write-Host "No se encontro la DLL: $Dll (compila primero)." -ForegroundColor Red; exit 1 }

$cat = Get-Content (Join-Path $root "src\assets\sdk_catalogo.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$ids = @{}
foreach ($e in $cat.entradas) { $ids[$e.id] = $e }
$errores = @()

# 1) entradas completas y referencias validas
foreach ($e in $cat.entradas) {
    if (-not $e.nombre -or -not $e.firma -or -not $e.resumen) { if (-not $e.interno) { $errores += "Entrada incompleta (falta nombre, firma o resumen): $($e.id)" } }
    if ($e.equiv -and -not $ids.ContainsKey($e.equiv)) { $errores += "Referencia rota: $($e.id) -> equiv $($e.equiv)" }
    foreach ($v in @($e.ver)) { if ($v -and -not $ids.ContainsKey($v)) { $errores += "Referencia rota: $($e.id) -> ver $v" } }
}

# 2) toda funcion publica tiene entrada
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Dll).Path)
$flags = [Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'
function Publicos($tipo) {
    $t = $asm.GetType($tipo)
    if (-not $t) { throw "No se encontro el tipo $tipo" }
    $t.GetMembers($flags) | Where-Object { $_.MemberType -in 'Method','Property','Field' -and -not $_.Name.StartsWith('get_') -and -not $_.Name.StartsWith('set_') -and $_.Name -ne 'GetType' -and $_.Name -ne 'ToString' -and $_.Name -ne 'Equals' -and $_.Name -ne 'GetHashCode' } | ForEach-Object { $_.Name } | Sort-Object -Unique
}
foreach ($n in Publicos 'BrosLMV.ScriptContext') { if (-not $ids.ContainsKey("cs:$n")) { $errores += "Falta en el catalogo: ctx.$n (agregalo a src\assets\sdk_catalogo.json; si es de uso interno, con `"interno`": true)" } }
foreach ($n in Publicos 'BrosLMV.ErpContext')    { if (-not $ids.ContainsKey("cs:erp.$n")) { $errores += "Falta en el catalogo: ctx.erp.$n (agregalo a src\assets\sdk_catalogo.json)" } }

if ($errores.Count -gt 0) {
    Write-Host "CATALOGO DEL SDK INCOMPLETO ($($errores.Count) problema(s)):" -ForegroundColor Red
    $errores | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
$publicas = @($cat.entradas | Where-Object { -not $_.interno }).Count
Write-Host "OK: catalogo del SDK completo ($publicas funciones publicas, $(@($cat.entradas | Where-Object { $_.detalle }).Count) con ficha completa)." -ForegroundColor Green
