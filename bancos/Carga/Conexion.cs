// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BrosLMV.Bancos.Carga
{
    // La conexion a la base de la empresa. BrosLMV ya guarda la credencial de cada terminal cifrada (DPAPI, ligada a la maquina) en
    // C:\BrosLMV\bin\broslmv_cred.dat: el programa la reutiliza, asi no hay que pedirle contrasenas a nadie.
    public static class Conexion
    {
        private const string ArchivoCredencial = @"C:\BrosLMV\bin\broslmv_cred.dat";
        private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("BrosLMV.DPAPI.v1.cadena-conexion");

        public static string CredencialDeLaTerminal()
        {
            try
            {
                if (!File.Exists(ArchivoCredencial)) return "";
                var cifra = File.ReadAllBytes(ArchivoCredencial);
                if (cifra.Length == 0) return "";
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(cifra, Entropia, DataProtectionScope.LocalMachine)).Trim();
            }
            catch { return ""; }
        }

        // Cadena para la empresa abierta: servidor y base los dice el boton de Comercial; las credenciales, la terminal.
        // Sin credencial guardada se prueba autenticacion integrada de Windows.
        public static string Para(string servidor, string baseDeDatos)
        {
            string cred = CredencialDeLaTerminal();
            string usuario = Valor(cred, "User Id"), clave = Valor(cred, "Password");
            if (usuario.Length == 0) usuario = Valor(cred, "User ID");
            if (clave.Length > 0)
                return "Server=" + servidor + ";Database=" + baseDeDatos + ";User Id=" + (usuario.Length == 0 ? "SA" : usuario) + ";Password=" + clave + ";TrustServerCertificate=True;Connect Timeout=8";
            return "Server=" + servidor + ";Database=" + baseDeDatos + ";Integrated Security=True;TrustServerCertificate=True;Connect Timeout=8";
        }

        private static string Valor(string cadena, string clave)
        {
            if (string.IsNullOrEmpty(cadena)) return "";
            foreach (var parte in cadena.Split(';'))
            {
                int i = parte.IndexOf('=');
                if (i > 0 && parte.Substring(0, i).Trim().Equals(clave, StringComparison.OrdinalIgnoreCase)) return parte.Substring(i + 1).Trim();
            }
            return "";
        }
    }
}
