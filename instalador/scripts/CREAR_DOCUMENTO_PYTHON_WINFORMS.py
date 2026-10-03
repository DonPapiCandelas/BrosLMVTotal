# lang: python
# timeout: 1800
# AppKey recomendado: CREAR_DOCUMENTO_PYTHON_WINFORMS
# Plantilla: Crear documento (Python · ventana Windows Forms)
# Categoria: Documentos
# Documentacion: CREAR_DOCUMENTO.html
# Crea un documento de Comercial desde una ventana de Windows Forms (pythonnet): factura de cliente, pedido, remisión, factura de compra, orden de compra o recepción.
# Es una plantilla de EJEMPLO funcional: ábrela, pruébala, y copia lo que necesites. Documentación: clic secundario sobre la plantilla → «Ver documentación».
#
# Qué enseña:
#   · El patrón completo de creación: NuevoDocumento → perfil del módulo → AgregarArticulo × N → RecalcCompleto → AffectStockNEW (solo si el módulo lo pide) → Save → agenda de pago.
#   · Documentos DERIVADOS: selecciona antes una o varias órdenes de compra (o un pedido) en la lista y la ventana ofrece partir de ellas con lo que aún falta por surtir.
#   · Windows Forms desde Python (pythonnet). Python corre en su propio proceso: la ventana es automáticamente independiente y un error nunca tumba Comercial.
# Para un botón de un solo tipo (por ejemplo solo «Orden de compra») deja esa fila en la tabla TIPOS y borra las demás.

import pythonnet
pythonnet.load("netfx")

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System.Drawing import Point, Size, Color, Font, FontStyle, ContentAlignment
from System.Windows.Forms import (
    Form, FormStartPosition, Label, TextBox, ComboBox, ComboBoxStyle, Button, FlatStyle, DataGridView,
    DataGridViewTextBoxColumn, DataGridViewComboBoxColumn, DataGridViewContentAlignment, DataGridViewAutoSizeColumnsMode,
    DataGridViewSelectionMode, DateTimePicker, DateTimePickerFormat, ListBox, CheckedListBox, CheckState, BorderStyle,
    Cursors, Keys, MessageBox, MessageBoxButtons, MessageBoxIcon,
)

System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA)


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


def L(v):
    return I(v)


# ---------- Borrador y preferencias (archivos en la carpeta local de datos de la persona) ----------
# El borrador guarda lo capturado cada vez que cambia algo: si la ventana se cierra sin querer (o Comercial se cae) se puede recuperar al abrirla de nuevo.
def carpeta_local():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    d = os.path.join(base, "BrosLMV", "borradores")
    os.makedirs(d, exist_ok=True)
    return d


def archivo_borrador(que):
    try:
        uid = int(ctx.user_id)
    except Exception:
        uid = 0
    return os.path.join(carpeta_local(), que + "_" + str(empresa) + "_" + str(uid) + ".json")


def leer_borrador(que, vence=True):
    try:
        a = archivo_borrador(que)
        if not os.path.exists(a):
            return None
        if vence and (datetime.datetime.now().timestamp() - os.path.getmtime(a)) > 7 * 86400:
            return None
        with open(a, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return None


def guardar_borrador(que, obj):
    try:
        with open(archivo_borrador(que), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False)
    except Exception:
        pass


def borrar_borrador(que):
    try:
        a = archivo_borrador(que)
        if os.path.exists(a):
            os.remove(a)
    except Exception:
        pass

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
        "SELECT d.DocumentID, d.ModuleID, d.FolioPrefix, d.Folio, d.BusinessEntityID, d.DepotID, d.DateDocument, ISNULL(d.Total,0) AS Total, ISNULL(be.CommercialName, be.OfficialName) AS Entidad FROM docDocument d "
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
                    "entidadNombre": S(d["Entidad"]), "almacen": I(d["DepotID"]), "fecha": fecha_txt(d["DateDocument"]), "total": D(d["Total"]), "partidasPor": por_tipo})
    return res


