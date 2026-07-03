# lang: python
# timeout: 1800
#
# PLANTILLA_EJEMPLO_PYTHON_WINFORMS.py
# La MISMA ventana que "Ejemplo Premium - C# WinForms" (misma Requisicion de compra,
# modulo 1040), pero orquestada 100% desde Python. Un usuario que no sepa C# puede
# copiar este patron para construir sus propias ventanas.
#
# Como funciona (para quien adapte esta plantilla):
# 1. pythonnet ("clr") deja crear objetos REALES de System.Windows.Forms desde Python,
#    cargando el .NET Framework 4.8 del equipo (el mismo que usa el addon) -> la ventana
#    se ve y se comporta IDENTICA a la version C#, no es una aproximacion.
# 2. Todo el acceso a datos/ERP es el SDK normal de BrosLMV: "from broslmv import ctx",
#    ctx.query/scalar/execute para SQL y ctx.erp.* para XEngine (folios, crear
#    documento, agregar partidas, recalcular) -- exactamente el mismo API que ya
#    conoces de los botones Python sin ventana.
# 3. El bloque que crea el documento esta en crear_requisicion(), cerca del final.
#    Es el MISMO patron NuevoDocumento -> AgregarArticulo -> RecalcCompleto -> Save
#    que usa la version C#.
#
# Nota para quien adapte: "# timeout: 1800" (arriba) es OBLIGATORIO en cualquier
# script Python con ventana interactiva -- el limite normal es 120s y una ventana
# puede quedar abierta minutos mientras el usuario captura datos.

import pythonnet
pythonnet.load("netfx")  # usa el .NET Framework del equipo (GAC), igual que el addon

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
    Application, Form, FormBorderStyle, FormStartPosition, Panel, Label, TextBox,
    ComboBox, ComboBoxStyle, NumericUpDown, Button, FlatStyle, DataGridView,
    DataGridViewTextBoxColumn, DataGridViewContentAlignment, DataGridViewAutoSizeColumnMode,
    DataGridViewHeaderBorderStyle, DataGridViewCellBorderStyle, DataGridViewSelectionMode,
    DataGridViewEditMode, DateTimePicker, DateTimePickerFormat, ListBox, DrawMode,
    DrawItemState, BorderStyle, ScrollBars, Cursors, Keys, MessageBox, MessageBoxButtons,
    MessageBoxIcon, DialogResult,
)

from broslmv import ctx

# Modal + WinForms necesitan un hilo STA. Debe fijarse ANTES de crear cualquier control.
System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA)

# "None" es palabra reservada en Python: no se puede escribir BorderStyle.None
# directamente. Se obtiene por reflexion con getattr, como en cualquier enum de .NET
# cuyo miembro se llame igual que una keyword de Python.
BORDER_NONE = getattr(BorderStyle, "None")
HEADER_BORDER_NONE = getattr(DataGridViewHeaderBorderStyle, "None")


# ═══════════════════ MENSAJES (nativos, sin ida y vuelta al host) ═══════════════════
# Al tener WinForms real en el propio proceso Python, los dialogos se muestran aqui
# mismo (mas rapido que relayar al addon). ctx.log() se sigue usando para auditoria.
def msg(texto, titulo="BrosLMV"):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Information)


