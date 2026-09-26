# BrosLMV

[![Licencia: GPL v3](https://img.shields.io/badge/Licencia-GPLv3-blue.svg)](LICENSE)
[![Estado](https://img.shields.io/badge/estado-en%20producci%C3%B3n-brightgreen.svg)](docs/CHANGELOG.md)

Software libre bajo [GPL-3.0](LICENSE). Las contribuciones son bienvenidas — ver
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## Qué es

BrosLMV es una familia de herramientas para CONTPAQi Comercial PRO, todas con el mismo
objetivo: que un negocio pueda automatizar y extender su Comercial PRO con una conexión nativa
al motor, sin depender de un tercero para cada cambio ni de servicios de licencia externos.

- **El addon (`src/`)** agrega botones personalizados a Comercial PRO. En el centro hay una
  **Consola** que compila y corre scripts al vuelo dentro de CONTPAQi, con acceso directo a la
  base de datos de la empresa activa, a los documentos seleccionados en pantalla y al propio
  motor de Comercial (crear, abrir, cancelar y afectar documentos por API).
- **Documentos en PDF y por correo (`htmlpdf/`)** — genera el PDF de cualquier documento a partir
  de los formatos HTML de Comercial (mismas etiquetas), con un motor moderno (Chromium/WebView2),
  vista previa, envío por correo con cuenta propia (Outlook, Gmail o dominio propio), carpeta y
  nombre de archivo configurables por módulo (incluye rutas de red), y **10 formatos genéricos**
  listos para usar, con código QR, desglose de impuestos y partidas ilimitadas por hoja.
- **`BrosLMV.Runner`** corre esos mismos scripts **sin abrir Comercial** — pensado para Tareas
  Programadas de Windows o integraciones externas (ya en producción: crea/actualiza documentos
  reales, afecta inventario y cuentas por cobrar, headless).
- **`BrosLMV.Descargas`** es un subproducto independiente (base de datos, instalador y versión
  propios): descarga los CFDI de un contribuyente directo del **Servicio de Descarga Masiva del
  SAT** (protocolo SOAP oficial, no scraping), con interfaz de escritorio, servicio de Windows
  para automatizarlo, y su propio instalador/desinstalador.

Tres lenguajes conviven en el mismo botón o consola del addon: C# (Roslyn, en proceso), Python
(host x64 fuera de proceso) y SQL (T-SQL directo por la conexión viva).

Este repositorio es el proyecto completo: código fuente, documentación suficiente para
reconstruir cada herramienta desde cero, y los paquetes de instalación listos para distribuir.

## Qué hace

### El addon + Consola (Comercial PRO)

- **Consola de scripts.** Escribe C#, Python o SQL, presiona *Ejecutar* y corre dentro de
  CONTPAQi sin recompilar ni reiniciar. Editor con resaltado de sintaxis, números de línea,
  búsqueda (Ctrl+F), pantalla completa, autocompletado de `ctx.`, referencias por lenguaje,
  inspector de contexto, modo de ejecución segura (solo lectura, confirmación de escrituras),
  candado opcional por contraseña, historial de versiones por script, y auditoría.
- **Conexión automática.** Reutiliza la conexión que CONTPAQi ya tiene abierta con la empresa
  activa. No hay que configurar credenciales por empresa, ni siquiera si el cliente maneja
  decenas de bases de datos distintas.
- **Botones a la medida.** Cada botón es un script guardado por empresa (tabla `zzBrosScript`);
  se crean y editan desde la misma consola, organizables por categoría. El ribbon los invoca vía
  `BrosLMV.<AppKey>`.
- **API del motor (`ctx.erp`).** Crear documentos con partidas, impuestos, series y lotes
  (`NuevoDocumento`, `AgregarArticulo`, `AgregarSerie`), abrirlos en pantalla
  (`AbrirDocumento`), cancelar/reactivar, afectar inventario y cuentas por cobrar, consultar
  precios, existencias y tipos de cambio, imprimir, exportar a Excel/PDF y enviar correo.
- **Formularios y reportes en HTML.** Ventanas con WebView2 (formularios, asistentes) y
  dashboards con tabla ordenable, buscador, paginación y exportación a Excel sin escribir HTML.
- **Integridad y auditoría.** Huella SHA-256 por script, aprobación opcional antes de ejecutar,
  bitácora local (SQLite) y central por empresa (`zzBrosAuditoria`).
- **Configuración de formato y Diagnóstico.** Pantalla moderna para asignar el formato HTML de
  cada módulo, definir carpeta y nombre del PDF, configurar el correo y comprobar de un vistazo
  que todo esté instalado y sano.

### Generar documento (PDF) y enviar por correo

Desde un documento abierto, un botón arma el PDF con el formato HTML asignado a su módulo:
resuelve las etiquetas `[Campo]`, las bandas de partidas y las máscaras `[Format(campo,#,##0.00)]`
igual que Comercial, y ofrece **Vista previa · Generar PDF · Enviar por correo**. Los formatos
incluidos cubren pedidos, remisiones, facturas, notas de crédito, órdenes de compra, recepciones,
gastos (con tipo de gasto por partida), entradas, traspasos y salidas de almacén, con varios
impuestos, descuentos y retenciones. Es un componente aparte (`BrosLMV.HtmlToPdf.exe`) que se
instala junto con el addon y requiere el WebView2 Runtime de Microsoft.

### `BrosLMV.Runner` (headless, sin Comercial abierto)

Standalone de 32 bits que levanta su propio XEngine (`ctx.erp.*`) y reutiliza el mismo motor de
scripting del addon (`ScriptContext`/`ScriptRunner`, enlazados desde `src/`, no reescritos).
Corre un script marcado `# job: safe-offline` por línea de comandos:

```
BrosLMV.Runner.exe --appkey <NOMBRE> --bd <EMPRESA> [--userid N] [--conn "..."]
```

Ya en producción creando documentos reales (compras, ventas, cobros) sin que Comercial esté
abierto en ningún momento.

### `BrosLMV.Descargas` (descarga masiva de CFDI del SAT)

- **Protocolo real del SAT**, verificado contra producción: Autenticación (WS-Security + XML-DSig),
  Solicitud (consume cupo diario, protegido detrás de confirmación explícita), Verificación y
  Descarga — 4 operaciones SOAP en 2 hosts distintos.
- **Base de datos propia**, con auditoría/histórico real (cambios de estatus de cancelación,
  relaciones entre CFDI) — no depende de ni modifica la base de Comercial Pro.
- **Interfaz de escritorio (WPF)**: catálogo de empresas, solicitud manual, bitácora, reportes,
  vínculo de un XML a un documento de Comercial (asociación manual, nunca automática).
- **Servicio de Windows** (`BrosLMV.Descargas.Servicio`) para automatizar solicitud/descarga sin
  Tareas Programadas visibles.
- **FIEL cifrada con DPAPI** atada a la máquina — se guarda para poder automatizar, nunca en
  texto plano ni portable a otro equipo.
- Instalador/desinstalador propios (`BrosLMV-Descargas-Instalador-X.Y.Z.exe`), independientes
  del instalador del addon.

Documentación completa: [`descargas/DOCUMENTACION.md`](descargas/DOCUMENTACION.md).

## Estructura del repositorio

```
BrosLMV/
├── README.md                 Empieza aquí
├── BrosLMV.sln                Solución de Visual Studio (abre el addon)
│
├── src/                        Addon del núcleo (Comercial PRO)
│   ├── BrosLMV.csproj          Proyecto .NET (target net48, dependencias)
│   ├── ClsMain.cs               COM server + despachador de botones + AssemblyResolve
│   ├── Scripting.cs             Motor Roslyn, contexto `ctx`, conexión y lectura del grid
│   ├── Datos.cs                  Almacenamiento local SQLite (auditoría, recientes, favoritos)
│   ├── Consola.cs                Ventana de la consola (editor, paneles, ejecución)
│   ├── ConsolaPasswordHash.cs      Hash salteado (PBKDF2) reusado por la contraseña de la Consola
│   ├── Rutas.cs                    Rutas fijas (C:\BrosLMV\...)
│   └── assets/                      Logo e icono embebidos en la DLL
│
├── runner/                    BrosLMV.Runner — ejecución headless (sin Comercial abierto)
├── htmlpdf/                   PDF de documentos: motor HTML→PDF (WebView2), scripts y 10 formatos
├── host/                       Supervisor x64 del canal C# ↔ Python
├── workers/                    Paquete Python (`ctx`) y ejecutor de scripts
├── protocol/                   Contrato del canal C# ↔ Python (protobuf)
│
├── descargas/                 BrosLMV.Descargas — motor + CLI (net8.0, consola)
├── descargas-ui/               Interfaz de escritorio (WPF)
├── descargas-servicio/          Servicio de Windows (automatización sin Tareas Programadas)
├── descargas-instalador/        Instalador propio (WPF)
├── descargas-desinstalador/     Desinstalador propio (WPF)
│
├── docs/                       Documentación del addon (ver docs/INDICE.md)
├── instaladores/               Fuente de los .exe del addon (C# WPF)
│   ├── Empresas/                 BrosLMV-Instalador.exe (bienvenida → runtime → provisión)
│   └── Desinstalador/            BrosLMV-Desinstalador.exe (quitar de empresas o del equipo)
├── instalador/                 Insumos de los .exe del addon (no se entrega suelto)
│   ├── bin/                      DLLs compiladas (+ x86/SQLite.Interop.dll)
│   ├── scripts/                  Scripts de ejemplo (.csx/.ctx)
│   ├── sql/                      provision_empresa.sql / desprovision_empresa.sql
│   └── assets/                    Logos + BrosLMV.ico
│
├── build/                      Scripts de compilación (addon y Descargas)
└── dist/                       Salida: instaladores/desinstaladores generados (no versionado)
```

## Instalación rápida

### El addon (botones + Consola en Comercial PRO)

Guía detallada (servidor, terminales, multi-empresa, problemas comunes):
[`docs/INSTALACION.md`](docs/INSTALACION.md).

Se entregan dos ejecutables autocontenidos en `dist/`, con la **versión en el nombre**
(p.ej. `BrosLMV-Instalador-X.Y.Z.exe`, con la versión real del build) para no confundir
cuál mandar:

1. `BrosLMV-Instalador-X.Y.Z.exe` — doble clic, aceptar UAC, **Instalar** (despliega el runtime a
   `C:\BrosLMV`, copia el icono y registra el componente COM), luego abre el GUI de provisión.
2. En el GUI: servidor\instancia + usuario/contraseña → **Probar conexión** → marca las
   empresas → **Instalar seleccionadas**. En una terminal sin acceso al SQL, cierra el GUI: el
   runtime ya quedó instalado igual.
3. Reinicia CONTPAQi. Debería aparecer el botón **Consola BrosLMV** en la pestaña "Soluciones LMV".

Para quitarlo: `BrosLMV-Desinstalador-X.Y.Z.exe` (quita de empresas específicas o del equipo
completo).

### BrosLMV.Descargas

Instalador propio, independiente del addon: `BrosLMV-Descargas-Instalador-X.Y.Z.exe` — pide la
cadena de conexión a la base de datos propia de Descargas (no la de Comercial Pro), instala la
UI y, si se quiere automatizar, el servicio de Windows. Desinstalador equivalente:
`BrosLMV-Descargas-Desinstalador-X.Y.Z.exe`. Detalle completo en
[`descargas/DOCUMENTACION.md`](descargas/DOCUMENTACION.md).

## Compilar y generar los instaladores

Requiere .NET SDK (y, la primera vez, internet para restaurar NuGet). Guía paso a paso del
addon: [`docs/DESARROLLO.md`](docs/DESARROLLO.md). Resumen:

```powershell
# ===== Addon (Comercial PRO) =====
# 1) Recompila el núcleo (DLL/consola) y actualiza instalador\bin
.\build\descargar_python.ps1
.\build\generar_instalador.ps1

# 2) Empaqueta y compila los ejecutables a dist\
.\build\generar_exes.ps1

# ===== BrosLMV.Descargas =====
# Empaqueta UI + servicio + instalador/desinstalador a dist\
.\build\generar_exes_descargas.ps1
```

Resultado: `dist\BrosLMV-Instalador-X.Y.Z.exe`, `dist\BrosLMV-Desinstalador-X.Y.Z.exe`,
`dist\BrosLMV-Descargas-Instalador-X.Y.Z.exe` y `dist\BrosLMV-Descargas-Desinstalador-X.Y.Z.exe`
(la versión del addon sale sola del `AssemblyVersion` empacado en `instalador\bin`, y
`generar_exes.ps1` borra los `.exe` de versiones anteriores en `dist\` antes de compilar, para
que no quede ninguno viejo dando vueltas). Sube la versión en `src/ClsMain.cs`
(`AssemblyVersion`) y anótala en [`docs/CHANGELOG.md`](docs/CHANGELOG.md).

## Documentación

Todo lo del addon vive en `docs/` (orden de lectura sugerido en [`docs/INDICE.md`](docs/INDICE.md)):

| Documento | Para qué |
|-----------|----------|
| [`MANUAL.md`](docs/MANUAL.md) | Crear y editar botones, API de `ctx`, ejemplos |
| [`CAPACIDADES.md`](docs/CAPACIDADES.md) | Alcance y poder real (reportes HTML, análisis, librerías) |
| [`INSTALACION.md`](docs/INSTALACION.md) | Instalación detallada (servidor + terminales) |
| [`DESARROLLO.md`](docs/DESARROLLO.md) | Modificar el código y recompilar |
| [`ESPECIFICACION.md`](docs/ESPECIFICACION.md) | Blueprint técnico completo, para reconstruir desde cero |
| [`CHANGELOG.md`](docs/CHANGELOG.md) | Historial de versiones |

`BrosLMV.Descargas` documenta su propio proyecto en un solo archivo, autocontenido:
[`descargas/DOCUMENTACION.md`](descargas/DOCUMENTACION.md) (arquitectura, protocolo del SAT,
decisiones de producto, estructura de carpetas, uso del CLI y de la UI).

## Requisitos

- **Para usar/instalar el addon:** Windows con .NET Framework 4.8 (ya viene con el sistema) y
  CONTPAQi Comercial PRO.
- **Para usar/instalar `BrosLMV.Descargas`:** Windows con .NET 8 Desktop Runtime (el instalador
  lo resuelve) y una FIEL vigente del contribuyente a descargar. No requiere Comercial PRO
  abierto ni instalado.
- **Para compilar o editar cualquiera de los dos:** .NET SDK 8 o superior, y opcionalmente
  Visual Studio o VS Code.
- **Python v3.0** (del addon) viaja empacado como CPython embeddable; no toca el PATH del
  sistema.

## Datos fijos del componente (addon)

- ProgID COM: `BrosLMV.clsMain`
- CLSID: `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`
- Instalación en el equipo: `C:\BrosLMV\` (bin, scripts, logs, data)
- Botón en el ribbon: `engRibbonControl.ControlExecute = 'BrosLMV.<AppKey>'`

## Apoya el proyecto

BrosLMV es y seguirá siendo gratuito, siempre. Lo construí con gusto porque quiero que
cualquiera que use CONTPAQi Comercial PRO pueda exprimirlo al máximo con una conexión nativa
al motor, sin depender de nadie más para cada cambio. Dicho esto, mantenerlo ha costado tiempo y dinero real: un servidor
de pruebas propio, la licencia de Comercial PRO necesaria para desarrollar y validar contra una
instalación real, y muchas horas y muchas horas de trabajo e investigación.

Si el proyecto te sirve y puedes apoyar, se agradece y ayuda a que siga creciendo. Si no puedes,
no pasa nada — la herramienta sigue siendo tuya igual. Entre todos podemos hacer esto grande.

| Método | Datos |
|---|---|
| PayPal | [paypal.me/CandelasGCristofer93](https://paypal.me/CandelasGCristofer93) |
| Mercado Pago | [link.mercadopago.com.mx/broslmv](https://link.mercadopago.com.mx/broslmv) |
| Transferencia (BBVA México) | CLABE `012010015098324800` — Cristofer Alejandro Candelas García |

## Contribuir

BrosLMV es un proyecto abierto. Antes de aportar, lee:

- [`CONTRIBUTING.md`](CONTRIBUTING.md) — flujo de trabajo y reglas del repositorio.
- [`docs/INDICE.md`](docs/INDICE.md) — índice de toda la documentación del addon.
- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) — código de conducta.
- [`SECURITY.md`](SECURITY.md) — reporte de vulnerabilidades, en privado.

## Licencia

Distribuido bajo la Licencia Pública General de GNU v3.0 (GPL-3.0). Eres libre de usar,
estudiar, modificar y redistribuir el código; si distribuyes una versión modificada, debe
seguir siendo software libre bajo la misma licencia. Texto completo en [`LICENSE`](LICENSE).

Copyright © 2026 Cristofer Candelas García.
