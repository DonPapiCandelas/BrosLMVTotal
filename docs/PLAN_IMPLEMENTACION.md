# BrosLMV — Plan de implementación (post-análisis 2026-07-22)

> Documento generado tras un análisis completo del repositorio, la base de datos
> (`localhost\compac`) y el runtime instalado (`C:\BrosLMV`). Cada punto trae:
> **qué es, por qué (con evidencia), cómo hacerlo paso a paso, archivos afectados,
> esfuerzo, riesgo y criterio de aceptación.**
>
> Estado del análisis: solo lectura en BD (ninguna escritura ejecutada).

---

## 1. Hallazgos del análisis (qué encontré, con evidencia)

### 1.1 Estado real verificado

| Elemento | Dónde | Valor encontrado |
|---|---|---|
| Versión del código fuente | `src/ClsMain.cs` línea 31 | **2.33.7.0** |
| Versión del runtime instalado | `C:\BrosLMV\bin\BrosLMVClsMain.dll` | **2.33.5.0** ⚠️ |
| Versión del instalador en `dist\` | `BrosLMV-Instalador-2.33.5.exe` | **2.33.5** ⚠️ |
| Empresas provisionadas | `zzBrosInfo` en ambas BDs | **2.33.5**, instalada **hoy 2026-07-22 22:17** |
| Instancia SQL activa | `localhost\compac` = `WIN-1KEA2J5D4JQ\COMPAC` | SQL Server 2022 Developer (16.0.1000.6) |
| CONTPAQi | Procesos `ComercialSP` activos | **Corriendo** durante el análisis |

### 1.2 Hallazgos ordenados por severidad

| # | Hallazgo | Evidencia | Severidad |
|---|---|---|---|
| H1 | **La "trampa del instalador" está viva otra vez**: el código va en 2.33.7 pero runtime y `dist\` van en 2.33.5. Las empresas se provisionaron hoy con una versión 2 atrás. | §1.1 | 🔴 Alta |
| H2 | **La documentación apunta a un servidor que ya no existe**: `ESTADO.md` dice `sqlcmd -S ".\COMPAC2022" -E`; todo `Entrenamiento/` apunta a `.\COMPAC2022` / IP `192.168.122.17:49876`. La BD de laboratorio `Comercial_IA_Auditoria` **no existe** en `localhost\compac`. | `ESTADO.md` línea 290; `Entrenamiento/empresa_base_cp/CLAUDE.md` línea 12-13; `Entrenamiento/comercial_ia_auditoria/AGENTS.md` §2 | 🔴 Alta |
| H3 | **`zzBrosAuditoria` se crea en la provisión pero NADIE escribe en ella**: existe en ambas empresas, con el esquema completo (Fecha, Usuario, Equipo, Modulo, AppKey, Origen, DuracionMs, Filas, Estado, Error) y está **vacía**. Toda la auditoría va a SQLite local (`Datos.RegistrarEjecucion` → `C:\BrosLMV\data\broslmv.db`). | `instalador/sql/provision_empresa.sql` línea 35-36; grep de `zzBrosAuditoria` en `src/` = 0 escrituras; verificado en ambas BDs | 🔴 Alta |
| H4 | **Scripts de empresas que ya no están en este servidor**: `C:\BrosLMV\scripts\EmpresaD\` (4 soluciones con assets) y `EmpresaC\` existen en disco, pero esas BDs no existen en `localhost\compac`. | Listado de `C:\BrosLMV\scripts` vs `sys.databases` | 🟡 Media |
| H5 | **Patrón dashboard duplicado 4 veces**: el mismo `xlsx.bundle.js` de **425,020 bytes** copiado en `ReporteXVehiculo_assets`, `CUENTAS_POR_COBRAR_assets`, `CUENTAS_POR_PAGAR_assets` y `SEGUIMIENTO_OC_assets`, más `index_template.html`/`app.js`/`style.css` por reporte. | Tamaños idénticos en listado de scripts | 🟡 Media |
| H6 | **`ESTADO.md` desactualizado contra su propia regla de oro**: última entrada "Estás aquí" = 2026-07-14 v2.32.0, pero el CHANGELOG ya va en **2.33.7** (2026-07-16). | `ESTADO.md` línea 42 vs `CHANGELOG.md` líneas 11-52 | 🟡 Media |
| H7 | **Advertencia pendiente ya anotada como deuda**: `Entrenamiento/Antioco/ANALISIS_TECNICO.md` (líneas 87-90) dice que el gotcha `IVABase = 0.000001` en partidas con 100% de descuento debe anotarse en `MANUAL.md` como advertencia de `ctx.erp.Timbrar` — no está en el MANUAL. | `ANALISIS_TECNICO.md` §4 | 🟡 Media |
| H8 | **Vector de seguridad abierto**: cualquier login con escritura en la BD puede insertar código arbitrario en `zzBrosScript` que corre dentro del ERP con la conexión viva. No hay hash, firma ni aprobación. Credenciales SA han circulado en texto plano. | Esquema `zzBrosScript` (sin columnas de integridad); análisis de amenazas | 🟡 Media |
| H9 | **Historial de versiones sin UI**: `zzBrosScriptHist` respalda cada versión anterior automáticamente (`Scripting.cs` líneas 799-800) pero no hay forma de verlo ni restaurarlo desde la consola. | `Scripting.cs` §"Almacen de scripts en SQL" | 🟢 Baja |
| H10 | **Gestor de ribbon existe pero solo para un cliente**: `GESTOR_RIBBON.py` (15.5 KB, validado contra la base real) vive solo en la carpeta de EmpresaD; crear botones sigue siendo SQL a mano para todos los demás. | `C:\BrosLMV\scripts\EmpresaD\GESTOR_RIBBON.py` | 🟢 Baja |
| H11 | **Cero pruebas automatizadas** en la solución; la verificación es manual/en vivo. La solución `BrosLMV.sln` no tiene proyecto de tests. | Estructura de la solución | 🟡 Media |
| H12 | **Las 3 copias de `ctx.py`** se sincronizan a mano (gotcha documentado en `ESTADO.md` línea 83-84 y 284). | `ESTADO.md` | 🟢 Baja |

### 1.3 Soluciones vivas detectadas (inventario)

| Empresa | Scripts en BD (`zzBrosScript`) | Scripts en disco | Uso real |
|---|---|---|---|
| `EmpresaA` | `ReporteXVehiculo` (Python, módulo 1262) | `ALTA_VEHICULO.ctx`, `ReporteXVehiculo.py` + assets WebView2 | Dashboard de flota, alta de vehículos |
| `EmpresaB` | `RecepcionOc` (47.7 KB), `REPORTE_EJECUTIVO` (57 KB Python), `FixStatusDelivery1882`, `PRUEBA_QR`, `TITULO_DOCUMENTO` | 8 scripts + assets | Recepciones desde OC, reporte ejecutivo |
| `EmpresaD` | (BD no presente en este servidor) | `CUENTAS_POR_COBRAR`, `CUENTAS_POR_PAGAR`, `SEGUIMIENTO_OC`, `GESTOR_RIBBON` + assets | CXC/CXP, seguimiento, gestor ribbon |
| `ComercialSP`, `Predeterminada` | Sin provisionar | — | Candidatas a sandbox |

---

## 2. Datos que faltan en la documentación

| # | Falta | Dónde debe ir | Por qué importa |
|---|---|---|---|
| D1 | Cadena de conexión actual `localhost\compac` (y que `.\COMPAC2022` quedó obsoleta el 2026-07-22) | `ESTADO.md` §"Recordatorios de entorno" | La regla de oro exige que cualquiera retome el proyecto solo con los `.md`; hoy esas instrucciones llevan a un servidor inexistente |
| D2 | Entrada "Estás aquí" para v2.33.x (show_html activation fix 2.33.7, Query retry 2.33.6, nombre con versión y pestaña "Soluciones LMV" 2.33.5) | `ESTADO.md` | La última entrada es v2.32.0; la regla de oro manda mantenerlo al día |
| D3 | Advertencia `IVABase = 0.000001` en partidas con 100% de descuento antes de timbrar | `MANUAL.md` §6.14 (`ctx.erp.Timbrar`) | Deuda explícita declarada en `ANALISIS_TECNICO.md`; un documento así NO timbra sin el fix |
| D4 | Sección sobre `zzBrosScript`/`zzBrosScriptHist` (almacenamiento en BD, historial automático) | `MANUAL.md` §13 o sección nueva | Los usuarios no saben que sus scripts viven en la BD ni que hay historial |
| D5 | Propósito de `zzBrosAuditoria` (tabla creada en provisión) | `MANUAL.md` + `INSTALACION.md` | Hoy es una tabla fantasma; al implementar A1 (§3) quedará documentada |
| D6 | Cuenta de servicio SQL recomendada (NO usar SA; login de mínimo privilegio) | `INSTALACION.md` | Seguridad básica de despliegue |
| D7 | Inventario de empresas activas y soluciones instaladas por empresa | `ESTADO.md` o doc nuevo | Hoy saber "qué tiene cada cliente" exige entrar BD por BD |
| D8 | Advertencia de las 3 copias de `ctx.py` para quien edita scripts Python | `PYTHON.md` (hoy solo está en `ESTADO.md`) | Quien edita `PYTHON.md` no necesariamente leyó `ESTADO.md` |

---

## 3. Plan por fases

Leyenda de esfuerzo: **XS** < 2h · **S** medio día · **M** 1-2 días · **L** 3-5 días · **XL** 1+ semana.

---

### FASE 0 — Saneamiento inmediato (hacer YA, 1 día total)

> **Nota (2026-07-29): esta fase se escribió el 2026-07-22 contra la versión 2.33.x — el
> código ya va en 2.36.0 + prototipo `BrosLMV.Runner`. Los números de versión de abajo están
> desactualizados a propósito (son la foto del momento), pero el ESTADO real de cada tarea
> SÍ se mantiene al día en su propio banner. Resumen rápido: T0.1 ✅ hecho (aunque con otro
> número de versión), T0.2 ✅ hecho en docs públicas, T0.3 sin objeto (superado por entradas
> más nuevas), T0.4 descartado por decisión del usuario (SA se queda), T0.5 sigue sin tocarse.**

#### T0.1 — Sincronizar versión desplegada (2.33.5 → 2.33.7)

> **Estado (2026-07-29): ✅ HECHO** (con otro número de versión — el problema de fondo, no la
> cifra puntual). La "trampa del instalador" se volvió a dar y se volvió a cerrar el mismo día:
> se instaló el SDK de .NET 8, se regeneró el instalador (`generar_instalador.ps1` +
> `generar_exes.ps1`, 0 errores) y se re-provisionaron `EmpresaA`/`EmpresaB`. Verificado:
> `BrosLMVClsMain.dll` = 2.34.0.0 y `zzBrosInfo.ProvisionVersion` = 2.34.0 en ambas. Ver
> `ESTADO.md` "Estás aquí (2026-07-29, v2.34.0)". Desde entonces cada versión (2.35.0, 2.36.0)
> siguió la misma disciplina de regenerar instalador — no ha vuelto a pasar.

- **Qué:** regenerar instalador y re-provisionar las empresas para que runtime, `dist\` y `zzBrosInfo` queden en 2.33.7.
- **Por qué (H1):** la trampa documentada en `ESTADO.md` ("el commit NO instala solo") está ocurriendo ahora mismo. Las empresas provisionadas hoy quedaron sin el fix de activación de ventanas WebView2 (2.33.7) y el retry de `Query`/`Scalar` (2.33.6) — el segundo afecta directamente a reportes tipo GESTOR_RIBBON.
- **Cómo (paso a paso):**
  1. Avisar: CONTPAQi está corriendo (procesos ComercialSP activos); `generar_instalador.ps1` lo **mata a la fuerza**. Hacerlo fuera de horario o con el usuario avisado.
  2. `.\build\generar_instalador.ps1` → compila addon Release → `instalador\bin`, host y workers.
  3. `.\build\generar_exes.ps1` → genera `dist\BrosLMV-Instalador-2.33.7.exe` (y borra los .exe viejos).
  4. Ejecutar el instalador en el servidor → GUI de provisión → marcar `EmpresaA` y `EmpresaB` → "Instalar seleccionadas" (el instalador ya detecta "Actualizar disponible" desde v2.32.0).
  5. Verificar: `FileVersion` de `C:\BrosLMV\bin\BrosLMVClsMain.dll` = 2.33.7.0 y `SELECT Valor FROM zzBrosInfo WHERE Clave='ProvisionVersion'` = 2.33.7 en ambas empresas.
- **Archivos:** ninguno de código. **Esfuerzo: S. Riesgo: bajo** (mata Comercial).
- **Criterio de aceptación:** las 3 fuentes (DLL, dist, zzBrosInfo) dicen 2.33.7.

#### T0.2 — Actualizar referencias de conexión en la documentación

> **Estado (2026-07-29): ✅ HECHO en la documentación pública** — `ESTADO.md` §"Recordatorios
> de entorno" ya dice `localhost\compac` con nota de que `.\COMPAC2022` quedó obsoleta el
> 2026-07-22. **Pendiente, prioridad baja:** los archivos dentro de `Entrenamiento/` (privado,
> gitignored, no forma parte de la "regla de oro" de docs públicas) todavía mencionan
> `COMPAC2022`/la IP vieja — no se tocaron porque no bloquean a nadie que retome el proyecto
> desde los `.md` públicos. La decisión sobre restaurar `Comercial_IA_Auditoria` (punto 3)
> sigue sin tomarse.

- **Qué:** apuntar todo al nuevo `localhost\compac` y decidir el destino del laboratorio.
- **Por qué (H2, D1):** la regla de oro del proyecto exige que los `.md` basten para retomar; hoy mandan a un servidor que no existe.
- **Cómo:**
  1. `docs/ESTADO.md` §"Recordatorios de entorno": reemplazar `sqlcmd -S ".\COMPAC2022" -E` por `localhost\compac` (con nota de la migración 2026-07-22).
  2. `Entrenamiento/empresa_base_cp/CLAUDE.md` línea 12-13 y `Entrenamiento/comercial_ia_auditoria/AGENTS.md` §2: actualizar instancia; marcar la IP vieja como histórica.
  3. **Decisión requerida del usuario:** ¿se restaura la BD `Comercial_IA_Auditoria` en `localhost\compac` (hay backup?) o se declara el laboratorio en pausa? Si se restaura, correr `tools\powershell\Test-Connection.ps1` para validar. Si no, agregar nota "LABORATORIO EN PAUSA — restaurar backup antes de experimentar".
- **Esfuerzo: XS-S. Riesgo: nulo.**
- **Criterio de aceptación:** ninguna instrucción operativa del repo apunta a `COMPAC2022` sin nota de obsolescencia.

#### T0.3 — Entrada de estado v2.33.x en ESTADO.md

> **Estado (2026-07-29): sin objeto (superado).** Nunca se escribió la entrada específica
> "v2.33.7" pedida aquí, pero `ESTADO.md` desde entonces sumó entradas nuevas y más completas
> (v2.34.0 y la de "2026-07-29, tarde" con T2.1/T2.3/T3.3) que ya cubren y superan lo que esta
> tarea buscaba (mantener el punto de reentrada al día). No vale la pena escribir la entrada
> vieja en retrospectiva — el hueco de fondo (H6: la regla de oro exige mantener `ESTADO.md`
> al día) está resuelto por la disciplina que se siguió después, no por esta tarea puntual.

- **Qué:** agregar la sección "Estás aquí (2026-07-16, v2.33.7)".
- **Por qué (H6, D2):** la regla de oro lo exige; las versiones 2.33.5-2.33.7 trajeron la pestaña "Soluciones LMV", nombres con versión en dist, retry de Query y fix de foco WebView2 — y nadie lo anotó en el punto de reentrada.
- **Cómo:** redactar entrada siguiendo el formato de las anteriores (ver líneas 42-86 de `ESTADO.md`), citando `CHANGELOG.md` [2.33.6]/[2.33.7] y las dos entradas sin versión del 2026-07-15.
- **Esfuerzo: XS. Riesgo: nulo.**

#### T0.4 — Higiene de credenciales

> **Estado (2026-07-30): ❌ DESCARTADO por decisión explícita del usuario** ("quita el de la
> cuenta SA, siempre va a ser esa"). No se va a crear un login dedicado ni a rotar SA — es
> una decisión operativa del usuario, no una tarea pendiente. Se deja el análisis original
> abajo como referencia histórica de por qué se había propuesto, pero **no se va a hacer**.

- **Qué (descartado):** sacar la cuenta SA del circuito diario.
- **Por qué se había propuesto (H8):** SA en texto plano + `zzBrosScript` ejecutando código arbitrario = riesgo total. Además el GUI de provisión pide credenciales en cada instalación.
- **Cómo (ya no aplica):**
  1. Rotar el password de SA.
  2. Crear login dedicado `broslmv_admin` con `db_datareader`+`db_datawriter` solo en las empresas a provisionar (sin sysadmin) y documentarlo en `INSTALACION.md` (D6).
  3. Verificar que la provisión funciona con ese login (el script de provisión hace CREATE TABLE en la empresa — requiere `db_ddladmin` también; documentar los 3 roles exactos).

#### T0.5 — Archivar scripts de empresas ausentes

> **Estado (2026-07-30): ✅ HECHO — pero BORRADO, no archivado, por decisión explícita del
> usuario.** Se confirmó contra `sys.databases` cuáles carpetas de `C:\BrosLMV\scripts\` no
> tienen base de datos en este servidor (4: dos nombres genéricos de prueba, y dos empresas
> reales sanitizadas en docs públicas — una de ellas es la misma cuyo `GESTOR_RIBBON.py` se
> promovió al núcleo en T1.2 de esta misma sesión). Se preguntó explícitamente si eran
> clientes activos en otro servidor o carpetas viejas; el usuario respondió **"bórralas"**
> (no "archívalas", que era la opción presentada) — se tomó la precaución extra de
> respaldarlas primero en un `.zip` antes de borrar, por si acaso: <br>
> `C:\BrosLMV\_respaldo_scripts_borrados_2026-07-30\scripts_huerfanos_2026-07-30.zip` (233 KB).

---

### FASE 1 — Consolidación técnica (semanas 1-2)

#### T1.1 — `ctx.dashboard()`: helper de reportes WebView2 con assets compartidos ⭐ (mayor ROI)

> **Estado (2026-07-30): pasos 1-4 y 6 en v2.34.0; paso 5 HECHO, pero NO como se planeó
> originalmente — ver por qué abajo.** Al revisar los 4 reportes reales para migrarlos se
> encontró que **ya no son candidatos para `ctx.dashboard()`** (el widget de tabla
> genérica): `ReporteXVehiculo` se reconstruyó por completo el 2026-07-29 (antes de esta
> revisión) en un reporte semanal a la medida (RENTAS/SERVICIOS/GASTOS/REFACCIONES por
> día, replicando el Excel de control del cliente) sin export a Excel; los otros 3
> (`CUENTAS_POR_COBRAR`, `CUENTAS_POR_PAGAR`, `SEGUIMIENTO_OC`) tienen semáforo de
> vencimiento, calendario y botones "Ver documento" — funcionalidad que el widget genérico
> de `ctx.dashboard()` no cubre. Forzarlos a la tabla genérica habría sido una
> **regresión real** (perder funcionalidad ya construida y en uso), no una mejora.
>
> **Lo que sí aplicaba y sí se hizo** (el problema de fondo real de H5, no la solución
> literal): `DASHBOARDS_HTML.md` §4 ya documentaba el camino correcto para reportes a la
> medida — solo las **librerías compartidas pesadas** (`xlsx.bundle.js`, 425 KB) deben
> vivir en `C:\BrosLMV\lib\dashboard\` y referenciarse vía
> `https://broslmv.local/dashboard/xlsx.bundle.js`, nunca copiarse por reporte. Los 3
> reportes de `CUENTAS_POR_*`/`SEGUIMIENTO_OC` sí tenían su propia copia local duplicada
> (confirmado: bytes idénticos entre las 3 copias y la compartida) — se corrigió: se quitó
> `xlsx.bundle.js` de sus 3 carpetas `_assets\` (~1.2 MB liberados) y su `index_template.html`
> ahora apunta al `<script src>` compartido en vez de inlinearlo. `ReporteXVehiculo` (2025)
> ya no tenía este problema — su reconstrucción no usa Excel, solo 3 archivos ligeros.
> **Probado**: un arnés en navegador confirmó que `XLSX` cargado vía `<script src>` genera
> un `.xlsx` real idéntico a como lo hacía inlineado (16,255 bytes, sin errores de consola).
> **Sin probar dentro de CONTPAQi real todavía** — la carga vía `broslmv.local` ya está
> probada en producción desde v2.34.0 (mismo mecanismo que usa `ctx.dashboard()`), pero el
> cambio puntual en estos 3 reportes no se ha visto correr en Comercial en vivo.

- **Qué:** abstraer el patrón "plantilla HTML + app.js + xlsx.bundle.js" en una sola librería del runtime y exponerlo como una llamada.
- **Por qué (H5):** el patrón se repitió 4 veces con el mismo archivo de 425 KB copiado por reporte (~1.7 MB duplicados). El reporte #5 hoy nace copiando carpetas; con el helper nace con 50 líneas de Python. Es el caso de uso #1 del producto en producción (los 4 reportes vivos son dashboards).
- **Cómo:**
  1. ✅ **Fuente versionada** `instalador\assets\dashboard\`: `xlsx.bundle.js` (+ `NOTICE.md` de licencia), `dashboard_base.html` (tabla ordenable + buscador + paginación + exportar), `dashboard.css`. `build\generar_instalador.ps1` (paso 5b) la copia a `instalador\lib\dashboard\` (gitignorado, "binarios regenerables") → el instalador la despliega a `C:\BrosLMV\lib\dashboard\`. **Ojo:** `instalador\lib\` está en `.gitignore` — cualquier archivo fuente nuevo va en `instalador\assets\`, nunca directo en `instalador\lib\`, o se pierde al hacer commit.
  2. ✅ `src/HostClient.cs` (`RenderUiHtml`): `SetVirtualHostNameToFolderMapping("broslmv.local", Rutas.Lib, Allow)`.
  3. ✅ `workers/python/broslmv/ctx.py`: `ctx.dashboard(title, data, columns=None, width=1000, height=700, modal=True)` — gzip+base64 automático (nunca choca con el límite de 2MB).
  4. ✅ **Sincronizadas las 3 copias de ctx.py** (repo + `C:\BrosLMV\workers\python\` + `C:\BrosLMV\host\workers\python\`).
  5. ✅ **Reinterpretado**: no se migró ningún reporte a `ctx.dashboard()` (ninguno de los 4
     encaja ya en el widget genérico — ver banner de arriba). Se aplicó `DASHBOARDS_HTML.md`
     §4 en su lugar: `CUENTAS_POR_COBRAR`/`CUENTAS_POR_PAGAR`/`SEGUIMIENTO_OC` ya no traen su
     propia copia de `xlsx.bundle.js`, referencian la compartida.
  6. ✅ Documentado: `docs/DASHBOARDS_HTML.md` (guía completa nueva) + `MANUAL.md` §9.4 + referencia cruzada.
- **Archivos:** `src/HostClient.cs`, `src/Rutas.cs`, `workers/python/broslmv/ctx.py`, `instalador\assets\dashboard\*`, `build\generar_instalador.ps1`, docs, y los scripts en `C:\BrosLMV\scripts\GRUPOMETALMECANICA\` (fuera del repo — runtime local).
- **Esfuerzo: L. Riesgo: medio** (tocar el pipeline de show_html — probar con reporte real).
- **Criterio de aceptación (reinterpretado):** ningún reporte de producción trae su propia copia de `xlsx.bundle.js` — logrado para los 3 que lo necesitaban; `ReporteXVehiculo` (2025) ya no usa Excel, no aplica.

#### T1.2 — Gestor de ribbon como feature del núcleo

> **Estado (2026-07-30): ✅ HECHO (v2.41.0).** `GESTOR_RIBBON.py` se encontró viviendo solo
> en la carpeta de una empresa (`GRUPOMETALMECANICA` — nombre real, sanitizado en docs
> públicas); se genericizó (quitado el nombre de esa empresa del encabezado, corregidas 2
> referencias a documentación que no existía en este proyecto) y se copió a
> `instalador\scripts\GESTOR_RIBBON.py` (compartido, no por empresa). Botón "Gestor de
> Ribbon" agregado en `provision_empresa.sql` junto al de la Consola, mismo patrón
> idempotente/adaptable. **Bug real encontrado y corregido en el camino:** `Instalar.ps1`
> (la vía de instalación manual/scriptada, documentada en `INSTALACION.md`) solo copiaba
> `.ctx`/`.csx` a `C:\BrosLMV\scripts\` — nunca copiaba `.py` ni `.sql`, así que todas las
> plantillas Python que ya vivían en `instalador\scripts\` nunca llegaban por esa vía a una
> instalación nueva. El instalador GUI real (`RuntimeInstaller.cs`, lo que la mayoría de la
> gente usa) NO tenía este bug (copia todo sin filtrar por extensión) — el problema era solo
> en la vía documentada como alternativa.

- **Qué:** promover `GESTOR_RIBBON.py` a scripts compartidos (raíz `C:\BrosLMV\scripts\`) para que esté disponible en TODAS las empresas, con botón propio en la pestaña "Soluciones LMV".
- **Por qué (H10):** crear un botón hoy exige SQL a mano contra `engRibbonControl` (`plantilla_crear_boton.sql`). El gestor ya está validado en producción y tiene la restricción de seguridad correcta (solo toca `ControlExecute LIKE 'BrosLMV.%'`). Es valor ya construido, solo hay que distribuirlo.
- **Cómo:**
  1. Quitar del encabezado la marca "EmpresaD" y parametrizar lo que sea específico.
  2. Copiar a `instalador\scripts\GESTOR_RIBBON.py` (compartido) para que el instalador lo distribuya.
  3. Agregar el botón en `provision_empresa.sql` junto al de la Consola (sección §2b, pestaña "Soluciones LMV").
  4. Documentar en `MANUAL.md` §4 ("Cómo crear un botón nuevo" → ahora con UI).
- **Esfuerzo: S-M. Riesgo: bajo.**
- **Criterio:** crear un botón nuevo en una empresa recién provisionada sin escribir SQL.

#### T1.3 — Paquetes `.bros` (exportar/importar soluciones)

> **Estado (2026-07-29): ✅ HECHO (v2.37.0), validado con arnés de pruebas real contra
> `EmpresaB` — 15/15 checks, incluyendo el caso de escape más riesgoso (comillas + ñ/acentos
> en el manifiesto) y asset anidado sobreviviendo el ZIP. Motivado por una necesidad real e
> inmediata: mover un botón entre bases de datos de cliente.** El manifiesto quedó más simple
> que lo planeado abajo (sin el array `archivos` ni `lenguaje` — innecesarios: el propio
> contenido de `assets\` en el ZIP ya dice qué archivos hay, y el lenguaje se detecta del
> código igual que siempre). **Pendiente:** confirmar haciendo clic de verdad en la Consola
> dentro de CONTPAQi (el arnés prueba la lógica, no los diálogos de Windows Forms); crear el
> botón del ribbon sigue siendo manual a propósito (copia el SQL al portapapeles, no lo corre
> solo). Detalle completo: `CHANGELOG.md` [2.37.0], `MANUAL.md` "📦 Paquetes .bros".

- **Qué:** formato ZIP único (script + assets + manifiesto) para mover soluciones entre empresas y máquinas.
- **Por qué:** los scripts viven en BD pero **los assets viven en disco por máquina** — en multi-terminal, el reporte de EmpresaA solo funciona en el equipo donde se creó. Además es el habilitador del marketplace futuro (Fase 5).
- **Cómo:**
  1. Manifiesto `paquete.json`: `{ appKey, nombre, lenguaje, modulo, versionMinima, archivos: [...] }`.
  2. Consola → menú Scripts → "Exportar paquete…": lee `zzBrosScript` + carpeta `<AppKey>_assets\` si existe → ZIP.
  3. "Importar paquete…": valida `versionMinima` contra `zzBrosInfo.ProvisionVersion`, upsert vía `BrosGuardar` (queda en hist), extrae assets a `scripts\<EMPRESA>\<AppKey>_assets\`, y ofrece crear el botón del ribbon (T1.2).
  4. Todo en `src/Consola.cs` (UI) + `src/Scripting.cs` (lógica); `System.IO.Compression` ya está en .NET 4.8.
- **Esfuerzo: M-L. Riesgo: bajo-medio.**
- **Criterio:** exportar `REPORTE_EJECUTIVO` de EmpresaB e importarlo en EmpresaA funcionando con assets.

#### T1.4 — Versionado visible de scripts (historial + diff + restaurar)

> **Estado (2026-07-29): ✅ HECHO (v2.40.0), probado en vivo con 15 verificaciones contra
> `EmpresaB` (datos) + 6/7 contra el algoritmo de diff aislado (el "fallo" fue de la
> aserción de prueba, no del código — ver `CHANGELOG.md`). Motivado directamente por el
> usuario preguntando cómo respaldar un botón antes de modificarlo, y pidiendo explícitamente
> "algo épico y súper funcional" con margen para proponer extras — se preguntó qué extras
> incluir (multi-select) antes de tocar código, y se implementaron los 4 que eligió: nombre
> real de usuario, exportar una versión vieja como `.bros`, etiquetar versiones, y purga
> manual protegiendo las etiquetadas.**

- **Qué:** UI en la consola para ver `zzBrosScriptHist`, comparar versiones y restaurar.
- **Por qué (H9):** el historial ya se guarda automáticamente en cada `BrosGuardar` — es valor pagado y no expuesto. Restaurar con un clic salva demos y errores en producción (el caso "dañé el script del cliente" se resuelve en 10 segundos).
- **Cómo:**
  1. `src/Scripting.cs`: `BrosHistListar(appKey)` (id, Fecha, Usuario, LEN(Codigo)) y `BrosHistLeer(id)`.
  2. `src/Consola.cs`: ventana "Historial de versiones" — lista a la izquierda, dos editores Scintilla lado a lado (actual vs seleccionada) con diff por líneas (algoritmo simple de LCS; no hace falta librería).
  3. Botón "Restaurar": pasa por `BrosGuardar` (la versión actual queda respaldada a su vez — restauración reversible).
- **Esfuerzo: M-L. Riesgo: bajo.**
- **Criterio:** restaurar una versión vieja de `RecepcionOc` y verificar en `zzBrosScriptHist` que la operación quedó registrada.

---

### FASE 2 — Gobernanza y seguridad (semana 3)

#### T2.1 — Auditoría central en `zzBrosAuditoria` ⭐

> **Estado (2026-07-29): escritura HECHA (v2.36.0), lectura/UI pendiente.** Compilado con
> 0 errores. Probado el `INSERT` real contra `zzBrosAuditoria` en `EmpresaA` (fila de
> prueba insertada, verificada, y borrada). **Falta el paso 3** (pestaña "Auditoría
> (empresa)" en el Historial de la Consola) — hoy los datos ya se escriben pero no hay
> forma de verlos desde la UI, solo por SQL directo. No probado el flujo completo dentro
> de CONTPAQi real (solo la escritura SQL simulada).

- **Qué:** escribir cada ejecución (botón y consola) también en la tabla central de la empresa.
- **Por qué (H3):** la tabla existe desde la provisión con el esquema exacto y nadie la llena. La auditoría local SQLite se fragmenta por terminal y se pierde al reinstalar. Para un cliente con control interno (o una auditoría fiscal), "quién ejecutó qué y cuándo" debe responderse a nivel empresa.
- **Cómo:**
  1. ✅ `src/Datos.cs` → `RegistrarEjecucion(...)` ahora acepta un `ScriptContext ctx` opcional; tras el INSERT en SQLite (se conserva igual), hace INSERT best-effort en `zzBrosAuditoria` por la conexión viva: `Equipo = Environment.MachineName`, `Error` truncado a 4000 chars. Los 4 puntos de llamada (`ClsMain.cs` ×3: botón C#/Python/SQL, `Consola.cs` ×1) ya pasan el `ctx`.
  2. ✅ **Best-effort estricto:** try/catch silencioso — si la empresa no está provisionada, la tabla no existe, no hay permiso, o el script está en modo solo-lectura, la ejecución NUNCA falla por auditoría.
  3. ⏳ Consola → ventana Historial: pestaña nueva "Auditoría (empresa)" que lee la tabla central con filtros (fecha, usuario, AppKey, estado). **No construida todavía.**
  4. ✅ Bono: la advertencia de integridad (T2.3) también escribe aquí ahora (`Origen='integridad'`, `Estado='ADVERTENCIA'`) — cierra el pendiente que había quedado en T2.3.
- **Archivos:** `src/Datos.cs`, `src/ClsMain.cs`, `src/Consola.cs`. Falta: UI en `src/Consola.cs`, `MANUAL.md` (D5 — documentar el propósito de la tabla para el usuario final).
- **Esfuerzo: M. Riesgo: bajo** (100% aditivo).
- **Criterio real, pendiente de probar:** ejecutar un botón en una empresa desde la consola y desde el ribbon **dentro de CONTPAQi real**, y ver ambas filas en `zzBrosAuditoria` con `Equipo` y `Origen` correctos.

#### T2.2 — Modo solo-lectura por usuario

- **Qué:** forzar `SoloLectura` por usuario vía `zzBrosPref` (la tabla ya existe: Usuario/Tipo/Valor).
- **Por qué (H8 mitigación):** hoy cualquier script puede escribir si el operador tiene permiso en Comercial. Un almacenista debería poder correr reportes pero no `RecepcionOc`.
- **Cómo:**
  1. Convención: `zzBrosPref` con `Tipo='SoloLectura'`, `Valor='1'` por `Usuario` (el `ctx.UserID` ya llega).
  2. En `Scripting.cs`, al construir el `ScriptContext`: si la pref existe, `SoloLectura = true` forzado (incluso para botones del ribbon; `ctx.NonQuery` ya se bloquea en solo-lectura).
  3. Documentar en `MANUAL.md` §12 con el INSERT de ejemplo.
- **Esfuerzo: S-M. Riesgo: bajo.**
- **Criterio:** usuario marcado ejecuta `REPORTE_EJECUTIVO` (OK) y `RecepcionOc` se bloquea con mensaje claro.

#### T2.3 — Integridad de scripts (hash + aprobación ligera)

> **Estado (2026-07-29): HECHO y desplegado en vivo (v2.35.0).** Compilado con 0 errores,
> migración corrida contra `EmpresaA` y `EmpresaB` (idempotencia
> verificada corriéndola dos veces). Hash verificado por comparación cruzada contra
> `SHA256` de PowerShell — mismo algoritmo, mismo resultado.
> **Actualizado (v2.36.0, T2.1):** el aviso de "script modificado" YA escribe en
> `zzBrosAuditoria` (`Origen='integridad'`, `Estado='ADVERTENCIA'`) — este pendiente quedó
> cerrado ahí, ya no aplica (ver T2.1 abajo). **Pendiente real que sigue abierto:** no se
> probó el flujo completo **dentro de CONTPAQi real** (clic en un botón manipulado a
> propósito, dentro del ribbon) — solo se verificó la lógica SQL, que compila, y (en
> `BrosLMV.Runner`, un camino distinto y ya probado en vivo — ver T3.3) el bloqueo headless.
> El camino específico del `MessageBox` en `ClsMain.cs`/ribbon sigue sin confirmación en vivo.

- **Qué:** detectar (y opcionalmente bloquear) scripts modificados por fuera de la consola.
- **Por qué (H8):** es el vector #1 de accidente/abuso. No necesita criptografía fuerte: necesita trazabilidad y un freno opcional.
- **Cómo:**
  1. ✅ Migración de esquema: `zzBrosScript` + `HashSHA256 char(64) NULL`, `AprobadoPor int NULL`, `AprobadoEl datetime NULL` — en `provision_empresa.sql` (para empresas nuevas) **y** como `ALTER TABLE ... IF COL_LENGTH(...) IS NULL` idempotente en el mismo script (para las ya provisionadas — no hizo falta un `upgrade_*.sql` aparte).
  2. ✅ `BrosGuardar`: calcula y guarda `HashSHA256` (`Scripting.CalcularHashSHA256`, SHA256+UTF8+hex minúsculas).
  3. ✅ Al ejecutar desde ribbon (`ClsMain.cs` → `EjecutarScript`): `ctx.BrosVerificarIntegridad(appKey, codigo)` compara el hash guardado contra el del código que se va a correr. Si no coincide → `MessageBox` de advertencia, **sigue corriendo** (primera versión: solo avisa). Scripts guardados antes de esta versión (`HashSHA256 IS NULL`) no disparan advertencia — no hay línea base con la que comparar.
  4. ✅ `zzBrosPref Tipo='ExigirAprobacion', Valor='1'` → bloquea la ejecución desde el ribbon si `AprobadoEl IS NULL`, con mensaje claro. Botón nuevo **"Aprobar"** en la Consola (`Aprobar()` en `Consola.cs`) registra `AprobadoPor`/`AprobadoEl`.
- **Archivos:** `instalador/sql/provision_empresa.sql`, `src/Scripting.cs`, `src/ClsMain.cs`, `src/Consola.cs`.
- **Esfuerzo: L. Riesgo: medio** (migración de esquema en clientes) — mitigado: idempotente, ya corrida en ambas empresas reales sin incidentes.
- **Criterio de aceptación real, pendiente de probar:** un UPDATE manual de `Codigo` desde SSMS debe disparar la advertencia al ejecutar el botón **desde CONTPAQi real** (no solo verificado por SQL/compilación).

---

### FASE 3 — Producto: no-code y automatización (semanas 4-8)

#### T3.1 — Motor de recetas no-code (MVP con 3 recetas) ⭐ (la meta estratégica)

- **Qué:** implementar `RECETAS_NOCODE.md` (ya diseñado) con MVP de 3 recetas: **"Crear documento a partir de otro"**, **"Ejecutar SQL con tokens"**, **"Exportar selección a Excel"**.
- **Por qué:** convierte a BrosLMV de "herramienta para programadores" en "herramienta para el implementador/contador". Es la diferencia decisiva frente a otras herramientas del mercado que exigen programar (IronPython u otro lenguaje) para lo mismo. Todo el fundamento YA existe: tokens (`ctx.ResolverTokens`, v2.x), builders fieles al nativo (`ctx.erp.NuevoDocumento` + 4 anclas, v2.18.0), grid editable en `ctx.form()` (v2.32.0), pestaña propia "Soluciones LMV" (2.33.5).
- **Cómo (seguir `RECETAS_NOCODE.md` §2 tal cual):**
  1. Registro de recetas (C#, en proceso — no necesita el host Python): interfaz `IReceta { EsquemaConfig; Ejecutar(config, ctx) }`.
  2. Wizard "Nueva automatización" en la pestaña Soluciones LMV: elegir receta → formulario generado desde `EsquemaConfig` → guarda en `zzBrosScript` con marcador `# lang: recipe` + JSON de config.
  3. Al ejecutar un AppKey tipo recipe: el dispatcher (`ClsMain.cs`) detecta el marcador y llama al motor en vez de a Roslyn/Python.
  4. Estructuras de documento destino como JSON (Req→OC, Cot→Pedido, Rem→Fact primero — son los flujos ya validados en producción).
