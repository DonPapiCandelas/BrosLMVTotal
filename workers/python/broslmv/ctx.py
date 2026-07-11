# BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
# Copyright (C) 2026 Cristofer Candelas Garcia
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

from __future__ import annotations

from typing import Any

from . import _bridge


class _Erp:
    """Espejo de ctx.erp (ErpContext de C#). Cada atributo es un método de XEngine que se
    relaya al addon en proceso (que tiene la conexión/engine vivos). Los nombres son los
    mismos que en C# (PascalCase): ctx.erp.GetProductStock(125, 0), ctx.erp.GetSalePrice(1)...
    El addon resuelve el método contra ErpContext (sin distinguir mayúsculas) y, si no es un
    wrapper tipado, cae a ctx.erp.Call(nombre, *args)."""

    def __getattr__(self, name: str):
        def _call(*args: Any) -> Any:
            return _bridge.call("erp", name, list(args))
        return _call


def _sql_lit(v: Any) -> str:
    """Convierte un valor Python a literal SQL seguro (escapa comillas)."""
    import datetime
    if v is None:
        return "NULL"
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, (int, float)):
        return str(v)
    if isinstance(v, (datetime.date, datetime.datetime)):
        return "'" + v.isoformat(sep=" ") + "'"
    return "N'" + str(v).replace("'", "''") + "'"


# Caché por tabla de la columna identidad (PK autoincremental). Evita re-consultar.
_PK_CACHE: dict[str, str | None] = {}
# Caché del flag de solo-lectura del contexto (no cambia durante una ejecución).
_RO_CACHE: dict[str, bool] = {}


class _Record:
    """Registro 'active-record' genérico sobre una tabla. Patrón:

        doc = ctx.nuevo("docDocument")
        doc["ModuleID"] = 183
        doc.guardar()          # INSERT; devuelve el nuevo ID (también en doc.id)
        print(doc["DocumentID"])

    Reusa el relay SQL (conexión viva del addon). Genérico para cualquier tabla.
    """

    def __init__(self, ctx: "Context", tabla: str):
        self._ctx = ctx
        self._tabla = tabla
        self._campos: dict[str, Any] = {}
        self._originales: dict[str, Any] = {}
        self._pkcol: str | None = None
        self.id: Any = None

    def __setitem__(self, k: str, v: Any) -> None:
        self._campos[k] = v

    def __getitem__(self, k: str) -> Any:
        return self._campos.get(k)

    def set(self, **kwargs: Any) -> "_Record":
        self._campos.update(kwargs)
        return self

    def _guarda_lectura(self) -> None:
        if "ro" not in _RO_CACHE:
            try:
                _RO_CACHE["ro"] = bool(self._ctx.context().get("solo_lectura", False))
            except Exception:
                _RO_CACHE["ro"] = False
        if _RO_CACHE["ro"]:
            raise RuntimeError("Modo SOLO LECTURA activo: no se puede escribir.")

    def _pk(self) -> str | None:
        if self._tabla not in _PK_CACHE:
            try:
                _PK_CACHE[self._tabla] = self._ctx.scalar(
                    "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(%s) AND is_identity=1"
                    % _sql_lit(self._tabla))
            except Exception:
                _PK_CACHE[self._tabla] = None
        return _PK_CACHE[self._tabla]

    def guardar(self) -> Any:
        """INSERT de los campos seteados. Devuelve el nuevo ID (columna identidad)."""
        self._guarda_lectura()
        if not self._campos:
            raise ValueError("No hay campos que guardar.")
        cols = list(self._campos.keys())
        vals = ", ".join(_sql_lit(self._campos[c]) for c in cols)
        self._pkcol = self._pk()
        out = (" OUTPUT INSERTED.%s" % self._pkcol) if self._pkcol else ""
        sql = "INSERT INTO %s (%s)%s VALUES (%s)" % (self._tabla, ", ".join(cols), out, vals)
        if self._pkcol:
            self.id = self._ctx.scalar(sql)
            self._campos[self._pkcol] = self.id
        else:
            self._ctx.execute(sql)
            self.id = None
        self._originales = dict(self._campos)
        return self.id

    def actualizar(self) -> int:
        """UPDATE solo de los campos modificados (excepto la PK) por la PK ya conocida."""
        self._guarda_lectura()
        if not self._pkcol or self.id is None:
            raise RuntimeError("actualizar() requiere un registro ya guardado o cargado (con PK).")
        cambios = {c: v for c, v in self._campos.items()
                   if c != self._pkcol and (c not in self._originales or self._originales[c] != v)}
        if not cambios:
            return 0
        sets = ", ".join("%s=%s" % (c, _sql_lit(v)) for c, v in cambios.items())
        filas = self._ctx.execute("UPDATE %s SET %s WHERE %s=%s" %
                                  (self._tabla, sets, self._pkcol, _sql_lit(self.id)))
        self._originales.update(cambios)
        return filas

    def _cargar(self, pk_value: Any) -> "_Record":
        self._pkcol = self._pk()
        if not self._pkcol:
            raise RuntimeError("La tabla '%s' no tiene columna identidad." % self._tabla)
        rows = self._ctx.query(
            "SELECT TOP 1 * FROM %s WHERE %s=%s AND DeletedOn IS NULL"
            % (self._tabla, self._pkcol, _sql_lit(pk_value)))
        if not rows:
            raise RuntimeError("No se encontró registro en '%s' con %s=%s."
                               % (self._tabla, self._pkcol, pk_value))
        self._campos = dict(rows[0])
        self._originales = dict(rows[0])
        self.id = pk_value
        return self

    def eliminar(self) -> int:
        """Borrado LÓGICO (DeletedOn=GETDATE()) por la PK conocida."""
        self._guarda_lectura()
        if not self._pkcol or self.id is None:
            raise RuntimeError("eliminar() requiere un registro ya guardado (con PK).")
        return self._ctx.execute("UPDATE %s SET DeletedOn=GETDATE() WHERE %s=%s" %
                                 (self._tabla, self._pkcol, _sql_lit(self.id)))


