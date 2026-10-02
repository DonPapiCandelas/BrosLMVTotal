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

    def ent(tabla):
        return ("SELECT be.BusinessEntityID AS id, ISNULL(be.CommercialName, be.OfficialName) AS nombre, ISNULL(mi.OfficialNumber,'') AS rfc FROM " + tabla + " x "
                "JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID "
                "WHERE be.DeletedOn IS NULL ORDER BY nombre")

    def lista(tabla):
        return [{"id": I(r["id"]), "nombre": S(r["nombre"]), "rfc": S(r["rfc"])} for r in ctx.query(ent(tabla))]

    lados = set(t["lado"] for t in TIPOS if t["clave"] in claves)
    cat["clientes"] = lista("orgCustomer") if "C" in lados else []
    cat["proveedores"] = lista("orgSupplier") if "P" in lados else []
    cat["condiciones"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "venta": I(r["v"]) == 1, "compra": I(r["c"]) == 1} for r in ctx.query(
        "SELECT PaymentTermID AS id, PaymentTermName AS nombre, Sales AS v, Buys AS c FROM engPaymentTerm WHERE DeletedOn IS NULL ORDER BY PaymentTermID")]
    cat["impuestos"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "perc": D(r["perc"])} for r in ctx.query(
        "SELECT t.TaxTypeID AS id, t.TaxTypeName AS nombre, ISNULL(tp.IVA_Perc,0) AS perc FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID ORDER BY t.TaxTypeName")]
    # Productos: lo mínimo para buscar y poner precio. Tope de seguridad de 30,000.
    cat["productos"] = [{"id": I(r["id"]), "clave": S(r["clave"]), "nombre": S(r["nombre"]), "unidad": S(r["unidad"]), "imp": I(r["imp"]),
                         "venta": D(r["venta"]), "costo": D(r["costo"])} for r in ctx.query(
        "SELECT TOP 30000 ProductID AS id, ISNULL(ProductKey,'') AS clave, ProductName AS nombre, ISNULL(Unit,'') AS unidad, ISNULL(TaxTypeID,0) AS imp, "
        "ISNULL(PriceList,0) AS venta, ISNULL(CostPrice,0) AS costo FROM orgProduct WHERE DeletedOn IS NULL ORDER BY ProductName")]
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
        sets = [t["perfil"], "CampaignID=NULL", "CostCenterID=NULL", "ProjectID=NULL"]
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
:root{--fondo:#f4f6f9;--tarjeta:#fff;--texto:#16263a;--suave:#64748b;--linea:#d5dde8;--azul:#2d6fe0;--rojo:#c82828;--verde:#16803b;--zebra:#f8fafc}
*{box-sizing:border-box}body{margin:0;font:13px 'Segoe UI',Arial,sans-serif;background:var(--fondo);color:var(--texto)}
header{padding:14px 22px 4px}h1{margin:0;font-size:20px}.sub{color:var(--suave);margin-top:2px}
.card{background:var(--tarjeta);border:1px solid var(--linea);border-radius:9px;margin:10px 22px;padding:12px 14px}
.card h2{margin:0 0 8px;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--suave)}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px 16px}label{display:block;color:var(--suave);font-size:12px;margin-bottom:3px}
input,select,textarea,button{font:inherit;border:1px solid var(--linea);border-radius:6px;padding:6px 9px;background:#fff;color:var(--texto);width:100%}button{cursor:pointer;width:auto}button.p{background:var(--azul);color:#fff;border-color:var(--azul)}
.seg{display:flex;flex-wrap:wrap;gap:0}.seg button{border-radius:0;margin-left:-1px}.seg button:first-child{border-radius:6px 0 0 6px;margin-left:0}.seg button:last-child{border-radius:0 6px 6px 0}.seg button.on{background:var(--azul);color:#fff;border-color:var(--azul)}
.combo{position:relative}.lista{position:absolute;left:0;right:0;top:100%;z-index:20;background:#fff;border:1px solid var(--linea);border-radius:6px;max-height:240px;overflow:auto;box-shadow:0 6px 18px #0002;display:none}
.lista div{padding:6px 9px;cursor:pointer}.lista div:hover,.lista div.sel{background:#e8f0ff}.lista small{color:var(--suave)}
table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}th{font-size:11px;text-transform:uppercase;letter-spacing:.04em;color:var(--suave);text-align:right;padding:4px 6px;border-bottom:1px solid var(--linea)}th:nth-child(-n+3),td:nth-child(-n+3){text-align:left}
td{padding:3px 4px;text-align:right;border-bottom:1px solid #eef2f7}td input,td select{padding:4px 6px;text-align:right}td.x{width:30px}.tot{display:flex;justify-content:flex-end;gap:26px;margin-top:10px}.tot div{text-align:right}.tot b{display:block;font-size:11px;text-transform:uppercase;color:var(--suave);font-weight:600}.tot span{font-size:16px}
.acc{display:flex;justify-content:flex-end;gap:10px;margin:10px 22px 26px}.err{margin:0 22px;color:var(--rojo);min-height:18px}.nota{color:var(--suave);font-size:12px}.org label{display:flex;gap:8px;align-items:center;color:var(--texto);font-size:13px}.org input{width:auto}
</style></head><body>
<header><h1>Crear documento</h1><div class='sub' id='sub'></div></header>
<div class='card'><h2>Tipo de documento</h2><div class='seg' id='segTipo'></div></div>
<div class='card' id='cardOrigen' style='display:none'><h2>Partir de un documento ya existente</h2><div class='org' id='origenes'></div><div class='nota'>Se cargan solo las partidas que aún faltan por surtir. Puedes combinar varios documentos del mismo cliente o proveedor.</div></div>
<div class='card'><h2>Datos generales</h2><div class='grid'>
 <div><label id='lblEnt'>Cliente</label><div class='combo'><input id='ent' placeholder='Escribe nombre, RFC o clave…' autocomplete='off'><div class='lista' id='lstEnt'></div></div></div>
 <div><label>Almacén</label><select id='alm'></select></div>
 <div id='bxCond'><label>Condición de pago</label><select id='cond'></select></div>
 <div><label>Fecha del documento</label><input type='date' id='fecha'></div>
 <div id='bxEntrega'><label>Fecha de entrega</label><input type='date' id='entrega'></div>
 <div><label>Título (opcional)</label><input id='titulo' maxlength='120'></div>
</div><div style='margin-top:10px'><label>Comentarios (opcional)</label><textarea id='coment' rows='2'></textarea></div></div>
<div class='card'><h2>Partidas</h2>
 <div class='combo' style='max-width:560px;margin-bottom:8px'><input id='prod' placeholder='Buscar producto por nombre o clave y presionar Enter…' autocomplete='off'><div class='lista' id='lstProd'></div></div>
 <div style='overflow:auto'><table><thead><tr><th>Clave</th><th>Producto</th><th>Unidad</th><th>Cantidad</th><th>Precio</th><th>Desc. %</th><th>Impuesto</th><th>Importe</th><th></th></tr></thead><tbody id='tb'></tbody></table></div>
 <div class='tot'><div><b>Subtotal</b><span id='tSub'>0.00</span></div><div><b>Descuento</b><span id='tDes'>0.00</span></div><div><b>Impuestos</b><span id='tImp'>0.00</span></div><div><b>Total</b><span id='tTot'>0.00</span></div></div>
 <div class='nota' id='avisoTot'>El total es un estimado; Comercial lo recalcula al crear el documento (descuentos globales, redondeos).</div></div>
<div class='err' id='err'></div>
<div class='acc'><button onclick='enviar({accion:&quot;cancelar&quot;})'>Cancelar</button><button class='p' onclick='crear()'>Crear documento</button></div>
<script>
var DATOS=__DATOS__;
function enviar(o){window.chrome.webview.postMessage(JSON.stringify(o));}
function esc(s){return String(s==null?'':s).replace(/[&<>']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;'}[c];});}
function f2(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:2});}
function $(i){return document.getElementById(i);}
var CAT=DATOS.cat,TIPOS=DATOS.tipos,percImp={};CAT.impuestos.forEach(function(i){percImp[i.id]=i.perc;});
var ST={tipo:TIPOS[0].clave,entidad:0,partidas:[],origenes:[]};
function tipoAct(){return TIPOS.filter(function(t){return t.clave===ST.tipo;})[0];}
function norm(s){return String(s||'').toLowerCase();}
// ---- combos de búsqueda ----
function combo(inp,lst,fuente,etiqueta,elegir){
  var sel=-1,vis=[];
  function pintar(){var q=norm(inp.value).split(/\s+/).filter(Boolean);vis=fuente().filter(function(x){var h=norm(etiqueta(x)[2]);return q.every(function(t){return h.indexOf(t)>=0;});}).slice(0,40);
    lst.innerHTML=vis.map(function(x,i){var e=etiqueta(x);return '<div data-i=\''+i+'\' class=\''+(i===sel?'sel':'')+'\'>'+esc(e[0])+(e[1]?' <small>'+esc(e[1])+'</small>':'')+'</div>';}).join('');lst.style.display=vis.length?'block':'none';}
  inp.addEventListener('input',function(){sel=-1;pintar();});inp.addEventListener('focus',function(){pintar();});
  inp.addEventListener('keydown',function(ev){if(ev.key==='ArrowDown'){sel=Math.min(vis.length-1,sel+1);pintar();ev.preventDefault();}else if(ev.key==='ArrowUp'){sel=Math.max(0,sel-1);pintar();ev.preventDefault();}
    else if(ev.key==='Enter'){var x=vis[Math.max(sel,0)];if(x){elegir(x);lst.style.display='none';}ev.preventDefault();}else if(ev.key==='Escape'){lst.style.display='none';}});
  lst.addEventListener('mousedown',function(ev){var d=ev.target.closest('div[data-i]');if(d){elegir(vis[+d.getAttribute('data-i')]);lst.style.display='none';ev.preventDefault();}});
  inp.addEventListener('blur',function(){setTimeout(function(){lst.style.display='none';},120);});
}
function listaEnt(){return tipoAct().lado==='C'?CAT.clientes:CAT.proveedores;}
combo($('ent'),$('lstEnt'),listaEnt,function(x){return [x.nombre,x.rfc,x.nombre+' '+x.rfc+' '+x.id];},function(x){ST.entidad=x.id;$('ent').value=x.nombre;});
$('ent').addEventListener('input',function(){ST.entidad=0;});
combo($('prod'),$('lstProd'),function(){return CAT.productos;},function(x){return [x.nombre,x.clave,x.nombre+' '+x.clave];},function(x){agregar(x);$('prod').value='';});
// ---- tipo ----
function pintarTipos(){$('segTipo').innerHTML=TIPOS.map(function(t){return '<button class=\''+(t.clave===ST.tipo?'on':'')+'\' onclick=\'setTipo(&quot;'+t.clave+'&quot;)\'>'+esc(t.nombre)+'</button>';}).join('');}
function setTipo(c){var antes=tipoAct().lado;ST.tipo=c;var t=tipoAct();if(t.lado!==antes){ST.entidad=0;$('ent').value='';}ST.origenes=[];ST.partidas=ST.partidas.filter(function(p){return !p.orig;});
  $('lblEnt').textContent=t.lado==='C'?'Cliente':'Proveedor';$('bxCond').style.display=t.condicion?'':'none';$('bxEntrega').style.display=t.entrega?'':'none';
  $('cond').innerHTML=CAT.condiciones.filter(function(c){return t.lado==='C'?c.venta:c.compra;}).map(function(c){return '<option value='+c.id+'>'+esc(c.nombre)+'</option>';}).join('');
  ST.partidas.forEach(function(p){p.precio=precioDe(p.id);});
  pintarTipos();pintarOrigenes();pintarPartidas();$('sub').textContent=t.nombre+' · módulo '+t.modulo;}
// ---- documentos de origen ----
function pintarOrigenes(){var t=tipoAct(),os=t.origen?DATOS.origenes.filter(function(o){return o.modulo===t.origen;}):[];$('cardOrigen').style.display=os.length?'':'none';
  $('origenes').innerHTML=os.map(function(o){return '<label><input type=checkbox data-id='+o.id+' '+(ST.origenes.indexOf(o.id)>=0?'checked':'')+' onchange=\'alternarOrigen(this)\'> '+esc(o.folio||('Documento '+o.id))+' · '+esc(o.entidadNombre)+' · '+((o.partidasPor[ST.tipo]||[]).length)+' partida(s) pendiente(s)</label>';}).join('');}
function alternarOrigen(cb){var id=+cb.getAttribute('data-id'),i=ST.origenes.indexOf(id);if(cb.checked&&i<0)ST.origenes.push(id);if(!cb.checked&&i>=0)ST.origenes.splice(i,1);
  var os=DATOS.origenes.filter(function(o){return ST.origenes.indexOf(o.id)>=0;});
  if(os.some(function(o){return o.entidad!==os[0].entidad;})){$('err').textContent='Los documentos de origen son de entidades distintas: quita alguno.';}else{$('err').textContent='';}
  ST.partidas=ST.partidas.filter(function(p){return !p.orig;});
  os.forEach(function(o){(o.partidasPor[ST.tipo]||[]).forEach(function(p){ST.partidas.push({id:p.id,clave:p.clave,nombre:p.nombre,unidad:p.unidad,cant:p.cant,max:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem,orig:o.id});});});
  if(os.length){ST.entidad=os[0].entidad;$('ent').value=os[0].entidadNombre;if(os[0].almacen)$('alm').value=os[0].almacen;}
  pintarPartidas();}
// ---- partidas ----
function prodPor(id){return CAT.productos.filter(function(p){return p.id===id;})[0];}
function precioDe(id){var p=prodPor(id);if(!p)return 0;return tipoAct().precio==='venta'?p.venta:p.costo;}
function agregar(x){var ya=ST.partidas.filter(function(p){return p.id===x.id&&!p.orig;})[0];if(ya){ya.cant+=1;}else{ST.partidas.push({id:x.id,clave:x.clave,nombre:x.nombre,unidad:x.unidad,cant:1,precio:precioDe(x.id),desc:0,imp:x.imp,origenItem:0,orig:0});}
  pintarPartidas();var f=document.querySelector('#tb tr:last-child input');if(f&&!ya){f.focus();f.select();}}
function pintarPartidas(){
  $('tb').innerHTML=ST.partidas.map(function(p,i){return '<tr><td>'+esc(p.clave)+'</td><td>'+esc(p.nombre)+(p.orig?' <span class=nota>(origen)</span>':'')+'</td><td>'+esc(p.unidad)+'</td>'
   +'<td><input type=number step=any min=0 value=\''+p.cant+'\' style=\'width:90px\' oninput=\'edita('+i+',&quot;cant&quot;,this.value)\'></td>'
   +'<td><input type=number step=any min=0 value=\''+p.precio+'\' style=\'width:100px\' oninput=\'edita('+i+',&quot;precio&quot;,this.value)\'></td>'
   +'<td><input type=number step=any min=0 max=100 value=\''+p.desc+'\' style=\'width:70px\' oninput=\'edita('+i+',&quot;desc&quot;,this.value)\'></td>'
   +'<td><select onchange=\'edita('+i+',&quot;imp&quot;,this.value)\'>'+CAT.impuestos.map(function(m){return '<option value='+m.id+(m.id===p.imp?' selected':'')+'>'+esc(m.nombre)+'</option>';}).join('')+'</select></td>'
   +'<td id=imp'+i+'></td><td class=x><button onclick=\'quita('+i+')\' title=\'Quitar\'>✕</button></td></tr>';}).join('');
  totales();}
function edita(i,c,v){var p=ST.partidas[i];v=+v;if(c==='imp')p.imp=v;else{if(isNaN(v)||v<0)v=0;if(c==='desc'&&v>100)v=100;if(c==='cant'&&p.max&&v>p.max)v=p.max;p[c]=v;}totales();}
function quita(i){ST.partidas.splice(i,1);pintarPartidas();}
function totales(){var s=0,d=0,im=0;ST.partidas.forEach(function(p,i){var bruto=p.cant*p.precio,desc=bruto*p.desc/100,base=bruto-desc,tax=base*(percImp[p.imp]||0);s+=bruto;d+=desc;im+=tax;var c=$('imp'+i);if(c)c.textContent=f2(base);});
  $('tSub').textContent=f2(s);$('tDes').textContent=f2(d);$('tImp').textContent=f2(im);$('tTot').textContent=f2(s-d+im);}
// ---- crear ----
function crear(){var t=tipoAct();$('err').textContent='';
  if(!ST.entidad){$('err').textContent='Elige el '+(t.lado==='C'?'cliente':'proveedor')+' de la lista (escribe y selecciona).';return;}
  if(!ST.partidas.length){$('err').textContent='Agrega al menos una partida.';return;}
  if(ST.partidas.some(function(p){return !(p.cant>0);})){$('err').textContent='Todas las partidas deben tener cantidad mayor a cero.';return;}
  enviar({accion:'crear',spec:JSON.stringify({tipo:ST.tipo,almacen:+$('alm').value,entidad:ST.entidad,condicion:t.condicion?+$('cond').value:0,fecha:$('fecha').value,entrega:t.entrega?$('entrega').value:'',titulo:$('titulo').value,comentarios:$('coment').value,
    origenes:ST.origenes,partidas:ST.partidas.map(function(p){return {id:p.id,nombre:p.nombre,cant:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem||0};})})});}
// ---- arranque ----
$('alm').innerHTML=CAT.almacenes.map(function(a){return '<option value='+a.id+'>'+esc(a.nombre)+'</option>';}).join('');
var hoy=new Date(),iso=function(d){return d.getFullYear()+'-'+('0'+(d.getMonth()+1)).slice(-2)+'-'+('0'+d.getDate()).slice(-2);};
$('fecha').value=iso(hoy);$('entrega').value=iso(hoy);
if(DATOS.inicial){var I=DATOS.inicial;ST.tipo=I.tipo;}
setTipo(ST.tipo);
if(DATOS.inicial){var I2=DATOS.inicial;ST.entidad=I2.entidad;var en=listaEnt().filter(function(x){return x.id===I2.entidad;})[0];$('ent').value=en?en.nombre:'';$('alm').value=I2.almacen;if(I2.condicion)$('cond').value=I2.condicion;
  $('fecha').value=I2.fecha||$('fecha').value;$('entrega').value=I2.entrega||$('entrega').value;$('titulo').value=I2.titulo||'';$('coment').value=I2.comentarios||'';
  ST.origenes=I2.origenes||[];ST.partidas=I2.partidas.map(function(p){var pr=prodPor(p.id)||{};var o=p.origenItem?1:0;return {id:p.id,clave:pr.clave||'',nombre:p.nombre||pr.nombre||'',unidad:pr.unidad||'',cant:p.cant,precio:p.precio,desc:p.desc,imp:p.imp,origenItem:p.origenItem||0,orig:o?(ST.origenes[0]||1):0};});
  pintarOrigenes();pintarPartidas();}
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
