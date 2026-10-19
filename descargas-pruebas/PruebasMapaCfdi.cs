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
        public void TotalDistinto_marcaElDocumentoQueNoCuadraConElCfdi()
        {
            if (!_bd.Disponible) return;
            const string rfc = "TDI010101TDI";
            var igual = Nuevo(rfc, false); var distinto = Nuevo(rfc, false); var cancelado = Nuevo(rfc, false); var sinDoc = Nuevo(rfc, false);
            void Doc(Guid u, decimal total, bool canc, string sistema) =>
                _bd.Ejecutar("INSERT INTO CfdiDocumentoComercial (RFC, UUID, DocumentID, Folio, Total, Cancelado, Sistema) VALUES ('" + rfc + "','" + u + "',1,'X'," + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + (canc ? 1 : 0) + ",'" + sistema + "')");
            Doc(igual, 116m, false, "Comercial");
            Doc(distinto, 100m, false, "Premium");           // al documento le falto una retencion
            Doc(cancelado, 100m, true, "Comercial");          // cancelado: no cuenta
            var filas = MapaCfdi.Calcular(_bd.Conn, rfc, false);
            Assert.Equal(1, filas.Sum(f => f.TotalDistinto));
            var lista = MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, false, "TotalDistinto", null, null, null);
            Assert.Single(lista); Assert.Equal(distinto, lista[0].Uuid);
            var docs = MapaCfdi.ObtenerDocumentosComercial(_bd.Conn, rfc, distinto);
            Assert.Equal(-16m, docs[0].Diferencia);
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
        public void CfdiQueSoloEstanEnElAdd_aparecenComoMesesPorDescargar()
        {
            if (!_bd.Disponible) return;
            const string rfc = "MAT010101MAT";
            var propio = Nuevo(rfc, false, diasAtras: 3);
            var viejo = Guid.NewGuid(); var viejoNomina = Guid.NewGuid();
            // «Externos»: estan en el ADD pero Descargas nunca los bajo (periodos anteriores).
            foreach (var (u, tipo) in new[] { (viejo, "I"), (viejoNomina, "N") })
                _bd.Ejecutar("INSERT INTO CfdiExterno (RFC, Sistema, UUID, FechaEmision, Direccion, TipoComprobante, Contraparte, SerieFolio, Total) VALUES ('" + rfc + "','ADD','" + u + "','2023-05-10','Recibido','" + tipo + "','PROVEEDOR','A1',100)");
            MapaCfdi.GuardarEstados(_bd.Conn, rfc, SistemaCfdi.Add, new[] { E(propio, "En ADD"), E(viejo, "En ADD"), E(viejoNomina, "En ADD") });

            var filas = MapaCfdi.Calcular(_bd.Conn, rfc, false);   // sin nomina
            var mayo = filas.Single(f => f.Anio == 2023 && f.Mes == 5 && f.Tipo == "Recibidos");
            Assert.Equal(1, mayo.SoloSistemas); Assert.Equal(0, mayo.Descargados); Assert.Equal(1, mayo.EnAdd);
            Assert.Equal(2, MapaCfdi.Calcular(_bd.Conn, rfc, true).Single(f => f.Anio == 2023 && f.Mes == 5).SoloSistemas);   // con nomina son 2

            var lista = MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, false, "SoloSistemas", null, null, null);
            var f1 = Assert.Single(lista);
            Assert.True(f1.Externo); Assert.Equal(viejo, f1.Uuid); Assert.Equal("Sin descargar", f1.Estatus);
            Assert.DoesNotContain(MapaCfdi.ObtenerCfdis(_bd.Conn, rfc, true, "FaltaComercial", null, null, null), x => x.Externo);   // los externos no «faltan» en Comercial

            // si luego se descarga, deja de ser externo (no se cuenta doble)
            BrosSatDb.GuardarCfdiRecibido(_bd.Conn, new CfdiParseado { UUID = viejo, RFCEmisor = "PRV010101PRV", RFCReceptor = rfc, FechaEmision = new DateTime(2023, 5, 10), Total = 100m, TipoComprobante = "I" }, null, out _);
            var tras = MapaCfdi.Calcular(_bd.Conn, rfc, false).Single(f => f.Anio == 2023 && f.Mes == 5);
            Assert.Equal(0, tras.SoloSistemas); Assert.Equal(1, tras.Descargados);
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
