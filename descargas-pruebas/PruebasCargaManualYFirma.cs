using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Carga manual de XML/ZIP y vigencia de la e.firma. Usan la base desechable; todo dato es ficticio.
    public class CargaManualTests : IClassFixture<BaseDesechable>, IDisposable
    {
        private readonly BaseDesechable _bd;
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "carga_manual_" + Guid.NewGuid().ToString("N"));
        public CargaManualTests(BaseDesechable bd) { _bd = bd; Directory.CreateDirectory(_tmp); }
        public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

        private const string RfcEmpresa = "AAA010101AAA";

        private static string Cfdi(Guid uuid, string emisor, string receptor) =>
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" xmlns:tfd=\"http://www.sat.gob.mx/TimbreFiscalDigital\" Version=\"4.0\" Serie=\"F\" Folio=\"7\" " +
            "Fecha=\"2026-03-15T10:00:00\" FormaPago=\"03\" MetodoPago=\"PUE\" SubTotal=\"100.00\" Moneda=\"MXN\" Total=\"116.00\" TipoDeComprobante=\"I\">" +
            "<cfdi:Emisor Rfc=\"" + emisor + "\" Nombre=\"Emisor Ficticio\" RegimenFiscal=\"601\"/>" +
            "<cfdi:Receptor Rfc=\"" + receptor + "\" Nombre=\"Receptor Ficticio\" UsoCFDI=\"G03\"/>" +
            "<cfdi:Conceptos><cfdi:Concepto ClaveProdServ=\"01010101\" Cantidad=\"1\" ClaveUnidad=\"H87\" Descripcion=\"Servicio\" ValorUnitario=\"100.00\" Importe=\"100.00\" ObjetoImp=\"02\">" +
            "<cfdi:Impuestos><cfdi:Traslados><cfdi:Traslado Base=\"100.00\" Impuesto=\"002\" TipoFactor=\"Tasa\" TasaOCuota=\"0.160000\" Importe=\"16.00\"/></cfdi:Traslados></cfdi:Impuestos></cfdi:Concepto></cfdi:Conceptos>" +
            "<cfdi:Impuestos TotalImpuestosTrasladados=\"16.00\"><cfdi:Traslados><cfdi:Traslado Base=\"100.00\" Impuesto=\"002\" TipoFactor=\"Tasa\" TasaOCuota=\"0.160000\" Importe=\"16.00\"/></cfdi:Traslados></cfdi:Impuestos>" +
            "<cfdi:Complemento><tfd:TimbreFiscalDigital Version=\"1.1\" UUID=\"" + uuid.ToString().ToUpperInvariant() + "\" FechaTimbrado=\"2026-03-15T10:00:05\" RfcProvCertif=\"SPR190613I52\"/></cfdi:Complemento>" +
            "</cfdi:Comprobante>";

        private EmpresaFila Empresa() => new EmpresaFila { RFC = RfcEmpresa, Nombre = "Prueba", Activa = true, CarpetaXml = Path.Combine(_tmp, "xml"), EstructuraCarpetas = "Plana", PlantillaNombreArchivo = "{UUID}" };

        [Fact]
        public async Task XmlSueltoYZip_SeCargan_NoSeDuplican_YSeRechazaLoAjeno()
        {
            if (!_bd.Disponible) return;
            var empresa = Empresa();
            var u1 = Guid.NewGuid(); var u2 = Guid.NewGuid(); var u3 = Guid.NewGuid();
            string suelto = Path.Combine(_tmp, "recibido.xml");
            File.WriteAllText(suelto, Cfdi(u1, "BBB010101BBB", RfcEmpresa));
            string zipRuta = Path.Combine(_tmp, "paquete.zip");
            using (var z = ZipFile.Open(zipRuta, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("emitido.xml").Open())) w.Write(Cfdi(u2, RfcEmpresa, "CCC010101CCC"));
                using (var w = new StreamWriter(z.CreateEntry("ajeno.xml").Open())) w.Write(Cfdi(u3, "DDD010101DDD", "EEE010101EEE"));
                using (var w = new StreamWriter(z.CreateEntry("leeme.txt").Open())) w.Write("no es xml");
            }
            string basura = Path.Combine(_tmp, "roto.xml");
            File.WriteAllText(basura, "<esto no es un cfdi>");

            var filas = await CargaManual.ImportarAsync(_bd.Conn, empresa, new[] { suelto, zipRuta, basura });
            Assert.Contains(filas, f => f.UUID == u1.ToString().ToUpperInvariant() && f.Resultado == ResultadoCarga.Nuevo && f.Tipo == "Recibidos");
            Assert.Contains(filas, f => f.UUID == u2.ToString().ToUpperInvariant() && f.Resultado == ResultadoCarga.Nuevo && f.Tipo == "Emitidos");
            Assert.Contains(filas, f => f.UUID == u3.ToString().ToUpperInvariant() && f.Resultado == ResultadoCarga.OtraEmpresa);
            Assert.Contains(filas, f => f.Archivo == "roto.xml" && f.Resultado == ResultadoCarga.NoEsCfdi);
            Assert.Equal(2, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiRecibido WHERE UUID IN ('" + u1 + "','" + u2 + "')"));
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiRecibido WHERE UUID = '" + u3 + "'"));
            Assert.True(File.Exists(Path.Combine(empresa.CarpetaXml, u1.ToString() + ".xml")) || Directory.EnumerateFiles(empresa.CarpetaXml, "*.xml", SearchOption.AllDirectories).Any());

            // Mismos archivos otra vez: ya estaban, no se duplica nada.
            var otra = await CargaManual.ImportarAsync(_bd.Conn, empresa, new[] { suelto, zipRuta });
            Assert.Contains(otra, f => f.UUID == u1.ToString().ToUpperInvariant() && f.Resultado == ResultadoCarga.YaExistia);
            Assert.Contains(otra, f => f.UUID == u2.ToString().ToUpperInvariant() && f.Resultado == ResultadoCarga.YaExistia);
            Assert.Equal(2, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiRecibido WHERE UUID IN ('" + u1 + "','" + u2 + "')"));
        }

        private string Cer(DateTime desde, DateTime hasta)
        {
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=Prueba", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (var c = req.CreateSelfSigned(desde, hasta))
                {
                    string ruta = Path.Combine(_tmp, Guid.NewGuid().ToString("N") + ".cer");
                    File.WriteAllBytes(ruta, c.Export(X509ContentType.Cert));
                    return ruta;
                }
            }
        }

        [Theory]
        [InlineData(-400, -1, 2)]   // ya vencio
        [InlineData(-300, 5, 2)]    // vence en menos de 7 dias
        [InlineData(-300, 20, 1)]     // vence en menos de 30 dias
        [InlineData(-300, 400, 0)]       // vigente
        public void Salud_AvisaDeLaVigenciaDeLaEfirma(int diasDesde, int diasHasta, int esperado)
        {
            if (!_bd.Disponible) return;
            var empresa = new EmpresaFila { RFC = RfcEmpresa, Nombre = "Prueba", Activa = true, AnioInicioDescargas = 2026, RutaCer = Cer(DateTime.Now.AddDays(diasDesde), DateTime.Now.AddDays(diasHasta + 0.5)) };
            var h = Salud.Evaluar(_bd.Conn, empresa).Single(x => x.Tema == "e.firma");
            Assert.Equal(esperado, (int)h.Nivel);
        }

        [Fact]
        public void Salud_AvisaSiFaltaElCer()
        {
            if (!_bd.Disponible) return;
            var empresa = new EmpresaFila { RFC = RfcEmpresa, Nombre = "Prueba", Activa = true, AnioInicioDescargas = 2026, RutaCer = Path.Combine(_tmp, "no_existe.cer") };
            Assert.Equal(NivelSalud.Aviso, Salud.Evaluar(_bd.Conn, empresa).Single(x => x.Tema == "e.firma").Nivel);
        }
    }
}
