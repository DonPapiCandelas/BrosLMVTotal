using System;
using System.Collections.Generic;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Configuracion de la carga al ADD de Contabilidad: se guarda por RFC, la clave solo se guarda cifrada y lo cargado se marca por UUID. Datos ficticios.
    public class ContabilidadConfigTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public ContabilidadConfigTests(BaseDesechable bd) { _bd = bd; }

        [Fact]
        public void GuardaLeeActualizaYQuita_PorRfc()
        {
            if (!_bd.Disponible) return;
            const string rfc = "CCC010101CCC";
            Assert.Null(BrosSatDb.ObtenerContabilidad(_bd.Conn, rfc));

            byte[] clave = DpapiHelper.Cifrar("secreta");
            BrosSatDb.GuardarContabilidad(_bd.Conn, new ContabilidadConfig { RFC = rfc, BaseDatos = "ctPRUEBA", Usuario = "SUPERVISOR", ClaveCifrada = clave, CargaAutomatica = true });
            var c = BrosSatDb.ObtenerContabilidad(_bd.Conn, rfc);
            Assert.Equal("ctPRUEBA", c.BaseDatos); Assert.Equal("SUPERVISOR", c.Usuario); Assert.True(c.CargaAutomatica);
            Assert.NotEqual("secreta", System.Text.Encoding.UTF8.GetString(c.ClaveCifrada)); // nunca en claro
            Assert.Equal("secreta", DpapiHelper.Descifrar(c.ClaveCifrada));

            // guardar de nuevo actualiza (no duplica) y acepta clave vacia
            BrosSatDb.GuardarContabilidad(_bd.Conn, new ContabilidadConfig { RFC = rfc, BaseDatos = "ctOTRA", Usuario = "ANA", ClaveCifrada = null, CargaAutomatica = false });
            c = BrosSatDb.ObtenerContabilidad(_bd.Conn, rfc);
            Assert.Equal("ctOTRA", c.BaseDatos); Assert.Null(c.ClaveCifrada); Assert.False(c.CargaAutomatica);
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM EmpresaContabilidad WHERE RFC='" + rfc + "'"));

            BrosSatDb.QuitarContabilidad(_bd.Conn, rfc);
            Assert.Null(BrosSatDb.ObtenerContabilidad(_bd.Conn, rfc));
        }

        [Fact]
        public void MarcaComoCargadosSoloLosUuidIndicados()
        {
            if (!_bd.Disponible) return;
            const string rfc = "DDD010101DDD";
            Guid Nuevo()
            {
                var u = Guid.NewGuid();
                BrosSatDb.GuardarCfdiRecibido(_bd.Conn, new CfdiParseado { UUID = u, RFCEmisor = "EEE010101EEE", RFCReceptor = rfc, FechaEmision = DateTime.Today, Total = 10m, TipoComprobante = "I" }, null, out _);
                return u;
            }
            var a = Nuevo(); var b = Nuevo();
            BrosSatDb.GuardarContabilidad(_bd.Conn, new ContabilidadConfig { RFC = rfc, BaseDatos = "ctPRUEBA", Usuario = "SUPERVISOR", CargaAutomatica = true });

            BrosSatDb.MarcarCargadoContabilidad(_bd.Conn, rfc, new List<Guid> { a });
            Assert.False(_bd.Escalar<bool>("SELECT CASE WHEN FechaCargadoContabilidad IS NULL THEN 1 ELSE 0 END FROM CfdiRecibido WHERE UUID='" + a + "'"));
            Assert.True(_bd.Escalar<bool>("SELECT CASE WHEN FechaCargadoContabilidad IS NULL THEN 1 ELSE 0 END FROM CfdiRecibido WHERE UUID='" + b + "'"));
            Assert.NotNull(_bd.Escalar<object>("SELECT FechaUltimaCarga FROM EmpresaContabilidad WHERE RFC='" + rfc + "'"));
        }
    }
}
