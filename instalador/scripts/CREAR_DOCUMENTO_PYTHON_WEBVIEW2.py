# lang: python
# timeout: 1800
# AppKey recomendado: CREAR_DOCUMENTO_PYTHON_WEBVIEW2
# Plantilla: Crear documento (Python · ventana HTML)
# Categoria: Documentos
# Documentacion: CREAR_DOCUMENTO.html
# Crea un documento de Comercial desde una ventana HTML (WebView2): factura de cliente, pedido, remisión, factura de compra, orden de compra o recepción.
# Es una plantilla de EJEMPLO funcional: ábrela, pruébala, y copia lo que necesites. Documentación: clic secundario sobre la plantilla → «Ver documentación».
#
# Qué enseña:
#   · El patrón completo de creación: NuevoDocumento → perfil del módulo → AgregarArticulo × N → RecalcCompleto → AffectStockNEW (solo si el módulo lo pide) → Save → agenda de pago.
#   · Documentos DERIVADOS: selecciona antes una o varias órdenes de compra (o un pedido) en la lista y la ventana ofrece partir de ellas con lo que aún falta por surtir.
#   · ctx.show_html_formulario: una ventana HTML de dos vías que devuelve lo capturado (el timeout de arriba es del script completo: la persona puede tardar).
# Para un botón de un solo tipo (por ejemplo solo «Orden de compra») deja esa fila en la tabla TIPOS y borra las demás.
# Nota: el cuerpo de la página (HTML) es idéntico al de la plantilla de C#; la diferencia está solo en el lenguaje que crea el documento.

import json
import datetime
import os

from broslmv import ctx


def S(v):
    return "" if v is None else str(v)


def I(v):
    try:
        return int(float(v))
    except Exception:
        return 0


def D(v):
    try:
        return float(v)
    except Exception:
        return 0.0


def Sq(s):
    """Texto para un literal SQL."""
    return S(s).replace("'", "''")


def Num(x):
    """Número para un literal SQL (siempre con punto)."""
    t = ("%.8f" % float(x)).rstrip("0").rstrip(".")
    return t if t not in ("", "-") else "0"


def fecha_txt(v):
    if v is None:
        return ""
    return v.strftime("%Y-%m-%d") if hasattr(v, "strftime") else S(v)[:10]


empresa = ctx.erp.OwnedBusinessEntityId

# ===================================================================================================================================
# TIPOS DE DOCUMENTO. Cada fila es el «perfil» que Comercial espera de ese módulo (confirmado contra capturas del documento nativo).
# Para quitar un tipo del formulario borra su fila; para agregar otro, copia una fila y ajusta sus datos. Nada más depende de esta tabla.
#   clave · nombre · módulo · lado (C = cliente, P = proveedor) · de dónde sale el precio (venta/compra) · perfil SQL de encabezado ·
#   ¿lleva condición de pago? · ¿lleva fecha de entrega? · cómo se liga al origen (""/cabecera/partida/entrega) · módulo de origen
# ===================================================================================================================================
def T(clave, nombre, modulo, lado, precio, perfil, condicion, entrega, vinculo, origen):
    return {"clave": clave, "nombre": nombre, "modulo": modulo, "lado": lado, "precio": precio, "perfil": perfil,
            "condicion": condicion, "entrega": entrega, "vinculo": vinculo, "origen": origen}


TIPOS = [
    T("factura_cliente", "Factura de cliente",  21,  "C", "venta",  "DepotIDFrom=0, StatusDeliveryID=0",                            True,  False, "",         0),
    T("pedido",          "Pedido de cliente",   967, "C", "venta",  "DepotIDFrom=0, StatusDeliveryID=3, StatusPaidID=0",            True,  True,  "",         0),
    T("remision",        "Remisión (entrega)",  157, "C", "venta",  "DepotIDFrom=0, PaymentTermID=0, StatusDeliveryID=0",           False, True,  "cabecera", 967),
    T("factura_compra",  "Factura de compra",   152, "P", "compra", "DepotIDFrom=0, StatusPaidID=3",                                True,  False, "partida",  183),
    T("orden_compra",    "Orden de compra",     183, "P", "compra", "DepotIDFrom=0, UserID=0",                                      True,  True,  "",         0),
    T("recepcion",       "Recepción de compra", 184, "P", "compra", "DepotIDFrom=0, UserID=0, PaymentTermID=0, StatusDeliveryID=0", False, True,  "entrega",  183),
]
TIPO_POR = {t["clave"]: t for t in TIPOS}
CLAVES = [t["clave"] for t in TIPOS]


# ---------- Catálogos para el formulario ----------
def catalogos(claves):
    cat = {}
    cat["almacenes"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query(
        "SELECT DepotID AS id, DepotName AS nombre FROM orgDepot WHERE DeletedOn IS NULL AND OwnedBusinessEntityID = " + str(empresa) + " ORDER BY DepotName")]

    # Persona con lo que ayuda a decidir al capturar: RFC, condición de pago y descuento habituales, límite de crédito, saldo abierto y fecha de su último documento.
    # Saldo = facturas, notas de cargo, recibos y gastos con saldo menos notas de crédito con saldo (docDocument.Balance; es la foto de hoy).
    def ent(tabla):
        return ("SELECT be.BusinessEntityID AS id, ISNULL(be.CommercialName, be.OfficialName) AS nombre, ISNULL(mi.OfficialNumber,'') AS rfc, ISNULL(x.PaymentTermID,0) AS cond, ISNULL(x.Discount,0) AS descto, "
                "ISNULL(x.CreditLimit,0) AS credito, ISNULL(sd.Saldo,0) AS saldo, sd.Ultimo AS ultimo, ISNULL(x.CurrencyID,0) AS moneda, " + ("ISNULL(x.ReceptorUsoCFDI,'')" if tabla == "orgCustomer" else "''") + " AS uso FROM " + tabla + " x "
                "JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID "
                "LEFT JOIN (SELECT d.BusinessEntityID, SUM(CASE WHEN d.DocumentTypeID = 6 THEN -ISNULL(d.Balance,0) ELSE ISNULL(d.Balance,0) END) AS Saldo, CONVERT(VARCHAR(10), MAX(d.DateDocument), 23) AS Ultimo FROM docDocument d "
                "  WHERE d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND d.OwnedBusinessEntityID = " + str(empresa) + " AND d.DocumentTypeID IN (5,6,7,8,9,13) GROUP BY d.BusinessEntityID) sd ON sd.BusinessEntityID = be.BusinessEntityID "
                "WHERE be.DeletedOn IS NULL AND x.DeletedOn IS NULL ORDER BY nombre")

    def lista(tabla):
        return [{"id": I(r["id"]), "nombre": S(r["nombre"]), "rfc": S(r["rfc"]), "cond": I(r["cond"]), "desc": D(r["descto"]), "credito": D(r["credito"]),
                 "saldo": D(r["saldo"]), "ultimo": S(r["ultimo"]), "moneda": I(r["moneda"]), "uso": S(r["uso"])} for r in ctx.query(ent(tabla))]

    lados = set(t["lado"] for t in TIPOS if t["clave"] in claves)
    cat["clientes"] = lista("orgCustomer") if "C" in lados else []
    cat["proveedores"] = lista("orgSupplier") if "P" in lados else []
    cat["condiciones"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "venta": I(r["v"]) == 1, "compra": I(r["c"]) == 1} for r in ctx.query(
        "SELECT PaymentTermID AS id, PaymentTermName AS nombre, Sales AS v, Buys AS c FROM engPaymentTerm WHERE DeletedOn IS NULL ORDER BY PaymentTermID")]
    # Moneda con su tipo de cambio; catálogos del SAT (forma de pago, método de pago, uso del CFDI) tal como los guarda Comercial; centros de costo
    cat["monedas"] = [{"id": I(r["id"]), "simbolo": S(r["simbolo"]), "nombre": S(r["nombre"]), "tc": D(r["tc"])} for r in ctx.query(
        "SELECT CurrencyID AS id, IntlSymbol AS simbolo, Currency AS nombre, Rate AS tc FROM vwLBSCurrencyList ORDER BY CurrencyID")]

    def sat(g1, g2=None):
        sql = "SELECT ISNULL(Custom1,'') AS clave, ItemValue AS nombre FROM engRefCombo WHERE DeletedOn IS NULL AND CboGroupName = N'%s' AND ISNULL(Custom1,'') <> '' ORDER BY CboOrder, ItemData"
        l = ctx.query(sql % g1)
        if len(l) == 0 and g2:
            l = ctx.query(sql % g2)
        return [{"clave": S(r["clave"]), "nombre": S(r["nombre"])} for r in l]
    cat["formas"] = sat("Anexo20v33_FormaPago")
    cat["metodos"] = sat("Anexo20v33_MetodoDePago")
    cat["usos"] = sat("Anexo20v40_UsoCFDI", "Anexo20v33_UsoCFDI")
    cat["centros"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query(
        "SELECT CostCenterID AS id, CostCenterName AS nombre FROM orgCostCenter WHERE DeletedOn IS NULL AND OwnedBusinessEntityID IN (0, " + str(empresa) + ") ORDER BY CostCenterName")]
    cat["impuestos"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "perc": D(r["perc"])} for r in ctx.query(
        "SELECT t.TaxTypeID AS id, t.TaxTypeName AS nombre, ISNULL(tp.IVA_Perc,0) AS perc FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID ORDER BY t.TaxTypeName")]
    # Productos: lo mínimo para buscar y poner precio. Tope de seguridad de 30,000.
    cat["productos"] = [{"id": I(r["id"]), "clave": S(r["clave"]), "nombre": S(r["nombre"]), "unidad": S(r["unidad"]), "imp": I(r["imp"]),
                         "venta": D(r["venta"]), "costo": D(r["costo"]), "barras": S(r["barras"]), "lote": I(r["lote"]) == 1, "serie": I(r["serie"]) == 1, "servicio": I(r["servicio"]) == 1} for r in ctx.query(
        "SELECT TOP 30000 ProductID AS id, ISNULL(ProductKey,'') AS clave, ProductName AS nombre, ISNULL(Unit,'') AS unidad, ISNULL(TaxTypeID,0) AS imp, "
        "ISNULL(PriceList,0) AS venta, ISNULL(CostPrice,0) AS costo, ISNULL(BarCode,'') AS barras, ISNULL(UseLot,0) AS lote, ISNULL(UseSerialNumber,0) AS serie, ISNULL(ProductIsService,0) AS servicio "
        "FROM orgProduct WHERE DeletedOn IS NULL ORDER BY ProductName")]
    # Existencias por almacén (suma del kardex): {productoId: {almacenId: cantidad}}
    exist = {}
    try:
        for r in ctx.query("SELECT ProductID, DepotID, SUM(Quantity) AS Q FROM orgProductKardex WHERE ISNULL(Cancelled,0) = 0 GROUP BY ProductID, DepotID HAVING ABS(SUM(Quantity)) > 0.00001"):
            exist.setdefault(S(r["ProductID"]), {})[S(r["DepotID"])] = round(D(r["Q"]), 4)
    except Exception:
        pass
    cat["existencias"] = exist
    # Siguiente folio probable de cada tipo (lo asigna Comercial al guardar; aquí solo se muestra)
    folios = {}
    for t in TIPOS:
        if t["clave"] in claves:
            folios[t["clave"]] = I(ctx.scalar("SELECT ISNULL((SELECT TOP 1 ISNULL(TRY_CONVERT(int, Folio),0) + 1 FROM docDocument WHERE ModuleID = " + str(t["modulo"]) + " AND OwnedBusinessEntityID = " + str(empresa) + " AND DeletedOn IS NULL ORDER BY DocumentID DESC), 1)"))
    cat["folios"] = folios
    return cat


