# lang: python
# timeout: 1800
#
# PLANTILLA: Recepción de Compra -- version Forms (Python, WinForms real via pythonnet)
# La MISMA logica que PLANTILLA_RECEPCION_COMPRA_FORMS_CSHARP.ctx (modulo 184, documento
# DERIVADO de N Ordenes de Compra, consolida por producto, captura lote/numero de serie,
# SI afecta inventario). Ver esa plantilla para la explicacion completa del patron (costo=
# precio unitario, QuantityToBeDelivered negativo, docDocumentLot/SerialNumber) y
# MANUAL.md #10 ("Ventanas WinForms: modeless").

import pythonnet
pythonnet.load("netfx")

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System.Drawing import (
    Point, Size, Color, Font, FontStyle, SolidBrush, Pen, ContentAlignment, RectangleF,
)
from System.Windows.Forms import (
    Form, FormStartPosition, FormBorderStyle, Panel, Label, TextBox, ComboBox, ComboBoxStyle,
    NumericUpDown, Button, FlatStyle, DataGridView, DataGridViewTextBoxColumn, DataGridViewCheckBoxColumn,
    DataGridViewContentAlignment, DataGridViewAutoSizeColumnMode, DataGridViewHeaderBorderStyle,
    DataGridViewCellBorderStyle, DataGridViewSelectionMode, DateTimePicker, DateTimePickerFormat,
    BorderStyle, ScrollBars, Cursors, Keys, MessageBox, MessageBoxButtons, MessageBoxIcon, DialogResult,
    OpenFileDialog,
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


# ═══════════════════ CLASES ═══════════════════
class Item:
    def __init__(self, pid, source_item_id, oc_folio, key, desc, unit, tax_type_id, tax_perc, precio, descuento_perc, pendiente, use_lot, use_serial):
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
        self.UseLot = use_lot
        self.UseSerialNumber = use_serial


class LoteCap:
    def __init__(self, lote, cantidad, caducidad=None):
        self.Lote = lote
        self.Cantidad = cantidad
        self.Caducidad = caducidad


class PartidaConsolidada:
    def __init__(self, pid, key, desc, unit, tax_type_id, tax_perc, precio, descuento_perc, use_lot, use_serial):
        self.PID = pid
        self.Key = key
        self.Desc = desc
        self.Unit = unit
        self.TaxTypeId = tax_type_id
        self.TaxPerc = tax_perc
        self.Precio = precio
        self.DescuentoPerc = descuento_perc
        self.UseLot = use_lot
        self.UseSerialNumber = use_serial
        self.Pendiente = 0.0
        self.Qty = 0.0
        self.OcsResumen = ""
        self.Fuentes = []
        self.Series = []
        self.Lotes = []


class OcPendiente:
    def __init__(self, document_id, folio, fecha):
        self.DocumentID = document_id
        self.Folio = folio
        self.Fecha = fecha
        self.Partidas = []


# ═══════════════════ DATOS INICIALES ═══════════════════
almacenes = ctx.query("SELECT DepotID, DepotName FROM orgDepot WHERE DeletedOn IS NULL ORDER BY DepotName")
proveedores_con_oc = ctx.query("""
    SELECT DISTINCT be.BusinessEntityID, be.OfficialName, ISNULL(m.OfficialNumber,'') AS RFC
    FROM docDocument d
    INNER JOIN docDocumentItem i ON i.DocumentID = d.DocumentID AND i.DeletedOn IS NULL AND i.MustBeDelivered <> 0
    INNER JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID
    LEFT JOIN orgBusinessEntityMainInfo m ON m.BusinessEntityID = be.BusinessEntityID
    WHERE d.ModuleID = 183 AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
    ORDER BY be.OfficialName
""")

ocs_pendientes = []
partidas_a_recibir = []
prov_all = []

# ═══════════════════ COLORES / FUENTES ═══════════════════
C_BG      = Color.FromArgb(241, 245, 249)
C_BORDER  = Color.FromArgb(203, 213, 225)
C_PRIMARY = Color.FromArgb(21, 128, 61)   # verde: distingue de OC (azul) y Requisicion
C_DANGER  = Color.FromArgb(239, 68, 68)
C_TEXT    = Color.FromArgb(30, 41, 59)
C_MUTED   = Color.FromArgb(100, 116, 139)
C_PANEL   = Color.White
C_HEADER  = Color.FromArgb(248, 250, 252)
C_BLUE_SEL = Color.FromArgb(220, 252, 231)
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
frm.Text = "Nueva Recepción de Compra (Python)"
frm.Size = Size(1160, 900)
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


def col(grid, name, header, width, read_only, align=None, fmt=None, chk=False):
    if chk:
        c = DataGridViewCheckBoxColumn()
        c.Name = name
        c.HeaderText = header
        c.Width = width
    else:
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
ribbon.Size = Size(1120, 96)
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


add_toolbar_btn("guardar",   "\U0001F4BE", "Guardar\nF5", Color.FromArgb(96, 165, 250))
add_toolbar_btn("nueva",     "➕", "Guardar y\nNueva")
add_toolbar_btn("cancelar",  "❌", "Cancelar\nEsc", Color.FromArgb(248, 113, 113))
add_sep()
add_toolbar_btn("refrescar", "\U0001F504", "Refrescar\npendientes")
add_sep()
add_toolbar_btn("limpiar",   "\U0001F9F9", "Limpiar\ncampos")

info_pnl = Panel()
info_pnl.Location = Point(700, 8)
info_pnl.Size = Size(412, 80)
info_pnl.BackColor = C_RIBBON
info_pnl.Parent = ribbon


def _info_paint(sender, ev):
    ev.Graphics.DrawRectangle(Pen(C_RIBBON_MU), 0, 6, info_pnl.Width - 1, info_pnl.Height - 10)


info_pnl.Paint += _info_paint
lbl("Información del documento", 8, 0, info_pnl, Font("Segoe UI", 8.0)).ForeColor = C_RIBBON_MU

lbl_fecha_lbl = lbl("Fecha:", 12, 24, info_pnl)
lbl_fecha_lbl.ForeColor = C_RIBBON_TX
dt_fecha = DateTimePicker()
dt_fecha.Location = Point(60, 21)
dt_fecha.Size = Size(100, 23)
dt_fecha.Format = DateTimePickerFormat.Short
dt_fecha.Font = F_BODY
dt_fecha.Parent = info_pnl

lbl_folio_lbl = lbl("Folio:", 12, 52, info_pnl)
lbl_folio_lbl.ForeColor = C_RIBBON_TX
txt_folio = TextBox()
txt_folio.Location = Point(60, 49)
txt_folio.Size = Size(100, 23)
txt_folio.Font = F_BODY
txt_folio.Parent = info_pnl

lbl_almacen_lbl = lbl("Almacén:", 312, 24, info_pnl)
lbl_almacen_lbl.ForeColor = C_RIBBON_TX
cbo_dep = ComboBox()
cbo_dep.Location = Point(312, 45)
cbo_dep.Size = Size(90, 23)
cbo_dep.Font = F_BODY
cbo_dep.DropDownStyle = ComboBoxStyle.DropDownList
cbo_dep.FlatStyle = FlatStyle.Flat
cbo_dep.Parent = info_pnl
dep_items = []
for d in almacenes:
    label = str(d["DepotName"])
    dep_items.append((label, int(d["DepotID"])))
    cbo_dep.Items.Add(label)
if cbo_dep.Items.Count > 0:
    cbo_dep.SelectedIndex = 0


def depot_sel():
    idx = cbo_dep.SelectedIndex
    return dep_items[idx][1] if 0 <= idx < len(dep_items) else 1


top_y = [116]

# ═══════════════════ 1. PROVEEDOR ═══════════════════
grp_prov = create_group("1. Proveedor (con Órdenes de Compra)", 10, top_y[0], 1120, 70, frm)
lbl("Buscar (RFC/Nombre):", 15, 38, grp_prov)
cbo_prov = ComboBox()
cbo_prov.Location = Point(160, 36)
cbo_prov.Size = Size(400, 23)
cbo_prov.Font = F_BODY
cbo_prov.DropDownStyle = ComboBoxStyle.DropDown
cbo_prov.FlatStyle = FlatStyle.Flat
cbo_prov.Parent = grp_prov
for p in proveedores_con_oc:
    prov_all.append((str(p["OfficialName"]), int(p["BusinessEntityID"]), str(p["RFC"] or "")))
for o in prov_all:
    cbo_prov.Items.Add(o[0])

lbl("RFC:", 590, 38, grp_prov)
txt_rfc = TextBox()
txt_rfc.Location = Point(630, 36)
txt_rfc.Size = Size(180, 23)
txt_rfc.ReadOnly = True
txt_rfc.Font = F_BODY
txt_rfc.ForeColor = C_MUTED
txt_rfc.Parent = grp_prov

top_y[0] += 80

# ═══════════════════ 2. ÓRDENES DE COMPRA PENDIENTES ═══════════════════
grp_ocs = create_group("2. Órdenes de Compra pendientes de recibir (marca una o varias)", 10, top_y[0], 1120, 190, frm)
grid_ocs = DataGridView()
grid_ocs.Location = Point(15, 35)
grid_ocs.Size = Size(1090, 145)
grid_ocs.BackgroundColor = C_PANEL
grid_ocs.BorderStyle = BorderStyle.FixedSingle
grid_ocs.ColumnHeadersBorderStyle = HEADER_BORDER_NONE
grid_ocs.EnableHeadersVisualStyles = False
grid_ocs.RowHeadersVisible = False
grid_ocs.AllowUserToAddRows = False
grid_ocs.AllowUserToDeleteRows = False
grid_ocs.SelectionMode = DataGridViewSelectionMode.CellSelect
grid_ocs.MultiSelect = False
grid_ocs.GridColor = C_BORDER
grid_ocs.Parent = grp_ocs
grid_ocs.ColumnHeadersDefaultCellStyle.BackColor = C_HEADER
grid_ocs.ColumnHeadersDefaultCellStyle.Font = F_H2
grid_ocs.ColumnHeadersHeight = 26
col(grid_ocs, "Incluir", "", 40, False, chk=True)
col(grid_ocs, "DocID", "DocID", 0, True).Visible = False
col(grid_ocs, "Folio", "FOLIO", 110, True)
col(grid_ocs, "Fecha", "FECHA", 100, True)
c_desc_oc = col(grid_ocs, "Partidas", "PARTIDAS PENDIENTES", 400, True)
c_desc_oc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
col(grid_ocs, "NumPartidas", "# PARTIDAS", 90, True)

lbl_ocs_vacio = Label()
lbl_ocs_vacio.Text = "Selecciona un proveedor arriba."
lbl_ocs_vacio.Font = F_BODY
lbl_ocs_vacio.ForeColor = C_MUTED
lbl_ocs_vacio.AutoSize = True
lbl_ocs_vacio.Location = Point(400, 60)
lbl_ocs_vacio.Parent = grp_ocs

top_y[0] += 200

# ═══════════════════ 3. PARTIDAS A RECIBIR ═══════════════════
grp_part = create_group("3. Partidas a recibir", 10, top_y[0], 1120, 260, frm)
grid = DataGridView()
grid.Location = Point(15, 35)
grid.Size = Size(1090, 215)
grid.BackgroundColor = C_PANEL
grid.BorderStyle = BorderStyle.FixedSingle
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

col(grid, "Incluir", "", 40, False, chk=True)
col(grid, "PID", "PID", 0, True).Visible = False
col(grid, "OcFolio", "ORDEN(ES) DE COMPRA", 160, True)
col(grid, "Key", "CLAVE", 90, True)
c_desc = col(grid, "Desc", "DESCRIPCIÓN", 200, True)
c_desc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
col(grid, "Unit", "U.M.", 55, True)
col(grid, "Pendiente", "PENDIENTE", 80, True, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Cantidad", "CANT. A RECIBIR", 110, False, DataGridViewContentAlignment.MiddleRight, "N2")
col(grid, "Lote", "LOTE", 130, True)
col(grid, "Serie", "NÚM. SERIE", 150, True)
grid.DefaultCellStyle.SelectionBackColor = C_BLUE_SEL
grid.DefaultCellStyle.SelectionForeColor = C_TEXT

lbl_part_vacio = Label()
lbl_part_vacio.Text = "Marca una o varias Órdenes de Compra arriba para ver sus partidas pendientes."
lbl_part_vacio.Font = F_BODY
lbl_part_vacio.ForeColor = C_MUTED
lbl_part_vacio.AutoSize = True
lbl_part_vacio.Location = Point(300, 100)
lbl_part_vacio.Parent = grp_part

top_y[0] += 270

# ═══════════════════ 4. COMENTARIOS ═══════════════════
grp_obs = create_group("4. Comentarios del Documento", 10, top_y[0], 1120, 75, frm)
txt_obs = TextBox()
txt_obs.Multiline = True
txt_obs.Location = Point(15, 35)
txt_obs.Size = Size(1090, 30)
txt_obs.Font = F_BODY
txt_obs.ScrollBars = ScrollBars.Vertical
txt_obs.Parent = grp_obs

top_y[0] += 85

# ═══════════════════ 5. FOOTER ═══════════════════
footer_y = frm.ClientSize.Height - 44
pnl_footer = Panel()
pnl_footer.Location = Point(0, footer_y)
pnl_footer.Size = Size(1160, 44)
pnl_footer.BackColor = C_PANEL
pnl_footer.Parent = frm


def _footer_paint(sender, ev):
    ev.Graphics.DrawLine(Pen(C_BORDER), 0, 0, 1160, 0)


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
lbl("Mod. 184  Recepción de Compra (Python)  ·  SÍ afecta inventario  ·  Usa 💾 Guardar (F5).", 300, 13, pnl_footer)


# ═══════════════════ LÓGICA CORE ═══════════════════
def prov_sel():
    idx = cbo_prov.SelectedIndex
    if 0 <= idx < len(prov_all):
        return prov_all[idx][1]
    t = (cbo_prov.Text or "").strip().lower()
    for o in prov_all:
        if o[0].lower() == t:
            return o[1]
    return 0


def actualizar_proveedor():
    idx = cbo_prov.SelectedIndex
    if 0 <= idx < len(prov_all):
        txt_rfc.Text = prov_all[idx][2] or "(sin RFC)"
    else:
        t = (cbo_prov.Text or "").strip().lower()
        m = next((o for o in prov_all if o[0].lower() == t), None)
        txt_rfc.Text = (m[2] or "(sin RFC)") if m else ""


def precargar_folio():
    try:
        pre = ctx.erp.GetFolioPrefix(184, depot_sel())
        fol = ctx.erp.GetNextFolio(184, pre, depot_sel())
        txt_folio.Text = fol or ""
    except Exception:
        txt_folio.Text = ""


def cargar_ocs_pendientes():
    ocs_pendientes.clear()
    grid_ocs.Rows.Clear()
    be = prov_sel()
    if be == 0:
        lbl_ocs_vacio.Visible = True
        lbl_ocs_vacio.Text = "Selecciona un proveedor arriba."
        return

    try:
        rows = ctx.query("""
            SELECT d.DocumentID, d.FolioPrefix, d.Folio, d.DateDocument,
                   i.DocumentItemID, i.ProductID, ISNULL(p.ProductKey, i.ProductKey) AS ProductKey,
                   ISNULL(p.ProductName, i.Description) AS ProductName, i.Unit, i.TaxTypeID, i.TaxPerc,
                   i.UnitPrice, i.DiscountPerc, i.Quantity AS Ordenado,
                   ISNULL((SELECT SUM(ri.Quantity) FROM docDocumentItem ri
                           INNER JOIN docDocument rd ON rd.DocumentID = ri.DocumentID
                           WHERE ri.DeliverDocumentItemID = i.DocumentItemID AND ri.DeletedOn IS NULL
                             AND rd.DeletedOn IS NULL AND rd.CancelledOn IS NULL), 0) AS Recibido,
                   ISNULL(p.UseLot,0) AS UseLot, ISNULL(p.UseSerialNumber,0) AS UseSerialNumber
            FROM docDocumentItem i
            INNER JOIN docDocument d ON d.DocumentID = i.DocumentID
            LEFT JOIN orgProduct p ON p.ProductID = i.ProductID
            WHERE d.ModuleID = 183 AND d.BusinessEntityID = %d AND d.DeletedOn IS NULL AND d.CancelledOn IS NULL
              AND i.DeletedOn IS NULL AND i.MustBeDelivered <> 0
              AND i.ProductID > 0 AND ISNULL(p.ProductIsService,0) = 0 AND ISNULL(p.ProductTypeID,0) <> 4
            ORDER BY d.DocumentID, i.LineNumber
        """ % be)
    except Exception as ex:
        msg("Error al calcular pendientes: " + str(ex), "Error")
        return

    por_oc = {}
    for r in rows:
        ordenado = float(r["Ordenado"] or 0)
        recibido = float(r["Recibido"] or 0)
        pendiente = ordenado - recibido
        if pendiente <= 0.0001:
            continue
        doc_id = int(r["DocumentID"])
        if doc_id not in por_oc:
            por_oc[doc_id] = OcPendiente(doc_id, str(r["FolioPrefix"] or "") + str(r["Folio"]), r["DateDocument"])
        por_oc[doc_id].Partidas.append(Item(
            int(r["ProductID"]), int(r["DocumentItemID"]), por_oc[doc_id].Folio,
            str(r["ProductKey"]), str(r["ProductName"]), str(r["Unit"]),
            int(r["TaxTypeID"] or 0), float(r["TaxPerc"] or 0), float(r["UnitPrice"] or 0),
            float(r["DiscountPerc"] or 0), pendiente,
            bool(r["UseLot"]), bool(r["UseSerialNumber"])
        ))

    for oc in sorted(por_oc.values(), key=lambda o: o.DocumentID):
        ocs_pendientes.append(oc)
        resumen = ", ".join(it.Key for it in oc.Partidas[:3])
        if len(oc.Partidas) > 3:
            resumen += "…"
        grid_ocs.Rows.Add(False, oc.DocumentID, oc.Folio, oc.Fecha.ToString("dd/MM/yyyy"), resumen, len(oc.Partidas))
    lbl_ocs_vacio.Visible = len(ocs_pendientes) == 0
    lbl_ocs_vacio.Text = "Este proveedor no tiene partidas pendientes de recibir en ninguna Orden de Compra."


def resumen_lote(it):
    if not it.UseLot:
        return ""
    if len(it.Lotes) == 0:
        return "(doble clic para capturar)"
    suma = sum(l.Cantidad for l in it.Lotes)
    return "%d lote(s) · %.2f" % (len(it.Lotes), suma)


def resumen_serie(it):
    if not it.UseSerialNumber:
        return ""
    if len(it.Series) == 0:
        return "(doble clic para capturar)"
    return "%d serie(s)" % len(it.Series)


def refresh_grid_partidas():
    grid.Rows.Clear()
    for it in partidas_a_recibir:
        i = grid.Rows.Add(True, it.PID, it.OcsResumen, it.Key, it.Desc, it.Unit, it.Pendiente, it.Qty, resumen_lote(it), resumen_serie(it))
        row = grid.Rows[i]
        row.Cells["Lote"].Style.BackColor = C_PANEL if it.UseLot else C_HEADER
        row.Cells["Lote"].Style.ForeColor = C_DANGER if (it.UseLot and len(it.Lotes) == 0) else (C_TEXT if it.UseLot else C_MUTED)
        row.Cells["Serie"].Style.BackColor = C_PANEL if it.UseSerialNumber else C_HEADER
        row.Cells["Serie"].Style.ForeColor = C_DANGER if (it.UseSerialNumber and len(it.Series) == 0) else (C_TEXT if it.UseSerialNumber else C_MUTED)
    lbl_part_vacio.Visible = len(partidas_a_recibir) == 0


def refrescar_partidas():
    fuentes = []
    for i in range(grid_ocs.Rows.Count):
        marcado = bool(grid_ocs.Rows[i].Cells["Incluir"].Value or False)
        if not marcado:
            continue
        doc_id = int(grid_ocs.Rows[i].Cells["DocID"].Value)
        oc = next((o for o in ocs_pendientes if o.DocumentID == doc_id), None)
        if oc is not None:
            fuentes.extend(oc.Partidas)

    previos = {p.PID: p for p in partidas_a_recibir}
    partidas_a_recibir.clear()

    grupos = {}
    for f in fuentes:
        grupos.setdefault(f.PID, []).append(f)

    for pid, grupo in grupos.items():
        primero = grupo[0]
        pc = PartidaConsolidada(primero.PID, primero.Key, primero.Desc, primero.Unit,
                                 primero.TaxTypeId, primero.TaxPerc, primero.Precio, primero.DescuentoPerc,
                                 primero.UseLot, primero.UseSerialNumber)
        pc.Fuentes = sorted(grupo, key=lambda f: f.SourceItemId)
        pc.OcsResumen = ", ".join(dict.fromkeys(f.OcFolio for f in grupo))
        pc.Pendiente = sum(f.Pendiente for f in pc.Fuentes)
        pc.Qty = pc.Pendiente
        if primero.PID in previos:
            prev = previos[primero.PID]
            pc.Qty = min(prev.Qty, pc.Pendiente)
            pc.Series = prev.Series
            pc.Lotes = prev.Lotes
        partidas_a_recibir.append(pc)
    refresh_grid_partidas()


def limpiar_todo():
    cbo_prov.SelectedIndex = -1
    cbo_prov.Text = ""
    ocs_pendientes.clear()
    grid_ocs.Rows.Clear()
    partidas_a_recibir.clear()
    grid.Rows.Clear()
    lbl_ocs_vacio.Visible = True
    lbl_ocs_vacio.Text = "Selecciona un proveedor arriba."
    lbl_part_vacio.Visible = True
    txt_obs.Clear()
    txt_rfc.Text = ""


# ═══════════════════ CAPTURA DE SERIES Y LOTES (doble clic en la celda) ═══════════════════
def capturar_series(pc):
    det = Form()
    det.Text = "Capturar números de serie"
    det.Size = Size(540, 500)
    det.StartPosition = FormStartPosition.CenterParent
    det.FormBorderStyle = FormBorderStyle.FixedDialog
    det.MinimizeBox = False
    det.MaximizeBox = False
    det.BackColor = C_BG

    lbl("Producto: %s — %s" % (pc.Key, pc.Desc), 15, 15, det, F_H2)
    lbl("Pega o escribe UNA serie por línea (o impórtalas desde un archivo).", 15, 38, det)

    txt = TextBox()
    txt.Multiline = True
    txt.ScrollBars = ScrollBars.Vertical
    txt.Location = Point(15, 62)
    txt.Size = Size(495, 310)
    txt.Font = F_BODY
    txt.Parent = det
    txt.Text = "\r\n".join(pc.Series)

    lbl_count = lbl("", 15, 378, det)

    def actualizar_conteo():
        lineas = [l.strip() for l in txt.Text.replace("\r", "\n").split("\n") if l.strip()]
        n = len(lineas)
        necesarias = int(round(pc.Qty))
        lbl_count.Text = "%d serie(s) capturada(s)  ·  se necesitan %d." % (n, necesarias)
        lbl_count.ForeColor = C_PRIMARY if n == necesarias else C_DANGER

    txt.TextChanged += lambda s, e: actualizar_conteo()
    actualizar_conteo()

    btn_importar = Button()
    btn_importar.Text = "Importar archivo..."
    btn_importar.Location = Point(15, 405)
    btn_importar.Size = Size(150, 28)
    btn_importar.FlatStyle = FlatStyle.Flat
    btn_importar.Font = F_BODY
    btn_importar.Parent = det

    def on_importar(sender, ev):
        ofd = OpenFileDialog()
        ofd.Filter = "CSV/TXT|*.csv;*.txt|Todos los archivos|*.*"
        if ofd.ShowDialog(det) == DialogResult.OK:
            try:
                raw = System.IO.File.ReadAllText(ofd.FileName)
                partes = [s.strip() for l in raw.replace("\r", "\n").split("\n") for s in l.split(",") if s.strip()]
                txt.Text = "\r\n".join(partes)
            except Exception as ex:
                msg("No se pudo leer el archivo: " + str(ex), "Error")

    btn_importar.Click += on_importar

    btn_ok = Button()
    btn_ok.Text = "Aceptar"
    btn_ok.Location = Point(370, 405)
    btn_ok.Size = Size(70, 28)
    btn_ok.BackColor = C_PRIMARY
    btn_ok.ForeColor = Color.White
    btn_ok.FlatStyle = FlatStyle.Flat
    btn_ok.Font = F_BODY
    btn_ok.Parent = det

    btn_cancel = Button()
    btn_cancel.Text = "Cancelar"
    btn_cancel.Location = Point(445, 405)
    btn_cancel.Size = Size(65, 28)
    btn_cancel.FlatStyle = FlatStyle.Flat
    btn_cancel.Font = F_BODY
    btn_cancel.Parent = det
    btn_cancel.Click += lambda s, e: det.Close()

    def on_ok(sender, ev):
        pc.Series = [l.strip() for l in txt.Text.replace("\r", "\n").split("\n") if l.strip()]
        det.Close()

    btn_ok.Click += on_ok
    det.AcceptButton = btn_ok
    det.ShowDialog(frm)
    refresh_grid_partidas()


def capturar_lotes(pc):
    det = Form()
    det.Text = "Capturar lotes"
    det.Size = Size(600, 460)
    det.StartPosition = FormStartPosition.CenterParent
    det.FormBorderStyle = FormBorderStyle.FixedDialog
    det.MinimizeBox = False
    det.MaximizeBox = False
    det.BackColor = C_BG

    lbl("Producto: %s — %s" % (pc.Key, pc.Desc), 15, 15, det, F_H2)
    lbl("Un producto puede recibirse en varios lotes; la suma debe ser igual a la cantidad a recibir.", 15, 38, det)

    grid_lotes = DataGridView()
    grid_lotes.Location = Point(15, 62)
    grid_lotes.Size = Size(555, 260)
    grid_lotes.BackgroundColor = C_PANEL
    grid_lotes.BorderStyle = BorderStyle.FixedSingle
    grid_lotes.RowHeadersVisible = False
    grid_lotes.AllowUserToAddRows = False
    grid_lotes.ColumnHeadersHeight = 26
    grid_lotes.GridColor = C_BORDER
    grid_lotes.Parent = det
    grid_lotes.ColumnHeadersDefaultCellStyle.BackColor = C_HEADER
    grid_lotes.ColumnHeadersDefaultCellStyle.Font = F_H2
    col(grid_lotes, "Lote", "LOTE", 180, False)
    col(grid_lotes, "Cantidad", "CANTIDAD", 110, False, DataGridViewContentAlignment.MiddleRight, "N2")
    col(grid_lotes, "Caducidad", "CADUCIDAD (dd/mm/aaaa)", 190, False)
    for l in pc.Lotes:
        grid_lotes.Rows.Add(l.Lote, l.Cantidad, l.Caducidad.ToString("dd/MM/yyyy") if l.Caducidad else "")
    if grid_lotes.Rows.Count == 0:
        grid_lotes.Rows.Add("", pc.Qty, "")

    lbl_suma = lbl("", 15, 330, det)

    def actualizar_suma():
        suma = 0.0
        for i in range(grid_lotes.Rows.Count):
            try:
                suma += float(grid_lotes.Rows[i].Cells["Cantidad"].Value or 0)
            except Exception:
                pass
        lbl_suma.Text = "Suma capturada: %.2f  ·  necesario: %.2f" % (suma, pc.Qty)
        lbl_suma.ForeColor = C_PRIMARY if abs(suma - pc.Qty) < 0.001 else C_DANGER

    grid_lotes.CellEndEdit += lambda s, e: actualizar_suma()
    actualizar_suma()

    btn_agregar = Button()
    btn_agregar.Text = "+ Agregar lote"
    btn_agregar.Location = Point(15, 358)
    btn_agregar.Size = Size(120, 28)
    btn_agregar.FlatStyle = FlatStyle.Flat
    btn_agregar.Font = F_BODY
    btn_agregar.Parent = det

    def on_agregar(sender, ev):
        grid_lotes.Rows.Add("", 0, "")
        actualizar_suma()

    btn_agregar.Click += on_agregar

    btn_quitar = Button()
    btn_quitar.Text = "Quitar lote"
    btn_quitar.Location = Point(145, 358)
    btn_quitar.Size = Size(100, 28)
    btn_quitar.FlatStyle = FlatStyle.Flat
    btn_quitar.ForeColor = C_DANGER
    btn_quitar.Font = F_BODY
    btn_quitar.Parent = det

    def on_quitar(sender, ev):
        if grid_lotes.CurrentRow is not None:
            grid_lotes.Rows.RemoveAt(grid_lotes.CurrentRow.Index)
        actualizar_suma()

    btn_quitar.Click += on_quitar

    btn_ok = Button()
    btn_ok.Text = "Aceptar"
    btn_ok.Location = Point(430, 405)
    btn_ok.Size = Size(70, 28)
    btn_ok.BackColor = C_PRIMARY
    btn_ok.ForeColor = Color.White
    btn_ok.FlatStyle = FlatStyle.Flat
    btn_ok.Font = F_BODY
    btn_ok.Parent = det

    btn_cancel = Button()
    btn_cancel.Text = "Cancelar"
    btn_cancel.Location = Point(505, 405)
    btn_cancel.Size = Size(65, 28)
    btn_cancel.FlatStyle = FlatStyle.Flat
    btn_cancel.Font = F_BODY
    btn_cancel.Parent = det
    btn_cancel.Click += lambda s, e: det.Close()

    def on_ok(sender, ev):
        lista = []
        for i in range(grid_lotes.Rows.Count):
            r = grid_lotes.Rows[i]
            lote = str(r.Cells["Lote"].Value or "").strip()
            if not lote:
                continue
            try:
                cant = float(r.Cells["Cantidad"].Value or 0)
            except Exception:
                cant = 0
            if cant <= 0:
                continue
            cad = None
            fecha_txt = str(r.Cells["Caducidad"].Value or "").strip()
            if fecha_txt:
                ok, f = System.DateTime.TryParse(fecha_txt)
                if ok:
                    cad = f
            lista.append(LoteCap(lote, cant, cad))
        pc.Lotes = lista
        det.Close()

    btn_ok.Click += on_ok
    det.AcceptButton = btn_ok
    det.ShowDialog(frm)
    refresh_grid_partidas()


# ═══════════════════ CREAR EN CONTPAQi ═══════════════════
def crear_recepcion(nueva):
    be = prov_sel()
    if be == 0:
        msg("Selecciona un proveedor.")
        return
    if len(partidas_a_recibir) == 0:
        msg("Marca al menos una Orden de Compra con partidas pendientes.")
        return
    if all(p.Qty <= 0 for p in partidas_a_recibir):
        msg("Captura una cantidad a recibir mayor a 0 en al menos una partida.")
        return
    for it in partidas_a_recibir:
        if it.Qty <= 0:
            continue
        if it.Qty > it.Pendiente + 0.0001:
            msg("La cantidad a recibir de \"%s\" (%.2f) no puede ser mayor a lo pendiente (%.2f)." % (it.Desc, it.Qty, it.Pendiente), "Cantidad inválida")
            return
        if it.UseSerialNumber and len(it.Series) != int(round(it.Qty)):
            msg("\"%s\" requiere exactamente %d número(s) de serie (capturados: %d). Doble clic en la columna SERIE." % (it.Desc, round(it.Qty), len(it.Series)), "Series incompletas")
            return
        if it.UseLot:
            suma_lotes = sum(l.Cantidad for l in it.Lotes)
            if len(it.Lotes) == 0 or abs(suma_lotes - it.Qty) > 0.001:
                msg("\"%s\" requiere que la suma de lotes (%.2f) sea igual a la cantidad a recibir (%.2f). Doble clic en la columna LOTE." % (it.Desc, suma_lotes, it.Qty), "Lotes incompletos")
                return
            if any(l.Caducidad is None for l in it.Lotes):
                msg("Todos los lotes de \"%s\" requieren fecha de caducidad." % it.Desc, "Falta información")
                return

    ocs_folios = list(dict.fromkeys(f.OcFolio for p in partidas_a_recibir if p.Qty > 0 for f in p.Fuentes))
    if not confirmar("¿Crear recepción de compra con %d partida(s) de %d orden(es) de compra?" % (sum(1 for p in partidas_a_recibir if p.Qty > 0), len(ocs_folios))):
        return

    frm.Cursor = Cursors.WaitCursor
    tool_buttons["guardar"].Enabled = False
    try:
        dep = depot_sel()
        doc = ctx.erp.NuevoDocumento(184, dep, be)
        if doc <= 0 or (ctx.erp.LastError or ""):
            raise Exception("NuevoDocumento: " + (ctx.erp.LastError or ""))

        primera_oc_doc_id = 0
        docs_incluidos = [oc.DocumentID for oc in ocs_pendientes
                          if any(f in [ff for p in partidas_a_recibir if p.Qty > 0 for ff in p.Fuentes] for f in oc.Partidas)]
        if docs_incluidos:
            primera_oc_doc_id = min(docs_incluidos)

        folio_manual = (txt_folio.Text or "").strip()
        sql = ("UPDATE docDocument SET DepotIDFrom=0, UserID=0, PaymentTermID=0, "
               "CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL, "
               "DateDocument='" + dt_fecha.Value.ToString("yyyyMMdd") + "', " +
               "SourceDocumentID=" + (str(primera_oc_doc_id) if primera_oc_doc_id > 0 else "NULL") + ", " +
               "Comments='" + txt_obs.Text.replace("'", "''") + "'" +
               (", Folio=N'" + folio_manual.replace("'", "''") + "'" if folio_manual else "") +
               " WHERE DocumentID=" + str(doc))
        ctx.execute(sql)

        for pc in partidas_a_recibir:
            if pc.Qty <= 0:
                continue
            restante = pc.Qty
            serie_idx = 0
            lote_queue = [LoteCap(l.Lote, l.Cantidad, l.Caducidad) for l in pc.Lotes]

            for fuente in pc.Fuentes:
                if restante <= 0.0001:
                    break
                a_recibir_de_esta = min(restante, fuente.Pendiente)
                if a_recibir_de_esta <= 0.0001:
                    continue

                # Costo = Precio Unitario en documentos de compra (confirmado contra el
                # sandbox real).
                item_id = ctx.erp.AgregarArticulo(doc, pc.PID, a_recibir_de_esta, pc.Precio, pc.Precio, pc.TaxTypeId, pc.DescuentoPerc, fuente.SourceItemId)
                if ctx.erp.LastError:
                    raise Exception("AgregarArticulo: " + ctx.erp.LastError)

                if pc.UseSerialNumber:
                    n = int(round(a_recibir_de_esta))
                    k = 0
                    while k < n and serie_idx < len(pc.Series):
                        ctx.execute(
                            "INSERT INTO docDocumentSerialNumber (DocumentID, DocumentItemID, ProductID, SerialNumber, Quantity, DepotID, StatusID, CreatedOn, CreatedBy) VALUES (" +
                            str(doc) + ", " + str(item_id) + ", " + str(pc.PID) + ", N'" + pc.Series[serie_idx].replace("'", "''") + "', 1, " + str(dep) + ", 1, GETDATE(), " + str(ctx.user_id) + ")"
                        )
                        serie_idx += 1
                        k += 1
                if pc.UseLot:
                    falta_en_este_item = a_recibir_de_esta
                    while falta_en_este_item > 0.0001 and len(lote_queue) > 0:
                        l = lote_queue[0]
                        usar = min(l.Cantidad, falta_en_este_item)
                        # OJO: la tabla real es docDocumentLot (NO docDocumentItemLot).
                        exp_cad = "'" + l.Caducidad.ToString("yyyyMMdd") + "'" if l.Caducidad else "NULL"
                        ctx.execute(
                            "INSERT INTO docDocumentLot (DocumentID, DocumentItemID, ProductID, Lot, ExpirationDate, "
                            "Quantity, Unit, BaseUnit, QuantityBaseUnit, DepotID, CreatedOn, CreatedBy) VALUES (" +
                            str(doc) + ", " + str(item_id) + ", " + str(pc.PID) + ", N'" + l.Lote.replace("'", "''") + "', " + exp_cad + ", " +
                            str(usar) + ", N'" + pc.Unit.replace("'", "''") + "', N'" + pc.Unit.replace("'", "''") + "', " +
                            str(usar) + ", " + str(dep) + ", GETDATE(), " + str(ctx.user_id) + ")"
                        )
                        l.Cantidad -= usar
                        falta_en_este_item -= usar
                        if l.Cantidad <= 0.0001:
                            lote_queue.pop(0)

                restante -= a_recibir_de_esta

        supplier_id = ctx.scalar("SELECT SupplierID FROM orgSupplier WHERE BusinessEntityID=" + str(be))
        for pid in {p.PID for p in partidas_a_recibir if p.Qty > 0}:
            ctx.execute(
                "IF NOT EXISTS (SELECT 1 FROM orgProductSupplier WHERE ProductID=" + str(pid) + " AND SupplierID=" + str(supplier_id) + ") " +
                "INSERT INTO orgProductSupplier (ProductID, SupplierID, CostPrice, CurrencyID, OrderNumber) VALUES (" + str(pid) + ", " + str(supplier_id) + ", 0, 3, 0)"
            )

        ctx.erp.RecalcCompleto(doc)
        if ctx.erp.LastError:
            raise Exception("RecalcCompleto: " + ctx.erp.LastError)
        # A diferencia de la Orden de Compra: la Recepción de Compra SÍ afecta inventario.
        ctx.erp.AffectStockNEW(doc)
        if ctx.erp.LastError:
            raise Exception("AffectStockNEW: " + ctx.erp.LastError)
        ctx.erp.Save(doc)
        if ctx.erp.LastError:
            raise Exception("Save: " + ctx.erp.LastError)
        try:
            ctx.erp.UpdateStatusDelivery(doc)
        except Exception:
            pass
        try:
            ctx.erp.RefreshGrid()
        except Exception:
            pass

        msg("Recepción de compra " + str(doc) + " creada exitosamente.", "OK")
        if nueva:
            limpiar_todo()
            precargar_folio()
        else:
            frm.Close()
    except Exception as ex:
        msg(str(ex), "Error")
    finally:
        frm.Cursor = Cursors.Default
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


wire_tool("guardar", lambda: crear_recepcion(False))
wire_tool("nueva", lambda: crear_recepcion(True))
wire_tool("cancelar", lambda: frm.Close())
wire_tool("refrescar", cargar_ocs_pendientes)
wire_tool("limpiar", lambda: limpiar_todo() if (len(partidas_a_recibir) == 0 or confirmar("¿Limpiar todos los campos?")) else None)

cbo_prov.SelectedIndexChanged += lambda s, e: (actualizar_proveedor(), cargar_ocs_pendientes())
cbo_dep.SelectedIndexChanged += lambda s, e: precargar_folio()


# OJO (mismo bug real que la version C#): refrescar el grid DENTRO del mismo evento de
# checkbox puede lanzar una excepcion reentrante de WinForms -- se difiere con BeginInvoke.
def on_grid_ocs_click(sender, ev):
    if ev.RowIndex < 0:
        return
    if grid_ocs.Columns[ev.ColumnIndex].Name == "Incluir":
        def deferido():
            grid_ocs.EndEdit()
            refrescar_partidas()
        grid_ocs.BeginInvoke(System.Action(deferido))


grid_ocs.CellContentClick += on_grid_ocs_click


def on_grid_end_edit(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas_a_recibir):
        return
    it = partidas_a_recibir[ev.RowIndex]
    col_name = grid.Columns[ev.ColumnIndex].Name
    if col_name != "Cantidad":
        return
    try:
        it.Qty = float(grid.Rows[ev.RowIndex].Cells["Cantidad"].Value or it.Qty)
    except Exception:
        pass
    if it.Qty < 0:
        it.Qty = 0
    if it.Qty > it.Pendiente:
        it.Qty = it.Pendiente
    grid.BeginInvoke(System.Action(refresh_grid_partidas))


grid.CellEndEdit += on_grid_end_edit


def on_grid_click(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas_a_recibir):
        return
    if grid.Columns[ev.ColumnIndex].Name != "Incluir":
        return

    def deferido():
        grid.EndEdit()
        if ev.RowIndex >= len(partidas_a_recibir):
            return
        it = partidas_a_recibir[ev.RowIndex]
        marcado = bool(grid.Rows[ev.RowIndex].Cells["Incluir"].Value or True)
        it.Qty = (it.Pendiente if it.Qty <= 0 else it.Qty) if marcado else 0
        refresh_grid_partidas()

    grid.BeginInvoke(System.Action(deferido))


grid.CellContentClick += on_grid_click


def on_grid_dbl_click(sender, ev):
    if ev.RowIndex < 0 or ev.RowIndex >= len(partidas_a_recibir):
        return
    pc = partidas_a_recibir[ev.RowIndex]
    col_name = grid.Columns[ev.ColumnIndex].Name
    if col_name == "Serie":
        if not pc.UseSerialNumber:
            msg("Este producto no requiere número de serie.")
            return
        if pc.Qty <= 0:
            msg("Primero captura la cantidad a recibir.")
            return
        capturar_series(pc)
    elif col_name == "Lote":
        if not pc.UseLot:
            msg("Este producto no requiere lote.")
            return
        if pc.Qty <= 0:
            msg("Primero captura la cantidad a recibir.")
            return
        capturar_lotes(pc)


grid.CellDoubleClick += on_grid_dbl_click


def on_key_down(sender, ev):
    if ev.KeyCode == Keys.F5:
        crear_recepcion(False)
        ev.Handled = True
    elif ev.KeyCode == Keys.Escape:
        frm.Close()
        ev.Handled = True


frm.KeyDown += on_key_down

# ═══════════════════ ARRANQUE: MODELESS ═══════════════════
lbl_ocs_vacio.Visible = True
lbl_part_vacio.Visible = True
precargar_folio()
frm.Show()
