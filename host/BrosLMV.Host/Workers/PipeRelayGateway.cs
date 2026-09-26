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

// PipeRelayGateway.cs — gateway de ctx que NO conecta a SQL por su cuenta: reenvía cada
// ctx.query/scalar/execute al ADDON por el pipe (IAddonChannel), que lo corre en la
// conexión VIVA de CONTPAQi. Así el SQL "funciona tal cual", contra la empresa activa y
// sin credenciales en el host (C6c). El contexto/selección salen del propio ExecuteScript.

using BrosLMV.Host.Protocol;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Workers;

public sealed class PipeRelayGateway : IPythonContextGateway
{
    private readonly IAddonChannel _channel;
    private readonly DefaultPythonContextGateway _ctx = new();

    public PipeRelayGateway(IAddonChannel channel) => _channel = channel;

    public Dictionary<string, object?> Context(BrosLMV.Protocol.ExecutionContext? ctx) => _ctx.Context(ctx);
    public long[] GetSelectedIds(BrosLMV.Protocol.ExecutionContext? ctx) => _ctx.GetSelectedIds(ctx);

    public List<Dictionary<string, object?>> Query(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        ContextResponse r = _channel.Call(MakeCall(sql, parameters, SqlMode.SqlQuery));
        ThrowIfError(r);
        return ValueCodec.FromTable(r.ResultCase == ContextResponse.ResultOneofCase.Table ? r.Table : null);
    }

    public object? Scalar(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        ContextResponse r = _channel.Call(MakeCall(sql, parameters, SqlMode.SqlScalar));
        ThrowIfError(r);
        return ValueCodec.FromValue(r.ResultCase == ContextResponse.ResultOneofCase.Value ? r.Value : null);
    }

    public int Execute(BrosLMV.Protocol.ExecutionContext? ctx, string sql, Dictionary<string, object?> parameters)
    {
        ContextResponse r = _channel.Call(MakeCall(sql, parameters, SqlMode.SqlNonQuery));
        ThrowIfError(r);
        object? v = ValueCodec.FromValue(r.ResultCase == ContextResponse.ResultOneofCase.Value ? r.Value : null);
        return v == null ? 0 : Convert.ToInt32(v);
    }

    public object? Erp(BrosLMV.Protocol.ExecutionContext? ctx, string method, List<object?> args)
    {
        var erp = new ErpCall { Method = method ?? "" };
        if (args != null)
            foreach (var a in args)
                erp.Args.Add(ValueCodec.ToValue(a));
        ContextResponse r = _channel.Call(new ContextCall { Erp = erp });
        ThrowIfError(r);
        return ValueCodec.FromValue(r.ResultCase == ContextResponse.ResultOneofCase.Value ? r.Value : null);
    }

    private static ContextCall MakeCall(string sql, Dictionary<string, object?> parameters, SqlMode mode)
    {
        var live = new LiveSql { Sql = sql ?? "", Mode = mode };
        if (parameters != null)
            foreach (var kv in parameters)
                live.Params[kv.Key] = ValueCodec.ToValue(kv.Value);
        return new ContextCall { Live = live };
    }

    private static void ThrowIfError(ContextResponse r)
    {
        if (r.ResultCase == ContextResponse.ResultOneofCase.Error)
            throw new InvalidOperationException(
                (r.Error.Code ?? "SQL_ERROR") + ": " + (r.Error.Message ?? "Error de SQL en el addon."));
    }
}
