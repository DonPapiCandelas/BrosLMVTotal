# lang: python
# timeout: 1800
# AppKey recomendado: PAGO_PROVEEDOR_PYTHON_WEBVIEW2
# Plantilla: Pago a proveedor (Python · ventana HTML)
# Categoria: Cuentas por pagar
# Documentacion: PAGO_PROVEEDOR.html
# ⚠ PLANTILLA AVANZADA, NO NATIVA. Registra un pago a proveedor y lo aplica a uno o varios documentos con saldo.

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

def TP(clave, nombre, lado, mod_op, recip, tipo_op, prefijo):
    return {"clave": clave, "nombre": nombre, "lado": lado, "modOp": mod_op, "recip": recip, "tipoOp": tipo_op, "prefijo": prefijo}

TIPOS = [
    TP("pago",  "Pago a proveedor", "P", 247, 2, 32, "PAG"),
]
TIPO_POR = {t["clave"]: t for t in TIPOS}

_modulos = ctx.query(
    "SELECT m.ModuleID, m.ModuleName, pv.Rec, pv.Af FROM engModule m JOIN ("
    "  SELECT ModuleID, MAX(CASE WHEN ParameterKey='DocRecipient' THEN TRY_CONVERT(int, Value) END) AS Rec, MAX(CASE WHEN ParameterKey='DocumentTypeID' THEN TRY_CONVERT(int, Value) END) AS Tipo, "
    "         MAX(CASE WHEN ParameterKey='FinancialAffectation' THEN TRY_CONVERT(int, Value) END) AS Af, MAX(CASE WHEN ParameterKey='TableName' THEN Value END) AS Tabla "
    "  FROM engModuleParameter WHERE ParameterKey IN ('DocRecipient','DocumentTypeID','FinancialAffectation','TableName') GROUP BY ModuleID"
    ") pv ON pv.ModuleID = ISNULL(NULLIF(m.ModuleIDBase,0), m.ModuleID) WHERE pv.Tabla = 'docDocument' AND pv.Rec IN (1,2) AND ((pv.Rec = 1 AND pv.Af = 1) OR (pv.Rec = 2 AND pv.Af = -1)) AND ISNULL(pv.Tipo,0) NOT IN (40,44)")
modulos_lado = {"C": [], "P": []}
nombre_mod = {}
for _r in _modulos:
    modulos_lado["C" if I(_r["Rec"]) == 1 else "P"].append(I(_r["ModuleID"]))
    nombre_mod[I(_r["ModuleID"])] = S(_r["ModuleName"])

monedas = [{"id": I(r["id"]), "simbolo": S(r["simbolo"]), "nombre": S(r["nombre"]), "letra": S(r["letra"]), "tc": D(r["tc"]) or 1.0} for r in ctx.query(
    "SELECT CurrencyID AS id, IntlSymbol AS simbolo, Currency AS nombre, ISNULL(MoneyLetter,'') AS letra, ISNULL(Rate,1) AS tc FROM vwLBSCurrencyList ORDER BY CurrencyID")]
moneda_por = {m["id"]: m for m in monedas}

def moneda_de(v):
    m = I(v)
    return 3 if m <= 0 else m

def simbolo_de(id_):
    return moneda_por[id_]["simbolo"] if id_ in moneda_por else "MXN"

def docs_con_saldo(lado):
    mods = modulos_lado[lado]
    if not mods:
        return []
    filas = ctx.query(
        "SELECT TOP 30000 d.DocumentID AS id, d.ModuleID AS modulo, d.BusinessEntityID AS ent, d.FolioPrefix, d.Folio, d.DateDocument AS fecha, ISNULL(d.Total,0) AS total, ISNULL(d.Balance,0) AS saldo, ISNULL(d.TotalPaid,0) AS pagado, ISNULL(d.Title,'') AS titulo, "
        "(SELECT MAX(a.DatePayment) FROM docDocumentPaymentAgenda a WHERE a.DocumentID = d.DocumentID AND a.DeletedOn IS NULL) AS vence, "
        "ISNULL((SELECT TOP 1 c.MetodoPago FROM docDocumentCFD c WHERE c.DocumentID = d.DocumentID), '') AS metodo, "
        "ISNULL(d.CurrencyID,0) AS moneda, ISNULL(d.Rate,1) AS tcDoc, "
        "(SELECT COUNT(DISTINCT a.PartialityNumber) FROM docDocumentPaymentAgenda a WHERE a.DocumentID = d.DocumentID AND a.DeletedOn IS NULL) AS nParc, "
        "(SELECT COUNT(*) FROM docDocumentPayment p WHERE p.DocumentID = d.DocumentID AND p.DeletedOn IS NULL AND ISNULL(p.PaymentWithDocumentID,0) = 0) AS nAplic, "
        "(SELECT COUNT(*) FROM docDocumentPayment p WHERE p.DocumentID = d.DocumentID AND p.DeletedOn IS NULL AND ISNULL(p.PaymentWithDocumentID,0) > 0) AS nNotas "
        "FROM docDocument d WHERE d.ModuleID IN (" + ",".join(str(m) for m in mods) + ") AND d.OwnedBusinessEntityID = " + str(empresa) +
        " AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND ISNULL(d.Balance,0) > 0.004 ORDER BY d.DateDocument")
    res = []
    for r in filas:
        mon = moneda_de(r["moneda"])
        saldo = D(r["saldo"])
        tcd = D(r["tcDoc"]) if D(r["tcDoc"]) > 0 else 1.0
        res.append({"id": I(r["id"]), "modulo": I(r["modulo"]), "tipo": nombre_mod.get(I(r["modulo"]), ""), "ent": I(r["ent"]), "folio": (S(r["FolioPrefix"]) + S(r["Folio"])).strip(),
                    "fecha": fecha_txt(r["fecha"]), "vence": fecha_txt(r["vence"]), "total": D(r["total"]), "saldo": saldo, "pagado": D(r["pagado"]), "titulo": S(r["titulo"]), "metodo": S(r["metodo"]),
                    "moneda": mon, "simbolo": simbolo_de(mon), "tcDoc": tcd, "saldoMx": saldo if mon == 3 else round(saldo * tcd, 2),
                    "nParc": I(r["nParc"]), "nAplic": I(r["nAplic"]), "nNotas": I(r["nNotas"])})
    return res

def parcialidades_de(doc, saldo):
    agenda = ctx.query("SELECT PartialityNumber AS n, MIN(DatePayment) AS vence, SUM(ISNULL(Amount,0)) AS importe FROM docDocumentPaymentAgenda WHERE DocumentID = " + str(doc) + " AND DeletedOn IS NULL GROUP BY PartialityNumber ORDER BY PartialityNumber")
    pagos = {}
    for r in ctx.query("SELECT CASE WHEN ISNULL(PartialityNumber,0) = 0 THEN 1 ELSE PartialityNumber END AS n, SUM(ISNULL(Amount,0)) AS pagado FROM docDocumentPayment WHERE DocumentID = " + str(doc) +
                       " AND DeletedOn IS NULL GROUP BY CASE WHEN ISNULL(PartialityNumber,0) = 0 THEN 1 ELSE PartialityNumber END"):
        pagos[I(r["n"])] = D(r["pagado"])
    if not agenda:
        pg = sum(pagos.values())
        return [{"n": 1, "vence": "", "importe": round(saldo + pg, 2), "pagado": round(pg, 2), "saldo": round(saldo, 2)}]
    res = []
    for a in agenda:
        n = I(a["n"])
        imp = D(a["importe"])
        pg = pagos.get(n, 0.0)
        res.append({"n": n, "vence": fecha_txt(a["vence"]), "importe": round(imp, 2), "pagado": round(pg, 2), "saldo": round(max(0.0, imp - pg), 2)})
    pend = sum(x["saldo"] for x in res)
    dif = round(saldo - pend, 2)
    if abs(dif) > 0.004:
        if dif > 0:
            res[-1]["saldo"] = round(res[-1]["saldo"] + dif, 2)
            res[-1]["importe"] = round(res[-1]["importe"] + dif, 2)
        else:
            sobra = -dif
            for x in reversed(res):
                if sobra <= 0.004:
                    break
                q = min(x["saldo"], sobra)
                x["saldo"] = round(x["saldo"] - q, 2)
                sobra = round(sobra - q, 2)
    return res

def detalle_doc(doc):
    d = ctx.query("SELECT d.DocumentID, d.ModuleID, d.FolioPrefix, d.Folio, ISNULL(d.Total,0) AS Total, ISNULL(d.Balance,0) AS Saldo, ISNULL(d.TotalPaid,0) AS Pagado, ISNULL(d.CurrencyID,0) AS Moneda, ISNULL(d.Rate,1) AS TC FROM docDocument d "
                  "WHERE d.DocumentID = " + str(doc) + " AND d.OwnedBusinessEntityID = " + str(empresa))
    if not d:
        return {}
    mon = moneda_de(d[0]["Moneda"])
    saldo = D(d[0]["Saldo"])
    res = {"id": doc, "folio": (S(d[0]["FolioPrefix"]) + S(d[0]["Folio"])).strip(), "moneda": mon, "simbolo": simbolo_de(mon), "total": D(d[0]["Total"]), "saldo": saldo, "pagado": D(d[0]["Pagado"]), "tcDoc": D(d[0]["TC"]),
           "parc": parcialidades_de(doc, saldo)}
    aplic = []
    for r in ctx.query("SELECT p.DocumentPaymentID AS id, p.DateOperation, ISNULL(p.Amount,0) AS Amount, ISNULL(p.Rate,1) AS Rate, ISNULL(p.PartialityNumber,0) AS Parc, ISNULL(p.FinancialOperationID,0) AS Op, ISNULL(p.PaymentWithDocumentID,0) AS ConDoc, "
                       "o.ModuleID AS OpMod, o.FolioPrefix AS OpPre, o.Folio AS OpFol, ISNULL(o.CurrencyID,0) AS OpMon, ISNULL(o.Amount,0) AS OpAmt, nc.ModuleID AS NcMod, nc.FolioPrefix AS NcPre, nc.Folio AS NcFol, ISNULL(m.ModuleName,'') AS NcNombre "
                       "FROM docDocumentPayment p LEFT JOIN docFinancialOperation o ON o.FinancialOperationID = p.FinancialOperationID LEFT JOIN docDocument nc ON nc.DocumentID = p.PaymentWithDocumentID LEFT JOIN engModule m ON m.ModuleID = nc.ModuleID "
                       "WHERE p.DocumentID = " + str(doc) + " AND p.DeletedOn IS NULL ORDER BY p.DateOperation, p.DocumentPaymentID"):
        nota = I(r["ConDoc"]) > 0
        op_mod = I(r["OpMod"]) if r["OpMod"] is not None else 0
        tipo = "nota" if nota else "cobro" if op_mod == 248 else "pago" if op_mod == 247 else "otro"
        folio = (S(r["NcNombre"]) + " " + (S(r["NcPre"]) + S(r["NcFol"])).strip()).strip() if nota else (S(r["OpPre"]) + "-" + S(r["OpFol"])).strip("- ")
        aplic.append({"fecha": fecha_txt(r["DateOperation"]), "tipo": tipo, "folio": folio, "monto": round(D(r["Amount"]), 2), "tc": D(r["Rate"]), "parc": I(r["Parc"]) or 1,
                      "monedaOp": mon if r["OpMod"] is None else moneda_de(r["OpMon"]), "montoOp": 0.0 if r["OpMod"] is None else round(D(r["OpAmt"]), 2)})
    res["aplic"] = aplic
    return res

def movimientos_de(entidad, clave):
    if clave not in TIPO_POR or entidad <= 0:
        return []
    return [{"id": I(r["id"]), "folio": (S(r["FolioPrefix"]) + "-" + S(r["Folio"])).strip(), "fecha": fecha_txt(r["DateOperation"]), "monto": D(r["Monto"]), "cuenta": S(r["Cuenta"]), "docs": I(r["Docs"])} for r in ctx.query(
        "SELECT TOP 8 o.FinancialOperationID AS id, o.FolioPrefix, o.Folio, o.DateOperation, ISNULL(o.Amount,0) AS Monto, ISNULL(f.FinancialEntityName,'') AS Cuenta, "
        "(SELECT COUNT(*) FROM docDocumentPayment p WHERE p.FinancialOperationID = o.FinancialOperationID AND p.DeletedOn IS NULL) AS Docs FROM docFinancialOperation o LEFT JOIN orgFinancialEntity f ON f.FinancialEntityID = o.FinancialEntityID "
        "WHERE o.BusinessEntityID = " + str(entidad) + " AND o.ModuleID = " + str(TIPO_POR[clave]["modOp"]) + " AND o.CancelledOn IS NULL AND o.DeletedOn IS NULL ORDER BY o.DateOperation DESC, o.FinancialOperationID DESC")]

def inteligencia_pago(entidad, clave):
    res = {}
    if clave not in TIPO_POR or entidad <= 0:
        return res
    t = TIPO_POR[clave]
    mods = modulos_lado[t["lado"]]
    por_mes = {}
    for r in ctx.query("SELECT CONVERT(CHAR(7), o.DateOperation, 120) AS mes, SUM(ISNULL(o.Amount,0)) AS total, COUNT(*) AS n FROM docFinancialOperation o WHERE o.BusinessEntityID = " + str(entidad) + " AND o.ModuleID = " + str(t["modOp"]) +
                       " AND o.CancelledOn IS NULL AND o.DeletedOn IS NULL AND o.DateOperation >= DATEADD(MONTH, -11, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)) GROUP BY CONVERT(CHAR(7), o.DateOperation, 120)"):
        por_mes[S(r["mes"])] = (D(r["total"]), D(r["n"]))
    meses = []
    total12 = 0.0
    mov12 = 0
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
        mov12 += int(v[1])
    res["meses"] = meses
    res["total12"] = round(total12, 2)
    res["mov12"] = mov12
    if mods:
        e = ctx.query("SELECT COUNT(*) AS n, AVG(CAST(DATEDIFF(DAY, d.DateDocument, p.DateOperation) AS float)) AS dias, AVG(CAST(DATEDIFF(DAY, ISNULL(ag.due, d.DateDocument), p.DateOperation) AS float)) AS atraso, "
                      "SUM(CASE WHEN p.DateOperation <= ISNULL(ag.due, d.DateDocument) THEN 1.0 ELSE 0 END) AS aTiempo FROM docDocumentPayment p JOIN docDocument d ON d.DocumentID = p.DocumentID "
                      "OUTER APPLY (SELECT MAX(a.DatePayment) AS due FROM docDocumentPaymentAgenda a WHERE a.DocumentID = d.DocumentID AND a.DeletedOn IS NULL) ag "
                      "WHERE d.BusinessEntityID = " + str(entidad) + " AND d.OwnedBusinessEntityID = " + str(empresa) + " AND d.ModuleID IN (" + ",".join(str(m) for m in mods) + ") AND p.DeletedOn IS NULL")
        n = I(e[0]["n"]) if e else 0
        res["pagos"] = n
        res["dias"] = round(D(e[0]["dias"]), 1) if n > 0 and e[0]["dias"] is not None else None
        res["atraso"] = round(D(e[0]["atraso"]), 1) if n > 0 and e[0]["atraso"] is not None else None
        res["puntual"] = round(D(e[0]["aTiempo"]) * 100.0 / n) if n > 0 else None
    return res