- **Esfuerzo: XL (2-3 semanas). Riesgo: medio-alto.**
- **Criterio:** un contador configura Req→OC sin escribir una línea de código, en una empresa de pruebas.

#### T3.2 — Importador Excel genérico con mapeo visual

- **Qué:** receta no-code "Importar Excel": archivo → mapeo columna→campo → previsualización → carga con reporte de errores por fila.
- **Por qué:** es la petición #1 de cualquier implementación ERP (altas masivas). Ya existe `ctx.read_excel()` (openpyxl, sin COM) y la plantilla `PLANTILLA_EJEMPLO_IMPORTAR_EXCEL_PYTHON.py` — falta el mapeo sin código.
- **Cómo:** destinos iniciales: `orgProduct` (alta de productos), `orgBusinessEntity` (clientes/proveedores), partidas de un documento. Diccionario de campos por destino (nombre amigable → columna real), validaciones (requeridos, tipos, existencias de catálogos), ejecución por lotes con transacción por fila y bitácora de errores descargable.
- **Esfuerzo: L-XL. Riesgo: medio** (escrituras masivas — modo "validar sin guardar" obligatorio).
- **Criterio:** 500 productos cargados desde Excel en sandbox, con 3 filas malas reportadas y sin basura parcial.

#### T3.3 — Programador headless (`BrosLMV.Runner`)

