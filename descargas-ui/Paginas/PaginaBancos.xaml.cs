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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaBancos : UserControl
    {
        private sealed class OpcionCuenta { public int? Id; public string Texto; public override string ToString() => Texto; }

        private readonly Sesion _s;
        private bool _armando;

        public PaginaBancos(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private Window Ventana => Window.GetWindow(this);
        private string Rfc => _s.Empresa.RFC;

        private void Cargar()
        {
            try
            {
                _armando = true;
                var cuentas = BancoDatos.ObtenerCuentas(_s.Conn, Rfc);
                int? previa = (CmbCuenta.SelectedItem as OpcionCuenta)?.Id;
                var opciones = new List<OpcionCuenta> { new OpcionCuenta { Id = null, Texto = "Todas las cuentas" } };
                opciones.AddRange(cuentas.Select(c => new OpcionCuenta { Id = c.Id, Texto = c.Etiqueta }));
                CmbCuenta.ItemsSource = opciones; CmbCuenta.SelectedItem = opciones.FirstOrDefault(o => o.Id == previa) ?? opciones[0];
                string tipoPrevio = CmbTipo.SelectedItem as string;
                var tipos = new List<string> { "Todos los tipos" }; tipos.AddRange(BancoDatos.Tipos(_s.Conn, Rfc));
                CmbTipo.ItemsSource = tipos; CmbTipo.SelectedItem = tipos.Contains(tipoPrevio) ? tipoPrevio : tipos[0];
                _armando = false;

                int total = cuentas.Sum(c => c.Movimientos);
                LblEstado.Text = cuentas.Count == 0
                    ? "Aún no hay cuentas bancarias. Pulsa «Importar estado de cuenta»: eliges el archivo del banco y ahí mismo registras la cuenta."
                    : cuentas.Count + " cuenta(s) · " + total.ToString("N0") + " movimientos cargados" + (total > 0 ? " · último movimiento " + cuentas.Max(c => c.UltimaFecha ?? DateTime.MinValue).ToString("yyyy-MM-dd") : "") + ". Todo se guarda solo en este equipo.";
                Pintar();
            }
            catch (Exception ex) { _armando = false; LblEstado.Text = "No se pudo leer: " + ex.Message; }
        }

        private void Pintar()
        {
            if (_armando) return;
            int? cuenta = (CmbCuenta.SelectedItem as OpcionCuenta)?.Id;
            string tipo = CmbTipo.SelectedIndex > 0 ? CmbTipo.SelectedItem as string : null;
            try
            {
                var movs = BancoDatos.Movimientos(_s.Conn, Rfc, cuenta, DpDesde.SelectedDate, DpHasta.SelectedDate, TxtBuscar.Text, tipo, 5000);
                Grid.ItemsSource = movs;
                LblConteo.Text = movs.Count.ToString("N0") + " movimientos" + (movs.Count >= 5000 ? " (se muestran 5,000; afina el filtro)" : "");
                LblTotales.Text = movs.Count == 0 ? "" : "Cargos " + movs.Sum(m => m.Cargo).ToString("C2") + "   ·   Abonos " + movs.Sum(m => m.Abono).ToString("C2") + "   ·   Neto " + movs.Sum(m => m.Abono - m.Cargo).ToString("C2");
            }
            catch (Exception ex) { LblConteo.Text = "No se pudo leer: " + ex.Message; }
        }

        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Pintar(); }

        private void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var w = new BancoImportarWindow(_s.Conn, Rfc) { Owner = Ventana };
            w.ShowDialog();
            Cargar();
        }

        private void BtnCuentas_Click(object sender, RoutedEventArgs e)
        {
            var w = new BancoCuentasWindow(_s.Conn, Rfc) { Owner = Ventana };
            w.ShowDialog();
            if (w.Cambio) Cargar();
        }

        private void BtnCargas_Click(object sender, RoutedEventArgs e)
        {
            var w = new BancoCargasWindow(_s.Conn, Rfc) { Owner = Ventana };
            w.ShowDialog();
            if (w.Cambio) Cargar();
        }
    }
}
