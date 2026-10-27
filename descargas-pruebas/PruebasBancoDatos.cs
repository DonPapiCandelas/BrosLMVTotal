using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Cuentas bancarias, importacion sin duplicados, sugerencia de cuenta y perfiles. Datos ficticios.
    public class BancoDatosTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public BancoDatosTests(BaseDesechable bd) { _bd = bd; }
        private const string Rfc = "BNK010101BNK";

        private static BancoCuentaFila Cta(int id, string alias, string banco, string numero, string clabe = "") =>
            new BancoCuentaFila { Id = id, Alias = alias, Banco = banco, Numero = numero, Clabe = clabe };

        private const string Archivo = "Fecha,Descripción,Cargo,Abono,Saldo\n01/10/2026,SALDO,,1000.00,1000.00\n02/10/2026,COMPRA,50.00,,950.00\n02/10/2026,COMPRA,50.00,,900.00\n03/10/2026,SPEI RECIBIDO RFC: ABC010101AB1,,300.00,1200.00\n";

        private static BancoResultado Leer(string t)
        {
            var filas = BancoTexto.LeerTexto(t);
            return BancoArchivo.Interpretar(filas, BancoArchivo.Detectar(filas, "x.csv").Mapa);
        }

        [Fact]
        public void SugerirCuenta_porNumero_esSegura_yElNumeroPuedeSerClabeOTerminacion()
        {
            var cuentas = new List<BancoCuentaFila> { Cta(1, "Banorte nómina", "Banorte", "0557039638"), Cta(2, "Banorte ventas", "Banorte", "0112233445"), Cta(3, "BBVA", "BBVA", "999888777") };
            var s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new[] { "0557039638" }, "x.csv", null, null);
            Assert.Equal(1, s.Cuenta.Id); Assert.True(s.Segura);
            s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new[] { "072180011223344556" }, "x.csv", null, null);          // una CLABE que contiene el numero de la segunda cuenta
            Assert.Equal(2, s.Cuenta.Id); Assert.True(s.Segura);
            s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new[] { "072180099999999998" }, "x.csv", null, null);          // una CLABE que no es de ninguna: no adivina entre las dos de Banorte
            Assert.Null(s.Cuenta);
            s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new string[0], "Banorte_Movimientos_9638.csv", null, null);   // terminacion en el nombre del archivo
            Assert.Equal(1, s.Cuenta.Id);
        }

        [Fact]
        public void SugerirCuenta_variasDelMismoBancoSinPistas_noEligeSola()
        {
            var cuentas = new List<BancoCuentaFila> { Cta(1, "Banorte 1", "Banorte", "1111111111"), Cta(2, "Banorte 2", "Banorte", "2222222222"), Cta(3, "Banorte 3", "Banorte", "3333333333") };
            var s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new string[0], "movimientos.csv", null, null);
            Assert.Null(s.Cuenta);
            Assert.Contains("3 cuentas", s.Motivo);
        }

        [Fact]
        public void SugerirCuenta_elSaldoInicialIgualAlUltimoSaldoDesempata_perNoEsSegura()
        {
            var cuentas = new List<BancoCuentaFila> { Cta(1, "Banorte 1", "Banorte", "1111111111"), Cta(2, "Banorte 2", "Banorte", "2222222222") };
            var ultimos = new Dictionary<int, (DateTime, decimal)> { [1] = (new DateTime(2026, 9, 30), 500m), [2] = (new DateTime(2026, 9, 30), 1234.56m) };
            var s = BancoDatos.SugerirCuenta(cuentas, "Banorte", new string[0], "m.csv", 1234.56m, ultimos);
            Assert.Equal(2, s.Cuenta.Id); Assert.False(s.Segura);
            Assert.Contains("último saldo", s.Motivo);
        }

        [Fact]
        public void SugerirCuenta_unicaCuentaDelBanco_sePreselecciona()
        {
            var cuentas = new List<BancoCuentaFila> { Cta(1, "Santander", "Santander", "5550001"), Cta(2, "BBVA", "BBVA", "7770001") };
            Assert.Equal(1, BancoDatos.SugerirCuenta(cuentas, "Santander", new string[0], "m.csv", null, null).Cuenta.Id);
            Assert.Null(BancoDatos.SugerirCuenta(cuentas, "Banorte", new string[0], "m.csv", null, null).Cuenta);
        }

        [Fact]
        public void ValidarCuenta_pideLoNecesario()
        {
            Assert.NotNull(BancoDatos.ValidarCuenta(Cta(0, "", "Banorte", "1234")));
            Assert.NotNull(BancoDatos.ValidarCuenta(Cta(0, "A", "Banorte", "12")));
            Assert.NotNull(BancoDatos.ValidarCuenta(Cta(0, "A", "Banorte", "123456", "123")));
            Assert.Null(BancoDatos.ValidarCuenta(Cta(0, "A", "Banorte", "123456", "072180005570396386")));
        }

        [Fact]
        public void Importar_noDuplicaAlCargarElMismoPeriodo_yConservaDosRenglonesIdenticos()
        {
            if (!_bd.Disponible) return;
            int cuenta = BancoDatos.GuardarCuenta(_bd.Conn, Rfc, Cta(0, "Prueba", "Banorte", "5550001"));
            var r = Leer(Archivo);
            Assert.Equal(4, r.Movs.Count);
            var a = BancoDatos.Importar(_bd.Conn, Rfc, cuenta, @"C:\x\a.csv", new string('a', 64), "Banorte", r.Movs, r.SaldoInicial, r.SaldoFinal, null);
            Assert.Equal(4, a.Nuevos); Assert.Equal(0, a.Duplicados);

            var b = BancoDatos.Importar(_bd.Conn, Rfc, cuenta, @"C:\x\a.csv", new string('a', 64), "Banorte", Leer(Archivo).Movs, null, null, null);
            Assert.Equal(0, b.Nuevos); Assert.Equal(4, b.Duplicados);
            Assert.Equal(4, BancoDatos.Movimientos(_bd.Conn, Rfc, cuenta, null, null, null, null).Count);
            Assert.Single(BancoDatos.ArchivosYaCargados(_bd.Conn, Rfc, new string('a', 64)).Take(1));

            // Un archivo que se encima (dos dias nuevos) solo agrega lo nuevo.
            var c = Leer(Archivo + "04/10/2026,OTRA COMPRA,200.00,,1000.00\n");
            var imp = BancoDatos.Importar(_bd.Conn, Rfc, cuenta, @"C:\x\b.csv", new string('b', 64), "Banorte", c.Movs, null, null, null);
            Assert.Equal(1, imp.Nuevos); Assert.Equal(4, imp.Duplicados);

            var contraparte = BancoDatos.Movimientos(_bd.Conn, Rfc, cuenta, null, null, "ABC010101AB1", null);
            Assert.Single(contraparte); Assert.Equal("ABC010101AB1", contraparte[0].RfcContraparte);
            Assert.Equal(5, BancoDatos.ObtenerCuentas(_bd.Conn, Rfc).Single(x => x.Id == cuenta).Movimientos);
        }

        [Fact]
        public void LaMismaCuenta_nuncaMezclaMovimientosConOtra()
        {
            if (!_bd.Disponible) return;
            int c1 = BancoDatos.GuardarCuenta(_bd.Conn, "BNK020202BNK", Cta(0, "Una", "Banorte", "1112223"));
            int c2 = BancoDatos.GuardarCuenta(_bd.Conn, "BNK020202BNK", Cta(0, "Otra", "Banorte", "4445556"));
            var r = Leer(Archivo);
            BancoDatos.Importar(_bd.Conn, "BNK020202BNK", c1, "a.csv", new string('c', 64), null, r.Movs, null, null, null);
            var e = BancoDatos.Importar(_bd.Conn, "BNK020202BNK", c2, "a.csv", new string('c', 64), null, Leer(Archivo).Movs, null, null, null);
            Assert.Equal(4, e.Nuevos);                                   // misma huella, otra cuenta: son movimientos distintos
            Assert.Equal(2, BancoDatos.ArchivosYaCargados(_bd.Conn, "BNK020202BNK", new string('c', 64)).Count);
        }

        [Fact]
        public void Continuidad_avisaSiEsteArchivoNoAbreDondeTerminoElAnterior()
        {
            if (!_bd.Disponible) return;
            const string rfc = "BNK030303BNK";
            int cuenta = BancoDatos.GuardarCuenta(_bd.Conn, rfc, Cta(0, "Cont", "Banorte", "9990001"));
            var r = Leer(Archivo);
            BancoDatos.Importar(_bd.Conn, rfc, cuenta, "a.csv", new string('d', 64), null, r.Movs, r.SaldoInicial, r.SaldoFinal, null);       // termina el 03/10 con 1,200.00
            Assert.Null(BancoDatos.AvisoDeContinuidad(_bd.Conn, cuenta, new DateTime(2026, 10, 4), 1200m));
            Assert.Contains("puede faltar un periodo", BancoDatos.AvisoDeContinuidad(_bd.Conn, cuenta, new DateTime(2026, 10, 4), 777m));
            Assert.Null(BancoDatos.AvisoDeContinuidad(_bd.Conn, cuenta, new DateTime(2026, 10, 2), 777m));      // se encima con lo ya cargado: lo cubre la huella
        }

        [Fact]
        public void Perfil_seGuardaPorFirma_yUnArchivoNuevoLoRecuperaAunqueElPreambuloCambie()
        {
            if (!_bd.Disponible) return;
            const string rfc = "BNK040404BNK";
            string corto = "Fecha,Descripción,Cargo,Abono\n01/10/2026,A,10.00,\n";
            string conPreambulo = "Banco Raro\nCuenta 1234567\n\nFecha,Descripción,Cargo,Abono\n01/10/2026,A,10.00,\n";
            var f1 = BancoTexto.LeerTexto(corto); var m1 = BancoArchivo.Detectar(f1, "a.csv").Mapa;
            BancoDatos.GuardarPerfil(_bd.Conn, rfc, "Mi banco raro", "Otro", m1);

            var f2 = BancoTexto.LeerTexto(conPreambulo);
            var det2 = BancoArchivo.Detectar(f2, "b.csv");
            var perfil = BancoDatos.BuscarPerfil(_bd.Conn, rfc, det2.Mapa.Firma);
            Assert.NotNull(perfil);
            var mapa = BancoArchivo.Reubicar(f2, perfil.Mapa);
            Assert.Equal(3, mapa.FilaEncabezado); Assert.Equal(4, mapa.PrimeraFila);
            Assert.Single(BancoArchivo.Interpretar(f2, mapa).Movs);
            Assert.Null(BancoDatos.BuscarPerfil(_bd.Conn, "OTRO010101OTR", det2.Mapa.Firma));              // los perfiles son de cada empresa
        }

        [Fact]
        public void DeshacerImportacion_borraSusMovimientosYSuRegistro()
        {
            if (!_bd.Disponible) return;
            const string rfc = "BNK050505BNK";
            int cuenta = BancoDatos.GuardarCuenta(_bd.Conn, rfc, Cta(0, "Del", "Banorte", "8880001"));
            var r = Leer(Archivo);
            var imp = BancoDatos.Importar(_bd.Conn, rfc, cuenta, "a.csv", new string('e', 64), null, r.Movs, null, null, null);
            var res = BancoDatos.DeshacerImportacion(_bd.Conn, rfc, imp.ImportacionId);
            Assert.True(res.ok); Assert.Equal(4, res.borrados);
            Assert.Empty(BancoDatos.Movimientos(_bd.Conn, rfc, cuenta, null, null, null, null));
            Assert.Empty(BancoDatos.Importaciones(_bd.Conn, rfc));

            // Una cuenta con movimientos solo se da de baja; sin movimientos se borra.
            BancoDatos.Importar(_bd.Conn, rfc, cuenta, "a.csv", new string('e', 64), null, Leer(Archivo).Movs, null, null, null);
            Assert.False(BancoDatos.EliminarOBaja(_bd.Conn, rfc, cuenta));
            Assert.Empty(BancoDatos.ObtenerCuentas(_bd.Conn, rfc));
            Assert.Single(BancoDatos.ObtenerCuentas(_bd.Conn, rfc, true));
        }
    }
}
