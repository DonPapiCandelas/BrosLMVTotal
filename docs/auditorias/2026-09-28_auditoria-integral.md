# Auditoría integral del repositorio — 2026-09-28

> **Estado:** en curso. Este reporte se actualiza durante la revisión: cada hallazgo
> contiene evidencia reproducible y se mantiene separado de cualquier cambio funcional.
> No incluye datos de clientes, credenciales ni evidencia privada.

## Alcance y método

- **Alcance solicitado:** todo el árbol de trabajo, incluyendo núcleo, ejecutables,
  instaladores, pruebas, documentación y material local de investigación.
- **Inventario inicial:** 12,208 archivos, 4.54 GB (lectura de metadatos el 2026-09-28).
  Hay 277 archivos Markdown (5.99 MB). Los binarios, cachés y trazas se revisan por
  procedencia, hash, manifiesto y relación con su fuente; no se interpretan como texto.
- **Regla de seguridad:** esta auditoría no ejecuta scripts contra bases de datos, no instala,
  no empaqueta y no publica. Las áreas privadas se usan únicamente como evidencia local y no
  se nombran ni se copian a este reporte público.
- **Orden de revisión:** arquitectura y fuentes de verdad; API y seguridad; plantillas y
  persistencia; instaladores y build; pruebas/CI; coherencia documental; artefactos.

## Mapa confirmado del producto

| Componente | Fuente de verdad revisada | Estado observado |
|---|---|---|
| Addon | `src/`, `docs/ESTADO.md`, `README.md` | DLL .NET Framework 4.8 / COM dentro de Comercial; Consola y scripts por empresa. |
| Runner | `runner/`, `README.md` | Ejecuta el mismo motor de scripting sin la UI principal. |
| Canal Python | `host/`, `workers/`, `protocol/`, `docs/ARQUITECTURA_V3.md` | Host .NET 8 x64 y Named Pipe con contrato Protobuf. |
| PDF y correo | `htmlpdf/`, documentación de formato | Componente separado .NET 8 / WebView2. |
| Descargas | `descargas/`, UI, servicio e instaladores propios | Producto .NET 8 independiente del addon, con base de datos propia. |
| Instalación | `instalador/`, `instaladores/`, `build/` | El paquete del addon se genera desde insumos y ejecutables; compilar no actualiza por sí solo una instalación. |

## Hallazgos

### I-01 — Una plantilla no es una acción de empresa

Una plantilla solo carga código en el editor: no crea una fila de `zzBrosScript`, por lo que no
aparece en el árbol de la empresa. El importador se registra con `BrosGuardar`, no con SQL
manual, para preservar SHA-256 e historial.

### I-02 — Gasto y producto son rutas distintas

`AgregarArticulo` toma atributos de `orgProduct`; por ello no debe obligarse a usarlo para un
gasto descriptivo. El importador admite la partida `ProductID=0` de Gasto, preserva descripción,
unidad, IVA y tipo de gasto, y delega los totales a `RecalcCompleto`/`Save`. Retenciones e
impuestos no validados se bloquean, no se degradan silenciosamente.

### I-03 — Registro de la acción en una empresa provisionada

La acción `IMPORTAR_DOCUMENTOS_XML` se registró por el mismo contrato de `BrosGuardar`:
respaldo previo en `zzBrosScriptHist`, `NVARCHAR(MAX)` parametrizado, hash SHA-256 UTF-8 y
categoría `CFDI`. La comprobación posterior confirmó fila activa y hash coincidente. La consola
ya abierta mantiene su árbol en memoria; debe recargarlo antes de mostrar una acción recién
registrada.

### W-02 — Prototipo de importación publicado antes de cumplir el diseño

- **Estado:** CONFIRMADO. No apto para operación.
- **Evidencia:** la primera interfaz muestra controles globales en una pestaña final, repite
  proveedores y conceptos, usa una vista previa de cuadro de mensaje y permite una revisión que
  no corresponde al lote por CFDI definido para el producto.
- **Corrección en curso:** reemplazo completo separado (`CREAR_DOC_DESDE_XML_V2_CSHARP.ctx`),
  aún no registrado en la empresa. La acción existente no se considera entrega ni evidencia de
  aceptación.
- **Regla de publicación:** no sustituir la acción de la empresa hasta contar con revisión por
  documento, agrupación, filtros fiscales, vista previa imprimible, alta/selección de catálogo y
  prueba en sandbox.

### D-01 — El estado del protocolo no está sincronizado con el host vigente

