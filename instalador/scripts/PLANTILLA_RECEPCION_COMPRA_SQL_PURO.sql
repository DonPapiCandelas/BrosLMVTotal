-- lang: sql
-- PLANTILLA: Recepción de Compra — SQL puro (INSERT directo, sin ctx.erp)
--
-- Qué hace: crea una Recepción de Compra real (módulo 184) DERIVADA de una Orden de Compra
-- ya existente, insertando directamente en las tablas de Comercial, SIN pasar por ctx.erp.
-- Confirmado campo por campo contra un documento nativo real creado con ctx.erp en ESTE
-- sandbox (no solo contra capturas de otro entorno -- ver advertencia de
-- PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql sobre por qué eso importa).
--
-- ⚠️ ADVERTENCIA REAL: esta plantilla recibe UNA sola partida de UNA sola Orden de Compra
-- por corrida (igual que las primeras versiones de PLANTILLA_REQUISICION_SQL_PURO.sql y
-- PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql, que también empezaron así) -- para consolidar VARIAS
-- Órdenes de Compra en una sola Recepción (el caso real de negocio, documentado en
-- MANUAL.md §10.4), usa PLANTILLA_RECEPCION_COMPRA_FORMS_CSHARP.ctx.
--
-- Diferencia real frente a Orden de Compra: la Recepción SÍ mueve inventario de verdad
-- (kardex con Quantity=CANTIDAD RECIBIDA, no 0 como la OC) y usa una columna de vínculo
-- propia por partida (DeliverDocumentItemID -> apunta a la partida de la OC que se está
-- recibiendo) -- no existe una columna equivalente a nivel encabezado más que
-- SourceDocumentID (solo informativo, no lo usa el cálculo real).
--
-- Costo = Precio unitario en documentos de compra (confirmado contra el sandbox):
-- CostPrice en docDocumentItem SÍ lleva el precio unitario aquí (a diferencia de la Orden
-- de Compra, donde CostPrice se queda en 0 y el costo vive solo en el kardex).
--
-- orgProductKardex.QuantityToBeDelivered queda NEGATIVO (-cantidad), no positivo como en la
-- OC -- reduce el pendiente por recibir, no lo registra como pendiente nuevo. Este es un
-- detalle NO obvio que se hubiera adivinado mal por analogía con la Orden de Compra; se
-- confirmó comparando contra el documento nativo real.
--
-- Lote / número de serie: usa @productoUsaLote o @productoUsaSerie según el producto (mira
-- orgProduct.UseLot / UseSerialNumber antes de correr) -- estas tablas van SIEMPRE ligadas
-- al DocumentItemID de ESTA Recepción (docDocumentLot.DocumentItemID / docDocumentSerialNumber
-- .DocumentItemID), no al de la OC origen.
--
-- Cómo se llenan los datos: reemplaza los valores de "PARÁMETROS" antes de correr. Deja
-- @lote/@caducidad en NULL si el producto no usa lote; deja @seriesCSV en NULL/vacío si no
-- usa número de serie (nunca los dos a la vez en el mismo producto).

-- ═══════════════════ PARÁMETROS (edítalos antes de correr) ═══════════════════
DECLARE @sourceOC        INT = 171;   -- DocumentID de la Orden de Compra que se está recibiendo
DECLARE @sourceItemId    INT = 172;   -- DocumentItemID de la partida de esa OC (columna DeliverDocumentItemID)
DECLARE @almacen         INT = 1;
DECLARE @productoID      INT = 2;
DECLARE @cantidad        DECIMAL(18,4) = 7;    -- cuánto se recibe AHORA (puede ser parcial vs. la OC)
DECLARE @precio          DECIMAL(18,2) = 250;  -- mismo precio unitario de la OC (costo = precio aquí)
-- Lote (deja NULL ambos si el producto no usa lote -- orgProduct.UseLot=0):
DECLARE @lote            NVARCHAR(50) = 'LOTE-HUMO-01';
DECLARE @caducidad       DATE = '2027-12-31';
-- Número de serie (deja NULL/vacío si el producto no usa serie -- orgProduct.UseSerialNumber=0;
-- una fila por serie, @cantidad debe ser igual al número de series listadas). Ejemplo con
-- un producto de número de serie: @productoID=3, @cantidad=5, @lote/@caducidad=NULL,
-- @seriesCSV='SN-001,SN-002,SN-003,SN-004,SN-005' -- ambas rutas (lote y serie) probadas
-- contra el sandbox real, ver EXP-DOC-recepcion_compra_001 y advertencia al inicio.
DECLARE @seriesCSV       NVARCHAR(500) = NULL; -- ej: 'SN-001,SN-002,SN-003'

