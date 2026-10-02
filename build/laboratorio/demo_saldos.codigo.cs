// job: safe-offline
// Datos de demostración para la plantilla «Saldos y estados de cuenta», en el laboratorio BROSLMV_DESARROLLO.
// Crea facturas de cliente y de compra con fechas atrasadas (para llenar todas las franjas de antigüedad) y cobros / pagos
// aplicados con la receta SQL de siete tablas (no existe función nativa; ver puntodeventa/lib/AplicarCobro.ctx).
// Todos llevan «DEMO SALDOS» en el título para encontrarlos en la lista de Comercial.
// Escenario (hoy = día 0). Las facturas vencen a 30 días de su fecha.

if (Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM docDocument WHERE Title LIKE 'DEMO SALDOS%' AND DeletedOn IS NULL")) > 0)
    return "YA EXISTE";

// Cuenta destino/origen de los cobros y pagos (el laboratorio no trae ninguna)
long cuenta = Convert.ToInt64(ctx.Scalar("SELECT ISNULL(MIN(FinancialEntityID),0) FROM orgFinancialEntity"));
if (cuenta == 0)
{
    ctx.NonQuery("INSERT INTO orgFinancialEntity (FinancialEntityName, CreatedOn, CreatedBy) VALUES (N'DEMO SALDOS · Banco', GETDATE(), 1)");
    cuenta = Convert.ToInt64(ctx.Scalar("SELECT MIN(FinancialEntityID) FROM orgFinancialEntity"));
}

int Crear(int modulo, int entidad, int diasAtras, int producto, int cantidad, double precio, string titulo)
{
    int doc = ctx.erp.NuevoDocumento(modulo, 1, entidad);
    ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0, StatusDeliveryID=0 WHERE DocumentID=" + doc);
    int item = ctx.erp.AgregarArticulo(doc, producto, cantidad, precio, -1, 5, 0);
    if (!string.IsNullOrEmpty(ctx.erp.LastError)) throw new Exception("AgregarArticulo (" + titulo + "): " + ctx.erp.LastError);
    ctx.erp.RecalcCompleto(doc);
    ctx.erp.Save(doc);
    double total = Convert.ToDouble(ctx.Scalar("SELECT Total FROM docDocument WHERE DocumentID=" + doc));
    ctx.NonQuery("UPDATE docDocument SET DateDocument=DATEADD(DAY,-" + diasAtras + ",GETDATE()), Title=N'" + titulo + "', Balance=Total, TotalPaid=0, StatusPaidID=3 WHERE DocumentID=" + doc);
    ctx.NonQuery("DELETE FROM docDocumentPaymentAgenda WHERE DocumentID=" + doc);
    ctx.NonQuery("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + doc + ", DATEADD(DAY,30-" + diasAtras + ",GETDATE()), 100, " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 1, GETDATE(), 1)");
    return doc;
}

// Receta de cobro / pago: 248 = cobro a cliente, 247 = pago a proveedor
void Abonar(int doc, bool cobro, double monto, int diasAtras)
{
    var d = ctx.Query("SELECT Total, Balance, TotalPaid, BusinessEntityID, OwnedBusinessEntityID, CurrencyID FROM docDocument WHERE DocumentID=" + doc)[0];
    double total = Convert.ToDouble(d["Total"]), saldo = Convert.ToDouble(d["Balance"]), pagado = Convert.ToDouble(d["TotalPaid"]);
    long be = Convert.ToInt64(d["BusinessEntityID"]), owned = Convert.ToInt64(d["OwnedBusinessEntityID"]); int moneda = Convert.ToInt32(d["CurrencyID"]);
    double aplicado = Math.Min(monto, saldo);
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    int modOp = cobro ? 248 : 247, recip = cobro ? 1 : 2, tipo = cobro ? 31 : 32; string pref = cobro ? "COB" : "PAG";
    var sb = new System.Text.StringBuilder();
    sb.Append("DECLARE @out TABLE(FinancialOperationID BIGINT); DECLARE @outPay TABLE(DocumentPaymentID BIGINT);\nBEGIN TRY BEGIN TRAN;\n");
    sb.Append("DECLARE @lk INT; EXEC @lk = sp_getapplock @Resource='BrosCobroFolio_" + modOp + "_" + pref + "', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;\n");
    sb.Append("DECLARE @folio BIGINT = ISNULL((SELECT MAX(TRY_CONVERT(BIGINT, Folio)) FROM docFinancialOperation WHERE ModuleID=" + modOp + " AND FolioPrefix=N'" + pref + "'),0)+1;\n");
    sb.Append("DECLARE @f DATETIME = DATEADD(DAY,-" + diasAtras + ",GETDATE()); DECLARE @opId BIGINT, @payId BIGINT;\n");
    sb.Append("INSERT INTO docFinancialOperation (ModuleID, DocRecipientID, DocumentTypeID, OwnedBusinessEntityID, BusinessEntityID, DateOperation, FinancialEntityID, Amount, CurrencyID, PaymentMethodID, PartialityNumber, PartialityTotal, DocumentID, FolioPrefix, Folio, CreatedOn, CreatedBy) OUTPUT INSERTED.FinancialOperationID INTO @out ");
    sb.Append("VALUES (" + modOp + "," + recip + "," + tipo + "," + owned + "," + be + ",@f," + cuenta + "," + aplicado.ToString(inv) + "," + moneda + ",3,1,1," + doc + ",N'" + pref + "',CONVERT(NVARCHAR(50),@folio),GETDATE(),1);\n");
    sb.Append("SELECT @opId = FinancialOperationID FROM @out;\n");
    sb.Append("INSERT INTO docDocumentPayment (DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber, SaldoAnterior, SaldoInsoluto) OUTPUT INSERTED.DocumentPaymentID INTO @outPay VALUES (" + doc + ",@opId,@f," + aplicado.ToString(inv) + ",1," + aplicado.ToString(inv) + ",1," + saldo.ToString(inv) + "," + (saldo - aplicado).ToString(inv) + ");\n");
    sb.Append("SELECT @payId = DocumentPaymentID FROM @outPay;\n");
    sb.Append("INSERT INTO docDocumentPaymentEspejo (DocumentPaymentID, DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber) VALUES (@payId," + doc + ",@opId,@f," + aplicado.ToString(inv) + ",1," + aplicado.ToString(inv) + ",1);\n");
    sb.Append("INSERT INTO docBankTransfer (FinancialOperationID, FinancialEntityID, TrackingNumber, CreatedOn, CreatedBy) VALUES (@opId," + cuenta + ",N'DEMO SALDOS',GETDATE(),1);\n");
    double prop = aplicado / total;
    foreach (var t in ctx.Query("SELECT DocumentTaxDetailID, DocumentItemID, TaxTypeID, Amount, TaxBase, TaxPerc, TaxName, TaxTypeName FROM docDocumentTaxDetail WHERE DocumentID=" + doc))
    {
        string item = t["DocumentItemID"] == null ? "NULL" : Convert.ToInt64(t["DocumentItemID"]).ToString();
        sb.Append("INSERT INTO docFinancialOperationTaxDetail (DocumentTaxDetailID, FinancialOperationID, DocumentID, DocumentItemID, Proporcion, Amount, TaxTypeID, TaxName, TaxTypeName, TaxBase, TaxPerc) VALUES ("
            + Convert.ToInt64(t["DocumentTaxDetailID"]) + ",@opId," + doc + "," + item + "," + prop.ToString(inv) + "," + (Convert.ToDouble(t["Amount"]) * prop).ToString(inv) + "," + Convert.ToInt32(t["TaxTypeID"])
            + ",N'" + Convert.ToString(t["TaxName"]).Replace("'", "''") + "',N'" + Convert.ToString(t["TaxTypeName"]).Replace("'", "''") + "'," + (Convert.ToDouble(t["TaxBase"]) * prop).ToString(inv) + "," + Convert.ToDouble(t["TaxPerc"]).ToString(inv) + ");\n");
    }
    double nuevo = saldo - aplicado;
    sb.Append("UPDATE docDocument SET TotalPaid=" + (pagado + aplicado).ToString(inv) + ", Balance=" + nuevo.ToString(inv) + ", StatusPaidID=" + (nuevo <= 0.0001 ? 1 : 2) + ", DateLastPayment=@f WHERE DocumentID=" + doc + ";\n");
    sb.Append("COMMIT TRAN; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRAN; THROW; END CATCH;");
    ctx.NonQuery(sb.ToString());
}

var ids = new List<string>();
double Saldo(int doc) => Convert.ToDouble(ctx.Scalar("SELECT Balance FROM docDocument WHERE DocumentID=" + doc));

// ---- Clientes (módulo 21): 2 = Accesorios de Teléfono, 3 = Autozone, 4 = Imex ----
int f1 = Crear(21, 2, 100, 2, 10, 120.0, "DEMO SALDOS · factura cliente 100 días sin pagar");
int f2 = Crear(21, 2, 40, 3, 20, 85.5, "DEMO SALDOS · factura cliente 40 días, abono del 50%");
Abonar(f2, true, Saldo(f2) / 2, 20);
int f3 = Crear(21, 3, 10, 4, 5, 300.0, "DEMO SALDOS · factura cliente reciente sin pagar");
int f4 = Crear(21, 3, 75, 2, 8, 120.0, "DEMO SALDOS · factura cliente 75 días pagada completa");
Abonar(f4, true, Saldo(f4), 50);
int f5 = Crear(21, 4, 200, 3, 30, 85.5, "DEMO SALDOS · factura cliente 200 días sin pagar");
int f6 = Crear(21, 4, 35, 4, 4, 300.0, "DEMO SALDOS · factura cliente 35 días, abono del 30%");
Abonar(f6, true, Saldo(f6) * 0.3, 5);

// ---- Proveedores (módulo 152): 5 = Ferretería Plascencia, 7 = Informática UG, 8 = Transportes ----
int p1 = Crear(152, 5, 45, 2, 10, 120.0, "DEMO SALDOS · factura compra 45 días sin pagar");
int p2 = Crear(152, 7, 20, 3, 20, 85.5, "DEMO SALDOS · factura compra 20 días, pago del 60%");
Abonar(p2, false, Saldo(p2) * 0.6, 3);
int p3 = Crear(152, 8, 120, 4, 6, 300.0, "DEMO SALDOS · factura compra 120 días pagada completa");
Abonar(p3, false, Saldo(p3), 100);
int p4 = Crear(152, 5, 80, 4, 3, 300.0, "DEMO SALDOS · factura compra 80 días sin pagar");

return "Clientes: " + string.Join(",", new[] { f1, f2, f3, f4, f5, f6 }) + " | Proveedores: " + string.Join(",", new[] { p1, p2, p3, p4 }) + " | cuenta=" + cuenta;
