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

// EmpresaWindow.xaml.cs -- valida la FIEL (carga real con SatFirmaXml.CargarFiel) ANTES de
// guardar, para no descubrir hasta la primera descarga automatica que la contrasena estaba
// mal escrita. La contrasena se cifra (DPAPI) y se guarda -- nunca queda en texto plano.
//
// Sirve para alta Y edicion (constructor con "existente" != null) -- pedido explicito del
// usuario 2026-08-14: poder elegir/cambiar la carpeta de los XML, la estructura de carpetas y
// la plantilla de nombre de archivo por empresa, no solo al momento de crearla.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;

namespace BrosLMV.DescargasUI
{
    public partial class EmpresaWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly EmpresaFila _existente;
        public bool SeGuardo { get; private set; }

        internal EmpresaWindow(SqlConnection conn, EmpresaFila existente = null)
        {
            InitializeComponent();
            _conn = conn;
            _existente = existente;

            // El año de inicio solo aplica al DAR DE ALTA (pedido explicito del usuario
            // 2026-08-19: "al instalar lo primero que haga sea realizar una descarga" -- se
            // pregunta una sola vez, no tiene sentido volver a pedirlo al editar una empresa que
            // ya tiene historial). Ultimos 4 años, mas reciente primero.
            int anioActual = DateTime.Today.Year;
            for (int anio = anioActual; anio >= anioActual - 3; anio--)
                CmbAnioInicio.Items.Add(anio);
            CmbAnioInicio.SelectedIndex = 0;

            if (_existente != null)
            {
                PanelAnioInicio.Visibility = Visibility.Collapsed;
                LblTitulo.Text = "Editar empresa";
                BtnGuardar.Content = "Guardar cambios";
                TxtNombre.Text = _existente.Nombre;
                TxtRfc.Text = _existente.RFC;
                TxtRfc.IsEnabled = false; // cambiar el RFC de una empresa ya usada es riesgoso -- ver DOCUMENTACION
                TxtCer.Text = _existente.RutaCer;
                TxtKey.Text = _existente.RutaKey;
                LblPassword.Text = "Contraseña de la FIEL (dejar en blanco para no cambiarla)";
                LblPasswordAyuda.Text = "Se guarda cifrada (DPAPI, atada a este equipo). Si la dejas en blanco se conserva la que ya estaba guardada.";
                TxtCarpetaXml.Text = _existente.CarpetaXml ?? "";
                TxtPlantilla.Text = _existente.PlantillaNombreArchivo;
                SeleccionarEstructura(_existente.EstructuraCarpetas);
                ChkOcultarNominas.IsChecked = _existente.OcultarNominas;
                TxtComercialRecibidos.Text = _existente.ComercialCarpetaXmlRecibidos ?? "";
                TxtComercialEmitidos.Text = _existente.ComercialCarpetaXmlEmitidos ?? "";
                CargarConexionComercialExistente(_existente.ComercialConexionSql);
            }
            else
            {
                ChkOcultarNominas.IsChecked = true;
            }

            ActualizarPanelComercialAuth();
            CargarContabilidad();
            LlenarEmpresasPremium();
            _perfilInicial = _existente == null ? new PerfilEmpresa() : BrosSatDb.ObtenerPerfil(_conn, _existente.RFC);
            ChkPerfilComercial.IsChecked = _perfilInicial.ComercialPro; ChkPerfilConta.IsChecked = _perfilInicial.Contabilidad; ChkPerfilPremium.IsChecked = _perfilInicial.ComercialPremium;
            Perfil_Changed(null, null);
        }

        // ---- Sistemas que usa la empresa ----

        private PerfilEmpresa _perfilInicial = new PerfilEmpresa();

        // Cada sistema marcado muestra su configuracion; sin marcar, se oculta (y al guardar se quita su integracion).
        private void Perfil_Changed(object sender, RoutedEventArgs e)
        {
            if (CardComercialEmp == null || CardContaEmp == null || CardPremiumEmp == null) return;   // durante la carga
            CardPremiumEmp.Visibility = ChkPerfilPremium.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CardComercialEmp.Visibility = ChkPerfilComercial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CardContaEmp.Visibility = ChkPerfilConta.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LblPerfilResumen.Text = ChkPerfilComercial.IsChecked != true && ChkPerfilConta.IsChecked != true && ChkPerfilPremium.IsChecked != true
                ? "Esta empresa solo descarga y analiza los XML (sin integración)."
                : "Se integrará con: " + string.Join(" + ", new[] { ChkPerfilComercial.IsChecked == true ? "Comercial Pro" : null, ChkPerfilPremium.IsChecked == true ? "Comercial Premium" : null, ChkPerfilConta.IsChecked == true ? "Contabilidad" : null }.Where(x => x != null)) + ".";
        }

