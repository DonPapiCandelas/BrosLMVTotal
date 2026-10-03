#!/usr/bin/env python
# Simulación del módulo `broslmv` (ctx) para probar las plantillas Python SIN Comercial: ctx.query / ctx.scalar leen de verdad el laboratorio con sqlcmd;
# ctx.execute y ctx.erp.* solo REGISTRAN lo que se les pidió (no escriben nada). Lo usan prueba_python.py y prueba_python_pagos.py.
import os
import subprocess
import sys
import types

SERVIDOR = r"localhost\compac"
BASE = "BROSLMV_DESARROLLO"
AQUI = os.path.dirname(os.path.abspath(__file__))
PLANTILLA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(AQUI, "..", "..", "instalador", "scripts", "CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py")
registro = []


def ruta_combinada(nombre):
    """Las plantillas de cobro y pago estan separadas en fabrica; las pruebas de la receta necesitan las dos mitades: se arma la version combinada en una carpeta temporal."""
    import tempfile
    d = os.path.join(tempfile.gettempdir(), "brosLMV_combinado")
    subprocess.run([sys.executable, os.path.join(AQUI, "generar.py"), "--combinado", d], check=True, capture_output=True)
    return os.path.join(d, nombre)


def sqlcmd(sql):
    r = subprocess.run(["sqlcmd", "-S", SERVIDOR, "-E", "-d", BASE, "-W", "-s", "\x1f", "-Q", "SET NOCOUNT ON; " + sql],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    lineas = [l for l in r.stdout.splitlines() if l.strip() != ""]
    if r.returncode != 0 or (lineas and lineas[0].startswith("Msg ")):
        raise Exception("SQL: " + " ".join(lineas[:3]))
    if len(lineas) < 2:
        return []
    cols = lineas[0].split("\x1f")
    filas = []
    for l in lineas[2:]:
        vals = l.split("\x1f")
        filas.append({c: (None if v == "NULL" else v) for c, v in zip(cols, vals)})
    return filas


class Erp:
    def OwnedBusinessEntityId(self): return 1        # como el real: en Python todo ctx.erp.X es una función
    _items = 0

    def NuevoDocumento(self, modulo, almacen, entidad):
        registro.append(("NuevoDocumento", modulo, almacen, entidad))
        return 999999

    def AgregarArticulo(self, doc, pid, cant, precio, costo, imp, desc, deliver):
        registro.append(("AgregarArticulo", pid, cant, precio, costo, imp, round(desc, 6), deliver))
        Erp._items += 1
        return 880000 + Erp._items

    def RecalcCompleto(self, doc): registro.append(("RecalcCompleto",))
    def AffectStockNEW(self, doc): registro.append(("AffectStockNEW",))
    def Save(self, doc): registro.append(("Save",))
    def UpdateDocumentPaidInfo(self, doc): registro.append(("UpdateDocumentPaidInfo",))
    def UpdateStatusDelivery(self, doc): registro.append(("UpdateStatusDelivery",))
    def RefreshGrid(self): pass
    def AbrirDocumento(self, doc, modulo): pass
    def LastError(self): return ""


class Ctx:
    erp = Erp()
    user_id = 1

    def query(self, sql, params=None): return sqlcmd(sql)

    def scalar(self, sql, params=None):
        f = sqlcmd(sql.replace("SELECT ", "SELECT TOP 1 ", 1) if False else sql)
        return list(f[0].values())[0] if f else None

    def execute(self, sql, params=None): registro.append(("execute", sql))
    def get_selected_ids(self): return []
    def msg(self, *a): pass


# La Total del documento ficticio 999999 no existe en la base: se simula para la agenda (Total = 1000)
_scalar_real = Ctx.scalar
def _scalar(self, sql, params=None):
    if "SELECT Total FROM docDocument WHERE DocumentID=999999" in sql:
        return "1000"
    if "StockAffectation" in sql:
        return _scalar_real(self, sql, params)
    return _scalar_real(self, sql, params)
Ctx.scalar = _scalar

modulo_falso = types.ModuleType("broslmv")
modulo_falso.ctx = Ctx()
sys.modules["broslmv"] = modulo_falso
sys.modules["__main__"].__dict__  # noqa

# La variante Windows Forms importa pythonnet/System: se sustituyen por maquetas (la prueba de la ventana real es a mano, dentro de Comercial)
from unittest.mock import MagicMock
class Falso(types.ModuleType):
    """Control de mentira: acepta cualquier atributo, llamada, comparación o +=, y sirve de módulo, para que la ventana arme sus pantallas."""
    def __init__(self, nombre="falso"): super().__init__(nombre)
    def __getattr__(self, n):
        if n.startswith("__"): raise AttributeError(n)
        return Falso(n)
    def __call__(self, *a, **k): return Falso("llamada")
    def __iadd__(self, o): return self
    def __gt__(self, o): return False
    def __lt__(self, o): return False
    def __ge__(self, o): return True
    def __le__(self, o): return True
    def __index__(self): return 0
    def __int__(self): return 0
    def __float__(self): return 0.0
    def __iter__(self): return iter(())
    def __len__(self): return 0
    def __getitem__(self, k): return Falso("item")
    def __setitem__(self, k, v): pass
    def __delitem__(self, k): pass
    def __bool__(self): return True
    # aritmética de mentira: una medida de un control (Height, Width, Right…) vale 0 para que los cálculos de posición corran
    def __add__(self, o): return 0
    def __radd__(self, o): return 0
    def __sub__(self, o): return 0
    def __rsub__(self, o): return 0
    def __mul__(self, o): return 0
    def __rmul__(self, o): return 0
    def __truediv__(self, o): return 0
    def __rtruediv__(self, o): return 0
    def __floordiv__(self, o): return 0
    def __rfloordiv__(self, o): return 0
    def __neg__(self): return 0
    def __str__(self): return ""
for _m in ("pythonnet", "clr", "System", "System.Threading", "System.Drawing", "System.Windows.Forms"):
    sys.modules.setdefault(_m, Falso())
