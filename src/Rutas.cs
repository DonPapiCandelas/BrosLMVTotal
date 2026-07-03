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

// Rutas.cs
// Rutas centralizadas del producto. Todo vive bajo C:\BrosLMV (independiente de la empresa).

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace BrosLMV
{
    public static class Rutas
    {
        public const string Base     = @"C:\BrosLMV";
        public const string Bin      = Base + @"\bin";
        public const string Scripts  = Base + @"\scripts";
        public const string Logs     = Base + @"\logs";
        public const string Data     = Base + @"\data";
        public const string ConnFile = Bin  + @"\broslmv_conn.txt";  // texto plano (heredado/compat)
        public const string CredFile = Bin  + @"\broslmv_cred.dat";  // cifrado DPAPI (preferente)

        public static void AsegurarCarpetas()
        {
            try { if (!Directory.Exists(Scripts)) Directory.CreateDirectory(Scripts); } catch { }
            try { if (!Directory.Exists(Logs))    Directory.CreateDirectory(Logs); }    catch { }
            try { if (!Directory.Exists(Data))    Directory.CreateDirectory(Data); }    catch { }
        }

        // =========================================================
        //   Credenciales cifradas con DPAPI (B2)
        // =========================================================
        // La cadena de conexión (con la contraseña) ya no vive en texto plano.
        // Se cifra con la API de protección de datos de Windows (DPAPI), ámbito
        // LocalMachine: el secreto queda atado a ESTA máquina (no se puede copiar
        // broslmv_cred.dat a otra PC y leerlo), pero sí lo comparten todos los
        // usuarios de Windows de esta terminal CONTPAQi. La "entropía" adicional
        // exige conocer este salt propio de BrosLMV para descifrar.
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("BrosLMV.DPAPI.v1.cadena-conexion");

        // Cifra y guarda la cadena de conexión en broslmv_cred.dat. La usa el
        // instalador (al "Probar conexión") y la consola. Idempotente.
        public static bool GuardarCredencial(string connString)
        {
            if (string.IsNullOrWhiteSpace(connString)) return false;
            try
            {
                if (!Directory.Exists(Bin)) Directory.CreateDirectory(Bin);
                byte[] claro  = Encoding.UTF8.GetBytes(connString.Trim());
                byte[] cifra  = ProtectedData.Protect(claro, Entropy, DataProtectionScope.LocalMachine);
                File.WriteAllBytes(CredFile, cifra);
                return true;
            }
            catch { return false; }
        }

        // Descifra broslmv_cred.dat. Devuelve "" si no existe o no se puede descifrar
        // (p. ej. el archivo fue copiado de otra máquina).
        public static string LeerCredencial()
        {
            try
            {
                if (!File.Exists(CredFile)) return "";
                byte[] cifra = File.ReadAllBytes(CredFile);
                if (cifra.Length == 0) return "";
                byte[] claro = ProtectedData.Unprotect(cifra, Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(claro).Trim();
            }
            catch { return ""; }
        }

        // Borra la credencial cifrada (al desinstalar o reconfigurar).
        public static void BorrarCredencial()
        {
            try { if (File.Exists(CredFile)) File.Delete(CredFile); } catch { }
        }

        // =========================================================
        //   Resolución de la cadena de conexión (con compat + migración)
        // =========================================================
        // Devuelve la cadena de conexión efectiva. Orden de preferencia:
        //   1) broslmv_cred.dat (cifrado DPAPI) — preferente.
        //   2) broslmv_conn.txt (texto plano heredado). Si trae una cadena REAL
        //      (no la plantilla), se MIGRA: se cifra a .dat y se limpia el .txt
        //      para que no quede la contraseña en claro.
        public static string ConnStr()
        {
            // 1) Cifrado (preferente).
            string cred = LeerCredencial();
            if (!string.IsNullOrEmpty(cred)) return cred;

            // 2) Texto plano heredado (compat).
            try
            {
                if (File.Exists(ConnFile))
                {
                    string txt = File.ReadAllText(ConnFile).Trim();
                    if (string.IsNullOrEmpty(txt)) return "";

                    // Solo migramos/limpiamos si es una cadena real (no la plantilla).
                    if (!EsPlantillaTxt(txt))
                    {
                        if (GuardarCredencial(txt))
                            LimpiarTxtPlano();   // ya está cifrada: borrar el password en claro
                    }
                    return txt;
                }
            }
            catch { }
            return "";
        }

        // Sustituye el contenido del .txt (con la contraseña) por un aviso, una vez
        // que la credencial ya quedó cifrada en .dat. No se borra el archivo para no
        // sorprender a instaladores que lo buscan; solo se vacía de secretos.
        private static void LimpiarTxtPlano()
        {
            try
            {
                string aviso =
                    "# La cadena de conexion se migro a broslmv_cred.dat (cifrado DPAPI) el " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\r\n" +
                    "# Este archivo ya NO contiene credenciales en texto plano.\r\n" +
                    "# Para reconfigurar, usa el instalador (Probar conexion) o Rutas.GuardarCredencial().\r\n";
                File.WriteAllText(ConnFile, aviso);
            }
            catch { }
        }

        // True si el .txt es la plantilla sin rellenar (no una cadena real ni el aviso).
        private static bool EsPlantillaTxt(string cs)
        {
            if (string.IsNullOrEmpty(cs)) return true;
            if (cs.TrimStart().StartsWith("#")) return true;   // ya migrado (aviso)
            return cs.IndexOf("SERVIDOR\\INSTANCIA", StringComparison.OrdinalIgnoreCase) >= 0
                || cs.IndexOf("TU_PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0
                || cs.IndexOf("NOMBRE_BD_DE_LA_EMPRESA", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Carpeta de scripts de UNA empresa: C:\BrosLMV\scripts\<empresa>.
        // Cada base de datos tiene su propia carpeta para que los scripts no se
        // mezclen entre empresas (reglas distintas, mismo nombre de archivo).
        // Si no hay empresa (sin conexion), cae a la carpeta raiz (compartida).
        public static string ScriptsDe(string empresa)
        {
            if (string.IsNullOrWhiteSpace(empresa)) return Scripts;
            foreach (var c in Path.GetInvalidFileNameChars()) empresa = empresa.Replace(c, '_');
            string dir = Path.Combine(Scripts, empresa.Trim());
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }
}
