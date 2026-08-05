-- lang: sql
-- PLANTILLA: Requisición → N Órdenes de Compra (una por proveedor) — SQL puro
--
-- Qué hace: dada una Requisición de Compra (módulo 1040) YA VALIDADA, genera UNA Orden de
-- Compra (módulo 183) POR CADA PROVEEDOR DISTINTO que aparezca en sus renglones -- nunca una
-- sola OC mezclando proveedores (no tiene sentido de negocio: una OC es un documento dirigido
-- a UN proveedor). Si la Requisición ya generó la OC de un proveedor en una corrida anterior,
-- esa corrida se salta (no la duplica) y solo genera las que faltan.
--
-- Origen: se identificó este patrón analizando un stored procedure real de producción de un
-- cliente (`ZLCGENERA_OC`, ver entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md,
-- sección "ZLCGENERA_OC"). El SP original tenía 2 validaciones reales y correctas (documento
-- origen validado, dedup por proveedor) pero sin transacción ni manejo de errores -- aquí se
-- corrigen esos 2 defectos con el mismo estándar que el resto de PLANTILLA_*_SQL_PURO desde
-- v2.66.0 (BEGIN TRAN/COMMIT TRAN con TRY/CATCH), y se completa el cálculo de impuestos/
-- kardex/agenda de entrega que el SP original truncaba (evidencia incompleta).
--
-- ⚠️ PRECEDENTE DE DISEÑO: esta es la PRIMERA plantilla SQL_PURO de BrosLMV que usa el flag
-- nativo de Comercial Pro `docDocument.ValidatedOn`/`ValidatedBy` ("documento validado/
-- aprobado") como precondición de negocio. Ninguna otra plantilla SQL_PURO lo usa hoy. La
-- Requisición origen DEBE tener `ValidatedOn IS NOT NULL` -- si tu flujo de captura de
-- Requisiciones no pasa por un paso de aprobación que llene esa columna (Comercial Pro no lo
-- hace solo; alguien tiene que hacer `UPDATE docDocument SET ValidatedOn=GETDATE(),
-- ValidatedBy=<UserID> WHERE DocumentID=...` o el mecanismo nativo equivalente), esta plantilla
-- rechaza con un mensaje claro en vez de generar OC de una Requisición sin aprobar.
--
-- ⚠️ CÓMO SE IDENTIFICA EL PROVEEDOR POR RENGLÓN (hallazgo real, confirmado contra
-- INFORMATION_SCHEMA del sandbox): `docDocumentItem.SupplierBusinessEntityID` es una columna
-- que YA EXISTE en el esquema exactamente para esto (proveedor sugerido/asignado a ESE
-- renglón), pero NINGÚN documento del sandbox la trae poblada -- ni los creados por Comercial
-- nativo ni por `PLANTILLA_REQUISICION_SQL_PURO.sql` (esa plantilla asume 1 solo proveedor
-- para TODA la Requisición, a nivel de cabecera `docDocument.BusinessEntityID`). Por eso esta
-- plantilla usa `COALESCE(NULLIF(SupplierBusinessEntityID,0), <BusinessEntityID de la
-- cabecera>)` por renglón: si el renglón trae su propio proveedor, se respeta (caso
-- multi-proveedor real, capturado a mano o por una pantalla que sí llene esa columna); si no
-- trae nada (0 o NULL, el caso de hoy), se usa el proveedor de la cabecera -- así una
-- Requisición "clásica" de un solo proveedor (creada con la plantilla existente) también
-- funciona con esta plantilla nueva (genera 1 sola OC, el caso degenerado de "N=1 proveedor").
--
-- Precio de la OC generada: la Requisición NUNCA captura precio (Total siempre $0, confirmado
-- en PLANTILLA_REQUISICION_SQL_PURO.sql). El precio de cada renglón de la OC se toma de
-- `orgProductSupplier.CostPrice` para el par (producto, proveedor) si ya está registrado: si
-- NO lo está, la partida se genera con UnitPrice=0 (misma limitación que documenta
-- PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql para casos sin precio -- edítala manualmente después si
-- pasa). El resto de constantes de negocio (IVA 16% simple, condición de pago, días de
-- entrega) son las MISMAS que usa PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql -- ver su advertencia:
-- si tu OC necesita otro régimen fiscal, esta plantilla tampoco lo calcula bien.
--
-- Dedup: antes de generar la OC de un proveedor, se busca si YA existe una
-- `docDocument WHERE SourceDocumentID=@requisicionID AND BusinessEntityID=<proveedor> AND
-- ModuleID=183 AND DeletedOn IS NULL AND CancelledOn IS NULL` -- si existe, se salta (se
-- reporta en el SELECT final con YaExistia=1, no se toca). Esto permite correr esta plantilla
-- varias veces sobre la misma Requisición sin duplicar: la primera corrida genera todas las OC
-- que faltan, las corridas siguientes no generan nada nuevo si ya están todas.
--
-- Atomicidad: todo el bloque (N documentos completos: cabecera + anclas + partidas + agenda de
-- entrega + impuestos + kardex + relación producto-proveedor, para TODOS los proveedores que
-- faltan) corre dentro de UNA sola transacción -- o se generan TODAS las OC que faltan, o
-- ROLLBACK y no se genera ninguna. Se prefirió esto sobre "una transacción por proveedor"
-- porque una corrida parcial (2 de 3 OC generadas, la 3a falló) sería más difícil de
-- diagnosticar que "todo o nada" -- si un proveedor tiene datos que rompen el cálculo (ej. un
-- producto sin ClaveUnidad), es mejor que NINGUNA OC se genere en esa corrida y el mensaje de
-- error sea claro, a dejar 2 OC huérfanas y 1 faltante silenciosa.
--
-- Guard clauses (antes de abrir transacción, sin necesidad de ella): la Requisición debe
-- existir, ser módulo 1040, no estar eliminada/cancelada, estar validada (ValidatedOn IS NOT
-- NULL), tener al menos 1 renglón, y cada proveedor calculado por renglón debe existir en
-- orgBusinessEntity (no eliminado) -- si algo de esto falla, error claro y RETURN, sin tocar
-- nada.
--
-- Cómo se llena (v2.77.0+): @requisicionID (DocumentID de la Requisición ya validada) ahora se
-- captura con un formulario automático (token tipado {DATOS:docDocument.DocumentID:*}, motor
-- v2.75.0/2.76.0) -- correspondencia 1:1 real con la columna. Ya no hace falta editar este
-- archivo a mano. Sigue sin haber más parámetros editables -- el proveedor y las cantidades se
-- leen de los renglones de la Requisición, no se capturan a mano (a diferencia de las otras
-- plantillas SQL_PURO de este catálogo, que sí piden @proveedorBE/@productoID/@cantidad).

-- ═══════════════════ PARÁMETRO (se captura con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @requisicionID INT = {DATOS:docDocument.DocumentID:Requisición ya validada (DocumentID):*};

-- ═══════════════════ VALIDACIONES MÍNIMAS (guard clauses, antes de abrir transacción) ═══════════════════
IF NOT EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @requisicionID AND ModuleID = 1040)
BEGIN SELECT 'ERROR: no existe una Requisición (ModuleID=1040) con DocumentID=' + CAST(@requisicionID AS VARCHAR) AS Resultado; RETURN; END
IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @requisicionID AND DeletedOn IS NOT NULL)
BEGIN SELECT 'ERROR: la Requisición DocumentID=' + CAST(@requisicionID AS VARCHAR) + ' está eliminada (DeletedOn no es NULL)' AS Resultado; RETURN; END
IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @requisicionID AND CancelledOn IS NOT NULL)
BEGIN SELECT 'ERROR: la Requisición DocumentID=' + CAST(@requisicionID AS VARCHAR) + ' está cancelada' AS Resultado; RETURN; END
-- La validación de negocio real (ver advertencia arriba): sin esto, cualquier Requisición
-- recién capturada podría generar OC sin que nadie la haya aprobado.
IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @requisicionID AND ValidatedOn IS NULL)
BEGIN SELECT 'ERROR: la Requisición DocumentID=' + CAST(@requisicionID AS VARCHAR) + ' debe estar validada (ValidatedOn IS NOT NULL) antes de generar Órdenes de Compra -- valídala primero.' AS Resultado; RETURN; END
IF NOT EXISTS (SELECT 1 FROM docDocumentItem WHERE DocumentID = @requisicionID)
BEGIN SELECT 'ERROR: la Requisición DocumentID=' + CAST(@requisicionID AS VARCHAR) + ' no tiene renglones' AS Resultado; RETURN; END

