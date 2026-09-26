// job: safe-offline
// Companion headless de PLANTILLA_RECEPCION_COMPRA_FORMS_SQL_PURO_CSHARP.ctx para el caso de
// humo #30. NO es la plantilla real (ventana WinForms modeless con grid de OC pendientes,
// captura de lote/numero de serie -- no automatizable headless, mismo limite documentado en
// 25_adjuntos_documento.codigo.cs). Reproduce LITERALMENTE el batch de T-SQL que arma
// CrearRecepcionSqlPuro() en el .ctx real para UNA sola partida de UNA sola OC de origen, sin
// lote ni numero de serie (producto sin UseLot/UseSerialNumber), con parametros fijos --
// seguido EXACTAMENTE de la misma secuencia de recalculo con XEngine que el .ctx real ejecuta
// tras el COMMIT: ctx.erp.RecalcCompleto(doc) + ctx.erp.AffectStockNEW(doc) +
// ctx.erp.UpdateStatusDelivery(doc) -- el cambio de v2.73.0 que esta plantilla capturaba sin
// ninguna prueba automatizada.
//
// Recibe --sourceOC / --sourceItemId / --productoID / --cantidad / --precio como parametros
// del boton via zzBrosScript.Codigo (el .ps1 los sustituye antes de registrar el boton, mismo
// patron que usa 23_recepcion_compra_consolidada_sql_puro.ps1 con la plantilla .sql).
int sourceOC = __SOURCE_OC__;
int sourceItemId = __SOURCE_ITEM_ID__;
int dep = 1, productoID = __PRODUCTO_ID__, taxTypeId = 5;
double cantidad = __CANTIDAD__, precio = __PRECIO__, descPerc = 0;
var ci = System.Globalization.CultureInfo.InvariantCulture;

int be = Convert.ToInt32(ctx.Scalar("SELECT BusinessEntityID FROM docDocument WHERE DocumentID=" + sourceOC));
long supplierID = Convert.ToInt64(ctx.Scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + be));
double importe = cantidad * precio;

var sb = new System.Text.StringBuilder();
sb.Append(@"
BEGIN TRY
BEGIN TRAN;
DECLARE @doc TABLE (DocumentID INT);
DECLARE @item TABLE (DocumentItemID INT);
DECLARE @itemId INT;
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 184), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, SourceDocumentID, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized, ExportID,
    SubTotal, SubTotalWithDiscount, Total, TotalTax, TotalCost, TotalLetter, TotalPaid, Balance, StatusPaidID, StatusDeliveryID,
    Comments, CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    184, 3, 2, 1, " + be + @",
    " + dep + @", 0, " + sourceOC + @", '', CAST(@folio AS NVARCHAR), GETDATE(), GETDATE(), GETDATE(), GETDATE(), GETDATE(), GETDATE(),
    3, 3, 1, 0, @ahora, 1, 1,
    0, 0, 0, 0, 0, N'PENDIENTE', 0, 0, 3, 3,
    N'', @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 1, @ahora, 0);
INSERT INTO docDocumentTax (DocumentID, IVA_T, IVA_R, ISR_R, IEPS_T, IEPS_R, Otro, Local_T, Local_R)
VALUES (@docId, 0, 0, 0, 0, 0, 0, 0, 0);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, UnitPrice, CostPrice, DiscountPerc, Total, LineNumber, MustBeDelivered,
    ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, CoefUnit, DeliverDocumentItemID, DateItem
)
OUTPUT INSERTED.DocumentItemID INTO @item
SELECT
    @docId, " + cantidad.ToString(ci) + @", p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', " + taxTypeId + @", 0.16, " + precio.ToString(ci) + @", " + precio.ToString(ci) + @", " + descPerc.ToString(ci) + @", " + importe.ToString(ci) + @", 1, 1,
    1, 1, 1, 1, " + sourceItemId + @", @ahora
FROM orgProduct p WHERE p.ProductID = " + productoID + @";
SET @itemId = (SELECT MAX(DocumentItemID) FROM @item);
IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + productoID + @" AND SupplierID=" + supplierID + @")
    INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + productoID + @", " + supplierID + @", " + precio.ToString(ci) + @", 3, 0);
COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH
SELECT @docId AS DocumentID, @folio AS Folio;");

var filas = ctx.Query(sb.ToString());
if (filas.Count == 0) throw new Exception("El batch no devolvio el documento creado.");
int doc = Convert.ToInt32(filas[0]["DocumentID"]);

// Misma secuencia que el .ctx real tras el COMMIT (docs/INVESTIGACION_XENGINE_SQL_PURO.md).
ctx.erp.RecalcCompleto(doc);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("RecalcCompleto: " + ctx.erp.LastError);
ctx.erp.AffectStockNEW(doc);
if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AffectStockNEW: " + ctx.erp.LastError);
try { ctx.erp.UpdateStatusDelivery(doc); } catch { }

return "doc=" + doc;