- **Qué:** ejecutar scripts marcados como job-safe por tarea programada de Windows, sin abrir Comercial.
- **Por qué:** los reportes ejecutivos son candidatos naturales a envío automático (7:00 am, correo). Hoy TODO requiere clic dentro de Comercial.
- **Estado: ✅ probado en vivo (2026-07-29).** Bootstrap de XEngine standalone confirmado real: se ejecutó un botón `# job: safe-offline` contra `EmpresaB` (BD real, `localhost\COMPAC`) SIN Comercial abierto -- `ctx.Empresa()` detectó la empresa correcta, el resultado del SELECT salió bien formateado, y quedó registrado tanto en el SQLite local como en `zzBrosAuditoria` central (`Origen='runner-sql'`). El script de prueba y su fila de auditoría se borraron después de confirmar.
- **Hallazgo que cambió el diseño original:** se descubrió (revisando una herramienta de terceros ajena a Comercial, solo para entender el mecanismo — nunca se copió código) que **SÍ es posible crear un `XengineLib.clsMain` vivo y conectado fuera del proceso de `ComercialSP.exe`**: `Type.GetTypeFromProgID("XengineLib.clsMain")` + `Activator.CreateInstance` + setear `OwnedBusinessEntityID`/`InternetConnection`/`LICENCE_CONTPAQ=false` + `DataLayer.CreateConnectionMSSQL(servidor, bd, usuario, contrasena)` + `SetDataLayers()`. Esto **contradice el supuesto original** de que "fuera de Comercial no hay XEngine ni conexión viva" — con XEngine standalone, `ctx.erp` podría funcionar headless también (sin probar aún; no se activó por prudencia, ver más abajo).
- **Cómo (implementado):**
  1. `runner/BrosLMV.Runner.csproj` (consola, net48) enlaza los MISMOS `Scripting.cs`/`Rutas.cs`/`Datos.cs` de `src\` (no los reescribe) — así el motor de scripts (`ScriptContext`, `ScriptRunner`, auditoría) nunca diverge del que corre dentro de Comercial.
  2. `Program.cs`: lee la cadena de conexión de respaldo ya cifrada (`Rutas.ConnStr()`, la misma que configura el instalador) o `--conn` explícito; crea el XEngine standalone (arriba); construye `new ScriptContext(userId, xe)` — el mismo constructor que usa `ClsMain.cs`.
  3. Busca el AppKey en `zzBrosScript` (igual que en Comercial) o como archivo `.sql/.ctx/.csx`.
  4. **Marcador `# job: safe-offline` obligatorio** en las primeras 10 líneas — sin él, el Runner se niega a ejecutar. Sigue siendo el candado principal, independiente de qué API queden disponibles.
  5. Uso: `BrosLMV.Runner.exe --appkey REPORTE_EJECUTIVO --bd EmpresaX [--userid N] [--conn "..."]`.
  6. **`--bd <empresa>` es obligatorio** (salvo con `--conn` completo): dentro de Comercial la base se completa combinando `Rutas.ConnStr()` (servidor+usuario+password) con el `DataLayer` de la sesión viva de la empresa activa; headless no hay sesión de la que inferirla, hay que decirla explícita.
  7. **Corregido en la primera prueba real:** el proyecto compilaba en `AnyCPU`, pero `XEngineLib.dll` solo está registrado como COM de **32 bits** (`WOW6432Node`) — un proceso `AnyCPU` corre en 64 bits en Windows moderno y no encuentra el CLSID (`REGDB_E_CLASSNOTREG`). `BrosLMV.Runner.csproj` ahora fija `<PlatformTarget>x86</PlatformTarget>`.
