# Rentabilidad operativa de proyectos sin factura

## Criterio del reporte

Una factura no debe ser el único documento capaz de aportar ingreso a un reporte operativo. El criterio debe decir explícitamente si los Pedidos pendientes representan venta comprometida o venta realizada. Incluir pedidos permite obtener margen sobre el importe comprometido; no demuestra por sí mismo que el trabajo esté terminado ni cobrado.

Si el reporte ya incluye Pedidos, crear una Remisión no debe eliminar ese importe cuando la Remisión tiene FinancialAffectation=0. La exclusión genérica «existe cualquier documento hijo» puede dejar ambas filas fuera del cálculo.

## Sustitución por partidas

Para los módulos base Pedido (967) y Venta (158), seguir las partidas vigentes por SourceDocumentItemID, o DeliverDocumentItemID cuando el primero no está poblado. Puede existir la cadena Pedido → Remisión → Factura. Considerar como sustituto una Factura (21) o Venta (158) con afectación financiera positiva que aporte al mismo proyecto y empresa propia.

El importe pendiente del origen se calcula proporcionalmente a la cantidad no sustituida:

```text
cantidad_base = Quantity × CoefUnit
fracción_sustituida = suma de las fracciones convertidas a documentos que aportan ingreso
importe_pendiente = importe_origen × max(0, 1 − fracción_sustituida)
```

Cuando CoefUnit está vacío o es cero, validar la convención del motor antes de usar 1 como respaldo. En la implementación comprobada se usó ese respaldo. Detener cada recorrido al encontrar el siguiente documento financiero: si una Venta luego se factura, no debe contarse dos veces la misma conversión del Pedido.

Excluir documentos cancelados/eliminados y partidas eliminadas en cada salto. Conservar la cantidad no facturada cuando una factura es parcial. Las facturas eliminadas no deben consumir el importe del Pedido. Usar protección frente a ciclos y un límite explícito de profundidad; la implementación inicial usa 20 saltos. Revisar cadenas que lleguen a ese límite, vínculos incompletos y conversiones de unidades no convencionales antes de aceptar el resultado.

Si existe solo vínculo de encabezado hacia una factura sin relaciones de partidas, no puede inferirse una conversión parcial por cantidad. La compatibilidad inicial conserva la sustitución completa para ese caso, siempre que el destino sea financiero y pertenezca al mismo proyecto y empresa. Es un límite del dato, no evidencia de una facturación completa.

Un destino sin proyecto no debe hacer desaparecer el importe del origen; debe revisarse su asignación antes de usarlo como sustituto en la rentabilidad. Una reasignación entre proyectos también necesita una política expresa.

## Consistencia del reporte

Resumen, detalle y Excel deben consumir la misma fuente de inclusión. No calcular «cuenta en el total» usando solo SourceDocumentID cuando los importes se calculan por partidas. En conversiones parciales, el detalle muestra el importe incluido y explica el importe original; los documentos completamente sustituidos permanecen visibles con estado excluido.

Los costos y egresos son una revisión aparte: no sumar compra y consumo del mismo material dos veces ni llamar costo real al costo actual de catálogo sin justificarlo. Esta corrección de sustitución de ingreso no redefine esas reglas.

## Casos comprobados

Consultas sobre datos sintéticos verificaron Remisión sin factura, factura completa, factura parcial, parcial por Remisión, Pedido → Venta → Factura, dos facturas parciales, destino asignado a otro proyecto y módulo sin afectación financiera positiva. Se comprobó además la reconciliación entre resumen y detalle y la generación del HTML con el mismo conjunto de documentos usado por la exportación.

No requiere cambios al núcleo, versión nueva del addon ni reinstalación: corresponde a la vista y al script personalizado del reporte. Conservar respaldo de ambos y su historial antes de aplicar. Mantener HashSHA256 consistente con el código UTF-8 al guardar el script.
