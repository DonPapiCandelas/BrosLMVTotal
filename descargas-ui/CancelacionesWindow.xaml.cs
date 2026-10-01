using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // CFDI cancelados en el SAT que ya estaban vinculados a un documento de Comercial.
    public partial class CancelacionesWindow : Window
    {
        private readonly SqlConnection _conn;

        public class Fila
        {
            public int Id { get; set; }
            public string FechaTexto { get; set; }
            public string Empresa { get; set; }
            public string UUID { get; set; }
            public string Descripcion { get; set; }
            public string Estado { get; set; }
        }

        public CancelacionesWindow(SqlConnection conn)
        {
            InitializeComponent();
            _conn = conn;
            Loaded += (s, e) => Cargar();
        }

        private void Cargar()
        {
            var filas = BrosSatDb.ObtenerCancelacionesConDocumento(_conn, ChkRevisadas.IsChecked == true)
                .Select(x => new Fila
                {
                    Id = x.Id, FechaTexto = x.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Empresa = x.Empresa,
                    UUID = x.UUID.ToString(), Descripcion = x.Descripcion, Estado = x.Revisada ? "Revisada" : "Pendiente"
                }).ToList();
            GridCancelaciones.ItemsSource = filas;
        }

        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Cargar(); }

        private void BtnRevisada_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridCancelaciones.SelectedItem is Fila f)) { MessageBox.Show(this, "Selecciona una fila.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            BrosSatDb.MarcarCancelacionRevisada(_conn, f.Id);
            Cargar();
        }
    }
}
