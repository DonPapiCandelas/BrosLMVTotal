using System;
using System.Linq;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Estados de cuenta: lectura por encabezado, deteccion del banco, fechas/montos y saldo corrido. Los ejemplos salen del PDF oficial de formatos
    // (datos ficticios); donde el PDF solo trae una fila se agrego una segunda para poder revisar el saldo.
    public class BancoArchivoTests
    {
        private static (BancoDeteccion det, BancoResultado res) Leer(string texto, string archivo = "estado.csv")
        {
            var filas = BancoTexto.LeerTexto(texto);
            var det = BancoArchivo.Detectar(filas, archivo);
            return (det, BancoArchivo.Interpretar(filas, det.Mapa));
        }

        private const string BanorteCab13 = "\"CUENTA\",\"FECHA DE OPERACIÓN\",\"FECHA\",\"REFERENCIA\",\"DESCRIPCIÓN\",\"COD. TRANSAC\",\"SUCURSAL\",\"DEPÓSITOS\",\"RETIROS\",\"SALDO\",\"MOVIMIENTO\",\"DESCRIPCIÓN DETALLADA\",\"CHEQUE\"";
        private const string BanorteCab14 = "\"CUENTA\",\"FECHA DE OPERACIÓN\",\"FECHA\",\"REFERENCIA\",\"DESCRIPCIÓN\",\"COD. TRANSAC\",\"SUCURSAL\",\"DEPÓSITOS\",\"RETIROS\",\"SALDO\",\"MONEDA\",\"MOVIMIENTO\",\"DESCRIPCIÓN DETALLADA\",\"CHEQUE\"";

        private static string Banorte13() => BanorteCab13 + "\n" +
            "\"'0557039638\",\"06/11/2025\",\"06/11/2025\",\"0000000000\",\"DEV COM ADQ 09797844\",\"061\",\"0302\",\"$0.41\",\"-\",\"$9,558.88\",\"21764\",\"CONCEPTO: TASA DE DESCUENTO\",\"-\"\n" +
            "\"'0557039638\",\"07/11/2025\",\"07/11/2025\",\"0000000000\",\"PAGO DE SERVICIO\",\"061\",\"0302\",\"-\",\"$100.00\",\"$9,458.88\",\"21765\",\"DETALLE\",\"-\"\n";

        private static string Banorte14() => BanorteCab14 + "\n" +
            "\"'0557039638\",\"06/11/2025\",\"06/11/2025\",\"0000000000\",\"DEV COM ADQ 09797844\",\"061\",\"0302\",\"$0.41\",\"-\",\"$9,558.88\",\"MXN\",\"21764\",\"CONCEPTO: TASA DE DESCUENTO\",\"-\"\n" +
            "\"'0557039638\",\"07/11/2025\",\"07/11/2025\",\"0000000000\",\"PAGO DE SERVICIO\",\"061\",\"0302\",\"-\",\"$100.00\",\"$9,458.88\",\"MXN\",\"21765\",\"DETALLE\",\"-\"\n";

        [Fact]
        public void Banorte_leePorEncabezado_conOSinLaColumnaMoneda()
        {
            foreach (var texto in new[] { Banorte13(), Banorte14() })
            {
                var (det, r) = Leer(texto);
                Assert.Equal("Banorte", det.Banco);
                Assert.Empty(r.Errores);
                Assert.Equal(2, r.Movs.Count);
                Assert.Equal(0.41m, r.Movs[0].Abono); Assert.Equal(0m, r.Movs[0].Cargo);
                Assert.Equal(100m, r.Movs[1].Cargo);
                Assert.Equal(9458.88m, r.SaldoFinal);
                Assert.Equal(9558.47m, r.SaldoInicial);
                Assert.Equal("0557039638", r.Movs[0].CuentaArchivo);          // sin el apostrofe de Excel
                Assert.True(r.SaldoVerificado); Assert.Equal(0, r.RupturasSaldo);
                Assert.Contains("TASA DE DESCUENTO", r.Movs[0].Detalle);
            }
        }

        [Fact]
        public void ArchivoDelMasNuevoAlMasViejo_seInvierteYElSaldoCuadra()
        {
            var lineas = Banorte13().Split('\n').Where(l => l.Length > 0).ToList();
            string alReves = lineas[0] + "\n" + lineas[2] + "\n" + lineas[1] + "\n";
            var (_, r) = Leer(alReves);
            Assert.True(r.Descendente);
            Assert.Equal(0, r.RupturasSaldo);
            Assert.Equal(new DateTime(2025, 11, 6), r.Movs[0].Fecha);
            Assert.Equal(1, r.Movs[0].Orden); Assert.Equal(2, r.Movs[1].Orden);
            Assert.Equal(9558.47m, r.SaldoInicial);
        }

        [Fact]
        public void SaldoQueNoCuadra_seReporta()
        {
            string roto = Banorte13().Replace("$9,458.88", "$9,000.00");
            var (_, r) = Leer(roto);
            Assert.Equal(1, r.RupturasSaldo);
            Assert.Contains(r.Avisos, a => a.Contains("no cuadra"));
        }

        [Fact]
        public void Banamex_saltaElPreambuloYTomaLaCuentaDeArriba()
        {
            string t = "Movimiento de cuenta Cheques,,,,\n,,,,\nResumen de la Cuenta del día 19/11/2025,,,,\nSucursal,7002,,Nombre,LOGICAL CONCEPT SA DE CV\nCuenta,7761915,,Retiros (0),$0.00\n" +
                "Depósito (2),\"$19,000.00 \",,,\n,,,,\nDetalle de Movimientos - Depósitos y Retiros,,,,\nFecha,Descripción,Depósitos,Retiros,Saldo\n" +
                "19/11/2025,0889 Sucursal: 859 Referencia Númerica: 191125 TRASP BNRT A BNX Autorización: 814005,\"9,000.00\",-,\"9,000.00\"\n" +
                "19/11/2025,Segundo deposito,\"10,000.00\",-,\"19,000.00\"\n";
            var (det, r) = Leer(t);
            Assert.Equal("Banamex", det.Banco);
            Assert.Contains("7761915", det.CuentasPreambulo);
            Assert.Equal(2, r.Movs.Count);
            Assert.Equal(9000m, r.Movs[0].Abono); Assert.Equal(19000m, r.SaldoFinal);
            Assert.Equal(0, r.RupturasSaldo);
            Assert.Equal("Traspaso", r.Movs[0].Tipo);
        }

        [Fact]
        public void BanBajio_extraeRfcClabeNombreYBancoDeLaDescripcion()
        {
            string t = "LOGICAL CONCEPT SA DE CV,22426910,Conecta BanBajio,01,10-Dic-2025,11:56:01,Mes Actual,TODOS,01-Dic-2025 al 31-Dic-2025,,93,926711.09,839025.55\n" +
                "#,Fecha Movimiento,Hora,Recibo,Descripción,Cargos,Abonos,Saldo\n" +
                "1,10-Dic-2025,10:39:30,3141288010248,SPEI Recibido: | Institucion contraparte: BMONEX Ordenante: EZI METALES SA DE CV Cuenta Ordenante: 112180000022215542  |  RFC Ordenante: EME960529C55 | Referencia: 251210 | Hora: 10:39:30 | Clave de Rastreo: 123799634 Concepto del Pago: A14217 | Recibo # 230203069,,2807.20,7917.15\n" +
                "2,10-Dic-2025,11:00:00,3141288010249,COMISION SPEI,10.00,,7907.15\n";
            var (det, r) = Leer(t, "bajio.txt");
            Assert.Equal("BanBajío", det.Banco);
            Assert.Contains("22426910", det.CuentasPreambulo);
            Assert.Equal(2, r.Movs.Count);
            var m = r.Movs[0];
            Assert.Equal(new DateTime(2025, 12, 10), m.Fecha);
            Assert.Equal(2807.20m, m.Abono);
            Assert.Equal("EME960529C55", m.RfcContraparte);
            Assert.Equal("112180000022215542", m.ClabeContraparte);
            Assert.Equal("123799634", m.ClaveRastreo);
            Assert.Equal("EZI METALES SA DE CV", m.NombreContraparte);
            Assert.Equal("BMONEX", m.BancoContraparte);
            Assert.Equal("SPEI recibido", m.Tipo);
            Assert.Equal("Comisión", r.Movs[1].Tipo);
            Assert.Equal(0, r.RupturasSaldo);
        }

        [Fact]
        public void Santander_signoEnColumnaYApostrofes()
        {
            string t = "Cuenta,Fecha,Hora,Sucursal,Descripcion,Cargo/Abono,Importe,Saldo,Referencia,Concepto,Banco Participante,Clave de Rastreo,Descripcion Larga\n" +
                "'65503140379,'02122025',01:02,'3420',SEGURO AUTOCOMPARA SANTANDER,-,1387.66,599.03,'00000219786,Poliza 060906995/01,,,SEGURO LARGO\n" +
                "'65503140379,'03122025',09:00,'3420',DEPOSITO,+,100.00,699.03,'00000219787,Pago,,,\n";
            var (det, r) = Leer(t);
            Assert.Equal("Santander", det.Banco);
            Assert.Equal(2, r.Movs.Count);
            Assert.Equal(new DateTime(2025, 12, 2), r.Movs[0].Fecha);
            Assert.Equal(1387.66m, r.Movs[0].Cargo); Assert.Equal(100m, r.Movs[1].Abono);
            Assert.Equal("65503140379", r.Movs[0].CuentaArchivo);
            Assert.Contains("Poliza 060906995/01", r.Movs[0].Detalle);
            Assert.Equal(0, r.RupturasSaldo);
        }

        [Fact]
        public void AmericanExpress_elImportePositivoEsLaCompra()
        {
            string t = "Fecha,Fecha de Compra,Descripción,Importe\n04 Nov 2025,04 Nov 2025,PASE MARQUESA VIAPASS   MIGUEL HIDALGO,55.00\n05 Nov 2025,05 Nov 2025,PAGO RECIBIDO GRACIAS,-1000.00\n";
            var (det, r) = Leer(t);
            Assert.Equal("American Express", det.Banco);
            Assert.False(det.Mapa.ImporteNegativoEsCargo);
            Assert.Equal(55m, r.Movs[0].Cargo); Assert.Equal(1000m, r.Movs[1].Abono);
        }

        [Fact]
        public void Afirme_fechaDeDosDigitosYCuentaEnColumna()
        {
            string t = "Concepto,Fecha (DD/MM/AA),Referencia,Cargo,Abono,Saldo,Cuenta,Código,No. Secuencia\n" +
                "RASTREO 251106123911501550652291446599 REFERENCIA:61125 SPEI ENVIADO A 14-SANTANDER A Cliente A LA CTA CLABE 014180655031403791 RFC ND CONCEPTO Trasp,06/11/25,61125,3900.00,0.00,3503.21,11681009347,CS,1\n";
            var (det, r) = Leer(t);
            Assert.Equal("Afirme", det.Banco);
            Assert.Equal(new DateTime(2025, 11, 6), r.Movs[0].Fecha);
            Assert.Equal(3900m, r.Movs[0].Cargo);
            Assert.Equal("11681009347", r.Movs[0].CuentaArchivo);
            Assert.Equal("", r.Movs[0].RfcContraparte);                      // "RFC ND" no es un RFC
            Assert.Equal("014180655031403791", r.Movs[0].ClabeContraparte);
            Assert.StartsWith("251106", r.Movs[0].ClaveRastreo);
            Assert.Equal("SPEI enviado", r.Movs[0].Tipo);
        }

        [Fact]
        public void SinEncabezado_seInfiereLaTablaYSePideRevisar()
        {
            string t = "2026/01/12\t49628.00\tCargo\t344340.09\t03\tSEL PAGO DE SERVICIOS CONTRATO: 00001103 HORA: 16:22\n2026/01/13\t1000.00\tAbono\t345340.09\t03\tDEPOSITO\n";
            var (det, r) = Leer(t, "scotiabank.txt");
            Assert.False(det.MapaPorEncabezado);
            Assert.Equal("Scotiabank", det.Banco);
            Assert.Equal(2, r.Movs.Count);
            Assert.Equal(49628m, r.Movs[0].Cargo); Assert.Equal(1000m, r.Movs[1].Abono);
            Assert.Equal(0, r.RupturasSaldo);
        }

        [Fact]
        public void RenglonesIdenticos_sonDosMovimientos_yReimportarDaLosMismosHash()
        {
            string t = "Fecha,Descripción,Cargo,Abono\n01/10/2026,COMPRA,50.00,\n01/10/2026,COMPRA,50.00,\n02/10/2026,OTRA,,10.00\n";
            var (_, a) = Leer(t); var (_, b) = Leer(t);
            Assert.Equal(3, a.Movs.Select(m => m.Hash).Distinct().Count());
            Assert.Equal(a.Movs.Select(m => m.Hash), b.Movs.Select(m => m.Hash));
            Assert.False(a.SaldoVerificado);
        }

        [Theory]
        [InlineData("06/11/2025", 2025, 11, 6)]
        [InlineData("06/11/25", 2025, 11, 6)]
        [InlineData("2026-01-12", 2026, 1, 12)]
        [InlineData("2026/01/12", 2026, 1, 12)]
        [InlineData("08 Jul 2026", 2026, 7, 8)]
        [InlineData("10-Dic-2025", 2025, 12, 10)]
        [InlineData("02/Agosto/2026 11:35:00", 2026, 8, 2)]
        [InlineData("'02122025'", 2025, 12, 2)]
        [InlineData("20251202", 2025, 12, 2)]
        [InlineData("2026-10-27T10:30:00", 2026, 10, 27)]
        [InlineData("13/02/2026", 2026, 2, 13)]
        [InlineData("02/13/2026", 2026, 2, 13)]
        public void Fechas_formatosDeLosBancos(string texto, int a, int m, int d)
        {
            Assert.True(BancoValor.Fecha(texto, "auto", out var f));
            Assert.Equal(new DateTime(a, m, d), f);
        }

        [Fact]
        public void Fechas_ambiguaEsDiaMes_yElMapaPuedeCambiarla()
        {
            BancoValor.Fecha("03/04/2026", "auto", out var f1); BancoValor.Fecha("03/04/2026", "mdy", out var f2);
            Assert.Equal(new DateTime(2026, 4, 3), f1); Assert.Equal(new DateTime(2026, 3, 4), f2);
            Assert.False(BancoValor.Fecha("no es fecha", "auto", out _));
            Assert.False(BancoValor.Fecha("31/02/2026", "auto", out _));
        }

        [Theory]
        [InlineData("$1,234.56", 1234.56)]
        [InlineData("-$150.00", -150)]
        [InlineData("(1,234.56)", -1234.56)]
        [InlineData("1234.56-", -1234.56)]
        [InlineData("1.234,56", 1234.56)]
        [InlineData("1234,56", 1234.56)]
        [InlineData("1,234", 1234)]
        [InlineData("-", 0)]
        [InlineData("", 0)]
        [InlineData("$ 27,500.00", 27500)]
        public void Montos_formatosDeLosBancos(string texto, double esperado)
        {
            Assert.True(BancoValor.Monto(texto, out var v));
            Assert.Equal((decimal)esperado, v);
        }

        [Fact]
        public void Montos_textoQueNoEsNumero_fallaEnVezDeSerCero()
        {
            Assert.False(BancoValor.Monto("abc", out _));
            Assert.False(BancoValor.Monto("12.3.4", out _));
        }

        [Theory]
        [InlineData("0557039638", "0557039638", true)]
        [InlineData("0557039638", "072180005570396386", true)]     // la CLABE contiene el numero de cuenta
        [InlineData("9638", "0557039638", true)]                   // ultimos cuatro digitos
        [InlineData("0557039638", "0557039639", false)]
        [InlineData("123", "0557039638", false)]
        public void MismaCuenta_numeroClabeOUltimosDigitos(string a, string b, bool esperado)
        {
            Assert.Equal(esperado, BancoArchivo.MismaCuenta(a, b));
        }

        [Fact]
        public void ArchivoSinMovimientos_diceQueFalta()
        {
            var (_, r) = Leer("Fecha,Descripción,Cargo,Abono\n");
            Assert.NotEmpty(r.Errores);
        }

        [Fact]
        public void ImporteQueNoEsNumero_bloqueaLaImportacion()
        {
            var (_, r) = Leer("Fecha,Descripción,Cargo,Abono\n01/10/2026,COMPRA,cincuenta,\n");
            Assert.Contains(r.Errores, e => e.Contains("cincuenta"));
        }
    }
}
