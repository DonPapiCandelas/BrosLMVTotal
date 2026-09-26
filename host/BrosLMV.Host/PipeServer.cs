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

// PipeServer.cs — servidor de Named Pipe del host (C3b).
// Seguridad en dos capas (decisión D6):
//   1) ACL del pipe: solo el usuario de Windows actual (ver PipeAcl).
//   2) token UUID por arranque: cada mensaje debe traer Envelope.auth_token válido;
//      si no, se rechaza el handshake.
// Por ahora atiende el handshake Hello -> HelloResponse. El enrutado completo de
// mensajes (ContextCall, UiRequest, ...) llega en C3c.

using System.IO.Pipes;
using BrosLMV.Host.Protocol;
using BrosLMV.Host.Security;
using BrosLMV.Host.Workers;
using BrosLMV.Protocol;

namespace BrosLMV.Host;

public sealed class PipeServer
{
    public const uint ProtocolVersion = 1;

    public string PipeName  { get; }
    public string AuthToken { get; }

    public PipeServer(string? pipeName = null, string? authToken = null)
    {
        PipeName  = pipeName  ?? $"BrosLMV.{Guid.NewGuid():N}";
        AuthToken = authToken ?? Guid.NewGuid().ToString();
    }

    // Crea el stream del servidor con la ACL del usuario actual.
    private NamedPipeServerStream CreateStream() =>
        NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 0, outBufferSize: 0, PipeAcl.CurrentUserOnly());

    // Espera UN cliente, hace SOLO el handshake y termina (usado por el test de C3b).
    public async Task ServeOneAsync(CancellationToken ct = default)
    {
        using var pipe = CreateStream();
        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
        await HandleHandshakeAsync(pipe, ct).ConfigureAwait(false);
        if (pipe.IsConnected) pipe.Disconnect();
    }

    // Espera UN cliente, hace el handshake y, si se acepta, atiende la SESIÓN completa
    // (bucle de mensajes enrutados) hasta que el cliente cierre o envíe CancelExecution.
    public async Task ServeSessionAsync(MessageRouter router, CancellationToken ct = default)
    {
        using var pipe = CreateStream();
        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

        bool accepted = await HandleHandshakeAsync(pipe, ct).ConfigureAwait(false);
        if (accepted)
        {
            while (!ct.IsCancellationRequested)
            {
                Envelope? msg = await FrameCodec.ReadAsync(pipe, ct).ConfigureAwait(false);
                if (msg is null) break; // el cliente cerró

                // Canal de callback al addon sobre ESTE pipe (SQL en vivo durante el script).
                var addon = new PipeAddonChannel(pipe);
                RouteResult result = await router.RouteAsync(msg, addon, ct).ConfigureAwait(false);
                if (result.Reply is not null)
                    await FrameCodec.WriteAsync(pipe, result.Reply, ct).ConfigureAwait(false);
                if (result.EndSession) break;
            }
        }
        if (pipe.IsConnected) pipe.Disconnect();
    }

    // Bucle de producción: atiende sesiones una a una hasta cancelación.
    public async Task ServeLoopAsync(MessageRouter router, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ServeSessionAsync(router, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[PipeServer] sesión falló: {ex.Message}"); }
        }
    }

    // Lee el primer mensaje (debe ser Hello con token válido) y responde HelloResponse.
    // Devuelve true si el handshake fue aceptado.
    private async Task<bool> HandleHandshakeAsync(Stream pipe, CancellationToken ct)
    {
        Envelope? req = await FrameCodec.ReadAsync(pipe, ct).ConfigureAwait(false);
        if (req is null) return false; // cliente cerró sin enviar nada

        bool tokenOk = CryptoEquals(req.AuthToken, AuthToken);
        bool isHello = req.MessageCase == Envelope.MessageOneofCase.Hello;
        bool accepted = tokenOk && isHello;

        var response = new Envelope
        {
            ProtocolVersion = ProtocolVersion,
            RequestId = req.RequestId,
            HelloResponse = new HelloResponse
            {
                ProtocolVersion = ProtocolVersion,
                Accepted = accepted,
            }
        };

        if (!accepted)
        {
            response.HelloResponse.Error = new Error
            {
                Code = tokenOk ? "BAD_HANDSHAKE" : "AUTH_DENIED",
                Message = tokenOk
                    ? "El primer mensaje debe ser Hello."
                    : "Token de pipe inválido."
            };
        }

        await FrameCodec.WriteAsync(pipe, response, ct).ConfigureAwait(false);
        return accepted;
    }

    // Comparación en tiempo constante para no filtrar el token por temporización.
    private static bool CryptoEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
