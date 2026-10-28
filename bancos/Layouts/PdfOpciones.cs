// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

namespace BrosLMV.Bancos.Layouts
{
    // Como leer un PDF con texto. "zonas" sirve para cualquier banco: cada renglon de movimiento empieza con una fecha y los importes se asignan a la
    // columna cuyo TITULO queda mas cerca (se ancla a las palabras del encabezado, no a pixeles, asi un PDF que se recorre un poco no se rompe).
    // La descripcion es el texto que queda arriba y abajo del renglon de la fecha. Las otras estrategias son lecturas hechas a mano para formatos de BBVA.
    public class PdfOpciones
    {
        public string Estrategia { get; set; } = "";               // zonas | bbvanet | export | estadocuenta (bbvanet = zonas con los valores de abajo)

        // ---- zonas ----
        public string FilaInicia { get; set; } = @"^\d{2}-\d{2}$";   // la primera palabra del renglon de movimiento (la fecha)
        public double FechaXMax { get; set; } = 60;                  // la fecha esta pegada a la izquierda: su X no pasa de aqui
        public string EncCargo { get; set; } = "Cargo";              // titulos de las columnas, como salen en el PDF
        public string EncAbono { get; set; } = "Abono";
        public string EncSaldo { get; set; } = "Saldo";
        public double DescXMin { get; set; } = 62;                   // la descripcion empieza a la derecha de esta X
        public double Alcance { get; set; } = 30;                    // hasta cuantos puntos arriba o abajo del renglon se busca su descripcion
        public string Ignorar { get; set; } = @"(?i)http|bbvanet|detalle de movimientos|^\d+/\d+$";   // lineas que no son movimiento (pies de pagina...)
        public string CuentaPatron { get; set; } = @"(?i)cuenta:?\s*(\d{6,18})";                      // donde dice el numero de cuenta (el grupo 1)
    }
}
