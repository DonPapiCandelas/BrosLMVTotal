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
// procesar las solicitudes pendientes de una corrida tipica). NO crea solicitudes nuevas -- eso
// lo hace --auto-solicitar-todas (Program.cs), con su propia regla de frecuencia (SI gasta cupo).

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
        // rfcFiltro debe pasarse siempre que pueda haber mas de una empresa en la BD -- ver el
        // comentario en BrosSatDb.ObtenerSolicitudesPendientes: la firma va atada a la FIEL de
        // UNA empresa, y el SAT rechaza la llamada si se usa para el RFC de otra.
        //
        // carpetaXml/estructuraCarpetas/plantillaNombreArchivo vienen de Empresa (ver
        // OrganizadorArchivos.cs) -- pedido explicito del usuario 2026-08-14, antes estaba fijo a
        // una carpeta "xml" relativa sin poder elegir donde ni como se organizaban los archivos.
        //
        // comercialCarpetaXmlRecibidos/Emitidos: pedido explicito del usuario 2026-08-18 -- copia
        // PLANA (sin subcarpetas) del XML hacia la carpeta que Comercial Pro usa para su propio
        // boton nativo "Importar" (Opciones > CFDI). NULL/vacio = no copiar (paso opcional, ver
        // material interno, Opcion A). Nunca se toca la subcarpeta
        // "Procesado" de Comercial -- eso es "importar" de verdad, un paso posterior y distinto.
        public static async Task EjecutarPasadaAsync(SqlConnection conn, X509Certificate2 cert, RSA llave,
            string carpetaXml, string estructuraCarpetas, string plantillaNombreArchivo, string rfcFiltro = null,
            string comercialCarpetaXmlRecibidos = null, string comercialCarpetaXmlEmitidos = null,
            string comercialConexionSql = null)
        {
            var pendientes = BrosSatDb.ObtenerSolicitudesPendientes(conn, rfcFiltro);
            if (pendientes.Count == 0)
            {
                Bitacora.Escribir("Nada pendiente.");
                return;
            }
            Bitacora.Escribir(pendientes.Count + " solicitud(es) pendiente(s).");

            var auth = await SatSoapClient.AutenticarAsync(cert, llave);
            if (!auth.Exito)
            {
                Bitacora.EscribirError("ERROR de Autenticacion, se aborta la pasada: " + auth.Error);
                return;
            }
            string token = auth.Token;

            foreach (var s in pendientes)
            {
                try
                {
                    await ProcesarUnaAsync(conn, cert, llave, token, s, carpetaXml, estructuraCarpetas, plantillaNombreArchivo,
                        comercialCarpetaXmlRecibidos, comercialCarpetaXmlEmitidos, comercialConexionSql);
                }
                catch (Exception ex)
                {
                    // Una solicitud con problemas no debe tumbar la pasada completa -- se
                    // reintenta sola en la siguiente corrida programada.
                    Bitacora.EscribirError("ERROR procesando " + s.IdSolicitud + ": " + ex.Message);
                }
            }
        }

        private static async Task ProcesarUnaAsync(SqlConnection conn, X509Certificate2 cert, RSA llave, string token,
            SolicitudPendiente s, string carpetaXml, string estructuraCarpetas, string plantillaNombreArchivo,
            string comercialCarpetaXmlRecibidos, string comercialCarpetaXmlEmitidos, string comercialConexionSql)
        {
            if (s.Estatus == "Aceptada" || s.Estatus == "EnProceso")
            {
                var verif = await SatSoapClient.VerificarSolicitudAsync(cert, llave, token, s.IdSolicitud, s.RfcSolicitante);
                if (!verif.Exito)
                {
                    Bitacora.EscribirError("  " + s.IdSolicitud + ": ERROR VerificaSolicitud: " + verif.Error);
                    return;
                }

                string[] nombresEstado = { "", "Aceptada", "EnProceso", "Terminada", "Error", "Rechazada", "Vencida" };
                string estadoTexto = int.TryParse(verif.EstadoSolicitud, out var n) && n >= 1 && n <= 6 ? nombresEstado[n] : verif.EstadoSolicitud;
                int? numeroCfdis = int.TryParse(verif.NumeroCFDIs, out var nc) ? nc : (int?)null;
                BrosSatDb.ActualizarEstatusSolicitud(conn, s.IdSolicitud, estadoTexto, numeroCfdis);
                Bitacora.Escribir("  " + s.IdSolicitud + ": " + estadoTexto + " (" + numeroCfdis + " CFDI)");

                if (verif.IdsPaquetes.Count > 0)
                    BrosSatDb.RegistrarPaquetesDetectados(conn, s.IdSolicitud, verif.IdsPaquetes);
            }

            var pendientesDescarga = BrosSatDb.ObtenerPaquetesSinDescargar(conn, s.IdSolicitud);
            foreach (var idPaquete in pendientesDescarga)
            {
                var desc = await SatSoapClient.DescargarAsync(cert, llave, token, idPaquete, s.RfcSolicitante);
                if (!desc.Exito)
                {
                    Bitacora.EscribirError("  " + idPaquete + ": ERROR Descarga: " + desc.Error);
                    // Solo se registra el intento (VecesDescargado++) si el SAT de verdad
                    // respondio -- eso es lo que hasta ahora se ha confirmado que cuenta contra
                    // el limite de 2 descargas del paquete (ver CodEstatus=5008 en la sec. 6 de
                    // DOCUMENTACION.md). Un error de red/HTTP antes de eso no deberia "gastar" un
                    // intento en la UI, y menos dejar la solicitud viendose "Terminada" sin avisar
                    // que la descarga en si fallo.
                    if (desc.RespuestaSatRecibida)
                        BrosSatDb.RegistrarPaquete(conn, s.IdSolicitud, idPaquete, error: desc.Error);
                    continue; // se reintenta en la siguiente pasada mientras queden intentos -- OJO con el limite de 2 descargas
                }

                int nuevos = 0;
                string errorZip = null;
                bool esMetadata = string.Equals(s.TipoSolicitud, "Metadata", StringComparison.OrdinalIgnoreCase);
                try
                {
                    using (var zip = new ZipArchive(new MemoryStream(desc.PaqueteZip), ZipArchiveMode.Read))
                    {
                        if (esMetadata)
                        {
                            // Metadata llega como TXT delimitado por ~ (no como XML). Además de
                            // conservar el archivo original, incorporamos sus registros al
                            // catálogo propio: ahí vive el estado cancelado que los XML vigentes no
                            // pueden reportar.
                            string carpetaMetadata = Path.Combine(string.IsNullOrWhiteSpace(carpetaXml) ? "xml" : carpetaXml, "_metadata", idPaquete.Replace(":", "_"));
                            Directory.CreateDirectory(carpetaMetadata);
                            foreach (var entrada in zip.Entries)
                            {
                                string rutaTxt = Path.Combine(carpetaMetadata, entrada.Name);
                                entrada.ExtractToFile(rutaTxt, overwrite: true);
                                string textoMetadata;
                                using (var lector = new StreamReader(rutaTxt)) textoMetadata = lector.ReadToEnd();
                                int registros = 0;
                                foreach (var metadata in CfdiMetadataParser.Parsear(textoMetadata))
                                {
                                    BrosSatDb.GuardarMetadata(conn, metadata, s.Tipo, s.IdSolicitud);
                                    registros++;
                                }
                                nuevos += registros;
                                Bitacora.Escribir("    Metadata guardada: " + rutaTxt);
                                Bitacora.Escribir("    " + registros + " registro(s) de metadata incorporados.");
                            }
                        }
                        else
                        {
                            foreach (var entrada in zip.Entries)
                            {
                                if (!entrada.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                                try
                                {
                                    string contenidoXml;
                                    using (var streamEntrada = entrada.Open())
                                    using (var lector = new StreamReader(streamEntrada))
                                        contenidoXml = lector.ReadToEnd();

                                    // Parsear PRIMERO -- la ruta final depende de datos del propio
                                    // CFDI (UUID/Fecha/Folio/etc, ver OrganizadorArchivos), no del
                                    // nombre que trae el ZIP del SAT.
                                    var parseado = CfdiXmlParser.Parsear(contenidoXml);
                                    string carpetaDestino = OrganizadorArchivos.ResolverCarpeta(carpetaXml, estructuraCarpetas, s.Tipo, parseado.FechaEmision);
                                    Directory.CreateDirectory(carpetaDestino);
                                    string rutaXml = OrganizadorArchivos.ResolverRutaArchivo(carpetaDestino, plantillaNombreArchivo, parseado);
                                    File.WriteAllText(rutaXml, contenidoXml);

                                    bool copiadoComercial = false;
                                    if (!string.IsNullOrWhiteSpace(comercialConexionSql))
                                    {
                                        // Automatiza el equivalente al boton nativo "Importar" de Comercial
                                        // -- pedido explicito del usuario 2026-08-18: "configurar que se
                                        // carguen los emitidos, porque esos los tuve que cargar
                                        // manualmente". Escribe directo en docDocumentCFDiSAT/
                                        // docDocumentItemCFDiSAT (solo staging, NO crea documentos ni
                                        // polizas -- ver ComercialImportador.cs). Deliberadamente NO se
                                        // copia tambien el archivo a XMLRecibidos/XMLEmitidos en este
                                        // caso: si el usuario despues le da "Importar" a mano sobre ese
                                        // mismo archivo, Comercial podria insertar una fila duplicada (no
                                        // hay UNIQUE constraint real sobre UUID en su tabla) -- mejor no
                                        // dejar nada ahi que invite a reimportar lo que ya se importo solo.
                                        var (resultadoImport, errorImport) = await ComercialImportador.ImportarAsync(
                                            DpapiHelper.DescifrarConexionSql(comercialConexionSql), contenidoXml, s.RfcSolicitante);
                                        if (resultadoImport == ResultadoImportComercial.Error)
                                            Bitacora.EscribirError("    No se pudo importar " + parseado.UUID + " a la base de Comercial: " + errorImport);
                                        else
                                            copiadoComercial = true; // Insertado o YaExistia -- en ambos casos ya esta en Comercial.
                                    }
                                    else
                                    {
                                        string carpetaComercial = s.Tipo == "Emitidos" ? comercialCarpetaXmlEmitidos : comercialCarpetaXmlRecibidos;
                                        if (!string.IsNullOrWhiteSpace(carpetaComercial))
                                        {
                                            try
                                            {
                                                Directory.CreateDirectory(carpetaComercial);
                                                string rutaComercial = Path.Combine(carpetaComercial, parseado.UUID + ".xml");
                                                File.WriteAllText(rutaComercial, contenidoXml);
                                                copiadoComercial = true;
                                            }
                                            catch (Exception exComercial)
                                            {
                                                // No debe tumbar el guardado propio del CFDI -- la copia hacia
                                                // Comercial es un paso adicional opcional, no la fuente de verdad.
                                                Bitacora.EscribirError("    No se pudo copiar " + parseado.UUID + " a la carpeta de Comercial (" + carpetaComercial + "): " + exComercial.Message);
                                            }
                                        }
                                    }

                                    int cfdiId = BrosSatDb.GuardarCfdiRecibido(conn, parseado, Path.GetFullPath(rutaXml), out bool esNuevo);
                                    if (esNuevo)
                                    {
                                        BrosSatDb.GuardarRelaciones(conn, parseado);
                                        BrosSatDb.GuardarConceptos(conn, cfdiId, parseado.Conceptos);
                                        BrosSatDb.GuardarPagosDocumentos(conn, cfdiId, parseado.PagosDocumentos);
                                        nuevos++;
                                    }
                                    // Pedido explicito del usuario 2026-08-18: estatus visible de
                                    // "sincronizado a Comercial / sin sincronizar" por CFDI.
                                    if (copiadoComercial) BrosSatDb.MarcarSincronizadoComercial(conn, parseado.UUID);
                                }
                                catch (Exception exParse)
                                {
                                    Bitacora.EscribirError("    No se pudo parsear " + entrada.Name + ": " + exParse.Message);
                                }
                            }
                        }
                    }
                }
                catch (Exception exZip)
                {
                    // El SAT ya sirvio los bytes (Exito=true) -- si no abren como ZIP, de todos
                    // modos hay que registrar el intento (RegistrarPaquete abajo) porque del lado
                    // del SAT ya se conto como una de las 2 descargas permitidas del paquete. Se
                    // guardan los bytes crudos para poder inspeccionar que llego realmente, en vez
                    // de perder la evidencia y solo reintentar a ciegas la siguiente pasada.
                    string carpetaError = Path.Combine(string.IsNullOrWhiteSpace(carpetaXml) ? "xml" : carpetaXml, "_errores");
                    Directory.CreateDirectory(carpetaError);
                    string rutaCruda = Path.Combine(carpetaError, idPaquete.Replace(":", "_") + "_crudo_no_es_zip.bin");
                    File.WriteAllBytes(rutaCruda, desc.PaqueteZip);
                    errorZip = "El contenido no abre como ZIP (" + exZip.Message + ").";
                    Bitacora.EscribirError("  " + idPaquete + ": el SAT respondio Exito pero el contenido no abre como ZIP (" + exZip.Message + "). Bytes crudos guardados en " + rutaCruda + " para revisar.");
                }

                BrosSatDb.RegistrarPaquete(conn, s.IdSolicitud, idPaquete, error: errorZip);
                Bitacora.Escribir("  " + idPaquete + ": descargado, " + nuevos + " CFDI nuevos guardados.");
            }
        }
    }
}
