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

// Program.cs
//
// Carga una FIEL real (.cer/.key + contrasena, pasados por argumentos -- nunca hardcoded ni
// pegados en un chat). Sin --autenticar, solo arma+firma el sobre de Autentica y lo verifica
// LOCALMENTE (Fase 0, sin red). Con --autenticar, ademas lo manda de verdad a Autenticacion.svc
// del SAT (Fase 1, primer paso) -- requiere el flag explicito porque ya es una llamada real
// contra el servicio de produccion del SAT, aunque Autenticacion en si no consuma del limite
// diario de SolicitaDescarga.
//
// Uso:
//   dotnet run -- --cer "C:\ruta\fiel.cer" --key "C:\ruta\fiel.key" --password "..." --rfc "XAXX010101000" [--autenticar]

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas
{
    internal static class Program
    {
        private static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        private static async Task<int> MainAsync(string[] args)
        {
            string rutaCer = null, rutaKey = null, password = null, rfc = null, salida = "sobre_firmado.xml", idSolicitud = null, idPaquete = null, salidaZip = null, conexionSql = null;
            bool autenticar = false, solicitar = false, auto = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--cer": rutaCer = Siguiente(args, ref i); break;
                    case "--key": rutaKey = Siguiente(args, ref i); break;
                    case "--password": password = Siguiente(args, ref i); break;
                    case "--rfc": rfc = Siguiente(args, ref i); break;
                    case "--salida": salida = Siguiente(args, ref i); break;
                    // Opcional -- si se pasa, ademas de imprimir en consola, se registra todo en
                    // la BD propia (SolicitudDescarga/CfdiRecibido/CfdiRelacion). Sin este flag,
                    // el programa sigue funcionando exactamente igual que antes (solo consola).
                    case "--conn": conexionSql = Siguiente(args, ref i); break;
                    // Opt-in explicito: sin este flag, el programa NUNCA toca la red -- solo
                    // arma y verifica la firma localmente, como en la Fase 0.
                    case "--autenticar": autenticar = true; break;
                    // Este SI gasta cupo diario real del SAT -- flag separado, aparte, y
                    // requiere --autenticar tambien (necesita el token de ese paso).
                    case "--solicitar": solicitar = true; break;
                    // Consulta el estatus de una solicitud YA hecha (IdSolicitud de una corrida
                    // anterior con --solicitar) -- esto NO gasta cupo diario, se puede llamar
                    // tantas veces como se quiera. Requiere --autenticar tambien.
                    case "--idsolicitud": idSolicitud = Siguiente(args, ref i); break;
                    // Descarga el ZIP de un paquete YA reportado listo por --idsolicitud --
                    // OJO: solo se puede descargar 2 veces por paquete y vive 72 horas.
                    case "--idpaquete": idPaquete = Siguiente(args, ref i); break;
                    case "--salida-zip": salidaZip = Siguiente(args, ref i); break;
                    // Una pasada del motor de cola: revisa TODAS las solicitudes pendientes en
                    // la BD (de cualquier --solicitar hecho antes), verifica estatus y descarga
                    // lo que ya este listo. Pensado para Tarea Programada de Windows. Requiere
                    // --conn (no tiene caso correrlo sin persistir resultados).
                    case "--auto": auto = true; break;
                    default:
                        Console.Error.WriteLine("Argumento desconocido: " + args[i]);
                        return Uso();
                }
            }

            if (string.IsNullOrEmpty(rutaCer) || string.IsNullOrEmpty(rutaKey) || string.IsNullOrEmpty(password))
                return Uso();
            if (!auto && string.IsNullOrEmpty(rfc))
                return Uso();
            if (auto && string.IsNullOrEmpty(conexionSql))
            {
                Console.Error.WriteLine("--auto requiere --conn (no tiene caso correr el motor de cola sin persistir resultados).");
                return Uso();
            }

            SqlConnection conn = null;
            try
            {
                var cert = SatFirmaXml.CargarFiel(rutaCer, rutaKey, password, out var llave);
                Console.WriteLine("FIEL cargada: " + cert.Subject);
                Console.WriteLine("Vigente: " + cert.NotBefore.ToString("yyyy-MM-dd") + " a " + cert.NotAfter.ToString("yyyy-MM-dd"));

                if (DateTime.Now < cert.NotBefore || DateTime.Now > cert.NotAfter)
                    Console.Error.WriteLine("ADVERTENCIA: el certificado esta fuera de su periodo de vigencia.");

                if (auto)
                {
                    conn = new SqlConnection(conexionSql);
                    conn.Open();
                    EsquemaSql.Asegurar(conn);
                    Console.WriteLine("Pasada del motor de cola iniciada (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ")...");
                    await SolicitudWorker.EjecutarPasadaAsync(conn, cert, llave, "xml");
                    Console.WriteLine("Pasada terminada.");
                    return 0;
                }

                var doc = SatFirmaXml.FirmarAutentica(cert, llave);
                doc.Save(salida);
                Console.WriteLine("Sobre firmado guardado en: " + salida);

                bool valida = SatFirmaXml.VerificarFirma(doc);
                Console.WriteLine(valida
                    ? "Firma XML-DSig verificada localmente: OK."
                    : "ADVERTENCIA: la firma NO paso la verificacion local -- no mandar esto al SAT.");
                if (!valida) return 1;

                if (!autenticar)
                {
                    Console.WriteLine("(Solo verificacion local -- pasa --autenticar para mandarlo de verdad a Autenticacion.svc del SAT.)");
                    return 0;
                }

                if (!string.IsNullOrEmpty(conexionSql))
                {
                    conn = new SqlConnection(conexionSql);
                    conn.Open();
                    EsquemaSql.Asegurar(conn);
                    Console.WriteLine("Conectado a la BD propia -- esquema verificado/creado.");
                }

                Console.WriteLine("Mandando Autentica al SAT real (" + rfc + ")...");
                var resultado = await SatSoapClient.AutenticarAsync(cert, llave);
                if (!resultado.Exito)
                {
                    Console.Error.WriteLine("ERROR de Autenticacion: " + resultado.Error);
                    if (!string.IsNullOrEmpty(resultado.RespuestaCruda))
                        Console.Error.WriteLine("Respuesta cruda del SAT:\n" + resultado.RespuestaCruda);
                    return 1;
                }

                Console.WriteLine("Autenticacion OK. Token (primeros 40 caracteres): " + resultado.Token.Substring(0, Math.Min(40, resultado.Token.Length)) + "...");

                if (!string.IsNullOrEmpty(idPaquete))
                {
                    string destinoZip = salidaZip ?? (idPaquete.Replace(":", "_") + ".zip");
                    Console.WriteLine("Descargando paquete " + idPaquete + " (max. 2 veces por paquete, vive 72h)...");
                    var desc = await SatSoapClient.DescargarAsync(cert, llave, resultado.Token, idPaquete, rfc);
                    if (!desc.Exito)
                    {
                        Console.Error.WriteLine("ERROR de Descarga: " + desc.Error);
                        if (!string.IsNullOrEmpty(desc.RespuestaCruda))
                            Console.Error.WriteLine("Respuesta cruda del SAT:\n" + desc.RespuestaCruda);
                        return 1;
                    }

                    File.WriteAllBytes(destinoZip, desc.PaqueteZip);
                    Console.WriteLine("CodEstatus=" + desc.CodEstatus + " Mensaje=" + desc.Mensaje);
                    Console.WriteLine("Paquete guardado en: " + destinoZip + " (" + desc.PaqueteZip.Length + " bytes)");

                    if (conn != null)
                    {
                        BrosSatDb.RegistrarPaquete(conn, idSolicitud ?? "", idPaquete);

                        string carpetaXml = Path.Combine("xml", idPaquete.Replace(":", "_"));
                        Directory.CreateDirectory(carpetaXml);
                        using (var zip = new ZipArchive(new MemoryStream(desc.PaqueteZip), ZipArchiveMode.Read))
                        {
                            int nuevos = 0, yaExistian = 0, errores = 0;
                            foreach (var entrada in zip.Entries)
                            {
                                if (!entrada.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                                string rutaXml = Path.Combine(carpetaXml, entrada.Name);
                                entrada.ExtractToFile(rutaXml, overwrite: true);
                                try
                                {
                                    var parseado = CfdiXmlParser.Parsear(File.ReadAllText(rutaXml));
                                    bool esNuevo = BrosSatDb.GuardarCfdiRecibido(conn, parseado, Path.GetFullPath(rutaXml));
                                    if (esNuevo) { BrosSatDb.GuardarRelaciones(conn, parseado); nuevos++; }
                                    else yaExistian++;
                                }
                                catch (Exception exParse)
                                {
                                    errores++;
                                    Console.Error.WriteLine("  No se pudo parsear " + entrada.Name + ": " + exParse.Message);
                                }
                            }
                            Console.WriteLine("BD: " + nuevos + " CFDI nuevos, " + yaExistian + " ya existian, " + errores + " con error de parseo.");
                        }
                    }
                    return 0;
                }

                if (!string.IsNullOrEmpty(idSolicitud))
                {
                    Console.WriteLine("Consultando estatus de IdSolicitud=" + idSolicitud + " (no gasta cupo diario)...");
                    var verif = await SatSoapClient.VerificarSolicitudAsync(cert, llave, resultado.Token, idSolicitud, rfc);
                    if (!verif.Exito)
                    {
                        Console.Error.WriteLine("ERROR de VerificaSolicitud: " + verif.Error);
                        if (!string.IsNullOrEmpty(verif.RespuestaCruda))
                            Console.Error.WriteLine("Respuesta cruda del SAT:\n" + verif.RespuestaCruda);
                        return 1;
                    }

                    Console.WriteLine("EstadoSolicitud=" + verif.EstadoSolicitud + " (1=Aceptada 2=EnProceso 3=Terminada 4=Error 5=Rechazada 6=Vencida)");
                    Console.WriteLine("CodEstatus=" + verif.CodEstatus + " Mensaje=" + verif.Mensaje + " NumeroCFDIs=" + verif.NumeroCFDIs);

                    if (conn != null)
                    {
                        string[] nombresEstado = { "", "Aceptada", "EnProceso", "Terminada", "Error", "Rechazada", "Vencida" };
                        int numEstado;
                        string estadoTexto = int.TryParse(verif.EstadoSolicitud, out numEstado) && numEstado >= 1 && numEstado <= 6
                            ? nombresEstado[numEstado] : verif.EstadoSolicitud;
                        int? numeroCfdis = int.TryParse(verif.NumeroCFDIs, out var n) ? n : (int?)null;
                        BrosSatDb.ActualizarEstatusSolicitud(conn, idSolicitud, estadoTexto, numeroCfdis);
                    }

                    if (verif.IdsPaquetes.Count > 0)
                    {
                        Console.WriteLine("Paquetes listos para descargar:");
                        foreach (var id in verif.IdsPaquetes) Console.WriteLine("  " + id);
                    }
                    else
                    {
                        Console.WriteLine("Todavia sin paquetes listos -- normal si EstadoSolicitud sigue en 1 o 2, vuelve a intentar mas tarde.");
                    }
                    return 0;
                }

                if (!solicitar)
                {
                    Console.WriteLine("(Autenticacion confirmada. Pasa --solicitar para hacer la SolicitaDescarga real, o --idsolicitud <id> para consultar una ya hecha.)");
                    return 0;
                }

                // Rango de prueba deliberadamente chico: ayer completo, solo RECIBIDOS.
                var hasta = DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                var desde = DateTime.Today.AddDays(-1);
                Console.WriteLine("Mandando SolicitaDescarga (RECIBIDOS, " + desde.ToString("yyyy-MM-dd") + ")... esto gasta una solicitud real de tu cupo diario.");

                var solic = await SatSoapClient.SolicitarDescargaAsync(
                    cert, llave, resultado.Token, rfcSolicitante: rfc, rfcEmisor: null, rfcReceptor: rfc, desde: desde, hasta: hasta);

                if (!solic.Exito)
                {
                    Console.Error.WriteLine("ERROR de SolicitaDescarga: " + solic.Error);
                    if (!string.IsNullOrEmpty(solic.RespuestaCruda))
                        Console.Error.WriteLine("Respuesta cruda del SAT:\n" + solic.RespuestaCruda);
                    return 1;
                }

                Console.WriteLine("SolicitaDescarga aceptada. IdSolicitud: " + solic.IdSolicitud);
                Console.WriteLine("El SAT tarda de minutos a horas en dejarla lista. Consulta el estatus despues con:");
                Console.WriteLine("  --autenticar --idsolicitud " + solic.IdSolicitud);

                if (conn != null)
                    BrosSatDb.RegistrarSolicitud(conn, solic.IdSolicitud, rfc, "Recibidos", desde, hasta, "Manual");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                conn?.Dispose();
            }
        }

        private static string Siguiente(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Falta el valor para " + args[i]);
            return args[++i];
        }

        private static int Uso()
        {
            Console.Error.WriteLine(
                "Uso: BrosLMV.Descargas --cer <ruta.cer> --key <ruta.key> --password <contrasena> [--rfc <RFC>] [--conn <cadena SQL>] [--salida <archivo.xml>] [--autenticar] [--solicitar | --idsolicitud <id> | --idpaquete <id> [--salida-zip <archivo.zip>] | --auto]\n\n" +
                "Sin flags: arma y firma el sobre Autentica, lo guarda en disco y lo verifica localmente. No toca la red.\n" +
                "Con --autenticar: ademas lo manda de verdad a Autenticacion.svc del SAT real (no gasta cupo diario).\n" +
                "Con --autenticar --solicitar: ademas hace SolicitaDescarga real (RECIBIDOS de ayer) -- SI gasta cupo diario.\n" +
                "Con --autenticar --idsolicitud <id>: consulta el estatus de una solicitud ya hecha -- no gasta cupo diario.\n" +
                "Con --autenticar --idpaquete <id>: descarga el ZIP de un paquete ya listo -- max. 2 descargas por paquete, vive 72h.\n" +
                "Con --conn <cadena>: ademas de imprimir en consola, registra todo en la BD propia (crea el esquema si falta).\n" +
                "Con --auto --conn <cadena>: una pasada del motor de cola -- revisa TODO lo pendiente en la BD, verifica y descarga. Pensado para Tarea Programada. No requiere --rfc.");
            return 2;
        }
    }
}
