# lang: python
# timeout: 1800
# AppKey recomendado: COBRO_PAGO_PYTHON_WEBVIEW2
# Plantilla: Cobro a cliente / Pago a proveedor (Python · ventana HTML)
# Categoria: Tesorería
# Documentacion: COBRO_PAGO.html
# ⚠ PLANTILLA AVANZADA, NO NATIVA. Registra un cobro a cliente o un pago a proveedor y lo aplica a uno o varios documentos con saldo.
# Comercial no ofrece una función para esto: la plantilla escribe directo en las tablas de Tesorería (una transacción por documento) y NO genera la póliza contable.
# Léela completa antes de usarla y pruébala primero en una base de pruebas. Documentación: clic secundario sobre la plantilla → «Ver documentación».
#
# Qué enseña: la receta de SQL directo de siete tablas (operación financiera, aplicación, espejo, transferencia bancaria, impuestos proporcionales, nuevo saldo),
# el folio serializado con candado de transacción y la revalidación del saldo dentro de la transacción.
# Nota: el cuerpo de la página (HTML) es idéntico al de la plantilla de C#; la diferencia está solo en el lenguaje que aplica el movimiento.

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
    TP("cobro", "Cobro a cliente",  "C", 248, 1, 31, "COB"),
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

    cat["clientes"] = lista("orgCustomer")
    cat["proveedores"] = lista("orgSupplier")
    cat["cuentas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"]), "def": I(r["def"]) == 1} for r in ctx.query(
        "SELECT FinancialEntityID AS id, FinancialEntityName AS nombre, ISNULL(IsDefault,0) AS def FROM orgFinancialEntity WHERE DeletedOn IS NULL ORDER BY ISNULL(IsDefault,0) DESC, FinancialEntityName")]
    cat["formas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query("SELECT ID AS id, Value AS nombre FROM vwcboCFDPaymentmethod ORDER BY CboOrder")]
    # Folio siguiente de cada tipo (el mismo cálculo que usa aplicar)
    cat["folios"] = {t["clave"]: I(ctx.scalar("SELECT ISNULL(MAX(TRY_CONVERT(BIGINT, Folio)),0) + 1 FROM docFinancialOperation WHERE ModuleID = " + str(t["modOp"]) + " AND FolioPrefix = N'" + t["prefijo"] + "'")) for t in TIPOS}
    # Documentos con saldo pendiente de cada lado. Tope de seguridad: 30,000 por lado (los más antiguos primero, que son los que se cobran/pagan primero).
    for lado in ("C", "P"):
        mods = modulos_lado[lado]
        if not mods:
            cat["docs" + lado] = []
            continue
        filas = ctx.query(
            "SELECT TOP 30000 d.DocumentID AS id, d.ModuleID AS modulo, d.BusinessEntityID AS ent, d.FolioPrefix, d.Folio, d.DateDocument AS fecha, ISNULL(d.Total,0) AS total, ISNULL(d.Balance,0) AS saldo, ISNULL(d.Title,'') AS titulo, "
            "(SELECT MAX(a.DatePayment) FROM docDocumentPaymentAgenda a WHERE a.DocumentID = d.DocumentID AND a.DeletedOn IS NULL) AS vence "
            "FROM docDocument d WHERE d.ModuleID IN (" + ",".join(str(m) for m in mods) + ") AND d.OwnedBusinessEntityID = " + str(empresa) +
            " AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL AND ISNULL(d.Balance,0) > 0.004 ORDER BY d.DateDocument")
        cat["docs" + lado] = [{"id": I(r["id"]), "modulo": I(r["modulo"]), "tipo": nombre_mod.get(I(r["modulo"]), ""), "ent": I(r["ent"]), "folio": (S(r["FolioPrefix"]) + S(r["Folio"])).strip(),
                               "fecha": fecha_txt(r["fecha"]), "vence": fecha_txt(r["vence"]), "total": D(r["total"]), "saldo": D(r["saldo"]), "titulo": S(r["titulo"])} for r in filas]
    return cat


# ---------- Aplicar ----------
# «spec»: tipo, entidad, cuenta, forma (c_FormaPago), fecha (yyyy-MM-dd), referencia, aplicaciones [{doc, monto}]
# Una operación financiera por documento (así lo hace Tesorería). Cada documento va en su propia transacción: si uno falla, los anteriores ya quedaron aplicados y el mensaje lo dice.
# Regresa un resumen legible con los folios.
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
    fecha = S(spec.get("fecha"))
    if len(fecha) < 10:
        raise Exception("Captura la fecha.")
    f8 = Sq(fecha[:10].replace("-", ""))
    tracking = S(spec.get("referencia"))
    mod_op, recip, tipo_op, pref = t["modOp"], t["recip"], t["tipoOp"], t["prefijo"]
    uid = str(ctx.user_id)
    resumen = []
    total_aplicado = 0.0

    for a in aps:
        doc = I(a.get("doc"))
        monto = round(D(a.get("monto")), 2)
        etiqueta = "documento " + str(doc)
        try:
            d = ctx.query("SELECT Total, Balance, TotalPaid, BusinessEntityID, OwnedBusinessEntityID, CurrencyID, ModuleID, FolioPrefix, Folio FROM docDocument WHERE DocumentID=" + str(doc) + " AND DeletedOn IS NULL AND CancelledOn IS NULL")
            if not d:
                raise Exception("el documento no existe o está cancelado.")
            x = d[0]
            if I(x["ModuleID"]) in nombre_mod:
                etiqueta = nombre_mod[I(x["ModuleID"])] + " " + (S(x["FolioPrefix"]) + S(x["Folio"])).strip()
            if I(x["ModuleID"]) not in modulos_lado[lado]:
                raise Exception("no es un documento que se " + ("cobre" if lado == "C" else "pague") + ".")
            if I(x["BusinessEntityID"]) != entidad:
                raise Exception("pertenece a otro " + ("cliente" if lado == "C" else "proveedor") + ".")
            total, saldo, pagado = D(x["Total"]), D(x["Balance"]), D(x["TotalPaid"])
            if monto > saldo + 0.005:
                raise Exception("el monto (" + "{:,.2f}".format(monto) + ") es mayor que su saldo (" + "{:,.2f}".format(saldo) + ").")
            if total <= 0:
                raise Exception("tiene total cero.")
            aplicado = min(monto, saldo)
            nuevo = round(saldo - aplicado, 2)
            prop = aplicado / total
            owned, moneda = I(x["OwnedBusinessEntityID"]), I(x["CurrencyID"])
            sb = []
            sb.append("DECLARE @out TABLE(FinancialOperationID BIGINT); DECLARE @outPay TABLE(DocumentPaymentID BIGINT);\nBEGIN TRY BEGIN TRAN;\n")
            # Folio serializado: el candado se toma dentro de la transacción y se libera solo al terminar, así dos cobros simultáneos nunca calculan el mismo folio
            sb.append("DECLARE @lk INT; EXEC @lk = sp_getapplock @Resource = 'BrosCobroFolio_" + str(mod_op) + "_" + pref + "', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;\n")
            sb.append("IF @lk < 0 THROW 50001, 'No se pudo obtener el candado del folio (otro cobro/pago en curso).', 1;\n")
            # El saldo se vuelve a comprobar DENTRO de la transacción: si alguien más aplicó algo mientras tanto, no se aplica de más
            sb.append("IF (SELECT ISNULL(Balance,0) FROM docDocument WHERE DocumentID=" + str(doc) + ") < " + Num(aplicado - 0.005) + " THROW 50002, 'El saldo del documento cambió mientras se capturaba.', 1;\n")
            sb.append("DECLARE @folio BIGINT = ISNULL((SELECT MAX(TRY_CONVERT(BIGINT, Folio)) FROM docFinancialOperation WHERE ModuleID=" + str(mod_op) + " AND FolioPrefix=N'" + pref + "'),0)+1;\n")
            sb.append("DECLARE @f DATETIME = '" + f8 + "'; DECLARE @opId BIGINT, @payId BIGINT;\n")
            sb.append("INSERT INTO docFinancialOperation (ModuleID, DocRecipientID, DocumentTypeID, OwnedBusinessEntityID, BusinessEntityID, DateOperation, FinancialEntityID, Amount, CurrencyID, PaymentMethodID, PartialityNumber, PartialityTotal, DocumentID, FolioPrefix, Folio, CreatedOn, CreatedBy) OUTPUT INSERTED.FinancialOperationID INTO @out ")
            sb.append("VALUES (" + str(mod_op) + "," + str(recip) + "," + str(tipo_op) + "," + str(owned) + "," + str(entidad) + ",@f," + str(cuenta) + "," + Num(aplicado) + "," + str(moneda) + "," + str(forma) + ",1,1," + str(doc) + ",N'" + pref + "',CONVERT(NVARCHAR(50),@folio),GETDATE()," + uid + ");\n")
            sb.append("SELECT @opId = FinancialOperationID FROM @out;\n")
            sb.append("INSERT INTO docDocumentPayment (DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber, SaldoAnterior, SaldoInsoluto) OUTPUT INSERTED.DocumentPaymentID INTO @outPay VALUES (" +
                      str(doc) + ",@opId,@f," + Num(aplicado) + ",1," + Num(aplicado) + ",1," + Num(saldo) + "," + Num(nuevo) + ");\nSELECT @payId = DocumentPaymentID FROM @outPay;\n")
            # docDocumentPaymentEspejo.DocumentPaymentID NO es identity: espeja el mismo id recién generado
            sb.append("INSERT INTO docDocumentPaymentEspejo (DocumentPaymentID, DocumentID, FinancialOperationID, DateOperation, Amount, Rate, AmountPaidCurrency, PartialityNumber) VALUES (@payId," + str(doc) + ",@opId,@f," + Num(aplicado) + ",1," + Num(aplicado) + ",1);\n")
            if forma != 1:        # efectivo no lleva transferencia; cualquier otra forma deja su registro bancario
                sb.append("INSERT INTO docBankTransfer (FinancialOperationID, FinancialEntityID, TrackingNumber, CreatedOn, CreatedBy) VALUES (@opId," + str(cuenta) + "," + ("NULL" if tracking == "" else "N'" + Sq(tracking) + "'") + ",GETDATE()," + uid + ");\n")
            # Reparto proporcional de impuestos: lo aplicado de esta operación entre el total del documento
            for tx in ctx.query("SELECT DocumentTaxDetailID, DocumentItemID, TaxTypeID, Amount, TaxBase, TaxPerc, TaxName, TaxTypeName FROM docDocumentTaxDetail WHERE DocumentID=" + str(doc)):
                item = "NULL" if tx["DocumentItemID"] is None else str(I(tx["DocumentItemID"]))
                sb.append("INSERT INTO docFinancialOperationTaxDetail (DocumentTaxDetailID, FinancialOperationID, DocumentID, DocumentItemID, Proporcion, Amount, TaxTypeID, TaxName, TaxTypeName, TaxBase, TaxPerc) VALUES (" +
                          str(I(tx["DocumentTaxDetailID"])) + ",@opId," + str(doc) + "," + item + "," + Num(prop) + "," + Num(D(tx["Amount"]) * prop) + "," + str(I(tx["TaxTypeID"])) +
                          ",N'" + Sq(tx["TaxName"]) + "',N'" + Sq(tx["TaxTypeName"]) + "'," + Num(D(tx["TaxBase"]) * prop) + "," + Num(D(tx["TaxPerc"])) + ");\n")
            sb.append("UPDATE docDocument SET TotalPaid=" + Num(pagado + aplicado) + ", Balance=" + Num(nuevo) + ", StatusPaidID=" + ("1" if nuevo <= 0.0049 else "2") + " WHERE DocumentID=" + str(doc) + ";\n")
            sb.append("COMMIT TRAN;\nEND TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRAN; THROW; END CATCH;")
            ctx.execute("".join(sb))
            folio = ctx.scalar("SELECT TOP 1 Folio FROM docFinancialOperation WHERE DocumentID=" + str(doc) + " AND ModuleID=" + str(mod_op) + " ORDER BY FinancialOperationID DESC")
            total_aplicado += aplicado
            resumen.append(pref + "-" + S(folio) + " · " + etiqueta + " · " + "{:,.2f}".format(aplicado) + (" (liquidado)" if nuevo <= 0.0049 else " (queda " + "{:,.2f}".format(nuevo) + ")"))
        except Exception as ex:
            raise Exception("No se pudo aplicar a " + etiqueta + ": " + S(ex) + ("\n\nYa quedaron aplicados antes:\n" + "\n".join(resumen) if resumen else ""))
    return t["nombre"] + " registrado: " + "{:,.2f}".format(total_aplicado) + "\n" + "\n".join(resumen)


# ---------- Pruebas automáticas (sin ventanas): variable de entorno BROSLMV_PAGO_TEST (JSON con el «spec»), resultado en BROSLMV_PAGO_OUT ----------
_modo_prueba = os.environ.get("BROSLMV_PAGO_TEST")
if _modo_prueba:
    _spec = json.loads(_modo_prueba)
    if _spec.get("catalogo"):
        _res = json.dumps(catalogos(), ensure_ascii=False, default=str)
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
# VENTANA (WebView2). Al presionar «Registrar» regresa lo capturado y aquí se aplica. Si falla, la ventana se vuelve a abrir con lo capturado.
# ===================================================================================================================================
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
@media (max-width:1180px){.cinta{flex-wrap:wrap}}@media (max-width:1100px){.cuerpo{flex-direction:column;overflow:auto}.der{width:auto;flex:none}.izq{overflow:visible}}
</style></head><body>
<div class='cinta'>
 <div class='marca'><small>BROSLMV · TESORERÍA</small><b id='ttl'>Cobro a cliente</b><span id='sub'></span></div>
 <div class='acciones'>
  <button class='ab p' id='bGuardar' onclick='aplicar(false)'><i>✅</i><span id='bGTxt'>Registrar</span><em>F5</em></button>
  <button class='ab' id='bNuevo' onclick='aplicar(true)'><i>➕</i><span>Registrar y nuevo</span><em>F6</em></button>
  <div class='sep'></div>
  <button class='ab' onclick='limpiar()'><i>🧹</i><span>Limpiar</span><em>&nbsp;</em></button>
  <button class='ab' onclick='cancelar()'><i>✕</i><span>Cancelar</span><em>Esc</em></button>
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
   <div style='color:var(--suave);font-size:12px;margin-top:7px'>Si capturas el monto, «Distribuir» lo reparte entre los documentos <b>más antiguos primero</b>; también puedes marcar y ajustar cada uno a mano.</div>
  </div>
  <div class='card'><h2><b>3</b>Documentos con saldo<span class='der2' id='docsEstado'></span></h2>
   <div class='herr'><button onclick='marcarTodos()'>Marcar todos</button><button onclick='marcarVencidos()'>Marcar vencidos</button><button onclick='quitarMarcas()'>Quitar marcas</button><span class='esp'></span><button class='p' onclick='distribuir()'>Distribuir el monto (más antiguos primero)</button></div>
   <div class='tw'><table><thead><tr><th></th><th class='l'>Documento</th><th>Fecha</th><th>Vence</th><th>Estado</th><th>Total</th><th>Saldo</th><th>Aplicar</th></tr></thead><tbody id='tb'></tbody></table></div>
  </div>
 </div>
 <div class='der'>
  <div class='card res'><h2>Resumen</h2>
   <div class='tot'><span>Documentos marcados</span><span id='tN'>0</span><span>Saldo de la persona</span><span id='tSal'>0.00</span><span>Quedaría</span><span id='tQ'>0.00</span><span id='lblMon2'>Monto recibido</span><span id='tMon'>—</span><span class='g'>Total a aplicar</span><span class='g' id='tTot'>0.00</span></div>
   <div id='avisosRes'></div>
  </div>
  <div class='card' id='cardMov' style='display:none'><h2 id='hMov'>Últimos movimientos</h2><div class='hist' id='hist'></div></div>
  <div class='card' style='color:var(--suave);font-size:12px'><b style='color:var(--texto)'>Cómo funciona</b><br>Se registra <b>una operación (folio) por documento</b>, con su aplicación, su espejo, la transferencia bancaria y el reparto de impuestos, en una transacción por documento. <b>Plantilla avanzada, no nativa:</b> no genera la póliza contable (la hace el Motor de Asientos al contabilizar); pruébala primero en una base de pruebas.<br><br><span class='kbd'>F2</span> persona · <span class='kbd'>F5</span> registrar · <span class='kbd'>F6</span> registrar y nuevo · <span class='kbd'>Esc</span> cancelar</div>
 </div>
</div>
<div class='pie'><span>Elaboró: <b id='pUsr'>—</b></span><span id='pEmp'></span><span id='pMod'></span></div>
<div class='toast' id='toast'></div>
<div class='vel' id='vel'><div class='modal'><h3 id='mTit'>Registrado</h3><div id='mSub' style='color:var(--suave)'></div><pre id='mPre'></pre><div class='bt'><button onclick='cerrarVentana()'>Cerrar ventana</button><button class='p' onclick='otroMas()'>Registrar otro</button></div></div></div>
<script>
var DATOS=__DATOS__;
function enviar(o){window.chrome.webview.postMessage(JSON.stringify(o));}
function esc(s){return String(s==null?'':s).replace(/[&<>']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;'}[c];});}
function f2(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:2});}
function $(i){return document.getElementById(i);}
function norm(s){return String(s||'').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g,'');}
function fmtF(s){if(!s)return '';var p=s.split('-');return p[2]+'/'+p[1]+'/'+p[0];}
function dias(a,b){return Math.round((Date.parse(b)-Date.parse(a))/864e5);}
var CAT=DATOS.cat,TIPOS=DATOS.tipos,VIVO=!!DATOS.vivo;
var ST={tipo:TIPOS[0].clave,ent:null,sel:{},guardando:false};
function tipoAct(){return TIPOS.filter(function(t){return t.clave===ST.tipo;})[0];}
function docsLado(){return tipoAct().lado==='C'?CAT.docsC:CAT.docsP;}
function ENT(){return tipoAct().lado==='C'?CAT.clientes:CAT.proveedores;}
// ---- consultas en vivo ----
var _req=0,_pend={};
function llamar(accion,datos){return new Promise(function(ok,mal){if(!VIVO){mal(new Error('sin conexión en vivo'));return;}var id=++_req;_pend[id]=ok;enviar(Object.assign({accion:accion,req:id},datos||{}));setTimeout(function(){if(_pend[id]){delete _pend[id];mal(new Error('tiempo agotado'));}},15000);});}
function respuesta(id,datos){var f=_pend[id];if(f){delete _pend[id];f(datos);}}
var _t=0;function aviso(m,tipo){var t=$('toast');t.textContent=m;t.className='toast '+(tipo||'');t.style.display='block';clearTimeout(_t);_t=setTimeout(function(){t.style.display='none';},tipo==='mal'?8000:3500);}
// ---- estadísticas por persona (a partir de los documentos con saldo a la fecha elegida) ----
function stats(){var hoy=$('fecha').value,m={};docsLado().forEach(function(d){var e=m[d.ent]=m[d.ent]||{pend:0,venc:0,n:0,b:[0,0,0,0,0]};e.pend+=d.saldo;e.n++;var dv=d.vence?dias(d.vence,hoy):-1;if(dv>0)e.venc+=d.saldo;var i=dv<=0?0:dv<=30?1:dv<=60?2:dv<=90?3:4;e.b[i]+=d.saldo;});return m;}
var ES={};
// ---- combos ----
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
// las personas con saldo salen primero
combo($('ent'),$('lstEnt'),function(){return ENT().slice().sort(function(a,b){return ((ES[b.id]||{}).pend||0)-((ES[a.id]||{}).pend||0)||(a.nombre<b.nombre?-1:1);});},
  function(x){var e=ES[x.id];return {a:x.nombre,b:x.rfc,c:e?'debe '+f2(e.pend)+' · '+e.n+' doc.':'sin saldo',buscar:x.nombre+' '+x.rfc+' '+x.id};},elegirEnt,60,true);
