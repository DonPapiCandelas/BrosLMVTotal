#!/usr/bin/env python3
"""Convierte un .md sencillo (el de docs\\) en una página HTML autónoma para «Ver documentación» de la Consola.

Uso:  python build\\md_a_html.py docs\\CREAR_DOC_DESDE_XML.md instalador\\docs\\plantillas\\CREAR_DOC_DESDE_XML.html

Soporta: títulos, párrafos, listas (con sangría de un nivel), tablas, bloques de código, citas, líneas horizontales, `código`, **negritas**
y [enlaces](url). Sin dependencias externas (no requiere instalar nada). Página con tema claro/oscuro según el sistema.
"""
import html
import re
import sys


def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r'`([^`]+)`', r'<code>\1</code>', t)
    t = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', lambda m: '<a href="%s">%s</a>' % (m.group(2), m.group(1)) if m.group(2).startswith(('http://', 'https://', '#')) else m.group(1), t)
    return t


def convertir(md):
    lineas = md.replace('\r\n', '\n').split('\n')
    out, i, titulo = [], 0, None
    while i < len(lineas):
        l = lineas[i]
        if l.strip().startswith('```'):
            cod = []
            i += 1
            while i < len(lineas) and not lineas[i].strip().startswith('```'):
                cod.append(lineas[i]); i += 1
            i += 1
            out.append('<pre><code>%s</code></pre>' % html.escape('\n'.join(cod)))
            continue
        m = re.match(r'^(#{1,6})\s+(.*)$', l)
        if m:
            n = len(m.group(1))
            if titulo is None and n == 1: titulo = re.sub(r'[`*]', '', m.group(2))
            out.append('<h%d>%s</h%d>' % (n, inline(m.group(2)), n)); i += 1; continue
        if re.match(r'^\s*(---+|\*\*\*+)\s*$', l):
            out.append('<hr>'); i += 1; continue
        if l.strip().startswith('>'):
            cita = []
            while i < len(lineas) and lineas[i].strip().startswith('>'):
                cita.append(re.sub(r'^\s*>\s?', '', lineas[i])); i += 1
            out.append('<blockquote>%s</blockquote>' % '<br>'.join(inline(c) for c in cita if c.strip() or True))
            continue
        if l.strip().startswith('|') and i + 1 < len(lineas) and re.match(r'^\s*\|[\s:|-]+\|\s*$', lineas[i + 1]):
            enc = [c.strip() for c in l.strip().strip('|').split('|')]
            i += 2
            filas = []
            while i < len(lineas) and lineas[i].strip().startswith('|'):
                filas.append([c.strip() for c in lineas[i].strip().strip('|').split('|')]); i += 1
            out.append('<div class="tabla"><table><thead><tr>%s</tr></thead><tbody>%s</tbody></table></div>' % (
                ''.join('<th>%s</th>' % inline(c) for c in enc),
                ''.join('<tr>%s</tr>' % ''.join('<td>%s</td>' % inline(c) for c in f) for f in filas)))
            continue
        m = re.match(r'^(\s*)([-*]|\d+\.)\s+(.*)$', l)
        if m:
            ordenada = m.group(2)[0].isdigit()
            items = []
            while i < len(lineas):
                mm = re.match(r'^(\s*)([-*]|\d+\.)\s+(.*)$', lineas[i])
                if mm:
                    items.append([len(mm.group(1)), mm.group(3)]); i += 1
                elif lineas[i].strip() and lineas[i].startswith('  ') and items:
                    items[-1][1] += ' ' + lineas[i].strip(); i += 1
                else:
                    break
            tag = 'ol' if ordenada else 'ul'
            res, nivel = ['<%s>' % tag], 0
            for sang, txt in items:
                if sang >= 2 and nivel == 0: res.append('<ul>'); nivel = 1
                elif sang < 2 and nivel == 1: res.append('</ul>'); nivel = 0
                res.append('<li>%s</li>' % inline(txt))
            if nivel == 1: res.append('</ul>')
            res.append('</%s>' % tag)
            out.append(''.join(res))
            continue
        if not l.strip():
            i += 1; continue
        par = [l.strip()]
        i += 1
        while i < len(lineas) and lineas[i].strip() and not re.match(r'^(#{1,6}\s|```|>|\||\s*([-*]|\d+\.)\s)', lineas[i]):
            par.append(lineas[i].strip()); i += 1
        out.append('<p>%s</p>' % inline(' '.join(par)))
    return titulo or 'Documentación', '\n'.join(out)


PLANTILLA = '''<!DOCTYPE html>
<html lang="es"><head><meta charset="utf-8"><title>{titulo}</title>
<style>
:root {{ --bg:#ffffff; --tx:#1e293b; --mut:#64748b; --lin:#e2e8f0; --cod:#f1f5f9; --ac:#1d4ed8; --q:#f8fafc; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#0f172a; --tx:#e2e8f0; --mut:#94a3b8; --lin:#334155; --cod:#1e293b; --ac:#60a5fa; --q:#111c33; }} }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--tx); font:14px/1.6 system-ui,'Segoe UI',Roboto,Arial,sans-serif; }}
main {{ max-width:960px; margin:0 auto; padding:24px 28px 60px; }}
h1 {{ font-size:24px; margin:0 0 8px; }} h2 {{ font-size:18px; margin:32px 0 8px; padding-top:8px; border-top:1px solid var(--lin); }}
h3 {{ font-size:15px; margin:22px 0 6px; }} h4 {{ font-size:14px; margin:16px 0 4px; }}
p, li {{ max-width:78ch; }} a {{ color:var(--ac); }}
code {{ background:var(--cod); padding:1px 5px; border-radius:4px; font:12.5px Consolas,monospace; }}
pre {{ background:var(--cod); padding:12px 14px; border-radius:8px; overflow:auto; }} pre code {{ background:none; padding:0; }}
blockquote {{ margin:12px 0; padding:8px 14px; border-left:3px solid var(--ac); background:var(--q); color:var(--mut); }}
.tabla {{ overflow-x:auto; margin:12px 0; }} table {{ border-collapse:collapse; width:100%; font-size:13px; }}
th, td {{ border:1px solid var(--lin); padding:6px 10px; text-align:left; vertical-align:top; }} th {{ background:var(--cod); }}
hr {{ border:0; border-top:1px solid var(--lin); margin:24px 0; }}
</style></head><body><main>
{cuerpo}
</main></body></html>
'''


def main():
    if len(sys.argv) != 3:
        print(__doc__); return 2
    with open(sys.argv[1], encoding='utf-8-sig') as f:
        titulo, cuerpo = convertir(f.read())
    with open(sys.argv[2], 'w', encoding='utf-8', newline='\n') as f:
        f.write(PLANTILLA.format(titulo=html.escape(titulo), cuerpo=cuerpo))
    print('OK: %s -> %s (%s)' % (sys.argv[1], sys.argv[2], titulo))
    return 0


if __name__ == '__main__':
    sys.exit(main())
