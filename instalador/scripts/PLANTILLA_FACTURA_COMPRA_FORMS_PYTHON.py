# lang: python
# timeout: 1800
#
# PLANTILLA: Factura de Compra -- version Forms (Python, WinForms real via pythonnet)
# La MISMA logica que PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx (modulo 152), pero en
# Python puro (pythonnet). Ver esa plantilla para la explicacion completa del patron
# (documento DERIVADO desde OC seleccionadas, SourceDocumentItemID, PaymentAgenda con
# montos reales) y MANUAL.md #10 ("Ventanas WinForms: modeless").
#
# Simplificacion real frente a la version C#: la columna "Impuesto" del grid es de SOLO
# LECTURA aqui (muestra el impuesto heredado de la Orden de Compra, no se puede cambiar por
# partida) -- la version C# usa un DataGridViewComboBoxColumn por fila, que en pythonnet es
# fragil de enlazar de forma confiable con objetos Python. Si necesitas cambiar el impuesto
# de una partida, usa PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx o WEBVIEW2.
#
# CORREGIDO (mismo bug que la version C#): el vinculo con la partida de la OC origen se
# pone con un UPDATE aparte despues de AgregarArticulo (columna SourceDocumentItemID) -- NO
# con el 8vo parametro de AgregarArticulo, que escribe DeliverDocumentItemID (la columna que
# usa Recepcion de Compra, no Factura).

