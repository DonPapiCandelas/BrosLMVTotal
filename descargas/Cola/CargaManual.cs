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

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal enum ResultadoCarga { Nuevo, YaExistia, OtraEmpresa, NoEsCfdi, Error }

    internal sealed class FilaCarga
    {
        public string Archivo;
        public ResultadoCarga Resultado;
        public string UUID = "";
        public string Tipo = "";      // Recibidos / Emitidos (por los RFC del propio comprobante)
        public string Detalle = "";
    }

    // Carga manual: XML sueltos o ZIP (los que baja el portal del SAT, o los de un proveedor) que se meten por el mismo camino
    // que los paquetes del servicio: se leen, se guardan en la carpeta de la empresa con su estructura, se registran en la base
    // y, si la empresa tiene la carga a Comercial, se pasan igual. Un comprobante solo entra si la empresa es su emisor o su
    // receptor; el tipo (Recibidos/Emitidos) sale de los RFC, no de lo que el usuario crea.
    internal static class CargaManual
    {
        public static async Task<List<FilaCarga>> ImportarAsync(SqlConnection conn, EmpresaFila empresa, IEnumerable<string> rutas)
        {
            var filas = new List<FilaCarga>();
            foreach (var ruta in rutas)
            {
                try
                {
                    if (Directory.Exists(ruta))
                    {
                        foreach (var f in Directory.EnumerateFiles(ruta, "*.*", SearchOption.AllDirectories).Where(EsXmlOZip))
                            await UnaRutaAsync(conn, empresa, f, filas);
                    }
                    else await UnaRutaAsync(conn, empresa, ruta, filas);
                }
                catch (Exception ex) { filas.Add(new FilaCarga { Archivo = Path.GetFileName(ruta), Resultado = ResultadoCarga.Error, Detalle = ex.Message }); }
            }
            return filas;
        }

        private static bool EsXmlOZip(string r) => r.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        private static async Task UnaRutaAsync(SqlConnection conn, EmpresaFila empresa, string ruta, List<FilaCarga> filas)
        {
            string nombre = Path.GetFileName(ruta);
            if (ruta.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var zip = ZipFile.OpenRead(ruta))
                    foreach (var e in zip.Entries.Where(x => x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                    {
                        string contenido;
                        using (var s = e.Open()) using (var l = new StreamReader(s)) contenido = l.ReadToEnd();
                        filas.Add(await UnXmlAsync(conn, empresa, nombre + " › " + e.Name, contenido));
                    }
            }
            else if (ruta.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                filas.Add(await UnXmlAsync(conn, empresa, nombre, File.ReadAllText(ruta)));
            else
                filas.Add(new FilaCarga { Archivo = nombre, Resultado = ResultadoCarga.NoEsCfdi, Detalle = "Solo se aceptan archivos .xml y .zip." });
        }

        private static async Task<FilaCarga> UnXmlAsync(SqlConnection conn, EmpresaFila empresa, string etiqueta, string contenidoXml)
        {
            var fila = new FilaCarga { Archivo = etiqueta };
            CfdiParseado p;
            try { p = CfdiXmlParser.Parsear(contenidoXml); }
            catch (Exception ex) { fila.Resultado = ResultadoCarga.NoEsCfdi; fila.Detalle = "No es un CFDI legible: " + ex.Message; return fila; }

            fila.UUID = p.UUID.ToString().ToUpperInvariant();
            bool emisor = string.Equals(p.RFCEmisor, empresa.RFC, StringComparison.OrdinalIgnoreCase);
            bool receptor = string.Equals(p.RFCReceptor, empresa.RFC, StringComparison.OrdinalIgnoreCase);
            if (!emisor && !receptor)
            {
                fila.Resultado = ResultadoCarga.OtraEmpresa;
                fila.Detalle = "Ni el emisor (" + p.RFCEmisor + ") ni el receptor (" + p.RFCReceptor + ") son " + empresa.RFC + ".";
                return fila;
            }
            string tipo = receptor ? "Recibidos" : "Emitidos";   // si la empresa se factura a si misma, cuenta como recibido
            fila.Tipo = tipo;
            try
            {
                string carpetaBase = string.IsNullOrWhiteSpace(empresa.CarpetaXml) ? "xml" : empresa.CarpetaXml;
                string carpeta = OrganizadorArchivos.ResolverCarpeta(carpetaBase, empresa.EstructuraCarpetas, tipo, p.FechaEmision);
                Directory.CreateDirectory(carpeta);
                string rutaXml = OrganizadorArchivos.ResolverRutaArchivo(carpeta, empresa.PlantillaNombreArchivo, p);

                bool copiadoComercial = false;
                if (!string.IsNullOrWhiteSpace(empresa.ComercialConexionSql))
                {
                    var (res, err) = await ComercialImportador.ImportarAsync(DpapiHelper.DescifrarConexionSql(empresa.ComercialConexionSql), contenidoXml, empresa.RFC,
                        empresa.ComercialCarpetaXmlRecibidos, empresa.ComercialCarpetaXmlEmitidos);
                    if (res == ResultadoImportComercial.Error) fila.Detalle = "No se pudo pasar a Comercial: " + err;
                    else copiadoComercial = true;
                }
                else
                {
                    string carpetaCom = tipo == "Emitidos" ? empresa.ComercialCarpetaXmlEmitidos : empresa.ComercialCarpetaXmlRecibidos;
                    if (!string.IsNullOrWhiteSpace(carpetaCom))
                    {
                        Directory.CreateDirectory(carpetaCom);
                        File.WriteAllText(Path.Combine(carpetaCom, p.UUID + ".xml"), contenidoXml);
                        copiadoComercial = true;
                    }
                }

                // El archivo se escribe solo si es nuevo: un reimporte no debe pisar el XML que ya esta guardado.
                bool yaEstaba = File.Exists(rutaXml);
                if (!yaEstaba) File.WriteAllText(rutaXml, contenidoXml);
                int cfdiId = BrosSatDb.GuardarCfdiRecibido(conn, p, Path.GetFullPath(rutaXml), out bool esNuevo);
                if (esNuevo)
                {
                    BrosSatDb.GuardarRelaciones(conn, p);
                    BrosSatDb.GuardarConceptos(conn, cfdiId, p.Conceptos);
                    BrosSatDb.GuardarPagosDocumentos(conn, cfdiId, p.PagosDocumentos);
                }
                if (copiadoComercial) BrosSatDb.MarcarSincronizadoComercial(conn, p.UUID);
                fila.Resultado = esNuevo ? ResultadoCarga.Nuevo : ResultadoCarga.YaExistia;
                if (!esNuevo && fila.Detalle.Length == 0) fila.Detalle = "Ya estaba en este equipo.";
            }
            catch (Exception ex) { fila.Resultado = ResultadoCarga.Error; fila.Detalle = ex.Message; }
            return fila;
        }
    }
}
