# Crear documentos desde XML

Plantilla C# `instalador/scripts/CREAR_DOC_DESDE_XML.ctx` (AppKey recomendado `CREAR_DOC_DESDE_XML`). Convierte los CFDI **recibidos** que Comercial ya cargó
en su staging (`docDocumentCFDiSAT` / `docDocumentItemCFDiSAT`) en **Facturas de Compra**, **Gastos** y los demás módulos que reciben XML. Se abre con los XML
seleccionados en la lista de Comercial. Es la **única plantilla de la versión 2.94.0**: las anteriores se archivaron y se irán rehaciendo.

> **Versión mínima:** BrosLMV 2.94.0. Diseño de referencia: `docs/DISENO_CREAR_DOC_DESDE_XML.md`. Pendiente antes de operar en producción: prueba en sandbox
> de cada camino (ver §7) y sustituir las altas por SQL cuando exista método COM de alta.

## 1. Objetivo: ágil

Con solo pulsar **Crear documentos** se crean todos los marcados, sin pasar por ventanas intermedias:

| Situación | Qué hace por omisión | Otras opciones |
|---|---|---|
| Proveedor que no existe | Lo da de alta solo (una vez por RFC) | «No crear» (queda pendiente) o vincular uno existente |
| Producto que no existe | Crea el renglón **sin producto** (descriptivo, como hace Comercial con los gastos) | «Crear el producto» automáticamente, o «Exigir producto» |
| Tipo de gasto | Sugiere el ya usado con ese proveedor y concepto | Tipo de gasto por omisión, o elegirlo a mano |
| Impuesto | Sale del XML y se empareja con los tipos de impuesto que **ya existen** | Si no hay coincidencia, el documento **no se crea** (§3) |

### Siempre la empresa activa (Owned)

Todo se filtra por la empresa activa (`ctx.erp.OwnedBusinessEntityId`). Varias tablas guardan **una copia por
empresa** en `OwnedBusinessEntityID`: los tipos de gasto (`engRefExpense`: `-1` es la plantilla de fábrica y `1`, `2`…
cada empresa; por eso aparecían tres veces), los almacenes, los proveedores (`orgSupplier`), los documentos y los
XML del staging. El importador solo lee y solo escribe los de la empresa activa: catálogos, historial de gastos,
muestras de valores fiscales, consecutivo de segmento de proveedores y memoria de producto/gasto. La persona
(`orgBusinessEntity`) es compartida entre empresas: si el RFC ya existe, se **reutiliza** y solo se agrega su rol de
proveedor de la empresa activa.

### Nada escrito a mano: todo se descubre de la empresa

El script no lleva identificadores de módulo, catálogo ni estatus. Sirve en empresas con configuraciones
distintas porque cada valor se obtiene así:

| Qué | De dónde sale |
|---|---|
| **Módulos destino** | Naturaleza guardada en `engModuleParameter` (sección «Parámetro»): `TableName = docDocument` y `XMLRecibido = 1`. Entran también los módulos copiados o renombrados. |
| Clase del módulo | Su `DocumentTypeID` comparado con la enumeración `DocumentType` de `engRefCombo` (Factura → *compra*, Gasto → *gasto*); cualquier otro → *otro* |
| País, idioma, moneda base | La empresa propia y la moda de sus proveedores |
| Dirección fiscal y clave de RFC | Tipo y nombre usados por la empresa propia |
| Persona moral / física / extranjero | `BusinessEntityType` de `engRefCombo`, elegido por el RFC (12 / 13 caracteres / genérico extranjero) |
| Tipo de comprobante, estatus, exportación, estatus de pago | Muestra de los documentos que Comercial ya creó desde XML; si no hay, del catálogo (`TipoComprobante`, `CFDStatus`, `Exportacion`) |
| Forma/método de pago y uso de CFDI por omisión | El más frecuente en esos documentos; si no hay, el primero del catálogo |
| Carpeta de los XML | Se busca el archivo en: la ruta de un documento que ya la tenga, el parámetro de carpeta de CFDI de Comercial (`CFDDocumentsPath`) y la carpeta de datos de Comercial (padre de su carpeta de respaldos) + `\CFDI\<RFC>\XMLRecibidos`; la primera donde exista el archivo |
| Plazo por omisión | El plazo de contado (parcialidades a 0 días) |
| Impuestos | Los tipos de impuesto de la empresa, emparejados por sus tasas |

Solo quedan como literales los **códigos del SAT** (letra `I` de Ingreso, `XEXX` de extranjero) y los nombres de
las tablas y de los grupos de catálogo del motor. Si en una empresa un catálogo tiene otros nombres y no se
puede descubrir, se fija por empresa en la tabla `zzBrosCfdiConfig (Clave, Valor)` (claves `TipoDocCompra`,
`TipoDocGasto`, `TipoEntidadMoral`, `TipoEntidadFisica`, `TipoEntidadExtranjero`) sin editar el script.

