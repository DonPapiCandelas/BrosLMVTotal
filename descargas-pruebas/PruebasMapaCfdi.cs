using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Mapa de CFDI: la verdad por sistema se guarda por RFC+sistema, se agrega por mes y los CFDI cancelados no cuentan como «faltantes». Datos ficticios.
    public class MapaCfdiTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public MapaCfdiTests(BaseDesechable bd) { _bd = bd; }

        private Guid Nuevo(string rfcPropio, bool emitido, string tipo = "I", int diasAtras = 3, string cancelado = null)
        {
            var u = Guid.NewGuid();
            var c = new CfdiParseado
            {
                UUID = u, RFCEmisor = emitido ? rfcPropio : "PRV010101PRV", RFCReceptor = emitido ? "CLI010101CLI" : rfcPropio,
                FechaEmision = DateTime.Today.AddDays(-diasAtras), Total = 116m, TipoComprobante = tipo, Folio = u.ToString().Substring(0, 6)
            };
            int id = BrosSatDb.GuardarCfdiRecibido(_bd.Conn, c, null, out _);
            if (cancelado != null) BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, id, "Cancelado", null);
            return u;
        }

        private static EstadoCfdi E(Guid u, string estado, string detalle = null) => new EstadoCfdi { Uuid = u, Estado = estado, Detalle = detalle };

        [Fact]
        public void Calcula_porSistema_ysoloVigentesCuentanComoFaltantes()
        {
            if (!_bd.Disponible) return;
            const string rfc = "MAP010101MAP";
            var a = Nuevo(rfc, false); var b = Nuevo(rfc, false); var c = Nuevo(rfc, false); var cancelado = Nuevo(rfc, false, cancelado: "x");
            var emitido = Nuevo(rfc, true);

            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Disco, new[] { E(a, "Con archivo"), E(b, "Con archivo"), E(c, "Con archivo"), E(cancelado, "Con archivo"), E(emitido, "Sin archivo") });
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Comercial, new[] { E(a, "Con documento"), E(b, "En lista") });
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new[] { E(a, "En ADD") });

            var filas = MapaCfdi.Calcular(_bd.Conn, rfc, true);
            var rec = filas.Single(f => f.Tipo == "Recibidos"); var emi = filas.Single(f => f.Tipo == "Emitidos");

            Assert.Equal(4, rec.Descargados);
            Assert.Equal(4, rec.EnDisco);
            Assert.Equal(2, rec.EnComercial); Assert.Equal(1, rec.ConDocumento);
            Assert.Equal(1, rec.EnAdd);
            // vigentes (3) menos los que ya estan: faltan 2 en Comercial? b y a estan; c falta -> 1. En el ADD: b y c faltan -> 2. El cancelado no cuenta.
            Assert.Equal(1, rec.FaltaComercial);
            Assert.Equal(2, rec.FaltaAdd);
            Assert.Equal(0, rec.FaltaDisco);
            Assert.Equal(1, emi.FaltaDisco);   // su archivo ya no esta
        }

        [Fact]
        public void GuardarEstados_reemplazaLoAnterior_ynoMezclaEmpresasNiSistemas()
        {
            if (!_bd.Disponible) return;
            const string rfc = "MAQ010101MAQ", otro = "MAR010101MAR";
            var a = Nuevo(rfc, false);
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new[] { E(a, "En ADD") });
            MapaCfdi.GuardarEstados(_bd.Conn, otro, SistemaCfdi.Add, new[] { E(a, "En ADD") });
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Comercial, new[] { E(a, "En lista") });
            Assert.Equal(1, MapaCfdi.Calcular(_bd.Conn, rfc, true).Single().EnAdd);

            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new List<EstadoCfdi>());   // el CFDI ya no esta en el ADD
            var fila = MapaCfdi.Calcular(_bd.Conn, rfc, true).Single();
            Assert.Equal(0, fila.EnAdd);
            Assert.Equal(1, fila.EnComercial);   // otro sistema intacto
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstadoSistema WHERE RFC='" + otro + "' AND Sistema='ADD'"));   // otra empresa intacta
        }

        [Fact]
        public void ObtenerCfdis_filtraLoQueFalta_yPuedeExcluirNomina()
        {
            if (!_bd.Disponible) return;
            const string rfc = "MAS010101MAS";
            var normal = Nuevo(rfc, false); var nomina = Nuevo(rfc, true, tipo: "N");
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new[] { E(normal, "En ADD") });

            Assert.DoesNotContain(MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, true, "FaltaAdd", null, null, "Recibidos"), x => x.Uuid == normal);   // ya esta
            var faltan = MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, true, "FaltaAdd", null, null, null);
            Assert.Contains(faltan, x => x.Uuid == nomina);                                                                              // la nomina falta...
            Assert.DoesNotContain(MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, false, "FaltaAdd", null, null, null), x => x.Uuid == nomina);   // ...salvo que se excluya
            Assert.Equal(2, MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, true, "Todos", null, null, null).Count);
            Assert.Single(MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, true, "Todos", null, null, "Emitidos"));
        }

        [Fact]
        public void DeduceRol_dePolizaSegunTipoSatYConcepto()
        {
            Assert.Equal("Provisión / diario", MapaCfdi.DeducirRol(3, "Compra a credito", "Poliza"));
            Assert.Equal("Pago / egreso", MapaCfdi.DeducirRol(2, "Pago a proveedor", "Movimiento"));
            Assert.Equal("Cobro / ingreso", MapaCfdi.DeducirRol(1, "Cobro de factura", "Poliza"));
            Assert.Equal("Cancelación", MapaCfdi.DeducirRol(3, "Cancelación P3273 FI2597", "Poliza"));   // aunque sea de tipo diario
            Assert.Equal("Otro", MapaCfdi.DeducirRol(null, "algo", "Poliza"));
            Assert.Equal("Otro", MapaCfdi.DeducirRol(3, "algo", "Otro"));
        }
    }
}