-- ═══════════════════ PROVEEDORES DISTINTOS (calculado ANTES de la transacción) ═══════════════════
-- COALESCE(NULLIF(SupplierBusinessEntityID,0), BusinessEntityID de cabecera) -- ver
-- advertencia de diseño al inicio del archivo.
DECLARE @proveedores TABLE (RowNum INT IDENTITY(1,1) PRIMARY KEY, ProveedorID INT);
INSERT INTO @proveedores (ProveedorID)
SELECT DISTINCT COALESCE(NULLIF(di.SupplierBusinessEntityID, 0), d.BusinessEntityID)
FROM docDocumentItem di
INNER JOIN docDocument d ON d.DocumentID = di.DocumentID
WHERE di.DocumentID = @requisicionID;

-- Cada proveedor calculado debe existir de verdad (FK real) -- si un renglón trae un
-- SupplierBusinessEntityID inválido (dato mal capturado), es mejor rechazar aquí, antes de
-- abrir la transacción, que generar una OC con un BusinessEntityID roto.
IF EXISTS (
    SELECT 1 FROM @proveedores pv
    WHERE NOT EXISTS (SELECT 1 FROM orgBusinessEntity be WHERE be.BusinessEntityID = pv.ProveedorID AND be.DeletedOn IS NULL)
)
BEGIN
    SELECT 'ERROR: al menos un proveedor calculado de los renglones de la Requisición no existe en orgBusinessEntity (o está eliminado) -- revisa docDocumentItem.SupplierBusinessEntityID' AS Resultado;
    RETURN;
