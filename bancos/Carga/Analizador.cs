// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BrosLMV.Bancos.Layouts;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.Bancos.Carga
{
    public enum EstadoArchivo { Listo, Revisar, YaCargado, SinLayout, Error }

    // Los movimientos de un archivo que van a UNA cuenta (un archivo con varias cuentas o divisas se reparte en grupos).
    public class GrupoCuenta
    {
        public string CuentaArchivo = "";            // lo que dice el archivo: numero de cuenta, divisa o vacio
        public List<BancoMov> Movs = new List<BancoMov>();
        public CuentaBanco Cuenta;                   // donde se cargan (null = falta elegir)
        public SugerenciaCuenta Sugerencia = new SugerenciaCuenta();
        public bool Confirmada;                      // la persona la confirmo o la eligio
        public string AvisoContinuidad = "";         // el saldo inicial del archivo no continua el ultimo saldo que Comercial tiene de la cuenta
        public bool ContinuidadConfirmada;           // la persona ya vio el aviso y quiere cargar asi
        public HashSet<BancoMov> Repetidos = new HashSet<BancoMov>();
        public int Nuevos { get { return Movs.Count - Repetidos.Count; } }
        public IEnumerable<BancoMov> MovsNuevos { get { return Movs.Where(m => !Repetidos.Contains(m)); } }
    }

    public class AnalisisArchivo
    {
        public string Ruta = "", Nombre = "", Hash = "";
        public EstadoArchivo Estado;
        public string Chip = "";                     // texto corto del estado
        public string Mensaje = "";                  // detalle cuando algo fallo
        public HojaArchivo Hoja;
        public BancoDeteccion Deteccion;
        public BancoResultado Resultado;
        public Layout Layout;                        // el layout con el que se leyo (null = ninguno reconocio el archivo)
        public bool BancoSeguro;
        public List<string> Notas = new List<string>();
        public List<GrupoCuenta> Grupos = new List<GrupoCuenta>();
        public int TotalMovs { get { return Grupos.Sum(g => g.Movs.Count); } }
        public int TotalNuevos { get { return Grupos.Sum(g => g.Nuevos); } }
        public int TotalRepetidos { get { return Grupos.Sum(g => g.Repetidos.Count); } }
        public string Banco { get { return Deteccion != null ? Deteccion.Banco : ""; } }
        public string Periodo
        {
            get
            {
                if (Resultado == null || !Resultado.Desde.HasValue) return "";
                var d = Resultado.Desde.Value; var h = Resultado.Hasta.Value;
                var ci = new System.Globalization.CultureInfo("es-MX");
                return d == h ? d.ToString("dd MMM yyyy", ci) : (d.Year == h.Year ? d.ToString("dd MMM", ci) + " – " + h.ToString("dd MMM yyyy", ci) : d.ToString("dd MMM yyyy", ci) + " – " + h.ToString("dd MMM yyyy", ci));
            }
        }
    }

    public delegate List<Existente> BuscarExistentes(long cuenta, DateTime desde, DateTime hasta);
    public delegate SaldoPrevio BuscarSaldoPrevio(long cuenta, DateTime fecha);

    public static class Analizador
    {
        public static string HashDe(string ruta)
        {
            using (var sha = SHA256.Create()) using (var fs = File.OpenRead(ruta)) return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        public static AnalisisArchivo Analizar(string ruta, IEnumerable<Layout> layouts, List<CuentaBanco> cuentas, BuscarExistentes existentes, BuscarSaldoPrevio saldoPrevio = null)
        {
            var a = new AnalisisArchivo { Ruta = ruta, Nombre = Path.GetFileName(ruta) };
            try
            {
                a.Hash = HashDe(ruta);
                var notas = new List<string>();
                var hojas = Lector.Leer(ruta, notas, layouts);
                if (hojas.Count == 0 || hojas[0].Filas.Count == 0) { a.Estado = EstadoArchivo.Error; a.Chip = "Vacío"; a.Mensaje = "El archivo no trae datos."; return a; }
                a.Hoja = hojas[0];
                a.Notas.AddRange(notas);
                a.Deteccion = BancoArchivo.Detectar(a.Hoja.Filas, a.Nombre, layouts);
                a.Layout = a.Deteccion.Layout;
                // PDF: el banco y el layout son los del layout de PDF que lo leyo (la tabla que arma el lector se parece a la de BBVA y no dice de que banco es)
                if (a.Hoja.LayoutPdf != null && !string.IsNullOrEmpty(a.Hoja.LayoutPdf.Banco)) { a.Deteccion.Banco = a.Hoja.LayoutPdf.Banco; a.Deteccion.Perfil = a.Hoja.LayoutPdf.Nombre; a.Deteccion.Confianza = Math.Max(a.Deteccion.Confianza, 2); a.Layout = a.Hoja.LayoutPdf; }
                a.BancoSeguro = a.Deteccion.Confianza >= 2 && a.Deteccion.Banco.Length > 0;
                a.Notas.AddRange(a.Deteccion.Notas);
                a.Resultado = BancoArchivo.Interpretar(a.Hoja.Filas, a.Deteccion.Mapa);
                if (a.Resultado.Errores.Count > 0) { a.Estado = EstadoArchivo.Error; a.Chip = "No se pudo leer"; a.Mensaje = a.Resultado.Errores[0]; return a; }
                a.Notas.AddRange(a.Resultado.Avisos);

                // un grupo por cuenta que dice el archivo
                foreach (var g in a.Resultado.Movs.GroupBy(m => m.CuentaArchivo ?? ""))
                    a.Grupos.Add(new GrupoCuenta { CuentaArchivo = g.Key, Movs = g.ToList() });
                string preambulo = a.Deteccion.CuentasPreambulo.FirstOrDefault();
                foreach (var g in a.Grupos)
                {
                    string declarada = g.CuentaArchivo.Length > 0 ? g.CuentaArchivo : (a.Grupos.Count == 1 ? (preambulo ?? "") : "");
                    decimal? saldoIni = a.Grupos.Count == 1 ? a.Resultado.SaldoInicial : null;
                    g.Sugerencia = SugeridorDeCuenta.Sugerir(cuentas, a.Banco, a.BancoSeguro, declarada, saldoIni);
                    g.Cuenta = g.Sugerencia.Cuenta;
                    g.Confirmada = g.Sugerencia.Segura;
                }
                Reevaluar(a, existentes, saldoPrevio);
            }
            catch (Exception ex)
            {
                bool sinLayout = ex.Message.StartsWith("No reconozco este PDF");
                a.Estado = sinLayout ? EstadoArchivo.SinLayout : EstadoArchivo.Error;
                a.Chip = sinLayout ? "Sin layout" : "No se pudo leer";
                a.Mensaje = ex.Message;
            }
            return a;
        }

        // Despues de elegir o confirmar cuentas: repetidos por grupo y estado final.
        public static void Reevaluar(AnalisisArchivo a, BuscarExistentes existentes, BuscarSaldoPrevio saldoPrevio = null)
        {
            foreach (var g in a.Grupos)
            {
                g.Repetidos = new HashSet<BancoMov>();
                if (g.Cuenta != null && existentes != null && g.Movs.Count > 0)
                    g.Repetidos = Duplicados.Marcar(g.Movs, existentes(g.Cuenta.Id, g.Movs.Min(m => m.Fecha), g.Movs.Max(m => m.Fecha)));
                g.AvisoContinuidad = g.Cuenta != null && saldoPrevio != null ? Continuidad(g, saldoPrevio) : "";
            }
            var r = a.Resultado;
            if (a.Layout == null) { a.Estado = EstadoArchivo.SinLayout; a.Chip = "Sin layout"; return; }
            if (a.Grupos.Any(g => g.Cuenta == null)) { a.Estado = EstadoArchivo.Revisar; a.Chip = a.Grupos.Count > 1 ? "Falta elegir cuentas" : "Elige la cuenta"; return; }
            if (a.TotalNuevos == 0) { a.Estado = EstadoArchivo.YaCargado; a.Chip = "Ya cargado"; return; }
            if (a.Grupos.Any(g => !g.Confirmada)) { a.Estado = EstadoArchivo.Revisar; a.Chip = "Confirma la cuenta"; return; }
            if (a.Grupos.Any(g => g.AvisoContinuidad.Length > 0 && !g.ContinuidadConfirmada)) { a.Estado = EstadoArchivo.Revisar; a.Chip = "El saldo no continúa"; return; }
            if (r != null && r.RupturasSaldo > 0) { a.Estado = EstadoArchivo.Revisar; a.Chip = "El saldo no cuadra"; return; }
            if (r != null && r.Movs.Count == 500) { a.Estado = EstadoArchivo.Revisar; a.Chip = "Tope de 500 movimientos"; return; }
            a.Estado = EstadoArchivo.Listo; a.Chip = "Listo";
        }

        public static void Asignar(AnalisisArchivo a, GrupoCuenta g, CuentaBanco cuenta, BuscarExistentes existentes, BuscarSaldoPrevio saldoPrevio = null)
        {
            g.Cuenta = cuenta; g.Confirmada = cuenta != null; g.ContinuidadConfirmada = false;
            Reevaluar(a, existentes, saldoPrevio);
        }

        // El saldo con que empieza el archivo (saldo del primer movimiento menos lo que ese movimiento movio) debe ser el que Comercial ya tiene de la cuenta.
        // Si no coincide, probablemente falta un periodo por cargar, o la cuenta elegida no es la del archivo. Sin saldos en el archivo o sin nada previo, no hay con que comparar.
        private static string Continuidad(GrupoCuenta g, BuscarSaldoPrevio saldoPrevio)
        {
            var primero = g.Movs.OrderBy(m => m.Orden).FirstOrDefault();
            if (primero == null || !primero.Saldo.HasValue || primero.Saldo.Value == 0m) return "";
            if (g.Movs.Count > 1 && g.Movs.Select(m => m.Saldo).Distinct().Count() == 1) return "";      // un solo saldo para todo el archivo: no es saldo por movimiento
            decimal inicial = Math.Round(primero.Saldo.Value - primero.Abono + primero.Cargo, 2);
            SaldoPrevio previo;
            try { previo = saldoPrevio(g.Cuenta.Id, primero.Fecha); } catch { return ""; }          // sin conexion o sin permiso no se inventa un aviso
            if (previo == null || previo.Saldos.Count == 0) return "";
            if (previo.Saldos.Any(s => Math.Abs(s - inicial) <= 0.01m)) return "";
            var ci = new System.Globalization.CultureInfo("es-MX");
            return "El archivo empieza con saldo " + inicial.ToString("C2", ci) + " y lo último que hay cargado en esta cuenta es " + previo.Ultimo.ToString("C2", ci) + " (" + previo.Fecha.ToString("dd/MM/yyyy") + "): puede faltar un periodo o ser otra cuenta.";
        }
    }
}
