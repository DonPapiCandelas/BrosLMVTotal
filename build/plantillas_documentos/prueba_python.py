#!/usr/bin/env python
# Prueba del NÚCLEO Python de las plantillas «Crear documento» SIN Comercial: no existe un Runner de Python, así que se simula el módulo `broslmv`:
#   · ctx.query / ctx.scalar leen de verdad el laboratorio (BROSLMV_DESARROLLO) con sqlcmd;
#   · ctx.execute y ctx.erp.* solo REGISTRAN lo que se les pidió (no escriben nada).
# Comprueba: que el archivo compila y corre, que los catálogos y los pendientes por tipo salen igual que en C#, y la secuencia exacta de llamadas
# al crear un documento (perfil del módulo, partidas con su vínculo, agenda de pago de una condición 50%-50%).
# Uso:  python build/plantillas_documentos/prueba_python.py [ruta_a_la_plantilla.py]     Devuelve 0 si pasó, 1 si falló.
import os
import subprocess
import sys
import types

SERVIDOR = r"localhost\compac"
BASE = "BROSLMV_DESARROLLO"
AQUI = os.path.dirname(os.path.abspath(__file__))
PLANTILLA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(AQUI, "..", "..", "instalador", "scripts", "CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py")
registro = []


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
    OwnedBusinessEntityId = 1
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
    def __str__(self): return ""
for _m in ("pythonnet", "clr", "System", "System.Threading", "System.Drawing", "System.Windows.Forms"):
    sys.modules.setdefault(_m, Falso())
espacio = {"__name__": "plantilla"}
os.environ.pop("BROSLMV_DOC_TEST", None)
os.environ["BROSLMV_DOC_TEST"] = '{"catalogo": true}'
os.environ["BROSLMV_DOC_OUT"] = os.path.join(os.environ.get("TEMP", "."), "prueba_py_doc.txt")
codigo = open(PLANTILLA, encoding="utf-8").read()


def fallo(m):
    print("  [ERROR] " + m)
    sys.exit(1)


exec(compile(codigo, PLANTILLA, "exec"), espacio)         # en modo de prueba: solo define y devuelve el catálogo
import json
cat = json.loads(espacio["result"])
if len(cat["almacenes"]) < 1 or len(cat["proveedores"]) < 1 or len(cat["productos"]) < 3 or len(cat["impuestos"]) < 1:
    fallo("El catálogo viene incompleto: " + str({k: len(v) for k, v in cat.items()}))
print("  Catálogo: %d almacenes, %d proveedores, %d productos, %d impuestos." % (len(cat["almacenes"]), len(cat["proveedores"]), len(cat["productos"]), len(cat["impuestos"])))

# Pendientes de una OC real del laboratorio (la última orden de compra con partidas pendientes de recibir)
oc = sqlcmd("SELECT TOP 1 DocumentID FROM docDocument WHERE ModuleID=183 AND DeletedOn IS NULL AND CancelledOn IS NULL AND Title LIKE 'DEMO CREAR DOC%' ORDER BY DocumentID DESC")
if oc:
    pend = espacio["origenes_de"]([int(oc[0]["DocumentID"])])
    if len(pend) != 1 or set(pend[0]["partidasPor"].keys()) != {"recepcion", "factura_compra"}:
        fallo("Los pendientes de la OC no salieron con los dos tipos derivados: " + str(pend))
    print("  Pendientes OC %s: recepción %d partida(s), factura %d partida(s)." % (oc[0]["DocumentID"], len(pend[0]["partidasPor"]["recepcion"]), len(pend[0]["partidasPor"]["factura_compra"])))

# Secuencia de creación: factura de compra desde una OC, condición 4 (50%-50%)
spec = {"tipo": "factura_compra", "almacen": 1, "entidad": 10020, "condicion": 4, "fecha": "2026-10-02", "titulo": "prueba", "origenes": [111],
        "partidas": [{"id": 2, "nombre": "x", "cant": 4, "precio": 120, "desc": 10, "imp": 5, "origenItem": 222}]}
