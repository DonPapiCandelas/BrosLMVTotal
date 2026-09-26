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

// CfdiXmlParser.cs -- extrae SOLO los campos de encabezado + relaciones que se van a usar
// para reportes de conciliacion (ver EsquemaSql.cs). El XML completo se queda en disco como
// fuente de verdad; esto no intenta modelar el 100% del CFDI (complementos de nomina,
// comercio exterior, etc. quedan fuera a proposito).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class CfdiParseado
    {
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string RFCReceptor;
        public string Serie;
        public string Folio;
        public string TipoComprobante;
        public string FormaPago;
        public string MetodoPago;
        public string UsoCFDI;
        public decimal? Subtotal;
        public decimal? Descuento;
        public decimal? IVA;
        public decimal? Retenciones;
        public decimal? Total;
        public string Moneda;
        public decimal? TipoCambio;
        public DateTime FechaEmision;
        public List<(Guid UuidRelacionado, string TipoRelacion)> Relaciones = new List<(Guid, string)>();
        public List<ConceptoParseado> Conceptos = new List<ConceptoParseado>();
        public List<PagoDoctoParseado> PagosDocumentos = new List<PagoDoctoParseado>();
    }

    // Las partidas del CFDI -- deliberadamente solo lo que ya vive en Conceptos/Concepto (ver
    // EsquemaSql.CfdiConcepto): sin desglosar impuestos por partida ni complementos especiales,
    // el XML completo en RutaArchivoXml sigue siendo la fuente de verdad si se necesita mas
    // detalle. Para CFDI de tipo Pago (P) el Concepto suele ser un renglon generico ("Pago",
    // Importe=0) -- sin informacion util, ver PagoDoctoParseado para lo que SI importa en ese caso.
    internal sealed class ConceptoParseado
    {
        public string ClaveProdServ;
        public string Descripcion;
        public decimal? Cantidad;
        public decimal? ValorUnitario;
        public decimal? Importe;
    }

    // Un <DoctoRelacionado> del complemento de Pagos -- el documento (factura) que un CFDI tipo
    // Pago (REP) esta pagando. Esto es lo que de verdad importa mostrar para un REP, no las
    // "partidas" genericas del Concepto (pedido explicito del usuario 2026-08-14: "en lugar de
    // partidas de productos o servicios debe decir los datos de las facturas que paga").
    internal sealed class PagoDoctoParseado
    {
        public Guid UUIDRelacionado;
        public string Serie;
        public string Folio;
        public DateTime? FechaPago;
        public string FormaDePago;
        public string Moneda;
        public int? NumParcialidad;
        public decimal? ImpSaldoAnt;
        public decimal? ImpPagado;
        public decimal? ImpSaldoInsoluto;
    }

    internal static class CfdiXmlParser
    {
        private const string NsTfd = "http://www.sat.gob.mx/TimbreFiscalDigital";

        // Acepta tanto CFDI 3.3 (http://www.sat.gob.mx/cfd/3) como 4.0 (.../cfd/4) -- el
        // namespace real se lee del propio elemento raiz en vez de asumir una version fija.
        public static CfdiParseado Parsear(string xmlContenido)
        {
            var doc = XDocument.Parse(xmlContenido);
            var comprobante = doc.Root;
            if (comprobante == null) throw new InvalidOperationException("XML vacio o sin elemento raiz.");
            XNamespace ns = comprobante.Name.Namespace;

            var emisor = comprobante.Element(ns + "Emisor");
            var receptor = comprobante.Element(ns + "Receptor");
            var tfd = comprobante.Descendants(XNamespace.Get(NsTfd) + "TimbreFiscalDigital").FirstOrDefault();

            if (tfd == null) throw new InvalidOperationException("No se encontro TimbreFiscalDigital (UUID) -- ¿es un CFDI timbrado?");

            var r = new CfdiParseado
            {
                UUID = Guid.Parse(Atributo(tfd, "UUID")),
                RFCEmisor = emisor != null ? Atributo(emisor, "Rfc") : null,
                NombreEmisor = emisor != null ? Atributo(emisor, "Nombre") : null,
                RFCReceptor = receptor != null ? Atributo(receptor, "Rfc") : null,
                Serie = Atributo(comprobante, "Serie"),
                Folio = Atributo(comprobante, "Folio"),
                TipoComprobante = Atributo(comprobante, "TipoDeComprobante"),
                FormaPago = Atributo(comprobante, "FormaPago"),
                MetodoPago = Atributo(comprobante, "MetodoPago"),
                UsoCFDI = receptor != null ? Atributo(receptor, "UsoCFDI") : null,
                Subtotal = DecimalOpcional(Atributo(comprobante, "SubTotal")),
                Descuento = DecimalOpcional(Atributo(comprobante, "Descuento")),
                Total = DecimalOpcional(Atributo(comprobante, "Total")),
                Moneda = Atributo(comprobante, "Moneda"),
                TipoCambio = DecimalOpcional(Atributo(comprobante, "TipoCambio")),
                FechaEmision = DateTime.Parse(Atributo(comprobante, "Fecha")),
            };

            // IVA/Retenciones: no siempre son atributos directos -- se leen de Impuestos si existe.
            var impuestos = comprobante.Element(ns + "Impuestos");
            r.IVA = impuestos != null ? DecimalOpcional(Atributo(impuestos, "TotalImpuestosTrasladados")) : null;
            r.Retenciones = impuestos != null ? DecimalOpcional(Atributo(impuestos, "TotalImpuestosRetenidos")) : null;

            // Los CFDI de tipo "P" (Recibo Electronico de Pago) no traen Total real en el
            // comprobante (viene en 0) -- el monto de verdad esta en el complemento de Pagos, en
            // el nodo <Totales MontoTotalPagos="...">, no en cada <Pago> individual (ese trae
            // "Monto", no "MontoTotalPagos" -- confundirlos suma 0 porque el atributo no existe
            // ahi, visto en vivo con un CFDI real). Se busca por nombre local sin atarse a la
            // version del namespace del complemento (Pagos 1.0 vs Pagos20).
            if (string.Equals(r.TipoComprobante, "P", StringComparison.OrdinalIgnoreCase))
            {
                var totales = comprobante.Descendants().FirstOrDefault(e => e.Name.LocalName == "Totales");
                var montoTotalPagos = totales != null ? DecimalOpcional(Atributo(totales, "MontoTotalPagos")) : null;
                if (montoTotalPagos.HasValue) r.Total = montoTotalPagos.Value;

                // Cada <Pago> puede traer varios <DoctoRelacionado> (un pago que cubre varias
                // facturas) -- una fila por documento, con los datos de FechaPago/FormaDePagoP/
                // MonedaP heredados del <Pago> que lo contiene (esos atributos viven en el padre,
                // no en el DoctoRelacionado). Buscado por nombre local, sin atarse a la version
                // del namespace del complemento (Pagos 1.0 vs Pagos20).
                foreach (var pago in comprobante.Descendants().Where(e => e.Name.LocalName == "Pago"))
                {
                    DateTime? fechaPago = DateTime.TryParse(Atributo(pago, "FechaPago"), out var fp) ? fp : (DateTime?)null;
                    string formaDePago = Atributo(pago, "FormaDePagoP");
                    string monedaPago = Atributo(pago, "MonedaP");

                    foreach (var docto in pago.Elements().Where(e => e.Name.LocalName == "DoctoRelacionado"))
                    {
                        string idDocumento = Atributo(docto, "IdDocumento");
                        if (!Guid.TryParse(idDocumento, out var uuidRel)) continue;

                        r.PagosDocumentos.Add(new PagoDoctoParseado
                        {
                            UUIDRelacionado = uuidRel,
                            Serie = Atributo(docto, "Serie"),
                            Folio = Atributo(docto, "Folio"),
                            FechaPago = fechaPago,
                            FormaDePago = formaDePago,
                            Moneda = monedaPago,
                            NumParcialidad = int.TryParse(Atributo(docto, "NumParcialidad"), out var np) ? np : (int?)null,
                            ImpSaldoAnt = DecimalOpcional(Atributo(docto, "ImpSaldoAnt")),
                            ImpPagado = DecimalOpcional(Atributo(docto, "ImpPagado")),
                            ImpSaldoInsoluto = DecimalOpcional(Atributo(docto, "ImpSaldoInsoluto"))
                        });
                    }
                }
            }

            var relacionados = comprobante.Element(ns + "CfdiRelacionados");
            if (relacionados != null)
            {
                string tipoRelacion = Atributo(relacionados, "TipoRelacion");
                foreach (var cr in relacionados.Elements(ns + "CfdiRelacionado"))
                {
                    string uuidTxt = Atributo(cr, "UUID");
                    if (Guid.TryParse(uuidTxt, out var uuidRel))
                        r.Relaciones.Add((uuidRel, tipoRelacion));
                }
            }

            var conceptos = comprobante.Element(ns + "Conceptos");
            if (conceptos != null)
            {
                foreach (var co in conceptos.Elements(ns + "Concepto"))
                {
                    r.Conceptos.Add(new ConceptoParseado
                    {
                        ClaveProdServ = Atributo(co, "ClaveProdServ"),
                        Descripcion = Atributo(co, "Descripcion"),
                        Cantidad = DecimalOpcional(Atributo(co, "Cantidad")),
                        ValorUnitario = DecimalOpcional(Atributo(co, "ValorUnitario")),
                        Importe = DecimalOpcional(Atributo(co, "Importe"))
                    });
                }
            }

            return r;
        }

        private static string Atributo(XElement el, string nombre) => el.Attribute(nombre)?.Value;

        private static decimal? DecimalOpcional(string valor) =>
            !string.IsNullOrEmpty(valor) && decimal.TryParse(valor, out var d) ? d : (decimal?)null;
    }
}