        private PerfilEmpresa PerfilElegido() => new PerfilEmpresa { ComercialPro = ChkPerfilComercial.IsChecked == true, Contabilidad = ChkPerfilConta.IsChecked == true, ComercialPremium = ChkPerfilPremium.IsChecked == true };

        // ---- Comercial Premium (solo lectura) ----

        private PremiumConfig LeerPremium(string rfc)
        {
            if (!(CmbPremium.SelectedItem is EmpresaPremiumInfo ep)) return null;
            return new PremiumConfig
            {
                RFC = rfc, BaseDatos = ep.BaseDatos, Usuario = string.IsNullOrWhiteSpace(TxtPremiumUsuario.Text) ? "SUPERVISOR" : TxtPremiumUsuario.Text.Trim(),
                ClaveCifrada = string.IsNullOrEmpty(PwdPremiumClave.Password) ? null : DpapiHelper.Cifrar(PwdPremiumClave.Password),
                CargaAutomatica = ChkPremiumAuto.IsChecked == true
            };
        }

        private async void ProbarPremium_Click(object sender, RoutedEventArgs e)
        {
            var cfg = LeerPremium((_existente?.RFC ?? TxtRfc.Text ?? "").Trim());
            if (cfg == null) { LblPremiumEstado.Text = "Elige primero la empresa de Premium."; return; }
            LblPremiumEstado.Text = "Probando acceso...";
            try { string error = await ComercialPremium.ProbarAccesoAsync(_conn, cfg); LblPremiumEstado.Text = error == null ? "Acceso correcto: se puede cargar al ADD de Premium." : error; }
            catch (Exception ex) { LblPremiumEstado.Text = ex.Message; }
        }

        private void ActualizarListaPremium_Click(object sender, RoutedEventArgs e) => LlenarEmpresasPremium();

        private void LlenarEmpresasPremium()
        {
            try
            {
                var lista = ComercialPremium.ListarEmpresas(_conn);
                string rfc = (_existente?.RFC ?? TxtRfc.Text ?? "").Trim();
                var guardada = _existente == null ? null : BrosSatDb.ObtenerPremium(_conn, _existente.RFC);
                CmbPremium.ItemsSource = lista;
                CmbPremium.SelectedItem = lista.FirstOrDefault(x => guardada != null && string.Equals(x.BaseDatos, guardada.BaseDatos, StringComparison.OrdinalIgnoreCase))
                                       ?? lista.FirstOrDefault(x => rfc.Length > 0 && string.Equals(x.Rfc, rfc, StringComparison.OrdinalIgnoreCase));
                if (guardada != null) { TxtPremiumUsuario.Text = string.IsNullOrWhiteSpace(guardada.Usuario) ? "SUPERVISOR" : guardada.Usuario; ChkPremiumAuto.IsChecked = guardada.CargaAutomatica; if (guardada.ClaveCifrada != null && guardada.ClaveCifrada.Length > 0) PwdPremiumClave.Password = ComercialPremium.DescifrarClave(guardada); }
                LblPremiumEstado.Text = lista.Count == 0 ? "No se encontraron empresas de Comercial Premium en este servidor SQL." : lista.Count + " empresa(s) de Premium encontrada(s)" + (CmbPremium.SelectedItem != null ? " · se propuso la de este RFC." : ".");
            }
            catch (Exception ex) { LblPremiumEstado.Text = "No se pudo leer la lista de Premium: " + ex.Message; }
        }

        // ---- Contabilidad (ADD) ----

        private ContabilidadConfig _contaActual;

        private void CargarContabilidad()
        {
            _contaActual = _existente == null ? null : BrosSatDb.ObtenerContabilidad(_conn, _existente.RFC);
            if (_contaActual != null)
            {
                TxtContaUsuario.Text = _contaActual.Usuario;
                ChkContaAuto.IsChecked = _contaActual.CargaAutomatica;
                if (_contaActual.ClaveCifrada != null && _contaActual.ClaveCifrada.Length > 0) PwdContaClave.Password = ContabilidadAdd.DescifrarClave(_contaActual);
            }
            LlenarEmpresasContabilidad();
            // Al escribir el RFC de una empresa nueva se vuelve a proponer la de Contabilidad con ese RFC.
            TxtRfc.LostFocus += (s, e) => { if (CmbContabilidad.SelectedItem == null) ProponerContabilidadPorRfc(); };
        }

