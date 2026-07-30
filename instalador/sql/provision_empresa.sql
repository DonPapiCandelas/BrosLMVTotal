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
    ModificadoPor INT           NULL,
    HashSHA256    CHAR(64)      NULL,
    AprobadoPor   INT           NULL,
    AprobadoEl    DATETIME      NULL,
    Categoria     NVARCHAR(100) NULL);

-- Migracion idempotente para empresas ya provisionadas ANTES de v2.35.0 (integridad
-- de scripts, T2.3): la tabla ya existe con el esquema viejo, hay que agregar las
-- columnas nuevas si faltan. Mismo patron que el fix de IconFile de abajo.
IF COL_LENGTH('dbo.zzBrosScript','HashSHA256') IS NULL
    ALTER TABLE dbo.zzBrosScript ADD HashSHA256 CHAR(64) NULL;
IF COL_LENGTH('dbo.zzBrosScript','AprobadoPor') IS NULL
    ALTER TABLE dbo.zzBrosScript ADD AprobadoPor INT NULL;
IF COL_LENGTH('dbo.zzBrosScript','AprobadoEl') IS NULL
    ALTER TABLE dbo.zzBrosScript ADD AprobadoEl DATETIME NULL;
-- v2.39.0: Categoria (texto libre, clasificacion manual -- se probo por modulo de Comercial
-- y se descarto, no reflejaba como el usuario organiza sus botones en la practica).
IF COL_LENGTH('dbo.zzBrosScript','Categoria') IS NULL
    ALTER TABLE dbo.zzBrosScript ADD Categoria NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='zzBrosScriptHist')
CREATE TABLE dbo.zzBrosScriptHist(
    id       INT IDENTITY(1,1) PRIMARY KEY,
    AppKey   NVARCHAR(80)  NULL,
    Codigo   NVARCHAR(MAX) NULL,
    Fecha    DATETIME      NULL,
    Usuario  INT           NULL,
    Etiqueta NVARCHAR(200) NULL);

-- v2.40.0: Etiqueta (T1.4, historial de versiones) -- nota libre por version, protege esa
-- version de la purga manual sin importar la antiguedad.
IF COL_LENGTH('dbo.zzBrosScriptHist','Etiqueta') IS NULL
    ALTER TABLE dbo.zzBrosScriptHist ADD Etiqueta NVARCHAR(200) NULL;

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
-- 2a) Pestaña propia "Soluciones LMV" (separada de General).
--     INSERT adaptable: solo columnas que existan en engRibbonTab.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM engRibbonTab WHERE TabCaption='Soluciones LMV')
BEGIN
    DECLARE @ct NVARCHAR(MAX), @vt NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('RibbonTabIDBase','0',1),
            ('ProductID','1',2),
            ('ModuleID','0',3),
            ('TabCaption','''Soluciones LMV''',4),
            ('TabOrder','101',5),
            ('Color','0',6),
            ('ExtraMenuModuleID','0',7),
            ('ShowIfSectionModuleIDIs','0',8),
            ('ResID','0',9),
            ('IfUserIDIs','0',10)
        ) v(col,val,ord)
        WHERE col IN (SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.engRibbonTab'))
    )
    SELECT @ct = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vt = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonTab ('+@ct+') VALUES ('+@vt+')');
END
DECLARE @tab INT = (SELECT TOP 1 RibbonTabID FROM engRibbonTab WHERE TabCaption='Soluciones LMV');

-- ============================================================
-- 2b) Grupo "BrosLMV" dentro de la pestaña Soluciones LMV.
--     Si ya existía en otra pestaña (instalaciones viejas: pestaña General),
--     se MIGRA (UPDATE) en vez de duplicar. Idempotente: si ya está en
--     Soluciones LMV, el UPDATE no cambia nada.
--     INSERT adaptable: solo columnas que existan en engRibbonGroup.
-- ============================================================
IF EXISTS (SELECT 1 FROM engRibbonGroup WHERE GroupCaption='BrosLMV')
    UPDATE engRibbonGroup SET RibbonTabID=@tab WHERE GroupCaption='BrosLMV';
ELSE
BEGIN
    DECLARE @cg NVARCHAR(MAX), @vg NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('RibbonGroupIDBase','0',1),
            ('RibbonTabID',CAST(@tab AS NVARCHAR(20)),2),
            ('GroupCaption','''BrosLMV''',3),
            ('GroupOrder','0',4),
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
DECLARE @grp INT = (SELECT TOP 1 RibbonGroupID FROM engRibbonGroup WHERE GroupCaption='BrosLMV');

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
-- 4b) Botón "Gestor de Ribbon" (BrosLMV.GESTOR_RIBBON), T1.2: promovido al núcleo
--     desde un script que vivía solo en una empresa. Mismo patrón adaptable que el
--     botón de la Consola (3-4). El script en sí (instalador\scripts\GESTOR_RIBBON.py)
--     se distribuye como script COMPARTIDO (C:\BrosLMV\scripts\, no por empresa) --
--     no hace falta copiarlo a zzBrosScript aquí.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM engRibbonControl WHERE ControlExecute='BrosLMV.GESTOR_RIBBON')
BEGIN
    DECLARE @cc2 NVARCHAR(MAX), @vc2 NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('ControlIDBase','0',1),
            ('ProductID','1',2),
            ('ModuleID','0',3),
            ('ControlCaption','''Gestor de Ribbon''',4),
            ('ControlDescription','''Crear pestañas/secciones y editar o mover botones de BrosLMV, sin SQL a mano''',5),
            ('ControlExecute','''BrosLMV.GESTOR_RIBBON''',6),
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
    SELECT @cc2 = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vc2 = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonControl ('+@cc2+') VALUES ('+@vc2+')');
END
DECLARE @ctrl2 INT = (SELECT TOP 1 ControlID FROM engRibbonControl WHERE ControlExecute='BrosLMV.GESTOR_RIBBON');

IF EXISTS (SELECT 1 FROM engRibbonMenu WHERE ControlID=@ctrl2)
    UPDATE engRibbonMenu SET RibbonGroupID=@grp, ControlOrder=2 WHERE ControlID=@ctrl2;
ELSE
BEGIN
    DECLARE @cm2 NVARCHAR(MAX), @vm2 NVARCHAR(MAX);
    ;WITH m(col,val,ord) AS (
        SELECT col,val,ord FROM (VALUES
            ('RibbonMenuIDBase','0',1),
            ('RibbonGroupID',CAST(@grp AS NVARCHAR(20)),2),
            ('ControlID',CAST(@ctrl2 AS NVARCHAR(20)),3),
            ('ControlOrder','2',4),
            ('ControlType','1',5),
            ('ExtraMenuModuleID','0',6),
            ('IfUserIDIs','0',7)
        ) v(col,val,ord)
        WHERE col IN (SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.engRibbonMenu'))
    )
    SELECT @cm2 = STUFF((SELECT ','+QUOTENAME(col) FROM m ORDER BY ord FOR XML PATH('')),1,1,''),
           @vm2 = STUFF((SELECT ','+val            FROM m ORDER BY ord FOR XML PATH('')),1,1,'');
    EXEC('INSERT dbo.engRibbonMenu ('+@cm2+') VALUES ('+@vm2+')');
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
