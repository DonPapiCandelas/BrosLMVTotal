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
using BrosLMV.Descargas.Datos;
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

        // La empresa de Premium como «empresa de ADD» (mismo tipo que Contabilidad): GuidDSL/GuidEmpresa de admParametros y la carpeta de sus archivos.
        public static EmpresaContabilidadInfo BuscarEmpresaAdd(SqlConnection conn, string baseDatos)
        {
            if (!Regex.IsMatch(baseDatos ?? "", "^[A-Za-z0-9_]+$")) return null;
            var e = new EmpresaContabilidadInfo { BaseDatos = baseDatos };
            using (var cmd = new SqlCommand("SELECT TOP 1 ISNULL(CNOMBREEMPRESA,''), ISNULL(CRFCEMPRESA,''), CONVERT(varchar(36), CGUIDDSL), CONVERT(varchar(36), CGUIDEMPRESA) FROM [" + baseDatos + "].dbo.admParametros", conn))
            using (var l = cmd.ExecuteReader())
            {
                if (!l.Read()) return null;
                e.Nombre = l.GetString(0).Trim(); e.Rfc = l.GetString(1).Trim(); e.GuidDsl = l.GetString(2); e.GuidEmpresa = l.GetString(3);
            }
            using (var cmd = new SqlCommand("SELECT TOP 1 physical_name FROM sys.master_files WHERE database_id = DB_ID(@b) AND type = 0", conn))
            {
                cmd.Parameters.AddWithValue("@b", baseDatos);
                var f = cmd.ExecuteScalar() as string;
                e.Ruta = string.IsNullOrEmpty(f) ? "" : System.IO.Path.GetDirectoryName(f).TrimEnd('\\') + "\\";
            }
            return e;
        }

        public static string DescifrarClave(PremiumConfig cfg) =>
            cfg.ClaveCifrada == null || cfg.ClaveCifrada.Length == 0 ? "" : DpapiHelper.Descifrar(cfg.ClaveCifrada);

        public static async System.Threading.Tasks.Task<string> ProbarAccesoAsync(SqlConnection conn, PremiumConfig cfg)
        {
            var emp = BuscarEmpresaAdd(conn, cfg.BaseDatos);
            if (emp == null) return "No se encontró la empresa de Premium «" + cfg.BaseDatos + "» en este servidor.";
            return await ContabilidadAdd.ProbarAccesoEnAsync(emp, cfg.Usuario, DescifrarClave(cfg)).ConfigureAwait(false);
        }

        // Carga al ADD de Premium lo descargado que aun no este alli (idempotente, igual que Contabilidad).
        public static async System.Threading.Tasks.Task<ResultadoContabilidad> CargarAsync(SqlConnection conn, string rfc, PremiumConfig cfg, Action<string> progreso = null, int maximo = 0, ICollection<Guid> soloUuids = null)
        {
            if (string.IsNullOrWhiteSpace(cfg.Usuario)) return new ResultadoContabilidad { Error = "Falta el usuario de Premium para cargar al ADD." };
            var emp = BuscarEmpresaAdd(conn, cfg.BaseDatos);
            if (emp == null) return new ResultadoContabilidad { Error = "No se encontró la empresa de Premium «" + cfg.BaseDatos + "»." };
            return await ContabilidadAdd.CargarEnAsync(conn, rfc, emp, cfg.Usuario, DescifrarClave(cfg), progreso, maximo, soloUuids, false).ConfigureAwait(false);
        }

        public static async System.Threading.Tasks.Task CargarAutomaticoAsync(SqlConnection conn, string rfc, string nombre)
        {
            try
            {
                var cfg = BrosSatDb.ObtenerPremium(conn, rfc);
                if (cfg == null || !cfg.CargaAutomatica || string.IsNullOrWhiteSpace(cfg.Usuario)) return;
                var r = await CargarAsync(conn, rfc, cfg, null).ConfigureAwait(false);
                if (!r.Exito) { Bitacora.EscribirError("  " + nombre + ": no se pudo cargar a Premium: " + r.Error); return; }
                if (r.Cargados > 0 || r.NoSeCargaron > 0)
                    Bitacora.Escribir("  " + nombre + ": " + r.Cargados + " XML cargados al ADD de Premium, " + r.YaEstaban + " ya estaban" + (r.NoSeCargaron > 0 ? ", " + r.NoSeCargaron + " rechazados por CONTPAQi." : "."));
            }
            catch (Exception ex) { Bitacora.EscribirError("  " + nombre + ": error al cargar a Premium: " + ex.Message); }
        }

        // Estado en admFoliosDigitales.CESTADO: 2 timbrado, 3 cancelado, 1 pendiente; recibidos: 11 cargado al ADD sin documento, 13 asociado a un documento.
        public static string EstadoTexto(int estado, bool tieneUuid)
        {
            if (!tieneUuid) return "Pendiente";
            switch (estado) { case 2: return "Timbrado"; case 3: return "Cancelado"; case 1: return "Pendiente"; case 11: return "En el ADD (sin documento)"; case 13: return "Con documento"; default: return "Estado " + estado; }
        }
    }
}
