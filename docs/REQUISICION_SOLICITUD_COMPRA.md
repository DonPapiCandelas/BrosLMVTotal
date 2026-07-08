# Solicitud de compra - documentacion tecnica

> Script auditado: `C:\BrosLMV\scripts\REQUISICION.ctx`
> Plantilla comunitaria versionada: `instalador\scripts\PLANTILLA_REQUISICION_COMPRA.ctx`
> Fecha del script: 2026-06-29 20:22
> Objetivo: crear una solicitud de compra de CONTPAQi Comercial Pro igual, en estructura y comportamiento, a una creada dentro del sistema.

Este documento consolida el aprendizaje obtenido investigando el comportamiento real de CONTPAQi
contra una base de datos de pruebas, para que el script de requisicion pueda mantenerse desde el
repo principal sin depender de notas locales.

La plantilla comunitaria `PLANTILLA_REQUISICION_COMPRA.ctx` es la version didactica y funcional:
abre una ventana WinForms, permite elegir proveedor, almacen y productos, y crea una solicitud de
compra real. La UI recolecta datos; la seccion **CREAR SOLICITUD** contiene el flujo ERP que
debe estudiarse y mantenerse.

## Visibilidad en la consola

Hallazgo operativo: la seccion **Plantillas** de la consola no enumera automaticamente todos los
archivos sueltos de `C:\BrosLMV\scripts`. Esa seccion se construye desde el arreglo interno
`PLANTILLAS` en `src\Consola.cs`.

Para esta primera plantilla se hizo el ajuste correcto:

1. El archivo versionado vive en `instalador\scripts\PLANTILLA_REQUISICION_COMPRA.ctx`.
2. El archivo runtime vive en `C:\BrosLMV\scripts\PLANTILLA_REQUISICION_COMPRA.ctx`.
3. `src\Consola.cs` agrega la entrada **Documento - Requisicion de compra** en `PLANTILLAS`.
4. Esa entrada no hardcodea todo el script en la DLL; llama `CargarPlantillaArchivo(...)` y lee el
   contenido desde `C:\BrosLMV\scripts`.

Implicacion: despues de agregar o cambiar una entrada de `PLANTILLAS`, hay que recompilar y
desplegar la DLL. Ademas, la ventana de consola ya abierta no refresca esa lista: hay que cerrarla
y abrirla de nuevo desde CONTPAQi.

## Comportamiento de la plantilla

La plantilla visible en consola no es un ejemplo minimo con valores hardcodeados. Es una ventana
real para usuarios:

1. Carga proveedores desde `orgBusinessEntity` + `orgSupplier`.
2. Carga almacenes desde `orgDepot`.
3. Permite buscar productos por clave o descripcion en `orgProduct`.
4. Permite capturar cantidad y agregar/quitar partidas.
5. Al confirmar, crea el documento con `ctx.erp.NuevoDocumento(1040, dep, be)`.
6. Aplica el perfil especifico de solicitud de compra con un `UPDATE docDocument`.
7. Agrega partidas con `ctx.erp.AgregarArticulo`.
8. Asegura `orgProductSupplier`.
9. Recalcula y guarda con `ctx.erp.RecalcCompleto` + `ctx.erp.Save`.

No llama `ctx.erp.AffectStockNEW` porque la solicitud de compra no afecta inventario.

## Diseno UX aplicado a la plantilla

La ventana no debe parecer una factura, compra o documento financiero. Una solicitud de compra
solo captura necesidad operativa; por eso la UI muestra datos de proveedor, almacen, producto,
cantidad solicitada y existencia.

Textos visibles definidos:

| Elemento | Texto |
|---|---|
| Titulo de ventana | `Nueva requisicion de compra` |
| Encabezado | `Requisicion de compra` |
| Subtitulo | `Mod. 1040 - Solicitud de compra - sin inventario` |
| Accion principal | `Crear requisicion` |

Campos visibles:

