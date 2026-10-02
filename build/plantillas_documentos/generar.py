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

def armar(cabecera, nucleo, ui, html, destino, helpers=None):
    helpers = helpers or ('helpers.cs.part' if nucleo.endswith('.cs.part') else 'helpers.py.part')
    t = leer(cabecera) + '\n' + leer(helpers) + '\n' + leer(nucleo) + '\n' + leer(ui)
    t = t.replace('__HTML_CS__', cs_verbatim(html)).replace('__HTML_PY__', py_raw(html))
    escribir(destino, t)

html_doc = leer('formulario.html.part')
print('Documentos:')
armar('cabecera_doc_cs_webview2.part', 'nucleo.cs.part', 'ui_webview2.cs.part', html_doc, 'CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_winforms.cs.part')):
    armar('cabecera_doc_cs_winforms.part', 'nucleo.cs.part', 'ui_winforms.cs.part', html_doc, 'CREAR_DOCUMENTO_CSHARP_WINFORMS.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_webview2.py.part')):
    armar('cabecera_doc_py_webview2.part', 'nucleo.py.part', 'ui_webview2.py.part', html_doc, 'CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py')
if os.path.exists(os.path.join(AQUI, 'ui_winforms.py.part')):
    armar('cabecera_doc_py_winforms.part', 'nucleo.py.part', 'ui_winforms.py.part', html_doc, 'CREAR_DOCUMENTO_PYTHON_WINFORMS.py')

print('Cobros y pagos:')
html_pago = leer('formulario_pagos.html.part')
armar('cabecera_pago_cs_webview2.part', 'nucleo_pagos.cs.part', 'ui_pagos_webview2.cs.part', html_pago, 'COBRO_PAGO_CSHARP_WEBVIEW2.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_pagos_winforms.cs.part')):
    armar('cabecera_pago_cs_winforms.part', 'nucleo_pagos.cs.part', 'ui_pagos_winforms.cs.part', html_pago, 'COBRO_PAGO_CSHARP_WINFORMS.ctx')
if os.path.exists(os.path.join(AQUI, 'ui_pagos_webview2.py.part')):
    armar('cabecera_pago_py_webview2.part', 'nucleo_pagos.py.part', 'ui_pagos_webview2.py.part', html_pago, 'COBRO_PAGO_PYTHON_WEBVIEW2.py')
if os.path.exists(os.path.join(AQUI, 'ui_pagos_winforms.py.part')):
    armar('cabecera_pago_py_winforms.part', 'nucleo_pagos.py.part', 'ui_pagos_winforms.py.part', html_pago, 'COBRO_PAGO_PYTHON_WINFORMS.py')
