/* ============================================================================
   Motor de Asientos Contables — BrosLMV
   ----------------------------------------------------------------------------
   Infraestructura OPCIONAL: crea las tablas propias y la vista de listado.
   NO genera, modifica ni sincroniza ninguna póliza por sí sola -- eso lo hacen
   los scripts que usan scripts\motor\MotorAsientoCobro.cs.txt /
   MotorAsientoPago.cs.txt (ver docs/MOTOR_ASIENTOS_CONTABLES.md).

   Este script NO se corre automáticamente en la provisión de una empresa
   (provision_empresa.sql) -- es una capacidad avanzada que se activa por
   cliente, cuando de verdad hace falta un asiento contable que Comercial no
   puede armar solo (normalmente: proveedor/cliente con cuentas distintas por
   moneda). Correrlo no hace nada visible hasta que se den de alta asientos
   en zzBrosAsientoContable/zzBrosAsientoContablePartida y se publique un
   script que los use.

   Idempotente: se puede correr varias veces sin duplicar nada.
   ============================================================================ */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Encabezado de cada definición de asiento (una fila = una receta completa).
IF OBJECT_ID(N'dbo.zzBrosAsientoContable', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.zzBrosAsientoContable
    (
        AsientoContableID     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_zzBrosAsientoContable PRIMARY KEY,
        OwnedBusinessEntityID bigint NOT NULL,
        Nombre                nvarchar(300) NOT NULL,
        TipoPolizaID          int NULL,               -- catálogo AccountingTipoPoliza nativo (9=Ingresos, 13=Egresos, etc.)
        FuenteFecha           nvarchar(100) NOT NULL CONSTRAINT DF_zzBrosAsientoContable_FuenteFecha DEFAULT N'Fecha operacion',
        Concepto              nvarchar(510) NULL,     -- plantilla del encabezado, con variables [Campo]
        Activo                bit NOT NULL CONSTRAINT DF_zzBrosAsientoContable_Activo DEFAULT (1),
        CreatedOn             datetime2(7) NOT NULL CONSTRAINT DF_zzBrosAsientoContable_CreatedOn DEFAULT GETDATE(),
        CreatedBy             bigint NULL,
        ModifiedOn            datetime2(7) NULL,
        ModifiedBy            bigint NULL,
        DeletedOn             datetime2(7) NULL,
        DeletedBy             bigint NULL
    );
END;

-- Módulo(s) de Comercial donde aplica cada asiento (ej. 248 = Cobros Cliente, 247 = Pagos Proveedor).
IF OBJECT_ID(N'dbo.zzBrosAsientoContableModulo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.zzBrosAsientoContableModulo
    (
        AsientoContableModuloID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_zzBrosAsientoContableModulo PRIMARY KEY,
        AsientoContableID       bigint NOT NULL,
        ModuleID                bigint NOT NULL
    );
END;

-- Partidas (renglones de cargo/abono) de cada asiento. Forma final -- ver
-- docs/MOTOR_ASIENTOS_CONTABLES.md para el significado de cada columna.
IF OBJECT_ID(N'dbo.zzBrosAsientoContablePartida', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.zzBrosAsientoContablePartida
    (
        AsientoContablePartidaID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_zzBrosAsientoContablePartida PRIMARY KEY,
        AsientoContableID        bigint NOT NULL,
        Orden                    int NOT NULL,
        TipoMovimiento           nvarchar(20) NOT NULL,      -- 'Cargo' | 'Abono'
        NombrePartida            nvarchar(200) NOT NULL,
        CuentaFormula            nvarchar(300) NOT NULL,     -- literal, "[CuentaBanco]", "*1201-01-*CL", "ID:<n>"...
        FuenteImporte            nvarchar(160) NOT NULL,     -- clave del catálogo MotFuentes del motor
        Porcentaje               float NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Porcentaje DEFAULT (100),
        MonedaCondicion          nvarchar(40) NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Moneda DEFAULT N'Todas',
        PorDocumento             bit NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_PorDocumento DEFAULT (1),
        Concepto                 nvarchar(510) NULL,         -- plantilla del renglón, variables [Campo]
        Referencia               nvarchar(510) NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Referencia DEFAULT N'',
        SuprimirCero             bit NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_SuprimirCero DEFAULT (1),
        ValorAbsoluto            bit NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_ValorAbsoluto DEFAULT (0),
        Concentrar               bit NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Concentrar DEFAULT (0),
        CondicionesJson          nvarchar(max) NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Condiciones DEFAULT N'[]',
        Activo                   bit NOT NULL CONSTRAINT DF_zzBrosAsientoContablePartida_Activo DEFAULT (1)
    );
END;

-- Huellas de idempotencia: evita reconstruir una póliza si nada cambió, y protege
-- las pólizas ya sincronizadas a Contabilidad (ver docs/MOTOR_ASIENTOS_CONTABLES.md).
-- Tablas independientes por tipo de operación (cobro/pago) -- nunca compartir una sola.
IF OBJECT_ID(N'dbo.zzBrosPolizaCobroEstado', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.zzBrosPolizaCobroEstado
    (
        FinancialOperationID bigint NOT NULL CONSTRAINT PK_zzBrosPolizaCobroEstado PRIMARY KEY,
        PolizaID             bigint NOT NULL,
        InputHash            char(64) NOT NULL,
        OutputHash           char(64) NOT NULL,
        UpdatedOn            datetime2(3) NOT NULL CONSTRAINT DF_zzBrosPolizaCobroEstado_UpdatedOn DEFAULT SYSDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.zzBrosPolizaPagoEstado', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.zzBrosPolizaPagoEstado
    (
        FinancialOperationID bigint NOT NULL CONSTRAINT PK_zzBrosPolizaPagoEstado PRIMARY KEY,
        PolizaID             bigint NOT NULL,
        InputHash            char(64) NOT NULL,
        OutputHash           char(64) NOT NULL,
        UpdatedOn            datetime2(3) NOT NULL CONSTRAINT DF_zzBrosPolizaPagoEstado_UpdatedOn DEFAULT SYSDATETIME()
    );
END;

-- Vista de listado para una pantalla de administración de asientos.
EXEC(N'
CREATE OR ALTER VIEW dbo.vwBrosAsientosContablesList
AS
WITH Modulos AS
(
    SELECT am.AsientoContableID,
           STRING_AGG(CONCAT(CONVERT(nvarchar(20), am.ModuleID), N'' - '', em.ModuleName), N'', '')
             WITHIN GROUP (ORDER BY em.ModuleName) AS Modulos
    FROM dbo.zzBrosAsientoContableModulo am
    LEFT JOIN dbo.engModule em ON em.ModuleID = am.ModuleID
    GROUP BY am.AsientoContableID
),
Partidas AS
(
    SELECT ap.AsientoContableID,
           COUNT(*) AS TotalPartidas,
           SUM(CASE WHEN ap.PorDocumento = 1 THEN 1 ELSE 0 END) AS PartidasPorDocumento
    FROM dbo.zzBrosAsientoContablePartida ap
    WHERE ap.Activo = 1
    GROUP BY ap.AsientoContableID
)
SELECT a.AsientoContableID, a.OwnedBusinessEntityID, a.Nombre,
       a.TipoPolizaID AS TipoPoliza, a.FuenteFecha, a.Concepto,
       ISNULL(m.Modulos, N'''') AS Modulos,
       ISNULL(p.TotalPartidas, 0) AS Partidas,
       ISNULL(p.PartidasPorDocumento, 0) AS PartidasPorDocumento,
       a.Activo,
       CASE WHEN a.DeletedOn IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END AS Deleted,
       a.CreatedOn, a.ModifiedOn
FROM dbo.zzBrosAsientoContable a
LEFT JOIN Modulos m ON m.AsientoContableID = a.AsientoContableID
LEFT JOIN Partidas p ON p.AsientoContableID = a.AsientoContableID;
');

COMMIT TRANSACTION;
