# lang: python
#
# PLANTILLA_EJEMPLO_IMPORTAR_EXCEL_PYTHON.py
# Lee un Excel con columnas "ProductKey" y "Cantidad", empareja cada renglón contra el
# catálogo, y crea una Requisición de Compra (módulo 1040, no afecta inventario) con los
# productos encontrados. Al final muestra un reporte de lo que sí y lo que no se pudo
# importar -- para que nunca te quedes preguntando qué pasó.
#
# ctx.read_excel usa openpyxl (nada de automatizar Excel.exe por COM): no requiere que la
# máquina de Comercial tenga Excel instalado, y es mucho más rápido.
#
# El Excel debe tener encabezados en la primera fila, por ejemplo:
#   ProductKey | Cantidad
#   00481-001  | 10
#   00481-002  | 25

from broslmv import ctx

DEPOT_ID = 1  # almacén de la requisición

ruta = ctx.select_file("Elegir Excel de productos a importar", "Excel|*.xlsx;*.xls")
if not ruta:
    ctx.log("Importación cancelada: no se eligió archivo.")
else:
    try:
        filas = ctx.read_excel(ruta)
    except Exception as ex:
        ctx.msg("No se pudo leer el Excel:\n\n" + str(ex), "Error")
        filas = None

    if filas is not None:
        if not filas:
            ctx.msg("El archivo no tiene filas (o la primera fila no tiene encabezados).")
        else:
            encontrados = []   # [(ProductID, ProductKey, ProductName, Cantidad), ...]
            no_encontrados = []  # [ProductKey, ...]

            for fila in filas:
                key = str(fila.get("ProductKey") or "").strip()
                try:
                    cantidad = float(fila.get("Cantidad") or 0)
                except (TypeError, ValueError):
                    cantidad = 0
                if not key or cantidad <= 0:
                    continue

                prod = ctx.query(
                    "SELECT TOP 1 ProductID, ProductKey, ProductName FROM orgProduct "
                    "WHERE ProductKey = @k AND DeletedOn IS NULL", {"k": key})
                if prod:
                    p = prod[0]
                    encontrados.append((int(p["ProductID"]), p["ProductKey"], p["ProductName"], cantidad))
                else:
                    no_encontrados.append(key)

            if not encontrados:
                ctx.msg("Ningún ProductKey del Excel coincide con el catálogo. No se creó nada.")
            elif not ctx.confirm(
                "Se van a importar " + str(len(encontrados)) + " producto(s)" +
                (" (" + str(len(no_encontrados)) + " no encontrados en el catálogo)." if no_encontrados else ".") +
                "\n\n¿Crear la Requisición de Compra?"
            ):
                ctx.log("Importación cancelada por el usuario tras la confirmación.")
            else:
                try:
                    doc = ctx.erp.NuevoDocumento(1040, DEPOT_ID)  # 1040 = Solicitud/Requisición de compra
                    if not doc or doc <= 0:
                        raise Exception("NuevoDocumento: " + str(ctx.erp.LastError()))

                    for pid, key, nombre, cantidad in encontrados:
                        ctx.erp.AgregarArticulo(doc, pid, cantidad)
                        if ctx.erp.LastError():
                            raise Exception("AgregarArticulo (" + key + "): " + str(ctx.erp.LastError()))

                    ctx.erp.RecalcCompleto(doc)
                    # Sin inventario: una requisición NO llama AffectStockNEW.
                    ctx.erp.Save(doc)
                    if ctx.erp.LastError():
                        raise Exception("Save: " + str(ctx.erp.LastError()))
                    ctx.erp.UpdateStatusDelivery(doc)
                    ctx.erp.RefreshGrid()
                except Exception as ex:
                    ctx.msg("Error al crear la requisición:\n\n" + str(ex), "Error")
                else:
                    resumen = "Requisición " + str(doc) + " creada con " + str(len(encontrados)) + " producto(s)."
                    if no_encontrados:
                        resumen += "\n\nNo se encontraron en el catálogo (se omitieron):\n" + "\n".join(no_encontrados)
                    ctx.msg(resumen, "Importación terminada")