### Qué módulos ofrece como destino

Todo módulo sobre `docDocument` con `XMLRecibido = 1` puede generarse desde un CFDI. Se clasifica así:

| Clase | Cómo se reconoce | Cómo se crea |
|---|---|---|
| **Compra** | su tipo de documento es el de Factura | productos por `AgregarArticulo`, renglones sin producto, saldo abierto |
| **Gasto** | su tipo de documento es el de Gasto | partidas con tipo de gasto |
| **Otro** (sin validar) | recibe XML pero es de otra naturaleza (honorarios, nota de crédito, pagarés…) | solo renglones descriptivos; se marca «sin validar» en la lista y en los avisos |

Los «otros» se ofrecen porque `XMLRecibido = 1` indica que Comercial puede generarlos desde un XML, pero su
comportamiento exacto aún no está validado. Hoy solo se convierten CFDI de Ingreso; los de Egreso y Pago
siguen bloqueados hasta validar su destino.

## 2. Pantalla (pestañas)

1. **Documentos** — una fila por CFDI: destino (Compra/Gasto), almacén, plazo, estado y avisos; filtros,
   «aplicar a marcados» y las tres opciones de arriba. Botón **Partidas** abre el detalle (método/forma
   de pago, uso del CFDI y, por partida, tipo de impuesto, tipo de gasto y producto).
2. **Proveedores** — una fila compacta por RFC con lo esencial: razón social, persona (moral/física/extranjero),
   **régimen fiscal**, **C.P.** (con estado y municipio del catálogo de direcciones de Comercial), colonia, vincular a
   uno existente y «Crear ahora». Nombre comercial, calle, números, teléfono, correo y plazo quedan en «+ datos».
   Régimen y C.P. se leen del XML (`RegimenFiscal` del emisor y `LugarExpedicion`, que es el lugar de expedición y
   no siempre el domicilio fiscal); los renglones en amarillo son los que aún no tienen régimen o C.P. válido.
   Sin tocar nada, se dan de alta con lo que trae el XML.
3. **Partidas** — una fila por proveedor + clave + descripción: producto y tipo de gasto en línea
   (con lista buscable), «Crear producto» y «Crear todos los productos faltantes».
4. **Impuestos** — una fila por composición fiscal del XML (p. ej. `IVA 16% + Ret. ISR 1.25%`) con el
   tipo de impuesto que le corresponde; permite elegir otro para todas las partidas de esa composición.
5. **Resultado** — documentos creados (con «Abrir»), errores, avisos y documentos sin póliza.

Estados: **Listo**, **Listo con avisos** (se creará proveedor, hay renglones sin producto…),
**Pendiente** (solo con «No crear proveedores» o «Exigir producto») y **Bloqueado** (ya creado, no es
Ingreso, o falta tipo de impuesto). Solo se convierten CFDI de **Ingreso**.

## 2b. Afectaciones: las decide el módulo

Cada módulo declara en `engModuleParameter` lo que afecta. El importador lo lee del módulo elegido (los
copiados traen los suyos) y llama a la función nativa que corresponde, en el orden canónico
`RecalcCompleto → AffectStockNEW → CalcularCostos → Save → UpdateDocumentPaidInfo → UpdateStatusDelivery`:

| Parámetro del módulo | Si es distinto de 0 |
|---|---|
| `StockAffectation` (inventario) | `AffectStockNEW` mueve el kardex (solo partidas con producto); el documento cuenta como entrega (`DateDelivery`/`DateDocDelivery` = fecha del documento). Las partidas sin producto **no** mueven inventario y se avisa. Con «Crear el producto» se crean para poder moverlo |
| `CostAffectation` / `CostAffectationComercial` | `CalcularCostos` (costo fiscal / comercial) |
| `FinancialAffectation` (saldo / banco) | agenda de pagos por el plazo real + `UpdateDocumentPaidInfo` (saldo por pagar/cobrar y estatus de pago) |
| ninguno | sin entrega (`DateDocDelivery = NULL`) |

`UpdateStatusDelivery` se llama siempre (`RecalcCompleto` no lo calcula). El importador **no** registra pagos
ni movimientos de banco: como Comercial al generar desde XML, deja la cuenta por pagar abierta.

## 3. Impuestos: los rige el documento

- Por partida se calculan las tasas reales del XML sobre su base (`importe − descuento`): IVA, IEPS,
  retención de IVA y retención de ISR, con tolerancia de centavos.
- Se busca un tipo de impuesto (`engTaxType`, tasas en `vwLBSTaxPerc`) que coincida **exacto** en las
  cuatro tasas. Con tasa cero: «IVA N/A» si el objeto de impuesto es `01`, «IVA 0%» si es `02`;
  «IVA Exento» solo si el usuario lo elige (el staging no distingue exento de 0%).
