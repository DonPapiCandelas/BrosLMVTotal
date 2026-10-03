// Piezas de apoyo del Diseñador: un «ctx» mínimo con la misma forma que el de los scripts (Query / Scalar / NonQuery / OpenConn / erp),
// para que el motor de etiquetas y las acciones del puente (build/disenador_formatos/motor.cs y acciones.cs) corran SIN cambios
// tanto dentro de Configuración de formato como aquí, en un programa aparte.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Disenador
{
    sealed class ErpShim { public int OwnedBusinessEntityId; public int UserId; }

    sealed class CtxShim
    {
        public string Cs;
        public ErpShim erp = new ErpShim();

        public SqlConnection OpenConn() { var c = new SqlConnection(Cs); c.Open(); return c; }

        public List<Dictionary<string, object>> Query(string sql)
        {
            using (var cn = OpenConn())
            using (var cm = cn.CreateCommand())
            {
                cm.CommandText = sql; cm.CommandTimeout = 60;
                using (var r = cm.ExecuteReader())
                {
                    var lista = new List<Dictionary<string, object>>();
                    while (r.Read())
                    {
                        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.GetValue(i);
                        lista.Add(d);
                    }
                    return lista;
                }
            }
        }

        public object Scalar(string sql)
        {
            using (var cn = OpenConn())
            using (var cm = cn.CreateCommand()) { cm.CommandText = sql; cm.CommandTimeout = 60; return cm.ExecuteScalar(); }
        }

        public int NonQuery(string sql)
        {
            using (var cn = OpenConn())
            using (var cm = cn.CreateCommand()) { cm.CommandText = sql; cm.CommandTimeout = 60; return cm.ExecuteNonQuery(); }
        }
    }

    // JSON <-> objetos simples (Dictionary<string, object>, List<object>, long, double, string, bool): lo que esperan las acciones del puente
    static class Json
    {
        public static object ToObj(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object: { var d = new Dictionary<string, object>(); foreach (var p in e.EnumerateObject()) d[p.Name] = ToObj(p.Value); return d; }
                case JsonValueKind.Array: return e.EnumerateArray().Select(ToObj).ToList();
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number: return e.TryGetInt64(out var l) ? (object)l : e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
        public static Dictionary<string, object> Parse(string json)
        {
            using (var doc = JsonDocument.Parse(json)) return ToObj(doc.RootElement) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        public static string Serialize(object o) => JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
