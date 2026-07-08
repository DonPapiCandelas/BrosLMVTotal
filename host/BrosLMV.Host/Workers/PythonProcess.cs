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

// PythonProcess.cs - invocacion minima del runner Python (C3d).
// Este es un protocolo interno host->worker por JSON lineal. El canal publico
// addon->host sigue siendo Named Pipes + Protobuf; el paquete Python `broslmv`
// de C5 podra reemplazar este transporte interno sin cambiar el contrato externo.

using System.Diagnostics;
using System.Text.Json;
using BrosLMV.Host.Callbacks;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Workers;

public sealed class PythonProcess
{
    private readonly string _pythonExe;
    private readonly string _runnerPath;
    private readonly IPythonContextGateway _gateway;
    private readonly IHostCallbackSink _callbacks;
    private string _executionId = "";

    public PythonProcess(string? pythonExe = null, string? runnerPath = null,
        IPythonContextGateway? gateway = null, IHostCallbackSink? callbacks = null)
    {
        _pythonExe = ResolvePythonExe(pythonExe);
        _runnerPath = ResolveRunnerPath(runnerPath);
        _gateway = gateway ?? new DefaultPythonContextGateway();
        _callbacks = callbacks ?? new LoggingHostCallbackSink();
    }

    public async Task<PythonExecutionResult> ExecuteAsync(ExecuteScript request, CancellationToken ct, string? executionId = null)
    {
        _executionId = executionId ?? "";
        int timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120_000;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            Arguments = Quote(_runnerPath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sw = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
                return PythonExecutionResult.Fail("PYTHON_START_FAILED", "No se pudo iniciar python.", "", sw.ElapsedMilliseconds);

            // StandardInputEncoding debe ser UTF-8 para que el payload JSON llegue sin corrupción.
            // Se configura en el ProcessStartInfo.
            string payload = JsonSerializer.Serialize(ToRunnerRequest(request, _executionId), JsonOptions());
            // Escribir bytes UTF-8 explícitos para máxima compatibilidad
            byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload + "\n");
            await process.StandardInput.BaseStream.WriteAsync(payloadBytes, 0, payloadBytes.Length, timeoutCts.Token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            RunnerResponse? runner = await ReadUntilCompletedAsync(process, request, timeoutCts.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            string stderr = await stderrTask.ConfigureAwait(false);
            sw.Stop();

            if (process.ExitCode != 0)
                return PythonExecutionResult.Fail("PYTHON_PROCESS_FAILED",
                    $"El runner Python termino con codigo {process.ExitCode}.", stderr, sw.ElapsedMilliseconds);

            if (runner is null)
                return PythonExecutionResult.Fail("PYTHON_EMPTY_RESPONSE", "El runner Python no devolvio respuesta final.", stderr, sw.ElapsedMilliseconds);

            return runner.Ok
                ? PythonExecutionResult.Ok(runner.ReturnValue ?? "", runner.Stdout ?? "", runner.RowsAffected, runner.ElapsedMs, sw.ElapsedMilliseconds)
                : PythonExecutionResult.Fail(runner.ErrorCode ?? "PYTHON_ERROR", runner.ErrorMessage ?? "Error en Python.",
                    runner.Traceback ?? runner.Stderr ?? "", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TryKill(process);
            sw.Stop();
            return PythonExecutionResult.Fail("PYTHON_TIMEOUT", $"El script Python excedio {timeoutMs} ms.", "", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            TryKill(process);
            sw.Stop();
            return PythonExecutionResult.Fail("PYTHON_HOST_ERROR", ex.Message, ex.ToString(), sw.ElapsedMilliseconds);
        }
    }

    private static RunnerRequest ToRunnerRequest(ExecuteScript request, string executionId)
    {
        var ctx = request.Context;
        return new RunnerRequest
        {
            Code = request.Code ?? "",
            CodeIsPath = request.CodeIsPath,
            Context = new Dictionary<string, object?>
            {
                ["execution_id"] = executionId,
                ["app_key"] = ctx?.AppKey ?? "",
                ["empresa"] = ctx?.Empresa ?? "",
                ["servidor"] = ctx?.Servidor ?? "",
                ["base_datos"] = ctx?.BaseDatos ?? "",
                ["user_id"] = ctx?.UserId ?? 0,
                ["module_id"] = ctx?.ModuleId ?? 0,
                ["selected_ids"] = ctx?.SelectedIds.ToArray() ?? Array.Empty<long>(),
                ["language"] = ctx?.Language ?? "python",
                ["solo_lectura"] = ctx?.SoloLectura ?? false,
            }
        };
    }

    private async Task<RunnerResponse?> ReadUntilCompletedAsync(Process process, ExecuteScript request, CancellationToken ct)
    {
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return null;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var msg = JsonSerializer.Deserialize<RunnerMessage>(line, JsonOptions());
            if (msg is null) continue;

            if (string.Equals(msg.Type, "context_call", StringComparison.OrdinalIgnoreCase))
            {
                var response = HandleContextCall(request, msg);
                string json = JsonSerializer.Serialize(response, JsonOptions());
                await process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                continue;
            }

            if (string.Equals(msg.Type, "completed", StringComparison.OrdinalIgnoreCase) || msg.Ok.HasValue)
                return msg.ToRunnerResponse();
        }
    }

    private RunnerContextResponse HandleContextCall(ExecuteScript request, RunnerMessage msg)
    {
        try
        {
            object? value = msg.Method switch
            {
                "context" => _gateway.Context(request.Context),
                "get_selected_ids" => _gateway.GetSelectedIds(request.Context),
                "msg" => HandleMsg(msg),
                "form" => HandleForm(msg),
                "log" => HandleLog(msg),
                "progress" => HandleProgress(msg),
                "query" => _gateway.Query(request.Context, GetStringArg(msg, 0), GetDictArg(msg, 1)),
                "scalar" => _gateway.Scalar(request.Context, GetStringArg(msg, 0), GetDictArg(msg, 1)),
                "execute" => _gateway.Execute(request.Context, GetStringArg(msg, 0), GetDictArg(msg, 1)),
                "erp" => _gateway.Erp(request.Context, GetStringArg(msg, 0), GetListArg(msg, 1)),
                _ => throw new NotSupportedException("Metodo ctx no soportado todavia: " + msg.Method),
            };
            return new RunnerContextResponse { RequestId = msg.RequestId, Ok = true, Value = value };
        }
        catch (Exception ex)
        {
            return new RunnerContextResponse { RequestId = msg.RequestId, Ok = false, Error = ex.Message };
        }
    }

    private object HandleMsg(RunnerMessage msg)
    {
        _callbacks.Message(_executionId, GetStringArg(msg, 0), GetStringArg(msg, 1, "BrosLMV"));
        return true;
    }

    private object HandleForm(RunnerMessage msg)
    {
        return _callbacks.Form(_executionId, GetDictArg(msg, 0));
    }

    private object HandleLog(RunnerMessage msg)
    {
        // log(text) o log(level, text)
        string a0 = GetStringArg(msg, 0);
        if (msg.Args.Count >= 2) _callbacks.Log(_executionId, a0, GetStringArg(msg, 1));
        else _callbacks.Log(_executionId, "INFO", a0);
        return true;
    }

    private object HandleProgress(RunnerMessage msg)
    {
        _callbacks.Progress(_executionId, GetStringArg(msg, 0), GetIntArg(msg, 1));
        return true;
    }

    private static string GetStringArg(RunnerMessage msg, int index, string fallback = "")
    {
        if (msg.Args.Count <= index) return fallback;
        var v = FromJsonElement(msg.Args[index]);
        return v?.ToString() ?? fallback;
    }

    private static int GetIntArg(RunnerMessage msg, int index, int fallback = 0)
    {
        if (msg.Args.Count <= index) return fallback;
        object? v = FromJsonElement(msg.Args[index]);
        return v switch
        {
            long l => (int)l,
            int i => i,
            double d => (int)d,
            decimal m => (int)m,
            _ => int.TryParse(v?.ToString(), out int p) ? p : fallback
        };
    }

    private static Dictionary<string, object?> GetDictArg(RunnerMessage msg, int index)
    {
        if (msg.Args.Count <= index) return new Dictionary<string, object?>();
        object? obj = FromJsonElement(msg.Args[index]);
        return obj as Dictionary<string, object?> ?? new Dictionary<string, object?>();
    }

    private static List<object?> GetListArg(RunnerMessage msg, int index)
    {
        if (msg.Args.Count <= index) return new List<object?>();
        object? obj = FromJsonElement(msg.Args[index]);
        return obj as List<object?> ?? new List<object?>();
    }

    private static object? FromJsonElement(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (e.TryGetInt64(out long l)) return l;
                if (e.TryGetDecimal(out decimal d)) return d;
                return e.GetDouble();
            case JsonValueKind.String:
                return e.GetString();
            case JsonValueKind.Array:
                return e.EnumerateArray().Select(FromJsonElement).ToList();
            case JsonValueKind.Object:
                return e.EnumerateObject()
                    .ToDictionary(p => p.Name, p => FromJsonElement(p.Value), StringComparer.OrdinalIgnoreCase);
            default:
                return e.ToString();
        }
    }

    private static string ResolvePythonExe(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
        string? env = Environment.GetEnvironmentVariable("BROSLMV_PYTHON_EXE");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        foreach (string candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "runtimes", "python", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "runtimes", "python", "python.exe"),
            @"C:\BrosLMV\runtimes\python\python.exe",
        })
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        // Solo para desarrollo local. En produccion C4 instala CPython embeddable.
        return "python";
    }

    private static string ResolveRunnerPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;

        string outputPath = Path.Combine(AppContext.BaseDirectory, "workers", "python", "runner.py");
        if (IsUsableRunner(outputPath)) return outputPath;

        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.Combine(dir, "workers", "python", "runner.py");
            if (IsUsableRunner(candidate)) return candidate;
            DirectoryInfo? parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        throw new FileNotFoundException("No se encontro workers/python/runner.py.", outputPath);
    }

    private static bool IsUsableRunner(string path)
    {
        if (!File.Exists(path)) return false;
        return Directory.Exists(Path.Combine(Path.GetDirectoryName(path) ?? "", "broslmv"));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private sealed class RunnerRequest
    {
        public string Code { get; set; } = "";
        public bool CodeIsPath { get; set; }
        public Dictionary<string, object?> Context { get; set; } = new();
    }

    private sealed class RunnerResponse
    {
        public bool Ok { get; set; }
        public string? ReturnValue { get; set; }
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public int RowsAffected { get; set; }
        public long ElapsedMs { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Traceback { get; set; }
    }

    private sealed class RunnerContextResponse
    {
        public string RequestId { get; set; } = "";
        public bool Ok { get; set; }
        public object? Value { get; set; }
        public string Error { get; set; } = "";
    }

    private sealed class RunnerMessage
    {
        public string Type { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string Method { get; set; } = "";
        public List<JsonElement> Args { get; set; } = new();
        public Dictionary<string, JsonElement> Kwargs { get; set; } = new();
        public bool? Ok { get; set; }
        public string? ReturnValue { get; set; }
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public int RowsAffected { get; set; }
        public long ElapsedMs { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Traceback { get; set; }

        public RunnerResponse ToRunnerResponse() => new()
        {
            Ok = Ok ?? false,
            ReturnValue = ReturnValue,
            Stdout = Stdout,
            Stderr = Stderr,
            RowsAffected = RowsAffected,
            ElapsedMs = ElapsedMs,
            ErrorCode = ErrorCode,
            ErrorMessage = ErrorMessage,
            Traceback = Traceback
        };
    }
}

public sealed record PythonExecutionResult(
    bool Success,
    string ReturnValue,
    string Stdout,
    int RowsAffected,
    long PythonElapsedMs,
    long HostElapsedMs,
    string ErrorCode,
    string ErrorMessage,
    string ErrorDetail)
{
    public static PythonExecutionResult Ok(string returnValue, string stdout, int rowsAffected, long pythonElapsedMs, long hostElapsedMs) =>
        new(true, returnValue, stdout, rowsAffected, pythonElapsedMs, hostElapsedMs, "", "", "");

    public static PythonExecutionResult Fail(string code, string message, string detail, long hostElapsedMs) =>
        new(false, "", "", 0, 0, hostElapsedMs, code, message, detail);
}
