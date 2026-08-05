// job: safe-offline
// Companion headless de PLANTILLA_REQUISICION_FORMS_SQL_PURO_CSHARP.ctx para el caso de humo
// #28. NO es la plantilla real (esa abre una ventana WinForms modeless con buscador de
// proveedor/producto y un grid de partidas -- no automatizable headless con las herramientas
// de este proyecto, igual que el resto del catalogo de plantillas Forms C#, ver el mismo
// limite documentado en 25_adjuntos_documento.codigo.cs). Reproduce LITERALMENTE el batch de
// T-SQL que arma CrearRequisicionSqlPuro() en el .ctx real (misma cabecera, misma partida,
// mismas anclas satelite) para UNA sola partida con parametros fijos, seguido EXACTAMENTE de
// la misma secuencia de recalculo con XEngine que el .ctx real ejecuta tras el COMMIT:
// ctx.erp.RecalcCompleto(doc) + ctx.erp.UpdateStatusDelivery(doc) -- el cambio de v2.73.0 que
// esta plantilla capturaba sin ninguna prueba automatizada hasta ahora.
//
// Parametros equivalentes a "capturar en la ventana": proveedor BusinessEntityID=2, almacen
// DepotID=1, condicion de pago PaymentTermID=1, producto ProductID=1, cantidad=2.

int be = 2, dep = 1, condId = 1, productoID = 1;
double cantidad = 2;
long supplierID = Convert.ToInt64(ctx.Scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + be));

var sb = new System.Text.StringBuilder();
sb.Append(@"
BEGIN TRY
BEGIN TRAN;
DECLARE @doc TABLE (DocumentID INT);
DECLARE @folio INT = ISNULL((SELECT MAX(TRY_CAST(Folio AS INT)) FROM docDocument WHERE ModuleID = 1040), 0) + 1;
DECLARE @ahora DATETIME = GETDATE();
INSERT INTO docDocument (
    ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, BusinessEntityID,
    DepotID, DepotIDFrom, FolioPrefix, Folio, DateDocument, DateDocDelivery, DateFrom, DateTo, DateCost,
    LanguageID, CurrencyID, Rate, PaymentTermID, DateLastPayment, MustBeSynchronized,
    ExportID, TotalLetter, Comments, CreatedOn, CreatedBy, UserID
)
OUTPUT INSERTED.DocumentID INTO @doc
VALUES (
    1040, 49, 2, 1, " + be + @",
    " + dep + @", 0, '', CAST(@folio AS NVARCHAR), GETDATE(), GETDATE(), GETDATE(), GETDATE(), GETDATE(),
    3, 3, 1, " + condId + @", @ahora, 1,
    1, 'CERO PESOS 00/100 M.N.', N'', @ahora, 0, 0
);
DECLARE @docId INT = (SELECT TOP 1 DocumentID FROM @doc);
INSERT INTO docDocumentExt (IDExtra) VALUES (@docId);
INSERT INTO docDocumentExtra (DocumentID) VALUES (@docId);
INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@docId, 0, '4.0');
INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy)
VALUES (@docId, @ahora, 100, 0, 1, @ahora, 0);
INSERT INTO docDocumentItem (
    DocumentID, Quantity, ProductID, Description, ProductKey, Unit, ClaveUnidad,
    ObjetoImpuesto, TaxTypeID, TaxPerc, LineNumber, MustBeDelivered, ApplyGlobalDiscount,
    DeductiblePerc, IsBusinessOperation, CoefUnit, DateItem
)
SELECT
    @docId, " + cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture) + @", p.ProductID, p.ProductName, p.ProductKey, p.Unit, p.ClaveUnidad,
    '', ISNULL(p.TaxTypeID, 0), ISNULL(tp.IVA_Perc, 0), 1, 1, 1,
    1, 1, 1, @ahora
FROM orgProduct p LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = p.TaxTypeID
WHERE p.ProductID = " + productoID + @";
IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + productoID + @" AND SupplierID=" + supplierID + @")
    INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber)
    VALUES (" + productoID + @", " + supplierID + @", 0, 3, 0);
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
try { ctx.erp.UpdateStatusDelivery(doc); } catch { }

return "doc=" + doc;
