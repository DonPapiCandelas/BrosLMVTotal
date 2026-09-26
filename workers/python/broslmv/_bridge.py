# BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
# Copyright (C) 2026 Cristofer Candelas Garcia
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

from __future__ import annotations

from typing import Any, Callable


class StdioBridge:
    def __init__(self, sender: Callable[[str, list, dict], Any]) -> None:
        self._sender = sender

    def call(self, method: str, *args: Any, **kwargs: Any) -> Any:
        return self._sender(method, list(args), dict(kwargs))


_bridge: StdioBridge | None = None


def set_bridge(bridge: StdioBridge) -> None:
    global _bridge
    _bridge = bridge


def call(method: str, *args: Any, **kwargs: Any) -> Any:
    if _bridge is None:
        raise RuntimeError("El puente BrosLMV no esta inicializado. Ejecuta el script desde BrosLMV.Host.")
    return _bridge.call(method, *args, **kwargs)
