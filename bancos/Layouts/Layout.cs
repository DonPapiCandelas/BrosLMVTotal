// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrosLMV.Bancos.Layouts
{
    // Un layout es un DATO: como reconocer un formato de banco y como leerlo. Se guarda como JSON, se versiona y se comparte.
    // Los de fabrica viajan con el programa; los de la empresa (creados o ajustados en el editor) viven en su base de datos y ganan.
    public class Layout
    {
        public string Id { get; set; } = "";
        public string Banco { get; set; } = "";                 // "" = formato que usan varios bancos (se elige el banco a mano)
        public string Nombre { get; set; } = "";
        public int Version { get; set; } = 1;
        public string Origen { get; set; } = "fabrica";         // fabrica | empresa
        public string Formato { get; set; } = "tabla";          // tabla (CSV, TXT, Excel) | pdf
        public int Confianza { get; set; } = 3;                 // 1 baja, 2 media, 3 alta: que tan seguro es el banco cuando este layout coincide
        public int Umbral { get; set; } = 100;                  // % de coincidencia minimo para usarlo solo
        public List<List<Condicion>> Reconocer { get; set; } = new List<List<Condicion>>();   // O de Y: basta que se cumpla UNA lista completa
        public Dictionary<string, List<string>> Columnas { get; set; }   // campo -> nombres de encabezado posibles, o "#3" (columna 3). Campos: fecha, descripcion, detalle, referencia, cargo, abono, importe, signo, saldo, cuenta, moneda
        public int FilaEncabezado { get; set; }                         // 1 = primera fila; 0 = buscarla por los nombres de Columnas
        public int PrimeraFila { get; set; }                            // primera fila de datos (1 = primera); 0 = la que sigue al encabezado
        public Ajustes Ajustes { get; set; } = new Ajustes();
        public List<string> Notas { get; set; } = new List<string>();
        public PdfOpciones Pdf { get; set; }

        private static readonly JsonSerializerOptions Opc = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string ADatos() { return JsonSerializer.Serialize(this, Opc); }
        public static Layout DeDatos(string json) { return JsonSerializer.Deserialize<Layout>(json, Opc); }
    }

    // Una condicion mira UN lugar del archivo: el encabezado de la tabla, el texto de las primeras filas, el nombre del archivo o el texto de un PDF.
    // Contiene / Empieza son listas: con que se cumpla alguno de sus valores basta. Todo se compara sin acentos ni mayusculas.
    public class Condicion
    {
        public string En { get; set; } = "encabezado";          // encabezado | texto | archivo | pdf
        public List<string> Contiene { get; set; }
        public List<string> Empieza { get; set; }
    }

    // Ajustes sobre lo que el motor ya deduce por encabezado: solo se escribe lo que difiere.
    public class Ajustes
    {
        public bool? ImporteNegativoEsCargo { get; set; }
        public string FormatoFecha { get; set; }                // auto (dd/mm) | mdy
        public bool CuentaDesdeDivisa { get; set; }             // cada divisa es una cuenta distinta (Monex)
    }

    public class PdfOpciones
    {
        public string Estrategia { get; set; } = "";            // bbvanet | export | estadocuenta
    }
}
