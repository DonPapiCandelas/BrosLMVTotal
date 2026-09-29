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
-- Atomicidad: SÍ (desde esta versión). Todo el bloque que crea cabecera + anclas satélite +
-- partida + agenda de entrega + impuestos + kardex comprometido corre dentro de BEGIN TRAN/
-- COMMIT TRAN con BEGIN TRY/CATCH y ROLLBACK si algo falla a medio camino -- antes, un fallo
-- intermedio dejaba un docDocument "fantasma". Las validaciones iniciales siguen corriendo
-- ANTES de abrir la transacción.
--
-- Cómo se llenan los datos (v2.77.0+): 6 de los 7 parámetros ahora se capturan con un
-- formulario automático (token tipado {DATOS:Tabla.Columna:*}, motor v2.75.0/2.76.0) -- ya no
-- hace falta editar este archivo a mano para lo más común (proveedor/almacén/producto/
-- cantidad/precio/condición de pago). @cantidad y @precio se anclan a docDocumentItem.Quantity/
-- UnitPrice (mismos tipos DECIMAL(18,4)/DECIMAL(18,2) que la partida real usa) solo para
-- inferir el tipo de campo correcto, no para leer un valor existente. @diasEntrega SIGUE
-- hardcodeado a propósito: no corresponde a ninguna columna real (es un número de días que se
-- SUMA a la fecha de hoy, no un valor que se guarde tal cual en ninguna tabla) -- edítalo a
-- mano si necesitas un plazo de entrega distinto a 15 días.

-- ═══════════════════ PARÁMETROS (la mayoría se capturan con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @proveedorBE  INT = {DATOS:orgBusinessEntity.BusinessEntityID:Proveedor (BusinessEntityID):*};
DECLARE @almacen      INT = {DATOS:orgDepot.DepotID:Almacén (DepotID):*};
DECLARE @productoID   INT = {DATOS:orgProduct.ProductID:Producto (ProductID):*};
DECLARE @cantidad     DECIMAL(18,4) = {DATOS:docDocumentItem.Quantity:Cantidad:*};
DECLARE @precio       DECIMAL(18,2) = {DATOS:docDocumentItem.UnitPrice:Precio unitario (sin IVA):*};
DECLARE @diasEntrega  INT = 15;             -- fecha de entrega = hoy + N días -- NO migrado, no corresponde a una columna real (ver nota arriba)
-- PaymentTermID: confirmado contra profiler real de una OC nativa (4 = CRÉDITO en el
-- catálogo de este sandbox; ver empresa_base_cp/orden_compra_compleja/profiler_analisis.md)
-- -- a diferencia de Entrada/Salida/Solicitud, la OC SIEMPRE lleva una condición de pago
-- real, nunca 0. El formulario pide el PaymentTermID real de tu catálogo (INT).
DECLARE @condicionPago INT = {DATOS:docDocument.PaymentTermID:Condición de pago (PaymentTermID):*};

-- ═══════════════════ VALIDACIONES MÍNIMAS ═══════════════════
IF NOT EXISTS (SELECT 1 FROM orgBusinessEntity WHERE BusinessEntityID = @proveedorBE)
BEGIN SELECT 'ERROR: no existe el proveedor BusinessEntityID=' + CAST(@proveedorBE AS VARCHAR) AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM orgProduct WHERE ProductID = @productoID)
BEGIN SELECT 'ERROR: no existe el producto ProductID=' + CAST(@productoID AS VARCHAR) AS Resultado; RETURN; END

-- ═══════════════════ TRANSACCIÓN (atomicidad real) ═══════════════════
-- Todo lo que sigue (cálculos, cabecera, anclas satélite, partida, agenda de entrega,
-- impuestos, kardex comprometido) corre dentro de UNA sola transacción. Antes de esto, un
-- fallo a mitad del batch dejaba un docDocument "fantasma" -- el mismo bug real que se
-- encontró en producción de un cliente, con años de documentos incompletos acumulados. THROW
-- (sin argumentos, SQL Server 2012+) repropaga el error original tras el ROLLBACK.
BEGIN TRY
BEGIN TRAN;

-- ═══════════════════ CÁLCULOS (lo que RecalcCompleto/AffectStockNEW hacen solos) ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 183), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
DECLARE @entrega DATETIME = DATEADD(DAY, @diasEntrega, @ahora);

DECLARE @tasaIva DECIMAL(9,4) = 0.16;
DECLARE @subtotal DECIMAL(18,2) = @cantidad * @precio;
DECLARE @iva DECIMAL(18,2) = ROUND(@subtotal * @tasaIva, 2);
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
-- Perfil de OC (distinto de Entrada/Salida/Solicitud): DocRecipientID=2 (rol proveedor, NO
-- el BusinessEntityID), PaymentTermID=condición real (nunca 0), StatusDeliveryID=3 (exige
-- UpdateStatusDelivery -- MANUAL.md §6.3, no es opcional). StatusPaidID=3 -- CONFIRMADO
-- contra el sandbox real (no 0 como decía una versión anterior de este comentario, basada
-- en una captura de profiler externa que no coincidía con este entorno): tras Save +
-- ctx.erp.UpdateDocumentPaidInfo + ctx.erp.UpdateStatusDelivery, el documento nativo queda
-- en StatusPaidID=3 -- el mismo default documentado para Solicitud de Compra en MANUAL.md
-- §7.3 ("3 = pagado, es el default, no implica pago real"), UpdateDocumentPaidInfo no lo
-- cambia a 0 en la práctica pese a lo que su nombre sugiere.
DECLARE @doc TABLE (DocumentID INT);
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
    SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalLetter, TotalPaid, Balance, StatusPaidID, StatusDeliveryID,
    CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    183, 40, 2, 1, @proveedorBE,
    @almacen, 0, '', CAST(@folio AS NVARCHAR), @ahora, @entrega, @entrega, @ahora, @ahora, @ahora,
    3, 3, 1, @condicionPago, @ahora, 1, 1,
    @subtotal, @subtotal, @total, @iva, @totalLetra, 0, @total, 3, 3,
    @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