import pythonnet
pythonnet.load("netfx")

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System.Drawing import (
    Point, Size, Color, Font, FontStyle, SolidBrush, Pen, ContentAlignment,
)
from System.Windows.Forms import (
    Form, FormStartPosition, Panel, Label, TextBox, ComboBox, ComboBoxStyle,
    Button, FlatStyle, DataGridView, DataGridViewTextBoxColumn,
    DataGridViewContentAlignment, DataGridViewAutoSizeColumnMode, DataGridViewHeaderBorderStyle,
    DataGridViewCellBorderStyle, DataGridViewSelectionMode, DateTimePicker, DateTimePickerFormat,
    BorderStyle, ScrollBars, Cursors, Keys, MessageBox, MessageBoxButtons, MessageBoxIcon, DialogResult,
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


# ═══════════════════ VALIDACIÓN TEMPRANA DE LA SELECCIÓN ═══════════════════
ids_seleccionados = ctx.get_selected_ids()
if not ids_seleccionados:
    msg("Selecciona una o varias Órdenes de Compra en la lista (Ctrl+clic) antes de usar este botón.", "Sin selección")
    raise SystemExit

ids_csv = ",".join(str(i) for i in ids_seleccionados)
ocs_seleccionadas = ctx.query("""
    SELECT d.DocumentID, d.BusinessEntityID, d.DepotID, d.FolioPrefix, d.Folio, be.OfficialName,
           ISNULL(m.OfficialNumber,'') AS RFC
    FROM docDocument d
    INNER JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
    WHERE d.DocumentID IN (%s) AND d.ModuleID = 183
      AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
""" % ids_csv)

if not ocs_seleccionadas:
    msg("Ninguna de las filas seleccionadas es una Orden de Compra activa (módulo 183).", "Selección inválida")
    raise SystemExit

proveedores_distintos = list({o["BusinessEntityID"] for o in ocs_seleccionadas})
if len(proveedores_distintos) > 1:
    msg("Las Órdenes de Compra seleccionadas son de proveedores distintos. Selecciona Órdenes de Compra de UN SOLO proveedor a la vez.", "Selección inválida")
    raise SystemExit

proveedor_be = proveedores_distintos[0]
proveedor_nombre = str(ocs_seleccionadas[0]["OfficialName"])
proveedor_rfc = str(ocs_seleccionadas[0]["RFC"] or "")
depot_origen = int(ocs_seleccionadas[0]["DepotID"] or 1)
ocs_resumen_texto = ", ".join((str(o["FolioPrefix"] or "")) + str(o["Folio"]) for o in ocs_seleccionadas)

# ═══════════════════ DATOS INICIALES ═══════════════════
condiciones = ctx.query("SELECT PaymentTermID, PaymentTermName FROM vwLBSPaymentTermList WHERE Buys=1 AND Deleted=0 ORDER BY PaymentTermID")
impuestos = ctx.query("""
    SELECT t.TaxTypeID, t.TaxTypeName, ISNULL(tp.IVA_Perc,0) AS IVA_Perc
    FROM vwLBSTaxType t LEFT JOIN vwLBSTaxPerc tp ON tp.TaxTypeID = t.TaxTypeID
    ORDER BY t.TaxTypeName
""")
tax_perc_by_type = {int(t["TaxTypeID"]): float(t["IVA_Perc"] or 0) for t in impuestos}
tax_label_by_type = {int(t["TaxTypeID"]): str(t["TaxTypeName"]) for t in impuestos}

partidas_a_facturar = []


class Item:
    def __init__(self, pid, source_item_id, oc_folio, key, desc, unit, tax_type_id, tax_perc, precio, descuento_perc, pendiente):
        self.PID = pid
        self.SourceItemId = source_item_id
        self.OcFolio = oc_folio
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.TaxTypeId = tax_type_id
        self.TaxPerc = tax_perc
        self.Precio = precio
        self.DescuentoPerc = descuento_perc
        self.Pendiente = pendiente


class PartidaConsolidada:
    def __init__(self, pid, key, desc, unit, tax_type_id, tax_perc, precio, descuento_perc):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.TaxTypeId = tax_type_id
        self.TaxPerc = tax_perc
        self.Precio = precio
        self.DescuentoPerc = descuento_perc
        self.Pendiente = 0.0
        self.Qty = 0.0
        self.OcsResumen = ""
        self.Fuentes = []


# Cálculo PROPIO de "cuánto de esta partida de OC ya se facturó" -- ver
# PLANTILLA_FACTURA_COMPRA_FORMS_CSHARP.ctx para la explicación completa.
rows = ctx.query("""
    SELECT d.DocumentID, d.FolioPrefix, d.Folio,
           i.DocumentItemID, i.ProductID, ISNULL(p.ProductKey, i.ProductKey) AS ProductKey,
           ISNULL(p.ProductName, i.Description) AS ProductName, i.Unit, i.TaxTypeID, i.TaxPerc,
           i.UnitPrice, i.DiscountPerc, i.Quantity AS Ordenado,
           ISNULL((SELECT SUM(fi.Quantity) FROM docDocumentItem fi
                   INNER JOIN docDocument fd ON fd.DocumentID = fi.DocumentID
                   WHERE fi.SourceDocumentItemID = i.DocumentItemID AND fi.DeletedOn IS NULL
                     AND fd.DeletedOn IS NULL AND fd.CancelledOn IS NULL), 0) AS YaFacturado
    FROM docDocumentItem i
    INNER JOIN docDocument d ON d.DocumentID = i.DocumentID
    LEFT JOIN orgProduct p ON p.ProductID = i.ProductID
    WHERE d.DocumentID IN (%s)
      AND i.DeletedOn IS NULL AND i.ProductID > 0
    ORDER BY d.DocumentID, i.LineNumber
""" % ids_csv)

oc_folio_por_id = {int(o["DocumentID"]): str(o["FolioPrefix"] or "") + str(o["Folio"]) for o in ocs_seleccionadas}
por_producto = {}
for r in rows:
    ordenado = float(r["Ordenado"] or 0)
    ya_facturado = float(r["YaFacturado"] or 0)
    pendiente = ordenado - ya_facturado
    if pendiente <= 0.0001:
        continue
    pid = int(r["ProductID"])
    doc_id = int(r["DocumentID"])
    fuente = Item(
        pid, int(r["DocumentItemID"]), oc_folio_por_id.get(doc_id, "Doc %d" % doc_id),
        str(r["ProductKey"]), str(r["ProductName"]), str(r["Unit"]),
        int(r["TaxTypeID"] or 0), float(r["TaxPerc"] or 0), float(r["UnitPrice"] or 0),
        float(r["DiscountPerc"] or 0), pendiente
    )
    if pid not in por_producto:
        por_producto[pid] = PartidaConsolidada(pid, fuente.Key, fuente.Desc, fuente.Unit, fuente.TaxTypeId, fuente.TaxPerc, fuente.Precio, fuente.DescuentoPerc)
    por_producto[pid].Fuentes.append(fuente)

for pc in por_producto.values():
    pc.Pendiente = sum(f.Pendiente for f in pc.Fuentes)
    pc.Qty = pc.Pendiente
    vistos = []
    for f in pc.Fuentes:
        if f.OcFolio not in vistos:
            vistos.append(f.OcFolio)
    pc.OcsResumen = ", ".join(vistos)
    partidas_a_facturar.append(pc)

# ═══════════════════ COLORES / FUENTES ═══════════════════
C_BG      = Color.FromArgb(241, 245, 249)
C_BORDER  = Color.FromArgb(203, 213, 225)
C_PRIMARY = Color.FromArgb(180, 83, 9)   # naranja: distingue de OC (azul) y RC (verde)
C_DANGER  = Color.FromArgb(239, 68, 68)
C_TEXT    = Color.FromArgb(30, 41, 59)
C_MUTED   = Color.FromArgb(100, 116, 139)
C_PANEL   = Color.White
C_HEADER  = Color.FromArgb(248, 250, 252)
C_BLUE_SEL = Color.FromArgb(255, 237, 213)
C_RIBBON    = Color.FromArgb(51, 65, 85)
C_RIBBON_TX = Color.FromArgb(226, 232, 240)
C_RIBBON_MU = Color.FromArgb(148, 163, 184)

F_TAB     = Font("Segoe UI", 10.0, FontStyle.Bold)
F_H2      = Font("Segoe UI", 9.0, FontStyle.Bold)
F_BODY    = Font("Segoe UI", 9.0)
F_SM      = Font("Segoe UI", 8.5)
F_ICON    = Font("Segoe UI Emoji", 20.0)

# ═══════════════════ VENTANA ═══════════════════
frm = Form()
frm.Text = "Nueva Factura de Compra (Python)"
frm.Size = Size(1200, 860)
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


# ═══════════════════ CINTA SUPERIOR (RIBBON) ═══════════════════
ribbon = Panel()
ribbon.Location = Point(10, 10)
ribbon.Size = Size(1160, 96)
ribbon.BackColor = C_RIBBON
ribbon.Parent = frm

lbl_title = Label()
lbl_title.Text = "Principal"
lbl_title.Font = F_TAB
lbl_title.ForeColor = C_RIBBON_TX
lbl_title.BackColor = C_RIBBON
lbl_title.Location = Point(12, 4)
lbl_title.AutoSize = True
lbl_title.Parent = ribbon

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


add_toolbar_btn("guardar",  "\U0001F4BE", "Guardar\nF5", Color.FromArgb(96, 165, 250))
add_toolbar_btn("cancelar", "❌", "Cancelar\nEsc", Color.FromArgb(248, 113, 113))
add_sep()
add_toolbar_btn("limpiar",  "\U0001F9F9", "Limpiar\ncampos")

info_pnl = Panel()
info_pnl.Location = Point(700, 8)
info_pnl.Size = Size(452, 80)
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

lbl_cond = Label()
lbl_cond.Text = "Cond. pago:"
lbl_cond.Font = F_BODY
lbl_cond.ForeColor = C_RIBBON_TX
lbl_cond.BackColor = C_RIBBON
lbl_cond.Location = Point(312, 24)
lbl_cond.AutoSize = True
lbl_cond.Parent = info_pnl

cbo_cond = ComboBox()
cbo_cond.Location = Point(312, 45)
cbo_cond.Size = Size(130, 23)
cbo_cond.Font = F_BODY
cbo_cond.DropDownStyle = ComboBoxStyle.DropDownList
cbo_cond.FlatStyle = FlatStyle.Flat
cbo_cond.Parent = info_pnl
cond_items = []
_cond_default_idx = 0
for idx, c in enumerate(condiciones):
    label = str(c["PaymentTermName"])
    val = int(c["PaymentTermID"])
    cond_items.append((label, val))
    cbo_cond.Items.Add(label)
    if val == 4:
        _cond_default_idx = idx
if cbo_cond.Items.Count > 0:
    cbo_cond.SelectedIndex = _cond_default_idx


def precargar_folio():
    try:
        pre = ctx.erp.GetFolioPrefix(152, depot_origen)
        fol = ctx.erp.GetNextFolio(152, pre, depot_origen)
        txt_folio.Text = fol or ""
    except Exception:
        txt_folio.Text = ""


top_y = [116]

# ═══════════════════ 1. PROVEEDOR Y ÓRDENES ORIGEN ═══════════════════
grp_prov = create_group("1. Proveedor y Órdenes de Compra origen", 10, top_y[0], 1160, 70, frm)
lbl("Proveedor:", 15, 38, grp_prov)
lbl_prov_txt = TextBox()
lbl_prov_txt.Text = proveedor_nombre
lbl_prov_txt.Location = Point(85, 36)
lbl_prov_txt.Size = Size(320, 23)
lbl_prov_txt.ReadOnly = True
lbl_prov_txt.Font = F_BODY
lbl_prov_txt.Parent = grp_prov

lbl("RFC:", 420, 38, grp_prov)
lbl_rfc_txt = TextBox()
lbl_rfc_txt.Text = proveedor_rfc
lbl_rfc_txt.Location = Point(455, 36)
lbl_rfc_txt.Size = Size(150, 23)
lbl_rfc_txt.ReadOnly = True
lbl_rfc_txt.Font = F_BODY
lbl_rfc_txt.ForeColor = C_MUTED
lbl_rfc_txt.Parent = grp_prov

lbl("Órdenes:", 620, 38, grp_prov)
lbl_ocs_txt = TextBox()
lbl_ocs_txt.Text = ocs_resumen_texto
lbl_ocs_txt.Location = Point(680, 36)
lbl_ocs_txt.Size = Size(460, 23)
lbl_ocs_txt.ReadOnly = True
lbl_ocs_txt.Font = F_BODY
lbl_ocs_txt.Parent = grp_prov

top_y[0] += 80

# ═══════════════════ 2. PARTIDAS A FACTURAR ═══════════════════
grp_part = create_group("2. Partidas a facturar (Cantidad/Precio/Desc. % editables -- Impuesto de solo lectura, ver nota al inicio)", 10, top_y[0], 1160, 320, frm)
grid = DataGridView()
grid.Location = Point(15, 35)
grid.Size = Size(1130, 275)
grid.BackgroundColor = C_PANEL
grid.BorderStyle = BORDER_NONE
grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
grid.ColumnHeadersBorderStyle = HEADER_BORDER_NONE
grid.EnableHeadersVisualStyles = False
grid.RowHeadersVisible = False
grid.AllowUserToAddRows = False
grid.AllowUserToDeleteRows = False
grid.SelectionMode = DataGridViewSelectionMode.CellSelect
grid.MultiSelect = False
grid.GridColor = C_BORDER
grid.Parent = grp_part
grid.ColumnHeadersDefaultCellStyle.BackColor = C_HEADER
grid.ColumnHeadersDefaultCellStyle.ForeColor = C_TEXT
grid.ColumnHeadersDefaultCellStyle.Font = F_H2
grid.ColumnHeadersHeight = 28

col(grid, "PID", "PID", 0, True).Visible = False
col(grid, "OcFolio", "ORDEN(ES)", 130, True)
col(grid, "Key", "CLAVE", 90, True)
c_desc = col(grid, "Desc", "DESCRIPCIÓN", 200, True)
c_desc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
col(grid, "Unit", "U.M.", 55, True)
col(grid, "Pendiente", "PENDIENTE", 85, True, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Cantidad", "CANT. A FACTURAR", 110, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Precio", "PRECIO UNIT.", 95, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Impuesto", "IMPUESTO", 130, True)
col(grid, "DescPerc", "DESC. %", 70, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Importe", "IMPORTE", 95, True, DataGridViewContentAlignment.MiddleRight, "N2")
grid.DefaultCellStyle.SelectionBackColor = C_BLUE_SEL
grid.DefaultCellStyle.SelectionForeColor = C_TEXT

lbl_part_vacio = Label()
lbl_part_vacio.Text = "No hay partidas pendientes de facturar en las Órdenes de Compra seleccionadas."
lbl_part_vacio.Font = F_BODY
lbl_part_vacio.ForeColor = C_MUTED
lbl_part_vacio.AutoSize = True
lbl_part_vacio.Location = Point(300, 120)
lbl_part_vacio.Visible = False
lbl_part_vacio.Parent = grp_part

top_y[0] += 330

# ═══════════════════ 3. COMENTARIOS ═══════════════════
grp_obs = create_group("3. Comentarios del Documento", 10, top_y[0], 1160, 60, frm)
txt_obs = TextBox()
txt_obs.Multiline = True
txt_obs.Location = Point(15, 30)
txt_obs.Size = Size(1130, 22)
txt_obs.Font = F_BODY
txt_obs.ScrollBars = ScrollBars.Vertical
txt_obs.Parent = grp_obs

top_y[0] += 70

# ═══════════════════ 4. TOTALES ═══════════════════
grp_tot = create_group("4. Totales", 10, top_y[0], 1160, 90, frm)
lbl("Subtotal:", 15, 38, grp_tot)
lbl_t_subtotal = Label()
lbl_t_subtotal.Text = "$0.00"
lbl_t_subtotal.Font = F_BODY
lbl_t_subtotal.AutoSize = False
lbl_t_subtotal.Size = Size(150, 20)
lbl_t_subtotal.TextAlign = ContentAlignment.MiddleRight
lbl_t_subtotal.Location = Point(95, 36)
lbl_t_subtotal.Parent = grp_tot

lbl("Descuento:", 270, 38, grp_tot)
lbl_t_desc = Label()
lbl_t_desc.Text = "-$0.00"
lbl_t_desc.Font = F_BODY
lbl_t_desc.ForeColor = C_DANGER
lbl_t_desc.AutoSize = False
lbl_t_desc.Size = Size(150, 20)
lbl_t_desc.TextAlign = ContentAlignment.MiddleRight
lbl_t_desc.Location = Point(355, 36)
lbl_t_desc.Parent = grp_tot

lbl("Impuestos:", 525, 38, grp_tot)
lbl_t_imp = Label()
lbl_t_imp.Text = "$0.00"
lbl_t_imp.Font = F_BODY
lbl_t_imp.AutoSize = False
lbl_t_imp.Size = Size(150, 20)
lbl_t_imp.TextAlign = ContentAlignment.MiddleRight
lbl_t_imp.Location = Point(605, 36)
lbl_t_imp.Parent = grp_tot

lbl("TOTAL:", 830, 35, grp_tot, F_H2)
lbl_t_total = Label()
lbl_t_total.Text = "$0.00"
lbl_t_total.Font = Font("Segoe UI", 13.0, FontStyle.Bold)
lbl_t_total.ForeColor = C_PRIMARY
lbl_t_total.AutoSize = False
lbl_t_total.Size = Size(190, 26)
lbl_t_total.TextAlign = ContentAlignment.MiddleRight
lbl_t_total.Location = Point(905, 32)
lbl_t_total.Parent = grp_tot

lbl("Son:", 15, 66, grp_tot)
lbl_t_letra = Label()
lbl_t_letra.Text = "SON: CERO PESOS 00/100 M.N."
lbl_t_letra.Font = Font("Segoe UI", 8.5, FontStyle.Italic)
lbl_t_letra.ForeColor = C_MUTED
lbl_t_letra.AutoSize = True
lbl_t_letra.Location = Point(55, 67)
lbl_t_letra.Parent = grp_tot

top_y[0] += 100

# ═══════════════════ 5. FOOTER ═══════════════════
footer_y = frm.ClientSize.Height - 44
pnl_footer = Panel()
pnl_footer.Location = Point(0, footer_y)
pnl_footer.Size = Size(1200, 44)
pnl_footer.BackColor = C_PANEL
pnl_footer.Parent = frm


def _footer_paint(sender, ev):
    ev.Graphics.DrawLine(Pen(C_BORDER), 0, 0, 1200, 0)


pnl_footer.Paint += _footer_paint
lbl("Elaboró:", 20, 13, pnl_footer)
try:
    elaboro = ctx.erp.UserName
except Exception:
    elaboro = ""
txt_elaboro = TextBox()
txt_elaboro.Text = elaboro or ""
txt_elaboro.Location = Point(80, 11)
txt_elaboro.Size = Size(200, 23)
txt_elaboro.ReadOnly = True
txt_elaboro.Font = F_BODY
txt_elaboro.Parent = pnl_footer
lbl("Mod. 152  Factura de Compra (Python)  ·  no afecta inventario  ·  Usa 💾 Guardar (F5).", 300, 13, pnl_footer)

# ═══════════════════ NÚMERO A LETRAS ═══════════════════
UNIDADES = ["", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ",
            "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE"]
DECENAS = ["", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"]
VEINTI = ["VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"]
CENTENAS = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
            "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"]


def centenas_(n):
    if n == 0:
        return ""
    if n == 100:
        return "CIEN"
    s = ""
    c, r = n // 100, n % 100
    if c > 0:
        s += CENTENAS[c] + " "
    if r > 0:
        if r <= 20:
            s += UNIDADES[r]
        else:
            d, u = r // 10, r % 10
            if d == 2 and u > 0:
                s += VEINTI[u - 1]
            else:
                s += DECENAS[d]
                if u > 0:
                    s += " Y " + UNIDADES[u]
    return s.strip()


def en_letras(n):
    if n == 0:
        return "CERO"
    resultado = ""
    millones, n = n // 1000000, n % 1000000
    miles, n = n // 1000, n % 1000
    resto = n
    if millones > 0:
        resultado += "UN MILLÓN " if millones == 1 else centenas_(millones) + " MILLONES "
    if miles > 0:
        resultado += "MIL " if miles == 1 else centenas_(miles) + " MIL "
    if resto > 0:
        resultado += centenas_(resto)
    resultado = resultado.strip()
    if resultado.endswith("UNO"):
        resultado = resultado[:-3] + "UN"
    return resultado


def numero_a_letras(valor):
    if valor < 0:
        valor = 0
    entero = int(valor)
    centavos = int(round((valor - entero) * 100))
    return en_letras(entero) + " PESOS " + str(centavos).zfill(2) + "/100"


# ═══════════════════ LÓGICA CORE ═══════════════════
def refresh_grid():
    grid.Rows.Clear()
    subtotal = descuento_total = impuestos_total = 0.0
    for it in partidas_a_facturar:
        importe = it.Qty * it.Precio
        desc_monto = importe * it.DescuentoPerc
        neto = importe - desc_monto
        imp_monto = neto * it.TaxPerc
        subtotal += importe
        descuento_total += desc_monto
        impuestos_total += imp_monto
        tax_label = tax_label_by_type.get(it.TaxTypeId, "")
        grid.Rows.Add(it.PID, it.OcsResumen, it.Key, it.Desc, it.Unit, it.Pendiente, it.Qty, it.Precio,
                      tax_label, it.DescuentoPerc * 100, importe)
    lbl_part_vacio.Visible = len(partidas_a_facturar) == 0

    gran_total = subtotal - descuento_total + impuestos_total
    lbl_t_subtotal.Text = "$" + ("%.2f" % subtotal)
    lbl_t_desc.Text = "-$" + ("%.2f" % descuento_total)
    lbl_t_imp.Text = "$" + ("%.2f" % impuestos_total)
    lbl_t_total.Text = "$" + ("%.2f" % gran_total)
    lbl_t_letra.Text = "SON: " + numero_a_letras(gran_total) + " M.N."


def limpiar_todo():
    for it in partidas_a_facturar:
        it.Qty = it.Pendiente
    refresh_grid()
    txt_obs.Clear()


def crear_factura():
    if len(partidas_a_facturar) == 0 or all(p.Qty <= 0 for p in partidas_a_facturar):
        msg("Marca al menos una partida con cantidad a facturar mayor a 0.")
        return
    for it in partidas_a_facturar:
        if it.Qty > it.Pendiente + 0.0001:
            msg("La cantidad a facturar de \"%s\" (%.2f) no puede ser mayor a lo pendiente (%.2f)." % (it.Desc, it.Qty, it.Pendiente), "Cantidad inválida")
            return

    cond_idx = cbo_cond.SelectedIndex
    cond_id = cond_items[cond_idx][1] if 0 <= cond_idx < len(cond_items) else 1

    if not confirmar("¿Crear factura de compra con %d partida(s)?" % sum(1 for p in partidas_a_facturar if p.Qty > 0)):
        return

    frm.Cursor = System.Windows.Forms.Cursors.WaitCursor
    tool_buttons["guardar"].Enabled = False
    try:
        doc = ctx.erp.NuevoDocumento(152, depot_origen, proveedor_be)
        if doc <= 0 or (ctx.erp.LastError or ""):
            raise Exception("NuevoDocumento: " + (ctx.erp.LastError or ""))

        folio_manual = (txt_folio.Text or "").strip()
        sql = ("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=" + str(cond_id) + ", StatusDeliveryID=0, "
               "CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL, "
               "DateDocument='" + dt_fecha.Value.ToString("yyyyMMdd") + "', DateDelivery=GETDATE(), "
               "Comments='" + txt_obs.Text.replace("'", "''") + "'" +
               (", Folio=N'" + folio_manual.replace("'", "''") + "'" if folio_manual else "") +
               " WHERE DocumentID=" + str(doc))
        ctx.execute(sql)

        for pc in partidas_a_facturar:
            if pc.Qty <= 0:
                continue
            restante = pc.Qty
            for fuente in pc.Fuentes:
                if restante <= 0.0001:
                    break
                a_facturar_de_esta = min(restante, fuente.Pendiente)
                if a_facturar_de_esta <= 0.0001:
                    continue
                # Sin 8vo parametro -- ese escribe DeliverDocumentItemID (Recepcion), no
                # SourceDocumentItemID (Factura). El vinculo real se pone con el UPDATE de abajo.
                item_id = ctx.erp.AgregarArticulo(doc, pc.PID, a_facturar_de_esta, pc.Precio, -1, pc.TaxTypeId, pc.DescuentoPerc)
                if ctx.erp.LastError:
                    raise Exception("AgregarArticulo: " + ctx.erp.LastError)
                ctx.execute("UPDATE docDocumentItem SET SourceDocumentItemID=" + str(fuente.SourceItemId) + " WHERE DocumentItemID=" + str(item_id))
                restante -= a_facturar_de_esta

        supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(proveedor_be))
        for pid in {p.PID for p in partidas_a_facturar if p.Qty > 0}:
            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id) + ") " +
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id) + ", 0, 3, 0)"
            )

        ctx.erp.RecalcCompleto(doc)
        if ctx.erp.LastError:
            raise Exception("RecalcCompleto: " + ctx.erp.LastError)
        # Factura de Compra NO afecta inventario -- SIN AffectStockNEW.
        ctx.erp.Save(doc)
        if ctx.erp.LastError:
            raise Exception("Save: " + ctx.erp.LastError)

        # PaymentAgenda: NuevoDocumento crea un placeholder Amount=0 que Save() no corrige --
        # se regenera a mano con los porcentajes reales de engPaymentTermDetail.
        try:
            total = float(ctx.scalar("SELECT Total FROM docDocument WHERE DocumentID=" + str(doc)) or 0)
            detalle = ctx.query("SELECT PaymentPerc, PaymentUnit, PaymentPeriod FROM engPaymentTermDetail WHERE PaymentTermID=" + str(cond_id) + " ORDER BY PaymentTermDetailID")
            if detalle:
                ctx.execute("UPDATE docDocumentPaymentAgenda SET DeletedOn=GETDATE() WHERE DeletedOn IS NULL AND DocumentID=" + str(doc))
                numero = 1
                for d in detalle:
                    perc = float(d["PaymentPerc"] or 0)
                    unidad = int(d["PaymentUnit"] or 1)
                    periodo = int(d["PaymentPeriod"] or 0)
                    if unidad == 3:
                        expr = "DATEADD(MONTH,%d,GETDATE())" % periodo
                    elif unidad == 2:
                        expr = "DATEADD(DAY,%d,GETDATE())" % (periodo * 7)
                    else:
                        expr = "DATEADD(DAY,%d,GETDATE())" % periodo
                    monto = total * perc / 100.0
                    ctx.execute(
                        "INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, Amount, PartialityNumber, CreatedOn, CreatedBy) VALUES (" +
                        str(doc) + ", " + expr + ", " + str(perc) + ", " + str(monto) + ", " + str(numero) + ", GETDATE(), " + str(ctx.user_id) + ")"
                    )
                    numero += 1
        except Exception as ex_pa:
            msg("La factura se creó, pero no se pudo regenerar la agenda de pago: " + str(ex_pa), "Aviso")

        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass

        msg("Factura de compra " + str(doc) + " creada exitosamente.", "OK")
        frm.Close()
    except Exception as ex:
        msg(str(ex), "Error")
    finally:
        frm.Cursor = System.Windows.Forms.Cursors.Default
        tool_buttons["guardar"].Enabled = True


