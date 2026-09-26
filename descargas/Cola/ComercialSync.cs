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

// ComercialSync.cs -- pedido explicito del usuario (2026-08-18): "ya baje mas de mil CFDI,
// necesitamos importarlos". La copia/importacion automatica hacia Comercial (agregada en
// SolicitudWorker en esta misma sesion) solo aplica a descargas NUEVAS -- todo lo que ya estaba
// descargado antes de ese cambio nunca se proceso. Este es el "backfill" de una sola vez: recorre
// TODOS los CFDI ya descargados de la empresa (no vuelve a pedir nada al SAT, no gasta cupo) y,
// segun este configurada la empresa, o los importa directo a la base de Comercial (mismo
// resultado que el boton nativo "Importar", via ComercialImportador) o copia el archivo a
// XMLRecibidos/XMLEmitidos para que el usuario le de "Importar" el mismo. Idempotente en ambos
// casos -- correrlo varias veces no duplica nada.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal sealed class ResultadoSincronizacion
    {
        public int ImportadosRecibidos;
        public int ImportadosEmitidos;
        public int CopiadosRecibidos;
        public int CopiadosEmitidos;
        public int SinArchivoEnDisco;
        public int Errores;
        public int SinCarpetaConfigurada;
    }

    internal static class ComercialSync
    {
        public static async Task<ResultadoSincronizacion> SincronizarTodoAsync(SqlConnection conn, EmpresaFila empresa)
        {
            var resultado = new ResultadoSincronizacion();

            bool tieneImportDirecto = !string.IsNullOrWhiteSpace(empresa.ComercialConexionSql);
            bool tieneRecibidos = !string.IsNullOrWhiteSpace(empresa.ComercialCarpetaXmlRecibidos);
            bool tieneEmitidos = !string.IsNullOrWhiteSpace(empresa.ComercialCarpetaXmlEmitidos);
            if (!tieneImportDirecto && !tieneRecibidos && !tieneEmitidos)
            {
                Bitacora.Escribir("  " + empresa.Nombre + ": sin integracion con Comercial configurada, nada que sincronizar.");
                return resultado;
            }

            var cfdis = BrosSatDb.ObtenerCfdiConArchivo(conn, empresa.RFC);
            Bitacora.Escribir("  " + empresa.Nombre + ": " + cfdis.Count + " CFDI con XML en disco, sincronizando...");

            foreach (var (uuid, rutaArchivoXml, esEmitido) in cfdis)
            {
                if (!File.Exists(rutaArchivoXml))
                {
                    resultado.SinArchivoEnDisco++;
                    continue;
                }

                if (tieneImportDirecto)
                {
                    try
                    {
                        string contenidoXml = File.ReadAllText(rutaArchivoXml);
                        var (r, errorImport) = await ComercialImportador.ImportarAsync(
                            DpapiHelper.DescifrarConexionSql(empresa.ComercialConexionSql), contenidoXml, empresa.RFC);
                        if (r == ResultadoImportComercial.Error)
                        {
                            resultado.Errores++;
                            Bitacora.EscribirError("    No se pudo importar " + uuid + " a la base de Comercial: " + errorImport);
                            continue;
                        }
                        BrosSatDb.MarcarSincronizadoComercial(conn, uuid);
                        if (esEmitido) resultado.ImportadosEmitidos++; else resultado.ImportadosRecibidos++;
                    }
                    catch (Exception ex)
                    {
                        resultado.Errores++;
                        Bitacora.EscribirError("    No se pudo leer/importar " + uuid + ": " + ex.Message);
                    }
                    continue;
                }

                string carpetaDestino = esEmitido ? empresa.ComercialCarpetaXmlEmitidos : empresa.ComercialCarpetaXmlRecibidos;
                if (string.IsNullOrWhiteSpace(carpetaDestino))
                {
                    resultado.SinCarpetaConfigurada++;
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(carpetaDestino);
                    string rutaComercial = Path.Combine(carpetaDestino, uuid + ".xml");
                    File.Copy(rutaArchivoXml, rutaComercial, overwrite: true);
                    BrosSatDb.MarcarSincronizadoComercial(conn, uuid);
                    if (esEmitido) resultado.CopiadosEmitidos++; else resultado.CopiadosRecibidos++;
                }
                catch (Exception ex)
                {
                    resultado.Errores++;
                    Bitacora.EscribirError("    No se pudo copiar " + uuid + " a " + carpetaDestino + ": " + ex.Message);
                }
            }

            Bitacora.Escribir("  " + empresa.Nombre + ": " + resultado.ImportadosRecibidos + " Recibidos + " + resultado.ImportadosEmitidos +
                " Emitidos importados directo a Comercial, " + resultado.CopiadosRecibidos + " Recibidos + " + resultado.CopiadosEmitidos +
                " Emitidos copiados a carpeta, " + resultado.SinArchivoEnDisco + " sin archivo en disco, " + resultado.Errores + " con error.");
            return resultado;
        }
    }
}