# ---------- Consultas en vivo (las pide la ventana mientras se captura) ----------
# Documentos que sirven de origen para el tipo elegido y que pertenecen a esa persona: los 40 más recientes del módulo de origen con partidas que aún faltan por surtir.
def pendientes_de(entidad, clave_tipo):
    if clave_tipo not in TIPO_POR or entidad <= 0:
        return []
    m_origen = TIPO_POR[clave_tipo]["origen"]
    if not m_origen:
        return []
    ids = [I(r["DocumentID"]) for r in ctx.query("SELECT TOP 40 DocumentID FROM docDocument WHERE ModuleID = " + str(m_origen) + " AND BusinessEntityID = " + str(entidad) + " AND OwnedBusinessEntityID = " + str(empresa) +
                                                 " AND DeletedOn IS NULL AND CancelledOn IS NULL ORDER BY DateDocument DESC, DocumentID DESC")]
    return [o for o in origenes_de(ids) if len(o["partidasPor"].get(clave_tipo, [])) > 0]


# Los últimos documentos de esa persona (cualquier módulo), para tener contexto antes de capturar
def ultimos_de(entidad):
    return [{"id": I(r["DocumentID"]), "modulo": I(r["ModuleID"]), "tipo": S(r["Modulo"]), "folio": (S(r["FolioPrefix"]) + S(r["Folio"])).strip(), "fecha": fecha_txt(r["DateDocument"]),
             "total": D(r["Total"]), "saldo": D(r["Saldo"])} for r in ctx.query(
        "SELECT TOP 6 d.DocumentID, d.ModuleID, ISNULL(m.ModuleName,'') AS Modulo, d.FolioPrefix, d.Folio, d.DateDocument, ISNULL(d.Total,0) AS Total, ISNULL(d.Balance,0) AS Saldo FROM docDocument d "
        "LEFT JOIN engModule m ON m.ModuleID = d.ModuleID WHERE d.BusinessEntityID = " + str(entidad) + " AND d.OwnedBusinessEntityID = " + str(empresa) + " AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL ORDER BY d.DateDocument DESC, d.DocumentID DESC")]


