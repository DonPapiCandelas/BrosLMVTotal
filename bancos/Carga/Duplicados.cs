// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.Bancos.Carga
{
    // Un renglon que ya esta en el estado de cuenta de Comercial (cargado por BrosLMV, por Logical o a mano).
    public class Existente
    {
        public long EdoCtaId;
        public DateTime Fecha;
        public decimal Cargo, Abono;
        public decimal? Saldo;           // saldo despues del movimiento; null o 0 = no se guardo
        public TimeSpan? Hora;
        public string Rastreo = "";
    }

    // Decide cuales movimientos de un archivo YA estan cargados. Mismo importe y misma fecha pueden ser dos movimientos distintos
    // (dos depositos iguales el mismo dia), asi que no basta contar: se compara con lo que distingue a un movimiento de otro.
    //   1. hora de la transaccion        2. clave de rastreo        3. saldo despues del movimiento
    // Dos renglones que se contradicen en cualquiera de esas cosas NUNCA son el mismo. Solo si no hay nada con que distinguirlos
    // (un lado no trae saldo, hora ni rastreo) se empareja por conteo, como hacia el estado de cuenta de Logical.
    public static class Duplicados
    {
        private static bool SaldoUtil(decimal? s) { return s.HasValue && s.Value != 0m; }

        private static bool IgualHora(TimeSpan a, TimeSpan b)
        {
            // un lado puede traer segundos y el otro no: se compara hasta el minuto
            return (int)a.TotalMinutes == (int)b.TotalMinutes;
        }

        private static bool Conflicto(BancoMov c, Existente e)
        {
            if (c.Hora.HasValue && e.Hora.HasValue && !IgualHora(c.Hora.Value, e.Hora.Value)) return true;
            if (!string.IsNullOrEmpty(c.ClaveRastreo) && !string.IsNullOrEmpty(e.Rastreo) && !string.Equals(c.ClaveRastreo, e.Rastreo, StringComparison.OrdinalIgnoreCase)) return true;
            if (SaldoUtil(c.Saldo) && SaldoUtil(e.Saldo) && Math.Abs(c.Saldo.Value - e.Saldo.Value) > 0.005m) return true;
            return false;
        }

        private static bool Fuerte(BancoMov c, Existente e)
        {
            if (c.Hora.HasValue && e.Hora.HasValue && IgualHora(c.Hora.Value, e.Hora.Value)) return true;
            if (!string.IsNullOrEmpty(c.ClaveRastreo) && string.Equals(c.ClaveRastreo, e.Rastreo, StringComparison.OrdinalIgnoreCase)) return true;
            if (SaldoUtil(c.Saldo) && SaldoUtil(e.Saldo) && Math.Abs(c.Saldo.Value - e.Saldo.Value) <= 0.005m) return true;
            return false;
        }

        private static string Clave(DateTime f, decimal cargo, decimal abono)
        {
            return f.ToString("yyyyMMdd") + "|" + cargo.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + abono.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Devuelve los movimientos del archivo que ya existen.
        public static HashSet<BancoMov> Marcar(IEnumerable<BancoMov> nuevos, IEnumerable<Existente> existentes)
        {
            var dup = new HashSet<BancoMov>();
            var porClave = existentes.GroupBy(e => Clave(e.Fecha, e.Cargo, e.Abono)).ToDictionary(g => g.Key, g => g.OrderBy(e => e.EdoCtaId).ToList());
            var usados = new HashSet<Existente>();
            var lista = nuevos.OrderBy(m => m.Orden).ToList();

            // Primera vuelta: lo que se puede asegurar con hora, rastreo o saldo.
            foreach (var c in lista)
            {
                List<Existente> e;
                if (!porClave.TryGetValue(Clave(c.Fecha, c.Cargo, c.Abono), out e)) continue;
                var par = e.FirstOrDefault(x => !usados.Contains(x) && !Conflicto(c, x) && Fuerte(c, x));
                if (par != null) { usados.Add(par); dup.Add(c); }
            }
            // Segunda vuelta: los que no tienen con que distinguirse se emparejan por conteo con lo que quedo sin pareja.
            foreach (var c in lista)
            {
                if (dup.Contains(c)) continue;
                List<Existente> e;
                if (!porClave.TryGetValue(Clave(c.Fecha, c.Cargo, c.Abono), out e)) continue;
                var par = e.FirstOrDefault(x => !usados.Contains(x) && !Conflicto(c, x));
                if (par != null) { usados.Add(par); dup.Add(c); }
            }
            return dup;
        }
    }
}
