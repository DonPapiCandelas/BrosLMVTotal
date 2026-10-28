using System.Collections.Generic;
using System.Linq;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Lo que promete el editor de layouts: un formato nuevo se puede ensenar sin programar, y lo de la empresa gana a lo de fabrica.
    public class LayoutsTests
    {
        private static List<string[]> Csv(string texto) { return BancoTexto.LeerTexto(texto); }

        private const string Hsbc =
            "Movimientos de la cuenta 4012345678\n" +
            "Fecha Op;Detalle;Debe;Haber;Saldo contable\n" +
            "03/11/2026;PAGO PROVEEDOR;1,500.00;;8,500.00\n" +
            "04/11/2026;DEPOSITO CLIENTE;;2,000.00;10,500.00\n";

        private static Layout LayoutHsbc()
        {
            return new Layout
            {
                Id = "hsbc-csv", Banco = "HSBC", Nombre = "HSBC CSV", Origen = "empresa", Confianza = 3,
                Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { En = "encabezado", Contiene = new List<string> { "debe" } }, new Condicion { En = "encabezado", Contiene = new List<string> { "haber" } } } },
                Columnas = new Dictionary<string, List<string>>
                {
                    ["fecha"] = new List<string> { "Fecha Op" }, ["descripcion"] = new List<string> { "Detalle" }, ["cargo"] = new List<string> { "Debe" }, ["abono"] = new List<string> { "Haber" }, ["saldo"] = new List<string> { "Saldo contable" }
                }
            };
        }

        [Fact]
        public void SinLayoutUnFormatoNuevoNoSeReconoceYConLayoutSeLeeBien()
        {
            var filas = Csv(Hsbc);
            Assert.Equal("", BancoArchivo.Detectar(filas, "hsbc_nov.csv").Banco.Replace("HSBC", ""));   // por el nombre del archivo se sugiere HSBC pero con baja confianza
            Assert.True(BancoArchivo.Detectar(filas, "movs.csv").Confianza == 0);

            var det = BancoArchivo.Detectar(filas, "movs.csv", Catalogo.Combinar(new[] { LayoutHsbc() }));
            Assert.Equal("HSBC", det.Banco);
            Assert.Equal(3, det.Confianza);
            Assert.Equal("hsbc-csv", det.Layout.Id);
            Assert.Contains("4012345678", det.CuentasPreambulo);

            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            Assert.Empty(res.Errores);
            Assert.Equal(2, res.Movs.Count);
            Assert.Equal(1500m, res.Movs[0].Cargo);
            Assert.Equal(2000m, res.Movs[1].Abono);
            Assert.True(res.SaldoVerificado);
            Assert.Equal(0, res.RupturasSaldo);
        }

        [Fact]
        public void UnLayoutSinEncabezadoLeePorPosicion()
        {
            var l = new Layout
            {
                Id = "xyz-fijo", Banco = "XYZ", Nombre = "XYZ posicional", Origen = "empresa",
                Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { En = "archivo", Contiene = new List<string> { "xyzbank" } } } },
                Columnas = new Dictionary<string, List<string>> { ["fecha"] = new List<string> { "#1" }, ["descripcion"] = new List<string> { "#2" }, ["cargo"] = new List<string> { "#3" }, ["saldo"] = new List<string> { "#4" } }
            };
            var filas = Csv("03112026|PAGO LUZ|1500.00|8500.00\n04112026|PAGO AGUA|300.00|8200.00\n");
            var det = BancoArchivo.Detectar(filas, "xyzbank_nov.txt", Catalogo.Combinar(new[] { l }));
            Assert.Equal("XYZ", det.Banco);
            Assert.Equal(-1, det.Mapa.FilaEncabezado);
            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            Assert.Empty(res.Errores);
            Assert.Equal(2, res.Movs.Count);
            Assert.Equal(300m, res.Movs[1].Cargo);
        }

        [Fact]
        public void LoDeLaEmpresaSustituyeAlLayoutDeFabricaConElMismoId()
        {
            var de = Catalogo.DeFabrica().First(x => x.Id == "bbva-txt");
            var mio = Layout.DeDatos(de.ADatos());
            mio.Origen = "empresa"; mio.Version = 2; mio.Notas = new List<string> { "ajustado por la empresa" };
            var lista = Catalogo.Combinar(new[] { mio });
            Assert.Equal(Catalogo.DeFabrica().Count, lista.Count);
            Assert.Equal(2, lista.First(x => x.Id == "bbva-txt").Version);
        }

        [Fact]
        public void ElPorcentajeDeCoincidenciaEsParcialYElUmbralLoDecide()
        {
            var l = new Layout
            {
                Id = "x", Umbral = 100,
                Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { Contiene = new List<string> { "fecha" } }, new Condicion { Contiene = new List<string> { "inexistente" } } } }
            };
            var h = new Huella { Encabezado = "fecha|monto" };
            Assert.Equal(50, Catalogo.Porcentaje(l, h));
            Assert.False(new Coincidencia { Layout = l, Porcentaje = 50 }.Usable);
            l.Umbral = 50;
            Assert.True(new Coincidencia { Layout = l, Porcentaje = 50 }.Usable);
        }

        [Fact]
        public void UnLayoutSobreviveAIrYVenirComoJson()
        {
            var l = LayoutHsbc();
            var otro = Layout.DeDatos(l.ADatos());
            Assert.Equal(l.Id, otro.Id);
            Assert.Equal("Debe", otro.Columnas["cargo"][0]);
            Assert.Equal("haber", otro.Reconocer[0][1].Contiene[0]);
        }

        [Fact]
        public void TodosLosLayoutsDeFabricaTienenIdUnicoYSeReconocenPorAlgo()
        {
            var ids = Catalogo.DeFabrica().Select(x => x.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(Catalogo.DeFabrica(), l => Assert.NotEmpty(l.Reconocer));
            Assert.All(Catalogo.DeFabrica().Where(x => x.Formato == "pdf"), l => Assert.False(string.IsNullOrEmpty(l.Pdf.Estrategia)));
        }
    }
}