        private void LlenarEmpresasContabilidad()
        {
            try
            {
                var lista = ContabilidadAdd.ListarEmpresas(_conn);
                CmbContabilidad.ItemsSource = lista;
                if (_contaActual != null)
                    CmbContabilidad.SelectedItem = lista.FirstOrDefault(x => string.Equals(x.BaseDatos, _contaActual.BaseDatos, StringComparison.OrdinalIgnoreCase));
                if (CmbContabilidad.SelectedItem == null) ProponerContabilidadPorRfc();
                if (lista.Count == 0) EstadoConta("No se encontraron empresas de Contabilidad en este servidor SQL.", false);
            }
            catch (Exception ex) { EstadoConta("No se pudo leer la lista de empresas de Contabilidad: " + ex.Message, false); }
        }

        private void ProponerContabilidadPorRfc()
        {
            string rfc = (_existente?.RFC ?? TxtRfc.Text ?? "").Trim();
            if (rfc.Length == 0 || !(CmbContabilidad.ItemsSource is System.Collections.Generic.List<EmpresaContabilidadInfo> lista)) return;
            var m = lista.FirstOrDefault(x => string.Equals(x.Rfc, rfc, StringComparison.OrdinalIgnoreCase));
            if (m != null) { CmbContabilidad.SelectedItem = m; EstadoConta("Se propuso la empresa de Contabilidad con el mismo RFC (" + rfc + ").", true); }
        }

