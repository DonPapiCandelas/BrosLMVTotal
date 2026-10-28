// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System.Collections.Generic;

namespace BrosLMV.Bancos.Motor
{
    // Una hoja o archivo ya leido como tabla de texto: lo unico que el resto del motor necesita saber de un archivo.
    public class HojaArchivo
    {
        public string Nombre;
        public List<string[]> Filas;
        public BrosLMV.Bancos.Layouts.Layout LayoutPdf;   // solo PDF: el layout con el que se leyo
        public override string ToString() { return Nombre + " (" + Filas.Count + " filas)"; }
    }
}
