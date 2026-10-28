// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using System.Collections.Generic;
using System.IO;

namespace BrosLMV.Bancos.Motor
{
    // Puerta de entrada: segun la extension convierte el archivo del banco en una o varias tablas de texto.
    public static class Lector
    {
        public static readonly string[] Extensiones = { ".csv", ".txt", ".xlsx", ".xlsm", ".xls", ".pdf" };

        public static bool EsArchivoDeBanco(string ruta)
        {
            return Array.IndexOf(Extensiones, Path.GetExtension(ruta).ToLowerInvariant()) >= 0;
        }

        public static List<HojaArchivo> Leer(string ruta, List<string> notas)
        {
            string ext = Path.GetExtension(ruta).ToLowerInvariant();
            string nombre = Path.GetFileName(ruta);
            if (ext == ".xlsx" || ext == ".xlsm") return BancoXlsx.Leer(ruta);
            if (ext == ".pdf") return new List<HojaArchivo> { new HojaArchivo { Nombre = nombre, Filas = BancoPdf.Leer(ruta, notas) } };
            if (ext == ".xls")
            {
                // Muchos bancos (Monex) llaman .xls a un archivo de texto. Un Excel antiguo de verdad (cabecera D0 CF 11 E0) todavia no se lee.
                var cab = new byte[4];
                using (var fs = File.OpenRead(ruta)) fs.Read(cab, 0, 4);
                if (cab[0] == 0xD0 && cab[1] == 0xCF && cab[2] == 0x11 && cab[3] == 0xE0)
                    throw new InvalidOperationException("Este .xls es un Excel antiguo (binario) y todavia no se lee. Guardalo como .xlsx o como CSV desde Excel.");
            }
            return new List<HojaArchivo> { new HojaArchivo { Nombre = nombre, Filas = BancoTexto.Leer(ruta) } };
        }
    }
}
