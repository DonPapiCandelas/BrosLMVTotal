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

// ConsolaPasswordHash.cs -- hash salteado (PBKDF2/Rfc2898) de la contraseña de la Consola.
// Enlazado (Link, no copiado) tanto en BrosLMVClsMain.dll (src\BrosLMV.csproj, verifica) como
// en el instalador (instaladores\Empresas, escribe) -- si algun dia cambia el algoritmo, se
// cambia UNA sola vez y ambos lados quedan sincronizados automaticamente.
//
// Deliberadamente NO es una "contrasena encriptada" reversible: no hace falta leerla de vuelta,
// solo comparar. Un hash salteado es mas seguro que cifrado reversible para este caso (si
// alguien copia la BD, no puede recuperar la contrasena real, solo intentar romper el hash).

using System.Security.Cryptography;

namespace BrosLMV
{
    internal static class ConsolaPasswordHash
    {
        private const int TamanoSalBytes = 16;
        private const int TamanoHashBytes = 32;
        public const int IteracionesActuales = 210000; // guia OWASP 2023 para PBKDF2-HMAC-SHA256

        public static void Generar(string password, out byte[] sal, out byte[] hash, out int iteraciones)
        {
            iteraciones = IteracionesActuales;
            using (var derivador = new Rfc2898DeriveBytes(password ?? "", TamanoSalBytes, iteraciones, HashAlgorithmName.SHA256))
            {
                sal = derivador.Salt;
                hash = derivador.GetBytes(TamanoHashBytes);
            }
        }

        public static bool Verificar(string password, byte[] sal, byte[] hashGuardado, int iteraciones)
        {
            if (sal == null || hashGuardado == null || iteraciones <= 0) return false;
            using (var derivador = new Rfc2898DeriveBytes(password ?? "", sal, iteraciones, HashAlgorithmName.SHA256))
            {
                byte[] hashCalculado = derivador.GetBytes(hashGuardado.Length);
                return CompararEnTiempoConstante(hashCalculado, hashGuardado);
            }
        }

        // Comparacion byte a byte SIN salir temprano -- evita que una diferencia de tiempo
        // (timing attack) filtre en cuantos bytes coincidio el intento con el hash real.
        private static bool CompararEnTiempoConstante(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diferencia = 0;
            for (int i = 0; i < a.Length; i++)
                diferencia |= a[i] ^ b[i];
            return diferencia == 0;
        }
    }
}
