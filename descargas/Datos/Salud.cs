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

// Salud.cs -- "¿Esta todo bien?" por empresa, calculado SOLO con lo que ya hay en la base local
// (nada sale de este equipo). Cada comprobacion devuelve un hallazgo con semaforo; el peor
// hallazgo da el color de la empresa. Lo usan la ventana Salud, el aviso de la bandeja y el
// servicio (que anota los criticos en la bitacora y en el Registro de eventos de Windows).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Descargas.Cola;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal enum NivelSalud { Ok = 0, Aviso = 1, Critico = 2 }

    internal sealed class HallazgoSalud
    {
        public NivelSalud Nivel;
        public string Tema;
        public string Detalle;
    }

    internal static class Salud
    {
        // Claves de ServicioEstado que escribe el servicio.
        public const string ClaveLatido = "Latido";
        public const string ClavePasadaDescargas = "PasadaDescargas";
        public const string ClavePasadaSolicitudes = "PasadaSolicitudes";
        public const string ClavePasadaEstatus = "PasadaEstatus";

        public static NivelSalud Peor(IEnumerable<HallazgoSalud> hallazgos) =>
            hallazgos.Any() ? hallazgos.Max(h => h.Nivel) : NivelSalud.Ok;

        public static List<HallazgoSalud> Evaluar(SqlConnection conn, EmpresaFila empresa)
        {
            var r = new List<HallazgoSalud>();
            void Agregar(NivelSalud n, string tema, string detalle) => r.Add(new HallazgoSalud { Nivel = n, Tema = tema, Detalle = detalle });

            // 1) El servicio esta vivo
            var latido = BrosSatDb.LeerEstado(conn, ClaveLatido);
            if (!latido.HasValue)
                Agregar(NivelSalud.Aviso, "Servicio", "El servicio todavia no ha reportado actividad (no esta instalado, esta detenido o es una version anterior a la 2.3.0).");
            else
            {
                var edad = DateTime.UtcNow - latido.Value;
                if (edad.TotalMinutes <= 5) Agregar(NivelSalud.Ok, "Servicio", "Activo (ultimo latido hace " + Formato(edad) + ").");
                else if (edad.TotalMinutes <= 30) Agregar(NivelSalud.Aviso, "Servicio", "Sin latido desde hace " + Formato(edad) + ".");
                else Agregar(NivelSalud.Critico, "Servicio", "Sin latido desde hace " + Formato(edad) + ": el servicio esta detenido o atorado. No se esta descargando nada.");

                if (edad.TotalMinutes <= 30) Pasada(r, conn, ClavePasadaDescargas, "Revision de solicitudes y descargas", 3, 12);
            }

            // 2) Ultima descarga de XML (informativo)
            using (var cmd = new SqlCommand("SELECT MAX(FechaDescarga), COUNT(*) FROM CfdiRecibido WHERE RFCReceptor=@r OR RFCEmisor=@r", conn))
            {
                cmd.Parameters.AddWithValue("@r", empresa.RFC);
                using (var lector = cmd.ExecuteReader())
                {
                    lector.Read();
                    int total = lector.GetInt32(1);
                    if (lector.IsDBNull(0)) Agregar(NivelSalud.Aviso, "XML descargados", "Todavia no se ha descargado ningun XML de esta empresa.");
                    else
                    {
                        var ultima = DateTime.SpecifyKind(lector.GetDateTime(0), DateTimeKind.Utc);
                        Agregar(NivelSalud.Ok, "XML descargados", total.ToString("N0") + " en total; el ultimo llego el " + ultima.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + ".");
                    }
                }
            }

            // 3) Huecos: dias que ninguna solicitud buena cubre
            var hasta = DateTime.Now.Hour >= 15 ? DateTime.Today : DateTime.Today.AddDays(-1);
            DateTime inicio = empresa.AnioInicioDescargas.HasValue ? new DateTime(empresa.AnioInicioDescargas.Value, 1, 1) : DateTime.Today.AddDays(-90);
            foreach (var tipo in new[] { "Recibidos", "Emitidos" })
            {
                var cubiertos = BrosSatDb.ObtenerRangosCubiertos(conn, empresa.RFC, "CFDI", tipo);
                var huecos = Huecos.Todos(cubiertos, inicio, hasta);
                int dias = huecos.Sum(h => (int)(h.Hasta.Date - h.Desde.Date).TotalDays + 1);
                if (dias == 0) { Agregar(NivelSalud.Ok, "Cobertura " + tipo, "Sin huecos desde " + inicio.ToString("dd/MM/yyyy") + "."); continue; }
                var masViejo = huecos.Min(h => h.Desde.Date);
                int antiguedad = (int)(DateTime.Today - masViejo).TotalDays;
                var nivel = antiguedad > 14 || dias > 45 ? NivelSalud.Critico : NivelSalud.Aviso;
                Agregar(nivel, "Cobertura " + tipo, dias + " dia(s) sin cubrir, el mas antiguo desde " + masViejo.ToString("dd/MM/yyyy") + ". Se piden solos en la siguiente pasada del servicio.");
            }

            // 4) Solicitudes atoradas y paquetes perdidos
            var atoradas = Contar(conn, "SELECT COUNT(*) FROM SolicitudDescarga WHERE RfcSolicitante=@r AND Estatus IN ('Aceptada','EnProceso') AND FechaSolicitud < DATEADD(HOUR,-24,SYSUTCDATETIME())", empresa.RFC);
            if (atoradas > 0) Agregar(NivelSalud.Aviso, "Solicitudes", atoradas + " solicitud(es) aceptada(s) por el SAT con mas de 24 h sin resultado (a los 3 dias se descartan y se vuelven a pedir).");
            var perdidos = Contar(conn, @"SELECT COUNT(*) FROM SolicitudDescargaPaquete p JOIN SolicitudDescarga s ON s.SolicitudDescargaID=p.SolicitudDescargaID
                WHERE s.RfcSolicitante=@r AND p.VecesDescargado>=2 AND p.UltimoError IS NOT NULL", empresa.RFC);
            if (perdidos > 0) Agregar(NivelSalud.Aviso, "Paquetes", perdidos + " paquete(s) del SAT no se pudieron descargar (agotaron sus 2 intentos); el rango se vuelve a solicitar.");
            var rechazos = Contar(conn, "SELECT COUNT(*) FROM SolicitudDescarga WHERE RfcSolicitante=@r AND Estatus='Rechazada' AND FechaSolicitud > DATEADD(HOUR,-24,SYSUTCDATETIME())", empresa.RFC);
            if (rechazos > 0) Agregar(NivelSalud.Aviso, "Rechazos del SAT", rechazos + " solicitud(es) rechazada(s) en las ultimas 24 h (p. ej. 5002: parametros repetidos); se reintentan con el rango ensanchado.");

            // 5) Verificacion contra Metadata: lo que el SAT dice que existe vs lo que tenemos
            var hayMetadata = Contar(conn, "SELECT COUNT(*) FROM CfdiMetadata WHERE RFCReceptor=@r OR RFCEmisor=@r", empresa.RFC) > 0;
            if (!hayMetadata)
                Agregar(NivelSalud.Aviso, "Verificacion contra el SAT", "Aun no hay Metadata del SAT: sin ella no se puede comprobar que no falte ningun XML ni detectar cancelaciones desde su origen.");
            else
            {
                int faltan = 0;
                foreach (var tipo in new[] { "Recibidos", "Emitidos" })
                    faltan += BrosSatDb.ObtenerMesesConFaltantes(conn, empresa.RFC, tipo).Sum(m => m.Faltan);
                if (faltan == 0) Agregar(NivelSalud.Ok, "Verificacion contra el SAT", "El SAT reporta CFDI vigentes y todos estan descargados.");
                else Agregar(faltan > 20 ? NivelSalud.Critico : NivelSalud.Aviso, "Verificacion contra el SAT", faltan + " CFDI vigente(s) que el SAT reporta no estan descargados; se piden de nuevo por mes.");
            }

            // 6) Estatus Vigente/Cancelado al dia
            var sinVerificar = Contar(conn, @"SELECT COUNT(*) FROM CfdiRecibido WHERE (RFCReceptor=@r OR RFCEmisor=@r)
                AND (FechaUltimaVerificacionEstatus IS NULL OR FechaUltimaVerificacionEstatus < DATEADD(HOUR,-48,SYSUTCDATETIME()))", empresa.RFC);
            if (sinVerificar > 0) Agregar(NivelSalud.Aviso, "Estatus (cancelaciones)", sinVerificar.ToString("N0") + " CFDI sin verificar su estatus en el SAT desde hace mas de 48 h (o nunca).");
            else Agregar(NivelSalud.Ok, "Estatus (cancelaciones)", "Todos los CFDI se verificaron en las ultimas 48 h.");

            // 6b) Cancelaciones que tocan documentos de Comercial, y emisores en la lista EFOS
            var canceladosConDoc = Contar(conn, @"SELECT COUNT(*) FROM CfdiCancelacionComercial k JOIN CfdiRecibido c ON c.CfdiID=k.CfdiID
                WHERE (c.RFCReceptor=@r OR c.RFCEmisor=@r) AND k.Revisada=0", empresa.RFC);
            if (canceladosConDoc > 0)
                Agregar(NivelSalud.Aviso, "Cancelaciones con documento", canceladosConDoc + " CFDI se cancelaron en el SAT y ya estaban vinculados a un documento de Comercial: hay que revisar si se cancela o se ajusta (Salud > Cancelaciones con documento).");
            var efos = Contar(conn, @"SELECT COUNT(*) FROM CfdiRecibido WHERE RFCReceptor=@r AND ValidacionEFOS IS NOT NULL AND ValidacionEFOS NOT IN ('200','201')", empresa.RFC);
            if (efos > 0)
                Agregar(NivelSalud.Aviso, "Lista 69-B (EFOS)", efos + " CFDI recibido(s) cuyo emisor figura en la lista de operaciones inexistentes del SAT segun la ultima consulta de estatus.");

            // 7) Archivos en disco
            int sinArchivo = 0;
            using (var cmd = new SqlCommand("SELECT RutaArchivoXml FROM CfdiRecibido WHERE (RFCReceptor=@r OR RFCEmisor=@r) AND RutaArchivoXml IS NOT NULL", conn))
            {
                cmd.Parameters.AddWithValue("@r", empresa.RFC);
                using (var lector = cmd.ExecuteReader())
                    while (lector.Read())
                        if (!File.Exists(lector.GetString(0))) sinArchivo++;
            }
            if (sinArchivo > 0) Agregar(NivelSalud.Critico, "Archivos XML", sinArchivo.ToString("N0") + " CFDI estan en la base pero su archivo XML ya no existe en disco (se movio o se borro).");
            else Agregar(NivelSalud.Ok, "Archivos XML", "Todos los XML registrados existen en disco.");

            // 8) Integracion con Comercial
            if (!string.IsNullOrWhiteSpace(empresa.ComercialConexionSql))
            {
                var sinSync = Contar(conn, @"SELECT COUNT(*) FROM CfdiRecibido WHERE (RFCReceptor=@r OR RFCEmisor=@r) AND Archivado=0
                    AND RutaArchivoXml IS NOT NULL AND FechaSincronizadoComercial IS NULL", empresa.RFC);
                if (sinSync > 0) Agregar(NivelSalud.Aviso, "Comercial", sinSync.ToString("N0") + " CFDI descargados todavia no estan en Comercial.");
                else Agregar(NivelSalud.Ok, "Comercial", "Todos los CFDI descargados estan en Comercial.");
            }

            return r;
        }

        private static void Pasada(List<HallazgoSalud> r, SqlConnection conn, string clave, string tema, double horasAviso, double horasCritico)
        {
            var f = BrosSatDb.LeerEstado(conn, clave);
            if (!f.HasValue) return;
            var edad = DateTime.UtcNow - f.Value;
            var nivel = edad.TotalHours > horasCritico ? NivelSalud.Critico : edad.TotalHours > horasAviso ? NivelSalud.Aviso : NivelSalud.Ok;
            r.Add(new HallazgoSalud { Nivel = nivel, Tema = tema, Detalle = "Ultima pasada hace " + Formato(edad) + "." });
        }

        private static int Contar(SqlConnection conn, string sql, string rfc)
        {
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                return (int)cmd.ExecuteScalar();
            }
        }

        public static string Formato(TimeSpan t) =>
            t.TotalMinutes < 1 ? "menos de 1 min" : t.TotalHours < 1 ? (int)t.TotalMinutes + " min" : t.TotalDays < 2 ? Math.Round(t.TotalHours, 1) + " h" : (int)t.TotalDays + " dias";
    }
}
