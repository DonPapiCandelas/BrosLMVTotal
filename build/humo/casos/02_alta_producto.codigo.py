# lang: python
# job: safe-offline
# Humo T4.1 #2: alta de producto headless (Python), via SQL puro (orgProduct) -- NO usa
# ctx.erp (NuevoDocumento/AgregarArticulo/Save requieren logica de negocio en vivo que aun
# no se valido headless; ver CHANGELOG "ctx.erp headless" para el porque). Idempotente por
# ProductKey.
from broslmv import ctx

existente = ctx.scalar("SELECT ProductID FROM orgProduct WHERE ProductKey = 'HUMO-PROD-001'")
if existente:
    prod_id = int(existente)
else:
    prod_id = int(ctx.scalar("""
        INSERT INTO orgProduct (ProductKey, ProductName, ProductTypeID, TaxTypeID,
            Unit, ClaveUnidad, ObjetoImpuesto, ClaveProdServ,
            ProductBuy, ProductSale, UseLot, UseSerialNumber,
            CreatedBy, CreatedOn, UserID)
        OUTPUT INSERTED.ProductID
        VALUES ('HUMO-PROD-001', 'Producto de humo T4.1', 1, 2,
            'PZA', 'H87', '02', '43231500',
            1, 1, 0, 0,
            0, GETDATE(), 0)
    """))
    ctx.execute("INSERT INTO orgProductPicture (ProductID) VALUES (" + str(prod_id) + ")")
    ctx.execute("INSERT INTO orgProductUnitConversion (ProductID) VALUES (" + str(prod_id) + ")")

result = "ProductID=" + str(prod_id)
