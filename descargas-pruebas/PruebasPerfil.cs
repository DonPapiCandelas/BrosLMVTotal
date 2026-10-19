using System;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Perfil por empresa: Descargas funciona sola y cada sistema de CONTPAQi se activa por empresa. Datos ficticios.
    public class PerfilEmpresaTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public PerfilEmpresaTests(BaseDesechable bd) { _bd = bd; }

        private void AltaEmpresa(string rfc, string conexionComercial = null, string carpetaRecibidos = null)
        {
            _bd.Ejecutar("INSERT INTO Empresa (Nombre, RFC, RutaCer, RutaKey, PasswordCifrada, ComercialConexionSql, ComercialCarpetaXmlRecibidos) VALUES ('Prueba " + rfc + "', '" + rfc + "', 'a.cer', 'a.key', 0x00, " +
                (conexionComercial == null ? "NULL" : "'" + conexionComercial + "'") + ", " + (carpetaRecibidos == null ? "NULL" : "'" + carpetaRecibidos + "'") + ")");
        }

        [Fact]
        public void SinPerfilGuardado_seDeduceDeLoQueYaEstaConfigurado()
        {
            if (!_bd.Disponible) return;
            AltaEmpresa("PRA010101PRA");                                             // nada configurado
            var solo = BrosSatDb.ObtenerPerfil(_bd.Conn, "PRA010101PRA");
            Assert.True(solo.SoloDescargar); Assert.True(solo.Deducido); Assert.Equal("Solo descarga y análisis de XML", solo.Resumen);

            AltaEmpresa("PRB010101PRB", conexionComercial: "cadena");               // conexion a Comercial -> Comercial Pro
            Assert.True(BrosSatDb.ObtenerPerfil(_bd.Conn, "PRB010101PRB").ComercialPro);

            AltaEmpresa("PRC010101PRC", carpetaRecibidos: @"C:\XML");               // solo carpetas tambien cuenta
            Assert.True(BrosSatDb.ObtenerPerfil(_bd.Conn, "PRC010101PRC").ComercialPro);

            AltaEmpresa("PRD010101PRD");
            BrosSatDb.GuardarContabilidad(_bd.Conn, new ContabilidadConfig { RFC = "PRD010101PRD", BaseDatos = "ctX", Usuario = "U", CargaAutomatica = true });
            var conta = BrosSatDb.ObtenerPerfil(_bd.Conn, "PRD010101PRD");
            Assert.True(conta.Contabilidad); Assert.False(conta.ComercialPro); Assert.Equal("Contabilidad", conta.Resumen);
        }

        [Fact]
        public void PerfilGuardado_mandaSobreLoDeducido_yQuitarUnSistemaBorraLoQueSeLeyoDeEl()
        {
            if (!_bd.Disponible) return;
            const string rfc = "PRE010101PRE";
            AltaEmpresa(rfc, conexionComercial: "cadena");
            BrosSatDb.GuardarContabilidad(_bd.Conn, new ContabilidadConfig { RFC = rfc, BaseDatos = "ctX", Usuario = "U", CargaAutomatica = true });
            var u = Guid.NewGuid();
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Comercial, new[] { new EstadoCfdi { Uuid = u, Estado = "En lista" } });
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new[] { new EstadoCfdi { Uuid = u, Estado = "En ADD" } });
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Disco, new[] { new EstadoCfdi { Uuid = u, Estado = "Con archivo" } });
            _bd.Ejecutar("INSERT INTO CfdiExterno (RFC, Sistema, UUID, FechaEmision, Direccion) VALUES ('" + rfc + "','ADD','" + Guid.NewGuid() + "','2023-01-01','Recibido')");

            // El perfil guardado (Comercial si, Contabilidad no) manda sobre lo que se deducia.
            BrosSatDb.GuardarPerfil(_bd.Conn, rfc, new PerfilEmpresa { ComercialPro = true, Contabilidad = false });
            var p = BrosSatDb.ObtenerPerfil(_bd.Conn, rfc);
            Assert.True(p.ComercialPro); Assert.False(p.Contabilidad); Assert.False(p.Deducido);

            // Se borro lo leido del ADD y de las polizas, pero NO lo de Comercial ni del disco.
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + rfc + "' AND Sistema IN ('ADD','Polizas')"));
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiExterno WHERE RFC='" + rfc + "'"));
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + rfc + "' AND Sistema='Comercial'"));
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + rfc + "' AND Sistema='Disco'"));

            // Guardar de nuevo actualiza (no duplica) y quitar Comercial borra sus lecturas.
            BrosSatDb.GuardarPerfil(_bd.Conn, rfc, new PerfilEmpresa());
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM EmpresaPerfil WHERE RFC='" + rfc + "'"));
            Assert.True(BrosSatDb.ObtenerPerfil(_bd.Conn, rfc).SoloDescargar);
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + rfc + "' AND Sistema='Comercial'"));
        }

        [Fact]
        public void Premium_seGuardaSeDeduceYSePurga()
        {
            if (!_bd.Disponible) return;
            const string rfc = "PRF010101PRF";
            AltaEmpresa(rfc);
            Assert.False(BrosSatDb.ObtenerPerfil(_bd.Conn, rfc).ComercialPremium);
            BrosSatDb.GuardarPremium(_bd.Conn, new PremiumConfig { RFC = rfc, BaseDatos = "adX" });
            Assert.Equal("adX", BrosSatDb.ObtenerPremium(_bd.Conn, rfc).BaseDatos);
            Assert.True(BrosSatDb.ObtenerPerfil(_bd.Conn, rfc).ComercialPremium);          // se deduce de su configuracion
            var u = Guid.NewGuid();
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Premium, new[] { new EstadoCfdi { Uuid = u, Estado = "Timbrado" } });
            BrosSatDb.GuardarPerfil(_bd.Conn, rfc, new PerfilEmpresa { ComercialPremium = false });
            Assert.Null(BrosSatDb.ObtenerPremium(_bd.Conn, rfc));
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + rfc + "' AND Sistema='Premium'"));
        }
    }
}
