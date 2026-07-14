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

        string _versionInstalada;
        public string VersionInstalada
        {
            get { return _versionInstalada; }
            set { _versionInstalada = value; PC("VersionInstalada"); }
        }

        string _estado;
        public string Estado
        {
            get { return _estado; }
            set { _estado = value; PC("Estado"); PC("EstadoColor"); PC("EstadoIcon"); }
        }
        public string EstadoColor
        {
            get
            {
                string e = (_estado ?? "").ToLower();
                if (e.Contains("error")) return "#DC2626";
                if (e.Contains("actualizar")) return "#2D6FE0";
                if (e.Contains("instalad") || e.Contains("actualizad")) return "#1E9E5A";
                return "#E08A1E";
            }
        }
        public string EstadoIcon
        {
            get
            {
                string e = (_estado ?? "").ToLower();
                if (e.Contains("error")) return "✕";
                if (e.Contains("actualizar")) return "↑";
                if (e.Contains("instalad") || e.Contains("actualizad")) return "✓";
                return "●";
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
            cbFiltro.Items.Add("Todas");
            cbFiltro.Items.Add("Pendientes");
            cbFiltro.Items.Add("Instaladas");
            cbFiltro.Items.Add("Por actualizar");
            cbFiltro.SelectedIndex = 0;
            _versionActual = Assembly.GetExecutingAssembly().GetName().Version;
            _versionActualTexto = _versionActual.Major + "." + _versionActual.Minor + "." + _versionActual.Build;
            lblVer.Text = "BrosLMV v" + _versionActualTexto;
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
            lblFooter.Text = (ok ? "✓ " : "⚠ ") + msg +
                "   —   Si este equipo es una TERMINAL, ya terminaste (puedes cerrar). Si es el SERVIDOR, conéctate y provisiona las empresas.";
            lblFooter.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ok ? "#1E9E5A" : "#DC2626"));
        }

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
                            string estado = !inst ? "Pendiente" : (desact ? "Actualizar disponible" : "Ya instalado");
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
            }
        }

        void UpdateCounts()
        {
            int tot = _rows.Count;
            int inst = _rows.Count(r => r.Instalado);
            int pend = tot - inst;
            int desact = _rows.Count(r => r.NecesitaActualizar);
            int sel = _rows.Count(r => r.Sel);
            cardTotal.Text = tot.ToString();
            cardInst.Text = inst.ToString();
            cardPend.Text = pend.ToString();
            btnInstall.Content = "Instalar seleccionadas (" + sel + ")";
            btnInstall.IsEnabled = sel > 0;
            lblFooter.Text = tot + " empresa(s) detectada(s) · " + inst + " instalada(s) · " + pend + " pendiente(s)" +
                (desact > 0 ? " · " + desact + " por actualizar" : "");
        }

        void ApplyFilter()
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            string txt = (txtSearch.Text ?? "").ToLower();
            int modo = cbFiltro.SelectedIndex;
            view.Filter = o =>
            {
                var it = (EmpresaRow)o;
                bool okTxt = string.IsNullOrWhiteSpace(txt) || it.DB.ToLower().Contains(txt);
                bool okEst = modo == 1 ? !it.Instalado
                    : modo == 2 ? it.Instalado
                    : modo == 3 ? it.NecesitaActualizar
                    : true;
                return okTxt && okEst;
            };
            view.Refresh();
        }

        void Conectar()
        {
            lblFooter.Text = "Conectando...";
            List<EmpresaRow> empresas;
            try { empresas = GetEmpresas(cbServer.Text.Trim(), txtUser.Text.Trim(), Pass()); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo conectar:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                lblConnTitle.Text = "Sin conexión"; lblFooter.Text = "Error de conexión.";
                return;
            }
            _rows.Clear();
            foreach (var e in empresas)
            {
                e.PropertyChanged += (s, ev) => { if (ev.PropertyName == "Sel") UpdateCounts(); };
                _rows.Add(e);
            }
            lblConnTitle.Text = "Conectado a SQL Server";
            lblConnSrv.Text = cbServer.Text.Trim();
            lblConnUsr.Text = "Usuario: " + txtUser.Text.Trim();

            // Guarda las credenciales en broslmv_conn.txt para que la consola pueda
            // conectar a SQL cuando CONTPAQi no entregue la conexión viva. La BASE la
            // pone el DataLayer de CONTPAQi (la empresa activa); aquí solo van
            // servidor + usuario + contraseña, válidos para todas las empresas.
            try
            {
                string dir = @"C:\BrosLMV\bin";
                if (System.IO.Directory.Exists(dir))
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "broslmv_conn.txt"),
                        "Server=" + cbServer.Text.Trim() + ";User Id=" + txtUser.Text.Trim() +
                        ";Password=" + Pass() + ";TrustServerCertificate=True;");
            }
            catch { }

            ApplyFilter();
            UpdateCounts();
        }

        // ---- Eventos ----
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

        void btnConn_Click(object sender, RoutedEventArgs e) { Conectar(); }
        void txtSearch_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
        void cbFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

        void btnMark_Click(object sender, RoutedEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            foreach (EmpresaRow it in view) if (!it.Instalado || it.NecesitaActualizar) it.Sel = true;
            grid.Items.Refresh();
            UpdateCounts();
        }

        void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            var sel = _rows.Where(r => r.Sel).ToList();
            if (sel.Count == 0) return;
            string server = cbServer.Text.Trim(), user = txtUser.Text.Trim(), pass = Pass();
            int ok = 0, err = 0;
            foreach (var row in sel)
            {
                lblFooter.Text = "Provisionando " + row.DB + " ...";
                try
                {
                    Provision(server, row.DB, user, pass);
                    row.Instalado = true; row.NecesitaActualizar = false;
                    row.VersionInstalada = _versionActualTexto;
                    row.Estado = "Instalado ahora"; row.Sel = false; ok++;
                }
                catch (Exception ex)
                {
                    row.Estado = "ERROR"; err++;
                    MessageBox.Show("Error en " + row.DB + ":\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            grid.Items.Refresh();
            UpdateCounts();
            MessageBox.Show("Provisión terminada.\n\nOK: " + ok + "\nErrores: " + err +
                "\n\nReinicia CONTPAQi para ver el botón 'Consola BrosLMV'.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
