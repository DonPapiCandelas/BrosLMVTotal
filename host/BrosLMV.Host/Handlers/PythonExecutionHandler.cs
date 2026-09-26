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

// PythonExecutionHandler.cs - handler real de ExecuteScript para C3d.

using BrosLMV.Host.Audit;
using BrosLMV.Host.Callbacks;
using BrosLMV.Host.Workers;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Handlers;

public sealed class PythonExecutionHandler : IExecutionHandler
{
    private readonly PythonProcess? _python;       // si se inyecta, se usa tal cual (tests)
    private readonly IExecutionAuditSink _audit;
    private readonly IHostCallbackSink _callbacks;

    public PythonExecutionHandler(
        PythonProcess? python = null,
        IExecutionAuditSink? audit = null,
        IHostCallbackSink? callbacks = null)
    {
        _python = python;
        _callbacks = callbacks ?? new LoggingHostCallbackSink();
        _audit = audit ?? new FileExecutionAuditSink();
    }

    public async Task<Envelope> ExecuteAsync(Envelope request, IAddonChannel addon, CancellationToken ct)
    {
        if (request.ExecuteScript is null)
            return Fail(request, "BAD_REQUEST", "El mensaje no contiene ExecuteScript.", "");

        // El SQL se reenvía al addon (conexión viva, C6c) cuando hay addon conectado;
        // si se inyectó un PythonProcess (tests), se respeta ese.
        // Los callbacks de UI (ctx.msg) se relayan al addon vía RelayingCallbackSink (v2.18.0+).
        PythonProcess python = _python ?? new PythonProcess(
            gateway: new PipeRelayGateway(addon),
            callbacks: new RelayingCallbackSink(addon, _callbacks));

        PythonExecutionResult result =
            await python.ExecuteAsync(request.ExecuteScript, ct, request.ExecutionId).ConfigureAwait(false);

        Audit(request, result);

        if (!result.Success)
            return Fail(request, result.ErrorCode, result.ErrorMessage, result.ErrorDetail, result.HostElapsedMs);

        string value = string.IsNullOrEmpty(result.ReturnValue) ? result.Stdout : result.ReturnValue;
        return new Envelope
        {
            ProtocolVersion = MessageRouter.ProtocolVersion,
            RequestId = request.RequestId,
            ExecutionId = request.ExecutionId,
            ExecutionCompleted = new ExecutionCompleted
            {
                ElapsedMs = result.HostElapsedMs,
                RowsAffected = result.RowsAffected,
                ReturnValue = new Value { StringValue = value ?? "" }
            }
        };
    }

    private void Audit(Envelope req, PythonExecutionResult result)
    {
        var ctx = req.ExecuteScript?.Context;
        _audit.Record(new ExecutionAuditEntry(
            ExecutionId:  req.ExecutionId,
            AppKey:       ctx?.AppKey ?? "",
            Empresa:      ctx?.Empresa ?? "",
            UserId:       ctx?.UserId ?? 0,
            ModuleId:     ctx?.ModuleId ?? 0,
            Language:     ctx?.Language ?? "python",
            Status:       result.Success ? "OK" : "ERROR",
            ElapsedMs:    result.HostElapsedMs,
            RowsAffected: result.RowsAffected,
            ErrorCode:    result.Success ? null : result.ErrorCode,
            ErrorMessage: result.Success ? null : result.ErrorMessage,
            TimestampUtc: DateTimeOffset.UtcNow));
    }

    private static Envelope Fail(Envelope req, string code, string message, string detail, long elapsedMs = 0) => new()
    {
        ProtocolVersion = MessageRouter.ProtocolVersion,
        RequestId = req.RequestId,
        ExecutionId = req.ExecutionId,
        ExecutionFailed = new ExecutionFailed
        {
            ElapsedMs = elapsedMs,
            Error = new Error { Code = code, Message = message, Detail = detail }
        }
    };
}
