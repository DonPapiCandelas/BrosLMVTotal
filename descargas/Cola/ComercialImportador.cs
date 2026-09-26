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

// ComercialImportador.cs -- pedido explicito del usuario (2026-08-18): "configurar que se
// carguen los emitidos, porque esos los tuve que cargar manualmente" -- automatiza el mismo
// resultado que el boton nativo "Importar" de Comercial Pro (Opciones > CFDI), escribiendo
// directo en las 2 tablas de staging (docDocumentCFDiSAT / docDocumentItemCFDiSAT) de la base
// de Comercial. Esto es la "Etapa 1" documentada en material interno
// (docs/01 y docs/05, confirmado real contra EmpresaA): un INSERT directo, sin stored
// procedures ni triggers, y SOLO staging -- no crea documentos ni polizas contables (eso es la
// Etapa 2, deliberadamente NO automatizada, ver docs/04 seccion "Opcion C").
//
// Guardarraíl principal: nunca hay 2 conexiones abiertas contra la misma BD que la de
// BrosLMV.Descargas -- esta es la PRIMERA vez que la herramienta escribe en la base de un
// tercero (Comercial), separada explicitamente en su propio archivo por eso mismo.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal enum ResultadoImportComercial { Insertado, YaExistia, Error }

    internal static class ComercialImportador
    {
        // Idempotente: si el UUID ya esta en docDocumentCFDiSAT (ya sea porque el usuario le dio
        // "Importar" el mismo a mano, o porque una corrida anterior ya lo inserto), no hace nada
        // y regresa YaExistia -- nunca hay fila duplicada. No hay UNIQUE constraint real sobre
        // UUID en la tabla (confirmado con sys.indexes contra EmpresaA), asi que esta
        // verificacion de la app ES la unica proteccion contra duplicados.
        public static async Task<(ResultadoImportComercial resultado, string error)> ImportarAsync(string comercialConexionSql, string contenidoXml, string rfcPropio)
        {
            try
            {
                var doc = XDocument.Parse(contenidoXml);
                var comprobante = doc.Root;
                XNamespace ns = comprobante.Name.Namespace;
                var emisor = comprobante.Element(ns + "Emisor");
                var receptor = comprobante.Element(ns + "Receptor");
                var tfd = comprobante.Descendants(XNamespace.Get("http://www.sat.gob.mx/TimbreFiscalDigital") + "TimbreFiscalDigital").FirstOrDefault();
                if (tfd == null) return (ResultadoImportComercial.Error, "Sin TimbreFiscalDigital (UUID).");

                string uuid = Atributo(tfd, "UUID");
                string rfcEmisor = Atributo(emisor, "Rfc");
                string rfcReceptor = Atributo(receptor, "Rfc");
                bool esEmitido = string.Equals(rfcEmisor, rfcPropio, StringComparison.OrdinalIgnoreCase);
                // RazonSocial en docDocumentCFDiSAT es siempre la CONTRAPARTE (no la empresa
                // propia) -- confirmado contra filas reales ya importadas nativamente (hallazgo 05).
                string razonSocial = esEmitido ? Atributo(receptor, "Nombre") : Atributo(emisor, "Nombre");

                var (ivaTotal, iva0, iva8, iva16, iepsTotal, ivaRet, isrRet) = DesglosarImpuestosEncabezado(comprobante, ns);

                using (var conn = new SqlConnection(comercialConexionSql))
                {
                    await conn.OpenAsync().ConfigureAwait(false);

                    using (var cmdExiste = new SqlCommand("SELECT DocSATID FROM docDocumentCFDiSAT WHERE UUID = @Uuid", conn))
                    {
                        cmdExiste.Parameters.AddWithValue("@Uuid", uuid);
                        var existente = await cmdExiste.ExecuteScalarAsync().ConfigureAwait(false);
                        if (existente != null) return (ResultadoImportComercial.YaExistia, null);
                    }

                    const string sqlInsertCabecera = @"
INSERT INTO docDocumentCFDiSAT
    (OwnedBusinessEntityID, DocumentID, UUID, TipoComprobante, RFCEmisor, RFCReceptor, RFCPAC, RazonSocial,
     FechaEmision, FechaCertificacion, Total, Status, ConciliationStatusID, XMLFileName, XMLSource,
     SubTotal, Descuento, IVA0, IVA8, IVA16, IEPS, IVARetenido, ISRRetenido, TipoEmisionID,
     Serie, Folio, Moneda, TipoCambio, FormaDePago, CondicionesDePago, MetodoDePago, Version, UsoCFDI,
     Exportacion, DomicilioFiscalReceptor, RegimenFiscalReceptor)
OUTPUT INSERTED.DocSATID
VALUES
    (1, 0, @Uuid, @TipoComprobante, @RfcEmisor, @RfcReceptor, @RfcPac, @RazonSocial,
     @FechaEmision, @FechaCertificacion, @Total, 'Vigente', 0, @XmlFileName, 0,
     @SubTotal, @Descuento, @Iva0, @Iva8, @Iva16, @Ieps, @IvaRetenido, @IsrRetenido, @TipoEmisionID,
     @Serie, @Folio, @Moneda, @TipoCambio, @FormaPago, @CondicionesPago, @MetodoPago, @Version, @UsoCfdi,
     @Exportacion, @DomicilioFiscalReceptor, @RegimenFiscalReceptor);";

                    long docSatId;
                    using (var cmd = new SqlCommand(sqlInsertCabecera, conn))
                    {
                        cmd.Parameters.AddWithValue("@Uuid", uuid);
                        cmd.Parameters.AddWithValue("@TipoComprobante", (object)TextoTipoComprobante(Atributo(comprobante, "TipoDeComprobante")) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@RfcEmisor", rfcEmisor);
                        cmd.Parameters.AddWithValue("@RfcReceptor", rfcReceptor);
                        cmd.Parameters.AddWithValue("@RfcPac", (object)Atributo(tfd, "RfcProvCertif") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@RazonSocial", (object)razonSocial ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FechaEmision", DateTime.Parse(Atributo(comprobante, "Fecha")));
                        var fechaCert = Atributo(tfd, "FechaTimbrado");
                        cmd.Parameters.AddWithValue("@FechaCertificacion", DateTime.TryParse(fechaCert, out var fc) ? (object)fc : DBNull.Value);
                        cmd.Parameters.AddWithValue("@Total", Decimal(Atributo(comprobante, "Total")));
                        cmd.Parameters.AddWithValue("@XmlFileName", uuid.ToLowerInvariant() + ".xml");
                        cmd.Parameters.AddWithValue("@SubTotal", Decimal(Atributo(comprobante, "SubTotal")));
                        cmd.Parameters.AddWithValue("@Descuento", Decimal(Atributo(comprobante, "Descuento")));
                        cmd.Parameters.AddWithValue("@Iva0", iva0);
                        cmd.Parameters.AddWithValue("@Iva8", iva8);
                        cmd.Parameters.AddWithValue("@Iva16", iva16);
                        cmd.Parameters.AddWithValue("@Ieps", iepsTotal);
                        cmd.Parameters.AddWithValue("@IvaRetenido", ivaRet);
                        cmd.Parameters.AddWithValue("@IsrRetenido", isrRet);
                        cmd.Parameters.AddWithValue("@TipoEmisionID", esEmitido ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Serie", (object)Atributo(comprobante, "Serie") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Folio", (object)Atributo(comprobante, "Folio") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Moneda", (object)Atributo(comprobante, "Moneda") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@TipoCambio", Atributo(comprobante, "TipoCambio") is string tc && decimal.TryParse(tc, out var tcv) ? (object)tcv : DBNull.Value);
                        cmd.Parameters.AddWithValue("@FormaPago", (object)TextoCatalogo(TextosFormaPago, Atributo(comprobante, "FormaPago")) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CondicionesPago", (object)Atributo(comprobante, "CondicionesDePago") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@MetodoPago", (object)TextoCatalogo(TextosMetodoPago, Atributo(comprobante, "MetodoPago")) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Version", (object)Atributo(comprobante, "Version") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@UsoCfdi", (object)TextoCatalogo(TextosUsoCfdi, Atributo(receptor, "UsoCFDI")) ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Exportacion", (object)Atributo(comprobante, "Exportacion") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DomicilioFiscalReceptor", (object)Atributo(receptor, "DomicilioFiscalReceptor") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@RegimenFiscalReceptor", (object)Atributo(receptor, "RegimenFiscalReceptor") ?? DBNull.Value);
                        docSatId = (long)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    }

                    var conceptos = comprobante.Element(ns + "Conceptos");
                    if (conceptos != null)
                    {
                        foreach (var co in conceptos.Elements(ns + "Concepto"))
                        {
                            var (ivaPerc, ivaTrasladado, iepsPerc, iepsTrasladado, ivaRetPerc, ivaRetenido, isrRetPerc, isrRetenido) = DesglosarImpuestosConcepto(co, ns);

                            const string sqlItem = @"
INSERT INTO docDocumentItemCFDiSAT
    (DocSATID, Cantidad, Unidad, NoIdentificacion, Descripcion, ValorUnitario, Importe, Descuento,
     IVAPerc, IEPSPerc, IVARetenidoPerc, ISRRetenidoPerc, ProductID, ExpenseTypeID,
     ClaveProdServ, ClaveUnidad, IVATrasladado, IEPSTrasladado, IVARetenido, ISRRetenido, ObjetoImpuesto)
VALUES
    (@DocSatId, @Cantidad, @Unidad, @NoIdentificacion, @Descripcion, @ValorUnitario, @Importe, @Descuento,
     @IvaPerc, @IepsPerc, @IvaRetPerc, @IsrRetPerc, 0, 0,
     @ClaveProdServ, @ClaveUnidad, @IvaTrasladado, @IepsTrasladado, @IvaRetenido, @IsrRetenido, @ObjetoImpuesto);";

                            using (var cmd = new SqlCommand(sqlItem, conn))
                            {
                                cmd.Parameters.AddWithValue("@DocSatId", docSatId);
                                cmd.Parameters.AddWithValue("@Cantidad", Decimal(Atributo(co, "Cantidad")));
                                cmd.Parameters.AddWithValue("@Unidad", (object)Atributo(co, "Unidad") ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@NoIdentificacion", (object)Atributo(co, "NoIdentificacion") ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@Descripcion", (object)Atributo(co, "Descripcion") ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ValorUnitario", Decimal(Atributo(co, "ValorUnitario")));
                                cmd.Parameters.AddWithValue("@Importe", Decimal(Atributo(co, "Importe")));
                                cmd.Parameters.AddWithValue("@Descuento", Decimal(Atributo(co, "Descuento")));
                                cmd.Parameters.AddWithValue("@IvaPerc", ivaPerc);
                                cmd.Parameters.AddWithValue("@IepsPerc", iepsPerc);
                                cmd.Parameters.AddWithValue("@IvaRetPerc", ivaRetPerc);
                                cmd.Parameters.AddWithValue("@IsrRetPerc", isrRetPerc);
                                cmd.Parameters.AddWithValue("@ClaveProdServ", (object)Atributo(co, "ClaveProdServ") ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ClaveUnidad", (object)Atributo(co, "ClaveUnidad") ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@IvaTrasladado", ivaTrasladado);
                                cmd.Parameters.AddWithValue("@IepsTrasladado", iepsTrasladado);
                                cmd.Parameters.AddWithValue("@IvaRetenido", ivaRetenido);
                                cmd.Parameters.AddWithValue("@IsrRetenido", isrRetenido);
                                // ObjetoImp (no "ObjetoImpuesto") es el nombre real del atributo en el
                                // XML -- confirmado inspeccionando un CFDI 4.0 real.
                                cmd.Parameters.AddWithValue("@ObjetoImpuesto", (object)Atributo(co, "ObjetoImp") ?? DBNull.Value);
                                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                    }

                    return (ResultadoImportComercial.Insertado, (string)null);
                }
            }
            catch (Exception ex)
            {
                return (ResultadoImportComercial.Error, ex.Message);
            }
        }

        // Impuesto="002" = IVA (agrupado por TasaOCuota: 0.16/0.08/0.00), "003" = IEPS, dentro de
        // Traslados. Retenciones: "002" = IVA retenido, "001" = ISR retenido. Mismo catalogo c_Impuesto
        // del SAT en encabezado y en cada Concepto.
        private static (decimal total, decimal iva0, decimal iva8, decimal iva16, decimal ieps, decimal ivaRet, decimal isrRet) DesglosarImpuestosEncabezado(XElement comprobante, XNamespace ns)
        {
            var impuestos = comprobante.Element(ns + "Impuestos");
            if (impuestos == null) return (0, 0, 0, 0, 0, 0, 0);

            decimal iva0 = 0, iva8 = 0, iva16 = 0, ieps = 0, ivaRet = 0, isrRet = 0;
            var traslados = impuestos.Element(ns + "Traslados");
            if (traslados != null)
            {
                foreach (var t in traslados.Elements(ns + "Traslado"))
                {
                    string impuesto = Atributo(t, "Impuesto");
                    decimal importe = Decimal(Atributo(t, "Importe"));
                    decimal tasa = Decimal(Atributo(t, "TasaOCuota"));
                    if (impuesto == "002") // IVA
                    {
                        if (tasa >= 0.15m) iva16 += importe;
                        else if (tasa >= 0.05m) iva8 += importe;
                        else iva0 += importe;
                    }
                    else if (impuesto == "003") ieps += importe; // IEPS
                }
            }
            var retenciones = impuestos.Element(ns + "Retenciones");
            if (retenciones != null)
            {
                foreach (var r in retenciones.Elements(ns + "Retencion"))
                {
                    string impuesto = Atributo(r, "Impuesto");
                    decimal importe = Decimal(Atributo(r, "Importe"));
                    if (impuesto == "002") ivaRet += importe;
                    else if (impuesto == "001") isrRet += importe;
                }
            }
            decimal total = Decimal(Atributo(impuestos, "TotalImpuestosTrasladados"));
            return (total, iva0, iva8, iva16, ieps, ivaRet, isrRet);
        }

        private static (decimal ivaPerc, decimal ivaTrasladado, decimal iepsPerc, decimal iepsTrasladado,
            decimal ivaRetPerc, decimal ivaRetenido, decimal isrRetPerc, decimal isrRetenido) DesglosarImpuestosConcepto(XElement concepto, XNamespace ns)
        {
            var impuestos = concepto.Element(ns + "Impuestos");
            if (impuestos == null) return (0, 0, 0, 0, 0, 0, 0, 0);

            decimal ivaPerc = 0, ivaTrasladado = 0, iepsPerc = 0, iepsTrasladado = 0;
            var traslados = impuestos.Element(ns + "Traslados");
            if (traslados != null)
            {
                foreach (var t in traslados.Elements(ns + "Traslado"))
                {
                    string impuesto = Atributo(t, "Impuesto");
                    if (impuesto == "002") { ivaPerc = Decimal(Atributo(t, "TasaOCuota")); ivaTrasladado += Decimal(Atributo(t, "Importe")); }
                    else if (impuesto == "003") { iepsPerc = Decimal(Atributo(t, "TasaOCuota")); iepsTrasladado += Decimal(Atributo(t, "Importe")); }
                }
            }
            decimal ivaRetPerc = 0, ivaRetenido = 0, isrRetPerc = 0, isrRetenido = 0;
            var retenciones = impuestos.Element(ns + "Retenciones");
            if (retenciones != null)
            {
                foreach (var r in retenciones.Elements(ns + "Retencion"))
                {
                    string impuesto = Atributo(r, "Impuesto");
                    if (impuesto == "002") { ivaRetPerc = Decimal(Atributo(r, "TasaOCuota")); ivaRetenido += Decimal(Atributo(r, "Importe")); }
                    else if (impuesto == "001") { isrRetPerc = Decimal(Atributo(r, "TasaOCuota")); isrRetenido += Decimal(Atributo(r, "Importe")); }
                }
            }
            return (ivaPerc, ivaTrasladado, iepsPerc, iepsTrasladado, ivaRetPerc, ivaRetenido, isrRetPerc, isrRetenido);
        }

        // Comercial NO guarda el codigo crudo del SAT en TipoComprobante -- guarda el texto
        // amigable completo ("I - Ingreso") literal en la columna. Confirmado 2026-08-19
        // comparando contra EmpresaA real: las 1340 filas del import nativo original
        // (DocSATID 1-1340) tienen "I - Ingreso"/"E - Egreso"/"P - Pago"; escribir solo la letra
        // ("I") hacia que el arbol de "XML Recibidos" mostrara un grupo aparte y duplicado para
        // el mismo tipo -- eso fue el bug que reporto el usuario ("no puede haber dos tipos de
        // CFDI de ingreso"). N/T no se vieron nunca en datos nativos reales (sin Nomina/Traslado
        // capturado todavia) -- se sigue el mismo patron de las 3 confirmadas.
        private static string TextoTipoComprobante(string codigo)
        {
            switch (codigo)
            {
                case "I": return "I - Ingreso";
                case "E": return "E - Egreso";
                case "P": return "P - Pago";
                case "N": return "N - Nomina";
                case "T": return "T - Traslado";
                default: return codigo;
            }
        }

        // Mismo hallazgo que TipoComprobante (ver TextoTipoComprobante) pero para FormaPago,
        // MetodoPago y UsoCFDI -- reportado por el usuario 2026-08-19 ("estan diferentes lo de
        // forma y metodo de pago"). Texto extraido LITERAL de las vistas propias de Comercial
        // (vwcboAnexo20v33_FormaPago, vwcboAnexo20v33_MetodoDePago, vwcboAnexo20v40_UsoCFDI) --
        // no es el catalogo "oficial" del SAT de memoria, es el que Comercial realmente tiene
        // instalado (difieren en detalles menores: sin acento en "mercancias", con punto final
        // en descripciones G/I/D pero no en CP01/CN01). Si el codigo no esta en el diccionario
        // (catalogo no confirmado o version mas nueva que la que tiene Comercial), se guarda el
        // codigo crudo tal cual en vez de fallar -- mejor que perder el CFDI completo.
        private static readonly Dictionary<string, string> TextosFormaPago = new Dictionary<string, string>
        {
            ["01"] = "01 - Efectivo",
            ["02"] = "02 - Cheque nominativo",
            ["03"] = "03 - Transferencia electrónica de fondos",
            ["04"] = "04 - Tarjeta de crédito",
            ["05"] = "05 - Monedero electrónico",
            ["06"] = "06 - Dinero electrónico",
            ["08"] = "08 - Vales de despensa",
            ["12"] = "12 - Dación en pago",
            ["13"] = "13 - Pago por subrogación",
            ["14"] = "14 - Pago por consignación",
            ["15"] = "15 - Condonación",
            ["17"] = "17 - Compensación",
            ["23"] = "23 - Novación",
            ["24"] = "24 - Confusión",
            ["25"] = "25 - Remisión de deuda",
            ["26"] = "26 - Prescripción o caducidad",
            ["27"] = "27 - A satisfacción del acreedor",
            ["28"] = "28 - Tarjeta de débito",
            ["29"] = "29 - Tarjeta de servicios",
            ["30"] = "30 - Aplicación de anticipos",
            ["31"] = "31 - Intermediario pagos",
            ["99"] = "99 - Por definir",
        };

        private static readonly Dictionary<string, string> TextosMetodoPago = new Dictionary<string, string>
        {
            ["PUE"] = "PUE - Pago en una sola exhibición",
            ["PPD"] = "PPD - Pago en parcialidades o diferido",
        };

        // CN01 no existe en vwcboAnexo20v40_UsoCFDI (version de Comercial instalada es anterior a
        // la actualizacion del catalogo del SAT que lo agrego) -- "CN01 - Nómina" es la mejor
        // aproximacion siguiendo el patron de CP01, NO esta confirmado contra el catalogo propio
        // de Comercial como el resto.
        private static readonly Dictionary<string, string> TextosUsoCfdi = new Dictionary<string, string>
        {
            ["G01"] = "G01 - Adquisición de mercancias.",
            ["G02"] = "G02 - Devoluciones, descuentos o bonificaciones.",
            ["G03"] = "G03 - Gastos en general.",
            ["I01"] = "I01 - Construcciones.",
            ["I02"] = "I02 - Mobilario y equipo de oficina por inversiones.",
            ["I03"] = "I03 - Equipo de transporte.",
            ["I04"] = "I04 - Equipo de computo y accesorios.",
            ["I05"] = "I05 - Dados, troqueles, moldes, matrices y herramental.",
            ["I06"] = "I06 - Comunicaciones telefónicas.",
            ["I07"] = "I07 - Comunicaciones satelitales.",
            ["I08"] = "I08 - Otra maquinaria y equipo.",
            ["D01"] = "D01 - Honorarios médicos, dentales y gastos hospitalarios.",
            ["D02"] = "D02 - Gastos médicos por incapacidad o discapacidad.",
            ["D03"] = "D03 - Gastos funerales.",
            ["D04"] = "D04 - Donativos.",
            ["D05"] = "D05 - Intereses reales efectivamente pagados por créditos hipotecarios (casa habitación).",
            ["D06"] = "D06 - Aportaciones voluntarias al SAR.",
            ["D07"] = "D07 - Primas por seguros de gastos médicos.",
            ["D08"] = "D08 - Gastos de transportación escolar obligatoria.",
            ["D09"] = "D09 - Depósitos en cuentas para el ahorro, primas que tengan como base planes de pensiones.",
            ["D10"] = "D10 - Pagos por servicios educativos (colegiaturas).",
            ["S01"] = "S01 - Sin efectos fiscales.",
            ["CP01"] = "CP01 - Pagos",
            ["CN01"] = "CN01 - Nómina",
        };

        private static string TextoCatalogo(Dictionary<string, string> catalogo, string codigo) =>
            codigo != null && catalogo.TryGetValue(codigo, out var texto) ? texto : codigo;

        private static string Atributo(XElement el, string nombre) => el?.Attribute(nombre)?.Value;
        private static decimal Decimal(string valor) => !string.IsNullOrEmpty(valor) && decimal.TryParse(valor, out var d) ? d : 0m;
    }
}
