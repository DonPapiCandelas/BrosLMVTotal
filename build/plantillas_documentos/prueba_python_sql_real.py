#!/usr/bin/env python
# Ejecuta DE VERDAD, dentro de una transacción que SIEMPRE se deshace (ROLLBACK), el T-SQL que arma el núcleo Python de cobros/pagos con moneda y parcialidades,
# para comprobar que es válido y que deja lo mismo que C#: cuenta en pesos, cuenta en dólares con documento en pesos y pago a proveedor.
# Uso:  python build/plantillas_documentos/prueba_python_sql_real.py      Devuelve 0 si pasó, 1 si falló. No deja nada en la base.
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_broslmv as m
from mock_broslmv import registro, sqlcmd

PLANTILLA = m.ruta_combinada("COBRO_PAGO_PYTHON_WEBVIEW2.py")
os.environ["BROSLMV_PAGO_TEST"] = '{"catalogo": true}'
os.environ["BROSLMV_PAGO_OUT"] = os.path.join(os.environ.get("TEMP", "."), "prueba_py_sqlreal.txt")
codigo = open(PLANTILLA, encoding="utf-8").read()
espacio = {"__name__": "plantilla"}


def fallo(msg):
    print("  [ERROR] " + msg)
    sys.exit(1)


exec(compile(codigo, PLANTILLA, "exec"), espacio)
import json
cat = json.loads(espacio["result"])


def correr(spec, comprobacion):
    """Arma el SQL con aplicar(), lo ejecuta con ROLLBACK y corre la comprobación DENTRO de la misma transacción."""
    registro.clear()
    resumen = espacio["aplicar"](spec)
    sql = " ".join(r[1] for r in registro if r[0] == "execute")
    sql = sql.replace("COMMIT TRAN;", comprobacion + "\nROLLBACK TRAN;")
    salida = sqlcmd(sql)
    return resumen, salida


cta_pesos = next(c for c in cat["cuentas"] if c["moneda"] == 3)
cta_usd = next(c for c in cat["cuentas"] if c["moneda"] == 2)
doc_c = next(d for d in cat["docsC"] if d["moneda"] == 3 and d["saldo"] > 400)
chequeo = "SELECT CONCAT(DebitCreditCoef,'|',Amount,'|',CurrencyID,'|',Rate,'|',AmountRate) AS x FROM docFinancialOperation WHERE FinancialOperationID = @opId;"

# 1) Cuenta en dólares cobrando una factura en pesos: 10 USD a 18 = 180 MXN
res, sal = correr({"tipo": "cobro", "entidad": doc_c["ent"], "cuenta": cta_usd["id"], "forma": 3, "fecha": "2026-10-02", "tc": 18, "aplicaciones": [{"doc": doc_c["id"], "monto": 10}]}, chequeo)
print("  Cobro USD a factura en pesos:", res.replace("\n", " | "))
if "queda" not in res or "1|10|2|18|180" not in str(sal):
    fallo("Resultado inesperado en la operación: " + str(sal))

# 2) Cuenta en pesos cobrando el mismo documento
res, sal = correr({"tipo": "cobro", "entidad": doc_c["ent"], "cuenta": cta_pesos["id"], "forma": 3, "fecha": "2026-10-02", "aplicaciones": [{"doc": doc_c["id"], "monto": 100}]}, chequeo)
print("  Cobro MXN a factura en pesos:", res.replace("\n", " | "))
if "1|100|" not in str(sal):
    fallo("Resultado inesperado: " + str(sal))

# 3) Pago a proveedor
doc_p = next(d for d in cat["docsP"] if d["moneda"] == 3 and d["saldo"] > 200)
res, sal = correr({"tipo": "pago", "entidad": doc_p["ent"], "cuenta": cta_pesos["id"], "forma": 3, "fecha": "2026-10-02", "aplicaciones": [{"doc": doc_p["id"], "monto": 50}]}, chequeo)
print("  Pago MXN a factura de compra en pesos:", res.replace("\n", " | "))
if "-1|50|" not in str(sal):
    fallo("El pago debía guardar coeficiente -1: " + str(sal))

# 4) El detalle trae parcialidades y aplicaciones
os.environ["BROSLMV_PAGO_TEST"] = json.dumps({"detalle": True, "doc": doc_c["id"]})
espacio2 = {"__name__": "plantilla"}
exec(compile(codigo, PLANTILLA, "exec"), espacio2)
det = json.loads(espacio2["result"])
if not det.get("parc") or "aplic" not in det:
    fallo("El detalle debía traer parcialidades y aplicaciones: " + str(det))
print("  Detalle de un documento: %d parcialidad(es), %d aplicación(es)." % (len(det["parc"]), len(det["aplic"])))
print("  El SQL de la plantilla Python es válido y se deshizo (ROLLBACK): la base quedó igual.")
