# lang: python
#
# PLANTILLA_EJEMPLO_DASHBOARD_VENTAS_PYTHON.py
# Dashboard de ventas del mes: 3 tarjetas resumen + top 10 productos, renderizado en
# HTML/CSS real dentro de Comercial (ctx.show_html, WebView2). Todo con datos reales de
# la empresa activa -- sin exportar a Excel, sin abrir el navegador externo.
#
# AJUSTA ModuleID a tu instalación: 21 es el ModuleID típico de "Facturas de Venta" en
# muchas instalaciones de Comercial, pero puede variar -- verifica el tuyo en
# engModule (SELECT ModuleID, ModuleName FROM engModule WHERE ModuleName LIKE '%actura%Venta%').

from broslmv import ctx

MODULE_ID_FACTURA_VENTA = 21

resumen = ctx.query("""
    SELECT COUNT(DISTINCT d.DocumentID) AS Documentos,
           ISNULL(SUM(d.Total), 0)      AS TotalVentas
    FROM docDocument d
    WHERE d.ModuleID = @mod AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
      AND d.DateDocument >= DATEADD(DAY, 1 - DAY(GETDATE()), CAST(GETDATE() AS date))
""", {"mod": MODULE_ID_FACTURA_VENTA})[0]

top_productos = ctx.query("""
    SELECT TOP 10 p.ProductName, SUM(i.Quantity) AS Cantidad, SUM(i.Quantity * i.UnitPrice) AS Importe
    FROM docDocumentItem i
    INNER JOIN docDocument d ON d.DocumentID = i.DocumentID
    INNER JOIN orgProduct p ON p.ProductID = i.ProductID
    WHERE d.ModuleID = @mod AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND i.DeletedOn IS NULL
      AND d.DateDocument >= DATEADD(DAY, 1 - DAY(GETDATE()), CAST(GETDATE() AS date))
    GROUP BY p.ProductName
    ORDER BY SUM(i.Quantity * i.UnitPrice) DESC
""", {"mod": MODULE_ID_FACTURA_VENTA})

documentos = int(resumen["Documentos"] or 0)
total = float(resumen["TotalVentas"] or 0)
ticket_prom = (total / documentos) if documentos > 0 else 0.0

filas_top = "".join(
    "<tr><td>{n}</td><td style='text-align:right'>{c:.2f}</td>"
    "<td style='text-align:right'>${i:,.2f}</td></tr>".format(
        n=p["ProductName"], c=float(p["Cantidad"] or 0), i=float(p["Importe"] or 0))
    for p in top_productos
) or "<tr><td colspan='3' style='color:#5E7CA0'>Sin ventas registradas este mes.</td></tr>"

html = """
<html><head><style>
  body {{ font-family: Segoe UI, sans-serif; background: #0B1E33; color: #E6EDF5; padding: 24px; margin: 0; }}
  h1 {{ font-size: 18px; color: #7FB0FF; margin: 0 0 18px; }}
  .tarjetas {{ display: flex; gap: 14px; margin-bottom: 24px; }}
  .tarjeta {{ flex: 1; background: #122B47; border: 1px solid #1D3A5C; border-radius: 10px; padding: 16px; }}
  .tarjeta .valor {{ font-size: 26px; font-weight: 700; color: #fff; margin-top: 4px; }}
  .tarjeta .etiqueta {{ font-size: 12px; color: #9FB6D1; text-transform: uppercase; letter-spacing: .04em; }}
  table {{ width: 100%; border-collapse: collapse; }}
  th, td {{ padding: 9px 10px; border-bottom: 1px solid #1D3A5C; font-size: 13px; }}
  th {{ text-align: left; color: #9FB6D1; font-weight: 600; text-transform: uppercase; font-size: 11px; }}
</style></head><body>
  <h1>Ventas del mes — {empresa}</h1>
  <div class="tarjetas">
    <div class="tarjeta"><div class="etiqueta">Documentos</div><div class="valor">{documentos}</div></div>
    <div class="tarjeta"><div class="etiqueta">Total vendido</div><div class="valor">${total:,.2f}</div></div>
    <div class="tarjeta"><div class="etiqueta">Ticket promedio</div><div class="valor">${ticket:,.2f}</div></div>
  </div>
  <h1 style="font-size:14px">Top 10 productos</h1>
  <table>
    <tr><th>Producto</th><th>Cantidad</th><th>Importe</th></tr>
    {filas}
  </table>
</body></html>
""".format(empresa=ctx.empresa, documentos=documentos, total=total, ticket=ticket_prom, filas=filas_top)

ctx.show_html(html, title="Dashboard de Ventas", width=760, height=680)
