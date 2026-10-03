#!/usr/bin/env python
# Prueba DE EXTREMO A EXTREMO del servidor «en vivo» de las plantillas Python con WebView2 (Crear documento y Cobro/Pago) SIN Comercial ni ventana:
# se simula ctx.show_html y, en su lugar, un hilo hace lo mismo que haría la página (fetch con el token) contra el servidor HTTP que levanta el script.
# Comprueba: que rechaza peticiones sin token, que contesta consultas en vivo con JavaScript («respuesta(id, …)»), que guarda y borra el borrador y las preferencias,
# que crea un documento / aplica un pago con la secuencia correcta, que cierra la ventana (window.close()) y que el script termina cuando la ventana lo pide.
# Los datos salen del laboratorio (BROSLMV_DESARROLLO, solo lectura); lo que escribiría se registra, no se ejecuta. Devuelve 0 si pasó, 1 si falló.
import json
import os
import re
import sys
import tempfile
import threading
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mock_broslmv as m
from mock_broslmv import registro, sqlcmd

os.environ["LOCALAPPDATA"] = tempfile.mkdtemp(prefix="brosLMV_prueba_")      # los borradores de la prueba no tocan los de verdad
RAIZ = os.path.join(m.AQUI, "..", "..", "instalador", "scripts")
errores = []


def fallo(txt):
    print("  [ERROR] " + txt)
    sys.exit(1)


def post(url, token, obj, token_malo=False):
    req = urllib.request.Request(url + "?t=" + ("mal" if token_malo else token), data=json.dumps(obj).encode("utf-8"), method="POST")
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, r.read().decode("utf-8"), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, "", dict(e.headers)


def correr(plantilla, escenario):
    os.environ.pop("BROSLMV_DOC_TEST", None)
    os.environ.pop("BROSLMV_PAGO_TEST", None)
    os.environ.pop("BROSLMV_DOC_HTML", None)
    os.environ.pop("BROSLMV_PAGO_HTML", None)
    resultado = {}

    def show_html(self, html, titulo="", ancho=0, alto=0, modal=True):
        mm = re.search(r'"http": \{"url": "([^"]+)", "token": "([a-f0-9]+)"\}', html)
        if not mm:
            fallo("La página no recibió DATOS.http (url y token).")
        if modal:
            fallo("La ventana en vivo debe abrirse con modal=False (no debe bloquear Comercial).")
        if "window.chrome.webview.postMessage" not in html or "DATOS.http" not in html:
            fallo("La página debía traer los dos transportes (WebView2 y HTTP).")

        def hilo():
            try:
                escenario(mm.group(1), mm.group(2), resultado)
            except BaseException as ex:                 # cualquier fallo del escenario se reporta y se cierra el servidor
                resultado["error"] = repr(ex)
                post(mm.group(1), mm.group(2), {"accion": "cancelar"})
        threading.Thread(target=hilo, daemon=True).start()

    m.Ctx.show_html = show_html
    codigo = open(plantilla if os.path.isabs(plantilla) else os.path.join(RAIZ, plantilla), encoding="utf-8").read()
    registro.clear()
    exec(compile(codigo, plantilla, "exec"), {"__name__": "plantilla"})
    if "error" in resultado:
        fallo(plantilla + ": " + resultado["error"])
    return resultado


