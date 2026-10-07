# lang: python
# timeout: 1800
# AppKey recomendado: CREAR_VENTA_PYTHON_WINFORMS
# Plantilla: Crear documento de venta (Python · ventana Windows Forms)
# Categoria: Ventas
# Documentacion: CREAR_VENTA.html
# Crea un documento de Comercial desde una ventana de Windows Forms (pythonnet): factura de cliente, pedido o remisión (ventas). Plantilla separada a propósito: solo VENTAS (clientes), para que quien la use no vea el otro lado.

import pythonnet
pythonnet.load("netfx")

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System import DateTime, Convert
from System.Drawing import Point, Size, Color, Font, FontStyle, ContentAlignment, Pen, SolidBrush, StringFormat, StringTrimming, StringFormatFlags, RectangleF
from System.Windows.Forms import (
    Form, FormStartPosition, Label, TextBox, ComboBox, ComboBoxStyle, Button, FlatStyle, DataGridView, Panel, FlowLayoutPanel, NumericUpDown, HorizontalAlignment, DockStyle, Padding,
    AutoSizeMode, DrawMode, DrawItemState, DataGridViewTextBoxColumn, DataGridViewComboBoxColumn, DataGridViewContentAlignment, DataGridViewAutoSizeColumnsMode,
    DataGridViewSelectionMode, DataGridViewCellBorderStyle, DataGridViewHeaderBorderStyle, DataGridViewDataErrorContexts, DateTimePicker, DateTimePickerFormat,
    ListBox, CheckedListBox, CheckState, BorderStyle, Cursors, Keys, MessageBox, MessageBoxButtons, MessageBoxIcon,
)
from System.Windows.Forms import Timer as FormsTimer

System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA)

import json
import datetime
import os
import math

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

empresa = I(ctx.erp.OwnedBusinessEntityId())

def L(v):
    return I(v)

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

def T(clave, nombre, modulo, lado, precio, perfil, condicion, entrega, vinculo, origen):
    return {"clave": clave, "nombre": nombre, "modulo": modulo, "lado": lado, "precio": precio, "perfil": perfil,
            "condicion": condicion, "entrega": entrega, "vinculo": vinculo, "origen": origen}

TIPOS = [
    T("factura_cliente", "Factura de cliente",  21,  "C", "venta",  "DepotIDFrom=0, StatusDeliveryID=0",                            True,  False, "",         0),
    T("pedido",          "Pedido de cliente",   967, "C", "venta",  "DepotIDFrom=0, StatusDeliveryID=3, StatusPaidID=0",            True,  True,  "",         0),
    T("remision",        "Remisión (entrega)",  157, "C", "venta",  "DepotIDFrom=0, PaymentTermID=0, StatusDeliveryID=0",           False, True,  "cabecera", 967),
]
TIPO_POR = {t["clave"]: t for t in TIPOS}
CLAVES = [t["clave"] for t in TIPOS]

def catalogos(claves):
    cat = {}
    cat["almacenes"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query(
        "SELECT DepotID AS id, DepotName AS nombre FROM orgDepot WHERE DeletedOn IS NULL AND OwnedBusinessEntityID = " + str(empresa) + " ORDER BY DepotName")]

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

    cat["productos"] = [{"id": I(r["id"]), "clave": S(r["clave"]), "nombre": S(r["nombre"]), "unidad": S(r["unidad"]), "imp": I(r["imp"]),
                         "venta": D(r["venta"]), "costo": D(r["costo"]), "barras": S(r["barras"]), "lote": I(r["lote"]) == 1, "serie": I(r["serie"]) == 1, "servicio": I(r["servicio"]) == 1} for r in ctx.query(
        "SELECT TOP 30000 ProductID AS id, ISNULL(ProductKey,'') AS clave, ProductName AS nombre, ISNULL(Unit,'') AS unidad, ISNULL(TaxTypeID,0) AS imp, "
        "ISNULL(PriceList,0) AS venta, ISNULL(CostPrice,0) AS costo, ISNULL(BarCode,'') AS barras, ISNULL(UseLot,0) AS lote, ISNULL(UseSerialNumber,0) AS serie, ISNULL(ProductIsService,0) AS servicio "
        "FROM orgProduct WHERE DeletedOn IS NULL ORDER BY ProductName")]

    exist = {}
    try:
        for r in ctx.query("SELECT ProductID, DepotID, SUM(Quantity) AS Q FROM orgProductKardex WHERE ISNULL(Cancelled,0) = 0 GROUP BY ProductID, DepotID HAVING ABS(SUM(Quantity)) > 0.00001"):
            exist.setdefault(S(r["ProductID"]), {})[S(r["DepotID"])] = round(D(r["Q"]), 4)
    except Exception:
        pass
    cat["existencias"] = exist

    folios = {}
    for t in TIPOS:
        if t["clave"] in claves:
            folios[t["clave"]] = I(ctx.scalar("SELECT ISNULL((SELECT TOP 1 ISNULL(TRY_CONVERT(int, Folio),0) + 1 FROM docDocument WHERE ModuleID = " + str(t["modulo"]) + " AND OwnedBusinessEntityID = " + str(empresa) + " AND DeletedOn IS NULL ORDER BY DocumentID DESC), 1)"))
    cat["folios"] = folios
    return cat

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

def pendientes_de(entidad, clave_tipo):
    if clave_tipo not in TIPO_POR or entidad <= 0:
        return []
    m_origen = TIPO_POR[clave_tipo]["origen"]
    if not m_origen:
        return []
    ids = [I(r["DocumentID"]) for r in ctx.query("SELECT TOP 40 DocumentID FROM docDocument WHERE ModuleID = " + str(m_origen) + " AND BusinessEntityID = " + str(entidad) + " AND OwnedBusinessEntityID = " + str(empresa) +
                                                 " AND DeletedOn IS NULL AND CancelledOn IS NULL ORDER BY DateDocument DESC, DocumentID DESC")]
    return [o for o in origenes_de(ids) if len(o["partidasPor"].get(clave_tipo, [])) > 0]

def ultimos_de(entidad):
    return [{"id": I(r["DocumentID"]), "modulo": I(r["ModuleID"]), "tipo": S(r["Modulo"]), "folio": (S(r["FolioPrefix"]) + S(r["Folio"])).strip(), "fecha": fecha_txt(r["DateDocument"]),
             "total": D(r["Total"]), "saldo": D(r["Saldo"])} for r in ctx.query(
        "SELECT TOP 6 d.DocumentID, d.ModuleID, ISNULL(m.ModuleName,'') AS Modulo, d.FolioPrefix, d.Folio, d.DateDocument, ISNULL(d.Total,0) AS Total, ISNULL(d.Balance,0) AS Saldo FROM docDocument d "
        "LEFT JOIN engModule m ON m.ModuleID = d.ModuleID WHERE d.BusinessEntityID = " + str(entidad) + " AND d.OwnedBusinessEntityID = " + str(empresa) + " AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL ORDER BY d.DateDocument DESC, d.DocumentID DESC")]

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

    lado = t["lado"]
    if I(ctx.scalar("SELECT COUNT(*) AS n FROM orgDepot WHERE DepotID = " + str(almacen) + " AND DeletedOn IS NULL AND OwnedBusinessEntityID = " + str(empresa))) == 0:
        raise Exception("El almacén elegido no existe en esta empresa.")
    if I(ctx.scalar("SELECT COUNT(*) AS n FROM " + ("orgCustomer" if lado == "C" else "orgSupplier") + " x JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID WHERE x.BusinessEntityID = " + str(entidad) + " AND x.DeletedOn IS NULL AND be.DeletedOn IS NULL")) == 0:
        raise Exception("El " + ("cliente" if lado == "C" else "proveedor") + " elegido no existe o está eliminado.")
    ids_prod = sorted(set(I(p.get("id")) for p in partidas))
    if I(ctx.scalar("SELECT COUNT(*) AS n FROM orgProduct WHERE DeletedOn IS NULL AND ProductID IN (" + ",".join(str(i) for i in ids_prod) + ")")) != len(ids_prod):
        raise Exception("Hay partidas con un producto que ya no existe o fue eliminado.")
    f_txt = S(spec.get("fecha"))
    try:
        f_doc = datetime.datetime.strptime(f_txt[:10], "%Y-%m-%d") if len(f_txt) >= 10 else datetime.datetime.now()
    except Exception:
        raise Exception("La fecha del documento no es válida.")
    e_txt = S(spec.get("entrega"))
    if t["entrega"] and len(e_txt) >= 10:
        try:
            f_ent = datetime.datetime.strptime(e_txt[:10], "%Y-%m-%d")
        except Exception:
            raise Exception("La fecha de entrega no es válida.")
        if f_ent.date() < f_doc.date():
            raise Exception("La fecha de entrega no puede ser anterior a la fecha del documento.")
    if t["condicion"] and I(ctx.scalar("SELECT COUNT(*) AS n FROM engPaymentTerm WHERE DeletedOn IS NULL AND PaymentTermID = " + str(I(spec.get("condicion"))) + " AND " + ("Sales" if lado == "C" else "Buys") + " = 1")) == 0:
        raise Exception("Elige una condición de pago válida para " + ("ventas" if lado == "C" else "compras") + ".")
    moneda_sel = I(spec.get("moneda"))
    tc_sel = D(spec.get("tc"))
    simbolo_mon = "MXN"
    if moneda_sel > 0:
        simbolo_mon = S(ctx.scalar("SELECT IntlSymbol FROM vwLBSCurrencyList WHERE CurrencyID = " + str(moneda_sel)))
        if simbolo_mon == "":
            raise Exception("La moneda elegida no existe.")
        if simbolo_mon != "MXN" and not tc_sel > 0:
            raise Exception("Captura el tipo de cambio de " + simbolo_mon + " (mayor a cero).")
    origenes = [I(x) for x in (spec.get("origenes") or []) if I(x) > 0]
    if vinculo == "":
        origenes = []

    doc = ctx.erp.NuevoDocumento(modulo, almacen, entidad)
    if not doc or doc <= 0 or _error_erp():
        raise Exception("No se pudo crear el documento: " + S(_error_erp()))

    try:

        centro = I(spec.get("centro"))
        sets = [t["perfil"], "CampaignID=NULL", "CostCenterID=" + (str(centro) if centro > 0 else "NULL"), "ProjectID=NULL"]
        moneda = I(spec.get("moneda"))
        tc = D(spec.get("tc"))
        if moneda > 0:
            sets.append("CurrencyID=" + str(moneda) + ", Rate=" + Num(1 if simbolo_mon == "MXN" else tc))
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
            sets.append("SourceDocumentID=" + str(origenes[0]))
        ctx.execute("UPDATE docDocument SET " + ", ".join(sets) + " WHERE DocumentID=" + str(doc))

        for p in partidas:
            pid = I(p.get("id"))
            cant = D(p.get("cant"))
            precio = D(p.get("precio"))
            imp = I(p.get("imp"))
            origen_item = I(p.get("origenItem"))

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

        if I(ctx.scalar("SELECT ISNULL(MAX(TRY_CONVERT(int, Value)),0) FROM engModuleParameter WHERE ParameterKey='StockAffectation' AND ModuleID=" + str(modulo))) != 0:
            ctx.erp.AffectStockNEW(doc)
            if _error_erp():
                raise Exception("AffectStockNEW: " + S(_error_erp()))
        ctx.erp.Save(doc)
        if _error_erp():
            raise Exception("Save: " + S(_error_erp()))
        if clave == "factura_cliente":
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
            armar_agenda(doc, I(spec.get("condicion")), base)
            try:
                ctx.erp.UpdateDocumentPaidInfo(doc)
            except Exception:
                pass
        if vinculo != "":
            try:
                ctx.erp.UpdateStatusDelivery(doc)
            except Exception:
                pass
        return doc
    except Exception as ex:

        raise Exception(S(ex) + "\n\nQuedó un documento incompleto (id " + str(doc) + "): elimínalo o cancélalo desde Comercial.")

