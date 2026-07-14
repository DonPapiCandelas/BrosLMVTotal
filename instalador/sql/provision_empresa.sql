-- provision_empresa.sql
-- Provisiona UNA empresa (base de datos) de CONTPAQi Comercial para BrosLMV.
-- Idempotente: se puede correr varias veces sin duplicar.
-- Crea: tablas zzBros*, el grupo "BrosLMV" en la pestaña General, y el botón Consola.
-- Lo ejecuta el instalador por cada empresa seleccionada.
--
-- GENÉRICO: las tablas del ribbon (engRibbonControl, etc.) NO tienen las mismas
-- columnas en todas las versiones de Comercial (p.ej. unas tienen Comments/AFP y
-- otras no). Por eso los INSERT al ribbon se arman de forma ADAPTABLE: solo se
-- incluyen las columnas que existen en esa base. Así funciona en cualquier empresa.

SET NOCOUNT ON;

-- ============================================================
-- 1) Tablas de datos (scripts, historial, auditoría, preferencias)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosScript')
CREATE TABLE dbo.zzBrosScript(
    AppKey        NVARCHAR(80)  NOT NULL PRIMARY KEY,
    Nombre        NVARCHAR(200) NULL,
    Codigo        NVARCHAR(MAX) NULL,
    Modulo        INT           NULL,
    Activo        BIT           NOT NULL DEFAULT 1,
    Modificado    DATETIME      NULL,
    ModificadoPor INT           NULL);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosScriptHist')
CREATE TABLE dbo.zzBrosScriptHist(
    id      INT IDENTITY(1,1) PRIMARY KEY,
    AppKey  NVARCHAR(80)  NULL,
    Codigo  NVARCHAR(MAX) NULL,
    Fecha   DATETIME      NULL,
    Usuario INT           NULL);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosAuditoria')
CREATE TABLE dbo.zzBrosAuditoria(
    id         INT IDENTITY(1,1) PRIMARY KEY,
    Fecha      DATETIME      NULL,
    Usuario    INT           NULL,
    Equipo     NVARCHAR(100) NULL,
    Modulo     INT           NULL,
    AppKey     NVARCHAR(80)  NULL,
    Origen     NVARCHAR(20)  NULL,
    DuracionMs INT           NULL,
    Filas      INT           NULL,
    Estado     NVARCHAR(10)  NULL,
    Error      NVARCHAR(MAX) NULL);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosPref')
CREATE TABLE dbo.zzBrosPref(
    Usuario INT          NOT NULL,
    Tipo    NVARCHAR(20) NOT NULL,
    Valor   NVARCHAR(200) NOT NULL,
    CONSTRAINT PK_zzBrosPref PRIMARY KEY (Usuario, Tipo, Valor));

-- ============================================================
-- 2) Grupo "BrosLMV" en la pestaña General genérica (RibbonTabID = 1)
--    INSERT adaptable: solo columnas que existan en engRibbonGroup.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM engRibbonGroup WHERE GroupCaption='BrosLMV' AND RibbonTabID=1)
BEGIN
    DECLARE @cg NVARCHAR(MAX), @vg NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('RibbonGroupIDBase','0',1),
            ('RibbonTabID','1',2),
            ('GroupCaption','''BrosLMV''',3),
            ('GroupOrder','99',4),
            ('ShowOptionButton','0',5),
            ('ExtraMenuModuleID','0',6),
            ('IfUserIDIs','0',7)
        ) v(col,val,ord)
        WHERE col IN (SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.engRibbonGroup'))
    )
    SELECT @cg = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vg = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonGroup ('+@cg+') VALUES ('+@vg+')');
END
DECLARE @grp INT = (SELECT TOP 1 RibbonGroupID FROM engRibbonGroup WHERE GroupCaption='BrosLMV' AND RibbonTabID=1);

