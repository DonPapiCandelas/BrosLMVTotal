using System;
using System.IO;
using System.Linq;
using BrosLMV.Descargas.Datos;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Expediente de CFDI: lectura de XML (CFDI 4.0, complemento de pagos 2.0 y 1.0, impuestos locales), estado de pago PPD, IVA por flujo de efectivo y observaciones.
    // Los XML son sinteticos (datos ficticios) y se escriben en una carpeta temporal.
    public class ExpedienteTests : IClassFixture<BaseDesechable>, IDisposable
    {
        private readonly BaseDesechable _bd;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "bros_expediente_" + Guid.NewGuid().ToString("N"));
        private const string Rfc = "EXP010101EXP", Prov = "PRV010101PRV";
        public ExpedienteTests(BaseDesechable bd) { _bd = bd; Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private int Guardar(Guid uuid, string xml, string emisor, string receptor, string tipo, string metodo, string forma, DateTime fecha, decimal total, decimal iva, string estatus = "Vigente", string serie = "A", string folio = null)
        {
            string ruta = Path.Combine(_dir, uuid + ".xml"); File.WriteAllText(ruta, xml);
            var c = new CfdiParseado { UUID = uuid, RFCEmisor = emisor, RFCReceptor = receptor, FechaEmision = fecha, Total = total, IVA = iva, TipoComprobante = tipo, MetodoPago = metodo, FormaPago = forma, Moneda = "MXN", Serie = serie, Folio = folio ?? uuid.ToString().Substring(0, 6) };
            int id = BrosSatDb.GuardarCfdiRecibido(_bd.Conn, c, ruta, out _);
            if (estatus != "Vigente") BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, id, estatus, null);
            return id;
        }

        private static string Factura(Guid uuid, string subtotal, string iva, string total, string metodo, string forma, string extra = "") =>
            "<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Version=\"4.0\" TipoDeComprobante=\"I\" MetodoPago=\"" + metodo + "\" FormaPago=\"" + forma + "\" Moneda=\"MXN\" SubTotal=\"" + subtotal + "\" Total=\"" + total + "\">" +
            "<cfdi:Emisor Rfc=\"" + Prov + "\" Nombre=\"PROVEEDOR\"/><cfdi:Receptor Rfc=\"" + Rfc + "\" Nombre=\"MI EMPRESA\"/>" +
            "<cfdi:Conceptos><cfdi:Concepto ClaveProdServ=\"01010101\" Cantidad=\"1\" ClaveUnidad=\"H87\" Descripcion=\"Servicio\" ValorUnitario=\"" + subtotal + "\" Importe=\"" + subtotal + "\" ObjetoImp=\"02\">" +
            "<cfdi:Impuestos><cfdi:Traslados><cfdi:Traslado Base=\"" + subtotal + "\" Impuesto=\"002\" TipoFactor=\"Tasa\" TasaOCuota=\"0.160000\" Importe=\"" + iva + "\"/></cfdi:Traslados></cfdi:Impuestos></cfdi:Concepto></cfdi:Conceptos>" +
            "<cfdi:Impuestos TotalImpuestosTrasladados=\"" + iva + "\"><cfdi:Traslados><cfdi:Traslado Base=\"" + subtotal + "\" Impuesto=\"002\" TipoFactor=\"Tasa\" TasaOCuota=\"0.160000\" Importe=\"" + iva + "\"/></cfdi:Traslados></cfdi:Impuestos>" +
            "<cfdi:Complemento>" + extra + "<tfd:TimbreFiscalDigital xmlns:tfd=\"http://www.sat.gob.mx/TimbreFiscalDigital\" UUID=\"" + uuid.ToString().ToUpperInvariant() + "\"/></cfdi:Complemento></cfdi:Comprobante>";

        private static string Rep20(Guid dr, string importePagado, string iva, string fechaPago) =>
            "<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Version=\"4.0\" TipoDeComprobante=\"P\" Moneda=\"XXX\" SubTotal=\"0\" Total=\"0\"><cfdi:Emisor Rfc=\"" + Prov + "\"/><cfdi:Receptor Rfc=\"" + Rfc + "\"/>" +
            "<cfdi:Complemento><pago20:Pagos xmlns:pago20=\"http://www.sat.gob.mx/Pagos20\" Version=\"2.0\"><pago20:Pago FechaPago=\"" + fechaPago + "\" FormaDePagoP=\"03\" MonedaP=\"MXN\" Monto=\"" + importePagado + "\" TipoCambioP=\"1\">" +
            "<pago20:DoctoRelacionado IdDocumento=\"" + dr.ToString().ToUpperInvariant() + "\" Serie=\"A\" Folio=\"1\" MonedaDR=\"MXN\" EquivalenciaDR=\"1\" NumParcialidad=\"1\" ImpSaldoAnt=\"1160.00\" ImpPagado=\"" + importePagado + "\" ImpSaldoInsoluto=\"0.00\" ObjetoImpDR=\"02\">" +
            "<pago20:ImpuestosDR><pago20:TrasladosDR><pago20:TrasladoDR BaseDR=\"" + (Math.Round(decimal.Parse(importePagado) / 1.16m, 2)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\" ImpuestoDR=\"002\" TipoFactorDR=\"Tasa\" TasaOCuotaDR=\"0.160000\" ImporteDR=\"" + iva + "\"/></pago20:TrasladosDR></pago20:ImpuestosDR>" +
            "</pago20:DoctoRelacionado></pago20:Pago></pago20:Pagos></cfdi:Complemento></cfdi:Comprobante>";

        private static string Rep10(Guid dr, string importePagado, string fechaPago) =>
            "<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/3\" Version=\"3.3\" TipoDeComprobante=\"P\" Moneda=\"XXX\" SubTotal=\"0\" Total=\"0\"><cfdi:Emisor Rfc=\"" + Prov + "\"/><cfdi:Receptor Rfc=\"" + Rfc + "\"/>" +
            "<cfdi:Complemento><pago10:Pagos xmlns:pago10=\"http://www.sat.gob.mx/Pagos\" Version=\"1.0\"><pago10:Pago FechaPago=\"" + fechaPago + "\" FormaDePagoP=\"03\" MonedaP=\"MXN\" Monto=\"" + importePagado + "\">" +
            "<pago10:DoctoRelacionado IdDocumento=\"" + dr.ToString().ToUpperInvariant() + "\" MonedaDR=\"MXN\" NumParcialidad=\"1\" ImpSaldoAnt=\"1160.00\" ImpPagado=\"" + importePagado + "\" ImpSaldoInsoluto=\"580.00\"/></pago10:Pago></pago10:Pagos></cfdi:Complemento></cfdi:Comprobante>";

        private ExpedienteDatos Construir(DateTime desde, DateTime hasta, string dir = "Ambos") =>
            Expediente.Construir(_bd.Conn, new ExpedienteOpciones { Rfc = Rfc, Desde = desde, Hasta = hasta, Direccion = dir, IncluirNomina = false });

        [Fact]
        public void LeeImpuestosPorTasa_Conceptos_Y_ComplementoDeImpuestosLocales()
        {
            if (!_bd.Disponible) return;
            var u = Guid.NewGuid();
            string locales = "<implocal:ImpuestosLocales xmlns:implocal=\"http://www.sat.gob.mx/implocal\" version=\"1.0\" TotaldeRetenciones=\"20.00\" TotaldeTraslados=\"3.00\">" +
                             "<implocal:RetencionesLocales ImpLocRetenido=\"CEDULAR\" TasadeRetencion=\"2.00\" Importe=\"20.00\"/><implocal:TrasladosLocales ImpLocTrasladado=\"ISH\" TasadeTraslado=\"3.00\" Importe=\"3.00\"/></implocal:ImpuestosLocales>";
            Guardar(u, Factura(u, "1000.00", "160.00", "1160.00", "PUE", "03", locales), Prov, Rfc, "I", "PUE", "03", new DateTime(2031, 3, 10), 1160m, 160m);

            var d = Construir(new DateTime(2031, 3, 1), new DateTime(2031, 3, 31));
            var c = Assert.Single(d.Cfdis);
            Assert.True(c.Leido); Assert.Equal("MI EMPRESA", c.NombreReceptor);
            Assert.Equal(160m, c.IvaTasa16); Assert.Equal(0m, c.IvaTasa8);
            Assert.Equal(20m, c.ImpuestosLocalesRetenciones); Assert.Equal(3m, c.ImpuestosLocalesTraslados);
            Assert.Contains("ImpuestosLocales", c.Complementos);
            var k = Assert.Single(d.Conceptos); Assert.Equal(160m, k.IvaTrasladado); Assert.Equal(0.16m, k.TasaIva); Assert.Equal("02", k.ObjetoImp);
            Assert.Contains(d.Impuestos, i => i.Tipo == "Traslado" && i.Impuesto == "IVA" && i.Tasa == 0.16m && i.Importe == 160m);
            Assert.Contains(d.Impuestos, i => i.Tipo == "Local retención" && i.Impuesto == "CEDULAR" && i.Importe == 20m);
            Assert.Contains(d.Impuestos, i => i.Tipo == "Local traslado" && i.Impuesto == "ISH");
        }

        [Fact]
        public void FlujoDeEfectivo_pue_enSuFecha_ppd_enLaFechaDelPago_yEstimaCuandoElRepNoDesglosa()
        {
            if (!_bd.Disponible) return;
            var pue = Guid.NewGuid(); var ppdA = Guid.NewGuid(); var ppdB = Guid.NewGuid(); var repA = Guid.NewGuid(); var repB = Guid.NewGuid(); var egreso = Guid.NewGuid();
            // PUE: IVA 160 en enero (2032). PPD A: IVA 160, pagado completo en febrero con REP 2.0 que desglosa. PPD B: IVA 160, pagado la mitad en marzo con REP 1.0 (sin desglose -> se estima 80).
            Guardar(pue, Factura(pue, "1000.00", "160.00", "1160.00", "PUE", "03"), Prov, Rfc, "I", "PUE", "03", new DateTime(2032, 1, 10), 1160m, 160m);
            Guardar(ppdA, Factura(ppdA, "1000.00", "160.00", "1160.00", "PPD", "99"), Prov, Rfc, "I", "PPD", "99", new DateTime(2032, 1, 12), 1160m, 160m);
            Guardar(ppdB, Factura(ppdB, "1000.00", "160.00", "1160.00", "PPD", "99"), Prov, Rfc, "I", "PPD", "99", new DateTime(2032, 1, 14), 1160m, 160m);
            Guardar(repA, Rep20(ppdA, "1160.00", "160.00", "2032-02-15T12:00:00"), Prov, Rfc, "P", null, null, new DateTime(2032, 2, 16), 0m, 0m);
            Guardar(repB, Rep10(ppdB, "580.00", "2032-03-05T12:00:00"), Prov, Rfc, "P", null, null, new DateTime(2032, 3, 6), 0m, 0m);
            // Nota de credito de 16 de IVA en enero: resta.
            string nc = Factura(egreso, "100.00", "16.00", "116.00", "PUE", "03").Replace("TipoDeComprobante=\"I\"", "TipoDeComprobante=\"E\"");
            Guardar(egreso, nc, Prov, Rfc, "E", "PUE", "03", new DateTime(2032, 1, 20), 116m, 16m);

            var d = Construir(new DateTime(2032, 1, 1), new DateTime(2032, 3, 31), "Recibidos");
            var ene = d.Flujo.Single(f => f.Mes == 1); var feb = d.Flujo.Single(f => f.Mes == 2); var mar = d.Flujo.Single(f => f.Mes == 3);
            Assert.Equal(160m, ene.IvaPue); Assert.Equal(16m, ene.IvaEgresos); Assert.Equal(144m, ene.Total);   // solo el PUE y la nota; los PPD aun no se pagan
            Assert.Equal(160m, feb.IvaPagos); Assert.Equal(0m, feb.IvaPagosEstimado);                          // REP 2.0 desglosa
            Assert.Equal(80m, mar.IvaPagos); Assert.Equal(80m, mar.IvaPagosEstimado);                          // REP 1.0: 160 x (580/1160)
            Assert.Equal(144m + 160m + 80m, d.Flujo.Sum(f => f.Total));
        }

        [Fact]
        public void Ppd_calculaPagado_saldo_yEstado_Y_Observaciones()
        {
            if (!_bd.Disponible) return;
            var sinRep = Guid.NewGuid(); var parcial = Guid.NewGuid(); var cancelado = Guid.NewGuid(); var pue99 = Guid.NewGuid();
            var viejo = DateTime.Today.AddDays(-90);
            Guardar(sinRep, Factura(sinRep, "1000.00", "160.00", "1160.00", "PPD", "99"), Prov, Rfc, "I", "PPD", "99", viejo, 1160m, 160m);
            int idParcial = Guardar(parcial, Factura(parcial, "1000.00", "160.00", "1160.00", "PPD", "99"), Prov, Rfc, "I", "PPD", "99", viejo.AddDays(1), 1160m, 160m);
            Guardar(cancelado, Factura(cancelado, "1000.00", "160.00", "1160.00", "PPD", "99"), Prov, Rfc, "I", "PPD", "99", viejo.AddDays(2), 1160m, 160m, estatus: "Cancelado");
            Guardar(pue99, Factura(pue99, "1000.00", "160.00", "1160.00", "PUE", "99"), Prov, Rfc, "I", "PUE", "99", viejo.AddDays(3), 1160m, 160m);
            // REP vigente que paga 580 del parcial (la tabla CfdiPagoDocto la llena el lector de CFDI al guardar un REP; aqui se siembra directo).
            var rep = Guid.NewGuid(); int idRep = Guardar(rep, Rep20(parcial, "580.00", "80.00", viejo.AddDays(10).ToString("yyyy-MM-ddT12:00:00")), Prov, Rfc, "P", null, null, viejo.AddDays(10), 0m, 0m);
            _bd.Ejecutar("INSERT INTO CfdiPagoDocto (CfdiID, UUIDRelacionado, FechaPago, NumParcialidad, ImpSaldoAnt, ImpPagado, ImpSaldoInsoluto) VALUES (" + idRep + ", '" + parcial + "', '" + viejo.AddDays(10).ToString("yyyy-MM-dd") + "', 1, 1160, 580, 580)");

            var d = Construir(viejo.AddDays(-1), viejo.AddDays(30));
            Assert.Equal("Sin complemento de pago", d.Ppd.Single(x => x.Uuid == sinRep).Estado);
            var p = d.Ppd.Single(x => x.Uuid == parcial); Assert.Equal("Pago parcial", p.Estado); Assert.Equal(580m, p.Pagado); Assert.Equal(580m, p.Saldo); Assert.Equal(1, p.Reps);
            Assert.Equal("Cancelado", d.Ppd.Single(x => x.Uuid == cancelado).Estado);

            Assert.Contains(d.Observaciones, o => o.Tema == "PPD sin complemento de pago" && o.Uuid == sinRep && o.Nivel == "Revisar");
            Assert.DoesNotContain(d.Observaciones, o => o.Tema == "PPD sin complemento de pago" && o.Uuid == cancelado);
            Assert.Contains(d.Observaciones, o => o.Tema == "PUE con forma de pago 99" && o.Uuid == pue99);
            Assert.Contains(d.Observaciones, o => o.Tema == "PPD con pago parcial" && o.Uuid == parcial);
        }

        [Fact]
        public void UnXmlQueFalta_noDetieneElExpediente_yQuedaComoObservacion()
        {
            if (!_bd.Disponible) return;
            var u = Guid.NewGuid();
            Guardar(u, Factura(u, "100.00", "16.00", "116.00", "PUE", "03"), Prov, Rfc, "I", "PUE", "03", new DateTime(2033, 5, 5), 116m, 16m);
            File.Delete(Path.Combine(_dir, u + ".xml"));
            var d = Construir(new DateTime(2033, 5, 1), new DateTime(2033, 5, 31));
            Assert.Single(d.Cfdis); Assert.False(d.Cfdis[0].Leido); Assert.Equal(1, d.XmlNoLeidos);
            Assert.Contains(d.Observaciones, o => o.Tema == "XML no encontrado" && o.Uuid == u);
        }
    }
}
