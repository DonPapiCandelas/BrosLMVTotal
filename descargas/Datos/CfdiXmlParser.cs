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
        public decimal? Total;
        public string Moneda;
        public DateTime FechaEmision;
        public List<(Guid UuidRelacionado, string TipoRelacion)> Relaciones = new List<(Guid, string)>();
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
                FechaEmision = DateTime.Parse(Atributo(comprobante, "Fecha")),
            };

            // IVA: no siempre es un atributo directo -- se suma desde Impuestos/Traslados si existe.
            var impuestos = comprobante.Element(ns + "Impuestos");
            r.IVA = impuestos != null ? DecimalOpcional(Atributo(impuestos, "TotalImpuestosTrasladados")) : null;

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

            return r;
        }

        private static string Atributo(XElement el, string nombre) => el.Attribute(nombre)?.Value;

        private static decimal? DecimalOpcional(string valor) =>
            !string.IsNullOrEmpty(valor) && decimal.TryParse(valor, out var d) ? d : (decimal?)null;
    }
}