$('ent').addEventListener('input',function(){if(ST.ent){ST.ent=null;ST.sel={};pintarEnt();pintarDocs();}});
// ---- tipo ----
function pintarTipos(){$('tipos').innerHTML=TIPOS.map(function(t){return '<button class=\'tipo '+(t.lado==='C'?'c':'p')+(t.clave===ST.tipo?' on':'')+'\' onclick=\'setTipo(&quot;'+t.clave+'&quot;)\'><i style=font-style:normal>'+(t.lado==='C'?'💰':'💸')+'</i>'+esc(t.nombre)+'</button>';}).join('');}
function setTipo(c){var antes=tipoAct().lado;ST.tipo=c;var t=tipoAct();if(t.lado!==antes){ST.ent=null;$('ent').value='';}ST.sel={};
  document.body.className=t.lado==='C'?'cobro':'pago';$('ttl').textContent=t.nombre;$('sub').textContent=t.lado==='C'?'Cuentas por cobrar · entra dinero':'Cuentas por pagar · sale dinero';
  $('lblEnt').textContent=t.lado==='C'?'Cliente':'Proveedor';$('lblCta').textContent=t.lado==='C'?'Entra a la cuenta':'Sale de la cuenta';$('lblMonto').textContent=t.lado==='C'?'Monto recibido (opcional)':'Monto a pagar (opcional)';$('lblMon2').textContent=t.lado==='C'?'Monto recibido':'Monto a pagar';
  $('bGTxt').textContent=t.lado==='C'?'Registrar cobro':'Registrar pago';$('pMod').innerHTML='Operación <b>'+t.modOp+'</b> · folio '+esc(t.prefijo)+'-n';
  var f=CAT.folios&&CAT.folios[t.clave];$('folio').innerHTML=(f?esc(t.prefijo)+'-'+f:'—')+'<small>lo asigna el sistema</small>';
  ES=stats();pintarTipos();pintarEnt();pintarDocs();}
