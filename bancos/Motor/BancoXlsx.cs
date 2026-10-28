// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace BrosLMV.Bancos.Motor
{
    public static class BancoXlsx
    {
    
        public static List<HojaArchivo> Leer(string ruta)
        {
            var hojas = new List<HojaArchivo>();
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
                    if (filas.Any(fl => fl.Any(x => x.Length > 0))) hojas.Add(new HojaArchivo { Nombre = ws.Name, Filas = filas });
                }
            }
            return hojas;
        }

        private static string Texto(IXLCell c)
        {
            try
            {
                if (c.IsEmpty()) return "";
                if (c.DataType == XLDataType.DateTime) { var d = c.GetDateTime(); return d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
                if (c.DataType == XLDataType.Number) return c.GetDouble().ToString("0.############", CultureInfo.InvariantCulture);
                return (c.GetString() ?? "").Trim();
            }
            catch { return (c.GetFormattedString() ?? "").Trim(); }
        }
    }
}
