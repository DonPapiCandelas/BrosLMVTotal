using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Bancos.Carga;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // El estado de cada archivo en la bandeja: lo que ve la persona antes de cargar.
    public class AnalizadorTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "bancos_prueba_" + Guid.NewGuid().ToString("N"));
        public AnalizadorTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private string Archivo(string nombre, string contenido) { string r = Path.Combine(_dir, nombre); File.WriteAllText(r, contenido, new System.Text.UTF8Encoding(false)); return r; }

        private static List<CuentaBanco> Cuentas(params CuentaBanco[] c) { return c.ToList(); }
        private static readonly BuscarExistentes Nada = (c, d, h) => new List<Existente>();

        private const string BbvaTxt =
            "Día\tConcepto / Referencia\tcargo\tAbono\tSaldo\n" +
            "15-06-2026\tSPEI ENVIADO SANTANDER/0045504497  014 0150626PAGO DE NOMINA\t4,725.60\t\t12,332.36\n" +
            "04-06-2026\tSPEI RECIBIDOFONDEADORA/0112167392  699 5541089TRASPASO ENTRE CUENTAS\t\t15,000.00\t17,057.96\n" +
            "03-06-2026\tIVA COM SERV BCA INTERNET/IVA COM SERV BC\t2.08\t\t2,057.96\n";

        [Fact]
        public void UnArchivoConUnaSolaCuentaDeBancoSeSugiereSoloSiElBancoEsSeguro()
        {
            var cta = new CuentaBanco { Id = 1, Nombre = "BBVA OPERACION", Banco = "BBVA BANCOMER", Numero = "0123456789" };
            var a = Analizador.Analizar(Archivo("mov.txt", BbvaTxt), null, Cuentas(cta), Nada);
            Assert.Equal("BBVA", a.Banco);
            Assert.Equal(cta, a.Grupos[0].Cuenta);
            // la unica cuenta del banco se sugiere pero hay que confirmarla: no es "Listo" todavia
            Assert.Equal(EstadoArchivo.Revisar, a.Estado);
            Assert.Equal("Confirma la cuenta", a.Chip);
            Analizador.Asignar(a, a.Grupos[0], cta, Nada);
            Assert.True(a.Estado == EstadoArchivo.Listo, a.Chip + " | " + string.Join(" ; ", a.Notas));
            Assert.Equal(3, a.TotalNuevos);
        }

        [Fact]
        public void ConVariasCuentasDelMismoBancoYSinPistasNoEligeSola()
        {
            var a = Analizador.Analizar(Archivo("mov.txt", BbvaTxt), null, Cuentas(new CuentaBanco { Id = 1, Nombre = "BBVA A", Banco = "BBVA" }, new CuentaBanco { Id = 2, Nombre = "BBVA B", Banco = "BBVA" }), Nada);
            Assert.Null(a.Grupos[0].Cuenta);
            Assert.Equal(EstadoArchivo.Revisar, a.Estado);
            Assert.Equal("Elige la cuenta", a.Chip);
        }

        [Fact]
        public void LaCuentaSeSugierePorElSaldoConElQueAbreElArchivo()
        {
            var a = Analizador.Analizar(Archivo("mov.txt", BbvaTxt), null, Cuentas(
                new CuentaBanco { Id = 1, Nombre = "BBVA A", Banco = "BBVA", UltimoSaldo = 111m }, new CuentaBanco { Id = 2, Nombre = "BBVA B", Banco = "BBVA", UltimoSaldo = 2060.04m }), Nada);
            Assert.Equal(2, a.Grupos[0].Cuenta.Id);
            Assert.False(a.Grupos[0].Confirmada);       // se sugiere por saldo, pero la persona lo confirma
        }

        [Fact]
        public void LoYaCargadoQuedaComoYaCargado()
        {
            var cta = new CuentaBanco { Id = 1, Nombre = "BBVA", Banco = "BBVA" };
            var a = Analizador.Analizar(Archivo("mov.txt", BbvaTxt), null, Cuentas(cta), Nada);
            Analizador.Asignar(a, a.Grupos[0], cta, Nada);
            BuscarExistentes todo = (c, d, h) => a.Grupos[0].Movs.Select((m, i) => new Existente { EdoCtaId = i + 1, Fecha = m.Fecha, Cargo = m.Cargo, Abono = m.Abono, Saldo = m.Saldo }).ToList();
            Analizador.Reevaluar(a, todo);
            Assert.Equal(EstadoArchivo.YaCargado, a.Estado);
            Assert.Equal(3, a.TotalRepetidos);
        }

        [Fact]
        public void UnArchivoQueNingunLayoutReconoceQuedaSinLayout()
        {
            var a = Analizador.Analizar(Archivo("raro.csv", "Fecha Op;Detalle;Debe;Haber;Saldo contable\n03/11/2026;PAGO;1,500.00;;8,500.00\n"), null, Cuentas(), Nada);
            Assert.Equal(EstadoArchivo.SinLayout, a.Estado);
            Assert.Equal("Sin layout", a.Chip);
        }

        [Fact]
        public void UnPdfQueNoEsDeBbvaQuedaSinLayoutYNoRevientaLaBandeja()
        {
            string ruta = Path.Combine(_dir, "otro.pdf");
            File.WriteAllBytes(ruta, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D });        // un PDF roto
            var a = Analizador.Analizar(ruta, null, Cuentas(), Nada);
            Assert.True(a.Estado == EstadoArchivo.Error || a.Estado == EstadoArchivo.SinLayout);
            Assert.False(string.IsNullOrEmpty(a.Mensaje));
        }

        [Fact]
        public void ElSaldoQueNoCuadraPideRevisar()
        {
            string malo = BbvaTxt.Replace("17,057.96", "99,999.00");
            var cta = new CuentaBanco { Id = 1, Nombre = "BBVA", Banco = "BBVA" };
            var a = Analizador.Analizar(Archivo("mal.txt", malo), null, Cuentas(cta), Nada);
            Analizador.Asignar(a, a.Grupos[0], cta, Nada);
            Assert.Equal(EstadoArchivo.Revisar, a.Estado);
            Assert.Equal("El saldo no cuadra", a.Chip);
        }
    }
}
