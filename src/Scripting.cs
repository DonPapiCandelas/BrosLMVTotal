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

// Scripting.cs
// Motor Roslyn (compila/ejecuta C# en caliente) + contexto inyectado a los scripts (ctx)
// + lectura de la seleccion del grid + CONEXION AUTOMATICA reutilizando la de CONTPAQi.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace BrosLMV
{
    // =========================================================
    //   Helpers de late-binding sobre System.__ComObject
    // =========================================================
    // v2.33.3: el Execute() por COM SI corrio (posibles efectos ya aplicados, p.ej. un INSERT)
    // pero la conexion murio leyendo el resultado -- Query/NonQuery/Scalar lanzan ESTE tipo
    // especifico (en vez de Exception generico) para que NuevoDocumento/AgregarArticulo puedan
    // distinguirlo y hacer una RECUPERACION de solo lectura (buscar la fila que probablemente
    // ya se inserto) en vez de reintentar ciegamente el mismo INSERT (que la duplicaria).
    internal class SqlYaEjecutadoException : Exception
    {
        public SqlYaEjecutadoException(string msg) : base(msg) { }
    }

    internal static class Com
    {
        public static string LastError { get; internal set; }

        // Log de diagnostico de conexion (v2.33.2) -- separado del Log() del script (que solo
        // se llama en el camino de EXITO). Escribe SIEMPRE, con hora exacta, para poder
        // reconstruir en cual de las 2 vias (COM vs OpenConn independiente) fallo cada intento,
        // sin depender de que el usuario copie el texto del MessageBox.
        private static readonly object _diagLogLock = new object();
        public static void DiagLog(string msg)
        {
            try
            {
                lock (_diagLogLock)
                {
                    if (!Directory.Exists(Rutas.Logs)) Directory.CreateDirectory(Rutas.Logs);
                    File.AppendAllText(Path.Combine(Rutas.Logs, "Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt"),
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [v" + Version + "] " + msg + Environment.NewLine);
                }
            }
            catch { /* el log nunca debe tronar el flujo real */ }
        }

        // Version del ensamblado realmente cargado en memoria -- para confirmar en el log
        // (y en el mensaje de error) cual build esta corriendo de verdad, sin dudas sobre si
        // el registro COM/CodeBase esta apuntando a una version vieja.
        public static string Version
        {
            get
            {
                try { return typeof(Com).Assembly.GetName().Version.ToString(); }
                catch { return "?"; }
            }
        }

        public static object GetProp(object o, string name)
        {
            try { LastError = null; return o.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, o, null); }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }
        public static object GetIndexed(object o, string name, object arg)
        {
            try { LastError = null; return o.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance, null, o, new object[] { arg }); }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }
        public static bool SetProp(object o, string name, object val)
        {
            try { LastError = null; o.GetType().InvokeMember(name, BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance, null, o, new object[] { val }); return true; }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
        public static object Call(object o, string name, object[] args)
        {
            try { LastError = null; return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance, null, o, args); }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException : ex.InnerException;
                LastError = inner?.Message ?? ex.Message;
                return null;
            }
        }
        public static int  ToInt(object v)  { if (v == null || v is DBNull) return 0; try { return Convert.ToInt32(v); } catch { return 0; } }
        public static long ToLong(object v) { if (v == null || v is DBNull) return 0; try { return Convert.ToInt64(v); } catch { return 0; } }
    }

    // =========================================================
    //   CONEXION: reutiliza la conexion viva de CONTPAQi
    // =========================================================
    // No usamos archivo ni credenciales: tomamos la conexion ADO que CONTPAQi ya
    // tiene abierta a la empresa activa y la reutilizamos.
    public static class Conexion
    {
        // NOTA (probado y revertido en v2.21.4): se intentó cachear esta conexión de forma
        // estática (por identidad COM del XEngineLib) para evitar repetir el recorrido de
        // propiedades COM en cada clic -- ese recorrido puede tardar varios segundos (medido:
        // 6.6s en un caso real) porque tocar "janusGrid.ADORecordset" fuerza a CONTPAQi a
        // materializar el recordset del grid activo. La causa REAL del "busy" reportado resultó
        // ser otra (Consola.Ejecutar corría Python síncrono -- ver v2.21.4), y cachear la
        // conexión trajo un riesgo real: si una ejecución se interrumpe a medias (host matado,
        // excepción) con una transacción abierta en esa conexión compartida, TODAS las
        // ejecuciones siguientes de la sesión reutilizan esa misma conexión potencialmente
        // dañada -- se observó `NuevoDocumento` fallando ("no se pudo crear el encabezado")
        // con el mismo SQL que funciona perfecto ejecutado directo contra la base. Sin caché,
        // cada ejecución resuelve su propia conexión fresca: más lento en el peor caso, pero
        // nunca hereda el estado dañado de una ejecución anterior. No reintentar cachear esto
        // sin antes resolver el problema de raíz (transacciones huérfanas si el host muere a
        // medias de un batch con BEGIN/COMMIT).
        //
        // ORDEN DE PREFERENCIA (invertido en v2.21.6): antes se prefería la conexión ligada al
        // grid (janusGrid.ADORecordset.ActiveConnection) sobre el DataLayer. Se confirmó en vivo
        // que ESA conexión se puede CERRAR a medio uso: un botón de Orden de Compra (ventana
        // Python de larga duración, minutos abierta) fallaba con "La operación no está permitida
        // si el objeto está cerrado" al hacer Guardar -- las consultas de lectura del arranque
        // funcionaban bien (la conexión aún estaba viva en ese momento), pero el INSERT de
        // NuevoDocumento, minutos después, ya la encontraba cerrada (el grid se refrescó/cerró
        // mientras tanto). El MISMO script corrido desde la Consola sí funcionaba, porque ahí no
        // hay una lista de documentos enlazada y se cae directo al DataLayer, que no depende del
        // ciclo de vida de ningún grid. Por eso ahora se prueba el DataLayer PRIMERO.

        // Devuelve un objeto ADO Connection (abierto) de la empresa actual, o null.
        public static object ObtenerAdo(object xEngineLib)
        {
            if (xEngineLib == null) return null;

            // 1) DataLayer: disponible en CUALQUIER pestaña (incluida General) y no depende del
            //    ciclo de vida de ningún grid -- preferido para scripts que pueden tardar
            //    (ventanas interactivas, WinForms) porque no se cierra si el grid se refresca.
            object dl = Com.GetProp(xEngineLib, "DataLayer");
            if (PuedeEjecutar(dl)) return dl;
            foreach (var n in new[] { "Conexion", "Connection", "ADOConnection", "Cnn", "Conn" })
            {
                object c = Com.GetProp(dl, n);
                if (PuedeEjecutar(c)) return c;
            }

            // 2) Conexión enlazada al grid (respaldo si no hay DataLayer disponible).
            try
            {
                object jg = Com.GetProp(xEngineLib, "janusGrid");
                object rs = Com.GetProp(jg, "ADORecordset");
                object ac = Com.GetProp(rs, "ActiveConnection");
                if (PuedeEjecutar(ac)) return ac;
            }
            catch { }

            // 3) Ultimo respaldo.
            object dls = Com.GetProp(xEngineLib, "Datalayers");
            if (PuedeEjecutar(dls)) return dls;

            return null;
        }

        // Cadena de conexión OLEDB que expone el DataLayer de CONTPAQi (trae el
        // servidor y la base de la EMPRESA ACTIVA; normalmente SIN contraseña).
        public static string DataLayerConnString(object xEngineLib)
        {
            try
            {
                object dl = Com.GetProp(xEngineLib, "DataLayer");
                if (dl == null) return "";
                return Com.GetProp(dl, "ConnectionString") as string ?? "";
            }
            catch { return ""; }
        }

        // Una conexión es usable si puede ejecutar una consulta trivial (más fiable
        // que mirar .State, que en el DataLayer de CONTPAQi no es 1). Internal: también la usa
        // ScriptContext.Ado() para revalidar la conexión cacheada antes de reutilizarla.
        //
        // OJO (bug encontrado y corregido en v2.21.8): el recordset que devuelve este SELECT de
        // prueba NUNCA se cerraba. Mientras esto se llamaba una sola vez por ejecución no se
        // notaba, pero al volverse auto-sanador (v2.21.7, revalida en CADA ctx.query/ctx.erp) se
        // empezaron a acumular recordsets abiertos sin cerrar en la MISMA conexión -- y ADO tiene
        // un límite de resultados abiertos simultáneos por conexión (sin MARS). Eso es lo que
        // terminaba manifestándose como "la operación no está permitida si el objeto está
        // cerrado" en NuevoDocumento, más adelante en la misma ejecución. Hay que cerrar el
        // recordset de prueba inmediatamente.
        internal static bool PuedeEjecutar(object cn)
        {
            if (cn == null) return false;
            object rs = null;
            try { rs = Com.Call(cn, "Execute", new object[] { "SELECT 1 AS x" }); return rs != null; }
            catch { return false; }
            finally { if (rs != null) try { Com.Call(rs, "Close", new object[0]); } catch { } }
        }

        // Lee un ADO Recordset (devuelto por Connection.Execute) a lista de diccionarios.
        public static List<Dictionary<string, object>> Leer(object rs) { bool ok; return Leer(rs, out ok); }

        // v2.33.3: distingue "consulta legitimamente sin filas" (EOF=true limpio) de "el
        // recordset se cerro/murio a medio camino" (falla GetProp EOF/Fields/Value) -- antes
        // ambos devolvian una lista vacia IDENTICA, asi que Query/NonQuery/Scalar nunca se
        // enteraban de que debian caer al respaldo OpenConn() cuando Execute() SI devolvio un
        // recordset valido pero la conexion moria un instante despues, durante la lectura.
        // Confirmado en vivo (log Conexion_20260715.txt): Execute() no fallaba -- "La operación
        // no está permitida si el objeto está cerrado" aparecia AQUI, leyendo EOF, no en el
        // Execute -- por eso v2.33.1/v2.33.2 (que solo reintentaban/logueaban el Execute) no
        // alcanzaban a activar el fallback para este caso.
        public static List<Dictionary<string, object>> Leer(object rs, out bool ok)
        {
            var rows = new List<Dictionary<string, object>>();
            ok = true;
            if (rs == null) return rows;
            try
            {
                while (true)
                {
                    Com.LastError = null;
                    object eof = Com.GetProp(rs, "EOF");
                    if (eof == null && !string.IsNullOrEmpty(Com.LastError))
                        throw new Exception(Com.LastError); // GetProp fallo de verdad (recordset muerto)
                    if (!(eof is bool) || (bool)eof) break; // fin legitimo (EOF=true, sin error)
                    object fields = Com.GetProp(rs, "Fields");
                    int n = Com.ToInt(Com.GetProp(fields, "Count"));
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < n; i++)
                    {
                        object f = Com.GetIndexed(fields, "Item", i);
                        string name = Com.GetProp(f, "Name") as string;
                        if (string.IsNullOrEmpty(name)) name = "col" + i;
                        object val = Com.GetProp(f, "Value");
                        row[name] = (val is DBNull) ? null : val;
                    }
                    rows.Add(row);
                    Com.Call(rs, "MoveNext", new object[0]);
                }
            }
            catch (Exception ex)
            {
                ok = false;
                Com.DiagLog("Conexion.Leer: el recordset se cerro A MEDIO CAMINO (" + rows.Count + " fila(s) leidas antes de fallar): " + ex.Message);
            }
            try { Com.Call(rs, "Close", new object[0]); } catch { } // liberar recordset para no bloquear la conexion
            return rows;
        }
    }

    // =========================================================
    //   LECTURA DE LA SELECCION DEL GRID
    // =========================================================
    public static class GridSelection
    {
        public static List<long> GetSelectedIds(object xEngineLib)
        {
            var ids = new List<long>();
            try
            {
                if (xEngineLib == null) return ids;
                object jg = Com.GetProp(xEngineLib, "janusGrid");
                if (jg == null) return ids;
                object rs = Com.GetProp(jg, "ADORecordset");
                if (rs == null) return ids;
                object selItems = Com.GetProp(jg, "SelectedItems");
                int selCnt = Com.ToInt(Com.GetProp(selItems, "Count"));
                if (selCnt <= 0) return ids;

                string keyCol = LlaveDeModulo(xEngineLib, rs);   // columna llave del módulo activo

                object rc = Com.Call(rs, "Clone", new object[0]); // clon: no mueve el grid
                object rsUse = rc != null ? rc : rs;

                for (int i = 1; i <= selCnt; i++)
                {
                    object it = Com.GetIndexed(selItems, "Item", i);
                    if (it == null) continue;
                    if (Com.ToInt(Com.GetProp(it, "RowType")) != 0) continue;
                    int rowIndex = Com.ToInt(Com.GetProp(it, "RowIndex"));
                    if (rowIndex <= 0) continue;
                    long id = 0;
                    if (Com.SetProp(rsUse, "AbsolutePosition", rowIndex))
                    {
                        id = Com.ToLong(ReadField(rsUse, keyCol));
                        if (id == 0 && keyCol != "DocumentID") id = Com.ToLong(ReadField(rsUse, "DocumentID")); // respaldo
                    }
                    if (id > 0 && !ids.Contains(id)) ids.Add(id);
                }

                if (rc != null) { try { Com.Call(rc, "Close", new object[0]); } catch { } }
            }
            catch { }
            return ids;
        }

        // Columna llave primaria del módulo activo (Proveedores=SupplierID, Documentos=DocumentID,
        // Pagos/Cobros=FinancialOperationID...). Se obtiene de engModuleParameter.
        public static string LlaveDeModulo(object xEngineLib, object rs)
        {
            int modulo = Com.ToInt(Com.GetProp(xEngineLib, "ActiveModuleID"));
            try
            {
                object v = Com.Call(xEngineLib, "GetModuleParameter", new object[] { modulo, "PrimaryKey" });
                string k = v as string;
                if (!string.IsNullOrEmpty(k)) return k.Trim();
            }
            catch { }
            try
            {
                object cn = Com.GetProp(rs, "ActiveConnection");
                if (cn != null)
                {
                    var rows = Conexion.Leer(Com.Call(cn, "Execute", new object[] {
                        "SELECT Value FROM engModuleParameter WHERE ModuleID=" + modulo + " AND ParameterKey='PrimaryKey'" }));
                    if (rows.Count > 0)
                        foreach (var val in rows[0].Values) { string k = Convert.ToString(val); if (!string.IsNullOrEmpty(k)) return k.Trim(); }
                }
            }
            catch { }
            return "DocumentID";
        }

        private static object ReadField(object rs, string name)
        {
            object fields = Com.GetProp(rs, "Fields");
            if (fields == null) return null;
            object fld = Com.GetIndexed(fields, "Item", name);
            return fld != null ? Com.GetProp(fld, "Value") : null;
        }
    }

    // =========================================================
    //   CONTEXTO inyectado a los scripts (se usa como ctx.*)
    // =========================================================
    public class ScriptContext
    {
        public int    UserID      { get; private set; }
        public object XEngineLib  { get; private set; }
        public bool   SoloLectura    { get; set; }   // si true, NonQuery se bloquea
        public int    FilasAfectadas { get; private set; } // acumulado de NonQuery (para auditoria)

        // ID numerico que traiga el AppKey de un evento nativo de Comercial (p. ej. "Evento=Guardar"
        // con Funcion="BrosLMV.<Script>_[DocumentID]"). Null si el script se ejecuto normal (boton/consola).
        public long? EventoId { get; set; }

        private object _ado;
        private bool   _adoTried;

        public ScriptContext(int userId, object xEngineLib)
        {
            UserID = userId; XEngineLib = xEngineLib;
        }

        // --- Contexto de CONTPAQi (para el inspector de la consola) ---
        public int ModuloActivo() { return Com.ToInt(Com.GetProp(XEngineLib, "ActiveModuleID")); }

        private string _empresa;
        public string Empresa()
        {
            if (_empresa != null) return _empresa;
            _empresa = "";
            // Usar SOLO la conexión viva (nunca el respaldo SqlClient, que podría
            // tardar el timeout completo si la cadena de respaldo no es válida).
            try
            {
                object cn = Ado();
                if (cn != null)
                {
                    var rows = Conexion.Leer(Com.Call(cn, "Execute", new object[] { "SELECT DB_NAME() AS n" }));
                    if (rows.Count > 0 && rows[0].ContainsKey("n")) _empresa = Convert.ToString(rows[0]["n"]);
                }
            }
            catch { }
            // Respaldo: si la conexión viva no respondió, sacar la BD del DataLayer.
            if (string.IsNullOrEmpty(_empresa))
            {
                try
                {
                    string dl = Conexion.DataLayerConnString(XEngineLib);
                    string b = ParseVal(dl, "Initial Catalog");
                    if (string.IsNullOrEmpty(b)) b = ParseVal(dl, "Database");
                    if (!string.IsNullOrEmpty(b)) _empresa = b;
                }
                catch { }
            }
            return _empresa;
        }

        // Servidor (instancia SQL) de la empresa activa, leído del DataLayer de CONTPAQi.
        // Sirve para que el host (proceso aparte) sepa a qué servidor/BD conectarse.
        public string ServidorActivo()
        {
            try
            {
                string dl = Conexion.DataLayerConnString(XEngineLib);
                string s = ParseVal(dl, "Data Source");
                if (string.IsNullOrEmpty(s)) s = ParseVal(dl, "Server");
                return s ?? "";
            }
            catch { return ""; }
        }

        // --- Tipo de script 'sql': T-SQL crudo con tokens, corrido tal cual (B3) ---
        // Resuelve {pID}/{DATOS:...}, ejecuta por la conexión viva y devuelve texto:
        // las filas si es SELECT/SP, o un OK si es DML. Sin Roslyn ni Python.
        public string EjecutarSql(string sqlCrudo)
        {
            string sql = ResolverTokens(sqlCrudo);
            bool escribe = Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|DROP|TRUNCATE|ALTER|MERGE|EXEC|EXECUTE)\b",
                                         RegexOptions.IgnoreCase);
            if (escribe && SoloLectura)
                return "Modo SOLO LECTURA activo: el script SQL contiene operaciones de escritura.";

            var filas = Query(sql);
            if (filas.Count > 0) return FormatarFilas(filas, 500);
            return "SQL ejecutado correctamente. (sin filas de resultado)";
        }

        // Formatea filas como tabla de texto (encabezado + filas, con tope).
        private static string FormatarFilas(List<Dictionary<string, object>> filas, int max)
        {
            var sb = new StringBuilder();
            var cols = new List<string>(filas[0].Keys);
            sb.AppendLine(string.Join(" | ", cols));
            int n = Math.Min(filas.Count, max);
            for (int i = 0; i < n; i++)
            {
                var vals = new List<string>();
                foreach (var c in cols)
                {
                    object v = filas[i].ContainsKey(c) ? filas[i][c] : null;
                    vals.Add(v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture));
                }
                sb.AppendLine(string.Join(" | ", vals));
            }
            sb.AppendLine(filas.Count > max
                ? "... (" + filas.Count + " filas, mostrando " + max + ")"
                : "(" + filas.Count + " fila(s))");
            return sb.ToString();
        }

        // Conexion ADO viva de CONTPAQi (cacheada DURANTE esta ejecución, no entre ejecuciones).
        // Auto-sanadora (v2.21.7): una ventana WinForms interactiva (Python) puede quedar abierta
        // varios minutos; en ese lapso la conexión ligada al grid -o incluso el DataLayer- se
        // puede cerrar (confirmado en vivo: "La operación no está permitida si el objeto está
        // cerrado" al hacer Guardar en una Orden de Compra, con las consultas de arranque ya
        // hechas minutos antes). Por eso, si ya había una cacheada, se revalida con un SELECT 1
        // trivial antes de reusarla; si ya no sirve, se vuelve a resolver desde cero.
        //
        // forzarNueva (v2.33.0): el chequeo de arriba (PuedeEjecutar) tiene una ventana de
        // carrera -- la conexion puede pasar el SELECT 1 de prueba y morir de todos modos un
        // instante despues, antes del Execute real (mas probable mientras mas tiempo lleve
        // abierta la ventana). Query/NonQuery/Scalar usan este parametro para pedir una
        // reconexion 100% desde cero (ignorando la cacheada) como reintento UNICO cuando el
        // Execute real ya fallo, no solo el chequeo previo. Confirmado en vivo: "NuevoDocumento:
        // no se pudo crear el encabezado ... La operación no está permitida si el objeto está
        // cerrado" en una Orden de Compra dejada abierta varios minutos.
        private object Ado(bool forzarNueva = false)
        {
            if (!forzarNueva && _adoTried && _ado != null && Conexion.PuedeEjecutar(_ado)) return _ado;
            _adoTried = true;
            _ado = Conexion.ObtenerAdo(XEngineLib);
            Com.DiagLog("Ado(forzarNueva=" + forzarNueva + ") -> " + (_ado != null ? "conexion COM resuelta" : "NULL (ObtenerAdo no encontro ninguna conexion COM viva)"));
            return _ado;
        }

        // --- Seleccion del grid ---
        public List<long> GetSelectedIds() { return GridSelection.GetSelectedIds(XEngineLib); }

        // Ejecuta un Execute sobre la conexion viva con UN reintento de reconexion forzada
        // si el Execute REAL falla (no solo el chequeo previo de Ado()). Devuelve null si
        // AMBOS intentos fallan -- eso significa que el objeto COM de CONTPAQi esta
        // genuinamente atorado (no solo la referencia cacheada vencida): pedirle "otra"
        // conexion por el MISMO camino COM devuelve el mismo objeto roto (confirmado en
        // vivo: el reintento de v2.33.0 fallo identico). El llamador (Query/NonQuery/Scalar)
        // debe entonces caer a OpenConn() -- una SqlConnection propia e independiente que
        // NO pasa por el objeto COM de CONTPAQi en absoluto.
        private object EjecutarAdo(object cn, string sql)
        {
            object rs = Com.Call(cn, "Execute", new object[] { sql });
            if (rs == null && !string.IsNullOrEmpty(Com.LastError))
            {
                string err1 = Com.LastError;
                Com.DiagLog("EjecutarAdo: 1er intento (COM) fallo: " + err1 + " | SQL=" + Recorte(sql));
                object cn2 = Ado(forzarNueva: true);
                if (cn2 != null) rs = Com.Call(cn2, "Execute", new object[] { sql });
                if (rs == null) Com.DiagLog("EjecutarAdo: 2do intento (COM, forzarNueva) TAMBIEN fallo: " + Com.LastError);
                else Com.DiagLog("EjecutarAdo: 2do intento (COM, forzarNueva) OK.");
            }
            return rs;
        }

        private static string Recorte(string sql) { sql = sql ?? ""; return sql.Length > 200 ? sql.Substring(0, 200) + "..." : sql; }

        // --- SQL (usa la conexion viva de CONTPAQi; si esta atorada o no hay, cae a la
        // conexion propia independiente vía OpenConn()) ---
        public object Scalar(string sql)
        {
            object cn = Ado();
            string errCom = null;
            if (cn != null)
            {
                object rs = EjecutarAdo(cn, sql);
                if (rs != null)
                {
                    bool leidoOk;
                    var rows = Conexion.Leer(rs, out leidoOk);
                    if (leidoOk)
                    {
                        if (rows.Count > 0) foreach (var v in rows[0].Values) return v;
                        return null;
                    }
                    // Execute() por COM SI corrio (pudo tener efectos si el SQL escribe) -- NO se
                    // reintenta por OpenConn(), porque re-ejecutar el MISMO sql duplicaria esos
                    // efectos (p.ej. un INSERT). Se reporta como fallo distinto e irrecuperable.
                    Com.DiagLog("Scalar: Execute() por COM SI corrio pero el recordset se cerro leyendo el resultado -- NO se reintenta (evitar duplicar efectos). SQL=" + Recorte(sql));
                    throw new SqlYaEjecutadoException("Scalar: el SQL se ejecutó por COM pero la conexión murió leyendo el resultado; no se reintenta para no duplicar efectos si el SQL escribe. Revisa Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt.");
                }
                errCom = Com.LastError;
            }
            Com.DiagLog("Scalar: cayendo a OpenConn() (motivo COM: " + (errCom ?? "Ado() devolvio null") + ") | SQL=" + Recorte(sql));
            try
            {
                using (var c = OpenConn()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; var v = cmd.ExecuteScalar(); Com.DiagLog("Scalar: OpenConn() OK."); return v; }
            }
            catch (Exception exFb)
            {
                Com.DiagLog("Scalar: OpenConn() TAMBIEN fallo: " + exFb);
                throw new Exception("Scalar fallo por COM (" + (errCom ?? "sin conexion") + ") y por conexion independiente (" + exFb.Message + ").", exFb);
            }
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            object cn = Ado();
            string errCom = null;
            if (cn != null)
            {
                object rs = EjecutarAdo(cn, sql);
                if (rs != null)
                {
                    bool leidoOk;
                    var rowsCom = Conexion.Leer(rs, out leidoOk);
                    if (leidoOk) return rowsCom;
                    // Execute() por COM SI corrio -- NO se reintenta por OpenConn() (duplicaria
                    // efectos si el batch escribe, como los INSERT de NuevoDocumento/AgregarArticulo).
                    Com.DiagLog("Query: Execute() por COM SI corrio pero el recordset se cerro leyendo el resultado -- NO se reintenta (evitar duplicar efectos). SQL=" + Recorte(sql));
                    throw new SqlYaEjecutadoException("Query: el SQL se ejecutó por COM pero la conexión murió leyendo el resultado; no se reintenta para no duplicar efectos si el SQL escribe. Revisa Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt.");
                }
                errCom = Com.LastError;
            }

            Com.DiagLog("Query: cayendo a OpenConn() (motivo COM: " + (errCom ?? "Ado() devolvio null") + ") | SQL=" + Recorte(sql));
            try
            {
                var rows = new List<Dictionary<string, object>>();
                using (var c = OpenConn()) using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = sql;
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < r.FieldCount; i++)
                                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                            rows.Add(row);
                        }
                }
                Com.DiagLog("Query: OpenConn() OK, " + rows.Count + " fila(s).");
                return rows;
            }
            catch (Exception exFb)
            {
                Com.DiagLog("Query: OpenConn() TAMBIEN fallo: " + exFb);
                throw new Exception("Query fallo por COM (" + (errCom ?? "sin conexion") + ") y por conexion independiente (" + exFb.Message + ").", exFb);
            }
        }

        public int NonQuery(string sql)
        {
            if (SoloLectura)
                throw new Exception("Modo SOLO LECTURA activo: las operaciones de escritura (NonQuery) estan bloqueadas.");
            int n = 0;
            object cn = Ado();
            bool ejecutadoPorCom = false;
            string errCom = null;
            if (cn != null)
            {
                object rs = EjecutarAdo(cn, "SET NOCOUNT ON; " + sql + "; SELECT @@ROWCOUNT AS Afectadas");
                if (rs != null)
                {
                    bool leidoOk;
                    var rows = Conexion.Leer(rs, out leidoOk);
                    if (leidoOk)
                    {
                        if (rows.Count > 0 && rows[0].ContainsKey("Afectadas")) n = Com.ToInt(rows[0]["Afectadas"]);
                        ejecutadoPorCom = true;
                    }
                    else
                    {
                        // Execute() por COM SI corrio (el UPDATE/INSERT/DELETE ya se aplico) -- NO
                        // se reintenta por OpenConn() (duplicaria el efecto). Se reporta distinto.
                        Com.DiagLog("NonQuery: Execute() por COM SI corrio pero el recordset se cerro leyendo @@ROWCOUNT -- NO se reintenta (evitar duplicar efectos). SQL=" + Recorte(sql));
                        throw new SqlYaEjecutadoException("NonQuery: el SQL se ejecutó por COM pero la conexión murió leyendo el resultado; no se reintenta para no duplicar efectos. Revisa Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt.");
                    }
                }
                else errCom = Com.LastError;
            }
            if (!ejecutadoPorCom)
            {
                Com.DiagLog("NonQuery: cayendo a OpenConn() (motivo COM: " + (errCom ?? "Ado() devolvio null") + ") | SQL=" + Recorte(sql));
                try
                {
                    using (var c = OpenConn()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; n = cmd.ExecuteNonQuery(); }
                    Com.DiagLog("NonQuery: OpenConn() OK, " + n + " fila(s) afectada(s).");
                }
                catch (Exception exFb)
                {
                    Com.DiagLog("NonQuery: OpenConn() TAMBIEN fallo: " + exFb);
                    throw new Exception("NonQuery fallo por COM (" + (errCom ?? "sin conexion") + ") y por conexion independiente (" + exFb.Message + ").", exFb);
                }
            }
            FilasAfectadas += n;
            return n;
        }

        // Conexion SqlClient propia (uso avanzado). Resuelve la cadena automaticamente.
        public SqlConnection OpenConn()
        {
            string cs = ResolverCadena();
            if (string.IsNullOrEmpty(cs))
            {
                Com.DiagLog("OpenConn: ResolverCadena() vacio -- ni GetModuleConnectionString, ni credencial cifrada, ni " + Rutas.ConnFile + " utilizable.");
                throw new Exception("No hay conexion disponible (ni automatica de CONTPAQi, ni credencial cifrada " + Rutas.CredFile + ", ni " + Rutas.ConnFile + ").");
            }
            // Timeout corto: si la cadena de respaldo no fuera válida, falla rápido
            // (4s) en vez de colgar 15s.
            if (cs.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) < 0)
                cs = cs.TrimEnd(';') + ";Connect Timeout=4";
            Com.DiagLog("OpenConn: conectando con -> " + MaskPwd(cs));
            var c = new SqlConnection(cs); c.Open(); return c;
        }

        private static string MaskPwd(string cs)
        {
            return Regex.Replace(cs ?? "", "(Password=)[^;]*", "$1***", RegexOptions.IgnoreCase);
        }

        public string JoinIds(IEnumerable<long> ids) { return string.Join(",", ids); }

        // ===== Almacen de scripts en SQL (zzBrosScript), por empresa =====
        // Viven en la BD de la empresa activa, asi se comparten entre TODAS las
        // terminales de esa empresa. Escriben por la conexion viva, SIN la guarda de
        // solo-lectura (guardar un script es una operacion interna del editor).
        private static string SqlStr(string s) { return "N'" + (s ?? "").Replace("'", "''") + "'"; }

        private void BrosExec(string sql)
        {
            object cn = Ado();
            if (cn != null) { Com.Call(cn, "Execute", new object[] { "SET NOCOUNT ON; " + sql }); return; }
            using (var c = OpenConn()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        // True si la empresa activa esta provisionada (existe la tabla zzBrosScript).
        public bool BrosScriptsDisponible()
        {
            try { return Com.ToInt(Scalar("SELECT COUNT(*) FROM sys.tables WHERE name='zzBrosScript'")) > 0; }
            catch { return false; }
        }

        // Crea las tablas de scripts si faltan (idempotente). Requiere conexion viva.
        // Permite guardar un script aunque la empresa no haya sido provisionada todavia.
        public void BrosAsegurarTablas()
        {
            BrosExec(
                "IF OBJECT_ID('dbo.zzBrosScript') IS NULL CREATE TABLE dbo.zzBrosScript(" +
                "AppKey NVARCHAR(80) NOT NULL PRIMARY KEY, Nombre NVARCHAR(200) NULL, Codigo NVARCHAR(MAX) NULL, " +
                "Modulo INT NULL, Activo BIT NOT NULL DEFAULT 1, Modificado DATETIME NULL, ModificadoPor INT NULL); " +
                "IF OBJECT_ID('dbo.zzBrosScriptHist') IS NULL CREATE TABLE dbo.zzBrosScriptHist(" +
                "id INT IDENTITY(1,1) PRIMARY KEY, AppKey NVARCHAR(80) NULL, Codigo NVARCHAR(MAX) NULL, " +
                "Fecha DATETIME NULL, Usuario INT NULL);");
        }

        // Lista los scripts (AppKey, Nombre) de la empresa activa.
        public List<Dictionary<string, object>> BrosListar()
        {
            try { return Query("SELECT AppKey, Nombre FROM zzBrosScript WHERE Activo=1 ORDER BY AppKey"); }
            catch { return new List<Dictionary<string, object>>(); }
        }

        // Devuelve el codigo de un script, o null si no existe.
        //
        // v2.21.12: igual que BrosGuardar, se confirmó en vivo que el texto se guarda BIEN
        // (verificado con una conexión directa) pero se lee MAL desde la ventana que ejecuta el
        // botón (acentos aparecían como "?" o "♦") -- es decir, el angostamiento no es solo al
        // escribir, también pasa al leer por la conexión COM de CONTPAQi (`Query()` → `Ado()`).
        // Se prefiere leer por una conexión SqlClient directa; si no se puede abrir, cae al
        // camino de siempre (nunca debe fallar solo por no tener la vía directa disponible).
        public string BrosCargar(string appKey)
        {
            try
            {
                using (var c = OpenConn())
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT Codigo FROM zzBrosScript WHERE AppKey=@ak";
                    cmd.Parameters.Add("@ak", System.Data.SqlDbType.NVarChar, 80).Value = appKey ?? "";
                    object val = cmd.ExecuteScalar();
                    if (val != null) return Convert.ToString(val);
                    // No hubo fila: puede ser que el AppKey no exista, o que la conexion directa
                    // apunte a una empresa distinta a la activa en Comercial -- verificar por el
                    // camino de siempre antes de reportar "no existe".
                }
            }
            catch { /* sin conexión directa disponible: cae al camino de siempre abajo */ }

            var r = Query("SELECT Codigo FROM zzBrosScript WHERE AppKey=" + SqlStr(appKey));
            return r.Count > 0 ? Convert.ToString(r[0]["Codigo"]) : null;
        }

        // Inserta/actualiza un script (upsert) y respalda la version anterior en zzBrosScriptHist.
        //
        // v2.21.11: se confirmó en vivo que guardar por la conexión viva de CONTPAQi (el
        // Execute() COM del DataLayer/grid) puede angostar el texto a ANSI y guardar acentos y
        // emoji como "?" -- pasa con el texto grande de un script, no con SQL de negocio normal.
        // Por eso aquí se PREFIERE una conexión SqlClient directa y parametrizada (Unicode
        // perfecto, confirmado con NVARCHAR(MAX) + SqlParameter). Si no se puede abrir esa
        // conexión directa (ni por GetModuleConnectionString, ni integrada, ni
        // broslmv_conn.txt -- p. ej. una instalación con SQL Server que solo acepta login por
        // contraseña sin que esté guardada), se cae automáticamente al camino de siempre por
        // la conexión viva: guardar NUNCA debe fallar solo por no poder usar la vía directa.
        public void BrosGuardar(string appKey, string nombre, string codigo, int modulo)
        {
            try
            {
                using (var c = OpenConn())
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "INSERT zzBrosScriptHist(AppKey,Codigo,Fecha,Usuario) " +
                                          "SELECT AppKey,Codigo,GETDATE(),@uid FROM zzBrosScript WHERE AppKey=@ak";
                        cmd.Parameters.Add("@uid", System.Data.SqlDbType.Int).Value = UserID;
                        cmd.Parameters.Add("@ak", System.Data.SqlDbType.NVarChar, 80).Value = appKey ?? "";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText =
                            "IF EXISTS(SELECT 1 FROM zzBrosScript WHERE AppKey=@ak) " +
                            "UPDATE zzBrosScript SET Nombre=@nombre, Codigo=@codigo, Modulo=@modulo, Activo=1, " +
                            "Modificado=GETDATE(), ModificadoPor=@uid WHERE AppKey=@ak " +
                            "ELSE INSERT zzBrosScript(AppKey,Nombre,Codigo,Modulo,Activo,Modificado,ModificadoPor) " +
                            "VALUES(@ak,@nombre,@codigo,@modulo,1,GETDATE(),@uid)";
                        cmd.Parameters.Add("@ak", System.Data.SqlDbType.NVarChar, 80).Value = appKey ?? "";
                        cmd.Parameters.Add("@nombre", System.Data.SqlDbType.NVarChar, 200).Value = (object)nombre ?? DBNull.Value;
                        cmd.Parameters.Add("@codigo", System.Data.SqlDbType.NVarChar, -1).Value = (object)codigo ?? DBNull.Value;
                        cmd.Parameters.Add("@modulo", System.Data.SqlDbType.Int).Value = modulo;
                        cmd.Parameters.Add("@uid", System.Data.SqlDbType.Int).Value = UserID;
                        cmd.ExecuteNonQuery();
                    }
                }
                return;
            }
            catch { /* sin conexión directa disponible: cae al camino de siempre abajo */ }

            BrosExec("INSERT zzBrosScriptHist(AppKey,Codigo,Fecha,Usuario) " +
                     "SELECT AppKey,Codigo,GETDATE()," + UserID + " FROM zzBrosScript WHERE AppKey=" + SqlStr(appKey));
            BrosExec("IF EXISTS(SELECT 1 FROM zzBrosScript WHERE AppKey=" + SqlStr(appKey) + ") " +
                     "UPDATE zzBrosScript SET Nombre=" + SqlStr(nombre) + ", Codigo=" + SqlStr(codigo) +
                       ", Modulo=" + modulo + ", Activo=1, Modificado=GETDATE(), ModificadoPor=" + UserID +
                       " WHERE AppKey=" + SqlStr(appKey) + "; " +
                     "ELSE INSERT zzBrosScript(AppKey,Nombre,Codigo,Modulo,Activo,Modificado,ModificadoPor) " +
                       "VALUES(" + SqlStr(appKey) + "," + SqlStr(nombre) + "," + SqlStr(codigo) + "," + modulo + ",1,GETDATE()," + UserID + ")");
        }

        public void BrosBorrar(string appKey) { BrosExec("DELETE FROM zzBrosScript WHERE AppKey=" + SqlStr(appKey)); }

        // --- Diagnostico de conexion (para verificar tras instalar) ---
        public string DiagConexion()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ModuloActivo = " + ModuloActivo());
            object cn = Ado();
            sb.AppendLine("Conexion viva (grid/DataLayer): " + (cn != null ? "SI" : "NO"));
            if (cn != null) sb.AppendLine("   Base actual: " + Empresa());

            // Explorar DataLayer (debería ser independiente de la pestaña)
            try
            {
                object dl = Com.GetProp(XEngineLib, "DataLayer");
                sb.AppendLine("XEngineLib.DataLayer = " + (dl == null ? "null" : dl.GetType().Name));
                if (dl != null)
                {
                    sb.AppendLine("   .State = " + Com.GetProp(dl, "State"));
                    foreach (var n in new[] { "Conexion", "Connection", "ConnectionString" })
                    {
                        object v = Com.GetProp(dl, n);
                        if (v != null) { string s = v.ToString(); sb.AppendLine("   ." + n + " = " + (s.Length > 80 ? s.Substring(0, 80) : s)); }
                    }
                }
            }
            catch { }

            string cad = DesdeGetModuleConn();
            sb.AppendLine("GetModuleConnectionString = " + (string.IsNullOrEmpty(cad) ? "no" : "SI (" + cad.Length + " chars)"));
            bool hayCifrada = !string.IsNullOrEmpty(Rutas.LeerCredencial());
            sb.AppendLine("Credencial DPAPI (broslmv_cred.dat) = " + (hayCifrada ? "SI (cifrada)" : "no"));
            sb.AppendLine("Cadena de respaldo disponible = " + (string.IsNullOrEmpty(Rutas.ConnStr()) ? "no" : "SI"));
            return sb.ToString();
        }

        // --- UI / utilidades ---
        public void Msg(string texto, string titulo = "BrosLMV")
        { MessageBox.Show(texto, titulo, MessageBoxButtons.OK, MessageBoxIcon.Information); }

        public bool Confirm(string texto, string titulo = "Confirmar")
        { return MessageBox.Show(texto, titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        public void Log(string texto)
        {
            try
            {
                if (!Directory.Exists(Rutas.Logs)) Directory.CreateDirectory(Rutas.Logs);
                File.AppendAllText(Path.Combine(Rutas.Logs, "Script_" + DateTime.Now.ToString("yyyyMMdd") + ".txt"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + texto + Environment.NewLine);
            }
            catch { }
        }

        // ---- ctx.erp: wrapper tipado de XEngine + COM auxiliares ----
        private ErpContext _erp;
        public ErpContext erp
        {
            get { if (_erp == null) _erp = new ErpContext(XEngineLib, this); return _erp; }
        }

        // UserID real: el COM nos pasa UserID=0; el verdadero está en ctx.erp.UserId.
        // Lo usamos para poblar el contexto que viaja a Python (que no tiene ctx.erp).
        public int UserIdReal()
        {
            try { int u = erp.UserId; if (u > 0) return u; } catch { }
            return UserID;
        }

        // ---- ctx.ResolverTokens: sustituye {pID}, {pUserID}, {DATOS:campo} ----
        // Tokens disponibles:
        //   {pID}           -> primer ID seleccionado en el grid (o 0)
        //   {pIDs}          -> todos los IDs seleccionados, separados por coma
        //   {pUserID}       -> usuario activo
        //   {pModulo}       -> módulo activo (ActiveModuleID)
        //   {pEmpresa}      -> nombre de la BD activa
        //   {DATOS:Campo}   -> valor del campo Campo en la primera fila seleccionada del grid
        public string ResolverTokens(string template)
        {
            if (string.IsNullOrEmpty(template)) return template;
            return ResolverTokensCore(template, GetSelectedIds(), UserID,
                                      ModuloActivo(), Empresa(), GetFilaActiva());
        }

        // Núcleo PURO de sustitución de tokens (sin dependencias COM/CONTPAQi), para
        // poder probarlo offline. ResolverTokens() solo recolecta el contexto vivo y
        // delega aquí. Ver tests en /.temp_tests/test_tokens.ps1 (punto D1).
        //   ids    : IDs seleccionados (vacío -> {pID} y {pIDs} = "0").
        //   fila   : campos de la primera fila del grid (null -> {DATOS:x} sin resolver).
        public static string ResolverTokensCore(string template, IList<long> ids,
            int userId, int modulo, string empresa, IDictionary<string, object> fila)
        {
            if (string.IsNullOrEmpty(template)) return template;
            ids = ids ?? new List<long>();
            string primerID = ids.Count > 0 ? ids[0].ToString() : "0";
            string todosIDs = ids.Count > 0 ? string.Join(",", ids) : "0";

            var result = template
                .Replace("{pID}",     primerID)
                .Replace("{pIDs}",    todosIDs)
                .Replace("{pUserID}", userId.ToString())
                .Replace("{pModulo}", modulo.ToString())
                .Replace("{pEmpresa}", empresa ?? "");

            // {DATOS:Campo} -> campo de la primera fila seleccionada del grid
            result = Regex.Replace(result, @"\{DATOS:([^}]+)\}", m =>
            {
                string campo = m.Groups[1].Value.Trim();
                if (fila != null && fila.ContainsKey(campo))
                {
                    var v = fila[campo];
                    // Invariante: tokens destinados a SQL (un decimal NO debe volverse
                    // "1234,5" en un Windows en español y romper la consulta).
                    return v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
                }
                return m.Value; // token sin resolver -> lo deja igual
            });

            return result;
        }

        public Dictionary<string, object> GetFilaActiva()
        {
            try
            {
                object xe = XEngineLib;
                if (xe == null) return null;
                object jg = Com.GetProp(xe, "janusGrid");
                if (jg == null) return null;
                object rs = Com.GetProp(jg, "ADORecordset");
                if (rs == null) return null;

                object selItems = Com.GetProp(jg, "SelectedItems");
                int selCnt = Com.ToInt(Com.GetProp(selItems, "Count"));
                if (selCnt <= 0) return null;

                object item = Com.GetIndexed(selItems, "Item", 1);
                if (item == null) return null;
                int rowIndex = Com.ToInt(Com.GetProp(item, "RowIndex"));
                if (rowIndex <= 0) return null;

                object rc = Com.Call(rs, "Clone", new object[0]) ?? rs;
                if (!Com.SetProp(rc, "AbsolutePosition", rowIndex)) return null;

                object fields = Com.GetProp(rc, "Fields");
                int n = Com.ToInt(Com.GetProp(fields, "Count"));
                var fila = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < n; i++)
                {
                    object f = Com.GetIndexed(fields, "Item", i);
                    string name = Com.GetProp(f, "Name") as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        object v = Com.GetProp(f, "Value");
                        fila[name] = (v is DBNull) ? null : v;
                    }
                }
                try { if (!object.ReferenceEquals(rc, rs)) Com.Call(rc, "Close", new object[0]); } catch { }
                return fila;
            }
            catch { }
            return null;
        }

        // --- resolucion de cadena para SqlClient (GetModuleConnectionString -> archivo) ---
        private string ResolverCadena()
        {
            // a) Cadena por módulo (rara vez disponible, pero si está, es la ideal).
            string cad = DesdeGetModuleConn();
            if (!string.IsNullOrEmpty(cad)) return cad;

            // b) Combinar: SERVIDOR + BASE de la empresa activa (del DataLayer de
            //    CONTPAQi) con las CREDENCIALES guardadas en broslmv_conn.txt.
            string fileCs = Rutas.ConnStr();
            if (EsPlantilla(fileCs)) fileCs = "";   // la plantilla sin rellenar no sirve
            string dlCs = Conexion.DataLayerConnString(XEngineLib);

            string server = ParseVal(dlCs, "Data Source");
            string db     = ParseVal(dlCs, "Initial Catalog");
            if (server == "") { server = ParseVal(fileCs, "Server"); if (server == "") server = ParseVal(fileCs, "Data Source"); }
            if (db == "")     { db = ParseVal(fileCs, "Database");    if (db == "") db = ParseVal(fileCs, "Initial Catalog"); }
            string user = ParseVal(fileCs, "User Id"); if (user == "") user = ParseVal(fileCs, "User ID");
            string pwd  = ParseVal(fileCs, "Password");

            if (server != "" && db != "" && pwd != "")
                return "Server=" + server + ";Database=" + db + ";User Id=" + (user == "" ? "SA" : user) +
                       ";Password=" + pwd + ";TrustServerCertificate=True";

            // c) Sin contraseña guardada pero SÍ servidor+base (del DataLayer de CONTPAQi):
            //    probar autenticación integrada de Windows -- funciona cuando SQL Server está
            //    configurado así (confirmado en campo). La usa BrosGuardar como vía de
            //    escritura directa para el TEXTO del script (evita el angostamiento a ANSI
            //    del Execute() COM de CONTPAQi); si falla aquí, ese llamador ya tiene su
            //    propio respaldo por la conexión viva de siempre.
            if (server != "" && db != "")
                return "Server=" + server + ";Database=" + db + ";Integrated Security=True;TrustServerCertificate=True";

            // d) Último recurso: el archivo tal cual (si ya trae todo y no es plantilla).
            return string.IsNullOrEmpty(fileCs) ? "" : fileCs;
        }

        // Extrae el valor de una clave (key=value) de una cadena de conexión.
        private static string ParseVal(string cs, string key)
        {
            if (string.IsNullOrEmpty(cs)) return "";
            foreach (var part in cs.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i <= 0) continue;
                if (part.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(i + 1).Trim();
            }
            return "";
        }

        // True si broslmv_conn.txt es la plantilla sin rellenar (no una cadena real).
        private static bool EsPlantilla(string cs)
        {
            if (string.IsNullOrEmpty(cs)) return true;
            if (cs.TrimStart().StartsWith("#")) return true;   // aviso de migracion DPAPI (sin credenciales)
            return cs.IndexOf("SERVIDOR\\INSTANCIA", StringComparison.OrdinalIgnoreCase) >= 0
                || cs.IndexOf("TU_PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0
                || cs.IndexOf("NOMBRE_BD_DE_LA_EMPRESA", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string DesdeGetModuleConn()
        {
            // Probar con el módulo activo y, si no, con 0 (genérico).
            foreach (int mid in new[] { ModuloActivo(), 0 })
            {
                try
                {
                    object s = Com.Call(XEngineLib, "GetModuleConnectionString", new object[] { mid });
                    string cs = s as string;
                    if (!string.IsNullOrEmpty(cs) &&
                        (cs.IndexOf("Data Source", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         cs.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // Quita el prefijo OLEDB "Provider=...;" para que SqlClient lo acepte.
                        var partes = cs.Split(';')
                            .Where(p => p.Trim() != "" &&
                                        !p.TrimStart().StartsWith("Provider", StringComparison.OrdinalIgnoreCase));
                        return string.Join(";", partes);
                    }
                }
                catch { }
            }
            return "";
        }
    }

    // =========================================================
    //   ErpContext: wrapper tipado sobre XEngine y COM auxiliares
    //   Disponible en scripts como ctx.erp.*
    //   Diseñado para ser el mismo contrato que usará el proxy Python (Named Pipes).
    // =========================================================
    public class ErpContext
    {
        private readonly object _xe;
        // Dueño (ScriptContext) para correr SQL en la conexión viva desde los helpers de
        // documento (NuevoDocumento/AgregarArticulo). Puede ser null en usos sin SQL.
        private readonly ScriptContext _owner;

        internal ErpContext(object xe) : this(xe, null) { }
        internal ErpContext(object xe, ScriptContext owner) { _xe = xe; _owner = owner; }

        // ---- Propiedades de contexto (sin parámetros) ----
        public int    UserId                => Com.ToInt(Com.GetProp(_xe, "userID"));
        public string UserName              => Com.GetProp(_xe, "UserName") as string ?? "";
        public int    OwnedBusinessEntityId => Com.ToInt(Com.GetProp(_xe, "OwnedBusinessEntityID"));
        public int    ActiveModuleId        => Com.ToInt(Com.GetProp(_xe, "ActiveModuleID"));
        public int    CurrencyId            => Com.ToInt(Com.GetProp(_xe, "CurrencyID"));
        public string ComercialRFC          => Com.GetProp(_xe, "COMERCIAL_RFC") as string ?? "";
        public string SoftwareVersion       => Com.GetProp(_xe, "SoftwareVersion") as string ?? "";

        // Último error de late-binding COM (null si la última llamada fue exitosa).
        // Revisar después de AffectStockNEW, Save, CancelDocument, Delete, etc.
        public string LastError => Com.LastError;

        // ---- Operaciones de documento (XEngine directo) ----
        // Afecta inventario (kardex). Preferir AffectStockNEW en módulos nuevos.
        public void AffectStock(int documentId)            { Com.Call(_xe, "AffectStock",            new object[] { (long)documentId }); }
        public void AffectStockNEW(int documentId)         { Com.Call(_xe, "AffectStockNEW",         new object[] { (long)documentId }); }
        // Actualiza costos del documento (costo promedio, PEPS, etc.).
        public void CalcularCostos(int documentId)         { Com.Call(_xe, "CalcularCostos",         new object[] { (long)documentId }); }
        // Actualiza el estatus de entrega (icono del grid de remisiones/pedidos).
        public void UpdateStatusDelivery(int documentId)   { Com.Call(_xe, "UpdateStatusDelivery",   new object[] { (long)documentId }); }
        // Recalcula saldo pagado y balance del documento.
        public void UpdateDocumentPaidInfo(int documentId) { Com.Call(_xe, "UpdateDocumentPaidInfo", new object[] { (long)documentId }); }
        // Actualiza parcialidad en complementos de pago SAT.
        public void ActualizarParcialidad(int documentId)  { Com.Call(_xe, "ActualizarParcialidad",  new object[] { (long)documentId }); }
        public void CancelDocument(int documentId)         { Com.Call(_xe, "CancelDocument",         new object[] { (long)documentId }); }
        public void ReactivateDocument(int documentId)     { Com.Call(_xe, "ReactivateDocument",     new object[] { (long)documentId }); }
        public void Save(int documentId)
        {
            // XEngineLib.Save(id) no funciona (DISP_E_TYPEMISMATCH para cualquier tipo).
            // El mecanismo correcto es: Doc.clsMain.LoadDocument(id) → doc.Save().
            // Descubierto por DIAG_SAVE.ctx + DIAG_SAVE2.ctx (2026-06-29).
            var doc = CrearHelper("Doc.clsMain");
            var loaded = Com.Call(doc, "LoadDocument", new object[] { (long)documentId });
            if (loaded != null)
                Com.Call(loaded, "Save", new object[0]);
            else
                Com.LastError = "Save: no se pudo cargar el documento " + documentId;
        }
        public void Delete(int documentId)                 { Com.Call(_xe, "Delete",                 new object[] { (long)documentId }); }
        public void AjustarSaldosInsolutos(int documentId) { Com.Call(_xe, "AjustarSaldosInsolutos", new object[] { (long)documentId }); }

        // ---- Doc.clsMain: recalcular y refrescar ----
        // Recalcula totales (subtotal, IVA, total) del documento.
        public void RecalcDocument(int documentId)
        {
            var doc = CrearHelper("Doc.clsMain");
            Com.Call(doc, "RecalcDocument", new object[] { (long)documentId });
        }

        // Recalc completo: totales + costos + saldo pagado. Usar después de crear/modificar partidas.
        public void RecalcCompleto(int documentId)
        {
            RecalcDocument(documentId);
            CalcularCostos(documentId);
            UpdateDocumentPaidInfo(documentId);
        }

        // Refresca visualmente un documento abierto en la pantalla de CONTPAQi.
        public void RefreshDocumento(int documentId)
        {
            var doc = CrearHelper("Doc.clsMain");
            var loaded = Com.Call(doc, "LoadDocument", new object[] { (long)documentId });
            if (loaded != null) Com.Call(loaded, "Refresh", new object[0]);
        }

        // ---- UI de CONTPAQi ----
        public void RefreshGrid()                   { Com.Call(_xe, "RefreshGrid",   new object[0]); }
        public void RefreshRibbon()                 { Com.Call(_xe, "RefreshRibbon", new object[0]); }
        public void GotoModuleID(int moduleId)      { Com.SetProp(_xe, "GotoModuleID", moduleId); }   // es PROPIEDAD-put, no método (verificado en dump)
        public void OpenModule(int moduleId)        { Com.Call(_xe, "OpenModule",    new object[] { moduleId }); }
        public void OpenBrowser(string url)         { Com.Call(_xe, "OpenBrowser",   new object[] { url }); }
        public void ShowMessage(string msg)         { Com.Call(_xe, "ShowMessage2",  new object[] { msg }); }

        // ---- Folio (LBS.clsMain) ----
        // Devuelve el prefijo (serie) configurado para el módulo y almacén.
        public string GetFolioPrefix(int moduleId, int depotId)
        {
            var lbs = CrearHelper("LBS.clsMain");
            return Com.Call(lbs, "GetFolioPrefix", new object[] { moduleId, depotId })?.ToString() ?? "";
        }

        // Devuelve el siguiente número de folio disponible como string.
        public string GetNextFolio(int moduleId, string prefix, int depotId)
        {
            var lbs = CrearHelper("LBS.clsMain");
            return Com.Call(lbs, "GetNextFolio", new object[] { moduleId, prefix, 0, depotId })?.ToString() ?? "";
        }

        // ---- CFDI / Timbrado ----
        // Timbra un documento usando el motor nativo de Comercial (COM CFDI3.clsMain) --
        // el mismo componente que usa el propio modulo de facturacion, sin pasar por SDKPro.
        // pruebas=true usa el modo de pruebas del PAC configurado (no genera timbre fiscal real).
        // Lanza excepcion si el timbrado falla; revisar el mensaje para el detalle del PAC/SAT.
        public void Timbrar(int documentId, bool pruebas = false)
        {
            string destino = "";
            if (_owner != null)
            {
                var cfg = _owner.Query("SELECT TOP 1 CFDDocumentsPath FROM orgBusinessEntityCFD WHERE BusinessEntityID=" + OwnedBusinessEntityId);
                if (cfg != null && cfg.Count > 0 && cfg[0].TryGetValue("CFDDocumentsPath", out var ruta) && ruta != null)
                    destino = ruta.ToString();
            }

            var cfdi = CrearHelper("CFDI3.clsMain");
            Com.SetProp(cfdi, "SDKMode", true);
            Com.SetProp(cfdi, "SoftwareName", "XE");
            Com.SetProp(cfdi, "SoftwareVersion", "2");
            Com.SetProp(cfdi, "SoftwareType", 0);
            Com.SetProp(cfdi, "DestinationDirectory", destino);
            Com.SetProp(cfdi, "AllIDs", documentId.ToString());
            Com.SetProp(cfdi, "TestMode", pruebas);

            // "Timbrar" es una PROPIEDAD (get) con efecto secundario: al leerla, CFDI3.clsMain
            // ejecuta el timbrado y regresa el resultado. No es un metodo.
            var ok = Com.GetProp(cfdi, "Timbrar");
            if (ok == null || !Convert.ToBoolean(ok))
            {
                var detalle = Com.GetProp(cfdi, "ErrorDescription") as string ?? Com.LastError ?? "";
                throw new Exception("Error al timbrar el documento " + documentId + ": " + detalle);
            }
        }

        // Relaciona un CFDI con otro documento (p. ej. nota de crédito que sustituye un
        // anticipo aplicado). tipoRelacion es el catálogo SAT c_TipoRelacion como string:
        // "01" nota de crédito de doctos relacionados, "03" devolución, "07" aplicación de
        // anticipo, etc. — ver catálogo oficial del SAT para el resto de los códigos.
        public void RelacionarCFDI(int documentId, int sourceDocumentId, string tipoRelacion)
        {
            if (_owner == null) throw new Exception("RelacionarCFDI requiere ScriptContext (SQL no disponible).");
            string sql = "INSERT INTO docDocumentCFDIRelacionados (DocumentID, SourceDocumentID, CFDTipoRelacion) VALUES (" +
                documentId + ", " + sourceDocumentId + ", N'" + (tipoRelacion ?? "").Replace("'", "''") + "')";
            _owner.NonQuery(sql);
        }

        // ---- Existencias / Precios ----
        public double GetProductStock(int productId, int depotId)
        {
            var v = Com.Call(_xe, "GetProductStock", new object[] { productId, depotId });
            return v != null ? Convert.ToDouble(v) : 0;
        }

        public double GetSalePrice(int productId, int businessEntityId = 0)
        {
            var v = businessEntityId > 0
                ? Com.Call(_xe, "GetBusinessEntitySalePrice", new object[] { productId, businessEntityId })
                : Com.Call(_xe, "GetSalePrice",              new object[] { productId });
            return v != null ? Convert.ToDouble(v) : 0;
        }

        public double GetBuyPrice(int productId)
        {
            var v = Com.Call(_xe, "GetBuyPrice", new object[] { productId });
            return v != null ? Convert.ToDouble(v) : 0;
        }

        public double GetCostPrice(int productId)
        {
            var v = Com.Call(_xe, "GetCostPrice", new object[] { productId });
            return v != null ? Convert.ToDouble(v) : 0;
        }

        // Devuelve precio con impuestos incluidos.
        // OJO: XEngine espera el orden (taxTypeId, price) — verificado en CONTPAQi real
        // (GetPriceWithTaxes(1,100)=116). Mantenemos la firma pública (price, taxTypeId) y
        // solo invertimos el orden al pasar a COM.
        public double GetPriceWithTaxes(double price, int taxTypeId)
        {
            var v = Com.Call(_xe, "GetPriceWithTaxes", new object[] { taxTypeId, price });
            return v != null ? Convert.ToDouble(v) : price;
        }

        // Tipo de cambio de la moneda indicada (respecto a MXN).
        public double GetCurrencyRate(int currencyId)
        {
            var v = Com.Call(_xe, "GetCurrencyRate", new object[] { currencyId });
            return v != null ? Convert.ToDouble(v) : 1;
        }

        public double GetCurrencyRateBanxico(int currencyId)
        {
            var v = Com.Call(_xe, "GetRateBanxico", new object[] { currencyId });
            return v != null ? Convert.ToDouble(v) : 1;
        }

        // Coeficiente de conversión entre unidades de un producto.
        public double GetCoefConversion(int productId, string fromUnit, string toUnit)
        {
            var v = Com.Call(_xe, "GetCoefConversion", new object[] { productId, fromUnit, toUnit });
            return v != null ? Convert.ToDouble(v) : 1;
        }

        public bool ProductIsKit(int productId)
        {
            var v = Com.Call(_xe, "ProductIsKit", new object[] { productId });
            return v != null && Convert.ToBoolean(v);
        }

        // ---- Crédito ----
        public bool VerifyCreditLimit(int businessEntityId, double amount)
        {
            var v = Com.Call(_xe, "VerifyCreditLimit", new object[] { businessEntityId, amount });
            return v == null || Convert.ToBoolean(v); // true = dentro de límite
        }

        public bool VerifyCreditLimitOverdue(int businessEntityId)
        {
            var v = Com.Call(_xe, "VerifyCreditLimitOverdue", new object[] { businessEntityId });
            return v != null && Convert.ToBoolean(v); // true = tiene vencidos
        }

        // ---- Parámetros de módulo ----
        public string GetModuleParameter(int moduleId, string key)
        {
            var v = Com.Call(_xe, "GetModuleParameter", new object[] { moduleId, key });
            return v as string;
        }

        public void SaveModuleParameter(int moduleId, string key, string value)
        {
            Com.Call(_xe, "SaveModuleParameter", new object[] { moduleId, key, value });
        }

        // Parámetro global (no por módulo).
        public string GetParameter(string key)
        {
            var v = Com.Call(_xe, "GetParameter", new object[] { key });
            return v as string;
        }

        // =====================================================
        //   Creación de documentos (alto nivel, active-record)
        //   Reimplementado a partir del esquema real de la base de datos.
        //   Escribimos SOLO el encabezado (docDocument) y las partidas
        //   (docDocumentItem); los impuestos, kardex y costos los generan los
        //   métodos de post-proceso (RecalcDocument/CalcularCostos/AffectStockNEW).
        //   NADA copiado de terceros.
        // =====================================================

        // Crea el encabezado de un documento con los defaults correctos del módulo
        // y devuelve el nuevo DocumentID. moduleId p.ej. 183 (órdenes de compra);
        // depotId = almacén; businessEntityId = cliente/proveedor (0 = sin asignar).
        // La cabecera + 4 anclas se ejecutan en una SOLA transacción (v2.18.0+):
        // si falla cualquier INSERT, se revierte todo. Evita documentos huérfanos.
        public int NuevoDocumento(int moduleId, int depotId, int businessEntityId = 0)
        {
            RequiereSql("NuevoDocumento");
            GuardaEscritura();

            int documentTypeID = ModuloParametroInt(moduleId, "DocumentTypeID", 0);
            int docRecipientID = ModuloParametroInt(moduleId, "DocRecipient", 0);
            int owned          = OwnedBusinessEntityId;
            int userID         = UserId;
            string prefix      = GetFolioPrefix(moduleId, depotId) ?? "";
            string folio       = GetNextFolio(moduleId, prefix, depotId) ?? "";

            // Campos que el nativo siempre pone igual en CUALQUIER documento (auditoría campo por
            // campo): MustBeSynchronized=1, ExportID=1 y las fechas = DateDocument (DateLastPayment
            // a nivel fecha). Los campos que VARÍAN por módulo (PaymentTermID, DepotIDFrom,
            // CampaignID/CostCenterID/ProjectID, impuesto de partida) los fija el caller según el
            // tipo de documento (dependen de la config del módulo).
            string insertDoc =
                "INSERT INTO docDocument (ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, " +
                "BusinessEntityID, DepotID, FolioPrefix, Folio, DateDocument, LanguageID, CurrencyID, Rate, " +
                "PaymentTermID, CreatedBy, CreatedOn, UserID, " +
                "MustBeSynchronized, ExportID, DateCost, DateDocDelivery, DateFrom, DateTo, DateLastPayment) " +
                "OUTPUT INSERTED.DocumentID VALUES (" +
                moduleId + ", " + documentTypeID + ", " + docRecipientID + ", " + owned + ", " +
                businessEntityId + ", " + depotId + ", " + Lit(prefix) + ", " + Lit(folio) + ", GETDATE(), " +
                "3, 3, 1, 1, " + userID + ", GETDATE(), 0, " +
                "1, 1, GETDATE(), GETDATE(), GETDATE(), GETDATE(), CAST(GETDATE() AS date))";

            // Ejecutar cabecera + 4 anclas en una SOLA llamada SQL (la conexión ADO del
            // DataLayer de CONTPAQi no mantiene @@TRANCOUNT entre llamadas Execute separadas,
            // así que si se necesitara una transacción explícita tendría que ir en el mismo
            // batch). SIN BEGIN TRANSACTION/COMMIT (v2.21.10): se confirmó en vivo que un
            // BEGIN TRANSACTION/COMMIT explícito por T-SQL en esta conexión terminaba fallando
            // con "la operación no está permitida si el objeto está cerrado" -- el mismo
            // encabezado + anclas ejecutados como sentencias sueltas (sin transacción) SÍ
            // funcionan. Hipótesis: el DataLayer de CONTPAQi administra su propia transacción
            // ambiental (estilo COM+/MTS, común en ERPs VB6 legado) y un BEGIN TRANSACTION
            // manual por T-SQL entra en conflicto con eso. Se pierde la atomicidad total (si
            // una de las 4 anclas fallara, el documento quedaría a medias), pero es preferible
            // a que la creación de documentos no funcione en absoluto.
            string batch =
                "DECLARE @newDocId BIGINT; " +
                "INSERT INTO docDocument (ModuleID, DocumentTypeID, DocRecipientID, OwnedBusinessEntityID, " +
                "BusinessEntityID, DepotID, FolioPrefix, Folio, DateDocument, LanguageID, CurrencyID, Rate, " +
                "PaymentTermID, CreatedBy, CreatedOn, UserID, " +
                "MustBeSynchronized, ExportID, DateCost, DateDocDelivery, DateFrom, DateTo, DateLastPayment) " +
                "VALUES (" +
                moduleId + ", " + documentTypeID + ", " + docRecipientID + ", " + owned + ", " +
                businessEntityId + ", " + depotId + ", " + Lit(prefix) + ", " + Lit(folio) + ", GETDATE(), " +
                "3, 3, 1, 1, " + userID + ", GETDATE(), 0, " +
                "1, 1, GETDATE(), GETDATE(), GETDATE(), GETDATE(), CAST(GETDATE() AS date)); " +
                "SET @newDocId = SCOPE_IDENTITY(); " +
                "INSERT INTO docDocumentExt (IDExtra) VALUES (@newDocId); " +
                "INSERT INTO docDocumentExtra (DocumentID) VALUES (@newDocId); " +
                "INSERT INTO docDocumentCFD (DocumentID, FinancialOperationID, Anexo20Ver) VALUES (@newDocId, 0, N'4.0'); " +
                "INSERT INTO docDocumentPaymentAgenda (DocumentID, DatePayment, TotalPerc, PartialityNumber, CreatedOn, CreatedBy) VALUES (@newDocId, GETDATE(), 100, 1, GETDATE(), " + userID + "); " +
                "SELECT @newDocId AS NewDocId;";

            // Si _owner.Query(batch) falla por COM Y por OpenConn() independiente, ya lanza su
            // propia excepcion (con ambos motivos) -- eso se propaga tal cual, no llega aqui abajo.
            List<Dictionary<string, object>> rows;
            try
            {
                rows = _owner.Query(batch);
            }
            catch (SqlYaEjecutadoException)
            {
                // El INSERT probablemente SI corrio (el Execute por COM tuvo exito) pero la
                // conexion murio leyendo el DocumentID de vuelta. NO se reintenta el INSERT
                // (duplicaria el encabezado) -- se recupera por una consulta de SOLO LECTURA
                // usando Folio+Modulo+Almacen+Proveedor, que ya son unicos para este intento
                // (el folio se reservo ANTES del INSERT, vía GetFolioPrefix/GetNextFolio).
                Com.DiagLog("NuevoDocumento: recuperando por folio tras SqlYaEjecutadoException. Folio=" + prefix + folio + " ModuleID=" + moduleId + " DepotID=" + depotId);
                rows = _owner.Query(
                    "SELECT TOP 1 DocumentID AS NewDocId FROM docDocument WHERE ModuleID=" + moduleId +
                    " AND DepotID=" + depotId + " AND BusinessEntityID=" + businessEntityId +
                    " AND FolioPrefix=" + Lit(prefix) + " AND Folio=" + Lit(folio) +
                    " AND CreatedOn >= DATEADD(MINUTE,-5,GETDATE()) ORDER BY DocumentID DESC");
                if (rows.Count > 0) Com.DiagLog("NuevoDocumento: recuperado doc=" + rows[0]["NewDocId"] + " por folio (no por el INSERT original).");
                else Com.DiagLog("NuevoDocumento: NO se encontro nada por folio -- el INSERT probablemente si fallo de verdad.");
            }
            int nuevoDocumentId = (rows.Count > 0 && rows[0].ContainsKey("NewDocId")) ? Com.ToInt(rows[0]["NewDocId"]) : 0;
            if (nuevoDocumentId <= 0)
            {
                // Query() SI devolvio (no truena) pero sin el NewDocId esperado -- distinto de
                // que Query() haya fallado del todo. Com.LastError() puede quedar con texto viejo
                // de un Com.Call anterior (es un campo estatico compartido) asi que se marca
                // explicito "(no confiable, ver Conexion_YYYYMMDD.txt)" para no confundir.
                string detalle = !string.IsNullOrEmpty(Com.LastError)
                    ? " — ultimo Com.LastError (puede ser de un paso anterior, no confiable por si solo): " + Com.LastError
                    : " (sin Com.LastError; Query devolvio " + rows.Count + " fila(s) sin NewDocId)";
                Com.DiagLog("NuevoDocumento: FALLO nuevoDocumentId<=0. ModuleID=" + moduleId + " DepotID=" + depotId + " filas=" + rows.Count + " LastError=" + Com.LastError);
                throw new Exception("NuevoDocumento [BrosLMV v" + Com.Version + "]: no se pudo crear el encabezado. ModuleID=" + moduleId + " DepotID=" + depotId + detalle +
                    "\n\nRevisa C:\\BrosLMV\\logs\\Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt para el detalle completo de esta ejecucion.");
            }
            return nuevoDocumentId;
        }

        // Agrega una partida (producto) a un documento; lee los datos del producto de
        // orgProduct. cantidad por defecto 1; precioUnitario < 0 = sin precio (0).
        // costo < 0 = no setear CostPrice; costo >= 0 puebla docDocumentItem.CostPrice
        // (costo de ENTRADA de inventario, distinto de UnitPrice). Debe poblarse ANTES de
        // RecalcCompleto/CalcularCostos/AffectStockNEW (que costean orgProductCostComercial).
        // taxTypeIdOverride >= 0 sustituye el impuesto del producto (por defecto se usa el
        // de orgProduct.TaxTypeID). descuentoPerc es fracción (0.05 = 5%), igual que TaxPerc.
        // Devuelve el nuevo DocumentItemID. Tras agregar partidas, llamar RecalcCompleto.
        // La partida se crea en una transacción atómica (v2.18.0+).
        //
        // deliverDocumentItemId (v2.22.0, para Recepción de Compra, módulo 184): DocumentItemID
        // de la partida de la Orden de Compra que esta partida está surtiendo. Es la base del
        // cálculo PROPIO de "cuánto falta recibir" por partida de OC (no depender de
        // vwLBSProductsToDeliver/vwLBSProductsDelivered, que solo soportan una OC origen por
        // encabezado de Recepción vía docDocument.SourceDocumentID — no sirve cuando una sola
        // Recepción cubre VARIAS Órdenes de Compra). 0 = no aplica (documento sin origen, p. ej.
        // una Orden de Compra).
        // lote/serialNumber: solo si el producto usa control de lote (orgProduct.UseLot) o
        // número de serie (orgProduct.UseSerialNumber) — ver MANUAL.md para el patrón completo.
        public int AgregarArticulo(int documentId, int productId, double cantidad = 1, double precioUnitario = -1,
            double costo = -1, int taxTypeIdOverride = -1, double descuentoPerc = 0,
            int deliverDocumentItemId = 0, string lote = null, string serialNumber = null)
        {
            RequiereSql("AgregarArticulo");
            GuardaEscritura();

            string productKey = "", description = "", unit = "", claveUnidad = "", objetoImpuesto = "";
            int taxTypeId = 0;
            var prod = _owner.Query("SELECT ProductKey, ProductName, TaxTypeID, Unit, ClaveUnidad, ObjetoImpuesto FROM orgProduct WHERE ProductID=" + productId);
            if (prod.Count > 0)
            {
                var p = prod[0];
                productKey     = ValStr(p, "ProductKey");
                description    = ValStr(p, "ProductName");
                unit           = ValStr(p, "Unit");
                taxTypeId      = ValInt(p, "TaxTypeID");
                claveUnidad    = ValStr(p, "ClaveUnidad");
                objetoImpuesto = ValStr(p, "ObjetoImpuesto");
            }
            if (taxTypeIdOverride >= 0) taxTypeId = taxTypeIdOverride;
            double precio = precioUnitario < 0 ? 0 : precioUnitario;

            // El nativo guarda el % de IVA EN LA PARTIDA (TaxPerc), no solo el TaxTypeID -- el
            // motor de recalculo usa ese valor tal cual, no lo vuelve a resolver. Sin esto, el
            // impuesto queda en 0 aunque TaxTypeID sí esté bien puesto. vwLBSTaxPerc es la
            // misma vista que usa LBS (el listado/combo nativo) para resolver TaxTypeID -> %.
            double taxPerc = 0;
            if (taxTypeId > 0)
            {
                var tp = _owner.Query("SELECT IVA_Perc FROM vwLBSTaxPerc WHERE TaxTypeID=" + taxTypeId);
                if (tp.Count > 0) { try { taxPerc = Convert.ToDouble(tp[0]["IVA_Perc"] ?? 0); } catch { } }
            }

            // CostPrice solo cuando se indica costo (>= 0); si no, se omite la columna y
            // queda en su default (0) como hasta ahora.
            bool conCosto = costo >= 0;
            string colCost = conCosto ? ", CostPrice" : "";
            string valCost = conCosto ? ", " + Num(costo) : "";

            // DeliverDocumentItemID/Lot/SerialNumber: opcionales, solo para Recepción de Compra
            // (ver comentario en la firma del método).
            bool conDeliver = deliverDocumentItemId > 0;
            string colDeliver = conDeliver ? ", DeliverDocumentItemID" : "";
            string valDeliver = conDeliver ? ", " + deliverDocumentItemId : "";
            bool conLote = !string.IsNullOrEmpty(lote);
            string colLote = conLote ? ", Lot" : "";
            string valLote = conLote ? ", " + Lit(lote) : "";
            bool conSerie = !string.IsNullOrEmpty(serialNumber);
            string colSerie = conSerie ? ", SerialNumber" : "";
            string valSerie = conSerie ? ", " + Lit(serialNumber) : "";

            // Campos que el nativo siempre pone en la partida (auditoría campo por campo): flags=1,
            // DateItem=fecha, CoefUnit=1 (unidad base), y claves SAT copiadas del producto. ClaveUnidad/
            // ObjetoImpuesto se ponen NULL si el producto no las tiene (para igualar al nativo).
            string valClave = string.IsNullOrEmpty(claveUnidad) ? "NULL" : Lit(claveUnidad);
            string valObj   = string.IsNullOrEmpty(objetoImpuesto) ? "NULL" : Lit(objetoImpuesto);

            // Todo en un solo batch, SIN BEGIN TRANSACTION/COMMIT explícito (v2.21.10 — ver
            // NuevoDocumento para la explicación completa de por qué se quitó).
            string batch =
                "DECLARE @line INT = ISNULL((SELECT MAX(LineNumber) FROM docDocumentItem WHERE DocumentID=" +
                documentId + " AND DeletedOn IS NULL),0)+1; " +
                "DECLARE @newItemId BIGINT; " +
                "INSERT INTO docDocumentItem (DocumentID, ProductID, ProductKey, Description, Unit, " +
                "Quantity, UnitPrice, Total, TaxTypeID, TaxPerc, DiscountPerc, LineNumber, DateItem, " +
                "ApplyGlobalDiscount, DeductiblePerc, IsBusinessOperation, MustBeDelivered, CoefUnit, " +
                "ClaveUnidad, ObjetoImpuesto" + colCost + colDeliver + colLote + colSerie + ") " +
                "VALUES (" +
                documentId + ", " + productId + ", " + Lit(productKey) + ", " + Lit(description) + ", " +
                Lit(unit) + ", " + Num(cantidad) + ", " + Num(precio) + ", " + Num(cantidad * precio) + ", " +
                taxTypeId + ", " + Num(taxPerc) + ", " + Num(descuentoPerc) + ", @line, GETDATE(), " +
                "1, 1, 1, 1, 1, " + valClave + ", " + valObj + valCost + valDeliver + valLote + valSerie + "); " +
                "SET @newItemId = SCOPE_IDENTITY(); " +
                "SELECT @newItemId AS NewItemId;";

            List<Dictionary<string, object>> rows;
            try
            {
                rows = _owner.Query(batch);
            }
            catch (SqlYaEjecutadoException)
            {
                // Mismo caso que en NuevoDocumento: el INSERT probablemente SI corrio pero la
                // conexion murio leyendo el DocumentItemID de vuelta. NO se reintenta (duplicaria
                // la partida) -- se recupera de solo lectura: la partida MAS RECIENTE para este
                // documento+producto (el script llama AgregarArticulo una sola vez por PID, ya
                // deduplicado en las partidas antes de llegar aqui).
                Com.DiagLog("AgregarArticulo: recuperando por DocID+ProdID tras SqlYaEjecutadoException. DocID=" + documentId + " ProdID=" + productId);
                rows = _owner.Query(
                    "SELECT TOP 1 DocumentItemID AS NewItemId FROM docDocumentItem " +
                    "WHERE DocumentID=" + documentId + " AND ProductID=" + productId + " AND DeletedOn IS NULL " +
                    "ORDER BY DocumentItemID DESC");
                if (rows.Count > 0) Com.DiagLog("AgregarArticulo: recuperado item=" + rows[0]["NewItemId"] + " por DocID+ProdID (no por el INSERT original).");
                else Com.DiagLog("AgregarArticulo: NO se encontro nada por DocID+ProdID -- el INSERT probablemente si fallo de verdad.");
            }
            int itemId = (rows.Count > 0 && rows[0].ContainsKey("NewItemId")) ? Com.ToInt(rows[0]["NewItemId"]) : 0;
            if (itemId <= 0)
            {
                string detalle = !string.IsNullOrEmpty(Com.LastError)
                    ? " — ultimo Com.LastError (puede ser de un paso anterior, no confiable por si solo): " + Com.LastError
                    : " (sin Com.LastError; Query devolvio " + rows.Count + " fila(s) sin NewItemId)";
                Com.DiagLog("AgregarArticulo: FALLO itemId<=0. DocID=" + documentId + " ProdID=" + productId + " filas=" + rows.Count + " LastError=" + Com.LastError);
                throw new Exception("AgregarArticulo [BrosLMV v" + Com.Version + "]: no se pudo crear la partida. DocID=" + documentId + " ProdID=" + productId + detalle +
                    "\n\nRevisa C:\\BrosLMV\\logs\\Conexion_" + DateTime.Now.ToString("yyyyMMdd") + ".txt para el detalle completo de esta ejecucion.");
            }
            return itemId;
        }

        // ---- helpers privados de los builders ----
        private void RequiereSql(string metodo)
        {
            if (_owner == null)
                throw new Exception("ctx.erp." + metodo + " requiere conexión SQL (no disponible en este contexto).");
        }
        private void GuardaEscritura()
        {
            if (_owner != null && _owner.SoloLectura)
                throw new Exception("Modo SOLO LECTURA activo: no se pueden crear/modificar documentos.");
        }
        private int ModuloParametroInt(int moduleId, string key, int defecto)
        {
            try { int n; return int.TryParse(GetModuleParameter(moduleId, key), out n) ? n : defecto; }
            catch { return defecto; }
        }
        private static string ValStr(Dictionary<string, object> row, string col)
        { object v; return row.TryGetValue(col, out v) ? (v as string ?? "") : ""; }
        private static int ValInt(Dictionary<string, object> row, string col)
        { object v; return row.TryGetValue(col, out v) && v != null ? Convert.ToInt32(v) : 0; }
        private static string Lit(string s) { return "N'" + (s ?? "").Replace("'", "''") + "'"; }
        private static string Num(double d) { return d.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        // ---- Utilidades de negocio ----
        // Importe con letra en español ("MIL ... PESOS 50/100 M.N."). El 2º parámetro es el
        // CurrencyId (int); si se omite (0), se usa la moneda activa. Verificado en CONTPAQi.
        public string GetTotalLetter(double amount, int currencyId = 0)
        {
            if (currencyId <= 0) currencyId = CurrencyId;
            var v = Com.Call(_xe, "GetTotalLetter", new object[] { amount, currencyId });
            return v as string ?? "";
        }

        // Código de barras como string codificado (base64/imagen, según versión).
        public string GetBarCode(string value, string barcodeType = "CODE128")
        {
            var v = Com.Call(_xe, "GetBarCode", new object[] { value, barcodeType });
            return v as string ?? "";
        }

        public string DecryptString(string encrypted)
        {
            return Com.Call(_xe, "DecryptString", new object[] { encrypted }) as string ?? "";
        }

        public string EncryptString(string plain)
        {
            return Com.Call(_xe, "EncryptString", new object[] { plain }) as string ?? "";
        }

        public bool ValidRFC(string rfc)
        {
            var v = Com.Call(_xe, "ValidRFC", new object[] { rfc });
            return v != null && Convert.ToBoolean(v);
        }

        public string FormatCurrency(double amount)
        {
            var v = Com.Call(_xe, "FormatCurrency", new object[] { amount });
            return v as string ?? amount.ToString("C2");
        }

        // (NumDecimalesMoneda/PUnitario/Conceptos se quitaron: devolvían NULL en CONTPAQi real.
        //  Si se necesita, queda accesible por ctx.erp.Call("NumDecimalesMoneda", ...) o por SQL.)

        // ---- DLookup (consultas puntuales sin SQL) ----
        public object DLookup(string field, string table, string where = "")
        {
            return Com.Call(_xe, "DLookup", new object[] { field, table, where });
        }

        public string DLookupStr(string field, string table, string where = "")
        {
            return DLookup(field, table, where) as string ?? "";
        }

        public int DLookupInt(string field, string table, string where = "")
        {
            return Com.ToInt(DLookup(field, table, where));
        }

        // ---- Auditoría / Log ----
        public void WriteToLog(string message)
        {
            Com.Call(_xe, "WriteToLog", new object[] { message });
        }

        public void WriteToTableLog(string message, string detail = "")
        {
            Com.Call(_xe, "WriteToTableLog", new object[] { message, detail });
        }

        // ---- Impresión / Exportación ----
        public void PrintDoc(int documentId)   { Com.Call(_xe, "PrintDoc",    new object[] { documentId }); }
        public void PrintModule()              { Com.Call(_xe, "PrintModule", new object[0]); }
        public void UpdatePrintedOn(int documentId) { Com.Call(_xe, "UpdatePrintedOn", new object[] { documentId }); }

        // Genera PDF del documento en la ruta indicada.
        public string CreatePDF(int documentId, string outputPath)
        {
            Com.Call(_xe, "CreatePDF", new object[] { documentId, outputPath });
            return outputPath;
        }

        // Exporta resultado de una consulta SQL a Excel (usa el motor nativo de CONTPAQi).
        public void ExportQueryToExcel(string sql, string outputPath = "")
        {
            if (string.IsNullOrEmpty(outputPath))
                Com.Call(_xe, "ExportQueryToExcel", new object[] { sql });
            else
                Com.Call(_xe, "ExportQueryToExcel", new object[] { sql, outputPath });
        }

        // Exporta la vista activa del módulo a Excel.
        public void ExportJanusToExcel(string outputPath = "")
        {
            if (string.IsNullOrEmpty(outputPath))
                Com.Call(_xe, "ExportJanusToExcel", new object[0]);
            else
                Com.Call(_xe, "ExportJanusToExcel", new object[] { outputPath });
        }

        // ---- Correo ----
        // Usa la config de correo de CONTPAQi (engUserMailConfig).
        public void SendMail(string to, string subject, string body, string attachmentPath = "")
        {
            if (string.IsNullOrEmpty(attachmentPath))
                Com.Call(_xe, "SendMailXE", new object[] { to, subject, body });
            else
                Com.Call(_xe, "SendMailXE", new object[] { to, subject, body, attachmentPath });
        }

        public string GetEmailTemplateID(string templateKey)
        {
            return Com.Call(_xe, "GetEmailTemplateID", new object[] { templateKey }) as string ?? "";
        }

        // ---- Internet / Web ----
        public string GetWebContent(string url)
        {
            return Com.Call(_xe, "GetWebContent", new object[] { url }) as string ?? "";
        }

        public bool IsConnectedToInternet()
        {
            var v = Com.Call(_xe, "IsConnectedToInternet", new object[0]);
            return v != null && Convert.ToBoolean(v);
        }

        // Ejecuta un shell command (ShellExecute) - equivalente a Process.Start.
        public void RunShellExecute(string path, string args = "")
        {
            Com.Call(_xe, "RunShellExecute", new object[] { path, args });
        }

        // ---- CFDI / Timbrado ----
        // Estado del QR/validación SAT de un documento (true = timbrado y válido).
        public bool AlreadyDocsSigned(int documentId)
        {
            var v = Com.Call(_xe, "AlreadyDocsSigned", new object[] { documentId });
            return v != null && Convert.ToBoolean(v);
        }

        // Obtiene el estado de pago del documento (0=sin pago, 1=parcial, 2=pagado).
        public int GetStatusPaidID(int documentId)
        {
            return Com.ToInt(Com.Call(_xe, "GetStatusPaidID", new object[] { documentId }));
        }

        // ---- Acceso genérico a CUALQUIER miembro de XEngine (los 562) ----
        // XEngine expone cientos de miembros; aquí solo se envuelven con tipo los más usados.
        // Para los demás, llámalos por nombre: el script provee los argumentos (late-bound).
        // Ejemplo: var qr = ctx.erp.Call("GetQRCode", "datos");
        //          var rfc = (string)ctx.erp.Get("COMERCIAL_RFC");
        public object Call(string method, params object[] args)
        {
            return Com.Call(_xe, method, args ?? new object[0]);
        }
        public object Get(string property)
        {
            return Com.GetProp(_xe, property);
        }
        public bool Set(string property, object value)
        {
            return Com.SetProp(_xe, property, value);
        }

        // ---- Wrappers tipados extra (análogos verificados de los existentes) ----
        // Precio de venta específico por entidad de negocio (cliente).
        public double GetBusinessEntitySalePrice(int productId, int businessEntityId)
        {
            var v = Com.Call(_xe, "GetBusinessEntitySalePrice", new object[] { productId, businessEntityId });
            return v != null ? Convert.ToDouble(v) : 0;
        }

        // Importe con letra en inglés. Mismo patrón: 2º parámetro = CurrencyId (0 = activa).
        public string GetTotalLetterEN(double amount, int currencyId = 0)
        {
            if (currencyId <= 0) currencyId = CurrencyId;
            return Com.Call(_xe, "GetTotalLetterEN", new object[] { amount, currencyId }) as string ?? "";
        }

        // Descarga el HTML de una URL (variante de GetWebContent).
        public string GetHTMLFromURL(string url)
        {
            return Com.Call(_xe, "GetHTMLFromURL", new object[] { url }) as string ?? "";
        }

        // ---- Helper: crear objetos COM auxiliares de CONTPAQi ----
        // Crea Doc.clsMain, LBS.clsMain, o cualquier otro COM helper con XEngineLib seteado.
        // Ejemplo: var doc = ctx.erp.CrearHelper("Doc.clsMain");
        //          Com.Call(doc, "RecalcDocument", new object[] { id });
        public object CrearHelper(string progId)
        {
            var type = Type.GetTypeFromProgID(progId);
            if (type == null) throw new Exception(progId + " no está registrado en este equipo.");
            var obj = Activator.CreateInstance(type);
            Com.SetProp(obj, "XEngineLib", _xe);
            return obj;
        }

        // Acceso directo al XEngineLib crudo (para casos no cubiertos por ctx.erp.*).
        public object XE => _xe;
    }

    // Objeto "globals": sus miembros publicos quedan accesibles en el script -> ctx.*
    public class ScriptHost { public ScriptContext ctx; }

    // =========================================================
    //   EJECUTOR Roslyn
    // =========================================================
    public static class ScriptRunner
    {
        private static ScriptOptions _options;
        // Cache de scripts ya creados/compilados, por codigo. Evita recompilar
        // (Verificar + Ejecutar del mismo codigo = una sola compilacion; repetir = instantaneo).
        private static readonly Dictionary<string, Script<object>> _cache =
            new Dictionary<string, Script<object>>(StringComparer.Ordinal);

        private static ScriptOptions BuildOptions()
        {
            if (_options != null) return _options;
            _options = ScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(Uri).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(DataTable).Assembly,
                    typeof(SqlConnection).Assembly,
                    typeof(Form).Assembly,
                    typeof(System.Drawing.Color).Assembly,
                    typeof(ScriptContext).Assembly)
                .WithImports(
                    "System", "System.Collections.Generic", "System.Linq", "System.Text",
                    "System.Data", "System.Data.SqlClient",
                    "System.Windows.Forms", "System.Drawing", "BrosLMV");
            return _options;
        }

        private static Script<object> Obtener(string codigo)
        {
            Script<object> s;
            if (_cache.TryGetValue(codigo, out s)) return s;
            s = CSharpScript.Create<object>(codigo, BuildOptions(), typeof(ScriptHost));
            if (_cache.Count > 60) _cache.Clear();   // evitar crecer sin limite
            _cache[codigo] = s;
            return s;
        }

        // Calienta Roslyn (1ra compilacion del proceso). Se llama en segundo plano.
        public static void Precalentar()
        {
            try { CSharpScript.Create<object>("1+1", BuildOptions(), typeof(ScriptHost)).Compile(); }
            catch { }
        }

        private static List<string> ErroresDe(Script<object> script)
        {
            var errores = new List<string>();
            try
            {
                foreach (var d in script.Compile())
                {
                    if (d.Severity != DiagnosticSeverity.Error) continue;
                    var ls = d.Location.GetLineSpan();
                    errores.Add("linea " + (ls.StartLinePosition.Line + 1) + ": " + d.GetMessage() + "  (" + d.Id + ")");
                }
            }
            catch (CompilationErrorException cex) { foreach (var d in cex.Diagnostics) errores.Add(d.ToString()); }
            return errores;
        }

        public static List<string> Compilar(string codigo)
        {
            try { return ErroresDe(Obtener(codigo)); }
            catch (Exception ex) { return new List<string> { "Error compilando: " + ex.Message }; }
        }

        public static string Ejecutar(string codigo, ScriptContext ctx)
        {
            string _; return EjecutarConValor(codigo, ctx, out _);
        }

        // Como Ejecutar, pero además devuelve por 'salida' el valor de retorno del script
        // (lo que el script haga `return ...;`), para mostrarlo en el panel de Salida.
        // El valor de retorno de este método sigue siendo SOLO el error ("" = éxito).
        public static string EjecutarConValor(string codigo, ScriptContext ctx, out string salida)
        {
            salida = "";
            Script<object> script;
            try { script = Obtener(codigo); }
            catch (Exception ex) { return "Error compilando: " + ex.Message; }

            var errores = ErroresDe(script);                       // usa la misma instancia (compila 1 vez)
            if (errores.Count > 0) return string.Join(Environment.NewLine, errores);

            try
            {
                var estado = script.RunAsync(new ScriptHost { ctx = ctx }).GetAwaiter().GetResult();
                if (estado.ReturnValue != null) salida = estado.ReturnValue.ToString();
                return "";
            }
            catch (Exception ex)
            {
                var e = ex.InnerException ?? ex;
                return "EXCEPCION EN EJECUCION:" + Environment.NewLine + e.GetType().Name + ": " + e.Message +
                       Environment.NewLine + e.StackTrace;
            }
        }

        public static string EjecutarArchivo(string ruta, ScriptContext ctx)
        {
            if (!File.Exists(ruta)) return "No existe el script: " + ruta;
            return Ejecutar(File.ReadAllText(ruta), ctx);
        }
    }
}
