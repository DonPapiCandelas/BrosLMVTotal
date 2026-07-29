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

// Program.cs -- BrosLMV.Runner (T3.3, prototipo)
//
// Ejecuta UN botón (AppKey) marcado "# job: safe-offline" sin abrir Comercial, para usarse
// desde Tarea Programada de Windows. Reusa el MISMO ScriptContext/ScriptRunner/Datos que usa
// el addon dentro de Comercial (enlazados desde src\, no reescritos) -- lo único distinto es
// cómo se obtiene XEngineLib: aquí se crea un XEngine standalone (fuera de ComercialSP.exe)
// en vez de recibirlo ya vivo de Comercial.
//
// Uso:
//   BrosLMV.Runner.exe --appkey REPORTE_EJECUTIVO [--userid 1] [--conn "Server=...;Database=...;User Id=...;Password=...;"]
//
// Si no se pasa --conn, se usa la misma cadena de respaldo cifrada que ya usa el addon
// (Rutas.ConnStr(), configurada por el instalador) -- así no hay que duplicar credenciales
// para el modo headless.
//
// Alcance v0 (prototipo): solo scripts 'sql' y 'csharp'. Python (host fuera de proceso) queda
// pendiente -- ese camino usa UiPump para regresar al hilo de Comercial, que aquí no existe.

using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Reflection;

// Prototipo T3.3 -- aún no se distribuye con el instalador; versión propia, independiente
// de BrosLMVClsMain.dll (que sí va en 2.36.0).
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyTitle("BrosLMV.Runner - Programador headless (prototipo T3.3)")]

