#!/usr/bin/env python3
"""Genera el manual del SDK de BrosLMV (HTML autónomo + Markdown) desde la fuente única: src/assets/sdk_catalogo.json + docs/SDK_GUIAS.md.

Uso:  python build/sdk/generar_referencia_sdk.py
Salida: instalador/docs/plantillas/SDK_REFERENCIA.html   (se incrusta en la DLL; lo abre la Consola: clic secundario -> Ver ficha / Manual del SDK)
        docs/SDK_REFERENCIA.md                            (lectura en GitHub)
El catálogo se edita a mano (JSON); este script nunca lo modifica. build/sdk/verificar_catalogo_sdk.ps1 comprueba que toda función pública tenga entrada.
"""
import html
import json
import os
import re
import sys

RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
sys.path.insert(0, os.path.join(RAIZ, 'build'))
from md_a_html import convertir  # noqa: E402

NOMBRE_LANG = {'cs': 'C#', 'py': 'Python', 'sql': 'SQL'}


def slug(i):
    return re.sub(r'[^A-Za-z0-9_-]', '-', i.replace(':', '-').replace('.', '-'))


def esc(t):
    return html.escape(str(t if t is not None else ''), quote=False)


def inline(t):
    t = esc(t)
    return re.sub(r'`([^`]+)`', r'<code>\1</code>', t)


def ficha_html(e, por_id):
    s = slug(e['id'])
    lang = e['lang']
    h = [f'<article class="ficha" id="{s}" data-lang="{lang}" data-txt="{esc((e["nombre"] + " " + e["resumen"] + " " + e.get("cat", "")).lower())}">']
    h.append(f'<h3><span class="lg lg-{lang}">{NOMBRE_LANG[lang]}</span> {esc(e["nombre"])}'
             + (f' <span class="desde">desde {esc(e["desde"])}</span>' if e.get('desde') else '') + '</h3>')
    h.append(f'<pre class="firma"><code>{esc(e["firma"])}</code></pre>')
    h.append(f'<p class="resumen">{inline(e["resumen"])}</p>')
    if e.get('detalle'):
        h.append(f'<p>{inline(e["detalle"])}</p>')
    if e.get('params'):
        h.append('<table><thead><tr><th>Parámetro</th><th>Tipo</th><th>Qué es</th></tr></thead><tbody>'
                 + ''.join(f'<tr><td><code>{esc(p[0])}</code></td><td>{esc(p[1])}</td><td>{inline(p[2])}</td></tr>' for p in e['params']) + '</tbody></table>')
    if e.get('retorna'):
        h.append(f'<p><strong>Devuelve:</strong> {inline(e["retorna"])}</p>')
    if e.get('tokens'):
        h.append('<table><thead><tr><th>Token</th><th>Valor</th></tr></thead><tbody>'
                 + ''.join(f'<tr><td><code>{esc(p[0])}</code></td><td>{inline(p[1])}</td></tr>' for p in e['tokens']) + '</tbody></table>')
    if e.get('notas'):
        h.append('<div class="notas"><strong>Ojo</strong><ul>' + ''.join(f'<li>{inline(n)}</li>' for n in e['notas']) + '</ul></div>')
    if e.get('ejemplo'):
        h.append(f'<p class="ej">Ejemplo</p><pre><code>{esc(e["ejemplo"])}</code></pre>')
    for x in e.get('ejemplo_extra', []):
        h.append(f'<p class="ej">{esc(x["titulo"])} <span class="lg lg-{x["lang"]}">{NOMBRE_LANG[x["lang"]]}</span></p><pre><code>{esc(x["codigo"])}</code></pre>')
    rel = []
    if e.get('equiv') and e['equiv'] in por_id:
        q = por_id[e['equiv']]
        rel.append(f'Equivalente en {NOMBRE_LANG[q["lang"]]}: <a href="#{slug(q["id"])}">{esc(q["nombre"])}</a>')
    if e.get('ver'):
        rel.append('Ver también: ' + ', '.join(f'<a href="#{slug(v)}">{esc(por_id[v]["nombre"])}</a>' for v in e['ver'] if v in por_id))
    if rel:
        h.append('<p class="rel">' + ' · '.join(rel) + '</p>')
    if not e.get('detalle'):
        h.append('<p class="corta">Ficha resumida: firma, descripción y ejemplo.</p>')
    h.append('</article>')
    return '\n'.join(h)


