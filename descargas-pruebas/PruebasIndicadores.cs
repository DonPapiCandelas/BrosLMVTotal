using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Indicadores fiscales: cada regla debe senalar exactamente los comprobantes sembrados para ella. Datos ficticios.
    public class IndicadoresTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        private const string Rfc = "AAA010101AAA";
        public IndicadoresTests(BaseDesechable bd) { _bd = bd; }

        private Guid Nuevo(string emisor, string receptor, int diasAtras, string tipo = "I", string metodo = null, string forma = null, string uso = null,
            decimal total = 116m, string moneda = "MXN", string serie = "A", string folio = null, List<PagoDoctoParseado> pagos = null)
        {
            var u = Guid.NewGuid();
            var c = new CfdiParseado { UUID = u, RFCEmisor = emisor, RFCReceptor = receptor, FechaEmision = DateTime.Today.AddDays(-diasAtras), Total = total, TipoComprobante = tipo,
                MetodoPago = metodo, FormaPago = forma, UsoCFDI = uso, Moneda = moneda, Serie = serie, Folio = folio ?? u.ToString().Substring(0, 8) };
            int id = BrosSatDb.GuardarCfdiRecibido(_bd.Conn, c, null, out _);
            if (pagos != null) BrosSatDb.GuardarPagosDocumentos(_bd.Conn, id, pagos);
            return u;
        }

        private HashSet<Guid> Corre(string clave)
        {
            var def = Indicadores.Predefinidos.Single(x => x.Clave == clave);
            var t = Indicadores.Ejecutar(_bd.Conn, def, Rfc, DateTime.Today.AddDays(-400), DateTime.Today.AddDays(1));
            return new HashSet<Guid>(t.Rows.Cast<DataRow>().Select(r => (Guid)r["UUID"]));
        }

        [Fact]
        public void CadaRegla_SenalaSoloLoSuyo()
        {
            if (!_bd.Disponible) return;
            string prov = "BBB010101BBB", cli = "CCC010101CCC";
            var pue99 = Nuevo(prov, Rfc, 5, metodo: "PUE", forma: "99");
            var ok = Nuevo(prov, Rfc, 5, metodo: "PUE", forma: "03");
            var ppdForma = Nuevo(prov, Rfc, 100, metodo: "PPD", forma: "03");
            var efectivo = Nuevo(prov, Rfc, 5, metodo: "PUE", forma: "01", total: 2500m);
            var efectivoChico = Nuevo(prov, Rfc, 5, metodo: "PUE", forma: "01", total: 1500m);
            var s01 = Nuevo(prov, Rfc, 5, metodo: "PUE", forma: "03", uso: "S01");

            // factura emitida PPD pagada de mas por dos REP con la misma parcialidad
            var fac = Nuevo(Rfc, cli, 100, metodo: "PPD", forma: "99", total: 116m);
            Func<Guid, decimal, PagoDoctoParseado> pago = (rel, imp) => new PagoDoctoParseado { UUIDRelacionado = rel, NumParcialidad = 1, ImpPagado = imp, Moneda = "MXN", FormaDePago = "03" };
            var rep1 = Nuevo(Rfc, cli, 40, tipo: "P", total: 0m, pagos: new List<PagoDoctoParseado> { pago(fac, 100m) });
            var rep2 = Nuevo(Rfc, cli, 30, tipo: "P", total: 0m, pagos: new List<PagoDoctoParseado> { pago(fac, 100m), pago(Guid.NewGuid(), 50m) });
            var ppdSinRep = Nuevo(Rfc, cli, 100, metodo: "PPD", forma: "99");
            var ppdReciente = Nuevo(Rfc, cli, 5, metodo: "PPD", forma: "99");

            // duplicados
            var dup1 = Nuevo(prov, Rfc, 20, metodo: "PUE", forma: "03", serie: "Z", folio: "777", total: 500m);
            var dup2 = Nuevo(prov, Rfc, 19, metodo: "PUE", forma: "03", serie: "Z", folio: "777", total: 500m);

            // EFOS y cancelacion en proceso
            var efos = Nuevo("EEE010101EEE", Rfc, 10, metodo: "PUE", forma: "03");
            BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, _bd.Escalar<int>("SELECT CfdiID FROM CfdiRecibido WHERE UUID='" + efos + "'"), "Vigente", null, "Consulta", "100");
            var proceso = Nuevo(prov, Rfc, 10, metodo: "PUE", forma: "03");
            BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, _bd.Escalar<int>("SELECT CfdiID FROM CfdiRecibido WHERE UUID='" + proceso + "'"), "Vigente", "En proceso de cancelacion");

            // ajeno a la empresa: no debe salir en ninguna regla
            var ajeno = Nuevo("FFF010101FFF", "GGG010101GGG", 5, metodo: "PUE", forma: "99", uso: "S01", total: 5000m);

            Assert.Contains(pue99, Corre("PUE_99")); Assert.DoesNotContain(ok, Corre("PUE_99")); Assert.DoesNotContain(ajeno, Corre("PUE_99"));
            Assert.Contains(ppdForma, Corre("PPD_FORMA")); Assert.DoesNotContain(fac, Corre("PPD_FORMA"));
            Assert.Contains(efectivo, Corre("EFECTIVO_2000")); Assert.DoesNotContain(efectivoChico, Corre("EFECTIVO_2000"));
            Assert.Contains(s01, Corre("USO_S01")); Assert.DoesNotContain(ajeno, Corre("USO_S01"));
            Assert.Contains(fac, Corre("PAGO_MAYOR"));
            Assert.Contains(fac, Corre("PARCIALIDAD_REPETIDA"));
            var sinRep = Corre("PPD_SIN_REP");
            Assert.Contains(ppdSinRep, sinRep); Assert.Contains(ppdForma, sinRep);
            Assert.DoesNotContain(fac, sinRep); Assert.DoesNotContain(ppdReciente, sinRep);
            var repSinFac = Corre("REP_SIN_FACTURA");
            Assert.Contains(rep2, repSinFac); Assert.DoesNotContain(rep1, repSinFac);
            var dups = Corre("POSIBLE_DUPLICADO");
            Assert.Contains(dup1, dups); Assert.Contains(dup2, dups); Assert.DoesNotContain(ok, dups);
            Assert.Contains(efos, Corre("EFOS")); Assert.DoesNotContain(ok, Corre("EFOS"));
            Assert.Contains(proceso, Corre("CANCELACION_PROCESO")); Assert.DoesNotContain(ok, Corre("CANCELACION_PROCESO"));
        }

        [Fact]
        public void RespetaElRangoDeFechas_YNoCuentaCancelados()
        {
            if (!_bd.Disponible) return;
            var fuera = Nuevo("BBB010101BBB", Rfc, 300, metodo: "PUE", forma: "99");
            var cancelado = Nuevo("BBB010101BBB", Rfc, 3, metodo: "PUE", forma: "99");
            BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, _bd.Escalar<int>("SELECT CfdiID FROM CfdiRecibido WHERE UUID='" + cancelado + "'"), "Cancelado");
            var def = Indicadores.Predefinidos.Single(x => x.Clave == "PUE_99");
            var t = Indicadores.Ejecutar(_bd.Conn, def, Rfc, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(1));
            var uuids = t.Rows.Cast<DataRow>().Select(r => (Guid)r["UUID"]).ToList();
            Assert.DoesNotContain(fuera, uuids); Assert.DoesNotContain(cancelado, uuids);
        }

        [Theory]
        [InlineData("SELECT UUID FROM CfdiRecibido WHERE RFCReceptor=@rfc", true)]
        [InlineData("WITH x AS (SELECT 1 AS a) SELECT a FROM x", true)]
        [InlineData("SELECT UUID FROM CfdiRecibido WHERE NombreEmisor = 'DELETE ME' -- UPDATE", true)]
        [InlineData("DELETE FROM CfdiRecibido", false)]
        [InlineData("SELECT 1; DROP TABLE CfdiRecibido", false)]
        [InlineData("SELECT * INTO copia FROM CfdiRecibido", false)]
        [InlineData("SELECT 1 /* ok */ ; EXEC sp_who", false)]
        [InlineData("UPDATE CfdiRecibido SET Total=0", false)]
        [InlineData("   ", false)]
        public void ValidarSql(string sql, bool aceptada)
        {
            Assert.Equal(aceptada, Indicadores.ValidarSql(sql) == null);
        }

        [Fact]
        public void Personalizado_SeGuardaSeEjecutaYSeBorra()
        {
            if (!_bd.Disponible) return;
            var u = Nuevo("BBB010101BBB", Rfc, 2, metodo: "PUE", forma: "03", total: 99999m);
            Assert.Throws<InvalidOperationException>(() => Indicadores.GuardarPersonalizado(_bd.Conn, 0, "Malo", "", "DELETE FROM CfdiRecibido", NivelIndicador.Aviso));
            int id = Indicadores.GuardarPersonalizado(_bd.Conn, 0, "Compras muy grandes", "Mayores a 50 mil", "SELECT UUID, FechaEmision, Total FROM CfdiRecibido WHERE RFCReceptor=@rfc AND Total>50000 AND FechaEmision>=@desde AND FechaEmision<@hasta", NivelIndicador.Info);
            var def = Indicadores.Personalizados(_bd.Conn).Single(x => x.PersonalizadoID == id);
            Assert.Equal("Compras muy grandes", def.Nombre);
            var t = Indicadores.Ejecutar(_bd.Conn, def, Rfc, DateTime.Today.AddDays(-10), DateTime.Today.AddDays(1));
            Assert.Contains(u, t.Rows.Cast<DataRow>().Select(r => (Guid)r["UUID"]));
            Indicadores.EliminarPersonalizado(_bd.Conn, id);
            Assert.DoesNotContain(Indicadores.Personalizados(_bd.Conn), x => x.PersonalizadoID == id);
        }

        [Fact]
        public void Personalizado_QueEscribe_NoSeEjecutaAunqueSeMetaALaTabla()
        {
            if (!_bd.Disponible) return;
            _bd.Ejecutar("INSERT INTO IndicadorPersonalizado (Nombre, Sql, Nivel) VALUES (N'Trampa', N'DELETE FROM CfdiRecibido', 1)");
            var def = Indicadores.Personalizados(_bd.Conn).Single(x => x.Nombre == "Trampa");
            int antes = _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiRecibido");
            Assert.Throws<InvalidOperationException>(() => Indicadores.Ejecutar(_bd.Conn, def, Rfc, DateTime.Today.AddDays(-10), DateTime.Today));
            Assert.Equal(antes, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiRecibido"));
        }
    }
}
