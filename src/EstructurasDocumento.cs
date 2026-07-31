namespace BrosLMV
{
    // Metadatos de "cómo se arma" un tipo de documento destino, sacados de MANUAL.md §7.
    // Generaliza el patrón NuevoDocumento -> perfil -> AgregarArticulo x N -> RecalcCompleto
    // -> AffectStockNEW? -> Save -> post-Save fixes, para que una receta no necesite un
    // caso especial por cada ModuleID.
    public class EstructuraDocumento
    {
        public int ModuleId;
        public string Nombre;
        // Perfil de encabezado (aplicado con UPDATE docDocument justo despues de NuevoDocumento):
        public bool DepotIdFromEsDepotId;   // true: DepotIDFrom=DepotID (Entrada/Salida). false: DepotIDFrom=0 (OC/Pedido/...)
        public int PaymentTermId;           // 0 = sin plazo (Entrada/Salida). OC suele usar otro valor -- ver nota en MANUAL.md 7.5.
        public bool RequiereFechaEntrega;   // true: UPDATE DateDelivery=GETDATE(), DateDocDelivery=GETDATE()
        // Partidas:
        public int? TaxTypeIdPartida;       // si no es null, UPDATE docDocumentItem SET TaxTypeID=<valor> despues de AgregarArticulo x N
        // Post-proceso (orden fijo, ver MANUAL.md 7.1 "patron canonico"):
        public bool AfectaInventario;       // true: llama ctx.erp.AffectStockNEW(doc) antes de Save
        public bool GeneraInfoPago;         // true: llama ctx.erp.UpdateDocumentPaidInfo(doc) despues de Save (solo OC/Factura)
    }

    public static class EstructurasRegistro
    {
        private static readonly System.Collections.Generic.Dictionary<int, EstructuraDocumento> Todas =
            new System.Collections.Generic.Dictionary<int, EstructuraDocumento>();

        static EstructurasRegistro()
        {
            // Orden de compra (ModuleID=183) -- MANUAL.md 7.5. Primera estructura real,
            // porque es la que usa la receta estrella de la fase 4.
            Registrar(new EstructuraDocumento
            {
                ModuleId = 183,
                Nombre = "Orden de compra",
                DepotIdFromEsDepotId = false,
                PaymentTermId = 0, // MANUAL.md usa 4 (50%+50% a 3 meses) como ejemplo -- 0 es
                                    // seguro en cualquier empresa (sin depender de que exista
                                    // el PaymentTermID=4 en su catalogo). Documentar en
                                    // MANUAL.md si se cambia el default.
                RequiereFechaEntrega = true,
                TaxTypeIdPartida = 5, // IVA 16% -- ver nota en MANUAL.md 7.5
                AfectaInventario = true, // deja kardex con Qty=0 (compromete sin mover), NO lo salta
                GeneraInfoPago = true,
            });

            // Entrada de almacen (ModuleID=202) -- MANUAL.md 7.3. Segunda estructura, mas
            // simple, para probar que el codigo generico NO esta hardcodeado solo para OC.
            Registrar(new EstructuraDocumento
            {
                ModuleId = 202,
                Nombre = "Entrada de almacen",
                DepotIdFromEsDepotId = true,
                PaymentTermId = 0,
                RequiereFechaEntrega = false,
                TaxTypeIdPartida = null,
                AfectaInventario = true,
                GeneraInfoPago = false,
            });

            // Agregar mas estructuras aqui conforme se necesiten (Salida=203, Recepcion=184,
            // Solicitud=1040, Factura=152, Traspaso=204 -- todas ya documentadas en
            // MANUAL.md 7.4/7.6/7.7/7.8, mismo patron).
        }

        private static void Registrar(EstructuraDocumento e) { Todas[e.ModuleId] = e; }

        public static EstructuraDocumento Buscar(int moduleId)
        { return Todas.TryGetValue(moduleId, out var e) ? e : null; }
    }
}
