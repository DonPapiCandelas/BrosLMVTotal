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

// ContaExcel.cs -- libro de Excel (sin macros) para asignar cuentas a terceros EN MASA: ida (exportar) y vuelta (leer). La hoja «Asignaciones» lleva
// listas desplegables de rol y moneda y una columna con el nombre de la cuenta calculada con formula contra la hoja «Catálogo».

using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Descargas.Datos;
using ClosedXML.Excel;

namespace BrosLMV.DescargasUI
{
    internal static class ContaExcel
    {
        private const string HojaAsig = "Asignaciones", HojaCat = "Catálogo", HojaListas = "Listas";
        private static readonly XLColor Cabecera = XLColor.FromHtml("#16263A"), Gris = XLColor.FromHtml("#94A3B8"), Entrada = XLColor.FromHtml("#FFF8E1");

        public static void Exportar(string ruta, string empresa, List<ContaTerceroFila> terceros, List<ContaCuenta> cuentas)
        {
            using (var wb = new XLWorkbook())
            {
                // --- Listas
                var li = wb.AddWorksheet(HojaListas);
                li.Cell(1, 1).Value = "Roles"; li.Cell(1, 2).Value = "Monedas"; li.Cell(1, 3).Value = "Acción";
                for (int i = 0; i < ContaRoles.Todos.Length; i++) li.Cell(2 + i, 1).Value = ContaRoles.Todos[i];
                for (int i = 0; i < ContaRoles.Monedas.Length; i++) li.Cell(2 + i, 2).Value = ContaRoles.Monedas[i];
                li.Cell(2, 3).Value = "BORRAR";
                li.Row(1).Style.Font.Bold = true; li.Columns().AdjustToContents();

                // --- Catalogo
                var ca = wb.AddWorksheet(HojaCat);
                ca.Cell(1, 1).Value = "Código"; ca.Cell(1, 2).Value = "Nombre"; ca.Cell(1, 3).Value = "Moneda";
                int rc = 2;
                foreach (var c in cuentas.Where(c => c.Afectable && !c.EsBaja)) { ca.Cell(rc, 1).SetValue(c.Codigo); ca.Cell(rc, 2).Value = c.Nombre; ca.Cell(rc, 3).Value = c.Moneda; rc++; }
                Encabezado(ca.Range(1, 1, 1, 3)); ca.Column(1).Width = 18; ca.Column(2).Width = 52; ca.Column(3).Width = 9; ca.SheetView.FreezeRows(1);

                // --- Asignaciones
                var ws = wb.AddWorksheet(HojaAsig);
                string[] cab = { "Clave", "Tercero", "RFC", "Tipo", "Rol", "Moneda", "Cuenta", "Nombre de la cuenta", "Origen", "Acción" };
                for (int c = 0; c < cab.Length; c++) ws.Cell(1, c + 1).Value = cab[c];
                Encabezado(ws.Range(1, 1, 1, cab.Length));
                int r = 2;
                foreach (var t in terceros.OrderBy(t => t.Nombre, StringComparer.CurrentCultureIgnoreCase))
                {
                    var asigs = t.Asignaciones.OrderBy(a => a.Rol).ThenBy(a => a.Moneda).ToList();
                    if (asigs.Count == 0) asigs.Add(new ContaAsignacion { Rol = t.EsCliente && !t.EsProveedor ? ContaRoles.Cliente : ContaRoles.Provision, Moneda = "MXN", Cuenta = "", Origen = "" });
                    foreach (var a in asigs)
                    {
                        ws.Cell(r, 1).Value = t.Clave; ws.Cell(r, 2).Value = t.Nombre; ws.Cell(r, 3).Value = t.Rfc; ws.Cell(r, 4).Value = t.Tipo;
                        ws.Cell(r, 5).Value = a.Rol; ws.Cell(r, 6).Value = a.Moneda; ws.Cell(r, 7).SetValue(a.Cuenta);
                        ws.Cell(r, 8).FormulaA1 = "IF(G" + r + "=\"\",\"\",IFERROR(VLOOKUP(G" + r + ",'" + HojaCat + "'!A:B,2,FALSE),\"(no está en el catálogo)\"))";
                        ws.Cell(r, 9).Value = a.Origen;
                        r++;
                    }
                }
                int ultima = Math.Max(r - 1, 2);
                ws.Range(2, 5, ultima, 7).Style.Fill.BackgroundColor = Entrada;      // lo que se edita
                ws.Range(2, 10, ultima, 10).Style.Fill.BackgroundColor = Entrada;
                ws.Range(2, 1, ultima, 1).Style.Font.FontColor = Gris; ws.Range(2, 9, ultima, 9).Style.Font.FontColor = Gris;
                ws.Range(2, 7, ultima, 7).Style.NumberFormat.Format = "@";
                var vr = ws.Range(2, 5, ultima, 5).CreateDataValidation(); vr.List("='" + HojaListas + "'!$A$2:$A$" + (1 + ContaRoles.Todos.Length));
                var vm = ws.Range(2, 6, ultima, 6).CreateDataValidation(); vm.List("='" + HojaListas + "'!$B$2:$B$" + (1 + ContaRoles.Monedas.Length));
                var va = ws.Range(2, 10, ultima, 10).CreateDataValidation(); va.List("='" + HojaListas + "'!$C$2:$C$2");
                ws.Column(1).Width = 14; ws.Column(2).Width = 44; ws.Column(3).Width = 16; ws.Column(4).Width = 18; ws.Column(5).Width = 24; ws.Column(6).Width = 9; ws.Column(7).Width = 16; ws.Column(8).Width = 44; ws.Column(9).Width = 13; ws.Column(10).Width = 10;
                ws.SheetView.FreezeRows(1); ws.Range(1, 1, ultima, cab.Length).SetAutoFilter();

                // --- Instrucciones
                var ins = wb.AddWorksheet("Cómo usar", 1);
                ins.Cell(1, 1).Value = "Terceros y cuentas — " + empresa; ins.Cell(1, 1).Style.Font.Bold = true; ins.Cell(1, 1).Style.Font.FontSize = 14;
                string[] txt = {
                    "1. En la hoja «Asignaciones» cada fila es UNA cuenta de un tercero: rol (provisión, gasto, anticipo, cliente…), moneda y cuenta.",
                    "2. Cambia los campos amarillos: Rol y Moneda (listas), Cuenta (código de Contabilidad; el nombre se calcula solo) y Acción.",
                    "3. Para dar más de una cuenta a un tercero (por ejemplo, una en dólares), copia su fila y cambia moneda y cuenta.",
                    "4. Para quitar una asignación, escribe BORRAR en Acción. Las filas sin cuenta se ignoran.",
                    "5. NO cambies la columna «Clave»: identifica al tercero. Guarda el libro y usa «Importar de Excel» en BrosLMV: se valida antes de aplicar.",
                    "6. La cuenta debe existir en el catálogo (hoja «Catálogo»), ser afectable y de la misma moneda; si no, se avisa o se rechaza la fila." };
                for (int i = 0; i < txt.Length; i++) ins.Cell(3 + i, 1).Value = txt[i];
                ins.Column(1).Width = 140;
                wb.Worksheet(HojaAsig).SetTabActive();
                wb.SaveAs(ruta);
            }
        }

