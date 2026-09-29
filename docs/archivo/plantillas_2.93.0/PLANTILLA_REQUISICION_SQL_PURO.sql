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
-- Cómo se llenan los datos (v2.77.0+): los 4 parámetros (@proveedorBE, @almacen,
-- @productoID, @cantidad) ahora se capturan con un formulario automático -- cada uno usa el
-- token tipado {DATOS:Tabla.Columna:*} (motor v2.75.0/2.76.0, ver CHANGELOG). Antes de correr
-- el script, ResolverFormularioTokens detecta los 4 tokens, consulta el tipo real de cada
-- columna en INFORMATION_SCHEMA.COLUMNS y muestra UN formulario con un campo por token (todos
-- numéricos aquí: BusinessEntityID/DepotID/ProductID son INT, Quantity es DECIMAL). @cantidad
-- no corresponde a una columna de catálogo -- se ancla a docDocumentItem.Quantity (mismo tipo
-- DECIMAL(18,4) que usa la partida real) solo para inferir el tipo de campo correcto, no para
-- leer un valor existente de esa tabla. Ya no hace falta editar este archivo a mano antes de
-- correrlo -- sigue sin haber una ventana con validaciones de negocio (combos con nombres,
-- etc.); para eso existen las otras 4 variantes (Forms/WebView2) de esta misma plantilla.
--
-- Folio: se calcula como MAX(Folio)+1 para el módulo 1040 -- mismo criterio que usa
-- ctx.erp.GetNextFolio() internamente (consecutivo simple, sin prefijo para este módulo).
-- No es seguro contra 2 personas creando un documento en el MISMO instante (condición de
-- carrera real) -- ctx.erp SÍ maneja eso internamente; SQL puro no.
--
-- Atomicidad: SÍ (desde esta versión). Todo el bloque que crea cabecera + anclas satélite +
-- partida + relación proveedor corre dentro de BEGIN TRAN/COMMIT TRAN con BEGIN TRY/CATCH y
-- ROLLBACK si algo falla a medio camino -- antes, un fallo intermedio dejaba un docDocument
-- "fantasma" (cabecera sin partida o sin anclas). Las validaciones iniciales (guard clauses)
-- siguen corriendo ANTES de abrir la transacción, sin necesidad de ella.

-- ═══════════════════ PARÁMETROS (se capturan con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @proveedorBE INT = {DATOS:orgBusinessEntity.BusinessEntityID:Proveedor (BusinessEntityID):*};      -- BusinessEntityID del proveedor (orgBusinessEntity)
DECLARE @almacen     INT = {DATOS:orgDepot.DepotID:Almacén (DepotID):*};      -- DepotID (orgDepot)
DECLARE @productoID  INT = {DATOS:orgProduct.ProductID:Producto (ProductID):*};      -- ProductID (orgProduct)
DECLARE @cantidad    DECIMAL(18,4) = {DATOS:docDocumentItem.Quantity:Cantidad:*};

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

-- ═══════════════════ TRANSACCIÓN (atomicidad real) ═══════════════════
-- Todo lo que sigue (cabecera + anclas satélite + partida + relación proveedor) se ejecuta
-- dentro de UNA sola transacción. Antes de esto, un fallo a mitad de los INSERT dejaba un
-- docDocument "fantasma" (cabecera sin partida, o sin anclas satélite) -- exactamente el bug
-- que se encontró en producción de un cliente real, con años de documentos incompletos
-- acumulados. THROW (sin argumentos, requiere SQL Server 2012+) repropaga el error original
-- después del ROLLBACK, para que el llamador (ctx.EjecutarSql) siga viendo el mensaje real.
BEGIN TRY
BEGIN TRAN;

-- ═══════════════════ FOLIO ═══════════════════
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 1040), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();

-- ═══════════════════ 1. docDocument (cabecera) ═══════════════════
-- Perfil validado en docs/REQUISICION_SOLICITUD_COMPRA.md: DepotIDFrom=0, UserID=0,
-- PaymentTermID=0, CampaignID/CostCenterID/ProjectID=0 (no NULL -- confirmado en el
-- snapshot real: esta versión de Comercial usa 0, no NULL, en estos 3 campos).
-- DocRecipientID: código de ROL fijo (2=proveedor), NO el BusinessEntityID real -- confirmado
-- en Scripting.cs NuevoDocumento() (lo resuelve de engModuleParameter, independiente del
-- proveedor) y en EXP-DOC-solicitud_compra_001 ("DocRecipientID = 2"). Usar @proveedorBE aquí
-- sería un bug latente si el proveedor cambia de ID.
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
    1040, 49, 2, 1, @proveedorBE,
    @almacen, 0, '', CAST(@folio AS NVARCHAR), @ahora, @ahora, @ahora, @ahora, @ahora,
    3, 3, 1, 0, @ahora, 1,
    1, 'CERO PESOS 00/100 M.N.', @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);

-- ═══════════════════ 2. Anclas satélite ═══════════════════
-- docDocumentExt SÍ aplica -- su columna que guarda el DocumentID se llama IDExtra, no
-- "DocumentID" (por eso una revisión superficial de columnas la pasaba por alto). Mismo
-- INSERT que usa el propio addon en Scripting.cs NuevoDocumento().
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
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
-- TaxPerc SÍ se llena (aunque el precio sea 0 y por lo tanto el impuesto también) -- el
-- profiler real de una Solicitud confirma TaxPerc con la tasa real del TaxTypeID del
-- producto, no 0.
DECLARE @item TABLE (DocumentItemID INT);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, LineNumber, MustBeDelivered, ApplyGlobalDiscount,
    DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, @cantidad, p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', ISNULL(p.TaxTypeID, 0), ISNULL(tp.IVA_Perc, 0), 1, 1, 1,
    1, 1, 1, @ahora
FROM orgProduct p LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = p.TaxTypeID
WHERE p.ProductID = @productoID;
DECLARE @itemId INT = (SELECT TOP 1 DocumentItemID FROM @item);

-- ═══════════════════ 4. Relación producto-proveedor ═══════════════════
-- Igual que las versiones Forms/WebView2 (confirmado como efecto lateral obligatorio de
-- la Solicitud en EXP-DOC-solicitud_compra_001): registra al proveedor como fuente de
-- este producto si todavía no lo era.
DECLARE @supplierID INT = (SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID = @proveedorBE);
IF @supplierID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID = @productoID AND SupplierID = @supplierID)
    INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber)
    VALUES (@productoID, @supplierID, 0, 3, 0);

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- El SELECT de resultado queda FUERA del TRY a propósito: si algo falla aquí (por ejemplo un
-- problema de red leyendo el resultado) no debe interpretarse como que la transacción falló --
-- el documento ya quedó comprometido (COMMIT) antes de llegar a esta línea.
SELECT 'Requisición (SQL puro) creada: doc=' + CAST(@docId AS VARCHAR) + ', folio=' + CAST(@folio AS VARCHAR) AS Resultado;
