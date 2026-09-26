// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;
using BrosLMV.Protocol;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Host.Workers;

public sealed class SqlServerPythonContextGateway : IPythonContextGateway
{
    private readonly DefaultPythonContextGateway _defaultGateway = new();
    private readonly SqlConnectionResolver _resolver;

    public SqlServerPythonContextGateway(SqlConnectionResolver? resolver = null)
    {
        _resolver = resolver ?? new SqlConnectionResolver();
    }

    public Dictionary<string, object?> Context(BrosLMV.Protocol.ExecutionContext? ctx) =>
        _defaultGateway.Context(ctx);

    public long[] GetSelectedIds(BrosLMV.Protocol.ExecutionContext? ctx) =>
        _defaultGateway.GetSelectedIds(ctx);

    // ctx.erp requiere el engine/XEngine vivo del addon; este gateway standalone (solo SQL por
    // credenciales) no lo tiene. En producción se usa PipeRelayGateway, que sí relaya al addon.
    public object? Erp(BrosLMV.Protocol.ExecutionContext? ctx, string method, List<object?> args) =>
        throw new NotSupportedException("ctx.erp no está disponible en modo standalone (sin addon). Usa el relay del addon.");

    public List<Dictionary<string, object?>> Query(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        using var conn = Open(ctx);
        using var cmd = CreateCommand(conn, sql, parameters);
        using var reader = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();

        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = Normalize(reader.GetValue(i));
            rows.Add(row);
        }

        return rows;
    }

    public object? Scalar(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        using var conn = Open(ctx);
        using var cmd = CreateCommand(conn, sql, parameters);
        return Normalize(cmd.ExecuteScalar());
    }

    public int Execute(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        using var conn = Open(ctx);
        using var cmd = CreateCommand(conn, sql, parameters);
        return cmd.ExecuteNonQuery();
    }

    private SqlConnection Open(BrosLMV.Protocol.ExecutionContext? ctx)
    {
        var conn = new SqlConnection(_resolver.Resolve(ctx));
        conn.Open();
        return conn;
    }

    private static SqlCommand CreateCommand(SqlConnection conn, string sql, Dictionary<string, object?> parameters)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("La consulta SQL esta vacia.", nameof(sql));

        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        foreach ((string key, object? value) in parameters)
        {
            string name = key.StartsWith("@", StringComparison.Ordinal) ? key : "@" + key;
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }

    private static object? Normalize(object? value)
    {
        if (value is null || value is DBNull) return null;
        if (value is decimal d) return d.ToString(CultureInfo.InvariantCulture);
        if (value is DateTime dt) return dt.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto) return dto.ToString("O", CultureInfo.InvariantCulture);
        if (value is Guid guid) return guid.ToString("D");
        return value;
    }
}
