-- Datos de demostración para ver en «Trazabilidad del documento» los lotes, series, pedimentos y conversiones de unidad, en el laboratorio BROSLMV_DESARROLLO.
-- Se agregan SOBRE los documentos «DEMO CREAR DOC» (orden de compra → recepción parcial → factura de compra): solo se escriben filas de detalle y la unidad/coeficiente de una partida
-- de la factura (no mueve inventario ni cambia importes). Idempotente: si ya existe el lote «LOTE-DEMO-A1», no hace nada. Solo para el laboratorio.
SET NOCOUNT ON;
IF DB_NAME() <> 'BROSLMV_DESARROLLO' BEGIN RAISERROR('Solo para BROSLMV_DESARROLLO', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM docDocumentLot WHERE Lot = 'LOTE-DEMO-A1') BEGIN PRINT 'YA EXISTE'; RETURN; END
DECLARE @rec BIGINT = (SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID = 184 AND Title LIKE 'DEMO CREAR DOC%' AND DeletedOn IS NULL);
DECLARE @fac BIGINT = (SELECT MAX(DocumentID) FROM docDocument WHERE ModuleID = 152 AND Title LIKE 'DEMO CREAR DOC - factura de compra%' AND DeletedOn IS NULL);
IF @rec IS NULL OR @fac IS NULL BEGIN RAISERROR('Primero corre build\humo\casos\39_crear_documento.ps1 (crea los documentos DEMO CREAR DOC)', 16, 1); RETURN; END
DECLARE @i1 BIGINT = (SELECT MIN(DocumentItemID) FROM docDocumentItem WHERE DocumentID = @rec AND DeletedOn IS NULL);
DECLARE @i2 BIGINT = (SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID = @rec AND DeletedOn IS NULL);
DECLARE @p1 INT = (SELECT ProductID FROM docDocumentItem WHERE DocumentItemID = @i1), @p2 INT = (SELECT ProductID FROM docDocumentItem WHERE DocumentItemID = @i2);
-- Dos lotes con caducidad en la primera partida de la recepción (4 piezas = 3 + 1)
INSERT INTO docDocumentLot (DocumentID, DocumentItemID, ProductID, Lot, Quantity, ExpirationDate, Unit, BaseUnit, QuantityBaseUnit, CreatedOn, CreatedBy) VALUES
  (@rec, @i1, @p1, 'LOTE-DEMO-A1', 3, '2027-06-30', 'PIEZA', 'PIEZA', 3, GETDATE(), 1),
  (@rec, @i1, @p1, 'LOTE-DEMO-A2', 1, '2027-12-31', 'PIEZA', 'PIEZA', 1, GETDATE(), 1);
-- Cinco números de serie en la segunda partida
INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, Quantity, CreatedOn, CreatedBy) VALUES
  (@rec, @i2, @p2, 'SN-DEMO-0001', 1, GETDATE(), 1), (@rec, @i2, @p2, 'SN-DEMO-0002', 1, GETDATE(), 1), (@rec, @i2, @p2, 'SN-DEMO-0003', 1, GETDATE(), 1),
  (@rec, @i2, @p2, 'SN-DEMO-0004', 1, GETDATE(), 1), (@rec, @i2, @p2, 'SN-DEMO-0005', 1, GETDATE(), 1);
-- Un pedimento para la primera partida
INSERT INTO docProductImport (OwnedBusinessEntityID, DateImport, ProductImportNumber, Title, CurrencyID, CountryID, Rate, Customs, ClaveAduana, ClavePedimento, CreatedOn, CreatedBy)
  VALUES ((SELECT OwnedBusinessEntityID FROM docDocument WHERE DocumentID = @rec), '2026-09-20', '26 47 3012 6001234', 'DEMO pedimento', 3, 1, 1, 'Veracruz', '47', 'A1', GETDATE(), 1);
INSERT INTO docDocumentProductImport (DocumentID, DocumentItemID, ProductID, DepotID, ProductImportID, Quantity, Unit, QuantityBaseUnit, BaseUnit, CreatedOn, CreatedBy)
  VALUES (@rec, @i1, @p1, 1, SCOPE_IDENTITY(), 4, 'PIEZA', 4, 'PIEZA', GETDATE(), 1);
-- Conversión de unidad: la segunda partida de la factura se factura en «CUBETA» pero el producto se maneja en otra unidad base (solo para ver el desglose en pantalla)
UPDATE docDocumentItem SET Unit = 'CAJA', CoefUnit = 12 WHERE DocumentID = @fac AND DocumentItemID = (SELECT MAX(DocumentItemID) FROM docDocumentItem WHERE DocumentID = @fac AND DeletedOn IS NULL);
PRINT 'OK: lotes, series, pedimento y conversión sembrados en los documentos ' + CAST(@rec AS VARCHAR(20)) + ' y ' + CAST(@fac AS VARCHAR(20));
