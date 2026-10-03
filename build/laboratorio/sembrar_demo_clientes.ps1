# Siembra en el laboratorio (BROSLMV_DESARROLLO) 6 clientes de demostración («DEMO CLIENTE …», con RFC, condicion de pago, descuento y limite de credito distintos)
# para probar la búsqueda de cliente de «Crear documento» y los avisos de crédito. Clona al cliente que dejó la base (Accesorios de Telefono, dado de baja).
# Idempotente: si ya existen, no crea nada. Solo escribe en el laboratorio; se niega a correr contra otra base. Para quitarlos: -Quitar (les pone DeletedOn).
param(
    [string]$Server   = "localhost\compac",
    [string]$Database = "BROSLMV_DESARROLLO",
    [switch]$Quitar
)
$ErrorActionPreference = "Stop"
if ($Database -ne "BROSLMV_DESARROLLO") { Write-Host "Esto solo corre contra BROSLMV_DESARROLLO." -ForegroundColor Red; exit 1 }
function Sql([string]$q) { sqlcmd -S $Server -E -d $Database -x -b -h -1 -W -s"|" -Q "SET NOCOUNT ON; $q" 2>&1 }

if ($Quitar) {
    Sql "UPDATE x SET DeletedOn = GETDATE() FROM orgCustomer x JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID WHERE be.OfficialName LIKE 'DEMO CLIENTE%' AND x.DeletedOn IS NULL; UPDATE orgBusinessEntity SET DeletedOn = GETDATE() WHERE OfficialName LIKE 'DEMO CLIENTE%' AND DeletedOn IS NULL" | Out-Null
    Write-Host "Clientes de demostración dados de baja."; exit 0
}
$ya = [int](Sql "SELECT COUNT(*) FROM orgBusinessEntity WHERE OfficialName LIKE 'DEMO CLIENTE%' AND DeletedOn IS NULL" | Select-Object -First 1)
if ($ya -ge 6) { Write-Host "Ya existen $ya clientes de demostración."; exit 0 }
$modelo = [long](Sql "SELECT TOP 1 BusinessEntityID FROM orgCustomer ORDER BY CustomerID" | Select-Object -First 1)
if (-not $modelo) { Write-Host "No hay un cliente modelo para clonar." -ForegroundColor Red; exit 1 }

# nombre, RFC, condicion (PaymentTermID), descuento %, limite de credito
$clientes = @(
    @('DEMO CLIENTE Ferreteria El Tornillo', 'FET010203AB1', 1, 5, 50000),
    @('DEMO CLIENTE Constructora Horizonte', 'CHO050607CD2', 2, 0, 250000),
    @('DEMO CLIENTE Abarrotes La Esperanza', 'AES080910EF3', 1, 3, 15000),
    @('DEMO CLIENTE Grupo Industrial del Bajio', 'GIB110213GH4', 2, 8, 500000),
    @('DEMO CLIENTE Distribuidora Norte', 'DNO140516IJ5', 1, 0, 0),
    @('DEMO CLIENTE Servicios Tecnicos Aguascalientes', 'STA170819KL6', 1, 2, 80000)
)
$plantilla = @"
DECLARE @modelo BIGINT = $modelo;
DECLARE @nom NVARCHAR(200) = N'{NOM}', @rfc NVARCHAR(30) = N'{RFC}', @cond INT = {COND}, @dsc FLOAT = {DSC}, @cred FLOAT = {CRED};
IF EXISTS (SELECT 1 FROM orgBusinessEntity WHERE OfficialName = @nom AND DeletedOn IS NULL) RETURN;
-- 1) entidad (clon del modelo, con otro nombre y otro GUID)
DECLARE @ov1 TABLE (n SYSNAME, e NVARCHAR(300));
INSERT @ov1 VALUES ('OfficialName','@nom'),('CommercialName','@nom'),('DeletedOn','NULL'),('DeletedBy','NULL'),('BusinessEntityGUID','CONVERT(NVARCHAR(50), NEWID())'),('BusinessEntityKey','NULL');
DECLARE @ins NVARCHAR(MAX), @sel NVARCHAR(MAX);
SELECT @ins = STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id), @sel = STRING_AGG(ISNULL(o.e, QUOTENAME(c.name)), ',') WITHIN GROUP (ORDER BY c.column_id)
FROM sys.columns c LEFT JOIN @ov1 o ON o.n = c.name WHERE c.object_id = OBJECT_ID('orgBusinessEntity') AND c.is_identity = 0 AND c.is_computed = 0 AND TYPE_NAME(c.user_type_id) <> 'timestamp';
DECLARE @nuevo BIGINT; DECLARE @sql NVARCHAR(MAX) = N'INSERT orgBusinessEntity (' + @ins + N') SELECT ' + @sel + N' FROM orgBusinessEntity WHERE BusinessEntityID = @m; SET @id = SCOPE_IDENTITY();';
EXEC sp_executesql @sql, N'@nom NVARCHAR(200), @m BIGINT, @id BIGINT OUTPUT', @nom = @nom, @m = @modelo, @id = @nuevo OUTPUT;
-- 2) datos principales (RFC)
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntityMainInfo WHERE BusinessEntityID = @nuevo)
BEGIN
    DECLARE @ov2 TABLE (n SYSNAME, e NVARCHAR(300)); INSERT @ov2 VALUES ('BusinessEntityID', CAST(@nuevo AS NVARCHAR(30))),('OfficialNumber','@rfc'),('AddressFiscalName','@nom');
    SELECT @ins = STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id), @sel = STRING_AGG(ISNULL(o.e, QUOTENAME(c.name)), ',') WITHIN GROUP (ORDER BY c.column_id)
    FROM sys.columns c LEFT JOIN @ov2 o ON o.n = c.name WHERE c.object_id = OBJECT_ID('orgBusinessEntityMainInfo') AND c.is_identity = 0 AND c.is_computed = 0 AND TYPE_NAME(c.user_type_id) <> 'timestamp';
    SET @sql = N'INSERT orgBusinessEntityMainInfo (' + @ins + N') SELECT ' + @sel + N' FROM orgBusinessEntityMainInfo WHERE BusinessEntityID = @m;';
    EXEC sp_executesql @sql, N'@nom NVARCHAR(200), @rfc NVARCHAR(30), @m BIGINT', @nom = @nom, @rfc = @rfc, @m = @modelo;