class Context:
    @property
    def user_id(self) -> int:
        return int(_bridge.call("context").get("user_id", 0))

    @property
    def module_id(self) -> int:
        return int(_bridge.call("context").get("module_id", 0))

    @property
    def empresa(self) -> str:
        return str(_bridge.call("context").get("empresa", ""))

    @property
    def app_key(self) -> str:
        return str(_bridge.call("context").get("app_key", ""))

    def context(self) -> dict[str, Any]:
        return dict(_bridge.call("context"))

    @property
    def fila(self) -> dict[str, Any]:
        return dict(_bridge.call("context").get("fila_activa", {}))

    def get_selected_ids(self) -> list[int]:
        return [int(x) for x in (_bridge.call("get_selected_ids") or [])]

    def msg(self, text: str, title: str = "BrosLMV") -> None:
        _bridge.call("msg", text, title)

    def form(self, spec: dict[str, Any]) -> dict[str, Any]:
        """Muestra un formulario WinForms renderizado por el addon.

        Ejemplo:
            r = ctx.form({
                "title": "Datos",
                "fields": [
                    {"name": "proveedor", "label": "Proveedor", "type": "text", "required": True},
                    {"name": "cantidad", "label": "Cantidad", "type": "decimal", "default": 1},
                ],
            })
            if r["submitted"]:
                print(r["values"])
        """
        return dict(_bridge.call("form", spec or {}))

    def show_html(self, html: str, title: str = "BrosLMV", width: int = 800,
                   height: int = 600, modal: bool = True) -> None:
        """Muestra una ventana con HTML/CSS/JS renderizado por WebView2 (Edge/Chromium),
        embebida en el addon dentro del proceso de Comercial.

        Ejemplo:
            ctx.show_html("<h1>Hola</h1><p>Reporte generado.</p>", title="Reporte", width=900, height=650)
        """
        _bridge.call("show_html", html or "", title, width, height, modal)

    def log(self, text: str, level: str = "INFO") -> None:
        # log(text) o log(level, text) segun cuantos argumentos lleguen al host.
        if level == "INFO":
            _bridge.call("log", text)
        else:
            _bridge.call("log", level, text)

    def progress(self, text: str = "", percent: int = 0) -> None:
        _bridge.call("progress", text, int(percent))

    @property
    def execution_id(self) -> str:
        return str(_bridge.call("context").get("execution_id", ""))

    def query(self, sql: str, params: dict[str, Any] | None = None) -> list[dict[str, Any]]:
        return _bridge.call("query", sql, params or {})

    def scalar(self, sql: str, params: dict[str, Any] | None = None) -> Any:
        return _bridge.call("scalar", sql, params or {})

    def execute(self, sql: str, params: dict[str, Any] | None = None) -> int:
        return int(_bridge.call("execute", sql, params or {}))

    @property
    def erp(self) -> _Erp:
        return _Erp()

    def nuevo(self, tabla: str) -> _Record:
        """Crea un registro 'active-record' para INSERT en `tabla`:

            it = ctx.nuevo("docDocumentItem")
            it["DocumentID"] = doc_id
            it["ProductID"]  = 1
            it.guardar()
        """
        return _Record(self, tabla)


    def registro(self, tabla: str, pk: Any) -> _Record:
        """Carga un registro existente por su PK (identidad):

            doc = ctx.registro("docDocument", 11556)
            doc["Comments"] = "Modificado"
            doc.actualizar()   # solo envía los campos que cambiaron
        """
        return _Record(self, tabla)._cargar(pk)


ctx = Context()
