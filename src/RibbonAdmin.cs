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

// RibbonAdmin.cs  (v2.95.0)
// Motor del asistente «Crear botón…»: lee y escribe la estructura del ribbon de Comercial
// (engRibbonTab / engRibbonGroup / engRibbonControl / engRibbonMenu). Documentación: docs\CREAR_BOTON.md y docs\DISENO_CREAR_BOTON.md.
//
// Reglas (aprendidas en 2.94.0, ver AGENTS.md):
//  - Todo el SQL va por una SqlConnection propia (ctx.OpenConn) dentro de UNA transacción por empresa; nunca ctx.NonQuery/Scalar para lotes.
//  - Solo se crean/cambian botones «BrosLMV.*» (los nativos no se tocan). Publicar es idempotente: si el botón ya existe, lo actualiza.
//  - Antes de cambiar o quitar un botón se guarda una copia (zzBrosRibbonHist) para poder deshacer.
//  - Una fila de engRibbonMenu = un lugar donde aparece el botón: ExtraMenuModuleID (0 = todos los módulos) x IfUserIDIs (0 = todos los usuarios).
//  - Se puede publicar en varias empresas; la pestaña y la sección se buscan por NOMBRE en cada base (los IDs no coinciden entre empresas).

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BrosLMV
{
    internal class BotonSpec
    {
        public string AppKey;            // clave técnica sin el prefijo (CREAR_DOC_DESDE_XML)
        public string Caption;           // texto del botón
        public string Description;       // texto al pasar el mouse (opcional)
        public string Icon;              // nombre de archivo .ico dentro de ...\ComercialSP\Icons
        public string TabCaption;        // pestaña (se crea si no existe)
        public string GroupCaption;      // sección (se crea si no existe)
        public List<long> Modules = new List<long>();  // vacío = todos los módulos
        public List<long> Users = new List<long>();    // vacío = todos los usuarios
        public List<string> Empresas = new List<string>(); // vacío = solo la empresa activa
    }

    internal class RibbonAdmin
    {
        private readonly ScriptContext _ctx;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // La cadena de conexión y el usuario se resuelven AQUÍ, en el hilo que crea el objeto (el de Comercial): la ventana del asistente
        // corre en otro hilo y un objeto COM de Comercial no se puede llamar desde ahí. Después todo el SQL usa una SqlConnection propia.
        private readonly string _cs;
        private readonly int _userId;

        public RibbonAdmin(ScriptContext ctx)
        {
            _ctx = ctx;
            _cs = ctx.CadenaSql();
            _userId = ctx.UserIdReal();
        }

        private SqlConnection Abrir() { var c = new SqlConnection(_cs); c.Open(); return c; }

        // ---------------------------------------------------------------- utilidades
        public static string Ejecuta(string appKey) { return "BrosLMV." + appKey; }

        // Carpeta donde Comercial lee los íconos del ribbon (x86 primero: Comercial es de 32 bits).
        public static string CarpetaIconos()
        {
            foreach (var d in new[] { @"C:\Program Files (x86)\Compac\ComercialSP\Icons", @"C:\Program Files\Compac\ComercialSP\Icons" })
                if (Directory.Exists(d)) return d;
            return null;
        }

        private static List<Dictionary<string, object>> Filas(SqlConnection c, string sql, SqlTransaction tx = null, params object[] pars)
        {
            var res = new List<Dictionary<string, object>>();
            using (var cmd = new SqlCommand(sql, c, tx))
            {
                for (int i = 0; i + 1 < pars.Length; i += 2) cmd.Parameters.AddWithValue((string)pars[i], pars[i + 1] ?? DBNull.Value);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                        res.Add(d);
                    }
            }
            return res;
        }

        private static object Escalar(SqlConnection c, SqlTransaction tx, string sql, params object[] pars)
        {
            using (var cmd = new SqlCommand(sql, c, tx))
            {
                for (int i = 0; i + 1 < pars.Length; i += 2) cmd.Parameters.AddWithValue((string)pars[i], pars[i + 1] ?? DBNull.Value);
                var o = cmd.ExecuteScalar();
                return o == DBNull.Value ? null : o;
            }
        }

        private static void Exec(SqlConnection c, SqlTransaction tx, string sql, params object[] pars)
        {
            using (var cmd = new SqlCommand(sql, c, tx))
            {
                for (int i = 0; i + 1 < pars.Length; i += 2) cmd.Parameters.AddWithValue((string)pars[i], pars[i + 1] ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private static long L(object o) { return o == null ? 0 : Convert.ToInt64(o); }

        // ---------------------------------------------------------------- contexto para la pantalla
        // Todo lo que la pantalla necesita para no escribir nada a mano: pestañas, secciones (con algunos botones para la vista previa),
        // módulos, usuarios/grupos, empresas y el catálogo de íconos.
        public Dictionary<string, object> Contexto(string appKey)
        {
            var res = new Dictionary<string, object>();
            using (var c = Abrir())
            {
                string db = Convert.ToString(Escalar(c, null, "SELECT DB_NAME()"));
                res["empresa"] = db;
                res["usuarioId"] = _userId;

                // ModuleID = pestaña propia de un módulo (0 = compartida); DRL = tipo de vista donde se muestra (drlGrid = listas, NULL = siempre).
                res["tabs"] = Filas(c, "SELECT RibbonTabID AS id, TabCaption AS caption, TabOrder AS orden, ModuleID AS modulo, DRL AS drl FROM engRibbonTab ORDER BY TabOrder, TabCaption")
                    .Select(t => new Dictionary<string, object> { { "id", L(t["id"]) }, { "caption", t["caption"] }, { "orden", t["orden"] }, { "modulo", L(t["modulo"]) }, { "drl", t["drl"] } }).ToList();

                // Sección sugerida: donde ya viven más botones BrosLMV.* (si no hay, la pantalla propone la primera pestaña).
                var sug = Filas(c, "SELECT TOP 1 t.TabCaption AS tab, g.GroupCaption AS grupo FROM engRibbonMenu m JOIN engRibbonControl k ON k.ControlID=m.ControlID " +
                    "JOIN engRibbonGroup g ON g.RibbonGroupID=m.RibbonGroupID JOIN engRibbonTab t ON t.RibbonTabID=g.RibbonTabID " +
                    "WHERE k.ControlExecute LIKE 'BrosLMV.%' GROUP BY t.TabCaption, g.GroupCaption ORDER BY COUNT(*) DESC");
                res["sugerida"] = sug.Count == 0 ? null : new Dictionary<string, object> { { "tab", sug[0]["tab"] }, { "group", sug[0]["grupo"] } };

                var botones = Filas(c,
                    "SELECT m.RibbonGroupID AS gid, c.ControlCaption AS cap, c.IconFile AS ico FROM engRibbonMenu m JOIN engRibbonControl c ON c.ControlID=m.ControlID " +
                    "WHERE m.RibbonGroupID IS NOT NULL ORDER BY m.RibbonGroupID, m.ControlOrder");
                var porGrupo = botones.GroupBy(b => L(b["gid"])).ToDictionary(g => g.Key, g => g.Take(4).Select(b => new Dictionary<string, object> { { "caption", b["cap"] }, { "icon", b["ico"] } }).ToList());
                res["groups"] = Filas(c, "SELECT RibbonGroupID AS id, RibbonTabID AS tabId, GroupCaption AS caption, GroupOrder AS orden FROM engRibbonGroup ORDER BY RibbonTabID, GroupOrder, GroupCaption")
                    .Select(g => new Dictionary<string, object> {
                        { "id", L(g["id"]) }, { "tabId", L(g["tabId"]) }, { "caption", g["caption"] },
                        { "botones", porGrupo.ContainsKey(L(g["id"])) ? (object)porGrupo[L(g["id"])] : new List<object>() } }).ToList();

                // Módulos con lista (grid) agrupados por su módulo padre (compras, ventas, inventarios…): salen de engModule, no de una lista escrita a mano.
                var mods = Filas(c, "SELECT ModuleID AS id, ModuleName AS nombre, ParentModuleID AS padre, Drl AS drl FROM engModule WHERE DeletedOn IS NULL AND Drl='drlGrid' ORDER BY ModuleOrder, ModuleName");
                var nombres = Filas(c, "SELECT ModuleID AS id, ModuleName AS nombre FROM engModule WHERE DeletedOn IS NULL").ToDictionary(x => L(x["id"]), x => Convert.ToString(x["nombre"]));
                res["modulos"] = mods.Select(m => new Dictionary<string, object> {
                    { "id", L(m["id"]) }, { "nombre", m["nombre"] }, { "drl", m["drl"] },
                    { "grupo", nombres.ContainsKey(L(m["padre"])) ? nombres[L(m["padre"])] : "Otros" } }).ToList();

                res["usuarios"] = Filas(c, "SELECT UserID AS id, UserName AS nombre, UserGroupID AS grupo FROM engUser ORDER BY UserName")
                    .Select(u => new Dictionary<string, object> { { "id", L(u["id"]) }, { "nombre", u["nombre"] }, { "grupo", u["grupo"] == null ? 0 : L(u["grupo"]) } }).ToList();
                res["grupos"] = Filas(c, "SELECT UserGroupID AS id, GroupName AS nombre FROM engUserGroup ORDER BY GroupName")
                    .Select(g => new Dictionary<string, object> { { "id", L(g["id"]) }, { "nombre", g["nombre"] } }).ToList();

                res["empresas"] = ListarEmpresas(c, db);
                res["existe"] = string.IsNullOrEmpty(appKey) ? null : Buscar(c, appKey);
            }
            res["iconosDir"] = CarpetaIconos();
            res["iconos"] = ListarIconos();
            return res;
        }

        // Empresas (bases) de Comercial a las que este usuario puede acceder y que tienen ribbon.
        private static List<string> ListarEmpresas(SqlConnection c, string actual)
        {
            var lista = new List<string> { actual };
            try
            {
                foreach (var f in Filas(c, "SELECT name FROM sys.databases WHERE HAS_DBACCESS(name)=1 AND database_id>4 AND state=0 ORDER BY name"))
                {
                    string n = Convert.ToString(f["name"]);
                    if (string.Equals(n, actual, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        object o = Escalar(c, null, "SELECT OBJECT_ID(QUOTENAME(@n)+'.dbo.engRibbonControl')", "@n", n);
                        if (o != null && Convert.ToInt64(o) > 0) lista.Add(n);
                    }
                    catch { }
                }
            }
            catch { }
            return lista;
        }

        // Íconos: los de BrosLMV (prefijo BrosLMV_, con etiquetas de búsqueda desde iconos.json) y los nativos de Comercial.
        // Del catálogo nativo se muestra un solo tamaño por ícono (el de 32) y se omiten los duplicados 16.
        private Dictionary<string, object> ListarIconos()
        {
            var res = new Dictionary<string, object>();
            string dir = CarpetaIconos();
            var propios = new List<object>();
            var nativos = new List<string>();
            if (dir != null)
            {
                var todos = new HashSet<string>(Directory.GetFiles(dir, "*.ico").Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
                foreach (var n in todos.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (n.StartsWith("BrosLMV_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.EndsWith("16") && (todos.Contains(n.Substring(0, n.Length - 2) + "32") || todos.Contains(n.Substring(0, n.Length - 2)))) continue;
                    if (!n.EndsWith("32") && !n.EndsWith("16") && todos.Contains(n + "32")) continue;
                    nativos.Add(n + ".ico");
                }
            }
            string cat = Path.Combine(Rutas.Base, "iconos", "iconos.json");
            if (File.Exists(cat))
            {
                try
                {
                    var j = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(cat, Encoding.UTF8));
                    var lista = j["iconos"] as System.Collections.ArrayList;
                    if (lista != null)
                        foreach (Dictionary<string, object> i in lista)
                        {
                            string archivo = "BrosLMV_" + i["n"] + ".ico";
                            if (dir == null || File.Exists(Path.Combine(dir, archivo)))
                                propios.Add(new object[] { archivo, i["t"] });
                        }
                }
                catch { }
            }
            res["propios"] = propios;
            res["nativos"] = nativos;
            return res;
        }

        // ---------------------------------------------------------------- lectura de un botón existente (modo editar)
        public Dictionary<string, object> Buscar(string appKey)
        {
            using (var c = Abrir()) return Buscar(c, appKey);
        }

        private Dictionary<string, object> Buscar(SqlConnection c, string appKey)
        {
            var ctl = Filas(c, "SELECT TOP 1 ControlID, ControlCaption, ControlDescription, IconFile FROM engRibbonControl WHERE ControlExecute=@e", null, "@e", Ejecuta(appKey));
            if (ctl.Count == 0) return null;
            long id = L(ctl[0]["ControlID"]);
            var filas = Filas(c,
                "SELECT m.RibbonGroupID, g.GroupCaption, t.TabCaption, m.ExtraMenuModuleID, m.IfUserIDIs FROM engRibbonMenu m " +
                "LEFT JOIN engRibbonGroup g ON g.RibbonGroupID=m.RibbonGroupID LEFT JOIN engRibbonTab t ON t.RibbonTabID=g.RibbonTabID WHERE m.ControlID=@i", null, "@i", id);
            var r = new Dictionary<string, object> {
                { "controlId", id }, { "caption", ctl[0]["ControlCaption"] }, { "description", ctl[0]["ControlDescription"] }, { "icon", ctl[0]["IconFile"] },
                { "tab", filas.Count > 0 ? filas[0]["TabCaption"] : null }, { "group", filas.Count > 0 ? filas[0]["GroupCaption"] : null },
                { "modules", filas.Where(f => L(f["ExtraMenuModuleID"]) != 0).Select(f => L(f["ExtraMenuModuleID"])).Distinct().ToList() },
                { "users", filas.Where(f => L(f["IfUserIDIs"]) != 0).Select(f => L(f["IfUserIDIs"])).Distinct().ToList() } };
            return r;
        }

        // ---------------------------------------------------------------- publicar / quitar / deshacer
        private const string SqlCrearHist =
            "IF OBJECT_ID('zzBrosRibbonHist') IS NULL CREATE TABLE zzBrosRibbonHist(" +
            "HistID INT IDENTITY(1,1) PRIMARY KEY, Fecha DATETIME NOT NULL DEFAULT GETDATE(), UserID INT NULL, Ejecuta NVARCHAR(200) NOT NULL, " +
            "Accion NVARCHAR(20) NOT NULL, Antes NVARCHAR(MAX) NULL, Deshecho BIT NOT NULL DEFAULT 0)";

        public List<Dictionary<string, object>> Publicar(BotonSpec s)
        {
            Validar(s);
            var resultados = new List<Dictionary<string, object>>();
            using (var c = Abrir())
            {
                string actual = Convert.ToString(Escalar(c, null, "SELECT DB_NAME()"));
                var destinos = new List<string> { actual };
                foreach (var e in s.Empresas) if (!destinos.Contains(e, StringComparer.OrdinalIgnoreCase)) destinos.Add(e);
                foreach (var db in destinos)
                {
                    var r = new Dictionary<string, object> { { "empresa", db } };
                    try
                    {
                        if (!string.Equals(db, actual, StringComparison.OrdinalIgnoreCase)) c.ChangeDatabase(db);
                        r["controlId"] = PublicarEn(c, s);
                        r["ok"] = true;
                    }
                    catch (Exception ex) { r["ok"] = false; r["error"] = ex.Message; }
                    finally { try { if (!string.Equals(c.Database, actual, StringComparison.OrdinalIgnoreCase)) c.ChangeDatabase(actual); } catch { } }
                    resultados.Add(r);
                }
            }
            return resultados;
        }

        private static void Validar(BotonSpec s)
        {
            if (s == null) throw new Exception("Faltan los datos del botón.");
            if (string.IsNullOrWhiteSpace(s.AppKey) || s.AppKey.Any(ch => !(ch < 128 && (char.IsLetterOrDigit(ch) || ch == '_'))))
                throw new Exception("La clave del botón solo puede llevar letras, números y guion bajo.");
            if (string.IsNullOrWhiteSpace(s.Caption)) throw new Exception("Escribe el nombre del botón.");
            if (string.IsNullOrWhiteSpace(s.TabCaption)) throw new Exception("Elige la pestaña.");
            if (string.IsNullOrWhiteSpace(s.GroupCaption)) throw new Exception("Elige la sección.");
            if (s.Modules.Count * Math.Max(1, s.Users.Count) > 400) throw new Exception("Demasiadas combinaciones de módulos y usuarios; elige grupos o menos módulos.");
        }

        // Publica en la base a la que está conectada c. Devuelve el ControlID. Todo en una transacción.
        private long PublicarEn(SqlConnection c, BotonSpec s)
        {
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    Exec(c, tx, SqlCrearHist);
                    string ejecuta = Ejecuta(s.AppKey);
                    GuardarCopia(c, tx, ejecuta, "publicar");

                    // el botón: se actualiza si ya existe (mismo Ejecutar), si no se crea
                    object ctlId = Escalar(c, tx, "SELECT TOP 1 ControlID FROM engRibbonControl WHERE ControlExecute=@e", "@e", ejecuta);
                    string desc = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim();
                    if (ctlId != null)
                    {
                        Exec(c, tx, "UPDATE engRibbonControl SET ControlCaption=@c, ControlDescription=@d, IconFile=@i WHERE ControlID=@id",
                            "@c", s.Caption.Trim(), "@d", desc, "@i", string.IsNullOrWhiteSpace(s.Icon) ? null : s.Icon, "@id", ctlId);
                    }
                    else
                    {
                        Exec(c, tx,
                            "INSERT engRibbonControl(ControlIDBase,ProductID,ModuleID,ControlCaption,ControlDescription,ControlExecute,IconFile,SystemButton,SystemButtonOrder,SystemButtonBeginGroup,SystemButtonParentID," +
                            "QuickAccessShow,QuickAccessSection,QuickAccessCaption,QuickAccessOrder,Shortcut,ResID,ResIDDescription) " +
                            "VALUES(0,1,0,@c,@d,@e,@i,0,0,0,0,0,NULL,NULL,0,NULL,0,0)",
                            "@c", s.Caption.Trim(), "@d", desc, "@e", ejecuta, "@i", string.IsNullOrWhiteSpace(s.Icon) ? null : s.Icon);
                        ctlId = Escalar(c, tx, "SELECT TOP 1 ControlID FROM engRibbonControl WHERE ControlExecute=@e", "@e", ejecuta);
                    }

                    // dónde aparece: se reemplazan TODAS sus filas por una por (módulo x usuario).
                    // La pestaña y la sección se resuelven POR MÓDULO: una pestaña propia de un módulo (p. ej. «General» de Facturas de compra) solo
                    // sirve a ese módulo, así que un botón para varios módulos puede terminar en grupos de pestañas distintas.
                    Exec(c, tx, "DELETE FROM engRibbonMenu WHERE ControlID=@id", "@id", ctlId);
                    var mods = s.Modules.Count == 0 ? new List<long> { 0 } : s.Modules.Distinct().ToList();
                    var usrs = s.Users.Count == 0 ? new List<long> { 0 } : s.Users.Distinct().ToList();
                    foreach (var m in mods)
                    {
                        object grpId = ResolverGrupo(c, tx, s, m);
                        long orden = L(Escalar(c, tx, "SELECT ISNULL(MAX(ControlOrder),0)+1 FROM engRibbonMenu WHERE RibbonGroupID=@g", "@g", grpId));
                        foreach (var u in usrs)
                            Exec(c, tx,
                                "INSERT engRibbonMenu(RibbonMenuIDBase,RibbonGroupID,ControlID,ControlOrder,ControlType,ExtraMenuModuleID,IfFieldsExist,IfUserIDIs) VALUES(0,@g,@id,@o,1,@m,NULL,@u)",
                                "@g", grpId, "@id", ctlId, "@o", orden, "@m", m, "@u", u);
                    }

                    tx.Commit();
                    return Convert.ToInt64(ctlId);
                }
                catch { try { tx.Rollback(); } catch { } throw; }
            }
        }

        // Devuelve el RibbonGroupID donde va el botón para el módulo m (0 = todos): busca la pestaña por nombre prefiriendo la propia del módulo
        // (ModuleID = m) sobre la compartida (ModuleID = 0); crea la pestaña y/o la sección si no existen.
        private static object ResolverGrupo(SqlConnection c, SqlTransaction tx, BotonSpec s, long m)
        {
            string tabCap = s.TabCaption.Trim(), grpCap = s.GroupCaption.Trim();
            object tabId = Escalar(c, tx,
                "SELECT TOP 1 RibbonTabID FROM engRibbonTab WHERE TabCaption=@c AND (ModuleID=@m OR ModuleID=0) ORDER BY CASE WHEN ModuleID=@m THEN 0 ELSE 1 END, TabOrder",
                "@c", tabCap, "@m", m);
            if (tabId == null)
            {
                Exec(c, tx,
                    "INSERT engRibbonTab(RibbonTabIDBase,ProductID,ModuleID,TabCaption,TabOrder,DRL,Color,ContextCaption,ExtraMenuModuleID,ShowIfSectionModuleIDIs,ResID,IfUserIDIs) " +
                    "VALUES(0,1,0,@c,(SELECT ISNULL(MAX(TabOrder),0)+1 FROM engRibbonTab WHERE TabOrder>=101 OR TabOrder IS NULL),NULL,0,NULL,0,0,0,0)", "@c", tabCap);
                tabId = Escalar(c, tx, "SELECT TOP 1 RibbonTabID FROM engRibbonTab WHERE TabCaption=@c ORDER BY RibbonTabID DESC", "@c", tabCap);
            }
            object grpId = Escalar(c, tx, "SELECT TOP 1 RibbonGroupID FROM engRibbonGroup WHERE RibbonTabID=@t AND GroupCaption=@g ORDER BY GroupOrder", "@t", tabId, "@g", grpCap);
            if (grpId == null)
            {
                Exec(c, tx,
                    "INSERT engRibbonGroup(RibbonGroupIDBase,RibbonTabID,GroupCaption,GroupOrder,ShowOptionButton,ToolTipText,IconFile,ExtraMenuModuleID,IfFieldsExist,ResID,IfUserIDIs) " +
                    "VALUES(0,@t,@g,(SELECT ISNULL(MAX(GroupOrder),0)+1 FROM engRibbonGroup WHERE RibbonTabID=@t),0,NULL,NULL,0,NULL,0,0)", "@t", tabId, "@g", grpCap);
                grpId = Escalar(c, tx, "SELECT TOP 1 RibbonGroupID FROM engRibbonGroup WHERE RibbonTabID=@t AND GroupCaption=@g ORDER BY RibbonGroupID DESC", "@t", tabId, "@g", grpCap);
            }
            return grpId;
        }

        // Copia del estado actual de un botón (control + filas de menú) para poder deshacer. Antes=NULL si el botón no existía.
        private void GuardarCopia(SqlConnection c, SqlTransaction tx, string ejecuta, string accion)
        {
            string antes = null;
            var ctl = Filas(c, "SELECT * FROM engRibbonControl WHERE ControlExecute=@e", tx, "@e", ejecuta);
            if (ctl.Count > 0)
            {
                var menu = Filas(c, "SELECT * FROM engRibbonMenu WHERE ControlID=@i", tx, "@i", ctl[0]["ControlID"]);
                antes = Json.Serialize(new Dictionary<string, object> { { "control", ctl[0] }, { "menu", menu } });
            }
            Exec(c, tx, "INSERT zzBrosRibbonHist(UserID,Ejecuta,Accion,Antes) VALUES(@u,@e,@a,@b)", "@u", _userId, "@e", ejecuta, "@a", accion, "@b", antes);
        }

        public List<Dictionary<string, object>> Quitar(string appKey, List<string> empresas)
        {
            var resultados = new List<Dictionary<string, object>>();
            using (var c = Abrir())
            {
                string actual = Convert.ToString(Escalar(c, null, "SELECT DB_NAME()"));
                var destinos = new List<string> { actual };
                if (empresas != null) foreach (var e in empresas) if (!destinos.Contains(e, StringComparer.OrdinalIgnoreCase)) destinos.Add(e);
                foreach (var db in destinos)
                {
                    var r = new Dictionary<string, object> { { "empresa", db } };
                    try
                    {
                        if (!string.Equals(db, actual, StringComparison.OrdinalIgnoreCase)) c.ChangeDatabase(db);
                        using (var tx = c.BeginTransaction())
                        {
                            try
                            {
                                Exec(c, tx, SqlCrearHist);
                                string ejecuta = Ejecuta(appKey);
                                GuardarCopia(c, tx, ejecuta, "quitar");
                                Exec(c, tx, "DELETE m FROM engRibbonMenu m JOIN engRibbonControl k ON k.ControlID=m.ControlID WHERE k.ControlExecute=@e", "@e", ejecuta);
                                Exec(c, tx, "DELETE FROM engRibbonControl WHERE ControlExecute=@e", "@e", ejecuta);
                                tx.Commit();
                                r["ok"] = true;
                            }
                            catch { try { tx.Rollback(); } catch { } throw; }
                        }
                    }
                    catch (Exception ex) { r["ok"] = false; r["error"] = ex.Message; }
                    finally { try { if (!string.Equals(c.Database, actual, StringComparison.OrdinalIgnoreCase)) c.ChangeDatabase(actual); } catch { } }
                    resultados.Add(r);
                }
            }
            return resultados;
        }

        // Deshace el último cambio (publicar o quitar) de ese botón en la empresa activa: restaura el control y sus filas de menú tal como estaban.
        public string DeshacerUltimo(string appKey)
        {
            using (var c = Abrir())
            using (var tx = c.BeginTransaction())
            {
                try
                {
                    Exec(c, tx, SqlCrearHist);
                    string ejecuta = Ejecuta(appKey);
                    var h = Filas(c, "SELECT TOP 1 HistID, Antes, Accion FROM zzBrosRibbonHist WHERE Ejecuta=@e AND Deshecho=0 ORDER BY HistID DESC", tx, "@e", ejecuta);
                    if (h.Count == 0) { tx.Rollback(); return "No hay cambios que deshacer para este botón."; }
                    Exec(c, tx, "DELETE m FROM engRibbonMenu m JOIN engRibbonControl k ON k.ControlID=m.ControlID WHERE k.ControlExecute=@e", "@e", ejecuta);
                    Exec(c, tx, "DELETE FROM engRibbonControl WHERE ControlExecute=@e", "@e", ejecuta);
                    string antes = h[0]["Antes"] as string;
                    if (!string.IsNullOrEmpty(antes))
                    {
                        var j = Json.Deserialize<Dictionary<string, object>>(antes);
                        var ctl = (Dictionary<string, object>)j["control"];
                        Exec(c, tx,
                            "INSERT engRibbonControl(ControlIDBase,ProductID,ModuleID,ControlCaption,ControlDescription,ControlExecute,IconFile,SystemButton,SystemButtonOrder,SystemButtonBeginGroup,SystemButtonParentID," +
                            "QuickAccessShow,QuickAccessSection,QuickAccessCaption,QuickAccessOrder,Shortcut,ResID,ResIDDescription) " +
                            "VALUES(0,1,0,@c,@d,@e,@i,0,0,0,0,0,NULL,NULL,0,NULL,0,0)",
                            "@c", ctl["ControlCaption"], "@d", ctl["ControlDescription"], "@e", ejecuta, "@i", ctl["IconFile"]);
                        object nuevo = Escalar(c, tx, "SELECT TOP 1 ControlID FROM engRibbonControl WHERE ControlExecute=@e", "@e", ejecuta);
                        foreach (Dictionary<string, object> m in (System.Collections.ArrayList)j["menu"])
                            Exec(c, tx,
                                "INSERT engRibbonMenu(RibbonMenuIDBase,RibbonGroupID,ControlID,ControlOrder,ControlType,ExtraMenuModuleID,IfFieldsExist,IfUserIDIs) VALUES(0,@g,@id,@o,1,@m,NULL,@u)",
                                "@g", m["RibbonGroupID"], "@id", nuevo, "@o", m["ControlOrder"], "@m", m["ExtraMenuModuleID"] ?? 0, "@u", m["IfUserIDIs"] ?? 0);
                    }
                    Exec(c, tx, "UPDATE zzBrosRibbonHist SET Deshecho=1 WHERE HistID=@h", "@h", h[0]["HistID"]);
                    tx.Commit();
                    return "Listo: se restauró el botón como estaba antes del último cambio.";
                }
                catch { try { tx.Rollback(); } catch { } throw; }
            }
        }
    }
}
