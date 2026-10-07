#!/usr/bin/env python
# Ensambla las plantillas «Crear documento» (C#/Python × WebView2/WinForms) y «Cobro/Pago» a partir de sus piezas.
# Las piezas comunes (núcleo, formulario HTML) viven una sola vez en esta carpeta; los .ctx / .py de instalador/scripts son el resultado y SE COMITEAN.
# Uso:  python build/plantillas_documentos/generar.py      (desde la raíz del repositorio)
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sin_comentarios
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
    if lado and destino.startswith('CREAR_DOCUMENTO_'):
        t = solo_lado_doc(t, lado)
        destino = destino.replace('CREAR_DOCUMENTO_', 'CREAR_VENTA_' if lado == 'C' else 'CREAR_COMPRA_')
    elif lado:
        t = solo_lado(t, lado)
    # La documentación de una plantilla va en su guía, no en el código: se entrega sin comentarios (las piezas .part sí los conservan para quien programa)
    t = sin_comentarios.sin_comentarios_py(t) if destino.endswith('.py') else sin_comentarios.sin_comentarios_cs(t)
    escribir(destino, t)

SALTO = chr(10)

def solo_lado(t, lado):
    """Plantilla de un solo lado: la persona que lleva cuentas por cobrar no debe ver las cuentas por pagar (y al revés). Quita la fila del otro tipo de la tabla TIPOS
    (el catálogo ya solo carga personas y documentos de los lados que hay en TIPOS) y ajusta el encabezado."""
    cobro = lado == 'C'
    otro = 'pago' if cobro else 'cobro'
    t = SALTO.join(l for l in t.split(SALTO) if not l.lstrip().startswith('TP("' + otro + '"'))
    t = t.replace('COBRO_PAGO_', 'COBRO_CLIENTE_' if cobro else 'PAGO_PROVEEDOR_')
    t = t.replace('Categoria: Tesorería', 'Categoria: ' + ('Cuentas por cobrar' if cobro else 'Cuentas por pagar')).replace('Documentacion: COBRO_PAGO.html', 'Documentacion: ' + ('COBRO_CLIENTE.html' if cobro else 'PAGO_PROVEEDOR.html'))
    t = t.replace('Plantilla: Cobro a cliente / Pago a proveedor (', 'Plantilla: ' + ('Cobro a cliente' if cobro else 'Pago a proveedor') + ' (')
    t = t.replace('Registra un cobro a cliente o un pago a proveedor y lo aplica', ('Registra un cobro a cliente y lo aplica' if cobro else 'Registra un pago a proveedor y lo aplica'))
    marca = 'Léela completa antes de usarla'
    aviso = ('Plantilla separada a propósito: ' + ('solo CUENTAS POR COBRAR (clientes)' if cobro else 'solo CUENTAS POR PAGAR (proveedores)') + ' para que quien la use no vea el otro lado. ')
    return t.replace(marca, aviso + marca, 1)

def solo_lado_doc(t, lado):
    """«Crear documento» de un solo lado: ventas (cliente) o compras (proveedor). Quien captura ventas no debe ver compras, ni al revés: se quitan las filas del otro lado de TIPOS
    (el catálogo solo carga las personas de los lados que hay en TIPOS), y se renombran el AppKey y la plantilla."""
    venta = lado == 'C'
    otro = '"P"' if venta else '"C"'
    t = SALTO.join(l for l in t.split(SALTO) if not (l.lstrip().startswith('T("') and l.split(',')[3].strip() == otro))
    t = t.replace('CREAR_DOCUMENTO_', 'CREAR_VENTA_' if venta else 'CREAR_COMPRA_')
    t = t.replace('Categoria: Documentos', 'Categoria: ' + ('Ventas' if venta else 'Compras')).replace('Documentacion: CREAR_DOCUMENTO.html', 'Documentacion: ' + ('CREAR_VENTA.html' if venta else 'CREAR_COMPRA.html'))
    t = t.replace('Plantilla: Crear documento (', 'Plantilla: ' + ('Crear documento de venta' if venta else 'Crear documento de compra') + ' (')
    t = t.replace('factura de cliente, pedido, remisión, factura de compra, orden de compra o recepción.',
                  ('factura de cliente, pedido o remisión (ventas).' if venta else 'factura de compra, orden de compra o recepción (compras).') +
                  ' Plantilla separada a propósito: ' + ('solo VENTAS (clientes)' if venta else 'solo COMPRAS (proveedores)') + ', para que quien la use no vea el otro lado.')
    return t

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
html_doc = sin_comentarios.sin_comentarios_html(inyectar(leer('formulario.html.part'), extras('extras_documento.html.part')))
print('Documentos:')
DOCS = [('cabecera_doc_cs_webview2.part', 'nucleo.cs.part', 'ui_webview2.cs.part', 'CREAR_DOCUMENTO_CSHARP_WEBVIEW2.ctx', None),
        ('cabecera_doc_cs_winforms.part', 'nucleo.cs.part', 'ui_winforms.cs.part', 'CREAR_DOCUMENTO_CSHARP_WINFORMS.ctx', None),
        ('cabecera_doc_py_webview2.part', 'nucleo.py.part', 'ui_webview2.py.part', 'CREAR_DOCUMENTO_PYTHON_WEBVIEW2.py', 'servidor_local.py.part'),
        ('cabecera_doc_py_winforms.part', 'nucleo.py.part', 'ui_winforms.py.part', 'CREAR_DOCUMENTO_PYTHON_WINFORMS.py', None)]
if '--combinado' in sys.argv:           # solo para pruebas: ventas y compras juntas, en otra carpeta (nunca en instalador/scripts)
    SALIDA = os.path.abspath(sys.argv[sys.argv.index('--combinado') + 1])
    os.makedirs(SALIDA, exist_ok=True)
    for cab, nuc, ui, destino, inter in DOCS:
        if os.path.exists(os.path.join(AQUI, ui)):
            armar(cab, nuc, ui, html_doc, destino, intermedio=inter)
else:
    for cab, nuc, ui, destino, inter in DOCS:
        if os.path.exists(os.path.join(AQUI, ui)):
            for lado in ('C', 'P'):
                armar(cab, nuc, ui, html_doc, destino, intermedio=inter, lado=lado)

print('Cobros y pagos:')
ex_doc, ex_pago = extras('extras_documento.html.part'), extras('extras_pago.html.part')
ex_pago['CSS'] = ex_doc['CSS'] + SALTO + ex_pago['CSS']          # el CSS común (paleta, hoja imprimible, tema oscuro) se comparte
def inyectar_pago(html, ex):
    for marca, clave in (('/*__EXTRAS_CSS__*/', 'CSS'), ('<!--__EXTRAS_BTN__-->', 'BTN'), ('<!--__EXTRAS_HERR__-->', 'HERR'), ('<!--__EXTRAS_DER__-->', 'DER'), ('<!--__EXTRAS_OVERLAYS__-->', 'OVERLAYS'), ('/*__EXTRAS_JS__*/', 'JS')):
        assert marca in html, marca
        html = html.replace(marca, ex[clave])
    return html
html_pago = sin_comentarios.sin_comentarios_html(inyectar_pago(leer('formulario_pagos.html.part'), ex_pago))
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