- **Si no hay coincidencia** el documento queda bloqueado con la sugerencia
  «dar de alta un tipo de impuesto *IVA 16% + Ret. IVA 4% + Ret. ISR 1.25%*»; tras darlo de alta en
  Comercial se pulsa **Recargar catálogos**. Nunca se usa un tipo «parecido» ni un identificador fijo.
- El tipo de gasto y el producto **no** intervienen en el impuesto: cambiarlos no lo cambia.
- Tras `Save` se compara el total de Comercial con el del XML; solo si difiere más de 2 centavos se
  ajusta por SQL (retenciones y totales) y se avisa en el Resultado.

## 4. Memoria de producto y tipo de gasto

Tablas propias por empresa: `zzBrosCfdiProductoMapa` y `zzBrosCfdiGastoMapa` (`OwnedBusinessEntityID`,
`RFCEmisor`, `ClaveProveedor`, `DescripcionNormalizada`, id, y en la de gasto `ClaveProdServ`).
Sugerencias, de más a menos específica (se ven con una etiqueta en la pestaña Partidas):

- Producto: exacto (RFC + clave + descripción) → *recordado*; misma clave del proveedor → *sugerido*.
- Tipo de gasto: exacto → *recordado*; misma clave SAT del proveedor → *sugerido*; historial de gastos
  del proveedor en Comercial con la misma descripción → *historial*; único tipo que ese proveedor ha
  usado → *sugerido*; tipo de gasto por omisión.
- Al crear cada documento se **aprende** el producto y el tipo de gasto usados (el impuesto no se aprende).

## 5. Qué escribe

- **Documento**: `ctx.erp.NuevoDocumento`, `AgregarArticulo` (partidas con producto), `RecalcCompleto` y
  `Save`. Las partidas sin producto son un `INSERT` en `docDocumentItem` (`ProductID=0`,
  `MustBeDelivered=0`), igual que las de un gasto. Con descuento se captura el **precio neto**.
- **Cabecera fiscal** en `docDocumentCFD` (no en `docDocument`): UUID, fechas, serie/folio, RFC,
  método/forma de pago, uso de CFDI, ruta del XML ya procesado.
- **Agenda de pagos** (`docDocumentPaymentAgenda`) desde `engPaymentTermDetail` sobre la **fecha de
  emisión** del XML (`NuevoDocumento` la deja en la fecha de hoy); `DateDocDelivery = NULL` en compras.
- **Alta de proveedor** (última opción mientras no haya método COM): entidad (persona moral/física/extranjero, régimen
  fiscal), dirección fiscal completa (`orgAddressDetail`, con estado/municipio/colonia y códigos del catálogo por el
  C.P.), clave de identificación (RFC), información principal (con la dirección desnormalizada), rol de proveedor,
  canales de teléfono y correo si se capturan, y los indicadores de importación de XML. Es el conjunto de tablas de
  la alta de Comercial, sin CRM ni código consecutivo de empresa. Es idempotente: si el RFC ya existe, lo reutiliza.
- **Alta de producto**: fila de `orgProduct` (tipo 1, costo, impuesto, claves SAT), la unidad en
  `engRefCombo` si es nueva y el vínculo con el proveedor (`orgProductSupplier`).
- Las altas y la creación de cada documento van en pocas sentencias por lote; el avance se muestra en
  pantalla.

## 6. Límites conocidos

- Los complementos del CFDI (Carta Porte, combustible, impuestos locales, pagos) no se guardan; solo
  queda el archivo. Se propone tabla propia en el ROADMAP 1.8.
- No consulta el estatus del CFDI ante el SAT. Si el XML ya se importó (`DocumentID` en el staging), queda
  bloqueado.
- **Póliza:** el guardado por script no la genera; el importador se la pide al motor nativo
  (`Accounting.clsMain.CrearPolizasDocumento`) cuando el módulo tiene `AccountingPoliza` activo (parámetro de la sección `Contabilidad`) y avisa si el
  documento quedó sin póliza. **No** la sincroniza con Contabilidad.
- **Grid de Comercial:** se refresca con `XEngine.RefreshGrid(janusGrid)` (`ctx.erp.Get("janusGrid")` + `ctx.erp.Call`), que recarga todo y
  deja la vista al principio; por eso antes se lee la fila actual del grid (Janus GridEX, propiedad `Row`) y después se restaura (`Row` +
  `EnsureVisible`). `MustRefreshGrid` sola no refresca y `RefreshGrid()` sin parámetros falla con `DISP_E_PARAMNOTOPTIONAL`. El grid ya
  muestra el documento creado, así que **si todo salió bien la ventana se cierra sola**; si hubo errores o algún documento quedó sin
  póliza, se queda en la pestaña Resultado. Los avisos informativos (p. ej. redondeo de centavos) van al log de scripts. El perfil
  temporal de WebView2 se borra en segundo plano para no congelar Comercial al cerrar.