-- ═══════════════════ VALIDACIONES MÍNIMAS ═══════════════════
DECLARE @proveedorBE INT, @almacenOC INT;
SELECT @proveedorBE = BusinessEntityID, @almacenOC = DepotID FROM docDocument WHERE DocumentID = @sourceOC AND ModuleID = 183;
IF @proveedorBE IS NULL
BEGIN SELECT 'ERROR: no existe la Orden de Compra DocumentID=' + CAST(@sourceOC AS VARCHAR) + ' (o no es modulo 183)' AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM docDocumentItem WHERE DocumentItemID = @sourceItemId AND DocumentID = @sourceOC)
BEGIN SELECT 'ERROR: la partida DocumentItemID=' + CAST(@sourceItemId AS VARCHAR) + ' no pertenece a la OC ' + CAST(@sourceOC AS VARCHAR) AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM orgProduct WHERE ProductID = @productoID)
BEGIN SELECT 'ERROR: no existe el producto ProductID=' + CAST(@productoID AS VARCHAR) AS Resultado; RETURN; END

-- ═══════════════════ CÁLCULOS ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 184), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
DECLARE @tasaIva DECIMAL(9,4) = 0.16;
DECLARE @subtotal DECIMAL(18,2) = @cantidad * @precio;
DECLARE @iva DECIMAL(18,2) = ROUND(@subtotal * @tasaIva, 2);
DECLARE @total DECIMAL(18,2) = @subtotal + @iva;
-- TotalCost = @subtotal (costo = precio unitario en documentos de compra -- confirmado
-- contra el sandbox: a diferencia de la OC, la Recepción SÍ llena esta columna).
DECLARE @totalCost DECIMAL(18,2) = @subtotal;

-- ---- Número a letras (mismo algoritmo ya probado en PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql) ----
DECLARE @entero BIGINT = FLOOR(@total);
DECLARE @centavos INT = ROUND((@total - @entero) * 100, 0);
DECLARE @miles BIGINT = (@entero / 1000) % 1000;
DECLARE @resto BIGINT = @entero % 1000;
DECLARE @millones BIGINT = @entero / 1000000;

DECLARE @centenas TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @centenas VALUES (0,''),(1,'CIENTO'),(2,'DOSCIENTOS'),(3,'TRESCIENTOS'),(4,'CUATROCIENTOS'),(5,'QUINIENTOS'),(6,'SEISCIENTOS'),(7,'SETECIENTOS'),(8,'OCHOCIENTOS'),(9,'NOVECIENTOS');
DECLARE @unidades TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @unidades VALUES (0,''),(1,'UNO'),(2,'DOS'),(3,'TRES'),(4,'CUATRO'),(5,'CINCO'),(6,'SEIS'),(7,'SIETE'),(8,'OCHO'),(9,'NUEVE'),(10,'DIEZ'),
(11,'ONCE'),(12,'DOCE'),(13,'TRECE'),(14,'CATORCE'),(15,'QUINCE'),(16,'DIECISEIS'),(17,'DIECISIETE'),(18,'DIECIOCHO'),(19,'DIECINUEVE'),(20,'VEINTE');
DECLARE @decenas TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @decenas VALUES (2,'VEINTE'),(3,'TREINTA'),(4,'CUARENTA'),(5,'CINCUENTA'),(6,'SESENTA'),(7,'SETENTA'),(8,'OCHENTA'),(9,'NOVENTA');
DECLARE @veinti TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @veinti VALUES (1,'VEINTIUNO'),(2,'VEINTIDOS'),(3,'VEINTITRES'),(4,'VEINTICUATRO'),(5,'VEINTICINCO'),(6,'VEINTISEIS'),(7,'VEINTISIETE'),(8,'VEINTIOCHO'),(9,'VEINTINUEVE');