END

DECLARE @almacenReq INT = (SELECT DepotID FROM docDocument WHERE DocumentID = @requisicionID);

-- ═══════════════════ TRANSACCIÓN (atomicidad real -- todo o nada, ver advertencia arriba) ═══════════════════
BEGIN TRY
BEGIN TRAN;

DECLARE @ahora DATETIME = GETDATE();
DECLARE @tasaIva DECIMAL(9,4) = 0.16;       -- mismo criterio que PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql
DECLARE @condicionPago INT = 4;             -- CRÉDITO -- mismo catálogo/criterio que esa plantilla
DECLARE @diasEntrega INT = 15;
DECLARE @entrega DATETIME = DATEADD(DAY, @diasEntrega, @ahora);

-- ---- Tablas de apoyo para "número a letras" (declaradas UNA vez, reusadas en cada vuelta
-- del WHILE -- el algoritmo en sí, copiado de PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql, se vuelve
-- a correr por cada OC porque el @total cambia por proveedor). ----
DECLARE @centenas TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @centenas VALUES (0,''),(1,'CIENTO'),(2,'DOSCIENTOS'),(3,'TRESCIENTOS'),(4,'CUATROCIENTOS'),(5,'QUINIENTOS'),(6,'SEISCIENTOS'),(7,'SETECIENTOS'),(8,'OCHOCIENTOS'),(9,'NOVECIENTOS');
DECLARE @unidades TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @unidades VALUES (0,''),(1,'UNO'),(2,'DOS'),(3,'TRES'),(4,'CUATRO'),(5,'CINCO'),(6,'SEIS'),(7,'SIETE'),(8,'OCHO'),(9,'NUEVE'),(10,'DIEZ'),
(11,'ONCE'),(12,'DOCE'),(13,'TRECE'),(14,'CATORCE'),(15,'QUINCE'),(16,'DIECISEIS'),(17,'DIECISIETE'),(18,'DIECIOCHO'),(19,'DIECINUEVE'),(20,'VEINTE');
DECLARE @decenas TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @decenas VALUES (2,'VEINTE'),(3,'TREINTA'),(4,'CUARENTA'),(5,'CINCUENTA'),(6,'SESENTA'),(7,'SETENTA'),(8,'OCHENTA'),(9,'NOVENTA');
DECLARE @veinti TABLE (n INT PRIMARY KEY, t NVARCHAR(20));
INSERT INTO @veinti VALUES (1,'VEINTIUNO'),(2,'VEINTIDOS'),(3,'VEINTITRES'),(4,'VEINTICUATRO'),(5,'VEINTICINCO'),(6,'VEINTISEIS'),(7,'VEINTISIETE'),(8,'VEINTIOCHO'),(9,'VEINTINUEVE');

