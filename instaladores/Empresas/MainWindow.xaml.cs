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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BrosLMV.Empresas
{
    // Fila de empresa para el DataGrid (con notificación de cambios).
    public class EmpresaRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void PC(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        bool _sel;
        public bool Sel { get { return _sel; } set { _sel = value; PC("Sel"); } }
        public string DB { get; set; }
        public bool Instalado { get; set; }
        public bool NecesitaActualizar { get; set; }
        public string VersionNueva { get; set; }

        string _versionInstalada;
        public string VersionInstalada
        {
            get { return _versionInstalada; }
            set { _versionInstalada = value; PC("VersionInstalada"); PC("VersionTexto"); }
        }

        // «3.1.2  →  3.2.1» cuando hay que actualizar; solo la versión cuando está al día; guion si no está instalada.
        public string VersionTexto
        {
            get
            {
                if (!Instalado) return "—";
                if (NecesitaActualizar && !string.IsNullOrEmpty(VersionNueva)) return _versionInstalada + "   →   " + VersionNueva;
                return _versionInstalada;
            }
        }

        string _estado;
        public string Estado
        {
            get { return _estado; }
            set { _estado = value; PC("Estado"); PC("EstadoColor"); PC("EstadoIcon"); PC("EstadoFondo"); PC("VersionTexto"); }
        }

        string Clase()
        {
            string e = (_estado ?? "").ToLower();
            if (e.Contains("error")) return "error";
            if (e.Contains("ahora") || e.Contains("actualizada")) return "listo";
            if (e.Contains("trabajando") || e.Contains("espera")) return "trabajo";
            if (e.Contains("actualizar")) return "actualizar";
            if (e.Contains("al día") || e.Contains("instalad")) return "listo";
            return "pendiente";
        }
        public string EstadoColor
        {
            get
            {
                switch (Clase())
                {
                    case "error": return "#B91C1C";
                    case "actualizar": return "#1D4ED8";
                    case "listo": return "#15803D";
                    case "trabajo": return "#475569";
                    default: return "#B45309";
                }
            }
        }
        public string EstadoFondo
        {
            get
            {
                switch (Clase())
                {
                    case "error": return "#FEE2E2";
                    case "actualizar": return "#DBEAFE";
                    case "listo": return "#DCFCE7";
                    case "trabajo": return "#E2E8F0";
                    default: return "#FEF3C7";
                }
            }
        }
        public string EstadoIcon
        {
            get
            {
                switch (Clase())
                {
                    case "error": return "";       // X
                    case "actualizar": return "";  // flecha arriba
                    case "listo": return "";       // palomita
                    case "trabajo": return "";     // sincronizar
                    default: return "";            // reloj
                }
            }
        }
    }

    public partial class MainWindow : Window
    {
        readonly ObservableCollection<EmpresaRow> _rows = new ObservableCollection<EmpresaRow>();
        readonly string _provisionSql;
        readonly Version _versionActual;
        readonly string _versionActualTexto;

        // Además de si existe el botón del ribbon, trae zzBrosInfo.ProvisionVersion (si la
        // tabla/fila existe -- empresas provisionadas ANTES de que existiera esta columna
        // simplemente regresan NULL, y se tratan como "versión desconocida" = ofrecer actualizar).
        // OJO (bug real, encontrado en pruebas v2.30.0): NO se puede referenciar zzBrosInfo
        // directamente dentro de una rama de CASE/subquery en el mismo lote dinámico, aunque
        // esa rama solo se alcance cuando la tabla existe -- SQL Server intenta enlazar el
        // nombre del objeto al ANALIZAR el lote (sp_executesql), no al ejecutarlo, así que
        // tronaba "Invalid object name ... zzBrosInfo" en TODAS las empresas (ninguna tenía
        // esa tabla todavía, por ser una columna nueva) y la consulta completa regresaba 0
        // filas sin ningún error visible en la UI (WPF no repinta "Conectando..." hasta que
        // termina la llamada síncrona, así que se sentía como "no hace nada"). Arreglo: cada
        // verificación va en su propio sp_executesql con OUTPUT, y solo se construye/ejecuta
        // la consulta a zzBrosInfo DESPUÉS de confirmar (en un paso previo, seguro) que existe.
        const string DetectSql = @"
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#r') IS NOT NULL DROP TABLE #r;
CREATE TABLE #r(db sysname, instalado bit, version nvarchar(50) NULL);
DECLARE @n sysname, @s nvarchar(max);
DECLARE @tieneReq bit, @inst bit, @tieneInfo bit, @ver nvarchar(200);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.databases WHERE database_id>4 AND state=0;
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS=0 BEGIN
  BEGIN TRY
    SET @tieneReq=0; SET @inst=0; SET @tieneInfo=0; SET @ver=NULL;

    SET @s = N'SELECT @out = CASE WHEN EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engRibbonControl'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engRibbonGroup'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engModule'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''docDocument'') THEN 1 ELSE 0 END';
    EXEC sp_executesql @s, N'@out bit OUTPUT', @out=@tieneReq OUTPUT;

    IF @tieneReq = 1
    BEGIN
      SET @s = N'SELECT @out = CASE WHEN EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.dbo.engRibbonControl WHERE ControlExecute=''BrosLMV.CONSOLA'') THEN 1 ELSE 0 END';
      EXEC sp_executesql @s, N'@out bit OUTPUT', @out=@inst OUTPUT;

      SET @s = N'SELECT @out = CASE WHEN EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''zzBrosInfo'') THEN 1 ELSE 0 END';
      EXEC sp_executesql @s, N'@out bit OUTPUT', @out=@tieneInfo OUTPUT;

      IF @tieneInfo = 1
      BEGIN
        SET @s = N'SELECT @out = (SELECT TOP 1 Valor FROM '+QUOTENAME(@n)+'.dbo.zzBrosInfo WHERE Clave=''ProvisionVersion'')';
        EXEC sp_executesql @s, N'@out nvarchar(200) OUTPUT', @out=@ver OUTPUT;
      END

      INSERT #r(db,instalado,version) VALUES (@n, @inst, @ver);
    END
  END TRY
  BEGIN CATCH END CATCH
  FETCH NEXT FROM c INTO @n;
END
CLOSE c; DEALLOCATE c;
SELECT db, instalado, version FROM #r ORDER BY db;";

        public MainWindow()
        {
            InitializeComponent();
            _provisionSql = LoadResourceText("provision_empresa.sql");
            grid.ItemsSource = _rows;
            _versionActual = Assembly.GetExecutingAssembly().GetName().Version;
            _versionActualTexto = _versionActual.Major + "." + _versionActual.Minor + "." + _versionActual.Build;
            lblVer.Text = "Versión " + _versionActualTexto;
            Loaded += async (s, e) => await IniciarAsync();
        }

        // Al abrir: detecta las instancias de este equipo y, si la instalación anterior dejó la conexión guardada,
        // la usa y entra sola (una actualización no debe pedir servidor, usuario ni contraseña otra vez).
        async Task IniciarAsync()
        {
            LlenarInstancias(null);
            var g = Conexion.LeerGuardada();
            if (g != null)
            {
                cbServer.Text = g.Servidor;
                if (!string.IsNullOrWhiteSpace(g.Usuario)) txtUser.Text = g.Usuario;
                pwdBox.Password = g.Contrasena ?? "";
                lblRecordada.Text = "Conexión recordada de la instalación anterior";
                await ConectarAsync(true);
            }
            else
            {
                // Sin conexión guardada: se propone la instancia detectada (nunca un nombre fijo).
                var lista = cbServer.Items.Cast<string>().ToList();
                cbServer.Text = lista.FirstOrDefault(x => x.IndexOf("COMPAC", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? (lista.Count == 1 ? lista[0] : lista.FirstOrDefault()) ?? "localhost";
                if (lista.Count > 1) lblRecordada.Text = lista.Count + " instancias detectadas en este equipo";
                else if (lista.Count == 1) lblRecordada.Text = "Instancia detectada en este equipo";
                else lblRecordada.Text = "No se detectó SQL Server en este equipo: escribe el servidor";
            }
            PintarPasos();
        }

        void LlenarInstancias(IEnumerable<string> extra)
        {
            string actual = cbServer.Text;
            var todas = new List<string>(Conexion.InstanciasLocales());
            if (extra != null) foreach (var x in extra) if (!todas.Contains(x, StringComparer.OrdinalIgnoreCase)) todas.Add(x);
            cbServer.ItemsSource = null;
            cbServer.ItemsSource = todas;
            cbServer.Text = actual;
        }

        // Compara "X.Y.Z" contra la versión actual del instalador. NULL/vacío/ilegible = "más
        // vieja" (se pide actualizar) -- son empresas provisionadas antes de que existiera el
        // registro de versión, así que no hay forma de saber qué tienen y lo más seguro es
        // ofrecer re-provisionar (el script es idempotente, no borra nada).
        bool VersionDesactualizada(string versionInstalada)
        {
            Version v;
            if (string.IsNullOrWhiteSpace(versionInstalada) || !Version.TryParse(versionInstalada, out v))
                return true;
            return v < _versionActual;
        }

        public void SetEstadoRuntime(string msg, bool ok)
        {
            _mensajeRuntime = (ok ? "✓ " : "⚠ ") + msg;
            lblFooter.Text = _mensajeRuntime;
        }
        string _mensajeRuntime;

        static string LoadResourceText(string endsWith)
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new Exception("No se encontró el recurso embebido: " + endsWith);
            using (var s = asm.GetManifestResourceStream(name))
            using (var r = new StreamReader(s))
                return r.ReadToEnd();
        }

        string Pass()
        {
            return pwdPlain.Visibility == Visibility.Visible ? pwdPlain.Text : pwdBox.Password;
        }

        static string ConnStr(string server, string db, string user, string pass)
        {
            return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Connect Timeout=15;";
        }

        List<EmpresaRow> GetEmpresas(string server, string user, string pass)
        {
            var list = new List<EmpresaRow>();
            using (var cn = new SqlConnection(ConnStr(server, "master", user, pass)))
            {
                cn.Open();
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = DetectSql; cmd.CommandTimeout = 60;
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read())
                        {
                            bool inst = Convert.ToInt32(rd["instalado"]) == 1;
                            string ver = rd["version"] == DBNull.Value ? null : rd["version"].ToString();
                            bool desact = inst && VersionDesactualizada(ver);
                            string estado = !inst ? "Pendiente" : (desact ? "Actualizar disponible" : "Al día");
                            list.Add(new EmpresaRow
                            {
                                DB = rd["db"].ToString(),
                                Instalado = inst,
                                NecesitaActualizar = desact,
                                VersionInstalada = inst ? (ver ?? "desconocida") : "—",
                                Estado = estado,
                                Sel = false
                            });
                        }
                }
            }
            return list;
        }

        void Provision(string server, string db, string user, string pass)
        {
            using (var cn = new SqlConnection(ConnStr(server, db, user, pass)))
            {
                cn.Open();
                using (var cmd = cn.CreateCommand())
                {
                    // @provisionVersion la usa provision_empresa.sql §5 para dejar registro de
                    // qué versión de este script quedó aplicada en la empresa (ver DetectSql).
                    cmd.CommandText =
                        "DECLARE @provisionVersion NVARCHAR(20) = '" + _versionActualTexto + "';\r\n" + _provisionSql;
                    cmd.CommandTimeout = 120;
                    cmd.ExecuteNonQuery();
                }

                // Siembra los scripts CORE en zzBrosScript de esta empresa (Cotizador,
                // ConfiguracionFormato). Asi el boton funciona en TODAS las terminales de la
                // empresa aunque a alguna le falle la copia del archivo local. Best-effort.
                try { SeedCoreScripts(cn); } catch { }
            }
        }

        static void SeedCoreScripts(SqlConnection cn)
        {
            foreach (var s in new[] { "Cotizador.ctx", "ConfiguracionFormato.ctx" })
            {
                string ruta = Path.Combine(@"C:\BrosLMV\scripts", s);
                if (!File.Exists(ruta)) continue;
                string codigo = File.ReadAllText(ruta);
                string appKey = Path.GetFileNameWithoutExtension(s);
                string nombre = appKey == "Cotizador" ? "Generar documento (PDF)" : "Configuración de formato";
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var b = sha.ComputeHash(Encoding.UTF8.GetBytes(codigo));
                    var sb2 = new StringBuilder(64);
                    foreach (var x in b) sb2.Append(x.ToString("x2"));
                    hash = sb2.ToString();
                }
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
IF OBJECT_ID('dbo.zzBrosScript') IS NULL RETURN;
MERGE dbo.zzBrosScript AS t USING (SELECT @k AS AppKey) s ON t.AppKey = s.AppKey
WHEN MATCHED THEN UPDATE SET Nombre=@n, Codigo=@c, Activo=1, Modificado=GETDATE(), ModificadoPor=0, HashSHA256=@h, AprobadoPor=0, AprobadoEl=GETDATE()
WHEN NOT MATCHED THEN INSERT (AppKey, Nombre, Codigo, Modulo, Activo, Modificado, ModificadoPor, HashSHA256, AprobadoPor, AprobadoEl)
     VALUES (@k, @n, @c, 0, 1, GETDATE(), 0, @h, 0, GETDATE());";
                    cmd.Parameters.AddWithValue("@k", appKey);
                    cmd.Parameters.AddWithValue("@n", nombre);
                    cmd.Parameters.AddWithValue("@c", codigo);
                    cmd.Parameters.AddWithValue("@h", hash);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Ya tiene contraseña activa esta empresa? (para que la UI decida si pide
        // "contraseña actual" o no). Nunca lanza -- empresa sin la tabla (instalador viejo)
        // o sin fila = "no tiene".
        bool ConsolaYaTienePassword(string server, string db, string user, string pass)
        {
            try
            {
                using (var cn = new SqlConnection(ConnStr(server, db, user, pass)))
                {
                    cn.Open();
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Habilitado FROM zzBrosConsolaPass WHERE Id=1";
                        var r = cmd.ExecuteScalar();
                        return r != null && r != DBNull.Value && Convert.ToBoolean(r);
                    }
                }
            }
            catch { return false; }
        }

        // Activa o cambia la contraseña de la Consola para UNA empresa. Si ya tenia una
        // activa, EXIGE que "actual" verifique contra el hash guardado antes de aceptar la
        // nueva -- si no coincide, lanza y esa empresa queda marcada como error (no se
        // cambia nada). Misma contraseña nueva para TODAS las empresas de esta corrida.
        void AplicarPasswordConsola(string server, string db, string user, string pass, string actual, string nueva)
        {
            using (var cn = new SqlConnection(ConnStr(server, db, user, pass)))
            {
                cn.Open();

                byte[] salActual = null, hashActual = null;
                int iteracionesActual = 0;
                bool yaHabilitado = false;
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Habilitado, Sal, Hash, Iteraciones FROM zzBrosConsolaPass WHERE Id=1";
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            yaHabilitado = Convert.ToBoolean(rd["Habilitado"]);
                            salActual = rd["Sal"] as byte[];
                            hashActual = rd["Hash"] as byte[];
                            iteracionesActual = rd["Iteraciones"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Iteraciones"]);
                        }
                    }
                }

                if (yaHabilitado && !ConsolaPasswordHash.Verificar(actual, salActual, hashActual, iteracionesActual))
                    throw new Exception("La contraseña actual de la Consola no es correcta en " + db + ".");

                byte[] sal, hash;
                int iteraciones;
                ConsolaPasswordHash.Generar(nueva, out sal, out hash, out iteraciones);

                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM zzBrosConsolaPass WHERE Id=1)
    UPDATE zzBrosConsolaPass SET Habilitado=1, Sal=@Sal, Hash=@Hash, Iteraciones=@Iter, FechaActualizacion=GETDATE() WHERE Id=1;
