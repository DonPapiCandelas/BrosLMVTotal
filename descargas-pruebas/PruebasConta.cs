using System;
using System.IO;
using System.Linq;
using BrosLMV.Descargas.Datos;
using BrosLMV.DescargasUI;
using ClosedXML.Excel;
using Xunit;

namespace BrosLMV.Descargas.Pruebas
{
    // Contabilizador, fase 3.1: terceros con cuentas por rol y moneda, validacion contra el catalogo y libro de Excel de ida y vuelta. Datos ficticios.
    public class ContaDatosTests : IClassFixture<BaseDesechable>
    {
        private readonly BaseDesechable _bd;
        public ContaDatosTests(BaseDesechable bd) { _bd = bd; }
        private const string Rfc = "CTA010101CTA";

        private void Cuenta(string rfc, string codigo, string nombre, string moneda = "MXN", bool afectable = true, bool baja = false) =>
            _bd.Ejecutar("INSERT INTO ContaCuentaCache (RFC, Codigo, Nombre, Tipo, Moneda, Afectable, EsBaja, UsaSegmento, FechaLectura) VALUES ('" + rfc + "','" + codigo + "','" + nombre + "','D','" + moneda + "'," + (afectable ? 1 : 0) + "," + (baja ? 1 : 0) + ",0,SYSUTCDATETIME())");
        private void Tercero(string rfc, string clave, string nombre) =>
            _bd.Ejecutar("INSERT INTO ContaTercero (RFC, Clave, TerceroRFC, Nombre, EsProveedor, EsCliente, Origen, NumCfdi) VALUES ('" + rfc + "','" + clave + "','" + clave + "','" + nombre + "',1,0,'Contabilidad',3)");

        [Fact]
        public void ClaveTercero_usaElRfc_ySoloDistingueLosGenericosPorNombre()
        {
            Assert.Equal("ABC010101AAA", ContaDatos.ClaveTercero(" abc010101aaa ", "Lo que sea"));
            var a = ContaDatos.ClaveTercero("XEXX010101000", "Bürger  Foods,  LLC");
            var b = ContaDatos.ClaveTercero("XEXX010101000", "BURGER FOODS, LLC");
            var c = ContaDatos.ClaveTercero("XEXX010101000", "Otra Empresa");
            Assert.Equal(a, b); Assert.NotEqual(a, c);
            Assert.StartsWith("SINRFC|", ContaDatos.ClaveTercero("", "Sin RFC SA"));
        }

        [Fact]
        public void Validar_rechazaCuentaInexistenteNoAfectableOBaja_yAvisaDeOtraMoneda()
        {
            if (!_bd.Disponible) return;
            Cuenta(Rfc, "20101001", "Proveedores MN"); Cuenta(Rfc, "20101002", "Proveedores USD", "USD"); Cuenta(Rfc, "20100000", "Pasivo", afectable: false); Cuenta(Rfc, "20101999", "Vieja", baja: true);
            Assert.Null(ContaDatos.Validar(_bd.Conn, Rfc, ContaRoles.Provision, "MXN", "20101001").error);
            Assert.Contains("no existe", ContaDatos.Validar(_bd.Conn, Rfc, ContaRoles.Provision, "MXN", "99999999").error);
            Assert.Contains("no es afectable", ContaDatos.Validar(_bd.Conn, Rfc, ContaRoles.Provision, "MXN", "20100000").error);
            Assert.Contains("baja", ContaDatos.Validar(_bd.Conn, Rfc, ContaRoles.Provision, "MXN", "20101999").error);
            var (e, aviso) = ContaDatos.Validar(_bd.Conn, Rfc, ContaRoles.Provision, "MXN", "20101002");     // cuenta en dolares para una asignacion en pesos
            Assert.Null(e); Assert.Contains("USD", aviso);
            Assert.NotNull(ContaDatos.Validar(_bd.Conn, Rfc, "RolInventado", "MXN", "20101001").error);
        }

        [Fact]
        public void Asignaciones_variasMonedasPorTercero_yLoManualNoSePisa()
        {
            if (!_bd.Disponible) return;
            const string rfc2 = "CTB010101CTB";
            Tercero(rfc2, "PRV010101PRV", "Proveedor Uno");
            ContaDatos.GuardarAsignacion(_bd.Conn, rfc2, "PRV010101PRV", ContaRoles.Provision, "MXN", "20101001", "Contabilidad");
            ContaDatos.GuardarAsignacion(_bd.Conn, rfc2, "PRV010101PRV", ContaRoles.Provision, "USD", "20101002");
            ContaDatos.GuardarAsignacion(_bd.Conn, rfc2, "PRV010101PRV", ContaRoles.Anticipo, "MXN", "11001001");
            var t = ContaDatos.ObtenerTerceros(_bd.Conn, rfc2).Single();
            Assert.Equal(3, t.Asignaciones.Count);
            Assert.Contains("20101002", t.Resumen(ContaRoles.Provision));
            // Guardar de nuevo la misma llave actualiza (no duplica) y pasa a Manual.
            ContaDatos.GuardarAsignacion(_bd.Conn, rfc2, "PRV010101PRV", ContaRoles.Provision, "MXN", "20101055");
            t = ContaDatos.ObtenerTerceros(_bd.Conn, rfc2).Single();
            Assert.Equal(3, t.Asignaciones.Count);
            Assert.Equal("Manual", t.Asignaciones.Single(a => a.Rol == ContaRoles.Provision && a.Moneda == "MXN").Origen);
            ContaDatos.BorrarAsignacion(_bd.Conn, rfc2, "PRV010101PRV", ContaRoles.Anticipo, "MXN");
            Assert.Equal(2, ContaDatos.ObtenerTerceros(_bd.Conn, rfc2).Single().Asignaciones.Count);
        }