DECLARE @gMil NVARCHAR(60) = '';
IF @miles > 0
BEGIN
    DECLARE @mc INT = @miles / 100, @mr INT = @miles % 100;
    IF @mc > 0 SET @gMil = @gMil + (SELECT t FROM @centenas WHERE n=@mc) + ' ';
    IF @miles = 100 SET @gMil = 'CIEN ';
    IF @mr > 0
    BEGIN
        IF @mr <= 20 SET @gMil = @gMil + (SELECT t FROM @unidades WHERE n=@mr);
        ELSE BEGIN
            DECLARE @md INT = @mr / 10, @mu INT = @mr % 10;
            IF @md = 2 AND @mu > 0 SET @gMil = @gMil + (SELECT t FROM @veinti WHERE n=@mu);
            ELSE BEGIN
                SET @gMil = @gMil + (SELECT t FROM @decenas WHERE n=@md);
                IF @mu > 0 SET @gMil = @gMil + ' Y ' + (SELECT t FROM @unidades WHERE n=@mu);
            END
        END
    END
END

DECLARE @gResto NVARCHAR(60) = '';
IF @resto > 0
BEGIN
    DECLARE @rc INT = @resto / 100, @rr INT = @resto % 100;
    IF @rc > 0 SET @gResto = @gResto + (SELECT t FROM @centenas WHERE n=@rc) + ' ';
    IF @resto = 100 SET @gResto = 'CIEN ';
    IF @rr > 0
    BEGIN
        IF @rr <= 20 SET @gResto = @gResto + (SELECT t FROM @unidades WHERE n=@rr);
        ELSE BEGIN
            DECLARE @rd INT = @rr / 10, @ru INT = @rr % 10;
            IF @rd = 2 AND @ru > 0 SET @gResto = @gResto + (SELECT t FROM @veinti WHERE n=@ru);
            ELSE BEGIN
                SET @gResto = @gResto + (SELECT t FROM @decenas WHERE n=@rd);
                IF @ru > 0 SET @gResto = @gResto + ' Y ' + (SELECT t FROM @unidades WHERE n=@ru);
            END
        END
    END
END

DECLARE @totalLetra NVARCHAR(200) = '';
IF @entero = 0 SET @totalLetra = 'CERO';
ELSE BEGIN
    IF @millones = 1 SET @totalLetra = @totalLetra + 'UN MILLON ';
    IF @millones > 1 SET @totalLetra = @totalLetra + CAST(@millones AS NVARCHAR) + ' MILLONES ';
    IF @miles = 1 SET @totalLetra = @totalLetra + 'MIL ';
    IF @miles > 1 SET @totalLetra = @totalLetra + LTRIM(RTRIM(@gMil)) + ' MIL ';
    IF @resto > 0 SET @totalLetra = @totalLetra + LTRIM(RTRIM(@gResto));
END
SET @totalLetra = LTRIM(RTRIM(@totalLetra));
IF RIGHT(@totalLetra, 3) = 'UNO' SET @totalLetra = LEFT(@totalLetra, LEN(@totalLetra) - 3) + 'UN';
SET @totalLetra = @totalLetra + ' PESOS ' + RIGHT('0' + CAST(@centavos AS NVARCHAR), 2) + '/100 M.N.';

-- ═══════════════════ 1. docDocument (cabecera) ═══════════════════
-- Perfil de Recepción (distinto de OC): DocumentTypeID=3 (no 40), PaymentTermID=0 (no la
-- condición real -- confirmado, a diferencia de OC/Factura que SÍ la llevan),
-- StatusDeliveryID=3 y StatusPaidID=3 (mismo default que el resto del catálogo tras llamar
-- UpdateStatusDelivery -- ver advertencia sobre esto en PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql,
-- el mismo hallazgo aplica aquí). SourceDocumentID=@sourceOC (informativo, no lo usa el
-- cálculo real de pendientes -- MANUAL.md §10.4).
DECLARE @doc TABLE (DocumentID INT);
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, SourceDocumentID, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
    SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalCost, TotalLetter, TotalPaid, Balance, StatusPaidID, StatusDeliveryID,
    CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    184, 3, 2, 1, @proveedorBE,
    @almacen, 0, @sourceOC, '', CAST(@folio AS NVARCHAR), @ahora, @ahora, @ahora, @ahora, @ahora, @ahora,
    3, 3, 1, 0, @ahora, 1, 1,
    @subtotal, @subtotal, @total, @iva, @totalCost, @totalLetra, 0, @total, 3, 3,
    @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 1, @ahora, 0);

