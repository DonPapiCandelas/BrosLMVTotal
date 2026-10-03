# Genera las dos plantillas de estado de cuenta a partir de estado_cuenta.tpl.ctx (una sola fuente, dos botones separados):
#   instalador/scripts/ESTADO_CUENTA_CLIENTES.ctx      cuentas por cobrar (DocRecipient = 1)
#   instalador/scripts/ESTADO_CUENTA_PROVEEDORES.ctx   cuentas por pagar  (DocRecipient = 2)
# Cada usuario ve solo el lado que le corresponde: el otro lado no se carga ni se muestra. Los archivos generados NO se editan a mano.
# También genera docs/ESTADO_CUENTA_CLIENTES.md y docs/ESTADO_CUENTA_PROVEEDORES.md desde estado_cuenta.tpl.md.
# Uso:  python build/saldos/generar.py
import os

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.normpath(os.path.join(AQUI, '..', '..'))
tpl = open(os.path.join(AQUI, 'estado_cuenta.tpl.ctx'), encoding='utf-8').read().replace('\r\n', '\n')

LADOS = [
    dict(APPKEY='ESTADO_CUENTA_CLIENTES', DOC='ESTADO_CUENTA_CLIENTES', NOMBRE='Estado de cuenta de clientes (cuentas por cobrar)', TITULO='Cuentas por cobrar',
         ENT='cliente', ENT_PL='clientes', OTRO='«Estado de cuenta de proveedores»', REC='1', LADO='C', ABONOS='cobros y notas de crédito'),
    dict(APPKEY='ESTADO_CUENTA_PROVEEDORES', DOC='ESTADO_CUENTA_PROVEEDORES', NOMBRE='Estado de cuenta de proveedores (cuentas por pagar)', TITULO='Cuentas por pagar',
         ENT='proveedor', ENT_PL='proveedores', OTRO='«Estado de cuenta de clientes»', REC='2', LADO='P', ABONOS='pagos y notas de crédito'),
]
EXCEL = open(os.path.join(AQUI, 'excel.cs.part'), encoding='utf-8').read().replace('\r\n', '\n')
for L in LADOS:
    t = tpl.replace('{{EXCEL}}', EXCEL)
    for k, v in L.items():
        t = t.replace('{{' + k + '}}', v)
    assert '{{' not in t, 'quedó un marcador sin reemplazar'
    open(os.path.join(RAIZ, 'instalador', 'scripts', L['APPKEY'] + '.ctx'), 'w', encoding='utf-8', newline='').write(t.replace('\n', '\r\n'))
md = open(os.path.join(AQUI, 'estado_cuenta.tpl.md'), encoding='utf-8').read().replace('\r\n', '\n')
for L in LADOS:
    t = md
    for k, v in L.items():
        t = t.replace('{{' + k + '}}', v)
    assert '{{' not in t
    open(os.path.join(RAIZ, 'docs', L['DOC'] + '.md'), 'w', encoding='utf-8', newline='').write(t.replace('\n', '\r\n'))
print('ok')
