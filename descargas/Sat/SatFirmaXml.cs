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

// SatFirmaXml.cs
//
// Arma y firma el sobre SOAP de "Autentica" del Servicio de Descarga Masiva de CFDI del SAT,
// usando la FIEL (certificado .cer + llave privada .key) del contribuyente. La firma sigue el
// perfil WS-Security que el SAT exige: un nodo <Timestamp> con Id propio, un
// <BinarySecurityToken> con el certificado en Base64, y un <Signature> XML-DSig (canonicalizacion
// exclusiva, rsa-sha1) cuya <KeyInfo> apunta al BinarySecurityToken por referencia (no incluye
// el certificado otra vez dentro de la firma).
//
// IMPORTANTE (corregido tras revisar el protocolo con mas cuidado, antes de gastar una
// solicitud real): la FIEL SOLO firma la llamada a Autenticacion.svc, que regresa un token
// temporal (~5 min). SolicitaDescarga/VerificaSolicitud/Descarga NO van firmados con la FIEL --
// van con el token del paso anterior en el header HTTP "Authorization: WRAP access_token=...".
// La primera version de este archivo firmaba directamente un sobre "SolicitaDescarga", que
// apuntaba al servicio equivocado -- se detecto antes de mandar nada real al SAT.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace BrosLMV.Descargas.Sat
{
    internal static class SatFirmaXml
    {
        private const string NsSoap = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string NsWsseSecExt = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        private const string NsWsseUtility = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        private const string ValueTypeX509 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
        private const string EncodingTypeBase64 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

        // Carga el certificado (.cer, DER) y la llave privada (.key, PKCS#8 cifrado con la
        // contrasena de la FIEL) tal como el SAT los entrega -- no son un .pfx combinado, hay
        // que unirlos a mano. X509Certificate2 no puede traer la llave sola desde un .key crudo,
        // asi que se arma un RSA en memoria a partir del PKCS#8 descifrado y se asocia por fuera.
        public static X509Certificate2 CargarFiel(string rutaCer, string rutaKey, string password, out RSA llavePrivada)
        {
            var cert = new X509Certificate2(rutaCer);

            byte[] keyBytes = System.IO.File.ReadAllBytes(rutaKey);
            var rsa = RSA.Create();
            rsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(password), keyBytes, out _);

            llavePrivada = rsa;
            return cert;
        }

        private const string NsAutentica = "http://DescargaMasivaTerceros.gob.mx";
        private const string NsDescarga = "http://DescargaMasivaTerceros.sat.gob.mx"; // OJO: dominio distinto a NsAutentica (sin "sat.")

        // Arma el sobre SOAP de Autentica, ya firmado, listo para mandar por HTTP a
        // Autenticacion.svc. Es la UNICA llamada del protocolo que lleva firma XML-DSig con la
        // FIEL -- el resultado es un token que las demas llamadas usan por header HTTP.
        public static XmlDocument FirmarAutentica(X509Certificate2 cert, RSA llavePrivada)
        {
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;

            var envelope = doc.CreateElement("s", "Envelope", NsSoap);
            doc.AppendChild(envelope);

            var header = doc.CreateElement("s", "Header", NsSoap);
            envelope.AppendChild(header);

            var security = doc.CreateElement("o", "Security", NsWsseSecExt);
            security.SetAttribute("xmlns:s", NsSoap); // fuerza que el prefijo s tambien resuelva dentro del subarbol firmado
            header.AppendChild(security);

            // --- Timestamp con Id "_0": es el nodo que realmente se firma ---
            const string timestampId = "_0";
            var timestamp = doc.CreateElement("u", "Timestamp", NsWsseUtility);
            var idAttr = doc.CreateAttribute("u", "Id", NsWsseUtility);
            idAttr.Value = timestampId;
            timestamp.Attributes.Append(idAttr);

            var creado = DateTime.UtcNow;
            var expira = creado.AddMinutes(5);
            var created = doc.CreateElement("u", "Created", NsWsseUtility);
            created.InnerText = creado.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var expires = doc.CreateElement("u", "Expires", NsWsseUtility);
            expires.InnerText = expira.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            timestamp.AppendChild(created);
            timestamp.AppendChild(expires);
            security.AppendChild(timestamp);

            // --- BinarySecurityToken: el certificado en Base64, con su propio Id ---
            const string bstId = "BST";
            var bst = doc.CreateElement("o", "BinarySecurityToken", NsWsseSecExt);
            var bstIdAttr = doc.CreateAttribute("u", "Id", NsWsseUtility);
            bstIdAttr.Value = bstId;
            bst.Attributes.Append(bstIdAttr);
            bst.SetAttribute("ValueType", ValueTypeX509);
            bst.SetAttribute("EncodingType", EncodingTypeBase64);
            bst.InnerText = Convert.ToBase64String(cert.GetRawCertData());
            security.AppendChild(bst);

            // --- Cuerpo: <Autentica/> vacio -- toda la informacion util va en el Security header ---
            var body = doc.CreateElement("s", "Body", NsSoap);
            envelope.AppendChild(body);
            var autentica = doc.CreateElement(null, "Autentica", NsAutentica);
            body.AppendChild(autentica);

            // --- Firma XML-DSig sobre el Timestamp, con KeyInfo apuntando al BST ---
            var signedXml = new SignedXmlConId(doc) { SigningKey = llavePrivada };
            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            var reference = new Reference("#" + timestampId);
            reference.AddTransform(new XmlDsigExcC14NTransform());
            reference.DigestMethod = SignedXml.XmlDsigSHA1Url;
            signedXml.AddReference(reference);

            signedXml.KeyInfo.AddClause(new BinarySecurityTokenReferenceClause(bstId));

            signedXml.ComputeSignature();
            XmlElement signatureXml = signedXml.GetXml();
            security.AppendChild(doc.ImportNode(signatureXml, true));

            return doc;
        }

        // Firma el nodo <solicitud> de SolicitaDescarga -- a diferencia de Autentica, esta NO
        // es una firma WS-Security en el Header, es una firma XML-DSig "enveloped" clasica
        // (la misma familia que usa un CFDI para firmarse a si mismo) embebida DENTRO del
        // propio <solicitud>, confirmado contra el manual oficial del SAT ("Documentacion del
        // Servicio de Solicitud de Descarga Masiva de CFDI...", v1.2, mayo 2022):
        // canonicalizacion ESTANDAR (no exclusiva), transform "enveloped-signature", KeyInfo
        // con X509Data (IssuerSerial + certificado), no SecurityTokenReference.
        //
        // Se construye en un XmlDocument aparte (solicitud como raiz) para que el Reference
        // URI="" firme exactamente ese subarbol, y luego se importa ya firmado al sobre real --
        // evita cualquier diferencia de canonicalizacion entre "firmar" y "servir".
        // Corregido tras un segundo 500 real: el manual de 2022 documentaba RfcReceptor como
        // elemento anidado (<RfcReceptores><RfcReceptor>), pero el WSDL/XSD REAL vigente hoy
        // (consultado directo, ?xsd=xsd0) muestra que para la operacion SolicitaDescargaRecibidos
        // el tipo "SolicitudDescargaMasivaTerceroRecibidos" trae RfcReceptor como ATRIBUTO
        // simple -- la API se dividio en SolicitaDescargaEmitidos/Recibidos/Folio desde que se
        // escribio ese manual, y cada operacion tiene su propio tipo de request.
        public static XmlElement FirmarSolicitud(
            XmlDocument docDestino, X509Certificate2 cert, RSA llavePrivada,
            string rfcSolicitante, string rfcEmisor, string rfcReceptor,
            DateTime desde, DateTime hasta, string tipoSolicitud)
        {
            var docSolicitud = new XmlDocument { PreserveWhitespace = true };
            var solicitud = docSolicitud.CreateElement(null, "solicitud", NsDescarga);
            docSolicitud.AppendChild(solicitud);

            solicitud.SetAttribute("FechaInicial", desde.ToString("yyyy-MM-ddTHH:mm:ss"));
            solicitud.SetAttribute("FechaFinal", hasta.ToString("yyyy-MM-ddTHH:mm:ss"));
            if (!string.IsNullOrEmpty(rfcEmisor)) solicitud.SetAttribute("RfcEmisor", rfcEmisor);
            solicitud.SetAttribute("RfcSolicitante", rfcSolicitante);
            solicitud.SetAttribute("TipoSolicitud", tipoSolicitud);
            if (!string.IsNullOrEmpty(rfcReceptor)) solicitud.SetAttribute("RfcReceptor", rfcReceptor);
            // Confirmado tras un rechazo real (CodEstatus 301 "no se encuentren cancelados"):
            // para TipoSolicitud=CFDI (descarga de XML, a diferencia de Metadata) el SAT SOLO
            // acepta EstadoComprobante="Vigente" -- "Todos"/"Cancelados" son rechazados en la
            // descarga de XML (si tiene sentido: un CFDI cancelado no tiene XML descargable).
            if (tipoSolicitud == "CFDI") solicitud.SetAttribute("EstadoComprobante", "Vigente");

            FirmarElementoEnveloped(docSolicitud, solicitud, cert, llavePrivada);
            return (XmlElement)docDestino.ImportNode(solicitud, true);
        }

        // Firma el nodo <solicitud> de VerificaSolicitudDescarga -- mismo mecanismo de firma
        // enveloped que FirmarSolicitud (verificado con el mismo patron via WSDL/XSD real:
        // tipo "VerificaSolicitudDescargaMasivaTercero", atributos IdSolicitud+RfcSolicitante
        // solamente, mas el Signature en secuencia).
        public static XmlElement FirmarVerificaSolicitud(
            XmlDocument docDestino, X509Certificate2 cert, RSA llavePrivada,
            string idSolicitud, string rfcSolicitante)
        {
            var docSolicitud = new XmlDocument { PreserveWhitespace = true };
            var solicitud = docSolicitud.CreateElement(null, "solicitud", NsDescarga);
            docSolicitud.AppendChild(solicitud);

            solicitud.SetAttribute("IdSolicitud", idSolicitud);
            solicitud.SetAttribute("RfcSolicitante", rfcSolicitante);

            FirmarElementoEnveloped(docSolicitud, solicitud, cert, llavePrivada);
            return (XmlElement)docDestino.ImportNode(solicitud, true);
        }

        // Firma el nodo <peticionDescarga> de Descargar -- mismo mecanismo enveloped que
        // FirmarSolicitud/FirmarVerificaSolicitud (confirmado contra el manual oficial del SAT,
        // "Documentacion...Servicio de Descarga de Solicitudes Exitosas", ago 2018, v1.1).
        public static XmlElement FirmarPeticionDescarga(
            XmlDocument docDestino, X509Certificate2 cert, RSA llavePrivada,
            string idPaquete, string rfcSolicitante)
        {
            var docPeticion = new XmlDocument { PreserveWhitespace = true };
            var peticion = docPeticion.CreateElement(null, "peticionDescarga", NsDescarga);
            docPeticion.AppendChild(peticion);

            peticion.SetAttribute("IdPaquete", idPaquete);
            peticion.SetAttribute("RfcSolicitante", rfcSolicitante);

            FirmarElementoEnveloped(docPeticion, peticion, cert, llavePrivada);
            return (XmlElement)docDestino.ImportNode(peticion, true);
        }

        // Firma XML-DSig "enveloped" clasica (la misma familia que usa un CFDI para firmarse a
        // si mismo), compartida por FirmarSolicitud y FirmarVerificaSolicitud: canonicalizacion
        // ESTANDAR (no exclusiva), transform "enveloped-signature", KeyInfo con X509Data
        // (IssuerSerial + certificado), no SecurityTokenReference (esa es solo para Autentica).
        private static void FirmarElementoEnveloped(XmlDocument docElemento, XmlElement elemento, X509Certificate2 cert, RSA llavePrivada)
        {
            var signedXml = new SignedXml(docElemento) { SigningKey = llavePrivada };
            signedXml.SignedInfo.CanonicalizationMethod = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

            var reference = new Reference("");
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.DigestMethod = SignedXml.XmlDsigSHA1Url;
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            var x509Data = new KeyInfoX509Data();
            // X509SerialNumber va en decimal, no en hex -- GetSerialNumber() de .NET regresa
            // los bytes en little-endian, por eso isBigEndian:false aqui (no es un bug, es el
            // orden nativo que .NET usa para este campo especifico).
            string serialDecimal = new System.Numerics.BigInteger(cert.GetSerialNumber(), isUnsigned: true, isBigEndian: false).ToString();
            x509Data.AddIssuerSerial(cert.IssuerName.Name, serialDecimal);
            x509Data.AddCertificate(cert);
            keyInfo.AddClause(x509Data);
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();
            elemento.AppendChild(docElemento.ImportNode(signedXml.GetXml(), true));
        }

        // Verificacion LOCAL de la firma (contra el certificado embebido en el propio
        // BinarySecurityToken, no contra ninguna autoridad) -- solo confirma que el XML-DSig
        // esta bien formado y que la firma corresponde al contenido del Timestamp, antes de
        // gastar una solicitud real contra el SAT.
        public static bool VerificarFirma(XmlDocument doc)
        {
            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("o", NsWsseSecExt);
            nsMgr.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var bstNode = doc.SelectSingleNode("//o:BinarySecurityToken", nsMgr) as XmlElement;
            var sigNode = doc.SelectSingleNode("//ds:Signature", nsMgr) as XmlElement;
            if (bstNode == null || sigNode == null) return false;

            var cert = new X509Certificate2(Convert.FromBase64String(bstNode.InnerText));
            var signedXml = new SignedXmlConId(doc);
            signedXml.LoadXml(sigNode);
            return signedXml.CheckSignature(cert, verifySignatureOnly: true);
        }

        // Verificacion LOCAL de la firma enveloped de <solicitud> (distinta de VerificarFirma:
        // ahi el certificado vive en un BinarySecurityToken WS-Security, aqui vive directo en
        // KeyInfo/X509Data/X509Certificate).
        public static bool VerificarFirmaSolicitud(XmlElement solicitud)
        {
            var nsMgr = new XmlNamespaceManager(solicitud.OwnerDocument.NameTable);
            nsMgr.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var sigNode = solicitud.SelectSingleNode("ds:Signature", nsMgr) as XmlElement;
            var certNode = solicitud.SelectSingleNode(".//ds:X509Certificate", nsMgr) as XmlElement;
            if (sigNode == null || certNode == null) return false;

            var cert = new X509Certificate2(Convert.FromBase64String(certNode.InnerText));
            var signedXml = new SignedXml(solicitud.OwnerDocument);
            signedXml.LoadXml(sigNode);
            return signedXml.CheckSignature(cert, verifySignatureOnly: true);
        }

        // .NET solo reconoce como "Id" de XML-DSig atributos llamados exactamente "Id" sin
        // namespace por defecto. El SAT (como todo WS-Security) usa wsu:Id -- hay que ensenarle
        // a SignedXml a resolver "#_0" contra ese atributo con namespace.
        private sealed class SignedXmlConId : SignedXml
        {
            public SignedXmlConId(XmlDocument doc) : base(doc) { }

            public override XmlElement GetIdElement(XmlDocument document, string id)
            {
                var porDefecto = base.GetIdElement(document, id);
                if (porDefecto != null) return porDefecto;

                var nsMgr = new XmlNamespaceManager(document.NameTable);
                nsMgr.AddNamespace("u", NsWsseUtility);
                return document.SelectSingleNode("//*[@u:Id='" + id + "']", nsMgr) as XmlElement;
            }
        }

        // KeyInfo que NO repite el certificado (ya va en el BinarySecurityToken) -- solo
        // referencia su Id, como exige el perfil X.509 Token de WS-Security.
        private sealed class BinarySecurityTokenReferenceClause : KeyInfoClause
        {
            private readonly string _bstId;
            public BinarySecurityTokenReferenceClause(string bstId) { _bstId = bstId; }

            // OJO: esto se anida DENTRO del <KeyInfo> que SignedXml.GetXml() ya arma por su
            // cuenta -- si aqui tambien se crea un <KeyInfo> propio queda duplicado
            // (<KeyInfo><KeyInfo>...</KeyInfo></KeyInfo>), que es justo el bug que se encontro
            // en la primera prueba estructural contra un certificado de prueba.
            public override XmlElement GetXml()
            {
                var doc = new XmlDocument();
                var str = doc.CreateElement("o", "SecurityTokenReference", NsWsseSecExt);
                var reference = doc.CreateElement("o", "Reference", NsWsseSecExt);
                reference.SetAttribute("URI", "#" + _bstId);
                reference.SetAttribute("ValueType", ValueTypeX509);
                str.AppendChild(reference);
                return str;
            }

            public override void LoadXml(XmlElement element)
            {
                throw new NotSupportedException("Solo se usa para generar, no para leer.");
            }
        }
    }
}
