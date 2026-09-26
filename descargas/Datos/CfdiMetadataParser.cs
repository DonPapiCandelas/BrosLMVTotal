using System;
using System.Collections.Generic;
using System.Globalization;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class MetadataParseada
    {
        public Guid UUID;
        public string RFCEmisor, NombreEmisor, RFCReceptor, NombreReceptor, RFCPac;
        public DateTime? FechaEmision, FechaCertificacion, FechaCancelacion;
        public decimal? Total;
        public string EfectoComprobante, EstatusSat, LineaCruda;
    }

    // El SAT documenta Metadata como TXT delimitado por "~". El orden de sus primeras once
    // columnas se mantiene desde CFDI 3.3/4.0; toleramos columnas extra y fechas vacías para no
    // descartar una descarga completa por una sola fila irregular.
    internal static class CfdiMetadataParser
    {
        public static IEnumerable<MetadataParseada> Parsear(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) yield break;
            foreach (var lineaOriginal in texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string linea = lineaOriginal.Trim();
                if (linea.Length == 0 || linea.StartsWith("UUID~", StringComparison.OrdinalIgnoreCase)) continue;
                string[] c = linea.Split('~');
                if (c.Length < 11 || !Guid.TryParse(Valor(c, 0), out var uuid)) continue;
                yield return new MetadataParseada
                {
                    UUID = uuid,
                    RFCEmisor = Valor(c, 1), NombreEmisor = Valor(c, 2), RFCReceptor = Valor(c, 3),
                    NombreReceptor = Valor(c, 4), RFCPac = Valor(c, 5),
                    FechaEmision = Fecha(Valor(c, 6)), FechaCertificacion = Fecha(Valor(c, 7)),
                    Total = Decimal(Valor(c, 8)), EfectoComprobante = Valor(c, 9),
                    EstatusSat = Estado(Valor(c, 10)), FechaCancelacion = Fecha(Valor(c, 11)),
                    LineaCruda = lineaOriginal
                };
            }
        }

        private static string Valor(string[] campos, int indice) => indice < campos.Length ? campos[indice].Trim() : null;
        private static decimal? Decimal(string valor) => decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : (decimal?)null;
        private static DateTime? Fecha(string valor)
        {
            string[] formatos = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd" };
            return DateTime.TryParseExact(valor, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                || DateTime.TryParse(valor, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out f) ? f : (DateTime?)null;
        }
        private static string Estado(string valor) => valor switch { "0" => "Cancelado", "1" => "Vigente", _ => valor };
    }
}
