using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Motor;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // El caso que mas importa: el mismo importe en la misma fecha puede ser un movimiento nuevo o uno ya cargado.
    public class DuplicadosTests
    {
        private static readonly DateTime D = new DateTime(2026, 10, 5);
        private static int _n;

        private static BancoMov Nuevo(decimal abono, decimal? saldo = null, string hora = null, string rastreo = "")
        {
            return new BancoMov { Fecha = D, Orden = ++_n, Abono = abono, Saldo = saldo, Hora = hora == null ? (TimeSpan?)null : TimeSpan.Parse(hora), ClaveRastreo = rastreo };
        }

        private static Existente Ya(decimal abono, decimal? saldo = null, string hora = null, string rastreo = "", long id = 1)
        {
            return new Existente { EdoCtaId = id, Fecha = D, Abono = abono, Saldo = saldo, Hora = hora == null ? (TimeSpan?)null : TimeSpan.Parse(hora), Rastreo = rastreo };
        }

        [Fact]
        public void DosDepositosIgualesElMismoDiaConSaldoDistintoSonDosMovimientos()
        {
            var a = Nuevo(1000m, 5000m); var b = Nuevo(1000m, 6000m);
            var dup = Duplicados.Marcar(new[] { a, b }, new[] { Ya(1000m, 5000m, id: 1) });
            Assert.Contains(a, dup);
            Assert.DoesNotContain(b, dup);
        }

        [Fact]
        public void CargarElMismoArchivoOtraVezNoDuplicaNada()
        {
            var a = Nuevo(1000m, 5000m); var b = Nuevo(1000m, 6000m);
            var dup = Duplicados.Marcar(new[] { a, b }, new[] { Ya(1000m, 5000m, id: 1), Ya(1000m, 6000m, id: 2) });
            Assert.Equal(2, dup.Count);
        }

        [Fact]
        public void LaHoraDistingueAunSinSaldoYElOrdenDelArchivoNoImporta()
        {
            var a = Nuevo(500m, hora: "10:00"); var b = Nuevo(500m, hora: "11:30");
            var dup = Duplicados.Marcar(new[] { b, a }, new[] { Ya(500m, hora: "10:00", id: 1), Ya(500m, hora: "11:30", id: 2) });
            Assert.Equal(2, dup.Count);
            var c = Nuevo(500m, hora: "12:15");
            Assert.DoesNotContain(c, Duplicados.Marcar(new[] { c }, new[] { Ya(500m, hora: "10:00", id: 1) }));
        }

        [Fact]
        public void UnLadoConSegundosYElOtroSinSegundosSeConsideraLaMismaHora()
        {
            var a = Nuevo(500m, hora: "10:00:42");
            Assert.Contains(a, Duplicados.Marcar(new[] { a }, new[] { Ya(500m, hora: "10:00") }));
        }

        [Fact]
        public void LaClaveDeRastreoDistingueMovimientos()
        {
            var a = Nuevo(750m, rastreo: "ABC123"); var b = Nuevo(750m, rastreo: "XYZ999");
            var dup = Duplicados.Marcar(new[] { a, b }, new[] { Ya(750m, rastreo: "abc123") });
            Assert.Contains(a, dup);
            Assert.DoesNotContain(b, dup);
        }

        [Fact]
        public void SinNadaConQueDistinguirseSeEmparejaPorConteo()
        {
            var a = Nuevo(300m); var b = Nuevo(300m); var c = Nuevo(300m);
            var dup = Duplicados.Marcar(new[] { a, b, c }, new[] { Ya(300m, id: 1), Ya(300m, id: 2) });
            Assert.Equal(2, dup.Count);
            Assert.DoesNotContain(c, dup);
        }

        [Fact]
        public void UnMovimientoSinSaldoSeEmparejaConUnoQueYaTraiaSaldo()
        {
            // el estado de cuenta oficial no trae saldo en todos los renglones: lo cargado antes por el TXT si lo tenia
            var a = Nuevo(2000m);
            Assert.Contains(a, Duplicados.Marcar(new[] { a }, new[] { Ya(2000m, 123456.78m) }));
        }

        [Fact]
        public void SaldosQueSeContradicenNuncaSonElMismoMovimiento()
        {
            var a = Nuevo(2000m, 9000m);
            Assert.DoesNotContain(a, Duplicados.Marcar(new[] { a }, new[] { Ya(2000m, 123456.78m) }));
        }

        [Fact]
        public void ImportesOFechasDistintosNuncaSeEmparejan()
        {
            var a = Nuevo(2000m, 9000m);
            Assert.DoesNotContain(a, Duplicados.Marcar(new[] { a }, new[] { Ya(2000.01m, 9000m) }));
            var b = Nuevo(2000m, 9000m); b.Fecha = D.AddDays(1);
            Assert.DoesNotContain(b, Duplicados.Marcar(new[] { b }, new[] { Ya(2000m, 9000m) }));
        }

        [Fact]
        public void LaPrimeraVueltaNoDejaQueElConteoSeRobeLaParejaDeOtro()
        {
            // 'x' no trae saldo; 'y' si. En la base hay un solo renglon con el saldo de 'y': debe quedarse con 'y', no con 'x'.
            var x = Nuevo(100m); var y = Nuevo(100m, 777m);
            var dup = Duplicados.Marcar(new[] { x, y }, new[] { Ya(100m, 777m) });
            Assert.Contains(y, dup);
            Assert.DoesNotContain(x, dup);
        }

        [Fact]
        public void ElMotorLeeLaHoraDeLaColumnaYDeLaFecha()
        {
            var filas = BancoTexto.LeerTexto("Cuenta,Fecha,Hora,Descripcion,Cargo/Abono,Importe,Saldo\n'1234567,'25032026',06:53,DEPOSITO,+,100.00,100.00\n'1234567,'25032026',14:07,PAGO,-,40.00,60.00\n");
            var det = BancoArchivo.Detectar(filas, "x.csv");
            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            Assert.Equal(new TimeSpan(6, 53, 0), res.Movs[0].Hora);
            Assert.Equal(new TimeSpan(14, 7, 0), res.Movs[1].Hora);
            Assert.Equal(new TimeSpan(11, 35, 0), BancoValor.HoraDe("02/Agosto/2026 11:35:00"));
            Assert.Null(BancoValor.HoraDe("2026-10-05"));
        }
    }
}
