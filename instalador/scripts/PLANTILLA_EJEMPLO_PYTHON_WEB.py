# lang: python
# -*- coding: utf-8 -*-
# timeout: 1800
# PLANTILLA_REQUISICION_COMPRA_PY.py
# Ejemplo comunitario en Python: crear una requisicion / solicitud de compra real
# con una ventana HTML/CSS moderna (pywebview, motor WebView2/Edge Chromium).
#
# Por que pywebview y no WinForms a pixel:
# - CSS real (flexbox, sombras, gradientes, tipografia) sin posicionar Point/Size a mano.
# - El puente JS<->Python (window.pywebview.api.*) ya resuelve la comunicacion; no hay
#   que inventar protocolo.
# - El SDK nativo NUNCA cambia: los botones de la ventana llaman funciones Python que
#   siguen usando ctx.erp.NuevoDocumento / AgregarArticulo / RecalcCompleto / Save
#   exactamente igual que cualquier otro script (addon v2.18.0, 4 anclas incluidas).
# - No afecta inventario: una solicitud de compra NO llama ctx.erp.AffectStockNEW.
#
# Requisitos de runtime:
# - El runtime embebido de BrosLMV ya trae 'pywebview' instalado (build/descargar_python.ps1).
# - "# timeout: 1800" le da al usuario hasta 30 minutos para capturar antes de que el
#   host mate el proceso (el default de scripts sin UI sigue siendo 120s).

from broslmv import ctx

import webview


# =============================================================================
# UTILIDADES
# =============================================================================

def texto(v):
    return "" if v is None else str(v)


def numero(v, defecto=0):
    if v is None:
        return defecto
    try:
        return int(v)
    except Exception:
        return defecto


def decimal(v, defecto=0.0):
    if v is None:
        return defecto
    try:
        return float(v)
    except Exception:
        return defecto


def revisar_erp(etapa):
    """Lee ctx.erp.LastError despues de llamadas criticas, si esta disponible."""
    try:
        err = ctx.erp.LastError()
    except Exception:
        err = ""
    if err:
        raise RuntimeError("{}: {}".format(etapa, err))


# =============================================================================
# BUSQUEDAS DE CATALOGOS (SQL controlado, ver docs/documentos/plantilla_documento_post_addon.md)
# =============================================================================

def to_native(data):
    if not data: return []
    res = []
    for row in data:
        try:
            d = {}
            keys = row.Keys if hasattr(row, 'Keys') else row
            for k in keys:
                d[str(k)] = row[k]
            res.append(d)
        except Exception as e:
            ctx.log("to_native err: " + str(e))
    return res

def buscar_proveedores(texto_busqueda):
    txt = texto_busqueda.strip()
    if not txt:
        return to_native(ctx.query(
            """
            SELECT TOP 50
                   be.BusinessEntityID,
                   be.OfficialName,
                   ISNULL(m.OfficialNumber, '') AS RFC
            FROM orgBusinessEntity be
            INNER JOIN orgSupplier s ON s.BusinessEntityID = be.BusinessEntityID
            LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
            WHERE be.DeletedOn IS NULL
            ORDER BY be.OfficialName
            """
        ))

    q = "%" + txt + "%"
    return to_native(ctx.query(
        """
        SELECT TOP 50
               be.BusinessEntityID,
               be.OfficialName,
               ISNULL(m.OfficialNumber, '') AS RFC
        FROM orgBusinessEntity be
        INNER JOIN orgSupplier s ON s.BusinessEntityID = be.BusinessEntityID
        LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
        WHERE be.DeletedOn IS NULL
          AND (m.OfficialNumber = @txt OR be.OfficialName LIKE @q)
        ORDER BY
          CASE WHEN m.OfficialNumber = @txt THEN 0 ELSE 1 END,
          be.OfficialName
        """,
        {"txt": txt, "q": q},
    ))


def buscar_almacenes(texto_busqueda):
    txt = texto_busqueda.strip()
    if not txt:
        return to_native(ctx.query(
            "SELECT TOP 50 DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName"
        ))
    q = "%" + txt + "%"
    return to_native(ctx.query(
        """
        SELECT TOP 50 DepotID, DepotName
        FROM orgDepot
        WHERE DeletedOn IS NULL
          AND DepotName LIKE @q
        ORDER BY
          CASE WHEN DepotName = @txt THEN 0 ELSE 1 END,
          DepotName
        """,
        {"txt": txt, "q": q},
    ))


def buscar_productos(texto_busqueda, depot_id):
    txt = texto_busqueda.strip()
    
    base_sql = """
        SELECT TOP 50
               p.ProductID, p.ProductKey, p.ProductName, p.Unit, p.TaxTypeID,
               ISNULL((
                   SELECT SUM(k.Quantity)
                   FROM orgProductKardex k
                   WHERE k.ProductID = p.ProductID
                     AND k.DepotID = @depot
                     AND k.Cancelled = 0
               ), 0) AS Stock
        FROM orgProduct p
        WHERE p.DeletedOn IS NULL
          AND p.TaxTypeID IS NOT NULL
          AND p.TaxTypeID > 0
    """
    
    if not txt:
        return to_native(ctx.query(
            base_sql + " ORDER BY p.ProductName",
            {"depot": depot_id}
        ))
        
    q = "%" + txt + "%"
    return to_native(ctx.query(
        base_sql + " AND (p.ProductKey = @txt OR p.ProductKey LIKE @q OR p.ProductName LIKE @q) ORDER BY CASE WHEN p.ProductKey = @txt THEN 0 ELSE 1 END, p.ProductName",
        {"txt": txt, "q": q, "depot": depot_id}
    ))


def asegurar_producto_proveedor(product_id, supplier_id):
    """Crea la relacion orgProductSupplier si CONTPAQi aun no la tiene."""
    ctx.execute(
        """
        IF NOT EXISTS (
            SELECT 1 FROM orgProductSupplier
            WHERE ProductID = @product_id AND SupplierID = @supplier_id
        )
        INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, RefSupplier, OrderNumber)
        VALUES (@product_id, @supplier_id, 0, 3, NULL, 0)
        """,
        {"product_id": product_id, "supplier_id": supplier_id},
    )


def crear_requisicion_doc(proveedor_be, depot_id, partidas_js, datos_doc):
    """Ejecuta el flujo canonico del addon para modulo 1040 (solicitud de compra).
    partidas_js: lista de dict {product_id, clave, cantidad, comentario} tal como llegan de JS
    datos_doc: dict con {fecha, serie, folio, comentarios}."""
    supplier_id = numero(
        ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID = @be", {"be": proveedor_be})
    )
    if supplier_id <= 0:
        raise RuntimeError("No se encontro SupplierID para BusinessEntityID={}".format(proveedor_be))

    partidas = []
    for p in partidas_js:
        partidas.append({
            "product_id": numero(p.get("product_id")),
            "clave": texto(p.get("clave")),
            "cantidad": decimal(p.get("cantidad")),
            "comentario": texto(p.get("comentario")),
        })
        if partidas[-1]["cantidad"] <= 0:
            raise RuntimeError("Cantidad invalida para {}".format(partidas[-1]["clave"]))

    ctx.log("PY-REQ inicio proveedor={} almacen={} partidas={}".format(proveedor_be, depot_id, len(partidas)))

    doc_id = numero(ctx.erp.NuevoDocumento(1040, depot_id, proveedor_be))
    revisar_erp("NuevoDocumento")
    if doc_id <= 0:
        raise RuntimeError("NuevoDocumento no devolvio DocumentID valido.")

    # Perfil del modulo 1040 (solicitud de compra): no afecta inventario ni CxP todavia.
    ctx.execute(
        """
        UPDATE docDocument
        SET DepotIDFrom = 0, UserID = 0,
            CampaignID = NULL, CostCenterID = NULL, ProjectID = NULL,
            PaymentTermID = 0,
            DateDocument = ISNULL(@fecha, DateDocument),
            DateDocDelivery = ISNULL(@fecha_req, DateDocDelivery),
            FolioPrefix = ISNULL(@serie, FolioPrefix),
            Folio = ISNULL(@folio, Folio),
            Observations = @obs
        WHERE DocumentID = @doc
        """,
        {
            "doc": doc_id,
            "fecha": datos_doc.get("fecha") if datos_doc.get("fecha") else None,
            "fecha_req": datos_doc.get("fecha_req") if datos_doc.get("fecha_req") else None,
            "serie": texto(datos_doc.get("serie") if datos_doc.get("serie") else None),
            "folio": texto(datos_doc.get("folio") if datos_doc.get("folio") else None),
            "obs": texto(datos_doc.get("comentarios"))
        },
    )

    for item in partidas:
        item_id = ctx.erp.AgregarArticulo(doc_id, item["product_id"], item["cantidad"])
        revisar_erp("AgregarArticulo {}".format(item["clave"]))
        asegurar_producto_proveedor(item["product_id"], supplier_id)
        
        comentario_item = item.get("comentario")
        if comentario_item:
            try:
                ctx.execute(
                    "UPDATE docDocumentItem SET Reference = @ref WHERE DocumentItemID = @item_id",
                    {"ref": comentario_item, "item_id": item_id}
                )
            except Exception:
                pass

    ctx.erp.RecalcCompleto(doc_id)
    revisar_erp("RecalcCompleto")

    ctx.erp.Save(doc_id)
    revisar_erp("Save")

    try:
        ctx.erp.RefreshGrid()
    except Exception:
        pass

    ctx.log("PY-REQ OK doc={}".format(doc_id))
    return doc_id


# =============================================================================
# PUENTE JS <-> PYTHON (window.pywebview.api.*)
# =============================================================================

class Api:
    window = None

    def buscar_proveedores(self, texto_busqueda):
        try:
            res = buscar_proveedores(texto_busqueda)
            ctx.log("PY-REQ prov_len=" + str(len(res) if res else 0))
            return res
        except Exception as e:
            ctx.log("PY-REQ err prov: " + str(e))
            return []

    def buscar_almacenes(self, texto_busqueda):
        try:
            res = buscar_almacenes(texto_busqueda)
            ctx.log("PY-REQ alm_len=" + str(len(res) if res else 0))
            return res
        except Exception as e:
            ctx.log("PY-REQ err alm: " + str(e))
            return []

    def buscar_productos(self, texto_busqueda, depot_id):
        try:
            res = buscar_productos(texto_busqueda, numero(depot_id))
            ctx.log("PY-REQ prod_len=" + str(len(res) if res else 0))
            return res
        except Exception as e:
            ctx.log("PY-REQ err prod: " + str(e))
            return []

    def crear_requisicion(self, proveedor_be, depot_id, partidas, datos_doc):
        try:
            doc_id = crear_requisicion_doc(numero(proveedor_be), numero(depot_id), partidas, datos_doc)
            return {"ok": True, "doc_id": doc_id}
        except Exception as ex:
            ctx.log("PY-REQ ERROR: {}".format(ex), "ERROR")
            return {"ok": False, "error": str(ex)}

    def cerrar(self):
        if self.window:
            self.window.destroy()


# =============================================================================
# UI (HTML/CSS/JS puro, sin frameworks)
# =============================================================================

