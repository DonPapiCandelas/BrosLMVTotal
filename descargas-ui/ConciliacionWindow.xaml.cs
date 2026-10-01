using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // Conciliacion mensual: lo que el SAT reporta (Metadata) contra los XML descargados.
    public partial class ConciliacionWindow : Window
    {
        private readonly SqlConnection _conn;
        private List<EmpresaFila> _empresas;
        private List<ConciliacionFila> _filas = new List<ConciliacionFila>();

        public ConciliacionWindow(SqlConnection conn, string rfcInicial = null)
        {
            InitializeComponent();
            _conn = conn;
            _empresas = BrosSatDb.ObtenerEmpresas(conn).Where(e => e.Activa).ToList();
            CmbEmpresa.ItemsSource = _empresas.Select(e => e.Nombre + "  ·  " + e.RFC).ToList();
            int i = rfcInicial == null ? 0 : Math.Max(0, _empresas.FindIndex(e => e.RFC == rfcInicial));
            if (_empresas.Count > 0) CmbEmpresa.SelectedIndex = i;
        }

        private void CmbEmpresa_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbEmpresa.SelectedIndex < 0) return;
            var empresa = _empresas[CmbEmpresa.SelectedIndex];
            _filas = Conciliacion.Calcular(_conn, empresa.RFC);
            GridConciliacion.ItemsSource = _filas;
            int faltan = _filas.Sum(f => f.Faltan);
            int sinMeta = _filas.Count(f => !f.HayMetadata && f.Descargados > 0);
            LblResumen.Text = faltan == 0
                ? (sinMeta == 0 ? "Todo conciliado: no falta ningún CFDI vigente que el SAT reporte." : "Sin faltantes en los meses con Metadata; " + sinMeta + " mes(es) todavía sin Metadata del SAT para verificar.")
                : "Faltan " + faltan.ToString("N0") + " CFDI vigentes que el SAT reporta; se vuelven a pedir por mes de forma automática.";
        }

        private void BtnCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_filas.Count == 0 || CmbEmpresa.SelectedIndex < 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv", FileName = "conciliacion_" + _empresas[CmbEmpresa.SelectedIndex].RFC + "_" + DateTime.Today.ToString("yyyyMMdd") + ".csv"
            };
            if (dlg.ShowDialog(this) != true) return;
            System.IO.File.WriteAllText(dlg.FileName, Conciliacion.ACsv(_filas), new System.Text.UTF8Encoding(true));
            MessageBox.Show(this, "Archivo guardado:\n" + dlg.FileName, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
