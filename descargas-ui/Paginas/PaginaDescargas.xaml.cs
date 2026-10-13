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

// PaginaDescargas.xaml.cs -- solicitudes de descarga al SAT de la empresa activa: nueva descarga, revision de pendientes, sincronizacion
// con Comercial y carga manual de XML/ZIP (boton o arrastrar y soltar). La FIEL se descifra (DPAPI) solo en memoria, al usarla.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaDescargas : UserControl
    {
        private readonly Sesion _s;
        private bool _ocupado;
        private List<SolicitudRow> _solicitudes = new List<SolicitudRow>();

        public PaginaDescargas(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => CargarDetalle();
        }

        private Window Ventana => Window.GetWindow(this);

        private void Aviso(string texto, string titulo = "BrosLMV Descargas", MessageBoxImage icono = MessageBoxImage.Information) =>
            MessageBox.Show(Ventana, texto, titulo, MessageBoxButton.OK, icono);

        // ---- Resumen y cola ----

        private void CargarDetalle()
        {
            var emp = _s.Empresa;
            _solicitudes = BrosSatDb.ObtenerSolicitudesRecientes(_s.Conn, emp.RFC).Select(x => new SolicitudRow
            {
                IdSolicitud = x.IdSolicitud, Tipo = x.Tipo, TipoSolicitud = x.TipoSolicitud,
                Rango = x.FechaInicial.ToString("dd/MM/yyyy") + " – " + x.FechaFinal.ToString("dd/MM/yyyy"),
                EstatusSat = x.Estatus, ErrorDescarga = x.UltimoErrorPaquete, NumeroCFDIs = x.NumeroCFDIs?.ToString() ?? "-",
                Fecha = x.FechaSolicitud.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), FechaSolicitudUtc = x.FechaSolicitud
            }).ToList();

            int pendientes = _solicitudes.Count(s => s.EstatusCategoria == "Pendiente");
            int terminadas = _solicitudes.Count(s => s.EstatusCategoria == "Terminada");
            int errores = _solicitudes.Count(s => s.EstatusCategoria == "Rechazada");
            CardPendientes.Text = pendientes.ToString("N0");
            CardTerminadas.Text = terminadas.ToString("N0");
            CardErrores.Text = errores.ToString("N0");
            PastillaErrores.Style = (Style)FindResource(errores > 0 ? "PastillaCritica" : "PastillaNeutra");

            var cobertura = BrosSatDb.ObtenerCoberturaDescargas(_s.Conn, emp.RFC);
            string ultimoXml = cobertura.UltimoXml.HasValue ? cobertura.UltimoXml.Value.ToLocalTime().ToString("dd/MM HH:mm") : "ninguno";
            LblActividad.Text = errores > 0
                ? errores + (errores == 1 ? " solicitud con incidencia." : " solicitudes con incidencia.") + " Último XML: " + ultimoXml + "."
                : "Último XML descargado: " + ultimoXml + ".";
            AplicarFiltro();
        }

        // La vista abre en "En proceso": no se mezclan tareas activas con un archivo historico. Las filas "Reemplazada" se ocultan desde SQL.
        private void AplicarFiltro()
        {
            string categoria = TabSolicitudesPendientes.IsChecked == true ? "Pendiente" : TabSolicitudesTerminadas.IsChecked == true ? "Terminada" : "Rechazada";
            var filas = _solicitudes.Where(s => s.EstatusCategoria == categoria).ToList();
            GridSolicitudes.ItemsSource = filas;
            Vacio.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (filas.Count == 0)
            {
                if (categoria == "Pendiente") { VacioIcono.Text = ""; VacioTitulo.Text = "Nada en espera"; VacioTexto.Text = "No hay solicitudes pendientes. Usa «Nueva descarga» para pedir CFDI al SAT."; }
                else if (categoria == "Terminada") { VacioIcono.Text = ""; VacioTitulo.Text = "Sin descargas terminadas"; VacioTexto.Text = "Aquí aparecerán las solicitudes que el SAT ya entregó."; }
                else { VacioIcono.Text = ""; VacioTitulo.Text = "Sin incidencias"; VacioTexto.Text = "Ninguna solicitud fue rechazada ni terminó con error."; }
            }
        }

        private void FiltroSolicitud_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) AplicarFiltro(); }

        private void BtnLimpiarSolicitudes_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(Ventana,
                "Se ocultarán de esta vista las solicitudes terminadas o con error de más de 30 días, y todas las marcadas como reemplazadas.\n\n" +
                "No se borra ningún XML, metadata ni dato de auditoría. Las solicitudes que siguen trabajando nunca se ocultan.",
                "Limpiar historial", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
            int ocultadas = BrosSatDb.OcultarSolicitudesHistoricas(_s.Conn, _s.Empresa.RFC);
            CargarDetalle();
            Aviso(ocultadas == 0 ? "No había solicitudes históricas para ocultar." : ocultadas + " solicitudes históricas ya no aparecen en la vista diaria.", "Historial limpio");
        }

        // ---- Acciones contra el SAT ----

        private (X509Certificate2 cert, RSA llave) CargarFiel(EmpresaRow empresa)
        {
            string password = DpapiHelper.Descifrar(empresa.PasswordCifrada);
            var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, password, out var llave);
            return (cert, llave);
        }

        private async void BtnRevisarPendientes_Click(object sender, RoutedEventArgs e)
        {
            var emp = _s.Empresa;
            await EjecutarConBloqueo(async () =>
            {
                var (cert, llave) = CargarFiel(emp);
                using (llave)
                    await SolicitudWorker.EjecutarPasadaAsync(_s.Conn, cert, llave, emp.CarpetaXml, emp.EstructuraCarpetas, emp.PlantillaNombreArchivo, emp.RFC,
                        emp.ComercialCarpetaXmlRecibidos, emp.ComercialCarpetaXmlEmitidos, emp.ComercialConexionSql);
            }, "Revisión de pendientes terminada.");
        }

        private async void BtnSincronizarComercial_Click(object sender, RoutedEventArgs e)
        {
            var emp = _s.Empresa;
            bool tieneIntegracion = !string.IsNullOrWhiteSpace(emp.ComercialConexionSql) || !string.IsNullOrWhiteSpace(emp.ComercialCarpetaXmlRecibidos) || !string.IsNullOrWhiteSpace(emp.ComercialCarpetaXmlEmitidos);
            if (!tieneIntegracion)
            {
                Aviso("Esta empresa no tiene configurada la integración con Comercial. Configúrala en «Empresas y e.firma» → Editar.");
                return;
            }
            if (_ocupado) return;
            _ocupado = true; Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var fila = new EmpresaFila
                {
                    RFC = emp.RFC, Nombre = emp.Nombre, ComercialCarpetaXmlRecibidos = emp.ComercialCarpetaXmlRecibidos,
                    ComercialCarpetaXmlEmitidos = emp.ComercialCarpetaXmlEmitidos, ComercialConexionSql = emp.ComercialConexionSql
                };
                var r = await ComercialSync.SincronizarTodoAsync(_s.Conn, fila);
                string mensaje = (r.ImportadosRecibidos + r.ImportadosEmitidos) + " CFDI importados directo a Comercial, " + (r.CopiadosRecibidos + r.CopiadosEmitidos) + " copiados a carpeta.";
                if (r.SinArchivoEnDisco > 0) mensaje += "\n" + r.SinArchivoEnDisco + " CFDI sin XML en disco (se omitieron).";
                if (r.Errores > 0) mensaje += "\n" + r.Errores + " con error (ver Bitácora).";
                Aviso(mensaje, "Sincronización con Comercial");
            }
            catch (Exception ex) { Aviso("No se pudo sincronizar:\n\n" + ex.Message, "BrosLMV Descargas", MessageBoxImage.Error); }
            finally { _ocupado = false; Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        private async void BtnNuevaSolicitud_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SolicitarWindow { Owner = Ventana };
            if (dlg.ShowDialog() != true) return;
            var emp = _s.Empresa;

            await EjecutarConBloqueo(async () =>
            {
                var (cert, llave) = CargarFiel(emp);
                using (llave)
                {
                    var auth = await SatSoapClient.AutenticarAsync(cert, llave);
                    if (!auth.Exito) throw new Exception("Autenticación: " + auth.Error);
                    string tipo = dlg.EsRecibidos ? "Recibidos" : "Emitidos";

                    // Un rango grande en UNA sola SolicitaDescarga arriesga un paquete demasiado grande (visto en vivo: 7+ meses -> 1332 CFDI, agoto sus
                    // 2 descargas con contenido vacio, CodEstatus=5008, bug #16 en DOCUMENTACION.md). SolicitarWindow ya lo parte en tramos de mes de
                    // calendario: una SolicitaDescarga real por tramo y, dentro de cada tramo, CFDI + Metadata juntos (pedido del usuario 2026-08-14):
                    // Metadata da el numero exacto de lo que existe en el SAT (incluye cancelados) y sirve de respaldo con datos basicos.
                    foreach (var (desdeTramo, hastaTramo) in dlg.Chunks)
                    {
                        var solicCfdi = await SatSoapClient.SolicitarDescargaAsync(cert, llave, auth.Token, rfcSolicitante: emp.RFC,
                            rfcEmisor: dlg.EsRecibidos ? null : emp.RFC, rfcReceptor: dlg.EsRecibidos ? emp.RFC : null, desde: desdeTramo, hasta: hastaTramo, tipoSolicitud: "CFDI");
                        if (!solicCfdi.Exito) throw new Exception("SolicitaDescarga (CFDI, " + desdeTramo.ToString("yyyy-MM") + "): " + solicCfdi.Error);
                        BrosSatDb.RegistrarSolicitud(_s.Conn, solicCfdi.IdSolicitud, emp.RFC, tipo, desdeTramo, hastaTramo, "Manual", "CFDI");

                        var solicMeta = await SatSoapClient.SolicitarDescargaAsync(cert, llave, auth.Token, rfcSolicitante: emp.RFC,
                            rfcEmisor: dlg.EsRecibidos ? null : emp.RFC, rfcReceptor: dlg.EsRecibidos ? emp.RFC : null, desde: desdeTramo, hasta: hastaTramo, tipoSolicitud: "Metadata");
                        if (!solicMeta.Exito) throw new Exception("SolicitaDescarga (Metadata, " + desdeTramo.ToString("yyyy-MM") + "): " + solicMeta.Error);
                        BrosSatDb.RegistrarSolicitud(_s.Conn, solicMeta.IdSolicitud, emp.RFC, tipo, desdeTramo, hastaTramo, "Manual", "Metadata");
                    }
                }
            }, "Solicitud(es) enviada(s) al SAT (CFDI y Metadata por cada mes). Usa «Revisar pendientes» en unos minutos para ver el estatus.");
        }

        private async Task EjecutarConBloqueo(Func<Task> accion, string mensajeExito)
        {
            if (_ocupado) return;
            _ocupado = true; Cursor = System.Windows.Input.Cursors.Wait;
            try { await accion(); CargarDetalle(); Aviso(mensajeExito); }
            catch (Exception ex) { Aviso("Error: " + ex.Message, "BrosLMV Descargas", MessageBoxImage.Error); }
            finally { _ocupado = false; Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        // ---- Carga manual de XML / ZIP (boton o arrastrar y soltar) ----

        private void BtnCargarXml_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "CFDI (*.xml;*.zip)|*.xml;*.zip", Title = "Elige XML o ZIP de CFDI" };
            if (dlg.ShowDialog(Ventana) == true) _ = CargarArchivosAsync(dlg.FileNames);
        }

        private void Pagina_DragOver(object sender, DragEventArgs e)
        {
            bool archivos = e.Data.GetDataPresent(DataFormats.FileDrop) && !_ocupado;
            e.Effects = archivos ? DragDropEffects.Copy : DragDropEffects.None;
            ZonaSoltar.Visibility = archivos ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        private void Pagina_DragLeave(object sender, DragEventArgs e) => ZonaSoltar.Visibility = Visibility.Collapsed;

        private void Pagina_Drop(object sender, DragEventArgs e)
        {
            ZonaSoltar.Visibility = Visibility.Collapsed;
            if (_ocupado || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            _ = CargarArchivosAsync((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private async Task CargarArchivosAsync(string[] rutas)
        {
            var datos = _s.DatosEmpresa();
            if (datos == null) return;
            _ocupado = true; Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var filas = await CargaManual.ImportarAsync(_s.Conn, datos, rutas);
                int nuevos = filas.Count(f => f.Resultado == ResultadoCarga.Nuevo);
                int ya = filas.Count(f => f.Resultado == ResultadoCarga.YaExistia);
                var problemas = filas.Where(f => f.Resultado != ResultadoCarga.Nuevo && f.Resultado != ResultadoCarga.YaExistia).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Revisados: " + filas.Count + " comprobante(s) de " + _s.Empresa.Nombre + ".");
                sb.AppendLine("• Nuevos: " + nuevos);
                sb.AppendLine("• Ya estaban: " + ya);
                sb.AppendLine("• Con problema: " + problemas.Count);
                foreach (var p in problemas.Take(12)) sb.AppendLine("    – " + p.Archivo + ": " + p.Detalle);
                if (problemas.Count > 12) sb.AppendLine("    … y " + (problemas.Count - 12) + " más.");
                if (nuevos > 0) sb.AppendLine(Environment.NewLine + "Su estatus ante el SAT se confirma en la siguiente verificación automática.");
                Aviso(sb.ToString(), "Carga manual", problemas.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                CargarDetalle();
            }
            catch (Exception ex) { Aviso("No se pudo completar la carga:" + Environment.NewLine + Environment.NewLine + ex.Message, "Carga manual", MessageBoxImage.Warning); }
            finally { _ocupado = false; Cursor = System.Windows.Input.Cursors.Arrow; }
        }
    }
}
