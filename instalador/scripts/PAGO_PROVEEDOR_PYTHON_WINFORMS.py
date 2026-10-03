# lang: python
# timeout: 1800
# AppKey recomendado: PAGO_PROVEEDOR_PYTHON_WINFORMS
# Plantilla: Pago a proveedor (Python · ventana Windows Forms)
# Categoria: Tesorería
# Documentacion: COBRO_PAGO.html
# ⚠ PLANTILLA AVANZADA, NO NATIVA. Registra un pago a proveedor y lo aplica a uno o varios documentos con saldo.
# Comercial no ofrece una función para esto: la plantilla escribe directo en las tablas de Tesorería (una transacción por documento) y NO genera la póliza contable.
# Plantilla separada a propósito: solo CUENTAS POR PAGAR (proveedores) para que quien la use no vea el otro lado. Léela completa antes de usarla y pruébala primero en una base de pruebas. Documentación: clic secundario sobre la plantilla → «Ver documentación».
#
# Qué enseña: la receta de SQL directo de siete tablas (operación financiera, aplicación, espejo, transferencia bancaria, impuestos proporcionales, nuevo saldo),
# el folio serializado con candado de transacción y la revalidación del saldo dentro de la transacción.
# Windows Forms desde Python (pythonnet): Python corre en su propio proceso, la ventana es automáticamente independiente y un error nunca tumba Comercial.

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
    AutoSizeMode, DrawMode, DrawItemState, DataGridViewTextBoxColumn, DataGridViewCheckBoxColumn, DataGridViewContentAlignment, DataGridViewAutoSizeColumnsMode,
    DataGridViewSelectionMode, DataGridViewCellBorderStyle, DataGridViewHeaderBorderStyle, DateTimePicker, DateTimePickerFormat,
    ListBox, BorderStyle, Cursors, Keys, MessageBox, MessageBoxButtons, MessageBoxIcon,
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


empresa = I(ctx.erp.OwnedBusinessEntityId())      # en Python ctx.erp.X siempre es una función (relevo al addon): se llama, aunque en C# sea una propiedad


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
# COBRO A CLIENTE / PAGO A PROVEEDOR.
# ⚠ PLANTILLA AVANZADA, NO NATIVA: Comercial no ofrece ninguna función para aplicar un cobro o un pago (se buscó en el SDK y en el motor, véase MANUAL §10.5).
# Esta plantilla repite, con SQL directo y en UNA sola transacción por documento, lo que hace la pantalla de Tesorería: la operación financiera, la aplicación al
# documento, su espejo, la transferencia bancaria, el reparto proporcional de impuestos y el nuevo saldo. La receta se validó contra cobros reales y contra el laboratorio.
# NO genera la póliza contable del cobro/pago (eso lo hace el Motor de Asientos al contabilizar). Pruébala SIEMPRE primero en una base de pruebas.
# ===================================================================================================================================
# Tipos: clave · nombre · lado (C/P) · módulo de la operación (248 cobro, 247 pago) · DocRecipientID · DocumentTypeID de la operación · prefijo de folio
def TP(clave, nombre, lado, mod_op, recip, tipo_op, prefijo):
    return {"clave": clave, "nombre": nombre, "lado": lado, "modOp": mod_op, "recip": recip, "tipoOp": tipo_op, "prefijo": prefijo}


TIPOS = [
    TP("pago",  "Pago a proveedor", "P", 247, 2, 32, "PAG"),
]
TIPO_POR = {t["clave"]: t for t in TIPOS}

# ---------- Qué módulos generan cuentas por cobrar / por pagar ----------
# Se leen de los parámetros del módulo (DocRecipient 1 = cliente, 2 = proveedor; FinancialAffectation ≠ 0) por ModuleIDBase, así los módulos clonados cuentan igual.
# Solo documentos que SUMAN saldo (facturas, notas de cargo, gastos): una nota de crédito no se cobra ni se paga, se aplica.
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


# ---------- Monedas ----------
# La cuenta tiene su moneda y cada documento la suya (0 = sin moneda = pesos). El tipo de cambio de cada moneda sale del catálogo de monedas de Comercial.
monedas = [{"id": I(r["id"]), "simbolo": S(r["simbolo"]), "nombre": S(r["nombre"]), "letra": S(r["letra"]), "tc": D(r["tc"]) or 1.0} for r in ctx.query(
    "SELECT CurrencyID AS id, IntlSymbol AS simbolo, Currency AS nombre, ISNULL(MoneyLetter,'') AS letra, ISNULL(Rate,1) AS tc FROM vwLBSCurrencyList ORDER BY CurrencyID")]
moneda_por = {m["id"]: m for m in monedas}


def moneda_de(v):
    m = I(v)
    return 3 if m <= 0 else m


def simbolo_de(id_):
    return moneda_por[id_]["simbolo"] if id_ in moneda_por else "MXN"


# ---------- Documentos con saldo pendiente de un lado (C = por cobrar, P = por pagar) ----------
# Tope de seguridad: 30,000 por lado (los más antiguos primero, que son los que se cobran/pagan primero). La ventana los pide otra vez después de aplicar, para ver los saldos nuevos.
# Además del saldo trae lo necesario para decidir: moneda y tipo de cambio del documento, cuántas parcialidades tiene, cuántos cobros/pagos y cuántas notas de crédito ya se le aplicaron.
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


# Parcialidades de un documento: la agenda de pago (número, vencimiento, importe) menos lo ya aplicado a cada una (los renglones sin número cuentan como la 1).
# Si la suma de lo pendiente no cuadra con el saldo del documento (intereses, redondeo) la diferencia se carga a la última parcialidad con saldo; sin agenda hay una sola parcialidad con todo el saldo.
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


# Todo lo que le ha pasado a un documento: sus parcialidades y cada aplicación (cobro, pago o nota de crédito) con folio, fecha, moneda, tipo de cambio y parcialidad. La ventana lo pide al seleccionar el documento.
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


# Comportamiento de pago de una persona: lo cobrado (o pagado) por mes en los últimos 12 meses, los días promedio que tarda desde la fecha del documento, el atraso promedio
# frente al vencimiento y el porcentaje de pagos a tiempo. Todo sale de Comercial en ese momento.
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


