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

// BancoXlsx.cs -- lee un libro de Excel de un banco como tabla de texto (una lista de filas) para que BancoArchivo lo interprete igual que un CSV.
// Las fechas salen como yyyy-MM-dd y los numeros con punto decimal, sin importar el formato de celda.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;

namespace BrosLMV.DescargasUI
{
    internal static class BancoXlsx
    {
        public sealed class Hoja
        {
            public string Nombre;
            public List<string[]> Filas;
            public override string ToString() => Nombre + " (" + Filas.Count + " filas)";
        }

        public static List<Hoja> Leer(string ruta)
        {
            var hojas = new List<Hoja>();
            using (var wb = new XLWorkbook(ruta))
            {
                foreach (var ws in wb.Worksheets)
                {
                    var rango = ws.RangeUsed();
                    if (rango == null) continue;
                    int c1 = rango.FirstColumn().ColumnNumber(), c2 = rango.LastColumn().ColumnNumber();
                    int f1 = rango.FirstRow().RowNumber(), f2 = rango.LastRow().RowNumber();
                    var filas = new List<string[]>();
                    for (int f = f1; f <= f2; f++)
                    {
                        var fila = new string[c2 - c1 + 1];
                        for (int c = c1; c <= c2; c++) fila[c - c1] = Texto(ws.Cell(f, c));
                        filas.Add(fila);
                    }
                    if (filas.Any(fl => fl.Any(x => x.Length > 0))) hojas.Add(new Hoja { Nombre = ws.Name, Filas = filas });
                }
            }
            return hojas;
        }

        private static string Texto(IXLCell c)
        {
            try
            {
                if (c.IsEmpty()) return "";
                if (c.DataType == XLDataType.DateTime) return c.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (c.DataType == XLDataType.Number) return c.GetDouble().ToString("0.############", CultureInfo.InvariantCulture);
                return (c.GetString() ?? "").Trim();
            }
            catch { return (c.GetFormattedString() ?? "").Trim(); }
        }
    }
}
