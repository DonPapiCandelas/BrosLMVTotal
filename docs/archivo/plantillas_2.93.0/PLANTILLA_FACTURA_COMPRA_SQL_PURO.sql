-- lang: sql
-- PLANTILLA: Factura de Compra — SQL puro (INSERT directo, sin ctx.erp)
--
-- Qué hace: crea una Factura de Compra real (módulo 152) DERIVADA de una Orden de Compra ya
-- existente, insertando directamente en las tablas de Comercial, SIN pasar por ctx.erp.
-- Confirmado campo por campo contra un documento nativo creado con ctx.erp en ESTE sandbox.
--
-- ⚠️ ADVERTENCIA REAL #1: recibe UNA sola partida de UNA sola Orden de Compra por corrida
-- (mismo punto de partida que las primeras versiones de las demás plantillas SQL puro) --
-- para facturar VARIAS Órdenes de Compra en un solo documento, usa
-- PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx.
--
-- ⚠️ ADVERTENCIA REAL #2 (esta SÍ es exclusiva de Factura, no la tienen las otras 3): la
-- Factura de Compra en teoría genera una PÓLIZA CONTABLE automática (accPoliza +
-- accPolizaTransaccion) al guardarse -- confirmado en una captura de profiler externa
-- (EXP-DOC-factura_compra_001). PERO en ESTE sandbox, ni siquiera guardando la Factura por
-- el camino 100% nativo (ctx.erp.Save) se generó ninguna póliza -- confirmado con
-- accPolizasPorDocumentID en 0 filas tras el Save. Esto es una limitación del SANDBOX (le
-- falta la configuración contable -- catálogo de cuentas / accPolizaDefinition para el
-- módulo 152), no un bug de esta plantilla: la generación de póliza es lógica interna de
-- XEngine que NINGUNA de las 6 variantes de esta plantilla puede replicar por SQL (no hay
-- forma de invocar esa lógica sin ctx.erp.Save). Si tu instalación SÍ tiene la contabilidad
-- configurada, prueba primero con la versión Forms (ctx.erp) para confirmar que tu Comercial
-- sí la genera antes de asumir que esta plantilla también debería.
--
-- Diferencia real frente a Recepción de Compra: la Factura NO afecta inventario (0 filas en
-- orgProductKardex) y usa su PROPIA columna de vínculo por partida
-- (docDocumentItem.SourceDocumentItemID) -- DISTINTA de la que usa Recepción
-- (DeliverDocumentItemID) -- ver MANUAL.md §10.4. CostPrice en la partida se queda en 0
-- (como en la Orden de Compra, NO como en la Recepción donde costo=precio).
--
-- PaymentAgenda con montos REALES (a diferencia de Orden de Compra, donde se queda en 0):
-- confirmado contra el sandbox real, con PaymentTermID=4 el reparto es 50% al momento + 50%
-- a 3 meses -- si tu catálogo usa otra condición de pago, ajusta el reparto en la sección 4.
--
-- Cómo se llenan los datos (v2.77.0+): los 7 parámetros ahora se capturan con un formulario
-- automático (token tipado {DATOS:Tabla.Columna:*}, motor v2.75.0/2.76.0). @sourceOC/
-- @sourceItemId se anclan a docDocument.DocumentID/docDocumentItem.DocumentItemID (mismo tipo
-- INT que esas columnas) solo para inferir el tipo de campo -- siguen siendo "el DocumentID/
-- DocumentItemID que tú ya conoces de la OC", no una lectura automática. @cantidad/@precio se
-- anclan a docDocumentItem.Quantity/UnitPrice y @condicionPago a docDocument.PaymentTermID
-- por la misma razón.
--
-- Atomicidad: SÍ (desde esta versión). Todo el bloque que crea cabecera + anclas satélite +
-- partida + impuestos + agenda de pago corre dentro de BEGIN TRAN/COMMIT TRAN con BEGIN TRY/
-- CATCH y ROLLBACK si algo falla a medio camino -- antes, un fallo intermedio dejaba un
-- docDocument "fantasma". Las validaciones iniciales siguen corriendo ANTES de abrir la
-- transacción.

