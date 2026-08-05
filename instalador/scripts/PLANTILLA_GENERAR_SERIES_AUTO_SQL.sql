-- lang: sql
-- PLANTILLA: Generación automática de números de serie — SQL puro (utilidad complementaria)
--
-- Qué hace: se corre DESPUÉS de crear un documento (Recepción de Compra, Entrada de
-- Almacén, cualquier módulo que afecte stock) para asignar números de serie a los
-- renglones de productos con orgProduct.UseSerialNumber=1 que se quedaron SIN serie —
-- el caso real de negocio "el cliente no trae sus propios números, solo quiere que se
-- autonumeren". Complementa (no reemplaza) a PLANTILLA_RECEPCION_COMPRA_SQL_PURO.sql, que
-- hoy exige listar las series a mano en @seriesCSV: deja @seriesCSV en NULL ahí y corre
-- esta plantilla después con el DocumentID resultante.
--
-- Origen: se identificó este patrón analizando un stored procedure real de un cliente
-- (`LCCREASeries`, ver entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md sección
-- "LCCREASeries"). Formato de serie idéntico al original: '<ClaveProducto>.<consecutivo
-- de 5 dígitos>' (ej. 'ABC123.00042') -- se mantiene porque no tiene ningún defecto en sí
-- mismo (es legible, ordenable, y trazable al producto); lo que sí se corrigió son los
-- DOS defectos reales del original:
--
-- 1) CONDICIÓN DE CARRERA (la razón de ser de esta plantilla). El SP original usa
--    DECLARE CURSOR ... WHILE fila por fila, calculando MAX(...)+1 UNA VEZ POR CADA
--    renglón del kardex y asumiendo que nadie más va a insertar entre el SELECT MAX y el
--    INSERT siguiente -- dos ejecuciones concurrentes (ej. dos Recepciones del mismo
--    producto corriendo casi al mismo tiempo) pueden leer el mismo MAX antes de que la
--    primera termine de insertar, generando el mismo número de serie dos veces. Esta
--    plantilla es 100% SET-BASED (un solo SELECT con ROW_NUMBER() para calcular todos los
--    consecutivos de una sola vez, sin cursor, sin WHILE) Y además serializa la sección
--    crítica completa con sp_getapplock (ver "CONCURRENCIA" abajo) -- ROW_NUMBER() por sí
--    solo NO evita la carrera entre SESIONES distintas (dos transacciones concurrentes
--    pueden cada una calcular su propio ROW_NUMBER() sobre el mismo MAX leído antes de que
--    la otra haga COMMIT); el lock es lo que de verdad cierra el hueco.
--
-- 2) MATCH DE FORMATO DEMASIADO LAXO. El original calcula el máximo existente con
--    `SerialNumber LIKE '%'+@Codigo+'%'` (contiene la clave del producto EN CUALQUIER
--    PARTE del texto) y luego `REPLACE(SerialNumber, @Codigo+'.', '')` a secas -- si existe
--    CUALQUIER serie vieja que solo contenga la clave como substring sin seguir el formato
--    exacto '<Clave>.<5 dígitos>' (dato real: en este mismo sandbox hay series con formato
--    libre tipo 'SN-HUMO-001' que no siguen el patrón), el REPLACE no limpia bien el
--    string y el CONVERT(bigint, ...) truena con error de conversión, tumbando todo el SP.
--    Esta plantilla ancla el match al formato EXACTO ('<Clave>.' seguido de 5 dígitos, ni
--    uno más ni uno menos) con LIKE + TRY_CONVERT, así que series con formato libre /
--    heredado simplemente se ignoran para el cálculo del máximo (no truenan, no cuentan).
--
-- CONCURRENCIA: el SP original no tenía ninguna protección más allá del cursor. Esta
-- plantilla toma un applock exclusivo con sp_getapplock (@Resource fijo
-- 'BrosLMV_GenerarSeriesAuto', @LockOwner='Transaction' -- se libera solo al COMMIT/
-- ROLLBACK) ANTES de leer el máximo existente, serializando TODAS las corridas
-- concurrentes de esta plantilla (no solo las del mismo producto). Se eligió un lock
-- único global -- más simple y más seguro que un lock por producto -- en vez de un lock
-- por ProductID porque esta plantilla se corre una vez por documento, justo después de
-- crearlo (no es un flujo de alto volumen concurrente); si en el futuro eso cambia, un
-- lock por ProductID (@Resource = ProductKey) permitiría paralelismo real entre productos
-- distintos a costa de más código.
--
-- Idempotencia: SÍ, es segura correr dos veces seguidas sobre el mismo documento. La
-- selección de renglones pendientes (kardex sin fila correspondiente en
-- docDocumentSerialNumber) se recalcula DESPUÉS de tomar el lock -- si ya no queda nada
-- pendiente (porque una corrida anterior ya lo resolvió, o porque otra sesión lo resolvió
-- mientras esta esperaba el lock), la plantilla NO inserta nada, NO abre una transacción
-- vacía innecesaria y regresa un mensaje 'OK: ...' (no 'ERROR: ...') -- correr esto dos
-- veces por accidente es un no-op seguro, no un error.
--
-- Atomicidad: el bloque que toma el lock, calcula los consecutivos e inserta las series
-- corre dentro de BEGIN TRY/BEGIN TRAN...COMMIT TRAN/END TRY BEGIN CATCH...ROLLBACK
-- TRAN; THROW; END CATCH -- mismo patrón que el resto de las plantillas SQL_PURO desde
-- v2.66.0. Las validaciones iniciales (documento existe, hay algo pendiente, cantidades
-- enteras) corren ANTES de abrir la transacción.
--
-- Cómo se llena (v2.77.0+): @documentoID ahora se captura con un formulario automático (token
-- tipado {DATOS:docDocument.DocumentID:*}, motor v2.75.0/2.76.0) -- correspondencia 1:1 real
-- con la columna. Ya no hace falta editar este archivo a mano: al correr el botón aparece un
-- campo numérico pidiendo el DocumentID del documento ya creado (con kardex de productos
-- UseSerialNumber=1 sin serie asignada).

