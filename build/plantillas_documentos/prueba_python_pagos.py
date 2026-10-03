#!/usr/bin/env python
# Prueba del NÚCLEO Python de las plantillas «Cobro a cliente / Pago a proveedor» SIN Comercial (véase mock_broslmv.py): lee el laboratorio de verdad
# y solo REGISTRA el SQL que se escribiría. Comprueba catálogos, la receta de siete tablas (campos y orden), el saldo nuevo, y los rechazos.
# Uso:  python build/plantillas_documentos/prueba_python_pagos.py [ruta_a_la_plantilla.py]     Devuelve 0 si pasó, 1 si falló.
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_broslmv as m
from mock_broslmv import registro, sqlcmd

PLANTILLA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(m.AQUI, "..", "..", "instalador", "scripts", "COBRO_PAGO_PYTHON_WEBVIEW2.py")
os.environ["BROSLMV_PAGO_TEST"] = '{"catalogo": true}'
os.environ["BROSLMV_PAGO_OUT"] = os.path.join(os.environ.get("TEMP", "."), "prueba_py_pago.txt")
codigo = open(PLANTILLA, encoding="utf-8").read()
espacio = {"__name__": "plantilla"}


def fallo(msg):
    print("  [ERROR] " + msg)
    sys.exit(1)


exec(compile(codigo, PLANTILLA, "exec"), espacio)
import json
cat = json.loads(espacio["result"])
if len(cat["cuentas"]) < 1 or len(cat["formas"]) < 3 or not cat["docsP"]:
    fallo("El catálogo viene incompleto: " + str({k: len(v) for k, v in cat.items()}))
print("  Catálogo: %d cuentas, %d formas de pago, %d documentos por cobrar y %d por pagar." % (len(cat["cuentas"]), len(cat["formas"]), len(cat["docsC"]), len(cat["docsP"])))

# Un documento por pagar con saldo, del laboratorio
doc = next(d for d in cat["docsP"] if d["saldo"] > 200)
cuenta = cat["cuentas"][0]["id"]
spec = {"tipo": "pago", "entidad": doc["ent"], "cuenta": cuenta, "forma": 3, "fecha": "2026-10-02", "referencia": "SPEI-PY", "aplicaciones": [{"doc": doc["id"], "monto": 100}]}
registro.clear()
resumen = espacio["aplicar"](spec)
sql = " ".join(r[1] for r in registro if r[0] == "execute")
nuevo = round(doc["saldo"] - 100, 2)
for pieza in ("sp_getapplock", "BrosCobroFolio_247_PAG", "INSERT INTO docFinancialOperation", "VALUES (247,2,32,", "INSERT INTO docDocumentPayment", "INSERT INTO docDocumentPaymentEspejo",
              "INSERT INTO docBankTransfer", "N'SPEI-PY'", "StatusPaidID=2", "TotalPaid=", "THROW 50002", "COMMIT TRAN"):
    if pieza not in sql:
        fallo("La receta de pago no contiene: " + pieza)
if ("Balance=" + ("%g" % nuevo)) not in sql.replace(" ", ""):
    fallo("El nuevo saldo debía ser %s." % nuevo)
if "Pago a proveedor registrado: 100.00" not in resumen or "PAG-" not in resumen:
    fallo("El resumen no trae el monto o el folio: " + resumen)
print("  Receta de pago a proveedor: operación 247, aplicación, espejo, transferencia con referencia, candado de folio, saldo nuevo %s y estatus parcial." % nuevo)

# Efectivo: sin transferencia bancaria
registro.clear()
espacio["aplicar"](dict(spec, forma=1))
if "docBankTransfer" in " ".join(r[1] for r in registro if r[0] == "execute"):
    fallo("El efectivo no debía dejar transferencia bancaria.")
print("  Efectivo: sin transferencia bancaria.")

# Cobro (cliente): otro módulo y prefijo
docC = next((d for d in cat["docsC"] if d["saldo"] > 1), None)
if docC:
    registro.clear()
    espacio["aplicar"]({"tipo": "cobro", "entidad": docC["ent"], "cuenta": cuenta, "forma": 3, "fecha": "2026-10-02", "aplicaciones": [{"doc": docC["id"], "monto": docC["saldo"]}]})
    sqlc = " ".join(r[1] for r in registro if r[0] == "execute")
    if "VALUES (248,1,31," not in sqlc or "BrosCobroFolio_248_COB" not in sqlc or "StatusPaidID=1" not in sqlc:
        fallo("El cobro debía ser módulo 248 / cliente / tipo 31 / COB y liquidar el documento.")
    print("  Cobro a cliente: operación 248 / COB y liquidación (estatus 1).")

# Rechazos con mensaje claro
casos = [(dict(spec, aplicaciones=[{"doc": doc["id"], "monto": doc["saldo"] + 5}]), "mayor que su saldo"),
         (dict(spec, entidad=1, aplicaciones=[{"doc": doc["id"], "monto": 1}]), "otro proveedor"),
         (dict(spec, cuenta=0), "cuenta"), (dict(spec, aplicaciones=[]), "al menos un documento"),
         (dict(spec, tipo="cobro", aplicaciones=[{"doc": doc["id"], "monto": 1}]), "")]
for malo, texto in casos:
    registro.clear()
    try:
        espacio["aplicar"](malo)
        fallo("Debía rechazarse: " + (texto or "documento de otro lado"))
    except Exception as ex:
        if texto not in str(ex):
            fallo("Mensaje inesperado (%s): %s" % (texto, ex))
    if any(r[0] == "execute" for r in registro):
        fallo("Un rechazo no debe escribir nada en la base.")
print("  Rechazos con mensaje claro (sobrepago, documento ajeno, sin cuenta, sin documentos, lado equivocado) y sin escribir.")

# Humo de la ventana
os.environ.pop("BROSLMV_PAGO_TEST", None)
if ("show_html_formulario" in codigo or "ventana_en_vivo" in codigo):
    os.environ["BROSLMV_PAGO_HTML"] = os.path.join(os.environ.get("TEMP", "."), "prueba_py_pago.html")
espacio2 = {"__name__": "plantilla"}
exec(compile(codigo, PLANTILLA, "exec"), espacio2)
if ("show_html_formulario" in codigo or "ventana_en_vivo" in codigo):
    pagina = open(os.environ["BROSLMV_PAGO_HTML"], encoding="utf-8").read()
    os.remove(os.environ["BROSLMV_PAGO_HTML"])
    if "__DATOS__" in pagina or "var DATOS={" not in pagina:
        fallo("La página HTML no recibió sus datos.")
    print("  Ventana HTML: la página se arma con sus datos (%d bytes)." % len(pagina))
else:
    print("  Ventana Windows Forms: el cuerpo se ejecuta completo con controles de mentira.")
sys.exit(0)