- **Estado:** CONFIRMADO.
- **Severidad:** Media.
- **Área:** D — coherencia documental.
- **Evidencia:** `protocol/README.md`, sección “Estado”, describe que el host y el código
  generado todavía están pendientes; `host/README.md`, secciones “Arquitectura actual” y
  “Estado y siguiente paso”, documenta C3–C6 como implementados y en producción; el proyecto
  `host/BrosLMV.Host/BrosLMV.Host.csproj` referencia `Google.Protobuf` y `Grpc.Tools`.
- **Qué pasa:** quien use exclusivamente el documento del protocolo puede concluir que el canal
  Python aún no existe o que debe implementarse desde cero.
- **Propuesta:** actualizar `protocol/README.md` para reflejar el estado real y aclarar que el
  código generado se produce durante la compilación y no se versiona.
- **Cómo verificar el arreglo:** contrastar el texto actualizado con el `csproj` del host y con
  `host/README.md`; compilar el host en una tarea posterior, sin involucrar una base de datos.

### W-01 — El cambio local del importador no cumple el diseño vigente de la capacidad

- **Estado:** CONFIRMADO.
- **Severidad:** Alta antes de cualquier despliegue; no se ha desplegado.
- **Área:** C — plantillas y persistencia.
- **Evidencia:** el cambio sin confirmar en el árbol de trabajo agrega una plantilla de Consola
  y sube la versión del addon; el diseño vigente de importación exige un mapeo persistente de
  producto por empresa, resolución en niveles, revisión agrupada y creación mediante el motor
  nativo. El cambio actual no implementa esas piezas ni registra una acción de empresa.
- **Qué pasa:** una plantilla en disco solo aparece como opción para crear código; no equivale a
  una acción guardada en `zzBrosScript` ni satisface el flujo masivo requerido.
- **Propuesta:** no instalar, publicar ni registrar este cambio. Replantear la capacidad como
  una acción persistida por empresa, con sus tablas propias y validaciones, después de cerrar
  esta auditoría y contrastar cada paso con las fuentes técnicas.
- **Cómo verificar el arreglo:** prueba aislada con documentos de prueba: comprobar que la
  acción aparece en la Consola, conserva el mapeo aprobado y no crea documentos cuando hay
  datos sin resolver.

### P-01 — La auditoría detectó documentación con versiones históricas que requiere clasificación

- **Estado:** CONFIRMADO como condición; pendiente clasificar cada documento.
- **Severidad:** Media.
- **Área:** D — coherencia documental.
- **Evidencia:** `docs/ESTADO.md` declara addon 2.93.0 como estado vigente; algunos documentos
  técnicos conservan encabezados de versiones anteriores. El repositorio ya distingue
  documentos archivados de fuentes vigentes, pero falta concluir cuáles encabezados históricos
  siguen siendo meramente informativos y cuáles inducen instrucciones obsoletas.
- **Qué pasa:** el lector no siempre puede distinguir entre una versión de introducción del
  documento y la vigencia de su contenido.
- **Propuesta:** durante la pasada documental, marcar cada caso como “histórico pero válido”,
  “actualizar” o “mover a archivo”; no modificar contenido técnico hasta verificarlo contra
  código y pruebas.
- **Cómo verificar el arreglo:** revisar los enlaces desde `docs/INDICE.md` y confirmar que
  todo documento de ruta de trabajo indique claramente su vigencia.

## Controles comprobados hasta ahora

- `build/verificar_regla_de_oro.ps1` cruza `AssemblyVersion`, CHANGELOG, notas de versión y
  el verificador local de términos prohibidos.
- `build/probar_humo.ps1` enumera los casos de `build/humo/casos/` y corta con error si falla
  uno; requiere un sandbox, por lo que no se ejecutó en esta auditoría de solo lectura.
- `build/publicar_release.ps1` exige árbol limpio, versión documentada, instaladores recientes
  y escaneo de términos prohibidos antes de publicar.
- La documentación de desarrollo exige PR para cambios; no se ha creado ni modificado ningún PR
  como parte de esta auditoría.

## Pendiente de la auditoría

1. Cruzar firmas reales de `ctx`/`ctx.erp` contra `MANUAL.md` y `SCRIPTING_CONTRATOS.md`.
2. Revisar las plantillas de documentos contra los perfiles por módulo y los casos de humo.
3. Revisar persistencia, integridad y permisos de scripts de empresa.
4. Auditar el flujo de empaquetado/instalación y sus artefactos generados.
5. Revisar cada subproducto, sus dependencias y la documentación de operación.
6. Completar la matriz de documentación vigente, archivada y contradictoria.
