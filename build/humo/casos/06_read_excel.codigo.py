# lang: python
# job: safe-offline
# Humo T4.1 #6: ctx.read_excel(). Puro I/O de archivo (openpyxl), sin UI -- el mas facil de
# los 3 restantes de automatizar de verdad (no solo "no truena"). Autocontenido: escribe su
# propio .xlsx de prueba con openpyxl (misma libreria que ya usa ctx.read_excel por dentro)
# y lo vuelve a leer, comparando el contenido real.
import os
import openpyxl
from broslmv import ctx

ruta = os.path.join(os.environ.get("TEMP", "."), "humo_t41_fixture.xlsx")

wb = openpyxl.Workbook()
ws = wb.active
ws.append(["ProductKey", "Cantidad"])
ws.append(["HUMO-PROD-001", 5])
wb.save(ruta)

filas = ctx.read_excel(ruta)

try:
    os.remove(ruta)
except OSError:
    pass

if len(filas) != 1:
    raise ValueError("Se esperaba 1 fila, se leyeron " + str(len(filas)))
if filas[0].get("ProductKey") != "HUMO-PROD-001" or filas[0].get("Cantidad") != 5:
    raise ValueError("Contenido inesperado: " + str(filas[0]))

result = "read_excel OK: " + str(filas[0])
