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

// Resumen.cs -- lo que muestra el Panel de una empresa y las insignias del menu lateral: salud, indicadores con casos, totales del mes
// y cobertura del ano. Solo lee la base local; no consulta al SAT.

using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    internal sealed class ResumenEmpresa
    {
        public DateTime Calculado;
        public List<HallazgoSalud> Salud = new List<HallazgoSalud>();
        public NivelSalud PeorSalud => BrosLMV.Descargas.Datos.Salud.Peor(Salud);
        public List<(IndicadorDef Def, int Casos)> Indicadores = new List<(IndicadorDef, int)>();

        public int RecibidosMes, EmitidosMes, RecibidosMesAnterior, EmitidosMesAnterior;
        public decimal TotalRecibidosMes, TotalEmitidosMes;
        public double CoberturaAnio;            // 0..1, promedio de Recibidos y Emitidos
        public int DiasSinCubrir;
        public DateTime? UltimoXml;

        public int CasosCriticos => Indicadores.Where(i => i.Def.Nivel == NivelIndicador.Critico).Sum(i => i.Casos);
        public int CasosAviso => Indicadores.Where(i => i.Def.Nivel == NivelIndicador.Aviso).Sum(i => i.Casos);
        public int CasosInfo => Indicadores.Where(i => i.Def.Nivel == NivelIndicador.Info).Sum(i => i.Casos);

        public static ResumenEmpresa Calcular(SqlConnection conn, EmpresaFila e)
        {
            var r = new ResumenEmpresa { Calculado = DateTime.Now };
            try { r.Salud = BrosLMV.Descargas.Datos.Salud.Evaluar(conn, e); } catch { }

            var hoy = DateTime.Today;
            var anioIni = new DateTime(hoy.Year, 1, 1);
            foreach (var d in BrosLMV.Descargas.Datos.Indicadores.Predefinidos)
            {
                try { r.Indicadores.Add((d, BrosLMV.Descargas.Datos.Indicadores.Ejecutar(conn, d, e.RFC, anioIni, hoy.AddDays(1)).Rows.Count)); }
                catch { r.Indicadores.Add((d, 0)); }
            }

            var mes = new DateTime(hoy.Year, hoy.Month, 1);
            var mesAnt = mes.AddMonths(-1);
            (r.RecibidosMes, r.TotalRecibidosMes) = Facturas(conn, e.RFC, true, mes, mes.AddMonths(1));
            (r.EmitidosMes, r.TotalEmitidosMes) = Facturas(conn, e.RFC, false, mes, mes.AddMonths(1));
            r.RecibidosMesAnterior = Facturas(conn, e.RFC, true, mesAnt, mes).n;
            r.EmitidosMesAnterior = Facturas(conn, e.RFC, false, mesAnt, mes).n;

            // Cobertura del ano en curso: dias que alguna solicitud buena cubre sobre los dias transcurridos.
            var hasta = DateTime.Now.Hour >= 15 ? hoy : hoy.AddDays(-1);
            double suma = 0; int dias = 0; int sinCubrir = 0;
            if (hasta >= anioIni)
            {
                dias = (int)(hasta - anioIni).TotalDays + 1;
                foreach (var tipo in new[] { "Recibidos", "Emitidos" })
                {
                    var huecos = Huecos.Todos(BrosSatDb.ObtenerRangosCubiertos(conn, e.RFC, "CFDI", tipo), anioIni, hasta);
                    int n = huecos.Sum(h => (int)(h.Hasta.Date - h.Desde.Date).TotalDays + 1);
                    suma += 1.0 - Math.Min(1.0, (double)n / dias); sinCubrir = Math.Max(sinCubrir, n);
                }
                r.CoberturaAnio = suma / 2.0;
            }
            else r.CoberturaAnio = 1.0;
            r.DiasSinCubrir = sinCubrir;

            try { r.UltimoXml = BrosSatDb.ObtenerCoberturaDescargas(conn, e.RFC).UltimoXml; } catch { }
            return r;
        }

        private static (int n, decimal total) Facturas(SqlConnection conn, string rfc, bool recibidas, DateTime desde, DateTime hasta)
        {
            string col = recibidas ? "RFCReceptor" : "RFCEmisor";
            using (var cmd = new SqlCommand("SELECT COUNT(*), ISNULL(SUM(Total),0) FROM CfdiRecibido WHERE " + col + "=@r AND TipoComprobante='I' AND EstatusSat='Vigente' AND FechaEmision>=@d AND FechaEmision<@h", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@d", desde); cmd.Parameters.AddWithValue("@h", hasta);
                using (var l = cmd.ExecuteReader()) { l.Read(); return (l.GetInt32(0), l.GetDecimal(1)); }
            }
        }
    }
}
