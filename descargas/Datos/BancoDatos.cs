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

// BancoDatos.cs -- cuentas bancarias, perfiles de formato, importaciones y movimientos en la base propia de Descargas.
// Todo es LOCAL: el estado de cuenta nunca sale de esta maquina.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class BancoCuentaFila
    {
        public int Id { get; set; }
        public string Alias { get; set; } = "";
        public string Banco { get; set; } = "";
        public string Numero { get; set; } = "";
        public string Clabe { get; set; } = "";
        public string Moneda { get; set; } = "MXN";
        public string Tipo { get; set; } = "Cuenta";
        public string CuentaContable { get; set; } = "";
        public bool Activa { get; set; } = true;
        public int Movimientos { get; set; }
        public DateTime? UltimaFecha { get; set; }
        public string UltimaTexto => UltimaFecha.HasValue ? UltimaFecha.Value.ToString("yyyy-MM-dd") : "";
        public string Terminacion => Regex.Replace(string.IsNullOrEmpty(Numero) ? (Clabe ?? "") : Numero, @"\D", "") is string d && d.Length > 4 ? d.Substring(d.Length - 4) : Numero;
        public string Etiqueta => Alias + "  ·  " + Banco + (Terminacion.Length > 0 ? " ···" + Terminacion : "") + (Moneda != "MXN" ? "  [" + Moneda + "]" : "");
        public override string ToString() => Etiqueta;
    }

    internal sealed class BancoSugerencia
    {
        public BancoCuentaFila Cuenta;       // null: no hay base para elegir sola
        public string Motivo = "";
        public bool Segura;                  // por numero de cuenta; las demas se preseleccionan pero se avisa
    }

    internal sealed class BancoImportado { public int ImportacionId, Nuevos, Duplicados; }

    internal sealed class BancoMovFila
    {
        public long Id { get; set; }
        public string Cuenta { get; set; } = "";
        public DateTime Fecha { get; set; }
        public int Orden { get; set; }
        public string Descripcion { get; set; } = "";
        public string Detalle { get; set; } = "";
        public string Referencia { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string ClaveRastreo { get; set; } = "";
        public string RfcContraparte { get; set; } = "";
        public string NombreContraparte { get; set; } = "";
        public string Estado { get; set; } = "";
        public decimal Cargo { get; set; }
        public decimal Abono { get; set; }
        public decimal? Saldo { get; set; }
    }

    internal sealed class BancoImportacionFila
    {
        public int Id { get; set; }
        public string Cuenta { get; set; } = "";
        public string Archivo { get; set; } = "";
        public string Perfil { get; set; } = "";
        public string Notas { get; set; } = "";
        public DateTime Fecha { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public int Leidos { get; set; }
        public int Nuevos { get; set; }
        public int Duplicados { get; set; }
        public decimal? SaldoInicial { get; set; }
        public decimal? SaldoFinal { get; set; }
        public string Periodo => Desde.HasValue ? Desde.Value.ToString("yyyy-MM-dd") + " a " + (Hasta ?? Desde.Value).ToString("yyyy-MM-dd") : "";
    }

    internal sealed class BancoPerfilGuardado { public int Id; public string Nombre, Banco; public BancoMapa Mapa; }

    internal static class BancoDatos
    {
        // ---------- Cuentas ----------
        public static List<BancoCuentaFila> ObtenerCuentas(SqlConnection conn, string rfc, bool inactivas = false)
        {
            var l = new List<BancoCuentaFila>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT c.Id, c.Alias, c.Banco, c.Numero, c.Clabe, c.Moneda, c.Tipo, c.CuentaContable, c.Activa,
       (SELECT COUNT(*) FROM BancoMovimiento m WHERE m.CuentaId = c.Id), (SELECT MAX(Fecha) FROM BancoMovimiento m WHERE m.CuentaId = c.Id)
FROM BancoCuenta c WHERE c.RFC = @r AND (@i = 1 OR c.Activa = 1) ORDER BY c.Banco, c.Alias";
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@i", inactivas ? 1 : 0);
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        l.Add(new BancoCuentaFila
                        {
                            Id = rd.GetInt32(0), Alias = rd.GetString(1), Banco = rd.GetString(2), Numero = rd.GetString(3), Clabe = rd.IsDBNull(4) ? "" : rd.GetString(4),
                            Moneda = rd.GetString(5), Tipo = rd.GetString(6), CuentaContable = rd.IsDBNull(7) ? "" : rd.GetString(7), Activa = rd.GetBoolean(8),
                            Movimientos = rd.GetInt32(9), UltimaFecha = rd.IsDBNull(10) ? (DateTime?)null : rd.GetDateTime(10)
                        });
            }
            return l;
        }

        public static string ValidarCuenta(BancoCuentaFila c)
        {
            if (string.IsNullOrWhiteSpace(c.Alias)) return "Escribe un nombre para reconocer la cuenta (por ejemplo «Banorte nómina»).";
            if (string.IsNullOrWhiteSpace(c.Banco)) return "Elige el banco.";
            string num = Regex.Replace(c.Numero ?? "", @"\D", "");
            if (num.Length < 4) return "Escribe el número de cuenta (o al menos los últimos 4 dígitos): con él reconozco a qué cuenta pertenece cada archivo.";
            if (!string.IsNullOrWhiteSpace(c.Clabe) && !Regex.IsMatch(c.Clabe.Trim(), @"^\d{18}$")) return "La CLABE debe tener 18 dígitos.";
            return null;
        }

        public static int GuardarCuenta(SqlConnection conn, string rfc, BancoCuentaFila c)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = c.Id == 0
                    ? "INSERT INTO BancoCuenta (RFC, Alias, Banco, Numero, Clabe, Moneda, Tipo, CuentaContable, Activa) OUTPUT INSERTED.Id VALUES (@r, @a, @b, @n, @cl, @m, @t, @cc, @act)"
                    : "UPDATE BancoCuenta SET Alias=@a, Banco=@b, Numero=@n, Clabe=@cl, Moneda=@m, Tipo=@t, CuentaContable=@cc, Activa=@act WHERE Id=@id AND RFC=@r; SELECT @id";
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@id", c.Id);
                cmd.Parameters.AddWithValue("@a", c.Alias.Trim()); cmd.Parameters.AddWithValue("@b", c.Banco.Trim());
                cmd.Parameters.AddWithValue("@n", Regex.Replace(c.Numero ?? "", @"\s", "")); cmd.Parameters.AddWithValue("@cl", string.IsNullOrWhiteSpace(c.Clabe) ? (object)DBNull.Value : c.Clabe.Trim());
                cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(c.Moneda) ? "MXN" : c.Moneda.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("@t", c.Tipo ?? "Cuenta");
                cmd.Parameters.AddWithValue("@cc", string.IsNullOrWhiteSpace(c.CuentaContable) ? (object)DBNull.Value : c.CuentaContable.Trim()); cmd.Parameters.AddWithValue("@act", c.Activa);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // Una cuenta con movimientos no se borra (se da de baja); sin movimientos se borra de verdad.
        public static bool EliminarOBaja(SqlConnection conn, string rfc, int id)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"IF EXISTS (SELECT 1 FROM BancoMovimiento WHERE CuentaId=@id) BEGIN UPDATE BancoCuenta SET Activa=0 WHERE Id=@id AND RFC=@r; SELECT 0; END
ELSE BEGIN DELETE FROM BancoCuenta WHERE Id=@id AND RFC=@r; SELECT 1; END";
                cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@r", rfc);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        public static Dictionary<int, (DateTime fecha, decimal saldo)> UltimosSaldos(SqlConnection conn, string rfc)
        {
            var d = new Dictionary<int, (DateTime, decimal)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT CuentaId, Fecha, Saldo FROM (
    SELECT CuentaId, Fecha, Saldo, ROW_NUMBER() OVER (PARTITION BY CuentaId ORDER BY Fecha DESC, Orden DESC, Id DESC) rn FROM BancoMovimiento WHERE RFC=@r AND Saldo IS NOT NULL) x WHERE rn = 1";
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var rd = cmd.ExecuteReader()) while (rd.Read()) d[rd.GetInt32(0)] = (rd.GetDateTime(1), rd.GetDecimal(2));
            }
            return d;
        }

        // ---------- Que cuenta es ----------
        // Por orden de certeza: numero de cuenta/CLABE que dice el archivo (o su nombre) > saldo inicial = ultimo saldo cargado > unica cuenta de ese banco.
        // Si hay varias cuentas del mismo banco y nada las distingue NO se elige sola: el usuario decide.
        public static BancoSugerencia SugerirCuenta(IList<BancoCuentaFila> cuentas, string banco, IEnumerable<string> numerosArchivo, string nombreArchivo,
            decimal? saldoInicial, IDictionary<int, (DateTime fecha, decimal saldo)> ultimos)
        {
            var activas = cuentas.Where(c => c.Activa).ToList();
            var nums = (numerosArchivo ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            foreach (var n in Regex.Matches(nombreArchivo ?? "", @"\d{4,18}").Cast<Match>()) nums.Add(n.Value);

            var porNumero = activas.Where(c => nums.Any(n => BancoArchivo.MismaCuenta(c.Numero, n) || BancoArchivo.MismaCuenta(c.Clabe, n))).ToList();
            if (porNumero.Count == 1) return new BancoSugerencia { Cuenta = porNumero[0], Segura = true, Motivo = "Coincide con el número de cuenta que trae el archivo." };
            if (porNumero.Count > 1) return new BancoSugerencia { Motivo = "El número del archivo coincide con varias cuentas (" + string.Join(", ", porNumero.Select(c => c.Alias)) + "): elige la correcta." };

            string bn = BancoTexto.Norm(banco);
            var delBanco = bn.Length == 0 ? activas : activas.Where(c => BancoTexto.Norm(c.Banco) == bn).ToList();
            if (saldoInicial.HasValue && ultimos != null)
            {
                var porSaldo = delBanco.Where(c => ultimos.TryGetValue(c.Id, out var u) && Math.Abs(u.saldo - saldoInicial.Value) <= 0.011m).ToList();
                if (porSaldo.Count == 1)
                {
                    var u = ultimos[porSaldo[0].Id];
                    return new BancoSugerencia { Cuenta = porSaldo[0], Motivo = "El saldo con el que abre el archivo (" + saldoInicial.Value.ToString("C2") + ") es el último saldo que cargaste de esta cuenta (" + u.fecha.ToString("yyyy-MM-dd") + "). Confírmalo." };
                }
            }
            if (delBanco.Count == 1) return new BancoSugerencia { Cuenta = delBanco[0], Motivo = "Es tu única cuenta " + (bn.Length > 0 ? "de " + banco : "registrada") + "." };
            if (delBanco.Count == 0) return new BancoSugerencia { Motivo = "Aún no tienes una cuenta " + (bn.Length > 0 ? "de " + banco + " " : "") + "registrada: créala con «Nueva cuenta»." };
            return new BancoSugerencia { Motivo = "Tienes " + delBanco.Count + " cuentas " + (bn.Length > 0 ? "de " + banco : "") + " y el archivo no dice cuál es: elige la cuenta." };
        }

        // ---------- Perfiles de formato ----------
        public static void GuardarPerfil(SqlConnection conn, string rfc, string nombre, string banco, BancoMapa mapa)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"DELETE FROM BancoPerfil WHERE RFC=@r AND Firma=@f;
INSERT INTO BancoPerfil (RFC, Nombre, Banco, Firma, MapaJson) VALUES (@r, @n, @b, @f, @j)";
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@n", nombre); cmd.Parameters.AddWithValue("@b", (object)banco ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@f", Recortar(mapa.Firma)); cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(mapa));
                cmd.ExecuteNonQuery();
            }
        }

        private static string Recortar(string firma) => firma != null && firma.Length > 450 ? firma.Substring(0, 450) : firma ?? "";

        public static BancoPerfilGuardado BuscarPerfil(SqlConnection conn, string rfc, string firma)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT TOP 1 Id, Nombre, Banco, MapaJson FROM BancoPerfil WHERE RFC=@r AND Firma=@f ORDER BY Id DESC";
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@f", Recortar(firma));
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read()) return null;
                    return new BancoPerfilGuardado { Id = rd.GetInt32(0), Nombre = rd.GetString(1), Banco = rd.IsDBNull(2) ? "" : rd.GetString(2), Mapa = JsonSerializer.Deserialize<BancoMapa>(rd.GetString(3)) };
                }
            }
        }

        // ---------- Importar ----------
        public static List<string> ArchivosYaCargados(SqlConnection conn, string rfc, string sha256)
        {
            var l = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT i.Fecha, c.Alias FROM BancoImportacion i JOIN BancoCuenta c ON c.Id = i.CuentaId WHERE i.RFC=@r AND i.Sha256=@s ORDER BY i.Fecha";
                cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@s", sha256);
                using (var rd = cmd.ExecuteReader()) while (rd.Read()) l.Add(rd.GetDateTime(0).ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " en " + rd.GetString(1));
            }
            return l;
        }

        // Cuantos de estos movimientos ya estan cargados en la cuenta (por huella).
        public static int ContarDuplicados(SqlConnection conn, int cuentaId, IList<BancoMov> movs)
        {
            if (movs.Count == 0) return 0;
            var existentes = new HashSet<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Hash FROM BancoMovimiento WHERE CuentaId=@c AND Fecha BETWEEN @d AND @h";
                cmd.Parameters.AddWithValue("@c", cuentaId); cmd.Parameters.AddWithValue("@d", movs.Min(m => m.Fecha)); cmd.Parameters.AddWithValue("@h", movs.Max(m => m.Fecha));
                using (var rd = cmd.ExecuteReader()) while (rd.Read()) existentes.Add(rd.GetString(0).Trim());
            }
            return movs.Count(m => existentes.Contains(m.Hash));
        }

        // El estado de cuenta anterior debe terminar donde empieza este. Si no, probablemente falta un periodo.
        public static string AvisoDeContinuidad(SqlConnection conn, int cuentaId, DateTime desde, decimal? saldoInicial)
        {
            if (!saldoInicial.HasValue) return null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT TOP 1 Fecha, Saldo FROM BancoMovimiento WHERE CuentaId=@c AND Saldo IS NOT NULL AND Fecha < @d ORDER BY Fecha DESC, Orden DESC, Id DESC;
SELECT COUNT(*) FROM BancoMovimiento WHERE CuentaId=@c AND Fecha >= @d;";
                cmd.Parameters.AddWithValue("@c", cuentaId); cmd.Parameters.AddWithValue("@d", desde.Date);
                using (var rd = cmd.ExecuteReader())
                {
                    DateTime? f = null; decimal s = 0;
                    if (rd.Read()) { f = rd.GetDateTime(0); s = rd.GetDecimal(1); }
                    rd.NextResult(); rd.Read(); int posteriores = rd.GetInt32(0);
                    if (f == null || posteriores > 0) return null;           // sin antecedente, o ya hay periodo encimado: la huella evita duplicados
                    if (Math.Abs(s - saldoInicial.Value) <= 0.011m) return null;
                    return "El último saldo que tienes cargado de esta cuenta es " + s.ToString("C2") + " (" + f.Value.ToString("yyyy-MM-dd") + ") y este archivo abre con " + saldoInicial.Value.ToString("C2") +
                        ": puede faltar un periodo entre los dos, o el archivo es de otra cuenta.";
                }
            }
        }

        public static BancoImportado Importar(SqlConnection conn, string rfc, int cuentaId, string archivo, string sha256, string perfil, IList<BancoMov> movs,
            decimal? saldoInicial, decimal? saldoFinal, string notas)
        {
            if (movs.Count == 0) return new BancoImportado();
            DateTime desde = movs.Min(m => m.Fecha), hasta = movs.Max(m => m.Fecha);
            using (var tx = conn.BeginTransaction())
            {
                var existentes = new HashSet<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT Hash FROM BancoMovimiento WHERE CuentaId=@c AND Fecha BETWEEN @d AND @h";
                    cmd.Parameters.AddWithValue("@c", cuentaId); cmd.Parameters.AddWithValue("@d", desde); cmd.Parameters.AddWithValue("@h", hasta);
                    using (var rd = cmd.ExecuteReader()) while (rd.Read()) existentes.Add(rd.GetString(0).Trim());
                }
                var nuevos = movs.Where(m => !existentes.Contains(m.Hash)).ToList();

                int impId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO BancoImportacion (RFC, CuentaId, Archivo, Sha256, Perfil, Desde, Hasta, Leidos, Nuevos, Duplicados, SaldoInicial, SaldoFinal, Notas)
OUTPUT INSERTED.Id VALUES (@r, @c, @a, @s, @p, @d, @h, @l, @n, @du, @si, @sf, @no)";
                    cmd.Parameters.AddWithValue("@r", rfc); cmd.Parameters.AddWithValue("@c", cuentaId); cmd.Parameters.AddWithValue("@a", System.IO.Path.GetFileName(archivo));
                    cmd.Parameters.AddWithValue("@s", sha256); cmd.Parameters.AddWithValue("@p", (object)perfil ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@d", desde); cmd.Parameters.AddWithValue("@h", hasta);
                    cmd.Parameters.AddWithValue("@l", movs.Count); cmd.Parameters.AddWithValue("@n", nuevos.Count); cmd.Parameters.AddWithValue("@du", movs.Count - nuevos.Count);
                    cmd.Parameters.AddWithValue("@si", (object)saldoInicial ?? DBNull.Value); cmd.Parameters.AddWithValue("@sf", (object)saldoFinal ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@no", string.IsNullOrEmpty(notas) ? (object)DBNull.Value : (notas.Length > 1000 ? notas.Substring(0, 1000) : notas));
                    impId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO BancoMovimiento (RFC, CuentaId, ImportacionId, Fecha, Orden, Descripcion, Detalle, Referencia, Cargo, Abono, Saldo, Tipo, ClaveRastreo, RfcContraparte, NombreContraparte, ClabeContraparte, BancoContraparte, Hash)
VALUES (@r, @c, @i, @f, @o, @de, @dt, @re, @ca, @ab, @sa, @ti, @cr, @rf, @nc, @cl, @bc, @h)";
                    var p = cmd.Parameters;
                    p.Add("@r", System.Data.SqlDbType.NVarChar, 13); p.Add("@c", System.Data.SqlDbType.Int); p.Add("@i", System.Data.SqlDbType.Int); p.Add("@f", System.Data.SqlDbType.Date); p.Add("@o", System.Data.SqlDbType.Int);
                    p.Add("@de", System.Data.SqlDbType.NVarChar, 1000); p.Add("@dt", System.Data.SqlDbType.NVarChar, -1); p.Add("@re", System.Data.SqlDbType.NVarChar, 200);
                    p.Add("@ca", System.Data.SqlDbType.Decimal); p.Add("@ab", System.Data.SqlDbType.Decimal); p.Add("@sa", System.Data.SqlDbType.Decimal);
                    p.Add("@ti", System.Data.SqlDbType.NVarChar, 30); p.Add("@cr", System.Data.SqlDbType.NVarChar, 40); p.Add("@rf", System.Data.SqlDbType.NVarChar, 13); p.Add("@nc", System.Data.SqlDbType.NVarChar, 120);
                    p.Add("@cl", System.Data.SqlDbType.NVarChar, 18); p.Add("@bc", System.Data.SqlDbType.NVarChar, 60); p.Add("@h", System.Data.SqlDbType.Char, 40);
                    foreach (var d in new[] { "@ca", "@ab", "@sa" }) { p[d].Precision = 19; p[d].Scale = 2; }
                    foreach (var m in nuevos)
                    {
                        p["@r"].Value = rfc; p["@c"].Value = cuentaId; p["@i"].Value = impId; p["@f"].Value = m.Fecha.Date; p["@o"].Value = m.Orden;
                        p["@de"].Value = Cortar(m.Descripcion, 1000); p["@dt"].Value = string.IsNullOrEmpty(m.Detalle) ? (object)DBNull.Value : m.Detalle; p["@re"].Value = Cortar(m.Referencia, 200);
                        p["@ca"].Value = m.Cargo; p["@ab"].Value = m.Abono; p["@sa"].Value = (object)m.Saldo ?? DBNull.Value;
                        p["@ti"].Value = Cortar(m.Tipo, 30); p["@cr"].Value = Cortar(m.ClaveRastreo, 40); p["@rf"].Value = Cortar(m.RfcContraparte, 13); p["@nc"].Value = Cortar(m.NombreContraparte, 120);
                        p["@cl"].Value = Cortar(m.ClabeContraparte, 18); p["@bc"].Value = Cortar(m.BancoContraparte, 60); p["@h"].Value = m.Hash;
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
                return new BancoImportado { ImportacionId = impId, Nuevos = nuevos.Count, Duplicados = movs.Count - nuevos.Count };
            }
        }

        private static object Cortar(string s, int max) => string.IsNullOrEmpty(s) ? (object)DBNull.Value : (s.Length > max ? s.Substring(0, max) : s);

        // ---------- Consultas ----------
        public static List<BancoMovFila> Movimientos(SqlConnection conn, string rfc, int? cuentaId, DateTime? desde, DateTime? hasta, string texto, string tipo, int tope = 5000)
        {
            var l = new List<BancoMovFila>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT TOP (@top) m.Id, c.Alias, m.Fecha, m.Orden, m.Descripcion, m.Detalle, m.Referencia, m.Cargo, m.Abono, m.Saldo, m.Tipo, m.ClaveRastreo, m.RfcContraparte, m.NombreContraparte, m.Estado
FROM BancoMovimiento m JOIN BancoCuenta c ON c.Id = m.CuentaId
WHERE m.RFC = @r AND (@c IS NULL OR m.CuentaId = @c) AND (@d IS NULL OR m.Fecha >= @d) AND (@h IS NULL OR m.Fecha <= @h) AND (@t IS NULL OR m.Tipo = @t)
  AND (@x IS NULL OR m.Descripcion LIKE @x OR m.Detalle LIKE @x OR m.Referencia LIKE @x OR m.NombreContraparte LIKE @x OR m.RfcContraparte LIKE @x OR m.ClaveRastreo LIKE @x
       OR CAST(m.Cargo AS NVARCHAR(30)) LIKE @x OR CAST(m.Abono AS NVARCHAR(30)) LIKE @x)
ORDER BY m.Fecha DESC, m.CuentaId, m.Orden DESC";
                cmd.Parameters.AddWithValue("@top", tope); cmd.Parameters.AddWithValue("@r", rfc);
                cmd.Parameters.AddWithValue("@c", (object)cuentaId ?? DBNull.Value); cmd.Parameters.AddWithValue("@d", (object)desde ?? DBNull.Value); cmd.Parameters.AddWithValue("@h", (object)hasta ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@t", string.IsNullOrEmpty(tipo) ? (object)DBNull.Value : tipo);
                cmd.Parameters.AddWithValue("@x", string.IsNullOrWhiteSpace(texto) ? (object)DBNull.Value : "%" + texto.Trim().Replace("[", "[[]").Replace("%", "[%]") + "%");
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        string S(int i) => rd.IsDBNull(i) ? "" : rd.GetString(i);
                        l.Add(new BancoMovFila
                        {
                            Id = rd.GetInt64(0), Cuenta = rd.GetString(1), Fecha = rd.GetDateTime(2), Orden = rd.GetInt32(3), Descripcion = S(4), Detalle = S(5), Referencia = S(6),
                            Cargo = rd.GetDecimal(7), Abono = rd.GetDecimal(8), Saldo = rd.IsDBNull(9) ? (decimal?)null : rd.GetDecimal(9), Tipo = S(10), ClaveRastreo = S(11), RfcContraparte = S(12),
                            NombreContraparte = S(13), Estado = S(14)
                        });
                    }
            }
            return l;
        }

        public static List<string> Tipos(SqlConnection conn, string rfc)
        {
            var l = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT Tipo FROM BancoMovimiento WHERE RFC=@r AND Tipo IS NOT NULL AND Tipo <> '' ORDER BY Tipo";
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var rd = cmd.ExecuteReader()) while (rd.Read()) l.Add(rd.GetString(0));
            }
            return l;
        }

        public static List<BancoImportacionFila> Importaciones(SqlConnection conn, string rfc)
        {
            var l = new List<BancoImportacionFila>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT i.Id, c.Alias, i.Archivo, i.Perfil, i.Notas, i.Fecha, i.Desde, i.Hasta, i.Leidos, i.Nuevos, i.Duplicados, i.SaldoInicial, i.SaldoFinal
FROM BancoImportacion i JOIN BancoCuenta c ON c.Id = i.CuentaId WHERE i.RFC=@r ORDER BY i.Id DESC";
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                        l.Add(new BancoImportacionFila
                        {
                            Id = rd.GetInt32(0), Cuenta = rd.GetString(1), Archivo = rd.GetString(2), Perfil = rd.IsDBNull(3) ? "" : rd.GetString(3), Notas = rd.IsDBNull(4) ? "" : rd.GetString(4),
                            Fecha = rd.GetDateTime(5).ToLocalTime(), Desde = rd.IsDBNull(6) ? (DateTime?)null : rd.GetDateTime(6), Hasta = rd.IsDBNull(7) ? (DateTime?)null : rd.GetDateTime(7),
                            Leidos = rd.GetInt32(8), Nuevos = rd.GetInt32(9), Duplicados = rd.GetInt32(10), SaldoInicial = rd.IsDBNull(11) ? (decimal?)null : rd.GetDecimal(11), SaldoFinal = rd.IsDBNull(12) ? (decimal?)null : rd.GetDecimal(12)
                        });
            }
            return l;
        }

        // Deshacer una carga: borra sus movimientos si ninguno esta ya conciliado (fase 2) y el registro de la carga.
        public static (bool ok, string error, int borrados) DeshacerImportacion(SqlConnection conn, string rfc, int importacionId)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT COUNT(*) FROM BancoMovimiento WHERE ImportacionId=@i AND RFC=@r AND Estado <> 'Pendiente'";
                    cmd.Parameters.AddWithValue("@i", importacionId); cmd.Parameters.AddWithValue("@r", rfc);
                    if (Convert.ToInt32(cmd.ExecuteScalar()) > 0) return (false, "Esta carga ya tiene movimientos conciliados: primero hay que deshacer su conciliación.", 0);
                }
                int n;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM BancoMovimiento WHERE ImportacionId=@i AND RFC=@r; SELECT @@ROWCOUNT; DELETE FROM BancoImportacion WHERE Id=@i AND RFC=@r;";
                    cmd.Parameters.AddWithValue("@i", importacionId); cmd.Parameters.AddWithValue("@r", rfc);
                    n = Convert.ToInt32(cmd.ExecuteScalar());
                }
                tx.Commit();
                return (true, null, n);
            }
        }
    }
}
