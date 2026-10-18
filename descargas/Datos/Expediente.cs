// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Expediente.cs -- datos para un EXPEDIENTE de CFDI (devolucion de impuestos, aclaracion al SAT, revision): todo lo que dicen los XML de un
// periodo, ordenado en tablas planas que la pantalla vuelca a Excel. No depende de ningun sistema CONTPAQi.
//
// Lo que ya esta en la base (cabeceras, estatus SAT, EFOS, pagos aplicados, relaciones) sale de SQL; lo que no (impuestos por tasa y por concepto,
// impuestos locales, complementos, impuestos de cada pago) se lee de los XML en disco, en paralelo. Un XML que falta o no se puede leer no detiene
// nada: queda como observacion. Los lectores no dependen de la version del CFDI (3.3 / 4.0) ni de la del complemento de pagos (1.0 / 2.0): se
// navega por nombre local, no por espacio de nombres.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class ExpedienteOpciones
    {
        public string Rfc;
        public DateTime Desde;
        public DateTime Hasta;                       // inclusive
        public string Direccion = "Ambos";           // Recibidos / Emitidos / Ambos
        public bool IncluirNomina;
        public bool IncluirCancelados = true;
    }

    // PROPIEDADES (no campos): las hojas de Excel y WPF las recorren por reflexion.
    internal sealed class CfdiExp
    {
        public int CfdiId { get; set; }
        public Guid Uuid { get; set; }
        public DateTime Fecha { get; set; }
        public string Direccion { get; set; }
        public string Tipo { get; set; }
        public string Serie { get; set; }
        public string Folio { get; set; }
        public string RfcEmisor { get; set; }
        public string NombreEmisor { get; set; }
        public string RfcReceptor { get; set; }
        public string NombreReceptor { get; set; }
        public string UsoCfdi { get; set; }
        public string FormaPago { get; set; }
        public string MetodoPago { get; set; }
        public string Moneda { get; set; }
        public decimal TipoCambio { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Descuento { get; set; }
        public decimal Total { get; set; }
        public decimal IvaTrasladado { get; set; }
        public decimal IvaTasa16 { get; set; }
        public decimal IvaTasa8 { get; set; }
        public decimal BaseTasa0 { get; set; }
        public decimal BaseExento { get; set; }
        public decimal IepsTrasladado { get; set; }
        public decimal IvaRetenido { get; set; }
        public decimal IsrRetenido { get; set; }
        public decimal ImpuestosLocalesTraslados { get; set; }
        public decimal ImpuestosLocalesRetenciones { get; set; }
        public string EstatusSat { get; set; }
        public DateTime? FechaCancelacion { get; set; }
        public string Efos { get; set; }
        public string Ruta { get; set; }
        public string Complementos { get; set; }
        public bool Leido { get; set; }              // el XML se pudo leer
        public decimal TotalMxn => Moneda == "MXN" || string.IsNullOrEmpty(Moneda) || TipoCambio <= 0 ? Total : Total * TipoCambio;
        public decimal Signo => Tipo == "E" ? -1m : 1m;   // egreso (nota de credito) resta
        public decimal Factor => Moneda == "MXN" || string.IsNullOrEmpty(Moneda) || TipoCambio <= 0 ? 1m : TipoCambio;
    }

    internal sealed class ConceptoExp
    {
        public Guid Uuid { get; set; }
        public string Folio { get; set; }
        public string ClaveProdServ { get; set; }
        public string NoIdentificacion { get; set; }
        public string Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public string ClaveUnidad { get; set; }
        public string Unidad { get; set; }
        public decimal ValorUnitario { get; set; }
        public decimal Importe { get; set; }
        public decimal Descuento { get; set; }
        public string ObjetoImp { get; set; }
        public decimal IvaTrasladado { get; set; }
        public decimal TasaIva { get; set; }
        public decimal IvaRetenido { get; set; }
        public decimal IsrRetenido { get; set; }
    }

    internal sealed class ImpuestoExp
    {
        public Guid Uuid { get; set; }
        public string Folio { get; set; }
        public string Tipo { get; set; }             // Traslado / Retención / Local traslado / Local retención
        public string Impuesto { get; set; }         // IVA / ISR / IEPS / nombre del impuesto local
        public string Factor { get; set; }
        public decimal Tasa { get; set; }
        public decimal Base { get; set; }
        public decimal Importe { get; set; }
    }

    // Un renglon por documento relacionado de cada pago (REP).
    internal sealed class PagoDrExp
    {
        public Guid RepUuid { get; set; }
        public string RepFolio { get; set; }
        public DateTime FechaPago { get; set; }
        public string FormaPago { get; set; }
        public string MonedaPago { get; set; }
        public decimal TipoCambioPago { get; set; }
        public decimal Monto { get; set; }
        public string CuentaOrdenante { get; set; }
        public string CuentaBeneficiario { get; set; }
        public Guid DrUuid { get; set; }
        public string DrFolio { get; set; }
        public string MonedaDr { get; set; }
        public decimal EquivalenciaDr { get; set; }   // unidades de la moneda del documento por 1 unidad de la moneda del pago
        public int Parcialidad { get; set; }
        public decimal SaldoAnterior { get; set; }
        public decimal ImportePagado { get; set; }
        public decimal SaldoInsoluto { get; set; }
        public string ObjetoImpDr { get; set; }
        public decimal IvaTrasladadoDr { get; set; }
        public decimal IvaRetenidoDr { get; set; }
        public decimal IsrRetenidoDr { get; set; }
        // Importe expresado en el documento -> pesos: se pasa a la moneda del pago con la equivalencia y luego a pesos con el tipo de cambio del pago.
        public decimal AMxn(decimal importeEnMonedaDelDocumento)
        {
            decimal enPago = EquivalenciaDr > 0m && !string.Equals(MonedaDr, MonedaPago, StringComparison.OrdinalIgnoreCase) ? importeEnMonedaDelDocumento / EquivalenciaDr : importeEnMonedaDelDocumento;
            bool pagoEnPesos = string.IsNullOrEmpty(MonedaPago) || string.Equals(MonedaPago, "MXN", StringComparison.OrdinalIgnoreCase);
            return pagoEnPesos ? enPago : enPago * (TipoCambioPago > 0m ? TipoCambioPago : 1m);
        }
        public bool ImpuestosEnElPago { get; set; } // true = el XML trae el desglose de impuestos del documento (REP 2.0)
        public string Direccion { get; set; }       // del REP: Recibido (lo paga la empresa) / Emitido
    }

    internal sealed class ComplementoExp
    {
        public Guid Uuid { get; set; }
        public string Folio { get; set; }
        public string Complemento { get; set; }
        public string Detalle { get; set; }
    }

    // CFDI de ingreso PPD y su estado de pago.
    internal sealed class PpdExp
    {
        public Guid Uuid { get; set; }
        public DateTime Fecha { get; set; }
        public string Direccion { get; set; }
        public string Folio { get; set; }
        public string Tercero { get; set; }
        public string Moneda { get; set; }
        public decimal Total { get; set; }
        public decimal Pagado { get; set; }
        public decimal Saldo => Math.Max(0m, Total - Pagado);
        public int Reps { get; set; }
        public DateTime? UltimoPago { get; set; }
        public string EstatusSat { get; set; }
        public string Estado => EstatusSat == "Cancelado" ? "Cancelado" : Reps == 0 ? "Sin complemento de pago" : Saldo <= 0.01m ? "Pagado" : "Pago parcial";
    }

    internal sealed class FlujoExp
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Direccion { get; set; }        // Recibidos (IVA acreditable) / Emitidos (IVA trasladado cobrado)
        public decimal IvaPue { get; set; }          // CFDI pagados en una exhibicion: IVA en la fecha del CFDI
        public decimal IvaPagos { get; set; }        // IVA de los pagos (REP) en la fecha de pago
        public decimal IvaPagosEstimado { get; set; }// parte de IvaPagos calculada proporcionalmente (el REP no desglosa impuestos)
        public decimal IvaEgresos { get; set; }      // notas de credito (resta)
        public decimal Total => IvaPue + IvaPagos - IvaEgresos;
        public string MesTexto => new DateTime(Anio, Mes, 1).ToString("yyyy-MM");
    }

    internal sealed class ObservacionExp
    {
        public string Nivel { get; set; }            // Revisar / Aviso / Info
        public string Tema { get; set; }
        public Guid? Uuid { get; set; }
        public string Folio { get; set; }
        public string Detalle { get; set; }
    }

    internal sealed class ExpedienteDatos
    {
        public ExpedienteOpciones Opciones;
        public List<CfdiExp> Cfdis = new List<CfdiExp>();
        public List<ConceptoExp> Conceptos = new List<ConceptoExp>();
        public List<ImpuestoExp> Impuestos = new List<ImpuestoExp>();
        public List<PagoDrExp> Pagos = new List<PagoDrExp>();
        public List<ComplementoExp> Complementos = new List<ComplementoExp>();
        public List<PpdExp> Ppd = new List<PpdExp>();
        public List<FlujoExp> Flujo = new List<FlujoExp>();
        public List<ObservacionExp> Observaciones = new List<ObservacionExp>();
        public int XmlNoLeidos;
    }

    internal static class Expediente
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ===================================================================================
        //  Construccion
        // ===================================================================================

        public static ExpedienteDatos Construir(SqlConnection conn, ExpedienteOpciones o, Action<string> progreso = null)
        {
            var d = new ExpedienteDatos { Opciones = o };
            progreso?.Invoke("Leyendo los CFDI del periodo...");
            d.Cfdis = LeerCabeceras(conn, o);

            progreso?.Invoke("Leyendo " + d.Cfdis.Count.ToString("N0") + " XML...");
            var conceptos = new ConcurrentBag<ConceptoExp>(); var impuestos = new ConcurrentBag<ImpuestoExp>(); var pagos = new ConcurrentBag<PagoDrExp>();
            var complementos = new ConcurrentBag<ComplementoExp>(); var observaciones = new ConcurrentBag<ObservacionExp>();
            int leidos = 0, fallidos = 0;
            Parallel.ForEach(d.Cfdis, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, c =>
            {
                try
                {
                    if (string.IsNullOrEmpty(c.Ruta) || !File.Exists(c.Ruta)) { System.Threading.Interlocked.Increment(ref fallidos); observaciones.Add(Obs("Revisar", "XML no encontrado", c, c.Ruta)); return; }
                    LeerXml(c, conceptos, impuestos, pagos, complementos);
                    c.Leido = true;
                    int n = System.Threading.Interlocked.Increment(ref leidos);
                    if (n % 2000 == 0) progreso?.Invoke("Leyendo XML " + n.ToString("N0") + " de " + d.Cfdis.Count.ToString("N0") + "...");
                }
                catch (Exception ex) { System.Threading.Interlocked.Increment(ref fallidos); observaciones.Add(Obs("Revisar", "XML ilegible", c, ex.Message)); }
            });
            d.XmlNoLeidos = fallidos;
            d.Conceptos = conceptos.OrderBy(x => x.Uuid).ToList();
            d.Impuestos = impuestos.OrderBy(x => x.Uuid).ToList();
            d.Pagos = pagos.OrderBy(x => x.FechaPago).ThenBy(x => x.RepUuid).ToList();
            d.Complementos = complementos.OrderBy(x => x.Uuid).ToList();
            d.Observaciones.AddRange(observaciones);

            progreso?.Invoke("Calculando pagos y flujo de efectivo...");
            d.Ppd = CalcularPpd(conn, o, d.Cfdis);
            d.Flujo = CalcularFlujo(d);
            d.Observaciones.AddRange(CalcularObservaciones(conn, d));
            d.Observaciones = d.Observaciones.OrderBy(x => x.Nivel == "Revisar" ? 0 : x.Nivel == "Aviso" ? 1 : 2).ThenBy(x => x.Tema).ToList();
            return d;
        }

        private static ObservacionExp Obs(string nivel, string tema, CfdiExp c, string detalle) =>
            new ObservacionExp { Nivel = nivel, Tema = tema, Uuid = c.Uuid, Folio = (c.Serie ?? "") + (c.Folio ?? ""), Detalle = detalle };

        private static List<CfdiExp> LeerCabeceras(SqlConnection conn, ExpedienteOpciones o)
        {
            string dir = o.Direccion == "Recibidos" ? "AND c.RFCReceptor = @r AND c.RFCEmisor <> @r" : o.Direccion == "Emitidos" ? "AND c.RFCEmisor = @r" : "AND (c.RFCEmisor = @r OR c.RFCReceptor = @r)";
            string sql = @"
SELECT c.CfdiID, c.UUID, c.FechaEmision, CASE WHEN c.RFCEmisor = @r THEN 'Emitido' ELSE 'Recibido' END, ISNULL(c.TipoComprobante, 'I'), c.Serie, c.Folio, c.RFCEmisor, c.NombreEmisor, c.RFCReceptor,
       c.UsoCFDI, c.FormaPago, c.MetodoPago, c.Moneda, c.TipoCambio, ISNULL(c.Subtotal, 0), ISNULL(c.Descuento, 0), ISNULL(c.Total, 0), ISNULL(c.IVA, 0), ISNULL(c.Retenciones, 0),
       c.EstatusSat, CASE WHEN c.EstatusSat = 'Cancelado' THEN ISNULL(c.FechaCambioEstatus, c.FechaUltimaVerificacionEstatus) END, c.ValidacionEFOS, c.RutaArchivoXml
FROM CfdiRecibido c
WHERE c.FechaEmision >= @d AND c.FechaEmision < @h " + dir + @" AND (@nom = 1 OR ISNULL(c.TipoComprobante, '') <> 'N') AND (@can = 1 OR c.EstatusSat <> 'Cancelado')
ORDER BY c.FechaEmision, c.UUID";
            var lista = new List<CfdiExp>();
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 300 })
            {
                cmd.Parameters.AddWithValue("@r", o.Rfc); cmd.Parameters.AddWithValue("@d", o.Desde.Date); cmd.Parameters.AddWithValue("@h", o.Hasta.Date.AddDays(1));
                cmd.Parameters.AddWithValue("@nom", o.IncluirNomina ? 1 : 0); cmd.Parameters.AddWithValue("@can", o.IncluirCancelados ? 1 : 0);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add(new CfdiExp
                        {
                            CfdiId = l.GetInt32(0), Uuid = l.GetGuid(1), Fecha = l.GetDateTime(2), Direccion = l.GetString(3), Tipo = l.GetString(4).Trim(),
                            Serie = l.IsDBNull(5) ? null : l.GetString(5), Folio = l.IsDBNull(6) ? null : l.GetString(6), RfcEmisor = l.GetString(7), NombreEmisor = l.IsDBNull(8) ? null : l.GetString(8),
                            RfcReceptor = l.GetString(9), UsoCfdi = l.IsDBNull(10) ? null : l.GetString(10), FormaPago = l.IsDBNull(11) ? null : l.GetString(11), MetodoPago = l.IsDBNull(12) ? null : l.GetString(12),
                            Moneda = l.IsDBNull(13) ? "MXN" : l.GetString(13), TipoCambio = l.IsDBNull(14) ? 1m : l.GetDecimal(14), Subtotal = l.GetDecimal(15), Descuento = l.GetDecimal(16), Total = l.GetDecimal(17),
                            IvaTrasladado = l.GetDecimal(18), IvaRetenido = l.GetDecimal(19), EstatusSat = l.GetString(20),
                            FechaCancelacion = l.IsDBNull(21) ? (DateTime?)null : l.GetDateTime(21), Efos = l.IsDBNull(22) ? null : l.GetString(22), Ruta = l.IsDBNull(23) ? null : l.GetString(23)
                        });
            }
            return lista;
        }

        // ===================================================================================
        //  Lectura de un XML
        // ===================================================================================

        internal static XElement Hijo(XElement e, string local) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == local);
        internal static IEnumerable<XElement> Hijos(XElement e, string local) => e == null ? Enumerable.Empty<XElement>() : e.Elements().Where(x => x.Name.LocalName == local);
        internal static string A(XElement e, string nombre) => e?.Attribute(nombre)?.Value;
        internal static decimal D(XElement e, string nombre) { var v = A(e, nombre); return !string.IsNullOrEmpty(v) && decimal.TryParse(v, NumberStyles.Any, Inv, out var r) ? r : 0m; }

        internal static string NombreImpuesto(string codigo)
        {
            switch (codigo) { case "001": return "ISR"; case "002": return "IVA"; case "003": return "IEPS"; default: return codigo ?? ""; }
        }

        // Llena las colecciones con lo que dice el XML de un CFDI y completa los datos de la cabecera que SQL no tiene.
        internal static void LeerXml(CfdiExp c, ConcurrentBag<ConceptoExp> conceptos, ConcurrentBag<ImpuestoExp> impuestos, ConcurrentBag<PagoDrExp> pagos, ConcurrentBag<ComplementoExp> complementos)
        {
            var raiz = XDocument.Load(c.Ruta).Root;
            string folio = (c.Serie ?? "") + (c.Folio ?? "");
            c.NombreReceptor = A(Hijo(raiz, "Receptor"), "Nombre");
            if (string.IsNullOrEmpty(c.NombreEmisor)) c.NombreEmisor = A(Hijo(raiz, "Emisor"), "Nombre");

            // --- Impuestos del comprobante ---
            var imp = Hijo(raiz, "Impuestos");
            foreach (var t in Hijos(Hijo(imp, "Traslados"), "Traslado"))
            {
                string codigo = A(t, "Impuesto"); decimal tasa = D(t, "TasaOCuota"), importe = D(t, "Importe"), @base = D(t, "Base"); string factor = A(t, "TipoFactor");
                impuestos.Add(new ImpuestoExp { Uuid = c.Uuid, Folio = folio, Tipo = "Traslado", Impuesto = NombreImpuesto(codigo), Factor = factor, Tasa = tasa, Base = @base, Importe = importe });
                if (codigo == "002")
                {
                    if (factor == "Exento") c.BaseExento += @base;
                    else if (tasa == 0m) c.BaseTasa0 += @base;
                    else if (Math.Abs(tasa - 0.16m) < 0.0001m) c.IvaTasa16 += importe;
                    else if (Math.Abs(tasa - 0.08m) < 0.0001m) c.IvaTasa8 += importe;
                }
                else if (codigo == "003") c.IepsTrasladado += importe;
            }
            foreach (var r in Hijos(Hijo(imp, "Retenciones"), "Retencion"))
            {
                string codigo = A(r, "Impuesto"); decimal importe = D(r, "Importe");
                impuestos.Add(new ImpuestoExp { Uuid = c.Uuid, Folio = folio, Tipo = "Retención", Impuesto = NombreImpuesto(codigo), Importe = importe });
                if (codigo == "001") c.IsrRetenido += importe;
            }
            // SQL guarda un solo total de retenciones; aqui se separa por impuesto (IVA / ISR) con lo que dice el XML.
            c.IvaRetenido = Hijos(Hijo(imp, "Retenciones"), "Retencion").Where(r => A(r, "Impuesto") == "002").Sum(r => D(r, "Importe"));
            decimal ivaTrasXml = Hijos(Hijo(imp, "Traslados"), "Traslado").Where(r => A(r, "Impuesto") == "002" && A(r, "TipoFactor") != "Exento").Sum(r => D(r, "Importe"));
            if (imp != null) c.IvaTrasladado = ivaTrasXml;

            // --- Conceptos ---
            foreach (var k in Hijos(Hijo(raiz, "Conceptos"), "Concepto"))
            {
                var ce = new ConceptoExp
                {
                    Uuid = c.Uuid, Folio = folio, ClaveProdServ = A(k, "ClaveProdServ"), NoIdentificacion = A(k, "NoIdentificacion"), Descripcion = A(k, "Descripcion"), Cantidad = D(k, "Cantidad"),
                    ClaveUnidad = A(k, "ClaveUnidad"), Unidad = A(k, "Unidad"), ValorUnitario = D(k, "ValorUnitario"), Importe = D(k, "Importe"), Descuento = D(k, "Descuento"), ObjetoImp = A(k, "ObjetoImp")
                };
                var ki = Hijo(k, "Impuestos");
                foreach (var t in Hijos(Hijo(ki, "Traslados"), "Traslado").Where(x => A(x, "Impuesto") == "002" && A(x, "TipoFactor") != "Exento")) { ce.IvaTrasladado += D(t, "Importe"); ce.TasaIva = D(t, "TasaOCuota"); }
                foreach (var r in Hijos(Hijo(ki, "Retenciones"), "Retencion")) { if (A(r, "Impuesto") == "002") ce.IvaRetenido += D(r, "Importe"); else if (A(r, "Impuesto") == "001") ce.IsrRetenido += D(r, "Importe"); }
                conceptos.Add(ce);
            }

            // --- Complementos ---
            var nombres = new List<string>();
            foreach (var comp in Hijo(raiz, "Complemento")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                string n = comp.Name.LocalName;
                if (n == "TimbreFiscalDigital") continue;
                nombres.Add(n);
                string detalle = null;
                switch (n)
                {
                    case "Pagos": LeerPagos(c, comp, pagos); detalle = Hijos(comp, "Pago").Count() + " pago(s)"; break;
                    case "ImpuestosLocales":
                        c.ImpuestosLocalesRetenciones = D(comp, "TotaldeRetenciones"); c.ImpuestosLocalesTraslados = D(comp, "TotaldeTraslados");
                        foreach (var r in Hijos(comp, "RetencionesLocales")) impuestos.Add(new ImpuestoExp { Uuid = c.Uuid, Folio = folio, Tipo = "Local retención", Impuesto = A(r, "ImpLocRetenido"), Tasa = D(r, "TasadeRetencion"), Importe = D(r, "Importe") });
                        foreach (var t in Hijos(comp, "TrasladosLocales")) impuestos.Add(new ImpuestoExp { Uuid = c.Uuid, Folio = folio, Tipo = "Local traslado", Impuesto = A(t, "ImpLocTrasladado"), Tasa = D(t, "TasadeTraslado"), Importe = D(t, "Importe") });
                        detalle = "Retenciones locales " + c.ImpuestosLocalesRetenciones.ToString("N2", Inv) + " · traslados locales " + c.ImpuestosLocalesTraslados.ToString("N2", Inv); break;
                    case "Nomina":
                        detalle = "Pago " + A(comp, "FechaPago") + " · días " + A(comp, "NumDiasPagados") + " · percepciones " + D(comp, "TotalPercepciones").ToString("N2", Inv) + " · deducciones " + D(comp, "TotalDeducciones").ToString("N2", Inv) +
                                  " · otros pagos " + D(comp, "TotalOtrosPagos").ToString("N2", Inv) + " · empleado " + A(Hijo(comp, "Receptor"), "NumEmpleado") + " " + A(Hijo(comp, "Receptor"), "Curp"); break;
                    case "EstadoDeCuentaCombustible":
                        detalle = "Cuenta " + A(comp, "NumeroDeCuenta") + " · " + Hijos(Hijo(comp, "Conceptos"), "ConceptoEstadoDeCuentaCombustible").Count() + " operación(es) · subtotal " + D(comp, "SubTotal").ToString("N2", Inv) + " · total " + D(comp, "Total").ToString("N2", Inv); break;
                    case "ComercioExterior":
                        detalle = "Operación " + A(comp, "TipoOperacion") + " · pedimento " + A(comp, "ClaveDePedimento") + " · total USD " + D(comp, "TotalUSD").ToString("N2", Inv); break;
                    case "CartaPorte":
                        detalle = "Versión " + A(comp, "Version") + " · distancia total " + D(comp, "TotalDistRec").ToString("N2", Inv) + " km · internacional " + A(comp, "TranspInternac"); break;
                    case "Divisas": detalle = "Operación " + A(comp, "tipoOperacion"); break;
                }
                complementos.Add(new ComplementoExp { Uuid = c.Uuid, Folio = folio, Complemento = n, Detalle = detalle });
            }
            c.Complementos = string.Join(", ", nombres.Distinct());
        }

        // Complemento de pagos 1.0 y 2.0: un renglon por documento relacionado.
        private static void LeerPagos(CfdiExp rep, XElement pagosEl, ConcurrentBag<PagoDrExp> salida)
        {
            string folio = (rep.Serie ?? "") + (rep.Folio ?? "");
            foreach (var p in Hijos(pagosEl, "Pago"))
            {
                DateTime fecha = DateTime.TryParse(A(p, "FechaPago"), Inv, DateTimeStyles.None, out var f) ? f : rep.Fecha;
                decimal tc = D(p, "TipoCambioP"); if (tc <= 0m) tc = 1m;
                foreach (var dr in Hijos(p, "DoctoRelacionado"))
                {
                    var x = new PagoDrExp
                    {
                        RepUuid = rep.Uuid, RepFolio = folio, FechaPago = fecha, FormaPago = A(p, "FormaDePagoP"), MonedaPago = A(p, "MonedaP"), TipoCambioPago = tc, Monto = D(p, "Monto"),
                        CuentaOrdenante = A(p, "CtaOrdenante"), CuentaBeneficiario = A(p, "CtaBeneficiario"), DrUuid = Guid.TryParse(A(dr, "IdDocumento"), out var g) ? g : Guid.Empty,
                        DrFolio = (A(dr, "Serie") ?? "") + (A(dr, "Folio") ?? ""), MonedaDr = A(dr, "MonedaDR"), EquivalenciaDr = D(dr, "EquivalenciaDR"), Parcialidad = (int)D(dr, "NumParcialidad"), SaldoAnterior = D(dr, "ImpSaldoAnt"),
                        ImportePagado = D(dr, "ImpPagado"), SaldoInsoluto = D(dr, "ImpSaldoInsoluto"), ObjetoImpDr = A(dr, "ObjetoImpDR"), Direccion = rep.Direccion
                    };
                    var idr = Hijo(dr, "ImpuestosDR");
                    if (idr != null)
                    {
                        x.ImpuestosEnElPago = true;
                        foreach (var t in Hijos(Hijo(idr, "TrasladosDR"), "TrasladoDR").Where(t => A(t, "ImpuestoDR") == "002" && A(t, "TipoFactorDR") != "Exento")) x.IvaTrasladadoDr += D(t, "ImporteDR");
                        foreach (var r in Hijos(Hijo(idr, "RetencionesDR"), "RetencionDR")) { if (A(r, "ImpuestoDR") == "002") x.IvaRetenidoDr += D(r, "ImporteDR"); else if (A(r, "ImpuestoDR") == "001") x.IsrRetenidoDr += D(r, "ImporteDR"); }
                    }
                    salida.Add(x);
                }
            }
        }

        // ===================================================================================
        //  Calculos
        // ===================================================================================

        // Estado de pago de los CFDI PPD del periodo: todo lo que tenga REP en la base (aunque el REP sea de otro periodo).
        private static List<PpdExp> CalcularPpd(SqlConnection conn, ExpedienteOpciones o, List<CfdiExp> cfdis)
        {
            var ppd = cfdis.Where(c => c.Tipo == "I" && string.Equals(c.MetodoPago, "PPD", StringComparison.OrdinalIgnoreCase)).ToList();
            var pagos = new Dictionary<Guid, (decimal pagado, int reps, DateTime? ultimo)>();
            using (var cmd = new SqlCommand(@"
SELECT p.UUIDRelacionado, SUM(p.ImpPagado), COUNT(DISTINCT p.CfdiID), MAX(p.FechaPago)
FROM CfdiPagoDocto p JOIN CfdiRecibido rep ON rep.CfdiID = p.CfdiID AND rep.EstatusSat <> 'Cancelado'
GROUP BY p.UUIDRelacionado", conn) { CommandTimeout = 120 })
            using (var l = cmd.ExecuteReader())
                while (l.Read()) pagos[l.GetGuid(0)] = (l.IsDBNull(1) ? 0m : l.GetDecimal(1), l.GetInt32(2), l.IsDBNull(3) ? (DateTime?)null : l.GetDateTime(3));
            return ppd.Select(c =>
            {
                pagos.TryGetValue(c.Uuid, out var p);
                return new PpdExp
                {
                    Uuid = c.Uuid, Fecha = c.Fecha, Direccion = c.Direccion, Folio = (c.Serie ?? "") + (c.Folio ?? ""), Moneda = c.Moneda, Total = c.Total, Pagado = p.pagado, Reps = p.reps, UltimoPago = p.ultimo,
                    EstatusSat = c.EstatusSat, Tercero = c.Direccion == "Emitido" ? (c.NombreReceptor ?? c.RfcReceptor) : (c.NombreEmisor ?? c.RfcEmisor)
                };
            }).OrderByDescending(x => x.Fecha).ToList();
        }

        // IVA «por flujo de efectivo» por mes: CFDI de pago en una exhibicion (PUE) en su fecha + IVA de cada pago (REP) en la fecha de pago − notas de credito.
        // Si el REP no desglosa impuestos (complemento 1.0), el IVA del pago se estima: IVA de la factura × (pagado / total de la factura).
        internal static List<FlujoExp> CalcularFlujo(ExpedienteDatos d)
        {
            var filas = new Dictionary<(int, int, string), FlujoExp>();
            FlujoExp Fila(DateTime f, string dir)
            {
                string key = dir == "Emitido" ? "Emitidos" : "Recibidos";
                if (!filas.TryGetValue((f.Year, f.Month, key), out var x)) filas[(f.Year, f.Month, key)] = x = new FlujoExp { Anio = f.Year, Mes = f.Month, Direccion = key };
                return x;
            }
            var porUuid = d.Cfdis.GroupBy(c => c.Uuid).ToDictionary(g => g.Key, g => g.First());

            foreach (var c in d.Cfdis.Where(c => c.EstatusSat != "Cancelado" && (c.Tipo == "I" || c.Tipo == "E")))
            {
                bool ppd = string.Equals(c.MetodoPago, "PPD", StringComparison.OrdinalIgnoreCase);
                if (c.Tipo == "I" && ppd) continue;                       // su IVA entra cuando se paga (REP)
                decimal iva = c.IvaTrasladado * c.Factor;
                if (c.Tipo == "E") Fila(c.Fecha, c.Direccion).IvaEgresos += iva; else Fila(c.Fecha, c.Direccion).IvaPue += iva;
            }

            foreach (var p in d.Pagos)
            {
                if (!porUuid.TryGetValue(p.RepUuid, out var rep) || rep.EstatusSat == "Cancelado") continue;
                decimal iva; bool estimado = false;
                if (p.ImpuestosEnElPago) iva = p.AMxn(p.IvaTrasladadoDr);
                else if (porUuid.TryGetValue(p.DrUuid, out var fac) && fac.Total > 0m) { iva = fac.IvaTrasladado * Math.Min(1m, p.ImportePagado / fac.Total) * fac.Factor; estimado = true; }
                else continue;                                            // factura desconocida: queda como observacion
                var f = Fila(p.FechaPago, p.Direccion); f.IvaPagos += iva; if (estimado) f.IvaPagosEstimado += iva;
            }
            return filas.Values.OrderByDescending(x => x.Anio).ThenByDescending(x => x.Mes).ThenBy(x => x.Direccion).ToList();
        }

        private static List<ObservacionExp> CalcularObservaciones(SqlConnection conn, ExpedienteDatos d)
        {
            var obs = new List<ObservacionExp>();
            string F(CfdiExp c) => (c.Serie ?? "") + (c.Folio ?? "");

            foreach (var x in d.Ppd.Where(x => x.EstatusSat != "Cancelado" && x.Reps == 0 && (DateTime.Today - x.Fecha).TotalDays > 30))
                obs.Add(new ObservacionExp { Nivel = "Revisar", Tema = "PPD sin complemento de pago", Uuid = x.Uuid, Folio = x.Folio, Detalle = "Emitido " + x.Fecha.ToString("yyyy-MM-dd") + " · " + x.Tercero + " · total " + x.Total.ToString("N2", Inv) + " (" + (int)(DateTime.Today - x.Fecha).TotalDays + " días sin REP)" });
            foreach (var x in d.Ppd.Where(x => x.EstatusSat != "Cancelado" && x.Reps > 0 && x.Saldo > 0.01m))
                obs.Add(new ObservacionExp { Nivel = "Info", Tema = "PPD con pago parcial", Uuid = x.Uuid, Folio = x.Folio, Detalle = "Pagado " + x.Pagado.ToString("N2", Inv) + " de " + x.Total.ToString("N2", Inv) + " · saldo " + x.Saldo.ToString("N2", Inv) });

            foreach (var c in d.Cfdis.Where(c => c.Tipo == "I" && c.EstatusSat != "Cancelado"))
            {
                if (string.Equals(c.MetodoPago, "PUE", StringComparison.OrdinalIgnoreCase) && c.FormaPago == "99") obs.Add(Obs("Revisar", "PUE con forma de pago 99", c, "Un CFDI pagado en una exhibicion no debe llevar «por definir»."));
                if (string.Equals(c.MetodoPago, "PPD", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.FormaPago) && c.FormaPago != "99") obs.Add(Obs("Aviso", "PPD con forma de pago distinta de 99", c, "Forma de pago " + c.FormaPago));
                if (c.FormaPago == "01" && c.Total > 2000m && c.Direccion == "Recibido") obs.Add(Obs("Aviso", "Pago en efectivo mayor a $2,000", c, "Total " + c.Total.ToString("N2", Inv) + ": el gasto en efectivo por encima de ese monto no es deducible."));
            }
            foreach (var c in d.Cfdis.Where(c => !string.IsNullOrEmpty(c.Efos) && c.Efos != "200" && c.Efos != "201" && c.Direccion == "Recibido"))
                obs.Add(Obs("Revisar", "Proveedor en listas del SAT (69-B)", c, c.NombreEmisor + " (" + c.RfcEmisor + "): el SAT lo reporta con el código " + c.Efos + "."));
            foreach (var c in d.Cfdis.Where(c => c.EstatusSat == "Cancelado"))
                obs.Add(Obs("Info", "CFDI cancelado", c, (c.FechaCancelacion.HasValue ? "Cancelado el " + c.FechaCancelacion.Value.ToString("yyyy-MM-dd") : "Cancelado") + " · total " + c.Total.ToString("N2", Inv)));

            // REP aplicados a un CFDI cancelado, y REP de facturas que no tenemos.
            var porUuid = d.Cfdis.GroupBy(c => c.Uuid).ToDictionary(g => g.Key, g => g.First());
            foreach (var p in d.Pagos)
            {
                if (p.DrUuid == Guid.Empty) continue;
                if (porUuid.TryGetValue(p.DrUuid, out var fac) && fac.EstatusSat == "Cancelado")
                    obs.Add(new ObservacionExp { Nivel = "Revisar", Tema = "Pago aplicado a un CFDI cancelado", Uuid = p.RepUuid, Folio = p.RepFolio, Detalle = "El pago de " + p.ImportePagado.ToString("N2", Inv) + " se aplicó a " + p.DrFolio + " (" + p.DrUuid.ToString().ToUpperInvariant() + "), que está cancelado." });
                else if (!porUuid.ContainsKey(p.DrUuid) && !p.ImpuestosEnElPago)
                    obs.Add(new ObservacionExp { Nivel = "Aviso", Tema = "Pago de una factura que no está en el periodo", Uuid = p.RepUuid, Folio = p.RepFolio, Detalle = "Paga " + p.DrFolio + " (" + p.DrUuid.ToString().ToUpperInvariant() + "); su IVA no se pudo estimar para el flujo de efectivo. Amplía el periodo." });
                if (!string.IsNullOrEmpty(p.MonedaDr) && !string.Equals(p.MonedaDr, "MXN", StringComparison.OrdinalIgnoreCase))
                    obs.Add(new ObservacionExp { Nivel = "Aviso", Tema = "Pago de un documento en moneda extranjera", Uuid = p.RepUuid, Folio = p.RepFolio, Detalle = "Documento en " + p.MonedaDr + (p.MonedaPago != p.MonedaDr ? ", pagado en " + p.MonedaPago : "") + ": el IVA del flujo de efectivo se convirtió a pesos con la equivalencia y el tipo de cambio del pago; revísalo." });
            }

            // Serie/folio repetidos entre CFDI vigentes del mismo emisor.
            foreach (var g in d.Cfdis.Where(c => c.EstatusSat != "Cancelado" && !string.IsNullOrEmpty(c.Folio)).GroupBy(c => (c.RfcEmisor, c.Tipo, c.Serie ?? "", c.Folio)).Where(g => g.Count() > 1))
                obs.Add(new ObservacionExp { Nivel = "Aviso", Tema = "Serie y folio repetidos", Uuid = g.First().Uuid, Folio = F(g.First()), Detalle = g.Count() + " CFDI vigentes de " + g.Key.RfcEmisor + " con el mismo folio." });
            return obs;
        }
    }
}
