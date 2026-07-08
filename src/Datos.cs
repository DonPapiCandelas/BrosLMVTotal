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

// Datos.cs
// Almacenamiento local en SQLite (un solo archivo, a nivel de equipo): auditoria de
// ejecuciones, recientes y favoritos. Portatil, sin servidor, se instala con el botón.

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace BrosLMV
{
    public static class Datos
    {
        private static bool _init;
        private static bool _ok;

        private static string CnnStr()
        {
            return "Data Source=" + Path.Combine(Rutas.Data, "broslmv.db") + ";Version=3;";
        }

        // Crea la BD y las tablas si no existen. Tolerante a fallos (si SQLite no
        // cargara, la consola sigue funcionando sin auditoria).
        public static bool Inicializar()
        {
            if (_init) return _ok;
            _init = true;
            try
            {
                // Forzar que SQLite busque su nativo junto a NUESTRAS DLLs (no en la
                // carpeta de ComercialSP). Debe ir ANTES del primer uso de SQLite.
                Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", Rutas.Bin);
                if (!Directory.Exists(Rutas.Data)) Directory.CreateDirectory(Rutas.Data);

                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    Exec(c, @"CREATE TABLE IF NOT EXISTS ejecuciones(
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        fecha TEXT, empresa TEXT, modulo INTEGER, usuario INTEGER,
                        script TEXT, origen TEXT, duracion_ms INTEGER, filas INTEGER,
                        estado TEXT, error TEXT);");
                    Exec(c, @"CREATE TABLE IF NOT EXISTS recientes(
                        nombre TEXT PRIMARY KEY, fecha TEXT);");
                    Exec(c, @"CREATE TABLE IF NOT EXISTS favoritos(
                        nombre TEXT PRIMARY KEY);");
                }
                _ok = true;
            }
            catch { _ok = false; }
            return _ok;
        }

        private static void Exec(SQLiteConnection c, string sql)
        {
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        // ---- Auditoria ----
        public static void RegistrarEjecucion(string empresa, int modulo, int usuario,
            string script, string origen, long duracionMs, int filas, string estado, string error)
        {
            if (!Inicializar()) return;
            try
            {
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO ejecuciones
                            (fecha,empresa,modulo,usuario,script,origen,duracion_ms,filas,estado,error)
                            VALUES (@f,@e,@m,@u,@s,@o,@d,@fi,@es,@er);";
                        cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@e", empresa ?? "");
                        cmd.Parameters.AddWithValue("@m", modulo);
                        cmd.Parameters.AddWithValue("@u", usuario);
                        cmd.Parameters.AddWithValue("@s", script ?? "");
                        cmd.Parameters.AddWithValue("@o", origen ?? "");
                        cmd.Parameters.AddWithValue("@d", duracionMs);
                        cmd.Parameters.AddWithValue("@fi", filas);
                        cmd.Parameters.AddWithValue("@es", estado ?? "");
                        cmd.Parameters.AddWithValue("@er", error ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }

        public static List<Dictionary<string, object>> UltimasEjecuciones(int n)
        {
            var lista = new List<Dictionary<string, object>>();
            if (!Inicializar()) return lista;
            try
            {
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT fecha,empresa,modulo,usuario,script,origen,duracion_ms,filas,estado,error FROM ejecuciones ORDER BY id DESC LIMIT @n;";
                        cmd.Parameters.AddWithValue("@n", n);
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                                lista.Add(row);
                            }
                    }
                }
            }
            catch { }
            return lista;
        }

        // ---- Recientes ----
        public static void AgregarReciente(string nombre)
        {
            if (string.IsNullOrEmpty(nombre) || !Inicializar()) return;
            try
            {
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO recientes(nombre,fecha) VALUES(@n,@f) ON CONFLICT(nombre) DO UPDATE SET fecha=@f;";
                        cmd.Parameters.AddWithValue("@n", nombre);
                        cmd.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }

        public static List<string> Recientes(int n)
        {
            var lista = new List<string>();
            if (!Inicializar()) return lista;
            try
            {
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT nombre FROM recientes ORDER BY fecha DESC LIMIT @n;";
                        cmd.Parameters.AddWithValue("@n", n);
                        using (var r = cmd.ExecuteReader()) while (r.Read()) lista.Add(r.GetString(0));
                    }
                }
            }
            catch { }
            return lista;
        }

        // ---- Favoritos ----
        public static List<string> Favoritos()
        {
            var lista = new List<string>();
            if (!Inicializar()) return lista;
            try
            {
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT nombre FROM favoritos ORDER BY nombre;";
                        using (var r = cmd.ExecuteReader()) while (r.Read()) lista.Add(r.GetString(0));
                    }
                }
            }
            catch { }
            return lista;
        }

        public static bool EsFavorito(string nombre)
        {
            return Favoritos().Contains(nombre);
        }

        public static void ToggleFavorito(string nombre)
        {
            if (string.IsNullOrEmpty(nombre) || !Inicializar()) return;
            try
            {
                bool es = EsFavorito(nombre);
                using (var c = new SQLiteConnection(CnnStr()))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = es ? "DELETE FROM favoritos WHERE nombre=@n;" : "INSERT OR IGNORE INTO favoritos(nombre) VALUES(@n);";
                        cmd.Parameters.AddWithValue("@n", nombre);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }
    }
}
