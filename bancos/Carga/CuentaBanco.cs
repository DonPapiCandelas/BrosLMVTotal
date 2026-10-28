// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BrosLMV.Bancos.Motor;

namespace BrosLMV.Bancos.Carga
{
    // Una cuenta de banco o tarjeta de Comercial (orgFinancialEntity, tipo 1 cuenta bancaria o 2 tarjeta).
    public class CuentaBanco
    {
        public long Id;
        public string Nombre = "", Numero = "", Clabe = "", Banco = "", Moneda = "";
        public int Tipo = 1;
        public decimal? UltimoSaldo;
        public DateTime? UltimaFecha;
        public override string ToString() { return Nombre + (Numero.Length > 0 ? " · " + Numero : "") + (Moneda.Length > 0 ? " · " + Moneda : ""); }
    }

    public class SugerenciaCuenta
    {
        public CuentaBanco Cuenta;       // null = que elija la persona
        public string Motivo = "";       // por que se sugiere, o por que no se pudo
        public bool Segura;              // true: numero de cuenta/divisa; false: hay que confirmar
    }

    // Que cuenta es, de mas a menos seguro. Con el numero que dice el archivo SOLO se adivina por numero; con varias cuentas del mismo banco y
    // ninguna pista, no elige sola.
    public static class SugeridorDeCuenta
    {
        public static SugerenciaCuenta Sugerir(List<CuentaBanco> cuentas, string banco, bool bancoSeguro, string cuentaArchivo, decimal? saldoInicial)
        {
            var r = new SugerenciaCuenta();
            string divisa = Regex.IsMatch(cuentaArchivo ?? "", @"^[A-Za-z]{3}$") ? cuentaArchivo.ToUpperInvariant().Replace("MXP", "MXN") : "";
            if (divisa.Length > 0)
            {
                var porDivisa = cuentas.Where(c => (string.IsNullOrEmpty(banco) || BancoTexto.Norm(c.Nombre + " " + c.Banco).Contains(BancoTexto.Norm(banco))) && c.Moneda.ToUpperInvariant() == divisa).ToList();
                if (porDivisa.Count == 1) { r.Cuenta = porDivisa[0]; r.Motivo = "por la divisa " + divisa; r.Segura = true; }
                return r;
            }
            if (!string.IsNullOrEmpty(cuentaArchivo))
            {
                var igual = cuentas.Where(c => BancoArchivo.MismaCuenta(cuentaArchivo, c.Numero) || BancoArchivo.MismaCuenta(cuentaArchivo, c.Clabe)).ToList();
                if (igual.Count == 1) { r.Cuenta = igual[0]; r.Motivo = "por el número de cuenta del archivo"; r.Segura = true; return r; }
                r.Motivo = igual.Count == 0
                    ? "El archivo dice que es de la cuenta " + cuentaArchivo + " y no hay una cuenta dada de alta con ese número."
                    : "El número de cuenta del archivo coincide con varias cuentas.";
                return r;
            }
            string nb = BancoTexto.Norm(banco ?? "");
            var delBanco = string.IsNullOrEmpty(nb) ? cuentas : cuentas.Where(c => { string cb = BancoTexto.Norm(c.Banco); return cb.Length > 0 && (cb.Contains(nb) || nb.Contains(cb)); }).ToList();
            if (saldoInicial.HasValue)
            {
                var porSaldo = (delBanco.Count > 0 ? delBanco : cuentas).Where(c => c.UltimoSaldo.HasValue && Math.Abs(c.UltimoSaldo.Value - saldoInicial.Value) < 0.005m).ToList();
                if (porSaldo.Count == 1) { r.Cuenta = porSaldo[0]; r.Motivo = "su saldo inicial es el último saldo cargado de esa cuenta"; return r; }
            }
            if (delBanco.Count == 1 && !string.IsNullOrEmpty(nb) && bancoSeguro) { r.Cuenta = delBanco[0]; r.Motivo = "es la única cuenta de ese banco"; }
            return r;
        }
    }
}
