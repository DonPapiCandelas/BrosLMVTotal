# -*- coding: utf-8 -*-
# lang: python
# ==============================================================================
# PLANTILLA: Vincular XML CFDI recibidos a un documento (Python, WinForms)
# ==============================================================================
#
# Qué hace: busca los XML CFDI ya cargados en Comercial que aún NO están
# vinculados a ningún documento (docDocumentCFDiSAT.DocumentID = 0), filtrados
# por el RFC del proveedor/emisor del documento activo, los muestra en una
# rejilla, y permite seleccionar uno o varios y vincularlos al documento en
# curso. Pensado para el módulo de Gastos (o cualquier módulo de compras que
# reciba XML de terceros) -- no asume ningún módulo fijo, trabaja sobre el
# documento que tengas seleccionado en Comercial en el momento de ejecutarlo.
#
# Doble clic en un renglón: abre una ventana modal con el detalle completo
# de ese XML (emisor, fechas, importes, datos fiscales) antes de decidir si
# lo vinculas.
#
# PORTABILIDAD -- por qué "solo copiarlo" ya funciona en cualquier empresa:
# la versión original de este script (AccesoFacil) dependía de una vista
# propia (`zzXMLRecibidos`) que había que crear a mano en cada base nueva.
# Se investigó su definición real (confirmado contra la base de origen) y
# resultó estar armada ENTERAMENTE sobre tablas nativas de Comercial Pro
# (`docDocumentCFDiSAT`, `orgBusinessEntity`, `orgBusinessEntityMainInfo`) --
# tablas que existen igual en cualquier instalación. Esta plantilla mete esa
# misma lógica directo en la consulta, sin depender de ninguna vista que haya
# que crear antes de usarla.
#
# Lo único genuinamente propio de la empresa original era una tabla chica de
# auditoría (`ZZUuidAsociados`, 4 columnas: qué UUID se vinculó a qué
# documento y cuándo) -- se conserva como funcionalidad opcional, pero la
# plantilla la crea sola si no existe (mismo patrón ya usado en
# PLANTILLA_AUTORIZACION_POR_MONTO_SQL_PURO.sql con `BrosAutorizaciones`).
#
# Diferencias reales frente al original AccesoFacil:
# - from broslmv import ctx (en vez de gl['conn']/gl['main']).
# - ctx.query/scalar/execute con parámetros nombrados reales.
# - ctx.get_selected_ids() / ctx.user_id en vez de gl['main'].IDs/.UserID.
# - Sin filtro de empresa hardcodeado ni ruta de logo -- el logo es opcional
#   y se omite limpio si no existe; el filtro de "empresa propia" usa el
#   OwnedBusinessEntityID real del documento activo, no un nombre fijo.
# - NUEVO: doble clic en un XML abre un detalle completo en ventana modal
#   (el original no lo tenía).

import clr, System
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('System.Drawing')
clr.AddReference('System.Windows.Forms')

from System import DBNull, DateTime
from System.Drawing import Size, Point, Color, Font, FontStyle, Image, ContentAlignment
from System.Windows.Forms import (
    Form, Panel, Label, PictureBox, TextBox, Button, DataGridView,
    DataGridViewCheckBoxColumn, DataGridViewTextBoxColumn,
    FormBorderStyle, FormStartPosition, MessageBox, MessageBoxButtons, MessageBoxIcon,
    AnchorStyles, DockStyle, PictureBoxSizeMode, DataGridViewAutoSizeColumnsMode,
    DataGridViewSelectionMode, TableLayoutPanel, ColumnStyle, SizeType, Padding,
    FlatStyle, DialogResult
)

from broslmv import ctx

# ==============================================================================
# CONFIGURACIÓN (genérica -- nada específico de una empresa)
# ==============================================================================

RUTA_BITACORA = r"C:\Compac\ComercialSP\Logs\Carga XML\Bitacora_Vinculacion_XML.txt"
LOGO_PATH     = r"C:\Compac\ComercialSP\Documentación\LOGO.png"   # opcional, se omite si no existe
MAX_FILAS     = 500

HEADER_BG     = Color.FromArgb(33, 47, 60)
HEADER_FG     = Color.White
SUBTITLE_FG   = Color.Gainsboro
HEADER_HEIGHT = 90
ACCENT_BLUE   = Color.FromArgb(52, 152, 219)
ACCENT_GREEN  = Color.FromArgb(46, 204, 113)
TITLE_TEXT    = "Vincular XML CFDI al Documento"
SUBTITLE_TEXT = "Busca XML recibidos sin vincular y asócialos a este documento"

