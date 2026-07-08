# BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
# Copyright (C) 2026 Cristofer Candelas Garcia
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""Runner minimo de Python para C3d/C5.

Lee una peticion JSON por stdin, ejecuta el codigo con `exec` y habla con el host por
mensajes JSON lineales en stdout/stdin. Las impresiones del script se capturan para no
romper el canal. El paquete `broslmv` usa este puente para implementar `ctx`.
"""

from __future__ import annotations

import contextlib
import io
import json
import sys
import time
import traceback
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from broslmv import _bridge


def main() -> int:
    raw = sys.stdin.readline()
    if not raw:
        _write({"ok": False, "errorCode": "EMPTY_REQUEST", "errorMessage": "No llego peticion."})
        return 2

    started = time.perf_counter()
    try:
        req = json.loads(raw)
        code = req.get("code") or ""
        if req.get("codeIsPath"):
            with open(code, "r", encoding="utf-8") as f:
                code = f.read()

        script_stdout = io.StringIO()
        stderr = io.StringIO()
        bridge = _bridge.StdioBridge(_send_call)
        _bridge.set_bridge(bridge)
        globals_dict = {
            "__name__": "__broslmv_script__",
            "ctx": req.get("context") or {},
        }

        with contextlib.redirect_stdout(script_stdout), contextlib.redirect_stderr(stderr):
            exec(compile(code, "<broslmv-python>", "exec"), globals_dict, globals_dict)

        elapsed_ms = int((time.perf_counter() - started) * 1000)
        result = globals_dict.get("result", "")
        _write(
            {
                "type": "completed",
                "ok": True,
                "returnValue": "" if result is None else str(result),
                "stdout": script_stdout.getvalue(),
                "stderr": stderr.getvalue(),
                "rowsAffected": int(globals_dict.get("rows_affected", 0) or 0),
                "elapsedMs": elapsed_ms,
            }
        )
        return 0
    except Exception as exc:
        elapsed_ms = int((time.perf_counter() - started) * 1000)
        _write(
            {
                "type": "completed",
                "ok": False,
                "errorCode": exc.__class__.__name__,
                "errorMessage": str(exc),
                "traceback": traceback.format_exc(),
                "elapsedMs": elapsed_ms,
            }
        )
        return 0


def _write(obj: dict) -> None:
    sys.__stdout__.write(json.dumps(obj, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.__stdout__.flush()


def _send_call(method: str, args: list, kwargs: dict) -> object:
    request_id = uuid.uuid4().hex
    _write({"type": "context_call", "requestId": request_id, "method": method, "args": args, "kwargs": kwargs})
    raw = sys.stdin.readline()
    if not raw:
        raise RuntimeError("El host cerro el canal mientras esperaba respuesta de ctx.")
    resp = json.loads(raw)
    if resp.get("requestId") != request_id:
        raise RuntimeError("Respuesta de ctx fuera de orden.")
    if not resp.get("ok", False):
        raise RuntimeError(resp.get("error") or "Error en ctx remoto.")
    return resp.get("value")


if __name__ == "__main__":
    raise SystemExit(main())
