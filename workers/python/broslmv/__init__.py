# BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
# Copyright (C) 2026 Cristofer Candelas Garcia
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""SDK Python minimo de BrosLMV.

El runner inyecta un puente hacia el host. Los scripts importan:

    from broslmv import ctx

C5 implementa el primer contrato remoto verificable; SQL/ERP completos llegan en los
siguientes sub-puntos del host.
"""

from .ctx import ctx

__all__ = ["ctx"]