-- docDocumentExt SÍ aplica -- su columna que guarda el DocumentID se llama IDExtra, no
-- "DocumentID". Mismo INSERT que usa el propio addon en Scripting.cs NuevoDocumento().
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
-- Amount/Total en 0 (no @total) a propósito -- comparado contra un documento nativo real:
-- ctx.erp.UpdateDocumentPaidInfo() (que en teoría "regenera la agenda con montos reales",
-- MANUAL.md §7.5) deja estos 2 campos en 0 en la práctica en esta versión de Comercial, no
-- los montos esperados. Se replica el comportamiento real observado, no el documentado.
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, Total, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 0, 1, @ahora, 0);

-- ═══════════════════ 3. Partida ═══════════════════
-- CostPrice=0 en docDocumentItem es correcto aunque parezca raro -- el costo real
-- (@precio) se guarda en orgProductKardex.AmountPrice; AgregarArticulo/RecalcCompleto
-- dejan ese campo del item en 0 -- confirmado comparando contra un documento nativo.
-- TaxPerc SÍ debe llevar la tasa real (0.16), confirmado contra el profiler real de una OC
-- nativa (docDocumentItem.TaxPerc=0.16 en la partida) -- a diferencia de CostPrice, este
-- campo NO se queda en 0.
DECLARE @item TABLE (DocumentItemID INT);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered,
    ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', 5, @tasaIva, @precio, 0, @subtotal, 1, 1,
    1, 1, 1, 1, @ahora
FROM orgProduct p WHERE p.ProductID = @productoID;
DECLARE @itemId INT = (SELECT TOP 1 DocumentItemID FROM @item);

-- ═══════════════════ 3b. Relación producto-proveedor ═══════════════════
-- Confirmado como paso explícito del método de réplica (EXP-DOC-orden_compra_001,
-- replication/orden_compra.ctx): registra al proveedor como fuente de este producto si
-- todavía no lo era.
DECLARE @supplierID INT = (SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID = @proveedorBE);
IF @supplierID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID = @productoID AND SupplierID = @supplierID)
    INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber)
    VALUES (@productoID, @supplierID, @precio, 3, 0);

-- ═══════════════════ 3c. Agenda de entrega ═══════════════════
-- docDocumentDeliveryAgenda: tabla exclusiva de OC (no aparece en Entrada/Salida/
-- Solicitud), una fila por partida física -- la genera el Save nativo automáticamente;
-- como SQL puro no puede llamar Save, se inserta a mano (confirmado en
-- EXP-DOC-orden_compra_001, sección "docDocumentDeliveryAgenda").
INSERT INTO docDocumentDeliveryAgenda (DocumentID, DocumentItemID, ProductID, Quantity, QtyDelivered, DateDelivery, CreatedOn, CreatedBy)
VALUES (@docId, @itemId, @productoID, @cantidad, 0, @entrega, @ahora, 0);

-- ═══════════════════ 4. Impuestos (IVA 16% simple -- ver advertencia al inicio) ═══════════════════
INSERT INTO docDocumentTax (DocumentID, IVA_T, IVA_R, ISR_R, IEPS_T, IEPS_R, Otro, Local_T, Local_R)
VALUES (@docId, @iva, 0, 0, 0, 0, 0, 0, 0);

INSERT INTO docDocumentTaxDetail (DocumentID, DocumentItemID, TaxTypeID, TaxItemID, Amount, Retention, IVASobreIEPS, RegionalTaxID, TaxName, TaxTypeName, TaxBase, TaxPerc, TipoFactor)
VALUES (@docId, @itemId, 5, 6, @iva, 0, 0, 0, 'IVA 16.00%', 'IVA', @subtotal, @tasaIva, 'Tasa');

INSERT INTO docDocumentTaxSum (DocumentID, TaxName1, TaxAmount1, TotalFederal, TotalLocal, TotalOtro)
VALUES (@docId, 'IVA 16%', @iva, @iva, 0, 0);

-- ═══════════════════ 5. Kardex comprometido (equivalente a AffectStockNEW para una OC) ═══════════════════
-- Quantity=0 a propósito: una OC compromete inventario SIN moverlo (la Recepción de
-- Compra, módulo 184, es la que sí mueve cantidad real) -- confirmado contra el sandbox.
INSERT INTO orgProductKardex (DateTransaction, DepotID, ProductID, DocumentID, DocumentItemID, Quantity, QuantityToBeDelivered, AmountPrice, Cancelled, ProductImportID, DepotValue, DepotValueAverage, QuantityImport)
VALUES (@ahora, @almacen, @productoID, @docId, @itemId, 0, @cantidad, @precio, 0, 0, 0, 0, 0);

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- el documento ya quedó comprometido (COMMIT) antes de llegar
-- aquí; un fallo leyendo este SELECT no debe interpretarse como que la transacción falló.
SELECT 'Orden de compra (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) + ', total=$' + CAST(@total AS VARCHAR) AS Resultado;
