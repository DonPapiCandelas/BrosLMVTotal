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

// MainWindow.xaml.cs -- pantalla inicial: lista de empresas (catalogo propio, ver EsquemaSql
// tabla Empresa). Al elegir una, se muestra su cola de solicitudes + historial de CFDI. La
// contrasena de la FIEL de cada empresa se descifra (DPAPI) solo en memoria, en el momento de
// usarla -- nunca se le vuelve a pedir al usuario.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;
using Forms = System.Windows.Forms;

namespace BrosLMV.DescargasUI
{
    // Filas de UI (formato ya listo para mostrar -- las consultas a BD regresan tipos crudos).
    // OJO: deben ser PROPIEDADES, no campos -- el binding de WPF ({Binding Nombre}) solo
    // resuelve propiedades por reflexion; con campos publicos simplemente no encuentra nada
    // y la celda queda en blanco SIN error visible (bug real encontrado en vivo).
    public sealed class EmpresaRow
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; }
        public string RFC { get; set; }
        public string RutaCer { get; set; }
        public string RutaKey { get; set; }
        public byte[] PasswordCifrada { get; set; }
        public int TotalCfdi { get; set; }
        public string UltimoEstatus { get; set; }
        public string CarpetaXml { get; set; }
        public string EstructuraCarpetas { get; set; }
        public string PlantillaNombreArchivo { get; set; }
        public string ComercialCarpetaXmlRecibidos { get; set; }
        public string ComercialCarpetaXmlEmitidos { get; set; }
        public string ComercialConexionSql { get; set; }
    }
    public sealed class SolicitudRow
    {
        public string IdSolicitud { get; set; }
        public string Tipo { get; set; }
        public string TipoSolicitud { get; set; } // CFDI / Metadata
        public string Rango { get; set; }
        public string EstatusSat { get; set; } // el Estatus crudo de la SOLICITUD, tal cual lo reporta el SAT
        public string ErrorDescarga { get; set; } // motivo real si algun paquete agoto sus 2 descargas sin exito
        public string Fecha { get; set; }
        public DateTime FechaSolicitudUtc { get; set; }
        public string NumeroCFDIs { get; set; }

        // El SAT no da ninguna barra de progreso -- lo unico que la app puede mostrar mientras
        // esta Aceptada/EnProceso es cuanto tiempo lleva esperando, para que quede claro que SI
        // se esta rastreando (no es que la app se haya quedado pasmada). Pedido directo del
        // usuario (2026-08-14): "no sabe si funcionara o no porque esta con estatus aceptada".
        public string TiempoTranscurrido
        {
            get
            {
                if (EstatusSat != "Aceptada" && EstatusSat != "EnProceso") return "-";
                var transcurrido = DateTime.UtcNow - FechaSolicitudUtc;
                if (transcurrido.TotalHours >= 1) return (int)transcurrido.TotalHours + "h " + transcurrido.Minutes + "min";
                return (int)transcurrido.TotalMinutes + " min";
            }
        }

        // "Terminada" en EstatusSat solo dice que el SAT ya proceso la SOLICITUD -- no que la
        // DESCARGA del paquete haya funcionado. Un paquete puede agotar sus 2 intentos sin traer
        // nunca los datos (visto en vivo: CodEstatus=5008), y sin este distingo la UI mostraba
        // "Terminada" con un numero de CFDIs que en realidad nunca llegaron a la base -- confuso
        // y enganoso, reportado directo por el usuario.
        public string Estatus => ErrorDescarga != null ? "Error de descarga" : EstatusSat;

        // Agrupa el Estatus mostrado en las 3 categorias que pide la pestana de filtro.
        public string EstatusCategoria =>
            ErrorDescarga != null ? "Rechazada" :
            EstatusSat == "Aceptada" || EstatusSat == "EnProceso" ? "Pendiente" :
            EstatusSat == "Terminada" ? "Terminada" :
            "Rechazada";
    }
    public sealed class CfdiRow
    {
        public bool Seleccionado { get; set; }
        public string RutaArchivoXml { get; set; }
        public int CfdiID { get; set; }
        public string UUID { get; set; }
        public string RFCEmisor { get; set; }
        public string NombreEmisor { get; set; }
        public string RFCReceptor { get; set; }
        // Contraparte = quien no es la propia empresa -- el proveedor si se ve la pestana
        // Recibidos, o el cliente si se ve Emitidos. Ver MainWindow.CargarDetalle.
        public string Contraparte { get; set; }
        public string NombreContraparte { get; set; }
        public string TipoComprobante { get; set; }
        public string FechaEmision { get; set; }
        public string Subtotal { get; set; }
        public string Descuento { get; set; }
        public string IVA { get; set; }
        public string Retenciones { get; set; }
        public string Total { get; set; }
        public string Moneda { get; set; }
        public string TipoCambio { get; set; }
        public string FormaPago { get; set; }
        public string MetodoPago { get; set; }
        public string EstatusSat { get; set; }
        public bool Archivado { get; set; }
        public string ArchivadoTexto => Archivado ? "Sí" : "";
        public DateTime? FechaSincronizadoComercial { get; set; }
        public string SincronizadoComercialTexto => FechaSincronizadoComercial.HasValue
            ? "Sincronizado (" + FechaSincronizadoComercial.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")"
            : "Sin sincronizar";
    }

    public partial class MainWindow : Window
    {
        private Configuracion _config;
        private SqlConnection _conn;
        private EmpresaRow _empresaActual;
        private bool _ocupado;
        private List<SolicitudRow> _solicitudesActuales = new List<SolicitudRow>();
        private Forms.NotifyIcon _iconoBandeja;
        private bool _saliendoDeVerdad;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            ConfigurarBandeja();
        }

        // Pedido explicito del usuario 2026-08-19: "cuando cierren la app no la cierre si no que
        // la tenga en segundo plano" -- ya no es necesario para que las descargas sigan
        // funcionando (eso lo hace el Servicio de Windows "BrosLMV Descargas" por su cuenta,
        // independiente de si esta ventana esta abierta), pero se deja igual por comodidad: asi
        // no hay que volver a abrir la app desde el menu Inicio cada vez.
        private void ConfigurarBandeja()
        {
            _iconoBandeja = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath),
                Text = "BrosLMV Descargas",
                Visible = false
            };
            _iconoBandeja.DoubleClick += (s, e) => RestaurarDesdeBandeja();

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir BrosLMV Descargas", null, (s, e) => RestaurarDesdeBandeja());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => { _saliendoDeVerdad = true; Close(); });
            _iconoBandeja.ContextMenuStrip = menu;
        }

        private void RestaurarDesdeBandeja()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _iconoBandeja.Visible = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_saliendoDeVerdad)
            {
                e.Cancel = true;
                Hide();
                _iconoBandeja.Visible = true;
                _iconoBandeja.ShowBalloonTip(3000, "BrosLMV Descargas",
                    "Sigue corriendo en segundo plano. Doble clic aquí para volver a abrirla.", Forms.ToolTipIcon.Info);
                return;
            }
            _iconoBandeja.Visible = false;
            _iconoBandeja.Dispose();
            base.OnClosing(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _config = Configuracion.Cargar();
            if (_config == null)
            {
                var cw = new ConexionWindow { Owner = this };
                if (cw.ShowDialog() != true) { Close(); return; }
                _config = cw.Resultado;
            }

            try
            {
                EsquemaSql.AsegurarBaseDeDatos(_config.CadenaConexion);
                _conn = new SqlConnection(_config.CadenaConexion);
                _conn.Open();
                EsquemaSql.Asegurar(_conn);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo conectar a la base de datos:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            LblConexion.Text = "Conectado: " + _conn.DataSource + " / " + _conn.Database;
            CargarEmpresas();
            // Para la instalación típica (una sola razón social), la primera pantalla debe ser
            // el tablero de trabajo, no un catálogo con una fila que obliga a doble clic.
            if (GridEmpresas.ItemsSource is System.Collections.Generic.IEnumerable<EmpresaRow> empresas && empresas.Count() == 1)
                AbrirDetalle(empresas.Single());
        }

        // ---- Panel de empresas ----

        private void CargarEmpresas()
        {
            var empresas = BrosSatDb.ObtenerEmpresas(_conn);
            var filas = new ObservableCollection<EmpresaRow>();
            int totalCfdi = 0;
            foreach (var e in empresas)
            {
                var cfdis = BrosSatDb.ObtenerCfdiRecientes(_conn, e.RFC, 10000);
                var solicitudes = BrosSatDb.ObtenerSolicitudesRecientes(_conn, e.RFC, 1);
                totalCfdi += cfdis.Count;
                filas.Add(new EmpresaRow
                {
                    EmpresaID = e.EmpresaID,
                    Nombre = e.Nombre,
                    RFC = e.RFC,
                    RutaCer = e.RutaCer,
                    RutaKey = e.RutaKey,
                    PasswordCifrada = e.PasswordCifrada,
                    TotalCfdi = cfdis.Count,
                    UltimoEstatus = solicitudes.Count > 0 ? solicitudes[0].Estatus : "—",
                    CarpetaXml = e.CarpetaXml,
                    EstructuraCarpetas = e.EstructuraCarpetas,
                    PlantillaNombreArchivo = e.PlantillaNombreArchivo,
                    ComercialCarpetaXmlRecibidos = e.ComercialCarpetaXmlRecibidos,
                    ComercialCarpetaXmlEmitidos = e.ComercialCarpetaXmlEmitidos,
                    ComercialConexionSql = e.ComercialConexionSql
                });
            }
            GridEmpresas.ItemsSource = filas;
            CardEmpresas.Text = filas.Count.ToString();
            CardCfdi.Text = totalCfdi.ToString();
        }

        private void BtnAgregarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EmpresaWindow(_conn) { Owner = this };
            if (dlg.ShowDialog() == true) CargarEmpresas();
        }

        private void BtnEditarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridEmpresas.SelectedItem as EmpresaRow;
            if (sel == null) return;
            var existente = new EmpresaFila
            {
                EmpresaID = sel.EmpresaID,
                Nombre = sel.Nombre,
                RFC = sel.RFC,
                RutaCer = sel.RutaCer,
                RutaKey = sel.RutaKey,
                PasswordCifrada = sel.PasswordCifrada,
                CarpetaXml = sel.CarpetaXml,
                EstructuraCarpetas = sel.EstructuraCarpetas,
                PlantillaNombreArchivo = sel.PlantillaNombreArchivo,
                ComercialCarpetaXmlRecibidos = sel.ComercialCarpetaXmlRecibidos,
                ComercialCarpetaXmlEmitidos = sel.ComercialCarpetaXmlEmitidos,
                ComercialConexionSql = sel.ComercialConexionSql
            };
            var dlg = new EmpresaWindow(_conn, existente) { Owner = this };
            if (dlg.ShowDialog() == true) CargarEmpresas();
        }

        private void BtnEliminarEmpresa_Click(object sender, RoutedEventArgs e)
        {
            var sel = GridEmpresas.SelectedItem as EmpresaRow;
            if (sel == null) return;
            var r = MessageBox.Show(this, "¿Eliminar \"" + sel.Nombre + "\" del catálogo? Esto NO borra los CFDI ya descargados, solo la FIEL guardada.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            BrosSatDb.EliminarEmpresa(_conn, sel.EmpresaID);
            CargarEmpresas();
        }

        // Respaldo cifrado de la configuracion de las empresas (FIEL, contrasenas, carpetas, conexion a Comercial).
        private void BtnRespaldo_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this,
                "¿Qué quieres hacer?\n\nSí = EXPORTAR un respaldo cifrado de todas las empresas.\nNo = IMPORTAR un respaldo (por ejemplo, en un servidor nuevo).\n\n" +
                "El archivo queda protegido con una contraseña que tú eliges y no se envía a ningún lado.",
                "Respaldo de empresas", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            try
            {
                if (r == MessageBoxResult.Yes)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Respaldo BrosLMV (*.brosbk)|*.brosbk", FileName = "empresas_" + DateTime.Today.ToString("yyyyMMdd") + ".brosbk" };
                    if (dlg.ShowDialog(this) != true) return;
                    var pw = new ContrasenaRespaldoWindow(true, "Contraseña del respaldo") { Owner = this };
                    if (pw.ShowDialog() != true) return;
                    int n = Respaldo.Exportar(_conn, dlg.FileName, pw.Contrasena);
                    MessageBox.Show(this, n + " empresa(s) respaldadas en:\n" + dlg.FileName + "\n\nGuarda el archivo y la contraseña en lugares distintos.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (r == MessageBoxResult.No)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Respaldo BrosLMV (*.brosbk)|*.brosbk" };
                    if (dlg.ShowDialog(this) != true) return;
                    var pw = new ContrasenaRespaldoWindow(false, "Contraseña del respaldo") { Owner = this };
                    if (pw.ShowDialog() != true) return;
                    string destino = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "fiel");
                    int n = Respaldo.Importar(_conn, dlg.FileName, pw.Contrasena, destino);
                    CargarEmpresas();
                    MessageBox.Show(this, n + " empresa(s) restauradas. Los archivos de la FIEL quedaron en:\n" + destino, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void NavSalud_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            new SaludWindow(_conn) { Owner = this }.ShowDialog();
        }

        private void NavBitacora_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            new BitacoraWindow { Owner = this }.ShowDialog();
        }

        private void NavReportes_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var empresa = _empresaActual ?? GridEmpresas.SelectedItem as EmpresaRow;
            if (empresa == null)
            {
                var todas = BrosSatDb.ObtenerEmpresas(_conn);
                if (todas.Count == 1) empresa = new EmpresaRow { RFC = todas[0].RFC, Nombre = todas[0].Nombre };
            }
            if (empresa == null)
            {
                MessageBox.Show(this, "Selecciona una empresa primero (doble clic en la lista de empresas).", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new ReportesWindow(_conn, empresa.RFC, empresa.Nombre) { Owner = this }.ShowDialog();
        }

        private EmpresaRow EmpresaParaModulo()
        {
            var empresa = _empresaActual ?? GridEmpresas.SelectedItem as EmpresaRow;
            if (empresa != null) return empresa;
            var todas = BrosSatDb.ObtenerEmpresas(_conn);
            return todas.Count == 1 ? new EmpresaRow { RFC = todas[0].RFC, Nombre = todas[0].Nombre } : null;
        }

        private void NavDescargas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var empresa = EmpresaParaModulo();
            if (empresa == null) { MessageBox.Show(this, "Selecciona una empresa primero.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            new DescargasWindow(_conn, empresa) { Owner = this }.ShowDialog();
        }

        private void NavActividad_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var empresa = EmpresaParaModulo();
            if (empresa == null) { MessageBox.Show(this, "Selecciona una empresa primero.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            new ActividadDescargasWindow(_conn, empresa) { Owner = this }.ShowDialog();
        }

        private void NavAutomatizacion_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            new AutomatizacionWindow { Owner = this }.ShowDialog();
        }

        private void GridEmpresas_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool haySeleccion = GridEmpresas.SelectedItem != null;
            BtnEliminarEmpresa.IsEnabled = haySeleccion;
            BtnEditarEmpresa.IsEnabled = haySeleccion;
        }

        private void GridEmpresas_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var sel = GridEmpresas.SelectedItem as EmpresaRow;
            if (sel == null) return;
            AbrirDetalle(sel);
        }

        // ---- Panel de detalle ----

        private void AbrirDetalle(EmpresaRow empresa)
        {
            _empresaActual = empresa;
            LblEmpresaTitulo.Text = empresa.Nombre + "  ·  " + empresa.RFC;
            PanelEmpresas.Visibility = Visibility.Collapsed;
            PanelDetalle.Visibility = Visibility.Visible;
            CargarDetalle();
        }

        private void BtnVolver_Click(object sender, RoutedEventArgs e)
        {
            PanelDetalle.Visibility = Visibility.Collapsed;
            PanelEmpresas.Visibility = Visibility.Visible;
            CargarEmpresas();
        }

        private void BtnAbrirDescargas_Click(object sender, RoutedEventArgs e)
        {
            if (_empresaActual != null) new DescargasWindow(_conn, _empresaActual) { Owner = this }.ShowDialog();
        }

        private void CargarDetalle()
        {
            var solicitudes = BrosSatDb.ObtenerSolicitudesRecientes(_conn, _empresaActual.RFC);
            _solicitudesActuales = solicitudes.Select(s => new SolicitudRow
            {
                IdSolicitud = s.IdSolicitud,
                Tipo = s.Tipo,
                TipoSolicitud = s.TipoSolicitud,
                Rango = s.FechaInicial.ToString("yyyy-MM-dd") + " a " + s.FechaFinal.ToString("yyyy-MM-dd"),
                EstatusSat = s.Estatus,
                ErrorDescarga = s.UltimoErrorPaquete,
                NumeroCFDIs = s.NumeroCFDIs?.ToString() ?? "-",
                Fecha = s.FechaSolicitud.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                FechaSolicitudUtc = s.FechaSolicitud
            }).ToList();

            int pendientes = _solicitudesActuales.Count(s => s.EstatusCategoria == "Pendiente");
            int terminadas = _solicitudesActuales.Count(s => s.EstatusCategoria == "Terminada");
            int errores = _solicitudesActuales.Count(s => s.EstatusCategoria == "Rechazada");
            CardPendientes.Text = pendientes.ToString();
            CardTerminadas.Text = terminadas.ToString();
            CardErrores.Text = errores.ToString();
            var cobertura = BrosSatDb.ObtenerCoberturaDescargas(_conn, _empresaActual.RFC);
            string ultimoXml = cobertura.UltimoXml.HasValue
                ? cobertura.UltimoXml.Value.ToLocalTime().ToString("dd/MM HH:mm")
                : "sin XML registrado";
            string metadata = cobertura.TotalMetadata == 0 ? "sin metadata" : cobertura.TotalMetadata.ToString("N0") + " metadata";
            LblActividad.Text = errores > 0
                ? errores.ToString() + " incidencias · último XML " + ultimoXml
                : "Último XML " + ultimoXml + " · " + metadata;
            AplicarFiltroSolicitudes();
        }

        // La vista abre en "En proceso": no se mezclan tareas activas con un archivo histórico.
        // Las filas "Reemplazada" se ocultan desde SQL porque son intentos caducados que no
        // requieren acción; no son un estado útil para quien opera la descarga.
        private void AplicarFiltroSolicitudes()
        {
            string categoria =
                TabSolicitudesPendientes.IsChecked == true ? "Pendiente" :
                TabSolicitudesTerminadas.IsChecked == true ? "Terminada" :
                "Rechazada";

            GridSolicitudes.ItemsSource = _solicitudesActuales
                .Where(s => s.EstatusCategoria == categoria).ToList();
        }

        private void FiltroSolicitud_Changed(object sender, RoutedEventArgs e)
        {
            if (_empresaActual == null) return; // el RadioButton "Todas" dispara Checked al construir la ventana
            AplicarFiltroSolicitudes();
        }

        private void BtnActualizarDetalle_Click(object sender, RoutedEventArgs e) => CargarDetalle();

        private void BtnLimpiarSolicitudes_Click(object sender, RoutedEventArgs e)
        {
            if (_empresaActual == null) return;
            var respuesta = MessageBox.Show(this,
                "Se ocultarán de esta vista las solicitudes terminadas o con error de más de 30 días, y todas las marcadas como reemplazadas.\n\n" +
                "No se borra ningún XML, metadata ni dato de auditoría. Las solicitudes que siguen trabajando nunca se ocultan.",
                "Limpiar historial", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (respuesta != MessageBoxResult.OK) return;

            int ocultadas = BrosSatDb.OcultarSolicitudesHistoricas(_conn, _empresaActual.RFC);
            CargarDetalle();
            MessageBox.Show(this, ocultadas == 0 ? "No había solicitudes históricas para ocultar." :
                ocultadas + " solicitudes históricas ya no aparecen en la vista diaria.",
                "Historial limpio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private (X509Certificate2 cert, RSA llave) CargarFielEmpresa(EmpresaRow empresa)
        {
            string password = DpapiHelper.Descifrar(empresa.PasswordCifrada);
            var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, password, out var llave);
            return (cert, llave);
        }

        private async void BtnRevisarPendientes_Click(object sender, RoutedEventArgs e)
        {
            await EjecutarConBloqueo(async () =>
            {
                var (cert, llave) = CargarFielEmpresa(_empresaActual);
                using (llave)
                    await SolicitudWorker.EjecutarPasadaAsync(_conn, cert, llave,
                        _empresaActual.CarpetaXml, _empresaActual.EstructuraCarpetas, _empresaActual.PlantillaNombreArchivo, _empresaActual.RFC,
                        _empresaActual.ComercialCarpetaXmlRecibidos, _empresaActual.ComercialCarpetaXmlEmitidos, _empresaActual.ComercialConexionSql);
            }, "Revisión de pendientes terminada.");
        }

        private async void BtnSincronizarComercial_Click(object sender, RoutedEventArgs e)
        {
            bool tieneAlgunaIntegracion = !string.IsNullOrWhiteSpace(_empresaActual.ComercialConexionSql) ||
                !string.IsNullOrWhiteSpace(_empresaActual.ComercialCarpetaXmlRecibidos) || !string.IsNullOrWhiteSpace(_empresaActual.ComercialCarpetaXmlEmitidos);
            if (!tieneAlgunaIntegracion)
            {
                MessageBox.Show(this, "Esta empresa no tiene configurada la integración con Comercial Pro. Configúrala en \"Editar\" primero.",
                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var empresaFila = new EmpresaFila
            {
                RFC = _empresaActual.RFC,
                Nombre = _empresaActual.Nombre,
                ComercialCarpetaXmlRecibidos = _empresaActual.ComercialCarpetaXmlRecibidos,
                ComercialCarpetaXmlEmitidos = _empresaActual.ComercialCarpetaXmlEmitidos,
                ComercialConexionSql = _empresaActual.ComercialConexionSql
            };

            if (_ocupado) return;
            _ocupado = true;
            try
            {
                var r = await ComercialSync.SincronizarTodoAsync(_conn, empresaFila);
                string mensaje = (r.ImportadosRecibidos + r.ImportadosEmitidos) + " CFDI importados directo a Comercial, " +
                    (r.CopiadosRecibidos + r.CopiadosEmitidos) + " copiados a carpeta.";
                if (r.SinArchivoEnDisco > 0) mensaje += "\n" + r.SinArchivoEnDisco + " CFDI sin XML en disco (se omitieron).";
                if (r.Errores > 0) mensaje += "\n" + r.Errores + " con error (ver Bitácora).";
                MessageBox.Show(this, mensaje, "Sincronización con Comercial", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo sincronizar:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _ocupado = false;
            }
        }

        private async void BtnNuevaSolicitud_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SolicitarWindow { Owner = this };
            if (dlg.ShowDialog() != true) return;

            await EjecutarConBloqueo(async () =>
            {
                var (cert, llave) = CargarFielEmpresa(_empresaActual);
                using (llave)
                {
                    var auth = await SatSoapClient.AutenticarAsync(cert, llave);
                    if (!auth.Exito) throw new Exception("Autenticación: " + auth.Error);

                    string tipo = dlg.EsRecibidos ? "Recibidos" : "Emitidos";

                    // Un rango grande en UNA sola SolicitaDescarga arriesga un paquete demasiado
                    // grande (visto en vivo: 7+ meses -> 1332 CFDIs, agoto sus 2 descargas con
                    // contenido vacio, CodEstatus=5008, bug #16 en DOCUMENTACION.md). SolicitarWindow
                    // ya lo partio en tramos de mes de calendario -- una SolicitaDescarga real por
                    // tramo, y dentro de cada tramo, CFDI + Metadata juntos (pedido explicito del
                    // usuario 2026-08-14): Metadata da el numero exacto de lo que existe en el SAT
                    // (incluye cancelados, que "CFDI" nunca entrega) y sirve de respaldo con datos
                    // basicos cuando la descarga del XML falla; conforme se descargan los XML reales
                    // via "CFDI", esos registros se van completando.
                    foreach (var (desdeTramo, hastaTramo) in dlg.Chunks)
                    {
                        var solicCfdi = await SatSoapClient.SolicitarDescargaAsync(
                            cert, llave, auth.Token,
                            rfcSolicitante: _empresaActual.RFC,
                            rfcEmisor: dlg.EsRecibidos ? null : _empresaActual.RFC,
                            rfcReceptor: dlg.EsRecibidos ? _empresaActual.RFC : null,
                            desde: desdeTramo, hasta: hastaTramo, tipoSolicitud: "CFDI");

                        if (!solicCfdi.Exito) throw new Exception("SolicitaDescarga (CFDI, " + desdeTramo.ToString("yyyy-MM") + "): " + solicCfdi.Error);
                        BrosSatDb.RegistrarSolicitud(_conn, solicCfdi.IdSolicitud, _empresaActual.RFC, tipo, desdeTramo, hastaTramo, "Manual", "CFDI");

                        var solicMetadata = await SatSoapClient.SolicitarDescargaAsync(
                            cert, llave, auth.Token,
                            rfcSolicitante: _empresaActual.RFC,
                            rfcEmisor: dlg.EsRecibidos ? null : _empresaActual.RFC,
                            rfcReceptor: dlg.EsRecibidos ? _empresaActual.RFC : null,
                            desde: desdeTramo, hasta: hastaTramo, tipoSolicitud: "Metadata");

                        if (!solicMetadata.Exito) throw new Exception("SolicitaDescarga (Metadata, " + desdeTramo.ToString("yyyy-MM") + "): " + solicMetadata.Error);
                        BrosSatDb.RegistrarSolicitud(_conn, solicMetadata.IdSolicitud, _empresaActual.RFC, tipo, desdeTramo, hastaTramo, "Manual", "Metadata");
                    }
                }
            }, "Solicitud(es) enviada(s) al SAT (CFDI + Metadata por cada mes). Usa \"Revisar pendientes\" en unos minutos para ver el estatus.");
        }

        private async Task EjecutarConBloqueo(Func<Task> accion, string mensajeExito)
        {
            if (_ocupado) return;
            _ocupado = true;
            try
            {
                await accion();
                CargarDetalle();
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

        protected override void OnClosed(EventArgs e)
        {
            _conn?.Dispose();
            base.OnClosed(e);
        }
    }
}
