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

// Indicadores.cs -- reglas que revisan los CFDI que ya estan en este equipo y senalan los que conviene mirar
// (emisor en lista 69-B, PUE con forma 99, PPD sin complemento de pago, pago mayor a la factura, ...).
// Cada indicador es una consulta SQL sobre la base local (BrosLMV_SAT) con los parametros @rfc, @desde y @hasta;
// todos devuelven las mismas columnas base para poder mostrarse en una sola tabla. Los personalizados son
// consultas del propio usuario, guardadas en la tabla IndicadorPersonalizado: solo lectura (se rechazan
// sentencias que escriban y se ejecutan dentro de una transaccion que siempre se revierte).
// Son senales para revisar, no dictamenes fiscales.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal enum NivelIndicador { Info = 0, Aviso = 1, Critico = 2 }

    internal sealed class IndicadorDef
    {
        public string Clave;
        public string Nombre;
        public string Descripcion;
        public NivelIndicador Nivel;
        public string Sql;
        public int PersonalizadoID;     // 0 = predefinido
        public bool EsPersonalizado => PersonalizadoID > 0;
    }

    internal static class Indicadores
    {
        // Filtro comun: comprobantes vigentes de la empresa dentro del rango [@desde, @hasta).
        private const string Base =
            "FROM CfdiRecibido c WHERE (c.RFCReceptor=@rfc OR c.RFCEmisor=@rfc) AND c.FechaEmision>=@desde AND c.FechaEmision<@hasta AND c.EstatusSat='Vigente'";

        private static string Consulta(string detalle, string condicion) =>
            "SELECT c.FechaEmision, CASE WHEN c.RFCReceptor=@rfc THEN 'Recibido' ELSE 'Emitido' END AS Tipo, c.RFCEmisor, c.NombreEmisor, " +
            "c.Serie, c.Folio, c.Total, c.Moneda, " + detalle + " AS Detalle, c.UUID " + Base + " AND " + condicion + " ORDER BY c.FechaEmision, c.UUID";

        public static readonly IReadOnlyList<IndicadorDef> Predefinidos = new List<IndicadorDef>
        {
            new IndicadorDef { Clave = "EFOS", Nombre = "Emisor en la lista 69-B (EFOS)", Nivel = NivelIndicador.Critico,
                Descripcion = "Comprobantes recibidos cuyo emisor figura, segun la ultima consulta al SAT, en la lista de operaciones inexistentes. Revisa antes de deducir.",
                Sql = Consulta("N'El emisor figura en la lista 69-B (codigo ' + c.ValidacionEFOS + N').'", "c.RFCReceptor=@rfc AND c.ValidacionEFOS IS NOT NULL AND c.ValidacionEFOS NOT IN ('200','201')") },
            new IndicadorDef { Clave = "PAGO_MAYOR", Nombre = "Pagos que superan al ingreso", Nivel = NivelIndicador.Critico,
                Descripcion = "Facturas cuyos complementos de pago suman mas que el total de la factura (misma moneda).",
                Sql = Consulta("N'Total de la factura ' + FORMAT(c.Total, 'N2') + N'; pagado segun complementos ' + FORMAT((SELECT SUM(p.ImpPagado) FROM CfdiPagoDocto p JOIN CfdiRecibido r ON r.CfdiID=p.CfdiID AND r.EstatusSat='Vigente' WHERE p.UUIDRelacionado=c.UUID AND p.Moneda=c.Moneda), 'N2')",
                    "c.TipoComprobante='I' AND (SELECT SUM(p.ImpPagado) FROM CfdiPagoDocto p JOIN CfdiRecibido r ON r.CfdiID=p.CfdiID AND r.EstatusSat='Vigente' WHERE p.UUIDRelacionado=c.UUID AND p.Moneda=c.Moneda) > c.Total + 0.01") },
            new IndicadorDef { Clave = "PUE_99", Nombre = "PUE con forma de pago 99", Nivel = NivelIndicador.Aviso,
                Descripcion = "Facturas en una sola exhibicion (PUE) con forma de pago «99 Por definir»: si ya se pagaron, deberian llevar la forma real.",
                Sql = Consulta("N'Metodo PUE con forma de pago 99.'", "c.TipoComprobante='I' AND c.MetodoPago='PUE' AND c.FormaPago='99'") },
            new IndicadorDef { Clave = "PPD_FORMA", Nombre = "PPD con forma de pago distinta de 99", Nivel = NivelIndicador.Aviso,
                Descripcion = "Facturas en parcialidades o diferido (PPD) que no traen «99 Por definir» como forma de pago.",
                Sql = Consulta("N'Metodo PPD con forma de pago ' + c.FormaPago + N'.'", "c.TipoComprobante='I' AND c.MetodoPago='PPD' AND c.FormaPago IS NOT NULL AND c.FormaPago<>'99'") },
            new IndicadorDef { Clave = "PPD_SIN_REP", Nombre = "PPD sin complemento de pago (más de 30 días)", Nivel = NivelIndicador.Aviso,
                Descripcion = "Facturas PPD de hace mas de 30 dias para las que no hay ningun complemento de pago en este equipo. Puede que aun no se descargue o que no se haya emitido.",
                Sql = Consulta("N'Sin complemento de pago; emitida hace ' + CONVERT(NVARCHAR(10), DATEDIFF(DAY, c.FechaEmision, GETDATE())) + N' dias.'",
                    "c.TipoComprobante='I' AND c.MetodoPago='PPD' AND c.FechaEmision < DATEADD(DAY,-30,GETDATE()) AND NOT EXISTS (SELECT 1 FROM CfdiPagoDocto p JOIN CfdiRecibido r ON r.CfdiID=p.CfdiID AND r.EstatusSat='Vigente' WHERE p.UUIDRelacionado=c.UUID)") },
            new IndicadorDef { Clave = "PARCIALIDAD_REPETIDA", Nombre = "Parcialidad repetida", Nivel = NivelIndicador.Aviso,
                Descripcion = "Facturas con el mismo numero de parcialidad en dos o mas complementos de pago distintos.",
                Sql = Consulta("N'Hay complementos de pago distintos con el mismo numero de parcialidad.'",
                    "EXISTS (SELECT 1 FROM CfdiPagoDocto p JOIN CfdiRecibido r ON r.CfdiID=p.CfdiID AND r.EstatusSat='Vigente' WHERE p.UUIDRelacionado=c.UUID AND p.NumParcialidad IS NOT NULL GROUP BY p.NumParcialidad HAVING COUNT(DISTINCT p.CfdiID) > 1)") },
            new IndicadorDef { Clave = "EFECTIVO_2000", Nombre = "Efectivo mayor a $2,000", Nivel = NivelIndicador.Aviso,
                Descripcion = "Compras con forma de pago «01 Efectivo» por mas de $2,000 MXN. Los pagos en efectivo de ese monto pueden no ser deducibles; confirma con tu contador.",
                Sql = Consulta("N'Forma de pago efectivo por ' + FORMAT(c.Total, 'N2') + N' MXN.'", "c.RFCReceptor=@rfc AND c.TipoComprobante='I' AND c.FormaPago='01' AND c.Moneda='MXN' AND c.Total > 2000") },
            new IndicadorDef { Clave = "CANCELACION_PROCESO", Nombre = "Cancelación en proceso", Nivel = NivelIndicador.Aviso,
                Descripcion = "Comprobantes vigentes cuya cancelacion esta en proceso segun la ultima consulta al SAT (esperan la aceptacion de la otra parte).",
                Sql = Consulta("c.EstatusCancelacion", "c.EstatusCancelacion LIKE N'%proceso%'") },
            new IndicadorDef { Clave = "POSIBLE_DUPLICADO", Nombre = "Posibles duplicados", Nivel = NivelIndicador.Aviso,
                Descripcion = "Recibidos con el mismo emisor, serie, folio y total que otro comprobante vigente pero distinto UUID.",
                Sql = Consulta("N'Mismo emisor, serie, folio y total que otro CFDI.'",
                    "c.RFCReceptor=@rfc AND c.Folio IS NOT NULL AND EXISTS (SELECT 1 FROM CfdiRecibido x WHERE x.UUID<>c.UUID AND x.EstatusSat='Vigente' AND x.RFCEmisor=c.RFCEmisor AND ISNULL(x.Serie,'')=ISNULL(c.Serie,'') AND x.Folio=c.Folio AND x.Total=c.Total)") },
            new IndicadorDef { Clave = "USO_S01", Nombre = "Uso «S01 Sin efectos fiscales»", Nivel = NivelIndicador.Info,
                Descripcion = "Compras facturadas con uso de CFDI S01: no son deducibles. Util para saber que no llevarlas a gasto.",
                Sql = Consulta("N'Uso de CFDI S01 (sin efectos fiscales).'", "c.RFCReceptor=@rfc AND c.TipoComprobante='I' AND c.UsoCFDI='S01'") },
            new IndicadorDef { Clave = "REP_SIN_FACTURA", Nombre = "Pago a una factura que no está en el equipo", Nivel = NivelIndicador.Info,
                Descripcion = "Complementos de pago que pagan facturas que no se han descargado (pueden ser de otro periodo o de otro RFC). No es un error por si solo.",
                Sql = Consulta("N'Paga ' + CONVERT(NVARCHAR(10), (SELECT COUNT(*) FROM CfdiPagoDocto p WHERE p.CfdiID=c.CfdiID AND NOT EXISTS (SELECT 1 FROM CfdiRecibido x WHERE x.UUID=p.UUIDRelacionado))) + N' factura(s) que no estan en este equipo.'",
                    "c.TipoComprobante='P' AND EXISTS (SELECT 1 FROM CfdiPagoDocto p WHERE p.CfdiID=c.CfdiID AND NOT EXISTS (SELECT 1 FROM CfdiRecibido x WHERE x.UUID=p.UUIDRelacionado))") },
        };

        // ---- Personalizados ----

        private static readonly Regex Prohibidas = new Regex(
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|SHUTDOWN|OPENROWSET|OPENQUERY|OPENDATASOURCE|BULK|INTO|WAITFOR|DBCC|USE|KILL|RECONFIGURE)\b|\bxp_|\bsp_",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Devuelve null si la consulta es aceptable, o el motivo del rechazo.
        public static string ValidarSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "Escribe la consulta.";
            string limpio = Regex.Replace(sql, @"--[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline);
            limpio = Regex.Replace(limpio, @"N?'(?:[^']|'')*'", "''");
            string t = limpio.Trim();
            if (!Regex.IsMatch(t, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase)) return "La consulta debe empezar con SELECT (o WITH).";
            if (t.TrimEnd(';').Contains(";")) return "Solo se permite una consulta, sin «;» intermedios.";
            var m = Prohibidas.Match(t);
            if (m.Success) return "No se permite «" + m.Value + "»: los indicadores solo leen datos.";
            return null;
        }

        public static List<IndicadorDef> Personalizados(SqlConnection conn)
        {
            var r = new List<IndicadorDef>();
            using (var cmd = new SqlCommand("SELECT IndicadorID, Nombre, Descripcion, Sql, Nivel FROM IndicadorPersonalizado ORDER BY Nombre", conn))
            using (var l = cmd.ExecuteReader())
                while (l.Read())
                    r.Add(new IndicadorDef
                    {
                        PersonalizadoID = l.GetInt32(0), Clave = "USR_" + l.GetInt32(0), Nombre = l.GetString(1), Descripcion = l.IsDBNull(2) ? "" : l.GetString(2),
                        Sql = l.GetString(3), Nivel = (NivelIndicador)l.GetInt32(4)
                    });
            return r;
        }

        public static int GuardarPersonalizado(SqlConnection conn, int id, string nombre, string descripcion, string sql, NivelIndicador nivel)
        {
            string motivo = ValidarSql(sql);
            if (motivo != null) throw new InvalidOperationException(motivo);
            if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("Ponle un nombre al indicador.");
            string q = id > 0
                ? "UPDATE IndicadorPersonalizado SET Nombre=@n, Descripcion=@d, Sql=@s, Nivel=@v WHERE IndicadorID=@id; SELECT @id;"
                : "INSERT INTO IndicadorPersonalizado (Nombre, Descripcion, Sql, Nivel) VALUES (@n,@d,@s,@v); SELECT CONVERT(INT, SCOPE_IDENTITY());";
            using (var cmd = new SqlCommand(q, conn))
            {
                cmd.Parameters.AddWithValue("@n", nombre.Trim());
                cmd.Parameters.AddWithValue("@d", (object)(descripcion ?? "").Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@s", sql);
                cmd.Parameters.AddWithValue("@v", (int)nivel);
                cmd.Parameters.AddWithValue("@id", id);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public static void EliminarPersonalizado(SqlConnection conn, int id)
        {
            using (var cmd = new SqlCommand("DELETE FROM IndicadorPersonalizado WHERE IndicadorID=@id", conn))
            { cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); }
        }

        // ---- Ejecucion ----

        // Ejecuta el indicador para una empresa y un rango [desde, hasta). Los personalizados corren dentro de una
        // transaccion que siempre se revierte, aunque la consulta hubiera logrado escribir.
        public static DataTable Ejecutar(SqlConnection conn, IndicadorDef d, string rfc, DateTime desde, DateTime hasta)
        {
            if (d.EsPersonalizado)
            {
                string motivo = ValidarSql(d.Sql);
                if (motivo != null) throw new InvalidOperationException(motivo);
            }
            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    using (var cmd = new SqlCommand(d.Sql, conn, tx) { CommandTimeout = 30 })
                    {
                        cmd.Parameters.AddWithValue("@rfc", rfc);
                        cmd.Parameters.AddWithValue("@desde", desde);
                        cmd.Parameters.AddWithValue("@hasta", hasta);
                        var tabla = new DataTable();
                        using (var l = cmd.ExecuteReader()) tabla.Load(l);
                        return tabla;
                    }
                }
                finally { try { tx.Rollback(); } catch { } }
            }
        }
    }
}
