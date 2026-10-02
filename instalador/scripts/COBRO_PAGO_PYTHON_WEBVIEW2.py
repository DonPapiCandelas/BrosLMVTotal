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

    def ent(tabla):
        return ("SELECT be.BusinessEntityID AS id, ISNULL(be.CommercialName, be.OfficialName) AS nombre, ISNULL(mi.OfficialNumber,'') AS rfc FROM " + tabla + " x "
                "JOIN orgBusinessEntity be ON be.BusinessEntityID = x.BusinessEntityID LEFT JOIN orgBusinessEntityMainInfo mi ON mi.BusinessEntityID = be.BusinessEntityID WHERE be.DeletedOn IS NULL ORDER BY nombre")

    def lista(tabla):
        return [{"id": I(r["id"]), "nombre": S(r["nombre"]), "rfc": S(r["rfc"])} for r in ctx.query(ent(tabla))]

    cat["clientes"] = lista("orgCustomer")
    cat["proveedores"] = lista("orgSupplier")
    cat["cuentas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query(
        "SELECT FinancialEntityID AS id, FinancialEntityName AS nombre FROM orgFinancialEntity WHERE DeletedOn IS NULL ORDER BY FinancialEntityName")]
    cat["formas"] = [{"id": I(r["id"]), "nombre": S(r["nombre"])} for r in ctx.query("SELECT ID AS id, Value AS nombre FROM vwcboCFDPaymentmethod ORDER BY CboOrder")]
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
:root{--fondo:#f4f6f9;--tarjeta:#fff;--texto:#16263a;--suave:#64748b;--linea:#d5dde8;--azul:#2d6fe0;--rojo:#c82828;--verde:#16803b;--zebra:#f8fafc;--ambar:#b45309}
*{box-sizing:border-box}body{margin:0;font:13px 'Segoe UI',Arial,sans-serif;background:var(--fondo);color:var(--texto)}
header{padding:14px 22px 4px}h1{margin:0;font-size:20px}.sub{color:var(--suave);margin-top:2px}
.aviso{margin:8px 22px 0;padding:8px 12px;background:#fdf0dc;border:1px solid #f0d3a5;border-radius:8px;color:var(--ambar);font-size:12px}
.card{background:var(--tarjeta);border:1px solid var(--linea);border-radius:9px;margin:10px 22px;padding:12px 14px}
.card h2{margin:0 0 8px;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--suave)}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px 16px}label{display:block;color:var(--suave);font-size:12px;margin-bottom:3px}
input,select,button{font:inherit;border:1px solid var(--linea);border-radius:6px;padding:6px 9px;background:#fff;color:var(--texto);width:100%}button{cursor:pointer;width:auto}button.p{background:var(--azul);color:#fff;border-color:var(--azul)}
.seg{display:flex}.seg button{border-radius:0;margin-left:-1px}.seg button:first-child{border-radius:6px 0 0 6px;margin-left:0}.seg button:last-child{border-radius:0 6px 6px 0}.seg button.on{background:var(--azul);color:#fff;border-color:var(--azul)}
.combo{position:relative}.lista{position:absolute;left:0;right:0;top:100%;z-index:20;background:#fff;border:1px solid var(--linea);border-radius:6px;max-height:240px;overflow:auto;box-shadow:0 6px 18px #0002;display:none}
.lista div{padding:6px 9px;cursor:pointer}.lista div:hover,.lista div.sel{background:#e8f0ff}.lista small{color:var(--suave)}
table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}th{font-size:11px;text-transform:uppercase;letter-spacing:.04em;color:var(--suave);text-align:right;padding:4px 6px;border-bottom:1px solid var(--linea)}th:nth-child(-n+5),td:nth-child(-n+5){text-align:left}
td{padding:3px 6px;text-align:right;border-bottom:1px solid #eef2f7}td input[type=number]{padding:4px 6px;text-align:right;width:120px}td input[type=checkbox]{width:auto}.venc{color:var(--rojo)}
.tot{display:flex;justify-content:flex-end;gap:26px;margin-top:10px}.tot b{display:block;font-size:11px;text-transform:uppercase;color:var(--suave);font-weight:600;text-align:right}.tot span{font-size:16px}
.acc{display:flex;justify-content:flex-end;gap:10px;margin:10px 22px 26px}.err{margin:0 22px;color:var(--rojo);min-height:18px}.nota{color:var(--suave);font-size:12px}.vacio{padding:24px;text-align:center;color:var(--suave)}
</style></head><body>
<header><h1 id='tit'>Cobro a cliente</h1><div class='sub'>Aplica un cobro o un pago a uno o varios documentos con saldo.</div></header>
<div class='aviso'><b>Plantilla avanzada, no nativa.</b> Comercial no ofrece una función para aplicar cobros y pagos: esta plantilla escribe directo en las tablas de Tesorería (una transacción por documento) y <b>no genera la póliza contable</b>. Pruébala primero en una base de pruebas.</div>
<div class='card'><h2>Qué vas a registrar</h2><div class='seg' id='segTipo'></div></div>
<div class='card'><h2>Datos del movimiento</h2><div class='grid'>
 <div><label id='lblEnt'>Cliente</label><div class='combo'><input id='ent' placeholder='Escribe nombre o RFC…' autocomplete='off'><div class='lista' id='lstEnt'></div></div></div>
 <div><label id='lblCta'>Cuenta donde entra el dinero</label><select id='cta'></select></div>
 <div><label>Forma de pago</label><select id='forma'></select></div>
 <div><label>Fecha</label><input type='date' id='fecha'></div>
 <div><label>Referencia o número de rastreo (opcional)</label><input id='ref' maxlength='60'></div>
</div></div>
<div class='card'><h2>Documentos con saldo</h2><div style='overflow:auto'><table><thead><tr><th></th><th>Tipo</th><th>Folio</th><th>Fecha</th><th>Vence</th><th>Total</th><th>Saldo</th><th>Aplicar</th></tr></thead><tbody id='tb'></tbody></table></div>
 <div class='tot'><div><b>Documentos marcados</b><span id='tN'>0</span></div><div><b>Total a aplicar</b><span id='tTot'>0.00</span></div></div>
 <div class='nota' style='margin-top:6px'>Marca los documentos y ajusta el monto de cada uno (por omisión, todo su saldo). Se crea una operación (folio) por documento.</div></div>
<div class='err' id='err'></div>
<div class='acc'><button onclick='enviar({accion:&quot;cancelar&quot;})'>Cancelar</button><button class='p' onclick='aplicar()'>Registrar</button></div>
<script>
var DATOS=__DATOS__;
function enviar(o){window.chrome.webview.postMessage(JSON.stringify(o));}
function esc(s){return String(s==null?'':s).replace(/[&<>']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;'}[c];});}
function f2(n){return (n||0).toLocaleString('es-MX',{minimumFractionDigits:2,maximumFractionDigits:2});}
function $(i){return document.getElementById(i);}
var CAT=DATOS.cat,TIPOS=DATOS.tipos,ST={tipo:TIPOS[0].clave,entidad:0,sel:{}};
function tipoAct(){return TIPOS.filter(function(t){return t.clave===ST.tipo;})[0];}
function norm(s){return String(s||'').toLowerCase();}
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
combo($('ent'),$('lstEnt'),listaEnt,function(x){return [x.nombre,x.rfc,x.nombre+' '+x.rfc];},function(x){ST.entidad=x.id;ST.sel={};$('ent').value=x.nombre;pintarDocs();});
$('ent').addEventListener('input',function(){ST.entidad=0;ST.sel={};pintarDocs();});
function pintarTipos(){$('segTipo').innerHTML=TIPOS.map(function(t){return '<button class=\''+(t.clave===ST.tipo?'on':'')+'\' onclick=\'setTipo(&quot;'+t.clave+'&quot;)\'>'+esc(t.nombre)+'</button>';}).join('');}
function setTipo(c){ST.tipo=c;ST.entidad=0;ST.sel={};$('ent').value='';var t=tipoAct();$('tit').textContent=t.nombre;$('lblEnt').textContent=t.lado==='C'?'Cliente':'Proveedor';
  $('lblCta').textContent=t.lado==='C'?'Cuenta donde entra el dinero':'Cuenta de donde sale el dinero';pintarTipos();pintarDocs();}
function docsDeEntidad(){var t=tipoAct();return (t.lado==='C'?CAT.docsC:CAT.docsP).filter(function(d){return d.ent===ST.entidad;});}
function pintarDocs(){
  var ds=docsDeEntidad(),hoy=$('fecha').value;
  if(!ST.entidad){$('tb').innerHTML='<tr><td colspan=8 class=vacio>Elige un '+(tipoAct().lado==='C'?'cliente':'proveedor')+' para ver sus documentos con saldo.</td></tr>';totales();return;}
  if(!ds.length){$('tb').innerHTML='<tr><td colspan=8 class=vacio>No tiene documentos con saldo pendiente.</td></tr>';totales();return;}
  $('tb').innerHTML=ds.map(function(d){var on=ST.sel[d.id]!==undefined,vencido=d.vence&&d.vence<hoy;
    return '<tr><td><input type=checkbox '+(on?'checked':'')+' onchange=\'marca('+d.id+',this.checked)\'></td><td>'+esc(d.tipo)+'</td><td>'+esc(d.folio)+(d.titulo?' <span class=nota>'+esc(d.titulo)+'</span>':'')+'</td><td>'+d.fecha+'</td><td class=\''+(vencido?'venc':'')+'\'>'+d.vence+'</td><td>'+f2(d.total)+'</td><td>'+f2(d.saldo)+'</td>'
      +'<td><input type=number step=any min=0 id=ap'+d.id+' value=\''+(on?ST.sel[d.id]:'')+'\' '+(on?'':'disabled')+' oninput=\'monto('+d.id+',this.value)\'></td></tr>';}).join('');
  totales();}
function marca(id,on){var d=docsDeEntidad().filter(function(x){return x.id===id;})[0];if(on){ST.sel[id]=d.saldo;}else{delete ST.sel[id];}var c=$('ap'+id);c.disabled=!on;c.value=on?d.saldo:'';totales();}
function monto(id,v){var d=docsDeEntidad().filter(function(x){return x.id===id;})[0];v=+v;if(isNaN(v)||v<0)v=0;if(v>d.saldo)v=d.saldo;ST.sel[id]=v;totales();}
function totales(){var n=0,t=0;for(var k in ST.sel){n++;t+=ST.sel[k];}$('tN').textContent=n;$('tTot').textContent=f2(t);}
function aplicar(){$('err').textContent='';var t=tipoAct();
  if(!ST.entidad){$('err').textContent='Elige el '+(t.lado==='C'?'cliente':'proveedor')+' de la lista (escribe y selecciona).';return;}
  if(!+$('cta').value){$('err').textContent='Elige la cuenta.';return;}
  var aps=[];for(var k in ST.sel){if(ST.sel[k]>0)aps.push({doc:+k,monto:ST.sel[k]});}
  if(!aps.length){$('err').textContent='Marca al menos un documento y captura cuánto aplicar.';return;}
  enviar({accion:'aplicar',spec:JSON.stringify({tipo:ST.tipo,entidad:ST.entidad,cuenta:+$('cta').value,forma:+$('forma').value,fecha:$('fecha').value,referencia:$('ref').value,aplicaciones:aps})});}
// ---- arranque ----
var hoy=new Date(),iso=function(d){return d.getFullYear()+'-'+('0'+(d.getMonth()+1)).slice(-2)+'-'+('0'+d.getDate()).slice(-2);};
$('fecha').value=iso(hoy);$('fecha').addEventListener('change',pintarDocs);
$('cta').innerHTML=CAT.cuentas.map(function(c){return '<option value='+c.id+'>'+esc(c.nombre)+'</option>';}).join('');
$('forma').innerHTML=CAT.formas.map(function(c){return '<option value='+c.id+(c.id===3?' selected':'')+'>'+esc(c.nombre)+'</option>';}).join('');
if(DATOS.inicial){var I=DATOS.inicial;ST.tipo=I.tipo;}
setTipo(ST.tipo);
if(DATOS.inicial){var I2=DATOS.inicial;ST.entidad=I2.entidad;var en=listaEnt().filter(function(x){return x.id===I2.entidad;})[0];$('ent').value=en?en.nombre:'';$('cta').value=I2.cuenta;$('forma').value=I2.forma;$('fecha').value=I2.fecha;$('ref').value=I2.referencia||'';
  ST.sel={};I2.aplicaciones.forEach(function(a){ST.sel[a.doc]=a.monto;});pintarDocs();}
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
