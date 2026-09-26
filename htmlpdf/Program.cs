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

// Program.cs -- BrosLMV.HtmlToPdf: renderiza un HTML con el motor de Chrome (WebView2) en vez
// del generador nativo de Comercial. Proceso APARTE -- nunca corre dentro de ComercialSP.
//
// Modos:
//   --html <e> --pdf <s>        headless: exporta a PDF y termina.
//   --html <e> --preview        ventana visible con [Guardar PDF] [Imprimir] [Cerrar].
//   --timeout <seg>             tope de tiempo total (default 45). Si se pasa -> exit 5.
//
// Codigos de salida:  0 ok | 1 argumentos | 2 navegacion fallo | 3 falta WebView2 Runtime
//                     4 PrintToPdf fallo | 5 timeout | 6 excepcion
//
// Siempre deja rastro en C:\BrosLMV\logs\htmltopdf_YYYYMMDD.txt (append).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrosLMV.HtmlToPdf
{
    internal static class Program
    {
        static string _logFile;
        static Stopwatch _sw;

        [STAThread]
        private static int Main(string[] args)
        {
            _sw = Stopwatch.StartNew();
            string rutaHtml = null, rutaPdf = null;
            bool preview = false;
            int timeoutSeg = 45;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--html" && i + 1 < args.Length) rutaHtml = args[++i];
                else if (args[i] == "--pdf" && i + 1 < args.Length) rutaPdf = args[++i];
                else if (args[i] == "--preview") preview = true;
                else if (args[i] == "--timeout" && i + 1 < args.Length) int.TryParse(args[++i], out timeoutSeg);
            }
            if (timeoutSeg < 5) timeoutSeg = 5;

            Log("== inicio == html=" + rutaHtml + " pdf=" + rutaPdf + (preview ? " (preview)" : "") + " timeout=" + timeoutSeg + "s");

            if (string.IsNullOrEmpty(rutaHtml) || (!preview && string.IsNullOrEmpty(rutaPdf)))
                return Salir(1, "Uso: BrosLMV.HtmlToPdf.exe --html <entrada.html> (--pdf <salida.pdf> | --preview) [--timeout <seg>]");

            if (!File.Exists(rutaHtml))
                return Salir(1, "No existe el archivo HTML: " + rutaHtml);

            // Validacion minima del HTML de entrada (no fatal, pero se anota).
            try
            {
                var fi = new FileInfo(rutaHtml);
                if (fi.Length == 0) return Salir(1, "El archivo HTML esta vacio: " + rutaHtml);
                string cabeza = File.ReadAllText(rutaHtml);
                if (cabeza.IndexOf("<body", StringComparison.OrdinalIgnoreCase) < 0)
                    Log("AVISO: el HTML no tiene <body> -- puede salir un PDF en blanco.");
            }
            catch (Exception ex) { Log("AVISO: no se pudo leer el HTML para validarlo: " + ex.Message); }

            // WebView2 Runtime instalado?
            try
            {
                string ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrEmpty(ver))
                    return Salir(3, "No se encontro el WebView2 Runtime en este equipo. Instalalo desde https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen Standalone).");
                Log("WebView2 Runtime: " + ver);
            }
            catch (Exception ex)
            {
                return Salir(3, "No se encontro el WebView2 Runtime (" + ex.Message + "). Instalalo desde https://developer.microsoft.com/microsoft-edge/webview2/");
            }

            // Watchdog de tiempo total: si algo se cuelga (EnsureCoreWebView2Async, navegacion),
            // este timer mata el proceso con codigo 5. En preview el usuario cierra la ventana,
            // asi que ahi el watchdog es mas largo (el timeout aplica solo a la carga inicial).
            int matarEnMs = (preview ? Math.Max(timeoutSeg, 20) : timeoutSeg) * 1000;
            var watchdog = new System.Threading.Timer(_ =>
            {
                Log("TIMEOUT: se paso de " + (matarEnMs / 1000) + "s -> se aborta.");
                Console.Error.WriteLine("TIMEOUT generando el PDF.");
                Environment.Exit(preview ? 0 : 5);
            }, null, matarEnMs, System.Threading.Timeout.Infinite);

            int rc;
            try { rc = preview ? EjecutarPreview(rutaHtml, rutaPdf) : EjecutarHeadless(rutaHtml, rutaPdf); }
            catch (Exception ex) { rc = Salir(6, "Excepcion: " + ex); }
            finally { watchdog.Dispose(); }
            Log("== fin == rc=" + rc + " (" + _sw.ElapsedMilliseconds + " ms)");
            return rc;
        }

        // ---- modo headless: exporta el PDF y sale ----
        private static int EjecutarHeadless(string rutaHtml, string rutaPdf)
        {
            int codigoSalida = 6;
            using (var form = new Form { WindowState = FormWindowState.Minimized, ShowInTaskbar = false, Width = 1, Height = 1 })
            using (var webView = new WebView2 { Dock = DockStyle.Fill })
            {
                form.Controls.Add(webView);
                form.Load += async (s, e) =>
                {
                    string perfilTemporal = null;
                    try
                    {
                        var (entorno, perfil) = await CrearEntornoAsync();
                        perfilTemporal = perfil;
                        await webView.EnsureCoreWebView2Async(entorno);
                        bool navegoOk = await NavegarAsync(webView, rutaHtml);
                        if (!navegoOk) { codigoSalida = 2; Console.Error.WriteLine("La navegacion al HTML no tuvo exito."); Log("ERROR: navegacion fallo."); }
                        else
                        {
                            // Espera a que la plantilla termine de armarse (QR, Paged.js si lo usa).
                            // Contrato: el HTML pone window.__READY_FOR_PDF__=true.
                            await EsperarListoAsync(webView);
                            bool ok = await webView.CoreWebView2.PrintToPdfAsync(Path.GetFullPath(rutaPdf), AjustesImpresion(webView));
                            if (!ok) { codigoSalida = 4; Console.Error.WriteLine("PrintToPdfAsync regreso false."); Log("ERROR: PrintToPdfAsync=false."); }
                            else if (!File.Exists(rutaPdf) || new FileInfo(rutaPdf).Length < 200)
                            { codigoSalida = 4; Console.Error.WriteLine("El PDF no se creo o quedo vacio."); Log("ERROR: PDF ausente/vacio."); }
                            else { codigoSalida = 0; Console.WriteLine("PDF generado: " + rutaPdf); Log("OK: " + rutaPdf + " (" + new FileInfo(rutaPdf).Length + " bytes)"); }
                        }
                    }
                    catch (Exception ex) { codigoSalida = 6; Console.Error.WriteLine("Error generando PDF: " + ex); Log("EXCEPCION: " + ex.Message); }
                    finally { LimpiarPerfil(perfilTemporal); Application.Exit(); }
                };
                Application.Run(form);
            }
            return codigoSalida;
        }

        // ---- modo preview: ventana visible con barra [Guardar PDF] [Imprimir] [Cerrar] ----
        private static int EjecutarPreview(string rutaHtml, string rutaPdfSugerido)
        {
            using (var form = new Form { Text = "BrosLMV - Vista previa del documento", Width = 900, Height = 1000, StartPosition = FormStartPosition.CenterScreen })
            {
                var barra = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
                var btnPdf = new ToolStripButton("Guardar PDF");
                var btnImprimir = new ToolStripButton("Imprimir");
                var btnCerrar = new ToolStripButton("Cerrar");
                barra.Items.Add(btnPdf);
                barra.Items.Add(btnImprimir);
                barra.Items.Add(new ToolStripSeparator());
                barra.Items.Add(btnCerrar);

                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                form.Controls.Add(barra);

                string perfilTemporal = null;
                form.Load += async (s, e) =>
                {
                    try
                    {
                        var (entorno, perfil) = await CrearEntornoAsync();
                        perfilTemporal = perfil;
                        await webView.EnsureCoreWebView2Async(entorno);
                        await NavegarAsync(webView, rutaHtml);
                        await EsperarListoAsync(webView);   // deja la pagina paginada antes de que el usuario pueda guardar
                    }
                    catch (Exception ex) { MessageBox.Show("No se pudo cargar la vista previa:\n" + ex.Message, "BrosLMV"); }
                };

                btnPdf.Click += async (s, e) =>
                {
                    using (var sfd = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = SugerirNombre(rutaPdfSugerido, rutaHtml) })
                    {
                        if (sfd.ShowDialog() != DialogResult.OK) return;
                        try
                        {
                            await EsperarListoAsync(webView);
                            bool ok = await webView.CoreWebView2.PrintToPdfAsync(sfd.FileName, AjustesImpresion(webView));
                            if (ok && File.Exists(sfd.FileName))
                            {
                                Log("OK preview->PDF: " + sfd.FileName);
                                try { Process.Start(new ProcessStartInfo { FileName = sfd.FileName, UseShellExecute = true }); } catch { }
                            }
                            else MessageBox.Show("No se pudo generar el PDF.", "BrosLMV");
                        }
                        catch (Exception ex) { MessageBox.Show("Error al guardar el PDF:\n" + ex.Message, "BrosLMV"); }
                    }
                };
                btnImprimir.Click += (s, e) => { try { webView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch { } };
                btnCerrar.Click += (s, e) => form.Close();
                form.FormClosed += (s, e) => LimpiarPerfil(perfilTemporal);

                Application.Run(form);
            }
            return 0;
        }

        // Ajustes de impresion: LO IMPORTANTE es ShouldPrintBackgrounds=true -- sin esto Chromium
        // NO imprime los fondos de color y el PDF sale descolorido respecto a la vista previa.
        // Margenes en 0: los controla el @page de cada plantilla.
        private static CoreWebView2PrintSettings AjustesImpresion(WebView2 webView)
        {
            var s = webView.CoreWebView2.Environment.CreatePrintSettings();
            s.ShouldPrintBackgrounds = true;
            s.MarginTop = s.MarginBottom = s.MarginLeft = s.MarginRight = 0;
            return s;
        }

        // Espera a que el HTML avise que ya esta listo para exportar.
        //   - Formatos nuevos: ponen window.__READY_FOR_PDF__ = true al terminar (QR + Paged.js).
        //   - Formatos viejos: no definen el flag -> se espera un minimo y se continua.
        private static async Task EsperarListoAsync(WebView2 webView)
        {
            const int pasoMs = 100, minMs = 700, maxMs = 15000;
            int t = 0;
            bool listo = false;
            while (t < maxMs)
            {
                await Task.Delay(pasoMs);
                t += pasoMs;
                try
                {
                    var r = await webView.CoreWebView2.ExecuteScriptAsync("window.__READY_FOR_PDF__ === true");
                    if (r == "true") { listo = true; break; }
                    if (t >= minMs)
                    {
                        var def = await webView.CoreWebView2.ExecuteScriptAsync("typeof window.__READY_FOR_PDF__ !== 'undefined'");
                        if (def != "true") break;   // el formato no implementa el contrato -> seguir
                    }
                }
                catch { }
            }
            Log("EsperarListoAsync: " + (listo ? "READY en " : "sin contrato, ") + t + "ms");
            await Task.Delay(listo ? 120 : 200);
        }

        private static async Task<(CoreWebView2Environment, string)> CrearEntornoAsync()
        {
            string perfil = Path.Combine(Path.GetTempPath(), "BrosLMV_HtmlToPdf_" + Guid.NewGuid().ToString("N"));
            var entorno = await CoreWebView2Environment.CreateAsync(null, perfil, new CoreWebView2EnvironmentOptions());
            return (entorno, perfil);
        }

        private static async Task<bool> NavegarAsync(WebView2 webView, string rutaHtml)
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(object s, CoreWebView2NavigationCompletedEventArgs e2) { webView.CoreWebView2.NavigationCompleted -= Handler; tcs.TrySetResult(e2.IsSuccess); }
            webView.CoreWebView2.NavigationCompleted += Handler;
            webView.CoreWebView2.Navigate(new Uri(Path.GetFullPath(rutaHtml)).AbsoluteUri);
            return await tcs.Task;
        }

        private static string SugerirNombre(string rutaPdfSugerido, string rutaHtml)
        {
            if (!string.IsNullOrEmpty(rutaPdfSugerido)) return Path.GetFileName(rutaPdfSugerido);
            return Path.GetFileNameWithoutExtension(rutaHtml) + ".pdf";
        }

        private static void LimpiarPerfil(string perfil)
        {
            if (string.IsNullOrEmpty(perfil)) return;
            try { Directory.Delete(perfil, true); } catch { }
        }

        // ---- utilidades ----
        private static int Salir(int codigo, string mensaje)
        {
            if (codigo == 0) Console.WriteLine(mensaje); else Console.Error.WriteLine(mensaje);
            Log("SALIDA " + codigo + ": " + mensaje);
            return codigo;
        }

        private static void Log(string msg)
        {
            try
            {
                if (_logFile == null)
                {
                    string dir = @"C:\BrosLMV\logs";
                    Directory.CreateDirectory(dir);
                    _logFile = Path.Combine(dir, "htmltopdf_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
                }
                File.AppendAllText(_logFile,
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [pid " + Process.GetCurrentProcess().Id + "] " + msg + Environment.NewLine);
            }
            catch { /* el log jamas debe tronar la generacion */ }
        }
    }
}