# ==============================================================================
# HELPERS DE DATOS
# ==============================================================================

def log_to_file(mensaje):
    try:
        import System.IO as IO
        directorio = IO.Path.GetDirectoryName(RUTA_BITACORA)
        if not IO.Directory.Exists(directorio):
            IO.Directory.CreateDirectory(directorio)
        contenido = "{0} | {1}\r\n".format(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), mensaje)
        IO.File.AppendAllText(RUTA_BITACORA, contenido)
    except:
        pass

def parse_float(v, default=0.0):
    try:
        if v is None: return default
        s = str(v).strip()
        if s == "": return default
        return float(s.replace(",", ""))
    except:
        return default

def get_document_context(docid):
    """Info del documento activo: RFC del emisor/proveedor, folio, total, empresa
    propia (OwnedBusinessEntityID) -- usa las mismas vistas nativas de Comercial
    (vwLBS*) que ya usaba el original, con fallback a tablas base si la vista no
    trae el dato (ej. RFC no expuesto en algunas vistas de venta)."""
    info = {
        "RFC": None, "Folio": "", "Total": 0.0, "Fecha": "", "Proveedor": "Sin proveedor",
        "OwnedBusinessEntityID": None
    }

    filas = ctx.query(
        "SELECT d.OwnedBusinessEntityID, ISNULL(d.FolioPrefix,'')+ISNULL(d.Folio,'') AS Folio, "
        "ISNULL(d.Total,0) AS Total, d.DateDocument, "
        "ISNULL(m.OfficialNumber, be.BusinessEntityKey) AS RFC, "
        "ISNULL(be.OfficialName, 'Sin proveedor') AS Proveedor "
        "FROM dbo.docDocument d WITH (NOLOCK) "
        "LEFT JOIN dbo.orgBusinessEntity be WITH (NOLOCK) ON be.BusinessEntityID = d.BusinessEntityID "
        "LEFT JOIN dbo.orgBusinessEntityMainInfo m WITH (NOLOCK) ON m.BusinessEntityID = d.BusinessEntityID "
        "WHERE d.DocumentID = @doc",
        {"doc": docid}
    )
    if filas:
        f = filas[0]
        info["OwnedBusinessEntityID"] = f.get("OwnedBusinessEntityID")
        info["Folio"] = str(f.get("Folio") or "")
        info["Total"] = float(f.get("Total") or 0.0)
        info["Fecha"] = str(f.get("DateDocument") or "")
        info["RFC"] = str(f.get("RFC") or "").strip() or None
        info["Proveedor"] = str(f.get("Proveedor") or "Sin proveedor")
    return info

def get_currency_id(moneda):
    if not moneda:
        return None
    filas = ctx.query(
        "SELECT TOP 1 CurrencyID FROM dbo.engRefCurrency WITH (NOLOCK) "
        "WHERE UPPER(ISNULL(Currency,'')) = @m OR UPPER(ISNULL(IntlSymbol,'')) = @m",
        {"m": str(moneda).upper()}
    )
    return int(filas[0]["CurrencyID"]) if filas else None