// ---- persona ----
function elegirEnt(x){ST.ent=x;ST.sel={};$('ent').value=x.nombre;$('ent').classList.remove('mal');pintarEnt();pintarDocs();cargarMov();$('monto').focus();}
function pintarEnt(){var x=ST.ent,c=$('entCard'),ed=$('edades'),t=tipoAct();
  if(!x){c.style.display='none';ed.style.display='none';$('cardMov').style.display='none';return;}
  var e=ES[x.id]||{pend:0,venc:0,n:0,b:[0,0,0,0,0]};c.style.display='grid';
  c.innerHTML='<div><b>RFC</b><span>'+esc(x.rfc||'—')+'</span></div><div><b>Saldo pendiente</b><span>'+f2(e.pend)+'</span></div><div><b>Vencido</b><span style=\'color:'+(e.venc>0?'var(--rojo)':'inherit')+'\'>'+f2(e.venc)+'</span></div><div><b>Documentos</b><span>'+e.n+'</span></div>'
   +(x.credito>0?'<div><b>Límite de crédito</b><span>'+f2(x.credito)+'</span></div><div><b>Crédito disponible</b><span>'+f2(x.credito-e.pend)+'</span></div>':'')+'<div><b>'+(t.lado==='C'?'Último cobro':'Último pago')+'</b><span>'+(x.ultFecha?fmtF(x.ultFecha)+' · '+f2(x.ultMonto):'—')+'</span></div>';
  var nom=['Vigente','1-30 días','31-60 días','61-90 días','Más de 90'];ed.style.display='flex';ed.innerHTML=e.b.map(function(v,i){return '<div class=\'edad '+(i>0&&v>0?'r':'')+'\'><b>'+nom[i]+'</b><span>'+f2(v)+'</span></div>';}).join('');}