-- ═══════════════════ RESULTADO (una fila por proveedor, generada o ya existente) ═══════════════════
DECLARE @generadas TABLE (DocumentID INT, BusinessEntityID INT, Folio NVARCHAR(20), YaExistia BIT);

-- ⚠️ IMPORTANTE (encontrado corriendo la prueba real en el sandbox, no es teórico): las tablas
-- que se llenan DENTRO del loop se declaran UNA sola vez AQUÍ AFUERA y se VACÍAN con DELETE al
-- inicio de cada vuelta -- NO se declaran dentro del WHILE. Se probó declararlas dentro del
-- loop (`DECLARE @itemsProv TABLE (...)` en cada vuelta) y el motor NO las reinicia de forma
-- confiable entre iteraciones: la 2a vuelta terminó reusando el DocumentID de la 1a (violación
-- de PK en docDocumentExt al intentar insertar la misma llave 2 veces) porque `SELECT TOP 1 ...
-- FROM @doc` sin ORDER BY recogió la fila vieja en vez de la nueva. La corrección real fue 2
-- cosas: (1) usar SCOPE_IDENTITY() en vez de OUTPUT INTO una tabla acumulable para @docId, y
-- (2) declarar @itemsProv/@itemsIns una sola vez antes del loop y limpiarlas con DELETE, nunca
-- volver a declararlas dentro.
DECLARE @itemsProv TABLE (ProductID INT, Quantity DECIMAL(18,4), UnitPrice DECIMAL(18,2), ProductName NVARCHAR(200), ProductKey NVARCHAR(50), Unit NVARCHAR(20), ClaveUnidad NVARCHAR(10), TaxTypeID INT);
DECLARE @itemsIns TABLE (DocumentItemID INT, ProductID INT, Quantity DECIMAL(18,4), UnitPrice DECIMAL(18,2), Total DECIMAL(18,2), TaxAmount DECIMAL(18,2));

