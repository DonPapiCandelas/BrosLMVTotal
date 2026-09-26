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

// FrameCodec.cs — framing del canal: [4 bytes longitud big-endian][Envelope Protobuf].
// Es el formato acordado en ARQUITECTURA_V3 §4. Se usa en ambos extremos del pipe.

using System.Buffers.Binary;
using Google.Protobuf;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Protocol;

public static class FrameCodec
{
    // Tope de seguridad para no asignar memoria arbitraria si llega una longitud absurda.
    public const int MaxFrameBytes = 64 * 1024 * 1024; // 64 MB

    // Escribe un Envelope enmarcado: 4 bytes de longitud (BE) + payload Protobuf.
    public static async Task WriteAsync(Stream stream, Envelope env, CancellationToken ct = default)
    {
        byte[] payload = env.ToByteArray();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // Lee un Envelope enmarcado. Devuelve null si el otro extremo cerró limpiamente (EOF).
    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[]? header = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
        if (header is null) return null; // EOF antes de empezar un frame -> cierre limpio

        int len = BinaryPrimitives.ReadInt32BigEndian(header);
        if (len < 0 || len > MaxFrameBytes)
            throw new InvalidDataException($"Longitud de frame inválida: {len} bytes.");

        byte[]? payload = await ReadExactAsync(stream, len, ct).ConfigureAwait(false);
        if (payload is null)
            throw new EndOfStreamException("El stream se cortó a mitad de un frame.");

        return Envelope.Parser.ParseFrom(payload);
    }

    // Lee EXACTAMENTE 'count' bytes (los streams pueden devolver lecturas parciales).
    // Devuelve null solo si hay EOF antes del primer byte (cierre limpio).
    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        if (count == 0) return Array.Empty<byte>();
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (n == 0)
                return offset == 0 ? null : throw new EndOfStreamException("EOF inesperado a mitad de frame.");
            offset += n;
        }
        return buffer;
    }
}