# ---------- Documentos de origen (los que estaban seleccionados al lanzar el botón) ----------
# Para cada documento seleccionado cuyo módulo sirve de origen de algún tipo: sus partidas con lo que AÚN falta por surtir PARA CADA TIPO DERIVADO
# (una orden de compra puede estar toda recibida y aún sin facturar). «Lo ya surtido» se cuenta por la columna de vínculo propia de cada tipo:
#   Recepción → DeliverDocumentItemID · Factura de compra → SourceDocumentItemID · Remisión → por producto, dentro de las remisiones que apuntan al pedido (SourceDocumentID).
def origenes_de(ids):
    res = []
    if not ids:
        return res
    docs = ctx.query(
        "SELECT d.DocumentID, d.ModuleID, d.FolioPrefix, d.Folio, d.BusinessEntityID, d.DepotID, ISNULL(be.CommercialName, be.OfficialName) AS Entidad FROM docDocument d "
        "LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID WHERE d.DocumentID IN (" + ",".join(str(int(x)) for x in ids) + ") "
        "AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND d.OwnedBusinessEntityID = " + str(empresa))
    for d in docs:
        mod = I(d["ModuleID"])
        did = I(d["DocumentID"])
        derivados = [t for t in TIPOS if t["origen"] == mod]
        if not derivados:
            continue
        por_tipo = {}
        for t in derivados:
            v = t["vinculo"]
            m_der = t["modulo"]
            if v == "cabecera":
                sql = ("SELECT MIN(i.DocumentItemID) AS Item, i.ProductID, ISNULL(MAX(p.ProductKey),'') AS Clave, MAX(i.Description) AS Descr, ISNULL(MAX(i.Unit),'') AS Unidad, "
                       "SUM(i.Quantity) - ISNULL((SELECT SUM(x.Quantity) FROM docDocumentItem x JOIN docDocument xd ON xd.DocumentID = x.DocumentID WHERE x.DeletedOn IS NULL AND xd.DeletedOn IS NULL AND xd.CancelledOn IS NULL "
                       "  AND xd.SourceDocumentID = " + str(did) + " AND xd.ModuleID = " + str(m_der) + " AND x.ProductID = i.ProductID), 0) AS Pend, MAX(ISNULL(i.UnitPrice,0)) AS Precio, MAX(ISNULL(i.DiscountPerc,0)) AS Descuento, MAX(ISNULL(i.TaxTypeID,0)) AS Imp "
                       "FROM docDocumentItem i LEFT JOIN orgProduct p ON p.ProductID = i.ProductID WHERE i.DocumentID = " + str(did) + " AND i.DeletedOn IS NULL AND i.ProductID > 0 GROUP BY i.ProductID ORDER BY MIN(i.DocumentItemID)")
            else:
                col = "x.DeliverDocumentItemID" if v == "entrega" else "x.SourceDocumentItemID"
                sql = ("SELECT i.DocumentItemID AS Item, i.ProductID, ISNULL(p.ProductKey,'') AS Clave, i.Description AS Descr, ISNULL(i.Unit,'') AS Unidad, "
                       "i.Quantity - ISNULL((SELECT SUM(x.Quantity) FROM docDocumentItem x JOIN docDocument xd ON xd.DocumentID = x.DocumentID WHERE x.DeletedOn IS NULL AND xd.DeletedOn IS NULL AND xd.CancelledOn IS NULL "
                       "  AND " + col + " = i.DocumentItemID AND xd.ModuleID = " + str(m_der) + "), 0) AS Pend, ISNULL(i.UnitPrice,0) AS Precio, ISNULL(i.DiscountPerc,0) AS Descuento, ISNULL(i.TaxTypeID,0) AS Imp "
                       "FROM docDocumentItem i LEFT JOIN orgProduct p ON p.ProductID = i.ProductID WHERE i.DocumentID = " + str(did) + " AND i.DeletedOn IS NULL AND i.ProductID > 0 ORDER BY i.DocumentItemID")
            items = []
            for p in ctx.query(sql):
                pend = D(p["Pend"])
                if pend <= 0.00001:
                    continue
                items.append({"origenItem": I(p["Item"]), "id": I(p["ProductID"]), "clave": S(p["Clave"]), "nombre": S(p["Descr"]), "unidad": S(p["Unidad"]),
                              "cant": pend, "precio": D(p["Precio"]), "desc": round(D(p["Descuento"]) * 100.0, 4), "imp": I(p["Imp"])})
            por_tipo[t["clave"]] = items
        res.append({"id": did, "modulo": mod, "folio": (S(d["FolioPrefix"]) + S(d["Folio"])).strip(), "entidad": I(d["BusinessEntityID"]),
                    "entidadNombre": S(d["Entidad"]), "almacen": I(d["DepotID"]), "partidasPor": por_tipo})
    return res


# ---------- Agenda de pago ----------
# NuevoDocumento deja una parcialidad «de relleno» con importe 0; después de guardar hay que rehacerla con el total real y la condición de pago elegida.
# Cada renglón de engPaymentTermDetail vence en (fecha + PaymentUnit × días del periodo) y lleva su porcentaje; el último absorbe el redondeo.
def armar_agenda(doc, condicion, fecha):
    total = D(ctx.scalar("SELECT Total FROM docDocument WHERE DocumentID=" + str(doc)))
    det = ctx.query("SELECT d.PaymentPerc, d.PaymentUnit, ISNULL(p.Dias,1) AS Dias, ISNULL(d.ForceEndOfMonth,0) AS FinMes FROM engPaymentTermDetail d "
                    "LEFT JOIN vwcboPaymentTermDays p ON p.PaymentPeriodID = d.PaymentPeriodID WHERE d.PaymentTermID = " + str(condicion) + " ORDER BY d.PaymentTermDetailID")
    if not det:
        return
    uid = str(ctx.user_id)
    ctx.execute("UPDATE docDocumentPaymentAgenda SET DeletedOn=GETDATE(), DeletedBy=" + uid + " WHERE DeletedOn IS NULL AND DocumentID=" + str(doc))
    acumulado = 0.0
    n = 0
    for d in det:
        n += 1
        vence = fecha + datetime.timedelta(days=D(d["PaymentUnit"]) * D(d["Dias"]))
        if I(d["FinMes"]) == 1:
            siguiente = (vence.replace(day=28) + datetime.timedelta(days=4)).replace(day=1)
            vence = siguiente - datetime.timedelta(days=1)
        perc = D(d["PaymentPerc"])
        monto = round(total - acumulado, 2) if n == len(det) else round(total * perc / 100.0, 2)
        acumulado += monto
        ctx.execute("INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" + str(doc) + ", '"
                    + vence.strftime("%Y%m%d") + "', " + Num(perc) + ", " + Num(monto) + ", " + str(n) + ", GETDATE(), " + uid + ")")


