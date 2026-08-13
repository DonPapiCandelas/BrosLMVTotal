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

// SolicitudWorker.cs -- una PASADA del motor de cola: revisa las solicitudes pendientes en la
// BD propia, pregunta su estatus al SAT (no gasta cupo diario), y descarga los paquetes que ya
// esten listos (esto SI tiene limite: 2 descargas por paquete, 72h de vida).
//
// Pensado para correr desde una Tarea Programada de Windows cada N minutos -- una pasada
// deliberadamente corta (autentica UNA vez al inicio, el token dura ~5 min, suficiente para
// procesar las solicitudes pendientes de una corrida tipica). NO crea solicitudes nuevas --
// eso sigue siendo una decision explicita del usuario via --solicitar, no algo automatico.

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal static class SolicitudWorker
    {
        public static async Task EjecutarPasadaAsync(SqlConnection conn, X509Certificate2 cert, RSA llave, string carpetaXmlBase)
        {
            var pendientes = BrosSatDb.ObtenerSolicitudesPendientes(conn);
            if (pendientes.Count == 0)
            {
                Console.WriteLine("Nada pendiente.");
                return;
            }
            Console.WriteLine(pendientes.Count + " solicitud(es) pendiente(s).");

            var auth = await SatSoapClient.AutenticarAsync(cert, llave);
            if (!auth.Exito)
            {
                Console.Error.WriteLine("ERROR de Autenticacion, se aborta la pasada: " + auth.Error);
                return;
            }
            string token = auth.Token;

            foreach (var s in pendientes)
            {
                try
                {
                    await ProcesarUnaAsync(conn, cert, llave, token, s, carpetaXmlBase);
                }
                catch (Exception ex)
                {
                    // Una solicitud con problemas no debe tumbar la pasada completa -- se
                    // reintenta sola en la siguiente corrida programada.
                    Console.Error.WriteLine("ERROR procesando " + s.IdSolicitud + ": " + ex.Message);
                }
            }
        }

        private static async Task ProcesarUnaAsync(SqlConnection conn, X509Certificate2 cert, RSA llave, string token,
            SolicitudPendiente s, string carpetaXmlBase)
        {
            if (s.Estatus == "Aceptada" || s.Estatus == "EnProceso")
            {
                var verif = await SatSoapClient.VerificarSolicitudAsync(cert, llave, token, s.IdSolicitud, s.RfcSolicitante);
                if (!verif.Exito)
                {
                    Console.Error.WriteLine("  " + s.IdSolicitud + ": ERROR VerificaSolicitud: " + verif.Error);
                    return;
                }

                string[] nombresEstado = { "", "Aceptada", "EnProceso", "Terminada", "Error", "Rechazada", "Vencida" };
                string estadoTexto = int.TryParse(verif.EstadoSolicitud, out var n) && n >= 1 && n <= 6 ? nombresEstado[n] : verif.EstadoSolicitud;
                int? numeroCfdis = int.TryParse(verif.NumeroCFDIs, out var nc) ? nc : (int?)null;
                BrosSatDb.ActualizarEstatusSolicitud(conn, s.IdSolicitud, estadoTexto, numeroCfdis);
                Console.WriteLine("  " + s.IdSolicitud + ": " + estadoTexto + " (" + numeroCfdis + " CFDI)");

                if (verif.IdsPaquetes.Count > 0)
                    BrosSatDb.RegistrarPaquetesDetectados(conn, s.IdSolicitud, verif.IdsPaquetes);
            }

            var pendientesDescarga = BrosSatDb.ObtenerPaquetesSinDescargar(conn, s.IdSolicitud);
            foreach (var idPaquete in pendientesDescarga)
            {
                var desc = await SatSoapClient.DescargarAsync(cert, llave, token, idPaquete, s.RfcSolicitante);
                if (!desc.Exito)
                {
                    Console.Error.WriteLine("  " + idPaquete + ": ERROR Descarga: " + desc.Error);
                    continue; // se reintenta en la siguiente pasada -- OJO con el limite de 2 descargas
                }

                string carpetaPaquete = Path.Combine(carpetaXmlBase, idPaquete.Replace(":", "_"));
                Directory.CreateDirectory(carpetaPaquete);
                int nuevos = 0;
                using (var zip = new ZipArchive(new MemoryStream(desc.PaqueteZip), ZipArchiveMode.Read))
                {
                    foreach (var entrada in zip.Entries)
                    {
                        if (!entrada.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                        string rutaXml = Path.Combine(carpetaPaquete, entrada.Name);
                        entrada.ExtractToFile(rutaXml, overwrite: true);
                        try
                        {
                            var parseado = CfdiXmlParser.Parsear(File.ReadAllText(rutaXml));
                            if (BrosSatDb.GuardarCfdiRecibido(conn, parseado, Path.GetFullPath(rutaXml)))
                            {
                                BrosSatDb.GuardarRelaciones(conn, parseado);
                                nuevos++;
                            }
                        }
                        catch (Exception exParse)
                        {
                            Console.Error.WriteLine("    No se pudo parsear " + entrada.Name + ": " + exParse.Message);
                        }
                    }
                }

                BrosSatDb.RegistrarPaquete(conn, s.IdSolicitud, idPaquete);
                Console.WriteLine("  " + idPaquete + ": descargado, " + nuevos + " CFDI nuevos guardados.");
            }
        }
    }
}