        private void EstadoConta(string texto, bool ok)
        {
            LblContaEstado.Text = texto;
            LblContaEstado.Foreground = ok ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61))
                                           : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        }

        private void ActualizarListaContabilidad_Click(object sender, RoutedEventArgs e) => LlenarEmpresasContabilidad();

        // Devuelve null si no se eligio empresa de Contabilidad (= sin integracion).
        private ContabilidadConfig LeerContabilidad(string rfc)
        {
            var emp = CmbContabilidad.SelectedItem as EmpresaContabilidadInfo;
            if (emp == null) return null;
            return new ContabilidadConfig
            {
                RFC = rfc, BaseDatos = emp.BaseDatos, Usuario = string.IsNullOrWhiteSpace(TxtContaUsuario.Text) ? "SUPERVISOR" : TxtContaUsuario.Text.Trim(),
                ClaveCifrada = string.IsNullOrEmpty(PwdContaClave.Password) ? null : DpapiHelper.Cifrar(PwdContaClave.Password),
                CargaAutomatica = ChkContaAuto.IsChecked == true
            };
        }

        private async void ProbarContabilidad_Click(object sender, RoutedEventArgs e)
        {
            string rfc = (_existente?.RFC ?? TxtRfc.Text ?? "").Trim().ToUpperInvariant();
            var cfg = LeerContabilidad(rfc);
            if (cfg == null) { EstadoConta("Elige primero la empresa de Contabilidad.", false); return; }
            var emp = (EmpresaContabilidadInfo)CmbContabilidad.SelectedItem;
            if (rfc.Length > 0 && !string.Equals(emp.Rfc, rfc, StringComparison.OrdinalIgnoreCase))
            { EstadoConta("El RFC de esa empresa de Contabilidad (" + emp.Rfc + ") no es el de esta empresa (" + rfc + ").", false); return; }
            EstadoConta("Probando acceso...", true); Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                string error = await ContabilidadAdd.ProbarAccesoAsync(_conn, cfg);
                if (error == null) EstadoConta("Acceso correcto. Listo para cargar a «" + emp.Nombre + "».", true); else EstadoConta(error, false);
            }
            catch (Exception ex) { EstadoConta(ex.Message, false); }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }

        // Guarda (o quita) la integracion con Contabilidad de este RFC. El RFC ya se valido antes de guardar la empresa.
        private void GuardarContabilidad(string rfc)
        {
            // Premium: primero la empresa elegida y luego el perfil (si se desmarca, GuardarPerfil borra su configuracion y sus lecturas).
            if (ChkPerfilPremium.IsChecked == true) { var pc = LeerPremium(rfc); if (pc != null) BrosSatDb.GuardarPremium(_conn, pc); }
            BrosSatDb.GuardarPerfil(_conn, rfc, PerfilElegido());
            var cfg = ChkPerfilConta.IsChecked == true ? LeerContabilidad(rfc) : null;
            if (cfg == null) BrosSatDb.QuitarContabilidad(_conn, rfc); else BrosSatDb.GuardarContabilidad(_conn, cfg);
        }

        // Pedido explicito del usuario 2026-08-19: "la conexion no me gusta asi, prefiero algo
        // mas simple... donde les pida instancia, usuario, contraseña de SQL" -- reemplaza el
        // TextBox de cadena de conexion cruda por campos guiados. Se arma/lee con
        // SqlConnectionStringBuilder (nunca concatenacion de texto a mano) y se cifra con DPAPI
        // antes de guardar, igual que la contraseña de la FIEL -- antes quedaba en texto plano
        // en la BD propia.
        private void CargarConexionComercialExistente(string comercialConexionSqlCifrada)
        {
            if (string.IsNullOrWhiteSpace(comercialConexionSqlCifrada)) return;

            string conexionPlana;
            try
            {
                conexionPlana = DpapiHelper.Descifrar(Convert.FromBase64String(comercialConexionSqlCifrada));
            }
            catch
            {
                // Compatibilidad con el valor de prueba guardado en texto plano antes de este
                // cambio (2026-08-19) -- si no descifra como DPAPI/Base64, se asume que ya es la
                // cadena de conexion sin cifrar.
                conexionPlana = comercialConexionSqlCifrada;
            }

            try
            {
                var csb = new SqlConnectionStringBuilder(conexionPlana);
                TxtComercialServidor.Text = csb.DataSource;
                TxtComercialBaseDatos.Text = csb.InitialCatalog;
                ChkComercialWindowsAuth.IsChecked = csb.IntegratedSecurity;
                TxtComercialUsuario.Text = csb.UserID;
                PwdComercialContrasena.Password = csb.Password;
            }
            catch
            {
                // Cadena guardada no parseable -- se deja en blanco en vez de tronar la ventana.
            }
        }

        private void ChkComercialWindowsAuth_Changed(object sender, RoutedEventArgs e) => ActualizarPanelComercialAuth();

        private void ActualizarPanelComercialAuth()
        {
            bool windowsAuth = ChkComercialWindowsAuth.IsChecked == true;
            PanelComercialSqlAuth.IsEnabled = !windowsAuth;
            PanelComercialSqlAuth.Opacity = windowsAuth ? 0.4 : 1.0;
        }

        // NULL = esta empresa no usa Comercial Pro (campo Servidor vacio). Regresa la cadena YA
        // cifrada (DPAPI + Base64) lista para guardar en Empresa.ComercialConexionSql.
        private string ArmarConexionComercialCifrada()
        {
            if (string.IsNullOrWhiteSpace(TxtComercialServidor.Text)) return null;

            var csb = new SqlConnectionStringBuilder
            {
                DataSource = TxtComercialServidor.Text.Trim(),
                InitialCatalog = TxtComercialBaseDatos.Text.Trim(),
                TrustServerCertificate = true
            };
            if (ChkComercialWindowsAuth.IsChecked == true)
            {
                csb.IntegratedSecurity = true;
            }
            else
            {
                csb.UserID = TxtComercialUsuario.Text.Trim();
                csb.Password = PwdComercialContrasena.Password;
            }

            return Convert.ToBase64String(DpapiHelper.Cifrar(csb.ConnectionString));
        }

        private void SeleccionarEstructura(string tag)
        {
            foreach (ComboBoxItem item in CmbEstructura.Items)
            {
                if ((string)item.Tag == tag) { CmbEstructura.SelectedItem = item; return; }
            }
        }

        private void ExaminarCer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Certificado (*.cer)|*.cer|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true) TxtCer.Text = dlg.FileName;
        }

        private void ExaminarKey_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Llave privada (*.key)|*.key|Todos los archivos|*.*" };
            if (dlg.ShowDialog() == true) TxtKey.Text = dlg.FileName;
        }

        private void ExaminarCarpeta_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Carpeta donde se guardarán los XML de esta empresa" };
            if (!string.IsNullOrWhiteSpace(TxtCarpetaXml.Text)) dlg.InitialDirectory = TxtCarpetaXml.Text;
            if (dlg.ShowDialog() == true) TxtCarpetaXml.Text = dlg.FolderName;
        }

        private void ExaminarComercialRecibidos_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Carpeta \"Ruta XML Recibidos\" de Comercial Pro (Opciones > CFDI)" };
            if (!string.IsNullOrWhiteSpace(TxtComercialRecibidos.Text)) dlg.InitialDirectory = TxtComercialRecibidos.Text;
            if (dlg.ShowDialog() == true) TxtComercialRecibidos.Text = dlg.FolderName;
        }

        private void ExaminarComercialEmitidos_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Carpeta \"Ruta XML Emitidos\" de Comercial Pro (Opciones > CFDI)" };
            if (!string.IsNullOrWhiteSpace(TxtComercialEmitidos.Text)) dlg.InitialDirectory = TxtComercialEmitidos.Text;
            if (dlg.ShowDialog() == true) TxtComercialEmitidos.Text = dlg.FolderName;
        }

        private void BtnRestaurarPlantilla_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TxtPlantilla.Text = "{UUID}";
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void Guardar_Click(object sender, RoutedEventArgs e)
        {
            bool esEdicion = _existente != null;

            if (string.IsNullOrWhiteSpace(TxtNombre.Text) || string.IsNullOrWhiteSpace(TxtRfc.Text) ||
                string.IsNullOrWhiteSpace(TxtCer.Text) || string.IsNullOrWhiteSpace(TxtKey.Text) ||
                (!esEdicion && string.IsNullOrEmpty(PwdFiel.Password)))
            {
                MessageBox.Show(this, "Completa todos los campos.", "Falta información", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TxtPlantilla.Text))
            {
                MessageBox.Show(this, "El nombre de archivo no puede quedar vacío -- usa \"Restaurar por defecto\" si no sabes qué poner.", "Falta información", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Solo se vuelve a validar la FIEL contra el SAT si de verdad cambio algo relevante
            // (password nueva, o .cer/.key nuevos) -- reeditar solo la carpeta de guardado no
            // deberia obligar a teclear la contraseña de nuevo.
            bool hayPasswordNueva = !string.IsNullOrEmpty(PwdFiel.Password);
            if (!esEdicion || hayPasswordNueva)
            {
                string passwordParaProbar = hayPasswordNueva ? PwdFiel.Password : null;
                if (passwordParaProbar != null)
                {
                    try
                    {
                        // Prueba real de la FIEL -- si la contraseña esta mal, se sabe AHORA, no en
                        // la primera corrida automatica sin nadie viendo.
                        SatFirmaXml.CargarFiel(TxtCer.Text, TxtKey.Text, passwordParaProbar, out var llave);
                        llave.Dispose();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "No se pudo cargar la FIEL con esos datos:\n\n" + ex.Message,
                            "FIEL inválida", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            string estructura = (string)((ComboBoxItem)CmbEstructura.SelectedItem).Tag;
            string carpetaXml = string.IsNullOrWhiteSpace(TxtCarpetaXml.Text) ? null : TxtCarpetaXml.Text.Trim();
            string comercialRecibidos = string.IsNullOrWhiteSpace(TxtComercialRecibidos.Text) ? null : TxtComercialRecibidos.Text.Trim();
            string comercialEmitidos = string.IsNullOrWhiteSpace(TxtComercialEmitidos.Text) ? null : TxtComercialEmitidos.Text.Trim();
            string comercialConexion = ArmarConexionComercialCifrada();

            // Un sistema desmarcado se quita: se confirma antes si ya tenia algo configurado. Lo ya importado/cargado en el sistema no se toca.
            var perfil = PerfilElegido();
            bool teniaComercial = !string.IsNullOrEmpty(comercialRecibidos) || !string.IsNullOrEmpty(comercialEmitidos) || !string.IsNullOrEmpty(comercialConexion);
            bool teniaConta = CmbContabilidad.SelectedItem != null;
            bool teniaPremium = CmbPremium.SelectedItem != null;
            string quitar = (!perfil.ComercialPremium && teniaPremium ? "• Comercial Premium (la empresa configurada)\n" : "") + (!perfil.ComercialPro && teniaComercial ? "• Comercial Pro (la conexión y las carpetas que tenías configuradas)\n" : "") + (!perfil.Contabilidad && teniaConta ? "• Contabilidad (la empresa y el usuario configurados)\n" : "");
            if (quitar.Length > 0 && MessageBox.Show(this, "Vas a dejar de usar con esta empresa:\n\n" + quitar + "\nSe quita su configuración de BrosLMV Descargas. Lo que ya se cargó o importó en esos sistemas se queda ahí.\n\n¿Continuar?", "Quitar sistema", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            if (!perfil.ComercialPro) { comercialRecibidos = null; comercialEmitidos = null; comercialConexion = null; }

            // Contabilidad: si la empresa elegida no es de este RFC, se avisa ANTES de guardar nada.
            string rfcGuardar = esEdicion ? _existente.RFC : TxtRfc.Text.Trim().ToUpperInvariant();
            if (ChkPerfilConta.IsChecked == true && CmbContabilidad.SelectedItem is EmpresaContabilidadInfo ec && !string.Equals(ec.Rfc, rfcGuardar, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "La empresa de Contabilidad elegida tiene el RFC " + ec.Rfc + ", distinto al de esta empresa (" + rfcGuardar + ").\n\nElige la correcta o deja la lista sin elegir.",
                    "Contabilidad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                bool ocultarNominas = ChkOcultarNominas.IsChecked == true;
                if (esEdicion)
                {
                    byte[] passwordCifrada = hayPasswordNueva ? DpapiHelper.Cifrar(PwdFiel.Password) : null;
                    BrosSatDb.ActualizarEmpresa(_conn, _existente.EmpresaID, TxtNombre.Text.Trim(), TxtCer.Text.Trim(), TxtKey.Text.Trim(),
                        passwordCifrada, carpetaXml, estructura, TxtPlantilla.Text.Trim(), comercialRecibidos, comercialEmitidos, comercialConexion,
                        ocultarNominas);
                    GuardarContabilidad(_existente.RFC);
                    SeGuardo = true;
                    DialogResult = true;
                    Close();
                    return;
                }

                int anioInicio = (int)CmbAnioInicio.SelectedItem;
                string rfc = TxtRfc.Text.Trim().ToUpperInvariant();
                byte[] passwordCifradaNueva = DpapiHelper.Cifrar(PwdFiel.Password);
                int empresaId = BrosSatDb.GuardarEmpresaNueva(_conn, TxtNombre.Text.Trim(), rfc,
                    TxtCer.Text.Trim(), TxtKey.Text.Trim(), passwordCifradaNueva, carpetaXml, estructura, TxtPlantilla.Text.Trim(),
                    comercialRecibidos, comercialEmitidos, comercialConexion, anioInicio, ocultarNominas);
                GuardarContabilidad(rfc);
                SeGuardo = true;

                // Pedido explicito del usuario 2026-08-19: "al instalar lo primero que haga sea
                // realizar una descarga" -- se dispara el primer tramo (Recibidos + Emitidos)
                // YA, en vez de esperar hasta la proxima corrida de la Tarea Programada
                // (--auto-solicitar-todas corre 1 vez al dia). El resto del historico se sigue
                // pidiendo solo, un mes a la vez, en las corridas automaticas de ahi en adelante.
                BtnGuardar.IsEnabled = false;
                BtnGuardar.Content = "Descargando...";
                try
                {
                    var nuevaEmpresa = new EmpresaFila
                    {
                        EmpresaID = empresaId,
                        Nombre = TxtNombre.Text.Trim(),
                        RFC = rfc,
                        RutaCer = TxtCer.Text.Trim(),
                        RutaKey = TxtKey.Text.Trim(),
                        PasswordCifrada = passwordCifradaNueva,
                        AnioInicioDescargas = anioInicio
                    };
                    int errores = await AutoSolicitador.SolicitarSiguienteTramoAsync(_conn, nuevaEmpresa);
                    string mensaje = "Empresa guardada. Se disparó la descarga histórica desde " + anioInicio + " (Recibidos y Emitidos) -- continuará sola, un mes a la vez, en las corridas automáticas.";
                    if (errores > 0) mensaje += "\n\nHubo " + errores + " error(es) al solicitar -- revisa la Bitácora. Se reintenta solo en la próxima corrida.";
                    MessageBox.Show(this, mensaje, "BrosLMV", MessageBoxButton.OK, errores > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                catch (Exception exSolicitud)
                {
                    // La empresa YA se guardo -- un fallo al solicitar no debe verse como que
                    // "no se guardo nada". Se reintenta sola en la proxima corrida automatica.
                    MessageBox.Show(this, "La empresa se guardó, pero no se pudo iniciar la descarga automática ahora:\n\n" + exSolicitud.Message +
                        "\n\nSe reintentará sola en la próxima corrida automática.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar la empresa:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnGuardar.IsEnabled = true;
                BtnGuardar.Content = "Guardar empresa";
            }
        }
    }
}
