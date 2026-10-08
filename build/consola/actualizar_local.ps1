# BrosLMV - GPL-3.0. Copyright (C) 2026 Cristofer Candelas Garcia.
# Actualiza solo el addon local, sin provisionar empresas ni copiar plantillas.
param(
    [Parameter(Mandatory=$true)][string]$Respaldo,
    [Parameter(Mandatory=$true)][string]$RegistroHumo,
    [switch]$Elevado
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$origen = Join-Path $root 'instalador\bin\BrosLMVClsMain.dll'
$destino = 'C:\BrosLMV\bin\BrosLMVClsMain.dll'
$regasm = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe'
$Respaldo = [IO.Path]::GetFullPath($Respaldo)
if (-not $Respaldo.StartsWith('C:\RespaldosLMV\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El respaldo debe quedar dentro de C:\RespaldosLMV.'
}
if (-not (Test-Path -LiteralPath $Respaldo -PathType Container)) { throw 'Falta la carpeta de respaldo.' }
$lineas = @(Get-Content -LiteralPath $RegistroHumo | Where-Object { $_.Trim() -ne '' })
if ($lineas.Count -eq 0 -or $lineas[-1] -notmatch '^Todos los casos en verde \(46\)\.$' -or
    @($lineas | Where-Object { $_ -match '^\[ROJO\]' }).Count -ne 0) {
    throw 'La bateria completa debe estar en verde antes de actualizar.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Elevado) { throw 'No se concedieron permisos de administrador.' }
    $argumentos = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath +
        '" -Respaldo "' + $Respaldo + '" -RegistroHumo "' + $RegistroHumo + '" -Elevado'
    $proceso = Start-Process powershell -Verb RunAs -WindowStyle Hidden -ArgumentList $argumentos -Wait -PassThru
    exit $proceso.ExitCode
}

$log = Join-Path $Respaldo 'actualizacion-local.log'
Start-Transcript -LiteralPath $log -Force | Out-Null
$copiado = $false
$original = Join-Path $Respaldo 'BrosLMVClsMain.dll'
$tlb = [IO.Path]::ChangeExtension($destino, '.tlb')
$originalTlb = Join-Path $Respaldo 'BrosLMVClsMain.tlb'
try {
    if (Get-Process ComercialSP -ErrorAction SilentlyContinue) {
        throw 'Cierra Comercial voluntariamente antes de actualizar. No se cerrara a la fuerza.'
    }
    $version = [Reflection.AssemblyName]::GetAssemblyName($origen).Version
    $anterior = [Reflection.AssemblyName]::GetAssemblyName($destino).Version
    if ($version -ne [Version]'3.1.4.0' -or $anterior -ne [Version]'3.1.3.0') {
        throw "Versiones no esperadas: origen $version, instalado $anterior."
    }
    # Una DLL cargada por otro proceso impide este acceso exclusivo; nunca se fuerza el cierre.
    $archivo = [IO.File]::Open($destino, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archivo.Dispose()
    if (Test-Path -LiteralPath $original) {
        if ((Get-FileHash -LiteralPath $original).Hash -ne (Get-FileHash -LiteralPath $destino).Hash) {
            throw 'El respaldo existente no coincide con el archivo instalado; no se sobrescribe.'
        }
    } else { Copy-Item -LiteralPath $destino -Destination $original }
    if ((Test-Path -LiteralPath $tlb) -and -not (Test-Path -LiteralPath $originalTlb)) {
        Copy-Item -LiteralPath $tlb -Destination $originalTlb
    }
    $copiado = $true
    Copy-Item -LiteralPath $origen -Destination $destino -Force
    # La interfaz publica no cambia en 3.1.4. Registrar tipos, sin exportar protocolo/SDK a TLB.
    & $regasm $destino /codebase
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el registro COM de 32 bits.' }
    $registro = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
    try {
        $clave = $registro.OpenSubKey('SOFTWARE\Classes\CLSID\{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}\InprocServer32')
        if ($null -eq $clave) { throw 'No se encontro el registro COM de 32 bits.' }
        try {
            if ($clave.GetValue('Assembly') -notmatch 'Version=3\.1\.4\.0') { throw 'El registro COM no apunta a la nueva version.' }
        } finally { $clave.Dispose() }
    } finally { $registro.Dispose() }
    if ((Get-FileHash -LiteralPath $origen).Hash -ne (Get-FileHash -LiteralPath $destino).Hash) {
        throw 'La DLL instalada no coincide con la compilacion validada.'
    }
    Write-Host "ACTUALIZADO: $destino, version $version. Sin cambios SQL ni plantillas."
} catch {
    $fallo = $_
    if ($copiado) {
        try { & $regasm $destino /unregister }
        catch { Write-Host 'No se pudo desregistrar el candidato; se restaurara el original.' }
        Copy-Item -LiteralPath $original -Destination $destino -Force
        if (Test-Path -LiteralPath $originalTlb) { Copy-Item -LiteralPath $originalTlb -Destination $tlb -Force }
        & $regasm $destino /codebase
        if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: revisar el registro COM durante la restauracion.' }
        Write-Host 'Se restauro la DLL anterior desde el respaldo.'
    }
    Write-Error $fallo -ErrorAction Continue
    Stop-Transcript | Out-Null
    exit 1
}
Stop-Transcript | Out-Null
exit 0
