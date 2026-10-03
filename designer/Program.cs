using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrosLMV.Disenador
{
    static class Program
    {
        static string Arg(string[] a, string nombre) { for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], nombre, StringComparison.OrdinalIgnoreCase)) return a[i + 1]; return null; }

        [STAThread]
        static int Main(string[] args)
        {
            string bd = Arg(args, "--bd"), conn = Arg(args, "--conn");
            int empresa = int.TryParse(Arg(args, "--empresa"), out var e1) ? e1 : 1, userId = int.TryParse(Arg(args, "--userid"), out var u1) ? u1 : 1;
            long formato = long.TryParse(Arg(args, "--formato"), out var f1) ? f1 : 0;
            string prueba = Arg(args, "--prueba"), salida = Arg(args, "--salida");

            string cs = conn;
            if (string.IsNullOrEmpty(cs))
            {
                string baseCs = Rutas.ConnStr();
                if (string.IsNullOrEmpty(baseCs)) return Error("No hay cadena de conexión disponible. Configura la conexión con el instalador de BrosLMV (\"Probar conexión\") o pasa --conn.", prueba != null);
                if (string.IsNullOrEmpty(bd)) return Error("Falta --bd <base de datos de la empresa>.", prueba != null);
                cs = baseCs.TrimEnd(';') + ";Database=" + bd;
            }
            if (cs.IndexOf("TrustServerCertificate", StringComparison.OrdinalIgnoreCase) < 0) cs = cs.TrimEnd(';') + ";TrustServerCertificate=True";
            if (cs.IndexOf("Encrypt", StringComparison.OrdinalIgnoreCase) < 0) cs = cs.TrimEnd(';') + ";Encrypt=False";
            Motor.Iniciar(new CtxShim { Cs = cs, erp = { OwnedBusinessEntityId = empresa, UserId = userId } }, empresa, userId);

            if (prueba != null)        // modo de pruebas: ejecuta una acción del puente y escribe el resultado, sin ventana
            {
                string json;
                try
                {
                    var p = Json.Parse(prueba);
                    string accion = p.ContainsKey("action") ? Convert.ToString(p["action"]) : "";
                    var carga = p.ContainsKey("payload") ? p["payload"] as Dictionary<string, object> : null;
                    json = Json.Serialize(new Dictionary<string, object> { ["ok"] = true, ["data"] = Motor.Despachar(accion, carga ?? new Dictionary<string, object>()) });
                }
                catch (Exception ex) { json = Json.Serialize(new Dictionary<string, object> { ["ok"] = false, ["error"] = ex.Message }); }
                if (!string.IsNullOrEmpty(salida)) File.WriteAllText(salida, json, new UTF8Encoding(false)); else Console.WriteLine(json);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log("Inicio: bd=" + bd + " formato=" + formato);
            Application.Run(new Ventana(formato, bd ?? ""));
            Log("Fin normal");
            return 0;
        }

        public static void Log(string m)
        {
            try { File.AppendAllText(Path.Combine(Directory.Exists(Rutas.Logs) ? Rutas.Logs : Path.GetTempPath(), "Disenador_" + DateTime.Now.ToString("yyyyMMdd") + ".log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + m + Environment.NewLine); } catch { }
        }

        static int Error(string m, bool consola) { if (consola) Console.Error.WriteLine(m); else MessageBox.Show(m, "Diseñador de formatos", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
    }

    sealed class Ventana : Form
    {
        readonly WebView2 wv = new WebView2 { Dock = DockStyle.Fill };
        readonly long formato; readonly string bd, perfil = Path.Combine(Path.GetTempPath(), "BrosLMV_Disenador_" + Guid.NewGuid().ToString("N"));

        public Ventana(long formato, string bd)
        {
            this.formato = formato; this.bd = bd;
            Text = "Diseñador de formatos · BrosLMV" + (bd != "" ? "  —  " + bd : "");
            Width = 1440; Height = 900; MinimumSize = new System.Drawing.Size(1000, 640); StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(wv);
            Load += async (s, e) => await Iniciar();
            FormClosed += (s, e) => { Program.Log("Ventana cerrada (" + e.CloseReason + ")"); try { wv.Dispose(); } catch { } try { Directory.Delete(perfil, true); } catch { } };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Program.Log("Cerrando: " + e.CloseReason);
            base.OnFormClosing(e);
        }

        async Task Iniciar()
        {
            try
            {
                var op = new CoreWebView2EnvironmentOptions();
                string dbg = Environment.GetEnvironmentVariable("BROSLMV_DEBUG_PORT");
                if (!string.IsNullOrEmpty(dbg)) op.AdditionalBrowserArguments = "--remote-debugging-port=" + dbg;
                var env = await CoreWebView2Environment.CreateAsync(null, perfil, op);
                await wv.EnsureCoreWebView2Async(env);
                wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                wv.CoreWebView2.WebMessageReceived += (s, ev) => Mensaje(ev);
                if (Directory.Exists(Rutas.Lib)) wv.CoreWebView2.SetVirtualHostNameToFolderMapping("broslmv.local", Rutas.Lib, CoreWebView2HostResourceAccessKind.Allow);
                string pagina;
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("pagina.html")) using (var rd = new StreamReader(st, Encoding.UTF8)) pagina = rd.ReadToEnd();
                pagina = pagina.Replace("__INICIO__", "{\"formato\":" + formato + ",\"bd\":" + Json.Serialize(bd) + "}");
                wv.CoreWebView2.NavigateToString(pagina);
            }
            catch (Exception ex) { Program.Log("ERROR al iniciar: " + ex); MessageBox.Show("No se pudo iniciar el Diseñador: " + ex.Message, "Diseñador de formatos", MessageBoxButtons.OK, MessageBoxIcon.Error); Close(); }
        }

        void Responder(string reqId, bool ok, object data, string error)
        {
            try
            {
                string json = Json.Serialize(new Dictionary<string, object> { ["reqId"] = reqId, ["ok"] = ok, ["data"] = data, ["error"] = error });
                if (InvokeRequired) BeginInvoke(new Action(() => wv.CoreWebView2.PostWebMessageAsJson(json))); else wv.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { }
        }

        void Mensaje(CoreWebView2WebMessageReceivedEventArgs ev)
        {
            string raw; try { raw = ev.TryGetWebMessageAsString(); } catch { raw = ev.WebMessageAsJson; }
            Dictionary<string, object> m; try { m = Json.Parse(raw); } catch { return; }
            string reqId = m.ContainsKey("reqId") ? Convert.ToString(m["reqId"]) : "", accion = m.ContainsKey("action") ? Convert.ToString(m["action"]) : "";
            var carga = m.ContainsKey("payload") ? m["payload"] as Dictionary<string, object> : null; carga = carga ?? new Dictionary<string, object>();
            Program.Log("acción: " + accion);
            if (accion == "cerrarApp") { Responder(reqId, true, true, null); BeginInvoke(new Action(Close)); return; }
            if (accion == "disenoLogo")        // el cuadro de diálogo de archivos necesita este hilo
            {
                try { Responder(reqId, true, Motor.Despachar(accion, carga), null); } catch (Exception ex) { Responder(reqId, false, null, ex.Message); }
                return;
            }
            Task.Run(() =>
            {
                try { Responder(reqId, true, Motor.Despachar(accion, carga), null); }
                catch (Exception ex) { Responder(reqId, false, null, ex.InnerException != null ? ex.InnerException.Message : ex.Message); }
            });
        }
    }
}