def load_xml_pendientes(rfc, owned_business_entity_id):
    """Equivalente inline de la vista zzXMLRecibidos -- misma lógica (confirmada
    contra su definición real), pero sin depender de que exista esa vista."""
    filas = ctx.query(
        "SELECT TOP {top} "
        "  sat.DocSATID, sat.RFCEmisor, sat.FechaCertificacion AS Fecha, sat.Serie, sat.Folio, "
        "  sat.Moneda, sat.TipoCambio, sat.SubTotal, sat.Descuento, sat.IVA0, sat.IVA16, sat.Total, "
        "  sat.MetodoDePago, sat.FormaDePago, sat.UsoCFDI, sat.Version, sat.TipoComprobante, "
        "  sat.UUID, sat.XMLFileName, sat.RazonSocial, "
        "  ISNULL(propia.OfficialName, '') AS EmpresaPropia, "
        "  ISNULL(emisor.OfficialName, sat.RazonSocial) AS Proveedor "
        "FROM dbo.docDocumentCFDiSAT sat WITH (NOLOCK) "
        "LEFT JOIN dbo.orgBusinessEntity propia WITH (NOLOCK) ON propia.BusinessEntityID = sat.OwnedBusinessEntityID "
        "LEFT JOIN dbo.orgBusinessEntityMainInfo m WITH (NOLOCK) ON m.OfficialNumber = sat.RFCEmisor "
        "LEFT JOIN dbo.orgBusinessEntity emisor WITH (NOLOCK) ON emisor.BusinessEntityID = m.BusinessEntityID "
        "WHERE sat.TipoComprobante = N'I - Ingreso' "
        "  AND sat.DocumentID = 0 "
        "  AND sat.DeletedOn IS NULL "
        "  AND sat.RFCEmisor = @rfc "
        "  {filtro_empresa} "
        "ORDER BY sat.FechaCertificacion DESC"
        .format(
            top=MAX_FILAS,
            filtro_empresa="AND sat.OwnedBusinessEntityID = @owned" if owned_business_entity_id else ""
        ),
        {"rfc": rfc, "owned": owned_business_entity_id} if owned_business_entity_id else {"rfc": rfc}
    )
    rows = []
    for f in filas:
        rows.append({
            "DocSATID": int(f["DocSATID"]),
            "RFCEmisor": str(f["RFCEmisor"] or ""),
            "Proveedor": str(f["Proveedor"] or ""),
            "Fecha": str(f["Fecha"] or ""),
            "Serie": str(f["Serie"] or ""),
            "Folio": str(f["Folio"] or ""),
            "Moneda": str(f["Moneda"] or ""),
            "TipoCambio": parse_float(f["TipoCambio"], None),
            "SubTotal": parse_float(f["SubTotal"]),
            "Descuento": parse_float(f["Descuento"]),
            "IVA0": parse_float(f["IVA0"]),
            "IVA16": parse_float(f["IVA16"]),
            "Total": parse_float(f["Total"]),
            "MetodoDePago": str(f["MetodoDePago"] or ""),
            "FormaDePago": str(f["FormaDePago"] or ""),
            "UsoCFDI": str(f["UsoCFDI"] or ""),
            "Version": str(f["Version"] or ""),
            "UUID": str(f["UUID"] or ""),
            "XMLFileName": str(f["XMLFileName"] or ""),
            "EmpresaPropia": str(f["EmpresaPropia"] or ""),
        })
    return rows

def asegurar_tabla_auditoria():
    ctx.execute(
        "IF OBJECT_ID('dbo.ZZUuidAsociados') IS NULL "
        "BEGIN "
        "  CREATE TABLE dbo.ZZUuidAsociados ("
        "    CID INT IDENTITY(1,1) PRIMARY KEY, "
        "    UUID NVARCHAR(50) NULL, "
        "    DocumentID BIGINT NULL, "
        "    TotalUUID FLOAT NULL, "
        "    CreatedOn DATETIME2 NULL DEFAULT (SYSDATETIME())"
        "  ) "
        "END", {}
    )

# ==============================================================================
# MODAL DE DETALLE (doble clic)
# ==============================================================================