-- ═══════════════════ PARÁMETRO (se captura con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @documentoID INT = {DATOS:docDocument.DocumentID:Documento ya creado (kardex sin serie pendiente):*};  -- DocumentID del documento ya creado (kardex sin serie pendiente)

-- ═══════════════════ VALIDACIONES MÍNIMAS (antes de abrir transacción) ═══════════════════
IF NOT EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID)
BEGIN SELECT 'ERROR: no existe el documento DocumentID=' + CAST(@documentoID AS VARCHAR) AS Resultado; RETURN; END

-- Renglones candidatos: kardex del documento, producto con UseSerialNumber=1, sin
-- fila viva (DeletedOn IS NULL) en docDocumentSerialNumber para ese DocumentItemID.
IF NOT EXISTS (
    SELECT 1
    FROM orgProductKardex K
    INNER JOIN orgProduct P ON P.ProductID = K.ProductID AND P.UseSerialNumber = 1 AND P.DeletedOn IS NULL
    LEFT JOIN docDocumentSerialNumber S ON S.DocumentItemID = K.DocumentItemID AND S.DeletedOn IS NULL
    WHERE K.DocumentID = @documentoID AND K.Cancelled = 0 AND K.Quantity > 0 AND S.DocumentSerialNumberID IS NULL
)
BEGIN SELECT 'OK: nada pendiente -- el documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' no tiene renglones de producto con número de serie sin asignar (o ya se generaron antes; correr esta plantilla dos veces es seguro).' AS Resultado; RETURN; END

-- Cantidades fraccionarias no tienen sentido para número de serie (una serie = una
-- unidad física) -- se valida ANTES de la transacción para no abrir/cerrar un TRAN por
-- un error que ya se sabía de antemano.
IF EXISTS (
    SELECT 1
    FROM orgProductKardex K
    INNER JOIN orgProduct P ON P.ProductID = K.ProductID AND P.UseSerialNumber = 1 AND P.DeletedOn IS NULL
    LEFT JOIN docDocumentSerialNumber S ON S.DocumentItemID = K.DocumentItemID AND S.DeletedOn IS NULL
    WHERE K.DocumentID = @documentoID AND K.Cancelled = 0 AND K.Quantity > 0 AND S.DocumentSerialNumberID IS NULL
      AND K.Quantity <> FLOOR(K.Quantity)
)
BEGIN SELECT 'ERROR: hay renglones con Quantity fraccionaria en un producto de número de serie (documento ' + CAST(@documentoID AS VARCHAR) + ') -- una serie = una unidad, no se puede autonumerar una cantidad parcial.' AS Resultado; RETURN; END

-- ═══════════════════ TRANSACCIÓN (atomicidad real + sección crítica serializada) ═══════════════════
BEGIN TRY
BEGIN TRAN;

