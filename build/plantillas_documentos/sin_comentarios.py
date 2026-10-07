#!/usr/bin/env python
# Quita los comentarios del código de una plantilla ya armada: la documentación de una plantilla va en su guía («Ver documentación»), no en el código.
# Conserva la cabecera que lee la Consola (lang, AppKey recomendado, Plantilla, Categoria, Documentacion, job, timeout) y UNA línea de descripción.
# Uso como programa:  python sin_comentarios.py archivo.ctx [archivo2.py ...]      (reescribe los archivos)
import io
import re
import sys
import tokenize

META = re.compile(r'^\s*(?://|#)\s*(lang|AppKey recomendado|Plantilla|Categor[ií]a|Documentaci[oó]n|job|timeout)\s*:', re.I)


def _cabecera(lineas, marca):
    """Índice donde termina la cabecera (líneas de metadatos, #r y comentarios iniciales) y la lista de líneas que se conservan."""
    conservar, descripcion = [], None
    i = 0
    while i < len(lineas):
        l = lineas[i]
        s = l.strip()
        if s == '':
            i += 1
            continue
        if META.match(l) or s.startswith('#r ') or (marca == '#' and s.startswith('# lang')):
            conservar.append(l)
        elif s.startswith(marca):
            if descripcion is None and s.strip(marca + ' ').strip() != '' and not set(s) <= set(marca + '=-* '):
                descripcion = l
        else:
            break
        i += 1
    if descripcion:
        conservar.append(descripcion)
    return i, conservar


def sin_comentarios_cs(t):
    t = t.replace('\r\n', '\n')
    lineas = t.split('\n')
    fin, cab = _cabecera(lineas, '//')
    resto = '\n'.join(lineas[fin:])
    out, i, n = [], 0, len(resto)
    while i < n:
        c = resto[i]
        if resto[i:i + 2] == '//':
            while i < n and resto[i] != '\n':
                i += 1
        elif resto[i:i + 2] == '/*':
            j = resto.find('*/', i + 2)
            i = n if j < 0 else j + 2
        elif resto[i:i + 2] in ('@"',) or resto[i:i + 3] in ('$@"', '@$"'):
            j = resto.index('"', i) + 1               # cadena textual: dura hasta una comilla que no sea doble
            while True:
                k = resto.index('"', j)
                if resto[k + 1:k + 2] == '"':
                    j = k + 2
                else:
                    j = k + 1
                    break
            out.append(resto[i:j])
            i = j
        elif c == '"' or resto[i:i + 2] == '$"':
            j = resto.index('"', i) + 1
            while resto[j] != '"':
                j += 2 if resto[j] == chr(92) else 1
            j += 1
            out.append(resto[i:j])
            i = j
        elif c == "'":
            j = i + 1
            while resto[j] != "'":
                j += 2 if resto[j] == chr(92) else 1
            out.append(resto[i:j + 1])
            i = j + 1
        else:
            out.append(c)
            i += 1
    return _limpiar_lineas('\n'.join(cab) + '\n\n' + ''.join(out))


def sin_comentarios_py(t):
    t = t.replace('\r\n', '\n')
    lineas = t.split('\n')
    fin, cab = _cabecera(lineas, '#')
    resto = '\n'.join(lineas[fin:]) + '\n'
    quitar = []
    for tk in tokenize.generate_tokens(io.StringIO(resto).readline):
        if tk.type == tokenize.COMMENT:
            quitar.append(tk)
    rl = resto.split('\n')
    for tk in reversed(quitar):
        r, c = tk.start[0] - 1, tk.start[1]
        rl[r] = rl[r][:c].rstrip()
    return _limpiar_lineas('\n'.join(cab) + '\n\n' + '\n'.join(rl))


def _limpiar_lineas(t):
    """Sin espacios al final y sin más de una línea en blanco seguida."""
    res, blanco = [], 0
    for l in t.split('\n'):
        l = l.rstrip()
        if l == '':
            blanco += 1
            if blanco > 1:
                continue
        else:
            blanco = 0
        res.append(l)
    return '\n'.join(res).strip('\n') + '\n'


def sin_comentarios_html(h):
    """Páginas incrustadas (JS y CSS): quita las líneas que son solo un comentario."""
    out = []
    for l in h.split('\n'):
        s = l.strip()
        if s.startswith('//') or (s.startswith('/*') and s.endswith('*/') and '__' not in s) or (s.startswith('<!--') and s.endswith('-->') and '__' not in s):
            continue
        out.append(l)
    return '\n'.join(out)


def procesar(ruta):
    t = open(ruta, encoding='utf-8-sig').read()
    bom = open(ruta, 'rb').read(3) == b'\xef\xbb\xbf'
    nuevo = sin_comentarios_py(t) if ruta.lower().endswith('.py') else sin_comentarios_cs(t)
    open(ruta, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(nuevo)
    return len(t), len(nuevo)


if __name__ == '__main__':
    for r in sys.argv[1:]:
        a, b = procesar(r)
        print('%s: %d -> %d caracteres' % (r, a, b))