-- ============================================================
-- 3) Botón "Consola BrosLMV" (BrosLMV.CONSOLA) dentro del grupo BrosLMV
--    INSERT adaptable: solo columnas que existan en engRibbonControl
--    (así evita Comments/AFP en versiones que no las tienen).
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM engRibbonControl WHERE ControlExecute='BrosLMV.CONSOLA')
BEGIN
    DECLARE @cc NVARCHAR(MAX), @vc NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('ControlIDBase','0',1),
            ('ProductID','1',2),
            ('ModuleID','0',3),
            ('ControlCaption','''Consola BrosLMV''',4),
            ('ControlDescription','''Consola de scripts BrosLMV''',5),
            ('ControlExecute','''BrosLMV.CONSOLA''',6),
            ('IconFile','''BrosLMV.ico''',7),
            ('SystemButton','0',8),
            ('SystemButtonOrder','0',9),
            ('SystemButtonBeginGroup','0',10),
            ('SystemButtonParentID','0',11),
            ('QuickAccessShow','0',12),
            ('QuickAccessOrder','0',13),
            ('ResID','0',14),
            ('ResIDDescription','0',15)
        ) v(col,val,ord)
        WHERE col IN (SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.engRibbonControl'))
    )
    SELECT @cc = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vc = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonControl ('+@cc+') VALUES ('+@vc+')');
END

-- Asegurar el icono (por si el botón ya existía sin él)
IF COL_LENGTH('dbo.engRibbonControl','IconFile') IS NOT NULL
    UPDATE engRibbonControl SET IconFile='BrosLMV.ico' WHERE ControlExecute='BrosLMV.CONSOLA';
DECLARE @ctrl INT = (SELECT TOP 1 ControlID FROM engRibbonControl WHERE ControlExecute='BrosLMV.CONSOLA');

-- ============================================================
-- 4) Vincular el control al grupo BrosLMV (engRibbonMenu), adaptable.
-- ============================================================
IF EXISTS (SELECT 1 FROM engRibbonMenu WHERE ControlID=@ctrl)
    UPDATE engRibbonMenu SET RibbonGroupID=@grp, ControlOrder=1 WHERE ControlID=@ctrl;
ELSE
BEGIN
    DECLARE @cm NVARCHAR(MAX), @vm NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('RibbonMenuIDBase','0',1),
            ('RibbonGroupID',CAST(@grp AS NVARCHAR(20)),2),
            ('ControlID',CAST(@ctrl AS NVARCHAR(20)),3),
            ('ControlOrder','1',4),
            ('ControlType','1',5),
            ('ExtraMenuModuleID','0',6),
            ('IfUserIDIs','0',7)
        ) v(col,val,ord)
        WHERE col IN (SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.engRibbonMenu'))
    )
    SELECT @cm = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vm = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonMenu ('+@cm+') VALUES ('+@vm+')');
END

-- ============================================================
-- 5) Versión de aprovisionamiento aplicada. NO es la versión del addon (esa vive por
--    ESTACION, en C:\BrosLMV\bin, y es la misma para todas las empresas) -- esto registra
--    qué versión de ESTE SCRIPT se corrió en esta empresa, para que el instalador pueda
--    avisar "Actualizar disponible" cuando exista una versión más nueva de provisión
--    (p.ej. porque se agregó una tabla zzBros* nueva) en vez de asumir a ciegas que ya
--    está al día solo porque el botón del ribbon existe.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosInfo')
CREATE TABLE dbo.zzBrosInfo(
    Clave NVARCHAR(50)  NOT NULL PRIMARY KEY,
    Valor NVARCHAR(200) NULL);

IF EXISTS (SELECT 1 FROM zzBrosInfo WHERE Clave='ProvisionVersion')
    UPDATE zzBrosInfo SET Valor=@provisionVersion WHERE Clave='ProvisionVersion';
ELSE
    INSERT INTO zzBrosInfo (Clave, Valor) VALUES ('ProvisionVersion', @provisionVersion);

IF EXISTS (SELECT 1 FROM zzBrosInfo WHERE Clave='UltimaInstalacion')
    UPDATE zzBrosInfo SET Valor=CONVERT(NVARCHAR(30), GETDATE(), 120) WHERE Clave='UltimaInstalacion';
ELSE
    INSERT INTO zzBrosInfo (Clave, Valor) VALUES ('UltimaInstalacion', CONVERT(NVARCHAR(30), GETDATE(), 120));

SELECT @grp AS GrupoBrosLMV, @ctrl AS ControlConsola, 'Reinicia CONTPAQi' AS Nota;
