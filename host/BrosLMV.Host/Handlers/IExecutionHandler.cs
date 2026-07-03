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

// IExecutionHandler.cs — el "seam" donde se ejecuta un script.
// En C3c hay un stub (EchoExecutionHandler) que NO lanza Python todavía; solo confirma
// que el ExecuteScript llegó y responde ExecutionCompleted. En C3d se sustituye por el
// handler real que arranca el worker de Python.

using BrosLMV.Host.Workers;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Handlers;

public interface IExecutionHandler
{
    // Recibe el Envelope con ExecuteScript y devuelve ExecutionCompleted o ExecutionFailed.
    // 'addon' permite callbacks al addon durante la ejecución (SQL en vivo, C6c).
    Task<Envelope> ExecuteAsync(Envelope request, IAddonChannel addon, CancellationToken ct);
}

// Stub de C3c: responde sin ejecutar Python (eco del AppKey recibido).
public sealed class EchoExecutionHandler : IExecutionHandler
{
    public Task<Envelope> ExecuteAsync(Envelope request, IAddonChannel addon, CancellationToken ct)
    {
        string appKey = request.ExecuteScript?.Context?.AppKey ?? "(desconocido)";
        var response = new Envelope
        {
            ProtocolVersion = 1,
            RequestId   = request.RequestId,
            ExecutionId = request.ExecutionId,
            ExecutionCompleted = new ExecutionCompleted
            {
                ElapsedMs = 0,
                RowsAffected = 0,
                ReturnValue = new Value { StringValue = $"(stub C3c) ExecuteScript recibido: {appKey}" }
            }
        };
        return Task.FromResult(response);
    }
}
