// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using BrosLMV.Protocol;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Host.Workers;

public sealed class SqlConnectionResolver
{
    public const string DefaultCredFile = @"C:\BrosLMV\bin\broslmv_cred.dat";
    public const string DefaultConnFile = @"C:\BrosLMV\bin\broslmv_conn.txt";

    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("BrosLMV.DPAPI.v1.cadena-conexion");

    public string Resolve(BrosLMV.Protocol.ExecutionContext? ctx)
    {
        string baseConnection = ReadConfiguredConnectionString();
        if (string.IsNullOrWhiteSpace(baseConnection))
            throw new InvalidOperationException("No hay cadena SQL configurada para Python. Configura BrosLMV desde el instalador/consola o define BROSLMV_SQL_CONN.");

        var builder = new SqlConnectionStringBuilder(baseConnection);
        if (!string.IsNullOrWhiteSpace(ctx?.Servidor))
            builder.DataSource = ctx.Servidor;
        if (!string.IsNullOrWhiteSpace(ctx?.BaseDatos))
            builder.InitialCatalog = ctx.BaseDatos;

        if (!builder.ContainsKey("TrustServerCertificate"))
            builder.TrustServerCertificate = true;
        if (builder.ConnectTimeout <= 0)
            builder.ConnectTimeout = 8;

        return builder.ConnectionString;
    }

    public string ReadConfiguredConnectionString()
    {
        string? env = Environment.GetEnvironmentVariable("BROSLMV_SQL_CONN");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        string credFile = Environment.GetEnvironmentVariable("BROSLMV_CRED_FILE") ?? DefaultCredFile;
        string cred = ReadDpapiFile(credFile);
        if (!string.IsNullOrWhiteSpace(cred)) return cred;

        string connFile = Environment.GetEnvironmentVariable("BROSLMV_CONN_FILE") ?? DefaultConnFile;
        return ReadPlainTextFile(connFile);
    }

    private static string ReadDpapiFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            byte[] encrypted = File.ReadAllBytes(path);
            if (encrypted.Length == 0) return "";
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string ReadPlainTextFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            string text = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) return "";
            if (text.Contains("TU_PASSWORD", StringComparison.OrdinalIgnoreCase)) return "";
            return text;
        }
        catch
        {
            return "";
        }
    }
}
