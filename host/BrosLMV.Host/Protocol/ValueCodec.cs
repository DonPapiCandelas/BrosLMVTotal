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

// ValueCodec.cs — conversión entre el tipo Protobuf `Value`/`Table` y objetos CLR.
// Lo usa el relay SQL (host <-> addon). Decimales como string exacto (no double).

using System.Globalization;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Protocol;

public static class ValueCodec
{
    public static Value ToValue(object? clr)
    {
        switch (clr)
        {
            case null: return new Value();                          // kind sin asignar = NULL
            case bool b: return new Value { BoolValue = b };
            case int i: return new Value { Int32Value = i };
            case long l: return new Value { Int64Value = l };
            case decimal m: return new Value { DecimalValue = m.ToString(CultureInfo.InvariantCulture) };
            case double d: return new Value { DoubleValue = d };
            case float f: return new Value { DoubleValue = f };
            case byte[] by: return new Value { BytesValue = Google.Protobuf.ByteString.CopyFrom(by) };
            case DateTime dt: return new Value { DatetimeValue = dt.ToString("O", CultureInfo.InvariantCulture) };
            case Guid g: return new Value { GuidValue = g.ToString("D") };
            case string s: return new Value { StringValue = s };
            default: return new Value { StringValue = System.Convert.ToString(clr, CultureInfo.InvariantCulture) ?? "" };
        }
    }

    public static object? FromValue(Value? v)
    {
        if (v == null) return null;
        switch (v.KindCase)
        {
            case Value.KindOneofCase.None: return null;
            case Value.KindOneofCase.BoolValue: return v.BoolValue;
            case Value.KindOneofCase.Int32Value: return v.Int32Value;
            case Value.KindOneofCase.Int64Value: return v.Int64Value;
            case Value.KindOneofCase.DecimalValue: return v.DecimalValue;   // string exacto
            case Value.KindOneofCase.DoubleValue: return v.DoubleValue;
            case Value.KindOneofCase.StringValue: return v.StringValue;
            case Value.KindOneofCase.BytesValue: return v.BytesValue.ToByteArray();
            case Value.KindOneofCase.DateValue: return v.DateValue;
            case Value.KindOneofCase.TimeValue: return v.TimeValue;
            case Value.KindOneofCase.DatetimeValue: return v.DatetimeValue;
            case Value.KindOneofCase.DatetimeoffsetValue: return v.DatetimeoffsetValue;
            case Value.KindOneofCase.GuidValue: return v.GuidValue;
            default: return v.ToString();
        }
    }

    // Tabla -> lista de diccionarios (lo que espera el SDK Python para query()).
    public static List<Dictionary<string, object?>> FromTable(Table? t)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (t == null) return rows;
        foreach (Row r in t.Rows)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < t.Columns.Count && i < r.Cells.Count; i++)
                row[t.Columns[i].Name] = FromValue(r.Cells[i]);
            rows.Add(row);
        }
        return rows;
    }

    public static Table ToTable(List<Dictionary<string, object?>> rows)
    {
        var t = new Table();
        if (rows.Count == 0) return t;
        foreach (var col in rows[0].Keys) t.Columns.Add(new Column { Name = col });
        foreach (var row in rows)
        {
            var r = new Row();
            foreach (var col in t.Columns) r.Cells.Add(ToValue(row.TryGetValue(col.Name, out var val) ? val : null));
            t.Rows.Add(r);
        }
        return t;
    }
}
