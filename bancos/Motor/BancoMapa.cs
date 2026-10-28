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
    public sealed class BancoMapa
    {
        public int FilaEncabezado { get; set; } = -1;     // -1: el archivo no trae encabezado
        public int PrimeraFila { get; set; }              // primera fila de datos
        public int Fecha { get; set; } = -1;
        public int Descripcion { get; set; } = -1;
        public List<int> Detalle { get; set; } = new List<int>();    // columnas que amplian la descripcion (descripcion larga, concepto...)
        public int Referencia { get; set; } = -1;
        public int Cargo { get; set; } = -1;
        public int Abono { get; set; } = -1;
        public int Importe { get; set; } = -1;            // columna unica con signo (o con Signo aparte)
        public int Signo { get; set; } = -1;              // columna que dice Cargo/Abono ("-", "+", "Cargo", "Abono")
        public int Saldo { get; set; } = -1;
        public int Cuenta { get; set; } = -1;
        public int Moneda { get; set; } = -1;
        public int Hora { get; set; } = -1;
        public bool ImporteNegativoEsCargo { get; set; } = true;     // en tarjetas como Amex el importe positivo es la compra
        public string FormatoFecha { get; set; } = "auto";           // auto (dd/mm) | mdy
        public string Firma { get; set; } = "";                      // encabezado normalizado: identifica el formato para recordarlo

        public BancoMapa Copiar() { var m = (BancoMapa)MemberwiseClone(); m.Detalle = new List<int>(Detalle); return m; }
        public bool Completo => Fecha >= 0 && (Cargo >= 0 || Abono >= 0 || Importe >= 0);
    }
}
