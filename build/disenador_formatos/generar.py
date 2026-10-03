#!/usr/bin/env python
# Ensambla instalador/scripts/ConfiguracionFormato.ctx a partir de:
#   base.ctx            la «Configuración de formato» sin el diseñador (copia del script instalado antes del editor)
#   motor.cs            el motor de etiquetas + los valores de ejemplo del diseñador
#   acciones.cs         las acciones nuevas del puente C# <-> página (editor, diseño, referencias, logos)
#   disenador.css/.html/1-3.js   el Diseñador de formatos (se inyecta como una capa encima de la ventana)
# Edita las piezas y regenera; no edites el .ctx generado. Uso:  python build/disenador_formatos/generar.py
import os
AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.abspath(os.path.join(AQUI, '..', '..', 'instalador', 'scripts', 'ConfiguracionFormato.ctx'))


def leer(n):
    return open(os.path.join(AQUI, n), encoding='utf-8').read().replace('\r\n', '\n')


t = leer('base.ctx')


def sub(a, b):
    global t
    assert t.count(a) == 1, (t.count(a), a[:90])
    t = t.replace(a, b, 1)


dq = lambda x: x.replace('"', '""')                      # la página vive dentro de una cadena @"..." de C#: las comillas se duplican
js = leer('disenador1.js') + '\n' + leer('disenador2.js') + '\n' + leer('disenador3.js')
sub('// ===================== ventana + WebView2 =====================', leer('motor.cs') + '\n// ===================== ventana + WebView2 =====================')
sub('                else if (action == "editarHtml")', leer('acciones.cs').rstrip('\n') + '\n                else if (action == "editarHtml")')
sub("</style></head>\n<body><div class='app'>", dq(leer('disenador.css')) + "\n</style></head>\n<body><div class='app'>")
sub("<div id='toast' class='toast'></div>\n<script>", dq(leer('disenador.html')) + "\n<div id='toast' class='toast'></div>\n<script>")
sub("(async function(){DATA=await call('cargarTodo');renderList('');renderDetail(DATA.general);})();\n</script></body></html>\";",
    dq(js) + "\n\n(async function(){DATA=await call('cargarTodo');renderList('');renderDetail(DATA.general);})();\n</script></body></html>\";")
# el botón de la lista de formatos abre el diseñador
sub("<button class='btn sm' onclick='editarHtml()'>Editar HTML</button>", "<button class='btn sm primary' onclick='editarHtml()'>Diseñar formato…</button>")
sub("async function editarHtml(){const id=+val('fmt');if(!id){toast('No hay archivo','bad');return;}await call('editarHtml',{formatId:id});}",
    "async function editarHtml(){const id=+val('fmt');if(!id){toast('No hay archivo','bad');return;}abrirDisenador(id);}")
sub('usa “Editar HTML” para ajustarlo', 'usa “Diseñar formato…” para ajustarlo')
open(SALIDA, 'w', encoding='utf-8', newline='').write(t.replace('\n', '\r\n'))
print('ok', len(t))
