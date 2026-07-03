-- desprovision_empresa.sql
-- Quita BrosLMV de UNA empresa (base de datos) de CONTPAQi Comercial.
-- Elimina el botón del ribbon, el grupo si queda vacío, y las tablas zzBros*.
-- Idempotente: se puede correr varias veces sin error.

SET NOCOUNT ON;

-- 1) Quitar el vínculo del control en el menú y el control "Consola BrosLMV"
DELETE FROM engRibbonMenu
 WHERE ControlID IN (SELECT ControlID FROM engRibbonControl WHERE ControlExecute LIKE 'BrosLMV.%');
DELETE FROM engRibbonControl WHERE ControlExecute LIKE 'BrosLMV.%';

-- 2) Quitar el grupo "BrosLMV" de la pestaña General si ya no tiene controles
DELETE g
  FROM engRibbonGroup g
 WHERE g.GroupCaption = 'BrosLMV' AND g.RibbonTabID = 1
   AND NOT EXISTS (SELECT 1 FROM engRibbonMenu m WHERE m.RibbonGroupID = g.RibbonGroupID);

-- 3) Quitar las tablas de datos de BrosLMV (scripts, historial, auditoría, prefs)
IF OBJECT_ID('dbo.zzBrosScript')     IS NOT NULL DROP TABLE dbo.zzBrosScript;
IF OBJECT_ID('dbo.zzBrosScriptHist') IS NOT NULL DROP TABLE dbo.zzBrosScriptHist;
IF OBJECT_ID('dbo.zzBrosAuditoria')  IS NOT NULL DROP TABLE dbo.zzBrosAuditoria;
IF OBJECT_ID('dbo.zzBrosPref')       IS NOT NULL DROP TABLE dbo.zzBrosPref;

SELECT 'BrosLMV quitado de esta empresa. Reinicia CONTPAQi.' AS Nota;