function cargarMov(){var x=ST.ent;if(!x||!VIVO)return;var t=tipoAct();llamar('movimientos',{entidad:x.id,tipo:ST.tipo}).then(function(h){if(!ST.ent||ST.ent.id!==x.id)return;$('cardMov').style.display=h.length?'':'none';$('hMov').textContent=t.lado==='C'?'Últimos cobros':'Últimos pagos';
  $('hist').innerHTML=h.map(function(m){return '<div><span><b>'+esc(m.folio)+'</b> <small>· '+fmtF(m.fecha)+(m.cuenta?' · '+esc(m.cuenta):'')+' · '+m.docs+' doc.</small></span><span>'+f2(m.monto)+'</span></div>';}).join('');}).catch(function(){});}
// ---- documentos ----
function docsDeEnt(){return ST.ent?docsLado().filter(function(d){return d.ent===ST.ent.id;}):[];}
function estadoDoc(d){var hoy=$('fecha').value;if(!d.vence)return '<span class=chip>sin vencimiento</span>';var dv=dias(d.vence,hoy);return dv>0?'<span class=\'chip r\'>vencido '+dv+' d</span>':dv>=-7?'<span class=\'chip a\'>vence en '+(-dv)+' d</span>':'<span class=\'chip v\'>vigente</span>';}
function pintarDocs(){var ds=docsDeEnt(),t=tipoAct();
  if(!ST.ent){$('tb').innerHTML='<tr><td colspan=8><div class=vacioP><b>Elige '+(t.lado==='C'?'un cliente':'un proveedor')+'</b>Escribe su nombre o RFC (F2): verás sus documentos con saldo para cobrar o pagar.</div></td></tr>';$('docsEstado').textContent='';totales();return;}
  if(!ds.length){$('tb').innerHTML='<tr><td colspan=8><div class=vacioP><b>Sin saldo pendiente</b>Esta persona no tiene documentos con saldo.</div></td></tr>';$('docsEstado').textContent='';totales();return;}
  $('docsEstado').textContent=ds.length+' documento(s)';
  $('tb').innerHTML=ds.map(function(d){var on=ST.sel[d.id]!==undefined;
    return '<tr class=\''+(on?'on':'')+'\'><td><input type=checkbox '+(on?'checked':'')+' onchange=\'marca('+d.id+',this.checked)\'></td><td class=l><b>'+esc(d.tipo)+' '+esc(d.folio)+'</b>'+(d.titulo?'<small>'+esc(d.titulo)+'</small>':'')+'</td><td>'+fmtF(d.fecha)+'</td><td>'+(d.vence?fmtF(d.vence):'—')+'</td><td>'+estadoDoc(d)+'</td><td>'+f2(d.total)+'</td><td><b>'+f2(d.saldo)+'</b></td>'
      +'<td><input type=number step=any min=0 id=ap'+d.id+' value=\''+(on?ST.sel[d.id]:'')+'\' '+(on?'':'disabled')+' oninput=\'monto('+d.id+',this.value)\'></td></tr>';}).join('');
  totales();}
