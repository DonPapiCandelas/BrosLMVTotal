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

// ReportesWindow.xaml.cs -- Presentación y exportación ejecutiva de reportes financieros y fiscales.
// Conecta los datos de ReportesSql con la interfaz WPF moderna y con el motor de generación
// de Excel corporativo (ExcelReporteEjecutivo.cs).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace BrosLMV.DescargasUI
{
    public partial class ReportesWindow : Window
    {
        private readonly SqlConnection _conn;
        private readonly string _rfc;
        private readonly string _nombreEmpresa;

        // Catálogo c_UsoCFDI del SAT para visualización amigable
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
            ["D01"] = "Honorarios médicos, dentales y hospitalarios",
            ["D02"] = "Gastos médicos por incapacidad",
            ["D03"] = "Gastos funerales",
            ["D04"] = "Donativos",
            ["D05"] = "Intereses por créditos hipotecarios",
            ["D06"] = "Aportaciones voluntarias al SAR",
            ["D07"] = "Primas por seguros de gastos médicos",
            ["D08"] = "Gastos de transportación escolar",
            ["D09"] = "Depósitos en cuentas para el ahorro/pensiones",
            ["D10"] = "Colegiaturas y servicios educativos",
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
            _nombreEmpresa = nombreEmpresa;
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
                meses.Add(new MesItem
                {
                    Anio = cursor.Year,
                    Mes = cursor.Month,
                    Texto = cursor.ToString("MMMM yyyy", new CultureInfo("es-MX"))
                });
                cursor = cursor.AddMonths(-1);
            }
            CmbMes.ItemsSource = meses;

            // Busca automáticamente el mes más reciente con CFDIs en la BD para no mostrar una pantalla en ceros
            try
            {
                const string sql = @"
SELECT TOP 1 YEAR(FechaEmision), MONTH(FechaEmision)
FROM CfdiRecibido
WHERE (RFCEmisor = @Rfc OR RFCReceptor = @Rfc) AND Archivado = 0
ORDER BY FechaEmision DESC;";
                using (var cmd = new SqlCommand(sql, _conn))
                {
                    cmd.Parameters.AddWithValue("@Rfc", _rfc);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int anioDb = reader.GetInt32(0);
                            int mesDb = reader.GetInt32(1);
                            var item = meses.FirstOrDefault(m => m.Anio == anioDb && m.Mes == mesDb);
                            if (item != null)
                            {
                                CmbMes.SelectedItem = item;
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback silencioso al primer elemento
            }

            CmbMes.SelectedIndex = 0;
        }

        private void CmbMes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => CargarTodo();

        private void BtnActualizar_Click(object sender, RoutedEventArgs e) => CargarTodo();

        private void CargarTodo()
        {
            if (!(CmbMes.SelectedItem is MesItem mes)) return;

            CargarResumen(mes.Anio, mes.Mes);
            CargarAuditoriaFiscal(mes.Anio, mes.Mes);
            CargarProveedores(mes.Anio, mes.Mes);
            CargarClientes(mes.Anio, mes.Mes);
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
            LblFacturadoSub.Text = r.FacturadoCount + " comprobantes emitidos";

            LblComprado.Text = Moneda(r.CompradoTotal);
            LblCompradoSub.Text = r.CompradoCount + " facturas recibidas";

            decimal balanceNeto = r.FacturadoTotal - r.CompradoTotal;
            LblBalanceNeto.Text = Moneda(balanceNeto);
            if (balanceNeto >= 0)
            {
                LblBalanceNeto.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                LblBalanceNetoSub.Text = "Superávit comercial positivo";
                CardBorderBalance.BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
            }
            else
            {
                LblBalanceNeto.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                LblBalanceNetoSub.Text = "Déficit mensual en compras";
                CardBorderBalance.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xA4, 0xAF));
            }

            LblIvaFacturado.Text = Moneda(r.FacturadoIva);
            LblIvaComprado.Text = Moneda(r.CompradoIva);
            decimal balanceIva = r.FacturadoIva - r.CompradoIva;
            LblIvaDiferencial.Text = "Diferencial de IVA: " + Moneda(balanceIva);

            LblPue.Text = Moneda(r.CompradoPueTotal);
            LblPueSub.Text = r.CompradoPueCount + " facturas";

            LblPpd.Text = Moneda(r.CompradoPpdTotal);
            LblPpdSub.Text = r.CompradoPpdCount + " facturas";
        }

        private void CargarAuditoriaFiscal(int anio, int mes)
        {
            var c = ReportesSql.ObtenerCedulaImpuestos(_conn, _rfc, anio, mes);
            var riesgoRep = ReportesSql.ObtenerFacturasRiesgoRep(_conn, _rfc, diasMinimos: 60);

            LblAuditIvaCobrado.Text = Moneda(c.VentasIvaPue);
            LblAuditVentasPueSub.Text = "Base gravable: " + Moneda(c.VentasSubtotalPue);

            LblAuditIvaPagado.Text = Moneda(c.ComprasIvaPue);
            LblAuditComprasPueSub.Text = "Base gravable: " + Moneda(c.ComprasSubtotalPue);

            decimal ivaNeto = c.IvaDiferencialEfectivo;
            LblAuditIvaNeto.Text = Moneda(Math.Abs(ivaNeto));
            if (ivaNeto >= 0)
            {
                LblAuditIvaNeto.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                LblAuditIvaNetoSub.Text = "Estimado a CARGO (a pagar al SAT)";
                CardBorderAuditIva.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xA4, 0xAF));
            }
            else
            {
                LblAuditIvaNeto.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                LblAuditIvaNetoSub.Text = "Estimado A FAVOR (saldo recuperable)";
                CardBorderAuditIva.BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
            }

            decimal totalIvaRiesgo = riesgoRep.Sum(x => x.IVA);
            LblAuditPpdRiesgo.Text = riesgoRep.Count + " facturas (" + Moneda(totalIvaRiesgo) + ")";
            LblAuditPpdRiesgoSub.Text = "Monto en riesgo: " + Moneda(riesgoRep.Sum(x => x.Total));

            // Desglose Ventas
            LblAuditVentasSubtotalPue.Text = Moneda(c.VentasSubtotalPue);
            LblAuditVentasIvaPue.Text = Moneda(c.VentasIvaPue);
            LblAuditVentasSubtotalPpd.Text = Moneda(c.VentasSubtotalPpd);
            LblAuditVentasIvaPpd.Text = Moneda(c.VentasIvaPpd);
            LblAuditVentasTotal.Text = Moneda(c.VentasTotal);
            LblAuditRetencionesEmitidas.Text = Moneda(c.RetencionesEmitidas);

            // Desglose Compras
            LblAuditComprasSubtotalPue.Text = Moneda(c.ComprasSubtotalPue);
            LblAuditComprasIvaPue.Text = Moneda(c.ComprasIvaPue);
            LblAuditComprasSubtotalPpd.Text = Moneda(c.ComprasSubtotalPpd);
            LblAuditComprasIvaPpd.Text = Moneda(c.ComprasIvaPpd);
            LblAuditComprasTotal.Text = Moneda(c.ComprasTotal);
            LblAuditRetencionesRecibidas.Text = Moneda(c.RetencionesRecibidas);

            GridRiesgoRep.ItemsSource = riesgoRep.Select(x => new
            {
                FechaTexto = x.FechaEmision.ToString("yyyy-MM-dd"),
                x.RFCEmisor,
                NombreEmisor = string.IsNullOrWhiteSpace(x.NombreEmisor) ? "(Sin nombre)" : x.NombreEmisor,
                SerieFolio = string.IsNullOrWhiteSpace(x.SerieFolio) ? "-" : x.SerieFolio,
                TotalTexto = Moneda(x.Total),
                IvaTexto = Moneda(x.IVA),
                DiasTexto = x.DiasTranscurridos + " días",
                x.Riesgo
            }).ToList();
        }

        private void CargarProveedores(int anio, int mes)
        {
            var lista = ReportesSql.ObtenerTopProveedores(_conn, _rfc, anio, mes, top: 10)
                .Select(p => new
                {
                    p.RFC,
                    Nombre = string.IsNullOrWhiteSpace(p.Nombre) ? "(Sin nombre)" : p.Nombre,
                    p.Count,
                    TotalTexto = Moneda(p.Total)
                }).ToList();
            GridProveedores.ItemsSource = lista;
        }

        private void CargarClientes(int anio, int mes)
        {
            var lista = ReportesSql.ObtenerTopClientes(_conn, _rfc, anio, mes, top: 10)
                .Select(c => new
                {
                    c.RFC,
                    Nombre = string.IsNullOrWhiteSpace(c.Nombre) ? c.RFC : c.Nombre,
                    c.Count,
                    TotalTexto = Moneda(c.Total)
                }).ToList();
            GridClientes.ItemsSource = lista;
        }

        private void CargarErroresCoherencia()
        {
            var lista = ReportesSql.ObtenerErroresCoherenciaPago(_conn, _rfc)
                .Select(x => new
                {
                    FechaTexto = x.FechaEmision.ToString("yyyy-MM-dd"),
                    x.RFCEmisor,
                    NombreEmisor = string.IsNullOrWhiteSpace(x.NombreEmisor) ? "(Sin nombre)" : x.NombreEmisor,
                    SerieFolio = (x.Serie ?? "") + " " + (x.Folio ?? ""),
                    x.MetodoPago,
                    x.FormaPago,
                    UsoCFDI = x.UsoCFDI ?? "-",
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
                    NombreEmisor = string.IsNullOrWhiteSpace(x.NombreEmisor) ? "(Sin nombre)" : x.NombreEmisor,
                    SerieFolio = (x.Serie ?? "") + " " + (x.Folio ?? ""),
                    TotalTexto = Moneda(x.Total),
                    PagadoTexto = Moneda(x.Pagado),
                    NotaCreditoTexto = x.NotaCreditoTotal > 0 ? Moneda(x.NotaCreditoTotal) : "-",
                    SaldoTexto = Moneda(x.Saldo),
                    SaldoNumerico = x.Saldo,
                    Estado = x.NumComplementos == 0 ? "Sin complemento" : "Parcial (" + x.NumComplementos + ")",
                    DiasTexto = x.DiasTranscurridos + " días"
                }).ToList();

            decimal totalSaldo = lista.Sum(x => x.SaldoNumerico);
            LblTotalCxP.Text = "Saldo por pagar: " + Moneda(totalSaldo);
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
                    SaldoNumerico = x.Saldo,
                    Estado = x.NumComplementos == 0 ? "Sin complemento" : "Parcial (" + x.NumComplementos + ")",
                    DiasTexto = x.DiasTranscurridos + " días"
                }).ToList();

            decimal totalSaldo = lista.Sum(x => x.SaldoNumerico);
            LblTotalCxC.Text = "Cartera por cobrar: " + Moneda(totalSaldo);
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
                    SerieFolioOriginal = x.OriginalEnBd ? (x.SerieOriginal ?? "") + " " + (x.FolioOriginal ?? "") : "(No descargada)",
                    TotalOriginalTexto = x.OriginalEnBd ? Moneda(x.TotalOriginal ?? 0) : "-",
                    OriginalEnBdTexto = x.OriginalEnBd ? "Sí" : "No"
                }).ToList();
            GridNotasCredito.ItemsSource = lista;
        }

        private void CargarUsosCfdi(int anio, int mes)
        {
            var lista = ReportesSql.ObtenerUsosCfdiResumen(_conn, _rfc, anio, mes)
                .Select(x => new
                {
                    x.UsoCFDI,
                    Descripcion = DescripcionesUso.TryGetValue(x.UsoCFDI, out var d) ? d : "(Otro concepto)",
                    Count = x.Count.ToString("N0"),
                    TotalTexto = Moneda(x.Total)
                }).ToList();
            GridUsos.ItemsSource = lista;
        }

        // ==========================================
        // EXPORTACIONES A EXCEL EJECUTIVO
        // ==========================================

        private void BtnExportarEjecutivo_Click(object sender, RoutedEventArgs e)
        {
            if (!(CmbMes.SelectedItem is MesItem mes)) return;

            string nombreLimpio = new string(_nombreEmpresa.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (string.IsNullOrWhiteSpace(nombreLimpio)) nombreLimpio = _rfc;

            var dialogo = new SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"Informe_Ejecutivo_{nombreLimpio}_{mes.Anio}{mes.Mes:00}.xlsx"
            };

            if (dialogo.ShowDialog(this) != true) return;

            try
            {
                ExcelReporteEjecutivo.GenerarReporteCompleto(
                    dialogo.FileName,
                    _conn,
                    _rfc,
                    _nombreEmpresa,
                    mes.Anio,
                    mes.Mes);

                var r = MessageBox.Show(this,
                    $"Se generó exitosamente el Informe Ejecutivo en Excel:\n\n{dialogo.FileName}\n\n¿Deseas abrir el archivo ahora?",
                    "Exportación Ejecutiva Completada",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialogo.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error al generar el libro de Excel:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportarErrores_Click(object sender, RoutedEventArgs e)
        {
            ExportarPestanaGenerica("Inconsistencias_Fiscales", "Inconsistencias Fiscales", new[]
            {
                "Fecha Emisión", "RFC Emisor", "Proveedor", "Serie / Folio", "Método Pago", "Forma Pago", "Total", "Motivo de Inconsistencia"
            }, (ws, startRow) =>
            {
                var lista = ReportesSql.ObtenerErroresCoherenciaPago(_conn, _rfc);
                int r = startRow;
                foreach (var item in lista)
                {
                    ws.Cell(r, 1).Value = item.FechaEmision.ToString("yyyy-MM-dd");
                    ws.Cell(r, 2).Value = item.RFCEmisor;
                    ws.Cell(r, 3).Value = item.NombreEmisor ?? "";
                    ws.Cell(r, 4).Value = (item.Serie ?? "") + " " + (item.Folio ?? "");
                    ws.Cell(r, 5).Value = item.MetodoPago;
                    ws.Cell(r, 6).Value = item.FormaPago;
                    ws.Cell(r, 7).Value = item.Total ?? 0m;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 8).Value = item.Motivo;
                    r++;
                }
            });
        }

        private void BtnExportarCxP_Click(object sender, RoutedEventArgs e)
        {
            ExportarPestanaGenerica("Cuentas_Por_Pagar", "Cuentas por Pagar", new[]
            {
                "RFC Emisor", "Proveedor", "Serie / Folio", "Fecha Emisión", "Días", "Total Factura", "Pagado", "Nota Crédito", "Saldo Pendiente", "Estado"
            }, (ws, startRow) =>
            {
                var lista = ReportesSql.ObtenerCuentasPorPagar(_conn, _rfc);
                int r = startRow;
                foreach (var item in lista)
                {
                    ws.Cell(r, 1).Value = item.RFCEmisor;
                    ws.Cell(r, 2).Value = item.NombreEmisor ?? "";
                    ws.Cell(r, 3).Value = (item.Serie ?? "") + " " + (item.Folio ?? "");
                    ws.Cell(r, 4).Value = item.FechaEmision.ToString("yyyy-MM-dd");
                    ws.Cell(r, 5).Value = item.DiasTranscurridos;
                    ws.Cell(r, 6).Value = item.Total;
                    ws.Cell(r, 6).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 7).Value = item.Pagado;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 8).Value = item.NotaCreditoTotal;
                    ws.Cell(r, 8).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 9).Value = item.Saldo;
                    ws.Cell(r, 9).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 9).Style.Font.Bold = true;
                    ws.Cell(r, 10).Value = item.NumComplementos == 0 ? "Sin complemento" : $"Parcial ({item.NumComplementos})";
                    r++;
                }
            });
        }

        private void BtnExportarCxC_Click(object sender, RoutedEventArgs e)
        {
            ExportarPestanaGenerica("Cuentas_Por_Cobrar", "Cuentas por Cobrar", new[]
            {
                "RFC Cliente", "Serie / Folio", "Fecha Emisión", "Días", "Total Factura", "Cobrado", "Nota Crédito", "Saldo por Cobrar", "Estado"
            }, (ws, startRow) =>
            {
                var lista = ReportesSql.ObtenerCuentasPorCobrar(_conn, _rfc);
                int r = startRow;
                foreach (var item in lista)
                {
                    ws.Cell(r, 1).Value = item.RFCReceptor;
                    ws.Cell(r, 2).Value = (item.Serie ?? "") + " " + (item.Folio ?? "");
                    ws.Cell(r, 3).Value = item.FechaEmision.ToString("yyyy-MM-dd");
                    ws.Cell(r, 4).Value = item.DiasTranscurridos;
                    ws.Cell(r, 5).Value = item.Total;
                    ws.Cell(r, 5).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 6).Value = item.Pagado;
                    ws.Cell(r, 6).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 7).Value = item.NotaCreditoTotal;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 8).Value = item.Saldo;
                    ws.Cell(r, 8).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 8).Style.Font.Bold = true;
                    ws.Cell(r, 9).Value = item.NumComplementos == 0 ? "Sin complemento" : $"Parcial ({item.NumComplementos})";
                    r++;
                }
            });
        }

        private void BtnExportarNC_Click(object sender, RoutedEventArgs e)
        {
            ExportarPestanaGenerica("Notas_De_Credito", "Notas de Crédito", new[]
            {
                "Fecha NC", "Serie/Folio NC", "Importe NC", "Factura Original", "Importe Original", "En Base de Datos"
            }, (ws, startRow) =>
            {
                var lista = ReportesSql.ObtenerNotasCreditoRelacionadas(_conn, _rfc);
                int r = startRow;
                foreach (var item in lista)
                {
                    ws.Cell(r, 1).Value = item.FechaNotaCredito.ToString("yyyy-MM-dd");
                    ws.Cell(r, 2).Value = (item.SerieNotaCredito ?? "") + " " + (item.FolioNotaCredito ?? "");
                    ws.Cell(r, 3).Value = item.TotalNotaCredito ?? 0m;
                    ws.Cell(r, 3).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 4).Value = item.OriginalEnBd ? ((item.SerieOriginal ?? "") + " " + (item.FolioOriginal ?? "")) : "(No descargada)";
                    ws.Cell(r, 5).Value = item.OriginalEnBd ? (item.TotalOriginal ?? 0m) : 0m;
                    ws.Cell(r, 5).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 6).Value = item.OriginalEnBd ? "Sí" : "No";
                    r++;
                }
            });
        }

        private void BtnExportarUsos_Click(object sender, RoutedEventArgs e)
        {
            if (!(CmbMes.SelectedItem is MesItem mes)) return;

            ExportarPestanaGenerica("Usos_De_CFDI", "Distribución Usos de CFDI", new[]
            {
                "Clave Uso", "Descripción SAT", "Comprobantes", "Importe Total"
            }, (ws, startRow) =>
            {
                var lista = ReportesSql.ObtenerUsosCfdiResumen(_conn, _rfc, mes.Anio, mes.Mes);
                int r = startRow;
                foreach (var item in lista)
                {
                    ws.Cell(r, 1).Value = item.UsoCFDI;
                    ws.Cell(r, 2).Value = DescripcionesUso.TryGetValue(item.UsoCFDI, out var d) ? d : "(Otro concepto)";
                    ws.Cell(r, 3).Value = item.Count;
                    ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(r, 4).Value = item.Total;
                    ws.Cell(r, 4).Style.NumberFormat.Format = "$#,##0.00";
                    r++;
                }
            });
        }

        private void BtnExportarRiesgoRep_Click(object sender, RoutedEventArgs e)
        {
            var facturas = ReportesSql.ObtenerFacturasRiesgoRep(_conn, _rfc, diasMinimos: 60);
            if (facturas.Count == 0)
            {
                MessageBox.Show(this, "No se detectaron facturas PPD en riesgo (más de 60 días sin REP).", "Auditoría Fiscal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ExportarPestanaGenerica("Riesgo_Acreditamiento_REP", "Facturas PPD en Riesgo (sin REP)", new[]
            {
                "Fecha Emisión", "RFC Proveedor", "Proveedor", "Serie / Folio", "Total Factura", "IVA en Riesgo", "Días Vencidos", "Nivel de Riesgo"
            }, (ws, startRow) =>
            {
                int r = startRow;
                foreach (var item in facturas)
                {
                    ws.Cell(r, 1).Value = item.FechaEmision.ToString("yyyy-MM-dd");
                    ws.Cell(r, 2).Value = item.RFCEmisor;
                    ws.Cell(r, 3).Value = item.NombreEmisor ?? "(Sin nombre)";
                    ws.Cell(r, 4).Value = item.SerieFolio;
                    ws.Cell(r, 5).Value = item.Total;
                    ws.Cell(r, 5).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 6).Value = item.IVA;
                    ws.Cell(r, 6).Style.NumberFormat.Format = "$#,##0.00";
                    ws.Cell(r, 6).Style.Font.Bold = true;
                    ws.Cell(r, 7).Value = item.DiasTranscurridos;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(r, 8).Value = item.Riesgo;
                    r++;
                }
            });
        }

        private void ExportarPestanaGenerica(
            string prefijoArchivo,
            string tituloPestana,
            string[] encabezados,
            Action<ClosedXML.Excel.IXLWorksheet, int> llenarFilas)
        {
            var dialogo = new SaveFileDialog
            {
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = $"{prefijoArchivo}_{_rfc}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (dialogo.ShowDialog(this) != true) return;

            try
            {
                string periodo = (CmbMes.SelectedItem as MesItem)?.Texto ?? DateTime.Now.ToString("MMMM yyyy");
                ExcelReporteEjecutivo.GenerarExportacionPestana(
                    dialogo.FileName,
                    tituloPestana,
                    _nombreEmpresa,
                    _rfc,
                    periodo,
                    encabezados,
                    llenarFilas);

                var r = MessageBox.Show(this,
                    $"Se creó el reporte de Excel:\n\n{dialogo.FileName}\n\n¿Deseas abrir el archivo?",
                    "Exportación completada",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialogo.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error al crear archivo de Excel:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
