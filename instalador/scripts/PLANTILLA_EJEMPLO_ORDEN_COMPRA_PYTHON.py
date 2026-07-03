# lang: python
# timeout: 1800
#
# PLANTILLA_EJEMPLO_ORDEN_COMPRA_PYTHON.py
# La MISMA ventana que PLANTILLA_EJEMPLO_ORDEN_COMPRA_CSHARP.ctx (Orden de Compra,
# modulo 183), pero en Python puro (pythonnet). Ver esa plantilla y MANUAL.md #10
# ("Ventanas WinForms: modeless") para la explicacion completa del patron.
#
# Diferencia frente a "Ejemplo Premium - Python WinForms" (Requisicion, modulo 1040):
# una Orden de Compra SI compromete un PRECIO por partida (es un acuerdo real con el
# proveedor) y captura la FECHA DE ENTREGA esperada. No afecta inventario (modulo 183;
# eso pasa hasta la Recepcion de Compra, modulo 184) -- por eso no se llama AffectStockNEW.

import pythonnet
pythonnet.load("netfx")

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System import Decimal
from System.Drawing import (
    Point, Size, Color, Font, FontStyle, SolidBrush, Pen, RectangleF, ContentAlignment,
)
from System.Windows.Forms import (
    Form, FormStartPosition, Panel, Label, TextBox, ComboBox, ComboBoxStyle,
    NumericUpDown, Button, FlatStyle, DataGridView, DataGridViewTextBoxColumn,
    DataGridViewContentAlignment, DataGridViewAutoSizeColumnMode, DataGridViewHeaderBorderStyle,
    DataGridViewCellBorderStyle, DataGridViewSelectionMode, DateTimePicker, DateTimePickerFormat,
    ListBox, DrawMode, DrawItemState, BorderStyle, ScrollBars, Cursors, Keys, MessageBox,
    MessageBoxButtons, MessageBoxIcon, DialogResult,
)

from broslmv import ctx

System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA)

BORDER_NONE = getattr(BorderStyle, "None")
HEADER_BORDER_NONE = getattr(DataGridViewHeaderBorderStyle, "None")


def msg(texto, titulo="BrosLMV"):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Information)