# ---------------------------------------------------------------------------------------------------------------------------------------------------
def escenario_documento(url, token, res):
    st, _, _ = post(url, token, {"accion": "latido"}, token_malo=True)
    assert st == 403, "Sin el token correcto debía rechazar (403) y dio %s" % st
    st, cuerpo, cab = post(url, token, {"accion": "latido"})
    assert st == 200 and cuerpo == "" and cab.get("Access-Control-Allow-Origin") == "*", "El latido debía contestar vacío y con CORS"
    cli = sqlcmd("SELECT TOP 1 BusinessEntityID FROM docDocument WHERE ModuleID=152 AND DeletedOn IS NULL AND CancelledOn IS NULL GROUP BY BusinessEntityID ORDER BY COUNT(*) DESC")[0]["BusinessEntityID"]
    _, c, _ = post(url, token, {"accion": "entidad", "req": 7, "id": int(cli)})
    assert c.startswith("respuesta(7,[") , "entidad: " + c[:80]
    _, c, _ = post(url, token, {"accion": "pendientes", "req": 8, "entidad": int(cli), "tipo": "recepcion"})
    assert c.startswith("respuesta(8,"), "pendientes: " + c[:80]
    _, c, _ = post(url, token, {"accion": "inteligencia", "req": 9, "id": int(cli), "tipo": "factura_compra"})
    assert c.startswith("respuesta(9,{") and '"meses"' in c and '"top"' in c and '"ultimo"' in c, "inteligencia: " + c[:120]
    d = json.loads(c[len("respuesta(9,"):-1])
    assert len(d["meses"]) == 12 and len(d["top"]) >= 1, "La inteligencia debía traer 12 meses y productos habituales."
    res["inteligencia"] = "%d productos, %d doc. en 12 meses" % (len(d["top"]), d["docs12"])
    post(url, token, {"accion": "borrador", "spec": json.dumps({"tipo": "factura_compra", "entidad": int(cli), "partidas": []})})
    arch = [f for f in os.listdir(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores")) if f.startswith("documento_")]
    assert arch, "El borrador debía quedar en disco."
    post(url, token, {"accion": "pref", "tema": "oscuro"})
    assert os.path.exists(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores", [f for f in os.listdir(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores")) if f.startswith("pref_ui_")][0]))
    # crear un documento: «Guardar y nuevo» (la ventana sigue) y después «Guardar y abrir» (la ventana se cierra)
    prod = sqlcmd("SELECT TOP 1 ProductID FROM orgProduct WHERE DeletedOn IS NULL AND TaxTypeID IS NOT NULL")[0]["ProductID"]
    alm = sqlcmd("SELECT TOP 1 DepotID FROM orgDepot WHERE DeletedOn IS NULL")[0]["DepotID"]
    spec = {"tipo": "orden_compra", "almacen": int(alm), "entidad": int(cli), "condicion": 1, "fecha": "2026-10-03", "entrega": "2026-10-10", "moneda": 3, "tc": 1,
            "partidas": [{"id": int(prod), "nombre": "x", "cant": 2, "precio": 10, "desc": 0, "imp": 5, "origenItem": 0}]}
    _, c, _ = post(url, token, {"accion": "crear", "nuevo": True, "spec": json.dumps(spec)})
    assert c.startswith("creado(") and '"nuevo": true' in c, "crear (nuevo): " + c[:120]
    assert not [f for f in os.listdir(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores")) if f.startswith("documento_")], "Al crear debía borrarse el borrador."
    mal = dict(spec, partidas=[])
    _, c, _ = post(url, token, {"accion": "crear", "spec": json.dumps(mal)})
    assert c.startswith("falloCrear(") and "al menos una partida" in c, "crear sin partidas: " + c[:120]
    _, c, _ = post(url, token, {"accion": "crear", "nuevo": False, "spec": json.dumps(spec)})
    assert c == "window.close()", "Guardar y abrir debía cerrar la ventana: " + c[:120]
    res["fin"] = True


def escenario_pago(url, token, res):
    _, c, _ = post(url, token, {"accion": "latido"})
    assert c == "", "latido"
    prov = sqlcmd("SELECT TOP 1 BusinessEntityID FROM docDocument WHERE ModuleID=152 AND DeletedOn IS NULL AND CancelledOn IS NULL AND ISNULL(Balance,0) > 1 GROUP BY BusinessEntityID ORDER BY SUM(Balance) DESC")[0]["BusinessEntityID"]
    _, c, _ = post(url, token, {"accion": "movimientos", "req": 3, "entidad": int(prov), "tipo": "pago"})
    assert c.startswith("respuesta(3,["), "movimientos: " + c[:80]
    _, c, _ = post(url, token, {"accion": "docs", "req": 4, "lado": "P"})
    docs = json.loads(c[len("respuesta(4,"):-1])
    assert docs and "metodo" in docs[0], "docs: debían traer el método de pago (PUE/PPD)"
    _, c, _ = post(url, token, {"accion": "inteligencia", "req": 5, "entidad": int(prov), "tipo": "pago"})
    d = json.loads(c[len("respuesta(5,"):-1])
    assert len(d["meses"]) == 12 and "puntual" in d, "inteligencia de pago: " + c[:120]
    post(url, token, {"accion": "borrador", "spec": json.dumps({"tipo": "pago", "entidad": int(prov), "sel": {}})})
    assert [f for f in os.listdir(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores")) if f.startswith("cobropago_")], "El borrador debía quedar en disco."
    doc = next(x for x in docs if x["ent"] == int(prov) and x["saldo"] > 200 and x["moneda"] == 3)
    cta = sqlcmd("SELECT TOP 1 FinancialEntityID FROM orgFinancialEntity WHERE DeletedOn IS NULL AND ISNULL(CurrencyID,0) IN (0,3)")[0]["FinancialEntityID"]
    _, c, _ = post(url, token, {"accion": "detalle", "req": 6, "doc": doc["id"]})
    dd = json.loads(c[len("respuesta(6,"):-1])
    assert dd.get("parc") and "aplic" in dd, "detalle: " + c[:120]
    spec = {"tipo": "pago", "entidad": doc["ent"], "cuenta": int(cta), "forma": 3, "fecha": "2026-10-03", "referencia": "SPEI-SERV", "aplicaciones": [{"doc": doc["id"], "monto": 100}]}
    _, c, _ = post(url, token, {"accion": "aplicar", "nuevo": True, "spec": json.dumps(spec)})
    assert c.startswith("aplicado(") and "registrado: 100.00 MXN" in c, "aplicar: " + c[:160]
    assert not [f for f in os.listdir(os.path.join(os.environ["LOCALAPPDATA"], "BrosLMV", "borradores")) if f.startswith("cobropago_")], "Al aplicar debía borrarse el borrador."
    _, c, _ = post(url, token, {"accion": "aplicar", "spec": json.dumps(dict(spec, aplicaciones=[{"doc": doc["id"], "monto": doc["saldo"] + 5}]))})
    assert c.startswith("falloAplicar(") and "mayor que su saldo" in c, "sobrepago: " + c[:160]
    _, c, _ = post(url, token, {"accion": "cancelar"})
    assert c == "window.close()", "cancelar"
    res["fin"] = True


r1 = correr("CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx" if False else "CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py", escenario_documento)
print("  Crear documento (Python en vivo): token, latido, entidad, pendientes, inteligencia (%s), borrador, tema, crear y nuevo, validación y cierre." % r1["inteligencia"])
r2 = correr(m.ruta_combinada("COBRO_PAGO_PYTHON_WEBVIEW2.py"), escenario_pago)
print("  Cobro/Pago (Python en vivo): movimientos, documentos con método de pago, comportamiento de pago, borrador, aplicar, rechazo de sobrepago y cierre.")
sys.exit(0)
