// PaginaMapa.xaml.cs -- «Mapa de CFDI»: donde esta cada CFDI de la empresa activa (SAT, XML en disco, Comercial, ADD, polizas).
// Todo sale de lo que MapaCfdi leyo de cada sistema (solo lectura); «Verificar ahora» lo vuelve a leer. Las acciones de esta pagina
// (cargar al ADD, importar a Comercial) solo tocan los CFDI que FALTAN en lo que se esta viendo, y despues vuelven a leer ese sistema.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using ClosedXML.Excel;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaMapa : UserControl
    {
        // Fila de la tabla por mes. PROPIEDADES: WPF no resuelve campos.
        public sealed class MapaVista
        {
            internal MapaFila Fila;
            public string MesTexto => Fila.MesTexto;
            public string Tipo => Fila.Tipo;
            public string Sat { get; set; }
            public string Descargados { get; set; }
            public string SoloAdd { get; set; }
            public Style EstiloSoloAdd { get; set; }
            public string Comercial { get; set; }
            public string Premium { get; set; }
            public string Add { get; set; }
            public string Poliza { get; set; }
            public string FaltaAdd { get; set; }
            public string FaltaComercial { get; set; }
            public string SinPoliza { get; set; }
            public Style EstiloFaltaAdd { get; set; }
            public Style EstiloFaltaComercial { get; set; }
            public Style EstiloSinPoliza { get; set; }
        }

        private readonly Sesion _s;
        private Dictionary<string, ResultadoVerificacion> _verif = new Dictionary<string, ResultadoVerificacion>();
        private List<MapaFila> _filas = new List<MapaFila>();
        private bool _cargando;
        private bool _ocupado;
        private bool _pestanaElegida;
        private const int LimiteDetalle = 3000;

        public PaginaMapa(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            ChkNomina.IsChecked = !_s.Empresa.OcultarNominas;
            Loaded += async (s, e) => await IniciarAsync();
        }

        private Window Ventana => Window.GetWindow(this);
        private string Rfc => _s.Empresa.RFC;
        private bool IncluirNomina => ChkNomina.IsChecked == true;

        // ---- Estado de cada sistema ----
        // Un sistema que la empresa no usa no cuenta aunque queden lecturas viejas.
        private bool Usa(string sistema) => sistema == SistemaCfdi.Comercial ? _s.Perfil.ComercialPro : sistema == SistemaCfdi.Premium ? _s.Perfil.ComercialPremium : (sistema == SistemaCfdi.Add || sistema == SistemaCfdi.Polizas) ? _s.Perfil.Contabilidad : true;
        private bool Ok(string sistema) => Usa(sistema) && _verif.TryGetValue(sistema, out var v) && v.Ok;
        private bool Configurado(string sistema) => Usa(sistema) && _verif.TryGetValue(sistema, out var v) && v.Configurado;

        private async Task IniciarAsync()
        {
            Cargar();
            // Primera vez: no hay nada leido todavia, se lee solo para no mostrar una pantalla vacia.
            // (o cuando la empresa empieza a usar un sistema del que todavia no se ha leido nada).
            bool faltaLeer = new[] { SistemaCfdi.Disco, SistemaCfdi.Comercial, SistemaCfdi.Premium, SistemaCfdi.Add, SistemaCfdi.Polizas }.Any(x => Usa(x) && !_verif.ContainsKey(x));
            if (faltaLeer && _filas.Count > 0) await VerificarAsync();
        }

        // Lo de un sistema que la empresa no usa no se muestra (ni tarjeta, ni columnas, ni pestañas).
        private void AplicarPerfil()
        {
            var perfil = _s.Perfil;
            var com = perfil.ComercialPro ? Visibility.Visible : Visibility.Collapsed; var con = perfil.Contabilidad ? Visibility.Visible : Visibility.Collapsed;
            var pre = perfil.ComercialPremium ? Visibility.Visible : Visibility.Collapsed;
            CardComercial.Visibility = com; CardPremium.Visibility = pre; CardAdd.Visibility = con; CardPoliza.Visibility = con;
            // columnas de la tabla por mes: 4 SOLO EN ADD · 5 COMERCIAL · 6 PREMIUM · 7 ADD · 8 CON POLIZA · 9 FALTAN EN ADD · 10 FALTAN EN COMERCIAL · 11 SIN POLIZA
            foreach (int i in new[] { 4, 7, 8, 9, 11 }) GridMapa.Columns[i].Visibility = con;
            foreach (int i in new[] { 5, 10 }) GridMapa.Columns[i].Visibility = com;
            GridMapa.Columns[6].Visibility = pre;
            // detalle: 8 COMERCIAL · 9 PREMIUM · 10 ADD · 11 POLIZAS
            GridDetalle.Columns[8].Visibility = com; GridDetalle.Columns[9].Visibility = pre; GridDetalle.Columns[10].Visibility = con; GridDetalle.Columns[11].Visibility = con;
        }

        private void Cargar()
        {
            _cargando = true;
            try
            {
                AplicarPerfil();
                _verif = MapaCfdi.ObtenerVerificaciones(_s.Conn, Rfc);
                _filas = MapaCfdi.Calcular(_s.Conn, Rfc, IncluirNomina);
                PintarTabla();
                PintarCifras();
                PintarAviso();
                TabFaltaAdd.Visibility = Ok(SistemaCfdi.Add) ? Visibility.Visible : Visibility.Collapsed;
                TabSoloAdd.Visibility = Ok(SistemaCfdi.Add) && _filas.Any(f => f.SoloSistemas > 0) ? Visibility.Visible : Visibility.Collapsed;
                TabFaltaComercial.Visibility = Ok(SistemaCfdi.Comercial) ? Visibility.Visible : Visibility.Collapsed;
                TabSinPoliza.Visibility = Ok(SistemaCfdi.Polizas) ? Visibility.Visible : Visibility.Collapsed;
                BtnCargarAdd.Visibility = Ok(SistemaCfdi.Add) ? Visibility.Visible : Visibility.Collapsed;
                BtnCargarPremium.Visibility = Ok(SistemaCfdi.Premium) && !string.IsNullOrWhiteSpace(BrosSatDb.ObtenerPremium(_s.Conn, Rfc)?.Usuario) ? Visibility.Visible : Visibility.Collapsed;
                BtnImportarComercial.Visibility = Ok(SistemaCfdi.Comercial) ? Visibility.Visible : Visibility.Collapsed;
                Vacio.Visibility = _filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // La primera vez se abre en lo que mas interesa: lo que falta en el ADD (o en Comercial); despues se respeta la eleccion.
                if (!_pestanaElegida && _filas.Count > 0)
                {
                    _pestanaElegida = true;
                    if (Ok(SistemaCfdi.Add) && _filas.Sum(f => f.FaltaAdd) > 0) TabFaltaAdd.IsChecked = true;
                    else if (Ok(SistemaCfdi.Comercial) && _filas.Sum(f => f.FaltaComercial) > 0) TabFaltaComercial.IsChecked = true;
                }
            }
            catch (Exception ex) { LblVerificacion.Text = "No se pudo leer el mapa: " + ex.Message; }
            finally { _cargando = false; }
            CargarDetalle();
        }

        private void PintarTabla()
        {
            Style Est(int n, bool activo) => (Style)FindResource(!activo ? "PastillaNeutra" : n > 0 ? "PastillaAviso" : "PastillaOk");
            string N(int n, bool activo) => activo ? n.ToString("N0") : "—";
            bool com = Ok(SistemaCfdi.Comercial), add = Ok(SistemaCfdi.Add), pol = Ok(SistemaCfdi.Polizas);
            GridMapa.ItemsSource = _filas.Select(f => new MapaVista
            {
                Fila = f, Sat = f.SatTotal > 0 ? f.SatTotal.ToString("N0") : "—", Descargados = f.Descargados.ToString("N0"),
                SoloAdd = N(f.SoloSistemas, add), EstiloSoloAdd = (Style)FindResource(!add ? "PastillaNeutra" : f.SoloSistemas > 0 ? "PastillaInfo" : "PastillaNeutra"),
                Comercial = N(f.EnComercial, com), Premium = N(f.EnPremium, Ok(SistemaCfdi.Premium)), Add = N(f.EnAdd, add), Poliza = N(f.ConPoliza, pol),
                FaltaAdd = N(f.FaltaAdd, add), FaltaComercial = N(f.FaltaComercial, com), SinPoliza = N(f.SinPoliza, pol),
                EstiloFaltaAdd = Est(f.FaltaAdd, add), EstiloFaltaComercial = Est(f.FaltaComercial, com), EstiloSinPoliza = Est(f.SinPoliza, pol)
            }).ToList();
        }

        private void PintarCifras()
        {
            int sat = _filas.Sum(f => f.SatTotal), satVig = _filas.Sum(f => f.SatVigentes), desc = _filas.Sum(f => f.Descargados), sinArch = _filas.Sum(f => f.FaltaDisco);
            K1.Text = sat > 0 ? sat.ToString("N0") : "—"; K1d.Text = sat > 0 ? satVig.ToString("N0") + " vigentes (Metadata)" : "Sin Metadata todavía";
            int solo = _filas.Sum(f => f.SoloSistemas);
            K2.Text = desc.ToString("N0");
            K2d.Text = (sinArch > 0 ? sinArch.ToString("N0") + " sin archivo en disco" : "todos con su archivo") + (Ok(SistemaCfdi.Add) && solo > 0 ? " · " + solo.ToString("N0") + " solo en el ADD" : "");

            if (Ok(SistemaCfdi.Comercial)) { K3.Text = _filas.Sum(f => f.EnComercial).ToString("N0"); int fa = _filas.Sum(f => f.FaltaComercial); K3d.Text = _filas.Sum(f => f.ConDocumento).ToString("N0") + " con documento · " + (fa > 0 ? fa.ToString("N0") + " faltan" : "nada falta"); }
            else { K3.Text = "—"; K3d.Text = Configurado(SistemaCfdi.Comercial) ? "no se pudo leer" : "sin configurar"; }

            if (Ok(SistemaCfdi.Premium))
            {
                int enPremium = _filas.Sum(f => f.EnPremium), total = _filas.Sum(f => f.Descargados);
                K6.Text = enPremium.ToString("N0"); K6d.Text = "de " + total.ToString("N0") + " tuyos (emitidos y recibidos)";
            }
            else { K6.Text = "—"; K6d.Text = Configurado(SistemaCfdi.Premium) ? "no se pudo leer" : "sin configurar"; }

            if (Ok(SistemaCfdi.Add)) { K4.Text = _filas.Sum(f => f.EnAdd).ToString("N0"); int fa = _filas.Sum(f => f.FaltaAdd); K4d.Text = fa > 0 ? fa.ToString("N0") + " faltan" : "nada falta"; }
            else { K4.Text = "—"; K4d.Text = Configurado(SistemaCfdi.Add) ? "no se pudo leer" : "sin configurar"; }

            if (Ok(SistemaCfdi.Polizas)) { K5.Text = _filas.Sum(f => f.ConPoliza).ToString("N0"); int sp = _filas.Sum(f => f.SinPoliza); K5d.Text = sp > 0 ? sp.ToString("N0") + " sin póliza" : "todos con póliza"; }
            else { K5.Text = "—"; K5d.Text = Configurado(SistemaCfdi.Polizas) ? "no se pudo leer" : "sin configurar"; }
        }

        private void PintarAviso()
        {
            if (_verif.Count == 0) { Aviso.Style = (Style)FindResource("BandaInfo"); LblVerificacion.Text = "Todavía no se ha leído dónde está cada CFDI. Pulsa «Verificar ahora»."; return; }
            if (_s.Perfil.SoloDescargar) { Aviso.Style = (Style)FindResource("BandaInfo"); LblVerificacion.Text = "Esta empresa solo descarga y analiza los XML (no usa Comercial ni Contabilidad). Para ver también dónde está cada CFDI en esos sistemas, actívalos en «Empresas y e.firma» → Editar."; return; }
            var partes = new List<string>(); bool hayError = false;
            foreach (var s in new[] { SistemaCfdi.Disco, SistemaCfdi.Comercial, SistemaCfdi.Premium, SistemaCfdi.Add, SistemaCfdi.Polizas })
            {
                if (!_verif.TryGetValue(s, out var v)) continue;
                if (!v.Configurado) { if ((s == SistemaCfdi.Comercial && _s.Perfil.ComercialPro) || (s == SistemaCfdi.Premium && _s.Perfil.ComercialPremium) || ((s == SistemaCfdi.Add || s == SistemaCfdi.Polizas) && _s.Perfil.Contabilidad)) partes.Add(SistemaCfdi.Nombre(s) + ": sin configurar"); continue; }
                if (!v.Ok) { hayError = true; partes.Add(SistemaCfdi.Nombre(s) + ": " + (v.Mensaje ?? "error")); continue; }
                partes.Add(SistemaCfdi.Nombre(s) + " ✓ " + Hace(v.Fecha));
            }
            Aviso.Style = (Style)FindResource(hayError ? "BandaAviso" : "BandaInfo");
            LblVerificacion.Text = string.Join("   ·   ", partes);
        }

        private static string Hace(DateTime? f)
        {
            if (!f.HasValue) return "";
            var d = DateTime.Now - f.Value;
            return d.TotalMinutes < 1 ? "hace un momento" : d.TotalMinutes < 60 ? "hace " + (int)d.TotalMinutes + " min" : d.TotalHours < 48 ? "hace " + (int)d.TotalHours + " h" : "hace " + (int)d.TotalDays + " días";
        }

        // ---- Detalle ----

        private MapaFila Seleccion => (GridMapa.SelectedItem as MapaVista)?.Fila;

        private string FiltroActual =>
            TabFaltaAdd.IsChecked == true ? "FaltaAdd" : TabSoloAdd.IsChecked == true ? "SoloSistemas" : TabFaltaComercial.IsChecked == true ? "FaltaComercial" : TabSinPoliza.IsChecked == true ? "SinPoliza" : "Todos";

        private void GridMapa_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_cargando) CargarDetalle(); }
        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded && !_cargando) CargarDetalle(); }
        private void ChkNomina_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Cargar(); }

        private void CargarDetalle()
        {
            try
            {
                // Una pestaña oculta no puede quedar activa.
                if ((TabFaltaAdd.IsChecked == true && TabFaltaAdd.Visibility != Visibility.Visible) || (TabSoloAdd.IsChecked == true && TabSoloAdd.Visibility != Visibility.Visible) || (TabFaltaComercial.IsChecked == true && TabFaltaComercial.Visibility != Visibility.Visible) ||
                    (TabSinPoliza.IsChecked == true && TabSinPoliza.Visibility != Visibility.Visible)) { _cargando = true; TabTodos.IsChecked = true; _cargando = false; }
                var sel = Seleccion;
                var lista = MapaCfdi.ObtenerCfdis(_s.Conn, Rfc, IncluirNomina, FiltroActual, sel?.Anio, sel?.Mes, sel?.Tipo, LimiteDetalle);
                GridDetalle.ItemsSource = lista;
                string donde = sel == null ? "Todos los meses" : sel.MesTexto + " · " + sel.Tipo;
                LblAlcance.Text = donde + " · " + lista.Count.ToString("N0") + " CFDI" + (lista.Count >= LimiteDetalle ? " (los primeros " + LimiteDetalle.ToString("N0") + ")" : "") + "  —  doble clic para ver el detalle";
            }
            catch (Exception ex) { LblAlcance.Text = "No se pudo leer el detalle: " + ex.Message; }
        }

        private void GridDetalle_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(GridDetalle.SelectedItem is MapaCfdiFila f)) return;
            if (f.Externo)
            {
                MessageBox.Show(Ventana, "Este CFDI está en el ADD de Contabilidad pero todavía no lo has descargado del SAT, así que Descargas no tiene su XML ni su detalle.\n\n" +
                    "Descarga el mes " + f.Fecha.ToString("yyyy-MM") + " en «Descargas del SAT» → Nueva descarga.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new CfdiDetalleWindow(_s.Conn, f.CfdiId) { Owner = Ventana }.Show();
        }

        // ---- Verificar ----

        private void Bloquear(bool bloqueado)
        {
            _ocupado = bloqueado;
            BtnVerificar.IsEnabled = !bloqueado; BtnCargarAdd.IsEnabled = !bloqueado; BtnCargarPremium.IsEnabled = !bloqueado; BtnImportarComercial.IsEnabled = !bloqueado; ChkNomina.IsEnabled = !bloqueado;
            Cursor = bloqueado ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        }

        private void Estado(string texto) => Dispatcher.Invoke(() => { Aviso.Style = (Style)FindResource("BandaInfo"); LblVerificacion.Text = texto; });

        private async void BtnVerificar_Click(object sender, RoutedEventArgs e) => await VerificarAsync();

        private async Task VerificarAsync()
        {
            if (_ocupado) return;
            Bloquear(true);
            try
            {
                var empresa = Sesion.AFila(_s.Empresa);
                await Task.Run(() => MapaCfdi.VerificarTodoAsync(_s.Conn, empresa, Estado).GetAwaiter().GetResult());
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo verificar:\n\n" + ex.Message, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { Bloquear(false); Cargar(); }
        }

        // ---- Acciones sobre lo que falta ----

        private List<MapaCfdiFila> Faltantes(string filtro)
        {
            var sel = Seleccion;
            return MapaCfdi.ObtenerCfdis(_s.Conn, Rfc, IncluirNomina, filtro, sel?.Anio, sel?.Mes, sel?.Tipo, 1000000);
        }

        private async void BtnCargarAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_ocupado) return;
            var cfg = BrosSatDb.ObtenerContabilidad(_s.Conn, Rfc);
            if (cfg == null) { MessageBox.Show(Ventana, "Esta empresa no tiene Contabilidad configurada. Configúrala en «Empresas y e.firma» → Editar.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var faltan = Faltantes("FaltaAdd");
            if (faltan.Count == 0) { MessageBox.Show(Ventana, "En lo que estás viendo no falta ningún CFDI vigente en el ADD.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            double minutos = faltan.Count / 50.0;
            string tiempo = minutos < 1 ? "menos de un minuto" : minutos < 90 ? "unos " + (int)Math.Ceiling(minutos) + " minutos" : "unas " + Math.Round(minutos / 60.0, 1) + " horas";
            var sel = Seleccion;
            if (MessageBox.Show(Ventana, "Se cargarán " + faltan.Count.ToString("N0") + " CFDI al ADD de Contabilidad (" + (sel == null ? "todos los meses" : sel.MesTexto + " · " + sel.Tipo) + ").\n\n" +
                "CONTPAQi procesa unos 50 por minuto: " + tiempo + ". Mientras trabaja puedes seguir usando Descargas.\n\n¿Continuar?", "Cargar al ADD", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            Bloquear(true);
            try
            {
                var uuids = new HashSet<Guid>(faltan.Select(f => f.Uuid));
                var r = await Task.Run(() => ContabilidadAdd.CargarAsync(_s.Conn, Rfc, cfg, Estado, 0, uuids));
                if (!r.Exito) MessageBox.Show(Ventana, "No se pudo cargar al ADD:\n\n" + r.Error, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Warning);
                else MessageBox.Show(Ventana, r.Cargados.ToString("N0") + " CFDI cargados al ADD." + (r.NoSeCargaron > 0 ? "\n" + r.NoSeCargaron + " no se pudieron cargar (CONTPAQi los rechazó)." : ""), "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information);
                var empresa = Sesion.AFila(_s.Empresa);
                Estado("Leyendo de nuevo el ADD...");
                await Task.Run(() => { MapaCfdi.VerificarUnoAsync(_s.Conn, empresa, SistemaCfdi.Add).GetAwaiter().GetResult(); MapaCfdi.VerificarUnoAsync(_s.Conn, empresa, SistemaCfdi.Polizas).GetAwaiter().GetResult(); });
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo cargar:\n\n" + ex.Message, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Bloquear(false); Cargar(); }
        }

        private async void BtnCargarPremium_Click(object sender, RoutedEventArgs e)
        {
            if (_ocupado) return;
            var cfg = BrosSatDb.ObtenerPremium(_s.Conn, Rfc);
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Usuario)) { MessageBox.Show(Ventana, "Falta el usuario de Premium. Configúralo en «Empresas y e.firma» → Editar.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var faltan = Faltantes("FaltaPremium");
            if (faltan.Count == 0) { MessageBox.Show(Ventana, "En lo que estás viendo no falta ningún CFDI vigente en Premium.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var sel = Seleccion;
            if (MessageBox.Show(Ventana, "Se cargarán " + faltan.Count.ToString("N0") + " CFDI al ADD de Comercial Premium (" + (sel == null ? "todos los meses" : sel.MesTexto + " · " + sel.Tipo) + ").\n\nCONTPAQi procesa unos 50 por minuto. Mientras trabaja puedes seguir usando Descargas.\n\n¿Continuar?", "Cargar a Premium", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            Bloquear(true);
            try
            {
                var uuids = new HashSet<Guid>(faltan.Select(f => f.Uuid));
                var r = await Task.Run(() => ComercialPremium.CargarAsync(_s.Conn, Rfc, cfg, Estado, 0, uuids));
                if (!r.Exito) MessageBox.Show(Ventana, "No se pudo cargar a Premium:\n\n" + r.Error, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Warning);
                else MessageBox.Show(Ventana, r.Cargados.ToString("N0") + " CFDI cargados al ADD de Premium." + (r.NoSeCargaron > 0 ? "\n" + r.NoSeCargaron + " no se pudieron cargar (CONTPAQi los rechazó)." : ""), "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information);
                Estado("Leyendo de nuevo Premium...");
                var empresa = Sesion.AFila(_s.Empresa);
                await Task.Run(() => MapaCfdi.VerificarUnoAsync(_s.Conn, empresa, SistemaCfdi.Premium).GetAwaiter().GetResult());
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo cargar:\n\n" + ex.Message, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Bloquear(false); Cargar(); }
        }

        private async void BtnImportarComercial_Click(object sender, RoutedEventArgs e)
        {
            if (_ocupado) return;
            var emp = _s.Empresa;
            if (string.IsNullOrWhiteSpace(emp.ComercialConexionSql)) { MessageBox.Show(Ventana, "Esta empresa no tiene conexión directa a Comercial. Configúrala en «Empresas y e.firma» → Editar.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var faltan = Faltantes("FaltaComercial");
            if (faltan.Count == 0) { MessageBox.Show(Ventana, "En lo que estás viendo no falta ningún CFDI vigente en la lista de Comercial.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var sel = Seleccion;
            if (MessageBox.Show(Ventana, "Se agregarán " + faltan.Count.ToString("N0") + " CFDI a la lista de XML de Comercial (" + (sel == null ? "todos los meses" : sel.MesTexto + " · " + sel.Tipo) + ").\n\nNo se crea ningún documento ni póliza.", "Importar a Comercial", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            Bloquear(true);
            try
            {
                var empresa = Sesion.AFila(emp);
                var uuids = new HashSet<Guid>(faltan.Select(f => f.Uuid));
                var r = await Task.Run(() => ComercialSync.SincronizarUuidsAsync(_s.Conn, empresa, uuids, Estado));
                string msg = (r.ImportadosRecibidos + r.ImportadosEmitidos).ToString("N0") + " CFDI agregados a la lista de Comercial.";
                if (r.SinArchivoEnDisco > 0) msg += "\n" + r.SinArchivoEnDisco + " sin XML en disco (se omitieron).";
                if (r.Errores > 0) msg += "\n" + r.Errores + " con error (ver Bitácora).";
                MessageBox.Show(Ventana, msg, "Mapa de CFDI", MessageBoxButton.OK, r.Errores > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                Estado("Leyendo de nuevo Comercial...");
                await Task.Run(() => MapaCfdi.VerificarUnoAsync(_s.Conn, empresa, SistemaCfdi.Comercial).GetAwaiter().GetResult());
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo importar:\n\n" + ex.Message, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { Bloquear(false); Cargar(); }
        }

        // ---- Exportar ----

        private void BtnExpediente_Click(object sender, RoutedEventArgs e) =>
            new ExpedienteWindow(_s.Conn, Rfc, _s.Empresa.Nombre) { Owner = Ventana }.Show();

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (_filas.Count == 0) { MessageBox.Show(Ventana, "No hay nada que exportar todavía.", "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "mapa_cfdi_" + Rfc + "_" + DateTime.Today.ToString("yyyyMMdd") + ".xlsx" };
            if (dlg.ShowDialog(Ventana) != true) return;
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet("Por mes");
                    string[] cab = { "Mes", "Tipo", "SAT reporta", "SAT vigentes", "Tus XML", "Solo en el ADD", "XML en disco", "En Comercial", "Con documento", "En Premium", "En el ADD", "Con póliza", "Faltan en el ADD", "Faltan en Comercial", "Sin póliza" };
                    for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                    int r = 2;
                    foreach (var f in _filas)
                    {
                        object[] v = { f.MesTexto, f.Tipo, f.SatTotal, f.SatVigentes, f.Descargados, f.SoloSistemas, f.EnDisco, f.EnComercial, f.ConDocumento, f.EnPremium, f.EnAdd, f.ConPoliza, f.FaltaAdd, f.FaltaComercial, f.SinPoliza };
                        for (int i = 0; i < v.Length; i++) ws.Cell(r, i + 1).Value = XLCellValue.FromObject(v[i]);
                        r++;
                    }
                    Encabezado(ws, cab.Length);

                    var ws2 = wb.AddWorksheet("CFDI");
                    string[] cab2 = { "Fecha", "Dirección", "Tipo", "Contraparte", "Serie/Folio", "Total", "Estatus SAT", "XML", "Comercial", "Premium", "ADD", "Pólizas", "UUID" };
                    for (int i = 0; i < cab2.Length; i++) ws2.Cell(1, i + 1).Value = cab2[i];
                    int r2 = 2;
                    foreach (var c in MapaCfdi.ObtenerCfdis(_s.Conn, Rfc, IncluirNomina, "Todos", null, null, null, 1000000))
                    {
                        ws2.Cell(r2, 1).Value = c.Fecha; ws2.Cell(r2, 1).Style.DateFormat.Format = "yyyy-mm-dd";
                        ws2.Cell(r2, 2).Value = c.Direccion; ws2.Cell(r2, 3).Value = c.TipoComprobante; ws2.Cell(r2, 4).Value = c.Contraparte; ws2.Cell(r2, 5).Value = c.SerieFolio;
                        ws2.Cell(r2, 6).Value = c.Total; ws2.Cell(r2, 6).Style.NumberFormat.Format = "#,##0.00"; ws2.Cell(r2, 7).Value = c.Estatus; ws2.Cell(r2, 8).Value = c.Disco;
                        ws2.Cell(r2, 9).Value = c.Comercial; ws2.Cell(r2, 10).Value = c.Premium; ws2.Cell(r2, 11).Value = c.Add; ws2.Cell(r2, 12).Value = c.Polizas; ws2.Cell(r2, 13).Value = c.UuidTexto;
                        r2++;
                    }
                    Encabezado(ws2, cab2.Length);
                    wb.SaveAs(dlg.FileName);
                }
                Cursor = System.Windows.Input.Cursors.Arrow;
                MessageBox.Show(Ventana, "Archivo guardado:\n" + dlg.FileName, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { Cursor = System.Windows.Input.Cursors.Arrow; MessageBox.Show(Ventana, "No se pudo exportar:\n\n" + ex.Message, "Mapa de CFDI", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private static void Encabezado(IXLWorksheet ws, int columnas)
        {
            var fila = ws.Range(1, 1, 1, columnas);
            fila.Style.Font.Bold = true; fila.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2B4A"); fila.Style.Font.FontColor = XLColor.White;
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, 200);
        }
    }
}
