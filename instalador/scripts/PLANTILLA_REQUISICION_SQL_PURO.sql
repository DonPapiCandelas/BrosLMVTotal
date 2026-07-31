-- lang: sql
-- PLANTILLA: Requisición de Compra — SQL puro (INSERT directo, sin ctx.erp)
--
-- Qué hace: crea una Solicitud de Compra real (módulo 1040) insertando directamente en las
-- tablas de Comercial, SIN pasar por ctx.erp.NuevoDocumento/AgregarArticulo. Mismo
-- resultado final que las versiones Forms/WebView2 -- confirmado comparando campo por
-- campo contra un documento creado por el camino nativo (ctx.erp), no solo "se ve bien".
--
-- ⚠️ ADVERTENCIA REAL (no la ignores): esta plantilla existe para mostrar que SÍ se puede
-- hacer con SQL puro, pero es la forma MÁS FRÁGIL de crear un documento. ctx.erp.
-- NuevoDocumento() ya resuelve, de forma probada, las 4 "anclas" que un documento necesita
-- (docDocument, docDocumentExtra, docDocumentCFD, docDocumentPaymentAgenda) -- aquí se
-- replican A MANO. Si Comercial cambia de versión y agrega una columna nueva con una regla
-- de negocio distinta, esta plantilla puede quedar desactualizada SIN AVISAR (no truena,
-- simplemente el documento queda incompleto). La investigación previa del proyecto sobre
-- esto (docs/REQUISICION_SOLICITUD_COMPRA.md) ya encontró este riesgo -- por eso el
-- estándar general del proyecto es "usa ctx.erp primero, SQL solo para lo que no cubre".
-- Usa esta plantilla cuando sepas exactamente por qué la necesitas (por ejemplo, para
-- carga masiva desde SQL puro sin abrir Comercial), no como default.
--
-- Cómo se llenan los datos: reemplaza los valores de la sección "PARÁMETROS" de abajo
-- antes de correr -- @proveedorBE, @almacen, @productoID, @cantidad. Esta plantilla NO
-- tiene interfaz -- es la diferencia real contra las versiones Forms/WebView2 (que sí
-- capturan datos con una ventana). Con SQL puro no hay forma de mostrar una ventana; para
-- eso existen las otras 4 variantes de esta misma plantilla.
--
-- Folio: se calcula como MAX(Folio)+1 para el módulo 1040 -- mismo criterio que usa
-- ctx.erp.GetNextFolio() internamente (consecutivo simple, sin prefijo para este módulo).
-- No es seguro contra 2 personas creando un documento en el MISMO instante (condición de
-- carrera real) -- ctx.erp SÍ maneja eso internamente; SQL puro no.

-- ═══════════════════ PARÁMETROS (edítalos antes de correr) ═══════════════════
DECLARE @proveedorBE INT = 2;      -- BusinessEntityID del proveedor (orgBusinessEntity)
DECLARE @almacen     INT = 1;      -- DepotID (orgDepot)
DECLARE @productoID  INT = 1;      -- ProductID (orgProduct)
DECLARE @cantidad    DECIMAL(18,4) = 3;

-- ═══════════════════ VALIDACIONES MÍNIMAS ═══════════════════
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntity WHERE BusinessEntityID = @proveedorBE)
BEGIN
    SELECT 'ERROR: no existe el proveedor BusinessEntityID=' + CAST(@proveedorBE AS VARCHAR) AS Resultado;
    RETURN;
END
IF NOT EXISTS (SELECT 1 FROM orgProduct WHERE ProductID = @productoID)
BEGIN
    SELECT 'ERROR: no existe el producto ProductID=' + CAST(@productoID AS VARCHAR) AS Resultado;
    RETURN;
END

-- ═══════════════════ FOLIO ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 1040), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();

-- ═══════════════════ 1. docDocument (cabecera) ═══════════════════
-- Perfil validado en docs/REQUISICION_SOLICITUD_COMPRA.md: DepotIDFrom=0, UserID=0,
-- PaymentTermID=0, CampaignID/CostCenterID/ProjectID=0 (no NULL -- confirmado en el
-- snapshot real: esta versión de Comercial usa 0, no NULL, en estos 3 campos).
DECLARE @doc TABLE (DocumentID INT);
-- FolioPrefix='' (no NULL) y TotalLetter='CERO PESOS 00/100 M.N.' (no NULL) son las 2
-- diferencias reales que se encontraron comparando contra un documento nativo -- sin
-- ellas, RecalcCompleto/Save (que NuevoDocumento SÍ dispara internamente) las hubiera
-- llenado solas; SQL puro no las calcula, hay que ponerlas a mano. TotalLetter es
-- literalmente "el total en letras" -- para Requisición SIEMPRE es $0 (no captura precio),
-- así que este valor fijo es correcto aquí. Si algún día esta plantilla se adapta a un
-- documento con importes reales, TotalLetter tendría que calcularse (no hay una función de
-- SQL puro para "número a letras" en este proyecto todavía).
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized,
    ExportID, TotalLetter, CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    1040, 49, @proveedorBE, 1, @proveedorBE,
    @almacen, 0, '', CAST(@folio AS NVARCHAR), @ahora, @ahora, @ahora, @ahora, @ahora,
    3, 3, 1, 0, @ahora, 1,
    1, 'CERO PESOS 00/100 M.N.', @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
-- docDocumentExt NO aplica en esta version de Comercial (la tabla no tiene columna
-- DocumentID -- confirmado revisando sys.columns, es una tabla legacy sin relación con
-- documentos en este esquema). Si tu version de Comercial SÍ la usa, agrégala aquí.
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
INSERT INTO docDocumentPaymentAgenda (
    DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy
)
VALUES (@docId, @ahora, 100, 0, 1, @ahora, 0);

-- ═══════════════════ 3. Partida ═══════════════════
-- ClaveUnidad se copia del producto. ObjetoImpuesto se deja '' a propósito (NO se copia
-- del producto, aunque el producto sí tenga uno) -- comparado campo por campo contra un
-- documento nativo: AgregarArticulo deja ObjetoImpuesto vacío en una Solicitud de Compra
-- (no genera CFDI, ese campo es fiscal y no aplica aquí) aunque el producto tenga '02'.
-- CoefUnit=1 (coeficiente de conversión de unidad, 1 = sin conversión) también hay que
-- ponerlo a mano -- sin esto queda en 0, que rompe cualquier cálculo que dependa de él.
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, LineNumber, MustBeDelivered, ApplyGlobalDiscount,
    DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
)
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', ISNULL(p.TaxTypeID, 0), 1, 1, 1,
    1, 1, 1, @ahora
FROM orgProduct p WHERE p.ProductID = @productoID;

SELECT 'Requisición (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) AS Resultado;
