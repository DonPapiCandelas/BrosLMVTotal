# lang: python
# PLANTILLA: Extracción de datos (Python)
#
# Qué hace: EXACTAMENTE lo mismo que las plantillas SQL/C# con el mismo nombre -- consulta
# folio, total y fecha del documento seleccionado, y lo muestra.
#
# Diferencia real contra SQL/C#: Python NO TIENE tokens de texto -- {pID} no existe aquí
# (confirmado revisando ctx.py: no hay ningún ResolverTokens ni sustitución de {...}). En
# vez de eso, usas la función nativa que te da el mismo dato: ctx.get_selected_ids().
#
# Cuándo usar Python en vez de C#: si el script necesita librerías que no existen en .NET
# (openpyxl para Excel, por ejemplo) o si vas a construir una ventana HTML con
# ctx.show_html/ctx.show_html_formulario. Para algo tan simple como esto, da exactamente lo
# mismo cuál elijas.
from broslmv import ctx

ids = ctx.get_selected_ids()
if not ids:
    ctx.msg("No se encontró el documento (¿seleccionaste uno?).")
else:
    filas = ctx.query("SELECT Folio, Total, DateDocument FROM docDocument WHERE DocumentID = " + str(ids[0]))
    f = filas[0]
    ctx.msg("Folio: " + str(f["Folio"]) + "\nTotal: $" + str(f["Total"]) + "\nFecha: " + str(f["DateDocument"]))
