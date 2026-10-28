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
    public sealed class BancoMov
    {
        public DateTime Fecha;
        public int Orden;
        public int FilaOrigen;
        public string Descripcion = "", Detalle = "", Referencia = "", CuentaArchivo = "", Moneda = "";
        public decimal Cargo, Abono;
        public decimal? Saldo;
        public string ClaveRastreo = "", RfcContraparte = "", NombreContraparte = "", ClabeContraparte = "", BancoContraparte = "", Tipo = "";
        public string Hash = "";
        public decimal Importe => Abono - Cargo;      // + entra dinero, - sale
    }
}