| Campo | Motivo |
|---|---|
| Proveedor | Es parte del documento de compra y permite asegurar `orgProductSupplier`. |
| Almacen | Define el `DepotID` del documento y la existencia consultada. |
| Fecha informativa | Se muestra como automatica porque `NuevoDocumento` usa la fecha del sistema. |
| Solicitante | Se toma de `ctx.erp.UserName` cuando esta disponible; es contexto visual. |
| Prioridad | Se muestra como `Normal` para orientar la captura; no se persiste en esta version. |
| Centro de costo | Se muestra como `Sin centro de costo`; no se persiste hasta validar el campo nativo correcto. |
| Observaciones | Campo visual preparado para UX; no se escribe al documento hasta definir contrato. |
| Producto | Permite buscar por clave o descripcion. |
| Cantidad | Captura la cantidad solicitada por partida. |
| Existencia | Ayuda al usuario a decidir sin afectar inventario. |

Campos omitidos de la UI:

| Campo | Motivo |
|---|---|
| Precio | La solicitud no captura importes. |
| Subtotal | No corresponde a este flujo operativo. |
| IVA / impuesto | Puede existir internamente por producto, pero no debe mostrarse como decision del usuario. |
| Total monetario | El documento no debe verse como compra facturable. |
| Descuentos / moneda / forma de pago | No forman parte de la captura de una solicitud. |

La busqueda de productos usa una lista dibujada por la plantilla: clave, descripcion, unidad,
existencia y tipo. Se retiro el formato monoespaciado con separadores porque era dificil de leer
como resultado de busqueda.

La tabla de partidas muestra seleccion, clave, descripcion, unidad, cantidad solicitada,
existencia y estatus operativo `Pendiente`. Tambien tiene una barra de trabajo con busqueda por
clave/descripcion, filtro, punto de extension para importacion y contador de partidas. El estatus
es visual; la creacion del documento sigue dependiendo de `ctx.erp.AgregarArticulo`.

## Plantilla Python

Tambien existe `PLANTILLA_REQUISICION_COMPRA_PY.py`. Abre un formulario WinForms declarativo
desde Python con `ctx.form` y documenta el flujo tecnico:

1. Lee proveedor, almacen y productos con `ctx.query`.
2. Valida que las busquedas sean unicas.
3. Crea el documento con `ctx.erp.NuevoDocumento(1040, depot, proveedorBE)`.
4. Aplica el perfil del modulo 1040 con `ctx.execute`.
5. Agrega partidas con `ctx.erp.AgregarArticulo`.
6. Asegura `orgProductSupplier`.
7. Recalcula, guarda y refresca con `ctx.erp.RecalcCompleto`, `ctx.erp.Save` y
   `ctx.erp.RefreshGrid`.

La plantilla Python inicia con `MODO_INTERACTIVO = True`. En ese modo abre ventana y captura
proveedor, almacen, producto y cantidad. Para evitar creaciones accidentales, solo crea el
documento si el usuario marca `Crear documento al aceptar`.

Estado de UI Python: ya existe un primer `ctx.form` renderizado por el addon en WinForms para
campos simples (`text`, `number`, `decimal`, `date`, `bool`, `combo`, `memo`). La plantilla Python
lo usa para capturar proveedor, almacen, producto y cantidad. Aun falta extender ese renderizador
a grid editable avanzado para igualar por completo la experiencia visual de la plantilla C#.

## Placeholders de plantillas

No usar placeholders que puedan coincidir con datos reales, por ejemplo `"ALMACEN"` o
`"PROVEEDOR"`. En la empresa de prueba, `"ALMACEN"` coincide con varios almacenes y provoca:

```text
El texto de almacen no es unico. Usa un nombre mas especifico: ALMACEN
```

Regla para plantillas comunitarias:

1. Dejar campos obligatorios en cadena vacia (`""`).
2. Validar al inicio con `ctx.Msg(...)` y `return`, no con `throw`, para evitar stack trace en
   errores normales de configuracion.
3. Poner ejemplos en comentarios, no como valores ejecutables.
4. Si una busqueda encuentra mas de una fila, no elegir automaticamente; pedir un texto mas
   especifico o un RFC/clave exacta.

## Veredicto corto

El script actual NO esta hecho principalmente con SQL duro. La creacion del documento usa el
estandar correcto del addon:

1. `ctx.erp.NuevoDocumento(1040, dep, be)`
2. `ctx.erp.AgregarArticulo(doc, producto, cantidad)`
3. `ctx.erp.RecalcCompleto(doc)`
4. `ctx.erp.Save(doc)`
5. `ctx.erp.RefreshGrid()`

