# verificar_terminos_prohibidos.ps1
# Guardian: ningun archivo TRACKEADO por git debe mencionar nombres de terceros,
# clientes, servidores o rutas privadas (ver AGENTS.md #5 "Nunca nombrar terceros").
#
# La lista vive SOLO en este equipo -- .terminos_prohibidos.local (raiz, no
# versionado, ver .git\info\exclude). Si el archivo no existe (ej. un clon fresco o
# CI), este script avisa y sale en verde: no hay nada que comparar todavia, pero
# tampoco se puede fallar un chequeo que no tiene con que compararse.
#
# Uso local:  .\build\verificar_terminos_prohibidos.ps1
# Sale con exit code 1 si algun archivo trackeado menciona un termino de la lista.

$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$lista = Join-Path $raiz ".terminos_prohibidos.local"

if (-not (Test-Path $lista)) {
    Write-Host "AVISO: no existe .terminos_prohibidos.local -- nada que verificar en este equipo." -ForegroundColor Yellow
    exit 0
}

$terminos = Get-Content $lista | Where-Object { $_.Trim() -and -not $_.StartsWith('#') }
if ($terminos.Count -eq 0) {
    Write-Host "AVISO: .terminos_prohibidos.local esta vacio -- nada que verificar." -ForegroundColor Yellow
    exit 0
}
$rx = '(' + ($terminos -join '|') + ')'

# Excepcion unica y deliberada: AGENTS.md nombra la carpeta privada de material de
# terceros a proposito, para explicar por que ninguna otra carpeta debe reusar ese
# nombre (ver AGENTS.md #1). Es la unica mencion permitida en todo el repo --
# cualquier otra es una regresion real.
$excepciones = @('AGENTS.md')

Push-Location $raiz
try {
    # Rastreados + nuevos no ignorados: un archivo recien creado que todavia no se agrega
    # tambien entra al siguiente commit, y antes se colaba sin revisar (paso de verdad).
    $archivos = @(git ls-files) + @(git ls-files --others --exclude-standard)
} finally {
    Pop-Location
}

# Extensiones binarias/regenerables que no tiene caso leer como texto.
$extBinaria = '\.(dll|exe|pdb|png|jpg|jpeg|gif|ico|zip|xlsx|db|bak|woff2?|ttf|eot)$'

$hallazgos = @()
foreach ($f in $archivos) {
    if ($f -match $extBinaria) { continue }
    if ($excepciones -contains $f) { continue }
    $ruta = Join-Path $raiz $f
    if (-not (Test-Path $ruta -PathType Leaf)) { continue }
    try {
        $contenido = Get-Content -LiteralPath $ruta -Raw -ErrorAction Stop
    } catch { continue }
    if ($null -eq $contenido) { continue }
    $m = [regex]::Matches($contenido, $rx, 'IgnoreCase')
    if ($m.Count -gt 0) {
        $unicos = ($m | ForEach-Object { $_.Value } | Select-Object -Unique) -join ', '
        $hallazgos += "  - ${f}: $unicos"
    }
}

if ($hallazgos.Count -gt 0) {
    Write-Host ""
    Write-Host "TERMINOS PROHIBIDOS ENCONTRADOS (no commitear/publicar asi):" -ForegroundColor Red
    $hallazgos | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host ""
    Write-Host "Ver AGENTS.md #5 y .terminos_prohibidos.local." -ForegroundColor Yellow
    exit 1
}

Write-Host "OK: ningun archivo trackeado menciona un termino de .terminos_prohibidos.local." -ForegroundColor Green
exit 0
