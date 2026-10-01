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

// Respaldo.cs -- respaldo CIFRADO de la configuracion de las empresas (FIEL .cer/.key, su contrasena,
// carpetas, conexion a Comercial). Las contrasenas se guardan en la base con DPAPI de ESTA maquina, asi
// que si el servidor se reemplaza hay que volver a capturarlas una por una; con este archivo, una sola
// contrasena de respaldo restaura todo. Se queda en tus manos: no se envia a ningun lado.
//
// Formato: "BROSLMVBK1" + sal(16) + nonce(12) + etiqueta(16) + cifrado AES-256-GCM del JSON.
// Clave: PBKDF2-SHA256 de la contrasena de respaldo, 600,000 iteraciones. Cualquier cambio al archivo
// (o una contrasena equivocada) hace fallar el descifrado.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class EmpresaRespaldo
    {
        public string Nombre { get; set; }
        public string RFC { get; set; }
        public bool Activa { get; set; }
        public string CarpetaXml { get; set; }
        public string EstructuraCarpetas { get; set; }
        public string PlantillaNombreArchivo { get; set; }
        public string ComercialCarpetaXmlRecibidos { get; set; }
        public string ComercialCarpetaXmlEmitidos { get; set; }
        public string ComercialConexionSql { get; set; }   // en claro DENTRO del archivo cifrado
        public int? AnioInicioDescargas { get; set; }
        public string PasswordFiel { get; set; }            // en claro DENTRO del archivo cifrado
        public string NombreCer { get; set; }
        public string CerBase64 { get; set; }
        public string NombreKey { get; set; }
        public string KeyBase64 { get; set; }
    }

    internal sealed class ContenidoRespaldo
    {
        public int Version { get; set; } = 1;
        public string Fecha { get; set; }
        public List<EmpresaRespaldo> Empresas { get; set; } = new List<EmpresaRespaldo>();
    }

    internal static class Respaldo
    {
        private const string Magia = "BROSLMVBK1";
        private const int Iteraciones = 600000;

        public static byte[] Cifrar(string texto, string contrasena)
        {
            if (string.IsNullOrEmpty(contrasena) || contrasena.Length < 8) throw new ArgumentException("La contrasena de respaldo debe tener al menos 8 caracteres.");
            byte[] sal = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12);
            byte[] clave = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(contrasena), sal, Iteraciones, HashAlgorithmName.SHA256, 32);
            byte[] plano = Encoding.UTF8.GetBytes(texto), cifrado = new byte[plano.Length], etiqueta = new byte[16];
            using (var aes = new AesGcm(clave, 16)) aes.Encrypt(nonce, plano, cifrado, etiqueta, Encoding.ASCII.GetBytes(Magia));
            using (var ms = new MemoryStream())
            {
                ms.Write(Encoding.ASCII.GetBytes(Magia)); ms.Write(sal); ms.Write(nonce); ms.Write(etiqueta); ms.Write(cifrado);
                return ms.ToArray();
            }
        }

        public static string Descifrar(byte[] archivo, string contrasena)
        {
            int cab = Magia.Length;
            if (archivo == null || archivo.Length < cab + 16 + 12 + 16 || Encoding.ASCII.GetString(archivo, 0, cab) != Magia)
                throw new InvalidDataException("El archivo no es un respaldo de BrosLMV.");
            byte[] sal = archivo.Skip(cab).Take(16).ToArray(), nonce = archivo.Skip(cab + 16).Take(12).ToArray(), etiqueta = archivo.Skip(cab + 28).Take(16).ToArray();
            byte[] cifrado = archivo.Skip(cab + 44).ToArray(), plano = new byte[cifrado.Length];
            byte[] clave = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(contrasena ?? ""), sal, Iteraciones, HashAlgorithmName.SHA256, 32);
            try
            {
                using (var aes = new AesGcm(clave, 16)) aes.Decrypt(nonce, cifrado, etiqueta, plano, Encoding.ASCII.GetBytes(Magia));
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("Contrasena de respaldo incorrecta, o el archivo esta danado.");
            }
            return Encoding.UTF8.GetString(plano);
        }

        public static int Exportar(SqlConnection conn, string archivo, string contrasena)
        {
            var contenido = new ContenidoRespaldo { Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            foreach (var e in BrosSatDb.ObtenerEmpresas(conn))
            {
                contenido.Empresas.Add(new EmpresaRespaldo
                {
                    Nombre = e.Nombre, RFC = e.RFC, Activa = e.Activa, CarpetaXml = e.CarpetaXml, EstructuraCarpetas = e.EstructuraCarpetas,
                    PlantillaNombreArchivo = e.PlantillaNombreArchivo, ComercialCarpetaXmlRecibidos = e.ComercialCarpetaXmlRecibidos,
                    ComercialCarpetaXmlEmitidos = e.ComercialCarpetaXmlEmitidos, ComercialConexionSql = DpapiHelper.DescifrarConexionSql(e.ComercialConexionSql),
                    AnioInicioDescargas = e.AnioInicioDescargas, PasswordFiel = DpapiHelper.Descifrar(e.PasswordCifrada),
                    NombreCer = Path.GetFileName(e.RutaCer), CerBase64 = File.Exists(e.RutaCer) ? Convert.ToBase64String(File.ReadAllBytes(e.RutaCer)) : null,
                    NombreKey = Path.GetFileName(e.RutaKey), KeyBase64 = File.Exists(e.RutaKey) ? Convert.ToBase64String(File.ReadAllBytes(e.RutaKey)) : null
                });
            }
            File.WriteAllBytes(archivo, Cifrar(JsonSerializer.Serialize(contenido), contrasena));
            return contenido.Empresas.Count;
        }

        // Restaura las empresas del respaldo: escribe el .cer/.key en carpetaFiel\<RFC>\ y crea la empresa (o actualiza la
        // que ya exista con ese RFC). Las contrasenas se vuelven a cifrar con DPAPI de ESTA maquina.
        public static int Importar(SqlConnection conn, string archivo, string contrasena, string carpetaFiel)
        {
            var contenido = JsonSerializer.Deserialize<ContenidoRespaldo>(Descifrar(File.ReadAllBytes(archivo), contrasena));
            int n = 0;
            foreach (var r in contenido.Empresas)
            {
                string carpeta = Path.Combine(carpetaFiel, r.RFC);
                Directory.CreateDirectory(carpeta);
                string cer = Path.Combine(carpeta, r.NombreCer ?? (r.RFC + ".cer")), key = Path.Combine(carpeta, r.NombreKey ?? (r.RFC + ".key"));
                if (r.CerBase64 != null) File.WriteAllBytes(cer, Convert.FromBase64String(r.CerBase64));
                if (r.KeyBase64 != null) File.WriteAllBytes(key, Convert.FromBase64String(r.KeyBase64));

                byte[] pwd = DpapiHelper.Cifrar(r.PasswordFiel);
                string conexion = string.IsNullOrWhiteSpace(r.ComercialConexionSql) ? null : Convert.ToBase64String(DpapiHelper.Cifrar(r.ComercialConexionSql));
                var existente = BrosSatDb.ObtenerEmpresas(conn).FirstOrDefault(e => e.RFC == r.RFC);
                if (existente == null)
                    BrosSatDb.GuardarEmpresaNueva(conn, r.Nombre, r.RFC, cer, key, pwd, r.CarpetaXml, r.EstructuraCarpetas ?? "AnioTipoMes", r.PlantillaNombreArchivo ?? "{UUID}",
                        r.ComercialCarpetaXmlRecibidos, r.ComercialCarpetaXmlEmitidos, conexion, r.AnioInicioDescargas);
                else
                    BrosSatDb.ActualizarEmpresa(conn, existente.EmpresaID, r.Nombre, cer, key, pwd, r.CarpetaXml, r.EstructuraCarpetas ?? "AnioTipoMes",
                        r.PlantillaNombreArchivo ?? "{UUID}", r.ComercialCarpetaXmlRecibidos, r.ComercialCarpetaXmlEmitidos, conexion);
                n++;
            }
            return n;
        }
    }
}