- **Integridad (T2.3) enchufada en el Runner (2026-07-29), probada en vivo con 3 escenarios contra `EmpresaB`:**
  - Sin `HashSHA256` guardado (script viejo/de archivo): corre normal.
  - `HashSHA256` no coincide (modificado por fuera de la Consola): **BLOQUEA** (exit 7) y registra `Origen='runner-integridad'`, `Estado='ERROR'` en la auditoría — a diferencia de `ClsMain.cs` (donde el usuario ve el MensajeBox y decide seguir), headless no hay nadie mirando, así que aquí el mismatch DETIENE la ejecución en vez de solo avisar.
  - `ExigirAprobacion` activa y sin aprobar: **BLOQUEA** (exit 6), sin ejecutar ni auditar (igual que `ClsMain.cs`).
  - Los 3 escenarios se probaron con datos de prueba insertados y borrados en la misma sesión — no quedó nada en la BD real.
- **Pendiente (no implementado aún):**
  - Python headless: el camino actual de `ctx` Python usa `UiPump` para regresar al hilo de Comercial, que no existe en modo headless — requiere su propio diseño, no incluido en este prototipo.
  - Decidir si `ctx.erp`/grid se habilitan headless dado el hallazgo de arriba, o si se deja bloqueado por diseño (ver riesgo abajo) hasta probarlo a fondo con un script real que escriba.
  - Acciones de salida (guardar Excel/PDF a `--salida`, SMTP) y receta de Task Scheduler — el prototipo hoy solo ejecuta y devuelve texto/exit code.
