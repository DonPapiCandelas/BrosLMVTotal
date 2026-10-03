# Asignar centro de costo de forma masiva

Plantilla de fábrica **Plantillas → Documentos → Asignar centro de costo de forma masiva** (`ASIGNAR_CENTRO_COSTO.ctx`). Pone un centro de costo a **muchos documentos de una vez**, con vista previa y con
**deshacer**. Los centros de costo deben estar ya dados de alta en el catálogo de Comercial: esta plantilla **no** los crea.

## Cómo usarla

1. En la **Consola** abre la plantilla, guárdala como botón (nombre sugerido `ASIGNAR_CENTRO_COSTO`) y ponla en el ribbon con *Crear botón…*.
2. En la lista de documentos de Comercial **selecciona** los documentos a los que quieres asignar el centro de costo y pulsa el botón. Sin documentos seleccionados la plantilla no hace nada y te lo dice.
3. **Paso 1 · Documentos seleccionados.** La ventana te muestra los documentos que seleccionaste (cuántos por tipo, y su cliente o proveedor y centro de costo actual). Los cancelados, eliminados o de otra empresa no entran y se avisa.
4. **Paso 2 · Qué centro de costo y dónde.**
   - **Dónde:** *Encabezado* (lo normal: es lo que usan casi todos los módulos), *Partidas* o *Ambos*.
   - **A cuáles:** *Solo donde esté vacío* (no pisa nada, es lo más seguro) o *Reemplazar el que tengan*.
5. **Vista previa:** cuántos documentos se modifican por módulo, los primeros 25 con su centro actual y los avisos. Hasta que no pulses **Aplicar** no se cambia nada.
6. **Aplicar:** todo el lote se hace en **una sola transacción** (o se aplica completo o no se aplica nada) y la lista se refresca.

## Deshacer

**«Deshacer mi último lote»** (en el paso 1) devuelve el centro de costo anterior a los documentos y partidas de tu último lote. Se puede repetir para ir retrocediendo lote por lote.
Solo restaura lo que **sigue valiendo lo que puso ese lote**: si alguien cambió después un documento, no se le pisa.

La bitácora vive en la tabla `zzBrosCentroCostoLog` de tu empresa (lote, fecha, usuario, documento, partida, centro anterior y nuevo).

## Qué escribe y qué no

| Escribe | No toca |
|---|---|
| `docDocument.CostCenterID` (encabezado) | Documentos **cancelados** o **eliminados** |
| `docDocumentItem.CostCenterID` (partidas), si lo pides | Documentos que **otro usuario tiene abiertos** (se omiten y se avisa cuáles) |
| `zzBrosCentroCostoLog` (bitácora) | **Pólizas ya generadas**: cada póliza guarda su propio centro de costo (`accPolizaTransaccion.CostCenterID`) y no se modifica |
| | Cobros y pagos (`docFinancialOperation`) |

> **Sobre las pólizas.** El centro de costo del documento se lee cuando se **genera** la póliza. Si cambias el centro de costo de un documento que ya tiene póliza, la póliza conserva el anterior.
> La plantilla lo avisa en la vista previa y no la reescribe, porque una póliza ya enviada a Contabilidad no debe cambiar a escondidas.

## Por qué el encabezado por defecto

Medido en dos empresas reales: el centro de costo del **encabezado** lo traen decenas de miles de documentos de casi todos los módulos (órdenes de compra, recepciones, facturas, gastos, salidas…); el de **partida**
solo aparece en muy pocos documentos de un solo módulo. Por eso «Encabezado» es la opción inicial y «Partidas» queda para quien la usa.

## Límites del lote

- Hasta **5,000 documentos** por lote; si hay más, se aplica ese lote y se avisa cuántos quedaron fuera para repetir.
- A partir de **500** documentos la vista previa pide revisar con cuidado los criterios.

## Reglas de BrosLMV que cumple

- **Empresa activa:** el catálogo de centros, los módulos y los documentos salen de `OwnedBusinessEntityID`; nada va escrito a mano.
- **Transacción y bitácora:** nada queda a medias y todo se puede deshacer.
- **Conexión propia** (`ctx.OpenConn`) para escribir por lotes; `RefreshGrid` una sola vez al final.
- **Probada:** `build/humo/casos/36_asignar_centro_costo.ps1` corre la plantilla real contra el laboratorio (`BROSLMV_DESARROLLO`) (vista previa, solo-vacíos, repetir, reemplazar, documento en uso, deshacer en orden inverso, partidas) y lo deja todo como estaba.

## Para desarrolladores

Con la variable de entorno `BROSLMV_CC_TEST` (JSON con `accion` = `previsualizar` | `aplicar` | `deshacer` y los mismos parámetros del formulario) el script no abre ventanas y escribe el resultado en el archivo de
`BROSLMV_CC_OUT`. Así corre en `BrosLMV.Runner` (lleva `// job: safe-offline`).
