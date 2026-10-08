# BrosLMV - GPL-3.0. Copyright (C) 2026 Cristofer Candelas Garcia.
# Prueba aislada del layout: compila una copia temporal, sin conexion ni instalacion.
param([string]$Salida = (Join-Path $PSScriptRoot '..\out\consola-diseno'))
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$temp = Join-Path $env:TEMP ('BrosLMV-ConsolaLayout-' + [Guid]::NewGuid().ToString('N'))
$src = Join-Path $temp 'src'
New-Item -ItemType Directory -Path $src -Force | Out-Null
Copy-Item (Join-Path $root 'src\*.cs') $src
Copy-Item (Join-Path $root 'src\BrosLMV.csproj') $src
Copy-Item (Join-Path $root 'src\assets') $src -Recurse
New-Item -ItemType Directory -Path (Join-Path $temp 'protocol') | Out-Null
Copy-Item (Join-Path $root 'protocol\broslmv.proto') (Join-Path $temp 'protocol')
Copy-Item (Join-Path $PSScriptRoot 'PruebaDiseno.cs') $src

# Solo la copia de laboratorio omite el arranque de empresa y el acceso SQL.
# El constructor original y sus controles de acceso nunca se modifican.
$consola = Join-Path $src 'Consola.cs'
$codigo = [IO.File]::ReadAllText($consola)
$inicio = $codigo.IndexOf('        public BrosConsola(int userId, object xEngineLib)')
$fin = $codigo.IndexOf('        // Evita refrescar en el primer Activated', $inicio)
if ($inicio -lt 0 -or $fin -le $inicio) { throw 'No se encontro el constructor para la prueba aislada.' }
$constructor = "        public BrosConsola(int userId, object xEngineLib) { BuildUI(); CargarMetodos(); NuevoScript(); }`r`n`r`n"
[IO.File]::WriteAllText($consola, $codigo.Substring(0, $inicio) + $constructor + $codigo.Substring($fin), [Text.UTF8Encoding]::new($false))

[xml]$proyecto = [IO.File]::ReadAllText((Join-Path $src 'BrosLMV.csproj'))
$grupo = $proyecto.Project.ItemGroup | Where-Object { $_.Compile } | Select-Object -First 1
$compile = $proyecto.CreateElement('Compile')
$compile.SetAttribute('Include', 'PruebaDiseno.cs')
[void]$grupo.AppendChild($compile)
$proyecto.Project.PropertyGroup.OutputType = 'Exe'
$proyecto.Project.PropertyGroup.PlatformTarget = 'x86'
$proyecto.Save((Join-Path $src 'BrosLMV.csproj'))
$bin = Join-Path $temp 'bin'
dotnet build (Join-Path $src 'BrosLMV.csproj') -c Release -o $bin
if ($LASTEXITCODE -ne 0) { throw 'No compilo la copia de prueba.' }
$Salida = [IO.Path]::GetFullPath($Salida)
New-Item -ItemType Directory -Path $Salida -Force | Out-Null
& (Join-Path $bin 'BrosLMVClsMain.exe') $Salida
if ($LASTEXITCODE -ne 0) { throw 'Fallo la prueba de la Consola.' }
Write-Host "Layout comprobado. Capturas: $Salida"
