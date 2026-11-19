// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BrosLMV.Bancos.Carga
{
    // Un renglon vigente del estado de cuenta de Comercial (docEdoCtaBanco), cargado por quien sea.
    public class RenglonEdoCta
    {
        public long EdoCtaId, CuentaId;
        public string Cuenta = "";
        public DateTime Fecha;
        public decimal Cargo, Abono;
        public decimal? Saldo;           // saldo despues del movimiento; null o 0 = no se guardo
        public string Descripcion = "";
        public bool Ligado;              // ya tiene un movimiento bancario (cobro/pago) relacionado
        public DateTime? Cargado;
    }

    // Un mismo movimiento que esta varias veces en el estado de cuenta.
    public class GrupoDuplicado
    {
        public RenglonEdoCta Conservar;                                   // el que se queda: el ligado a un pago, si hay; si no, el mas antiguo
        public List<RenglonEdoCta> Sobrantes = new List<RenglonEdoCta>(); // copias SIN pago ligado: las que se pueden dar de baja
        public List<RenglonEdoCta> LigadosExtra = new List<RenglonEdoCta>(); // otras copias que YA tienen pago ligado: nunca se tocan, se revisan a mano
        public int Copias { get { return 1 + Sobrantes.Count + LigadosExtra.Count; } }
    }

    // Detecta movimientos repetidos que YA estan en el estado de cuenta (los dejo otro cargador, una carga manual repetida, etc.).
    // Ejemplo real: un cargador anterior, tras actualizarse, volvio a insertar en cada ejecucion los movimientos del mismo dia:
    // decenas de copias del mismo renglon, cada una con un OrderNumber nuevo (por eso el orden no los delata).
    //
    // Dos renglones son el MISMO movimiento cuando coinciden en cuenta, fecha, cargo, abono, SALDO despues del movimiento y
    // descripcion. El saldo corrido es lo que distingue dos movimientos iguales del mismo dia (cada uno cambia el saldo): si dos
    // renglones tienen el mismo saldo, es el mismo movimiento cargado dos veces. Sin saldo guardado (null o 0) NO se agrupa: no hay
    // con que distinguir y podrian ser dos movimientos legitimos.
    public static class RevisionDuplicados
    {
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return string.Join(" ", s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static string Clave(RenglonEdoCta r)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return r.CuentaId + "|" + r.Fecha.ToString("yyyyMMdd") + "|" + r.Cargo.ToString("0.00", ci) + "|" + r.Abono.ToString("0.00", ci) + "|" + r.Saldo.Value.ToString("0.00", ci) + "|" + Norm(r.Descripcion);
        }

        // Una cuenta cuyo "saldo" es el del cierre del dia (el mismo en todos los renglones de un dia con varios movimientos distintos) NO sirve para
        // distinguir un movimiento repetido de dos iguales y legitimos: se omite y se avisa. Si ningun dia tiene varios movimientos distintos no hay
        // evidencia en contra y se revisa normal.
        public static HashSet<long> CuentasConSaldoPorDia(IEnumerable<RenglonEdoCta> renglones)
        {
            var salida = new HashSet<long>();
            foreach (var cuenta in renglones.Where(r => r.Saldo.HasValue && r.Saldo.Value != 0m).GroupBy(r => r.CuentaId))
            {
                bool hayDiaConVarios = false, algunoPorMovimiento = false;
                foreach (var dia in cuenta.GroupBy(r => r.Fecha))
                {
                    int movimientos = dia.Select(r => r.Cargo.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + r.Abono.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + Norm(r.Descripcion)).Distinct().Count();
                    if (movimientos < 2) continue;
                    hayDiaConVarios = true;
                    if (dia.Select(r => r.Saldo.Value).Distinct().Count() > 1) { algunoPorMovimiento = true; break; }
                }
                if (hayDiaConVarios && !algunoPorMovimiento) salida.Add(cuenta.Key);
            }
            return salida;
        }

        public static List<GrupoDuplicado> Buscar(IEnumerable<RenglonEdoCta> renglones) { List<string> omitidas; return Buscar(renglones, out omitidas); }

        public static List<GrupoDuplicado> Buscar(IEnumerable<RenglonEdoCta> renglones, out List<string> cuentasOmitidas)
        {
            var salida = new List<GrupoDuplicado>();
            var todos = renglones.ToList();
            var porDia = CuentasConSaldoPorDia(todos);
            cuentasOmitidas = todos.Where(r => porDia.Contains(r.CuentaId)).Select(r => r.Cuenta).Distinct().OrderBy(x => x).ToList();
            var conSaldo = todos.Where(r => r.Saldo.HasValue && r.Saldo.Value != 0m && !porDia.Contains(r.CuentaId));
            foreach (var g in conSaldo.GroupBy(Clave))
            {
                var lista = g.OrderBy(r => r.EdoCtaId).ToList();
                if (lista.Count < 2) continue;
                var conservar = lista.FirstOrDefault(r => r.Ligado) ?? lista[0];
                var gr = new GrupoDuplicado { Conservar = conservar };
                foreach (var r in lista)
                {
                    if (r == conservar) continue;
                    if (r.Ligado) gr.LigadosExtra.Add(r); else gr.Sobrantes.Add(r);
                }
                salida.Add(gr);
            }
            return salida.OrderByDescending(x => x.Conservar.Fecha).ThenBy(x => x.Conservar.Cuenta).ThenBy(x => x.Conservar.EdoCtaId).ToList();
        }

        public static int TotalSobrantes(IEnumerable<GrupoDuplicado> grupos) { return grupos.Sum(g => g.Sobrantes.Count); }
    }
}
