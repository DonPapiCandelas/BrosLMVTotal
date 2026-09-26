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

// Program.cs — punto de entrada de BrosLMV.Host.
//   (sin args)              -> prueba de humo del contrato (C3a).
//   --serve [--pipe N] [--token T]
//                           -> abre el Named Pipe seguro y atiende la sesion (C3b-C3d).
//                              Si no se pasan pipe/token, los genera e imprime (en
//                              producción el addon los genera y los pasa por args).

using Google.Protobuf;
using BrosLMV.Host;
using BrosLMV.Host.Handlers;
using BrosLMV.Protocol;

if (Array.IndexOf(args, "--serve") >= 0)
{
    string? pipe  = ArgValue(args, "--pipe");
    string? token = ArgValue(args, "--token");
    var server = new PipeServer(pipe, token);

    // Líneas que el addon puede leer del stdout para conectarse.
    Console.WriteLine($"PIPE={server.PipeName}");
    Console.WriteLine($"TOKEN={server.AuthToken}");
    Console.WriteLine("BrosLMV.Host escuchando (Ctrl+C para salir)...");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    var router = new MessageRouter(new PythonExecutionHandler());
    await server.ServeLoopAsync(router, cts.Token);
    return;
}

// --- Prueba de humo (C3a): confirma x64 + codegen del proto ---
Console.WriteLine("BrosLMV.Host — supervisor del canal C# <-> Python (v3.0)");
Console.WriteLine($"  .NET runtime : {Environment.Version}");
Console.WriteLine($"  Proceso x64  : {Environment.Is64BitProcess}");

var smoke = new Envelope
{
    ProtocolVersion = 1,
    RequestId = Guid.NewGuid().ToString(),
    Hello = new Hello { Runtime = "python", RuntimeVersion = "3.13", ProtocolVersion = 1 }
};
byte[] bytes = smoke.ToByteArray();
var roundTrip = Envelope.Parser.ParseFrom(bytes);
Console.WriteLine($"  Contrato     : Envelope OK ({bytes.Length} bytes, msg={roundTrip.MessageCase}).");
Console.WriteLine("Usa '--serve' para abrir el Named Pipe seguro (C3b).");

static string? ArgValue(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
}
