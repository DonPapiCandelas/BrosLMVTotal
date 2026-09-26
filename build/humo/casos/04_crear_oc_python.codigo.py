# lang: python
# job: safe-offline
# Humo T4.1 #4: crear OC via ctx.erp de ESCRITURA headless, canal Python (contraparte del
# caso 3, que ya probo lo mismo en C#) -- confirma que el mecanismo tambien funciona por el
# canal completo BrosLMV.Host.exe + UiPump, no solo directo en el proceso del Runner.
# Reusa el mismo proveedor/producto de humo que los casos 2-3 (idempotentes por Key).
# NO hay equivalente a ctx.Msg bloqueante en Python vía este canal -- aun asi, se evita
# cualquier llamada que muestre UI; el resultado se verifica por SQL desde el .ps1.
from broslmv import ctx

existe_prov = ctx.scalar("SELECT BusinessEntityID FROM orgBusinessEntity WHERE BusinessEntityKey = 'HUMO-PROV-001'")
if existe_prov:
    proveedor_be = int(existe_prov)
else:
    proveedor_be = int(ctx.scalar("""
        INSERT INTO orgBusinessEntity (CommercialName, BusinessEntityKey, OfficialName, CreatedBy, CreatedOn, UserID)
        OUTPUT INSERTED.BusinessEntityID
        VALUES ('Proveedor de humo T4.1', 'HUMO-PROV-001', 'Proveedor de humo T4.1 SA de CV', 0, GETDATE(), 0)
    """))
    ctx.execute("INSERT INTO orgSupplier (BusinessEntityID) VALUES (" + str(proveedor_be) + ")")

prod_id = int(ctx.scalar("SELECT ProductID FROM orgProduct WHERE ProductKey = 'HUMO-PROD-001'"))

depot = 1
doc = ctx.erp.NuevoDocumento(183, depot, proveedor_be)

ctx.execute("""
    UPDATE docDocument SET
        DepotIDFrom=0, PaymentTermID=0,
        DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
    WHERE DocumentID=""" + str(doc))

ctx.erp.AgregarArticulo(doc, prod_id, 5, 250, 200)

ctx.execute("UPDATE docDocumentItem SET TaxTypeID=5 WHERE DocumentID=" + str(doc) + " AND DeletedOn IS NULL")

ctx.erp.RecalcCompleto(doc)
ctx.erp.AffectStockNEW(doc)
ctx.erp.Save(doc)

result = "OC creada (python): doc=" + str(doc)