def _error_erp():
    try:
        return ctx.erp.LastError()
    except Exception:
        return ""


# ---------- Crear el documento ----------
# «spec»: tipo, almacen, entidad, condicion, fecha (yyyy-MM-dd), entrega (yyyy-MM-dd), titulo, comentarios, origenes [ids], partidas [{id, cant, precio, desc (0-100), imp, origenItem}]
# Regresa el DocumentID. Si algo no es válido lanza una excepción con un mensaje que se le puede mostrar a la persona.
def crear_documento(spec):
    clave = S(spec.get("tipo"))
    if clave not in TIPO_POR:
        raise Exception("Tipo de documento desconocido: " + clave)
    t = TIPO_POR[clave]
    modulo = t["modulo"]
    vinculo = t["vinculo"]
    venta = t["precio"] == "venta"
    almacen = I(spec.get("almacen"))
    entidad = I(spec.get("entidad"))
    if almacen <= 0:
        raise Exception("Elige el almacén.")
    if entidad <= 0:
        raise Exception("Elige el " + ("cliente" if t["lado"] == "C" else "proveedor") + ".")
    partidas = spec.get("partidas") or []
    if len(partidas) == 0:
        raise Exception("Agrega al menos una partida.")
    for n, p in enumerate(partidas, 1):
        if I(p.get("id")) <= 0:
            raise Exception("La partida " + str(n) + " no tiene producto.")
        if D(p.get("cant")) <= 0:
            raise Exception("La partida " + str(n) + " debe tener cantidad mayor a cero.")
        if D(p.get("precio")) < 0:
            raise Exception("La partida " + str(n) + " tiene precio negativo.")
        if D(p.get("desc")) < 0 or D(p.get("desc")) > 100:
            raise Exception("El descuento de la partida " + str(n) + " debe estar entre 0 y 100.")
    origenes = [I(x) for x in (spec.get("origenes") or []) if I(x) > 0]
    if vinculo == "":
        origenes = []

    doc = ctx.erp.NuevoDocumento(modulo, almacen, entidad)
    if not doc or doc <= 0 or _error_erp():
        raise Exception("No se pudo crear el documento: " + S(_error_erp()))

    try:
        # Encabezado: perfil del módulo + lo que capturó la persona
        centro = I(spec.get("centro"))
        sets = [t["perfil"], "CampaignID=NULL", "CostCenterID=" + (str(centro) if centro > 0 else "NULL"), "ProjectID=NULL"]
        moneda = I(spec.get("moneda"))
        tc = D(spec.get("tc"))
        if moneda > 0:
            sets.append("CurrencyID=" + str(moneda) + ", Rate=" + Num(tc if tc > 0 else 1))
        if t["condicion"]:
            sets.append("PaymentTermID=" + str(I(spec.get("condicion"))))
        fecha = S(spec.get("fecha"))
        if len(fecha) >= 10:
            sets.append("DateDocument='" + Sq(fecha[:10].replace("-", "")) + "'")
        entrega = S(spec.get("entrega"))
        if t["entrega"] and len(entrega) >= 10:
            e8 = Sq(entrega[:10].replace("-", ""))
            sets.append("DateDelivery='" + e8 + "', DateDocDelivery='" + e8 + "'")
        titulo = S(spec.get("titulo"))
        if titulo:
            sets.append("Title=N'" + Sq(titulo) + "'")
        coment = S(spec.get("comentarios"))
        if coment:
            sets.append("Comments=N'" + Sq(coment) + "'")
        if origenes:
            sets.append("SourceDocumentID=" + str(origenes[0]))   # el sistema solo guarda UN origen en el encabezado; cada partida liga el suyo
        ctx.execute("UPDATE docDocument SET " + ", ".join(sets) + " WHERE DocumentID=" + str(doc))

        for p in partidas:
            pid = I(p.get("id"))
            cant = D(p.get("cant"))
            precio = D(p.get("precio"))
            imp = I(p.get("imp"))
            origen_item = I(p.get("origenItem"))
            # Costo: compra → el precio pactado; remisión → costo promedio del producto; el resto de ventas no lleva costo
            costo = -1
            if not venta:
                costo = precio
            elif modulo == 157:
                costo = D(ctx.scalar("SELECT ISNULL(MAX(TOT_Cost),0) FROM orgProductCostComercial WHERE ProductID=" + str(pid) + " AND DepotID=" + str(almacen)))
            item = ctx.erp.AgregarArticulo(doc, pid, cant, precio, costo, imp if imp > 0 else -1, D(p.get("desc")) / 100.0, origen_item if vinculo == "entrega" else 0)
            if _error_erp():
                raise Exception("No se pudo agregar la partida de «" + S(p.get("nombre") or pid) + "»: " + S(_error_erp()))
            if vinculo == "partida" and origen_item > 0:
                ctx.execute("UPDATE docDocumentItem SET SourceDocumentItemID=" + str(origen_item) + " WHERE DocumentItemID=" + str(item))

        ctx.erp.RecalcCompleto(doc)
        # ¿Mueve o compromete inventario? Lo decide el módulo (StockAffectation ≠ 0), no el tipo de documento
        if I(ctx.scalar("SELECT ISNULL(MAX(TRY_CONVERT(int, Value)),0) FROM engModuleParameter WHERE ParameterKey='StockAffectation' AND ModuleID=" + str(modulo))) != 0:
            ctx.erp.AffectStockNEW(doc)
            if _error_erp():
                raise Exception("AffectStockNEW: " + S(_error_erp()))
        ctx.erp.Save(doc)
        if _error_erp():
            raise Exception("Save: " + S(_error_erp()))
        if clave == "factura_cliente":                     # datos del comprobante: el motor deja la fila de docDocumentCFD con valores por omisión (G03 / PPD / 99)
            cf = []
            for col, llave in (("ReceptorUsoCFDI", "uso"), ("FormaPago", "forma"), ("MetodoPago", "metodo")):
                if S(spec.get(llave)) != "":
                    cf.append(col + "=N'" + Sq(S(spec.get(llave))) + "'")
            if cf:
                ctx.execute("UPDATE docDocumentCFD SET " + ", ".join(cf) + " WHERE DocumentID=" + str(doc))
        if t["condicion"]:
            try:
                base = datetime.datetime.strptime(fecha[:10], "%Y-%m-%d") if len(fecha) >= 10 else datetime.datetime.today()
            except Exception:
                base = datetime.datetime.today()
            armar_agenda(doc, I(spec.get("condicion")), base)   # agenda de pago con los montos reales
            try:
                ctx.erp.UpdateDocumentPaidInfo(doc)              # saldo y balance
            except Exception:
                pass
        if vinculo != "":
            try:
                ctx.erp.UpdateStatusDelivery(doc)                # estado de entrega del documento de origen
            except Exception:
                pass
        return doc
    except Exception as ex:
        # El documento ya existe como borrador: se avisa su número para que no quede perdido
        raise Exception(S(ex) + "\n\nQuedó un documento incompleto (id " + str(doc) + "): elimínalo o cancélalo desde Comercial.")


# ---------- Qué documentos seleccionados sirven de origen ----------
try:
    seleccion = [int(x) for x in (ctx.get_selected_ids() or [])]
except Exception:
    seleccion = []

# ---------- Pruebas automáticas (sin ventanas): variable de entorno BROSLMV_DOC_TEST (JSON con el «spec»), resultado en BROSLMV_DOC_OUT ----------
_modo_prueba = os.environ.get("BROSLMV_DOC_TEST")
if _modo_prueba:
    _spec = json.loads(_modo_prueba)
    if "seleccion" in _spec:
        seleccion = [int(x) for x in _spec["seleccion"]]
    if _spec.get("pendientes"):
        _res = json.dumps(origenes_de(seleccion), ensure_ascii=False, default=str)
    elif _spec.get("catalogo"):
        _res = json.dumps(catalogos(CLAVES), ensure_ascii=False, default=str)
    else:
        try:
            _res = "DOC " + str(crear_documento(_spec))
        except Exception as _ex:
            _res = "ERROR " + S(_ex)
    _salida = os.environ.get("BROSLMV_DOC_OUT")
    if _salida:
        with open(_salida, "w", encoding="utf-8") as _f:
            _f.write(_res)
    result = _res



