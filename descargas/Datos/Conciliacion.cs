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

// Conciliacion.cs -- "el SAT reporta N, tienes N". Por mes y direccion (Recibidos/Emitidos) compara
// lo que el SAT informa en Metadata contra los XML descargados. Es el numero que da tranquilidad:
// Faltan = 0 significa que no hay ningun CFDI vigente del SAT sin su XML. Solo datos locales.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class ConciliacionFila
    {
        public int Anio, Mes;
        public string Tipo;            // Recibidos / Emitidos
        public int SatTotal;           // Metadata: todos los CFDI que el SAT reporta ese mes
        public int SatVigentes;
        public int SatCancelados;
        public int Descargados;        // XML en la base
        public int Faltan;             // vigentes del SAT sin XML
        public int SoloLocal;          // XML que Metadata no reporto (informativo)
        public bool HayMetadata => SatTotal > 0;

        public string MesTexto => new DateTime(Anio, Mes, 1).ToString("yyyy-MM");

        public string Estado =>
            !HayMetadata ? (Descargados == 0 ? "Sin datos" : "Sin Metadata del SAT (no verificable)") :
            Faltan == 0 ? "Conciliado" : "Faltan " + Faltan;
    }

    internal static class Conciliacion
    {
        public static List<ConciliacionFila> Calcular(SqlConnection conn, string rfc)
        {
            var filas = new Dictionary<(int, int, string), ConciliacionFila>();
            ConciliacionFila Fila(int a, int m, string tipo)
            {
                if (!filas.TryGetValue((a, m, tipo), out var f)) filas[(a, m, tipo)] = f = new ConciliacionFila { Anio = a, Mes = m, Tipo = tipo };
                return f;
            }

            foreach (var tipo in new[] { "Recibidos", "Emitidos" })
            {
                string cond = tipo == "Recibidos" ? "RFCReceptor = @r" : "RFCEmisor = @r";

                using (var cmd = new SqlCommand(@"
SELECT YEAR(FechaEmision), MONTH(FechaEmision), COUNT(*),
       SUM(CASE WHEN EstatusSat='Vigente' THEN 1 ELSE 0 END), SUM(CASE WHEN EstatusSat='Cancelado' THEN 1 ELSE 0 END)
FROM CfdiMetadata WHERE Tipo=@t AND " + cond + @" AND FechaEmision IS NOT NULL
GROUP BY YEAR(FechaEmision), MONTH(FechaEmision)", conn))
                {
                    cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@t", tipo);
                    using (var l = cmd.ExecuteReader())
                        while (l.Read())
                        {
                            var f = Fila(l.GetInt32(0), l.GetInt32(1), tipo);
                            f.SatTotal = l.GetInt32(2); f.SatVigentes = l.GetInt32(3); f.SatCancelados = l.GetInt32(4);
                        }
                }

                using (var cmd = new SqlCommand(@"
SELECT YEAR(c.FechaEmision), MONTH(c.FechaEmision), COUNT(*),
       SUM(CASE WHEN m.UUID IS NULL THEN 1 ELSE 0 END)
FROM CfdiRecibido c LEFT JOIN CfdiMetadata m ON m.UUID = c.UUID
WHERE c." + cond + @"
GROUP BY YEAR(c.FechaEmision), MONTH(c.FechaEmision)", conn))
                {
                    cmd.Parameters.AddWithValue("@r", rfc);
                    using (var l = cmd.ExecuteReader())
                        while (l.Read())
                        {
                            var f = Fila(l.GetInt32(0), l.GetInt32(1), tipo);
                            f.Descargados = l.GetInt32(2); f.SoloLocal = l.GetInt32(3);
                        }
                }

                foreach (var (anio, mes, faltan) in BrosSatDb.ObtenerMesesConFaltantes(conn, rfc, tipo))
                    Fila(anio, mes, tipo).Faltan = faltan;
            }

            return filas.Values.OrderByDescending(f => f.Anio).ThenByDescending(f => f.Mes).ThenBy(f => f.Tipo).ToList();
        }

        // CSV con encabezado, separado por comas y entre comillas (se abre bien en Excel).
        public static string ACsv(IEnumerable<ConciliacionFila> filas)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mes,Tipo,SAT reporta,SAT vigentes,SAT cancelados,XML descargados,Faltan,Solo local,Estado");
            foreach (var f in filas)
                sb.AppendLine(string.Join(",", new[] { f.MesTexto, f.Tipo, f.SatTotal.ToString(), f.SatVigentes.ToString(), f.SatCancelados.ToString(),
                    f.Descargados.ToString(), f.Faltan.ToString(), f.SoloLocal.ToString(), "\"" + f.Estado + "\"" }));
            return sb.ToString();
        }
    }
}
