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
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BrosLMV.Empresas
{
    // Conexion a SQL Server del instalador: detecta las instancias del equipo (nada de nombres fijos) y recuerda la conexion de la
    // instalacion anterior para que una ACTUALIZACION no pida servidor/usuario/contrasena otra vez.
    // La credencial se guarda como la guarda el addon (src\Rutas.cs): DPAPI de la maquina en C:\BrosLMV\bin\broslmv_cred.dat, con la misma
    // entropia, asi la leen igual la consola, el runner y el host. Antes el instalador la dejaba en texto plano (broslmv_conn.txt).
    static class Conexion
    {
        const string Bin = @"C:\BrosLMV\bin";
        static readonly string CredFile = Path.Combine(Bin, "broslmv_cred.dat");
        static readonly string ConnFile = Path.Combine(Bin, "broslmv_conn.txt");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BrosLMV.DPAPI.v1.cadena-conexion");

        public sealed class Guardada { public string Servidor, Usuario, Contrasena; }

        // ---- Instancias de SQL Server de ESTE equipo (registro, vista de 64 y de 32 bits) ----
        public static List<string> InstanciasLocales()
        {
            var nombres = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var vista in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, vista))
                    using (var k = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                        if (k != null) foreach (var n in k.GetValueNames()) nombres.Add(n);
                }
                catch { }
            }
            // Respaldo: servicios MSSQL$NOMBRE / MSSQLSERVER (por si el registro no deja leerlo).
            try
            {
                foreach (var s in System.ServiceProcess.ServiceController.GetServices())
                {
                    if (s.ServiceName.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)) nombres.Add(s.ServiceName.Substring(6));
                    else if (s.ServiceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)) nombres.Add("MSSQLSERVER");
                }
            }
            catch { }
            return nombres.Select(n => n.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "localhost" : @"localhost\" + n).ToList();
        }

        // ---- Busqueda en la red (lenta, solo cuando el usuario la pide) ----
        public static List<string> InstanciasEnRed()
        {
            var lista = new List<string>();
            try
            {
                DataTable t = SqlDataSourceEnumerator.Instance.GetDataSources();
                foreach (DataRow r in t.Rows)
                {
                    string srv = Convert.ToString(r["ServerName"]); string inst = Convert.ToString(r["InstanceName"]);
                    if (string.IsNullOrWhiteSpace(srv)) continue;
                    lista.Add(string.IsNullOrWhiteSpace(inst) ? srv : srv + "\\" + inst);
                }
            }
            catch { }
            return lista.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---- Lo que dejo la instalacion anterior ----
        public static Guardada LeerGuardada()
        {
            string cadena = null;
            try
            {
                if (File.Exists(CredFile))
                {
                    byte[] cifra = File.ReadAllBytes(CredFile);
                    if (cifra.Length > 0) cadena = Encoding.UTF8.GetString(ProtectedData.Unprotect(cifra, Entropy, DataProtectionScope.LocalMachine)).Trim();
                }
            }
            catch { cadena = null; }

            if (string.IsNullOrWhiteSpace(cadena))
            {
                try
                {
                    if (File.Exists(ConnFile))
                    {
                        string txt = File.ReadAllText(ConnFile).Trim();
                        if (!string.IsNullOrEmpty(txt) && !txt.StartsWith("#") &&
                            txt.IndexOf("SERVIDOR\\INSTANCIA", StringComparison.OrdinalIgnoreCase) < 0 &&
                            txt.IndexOf("TU_PASSWORD", StringComparison.OrdinalIgnoreCase) < 0) cadena = txt;
                    }
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(cadena)) return null;

            try
            {
                var b = new SqlConnectionStringBuilder(cadena);
                if (string.IsNullOrWhiteSpace(b.DataSource)) return null;
                return new Guardada { Servidor = b.DataSource, Usuario = b.UserID, Contrasena = b.Password };
            }
            catch { return null; }
        }

        // Guarda la conexion cifrada y vacia el .txt plano (mismo aviso que pone el addon).
        public static void Guardar(string servidor, string usuario, string contrasena)
        {
            try
            {
                if (!Directory.Exists(Bin)) return;
                string cadena = "Server=" + servidor + ";User Id=" + usuario + ";Password=" + contrasena + ";TrustServerCertificate=True;";
                File.WriteAllBytes(CredFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(cadena), Entropy, DataProtectionScope.LocalMachine));
                File.WriteAllText(ConnFile,
                    "# La cadena de conexion esta en broslmv_cred.dat (cifrado DPAPI). Actualizada por el instalador el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\r\n" +
                    "# Este archivo ya NO contiene credenciales en texto plano.\r\n");
            }
            catch { }
        }

        // Version del addon que hay instalado ahora (para decir «actualizar de X a Y»). null = no hay instalacion.
        public static string VersionInstalada()
        {
            try
            {
                string dll = Path.Combine(Bin, "BrosLMVClsMain.dll");
                if (!File.Exists(dll)) return null;
                var v = System.Reflection.AssemblyName.GetAssemblyName(dll).Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return null; }
        }
    }
}
