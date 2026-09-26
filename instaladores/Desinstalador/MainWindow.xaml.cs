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

namespace BrosLMV.Desinstalador
{
    public class EmpresaRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void PC(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        bool _sel;
        public bool Sel { get { return _sel; } set { _sel = value; PC("Sel"); } }
        public string DB { get; set; }
        string _estado;
        public string Estado { get { return _estado; } set { _estado = value; PC("Estado"); PC("EstadoColor"); PC("EstadoIcon"); } }
        public string EstadoColor
        {
            get { string e = (_estado ?? "").ToLower();
                  if (e.Contains("error")) return "#DC2626";
                  if (e.Contains("quitad")) return "#1E9E5A";
                  return "#E08A1E"; }
        }
        public string EstadoIcon
        {
            get { string e = (_estado ?? "").ToLower();
                  if (e.Contains("error")) return "✕";
                  if (e.Contains("quitad")) return "✓";
                  return "●"; }
        }
    }

    public partial class MainWindow : Window
    {
        readonly ObservableCollection<EmpresaRow> _rows = new ObservableCollection<EmpresaRow>();
        readonly string _desprovSql;

        // Lista solo las empresas que TIENEN BrosLMV (botón BrosLMV.CONSOLA).
        const string DetectSql = @"
SET NOCOUNT ON;
IF OBJECT_ID('tempdb..#r') IS NOT NULL DROP TABLE #r;
CREATE TABLE #r(db sysname);
DECLARE @n sysname, @s nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.databases WHERE database_id>4 AND state=0;
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS=0 BEGIN
  SET @s=N'
    IF EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.sys.tables WHERE name=''engRibbonControl'')
       AND EXISTS(SELECT 1 FROM '+QUOTENAME(@n)+'.dbo.engRibbonControl WHERE ControlExecute LIKE ''BrosLMV.%'')
    INSERT #r(db) SELECT N'''+REPLACE(@n,'''','''''')+''';';
  BEGIN TRY EXEC sp_executesql @s; END TRY BEGIN CATCH END CATCH;
  FETCH NEXT FROM c INTO @n;
END
CLOSE c; DEALLOCATE c;
SELECT db FROM #r ORDER BY db;";

        public MainWindow()
        {
            InitializeComponent();
            _desprovSql = LoadResourceText("desprovision_empresa.sql");
            grid.ItemsSource = _rows;
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            lblVer.Text = "BrosLMV v" + v.Major + "." + v.Minor + "." + v.Build;
        }

        static string LoadResourceText(string endsWith)
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new Exception("No se encontró el recurso embebido: " + endsWith);
            using (var s = asm.GetManifestResourceStream(name))
            using (var r = new StreamReader(s)) return r.ReadToEnd();
        }

        string Pass() { return pwdPlain.Visibility == Visibility.Visible ? pwdPlain.Text : pwdBox.Password; }
        static string ConnStr(string server, string db, string user, string pass)
        { return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Connect Timeout=15;"; }

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
                            list.Add(new EmpresaRow { DB = rd["db"].ToString(), Estado = "Instalado", Sel = false });
                }
            }
            return list;
        }

        void Desprovision(string server, string db, string user, string pass)
        {
            using (var cn = new SqlConnection(ConnStr(server, db, user, pass)))
            {
                cn.Open();
                using (var cmd = cn.CreateCommand())
                { cmd.CommandText = _desprovSql; cmd.CommandTimeout = 120; cmd.ExecuteNonQuery(); }
            }
        }

        void UpdateCount()
        {
            int sel = _rows.Count(r => r.Sel);
            btnQuitarEmpresas.Content = "Quitar de empresas seleccionadas (" + sel + ")";
            btnQuitarEmpresas.IsEnabled = sel > 0;
        }

        void Conectar()
        {
            lblFooter.Text = "Conectando...";
            List<EmpresaRow> empresas;
            try { empresas = GetEmpresas(cbServer.Text.Trim(), txtUser.Text.Trim(), Pass()); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo conectar:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                lblConnTitle.Text = "Sin conexión"; lblFooter.Text = "Error de conexión."; return;
            }
            _rows.Clear();
            foreach (var e in empresas)
            {
                e.PropertyChanged += (s, ev) => { if (ev.PropertyName == "Sel") UpdateCount(); };
                _rows.Add(e);
            }
            lblConnTitle.Text = "Conectado a SQL Server";
            lblConnSrv.Text = cbServer.Text.Trim();
            lblConnUsr.Text = "Usuario: " + txtUser.Text.Trim();
            lblFooter.Text = empresas.Count + " empresa(s) con BrosLMV. Marca y presiona Quitar de empresas.";
            UpdateCount();
        }

        void btnEye_Click(object sender, RoutedEventArgs e)
        {
            if (pwdPlain.Visibility == Visibility.Visible)
            { pwdBox.Password = pwdPlain.Text; pwdPlain.Visibility = Visibility.Collapsed; pwdBox.Visibility = Visibility.Visible; }
            else
            { pwdPlain.Text = pwdBox.Password; pwdBox.Visibility = Visibility.Collapsed; pwdPlain.Visibility = Visibility.Visible; }
        }

        void btnConn_Click(object sender, RoutedEventArgs e) { Conectar(); }

        void btnMark_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in _rows) it.Sel = true;
            grid.Items.Refresh(); UpdateCount();
        }

        void btnQuitarEmpresas_Click(object sender, RoutedEventArgs e)
        {
            var sel = _rows.Where(r => r.Sel).ToList();
            if (sel.Count == 0) return;
            if (MessageBox.Show("Se quitará BrosLMV (botón + tablas zzBros*) de " + sel.Count + " empresa(s).\n\n¿Continuar?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string server = cbServer.Text.Trim(), user = txtUser.Text.Trim(), pass = Pass();
            int ok = 0, err = 0;
            foreach (var row in sel)
            {
                lblFooter.Text = "Quitando de " + row.DB + " ...";
                try { Desprovision(server, row.DB, user, pass); row.Estado = "Quitado"; row.Sel = false; ok++; }
                catch (Exception ex) { row.Estado = "ERROR"; err++; MessageBox.Show("Error en " + row.DB + ":\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            grid.Items.Refresh(); UpdateCount();
            MessageBox.Show("Listo.\n\nQuitadas: " + ok + "\nErrores: " + err + "\n\nReinicia CONTPAQi.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void btnQuitarEquipo_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Esto eliminará por completo C:\\BrosLMV, des-registrará el componente y borrará el icono de este equipo.\n\n¿Continuar?",
                "Quitar de este equipo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string res;
            try { res = RuntimeUninstaller.Remove(); }
            catch (Exception ex) { res = "Error: " + ex.Message; }
            lblFooter.Text = res;
            MessageBox.Show(res + "\n\nBrosLMV fue quitado de este equipo.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