function docPor(id){return docsDeEnt().filter(function(x){return x.id===id;})[0];}
function marca(id,on){var d=docPor(id);if(on)ST.sel[id]=d.saldo;else delete ST.sel[id];pintarDocs();}
function monto(id,v){var d=docPor(id);v=+v;if(isNaN(v)||v<0)v=0;if(v>d.saldo)v=d.saldo;ST.sel[id]=v;totales();}
function marcarTodos(){docsDeEnt().forEach(function(d){ST.sel[d.id]=d.saldo;});pintarDocs();}
function marcarVencidos(){var hoy=$('fecha').value;ST.sel={};docsDeEnt().forEach(function(d){if(d.vence&&d.vence<hoy)ST.sel[d.id]=d.saldo;});pintarDocs();if(!Object.keys(ST.sel).length)aviso('No hay documentos vencidos.');}
function quitarMarcas(){ST.sel={};pintarDocs();}
function distribuir(){var m=+$('monto').value;if(!ST.ent){aviso('Elige primero la persona.','mal');return;}if(!(m>0)){$('monto').classList.add('mal');$('monto').focus();aviso('Captura el monto que se va a repartir.','mal');return;}$('monto').classList.remove('mal');
  var ds=docsDeEnt().slice().sort(function(a,b){var x=a.vence||a.fecha,y=b.vence||b.fecha;return x<y?-1:x>y?1:a.id-b.id;});ST.sel={};var resto=Math.round(m*100)/100;
  ds.forEach(function(d){if(resto<=0)return;var ap=Math.min(d.saldo,resto);ST.sel[d.id]=Math.round(ap*100)/100;resto=Math.round((resto-ap)*100)/100;});pintarDocs();}
