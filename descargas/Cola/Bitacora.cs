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

// Bitacora.cs -- pedido explicito del usuario (2026-08-14): "no me da bitacora o logs". Todo
// salia por Console.WriteLine, que se pierde cuando el proceso corre en segundo plano via Tarea
// Programada (/RU SYSTEM, sin consola visible). Ubicacion FIJA (no relativa a donde corre el
// proceso) para que la UI y el CLI, sin importar desde donde se invoquen, escriban siempre al
// mismo lugar y el usuario sepa donde buscar.
//
// %ProgramData% (no %LocalAppData%) -- bug real encontrado 2026-09-07: LocalApplicationData
// resuelve distinto segun quien corre el proceso. Con el Servicio de Windows corriendo como
// LocalSystem (ver descargas-servicio), sus logs se iban al perfil de SYSTEM
// (C:\Windows\System32\config\systemprofile\AppData\Local\...), invisibles para el usuario --
// la UI y el CLI interactivo seguian escribiendo en su propio %LocalAppData%, asi que dos
// bitacoras separadas "se veian vacias" segun quien las mirara. ProgramData es compartida entre
// cualquier cuenta de la misma maquina (mismo patron ya usado en ConfigServicio.cs).

using System;
using System.IO;

namespace BrosLMV.Descargas.Cola
{
    // "public" (no "internal"): descargas-servicio la usa directo via ProjectReference.
    public static class Bitacora
    {
        private static readonly object Candado = new object();

        public static string CarpetaLogs =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "logs");

        public static void Escribir(string mensaje)
        {
            Console.WriteLine(mensaje);
            Anotar(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + mensaje);
        }

        public static void EscribirError(string mensaje)
        {
            Console.Error.WriteLine(mensaje);
            Anotar(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ERROR: " + mensaje);
        }

        // La bitacora NUNCA debe tumbar a quien la usa. Visto en vivo (2026-10-01): una pasada
        // lanzada por un usuario fallo con "Access denied" al escribir el log que el servicio
        // (cuenta SYSTEM) habia creado, y la excepcion mataba el proceso. En el servicio, esa misma
        // excepcion fuera de un try detenia las descargas hasta reiniciarlo a mano. Si la ruta
        // compartida no se puede escribir se usa una de respaldo en el perfil del usuario, y si
        // tampoco se puede, se descarta la linea en silencio.
        private static void Anotar(string linea)
        {
            lock (Candado)
            {
                string archivo = "broslmv-" + DateTime.Now.ToString("yyyy-MM") + ".log";
                foreach (var carpeta in new[] { CarpetaLogs, CarpetaLogsRespaldo })
                {
                    try
                    {
                        Directory.CreateDirectory(carpeta);
                        File.AppendAllText(Path.Combine(carpeta, archivo), linea + Environment.NewLine);
                        return;
                    }
                    catch { /* se prueba la siguiente ubicacion */ }
                }
            }
        }

        public static string CarpetaLogsRespaldo =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrosLMV", "Descargas", "logs");
    }
}