-- ═══════════════════ PARÁMETROS (se capturan con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @sourceOC        INT = {DATOS:docDocument.DocumentID:OC origen (DocumentID):*};   -- DocumentID de la Orden de Compra que se está facturando
DECLARE @sourceItemId    INT = {DATOS:docDocumentItem.DocumentItemID:Partida de la OC (DocumentItemID):*};   -- DocumentItemID de la partida de esa OC (columna SourceDocumentItemID)
DECLARE @almacen         INT = {DATOS:orgDepot.DepotID:Almacén (DepotID):*};
DECLARE @productoID      INT = {DATOS:orgProduct.ProductID:Producto (ProductID):*};
DECLARE @cantidad        DECIMAL(18,4) = {DATOS:docDocumentItem.Quantity:Cantidad:*};
DECLARE @precio          DECIMAL(18,2) = {DATOS:docDocumentItem.UnitPrice:Precio unitario (sin IVA):*};
DECLARE @condicionPago   INT = {DATOS:docDocument.PaymentTermID:Condición de pago (PaymentTermID):*};              -- condición de pago real (nunca 0, igual que en OC)

-- ═══════════════════ VALIDACIONES MÍNIMAS ═══════════════════
DECLARE @proveedorBE INT;
SELECT @proveedorBE = BusinessEntityID FROM docDocument WHERE DocumentID = @sourceOC AND ModuleID = 183;
IF @proveedorBE IS NULL
BEGIN SELECT 'ERROR: no existe la Orden de Compra DocumentID=' + CAST(@sourceOC AS VARCHAR) + ' (o no es modulo 183)' AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM docDocumentItem WHERE DocumentItemID = @sourceItemId AND DocumentID = @sourceOC)
BEGIN SELECT 'ERROR: la partida DocumentItemID=' + CAST(@sourceItemId AS VARCHAR) + ' no pertenece a la OC ' + CAST(@sourceOC AS VARCHAR) AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM orgProduct WHERE ProductID = @productoID)
BEGIN SELECT 'ERROR: no existe el producto ProductID=' + CAST(@productoID AS VARCHAR) AS Resultado; RETURN; END

-- ═══════════════════ TRANSACCIÓN (atomicidad real) ═══════════════════
-- Todo lo que sigue (cálculos, cabecera, anclas satélite, partida, impuestos, agenda de pago
-- con montos reales) corre dentro de UNA sola transacción. Antes de esto, un fallo a mitad
-- del batch dejaba un docDocument "fantasma" -- el mismo bug real que se encontró en
-- producción de un cliente, con años de documentos incompletos acumulados. THROW (sin
-- argumentos, SQL Server 2012+) repropaga el error original tras el ROLLBACK.
BEGIN TRY
BEGIN TRAN;

-- ═══════════════════ CÁLCULOS ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 152), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
DECLARE @tasaIva DECIMAL(9,4) = 0.16;
DECLARE @subtotal DECIMAL(18,2) = @cantidad * @precio;
DECLARE @iva DECIMAL(18,2) = ROUND(@subtotal * @tasaIva, 2);
DECLARE @total DECIMAL(18,2) = @subtotal + @iva;

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
-- Perfil de Factura: DocumentTypeID=5, StatusDeliveryID=0 SIEMPRE (a diferencia de OC/
-- Recepción, la Factura no tiene un "estatus de entrega" real -- confirmado contra el
-- sandbox Y contra una captura de profiler externa: coinciden en 0, así que aquí NO se
-- llama UpdateStatusDelivery, a propósito). TotalCost=0 (a diferencia de la Recepción,
-- donde costo=precio -- aquí, como en la OC, CostPrice de la partida se queda en 0).
DECLARE @doc TABLE (DocumentID INT);
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
    SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalCost, TotalLetter, TotalPaid, Balance, StatusPaidID, StatusDeliveryID,
    CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    152, 5, 2, 1, @proveedorBE,
    @almacen, 0, '', CAST(@folio AS NVARCHAR), @ahora, @ahora, @ahora, @ahora, @ahora, @ahora,
    3, 3, 1, @condicionPago, @ahora, 1, 1,
    @subtotal, @subtotal, @total, @iva, 0, @totalLetra, 0, @total, 3, 0,
    @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');

-- ═══════════════════ 3. Partida ═══════════════════
-- CostPrice=0 (a diferencia de Recepción de Compra) -- confirmado contra el sandbox.
-- SourceDocumentItemID = @sourceItemId -- columna de vínculo PROPIA de Factura, DISTINTA de
-- DeliverDocumentItemID (esa es de Recepción) -- MANUAL.md §10.4.
DECLARE @item TABLE (DocumentItemID INT);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered,
    ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, SourceDocumentItemID, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', 5, @tasaIva, @precio, 0, @subtotal, 1, 1,
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

-- ═══════════════════ 5. Agenda de pago CON MONTOS REALES ═══════════════════
-- A diferencia de Orden de Compra (Amount=0): la Factura SÍ es una cuenta por pagar real.
-- Reparto 50%/50% (al momento + 3 meses) confirmado contra el sandbox para PaymentTermID=4
-- -- si usas otra condición de pago, ajusta este reparto según tu catálogo
-- (engPaymentTermDetail tiene los porcentajes/plazos reales de cada PaymentTermID).
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 50, ROUND(@total * 0.5, 2), 1, @ahora, 0);
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, DATEADD(MONTH, 3, @ahora), 50, ROUND(@total * 0.5, 2), 2, @ahora, 0);

-- ═══════════════════ 6. SIN kardex, SIN póliza contable ═══════════════════
-- La Factura de Compra NO afecta inventario (0 filas en orgProductKardex, a diferencia de
-- Orden de Compra y Recepción). La póliza contable NO se genera aquí -- ver advertencia #2
-- al inicio del archivo.

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- el documento ya quedó comprometido (COMMIT) antes de llegar
-- aquí; un fallo leyendo este SELECT no debe interpretarse como que la transacción falló.
SELECT 'Factura de compra (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) + ', origen=OC ' + CAST(@sourceOC AS VARCHAR) AS Resultado;
