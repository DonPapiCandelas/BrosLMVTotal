#!/usr/bin/env python
# Ensambla las plantillas «Crear documento» (C#/Python × WebView2/WinForms) y «Cobro/Pago» a partir de sus piezas.
# Las piezas comunes (núcleo, formulario HTML) viven una sola vez en esta carpeta; los .ctx / .py de instalador/scripts son el resultado y SE COMITEAN.
# Uso:  python build/plantillas_documentos/generar.py      (desde la raíz del repositorio)
import os, sys
AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.abspath(os.path.join(AQUI, '..', '..', 'instalador', 'scripts'))
def leer(n): return open(os.path.join(AQUI, n), encoding='utf-8').read().replace('\r\n', '\n')
def escribir(n, t):
    open(os.path.join(SALIDA, n), 'w', encoding='utf-8', newline='').write(t)
    print('  ' + n)
def cs_verbatim(h): return h.replace('"', '""')
def py_raw(h):
    assert "'''" not in h and not h.rstrip().endswith(chr(92))
    return h

def armar(cabecera, nucleo, ui, html, destino, helpers=None, intermedio=None, lado=None):
    helpers = helpers or ('helpers.cs.part' if nucleo.endswith('.cs.part') else 'helpers.py.part')
    t = leer(cabecera) + '\n' + leer(helpers) + '\n' + leer(nucleo) + '\n' + (leer(intermedio) + '\n' if intermedio else '') + leer(ui)
    t = t.replace('__HTML_CS__', cs_verbatim(html)).replace('__HTML_PY__', py_raw(html))
    if lado:
        t = solo_lado(t, lado)
    escribir(destino, t)

SALTO = chr(10)

def solo_lado(t, lado):
    """Plantilla de un solo lado: la persona que lleva cuentas por cobrar no debe ver las cuentas por pagar (y al revés). Quita la fila del otro tipo de la tabla TIPOS
    (el catálogo ya solo carga personas y documentos de los lados que hay en TIPOS) y ajusta el encabezado."""
    cobro = lado == 'C'
    otro = 'pago' if cobro else 'cobro'
    t = SALTO.join(l for l in t.split(SALTO) if not l.lstrip().startswith('TP("' + otro + '"'))
    t = t.replace('COBRO_PAGO_', 'COBRO_CLIENTE_' if cobro else 'PAGO_PROVEEDOR_')
    t = t.replace('Plantilla: Cobro a cliente / Pago a proveedor (', 'Plantilla: ' + ('Cobro a cliente' if cobro else 'Pago a proveedor') + ' (')
    t = t.replace('Registra un cobro a cliente o un pago a proveedor y lo aplica', ('Registra un cobro a cliente y lo aplica' if cobro else 'Registra un pago a proveedor y lo aplica'))
    marca = 'Léela completa antes de usarla'
    aviso = ('Plantilla separada a propósito: ' + ('solo CUENTAS POR COBRAR (clientes)' if cobro else 'solo CUENTAS POR PAGAR (proveedores)') + ' para que quien la use no vea el otro lado. ')
    return t.replace(marca, aviso + marca, 1)

def extras(nombre):
    partes, actual = {}, None
    for linea in leer(nombre).split(SALTO):
        if linea.startswith('@@'):
            actual = linea[2:].strip(); partes[actual] = []; continue
        if actual and actual != 'END': partes[actual].append(linea)
    return {k: SALTO.join(v) for k, v in partes.items()}
def inyectar(html, ex):
    for marca, clave in (('/*__EXTRAS_CSS__*/', 'CSS'), ('<!--__EXTRAS_BTN__-->', 'BTN'), ('<!--__EXTRAS_DER__-->', 'DER'), ('<!--__EXTRAS_OVERLAYS__-->', 'OVERLAYS'), ('/*__EXTRAS_JS__*/', 'JS')):
        assert marca in html, marca
        html = html.replace(marca, ex[clave])
    return html
html_doc = inyectar(leer('formulario.html.part'), extras('extras_documento.html.part'))
print('Documentos:')
armar('cabecera_doc_cs_webview2.part', 'nucleo.cs.part', 'ui_webview2.cs.part', html_doc, 'CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_winforms.cs.part')):
    armar('cabecera_doc_cs_winforms.part', 'nucleo.cs.part', 'ui_winforms.cs.part', html_doc, 'CREAR_DOCUMENTO_CSHARP_WINFORMS.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_webview2.py.part')):
    armar('cabecera_doc_py_webview2.part', 'nucleo.py.part', 'ui_webview2.py.part', html_doc, 'CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py', intermedio='servidor_local.py.part')
if os.path.exists(os.path.join(AQUI, 'ui_winforms.py.part')):
    armar('cabecera_doc_py_winforms.part', 'nucleo.py.part', 'ui_winforms.py.part', html_doc, 'CREAR_DOCUMENTO_PYTHON_WINFORMS.py')

print('Cobros y pagos:')
ex_doc, ex_pago = extras('extras_documento.html.part'), extras('extras_pago.html.part')
ex_pago['CSS'] = ex_doc['CSS'] + SALTO + ex_pago['CSS']          # el CSS común (paleta, hoja imprimible, tema oscuro) se comparte
def inyectar_pago(html, ex):
    for marca, clave in (('/*__EXTRAS_CSS__*/', 'CSS'), ('<!--__EXTRAS_BTN__-->', 'BTN'), ('<!--__EXTRAS_HERR__-->', 'HERR'), ('<!--__EXTRAS_DER__-->', 'DER'), ('<!--__EXTRAS_OVERLAYS__-->', 'OVERLAYS'), ('/*__EXTRAS_JS__*/', 'JS')):
        assert marca in html, marca
        html = html.replace(marca, ex[clave])
    return html
html_pago = inyectar_pago(leer('formulario_pagos.html.part'), ex_pago)
FAMILIAS = [('cabecera_pago_cs_webview2.part', 'nucleo_pagos.cs.part', 'ui_pagos_webview2.cs.part', 'COBRO_PAGO_CSHARP_WEBVIEW2.ctx', None),
            ('cabecera_pago_cs_winforms.part', 'nucleo_pagos.cs.part', 'ui_pagos_winforms.cs.part', 'COBRO_PAGO_CSHARP_WINFORMS.ctx', None),
            ('cabecera_pago_py_webview2.part', 'nucleo_pagos.py.part', 'ui_pagos_webview2.py.part', 'COBRO_PAGO_PYTHON_WEBVIEW2.py', 'servidor_local.py.part'),
            ('cabecera_pago_py_winforms.part', 'nucleo_pagos.py.part', 'ui_pagos_winforms.py.part', 'COBRO_PAGO_PYTHON_WINFORMS.py', None)]
if '--combinado' in sys.argv:           # solo para pruebas: las dos mitades juntas, en otra carpeta (nunca en instalador/scripts)
    SALIDA = os.path.abspath(sys.argv[sys.argv.index('--combinado') + 1])
    os.makedirs(SALIDA, exist_ok=True)
    for cab, nuc, ui, destino, inter in FAMILIAS:
        armar(cab, nuc, ui, html_pago, destino, intermedio=inter)
    sys.exit(0)
for cab, nuc, ui, destino, inter in FAMILIAS:
    if not os.path.exists(os.path.join(AQUI, ui)):
        continue
    for lado, prefijo in (('C', 'COBRO_CLIENTE_'), ('P', 'PAGO_PROVEEDOR_')):
        armar(cab, nuc, ui, html_pago, destino.replace('COBRO_PAGO_', prefijo), intermedio=inter, lado=lado)
