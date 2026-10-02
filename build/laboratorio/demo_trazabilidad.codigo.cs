// job: safe-offline
// Datos de demostración para la plantilla «Trazabilidad del documento», en el laboratorio BROSLMV_DESARROLLO.
// Crea TRES órdenes de compra del mismo proveedor y UNA factura de compra consolidada que las factura juntas:
//   · cada partida de la factura apunta a su partida de origen (SourceDocumentItemID: lo que hace el sistema),
//   · el encabezado de la factura guarda UN solo SourceDocumentID (la primera OC: el sistema no puede guardar más),
//   · y cada OC guarda DestinationDocumentID = la factura (lo que hacen los scripts y las personas cuando de varios orígenes sale un destino).
// Todos llevan «DEMO TRAZ» en el título para encontrarlos en la lista de Comercial. Se crea con las rutas nativas (NuevoDocumento/AgregarArticulo/RecalcCompleto/Save).

int proveedor = 5, almacen = 1;
int[] productos = { 2, 3, 4 };
int[] cantidades = { 10, 20, 5 };
decimal[] precios = { 120m, 85.5m, 300m };

if (Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM docDocument WHERE Title LIKE 'DEMO TRAZ%' AND DeletedOn IS NULL")) > 0)
    return "YA EXISTE";

var ocs = new List<int>();
var partidasOC = new List<long>();
for (int i = 0; i < 3; i++)
{
    int oc = ctx.erp.NuevoDocumento(183, almacen, proveedor);
    ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, PaymentTermID=0, DateDelivery=GETDATE(), DateDocDelivery=GETDATE() WHERE DocumentID=" + oc);
    ctx.erp.AgregarArticulo(oc, productos[i], cantidades[i], (double)precios[i], 100);
    if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo OC " + (i + 1) + ": " + ctx.erp.LastError);
    ctx.NonQuery("UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID=" + oc + " AND DeletedOn IS NULL");
    ctx.erp.RecalcCompleto(oc);
    ctx.erp.Save(oc);
    ocs.Add(oc);
    partidasOC.Add(Convert.ToInt64(ctx.Scalar("SELECT TOP 1 DocumentItemID FROM docDocumentItem WHERE DocumentID=" + oc + " AND DeletedOn IS NULL ORDER BY DocumentItemID")));
}

int fact = ctx.erp.NuevoDocumento(152, almacen, proveedor);
ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=4, StatusDeliveryID=0 WHERE DocumentID=" + fact);
for (int i = 0; i < 3; i++)
{
    int itemId = ctx.erp.AgregarArticulo(fact, productos[i], cantidades[i], (double)precios[i], -1, 5, 0);
    if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo factura " + (i + 1) + ": " + ctx.erp.LastError);
    ctx.NonQuery("UPDATE docDocumentItem SET SourceDocumentItemID=" + partidasOC[i] + " WHERE DocumentItemID=" + itemId);
}
ctx.erp.RecalcCompleto(fact);
ctx.erp.Save(fact);

// Agenda de pago 50% hoy / 50% a 3 meses (condición 4), como la deja la receta de factura de compra
try
{
    double total = Convert.ToDouble(ctx.Scalar("SELECT Total FROM docDocument WHERE DocumentID=" + fact));
    ctx.NonQuery("DELETE FROM docDocumentPaymentAgenda WHERE DocumentID=" + fact);
    ctx.NonQuery("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + fact + ", GETDATE(), 50, " + Math.Round(total / 2, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 1, GETDATE(), 0)");
    ctx.NonQuery("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + fact + ", DATEADD(MONTH,3,GETDATE()), 50, " + Math.Round(total - Math.Round(total / 2, 2), 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 2, GETDATE(), 0)");
}
catch { }

// Vínculos de encabezado y títulos
ctx.NonQuery("UPDATE docDocument SET SourceDocumentID=" + ocs[0] + ", Title='DEMO TRAZ · factura consolidada de 3 OC' WHERE DocumentID=" + fact);
for (int i = 0; i < 3; i++)
    ctx.NonQuery("UPDATE docDocument SET DestinationDocumentID=" + fact + ", Title='DEMO TRAZ · OC " + (i + 1) + " de 3 (una sola factura)' WHERE DocumentID=" + ocs[i]);

return "OCs=" + string.Join(",", ocs) + " factura=" + fact;
