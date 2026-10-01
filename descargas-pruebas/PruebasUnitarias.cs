using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Pruebas sin base de datos ni red: la logica pura que ya nos fallo en produccion.
    public class HuecosTests
    {
        private static DateTime D(int dia) => new DateTime(2026, 9, dia);

        [Fact]
        public void Dia17Fallido_Y_19Bueno_ElHuecoSeDetecta()
        {
            // El caso real reportado: el 17 fallo, el 19 salio bien. El sistema viejo seguia desde el 20.
            var cubiertos = new List<(DateTime, DateTime)> { (D(1), D(16).AddHours(23)), (D(19), D(20)) };
            var hueco = Huecos.PrimerHueco(cubiertos, D(1), D(20));
            Assert.NotNull(hueco);
            Assert.Equal(D(17), hueco.Value.Desde);
            Assert.Equal(D(18), hueco.Value.Hasta);
        }

        [Fact]
        public void TodoCubierto_NoHayHueco()
        {
            var cubiertos = new List<(DateTime, DateTime)> { (D(1), D(10)), (D(11), D(20)) };
            Assert.Null(Huecos.PrimerHueco(cubiertos, D(1), D(20)));
        }

        [Fact]
        public void SinNadaCubierto_ElHuecoEsTodoElPeriodo()
        {
            var hueco = Huecos.PrimerHueco(new List<(DateTime, DateTime)>(), D(5), D(9));
            Assert.Equal((D(5), D(9)), (hueco.Value.Desde, hueco.Value.Hasta));
        }

        [Fact]
        public void HuecoAlInicioYAlFinal()
        {
            Assert.Equal(D(1), Huecos.PrimerHueco(new List<(DateTime, DateTime)> { (D(3), D(9)) }, D(1), D(9)).Value.Desde);
            Assert.Equal(D(8), Huecos.PrimerHueco(new List<(DateTime, DateTime)> { (D(1), D(7)) }, D(1), D(9)).Value.Desde);
        }

        [Fact]
        public void Todos_ListaCadaHueco()
        {
            var cubiertos = new List<(DateTime, DateTime)> { (D(3), D(4)), (D(8), D(9)) };
            var huecos = Huecos.Todos(cubiertos, D(1), D(12));
            Assert.Equal(3, huecos.Count);
            Assert.Equal((D(1), D(2)), huecos[0]);
            Assert.Equal((D(5), D(7)), huecos[1]);
            Assert.Equal((D(10), D(12)), huecos[2]);
        }

        [Fact]
        public void RangosTraslapados_NoGeneranHuecos()
        {
            var cubiertos = new List<(DateTime, DateTime)> { (D(1), D(15)), (D(10), D(20)), (D(5), D(7)) };
            Assert.Null(Huecos.PrimerHueco(cubiertos, D(1), D(20)));
        }
    }

    public class ChunkerTests
    {
        [Fact]
        public void PartirEnMeses_AlineaAMesDeCalendario()
        {
            var tramos = SolicitudChunker.PartirEnMeses(new DateTime(2026, 1, 15), new DateTime(2026, 3, 10));
            Assert.Equal(3, tramos.Count);
            Assert.Equal(new DateTime(2026, 1, 15), tramos[0].Desde);
            Assert.Equal(new DateTime(2026, 1, 31, 23, 59, 59), tramos[0].Hasta);
            Assert.Equal(new DateTime(2026, 2, 1), tramos[1].Desde);
            Assert.Equal(new DateTime(2026, 3, 10, 23, 59, 59), tramos[2].Hasta);
        }
    }

    public class MetadataParserTests
    {
        [Fact]
        public void Parsea_VigenteYCancelado_YSaltaEncabezado()
        {
            string txt =
                "UUID~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion\n" +
                "11111111-1111-1111-1111-111111111111~AAA010101AAA~Emisor SA~BBB020202BBB~Receptor SA~PAC010101AAA~2026-09-01 10:00:00~2026-09-01 10:00:05~116.00~I~1~\n" +
                "22222222-2222-2222-2222-222222222222~AAA010101AAA~Emisor SA~BBB020202BBB~Receptor SA~PAC010101AAA~2026-09-02 11:00:00~2026-09-02 11:00:05~58.00~E~0~2026-09-05 08:00:00\n" +
                "basura sin formato\n";
            var filas = CfdiMetadataParser.Parsear(txt).ToList();
            Assert.Equal(2, filas.Count);
            Assert.Equal("Vigente", filas[0].EstatusSat);
            Assert.Equal("Cancelado", filas[1].EstatusSat);
            Assert.Equal(116.00m, filas[0].Total);
            Assert.NotNull(filas[1].FechaCancelacion);
        }
    }

    public class OrganizadorTests
    {
        private static CfdiParseado Cfdi() => new CfdiParseado
        {
            UUID = Guid.Parse("0a1a3d7b-9373-42f2-88bf-e834fffd196d"), RFCEmisor = "AAA010101AAA", RFCReceptor = "BBB020202BBB",
            Folio = "123", FechaEmision = new DateTime(2026, 2, 11), Total = 116m, TipoComprobante = "I"
        };

        [Fact]
        public void PlantillaConLlaves_ResuelveElUuid()
        {
            string ruta = OrganizadorArchivos.ResolverRutaArchivo(Path.GetTempPath(), "{UUID}", Cfdi());
            Assert.EndsWith("0a1a3d7b-9373-42f2-88bf-e834fffd196d.xml", ruta);
        }

        [Fact]
        public void PlantillaConCorchetes_TambienResuelveElUuid()
        {
            // Bug 22: una empresa se dio de alta con "[UUID]" y todos sus XML se llamaron literalmente "[UUID].xml".
            string ruta = OrganizadorArchivos.ResolverRutaArchivo(Path.GetTempPath(), "[UUID]", Cfdi());
            Assert.DoesNotContain("[UUID]", ruta);
            Assert.EndsWith("0a1a3d7b-9373-42f2-88bf-e834fffd196d.xml", ruta);
        }

        [Fact]
        public void PlantillaCompuesta_ConFolioYFecha()
        {
            string ruta = OrganizadorArchivos.ResolverRutaArchivo(Path.GetTempPath(), "{Fecha}_{Folio}_{UUID}", Cfdi());
            Assert.EndsWith("2026-02-11_123_0a1a3d7b-9373-42f2-88bf-e834fffd196d.xml", ruta);
        }

        [Fact]
        public void Carpetas_PorAnioYMes()
        {
            Assert.EndsWith(Path.Combine("2026", "02-Febrero"), OrganizadorArchivos.ResolverCarpeta("base", "AnioMes", "Recibidos", new DateTime(2026, 2, 11)));
            Assert.Equal("base", OrganizadorArchivos.ResolverCarpeta("base", "Plana", "Recibidos", new DateTime(2026, 2, 11)));
        }
    }

    public class RespaldoTests
    {
        [Fact]
        public void CifrarYDescifrar_IdaYVuelta()
        {
            byte[] archivo = Respaldo.Cifrar("{\"secreto\":\"FIEL de prueba ñ\"}", "una-contrasena-larga");
            Assert.DoesNotContain("secreto", System.Text.Encoding.UTF8.GetString(archivo)); // no queda legible
            Assert.Equal("{\"secreto\":\"FIEL de prueba ñ\"}", Respaldo.Descifrar(archivo, "una-contrasena-larga"));
        }

        [Fact]
        public void ContrasenaEquivocada_Falla()
        {
            byte[] archivo = Respaldo.Cifrar("hola", "una-contrasena-larga");
            var ex = Assert.Throws<InvalidDataException>(() => Respaldo.Descifrar(archivo, "otra-contrasena-mala"));
            Assert.Contains("incorrecta", ex.Message);
        }

        [Fact]
        public void ArchivoAlterado_Falla()
        {
            byte[] archivo = Respaldo.Cifrar("hola mundo", "una-contrasena-larga");
            archivo[archivo.Length - 1] ^= 0x01; // un solo bit
            Assert.Throws<InvalidDataException>(() => Respaldo.Descifrar(archivo, "una-contrasena-larga"));
        }

        [Fact]
        public void ArchivoQueNoEsRespaldo_Falla()
        {
            Assert.Throws<InvalidDataException>(() => Respaldo.Descifrar(new byte[100], "una-contrasena-larga"));
        }

        [Fact]
        public void ContrasenaCorta_SeRechaza()
        {
            Assert.Throws<ArgumentException>(() => Respaldo.Cifrar("x", "corta"));
        }
    }
}
