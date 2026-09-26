// job: safe-offline
// Humo T4.1 #3: crear OC via ctx.erp (escritura headless -- primera prueba real de esto,
// solo en el sandbox ComercialSP, nunca contra una empresa de cliente real). Sigue la
// receta de MANUAL.md 7.5, simplificada (sin UpdateDocumentPaidInfo/RefreshGrid, que son
// para refrescar UI/agenda de pago -- no aplican sin Comercial abierto).
// NO usa ctx.Msg: bloquea con MessageBox.Show y no hay nadie headless para cerrarlo.
// Cada corrida crea una OC NUEVA (no es idempotente como los casos 1-2) -- es lo esperado
// de un caso de humo que valida creacion, no una operacion de solo-lectura.

object existeProv = ctx.Scalar("SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey = 'HUMO-PROV-001'");
int proveedorBE;
if (existeProv != null) {
    proveedorBE = (int)(long)existeProv;
} else {
    proveedorBE = (int)(long)ctx.Scalar(@"
        INSERT INTO orgBusinessEntity (CommercialName, BusinessEntityKey, OfficialName, CreatedBy, CreatedOn, UserID)
        OUTPUT INSERTED.BusinessEntityID
        VALUES ('Proveedor de humo T4.1', 'HUMO-PROV-001', 'Proveedor de humo T4.1 SA de CV', 0, GETDATE(), 0)");
    ctx.NonQuery("INSERT INTO orgSupplier (BusinessEntityID) VALUES (" + proveedorBE + ")");
}

int prodId = (int)(long)ctx.Scalar("SELECT ProductID FROM orgProduct WHERE ProductKey = 'HUMO-PROD-001'");

int depot = 1;
int doc = ctx.erp.NuevoDocumento(183, depot, proveedorBE);

ctx.NonQuery($@"
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID=0,
        DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
    WHERE DocumentID={doc}");

ctx.erp.AgregarArticulo(doc, prodId, 5, 250, 200);

ctx.NonQuery($"UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID={doc} AND DeletedOn IS NULL");

ctx.erp.RecalcCompleto(doc);
ctx.erp.AffectStockNEW(doc);
ctx.erp.Save(doc);

return "OC creada: doc=" + doc;
