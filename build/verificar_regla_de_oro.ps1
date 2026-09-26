# verificar_regla_de_oro.ps1 (T4.2)
# Guardian automatico de la "REGLA DE ORO" del proyecto (ver docs\ESTADO.md):
# la version actual del addon (AssemblyVersion en src\ClsMain.cs) SIEMPRE debe tener
# una entrada correspondiente en docs\CHANGELOG.md y en src\assets\notas_version.html
# (lo que el usuario ve en "Acerca de"). Si alguien sube un cambio de version sin
# documentarlo, esto falla el build -- ya no depende de que nadie se acuerde.
#
# Uso local:  .\build\verificar_regla_de_oro.ps1
# Sale con exit code 1 si algo falta (para que CI lo marque en rojo).

$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$clsMain = Join-Path $raiz "src\ClsMain.cs"
$changelog = Join-Path $raiz "docs\CHANGELOG.md"
$notas = Join-Path $raiz "src\assets\notas_version.html"

foreach ($f in @($clsMain, $changelog, $notas)) {
    if (-not (Test-Path $f)) { Write-Error "No se encontro: $f"; exit 1 }
}

$contenidoCls = Get-Content $clsMain -Raw
$m = [regex]::Match($contenidoCls, 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)\.(\d+)"\)')
if (-not $m.Success) {
    Write-Error "No se pudo leer AssemblyVersion de src\ClsMain.cs"
    exit 1
}
# La version "de producto" son los primeros 3 numeros (X.Y.Z) -- el 4o (build) no se
# usa en CHANGELOG.md/notas_version.html, siempre es .0 por convencion del proyecto.
$version = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"

Write-Host "Version actual del addon (src\ClsMain.cs): $version"

$fallas = @()

$contenidoChangelog = Get-Content $changelog -Raw
if ($contenidoChangelog -notmatch [regex]::Escape("[$version]")) {
    $fallas += "docs\CHANGELOG.md no tiene una entrada '## [$version]' -- documenta el cambio antes de mergear."
}

$contenidoNotas = Get-Content $notas -Raw
if ($contenidoNotas -notmatch [regex]::Escape("<h2>$version</h2>")) {
    $fallas += "src\assets\notas_version.html no tiene '<h2>$version</h2>' -- el usuario no vera esta version en Acerca de."
}

if ($fallas.Count -gt 0) {
    Write-Host ""
    Write-Host "REGLA DE ORO ROTA:" -ForegroundColor Red
    foreach ($f in $fallas) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Ver docs\ESTADO.md 'REGLA DE ORO: documentar todo, siempre'." -ForegroundColor Yellow
    exit 1
}

Write-Host "OK: version $version documentada en CHANGELOG.md y notas_version.html." -ForegroundColor Green
exit 0
