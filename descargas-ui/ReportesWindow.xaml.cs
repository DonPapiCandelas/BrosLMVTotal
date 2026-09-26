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

// ReportesWindow.xaml.cs -- pedido explicito del usuario (2026-08-18): tarjeta de totales por
// mes, facturado/comprado, top proveedores, cobrado (PUE) vs a credito (PPD), facturas con
// MetodoPago/FormaPago incoherentes, PPD sin complemento de pago, notas de credito relacionadas,
// y desglose por UsoCFDI. Los datos vienen de ReportesSql.cs (Datos/), esta ventana solo formatea
// para mostrar.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    public partial class ReportesWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly string _rfc;

        // Catalogo c_UsoCFDI del SAT -- solo las claves de uso comun, para que el reporte diga
        // algo mas util que el codigo pelón. Si aparece una clave no listada aqui, se muestra el
        // codigo solo (ver DescripcionUso).
        private static readonly Dictionary<string, string> DescripcionesUso = new Dictionary<string, string>
        {
            ["G01"] = "Adquisición de mercancías",
            ["G02"] = "Devoluciones, descuentos o bonificaciones",
            ["G03"] = "Gastos en general",
            ["I01"] = "Construcciones",
            ["I02"] = "Mobiliario y equipo de oficina",
            ["I03"] = "Equipo de transporte",
            ["I04"] = "Equipo de cómputo",
            ["I05"] = "Dados, troqueles, moldes, matrices",
            ["I06"] = "Comunicaciones telefónicas",
            ["I07"] = "Comunicaciones satelitales",
            ["I08"] = "Otra maquinaria y equipo",
            ["D01"] = "Honorarios médicos, dentales y gastos hospitalarios",
            ["D02"] = "Gastos médicos por incapacidad o discapacidad",
            ["D03"] = "Gastos funerales",
            ["D04"] = "Donativos",
            ["D05"] = "Intereses reales por créditos hipotecarios",
            ["D06"] = "Aportaciones voluntarias al SAR",
            ["D07"] = "Primas por seguros de gastos médicos",
            ["D08"] = "Gastos de transportación escolar obligatoria",
            ["D09"] = "Depósitos en cuentas para el ahorro/pensiones",
            ["D10"] = "Pagos por servicios educativos (colegiaturas)",
            ["S01"] = "Sin efectos fiscales",
            ["CP01"] = "Pagos",
            ["CN01"] = "Nómina",
            ["P01"] = "Por definir"
        };

        internal ReportesWindow(SqlConnection conn, string rfc, string nombreEmpresa)
        {
            InitializeComponent();
            _conn = conn;
            _rfc = rfc;
            LblEmpresa.Text = nombreEmpresa + " (" + rfc + ")";

            CargarListaDeMeses();
            CargarTodo();
        }

        private void CargarListaDeMeses()
        {
            var meses = new List<MesItem>();
            var cursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            for (int i = 0; i < 24; i++)
            {
                meses.Add(new MesItem { Anio = cursor.Year, Mes = cursor.Month, Texto = cursor.ToString("MMMM yyyy", new CultureInfo("es-MX")) });
                cursor = cursor.AddMonths(-1);
            }
            CmbMes.ItemsSource = meses;
            CmbMes.SelectedIndex = 0;
        }

        private void CmbMes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => CargarTodo();

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => CargarTodo();

        private void CargarTodo()
        {
            if (!(CmbMes.SelectedItem is MesItem mes)) return;

            CargarResumen(mes.Anio, mes.Mes);
            CargarProveedores(mes.Anio, mes.Mes);
            CargarErroresCoherencia();
            CargarCuentasPorPagar();
            CargarCuentasPorCobrar();
            CargarNotasCredito();
            CargarUsosCfdi(mes.Anio, mes.Mes);
        }

        private static string Moneda(decimal valor) => valor.ToString("C2", new CultureInfo("es-MX"));

        private void CargarResumen(int anio, int mes)
        {
            var r = ReportesSql.ObtenerResumenMensual(_conn, _rfc, anio, mes);
            LblFacturado.Text = Moneda(r.FacturadoTotal);
            LblFacturadoSub.Text = r.FacturadoCount + " factura(s)";
            LblComprado.Text = Moneda(r.CompradoTotal);
            LblCompradoSub.Text = r.CompradoCount + " factura(s)";
            LblIvaFacturado.Text = Moneda(r.FacturadoIva);
            LblIvaComprado.Text = Moneda(r.CompradoIva);
            LblPue.Text = Moneda(r.CompradoPueTotal);
            LblPueSub.Text = r.CompradoPueCount + " factura(s)";
            LblPpd.Text = Moneda(r.CompradoPpdTotal);
            LblPpdSub.Text = r.CompradoPpdCount + " factura(s)";
        }

        private void CargarProveedores(int anio, int mes)
        {
            var lista = ReportesSql.ObtenerTopProveedores(_conn, _rfc, anio, mes, top: 15)
                .Select(p => new { p.RFC, p.Nombre, p.Count, TotalTexto = Moneda(p.Total) })
                .ToList();
            GridProveedores.ItemsSource = lista;
        }

        private void CargarErroresCoherencia()
        {
            var lista = ReportesSql.ObtenerErroresCoherenciaPago(_conn, _rfc)
                .Select(x => new
                {
                    FechaTexto = x.FechaEmision.ToString("yyyy-MM-dd"),
                    x.RFCEmisor,
                    x.NombreEmisor,
                    SerieFolio = (x.Serie ?? "") + " " + (x.Folio ?? ""),
                    x.MetodoPago,
                    x.FormaPago,
                    TotalTexto = Moneda(x.Total ?? 0),
                    x.Motivo
                }).ToList();
            GridErrores.ItemsSource = lista;
        }

        private void CargarCuentasPorPagar()
        {
            var lista = ReportesSql.ObtenerCuentasPorPagar(_conn, _rfc)
                .Select(x => new
                {
                    FechaTexto = x.FechaEmision.ToString("yyyy-MM-dd"),
                    x.RFCEmisor,
                    x.NombreEmisor,
                    SerieFolio = (x.Serie ?? "") + " " + (x.Folio ?? ""),
                    TotalTexto = Moneda(x.Total),
                    PagadoTexto = Moneda(x.Pagado),
                    NotaCreditoTexto = x.NotaCreditoTotal > 0 ? Moneda(x.NotaCreditoTotal) : "-",
                    SaldoTexto = Moneda(x.Saldo),
                    Estado = x.NumComplementos == 0 ? "Sin complemento" : "Parcial (" + x.NumComplementos + ")",
                    DiasTexto = x.DiasTranscurridos + " dias"
                }).ToList();
            GridCuentasPorPagar.ItemsSource = lista;
        }

        private void CargarCuentasPorCobrar()
        {
            var lista = ReportesSql.ObtenerCuentasPorCobrar(_conn, _rfc)
                .Select(x => new
                {
                    FechaTexto = x.FechaEmision.ToString("yyyy-MM-dd"),
                    x.RFCReceptor,
                    SerieFolio = (x.Serie ?? "") + " " + (x.Folio ?? ""),
                    TotalTexto = Moneda(x.Total),
                    PagadoTexto = Moneda(x.Pagado),
                    NotaCreditoTexto = x.NotaCreditoTotal > 0 ? Moneda(x.NotaCreditoTotal) : "-",
                    SaldoTexto = Moneda(x.Saldo),
                    Estado = x.NumComplementos == 0 ? "Sin complemento" : "Parcial (" + x.NumComplementos + ")",
                    DiasTexto = x.DiasTranscurridos + " dias"
                }).ToList();
            GridCuentasPorCobrar.ItemsSource = lista;
        }

        private void CargarNotasCredito()
        {
            var lista = ReportesSql.ObtenerNotasCreditoRelacionadas(_conn, _rfc)
                .Select(x => new
                {
                    FechaNcTexto = x.FechaNotaCredito.ToString("yyyy-MM-dd"),
                    SerieFolioNc = (x.SerieNotaCredito ?? "") + " " + (x.FolioNotaCredito ?? ""),
                    TotalNcTexto = Moneda(x.TotalNotaCredito ?? 0),
                    SerieFolioOriginal = x.OriginalEnBd ? (x.SerieOriginal ?? "") + " " + (x.FolioOriginal ?? "") : "(no descargada)",
                    TotalOriginalTexto = x.OriginalEnBd ? Moneda(x.TotalOriginal ?? 0) : "-",
                    OriginalEnBdTexto = x.OriginalEnBd ? "Si" : "No"
                }).ToList();
            GridNotasCredito.ItemsSource = lista;
        }

        private void CargarUsosCfdi(int anio, int mes)
        {
            var lista = ReportesSql.ObtenerUsosCfdiResumen(_conn, _rfc, anio, mes)
                .Select(x => new
                {
                    x.UsoCFDI,
                    Descripcion = DescripcionesUso.TryGetValue(x.UsoCFDI, out var d) ? d : "",
                    x.Count,
                    TotalTexto = Moneda(x.Total)
                }).ToList();
            GridUsos.ItemsSource = lista;
        }

        private sealed class MesItem
        {
            public int Anio;
            public int Mes;
            public string Texto;
            public override string ToString() => Texto;
        }
    }
}