def md_ficha(e):
    m = [f'### `{e["nombre"]}` ({NOMBRE_LANG[e["lang"]]})', '', '```', e['firma'], '```', '', e['resumen'], '']
    if e.get('detalle'):
        m += [e['detalle'], '']
    if e.get('params'):
        m += ['| Parámetro | Tipo | Qué es |', '|---|---|---|'] + [f'| `{p[0]}` | {p[1]} | {p[2]} |' for p in e['params']] + ['']
    if e.get('retorna'):
        m += [f'**Devuelve:** {e["retorna"]}', '']
    if e.get('tokens'):
        m += ['| Token | Valor |', '|---|---|'] + [f'| `{p[0]}` | {p[1]} |' for p in e['tokens']] + ['']
    if e.get('notas'):
        m += ['**Ojo**'] + [f'- {n}' for n in e['notas']] + ['']
    if e.get('ejemplo'):
        m += ['Ejemplo:', '', '```', e['ejemplo'].replace('\r\n', '\n'), '```', '']
    for x in e.get('ejemplo_extra', []):
        m += [f'{x["titulo"]} ({NOMBRE_LANG[x["lang"]]}):', '', '```', x['codigo'].replace('\r\n', '\n'), '```', '']
    return '\n'.join(m)


CSS = '''
:root{--bg:#f5f7fb;--card:#fff;--tx:#1b2433;--mut:#66748a;--lin:#dde3ec;--ac:#1f5fd6;--acs:#e8f0fe;--cod:#f1f4f9;--warn:#8a5a00;--warns:#fff4e0}
@media (prefers-color-scheme:dark){:root{--bg:#0f1520;--card:#161f2e;--tx:#e6ebf3;--mut:#93a1b6;--lin:#2a3648;--ac:#6ea2ff;--acs:#1d2b47;--cod:#0f1724;--warn:#f0b35a;--warns:#3a2c12}}
*{box-sizing:border-box}html{scroll-behavior:smooth}
body{margin:0;background:var(--bg);color:var(--tx);font:14px/1.6 "Segoe UI",system-ui,sans-serif}
.wrap{display:grid;grid-template-columns:290px minmax(0,1fr);min-height:100vh}
nav{position:sticky;top:0;align-self:start;height:100vh;overflow:auto;background:var(--card);border-right:1px solid var(--lin);padding:16px 14px}
nav h1{font-size:15px;margin:0 0 2px}nav .sub{color:var(--mut);font-size:12px;margin-bottom:12px}
nav input{width:100%;padding:8px 10px;border:1px solid var(--lin);border-radius:6px;background:var(--bg);color:var(--tx);font:inherit;margin-bottom:8px}
.seg{display:flex;border:1px solid var(--lin);border-radius:7px;overflow:hidden;margin-bottom:12px}
.seg button{flex:1;border:0;background:var(--card);color:var(--tx);padding:6px 4px;font:inherit;font-size:12.5px;cursor:pointer;border-right:1px solid var(--lin)}
.seg button:last-child{border-right:0}.seg button.on{background:var(--ac);color:#fff}
nav h2{font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--mut);margin:14px 0 4px}
nav a{display:block;color:var(--tx);text-decoration:none;padding:2px 8px;border-radius:5px;font-size:13px}nav a:hover{background:var(--acs);color:var(--ac)}
main{padding:24px 34px 80px;max-width:980px}
main h1{font-size:24px;margin:0 0 6px}main h2{font-size:19px;margin:36px 0 10px;padding-top:10px;border-top:1px solid var(--lin)}
main h3{font-size:16px;margin:0 0 6px}p,li{max-width:78ch}a{color:var(--ac)}
code{background:var(--cod);padding:1px 5px;border-radius:4px;font:12.5px Consolas,monospace}
pre{background:var(--cod);padding:12px 14px;border-radius:8px;overflow:auto;margin:8px 0}pre code{background:none;padding:0}
.ficha{background:var(--card);border:1px solid var(--lin);border-radius:10px;padding:16px 20px;margin:0 0 14px}
.firma{margin:4px 0 8px}.resumen{font-weight:600;margin:0 0 6px}
.lg{font-size:11px;padding:1px 7px;border-radius:99px;background:var(--acs);color:var(--ac);vertical-align:middle;font-weight:600}
.lg-py{background:#e3f6ee;color:#12805c}.lg-sql{background:var(--warns);color:var(--warn)}
@media (prefers-color-scheme:dark){.lg-py{background:#12332a;color:#4ad0a0}}
.desde{font-size:11.5px;color:var(--mut);font-weight:400}
.ej{margin:10px 0 0;color:var(--mut);font-size:12.5px}
.notas{background:var(--warns);color:var(--warn);border-radius:8px;padding:8px 14px;margin:8px 0}.notas ul{margin:4px 0 0;padding-left:18px}
.rel{color:var(--mut);font-size:12.5px;margin-bottom:0}.corta{color:var(--mut);font-size:12px;margin:8px 0 0}
table{border-collapse:collapse;width:100%;font-size:13px;margin:8px 0}th,td{border:1px solid var(--lin);padding:5px 9px;text-align:left;vertical-align:top}th{background:var(--cod)}
.guias h2{font-size:18px}.guias h3{font-size:15px}
.ficha.oculta{display:none}.ficha:target{outline:2px solid var(--ac);outline-offset:2px}
.aviso{color:var(--mut);font-size:13px}
details.interno summary{cursor:pointer;color:var(--mut);margin:8px 0}
@media (max-width:860px){.wrap{grid-template-columns:1fr}nav{position:static;height:auto}main{padding:16px}}
'''