import base64
HTML = base64.b64decode("PCFkb2N0eXBlIGh0bWw+CjxodG1sIGxhbmc9ImVzIj4KPGhlYWQ+CjxtZXRhIGNoYXJzZXQ9InV0Zi04Ij4KPG1ldGEgbmFtZT0idmlld3BvcnQiIGNvbnRlbnQ9IndpZHRoPWRldmljZS13aWR0aCwgaW5pdGlhbC1zY2FsZT0xIj4KPHRpdGxlPlJlcXVpc2ljacOzbiBkZSBjb21wcmE8L3RpdGxlPgo8c3R5bGU+CiAgOnJvb3QgewogICAgLS1iZy1tYWluOiAjZTJlOGYwOwogICAgLS1jYXJkLWJnOiAjZmZmZmZmOwogICAgLS10ZXh0LXByaW1hcnk6ICMxZTI5M2I7CiAgICAtLXRleHQtc2Vjb25kYXJ5OiAjNDc1NTY5OwogICAgLS10ZXh0LW11dGVkOiAjOWNhM2FmOwogICAgLS1ib3JkZXItY29sb3I6ICNjYmQ1ZTE7CiAgICAKICAgIC0tcHJpbWFyeTogIzI1NjNlYjsKICAgIC0tcHJpbWFyeS1ob3ZlcjogIzFkNGVkODsKICAgIC0tc3VjY2VzczogIzEwYjk4MTsKICAgIC0tc3VjY2Vzcy1ob3ZlcjogIzA1OTY2OTsKICAgIC0tZGFuZ2VyOiAjZWY0NDQ0OwogICAgLS1kYW5nZXItaG92ZXI6ICNkYzI2MjY7CiAgICAKICAgIC0tcmFkaXVzLXNtOiA2cHg7CiAgICAtLXJhZGl1cy1tZDogOHB4OwogICAgLS1yYWRpdXMtbGc6IDEycHg7CiAgfQoKICAqIHsgYm94LXNpemluZzogYm9yZGVyLWJveDsgZm9udC1mYW1pbHk6ICdJbnRlcicsICdTZWdvZSBVSScsIHN5c3RlbS11aSwgc2Fucy1zZXJpZjsgbWFyZ2luOiAwOyBwYWRkaW5nOiAwOyB9CiAgCiAgYm9keSB7CiAgICBiYWNrZ3JvdW5kOiB2YXIoLS1iZy1tYWluKTsKICAgIGNvbG9yOiB2YXIoLS10ZXh0LXByaW1hcnkpOwogICAgLXdlYmtpdC1mb250LXNtb290aGluZzogYW50aWFsaWFzZWQ7CiAgICBmb250LXNpemU6IDEzcHg7CiAgICBvdmVyZmxvdy14OiBoaWRkZW47CiAgfQogIAogIC8qIFNjcm9sbGJhciAqLwogIDo6LXdlYmtpdC1zY3JvbGxiYXIgeyB3aWR0aDogNnB4OyBoZWlnaHQ6IDZweDsgfQogIDo6LXdlYmtpdC1zY3JvbGxiYXItdHJhY2sgeyBiYWNrZ3JvdW5kOiB0cmFuc3BhcmVudDsgfQogIDo6LXdlYmtpdC1zY3JvbGxiYXItdGh1bWIgeyBiYWNrZ3JvdW5kOiAjY2JkNWUxOyBib3JkZXItcmFkaXVzOiA0cHg7IH0KICA6Oi13ZWJraXQtc2Nyb2xsYmFyLXRodW1iOmhvdmVyIHsgYmFja2dyb3VuZDogIzk0YTNiODsgfQoKICAvKiBIRUFERVIgKi8KICBoZWFkZXIgewogICAgYmFja2dyb3VuZDogbGluZWFyLWdyYWRpZW50KDE4MGRlZywgI2Y4ZmFmYyAwJSwgI2YxZjVmOSAxMDAlKTsKICAgIGJvcmRlci1ib3R0b206IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOwogICAgcGFkZGluZzogMTZweCAyNHB4OwogICAgZGlzcGxheTogZmxleDsKICAgIGp1c3RpZnktY29udGVudDogc3BhY2UtYmV0d2VlbjsKICAgIGFsaWduLWl0ZW1zOiBjZW50ZXI7CiAgICBwb3NpdGlvbjogc3RpY2t5OwogICAgdG9wOiAwOwogICAgei1pbmRleDogMTAwOwogIH0KCiAgLmhlYWRlci1sZWZ0IHsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsgZ2FwOiAxNnB4OyBmbGV4OiAxOyBtaW4td2lkdGg6IDA7IH0KICAuaGVhZGVyLWljb24gewogICAgZmxleC1zaHJpbms6IDA7CiAgICB3aWR0aDogNDhweDsgaGVpZ2h0OiA0OHB4OwogICAgYmFja2dyb3VuZDogI2VmZjZmZjsgY29sb3I6IHZhcigtLXByaW1hcnkpOwogICAgYm9yZGVyLXJhZGl1czogMTJweDsKICAgIGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGp1c3RpZnktY29udGVudDogY2VudGVyOwogIH0KICAuaGVhZGVyLXRpdGxlLWNvbnRhaW5lciB7IGRpc3BsYXk6IGZsZXg7IGZsZXgtZGlyZWN0aW9uOiBjb2x1bW47IGdhcDogNHB4OyBtaW4td2lkdGg6IDA7IH0KICAuaGVhZGVyLXRpdGxlLXJvdyB7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogMTJweDsgZmxleC13cmFwOiB3cmFwOyB9CiAgLmhlYWRlci10aXRsZS1yb3cgaDEgeyBmb250LXNpemU6IDIwcHg7IGZvbnQtd2VpZ2h0OiA3MDA7IGNvbG9yOiB2YXIoLS10ZXh0LXByaW1hcnkpOyBsZXR0ZXItc3BhY2luZzogLTAuMDJlbTsgd2hpdGUtc3BhY2U6IG5vd3JhcDsgb3ZlcmZsb3c6IGhpZGRlbjsgdGV4dC1vdmVyZmxvdzogZWxsaXBzaXM7IH0KICAuYmFkZ2UtYm9ycmFkb3IgewogICAgYmFja2dyb3VuZDogI2VmZjZmZjsgY29sb3I6IHZhcigtLXByaW1hcnkpOyBmb250LXNpemU6IDExcHg7IGZvbnQtd2VpZ2h0OiA2MDA7CiAgICBwYWRkaW5nOiA0cHggMTBweDsgYm9yZGVyLXJhZGl1czogOTk5OXB4OyBib3JkZXI6IDFweCBzb2xpZCAjZGJlYWZlOwogIH0KICAuaGVhZGVyLXRpdGxlLWNvbnRhaW5lciBwIHsgY29sb3I6IHZhcigtLXRleHQtc2Vjb25kYXJ5KTsgZm9udC1zaXplOiAxM3B4OyB9CgogIC5oZWFkZXItcmlnaHQgeyBkaXNwbGF5OiBmbGV4OyBnYXA6IDE2cHg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IH0KICAuaGVhZGVyLWluZm8tYm94IHsgZGlzcGxheTogZmxleDsgZmxleC1kaXJlY3Rpb246IGNvbHVtbjsgZ2FwOiA0cHg7IHBhZGRpbmctbGVmdDogMTZweDsgYm9yZGVyLWxlZnQ6IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOyB9CiAgLmhlYWRlci1pbmZvLWJveDpmaXJzdC1jaGlsZCB7IGJvcmRlci1sZWZ0OiBub25lOyBwYWRkaW5nLWxlZnQ6IDA7IH0KICAuaGVhZGVyLWluZm8tYm94IGxhYmVsIHsgZm9udC1zaXplOiAxMXB4OyBjb2xvcjogdmFyKC0tdGV4dC1zZWNvbmRhcnkpOyB9CiAgLmhlYWRlci1pbmZvLWJveCAudmFsIHsgZm9udC1zaXplOiAxM3B4OyBmb250LXdlaWdodDogNTAwOyBjb2xvcjogdmFyKC0tdGV4dC1wcmltYXJ5KTsgfQogIC5oZWFkZXItaW5wdXQtZGF0ZSB7CiAgICBib3JkZXI6IG5vbmU7IG91dGxpbmU6IG5vbmU7IGZvbnQtc2l6ZTogMTNweDsgZm9udC13ZWlnaHQ6IDUwMDsgY29sb3I6IHZhcigtLXRleHQtcHJpbWFyeSk7CiAgICBiYWNrZ3JvdW5kOiB0cmFuc3BhcmVudDsgZm9udC1mYW1pbHk6IGluaGVyaXQ7IGN1cnNvcjogcG9pbnRlcjsKICB9CiAgCiAgLyogTUFJTiBDT05URU5UICovCiAgbWFpbiB7IHBhZGRpbmc6IDI0cHg7IG1heC13aWR0aDogMTIwMHB4OyBtYXJnaW46IDAgYXV0bzsgZGlzcGxheTogZmxleDsgZmxleC1kaXJlY3Rpb246IGNvbHVtbjsgZ2FwOiAyNHB4OyBwYWRkaW5nLWJvdHRvbTogMTIwcHg7IH0KCiAgLyogQ0FSRFMgKi8KICAuY2FyZCB7CiAgICBiYWNrZ3JvdW5kOiB2YXIoLS1jYXJkLWJnKTsKICAgIGJvcmRlci1yYWRpdXM6IHZhcigtLXJhZGl1cy1sZyk7CiAgICBib3JkZXI6IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOwogICAgcGFkZGluZzogMjRweDsKICAgIGJveC1zaGFkb3c6IDAgNHB4IDZweCAtMXB4IHJnYmEoMCwgMCwgMCwgMC4wNSksIDAgMnB4IDRweCAtMXB4IHJnYmEoMCwgMCwgMCwgMC4wMyk7CiAgfQogIAogIC5jYXJkLWhlYWRlciB7CiAgICBkaXNwbGF5OiBmbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBqdXN0aWZ5LWNvbnRlbnQ6IHNwYWNlLWJldHdlZW47IG1hcmdpbi1ib3R0b206IDIwcHg7CiAgfQogIC5jYXJkLXRpdGxlIHsKICAgIGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogMTBweDsKICAgIGZvbnQtc2l6ZTogMTVweDsgZm9udC13ZWlnaHQ6IDYwMDsgY29sb3I6IHZhcigtLXRleHQtcHJpbWFyeSk7CiAgfQogIC5jYXJkLXRpdGxlIHN2ZyB7IGNvbG9yOiB2YXIoLS1wcmltYXJ5KTsgd2lkdGg6IDIwcHg7IGhlaWdodDogMjBweDsgfQogIAogIC5iYWRnZS1wYXJ0aWRhcyB7CiAgICBiYWNrZ3JvdW5kOiAjZWZmNmZmOyBjb2xvcjogdmFyKC0tcHJpbWFyeSk7IGZvbnQtc2l6ZTogMTJweDsgZm9udC13ZWlnaHQ6IDUwMDsKICAgIHBhZGRpbmc6IDRweCAxMnB4OyBib3JkZXItcmFkaXVzOiA5OTk5cHg7CiAgfQoKICAvKiBGT1JNUyAmIEdSSURTICovCiAgLmdyaWQtMyB7IGRpc3BsYXk6IGdyaWQ7IGdyaWQtdGVtcGxhdGUtY29sdW1uczogMWZyIDFmciAxZnI7IGdhcDogMjBweDsgbWFyZ2luLWJvdHRvbTogMjBweDsgfQogIC5ncmlkLTItY29tbWVudCB7IGRpc3BsYXk6IGdyaWQ7IGdyaWQtdGVtcGxhdGUtY29sdW1uczogMjUwcHggMWZyOyBnYXA6IDIwcHg7IH0KICAKICAuZm9ybS1ncm91cCB7IGRpc3BsYXk6IGZsZXg7IGZsZXgtZGlyZWN0aW9uOiBjb2x1bW47IGdhcDogOHB4OyBwb3NpdGlvbjogcmVsYXRpdmU7IH0KICAuZm9ybS1ncm91cCBsYWJlbCB7IGZvbnQtc2l6ZTogMTJweDsgZm9udC13ZWlnaHQ6IDYwMDsgY29sb3I6IHZhcigtLXRleHQtcHJpbWFyeSk7IH0KICAuZm9ybS1ncm91cCBsYWJlbCBzcGFuLnJlcSB7IGNvbG9yOiB2YXIoLS1kYW5nZXIpOyB9CiAgCiAgLmlucHV0LXdpdGgtaWNvbiB7IHBvc2l0aW9uOiByZWxhdGl2ZTsgfQogIC5pbnB1dC13aXRoLWljb24gc3ZnIHsKICAgIHBvc2l0aW9uOiBhYnNvbHV0ZTsgbGVmdDogMTJweDsgdG9wOiA1MCU7IHRyYW5zZm9ybTogdHJhbnNsYXRlWSgtNTAlKTsKICAgIHdpZHRoOiAxNnB4OyBoZWlnaHQ6IDE2cHg7IG1pbi13aWR0aDogMTZweDsgbWluLWhlaWdodDogMTZweDsgY29sb3I6IHZhcigtLXRleHQtbXV0ZWQpOyBwb2ludGVyLWV2ZW50czogbm9uZTsKICB9CiAgLmlucHV0LXdpdGgtaWNvbiBpbnB1dCwgLmlucHV0LXdpdGgtaWNvbiBzZWxlY3QgeyBwYWRkaW5nLWxlZnQ6IDM2cHg7IH0KICAKICAuaW5wdXQtd2l0aC1pY29uLXJpZ2h0IHN2ZyB7IGxlZnQ6IGF1dG87IHJpZ2h0OiAxMnB4OyB9CiAgLmlucHV0LXdpdGgtaWNvbi1yaWdodCBpbnB1dCB7IHBhZGRpbmctcmlnaHQ6IDM2cHg7IHBhZGRpbmctbGVmdDogMTJweDsgfQogIAogIGlucHV0W3R5cGU9dGV4dF0sIGlucHV0W3R5cGU9bnVtYmVyXSwgc2VsZWN0LCB0ZXh0YXJlYSB7CiAgICB3aWR0aDogMTAwJTsgYm9yZGVyOiAxcHggc29saWQgdmFyKC0tYm9yZGVyLWNvbG9yKTsgYm9yZGVyLXJhZGl1czogdmFyKC0tcmFkaXVzLW1kKTsKICAgIHBhZGRpbmc6IDEwcHggMTJweDsgZm9udC1zaXplOiAxM3B4OyBjb2xvcjogdmFyKC0tdGV4dC1wcmltYXJ5KTsKICAgIGJhY2tncm91bmQ6ICNmZmY7IG91dGxpbmU6IG5vbmU7IHRyYW5zaXRpb246IGFsbCAwLjJzOwogIH0KICBpbnB1dDo6cGxhY2Vob2xkZXIsIHRleHRhcmVhOjpwbGFjZWhvbGRlciB7IGNvbG9yOiB2YXIoLS10ZXh0LW11dGVkKTsgfQogIGlucHV0OmZvY3VzLCBzZWxlY3Q6Zm9jdXMsIHRleHRhcmVhOmZvY3VzIHsgYm9yZGVyLWNvbG9yOiB2YXIoLS1wcmltYXJ5KTsgYm94LXNoYWRvdzogMCAwIDAgM3B4IHJnYmEoMzcsOTksMjM1LDAuMSk7IH0KICAKICB0ZXh0YXJlYSB7IHJlc2l6ZTogdmVydGljYWw7IG1pbi1oZWlnaHQ6IDgwcHg7IHBhZGRpbmctbGVmdDogMzZweDsgfQogIC50ZXh0YXJlYS1pY29uIHsgcG9zaXRpb246IGFic29sdXRlOyBsZWZ0OiAxMnB4OyB0b3A6IDEycHg7IHdpZHRoOiAxNnB4OyBoZWlnaHQ6IDE2cHg7IGNvbG9yOiB2YXIoLS10ZXh0LW11dGVkKTsgfQogIC5jaGFyLWNvdW50ZXIgeyBwb3NpdGlvbjogYWJzb2x1dGU7IHJpZ2h0OiAxMnB4OyBib3R0b206IDhweDsgZm9udC1zaXplOiAxMXB4OyBjb2xvcjogdmFyKC0tdGV4dC1tdXRlZCk7IH0KCiAgLyogU0VBUkNIIEJBUiAoUEFSVElEQVMpICovCiAgLnNlYXJjaC1iYXItcm93IHsKICAgIGRpc3BsYXk6IGZsZXg7IGdhcDogMTZweDsgYWxpZ24taXRlbXM6IGZsZXgtZW5kOyBtYXJnaW4tYm90dG9tOiAxNnB4OwogIH0KICAuc2VhcmNoLWJhci1yb3cgLmZsZXgtMSB7IGZsZXg6IDE7IH0KICAKICAuYnRuLWFkZCB7CiAgICBiYWNrZ3JvdW5kOiB2YXIoLS1wcmltYXJ5KTsgY29sb3I6IHdoaXRlOyBib3JkZXI6IG5vbmU7IGJvcmRlci1yYWRpdXM6IHZhcigtLXJhZGl1cy1tZCk7CiAgICBwYWRkaW5nOiAxMHB4IDE2cHg7IGZvbnQtc2l6ZTogMTNweDsgZm9udC13ZWlnaHQ6IDUwMDsgY3Vyc29yOiBwb2ludGVyOwogICAgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsgZ2FwOiA4cHg7IHRyYW5zaXRpb246IGJhY2tncm91bmQgMC4yczsKICB9CiAgLmJ0bi1hZGQ6aG92ZXIgeyBiYWNrZ3JvdW5kOiB2YXIoLS1wcmltYXJ5LWhvdmVyKTsgfQoKICAvKiBUQUJMRSAqLwogIC50YWJsZS1jb250YWluZXIgeyBib3JkZXI6IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOyBib3JkZXItcmFkaXVzOiB2YXIoLS1yYWRpdXMtbWQpOyBvdmVyZmxvdy14OiBhdXRvOyBtYXJnaW4tYm90dG9tOiAxNnB4OyB9CiAgdGFibGUgeyB3aWR0aDogMTAwJTsgYm9yZGVyLWNvbGxhcHNlOiBjb2xsYXBzZTsgdGV4dC1hbGlnbjogbGVmdDsgfQogIHRoIHsgcGFkZGluZzogMTJweCAxNnB4OyBmb250LXNpemU6IDEycHg7IGZvbnQtd2VpZ2h0OiA2MDA7IGNvbG9yOiB2YXIoLS10ZXh0LXNlY29uZGFyeSk7IGJvcmRlci1ib3R0b206IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOyBiYWNrZ3JvdW5kOiAjZmFmYWZhOyB9CiAgdGQgeyBwYWRkaW5nOiAxMnB4IDE2cHg7IGJvcmRlci1ib3R0b206IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOyBmb250LXNpemU6IDEzcHg7IHZlcnRpY2FsLWFsaWduOiBtaWRkbGU7IH0KICB0cjpsYXN0LWNoaWxkIHRkIHsgYm9yZGVyLWJvdHRvbTogbm9uZTsgfQogIAogIC5kb3QgeyBkaXNwbGF5OiBpbmxpbmUtYmxvY2s7IHdpZHRoOiA4cHg7IGhlaWdodDogOHB4OyBib3JkZXItcmFkaXVzOiA1MCU7IG1hcmdpbi1yaWdodDogNnB4OyB9CiAgLmRvdC5ncmVlbiB7IGJhY2tncm91bmQ6IHZhcigtLXN1Y2Nlc3MpOyB9CiAgLmRvdC5yZWQgeyBiYWNrZ3JvdW5kOiB2YXIoLS1kYW5nZXIpOyB9CiAgCiAgLnRhYmxlLWFjdGlvbnMgeyBkaXNwbGF5OiBmbGV4OyBnYXA6IDEycHg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IH0KICAuYnRuLWljb24geyBiYWNrZ3JvdW5kOiBub25lOyBib3JkZXI6IG5vbmU7IGN1cnNvcjogcG9pbnRlcjsgY29sb3I6IHZhcigtLXRleHQtbXV0ZWQpOyB0cmFuc2l0aW9uOiBjb2xvciAwLjJzOyBwYWRkaW5nOiA0cHg7IH0KICAuYnRuLWljb246aG92ZXIgeyBjb2xvcjogdmFyKC0tcHJpbWFyeSk7IH0KICAuYnRuLWljb24uZGVsZXRlOmhvdmVyIHsgY29sb3I6IHZhcigtLWRhbmdlcik7IH0KICAuYnRuLWljb24gc3ZnIHsgd2lkdGg6IDE2cHg7IGhlaWdodDogMTZweDsgfQoKICAudGFibGUtZm9vdGVyIHsgZGlzcGxheTogZmxleDsganVzdGlmeS1jb250ZW50OiBzcGFjZS1iZXR3ZWVuOyBhbGlnbi1pdGVtczogY2VudGVyOyBmb250LXNpemU6IDEycHg7IGNvbG9yOiB2YXIoLS10ZXh0LXNlY29uZGFyeSk7IH0KICAucGFnaW5hdGlvbiB7IGRpc3BsYXk6IGZsZXg7IGdhcDogNHB4OyBhbGlnbi1pdGVtczogY2VudGVyOyB9CiAgLnBhZ2UtYnRuIHsKICAgIHdpZHRoOiAyOHB4OyBoZWlnaHQ6IDI4cHg7IGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGp1c3RpZnktY29udGVudDogY2VudGVyOwogICAgYm9yZGVyOiAxcHggc29saWQgdmFyKC0tYm9yZGVyLWNvbG9yKTsgYm9yZGVyLXJhZGl1czogNHB4OyBiYWNrZ3JvdW5kOiB3aGl0ZTsgY3Vyc29yOiBwb2ludGVyOyBjb2xvcjogdmFyKC0tdGV4dC1zZWNvbmRhcnkpOwogIH0KICAucGFnZS1idG46aG92ZXIgeyBiYWNrZ3JvdW5kOiAjZjFmNWY5OyB9CiAgLnBhZ2UtYnRuLmFjdGl2ZSB7IGJhY2tncm91bmQ6IHZhcigtLXByaW1hcnkpOyBjb2xvcjogd2hpdGU7IGJvcmRlci1jb2xvcjogdmFyKC0tcHJpbWFyeSk7IH0KCiAgLyogSU5GTyBDQVJEUyAqLwogIC5pbmZvLWNhcmRzIHsgZGlzcGxheTogZ3JpZDsgZ3JpZC10ZW1wbGF0ZS1jb2x1bW5zOiAxZnIgMWZyIDFmcjsgZ2FwOiAxNnB4OyB9CiAgLmluZm8tY2FyZCB7CiAgICBkaXNwbGF5OiBmbGV4OyBnYXA6IDEycHg7IHBhZGRpbmc6IDE2cHg7IGJvcmRlci1yYWRpdXM6IHZhcigtLXJhZGl1cy1tZCk7IGJvcmRlcjogMXB4IHNvbGlkIHZhcigtLWJvcmRlci1jb2xvcik7CiAgICBiYWNrZ3JvdW5kOiAjZjhmYWZjOwogIH0KICAuaW5mby1jYXJkLmdyZWVuIHsgYmFja2dyb3VuZDogI2YwZmRmNDsgYm9yZGVyLWNvbG9yOiAjYmJmN2QwOyB9CiAgLmluZm8tY2FyZC5ibHVlIHsgYmFja2dyb3VuZDogI2VmZjZmZjsgYm9yZGVyLWNvbG9yOiAjYmZkYmZlOyB9CiAgLmluZm8tY2FyZC5ncmF5IHsgYmFja2dyb3VuZDogI2Y4ZmFmYzsgYm9yZGVyLWNvbG9yOiAjZTVlN2ViOyB9CiAgLmluZm8tY2FyZC1pY29uIHsgcGFkZGluZy10b3A6IDJweDsgfQogIC5pbmZvLWNhcmQuZ3JlZW4gLmluZm8tY2FyZC1pY29uIHsgY29sb3I6IHZhcigtLXN1Y2Nlc3MpOyB9CiAgLmluZm8tY2FyZC5ibHVlIC5pbmZvLWNhcmQtaWNvbiB7IGNvbG9yOiB2YXIoLS1wcmltYXJ5KTsgfQogIC5pbmZvLWNhcmQuZ3JheSAuaW5mby1jYXJkLWljb24geyBjb2xvcjogdmFyKC0tdGV4dC1zZWNvbmRhcnkpOyB9CiAgLmluZm8tY2FyZC10ZXh0IGg0IHsgZm9udC1zaXplOiAxM3B4OyBmb250LXdlaWdodDogNjAwOyBtYXJnaW4tYm90dG9tOiAycHg7IGNvbG9yOiB2YXIoLS10ZXh0LXByaW1hcnkpOyB9CiAgLmluZm8tY2FyZC10ZXh0IHAgeyBmb250LXNpemU6IDExcHg7IGNvbG9yOiB2YXIoLS10ZXh0LXNlY29uZGFyeSk7IGxpbmUtaGVpZ2h0OiAxLjQ7IH0KCiAgLyogRk9PVEVSIEZJWEVEICovCiAgZm9vdGVyIHsKICAgIHBvc2l0aW9uOiBmaXhlZDsgYm90dG9tOiAwOyBsZWZ0OiAwOyByaWdodDogMDsKICAgIGJhY2tncm91bmQ6IHZhcigtLWNhcmQtYmcpOyBib3JkZXItdG9wOiAxcHggc29saWQgdmFyKC0tYm9yZGVyLWNvbG9yKTsKICAgIHBhZGRpbmc6IDE2cHggMjRweDsgZGlzcGxheTogZmxleDsganVzdGlmeS1jb250ZW50OiBzcGFjZS1iZXR3ZWVuOyBhbGlnbi1pdGVtczogY2VudGVyOyB6LWluZGV4OiAxMDA7CiAgfQogIC5mb290ZXItbGVmdCB7IGZvbnQtc2l6ZTogMTJweDsgY29sb3I6IHZhcigtLXRleHQtc2Vjb25kYXJ5KTsgZGlzcGxheTogZmxleDsgYWxpZ24taXRlbXM6IGNlbnRlcjsgZ2FwOiA2cHg7IH0KICAuZm9vdGVyLWxlZnQgc3ZnIHsgd2lkdGg6IDE0cHg7IGhlaWdodDogMTRweDsgfQogIC5yZXEtc3RhciB7IGNvbG9yOiB2YXIoLS1kYW5nZXIpOyB9CiAgCiAgLmZvb3Rlci1hY3Rpb25zIHsgZGlzcGxheTogZmxleDsgZ2FwOiAxMnB4OyBhbGlnbi1pdGVtczogY2VudGVyOyB9CiAgLmJ0bi1vdXRsaW5lIHsKICAgIGJhY2tncm91bmQ6IHdoaXRlOyBib3JkZXI6IDFweCBzb2xpZCB2YXIoLS1ib3JkZXItY29sb3IpOyBjb2xvcjogdmFyKC0tdGV4dC1wcmltYXJ5KTsKICAgIHBhZGRpbmc6IDEwcHggMjBweDsgYm9yZGVyLXJhZGl1czogdmFyKC0tcmFkaXVzLW1kKTsgZm9udC1zaXplOiAxM3B4OyBmb250LXdlaWdodDogNTAwOyBjdXJzb3I6IHBvaW50ZXI7IHRyYW5zaXRpb246IGJhY2tncm91bmQgMC4yczsKICAgIGRpc3BsYXk6IGZsZXg7IGFsaWduLWl0ZW1zOiBjZW50ZXI7IGdhcDogOHB4OwogIH0KICAuYnRuLW91dGxpbmU6aG92ZXIgeyBiYWNrZ3JvdW5kOiAjZjlmYWZiOyB9CiAgLmJ0bi1vdXRsaW5lLnRleHQtYmx1ZSB7IGNvbG9yOiB2YXIoLS1wcmltYXJ5KTsgYm9yZGVyLWNvbG9yOiAjYmZkYmZlOyB9CiAgLmJ0bi1vdXRsaW5lLnRleHQtYmx1ZTpob3ZlciB7IGJhY2tncm91bmQ6ICNlZmY2ZmY7IH0KICAKICAuYnRuLXN1Ym1pdCB7CiAgICBiYWNrZ3JvdW5kOiB2YXIoLS1zdWNjZXNzKTsgY29sb3I6IHdoaXRlOyBib3JkZXI6IG5vbmU7CiAgICBwYWRkaW5nOiAxMHB4IDIwcHg7IGJvcmRlci1yYWRpdXM6IHZhcigtLXJhZGl1cy1tZCk7IGZvbnQtc2l6ZTogMTNweDsgZm9udC13ZWlnaHQ6IDYwMDsgY3Vyc29yOiBwb2ludGVyOyB0cmFuc2l0aW9uOiBiYWNrZ3JvdW5kIDAuMnM7CiAgICBkaXNwbGF5OiBmbGV4OyBhbGlnbi1pdGVtczogY2VudGVyOyBnYXA6IDhweDsKICB9CiAgLmJ0bi1zdWJtaXQ6aG92ZXIgeyBiYWNrZ3JvdW5kOiB2YXIoLS1zdWNjZXNzLWhvdmVyKTsgfQogIC5idG4tc3VibWl0IHN2ZyB7IHdpZHRoOiAxNnB4OyBoZWlnaHQ6IDE2cHg7IH0KCiAgLyogRFJPUERPV05TICovCiAgLmRyb3Bkb3duLWNvbnRhaW5lciB7IHBvc2l0aW9uOiByZWxhdGl2ZTsgd2lkdGg6IDEwMCU7IH0KICAucmVzdWx0YWRvcyB7CiAgICBwb3NpdGlvbjogYWJzb2x1dGU7IHRvcDogY2FsYygxMDAlICsgNHB4KTsgbGVmdDogMDsgcmlnaHQ6IDA7CiAgICBiYWNrZ3JvdW5kOiB3aGl0ZTsgYm9yZGVyOiAxcHggc29saWQgdmFyKC0tYm9yZGVyLWNvbG9yKTsgYm9yZGVyLXJhZGl1czogdmFyKC0tcmFkaXVzLW1kKTsKICAgIGJveC1zaGFkb3c6IDAgMTBweCAyNXB4IHJnYmEoMCwwLDAsMC4xKTsgbWF4LWhlaWdodDogMjUwcHg7IG92ZXJmbG93LXk6IGF1dG87CiAgICB6LWluZGV4OiAxMDsgZGlzcGxheTogbm9uZTsKICB9CiAgLnJlc3VsdGFkb3MudmlzaWJsZSB7IGRpc3BsYXk6IGJsb2NrOyB9CiAgLnJlc3VsdGFkby1pdGVtIHsgcGFkZGluZzogMTBweCAxNHB4OyBjdXJzb3I6IHBvaW50ZXI7IGJvcmRlci1ib3R0b206IDFweCBzb2xpZCAjZjFmNWY5OyB9CiAgLnJlc3VsdGFkby1pdGVtOmhvdmVyIHsgYmFja2dyb3VuZDogI2Y4ZmFmYzsgfQogIC5yZXN1bHRhZG8taXRlbSAucHJpbmNpcGFsIHsgZm9udC13ZWlnaHQ6IDUwMDsgY29sb3I6IHZhcigtLXRleHQtcHJpbWFyeSk7IGZvbnQtc2l6ZTogMTNweDsgfQogIC5yZXN1bHRhZG8taXRlbSAuc2VjdW5kYXJpbyB7IGNvbG9yOiB2YXIoLS10ZXh0LXNlY29uZGFyeSk7IGZvbnQtc2l6ZTogMTFweDsgbWFyZ2luLXRvcDogMnB4OyB9CiAgLnJlc3VsdGFkby12YWNpbyB7IHBhZGRpbmc6IDE0cHg7IGNvbG9yOiB2YXIoLS10ZXh0LXNlY29uZGFyeSk7IGZvbnQtc2l6ZTogMTJweDsgdGV4dC1hbGlnbjogY2VudGVyOyB9CiAgCiAgLyogQkFOTkVSIEFMRVJUICovCiAgI2Jhbm5lciB7CiAgICBwb3NpdGlvbjogZml4ZWQ7IHRvcDogMjRweDsgbGVmdDogNTAlOyB0cmFuc2Zvcm06IHRyYW5zbGF0ZVgoLTUwJSk7CiAgICBwYWRkaW5nOiAxMnB4IDI0cHg7IGJvcmRlci1yYWRpdXM6IDhweDsgZm9udC1zaXplOiAxM3B4OyBmb250LXdlaWdodDogNTAwOwogICAgY29sb3I6IHdoaXRlOyBkaXNwbGF5OiBub25lOyB6LWluZGV4OiA5OTk5OyBib3gtc2hhZG93OiAwIDEwcHggMjVweCByZ2JhKDAsMCwwLDAuMik7CiAgfQogICNiYW5uZXIud2FybiB7IGJhY2tncm91bmQ6ICNmNTllMGI7IGRpc3BsYXk6IGJsb2NrOyB9CiAgI2Jhbm5lci5lcnJvciB7IGJhY2tncm91bmQ6IHZhcigtLWRhbmdlcik7IGRpc3BsYXk6IGJsb2NrOyB9CiAgI2Jhbm5lci5vayB7IGJhY2tncm91bmQ6IHZhcigtLXN1Y2Nlc3MpOyBkaXNwbGF5OiBibG9jazsgfQo8L3N0eWxlPgo8L2hlYWQ+Cjxib2R5PgoKPGRpdiBpZD0iYmFubmVyIj48L2Rpdj4KCjxoZWFkZXI+CiAgPGRpdiBjbGFzcz0iaGVhZGVyLWxlZnQiPgogICAgPGRpdiBjbGFzcz0iaGVhZGVyLWljb24iPgogICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjI4IiBoZWlnaHQ9IjI4IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCI+PGNpcmNsZSBjeD0iOSIgY3k9IjIxIiByPSIxIi8+PGNpcmNsZSBjeD0iMjAiIGN5PSIyMSIgcj0iMSIvPjxwYXRoIGQ9Ik0xIDFoNGwyLjY4IDEzLjM5YTIgMiAwIDAgMCAyIDEuNjFoOS43MmEyIDIgMCAwIDAgMi0xLjYxTDIzIDZINiIvPjwvc3ZnPgogICAgPC9kaXY+CiAgICA8ZGl2IGNsYXNzPSJoZWFkZXItdGl0bGUtY29udGFpbmVyIj4KICAgICAgPGRpdiBjbGFzcz0iaGVhZGVyLXRpdGxlLXJvdyI+CiAgICAgICAgPGgxPk51ZXZhIHJlcXVpc2ljacOzbiBkZSBjb21wcmE8L2gxPgogICAgICAgIDxzcGFuIGNsYXNzPSJiYWRnZS1ib3JyYWRvciI+Qm9ycmFkb3I8L3NwYW4+CiAgICAgIDwvZGl2PgogICAgICA8cD5DYXB0dXJhIHLDoXBpZGEgZGUgc29saWNpdHVkZXMgaW50ZXJuYXM8L3A+CiAgICA8L2Rpdj4KICA8L2Rpdj4KICAKICA8ZGl2IGNsYXNzPSJoZWFkZXItcmlnaHQiPgogICAgPGRpdiBjbGFzcz0iaGVhZGVyLWluZm8tYm94Ij4KICAgICAgPGxhYmVsPlNlcmllPC9sYWJlbD4KICAgICAgPGlucHV0IHR5cGU9InRleHQiIGlkPSJpbnAtc2VyaWUiIGNsYXNzPSJoZWFkZXItaW5wdXQtZGF0ZSIgdmFsdWU9IkF1dG9tw6F0aWNhIiBzdHlsZT0id2lkdGg6ODBweCIgb25mb2N1cz0iaWYodGhpcy52YWx1ZT09PSdBdXRvbcOhdGljYScpdGhpcy52YWx1ZT0nJyIgb25ibHVyPSJpZih0aGlzLnZhbHVlPT09JycpdGhpcy52YWx1ZT0nQXV0b23DoXRpY2EnIj4KICAgIDwvZGl2PgogICAgPGRpdiBjbGFzcz0iaGVhZGVyLWluZm8tYm94Ij4KICAgICAgPGxhYmVsPkZvbGlvPC9sYWJlbD4KICAgICAgPGlucHV0IHR5cGU9InRleHQiIGlkPSJpbnAtZm9saW8iIGNsYXNzPSJoZWFkZXItaW5wdXQtZGF0ZSIgdmFsdWU9IkF1dG9tw6F0aWNvIiBzdHlsZT0id2lkdGg6ODBweCIgb25mb2N1cz0iaWYodGhpcy52YWx1ZT09PSdBdXRvbcOhdGljbycpdGhpcy52YWx1ZT0nJyIgb25ibHVyPSJpZih0aGlzLnZhbHVlPT09JycpdGhpcy52YWx1ZT0nQXV0b23DoXRpY28nIj4KICAgIDwvZGl2PgogICAgPGRpdiBjbGFzcz0iaGVhZGVyLWluZm8tYm94Ij4KICAgICAgPGxhYmVsPkZlY2hhPC9sYWJlbD4KICAgICAgPGlucHV0IHR5cGU9ImRhdGUiIGlkPSJpbnAtZmVjaGEiIGNsYXNzPSJoZWFkZXItaW5wdXQtZGF0ZSI+CiAgICA8L2Rpdj4KICAgIDxkaXYgY2xhc3M9ImhlYWRlci1pbmZvLWJveCI+CiAgICAgIDxsYWJlbD5GZWNoYSByZXF1ZXJpZGE8L2xhYmVsPgogICAgICA8aW5wdXQgdHlwZT0iZGF0ZSIgaWQ9ImlucC1mZWNoYS1yZXEiIGNsYXNzPSJoZWFkZXItaW5wdXQtZGF0ZSI+CiAgICA8L2Rpdj4KICA8L2Rpdj4KPC9oZWFkZXI+Cgo8bWFpbj4KICA8IS0tIERBVE9TIEdFTkVSQUxFUyAtLT4KICA8ZGl2IGNsYXNzPSJjYXJkIj4KICAgIDxkaXYgY2xhc3M9ImNhcmQtaGVhZGVyIj4KICAgICAgPGRpdiBjbGFzcz0iY2FyZC10aXRsZSI+CiAgICAgICAgPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIyNCIgaGVpZ2h0PSIyNCIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiPjxwYXRoIGQ9Ik00IDZoMTZNNCAxMmgxNk00IDE4aDciLz48L3N2Zz4KICAgICAgICBEYXRvcyBnZW5lcmFsZXMKICAgICAgPC9kaXY+CiAgICA8L2Rpdj4KICAgIAogICAgPGRpdiBjbGFzcz0iZ3JpZC0zIj4KICAgICAgPGRpdiBjbGFzcz0iZm9ybS1ncm91cCBkcm9wZG93bi1jb250YWluZXIiPgogICAgICAgIDxsYWJlbD5Tb2xpY2l0YW50ZSA8c3BhbiBjbGFzcz0icmVxIj4qPC9zcGFuPjwvbGFiZWw+CiAgICAgICAgPGRpdiBjbGFzcz0iaW5wdXQtd2l0aC1pY29uIGlucHV0LXdpdGgtaWNvbi1yaWdodCI+CiAgICAgICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48cGF0aCBkPSJNMjAgMjF2LTJhNCA0IDAgMCAwLTQtNEg4YTQgNCAwIDAgMC00IDR2MiIvPjxjaXJjbGUgY3g9IjEyIiBjeT0iNyIgcj0iNCIvPjwvc3ZnPgogICAgICAgICAgPGlucHV0IHR5cGU9InRleHQiIGlkPSJpbnAtcHJvdmVlZG9yIiBwbGFjZWhvbGRlcj0iQnVzY2FyIHNvbGljaXRhbnRlLi4uIiBhdXRvY29tcGxldGU9Im9mZiI+CiAgICAgICAgICA8c3ZnIHN0eWxlPSJyaWdodDoxMnB4OyBsZWZ0OmF1dG87IiB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PHBvbHlsaW5lIHBvaW50cz0iNiA5IDEyIDE1IDE4IDkiLz48L3N2Zz4KICAgICAgICA8L2Rpdj4KICAgICAgICA8ZGl2IGNsYXNzPSJyZXN1bHRhZG9zIiBpZD0icmVzLXByb3ZlZWRvciI+PC9kaXY+CiAgICAgIDwvZGl2PgogICAgICAKICAgICAgPGRpdiBjbGFzcz0iZm9ybS1ncm91cCI+CiAgICAgICAgPGxhYmVsPsOBcmVhIC8gQ2VudHJvIGRlIGNvc3RvPC9sYWJlbD4KICAgICAgICA8ZGl2IGNsYXNzPSJpbnB1dC13aXRoLWljb24gaW5wdXQtd2l0aC1pY29uLXJpZ2h0Ij4KICAgICAgICAgIDxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxyZWN0IHg9IjQiIHk9IjQiIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiIgcng9IjIiIHJ5PSIyIi8+PHJlY3QgeD0iOSIgeT0iOSIgd2lkdGg9IjYiIGhlaWdodD0iNiIvPjwvc3ZnPgogICAgICAgICAgPGlucHV0IHR5cGU9InRleHQiIGlkPSJpbnAtY2VudHJvIiBwbGFjZWhvbGRlcj0iU2VsZWNjaW9uYXIgw6FyZWEgbyBjZW50cm8gZGUgY29zdG8uLi4iIGF1dG9jb21wbGV0ZT0ib2ZmIj4KICAgICAgICAgIDxzdmcgc3R5bGU9InJpZ2h0OjEycHg7IGxlZnQ6YXV0bzsiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48cG9seWxpbmUgcG9pbnRzPSI2IDkgMTIgMTUgMTggOSIvPjwvc3ZnPgogICAgICAgIDwvZGl2PgogICAgICA8L2Rpdj4KCiAgICAgIDxkaXYgY2xhc3M9ImZvcm0tZ3JvdXAgZHJvcGRvd24tY29udGFpbmVyIj4KICAgICAgICA8bGFiZWw+QWxtYWPDqW4gZGVzdGlubyA8c3BhbiBjbGFzcz0icmVxIj4qPC9zcGFuPjwvbGFiZWw+CiAgICAgICAgPGRpdiBjbGFzcz0iaW5wdXQtd2l0aC1pY29uIGlucHV0LXdpdGgtaWNvbi1yaWdodCI+CiAgICAgICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48cGF0aCBkPSJNMyA5bDktNyA5IDd2MTFhMiAyIDAgMCAxLTIgMkg1YTIgMiAwIDAgMS0yLTJ6Ii8+PHBvbHlsaW5lIHBvaW50cz0iOSAyMiA5IDEyIDE1IDEyIDE1IDIyIi8+PC9zdmc+CiAgICAgICAgICA8aW5wdXQgdHlwZT0idGV4dCIgaWQ9ImlucC1hbG1hY2VuIiBwbGFjZWhvbGRlcj0iU2VsZWNjaW9uYXIgYWxtYWPDqW4gZGVzdGluby4uLiIgYXV0b2NvbXBsZXRlPSJvZmYiPgogICAgICAgICAgPHN2ZyBzdHlsZT0icmlnaHQ6MTJweDsgbGVmdDphdXRvOyIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwb2x5bGluZSBwb2ludHM9IjYgOSAxMiAxNSAxOCA5Ii8+PC9zdmc+CiAgICAgICAgPC9kaXY+CiAgICAgICAgPGRpdiBjbGFzcz0icmVzdWx0YWRvcyIgaWQ9InJlcy1hbG1hY2VuIj48L2Rpdj4KICAgICAgPC9kaXY+CiAgICA8L2Rpdj4KCiAgICA8ZGl2IGNsYXNzPSJncmlkLTItY29tbWVudCI+CiAgICAgIDxkaXYgY2xhc3M9ImZvcm0tZ3JvdXAiPgogICAgICAgIDxsYWJlbD5QcmlvcmlkYWQgPHNwYW4gY2xhc3M9InJlcSI+Kjwvc3Bhbj48L2xhYmVsPgogICAgICAgIDxkaXYgY2xhc3M9ImlucHV0LXdpdGgtaWNvbiBpbnB1dC13aXRoLWljb24tcmlnaHQiPgogICAgICAgICAgPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PHBhdGggZD0iTTQgMTVzMS0xIDQtMSA1IDIgOCAyIDQtMSA0LTFWM3MtMSAxLTQgMS01LTItOC0yLTQgMS00IDF6Ii8+PGxpbmUgeDE9IjQiIHkxPSIyMiIgeDI9IjQiIHkyPSIxNSIvPjwvc3ZnPgogICAgICAgICAgPHNlbGVjdCBpZD0iaW5wLXByaW9yaWRhZCI+CiAgICAgICAgICAgIDxvcHRpb24+QmFqYTwvb3B0aW9uPgogICAgICAgICAgICA8b3B0aW9uIHNlbGVjdGVkPk1lZGlhPC9vcHRpb24+CiAgICAgICAgICAgIDxvcHRpb24+QWx0YTwvb3B0aW9uPgogICAgICAgICAgPC9zZWxlY3Q+CiAgICAgICAgPC9kaXY+CiAgICAgIDwvZGl2PgogICAgICAKICAgICAgPGRpdiBjbGFzcz0iZm9ybS1ncm91cCI+CiAgICAgICAgPGxhYmVsPkNvbWVudGFyaW9zPC9sYWJlbD4KICAgICAgICA8ZGl2IGNsYXNzPSJpbnB1dC13aXRoLWljb24iPgogICAgICAgICAgPHN2ZyBjbGFzcz0idGV4dGFyZWEtaWNvbiIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwYXRoIGQ9Ik0yMSAxNWEyIDIgMCAwIDEtMiAySDdsLTQgNFY1YTIgMiAwIDAgMSAyLTJoMTRhMiAyIDAgMCAxIDIgMnoiLz48L3N2Zz4KICAgICAgICAgIDx0ZXh0YXJlYSBpZD0iaW5wLWNvbWVudGFyaW9zIiBwbGFjZWhvbGRlcj0iQWdyZWdhciBjb21lbnRhcmlvcyB1IG9ic2VydmFjaW9uZXMgZ2VuZXJhbGVzIGRlIGxhIHJlcXVpc2ljacOzbi4uLiI+PC90ZXh0YXJlYT4KICAgICAgICAgIDxzcGFuIGNsYXNzPSJjaGFyLWNvdW50ZXIiPjAvNTAwPC9zcGFuPgogICAgICAgIDwvZGl2PgogICAgICA8L2Rpdj4KICAgIDwvZGl2PgogIDwvZGl2PgoKICA8IS0tIFBBUlRJREFTIC0tPgogIDxkaXYgY2xhc3M9ImNhcmQiPgogICAgPGRpdiBjbGFzcz0iY2FyZC1oZWFkZXIiPgogICAgICA8ZGl2IGNsYXNzPSJjYXJkLXRpdGxlIj4KICAgICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjI0IiBoZWlnaHQ9IjI0IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCI+PHJlY3QgeD0iMyIgeT0iMyIgd2lkdGg9IjciIGhlaWdodD0iNyIvPjxyZWN0IHg9IjE0IiB5PSIzIiB3aWR0aD0iNyIgaGVpZ2h0PSI3Ii8+PHJlY3QgeD0iMTQiIHk9IjE0IiB3aWR0aD0iNyIgaGVpZ2h0PSI3Ii8+PHJlY3QgeD0iMyIgeT0iMTQiIHdpZHRoPSI3IiBoZWlnaHQ9IjciLz48L3N2Zz4KICAgICAgICBQYXJ0aWRhcwogICAgICA8L2Rpdj4KICAgICAgPHNwYW4gY2xhc3M9ImJhZGdlLXBhcnRpZGFzIiBpZD0iYmFkZ2UtcGFydGlkYXMtY291bnQiPjAgcGFydGlkYXM8L3NwYW4+CiAgICA8L2Rpdj4KCiAgICA8ZGl2IGNsYXNzPSJzZWFyY2gtYmFyLXJvdyI+CiAgICAgIDxkaXYgY2xhc3M9ImZvcm0tZ3JvdXAgZmxleC0xIGRyb3Bkb3duLWNvbnRhaW5lciI+CiAgICAgICAgPGRpdiBjbGFzcz0iaW5wdXQtd2l0aC1pY29uIj4KICAgICAgICAgIDxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxjaXJjbGUgY3g9IjExIiBjeT0iMTEiIHI9IjgiLz48bGluZSB4MT0iMjEiIHkxPSIyMSIgeDI9IjE2LjY1IiB5Mj0iMTYuNjUiLz48L3N2Zz4KICAgICAgICAgIDxpbnB1dCB0eXBlPSJ0ZXh0IiBpZD0iaW5wLXByb2R1Y3RvIiBwbGFjZWhvbGRlcj0iQnVzY2FyIHByb2R1Y3RvIG8gc2VydmljaW8gKGNsYXZlIG8gZGVzY3JpcGNpw7NuKS4uLiIgYXV0b2NvbXBsZXRlPSJvZmYiPgogICAgICAgIDwvZGl2PgogICAgICAgIDxkaXYgY2xhc3M9InJlc3VsdGFkb3MiIGlkPSJyZXMtcHJvZHVjdG8iPjwvZGl2PgogICAgICA8L2Rpdj4KICAgICAgCiAgICAgIDxkaXYgY2xhc3M9ImZvcm0tZ3JvdXAiIHN0eWxlPSJ3aWR0aDogMTAwcHg7Ij4KICAgICAgICA8bGFiZWw+Q2FudGlkYWQgPHNwYW4gY2xhc3M9InJlcSI+Kjwvc3Bhbj48L2xhYmVsPgogICAgICAgIDxpbnB1dCB0eXBlPSJudW1iZXIiIGlkPSJpbnAtY2FudGlkYWQiIHZhbHVlPSIxIiBtaW49IjAuMDEiIHN0ZXA9IjAuMDEiIHN0eWxlPSJ3aWR0aDoxMDAlOyI+CiAgICAgIDwvZGl2PgoKICAgICAgPGRpdiBjbGFzcz0iZm9ybS1ncm91cCIgc3R5bGU9IndpZHRoOiAxNDBweDsiPgogICAgICAgIDxsYWJlbD5VLk0uIDxzcGFuIGNsYXNzPSJyZXEiPio8L3NwYW4+PC9sYWJlbD4KICAgICAgICA8ZGl2IGNsYXNzPSJpbnB1dC13aXRoLWljb24tcmlnaHQiIHN0eWxlPSJwb3NpdGlvbjpyZWxhdGl2ZTsiPgogICAgICAgICAgPGlucHV0IHR5cGU9InRleHQiIGlkPSJpbnAtdW0iIHBsYWNlaG9sZGVyPSJTZWxlY2Npb25hci4uLiIgcmVhZG9ubHkgc3R5bGU9ImJhY2tncm91bmQ6I2Y5ZmFmYjsgY3Vyc29yOmRlZmF1bHQ7Ij4KICAgICAgICAgIDxzdmcgc3R5bGU9InBvc2l0aW9uOmFic29sdXRlOyByaWdodDoxMnB4OyB0b3A6NTAlOyB0cmFuc2Zvcm06dHJhbnNsYXRlWSgtNTAlKTsgd2lkdGg6MTZweDsgaGVpZ2h0OjE2cHg7IGNvbG9yOiM5Y2EzYWY7IiB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PHBvbHlsaW5lIHBvaW50cz0iNiA5IDEyIDE1IDE4IDkiLz48L3N2Zz4KICAgICAgICA8L2Rpdj4KICAgICAgPC9kaXY+CgogICAgICA8YnV0dG9uIGNsYXNzPSJidG4tYWRkIiBvbmNsaWNrPSJhZ3JlZ2FyUGFydGlkYU1hbnVhbCgpIj4KICAgICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjE2IiBoZWlnaHQ9IjE2IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxsaW5lIHgxPSIxMiIgeTE9IjUiIHgyPSIxMiIgeTI9IjE5Ii8+PGxpbmUgeDE9IjUiIHkxPSIxMiIgeDI9IjE5IiB5Mj0iMTIiLz48L3N2Zz4KICAgICAgICBBZ3JlZ2FyIHBhcnRpZGEKICAgICAgPC9idXR0b24+CiAgICA8L2Rpdj4KCiAgICA8ZGl2IGNsYXNzPSJ0YWJsZS1jb250YWluZXIiPgogICAgICA8dGFibGU+CiAgICAgICAgPHRoZWFkPgogICAgICAgICAgPHRyPgogICAgICAgICAgICA8dGg+Q2xhdmU8L3RoPgogICAgICAgICAgICA8dGg+RGVzY3JpcGNpw7NuPC90aD4KICAgICAgICAgICAgPHRoPlUuTS48L3RoPgogICAgICAgICAgICA8dGg+RXhpc3RlbmNpYTwvdGg+CiAgICAgICAgICAgIDx0aD5DYW50aWRhZCBzb2xpY2l0YWRhPC90aD4KICAgICAgICAgICAgPHRoPk9ic2VydmFjaW9uZXM8L3RoPgogICAgICAgICAgICA8dGggc3R5bGU9InRleHQtYWxpZ246cmlnaHQ7Ij5BY2Npb25lczwvdGg+CiAgICAgICAgICA8L3RyPgogICAgICAgIDwvdGhlYWQ+CiAgICAgICAgPHRib2R5IGlkPSJ0YWJsYS1wYXJ0aWRhcyI+CiAgICAgICAgICA8dHI+PHRkIGNvbHNwYW49IjciIGNsYXNzPSJ2YWNpby10YWJsYSI+Tm8gaGF5IHBhcnRpZGFzIGFncmVnYWRhczwvdGQ+PC90cj4KICAgICAgICA8L3Rib2R5PgogICAgICA8L3RhYmxlPgogICAgPC9kaXY+CgogICAgPGRpdiBjbGFzcz0idGFibGUtZm9vdGVyIj4KICAgICAgPHNwYW4gaWQ9InR4dC1tb3N0cmFuZG8iPk1vc3RyYW5kbyAwIGRlIDAgcGFydGlkYXM8L3NwYW4+CiAgICAgIDxkaXYgY2xhc3M9InBhZ2luYXRpb24iPgogICAgICAgIDxidXR0b24gY2xhc3M9InBhZ2UtYnRuIj48c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjE0IiBoZWlnaHQ9IjE0IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwb2x5bGluZSBwb2ludHM9IjE1IDE4IDkgMTIgMTUgNiIvPjwvc3ZnPjwvYnV0dG9uPgogICAgICAgIDxidXR0b24gY2xhc3M9InBhZ2UtYnRuIGFjdGl2ZSI+MTwvYnV0dG9uPgogICAgICAgIDxidXR0b24gY2xhc3M9InBhZ2UtYnRuIj48c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjE0IiBoZWlnaHQ9IjE0IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwb2x5bGluZSBwb2ludHM9IjkgMTggMTUgMTIgOSA2Ii8+PC9zdmc+PC9idXR0b24+CiAgICAgIDwvZGl2PgogICAgPC9kaXY+CiAgPC9kaXY+CgogIDwhLS0gSU5GTyBDQVJEUyAtLT4KICA8ZGl2IGNsYXNzPSJpbmZvLWNhcmRzIj4KICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZCBncmVlbiI+CiAgICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZC1pY29uIj4KICAgICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjIwIiBoZWlnaHQ9IjIwIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwYXRoIGQ9Ik0yMiAxMS4wOFYxMmExMCAxMCAwIDEgMS01LjkzLTkuMTQiLz48cG9seWxpbmUgcG9pbnRzPSIyMiA0IDEyIDE0LjAxIDkgMTEuMDEiLz48L3N2Zz4KICAgICAgPC9kaXY+CiAgICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZC10ZXh0Ij4KICAgICAgICA8aDQ+U2luIGltcG9ydGVzPC9oND4KICAgICAgICA8cD5Fc3RhIHJlcXVpc2ljacOzbiBubyBpbmNsdXllIHByZWNpb3MgbmkgbW9udG9zLjwvcD4KICAgICAgPC9kaXY+CiAgICA8L2Rpdj4KICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZCBncmF5Ij4KICAgICAgPGRpdiBjbGFzcz0iaW5mby1jYXJkLWljb24iPgogICAgICAgIDxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB3aWR0aD0iMjAiIGhlaWdodD0iMjAiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PGxpbmUgeDE9IjgiIHkxPSI2IiB4Mj0iMjEiIHkyPSI2Ii8+PGxpbmUgeDE9IjgiIHkxPSIxMiIgeDI9IjIxIiB5Mj0iMTIiLz48bGluZSB4MT0iOCIgeTE9IjE4IiB4Mj0iMjEiIHkyPSIxOCIvPjxsaW5lIHgxPSIzIiB5MT0iNiIgeDI9IjMuMDEiIHkyPSI2Ii8+PGxpbmUgeDE9IjMiIHkxPSIxMiIgeDI9IjMuMDEiIHkyPSIxMiIvPjxsaW5lIHgxPSIzIiB5MT0iMTgiIHgyPSIzLjAxIiB5Mj0iMTgiLz48L3N2Zz4KICAgICAgPC9kaXY+CiAgICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZC10ZXh0Ij4KICAgICAgICA8aDQ+Q2FwdHVyYSBvcmllbnRhZGEgYSBwYXJ0aWRhczwvaDQ+CiAgICAgICAgPHA+RGVmaW5lIGxvcyBwcm9kdWN0b3MgeSBzZXJ2aWNpb3MgcXVlIHJlcXVpZXJlIHR1IMOhcmVhLjwvcD4KICAgICAgPC9kaXY+CiAgICA8L2Rpdj4KICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZCBibHVlIj4KICAgICAgPGRpdiBjbGFzcz0iaW5mby1jYXJkLWljb24iPgogICAgICAgIDxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB3aWR0aD0iMjAiIGhlaWdodD0iMjAiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PHBhdGggZD0iTTEyIDIyczgtNCA4LTEwVjVsLTgtMy04IDN2N2MwIDYgOCAxMCA4IDEweiIvPjxjaXJjbGUgY3g9IjEyIiBjeT0iMTEiIHI9IjMiLz48L3N2Zz4KICAgICAgPC9kaXY+CiAgICAgIDxkaXYgY2xhc3M9ImluZm8tY2FyZC10ZXh0Ij4KICAgICAgICA8aDQ+Q29tcGF0aWJsZSBjb24gbcO6bHRpcGxlcyBwYXJ0aWRhczwvaDQ+CiAgICAgICAgPHA+QWdyZWdhIHRvZGFzIGxhcyBwYXJ0aWRhcyBuZWNlc2FyaWFzIHBhcmEgdHUgc29saWNpdHVkLjwvcD4KICAgICAgPC9kaXY+CiAgICA8L2Rpdj4KICA8L2Rpdj4KPC9tYWluPgoKPGZvb3Rlcj4KICA8ZGl2IGNsYXNzPSJmb290ZXItbGVmdCI+CiAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48Y2lyY2xlIGN4PSIxMiIgY3k9IjEyIiByPSIxMCIvPjxsaW5lIHgxPSIxMiIgeTE9IjE2IiB4Mj0iMTIiIHkyPSIxMiIvPjxsaW5lIHgxPSIxMiIgeTE9IjgiIHgyPSIxMi4wMSIgeTI9IjgiLz48L3N2Zz4KICAgIDxzcGFuPkxvcyBjYW1wb3MgbWFyY2Fkb3MgY29uIDxzcGFuIGNsYXNzPSJyZXEtc3RhciI+Kjwvc3Bhbj4gc29uIG9ibGlnYXRvcmlvczwvc3Bhbj4KICA8L2Rpdj4KICA8ZGl2IGNsYXNzPSJmb290ZXItYWN0aW9ucyI+CiAgICA8YnV0dG9uIGNsYXNzPSJidG4tb3V0bGluZSIgb25jbGljaz0iY2FuY2VsYXIoKSI+CiAgICAgIDxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PGxpbmUgeDE9IjE4IiB5MT0iNiIgeDI9IjYiIHkyPSIxOCIvPjxsaW5lIHgxPSI2IiB5MT0iNiIgeDI9IjE4IiB5Mj0iMTgiLz48L3N2Zz4KICAgICAgQ2FuY2VsYXIKICAgIDwvYnV0dG9uPgogICAgPGJ1dHRvbiBjbGFzcz0iYnRuLW91dGxpbmUgdGV4dC1ibHVlIj4KICAgICAgPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48cGF0aCBkPSJNMTkgMjFINWEyIDIgMCAwIDEtMi0yVjVhMiAyIDAgMCAxIDItMmgxMWw1IDV2MTFhMiAyIDAgMCAxLTIgMnoiLz48cG9seWxpbmUgcG9pbnRzPSIxNyAyMSAxNyAxMyA3IDEzIDcgMjEiLz48cG9seWxpbmUgcG9pbnRzPSI3IDMgNyA4IDE1IDgiLz48L3N2Zz4KICAgICAgR3VhcmRhciBib3JyYWRvcgogICAgPC9idXR0b24+CiAgICA8YnV0dG9uIGNsYXNzPSJidG4tc3VibWl0IiBvbmNsaWNrPSJjcmVhclJlcXVpc2ljaW9uKCkiPgogICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgd2lkdGg9IjE2IiBoZWlnaHQ9IjE2IiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwb2x5bGluZSBwb2ludHM9IjIwIDYgOSAxNyA0IDEyIi8+PC9zdmc+CiAgICAgIENyZWFyIHJlcXVpc2ljacOzbgogICAgICA8c3ZnIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgc3R5bGU9Im1hcmdpbi1sZWZ0OjRweDsiIHdpZHRoPSIxNCIgaGVpZ2h0PSIxNCIgdmlld0JveD0iMCAwIDI0IDI0IiBmaWxsPSJub25lIiBzdHJva2U9ImN1cnJlbnRDb2xvciIgc3Ryb2tlLXdpZHRoPSIyIj48cG9seWxpbmUgcG9pbnRzPSI2IDkgMTIgMTUgMTggOSIvPjwvc3ZnPgogICAgPC9idXR0b24+CiAgPC9kaXY+CjwvZm9vdGVyPgoKPHNjcmlwdD4KbGV0IGVzdGFkbyA9IHsgcHJvdmVlZG9yOiBudWxsLCBhbG1hY2VuOiBudWxsLCBwYXJ0aWRhczogW10sIHByb2R1Y3RvVGVtcG9yYWw6IG51bGwgfTsKbGV0IHRpbWVvdXRzID0ge307CgovLyBGZWNoYXMgaW5pY2lhbGVzCmNvbnN0IGhveSA9IG5ldyBEYXRlKCk7CmNvbnN0IHJlcSA9IG5ldyBEYXRlKCk7IHJlcS5zZXREYXRlKHJlcS5nZXREYXRlKCkgKyA3KTsKZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1mZWNoYScpLnZhbHVlQXNEYXRlID0gaG95Owpkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLWZlY2hhLXJlcScpLnZhbHVlQXNEYXRlID0gcmVxOwoKZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1jb21lbnRhcmlvcycpLmFkZEV2ZW50TGlzdGVuZXIoJ2lucHV0JywgZnVuY3Rpb24oZSkgewogIGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3IoJy5jaGFyLWNvdW50ZXInKS50ZXh0Q29udGVudCA9IGAke3RoaXMudmFsdWUubGVuZ3RofS81MDBgOwp9KTsKCmRvY3VtZW50LmFkZEV2ZW50TGlzdGVuZXIoJ2NsaWNrJywgKGUpID0+IHsKICBpZiAoIWUudGFyZ2V0LmNsb3Nlc3QoJy5kcm9wZG93bi1jb250YWluZXInKSkgewogICAgZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbCgnLnJlc3VsdGFkb3MnKS5mb3JFYWNoKGVsID0+IGVsLmNsYXNzTGlzdC5yZW1vdmUoJ3Zpc2libGUnKSk7CiAgfQp9KTsKCmZ1bmN0aW9uIG1vc3RyYXJCYW5uZXIobXNnLCB0aXBvKSB7CiAgY29uc3QgYiA9IGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCdiYW5uZXInKTsKICBiLnRleHRDb250ZW50ID0gbXNnOwogIGIuY2xhc3NOYW1lID0gdGlwbzsKICBjbGVhclRpbWVvdXQod2luZG93Ll9iYW5uZXJUaW1lb3V0KTsKICB3aW5kb3cuX2Jhbm5lclRpbWVvdXQgPSBzZXRUaW1lb3V0KCgpID0+IHsgYi5jbGFzc05hbWUgPSAnJzsgYi5zdHlsZS5kaXNwbGF5ID0gJ25vbmUnOyB9LCA0MDAwKTsKfQoKZnVuY3Rpb24gcmVuZGVyUmVzdWx0YWRvcyhjb250SWQsIGZpbGFzLCByZW5kZXIsIG9uUGljaykgewogIGNvbnN0IGNvbnQgPSBkb2N1bWVudC5nZXRFbGVtZW50QnlJZChjb250SWQpOwogIGNvbnQuaW5uZXJIVE1MID0gJyc7CiAgaWYgKCFmaWxhcyB8fCBmaWxhcy5sZW5ndGggPT09IDApIHsKICAgIGNvbnQuaW5uZXJIVE1MID0gJzxkaXYgY2xhc3M9InJlc3VsdGFkby12YWNpbyI+Tm8gc2UgZW5jb250cmFyb24gcmVzdWx0YWRvczwvZGl2Pic7CiAgfSBlbHNlIHsKICAgIGZpbGFzLmZvckVhY2goKGZpbGEpID0+IHsKICAgICAgY29uc3QgZGl2ID0gZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnZGl2Jyk7CiAgICAgIGRpdi5jbGFzc05hbWUgPSAncmVzdWx0YWRvLWl0ZW0nOwogICAgICBkaXYuaW5uZXJIVE1MID0gcmVuZGVyKGZpbGEpOwogICAgICBkaXYub25jbGljayA9IChlKSA9PiB7IGUuc3RvcFByb3BhZ2F0aW9uKCk7IG9uUGljayhmaWxhKTsgY29udC5jbGFzc0xpc3QucmVtb3ZlKCd2aXNpYmxlJyk7IH07CiAgICAgIGNvbnQuYXBwZW5kQ2hpbGQoZGl2KTsKICAgIH0pOwogIH0KICBjb250LmNsYXNzTGlzdC5hZGQoJ3Zpc2libGUnKTsKfQoKZnVuY3Rpb24gZGVib3VuY2VTZWFyY2goaWQsIGZ1bmMsIGRlbGF5PTMwMCkgewogIGNvbnN0IGlucHV0ID0gZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoaWQpOwogIGlucHV0LmFkZEV2ZW50TGlzdGVuZXIoJ2lucHV0JywgKCkgPT4gewogICAgY2xlYXJUaW1lb3V0KHRpbWVvdXRzW2lkXSk7CiAgICB0aW1lb3V0c1tpZF0gPSBzZXRUaW1lb3V0KCgpID0+IHsgZnVuYyhpbnB1dC52YWx1ZS50cmltKCkpOyB9LCBkZWxheSk7CiAgfSk7CiAgaW5wdXQuYWRkRXZlbnRMaXN0ZW5lcignY2xpY2snLCAoKSA9PiB7CiAgICBpZighZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ3Jlcy0nICsgaWQuc3BsaXQoJy0nKVsxXSkuY2xhc3NMaXN0LmNvbnRhaW5zKCd2aXNpYmxlJykpewogICAgICAgIGZ1bmMoaW5wdXQudmFsdWUudHJpbSgpKTsKICAgIH0KICB9KTsKfQoKYXN5bmMgZnVuY3Rpb24gYnVzY2FyUHJvdmVlZG9yKHRleHRvKSB7CiAgY29uc3QgZmlsYXMgPSBhd2FpdCB3aW5kb3cucHl3ZWJ2aWV3LmFwaS5idXNjYXJfcHJvdmVlZG9yZXModGV4dG8pOwogIHJlbmRlclJlc3VsdGFkb3MoJ3Jlcy1wcm92ZWVkb3InLCBmaWxhcywKICAgIChmKSA9PiBgPGRpdiBjbGFzcz0icHJpbmNpcGFsIj4ke2YuT2ZmaWNpYWxOYW1lfTwvZGl2PjxkaXYgY2xhc3M9InNlY3VuZGFyaW8iPlJGQzogJHtmLlJGQyB8fCAnTi9BJ308L2Rpdj5gLAogICAgKGYpID0+IHsKICAgICAgZXN0YWRvLnByb3ZlZWRvciA9IGY7CiAgICAgIGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCdpbnAtcHJvdmVlZG9yJykudmFsdWUgPSBmLk9mZmljaWFsTmFtZTsKICAgIH0pOwp9CmRlYm91bmNlU2VhcmNoKCdpbnAtcHJvdmVlZG9yJywgYnVzY2FyUHJvdmVlZG9yKTsKCmFzeW5jIGZ1bmN0aW9uIGJ1c2NhckFsbWFjZW4odGV4dG8pIHsKICBjb25zdCBmaWxhcyA9IGF3YWl0IHdpbmRvdy5weXdlYnZpZXcuYXBpLmJ1c2Nhcl9hbG1hY2VuZXModGV4dG8pOwogIHJlbmRlclJlc3VsdGFkb3MoJ3Jlcy1hbG1hY2VuJywgZmlsYXMsCiAgICAoZikgPT4gYDxkaXYgY2xhc3M9InByaW5jaXBhbCI+JHtmLkRlcG90TmFtZX08L2Rpdj5gLAogICAgKGYpID0+IHsKICAgICAgZXN0YWRvLmFsbWFjZW4gPSBmOwogICAgICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLWFsbWFjZW4nKS52YWx1ZSA9IGYuRGVwb3ROYW1lOwogICAgfSk7Cn0KZGVib3VuY2VTZWFyY2goJ2lucC1hbG1hY2VuJywgYnVzY2FyQWxtYWNlbik7Cgphc3luYyBmdW5jdGlvbiBidXNjYXJQcm9kdWN0byh0ZXh0bykgewogIGlmICghZXN0YWRvLmFsbWFjZW4pIHJldHVybiBtb3N0cmFyQmFubmVyKCdTZWxlY2Npb25hIHByaW1lcm8gZWwgYWxtYWPDqW4gZGVzdGlubycsICd3YXJuJyk7CiAgY29uc3QgZmlsYXMgPSBhd2FpdCB3aW5kb3cucHl3ZWJ2aWV3LmFwaS5idXNjYXJfcHJvZHVjdG9zKHRleHRvLCBlc3RhZG8uYWxtYWNlbi5EZXBvdElEKTsKICByZW5kZXJSZXN1bHRhZG9zKCdyZXMtcHJvZHVjdG8nLCBmaWxhcywKICAgIChmKSA9PiBgPGRpdiBjbGFzcz0icHJpbmNpcGFsIj4ke2YuUHJvZHVjdEtleX0gLSAke2YuUHJvZHVjdE5hbWV9PC9kaXY+PGRpdiBjbGFzcz0ic2VjdW5kYXJpbyI+JHtmLlVuaXR9ICZtaWRkb3Q7IEV4aXN0ZW5jaWE6ICR7TnVtYmVyKGYuU3RvY2spLnRvRml4ZWQoMil9PC9kaXY+YCwKICAgIChmKSA9PiB7CiAgICAgIGVzdGFkby5wcm9kdWN0b1RlbXBvcmFsID0gZjsKICAgICAgZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1wcm9kdWN0bycpLnZhbHVlID0gZi5Qcm9kdWN0TmFtZTsKICAgICAgZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC11bScpLnZhbHVlID0gZi5Vbml0OwogICAgICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLWNhbnRpZGFkJykuZm9jdXMoKTsKICAgIH0pOwp9CmRlYm91bmNlU2VhcmNoKCdpbnAtcHJvZHVjdG8nLCBidXNjYXJQcm9kdWN0byk7CgpmdW5jdGlvbiBhZ3JlZ2FyUGFydGlkYU1hbnVhbCgpIHsKICBpZiAoIWVzdGFkby5wcm9kdWN0b1RlbXBvcmFsKSB7CiAgICByZXR1cm4gbW9zdHJhckJhbm5lcignQnVzY2EgeSBzZWxlY2Npb25hIHVuIHByb2R1Y3RvLicsICd3YXJuJyk7CiAgfQogIGNvbnN0IGYgPSBlc3RhZG8ucHJvZHVjdG9UZW1wb3JhbDsKICBjb25zdCBjYW50aWRhZCA9IHBhcnNlRmxvYXQoZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1jYW50aWRhZCcpLnZhbHVlKSB8fCAwOwogIGlmIChjYW50aWRhZCA8PSAwKSByZXR1cm4gbW9zdHJhckJhbm5lcignQ2FudGlkYWQgaW52w6FsaWRhLicsICd3YXJuJyk7CgogIGVzdGFkby5wYXJ0aWRhcy5wdXNoKHsKICAgIHByb2R1Y3RfaWQ6IGYuUHJvZHVjdElELCBjbGF2ZTogZi5Qcm9kdWN0S2V5LCBkZXNjcmlwY2lvbjogZi5Qcm9kdWN0TmFtZSwKICAgIHVuaWRhZDogZi5Vbml0LCBzdG9jazogZi5TdG9jaywgY2FudGlkYWQ6IGNhbnRpZGFkLCBjb21lbnRhcmlvOiAnJwogIH0pOwogIAogIGVzdGFkby5wcm9kdWN0b1RlbXBvcmFsID0gbnVsbDsKICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLXByb2R1Y3RvJykudmFsdWUgPSAnJzsKICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLXVtJykudmFsdWUgPSAnJzsKICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnaW5wLWNhbnRpZGFkJykudmFsdWUgPSAnMSc7CiAgcmVuZGVyVGFibGEoKTsKfQoKZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1jYW50aWRhZCcpLmFkZEV2ZW50TGlzdGVuZXIoJ2tleXByZXNzJywgKGUpID0+IHsKICBpZiAoZS5rZXkgPT09ICdFbnRlcicpIGFncmVnYXJQYXJ0aWRhTWFudWFsKCk7Cn0pOwoKZnVuY3Rpb24gcmVuZGVyVGFibGEoKSB7CiAgY29uc3QgdGJvZHkgPSBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgndGFibGEtcGFydGlkYXMnKTsKICBjb25zdCBjID0gZXN0YWRvLnBhcnRpZGFzLmxlbmd0aDsKICBkb2N1bWVudC5nZXRFbGVtZW50QnlJZCgnYmFkZ2UtcGFydGlkYXMtY291bnQnKS50ZXh0Q29udGVudCA9IGAke2N9IHBhcnRpZGEke2MhPT0xPydzJzonJ31gOwogIGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCd0eHQtbW9zdHJhbmRvJykudGV4dENvbnRlbnQgPSBgTW9zdHJhbmRvICR7Y30gZGUgJHtjfSBwYXJ0aWRhc2A7CiAgCiAgaWYgKGMgPT09IDApIHsKICAgIHRib2R5LmlubmVySFRNTCA9ICc8dHI+PHRkIGNvbHNwYW49IjciIGNsYXNzPSJ2YWNpby10YWJsYSI+Tm8gaGF5IHBhcnRpZGFzIGFncmVnYWRhczwvdGQ+PC90cj4nOwogICAgcmV0dXJuOwogIH0KICB0Ym9keS5pbm5lckhUTUwgPSBlc3RhZG8ucGFydGlkYXMubWFwKChwLCBpKSA9PiBgCiAgICA8dHI+CiAgICAgIDx0ZCBzdHlsZT0iY29sb3I6dmFyKC0tdGV4dC1zZWNvbmRhcnkpOyBmb250LXNpemU6MTJweDsiPiR7cC5jbGF2ZX08L3RkPgogICAgICA8dGQgc3R5bGU9ImZvbnQtd2VpZ2h0OjUwMDsiPiR7cC5kZXNjcmlwY2lvbn08L3RkPgogICAgICA8dGQgc3R5bGU9ImNvbG9yOnZhcigtLXRleHQtc2Vjb25kYXJ5KTsgZm9udC1zaXplOjEycHg7Ij4ke3AudW5pZGFkfTwvdGQ+CiAgICAgIDx0ZD48c3BhbiBjbGFzcz0iZG90ICR7cC5zdG9jaz4wPydncmVlbic6J3JlZCd9Ij48L3NwYW4+JHtOdW1iZXIocC5zdG9jaykudG9GaXhlZCgwKX08L3RkPgogICAgICA8dGQ+JHtOdW1iZXIocC5jYW50aWRhZCkudG9GaXhlZCgwKX08L3RkPgogICAgICA8dGQ+CiAgICAgICAgPGlucHV0IHR5cGU9InRleHQiIHZhbHVlPSIke3AuY29tZW50YXJpb30iIHBsYWNlaG9sZGVyPSJPYnNlcnZhY2lvbmVzLi4uIiBvbmNoYW5nZT0iZXN0YWRvLnBhcnRpZGFzWyR7aX1dLmNvbWVudGFyaW89dGhpcy52YWx1ZSIgCiAgICAgICAgICAgICAgIHN0eWxlPSJib3JkZXI6bm9uZTsgYm9yZGVyLWJvdHRvbToxcHggc29saWQgdHJhbnNwYXJlbnQ7IGJvcmRlci1yYWRpdXM6MDsgcGFkZGluZzo0cHggMDsgYmFja2dyb3VuZDp0cmFuc3BhcmVudDsiPgogICAgICA8L3RkPgogICAgICA8dGQgc3R5bGU9InRleHQtYWxpZ246cmlnaHQ7Ij4KICAgICAgICA8ZGl2IGNsYXNzPSJ0YWJsZS1hY3Rpb25zIiBzdHlsZT0ianVzdGlmeS1jb250ZW50OmZsZXgtZW5kOyI+CiAgICAgICAgICA8YnV0dG9uIGNsYXNzPSJidG4taWNvbiI+PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PHBhdGggZD0iTTEyIDIwaDkiLz48cGF0aCBkPSJNMTYuNSAzLjVhMi4xMjEgMi4xMjEgMCAwIDEgMyAzTDcgMTlsLTQgMSAxLTRMMTYuNSAzLjV6Ii8+PC9zdmc+PC9idXR0b24+CiAgICAgICAgICA8YnV0dG9uIGNsYXNzPSJidG4taWNvbiBkZWxldGUiIG9uY2xpY2s9InF1aXRhclBhcnRpZGEoJHtpfSkiPjxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiB2aWV3Qm94PSIwIDAgMjQgMjQiIGZpbGw9Im5vbmUiIHN0cm9rZT0iY3VycmVudENvbG9yIiBzdHJva2Utd2lkdGg9IjIiPjxwb2x5bGluZSBwb2ludHM9IjMgNiA1IDYgMjEgNiIvPjxwYXRoIGQ9Ik0xOSA2djE0YTIgMiAwIDAgMS0yIDJIN2EyIDIgMCAwIDEtMi0yVjZtMyAwVjRhMiAyIDAgMCAxIDItMmg0YTIgMiAwIDAgMSAyIDJ2MiIvPjxsaW5lIHgxPSIxMCIgeTE9IjExIiB4Mj0iMTAiIHkyPSIxNyIvPjxsaW5lIHgxPSIxNCIgeTE9IjExIiB4Mj0iMTQiIHkyPSIxNyIvPjwvc3ZnPjwvYnV0dG9uPgogICAgICAgIDwvZGl2PgogICAgICA8L3RkPgogICAgPC90cj5gKS5qb2luKCcnKTsKfQoKZnVuY3Rpb24gcXVpdGFyUGFydGlkYShpKSB7IGVzdGFkby5wYXJ0aWRhcy5zcGxpY2UoaSwgMSk7IHJlbmRlclRhYmxhKCk7IH0KZnVuY3Rpb24gY2FuY2VsYXIoKSB7IHdpbmRvdy5weXdlYnZpZXcuYXBpLmNlcnJhcigpOyB9Cgphc3luYyBmdW5jdGlvbiBjcmVhclJlcXVpc2ljaW9uKCkgewogIGlmICghZXN0YWRvLnByb3ZlZWRvcikgcmV0dXJuIG1vc3RyYXJCYW5uZXIoJ1NlbGVjY2lvbmEgZWwgU29saWNpdGFudGUvUHJvdmVlZG9yLicsICd3YXJuJyk7CiAgaWYgKCFlc3RhZG8uYWxtYWNlbikgcmV0dXJuIG1vc3RyYXJCYW5uZXIoJ1NlbGVjY2lvbmEgZWwgQWxtYWPDqW4gZGVzdGluby4nLCAnd2FybicpOwogIGlmIChlc3RhZG8ucGFydGlkYXMubGVuZ3RoID09PSAwKSByZXR1cm4gbW9zdHJhckJhbm5lcignQWdyZWdhIGFsIG1lbm9zIHVuYSBwYXJ0aWRhLicsICd3YXJuJyk7CgogIGNvbnN0IGJ0biA9IGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3IoJy5idG4tc3VibWl0Jyk7CiAgYnRuLmRpc2FibGVkID0gdHJ1ZTsKCiAgY29uc3QgcyA9IGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCdpbnAtc2VyaWUnKS52YWx1ZS50cmltKCk7CiAgY29uc3QgZiA9IGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCdpbnAtZm9saW8nKS52YWx1ZS50cmltKCk7CiAgCiAgY29uc3QgZGF0b3NfZG9jID0gewogICAgZmVjaGE6IGRvY3VtZW50LmdldEVsZW1lbnRCeUlkKCdpbnAtZmVjaGEnKS52YWx1ZSwKICAgIGZlY2hhX3JlcTogZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1mZWNoYS1yZXEnKS52YWx1ZSwKICAgIHNlcmllOiBzID09PSAnQXV0b23DoXRpY2EnID8gJycgOiBzLAogICAgZm9saW86IGYgPT09ICdBdXRvbcOhdGljbycgPyAnJyA6IGYsCiAgICBjb21lbnRhcmlvczogZG9jdW1lbnQuZ2V0RWxlbWVudEJ5SWQoJ2lucC1jb21lbnRhcmlvcycpLnZhbHVlLnRyaW0oKQogIH07CgogIGNvbnN0IHIgPSBhd2FpdCB3aW5kb3cucHl3ZWJ2aWV3LmFwaS5jcmVhcl9yZXF1aXNpY2lvbigKICAgIGVzdGFkby5wcm92ZWVkb3IuQnVzaW5lc3NFbnRpdHlJRCwgZXN0YWRvLmFsbWFjZW4uRGVwb3RJRCwgZXN0YWRvLnBhcnRpZGFzLCBkYXRvc19kb2MKICApOwogIAogIGlmIChyLm9rKSB7CiAgICBtb3N0cmFyQmFubmVyKCfCoVJlcXVpc2ljacOzbiBndWFyZGFkYSBjb24gw6l4aXRvIScsICdvaycpOwogICAgc2V0VGltZW91dCgoKSA9PiB3aW5kb3cucHl3ZWJ2aWV3LmFwaS5jZXJyYXIoKSwgMjAwMCk7CiAgfSBlbHNlIHsKICAgIG1vc3RyYXJCYW5uZXIoJ0Vycm9yOiAnICsgci5lcnJvciwgJ2Vycm9yJyk7CiAgICBidG4uZGlzYWJsZWQgPSBmYWxzZTsKICB9Cn0KPC9zY3JpcHQ+CjwvYm9keT4KPC9odG1sPgo=").decode("utf-8")


# =============================================================================
# ARRANQUE
# =============================================================================

api = Api()
window = webview.create_window(
    "Requisicion de compra", html=HTML, js_api=api,
    width=980, height=800, min_size=(820, 680), resizable=True,
)
api.window = window

webview.start(gui='edgechromium')

result = "Ventana de requisicion cerrada."
