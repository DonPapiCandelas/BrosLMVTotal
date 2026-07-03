// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using BrosLMV.Protocol;

namespace BrosLMV.Host.Workers;

public interface IPythonContextGateway
{
    Dictionary<string, object?> Context(BrosLMV.Protocol.ExecutionContext? ctx);
    long[] GetSelectedIds(BrosLMV.Protocol.ExecutionContext? ctx);
    List<Dictionary<string, object?>> Query(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters);
    object? Scalar(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters);
    int Execute(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters);
    object? Erp(BrosLMV.Protocol.ExecutionContext? ctx, string method, List<object?> args);
}

public sealed class DefaultPythonContextGateway : IPythonContextGateway
{
    public Dictionary<string, object?> Context(BrosLMV.Protocol.ExecutionContext? ctx)
    {
        var dict = new Dictionary<string, object?>
        {
            ["app_key"] = ctx?.AppKey ?? "",
            ["empresa"] = ctx?.Empresa ?? "",
            ["servidor"] = ctx?.Servidor ?? "",
            ["base_datos"] = ctx?.BaseDatos ?? "",
            ["user_id"] = ctx?.UserId ?? 0,
            ["module_id"] = ctx?.ModuleId ?? 0,
            ["selected_ids"] = ctx?.SelectedIds.ToArray() ?? Array.Empty<long>(),
            ["language"] = ctx?.Language ?? "python",
            ["solo_lectura"] = ctx?.SoloLectura ?? false,
            ["fila_activa"] = new Dictionary<string, object?>()
        };

        if (ctx != null && ctx.FilaActiva != null)
        {
            var filaMap = new Dictionary<string, object?>();
            foreach (var kvp in ctx.FilaActiva)
            {
                filaMap[kvp.Key] = BrosLMV.Host.Protocol.ValueCodec.FromValue(kvp.Value);
            }
            dict["fila_activa"] = filaMap;
        }
        
        return dict;
    }

    public long[] GetSelectedIds(BrosLMV.Protocol.ExecutionContext? ctx) =>
        ctx?.SelectedIds.ToArray() ?? Array.Empty<long>();

    public List<Dictionary<string, object?>> Query(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters) =>
        throw new NotSupportedException("No hay gateway SQL configurado para Python. En produccion se conecta al addon/SQL proxy.");

    public object? Scalar(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters) =>
        throw new NotSupportedException("No hay gateway SQL configurado para Python. En produccion se conecta al addon/SQL proxy.");

    public int Execute(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters) =>
        throw new NotSupportedException("No hay gateway SQL configurado para Python. En produccion se conecta al addon/SQL proxy.");

    public object? Erp(BrosLMV.Protocol.ExecutionContext? ctx, string method, List<object?> args) =>
        throw new NotSupportedException("No hay gateway ERP configurado para Python. En produccion se relaya al addon (ctx.erp).");
}
