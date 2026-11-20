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

    // Otra herramienta que esta cargando renglones al estado de cuenta (no es BrosLMV Bancos).
    public class OtroCargador
    {
        public string Nombre = "";
        public int Renglones;
        public DateTime Desde, Hasta;
    }

    // Lo que Comercial ya tiene de una cuenta justo antes de la fecha de un archivo: para comprobar que el archivo continua donde se quedo la cuenta.
    public class SaldoPrevio
    {
        public DateTime Fecha;                       // ultimo dia con saldo anterior a la fecha del archivo
        public decimal Ultimo;                       // saldo con que termino ese dia
        public List<decimal> Saldos = new List<decimal>();    // saldos de ese dia y del dia del archivo: el saldo inicial del archivo debe ser uno de ellos
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
IF OBJECT_ID('zzBrosEdoCtaBaja') IS NULL
CREATE TABLE zzBrosEdoCtaBaja (
    ID BIGINT IDENTITY PRIMARY KEY, Lote UNIQUEIDENTIFIER NOT NULL, Fecha DATETIME2 NOT NULL DEFAULT SYSDATETIME(), UserID INT NOT NULL,
    EdoCtaID BIGINT NOT NULL, FinancialEntityID BIGINT NULL, DateAffectation DATETIME NULL, Debit FLOAT NULL, Credit FLOAT NULL, Balance FLOAT NULL,
    Description NVARCHAR(MAX) NULL, Reference NVARCHAR(255) NULL, CreatedOn DATETIME NULL, Motivo NVARCHAR(100) NULL, Restaurado BIT NOT NULL DEFAULT 0);
IF COL_LENGTH('zzBrosEdoCtaBaja','BajaOn') IS NULL ALTER TABLE zzBrosEdoCtaBaja ADD BajaOn DATETIME2 NULL;
IF COL_LENGTH('zzBrosEdoCtaBaja','DeletedByAntes') IS NULL ALTER TABLE zzBrosEdoCtaBaja ADD DeletedByAntes BIGINT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_zzBrosEdoCtaBaja_Lote' AND object_id=OBJECT_ID('zzBrosEdoCtaBaja'))
    CREATE INDEX IX_zzBrosEdoCtaBaja_Lote ON zzBrosEdoCtaBaja (Lote, Restaurado);
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
WHERE f.FinancialEntityTypeID IN (1,2) AND f.DeletedOn IS NULL
  -- solo las cuentas de la empresa: Comercial guarda en la misma tabla las cuentas de cada proveedor y cliente (cientos en una base real)
  AND EXISTS (SELECT 1 FROM orgBusinessEntity b WHERE b.BusinessEntityID = f.BusinessEntityID AND b.IsOwned = 1)
ORDER BY f.FinancialEntityName"))
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
            return CargarGrupos(new[] { new KeyValuePair<long, IEnumerable<BancoMov>>(cuenta, movs) }, archivo, archivoHash, layoutId);
        }

        // Un archivo puede traer varias cuentas (Monex: una por divisa): se cargan todas en la MISMA transaccion, o ninguna.
        public ResultadoCarga CargarGrupos(IEnumerable<KeyValuePair<long, IEnumerable<BancoMov>>> grupos, string archivo, string archivoHash, string layoutId)
        {
            var lote = Guid.NewGuid(); int n = 0;
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    foreach (var grupo in grupos)
                    {
                    long cuenta = grupo.Key;
                    foreach (var m in grupo.Value.OrderBy(x => x.Fecha).ThenBy(x => x.Orden))
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

        // ---------- duplicados que YA estaban en el estado de cuenta ----------
        // Renglones vigentes desde una fecha, de todas las cuentas (o de una). Los lee tal cual estan en Comercial, venga de donde venga la carga.
        public List<RenglonEdoCta> Renglones(DateTime desde, long? cuenta = null)
        {
            using (var cn = Abrir()) return LeerRenglones(cn, null, desde, cuenta);
        }

        private static List<RenglonEdoCta> LeerRenglones(SqlConnection cn, SqlTransaction tx, DateTime desde, long? cuenta)
        {
            var lista = new List<RenglonEdoCta>();
            using (var c = Cmd(cn, @"SELECT e.EdoCtaID, e.FinancialEntityID, ISNULL(f.FinancialEntityName,''), e.DateAffectation, ISNULL(e.Debit,0), ISNULL(e.Credit,0), e.Balance,
 ISNULL(CONVERT(nvarchar(300), e.Description),''), CASE WHEN ISNULL(e.FinancialOperationID,0)<>0 THEN 1 ELSE 0 END, e.CreatedOn
FROM docEdoCtaBanco e LEFT JOIN orgFinancialEntity f ON f.FinancialEntityID = e.FinancialEntityID
WHERE e.DeletedOn IS NULL AND e.DateAffectation >= @d AND (@c IS NULL OR e.FinancialEntityID = @c)", tx))
            {
                c.Parameters.AddWithValue("@d", desde.Date);
                c.Parameters.AddWithValue("@c", cuenta.HasValue ? (object)cuenta.Value : DBNull.Value);
                using (var r = c.ExecuteReader())
                    while (r.Read())
                        lista.Add(new RenglonEdoCta
                        {
                            EdoCtaId = r.GetInt64(0), CuentaId = r.GetInt64(1), Cuenta = r.GetString(2), Fecha = r.GetDateTime(3).Date, Cargo = Dinero(r.GetValue(4)), Abono = Dinero(r.GetValue(5)),
                            Saldo = r.IsDBNull(6) ? (decimal?)null : Dinero(r.GetValue(6)), Descripcion = r.GetString(7), Ligado = r.GetInt32(8) == 1, Cargado = r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9)
                        });
            }
            return lista;
        }

        public List<GrupoDuplicado> DuplicadosExistentes(DateTime desde, long? cuenta = null) { List<string> omitidas; return DuplicadosExistentes(desde, cuenta, out omitidas); }

        // cuentasOmitidas: cuentas cuyo saldo es el del dia (no por movimiento): no se pueden revisar sin arriesgar movimientos legitimos.
        public List<GrupoDuplicado> DuplicadosExistentes(DateTime desde, long? cuenta, out List<string> cuentasOmitidas) { return RevisionDuplicados.Buscar(Renglones(desde, cuenta), out cuentasOmitidas); }

        // Da de baja (como lo hace Comercial: DeletedOn/DeletedBy, nunca DELETE) los renglones indicados, SOLO si siguen vigentes, SIN pago ligado y
        // SIGUEN siendo copias sobrantes con el estado de cuenta de AHORA (la lista pudo verse hace rato: si alguien ya borro o ligo la copia que se
        // conservaba, no se da de baja nada de ese movimiento). Antes guarda una copia completa de cada renglon en zzBrosEdoCtaBaja para restaurarlos.
        public string DarDeBajaDuplicados(IEnumerable<long> ids)
        {
            var pedidos = ids.Distinct().ToList();
            if (pedidos.Count == 0) return "No hay renglones que dar de baja.";
            var lote = Guid.NewGuid(); int bajas = 0;
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    // 1) revalidar contra el estado de cuenta tal como esta ahora, con la misma regla de la deteccion
                    string todos = string.Join(",", pedidos.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));   // enteros: sin riesgo de inyeccion
                    DateTime minFecha;
                    using (var c = Cmd(cn, "SELECT MIN(DateAffectation) FROM docEdoCtaBanco WHERE EdoCtaID IN (" + todos + ")", tx))
                    { var o = c.ExecuteScalar(); if (o == null || o == DBNull.Value) { tx.Rollback(); return "Esos renglones ya no existen."; } minFecha = Convert.ToDateTime(o).Date; }
                    var vigentes = RevisionDuplicados.Buscar(LeerRenglones(cn, tx, minFecha.AddDays(-60), null));      // 60 dias antes: evidencia de si la cuenta trae saldo por movimiento
                    var validos = new HashSet<long>(vigentes.SelectMany(g => g.Sobrantes).Select(x => x.EdoCtaId));
                    var aBajar = pedidos.Where(validos.Contains).ToList();
                    if (aBajar.Count == 0) { tx.Rollback(); return "No se dio de baja nada: esos renglones ya cambiaron en el estado de cuenta (se revisó de nuevo y ya no son copias sobrantes)."; }

                    // 2) la hora exacta de la baja se guarda para restaurar solo lo que sigue como lo dejamos
                    DateTime ahora;
                    using (var c = Cmd(cn, "SELECT CONVERT(datetime2(6), GETDATE())", tx)) ahora = (DateTime)c.ExecuteScalar();
                    for (int i = 0; i < aBajar.Count; i += 500)
                    {
                        string lista = string.Join(",", aBajar.Skip(i).Take(500).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        using (var c = Cmd(cn, @"SET NOCOUNT ON; INSERT INTO zzBrosEdoCtaBaja (Lote, UserID, EdoCtaID, FinancialEntityID, DateAffectation, Debit, Credit, Balance, Description, Reference, CreatedOn, Motivo, BajaOn, DeletedByAntes)
SELECT @l, @u, e.EdoCtaID, e.FinancialEntityID, e.DateAffectation, e.Debit, e.Credit, e.Balance, CONVERT(nvarchar(max), e.Description), e.Reference, e.CreatedOn, N'renglon repetido', @t, e.DeletedBy
FROM docEdoCtaBanco e WHERE e.EdoCtaID IN (" + lista + ") AND e.DeletedOn IS NULL AND ISNULL(e.FinancialOperationID,0)=0", tx))
                        { c.Parameters.AddWithValue("@l", lote); c.Parameters.AddWithValue("@u", _usuario); c.Parameters.Add("@t", SqlDbType.DateTime2).Value = ahora; c.ExecuteNonQuery(); }
                        using (var c = Cmd(cn, @"SET NOCOUNT ON; UPDATE docEdoCtaBanco SET DeletedOn=@t, DeletedBy=@u
WHERE EdoCtaID IN (" + lista + @") AND DeletedOn IS NULL AND ISNULL(FinancialOperationID,0)=0; SELECT @@ROWCOUNT;", tx))
                        { c.Parameters.AddWithValue("@u", _usuario); c.Parameters.Add("@t", SqlDbType.DateTime2).Value = ahora; bajas += Convert.ToInt32(c.ExecuteScalar()); }
                    }
                    // DeletedOn puede tener menos precision que la hora de arriba (datetime2(2) en Comercial): se guarda lo que QUEDO escrito, para poder compararlo despues
                    using (var c = Cmd(cn, "SET NOCOUNT ON; UPDATE b SET BajaOn = e.DeletedOn FROM zzBrosEdoCtaBaja b JOIN docEdoCtaBanco e ON e.EdoCtaID = b.EdoCtaID WHERE b.Lote=@l AND e.DeletedBy=@u", tx))
                    { c.Parameters.AddWithValue("@l", lote); c.Parameters.AddWithValue("@u", _usuario); c.ExecuteNonQuery(); }
                    tx.Commit();
                }
                catch { tx.Rollback(); throw; }
            }
            int omitidos = pedidos.Count - bajas;
            return "Se dieron de baja " + bajas + " renglones repetidos." + (omitidos > 0 ? " " + omitidos + " no se tocaron (ya no estaban vigentes, ya tienen un movimiento ligado o dejaron de ser copias sobrantes)." : "");
        }

        // Devuelve a vigentes los renglones de la ultima baja de duplicados de este usuario.
        public string RestaurarUltimaBaja()
        {
            using (var cn = Abrir())
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    Guid lote;
                    using (var c = Cmd(cn, "SELECT TOP 1 Lote FROM zzBrosEdoCtaBaja WHERE UserID=@u AND Restaurado=0 ORDER BY ID DESC", tx))
                    {
                        c.Parameters.AddWithValue("@u", _usuario);
                        var o = c.ExecuteScalar();
                        if (o == null) { tx.Rollback(); return "No hay bajas tuyas que restaurar."; }
                        lote = (Guid)o;
                    }
                    // Solo se restaura lo que sigue exactamente como lo dejo la baja (misma hora y mismo usuario): si alguien lo borro despues
                    // a mano en Comercial, se respeta. Se devuelve el DeletedBy que tenia antes. (Bajas viejas sin BajaOn: basta que lo haya dado de baja este usuario.)
                    int n, total;
                    using (var c = Cmd(cn, "SELECT COUNT(*) FROM zzBrosEdoCtaBaja WHERE Lote=@l", tx)) { c.Parameters.AddWithValue("@l", lote); total = Convert.ToInt32(c.ExecuteScalar()); }
                    using (var c = Cmd(cn, @"SET NOCOUNT ON; UPDATE e SET DeletedOn=NULL, DeletedBy=b.DeletedByAntes FROM docEdoCtaBanco e JOIN zzBrosEdoCtaBaja b ON b.EdoCtaID=e.EdoCtaID
WHERE b.Lote=@l AND b.Restaurado=0 AND e.DeletedOn IS NOT NULL AND e.DeletedBy=b.UserID AND (b.BajaOn IS NULL OR e.DeletedOn=b.BajaOn); SELECT @@ROWCOUNT;", tx))
                    { c.Parameters.AddWithValue("@l", lote); n = Convert.ToInt32(c.ExecuteScalar()); }
                    using (var c = Cmd(cn, "SET NOCOUNT ON; UPDATE zzBrosEdoCtaBaja SET Restaurado=1 WHERE Lote=@l", tx)) { c.Parameters.AddWithValue("@l", lote); c.ExecuteNonQuery(); }
                    tx.Commit();
                    return "Se restauraron " + n + " renglones." + (total > n ? " " + (total - n) + " no se restauraron porque alguien los cambió después en Comercial." : "");
                }
                catch { tx.Rollback(); throw; }
            }
        }

        // ---------- continuidad del saldo ----------
        // Lo ultimo que Comercial tiene de la cuenta ANTES de una fecha (el ultimo dia con saldo estrictamente anterior), mas los saldos del propio dia
        // de la fecha por si el archivo empieza a media jornada. null = la cuenta no tiene nada antes: es la primera carga y no hay con que comparar.
        public SaldoPrevio SaldoAnterior(long cuenta, DateTime fecha)
        {
            using (var cn = Abrir())
            {
                DateTime dia;
                using (var c = Cmd(cn, "SELECT TOP 1 DateAffectation FROM docEdoCtaBanco WHERE FinancialEntityID=@c AND DeletedOn IS NULL AND DateAffectation < @f AND Balance IS NOT NULL AND Balance<>0 ORDER BY DateAffectation DESC"))
                {
                    c.Parameters.AddWithValue("@c", cuenta); c.Parameters.AddWithValue("@f", fecha.Date);
                    var o = c.ExecuteScalar(); if (o == null) return null;
                    dia = Convert.ToDateTime(o).Date;
                }
                var sp = new SaldoPrevio { Fecha = dia };
                long ultimoId = -1;
                using (var c = Cmd(cn, @"SELECT e.EdoCtaID, e.Balance, e.DateAffectation FROM docEdoCtaBanco e
WHERE e.FinancialEntityID=@c AND e.DeletedOn IS NULL AND e.Balance IS NOT NULL AND e.Balance<>0
  AND ((e.DateAffectation >= @d AND e.DateAffectation < @d1) OR (e.DateAffectation >= @f AND e.DateAffectation < @f1))"))
                {
                    c.Parameters.AddWithValue("@c", cuenta); c.Parameters.AddWithValue("@d", dia); c.Parameters.AddWithValue("@d1", dia.AddDays(1));
                    c.Parameters.AddWithValue("@f", fecha.Date); c.Parameters.AddWithValue("@f1", fecha.Date.AddDays(1));
                    using (var r = c.ExecuteReader())
                        while (r.Read())
                        {
                            decimal s = Dinero(r.GetValue(1)); long id = r.GetInt64(0);
                            if (!sp.Saldos.Contains(s)) sp.Saldos.Add(s);
                            if (r.GetDateTime(2).Date == dia && id > ultimoId) { ultimoId = id; sp.Ultimo = s; }
                        }
                }
                return sp.Saldos.Count == 0 ? null : sp;
            }
        }

        // ---------- otro cargador activo ----------
        // Renglones recientes que NO cargo BrosLMV Bancos pero que traen la marca de otra herramienta de carga (TransactionSyncFyID: la usa SmartConnect).
        // Si la base no tiene esa columna, no hay nada que decir.
        public List<OtroCargador> OtrosCargadores(int dias)
        {
            var lista = new List<OtroCargador>();
            using (var cn = Abrir())
            {
                using (var c = Cmd(cn, "SELECT COL_LENGTH('docEdoCtaBanco','TransactionSyncFyID')"))
                { var o = c.ExecuteScalar(); if (o == null || o == DBNull.Value) return lista; }
                using (var c = Cmd(cn, @"SELECT CONVERT(nvarchar(100), e.TransactionSyncFyID), COUNT(*), MIN(e.CreatedOn), MAX(e.CreatedOn)
FROM docEdoCtaBanco e
WHERE e.CreatedOn >= @d AND e.DeletedOn IS NULL AND ISNULL(CONVERT(nvarchar(100), e.TransactionSyncFyID),'') <> ''
  AND NOT EXISTS (SELECT 1 FROM zzBrosEdoCtaCarga l WHERE l.EdoCtaID = e.EdoCtaID)
GROUP BY CONVERT(nvarchar(100), e.TransactionSyncFyID) ORDER BY MAX(e.CreatedOn) DESC"))
                {
                    c.Parameters.AddWithValue("@d", DateTime.Today.AddDays(-Math.Max(1, dias)));
                    using (var r = c.ExecuteReader())
                        while (r.Read())
                            lista.Add(new OtroCargador { Nombre = r.GetString(0), Renglones = r.GetInt32(1), Desde = r.GetDateTime(2), Hasta = r.GetDateTime(3) });
                }
            }
            return lista;
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

        // Quita el ajuste de la empresa: vuelve a usarse el layout de fabrica (el historial se conserva).
        public void QuitarLayout(string id)
        {
            using (var cn = Abrir())
            using (var c = Cmd(cn, "UPDATE zzBrosBancoLayout SET Activo=0 WHERE Id=@i"))
            { c.Parameters.AddWithValue("@i", id); c.ExecuteNonQuery(); }
        }

        // Volver a una version anterior = guardarla como version nueva (el historial nunca se pierde).
        public int RestaurarLayout(string id, int version)
        {
            var l = HistorialDeLayout(id).FirstOrDefault(x => x.Version == version);
            if (l == null) throw new InvalidOperationException("No existe la version " + version + " del layout " + id + ".");
            return GuardarLayout(l);
        }

        // ---------- usuarios de Comercial ----------
        public class Usuario { public int Id; public string Nombre = ""; public bool Administrador; public override string ToString() { return Nombre; } }

        public List<Usuario> Usuarios()
        {
            var lista = new List<Usuario>();
            using (var cn = Abrir())
            using (var c = Cmd(cn, "SELECT UserID, ISNULL(UserName,''), ISNULL(UserGroupID,0) FROM engUser WHERE DeletedOn IS NULL ORDER BY UserName"))
            using (var r = c.ExecuteReader())
                while (r.Read()) lista.Add(new Usuario { Id = Convert.ToInt32(r.GetValue(0)), Nombre = r.GetString(1), Administrador = Convert.ToInt32(r.GetValue(2)) == 1 });
            return lista;
        }

        public bool EsAdministrador(int usuario)
        {
            return Usuarios().Any(u => u.Id == usuario && u.Administrador);
        }

        // Quien puede crear o cambiar layouts: todos (predeterminado), solo administradores o usuarios elegidos.
        public bool PuedeEditarLayouts(int usuario)
        {
            string modo = Config("layouts.permiso", "todos");
            if (modo == "admin") return EsAdministrador(usuario);
            if (modo == "usuarios")
                return EsAdministrador(usuario) || Config("layouts.usuarios", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x.Trim() == usuario.ToString());
            return true;
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
