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

// ClsMain.cs
// COM server que XEngine (CONTPAQi Comercial) instancia via ProgID "BrosLMV.clsMain".
// Se ejecuta de forma autonoma, en proceso, sin servicios de licencia externos.
//
// XEngine: lee ControlExecute "BrosLMV.<AppKey>" -> CreateObject("BrosLMV.clsMain")
//          -> setea XEngineLib/UserID -> llama ExecuteFunction("<AppKey>").

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyVersion("2.24.0.0")]
[assembly: AssemblyTitle("BrosLMV - Botones CONTPAQi")]

namespace BrosLMV
{
    // GUID fijo: el registro COM no cambia entre recompilaciones.
    [Guid("E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ProgId("BrosLMV.clsMain")]
    [ComVisible(true)]
    public class clsMain
    {
        // Resolutor de ensamblados: cuando el CLR no encuentre una DLL (Roslyn o sus
        // dependencias), la cargamos desde la carpeta de NUESTRA DLL ignorando version.
        // Asi evitamos binding redirects en ComercialSP.exe.config.
        static clsMain()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += delegate (object s, ResolveEventArgs e)
                {
                    try
                    {
                        string dir    = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        string simple = new AssemblyName(e.Name).Name;
                        string path   = Path.Combine(dir, simple + ".dll");
                        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                    }
                    catch { return null; }
                };
            }
            catch { }
        }

        // Consola modeless: una sola instancia viva, compartida entre invocaciones del COM.
        private static BrosConsola _consola;

        // Propiedades que XEngine puede setear antes de ExecuteFunction.
        public object XEngineLib       { get; set; } // motor de CONTPAQi (clave)
        public int    ModuleID         { get; set; }
        public int    UserID           { get; set; }
        public int    BusinessEntityID { get; set; }
        public object IDs              { get; set; } // normalmente null
        public bool   MustRefreshList  { get; set; }

        // Punto de entrada que invoca XEngine. 'appKey' = texto despues del 1er punto.
        public void ExecuteFunction(string appKey)
        {
            try
            {
                Rutas.AsegurarCarpetas();
                UiPump.Asegurar(); // deja lista la bomba de marshaling para botones Python
                switch (appKey)
                {
                    case "CONSOLA":
                        // Abre la consola de scripts MODELESS: se puede minimizar y seguir
                        // trabajando en Comercial. Una sola instancia; si ya está abierta,
                        // se restaura y se trae al frente.
                        if (_consola != null && !_consola.IsDisposed)
                        {
                            if (_consola.WindowState == FormWindowState.Minimized)
                                _consola.WindowState = FormWindowState.Normal;
                            _consola.Activate();
                            _consola.BringToFront();
                        }
                        else
                        {
                            _consola = new BrosConsola(UserID, XEngineLib);
                            _consola.FormClosed += delegate { _consola = null; };
                            _consola.Show(); // modeless (no bloquea, no using: se auto-libera al cerrar)
                        }
                        break;

                    case "PRUEBA":
                        MessageBox.Show(
                            "BrosLMV OK.\nModuleID=" + ModuleID + "  UserID=" + UserID,
                            "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;

                    default:
                        // Cualquier otro AppKey -> ejecuta scripts\<AppKey>.csx (sin recompilar).
                        EjecutarScript(appKey);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error en BrosLMV.ExecuteFunction(" + appKey + "):\n\n" + ex,
                    "BrosLMV - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EjecutarScript(string appKey)
        {
            var ctx = new ScriptContext(UserID, XEngineLib);
            string emp = SafeEmpresa(ctx);

            // 1) Buscar el script en SQL (zzBrosScript de la empresa activa). Asi se
            //    comparte entre todas las terminales de esa empresa.
            string codigo = null;
            try { if (ctx.BrosScriptsDisponible()) codigo = ctx.BrosCargar(appKey); } catch { }

            // 1b) Eventos nativos de Comercial (p. ej. Propiedades > Avanzado > Evento=Guardar
            //     con Funcion="BrosLMV.<Script>_[DocumentID]") sustituyen el token como texto
            //     ANTES de invocar, asi que el AppKey llega literal como "<Script>_12345". Si no
            //     hubo match exacto, se intenta con el nombre base + se expone el numero a ctx.
            if (string.IsNullOrEmpty(codigo))
            {
                var mEvento = Regex.Match(appKey, @"^(.+)_(\d+)$");
                if (mEvento.Success)
                {
                    string baseKey = mEvento.Groups[1].Value;
                    try { if (ctx.BrosScriptsDisponible()) codigo = ctx.BrosCargar(baseKey); } catch { }
                    if (!string.IsNullOrEmpty(codigo))
                    {
                        appKey = baseKey; // para el resto del flujo (lookup de archivo, auditoria, etc.)
                        ctx.EventoId = long.Parse(mEvento.Groups[2].Value);
                    }
                }
            }

            // 2) Compatibilidad: si no esta en SQL, buscar archivo (empresa y luego raiz).
            //    .py = Python (host v3.0); .ctx/.csx = C# (Roslyn, en proceso).
            bool esPython = false, esSql = false;
            if (string.IsNullOrEmpty(codigo))
            {
                foreach (var dir in new[] { Rutas.ScriptsDe(emp), Rutas.Scripts })
                {
                    foreach (var ext in new[] { ".py", ".sql", ".ctx", ".csx" })
                    {
                        string p = Path.Combine(dir, appKey + ext);
                        if (File.Exists(p))
                        {
                            try { codigo = File.ReadAllText(p); } catch { }
                            esPython = (ext == ".py");
                            esSql = (ext == ".sql");
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(codigo)) break;
                }
            }

            // Scripts en SQL (zzBrosScript) declaran su lenguaje con un marcador en la 1a linea.
            if (!esPython && !esSql && !string.IsNullOrEmpty(codigo))
            {
                esPython = HostClient.EsPython(codigo);
                if (!esPython) esSql = HostClient.EsSql(codigo);
            }

            if (string.IsNullOrEmpty(codigo))
            {
                MessageBox.Show(
                    "AppKey desconocido: " + appKey + "\n\nNo esta en zzBrosScript de \"" + emp +
                    "\" ni como archivo.",
                    "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (esPython) { EjecutarPython(appKey, codigo, ctx, emp); return; }
            if (esSql)    { EjecutarSql(appKey, codigo, ctx, emp); return; }

            // --- Script C# (Roslyn, en proceso) ---
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string res = ScriptRunner.Ejecutar(codigo, ctx);
            sw.Stop();
            try
            {
                Datos.RegistrarEjecucion(emp, ctx.ModuloActivo(), UserID, appKey,
                    "boton", sw.ElapsedMilliseconds, ctx.FilasAfectadas,
                    res == "" ? "OK" : "ERROR", res);
            }
            catch { }
            if (res != "")
                MessageBox.Show(res, "Error en script " + appKey,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // --- Script Python (host v3.0, fuera de proceso) ---
        private void EjecutarPython(string appKey, string codigo, ScriptContext ctx, string emp)
        {
            // Evita lanzar el mismo botón dos veces mientras la primera ejecución sigue en
            // curso (doble clic / impaciencia) -- cada ejecución superpuesta compite por el
            // mismo hilo de Comercial vía UiPump y puede disparar el "busy" nativo de Windows.
            if (!GuardiaEjecucion.TryEntrar(appKey))
            {
                MessageBox.Show("\"" + appKey + "\" ya se está ejecutando. Espera a que termine antes de volver a hacer clic.",
                    "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Congelar el contexto del botón AQUI, en el hilo de Comercial (rápido, sin
            // I/O): igual que antes, el script ve el módulo/selección del momento del clic.
            var hctx = new HostClient.Contexto
            {
                AppKey      = appKey,
                Empresa     = emp,
                Servidor    = ctx.ServidorActivo(),
                BaseDatos   = emp,                 // la BD activa; el host la usa para el SQL
                UserId      = ctx.UserIdReal(),    // UserID del COM viene 0; el real está en ctx.erp
                ModuleId    = ctx.ModuloActivo(),
                Language    = "python",
                SelectedIds = ctx.GetSelectedIds().ToArray(),
                FilaActiva  = ctx.GetFilaActiva(),
            };
            int timeoutMs = HostClient.TimeoutMsFromHeader(codigo);

            // A partir de aquí, TODO corre en segundo plano: Comercial no se queda
            // esperando bloqueado (así una ventana Python interactiva -crear un
            // documento, etc.- no congela Comercial ni impide abrir otros botones).
            // Las llamadas reales a ctx.query/ctx.erp (dentro de CtxSqlRunner/CtxErpRunner)
            // se remiten de vuelta al hilo de Comercial vía UiPump: siguen siendo el
            // único hilo que toca el COM de CONTPAQi, solo que Comercial ya no espera.
            System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                HostClient.Resultado r;
                try
                {
                    r = HostClient.EjecutarPython(codigo, hctx, timeoutMs: timeoutMs,
                        sqlRunner: new CtxSqlRunner(ctx), erpRunner: new CtxErpRunner(ctx));
                }
                catch (Exception ex)
                {
                    r = new HostClient.Resultado { Exito = false, CodigoError = "BOTON_PYTHON_ERROR", MensajeError = ex.Message };
                }
                sw.Stop();

                // Auditoría + diálogos: de vuelta al hilo de Comercial (SQLite/MessageBox
                // conviene mostrarlos siempre desde ahí, no desde un hilo del ThreadPool).
                UiPump.Invoke(() =>
                {
                    try
                    {
                        Datos.RegistrarEjecucion(emp, ctx.ModuloActivo(), UserID, appKey,
                            "boton-python", sw.ElapsedMilliseconds, r.FilasAfectadas,
                            r.Exito ? "OK" : "ERROR",
                            r.Exito ? r.Valor : (r.CodigoError + ": " + r.MensajeError));
                    }
                    catch { }

                    if (!r.Exito)
                        MessageBox.Show(r.MensajeError + "\n\n[" + r.CodigoError + "]",
                            "Error en script Python " + appKey, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else if (!string.IsNullOrEmpty(r.Valor))
                        MessageBox.Show(r.Valor, "BrosLMV - " + appKey,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
                GuardiaEjecucion.Salir(appKey);
            });
        }

        // --- Script SQL crudo (tipo 'sql', corrido por la conexion viva) ---
        private void EjecutarSql(string appKey, string codigo, ScriptContext ctx, string emp)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string res;
            try { res = ctx.EjecutarSql(codigo); }
            catch (Exception ex) { res = "ERROR: " + ex.Message; }
            sw.Stop();

            bool error = res.StartsWith("ERROR");
            try
            {
                Datos.RegistrarEjecucion(emp, ctx.ModuloActivo(), UserID, appKey,
                    "boton-sql", sw.ElapsedMilliseconds, ctx.FilasAfectadas,
                    error ? "ERROR" : "OK", res);
            }
            catch { }

            MessageBox.Show(res, "BrosLMV SQL - " + appKey, MessageBoxButtons.OK,
                error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }

        private static string SafeEmpresa(ScriptContext ctx)
        { try { return ctx.Empresa(); } catch { return ""; } }
    }

    // =====================================================
    //   UiPump: bombeo al hilo de UI de Comercial
    // =====================================================
    // Los botones Python corren su intercambio con el host EN SEGUNDO PLANO (para no
    // congelar Comercial mientras la ventana está abierta), pero XEngineLib (el COM de
    // CONTPAQi) solo debe tocarse desde UN hilo a la vez -- si dos hilos lo llamaran a
    // la vez (el nuestro en segundo plano + el propio Comercial reaccionando a un clic
    // del usuario) el riesgo es corrupcion/crash del COM, peor que la congelada actual.
    // Por eso CADA llamada real a ctx.query/ctx.erp se reenvia (Invoke) a ESTE control,
    // creado una sola vez en el hilo de Comercial: sigue siendo el UNICO hilo que toca
    // el COM, pero Comercial ya no se queda esperando bloqueado mientras tanto.
    internal static class UiPump
    {
        private static Control _bomba;

        // Debe llamarse desde el hilo de Comercial (p. ej. al entrar a ExecuteFunction).
        // Idempotente: la segunda vez en adelante no hace nada.
        internal static void Asegurar()
        {
            if (_bomba != null && !_bomba.IsDisposed) return;
            var f = new Form
            {
                ShowInTaskbar = false,
                Opacity = 0,
                Width = 0,
                Height = 0,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000)
            };
            var h = f.Handle; // fuerza la creacion del HWND sin necesidad de Show()
            _bomba = f;
        }

        // Ejecuta f() en el hilo de Comercial y devuelve su resultado, sin importar
        // desde que hilo se llame Invoke. Si la bomba no esta lista (no debería pasar
        // tras Asegurar()), corre f() directo como respaldo de seguridad.
        internal static T Invoke<T>(Func<T> f)
        {
            var b = _bomba;
            if (b == null || b.IsDisposed || !b.IsHandleCreated) return f();
            if (!b.InvokeRequired) return f(); // ya estamos en el hilo correcto
            return (T)b.Invoke(f);
        }

        internal static void Invoke(Action a) { Invoke<object>(() => { a(); return null; }); }
    }
}
