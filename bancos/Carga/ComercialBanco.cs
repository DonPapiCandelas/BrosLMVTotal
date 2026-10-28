// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Bancos.Carga
{
    public class ResultadoCarga
    {
        public Guid Lote;
        public int Cargados;
    }

    // Todo lo que el programa le hace a la base de la empresa. Los movimientos van al estado de cuenta NATIVO de Comercial
    // (docEdoCtaBanco: lo que se ve en Estados de cuenta); lo propio de BrosLMV (hora, rastreo, de que archivo vino, layouts,
    // configuracion) va en tablas zzBros* que Comercial ignora.
    public class ComercialBanco
    {
        private readonly string _cadena;
        private readonly int _empresa, _usuario;

        public ComercialBanco(string cadena, int empresa, int usuario) { _cadena = cadena; _empresa = empresa; _usuario = usuario; }

        private SqlConnection Abrir() { var cn = new SqlConnection(_cadena); cn.Open(); return cn; }

        private static SqlCommand Cmd(SqlConnection cn, string sql, SqlTransaction tx = null)
        {
            var c = cn.CreateCommand(); c.CommandText = sql; c.CommandTimeout = 60; if (tx != null) c.Transaction = tx; return c;
        }

        // ---------- tablas propias ----------
        public void Asegurar()
        {
            using (var cn = Abrir())
            using (var c = Cmd(cn, @"SET NOCOUNT ON;
IF OBJECT_ID('zzBrosEdoCtaCarga') IS NULL
CREATE TABLE zzBrosEdoCtaCarga (
    ID BIGINT IDENTITY PRIMARY KEY, Lote UNIQUEIDENTIFIER NOT NULL, Fecha DATETIME2 NOT NULL DEFAULT SYSDATETIME(), UserID INT NOT NULL,
    FinancialEntityID BIGINT NOT NULL, EdoCtaID BIGINT NOT NULL, Archivo NVARCHAR(260) NULL, Tipo NVARCHAR(60) NULL, ClaveRastreo NVARCHAR(100) NULL,
    RfcContraparte NVARCHAR(20) NULL, NombreContraparte NVARCHAR(200) NULL, Deshecho BIT NOT NULL DEFAULT 0);
IF COL_LENGTH('zzBrosEdoCtaCarga','Hora') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD Hora TIME NULL;
IF COL_LENGTH('zzBrosEdoCtaCarga','SaldoDespues') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD SaldoDespues DECIMAL(19,2) NULL;
IF COL_LENGTH('zzBrosEdoCtaCarga','LayoutId') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD LayoutId NVARCHAR(100) NULL;
IF COL_LENGTH('zzBrosEdoCtaCarga','ArchivoHash') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD ArchivoHash NVARCHAR(64) NULL;
IF COL_LENGTH('zzBrosEdoCtaCarga','BancoContraparte') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD BancoContraparte NVARCHAR(100) NULL;
IF COL_LENGTH('zzBrosEdoCtaCarga','ClabeContraparte') IS NULL ALTER TABLE zzBrosEdoCtaCarga ADD ClabeContraparte NVARCHAR(30) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_zzBrosEdoCtaCarga_Lote' AND object_id=OBJECT_ID('zzBrosEdoCtaCarga'))
    CREATE INDEX IX_zzBrosEdoCtaCarga_Lote ON zzBrosEdoCtaCarga (Lote, Deshecho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_zzBrosEdoCtaCarga_EdoCta' AND object_id=OBJECT_ID('zzBrosEdoCtaCarga'))
    CREATE INDEX IX_zzBrosEdoCtaCarga_EdoCta ON zzBrosEdoCtaCarga (EdoCtaID);
IF OBJECT_ID('zzBrosBancoLayout') IS NULL
CREATE TABLE zzBrosBancoLayout (Id NVARCHAR(100) NOT NULL, Version INT NOT NULL, Json NVARCHAR(MAX) NOT NULL, Fecha DATETIME2 NOT NULL DEFAULT SYSDATETIME(), UserID INT NOT NULL DEFAULT 0, Activo BIT NOT NULL DEFAULT 1, PRIMARY KEY (Id, Version));
IF OBJECT_ID('zzBrosBancoConfig') IS NULL
CREATE TABLE zzBrosBancoConfig (Clave NVARCHAR(100) NOT NULL PRIMARY KEY, Valor NVARCHAR(MAX) NULL);"))
                c.ExecuteNonQuery();
        }

        // ---------- cuentas ----------
        public List<CuentaBanco> Cuentas()
        {
            var lista = new List<CuentaBanco>();
            using (var cn = Abrir())
            using (var c = Cmd(cn, @"SELECT f.FinancialEntityID, ISNULL(f.FinancialEntityName,''), ISNULL(NULLIF(f.FinancialEntityNumber,''), ISNULL(f.CreditCardNumber,'')), ISNULL(f.CLABE,''), ISNULL(f.BankName,''),
 ISNULL(cu.IntlSymbol,''), f.FinancialEntityTypeID,
 (SELECT TOP 1 e.Balance FROM docEdoCtaBanco e WHERE e.FinancialEntityID=f.FinancialEntityID AND e.DeletedOn IS NULL ORDER BY e.DateAffectation DESC, e.EdoCtaID DESC),
 (SELECT TOP 1 e.DateAffectation FROM docEdoCtaBanco e WHERE e.FinancialEntityID=f.FinancialEntityID AND e.DeletedOn IS NULL ORDER BY e.DateAffectation DESC, e.EdoCtaID DESC)
FROM orgFinancialEntity f LEFT JOIN engRefCurrency cu ON cu.CurrencyID=f.CurrencyID
WHERE f.FinancialEntityTypeID IN (1,2) AND f.DeletedOn IS NULL ORDER BY f.FinancialEntityName"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                    lista.Add(new CuentaBanco
                    {
                        Id = r.GetInt64(0), Nombre = r.GetString(1), Numero = r.GetString(2), Clabe = r.GetString(3), Banco = r.GetString(4), Moneda = r.GetString(5), Tipo = Convert.ToInt32(r.GetValue(6)),
                        UltimoSaldo = r.IsDBNull(7) ? (decimal?)null : Math.Round((decimal)Convert.ToDouble(r.GetValue(7)), 2), UltimaFecha = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8)
                    });
            return lista;
        }

        // ---------- lo que ya esta cargado ----------
        public List<Existente> Existentes(long cuenta, DateTime desde, DateTime hasta)
        {
            var lista = new List<Existente>();
            using (var cn = Abrir())
            using (var c = Cmd(cn, @"SELECT e.EdoCtaID, e.DateAffectation, ISNULL(e.Debit,0), ISNULL(e.Credit,0), e.Balance, c.Hora, c.ClaveRastreo
FROM docEdoCtaBanco e LEFT JOIN zzBrosEdoCtaCarga c ON c.EdoCtaID = e.EdoCtaID AND c.Deshecho = 0
WHERE e.FinancialEntityID=@c AND e.DeletedOn IS NULL AND e.DateAffectation >= @a AND e.DateAffectation < @b"))
            {
                c.Parameters.AddWithValue("@c", cuenta); c.Parameters.AddWithValue("@a", desde.Date); c.Parameters.AddWithValue("@b", hasta.Date.AddDays(1));
                using (var r = c.ExecuteReader())
                    while (r.Read())
                        lista.Add(new Existente
                        {
                            EdoCtaId = r.GetInt64(0), Fecha = r.GetDateTime(1).Date, Cargo = Dinero(r.GetValue(2)), Abono = Dinero(r.GetValue(3)),
                            Saldo = r.IsDBNull(4) ? (decimal?)null : Dinero(r.GetValue(4)), Hora = r.IsDBNull(5) ? (TimeSpan?)null : (TimeSpan)r.GetValue(5), Rastreo = r.IsDBNull(6) ? "" : r.GetString(6)
                        });
            }
            return lista;
        }

        private static decimal Dinero(object o) { return Math.Round((decimal)Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture), 2); }

        public bool ArchivoYaCargado(string archivoHash)
        {
            using (var cn = Abrir())
            using (var c = Cmd(cn, "SELECT TOP 1 1 FROM zzBrosEdoCtaCarga WHERE ArchivoHash=@h AND Deshecho=0"))
            { c.Parameters.AddWithValue("@h", archivoHash); return c.ExecuteScalar() != null; }
        }

        // ---------- carga ----------
        // Todo o nada. Los movimientos que ya existian NO se mandan aqui: los filtra quien llama con Duplicados.Marcar.
        public ResultadoCarga Cargar(long cuenta, IEnumerable<BancoMov> movs, string archivo, string archivoHash, string layoutId)
        {
            var lote = Guid.NewGuid(); int n = 0;
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    foreach (var m in movs.OrderBy(x => x.Fecha).ThenBy(x => x.Orden))
                    {
                        long id;
                        using (var c = Cmd(cn, @"SET NOCOUNT ON; INSERT INTO docEdoCtaBanco (OwnedBusinessEntityID, DateAffectation, FinancialEntityID, Description, Debit, Credit, Balance, Reference, CreatedOn, CreatedBy, UserID, FinancialOperationID)
OUTPUT inserted.EdoCtaID VALUES (@emp, @f, @c, @d, @deb, @cre, @bal, @ref, GETDATE(), @u, @u, 0)", tx))
                        {
                            string desc = (m.Descripcion + (m.Detalle.Length > 0 && !m.Descripcion.Contains(m.Detalle) ? " " + m.Detalle : "")).Trim();
                            c.Parameters.AddWithValue("@emp", _empresa); c.Parameters.AddWithValue("@f", m.Fecha.Date); c.Parameters.AddWithValue("@c", cuenta);
                            c.Parameters.AddWithValue("@d", Corto(desc, 255)); c.Parameters.AddWithValue("@deb", (double)m.Cargo); c.Parameters.AddWithValue("@cre", (double)m.Abono);
                            c.Parameters.AddWithValue("@bal", m.Saldo.HasValue ? (double)m.Saldo.Value : 0.0);
                            c.Parameters.AddWithValue("@ref", Corto(m.Referencia.Length > 0 ? m.Referencia : m.ClaveRastreo, 255)); c.Parameters.AddWithValue("@u", _usuario);
                            id = Convert.ToInt64(c.ExecuteScalar());
                        }
                        using (var c = Cmd(cn, @"SET NOCOUNT ON; INSERT INTO zzBrosEdoCtaCarga (Lote, UserID, FinancialEntityID, EdoCtaID, Archivo, Tipo, ClaveRastreo, RfcContraparte, NombreContraparte, Hora, SaldoDespues, LayoutId, ArchivoHash, BancoContraparte, ClabeContraparte)
VALUES (@l,@u,@c,@e,@a,@t,@k,@r,@n,@h,@s,@ly,@ah,@bc,@cl)", tx))
                        {
                            c.Parameters.AddWithValue("@l", lote); c.Parameters.AddWithValue("@u", _usuario); c.Parameters.AddWithValue("@c", cuenta); c.Parameters.AddWithValue("@e", id);
                            c.Parameters.AddWithValue("@a", Corto(archivo, 260)); c.Parameters.AddWithValue("@t", Corto(m.Tipo, 60)); c.Parameters.AddWithValue("@k", Corto(m.ClaveRastreo, 100));
                            c.Parameters.AddWithValue("@r", Corto(m.RfcContraparte, 20)); c.Parameters.AddWithValue("@n", Corto(m.NombreContraparte, 200));
                            c.Parameters.Add("@h", SqlDbType.Time).Value = m.Hora.HasValue ? (object)m.Hora.Value : DBNull.Value;
                            c.Parameters.Add("@s", SqlDbType.Decimal).Value = m.Saldo.HasValue ? (object)m.Saldo.Value : DBNull.Value;
                            c.Parameters.AddWithValue("@ly", (object)layoutId ?? DBNull.Value); c.Parameters.AddWithValue("@ah", (object)archivoHash ?? DBNull.Value);
                            c.Parameters.AddWithValue("@bc", Corto(m.BancoContraparte, 100)); c.Parameters.AddWithValue("@cl", Corto(m.ClabeContraparte, 30));
                            c.ExecuteNonQuery();
                        }
                        n++;
                    }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
            return new ResultadoCarga { Lote = lote, Cargados = n };
        }

        private static string Corto(string s, int max) { s = s ?? ""; return s.Length <= max ? s : s.Substring(0, max); }

        // Da de baja (como lo hace Comercial: DeletedOn) los renglones del ultimo lote del usuario que siguen sin movimiento ligado.
        public string DeshacerUltima()
        {
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    Guid lote; string archivo;
                    using (var c = Cmd(cn, "SELECT TOP 1 Lote, ISNULL(Archivo,'') FROM zzBrosEdoCtaCarga WHERE UserID=@u AND Deshecho=0 ORDER BY ID DESC", tx))
                    {
                        c.Parameters.AddWithValue("@u", _usuario);
                        using (var r = c.ExecuteReader()) { if (!r.Read()) { r.Close(); tx.Rollback(); return "No hay cargas tuyas que deshacer."; } lote = r.GetGuid(0); archivo = r.GetString(1); }
                    }
                    int bajas, ligados;
                    using (var c = Cmd(cn, @"SET NOCOUNT ON; UPDATE e SET DeletedOn=GETDATE(), DeletedBy=@u FROM docEdoCtaBanco e JOIN zzBrosEdoCtaCarga l ON l.EdoCtaID=e.EdoCtaID
WHERE l.Lote=@l AND l.Deshecho=0 AND e.DeletedOn IS NULL AND ISNULL(e.FinancialOperationID,0)=0; SELECT @@ROWCOUNT;", tx))
                    { c.Parameters.AddWithValue("@u", _usuario); c.Parameters.AddWithValue("@l", lote); bajas = Convert.ToInt32(c.ExecuteScalar()); }
                    using (var c = Cmd(cn, "SET NOCOUNT ON; SELECT COUNT(*) FROM docEdoCtaBanco e JOIN zzBrosEdoCtaCarga l ON l.EdoCtaID=e.EdoCtaID WHERE l.Lote=@l AND e.DeletedOn IS NULL", tx))
                    { c.Parameters.AddWithValue("@l", lote); ligados = Convert.ToInt32(c.ExecuteScalar()); }
                    using (var c = Cmd(cn, "SET NOCOUNT ON; UPDATE zzBrosEdoCtaCarga SET Deshecho=1 WHERE Lote=@l AND EdoCtaID IN (SELECT EdoCtaID FROM docEdoCtaBanco WHERE DeletedOn IS NOT NULL)", tx))
                    { c.Parameters.AddWithValue("@l", lote); c.ExecuteNonQuery(); }
                    tx.Commit();
                    return "Se dieron de baja " + bajas + " renglones de «" + archivo + "»." + (ligados > 0 ? " " + ligados + " ya están ligados a un movimiento bancario y se dejaron." : "");
                }
                catch { tx.Rollback(); throw; }
            }
        }

        // ---------- layouts de la empresa (versionados) ----------
        public List<Layout> LayoutsEmpresa()
        {
            var lista = new List<Layout>();
            using (var cn = Abrir())
            using (var c = Cmd(cn, "SELECT Json FROM zzBrosBancoLayout WHERE Activo=1 ORDER BY Id"))
            using (var r = c.ExecuteReader())
                while (r.Read()) { try { lista.Add(Layout.DeDatos(r.GetString(0))); } catch { } }
            return lista;
        }

        // Guarda una version nueva (la anterior queda en el historial) y devuelve el numero de version.
        public int GuardarLayout(Layout l)
        {
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    int v;
                    using (var c = Cmd(cn, "SELECT ISNULL(MAX(Version),0)+1 FROM zzBrosBancoLayout WHERE Id=@i", tx)) { c.Parameters.AddWithValue("@i", l.Id); v = Convert.ToInt32(c.ExecuteScalar()); }
                    l.Version = v; l.Origen = "empresa";
                    using (var c = Cmd(cn, "UPDATE zzBrosBancoLayout SET Activo=0 WHERE Id=@i", tx)) { c.Parameters.AddWithValue("@i", l.Id); c.ExecuteNonQuery(); }
                    using (var c = Cmd(cn, "INSERT INTO zzBrosBancoLayout (Id, Version, Json, UserID, Activo) VALUES (@i,@v,@j,@u,1)", tx))
                    { c.Parameters.AddWithValue("@i", l.Id); c.Parameters.AddWithValue("@v", v); c.Parameters.AddWithValue("@j", l.ADatos()); c.Parameters.AddWithValue("@u", _usuario); c.ExecuteNonQuery(); }
                    tx.Commit();
                    return v;
                }
                catch { tx.Rollback(); throw; }
            }
        }

        public List<Layout> HistorialDeLayout(string id)
        {
            var lista = new List<Layout>();
            using (var cn = Abrir())
            using (var c = Cmd(cn, "SELECT Json FROM zzBrosBancoLayout WHERE Id=@i ORDER BY Version DESC"))
            {
                c.Parameters.AddWithValue("@i", id);
                using (var r = c.ExecuteReader()) while (r.Read()) { try { lista.Add(Layout.DeDatos(r.GetString(0))); } catch { } }
            }
            return lista;
        }

        // Volver a una version anterior = guardarla como version nueva (el historial nunca se pierde).
        public int RestaurarLayout(string id, int version)
        {
            var l = HistorialDeLayout(id).FirstOrDefault(x => x.Version == version);
            if (l == null) throw new InvalidOperationException("No existe la version " + version + " del layout " + id + ".");
            return GuardarLayout(l);
        }

        // ---------- configuracion de la empresa ----------
        public string Config(string clave, string predeterminado = "")
        {
            using (var cn = Abrir())
            using (var c = Cmd(cn, "SELECT Valor FROM zzBrosBancoConfig WHERE Clave=@k"))
            { c.Parameters.AddWithValue("@k", clave); var o = c.ExecuteScalar(); return o == null || o is DBNull ? predeterminado : Convert.ToString(o); }
        }

        public void PonerConfig(string clave, string valor)
        {
            using (var cn = Abrir())
            using (var c = Cmd(cn, "MERGE zzBrosBancoConfig AS t USING (SELECT @k AS Clave) s ON t.Clave=s.Clave WHEN MATCHED THEN UPDATE SET Valor=@v WHEN NOT MATCHED THEN INSERT (Clave,Valor) VALUES (@k,@v);"))
            { c.Parameters.AddWithValue("@k", clave); c.Parameters.AddWithValue("@v", (object)valor ?? DBNull.Value); c.ExecuteNonQuery(); }
        }
    }
}
