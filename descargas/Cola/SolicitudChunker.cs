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

// SolicitudChunker.cs -- parte un rango de fechas grande en tramos de mes de calendario antes de
// mandarlo como SolicitaDescarga. Compartido entre la UI (SolicitarWindow) y el CLI
// (--auto-solicitar-todas) para que ambos partan los rangos exactamente igual.

using System;
using System.Collections.Generic;

namespace BrosLMV.Descargas.Cola
{
    internal static class SolicitudChunker
    {
        // Un rango grande en UNA sola SolicitaDescarga arriesga un paquete demasiado grande --
        // visto en vivo con un rango de 7+ meses: 1332 CFDIs, agoto sus 2 descargas con contenido
        // vacio (CodEstatus=5008, ver bug #16 en DOCUMENTACION.md). Partir en meses de calendario
        // es la practica de mercado (confirmado por el usuario: "probe con una app del mercado y
        // ahi si puedo descargar de todo el ano" -- ese es el truco, no un limite mas alto del SAT).
        //
        // Parte [desde, hasta] en tramos alineados a mes de calendario (el primer y ultimo tramo
        // pueden ser parciales si desde/hasta no caen en dia 1 / fin de mes). Cada tramo regresa
        // ya con horas 00:00:00 / 23:59:59, listo para pasarlo directo a SolicitarDescargaAsync.
        public static List<(DateTime Desde, DateTime Hasta)> PartirEnMeses(DateTime desde, DateTime hasta)
        {
            var chunks = new List<(DateTime, DateTime)>();
            DateTime inicioActual = desde.Date;
            DateTime hastaDate = hasta.Date;
            while (inicioActual <= hastaDate)
            {
                DateTime finMes = new DateTime(inicioActual.Year, inicioActual.Month, 1).AddMonths(1).AddDays(-1);
                DateTime finTramo = finMes < hastaDate ? finMes : hastaDate;
                chunks.Add((inicioActual, finTramo.AddHours(23).AddMinutes(59).AddSeconds(59)));
                inicioActual = finTramo.AddDays(1);
            }
            return chunks;
        }
    }
}