def confirmar(texto, titulo="Confirmar"):
    r = MessageBox.Show(texto, titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
    return r == DialogResult.Yes


# ═══════════════════ DATOS INICIALES (mismas consultas que la version C#) ═══════════════════
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

partidas = []          # lista de Item
prov_all = []           # lista de Opt (todos los proveedores, para el filtro incremental)
cur_rate = {}           # CurrencyID -> Rate
producto_seleccionado = None   # Prod
suspender_busqueda = [False]   # listas de 1 elemento = "referencia mutable" (closures)
suspender_prov = [False]
# Resultados de busqueda de producto (lst_match.Items solo lleva texto: ver nota
# en la seccion del ListBox mas abajo sobre por que no se pueden meter objetos
# Python ahi directamente).
match_items = []        # lista de Prod, EN EL MISMO ORDEN que lst_match.Items


# ═══════════════════ CLASES DE APOYO ═══════════════════
class Opt:
    def __init__(self, txt, val, extra=""):
        self.Txt = txt
        self.Val = val
        self.Extra = extra or ""

    def __str__(self):
        # WinForms llama ToString() para pintar el texto del combo: __str__ cubre eso.
        return self.Txt


class Item:
    def __init__(self, pid, key, desc, unit, qty, stock):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.Qty = qty
        self.Stock = stock


class Prod:
    def __init__(self, pid, key, desc, unit, stock):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.Stock = stock


# ═══════════════════ COLORES / FUENTES (identicos a la version C#) ═══════════════════
C_BG      = Color.FromArgb(241, 245, 249)
C_BORDER  = Color.FromArgb(203, 213, 225)
C_PRIMARY = Color.FromArgb(16, 185, 129)
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
frm.Text = "Nueva Requisición de Compra (Python)"
frm.Size = Size(1100, 800)
frm.StartPosition = FormStartPosition.CenterScreen
frm.BackColor = C_BG
frm.KeyPreview = True


# ═══════════════════ HELPERS DE CONTROL ═══════════════════
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
    # items_paralelos: lista de (label, val) EN EL MISMO ORDEN que cbo.Items
    # (que solo contiene los labels/strings; ver nota sobre DropDownList arriba).
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


add_toolbar_btn("guardar",  "\U0001F4BE", "Guardar\nF5",       Color.FromArgb(52, 211, 153))
add_toolbar_btn("nueva",    "➕", "Guardar y\nNueva")
add_toolbar_btn("cancelar", "❌", "Cancelar\nEsc",      Color.FromArgb(248, 113, 113))
add_sep()
add_toolbar_btn("preview",  "\U0001F50E", "Vista\nprevia")
add_toolbar_btn("imprimir", "\U0001F5A8", "Imprimir")
add_sep()
add_toolbar_btn("limpiar",  "\U0001F9F9", "Limpiar\ncampos")

# Info del documento (Fecha / Serie / Folio) - a la derecha de la cinta, sin encimarse.
info_pnl = Panel()
info_pnl.Location = Point(700, 8)
info_pnl.Size = Size(352, 80)
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
dt_fecha.Size = Size(110, 23)
dt_fecha.Format = DateTimePickerFormat.Short
dt_fecha.Font = F_BODY
dt_fecha.Parent = info_pnl

lbl_serie = Label()
lbl_serie.Text = "Serie:"
lbl_serie.Font = F_BODY
lbl_serie.ForeColor = C_RIBBON_TX
lbl_serie.BackColor = C_RIBBON
lbl_serie.Location = Point(185, 24)
lbl_serie.AutoSize = True
lbl_serie.Parent = info_pnl

txt_serie = TextBox()
txt_serie.Location = Point(228, 21)
txt_serie.Size = Size(110, 23)
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

# Folio editable: se precarga el consecutivo pero el usuario puede modificarlo.
txt_folio = TextBox()
txt_folio.Location = Point(60, 49)
txt_folio.Size = Size(110, 23)
txt_folio.Font = F_BODY
txt_folio.Parent = info_pnl

lbl_folio_nota = Label()
lbl_folio_nota.Text = "(consecutivo, editable)"
lbl_folio_nota.Font = F_SM
lbl_folio_nota.ForeColor = C_RIBBON_MU
lbl_folio_nota.BackColor = C_RIBBON
lbl_folio_nota.Location = Point(185, 52)
lbl_folio_nota.AutoSize = True
lbl_folio_nota.Parent = info_pnl

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
# NOTA (pythonnet): ComboBox.Items solo lleva STRINGS (ver nota mas abajo, misma
# limitacion que los combos DropDownList). prov_filtered guarda, EN EL MISMO
# ORDEN que cbo_prov.Items, los Opt reales de lo que esta filtrado/visible ahora.
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

# NOTA IMPORTANTE (pythonnet): los combos DropDownList NO pueden llevar objetos
# Python personalizados en Items -- ComboBox.SelectedIndex dispara UpdateText(),
# que intenta convertir el item a System.String y truena (InvalidCastException)
# con objetos que no sean nativos de .NET. Por eso aqui se usan SOLO strings en
# Items, con una lista paralela en Python para recuperar el valor real (ID).
# (El combo de Proveedor de arriba SI puede usar objetos, porque es DropDown
# editable, no DropDownList -- ese camino de ComboBox no tiene el mismo problema.)
lbl("Moneda:", 440, 40, grp_prov)
cbo_moneda = ComboBox()
cbo_moneda.Location = Point(510, 38)
cbo_moneda.Size = Size(130, 23)
cbo_moneda.Font = F_BODY
cbo_moneda.DropDownStyle = ComboBoxStyle.DropDownList
cbo_moneda.FlatStyle = FlatStyle.Flat
cbo_moneda.Parent = grp_prov
moneda_items = []  # [(label, CurrencyID), ...] paralelo a cbo_moneda.Items
for m in monedas:
    cid = int(m["CurrencyID"])
    cur_rate[cid] = float(m["Rate"] or 1)
    label = str(m["IntlSymbol"]) + " - " + str(m["Currency"])
    moneda_items.append((label, cid))
    cbo_moneda.Items.Add(label)
_sel_default(cbo_moneda, moneda_items, 3)  # MXN por defecto

lbl("Cond. pago:", 440, 70, grp_prov)
cbo_cond = ComboBox()
cbo_cond.Location = Point(510, 68)
cbo_cond.Size = Size(130, 23)
cbo_cond.Font = F_BODY
cbo_cond.DropDownStyle = ComboBoxStyle.DropDownList
cbo_cond.FlatStyle = FlatStyle.Flat
cbo_cond.Parent = grp_prov
cond_items = []  # [(label, PaymentTermID), ...] paralelo a cbo_cond.Items
for c in condiciones:
    label = str(c["PaymentTermName"])
    cond_items.append((label, int(c["PaymentTermID"])))
    cbo_cond.Items.Add(label)
_sel_default(cbo_cond, cond_items, 1)  # CONTADO por defecto

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
dep_items = []  # [(label, DepotID), ...] paralelo a cbo_dep.Items
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

# ═══════════════════ 4. PARTIDAS ═══════════════════
grp_part = create_group("4. Partidas", 10, top_y[0], 1060, 320, frm)

txt_producto = TextBox()
txt_producto.Location = Point(15, 35)
txt_producto.Size = Size(260, 23)
txt_producto.Font = F_BODY
txt_producto.Parent = grp_part

btn_buscar_prod = Button()
btn_buscar_prod.Text = "\U0001F50D"
btn_buscar_prod.Location = Point(280, 34)
btn_buscar_prod.Size = Size(28, 25)
btn_buscar_prod.Font = F_ICON_SM
btn_buscar_prod.FlatStyle = FlatStyle.Flat
btn_buscar_prod.Parent = grp_part
btn_buscar_prod.FlatAppearance.BorderColor = C_BORDER

lbl("Cantidad:", 325, 38, grp_part)
nud_cantidad = NumericUpDown()
nud_cantidad.Value = Decimal(1)
nud_cantidad.DecimalPlaces = 4
nud_cantidad.Maximum = Decimal(999999999)
nud_cantidad.Location = Point(385, 35)
nud_cantidad.Size = Size(80, 23)
nud_cantidad.Font = F_BODY
nud_cantidad.Parent = grp_part

btn_agregar = Button()
btn_agregar.Text = "➕  Agregar"
btn_agregar.Location = Point(475, 34)
btn_agregar.Size = Size(90, 26)
btn_agregar.BackColor = C_PRIMARY
btn_agregar.ForeColor = Color.White
btn_agregar.FlatStyle = FlatStyle.Flat
btn_agregar.Font = F_BODY
btn_agregar.Parent = grp_part
btn_agregar.FlatAppearance.BorderSize = 0

btn_eliminar = Button()
btn_eliminar.Text = "❌  Eliminar"
btn_eliminar.Location = Point(575, 34)
btn_eliminar.Size = Size(90, 26)
btn_eliminar.FlatStyle = FlatStyle.Flat
btn_eliminar.ForeColor = C_DANGER
btn_eliminar.Font = F_BODY
btn_eliminar.Parent = grp_part
btn_eliminar.FlatAppearance.BorderColor = C_BORDER

lbl_total_partidas = lbl("Total de partidas: 0", 910, 38, grp_part, F_H2)

grid = DataGridView()
grid.Location = Point(15, 70)
grid.Size = Size(1030, 235)
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
col(grid, "Key", "CLAVE", 120, True)
c_desc = col(grid, "Desc", "DESCRIPCIÓN", 400, True)
c_desc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
col(grid, "Unit", "U.M.", 80, True)
col(grid, "Stock", "EXISTENCIA", 100, True, DataGridViewContentAlignment.MiddleRight)
col(grid, "Qty", "CANTIDAD", 100, False, DataGridViewContentAlignment.MiddleRight)
grid.DefaultCellStyle.SelectionBackColor = C_BLUE_SEL
grid.DefaultCellStyle.SelectionForeColor = C_TEXT

pnl_empty = Panel()
pnl_empty.Location = Point(300, 120)
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

# Lista flotante de resultados de producto
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

# ═══════════════════ 5. FOOTER ═══════════════════
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
lbl_footer_nota.Text = "Usa  \U0001F4BE Guardar (F5)  en la cinta para crear la requisición."
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
    # Opt real detras del texto que el combo muestra ahora mismo: por indice
    # seleccionado dentro de lo filtrado, o por texto exacto (si el usuario
    # escribio libremente sin usar el desplegable).
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
        pre = ctx.erp.GetFolioPrefix(1040, depot_sel())
        fol = ctx.erp.GetNextFolio(1040, pre, depot_sel())
        txt_serie.Text = pre or ""
        txt_folio.Text = fol or ""
    except Exception:
        txt_serie.Text = "REQ"
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
            "SELECT " + limite + " p.ProductID, p.ProductKey, p.ProductName, p.Unit, "
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

    # NOTA (pythonnet): un ListBox con DrawMode=OwnerDrawFixed pierde la seleccion
    # (SelectedIndex vuelve a -1) si Items contiene objetos Python -- ocurre al
    # crear el control nativo la primera vez, no es un error de este script.
    # Por eso Items solo lleva un string (el texto no se usa, se pinta a mano en
    # _draw_item) y el objeto real vive en match_items, mismo orden/indice.
    match_items = []
    lst_match.Items.Clear()
    for r in rows:
        p = Prod(int(r["ProductID"]), str(r["ProductKey"]), str(r["ProductName"]),
                  str(r["Unit"]), float(r["Stock"] or 0))
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
    ocultar_resultados()
    nud_cantidad.Focus()


def refresh_grid():
    grid.Rows.Clear()
    for it in partidas:
        grid.Rows.Add(it.PID, it.Key, it.Desc, it.Unit, it.Stock, it.Qty)
    pnl_empty.Visible = len(partidas) == 0
    lbl_total_partidas.Text = "Total de partidas: " + str(len(partidas))


def limpiar_captura():
    global producto_seleccionado
    producto_seleccionado = None
    suspender_busqueda[0] = True
    txt_producto.Clear()
    suspender_busqueda[0] = False
    nud_cantidad.Value = Decimal(1)
    txt_producto.Focus()


def agregar_producto():
    global producto_seleccionado
    if producto_seleccionado is None:
        if pnl_drop.Visible and 0 <= lst_match.SelectedIndex < len(match_items):
            seleccionar_producto()
        else:
            msg("Busca y selecciona un producto.")
            txt_producto.Focus()
            return

    cant = float(str(nud_cantidad.Value))
    p = producto_seleccionado
    existente = next((z for z in partidas if z.PID == p.PID), None)
    if existente is not None:
        existente.Qty += cant
    else:
        partidas.append(Item(p.PID, p.Key, p.Desc, p.Unit, cant, p.Stock))

    refresh_grid()
    limpiar_captura()


def limpiar_todo():
    partidas.clear()
    refresh_grid()
    limpiar_captura()
    txt_obs.Clear()
    precargar_folio()


# ═══════════════════ CREAR EN CONTPAQI (el mismo patrón que la version C#) ═══════════════════
def crear_requisicion(nueva):
    be = prov_sel()
    if be == 0:
        msg("Selecciona un proveedor.")
        return
    if len(partidas) == 0:
        msg("Agrega al menos un producto.")
        return

    supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=@be", {"be": be})
    if supplier_id is None:
        msg("El proveedor seleccionado no tiene registro de proveedor (orgSupplier).", "Error")
        return

    if not confirmar("¿Crear requisición de compra con " + str(len(partidas)) +
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

        ctx.execute("SET XACT_ABORT OFF; SET IMPLICIT_TRANSACTIONS OFF")
        doc = ctx.erp.NuevoDocumento(1040, dep, be)  # 1040 = Solicitud de Compra
        if doc is None or doc <= 0 or ctx.erp.LastError():
            raise Exception("NuevoDocumento: " + str(ctx.erp.LastError()))

        # Moneda + condicion de pago + fecha + comentarios (del sistema / capturados)
        ctx.execute(
            "UPDATE docDocument SET DepotIDFrom=0, UserID=0, "
            "CurrencyID=@cur, Rate=@rate, PaymentTermID=@cond, "
            "DateDocument=@fecha, Comments=@obs WHERE DocumentID=@doc",
            {"cur": moneda_id, "rate": rate, "cond": cond_id,
             "fecha": dt_fecha.Value.ToString("yyyyMMdd"), "obs": txt_obs.Text or "", "doc": doc},
        )

        # Folio manual (si el usuario lo modifico)
        folio_manual = (txt_folio.Text or "").strip()
        if len(folio_manual) > 0:
            ctx.execute("UPDATE docDocument SET Folio=@f WHERE DocumentID=@doc", {"f": folio_manual, "doc": doc})

        for it in partidas:
            ctx.erp.AgregarArticulo(doc, it.PID, it.Qty)
            if ctx.erp.LastError():
                raise Exception("AgregarArticulo: " + str(ctx.erp.LastError()))
            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=@pid AND SupplierID=@sup) "
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) "
                "VALUES (@pid, @sup, 0, @cur, 0)",
                {"pid": it.PID, "sup": supplier_id, "cur": moneda_id},
            )

        ctx.erp.RecalcCompleto(doc)
        if ctx.erp.LastError():
            raise Exception("RecalcCompleto: " + str(ctx.erp.LastError()))
        ctx.erp.Save(doc)
        if ctx.erp.LastError():
            raise Exception("Save: " + str(ctx.erp.LastError()))
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass

        ctx.log("REQ-OK doc=" + str(doc))
        msg("Requisición de compra " + str(doc) + " creada exitosamente.", "OK")
        if nueva:
            limpiar_todo()
        else:
            frm.Close()
    except Exception as ex:
        ctx.log("ERROR REQ: " + str(ex))
        msg(str(ex), "Error")
    finally:
        frm.Cursor = Cursors.Default
        tool_buttons["guardar"].Enabled = True


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


