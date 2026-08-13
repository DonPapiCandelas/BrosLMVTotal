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

// MainWindow.xaml.cs -- orquesta la UI sobre el motor ya probado (Sat/Datos/Cola, enlazados
// desde ..\descargas\, NO reescritos). La contrasena de la FIEL vive SOLO en memoria durante
// la sesion (nunca en config.json ni en ningun archivo).

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;
using Microsoft.Web.WebView2.Core;

namespace BrosLMV.DescargasUI
{
    public partial class MainWindow : Window
    {
        private Configuracion _config;
        private X509Certificate2 _cert;
        private RSA _llave;
        private SqlConnection _conn;
        private bool _ocupado;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _config = Configuracion.Cargar();
            if (_config == null)
            {
                var cfgWin = new ConfigWindow { Owner = this };
                if (cfgWin.ShowDialog() != true) { Close(); return; }
                _config = cfgWin.Resultado;
            }

            var pwdWin = new PasswordWindow { Owner = this };
            if (pwdWin.ShowDialog() != true) { Close(); return; }

            try
            {
                _cert = SatFirmaXml.CargarFiel(_config.RutaCer, _config.RutaKey, pwdWin.Password, out _llave);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo cargar la FIEL: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            try
            {
                _conn = new SqlConnection(_config.CadenaConexion);
                _conn.Open();
                EsquemaSql.Asegurar(_conn);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo conectar a la base de datos: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            await Panel.EnsureCoreWebView2Async();
            Panel.CoreWebView2.WebMessageReceived += Panel_WebMessageReceived;
            RenderizarPanel();
        }

        private async void Panel_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_ocupado) return; // evita doble clic mientras una accion real esta en curso
            string accion = e.TryGetWebMessageAsString();

            switch (accion)
            {
                case "actualizar":
                    RenderizarPanel();
                    break;

                case "revisar-pendientes":
                    await EjecutarConBloqueo(async () =>
                    {
                        await SolicitudWorker.EjecutarPasadaAsync(_conn, _cert, _llave, "xml");
                    }, "Revisión de pendientes terminada.");
                    break;

                case "nueva-solicitud":
                    var dlg = new SolicitarWindow { Owner = this };
                    if (dlg.ShowDialog() == true)
                    {
                        await EjecutarConBloqueo(async () =>
                        {
                            var auth = await SatSoapClient.AutenticarAsync(_cert, _llave);
                            if (!auth.Exito) throw new Exception("Autenticacion: " + auth.Error);

                            var tipo = dlg.EsRecibidos ? "Recibidos" : "Emitidos";
                            var solic = await SatSoapClient.SolicitarDescargaAsync(
                                _cert, _llave, auth.Token,
                                rfcSolicitante: _config.Rfc,
                                rfcEmisor: dlg.EsRecibidos ? null : _config.Rfc,
                                rfcReceptor: dlg.EsRecibidos ? _config.Rfc : null,
                                desde: dlg.Desde, hasta: dlg.Hasta);

                            if (!solic.Exito) throw new Exception("SolicitaDescarga: " + solic.Error);

                            BrosSatDb.RegistrarSolicitud(_conn, solic.IdSolicitud, _config.Rfc, tipo, dlg.Desde, dlg.Hasta, "Manual");
                        }, "Solicitud enviada al SAT. Usa \"Revisar pendientes\" en unos minutos para ver el estatus.");
                    }
                    break;
            }
        }

        private async System.Threading.Tasks.Task EjecutarConBloqueo(Func<System.Threading.Tasks.Task> accion, string mensajeExito)
        {
            _ocupado = true;
            try
            {
                await accion();
                RenderizarPanel();
                MessageBox.Show(this, mensajeExito, "BrosLMV Descargas", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error: " + ex.Message, "BrosLMV Descargas", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _ocupado = false;
            }
        }

        private void RenderizarPanel()
        {
            string plantilla;
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("BrosLMV.DescargasUI.Assets.panel.html"))
            using (var reader = new StreamReader(stream))
                plantilla = reader.ReadToEnd();

            var solicitudes = BrosSatDb.ObtenerSolicitudesRecientes(_conn);
            var cfdis = BrosSatDb.ObtenerCfdiRecientes(_conn);

            var filasSolicitudes = new StringBuilder();
            if (solicitudes.Count == 0)
            {
                filasSolicitudes.Append("<tr><td colspan=\"6\" class=\"vacio\">Sin solicitudes todavía -- usa \"Nueva solicitud\".</td></tr>");
            }
            else
            {
                foreach (var s in solicitudes)
                {
                    filasSolicitudes.Append("<tr>")
                        .Append("<td>").Append(Html(s.IdSolicitud)).Append("</td>")
                        .Append("<td>").Append(Html(s.Tipo)).Append("</td>")
                        .Append("<td>").Append(s.FechaInicial.ToString("yyyy-MM-dd")).Append(" a ").Append(s.FechaFinal.ToString("yyyy-MM-dd")).Append("</td>")
                        .Append("<td>").Append(Pill(s.Estatus)).Append("</td>")
                        .Append("<td class=\"num\">").Append(s.NumeroCFDIs?.ToString() ?? "-").Append("</td>")
                        .Append("<td>").Append(s.FechaSolicitud.ToString("yyyy-MM-dd HH:mm")).Append("</td>")
                        .Append("</tr>");
                }
            }

            var filasCfdi = new StringBuilder();
            if (cfdis.Count == 0)
            {
                filasCfdi.Append("<tr><td colspan=\"6\" class=\"vacio\">Sin CFDI descargados todavía.</td></tr>");
            }
            else
            {
                foreach (var c in cfdis)
                {
                    filasCfdi.Append("<tr>")
                        .Append("<td>").Append(Html(c.RFCEmisor)).Append("</td>")
                        .Append("<td>").Append(Html(c.NombreEmisor)).Append("</td>")
                        .Append("<td>").Append(Html(c.TipoComprobante)).Append("</td>")
                        .Append("<td class=\"num\">").Append(c.Total?.ToString("N2", CultureInfo.InvariantCulture) ?? "-").Append("</td>")
                        .Append("<td>").Append(c.FechaEmision.ToString("yyyy-MM-dd HH:mm")).Append("</td>")
                        .Append("<td>").Append(Pill(c.EstatusSat)).Append("</td>")
                        .Append("</tr>");
                }
            }

            string html = plantilla
                .Replace("{{RFC}}", Html(_config.Rfc))
                .Replace("{{FILAS_SOLICITUDES}}", filasSolicitudes.ToString())
                .Replace("{{FILAS_CFDI}}", filasCfdi.ToString())
                .Replace("{{FECHA_ACTUALIZACION}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Panel.CoreWebView2.NavigateToString(html);
        }

        private static string Pill(string estatus)
        {
            string clase = "pill-" + (estatus ?? "").ToLowerInvariant();
            return "<span class=\"pill " + clase + "\">" + Html(estatus) + "</span>";
        }

        private static string Html(string texto) =>
            System.Net.WebUtility.HtmlEncode(texto ?? "");

        protected override void OnClosed(EventArgs e)
        {
            _conn?.Dispose();
            base.OnClosed(e);
        }
    }
}
