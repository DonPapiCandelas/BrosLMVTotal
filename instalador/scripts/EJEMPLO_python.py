# lang: python
# BrosLMV - Script de EJEMPLO en Python (v3.0, fuera de proceso).
# Copyright (C) 2026 Cristofer Candelas Garcia - GPL-3.0
#
# Como usarlo:
#   1) Copia este archivo a  C:\BrosLMV\scripts\<EMPRESA>\MiBotonPy.py
#      (o deja el nombre y crea un boton con AppKey = EJEMPLO_python).
#   2) En CONTPAQi, crea un boton cuyo ControlExecute sea "BrosLMV.<AppKey>".
#   3) Selecciona algunos documentos y pulsa el boton.
#
# El script corre en CPython fuera de proceso. Usa el contrato `ctx` del paquete broslmv.
# Devuelve un valor asignando la variable global `result` (se muestra al usuario).

from broslmv import ctx

# --- Contexto vivo del boton ---
ids = ctx.get_selected_ids()          # IDs seleccionados en el grid
usuario = ctx.user_id                  # usuario de CONTPAQi
empresa = ctx.empresa                  # base de datos activa

lineas = [
    f"Empresa: {empresa}",
    f"Usuario: {usuario}",
    f"Seleccionados: {len(ids)} -> {ids}",
]

# --- Ejemplo de SQL (relay por la conexion viva de CONTPAQi) ---
# El SQL corre contra la empresa activa (BD ComercialSP), igual que en C#. CONTPAQi
# Comercial PRO usa prefijos doc* (documentos), org* (productos/clientes), eng* (motor).
# Descomenta para probar:
#
# total = ctx.scalar("SELECT COUNT(*) FROM docDocument")
# lineas.append(f"docDocument tiene {total} filas")
#
# filas = ctx.query("SELECT TOP 5 * FROM docDocument")
# for f in filas:
#     lineas.append(str(f))
#
# El esquema real de la empresa usa ~500 tablas con prefijos doc*/org*/eng*
# (documentos, organización/catálogos, motor/configuración).

result = "\n".join(lineas)
