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
                if (e.Contains("instalad")) return "#1E9E5A";
                return "#E08A1E";
            }
        }
        public string EstadoIcon
        {
            get
            {
                string e = (_estado ?? "").ToLower();
                if (e.Contains("error")) return "✕";
                if (e.Contains("instalad")) return "✓";
                return "●";
            }
        }
    }

    public partial class MainWindow : Window
    {
        readonly ObservableCollection<EmpresaRow> _rows = new ObservableCollection<EmpresaRow>();
        readonly string _provisionSql;

        const string DetectSql = @"
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#r') IS NOT NULL DROP TABLE #r;
CREATE TABLE #r(db sysname, instalado bit);
DECLARE @n sysname, @s nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.databases WHERE database_id>4 AND state=0;
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS=0 BEGIN
  SET @s=N'
    IF EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engRibbonControl'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engRibbonGroup'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engModule'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''docDocument'')
    INSERT #r(db,instalado) SELECT N'''+REPLACE(@n,'''','''''')+''',
       CASE WHEN EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.dbo.engRibbonControl WHERE ControlExecute=''BrosLMV.CONSOLA'') THEN 1 ELSE 0 END;';
  BEGIN TRY EXEC sp_executesql @s; END TRY BEGIN CATCH END CATCH;
  FETCH NEXT FROM c INTO @n;
END
CLOSE c; DEALLOCATE c;
SELECT db, instalado FROM #r ORDER BY db;";

        public MainWindow()
        {
            InitializeComponent();
            _provisionSql = LoadResourceText("provision_empresa.sql");
            grid.ItemsSource = _rows;
            cbFiltro.Items.Add("Todas");
            cbFiltro.Items.Add("Pendientes");
            cbFiltro.Items.Add("Instaladas");
            cbFiltro.SelectedIndex = 0;
            var _v = Assembly.GetExecutingAssembly().GetName().Version;
            lblVer.Text = "BrosLMV v" + _v.Major + "." + _v.Minor + "." + _v.Build;
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
                            list.Add(new EmpresaRow
                            {
                                DB = rd["db"].ToString(),
                                Instalado = inst,
                                Estado = inst ? "Ya instalado" : "Pendiente",
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
                    cmd.CommandText = _provisionSql; cmd.CommandTimeout = 120;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        void UpdateCounts()
        {
            int tot = _rows.Count;
            int inst = _rows.Count(r => r.Instalado);
            int pend = tot - inst;
            int sel = _rows.Count(r => r.Sel);
            cardTotal.Text = tot.ToString();
            cardInst.Text = inst.ToString();
            cardPend.Text = pend.ToString();
            btnInstall.Content = "Instalar seleccionadas (" + sel + ")";
            btnInstall.IsEnabled = sel > 0;
            lblFooter.Text = tot + " empresa(s) detectada(s) · " + inst + " instalada(s) · " + pend + " pendiente(s)";
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
                bool okEst = modo == 1 ? !it.Instalado : (modo == 2 ? it.Instalado : true);
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
            foreach (EmpresaRow it in view) if (!it.Instalado) it.Sel = true;
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
                    row.Instalado = true; row.Estado = "Instalado ahora"; row.Sel = false; ok++;
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
