# Recepción de mercancía y protección de colas

Patrones verificados en una integración web con Comercial y SQL Server. Son criterios de integración, no cambios del núcleo del addon o Runner.

- Conservar vínculo exacto de la partida origen. El pendiente físico utiliza entregas vigentes vinculadas mediante `DeliverDocumentItemID`; una factura sin afectación de inventario no equivale a recepción. Verificar tipos y módulos de la instalación.
- Reservar capturas todavía no confirmadas. No sumar otra vez una recepción ya reflejada en Comercial.
- Revalidar proveedor, documento, partida y disponible al ejecutar. Acumular cantidades de partidas repetidas antes de compararlas con el pendiente.
- Reclamar la cola atómicamente y serializar por orden origen. Usar una clave única para la captura; proteger separadamente la ejecución nativa.
- Guardar el identificador nativo inmediatamente, incluso si falla una operación posterior. Si ya existe, conciliar en vez de crear otro documento automáticamente.
- Revisar resultado/error SDK después de cálculo, costos, inventario y guardado; crear un encabezado no acredita ejecución completa.
- Volver a leer precio, impuesto, moneda, unidad y proyecto del origen al ejecutar. Confirmar si el descuento es fracción o porcentaje.
- Serializar decimales con precisión explícita: valores PHP en notación científica pueden fallar al convertirse en SQL Server.
- Bloquear conversiones de unidades que no estén implementadas y verificadas.
- Restringir datos y rutas del servidor para almacén, incluidas peticiones Livewire y descargas. Ocultar botones no controla el acceso.

En pruebas con base copiada, configurar conexión y caché antes del arranque de proveedores. Cambiar la conexión después no garantiza aislamiento de permisos ni cachés.

Separar monedas y distinguir saldo nativo, pagos, compromisos y gasto. Sin tarifa por hora, el tiempo registrado no constituye costo ni utilidad.
