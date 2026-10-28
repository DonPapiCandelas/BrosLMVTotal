// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BrosLMV.Bancos.Motor
{
    public sealed class BancoResultado
    {
        public List<BancoMov> Movs = new List<BancoMov>();
        public List<string> Errores = new List<string>();       // bloquean la importacion
        public List<string> Avisos = new List<string>();
        public int FilasIgnoradas;
        public DateTime? Desde, Hasta;
        public decimal? SaldoInicial, SaldoFinal;
        public int RupturasSaldo;                               // renglones donde saldo anterior +/- movimiento no da el saldo
        public bool SaldoVerificado;                            // el archivo trae saldo y se pudo revisar
        public bool SaldoDeuda;                                 // el saldo sube con los cargos (tarjeta de credito)
        public bool Descendente;                                // el archivo venia del mas nuevo al mas viejo
        public decimal TotalCargos => Movs.Sum(m => m.Cargo);
        public decimal TotalAbonos => Movs.Sum(m => m.Abono);
        public IEnumerable<string> Cuentas => Movs.Select(m => m.CuentaArchivo).Where(c => !string.IsNullOrEmpty(c)).Distinct();
    }
}