El SQL directo que queda se usa para dos huecos que todavia no estan encapsulados como funcion
de alto nivel:

1. Perfil especifico del modulo 1040 en `docDocument`.
2. Relacion auxiliar `orgProductSupplier` entre producto y proveedor.

Eso es consistente con el estandar actual del laboratorio: usar `ctx.erp` para lo nativo y usar
SQL solo para los campos/reglas que el addon todavia no abstrae.

## Donde esta cada cosa en el script

| Area | Lineas aproximadas | Que hace | Tipo |
|---|---:|---|---|
| Carga de almacenes/proveedores | 39-57 | Lee `orgDepot`, `orgSupplier`, `orgBusinessEntity` para llenar combos | SQL lectura |
| Busqueda de productos | 433-455 | Busca productos con impuesto y existencia por almacen | SQL lectura |
| Obtiene `SupplierID` | 704-705 | Convierte `BusinessEntityID` del proveedor a `SupplierID` | SQL lectura |
| Resetea estado transaccional ADO | 719 | `SET XACT_ABORT OFF; SET IMPLICIT_TRANSACTIONS OFF` | SQL tecnico |
| Crea cabecera + anclas | 721 | `ctx.erp.NuevoDocumento(1040, dep, be)` | SDK / `ctx.erp` |
| Aplica perfil modulo 1040 | 725-728 | `DepotIDFrom=0`, `UserID=0`, `PaymentTermID=0`, etc. | SQL directo controlado |
| Crea partidas | 730-734 | `ctx.erp.AgregarArticulo(...)` por cada producto | SDK / `ctx.erp` |
| Asegura producto-proveedor | 736-743 | Inserta en `orgProductSupplier` si falta | SQL directo controlado |
| Recalcula totales/impuestos | 745-747 | `ctx.erp.RecalcCompleto(doc)` | SDK / `ctx.erp` |
| Guarda con mecanismo nativo | 749-751 | `ctx.erp.Save(doc)` | SDK / `ctx.erp` |
| Refresca Comercial | 753 | `ctx.erp.RefreshGrid()` | SDK / `ctx.erp` |

## Flujo canonico para solicitud de compra

La solicitud de compra es el modulo `1040`. En el laboratorio quedo validado que NO afecta
inventario, por lo tanto no debe llamar `AffectStockNEW`.

```csharp
int doc = ctx.erp.NuevoDocumento(1040, depotId, proveedorBusinessEntityId);

ctx.NonQuery(
    "UPDATE docDocument SET DepotIDFrom=0, UserID=0, " +
    "CampaignID=NULL, CostCenterID=NULL, ProjectID=NULL, PaymentTermID=0 " +
    "WHERE DocumentID=" + doc);

foreach (var item in partidas)
    ctx.erp.AgregarArticulo(doc, item.ProductID, item.Cantidad);

foreach (var item in partidas)
    ctx.NonQuery("IF NOT EXISTS (...) INSERT INTO orgProductSupplier (...)");

ctx.erp.RecalcCompleto(doc);
ctx.erp.Save(doc);
ctx.erp.RefreshGrid();
```

## Que ya hace el addon

Desde v2.18.0, `ctx.erp.NuevoDocumento` ya crea lo que antes se intentaba clonar o insertar a
mano:

| Tabla / campo | Responsable actual |
|---|---|
| `docDocument` base | `NuevoDocumento` |
| `docDocumentExt` | `NuevoDocumento` |
| `docDocumentExtra` | `NuevoDocumento` |
| `docDocumentCFD` | `NuevoDocumento` |
| `docDocumentPaymentAgenda` | `NuevoDocumento` |
| `MustBeSynchronized=1` | `NuevoDocumento` |
| `ExportID=1` | `NuevoDocumento` |
| `DateCost`, `DateDocDelivery`, `DateFrom`, `DateTo`, `DateLastPayment` | `NuevoDocumento` |
| `docDocumentItem` base | `AgregarArticulo` |
| `ApplyGlobalDiscount`, `DeductiblePerc`, `IsBusinessOperation`, `MustBeDelivered` | `AgregarArticulo` |
| `DateItem`, `CoefUnit`, `ClaveUnidad`, `ObjetoImpuesto` | `AgregarArticulo` |

