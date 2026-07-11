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

// RelayingCallbackSink.cs — sink de callbacks que envía UiRequest al addon por el pipe (v2.18.0+).
// Cuando Python llama ctx.msg(texto), el host lo recibe y envía UiRequest al addon, que
// muestra un MessageBox real en el proceso de Comercial. ctx.log y ctx.progress se registran
// en el log del host (no viajan al addon).

using BrosLMV.Host.Workers;
using BrosLMV.Protocol;

namespace BrosLMV.Host.Callbacks;

public sealed class RelayingCallbackSink : IHostCallbackSink
{
    private readonly IAddonChannel _channel;
    private readonly IHostCallbackSink _logFallback;

    public RelayingCallbackSink(IAddonChannel channel, IHostCallbackSink? logFallback = null)
    {
        _channel = channel;
        _logFallback = logFallback ?? new LoggingHostCallbackSink();
    }

    public void Message(string executionId, string text, string title)
    {
        try
        {
            var req = new UiRequest { Msg = new UiMessage { Text = text ?? "", Title = title ?? "BrosLMV" } };
            UiResponse resp = _channel.SendUi(req);
            if (resp.Error != null)
                _logFallback.Message(executionId, "[UI_ERROR] " + text, resp.Error.Code + ": " + resp.Error.Message);
        }
        catch
        {
            // Si el relay falla (pipe roto, addon no conectado), caer al log.
            _logFallback.Message(executionId, text, title);
        }
    }

    public void Log(string executionId, string level, string text) =>
        _logFallback.Log(executionId, level, text);

    public void Progress(string executionId, string text, int percent) =>
        _logFallback.Progress(executionId, text, percent);

    public Dictionary<string, object?> Form(string executionId, Dictionary<string, object?> spec)
    {
        try
        {
            UiResponse resp = _channel.SendUi(new UiRequest { Form = ToUiForm(spec) });
            if (resp.Error != null)
                throw new InvalidOperationException(resp.Error.Code + ": " + resp.Error.Message);
            if (resp.FormResult == null)
                return new Dictionary<string, object?> { ["submitted"] = false };

            var values = new Dictionary<string, object?>();
            foreach (var kv in resp.FormResult.Values)
                values[kv.Key] = FromValue(kv.Value);

            return new Dictionary<string, object?>
            {
                ["submitted"] = resp.FormResult.Submitted,
                ["values"] = values
            };
        }
        catch (Exception ex)
        {
            _logFallback.Log(executionId, "ERROR", "FORM: " + ex.Message);
            return new Dictionary<string, object?> { ["submitted"] = false, ["error"] = ex.Message };
        }
    }

    public void ShowHtml(string executionId, string html, string title, int width, int height, bool modal)
    {
        try
        {
            UiResponse resp = _channel.SendUi(new UiRequest
            {
                ShowHtml = new UiShowHtml
                {
                    Html = html ?? "",
                    Title = title ?? "BrosLMV",
                    Width = width,
                    Height = height,
                    Modal = modal
                }
            });
            if (resp.Error != null)
                _logFallback.Log(executionId, "ERROR", "SHOW_HTML: " + resp.Error.Code + ": " + resp.Error.Message);
        }
        catch (Exception ex)
        {
            _logFallback.Log(executionId, "ERROR", "SHOW_HTML: " + ex.Message);
        }
    }

    private static UiForm ToUiForm(Dictionary<string, object?> spec)
    {
        var form = new UiForm
        {
            Title = S(spec, "title", "BrosLMV"),
            OkLabel = S(spec, "ok_label", "Aceptar"),
            CancelLabel = S(spec, "cancel_label", "Cancelar"),
            Width = I(spec, "width", 720),
            Height = I(spec, "height", 520)
        };

        if (spec.TryGetValue("fields", out var rawFields) && rawFields is List<object?> fields)
        {
            foreach (var raw in fields)
            {
                if (raw is not Dictionary<string, object?> f) continue;
                var field = new FormField
                {
                    Name = S(f, "name", ""),
                    Label = S(f, "label", S(f, "name", "")),
                    Type = FieldTypeOf(S(f, "type", "text")),
                    Required = B(f, "required", false),
                    ReadOnly = B(f, "read_only", false)
                };
                if (f.ContainsKey("default")) field.DefaultValue = ToValue(f["default"]);
                if (f.TryGetValue("options", out var rawOpts) && rawOpts is List<object?> opts)
                {
                    foreach (var rawOpt in opts)
                    {
                        if (rawOpt is Dictionary<string, object?> opt)
                        {
                            field.Options.Add(new ComboOption
                            {
                                Label = S(opt, "label", Convert.ToString(opt.GetValueOrDefault("value")) ?? ""),
                                Value = ToValue(opt.GetValueOrDefault("value"))
                            });
                        }
                        else
                        {
                            field.Options.Add(new ComboOption
                            {
                                Label = Convert.ToString(rawOpt) ?? "",
                                Value = ToValue(rawOpt)
                            });
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(field.Name))
                    form.Fields.Add(field);
            }
        }

        return form;
    }

    private static string S(Dictionary<string, object?> d, string k, string fallback) =>
        d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) ?? fallback : fallback;

    private static int I(Dictionary<string, object?> d, string k, int fallback) =>
        d.TryGetValue(k, out var v) && int.TryParse(Convert.ToString(v), out int i) ? i : fallback;

    private static bool B(Dictionary<string, object?> d, string k, bool fallback) =>
        d.TryGetValue(k, out var v) && bool.TryParse(Convert.ToString(v), out bool b) ? b : fallback;

    private static FieldType FieldTypeOf(string type) => (type ?? "").ToLowerInvariant() switch
    {
        "number" => FieldType.FtNumber,
        "int" => FieldType.FtNumber,
        "decimal" => FieldType.FtDecimal,
        "date" => FieldType.FtDate,
        "bool" => FieldType.FtBool,
        "checkbox" => FieldType.FtBool,
        "combo" => FieldType.FtCombo,
        "select" => FieldType.FtCombo,
        "memo" => FieldType.FtMemo,
        "textarea" => FieldType.FtMemo,
        _ => FieldType.FtText
    };

    private static Value ToValue(object? v)
    {
        if (v == null) return new Value();
        if (v is bool b) return new Value { BoolValue = b };
        if (v is int i) return new Value { Int32Value = i };
        if (v is long l) return new Value { Int64Value = l };
        if (v is decimal m) return new Value { DecimalValue = m.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (v is double d) return new Value { DoubleValue = d };
        if (v is float f) return new Value { DoubleValue = f };
        return new Value { StringValue = Convert.ToString(v) ?? "" };
    }

    private static object? FromValue(Value v) => v.KindCase switch
    {
        Value.KindOneofCase.None => null,
        Value.KindOneofCase.BoolValue => v.BoolValue,
        Value.KindOneofCase.Int32Value => v.Int32Value,
        Value.KindOneofCase.Int64Value => v.Int64Value,
        Value.KindOneofCase.DecimalValue => decimal.TryParse(v.DecimalValue, out var m) ? m : v.DecimalValue,
        Value.KindOneofCase.DoubleValue => v.DoubleValue,
        Value.KindOneofCase.StringValue => v.StringValue,
        Value.KindOneofCase.DateValue => v.DateValue,
        Value.KindOneofCase.DatetimeValue => v.DatetimeValue,
        _ => v.ToString()
    };
}