function totales(){var n=0,t=0;for(var k in ST.sel){n++;t+=ST.sel[k];}t=Math.round(t*100)/100;var e=ST.ent?(ES[ST.ent.id]||{pend:0}):{pend:0},m=+$('monto').value;
  $('tN').textContent=n;$('tTot').textContent=f2(t);$('tSal').textContent=f2(e.pend);$('tQ').textContent=f2(e.pend-t);$('tMon').textContent=m>0?f2(m):'—';
  var av='';if(m>0&&t>0&&Math.abs(m-t)>0.005)av+='<div class=\'aviso '+(m>t?'a':'r')+'\'>'+(m>t?'Sobran '+f2(m-t)+' del monto: no se aplican (los anticipos no se manejan aquí).':'Faltan '+f2(t-m)+' para cubrir lo marcado.')+'</div>';
  if($('fecha').value>iso(new Date()))av+='<div class=\'aviso a\'>La fecha es posterior a hoy.</div>';
  $('avisosRes').innerHTML=av;}
// ---- registrar ----
function validar(){var t=tipoAct();document.querySelectorAll('.mal').forEach(function(e){e.classList.remove('mal');});
  if(!ST.ent){$('ent').classList.add('mal');$('ent').focus();aviso('Elige '+(t.lado==='C'?'el cliente':'el proveedor')+' de la lista (escribe y selecciona).','mal');return false;}
  if(!+$('cta').value){$('cta').classList.add('mal');aviso('Elige la cuenta.','mal');return false;}
  var hay=false;for(var k in ST.sel){if(ST.sel[k]>0)hay=true;}if(!hay){aviso('Marca al menos un documento y captura cuánto aplicar.','mal');return false;}
  return true;}
