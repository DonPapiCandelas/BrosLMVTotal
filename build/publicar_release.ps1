# BrosLMV - Copyright (C) 2026 Cristofer Candelas Garcia - GPL-3.0
#
# publicar_release.ps1 -- publica en GitHub Releases el instalador YA COMPILADO (dist\).
#
# Uso:
#   .\build\publicar_release.ps1                      # SIMULACION: hace todas las revisiones, no publica
#   .\build\publicar_release.ps1 -Publicar            # publica el addon (BrosLMV-Instalador/Desinstalador)
#   .\build\publicar_release.ps1 -Producto Descargas -Publicar
#
# Requisitos: GitHub CLI (gh) con sesion iniciada (gh auth login) y haber corrido
# generar_instalador.ps1 + generar_exes.ps1 (o generar_exes_descargas.ps1) ANTES.
#
# Revisiones (si alguna falla, NO publica):
#   1. Regla de oro (version documentada) -- solo addon.
#   2. Arbol de git limpio y HEAD ya subido a origin/main (el instalador debe corresponder al codigo publico).
#   3. Los .exe existen y no son mas viejos que los binarios de instalador\bin (evita publicar un build viejo).
#   4. Escaneo del contenido de los instaladores contra la lista LOCAL de terminos prohibidos
#      (archivo .terminos_prohibidos.local en la raiz; NO se versiona, ver .git\info\exclude).
param(
    [ValidateSet('Addon','Descargas')][string]$Producto = 'Addon',
    [switch]$Publicar,
    [switch]$SinEscaneo
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Falla($m) { Write-Host "  [X] $m" -ForegroundColor Red; exit 1 }
function Ok($m)    { Write-Host "  [OK] $m" -ForegroundColor Green }

# --- gh ---
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = 'C:\Program Files\GitHub CLI\gh.exe' }
if (-not (Test-Path $gh)) { Falla "No encuentro gh. Instala: winget install GitHub.cli" }
& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Falla "gh sin sesion. Corre: gh auth login" }
$repo = (& $gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()

Write-Host "Publicar release ($Producto) en $repo -- " -NoNewline
if ($Publicar) { Write-Host "MODO PUBLICAR" -ForegroundColor Yellow } else { Write-Host "simulacion" -ForegroundColor Cyan }

# --- version y archivos ---
if ($Producto -eq 'Addon') {
    $src = Get-Content (Join-Path $root 'src\ClsMain.cs') -Raw
    if ($src -notmatch 'AssemblyVersion\("(\d+\.\d+\.\d+)\.0"\)') { Falla "No pude leer AssemblyVersion de src\ClsMain.cs" }
    $ver = $Matches[1]
    $archivos = @("dist\BrosLMV-Instalador-$ver.exe", "dist\BrosLMV-Desinstalador-$ver.exe")
    $tag = "v$ver"
    $titulo = "BrosLMV v$ver"
    $binRef = 'instalador\bin\BrosLMVClsMain.dll'
    Write-Host "`n1) Regla de oro"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verificar_regla_de_oro.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { Falla "verificar_regla_de_oro.ps1 fallo" } else { Ok "version $ver documentada" }
} else {
    $inst = Get-ChildItem (Join-Path $root 'dist') -Filter 'BrosLMV-Descargas-Instalador-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $inst) { Falla "No hay dist\BrosLMV-Descargas-Instalador-*.exe (corre generar_exes_descargas.ps1)" }
    if ($inst.Name -notmatch 'Instalador-(\d+\.\d+\.\d+)\.exe') { Falla "No pude leer la version de $($inst.Name)" }
    $ver = $Matches[1]
    $archivos = @("dist\BrosLMV-Descargas-Instalador-$ver.exe", "dist\BrosLMV-Descargas-Desinstalador-$ver.exe")
    $tag = "descargas-v$ver"
    $titulo = "BrosLMV.Descargas v$ver"
    $binRef = $null
    Write-Host "`n1) Regla de oro: no aplica a Descargas"
}

# --- git ---
Write-Host "`n2) Git"
& git fetch origin --quiet
if (& git status --porcelain) { Falla "Hay cambios sin commitear. Haz commit y push antes de publicar." } else { Ok "arbol limpio" }
$head = (& git rev-parse HEAD).Trim(); $remoto = (& git rev-parse origin/main).Trim()
if ($head -ne $remoto) { Falla "HEAD ($($head.Substring(0,7))) no coincide con origin/main ($($remoto.Substring(0,7))). Haz push primero." } else { Ok "HEAD $($head.Substring(0,7)) = origin/main" }

# --- archivos ---
Write-Host "`n3) Archivos"
foreach ($a in $archivos) {
    $p = Join-Path $root $a
    if (-not (Test-Path $p)) { Falla "Falta $a (compila primero)" }
    $mb = [math]::Round((Get-Item $p).Length / 1MB, 1)
    Ok "$a ($mb MB)"
}
if ($binRef) {
    $tDll = (Get-Item (Join-Path $root $binRef)).LastWriteTime
    foreach ($a in $archivos) {
        if ((Get-Item (Join-Path $root $a)).LastWriteTime -lt $tDll.AddMinutes(-1)) {
            Falla "$a es MAS VIEJO que $binRef ($tDll). Recompila con generar_instalador.ps1 + generar_exes.ps1."
        }
    }
    Ok "los .exe son posteriores a la DLL empacada"
}

# --- escaneo de terminos prohibidos ---
Write-Host "`n4) Escaneo de contenido"
if ($SinEscaneo) { Write-Host "  (omitido por -SinEscaneo)" -ForegroundColor Yellow }
else {
    $lista = Join-Path $root '.terminos_prohibidos.local'
    if (-not (Test-Path $lista)) { Falla "Falta $lista (lista local de terminos prohibidos, una expresion regular por linea)." }
    $terminos = Get-Content $lista | Where-Object { $_.Trim() -and -not $_.StartsWith('#') }
    $rx = '(' + ($terminos -join '|') + ')'
    Add-Type -AssemblyName System.IO.Compression
    $extTexto = '\.(ps1|ctx|csx|sql|py|md|html?|json|txt|cs|xaml|xml|config|js|css|proto|csproj|yml|yaml|ini|cfg)$'
    $enc = [Text.Encoding]::GetEncoding(28591)
    $hallazgos = @()
    function Escanea([byte[]]$b, [string]$donde) {
        $t = $enc.GetString($b)
        $m = [regex]::Matches($t, $rx, 'IgnoreCase')
        if ($m.Count) { $script:hallazgos += ("{0}: {1}" -f $donde, (($m | ForEach-Object { $_.Value } | Select-Object -Unique) -join ', ')) }
    }
    foreach ($a in $archivos) {
        $exe = Join-Path $root $a
        try { $asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom($exe) } catch { Falla "No pude abrir $a para escanear: $($_.Exception.Message)" }
        foreach ($n in $asm.GetManifestResourceNames()) {
            $s = $asm.GetManifestResourceStream($n); $ms = New-Object IO.MemoryStream; $s.CopyTo($ms); $bytes = $ms.ToArray()
            if ($n -like '*.zip') {
                $z = New-Object IO.Compression.ZipArchive((New-Object IO.MemoryStream(,$bytes)))
                foreach ($e in $z.Entries) {
                    if ($e.Length -eq 0 -or $e.FullName -notmatch $extTexto) { continue }
                    $es = $e.Open(); $m2 = New-Object IO.MemoryStream; $es.CopyTo($m2); $es.Dispose()
                    Escanea $m2.ToArray() ("$a > $($e.FullName)")
                }
            } elseif ($n -match $extTexto -or $n -like '*.sql') { Escanea $bytes ("$a > $n") }
        }
    }
    if ($hallazgos.Count) {
        $hallazgos | ForEach-Object { Write-Host "  [!] $_" -ForegroundColor Red }
        Falla "El contenido de los instaladores menciona terminos prohibidos. Limpia el codigo fuente y recompila."
    } else { Ok "sin terminos prohibidos en el contenido de los instaladores" }
}

# --- notas y hashes ---
$notas = New-Object Collections.Generic.List[string]
if ($Producto -eq 'Addon') {
    $cl = Get-Content (Join-Path $root 'docs\CHANGELOG.md') -Raw
    $mm = [regex]::Match($cl, "(?ms)^## \[$([regex]::Escape($ver))\][^\r\n]*\r?\n(.*?)(?=^## \[)")
    if ($mm.Success) { $notas.Add($mm.Groups[1].Value.Trim()) } else { $notas.Add("Ver docs/CHANGELOG.md") }
} else {
    $notas.Add("Instalador y desinstalador de BrosLMV.Descargas v$ver. Ver descargas/DOCUMENTACION.md.")
}
$notas.Add("")
$notas.Add("### SHA-256")
foreach ($a in $archivos) { $notas.Add(('- `{0}`: `{1}`' -f (Split-Path $a -Leaf), (Get-FileHash (Join-Path $root $a) -Algorithm SHA256).Hash.ToLower())) }
$notas.Add("")
$notas.Add("> Los ejecutables no estan firmados digitalmente: Windows SmartScreen o un antivirus pueden pedir confirmacion o dar un falso positivo.")
$archNotas = Join-Path $env:TEMP "notas_release_$tag.md"
Set-Content -Path $archNotas -Value ($notas -join "`n") -Encoding UTF8

Write-Host "`n5) Publicacion"
Write-Host "  tag: $tag   titulo: $titulo   notas: $archNotas"
if (-not $Publicar) { Write-Host "`nSIMULACION terminada: todo en orden. Agrega -Publicar para subirlo." -ForegroundColor Cyan; exit 0 }

$existe = $false
& $gh release view $tag *> $null; if ($LASTEXITCODE -eq 0) { $existe = $true }
$rutas = $archivos | ForEach-Object { Join-Path $root $_ }
if ($existe) {
    Write-Host "  El release $tag ya existe: reemplazo los archivos."
    & $gh release upload $tag @rutas --clobber
    & $gh release edit $tag --notes-file $archNotas --title $titulo
} else {
    $latest = if ($Producto -eq 'Addon') { '--latest' } else { '--latest=false' }
    & $gh release create $tag @rutas --title $titulo --notes-file $archNotas --target main $latest
}
if ($LASTEXITCODE -ne 0) { Falla "gh fallo al publicar" }
Write-Host "`nPublicado: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
