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

// MessageRouter.cs — despacha cada Envelope entrante a su manejador según el oneof.
// Lado HOST del pipe addon<->host. Por ahora maneja ExecuteScript (vía IExecutionHandler),
// Heartbeat (eco) y CancelExecution (fin de sesión). Los demás tipos se rechazan con
// UNSUPPORTED. Los manejadores reales de SQL/ERP/UI se enchufan más adelante.

using BrosLMV.Host.Handlers;
using BrosLMV.Host.Workers;
using BrosLMV.Protocol;

namespace BrosLMV.Host;

// Resultado de enrutar un mensaje: una respuesta opcional y si la sesión debe terminar.
public readonly struct RouteResult
{
    public Envelope? Reply { get; }
    public bool EndSession { get; }
    private RouteResult(Envelope? reply, bool end) { Reply = reply; EndSession = end; }

    public static RouteResult ReplyWith(Envelope env) => new(env, false);
    public static RouteResult Ignore()                => new(null, false);
    public static RouteResult End(Envelope? final = null) => new(final, true);
}

public sealed class MessageRouter
{
    public const uint ProtocolVersion = 1;
    private readonly IExecutionHandler _exec;

    public MessageRouter(IExecutionHandler? exec = null) => _exec = exec ?? new EchoExecutionHandler();

    public async Task<RouteResult> RouteAsync(Envelope msg, IAddonChannel addon, CancellationToken ct)
    {
        switch (msg.MessageCase)
        {
            case Envelope.MessageOneofCase.ExecuteScript:
                return RouteResult.ReplyWith(await _exec.ExecuteAsync(msg, addon, ct).ConfigureAwait(false));

            case Envelope.MessageOneofCase.Heartbeat:
                return RouteResult.ReplyWith(new Envelope
                {
                    ProtocolVersion = ProtocolVersion,
                    RequestId = msg.RequestId,
                    Heartbeat = new Heartbeat { TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                });

            case Envelope.MessageOneofCase.CancelExecution:
                return RouteResult.End();

            default:
                return RouteResult.ReplyWith(Fail(msg, "UNSUPPORTED",
                    $"Tipo de mensaje no soportado en esta etapa: {msg.MessageCase}."));
        }
    }

    private static Envelope Fail(Envelope req, string code, string message) => new()
    {
        ProtocolVersion = ProtocolVersion,
        RequestId = req.RequestId,
        ExecutionId = req.ExecutionId,
        ExecutionFailed = new ExecutionFailed { Error = new Error { Code = code, Message = message } }
    };
}
