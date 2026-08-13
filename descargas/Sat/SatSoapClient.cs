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

// SatSoapClient.cs -- Fase 1, primer paso: SOLO Autenticacion.
//
// Deliberadamente no incluye todavia SolicitaDescarga/VerificaSolicitud/Descarga -- primero
// hay que confirmar que la Autenticacion (la unica llamada firmada con la FIEL) funciona
// contra el SAT real antes de construir el resto sobre esa base. Autenticacion.svc no consume
// del limite diario de "solicitudes de descarga" (ese limite aplica a SolicitaDescarga), asi
// que es un primer paso mas barato para validar que la firma es aceptada.

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace BrosLMV.Descargas.Sat
{
    internal sealed class SatAutenticacionResultado
    {
        public bool Exito;
        public string Token;
        public string Error;
        public string RespuestaCruda;
    }

    internal sealed class SatSolicitaDescargaResultado
    {
        public bool Exito;
        public string IdSolicitud;
        public string CodEstatus;
        public string Mensaje;
        public string Error;
        public string RespuestaCruda;
    }

    internal static class SatSoapClient
    {
        private const string UrlAutenticacion = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/Autenticacion/Autenticacion.svc";
        private const string SoapActionAutentica = "http://DescargaMasivaTerceros.gob.mx/IAutenticacion/Autentica";

        // Corregido dos veces con pruebas reales: (1) el nombre correcto del servicio es
        // "SolicitaDescarga" (no "SolicitudDescarga"), sin carpeta de por medio; (2) la
        // operacion YA NO se llama "SolicitaDescarga" a secas -- se dividio en
        // SolicitaDescargaEmitidos/SolicitaDescargaRecibidos/SolicitaDescargaFolio (confirmado
        // bajando el WSDL/XSD real del servicio, ?wsdl y ?xsd=xsd0, no por otra prueba a ciegas).
        // Aqui solo se implementa RECIBIDOS -- Emitidos/Folio quedan para cuando se necesiten.
        private const string UrlSolicitaDescarga = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/SolicitaDescargaService.svc";
        private const string SoapActionSolicitaDescargaRecibidos = "http://DescargaMasivaTerceros.sat.gob.mx/ISolicitaDescargaService/SolicitaDescargaRecibidos";
        private const string ElementoSolicitaDescargaRecibidos = "SolicitaDescargaRecibidos";
        private const string NsDescarga = "http://DescargaMasivaTerceros.sat.gob.mx";
        private const string NsSoap = "http://schemas.xmlsoap.org/soap/envelope/";

        // Para las siguientes fases (VerificaSolicitud, Descarga) -- anotados ahora que ya se
        // verificaron contra documentacion externa, para no tener que volver a buscarlos.
        // OJO: Descarga vive en un host DISTINTO (cfdidescargamasiva, sin "solicitud").
        private const string UrlVerificaSolicitud = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/VerificaSolicitudDescargaService.svc";
        private const string SoapActionVerificaSolicitud = "http://DescargaMasivaTerceros.sat.gob.mx/IVerificaSolicitudDescargaService/VerificaSolicitudDescarga";
        private const string UrlDescarga = "https://cfdidescargamasiva.clouda.sat.gob.mx/DescargaMasivaService.svc";
        private const string SoapActionDescarga = "http://DescargaMasivaTerceros.sat.gob.mx/IDescargaMasivaTercerosService/Descargar";

        public static async Task<SatAutenticacionResultado> AutenticarAsync(X509Certificate2 cert, RSA llavePrivada)
        {
            var sobre = SatFirmaXml.FirmarAutentica(cert, llavePrivada);
            string xmlSobre = sobre.OuterXml;

            using (var http = new HttpClient())
            {
                var contenido = new StringContent(xmlSobre, Encoding.UTF8, "text/xml");
                contenido.Headers.Remove("Content-Type");
                contenido.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("SOAPAction", SoapActionAutentica);

                HttpResponseMessage resp;
                try
                {
                    resp = await http.PostAsync(UrlAutenticacion, contenido).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new SatAutenticacionResultado { Exito = false, Error = "Error de red: " + ex.Message };
                }

                string cuerpoResp = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return new SatAutenticacionResultado
                    {
                        Exito = false,
                        Error = "HTTP " + (int)resp.StatusCode + " " + resp.StatusCode + " -- el SAT rechazo la solicitud (revisar Content-Id/mensaje SOAP Fault abajo).",
                        RespuestaCruda = cuerpoResp
                    };
                }

                string token = ExtraerToken(cuerpoResp);
                if (string.IsNullOrEmpty(token))
                {
                    return new SatAutenticacionResultado
                    {
                        Exito = false,
                        Error = "HTTP 200 pero no se encontro token en la respuesta -- revisar RespuestaCruda.",
                        RespuestaCruda = cuerpoResp
                    };
                }

                return new SatAutenticacionResultado { Exito = true, Token = token, RespuestaCruda = cuerpoResp };
            }
        }

        // La respuesta trae <AutenticaResult>TOKEN</AutenticaResult> dentro del Body -- se
        // busca por nombre local para no depender del prefijo de namespace que use el SAT.
        private static string ExtraerToken(string xmlRespuesta)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlRespuesta);
                var nodo = doc.GetElementsByTagName("AutenticaResult");
                if (nodo.Count > 0) return nodo[0].InnerText;
                return null;
            }
            catch
            {
                return null;
            }
        }

        // ESTA llamada SI consume del limite diario de solicitudes del SAT. A diferencia de lo
        // que se penso originalmente, SI lleva firma XML-DSig propia (ver SatFirmaXml.
        // FirmarSolicitud) -- ademas del token de Autenticacion, que va por header HTTP
        // Authorization ("WRAP access_token=..."). Confirmado contra el manual oficial del SAT.
        //
        // rfcEmisor: para descargar EMITIDOS (documentos que TU generaste).
        // rfcReceptor: para descargar RECIBIDOS (documentos que te generaron proveedores).
        // Se debe indicar exactamente uno de los dos.
        public static async Task<SatSolicitaDescargaResultado> SolicitarDescargaAsync(
            X509Certificate2 cert, RSA llavePrivada, string token,
            string rfcSolicitante, string rfcEmisor, string rfcReceptor,
            DateTime desde, DateTime hasta, string tipoSolicitud = "CFDI")
        {
            if (string.IsNullOrEmpty(rfcEmisor) == string.IsNullOrEmpty(rfcReceptor))
                throw new ArgumentException("Indica exactamente uno: rfcEmisor (EMITIDOS) o rfcReceptor (RECIBIDOS), no ambos ni ninguno.");

            var doc = new XmlDocument();
            var envelope = doc.CreateElement("s", "Envelope", NsSoap);
            doc.AppendChild(envelope);
            envelope.AppendChild(doc.CreateElement("s", "Header", NsSoap));

            var body = doc.CreateElement("s", "Body", NsSoap);
            envelope.AppendChild(body);
            var solicitaDescarga = doc.CreateElement(null, ElementoSolicitaDescargaRecibidos, NsDescarga);
            body.AppendChild(solicitaDescarga);

            var solicitudFirmada = SatFirmaXml.FirmarSolicitud(
                doc, cert, llavePrivada, rfcSolicitante, rfcEmisor, rfcReceptor, desde, hasta, tipoSolicitud);
            solicitaDescarga.AppendChild(solicitudFirmada);

            using (var http = new HttpClient())
            {
                var contenido = new StringContent(doc.OuterXml, Encoding.UTF8, "text/xml");
                contenido.Headers.Remove("Content-Type");
                contenido.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("SOAPAction", "\"" + SoapActionSolicitaDescargaRecibidos + "\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "WRAP access_token=\"" + token + "\"");

                HttpResponseMessage resp;
                try
                {
                    resp = await http.PostAsync(UrlSolicitaDescarga, contenido).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new SatSolicitaDescargaResultado { Exito = false, Error = "Error de red: " + ex.Message };
                }

                string cuerpoResp = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return new SatSolicitaDescargaResultado
                    {
                        Exito = false,
                        Error = "HTTP " + (int)resp.StatusCode + " " + resp.StatusCode,
                        RespuestaCruda = cuerpoResp
                    };
                }

                try
                {
                    var respDoc = new XmlDocument();
                    respDoc.LoadXml(cuerpoResp);
                    var nodo = respDoc.GetElementsByTagName("SolicitaDescargaRecibidosResult");
                    if (nodo.Count == 0)
                        return new SatSolicitaDescargaResultado { Exito = false, Error = "HTTP 200 sin SolicitaDescargaRecibidosResult -- revisar RespuestaCruda.", RespuestaCruda = cuerpoResp };

                    var el = (XmlElement)nodo[0];
                    string codEstatus = el.GetAttribute("CodEstatus");
                    string idSolicitud = el.GetAttribute("IdSolicitud");
                    string mensaje = el.GetAttribute("Mensaje");

                    // CodEstatus "5000" = aceptada. Cualquier otro codigo es rechazo/error del SAT.
                    bool ok = codEstatus == "5000" && !string.IsNullOrEmpty(idSolicitud);
                    return new SatSolicitaDescargaResultado
                    {
                        Exito = ok,
                        IdSolicitud = idSolicitud,
                        CodEstatus = codEstatus,
                        Mensaje = mensaje,
                        Error = ok ? null : ("CodEstatus=" + codEstatus + " Mensaje=" + mensaje),
                        RespuestaCruda = cuerpoResp
                    };
                }
                catch (Exception ex)
                {
                    return new SatSolicitaDescargaResultado { Exito = false, Error = "No se pudo parsear la respuesta: " + ex.Message, RespuestaCruda = cuerpoResp };
                }
            }
        }

        // Solo consulta estatus -- no gasta cupo diario (ese limite es sobre SolicitaDescarga,
        // no sobre cuantas veces preguntas si ya esta lista). Se puede llamar tan seguido como
        // se quiera, aunque no tiene caso hacerlo mas de cada varios minutos: el SAT tarda de
        // minutos a horas en terminar de procesar una solicitud.
        public static async Task<SatVerificaSolicitudResultado> VerificarSolicitudAsync(
            X509Certificate2 cert, RSA llavePrivada, string token, string idSolicitud, string rfcSolicitante)
        {
            var doc = new XmlDocument();
            var envelope = doc.CreateElement("s", "Envelope", NsSoap);
            doc.AppendChild(envelope);
            envelope.AppendChild(doc.CreateElement("s", "Header", NsSoap));

            var body = doc.CreateElement("s", "Body", NsSoap);
            envelope.AppendChild(body);
            var verifica = doc.CreateElement(null, "VerificaSolicitudDescarga", NsDescarga);
            body.AppendChild(verifica);

            var solicitudFirmada = SatFirmaXml.FirmarVerificaSolicitud(doc, cert, llavePrivada, idSolicitud, rfcSolicitante);
            verifica.AppendChild(solicitudFirmada);

            using (var http = new HttpClient())
            {
                var contenido = new StringContent(doc.OuterXml, Encoding.UTF8, "text/xml");
                contenido.Headers.Remove("Content-Type");
                contenido.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("SOAPAction", "\"" + SoapActionVerificaSolicitud + "\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "WRAP access_token=\"" + token + "\"");

                HttpResponseMessage resp;
                try
                {
                    resp = await http.PostAsync(UrlVerificaSolicitud, contenido).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new SatVerificaSolicitudResultado { Exito = false, Error = "Error de red: " + ex.Message };
                }

                string cuerpoResp = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return new SatVerificaSolicitudResultado
                    {
                        Exito = false,
                        Error = "HTTP " + (int)resp.StatusCode + " " + resp.StatusCode,
                        RespuestaCruda = cuerpoResp
                    };
                }

                try
                {
                    var respDoc = new XmlDocument();
                    respDoc.LoadXml(cuerpoResp);
                    var nodo = respDoc.GetElementsByTagName("VerificaSolicitudDescargaResult");
                    if (nodo.Count == 0)
                        return new SatVerificaSolicitudResultado { Exito = false, Error = "HTTP 200 sin VerificaSolicitudDescargaResult -- revisar RespuestaCruda.", RespuestaCruda = cuerpoResp };

                    var el = (XmlElement)nodo[0];
                    var idsPaquetes = new System.Collections.Generic.List<string>();
                    foreach (XmlNode hijo in el.ChildNodes)
                        if (hijo is XmlElement he && he.LocalName == "IdsPaquetes")
                            idsPaquetes.Add(he.InnerText);

                    return new SatVerificaSolicitudResultado
                    {
                        Exito = true,
                        CodEstatus = el.GetAttribute("CodEstatus"),
                        EstadoSolicitud = el.GetAttribute("EstadoSolicitud"),
                        CodigoEstadoSolicitud = el.GetAttribute("CodigoEstadoSolicitud"),
                        NumeroCFDIs = el.GetAttribute("NumeroCFDIs"),
                        Mensaje = el.GetAttribute("Mensaje"),
                        IdsPaquetes = idsPaquetes,
                        RespuestaCruda = cuerpoResp
                    };
                }
                catch (Exception ex)
                {
                    return new SatVerificaSolicitudResultado { Exito = false, Error = "No se pudo parsear la respuesta: " + ex.Message, RespuestaCruda = cuerpoResp };
                }
            }
        }

        // OJO: un paquete solo se puede descargar 2 veces en total (CodEstatus 5008 si se agota),
        // y solo vive 72 horas desde que VerificaSolicitud lo reporto listo (CodEstatus 5007 si
        // ya vencio) -- confirmado contra el manual oficial. No llamar esto "por si las dudas".
        public static async Task<SatDescargaResultado> DescargarAsync(
            X509Certificate2 cert, RSA llavePrivada, string token, string idPaquete, string rfcSolicitante)
        {
            var doc = new XmlDocument();
            var envelope = doc.CreateElement("s", "Envelope", NsSoap);
            doc.AppendChild(envelope);
            envelope.AppendChild(doc.CreateElement("s", "Header", NsSoap));

            var body = doc.CreateElement("s", "Body", NsSoap);
            envelope.AppendChild(body);
            var peticionEntrada = doc.CreateElement(null, "PeticionDescargaMasivaTercerosEntrada", NsDescarga);
            body.AppendChild(peticionEntrada);

            var peticionFirmada = SatFirmaXml.FirmarPeticionDescarga(doc, cert, llavePrivada, idPaquete, rfcSolicitante);
            peticionEntrada.AppendChild(peticionFirmada);

            using (var http = new HttpClient())
            {
                var contenido = new StringContent(doc.OuterXml, Encoding.UTF8, "text/xml");
                contenido.Headers.Remove("Content-Type");
                contenido.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("SOAPAction", "\"" + SoapActionDescarga + "\"");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "WRAP access_token=\"" + token + "\"");

                HttpResponseMessage resp;
                try
                {
                    resp = await http.PostAsync(UrlDescarga, contenido).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new SatDescargaResultado { Exito = false, Error = "Error de red: " + ex.Message };
                }

                string cuerpoResp = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return new SatDescargaResultado
                    {
                        Exito = false,
                        Error = "HTTP " + (int)resp.StatusCode + " " + resp.StatusCode,
                        RespuestaCruda = cuerpoResp
                    };
                }

                try
                {
                    var respDoc = new XmlDocument();
                    respDoc.LoadXml(cuerpoResp);

                    // El estatus real (CodEstatus/Mensaje) viene en el HEADER del SOAP de
                    // respuesta ("<h:respuesta CodEstatus=... Mensaje=.../>", CON prefijo), no
                    // en el Body -- distinto a SolicitaDescarga/VerificaSolicitud. Confirmado
                    // en vivo: GetElementsByTagName("respuesta") no matcheaba "h:respuesta"
                    // (empareja por nombre calificado completo, no por LocalName) -- corregido
                    // buscando por LocalName, que sí ignora el prefijo.
                    string codEstatus = null, mensaje = null;
                    foreach (XmlElement elResp in respDoc.GetElementsByTagName("*"))
                    {
                        if (elResp.LocalName != "respuesta") continue;
                        codEstatus = elResp.GetAttribute("CodEstatus");
                        mensaje = elResp.GetAttribute("Mensaje");
                        break;
                    }

                    var nodoPaquete = respDoc.GetElementsByTagName("Paquete");
                    if (nodoPaquete.Count == 0)
                    {
                        return new SatDescargaResultado
                        {
                            Exito = false,
                            CodEstatus = codEstatus,
                            Mensaje = mensaje,
                            Error = "Sin <Paquete> en la respuesta. CodEstatus=" + codEstatus + " Mensaje=" + mensaje,
                            RespuestaCruda = cuerpoResp
                        };
                    }

                    byte[] zip = Convert.FromBase64String(nodoPaquete[0].InnerText);
                    return new SatDescargaResultado
                    {
                        Exito = true,
                        CodEstatus = codEstatus,
                        Mensaje = mensaje,
                        PaqueteZip = zip,
                        RespuestaCruda = cuerpoResp
                    };
                }
                catch (Exception ex)
                {
                    return new SatDescargaResultado { Exito = false, Error = "No se pudo parsear la respuesta: " + ex.Message, RespuestaCruda = cuerpoResp };
                }
            }
        }
    }

    internal sealed class SatDescargaResultado
    {
        public bool Exito;
        public string CodEstatus;
        public string Mensaje;
        public byte[] PaqueteZip;
        public string Error;
        public string RespuestaCruda;
    }

    internal sealed class SatVerificaSolicitudResultado
    {
        public bool Exito;
        // EstadoSolicitud (documentado por el SAT): 1=Aceptada, 2=EnProceso, 3=Terminada, 4=Error, 5=Rechazada, 6=Vencida
        public string EstadoSolicitud;
        public string CodigoEstadoSolicitud;
        public string CodEstatus;
        public string NumeroCFDIs;
        public string Mensaje;
        public System.Collections.Generic.List<string> IdsPaquetes = new System.Collections.Generic.List<string>();
        public string Error;
        public string RespuestaCruda;
    }
}