-- ═══════════════════ 3. Partida ═══════════════════
-- CostPrice = @precio (a diferencia de la OC, donde se queda en 0) -- confirmado contra el
-- sandbox: "costo = precio unitario" es la regla real en documentos de compra que SÍ mueven
-- inventario. DeliverDocumentItemID = @sourceItemId (vínculo a la partida de la OC).
DECLARE @item TABLE (DocumentItemID INT);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered,
    ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DeliverDocumentItemID, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', 5, @tasaIva, @precio, @precio, @subtotal, 1, 1,
    1, 1, 1, 1, @sourceItemId, @ahora
FROM orgProduct p WHERE p.ProductID = @productoID;
DECLARE @itemId INT = (SELECT TOP 1 DocumentItemID FROM @item);

-- ═══════════════════ 3b. Relación producto-proveedor ═══════════════════
DECLARE @supplierID INT = (SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID = @proveedorBE);
IF @supplierID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID = @productoID AND SupplierID = @supplierID)
    INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber)
    VALUES (@productoID, @supplierID, @precio, 3, 0);

-- ═══════════════════ 4. Impuestos ═══════════════════
INSERT INTO docDocumentTax (DocumentID, IVA_T, IVA_R, ISR_R, IEPS_T, IEPS_R, Otro, Local_T, Local_R)
VALUES (@docId, @iva, 0, 0, 0, 0, 0, 0, 0);
INSERT INTO docDocumentTaxDetail (DocumentID, DocumentItemID, TaxTypeID, TaxItemID, Amount, Retention, IVASobreIEPS, RegionalTaxID, TaxName, TaxTypeName, TaxBase, TaxPerc, TipoFactor)
VALUES (@docId, @itemId, 5, 6, @iva, 0, 0, 0, 'IVA 16.00%', 'IVA', @subtotal, @tasaIva, 'Tasa');
INSERT INTO docDocumentTaxSum (DocumentID, TaxName1, TaxAmount1, TotalFederal, TotalLocal, TotalOtro)
VALUES (@docId, 'IVA 16%', @iva, @iva, 0, 0);

-- ═══════════════════ 5. Kardex REAL (a diferencia de la OC, Quantity aquí SÍ mueve) ═══════════════════
-- QuantityToBeDelivered queda NEGATIVO (-@cantidad) -- reduce el pendiente por recibir de la
-- OC origen, no lo registra como pendiente nuevo. Confirmado contra el sandbox real: por
-- analogía con la OC (donde ese campo queda en +cantidad) se hubiera adivinado el signo mal.
INSERT INTO orgProductKardex (DateTransaction, DepotID, ProductID, DocumentID, DocumentItemID, Quantity, QuantityToBeDelivered, AmountPrice, Cancelled, ProductImportID, DepotValue, DepotValueAverage, QuantityImport)
VALUES (@ahora, @almacen, @productoID, @docId, @itemId, @cantidad, -@cantidad, @precio, 0, 0, 0, 0, 0);

-- ═══════════════════ 6. Lote (solo si @lote no es NULL) ═══════════════════
IF @lote IS NOT NULL
    INSERT INTO docDocumentLot (DocumentID, DocumentItemID, ProductID, Lot, ExpirationDate, Quantity, Unit, BaseUnit, QuantityBaseUnit, DepotID, CreatedOn, CreatedBy)
    SELECT @docId, @itemId, @productoID, @lote, @caducidad, @cantidad, p.Unit, p.Unit, @cantidad, @almacen, @ahora, 0
    FROM orgProduct p WHERE p.ProductID = @productoID;

-- ═══════════════════ 7. Números de serie (solo si @seriesCSV no es NULL/vacío) ═══════════════════
-- Una fila por serie -- STRING_SPLIT requiere @seriesCSV sin espacios extra alrededor de comas.
IF @seriesCSV IS NOT NULL AND LEN(@seriesCSV) > 0
    INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, Quantity, DepotID, StatusID, CreatedOn, CreatedBy)
    SELECT @docId, @itemId, @productoID, LTRIM(RTRIM(value)), 1, @almacen, 1, @ahora, 0
    FROM STRING_SPLIT(@seriesCSV, ',');

SELECT 'Recepción de compra (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) + ', origen=OC ' + CAST(@sourceOC AS VARCHAR) AS Resultado;
