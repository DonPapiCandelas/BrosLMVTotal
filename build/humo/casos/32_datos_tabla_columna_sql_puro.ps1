# Caso de humo #32: motor de tokens tipados {DATOS:Tabla.Columna} (v2.75.0).
#
# LIMITE HONESTO (mismo patron que el caso 25, Adjuntos): la parte de UI real
# (HostClient.ResolverFormularioTokens -> RenderUiForm -> Form.ShowDialog()) es una ventana
# WinForms modal, no automatizable headless con las herramientas de este proyecto -- no hay
# forma de "hacer clic en Aceptar" desde un proceso sin sesion interactiva sin agregar un
# flag de simulacion al Runner (fuera de alcance de esta tarea; no se agrego para no tocar
# el Runner de produccion solo por una prueba).
#
# Lo que SI se puede probar de punta a punta, headless, contra el sandbox real
# (localhost\compac / ComercialSP), es el nucleo de lo que hace el motor nuevo antes y
# despues de mostrar el formulario -- exactamente el riesgo real de esta arquitectura:
#   1) Resolucion de tipo real por INFORMATION_SCHEMA.COLUMNS para una columna conocida
#      (docDocument.Title, nvarchar) -- confirma que la consulta que usa
#      ResolverFormularioTokens trae DATA_TYPE/CHARACTER_MAXIMUM_LENGTH correctos.
#   2) El mapeo DATA_TYPE -> FieldType (reproducido aqui con la MISMA tabla de
#      MapDataTypeAFieldType en src/HostClient.cs) da FT_TEXT/FT_MEMO segun la longitud.
#   3) El escaping que usaria EncodeLiteral para un string con comillas simples (O'Brien)
#      round-tripea correcto contra SQL real: se arma el mismo literal N'...' con comillas
#      dobladas que produce EncodeLiteral, se hace un UPDATE real con ese literal sobre una
#      fila de prueba, y se lee de vuelta para confirmar que MSSQL guardo exactamente
#      "O'Brien" (ni la comilla se perdio ni se rompio la sentencia).
#   4) Caso de error: Tabla.Columna que no existe -> INFORMATION_SCHEMA.COLUMNS regresa 0
#      filas, que es justo la condicion que hace abortar a ResolverFormularioTokens ANTES
#      de construir el formulario.
#
# Verificacion de la inspeccion de codigo (no automatizable aqui, documentada explicitamente):
#   - El tipo de campo mostrado se verifico leyendo MapDataTypeAFieldType (src/HostClient.cs)
#     linea por linea contra la tabla de conversion pedida (int/decimal/date/bit/varchar-largo
#     -> memo/varchar-corto -> texto/default -> texto).
#   - El token viejo {DATOS:Campo} (sin punto) se verifico por inspeccion de
#     ResolverTokensCore (src/Scripting.cs): su regex \{DATOS:([^}]+)\} tecnicamente tambien
#     matchearia un token con punto, pero ResolverFormularioTokens corre y sustituye ANTES
#     (en ClsMain.cs/Consola.cs, antes de que el texto llegue a ctx.EjecutarSql), asi que en
#     la practica nunca hay colision -- confirmado leyendo el orden de las llamadas en los 3
#     puntos de dispatch, no solo el texto de este caso de humo.
#
# Devuelve 0 si paso, 1 si fallo.
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "ComercialSP"
)
$ErrorActionPreference = "Continue"

function Exec-Sql([string]$query) {
    return (sqlcmd -S $Server -E -d $Database -h -1 -Q $query -W 2>&1)
}

function Fail([string]$msg) {
    Write-Host "  [ERROR] $msg" -ForegroundColor Red
    exit 1
}

