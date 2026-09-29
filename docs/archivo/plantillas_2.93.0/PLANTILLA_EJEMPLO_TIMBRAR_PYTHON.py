# lang: python
#
# PLANTILLA_EJEMPLO_TIMBRAR_PYTHON.py
# Timbra el documento seleccionado en el grid, usando el motor de facturacion propio de
# Comercial (ctx.erp.Timbrar) -- no un PAC aparte ni una libreria de firmado de BrosLMV.
# Punto de partida: agregalo a un boton en Facturas de Venta/Compra o Notas de Credito.
#
# QUE PASA POR DEBAJO (docs/MANUAL.md #6.14): ctx.erp.Timbrar usa el MISMO componente COM
# que usa el propio modulo de facturacion de Comercial para timbrar. Si falla, el mensaje
# de error viene directo del PAC/SAT -- no hay que adivinar, ya trae el detalle real.
#
# IMPORTANTE: timbrar es una operacion fiscal REAL. Antes de usar esto en produccion,
# cambia PRUEBAS a True un rato (usa el modo de pruebas del PAC configurado, sin generar
# timbre fiscal real) y confirma que el documento esta completo (partidas, cliente, forma
# de pago) -- si el CFDI esta incompleto, Timbrar avisa que fallo, no lo arregla por ti.

from broslmv import ctx

PRUEBAS = False  # True mientras pruebas el flujo; False para timbrar de verdad

ids = ctx.get_selected_ids()
if not ids:
    ctx.msg("Selecciona un documento en el grid antes de timbrar.")
else:
    doc = ids[0]

    # Evita re-timbrar: el PAC cobra por timbre, y volver a timbrar uno ya firmado
    # normalmente falla o genera un timbre duplicado que despues hay que cancelar.
    if ctx.erp.AlreadyDocsSigned(doc):
        ctx.msg("El documento " + str(doc) + " ya está timbrado. No se vuelve a timbrar.")
    else:
        aviso = "Vas a timbrar el documento " + str(doc)
        aviso += " EN MODO PRUEBA (no genera timbre fiscal real)." if PRUEBAS else " EN PRODUCCIÓN (timbre fiscal real)."
        if ctx.confirm(aviso + "\n\n¿Continuar?"):
            try:
                ctx.erp.Timbrar(doc, PRUEBAS)
                ctx.msg("Documento " + str(doc) + " timbrado correctamente" +
                         (" (modo prueba)." if PRUEBAS else "."))
                ctx.erp.RefreshGrid()
            except Exception as ex:
                ctx.msg("Error al timbrar:\n\n" + str(ex), "Error")
