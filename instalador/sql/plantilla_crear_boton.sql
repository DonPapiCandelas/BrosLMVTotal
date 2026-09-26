-- plantilla_crear_boton.sql
-- Plantilla para dar de alta CUALQUIER boton de script.
-- Regla: el AppKey del boton = el nombre del archivo .csx (sin extension).
--   Archivo:  C:\BrosLMV\scripts\SUMA.csx   ->   @Execute = 'BrosLMV.SUMA'
--
-- Normalmente NO necesitas esto: con la Consola creas los .csx. Esto es solo
-- para enlazar un .csx a un boton del ribbon.

SET NOCOUNT ON;

-- ▼▼▼ EDITA ESTO ▼▼▼
DECLARE @Caption        nvarchar(200) = 'Suma Total';
DECLARE @Execute        nvarchar(200) = 'BrosLMV.SUMA';   -- = scripts\SUMA.csx
DECLARE @RibbonGroupID  int           = NULL;             -- NULL = automatico
DECLARE @Orden          int           = 100;
-- ▲▲▲ ─────────── ▲▲▲

IF @RibbonGroupID IS NULL
BEGIN
    -- Preferir el grupo donde ya viven otros botones BrosLMV; si no hay, el grupo
    -- del ribbon mas poblado (el que ya tiene mas botones).
    SELECT TOP 1 @RibbonGroupID = m.RibbonGroupID
    FROM engRibbonMenu m
    INNER JOIN engRibbonControl c ON c.ControlID = m.ControlID
    WHERE c.ControlExecute LIKE 'BrosLMV.%'
    ORDER BY m.RibbonGroupID;

    IF @RibbonGroupID IS NULL
        SELECT TOP 1 @RibbonGroupID = RibbonGroupID
        FROM engRibbonMenu GROUP BY RibbonGroupID ORDER BY COUNT(*) DESC;
END

IF EXISTS (SELECT 1 FROM engRibbonControl WHERE ControlExecute = @Execute)
BEGIN
    PRINT 'El boton ' + @Execute + ' ya existe.';
    RETURN;
END

INSERT INTO engRibbonControl
    (ControlIDBase, ProductID, ModuleID, ControlCaption, ControlDescription, ControlExecute,
     IconFile, SystemButton, SystemButtonOrder, SystemButtonBeginGroup, SystemButtonParentID,
     QuickAccessShow, QuickAccessSection, QuickAccessCaption, QuickAccessOrder, Shortcut,
     ResID, ResIDDescription, Comments, AFP)
VALUES
    (0, 1, 0, @Caption, @Caption, @Execute, NULL, 0, 0, 0, 0, 0, NULL, NULL, 0, NULL, 0, 0, NULL, NULL);

DECLARE @newCtrl int = SCOPE_IDENTITY();

INSERT INTO engRibbonMenu
    (RibbonMenuIDBase, RibbonGroupID, ControlID, ControlOrder, ControlType,
     ExtraMenuModuleID, IfFieldsExist, IfUserIDIs)
VALUES
    (0, @RibbonGroupID, @newCtrl, @Orden, 1, 0, NULL, 0);

SELECT @newCtrl AS NuevoControlID, @RibbonGroupID AS GrupoUsado;

-- Para BORRAR un boton:
--   DECLARE @cid int = (SELECT ControlID FROM engRibbonControl WHERE ControlExecute='BrosLMV.SUMA');
--   DELETE FROM engRibbonMenu    WHERE ControlID=@cid;
--   DELETE FROM engRibbonControl WHERE ControlID=@cid;
