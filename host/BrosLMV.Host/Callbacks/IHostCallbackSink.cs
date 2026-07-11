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

// IHostCallbackSink.cs — seam de callbacks del host HACIA el addon (C5d).
// Cuando un script Python llama ctx.msg / ctx.log / ctx.progress, el host recibe el
// callback y lo entrega aquí. Hoy el addon-cliente no está integrado, así que el sink
// por defecto escribe a logs; cuando el addon se conecte, su relay reenviará estos
// callbacks como UiRequest / LogEvent / ProgressEvent por el Named Pipe.

using BrosLMV.Host.Logging;

namespace BrosLMV.Host.Callbacks;

public interface IHostCallbackSink
{
    void Message(string executionId, string text, string title);
    void Log(string executionId, string level, string text);
    void Progress(string executionId, string text, int percent);
    Dictionary<string, object?> Form(string executionId, Dictionary<string, object?> spec);
    void ShowHtml(string executionId, string html, string title, int width, int height, bool modal);
}

// Sink por defecto: registra los callbacks en C:\BrosLMV\logs\host-callbacks.log
// (con el execution_id para correlacionar). Reemplazable por el relay real al addon.
public sealed class LoggingHostCallbackSink : IHostCallbackSink
{
    private readonly string _path;
    private readonly object _lock = new();

    public LoggingHostCallbackSink(string? path = null) =>
        _path = path ?? LogPaths.File("host-callbacks.log");

    public void Message(string executionId, string text, string title) =>
        Append(executionId, "MSG", $"[{title}] {text}");

    public void Log(string executionId, string level, string text) =>
        Append(executionId, "LOG", $"[{level}] {text}");

    public void Progress(string executionId, string text, int percent) =>
        Append(executionId, "PROGRESS", $"{percent}% {text}");

    public Dictionary<string, object?> Form(string executionId, Dictionary<string, object?> spec)
    {
        Append(executionId, "FORM", "Formulario solicitado sin addon conectado.");
        return new Dictionary<string, object?> { ["submitted"] = false };
    }

    public void ShowHtml(string executionId, string html, string title, int width, int height, bool modal) =>
        Append(executionId, "SHOW_HTML", "Ventana HTML solicitada sin addon conectado.");

    private void Append(string executionId, string kind, string detail)
    {
        string line = $"{DateTimeOffset.UtcNow:O}\t{executionId}\t{kind}\t{detail}";
        try { lock (_lock) { LogPaths.EnsureDir(_path); File.AppendAllText(_path, line + Environment.NewLine); } }
        catch { /* el logging nunca debe tumbar una ejecución */ }
    }
}