ELSE
    INSERT INTO zzBrosConsolaPass (Id, Habilitado, Sal, Hash, Iteraciones, FechaActualizacion)
    VALUES (1, 1, @Sal, @Hash, @Iter, GETDATE());";
                    cmd.Parameters.AddWithValue("@Sal", sal);
                    cmd.Parameters.AddWithValue("@Hash", hash);
                    cmd.Parameters.AddWithValue("@Iter", iteraciones);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ---- Estado visual ----

        bool _conectado;

        void PintarPasos()
        {
            var azul = (System.Windows.Media.Brush)FindResource("Azul");
            var hecho = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#16A34A"));
            var apagado = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#24405E"));
            var textoApagado = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8CA5C3"));
            int sel = _rows.Count(r => r.Sel);

            Marcar(dot1, ico1, "1", _conectado ? 2 : 1, hecho, azul, apagado, textoApagado);
            Marcar(dot2, ico2, "2", !_conectado ? 0 : (sel > 0 || _trabajando ? 2 : 1), hecho, azul, apagado, textoApagado);
            Marcar(dot3, ico3, "3", !_conectado ? 0 : (_trabajando ? 1 : 0), hecho, azul, apagado, textoApagado);
            lblPaso1.Text = _conectado ? "Conectado" : "Elige el servidor SQL";
            lblPaso2.Text = !_conectado ? "Marca las que quieres" : (sel > 0 ? sel + " seleccionada(s)" : "Marca las que quieres");
            lblPaso3.Text = _trabajando ? "Trabajando..." : "Instala o actualiza";
        }

        // estado: 0 = pendiente, 1 = actual, 2 = hecho
        static void Marcar(System.Windows.Controls.Border dot, TextBlock ico, string numero, int estado,
            System.Windows.Media.Brush hecho, System.Windows.Media.Brush azul, System.Windows.Media.Brush apagado, System.Windows.Media.Brush textoApagado)
        {
            if (estado == 2) { dot.Background = hecho; ico.Text = ""; ico.FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"); ico.FontSize = 12; ico.Foreground = System.Windows.Media.Brushes.White; }
            else
            {
                ico.Text = numero; ico.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); ico.FontSize = 13;
                dot.Background = estado == 1 ? azul : apagado;
                ico.Foreground = estado == 1 ? System.Windows.Media.Brushes.White : textoApagado;
            }
        }

        void MostrarConexion(bool conectado)
        {
            _conectado = conectado;
            pnlConexionResumen.Visibility = conectado ? Visibility.Visible : Visibility.Collapsed;
            pnlConexionEditar.Visibility = conectado ? Visibility.Collapsed : Visibility.Visible;
            ledConn.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(conectado ? "#22C55E" : "#64748B"));
            lblConnTitle.Text = conectado ? "Conectado a SQL Server" : "Sin conexión";
            lblConnSrv.Text = conectado ? cbServer.Text.Trim() : "";
            lblConnUsr.Text = conectado ? "Usuario: " + txtUser.Text.Trim() : "";
            lblResumenServidor.Text = cbServer.Text.Trim();
            lblResumenDetalle.Text = "Usuario " + txtUser.Text.Trim() + " · " + _rows.Count + " empresa(s) de Comercial encontrada(s)";
            lblVacio.Visibility = conectado && _rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            lblVacio.Text = conectado ? "No se encontraron empresas de CONTPAQi Comercial en este servidor." : "Conecta al servidor para ver tus empresas.";
            PintarPasos();
        }

        void UpdateCounts()
        {
            int tot = _rows.Count;
            int act = _rows.Count(r => r.NecesitaActualizar);
            int pend = _rows.Count(r => !r.Instalado);
            int alDia = tot - act - pend;
            int sel = _rows.Count(r => r.Sel);
            cardTotal.Text = tot.ToString();
            cardInst.Text = alDia.ToString();
            cardAct.Text = act.ToString();
            cardPend.Text = pend.ToString();

            btnActualizarTodas.Visibility = act > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnActualizarTodas.Content = "Actualizar todas (" + act + ")";
            btnInstalarPendientes.Visibility = pend > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnInstalarPendientes.Content = "Instalar pendientes (" + pend + ")";
            btnInstall.Content = "Aplicar a las seleccionadas (" + sel + ")";
            btnInstall.IsEnabled = sel > 0 && !_trabajando;
            btnActualizarTodas.IsEnabled = btnInstalarPendientes.IsEnabled = !_trabajando;

            if (!_trabajando)
                lblFooter.Text = tot == 0 ? "Conecta a tu SQL Server para listar las empresas."
                    : (act > 0 ? act + " empresa(s) tienen una versión anterior: pulsa «Actualizar todas» o marca las que quieras."
                    : pend > 0 ? pend + " empresa(s) todavía no tienen BrosLMV."
                    : "Todas tus empresas están al día con la versión " + _versionActualTexto + ".");

            if (tot > 0)
            {
                bool actualizando = _rows.Any(r => r.Instalado);
                lblTitulo.Text = actualizando && act > 0 ? "Actualizar BrosLMV a la versión " + _versionActualTexto
                               : actualizando ? "BrosLMV " + _versionActualTexto + " en tus empresas"
                               : "Instalar BrosLMV en tus empresas";
                lblSubtitulo.Text = act > 0 ? "Se encontraron empresas con una versión anterior. Actualízalas todas de una vez o elige cuáles."
                                  : pend > 0 ? "Elige las empresas de CONTPAQi Comercial donde quieres usar BrosLMV."
                                  : "Nada pendiente. Puedes volver a aplicar a una empresa si lo necesitas.";
            }
            chkTodas.IsChecked = _verFilas().Any() && _verFilas().All(r => r.Sel);
            PintarPasos();
        }

        IEnumerable<EmpresaRow> _verFilas()
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            return view.Cast<EmpresaRow>();
        }

        void ApplyFilter()
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            string txt = (txtSearch.Text ?? "").ToLower();
            int modo = rbAct.IsChecked == true ? 1 : rbPend.IsChecked == true ? 2 : rbOk.IsChecked == true ? 3 : 0;
            view.Filter = o =>
            {
                var it = (EmpresaRow)o;
                bool okTxt = string.IsNullOrWhiteSpace(txt) || it.DB.ToLower().Contains(txt);
                bool okEst = modo == 1 ? it.NecesitaActualizar
                    : modo == 2 ? !it.Instalado
                    : modo == 3 ? (it.Instalado && !it.NecesitaActualizar)
                    : true;
                return okTxt && okEst;
            };
            view.Refresh();
        }

        // ---- Conexión ----

        async Task ConectarAsync(bool silencioso)
        {
            string server = cbServer.Text.Trim(), user = txtUser.Text.Trim(), pass = Pass();
            if (string.IsNullOrEmpty(server)) { MostrarError("Escribe o elige el servidor \\ instancia."); return; }
            lblConnError.Visibility = Visibility.Collapsed;
            btnConn.IsEnabled = false; btnConn.Content = "Conectando...";
            lblFooter.Text = "Conectando a " + server + "...";
            List<EmpresaRow> empresas;
            try { empresas = await Task.Run(() => GetEmpresas(server, user, pass)); }
            catch (Exception ex)
            {
                btnConn.IsEnabled = true; btnConn.Content = "Conectar";
                MostrarConexion(false);
                string motivo = ex.Message;
                MostrarError((silencioso ? "No se pudo usar la conexión guardada (" + server + "): " : "No se pudo conectar: ") + motivo);
                lblFooter.Text = "Sin conexión.";
                return;
            }
            btnConn.IsEnabled = true; btnConn.Content = "Conectar";

            _rows.Clear();
            foreach (var e in empresas)
            {
                e.VersionNueva = _versionActualTexto;
                e.PropertyChanged += (s, ev) =>
                {
                    if (ev.PropertyName != "Sel") return;
                    UpdateCounts();
                    if (chkProtegerConsola.IsChecked == true) chkProtegerConsola_Changed(null, null);
                };
                _rows.Add(e);
            }
            MostrarConexion(true);

            // Recuerda la conexión (cifrada con DPAPI, igual que el addon) para la próxima actualización.
            Conexion.Guardar(server, user, pass);

            ApplyFilter();
            UpdateCounts();
            // Si hay empresas por actualizar, que el filtro útil quede a la vista.
            if (_rows.Any(r => r.NecesitaActualizar)) rbAct.IsChecked = true;
        }

        void MostrarError(string texto)
        {
            lblConnError.Text = "⚠  " + texto;
            lblConnError.Visibility = Visibility.Visible;
        }

        void btnConn_Click(object sender, RoutedEventArgs e) { var _ = ConectarAsync(false); }
        void pwd_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) { var _ = ConectarAsync(false); } }

        void btnCambiar_Click(object sender, RoutedEventArgs e)
        {
            lblRecordada.Text = "";
            MostrarConexion(false);
        }

        void btnDetectar_Click(object sender, RoutedEventArgs e)
        {
            LlenarInstancias(null);
            int n = cbServer.Items.Count;
            lblRecordada.Text = n == 0 ? "No se detectó SQL Server en este equipo" : n + " instancia(s) detectada(s) en este equipo";
            if (n > 0) cbServer.IsDropDownOpen = true;
        }

        async void btnBuscarRed_Click(object sender, RoutedEventArgs e)
        {
            btnBuscarRed.IsEnabled = false; lblRecordada.Text = "Buscando servidores SQL en la red...";
            List<string> red = null;
            try { red = await Task.Run(() => Conexion.InstanciasEnRed()); } catch { }
            btnBuscarRed.IsEnabled = true;
            LlenarInstancias(red);
            lblRecordada.Text = (red == null || red.Count == 0) ? "No se encontraron más servidores en la red" : cbServer.Items.Count + " servidor(es) en la lista";
            if (cbServer.Items.Count > 0) cbServer.IsDropDownOpen = true;
        }

        void btnEye_Click(object sender, RoutedEventArgs e)
        {
            if (pwdPlain.Visibility == Visibility.Visible)
            {
                pwdBox.Password = pwdPlain.Text;
                pwdPlain.Visibility = Visibility.Collapsed; pwdBox.Visibility = Visibility.Visible;
            }
            else
            {
                pwdPlain.Text = pwdBox.Password;
                pwdBox.Visibility = Visibility.Collapsed; pwdPlain.Visibility = Visibility.Visible;
            }
        }

        // ---- Lista ----

        void txtSearch_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) { ApplyFilter(); UpdateCounts(); } }
        void filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) { ApplyFilter(); UpdateCounts(); } }

        void chkTodas_Click(object sender, RoutedEventArgs e)
        {
            bool marcar = chkTodas.IsChecked == true;
            foreach (var it in _verFilas().ToList()) it.Sel = marcar;
            UpdateCounts();
        }

        // ---- Aplicar (instalar / actualizar) ----

        bool _trabajando;

        void btnActualizarTodas_Click(object sender, RoutedEventArgs e)
        {
            var lista = _rows.Where(r => r.NecesitaActualizar).ToList();
            if (lista.Count == 0) return;
            if (MessageBox.Show(this, "Se actualizarán " + lista.Count + " empresa(s) a la versión " + _versionActualTexto + ":\n\n  • " +
                    string.Join("\n  • ", lista.Take(12).Select(r => r.DB)) + (lista.Count > 12 ? "\n  • ... y " + (lista.Count - 12) + " más" : "") +
                    "\n\nEs seguro: el proceso no borra datos y se puede repetir.", "Actualizar todas", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var _ = AplicarAsync(lista);
        }

        void btnInstalarPendientes_Click(object sender, RoutedEventArgs e)
        {
            var lista = _rows.Where(r => !r.Instalado).ToList();
            if (lista.Count == 0) return;
            if (MessageBox.Show(this, "Se instalará BrosLMV en " + lista.Count + " empresa(s) pendiente(s).", "Instalar pendientes", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var _ = AplicarAsync(lista);
        }

        void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            var sel = _rows.Where(r => r.Sel).ToList();
            if (sel.Count == 0) return;
            var _ = AplicarAsync(sel);
        }

        async Task AplicarAsync(List<EmpresaRow> lista)
        {
            if (_trabajando || lista.Count == 0) return;

            bool proteger = chkProtegerConsola.IsChecked == true;
            string actual = pwdConsolaActual.Password;
            string nueva = pwdConsolaNueva.Password;
            if (proteger)
            {
                if (string.IsNullOrEmpty(nueva))
                {
                    expConsola.IsExpanded = true;
                    MessageBox.Show("Escribe la contraseña de la Consola (o desmarca la casilla si no la quieres activar/cambiar ahora).", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (nueva != pwdConsolaConfirmar.Password)
                {
                    expConsola.IsExpanded = true;
                    MessageBox.Show("La confirmación de la contraseña de la Consola no coincide.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (pwdConsolaActual.Visibility == Visibility.Visible && string.IsNullOrEmpty(actual))
                {
                    expConsola.IsExpanded = true;
                    MessageBox.Show("Esta empresa ya tiene contraseña: escribe la actual para poder cambiarla.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string server = cbServer.Text.Trim(), user = txtUser.Text.Trim(), pass = Pass();
            _trabajando = true; UpdateCounts();
            barProgreso.Visibility = Visibility.Visible; barProgreso.Minimum = 0; barProgreso.Maximum = lista.Count; barProgreso.Value = 0;
            foreach (var r in lista) r.Estado = "En espera";
            int ok = 0; var fallos = new List<string>();

            foreach (var row in lista)
            {
                row.Estado = "Trabajando...";
                lblFooter.Text = (ok + fallos.Count + 1) + " de " + lista.Count + " · " + row.DB;
                try
                {
                    await Task.Run(() =>
                    {
                        Provision(server, row.DB, user, pass);
                        if (proteger) AplicarPasswordConsola(server, row.DB, user, pass, actual, nueva);
                    });
                    row.Instalado = true; row.NecesitaActualizar = false;
                    row.VersionInstalada = _versionActualTexto;
                    row.Estado = "Listo ahora"; row.Sel = false; ok++;
                }
                catch (Exception ex)
                {
                    row.Estado = "Error"; fallos.Add(row.DB + ": " + ex.Message);
                }
                barProgreso.Value = ok + fallos.Count;
            }

            _trabajando = false; barProgreso.Visibility = Visibility.Collapsed;
            grid.Items.Refresh(); UpdateCounts();
            lblFooter.Text = ok + " lista(s)" + (fallos.Count > 0 ? ", " + fallos.Count + " con error." : ".") + " Reinicia CONTPAQi para ver el botón «Consola BrosLMV».";

            string msg = "Terminado.\n\nListas: " + ok + "\nCon error: " + fallos.Count;
            if (fallos.Count > 0) msg += "\n\n" + string.Join("\n", fallos.Take(6));
            if (proteger && ok > 0) msg += "\n\nContraseña de la Consola aplicada.";
            msg += "\n\nReinicia CONTPAQi para ver el botón «Consola BrosLMV».";
            MessageBox.Show(this, msg, "BrosLMV", MessageBoxButton.OK, fallos.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // Al abrir/cerrar la sección de la Consola: si es una sola empresa y ya tiene contraseña, pide la actual.
        void expConsola_Changed(object sender, RoutedEventArgs e) { }

        void chkProtegerConsola_Changed(object sender, RoutedEventArgs e)
        {
            bool activo = chkProtegerConsola.IsChecked == true;
            pnlPasswordConsola.IsEnabled = activo;
            if (!activo) { pwdConsolaActual.Visibility = Visibility.Collapsed; return; }

            var sel = _rows.Where(r => r.Sel).ToList();
            bool pideActual = false;
            if (sel.Count == 1)
            {
                try { pideActual = ConsolaYaTienePassword(cbServer.Text.Trim(), sel[0].DB, txtUser.Text.Trim(), Pass()); }
                catch { pideActual = false; }
            }
            pwdConsolaActual.Visibility = pideActual ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
