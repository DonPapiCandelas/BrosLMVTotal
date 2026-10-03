# lang: python
# timeout: 1800
# AppKey recomendado: COBRO_PAGO_PYTHON_WINFORMS
# Plantilla: Cobro a cliente / Pago a proveedor (Python · ventana Windows Forms)
# Categoria: Tesorería
# Documentacion: COBRO_PAGO.html
# ⚠ PLANTILLA AVANZADA, NO NATIVA. Registra un cobro a cliente o un pago a proveedor y lo aplica a uno o varios documentos con saldo.
# Comercial no ofrece una función para esto: la plantilla escribe directo en las tablas de Tesorería (una transacción por documento) y NO genera la póliza contable.
# Léela completa antes de usarla y pruébala primero en una base de pruebas. Documentación: clic secundario sobre la plantilla → «Ver documentación».
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
from System.Drawing import Point, Size, Color, Font, FontStyle, ContentAlignment
from System.Windows.Forms import (
    Form, FormStartPosition, Label, TextBox, ComboBox, ComboBoxStyle, Button, FlatStyle, DataGridView,
    DataGridViewTextBoxColumn, DataGridViewCheckBoxColumn, DataGridViewComboBoxColumn, DataGridViewDataErrorContexts, DataGridViewCheckBoxCell, DataGridViewContentAlignment, DataGridViewAutoSizeColumnsMode,
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
# VENTANA (Windows Forms). Python corre en su propio proceso: no hace falta modeless ni proteger a Comercial, pero cada manejador va en
# «seguro» para que un error se explique en vez de dejar la ventana muda.
# ===================================================================================================================================
def msg(texto, titulo="Cobro o pago", icono=None):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, icono if icono is not None else MessageBoxIcon.Warning)


def seguro(fn):
    def envuelto(sender=None, args=None):
        try:
            fn()
        except Exception as ex:
            msg(str(ex))
    return envuelto


