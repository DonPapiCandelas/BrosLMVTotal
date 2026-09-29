# lang: python
#
# PLANTILLA_EJEMPLO_CONTEO_GRID_PYTHON.py
# Conteo fisico de un almacen: precarga la existencia de sistema por producto en una
# grilla editable, el usuario captura lo que SI contó, y al final muestra las diferencias.
# No mueve inventario -- es un reporte de comparacion, seguro de correr en produccion.
#
# ESTO ES LO QUE HACE ESPECIAL AL GRID DE ctx.form: comparalo con
# PLANTILLA_EJEMPLO_ORDEN_COMPRA_PYTHON.py (~1500 lineas de DataGridView, columnas,
# eventos de edicion, resize...) -- aqui la captura de una tabla editable completa
# (precargada, con "agregar renglon") es UN SOLO diccionario "grid" dentro de ctx.form().
# Nada de DataGridView a mano, nada de manejar CellEndEdit tu mismo.

from broslmv import ctx

DEPOT_ID = 1  # cambia al almacen que quieras auditar (o agrega un ctx.form previo para elegirlo)

productos = ctx.query("""
    SELECT TOP 100
           p.ProductID, p.ProductKey, p.ProductName,
           ISNULL((SELECT SUM(k.Quantity) FROM orgProductKardex k
                   WHERE k.ProductID = p.ProductID AND k.DepotID = @dep AND k.Cancelled = 0), 0) AS Existencia
    FROM orgProduct p
    WHERE p.DeletedOn IS NULL AND p.TaxTypeID IS NOT NULL AND p.TaxTypeID > 0
    ORDER BY p.ProductName
""", {"dep": DEPOT_ID})

if not productos:
    ctx.msg("No se encontraron productos para ese almacén.")
else:
    filas_iniciales = [
        {
            "producto": p["ProductKey"] + " - " + p["ProductName"],
            "sistema": float(p["Existencia"] or 0),
            "contado": float(p["Existencia"] or 0),  # arranca igual al sistema; el usuario ajusta
        }
        for p in productos
    ]

    r = ctx.form({
        "title": "Conteo físico — Almacén " + str(DEPOT_ID),
        "grid": {
            "columns": [
                {"name": "producto", "caption": "Producto",         "type": "text",    "width": 320, "editable": False},
                {"name": "sistema",  "caption": "Existencia sistema", "type": "decimal", "width": 130, "editable": False},
                {"name": "contado",  "caption": "Conteo físico",      "type": "decimal", "width": 130, "editable": True},
            ],
            "rows": filas_iniciales,
            "allow_add": False,     # aquí no aplica agregar renglones sueltos (son productos del catálogo)
            "allow_delete": False,
        },
        "ok_label": "Generar diferencias",
        "cancel_label": "Cancelar",
        "width": 640,
        "height": 560,
    })

    if not r["submitted"]:
        ctx.log("Conteo cancelado por el usuario.")
    else:
        diferencias = [
            fila for fila in r["grid_rows"]
            if abs(float(fila["contado"]) - float(fila["sistema"])) > 0.0001
        ]

        if not diferencias:
            ctx.msg("Sin diferencias: el conteo coincide con el sistema en todos los productos.")
        else:
            filas_html = "".join(
                "<tr><td>{p}</td><td style='text-align:right'>{s:.2f}</td>"
                "<td style='text-align:right'>{c:.2f}</td>"
                "<td style='text-align:right;color:{color}'>{d:+.2f}</td></tr>".format(
                    p=f["producto"], s=float(f["sistema"]), c=float(f["contado"]),
                    d=float(f["contado"]) - float(f["sistema"]),
                    color="#D24444" if float(f["contado"]) - float(f["sistema"]) < 0 else "#37C97A",
                )
                for f in diferencias
            )
            html = """
            <html><head><style>
              body {{ font-family: Segoe UI, sans-serif; background: #0B1E33; color: #E6EDF5; padding: 24px; }}
              h1 {{ font-size: 16px; color: #7FB0FF; }}
              table {{ width: 100%; border-collapse: collapse; margin-top: 14px; }}
              th, td {{ padding: 8px 10px; border-bottom: 1px solid #1D3A5C; font-size: 13px; }}
              th {{ text-align: left; color: #9FB6D1; font-weight: 600; }}
            </style></head><body>
              <h1>Diferencias de conteo — {n} producto(s)</h1>
              <table>
                <tr><th>Producto</th><th>Sistema</th><th>Contado</th><th>Diferencia</th></tr>
                {filas}
              </table>
            </body></html>
            """.format(n=len(diferencias), filas=filas_html)

            ctx.show_html(html, title="Diferencias de conteo físico", width=700, height=500)
