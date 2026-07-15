# BrosLMV — Guía de desarrollo (modificar y compilar)

Para **desarrolladores** que van a modificar o recompilar BrosLMV: el **núcleo**
(la DLL/consola) y/o los **instaladores** (`.exe`).

> Para solo crear botones `.csx` **no** necesitas nada de aquí → [`MANUAL.md`](MANUAL.md).
> Para entender cada decisión técnica a fondo → [`ESPECIFICACION.md`](ESPECIFICACION.md).

---

## 1. Qué necesitas (requisitos)

| Requisito | Para qué | Cómo verificar / obtener |
|-----------|----------|--------------------------|
| **Windows** | Es un addon de CONTPAQi (Windows) | — |
| **.NET SDK 8.0 o superior** | Compilar todo (`dotnet build`) | `dotnet --version`. Descarga: https://dotnet.microsoft.com/download |
| **.NET Framework 4.8** (runtime) | Es el destino de compilación (`net48`) | Ya viene en Windows 10/11 y Server 2016+ |
| **Internet a nuget.org** (solo la 1ª vez) | Bajar Roslyn, ScintillaNET y SQLite | Quedan en caché local; luego compila sin internet |
| **PowerShell** | Correr los scripts de `build\` | Incluido en Windows |
| (Opcional) **VS Code o Visual Studio** | Editar cómodo | No es obligatorio: todo se compila por terminal |

> **No necesitas Visual Studio.** Todo se hace con `dotnet build` desde PowerShell,
> a través de los scripts de la carpeta `build\`.

Para **probar la instalación** en este mismo equipo, además: **CONTPAQi Comercial PRO**
instalado y permisos de **administrador** (el instalador registra el COM en HKLM).

---

## 2. Estructura del repositorio

```
C:\MLVTotal\
├── src\                      ← CÓDIGO FUENTE DEL NÚCLEO (la DLL/consola)
│   ├── BrosLMV.csproj        Proyecto del addon (net48, dependencias)
│   ├── ClsMain.cs            COM server + despachador + AssemblyResolve + AssemblyVersion
│   ├── Scripting.cs          Motor Roslyn, contexto ctx, conexión, lectura del grid
│   ├── Datos.cs              SQLite (auditoría, recientes, favoritos)
│   ├── Consola.cs            La consola (editor Scintilla, paneles, estética)
│   ├── Rutas.cs              Rutas fijas C:\BrosLMV\...
│   └── assets\               Logo + icono EMBEBIDOS en la DLL (consola)
│
├── instaladores\             ← CÓDIGO FUENTE DE LOS .EXE (C# WPF)
│   ├── Empresas\             BrosLMV-Instalador.exe (bienvenida → runtime → provisión)
│   └── Desinstalador\        BrosLMV-Desinstalador.exe (quitar de empresas / del equipo)
│
├── instalador\               ← INSUMOS de los instaladores (NO se entrega suelto)
│   ├── bin\                  DLLs compiladas del addon (las empaqueta el .exe)
│   ├── scripts\              Scripts .csx de ejemplo
│   ├── sql\                  provision_empresa.sql, desprovision_empresa.sql, diagnóstico
│   └── assets\               Logos + BrosLMV.ico (fuente de los .exe)
│
├── build\                    ← SCRIPTS DE COMPILACIÓN
│   ├── compilar.ps1          Solo compila el addon → build\out
│   ├── descargar_python.ps1    Descarga CPython embeddable a instalador\runtimes\python
│   ├── generar_instalador.ps1  Compila addon + host y actualiza instalador\
│   └── generar_exes.ps1      Empaqueta y compila los .exe → dist\
│
├── dist\                     ← SALIDA: BrosLMV-Instalador-X.Y.Z.exe / BrosLMV-Desinstalador-X.Y.Z.exe
├── docs\                     Documentación
└── BrosLMV.sln               Solución (abre el núcleo en Visual Studio)
```

> `dist\`, `instaladores\*\obj|bin|assets|app.ico` y `payload.zip` son **generados**
> (están en `.gitignore`); se regeneran con los scripts de `build\`.

---

## 3. Cómo compilar (paso a paso)

Hay tres niveles según lo que toques. **Para un release completo, corre A y luego B.**

### A) Recompilar el núcleo y actualizar el paquete

Si cambiaste algo en `src\` (la DLL/consola):

```powershell
# Cierra CONTPAQi si está abierto en este equipo (bloquea la DLL)
Get-Process ComercialSP -ErrorAction SilentlyContinue | Stop-Process -Force

# Compila el addon, compila el host v3.0 y actualiza instalador\
C:\MLVTotal\build\generar_instalador.ps1
```

> Solo compilar sin tocar el paquete: `build\compilar.ps1` (deja las DLLs en `build\out`).
> Para preparar/actualizar Python embeddable: `build\descargar_python.ps1`.

### B) Generar los ejecutables

Empaqueta el runtime (`instalador\bin`, `host`, `workers`, `runtimes`) dentro del `.exe`
y compila ambos:

```powershell
C:\MLVTotal\build\generar_exes.ps1
```

Resultado en `dist\` (el nombre lleva la versión del addon empacado, p.ej. `2.33.5`, para
no confundir cuál mandar; `generar_exes.ps1` borra los `.exe` de versiones anteriores antes
de compilar, así `dist\` nunca acumula versiones viejas):
- **`BrosLMV-Instalador-X.Y.Z.exe`** (~50 MB con host .NET self-contained + CPython embeddable;
  lleva el runtime embebido como `payload.zip`).
- **`BrosLMV-Desinstalador-X.Y.Z.exe`** (~0.1 MB).

### Flujo típico de una mejora (de principio a fin)

1. Editas `src\*.cs` (o `instaladores\…` si tocas el GUI/instalador).
2. `build\generar_instalador.ps1`  → recompila la DLL a `instalador\bin`.
3. `build\generar_exes.ps1`  → genera los `.exe` en `dist\`.
4. Pruebas: corre `dist\BrosLMV-Instalador-X.Y.Z.exe` (instala el runtime y abre el GUI).
5. Subes la versión en `src\ClsMain.cs` (`AssemblyVersion`) y anotas en
   [`CHANGELOG.md`](CHANGELOG.md); actualizas los `.md` afectados (ver §7).

> **Requisitos al compilar:** .NET SDK presente y, la 1ª vez, internet (NuGet).
> Si `dotnet build` falla por paquetes, es que faltó internet en la 1ª compilación.

---

## 4. Cómo está armado (resumen técnico)

### El COM server y el despachador (`ClsMain.cs`)
CONTPAQi lee `ControlExecute = "BrosLMV.<AppKey>"`, crea `BrosLMV.clsMain` y llama
`ExecuteFunction("<AppKey>")`. El `switch`: `CONSOLA` → abre la consola; `PRUEBA` →
diagnóstico; cualquier otro → ejecuta `C:\BrosLMV\scripts\<AppKey>.csx`.

**Identidad COM fija (NO cambiar, rompería el registro y el ribbon):**
- ProgID `BrosLMV.clsMain`
- CLSID `{E593D5A9-4BAA-4618-A5BB-F7E1F9B0359E}`

### El resolutor de ensamblados (CRÍTICO — no quitar)
Un **constructor estático** en `clsMain` engancha `AppDomain.AssemblyResolve` y carga
las dependencias (Roslyn, `System.*`) desde `C:\BrosLMV\bin` **por nombre simple,
ignorando la versión**. Hace de *binding redirects* (que `dotnet build` no genera y no
controlamos en `ComercialSP.exe.config`). Sin esto, ejecutar un script falla con
`TypeInitializationException` de `PerTypeValues`.

### El motor de scripts (`Scripting.cs`)
`ScriptRunner` usa Roslyn (`Microsoft.CodeAnalysis.CSharp.Scripting`) para compilar el
`.csx` en memoria e inyectar `ctx`. La **conexión** (`Conexion`) reutiliza la que
CONTPAQi ya tiene abierta (`XEngineLib.DataLayer` o `janusGrid.ADORecordset.ActiveConnection`).
La **selección** (`GridSelection`) se lee del grid clonando el recordset; llave
primaria dinámica por módulo (`engModuleParameter`).

### La consola (`Consola.cs`)
WinForms + editor Scintilla. Cabecera con gradiente navy + **logo** (embebido en la
DLL, `src\assets`) + icono de ventana. Biblioteca de scripts, inspector de `ctx`,
ejecución segura (modo solo lectura, confirmación de escrituras), historial.

### Los instaladores (`instaladores\`)
- **`Empresas\`** (`BrosLMV-Instalador.exe`, WPF): `App.xaml.cs` → eleva (UAC) →
  `WelcomeWindow` → `ProgressWindow` mientras `RuntimeInstaller.Install()` despliega el
  `payload.zip` embebido a `C:\BrosLMV`, copia el icono y registra el COM → abre
  `MainWindow` (provisión SQL con `provision_empresa.sql` embebido, adaptable al esquema).
- **`Desinstalador\`** (`BrosLMV-Desinstalador.exe`, WPF): eleva (UAC) → `MainWindow`
  con "Quitar de empresas" (`desprovision_empresa.sql`) y "Quitar de este equipo"
  (`RuntimeUninstaller`: des-registra COM, borra icono y elimina `C:\BrosLMV`).

---

## 5. Las DLLs que produce el build (15 + 1 nativa)

`BrosLMVClsMain.dll` (nuestra) + 4 `Microsoft.CodeAnalysis*` (Roslyn) + 8 `System.*`
(dependencias de Roslyn) + `ScintillaNET.dll` (editor; embebe `SciLexer.dll`) +
`System.Data.SQLite.dll`, y aparte el nativo **`bin\x86\SQLite.Interop.dll`**.
`dotnet build` las resuelve y copia todas. Detalle y la trampa de versiones en
[`ESPECIFICACION.md`](ESPECIFICACION.md).

---

## 6. Cambios típicos

| Quiero... | Dónde |
|-----------|-------|
| Agregar una función a `ctx` | `src\Scripting.cs` → `ScriptContext` |
| Cambiar la consola (estética/funciones) | `src\Consola.cs` |
| Agregar un caso compilado al despachador | `src\ClsMain.cs` → `switch` |
| Cambiar las rutas (`C:\BrosLMV`) | `src\Rutas.cs` |
| Cambiar el GUI del instalador | `instaladores\Empresas\MainWindow.xaml(.cs)` |
| Cambiar la bienvenida del instalador | `instaladores\Empresas\WelcomeWindow.xaml(.cs)` |
| Cambiar qué se instala/quita en el equipo | `RuntimeInstaller.cs` / `RuntimeUninstaller.cs` |
| Cambiar el SQL de provisión/des-provisión | `instalador\sql\*provision_empresa.sql` |

> El logo/icono de la consola se cambian en `src\assets\`; los del instalador en
> `instalador\assets\` (los scripts de `build\` los copian al compilar).

---

## 7. Mantener la documentación (OBLIGATORIO en cada mejora)

Ningún cambio se da por terminado sin actualizar la documentación en el mismo momento.

| Si cambiaste... | Actualiza también... |
|-----------------|----------------------|
| La **API de `ctx`** (`Scripting.cs`) | Tabla de API en [`MANUAL.md`](MANUAL.md) |
| El **despachador** / un caso nuevo (`ClsMain.cs`) | `MANUAL.md` |
| **Rutas**, **instalador** o **desinstalador** | `INSTALACION.md` y `../README.md` |
| Las **DLLs** / dependencias (`.csproj`) | §5 de este doc, `ESPECIFICACION.md`, `../README.md` |
| **Cualquier cosa** | [`CHANGELOG.md`](CHANGELOG.md) (nueva versión) y `AssemblyVersion` en `ClsMain.cs` |
| **Descubriste una recomendación, patrón o limitación** (no un cambio de código en sí — p. ej.<br>"esto tronó por X, hacerlo así en vez de asá") | **Siempre** también a [`MANUAL.md`](MANUAL.md), en la sección que corresponda, **bien explicada** (qué pasa, por qué, qué hacer). No basta con dejarla en el `CHANGELOG.md` (ahí es historial técnico, no lo que usa quien escribe scripts) ni solo mencionarla en el chat. Ver regla completa en [`ESTADO.md`](ESTADO.md) → "REGLA DE ORO". |