def confirmar(texto, titulo="Confirmar"):
    r = MessageBox.Show(texto, titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
    return r == DialogResult.Yes


# ═══════════════════ DATOS INICIALES ═══════════════════
almacenes = ctx.query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName")
proveedores = ctx.query("""
    SELECT be.BusinessEntityID, be.OfficialName,
           ISNULL(m.OfficialNumber,'') AS RFC
    FROM orgBusinessEntity be
    INNER JOIN orgSupplier s ON s.BusinessEntityID=be.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID=be.BusinessEntityID
    WHERE be.DeletedOn IS NULL
    ORDER BY be.OfficialName
""")
monedas = ctx.query("SELECT CurrencyID, IntlSymbol, Currency, Rate FROM vwLBSCurrencyList ORDER BY CurrencyID")
condiciones = ctx.query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID")
# Catalogo de impuestos (misma vista que usa LBS para el combo/listado nativo), con su
# porcentaje real (vwLBSTaxPerc) para poder calcular el total con impuestos aqui mismo.
impuestos = ctx.query("""
    SELECT t.TaxTypeID, t.TaxTypeName, ISNULL(tp.IVA_Perc,0) AS IVA_Perc
    FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID
    ORDER BY t.TaxTypeName
""")

partidas = []
prov_all = []
cur_rate = {}
tax_perc_by_type = {}
moneda_palabra = {}
producto_seleccionado = None
suspender_busqueda = [False]
suspender_prov = [False]
match_items = []


class Opt:
    def __init__(self, txt, val, extra=""):
        self.Txt = txt
        self.Val = val
        self.Extra = extra or ""

    def __str__(self):
        return self.Txt


class Item:
    def __init__(self, pid, key, desc, unit, qty, precio, tax_type_id=0, tax_label="", descuento_perc=0.0, tax_perc=0.0):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.Qty = qty
        self.Precio = precio
        self.TaxTypeId = tax_type_id
        self.TaxLabel = tax_label
        self.DescuentoPerc = descuento_perc  # fraccion (0.05 = 5%)
        self.TaxPerc = tax_perc  # fraccion (0.16 = 16%), para calcular el total con impuestos


class Prod:
    def __init__(self, pid, key, desc, unit, stock, tax_type_id=0):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.Stock = stock
        self.TaxTypeId = tax_type_id


# ═══════════════════ COLORES / FUENTES ═══════════════════
C_BG      = Color.FromArgb(241, 245, 249)
C_BORDER  = Color.FromArgb(203, 213, 225)
C_PRIMARY = Color.FromArgb(37, 99, 235)   # azul: distingue de la Requisicion (verde)
C_DANGER  = Color.FromArgb(239, 68, 68)
C_TEXT    = Color.FromArgb(30, 41, 59)
C_MUTED   = Color.FromArgb(100, 116, 139)
C_PANEL   = Color.White
C_HEADER  = Color.FromArgb(248, 250, 252)
C_BLUE_SEL = Color.FromArgb(238, 242, 255)
C_RIBBON    = Color.FromArgb(51, 65, 85)
C_RIBBON_TX = Color.FromArgb(226, 232, 240)
C_RIBBON_MU = Color.FromArgb(148, 163, 184)

F_TAB     = Font("Segoe UI", 10.0, FontStyle.Bold)
F_H2      = Font("Segoe UI", 9.0, FontStyle.Bold)
F_BODY    = Font("Segoe UI", 9.0)
F_SM      = Font("Segoe UI", 8.5)
F_ICON    = Font("Segoe UI Emoji", 20.0)
F_ICON_SM = Font("Segoe UI Emoji", 10.0)

# ═══════════════════ VENTANA ═══════════════════
frm = Form()
frm.Text = "Nueva Orden de Compra (Python)"
frm.Size = Size(1100, 900)
frm.StartPosition = FormStartPosition.CenterScreen
frm.BackColor = C_BG
frm.KeyPreview = True


def lbl(texto, x, y, parent, font=F_BODY):
    l = Label()
    l.Text = texto
    l.Location = Point(x, y)
    l.Font = font
    l.AutoSize = True
    l.Parent = parent
    return l


def create_group(title, x, y, w, h, parent):
    p = Panel()
    p.Location = Point(x, y)
    p.Size = Size(w, h)
    p.BackColor = C_PANEL
    p.Parent = parent

    def on_paint(sender, ev):
        g = ev.Graphics
        g.DrawRectangle(Pen(C_BORDER), 0, 0, w - 1, h - 1)
        g.FillRectangle(SolidBrush(C_HEADER), 1, 1, w - 2, 28)
        g.DrawLine(Pen(C_BORDER), 0, 29, w, 29)
        g.DrawString(title, F_H2, SolidBrush(C_TEXT), 10.0, 5.0)

    p.Paint += on_paint
    return p


def col(grid, name, header, width, read_only, align=None, fmt=None):
    c = DataGridViewTextBoxColumn()
    c.Name = name
    c.HeaderText = header
    c.Width = width
    c.ReadOnly = read_only
    if align is not None:
        c.DefaultCellStyle.Alignment = align
    if fmt is not None:
        c.DefaultCellStyle.Format = fmt
    grid.Columns.Add(c)
    return c


def _sel_default(cbo, items_paralelos, val):
    for i, (_, v) in enumerate(items_paralelos):
        if v == val:
            cbo.SelectedIndex = i
            return
    if cbo.Items.Count > 0:
        cbo.SelectedIndex = 0


# ═══════════════════ CINTA SUPERIOR (RIBBON) ═══════════════════
ribbon = Panel()
ribbon.Location = Point(10, 10)
ribbon.Size = Size(1060, 96)
ribbon.BackColor = C_RIBBON
ribbon.Parent = frm

lbl_ribbon_title = Label()
lbl_ribbon_title.Text = "Principal"
lbl_ribbon_title.Font = F_TAB
lbl_ribbon_title.ForeColor = C_RIBBON_TX
lbl_ribbon_title.BackColor = C_RIBBON
lbl_ribbon_title.Location = Point(12, 4)
lbl_ribbon_title.AutoSize = True
lbl_ribbon_title.Parent = ribbon

_current_x = [12]
tool_buttons = {}


def add_toolbar_btn(key, icon, text, icon_color=None):
    p = Panel()
    p.Location = Point(_current_x[0], 24)
    p.Size = Size(78, 66)
    p.BackColor = C_RIBBON
    p.Cursor = Cursors.Hand
    p.Parent = ribbon

    lbl_icon = Label()
    lbl_icon.Text = icon
    lbl_icon.Font = F_ICON
    lbl_icon.ForeColor = icon_color if icon_color else C_RIBBON_TX
    lbl_icon.AutoSize = False
    lbl_icon.Size = Size(78, 34)
    lbl_icon.TextAlign = ContentAlignment.MiddleCenter
    lbl_icon.BackColor = C_RIBBON
    lbl_icon.Parent = p

    lbl_txt = Label()
    lbl_txt.Text = text
    lbl_txt.Font = Font("Segoe UI", 8.0)
    lbl_txt.ForeColor = C_RIBBON_TX
    lbl_txt.AutoSize = False
    lbl_txt.Size = Size(78, 30)
    lbl_txt.Location = Point(0, 34)
    lbl_txt.TextAlign = ContentAlignment.TopCenter
    lbl_txt.BackColor = C_RIBBON
    lbl_txt.Parent = p

    def on_enter(sender, ev):
        p.BackColor = Color.FromArgb(71, 85, 105)
        lbl_icon.BackColor = p.BackColor
        lbl_txt.BackColor = p.BackColor

    def on_leave(sender, ev):
        p.BackColor = C_RIBBON
        lbl_icon.BackColor = C_RIBBON
        lbl_txt.BackColor = C_RIBBON

    p.MouseEnter += on_enter
    p.MouseLeave += on_leave

    tool_buttons[key] = p
    _current_x[0] += 80
    return p


def add_sep():
    s = Panel()
    s.Location = Point(_current_x[0], 28)
    s.Size = Size(1, 58)
    s.BackColor = C_RIBBON_MU
    s.Parent = ribbon
    _current_x[0] += 12


add_toolbar_btn("guardar",  "\U0001F4BE", "Guardar\nF5",       Color.FromArgb(96, 165, 250))
add_toolbar_btn("nueva",    "➕", "Guardar y\nNueva")
add_toolbar_btn("cancelar", "❌", "Cancelar\nEsc",      Color.FromArgb(248, 113, 113))
add_sep()
add_toolbar_btn("preview",  "\U0001F50E", "Vista\nprevia")
add_toolbar_btn("imprimir", "\U0001F5A8", "Imprimir")
add_sep()
add_toolbar_btn("limpiar",  "\U0001F9F9", "Limpiar\ncampos")

info_pnl = Panel()
info_pnl.Location = Point(650, 8)
info_pnl.Size = Size(402, 80)
info_pnl.BackColor = C_RIBBON
info_pnl.Parent = ribbon


def _info_paint(sender, ev):
    ev.Graphics.DrawRectangle(Pen(C_RIBBON_MU), 0, 6, info_pnl.Width - 1, info_pnl.Height - 10)


info_pnl.Paint += _info_paint

lbl_info_title = Label()
lbl_info_title.Text = "Información del documento"
lbl_info_title.Font = Font("Segoe UI", 8.0)
lbl_info_title.ForeColor = C_RIBBON_MU
lbl_info_title.BackColor = C_RIBBON
lbl_info_title.Location = Point(8, 0)
lbl_info_title.AutoSize = True
lbl_info_title.Parent = info_pnl

lbl_fecha = Label()
lbl_fecha.Text = "Fecha:"
lbl_fecha.Font = F_BODY
lbl_fecha.ForeColor = C_RIBBON_TX
lbl_fecha.BackColor = C_RIBBON
lbl_fecha.Location = Point(12, 24)
lbl_fecha.AutoSize = True
lbl_fecha.Parent = info_pnl

dt_fecha = DateTimePicker()
dt_fecha.Location = Point(60, 21)
dt_fecha.Size = Size(100, 23)
dt_fecha.Format = DateTimePickerFormat.Short
dt_fecha.Font = F_BODY
dt_fecha.Parent = info_pnl

lbl_serie = Label()
lbl_serie.Text = "Serie:"
lbl_serie.Font = F_BODY
lbl_serie.ForeColor = C_RIBBON_TX
lbl_serie.BackColor = C_RIBBON
lbl_serie.Location = Point(172, 24)
lbl_serie.AutoSize = True
lbl_serie.Parent = info_pnl

txt_serie = TextBox()
txt_serie.Location = Point(212, 21)
txt_serie.Size = Size(90, 23)
txt_serie.Font = F_BODY
txt_serie.Parent = info_pnl

lbl_folio = Label()
lbl_folio.Text = "Folio:"
lbl_folio.Font = F_BODY
lbl_folio.ForeColor = C_RIBBON_TX
lbl_folio.BackColor = C_RIBBON
lbl_folio.Location = Point(12, 52)
lbl_folio.AutoSize = True
lbl_folio.Parent = info_pnl

txt_folio = TextBox()
txt_folio.Location = Point(60, 49)
txt_folio.Size = Size(100, 23)
txt_folio.Font = F_BODY
txt_folio.Parent = info_pnl

lbl_folio_nota = Label()
lbl_folio_nota.Text = "(consecutivo, editable)"
lbl_folio_nota.Font = F_SM
lbl_folio_nota.ForeColor = C_RIBBON_MU
lbl_folio_nota.BackColor = C_RIBBON
lbl_folio_nota.Location = Point(172, 52)
lbl_folio_nota.AutoSize = True
lbl_folio_nota.Parent = info_pnl

lbl_entrega = Label()
lbl_entrega.Text = "Entrega esp.:"
lbl_entrega.Font = F_BODY
lbl_entrega.ForeColor = C_RIBBON_TX
lbl_entrega.BackColor = C_RIBBON
lbl_entrega.Location = Point(312, 24)
lbl_entrega.AutoSize = True
lbl_entrega.Parent = info_pnl

dt_entrega = DateTimePicker()
dt_entrega.Location = Point(312, 45)
dt_entrega.Size = Size(85, 23)
dt_entrega.Format = DateTimePickerFormat.Short
dt_entrega.Value = System.DateTime.Today.AddDays(7)
dt_entrega.Font = F_BODY
dt_entrega.Parent = info_pnl

top_y = [116]

# ═══════════════════ 1. PROVEEDOR ═══════════════════
grp_prov = create_group("1. Proveedor", 10, top_y[0], 650, 100, frm)
lbl("Buscar (RFC/Nombre):", 15, 40, grp_prov)

cbo_prov = ComboBox()
cbo_prov.Location = Point(150, 38)
cbo_prov.Size = Size(280, 23)
cbo_prov.Font = F_BODY
cbo_prov.DropDownStyle = ComboBoxStyle.DropDown
cbo_prov.FlatStyle = FlatStyle.Flat
cbo_prov.Parent = grp_prov

for p in proveedores:
    prov_all.append(Opt(str(p["OfficialName"]), int(p["BusinessEntityID"]), str(p["RFC"] or "")))
prov_filtered = list(prov_all)
for o in prov_filtered:
    cbo_prov.Items.Add(o.Txt)
if cbo_prov.Items.Count > 0:
    cbo_prov.SelectedIndex = 0

lbl("RFC:", 15, 70, grp_prov)
txt_rfc = TextBox()
txt_rfc.Text = ""
txt_rfc.Location = Point(150, 68)
txt_rfc.Size = Size(280, 23)
txt_rfc.ReadOnly = True
txt_rfc.Font = F_BODY
txt_rfc.ForeColor = C_MUTED
txt_rfc.Parent = grp_prov

lbl("Moneda:", 440, 40, grp_prov)
cbo_moneda = ComboBox()
cbo_moneda.Location = Point(510, 38)
cbo_moneda.Size = Size(130, 23)
cbo_moneda.Font = F_BODY
cbo_moneda.DropDownStyle = ComboBoxStyle.DropDownList
cbo_moneda.FlatStyle = FlatStyle.Flat
cbo_moneda.Parent = grp_prov
moneda_items = []
for m in monedas:
    cid = int(m["CurrencyID"])
    cur_rate[cid] = float(m["Rate"] or 1)
    label = str(m["IntlSymbol"]) + " - " + str(m["Currency"])
    moneda_items.append((label, cid))
    cbo_moneda.Items.Add(label)
    cur_upper = str(m["Currency"] or "").upper()
    if "PESO" in cur_upper:
        palabra = "PESOS"
    elif "DOLAR" in cur_upper or "DÓLAR" in cur_upper:
        palabra = "DÓLARES"
    elif "EURO" in cur_upper:
        palabra = "EUROS"
    else:
        palabra = (cur_upper + "S") if cur_upper else "PESOS"
    moneda_palabra[cid] = palabra
_sel_default(cbo_moneda, moneda_items, 3)

lbl("Cond. pago:", 440, 70, grp_prov)
cbo_cond = ComboBox()
cbo_cond.Location = Point(510, 68)
cbo_cond.Size = Size(130, 23)
cbo_cond.Font = F_BODY
cbo_cond.DropDownStyle = ComboBoxStyle.DropDownList
cbo_cond.FlatStyle = FlatStyle.Flat
cbo_cond.Parent = grp_prov
cond_items = []
for c in condiciones:
    label = str(c["PaymentTermName"])
    cond_items.append((label, int(c["PaymentTermID"])))
    cbo_cond.Items.Add(label)
_sel_default(cbo_cond, cond_items, 1)

# ═══════════════════ 2. ALMACEN Y CENTRO DE COSTO ═══════════════════
grp_alm = create_group("2. Almacén y Centro de Costo", 670, top_y[0], 400, 100, frm)
lbl("Almacén:", 15, 40, grp_alm)
cbo_dep = ComboBox()
cbo_dep.Location = Point(120, 38)
cbo_dep.Size = Size(260, 23)
cbo_dep.Font = F_BODY
cbo_dep.DropDownStyle = ComboBoxStyle.DropDownList
cbo_dep.FlatStyle = FlatStyle.Flat
cbo_dep.Parent = grp_alm
dep_items = []
for d in almacenes:
    label = str(d["DepotName"])
    dep_items.append((label, int(d["DepotID"])))
    cbo_dep.Items.Add(label)
if cbo_dep.Items.Count > 0:
    cbo_dep.SelectedIndex = 0

lbl("Centro Costo:", 15, 70, grp_alm)
cbo_cc = ComboBox()
cbo_cc.Location = Point(120, 68)
cbo_cc.Size = Size(260, 23)
cbo_cc.Font = F_BODY
cbo_cc.FlatStyle = FlatStyle.Flat
cbo_cc.Parent = grp_alm
cbo_cc.Items.Add("Ninguno")
cbo_cc.SelectedIndex = 0

top_y[0] += 110

# ═══════════════════ 3. COMENTARIOS ═══════════════════
grp_obs = create_group("3. Comentarios del Documento", 10, top_y[0], 1060, 75, frm)
txt_obs = TextBox()
txt_obs.Multiline = True
txt_obs.Location = Point(15, 35)
txt_obs.Size = Size(1030, 30)
txt_obs.Font = F_BODY
txt_obs.ScrollBars = ScrollBars.Vertical
txt_obs.Parent = grp_obs

top_y[0] += 85

# ═══════════════════ 4. PARTIDAS (con PRECIO, IMPUESTO y DESCUENTO) ═══════════════════
grp_part = create_group("4. Partidas", 10, top_y[0], 1060, 350, frm)

txt_producto = TextBox()
txt_producto.Location = Point(15, 35)
txt_producto.Size = Size(230, 23)
txt_producto.Font = F_BODY
txt_producto.Parent = grp_part

btn_buscar_prod = Button()
btn_buscar_prod.Text = "\U0001F50D"
btn_buscar_prod.Location = Point(250, 34)
btn_buscar_prod.Size = Size(28, 25)
btn_buscar_prod.Font = F_ICON_SM
btn_buscar_prod.FlatStyle = FlatStyle.Flat
btn_buscar_prod.Parent = grp_part
btn_buscar_prod.FlatAppearance.BorderColor = C_BORDER

lbl("Cantidad:", 290, 38, grp_part)
nud_cantidad = NumericUpDown()
nud_cantidad.Value = Decimal(1)
nud_cantidad.DecimalPlaces = 4
nud_cantidad.Maximum = Decimal(999999999)
nud_cantidad.Location = Point(350, 35)
nud_cantidad.Size = Size(75, 23)
nud_cantidad.Font = F_BODY
nud_cantidad.Parent = grp_part

lbl("Precio unit.:", 435, 38, grp_part)
nud_precio = NumericUpDown()
nud_precio.Value = Decimal(0)
nud_precio.DecimalPlaces = 2
nud_precio.Maximum = Decimal(999999999)
nud_precio.Location = Point(510, 35)
nud_precio.Size = Size(90, 23)
nud_precio.Font = F_BODY
nud_precio.Parent = grp_part

btn_agregar = Button()
btn_agregar.Text = "➕  Agregar"
btn_agregar.Location = Point(610, 34)
btn_agregar.Size = Size(90, 26)
btn_agregar.BackColor = C_PRIMARY
btn_agregar.ForeColor = Color.White
btn_agregar.FlatStyle = FlatStyle.Flat
btn_agregar.Font = F_BODY
btn_agregar.Parent = grp_part
btn_agregar.FlatAppearance.BorderSize = 0

btn_eliminar = Button()
btn_eliminar.Text = "❌  Eliminar"
btn_eliminar.Location = Point(710, 34)
btn_eliminar.Size = Size(90, 26)
btn_eliminar.FlatStyle = FlatStyle.Flat
btn_eliminar.ForeColor = C_DANGER
btn_eliminar.Font = F_BODY
btn_eliminar.Parent = grp_part
btn_eliminar.FlatAppearance.BorderColor = C_BORDER

# --- Segunda fila: Impuesto (precargado del producto, pero se puede cambiar) + Descuento % ---
# NOTA (pythonnet): DropDownList con objetos Python en Items pierde la seleccion al
# crear el control nativo (ver PLANTILLA_EJEMPLO_PYTHON_WINFORMS.py) -- por eso aqui
# tambien Items lleva solo strings, con impuesto_items como lista paralela.
lbl("Impuesto:", 15, 67, grp_part)
cbo_impuesto = ComboBox()
cbo_impuesto.Location = Point(80, 64)
cbo_impuesto.Size = Size(160, 23)
cbo_impuesto.Font = F_BODY
cbo_impuesto.DropDownStyle = ComboBoxStyle.DropDownList
cbo_impuesto.FlatStyle = FlatStyle.Flat
cbo_impuesto.Parent = grp_part
impuesto_items = []  # [(label, TaxTypeID), ...] paralelo a cbo_impuesto.Items
for t in impuestos:
    label = str(t["TaxTypeName"])
    ttid = int(t["TaxTypeID"])
    impuesto_items.append((label, ttid))
    tax_perc_by_type[ttid] = float(t["IVA_Perc"] or 0)
    cbo_impuesto.Items.Add(label)

lbl("Descuento %:", 255, 67, grp_part)
nud_descuento = NumericUpDown()
nud_descuento.Value = Decimal(0)
nud_descuento.DecimalPlaces = 2
nud_descuento.Maximum = Decimal(100)
nud_descuento.Location = Point(335, 64)
nud_descuento.Size = Size(65, 23)
nud_descuento.Font = F_BODY
nud_descuento.Parent = grp_part

lbl_total_partidas = lbl("Total de partidas: 0", 415, 68, grp_part, F_SM)
lbl_total_partidas.ForeColor = C_MUTED

lbl_subtotal = lbl("Subtotal estimado: $0.00", 850, 64, grp_part, F_H2)

grid = DataGridView()
grid.Location = Point(15, 100)
grid.Size = Size(1030, 205)
grid.BackgroundColor = C_PANEL
grid.BorderStyle = BORDER_NONE
grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
grid.ColumnHeadersBorderStyle = HEADER_BORDER_NONE
grid.EnableHeadersVisualStyles = False
grid.RowHeadersVisible = False
grid.AllowUserToAddRows = False
grid.AllowUserToDeleteRows = False
grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
grid.MultiSelect = False
grid.GridColor = C_BORDER
grid.Parent = grp_part
grid.ColumnHeadersDefaultCellStyle.BackColor = C_HEADER
grid.ColumnHeadersDefaultCellStyle.ForeColor = C_TEXT
grid.ColumnHeadersDefaultCellStyle.Font = F_H2
grid.ColumnHeadersHeight = 28

col(grid, "PID", "PID", 0, True).Visible = False
col(grid, "Key", "CLAVE", 100, True)
c_desc = col(grid, "Desc", "DESCRIPCIÓN", 250, True)
c_desc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
col(grid, "Unit", "U.M.", 60, True)
col(grid, "Qty", "CANTIDAD", 80, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Precio", "PRECIO", 90, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Impuesto", "IMPUESTO", 110, True)
col(grid, "DescPerc", "DESC. %", 70, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Importe", "IMPORTE", 100, True, DataGridViewContentAlignment.MiddleRight, "N2")
grid.DefaultCellStyle.SelectionBackColor = C_BLUE_SEL
grid.DefaultCellStyle.SelectionForeColor = C_TEXT

pnl_empty = Panel()
pnl_empty.Location = Point(300, 150)
pnl_empty.Size = Size(430, 100)
pnl_empty.BackColor = C_PANEL
pnl_empty.Parent = grp_part

lbl_empty_icon = Label()
lbl_empty_icon.Text = "\U0001F4E6"
lbl_empty_icon.Font = Font("Segoe UI Emoji", 40.0)
lbl_empty_icon.AutoSize = True
lbl_empty_icon.Location = Point(40, 20)
lbl_empty_icon.ForeColor = C_MUTED
lbl_empty_icon.Parent = pnl_empty

lbl_empty_1 = Label()
lbl_empty_1.Text = "No hay partidas agregadas"
lbl_empty_1.Font = Font("Segoe UI", 12.0, FontStyle.Bold)
lbl_empty_1.AutoSize = True
lbl_empty_1.Location = Point(120, 30)
lbl_empty_1.ForeColor = C_TEXT
lbl_empty_1.Parent = pnl_empty

lbl_empty_2 = Label()
lbl_empty_2.Text = "Busque y agregue productos para comenzar."
lbl_empty_2.Font = F_BODY
lbl_empty_2.AutoSize = True
lbl_empty_2.Location = Point(120, 55)
lbl_empty_2.ForeColor = C_MUTED
lbl_empty_2.Parent = pnl_empty

pnl_drop = Panel()
pnl_drop.Visible = False
pnl_drop.BackColor = C_BORDER
pnl_drop.Parent = frm

lst_match = ListBox()
lst_match.Visible = True
lst_match.Font = F_BODY
lst_match.BorderStyle = BORDER_NONE
lst_match.IntegralHeight = False
lst_match.BackColor = C_PANEL
lst_match.ForeColor = C_TEXT
lst_match.DrawMode = DrawMode.OwnerDrawFixed
lst_match.ItemHeight = 45
lst_match.Parent = pnl_drop
lst_match.Location = Point(1, 1)


def _resize_lst(sender, ev):
    lst_match.Size = Size(pnl_drop.Width - 2, pnl_drop.Height - 2)


pnl_drop.Resize += _resize_lst

lbl_hint_detalle = lbl("(doble clic en una partida para ver el detalle del producto)", 15, 308, grp_part, F_SM)
lbl_hint_detalle.ForeColor = C_MUTED

top_y[0] += 360

# ═══════════════════ 5. TOTALES ═══════════════════
grp_totales = create_group("5. Totales", 10, top_y[0], 1060, 90, frm)
lbl("Subtotal:", 15, 38, grp_totales)
lbl_t_subtotal = Label()
lbl_t_subtotal.Text = "$0.00"
lbl_t_subtotal.Font = F_BODY
lbl_t_subtotal.AutoSize = False
lbl_t_subtotal.Size = Size(150, 20)
lbl_t_subtotal.TextAlign = ContentAlignment.MiddleRight
lbl_t_subtotal.Location = Point(95, 36)
lbl_t_subtotal.Parent = grp_totales

lbl("Descuento:", 270, 38, grp_totales)
lbl_t_descuento = Label()
lbl_t_descuento.Text = "-$0.00"
lbl_t_descuento.Font = F_BODY
lbl_t_descuento.ForeColor = C_DANGER
lbl_t_descuento.AutoSize = False
lbl_t_descuento.Size = Size(150, 20)
lbl_t_descuento.TextAlign = ContentAlignment.MiddleRight
lbl_t_descuento.Location = Point(355, 36)
lbl_t_descuento.Parent = grp_totales

lbl("Impuestos:", 525, 38, grp_totales)
lbl_t_impuestos = Label()
lbl_t_impuestos.Text = "$0.00"
lbl_t_impuestos.Font = F_BODY
lbl_t_impuestos.AutoSize = False
lbl_t_impuestos.Size = Size(150, 20)
lbl_t_impuestos.TextAlign = ContentAlignment.MiddleRight
lbl_t_impuestos.Location = Point(605, 36)
lbl_t_impuestos.Parent = grp_totales

lbl_total_cap = Label()
lbl_total_cap.Text = "TOTAL:"
lbl_total_cap.Font = F_H2
lbl_total_cap.AutoSize = True
lbl_total_cap.Location = Point(775, 35)
lbl_total_cap.Parent = grp_totales

lbl_t_total = Label()
lbl_t_total.Text = "$0.00"
lbl_t_total.Font = Font("Segoe UI", 13.0, FontStyle.Bold)
lbl_t_total.ForeColor = C_PRIMARY
lbl_t_total.AutoSize = False
lbl_t_total.Size = Size(190, 26)
lbl_t_total.TextAlign = ContentAlignment.MiddleRight
lbl_t_total.Location = Point(850, 32)
lbl_t_total.Parent = grp_totales

lbl("Son:", 15, 66, grp_totales)
lbl_total_letra = Label()
lbl_total_letra.Text = "CERO PESOS 00/100 M.N."
lbl_total_letra.Font = Font("Segoe UI", 8.5, FontStyle.Italic)
lbl_total_letra.ForeColor = C_MUTED
lbl_total_letra.AutoSize = True
lbl_total_letra.Location = Point(55, 67)
lbl_total_letra.Parent = grp_totales

# ═══════════════════ 6. FOOTER ═══════════════════
footer_y = frm.ClientSize.Height - 44
pnl_footer = Panel()
pnl_footer.Location = Point(0, footer_y)
pnl_footer.Size = Size(1100, 44)
pnl_footer.BackColor = C_PANEL
pnl_footer.Parent = frm


def _footer_paint(sender, ev):
    ev.Graphics.DrawLine(Pen(C_BORDER), 0, 0, 1100, 0)


pnl_footer.Paint += _footer_paint
lbl("Elaboró:", 20, 13, pnl_footer)
try:
    elaboro = str(ctx.erp.UserName())
except Exception:
    elaboro = ""
txt_elaboro = TextBox()
txt_elaboro.Text = elaboro
txt_elaboro.Location = Point(80, 11)
txt_elaboro.Size = Size(200, 23)
txt_elaboro.ReadOnly = True
txt_elaboro.Font = F_BODY
txt_elaboro.Parent = pnl_footer

lbl_footer_nota = Label()
lbl_footer_nota.Text = "Mod. 183  Orden de Compra  ·  no afecta inventario  ·  Usa \U0001F4BE Guardar (F5)."
lbl_footer_nota.Location = Point(300, 13)
lbl_footer_nota.AutoSize = True
lbl_footer_nota.Font = F_BODY
lbl_footer_nota.ForeColor = C_MUTED
lbl_footer_nota.Parent = pnl_footer

# ═══════════════════ LÓGICA DE DIBUJO DE LA LISTA ═══════════════════
def _draw_item(sender, ev):
    if ev.Index < 0 or ev.Index >= len(match_items):
        return
    p = match_items[ev.Index]
    sel = bool(ev.State & DrawItemState.Selected)
    ev.Graphics.FillRectangle(SolidBrush(C_BLUE_SEL if sel else C_PANEL), ev.Bounds)
    if p is None:
        return
    r = ev.Bounds
    ev.Graphics.DrawString(p.Key, Font("Segoe UI", 9.0, FontStyle.Bold), SolidBrush(C_TEXT), float(r.Left + 10), float(r.Top + 6))
    ev.Graphics.DrawString(p.Desc, F_BODY, SolidBrush(C_TEXT),
                            RectangleF(float(r.Left + 120), float(r.Top + 6), float(max(180, r.Width - 130)), 18.0))
    ev.Graphics.DrawString("Unidad " + p.Unit + " · Exist: " + ("%.2f" % p.Stock), F_SM, SolidBrush(C_MUTED),
                            float(r.Left + 120), float(r.Top + 24))


lst_match.DrawItem += _draw_item


# ═══════════════════ LÓGICA CORE ═══════════════════
def depot_sel():
    i = cbo_dep.SelectedIndex
    return dep_items[i][1] if 0 <= i < len(dep_items) else 1


def _prov_opt_seleccionado():
    i = cbo_prov.SelectedIndex
    if 0 <= i < len(prov_filtered):
        return prov_filtered[i]
    t = (cbo_prov.Text or "").strip().lower()
    return next((o for o in prov_all if o.Txt.lower() == t), None)


def prov_sel():
    o = _prov_opt_seleccionado()
    return o.Val if o is not None else 0


def actualizar_proveedor():
    o = _prov_opt_seleccionado()
    txt_rfc.Text = (o.Extra if o.Extra else "(sin RFC)") if o is not None else ""


def precargar_folio():
    try:
        pre = ctx.erp.GetFolioPrefix(183, depot_sel())
        fol = ctx.erp.GetNextFolio(183, pre, depot_sel())
        txt_serie.Text = pre or ""
        txt_folio.Text = fol or ""
    except Exception:
        txt_serie.Text = "OC"
        txt_folio.Text = ""


def ocultar_resultados():
    pnl_drop.Visible = False


def buscar(query):
    global match_items
    q = (query or "").strip()
    if len(q) == 0:
        match_items = []
        lst_match.Items.Clear()
        ocultar_resultados()
        return

    filtro = "" if q == "#" else "AND (p.ProductKey LIKE @f OR p.ProductName LIKE @f) "
    limite = "TOP 300" if q == "#" else "TOP 80"
    params = {} if q == "#" else {"f": "%" + q + "%"}
    params["dep"] = depot_sel()

    # Aqui un error no puede tumbar Comercial (Python corre en su propio proceso),
    # pero sin este try/except la ventana se quedaria "muda" sin explicar por que.
    try:
        rows = ctx.query(
            "SELECT " + limite + " p.ProductID, p.ProductKey, p.ProductName, p.Unit, p.TaxTypeID, "
            "ISNULL((SELECT SUM(Quantity) FROM orgProductKardex k WHERE k.ProductID=p.ProductID "
            "AND k.DepotID=@dep AND k.Cancelled=0),0) AS Stock "
            "FROM orgProduct p WHERE p.DeletedOn IS NULL AND p.TaxTypeID IS NOT NULL AND p.TaxTypeID > 0 "
            + filtro + "ORDER BY p.ProductName",
            params,
        )
    except Exception as ex:
        msg("Error al buscar productos: " + str(ex), "Error")
        ocultar_resultados()
        return

    match_items = []
    lst_match.Items.Clear()
    for r in rows:
        p = Prod(int(r["ProductID"]), str(r["ProductKey"]), str(r["ProductName"]),
                  str(r["Unit"]), float(r["Stock"] or 0), int(r["TaxTypeID"] or 0))
        match_items.append(p)
        lst_match.Items.Add(p.Key)

    if lst_match.Items.Count == 0:
        ocultar_resultados()
        msg("No se encontraron productos con esa búsqueda.")
        return

    lst_match.SelectedIndex = 0
    alto = min(lst_match.Items.Count * lst_match.ItemHeight + 4, 280)
    pnl_drop.SetBounds(grp_part.Left + txt_producto.Left, grp_part.Top + txt_producto.Bottom + 2, 600, alto)
    pnl_drop.Visible = True
    pnl_drop.BringToFront()


def seleccionar_producto():
    global producto_seleccionado
    i = lst_match.SelectedIndex
    if not (0 <= i < len(match_items)):
        return
    p = match_items[i]
    producto_seleccionado = p
    suspender_busqueda[0] = True
    txt_producto.Text = p.Key + " — " + p.Desc
    txt_producto.SelectionStart = txt_producto.TextLength
    suspender_busqueda[0] = False
    # Precarga el impuesto del catalogo de productos; el usuario lo puede cambiar
    # antes de agregar.
    idx = next((i2 for i2, (_, v) in enumerate(impuesto_items) if v == p.TaxTypeId), 0)
    if cbo_impuesto.Items.Count > 0:
        cbo_impuesto.SelectedIndex = idx
    ocultar_resultados()
    nud_cantidad.Focus()


def refresh_grid():
    grid.Rows.Clear()
    subtotal = 0.0
    descuento_total = 0.0
    impuestos_total = 0.0
    for it in partidas:
        # Importe = cantidad x precio (igual que el nativo: Total en docDocumentItem NO
        # resta el descuento; el descuento se aplica aparte al recalcular el documento).
        importe = it.Qty * it.Precio
        desc_monto = importe * it.DescuentoPerc
        neto = importe - desc_monto
        imp_monto = neto * it.TaxPerc
        subtotal += importe
        descuento_total += desc_monto
        impuestos_total += imp_monto
        grid.Rows.Add(it.PID, it.Key, it.Desc, it.Unit, it.Qty, it.Precio, it.TaxLabel, it.DescuentoPerc * 100, importe)
    gran_total = subtotal - descuento_total + impuestos_total
    pnl_empty.Visible = len(partidas) == 0
    lbl_total_partidas.Text = "Total de partidas: " + str(len(partidas))
    lbl_subtotal.Text = "Subtotal estimado: $%.2f" % subtotal

    lbl_t_subtotal.Text = "$%.2f" % subtotal
    lbl_t_descuento.Text = "-$%.2f" % descuento_total
    lbl_t_impuestos.Text = "$%.2f" % impuestos_total
    lbl_t_total.Text = "$%.2f" % gran_total
    mi = cbo_moneda.SelectedIndex
    moneda_id = moneda_items[mi][1] if 0 <= mi < len(moneda_items) else 3
    palabra = moneda_palabra.get(moneda_id, "PESOS")
    sufijo = " M.N." if moneda_id == 3 else ""
    lbl_total_letra.Text = "SON: " + numero_a_letras(gran_total, palabra) + sufijo


def limpiar_captura():
    global producto_seleccionado
    producto_seleccionado = None
    suspender_busqueda[0] = True
    txt_producto.Clear()
    suspender_busqueda[0] = False
    nud_cantidad.Value = Decimal(1)
    nud_precio.Value = Decimal(0)
    nud_descuento.Value = Decimal(0)
    txt_producto.Focus()


def agregar_producto():
    global producto_seleccionado
    if producto_seleccionado is None:
        if pnl_drop.Visible and lst_match.SelectedItem is not None:
            seleccionar_producto()
        else:
            msg("Busca y selecciona un producto.")
            txt_producto.Focus()
            return

    cant = float(str(nud_cantidad.Value))
    precio = float(str(nud_precio.Value))
    descuento = float(str(nud_descuento.Value)) / 100.0  # fraccion (5% -> 0.05)
    idx_imp = cbo_impuesto.SelectedIndex
    if 0 <= idx_imp < len(impuesto_items):
        tax_label, tax_type_id = impuesto_items[idx_imp]
    else:
        tax_label, tax_type_id = "", 0
    tax_perc = tax_perc_by_type.get(tax_type_id, 0.0)
    p = producto_seleccionado
    existente = next((z for z in partidas if z.PID == p.PID), None)
    if existente is not None:
        existente.Qty += cant
        if precio > 0:
            existente.Precio = precio
        existente.TaxTypeId = tax_type_id
        existente.TaxLabel = tax_label
        existente.DescuentoPerc = descuento
        existente.TaxPerc = tax_perc
    else:
        partidas.append(Item(p.PID, p.Key, p.Desc, p.Unit, cant, precio, tax_type_id, tax_label, descuento, tax_perc))

    refresh_grid()
    limpiar_captura()


def limpiar_todo():
    partidas.clear()
    refresh_grid()
    limpiar_captura()
    txt_obs.Clear()
    precargar_folio()


# ═══════════════════ CREAR EN CONTPAQI ═══════════════════
def crear_orden_compra(nueva):
    be = prov_sel()
    if be == 0:
        msg("Selecciona un proveedor.")
        return
    if len(partidas) == 0:
        msg("Agrega al menos un producto.")
        return
    if any(p.Precio <= 0 for p in partidas) and not confirmar(
            "Hay partidas con precio en $0.00. ¿Continuar de todos modos?"):
        return

    supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=@be", {"be": be})
    if supplier_id is None:
        msg("El proveedor seleccionado no tiene registro de proveedor (orgSupplier).", "Error")
        return

    if not confirmar("¿Crear orden de compra con " + str(len(partidas)) +
                      (" producto?" if len(partidas) == 1 else " productos?")):
        return

    frm.Cursor = Cursors.WaitCursor
    tool_buttons["guardar"].Enabled = False
    try:
        dep = depot_sel()
        mi, ci = cbo_moneda.SelectedIndex, cbo_cond.SelectedIndex
        moneda_id = moneda_items[mi][1] if 0 <= mi < len(moneda_items) else 3
        cond_id = cond_items[ci][1] if 0 <= ci < len(cond_items) else 0
        rate = cur_rate.get(moneda_id, 1.0)

        doc = ctx.erp.NuevoDocumento(183, dep, be)  # 183 = Orden de Compra
        if doc is None or doc <= 0 or ctx.erp.LastError():
            raise Exception("NuevoDocumento: " + str(ctx.erp.LastError()))

        ctx.execute(
            "UPDATE docDocument SET DepotIDFrom=0, UserID=0, "
            "CurrencyID=@cur, Rate=@rate, PaymentTermID=@cond, "
            "DateDocument=@fecha, DateDelivery=@entrega, Comments=@obs WHERE DocumentID=@doc",
            {"cur": moneda_id, "rate": rate, "cond": cond_id,
             "fecha": dt_fecha.Value.ToString("yyyyMMdd"), "entrega": dt_entrega.Value.ToString("yyyyMMdd"),
             "obs": txt_obs.Text or "", "doc": doc},
        )

        folio_manual = (txt_folio.Text or "").strip()
        if len(folio_manual) > 0:
            ctx.execute("UPDATE docDocument SET Folio=@f WHERE DocumentID=@doc", {"f": folio_manual, "doc": doc})

        for it in partidas:
            # taxTypeIdOverride: el impuesto elegido en la ventana (por defecto el del
            # catalogo, pero pudo cambiarse). descuentoPerc ya viene en fraccion.
            ctx.erp.AgregarArticulo(doc, it.PID, it.Qty, it.Precio, -1, it.TaxTypeId, it.DescuentoPerc)
            if ctx.erp.LastError():
                raise Exception("AgregarArticulo: " + str(ctx.erp.LastError()))
            # Vincula/actualiza el precio negociado con el proveedor (orgProductSupplier).
            existe = ctx.scalar(
                "SELECT 1 FROM orgProductSupplier WHERE ProductID=@pid AND SupplierID=@sup",
                {"pid": it.PID, "sup": supplier_id},
            )
            if existe is None:
                ctx.execute(
                    "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) "
                    "VALUES (@pid, @sup, @precio, @cur, 0)",
                    {"pid": it.PID, "sup": supplier_id, "precio": it.Precio, "cur": moneda_id},
                )
            else:
                ctx.execute(
                    "UPDATE orgProductSupplier SET CostPrice=@precio WHERE ProductID=@pid AND SupplierID=@sup",
                    {"precio": it.Precio, "pid": it.PID, "sup": supplier_id},
                )

        ctx.erp.RecalcCompleto(doc)
        if ctx.erp.LastError():
            raise Exception("RecalcCompleto: " + str(ctx.erp.LastError()))
        # Nota: NO se llama AffectStockNEW -- la Orden de Compra NO afecta inventario.
        ctx.erp.Save(doc)
        if ctx.erp.LastError():
            raise Exception("Save: " + str(ctx.erp.LastError()))
        # Sin esto, el documento queda con estatus de entrega "No Aplica" en el grid
        # nativo aunque el documento este bien creado -- RecalcCompleto NO lo calcula.
        try:
            ctx.erp.UpdateStatusDelivery(doc)
        except Exception:
            pass
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass

        ctx.log("OC-OK doc=" + str(doc))
        msg("Orden de compra " + str(doc) + " creada exitosamente.", "OK")
        if nueva:
            limpiar_todo()
        else:
            frm.Close()
    except Exception as ex:
        ctx.log("ERROR OC: " + str(ex))
        msg(str(ex), "Error")
    finally:
        frm.Cursor = Cursors.Default
        tool_buttons["guardar"].Enabled = True


# ═══════════════════ NUMERO A LETRAS (para el total, estilo comprobante mexicano) ═══════════════════
_UNIDADES = ["", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ",
             "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO",
             "DIECINUEVE", "VEINTE"]
_DECENAS = ["", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"]
_VEINTI = ["VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS",
           "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"]
_CENTENAS = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
             "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"]


def _centenas(n):  # 0..999
    if n == 0:
        return ""
    if n == 100:
        return "CIEN"
    s = ""
    c, r = n // 100, n % 100
    if c > 0:
        s += _CENTENAS[c] + " "
    if r > 0:
        if r <= 20:
            s += _UNIDADES[r]
        else:
            d, u = r // 10, r % 10
            if d == 2 and u > 0:
                s += _VEINTI[u - 1]
            else:
                s += _DECENAS[d]
                if u > 0:
                    s += " Y " + _UNIDADES[u]
    return s.strip()


def _en_letras(n):  # 0..999,999,999
    if n == 0:
        return "CERO"
    resultado = ""
    millones, n = n // 1000000, n % 1000000
    miles, n = n // 1000, n % 1000
    resto = n
    if millones > 0:
        resultado += "UN MILLÓN " if millones == 1 else _centenas(millones) + " MILLONES "
    if miles > 0:
        resultado += "MIL " if miles == 1 else _centenas(miles) + " MIL "
    if resto > 0:
        resultado += _centenas(resto)
    resultado = resultado.strip()
    if resultado.endswith("UNO"):
        resultado = resultado[:-3] + "UN"  # UN peso, no UNO peso
    return resultado


def numero_a_letras(valor, moneda):
    if valor < 0:
        valor = 0
    entero = int(valor)
    centavos = int(round((valor - entero) * 100))
    return _en_letras(entero) + " " + moneda + " " + ("%02d" % centavos) + "/100"


# ═══════════════════ DETALLE DE PRODUCTO (doble clic en una partida) ═══════════════════
def mostrar_detalle_producto(pid):
    try:
        rows = ctx.query(
            "SELECT ProductKey, ProductName, ProductDescription, Unit, UnitSale, UnitBuy, CostPrice, PriceList, "
            "TaxTypeID, TaxPerc, Category1, Category2, Category3, Category4 "
            "FROM orgProduct WHERE ProductID=@pid", {"pid": pid})
    except Exception as ex:
        msg("Error al leer el producto: " + str(ex), "Error")
        return
    if not rows:
        msg("No se encontró el producto.")
        return
    info = rows[0]

    def val(k):
        v = info.get(k)
        return "" if v is None else str(v)

    try:
        existencias = ctx.query(
            "SELECT d.DepotName, ISNULL(SUM(k.Quantity),0) AS Existencia "
            "FROM orgDepot d LEFT JOIN orgProductKardex k ON k.DepotID=d.DepotID AND k.ProductID=@pid AND k.Cancelled=0 "
            "WHERE d.DeletedOn IS NULL GROUP BY d.DepotName ORDER BY d.DepotName", {"pid": pid})
    except Exception:
        existencias = []
    try:
        listas_precio = ctx.query(
            "SELECT pl.PriceListName, ppl.Price FROM orgProductPriceList ppl "
            "INNER JOIN orgPriceList pl ON pl.PriceListID=ppl.PriceListID "
            "WHERE ppl.ProductID=@pid AND pl.DeletedOn IS NULL ORDER BY pl.PriceListName", {"pid": pid})
    except Exception:
        listas_precio = []
    try:
        precios_proveedor = ctx.query(
            "SELECT be.OfficialName, ps.CostPrice, ps.RefSupplier FROM orgProductSupplier ps "
            "INNER JOIN orgSupplier s ON s.SupplierID=ps.SupplierID "
            "INNER JOIN orgBusinessEntity be ON be.BusinessEntityID=s.BusinessEntityID "
            "WHERE ps.ProductID=@pid ORDER BY ps.CostPrice", {"pid": pid})
    except Exception:
        precios_proveedor = []

    det = Form()
    det.Text = "Detalle de producto"
    det.Size = Size(700, 800)
    det.StartPosition = FormStartPosition.CenterParent
    det.BackColor = C_BG
    det.MinimizeBox = False
    det.MaximizeBox = False
    det.FormBorderStyle = getattr(System.Windows.Forms.FormBorderStyle, "FixedDialog")
    det.AutoScaleMode = getattr(System.Windows.Forms.AutoScaleMode, "None")

    tax_perc_info = float(info.get("TaxPerc") or 0)

    grp_gen = create_group("Datos generales", 10, 10, 660, 185, det)
    lbl("Clave:", 15, 38, grp_gen)
    lbl(val("ProductKey"), 110, 38, grp_gen, F_H2)
    lbl("Nombre:", 340, 38, grp_gen)
    lbl(val("ProductName"), 410, 38, grp_gen, F_H2)
    lbl("Descripción:", 15, 64, grp_gen)
    lbl_desc_larga = Label()
    lbl_desc_larga.Text = val("ProductDescription")
    lbl_desc_larga.Location = Point(110, 64)
    lbl_desc_larga.Size = Size(535, 34)
    lbl_desc_larga.Font = F_SM
    lbl_desc_larga.ForeColor = C_MUTED
    lbl_desc_larga.Parent = grp_gen
    lbl("Unidad:", 15, 108, grp_gen)
    lbl(val("Unit"), 110, 108, grp_gen)
    lbl("U. venta:", 250, 108, grp_gen)
    lbl(val("UnitSale"), 335, 108, grp_gen)
    lbl("U. compra:", 460, 108, grp_gen)
    lbl(val("UnitBuy"), 555, 108, grp_gen)
    lbl("Costo:", 15, 138, grp_gen)
    lbl("$%.2f" % float(info.get("CostPrice") or 0), 110, 138, grp_gen)
    lbl("Precio lista:", 250, 138, grp_gen)
    lbl("$%.2f" % float(info.get("PriceList") or 0), 335, 138, grp_gen)
    lbl("Impuesto:", 460, 138, grp_gen)
    lbl("%.2f%%" % (tax_perc_info * 100), 555, 138, grp_gen)

    grp_clas = create_group("Clasificaciones", 10, 205, 660, 90, det)
    lbl("Categoría 1:", 15, 38, grp_clas)
    lbl(val("Category1"), 130, 38, grp_clas, F_H2)
    lbl("Categoría 2:", 355, 38, grp_clas)
    lbl(val("Category2"), 470, 38, grp_clas, F_H2)
    lbl("Categoría 3:", 15, 63, grp_clas)
    lbl(val("Category3"), 130, 63, grp_clas, F_H2)
    lbl("Categoría 4:", 355, 63, grp_clas)
    lbl(val("Category4"), 470, 63, grp_clas, F_H2)

    def mini_grid(y, h, nombres, encabezados):
        g = DataGridView()
        g.Location = Point(15, y)
        g.Size = Size(640, h)
        g.BackgroundColor = C_PANEL
        g.BorderStyle = getattr(System.Windows.Forms.BorderStyle, "FixedSingle")
        g.ColumnHeadersBorderStyle = HEADER_BORDER_NONE
        g.EnableHeadersVisualStyles = False
        g.RowHeadersVisible = False
        g.AllowUserToAddRows = False
        g.AllowUserToDeleteRows = False
        g.ReadOnly = True
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        g.GridColor = C_BORDER
        g.Parent = det
        g.ColumnHeadersDefaultCellStyle.BackColor = C_HEADER
        g.ColumnHeadersDefaultCellStyle.Font = F_H2
        g.ColumnHeadersHeight = 26
        g.RowTemplate.Height = 24
        for n, e in zip(nombres, encabezados):
            g.Columns.Add(n, e)
        g.Columns[len(nombres) - 1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        return g

    y_pos = 305
    lbl_exist_title = lbl("Existencia por almacén", 15, y_pos, det, F_H2)
    grid_exist = mini_grid(y_pos + 22, 120, ["Depot", "Qty"], ["ALMACÉN", "EXISTENCIA"])
    for r in existencias:
        grid_exist.Rows.Add(str(r["DepotName"]), "%.2f" % float(r["Existencia"] or 0))
    if not existencias:
        grid_exist.Rows.Add("(sin almacenes)", "")
    y_pos += 22 + 120 + 24

    lbl_listas_title = lbl("Listas de precios", 15, y_pos, det, F_H2)
    grid_listas = mini_grid(y_pos + 22, 100, ["Lista", "Precio"], ["LISTA DE PRECIOS", "PRECIO"])
    for r in listas_precio:
        grid_listas.Rows.Add(str(r["PriceListName"]), "$%.2f" % float(r["Price"] or 0))
    if not listas_precio:
        grid_listas.Rows.Add("(sin listas de precio asignadas)", "")
    y_pos += 22 + 100 + 24

    lbl_prov_title = lbl("Precios por proveedor", 15, y_pos, det, F_H2)
    grid_prov = mini_grid(y_pos + 22, 100, ["Prov", "Ref", "Precio"], ["PROVEEDOR", "REF. PROVEEDOR", "COSTO"])
    for r in precios_proveedor:
        grid_prov.Rows.Add(str(r["OfficialName"]), str(r["RefSupplier"] or ""), "$%.2f" % float(r["CostPrice"] or 0))
    if not precios_proveedor:
        grid_prov.Rows.Add("(sin proveedores registrados)", "", "")
    y_pos += 22 + 100 + 20

    btn_cerrar = Button()
    btn_cerrar.Text = "Cerrar"
    btn_cerrar.Location = Point(585, y_pos)
    btn_cerrar.Size = Size(90, 30)
    btn_cerrar.FlatStyle = FlatStyle.Flat
    btn_cerrar.Font = F_BODY
    btn_cerrar.Parent = det
    btn_cerrar.FlatAppearance.BorderColor = C_BORDER
    btn_cerrar.Click += lambda s, e: det.Close()
    det.AcceptButton = btn_cerrar
    det.ClientSize = Size(700, y_pos + 30 + 20)

    det.ShowDialog(frm)


# ═══════════════════ EVENTOS ═══════════════════
def wire_tool(key, accion):
    host = tool_buttons[key]

    def handler(sender, ev):
        try:
            accion()
        except Exception as ex:
            msg(str(ex), "Error")

    host.Click += handler
    for ch in host.Controls:
        ch.Click += handler


wire_tool("guardar",  lambda: crear_orden_compra(False))
wire_tool("nueva",    lambda: crear_orden_compra(True))
wire_tool("cancelar", lambda: frm.Close())
wire_tool("preview",  lambda: msg("Vista previa: en proceso (próxima versión de la plantilla).", "En proceso"))
wire_tool("imprimir", lambda: msg("Impresión: en proceso (próxima versión de la plantilla).", "En proceso"))
wire_tool("limpiar",  lambda: (limpiar_todo() if len(partidas) == 0 or confirmar("¿Limpiar todos los campos?") else None))


def _prov_text_update(sender, ev):
    if suspender_prov[0]:
        return
    f = cbo_prov.Text
    fu = f.upper()
    suspender_prov[0] = True
    global prov_filtered
    prov_filtered = [o for o in prov_all
                      if len(f) == 0 or fu in o.Txt.upper() or fu in (o.Extra or "").upper()]
    cbo_prov.Items.Clear()
    for o in prov_filtered:
        cbo_prov.Items.Add(o.Txt)
    cbo_prov.Text = f
    cbo_prov.SelectionStart = len(f)
    cbo_prov.SelectionLength = 0
    cbo_prov.DroppedDown = cbo_prov.Items.Count > 0
    suspender_prov[0] = False
    actualizar_proveedor()


cbo_prov.TextUpdate += _prov_text_update
cbo_prov.SelectedIndexChanged += lambda s, e: (actualizar_proveedor() if not suspender_prov[0] else None)


def _txt_producto_changed(sender, ev):
    global producto_seleccionado
    if suspender_busqueda[0]:
        return
    producto_seleccionado = None
    buscar(txt_producto.Text)


txt_producto.TextChanged += _txt_producto_changed


def _btn_buscar_click(sender, ev):
    t = (txt_producto.Text or "").strip()
    buscar("#" if len(t) == 0 else t)
    txt_producto.Focus()


btn_buscar_prod.Click += _btn_buscar_click
lst_match.MouseDoubleClick += lambda s, e: seleccionar_producto()


def _lst_keydown(sender, ev):
    if ev.KeyCode == Keys.Enter:
        seleccionar_producto()
        ev.Handled = True
    elif ev.KeyCode == Keys.Escape:
        ocultar_resultados()
        txt_producto.Focus()
        ev.Handled = True


lst_match.KeyDown += _lst_keydown


def _txt_producto_keydown(sender, ev):
    if ev.KeyCode == Keys.Down and pnl_drop.Visible:
        lst_match.Focus()
        if lst_match.SelectedIndex < 0 and lst_match.Items.Count > 0:
            lst_match.SelectedIndex = 0
        ev.Handled = True
    elif ev.KeyCode == Keys.Enter:
        if pnl_drop.Visible and lst_match.SelectedItem is not None:
            seleccionar_producto()
        elif producto_seleccionado is not None:
            agregar_producto()
        ev.Handled = True
        ev.SuppressKeyPress = True
    elif ev.KeyCode == Keys.Escape:
        limpiar_captura()
        ev.Handled = True


txt_producto.KeyDown += _txt_producto_keydown


def _nud_keydown(sender, ev):
    if ev.KeyCode == Keys.Enter:
        agregar_producto()
        ev.Handled = True
        ev.SuppressKeyPress = True


nud_cantidad.KeyDown += _nud_keydown
nud_precio.KeyDown += _nud_keydown
nud_descuento.KeyDown += _nud_keydown
btn_agregar.Click += lambda s, e: agregar_producto()


def _btn_eliminar_click(sender, ev):
    if grid.CurrentRow is None:
        return
    i = grid.CurrentRow.Index
    if 0 <= i < len(partidas):
        del partidas[i]
        refresh_grid()


btn_eliminar.Click += _btn_eliminar_click
cbo_dep.SelectedIndexChanged += lambda s, e: precargar_folio()
cbo_moneda.SelectedIndexChanged += lambda s, e: refresh_grid()  # el total en letra depende de la moneda elegida


def _grid_double_click(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas):
        return
    mostrar_detalle_producto(partidas[ev.RowIndex].PID)


grid.CellDoubleClick += _grid_double_click


def _grid_cell_end_edit(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas):
        return
    it = partidas[ev.RowIndex]
    nombre_col = grid.Columns[ev.ColumnIndex].Name
    try:
        if nombre_col == "Qty":
            it.Qty = float(str(grid.Rows[ev.RowIndex].Cells["Qty"].Value or it.Qty))
        elif nombre_col == "Precio":
            it.Precio = float(str(grid.Rows[ev.RowIndex].Cells["Precio"].Value or it.Precio))
        elif nombre_col == "DescPerc":
            valor = grid.Rows[ev.RowIndex].Cells["DescPerc"].Value
            it.DescuentoPerc = float(str(valor if valor is not None else it.DescuentoPerc * 100)) / 100.0
    except Exception:
        pass
    if it.Qty <= 0:
        it.Qty = 1
    if it.Precio < 0:
        it.Precio = 0
    if it.DescuentoPerc < 0:
        it.DescuentoPerc = 0
    if it.DescuentoPerc > 1:
        it.DescuentoPerc = 1
    refresh_grid()


grid.CellEndEdit += _grid_cell_end_edit


def _frm_keydown(sender, ev):
    if ev.KeyCode == Keys.F5:
        crear_orden_compra(False)
        ev.Handled = True
    elif ev.KeyCode == Keys.Escape and not pnl_drop.Visible:
        frm.Close()
        ev.Handled = True


frm.KeyDown += _frm_keydown

# ═══════════════════ ARRANQUE ═══════════════════
actualizar_proveedor()
precargar_folio()
refresh_grid()
frm.ShowDialog()

result = "Ventana de orden de compra (Python) cerrada."