-- ---- Lock exclusivo global de la plantilla (ver "CONCURRENCIA" arriba) ----
-- Se libera solo al COMMIT/ROLLBACK (@LockOwner='Transaction'). Si otra sesión está
-- corriendo esta misma plantilla ahora mismo, esta llamada espera hasta @LockTimeout
-- (10s) en vez de leer el MAX en paralelo -- es lo que de verdad cierra la condición de
-- carrera, ROW_NUMBER() por sí solo no basta entre transacciones distintas.
DECLARE @lockResult INT;
EXEC @lockResult = sp_getapplock @Resource = 'BrosLMV_GenerarSeriesAuto', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
IF @lockResult < 0
    RAISERROR('No se pudo obtener el lock exclusivo BrosLMV_GenerarSeriesAuto (sp_getapplock devolvió %d -- timeout o error de deadlock).', 16, 1, @lockResult);

-- ---- Renglones pendientes, recalculados DESPUÉS de tomar el lock ----
-- (idempotencia real: si otra sesión resolvió esto mientras esta esperaba el lock, aquí
-- ya no aparece nada y el INSERT de abajo simplemente no inserta filas).
DECLARE @pendiente TABLE (DepotID INT, ProductID INT, ProductKey NVARCHAR(50), DocumentItemID INT, Cantidad INT);
INSERT INTO @pendiente (DepotID, ProductID, ProductKey, DocumentItemID, Cantidad)
SELECT K.DepotID, K.ProductID, P.ProductKey, K.DocumentItemID, CONVERT(INT, K.Quantity)
FROM orgProductKardex K
INNER JOIN orgProduct P ON P.ProductID = K.ProductID AND P.UseSerialNumber = 1 AND P.DeletedOn IS NULL
LEFT JOIN docDocumentSerialNumber S ON S.DocumentItemID = K.DocumentItemID AND S.DeletedOn IS NULL
WHERE K.DocumentID = @documentoID AND K.Cancelled = 0 AND K.Quantity > 0 AND S.DocumentSerialNumberID IS NULL;

-- ---- Máximo consecutivo YA usado por producto, con match de formato EXACTO ----
-- (defecto #2 corregido: '<Clave>.' + exactamente 5 dígitos, no una substring cualquiera).
DECLARE @generadas TABLE (DocumentSerialNumberID BIGINT, DocumentItemID INT, ProductID INT, SerialNumber NVARCHAR(50));
DECLARE @maxPorProducto TABLE (ProductID INT PRIMARY KEY, MaxConsecutivo BIGINT);
INSERT INTO @maxPorProducto (ProductID, MaxConsecutivo)
SELECT t.ProductID, MAX(t.Consecutivo)
FROM (
    SELECT DISTINCT pd.ProductID,
        TRY_CONVERT(BIGINT, SUBSTRING(S.SerialNumber, LEN(pd.ProductKey) + 2, 5)) AS Consecutivo
    FROM @pendiente pd
    INNER JOIN docDocumentSerialNumber S
        ON S.ProductID = pd.ProductID AND S.DeletedOn IS NULL
        AND S.SerialNumber LIKE pd.ProductKey + '.[0-9][0-9][0-9][0-9][0-9]'
) t
WHERE t.Consecutivo IS NOT NULL
GROUP BY t.ProductID;

-- ---- Expandir cada renglón pendiente a una fila POR UNIDAD (Cantidad puede ser > 1) ----
-- usando una tabla numérica (Tally) en vez de cursor -- todavía set-based.
;WITH Tally AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM Tally WHERE n < 9999
),
Unidades AS (
    SELECT pd.DepotID, pd.ProductID, pd.ProductKey, pd.DocumentItemID,
        ROW_NUMBER() OVER (PARTITION BY pd.ProductID ORDER BY pd.DocumentItemID, t.n) AS Consecutivo
    FROM @pendiente pd
    INNER JOIN Tally t ON t.n <= pd.Cantidad
)
-- ---- INSERT final: consecutivo = MAX ya usado (0 si no había ninguno) + posición dentro del lote ----
INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, Quantity, DepotID, StatusID, CreatedOn, CreatedBy)
OUTPUT INSERTED.DocumentSerialNumberID, INSERTED.DocumentItemID, INSERTED.ProductID, INSERTED.SerialNumber INTO @generadas
SELECT
    @documentoID, u.DocumentItemID, u.ProductID,
    u.ProductKey + '.' + RIGHT('00000' + CAST(ISNULL(mp.MaxConsecutivo, 0) + u.Consecutivo AS VARCHAR(15)), 5),
    1, u.DepotID, 1, GETDATE(), 0
FROM Unidades u
LEFT JOIN @maxPorProducto mp ON mp.ProductID = u.ProductID
OPTION (MAXRECURSION 9999);

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- igual que el resto de las plantillas SQL_PURO, un fallo
-- leyendo este SELECT no debe interpretarse como que la transacción falló (ya hizo COMMIT).
SELECT DocumentSerialNumberID, DocumentItemID, ProductID, SerialNumber
FROM @generadas
ORDER BY ProductID, SerialNumber;