Regla importante: el script de requisicion no debe volver a crear anclas ni repetir esos campos
universales. Hacerlo puede generar duplicados o diferencias contra el documento nativo.

## Por que queda SQL directo

### Perfil del modulo 1040

El modulo de solicitud de compra no comparte exactamente el mismo perfil que entrada/salida de
almacen. El laboratorio valido este perfil para `docDocument`:

| Campo | Valor esperado |
|---|---|
| `ModuleID` | `1040` |
| `DocumentTypeID` | `49` |
| `DocRecipientID` | `2` proveedor |
| `DepotIDFrom` | `0` |
| `UserID` | `0` |
| `CampaignID` | `NULL` |
| `CostCenterID` | `NULL` |
| `ProjectID` | `NULL` |
| `PaymentTermID` | `0` |

Hoy esos campos se aplican con un `UPDATE docDocument`. Es aceptable mientras no exista una
funcion tipo `ctx.erp.SetPerfilModulo(doc, 1040)`.

### `orgProductSupplier`

CONTPAQi guarda o espera la relacion entre producto y proveedor para documentos de compra. El
script la asegura asi:

```sql
IF NOT EXISTS (
    SELECT 1
    FROM orgProductSupplier
    WHERE ProductID = <producto> AND SupplierID = <proveedor>
)
INSERT INTO orgProductSupplier
    (ProductID, SupplierID, CostPrice, CurrencyID, RefSupplier, OrderNumber)
VALUES
    (<producto>, <proveedor>, 0, 3, NULL, 0)
```

Esto tambien es SQL directo controlado. El siguiente paso natural seria envolverlo en
`ctx.erp.AsignarProveedor(productId, supplierId, costPrice)`.

## Que NO debe hacer una solicitud de compra

Una solicitud de compra no mueve inventario ni costea existencias. Por eso el script actual
correctamente NO llama:

```csharp
ctx.erp.AffectStockNEW(doc);
```

Tampoco debe insertar manualmente kardex ni costos:

| Tabla | Esperado en solicitud |
|---|---|
| `orgProductKardex` | 0 filas |
| `orgProductCostComercial` | 0 filas |
| `orgProductCostFiscal` | 0 filas |

## Evidencia de validación

Resultado documentado del experimento principal (documento creado a mano en Comercial vs.
documento replicado por el script, comparados campo por campo):

- Estado: `VALIDADO_CONSOLA`.
- Comparacion campo por campo entre documento manual y replica.
- Sin diferencias estructurales en `docDocument` despues de corregir perfil.
- Partidas equivalentes despues de corregir el sub-llenado de `AgregarArticulo`.
- 4 anclas presentes.
- `orgProductSupplier` presente.
- Sin afectacion de inventario.

Lecciones aplicadas al script actual:

1. `DepotIDFrom=0` para solicitud, no `DepotIDFrom=DepotID`.
2. `PaymentTermID=0`.
3. `CampaignID`, `CostCenterID` y `ProjectID` en `NULL`.
4. `UserID=0`.
5. No `AffectStockNEW`.
6. Revisar `ctx.erp.LastError` despues de cada llamada critica.

## Riesgos actuales del script

### 1. No hay transaccion externa de todo el documento

`NuevoDocumento` y `AgregarArticulo` son atomicos internamente, pero el flujo completo no esta
envuelto en una sola transaccion externa. Si falla despues de crear cabecera y algunas partidas,
puede quedar un documento parcial.

Esto no se resuelve facilmente desde el script porque la conexion ADO viva de CONTPAQi no mantiene
`@@TRANCOUNT` entre llamadas separadas. La solucion correcta es encapsular mas pasos en funciones
del addon o crear un builder de documento que ejecute el flujo completo con control interno.

### 2. SQL concatenado

El script concatena IDs y cantidades ya controlados por la UI. Es riesgo bajo, pero el estandar
ideal para nuevas partes con texto libre es `ctx.OpenConn()` con parametros o una funcion del addon.

### 3. Fecha informativa, no editable