-- Loop set-based por proveedor (NO es un DECLARE CURSOR -- ver antipatrón #5 documentado en
-- entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md; aquí el loop es sobre un puñado de
-- PROVEEDORES por Requisición, no sobre filas de kardex/series, así que no hay la condición de
-- carrera que sí aplicaría a un consecutivo compartido).
DECLARE @i INT = 1;
DECLARE @n INT = (SELECT COUNT(*) FROM @proveedores);
WHILE @i <= @n
BEGIN
    DECLARE @proveedorID INT = (SELECT ProveedorID FROM @proveedores WHERE RowNum = @i);

    -- ═══════════ DEDUP: si ya existe una OC de este proveedor derivada de esta Requisición, sáltala ═══════════
    IF EXISTS (
        SELECT 1 FROM docDocument oc
        WHERE oc.SourceDocumentID = @requisicionID AND oc.BusinessEntityID = @proveedorID
          AND oc.ModuleID = 183 AND oc.DeletedOn IS NULL AND oc.CancelledOn IS NULL
    )
    BEGIN
        INSERT INTO @generadas (DocumentID, BusinessEntityID, Folio, YaExistia)
        SELECT TOP 1 DocumentID, BusinessEntityID, Folio, 1
        FROM docDocument
        WHERE SourceDocumentID = @requisicionID AND BusinessEntityID = @proveedorID
          AND ModuleID = 183 AND DeletedOn IS NULL AND CancelledOn IS NULL;

        SET @i = @i + 1;
        CONTINUE;
    END

    -- ═══════════ Renglones de ESTE proveedor únicamente ═══════════
    DELETE FROM @itemsProv;
    DELETE FROM @itemsIns;
    INSERT INTO @itemsProv (ProductID, Quantity, UnitPrice, ProductName, ProductKey, Unit, ClaveUnidad, TaxTypeID)
    SELECT di.ProductID, di.Quantity, ISNULL(ps.CostPrice, 0), p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad, ISNULL(p.TaxTypeID, 0)
    FROM docDocumentItem di
    INNER JOIN docDocument d ON d.DocumentID = di.DocumentID
    INNER JOIN orgProduct p ON p.ProductID = di.ProductID
    OUTER APPLY (SELECT TOP 1 SupplierID FROM orgSupplier WHERE BusinessEntityID = @proveedorID) s
    LEFT JOIN orgProductSupplier ps ON ps.ProductID = di.ProductID AND ps.SupplierID = s.SupplierID
    WHERE di.DocumentID = @requisicionID
      AND COALESCE(NULLIF(di.SupplierBusinessEntityID, 0), d.BusinessEntityID) = @proveedorID;

    DECLARE @subtotal DECIMAL(18,2) = (SELECT ISNULL(SUM(Quantity * UnitPrice), 0) FROM @itemsProv);
    DECLARE @iva DECIMAL(18,2) = ROUND(@subtotal * @tasaIva, 2);
    DECLARE @total DECIMAL(18,2) = @subtotal + @iva;
    DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 183), 0) + 1;

    -- ---- Número a letras (mismo algoritmo que PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql) ----
    DECLARE @entero BIGINT = FLOOR(@total);
    DECLARE @centavos INT = ROUND((@total - @entero) * 100, 0);
    DECLARE @miles BIGINT = (@entero / 1000) % 1000;
    DECLARE @resto BIGINT = @entero % 1000;
    DECLARE @millones BIGINT = @entero / 1000000;

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

    -- ═══════════ 1. docDocument (cabecera) -- SourceDocumentID=@requisicionID es la llave real ═══════════
    -- SCOPE_IDENTITY() en vez de OUTPUT INTO una tabla acumulable -- ver advertencia arriba
    -- (una tabla @doc reusada entre vueltas del loop puede devolver la fila de la vuelta
    -- ANTERIOR con `SELECT TOP 1` sin ORDER BY; SCOPE_IDENTITY() siempre da el identity real
    -- del último INSERT hecho en este scope, sin ambigüedad).
    INSERT INTO docDocument (
        ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
        DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
        LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
        SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalLetter, TotalPaid, Balance, StatusPaidID, StatusDeliveryID,
        CreatedOn, CreatedBy, UserID,
        SourceDocumentID
    )
    VALUES (
        183, 40, 2, 1, @proveedorID,
        @almacenReq, 0, '', CAST(@folio AS NVARCHAR), @ahora, @entrega, @entrega, @ahora, @ahora, @ahora,
        3, 3, 1, @condicionPago, @ahora, 1, 1,
        @subtotal, @subtotal, @total, @iva, @totalLetra, 0, @total, 3, 3,
        @ahora, 0, 0,
        @requisicionID
    );
    DECLARE @docId INT = CAST(SCOPE_IDENTITY() AS INT);

    -- ═══════════ 2. Anclas satélite (idénticas a PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql) ═══════════
    INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
    INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
    INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
    INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, Total, PartialityNumber, CreatedOn, CreatedBy)
    VALUES (@docId, @ahora, 100, 0, 0, 1, @ahora, 0);

    -- ═══════════ 3. Partidas (TODAS las de este proveedor, en un solo INSERT set-based) ═══════════
    INSERT INTO docDocumentItem (
        DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
        ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, Total, LineNumber, MustBeDelivered,
        ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
    )
    OUTPUT INSERTED.DocumentItemID, INSERTED.ProductID, INSERTED.Quantity, INSERTED.UnitPrice, INSERTED.Total, ROUND(INSERTED.Total * @tasaIva, 2)
    INTO @itemsIns (DocumentItemID, ProductID, Quantity, UnitPrice, Total, TaxAmount)
    SELECT
        @docId, ip.Quantity, ip.ProductID, ip.ProductName, ip.ProductKey, ip.Unit, ip.ClaveUnidad,
        '', ip.TaxTypeID, @tasaIva, ip.UnitPrice, 0, ip.Quantity * ip.UnitPrice, ROW_NUMBER() OVER (ORDER BY ip.ProductID), 1,
        1, 1, 1, 1, @ahora
    FROM @itemsProv ip;

    -- ═══════════ 3b. Relación producto-proveedor (registra al proveedor si aún no lo era) ═══════════
    DECLARE @supplierID INT = (SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID = @proveedorID);
    IF @supplierID IS NOT NULL
        INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber)
        SELECT ip.ProductID, @supplierID, ip.UnitPrice, 3, 0
        FROM @itemsProv ip
        WHERE NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID = ip.ProductID AND SupplierID = @supplierID);

    -- ═══════════ 3c. Agenda de entrega (una fila por partida, set-based) ═══════════
    INSERT INTO docDocumentDeliveryAgenda (DocumentID, DocumentItemID, ProductID, Quantity, QtyDelivered, DateDelivery, CreatedOn, CreatedBy)
    SELECT @docId, DocumentItemID, ProductID, Quantity, 0, @entrega, @ahora, 0
    FROM @itemsIns;

    -- ═══════════ 4. Impuestos (IVA 16% simple -- ver advertencia al inicio) ═══════════
    INSERT INTO docDocumentTax (DocumentID, IVA_T, IVA_R, ISR_R, IEPS_T, IEPS_R, Otro, Local_T, Local_R)
    VALUES (@docId, @iva, 0, 0, 0, 0, 0, 0, 0);

    INSERT INTO docDocumentTaxDetail (DocumentID, DocumentItemID, TaxTypeID, TaxItemID, Amount, Retention, IVASobreIEPS, RegionalTaxID, TaxName, TaxTypeName, TaxBase, TaxPerc, TipoFactor)
    SELECT @docId, DocumentItemID, 5, 6, TaxAmount, 0, 0, 0, 'IVA 16.00%', 'IVA', Total, @tasaIva, 'Tasa'
    FROM @itemsIns;

    INSERT INTO docDocumentTaxSum (DocumentID, TaxName1, TaxAmount1, TotalFederal, TotalLocal, TotalOtro)
    VALUES (@docId, 'IVA 16%', @iva, @iva, 0, 0);

    -- ═══════════ 5. Kardex comprometido (Quantity=0, igual que una OC normal -- no mueve inventario) ═══════════
    INSERT INTO orgProductKardex (DateTransaction, DepotID, ProductID, DocumentID, DocumentItemID, Quantity, QuantityToBeDelivered, AmountPrice, Cancelled, ProductImportID, DepotValue, DepotValueAverage, QuantityImport)
    SELECT @ahora, @almacenReq, ProductID, @docId, DocumentItemID, 0, Quantity, UnitPrice, 0, 0, 0, 0, 0
    FROM @itemsIns;

    INSERT INTO @generadas (DocumentID, BusinessEntityID, Folio, YaExistia)
    VALUES (@docId, @proveedorID, CAST(@folio AS NVARCHAR), 0);

    SET @i = @i + 1;
END

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- las OC ya quedaron comprometidas (COMMIT) antes de llegar aquí;
-- un fallo leyendo este SELECT no debe interpretarse como que la transacción falló.
SELECT DocumentID, BusinessEntityID AS ProveedorID, Folio,
       CASE WHEN YaExistia = 1 THEN 'ya existía (no duplicada)' ELSE 'generada en esta corrida' END AS Estado
FROM @generadas
ORDER BY YaExistia, DocumentID;