function aplicar(otro){if(ST.guardando||!validar())return;var t=tipoAct();ST.guardando=true;$('bGuardar').disabled=true;$('bNuevo').disabled=true;$('bGTxt').textContent='Registrando…';
  var aps=[];for(var k in ST.sel){if(ST.sel[k]>0)aps.push({doc:+k,monto:ST.sel[k]});}
  enviar({accion:'aplicar',nuevo:!!otro&&VIVO,spec:JSON.stringify({tipo:ST.tipo,entidad:ST.ent.id,cuenta:+$('cta').value,forma:+$('forma').value,fecha:$('fecha').value,referencia:$('ref').value,aplicaciones:aps})});}
function liberar(){ST.guardando=false;$('bGuardar').disabled=false;$('bNuevo').disabled=false;var t=tipoAct();$('bGTxt').textContent=t.lado==='C'?'Registrar cobro':'Registrar pago';}
function refrescarDocs(){return llamar('docs',{lado:tipoAct().lado}).then(function(l){if(tipoAct().lado==='C')CAT.docsC=l;else CAT.docsP=l;ES=stats();ST.sel={};pintarEnt();pintarDocs();cargarMov();}).catch(function(){});}
function falloAplicar(m){liberar();aviso(m.split('\n')[0],'mal');var a=document.createElement('div');a.className='aviso r';a.style.whiteSpace='pre-wrap';a.textContent=m;$('avisosRes').insertBefore(a,$('avisosRes').firstChild);refrescarDocs();}
function aplicado(i){liberar();var lineas=String(i.resumen||'').split('\n'),titulo=lineas.shift();
  if(i.nuevo){aviso(titulo,'bien');limpiarPersona();refrescarDocs();$('ent').focus();return;}
  $('mTit').textContent=titulo;$('mSub').textContent='Se creó una operación por documento:';$('mPre').textContent=lineas.join('\n');$('vel').className='vel on';refrescarDocs();}
