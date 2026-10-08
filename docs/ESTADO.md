# Estado del proyecto y cómo continuar

> **Punto de entrada al retomar.** Corto a propósito — historia completa versión por versión
> en [`CHANGELOG.md`](CHANGELOG.md), reglas no-negociables en [`../AGENTS.md`](../AGENTS.md).
> Bitácora larga de sesiones anteriores a 2026-09-27 (con contradicciones entre sí, léase con
> cautela): [`ESTADO_ARCHIVO.md`](archivo/ESTADO_ARCHIVO.md).

## REGLA DE ORO: documentar todo, siempre

Cualquiera que retome el proyecto debe poder hacerlo **desde cero** con solo los `.md` + el
código. Cada cambio en `src/` se acompaña, en el mismo commit, de: entrada en
[`CHANGELOG.md`](CHANGELOG.md) + `AssemblyVersion` en `src/ClsMain.cs` + bloque en
`src/assets/notas_version.html` + los `.md` afectados. Verificado por
`build/verificar_regla_de_oro.ps1` (y en CI, `.github/workflows/ci.yml`, en cada push/PR).
Todo cambio real, "gotcha" o límite descubierto va también a `MANUAL.md`, no solo al
CHANGELOG — ver detalle en [`AGENTS.md`](../AGENTS.md).

## ⚠️ TRAMPA: compilar no es instalar

`dist/` no se actualiza solo. Después de cualquier cambio que deba llegar a una instalación
real: `build/generar_instalador.ps1` (por defecto mata `ComercialSP.exe` a la fuerza;
usar `-MantenerComercialAbierto` para preparar el paquete sin cerrar la aplicación ni
instalar) + `build/generar_exes.ps1`. Ya pasó de verdad: se hizo una demo con un cliente
con una versión semanas más vieja que la de GitHub porque nadie regeneró el instalador.

## Mejora local validada y aplicada (2026-10-08, v3.1.4)

Contexto/SDK visibles de inicio y superficies gris claro de menor brillo. Completado
por catalogo C#/Python, `ctx.erp`, Ctrl+Espacio, sangria y parejas de delimitadores;
referencias sincronizadas con lenguaje y filtro SDK; busqueda por palabras/mayusculas
y atajos de archivo. No cambia motor, permisos ni API publica. Respaldo de codigo,
DLL instalada e instalador 3.1.3 fuera del repositorio antes de editar.
Layout aislado y ayudas de programacion: 51 comprobaciones aprobadas a 1280x840 y
1040x660, con inspector y busqueda visibles. Compilacion del paquete: cero errores
y cero advertencias; catalogo SDK completo. Bateria completa: 46/46 aprobadas en el
laboratorio autorizado, con respaldo SQL COPY_ONLY/CHECKSUM y VERIFYONLY previos.
Aplicada localmente: DLL instalada 3.1.4.0, hash igual al paquete probado. Activacion
COM nueva de 32 bits confirmo version y ruta; enlace tardio confirmo propiedades y
asignacion de XEngineLib. Actualizacion de solo DLL/registro COM, sin cambios SQL ni
plantillas; respaldo de la 3.1.3 conservado. Abrir Comercial de nuevo para cargarla.
Paquete, instalador y desinstalador 3.1.4 regenerados. Recursos embebidos verificados;
el SHA256 de la DLL dentro del payload del instalador coincide con el de la instalada
y probada. Sin publicacion ni commit por esta tarea.

## Trabajo local (2026-10-08, v3.1.3 validada)

Consola nativa rediseñada con biblioteca clara, selector C#/Python/SQL, editor oscuro y
salida debajo. El inspector se abre con **Contexto y SDK**. No se cambia el constructor
de producción, el motor de ejecución ni los permisos. Se corrigieron la sincronización
de pestañas, el estado de guardado y la restauración del contexto al salir de Zen.

- Respaldo previo del código y de los instaladores creado fuera del repositorio.
- Compilación del addon: cero errores y cero advertencias. Layout aislado aprobado a
  1280×840 y 1040×660, incluyendo inspector, lenguajes, pestañas y estado de edición.
- Prueba aislada: `build/consola/verificar_diseno.ps1`; imágenes en
  `build/out/consola-diseno/`. Solo la copia temporal de prueba omite empresa/SQL.
- Batería completa ejecutada en `BROSLMV_DESARROLLO` con autorización explícita del
  usuario y respaldo SQL verificado previo. Primera pasada: 25/46 aprobadas; se detectó
  Runner local desactualizado, rutas con un carácter de control en once pruebas y
  dependencias externas ausentes. Runner recompilado, rutas reparadas y dependencias
  preparadas: segunda pasada, 36/46 aprobadas. Repetición del caso 37 con el motor PDF
  actual: aprobada (PDF sueltos, ZIP, unido, ZIP + unido y tipo personalizado).
  Los nueve pendientes quedaron resueltos el 2026-10-08: las referencias nativas de
  ocho pruebas reciben explícitamente `Title=NULL`, como el fixture SQL, en vez del
  default vacío de XEngine. La comparación sigue incluyendo estrictamente `Title`;
  no se modificaron plantillas históricas ni se quitó ninguna comprobación. El caso 20
  prepara idempotentemente `HUMO-PROD-SERIE` con series solo en el laboratorio autorizado,
  sin alterar productos existentes. Batería completa repetida: **46/46 aprobadas**.
  El caso 37 también usaba un motor de PDF local anterior: se recompiló
  `htmlpdf/BrosLMV.HtmlToPdf.csproj` en su salida por defecto y se repitió correctamente.
  Compilar con `-o instalador/...` no actualiza los ejecutables de `bin/Release` que
  varias pruebas usan por defecto; recompilar allí Runner y PDF antes de correr humo.
  El respaldo SQL previo a esta repetición también fue verificado.