# ===================================================================================================================================
# VENTANA (WebView2). El formulario es una página HTML; al presionar «Crear documento» regresa lo capturado y aquí se crea el documento.
# Si falla, la ventana se vuelve a abrir con lo que ya capturó la persona (no se pierde nada).
# ===================================================================================================================================
PAGINA = r'''<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'><title>Crear documento</title><style>
:root{--marino:#15324F;--marino2:#1d4468;--azul:#2D6FE0;--acc:#2D6FE0;--accsuave:#E8F0FF;--texto:#16263A;--suave:#64748B;--linea:#D8E0EB;--fondo:#EEF2F7;--tarjeta:#fff;--rojo:#C82828;--ambar:#B45309;--verde:#16803B;--zebra:#F8FAFC}
body.compra{--acc:#0F766E;--accsuave:#E3F5F2}
*{box-sizing:border-box}html,body{height:100%}body{margin:0;font:13px 'Segoe UI',Arial,sans-serif;background:var(--fondo);color:var(--texto);display:flex;flex-direction:column;overflow:hidden}
button,input,select,textarea{font:inherit;color:var(--texto)}
/* ---- cinta superior ---- */
.cinta{background:linear-gradient(180deg,var(--marino2),var(--marino));color:#fff;padding:10px 18px 12px;display:flex;gap:18px;align-items:stretch;flex-wrap:nowrap;box-shadow:0 2px 8px #0003;z-index:5}
.marca{display:flex;flex-direction:column;justify-content:center;min-width:0;flex:1;overflow:hidden}.marca small{color:#93C5FD;font-weight:700;letter-spacing:.14em;font-size:10px}.marca b{font-size:19px;font-weight:650;margin-top:1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.marca span{color:#B6C7DA;font-size:11.5px;margin-top:2px}
.acciones{display:flex;gap:6px;align-items:stretch}.ab{border:0;background:transparent;color:#E8EEF6;border-radius:9px;padding:6px 12px;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;min-width:92px;gap:2px}
.ab i{font-style:normal;font-size:22px;line-height:1.1}.ab span{font-size:11px;line-height:1.15;text-align:center}.ab em{font-style:normal;color:#93A9C2;font-size:10px}.ab:hover{background:#ffffff1f}.ab:disabled{opacity:.45;cursor:default}
.ab.p{background:var(--acc);color:#fff;box-shadow:0 1px 0 #fff3 inset}.ab.p:hover{filter:brightness(1.1);background:var(--acc)}.ab.p em{color:#ffffffb0}
.sep{width:1px;background:#ffffff30;margin:6px 4px}
.info{margin-left:auto;display:grid;grid-template-columns:repeat(3,auto);gap:4px 14px;align-items:center;border:1px solid #ffffff2c;border-radius:10px;padding:8px 14px;background:#ffffff10}
.info label{display:block;font-size:10px;color:#9FB4CC;letter-spacing:.08em;text-transform:uppercase;margin:0 0 2px}.info input,.info select{background:#fff;border:1px solid #fff;border-radius:6px;padding:4px 7px;width:140px}.info .fol{font-size:15px;font-weight:650;color:#fff}.info .fol small{display:block;font-weight:400;font-size:10px;color:#9FB4CC}
/* ---- tipos ---- */
.tipos{background:#fff;border-bottom:1px solid var(--linea);padding:8px 18px;display:flex;gap:6px;align-items:center;flex-wrap:wrap}
.grp{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:var(--suave);font-weight:700;margin:0 4px 0 10px}.grp:first-child{margin-left:0}
.tipo{border:1px solid var(--linea);background:#fff;border-radius:999px;padding:5px 13px 5px 9px;cursor:pointer;display:flex;gap:6px;align-items:center}.tipo i{font-style:normal}.tipo:hover{border-color:var(--acc)}
.tipo.on{background:var(--acc);border-color:var(--acc);color:#fff;font-weight:600}.tipo.v.on{background:#2D6FE0;border-color:#2D6FE0}.tipo.c.on{background:#0F766E;border-color:#0F766E}
/* ---- cuerpo ---- */
.cuerpo{flex:1;min-height:0;display:flex;gap:14px;padding:14px 18px}
.izq{flex:1;min-width:0;overflow:auto;padding-right:6px}.der{width:340px;flex:0 0 340px;overflow:auto;display:flex;flex-direction:column;gap:12px}
.card{background:var(--tarjeta);border:1px solid var(--linea);border-radius:12px;padding:12px 14px;margin-bottom:12px;box-shadow:0 1px 2px #1b2a3d0d}
.card h2{margin:0 0 9px;font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:var(--suave);display:flex;align-items:center;gap:8px}
.card h2 b{display:inline-flex;width:20px;height:20px;border-radius:50%;background:var(--acc);color:#fff;align-items:center;justify-content:center;font-size:11px;letter-spacing:0}
.card h2 .der2{margin-left:auto;text-transform:none;letter-spacing:0;font-weight:400}
label{display:block;color:var(--suave);font-size:12px;margin-bottom:3px}
input,select,textarea{border:1px solid var(--linea);border-radius:7px;padding:6px 9px;background:#fff;width:100%}input:focus,select:focus,textarea:focus{outline:2px solid var(--acc);outline-offset:-1px;border-color:var(--acc)}
input.mal{border-color:var(--rojo);outline:2px solid #C8282833}
.fila{display:grid;gap:10px 14px}.f2{grid-template-columns:2fr 1fr}.f3{grid-template-columns:repeat(3,1fr)}
.combo{position:relative}.lista{position:absolute;left:0;right:0;top:100%;z-index:30;background:#fff;border:1px solid var(--linea);border-radius:9px;max-height:300px;overflow:auto;box-shadow:0 10px 28px #0003;display:none;margin-top:3px}
.lista div{padding:7px 10px;cursor:pointer;display:flex;justify-content:space-between;gap:12px;align-items:center;border-bottom:1px solid #f0f3f8}.lista div:last-child{border:0}.lista div:hover,.lista div.sel{background:var(--accsuave)}
.lista b{font-weight:600}.lista small{color:var(--suave);white-space:nowrap}.lista .vacio{cursor:default;color:var(--suave);justify-content:center}
.chip{display:inline-block;border-radius:999px;padding:1px 8px;font-size:11px;background:#E5EAF1;color:var(--suave);white-space:nowrap}.chip.r{background:#FDE4E4;color:var(--rojo)}.chip.a{background:#FDF0DC;color:var(--ambar)}.chip.v{background:#E0F3E6;color:var(--verde)}.chip.b{background:var(--accsuave);color:var(--acc)}
.ent{display:none;margin-top:10px;border:1px solid var(--linea);border-radius:10px;padding:9px 12px;background:var(--zebra);gap:8px 18px;grid-template-columns:repeat(4,auto);justify-content:start}.ent div b{display:block;font-size:10px;text-transform:uppercase;letter-spacing:.07em;color:var(--suave);font-weight:600}.ent div span{font-size:13.5px;font-variant-numeric:tabular-nums}
.org{display:flex;flex-direction:column;gap:6px}.orgi{display:flex;gap:10px;align-items:center;border:1px solid var(--linea);border-radius:9px;padding:7px 10px;cursor:pointer}.orgi:hover{border-color:var(--acc)}.orgi.on{background:var(--accsuave);border-color:var(--acc)}.orgi input{width:auto}.orgi .t{flex:1}.orgi .t small{display:block;color:var(--suave)}
/* ---- partidas ---- */
.tw{overflow:auto;border:1px solid var(--linea);border-radius:10px}table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}
th{background:var(--zebra);font-size:10.5px;text-transform:uppercase;letter-spacing:.06em;color:var(--suave);text-align:right;padding:7px 8px;border-bottom:1px solid var(--linea);position:sticky;top:0;z-index:1;white-space:nowrap}th.l,td.l{text-align:left}
td{padding:4px 6px;text-align:right;border-bottom:1px solid #eef2f7;vertical-align:middle}tr:last-child td{border:0}td input,td select{padding:4px 6px;text-align:right}td .cl{color:var(--suave);font-size:11px;display:block}td.nom{text-align:left;min-width:220px}
td.x button{border:0;background:transparent;color:var(--suave);cursor:pointer;font-size:15px;border-radius:6px;padding:2px 7px}td.x button:hover{background:#FDE4E4;color:var(--rojo)}
.vacioP{padding:26px;text-align:center;color:var(--suave)}.vacioP b{display:block;font-size:15px;color:var(--texto);margin-bottom:3px}
/* ---- lado derecho ---- */
.res{background:linear-gradient(180deg,#fff,#F5F9FF)}.tot{display:grid;grid-template-columns:1fr auto;gap:5px 10px;font-variant-numeric:tabular-nums}.tot span:nth-child(even){text-align:right}.tot .g{font-size:22px;font-weight:700;color:var(--acc);border-top:1px solid var(--linea);padding-top:7px;margin-top:3px}
.barra{height:8px;border-radius:5px;background:#E5EAF1;overflow:hidden;margin:5px 0 3px}.barra i{display:block;height:100%;background:var(--verde);border-radius:5px}.barra.r i{background:var(--rojo)}.barra.a i{background:#D97706}
.hist{display:flex;flex-direction:column;gap:3px}.hist a{display:flex;justify-content:space-between;gap:8px;padding:5px 7px;border-radius:7px;cursor:pointer;color:var(--texto);text-decoration:none}.hist a:hover{background:var(--accsuave)}.hist small{color:var(--suave)}
.aviso{border-radius:9px;padding:7px 10px;margin-top:8px;font-size:12px}.aviso.a{background:#FFF7E6;color:#7A4B06;border:1px solid #F0D9AD}.aviso.r{background:#FDECEC;color:#B42318;border:1px solid #F4C4C4}.aviso.v{background:#EAF7EE;color:#166534;border:1px solid #BFE3CB}
.pie{background:#fff;border-top:1px solid var(--linea);padding:6px 18px;color:var(--suave);font-size:11.5px;display:flex;gap:18px;flex-wrap:wrap}.pie b{color:var(--texto);font-weight:600}
.toast{position:fixed;left:50%;bottom:44px;transform:translateX(-50%);background:#16263A;color:#fff;padding:9px 16px;border-radius:10px;box-shadow:0 8px 24px #0005;z-index:60;display:none;max-width:70vw}.toast.mal{background:#B42318}.toast.bien{background:#166534}
.kbd{border:1px solid var(--linea);border-bottom-width:2px;border-radius:5px;padding:0 5px;font-size:10.5px;color:var(--suave);background:#fff}
td .pn{display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;line-height:1.3}
@media (max-width:1180px){.cinta{flex-wrap:wrap}}
@media (max-width:1100px){.cuerpo{flex-direction:column;overflow:auto}.der{width:auto;flex:none}.izq{overflow:visible}}
</style></head><body>
<div class='cinta'>
 <div class='marca'><small>BROSLMV</small><b id='ttl'>Nuevo documento</b><span id='sub'></span></div>
 <div class='acciones'>
  <button class='ab p' id='bGuardar' onclick='crear(false)'><i>💾</i><span>Guardar y abrir</span><em>F5</em></button>
  <button class='ab' id='bNuevo' onclick='crear(true)'><i>➕</i><span>Guardar y nuevo</span><em>F6</em></button>
  <div class='sep'></div>
  <button class='ab' onclick='limpiar()'><i>🧹</i><span>Limpiar</span><em>&nbsp;</em></button>
  <button class='ab' onclick='cancelar()'><i>✕</i><span>Cancelar</span><em>Esc</em></button>
 </div>
 <div class='info'>
  <div><label>Fecha</label><input type='date' id='fecha'></div>
  <div><label>Folio</label><div class='fol' id='folio'>—<small>lo asigna Comercial</small></div></div>
  <div><label>Almacén</label><select id='alm'></select></div>
 </div>
</div>
<div class='tipos' id='tipos'></div>
<div class='cuerpo'>
 <div class='izq'>
  <div class='card'><h2><b>1</b><span id='lblEnt'>Cliente</span><span class='der2'><span class='kbd'>F2</span> buscar</span></h2>
   <div class='fila f2'>
    <div><div class='combo'><input id='ent' placeholder='Escribe nombre, RFC o clave…' autocomplete='off'><div class='lista' id='lstEnt'></div></div></div>
    <div id='bxCond'><select id='cond'></select></div>
   </div>
   <div class='ent' id='entCard'></div>
   <div id='avisoEnt'></div>
  </div>
  <div class='card'><h2>Moneda, centro de costo<span id='lblCfdi'> y datos fiscales del CFDI</span></h2>
   <div style='display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:10px'>
    <div><label>Moneda</label><select id='mon'></select></div>
    <div><label>Tipo de cambio</label><input type='number' id='tc' min='0' step='0.0001'></div>
    <div><label>Centro de costo</label><select id='cc'></select></div>
   </div>
   <div id='bxCfdi' style='display:none;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:10px;margin-top:10px'>
    <div><label>Uso del CFDI</label><select id='uso'></select></div>
    <div><label>Forma de pago</label><select id='forma'></select></div>
    <div><label>Método de pago</label><select id='metodo'></select></div>
   </div>
  </div>
  <div class='card' id='cardOrigen' style='display:none'><h2><b>2</b>Partir de un documento ya existente<span class='der2' id='orgEstado'></span></h2><div class='org' id='origenes'></div>
   <div class='nota' style='color:var(--suave);font-size:12px;margin-top:6px'>Se cargan solo las partidas que aún faltan por surtir; puedes combinar varios documentos de la misma persona.</div></div>
  <div class='card'><h2><b id='nPart'>2</b>Partidas<span class='der2'><span class='kbd'>F3</span> buscar producto · escanea o escribe el código de barras y Enter</span></h2>
   <div class='combo' style='max-width:640px;margin-bottom:9px'><input id='prod' placeholder='Buscar producto por nombre, clave o código de barras…' autocomplete='off'><div class='lista' id='lstProd'></div></div>
   <div class='tw'><table><thead><tr><th class='l'>Producto</th><th>Existencia</th><th>Cantidad</th><th>Precio</th><th>Desc. %</th><th>Impuesto</th><th>Importe</th><th></th></tr></thead><tbody id='tb'></tbody></table></div>
  </div>
  <div class='card'><div class='fila f2'><div><label>Título (opcional)</label><input id='titulo' maxlength='120' placeholder='Referencia corta que verás en la lista'></div><div id='bxEntrega'><label>Fecha de entrega</label><input type='date' id='entrega'></div></div>
   <div style='margin-top:10px'><label>Comentarios (opcional)</label><textarea id='coment' rows='2' placeholder='Notas para este documento'></textarea></div></div>
 </div>
 <div class='der'>
  <div class='card res'><h2>Resumen</h2>
   <div class='tot'><span>Partidas</span><span id='tN'>0</span><span>Piezas</span><span id='tP'>0</span><span>Subtotal</span><span id='tSub'>0.00</span><span>Descuento</span><span id='tDes'>0.00</span><span>Impuestos</span><span id='tImp'>0.00</span><span class='g'>Total</span><span class='g' id='tTot'>0.00</span></div>
   <div id='avisosRes'></div>
   <div style='color:var(--suave);font-size:11.5px;margin-top:7px'>Total estimado: Comercial lo recalcula al guardar (descuentos globales, redondeos).</div>
  </div>
  <div class='card' id='cardCred' style='display:none'><h2>Crédito</h2><div id='cred'></div></div>
  <div class='card' id='cardHist' style='display:none'><h2>Últimos documentos <span class='der2' style='font-size:11px'>clic para abrir</span></h2><div class='hist' id='hist'></div></div>
  <div class='card' id='cardAyuda' style='color:var(--suave);font-size:12px'><b style='color:var(--texto)'>Atajos</b><br><span class='kbd'>F2</span> cliente · <span class='kbd'>F3</span> producto · <span class='kbd'>F5</span> guardar y abrir · <span class='kbd'>F6</span> guardar y nuevo · <span class='kbd'>Esc</span> cancelar<br>Siempre se abre el documento nativo de Comercial al guardar.</div>
 </div>
</div>
<div class='pie'><span>Elaboró: <b id='pUsr'>—</b></span><span id='pEmp'></span><span id='pMod'></span><span id='pInv'></span></div>
<div class='toast' id='toast'></div>
<script>
var DATOS=__DATOS__;
function enviar(o){window.chrome.webview.postMessage(JSON.stringify(o));}
function esc(s){return String(s==null?'':s).replace(/[&<>']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;'}[c];});}
function f2(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:2});}
function f0(n){return (n||0).toLocaleString('es-MX',{maximumFractionDigits:4});}
function $(i){return document.getElementById(i);}
function norm(s){return String(s||'').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g,'');}
var CAT=DATOS.cat,TIPOS=DATOS.tipos,VIVO=!!DATOS.vivo,percImp={};CAT.impuestos.forEach(function(i){percImp[i.id]=i.perc;});
var ST={tipo:TIPOS[0].clave,ent:null,partidas:[],origenes:[],pend:[],guardando:false};
function tipoAct(){return TIPOS.filter(function(t){return t.clave===ST.tipo;})[0];}
function ENT(){return tipoAct().lado==='C'?CAT.clientes:CAT.proveedores;}
// ---- consultas en vivo a Comercial (solo con ctx.ShowHtmlModeless; en las versiones modales se usa lo precargado) ----
var _req=0,_pend={};
function llamar(accion,datos){return new Promise(function(ok,mal){if(!VIVO){mal(new Error('sin conexión en vivo'));return;}var id=++_req;_pend[id]=ok;enviar(Object.assign({accion:accion,req:id},datos||{}));setTimeout(function(){if(_pend[id]){delete _pend[id];mal(new Error('tiempo agotado'));}},15000);});}
function respuesta(id,datos){var f=_pend[id];if(f){delete _pend[id];f(datos);}}
// ---- avisos ----
var _t=0;function aviso(m,tipo){var t=$('toast');t.textContent=m;t.className='toast '+(tipo||'');t.style.display='block';clearTimeout(_t);_t=setTimeout(function(){t.style.display='none';},tipo==='mal'?7000:3500);}
// ---- combos de búsqueda (teclado completo) ----
function combo(inp,lst,fuente,fila,elegir,limite,abrirAlFocus){
  var sel=0,vis=[];
  function pintar(){var q=norm(inp.value).split(/\s+/).filter(Boolean);var base=fuente();
    vis=(q.length?base.filter(function(x){var h=norm(fila(x).buscar);return q.every(function(t){return h.indexOf(t)>=0;});}):base).slice(0,limite||40);
    if(sel>=vis.length)sel=Math.max(0,vis.length-1);
    lst.innerHTML=vis.length?vis.map(function(x,i){var e=fila(x);return '<div data-i=\''+i+'\' class=\''+(i===sel?'sel':'')+'\'><span><b>'+esc(e.a)+'</b>'+(e.b?' <small>'+esc(e.b)+'</small>':'')+'</span><small>'+(e.c||'')+'</small></div>';}).join(''):'<div class=vacio>Sin resultados</div>';lst.style.display='block';}
  inp.addEventListener('input',function(){sel=0;pintar();});inp.addEventListener('focus',function(){inp.select();if(abrirAlFocus)pintar();});
  inp.addEventListener('keydown',function(ev){if(ev.key==='ArrowDown'){sel=Math.min(vis.length-1,sel+1);pintar();ev.preventDefault();}else if(ev.key==='ArrowUp'){sel=Math.max(0,sel-1);pintar();ev.preventDefault();}
    else if(ev.key==='Enter'){var x=vis[sel];if(inp.__exacto){var ex=inp.__exacto(inp.value);if(ex)x=ex;}if(x){elegir(x);lst.style.display='none';}ev.preventDefault();ev.stopPropagation();}else if(ev.key==='Escape'){lst.style.display='none';ev.stopPropagation();}});
  lst.addEventListener('mousedown',function(ev){var d=ev.target.closest('div[data-i]');if(d){elegir(vis[+d.getAttribute('data-i')]);lst.style.display='none';ev.preventDefault();}});
  inp.addEventListener('blur',function(){setTimeout(function(){lst.style.display='none';},130);});
}
combo($('ent'),$('lstEnt'),ENT,function(x){return {a:x.nombre,b:x.rfc,c:(x.saldo?'saldo '+f2(x.saldo):''),buscar:x.nombre+' '+x.rfc+' '+x.id};},elegirEnt,60,true);
$('ent').addEventListener('input',function(){if(ST.ent){ST.ent=null;pintarEnt();}});
combo($('prod'),$('lstProd'),function(){return CAT.productos;},function(x){var ex=existDe(x.id);return {a:x.nombre,b:x.clave,c:(x.servicio?'servicio':'exist. '+f0(ex))+' · '+f2(precioDe(x.id)),buscar:x.nombre+' '+x.clave+' '+x.barras};},function(x){agregar(x);$('prod').value='';},30);
$('prod').__exacto=function(v){v=String(v||'').trim();if(!v)return null;return CAT.productos.filter(function(p){return p.barras&&p.barras===v||norm(p.clave)===norm(v);})[0]||null;};
// ---- tipos ----
function pintarTipos(){var h='',g='';TIPOS.forEach(function(t){var gr=t.lado==='C'?'Ventas':'Compras';if(gr!==g){h+='<span class=grp>'+gr+'</span>';g=gr;}
  h+='<button class=\'tipo '+(t.lado==='C'?'v':'c')+(t.clave===ST.tipo?' on':'')+'\' onclick=\'setTipo(&quot;'+t.clave+'&quot;)\'><i>'+({factura_cliente:'🧾',pedido:'📋',remision:'🚚',factura_compra:'📥',orden_compra:'🛒',recepcion:'📦'}[t.clave]||'📄')+'</i>'+esc(t.nombre)+'</button>';});$('tipos').innerHTML=h;}
function setTipo(c,mantener){var antes=tipoAct().lado;ST.tipo=c;var t=tipoAct();if(t.lado!==antes&&!mantener){ST.ent=null;$('ent').value='';}ST.origenes=[];ST.pend=[];ST.partidas=ST.partidas.filter(function(p){return !p.orig;});
  document.body.className=t.lado==='C'?'venta':'compra';$('lblEnt').textContent=t.lado==='C'?'Cliente':'Proveedor';$('bxCond').style.display=t.condicion?'':'none';$('bxEntrega').style.display=t.entrega?'':'none';var cf=t.clave==='factura_cliente';$('bxCfdi').style.display=cf?'grid':'none';$('lblCfdi').style.display=cf?'':'none';ST.manual=false;
  $('cond').innerHTML=CAT.condiciones.filter(function(x){return t.lado==='C'?x.venta:x.compra;}).map(function(x){return '<option value='+x.id+'>'+esc(x.nombre)+'</option>';}).join('');
  ST.partidas.forEach(function(p){p.precio=precioDe(p.id);});
  $('ttl').textContent='Nuevo documento · '+t.nombre;$('sub').textContent='Módulo '+t.modulo+(t.lado==='C'?' · ventas':' · compras');$('pMod').innerHTML='Módulo <b>'+t.modulo+'</b> · '+esc(t.nombre);
  var f=CAT.folios&&CAT.folios[t.clave];$('folio').innerHTML=(f?'≈ '+f:'—')+'<small>lo asigna Comercial</small>';
  pintarTipos();pintarEnt();pintarOrigenes();pintarPartidas();if(ST.ent)cargarContexto();}
// ---- cliente / proveedor ----
function elegirEnt(x){ST.ent=x;$('ent').value=x.nombre;$('ent').classList.remove('mal');
  if(x.cond&&tipoAct().condicion&&[].some.call($('cond').options,function(o){return +o.value===x.cond;}))$('cond').value=x.cond;
  if(x.moneda&&[].some.call($('mon').options,function(o){return +o.value===x.moneda;})){$('mon').value=x.moneda;cambiaMon();}
  if(x.uso&&[].some.call($('uso').options,function(o){return o.value===x.uso;}))$('uso').value=x.uso;autoMetodo();
  pintarEnt();cargarContexto();pintarPartidas();$('prod').focus();}
function pintarEnt(){var x=ST.ent,c=$('entCard'),t=tipoAct();
  if(!x){c.style.display='none';$('cardCred').style.display='none';$('cardHist').style.display='none';$('avisoEnt').innerHTML='';return;}
  c.style.display='grid';var disp=x.credito>0?x.credito-x.saldo:null;
  c.innerHTML='<div><b>RFC</b><span>'+esc(x.rfc||'—')+'</span></div><div><b>Saldo abierto</b><span>'+f2(x.saldo)+'</span></div>'+(x.credito>0?'<div><b>Límite de crédito</b><span>'+f2(x.credito)+'</span></div>':'')+'<div><b>Último documento</b><span>'+(x.ultimo?fmtF(x.ultimo):'—')+'</span></div>'+(x.desc>0?'<div><b>Descuento habitual</b><span>'+f0(x.desc)+' %</span></div>':'');
  var cc=$('cardCred');if(x.credito>0){cc.style.display='';var uso=Math.min(100,Math.max(0,x.saldo/x.credito*100));$('cred').innerHTML='<div style=\'display:flex;justify-content:space-between\'><span>Usado</span><b>'+f2(x.saldo)+'</b></div><div class=\'barra '+(uso>=100?'r':uso>=80?'a':'')+'\'><i style=\'width:'+uso+'%\'></i></div><div style=\'display:flex;justify-content:space-between;color:var(--suave)\'><span>Disponible</span><span>'+f2(disp)+'</span></div>';}else cc.style.display='none';}
function fmtF(s){if(!s)return '';var p=s.split('-');return p[2]+'/'+p[1]+'/'+p[0];}
function cargarContexto(){var x=ST.ent;if(!x||!VIVO){return;}var t=tipoAct();
  llamar('entidad',{id:x.id}).then(function(h){if(!ST.ent||ST.ent.id!==x.id)return;$('cardHist').style.display=h.length?'':'none';$('hist').innerHTML=h.map(function(d){return '<a onclick=\'abrirDoc('+d.id+','+d.modulo+')\'><span>'+esc(d.tipo)+' <b>'+esc(d.folio)+'</b><small> · '+fmtF(d.fecha)+'</small></span><span>'+f2(d.total)+(d.saldo?' <small style=color:var(--ambar)>debe '+f2(d.saldo)+'</small>':'')+'</span></a>';}).join('');}).catch(function(){});
  if(t.origen){$('orgEstado').textContent='buscando pendientes…';llamar('pendientes',{entidad:x.id,tipo:ST.tipo}).then(function(os){if(!ST.ent||ST.ent.id!==x.id)return;ST.pend=os;pintarOrigenes();}).catch(function(){$('orgEstado').textContent='';});}}
function abrirDoc(id,m){enviar({accion:'abrirDoc',id:id,modulo:m});}
// ---- documentos de origen ----
function origenesVisibles(){var t=tipoAct(),vistos={},lista=[];if(!t.origen)return lista;
  DATOS.origenes.concat(ST.pend).forEach(function(o){if(o.modulo!==t.origen||vistos[o.id])return;if((o.partidasPor[ST.tipo]||[]).length===0)return;if(ST.ent&&o.entidad!==ST.ent.id)return;vistos[o.id]=1;lista.push(o);});return lista;}
function pintarOrigenes(){var t=tipoAct(),os=origenesVisibles();$('cardOrigen').style.display=t.origen?'':'none';
  $('orgEstado').textContent=t.origen?(os.length?os.length+' disponible(s)':(ST.ent?'sin pendientes':'elige primero la persona')):'';
  $('origenes').innerHTML=os.length?os.map(function(o){var n=(o.partidasPor[ST.tipo]||[]).length,on=ST.origenes.indexOf(o.id)>=0;return '<label class=\'orgi '+(on?'on':'')+'\'><input type=checkbox data-id='+o.id+' '+(on?'checked':'')+' onchange=\'alternarOrigen(this)\'><span class=t><b>'+esc(o.folio||('Documento '+o.id))+'</b> · '+esc(o.entidadNombre)+'<small>'+(o.fecha?fmtF(o.fecha)+' · ':'')+n+' partida(s) pendiente(s)'+(o.total?' · total '+f2(o.total):'')+'</small></span></label>';}).join(''):'<div class=vacioP style=padding:8px>'+(t.origen?'Cuando elijas la persona verás aquí sus documentos pendientes de surtir.':'')+'</div>';}
function alternarOrigen(cb){var id=+cb.getAttribute('data-id'),i=ST.origenes.indexOf(id);if(cb.checked&&i<0)ST.origenes.push(id);if(!cb.checked&&i>=0)ST.origenes.splice(i,1);reconstruirOrigenes();}
function reconstruirOrigenes(){
  var os=origenesVisibles().filter(function(o){return ST.origenes.indexOf(o.id)>=0;});
  ST.partidas=ST.partidas.filter(function(p){return !p.orig;});
  os.forEach(function(o){(o.partidasPor[ST.tipo]||[]).forEach(function(p){ST.partidas.push({id:p.id,clave:p.clave,nombre:p.nombre,unidad:p.unidad,cant:p.cant,max:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem,orig:o.id});});});
  if(os.length){var o0=os[0],en=ENT().filter(function(x){return x.id===o0.entidad;})[0];if(en&&(!ST.ent||ST.ent.id!==en.id))elegirEnt(en);else if(!en){ST.ent={id:o0.entidad,nombre:o0.entidadNombre,rfc:'',saldo:0,credito:0,desc:0,cond:0,ultimo:''};$('ent').value=o0.entidadNombre;pintarEnt();}
    if(o0.almacen)$('alm').value=o0.almacen;}
  pintarOrigenes();pintarPartidas();}
// ---- partidas ----
function prodPor(id){return CAT.productos.filter(function(p){return p.id===id;})[0];}
function precioDe(id){var p=prodPor(id);if(!p)return 0;return tipoAct().precio==='venta'?p.venta:p.costo;}
function existDe(id){var e=(CAT.existencias||{})[id];if(!e)return 0;var a=$('alm').value;return e[a]||0;}
function agregar(x){var ya=ST.partidas.filter(function(p){return p.id===x.id&&!p.orig;})[0];
  if(ya){ya.cant+=1;}else{ST.partidas.push({id:x.id,clave:x.clave,nombre:x.nombre,unidad:x.unidad,cant:1,precio:precioDe(x.id),desc:(tipoAct().lado==='C'&&ST.ent&&ST.ent.desc)||0,imp:x.imp,origenItem:0,orig:0});}
  pintarPartidas();var f=document.querySelector('#tb tr:last-child input');if(f&&!ya){f.focus();f.select();}}
function pintarPartidas(){var t=tipoAct(),venta=t.precio==='venta';
  $('tb').innerHTML=ST.partidas.length?ST.partidas.map(function(p,i){var pr=prodPor(p.id)||{},ex=existDe(p.id),falta=venta&&!pr.servicio&&!p.orig&&p.cant>ex;
   return '<tr><td class=\'l nom\'><div class=pn title=\''+esc(p.nombre)+'\'>'+esc(p.nombre)+'</div>'+(p.orig?' <span class=\'chip b\'>origen</span>':'')+(pr.lote?' <span class=chip>lote</span>':'')+(pr.serie?' <span class=chip>serie</span>':'')+'<span class=cl>'+esc(p.clave)+(p.unidad?' · '+esc(p.unidad):'')+'</span></td>'
   +'<td>'+(pr.servicio?'<span class=chip>servicio</span>':'<span class=\'chip '+(falta?'a':ex>0?'v':'')+'\'>'+f0(ex)+'</span>')+'</td>'
   +'<td><input type=number step=any min=0 value=\''+p.cant+'\' style=\'width:84px\' oninput=\'edita('+i+',&quot;cant&quot;,this.value)\'></td>'
   +'<td><input type=number step=any min=0 value=\''+p.precio+'\' style=\'width:96px\' oninput=\'edita('+i+',&quot;precio&quot;,this.value)\'></td>'
   +'<td><input type=number step=any min=0 max=100 value=\''+p.desc+'\' style=\'width:64px\' oninput=\'edita('+i+',&quot;desc&quot;,this.value)\'></td>'
   +'<td><select onchange=\'edita('+i+',&quot;imp&quot;,this.value)\'>'+CAT.impuestos.map(function(m){return '<option value='+m.id+(m.id===p.imp?' selected':'')+'>'+esc(m.nombre)+'</option>';}).join('')+'</select></td>'
   +'<td id=imp'+i+' style=\'font-weight:600\'></td><td class=x><button onclick=\'quita('+i+')\' title=\'Quitar\'>✕</button></td></tr>';}).join('')
   :'<tr><td colspan=8><div class=vacioP><b>Aún no hay partidas</b>Busca un producto arriba (nombre, clave o código de barras) y presiona Enter.</div></td></tr>';
  totales();}
function edita(i,c,v){var p=ST.partidas[i];v=+v;if(c==='imp')p.imp=v;else{if(isNaN(v)||v<0)v=0;if(c==='desc'&&v>100)v=100;if(c==='cant'&&p.max&&v>p.max)v=p.max;p[c]=v;}totales();}
function quita(i){ST.partidas.splice(i,1);pintarPartidas();}
function totales(){var s=0,d=0,im=0,pz=0;ST.partidas.forEach(function(p,i){var bruto=p.cant*p.precio,desc=bruto*p.desc/100,base=bruto-desc,tax=base*(percImp[p.imp]||0);s+=bruto;d+=desc;im+=tax;pz+=p.cant;var c=$('imp'+i);if(c)c.textContent=f2(base);});
  var tot=s-d+im;$('tN').textContent=ST.partidas.length;$('tP').textContent=f0(pz);$('tSub').textContent=f2(s);$('tDes').textContent=f2(d);$('tImp').textContent=f2(im);$('tTot').textContent=f2(tot);$('nPart').textContent=tipoAct().origen?'3':'2';
  var av='';var t=tipoAct();if(t.lado==='C'&&ST.ent&&ST.ent.credito>0&&ST.ent.saldo+tot>ST.ent.credito)av+='<div class=\'aviso r\'>Con este documento el saldo ('+f2(ST.ent.saldo+tot)+') excede el límite de crédito ('+f2(ST.ent.credito)+').</div>';
  if(t.precio==='venta'){var faltan=ST.partidas.filter(function(p){var pr=prodPor(p.id)||{};return !pr.servicio&&!p.orig&&p.cant>existDe(p.id);}).length;if(faltan)av+='<div class=\'aviso a\'>'+faltan+' partida(s) piden más de la existencia del almacén (se puede guardar; Comercial decide si permite vender en negativo).</div>';}
  if(ST.partidas.some(function(p){return p.precio<=0;}))av+='<div class=\'aviso a\'>Hay partidas con precio en cero.</div>';
  $('avisosRes').innerHTML=av;}
// ---- guardar ----
function validar(){var t=tipoAct();document.querySelectorAll('.mal').forEach(function(e){e.classList.remove('mal');});
  if(!ST.ent){$('ent').classList.add('mal');$('ent').focus();aviso('Elige el '+(t.lado==='C'?'cliente':'proveedor')+' de la lista (escribe y selecciona).','mal');return false;}
  if(!ST.partidas.length){$('prod').focus();aviso('Agrega al menos una partida.','mal');return false;}
  if(ST.partidas.some(function(p){return !(p.cant>0);})){aviso('Todas las partidas deben tener cantidad mayor a cero.','mal');return false;}
  if(tipoAct().clave==='factura_cliente'&&!($('uso').value&&$('forma').value&&$('metodo').value)){aviso('Completa los datos fiscales: uso del CFDI, forma de pago y método de pago.','mal');return false;}
  return true;}
function crear(otro){if(ST.guardando||!validar())return;var t=tipoAct();ST.guardando=true;$('bGuardar').disabled=true;$('bNuevo').disabled=true;$('bGuardar').querySelector('span').textContent='Creando…';
  enviar({accion:'crear',nuevo:!!otro&&VIVO,spec:JSON.stringify({tipo:ST.tipo,almacen:+$('alm').value,entidad:ST.ent.id,condicion:t.condicion?+$('cond').value:0,fecha:$('fecha').value,entrega:t.entrega?$('entrega').value:'',titulo:$('titulo').value,comentarios:$('coment').value,moneda:+$('mon').value,tc:+$('tc').value||1,centro:+$('cc').value||0,uso:$('uso').value,forma:$('forma').value,metodo:$('metodo').value,
    origenes:ST.origenes,partidas:ST.partidas.map(function(p){return {id:p.id,nombre:p.nombre,cant:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem||0};})})});}
function liberar(){ST.guardando=false;$('bGuardar').disabled=false;$('bNuevo').disabled=false;$('bGuardar').querySelector('span').textContent='Guardar y abrir';}
function falloCrear(m){liberar();aviso(m,'mal');var a=document.createElement('div');a.className='aviso r';a.textContent=m;$('avisosRes').insertBefore(a,$('avisosRes').firstChild);}
function creado(i){liberar();aviso('Documento '+(i.folio||i.id)+' creado'+(i.avisoAbrir?'. '+i.avisoAbrir:' y abierto en Comercial.'),i.avisoAbrir?'':'bien');limpiar(true);$('ent').focus();}
function limpiar(silencio){ST.ent=null;ST.partidas=[];ST.origenes=[];ST.pend=[];$('ent').value='';$('titulo').value='';$('coment').value='';$('prod').value='';pintarEnt();pintarOrigenes();pintarPartidas();$('avisosRes').innerHTML='';if(!silencio)$('ent').focus();var f=CAT.folios&&CAT.folios[ST.tipo];}
function cancelar(){if(ST.partidas.length&&!confirm('Hay partidas capturadas. ¿Cerrar sin guardar?'))return;enviar({accion:'cancelar'});}
document.addEventListener('keydown',function(e){if(e.key==='F5'){e.preventDefault();crear(false);}else if(e.key==='F6'){e.preventDefault();crear(true);}else if(e.key==='F2'){e.preventDefault();$('ent').focus();}else if(e.key==='F3'){e.preventDefault();$('prod').focus();}else if(e.key==='Escape'){var ab=document.querySelector('.lista[style*=block]');if(!ab)cancelar();}});
// ---- arranque ----
$('alm').innerHTML=CAT.almacenes.map(function(a){return '<option value='+a.id+'>'+esc(a.nombre)+'</option>';}).join('');$('alm').addEventListener('change',pintarPartidas);
var hoy=new Date(),iso=function(d){return d.getFullYear()+'-'+('0'+(d.getMonth()+1)).slice(-2)+'-'+('0'+d.getDate()).slice(-2);};
function opcs(l,v,t){return l.map(function(x){return '<option value="'+esc(x[v])+'">'+esc(t(x))+'</option>';}).join('');}
$('mon').innerHTML=opcs(CAT.monedas,'id',function(x){return x.simbolo+' · '+x.nombre;});$('mon').value=3;
$('cc').innerHTML='<option value=0>(ninguno)</option>'+opcs(CAT.centros,'id',function(x){return x.nombre;});
$('uso').innerHTML=opcs(CAT.usos,'clave',function(x){return x.clave+' · '+x.nombre;});$('forma').innerHTML=opcs(CAT.formas,'clave',function(x){return x.clave+' · '+x.nombre;});$('metodo').innerHTML=opcs(CAT.metodos,'clave',function(x){return x.clave+' · '+x.nombre;});
$('uso').value='G03';
function cambiaMon(){var m=CAT.monedas.filter(function(x){return x.id===+$('mon').value;})[0];if(m){$('tc').value=m.tc;$('tc').disabled=m.simbolo==='MXN';}pintarPartidas();}
function autoMetodo(){if(ST.manual||tipoAct().clave!=='factura_cliente')return;var c=$('cond').selectedOptions[0],contado=c&&/contado/i.test(c.textContent);$('metodo').value=contado?'PUE':'PPD';$('forma').value=contado?'01':'99';}
$('mon').addEventListener('change',cambiaMon);$('cond').addEventListener('change',autoMetodo);
$('metodo').addEventListener('change',function(){ST.manual=true;if($('metodo').value==='PPD')$('forma').value='99';});$('forma').addEventListener('change',function(){ST.manual=true;});
cambiaMon();
$('fecha').value=iso(hoy);$('entrega').value=iso(hoy);$('pUsr').textContent=DATOS.usuario||'—';$('pEmp').innerHTML=DATOS.empresa?'Empresa <b>'+esc(DATOS.empresa)+'</b>':'';
if(!VIVO)$('bNuevo').style.display='none';
if(DATOS.inicial){var I=DATOS.inicial;ST.tipo=I.tipo;}
setTipo(ST.tipo);
if(DATOS.inicial){var I2=DATOS.inicial;var en=ENT().filter(function(x){return x.id===I2.entidad;})[0];if(en){ST.ent=en;$('ent').value=en.nombre;pintarEnt();}$('alm').value=I2.almacen;if(I2.condicion)$('cond').value=I2.condicion;
  $('fecha').value=I2.fecha||$('fecha').value;$('entrega').value=I2.entrega||$('entrega').value;$('titulo').value=I2.titulo||'';$('coment').value=I2.comentarios||'';
  ST.origenes=I2.origenes||[];ST.partidas=I2.partidas.map(function(p){var pr=prodPor(p.id)||{};var o=p.origenItem?1:0;return {id:p.id,clave:pr.clave||'',nombre:p.nombre||pr.nombre||'',unidad:pr.unidad||'',cant:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem||0,orig:o?(ST.origenes[0]||1):0};});
  pintarOrigenes();pintarPartidas();}
if(DATOS.origenes&&DATOS.origenes.length){var o1=DATOS.origenes[0];/* seleccionados en la lista de Comercial: se propone el tipo que parte de ellos */var ti=TIPOS.filter(function(t){return t.origen===o1.modulo;})[0];if(ti&&!DATOS.inicial){setTipo(ti.clave);}}
if(!DATOS.inicial){var ov=origenesVisibles();if(ov.length){ST.origenes=ov.map(function(o){return o.id;});reconstruirOrigenes();}}
setTimeout(function(){$('ent').focus();},60);
</script></body></html>
'''