function cerrarVentana(){enviar({accion:'cancelar'});}
function otroMas(){$('vel').className='vel';limpiarPersona();$('ent').focus();}
function limpiarPersona(){ST.ent=null;ST.sel={};$('ent').value='';$('ref').value='';$('monto').value='';pintarEnt();pintarDocs();$('avisosRes').innerHTML='';}
function limpiar(){limpiarPersona();$('ent').focus();}
function cancelar(){if(Object.keys(ST.sel).length&&!confirm('Hay documentos marcados. ¿Cerrar sin registrar?'))return;enviar({accion:'cancelar'});}
document.addEventListener('keydown',function(e){if(e.key==='F5'){e.preventDefault();aplicar(false);}else if(e.key==='F6'){e.preventDefault();aplicar(true);}else if(e.key==='F2'){e.preventDefault();$('ent').focus();}else if(e.key==='Escape'){if($('vel').className.indexOf('on')>=0)return;var ab=document.querySelector('.lista[style*=block]');if(!ab)cancelar();}});
// ---- arranque ----
var iso=function(d){return d.getFullYear()+'-'+('0'+(d.getMonth()+1)).slice(-2)+'-'+('0'+d.getDate()).slice(-2);};
$('fecha').value=iso(new Date());$('fecha').addEventListener('change',function(){ES=stats();pintarEnt();pintarDocs();});$('monto').addEventListener('input',totales);
$('cta').innerHTML=CAT.cuentas.map(function(c){return '<option value='+c.id+'>'+esc(c.nombre)+'</option>';}).join('');var cd=CAT.cuentas.filter(function(c){return c.def;})[0];if(cd)$('cta').value=cd.id;
$('forma').innerHTML=CAT.formas.map(function(c){return '<option value='+c.id+(c.id===3?' selected':'')+'>'+esc(c.nombre)+'</option>';}).join('');
$('pUsr').textContent=DATOS.usuario||'—';$('pEmp').innerHTML=DATOS.empresa?'Empresa <b>'+esc(DATOS.empresa)+'</b>':'';if(!VIVO)$('bNuevo').style.display='none';
if(DATOS.inicial){var I=DATOS.inicial;ST.tipo=I.tipo;}
setTipo(ST.tipo);
if(DATOS.inicial){var I2=DATOS.inicial;var en=ENT().filter(function(x){return x.id===I2.entidad;})[0];if(en){ST.ent=en;$('ent').value=en.nombre;}$('cta').value=I2.cuenta;$('forma').value=I2.forma;$('fecha').value=I2.fecha;$('ref').value=I2.referencia||'';
  ST.sel={};I2.aplicaciones.forEach(function(a){ST.sel[a.doc]=a.monto;});ES=stats();pintarEnt();pintarDocs();}
setTimeout(function(){$('ent').focus();},60);
</script></body></html>
'''


def principal():
    global result
    catalogo = catalogos()
    inicial = None
    barra = chr(92)
    html_prueba = os.environ.get("BROSLMV_PAGO_HTML")   # pruebas: escribe la página (con sus datos) en un archivo y no abre ventana
    if html_prueba:
        d0 = {"tipos": TIPOS, "cat": catalogo, "inicial": None}
        with open(html_prueba, "w", encoding="utf-8") as f:
            f.write(PAGINA.replace("__DATOS__", json.dumps(d0, ensure_ascii=False, default=str).replace("</", "<" + barra + "/")))
        result = "HTML"
        return
    while True:
        datos = {"tipos": TIPOS, "cat": catalogo, "inicial": inicial}
        json_txt = json.dumps(datos, ensure_ascii=False, default=str).replace("</", "<" + barra + "/")
        r = ctx.show_html_formulario(PAGINA.replace("__DATOS__", json_txt), "Cobro o pago", 1180, 900, 1800000)
        if not r.get("submitted") or r.get("accion") != "aplicar":
            result = "CANCELADO"
            return
        spec = json.loads(r["spec"])
        try:
            resumen = aplicar(spec)
            try:
                ctx.erp.RefreshGrid()
            except Exception:
                pass
            ctx.msg(resumen, "Cobro o pago registrado")
            result = resumen
            return
        except Exception as ex:
            ctx.msg(str(ex), "Cobro o pago")
            inicial = spec
            catalogo = catalogos()          # los saldos pudieron cambiar


if not _modo_prueba:
    principal()
