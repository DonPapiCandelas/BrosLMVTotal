# BrosLMV

[![Licencia: GPL v3](https://img.shields.io/badge/Licencia-GPLv3-blue.svg)](LICENSE)
[![Estado](https://img.shields.io/badge/estado-en%20producci%C3%B3n-brightgreen.svg)](docs/CHANGELOG.md)

Software libre bajo [GPL-3.0](LICENSE). Las contribuciones son bienvenidas — ver
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## Qué es

BrosLMV agrega botones personalizados a CONTPAQi Comercial PRO que se ejecutan de forma
autónoma, sin depender de servicios de licencia externos. En el centro hay una consola que
compila y corre scripts al vuelo dentro de CONTPAQi, con acceso directo a la base de datos de
la empresa activa y a los documentos que el usuario tiene seleccionados en pantalla.

Tres lenguajes conviven en el mismo botón o consola: C# (Roslyn, en proceso), Python (host x64
fuera de proceso) y SQL (T-SQL directo por la conexión viva). El objetivo es simple: que
cualquier negocio con Comercial PRO pueda automatizar lo que necesite sin pagar un SDK aparte
ni depender de un tercero para cada cambio.

Este repositorio es el proyecto completo: código fuente, documentación suficiente para
reconstruir la herramienta desde cero, y el paquete de instalación listo para distribuir.

## Qué hace

- **Consola de scripts.** Escribe C#, Python o SQL, presiona *Ejecutar* y corre dentro de
  CONTPAQi sin recompilar ni reiniciar. Editor con resaltado de sintaxis, números de línea,
  búsqueda (Ctrl+F), pantalla completa, autocompletado de `ctx.`, referencias por lenguaje,
  inspector de contexto, modo de ejecución segura (solo lectura, confirmación de escrituras) e
  historial de auditoría.
- **Conexión automática.** Reutiliza la conexión que CONTPAQi ya tiene abierta con la empresa
  activa. No hay que configurar credenciales por empresa, ni siquiera si el cliente maneja
  decenas de bases de datos distintas.
- **Botones a la medida.** Cada botón es un script guardado por empresa (tabla `zzBrosScript`);
  se crean y editan desde la misma consola. El ribbon los invoca vía
  `BrosLMV.<AppKey>`.
- **Auditoría local en SQLite.** Registra cada ejecución (desde consola o desde botón), además
  de recientes y favoritos.

## Estructura del repositorio

```
BrosLMV/
├── README.md            Empieza aquí
├── BrosLMV.sln          Solución de Visual Studio (abre todo el código)
├── src/                 Código fuente del núcleo
│   ├── BrosLMV.csproj   Proyecto .NET (target net48, dependencias)
│   ├── ClsMain.cs       COM server + despachador de botones + AssemblyResolve
│   ├── Scripting.cs     Motor Roslyn, contexto `ctx`, conexión y lectura del grid
│   ├── Datos.cs         Almacenamiento local SQLite (auditoría, recientes, favoritos)
│   ├── Consola.cs       Ventana de la consola (editor, paneles, ejecución)
│   ├── Rutas.cs         Rutas fijas (C:\BrosLMV\...)
│   └── assets/          Logo e icono embebidos en la DLL
├── docs/                Documentación completa (ver docs/INDICE.md)
├── instaladores/        Fuente de los .exe (C# WPF)
│   ├── Empresas/        BrosLMV-Instalador.exe (bienvenida → runtime → provisión)
│   └── Desinstalador/   BrosLMV-Desinstalador.exe (quitar de empresas o del equipo)
├── instalador/          Insumos de los .exe (no se entrega suelto)
│   ├── bin/             DLLs compiladas (+ x86/SQLite.Interop.dll)
│   ├── scripts/         Scripts de ejemplo (.csx/.ctx)
│   ├── sql/             provision_empresa.sql / desprovision_empresa.sql
│   └── assets/          Logos + BrosLMV.ico
├── build/               Scripts de compilación
│   ├── compilar.ps1            solo compila el núcleo → build\out
│   ├── generar_instalador.ps1  compila el núcleo → instalador\bin
│   └── generar_exes.ps1        empaqueta y compila los .exe → dist\
└── dist/                Salida: BrosLMV-Instalador-X.Y.Z.exe / BrosLMV-Desinstalador-X.Y.Z.exe
```

## Instalación rápida

Guía detallada (servidor, terminales, multi-empresa, problemas comunes):
[`docs/INSTALACION.md`](docs/INSTALACION.md).

Se entregan dos ejecutables autocontenidos en `dist/`, con la **versión en el nombre**
(p.ej. `BrosLMV-Instalador-2.33.5.exe`) para no confundir cuál mandar:

1. `BrosLMV-Instalador-X.Y.Z.exe` — doble clic, aceptar UAC, **Instalar** (despliega el runtime a
   `C:\BrosLMV`, copia el icono y registra el componente COM), luego abre el GUI de provisión.
2. En el GUI: servidor\instancia + usuario/contraseña → **Probar conexión** → marca las
   empresas → **Instalar seleccionadas**. En una terminal sin acceso al SQL, cierra el GUI: el
   runtime ya quedó instalado igual.
3. Reinicia CONTPAQi. Debería aparecer el botón **Consola BrosLMV** en la pestaña "Soluciones LMV".

Para quitarlo: `BrosLMV-Desinstalador-X.Y.Z.exe` (quita de empresas específicas o del equipo
completo).

## Compilar y generar los instaladores

Requiere .NET SDK (y, la primera vez, internet para restaurar NuGet). Guía paso a paso:
[`docs/DESARROLLO.md`](docs/DESARROLLO.md). Resumen:

```powershell
# 1) Recompila el núcleo (DLL/consola) y actualiza instalador\bin
.\build\descargar_python.ps1
.\build\generar_instalador.ps1

# 2) Empaqueta y compila los ejecutables a dist\
.\build\generar_exes.ps1
```

Resultado: `dist\BrosLMV-Instalador-X.Y.Z.exe` y `dist\BrosLMV-Desinstalador-X.Y.Z.exe` (la versión
sale sola del `AssemblyVersion` empacado en `instalador\bin`, y `generar_exes.ps1` borra los
`.exe` de versiones anteriores en `dist\` antes de compilar, para que no quede ninguno viejo
dando vueltas). Sube la versión en `src/ClsMain.cs` (`AssemblyVersion`) y anótala en
[`docs/CHANGELOG.md`](docs/CHANGELOG.md).

## Documentación

Todo vive en `docs/` (orden de lectura sugerido en [`docs/INDICE.md`](docs/INDICE.md)):

| Documento | Para qué |
|-----------|----------|
| [`MANUAL.md`](docs/MANUAL.md) | Crear y editar botones, API de `ctx`, ejemplos |
| [`CAPACIDADES.md`](docs/CAPACIDADES.md) | Alcance y poder real (reportes HTML, análisis, librerías) |
| [`INSTALACION.md`](docs/INSTALACION.md) | Instalación detallada (servidor + terminales) |
| [`DESARROLLO.md`](docs/DESARROLLO.md) | Modificar el código y recompilar |
| [`ESPECIFICACION.md`](docs/ESPECIFICACION.md) | Blueprint técnico completo, para reconstruir desde cero |
| [`CHANGELOG.md`](docs/CHANGELOG.md) | Historial de versiones |

## Requisitos

- **Para usar/instalar:** Windows con .NET Framework 4.8 (ya viene con el sistema) y CONTPAQi
  Comercial PRO.
- **Para compilar o editar:** .NET SDK 8 o superior, y opcionalmente Visual Studio o VS Code.
- **Python v3.0** viaja empacado como CPython embeddable; no toca el PATH del sistema.

## Datos fijos del componente

- ProgID COM: `BrosLMV.clsMain`
- CLSID: `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`
- Instalación en el equipo: `C:\BrosLMV\` (bin, scripts, logs, data)
- Botón en el ribbon: `engRibbonControl.ControlExecute = 'BrosLMV.<AppKey>'`

## Apoya el proyecto

BrosLMV es y seguirá siendo gratuito, siempre. Lo construí con gusto porque quiero que
cualquiera que use CONTPAQi Comercial PRO pueda exprimirlo al máximo sin pagar licencias de SDK
ni depender de nadie más. Dicho esto, mantenerlo ha costado tiempo y dinero real: un servidor
de pruebas propio, la licencia de Comercial PRO necesaria para desarrollar y validar contra una
instalación real, y muchas horas — solo el historial de commits del repositorio ya documenta
más de 60 horas de trabajo activo, sin contar la investigación previa.

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
- [`docs/INDICE.md`](docs/INDICE.md) — índice de toda la documentación.
- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) — código de conducta.
- [`SECURITY.md`](SECURITY.md) — reporte de vulnerabilidades, en privado.

## Licencia

Distribuido bajo la Licencia Pública General de GNU v3.0 (GPL-3.0). Eres libre de usar,
estudiar, modificar y redistribuir el código; si distribuyes una versión modificada, debe
seguir siendo software libre bajo la misma licencia. Texto completo en [`LICENSE`](LICENSE).

Copyright © 2026 Cristofer Candelas García.