END
-- 3) cliente
DECLARE @ov3 TABLE (n SYSNAME, e NVARCHAR(300)); INSERT @ov3 VALUES ('BusinessEntityID', CAST(@nuevo AS NVARCHAR(30))),('PaymentTermID', CAST(@cond AS NVARCHAR(10))),('Discount', CAST(@dsc AS NVARCHAR(30))),('CreditLimit', CAST(@cred AS NVARCHAR(30))),('DeletedOn','NULL'),('DeletedBy','NULL');
SELECT @ins = STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id), @sel = STRING_AGG(ISNULL(o.e, QUOTENAME(c.name)), ',') WITHIN GROUP (ORDER BY c.column_id)
FROM sys.columns c LEFT JOIN @ov3 o ON o.n = c.name WHERE c.object_id = OBJECT_ID('orgCustomer') AND c.is_identity = 0 AND c.is_computed = 0 AND TYPE_NAME(c.user_type_id) <> 'timestamp';
SET @sql = N'INSERT orgCustomer (' + @ins + N') SELECT TOP 1 ' + @sel + N' FROM orgCustomer WHERE BusinessEntityID = @m;';
EXEC sp_executesql @sql, N'@m BIGINT', @m = @modelo;
"@
$n = 0
foreach ($c in $clientes) {
    $q = $plantilla.Replace('{NOM}', $c[0].Replace("'", "''")).Replace('{RFC}', $c[1]).Replace('{COND}', [string]$c[2]).Replace('{DSC}', [string]$c[3]).Replace('{CRED}', [string]$c[4])
    $tmp = Join-Path $env:TEMP ("cli_" + [Guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tmp, $q, (New-Object System.Text.UTF8Encoding($true)))
    $o = sqlcmd -S $Server -E -d $Database -x -b -i $tmp 2>&1; $ex = $LASTEXITCODE; Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    if ($ex -ne 0) { Write-Host "Falló al sembrar '$($c[0])':" -ForegroundColor Red; $o | ForEach-Object { Write-Host "  $_" }; exit 1 }
    $n++
}
Write-Host "Clientes de demostración sembrados: $n."
Sql "SELECT x.CustomerID, be.BusinessEntityID, be.OfficialName, mi.OfficialNumber, x.PaymentTermID, x.Discount, x.CreditLimit FROM orgCustomer x JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID WHERE be.OfficialName LIKE 'DEMO CLIENTE%' AND x.DeletedOn IS NULL"
exit 0
