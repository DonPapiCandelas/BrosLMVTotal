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

// IAddonChannel.cs — canal de callback del HOST hacia el ADDON sobre el MISMO pipe (C6c).
// Durante un ExecuteScript, el host puede pedirle al addon que ejecute SQL en la conexión
// VIVA de CONTPAQi (relay), sin credenciales propias. Es request/response estricto: el
// host escribe un ContextCall y lee el ContextResponse siguiente. Sólo se usa mientras la
// sesión está dentro de un ExecuteScript (no hay acceso concurrente al pipe).

using BrosLMV.Host.Protocol;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Workers;

public interface IAddonChannel
{
    ContextResponse Call(ContextCall call);

    // Envía una petición de UI al addon (ctx.msg/confirm desde Python → MessageBox en Comercial).
    // Bloques hasta que el addon responda (el usuario cierra el diálogo).
    UiResponse SendUi(UiRequest req);
}

// Canal real sobre el pipe addon<->host.
public sealed class PipeAddonChannel : IAddonChannel
{
    private readonly Stream _pipe;
    public PipeAddonChannel(Stream pipe) => _pipe = pipe;

    public ContextResponse Call(ContextCall call)
    {
        var env = new Envelope
        {
            ProtocolVersion = 1,
            RequestId = Guid.NewGuid().ToString(),
            ContextCall = call
        };
        FrameCodec.WriteAsync(_pipe, env).GetAwaiter().GetResult();
        Envelope? resp = FrameCodec.ReadAsync(_pipe).GetAwaiter().GetResult();
        if (resp == null || resp.MessageCase != Envelope.MessageOneofCase.ContextResponse)
            throw new IOException("El addon no devolvió un ContextResponse al callback de SQL.");
        return resp.ContextResponse;
    }

    public UiResponse SendUi(UiRequest req)
    {
        var env = new Envelope
        {
            ProtocolVersion = 1,
            RequestId = Guid.NewGuid().ToString(),
            UiRequest = req
        };
        FrameCodec.WriteAsync(_pipe, env).GetAwaiter().GetResult();
        Envelope? resp = FrameCodec.ReadAsync(_pipe).GetAwaiter().GetResult();
        if (resp == null || resp.MessageCase != Envelope.MessageOneofCase.UiResponse)
            throw new IOException("El addon no devolvió UiResponse al callback de UI.");
        return resp.UiResponse;
    }
}

// Canal nulo: cuando no hay addon (p. ej. el host corre suelto). El SQL en vivo no aplica.
public sealed class NullAddonChannel : IAddonChannel
{
    public ContextResponse Call(ContextCall call) => new ContextResponse
    {
        Error = new Error { Code = "NO_ADDON", Message = "No hay addon conectado para SQL en vivo." }
    };

    public UiResponse SendUi(UiRequest req) => new UiResponse
    {
        Error = new Error { Code = "NO_ADDON", Message = "No hay addon conectado para UI." }
    };
}
