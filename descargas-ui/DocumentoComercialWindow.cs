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
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    // «Ver documento»: vista previa de SOLO LECTURA de un documento de Comercial Pro, leida de su base de datos (funciona con Comercial cerrado).
    // Se abre con Show(), no ShowDialog(): no bloquea nada y se pueden tener varios a la vez. Para modificar el documento hay que abrirlo en Comercial.
    internal sealed class DocumentoComercialWindow : Window
    {
        public sealed class LineaDoc
        {
            public string Numero { get; set; }
            public string Clave { get; set; }
            public string Descripcion { get; set; }
            public string Cantidad { get; set; }
            public string Unidad { get; set; }
            public string Precio { get; set; }
            public string Impuesto { get; set; }
            public string Total { get; set; }
        }

        public DocumentoComercialWindow(string cadenaComercial, long documentId)
        {
            Title = "Documento de Comercial";
            Width = 1020; Height = 660; MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "PincelLienzo");
            var raiz = new Grid { Margin = new Thickness(24, 20, 24, 20) };
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = raiz;

            try
            {
                string titulo, linea1, linea2, comentarios = null; decimal sub = 0, imp = 0, ret = 0, desc = 0, total = 0, saldo = 0; string estado = "Vigente";
                var lineas = new List<LineaDoc>();
                using (var cn = new SqlConnection(cadenaComercial))
                {
                    cn.Open();
                    using (var cmd = new SqlCommand(@"
SELECT m.ModuleName, d.FolioPrefix, d.Folio, d.DateDocument, ISNULL(be.OfficialName, be.CommercialName), d.SubTotal, d.TotalTax, d.TotalRetention, d.TotalDiscount, d.Total, d.Balance,
       d.CancelledOn, d.CancellationReason, d.Comments, d.CreatedOn
FROM docDocument d LEFT JOIN engModule m ON m.ModuleID = d.ModuleID LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID WHERE d.DocumentID = @id", cn))
                    {
                        cmd.Parameters.AddWithValue("@id", documentId);
                        using (var l = cmd.ExecuteReader())
                        {
                            if (!l.Read()) throw new InvalidOperationException("El documento #" + documentId + " ya no existe en Comercial.");
                            string modulo = l.IsDBNull(0) ? "Documento" : Convert.ToString(l.GetValue(0));
                            titulo = modulo + " " + (l.IsDBNull(1) ? "" : Convert.ToString(l.GetValue(1))) + (l.IsDBNull(2) ? "" : Convert.ToString(l.GetValue(2)));
                            linea1 = (l.IsDBNull(4) ? "" : Convert.ToString(l.GetValue(4)));
                            linea2 = "Fecha " + (l.IsDBNull(3) ? "—" : Convert.ToDateTime(l.GetValue(3)).ToString("yyyy-MM-dd")) + "   ·   Documento #" + documentId;
                            sub = Dec(l, 5); imp = Dec(l, 6); ret = Dec(l, 7); desc = Dec(l, 8); total = Dec(l, 9); saldo = Dec(l, 10);
                            if (!l.IsDBNull(11)) estado = "Cancelado el " + Convert.ToDateTime(l.GetValue(11)).ToString("yyyy-MM-dd") + (l.IsDBNull(12) ? "" : " · " + Convert.ToString(l.GetValue(12)));
                            comentarios = l.IsDBNull(13) ? null : Convert.ToString(l.GetValue(13));
                        }
                    }
                    using (var cmd = new SqlCommand(@"
SELECT LineNumber, ProductKey, Description, Quantity, Unit, UnitPrice, TaxPerc, Total FROM docDocumentItem WHERE DocumentID = @id AND DeletedOn IS NULL ORDER BY LineNumber, DocumentItemID", cn))
                    {
                        cmd.Parameters.AddWithValue("@id", documentId);
                        using (var l = cmd.ExecuteReader())
                            while (l.Read())
                                lineas.Add(new LineaDoc
                                {
                                    Numero = l.IsDBNull(0) ? "" : Convert.ToString(l.GetValue(0)), Clave = l.IsDBNull(1) ? "" : Convert.ToString(l.GetValue(1)),
                                    Descripcion = l.IsDBNull(2) ? "" : Convert.ToString(l.GetValue(2)), Cantidad = N(l, 3), Unidad = l.IsDBNull(4) ? "" : Convert.ToString(l.GetValue(4)),
                                    Precio = N(l, 5), Impuesto = l.IsDBNull(6) ? "" : (Convert.ToDecimal(l.GetValue(6)) <= 1m ? Convert.ToDecimal(l.GetValue(6)) * 100m : Convert.ToDecimal(l.GetValue(6))).ToString("0.##", CultureInfo.InvariantCulture) + " %", Total = N(l, 7)
                                });
                    }
                }

                Title = titulo;
                var cab = new StackPanel();
                cab.Children.Add(new TextBlock { Text = titulo, FontSize = 22, FontWeight = FontWeights.Bold });
                cab.Children.Add(new TextBlock { Text = linea1, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                var sec = new TextBlock { Text = linea2 + "   ·   " + estado, Margin = new Thickness(0, 2, 0, 0) }; sec.SetResourceReference(TextBlock.ForegroundProperty, "PincelTexto2"); cab.Children.Add(sec);
                var aviso = new TextBlock { Text = "Vista previa de solo lectura. Para modificar el documento, ábrelo en Comercial.", Margin = new Thickness(0, 8, 0, 0), FontSize = 11.5 };
                aviso.SetResourceReference(TextBlock.ForegroundProperty, "PincelTexto3"); cab.Children.Add(aviso);
                Grid.SetRow(cab, 0); raiz.Children.Add(cab);

                var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, ItemsSource = lineas, Margin = new Thickness(0, 14, 0, 12), HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
                Col(grid, "#", "Numero", 40); Col(grid, "CLAVE", "Clave", 110); Col(grid, "DESCRIPCIÓN", "Descripcion", 0); Col(grid, "CANT.", "Cantidad", 80, true);
                Col(grid, "UNIDAD", "Unidad", 80); Col(grid, "PRECIO", "Precio", 100, true); Col(grid, "IMPUESTO", "Impuesto", 80, true); Col(grid, "TOTAL", "Total", 110, true);
                Grid.SetRow(grid, 1); raiz.Children.Add(grid);

                var pie = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                void Cifra(string r, decimal v, bool fuerte = false)
                {
                    var p = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
                    var rot = new TextBlock { Text = r, FontSize = 11 }; rot.SetResourceReference(TextBlock.ForegroundProperty, "PincelTexto3"); p.Children.Add(rot);
                    p.Children.Add(new TextBlock { Text = v.ToString("N2", CultureInfo.InvariantCulture), FontSize = fuerte ? 18 : 14, FontWeight = fuerte ? FontWeights.Bold : FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
                    pie.Children.Add(p);
                }
                Cifra("SUBTOTAL", sub); if (desc != 0) Cifra("DESCUENTO", desc); Cifra("IMPUESTOS", imp); if (ret != 0) Cifra("RETENCIONES", ret); Cifra("SALDO", saldo); Cifra("TOTAL", total, true);
                Grid.SetRow(pie, 2); raiz.Children.Add(pie);
            }
            catch (Exception ex)
            {
                var t = new TextBlock { Text = "No se pudo mostrar el documento:\n\n" + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
                Grid.SetRow(t, 1); raiz.Children.Add(t);
            }
        }

        private static decimal Dec(SqlDataReader l, int i) => l.IsDBNull(i) ? 0m : Convert.ToDecimal(l.GetValue(i));
        private static string N(SqlDataReader l, int i) => l.IsDBNull(i) ? "" : Convert.ToDecimal(l.GetValue(i)).ToString("N2", CultureInfo.InvariantCulture);

        private static void Col(DataGrid g, string cab, string prop, double ancho, bool derecha = false)
        {
            var c = new DataGridTextColumn { Header = cab, Binding = new System.Windows.Data.Binding(prop), Width = ancho <= 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(ancho) };
            if (derecha) { var st = new Style(typeof(TextBlock)); st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right)); c.ElementStyle = st; }
            g.Columns.Add(c);
        }
    }
}