try:
    seleccion = [int(x) for x in (ctx.get_selected_ids() or [])]
except Exception:
    seleccion = []

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

def msg(texto, titulo="Crear documento"):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning)

def seguro(fn):
    def envuelto(sender=None, args=None):
        try:
            fn()
        except Exception as ex:
            E["guardando"] = False
            msg(str(ex))
    return envuelto

def rgb(r, g, b):
    return Color.FromArgb(r, g, b)

C_BG = rgb(241, 245, 249)
C_LINE = rgb(203, 213, 225)
C_TXT = rgb(30, 41, 59)
C_MUTED = rgb(100, 116, 139)
C_HEAD = rgb(248, 250, 252)
C_SEL = rgb(238, 242, 255)
C_RIB = rgb(51, 65, 85)
C_RIB_TX = rgb(226, 232, 240)
C_RIB_MU = rgb(148, 163, 184)
C_VENTA = rgb(37, 99, 235)
C_COMPRA = rgb(15, 118, 110)
C_ROJO = rgb(200, 40, 40)
C_AMBAR = rgb(180, 83, 9)
C_VERDE = rgb(22, 128, 59)
F_BASE = Font("Segoe UI", 9.0)
F_B = Font("Segoe UI", 9.0, FontStyle.Bold)
F_H2 = Font("Segoe UI", 9.5, FontStyle.Bold)
F_SM = Font("Segoe UI", 8.5)
F_ICON = Font("Segoe UI Emoji", 20.0)
F_TOT = Font("Segoe UI Semibold", 18.0)
F_VAL = Font("Segoe UI Semibold", 11.0)
F_ITAL = Font("Segoe UI", 9.0, FontStyle.Italic)

E = {"ent": None, "prod": None, "guardando": False, "suspender": False, "metodo_manual": False, "ver_g3": True, "ver_g4": False, "tipo": 0, "resultado": "CANCELADO", "tc_prev": 1.0, "en_cambio_mon": False}

