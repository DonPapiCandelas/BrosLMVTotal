using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BrosLMV.Bancos.Motor;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Corre los archivos reales de los bancos y compara con la ultima corrida buena (golden). Si un cambio al motor altera como se lee
    // cualquier archivo, esta prueba lo dice con el nombre del archivo y la columna que cambio.
    public class RegresionCorpusTests
    {
        private static string Carpeta()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "BrosLMV.sln"))) d = d.Parent;
            return d == null ? null : Path.Combine(d.FullName, "Entrenamiento", "Bancos");
        }

        private static readonly string[] Cols = { "archivo", "banco", "perfil", "conf", "porEnc", "filas", "movs", "errores", "avisos", "desde", "hasta", "saldoIni", "saldoFin", "verif", "ruptur", "desc", "deuda", "cuentas", "sinTipo" };

        private static string S(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture); }

        private static string[] Resumen(string carpeta, string ruta)
        {
            var notas = new List<string>();
            var filas = Lector.Leer(ruta, notas)[0].Filas;
            var det = BancoArchivo.Detectar(filas, Path.GetFileName(ruta));
            var res = BancoArchivo.Interpretar(filas, det.Mapa);
            return new[]
            {
                ruta.Substring(carpeta.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), det.Banco, det.Perfil, S(det.Confianza), S(det.MapaPorEncabezado), S(filas.Count), S(res.Movs.Count), S(res.Errores.Count), S(res.Avisos.Count),
                res.Desde.HasValue ? res.Desde.Value.ToString("yyyy-MM-dd") : "", res.Hasta.HasValue ? res.Hasta.Value.ToString("yyyy-MM-dd") : "", S(res.SaldoInicial), S(res.SaldoFinal), S(res.SaldoVerificado), S(res.RupturasSaldo),
                S(res.Descendente), S(res.SaldoDeuda), string.Join(",", det.CuentasPreambulo.Concat(res.Cuentas).Distinct()), S(res.Movs.Count(m => m.Tipo.Length == 0))
            };
        }

        [Fact]
        public void LosArchivosRealesSeLeenComoLaUltimaVezQueFuncionaron()
        {
            string carpeta = Carpeta();
            string golden = carpeta == null ? null : Path.Combine(carpeta, "golden_lote.tsv");
            if (golden == null || !File.Exists(golden)) return;       // sin la carpeta privada no hay nada que comparar

            var esperado = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var linea in File.ReadAllLines(golden).Skip(1))
            {
                var c = linea.Split('\t'); if (c.Length < 19) continue;
                esperado[c[0]] = c.Take(19).ToArray();
            }

            var difs = new List<string>(); int comparados = 0;
            foreach (var ruta in Directory.GetFiles(carpeta, "*.*", SearchOption.AllDirectories).OrderBy(x => x))
            {
                if (!Lector.EsArchivoDeBanco(ruta)) continue;
                string[] real;
                try { real = Resumen(carpeta, ruta); } catch (Exception ex) { difs.Add(ruta + ": EXCEPCION " + ex.Message); continue; }
                string[] esp;
                if (!esperado.TryGetValue(real[0], out esp)) { difs.Add(real[0] + ": no estaba en el golden"); continue; }
                comparados++;
                for (int i = 1; i < Cols.Length; i++)
                    if (!string.Equals(real[i], esp[i], StringComparison.Ordinal)) difs.Add(real[0] + " [" + Cols[i] + "]: esperado «" + esp[i] + "» y salio «" + real[i] + "»");
            }
            Assert.True(comparados > 0, "no se comparo ningun archivo");
            Assert.True(difs.Count == 0, string.Join(Environment.NewLine, difs.Take(40)));
        }
    }
}
