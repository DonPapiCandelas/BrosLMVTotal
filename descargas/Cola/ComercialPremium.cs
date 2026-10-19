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

// ComercialPremium.cs -- empresas de CONTPAQi Comercial Premium de este servidor SQL (bases adXXX). Solo lectura.
// Premium registra en admFoliosDigitales los CFDI que TIMBRA (emitidos) y los que carga a su ADD (recibidos); ver Entrenamiento\comercial_premium\ANALISIS_CFDI_PREMIUM.md.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal sealed class EmpresaPremiumInfo
    {
        public string Nombre;
        public string BaseDatos;   // adXXX
        public string Rfc;
        public override string ToString() => Nombre + "  (" + Rfc + ")  [" + BaseDatos + "]";
    }

    internal static class ComercialPremium
    {
        // Bases «ad...» con la tabla admParametros: cada una es una empresa de Premium.
        public static List<EmpresaPremiumInfo> ListarEmpresas(SqlConnection conn)
        {
            var nombres = new List<string>();
            using (var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE 'ad%' AND state = 0 ORDER BY name", conn))
            using (var l = cmd.ExecuteReader()) while (l.Read()) nombres.Add(l.GetString(0));

            var lista = new List<EmpresaPremiumInfo>();
            foreach (var bd in nombres)
            {
                if (!Regex.IsMatch(bd, "^[A-Za-z0-9_]+$")) continue;
                try
                {
                    using (var cmd = new SqlCommand("SELECT TOP 1 ISNULL(CNOMBREEMPRESA, ''), ISNULL(CRFCEMPRESA, '') FROM [" + bd + "].dbo.admParametros", conn))
                    using (var l = cmd.ExecuteReader())
                        if (l.Read()) lista.Add(new EmpresaPremiumInfo { BaseDatos = bd, Nombre = l.GetString(0).Trim(), Rfc = l.GetString(1).Trim() });
                }
                catch { /* no es una empresa de Premium o no se puede leer */ }
            }
            return lista;
        }

        // Estado en admFoliosDigitales.CESTADO: 2 timbrado, 3 cancelado, 1 pendiente; recibidos: 11 cargado al ADD sin documento, 13 asociado a un documento.
        public static string EstadoTexto(int estado, bool tieneUuid)
        {
            if (!tieneUuid) return "Pendiente";
            switch (estado) { case 2: return "Timbrado"; case 3: return "Cancelado"; case 1: return "Pendiente"; case 11: return "En el ADD (sin documento)"; case 13: return "Con documento"; default: return "Estado " + estado; }
        }
    }
}
