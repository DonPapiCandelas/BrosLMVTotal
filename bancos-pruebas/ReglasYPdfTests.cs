using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Lo que el editor ensena sin programar: reglas de tipo y de extraccion propias del banco, y lectura de PDF por zonas.
    public class ReglasYPdfTests
    {
        private const string Csv =
            "Fecha Op;Detalle;Debe;Haber;Saldo contable\n" +
            "03/11/2026;PAGO NOMINA QUINCENA REF 12345;1,500.00;;8,500.00\n" +
            "04/11/2026;DEPOSITO CLIENTE RFC XAXX010101000;;2,000.00;10,500.00\n" +
            "05/11/2026;COMISION MANEJO;12.50;;10,487.50\n";

        private static Layout Base()
        {
            return new Layout
            {
                Id = "hsbc-csv", Banco = "HSBC", Nombre = "HSBC CSV", Origen = "empresa",
                Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { En = "encabezado", Contiene = new List<string> { "debe" } } } },
                Columnas = new Dictionary<string, List<string>> { ["fecha"] = new List<string> { "Fecha Op" }, ["descripcion"] = new List<string> { "Detalle" }, ["cargo"] = new List<string> { "Debe" }, ["abono"] = new List<string> { "Haber" }, ["saldo"] = new List<string> { "Saldo contable" } }
            };
        }

        [Fact]
        public void LasReglasDeTipoDelLayoutGananAlasGeneralesYRespetanCargoOAbono()
        {
            var l = Base();
            l.Tipos = new List<ReglaTipo> { new ReglaTipo { Patron = "nomina", Tipo = "Nómina propia", Cuando = "cargo" }, new ReglaTipo { Patron = "deposito cliente", Tipo = "Cobro de cliente", Cuando = "abono" } };
            var filas = BancoTexto.LeerTexto(Csv);
            var det = BancoArchivo.Detectar(filas, "x.csv", Catalogo.Combinar(new[] { l }));
            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            Assert.Equal("Nómina propia", res.Movs[0].Tipo);
            Assert.Equal("Cobro de cliente", res.Movs[1].Tipo);
            Assert.Equal("Comisión", res.Movs[2].Tipo);            // sin regla propia: queda la general
        }

        [Fact]
        public void UnaReglaDeTipoDeAbonoNoAplicaAUnCargo()
        {
            var l = Base();
            l.Tipos = new List<ReglaTipo> { new ReglaTipo { Patron = "nomina", Tipo = "Cobro", Cuando = "abono" } };
            var filas = BancoTexto.LeerTexto(Csv);
            var res = BancoArchivo.Interpretar(filas, BancoArchivo.Detectar(filas, "x.csv", Catalogo.Combinar(new[] { l })).Mapa);
            Assert.NotEqual("Cobro", res.Movs[0].Tipo);
        }

        [Fact]
        public void LasReglasDeExtraccionSacanReferenciaRfcYNombreDelConcepto()
        {
            var l = Base();
            l.Extraer = new List<ReglaExtraer>
            {
                new ReglaExtraer { Campo = "referencia", Patron = @"REF (\d+)" },
                new ReglaExtraer { Campo = "rfc", Patron = @"RFC ([A-Z&]{3,4}\d{6}[A-Z0-9]{3})" },
                new ReglaExtraer { Campo = "claveRastreo", Patron = @"[(" }                       // patron mal escrito: se ignora, no truena
            };
            var filas = BancoTexto.LeerTexto(Csv);
            var res = BancoArchivo.Interpretar(filas, BancoArchivo.Detectar(filas, "x.csv", Catalogo.Combinar(new[] { l })).Mapa);
            Assert.Equal("12345", res.Movs[0].Referencia);
            Assert.Equal("XAXX010101000", res.Movs[1].RfcContraparte);
            Assert.Empty(res.Errores);
        }

        [Fact]
        public void LasReglasSobrevivenAIrYVenirComoJson()
        {
            var l = Base();
            l.Tipos = new List<ReglaTipo> { new ReglaTipo { Patron = "x", Tipo = "Y", Cuando = "abono" } };
            l.Extraer = new List<ReglaExtraer> { new ReglaExtraer { Campo = "rfc", Patron = @"RFC (\w+)" } };
            var otro = Layout.DeDatos(l.ADatos());
            Assert.Equal("abono", otro.Tipos[0].Cuando);
            Assert.Equal(@"RFC (\w+)", otro.Extraer[0].Patron);
        }

        private static string Pdf(string nombre)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "BrosLMV.sln"))) d = d.Parent;
            if (d == null) return null;
            string r = Path.Combine(d.FullName, "Entrenamiento", "Bancos", "Nuevos", nombre);
            return File.Exists(r) ? r : null;
        }

        [Fact]
        public void UnLayoutDePdfPorZonasLeeSinProgramarYSeAjustaPorLosTitulosDeLasColumnas()
        {
            string ruta = Pdf("Alma BBVA pesos Oct.pdf"); if (ruta == null) return;             // sin la carpeta privada se omite
            var propio = new Layout
            {
                Id = "otro-banco-pdf", Banco = "Otro", Nombre = "PDF de prueba", Formato = "pdf", Origen = "empresa",
                Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { En = "pdf", Contiene = new List<string> { "alma vegetales" } } } },
                Pdf = new PdfOpciones { Estrategia = "zonas", EncCargo = "Cargo", EncAbono = "Abono", EncSaldo = "Saldo", FilaInicia = @"^\d{2}-\d{2}$" }
            };
            var notas = new List<string>();
            // los de fabrica (bbvanet) tambien coinciden: el de la empresa gana por orden de lista solo si su confianza es mayor
            propio.Confianza = 3;
            Layout usado;
            var filas = BancoPdf.Leer(ruta, notas, new[] { propio }, out usado);
            Assert.Equal("otro-banco-pdf", usado.Id);
            var det = BancoArchivo.Detectar(filas, "x.pdf");
            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            Assert.True(res.Movs.Count > 50);
            Assert.True(res.SaldoVerificado);
            Assert.Equal(0, res.RupturasSaldo);

            // con los titulos equivocados no encuentra la tabla: no inventa movimientos
            propio.Pdf.EncCargo = "Debitos";
            var vacio = BancoPdf.Leer(ruta, new List<string>(), new[] { propio }, out usado);
            Assert.DoesNotContain(vacio, f => f.Length > 0 && f[0].StartsWith("20"));         // ningun movimiento: solo el preambulo y el encabezado sinteticos
        }
    }
}
