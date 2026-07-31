-- lang: sql
-- PLANTILLA: Orden de Compra — SQL puro (INSERT directo, sin ctx.erp)
--
-- Qué hace: crea una Orden de Compra real (módulo 183) insertando directamente en las
-- tablas de Comercial, SIN pasar por ctx.erp. Mismo resultado final que las otras 4
-- variantes -- confirmado comparando campo por campo contra un documento nativo real
-- (misma metodología que PLANTILLA_REQUISICION_SQL_PURO.sql).
--
-- ⚠️ ADVERTENCIA REAL: ESTA ES LA PLANTILLA MÁS FRÁGIL DE TODO EL CATÁLOGO. A diferencia de
-- Requisición (que siempre tiene Total=$0, no captura importes), una Orden de Compra SÍ
-- calcula impuestos, kardex comprometido y agenda de pago con montos reales -- SQL puro
-- tiene que replicar ese cálculo A MANO:
--   1. IVA 16% simple (config.TaxTypeID=5) -- si tu OC necesita otro régimen fiscal
--      (retenciones, IEPS, tasa 0%, exento), esta plantilla NO lo calcula bien, tendrías
--      que extenderla.
--   2. "Total en letras" (TotalLetter) -- se reimplementó el algoritmo número-a-letras
--      completo en T-SQL (ver sección al final), probado contra 6 casos reales
--      (incluye "MIL", "CIEN", "VEINTIUN", límite 999,999.99). No traduce millones a
--      letras completas (caso raro para una OC, límite práctico de este ejemplo).
--   3. `orgProductKardex` -- se inserta a mano con Quantity=0 (compromete sin mover,
--      igual que AffectStockNEW), pero SIN el cálculo de costo promedio que XEngine sí
--      hace internamente para otros tipos de documento.
-- Usa esta plantilla cuando sepas exactamente por qué la necesitas, no como default --
-- para el 95% de los casos, usa cualquiera de las otras 4 variantes (más simples y menos
-- riesgo de que un caso fiscal no cubierto aquí deje un documento mal calculado).
--
-- Cómo se llenan los datos: reemplaza los valores de "PARÁMETROS" antes de correr.

-- ═══════════════════ PARÁMETROS (edítalos antes de correr) ═══════════════════
DECLARE @proveedorBE INT = 2;
DECLARE @almacen     INT = 1;
DECLARE @productoID  INT = 1;
DECLARE @cantidad    DECIMAL(18,4) = 2;
DECLARE @precio      DECIMAL(18,2) = 80;   -- precio unitario, sin IVA
DECLARE @diasEntrega INT = 15;             -- fecha de entrega = hoy + N días

-- ═══════════════════ VALIDACIONES MÍNIMAS ═══════════════════
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntity WHERE BusinessEntityID = @proveedorBE)
BEGIN SELECT 'ERROR: no existe el proveedor BusinessEntityID=' + CAST(@proveedorBE AS VARCHAR) AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM orgProduct WHERE ProductID = @productoID)
BEGIN SELECT 'ERROR: no existe el producto ProductID=' + CAST(@productoID AS VARCHAR) AS Resultado; RETURN; END

-- ═══════════════════ CÁLCULOS (lo que RecalcCompleto/AffectStockNEW hacen solos) ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 183), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
DECLARE @entrega DATETIME = DATEADD(DAY, @diasEntrega, @ahora);

DECLARE @subtotal DECIMAL(18,2) = @cantidad * @precio;
DECLARE @iva DECIMAL(18,2) = ROUND(@subtotal * 0.16, 2);
DECLARE @total DECIMAL(18,2) = @subtotal + @iva;

-- ---- Número a letras (algoritmo completo, probado contra 6 casos reales) ----
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
DECLARE @doc TABLE (DocumentID INT);
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
    SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalLetter, TotalPaid, Balance, StatusPaidID,
    CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    183, 40, @proveedorBE, 1, @proveedorBE,
    @almacen, 0, '', CAST(@folio AS NVARCHAR), @ahora, @entrega, @entrega, @ahora, @ahora, @ahora,
    3, 3, 1, 0, @ahora, 1, 1,
    @subtotal, @subtotal, @total, @iva, @totalLetra, 0, @total, 3,
    @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
-- Amount/Total en 0 (no @total) a propósito -- comparado contra un documento nativo real:
-- ctx.erp.UpdateDocumentPaidInfo() (que en teoría "regenera la agenda con montos reales",
-- MANUAL.md §7.5) deja estos 2 campos en 0 en la práctica en esta versión de Comercial, no
-- los montos esperados. Se replica el comportamiento real observado, no el documentado.
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, Total, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 0, 1, @ahora, 0);

-- ═══════════════════ 3. Partida ═══════════════════
-- CostPrice=0 y TaxPerc=0 en docDocumentItem son correctos aunque parezca raro -- el
-- costo real (@precio) y la tasa (0.16) se guardan en otras tablas
-- (orgProductKardex.AmountPrice y docDocumentTaxDetail.TaxPerc); AgregarArticulo/
-- RecalcCompleto dejan estos 2 campos del item en 0 -- confirmado comparando contra un
-- documento nativo, no es una omisión de esta plantilla.
DECLARE @item TABLE (DocumentItemID INT);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered,
    ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', 5, 0, @precio, 0, @subtotal, 1, 1,
    1, 1, 1, 1, @ahora
FROM orgProduct p WHERE p.ProductID = @productoID;
DECLARE @itemId INT = (SELECT TOP 1 DocumentItemID FROM @item);

-- ═══════════════════ 4. Impuestos (IVA 16% simple -- ver advertencia al inicio) ═══════════════════
INSERT INTO docDocumentTax (DocumentID, IVA_T, IVA_R, ISR_R, IEPS_T, IEPS_R, Otro, Local_T, Local_R)
VALUES (@docId, @iva, 0, 0, 0, 0, 0, 0, 0);

INSERT INTO docDocumentTaxDetail (DocumentID, DocumentItemID, TaxTypeID, TaxItemID, Amount, Retention, IVASobreIEPS, RegionalTaxID, TaxName, TaxTypeName, TaxBase, TaxPerc, TipoFactor)
VALUES (@docId, @itemId, 5, 6, @iva, 0, 0, 0, 'IVA 16.00%', 'IVA', @subtotal, 0.16, 'Tasa');

INSERT INTO docDocumentTaxSum (DocumentID, TaxName1, TaxAmount1, TotalFederal, TotalLocal, TotalOtro)
VALUES (@docId, 'IVA 16%', @iva, @iva, 0, 0);

-- ═══════════════════ 5. Kardex comprometido (equivalente a AffectStockNEW para una OC) ═══════════════════
-- Quantity=0 a propósito: una OC compromete inventario SIN moverlo (la Recepción de
-- Compra, módulo 184, es la que sí mueve cantidad real) -- confirmado contra el sandbox.
INSERT INTO orgProductKardex (DateTransaction, DepotID, ProductID, DocumentID, DocumentItemID, Quantity, QuantityToBeDelivered, AmountPrice, Cancelled, ProductImportID, DepotValue, DepotValueAverage, QuantityImport)
VALUES (@ahora, @almacen, @productoID, @docId, @itemId, 0, @cantidad, @precio, 0, 0, 0, 0, 0);

SELECT 'Orden de compra (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) + ', total=$' + CAST(@total AS VARCHAR) AS Resultado;