def catalogos():
    cat = {}

    def ent(tabla):
        return ("SELECT be.BusinessEntityID AS id, ISNULL(be.CommercialName, be.OfficialName) AS nombre, ISNULL(mi.OfficialNumber,'') AS rfc, ISNULL(x.CreditLimit,0) AS credito, "
                "CONVERT(VARCHAR(10), u.DateOperation, 23) AS ultFecha, ISNULL(u.Amount,0) AS ultMonto FROM " + tabla + " x "
                "JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID "
                "OUTER APPLY (SELECT TOP 1 o.DateOperation, o.Amount FROM docFinancialOperation o WHERE o.BusinessEntityID = be.BusinessEntityID AND o.ModuleID IN (247,248) AND o.CancelledOn IS NULL AND o.DeletedOn IS NULL ORDER BY o.DateOperation DESC, o.FinancialOperationID DESC) u "
                "WHERE be.DeletedOn IS NULL AND x.DeletedOn IS NULL ORDER BY nombre")

    def lista(tabla):
        return [{"id": I(r["id"]), "nombre": S(r["nombre"]), "rfc": S(r["rfc"]), "credito": D(r["credito"]), "ultFecha": S(r["ultFecha"]), "ultMonto": D(r["ultMonto"])} for r in ctx.query(ent(tabla))]

    ver_c = any(t["lado"] == "C" for t in TIPOS)
    ver_p = any(t["lado"] == "P" for t in TIPOS)
    cat["clientes"] = lista("orgCustomer") if ver_c else []
    cat["proveedores"] = lista("orgSupplier") if ver_p else []
    cat["cuentas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "def": I(r["def"]) == 1, "moneda": moneda_de(r["moneda"])} for r in ctx.query(
        "SELECT FinancialEntityID AS id, FinancialEntityName AS nombre, ISNULL(IsDefault,0) AS def, ISNULL(CurrencyID,0) AS moneda FROM orgFinancialEntity WHERE DeletedOn IS NULL ORDER BY ISNULL(IsDefault,0) DESC, FinancialEntityName")]
    cat["monedas"] = monedas
    cat["formas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query("SELECT ID AS id, Value AS nombre FROM vwcboCFDPaymentmethod ORDER BY CboOrder")]

    cat["folios"] = {t["clave"]: I(ctx.scalar("SELECT ISNULL(MAX(TRY_CONVERT(BIGINT, Folio)),0) + 1 FROM docFinancialOperation WHERE ModuleID = " + str(t["modOp"]) + " AND FolioPrefix = N'" + t["prefijo"] + "'")) for t in TIPOS}

    cat["docsC"] = docs_con_saldo("C") if ver_c else []
    cat["docsP"] = docs_con_saldo("P") if ver_p else []
    return cat

LUNI = ["", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE"]
LDEC = ["", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"]
LVEI = ["VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"]
LCEN = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"]

def letra_centenas(n):
    if n == 0:
        return ""
    if n == 100:
        return "CIEN"
    r = ""
    c, d = n // 100, n % 100
    if c > 0:
        r += LCEN[c] + " "
    if d > 0:
        if d <= 20:
            r += LUNI[d]
        else:
            a, u = d // 10, d % 10
            if a == 2 and u > 0:
                r += LVEI[u - 1]
            else:
                r += LDEC[a]
                if u > 0:
                    r += " Y " + LUNI[u]
    return r.strip()

def letra_en(n):
    if n == 0:
        return "CERO"
    r = ""
    mill = n // 1000000
    n %= 1000000
    mil = n // 1000
    n %= 1000
    if mill > 0:
        r += "UN MILLÓN " if mill == 1 else letra_centenas(mill) + " MILLONES "
    if mil > 0:
        r += "MIL " if mil == 1 else letra_centenas(mil) + " MIL "
    if n > 0:
        r += letra_centenas(n)
    r = r.strip()
    if r.endswith("UNO"):
        r = r[:-3] + "UN"
    return r

def letra_importe(v, moneda):
    v = max(0.0, v)
    e = int(v)
    c = int(round((v - e) * 100))
    if c == 100:
        e += 1
        c = 0
    pal = moneda_por[moneda]["letra"].upper() if moneda in moneda_por and moneda_por[moneda]["letra"] else "PESOS"
    return letra_en(e) + " " + pal + " " + ("%02d" % c) + "/100" + (" M.N." if moneda == 3 else "")

def aplicar(spec):
    clave = S(spec.get("tipo"))
    if clave not in TIPO_POR:
        raise Exception("Tipo desconocido: " + clave)
    t = TIPO_POR[clave]
    lado = t["lado"]
    entidad = I(spec.get("entidad"))
    cuenta = I(spec.get("cuenta"))
    forma = I(spec.get("forma"))
    if entidad <= 0:
        raise Exception("Elige el " + ("cliente" if lado == "C" else "proveedor") + ".")
    if cuenta <= 0:
        raise Exception("Elige la cuenta bancaria o caja " + ("donde entra" if lado == "C" else "de donde sale") + " el dinero.")
    if forma <= 0:
        raise Exception("Elige la forma de pago.")
    aps = [a for a in (spec.get("aplicaciones") or []) if D(a.get("monto")) > 0]
    if not aps:
        raise Exception("Marca al menos un documento y captura cuánto aplicar.")
    if len(set(I(a.get("doc")) for a in aps)) != len(aps):
        raise Exception("Un documento no puede aparecer dos veces en el mismo movimiento.")
    fecha = S(spec.get("fecha"))
    if len(fecha) < 10:
        raise Exception("Captura la fecha.")
    f8 = Sq(fecha[:10].replace("-", ""))
    tracking = S(spec.get("referencia"))
    tc = D(spec.get("tc"))
    mod_op, recip, tipo_op, pref = t["modOp"], t["recip"], t["tipoOp"], t["prefijo"]
    coef = 1 if lado == "C" else -1
    cu = ctx.query("SELECT ISNULL(CurrencyID,0) AS Moneda FROM orgFinancialEntity WHERE FinancialEntityID = " + str(cuenta) + " AND DeletedOn IS NULL")
    if not cu:
        raise Exception("La cuenta elegida no existe.")
    m_cuenta = moneda_de(cu[0]["Moneda"])
    persona = S(ctx.scalar("SELECT ISNULL(CommercialName, OfficialName) FROM orgBusinessEntity WHERE BusinessEntityID = " + str(entidad)))
    uid = str(ctx.user_id)

    lineas = []
    por_doc = []
    total_cuenta = 0.0
    foraneas = set()
    for a in aps:
        doc = I(a.get("doc"))
        monto = round(D(a.get("monto")), 2)
        parc_pedida = I(a.get("parcialidad"))
        d = ctx.query("SELECT Total, Balance, TotalPaid, BusinessEntityID, ISNULL(CurrencyID,0) AS Moneda, ModuleID, FolioPrefix, Folio FROM docDocument WHERE DocumentID=" + str(doc) +
                      " AND DeletedOn IS NULL AND CancelledOn IS NULL AND OwnedBusinessEntityID = " + str(empresa))
        etiqueta = "documento " + str(doc)
        if not d:
            raise Exception("No se pudo aplicar a " + etiqueta + ": el documento no existe o está cancelado.")
        x = d[0]
        if I(x["ModuleID"]) in nombre_mod:
            etiqueta = nombre_mod[I(x["ModuleID"])] + " " + (S(x["FolioPrefix"]) + S(x["Folio"])).strip()
        if I(x["ModuleID"]) not in modulos_lado[lado]:
            raise Exception("No se pudo aplicar a " + etiqueta + ": no es un documento que se " + ("cobre" if lado == "C" else "pague") + ".")
        if I(x["BusinessEntityID"]) != entidad:
            raise Exception("No se pudo aplicar a " + etiqueta + ": pertenece a otro " + ("cliente" if lado == "C" else "proveedor") + ".")
        total, saldo, pagado = D(x["Total"]), D(x["Balance"]), D(x["TotalPaid"])
        m_doc = moneda_de(x["Moneda"])
        if total <= 0:
            raise Exception("No se pudo aplicar a " + etiqueta + ": tiene total cero.")
        if m_cuenta == m_doc:
            modo = "igual"
        elif m_cuenta == 3:
            modo = "cuentaPesos"
        elif m_doc == 3:
            modo = "cuentaExtranjera"
        else:
            raise Exception("No se pudo aplicar a " + etiqueta + ": la cuenta está en " + simbolo_de(m_cuenta) + " y el documento en " + simbolo_de(m_doc) + ". Esa combinación solo se puede registrar en la pantalla nativa de Tesorería.")
        if m_cuenta != 3:
            foraneas.add(m_cuenta)
        if m_doc != 3:
            foraneas.add(m_doc)

        if modo == "igual":
            doc_amt = monto
            rate_linea = 1.0 if m_doc == 3 else tc
            if m_doc != 3 and not tc > 0:
                raise Exception("Captura el tipo de cambio de " + simbolo_de(m_doc) + ".")
        elif modo == "cuentaPesos":
            if not tc > 0:
                raise Exception("Captura el tipo de cambio: " + etiqueta + " está en " + simbolo_de(m_doc) + " y la cuenta en pesos.")
            doc_amt = monto / tc
            rate_linea = tc
        else:
            if not tc > 0:
                raise Exception("Captura el tipo de cambio: la cuenta está en " + simbolo_de(m_cuenta) + " y " + etiqueta + " en pesos.")
            doc_amt = monto * tc
            rate_linea = 1.0
        doc_amt = round(doc_amt, 2)
        if abs(doc_amt - saldo) <= 0.011:
            doc_amt = saldo
        if doc_amt > saldo + 0.005:
            raise Exception("No se pudo aplicar a " + etiqueta + ": el importe (" + "{:,.2f}".format(doc_amt) + " " + simbolo_de(m_doc) + ") es mayor que su saldo (" + "{:,.2f}".format(saldo) + " " + simbolo_de(m_doc) + ").")

        parc = parcialidades_de(doc, saldo)
        reparto = []
        resto = doc_amt
        if parc_pedida > 0:
            pp = next((p for p in parc if p["n"] == parc_pedida), None)
            if pp is None:
                raise Exception("No se pudo aplicar a " + etiqueta + ": no tiene la parcialidad " + str(parc_pedida) + ".")
            if doc_amt > pp["saldo"] + 0.011:
                raise Exception("No se pudo aplicar a " + etiqueta + ": la parcialidad " + str(parc_pedida) + " solo tiene pendiente " + "{:,.2f}".format(pp["saldo"]) + " " + simbolo_de(m_doc) + ".")
            reparto.append((parc_pedida, doc_amt))
        else:
            for p in parc:
                if resto <= 0.0049:
                    break
                s = p["saldo"]
                if s <= 0.0049:
                    continue
                q = min(s, resto)
                reparto.append((p["n"], round(q, 2)))
                resto = round(resto - q, 2)
            if resto > 0.0049:
                if reparto:
                    reparto[-1] = (reparto[-1][0], round(reparto[-1][1] + resto, 2))
                else:
                    reparto.append((1, resto))
        corriente = saldo
        for n_parc, m_linea in reparto:
            sa = corriente
            si = round(corriente - m_linea, 2)
            corriente = si
            lineas.append({"doc": doc, "parc": n_parc, "monto": m_linea, "rate": rate_linea, "sa": sa, "si": si})
        total_cuenta += monto
        por_doc.append({"doc": doc, "etiqueta": etiqueta, "total": total, "saldo": saldo, "pagado": pagado, "doc_amt": doc_amt, "nuevo": round(saldo - doc_amt, 2), "monto": monto, "m_doc": m_doc, "reparto": reparto})
    if len(foraneas) > 1:
        raise Exception("Los documentos están en monedas extranjeras distintas (" + ", ".join(simbolo_de(m) for m in sorted(foraneas)) + "): regístralos por separado.")
    rate_op = 1.0 if m_cuenta == 3 else tc
    total_cuenta = round(total_cuenta, 2)

    sb = []
    sb.append("DECLARE @out TABLE(FinancialOperationID BIGINT); DECLARE @outPay TABLE(DocumentPaymentID BIGINT);\nBEGIN TRY BEGIN TRAN;\n")
    sb.append("DECLARE @lk INT; EXEC @lk = sp_getapplock @Resource = 'BrosCobroFolio_" + str(mod_op) + "_" + pref + "', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;\n")
    sb.append("IF @lk < 0 THROW 50001, 'No se pudo obtener el candado del folio (otro cobro/pago en curso).', 1;\n")
    for pd in por_doc:
        sb.append("IF (SELECT ISNULL(Balance,0) FROM docDocument WHERE DocumentID=" + str(pd["doc"]) + ") < " + Num(pd["doc_amt"] - 0.011) + " THROW 50002, 'El saldo de un documento cambió mientras se capturaba.', 1;\n")
    sb.append("DECLARE @folio BIGINT = ISNULL((SELECT MAX(TRY_CONVERT(BIGINT, Folio)) FROM docFinancialOperation WHERE ModuleID=" + str(mod_op) + " AND FolioPrefix=N'" + pref + "'),0)+1;\n")
    sb.append("DECLARE @f DATETIME = '" + f8 + " 12:00:00'; DECLARE @opId BIGINT, @payId BIGINT;\n")
    sb.append("INSERT INTO docFinancialOperation (ModuleID, DocRecipientID, DocumentTypeID, OwnedBusinessEntityID, BusinessEntityID, DateOperation, DateAffectation, FinancialEntityID, DebitCreditCoef, Amount, CurrencyID, FinancialEntityAmount, Rate, AmountRate, PaymentMethodID, "
              "Description, PartialityNumber, PartialityTotal, DocumentID, Reference, TotalLetter, RecipientName, ExportID, VersionComplemento, FolioPrefix, Folio, CreatedOn, CreatedBy) OUTPUT INSERTED.FinancialOperationID INTO @out ")
    sb.append("VALUES (" + str(mod_op) + "," + str(recip) + "," + str(tipo_op) + "," + str(empresa) + "," + str(entidad) + ",@f,@f," + str(cuenta) + "," + str(coef) + "," + Num(total_cuenta) + "," + str(m_cuenta) + "," + Num(coef * total_cuenta) + "," + Num(rate_op) + "," +
              Num(total_cuenta * rate_op) + "," + str(forma) + "," + "N'" + Sq(("Cobro Cliente " if lado == "C" else "Pago Proveedor ") + persona) + "',0,0,0," + ("NULL" if tracking == "" else "N'" + Sq(tracking) + "'") + ",N'" + Sq(letra_importe(total_cuenta, m_cuenta)) + "',N'" + Sq(persona) + "'," +
              ("1" if lado == "C" else "0") + ",N'2.0',N'" + pref + "',CONVERT(NVARCHAR(50),@folio),GETDATE()," + uid + ");\n")
    sb.append("SELECT @opId = FinancialOperationID FROM @out;\n")
    for ln in lineas:
        m, r = ln["monto"], ln["rate"]
        sb.append("INSERT INTO docDocumentPayment (DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber, SaldoAnterior, SaldoInsoluto) OUTPUT INSERTED.DocumentPaymentID INTO @outPay VALUES (" +
                  str(ln["doc"]) + ",@opId,@f," + Num(m) + "," + Num(r) + "," + Num(m * r) + "," + str(ln["parc"]) + "," + Num(ln["sa"]) + "," + Num(ln["si"]) + ");\nSELECT @payId = DocumentPaymentID FROM @outPay; DELETE FROM @outPay;\n")

        sb.append("INSERT INTO docDocumentPaymentEspejo (DocumentPaymentID, DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber) VALUES (@payId," + str(ln["doc"]) + ",@opId,@f," + Num(m) + "," + Num(r) + "," + Num(m * r) + "," + str(ln["parc"]) + ");\n")
    if forma != 1:
        sb.append("INSERT INTO docBankTransfer (FinancialOperationID, FinancialEntityID, TrackingNumber, CreatedOn, CreatedBy) VALUES (@opId," + str(cuenta) + "," + ("NULL" if tracking == "" else "N'" + Sq(tracking) + "'") + ",GETDATE()," + uid + ");\n")
    for pd in por_doc:
        doc = pd["doc"]
        prop = pd["doc_amt"] / pd["total"]

        for tx in ctx.query("SELECT DocumentTaxDetailID, DocumentItemID, TaxTypeID, Amount, TaxBase, TaxPerc, TaxName, TaxTypeName FROM docDocumentTaxDetail WHERE DocumentID=" + str(doc)):
            item = "NULL" if tx["DocumentItemID"] is None else str(I(tx["DocumentItemID"]))
            sb.append("INSERT INTO docFinancialOperationTaxDetail (DocumentTaxDetailID, FinancialOperationID, DocumentID, DocumentItemID, Proporcion, Amount, TaxTypeID, TaxName, TaxTypeName, TaxBase, TaxPerc) VALUES (" +
                      str(I(tx["DocumentTaxDetailID"])) + ",@opId," + str(doc) + "," + item + "," + Num(prop) + "," + Num(D(tx["Amount"]) * prop) + "," + str(I(tx["TaxTypeID"])) +
                      ",N'" + Sq(tx["TaxName"]) + "',N'" + Sq(tx["TaxTypeName"]) + "'," + Num(D(tx["TaxBase"]) * prop) + "," + Num(D(tx["TaxPerc"])) + ");\n")
        sb.append("UPDATE docDocument SET TotalPaid=" + Num(pd["pagado"] + pd["doc_amt"]) + ", Balance=" + Num(pd["nuevo"]) + ", StatusPaidID=" + ("1" if pd["nuevo"] <= 0.0049 else "2") + " WHERE DocumentID=" + str(doc) + ";\n")
    sb.append("COMMIT TRAN;\nEND TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRAN; THROW; END CATCH;")
    ctx.execute("".join(sb))
    folio = ctx.scalar("SELECT TOP 1 Folio FROM docFinancialOperation WHERE ModuleID=" + str(mod_op) + " AND FolioPrefix=N'" + pref + "' AND BusinessEntityID=" + str(entidad) + " ORDER BY FinancialOperationID DESC")
    resumen = []
    for pd in por_doc:
        reparto = " + ".join("parc. " + str(k) for k, _v in pd["reparto"])
        conv = " (" + "{:,.2f}".format(pd["doc_amt"]) + " " + simbolo_de(pd["m_doc"]) + ")" if pd["m_doc"] != m_cuenta else ""
        resumen.append(pd["etiqueta"] + " · " + "{:,.2f}".format(pd["monto"]) + " " + simbolo_de(m_cuenta) + conv + " · " + reparto + (" · liquidado" if pd["nuevo"] <= 0.0049 else " · queda " + "{:,.2f}".format(pd["nuevo"]) + " " + simbolo_de(pd["m_doc"])))
    return t["nombre"] + " " + pref + "-" + S(folio) + " registrado: " + "{:,.2f}".format(total_cuenta) + " " + simbolo_de(m_cuenta) + ((" a tipo de cambio " + ("%g" % tc)) if foraneas else "") + "\n" + "\n".join(resumen)

_modo_prueba = os.environ.get("BROSLMV_PAGO_TEST")
if _modo_prueba:
    _spec = json.loads(_modo_prueba)
    if _spec.get("catalogo"):
        _res = json.dumps(catalogos(), ensure_ascii=False, default=str)
    elif _spec.get("movimientos"):
        _res = json.dumps(movimientos_de(I(_spec["entidad"]), S(_spec["tipo"])), ensure_ascii=False, default=str)
    elif _spec.get("docs"):
        _res = json.dumps(docs_con_saldo(S(_spec["lado"])), ensure_ascii=False, default=str)
    elif _spec.get("detalle"):
        _res = json.dumps(detalle_doc(I(_spec["doc"])), ensure_ascii=False, default=str)
    elif _spec.get("inteligencia"):
        _res = json.dumps(inteligencia_pago(I(_spec["entidad"]), S(_spec["tipo"])), ensure_ascii=False, default=str)
    else:
        try:
            _res = "OK " + aplicar(_spec)
        except Exception as _ex:
            _res = "ERROR " + S(_ex)
    _salida = os.environ.get("BROSLMV_PAGO_OUT")
    if _salida:
        with open(_salida, "w", encoding="utf-8") as _f:
            _f.write(_res)
    result = _res

import http.server
import secrets
import time
import urllib.parse

def js_json(o):
    """JSON seguro para incrustarlo en una página o en un fragmento de JavaScript."""
    return json.dumps(o, ensure_ascii=False, default=str).replace("</", "<" + chr(92) + "/")

def ventana_en_vivo(armar_pagina, despachar, titulo, ancho, alto):
    """armar_pagina(http) → texto HTML de la página (http = {"url", "token"}); despachar(peticion, estado) → fragmento de JavaScript (o "").
    Sale cuando la página lo pide (estado["fin"] = True) o cuando la ventana deja de latir."""
    token = secrets.token_hex(16)
    estado = {"fin": False, "visto": False, "ultimo": time.time()}

    class _Manejador(http.server.BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def _cors(self):
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "*")
            self.send_header("Access-Control-Allow-Private-Network", "true")

        def do_OPTIONS(self):
            self.send_response(204)
            self._cors()
            self.end_headers()

        def do_POST(self):
            if urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get("t", [""])[0] != token:
                self.send_response(403)
                self._cors()
                self.end_headers()
                return
            n = int(self.headers.get("Content-Length") or 0)
            cuerpo = self.rfile.read(n).decode("utf-8") if n else "{}"
            estado["visto"] = True
            estado["ultimo"] = time.time()
            try:
                js = despachar(json.loads(cuerpo), estado)
            except Exception as ex:
                js = "aviso(" + js_json(S(ex)) + ",'mal')"
            estado["ultimo"] = time.time()
            datos = (js or "").encode("utf-8")
            self.send_response(200)
            self._cors()
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(datos)))
            self.end_headers()
            self.wfile.write(datos)

    class _Servidor(http.server.HTTPServer):
        def handle_error(self, request, client_address):
            pass

    servidor = _Servidor(("127.0.0.1", 0), _Manejador)
    servidor.timeout = 1.0
    url = "http://127.0.0.1:%d/api" % servidor.server_address[1]
    ctx.show_html(armar_pagina({"url": url, "token": token}), titulo, ancho, alto, False)
    inicio = time.time()
    try:
        while not estado["fin"]:
            servidor.handle_request()
            ahora = time.time()
            if not estado["visto"] and ahora - inicio > 90:
                break
            if estado["visto"] and ahora - estado["ultimo"] > 10:
                break
    finally:
        servidor.server_close()
    return estado

PAGINA = r'''<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'><title>Cobro o pago</title><style>
:root{--marino:#15324F;--marino2:#1d4468;--azul:#2D6FE0;--acc:#2D6FE0;--accsuave:#E8F0FF;--texto:#16263A;--suave:#64748B;--linea:#D8E0EB;--fondo:#EEF2F7;--tarjeta:#fff;--rojo:#C82828;--ambar:#B45309;--verde:#16803B;--zebra:#F8FAFC}
body.pago{--acc:#0F766E;--accsuave:#E3F5F2}
*{box-sizing:border-box}html,body{height:100%}body{margin:0;font:13px 'Segoe UI',Arial,sans-serif;background:var(--fondo);color:var(--texto);display:flex;flex-direction:column;overflow:hidden}
button,input,select,textarea{font:inherit;color:var(--texto)}
.cinta{background:linear-gradient(180deg,var(--marino2),var(--marino));color:#fff;padding:10px 18px 12px;display:flex;gap:18px;align-items:stretch;flex-wrap:nowrap;box-shadow:0 2px 8px #0003;z-index:5}
.marca{display:flex;flex-direction:column;justify-content:center;min-width:0;flex:1;overflow:hidden}.marca small{color:#93C5FD;font-weight:700;letter-spacing:.14em;font-size:10px}.marca b{font-size:19px;font-weight:650;margin-top:1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.marca span{color:#B6C7DA;font-size:11.5px;margin-top:2px}
.acciones{display:flex;gap:6px;align-items:stretch}.ab{border:0;background:transparent;color:#E8EEF6;border-radius:9px;padding:6px 12px;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;min-width:92px;gap:2px}
.ab i{font-style:normal;font-size:22px;line-height:1.1}.ab span{font-size:11px;line-height:1.15;text-align:center}.ab em{font-style:normal;color:#93A9C2;font-size:10px}.ab:hover{background:#ffffff1f}.ab:disabled{opacity:.45;cursor:default}
.ab.p{background:var(--acc);color:#fff}.ab.p:hover{filter:brightness(1.1);background:var(--acc)}.ab.p em{color:#ffffffb0}.sep{width:1px;background:#ffffff30;margin:6px 4px}
.info{margin-left:auto;display:grid;grid-template-columns:repeat(3,auto);gap:4px 14px;align-items:center;border:1px solid #ffffff2c;border-radius:10px;padding:8px 14px;background:#ffffff10}
.info label{display:block;font-size:10px;color:#9FB4CC;letter-spacing:.08em;text-transform:uppercase;margin:0 0 2px}.info input,.info select{background:#fff;border:1px solid #fff;border-radius:6px;padding:4px 7px;width:140px}.info .fol{font-size:15px;font-weight:650;color:#fff}.info .fol small{display:block;font-weight:400;font-size:10px;color:#9FB4CC}
.tipos{background:#fff;border-bottom:1px solid var(--linea);padding:8px 18px;display:flex;gap:6px;align-items:center;flex-wrap:wrap}
.tipo{border:1px solid var(--linea);background:#fff;border-radius:999px;padding:5px 15px 5px 10px;cursor:pointer;display:flex;gap:6px;align-items:center}.tipo:hover{border-color:var(--acc)}.tipo.on{color:#fff;font-weight:600}.tipo.c.on{background:#2D6FE0;border-color:#2D6FE0}.tipo.p.on{background:#0F766E;border-color:#0F766E}
.cuerpo{flex:1;min-height:0;display:flex;gap:14px;padding:14px 18px}
.izq{flex:1;min-width:0;overflow:auto;padding-right:6px}.der{width:340px;flex:0 0 340px;overflow:auto;display:flex;flex-direction:column;gap:12px}
.card{background:var(--tarjeta);border:1px solid var(--linea);border-radius:12px;padding:12px 14px;margin-bottom:12px;box-shadow:0 1px 2px #1b2a3d0d}
.card h2{margin:0 0 9px;font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:var(--suave);display:flex;align-items:center;gap:8px}
.card h2 b{display:inline-flex;width:20px;height:20px;border-radius:50%;background:var(--acc);color:#fff;align-items:center;justify-content:center;font-size:11px;letter-spacing:0}.card h2 .der2{margin-left:auto;text-transform:none;letter-spacing:0;font-weight:400}
label{display:block;color:var(--suave);font-size:12px;margin-bottom:3px}
input,select,textarea{border:1px solid var(--linea);border-radius:7px;padding:6px 9px;background:#fff;width:100%}input:focus,select:focus{outline:2px solid var(--acc);outline-offset:-1px;border-color:var(--acc)}input.mal,select.mal{border-color:var(--rojo);outline:2px solid #C8282833}
.fila{display:grid;gap:10px 14px}.f2{grid-template-columns:2fr 1fr}.f3{grid-template-columns:repeat(3,1fr)}
.combo{position:relative}.lista{position:absolute;left:0;right:0;top:100%;z-index:30;background:#fff;border:1px solid var(--linea);border-radius:9px;max-height:300px;overflow:auto;box-shadow:0 10px 28px #0003;display:none;margin-top:3px}
.lista div{padding:7px 10px;cursor:pointer;display:flex;justify-content:space-between;gap:12px;align-items:center;border-bottom:1px solid #f0f3f8}.lista div:last-child{border:0}.lista div:hover,.lista div.sel{background:var(--accsuave)}.lista b{font-weight:600}.lista small{color:var(--suave);white-space:nowrap}.lista .vacio{cursor:default;color:var(--suave);justify-content:center}
.chip{display:inline-block;border-radius:999px;padding:1px 8px;font-size:11px;background:#E5EAF1;color:var(--suave);white-space:nowrap}.chip.r{background:#FDE4E4;color:var(--rojo)}.chip.a{background:#FDF0DC;color:var(--ambar)}.chip.v{background:#E0F3E6;color:var(--verde)}.chip.b{background:var(--accsuave);color:var(--acc)}
.ent{display:none;margin-top:10px;border:1px solid var(--linea);border-radius:10px;padding:9px 12px;background:var(--zebra);gap:8px 22px;grid-template-columns:repeat(4,auto);justify-content:start}.ent div b{display:block;font-size:10px;text-transform:uppercase;letter-spacing:.07em;color:var(--suave);font-weight:600}.ent div span{font-size:13.5px;font-variant-numeric:tabular-nums}
.edades{display:none;margin-top:8px;gap:6px;flex-wrap:wrap}.edad{border:1px solid var(--linea);border-radius:9px;padding:4px 10px;background:#fff;min-width:96px}.edad b{display:block;font-size:10px;text-transform:uppercase;letter-spacing:.06em;color:var(--suave);font-weight:600}.edad span{font-variant-numeric:tabular-nums}.edad.r span{color:var(--rojo)}
.herr{display:flex;gap:6px;flex-wrap:wrap;align-items:center;margin-bottom:9px}.herr button{border:1px solid var(--linea);background:#fff;border-radius:8px;padding:5px 11px;cursor:pointer}.herr button:hover{border-color:var(--acc);color:var(--acc)}.herr .esp{flex:1}.herr input{width:130px;text-align:right}.herr .p{background:var(--acc);border-color:var(--acc);color:#fff}.herr .p:hover{color:#fff;filter:brightness(1.08)}
.tw{overflow:auto;border:1px solid var(--linea);border-radius:10px}table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}
th{background:var(--zebra);font-size:10.5px;text-transform:uppercase;letter-spacing:.06em;color:var(--suave);text-align:right;padding:7px 8px;border-bottom:1px solid var(--linea);position:sticky;top:0;z-index:1;white-space:nowrap}th.l,td.l{text-align:left}
td{padding:5px 8px;text-align:right;border-bottom:1px solid #eef2f7;vertical-align:middle}tr:last-child td{border:0}tr.on td{background:var(--accsuave)}tr.clic{cursor:pointer}td input[type=number]{padding:4px 6px;text-align:right;width:116px}td input[type=checkbox]{width:auto}td small{color:var(--suave);display:block;font-size:11px}
.vacioP{padding:26px;text-align:center;color:var(--suave)}.vacioP b{display:block;font-size:15px;color:var(--texto);margin-bottom:3px}
.res{background:linear-gradient(180deg,#fff,#F5F9FF)}.tot{display:grid;grid-template-columns:1fr auto;gap:5px 10px;font-variant-numeric:tabular-nums}.tot span:nth-child(even){text-align:right}.tot .g{font-size:22px;font-weight:700;color:var(--acc);border-top:1px solid var(--linea);padding-top:7px;margin-top:3px}
.hist{display:flex;flex-direction:column;gap:3px}.hist div{display:flex;justify-content:space-between;gap:8px;padding:5px 7px;border-radius:7px}.hist small{color:var(--suave)}
.aviso{border-radius:9px;padding:7px 10px;margin-top:8px;font-size:12px}.aviso.a{background:#FFF7E6;color:#7A4B06;border:1px solid #F0D9AD}.aviso.r{background:#FDECEC;color:#B42318;border:1px solid #F4C4C4}.aviso.v{background:#EAF7EE;color:#166534;border:1px solid #BFE3CB}
.pie{background:#fff;border-top:1px solid var(--linea);padding:6px 18px;color:var(--suave);font-size:11.5px;display:flex;gap:18px;flex-wrap:wrap}.pie b{color:var(--texto);font-weight:600}
.toast{position:fixed;left:50%;bottom:44px;transform:translateX(-50%);background:#16263A;color:#fff;padding:9px 16px;border-radius:10px;box-shadow:0 8px 24px #0005;z-index:60;display:none;max-width:70vw}.toast.mal{background:#B42318}.toast.bien{background:#166534}
.kbd{border:1px solid var(--linea);border-bottom-width:2px;border-radius:5px;padding:0 5px;font-size:10.5px;color:var(--suave);background:#fff}
.vel{position:fixed;inset:0;background:#0f1e32aa;z-index:70;display:none;align-items:center;justify-content:center}.vel.on{display:flex}.modal{background:#fff;border-radius:14px;box-shadow:0 20px 60px #0006;width:min(560px,92vw);padding:20px 22px}.modal h3{margin:0 0 4px;font-size:18px;color:var(--acc)}.modal pre{background:var(--zebra);border:1px solid var(--linea);border-radius:9px;padding:10px 12px;font:12.5px 'Consolas','Segoe UI',monospace;white-space:pre-wrap;margin:10px 0}.modal .bt{display:flex;gap:8px;justify-content:flex-end}.modal button{border:1px solid var(--linea);background:#fff;border-radius:8px;padding:7px 16px;cursor:pointer}.modal button.p{background:var(--acc);border-color:var(--acc);color:#fff}
b.ver{cursor:pointer;border-bottom:1px dotted var(--suave)}b.ver:hover{color:var(--acc)}tr.nc td{opacity:.55}tr.det td{background:#2D6FE00d}.pc{margin-top:3px;display:flex;gap:4px;flex-wrap:wrap}td select{padding:4px 6px;width:auto;max-width:158px}td.l .chip{vertical-align:1px}
.kpi3{display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-bottom:6px}.kpi3 b{display:block;font-size:10px;text-transform:uppercase;letter-spacing:.06em;color:var(--suave);font-weight:600}.kpi3 span{font-variant-numeric:tabular-nums}
.mini2 th{position:static;padding:4px 6px}.mini2 td{padding:3px 6px;font-size:12px}.ap{display:flex;gap:6px;align-items:baseline;flex-wrap:wrap;padding:5px 0;border-bottom:1px solid #eef2f7}.ap:last-child{border:0}.ap .im{margin-left:auto;font-variant-numeric:tabular-nums;font-weight:600}.cx{cursor:pointer;color:var(--suave);font-size:14px}.cx:hover{color:var(--rojo)}.sm{color:var(--suave);font-size:11.5px}
@media (max-width:1180px){.cinta{flex-wrap:wrap}}@media (max-width:1100px){.cuerpo{flex-direction:column;overflow:auto}.der{width:auto;flex:none}.izq{overflow:visible}}
.cinta{flex-wrap:wrap;row-gap:8px}.marca{min-width:230px;flex:1 1 230px}.ab{min-width:80px}.ab.s{min-width:60px;padding:6px 6px}.info input,.info select{width:128px}
.ab.s{min-width:64px;padding:6px 8px}.ab.s i{font-size:18px}.ab.s span{font-size:10px}
.ov{position:fixed;inset:0;background:#0b1220a8;z-index:80;display:none;align-items:flex-start;justify-content:center;padding-top:9vh}.ov.on{display:flex}
.palc{width:min(640px,92vw);background:var(--tarjeta);border:1px solid var(--linea);border-radius:14px;box-shadow:0 24px 60px #0008;overflow:hidden}
.palc input{border:0;border-bottom:1px solid var(--linea);border-radius:0;padding:14px 16px;font-size:15px;background:transparent}.palc input:focus{outline:0}
.pall{max-height:52vh;overflow:auto}.pall div{padding:9px 16px;display:flex;justify-content:space-between;gap:14px;cursor:pointer;border-bottom:1px solid var(--zebra)}.pall div.sel,.pall div:hover{background:var(--accsuave)}.pall small{color:var(--suave)}.pall .gr{cursor:default;font-size:10px;text-transform:uppercase;letter-spacing:.1em;color:var(--suave);background:var(--zebra);padding:5px 16px}
.palc .pie2{padding:7px 16px;color:var(--suave);font-size:11px;background:var(--zebra);display:flex;gap:14px}
.caja{width:min(760px,94vw);background:var(--tarjeta);border:1px solid var(--linea);border-radius:14px;box-shadow:0 24px 60px #0008;padding:16px 18px}.caja h3{margin:0 0 8px;font-size:15px}.caja textarea{height:210px;font:12px Consolas,monospace}
.caja .bt{display:flex;gap:8px;justify-content:flex-end;margin-top:10px}.btn{border:1px solid var(--linea);background:var(--tarjeta);border-radius:8px;padding:6px 14px;cursor:pointer}.btn.p{background:var(--acc);border-color:var(--acc);color:#fff}
.hoja{background:#fff;color:#16263A;width:min(860px,96vw);max-height:86vh;overflow:auto;border-radius:6px;box-shadow:0 24px 60px #0009;padding:34px 40px}.hoja h1{margin:0;font-size:22px}.hoja table{margin-top:14px}.hoja th{position:static;background:#F1F5F9;color:#334155}.hoja td,.hoja th{padding:6px 8px;border-bottom:1px solid #E2E8F0}
.hoja .tt{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;border-bottom:2px solid #16263A;padding-bottom:10px}.hoja .bloque{display:grid;grid-template-columns:1fr 1fr;gap:6px 24px;margin-top:12px;font-size:12.5px}.hoja .tot2{margin-left:auto;margin-top:12px;width:280px;font-variant-numeric:tabular-nums}.hoja .tot2 div{display:flex;justify-content:space-between;padding:2px 0}.hoja .tot2 .g{font-size:17px;font-weight:700;border-top:2px solid #16263A;margin-top:4px;padding-top:5px}.hoja .letra{margin-top:10px;font-style:italic;color:#475569;font-size:12px}
.ban{display:none;margin:0 0 12px;border:1px solid #F0D9AD;background:#FFF7E6;color:#7A4B06;border-radius:10px;padding:8px 12px;gap:10px;align-items:center}.ban.on{display:flex}.ban span{flex:1}
.kpi{display:grid;grid-template-columns:repeat(2,1fr);gap:6px 12px;margin:8px 0}.kpi div b{display:block;font-size:10px;text-transform:uppercase;letter-spacing:.07em;color:var(--suave);font-weight:600}.kpi div span{font-size:15px;font-weight:650;font-variant-numeric:tabular-nums}
.top{display:flex;flex-direction:column;gap:2px;margin-top:6px}.top a{display:flex;justify-content:space-between;gap:8px;padding:5px 7px;border-radius:7px;align-items:center}.top a:hover{background:var(--accsuave)}.top small{color:var(--suave);display:block}.top button{border:1px solid var(--linea);background:var(--tarjeta);border-radius:6px;cursor:pointer;width:26px;height:26px;color:var(--acc);font-weight:700}
.rev{display:flex;flex-direction:column;gap:3px;font-size:12px}.rev div{display:flex;gap:7px;align-items:flex-start}.rev i{font-style:normal;font-weight:700;width:14px;text-align:center}.rev .v i{color:var(--verde)}.rev .a i{color:var(--ambar)}.rev .r i{color:var(--rojo)}
.mg{display:flex;align-items:baseline;justify-content:space-between}.mg b{font-size:20px;font-variant-numeric:tabular-nums}
.txsm{color:var(--suave);font-size:11.5px}
body.oscuro{--fondo:#0E1620;--tarjeta:#162231;--texto:#E5ECF5;--suave:#93A5BC;--linea:#2B3C52;--zebra:#1A2A3C;--accsuave:#1E3552;--marino:#0B1626;--marino2:#12263F}
body.oscuro .tipos,body.oscuro .pie,body.oscuro input,body.oscuro select,body.oscuro textarea,body.oscuro .lista,body.oscuro .tipo,body.oscuro .kbd,body.oscuro .res{background:var(--tarjeta);color:var(--texto)}
body.oscuro .res{background:linear-gradient(180deg,#162231,#1A2A3C)}body.oscuro .chip{background:#223349;color:#B8C7DA}body.oscuro .info input,body.oscuro .info select{background:#1B2B3F;border-color:#2B3C52;color:#E5ECF5}
body.oscuro .tipo.on{color:#fff}body.oscuro .aviso.a{background:#3a2c10;color:#f3d08a;border-color:#5b4416}body.oscuro .aviso.r{background:#3d1717;color:#f5b5b5;border-color:#6b2a2a}
@media print{body>*:not(#ovPrev){display:none!important}#ovPrev{display:block!important;position:static;background:#fff;padding:0}.hoja{box-shadow:none;max-height:none;width:100%}.noprint{display:none!important}}
.herr select{width:auto;padding:5px 8px;border-radius:8px}
.edad{background:var(--tarjeta)}body.oscuro .edad,body.oscuro .herr button,body.oscuro .modal,body.oscuro .edad{background:var(--tarjeta);color:var(--texto)}
.donut{display:flex;gap:14px;align-items:center}.donut svg{flex:0 0 112px}.leyenda{display:flex;flex-direction:column;gap:3px;font-size:11.5px;flex:1}.leyenda div{display:flex;justify-content:space-between;gap:8px}.leyenda i{display:inline-block;width:9px;height:9px;border-radius:3px;margin-right:6px}
.deud{display:flex;flex-direction:column;gap:5px;margin-top:8px}.deud a{display:block;cursor:pointer;border-radius:7px;padding:3px 5px}.deud a:hover{background:var(--accsuave)}.deud .fl{display:flex;justify-content:space-between;gap:8px}.deud .bar{height:6px;border-radius:4px;background:var(--linea);margin-top:3px;overflow:hidden}.deud .bar i{display:block;height:100%;background:var(--acc)}
tr.on td .mini{display:block;height:4px;border-radius:3px;background:var(--linea);margin-top:3px;overflow:hidden}tr.on td .mini i{display:block;height:100%;background:var(--acc)}
</style></head><body>
<div class='cinta'>
 <div class='marca'><small>BROSLMV · TESORERÍA</small><b id='ttl'>Cobro a cliente</b><span id='sub'></span></div>
 <div class='acciones'>
  <button class='ab p' id='bGuardar' onclick='aplicar(false)'><i>✅</i><span id='bGTxt'>Registrar</span><em>F5</em></button>
  <button class='ab' id='bNuevo' onclick='aplicar(true)'><i>➕</i><span>Registrar y nuevo</span><em>F6</em></button>
  <div class='sep'></div>
  <button class='ab' onclick='limpiar()'><i>🧹</i><span>Limpiar</span><em>&nbsp;</em></button>
  <button class='ab' onclick='cancelar()'><i>✕</i><span>Cancelar</span><em>Esc</em></button>
  <div class='sep'></div>
  <button class='ab s' onclick='abrirPaleta()' title='Paleta de comandos (Ctrl+K)'><i>🔎</i><span>Paleta</span><em>Ctrl+K</em></button>
  <button class='ab s' onclick='vistaPrevia()' title='Recibo o comprobante imprimible (Ctrl+P)'><i>🖨️</i><span>Vista previa</span><em>Ctrl+P</em></button>
  <button class='ab s' onclick='copiarDocs()' title='Copia los documentos con saldo para pegarlos en Excel'><i>📋</i><span>Copiar</span><em>a Excel</em></button>
  <button class='ab s' onclick='alternarTema()' title='Tema claro u oscuro'><i>🌓</i><span>Tema</span><em>&nbsp;</em></button>
 </div>
 <div class='info'>
  <div><label>Fecha</label><input type='date' id='fecha'></div>
  <div><label>Folio</label><div class='fol' id='folio'>—<small>lo asigna el sistema</small></div></div>
  <div><label id='lblCta'>Cuenta</label><select id='cta'></select></div>
 </div>
</div>
<div class='tipos' id='tipos'></div>
<div class='cuerpo'>
 <div class='izq'>
  <div class='card'><h2><b>1</b><span id='lblEnt'>Cliente</span><span class='der2'><span class='kbd'>F2</span> buscar</span></h2>
   <div class='combo'><input id='ent' placeholder='Escribe nombre o RFC…' autocomplete='off'><div class='lista' id='lstEnt'></div></div>
   <div class='ent' id='entCard'></div><div class='edades' id='edades'></div>
  </div>
  <div class='card'><h2><b>2</b>Datos del movimiento</h2>
   <div class='fila f3'>
    <div><label>Forma de pago</label><select id='forma'></select></div>
    <div><label>Referencia o número de rastreo (opcional)</label><input id='ref' maxlength='60' placeholder='SPEI, cheque, folio de depósito…'></div>
    <div><label id='lblMonto'>Monto recibido (opcional)</label><input type='number' step='any' min='0' id='monto' placeholder='Para repartirlo solo'></div>
   </div>
   <div class='fila f3' id='filaMon' style='display:none;margin-top:10px'>
    <div><label>Moneda de la cuenta</label><div id='monCta' style='padding:6px 0'></div></div>
    <div><label id='lblTc'>Tipo de cambio</label><input type='number' step='any' min='0' id='tc'></div>
    <div><label>&nbsp;</label><div id='tcAyuda' class='sm'></div></div>
   </div>
   <div style='color:var(--suave);font-size:12px;margin-top:7px'>Si capturas el monto, «Distribuir» lo reparte entre los documentos con la estrategia que elijas (por omisión, <b>más antiguos primero</b>); también puedes marcar y ajustar cada uno a mano.</div>
  </div>
  <div class='card'><h2><b>3</b>Documentos con saldo<span class='der2' id='docsEstado'></span></h2>
   <div class='herr'><button onclick='marcarTodos()'>Marcar todos</button><button onclick='marcarVencidos()'>Marcar vencidos</button><button onclick='quitarMarcas()'>Quitar marcas</button><span class='esp'></span><select id='estrat' title='Cómo se reparte el monto entre los documentos'><option value='antiguos'>Más antiguos primero</option><option value='vencidos'>Solo vencidos</option><option value='mayor'>Mayor saldo primero</option><option value='menor'>Menor saldo primero (liquida más documentos)</option><option value='prop'>Proporcional al saldo</option></select><button class='p' onclick='distribuir()'>Distribuir el monto</button></div>
   <div class='tw'><table><thead><tr><th></th><th class='l'>Documento</th><th>Fecha</th><th>Vence</th><th>Estado</th><th>Total</th><th>Saldo</th><th>Parcialidad</th><th>Aplicar en <span id='thMon'>MXN</span></th></tr></thead><tbody id='tb'></tbody></table></div>
  </div>
 </div>
 <div class='der'>
  <div class='card res'><h2>Resumen</h2>
   <div class='tot'><span>Documentos marcados</span><span id='tN'>0</span><span>Saldo de la persona (MXN)</span><span id='tSal'>0.00</span><span>Quedaría</span><span id='tQ'>0.00</span><span id='lblMon2'>Monto recibido</span><span id='tMon'>—</span><span id='lVP' style='display:none'>Valor en pesos</span><span id='tVP' style='display:none'>—</span><span class='g'>Total a aplicar</span><span class='g' id='tTot'>0.00</span></div>
   <div id='avisosRes'></div>
  </div>
  <div class='card' id='cardDet' style='display:none'><h2><span id='hDet'>Documento</span><span class='der2'><span class='cx' onclick='cerrarDet()' title='Cerrar el detalle'>✕</span></span></h2><div id='det'></div></div>
  <div class='card' id='cardRev'><h2>Revisión previa <span class='der2' id='revSub'></span></h2><div class='rev' id='revis'></div></div>
  <div class='card' id='cardComp' style='display:none'><h2>Comportamiento de pago <span class='der2 txsm' id='compSub'></span></h2><div id='comp'></div></div>
  <div class='card' id='cardPron'><h2><span id='pronTit'>Vencimientos</span><span class='der2 txsm'>próximas 8 semanas</span></h2><div id='pron'></div></div>
  <div class='card' id='cardCart'><h2><span id='cartTit'>Cartera</span><span class='der2 txsm' id='cartSub'></span></h2><div id='cart'></div></div>
  <div class='card' id='cardMov' style='display:none'><h2 id='hMov'>Últimos movimientos</h2><div class='hist' id='hist'></div></div>
  <div class='card' style='color:var(--suave);font-size:12px'><b style='color:var(--texto)'>Cómo funciona</b><br>Se registra <b>una sola operación (un folio)</b> con un renglón por documento y parcialidad, su espejo, la transferencia bancaria y el reparto de impuestos, todo en una transacción. Con moneda extranjera captura el <b>tipo de cambio</b>: lo que escribes en «Aplicar» es siempre en la moneda de la cuenta y la ventana te dice cuánto baja el saldo de cada documento en su propia moneda. <b>Plantilla avanzada, no nativa:</b> no genera la póliza contable (la hace el Motor de Asientos al contabilizar); pruébala primero en una base de pruebas.<br><br><span class='kbd'>F2</span> persona · <span class='kbd'>F5</span> registrar · <span class='kbd'>F6</span> registrar y nuevo · <span class='kbd'>Esc</span> cancelar</div>
 </div>
</div>
<div class='pie'><span>Elaboró: <b id='pUsr'>—</b></span><span id='pEmp'></span><span id='pMod'></span><span id='bdr' style='margin-left:auto'></span></div>
<div class='ov' id='ovPal'><div class='palc'><input id='palq' placeholder='Acción, persona o folio de un documento…  (Esc cierra)' autocomplete='off'><div class='pall' id='pall'></div><div class='pie2'><span>↑↓ elegir</span><span>Enter ejecuta</span><span>Esc cierra</span></div></div></div>
<div class='ov' id='ovPrev' style='padding-top:4vh'><div style='display:flex;flex-direction:column;gap:8px;align-items:center'><div class='noprint' style='display:flex;gap:8px'><button class='btn p' onclick='window.print()'>Imprimir</button><button class='btn' onclick='cerrarOv("ovPrev")'>Cerrar</button></div><div class='hoja' id='hoja'></div></div></div>
<div class='ban' id='banBorr'></div>
<div class='toast' id='toast'></div>
<div class='vel' id='vel'><div class='modal'><h3 id='mTit'>Registrado</h3><div id='mSub' style='color:var(--suave)'></div><pre id='mPre'></pre><div class='bt'><button onclick='cerrarVentana()'>Cerrar ventana</button><button class='p' onclick='otroMas()'>Registrar otro</button></div></div></div>
<script>
var DATOS=__DATOS__;
function enviar(o){if(DATOS.http){fetch(DATOS.http.url+'?t='+DATOS.http.token,{method:'POST',body:JSON.stringify(o)}).then(function(r){return r.text();}).then(function(t){if(t)(0,eval)(t);}).catch(function(){});}else window.chrome.webview.postMessage(JSON.stringify(o));}
if(DATOS.http){enviar({accion:'latido'});setInterval(function(){enviar({accion:'latido'});},3000);}
function esc(s){return String(s==null?'':s).replace(/[&<>']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;'}[c];});}
function f2(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:2});}
function $(i){return document.getElementById(i);}
function norm(s){return String(s||'').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g,'');}
function fmtF(s){if(!s)return '';var p=s.split('-');return p[2]+'/'+p[1]+'/'+p[0];}
function dias(a,b){return Math.round((Date.parse(b)-Date.parse(a))/864e5);}
var CAT=DATOS.cat,TIPOS=DATOS.tipos,VIVO=!!DATOS.vivo;
var ST={tipo:TIPOS[0].clave,ent:null,sel:{},parc:{},det:null,guardando:false};
function tipoAct(){return TIPOS.filter(function(t){return t.clave===ST.tipo;})[0];}
function docsLado(){return tipoAct().lado==='C'?CAT.docsC:CAT.docsP;}
function ENT(){return tipoAct().lado==='C'?CAT.clientes:CAT.proveedores;}
var MON={};CAT.monedas.forEach(function(m){MON[m.id]=m;});var DET={};
function simb(id){return (MON[id]||{simbolo:'MXN'}).simbolo;}
function r2(x){return Math.round(x*100)/100;}
function f4(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:4});}
function ctaAct(){var v=+$('cta').value;return CAT.cuentas.filter(function(c){return c.id===v;})[0];}
function mCta(){var c=ctaAct();return c?c.moneda:3;}
function tcAct(){return +$('tc').value||0;}
function compat(d){var mc=mCta();return d.moneda===mc||mc===3||d.moneda===3;}
function aDoc(d,m){var mc=mCta();if(d.moneda===mc)return m;if(mc===3)return tcAct()>0?m/tcAct():0;return m*tcAct();}
function a2(d,s){var mc=mCta();if(d.moneda===mc)return r2(s);if(mc===3)return r2(s*tcAct());var v=tcAct()>0?s/tcAct():0;return Math.floor(v*100+1e-9)/100;}
function sc(d){return a2(d,d.saldo);}
function valMx(d,da){return d.moneda===3?da:da*d.tcDoc;}
function monedaFx(){var mc=mCta();if(mc!==3)return mc;for(var k in ST.sel){var d=docPor(+k);if(d&&d.moneda!==3)return d.moneda;}var l=docsDeEnt().filter(function(d){return d.moneda!==3;})[0];return l?l.moneda:null;}
function pintarMon(){var mc=mCta(),fx=monedaFx(),t=tipoAct(),sy=simb(mc);
  $('thMon').textContent=sy;$('lblMonto').textContent=(t.lado==='C'?'Monto recibido':'Monto a pagar')+(mc!==3?' ('+sy+')':'')+' (opcional)';$('lblMon2').textContent=(t.lado==='C'?'Monto recibido':'Monto a pagar')+(mc!==3?' ('+sy+')':'');
  var f=$('filaMon');if(fx===null){f.style.display='none';return;}f.style.display='';
  $('monCta').innerHTML='<span class=\'chip b\'>'+esc(sy)+'</span> '+esc((MON[mc]||{nombre:'Peso'}).nombre);
  if(ST.tcMon!==fx){ST.tcMon=fx;$('tc').value=MON[fx]?Math.round(MON[fx].tc*10000)/10000:1;}
  $('lblTc').textContent='Tipo de cambio ('+simb(fx)+' → MXN)';
  $('tcAyuda').textContent=mc===3?'Los documentos en '+simb(fx)+' se cobran/pagan en pesos: lo que captures se divide entre este tipo de cambio para saber cuánto baja su saldo en '+simb(fx)+'.'
    :'La cuenta está en '+simb(mc)+': los documentos en '+simb(mc)+' bajan 1 a 1 (el tipo de cambio fija su valor en pesos) y los documentos en pesos bajan al multiplicar por este tipo de cambio.';}
function cambioCuenta(){var mc=mCta();if(mc!==ST.mcPrev){ST.mcPrev=mc;ST.tcMon=undefined;for(var k in ST.sel){var d=docPor(+k);if(d&&compat(d))ST.sel[k]=sc(d);}}
  for(var k2 in ST.sel){var d2=docPor(+k2);if(!d2||!compat(d2)){delete ST.sel[k2];delete ST.parc[k2];}else if(ST.sel[k2]>sc(d2))ST.sel[k2]=sc(d2);}
  pintarMon();pintarDocs();}
function leyenda(d){var m=ST.sel[d.id]||0,da=r2(aDoc(d,m));if(Math.abs(da-d.saldo)<=0.011)da=d.saldo;var q=r2(d.saldo-da);
  return (d.moneda!==mCta()?'= '+f2(da)+' '+esc(d.simbolo)+' · ':'')+(q<=0.004?'<b style=\'color:var(--verde)\'>liquida el documento</b>':'queda '+f2(q)+' '+esc(d.simbolo));}
function pagoChips(d){var t=tipoAct(),h='';
  if(d.nAplic+d.nNotas===0&&d.pagado<=0.004)h+='<span class=chip>sin pagos aplicados</span>';else h+='<span class=\'chip b\'>pagado '+f2(d.pagado)+' ('+Math.round(d.pagado/(d.total||1)*100)+' %)</span>';
  if(d.nAplic>0)h+='<span class=\'chip v\'>'+d.nAplic+(t.lado==='C'?' cobro':' pago')+(d.nAplic>1?'s':'')+'</span>';
  if(d.nNotas>0)h+='<span class=\'chip a\'>'+d.nNotas+' nota'+(d.nNotas>1?'s':'')+' de crédito</span>';return h;}
function selParc(d,on){if(d.nParc<2)return '<small>'+(d.nParc===1?'1 de 1':'—')+'</small>';var ps=(DET[d.id]||{}).parc,ns=[],i;
  if(ps)ps.forEach(function(p){ns.push(p.n);});else for(i=1;i<=d.nParc;i++)ns.push(i);
  var o='<option value=0>Automático</option>'+ns.map(function(n){var p=ps?ps.filter(function(x){return x.n===n;})[0]:null;return '<option value='+n+(ST.parc[d.id]===n?' selected':'')+'>Parc. '+n+(p?' · '+(p.saldo>0.004?'debe '+f2(p.saldo):'liquidada'):'')+'</option>';}).join('');
  return '<select '+(on?'':'disabled ')+'onchange=\'parc('+d.id+',this.value)\'>'+o+'</select>';}
function parc(id,v){v=+v;var d=docPor(id);if(v>0)ST.parc[id]=v;else delete ST.parc[id];
  var p=v>0&&DET[id]?DET[id].parc.filter(function(x){return x.n===v;})[0]:null;if(p&&ST.sel[id]!==undefined)ST.sel[id]=Math.min(sc(d),a2(d,p.saldo));pintarDocs();}
function ver(id){ST.det=id;pintarDetalle();if(!DET[id])cargarDet(id);pintarDocs();}
function cerrarDet(){ST.det=null;pintarDetalle();pintarDocs();}
function cargarDet(id){if(!VIVO)return;llamar('detalle',{doc:id}).then(function(r){DET[id]=r;if(ST.det===id)pintarDetalle();var d=docPor(id);if(d&&d.nParc>1)pintarDocs();}).catch(function(){});}
var TA={cobro:['Cobro','v'],pago:['Pago','v'],nota:['Nota','a'],otro:['Otro','']};
function pintarDetalle(){var c=$('cardDet'),id=ST.det,d=id?docPor(id):null;if(!d){c.style.display='none';return;}c.style.display='';$('hDet').textContent=d.tipo+' '+d.folio;var r=DET[id],hoy=$('fecha').value;
  var h='<div class=kpi3><div><b>Total</b><span>'+f2(d.total)+' '+esc(d.simbolo)+'</span></div><div><b>Pagado</b><span>'+f2(d.pagado)+'</span></div><div><b>Saldo</b><span>'+f2(d.saldo)+'</span></div></div>';
  if(d.moneda!==3)h+='<div class=sm>Documento en '+esc(d.simbolo)+' · tipo de cambio del documento '+f4(d.tcDoc)+' · saldo ≈ '+f2(d.saldoMx)+' MXN</div>';
  if(!r){$('det').innerHTML=h+'<div class=sm style=\'margin-top:8px\'>'+(VIVO?'Consultando parcialidades y pagos…':'El detalle completo requiere la conexión en vivo.')+'</div>';return;}
  h+='<div class=sm style=\'margin-top:9px\'><b>Parcialidades</b></div><div class=tw style=\'margin-top:3px\'><table class=mini2><thead><tr><th>N.º</th><th>Vence</th><th>Importe</th><th>Pagado</th><th>Saldo</th></tr></thead><tbody>'+r.parc.map(function(p){
    var e=p.saldo<=0.004?'<span class=\'chip v\'>liquidada</span>':'<b'+(p.vence&&p.vence<hoy?' style=\'color:var(--rojo)\' title=\'vencida\'':'')+'>'+f2(p.saldo)+'</b>';
    return '<tr><td>'+p.n+'</td><td>'+(p.vence?fmtF(p.vence):'—')+'</td><td>'+f2(p.importe)+'</td><td>'+f2(p.pagado)+'</td><td>'+e+'</td></tr>';}).join('')+'</tbody></table></div>';
  h+='<div class=sm style=\'margin-top:9px\'><b>Aplicaciones</b> (cobros, pagos y notas de crédito)</div>';
  h+=r.aplic.length?r.aplic.map(function(a){var ta=TA[a.tipo]||TA.otro;return '<div class=ap><span class=sm>'+fmtF(a.fecha)+'</span><span class=\'chip '+ta[1]+'\'>'+ta[0]+'</span><span>'+esc(a.folio)+'</span><span class=sm>parc. '+a.parc+(r.moneda!==3&&a.tc>0&&a.tipo!=='nota'?' · TC '+f4(a.tc):'')+'</span><span class=im>'+f2(a.monto)+' '+esc(r.simbolo)+'</span></div>';}).join(''):'<div class=sm style=\'padding:4px 0\'>Sin cobros, pagos ni notas de crédito aplicados todavía.</div>';
  $('det').innerHTML=h;}
var _req=0,_pend={};
function llamar(accion,datos){return new Promise(function(ok,mal){if(!VIVO){mal(new Error('sin conexión en vivo'));return;}var id=++_req;_pend[id]=ok;enviar(Object.assign({accion:accion,req:id},datos||{}));setTimeout(function(){if(_pend[id]){delete _pend[id];mal(new Error('tiempo agotado'));}},15000);});}
function respuesta(id,datos){var f=_pend[id];if(f){delete _pend[id];f(datos);}}
var _t=0;function aviso(m,tipo){var t=$('toast');t.textContent=m;t.className='toast '+(tipo||'');t.style.display='block';clearTimeout(_t);_t=setTimeout(function(){t.style.display='none';},tipo==='mal'?8000:3500);}
function stats(){var hoy=$('fecha').value,m={};docsLado().forEach(function(d){var e=m[d.ent]=m[d.ent]||{pend:0,venc:0,n:0,b:[0,0,0,0,0]};e.pend+=d.saldoMx;e.n++;var dv=d.vence?dias(d.vence,hoy):-1;if(dv>0)e.venc+=d.saldoMx;var i=dv<=0?0:dv<=30?1:dv<=60?2:dv<=90?3:4;e.b[i]+=d.saldoMx;});return m;}
var ES={};
function combo(inp,lst,fuente,fila,elegir,limite,abrirAlFocus){
  var sel=0,vis=[];
  function pintar(){var q=norm(inp.value).split(/\s+/).filter(Boolean);var base=fuente();
    vis=(q.length?base.filter(function(x){var h=norm(fila(x).buscar);return q.every(function(t){return h.indexOf(t)>=0;});}):base).slice(0,limite||40);
    if(sel>=vis.length)sel=Math.max(0,vis.length-1);
    lst.innerHTML=vis.length?vis.map(function(x,i){var e=fila(x);return '<div data-i=\''+i+'\' class=\''+(i===sel?'sel':'')+'\'><span><b>'+esc(e.a)+'</b>'+(e.b?' <small>'+esc(e.b)+'</small>':'')+'</span><small>'+(e.c||'')+'</small></div>';}).join(''):'<div class=vacio>Sin resultados</div>';lst.style.display='block';}
  inp.addEventListener('input',function(){sel=0;pintar();});inp.addEventListener('focus',function(){inp.select();if(abrirAlFocus)pintar();});
  inp.addEventListener('keydown',function(ev){if(ev.key==='ArrowDown'){sel=Math.min(vis.length-1,sel+1);pintar();ev.preventDefault();}else if(ev.key==='ArrowUp'){sel=Math.max(0,sel-1);pintar();ev.preventDefault();}
    else if(ev.key==='Enter'){var x=vis[sel];if(x){elegir(x);lst.style.display='none';}ev.preventDefault();ev.stopPropagation();}else if(ev.key==='Escape'){lst.style.display='none';ev.stopPropagation();}});
  lst.addEventListener('mousedown',function(ev){var d=ev.target.closest('div[data-i]');if(d){elegir(vis[+d.getAttribute('data-i')]);lst.style.display='none';ev.preventDefault();}});
  inp.addEventListener('blur',function(){setTimeout(function(){lst.style.display='none';},130);});
}
combo($('ent'),$('lstEnt'),function(){return ENT().slice().sort(function(a,b){return ((ES[b.id]||{}).pend||0)-((ES[a.id]||{}).pend||0)||(a.nombre<b.nombre?-1:1);});},
  function(x){var e=ES[x.id];return {a:x.nombre,b:x.rfc,c:e?'debe '+f2(e.pend)+' · '+e.n+' doc.':'sin saldo',buscar:x.nombre+' '+x.rfc+' '+x.id};},elegirEnt,60,true);
$('ent').addEventListener('input',function(){if(ST.ent){ST.ent=null;ST.sel={};ST.parc={};pintarEnt();pintarDocs();}});
function pintarTipos(){$('tipos').style.display=TIPOS.length>1?'':'none';$('tipos').innerHTML=TIPOS.map(function(t){return '<button class=\'tipo '+(t.lado==='C'?'c':'p')+(t.clave===ST.tipo?' on':'')+'\' onclick=\'setTipo(&quot;'+t.clave+'&quot;)\'><i style=font-style:normal>'+(t.lado==='C'?'💰':'💸')+'</i>'+esc(t.nombre)+'</button>';}).join('');}
function setTipo(c){var antes=tipoAct().lado;ST.tipo=c;var t=tipoAct();if(t.lado!==antes){ST.ent=null;$('ent').value='';}ST.sel={};ST.parc={};
  document.body.classList.remove('cobro','pago');document.body.classList.add(t.lado==='C'?'cobro':'pago');$('ttl').textContent=t.nombre;$('sub').textContent=t.lado==='C'?'Cuentas por cobrar · entra dinero':'Cuentas por pagar · sale dinero';
  $('lblEnt').textContent=t.lado==='C'?'Cliente':'Proveedor';$('lblCta').textContent=t.lado==='C'?'Entra a la cuenta':'Sale de la cuenta';$('lblMonto').textContent=t.lado==='C'?'Monto recibido (opcional)':'Monto a pagar (opcional)';$('lblMon2').textContent=t.lado==='C'?'Monto recibido':'Monto a pagar';
  $('bGTxt').textContent=t.lado==='C'?'Registrar cobro':'Registrar pago';$('pMod').innerHTML='Operación <b>'+t.modOp+'</b> · folio '+esc(t.prefijo)+'-n';
  var f=CAT.folios&&CAT.folios[t.clave];$('folio').innerHTML=(f?esc(t.prefijo)+'-'+f:'—')+'<small>lo asigna el sistema</small>';
  ES=stats();pintarTipos();pintarEnt();pintarDocs();}
function elegirEnt(x){ST.ent=x;ST.sel={};ST.parc={};$('ent').value=x.nombre;$('ent').classList.remove('mal');pintarEnt();pintarDocs();cargarMov();$('monto').focus();}
function pintarEnt(){var x=ST.ent,c=$('entCard'),ed=$('edades'),t=tipoAct();
  if(!x){c.style.display='none';ed.style.display='none';$('cardMov').style.display='none';return;}
  var e=ES[x.id]||{pend:0,venc:0,n:0,b:[0,0,0,0,0]};c.style.display='grid';
  c.innerHTML='<div><b>RFC</b><span>'+esc(x.rfc||'—')+'</span></div><div><b>Saldo pendiente (MXN)</b><span>'+f2(e.pend)+'</span></div><div><b>Vencido</b><span style=\'color:'+(e.venc>0?'var(--rojo)':'inherit')+'\'>'+f2(e.venc)+'</span></div><div><b>Documentos</b><span>'+e.n+'</span></div>'
   +(x.credito>0?'<div><b>Límite de crédito</b><span>'+f2(x.credito)+'</span></div><div><b>Crédito disponible</b><span>'+f2(x.credito-e.pend)+'</span></div>':'')+'<div><b>'+(t.lado==='C'?'Último cobro':'Último pago')+'</b><span>'+(x.ultFecha?fmtF(x.ultFecha)+' · '+f2(x.ultMonto):'—')+'</span></div>';
  var nom=['Vigente','1-30 días','31-60 días','61-90 días','Más de 90'];ed.style.display='flex';ed.innerHTML=e.b.map(function(v,i){return '<div class=\'edad '+(i>0&&v>0?'r':'')+'\'><b>'+nom[i]+'</b><span>'+f2(v)+'</span></div>';}).join('');}
function cargarMov(){var x=ST.ent;if(!x||!VIVO)return;var t=tipoAct();llamar('movimientos',{entidad:x.id,tipo:ST.tipo}).then(function(h){if(!ST.ent||ST.ent.id!==x.id)return;$('cardMov').style.display=h.length?'':'none';$('hMov').textContent=t.lado==='C'?'Últimos cobros':'Últimos pagos';
  $('hist').innerHTML=h.map(function(m){return '<div><span><b>'+esc(m.folio)+'</b> <small>· '+fmtF(m.fecha)+(m.cuenta?' · '+esc(m.cuenta):'')+' · '+m.docs+' doc.</small></span><span>'+f2(m.monto)+'</span></div>';}).join('');}).catch(function(){});}
function docsDeEnt(){return ST.ent?docsLado().filter(function(d){return d.ent===ST.ent.id;}):[];}
function estadoDoc(d){var hoy=$('fecha').value;if(!d.vence)return '<span class=chip>sin vencimiento</span>';var dv=dias(d.vence,hoy);return dv>0?'<span class=\'chip r\'>vencido '+dv+' d</span>':dv>=-7?'<span class=\'chip a\'>vence en '+(-dv)+' d</span>':'<span class=\'chip v\'>vigente</span>';}
function pintarDocs(){var ds=docsDeEnt(),t=tipoAct();pintarMon();
  if(!ST.ent){$('tb').innerHTML='<tr><td colspan=9><div class=vacioP><b>Elige '+(t.lado==='C'?'un cliente':'un proveedor')+'</b>Escribe su nombre o RFC (F2): verás sus documentos con saldo para cobrar o pagar.</div></td></tr>';$('docsEstado').textContent='';totales();return;}
  if(!ds.length){$('tb').innerHTML='<tr><td colspan=9><div class=vacioP><b>Sin saldo pendiente</b>Esta persona no tiene documentos con saldo.</div></td></tr>';$('docsEstado').textContent='';totales();return;}
  $('docsEstado').textContent=ds.length+' documento(s)';
  $('tb').innerHTML=ds.map(function(d){var on=ST.sel[d.id]!==undefined,ok=compat(d);
    return '<tr class=\''+(on?'on ':'')+(ok?'':'nc ')+(ST.det===d.id?'det':'')+'\'><td><input type=checkbox '+(on?'checked ':'')+(ok?'':'disabled ')+'onchange=\'marca('+d.id+',this.checked)\'></td><td class=l><b class=ver onclick=\'ver('+d.id+')\' title=\'Ver parcialidades y pagos aplicados\'>'+esc(d.tipo)+' '+esc(d.folio)+'</b>'+(d.moneda!==3?' <span class=\'chip b\'>'+esc(d.simbolo)+'</span>':'')+(d.titulo?'<small>'+esc(d.titulo)+'</small>':'')+'</td><td>'+fmtF(d.fecha)+'</td><td>'+(d.vence?fmtF(d.vence):'—')+'</td>'
      +'<td class=l>'+estadoDoc(d)+'<div class=pc>'+pagoChips(d)+'</div></td><td>'+f2(d.total)+'</td><td><b>'+f2(d.saldo)+'</b><small>'+esc(d.simbolo)+(d.moneda!==3?' · TC '+f4(d.tcDoc):'')+'</small></td><td>'+selParc(d,on)+'</td>'
      +'<td>'+(ok?'<input type=number step=any min=0 id=ap'+d.id+' value=\''+(on?ST.sel[d.id]:'')+'\' '+(on?'':'disabled')+' oninput=\'monto('+d.id+',this.value)\'><small id=lg'+d.id+'>'+(on?leyenda(d):'')+'</small>':'<small style=\'color:var(--rojo)\'>No aplica con una cuenta en '+esc(simb(mCta()))+'</small>')+'</td></tr>';}).join('');
  totales();}
function docPor(id){return docsDeEnt().filter(function(x){return x.id===id;})[0];}
function marca(id,on){var d=docPor(id);if(on){if(!(sc(d)>0))aviso('Captura primero el tipo de cambio.','mal');ST.sel[id]=sc(d);ST.det=id;pintarDetalle();if(!DET[id])cargarDet(id);}else{delete ST.sel[id];delete ST.parc[id];}pintarDocs();}
function monto(id,v){var d=docPor(id);v=+v;if(isNaN(v)||v<0)v=0;var tp=sc(d);if(v>tp){v=tp;var i=$('ap'+id);if(i)i.value=v;aviso('El importe se ajustó al saldo del documento.');}ST.sel[id]=v;var lg=$('lg'+id);if(lg)lg.innerHTML=leyenda(d);totales();}
function marcarTodos(){docsDeEnt().filter(compat).forEach(function(d){ST.sel[d.id]=sc(d);});pintarDocs();}
function marcarVencidos(){var hoy=$('fecha').value;ST.sel={};ST.parc={};docsDeEnt().filter(compat).forEach(function(d){if(d.vence&&d.vence<hoy)ST.sel[d.id]=sc(d);});pintarDocs();if(!Object.keys(ST.sel).length)aviso('No hay documentos vencidos.');}
function quitarMarcas(){ST.sel={};ST.parc={};pintarDocs();}
function distribuir(){var m=+$('monto').value;if(!ST.ent){aviso('Elige primero la persona.','mal');return;}if(!(m>0)){$('monto').classList.add('mal');$('monto').focus();aviso('Captura el monto que se va a repartir.','mal');return;}$('monto').classList.remove('mal');
  var ds=docsDeEnt().filter(compat).sort(function(a,b){var x=a.vence||a.fecha,y=b.vence||b.fecha;return x<y?-1:x>y?1:a.id-b.id;});ST.sel={};ST.parc={};var resto=r2(m);
  ds.forEach(function(d){if(resto<=0)return;var ap=Math.min(sc(d),resto);if(ap>0){ST.sel[d.id]=r2(ap);resto=r2(resto-ap);}});pintarDocs();}
function totales(){var n=0,t=0,pm=0,mc=mCta(),sy=simb(mc);for(var k in ST.sel){n++;t+=ST.sel[k];var d=docPor(+k);if(d){var da=aDoc(d,ST.sel[k]);if(Math.abs(da-d.saldo)<=0.011)da=d.saldo;pm+=valMx(d,da);}}t=r2(t);pm=r2(pm);var e=ST.ent?(ES[ST.ent.id]||{pend:0}):{pend:0},m=+$('monto').value;
  $('tN').textContent=n;$('tTot').textContent=f2(t)+' '+sy;$('tSal').textContent=f2(e.pend);$('tQ').textContent=f2(Math.max(0,e.pend-pm));$('tMon').textContent=m>0?f2(m)+' '+sy:'—';$('lVP').style.display=$('tVP').style.display=mc!==3?'':'none';$('tVP').textContent=f2(t*tcAct())+' MXN';
  var av='';if(m>0&&t>0&&Math.abs(m-t)>0.005)av+='<div class=\'aviso '+(m>t?'a':'r')+'\'>'+(m>t?'Sobran '+f2(m-t)+' '+sy+' del monto: no se aplican (los anticipos no se manejan aquí).':'Faltan '+f2(t-m)+' '+sy+' para cubrir lo marcado.')+'</div>';
  if($('fecha').value>iso(new Date()))av+='<div class=\'aviso a\'>La fecha es posterior a hoy.</div>';
  $('avisosRes').innerHTML=av;}
function validar(){var t=tipoAct();document.querySelectorAll('.mal').forEach(function(e){e.classList.remove('mal');});
  if(!ST.ent){$('ent').classList.add('mal');$('ent').focus();aviso('Elige '+(t.lado==='C'?'el cliente':'el proveedor')+' de la lista (escribe y selecciona).','mal');return false;}
  if(!+$('cta').value){$('cta').classList.add('mal');aviso('Elige la cuenta.','mal');return false;}
  var hay=false;for(var k in ST.sel){if(ST.sel[k]>0)hay=true;}if(!hay){aviso('Marca al menos un documento y captura cuánto aplicar.','mal');return false;}
  var fxs={},mc=mCta();for(var k2 in ST.sel){var d=docPor(+k2);if(d&&ST.sel[k2]>0&&d.moneda!==3&&d.moneda!==mc)fxs[d.moneda]=1;}
  if(Object.keys(fxs).length>1){aviso('Hay documentos en monedas extranjeras distintas: regístralos por separado.','mal');return false;}
  if(monedaFx()!==null&&!(tcAct()>0)){$('tc').classList.add('mal');$('tc').focus();aviso('Captura el tipo de cambio.','mal');return false;}
  return true;}
function aplicar(otro){if(ST.guardando||!validar())return;var t=tipoAct();ST.guardando=true;$('bGuardar').disabled=true;$('bNuevo').disabled=true;$('bGTxt').textContent='Registrando…';
  var aps=[];for(var k in ST.sel){if(ST.sel[k]>0)aps.push({doc:+k,monto:ST.sel[k],parcialidad:ST.parc[k]||0});}
  enviar({accion:'aplicar',nuevo:!!otro&&VIVO,spec:JSON.stringify({tipo:ST.tipo,entidad:ST.ent.id,cuenta:+$('cta').value,forma:+$('forma').value,fecha:$('fecha').value,referencia:$('ref').value,tc:monedaFx()!==null?tcAct():0,aplicaciones:aps})});}
function liberar(){ST.guardando=false;$('bGuardar').disabled=false;$('bNuevo').disabled=false;var t=tipoAct();$('bGTxt').textContent=t.lado==='C'?'Registrar cobro':'Registrar pago';}
function refrescarDocs(){DET={};return llamar('docs',{lado:tipoAct().lado}).then(function(l){if(tipoAct().lado==='C')CAT.docsC=l;else CAT.docsP=l;ES=stats();ST.sel={};ST.parc={};pintarEnt();pintarDocs();cargarMov();}).catch(function(){});}
function falloAplicar(m){liberar();aviso(m.split('\n')[0],'mal');var a=document.createElement('div');a.className='aviso r';a.style.whiteSpace='pre-wrap';a.textContent=m;$('avisosRes').insertBefore(a,$('avisosRes').firstChild);refrescarDocs();}
function aplicado(i){liberar();var lineas=String(i.resumen||'').split('\n'),titulo=lineas.shift();
  if(i.nuevo){aviso(titulo,'bien');limpiarPersona();refrescarDocs();$('ent').focus();return;}
  $('mTit').textContent=titulo;$('mSub').textContent='Se registró una sola operación:';$('mPre').textContent=lineas.join('\n');$('vel').className='vel on';refrescarDocs();}
function cerrarVentana(){enviar({accion:'cancelar'});}
function otroMas(){$('vel').className='vel';limpiarPersona();$('ent').focus();}
function limpiarPersona(){ST.ent=null;ST.sel={};ST.parc={};$('ent').value='';$('ref').value='';$('monto').value='';pintarEnt();pintarDocs();$('avisosRes').innerHTML='';}
function limpiar(){limpiarPersona();$('ent').focus();}
function cancelar(){if(Object.keys(ST.sel).length&&!confirm('Hay documentos marcados. ¿Cerrar sin registrar?'))return;enviar({accion:'cancelar'});}
document.addEventListener('keydown',function(e){if(e.key==='F5'){e.preventDefault();aplicar(false);}else if(e.key==='F6'){e.preventDefault();aplicar(true);}else if(e.key==='F2'){e.preventDefault();$('ent').focus();}else if(e.key==='Escape'){if($('vel').className.indexOf('on')>=0)return;var ab=document.querySelector('.lista[style*=block]');if(!ab)cancelar();}});
var iso=function(d){return d.getFullYear()+'-'+('0'+(d.getMonth()+1)).slice(-2)+'-'+('0'+d.getDate()).slice(-2);};
$('fecha').value=iso(new Date());$('tc').addEventListener('input',function(){pintarDocs();});$('cta').addEventListener('change',cambioCuenta);$('fecha').addEventListener('change',function(){ES=stats();pintarEnt();pintarDocs();});$('monto').addEventListener('input',totales);
$('cta').innerHTML=CAT.cuentas.map(function(c){return '<option value='+c.id+'>'+esc(c.nombre)+'</option>';}).join('');var cd=CAT.cuentas.filter(function(c){return c.def;})[0];if(cd)$('cta').value=cd.id;ST.mcPrev=mCta();
$('forma').innerHTML=CAT.formas.map(function(c){return '<option value='+c.id+(c.id===3?' selected':'')+'>'+esc(c.nombre)+'</option>';}).join('');
$('pUsr').textContent=DATOS.usuario||'—';$('pEmp').innerHTML=DATOS.empresa?'Empresa <b>'+esc(DATOS.empresa)+'</b>':'';if(!VIVO)$('bNuevo').style.display='none';
if(DATOS.inicial){var I=DATOS.inicial;ST.tipo=I.tipo;}
setTipo(ST.tipo);
if(DATOS.inicial){var I2=DATOS.inicial;var en=ENT().filter(function(x){return x.id===I2.entidad;})[0];if(en){ST.ent=en;$('ent').value=en.nombre;}$('cta').value=I2.cuenta;$('forma').value=I2.forma;$('fecha').value=I2.fecha;$('ref').value=I2.referencia||'';
  ST.sel={};ST.parc={};I2.aplicaciones.forEach(function(a){ST.sel[a.doc]=a.monto;if(a.parcialidad)ST.parc[a.doc]=a.parcialidad;});ST.mcPrev=mCta();ES=stats();pintarEnt();pintarDocs();if(I2.tc)$('tc').value=I2.tc;}
setTimeout(function(){$('ent').focus();},60);
var EX={hist:[],fut:[],snapT:0,intel:null,bT:0,borr:DATOS.borrador||null,pref:DATOS.pref||{}};
var TAB=String.fromCharCode(9),NL=String.fromCharCode(10),CR=String.fromCharCode(13);
function cerrarOv(id){$(id).classList.remove('on');}
function f0(n){return (n||0).toLocaleString('es-MX',{maximumFractionDigits:1});}
function snap(){var s=JSON.stringify(ST.sel);if(EX.hist.length&&EX.hist[EX.hist.length-1]===s)return;EX.hist.push(s);if(EX.hist.length>80)EX.hist.shift();EX.fut=[];}
function deshacer(){if(EX.hist.length<2){aviso('No hay nada que deshacer.');return;}EX.fut.push(EX.hist.pop());ST.sel=JSON.parse(EX.hist[EX.hist.length-1]);pintarDocs();pedirBorrador();aviso('Deshecho · Ctrl+Y lo rehace');}
function rehacer(){if(!EX.fut.length){aviso('No hay nada que rehacer.');return;}var s=EX.fut.pop();EX.hist.push(s);ST.sel=JSON.parse(s);pintarDocs();pedirBorrador();aviso('Rehecho');}
['marca','marcarTodos','marcarVencidos','quitarMarcas'].forEach(function(n){var o=window[n];window[n]=function(){var r=o.apply(null,arguments);snap();pedirBorrador();return r;};});
var _monto=monto;monto=function(id,v){_monto(id,v);clearTimeout(EX.snapT);EX.snapT=setTimeout(snap,700);pedirBorrador();};
distribuir=function(){var m=+$('monto').value;if(!ST.ent){aviso('Elige primero la persona.','mal');return;}if(!(m>0)){$('monto').classList.add('mal');$('monto').focus();aviso('Captura el monto que se va a repartir.','mal');return;}$('monto').classList.remove('mal');
  var e=$('estrat').value,hoy=$('fecha').value,ds=docsDeEnt().filter(compat),resto=r2(m),fv=function(d){return d.vence||d.fecha;};
  if(e==='vencidos')ds=ds.filter(function(d){return d.vence&&d.vence<hoy;});
  if(e==='mayor')ds.sort(function(a,b){return sc(b)-sc(a)||a.id-b.id;});
  else if(e==='menor')ds.sort(function(a,b){return sc(a)-sc(b)||a.id-b.id;});
  else ds.sort(function(a,b){return fv(a)<fv(b)?-1:fv(a)>fv(b)?1:a.id-b.id;});
  ST.sel={};ST.parc={};
  if(e==='prop'){var tot=ds.reduce(function(a,d){return a+sc(d);},0),usado=0;ds.forEach(function(d,i){var ap=i===ds.length-1?r2(Math.min(resto,tot)-usado):r2(Math.min(sc(d),resto*sc(d)/tot));if(ap>0){ST.sel[d.id]=Math.min(ap,sc(d));usado+=ST.sel[d.id];}});}
  else ds.forEach(function(d){if(resto<=0)return;var ap=Math.min(sc(d),resto);if(ap>0){ST.sel[d.id]=r2(ap);resto=r2(resto-ap);}});
  pintarDocs();snap();pedirBorrador();if(e==='vencidos'&&!Object.keys(ST.sel).length)aviso('Esta persona no tiene documentos vencidos.');};
var _pintarDocs=pintarDocs;pintarDocs=function(){_pintarDocs();decorar();};
function decorar(){docsDeEnt().forEach(function(d){var inp=$('ap'+d.id);if(inp&&ST.sel[d.id]!==undefined&&inp.parentNode){var pct=sc(d)>0?Math.min(100,ST.sel[d.id]/sc(d)*100):0;var m=document.createElement('span');m.className='mini';m.innerHTML='<i style="width:'+pct+'%"></i>';m.title=Math.round(pct)+' % del saldo';inp.parentNode.appendChild(m);}});}
var _totales=totales;totales=function(){_totales();revisar();};
var _pintarEnt=pintarEnt;pintarEnt=function(){_pintarEnt();pintarPron();if(!ST.ent){$('cardComp').style.display='none';EX.intel=null;}};
var _cargarMov=cargarMov;cargarMov=function(){_cargarMov();cargarComp();EX.hist=[];snap();pedirBorrador();};      // elegirEnt la llama (la lista de personas conserva la función original, así que se engancha aquí)
var _limpiarP=limpiarPersona;limpiarPersona=function(){_limpiarP();EX.hist=[];EX.fut=[];snap();};
var _setTipo=setTipo;setTipo=function(c){_setTipo(c);pintarCartera();pintarPron();};
var _aplicado=aplicado;aplicado=function(i){_aplicado(i);if(VIVO)enviar({accion:'borradorBorrar'});$('bdr').textContent='';};
var _refr=refrescarDocs;refrescarDocs=function(){var r=_refr();if(r&&r.then)r.then(function(){pintarCartera();pintarPron();});return r;};
var COL=['#16803B','#65A30D','#D97706','#EA580C','#C82828'],NOMB=['Vigente','1-30 días','31-60 días','61-90 días','Más de 90'];
function svgDona(b,total){var R=42,C=2*Math.PI*R,off=0,s='<svg viewBox="0 0 112 112" width="112" height="112" role="img" aria-label="Antigüedad de saldos"><circle cx="56" cy="56" r="'+R+'" fill="none" stroke="var(--linea)" stroke-width="16"/>';
  b.forEach(function(v,i){if(v<=0)return;var l=v/total*C;s+='<circle cx="56" cy="56" r="'+R+'" fill="none" stroke="'+COL[i]+'" stroke-width="16" stroke-dasharray="'+l+' '+(C-l)+'" stroke-dashoffset="'+(-off)+'" transform="rotate(-90 56 56)"><title>'+NOMB[i]+': '+f2(v)+' ('+Math.round(v/total*100)+' %)</title></circle>';off+=l;});
  return s+'<text x="56" y="53" text-anchor="middle" font-size="9" fill="var(--suave)">saldo</text><text x="56" y="67" text-anchor="middle" font-size="11" font-weight="700" fill="var(--texto)">'+(total>=1e6?f0(total/1e6)+' M':total>=1e3?f0(total/1e3)+' mil':f2(total))+'</text></svg>';}
function bucket(d,hoy){var dv=d.vence?dias(d.vence,hoy):-1;return dv<=0?0:dv<=30?1:dv<=60?2:dv<=90?3:4;}
function pintarCartera(){var ds=docsLado(),hoy=$('fecha').value,tot=0,venc=0,b=[0,0,0,0,0],por={},t=tipoAct();
  ds.forEach(function(d){tot+=d.saldoMx;var k=bucket(d,hoy);b[k]+=d.saldoMx;if(k>0)venc+=d.saldoMx;por[d.ent]=(por[d.ent]||0)+d.saldoMx;});
  $('cartTit').textContent='Cartera · '+(t.lado==='C'?'por cobrar':'por pagar');$('cartSub').textContent=ds.length+' documento(s) · en pesos';
  if(!tot){$('cart').innerHTML='<div class=txsm>No hay saldo pendiente.</div>';return;}
  var h='<div class=donut>'+svgDona(b,tot)+'<div class=leyenda>'+b.map(function(v,i){return '<div><span><i style="background:'+COL[i]+'"></i>'+NOMB[i]+'</span><b>'+f2(v)+'</b></div>';}).join('')+'<div style="border-top:1px solid var(--linea);padding-top:3px"><span>Vencido</span><b style="color:var(--rojo)">'+Math.round(venc/tot*100)+' %</b></div></div></div>';
  var top=Object.keys(por).map(function(k){return {id:+k,v:por[k]};}).sort(function(a,b){return b.v-a.v;}).slice(0,8),mx=top[0].v;
  h+='<div class=txsm style="margin-top:8px">'+(t.lado==='C'?'Principales deudores':'Principales acreedores')+' (clic para abrir)</div><div class=deud>'+top.map(function(x){var p=ENT().filter(function(e){return e.id===x.id;})[0];return '<a onclick="elegirPorId('+x.id+')"><div class=fl><span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:190px">'+esc(p?p.nombre:'#'+x.id)+'</span><b>'+f2(x.v)+'</b></div><div class=bar><i style="width:'+(x.v/mx*100)+'%"></i></div></a>';}).join('')+'</div>';
  $('cart').innerHTML=h;}
function elegirPorId(id){var p=ENT().filter(function(e){return e.id===id;})[0];if(p)elegirEnt(p);}
function pintarPron(){var ds=ST.ent?docsDeEnt():docsLado(),hoy=$('fecha').value,sem=[0,0,0,0,0,0,0,0],ven=0,t0=Date.parse(hoy+'T12:00:00'),t=tipoAct();
  ds.forEach(function(d){if(!d.vence)return;var dv=dias(hoy,d.vence);if(dv<0)ven+=d.saldoMx;else if(dv<56)sem[Math.floor(dv/7)]+=d.saldoMx;});
  $('pronTit').textContent=(ST.ent?'Vencimientos de '+ST.ent.nombre.split(' ').slice(0,2).join(' '):'Vencimientos · '+(t.lado==='C'?'por cobrar':'por pagar'));
  var vals=[ven].concat(sem),mx=Math.max.apply(null,vals.concat([1])),W=310,H=112,base=18,bw=(W-8)/vals.length,s='<svg viewBox="0 0 '+W+' '+H+'" width="100%" role="img" aria-label="Vencimientos por semana"><line x1="4" x2="'+(W-4)+'" y1="'+(H-base)+'" y2="'+(H-base)+'" stroke="var(--linea)"/>';
  vals.forEach(function(v,i){var h=v/mx*(H-base-14),x=4+i*bw,y=H-base-h,lbl=i===0?'vencido':new Date(t0+(i-1)*7*864e5).toLocaleDateString('es-MX',{day:'2-digit',month:'2-digit'});
    s+='<g><title>'+(i===0?'Ya vencido: ':'Semana del '+lbl+': ')+f2(v)+'</title><rect x="'+(x+2)+'" y="'+(v>0?y:H-base-1)+'" width="'+(bw-4)+'" height="'+(v>0?Math.max(h,1.5):1)+'" rx="3" fill="'+(i===0?'var(--rojo)':'var(--acc)')+'" opacity="'+(i===0?.85:.6)+'"/><text x="'+(x+bw/2)+'" y="'+(H-5)+'" font-size="7.5" text-anchor="middle" fill="var(--suave)">'+lbl+'</text></g>';});
  $('pron').innerHTML=s+'</svg>'+(ven>0?'<div class=txsm style="color:var(--rojo)">Ya vencido: <b>'+f2(ven)+'</b></div>':'');}
function mesCorto(m){return ['ene','feb','mar','abr','may','jun','jul','ago','sep','oct','nov','dic'][+m.slice(5,7)-1];}
function svgBarras(ms){var W=310,H=112,base=16,mx=Math.max.apply(null,ms.map(function(m){return m.total;}).concat([1])),bw=(W-8)/ms.length,s='<svg viewBox="0 0 '+W+' '+H+'" width="100%" role="img" aria-label="Movimientos por mes"><line x1="4" x2="'+(W-4)+'" y1="'+(H-base)+'" y2="'+(H-base)+'" stroke="var(--linea)"/>';
  ms.forEach(function(m,i){var h=m.total/mx*(H-base-12),x=4+i*bw,y=H-base-h;s+='<g><title>'+mesCorto(m.mes)+' '+m.mes.slice(0,4)+': '+f2(m.total)+' · '+m.n+' mov.</title><rect x="'+(x+1.5)+'" y="'+(m.total>0?y:H-base-1)+'" width="'+(bw-3)+'" height="'+(m.total>0?Math.max(h,1.5):1)+'" rx="3" fill="var(--acc)" opacity="'+(i===ms.length-1?1:.55)+'"/><text x="'+(x+bw/2)+'" y="'+(H-4)+'" font-size="8" text-anchor="middle" fill="var(--suave)">'+mesCorto(m.mes)+'</text></g>';});return s+'</svg>';}
function cargarComp(){var x=ST.ent;if(!x||!VIVO)return;var t=tipoAct();$('cardComp').style.display='';$('comp').innerHTML='<div class=txsm>Calculando con los datos de Comercial…</div>';
  llamar('inteligencia',{entidad:x.id,tipo:ST.tipo}).then(function(d){if(!ST.ent||ST.ent.id!==x.id)return;EX.intel=d;var p=d.puntual;
    $('compSub').innerHTML=p==null?'':'<span class="chip '+(p>=80?'v':p>=50?'a':'r')+'">'+p+' % a tiempo</span>';
    $('comp').innerHTML=svgBarras(d.meses)+'<div class=kpi><div><b>'+(t.lado==='C'?'Cobrado 12 meses':'Pagado 12 meses')+'</b><span>'+f2(d.total12)+'</span></div><div><b>Movimientos</b><span>'+d.mov12+'</span></div><div><b>Días promedio de pago</b><span>'+(d.dias==null?'—':f0(d.dias))+'</span></div><div><b>Atraso promedio</b><span style="color:'+(d.atraso>0?'var(--rojo)':'var(--verde)')+'">'+(d.atraso==null?'—':(d.atraso>0?'+':'')+f0(d.atraso)+' d')+'</span></div></div>';}).catch(function(){$('comp').innerHTML='<div class=txsm>No se pudo consultar el historial.</div>';});}
function revisar(){var t=tipoAct(),it=[],x=ST.ent,sel=ST.sel,n=0,tot=0,ppd=0;function ok(s){it.push(['v','✓',s]);}function av(s){it.push(['a','!',s]);}function ma(s){it.push(['r','✕',s]);}
  for(var k in sel){if(sel[k]>0){n++;tot+=sel[k];var d=docPor(+k);if(d&&d.metodo==='PPD')ppd++;}}tot=Math.round(tot*100)/100;
  x?ok((t.lado==='C'?'Cliente':'Proveedor')+': '+x.nombre):ma('Falta elegir '+(t.lado==='C'?'el cliente':'el proveedor'));
  +$('cta').value?ok('Cuenta: '+($('cta').selectedOptions[0]||{}).textContent):ma('Falta elegir la cuenta');
  n?ok(n+' documento(s) · '+f2(tot)+' '+simb(mCta())):ma('Marca al menos un documento');
  if(monedaFx()!==null){tcAct()>0?ok('Tipo de cambio '+simb(monedaFx())+': '+f4(tcAct())):ma('Falta el tipo de cambio');}
  var m=+$('monto').value;if(m>0&&tot>0&&Math.abs(m-tot)>0.005)av(m>tot?'Sobran '+f2(m-tot)+' del monto (no se aplican)':'Faltan '+f2(tot-m)+' para cubrir lo marcado');
  if(n&&tot>0){var nc=0,nn=0;for(var k3 in sel){var d3=docPor(+k3);if(d3&&sel[k3]>0){if(d3.nParc>1&&!ST.parc[k3])nc++;if(d3.nNotas>0)nn++;}}if(nc)av(nc+' documento(s) con parcialidades en modo automático: se aplica a la más antigua con saldo');if(nn)av(nn+' documento(s) ya tienen notas de crédito aplicadas (revisa su detalle)');}
  if($('fecha').value>iso(new Date()))av('La fecha es posterior a hoy');
  if(ppd)av(ppd+' documento(s) PPD: '+(t.lado==='C'?'recuerda emitir el complemento de pago (REP)':'pide a tu proveedor el complemento de pago (REP)'));
  var forma=+$('forma').value;if(n&&forma!==1&&!$('ref').value.trim())av('Sin referencia o número de rastreo: ayuda a conciliar el banco');
  if(t.lado==='P'&&forma===1&&tot>2000)av('Pago en efectivo mayor a $2,000: no es deducible (LISR art. 27)');
  var ma2=it.filter(function(a){return a[0]==='r';}).length,w=it.filter(function(a){return a[0]==='a';}).length;
  $('revSub').innerHTML=ma2?'<span class="chip r">'+ma2+' por corregir</span>':w?'<span class="chip a">'+w+' aviso(s)</span>':'<span class="chip v">todo en orden</span>';
  $('revis').innerHTML=it.map(function(a){return '<div class='+a[0]+'><i>'+a[1]+'</i><span>'+esc(a[2])+'</span></div>';}).join('');}
['cta','forma','ref','fecha','tc'].forEach(function(i){$(i).addEventListener('input',revisar);$(i).addEventListener('change',revisar);});
$('fecha').addEventListener('change',function(){pintarCartera();pintarPron();});
var PAL={sel:0,vis:[]};
function comandos(){var c=[{g:'Acciones',a:'Registrar y abrir resumen',b:'F5',f:function(){aplicar(false);}},{g:'Acciones',a:'Registrar y capturar otro',b:'F6',f:function(){aplicar(true);}},{g:'Acciones',a:'Marcar todos los documentos',f:marcarTodos},{g:'Acciones',a:'Marcar solo los vencidos',f:marcarVencidos},{g:'Acciones',a:'Quitar marcas',f:quitarMarcas},
  {g:'Acciones',a:'Vista previa del recibo',b:'Ctrl+P',f:vistaPrevia},{g:'Acciones',a:'Copiar documentos a Excel',f:copiarDocs},{g:'Acciones',a:'Deshacer',b:'Ctrl+Z',f:deshacer},{g:'Acciones',a:'Rehacer',b:'Ctrl+Y',f:rehacer},{g:'Acciones',a:'Cambiar tema claro/oscuro',f:alternarTema},{g:'Acciones',a:'Cancelar y cerrar',b:'Esc',f:cancelar}];
  [['antiguos','Repartir el monto: más antiguos primero'],['vencidos','Repartir el monto: solo vencidos'],['mayor','Repartir el monto: mayor saldo primero'],['menor','Repartir el monto: menor saldo primero'],['prop','Repartir el monto: proporcional']].forEach(function(e){c.push({g:'Repartir el monto',a:e[1],f:function(){$('estrat').value=e[0];distribuir();}});});
  TIPOS.forEach(function(t){c.push({g:'Tipo de movimiento',a:t.nombre,f:function(){setTipo(t.clave);}});});return c;}
function abrirPaleta(){$('ovPal').classList.add('on');$('palq').value='';PAL.sel=0;pintarPaleta();setTimeout(function(){$('palq').focus();},20);}
function pintarPaleta(){var q=norm($('palq').value).split(/\s+/).filter(Boolean),out=[];
  comandos().forEach(function(c){var h=norm(c.a+' '+c.g);if(q.every(function(w){return h.indexOf(w)>=0;}))out.push(c);});
  if(q.length&&q.join('').length>=2){var n=0;ENT().forEach(function(e){if(n>=6)return;var h=norm(e.nombre+' '+e.rfc);if(q.every(function(w){return h.indexOf(w)>=0;})){n++;var s=ES[e.id];out.push({g:tipoAct().lado==='C'?'Clientes':'Proveedores',a:e.nombre,b:s?'debe '+f2(s.pend):'sin saldo',f:function(){elegirEnt(e);}});}});
    n=0;docsLado().forEach(function(d){if(n>=6)return;var h=norm(d.tipo+' '+d.folio+' '+(d.titulo||''));if(q.every(function(w){return h.indexOf(w)>=0;})){n++;out.push({g:'Documentos con saldo (abre a la persona y lo marca)',a:d.tipo+' '+d.folio,b:'saldo '+f2(d.saldo)+' '+d.simbolo+(d.vence?' · vence '+fmtF(d.vence):''),f:function(){elegirPorId(d.ent);marca(d.id,true);}});}});}
  PAL.vis=out.slice(0,40);if(PAL.sel>=PAL.vis.length)PAL.sel=0;var g='',h='';PAL.vis.forEach(function(c,i){if(c.g!==g){g=c.g;h+='<div class=gr>'+esc(g)+'</div>';}h+='<div data-i="'+i+'" class="'+(i===PAL.sel?'sel':'')+'"><span>'+esc(c.a)+'</span><small>'+esc(c.b||'')+'</small></div>';});$('pall').innerHTML=h||'<div class=gr>Sin coincidencias</div>';}
function ejecutarPaleta(i){var c=PAL.vis[i];if(!c)return;cerrarOv('ovPal');setTimeout(function(){c.f();},10);}
$('palq').addEventListener('input',function(){PAL.sel=0;pintarPaleta();});
$('palq').addEventListener('keydown',function(e){if(e.key==='ArrowDown'){PAL.sel=Math.min(PAL.vis.length-1,PAL.sel+1);pintarPaleta();var s=document.querySelector('#pall .sel');if(s)s.scrollIntoView({block:'nearest'});e.preventDefault();}else if(e.key==='ArrowUp'){PAL.sel=Math.max(0,PAL.sel-1);pintarPaleta();var s2=document.querySelector('#pall .sel');if(s2)s2.scrollIntoView({block:'nearest'});e.preventDefault();}else if(e.key==='Enter'){ejecutarPaleta(PAL.sel);e.preventDefault();}});
$('pall').addEventListener('mousedown',function(e){var d=e.target.closest('div[data-i]');if(d){ejecutarPaleta(+d.getAttribute('data-i'));e.preventDefault();}});
function copiarDocs(){var ds=ST.ent?docsDeEnt():docsLado();if(!ds.length){aviso('No hay documentos con saldo que copiar.');return;}
  var tx=['Documento','Persona','Moneda','Fecha','Vence','Total','Pagado','Saldo','Parcialidades','Aplicar'].join(TAB)+NL+ds.map(function(d){var p=ENT().filter(function(e){return e.id===d.ent;})[0];return [d.tipo+' '+d.folio,p?p.nombre:d.ent,d.simbolo,d.fecha,d.vence||'',d.total,d.pagado,d.saldo,d.nParc,ST.sel[d.id]!==undefined?ST.sel[d.id]:''].join(TAB);}).join(NL);
  function listo(){aviso(ds.length+' documento(s) copiados: pégalos en Excel con Ctrl+V.','bien');}
  function plan(){var a=document.createElement('textarea');a.value=tx;a.style.position='fixed';a.style.opacity='0';document.body.appendChild(a);a.select();try{document.execCommand('copy');listo();}catch(e){aviso('No se pudo copiar al portapapeles.','mal');}document.body.removeChild(a);}
  if(navigator.clipboard&&navigator.clipboard.writeText)navigator.clipboard.writeText(tx).then(listo).catch(plan);else plan();}
var U1=['','UNO','DOS','TRES','CUATRO','CINCO','SEIS','SIETE','OCHO','NUEVE','DIEZ','ONCE','DOCE','TRECE','CATORCE','QUINCE','DIECISÉIS','DIECISIETE','DIECIOCHO','DIECINUEVE','VEINTE'],D1=['','','VEINTE','TREINTA','CUARENTA','CINCUENTA','SESENTA','SETENTA','OCHENTA','NOVENTA'],V1=['VEINTIUNO','VEINTIDÓS','VEINTITRÉS','VEINTICUATRO','VEINTICINCO','VEINTISÉIS','VEINTISIETE','VEINTIOCHO','VEINTINUEVE'],C1=['','CIENTO','DOSCIENTOS','TRESCIENTOS','CUATROCIENTOS','QUINIENTOS','SEISCIENTOS','SETECIENTOS','OCHOCIENTOS','NOVECIENTOS'];
function cent(n){if(n===0)return '';if(n===100)return 'CIEN';var r='',c=Math.floor(n/100),d=n%100;if(c>0)r+=C1[c]+' ';if(d>0){if(d<=20)r+=U1[d];else{var a=Math.floor(d/10),u=d%10;if(a===2&&u>0)r+=V1[u-1];else{r+=D1[a];if(u>0)r+=' Y '+U1[u];}}}return r.trim();}
function enLetras(n){if(n===0)return 'CERO';var r='',mi=Math.floor(n/1000000);n%=1000000;var m=Math.floor(n/1000);n%=1000;if(mi>0)r+=mi===1?'UN MILLÓN ':cent(mi)+' MILLONES ';if(m>0)r+=m===1?'MIL ':cent(m)+' MIL ';if(n>0)r+=cent(n);r=r.trim();if(/UNO$/.test(r))r=r.slice(0,-3)+'UN';return r;}
function aLetras(v){v=Math.max(0,v);var e=Math.floor(v),c=Math.round((v-e)*100),mc=mCta(),pal=(MON[mc]&&MON[mc].letra?MON[mc].letra:'Pesos').toUpperCase();if(c===100){e++;c=0;}return enLetras(e)+' '+pal+' '+('0'+c).slice(-2)+'/100'+(mc===3?' M.N.':'');}
function vistaPrevia(){var t=tipoAct(),x=ST.ent,f=CAT.folios&&CAT.folios[t.clave],filas=[],tot=0;
  docsDeEnt().forEach(function(d){if(ST.sel[d.id]>0){tot+=ST.sel[d.id];var da=r2(aDoc(d,ST.sel[d.id]));if(Math.abs(da-d.saldo)<=0.011)da=d.saldo;filas.push('<tr><td class=l>'+esc(d.tipo+' '+d.folio)+(ST.parc[d.id]?' · parc. '+ST.parc[d.id]:'')+'</td><td>'+fmtF(d.fecha)+'</td><td>'+f2(d.saldo)+' '+esc(d.simbolo)+'</td><td>'+f2(ST.sel[d.id])+' '+esc(simb(mCta()))+(d.moneda!==mCta()?'<br><span style="color:#64748B;font-size:11px">= '+f2(da)+' '+esc(d.simbolo)+'</span>':'')+'</td><td>'+f2(Math.max(0,r2(d.saldo-da)))+' '+esc(d.simbolo)+'</td></tr>');}});tot=Math.round(tot*100)/100;
  var h='<div class=tt><div><h1>'+(t.lado==='C'?'Recibo de cobro':'Comprobante de pago')+'</h1><div style="color:#64748B">'+esc(DATOS.empresa||'')+'</div></div><div style="text-align:right"><div><b>Folio</b> ≈ '+esc(t.prefijo)+'-'+(f||'—')+'</div><div><b>Fecha</b> '+fmtF($('fecha').value)+'</div>'+(monedaFx()!==null?'<div><b>Tipo de cambio</b> '+f4(tcAct())+'</div>':'')+'</div></div>';
  h+='<div class=bloque><div><b>'+(t.lado==='C'?'Cliente':'Proveedor')+'</b><br>'+esc(x?x.nombre:'(sin elegir)')+'</div><div><b>RFC</b><br>'+esc(x&&x.rfc||'—')+'</div><div><b>'+(t.lado==='C'?'Entra a la cuenta':'Sale de la cuenta')+'</b><br>'+esc(($('cta').selectedOptions[0]||{}).textContent||'—')+'</div><div><b>Forma de pago</b><br>'+esc(($('forma').selectedOptions[0]||{}).textContent||'—')+'</div>'+($('ref').value?'<div style="grid-column:span 2"><b>Referencia</b><br>'+esc($('ref').value)+'</div>':'')+'</div>';
  h+='<table><thead><tr><th class=l>Documento</th><th>Fecha</th><th>Saldo anterior</th><th>Se aplica</th><th>Saldo restante</th></tr></thead><tbody>'+(filas.length?filas.join(''):'<tr><td colspan=5 style="text-align:center;color:#64748B;padding:18px">Sin documentos marcados</td></tr>')+'</tbody></table>';
  h+='<div class=tot2><div class=g><span>Total</span><span>'+f2(tot)+' '+esc(simb(mCta()))+'</span></div></div><div class=letra>SON: '+aLetras(tot)+'</div>';
  h+='<div style="display:flex;gap:60px;margin-top:46px;font-size:12px"><div style="flex:1;border-top:1px solid #16263A;text-align:center;padding-top:4px">'+(t.lado==='C'?'Recibí':'Entregué')+'</div><div style="flex:1;border-top:1px solid #16263A;text-align:center;padding-top:4px">'+(t.lado==='C'?'Entregó':'Recibió')+'</div></div><div style="margin-top:14px;color:#94A3B8;font-size:10.5px">Vista previa generada por BrosLMV. Los folios definitivos los asigna Comercial al registrar (una sola operación con un renglón por documento).</div>';
  $('hoja').innerHTML=h;$('ovPrev').classList.add('on');}
function extrasSpec(){var aps={};for(var k in ST.sel){aps[k]=ST.sel[k];}return {tipo:ST.tipo,entidad:ST.ent?ST.ent.id:0,cuenta:+$('cta').value,forma:+$('forma').value,fecha:$('fecha').value,referencia:$('ref').value,monto:+$('monto').value||0,tc:tcAct(),parc:ST.parc,sel:aps};}
function pedirBorrador(){if(!VIVO||ST.guardando)return;clearTimeout(EX.bT);EX.bT=setTimeout(function(){if(!ST.ent)return;enviar({accion:'borrador',spec:JSON.stringify(extrasSpec())});var d=new Date();$('bdr').textContent='Borrador guardado '+('0'+d.getHours()).slice(-2)+':'+('0'+d.getMinutes()).slice(-2);},1500);}
document.addEventListener('change',pedirBorrador);
function restaurar(sp){setTipo(sp.tipo);var en=ENT().filter(function(x){return x.id===sp.entidad;})[0];$('cta').value=sp.cuenta;$('forma').value=sp.forma;$('fecha').value=sp.fecha||$('fecha').value;$('ref').value=sp.referencia||'';$('monto').value=sp.monto||'';
  if(en){ES=stats();ST.ent=en;$('ent').value=en.nombre;ST.sel={};for(var k in sp.sel){if(docPor(+k)){ST.sel[k]=sp.sel[k];if(sp.parc&&sp.parc[k])ST.parc[k]=sp.parc[k];}}ST.mcPrev=mCta();pintarEnt();pintarDocs();if(sp.tc)$('tc').value=sp.tc;cargarMov();cargarComp();}snap();}
(function(){var b=$('banBorr'),izq=document.querySelector('.izq');izq.insertBefore(b,izq.firstChild);
  if(EX.borr&&EX.borr.spec&&!DATOS.inicial){var sp;try{sp=JSON.parse(EX.borr.spec);}catch(e){sp=null;}
    if(sp&&sp.entidad){var cuando=EX.borr.cuando?new Date(EX.borr.cuando):null,min=cuando?Math.max(1,Math.round((Date.now()-cuando.getTime())/60000)):0,n=Object.keys(sp.sel||{}).length;
      b.innerHTML='<span>Hay un <b>borrador sin registrar</b>'+(min?' de hace '+(min<90?min+' min':Math.round(min/60)+' h'):'')+' ('+n+' documento(s) marcados). ¿Lo recuperas?</span><button class=btn onclick="descartarBorrador()">Descartar</button><button class="btn p" onclick="recuperarBorrador()">Recuperar</button>';b.classList.add('on');EX.borrSpec=sp;}}})();
function recuperarBorrador(){$('banBorr').classList.remove('on');restaurar(EX.borrSpec);aviso('Borrador recuperado.','bien');}
function descartarBorrador(){$('banBorr').classList.remove('on');if(VIVO)enviar({accion:'borradorBorrar'});}
function ponerTema(o){document.body.classList.toggle('oscuro',!!o);}
function alternarTema(){var o=!document.body.classList.contains('oscuro');ponerTema(o);if(VIVO)enviar({accion:'pref',tema:o?'oscuro':'claro'});}
if(EX.pref.tema==='oscuro')ponerTema(true);
document.addEventListener('keydown',function(e){var k=e.key.toLowerCase(),en=/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
  if(e.ctrlKey&&k==='k'){e.preventDefault();abrirPaleta();}
  else if(e.ctrlKey&&k==='p'){e.preventDefault();vistaPrevia();}
  else if(e.ctrlKey&&k==='z'&&!en){e.preventDefault();deshacer();}
  else if(e.ctrlKey&&k==='y'&&!en){e.preventDefault();rehacer();}
  else if(e.key==='Escape'){var o=document.querySelector('.ov.on');if(o){o.classList.remove('on');e.stopPropagation();}}},true);
snap();pintarCartera();pintarPron();revisar();
</script></body></html>
'''

def principal():
    global result
    catalogo = catalogos()
    usuario = ""
    nombre_empresa = ""
    try:
        usuario = S(ctx.scalar("SELECT TOP 1 ISNULL(UserName,'') FROM engUser WHERE UserID = " + str(int(ctx.user_id))))
    except Exception:
        pass
    try:
        nombre_empresa = S(ctx.scalar("SELECT TOP 1 ISNULL(CommercialName, OfficialName) FROM orgBusinessEntity WHERE BusinessEntityID = " + str(empresa)))
    except Exception:
        pass

    html_prueba = os.environ.get("BROSLMV_PAGO_HTML")
    if html_prueba:
        d0 = {"tipos": TIPOS, "cat": catalogo, "inicial": None, "usuario": usuario, "empresa": nombre_empresa}
        with open(html_prueba, "w", encoding="utf-8") as f:
            f.write(PAGINA.replace("__DATOS__", js_json(d0)))
        result = "HTML"
        return

    def pagina(http):
        datos = {"tipos": TIPOS, "cat": catalogo, "inicial": None, "vivo": True, "http": http, "usuario": usuario, "empresa": nombre_empresa,
                 "borrador": leer_borrador("cobropago"), "pref": leer_borrador("pref_ui", False) or {}}
        return PAGINA.replace("__DATOS__", js_json(datos))

    def despachar(r, estado):
        accion = S(r.get("accion"))
        req = S(r.get("req") or "0")
        if accion == "latido":
            return ""
        if accion == "cancelar":
            estado["fin"] = True
            return "window.close()"
        if accion == "movimientos":
            return "respuesta(" + req + "," + js_json(movimientos_de(I(r["entidad"]), S(r["tipo"]))) + ")"
        if accion == "docs":
            return "respuesta(" + req + "," + js_json(docs_con_saldo(S(r["lado"]))) + ")"
        if accion == "detalle":
            return "respuesta(" + req + "," + js_json(detalle_doc(I(r["doc"]))) + ")"
        if accion == "inteligencia":
            return "respuesta(" + req + "," + js_json(inteligencia_pago(I(r["entidad"]), S(r["tipo"]))) + ")"
        if accion == "borrador":
            guardar_borrador("cobropago", {"cuando": datetime.datetime.now().isoformat(), "spec": S(r.get("spec"))})
            return ""
        if accion == "borradorBorrar":
            borrar_borrador("cobropago")
            return ""
        if accion == "pref":
            guardar_borrador("pref_ui", {"tema": S(r.get("tema"))})
            return ""
        if accion != "aplicar":
            return ""
        spec = json.loads(S(r["spec"]))
        otro = bool(r.get("nuevo"))
        try:
            resumen = aplicar(spec)
        except Exception as ex:
            return "falloAplicar(" + js_json(S(ex)) + ")"
        borrar_borrador("cobropago")
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass
        return "aplicado(" + js_json({"resumen": resumen, "nuevo": otro}) + ")"

    ventana_en_vivo(pagina, despachar, "Cobros y pagos · BrosLMV", 1280, 900)
    result = "OK"

if not _modo_prueba:
    principal()
