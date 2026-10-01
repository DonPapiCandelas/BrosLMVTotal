using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Base de datos desechable (BrosLMV_SAT_Pruebas_<guid>) en localhost\compac: se crea al empezar la clase de
    // pruebas y se borra al terminar. Si no hay SQL Server alcanzable, las pruebas se omiten (pasan sin correr).
    public class BaseDesechable : IDisposable
    {
        public const string Rfc = "BPL170810CL4";
        private static readonly string Servidor = Environment.GetEnvironmentVariable("BROSLMV_PRUEBAS_SQL") ?? @"localhost\compac";
        public readonly string Nombre = "BrosLMV_SAT_Pruebas_" + Guid.NewGuid().ToString("N");
        public readonly bool Disponible;
        public SqlConnection Conn;

        private string Cadena(string bd) => "Server=" + Servidor + ";Database=" + bd + ";Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;";

        public BaseDesechable()
        {
            try
            {
                using (var master = new SqlConnection(Cadena("master")))
                {
                    master.Open();
                    using (var cmd = new SqlCommand("CREATE DATABASE [" + Nombre + "]", master)) cmd.ExecuteNonQuery();
                }
                Conn = new SqlConnection(Cadena(Nombre));
                Conn.Open();
                EsquemaSql.Asegurar(Conn);
                Disponible = true;
            }
            catch { Disponible = false; }
        }

        public void Ejecutar(string sql)
        {
            using (var cmd = new SqlCommand(sql, Conn)) cmd.ExecuteNonQuery();
        }

        public T Escalar<T>(string sql)
        {
            using (var cmd = new SqlCommand(sql, Conn)) return (T)Convert.ChangeType(cmd.ExecuteScalar(), typeof(T));
        }

        public void Dispose()
        {
            if (!Disponible) return;
            try
            {
                Conn.Dispose();
                SqlConnection.ClearAllPools();
                using (var master = new SqlConnection(Cadena("master")))
                {
                    master.Open();
                    using (var cmd = new SqlCommand("ALTER DATABASE [" + Nombre + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + Nombre + "]", master)) cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }
    }

    public class DisponibilidadTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public DisponibilidadTests(BaseDesechable bd) { _bd = bd; }

        // Sin esta prueba, las demas de base de datos "pasarian" sin ejecutarse si SQL Server no responde.
        // Para correr sin SQL Server: definir BROSLMV_PRUEBAS_OMITIR_SQL=1.
        [Fact]
        public void SqlServer_EstaDisponible_ParaLasPruebasDeBaseDeDatos()
        {
            if (Environment.GetEnvironmentVariable("BROSLMV_PRUEBAS_OMITIR_SQL") == "1") return;
            Assert.True(_bd.Disponible, "No se pudo crear la base desechable en SQL Server. Define BROSLMV_PRUEBAS_SQL con el servidor o BROSLMV_PRUEBAS_OMITIR_SQL=1.");
        }
    }

    public class CoberturaTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public CoberturaTests(BaseDesechable bd) { _bd = bd; }

        private static DateTime D(int dia) => new DateTime(2026, 9, dia);

        [Fact]
        public void SolicitudBuenaPosterior_NoTapaElHuecoAnterior()
        {
            if (!_bd.Disponible) return;
            BrosSatDb.RegistrarSolicitud(_bd.Conn, "a-1", BaseDesechable.Rfc, "Recibidos", D(1), D(16).AddHours(23), "Automatica");
            BrosSatDb.ActualizarEstatusSolicitud(_bd.Conn, "a-1", "Terminada", 10);
            BrosSatDb.RegistrarSolicitud(_bd.Conn, "a-2", BaseDesechable.Rfc, "Recibidos", D(19), D(20), "Automatica");
            BrosSatDb.ActualizarEstatusSolicitud(_bd.Conn, "a-2", "Terminada", 3);

            var cubiertos = BrosSatDb.ObtenerRangosCubiertos(_bd.Conn, BaseDesechable.Rfc, "CFDI", "Recibidos");
            var hueco = Huecos.PrimerHueco(cubiertos, D(1), D(20));
            Assert.Equal(D(17), hueco.Value.Desde);
        }

        [Fact]
        public void SolicitudRechazadaONoTerminada_NoCuentaComoCubierta()
        {
            if (!_bd.Disponible) return;
            BrosSatDb.RegistrarIntentoFallido(_bd.Conn, BaseDesechable.Rfc, "Emitidos", D(1), D(5), "Automatica", "CFDI", "5002", "Se han agotado las solicitudes de por vida");
            var cubiertos = BrosSatDb.ObtenerRangosCubiertos(_bd.Conn, BaseDesechable.Rfc, "CFDI", "Emitidos");
            Assert.Empty(cubiertos);
            // ...pero un rechazo reciente SI se omite temporalmente para pasar al siguiente hueco
            Assert.Single(BrosSatDb.ObtenerRangosRechazadosRecientes(_bd.Conn, BaseDesechable.Rfc, "CFDI", "Emitidos", 6));
        }

        [Fact]
        public void SolicitudAtorada_PasaAVencidaYSeReabreElRango()
        {
            if (!_bd.Disponible) return;
            BrosSatDb.RegistrarSolicitud(_bd.Conn, "atorada-1", BaseDesechable.Rfc, "Recibidos", new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), "Automatica");
            _bd.Ejecutar("UPDATE SolicitudDescarga SET FechaSolicitud = DATEADD(DAY,-5,SYSUTCDATETIME()) WHERE IdSolicitud='atorada-1'");
            int vencidas = BrosSatDb.VencerSolicitudesAtoradas(_bd.Conn, BaseDesechable.Rfc, 3);
            Assert.True(vencidas >= 1);
            Assert.Equal("Vencida", _bd.Escalar<string>("SELECT Estatus FROM SolicitudDescarga WHERE IdSolicitud='atorada-1'"));
        }

        [Fact]
        public void EstadoInvalidoDelSat_NoSacaLaSolicitudDeLosPendientes()
        {
            if (!_bd.Disponible) return;
            BrosSatDb.RegistrarSolicitud(_bd.Conn, "meta-1", BaseDesechable.Rfc, "Emitidos", new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), "Automatica", "Metadata");
            // El SAT contesto sin EstadoSolicitud valido (bug 18): se anota y la solicitud sigue pendiente
            BrosSatDb.RegistrarVerificacionSinEstado(_bd.Conn, "meta-1", "5000", "texto del SAT");
            var pendientes = BrosSatDb.ObtenerSolicitudesPendientes(_bd.Conn, BaseDesechable.Rfc);
            Assert.Contains(pendientes, p => p.IdSolicitud == "meta-1");
            Assert.Equal("5000", _bd.Escalar<string>("SELECT UltimoCodEstatus FROM SolicitudDescarga WHERE IdSolicitud='meta-1'"));
        }

        [Fact]
        public void ReintentoDelMismoRango_SeCuenta()
        {
            if (!_bd.Disponible) return;
            BrosSatDb.RegistrarIntentoFallido(_bd.Conn, BaseDesechable.Rfc, "Recibidos", D(1), D(30), "Automatica", "CFDI", "5002", "x");
            BrosSatDb.RegistrarIntentoFallido(_bd.Conn, BaseDesechable.Rfc, "Recibidos", D(1), D(30), "Automatica", "CFDI", "5002", "x");
            Assert.Equal(2, BrosSatDb.ContarSolicitudesDelRango(_bd.Conn, BaseDesechable.Rfc, "CFDI", "Recibidos", D(1), D(30)));
        }
    }

    public class EstatusTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public EstatusTests(BaseDesechable bd) { _bd = bd; }

        private int NuevoCfdi(Guid uuid, string emisor, string receptor, DateTime fecha, string ruta = null)
        {
            var c = new CfdiParseado { UUID = uuid, RFCEmisor = emisor, RFCReceptor = receptor, FechaEmision = fecha, Total = 116m, TipoComprobante = "I", Serie = "A", Folio = "1" };
            return BrosSatDb.GuardarCfdiRecibido(_bd.Conn, c, ruta, out _);
        }

        [Fact]
        public void NoEncontrado_NoPisaUnEstatusBueno()
        {
            if (!_bd.Disponible) return;
            var uuid = Guid.NewGuid();
            int id = NuevoCfdi(uuid, "AAA010101AAA", BaseDesechable.Rfc, new DateTime(2026, 2, 1));
            BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, id, "No Encontrado");
            Assert.Equal("Vigente", _bd.Escalar<string>("SELECT EstatusSat FROM CfdiRecibido WHERE CfdiID=" + id));
            Assert.Equal(0, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstatusHistorial WHERE CfdiID=" + id));
        }

        [Fact]
        public void Cancelacion_QuedaEnElHistorial()
        {
            if (!_bd.Disponible) return;
            int id = NuevoCfdi(Guid.NewGuid(), "AAA010101AAA", BaseDesechable.Rfc, new DateTime(2026, 2, 2));
            BrosSatDb.ActualizarEstatusCfdi(_bd.Conn, id, "Cancelado", "Cancelado sin aceptacion");
            Assert.Equal("Cancelado", _bd.Escalar<string>("SELECT EstatusSat FROM CfdiRecibido WHERE CfdiID=" + id));
            Assert.Equal(1, _bd.Escalar<int>("SELECT COUNT(*) FROM CfdiEstatusHistorial WHERE CfdiID=" + id + " AND EstatusNuevo='Cancelado'"));
        }

        [Fact]
        public void Metadata_CancelaUnCfdiYaDescargado_PeroNuncaAlReves()
        {
            if (!_bd.Disponible) return;
            var uuid = Guid.NewGuid();
            int id = NuevoCfdi(uuid, "AAA010101AAA", BaseDesechable.Rfc, new DateTime(2026, 2, 3));
            var m = new MetadataParseada { UUID = uuid, RFCEmisor = "AAA010101AAA", RFCReceptor = BaseDesechable.Rfc, FechaEmision = new DateTime(2026, 2, 3), Total = 116m, EstatusSat = "Cancelado", LineaCruda = "x" };
            BrosSatDb.GuardarMetadata(_bd.Conn, m, "Recibidos", "no-existe");
            Assert.Equal("Cancelado", _bd.Escalar<string>("SELECT EstatusSat FROM CfdiRecibido WHERE CfdiID=" + id));

            m.EstatusSat = "Vigente"; // una foto vieja no debe revivirlo
            BrosSatDb.GuardarMetadata(_bd.Conn, m, "Recibidos", "no-existe");
            Assert.Equal("Cancelado", _bd.Escalar<string>("SELECT EstatusSat FROM CfdiRecibido WHERE CfdiID=" + id));
        }

        [Fact]
        public void Conciliacion_DetectaLosFaltantes()
        {
            if (!_bd.Disponible) return;
            var tengo = Guid.NewGuid(); var falta = Guid.NewGuid();
            NuevoCfdi(tengo, "AAA010101AAA", BaseDesechable.Rfc, new DateTime(2025, 11, 5));
            foreach (var u in new[] { tengo, falta })
                BrosSatDb.GuardarMetadata(_bd.Conn, new MetadataParseada { UUID = u, RFCEmisor = "AAA010101AAA", RFCReceptor = BaseDesechable.Rfc, FechaEmision = new DateTime(2025, 11, 5), Total = 116m, EstatusSat = "Vigente", LineaCruda = "x" }, "Recibidos", "no-existe");

            var fila = Conciliacion.Calcular(_bd.Conn, BaseDesechable.Rfc).Single(f => f.Anio == 2025 && f.Mes == 11 && f.Tipo == "Recibidos");
            Assert.Equal(2, fila.SatTotal);
            Assert.Equal(1, fila.Descargados);
            Assert.Equal(1, fila.Faltan);
            Assert.Equal("Faltan 1", fila.Estado);
        }

        [Fact]
        public void RepararNombresLiterales_RenombraAUuid()
        {
            if (!_bd.Disponible) return;
            string carpeta = Path.Combine(Path.GetTempPath(), "bros_pruebas_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(carpeta);
            try
            {
                var uuid = Guid.NewGuid();
                string mala = Path.Combine(carpeta, "[UUID]_abcd1234.xml");
                File.WriteAllText(mala, "<x/>");
                int id = NuevoCfdi(uuid, "AAA010101AAA", BaseDesechable.Rfc, new DateTime(2026, 2, 4), mala);

                int arreglados = OrganizadorArchivos.RepararNombresLiterales(_bd.Conn, BaseDesechable.Rfc);
                Assert.True(arreglados >= 1);
                Assert.True(File.Exists(Path.Combine(carpeta, uuid + ".xml")));
                Assert.False(File.Exists(mala));
                Assert.Equal(Path.Combine(carpeta, uuid + ".xml"), _bd.Escalar<string>("SELECT RutaArchivoXml FROM CfdiRecibido WHERE CfdiID=" + id));
            }
            finally { try { Directory.Delete(carpeta, true); } catch { } }
        }
    }

    public class SaludTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public SaludTests(BaseDesechable bd) { _bd = bd; }

        [Fact]
        public void Evaluar_NoLanza_YAvisaQueNoHayServicio()
        {
            if (!_bd.Disponible) return;
            var empresa = new EmpresaFila { RFC = BaseDesechable.Rfc, Nombre = "Prueba", Activa = true, AnioInicioDescargas = 2026 };
            var hallazgos = Salud.Evaluar(_bd.Conn, empresa);
            Assert.Contains(hallazgos, h => h.Tema == "Servicio" && h.Nivel != NivelSalud.Ok);

            BrosSatDb.GuardarEstado(_bd.Conn, Salud.ClaveLatido);
            hallazgos = Salud.Evaluar(_bd.Conn, empresa);
            Assert.Contains(hallazgos, h => h.Tema == "Servicio" && h.Nivel == NivelSalud.Ok);
        }
    }
}