class FrmDetalleXML(Form):
    def __init__(self, xml_data):
        self.Text = "Detalle del XML"
        self.FormBorderStyle = FormBorderStyle.FixedDialog
        self.StartPosition = FormStartPosition.CenterParent
        self.MaximizeBox = False; self.MinimizeBox = False
        self.ClientSize = Size(560, 560)
        self.BackColor = Color.White
        self.Font = Font("Segoe UI", 9.0)

        header = Panel(); header.Parent = self; header.Dock = DockStyle.Top; header.Height = 60; header.BackColor = HEADER_BG
        lblT = Label(); lblT.Parent = header; lblT.Text = "Detalle del CFDI"; lblT.ForeColor = Color.White
        lblT.Font = Font("Segoe UI", 13, FontStyle.Bold); lblT.Location = Point(20, 10); lblT.AutoSize = True
        lblU = Label(); lblU.Parent = header; lblU.Text = xml_data["UUID"]; lblU.ForeColor = SUBTITLE_FG
        lblU.Font = Font("Consolas", 9); lblU.Location = Point(22, 36); lblU.AutoSize = True

        body = TableLayoutPanel()
        body.Parent = self
        body.Location = Point(20, 76)
        body.Size = Size(520, 460)
        body.ColumnCount = 2
        body.ColumnStyles.Add(ColumnStyle(SizeType.Absolute, 180))
        body.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 100))
        body.AutoSize = True

        def fila(etiqueta, valor, negrita=False):
            lbl = Label(); lbl.Text = etiqueta; lbl.ForeColor = Color.FromArgb(90, 90, 90)
            lbl.Font = Font("Segoe UI", 9, FontStyle.Bold); lbl.AutoSize = True; lbl.Margin = Padding(0, 6, 0, 6)
            val = Label(); val.Text = valor if valor not in (None, "") else "-"
            val.Font = Font("Segoe UI", 9, FontStyle.Bold if negrita else FontStyle.Regular)
            val.AutoSize = True; val.MaximumSize = Size(320, 0); val.Margin = Padding(0, 6, 0, 6)
            body.Controls.Add(lbl); body.Controls.Add(val)

        fila("Proveedor", xml_data["Proveedor"])
        fila("RFC emisor", xml_data["RFCEmisor"])
        fila("Empresa propia", xml_data["EmpresaPropia"])
        fila("Fecha certificación", str(xml_data["Fecha"])[:19])
        fila("Serie / Folio", (xml_data["Serie"] or "-") + " / " + (xml_data["Folio"] or "-"))
        fila("Versión CFDI", xml_data["Version"])
        fila("Moneda", xml_data["Moneda"] + ("  (TC " + str(xml_data["TipoCambio"]) + ")" if xml_data["TipoCambio"] else ""))
        fila("Subtotal", "$ {:,.2f}".format(xml_data["SubTotal"]))
        fila("Descuento", "$ {:,.2f}".format(xml_data["Descuento"]))
        fila("IVA 0%", "$ {:,.2f}".format(xml_data["IVA0"]))
        fila("IVA 16%", "$ {:,.2f}".format(xml_data["IVA16"]))
        fila("Total", "$ {:,.2f}".format(xml_data["Total"]), negrita=True)
        fila("Método de pago", xml_data["MetodoDePago"])
        fila("Forma de pago", xml_data["FormaDePago"])
        fila("Uso CFDI", xml_data["UsoCFDI"])
        fila("Archivo XML", xml_data["XMLFileName"])

        btnCerrar = Button(); btnCerrar.Parent = self; btnCerrar.Text = "Cerrar"
        btnCerrar.Size = Size(100, 30); btnCerrar.Location = Point(self.ClientSize.Width - 120, self.ClientSize.Height - 44)
        btnCerrar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        btnCerrar.BackColor = ACCENT_BLUE; btnCerrar.ForeColor = Color.White; btnCerrar.FlatStyle = FlatStyle.Flat
        btnCerrar.Click += lambda s, e: self.Close()

# ==============================================================================
# FORMULARIO PRINCIPAL
# ==============================================================================

