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

// PaginaCancelaciones.xaml.cs -- CFDI cancelados por el SAT que ya estaban vinculados a un documento de Comercial.
// BrosLMV no toca esos documentos: aqui solo se listan y se marcan como revisados.

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaCancelaciones : UserControl
    {
        private readonly Sesion _s;

        public class Fila
        {
            public int Id { get; set; }
            public string FechaTexto { get; set; }
            public string Empresa { get; set; }
            public string UUID { get; set; }
            public string Descripcion { get; set; }
            public string Estado { get; set; }
        }

        public PaginaCancelaciones(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private void Cargar()
        {
            var filas = BrosSatDb.ObtenerCancelacionesConDocumento(_s.Conn, ChkRevisadas.IsChecked == true)
                .Select(x => new Fila
                {
                    Id = x.Id, FechaTexto = x.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Empresa = x.Empresa,
                    UUID = x.UUID.ToString(), Descripcion = x.Descripcion, Estado = x.Revisada ? "Revisada" : "Pendiente"
                }).ToList();
            GridCancelaciones.ItemsSource = filas;
            Vacio.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Cargar(); }

        private void BtnRevisada_Click(object sender, RoutedEventArgs e)
        {
            if (!(GridCancelaciones.SelectedItem is Fila f))
            {
                MessageBox.Show(Window.GetWindow(this), "Selecciona primero una fila de la tabla.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            BrosSatDb.MarcarCancelacionRevisada(_s.Conn, f.Id);
            Cargar();
        }
    }
}