# ---------- Catálogos y documentos con saldo para el formulario ----------
def catalogos():
    cat = {}

    # Persona con RFC, límite de crédito y su último cobro o pago (fecha e importe), para dar contexto al capturar
    def ent(tabla):
        return ("SELECT be.BusinessEntityID AS id, ISNULL(be.CommercialName, be.OfficialName) AS nombre, ISNULL(mi.OfficialNumber,'') AS rfc, ISNULL(x.CreditLimit,0) AS credito, "
                "CONVERT(VARCHAR(10), u.DateOperation, 23) AS ultFecha, ISNULL(u.Amount,0) AS ultMonto FROM " + tabla + " x "
                "JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID "
                "OUTER APPLY (SELECT TOP 1 o.DateOperation, o.Amount FROM docFinancialOperation o WHERE o.BusinessEntityID = be.BusinessEntityID AND o.ModuleID IN (247,248) AND o.CancelledOn IS NULL AND o.DeletedOn IS NULL ORDER BY o.DateOperation DESC, o.FinancialOperationID DESC) u "
                "WHERE be.DeletedOn IS NULL AND x.DeletedOn IS NULL ORDER BY nombre")

    def lista(tabla):
        return [{"id": I(r["id"]), "nombre": S(r["nombre"]), "rfc": S(r["rfc"]), "credito": D(r["credito"]), "ultFecha": S(r["ultFecha"]), "ultMonto": D(r["ultMonto"])} for r in ctx.query(ent(tabla))]

    # Solo el lado de esta plantilla: la persona que lleva cuentas por cobrar no ve nada de las cuentas por pagar (y al revés); ni siquiera se cargan sus datos.
    ver_c = any(t["lado"] == "C" for t in TIPOS)
    ver_p = any(t["lado"] == "P" for t in TIPOS)
    cat["clientes"] = lista("orgCustomer") if ver_c else []
    cat["proveedores"] = lista("orgSupplier") if ver_p else []
    cat["cuentas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "def": I(r["def"]) == 1, "moneda": moneda_de(r["moneda"])} for r in ctx.query(
        "SELECT FinancialEntityID AS id, FinancialEntityName AS nombre, ISNULL(IsDefault,0) AS def, ISNULL(CurrencyID,0) AS moneda FROM orgFinancialEntity WHERE DeletedOn IS NULL ORDER BY ISNULL(IsDefault,0) DESC, FinancialEntityName")]
    cat["monedas"] = monedas
    cat["formas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query("SELECT ID AS id, Value AS nombre FROM vwcboCFDPaymentmethod ORDER BY CboOrder")]
    # Folio siguiente de cada tipo (el mismo cálculo que usa aplicar)
    cat["folios"] = {t["clave"]: I(ctx.scalar("SELECT ISNULL(MAX(TRY_CONVERT(BIGINT, Folio)),0) + 1 FROM docFinancialOperation WHERE ModuleID = " + str(t["modOp"]) + " AND FolioPrefix = N'" + t["prefijo"] + "'")) for t in TIPOS}
    # Documentos con saldo pendiente de cada lado (los más antiguos primero)
    cat["docsC"] = docs_con_saldo("C") if ver_c else []
    cat["docsP"] = docs_con_saldo("P") if ver_p else []
    return cat


# ---------- Importe en letra (el mismo estilo del comprobante mexicano) ----------
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


# ---------- Aplicar ----------
# «spec»: tipo, entidad, cuenta, forma (c_FormaPago), fecha (yyyy-MM-dd), referencia, tc (tipo de cambio, solo si hay moneda extranjera), aplicaciones [{doc, monto, parcialidad}].
#   «monto» va SIEMPRE en la moneda de la CUENTA (lo que entra o sale del banco); «parcialidad» 0 = automática (en orden), n = esa parcialidad.
# Igual que Tesorería: UNA operación (un folio) con un renglón por documento (y por parcialidad), todo en una sola transacción: o queda todo o no queda nada.
# Moneda (verificado contra miles de aplicaciones de una empresa con dólares): el renglón se guarda en la moneda del DOCUMENTO, con su tipo de cambio en Rate y AmountPaidCurrency = Amount × Rate (valor en pesos):
#   · cuenta y documento en la misma moneda → importe tal cual (Rate = 1 en pesos, o el tipo de cambio en moneda extranjera);
#   · cuenta en pesos y documento en moneda extranjera → importe del documento = pesos ÷ tipo de cambio (Rate = tipo de cambio);
#   · cuenta en moneda extranjera y documento en pesos → importe del documento = moneda extranjera × tipo de cambio (Rate = 1; la operación guarda el tipo de cambio).
#   Cualquier otra combinación (p. ej. cuenta en euros y documento en dólares) se rechaza: usa la pantalla nativa de Tesorería.
# Regresa un resumen legible con el folio y, por documento, lo aplicado, su equivalente en la moneda del documento y lo que queda.
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

    # 1) Cada documento: validación, conversión a su moneda y reparto por parcialidades (todo en memoria; no se escribe nada todavía)
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
        # Importe en la moneda del documento y tipo de cambio del renglón
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
            doc_amt = saldo                                               # un residuo de redondeo de la conversión no deja el documento «casi liquidado»
        if doc_amt > saldo + 0.005:
            raise Exception("No se pudo aplicar a " + etiqueta + ": el importe (" + "{:,.2f}".format(doc_amt) + " " + simbolo_de(m_doc) + ") es mayor que su saldo (" + "{:,.2f}".format(saldo) + " " + simbolo_de(m_doc) + ").")
        # Reparto por parcialidades: la pedida o, en automático, en orden
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

    # 2) Una sola transacción: la operación, sus renglones, los espejos, la transferencia, el reparto de impuestos y los saldos
    sb = []
    sb.append("DECLARE @out TABLE(FinancialOperationID BIGINT); DECLARE @outPay TABLE(DocumentPaymentID BIGINT);\nBEGIN TRY BEGIN TRAN;\n")
    sb.append("DECLARE @lk INT; EXEC @lk = sp_getapplock @Resource = 'BrosCobroFolio_" + str(mod_op) + "_" + pref + "', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;\n")
    sb.append("IF @lk < 0 THROW 50001, 'No se pudo obtener el candado del folio (otro cobro/pago en curso).', 1;\n")
    for pd in por_doc:      # el saldo se vuelve a comprobar DENTRO de la transacción: si alguien más aplicó algo mientras tanto, no se aplica de más
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
        # docDocumentPaymentEspejo.DocumentPaymentID NO es identity: espeja el mismo id recién generado
        sb.append("INSERT INTO docDocumentPaymentEspejo (DocumentPaymentID, DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber) VALUES (@payId," + str(ln["doc"]) + ",@opId,@f," + Num(m) + "," + Num(r) + "," + Num(m * r) + "," + str(ln["parc"]) + ");\n")
    if forma != 1:          # efectivo no lleva transferencia; cualquier otra forma deja su registro bancario
        sb.append("INSERT INTO docBankTransfer (FinancialOperationID, FinancialEntityID, TrackingNumber, CreatedOn, CreatedBy) VALUES (@opId," + str(cuenta) + "," + ("NULL" if tracking == "" else "N'" + Sq(tracking) + "'") + ",GETDATE()," + uid + ");\n")
    for pd in por_doc:
        doc = pd["doc"]
        prop = pd["doc_amt"] / pd["total"]
        # Reparto proporcional de impuestos: lo aplicado al documento (en su moneda) entre su total
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


# ---------- Pruebas automáticas (sin ventanas): variable de entorno BROSLMV_PAGO_TEST (JSON con el «spec»), resultado en BROSLMV_PAGO_OUT ----------
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

# ===================================================================================================================================
# VENTANA (Windows Forms desde Python con pythonnet). Es el MISMO diseño que la versión de C# (ui_pagos_winforms.cs.part), con las mismas posiciones, colores y reglas:
# cinta oscura con las acciones y la información del movimiento (fecha y folio), grupos numerados con la etiqueta arriba de cada campo (nada se encima),
#   1 · Cliente / proveedor (búsqueda por nombre o RFC; las personas con saldo salen primero) · 2 · Datos del movimiento (cuenta, forma de pago, referencia, monto con «Distribuir») ·
#   3 · Antigüedad de saldos de la persona · 4 · Documentos con saldo (marcables, con lo que se aplica a cada uno) · Resumen con lo que quedaría.
# Python corre en su propio proceso: la ventana es independiente y no bloquea a Comercial. Cada manejador va en «seguro» para que un error se explique en vez de dejar la ventana muda.
# ===================================================================================================================================
def msg(texto, titulo="Cobro o pago"):
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
C_COBRO = rgb(37, 99, 235)
C_PAGO = rgb(15, 118, 110)
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

E = {"ent": None, "guardando": False, "pintando": False, "tipo": 0, "resultado": "CANCELADO", "tc_mon": -1, "mc_prev": -1, "mc_prev_docs": -1, "ajustando": False}


def fe(s):
    try:
        return datetime.datetime.strptime(S(s)[:10], "%Y-%m-%d")
    except Exception:
        return None


def principal():
    global result
    catalogo = catalogos()
    folios = catalogo["folios"]
    seleccion = {}        # documento → monto a aplicar, SIEMPRE en la moneda de la cuenta (lo que entra o sale del banco)
    parc_sel = {}         # documento → parcialidad elegida (sin entrada = automática, en orden)
    pastillas = []
    vis = {"ent": []}
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
    frm.Text = "Cobros y pagos · BrosLMV"
    frm.ClientSize = Size(1200, 900)
    frm.MinimumSize = Size(1160, 820)
    frm.StartPosition = FormStartPosition.CenterScreen
    frm.BackColor = C_BG
    frm.Font = F_BASE
    frm.KeyPreview = True

    def al_frente(s, e):             # Python corre en otro proceso: sin esto la ventana puede quedar detrás de Comercial
        frm.TopMost = True
        frm.Activate()
        frm.BringToFront()
        frm.TopMost = False
    frm.Shown += al_frente

    def tipo_actual():
        return TIPOS[E["tipo"]]

    def es_cobro():
        return tipo_actual()["lado"] == "C"

    def acento():
        return C_COBRO if es_cobro() else C_PAGO

    def docs_lado():
        return catalogo["docsC" if es_cobro() else "docsP"]

    # ---------- piezas de construcción ----------
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
        # Marco de altura fija que dibuja el borde; el TextBox va adentro SIN borde y centrado (la altura no depende de la escala del monitor)
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

    def lista(padre, etiqueta, x, y, w):
        et(padre, etiqueta, x, y)
        c = ComboBox()
        c.Width = w
        c.DropDownStyle = ComboBoxStyle.DropDownList
        c.FlatStyle = FlatStyle.Flat
        c.Location = Point(x, y + 18 + max(0, (26 - c.Height) // 2))
        padre.Controls.Add(c)
        return c

    def dato(padre, etiqueta, x, y):
        et(padre, etiqueta, x, y)
        v = Label()
        v.Text = "0.00"
        v.Font = F_VAL
        v.ForeColor = C_TXT
        v.Location = Point(x, y + 16)
        v.Size = Size(140, 24)
        v.TextAlign = ContentAlignment.MiddleLeft
        v.BackColor = Color.Transparent
        padre.Controls.Add(v)
        return v

    def boton_plano(texto, x, y, w, h):
        b = Button()
        b.Text = texto
        b.Location = Point(x, y)
        b.Size = Size(w, h)
        b.FlatStyle = FlatStyle.Flat
        b.BackColor = Color.White
        b.ForeColor = C_TXT
        b.Cursor = Cursors.Hand
        b.FlatAppearance.BorderColor = C_LINE
        return b

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

    # ---------- cinta superior ----------
    ribbon = Panel()
    ribbon.BackColor = C_RIB
    ribbon.Size = Size(1176, 100)
    lbl_rib_titulo = Label()
    lbl_rib_titulo.Text = "Cobro a cliente"
    lbl_rib_titulo.Font = F_H2
    lbl_rib_titulo.ForeColor = C_RIB_TX
    lbl_rib_titulo.BackColor = C_RIB
    lbl_rib_titulo.Location = Point(12, 5)
    lbl_rib_titulo.AutoSize = True
    ribbon.Controls.Add(lbl_rib_titulo)
    bx = [12]
    textos_boton = {}

    def boton_cinta(icono, texto, tecla, color_icono, al_clic, clave=None):
        p = Panel()
        p.Location = Point(bx[0], 26)
        p.Size = Size(96, 68)
        p.BackColor = C_RIB
        p.Cursor = Cursors.Hand
        bx[0] += 100
        li = Label()
        li.Text = icono
        li.Font = F_ICON
        li.ForeColor = color_icono if color_icono is not None else C_RIB_TX
        li.BackColor = C_RIB
        li.AutoSize = False
        li.Size = Size(96, 34)
        li.TextAlign = ContentAlignment.MiddleCenter
        li.Location = Point(0, 0)
        lt = Label()
        lt.Text = texto
        lt.Font = F_SM
        lt.ForeColor = C_RIB_TX
        lt.BackColor = C_RIB
        lt.AutoSize = False
        lt.Size = Size(96, 18)
        lt.TextAlign = ContentAlignment.MiddleCenter
        lt.Location = Point(0, 35)
        lk = Label()
        lk.Text = tecla
        lk.Font = F_SM
        lk.ForeColor = C_RIB_MU
        lk.BackColor = C_RIB
        lk.AutoSize = False
        lk.Size = Size(96, 15)
        lk.TextAlign = ContentAlignment.MiddleCenter
        lk.Location = Point(0, 52)
        if clave:
            textos_boton[clave] = lt
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

    boton_cinta("✅", "Registrar", "F5", rgb(74, 222, 128), lambda: registrar(False), "registrar")
    boton_cinta("➕", "Registrar y nuevo", "F6", None, lambda: registrar(True))
    boton_cinta("❌", "Cancelar", "Esc", rgb(248, 113, 113), lambda: frm.Close())
    sep = Panel()
    sep.Location = Point(bx[0], 30)
    sep.Size = Size(1, 60)
    sep.BackColor = C_RIB_MU
    ribbon.Controls.Add(sep)
    bx[0] += 12
    boton_cinta("🧹", "Limpiar", "", None, lambda: limpiar())

    info = Panel()
    info.Size = Size(450, 92)
    info.BackColor = C_RIB
    ribbon.Controls.Add(info)

    def pintar_info(s, e):
        pen = Pen(C_RIB_MU)
        e.Graphics.DrawRectangle(pen, 0, 7, info.Width - 1, info.Height - 11)
        pen.Dispose()
    info.Paint += pintar_info
    t_info = Label()
    t_info.Text = "Información del movimiento"
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
    et_r("Folio", 146, 20)
    txt_folio = caja_info(146, 38, 110)
    nota = et_r("(lo asigna el sistema al registrar)", 14, 70)
    nota.ForeColor = C_RIB_MU
    lbl_tc_et = et_r("Tipo de cambio", 270, 20)
    lbl_tc_et.Visible = False
    nud_tc = NumericUpDown()
    nud_tc.Width = 120
    nud_tc.DecimalPlaces = 4
    nud_tc.Maximum = Convert.ToDecimal(99999)
    nud_tc.Minimum = Convert.ToDecimal(0)
    nud_tc.TextAlign = HorizontalAlignment.Right
    nud_tc.Location = Point(270, 38 + max(0, (26 - nud_tc.Height) // 2))
    nud_tc.Visible = False
    info.Controls.Add(nud_tc)

    # ---------- tipos ----------
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
    for i, t in enumerate(TIPOS):
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

    # ---------- 1 · persona ----------
    g1 = grupo(lambda: "1. " + ("Cliente" if es_cobro() else "Proveedor"))
    txt_ent, marco_ent = cuadro(g1, "Buscar por nombre o RFC  (F2) · las personas con saldo salen primero", 14, 34, 440)
    txt_rfc, _m = cuadro(g1, "RFC", 466, 34, 140, True)
    txt_rfc.TabStop = False
    p_chips = Panel()
    p_chips.Location = Point(14, 88)
    p_chips.Size = Size(490, 34)
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
    btn_mov = boton_plano("Movimientos", 516, 92, 90, 28)
    g1.Controls.Add(btn_mov)

    # ---------- 2 · datos del movimiento ----------
    g2 = grupo(lambda: "2. Datos del movimiento")
    cmb_cta = lista(g2, "Cuenta o caja", 14, 34, 240)
    cmb_forma = lista(g2, "Forma de pago", 266, 34, 230)
    txt_ref, _m2 = cuadro(g2, "Referencia o número de rastreo (opcional)", 14, 84, 240)
    txt_ref.MaxLength = 60
    lbl_monto_et = et(g2, "Monto recibido (opcional)", 266, 84)
    nud_monto = NumericUpDown()
    nud_monto.Width = 130
    nud_monto.DecimalPlaces = 2
    nud_monto.Maximum = Convert.ToDecimal(9999999999)
    nud_monto.Minimum = Convert.ToDecimal(0)
    nud_monto.ThousandsSeparator = True
    nud_monto.TextAlign = HorizontalAlignment.Right
    nud_monto.Location = Point(266, 102 + max(0, (26 - nud_monto.Height) // 2))
    g2.Controls.Add(nud_monto)
    btn_dist = boton_plano("Distribuir", 404, 102, 92, 26)
    g2.Controls.Add(btn_dist)
    cta_items = [{"id": c["id"], "nombre": c["nombre"], "def": c.get("def", False), "moneda": c["moneda"]} for c in catalogo["cuentas"]]
    for c in cta_items:
        cmb_cta.Items.Add(c["nombre"])
    for i, c in enumerate(cta_items):
        if c["def"]:
            cmb_cta.SelectedIndex = i
            break
    if cmb_cta.SelectedIndex < 0 and cmb_cta.Items.Count > 0:
        cmb_cta.SelectedIndex = 0
    forma_items = [{"id": f["id"], "nombre": f["nombre"]} for f in catalogo["formas"]]
    for f in forma_items:
        cmb_forma.Items.Add(f["nombre"])
    poner_por_id(cmb_forma, forma_items, 3)

    # ---------- 3 · antigüedad ----------
    g3 = grupo(lambda: "3. Antigüedad de saldos")
    p_edad = Panel()
    p_edad.Location = Point(14, 36)
    p_edad.Size = Size(1100, 40)
    p_edad.BackColor = Color.White
    g3.Controls.Add(p_edad)

    def pintar_edad(s, e):
        g = e.Graphics
        ent = E["ent"]
        if ent is None or ent["id"] not in ES:
            b = SolidBrush(C_MUTED)
            g.DrawString("Elige una persona para ver cuánto debe y desde cuándo." if ent is None else "Esta persona no tiene saldo pendiente.", F_BASE, b, 0.0, 10.0)
            b.Dispose()
            return
        a = ES[ent["id"]]
        total = max(0.01, a[0])
        nom = ["Vigente", "1-30 días", "31-60 días", "61-90 días", "Más de 90"]
        col = [C_VERDE, rgb(101, 163, 13), rgb(217, 119, 6), rgb(234, 88, 12), C_ROJO]
        w = (p_edad.Width - 4 * 10) // 5
        for i in range(5):
            x = i * (w + 10)
            b = SolidBrush(C_MUTED)
            g.DrawString(nom[i], F_SM, b, float(x), 0.0)
            b.Dispose()
            b = SolidBrush(col[i])
            g.DrawString("{:,.2f}".format(a[3 + i]), F_B, b, float(x), 14.0)
            b.Dispose()
            b = SolidBrush(rgb(229, 234, 241))
            g.FillRectangle(b, x, 34, w, 5)
            b.Dispose()
            b = SolidBrush(col[i])
            g.FillRectangle(b, x, 34, int(w * a[3 + i] / total), 5)
            b.Dispose()
    p_edad.Paint += pintar_edad
    p_edad.Resize += lambda s, e: p_edad.Invalidate()

    # ---------- 4 · documentos ----------
    g4 = grupo(lambda: "4. Documentos con saldo")
    lbl_docs_est = Label()
    lbl_docs_est.Location = Point(230, 7)
    lbl_docs_est.AutoSize = True
    lbl_docs_est.ForeColor = C_MUTED
    lbl_docs_est.Font = F_SM
    lbl_docs_est.BackColor = C_HEAD
    g4.Controls.Add(lbl_docs_est)
    xb = 14
    for texto, accion in (("Marcar todos", lambda: marcar_todos()), ("Marcar vencidos", lambda: marcar_vencidos()), ("Quitar marcas", lambda: quitar_marcas()), ("Ver detalle…", lambda: detalle_sel())):
        b = boton_plano(texto, xb, 34, 120, 28)
        b.Click += seguro(accion)
        g4.Controls.Add(b)
        xb += 126
    grid = DataGridView()
    grid.Location = Point(14, 70)
    grid.Size = Size(560, 150)
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
    c_marca = DataGridViewCheckBoxColumn()
    c_marca.Name = "Marca"
    c_marca.HeaderText = ""
    c_marca.FillWeight = 4
    grid.Columns.Add(c_marca)
    for nombre, titulo, peso in (("Doc", "DOCUMENTO", 17), ("Fecha", "FECHA", 7), ("Vence", "VENCE", 7), ("Estado", "ESTADO", 9), ("Pagos", "PAGOS APLICADOS", 17), ("Total", "TOTAL", 8), ("Saldo", "SALDO", 9), ("Parc", "PARC.", 6), ("Aplicar", "APLICAR", 10), ("Queda", "RESULTADO", 12)):
        c = DataGridViewTextBoxColumn()
        c.Name = nombre
        c.HeaderText = titulo
        c.FillWeight = peso
        c.ReadOnly = nombre != "Aplicar" and nombre != "Parc"
        grid.Columns.Add(c)
    for nombre in ("Total", "Saldo", "Aplicar"):
        grid.Columns[nombre].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        grid.Columns[nombre].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
    grid.Columns["Saldo"].DefaultCellStyle.Font = F_B
    grid.Columns["Aplicar"].DefaultCellStyle.BackColor = rgb(255, 251, 235)
    grid.Columns["Parc"].DefaultCellStyle.BackColor = rgb(255, 251, 235)
    grid.Columns["Parc"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
    grid.Columns["Parc"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
    grid.Columns["Pagos"].DefaultCellStyle.ForeColor = C_MUTED
    grid.Columns["Queda"].DefaultCellStyle.ForeColor = C_MUTED
    grid.Columns["Queda"].DefaultCellStyle.Font = F_SM
    g4.Controls.Add(grid)

    # ---------- resumen ----------
    g5 = grupo(lambda: "Resumen")
    lbl_n = dato(g5, "Documentos marcados", 14, 34)
    lbl_sal = dato(g5, "Saldo (MXN)", 174, 34)
    lbl_q = dato(g5, "Quedaría", 334, 34)
    lbl_mon = dato(g5, "Monto capturado", 494, 34)
    lbl_tot_et = Label()
    lbl_tot_et.Text = "A APLICAR"
    lbl_tot_et.Font = F_SM
    lbl_tot_et.ForeColor = C_MUTED
    lbl_tot_et.AutoSize = False
    lbl_tot_et.Size = Size(240, 16)
    lbl_tot_et.TextAlign = ContentAlignment.MiddleRight
    lbl_tot_et.Location = Point(560, 32)
    g5.Controls.Add(lbl_tot_et)
    lbl_tot = Label()
    lbl_tot.Text = "0.00"
    lbl_tot.Font = F_TOT
    lbl_tot.AutoSize = False
    lbl_tot.Size = Size(240, 36)
    lbl_tot.TextAlign = ContentAlignment.MiddleRight
    lbl_tot.Location = Point(560, 46)
    g5.Controls.Add(lbl_tot)
    et(g5, "Se registra una sola operación (un folio) con un renglón por documento y parcialidad. Lo que captures en «Aplicar» va en la moneda de la cuenta. No genera la póliza contable; pruébalo primero en una base de pruebas.", 14, 82)

    # ---------- pie ----------
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
    lst_ent = hacer_lista(560, 258, ent_l2)
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

    for c in (ribbon, p_tipos, g1, g2, g3, g4, g5, p_pie, lst_ent, lbl_toast):
        frm.Controls.Add(c)

    # ---------- distribución (todo a mano: nada se encima y se ajusta al tamaño de la ventana) ----------
    def acomodar():
        W = frm.ClientSize.Width
        H = frm.ClientSize.Height
        m = 12
        w = W - 2 * m
        ribbon.SetBounds(m, 10, w, 100)
        info.Location = Point(ribbon.Width - info.Width - 10, 6)
        ver_tipos = len(TIPOS) > 1
        p_tipos.Visible = ver_tipos      # con un solo tipo (cobro o pago, plantillas separadas) no hace falta la franja
        p_tipos.SetBounds(m, 118, w, 40)
        y = 166 if ver_tipos else 118
        g1.SetBounds(m, y, 620, 132)
        g2.SetBounds(m + 632, y, w - 632, 132)
        y += 140
        g3.SetBounds(m, y, w, 84)
        p_edad.SetBounds(14, 36, g3.Width - 28, 40)
        y += 92
        yb = H - 40 - 108 - 8
        g5.SetBounds(m, yb, w, 108)
        lbl_tot_et.Location = Point(g5.Width - 254, 32)
        lbl_tot.Location = Point(g5.Width - 254, 46)
        g4.SetBounds(m, y, w, max(170, yb - 8 - y))
        grid.SetBounds(14, 70, g4.Width - 28, g4.Height - 82)
        p_pie.SetBounds(0, H - 40, W, 40)
        x0 = max(420, lbl_pie.Right + 20)
        lbl_avisos.SetBounds(x0, 5, max(100, W - x0 - 14), 30)
        for g in (g1, g2, g3, g4, g5):
            g.Invalidate()
        p_edad.Invalidate()
    frm.Resize += lambda s, e: acomodar()

    # ---------- lógica ----------
    def hoy():
        return dt_fecha.Value.ToString("yyyy-MM-dd")

    def dias_vencido(d):
        v = fe(d["vence"]) if d["vence"] else None
        if v is None:
            return None
        h = fe(hoy())
        if h is None:
            return None
        return (h - v).days

    def docs_de_ent():
        if E["ent"] is None:
            return []
        return [d for d in docs_lado() if d["ent"] == E["ent"]["id"]]

    # ---------- moneda, tipo de cambio y parcialidades (lo mismo que la página de WebView2) ----------
    def m_cta():
        i = cmb_cta.SelectedIndex
        return cta_items[i]["moneda"] if 0 <= i < len(cta_items) else 3

    def tc():
        return Convert.ToDouble(nud_tc.Value)

    def compat(d):
        mc = m_cta()
        return d["moneda"] == mc or mc == 3 or d["moneda"] == 3

    def a_doc(d, m):
        mc = m_cta()
        if d["moneda"] == mc:
            return m
        if mc == 3:
            return m / tc() if tc() > 0 else 0.0
        return m * tc()

    def a2(d, sa):
        mc = m_cta()
        if d["moneda"] == mc:
            return round(sa, 2)
        if mc == 3:
            return round(sa * tc(), 2)
        v = sa / tc() if tc() > 0 else 0.0
        return math.floor(v * 100 + 1e-9) / 100

    def sc(d):
        return a2(d, d["saldo"])

    def val_mx(d, da):
        return da if d["moneda"] == 3 else da * d["tcDoc"]

    def moneda_fx():       # moneda extranjera que interviene (0 = todo en pesos: no se pide tipo de cambio)
        mc = m_cta()
        if mc != 3:
            return mc
        ds = docs_de_ent()
        for k in seleccion:
            d = next((x for x in ds if x["id"] == k), None)
            if d is not None and d["moneda"] != 3:
                return d["moneda"]
        l = next((x for x in ds if x["moneda"] != 3), None)
        return l["moneda"] if l is not None else 0

    def pintar_mon():
        mc = m_cta()
        fx = moneda_fx()
        sy = simbolo_de(mc)
        ver = fx != 0
        lbl_tc_et.Visible = ver
        nud_tc.Visible = ver
        lbl_monto_et.Text = ("Monto recibido" if es_cobro() else "Monto a pagar") + (" (" + sy + ")" if mc != 3 else "") + " (opcional)"
        grid.Columns["Aplicar"].HeaderText = "APLICAR " + sy
        nota.Text = ("Cuenta en " + sy + " · " + moneda_por[mc]["nombre"]) if mc != 3 and mc in moneda_por else "(lo asigna el sistema al registrar)"
        if ver:
            lbl_tc_et.Text = "Tipo de cambio " + simbolo_de(fx) + " → MXN"
            if E["tc_mon"] != fx:
                E["tc_mon"] = fx
                E["ajustando"] = True
                nud_tc.Value = Convert.ToDecimal(round(moneda_por[fx]["tc"], 4)) if fx in moneda_por else Convert.ToDecimal(1)
                E["ajustando"] = False

    def leyenda(d):        # resultado en la moneda del documento
        id_ = d["id"]
        m = seleccion.get(id_, 0.0)
        sal = d["saldo"]
        da = round(a_doc(d, m), 2)
        if abs(da - sal) <= 0.011:
            da = sal
        q = round(sal - da, 2)
        sy = d["simbolo"]
        return ("= " + "{:,.2f}".format(da) + " " + sy + " · " if d["moneda"] != m_cta() else "") + ("liquida" if q <= 0.004 else "queda " + "{:,.2f}".format(q) + " " + sy)

    def pago_texto(d):
        na = d["nAplic"]
        nn = d["nNotas"]
        pg = d["pagado"]
        tot = d["total"]
        if na + nn == 0 and pg <= 0.004:
            return "sin pagos aplicados"
        p = ["pagado " + "{:,.2f}".format(pg) + " (%d %%)" % int(round(pg / (tot if tot else 1) * 100))]
        if na > 0:
            p.append(str(na) + (" cobro" if es_cobro() else " pago") + ("s" if na > 1 else ""))
        if nn > 0:
            p.append(str(nn) + " nota" + ("s" if nn > 1 else "") + " de crédito")
        return " · ".join(p)

    def cambio_cuenta():
        mc = m_cta()
        if mc != E["mc_prev"]:
            E["mc_prev"] = mc
            E["tc_mon"] = -1
        pintar_mon()
        ds = docs_de_ent()
        for k in list(seleccion.keys()):
            d = next((x for x in ds if x["id"] == k), None)
            if d is None or not compat(d):
                seleccion.pop(k, None)
                parc_sel.pop(k, None)
            elif seleccion[k] > sc(d) or mc != E["mc_prev_docs"]:
                seleccion[k] = sc(d)
        E["mc_prev_docs"] = mc
        pintar_docs()

    def stats():           # por persona (en pesos): [pendiente, vencido, #docs, v0..v4]
        m = {}
        for d in docs_lado():
            a = m.setdefault(d["ent"], [0.0] * 8)
            s = d["saldoMx"]
            a[0] += s
            a[2] += 1
            dv = dias_vencido(d)
            if dv is not None and dv > 0:
                a[1] += s
            b = 0 if dv is None or dv <= 0 else 1 if dv <= 30 else 2 if dv <= 60 else 3 if dv <= 90 else 4
            a[3 + b] += s
        return m

    ES = stats()

    def totales():
        t = round(sum(seleccion.values()), 2)
        ent = E["ent"]
        pend = ES[ent["id"]][0] if ent is not None and ent["id"] in ES else 0.0
        mon = Convert.ToDouble(nud_monto.Value)
        mc = m_cta()
        sy = simbolo_de(mc)
        pm = 0.0
        dsel = docs_de_ent()
        for k, v in seleccion.items():
            d = next((x for x in dsel if x["id"] == k), None)
            if d is None:
                continue
            da = a_doc(d, v)
            if abs(da - d["saldo"]) <= 0.011:
                da = d["saldo"]
            pm += val_mx(d, da)
        lbl_n.Text = str(len(seleccion))
        lbl_sal.Text = "{:,.2f}".format(pend)
        lbl_q.Text = "{:,.2f}".format(max(0.0, pend - round(pm, 2)))
        lbl_mon.Text = ("{:,.2f}".format(mon) + " " + sy) if mon > 0 else "—"
        lbl_tot.Text = "{:,.2f}".format(t) + " " + sy
        lbl_tot.ForeColor = acento()
        lbl_tot_et.Text = ("A APLICAR  ·  ≈ " + "{:,.2f}".format(t * tc()) + " MXN") if mc != 3 else "A APLICAR"
        av = []
        if mon > 0 and t > 0 and abs(mon - t) > 0.005:
            av.append(("⚠ Sobran " + "{:,.2f}".format(mon - t) + " " + sy + " del monto: no se aplican (los anticipos no se manejan aquí)") if mon > t else ("⚠ Faltan " + "{:,.2f}".format(t - mon) + " " + sy + " para cubrir lo marcado"))
        if dt_fecha.Value.Date.CompareTo(DateTime.Today) > 0:
            av.append("⚠ La fecha es posterior a hoy")
        lbl_avisos.Text = "     ".join(av)

    def pintar_docs():
        E["pintando"] = True
        pintar_mon()
        grid.Rows.Clear()
        ds = docs_de_ent()
        for d in ds:
            id_ = d["id"]
            on = id_ in seleccion
            ok = compat(d)
            ext = d["moneda"] != 3
            np_ = d["nParc"]
            dv = dias_vencido(d)
            estado = "sin vencimiento" if dv is None else ("vencido %d d" % dv) if dv > 0 else ("vence en %d d" % (-dv)) if dv >= -7 else "vigente"
            fecha = fe(d["fecha"])
            vence = fe(d["vence"]) if d["vence"] else None
            r = grid.Rows.Add(on, d["tipo"] + " " + d["folio"] + ("  [" + d["simbolo"] + "]" if ext else ""), fecha.strftime("%d/%m/%Y") if fecha else "", vence.strftime("%d/%m/%Y") if vence else "—", estado, pago_texto(d),
                              "{:,.2f}".format(d["total"]), "{:,.2f}".format(d["saldo"]) + (" " + d["simbolo"] if ext else ""),
                              ("1 de 1" if np_ == 1 else "—") if np_ < 2 else (str(parc_sel[id_]) if id_ in parc_sel else "auto"),
                              (("%.2f" % seleccion[id_]) if on else "") if ok else "—", (leyenda(d) if on else "") if ok else "no aplica con una cuenta en " + simbolo_de(m_cta()))
            grid.Rows[r].Tag = id_
            grid.Rows[r].Cells["Estado"].Style.ForeColor = C_ROJO if dv is not None and dv > 0 else C_AMBAR if dv is not None and dv >= -7 else C_VERDE
            grid.Rows[r].Cells["Parc"].ReadOnly = np_ < 2 or not on
            grid.Rows[r].Cells["Aplicar"].ReadOnly = (not ok) or (not on)
            if not ok:
                grid.Rows[r].DefaultCellStyle.ForeColor = C_MUTED
            if d["nAplic"] + d["nNotas"] > 0:
                grid.Rows[r].Cells["Pagos"].Style.ForeColor = C_VERDE
        lbl_docs_est.Text = "elige primero la persona" if E["ent"] is None else "%d documento(s) con saldo · captura el monto y usa «Distribuir», o marca y ajusta «Aplicar»" % len(ds)
        E["pintando"] = False
        totales()

    def pintar_ent():
        del pastillas[:]
        ent = E["ent"]
        txt_rfc.Text = "" if ent is None else ("(sin RFC)" if not ent["rfc"] else ent["rfc"])
        btn_mov.Enabled = ent is not None
        if ent is not None:
            e = ES.get(ent["id"], [0.0] * 8)
            pastillas.append(("Saldo pendiente (MXN)", "{:,.2f}".format(e[0]), C_AMBAR if e[0] > 0 else C_TXT))
            pastillas.append(("Vencido", "{:,.2f}".format(e[1]), C_ROJO if e[1] > 0 else C_TXT))
            pastillas.append(("Documentos", "%d" % e[2], C_TXT))
            if ent["credito"] > 0:
                pastillas.append(("Límite de crédito", "{:,.2f}".format(ent["credito"]), C_TXT))
            uf = fe(ent["ultFecha"]) if ent["ultFecha"] else None
            pastillas.append(("Último cobro" if es_cobro() else "Último pago", (uf.strftime("%d/%m/%y") + " · " + "{:,.2f}".format(ent["ultMonto"])) if uf else "—", C_TXT))
        p_chips.Invalidate()
        p_edad.Invalidate()

    def elegir_ent(x):
        E["ent"] = x
        txt_ent.Text = x["nombre"]
        lst_ent.Visible = False
        seleccion.clear()
        parc_sel.clear()
        pintar_ent()
        pintar_docs()
        nud_monto.Focus()

    def cambiar_tipo(idx):
        antes = tipo_actual()["lado"]
        E["tipo"] = idx
        t = tipo_actual()
        if t["lado"] != antes:
            E["ent"] = None
            txt_ent.Text = ""
        seleccion.clear()
        parc_sel.clear()
        ES.clear()
        ES.update(stats())
        for i, b in enumerate(btns_tipo):
            on = i == idx
            col = C_COBRO if TIPOS[i]["lado"] == "C" else C_PAGO
            b.BackColor = col if on else Color.White
            b.ForeColor = Color.White if on else C_TXT
            b.FlatAppearance.BorderColor = col if on else C_LINE
            b.Font = F_B if on else F_BASE
        lbl_rib_titulo.Text = t["nombre"] + ("  ·  cuentas por cobrar, entra dinero" if es_cobro() else "  ·  cuentas por pagar, sale dinero")
        lbl_monto_et.Text = "Monto recibido (opcional)" if es_cobro() else "Monto a pagar (opcional)"
        textos_boton["registrar"].Text = "Registrar cobro" if es_cobro() else "Registrar pago"
        btn_mov.Text = "Movimientos"
        fo = folios.get(t["clave"])
        txt_folio.Text = (t["prefijo"] + "-" + str(fo)) if fo is not None else ""
        lbl_pie.Text = "Elaboró: " + (quien_soy or "—") + "      Empresa: " + nombre_empresa + "      Operación " + str(t["modOp"]) + " · folio " + t["prefijo"] + "-n"
        pintar_ent()
        pintar_docs()
        acomodar()

    def marcar_todos():
        for d in docs_de_ent():
            if compat(d):
                seleccion[d["id"]] = sc(d)
        pintar_docs()

    def marcar_vencidos():
        seleccion.clear()
        parc_sel.clear()
        for d in docs_de_ent():
            dv = dias_vencido(d)
            if compat(d) and dv is not None and dv > 0:
                seleccion[d["id"]] = sc(d)
        pintar_docs()
        if not seleccion:
            aviso("No hay documentos vencidos.")

    def quitar_marcas():
        seleccion.clear()
        parc_sel.clear()
        pintar_docs()

    def repartir():
        if E["ent"] is None:
            raise Exception("Elige primero la persona.")
        m = Convert.ToDouble(nud_monto.Value)
        if m <= 0:
            nud_monto.Focus()
            raise Exception("Captura el monto que se va a repartir.")
        seleccion.clear()
        parc_sel.clear()
        resto = round(m, 2)
        for d in sorted([x for x in docs_de_ent() if compat(x)], key=lambda x: ((x["vence"] or x["fecha"]), x["id"])):
            if resto <= 0:
                break
            ap = min(sc(d), resto)
            if ap <= 0:
                continue
            seleccion[d["id"]] = round(ap, 2)
            resto = round(resto - ap, 2)
        pintar_docs()

    # búsqueda de persona (las que tienen saldo, primero)
    def fuente_ent():
        lst = catalogo["clientes"] if es_cobro() else catalogo["proveedores"]
        return sorted(lst, key=lambda x: (-(ES[x["id"]][0] if x["id"] in ES else 0.0), x["nombre"]))

    def desplegar():
        q = txt_ent.Text.lower().split()
        vis_ = []
        for x in fuente_ent():
            h = (x["nombre"] + " " + x["rfc"] + " " + str(x["id"])).lower()
            if all(w in h for w in q):
                vis_.append(x)
                if len(vis_) >= 60:
                    break
        vis["ent"][:] = vis_
        lst_ent.Items.Clear()
        del ent_l2[:]
        for x in vis_:
            lst_ent.Items.Add(x["nombre"])
            ent_l2.append((x["rfc"] if x["rfc"] else "sin RFC") + "   ·   " + (("debe " + "{:,.2f}".format(ES[x["id"]][0]) + " en %d documento(s)" % ES[x["id"]][2]) if x["id"] in ES else "sin saldo"))
        pos = frm.PointToClient(marco_ent.Parent.PointToScreen(Point(marco_ent.Left, marco_ent.Bottom + 1)))
        lst_ent.Location = pos
        lst_ent.Height = min(280, lst_ent.Items.Count * lst_ent.ItemHeight + 4)
        lst_ent.Visible = lst_ent.Items.Count > 0
        if lst_ent.Visible:
            lst_ent.BringToFront()
            lst_ent.SelectedIndex = 0

    def elegir_de_lista():
        i = lst_ent.SelectedIndex
        if 0 <= i < len(vis["ent"]):
            elegir_ent(vis["ent"][i])

    def ent_cambio():
        if not txt_ent.Focused:
            return
        if E["ent"] is not None:
            E["ent"] = None
            seleccion.clear()
            parc_sel.clear()
            pintar_ent()
            pintar_docs()
        desplegar()
    txt_ent.TextChanged += seguro(ent_cambio)
    txt_ent.Enter += seguro(lambda: (txt_ent.SelectAll(), desplegar() if E["ent"] is None else None))

    def tecla_ent(sender, ev):
        if ev.KeyCode == Keys.Down and lst_ent.Visible:
            lst_ent.Focus()
            ev.Handled = True
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista)()
            ev.SuppressKeyPress = True
        if ev.KeyCode == Keys.Escape and lst_ent.Visible:
            lst_ent.Visible = False
            ev.SuppressKeyPress = True
            ev.Handled = True
    txt_ent.KeyDown += tecla_ent
    lst_ent.Click += seguro(elegir_de_lista)

    def tecla_lst(sender, ev):
        if ev.KeyCode == Keys.Enter:
            seguro(elegir_de_lista)()
            ev.SuppressKeyPress = True
    lst_ent.KeyDown += tecla_lst

    def fuera_ent(sender, ev):
        if not lst_ent.Focused:
            lst_ent.Visible = False
    txt_ent.Leave += fuera_ent
    nud_monto.ValueChanged += lambda s, e: totales()

    def tecla_monto(sender, ev):
        if ev.KeyCode == Keys.Enter:
            seguro(repartir)()
            ev.SuppressKeyPress = True
    nud_monto.KeyDown += tecla_monto
    btn_dist.Click += seguro(repartir)

    def cambio_fecha():
        ES.clear()
        ES.update(stats())
        pintar_ent()
        pintar_docs()
    dt_fecha.ValueChanged += seguro(cambio_fecha)
    cmb_cta.SelectedIndexChanged += seguro(lambda: cambio_cuenta() if (cmb_cta.Focused or E["mc_prev"] != m_cta()) else None)
    nud_tc.ValueChanged += seguro(lambda: pintar_docs() if not E["ajustando"] else None)

    def clic_celda(sender, ev):
        if E["pintando"] or ev.RowIndex < 0 or grid.Columns[ev.ColumnIndex].Name != "Marca":
            return
        try:
            grid.EndEdit()
            id_ = grid.Rows[ev.RowIndex].Tag
            d = next(x for x in docs_de_ent() if x["id"] == id_)
            on = bool(grid.Rows[ev.RowIndex].Cells["Marca"].EditedFormattedValue)
            if on and not compat(d):
                pintar_docs()
                aviso("Ese documento está en " + d["simbolo"] + " y la cuenta en " + simbolo_de(m_cta()) + ": no se puede aplicar aquí.", True)
                return
            if on and not sc(d) > 0:
                aviso("Captura primero el tipo de cambio.", True)
            if on:
                seleccion[id_] = sc(d)
            else:
                seleccion.pop(id_, None)
                parc_sel.pop(id_, None)
            pintar_docs()
        except Exception as ex:
            msg(str(ex))
    grid.CellContentClick += lambda s, ev: grid.BeginInvoke(System.Action(lambda: clic_celda(s, ev)))      # diferido: repintar la rejilla dentro de su propio evento da «llamada reentrante»

    def fin_edicion(sender, ev):
        col = grid.Columns[ev.ColumnIndex].Name if ev.ColumnIndex >= 0 else ""
        if E["pintando"] or ev.RowIndex < 0 or col not in ("Aplicar", "Parc"):
            return
        try:
            id_ = grid.Rows[ev.RowIndex].Tag
            d = next(x for x in docs_de_ent() if x["id"] == id_)
            if col == "Parc":        # parcialidad: número, o vacío / «auto» para ir en orden; al elegirla se propone el saldo de esa parcialidad
                try:
                    pn = int(str(grid.Rows[ev.RowIndex].Cells["Parc"].Value))
                except Exception:
                    pn = 0
                if pn < 1 or pn > d["nParc"]:
                    parc_sel.pop(id_, None)
                else:
                    parc_sel[id_] = pn
                    p = next((x for x in parcialidades_de(id_, d["saldo"]) if x["n"] == pn), None)
                    if p is not None and id_ in seleccion:
                        seleccion[id_] = min(sc(d), a2(d, p["saldo"]))
                pintar_docs()
                return
            try:
                n = float(str(grid.Rows[ev.RowIndex].Cells["Aplicar"].Value).replace(",", ""))
            except Exception:
                n = 0.0
            tope = sc(d)
            if n > tope:
                n = tope
                aviso("El importe se ajustó al saldo del documento.")
            n = max(0.0, n)
            if n > 0:
                seleccion[id_] = n
            else:
                seleccion.pop(id_, None)
                parc_sel.pop(id_, None)
            pintar_docs()
        except Exception as ex:
            msg(str(ex))
    grid.CellEndEdit += lambda s, ev: grid.BeginInvoke(System.Action(lambda: fin_edicion(s, ev)))
    grid.DataError += lambda s, ev: setattr(ev, "ThrowException", False)

    def doble_clic(sender, ev):
        if ev.RowIndex >= 0 and grid.Columns[ev.ColumnIndex].Name not in ("Marca", "Aplicar", "Parc"):
            try:
                abrir_detalle(grid.Rows[ev.RowIndex].Tag)
            except Exception as ex:
                msg(str(ex))
    grid.CellDoubleClick += doble_clic

    def detalle_sel():
        if grid.CurrentRow is None or grid.CurrentRow.Tag is None:
            aviso("Elige un documento de la lista (clic en su renglón).", True)
            return
        abrir_detalle(grid.CurrentRow.Tag)

    # parcialidades y pagos aplicados de un documento (cobros, pagos y notas de crédito): ventana aparte, también sin bloquear
    def abrir_detalle(id_):
        d = next((x for x in docs_de_ent() if x["id"] == id_), None)
        if d is None:
            return
        r = detalle_doc(id_)
        sy = r["simbolo"]
        ext = r["moneda"] != 3
        h = Form()
        h.Text = "Detalle · " + d["tipo"] + " " + d["folio"]
        h.Size = Size(780, 540)
        h.StartPosition = FormStartPosition.CenterParent
        h.BackColor = Color.White
        h.Font = F_BASE
        h.MinimumSize = Size(640, 420)
        lt = Label()
        lt.Dock = DockStyle.Top
        lt.Height = 56
        lt.Padding = Padding(12, 8, 12, 0)
        lt.ForeColor = C_TXT
        lt.Font = F_B
        lt.Text = "Total " + "{:,.2f}".format(r["total"]) + " " + sy + "     Pagado " + "{:,.2f}".format(r["pagado"]) + "     Saldo " + "{:,.2f}".format(r["saldo"]) + (
            "\nDocumento en " + sy + " · tipo de cambio del documento " + ("%g" % round(r["tcDoc"], 4)) + " · saldo ≈ " + "{:,.2f}".format(d["saldoMx"]) + " MXN" if ext else "")

        def rejilla(cols, dock, alto):
            g = DataGridView()
            g.Dock = dock
            g.Height = alto
            g.AllowUserToAddRows = False
            g.AllowUserToDeleteRows = False
            g.ReadOnly = True
            g.RowHeadersVisible = False
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            g.BackgroundColor = Color.White
            g.BorderStyle = getattr(BorderStyle, "None")
            g.EnableHeadersVisualStyles = False
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
            g.GridColor = rgb(230, 235, 242)
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            g.RowTemplate.Height = 26
            g.ColumnHeadersDefaultCellStyle.BackColor = C_HEAD
            g.ColumnHeadersDefaultCellStyle.ForeColor = C_TXT
            g.ColumnHeadersDefaultCellStyle.Font = F_B
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = C_HEAD
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = C_TXT
            g.DefaultCellStyle.SelectionBackColor = C_SEL
            g.DefaultCellStyle.SelectionForeColor = C_TXT
            for nombre, titulo, peso in cols:
                c = DataGridViewTextBoxColumn()
                c.Name = nombre
                c.HeaderText = titulo
                c.FillWeight = peso
                g.Columns.Add(c)
            return g

        g_ap = rejilla((("Fecha", "FECHA", 12), ("Tipo", "TIPO", 14), ("Folio", "FOLIO", 28), ("Parc", "PARC.", 8), ("TC", "TIPO DE CAMBIO", 14), ("Monto", "MONTO (" + sy + ")", 16)), DockStyle.Fill, 100)
        for a in r["aplic"]:
            tp = {"cobro": "Cobro", "pago": "Pago", "nota": "Nota de crédito"}.get(a["tipo"], "Otro")
            f_ = fe(a["fecha"]) if a["fecha"] else None
            fila = g_ap.Rows.Add(f_.strftime("%d/%m/%Y") if f_ else "", tp, a["folio"], str(a["parc"]), ("%g" % round(a["tc"], 4)) if ext and a["tc"] > 0 and a["tipo"] != "nota" else "", "{:,.2f}".format(a["monto"]))
            g_ap.Rows[fila].Cells["Tipo"].Style.ForeColor = C_AMBAR if a["tipo"] == "nota" else C_VERDE
        if g_ap.Rows.Count == 0:
            g_ap.Rows.Add("", "Sin cobros, pagos ni notas de crédito aplicados todavía", "", "", "", "")
        g_pa = rejilla((("N", "N.º", 8), ("Vence", "VENCE", 18), ("Importe", "IMPORTE", 18), ("Pagado", "PAGADO", 18), ("Saldo", "SALDO", 18)), DockStyle.Top, 30 + 26 * max(1, len(r["parc"])))
        for p in r["parc"]:
            v_ = fe(p["vence"]) if p["vence"] else None
            fila = g_pa.Rows.Add(str(p["n"]), v_.strftime("%d/%m/%Y") if v_ else "—", "{:,.2f}".format(p["importe"]), "{:,.2f}".format(p["pagado"]), "liquidada" if p["saldo"] <= 0.004 else "{:,.2f}".format(p["saldo"]))
            g_pa.Rows[fila].Cells["Saldo"].Style.ForeColor = C_VERDE if p["saldo"] <= 0.004 else C_ROJO if p["vence"] and p["vence"] < hoy() else C_TXT
        for gx in (g_pa, g_ap):
            for c in gx.Columns:
                if c.Name not in ("Tipo", "Folio", "Fecha"):
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                    c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
        lp = Label()
        lp.Text = "Parcialidades"
        lp.Dock = DockStyle.Top
        lp.Height = 24
        lp.Padding = Padding(12, 4, 0, 0)
        lp.ForeColor = C_MUTED
        lp.Font = F_B
        la = Label()
        la.Text = "Aplicaciones: cobros, pagos y notas de crédito"
        la.Dock = DockStyle.Top
        la.Height = 28
        la.Padding = Padding(12, 10, 0, 0)
        la.ForeColor = C_MUTED
        la.Font = F_B
        h.Controls.Add(g_ap)
        h.Controls.Add(la)
        h.Controls.Add(g_pa)
        h.Controls.Add(lp)
        h.Controls.Add(lt)
        h.Show(frm)

    # últimos movimientos de la persona: ventana aparte
    def ver_movimientos():
        ent = E["ent"]
        if ent is None:
            return
        mv = movimientos_de(ent["id"], tipo_actual()["clave"])
        h = Form()
        h.Text = ("Últimos cobros · " if es_cobro() else "Últimos pagos · ") + ent["nombre"]
        h.Size = Size(640, 330)
        h.StartPosition = FormStartPosition.CenterParent
        h.BackColor = Color.White
        h.Font = F_BASE
        h.MaximizeBox = False
        l2 = []
        lb = hacer_lista(600, 250, l2)
        lb.Dock = DockStyle.Fill
        lb.BorderStyle = getattr(BorderStyle, "None")
        for m in mv:
            f = fe(m["fecha"])
            lb.Items.Add(m["folio"] + "   ·   " + "{:,.2f}".format(m["monto"]))
            l2.append((f.strftime("%d/%m/%Y") if f else "") + "   ·   " + m["cuenta"] + "   ·   %d documento(s)" % m["docs"])
        if not mv:
            lb.Items.Add("Sin movimientos registrados")
        lb.Visible = True
        h.Controls.Add(lb)
        h.Show(frm)
    btn_mov.Click += seguro(ver_movimientos)

    def limpiar():
        E["ent"] = None
        txt_ent.Text = ""
        seleccion.clear()
        parc_sel.clear()
        txt_ref.Text = ""
        nud_monto.Value = Convert.ToDecimal(0)
        pintar_ent()
        pintar_docs()
        txt_ent.Focus()

    def refrescar_docs():          # después de aplicar (bien o mal) los saldos cambiaron
        try:
            catalogo["docsC"] = docs_con_saldo("C")
            catalogo["docsP"] = docs_con_saldo("P")
        except Exception:
            pass
        seleccion.clear()
        parc_sel.clear()
        ES.clear()
        ES.update(stats())
        pintar_ent()
        pintar_docs()

    def registrar(otro):
        if E["guardando"]:
            return
        t = tipo_actual()
        if E["ent"] is None:
            txt_ent.Focus()
            raise Exception("Elige " + ("el cliente" if es_cobro() else "el proveedor") + " de la lista (escribe y selecciona).")
        cta = seleccionado(cmb_cta, cta_items)
        if cta is None:
            raise Exception("Elige la cuenta.")
        aps = [{"doc": k, "monto": v, "parcialidad": parc_sel.get(k, 0)} for k, v in seleccion.items() if v > 0]
        if not aps:
            raise Exception("Marca al menos un documento y captura cuánto aplicar.")
        mc_r = m_cta()
        fx_r = moneda_fx()
        ds_r = docs_de_ent()
        fxs = set()
        for k in seleccion:
            dd = next((x for x in ds_r if x["id"] == k), None)
            if dd is not None and dd["moneda"] != 3 and dd["moneda"] != mc_r:
                fxs.add(dd["moneda"])
        if len(fxs) > 1:
            raise Exception("Hay documentos en monedas extranjeras distintas: regístralos por separado.")
        if fx_r != 0 and not tc() > 0:
            raise Exception("Captura el tipo de cambio.")
        forma = seleccionado(cmb_forma, forma_items)
        spec = {"tipo": t["clave"], "entidad": E["ent"]["id"], "cuenta": cta["id"], "forma": forma["id"] if forma else 0, "fecha": hoy(), "referencia": txt_ref.Text, "tc": tc() if fx_r != 0 else 0.0, "aplicaciones": aps}
        E["guardando"] = True
        frm.Cursor = Cursors.WaitCursor
        try:
            resumen = aplicar(spec)
        finally:
            E["guardando"] = False
            frm.Cursor = Cursors.Default
            refrescar_docs()
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass
        E["resultado"] = resumen
        if otro:
            titulo = resumen.split("\n")[0]
            limpiar()
            aviso(titulo)
        else:
            MessageBox.Show(resumen, "Cobro o pago registrado", MessageBoxButtons.OK, MessageBoxIcon.Information)
            frm.Close()

    def tecla_forma(sender, ev):
        if ev.KeyCode == Keys.F5:
            seguro(lambda: registrar(False))()
            ev.Handled = True
        elif ev.KeyCode == Keys.F6:
            seguro(lambda: registrar(True))()
            ev.Handled = True
        elif ev.KeyCode == Keys.F2:
            txt_ent.Focus()
            ev.Handled = True
        elif ev.KeyCode == Keys.Escape and not lst_ent.Visible:
            frm.Close()
            ev.Handled = True
    frm.KeyDown += tecla_forma

    # ---------- Arranque ----------
    E["mc_prev"] = m_cta()
    E["mc_prev_docs"] = E["mc_prev"]
    cambiar_tipo(0)
    acomodar()
    frm.ShowDialog()
    result = E["resultado"]


if not _modo_prueba:
    principal()