class FrmVincularXML(Form):
    def __init__(self, docid):
        self._docid = docid
        self._ctxdoc = get_document_context(docid) if docid else None
        self._rows = []

        self.Text = "Vincular XML al Documento"
        self.FormBorderStyle = FormBorderStyle.Sizable
        self.StartPosition = FormStartPosition.CenterScreen
        self.ClientSize = Size(1200, 700)
        self.BackColor = Color.White
        self.Font = Font("Segoe UI", 9.0)

        # --- HEADER ---
        self.header = Panel(); self.header.Parent = self; self.header.Dock = DockStyle.Top
        self.header.Height = HEADER_HEIGHT; self.header.BackColor = HEADER_BG

        try:
            import System.IO as IO
            if IO.File.Exists(LOGO_PATH):
                pic = PictureBox(); pic.Parent = self.header; pic.Location = Point(16, 12)
                pic.Size = Size(132, 56); pic.SizeMode = PictureBoxSizeMode.Zoom
                pic.Image = Image.FromFile(LOGO_PATH)
                titulo_x = 160
            else:
                titulo_x = 20
        except:
            titulo_x = 20

        lblTitle = Label(); lblTitle.Parent = self.header; lblTitle.Text = TITLE_TEXT
        lblTitle.ForeColor = HEADER_FG; lblTitle.Font = Font("Segoe UI", 16, FontStyle.Bold)
        lblTitle.Location = Point(titulo_x, 14); lblTitle.AutoSize = True

        lblSub = Label(); lblSub.Parent = self.header; lblSub.Text = SUBTITLE_TEXT
        lblSub.ForeColor = SUBTITLE_FG; lblSub.Font = Font("Segoe UI", 10)
        lblSub.Location = Point(titulo_x + 2, 46); lblSub.AutoSize = True

        self.lblDocInfo = Label(); self.lblDocInfo.Parent = self.header
        self.lblDocInfo.Font = Font("Consolas", 10, FontStyle.Bold); self.lblDocInfo.ForeColor = Color.Yellow
        self.lblDocInfo.AutoSize = True; self.lblDocInfo.TextAlign = ContentAlignment.TopRight
        self.lblDocInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right
        self.lblDocInfo.Location = Point(self.ClientSize.Width - 480, 12)
        if self._ctxdoc:
            self.lblDocInfo.Text = (
                "Doc: {0}   Folio: {1}\nTotal: $ {2:,.2f}\nProv: {3}"
            ).format(self._docid, self._ctxdoc["Folio"], self._ctxdoc["Total"], self._ctxdoc["Proveedor"][:40])
        else:
            self.lblDocInfo.Text = "Sin documento seleccionado"

        # --- BARRA DE BÚSQUEDA ---
        topY = HEADER_HEIGHT + 10
        Label(Parent=self, Text="RFC:", Location=Point(16, topY + 3), AutoSize=True)
        self.txtRFC = TextBox(Parent=self, Location=Point(56, topY), Size=Size(220, 24))
        if self._ctxdoc and self._ctxdoc["RFC"]:
            self.txtRFC.Text = self._ctxdoc["RFC"]

        self.btnBuscar = Button(Parent=self, Text="Buscar XML", Location=Point(286, topY - 1), Size=Size(110, 28))
        self.btnBuscar.BackColor = ACCENT_BLUE; self.btnBuscar.ForeColor = Color.White; self.btnBuscar.FlatStyle = FlatStyle.Flat
        self.btnBuscar.Click += self.on_buscar

        Label(Parent=self, Text="Filtro rápido:", Location=Point(406, topY + 3), AutoSize=True)
        self.txtFiltro = TextBox(Parent=self, Location=Point(486, topY), Size=Size(300, 24))
        self.txtFiltro.TextChanged += self.on_filtrar

        self.lblCount = Label(Parent=self, Text="", Location=Point(self.ClientSize.Width - 220, topY + 3), AutoSize=True)
        self.lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right

        # --- GRID ---
        gridTop = topY + 32
        self.grid = DataGridView()
        self.grid.Parent = self
        self.grid.Location = Point(12, gridTop)
        self.grid.Size = Size(self.ClientSize.Width - 24, self.ClientSize.Height - gridTop - 60)
        self.grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        self.grid.AllowUserToAddRows = False
        self.grid.ReadOnly = False
        self.grid.MultiSelect = False
        self.grid.RowHeadersVisible = False
        self.grid.EnableHeadersVisualStyles = False
        self.grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        self.grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245)
        self.grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250)
        self.grid.CellDoubleClick += self.on_grid_double_click

        colSel = DataGridViewCheckBoxColumn(); colSel.HeaderText = "Sel"; colSel.Width = 40
        self.grid.Columns.Add(colSel)

        def add_col(name, header, width=None, vis=True):
            c = DataGridViewTextBoxColumn()
            c.Name = name; c.HeaderText = header; c.ReadOnly = True; c.Visible = vis
            if width is not None: c.Width = width
            self.grid.Columns.Add(c)

        add_col("Fecha", "Fecha", 130)
        add_col("Serie", "Serie", 70)
        add_col("Folio", "Folio", 70)
        add_col("Moneda", "Mon", 55)
        add_col("SubTotal", "Subtotal", 90)
        add_col("IVA16", "IVA", 80)
        add_col("Total", "Total", 90)
        add_col("Proveedor", "Proveedor", 200)
        add_col("EmpresaPropia", "Empresa propia", 160)
        add_col("UUID", "UUID", 220)
        add_col("RFCEmisor", "RFC", 100, vis=False)
        add_col("DocSATID", "DocSATID", 0, vis=False)

        Label(Parent=self, Text="Doble clic en un renglón para ver el detalle completo del XML.",
              Location=Point(14, gridTop - 22), AutoSize=True, ForeColor=Color.FromArgb(120, 120, 120))

        self.btnVincular = Button(Parent=self, Text="Vincular seleccionados",
                                   Location=Point(self.ClientSize.Width - 240, self.ClientSize.Height - 36),
                                   Size=Size(220, 26))
        self.btnVincular.Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        self.btnVincular.BackColor = ACCENT_GREEN; self.btnVincular.ForeColor = Color.White; self.btnVincular.FlatStyle = FlatStyle.Flat
        self.btnVincular.Click += self.on_vincular

        if self.txtRFC.Text.strip():
            self.on_buscar(None, None)

    def bind_rows(self, rows):
        self._rows = rows
        self.grid.Rows.Clear()
        for r in rows:
            self.grid.Rows.Add(
                False, r["Fecha"], r["Serie"], r["Folio"], r["Moneda"],
                "{:,.2f}".format(r["SubTotal"]), "{:,.2f}".format(r["IVA16"]), "{:,.2f}".format(r["Total"]),
                r["Proveedor"], r["EmpresaPropia"], r["UUID"], r["RFCEmisor"], r["DocSATID"]
            )
        self.lblCount.Text = "Registros: {0}".format(len(rows))

    def on_buscar(self, sender, args):
        rfc = (self.txtRFC.Text or "").strip()
        if not rfc:
            MessageBox.Show("Captura un RFC para buscar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            return
        try:
            owned = self._ctxdoc["OwnedBusinessEntityID"] if self._ctxdoc else None
            rows = load_xml_pendientes(rfc, owned)
            self.bind_rows(rows)
            self.txtFiltro.Text = ""
        except Exception as e:
            MessageBox.Show("Error al consultar XML:\n" + str(e), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)

    def on_filtrar(self, sender, args):
        if not self._rows: return
        q = (self.txtFiltro.Text or "").lower().strip()
        if not q:
            self.bind_rows(self._rows); return
        fil = [r for r in self._rows if
               q in (r["Proveedor"] or "").lower() or q in (r["UUID"] or "").lower()
               or q in (r["Serie"] or "").lower() or q in (r["Folio"] or "").lower()
               or q in str(r["Fecha"]).lower() or q in str(r["Total"]).lower()]
        self.bind_rows(fil)

    def on_grid_double_click(self, sender, args):
        if args.RowIndex < 0: return
        docsatid = int(self.grid.Rows[args.RowIndex].Cells["DocSATID"].Value)
        data = next((r for r in self._rows if r["DocSATID"] == docsatid), None)
        if data:
            FrmDetalleXML(data).ShowDialog(self)

    def on_vincular(self, sender, args):
        # Se relee directo del grid (por DocSATID) en vez de por índice, porque
        # bind_rows/on_filtrar pueden desalinear el índice contra self._rows.
        seleccionados = []
        for i in range(self.grid.Rows.Count):
            if bool(self.grid.Rows[i].Cells[0].Value):
                docsatid = int(self.grid.Rows[i].Cells["DocSATID"].Value)
                data = next((r for r in self._rows if r["DocSATID"] == docsatid), None)
                if data: seleccionados.append(data)

        if not seleccionados:
            MessageBox.Show("Selecciona al menos un XML (columna 'Sel').", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            return
        if not self._docid:
            MessageBox.Show("No se pudo determinar el documento activo.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            return

        try:
            asegurar_tabla_auditoria()
        except: pass

        uuids_ok = []
        for item in seleccionados:
            try:
                self._vincular_uno(item)
                uuids_ok.append(item["UUID"])
            except Exception as ex:
                MessageBox.Show(
                    "Se detuvo al vincular UUID {0}:\n{1}\n\nYa vinculados antes de este: {2}".format(
                        item["UUID"], str(ex), len(uuids_ok)),
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                )
                break

        if uuids_ok:
            try:
                ctx.execute(
                    "UPDATE dbo.docDocument SET Comments = ISNULL(CAST(Comments AS NVARCHAR(MAX)), '') + ' ' + @uuids "
                    "WHERE DocumentID = @doc",
                    {"uuids": " ".join(uuids_ok), "doc": self._docid}
                )
            except: pass
            log_to_file("DocID {0}: vinculados {1} XML(s): {2}".format(self._docid, len(uuids_ok), ", ".join(uuids_ok)))
            MessageBox.Show("Se vincularon {0} XML al documento {1}.".format(len(uuids_ok), self._docid),
                             "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information)
            self.Close()

    def _vincular_uno(self, item):
        docid = self._docid
        docsatid = item["DocSATID"]
        uuid = item["UUID"]

        # ¿Ya está vinculado a OTRO documento?
        dueño_actual = ctx.scalar(
            "SELECT ISNULL(DocumentID,0) FROM dbo.docDocumentCFDiSAT WHERE DocSATID = @sat",
            {"sat": docsatid}
        )
        dueño_actual = int(dueño_actual) if dueño_actual is not None else 0
        if dueño_actual != 0 and dueño_actual != docid:
            raise Exception("Ya está asociado al documento {0}.".format(dueño_actual))

        # Vincula el XML a este documento
        ctx.execute(
            "UPDATE dbo.docDocumentCFDiSAT SET DocumentID = @doc WHERE DocSATID = @sat AND DocumentID = 0",
            {"doc": docid, "sat": docsatid}
        )

        # ¿Ya hay un CFD "principal" para este documento? Si no, este XML lo es.
        uuid_existente = ctx.scalar(
            "SELECT CFDIFolioFiscal FROM dbo.docDocumentCFD WHERE DocumentID = @doc", {"doc": docid}
        )
        es_secundario = bool(uuid_existente and str(uuid_existente).strip())

        if not es_secundario:
            existe_cfd = ctx.scalar(
                "SELECT COUNT(1) FROM dbo.docDocumentCFD WHERE DocumentID = @doc", {"doc": docid}
            )
            if int(existe_cfd or 0) > 0:
                ctx.execute(
                    "UPDATE dbo.docDocumentCFD SET CFDFileXML=@xml, CFDIFolioFiscal=@uuid, "
                    "CFDIFechaCertificacion=@fecha, Anexo20Ver=@ver, Serie=@serie, Folio=@folio "
                    "WHERE DocumentID=@doc",
                    {"xml": item["XMLFileName"], "uuid": uuid, "fecha": item["Fecha"], "ver": item["Version"],
                     "serie": item["Serie"], "folio": item["Folio"], "doc": docid}
                )
            else:
                ctx.execute(
                    "INSERT INTO dbo.docDocumentCFD "
                    "(DocumentID, CFDFileXML, CFDIFolioFiscal, CFDIFechaCertificacion, Anexo20Ver, Serie, Folio, FinancialOperationID) "
                    "VALUES (@doc, @xml, @uuid, @fecha, @ver, @serie, @folio, 0)",
                    {"doc": docid, "xml": item["XMLFileName"], "uuid": uuid, "fecha": item["Fecha"],
                     "ver": item["Version"], "serie": item["Serie"], "folio": item["Folio"]}
                )

            if item["Moneda"] or item["TipoCambio"] is not None:
                curr_id = get_currency_id(item["Moneda"])
                if curr_id is not None:
                    if item["TipoCambio"] is not None:
                        ctx.execute("UPDATE dbo.docDocument SET CurrencyID=@cid, Rate=@rate WHERE DocumentID=@doc",
                                    {"cid": curr_id, "rate": item["TipoCambio"], "doc": docid})
                    else:
                        ctx.execute("UPDATE dbo.docDocument SET CurrencyID=@cid WHERE DocumentID=@doc",
                                    {"cid": curr_id, "doc": docid})
                elif item["TipoCambio"] is not None:
                    ctx.execute("UPDATE dbo.docDocument SET Rate=@rate WHERE DocumentID=@doc",
                                {"rate": item["TipoCambio"], "doc": docid})

        try:
            ctx.execute(
                "INSERT INTO dbo.ZZUuidAsociados (UUID, DocumentID, TotalUUID) VALUES (@uuid, @doc, @total)",
                {"uuid": uuid, "doc": docid, "total": item["Total"]}
            )
        except: pass

# ==============================================================================
# BOOTSTRAP
# ==============================================================================

def main():
    try:
        ids = ctx.get_selected_ids()
        docid = ids[0] if ids else None
        if not docid:
            MessageBox.Show("Selecciona un documento antes de ejecutar este botón.", "Aviso",
                             MessageBoxButtons.OK, MessageBoxIcon.Warning)
            return
        FrmVincularXML(docid).ShowDialog()
    except Exception as e:
        MessageBox.Show(str(e), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)

main()