registro.clear()
doc = espacio["crear_documento"](spec)
nombres = [r[0] for r in registro]
esperado = ["NuevoDocumento", "execute", "AgregarArticulo", "execute", "RecalcCompleto", "Save", "execute", "execute", "execute", "UpdateDocumentPaidInfo", "UpdateStatusDelivery"]
if doc != 999999 or nombres != esperado:
    fallo("Secuencia inesperada:\n    " + "\n    ".join(map(str, registro)))
upd = registro[1][1]
if "DepotIDFrom=0, StatusPaidID=3" not in upd or "PaymentTermID=4" not in upd or "SourceDocumentID=111" not in upd or "DateDocument='20261002'" not in upd:
    fallo("El UPDATE del encabezado no trae el perfil esperado: " + upd)
if registro[2][1:] != (2, 4.0, 120.0, 120.0, 5, 0.1, 0):
    fallo("AgregarArticulo recibió argumentos inesperados: " + str(registro[2]))
if "SourceDocumentItemID=222" not in registro[3][1]:
    fallo("La partida debía ligarse con SourceDocumentItemID=222.")
agenda = [r[1] for r in registro if r[0] == "execute" and "INSERT INTO docDocumentPaymentAgenda" in r[1]]
if len(agenda) != 2 or ", 50, 500, 1," not in agenda[0] or ", 50, 500, 2," not in agenda[1]:
    fallo("La agenda 50%-50% de 1000 debía ser dos parcialidades de 500: " + str(agenda))
print("  Secuencia de creación correcta (perfil, partida ligada, agenda 50%-50% de 2 x 500).")

# Validaciones con mensaje claro
for malo, texto in [({"tipo": "orden_compra", "almacen": 1, "entidad": 10020, "partidas": []}, "al menos una partida"),
                    ({"tipo": "orden_compra", "almacen": 0, "entidad": 10020, "partidas": [{"id": 2, "cant": 1, "precio": 1, "desc": 0}]}, "almacén"),
                    ({"tipo": "orden_compra", "almacen": 1, "entidad": 10020, "partidas": [{"id": 2, "cant": 0, "precio": 1, "desc": 0}]}, "cantidad mayor a cero")]:
    try:
        espacio["crear_documento"](malo)
        fallo("Debía rechazarse: " + texto)
    except Exception as ex:
        if texto not in str(ex):
            fallo("Mensaje inesperado (%s): %s" % (texto, ex))
print("  Validaciones rechazadas con mensaje claro.")

# Humo de la ventana: se ejecuta el cuerpo de la ventana (construcción de controles, catálogos, primer pintado) con controles de mentira.
# Atrapa nombres mal escritos y errores de lógica al armar la pantalla; NO prueba los clics.
if "Form()" in codigo or "show_html_formulario" in codigo:
    os.environ.pop("BROSLMV_DOC_TEST", None)
    os.environ.pop("BROSLMV_DOC_HTML", None)
    if "show_html_formulario" in codigo:
        os.environ["BROSLMV_DOC_HTML"] = os.path.join(os.environ.get("TEMP", "."), "prueba_py_pagina.html")
    espacio2 = {"__name__": "plantilla"}
    exec(compile(codigo, PLANTILLA, "exec"), espacio2)
    if "show_html_formulario" in codigo:
        pagina = open(os.environ["BROSLMV_DOC_HTML"], encoding="utf-8").read()
        if "__DATOS__" in pagina or "var DATOS={" not in pagina:
            fallo("La página HTML no recibió sus datos.")
        os.remove(os.environ["BROSLMV_DOC_HTML"])
        print("  Ventana HTML: la página se arma con sus datos (%d bytes)." % len(pagina))
    else:
        print("  Ventana Windows Forms: el cuerpo se ejecuta completo con controles de mentira.")
sys.exit(0)