# Inteligencia de una persona: facturado (o comprado) por mes en los últimos 12 meses, los productos que más maneja con su último precio, los días promedio que tarda en pagar y el
# último documento de ese tipo con sus partidas (para «Repetir último»). Todo sale de Comercial en ese momento; nada se guarda.
def inteligencia_de(entidad, clave_tipo):
    res = {}
    if clave_tipo not in TIPO_POR or entidad <= 0:
        return res
    t = TIPO_POR[clave_tipo]
    lado = t["lado"]
    mod_fact = [x for x in TIPOS if x["lado"] == lado and x["clave"] in ("factura_cliente", "factura_compra")][0]["modulo"]
    quien = " d.BusinessEntityID = " + str(entidad) + " AND d.OwnedBusinessEntityID = " + str(empresa) + " AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL"
    por_mes = {}
    for r in ctx.query("SELECT CONVERT(CHAR(7), d.DateDocument, 120) AS mes, SUM(ISNULL(d.Total,0)) AS total, COUNT(*) AS n FROM docDocument d WHERE" + quien + " AND d.ModuleID = " + str(mod_fact) +
                       " AND d.DateDocument >= DATEADD(MONTH, -11, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) GROUP BY CONVERT(CHAR(7), d.DateDocument, 120)"):
        por_mes[S(r["mes"])] = (D(r["total"]), D(r["n"]))
    meses = []
    total12 = 0.0
    docs12 = 0
    hoy = datetime.date.today()
    for i in range(11, -1, -1):
        y, m = hoy.year, hoy.month - i
        while m <= 0:
            m += 12
            y -= 1
        k = "%04d-%02d" % (y, m)
        v = por_mes.get(k, (0.0, 0.0))
        meses.append({"mes": k, "total": round(v[0], 2), "n": int(v[1])})
        total12 += v[0]
        docs12 += int(v[1])
    res["meses"] = meses
    res["total12"] = round(total12, 2)
    res["docs12"] = docs12
    res["ticket"] = round(total12 / docs12, 2) if docs12 > 0 else 0.0
    ultimo = ("SELECT TOP 1 i2.%s FROM docDocumentItem i2 JOIN docDocument d2 ON d2.DocumentID = i2.DocumentID WHERE d2.BusinessEntityID = " + str(entidad) + " AND d2.ModuleID = " + str(mod_fact) +
              " AND i2.ProductID = i.ProductID AND i2.DeletedOn IS NULL AND d2.DeletedOn IS NULL AND d2.CancelledOn IS NULL ORDER BY d2.DateDocument DESC, i2.DocumentItemID DESC")
    top = ctx.query("SELECT TOP 10 i.ProductID AS id, MAX(ISNULL(p.ProductKey,'')) AS clave, MAX(ISNULL(p.ProductName, i.Description)) AS nombre, COUNT(DISTINCT d.DocumentID) AS veces, SUM(i.Quantity) AS cant, MAX(d.DateDocument) AS ult, ("
                    + (ultimo % "UnitPrice") + ") AS precio, (" + (ultimo % "DiscountPerc") + ") AS descto FROM docDocumentItem i JOIN docDocument d ON d.DocumentID = i.DocumentID LEFT JOIN orgProduct p ON p.ProductID = i.ProductID WHERE" + quien +
                    " AND d.ModuleID = " + str(mod_fact) + " AND i.DeletedOn IS NULL AND i.ProductID > 0 GROUP BY i.ProductID ORDER BY COUNT(DISTINCT d.DocumentID) DESC, MAX(d.DateDocument) DESC")
    res["top"] = [{"id": I(r["id"]), "clave": S(r["clave"]), "nombre": S(r["nombre"]), "veces": I(r["veces"]), "cant": D(r["cant"]), "ult": fecha_txt(r["ult"]),
                   "precio": D(r["precio"]), "desc": round(D(r["descto"]) * 100.0, 4)} for r in top]
    dias = ctx.scalar("SELECT AVG(CAST(DATEDIFF(DAY, d.DateDocument, p.DateOperation) AS float)) FROM docDocumentPayment p JOIN docDocument d ON d.DocumentID = p.DocumentID WHERE" + quien + " AND d.ModuleID = " + str(mod_fact) + " AND p.DeletedOn IS NULL")
    res["diasPago"] = round(D(dias), 1) if dias is not None else None
    ud = ctx.query("SELECT TOP 1 d.DocumentID, d.FolioPrefix, d.Folio, d.DateDocument FROM docDocument d WHERE" + quien + " AND d.ModuleID = " + str(t["modulo"]) + " ORDER BY d.DateDocument DESC, d.DocumentID DESC")
    if ud:
        idd = I(ud[0]["DocumentID"])
        ps = [{"id": I(r["ProductID"]), "cant": D(r["Quantity"]), "precio": D(r["UnitPrice"]), "desc": round(D(r["DiscountPerc"]) * 100.0, 4), "imp": I(r["TaxTypeID"])} for r in ctx.query(
            "SELECT ProductID, Quantity, UnitPrice, DiscountPerc, TaxTypeID FROM docDocumentItem WHERE DocumentID = " + str(idd) + " AND DeletedOn IS NULL AND ProductID > 0 ORDER BY LineNumber")]
        res["ultimo"] = {"id": idd, "folio": (S(ud[0]["FolioPrefix"]) + S(ud[0]["Folio"])).strip(), "fecha": fecha_txt(ud[0]["DateDocument"]), "partidas": ps}
    else:
        res["ultimo"] = None
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
    elif _spec.get("pendientesDe"):
        _res = json.dumps(pendientes_de(I(_spec["entidad"]), S(_spec["tipo"])), ensure_ascii=False, default=str)
    elif _spec.get("ultimos"):
        _res = json.dumps(ultimos_de(I(_spec["entidad"])), ensure_ascii=False, default=str)
    elif _spec.get("inteligencia"):
        _res = json.dumps(inteligencia_de(I(_spec["entidad"]), S(_spec["tipo"])), ensure_ascii=False, default=str)
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
# VENTANA (Windows Forms). Python corre en su propio proceso: no hace falta modeless ni proteger a Comercial, pero cada manejador va en
# «seguro» para que un error se explique en vez de dejar la ventana muda.
# ===================================================================================================================================
def msg(texto, titulo="Crear documento"):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning)