UNI = ["", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE"]
DEC = ["", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"]
VEI = ["VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"]
CEN = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"]

def centenas(n):
    if n == 0:
        return ""
    if n == 100:
        return "CIEN"
    r = ""
    c, d = n // 100, n % 100
    if c > 0:
        r += CEN[c] + " "
    if d > 0:
        if d <= 20:
            r += UNI[d]
        else:
            a, u = d // 10, d % 10
            if a == 2 and u > 0:
                r += VEI[u - 1]
            else:
                r += DEC[a]
                if u > 0:
                    r += " Y " + UNI[u]
    return r.strip()

def en_letras(n):
    if n == 0:
        return "CERO"
    r = ""
    mill = n // 1000000
    n %= 1000000
    mil = n // 1000
    n %= 1000
    if mill > 0:
        r += "UN MILLÓN " if mill == 1 else centenas(mill) + " MILLONES "
    if mil > 0:
        r += "MIL " if mil == 1 else centenas(mil) + " MIL "
    if n > 0:
        r += centenas(n)
    r = r.strip()
    if r.endswith("UNO"):
        r = r[:-3] + "UN"
    return r

def a_letras(v, moneda, mn):
    v = max(0.0, v)
    e = int(v)
    c = int(round((v - e) * 100))
    if c == 100:
        e += 1
        c = 0
    return en_letras(e) + " " + moneda + " " + ("%02d" % c) + "/100" + (" M.N." if mn else "")

def principal():
    global result
    catalogo = catalogos(CLAVES)
    origenes = origenes_de(seleccion)
    impuestos = catalogo["impuestos"]
    perc_imp = {i["id"]: i["perc"] for i in impuestos}
    nombre_imp = {i["id"]: i["nombre"] for i in impuestos}
    id_imp = {i["nombre"]: i["id"] for i in impuestos}
    productos = catalogo["productos"]
    monedas = catalogo["monedas"]
    existencias = catalogo["existencias"]
    folios = catalogo["folios"]
    filas = []
    origen_sel = []
    pendientes = []
    pastillas = []
    vis = {"ent": [], "prod": [], "origen": []}
    quien_soy = ""
    nombre_empresa = ""
    try:
        quien_soy = S(ctx.scalar("SELECT TOP 1 ISNULL(UserName,'') FROM engUser WHERE UserID = " + str(int(ctx.user_id))))
    except Exception:
        pass
    try:
        nombre_empresa = S(ctx.scalar("SELECT TOP 1 ISNULL(CommercialName, OfficialName) FROM orgBusinessEntity WHERE BusinessEntityID = " + str(empresa)))
    except Exception:
        pass

    frm = Form()
    frm.Text = "Crear documento · BrosLMV"
    frm.ClientSize = Size(1200, 900)
    frm.MinimumSize = Size(1160, 820)
    frm.StartPosition = FormStartPosition.CenterScreen
    frm.BackColor = C_BG
    frm.Font = F_BASE
    frm.KeyPreview = True

    def al_frente(s, e):
        frm.TopMost = True
        frm.Activate()
        frm.BringToFront()
        frm.TopMost = False
    frm.Shown += al_frente

    def tipo_actual():
        return TIPOS[E["tipo"]]

    def es_venta():
        return tipo_actual()["lado"] == "C"

    def con_cfdi():
        return tipo_actual()["clave"] == "factura_cliente"

    def acento():
        return C_VENTA if es_venta() else C_COMPRA

    def grupo(titulo):
        p = Panel()
        p.BackColor = Color.White
        p.Size = Size(600, 120)

        def pintar(s, e):
            g = e.Graphics
            pen = Pen(C_LINE)
            g.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1)
            g.DrawLine(pen, 0, 28, p.Width, 28)
            pen.Dispose()
            b = SolidBrush(C_HEAD)
            g.FillRectangle(b, 1, 1, p.Width - 2, 27)
            b.Dispose()
            b = SolidBrush(acento())
            g.FillRectangle(b, 1, 1, 4, 27)
            b.Dispose()
            b = SolidBrush(C_TXT)
            g.DrawString(titulo(), F_H2, b, 14.0, 5.0)
            b.Dispose()
        p.Paint += pintar
        return p

    def et(padre, texto, x, y):
        l = Label()
        l.Text = texto
        l.Location = Point(x, y)
        l.AutoSize = True
        l.ForeColor = C_MUTED
        l.Font = F_SM
        l.BackColor = Color.Transparent
        padre.Controls.Add(l)
        return l

    def cuadro(padre, etiqueta, x, y, w, solo_lectura=False):

        if etiqueta:
            et(padre, etiqueta, x, y)
        marco = Panel()
        marco.Location = Point(x, y + 18)
        marco.Size = Size(w, 26)
        marco.BackColor = C_HEAD if solo_lectura else Color.White

        def pintar(s, e):
            pen = Pen(C_LINE)
            e.Graphics.DrawRectangle(pen, 0, 0, marco.Width - 1, marco.Height - 1)
            pen.Dispose()
        marco.Paint += pintar
        t = TextBox()
        t.BorderStyle = getattr(BorderStyle, "None")
        t.ReadOnly = solo_lectura
        t.BackColor = marco.BackColor
        t.ForeColor = C_MUTED if solo_lectura else C_TXT
        t.Width = w - 14
        t.Location = Point(7, max(0, (26 - t.Height) // 2))
        marco.Controls.Add(t)
        padre.Controls.Add(marco)
        marco.Click += lambda s, e: t.Focus()
        return t, marco

    def lista(padre, etiqueta, x, y, w, ancho_menu=0):
        et(padre, etiqueta, x, y)
        c = ComboBox()
        c.Width = w
        c.DropDownStyle = ComboBoxStyle.DropDownList
        c.FlatStyle = FlatStyle.Flat
        c.Location = Point(x, y + 18 + max(0, (26 - c.Height) // 2))
        if ancho_menu > 0:
            c.DropDownWidth = ancho_menu
        padre.Controls.Add(c)
        return c

    def numero(padre, etiqueta, x, y, w, dec, maximo):
        et(padre, etiqueta, x, y)
        n = NumericUpDown()
        n.Width = w
        n.DecimalPlaces = dec
        n.Maximum = Convert.ToDecimal(maximo)
        n.Minimum = Convert.ToDecimal(0)
        n.ThousandsSeparator = True
        n.TextAlign = HorizontalAlignment.Right
        n.Location = Point(x, y + 18 + max(0, (26 - n.Height) // 2))
        padre.Controls.Add(n)
        return n

    def boton_plano(texto, x, y, w, h, fondo=None, color=None, negrita=False):
        b = Button()
        b.Text = texto
        b.Location = Point(x, y)
        b.Size = Size(w, h)
        b.FlatStyle = FlatStyle.Flat
        b.BackColor = fondo if fondo is not None else Color.White
        b.ForeColor = color if color is not None else C_TXT
        b.Cursor = Cursors.Hand
        if negrita:
            b.Font = F_B
        b.FlatAppearance.BorderColor = C_LINE
        return b

    def poner_por_clave(cmb, items, clave):
        for i, it in enumerate(items):
            if it["clave"].lower() == clave.lower():
                cmb.SelectedIndex = i
                return

    def poner_por_id(cmb, items, id_):
        for i, it in enumerate(items):
            if it["id"] == id_:
                cmb.SelectedIndex = i
                return
        if len(items) > 0:
            cmb.SelectedIndex = 0

    def seleccionado(cmb, items):
        i = cmb.SelectedIndex
        return items[i] if 0 <= i < len(items) else None

    ribbon = Panel()
    ribbon.BackColor = C_RIB
    ribbon.Size = Size(1176, 100)
    lbl_rib_titulo = Label()
    lbl_rib_titulo.Text = "Nuevo documento"
    lbl_rib_titulo.Font = F_H2
    lbl_rib_titulo.ForeColor = C_RIB_TX
    lbl_rib_titulo.BackColor = C_RIB
    lbl_rib_titulo.Location = Point(12, 5)
    lbl_rib_titulo.AutoSize = True
    ribbon.Controls.Add(lbl_rib_titulo)
    bx = [12]

    def boton_cinta(icono, texto, tecla, color_icono, al_clic):
        p = Panel()
        p.Location = Point(bx[0], 26)
        p.Size = Size(84, 68)
        p.BackColor = C_RIB
        p.Cursor = Cursors.Hand
        bx[0] += 88
        li = Label()
        li.Text = icono
        li.Font = F_ICON
        li.ForeColor = color_icono if color_icono is not None else C_RIB_TX
        li.BackColor = C_RIB
        li.AutoSize = False
        li.Size = Size(84, 34)
        li.TextAlign = ContentAlignment.MiddleCenter
        li.Location = Point(0, 0)
        lt = Label()
        lt.Text = texto
        lt.Font = F_SM
        lt.ForeColor = C_RIB_TX
        lt.BackColor = C_RIB
        lt.AutoSize = False
        lt.Size = Size(84, 18)
        lt.TextAlign = ContentAlignment.MiddleCenter
        lt.Location = Point(0, 35)
        lk = Label()
        lk.Text = tecla
        lk.Font = F_SM
        lk.ForeColor = C_RIB_MU
        lk.BackColor = C_RIB
        lk.AutoSize = False
        lk.Size = Size(84, 15)
        lk.TextAlign = ContentAlignment.MiddleCenter
        lk.Location = Point(0, 52)
        p.Controls.Add(li)
        p.Controls.Add(lt)
        p.Controls.Add(lk)

        def hover(encima):
            c = rgb(71, 85, 105) if encima else C_RIB
            p.BackColor = c
            li.BackColor = c
            lt.BackColor = c
            lk.BackColor = c
        for c in (p, li, lt, lk):
            c.MouseEnter += lambda s, e: hover(True)
            c.MouseLeave += lambda s, e: hover(False)
            c.Click += seguro(al_clic)
        ribbon.Controls.Add(p)
        return p

    btn_guardar = boton_cinta("💾", "Guardar y abrir", "F5", rgb(96, 165, 250), lambda: crear(False))
    boton_cinta("➕", "Guardar y nuevo", "F6", None, lambda: crear(True))
    boton_cinta("❌", "Cancelar", "Esc", rgb(248, 113, 113), lambda: frm.Close())
    sep = Panel()
    sep.Location = Point(bx[0], 30)
    sep.Size = Size(1, 60)
    sep.BackColor = C_RIB_MU
    ribbon.Controls.Add(sep)
    bx[0] += 12
    boton_cinta("🧹", "Limpiar", "", None, lambda: limpiar())

    info = Panel()
    info.Size = Size(430, 92)
    info.BackColor = C_RIB
    ribbon.Controls.Add(info)

    def pintar_info(s, e):
        pen = Pen(C_RIB_MU)
        e.Graphics.DrawRectangle(pen, 0, 7, info.Width - 1, info.Height - 11)
        pen.Dispose()
    info.Paint += pintar_info
    t_info = Label()
    t_info.Text = "Información del documento"
    t_info.Font = F_SM
    t_info.ForeColor = C_RIB_MU
    t_info.BackColor = C_RIB
    t_info.Location = Point(10, 0)
    t_info.AutoSize = True
    info.Controls.Add(t_info)

    def et_r(texto, x, y):
        l = Label()
        l.Text = texto
        l.Font = F_SM
        l.ForeColor = C_RIB_TX
        l.BackColor = C_RIB
        l.Location = Point(x, y)
        l.AutoSize = True
        info.Controls.Add(l)
        return l

    def caja_info(x, y, w):
        marco = Panel()
        marco.Location = Point(x, y)
        marco.Size = Size(w, 26)
        marco.BackColor = Color.White

        def pintar(s, e):
            pen = Pen(C_RIB_MU)
            e.Graphics.DrawRectangle(pen, 0, 0, marco.Width - 1, marco.Height - 1)
            pen.Dispose()
        marco.Paint += pintar
        t = TextBox()
        t.BorderStyle = getattr(BorderStyle, "None")
        t.ReadOnly = True
        t.BackColor = Color.White
        t.ForeColor = C_TXT
        t.Width = w - 12
        t.Location = Point(6, max(0, (26 - t.Height) // 2))
        marco.Controls.Add(t)
        info.Controls.Add(marco)
        return t

    et_r("Fecha", 14, 20)
    dt_fecha = DateTimePicker()
    dt_fecha.Format = DateTimePickerFormat.Short
    dt_fecha.Width = 118
    dt_fecha.Location = Point(14, 38 + max(0, (26 - dt_fecha.Height) // 2))
    info.Controls.Add(dt_fecha)
    et_r("Serie", 146, 20)
    txt_serie = caja_info(146, 38, 64)
    et_r("Folio", 222, 20)
    txt_folio = caja_info(222, 38, 86)
    lbl_entrega_et = et_r("Entrega esperada", 320, 20)
    dt_entrega = DateTimePicker()
    dt_entrega.Format = DateTimePickerFormat.Short
    dt_entrega.Width = 100
    dt_entrega.Value = DateTime.Today.AddDays(7)
    dt_entrega.Location = Point(320, 38 + max(0, (26 - dt_entrega.Height) // 2))
    info.Controls.Add(dt_entrega)
    nota = et_r("(el folio definitivo lo asigna Comercial al guardar)", 14, 70)
    nota.ForeColor = C_RIB_MU

    p_tipos = Panel()
    p_tipos.BackColor = Color.White
    p_tipos.Size = Size(1176, 40)

    def pintar_tipos(s, e):
        pen = Pen(C_LINE)
        e.Graphics.DrawRectangle(pen, 0, 0, p_tipos.Width - 1, p_tipos.Height - 1)
        pen.Dispose()
    p_tipos.Paint += pintar_tipos
    fl_tipos = FlowLayoutPanel()
    fl_tipos.Dock = DockStyle.Fill
    fl_tipos.WrapContents = False
    fl_tipos.AutoScroll = False
    fl_tipos.BackColor = Color.White
    fl_tipos.Padding = Padding(8, 5, 8, 0)
    p_tipos.Controls.Add(fl_tipos)
    btns_tipo = []
    grupo_actual = ""
    for i, t in enumerate(TIPOS):
        g = "VENTAS" if t["lado"] == "C" else "COMPRAS"
        if g != grupo_actual:
            grupo_actual = g
            lg = Label()
            lg.Text = g
            lg.Font = Font("Segoe UI Semibold", 8.0)
            lg.ForeColor = C_MUTED
            lg.AutoSize = False
            lg.Size = Size(len(g) * 8 + 18, 28)
            lg.TextAlign = ContentAlignment.MiddleLeft
            lg.Margin = Padding(6, 0, 2, 0)
            fl_tipos.Controls.Add(lg)
        b = Button()
        b.Text = t["nombre"]
        b.FlatStyle = FlatStyle.Flat
        b.Height = 28
        b.AutoSize = True
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink
        b.Padding = Padding(10, 0, 10, 0)
        b.Margin = Padding(3, 0, 3, 0)
        b.BackColor = Color.White
        b.ForeColor = C_TXT
        b.Cursor = Cursors.Hand
        b.Font = F_BASE
        b.FlatAppearance.BorderColor = C_LINE
        b.Click += seguro((lambda k: (lambda: cambiar_tipo(k)))(i))
        btns_tipo.append(b)
        fl_tipos.Controls.Add(b)

    g1 = grupo(lambda: str(numero_grupo(g1)) + ". " + ("Cliente" if es_venta() else "Proveedor"))
    txt_ent, marco_ent = cuadro(g1, "Buscar por nombre o RFC  (F2)", 14, 34, 320)
    txt_rfc, _m = cuadro(g1, "RFC", 346, 34, 130, True)
    txt_rfc.TabStop = False
    cmb_cond = lista(g1, "Condición de pago", 488, 34, 118)
    conds_vis = []
    p_chips = Panel()
    p_chips.Location = Point(14, 88)
    p_chips.Size = Size(500, 34)
    p_chips.BackColor = Color.White
    g1.Controls.Add(p_chips)

    def pintar_chips(s, e):
        x = 0
        g = e.Graphics
        for titulo, valor, color in pastillas:
            sz1 = g.MeasureString(titulo, F_SM)
            sz2 = g.MeasureString(valor, F_B)
            w = int(max(sz1.Width, sz2.Width)) + 18
            b = SolidBrush(C_HEAD)
            g.FillRectangle(b, x, 0, w, 32)
            b.Dispose()
            pen = Pen(C_LINE)
            g.DrawRectangle(pen, x, 0, w - 1, 31)
            pen.Dispose()
            b = SolidBrush(C_MUTED)
            g.DrawString(titulo, F_SM, b, float(x + 9), 1.0)
            b.Dispose()
            b = SolidBrush(color)
            g.DrawString(valor, F_B, b, float(x + 9), 15.0)
            b.Dispose()
            x += w + 6
    p_chips.Paint += pintar_chips
    btn_hist = boton_plano("Historial", 526, 92, 80, 28)
    g1.Controls.Add(btn_hist)

    g2 = grupo(lambda: str(numero_grupo(g2)) + ". Almacén, centro de costo y moneda")
    cmb_alm = lista(g2, "Almacén", 14, 34, 230)
    cmb_cc = lista(g2, "Centro de costo", 256, 34, 210)
    cmb_mon = lista(g2, "Moneda", 14, 84, 230)
    nud_tc = numero(g2, "Tipo de cambio", 256, 84, 120, 4, 9999999)
    alm_items = [{"id": a["id"], "nombre": a["nombre"]} for a in catalogo["almacenes"]]
    for a in alm_items:
        cmb_alm.Items.Add(a["nombre"])
    if cmb_alm.Items.Count > 0:
        cmb_alm.SelectedIndex = 0
    cc_items = [{"id": 0, "nombre": "(ninguno)"}] + [{"id": c["id"], "nombre": c["nombre"]} for c in catalogo["centros"]]
    for c in cc_items:
        cmb_cc.Items.Add(c["nombre"])
    cmb_cc.SelectedIndex = 0
    mon_items = [{"id": m["id"], "nombre": m["simbolo"] + "  ·  " + m["nombre"], "simbolo": m["simbolo"], "tc": m["tc"], "mn": m["nombre"]} for m in monedas]
    for m in mon_items:
        cmb_mon.Items.Add(m["nombre"])
    poner_por_id(cmb_mon, mon_items, 3)

    g3 = grupo(lambda: str(numero_grupo(g3)) + ". Datos fiscales del CFDI")
    cmb_uso = lista(g3, "Uso del CFDI", 14, 34, 410, 640)
    cmb_forma = lista(g3, "Forma de pago", 436, 34, 330, 520)
    cmb_metodo = lista(g3, "Método de pago", 778, 34, 290, 420)

    def llenar_sat(cmb, grupo_cat):
        items = [{"clave": x["clave"], "nombre": x["clave"] + "  ·  " + x["nombre"]} for x in catalogo[grupo_cat]]
        for it in items:
            cmb.Items.Add(it["nombre"])
        return items
    uso_items = llenar_sat(cmb_uso, "usos")
    forma_items = llenar_sat(cmb_forma, "formas")
    metodo_items = llenar_sat(cmb_metodo, "metodos")
    poner_por_clave(cmb_uso, uso_items, "G03")
    poner_por_clave(cmb_metodo, metodo_items, "PUE")
    poner_por_clave(cmb_forma, forma_items, "01")

    def clave_de(cmb, items):
        i = cmb.SelectedIndex
        return items[i]["clave"] if 0 <= i < len(items) else ""

    g4 = grupo(lambda: str(numero_grupo(g4)) + ". Partir de un documento existente")
    lbl_origen_est = Label()
    lbl_origen_est.Location = Point(330, 7)
    lbl_origen_est.AutoSize = True
    lbl_origen_est.ForeColor = C_MUTED
    lbl_origen_est.Font = F_SM
    lbl_origen_est.BackColor = C_HEAD
    g4.Controls.Add(lbl_origen_est)
    clb_origen = CheckedListBox()
    clb_origen.Location = Point(14, 36)
    clb_origen.Size = Size(1000, 70)
    clb_origen.CheckOnClick = True
    clb_origen.BorderStyle = getattr(BorderStyle, "None")
    clb_origen.BackColor = Color.White
    g4.Controls.Add(clb_origen)

    g5 = grupo(lambda: str(numero_grupo(g5)) + ". Partidas")
    txt_prod, marco_prod = cuadro(g5, "Producto: nombre, clave o código de barras  (F3)", 14, 34, 340)
    btn_busca_prod = boton_plano("Ver todos", 360, 52, 80, 26)
    g5.Controls.Add(btn_busca_prod)
    nud_cant = numero(g5, "Cantidad", 450, 34, 92, 4, 999999999)
    nud_cant.Value = Convert.ToDecimal(1)
    nud_precio = numero(g5, "Precio unitario", 554, 34, 118, 2, 999999999)
    nud_desc = numero(g5, "Descuento %", 684, 34, 84, 2, 100)
    cmb_imp = lista(g5, "Impuesto", 780, 34, 170)
    for i in impuestos:
        cmb_imp.Items.Add(i["nombre"])
    if cmb_imp.Items.Count > 0:
        idx16 = next((k for k, i in enumerate(impuestos) if abs(i["perc"] - 0.16) < 0.0001), 0)
        cmb_imp.SelectedIndex = idx16
    btn_agregar = boton_plano("Agregar", 964, 52, 92, 26, None, Color.White, True)
    btn_agregar.FlatAppearance.BorderSize = 0
    g5.Controls.Add(btn_agregar)
    btn_quitar = boton_plano("Quitar", 1062, 52, 80, 26, None, C_ROJO)
    g5.Controls.Add(btn_quitar)
    lbl_num_part = Label()
    lbl_num_part.Location = Point(14, 86)
    lbl_num_part.AutoSize = True
    lbl_num_part.ForeColor = C_MUTED
    lbl_num_part.Font = F_SM
    lbl_num_part.Text = "Sin partidas"
    g5.Controls.Add(lbl_num_part)
    lbl_hint = Label()
    lbl_hint.Location = Point(420, 86)
    lbl_hint.AutoSize = True
    lbl_hint.ForeColor = C_MUTED
    lbl_hint.Font = F_SM
    lbl_hint.Text = "Escribe el nombre y Enter · escanea el código de barras y Enter agrega directo · edita cantidad, precio, descuento e impuesto en la tabla."
    g5.Controls.Add(lbl_hint)

    grid = DataGridView()
    grid.Location = Point(14, 106)
    grid.Size = Size(560, 160)
    grid.AllowUserToAddRows = False
    grid.AllowUserToDeleteRows = False
    grid.RowHeadersVisible = False
    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
    grid.MultiSelect = False
    grid.BackgroundColor = Color.White
    grid.BorderStyle = BorderStyle.FixedSingle
    grid.EnableHeadersVisualStyles = False
    grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
    grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
    grid.GridColor = rgb(230, 235, 242)
    grid.RowTemplate.Height = 30
    grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    grid.ColumnHeadersDefaultCellStyle.BackColor = C_HEAD
    grid.ColumnHeadersDefaultCellStyle.ForeColor = C_TXT
    grid.ColumnHeadersDefaultCellStyle.Font = F_B
    grid.ColumnHeadersHeight = 30
    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = C_HEAD
    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = C_TXT
    grid.DefaultCellStyle.SelectionBackColor = C_SEL
    grid.DefaultCellStyle.SelectionForeColor = C_TXT
    grid.DefaultCellStyle.Padding = Padding(4, 0, 4, 0)

    def col_texto(nombre, titulo, peso, solo_lectura):
        c = DataGridViewTextBoxColumn()
        c.Name = nombre
        c.HeaderText = titulo
        c.ReadOnly = solo_lectura
        c.FillWeight = peso
        grid.Columns.Add(c)
        return c

    col_texto("Clave", "CLAVE", 12, True)
    col_texto("Desc", "DESCRIPCIÓN", 34, True)
    col_texto("Exist", "EXIST.", 8, True)
    col_texto("Unidad", "U.M.", 6, True)
    col_texto("Cant", "CANTIDAD", 9, False)
    col_texto("Precio", "PRECIO", 10, False)
    col_texto("DescPerc", "DESC. %", 7, False)
    c_imp = DataGridViewComboBoxColumn()
    c_imp.Name = "Imp"
    c_imp.HeaderText = "IMPUESTO"
    c_imp.FillWeight = 13
    c_imp.FlatStyle = FlatStyle.Flat
    for i in impuestos:
        c_imp.Items.Add(i["nombre"])
    grid.Columns.Add(c_imp)
    col_texto("Importe", "IMPORTE", 11, True)
    for nombre in ("Exist", "Cant", "Precio", "DescPerc", "Importe"):
        grid.Columns[nombre].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        grid.Columns[nombre].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
    grid.Columns["Importe"].DefaultCellStyle.Font = F_B
    g5.Controls.Add(grid)

    g6 = grupo(lambda: "Referencia y comentarios")
    txt_titulo, _m1 = cuadro(g6, "Título (opcional)", 14, 34, 190)
    txt_titulo.MaxLength = 120
    txt_coment, _m2 = cuadro(g6, "Comentarios (opcional)", 216, 34, 210)
    txt_coment.MaxLength = 250
    et(g6, "Ambos se guardan en el encabezado del documento.", 14, 80)

    g7 = grupo(lambda: "Totales")

    def dato_total(cap, x):
        et(g7, cap, x, 34)
        v = Label()
        v.Text = "0.00"
        v.Font = F_VAL
        v.ForeColor = C_TXT
        v.Location = Point(x, 50)
        v.Size = Size(140, 24)
        v.TextAlign = ContentAlignment.MiddleLeft
        v.BackColor = Color.Transparent
        g7.Controls.Add(v)
        return v
    lbl_sub = dato_total("Subtotal", 14)
    lbl_des = dato_total("Descuento", 164)
    lbl_imp = dato_total("Impuestos", 314)
    lbl_tot_et = Label()
    lbl_tot_et.Text = "TOTAL"
    lbl_tot_et.Font = F_SM
    lbl_tot_et.ForeColor = C_MUTED
    lbl_tot_et.AutoSize = False
    lbl_tot_et.Size = Size(240, 16)
    lbl_tot_et.TextAlign = ContentAlignment.MiddleRight
    lbl_tot_et.Location = Point(560, 32)
    g7.Controls.Add(lbl_tot_et)
    lbl_tot = Label()
    lbl_tot.Text = "0.00"
    lbl_tot.Font = F_TOT
    lbl_tot.AutoSize = False
    lbl_tot.Size = Size(240, 36)
    lbl_tot.TextAlign = ContentAlignment.MiddleRight
    lbl_tot.Location = Point(560, 46)
    g7.Controls.Add(lbl_tot)
    lbl_letra = Label()
    lbl_letra.Text = ""
    lbl_letra.Font = F_ITAL
    lbl_letra.ForeColor = C_MUTED
    lbl_letra.AutoSize = False
    lbl_letra.Size = Size(780, 20)
    lbl_letra.Location = Point(14, 86)
    lbl_letra.AutoEllipsis = True
    g7.Controls.Add(lbl_letra)

    p_pie = Panel()
    p_pie.BackColor = Color.White
    p_pie.Size = Size(1200, 40)

    def pintar_pie(s, e):
        pen = Pen(C_LINE)
        e.Graphics.DrawLine(pen, 0, 0, p_pie.Width, 0)
        pen.Dispose()
    p_pie.Paint += pintar_pie
    lbl_pie = Label()
    lbl_pie.Location = Point(14, 12)
    lbl_pie.AutoSize = True
    lbl_pie.ForeColor = C_MUTED
    lbl_pie.Font = F_BASE
    p_pie.Controls.Add(lbl_pie)
    lbl_avisos = Label()
    lbl_avisos.Location = Point(520, 5)
    lbl_avisos.Size = Size(660, 30)
    lbl_avisos.ForeColor = C_AMBAR
    lbl_avisos.Font = F_BASE
    lbl_avisos.TextAlign = ContentAlignment.MiddleLeft
    lbl_avisos.AutoEllipsis = True
    p_pie.Controls.Add(lbl_avisos)

    def hacer_lista(ancho, alto, renglones2):
        lb = ListBox()
        lb.Visible = False
        lb.Size = Size(ancho, alto)
        lb.BorderStyle = BorderStyle.FixedSingle
        lb.DrawMode = DrawMode.OwnerDrawFixed
        lb.ItemHeight = 42
        lb.IntegralHeight = False

        def dibuja(s, ev):
            if ev.Index < 0:
                return
            sel = (ev.State & DrawItemState.Selected) == DrawItemState.Selected
            b = SolidBrush(C_SEL if sel else Color.White)
            ev.Graphics.FillRectangle(b, ev.Bounds)
            b.Dispose()
            sf = StringFormat()
            sf.Trimming = StringTrimming.EllipsisCharacter
            sf.FormatFlags = StringFormatFlags.NoWrap
            b = SolidBrush(C_TXT)
            ev.Graphics.DrawString(str(lb.Items[ev.Index]), F_B, b, RectangleF(float(ev.Bounds.Left + 10), float(ev.Bounds.Top + 4), float(ev.Bounds.Width - 18), 18.0), sf)
            b.Dispose()
            if ev.Index < len(renglones2):
                b = SolidBrush(C_MUTED)
                ev.Graphics.DrawString(renglones2[ev.Index], F_SM, b, RectangleF(float(ev.Bounds.Left + 10), float(ev.Bounds.Top + 22), float(ev.Bounds.Width - 18), 16.0), sf)
                b.Dispose()
        lb.DrawItem += dibuja
        return lb

    ent_l2 = []
    prod_l2 = []
    lst_ent = hacer_lista(520, 258, ent_l2)
    lst_prod = hacer_lista(640, 294, prod_l2)
    lbl_toast = Label()
    lbl_toast.AutoSize = False
    lbl_toast.Height = 34
    lbl_toast.Visible = False
    lbl_toast.ForeColor = Color.White
    lbl_toast.BackColor = C_TXT
    lbl_toast.TextAlign = ContentAlignment.MiddleCenter
    lbl_toast.Font = F_B
    t_toast = FormsTimer()
    t_toast.Interval = 4500

    def oculta_toast(s, e):
        lbl_toast.Visible = False
        t_toast.Stop()
    t_toast.Tick += oculta_toast

    def aviso(m, mal=False):
        lbl_toast.Text = m
        lbl_toast.BackColor = C_ROJO if mal else C_VERDE
        lbl_toast.Width = min(frm.ClientSize.Width - 80, 900)
        lbl_toast.Location = Point((frm.ClientSize.Width - lbl_toast.Width) // 2, frm.ClientSize.Height - 96)
        lbl_toast.Visible = True
        lbl_toast.BringToFront()
        t_toast.Stop()
        t_toast.Start()

    for c in (ribbon, p_tipos, g1, g2, g3, g4, g5, g6, g7, p_pie, lst_ent, lst_prod, lbl_toast):
        frm.Controls.Add(c)

    def numero_grupo(g):
        n = 0
        for x in (g1, g2, g3, g4, g5):
            if (x is g3 and not E["ver_g3"]) or (x is g4 and not E["ver_g4"]):
                continue
            n += 1
            if x is g:
                return n
        return n

    def distribuir():
        W = frm.ClientSize.Width
        H = frm.ClientSize.Height
        m = 12
        w = W - 2 * m
        ribbon.SetBounds(m, 10, w, 100)
        info.Location = Point(ribbon.Width - info.Width - 10, 6)
        p_tipos.SetBounds(m, 118, w, 40)
        y = 166
        g1.SetBounds(m, y, 620, 132)
        g2.SetBounds(m + 632, y, w - 632, 132)
        y += 140
        g3.Visible = E["ver_g3"]
        g4.Visible = E["ver_g4"]
        if E["ver_g3"]:
            g3.SetBounds(m, y, w, 80)
            y += 88
        if E["ver_g4"]:
            g4.SetBounds(m, y, w, 116)
            y += 124
        yb = H - 40 - 112 - 8
        g6.SetBounds(m, yb, 440, 112)
        g7.SetBounds(m + 452, yb, w - 452, 112)
        g5.SetBounds(m, y, w, max(170, yb - 8 - y))
        p_pie.SetBounds(0, H - 40, W, 40)
        grid.SetBounds(14, 106, g5.Width - 28, g5.Height - 118)
        clb_origen.SetBounds(14, 36, g4.Width - 28, g4.Height - 46)
        lbl_tot_et.Location = Point(g7.Width - 254, 32)
        lbl_tot.Location = Point(g7.Width - 254, 46)
        lbl_letra.SetBounds(14, 86, g7.Width - 28, 20)
        x0 = max(420, lbl_pie.Right + 20)
        lbl_avisos.SetBounds(x0, 5, max(100, W - x0 - 14), 30)
        for g in (g1, g2, g3, g4, g5, g6, g7):
            g.Invalidate()
    frm.Resize += lambda s, e: distribuir()

    def prod_por(id_):
        for x in productos:
            if x["id"] == id_:
                return x
        return None

    def precio_de(id_):
        p = prod_por(id_)
        if p is None:
            return 0.0

        return round((p["costo"] if not es_venta() else p["venta"]) / tc_pesos(), 4)

    def tc_pesos():
        m = moneda_sel()
        v = Convert.ToDouble(nud_tc.Value)
        return v if m and m["simbolo"] != "MXN" and v > 0 else 1.0

    def exist_de(id_):
        e = existencias.get(str(id_))
        a = seleccionado(cmb_alm, alm_items)
        if not e or a is None:
            return 0.0
        return float(e.get(str(a["id"]), 0.0))

    def moneda_sel():
        return seleccionado(cmb_mon, mon_items)

    def totales():
        sub = des = imp = pz = 0.0
        faltan = 0
        for i, f in enumerate(filas):
            bruto = f["cant"] * f["precio"]
            d = bruto * f["desc"] / 100.0
            base = bruto - d
            sub += bruto
            des += d
            imp += base * perc_imp.get(f["imp"], 0.0)
            pz += f["cant"]
            if i < grid.Rows.Count:
                grid.Rows[i].Cells["Importe"].Value = "{:,.2f}".format(base)
            pr = prod_por(f["id"])
            if es_venta() and f["origen"] == 0 and pr is not None and not pr["servicio"] and f["cant"] > exist_de(f["id"]):
                faltan += 1
        tot = sub - des + imp
        mon = moneda_sel()
        sim = mon["simbolo"] if mon else ""
        lbl_sub.Text = "{:,.2f}".format(sub)
        lbl_des.Text = "-" + "{:,.2f}".format(des)
        lbl_des.ForeColor = C_ROJO if des > 0 else C_TXT
        lbl_imp.Text = "{:,.2f}".format(imp)
        lbl_tot.Text = "{:,.2f}".format(tot)
        lbl_tot.ForeColor = acento()
        lbl_tot_et.Text = "TOTAL" + (" (" + sim + ")" if sim else "")
        lbl_num_part.Text = "Sin partidas" if not filas else str(len(filas)) + " partida(s) · " + ("%g" % pz) + " pieza(s)"
        nombre_m = (mon["mn"].upper() if mon else "")
        palabra = "PESOS" if not mon else "PESOS" if "PESO" in nombre_m else "EUROS" if "EURO" in nombre_m else "DÓLARES" if "LAR" in nombre_m else nombre_m
        lbl_letra.Text = "SON: " + a_letras(tot, palabra, bool(mon) and mon["simbolo"] == "MXN")
        av = []
        ent = E["ent"]
        if es_venta() and ent is not None and ent["credito"] > 0 and ent["saldo"] + tot > ent["credito"]:
            av.append("⚠ El saldo con este documento (" + "{:,.2f}".format(ent["saldo"] + tot) + ") excede el límite de crédito")
        if faltan > 0:
            av.append("⚠ " + str(faltan) + " partida(s) piden más de la existencia del almacén")
        if any(f["precio"] <= 0 for f in filas):
            av.append("⚠ Hay partidas con precio en cero")
        if con_cfdi() and clave_de(cmb_metodo, metodo_items) == "PPD" and clave_de(cmb_forma, forma_items) != "99":
            av.append("⚠ Con método PPD la forma de pago debe ser 99 (por definir)")
        lbl_avisos.Text = "     ".join(av)
        for i, f in enumerate(filas):
            if i < grid.Rows.Count:
                pr = prod_por(f["id"])
                falta = es_venta() and f["origen"] == 0 and pr is not None and not pr["servicio"] and f["cant"] > exist_de(f["id"])
                grid.Rows[i].Cells["Exist"].Style.ForeColor = C_AMBAR if falta else C_MUTED

    def pintar_grid():
        E["suspender"] = True
        grid.Rows.Clear()
        for f in filas:
            pr = prod_por(f["id"])
            serv = pr is not None and pr["servicio"]
            r = grid.Rows.Add(f["clave"], f["nombre"] + ("   · de origen" if f["origen"] else ""), "servicio" if serv else ("%g" % exist_de(f["id"])), f["unidad"],
                              str(f["cant"]), str(f["precio"]), str(f["desc"]), nombre_imp.get(f["imp"]) or (impuestos[0]["nombre"] if impuestos else ""), "")
        E["suspender"] = False
        totales()

    def pintar_entidad():
        del pastillas[:]
        ent = E["ent"]
        txt_rfc.Text = "" if ent is None else ("(sin RFC)" if not ent["rfc"] else ent["rfc"])
        btn_hist.Enabled = ent is not None
        if ent is not None:
            saldo, cred = ent["saldo"], ent["credito"]
            pastillas.append(("Saldo abierto", "{:,.2f}".format(saldo), C_AMBAR if saldo > 0 else C_TXT))
            if cred > 0:
                pastillas.append(("Límite de crédito", "{:,.2f}".format(cred), C_TXT))
                pastillas.append(("Disponible", "{:,.2f}".format(cred - saldo), C_ROJO if cred - saldo < 0 else C_VERDE))
            pastillas.append(("Último documento", ent["ultimo"] if ent["ultimo"] else "—", C_TXT))
        p_chips.Invalidate()

    def origenes_visibles():
        t = tipo_actual()
        mod = t["origen"]
        clave = t["clave"]
        vistos = set()
        res = []
        if mod == 0:
            return res
        for o in origenes + pendientes:
            if o["modulo"] != mod or o["id"] in vistos:
                continue
            vistos.add(o["id"])
            if len(o["partidasPor"].get(clave, [])) == 0:
                continue
            if E["ent"] is not None and o["entidad"] != E["ent"]["id"]:
                continue
            res.append(o)
        return res

    def pintar_origenes():
        t = tipo_actual()
        mod = t["origen"]
        os_ = origenes_visibles()
        clave = t["clave"]
        ver = mod > 0
        if E["ver_g4"] != ver:
            E["ver_g4"] = ver
            distribuir()
        lbl_origen_est.Text = ("%d disponible(s): marca los que quieras surtir" % len(os_) if os_ else "elige primero la persona" if E["ent"] is None else "esta persona no tiene pendientes") if ver else ""
        clb_origen.Items.Clear()
        del vis["origen"][:]
        for o in os_:
            n = len(o["partidasPor"].get(clave, []))
            vis["origen"].append(o)
            clb_origen.Items.Add((o["folio"] or ("Documento " + str(o["id"]))) + "  ·  " + o["entidadNombre"] + "  ·  " + (o["fecha"] + "  ·  " if o.get("fecha") else "") + str(n) + " partida(s) pendiente(s)" +
                                 ("  ·  total " + "{:,.2f}".format(o["total"]) if o.get("total") else ""), o["id"] in origen_sel)

    def auto_metodo():

        if not con_cfdi() or E["metodo_manual"]:
            return
        c = seleccionado(cmb_cond, conds_vis)
        contado = c is not None and "CONTADO" in c["nombre"].upper()
        E["suspender"] = True
        poner_por_clave(cmb_metodo, metodo_items, "PUE" if contado else "PPD")
        poner_por_clave(cmb_forma, forma_items, "01" if contado else "99")
        E["suspender"] = False

    def elegir_entidad(x):
        E["ent"] = x
        E["suspender"] = True
        txt_ent.Text = x["nombre"]
        E["suspender"] = False
        lst_ent.Visible = False
        if tipo_actual()["condicion"] and x["cond"] > 0:
            poner_por_id(cmb_cond, conds_vis, x["cond"])
        if x["moneda"] > 0:
            poner_por_id(cmb_mon, mon_items, x["moneda"])
        if con_cfdi() and x["uso"]:
            poner_por_clave(cmb_uso, uso_items, x["uso"])
        auto_metodo()
        pendientes[:] = pendientes_de(x["id"], tipo_actual()["clave"])
        pintar_entidad()
        pintar_origenes()
        pintar_grid()
        txt_prod.Focus()

    def cambiar_tipo(idx):
        lado_antes = tipo_actual()["lado"]
        E["tipo"] = idx
        t = tipo_actual()
        if t["lado"] != lado_antes:
            E["ent"] = None
            E["suspender"] = True
            txt_ent.Text = ""
            E["suspender"] = False
        del origen_sel[:]
        del pendientes[:]
        filas[:] = [f for f in filas if not f["origen"]]
        for i, b in enumerate(btns_tipo):
            on = i == idx
            col = C_VENTA if TIPOS[i]["lado"] == "C" else C_COMPRA
            b.BackColor = col if on else Color.White
            b.ForeColor = Color.White if on else C_TXT
            b.FlatAppearance.BorderColor = col if on else C_LINE
            b.Font = F_B if on else F_BASE
        lbl_rib_titulo.Text = "Nuevo documento  ·  " + t["nombre"]
        btn_guardar.Invalidate()
        btn_agregar.BackColor = acento()
        cmb_cond.Enabled = t["condicion"]
        lbl_entrega_et.Visible = dt_entrega.Visible = t["entrega"]
        cmb_cond.Items.Clear()
        del conds_vis[:]
        for c in catalogo["condiciones"]:
            if (c["venta"] if t["lado"] == "C" else c["compra"]):
                conds_vis.append({"id": c["id"], "nombre": c["nombre"]})
                cmb_cond.Items.Add(c["nombre"])
        if cmb_cond.Items.Count > 0:
            cmb_cond.SelectedIndex = 0
        E["ver_g3"] = con_cfdi()
        E["metodo_manual"] = False
        auto_metodo()
        for f in filas:
            f["precio"] = precio_de(f["id"])
            f["auto"] = True
        lbl_pie.Text = "Elaboró: " + (quien_soy or "—") + "      Empresa: " + nombre_empresa + "      Módulo " + str(t["modulo"]) + " · " + t["nombre"]
        fo = folios.get(t["clave"])
        txt_folio.Text = ("≈ " + str(fo)) if fo is not None else ""
        txt_serie.Text = ""
        try:
            a = seleccionado(cmb_alm, alm_items)
            pre = ctx.erp.GetFolioPrefix(t["modulo"], a["id"]) if a else ""
            txt_serie.Text = S(pre)
        except Exception:
            pass
        pintar_entidad()
        pintar_origenes()
        pintar_grid()
        distribuir()
        if E["ent"] is not None:
            elegir_entidad(E["ent"])

    def desplegar(txt, marco, lst, renglones2, fuente, linea1, linea2, buscar_en, maximo, destino):
        q = txt.Text.lower().split()
        vis_ = []
        for x in fuente:
            h = buscar_en(x).lower()
            if all(w in h for w in q):
                vis_.append(x)
                if len(vis_) >= maximo:
                    break
        destino[:] = vis_
        lst.Items.Clear()
        del renglones2[:]
        for x in vis_:
            lst.Items.Add(linea1(x))
            renglones2.append(linea2(x))
        pos = frm.PointToClient(marco.Parent.PointToScreen(Point(marco.Left, marco.Bottom + 1)))
        lst.Location = pos
        lst.Height = min(280, lst.Items.Count * lst.ItemHeight + 4)
        lst.Visible = lst.Items.Count > 0
        if lst.Visible:
            lst.BringToFront()
            lst.SelectedIndex = 0

    def fuente_entidad():
        return catalogo["clientes"] if es_venta() else catalogo["proveedores"]

    def despliega_ent():
        desplegar(txt_ent, marco_ent, lst_ent, ent_l2, fuente_entidad(), lambda x: x["nombre"],
                  lambda x: (x["rfc"] if x["rfc"] else "sin RFC") + "   ·   saldo " + "{:,.2f}".format(x["saldo"]) + ("   ·   crédito " + "{:,.2f}".format(x["credito"]) if x["credito"] > 0 else ""),
                  lambda x: x["nombre"] + " " + x["rfc"] + " " + str(x["id"]), 60, vis["ent"])

    def elegir_de_lista_ent():
        i = lst_ent.SelectedIndex
        if 0 <= i < len(vis["ent"]):
            elegir_entidad(vis["ent"][i])

    def ent_cambio():
        if E["suspender"] or not txt_ent.Focused:
            return
        if E["ent"] is not None:
            E["ent"] = None
            del pendientes[:]
            pintar_entidad()
            pintar_origenes()
        despliega_ent()

    txt_ent.TextChanged += seguro(ent_cambio)
    txt_ent.Enter += seguro(lambda: (txt_ent.SelectAll(), despliega_ent() if E["ent"] is None else None))

    def tecla_ent(sender, ev):
        if ev.KeyCode == Keys.Down and lst_ent.Visible:
            lst_ent.Focus()
            ev.Handled = True
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista_ent)()
            ev.SuppressKeyPress = True
        if ev.KeyCode == Keys.Escape and lst_ent.Visible:
            lst_ent.Visible = False
            ev.SuppressKeyPress = True
            ev.Handled = True
    txt_ent.KeyDown += tecla_ent
    lst_ent.Click += seguro(elegir_de_lista_ent)

    def tecla_lst_ent(sender, ev):
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista_ent)()
            ev.SuppressKeyPress = True
    lst_ent.KeyDown += tecla_lst_ent

    def fuera_ent(sender, ev):
        if not lst_ent.Focused:
            lst_ent.Visible = False
    txt_ent.Leave += fuera_ent

    def limpiar_captura():
        E["prod"] = None
        E["suspender"] = True
        txt_prod.Text = ""
        E["suspender"] = False
        nud_cant.Value = Convert.ToDecimal(1)
        nud_precio.Value = Convert.ToDecimal(0)
        nud_desc.Value = Convert.ToDecimal(0)

    def agregar_captura():
        p = E["prod"]
        if p is None:
            txt_prod.Focus()
            raise Exception("Busca y elige un producto.")
        id_ = p["id"]
        cant = Convert.ToDouble(nud_cant.Value)
        precio = Convert.ToDouble(nud_precio.Value)
        desc = Convert.ToDouble(nud_desc.Value)
        i = cmb_imp.SelectedIndex
        imp = impuestos[i]["id"] if 0 <= i < len(impuestos) else 0
        if cant <= 0:
            nud_cant.Focus()
            raise Exception("La cantidad debe ser mayor a cero.")
        ya = next((f for f in filas if f["id"] == id_ and not f["origen"]), None)
        if ya is not None:
            ya["cant"] += cant
            if precio > 0:
                ya["precio"] = precio
                ya["auto"] = abs(precio - precio_de(id_)) < 1e-4
            ya["desc"] = desc
            ya["imp"] = imp
        else:
            filas.append({"id": id_, "clave": p["clave"], "nombre": p["nombre"], "unidad": p["unidad"], "cant": cant, "max": 0.0, "precio": precio, "auto": abs(precio - precio_de(id_)) < 1e-4, "desc": desc, "imp": imp, "origenItem": 0, "origen": 0})
        lst_prod.Visible = False
        limpiar_captura()
        pintar_grid()
        txt_prod.Focus()

    def elegir_producto(p):
        E["prod"] = p
        E["suspender"] = True
        txt_prod.Text = p["clave"] + " — " + p["nombre"]
        E["suspender"] = False
        lst_prod.Visible = False
        nud_cant.Value = Convert.ToDecimal(1)
        nud_precio.Value = Convert.ToDecimal(precio_de(p["id"]))
        nud_desc.Value = Convert.ToDecimal(min(100.0, E["ent"]["desc"] if es_venta() and E["ent"] is not None else 0.0))
        for k, i in enumerate(impuestos):
            if i["id"] == p["imp"]:
                cmb_imp.SelectedIndex = k
        nud_cant.Focus()
        nud_cant.Select(0, 10)

    def elegir_de_lista_prod():
        v = txt_prod.Text.strip()
        p = None
        if v:
            p = next((x for x in productos if (x["barras"] and x["barras"] == v) or x["clave"].lower() == v.lower()), None)
        if p is not None:
            elegir_producto(p)
            agregar_captura()
            return
        i = lst_prod.SelectedIndex
        if lst_prod.Visible and 0 <= i < len(vis["prod"]):
            elegir_producto(vis["prod"][i])
            return
        if E["prod"] is not None:
            agregar_captura()

    def despliega_prod():
        desplegar(txt_prod, marco_prod, lst_prod, prod_l2, productos, lambda x: x["nombre"],
                  lambda x: (x["clave"] + "   ·   " if x["clave"] else "") + ("servicio" if x["servicio"] else "existencia " + ("%g" % exist_de(x["id"]))) + "   ·   precio " + "{:,.2f}".format(precio_de(x["id"])),
                  lambda x: x["nombre"] + " " + x["clave"] + " " + x["barras"], 60, vis["prod"])

    def prod_cambio():
        if txt_prod.Focused and not E["suspender"]:
            E["prod"] = None
            despliega_prod()
    txt_prod.TextChanged += seguro(prod_cambio)

    def tecla_prod(sender, ev):
        if ev.KeyCode == Keys.Down and lst_prod.Visible:
            lst_prod.Focus()
            ev.Handled = True
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista_prod)()
            ev.SuppressKeyPress = True
        if ev.KeyCode == Keys.Escape and lst_prod.Visible:
            lst_prod.Visible = False
            ev.SuppressKeyPress = True
            ev.Handled = True
    txt_prod.KeyDown += tecla_prod
    lst_prod.Click += seguro(elegir_de_lista_prod)

    def tecla_lst_prod(sender, ev):
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista_prod)()
            ev.SuppressKeyPress = True
    lst_prod.KeyDown += tecla_lst_prod

    def fuera_prod(sender, ev):
        if not lst_prod.Focused:
            lst_prod.Visible = False
    txt_prod.Leave += fuera_prod
    btn_busca_prod.Click += seguro(lambda: (setattr(txt_prod, "Text", ""), txt_prod.Focus(), despliega_prod()))

    def tecla_captura(sender, ev):
        if ev.KeyCode == Keys.Enter:
            seguro(agregar_captura)()
            ev.SuppressKeyPress = True
    for n in (nud_cant, nud_precio, nud_desc):
        n.KeyDown += tecla_captura
    btn_agregar.Click += seguro(agregar_captura)

    def quitar():
        if grid.CurrentRow is not None and 0 <= grid.CurrentRow.Index < len(filas):
            del filas[grid.CurrentRow.Index]
            pintar_grid()
    btn_quitar.Click += seguro(quitar)

    def fin_edicion(sender, ev):
        if E["suspender"] or ev.RowIndex < 0 or ev.RowIndex >= len(filas):
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
                    f["auto"] = False
                elif col == "DescPerc":
                    f["desc"] = min(100.0, n)
                    grid.Rows[ev.RowIndex].Cells["DescPerc"].Value = str(f["desc"])
            totales()
        except Exception as ex:
            msg(str(ex))
    grid.CellEndEdit += fin_edicion

    def confirma_combo(sender, ev):
        if grid.IsCurrentCellDirty and grid.CurrentCell is not None and grid.CurrentCell.GetType().Name == "DataGridViewComboBoxCell":
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
    grid.CurrentCellDirtyStateChanged += confirma_combo
    grid.DataError += lambda s, ev: setattr(ev, "ThrowException", False)

    def cambio_alm():
        pintar_grid()
        try:
            a = seleccionado(cmb_alm, alm_items)
            txt_serie.Text = S(ctx.erp.GetFolioPrefix(tipo_actual()["modulo"], a["id"])) if a else ""
        except Exception:
            pass
    cmb_alm.SelectedIndexChanged += seguro(cambio_alm)

    def cambio_mon():
        m = moneda_sel()
        E["en_cambio_mon"] = True
        if m:
            nud_tc.Value = Convert.ToDecimal(max(0.0001, 1.0 if m["simbolo"] == "MXN" else m["tc"]))
            nud_tc.Enabled = m["simbolo"] != "MXN"
        E["en_cambio_mon"] = False
        repreciar(True)

    def repreciar(cambio_moneda):
        ahora = tc_pesos()
        antes = E["tc_prev"]
        E["tc_prev"] = ahora
        for f in filas:
            if f["origen"] != 0:
                continue
            if f.get("auto"):
                f["precio"] = precio_de(f["id"])
            elif cambio_moneda and abs(ahora - antes) > 1e-9:
                f["precio"] = round(f["precio"] * antes / ahora, 4)
        pintar_grid()
        totales()
    cmb_mon.SelectedIndexChanged += seguro(cambio_mon)
    nud_tc.ValueChanged += seguro(lambda: None if E["en_cambio_mon"] else repreciar(False))
    cmb_cond.SelectedIndexChanged += seguro(auto_metodo)

    def cambio_metodo():
        E["metodo_manual"] = True
        if clave_de(cmb_metodo, metodo_items) == "PPD":
            E["suspender"] = True
            poner_por_clave(cmb_forma, forma_items, "99")
            E["suspender"] = False
        totales()
    cmb_metodo.SelectionChangeCommitted += seguro(cambio_metodo)

    def cambio_forma():
        E["metodo_manual"] = True
        totales()
    cmb_forma.SelectionChangeCommitted += seguro(cambio_forma)

    def cambio_origen(sender, ev):
        try:
            marcados = []
            for i in range(clb_origen.Items.Count):
                chk = (ev.NewValue == CheckState.Checked) if i == ev.Index else clb_origen.GetItemChecked(i)
                if chk:
                    marcados.append(vis["origen"][i])
            origen_sel[:] = [o["id"] for o in marcados]
            t = tipo_actual()
            if len(set(o["entidad"] for o in marcados)) > 1:
                raise Exception("Los documentos de origen son de entidades distintas: quita alguno.")
            filas[:] = [f for f in filas if not f["origen"]]
            for o in marcados:
                for p in o["partidasPor"].get(t["clave"], []):
                    filas.append({"id": p["id"], "clave": p["clave"], "nombre": p["nombre"], "unidad": p["unidad"], "cant": p["cant"], "max": p["cant"],
                                  "precio": p["precio"], "desc": p["desc"], "imp": p["imp"], "origenItem": p["origenItem"], "origen": o["id"]})
            if marcados:
                en = next((e for e in fuente_entidad() if e["id"] == marcados[0]["entidad"]), None)
                if en is not None and (E["ent"] is None or E["ent"]["id"] != en["id"]):
                    E["ent"] = en
                    E["suspender"] = True
                    txt_ent.Text = en["nombre"]
                    E["suspender"] = False
                    pintar_entidad()
                for i, a in enumerate(alm_items):
                    if a["id"] == marcados[0]["almacen"]:
                        cmb_alm.SelectedIndex = i
            pintar_grid()
        except Exception as ex:
            msg(str(ex))
    clb_origen.ItemCheck += cambio_origen

    def ver_historial():
        ent = E["ent"]
        if ent is None:
            return
        ult = ultimos_de(ent["id"])
        h = Form()
        h.Text = "Últimos documentos · " + ent["nombre"]
        h.Size = Size(640, 330)
        h.StartPosition = FormStartPosition.CenterParent
        h.BackColor = Color.White
        h.Font = F_BASE
        h.MaximizeBox = False
        l2 = []
        lb = hacer_lista(600, 250, l2)
        lb.Dock = DockStyle.Fill
        lb.BorderStyle = getattr(BorderStyle, "None")
        for d in ult:
            lb.Items.Add(d["tipo"] + " " + d["folio"])
            l2.append(d["fecha"] + "   ·   total " + "{:,.2f}".format(d["total"]) + ("   ·   saldo " + "{:,.2f}".format(d["saldo"]) if d["saldo"] > 0 else ""))
        lb.Visible = True

        def abrir(s, e):
            i = lb.SelectedIndex
            if 0 <= i < len(ult):
                try:
                    ctx.erp.AbrirDocumento(ult[i]["id"], ult[i]["modulo"])
                except Exception as ex:
                    msg(str(ex))
        lb.DoubleClick += abrir
        h.Controls.Add(lb)
        pie_h = Label()
        pie_h.Text = "Doble clic para abrir el documento en Comercial"
        pie_h.Dock = DockStyle.Bottom
        pie_h.Height = 26
        pie_h.ForeColor = C_MUTED
        pie_h.Font = F_SM
        pie_h.TextAlign = ContentAlignment.MiddleLeft
        pie_h.Padding = Padding(10, 0, 0, 0)
        h.Controls.Add(pie_h)
        h.Show(frm)
    btn_hist.Click += seguro(ver_historial)

    def limpiar():
        E["ent"] = None
        E["suspender"] = True
        txt_ent.Text = ""
        E["suspender"] = False
        del filas[:]
        del origen_sel[:]
        del pendientes[:]
        txt_titulo.Text = ""
        txt_coment.Text = ""
        limpiar_captura()
        E["metodo_manual"] = False
        auto_metodo()
        pintar_entidad()
        pintar_origenes()
        pintar_grid()
        txt_ent.Focus()

    def crear(otro):
        if E["guardando"]:
            return
        t = tipo_actual()
        if E["ent"] is None:
            txt_ent.Focus()
            raise Exception("Elige el " + ("cliente" if t["lado"] == "C" else "proveedor") + " de la lista (escribe y selecciona).")
        if not filas:
            txt_prod.Focus()
            raise Exception("Agrega al menos una partida.")
        if any(f["cant"] <= 0 for f in filas):
            raise Exception("Todas las partidas deben tener cantidad mayor a cero.")
        if con_cfdi() and (not clave_de(cmb_uso, uso_items) or not clave_de(cmb_forma, forma_items) or not clave_de(cmb_metodo, metodo_items)):
            raise Exception("Completa los datos fiscales: uso del CFDI, forma de pago y método de pago.")
        alm = seleccionado(cmb_alm, alm_items)
        mon = moneda_sel()
        cc = seleccionado(cmb_cc, cc_items)
        cond = seleccionado(cmb_cond, conds_vis)
        spec = {
            "tipo": t["clave"], "almacen": alm["id"] if alm else 0, "entidad": E["ent"]["id"],
            "condicion": cond["id"] if t["condicion"] and cond else 0,
            "fecha": dt_fecha.Value.ToString("yyyy-MM-dd"), "entrega": dt_entrega.Value.ToString("yyyy-MM-dd") if t["entrega"] else "",
            "titulo": txt_titulo.Text, "comentarios": txt_coment.Text, "origenes": list(origen_sel),
            "moneda": mon["id"] if mon else 0, "tc": Convert.ToDouble(nud_tc.Value), "centro": cc["id"] if cc else 0,
            "partidas": [{"id": f["id"], "nombre": f["nombre"], "cant": f["cant"], "precio": f["precio"], "desc": f["desc"], "imp": f["imp"], "origenItem": f["origenItem"]} for f in filas],
        }
        if con_cfdi():
            spec["uso"] = clave_de(cmb_uso, uso_items)
            spec["forma"] = clave_de(cmb_forma, forma_items)
            spec["metodo"] = clave_de(cmb_metodo, metodo_items)
        E["guardando"] = True
        frm.Cursor = Cursors.WaitCursor
        try:
            doc = crear_documento(spec)
        finally:
            E["guardando"] = False
            frm.Cursor = Cursors.Default
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass
        folio = ""
        try:
            folio = S(ctx.scalar("SELECT ISNULL(FolioPrefix,'') + CAST(ISNULL(Folio,'') AS NVARCHAR(30)) FROM docDocument WHERE DocumentID = " + str(doc)))
        except Exception:
            pass
        abrir_mal = None
        try:
            ctx.erp.AbrirDocumento(doc, t["modulo"])
        except Exception as ex:
            abrir_mal = str(ex)
        if abrir_mal is not None:
            MessageBox.Show("El documento " + (folio or str(doc)) + " se creó, pero no se pudo abrir en Comercial:\n" + abrir_mal + "\n\nBúscalo por su folio.", "Crear documento", MessageBoxButtons.OK, MessageBoxIcon.Information)
        E["resultado"] = "OK " + t["nombre"] + " id=" + str(doc)
        if otro:
            limpiar()
            aviso("Documento " + (folio or str(doc)) + " creado" + (" y abierto en Comercial." if abrir_mal is None else "."))
        else:
            frm.Close()

    def tecla_forma(sender, ev):
        if ev.KeyCode == Keys.F5:
            seguro(lambda: crear(False))()
            ev.Handled = True
        elif ev.KeyCode == Keys.F6:
            seguro(lambda: crear(True))()
            ev.Handled = True
        elif ev.KeyCode == Keys.F2:
            txt_ent.Focus()
            ev.Handled = True
        elif ev.KeyCode == Keys.F3:
            txt_prod.Focus()
            ev.Handled = True
        elif ev.KeyCode == Keys.Escape and not lst_ent.Visible and not lst_prod.Visible:
            frm.Close()
            ev.Handled = True
    frm.KeyDown += tecla_forma

    m0 = moneda_sel()
    if m0:
        nud_tc.Value = Convert.ToDecimal(max(0.0001, m0["tc"]))
        nud_tc.Enabled = m0["simbolo"] != "MXN"
    cambiar_tipo(0)
    if origenes:
        ti = next((i for i, t in enumerate(TIPOS) if t["origen"] == origenes[0]["modulo"]), -1)
        if ti >= 0:
            cambiar_tipo(ti)
            for i in range(clb_origen.Items.Count):
                clb_origen.SetItemChecked(i, True)
    distribuir()
    frm.ShowDialog()
    result = E["resultado"]

if not _modo_prueba:
    principal()
