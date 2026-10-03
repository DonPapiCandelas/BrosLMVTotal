# Publica en el laboratorio (BROSLMV_DESARROLLO) las plantillas de fábrica COMO SCRIPTS (zzBrosScript), para poder editarlas y probarlas en Comercial SIN compilar ni
# generar una versión nueva del instalador: se edita el archivo de instalador\scripts, se corre este script y se vuelve a ejecutar el botón en Comercial.
# Escribe también el HashSHA256 del código, así Comercial no avisa «modificado por fuera de la Consola». AppKey = nombre del archivo sin extensión. Categoría: «Desarrollo».
# Solo escribe en el laboratorio; se niega a correr contra otra base. Al final del desarrollo, las plantillas se instalan con la versión nueva (el instalador las refresca).
# Uso:  .\build\laboratorio\publicar_scripts_lab.ps1 [-Scripts TRAZABILIDAD_DOCUMENTO,SALDOS_ESTADOS_CUENTA]     (sin -Scripts publica todas las plantillas)
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [string[]]$Scripts = @()
)
$ErrorActionPreference = "Stop"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "Esto solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
$carpeta = Join-Path $PSScriptRoot "..\..\instalador\scripts"
$archivos = Get-ChildItem $carpeta -File | Where-Object { $_.Extension -in ".ctx", ".csx", ".py", ".sql" }
$sha = [System.Security.Cryptography.SHA256]::Create()
$n = 0
foreach ($f in $archivos) {
    $texto = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $appKey = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $esNucleo = $appKey -in @("Cotizador", "ConfiguracionFormato")          # scripts de BrosLMV que ya están instalados como botones: solo se actualiza su código
    if ($texto -match '(?im)^\s*(//|#|--)\s*Plantilla\s*:\s*(.+?)\s*$') { $nombre = $Matches[2] }
    elseif ($esNucleo) { $nombre = $appKey }
    else { continue }
    if ($Scripts.Count -gt 0 -and $Scripts -notcontains $appKey) { continue }
    if ($esNucleo -and $Scripts.Count -eq 0) { continue }                  # sin -Scripts no se tocan: hay que pedirlos por nombre
    $hash = ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($texto)) | ForEach-Object { $_.ToString("x2") }) -join ""
    $codigo = $texto.Replace("'", "''"); $nombreSql = $nombre.Replace("'", "''")
    $tmp = Join-Path $env:TEMP ("pub_" + [Guid]::NewGuid().ToString('N') + ".sql")
    $sql = if ($esNucleo) { @"
UPDATE zzBrosScript SET Codigo = N'$codigo', Modificado = GETDATE(), HashSHA256 = '$hash' WHERE AppKey = '$appKey';
IF @@ROWCOUNT = 0 RAISERROR('El script $appKey no esta instalado en esta base (instala BrosLMV primero).', 16, 1);
"@ } else { @"
IF EXISTS (SELECT 1 FROM zzBrosScript WHERE AppKey = '$appKey')
    UPDATE zzBrosScript SET Nombre = N'$nombreSql', Codigo = N'$codigo', Activo = 1, Categoria = N'Desarrollo', Modificado = GETDATE(), HashSHA256 = '$hash' WHERE AppKey = '$appKey';
ELSE
    INSERT INTO zzBrosScript (AppKey, Nombre, Codigo, Modulo, Activo, Modificado, HashSHA256, Categoria) VALUES ('$appKey', N'$nombreSql', N'$codigo', 0, 1, GETDATE(), '$hash', N'Desarrollo');
"@ }
    [System.IO.File]::WriteAllText($tmp, $sql, (New-Object System.Text.UTF8Encoding($true)))
    # -x: sin sustitución de variables (el código trae «$(…)»); -b: que un error de SQL falle
    $salida = sqlcmd -S $Server -E -d $Database -x -b -i $tmp 2>&1
    $ok = $LASTEXITCODE -eq 0
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    if (-not $ok) { Write-Host "  [ERROR] $appKey : $salida" -ForegroundColor Red; exit 1 }
    Write-Host ("  {0}  ->  «{1}»" -f $appKey, $nombre)
    $n++
}
Write-Host "$n script(s) publicados en $Database (categoría Desarrollo). Ejecútalos desde la Consola de BrosLMV o desde un botón con ese AppKey."
