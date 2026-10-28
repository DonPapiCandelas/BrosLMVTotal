// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BrosLMV.BancosUI
{
    public partial class App : Application
    {
        private Mutex _instancia;
        private EventWaitHandle _activar;
        private VentanaPrincipal _ventana;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, ev) => { if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BROSLMV_BANCOS_SHOT"))) { File.WriteAllText(Environment.GetEnvironmentVariable("BROSLMV_BANCOS_SHOT") + ".error.txt", ev.Exception.ToString()); ev.Handled = true; Shutdown(); return; } MessageBox.Show(ev.Exception.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning); ev.Handled = true; };

            var ctx = Contexto.DeArgumentos(e.Args);
            bool dev = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_DEV") == "1";
            if (!ctx.Completo && dev) ctx = Contexto.Laboratorio();
            if (!ctx.Completo)
            {
                MessageBox.Show("Bancos se abre desde el botón de Comercial (Tesorería).", "Bancos", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(); return;
            }

            // Una sola ventana por empresa: si ya esta abierta, solo se trae al frente.
            string clave = new string((ctx.Servidor + "_" + ctx.BaseDeDatos).Where(char.IsLetterOrDigit).ToArray());
            bool nueva;
            _instancia = new Mutex(true, "BrosLMV.Bancos." + clave, out nueva);
            _activar = new EventWaitHandle(false, EventResetMode.AutoReset, "BrosLMV.Bancos.Activar." + clave);
            if (!nueva && !dev) { _activar.Set(); Shutdown(); return; }
            var hilo = new Thread(() => { while (_activar.WaitOne()) Dispatcher.BeginInvoke(new Action(Traer)); }) { IsBackground = true };
            hilo.Start();

            try { ctx.Conectar(); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo conectar a la base de la empresa.\n\n" + ex.Message, "Bancos", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(); return;
            }

            _ventana = new VentanaPrincipal(ctx);
            MainWindow = _ventana;
            _ventana.Show();

            string foto = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_SHOT");
            if (!string.IsNullOrEmpty(foto))
            {
                string extras = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_ARCHIVOS");
                if (!string.IsNullOrEmpty(extras)) _ventana.Dispatcher.InvokeAsync(() => _ventana.Bandeja.AgregarArchivos(extras.Split(';')), DispatcherPriority.Background);
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("BROSLMV_BANCOS_ESPERA"), out double seg) ? seg : 6) };
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    string pagina = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_PAGINA");
                    Window quien = _ventana;
                    if (pagina == "layouts") { _ventana.AbrirLayouts(); }
                    else if (pagina == "editor")
                    {
                        string filtro = (Environment.GetEnvironmentVariable("BROSLMV_BANCOS_MUESTRA") ?? "").ToLowerInvariant();
                        var m = _ventana.Bandeja.Archivos.FirstOrDefault(x => x.Nombre.ToLowerInvariant().Contains(filtro) && x.Hoja != null) ?? _ventana.Bandeja.Archivos.FirstOrDefault(x => x.Hoja != null);
                        string idLayout = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_LAYOUT");
                        var lay = string.IsNullOrEmpty(idLayout) ? null : _ventana.Bandeja.Layouts.FirstOrDefault(x => x.Id == idLayout);
                        if (Environment.GetEnvironmentVariable("BROSLMV_BANCOS_SINMUESTRA") == "1") m = null;
                        var ed = new EditorLayoutVentana(_ventana.Ctx, lay, m, true, _ventana.Bandeja.Layouts) { Owner = _ventana };
                        ed.Show(); ed.UpdateLayout(); quien = ed;
                    }
                    bool cargar = Environment.GetEnvironmentVariable("BROSLMV_BANCOS_AUTOCARGAR") == "1";
                    if (cargar) _ventana.Bandeja.CargarListos();
                    var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(cargar ? 5 : 1.5) };
                    t2.Tick += (s2, e2) => { t2.Stop(); Foto(foto, quien); Shutdown(); };
                    t2.Start();
                };
                t.Start();
            }
        }

        private void Traer()
        {
            if (_ventana == null) return;
            if (_ventana.WindowState == WindowState.Minimized) _ventana.WindowState = WindowState.Normal;
            _ventana.Activate(); _ventana.Topmost = true; _ventana.Topmost = false;
        }

        // Solo para verificar la pantalla sin abrir nada mas: guarda la ventana como imagen.
        private void Foto(string ruta, Window v)
        {
            v.UpdateLayout();
            int w = (int)v.ActualWidth, h = (int)v.ActualHeight;
            var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render((System.Windows.Media.Visual)v.Content);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(ruta)) enc.Save(fs);
        }
    }
}