def principal():
    global result
    catalogo = catalogos(CLAVES)
    origenes = origenes_de(seleccion)
    inicial = None
    html_prueba = os.environ.get("BROSLMV_DOC_HTML")   # pruebas: escribe la página (con sus datos) en un archivo y no abre ventana
    if html_prueba:
        d0 = {"tipos": TIPOS, "cat": catalogo, "origenes": origenes, "inicial": None}
        with open(html_prueba, "w", encoding="utf-8") as f:
            f.write(PAGINA.replace("__DATOS__", json.dumps(d0, ensure_ascii=False, default=str).replace("</", "<" + chr(92) + "/")))
        result = "HTML"
        return
    while True:
        datos = {"tipos": TIPOS, "cat": catalogo, "origenes": origenes, "inicial": inicial}
        json_txt = json.dumps(datos, ensure_ascii=False, default=str).replace("</", "<" + chr(92) + "/")
        r = ctx.show_html_formulario(PAGINA.replace("__DATOS__", json_txt), "Crear documento", 1180, 880, 1800000)
        if not r.get("submitted") or r.get("accion") != "crear":
            result = "CANCELADO"
            return
        spec = json.loads(r["spec"])
        try:
            doc = crear_documento(spec)
            try:
                ctx.erp.RefreshGrid()
            except Exception:
                pass
            try:
                ctx.erp.AbrirDocumento(doc, TIPO_POR[spec["tipo"]]["modulo"])
            except Exception:
                pass
            result = "OK " + TIPO_POR[spec["tipo"]]["nombre"] + " id=" + str(doc)
            return
        except Exception as ex:
            ctx.msg(str(ex), "Crear documento")
            inicial = spec


if not _modo_prueba:
    principal()
