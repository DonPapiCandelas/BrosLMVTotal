using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Bancos.Carga;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Contra el laboratorio BROSLMV_DESARROLLO (nunca contra una empresa de cliente). Si no hay SQL Server o no existe la base, se omiten.
    // Cada prueba limpia lo que escribio.
    public class ComercialBancoTests
    {
        private static string Cadena()
        {
            string cred = Conexion.CredencialDeLaTerminal();
            if (cred.Length == 0) return null;
            string servidor = "localhost\\COMPAC";
            foreach (var p in cred.Split(';')) { int i = p.IndexOf('='); if (i > 0 && (p.Substring(0, i).Trim().Equals("Server", StringComparison.OrdinalIgnoreCase) || p.Substring(0, i).Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))) servidor = p.Substring(i + 1).Trim(); }
            return Conexion.Para(servidor, "BROSLMV_DESARROLLO");
        }

        private static ComercialBanco Laboratorio(out string cadena)
        {
            cadena = Cadena();
            if (cadena == null) return null;
            try
            {
                using (var cn = new SqlConnection(cadena)) { cn.Open(); using (var c = cn.CreateCommand()) { c.CommandText = "SELECT COUNT(*) FROM orgFinancialEntity"; c.ExecuteScalar(); } }
            }
            catch { return null; }
            var cb = new ComercialBanco(cadena, 1, 1);
            cb.Asegurar();
            return cb;
        }

        private static void Limpiar(string cadena, Guid lote)
        {
            using (var cn = new SqlConnection(cadena))
            {
                cn.Open();
                using (var c = cn.CreateCommand())
                {
                    c.CommandText = "DELETE e FROM docEdoCtaBanco e JOIN zzBrosEdoCtaCarga l ON l.EdoCtaID=e.EdoCtaID WHERE l.Lote=@l; DELETE FROM zzBrosEdoCtaCarga WHERE Lote=@l";
                    c.Parameters.AddWithValue("@l", lote); c.ExecuteNonQuery();
                }
            }
        }

        private static BancoMov Mov(int orden, DateTime f, decimal cargo, decimal abono, decimal? saldo, string hora = null, string desc = "PRUEBA")
        {
            return new BancoMov { Orden = orden, Fecha = f, Cargo = cargo, Abono = abono, Saldo = saldo, Hora = hora == null ? (TimeSpan?)null : TimeSpan.Parse(hora), Descripcion = desc, Tipo = "SPEI recibido", ClaveRastreo = "RAS" + orden };
        }

        [Fact]
        public void CargarEscribeEnElEstadoDeCuentaNativoYLoCargadoSeReconoceDespues()
        {
            string cad; var cb = Laboratorio(out cad); if (cb == null) return;
            var cuenta = cb.Cuentas().FirstOrDefault(c => c.Tipo == 1); if (cuenta == null) return;
            var f = new DateTime(2031, 1, 15);                                    // una fecha que ninguna empresa real tiene
            var movs = new List<BancoMov> { Mov(1, f, 0, 1000m, 5000m, "10:00"), Mov(2, f, 0, 1000m, 6000m, "11:00"), Mov(3, f, 250m, 0, 5750m) };
            Guid lote = Guid.Empty;
            try
            {
                var res = cb.Cargar(cuenta.Id, movs, "prueba.csv", "HASH-PRUEBA", "bbva-txt"); lote = res.Lote;
                Assert.Equal(3, res.Cargados);

                var existentes = cb.Existentes(cuenta.Id, f, f);
                Assert.Equal(3, existentes.Count);
                Assert.Contains(existentes, e => e.Hora == new TimeSpan(10, 0, 0) && e.Abono == 1000m && e.Saldo == 5000m && e.Rastreo == "RAS1");

                // el mismo archivo otra vez: todo es repetido
                Assert.Equal(3, Duplicados.Marcar(movs, existentes).Count);
                // dos depositos iguales de otro dia con otros saldos: nuevos
                var otros = new List<BancoMov> { Mov(1, f, 0, 1000m, 7000m, "12:00") };
                Assert.Empty(Duplicados.Marcar(otros, existentes));

                Assert.True(cb.ArchivoYaCargado("HASH-PRUEBA"));
            }
            finally { if (lote != Guid.Empty) Limpiar(cad, lote); }
            Assert.False(cb.ArchivoYaCargado("HASH-PRUEBA"));
        }

        [Fact]
        public void DeshacerDaDeBajaLoCargadoYNoSeVuelveAVer()
        {
            string cad; var cb = Laboratorio(out cad); if (cb == null) return;
            var cuenta = cb.Cuentas().FirstOrDefault(c => c.Tipo == 1); if (cuenta == null) return;
            var f = new DateTime(2031, 2, 20);
            Guid lote = Guid.Empty;
            try
            {
                lote = cb.Cargar(cuenta.Id, new[] { Mov(1, f, 0, 10m, 10m) }, "deshacer.csv", "HASH-DESHACER", "bbva-txt").Lote;
                Assert.Single(cb.Existentes(cuenta.Id, f, f));
                string msg = cb.DeshacerUltima();
                Assert.Contains("1 renglones", msg);
                Assert.Empty(cb.Existentes(cuenta.Id, f, f));
            }
            finally { if (lote != Guid.Empty) Limpiar(cad, lote); }
        }

        [Fact]
        public void LosLayoutsDeLaEmpresaSeVersionanYSePuedeVolverAUnaAnterior()
        {
            string cad; var cb = Laboratorio(out cad); if (cb == null) return;
            string id = "prueba-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var l = new Layout { Id = id, Banco = "PRUEBA", Nombre = "Layout de prueba", Reconocer = new List<List<Condicion>> { new List<Condicion> { new Condicion { En = "archivo", Contiene = new List<string> { "zzprueba" } } } } };
            try
            {
                Assert.Equal(1, cb.GuardarLayout(l));
                l.Nombre = "Layout de prueba v2";
                Assert.Equal(2, cb.GuardarLayout(l));
                Assert.Equal("Layout de prueba v2", cb.LayoutsEmpresa().First(x => x.Id == id).Nombre);
                Assert.Equal(2, cb.HistorialDeLayout(id).Count);
                Assert.Equal(3, cb.RestaurarLayout(id, 1));
                Assert.Equal("Layout de prueba", cb.LayoutsEmpresa().First(x => x.Id == id).Nombre);
            }
            finally
            {
                using (var cn = new SqlConnection(cad)) { cn.Open(); using (var c = cn.CreateCommand()) { c.CommandText = "DELETE FROM zzBrosBancoLayout WHERE Id=@i"; c.Parameters.AddWithValue("@i", id); c.ExecuteNonQuery(); } }
            }
        }

        [Fact]
        public void LosPermisosDeLayoutsRespetanElModoElegido()
        {
            string cad; var cb = Laboratorio(out cad); if (cb == null) return;
            var usuarios = cb.Usuarios();
            var admin = usuarios.FirstOrDefault(u => u.Administrador); var otro = usuarios.FirstOrDefault(u => !u.Administrador);
            if (admin == null || otro == null) return;
            string antes = cb.Config("layouts.permiso", ""), antesU = cb.Config("layouts.usuarios", "");
            try
            {
                cb.PonerConfig("layouts.permiso", "todos");
                Assert.True(cb.PuedeEditarLayouts(otro.Id));
                cb.PonerConfig("layouts.permiso", "admin");
                Assert.True(cb.PuedeEditarLayouts(admin.Id));
                Assert.False(cb.PuedeEditarLayouts(otro.Id));
                cb.PonerConfig("layouts.permiso", "usuarios"); cb.PonerConfig("layouts.usuarios", otro.Id.ToString());
                Assert.True(cb.PuedeEditarLayouts(otro.Id));
                cb.PonerConfig("layouts.usuarios", "");
                Assert.False(cb.PuedeEditarLayouts(otro.Id));
            }
            finally
            {
                using (var cn = new SqlConnection(cad)) { cn.Open(); using (var c = cn.CreateCommand()) { c.CommandText = "DELETE FROM zzBrosBancoConfig WHERE Clave IN ('layouts.permiso','layouts.usuarios')"; c.ExecuteNonQuery(); } }
            }
        }

        [Fact]
        public void LaConfiguracionDeLaEmpresaSeGuardaYSeLee()
        {
            string cad; var cb = Laboratorio(out cad); if (cb == null) return;
            string k = "prueba.k" + Guid.NewGuid().ToString("N").Substring(0, 6);
            try
            {
                Assert.Equal("x", cb.Config(k, "x"));
                cb.PonerConfig(k, "valor"); cb.PonerConfig(k, "otro");
                Assert.Equal("otro", cb.Config(k));
            }
            finally { using (var cn = new SqlConnection(cad)) { cn.Open(); using (var c = cn.CreateCommand()) { c.CommandText = "DELETE FROM zzBrosBancoConfig WHERE Clave=@k"; c.Parameters.AddWithValue("@k", k); c.ExecuteNonQuery(); } } }
        }
    }
}