# ═══════════════════ EVENTOS ═══════════════════
def wire_tool(key, action):
    host = tool_buttons[key]

    def handler(sender, ev):
        try:
            action()
        except Exception as ex:
            msg(str(ex), "Error")

    host.Click += handler
    for ch in host.Controls:
        ch.Click += handler


wire_tool("guardar", crear_factura)
wire_tool("cancelar", lambda: frm.Close())
wire_tool("limpiar", lambda: limpiar_todo() if confirmar("¿Restablecer las cantidades a lo pendiente y limpiar comentarios?") else None)


def on_cell_end_edit(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas_a_facturar):
        return
    it = partidas_a_facturar[ev.RowIndex]
    col_name = grid.Columns[ev.ColumnIndex].Name
    try:
        if col_name == "Cantidad":
            it.Qty = float(grid.Rows[ev.RowIndex].Cells["Cantidad"].Value or it.Qty)
        elif col_name == "Precio":
            it.Precio = float(grid.Rows[ev.RowIndex].Cells["Precio"].Value or it.Precio)
        elif col_name == "DescPerc":
            it.DescuentoPerc = float(grid.Rows[ev.RowIndex].Cells["DescPerc"].Value or it.DescuentoPerc * 100) / 100.0
    except Exception:
        pass
    if it.Qty < 0:
        it.Qty = 0
    if it.Qty > it.Pendiente:
        it.Qty = it.Pendiente
    if it.DescuentoPerc < 0:
        it.DescuentoPerc = 0
    if it.DescuentoPerc > 1:
        it.DescuentoPerc = 1
    grid.BeginInvoke(System.Action(refresh_grid))


grid.CellEndEdit += on_cell_end_edit

frm.FormClosing += lambda sender, ev: None


def on_key_down(sender, ev):
    if ev.KeyCode == Keys.F5:
        crear_factura()
        ev.Handled = True
    elif ev.KeyCode == Keys.Escape:
        frm.Close()
        ev.Handled = True


frm.KeyDown += on_key_down

# ═══════════════════ ARRANQUE: MODELESS ═══════════════════
precargar_folio()
refresh_grid()
frm.Show()