namespace BrosLMV.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string appKey = null, connOverride = null, bd = null;
            int userId = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--appkey": appKey = ArgSiguiente(args, ref i); break;
                    case "--userid": int.TryParse(ArgSiguiente(args, ref i), out userId); break;
                    case "--conn":   connOverride = ArgSiguiente(args, ref i); break;
                    case "--bd":     bd = ArgSiguiente(args, ref i); break;
                    default:
                        Console.Error.WriteLine("Argumento desconocido: " + args[i]);
                        return Uso();
                }
            }
            if (string.IsNullOrEmpty(appKey)) return Uso();

            Console.WriteLine("BrosLMV.Runner v" + typeof(Program).Assembly.GetName().Version);

            object xe;
            try
            {
                string cs;
                if (!string.IsNullOrEmpty(connOverride))
                {
                    cs = connOverride;
                }
                else
                {
                    // Rutas.ConnStr() (la misma cadena cifrada que usa el addon) trae SOLO
                    // servidor+usuario+password A PROPÓSITO: dentro de Comercial, la base se
                    // completa con el DataLayer de la sesión viva de la empresa activa (ver
                    // ResolverCadena() en Scripting.cs). Aquí no hay sesión viva -- hay que
                    // decirle explícitamente qué empresa/base usar.
                    string baseCs = Rutas.ConnStr();
                    if (string.IsNullOrEmpty(baseCs))
                    {
                        Console.Error.WriteLine("No hay cadena de conexión disponible (ni --conn, ni " + Rutas.CredFile + "). " +
                            "Configura la conexión de respaldo con el instalador (\"Probar conexión\") antes de usar el Runner.");
                        return 1;
                    }
                    if (string.IsNullOrEmpty(bd))
                    {
                        Console.Error.WriteLine("Falta --bd <empresa> -- fuera de Comercial no hay una empresa activa de la que inferir " +
                            "la base de datos (la cadena de respaldo cifrada solo trae servidor+usuario+password, sin base).");
                        return 1;
                    }
                    cs = baseCs.TrimEnd(';') + ";Database=" + bd;
                }
                xe = CrearXEngineStandalone(cs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR al crear XEngine standalone: " + ex.Message);
                return 1;
            }

            var ctx = new ScriptContext(userId, xe);
            string emp = "";
            try { emp = ctx.Empresa(); } catch { }
            Console.WriteLine("Conectado. Empresa activa: " + (string.IsNullOrEmpty(emp) ? "(desconocida)" : emp));

            string codigo = null;
            try { if (ctx.BrosScriptsDisponible()) codigo = ctx.BrosCargar(appKey); } catch { }

            // Integridad (T2.3): igual que ClsMain.cs, solo aplica a scripts que vinieron de
            // zzBrosScript (los de archivo no tienen hash guardado que comparar). Best-effort:
            // BrosVerificarIntegridad nunca lanza; null = nada que verificar, sigue igual que
            // siempre. A diferencia de Comercial (donde el usuario ve el MessageBox y decide),
            // aquí NO hay nadie mirando -- así que un mismatch de hash o falta de aprobación
            // DETIENE la ejecución en vez de solo avisar, hasta que alguien lo revise a mano.
            if (!string.IsNullOrEmpty(codigo))
            {
                var integridad = ctx.BrosVerificarIntegridad(appKey, codigo);
                if (integridad != null)
                {
                    if (integridad.ExigeAprobacion && !integridad.EstaAprobado)
                    {
                        Console.Error.WriteLine("\"" + appKey + "\" requiere aprobación antes de ejecutarse " +
                            "(preferencia \"ExigirAprobacion\" activa para el usuario " + userId + "). " +
                            "Ábrelo en la Consola BrosLMV y apruébalo antes de programarlo headless.");
                        return 6;
                    }
                    if (integridad.TieneHash && !integridad.Coincide)
                    {
                        try
                        {
                            Datos.RegistrarEjecucion(emp, ctx.ModuloActivo(), userId, appKey,
                                "runner-integridad", 0, 0, "ERROR",
                                "Hash de Código no coincide con HashSHA256 guardado -- modificado por fuera de la Consola. " +
                                "Ejecución headless detenida (a diferencia del botón en Comercial, aquí no hay nadie para confirmar).",
                                ctx);
                        }
                        catch { }
                        Console.Error.WriteLine("\"" + appKey + "\" fue modificado por fuera de la Consola BrosLMV " +
                            "(el código no coincide con la última vez que se guardó desde ahí). No se ejecuta headless sin revisión -- " +
                            "ábrelo en la Consola, confirma el cambio y guárdalo de nuevo (eso actualiza el hash).");
                        return 7;
                    }
                }
            }

            bool esSql;
            if (!string.IsNullOrEmpty(codigo))
            {
                esSql = EsSql(codigo);
            }
            else
            {
                esSql = false;
                foreach (var dir in new[] { Rutas.ScriptsDe(emp), Rutas.Scripts })
                {
                    foreach (var ext in new[] { ".sql", ".ctx", ".csx" })
                    {
                        string p = Path.Combine(dir, appKey + ext);
                        if (File.Exists(p)) { codigo = File.ReadAllText(p); esSql = ext == ".sql"; break; }
                    }
                    if (!string.IsNullOrEmpty(codigo)) break;
                }
            }

            if (string.IsNullOrEmpty(codigo))
            {
                Console.Error.WriteLine("AppKey desconocido: " + appKey + " (no está en zzBrosScript de \"" + emp + "\" ni como archivo).");
                return 3;
            }
            if (EsPython(codigo))
            {
                Console.Error.WriteLine("\"" + appKey + "\" es un script Python -- BrosLMV.Runner aún no lo soporta (solo SQL/C#).");
                return 4;
            }
            if (!TieneMarcaJobSafe(codigo))
            {
                Console.Error.WriteLine("\"" + appKey + "\" no trae el marcador \"# job: safe-offline\" en las primeras líneas -- " +
                    "no se ejecuta fuera de Comercial sin marcarlo explícitamente. Ábrelo en la Consola y agrégalo si de verdad es seguro correrlo sin supervisión.");
                return 5;
            }

            var sw = Stopwatch.StartNew();
            string res; string origen;
            if (esSql)
            {
                origen = "runner-sql";
                try { res = ctx.EjecutarSql(codigo); }
                catch (Exception ex) { res = "ERROR: " + ex.Message; }
            }
            else
            {
                origen = "runner-csharp";
                res = ScriptRunner.Ejecutar(codigo, ctx); // "" = éxito
            }
            sw.Stop();

            bool error = esSql ? res.StartsWith("ERROR", StringComparison.Ordinal) : res != "";
            try
            {
                Datos.RegistrarEjecucion(emp, ctx.ModuloActivo(), userId, appKey,
                    origen, sw.ElapsedMilliseconds, ctx.FilasAfectadas,
                    error ? "ERROR" : "OK", res, ctx);
            }
            catch { }

            if (error)
            {
                Console.Error.WriteLine(res);
                return 1;
            }
            if (!string.IsNullOrEmpty(res)) Console.WriteLine(res);
            Console.WriteLine("OK (" + sw.ElapsedMilliseconds + " ms, " + ctx.FilasAfectadas + " fila(s) afectada(s)).");
            return 0;
        }

        // Equivalente C# del bootstrap standalone de XEngine: crea XengineLib.clsMain SIN que
        // Comercial lo esté hospedando, y le conecta el DataLayer a mano. Usa las mismas
        // ayudas de late-binding (Com.*) que ya usa ScriptContext para hablar con XEngine.
        private static object CrearXEngineStandalone(string connStr)
        {
            var b = new SqlConnectionStringBuilder(connStr);

            Type t = Type.GetTypeFromProgID("XengineLib.clsMain", throwOnError: false);
            if (t == null)
                throw new Exception("No se encontró \"XengineLib.clsMain\" registrado en este equipo (¿Comercial/XEngine instalado?).");
            object xe = Activator.CreateInstance(t);

            if (!Com.SetProp(xe, "OwnedBusinessEntityID", 1))
                throw new Exception("No se pudo setear OwnedBusinessEntityID: " + Com.LastError);
            if (!Com.SetProp(xe, "InternetConnection", true))
                throw new Exception("No se pudo setear InternetConnection: " + Com.LastError);
            if (!Com.SetProp(xe, "LICENCE_CONTPAQ", false))
                throw new Exception("No se pudo setear LICENCE_CONTPAQ: " + Com.LastError);

            object dl = Com.GetProp(xe, "DataLayer");
            if (dl == null)
                throw new Exception("XEngine no expuso DataLayer: " + Com.LastError);

            Com.Call(dl, "CreateConnectionMSSQL", new object[] { b.DataSource, b.InitialCatalog, b.UserID, b.Password });
            if (!string.IsNullOrEmpty(Com.LastError))
                throw new Exception("CreateConnectionMSSQL falló: " + Com.LastError);

            Com.Call(xe, "SetDataLayers", new object[0]);
            if (!string.IsNullOrEmpty(Com.LastError))
                throw new Exception("SetDataLayers falló: " + Com.LastError);

            return xe;
        }

        // Mismo criterio de detección que HostClient.EsPython/EsSql (no se enlaza HostClient.cs
        // completo aquí para no arrastrar WebView2/Protobuf a un ejecutable de consola).
        private const int LineasCabeceraARevisar = 10;

        private static bool EsPython(string codigo)
        {
            int revisadas = 0;
            foreach (var raw in codigo.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string low = line.ToLowerInvariant();
                if (low.Contains("broslmv:python") || low.StartsWith("#py")
                    || low.StartsWith("# lang: python") || low.StartsWith("#lang:python"))
                    return true;
                if (++revisadas >= LineasCabeceraARevisar) break;
            }
            return codigo.Contains("from broslmv import ctx");
        }

        private static bool EsSql(string codigo)
        {
            int revisadas = 0;
            foreach (var raw in codigo.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string low = line.ToLowerInvariant();
                if (low.Contains("broslmv:sql") || low.StartsWith("--sql")
                    || low.StartsWith("-- lang: sql") || low.StartsWith("# lang: sql") || low.StartsWith("#sql"))
                    return true;
                if (++revisadas >= LineasCabeceraARevisar) break;
            }
            return false;
        }

        // Marcador de seguridad de T3.3: un script solo corre headless si declara explícitamente
        // que es seguro correrlo sin supervisión (sin Comercial abierto, sin usuario viendo el
        // resultado). Se busca en cualquiera de las primeras líneas, junto a los otros marcadores.
        private static bool TieneMarcaJobSafe(string codigo)
        {
            int revisadas = 0;
            foreach (var raw in codigo.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.ToLowerInvariant().Contains("job: safe-offline")) return true;
                if (++revisadas >= LineasCabeceraARevisar) break;
            }
            return false;
        }

        private static string ArgSiguiente(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Falta el valor para " + args[i]);
            return args[++i];
        }

        private static int Uso()
        {
            Console.Error.WriteLine(
                "Uso: BrosLMV.Runner.exe --appkey <NOMBRE> --bd <EMPRESA> [--userid <N>] [--conn \"<cadena de conexión>\"]\n\n" +
                "Ejecuta el botón <NOMBRE> (marcado \"# job: safe-offline\") sin abrir Comercial.\n" +
                "Sin --conn, usa la cadena de respaldo cifrada que ya usa el addon (configurada por el instalador)\n" +
                "combinada con --bd (la empresa/base de datos -- aquí no hay sesión viva de la que inferirla).");
            return 2;
        }
    }
}