def principal():
    global result
    estado = {"catalogo": catalogos(), "entidad": 0, "bloquea": False, "vis_ent": [], "docs": [], "sel": {}}
    catalogo = lambda: estado["catalogo"]

    frm = Form()
    frm.Text = "Cobro o pago"
    frm.ClientSize = Size(1080, 700)
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

    aviso = poner(Label(), 16, 10, 1048, 38)
    aviso.Text = ("PLANTILLA AVANZADA, NO NATIVA: escribe directo en las tablas de Tesorería (una transacción por documento) y no genera la póliza contable. "
                  "Pruébala primero en una base de pruebas.")
    aviso.ForeColor = Color.FromArgb(180, 83, 9)
    aviso.BackColor = Color.FromArgb(253, 240, 220)
    aviso.BorderStyle = BorderStyle.FixedSingle

    et("Qué vas a registrar", 16, 58)
    cmb_tipo = poner(ComboBox(), 16, 78, 220)
    cmb_tipo.DropDownStyle = ComboBoxStyle.DropDownList
    for t in TIPOS:
        cmb_tipo.Items.Add(t["nombre"])
    cmb_tipo.SelectedIndex = 0
    lbl_ent = et("Cliente", 256, 58)
    txt_ent = poner(TextBox(), 256, 78, 400)
    lst_ent = ListBox()
    lst_ent.Visible = False
    lst_ent.Location = Point(256, 102)
    lst_ent.Size = Size(400, 150)
    frm.Controls.Add(lst_ent)
    lbl_cta = et("Cuenta donde entra el dinero", 676, 58)
    cmb_cta = poner(ComboBox(), 676, 78, 220)
    cmb_cta.DropDownStyle = ComboBoxStyle.DropDownList
    for c in catalogo()["cuentas"]:
        cmb_cta.Items.Add(c["nombre"])
    if cmb_cta.Items.Count > 0:
        cmb_cta.SelectedIndex = 0
    et("Forma de pago", 916, 58)
    cmb_forma = poner(ComboBox(), 916, 78, 148)
    cmb_forma.DropDownStyle = ComboBoxStyle.DropDownList
    for i, f in enumerate(catalogo()["formas"]):
        cmb_forma.Items.Add(f["nombre"])
        if f["id"] == 3:
            cmb_forma.SelectedIndex = i
    if cmb_forma.SelectedIndex < 0 and cmb_forma.Items.Count > 0:
        cmb_forma.SelectedIndex = 0

    et("Fecha", 16, 112)
    dt_fecha = poner(DateTimePicker(), 16, 132, 130)
    dt_fecha.Format = DateTimePickerFormat.Short
    et("Referencia o número de rastreo (opcional)", 166, 112)
    txt_ref = poner(TextBox(), 166, 132, 490)
    txt_ref.MaxLength = 60

    et("Documentos con saldo (marca los que quieras cobrar/pagar y ajusta el monto; por omisión, todo el saldo)", 16, 166)
    grid = poner(DataGridView(), 16, 186, 1048, 420)
    grid.AllowUserToAddRows = False
    grid.AllowUserToDeleteRows = False
    grid.RowHeadersVisible = False
    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
    grid.BackgroundColor = Color.White
    grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    c_marca = DataGridViewCheckBoxColumn()
    c_marca.Name = "Marca"
    c_marca.HeaderText = ""
    c_marca.FillWeight = 4
    grid.Columns.Add(c_marca)
    for nombre, titulo, peso in (("Tipo", "Tipo", 16), ("Folio", "Folio", 14), ("Fecha", "Fecha", 9), ("Vence", "Vence", 9), ("Total", "Total", 10), ("Saldo", "Saldo", 10), ("Aplicar", "Aplicar", 12)):
        c = DataGridViewTextBoxColumn()
        c.Name = nombre
        c.HeaderText = titulo
        c.ReadOnly = nombre != "Aplicar"
        c.FillWeight = peso
        grid.Columns.Add(c)
    for nombre in ("Total", "Saldo", "Aplicar"):
        grid.Columns[nombre].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
    lbl_tot = poner(Label(), 560, 612, 504, 26)
    lbl_tot.TextAlign = ContentAlignment.MiddleRight
    lbl_tot.Font = Font("Segoe UI", 10.0, FontStyle.Bold)
    btn_aplicar = poner(Button(), 826, 656, 150, 32)
    btn_aplicar.Text = "Registrar"
    btn_aplicar.BackColor = Color.FromArgb(45, 111, 224)
    btn_aplicar.ForeColor = Color.White
    btn_aplicar.FlatStyle = FlatStyle.Flat
    btn_cancelar = poner(Button(), 986, 656, 78, 32)
    btn_cancelar.Text = "Cancelar"

    # ---------- lógica ----------
    def tipo_actual():
        return TIPOS[cmb_tipo.SelectedIndex]

    def fuente_entidad():
        return catalogo()["clientes"] if tipo_actual()["lado"] == "C" else catalogo()["proveedores"]

    def totales():
        lbl_tot.Text = "{} documento(s) marcado(s)   TOTAL A APLICAR {:,.2f}".format(len(estado["sel"]), sum(estado["sel"].values()))

    def pintar_docs():
        estado["bloquea"] = True
        grid.Rows.Clear()
        estado["docs"] = []
        if estado["entidad"]:
            hoy = dt_fecha.Value.ToString("yyyy-MM-dd")
            for d in catalogo()["docsC" if tipo_actual()["lado"] == "C" else "docsP"]:
                if d["ent"] != estado["entidad"]:
                    continue
                estado["docs"].append(d)
                on = d["id"] in estado["sel"]
                r = grid.Rows.Add(on, d["tipo"], d["folio"], d["fecha"], d["vence"], "{:,.2f}".format(d["total"]), "{:,.2f}".format(d["saldo"]), "{:.2f}".format(estado["sel"][d["id"]]) if on else "")
                if d["vence"] and d["vence"] < hoy:
                    grid.Rows[r].Cells["Vence"].Style.ForeColor = Color.FromArgb(200, 40, 40)
        estado["bloquea"] = False
        totales()

    def cambio_tipo():
        t = tipo_actual()
        lbl_ent.Text = "Cliente" if t["lado"] == "C" else "Proveedor"
        lbl_cta.Text = "Cuenta donde entra el dinero" if t["lado"] == "C" else "Cuenta de donde sale el dinero"
        estado["entidad"] = 0
        estado["bloquea"] = True
        txt_ent.Text = ""
        estado["bloquea"] = False
        estado["sel"] = {}
        pintar_docs()

    cmb_tipo.SelectedIndexChanged += seguro(cambio_tipo)
    dt_fecha.ValueChanged += seguro(pintar_docs)

    def buscar():
        q = txt_ent.Text.lower().split()
        vis = []
        for x in fuente_entidad():
            h = (x["nombre"] + " " + x["rfc"]).lower()
            if all(w in h for w in q):
                vis.append(x)
                if len(vis) >= 40:
                    break
        estado["vis_ent"] = vis
        lst_ent.Items.Clear()
        for x in vis:
            lst_ent.Items.Add(x["nombre"] + ("  [" + x["rfc"] + "]" if x["rfc"] else ""))
        lst_ent.Visible = lst_ent.Items.Count > 0
        if lst_ent.Visible:
            lst_ent.BringToFront()
            lst_ent.SelectedIndex = 0

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
        estado["sel"] = {}
        pintar_docs()

    def ent_cambio():
        if estado["bloquea"]:
            return
        estado["entidad"] = 0
        estado["sel"] = {}
        pintar_docs()
        buscar()

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

    def suciedad(sender, ev):
        if grid.IsCurrentCellDirty and grid.CurrentCell is not None and grid.CurrentCell.OwningColumn.Name == "Marca":
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit)

    grid.CurrentCellDirtyStateChanged += suciedad

    def valor_cambiado(sender, ev):
        if estado["bloquea"] or ev.RowIndex < 0 or ev.RowIndex >= len(estado["docs"]):
            return
        try:
            d = estado["docs"][ev.RowIndex]
            col = grid.Columns[ev.ColumnIndex].Name
            fila = grid.Rows[ev.RowIndex]
            estado["bloquea"] = True
            if col == "Marca":
                if bool(fila.Cells["Marca"].Value):
                    estado["sel"][d["id"]] = d["saldo"]
                    fila.Cells["Aplicar"].Value = "{:.2f}".format(d["saldo"])
                else:
                    estado["sel"].pop(d["id"], None)
                    fila.Cells["Aplicar"].Value = ""
            elif col == "Aplicar":
                if d["id"] in estado["sel"]:
                    try:
                        n = float(str(fila.Cells["Aplicar"].Value).replace(",", ""))
                    except Exception:
                        n = 0.0
                    n = max(0.0, min(n, d["saldo"]))
                    estado["sel"][d["id"]] = n
                    fila.Cells["Aplicar"].Value = "{:.2f}".format(n)
                else:
                    fila.Cells["Aplicar"].Value = ""
            estado["bloquea"] = False
            totales()
        except Exception as ex:
            estado["bloquea"] = False
            msg(str(ex))

    grid.CellValueChanged += valor_cambiado

    def registrar():
        t = tipo_actual()
        if not estado["entidad"]:
            raise Exception("Elige el " + ("cliente" if t["lado"] == "C" else "proveedor") + " de la lista (escribe y selecciona).")
        aps = [{"doc": k, "monto": v} for k, v in estado["sel"].items() if v > 0]
        if not aps:
            raise Exception("Marca al menos un documento y captura cuánto aplicar.")
        spec = {"tipo": t["clave"], "entidad": estado["entidad"], "cuenta": catalogo()["cuentas"][cmb_cta.SelectedIndex]["id"] if cmb_cta.SelectedIndex >= 0 else 0,
                "forma": catalogo()["formas"][cmb_forma.SelectedIndex]["id"] if cmb_forma.SelectedIndex >= 0 else 0, "fecha": dt_fecha.Value.ToString("yyyy-MM-dd"),
                "referencia": txt_ref.Text, "aplicaciones": aps}
        btn_aplicar.Enabled = False
        frm.Cursor = Cursors.WaitCursor
        try:
            resumen = aplicar(spec)
            try:
                ctx.erp.RefreshGrid()
            except Exception:
                pass
            msg(resumen, "Cobro o pago registrado", MessageBoxIcon.Information)
            estado["resultado"] = resumen
            frm.Close()
        except Exception:
            estado["catalogo"] = catalogos()      # si falló, los saldos pudieron cambiar
            pintar_docs()
            raise
        finally:
            btn_aplicar.Enabled = True
            frm.Cursor = Cursors.Default

    btn_aplicar.Click += seguro(registrar)
    btn_cancelar.Click += lambda s, e: frm.Close()

    def tecla_forma(sender, ev):
        if ev.KeyCode == Keys.Escape and not lst_ent.Visible:
            frm.Close()
            ev.Handled = True

    frm.KeyDown += tecla_forma

    # ---------- Arranque ----------
    cambio_tipo()
    frm.ShowDialog()
    result = estado.get("resultado", "CANCELADO")


if not _modo_prueba:
    principal()