- **Esfuerzo restante: M. Riesgo: medio** — bajó de XL porque el bloqueador principal (obtener XEngine sin Comercial) ya está resuelto **y probado en vivo** contra una BD real; el riesgo que queda es de alcance (decidir `ctx.erp` headless con cuidado — un job desatendido con permisos de escritura es más peligroso que un botón que un humano ve antes de confirmar) y de features de salida (Excel/PDF/SMTP, Task Scheduler).
- **Criterio:** REPORTE_EJECUTIVO generado y enviado por correo sin sesión de Comercial abierta.

---

### FASE 4 — Calidad y continuidad (transversal, empieza en semana 2)

#### T4.1 — Harness de pruebas contra empresa sandbox

- **Qué:** batería automatizada de humo + equivalencia, reusando las herramientas del laboratorio de `Entrenamiento/`.
- **Por qué (H11):** hoy cada release se valida a mano en vivo; el laboratorio ya resolvió la parte difícil (snapshot before/after, `Compare-Documento.ps1`, matriz de equivalencia campo por campo).
- **Cómo:**
  1. Designar sandbox en `localhost\compac`: `ComercialSP` (sin provisionar, sin scripts) o restaurar `Comercial_IA_Auditoria` (ver T0.2).
  2. Batería mínima: crear OC (C# y Python), alta de producto, `ctx.form()` smoke, `show_html` smoke, `read_excel` smoke, timbrado en modo pruebas. Cada caso con verificación SQL automática contra "documento dorado" (`Compare-Documento.ps1`).
  3. `build\probar_humo.ps1` → corre todo y da verde/rojo. Regla: **no se corre `generar_exes.ps1` sin humo en verde**.
- **Esfuerzo: XL inicial, luego incremental. Riesgo: bajo.**
- **Criterio:** checklist de release automatizado al 80%.

#### T4.2 — CI ligero con guardián de la regla de oro

> **Estado (2026-07-30): ✅ HECHO — versión reducida.** `.github\workflows\ci.yml`, dos
> jobs: **regla-de-oro** (`build\verificar_regla_de_oro.ps1` — la versión actual en
> `src\ClsMain.cs` SIEMPRE debe tener su entrada en `CHANGELOG.md` y `notas_version.html`;
> probado local con un caso roto a propósito — falla correctamente) y **build** (compila
> addon + `BrosLMV.Runner` + host v3.0 con los mismos comandos `dotnet build` que ya se
> usaron a mano toda la sesión — probados localmente, 0 errores). **No incluye** compilar
> los instaladores (`Empresas`/`Desinstalador` requieren el addon ya empacado en
> `instalador\bin`, un pipeline de varios pasos — fuera de alcance de "CI ligero") ni
> publicar `dist\` como artefacto. **Sin confirmar que corra de verdad en GitHub Actions**
> (no hay forma de ver el resultado del workflow desde aquí — `gh` CLI no está disponible
> en este entorno) — la evidencia es que los mismos comandos exactos ya se probaron en
> local con éxito, en un entorno con las mismas piezas (.NET 8 SDK + paquete
> `Microsoft.NETFramework.ReferenceAssemblies` para los proyectos net48, sin Visual Studio
> completo instalado).

- **Qué:** GitHub Actions: build de `src` + `host` + instaladores + verificación documental.
- **Por qué:** la regla de oro se rompe por olvido humano (H6 lo demuestra). Un script de 30 líneas la hace cumplir sola.
- **Cómo:** workflow que falla si cambia `AssemblyVersion` sin entrada correspondiente en `CHANGELOG.md` y `src/assets/notas_version.html`; compila todo; publica `dist\` como artefacto de release.
- **Esfuerzo: M. Riesgo: nulo.**
- **Pendiente:** confirmar en la pestaña Actions de GitHub que el workflow corre en verde; cubrir instaladores + publicar `dist\` si vale la pena el esfuerzo extra.

#### T4.3 — Reducir bus factor

- **Qué:** walkthroughs por subsistema + glosario COM único.
- **Por qué:** la especificación ya es buena, pero el pipeline Python (`ctx.py` → pipe → `PythonProcess` → `RelayingCallbackSink` → `HostClient`) y los componentes COM de CONTPAQi (`Doc.clsMain`, `LBS.clsMain`, `CFDI3.clsMain`, `XEngine`) están dispersos en 6 documentos.
- **Cómo:** un `docs/PIPELINE_PYTHON.md` con diagrama de secuencia y un `docs/GLOSARIO_COMERCIAL.md`; 1-2 h por semana, incremental.
- **Esfuerzo: continuo. Riesgo: nulo.**

---

### FASE 5 — Plataforma (backlog, post-Fase 3)

| # | Idea | Fundamento |
|---|---|---|
| B1 | Marketplace/librería comunitaria de scripts y recetas (botón "Importar desde la comunidad") | Proyecto GPL-3.0 con gobernanza ya escrita; requiere T1.3 (paquetes .bros) |
| B2 | Suite CFDI: `ctx.erp.RelacionarCFDI(doc, origen, tipo)` (tipo 07 anticipos), timbrado masivo con reporte | Candidato documentado en `ANALISIS_TECNICO.md` §4 |
| B3 | Asistente IA en consola (RAG sobre MANUAL + XENGINE_FUNCIONES + docs de esquema de Entrenamiento), opt-in | El corpus documental ya existe y es excepcional |
| B4 | Power BI / REST read-only bridge sobre las empresas | Salida natural de T3.3 (runner headless) |
| B5 | Sync de preferencias/recientes entre terminales vía `zzBrosPref` | Tabla ya existe |

---

## 4. Matriz de priorización (impacto vs esfuerzo)

> **Foto del 2026-07-22, ya no refleja el orden real de ejecución** (T2.3/T2.1/T3.3/T1.3/T1.4
> se hicieron fuera de este orden; T0.4 se descartó). Se deja como referencia histórica de la
> lógica de priorización original, no como plan vigente — ver el banner de estado de cada
> tarea arriba para lo que de verdad aplica hoy.

```
                Esfuerzo bajo          Esfuerzo alto
Impacto alto | T0.1 T0.2 T0.3 T1.2   | T1.1 ⭐ T2.1 ⭐ T3.1 ⭐ T4.1
             | T2.2                  | T3.2 T3.3
Impacto med. | T0.5 T1.4 T4.2        | T2.3
Impacto bajo | T4.3 (continuo)       | Fase 5 (backlog)
```

**Orden de ejecución original (semanas) — histórico, no vigente:**

| Semana | Trabajo |
|---|---|
| 0 (ahora) | T0.1 → T0.2 → T0.3 → T0.5 (+ decisión laboratorio) |
| 1 | T1.2 (gestor ribbon), inicio T1.1 (dashboard) |
| 2 | T1.1 completo + piloto ReporteXVehiculo, T2.1 (auditoría central), T4.2 (CI) |
| 3 | T1.3 (paquetes .bros), T1.4 (versionado UI), T2.2 (solo-lectura) |
| 4 | T2.3 (integridad), inicio T4.1 (harness: sandbox + 2 humos) |
| 5-8 | T3.1 (no-code MVP) con T3.2 como receta #4; T4.1 crece en paralelo |
| 9+ | T3.3 (jobs), Fase 5 según demanda |

---

## 5. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Tocar el pipeline `show_html` rompe los 4 reportes en producción (T1.1) | Piloto con ReporteXVehiculo en horario sin usuarios; los assets viejos NO se borran hasta validar una semana |
| Migración de esquema en empresas de clientes (T2.3) | Script `upgrade_*.sql` versionado + el instalador ya distingue "Actualizar disponible"; probar primero en sandbox |
| Escrituras masivas del importador (T3.2) | Modo "validar sin guardar" obligatorio, transacción por fila, bitácora de errores, y respetar la escala de 13 niveles (catálogos van por el camino ya documentado en `Entrenamiento/empresa_base_cp`) |
| Jobs headless usados para lo que no son (T3.3) | Validación estática del marcador `# job: safe-offline`; documentación clara del subconjunto permitido |
| Alcance del no-code crece sin freno (T3.1) | MVP cerrado a 3 recetas; todo lo demás entra por el registro de recetas como plugin (que es justamente el diseño) |

---

## 6. Reglas de trabajo durante la ejecución

1. **Regla de oro vigente:** cada cambio de código → `CHANGELOG.md` + `AssemblyVersion` + `notas_version.html` + `.md` afectados, en el mismo commit.
2. **Todo patrón/gotcha descubierto → `MANUAL.md`** bien explicado (regla del 2026-07-01).
3. **Después de cada cambio que deba llegar a instalaciones:** `generar_instalador.ps1` + `generar_exes.ps1` (avisar — mata Comercial).
4. **Sincronizar las 3 copias de `ctx.py`** cuando se toque (D8).
5. **Solo escritura en sandbox** mientras no exista harness (T4.1); respetar el checklist de 30 puntos del `PROMPT_MAESTRO.md` para SQL directo.
6. Actualizar `ESTADO.md` ("Estás aquí") al cerrar cada tarea de este plan.
