using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Estados de cuenta en PDF ARMADOS AQUI: solo los titulos y marcas que usa cada banco, con datos inventados.
    // Nunca se guardan PDF reales de clientes: esto prueba la lectura sin llevar informacion de nadie.
    public class PdfSinteticosTests
    {
        // (La fuente estandar del PDF no trae acentos: el lector acepta los titulos con o sin acento.)
        // Pone palabras en un PDF con las coordenadas de arriba hacia abajo que usa el lector (Y desde el borde superior).
        private sealed class Hoja
        {
            private readonly PdfPageBuilder _p; private readonly PdfDocumentBuilder.AddedFont _f;
            public Hoja(PdfPageBuilder p, PdfDocumentBuilder.AddedFont f) { _p = p; _f = f; }
            public Hoja T(string texto, double x, double y) { _p.AddText(texto, 8, new PdfPoint(x, 792 - y), _f); return this; }
            // texto pegado a la derecha en x (los importes se alinean por su borde derecho)
            public Hoja D(string texto, double derecha, double y)
            {
                var letras = _p.MeasureText(texto, 8, new PdfPoint(0, 0), _f);
                double ancho = letras.Last().EndBaseLine.X - letras.First().StartBaseLine.X;
                return T(texto, derecha - ancho, y);
            }
            // varias palabras separadas (cada una es su propia palabra para el lector)
            public Hoja P(double x, double y, params string[] palabras)
            {
                foreach (var w in palabras)
                {
                    var letras = _p.MeasureText(w, 8, new PdfPoint(0, 0), _f);
                    T(w, x, y); x += letras.Last().EndBaseLine.X - letras.First().StartBaseLine.X + 4;
                }
                return this;
            }
        }

        private static string Armar(Action<Func<Hoja>> dibujar)
        {
            var b = new PdfDocumentBuilder(); var f = b.AddStandard14Font(Standard14Font.Helvetica);
            dibujar(() => new Hoja(b.AddPage(612, 792), f));
            string ruta = Path.Combine(Path.GetTempPath(), "bros_pdf_sintetico_" + Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllBytes(ruta, b.Build());
            return ruta;
        }

        private static List<string[]> Leer(string ruta, out Layout usado, out List<string> notas)
        {
            notas = new List<string>();
            try { return BancoPdf.Leer(ruta, notas, null, out usado); } finally { try { File.Delete(ruta); } catch { } }
        }

        // ---------- BBVA estado de cuenta oficial: solo la primera pagina trae los titulos de columna ----------
        [Fact]
        public void BbvaOficial_LasPaginasSiguientesUsanLasColumnasDeLaPrimera()
        {
            string ruta = Armar(nueva =>
            {
                var p1 = nueva();
                p1.P(318, 81, "No.", "de", "Cuenta", "1234567890").P(318, 51, "DEL", "01/06/2026", "AL", "30/06/2026");
                p1.P(10, 276, "Depositos", "/", "Abonos", "(+)", "2"); p1.P(10, 290, "Retiros", "/", "Cargos", "(-)", "1");
                p1.P(9, 489, "Detalle", "de", "Movimientos", "Realizados");
                p1.P(21, 530, "OPER").P(70, 530, "LIQ").P(381, 530, "CARGOS").P(427, 530, "ABONOS").P(469, 530, "OPERACION").P(536, 530, "LIQUIDACION");
                p1.P(16, 544, "17/JUN").P(61, 544, "17/JUN").P(107, 544, "SPEI", "RECIBIDO", "CLIENTE", "UNO"); p1.D("1,000.00", 458, 544);
                var p2 = nueva();                                        // sin titulos de columna: empieza debajo de «No. de Cliente»
                p2.P(335, 101, "No.", "de", "Cuenta", "1234567890").P(335, 118, "No.", "de", "Cliente", "11111111");
                p2.P(16, 191, "18/JUN").P(61, 191, "18/JUN").P(107, 191, "PAGO", "PROVEEDOR"); p2.D("300.00", 417, 191);
                p2.P(16, 247, "19/JUN").P(61, 247, "19/JUN").P(107, 247, "SPEI", "RECIBIDO", "CLIENTE", "DOS"); p2.D("500.50", 458, 247);
                p2.P(9, 569, "Total", "de", "Movimientos");
                p2.P(26, 592, "TOTAL", "IMPORTE", "CARGOS").D("300.00", 280, 592);
                p2.P(26, 606, "TOTAL", "IMPORTE", "ABONOS").D("1,500.50", 280, 606);
                p2.P(114, 725, "GASTOS", "OPERACION").D("0.00", 452, 725);   // apartados que siguen al detalle: no son movimientos
            });
            Layout usado; List<string> notas;
            var filas = Leer(ruta, out usado, out notas);
            Assert.Equal("bbva-pdf-estado-cuenta", usado.Id);
            var movs = filas.Where(f => f[0].StartsWith("20")).ToList();
            Assert.Equal(3, movs.Count);                                  // una pagina sin titulos ya no se salta
            Assert.Equal("1000.00", movs[0][3]); Assert.Equal("300.00", movs[1][2]); Assert.Equal("500.50", movs[2][3]);
            Assert.Contains(notas, n => n.StartsWith("Cuadra con los totales"));
        }

        // ---------- Banamex MiCuenta: bloques «DD MES», importes bajo RETIROS / DEPOSITOS / SALDO ----------
        [Fact]
        public void BanamexMiCuenta_LeeBloquesYUneImportesPartidos()
        {
            string ruta = Armar(nueva =>
            {
                var p1 = nueva();
                p1.P(25, 60, "Numero", "de", "cuenta", "de", "cheques", "123456789").P(25, 75, "Periodo", "del", "13", "de", "septiembre", "al", "12", "de", "octubre", "del", "2026");
                p1.P(25, 90, "MiCuenta").P(25, 110, "Detalle", "de", "Operaciones");
                p1.P(25, 150, "(+)", "2", "Depositos").D("1,500.00", 284, 150);
                p1.P(25, 165, "(-)", "1", "Retiros/Otros", "cargos").D("500.00", 286, 165);
                p1.P(25, 137, "FECHA").P(82, 137, "CONCEPTO").P(356, 137, "RETIROS").P(429, 137, "DEPOSITOS").P(533, 137, "SALDO");
                p1.P(25, 170, "13").P(42, 170, "SEP").P(82, 170, "SALDO", "ANTERIOR").D("2,000.00", 562, 170);          // solo saldo: no es movimiento
                p1.P(25, 190, "14").P(42, 190, "SEP").P(82, 190, "PAGO", "A", "PROVEEDOR");
                p1.P(82, 215, "HORA", "10:06").D("500.00", 391, 215).D("1,500.00", 562, 215);                         // los importes salen en otra linea del bloque
                p1.P(25, 240, "15").P(42, 240, "SEP").P(82, 240, "DEPOSITO", "EFECTIVO");
                p1.P(82, 265, "HORA", "11:00").D("1,000.0", 475, 265).T("0", 475.5, 265).D("2,500.00", 562, 265);     // «1,000.0» y «0» pegados: un solo importe
                p1.P(25, 290, "16").P(42, 290, "SEP").P(82, 290, "DEPOSITO", "TRANSFERENCIA");
                p1.P(82, 315, "HORA", "12:00").D("500.00", 478, 315).D("3,000.00", 562, 315);
            });
            Layout usado; List<string> notas;
            var filas = Leer(ruta, out usado, out notas);
            Assert.Equal("banamex-pdf-micuenta", usado.Id); Assert.Equal("Banamex", usado.Banco);
            var movs = filas.Where(f => f[0].StartsWith("20")).ToList();
            Assert.Equal(3, movs.Count);
            Assert.Equal("500.00", movs[0][2]); Assert.Equal("1000.00", movs[1][3]); Assert.Equal("500.00", movs[2][3]);
            Assert.Contains(notas, n => n.StartsWith("Cuadra con lo que declara"));
        }

        // ---------- Banamex tarjeta de credito: «dd-mmm-aaaa dd-mmm-aaaa descripcion + $importe» ----------
        [Fact]
        public void BanamexTarjeta_LeeCargosYAbonosPorSigno()
        {
            string ruta = Armar(nueva =>
            {
                var p1 = nueva();
                p1.P(300, 20, "Estado", "de", "Cuenta", "Mensual").P(26, 60, "CLABE", "Interbancaria").P(26, 421, "Cargos", "regulares").T("$450.00", 208, 421);
                p1.P(33, 682, "19-sep-2026").P(97, 682, "21-sep-2026").P(151, 682, "COMERCIO", "UNO").P(470, 682, "+").T("$300.00", 484, 682);
                p1.P(33, 700, "22-sep-2026").P(97, 700, "23-sep-2026").P(151, 700, "COMERCIO", "DOS").P(470, 700, "+").T("$150.00", 484, 700);
                p1.P(33, 718, "25-sep-2026").P(97, 718, "25-sep-2026").P(151, 718, "PAGO").P(470, 718, "-").T("$100.00", 484, 718);
            });
            Layout usado; List<string> notas;
            var filas = Leer(ruta, out usado, out notas);
            Assert.Equal("banamex-pdf-tarjeta", usado.Id);
            var movs = filas.Where(f => f[0].StartsWith("20")).ToList();
            Assert.Equal(3, movs.Count);
            Assert.Equal("300.00", movs[0][2]); Assert.Equal("150.00", movs[1][2]); Assert.Equal("100.00", movs[2][3]);   // el «-» es abono
        }
    }
}
