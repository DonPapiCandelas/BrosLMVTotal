using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // Cruce de la lista de XML de Comercial con sus documentos (solo lectura).
    public partial class VinculosWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly List<EmpresaFila> _empresas;

        public class FilaSin
        {
            public string FechaTexto { get; set; } public string Tipo { get; set; } public string Rfc { get; set; }
            public string Nombre { get; set; } public string TotalTexto { get; set; } public string UUID { get; set; } public string Sugerido { get; set; }
        }

        public class FilaDif
        {
            public string Folio { get; set; } public string TotalCfdiTexto { get; set; } public string TotalDocTexto { get; set; }
            public string DiferenciaTexto { get; set; } public string UUID { get; set; }
        }

        public VinculosWindow(SqlConnection conn)
        {
            InitializeComponent();
            _conn = conn;
            _empresas = BrosSatDb.ObtenerEmpresas(conn).Where(e => e.Activa && !string.IsNullOrWhiteSpace(e.ComercialConexionSql)).ToList();
            CmbEmpresa.ItemsSource = _empresas.Select(e => e.Nombre + "  ·  " + e.RFC).ToList();
            if (_empresas.Count > 0) CmbEmpresa.SelectedIndex = 0;
            else LblResumen.Text = "Ninguna empresa tiene configurada la conexión a Comercial (EmpresaWindow > integración con Comercial).";
        }

        private void CmbEmpresa_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Cargar();
        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => Cargar();

        private async void Cargar()
        {
            if (CmbEmpresa.SelectedIndex < 0) return;
            var empresa = _empresas[CmbEmpresa.SelectedIndex];
            LblResumen.Text = "Analizando Comercial...";
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var r = await Vinculacion.AnalizarAsync(DpapiHelper.DescifrarConexionSql(empresa.ComercialConexionSql));
                GridSinDocumento.ItemsSource = r.SinDocumento.Select(x => new FilaSin
                {
                    FechaTexto = x.Fecha.ToString("dd/MM/yyyy"), Tipo = x.Recibido ? "Recibido" : "Emitido", Rfc = x.RfcContraparte, Nombre = x.Nombre,
                    TotalTexto = x.Total.ToString("N2"), UUID = x.UUID.ToString(), Sugerido = x.MejorCandidato
                }).ToList();
                GridDiferencias.ItemsSource = r.Diferencias.Select(x => new FilaDif
                {
                    Folio = x.Folio, TotalCfdiTexto = x.TotalCfdi.ToString("N2"), TotalDocTexto = x.TotalDocumento.ToString("N2"),
                    DiferenciaTexto = x.Diferencia.ToString("N2"), UUID = x.UUID.ToString()
                }).ToList();
                int conSugerencia = r.SinDocumento.Count(x => x.Candidatos.Count > 0);
                LblResumen.Text = r.SinDocumento.Count + " CFDI vigente(s) de los últimos 6 meses sin documento (" + conSugerencia + " con documento sugerido); " +
                    r.Diferencias.Count + " documento(s) vinculado(s) con un total distinto al del CFDI.";
            }
            catch (Exception ex)
            {
                LblResumen.Text = "No se pudo leer Comercial: " + ex.Message;
            }
            finally { Cursor = System.Windows.Input.Cursors.Arrow; }
        }
    }
}
