# lang: python
# timeout: 1800
#
# PLANTILLA_BASE_PYTHON_WINFORMS.py
# Punto de partida MINIMO para una ventana en Python (WinForms real via pythonnet).
# Sin logica de negocio: solo el esqueleto + las protecciones recomendadas. Para un
# ejemplo completo (proveedor, grid, crear documento) ver "Ejemplo Premium - Python WinForms".
#
# En Python el boton YA es modeless de forma automatica desde v2.19.0 (el addon no
# se queda esperando bloqueado) -- no hay que hacer nada especial para eso. Lo que SI
# conviene es el try/except en cada evento que haga ctx.query/ctx.erp: aqui un error
# nunca puede tumbar Comercial (Python corre en su propio proceso, aislado), pero sin
# proteccion la ventana se queda "muda" sin explicarle el error al usuario.
#
# Nota: "# timeout: 1800" (arriba) es obligatorio en cualquier script con ventana
# interactiva -- el limite normal es 120s y una ventana puede quedar abierta minutos.

import pythonnet
pythonnet.load("netfx")  # usa el .NET Framework del equipo, igual que el addon

import clr
clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")

import System
import System.Threading
from System.Drawing import Point, Size, Color
from System.Windows.Forms import (
    Form, FormStartPosition, Label, TextBox, Button, ListBox, Keys,
    MessageBox, MessageBoxButtons, MessageBoxIcon,
)

from broslmv import ctx

# Modal/WinForms necesitan un hilo STA. Debe fijarse ANTES de crear cualquier control.
System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA)


def msg(texto, titulo="BrosLMV"):
    MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Information)


# ═══════════════════ VENTANA ═══════════════════
frm = Form()
frm.Text = "Nueva ventana"
frm.Size = Size(520, 360)
frm.StartPosition = FormStartPosition.CenterScreen
frm.BackColor = Color.FromArgb(241, 245, 249)

lbl = Label()
lbl.Text = "Buscar:"
lbl.Location = Point(16, 20)
lbl.AutoSize = True
lbl.Parent = frm

txt_buscar = TextBox()
txt_buscar.Location = Point(16, 42)
txt_buscar.Size = Size(300, 23)
txt_buscar.Parent = frm

btn_buscar = Button()
btn_buscar.Text = "Buscar"
btn_buscar.Location = Point(326, 41)
btn_buscar.Size = Size(90, 25)
btn_buscar.Parent = frm

lst = ListBox()
lst.Location = Point(16, 80)
lst.Size = Size(480, 190)
lst.Parent = frm

btn_cerrar = Button()
btn_cerrar.Text = "Cerrar"
btn_cerrar.Location = Point(16, 290)
btn_cerrar.Size = Size(90, 28)
btn_cerrar.Parent = frm


# ═══════════════════ LÓGICA (adapta esta parte a tu caso) ═══════════════════
def buscar():
    # El try/except va AQUÍ: sin él, un error de SQL deja la ventana "muda" (no
    # tumba Comercial, pero el usuario no sabría qué pasó).
    try:
        safe = txt_buscar.Text.replace("'", "''")
        filas = ctx.query(
            "SELECT TOP 50 ProductKey, ProductName FROM orgProduct "
            "WHERE DeletedOn IS NULL AND ProductName LIKE @f ORDER BY ProductName",
            {"f": "%" + safe + "%"},
        )
        lst.Items.Clear()
        for f in filas:
            lst.Items.Add(str(f["ProductKey"]) + " - " + str(f["ProductName"]))
        if lst.Items.Count == 0:
            lst.Items.Add("(sin resultados)")
    except Exception as ex:
        msg("Error al buscar: " + str(ex), "Error")


# ═══════════════════ EVENTOS ═══════════════════
def _btn_buscar_click(sender, ev):
    buscar()


def _txt_buscar_keydown(sender, ev):
    if ev.KeyCode == Keys.Enter:
        buscar()
        ev.Handled = True


btn_buscar.Click += _btn_buscar_click
txt_buscar.KeyDown += _txt_buscar_keydown
btn_cerrar.Click += lambda s, e: frm.Close()

# ═══════════════════ ARRANQUE ═══════════════════
# ShowDialog() aqui NO bloquea Comercial (v2.19.0): el addon ya no espera a que esta
# ventana cierre. Es solo el message loop de ESTE proceso Python, aislado del de Comercial.
frm.ShowDialog()

result = "Ventana cerrada."
