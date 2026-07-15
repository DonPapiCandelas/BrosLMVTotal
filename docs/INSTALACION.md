# BrosLMV — Manual de instalación

Guía para **instalar** y **desinstalar** BrosLMV (consola de scripts para CONTPAQi
Comercial PRO) usando los ejecutables autocontenidos.

> Resultado: en CONTPAQi, pestaña **Soluciones LMV**, grupo **BrosLMV**, aparece el botón
> **"Consola BrosLMV"**.

> Los `.exe` de `dist/` llevan la versión en el nombre (p.ej. `BrosLMV-Instalador-2.33.5.exe`)
> para no confundir cuál mandar. En esta guía se abrevia como `BrosLMV-Instalador.exe`.

---

## 0. Dos conceptos

BrosLMV tiene **dos partes**:

| Parte | Qué es | Dónde | Cada cuándo |
|-------|--------|-------|-------------|
| **Runtime (componente)** | Las DLLs + el COM `BrosLMV.clsMain` + el icono | En el equipo (`C:\BrosLMV\`) | **Una vez por equipo** (servidor y cada terminal) |
| **Provisión de empresa** | El botón en el ribbon + las tablas `zzBros*` | En la **base SQL** de la empresa | **Una vez por empresa** (la BD es compartida) |

El **`BrosLMV-Instalador.exe`** hace las dos cosas en un solo flujo. En una **terminal**
sin credenciales SQL, basta con instalar el runtime y cerrar la ventana de provisión.

---

## 1. Requisitos del equipo

| Requisito | Notas |
|-----------|-------|
| Windows con **.NET Framework 4.8** | Ya viene en Windows 10/11 y Server 2016+ |
| Runtime Python | No se instala aparte: CPython embeddable viaja dentro del instalador. |
| **CONTPAQi Comercial PRO** | Carpeta `C:\Program Files (x86)\Compac\ComercialSP\` |
| Permisos de **Administrador** | El instalador los pide solo (UAC) |
| Acceso a **SQL Server** | Solo para provisionar empresas (el servidor/admin) |

> No hace falta Visual Studio ni el .NET SDK: todo viaja **dentro** del `.exe`.

---

## 2. Lo que entregas

Un solo archivo (o dos, si vas a desinstalar):

| Archivo | Para qué |
|---------|----------|
| **`BrosLMV-Instalador.exe`** | Instala el runtime y abre el GUI de provisión. |
| **`BrosLMV-Desinstalador.exe`** | Quita BrosLMV de las empresas y/o del equipo. |

Ambos son **autocontenidos**: no hay que copiar carpetas ni DLLs sueltas.

---

## 3. Instalar (paso a paso)

1. Copia **`BrosLMV-Instalador.exe`** al equipo y haz **doble clic**.
2. Acepta **UAC** (se necesita admin para registrar el componente).
3. En la **pantalla de bienvenida**, presiona **Instalar**. Esto:
   - Crea `C:\BrosLMV\{bin, bin\x86, host, workers, runtimes, scripts, logs, data}`.
   - Copia las 15 DLLs del addon + el nativo `x86\SQLite.Interop.dll`.
   - Copia `BrosLMV.Host` y CPython embeddable a `C:\BrosLMV\runtimes\python`.
   - Copia el icono `BrosLMV.ico` a `…\ComercialSP\Icons\`.
   - Registra el COM `BrosLMV.clsMain` (RegAsm 32 bits + mapeo en `WOW6432Node`).
4. Al terminar se abre el **GUI de provisión** (ver §4).

> **Terminal sin SQL:** cuando se abra el GUI, ciérralo. El runtime ya quedó
> instalado; eso es todo lo que necesita una terminal.

---

## 4. Provisionar empresas (GUI)

En la ventana que abre el instalador (o ejecutándolo de nuevo):

1. Escribe **Servidor\Instancia**, **Usuario** y **Contraseña** → **Probar conexión**.
2. Se listan las **empresas Comercial Start/Pro** de la instancia, con su estado
   (**Pendiente** / **Ya instalado**).
3. Marca las que quieras y presiona **Instalar seleccionadas** (o **Marcar pendientes**
   para todas las que falten). Es **idempotente**: las ya instaladas no se duplican.
4. **Reinicia CONTPAQi** en las terminales → aparece el botón **Consola BrosLMV** en
   la pestaña **Soluciones LMV**.

> La conexión de los scripts es **automática** (reutiliza la de CONTPAQi); no se
> configuran credenciales por empresa. El filtro detecta solo Comercial Start/Pro
> (excluye Contabilidad/Nómina) leyendo el esquema; **tú** decides con las casillas.

### Empresa nueva más adelante
Vuelve a abrir el instalador (o el GUI), conéctate y provisiona **solo** la empresa
nueva. No reinstala archivos de más y no duplica nada.

---

## 5. Servidor + terminales (red)

| En el servidor | En cada terminal |
|----------------|------------------|
| `BrosLMV-Instalador.exe` (runtime) + provisionar empresas | `BrosLMV-Instalador.exe` (runtime) → cerrar el GUI |

El **runtime** debe estar en **cada equipo** que abra CONTPAQi (el código corre
local). La **provisión** se hace **una sola vez por empresa** (la BD es compartida):
todas las terminales ven el botón al reiniciar.

---

## 6. Desinstalar

Ejecuta **`BrosLMV-Desinstalador.exe`** (pide UAC). Tiene dos acciones:

- **Quitar de empresas:** conéctate a SQL → lista las empresas **con** BrosLMV →
  marca y **Quitar de seleccionadas**. Elimina el botón, el grupo y las tablas
  `zzBros*` (incluye los scripts guardados).
- **Quitar BrosLMV de este equipo:** elimina por completo `C:\BrosLMV`, des-registra
  el COM y borra el icono. **No deja las DLLs en el equipo.**

> Para un retiro total: primero "Quitar de empresas" (en el servidor, una vez) y
> luego "Quitar de este equipo" en cada equipo. Reinicia CONTPAQi.

---

## 7. Verificación rápida (end-to-end)

1. CONTPAQi → pestaña **Soluciones LMV** → botón **Consola BrosLMV** → abre la consola.
2. Abre `DIAGNOSTICO.csx` y presiona **Ejecutar (F5)**: debe reportar la conexión
   automática (`Conexión viva (grid/DataLayer): SI`) y el nombre de la base activa.
3. Abre `EJEMPLO_suma.csx` y ejecútalo: debe mostrar un total.

---

## 8. Problemas frecuentes

| Síntoma | Solución |
|---------|----------|
| No aparece el botón | ¿Provisionaste la BD de esa empresa? ¿Reiniciaste CONTPAQi? |
| El GUI no lista empresas | Revisa servidor/usuario/contraseña; el usuario SQL debe ver las BDs (sysadmin). |
| "No se pudo borrar C:\BrosLMV" al desinstalar | Cierra CONTPAQi y reintenta (la DLL queda en uso). |
| El botón aparece pero no hace nada | Registro COM incompleto; reinstala con `BrosLMV-Instalador.exe`. |
| Un script dice "No hay conexión disponible" | Corre `DIAGNOSTICO.csx`; como respaldo edita `C:\BrosLMV\bin\broslmv_conn.txt`. |

---

Crear botones y la API de `ctx`: [`MANUAL.md`](MANUAL.md). Compilar/modificar el
código: [`DESARROLLO.md`](DESARROLLO.md). Blueprint técnico: [`ESPECIFICACION.md`](ESPECIFICACION.md).