- Aplicación local: `build/consola/actualizar_local.ps1` sustituye solo la DLL del addon
  y registra COM de 32 bits con respaldo, permisos de administrador y humo verde como
  condiciones obligatorias. No cierra Comercial a la fuerza, no provisiona empresas
  ni actualiza bases o plantillas. La versión se verifica después por activación COM
  en un proceso nuevo de 32 bits, no solo por el archivo compilado.
- **Aplicada en este equipo el 2026-10-08:** `C:\BrosLMV\bin\BrosLMVClsMain.dll`,
  versión 3.1.3.0; hash idéntico al paquete validado. Activación COM nueva de 32 bits
  confirmó versión y ubicación; prueba de enlace tardío confirmó propiedades y
  asignación del objeto `XEngineLib`. No se actualizaron bases ni plantillas.
  El registro usa `/codebase`, sin `/tlb`: la exportación de tipos del protocolo falla
  también en la 3.1.2 con ByteString/System.Memory; ver `MANUAL.md` §14. El primer
  intento fue restaurado automáticamente antes de registrar correctamente.
- Paquete y ejecutables 3.1.3 regenerados; abrir Comercial de nuevo para ver la nueva
  Consola. El respaldo local conserva la DLL instalada anterior 3.1.2.
- Límite separado del rediseño: el instalador general y `instalador/Instalar.ps1`
  todavía solicitan `/tlb`. En este equipo esa exportación devuelve error de referencias;
  el ejecutable lo registra como aviso. El actualizador local evita esa exportación y
  se verificó exitosamente por activación y enlace tardío. No se cambió el instalador
  general por este hallazgo; queda como corrección de despliegue por tratar aparte.
- No se publica una versión en GitHub por esta tarea.

## Estado de referencia (2026-09-29, addon v2.98.0)

| Pieza | Estado | Dónde leer más |
|---|---|---|
| **Addon** (botones + Consola, C#/Python/SQL en `zzBrosScript`) | En producción en varias empresas. Últimos cambios (2.94.0–2.98.0): plantilla **Crear documentos desde XML** (única plantilla), asistente **Crear/Editar botón** con catálogo de más de 2,000 íconos, selector de lenguaje, respaldo de scripts y **manual del SDK** (catálogo único de 170 funciones) | `CHANGELOG.md`, `MANUAL.md`, `CREAR_DOC_DESDE_XML.md`, `CREAR_BOTON.md`, `SDK_REFERENCIA.md` |
| **Python** | En producción desde v2.6.0: host x64 fuera de proceso por Named Pipe + SQL por la conexión viva de Comercial. Pendiente: host persistente (C6d) | `PYTHON.md`, `ARQUITECTURA_V3.md`, `host/README.md` |
| **PDF de documentos + correo** (`htmlpdf/`) | En producción (2.87.0–2.89.0), 10 formatos genéricos. Paginación real con Paged.js disponible (2.91.0), sin aplicar a las plantillas todavía | `PAGINACION_PDF.md` |
| **`BrosLMV.Runner`** (headless) | Funciona, lo usan integraciones externas en producción (colas de documentos) y el instalador lo copia a `C:\BrosLMV\runner` (al menos desde v2.90.0). Pendiente: política de escrituras sin supervisión y reintentos | `MANUAL.md` §12 "Integraciones externas vía BrosLMV.Runner" |
| **Motor de recetas no-code** | MVP construido (2 recetas, pasos encadenados, asistente). Sin trabajo activo | `RECETAS_NOCODE.md` |
| **Motor de Asientos Contables** | Validado en producción (cobros/pagos multi-moneda); en el repo solo está el motor de cálculo y el esquema | `MOTOR_ASIENTOS_CONTABLES.md` |
| **Contabilidad** (SDK y modelo de datos) | Investigado y documentado; el SDK no se ha probado en vivo desde este repo | `SDK_CONTABILIDAD.md`, `CONTABILIDAD_MODELO_DATOS.md` |
| **`BrosLMV.Descargas`** (descarga masiva SAT) | Subproducto independiente, instalador propio v2.4.0 | `descargas/DOCUMENTACION.md` |
| **Punto de Venta** | Prototipo **privado**, fuera del repo público a propósito | `AGENTS.md` §1 |

Conocimiento consolidado recientemente (2026-09-27): el barrido de los proyectos de clientes
terminó y lo genérico ya está en `MANUAL.md` (§10.5 vínculos entre documentos, §10.6
operaciones financieras, §12 advertencias), `DASHBOARDS_HTML.md` y `MIGRAR_BOTONES_RT.md`.
La documentación vieja o superada se movió a `docs/archivo/`.

**Último release registrado en este resumen histórico:** [v2.98.0](https://github.com/DonPapiCandelas/BrosLMVTotal/releases/tag/v2.98.0)
(2026-09-29), con las notas de 2.94.0–2.98.0 (el anterior era v2.93.0). Las empresas ya provisionadas conservan su
propia copia de las plantillas de fábrica en `zzBrosScript`; el instalador `.exe` refresca la plantilla vigente y
mueve las anteriores a `scripts\_archivo\`. Pendiente de verificar en Comercial por quien lo use: el filtro por usuario
del asistente de botones (`IfUserIDIs`) y las funciones nuevas de la Consola (selector de lenguaje, respaldo de scripts,
manual del SDK).

## Qué sigue

El plan priorizado vive en [`ROADMAP.md`](ROADMAP.md). Para una revisión externa del proyecto,
ver [`AUDITORIA.md`](AUDITORIA.md).
