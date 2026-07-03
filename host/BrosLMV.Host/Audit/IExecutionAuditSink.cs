// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// IExecutionAuditSink.cs — auditoría de ejecuciones del host (C5d).
// Cada ExecuteScript se registra al terminar (éxito o error) con su execution_id para
// poder cruzar fallos con los logs de Python (Correlation IDs, ARQUITECTURA_V3 §6).

using System.Text.Json;
using BrosLMV.Host.Logging;

namespace BrosLMV.Host.Audit;

public sealed record ExecutionAuditEntry(
    string ExecutionId,
    string AppKey,
    string Empresa,
    int UserId,
    int ModuleId,
    string Language,
    string Status,           // "OK" | "ERROR"
    long ElapsedMs,
    int RowsAffected,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset TimestampUtc);

public interface IExecutionAuditSink
{
    void Record(ExecutionAuditEntry entry);
}

// Sink por defecto: una línea JSON por ejecución en C:\BrosLMV\logs\host-audit.jsonl.
public sealed class FileExecutionAuditSink : IExecutionAuditSink
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly string _path;
    private readonly object _lock = new();

    public FileExecutionAuditSink(string? path = null) =>
        _path = path ?? LogPaths.File("host-audit.jsonl");

    public void Record(ExecutionAuditEntry entry)
    {
        try
        {
            string line = JsonSerializer.Serialize(entry, Json);
            lock (_lock) { LogPaths.EnsureDir(_path); File.AppendAllText(_path, line + Environment.NewLine); }
        }
        catch { /* la auditoría nunca debe tumbar una ejecución */ }
    }
}