La UI ya no muestra un `DateTimePicker` editable. Muestra la fecha del dia como dato
informativo porque el flujo actual usa la fecha que pone `NuevoDocumento` internamente
(`GETDATE()`). Si mas adelante la fecha debe capturarse manualmente, falta una decision:

- agregar API `ctx.erp.SetFechaDocumento(doc, fecha)`, o
- aplicar un `UPDATE docDocument` controlado con `DateDocument`, `DateCost`, `DateDocDelivery`,
  `DateFrom`, `DateTo`, `DateLastPayment`.

### 4. Falta una matriz guardada para este script exacto

El laboratorio valido la receta base de solicitud, pero este archivo `REQUISICION.ctx` tiene UI y
seleccion dinamica. Para declararlo "identico al sistema" en su version actual debe existir una
validacion nueva contra un documento manual equivalente.

## Checklist para declarar una version como valida

1. Crear una solicitud manual en Comercial con el mismo proveedor, almacen y productos.
2. Crear una solicitud con `REQUISICION.ctx` usando los mismos datos.
3. Ejecutar comparacion campo por campo:

```powershell
.\tools\powershell\Compare-Documento.ps1 -DocA <manual> -DocB <replica>
```

4. Comparar tambien tablas satelite:

| Tabla | Que validar |
|---|---|
| `docDocument` | Sin diferencias estructurales |
| `docDocumentItem` | Cantidad, unidad, impuesto, flags, fechas, SAT |
| `docDocumentExt` | 1 fila |
| `docDocumentExtra` | 1 fila |
| `docDocumentCFD` | 1 fila |
| `docDocumentPaymentAgenda` | 1 fila |
| `docDocumentTax` | totales correctos |
| `docDocumentTaxDetail` | detalle por partida |
| `docDocumentTaxSum` | resumen correcto |
| `orgProductSupplier` | relacion producto-proveedor existente |
| `orgProductKardex` | sin filas nuevas |

5. Abrir la solicitud creada desde Comercial.
6. Confirmar que no exige reparacion, no queda en estado raro y se visualiza igual que la manual.
7. Guardar la matriz de equivalencia en `docs/` o en un experimento versionado.

## Estandar para futuras modificaciones

Al tocar `REQUISICION.ctx`, respetar esta prioridad:

1. Primero usar `ctx.erp.*` si existe funcion.
2. Usar `ctx.NonQuery` solo para perfil por modulo o tablas no cubiertas por `ctx.erp`.
3. No insertar manualmente anclas de documento.
4. No actualizar campos que `NuevoDocumento` y `AgregarArticulo` ya llenan correctamente.
5. No llamar `AffectStockNEW` en solicitud de compra.
6. Revisar `ctx.erp.LastError` despues de cada llamada critica.
7. Si se agrega texto libre a SQL, parametrizar.
8. Documentar cada diferencia contra el nativo como `ESPERADA`, `ACEPTABLE`, `SOSPECHOSA` o
   `INCORRECTA`.

## Funciones de addon recomendadas

Para reducir SQL directo en scripts de negocio, conviene agregar al addon:

```csharp
ctx.erp.SetPerfilModulo(documentId, moduleId);
ctx.erp.AsignarProveedor(productId, supplierId, costPrice = 0);
ctx.erp.CrearSolicitudCompra(proveedorBE, depotId, partidas);
```

La ultima funcion permitiria que el script UI solo recolecte datos y delegue la fidelidad al
addon, que es mas facil de probar y versionar.

## Estado actual

| Criterio | Estado |
|---|---|
| Usa `ctx.erp` para crear cabecera | Si |
| Usa `ctx.erp` para partidas | Si |
| Usa `ctx.erp` para recalculo | Si |
| Usa `ctx.erp` para guardado | Si |
| Evita `AffectStockNEW` | Si |
| Evita crear anclas a mano | Si |
| SQL directo reducido a huecos conocidos | Si |
| Identidad campo por campo de este archivo exacto | Pendiente de nueva matriz |

Conclusion: el script actual sigue mayormente el estandar correcto. No es una reconstruccion
manual completa por SQL. La deuda principal es encapsular los dos SQL restantes y validar esta
version exacta con una matriz de equivalencia guardada.