        [Fact]
        public void Importacion_validaAntesDeAplicar_ySoloValidarNoEscribe()
        {
            if (!_bd.Disponible) return;
            const string rfc3 = "CTC010101CTC";
            Cuenta(rfc3, "20101001", "C1"); Cuenta(rfc3, "20101002", "C2", "USD");
            Tercero(rfc3, "PRV020202PRV", "Prov Dos");
            ContaDatos.GuardarAsignacion(_bd.Conn, rfc3, "PRV020202PRV", ContaRoles.Gasto, "MXN", "20101001");
            var filas = new[]
            {
                new ContaImportFila { Fila = 2, Clave = "PRV020202PRV", Rol = ContaRoles.Provision, Moneda = "MXN", Cuenta = "20101001" },
                new ContaImportFila { Fila = 3, Clave = "PRV020202PRV", Rol = ContaRoles.Provision, Moneda = "USD", Cuenta = "20101002" },
                new ContaImportFila { Fila = 4, Clave = "NO_EXISTE", Rol = ContaRoles.Provision, Moneda = "MXN", Cuenta = "20101001" },
                new ContaImportFila { Fila = 5, Clave = "PRV020202PRV", Rol = ContaRoles.Provision, Moneda = "MXN", Cuenta = "00000000" },
                new ContaImportFila { Fila = 6, Clave = "PRV020202PRV", Rol = ContaRoles.Gasto, Moneda = "MXN", Borrar = true },
            };
            var prueba = ContaDatos.AplicarImportacion(_bd.Conn, rfc3, filas, true);
            Assert.Equal(2, prueba.Guardadas); Assert.Equal(1, prueba.Borradas); Assert.Equal(2, prueba.Errores.Count);
            Assert.Single(ContaDatos.ObtenerTerceros(_bd.Conn, rfc3).Single().Asignaciones);           // nada se escribio
            ContaDatos.AplicarImportacion(_bd.Conn, rfc3, filas, false);
            var asigs = ContaDatos.ObtenerTerceros(_bd.Conn, rfc3).Single().Asignaciones;
            Assert.Equal(2, asigs.Count); Assert.DoesNotContain(asigs, a => a.Rol == ContaRoles.Gasto);
            Assert.Contains(asigs, a => a.Rol == ContaRoles.Provision && a.Moneda == "USD" && a.Cuenta == "20101002");
        }

        [Fact]
        public void ExcelDeTerceros_idaYVuelta_conservaLaClaveYLeeLoEditado()
        {
            var t1 = new ContaTerceroFila { Clave = "PRV030303PRV", Rfc = "PRV030303PRV", Nombre = "Prov Tres", EsProveedor = true, Origen = "Contabilidad" };
            t1.Asignaciones.Add(new ContaAsignacion { Rol = ContaRoles.Provision, Moneda = "MXN", Cuenta = "20101001", Origen = "Contabilidad" });
            var t2 = new ContaTerceroFila { Clave = "SINRFC|CLIENTE SIN CUENTA", Rfc = "", Nombre = "Cliente sin cuenta", EsCliente = true, Origen = "CFDI" };
            var cuentas = new[] { new ContaCuenta { Codigo = "20101001", Nombre = "Proveedores MN", Moneda = "MXN", Afectable = true }, new ContaCuenta { Codigo = "10501001", Nombre = "Clientes", Moneda = "MXN", Afectable = true } }.ToList();
            string ruta = Path.Combine(Path.GetTempPath(), "conta_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                ContaExcel.Exportar(ruta, "Empresa de prueba", new[] { t1, t2 }.ToList(), cuentas);
                // El usuario asigna la cuenta del cliente sin cuenta (el rol Cliente viene propuesto) y borra la del proveedor.
                using (var wb = new XLWorkbook(ruta))
                {
                    var ws = wb.Worksheet("Asignaciones");
                    int fCli = Enumerable.Range(2, 2).First(r => ws.Cell(r, 1).GetString() == t2.Clave);
                    Assert.Equal(ContaRoles.Cliente, ws.Cell(fCli, 5).GetString());
                    ws.Cell(fCli, 7).SetValue("10501001");
                    int fPrv = Enumerable.Range(2, 2).First(r => ws.Cell(r, 1).GetString() == t1.Clave);
                    ws.Cell(fPrv, 10).Value = "BORRAR";
                    wb.Save();
                }
                var filas = ContaExcel.Leer(ruta);
                Assert.Equal(2, filas.Count);
                var cli = filas.Single(f => f.Clave == t2.Clave);
                Assert.Equal("10501001", cli.Cuenta); Assert.Equal(ContaRoles.Cliente, cli.Rol); Assert.False(cli.Borrar);
                Assert.True(filas.Single(f => f.Clave == t1.Clave).Borrar);
            }
            finally { try { File.Delete(ruta); } catch { } }
        }
    }
}