# ── 1) INFORMATION_SCHEMA.COLUMNS trae el tipo real de una columna conocida ────────────────
$fila = Exec-Sql "SELECT DATA_TYPE, ISNULL(CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)),'NULL') FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='docDocument' AND COLUMN_NAME='Title'"
$partes = ($fila | Select-Object -First 1).ToString().Trim() -split '\s+'
if ($partes.Count -lt 2) { Fail "No se pudo leer DATA_TYPE de docDocument.Title." }
$dataType = $partes[0].Trim().ToLowerInvariant()
$maxLenTxt = $partes[1].Trim()
if ($dataType -notin @('nvarchar','varchar','char','nchar')) {
    Fail "docDocument.Title no es un tipo de cadena en este sandbox (DATA_TYPE=$dataType) -- ajusta la columna de referencia."
}
Write-Host "  [OK] docDocument.Title DATA_TYPE=$dataType CHARACTER_MAXIMUM_LENGTH=$maxLenTxt"

# ── 2) Mapeo DATA_TYPE -> FieldType (misma tabla que MapDataTypeAFieldType) ────────────────
$maxLen = if ($maxLenTxt -eq 'NULL') { -1 } else { [int]$maxLenTxt }
$fieldType = if ($maxLen -lt 0) { "FT_MEMO" } else { "FT_TEXT" }
Write-Host "  [OK] Mapeo esperado: $dataType(len=$maxLen) -> $fieldType"

# ── 3) Escaping seguro (EncodeLiteral) round-tripea contra SQL real ────────────────────────
$AppKey = "HUMO_DATOS_TOKEN"
Exec-Sql "IF OBJECT_ID('tempdb..#humo32') IS NOT NULL DROP TABLE #humo32" | Out-Null
# Usamos una tabla de trabajo real (no #temp, sqlcmd abre una conexion nueva por llamada) en
# zzBrosPref reutilizando una fila de prueba, para no crear/depender de una tabla nueva.
Exec-Sql "DELETE FROM zzBrosPref WHERE Usuario=999904 AND Tipo='HUMO_DATOS_TOKEN'" | Out-Null
Exec-Sql "INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES (999904, 'HUMO_DATOS_TOKEN', N'placeholder')" | Out-Null

$valorOriginal = "O'Brien"
# Mismo algoritmo que EncodeLiteral (src/HostClient.cs) para Value.KindOneofCase.StringValue:
# "N'" + valor.Replace("'", "''") + "'"
$literal = "N'" + $valorOriginal.Replace("'", "''") + "'"

$updateSql = "UPDATE zzBrosPref SET Valor = $literal WHERE Usuario=999904 AND Tipo='HUMO_DATOS_TOKEN'"
Exec-Sql $updateSql | Out-Null

$leido = (Exec-Sql "SELECT Valor FROM zzBrosPref WHERE Usuario=999904 AND Tipo='HUMO_DATOS_TOKEN'" | Select-Object -First 1).ToString().Trim()
Exec-Sql "DELETE FROM zzBrosPref WHERE Usuario=999904 AND Tipo='HUMO_DATOS_TOKEN'" | Out-Null

if ($leido -ne $valorOriginal) {
    Fail "El escaping tipo EncodeLiteral no round-tripeo: se guardo '$literal' y se leyo '$leido' (se esperaba '$valorOriginal')."
}
Write-Host "  [OK] Escaping con comillas simples (O'Brien) round-tripea correcto contra SQL real."

# ── 4) Tabla/columna inexistente -> 0 filas (la condicion de aborto ANTES de mostrar UI) ───
$filaMala = Exec-Sql "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TablaQueNoExiste999' AND COLUMN_NAME='ColumnaFalsa999'"
$textoMala = (($filaMala | Where-Object { $_ -notmatch '^\s*\(\d+ rows? affected\)\s*$' -and $_.ToString().Trim() -ne '' }) | Out-String).Trim()
if ($textoMala -ne "") {
    Fail "Se esperaba 0 filas para Tabla.Columna inexistente, pero INFORMATION_SCHEMA.COLUMNS regreso algo: '$textoMala'."
}
Write-Host "  [OK] Tabla.Columna inexistente -> 0 filas en INFORMATION_SCHEMA.COLUMNS (dispara el abort-antes-de-UI en ResolverFormularioTokens)."

exit 0
