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

// RibbonUi.cs  (v2.95.0)
// Ventana del asistente «Crear botón…»: una página HTML (assets\crear_boton_app.html, incrustada en la DLL) dentro de WebView2.
// La página habla con C# por mensajes: JS envía {id, op, args} con window.chrome.webview.postMessage y C# responde {id, data|error}.
// Operaciones: init, publicar, quitar, deshacer, explorar (elegir un .ico/.png), ayuda, cerrar. Todo el SQL vive en RibbonAdmin.
// Los íconos se ven por un host virtual (https://iconos.local/ -> …\ComercialSP\Icons), sin pasar imágenes en base64.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BrosLMV
{
    internal class BotonResultado
    {
        public bool Publicado;    // se creó/actualizó el botón en la empresa activa
        public string AppKey;     // clave técnica del botón
        public string Caption;    // texto del botón (para crear el script si aún no existe)
    }

    internal static class CrearBotonForm
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static string Recurso(string termina)
        {
            var asm = Assembly.GetExecutingAssembly();
            string n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(termina, StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
            using (var s = asm.GetManifestResourceStream(n)) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        // Muestra el asistente (bloquea hasta que se cierra). appKey vacío = botón nuevo sin script todavía.
        public static BotonResultado Mostrar(ScriptContext ctx, string appKey, string nombreSugerido)
        {
            var resultado = new BotonResultado { AppKey = appKey };
            var admin = new RibbonAdmin(ctx);
            string html = Recurso("crear_boton_app.html");
            if (html == null) { MessageBox.Show("No se encontró la pantalla del asistente dentro de BrosLMV.", "BrosLMV"); return resultado; }
            string perfil = Path.Combine(Path.GetTempPath(), "BrosLMV_WebView2_" + Guid.NewGuid().ToString("N"));
            Exception hiloEx = null;

            var hilo = new Thread(() =>
            {
                try
                {
                    var frm = new Form { Text = "BrosLMV — Crear botón", StartPosition = FormStartPosition.CenterScreen, Width = 1180, Height = 820, MinimumSize = new Size(900, 640) };
                    var web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                    frm.Controls.Add(web);

                    frm.Load += async (s, e) =>
                    {
                        try
                        {
                            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: perfil);
                            await web.EnsureCoreWebView2Async(env);
                            string dirIconos = RibbonAdmin.CarpetaIconos();
                            if (dirIconos != null)
                                web.CoreWebView2.SetVirtualHostNameToFolderMapping("iconos.local", dirIconos, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                            web.CoreWebView2.WebMessageReceived += (s2, e2) => Atender(web, frm, ctx, admin, resultado, appKey, nombreSugerido, e2.TryGetWebMessageAsString());
                            web.CoreWebView2.NavigateToString(html);
                        }
                        catch (Exception ex) { hiloEx = ex; frm.Close(); }
                    };
                    frm.Shown += (s, e) => { frm.Activate(); frm.BringToFront(); web.Focus(); };
                    frm.ShowDialog();
                    web.Dispose(); frm.Dispose();
                    try { Directory.Delete(perfil, true); } catch { }
                }
                catch (Exception ex) { hiloEx = ex; }
            });
            hilo.SetApartmentState(ApartmentState.STA);   // WebView2 exige un hilo STA propio
            hilo.Start();
            hilo.Join();
            if (hiloEx != null) MessageBox.Show("No se pudo abrir el asistente: " + hiloEx.Message + "\n\n¿Está instalado el WebView2 Runtime?", "BrosLMV");
            return resultado;
        }

        private static void Responder(Microsoft.Web.WebView2.WinForms.WebView2 web, object id, object data, string error)
        {
            var r = new Dictionary<string, object> { { "id", id } };
            if (error != null) r["error"] = error; else r["data"] = data;
            try { web.CoreWebView2.PostWebMessageAsString(Json.Serialize(r)); } catch { }
        }

        private static void Atender(Microsoft.Web.WebView2.WinForms.WebView2 web, Form frm, ScriptContext ctx, RibbonAdmin admin, BotonResultado res, string appKeyInicial, string nombreInicial, string mensaje)
        {
            object id = null;
            try
            {
                var m = Json.Deserialize<Dictionary<string, object>>(mensaje);
                id = m["id"];
                string op = Convert.ToString(m["op"]);
                var a = m.ContainsKey("args") ? m["args"] as Dictionary<string, object> : new Dictionary<string, object>();
                switch (op)
                {
                    case "init":
                        Responder(web, id, new Dictionary<string, object> { { "ctx", admin.Contexto(appKeyInicial) }, { "appKey", appKeyInicial }, { "nombre", nombreInicial } }, null);
                        break;
                    case "publicar":
                    {
                        var spec = LeerSpec(a);
                        var r = admin.Publicar(spec);
                        if (r.Count > 0 && r[0].ContainsKey("ok") && (bool)r[0]["ok"]) { res.Publicado = true; res.AppKey = spec.AppKey; res.Caption = spec.Caption; }
                        Responder(web, id, new Dictionary<string, object> { { "resultados", r } }, null);
                        break;
                    }
                    case "quitar":
                    {
                        var r = admin.Quitar(Convert.ToString(a["appKey"]), Lista<string>(a, "empresas"));
                        Responder(web, id, new Dictionary<string, object> { { "resultados", r } }, null);
                        break;
                    }
                    case "deshacer":
                        Responder(web, id, new Dictionary<string, object> { { "mensaje", admin.DeshacerUltimo(Convert.ToString(a["appKey"])) } }, null);
                        break;
                    case "explorar":
                        Responder(web, id, new Dictionary<string, object> { { "icono", ElegirIcono(frm) } }, null);
                        break;
                    case "ayuda":
                        AbrirAyuda();
                        Responder(web, id, true, null);
                        break;
                    case "cerrar":
                        Responder(web, id, true, null);
                        frm.Close();
                        break;
                    default:
                        Responder(web, id, null, "Operación desconocida: " + op);
                        break;
                }
            }
            catch (Exception ex) { Responder(web, id, null, ex.Message); }
        }

        private static List<T> Lista<T>(Dictionary<string, object> a, string clave)
        {
            var r = new List<T>();
            if (a.ContainsKey(clave) && a[clave] is System.Collections.IEnumerable e && !(a[clave] is string))
                foreach (var x in e) r.Add((T)Convert.ChangeType(x, typeof(T)));
            return r;
        }

        private static BotonSpec LeerSpec(Dictionary<string, object> a)
        {
            var s = new BotonSpec
            {
                AppKey = Convert.ToString(a["appKey"]), Caption = Convert.ToString(a["caption"]), Description = Convert.ToString(a["description"]),
                Icon = Convert.ToString(a["icon"]), TabCaption = Convert.ToString(a["tab"]), GroupCaption = Convert.ToString(a["group"])
            };
            s.Modules = Lista<long>(a, "modules");
            s.Users = Lista<long>(a, "users");
            s.Empresas = Lista<string>(a, "empresas");
            return s;
        }

        // «Explorar…»: selector de Windows abierto en la carpeta de íconos de Comercial. Un .ico de otro lugar (o un .png) se copia a esa
        // carpeta con el prefijo BrosLMV_ (un .png se convierte a .ico de 16/32/48 px) porque Comercial solo lee íconos de ahí.
        private static string ElegirIcono(Form owner)
        {
            string dir = RibbonAdmin.CarpetaIconos();
            using (var d = new OpenFileDialog { Title = "Elegir ícono", Filter = "Íconos (*.ico;*.png)|*.ico;*.png", InitialDirectory = dir ?? "" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                string src = d.FileName;
                if (dir == null) throw new Exception("No se encontró la carpeta de íconos de Comercial (…\\ComercialSP\\Icons).");
                string nombre = Path.GetFileNameWithoutExtension(src);
                bool dentro = string.Equals(Path.GetDirectoryName(src), dir, StringComparison.OrdinalIgnoreCase);
                if (dentro && src.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(src);
                string destino = Path.Combine(dir, (nombre.StartsWith("BrosLMV_", StringComparison.OrdinalIgnoreCase) ? nombre : "BrosLMV_" + nombre) + ".ico");
                try
                {
                    if (src.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) using (var bmp = new Bitmap(src)) File.WriteAllBytes(destino, PngAIco(bmp));
                    else File.Copy(src, destino, true);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new Exception("Windows no dejó copiar el ícono a " + dir + ". Abre Comercial como administrador una vez, o copia el archivo ahí a mano y elígelo de nuevo.");
                }
                return Path.GetFileName(destino);
            }
        }

        // .ico clásico (imágenes DIB de 32 bits: 16, 32 y 48 px) a partir de una imagen.
        internal static byte[] PngAIco(Bitmap origen)
        {
            int[] tam = { 16, 32, 48 };
            var imgs = new List<byte[]>();
            foreach (int t in tam)
            {
                using (var b = new Bitmap(t, t, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Transparent);
                        g.DrawImage(origen, 0, 0, t, t);
                    }
                    int filaAnd = ((t + 31) / 32) * 4;
                    var ms = new MemoryStream();
                    var w = new BinaryWriter(ms);
                    w.Write(40); w.Write(t); w.Write(t * 2); w.Write((short)1); w.Write((short)32); w.Write(0); w.Write(t * t * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    for (int y = t - 1; y >= 0; y--)
                        for (int x = 0; x < t; x++) { var p = b.GetPixel(x, y); w.Write(p.B); w.Write(p.G); w.Write(p.R); w.Write(p.A); }
                    w.Write(new byte[filaAnd * t]);
                    imgs.Add(ms.ToArray());
                }
            }
            var o = new MemoryStream();
            var bw = new BinaryWriter(o);
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)imgs.Count);
            int off = 6 + 16 * imgs.Count;
            for (int i = 0; i < imgs.Count; i++)
            {
                bw.Write((byte)tam[i]); bw.Write((byte)tam[i]); bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(imgs[i].Length); bw.Write(off); off += imgs[i].Length;
            }
            foreach (var im in imgs) bw.Write(im);
            return o.ToArray();
        }

        // Guía «Cómo crear un botón» (HTML incrustado) en el navegador del sistema.
        private static void AbrirAyuda()
        {
            string html = Recurso("doc_CREAR_BOTON.html");
            if (html == null) { MessageBox.Show("No se encontró la guía de crear botones.", "BrosLMV"); return; }
            string dst = Path.Combine(Path.GetTempPath(), "BrosLMV_Como_crear_un_boton.html");
            File.WriteAllText(dst, html, new UTF8Encoding(false));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dst) { UseShellExecute = true });
        }
    }
}