        private static void Encabezado(IXLRange r) { r.Style.Font.Bold = true; r.Style.Font.FontColor = XLColor.White; r.Style.Fill.BackgroundColor = Cabecera; }

        // Lee la hoja «Asignaciones». Filas sin cuenta y sin BORRAR se ignoran.
        public static List<ContaImportFila> Leer(string ruta)
        {
            var res = new List<ContaImportFila>();
            using (var wb = new XLWorkbook(ruta))
            {
                if (!wb.TryGetWorksheet(HojaAsig, out var ws)) throw new InvalidOperationException("El libro no tiene la hoja «" + HojaAsig + "»; usa uno exportado desde BrosLMV.");
                int ultima = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = 2; r <= ultima; r++)
                {
                    string clave = Texto(ws.Cell(r, 1)), rol = Texto(ws.Cell(r, 5)), mon = Texto(ws.Cell(r, 6)), cuenta = Texto(ws.Cell(r, 7)), accion = Texto(ws.Cell(r, 10));
                    bool borrar = string.Equals(accion, "BORRAR", StringComparison.OrdinalIgnoreCase);
                    if (clave.Length == 0 || (cuenta.Length == 0 && !borrar)) continue;
                    res.Add(new ContaImportFila { Fila = r, Clave = clave, Rol = rol, Moneda = mon.Length == 0 ? "MXN" : mon, Cuenta = cuenta, Borrar = borrar });
                }
            }
            return res;
        }

        private static string Texto(IXLCell c)
        {
            if (c.IsEmpty()) return "";
            if (c.DataType == XLDataType.Number) return c.GetDouble().ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            return (c.GetString() ?? "").Trim();
        }
    }
}
