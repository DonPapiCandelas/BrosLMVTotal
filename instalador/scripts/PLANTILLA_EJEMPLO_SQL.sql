-- lang: sql
-- BrosLMV - Script de EJEMPLO tipo SQL (T-SQL crudo, GPL-3.0)
-- Corre por la conexion viva de CONTPAQi, contra la empresa activa (BD ComercialSP).
-- Si es un SELECT/SP muestra las filas; si es DML muestra "ejecutado".
--
-- Tokens disponibles (se sustituyen antes de correr):
--   {pID}      primer documento seleccionado     {pIDs}    todos los seleccionados
--   {pUserID}  usuario             {pModulo}  modulo activo   {pEmpresa} BD activa
--   {DATOS:Campo}  campo de la fila seleccionada
--
-- Esquema real: tablas con prefijos doc*/org*/eng* (documentos/catálogos/motor).
-- Recuerda filtrar borrados: WHERE DeletedOn IS NULL

SELECT TOP 20 DocumentID, FolioPrefix, Folio, Total, StatusPaidID
FROM docDocument
WHERE DeletedOn IS NULL
ORDER BY DocumentID DESC
