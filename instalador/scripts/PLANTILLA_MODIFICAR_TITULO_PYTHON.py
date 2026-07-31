# lang: python
# PLANTILLA: Modificar título del documento (Python)
#
# Qué hace: EXACTAMENTE lo mismo que las plantillas SQL/C# con el mismo nombre -- cambia el
# campo Title (nota libre, no es el Folio) del documento seleccionado.
#
# Diferencia real contra SQL/C#: ctx.execute() en vez de ctx.query() -- query es para LEER
# (SELECT), execute es para ESCRIBIR (UPDATE/INSERT/DELETE) y regresa cuántas filas afectó.
# Sin tokens de texto (ver la plantilla de Extracción de datos): se usa
# ctx.get_selected_ids() para saber cuál documento.

from broslmv import ctx

ids = ctx.get_selected_ids()
if not ids:
    ctx.msg("No se encontró el documento (¿seleccionaste uno?).")
else:
    filas = ctx.execute("UPDATE docDocument SET Title = 'Script de Prueba en Python' WHERE DocumentID = " + str(ids[0]))
    ctx.msg("Título actualizado." if filas > 0 else "No se pudo actualizar.")
