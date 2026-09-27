# Estado del proyecto y cómo continuar

> **Punto de entrada al retomar.** Corto a propósito — historia completa versión por versión
> en [`CHANGELOG.md`](CHANGELOG.md), reglas no-negociables en [`../AGENTS.md`](../AGENTS.md).
> Bitácora larga de sesiones anteriores a 2026-09-27 (con contradicciones entre sí, léase con
> cautela): [`ESTADO_ARCHIVO.md`](ESTADO_ARCHIVO.md).

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
real: `build/generar_instalador.ps1` (mata `ComercialSP.exe` a la fuerza — avisar si hay una
demo en curso) + `build/generar_exes.ps1`. Ya pasó de verdad: se hizo una demo con un cliente
con una versión semanas más vieja que la de GitHub porque nadie regeneró el instalador.

## Estado actual (2026-09-27, addon v2.90.0)

- **Addon**: v2.90.0. Lo más reciente: PDF de documentos + correo (`htmlpdf/`, botón
  "Generar documento (PDF)"), "Configuración de formato" (WebView2) con pestaña
  Diagnóstico, 10 formatos HTML genéricos, y el botón "Cotizador" dentro del documento ya se
  activa **por módulo** (no masivo — una versión vieja rompía el guardado nativo, ver
  `CHANGELOG.md` [2.88.0]/[2.89.0]).
- **`ctx.erp.NuevoDocumento`/`AgregarArticulo`** ampliados (tipo de cambio, condición de
  pago, moneda, título, documento origen, `AgregarSerie` nuevo) — CHANGELOG [2.90.0].
- **Motor de recetas no-code** (`src/Recetas.cs`, MVP de T3.1): existe y funciona (2 recetas:
  `sql_tokens` y `crear_documento_desde_otro`, más "pasos encadenados" y el asistente
  "Nueva acción" en la Consola). Sin trabajo activo reciente — no es prioridad actual salvo
  que se pida de nuevo.
- **`BrosLMV.Runner`** (ejecución headless): prototipo funcional, probado con un consumidor
  externo real en producción. **No shipped en el instalador todavía.**
- **Canal C# ↔ Python v3.0** (`host/`, `workers/`, `protocol/`): en desarrollo (checkpoints
  C3a–C3d, ver `host/README.md`), reemplaza al canal Python v1 actual cuando esté listo.
- **`BrosLMV.Descargas`**: subproducto independiente, shipped, instalador propio v2.1.1.
- **Punto de Venta** (`puntodeventa/`, `puntodeventa-app/`): prototipo, **privado, fuera del
  repo público a propósito** (ver `AGENTS.md` §1). Login WPF revisado recientemente: si el
  usuario real de Comercial ligado al alias no tiene password (`engUser.UserPassword` vacío),
  entra sin pedir nada — mismo comportamiento que Comercial.
- **Motor de Asientos Contables**: existe y **ya está validado en producción** con más
  de un cliente real (cobros/pagos multi-moneda) — se construyó 100% como scripts +
  tablas propias, sin tocar el addon (`ctx.EventoId`, la pieza que lo habilita, ya
  existía desde antes). Documentado a fondo en
  [`MOTOR_ASIENTOS_CONTABLES.md`](MOTOR_ASIENTOS_CONTABLES.md); esquema opcional en
  `instalador/sql/motor_asientos_contables.sql`, motor de cálculo reusable en
  `instalador/scripts/motor/`. Falta generalizar los scripts wrapper (hoy solo el motor
  de cálculo está en el repo) y construir el editor visual (diseño ya escrito, con su
  propia regla de "no guardar una opción que el motor todavía ignore").
- **GitHub**: historial reescrito a un solo commit limpio (sin nombres de terceros/clientes),
  `AGENTS.md`/`CLAUDE.md` nuevos, `build/publicar_release.ps1` para publicar releases con
  revisiones previas (regla de oro, árbol limpio, escaneo de términos prohibidos). v2.90.0 ya
  publicada como release.
- **`docs/CHANGELOG.md`** y este archivo se podaron hoy (2026-09-27): lo viejo/repetido pasó
  a `CHANGELOG_ARCHIVO.md`/`ESTADO_ARCHIVO.md`, sin perder nada.

## Qué sigue (sin decidir todavía, en ningún orden particular)

- Decidir si se construye el motor de pólizas/asientos propio (investigación lista, falta
  decisión de producto y diseño de tablas).
- Firmar digitalmente los `.exe` — un antivirus (TSplus) marcó `BrosLMV.Host.exe` como falso
  positivo de ransomware en un cliente; firmar es la solución real.
- Seguir probando el Punto de Venta antes de sacarlo de privado.
- Terminar el canal Python v3.0 (`host/`) y decidir cuándo reemplaza al v1.
