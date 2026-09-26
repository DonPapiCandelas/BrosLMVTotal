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

// Recetas.cs -- T3.1 fase 2: motor de recetas no-code (docs/RECETAS_NOCODE.md).
//
// Un boton "receta" no trae codigo -- trae un marcador de lenguaje ("# lang: receta",
// igual que "# lang: python"/"-- lang: sql") seguido de JSON: {"receta":"<id>",
// "config":{...}}. RecetasRegistro.Ejecutar() detecta el id, encuentra la IReceta
// registrada y le pasa el config ya parseado -- la receta no ve el marcador ni el JSON
// crudo, solo su propio diccionario de configuracion.
//
// Primera receta real: "Ejecutar SQL con tokens" (sql_tokens) -- delega a
// ctx.EjecutarSql(sql), el MISMO camino que ya usan los botones tipo 'sql' (resuelve
// {pID}/{pIDs}/{pUserID}/{pModulo}/{pEmpresa}/{DATOS:campo} y respeta SoloLectura). No
// reescribe nada nuevo: la receta es una capa de configuracion sobre un mecanismo que ya
// existia y ya estaba probado.

using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace BrosLMV
{
    public class CampoReceta
    {
        public string Nombre;      // clave en el JSON de config, p. ej. "sql"
        public string Etiqueta;    // texto visible, p. ej. "SQL (con tokens opcionales)"
        public string Tipo;        // "texto" | "numero" | "texto_multilinea"
        public bool PermiteTokens; // si true, el campo muestra el boton "Insertar token" (fase 1)
        public bool Requerido;
    }

    // Contrato de una receta: que le pregunta al usuario al crear el boton (EsquemaConfig,
    // usado por el futuro asistente de la Consola, fase 6) y que hace al ejecutarse.
    public interface IReceta
    {
        string Id { get; }       // identificador estable, va en el JSON guardado
        string Nombre { get; }   // nombre visible en el asistente
        // Explicacion corta (1-2 frases) de que hace la receta -- se muestra en el
        // asistente cuando el usuario la elige, para que sepa que va a pasar sin tener
        // que adivinar por los nombres de los campos.
        string Descripcion { get; }
        // Valores de ejemplo YA VALIDOS para cada campo de EsquemaConfig (mismas claves).
        // El asistente los usa para el boton "Llenar con ejemplo" -- la forma mas rapida
        // de entender que se espera en cada campo sin leer documentacion aparte.
        Dictionary<string, string> Ejemplo { get; }
        List<CampoReceta> EsquemaConfig { get; } // campos a pedir en el wizard
        // Ejecuta con el config ya parseado (JSON -> Dictionary). Debe seguir la misma
        // convencion que ctx.EjecutarSql/ScriptRunner.Ejecutar: "" o texto normal = OK,
        // "ERROR: ..." = fallo (nunca lanza hacia el llamador).
        string Ejecutar(Dictionary<string, object> config, ScriptContext ctx);
    }

    // Receta #1: ejecuta SQL crudo con tokens, exactamente como un boton tipo 'sql' -- la
    // receta mas simple posible, pensada para probar el flujo completo de punta a punta
    // (JSON guardado -> deteccion -> registro -> ejecucion -> auditoria) antes de construir
    // la receta estrella (crear documento a partir de otro, fase 4).
    public class RecetaSqlTokens : IReceta
    {
        public string Id => "sql_tokens";
        public string Nombre => "Ejecutar SQL con tokens";
        public string Descripcion =>
            "Corre una consulta SQL cuando se hace clic en el botón. Puedes usar tokens " +
            "como {pID} (el documento seleccionado) para que la consulta cambie según qué " +
            "esté seleccionado en Comercial. Útil para reportes rápidos o actualizaciones " +
            "puntuales sin escribir C#/Python.";
        public Dictionary<string, string> Ejemplo => new Dictionary<string, string> {
            { "sql", "SELECT Folio, Total FROM docDocument WHERE DocumentID = {pID}" }
        };
        public List<CampoReceta> EsquemaConfig => new List<CampoReceta> {
            new CampoReceta { Nombre="sql", Etiqueta="SQL (con tokens opcionales)", Tipo="texto_multilinea", PermiteTokens=true, Requerido=true }
        };

        public string Ejecutar(Dictionary<string, object> config, ScriptContext ctx)
        {
            if (config == null || !config.ContainsKey("sql") || config["sql"] == null)
                return "ERROR: la receta \"" + Nombre + "\" requiere config.sql (SQL con tokens opcionales).";
            string sql = Convert.ToString(config["sql"]);
            if (string.IsNullOrWhiteSpace(sql))
                return "ERROR: config.sql viene vacio.";
            try { return ctx.EjecutarSql(sql); }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }
    }

    public class RecetaCrearDocumentoDesdeOtro : IReceta
    {
        public string Id => "crear_documento_desde_otro";
        public string Nombre => "Crear documento a partir de otro";
        public string Descripcion =>
            "Crea un documento nuevo (por ejemplo una Orden de Compra) con encabezado y " +
            "partidas, usando el mismo motor que usa Comercial (calcula impuestos, afecta " +
            "inventario si aplica, etc.). Necesitas saber el ID del módulo destino (183 = " +
            "Orden de compra), el ID del almacén y el ID del proveedor/cliente -- consíguelos " +
            "con una consulta SQL si no los sabes de memoria. El módulo debe tener una " +
            "\"estructura\" registrada en src/EstructurasDocumento.cs (hoy: 183 y 202); si " +
            "usas otro módulo, la receta te lo va a decir con un error claro, no va a fallar " +
            "en silencio.";
        public Dictionary<string, string> Ejemplo => new Dictionary<string, string> {
            { "moduloDestino", "183" },
            { "depotId", "1" },
            { "businessEntityId", "2" },
            { "partidas", "[{\"productId\": 1, \"cantidad\": 5, \"precio\": 250, \"costo\": 200}]" }
        };
        public List<CampoReceta> EsquemaConfig => new List<CampoReceta> {
            new CampoReceta { Nombre="moduloDestino", Etiqueta="ID del Módulo Destino", Tipo="numero", Requerido=true },
            new CampoReceta { Nombre="depotId", Etiqueta="ID del Almacén", Tipo="numero", Requerido=true },
            new CampoReceta { Nombre="businessEntityId", Etiqueta="ID de la Entidad (Proveedor/Cliente)", Tipo="texto", PermiteTokens=true, Requerido=true },
            new CampoReceta { Nombre="partidas", Etiqueta="Partidas (JSON, ej: [{\"productId\":1,\"cantidad\":5}])", Tipo="texto_multilinea", Requerido=true }
        };

        public string Ejecutar(Dictionary<string, object> config, ScriptContext ctx)
        {
            if (config == null) return "ERROR: falta config.";
            if (!config.ContainsKey("moduloDestino"))
                return "ERROR: config.moduloDestino es requerido.";

            int moduloDestino = Convert.ToInt32(config["moduloDestino"]);
            var estructura = EstructurasRegistro.Buscar(moduloDestino);
            if (estructura == null)
                return "ERROR: no hay EstructuraDocumento registrada para ModuleID=" + moduloDestino +
                       " (agrégala en src/EstructurasDocumento.cs antes de usar este módulo).";

            int depotId = config.ContainsKey("depotId") ? Convert.ToInt32(config["depotId"]) : 1;
            int businessEntityId = config.ContainsKey("businessEntityId") ? Convert.ToInt32(config["businessEntityId"]) : 0;

            // "partidas" llega de dos formas validas: ya como lista (JSON anidado, p. ej. el
            // arnes de humo la manda asi) o como STRING con JSON adentro (el asistente
            // "Nueva acción" la pide en un textbox de texto libre porque no hay un grid
            // editable todavia -- ver docs/RECETAS_NOCODE.md 2.4). Sin este segundo camino,
            // toda receta guardada desde el asistente fallaba con "config.partidas debe ser
            // una lista" -- encontrado probando el JSON EXACTO que arma NuevaAccionForm
            // contra el sandbox, no en teoria.
            if (!config.ContainsKey("partidas"))
                return "ERROR: config.partidas es requerido.";
            object partidasRaw = config["partidas"];
            if (partidasRaw is string partidasTexto)
            {
                try { partidasRaw = new JavaScriptSerializer().Deserialize<object>(partidasTexto); }
                catch (Exception ex) { return "ERROR: config.partidas no es JSON valido -- " + ex.Message; }
            }
            if (!(partidasRaw is System.Collections.IEnumerable partidas) || partidasRaw is string)
                return "ERROR: config.partidas debe ser una lista con al menos 1 partida.";
            int totalPartidas = 0;
            foreach (var _ in partidas) totalPartidas++;
            if (totalPartidas == 0)
                return "ERROR: config.partidas debe ser una lista con al menos 1 partida.";

            try
            {
                int doc = ctx.erp.NuevoDocumento(moduloDestino, depotId, businessEntityId);

                string depotIdFromExpr = estructura.DepotIdFromEsDepotId ? "DepotID" : "0";
                string fechas = estructura.RequiereFechaEntrega
                    ? ", DateDelivery=GETDATE(), DateDocDelivery=GETDATE()" : "";
                ctx.NonQuery("UPDATE docDocument SET DepotIDFrom=" + depotIdFromExpr +
                              ", PaymentTermID=" + estructura.PaymentTermId + fechas +
                              " WHERE DocumentID=" + doc);

                foreach (Dictionary<string, object> p in partidas)
                {
                    int productId = Convert.ToInt32(p["productId"]);
                    double cantidad = Convert.ToDouble(p["cantidad"]);
                    double precio = p.ContainsKey("precio") ? Convert.ToDouble(p["precio"]) : -1;
                    double costo = p.ContainsKey("costo") ? Convert.ToDouble(p["costo"]) : -1;
                    ctx.erp.AgregarArticulo(doc, productId, cantidad, precio, costo);
                }

                if (estructura.TaxTypeIdPartida.HasValue)
                    ctx.NonQuery("UPDATE docDocumentItem SET TaxTypeID=" + estructura.TaxTypeIdPartida.Value +
                                  " WHERE DocumentID=" + doc + " AND DeletedOn IS NULL");

                ctx.erp.RecalcCompleto(doc);
                if (estructura.AfectaInventario) ctx.erp.AffectStockNEW(doc);
                ctx.erp.Save(doc);
                if (estructura.GeneraInfoPago) ctx.erp.UpdateDocumentPaidInfo(doc);

                return "Documento creado: doc=" + doc + " (" + estructura.Nombre + ")";
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }
    }

    public static class RecetasRegistro
    {
        private static readonly Dictionary<string, IReceta> Todas = new Dictionary<string, IReceta>(StringComparer.OrdinalIgnoreCase);

        static RecetasRegistro()
        {
            Registrar(new RecetaSqlTokens());
            Registrar(new RecetaCrearDocumentoDesdeOtro());
            // Nuevas recetas (fases futuras): Registrar(new RecetaXyz());
        }

        private static void Registrar(IReceta r) { Todas[r.Id] = r; }

        public static IEnumerable<IReceta> Listar() => Todas.Values;

        public static IReceta Buscar(string id)
        { return (!string.IsNullOrEmpty(id) && Todas.TryGetValue(id, out var r)) ? r : null; }

        // Quita el marcador de cabecera ("# lang: receta") y parsea el resto como JSON:
        // {"receta":"<id>","config":{...}}. Nunca lanza -- cualquier problema de formato
        // regresa "ERROR: ..." (misma convencion que EjecutarSql/ScriptRunner.Ejecutar), para
        // que el llamador (ClsMain.cs/runner) lo trate igual que cualquier otro fallo de
        // ejecucion, sin necesitar un catch aparte.
        public static string Ejecutar(string codigoConMarcador, ScriptContext ctx)
        {
            try
            {
                string json = QuitarMarcador(codigoConMarcador);
                var serializer = new JavaScriptSerializer();
                var raiz = serializer.Deserialize<Dictionary<string, object>>(json);

                if (raiz != null && raiz.ContainsKey("pasos") && raiz["pasos"] is System.Collections.ArrayList pasos)
                {
                    var resultados = new System.Text.StringBuilder();
                    for (int i = 0; i < pasos.Count; i++)
                    {
                        var pasoDict = pasos[i] as Dictionary<string, object>;
                        if (pasoDict == null) return "ERROR: paso " + (i + 1) + " tiene un formato invalido.";

                        string r = EjecutarUnaReceta(pasoDict, ctx);
                        if (r != null && r.StartsWith("ERROR"))
                            return "ERROR: paso " + (i + 1) + " de " + pasos.Count + " fallo -- " + r;
                        resultados.AppendLine("Paso " + (i + 1) + ": " + r);
                    }
                    return resultados.ToString().TrimEnd();
                }

                return EjecutarUnaReceta(raiz, ctx);
            }
            catch (Exception ex)
            {
                return "ERROR: JSON de receta invalido -- " + ex.Message;
            }
        }

        private static string EjecutarUnaReceta(Dictionary<string, object> raiz, ScriptContext ctx)
        {
            if (raiz == null || !raiz.ContainsKey("receta"))
                return "ERROR: el JSON no trae la clave \"receta\" (id).";
            string id = Convert.ToString(raiz["receta"]);
            var receta = Buscar(id);
            if (receta == null) return "ERROR: receta desconocida \"" + id + "\" (no esta en RecetasRegistro).";
            var config = raiz.ContainsKey("config") && raiz["config"] is Dictionary<string, object> d
                ? d : new Dictionary<string, object>();
            return receta.Ejecutar(config, ctx);
        }

        // Salta TODAS las lineas de cabecera (comentarios "#..." y vacias) antes del JSON, no
        // solo la del marcador de lenguaje -- una receta headless tambien trae
        // "# job: safe-offline" (T3.3) en otra linea de cabecera, y el orden entre ambas no
        // esta garantizado.
        private static string QuitarMarcador(string codigo)
        {
            var lineas = codigo.Split('\n');
            int desde = 0;
            for (int i = 0; i < lineas.Length; i++)
            {
                string t = lineas[i].Trim();
                if (t.Length == 0 || t.StartsWith("#")) { desde = i + 1; continue; }
                break;
            }
            return string.Join("\n", lineas, desde, lineas.Length - desde);
        }
    }
}
