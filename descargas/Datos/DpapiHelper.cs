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

// DpapiHelper.cs -- cifra/descifra la contrasena de la FIEL para que la descarga corra sola
// (Task Scheduler, sin nadie tecleando la contrasena cada vez). Mismo mecanismo que ya usa
// BrosLMV para la cadena de conexion SQL (src\Rutas.cs): DPAPI atado a ESTA maquina
// (DataProtectionScope.LocalMachine) -- si alguien copia la BD o el archivo a otro equipo, no
// puede descifrarlo ahi. Deliberadamente NO es portable entre maquinas (decision explicita).

using System;
using System.Security.Cryptography;
using System.Text;

namespace BrosLMV.Descargas.Datos
{
    internal static class DpapiHelper
    {
        // Entropia fija propia de este componente (no compartida con Rutas.cs) -- aunque
        // ambas usen DPAPI de la maquina, cada una cifra con su propia "sal" adicional para
        // que un valor cifrado por una no se pueda descifrar por error con la otra.
        private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("BrosLMV.Descargas.DPAPI.v1");

        public static byte[] Cifrar(string textoPlano)
        {
            byte[] datos = Encoding.UTF8.GetBytes(textoPlano ?? "");
            return ProtectedData.Protect(datos, Entropia, DataProtectionScope.LocalMachine);
        }

        public static string Descifrar(byte[] datosCifrados)
        {
            byte[] datos = ProtectedData.Unprotect(datosCifrados, Entropia, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(datos);
        }

        // Empresa.ComercialConexionSql se guarda como Base64(Cifrar(cadena)) desde 2026-08-19
        // (antes se guardaba la cadena de conexion en texto plano -- incluia usuario/contraseña
        // SQL sin cifrar). Con fallback a texto plano para no romper el unico valor de prueba
        // que ya existia en la BD antes de este cambio.
        public static string DescifrarConexionSql(string valorAlmacenado)
        {
            if (string.IsNullOrWhiteSpace(valorAlmacenado)) return null;
            try { return Descifrar(Convert.FromBase64String(valorAlmacenado)); }
            catch { return valorAlmacenado; }
        }
    }
}