JS = '''
const q=document.getElementById('q'),fichas=[...document.querySelectorAll('.ficha')];let lang='todos';
function filtra(){const t=q.value.trim().toLowerCase();fichas.forEach(f=>{const okL=lang==='todos'||f.dataset.lang===lang;const okT=!t||f.dataset.txt.includes(t);f.classList.toggle('oculta',!(okL&&okT))});
 document.querySelectorAll('section[data-cat]').forEach(s=>{s.style.display=[...s.querySelectorAll('.ficha')].some(f=>!f.classList.contains('oculta'))?'':'none'})}
q.oninput=filtra;
document.querySelectorAll('.seg button').forEach(b=>b.onclick=()=>{document.querySelectorAll('.seg button').forEach(x=>x.classList.toggle('on',x===b));lang=b.dataset.l;filtra()});
if(location.hash){const e=document.getElementById(decodeURIComponent(location.hash.slice(1)));if(e){e.scrollIntoView();}}
'''


def main():
    cat = json.load(open(os.path.join(RAIZ, 'src', 'assets', 'sdk_catalogo.json'), encoding='utf-8'))
    entradas = cat['entradas']
    por_id = {e['id']: e for e in entradas}
    guias_md = open(os.path.join(RAIZ, 'docs', 'SDK_GUIAS.md'), encoding='utf-8-sig').read()
    _, guias_html = convertir(guias_md)

    publicas = [e for e in entradas if not e.get('interno')]
    internas = [e for e in entradas if e.get('interno')]
    orden = ['cs', 'py', 'sql']
    nav = ['<h2>Guías</h2><a href="#guias">Guías del SDK</a>']
    cuerpo = []
    for lg in orden:
        lista = [e for e in publicas if e['lang'] == lg]
        cats = []
        for e in lista:
            if e['cat'] not in cats:
                cats.append(e['cat'])
        nav.append(f'<h2>{NOMBRE_LANG[lg]}</h2>')
        cuerpo.append(f'<h2 id="ref-{lg}">Referencia de {NOMBRE_LANG[lg]} <span class="aviso">({len(lista)})</span></h2>')
        for c in cats:
            sid = f'cat-{lg}-{slug(c)}'
            nav.append(f'<a href="#{sid}">{esc(c or "General")}</a>')
            cuerpo.append(f'<section data-cat="1" id="{sid}"><h3 style="margin:18px 0 8px;color:var(--mut);font-size:13px;text-transform:uppercase;letter-spacing:.05em">{esc(c or "General")}</h3>'
                          + '\n'.join(ficha_html(e, por_id) for e in lista if e['cat'] == c) + '</section>')
    if internas:
        cuerpo.append('<h2 id="internas">Funciones internas</h2><details class="interno"><summary>Uso interno de la Consola (no son para scripts de usuario)</summary><ul>'
                      + ''.join(f'<li><code>{esc(e["firma"])}</code></li>' for e in internas) + '</ul></details>')
        nav.append('<h2>Otros</h2><a href="#internas">Funciones internas</a>')

    n_fichas = sum(1 for e in publicas if e.get('detalle'))
    doc = f'''<!DOCTYPE html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Manual del SDK de BrosLMV</title>
<style>{CSS}</style></head><body><div class="wrap">
<nav><h1>SDK de BrosLMV</h1><div class="sub">Manual de referencia · {len(publicas)} funciones</div>
<input id="q" type="search" placeholder="Buscar función…"><div class="seg"><button class="on" data-l="todos">Todo</button><button data-l="cs">C#</button><button data-l="py">Python</button><button data-l="sql">SQL</button></div>
{''.join(nav)}</nav>
<main><h1>Manual del SDK de BrosLMV</h1>
<p class="aviso">Todo lo que un script puede hacer con <code>ctx</code>: consultar datos, crear documentos, mostrar ventanas y más. {n_fichas} funciones tienen ficha completa (parámetros, ejemplos y notas); el resto trae firma, descripción y ejemplo.
Este manual se genera del catálogo del SDK, así que siempre coincide con lo que el panel de referencias de la Consola muestra.</p>
<div class="guias" id="guias">{guias_html}</div>
{''.join(cuerpo)}
</main></div><script>{JS}</script></body></html>'''
    salida = os.path.join(RAIZ, 'instalador', 'docs', 'plantillas', 'SDK_REFERENCIA.html')
    os.makedirs(os.path.dirname(salida), exist_ok=True)
    open(salida, 'w', encoding='utf-8', newline='\n').write(doc)

    md = ['# Manual del SDK de BrosLMV', '',
          '> Generado por `build/sdk/generar_referencia_sdk.py` desde `src/assets/sdk_catalogo.json` y `docs/SDK_GUIAS.md`. **No se edita a mano**: cambia el catálogo y regenera.',
          f'> {len(publicas)} funciones, {n_fichas} con ficha completa. La versión navegable con buscador está en la Consola (Más opciones → Manual del SDK…).', '',
          '## Guías', '', guias_md.split('\n', 2)[2] if guias_md.startswith('# ') else guias_md, '']
    for lg in orden:
        md += [f'## Referencia de {NOMBRE_LANG[lg]}', '']
        cat_ant = None
        for e in [x for x in publicas if x['lang'] == lg]:
            if e['cat'] != cat_ant:
                md += [f'#### {e["cat"] or "General"}', '']
                cat_ant = e['cat']
            md += [md_ficha(e), '']
    open(os.path.join(RAIZ, 'docs', 'SDK_REFERENCIA.md'), 'w', encoding='utf-8', newline='\n').write('\n'.join(md))
    print(f'OK: {len(publicas)} funciones ({n_fichas} con ficha completa), {len(internas)} internas -> SDK_REFERENCIA.html + docs/SDK_REFERENCIA.md')


if __name__ == '__main__':
    main()
