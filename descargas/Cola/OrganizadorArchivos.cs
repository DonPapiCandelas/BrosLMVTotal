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

// OrganizadorArchivos.cs -- decide DONDE y con QUE NOMBRE se guarda cada XML en disco. Pedido
// explicito del usuario (2026-08-14): no una convencion fija, sino configurable por empresa --
// algunos quieren organizado por mes, el usuario mismo quiere todo en una sola carpeta plana
// para poder tomarlos y pasarlos a Comercial Pro mientras no esten integrados; y el nombre de
// archivo debe poder incluir Folio/RFC/monto, no solo el UUID. Compartido entre la UI
// (SolicitudWorker via MainWindow) y el CLI (SolicitudWorker via Program.cs), mismo patron que
// SolicitudChunker.cs.

using System;
using System.IO;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.Descargas.Cola
{
    internal static class OrganizadorArchivos
    {
        private static readonly string[] MesesNombre =
        {
            "01-Enero", "02-Febrero", "03-Marzo", "04-Abril", "05-Mayo", "06-Junio",
            "07-Julio", "08-Agosto", "09-Septiembre", "10-Octubre", "11-Noviembre", "12-Diciembre"
        };

        // carpetaBase: Empresa.CarpetaXml -- NULL/vacio usa "xml" relativa (comportamiento de
        // siempre, para no romper empresas creadas antes de que esta columna existiera).
        // estructuraCarpetas: Plana / Anio / AnioMes / AnioTipoMes (Empresa.EstructuraCarpetas).
        // tipo: "Recibidos" o "Emitidos" -- solo se usa en AnioTipoMes.
        public static string ResolverCarpeta(string carpetaBase, string estructuraCarpetas, string tipo, DateTime fechaEmision)
        {
            string baseReal = string.IsNullOrWhiteSpace(carpetaBase) ? "xml" : carpetaBase;
            string anio = fechaEmision.Year.ToString();
            string mes = MesesNombre[fechaEmision.Month - 1];

            switch (estructuraCarpetas)
            {
                case "Anio": return Path.Combine(baseReal, anio);
                case "AnioMes": return Path.Combine(baseReal, anio, mes);
                case "AnioTipoMes": return Path.Combine(baseReal, anio, tipo, mes);
                case "Plana":
                default: return baseReal;
            }
        }

        // Repara archivos que quedaron con el nombre literal "[UUID]" (ver ResolverRutaArchivo): los
        // renombra a <uuid>.xml y actualiza CfdiRecibido.RutaArchivoXml. Idempotente.
        public static int RepararNombresLiterales(Microsoft.Data.SqlClient.SqlConnection conn, string rfc)
        {
            var pendientes = new System.Collections.Generic.List<(int Id, Guid Uuid, string Ruta)>();
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CfdiID, UUID, RutaArchivoXml FROM CfdiRecibido WHERE (RFCReceptor=@r OR RFCEmisor=@r) AND RutaArchivoXml LIKE '%[[]UUID]%'", conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var lector = cmd.ExecuteReader())
                    while (lector.Read()) pendientes.Add((lector.GetInt32(0), lector.GetGuid(1), lector.GetString(2)));
            }

            int arreglados = 0;
            foreach (var (id, uuid, ruta) in pendientes)
            {
                try
                {
                    string nuevo = Path.Combine(Path.GetDirectoryName(ruta), uuid.ToString() + ".xml");
                    if (File.Exists(ruta))
                    {
                        if (File.Exists(nuevo)) File.Delete(ruta); else File.Move(ruta, nuevo);
                    }
                    else if (!File.Exists(nuevo)) continue;
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("UPDATE CfdiRecibido SET RutaArchivoXml=@n WHERE CfdiID=@i", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", nuevo);
                        cmd.Parameters.AddWithValue("@i", id);
                        cmd.ExecuteNonQuery();
                    }
                    arreglados++;
                }
                catch (Exception ex) { Bitacora.EscribirError("    No se pudo renombrar " + ruta + ": " + ex.Message); }
            }
            if (arreglados > 0) Bitacora.Escribir("    " + arreglados + " archivo(s) con nombre \"[UUID]\" renombrados a <uuid>.xml.");
            return arreglados;
        }

        // plantilla: Empresa.PlantillaNombreArchivo, con placeholders {UUID} {Folio} {Serie}
        // {RFCEmisor} {RFCReceptor} {NombreEmisor} {Fecha} {Total} {TipoComprobante}.
        public static string ResolverRutaArchivo(string carpetaDestino, string plantilla, CfdiParseado c)
        {
            string nombre = string.IsNullOrWhiteSpace(plantilla) ? "{UUID}" : plantilla;
            // Se acepta tambien [UUID] (corchetes): una empresa se dio de alta con la plantilla escrita
            // asi y TODOS sus XML quedaron con el nombre literal "[UUID].xml" mas un sufijo.
            foreach (var campo in new[] { "UUID", "Folio", "Serie", "RFCEmisor", "RFCReceptor", "NombreEmisor", "Fecha", "Total", "TipoComprobante" })
                nombre = nombre.Replace("[" + campo + "]", "{" + campo + "}");
            nombre = nombre
                .Replace("{UUID}", c.UUID.ToString())
                .Replace("{Folio}", c.Folio ?? "")
                .Replace("{Serie}", c.Serie ?? "")
                .Replace("{RFCEmisor}", c.RFCEmisor ?? "")
                .Replace("{RFCReceptor}", c.RFCReceptor ?? "")
                .Replace("{NombreEmisor}", c.NombreEmisor ?? "")
                .Replace("{Fecha}", c.FechaEmision.ToString("yyyy-MM-dd"))
                .Replace("{Total}", c.Total?.ToString("0.00") ?? "")
                .Replace("{TipoComprobante}", c.TipoComprobante ?? "");

            foreach (char invalido in Path.GetInvalidFileNameChars())
                nombre = nombre.Replace(invalido, '_');
            if (string.IsNullOrWhiteSpace(nombre)) nombre = c.UUID.ToString();

            string rutaFinal = Path.Combine(carpetaDestino, nombre + ".xml");

            // Colision real (plantilla ambigua sin {UUID}: dos CFDI distintos comparten
            // Folio+RFC+Fecha, por ejemplo) -- nunca sobreescribir el XML de OTRO CFDI, se
            // distingue con un sufijo corto del UUID.
            if (File.Exists(rutaFinal))
                rutaFinal = Path.Combine(carpetaDestino, nombre + "_" + c.UUID.ToString("N").Substring(0, 8) + ".xml");

            return rutaFinal;
        }
    }
}
