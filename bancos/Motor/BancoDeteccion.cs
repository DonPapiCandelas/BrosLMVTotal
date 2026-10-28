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
    public sealed class BancoDeteccion
    {
        public string Banco = "";            // "" = no se pudo saber
        public string Perfil = "";
        public int Confianza;                // 0 nada, 1 baja, 2 media, 3 alta
        public BancoMapa Mapa = new BancoMapa();
        public List<string> CuentasPreambulo = new List<string>();   // numeros de cuenta/CLABE que el archivo menciona antes de la tabla
        public List<string> Notas = new List<string>();
        public bool MapaPorEncabezado;       // false: se infirmo mirando el contenido (confianza baja)
        public BrosLMV.Bancos.Layouts.Layout Layout;                                      // el layout que se uso (null si solo se infirio)
        public List<BrosLMV.Bancos.Layouts.Coincidencia> Coincidencias = new List<BrosLMV.Bancos.Layouts.Coincidencia>();   // todos los layouts probados, con su porcentaje
    }
}