def seguro(fn):
    def envuelto(sender=None, args=None):
        try:
            fn()
        except Exception as ex:
            msg(str(ex))
    return envuelto


def principal():
    global result
    catalogo = catalogos(CLAVES)
    origenes = origenes_de(seleccion)
    impuestos = catalogo["impuestos"]
    perc_imp = {i["id"]: i["perc"] for i in impuestos}
    nombre_imp = {i["id"]: i["nombre"] for i in impuestos}
    id_imp = {i["nombre"]: i["id"] for i in impuestos}
    productos = catalogo["productos"]
    filas = []            # cada partida: dict(id, clave, nombre, unidad, cant, max, precio, desc, imp, origenItem, origen)
    estado = {"entidad": 0, "origenes": [], "vis_ent": [], "vis_prod": [], "tipo": TIPOS[0], "bloquea": False}

    frm = Form()
    frm.Text = "Crear documento"
    frm.ClientSize = Size(1080, 790)
    frm.StartPosition = FormStartPosition.CenterScreen
    frm.BackColor = Color.FromArgb(244, 246, 249)
    frm.Font = Font("Segoe UI", 9.0)
    frm.KeyPreview = True

    def et(texto, x, y):
        l = Label()
        l.Text = texto
        l.Location = Point(x, y)
        l.AutoSize = True
        l.ForeColor = Color.FromArgb(100, 116, 139)
        frm.Controls.Add(l)
        return l

    def poner(c, x, y, w, h=24):
        c.Location = Point(x, y)
        c.Size = Size(w, h)
        frm.Controls.Add(c)
        return c

    et("Tipo de documento", 16, 12)
    cmb_tipo = poner(ComboBox(), 16, 32, 260)
    cmb_tipo.DropDownStyle = ComboBoxStyle.DropDownList
    for t in TIPOS:
        cmb_tipo.Items.Add(t["nombre"])
    cmb_tipo.SelectedIndex = 0

    lbl_ent = et("Cliente", 296, 12)
    txt_ent = poner(TextBox(), 296, 32, 400)
    lst_ent = ListBox()
    lst_ent.Visible = False
    lst_ent.Location = Point(296, 56)
    lst_ent.Size = Size(400, 150)
    frm.Controls.Add(lst_ent)
    et("Almacén", 716, 12)
    cmb_alm = poner(ComboBox(), 716, 32, 170)
    cmb_alm.DropDownStyle = ComboBoxStyle.DropDownList
    for a in catalogo["almacenes"]:
        cmb_alm.Items.Add(a["nombre"])
    if cmb_alm.Items.Count > 0:
        cmb_alm.SelectedIndex = 0
    lbl_cond = et("Condición de pago", 906, 12)
    cmb_cond = poner(ComboBox(), 906, 32, 160)
    cmb_cond.DropDownStyle = ComboBoxStyle.DropDownList
    conds_vis = []

    et("Fecha del documento", 16, 66)
    dt_fecha = poner(DateTimePicker(), 16, 86, 130)
    dt_fecha.Format = DateTimePickerFormat.Short
    lbl_entrega = et("Fecha de entrega", 166, 66)
    dt_entrega = poner(DateTimePicker(), 166, 86, 130)
    dt_entrega.Format = DateTimePickerFormat.Short
    et("Título (opcional)", 296, 66)
    txt_titulo = poner(TextBox(), 296, 86, 400)
    txt_titulo.MaxLength = 120
    et("Comentarios (opcional)", 716, 66)
    txt_coment = poner(TextBox(), 716, 86, 350)

    lbl_origen = et("Partir de un documento ya existente (solo se cargan las partidas que faltan por surtir)", 16, 122)
    clb_origen = poner(CheckedListBox(), 16, 142, 1050, 62)
    clb_origen.CheckOnClick = True
    clb_origen.BorderStyle = BorderStyle.FixedSingle
    origenes_vis = []

    et("Buscar producto (nombre o clave) y presionar Enter", 16, 214)
    txt_prod = poner(TextBox(), 16, 234, 520)
    lst_prod = ListBox()
    lst_prod.Visible = False
    lst_prod.Location = Point(16, 258)
    lst_prod.Size = Size(520, 190)
    frm.Controls.Add(lst_prod)

    grid = poner(DataGridView(), 16, 266, 1050, 380)
    grid.AllowUserToAddRows = False
    grid.AllowUserToDeleteRows = False
    grid.RowHeadersVisible = False
    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
    grid.BackgroundColor = Color.White
    grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill

    def col_texto(nombre, titulo, peso, solo_lectura):
        c = DataGridViewTextBoxColumn()
        c.Name = nombre
        c.HeaderText = titulo
        c.ReadOnly = solo_lectura
        c.FillWeight = peso
        grid.Columns.Add(c)
        return c

    col_texto("Clave", "Clave", 12, True)
    col_texto("Producto", "Producto", 36, True)
    col_texto("Unidad", "Unidad", 8, True)
    col_texto("Cant", "Cantidad", 9, False)
    col_texto("Precio", "Precio", 10, False)
    col_texto("Desc", "Desc. %", 7, False)
    c_imp = DataGridViewComboBoxColumn()
    c_imp.Name = "Imp"
    c_imp.HeaderText = "Impuesto"
    c_imp.FillWeight = 12
    for i in impuestos:
        c_imp.Items.Add(i["nombre"])
    grid.Columns.Add(c_imp)
    col_texto("Importe", "Importe", 10, True)
    for nombre in ("Cant", "Precio", "Desc", "Importe"):
        grid.Columns[nombre].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
    btn_quitar = poner(Button(), 16, 652, 120, 26)
    btn_quitar.Text = "Quitar partida"

    lbl_tot = poner(Label(), 560, 652, 506, 26)
    lbl_tot.TextAlign = ContentAlignment.MiddleRight
    lbl_tot.Font = Font("Segoe UI", 10.0, FontStyle.Bold)
    lbl_aviso = poner(Label(), 16, 684, 1050, 20)
    lbl_aviso.ForeColor = Color.FromArgb(100, 116, 139)
    lbl_aviso.Text = "El total es un estimado: Comercial lo recalcula al crear el documento (descuentos globales, redondeos)."
    btn_crear = poner(Button(), 826, 740, 150, 32)
    btn_crear.Text = "Crear documento"
    btn_crear.BackColor = Color.FromArgb(45, 111, 224)
    btn_crear.ForeColor = Color.White
    btn_crear.FlatStyle = FlatStyle.Flat
    btn_cancelar = poner(Button(), 986, 740, 80, 32)
    btn_cancelar.Text = "Cancelar"

    # ---------- lógica ----------
    def tipo_actual():
        return TIPOS[cmb_tipo.SelectedIndex]

    def precio_de(pid):
        for p in productos:
            if p["id"] == pid:
                return p["venta"] if tipo_actual()["precio"] == "venta" else p["costo"]
        return 0.0

    def nombre_de_imp(i):
        return nombre_imp.get(i) or (impuestos[0]["nombre"] if impuestos else "")

    def totales():
        sub = des = imp = 0.0
        for i, f in enumerate(filas):
            bruto = f["cant"] * f["precio"]
            d = bruto * f["desc"] / 100.0
            base = bruto - d
            sub += bruto
            des += d
            imp += base * perc_imp.get(f["imp"], 0.0)
            if i < grid.Rows.Count:
                grid.Rows[i].Cells["Importe"].Value = "{:,.2f}".format(base)
        lbl_tot.Text = "Subtotal {:,.2f}   Descuento {:,.2f}   Impuestos {:,.2f}   TOTAL {:,.2f}".format(sub, des, imp, sub - des + imp)

    def pintar_grid():
        estado["bloquea"] = True
        grid.Rows.Clear()
        for f in filas:
            grid.Rows.Add(f["clave"], f["nombre"] + ("  (origen)" if f["origen"] else ""), f["unidad"], str(f["cant"]), str(f["precio"]), str(f["desc"]), nombre_de_imp(f["imp"]), "")
        estado["bloquea"] = False
        totales()

    def pintar_origenes():
        t = tipo_actual()
        mod = t["origen"]
        os_ = [o for o in origenes if o["modulo"] == mod] if mod > 0 else []
        del origenes_vis[:]
        clb_origen.Items.Clear()
        for o in os_:
            n = len(o["partidasPor"].get(t["clave"], []))
            origenes_vis.append(o)
            clb_origen.Items.Add((o["folio"] or ("Documento " + str(o["id"]))) + " · " + o["entidadNombre"] + " · " + str(n) + " partida(s) pendiente(s)", o["id"] in estado["origenes"])
        lbl_origen.Visible = clb_origen.Visible = len(os_) > 0

    def cambio_tipo():
        t = tipo_actual()
        estado["tipo"] = t
        lbl_ent.Text = "Cliente" if t["lado"] == "C" else "Proveedor"
        lbl_cond.Visible = cmb_cond.Visible = t["condicion"]
        lbl_entrega.Visible = dt_entrega.Visible = t["entrega"]
        cmb_cond.Items.Clear()
        del conds_vis[:]
        for c in catalogo["condiciones"]:
            if (c["venta"] if t["lado"] == "C" else c["compra"]):
                conds_vis.append(c)
                cmb_cond.Items.Add(c["nombre"])
        if cmb_cond.Items.Count > 0:
            cmb_cond.SelectedIndex = 0
        del estado["origenes"][:]
        filas[:] = [f for f in filas if not f["origen"]]
        for f in filas:
            f["precio"] = precio_de(f["id"])
        estado["entidad"] = 0
        txt_ent.Text = ""
        pintar_origenes()
        pintar_grid()

    cmb_tipo.SelectedIndexChanged += seguro(cambio_tipo)

    # Cuadro de búsqueda con lista desplegable (entidad y producto)
    def buscar(txt, lst, fuente, texto, destino):
        q = txt.Text.lower().split()
        vis = []
        for x in fuente:
            h = texto(x).lower()
            if all(w in h for w in q):
                vis.append(x)
                if len(vis) >= 40:
                    break
        destino[:] = vis
        lst.Items.Clear()
        for x in vis:
            lst.Items.Add(texto(x))
        lst.Visible = lst.Items.Count > 0
        if lst.Visible:
            lst.BringToFront()
            lst.SelectedIndex = 0

    def fuente_entidad():
        return catalogo["clientes"] if tipo_actual()["lado"] == "C" else catalogo["proveedores"]

    def texto_entidad(x):
        return x["nombre"] + ("  [" + x["rfc"] + "]" if x["rfc"] else "")

    def texto_producto(x):
        return x["nombre"] + ("  [" + x["clave"] + "]" if x["clave"] else "")

    def elegir_entidad():
        i = lst_ent.SelectedIndex
        if i < 0 or i >= len(estado["vis_ent"]):
            return
        x = estado["vis_ent"][i]
        estado["entidad"] = x["id"]
        estado["bloquea"] = True
        txt_ent.Text = x["nombre"]
        estado["bloquea"] = False
        lst_ent.Visible = False

    def ent_cambio():
        if estado["bloquea"]:
            return
        estado["entidad"] = 0
        buscar(txt_ent, lst_ent, fuente_entidad(), texto_entidad, estado["vis_ent"])

    txt_ent.TextChanged += seguro(ent_cambio)

    def tecla_ent(sender, ev):
        if ev.KeyCode == Keys.Down and lst_ent.Visible:
            lst_ent.Focus()
            ev.Handled = True
        elif ev.KeyCode == Keys.Enter:
            seguro(elegir_entidad)()
            ev.SuppressKeyPress = True

    txt_ent.KeyDown += tecla_ent
    lst_ent.Click += seguro(elegir_entidad)
    lst_ent.KeyDown += lambda s, ev: (seguro(elegir_entidad)(), setattr(ev, "SuppressKeyPress", True)) if ev.KeyCode == Keys.Enter else None

    def agregar_producto():
        i = lst_prod.SelectedIndex
        if i < 0 or i >= len(estado["vis_prod"]):
            return
        p = estado["vis_prod"][i]
        ya = next((f for f in filas if f["id"] == p["id"] and not f["origen"]), None)
        if ya:
            ya["cant"] += 1
        else:
            filas.append({"id": p["id"], "clave": p["clave"], "nombre": p["nombre"], "unidad": p["unidad"], "cant": 1.0, "max": 0.0,
                          "precio": precio_de(p["id"]), "desc": 0.0, "imp": p["imp"], "origenItem": 0, "origen": 0})
        lst_prod.Visible = False
        estado["bloquea"] = True
        txt_prod.Text = ""
        estado["bloquea"] = False
        pintar_grid()

    def prod_cambio():
        if estado["bloquea"]:
            return
        buscar(txt_prod, lst_prod, productos, texto_producto, estado["vis_prod"])

    txt_prod.TextChanged += seguro(prod_cambio)

    def tecla_prod(sender, ev):
        if ev.KeyCode == Keys.Down and lst_prod.Visible:
            lst_prod.Focus()
            ev.Handled = True
        elif ev.KeyCode == Keys.Enter:
            seguro(agregar_producto)()
            ev.SuppressKeyPress = True

    txt_prod.KeyDown += tecla_prod
    lst_prod.Click += seguro(agregar_producto)
    lst_prod.KeyDown += lambda s, ev: (seguro(agregar_producto)(), setattr(ev, "SuppressKeyPress", True)) if ev.KeyCode == Keys.Enter else None

    def fin_edicion(sender, ev):
        if estado["bloquea"] or ev.RowIndex < 0 or ev.RowIndex >= len(filas):
            return
        try:
            f = filas[ev.RowIndex]
            col = grid.Columns[ev.ColumnIndex].Name
            v = grid.Rows[ev.RowIndex].Cells[col].Value
            if col == "Imp":
                if v is not None and str(v) in id_imp:
                    f["imp"] = id_imp[str(v)]
            else:
                try:
                    n = float(str(v).replace(",", ""))
                except Exception:
                    n = 0.0
                if n < 0:
                    n = 0.0
                if col == "Cant":
                    f["cant"] = f["max"] if f["max"] > 0 and n > f["max"] else n
                    grid.Rows[ev.RowIndex].Cells["Cant"].Value = str(f["cant"])
                elif col == "Precio":
                    f["precio"] = n
                elif col == "Desc":
                    f["desc"] = min(100.0, n)
                    grid.Rows[ev.RowIndex].Cells["Desc"].Value = str(f["desc"])
            totales()
        except Exception as ex:
            msg(str(ex))

    grid.CellEndEdit += fin_edicion

    def quitar():
        if grid.CurrentRow is not None and grid.CurrentRow.Index < len(filas):
            del filas[grid.CurrentRow.Index]
            pintar_grid()

    btn_quitar.Click += seguro(quitar)

    def cambio_origen(sender, ev):
        try:
            marcados = []
            for i in range(clb_origen.Items.Count):
                chk = (ev.NewValue == CheckState.Checked) if i == ev.Index else clb_origen.GetItemChecked(i)
                if chk:
                    marcados.append(origenes_vis[i])
            estado["origenes"][:] = [o["id"] for o in marcados]
            t = tipo_actual()
            if len(set(o["entidad"] for o in marcados)) > 1:
                raise Exception("Los documentos de origen son de entidades distintas: quita alguno.")
            filas[:] = [f for f in filas if not f["origen"]]
            for o in marcados:
                for p in o["partidasPor"].get(t["clave"], []):
                    filas.append({"id": p["id"], "clave": p["clave"], "nombre": p["nombre"], "unidad": p["unidad"], "cant": p["cant"], "max": p["cant"],
                                  "precio": p["precio"], "desc": p["desc"], "imp": p["imp"], "origenItem": p["origenItem"], "origen": o["id"]})
            if marcados:
                estado["entidad"] = marcados[0]["entidad"]
                estado["bloquea"] = True
                txt_ent.Text = marcados[0]["entidadNombre"]
                estado["bloquea"] = False
                lst_ent.Visible = False
                for i, a in enumerate(catalogo["almacenes"]):
                    if a["id"] == marcados[0]["almacen"]:
                        cmb_alm.SelectedIndex = i
            pintar_grid()
        except Exception as ex:
            msg(str(ex))

    clb_origen.ItemCheck += cambio_origen

    def crear():
        t = tipo_actual()
        if not estado["entidad"]:
            raise Exception("Elige el " + ("cliente" if t["lado"] == "C" else "proveedor") + " de la lista (escribe y selecciona).")
        if not filas:
            raise Exception("Agrega al menos una partida.")
        if any(f["cant"] <= 0 for f in filas):
            raise Exception("Todas las partidas deben tener cantidad mayor a cero.")
        spec = {
            "tipo": t["clave"],
            "almacen": catalogo["almacenes"][cmb_alm.SelectedIndex]["id"] if cmb_alm.SelectedIndex >= 0 else 0,
            "entidad": estado["entidad"],
            "condicion": conds_vis[cmb_cond.SelectedIndex]["id"] if t["condicion"] and cmb_cond.SelectedIndex >= 0 else 0,
            "fecha": dt_fecha.Value.ToString("yyyy-MM-dd"),
            "entrega": dt_entrega.Value.ToString("yyyy-MM-dd") if t["entrega"] else "",
            "titulo": txt_titulo.Text, "comentarios": txt_coment.Text, "origenes": list(estado["origenes"]),
            "partidas": [{"id": f["id"], "nombre": f["nombre"], "cant": f["cant"], "precio": f["precio"], "desc": f["desc"], "imp": f["imp"], "origenItem": f["origenItem"]} for f in filas],
        }
        btn_crear.Enabled = False
        frm.Cursor = Cursors.WaitCursor
        try:
            doc = crear_documento(spec)
            try:
                ctx.erp.RefreshGrid()
            except Exception:
                pass
            try:
                ctx.erp.AbrirDocumento(doc, t["modulo"])
            except Exception:
                pass
            estado["resultado"] = "OK " + t["nombre"] + " id=" + str(doc)
            frm.Close()
        finally:
            btn_crear.Enabled = True
            frm.Cursor = Cursors.Default

    btn_crear.Click += seguro(crear)
    btn_cancelar.Click += lambda s, e: frm.Close()

    def tecla_forma(sender, ev):
        if ev.KeyCode == Keys.Escape and not lst_ent.Visible and not lst_prod.Visible:
            frm.Close()
            ev.Handled = True

    frm.KeyDown += tecla_forma

    # ---------- Arranque ----------
    cambio_tipo()
    frm.ShowDialog()
    result = estado.get("resultado", "CANCELADO")


if not _modo_prueba:
    principal()