wire_tool("guardar",  lambda: crear_requisicion(False))
wire_tool("nueva",    lambda: crear_requisicion(True))
wire_tool("cancelar", lambda: frm.Close())
wire_tool("preview",  lambda: msg("Vista previa: en proceso (próxima versión de la plantilla).", "En proceso"))
wire_tool("imprimir", lambda: msg("Impresión: en proceso (próxima versión de la plantilla).", "En proceso"))
wire_tool("limpiar",  lambda: (limpiar_todo() if len(partidas) == 0 or confirmar("¿Limpiar todos los campos?") else None))


def _prov_text_update(sender, ev):
    global prov_filtered
    if suspender_prov[0]:
        return
    f = cbo_prov.Text
    fu = f.upper()
    suspender_prov[0] = True
    prov_filtered = [o for o in prov_all
                      if len(f) == 0 or fu in o.Txt.upper() or fu in (o.Extra or "").upper()]
    cbo_prov.Items.Clear()
    for o in prov_filtered:
        cbo_prov.Items.Add(o.Txt)  # solo el texto (ver nota de ComboBox mas arriba)
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
        if pnl_drop.Visible and 0 <= lst_match.SelectedIndex < len(match_items):
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


def _frm_keydown(sender, ev):
    if ev.KeyCode == Keys.F5:
        crear_requisicion(False)
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

result = "Ventana de requisición (Python) cerrada."
