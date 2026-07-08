-- 0_buscar_grupos.sql
-- DIAGNOSTICO (opcional). La instalacion normal NO necesita este script:
-- provision_empresa.sql ya crea el grupo "BrosLMV" en la pestana General y el boton.
-- Usa esto solo para inspeccionar el ribbon: lista los grupos (RibbonGroupID) que
-- existen, cuantos botones tiene cada uno, su modulo y un ejemplo, y muestra los
-- botones BrosLMV ya instalados.

SET NOCOUNT ON;

SELECT
    m.RibbonGroupID,
    COUNT(*)                       AS Botones,
    MIN(c.ModuleID)                AS ModuloEjemplo,
    MIN(c.ControlCaption)          AS BotonEjemplo
FROM engRibbonMenu m
INNER JOIN engRibbonControl c ON c.ControlID = m.ControlID
GROUP BY m.RibbonGroupID
ORDER BY Botones DESC;

-- Si ya hay botones BrosLMV instalados, aqui los ves:
SELECT ControlID, ControlCaption, ControlExecute
FROM engRibbonControl
WHERE ControlExecute LIKE 'BrosLMV.%';
