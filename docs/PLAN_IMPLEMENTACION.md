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

#### T0.1 — Sincronizar versión desplegada (2.33.5 → 2.33.7)

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

- **Qué:** apuntar todo al nuevo `localhost\compac` y decidir el destino del laboratorio.
- **Por qué (H2, D1):** la regla de oro del proyecto exige que los `.md` basten para retomar; hoy mandan a un servidor que no existe.
- **Cómo:**
  1. `docs/ESTADO.md` §"Recordatorios de entorno": reemplazar `sqlcmd -S ".\COMPAC2022" -E` por `localhost\compac` (con nota de la migración 2026-07-22).
  2. `Entrenamiento/empresa_base_cp/CLAUDE.md` línea 12-13 y `Entrenamiento/comercial_ia_auditoria/AGENTS.md` §2: actualizar instancia; marcar la IP vieja como histórica.
  3. **Decisión requerida del usuario:** ¿se restaura la BD `Comercial_IA_Auditoria` en `localhost\compac` (hay backup?) o se declara el laboratorio en pausa? Si se restaura, correr `tools\powershell\Test-Connection.ps1` para validar. Si no, agregar nota "LABORATORIO EN PAUSA — restaurar backup antes de experimentar".
- **Esfuerzo: XS-S. Riesgo: nulo.**
- **Criterio de aceptación:** ninguna instrucción operativa del repo apunta a `COMPAC2022` sin nota de obsolescencia.

#### T0.3 — Entrada de estado v2.33.x en ESTADO.md

- **Qué:** agregar la sección "Estás aquí (2026-07-16, v2.33.7)".
- **Por qué (H6, D2):** la regla de oro lo exige; las versiones 2.33.5-2.33.7 trajeron la pestaña "Soluciones LMV", nombres con versión en dist, retry de Query y fix de foco WebView2 — y nadie lo anotó en el punto de reentrada.
- **Cómo:** redactar entrada siguiendo el formato de las anteriores (ver líneas 42-86 de `ESTADO.md`), citando `CHANGELOG.md` [2.33.6]/[2.33.7] y las dos entradas sin versión del 2026-07-15.
- **Esfuerzo: XS. Riesgo: nulo.**

#### T0.4 — Higiene de credenciales

- **Qué:** sacar la cuenta SA del circuito diario.
- **Por qué (H8):** SA en texto plano + `zzBrosScript` ejecutando código arbitrario = riesgo total. Además el GUI de provisión pide credenciales en cada instalación.
- **Cómo:**
  1. Rotar el password de SA.
  2. Crear login dedicado `broslmv_admin` con `db_datareader`+`db_datawriter` solo en las empresas a provisionar (sin sysadmin) y documentarlo en `INSTALACION.md` (D6).
  3. Verificar que la provisión funciona con ese login (el script de provisión hace CREATE TABLE en la empresa — requiere `db_ddladmin` también; documentar los 3 roles exactos).
- **Esfuerzo: S. Riesgo: bajo** (probar en una empresa primero).
- **Criterio:** provisión exitosa con `broslmv_admin` sin sysadmin; SA solo para emergencias.

#### T0.5 — Archivar scripts de empresas ausentes

- **Qué:** mover `C:\BrosLMV\scripts\EmpresaD\` y `EmpresaC\` a `C:\BrosLMV\scripts\_archivo\` y registrar el inventario (D7) en `ESTADO.md`.
- **Por qué (H4):** esas BDs no existen en este servidor; los scripts huérfanos confunden ("¿esto está vivo?") y el `.legacy` de EmpresaA igual. **No se borra nada** — solo se archiva y se documenta.
- **Esfuerzo: XS. Riesgo: nulo** (si la BD vuelve, se des-archiva).
- **Nota:** confirmar con el usuario antes — EmpresaD puede ser un cliente activo en OTRO servidor.

---

### FASE 1 — Consolidación técnica (semanas 1-2)

#### T1.1 — `ctx.dashboard()`: helper de reportes WebView2 con assets compartidos ⭐ (mayor ROI)

> **Estado (2026-07-29): pasos 1-4 y 6 implementados en v2.34.0**, detonado por un bug real
> en producción de `ReporteXVehiculo` (ruta de assets con el nombre de empresa fijo a
> mano — ver `CHANGELOG.md` [2.34.0] y `DASHBOARDS_HTML.md`). Falta el paso 5 (migrar
> `ReporteXVehiculo` y los otros 3 reportes para que usen `ctx.dashboard()` en vez de su
> propia carpeta `_assets\`) — el bug puntual ya está corregido (usa `ctx.empresa`), pero
> sigue con su carpeta de assets propia hasta que se migre. **Sin probar en CONTPAQi real
> todavía** — verificado con datos reales (hasta 3,000 filas) en navegador aislado, pero no
> dentro de WebView2/Comercial en vivo.

- **Qué:** abstraer el patrón "plantilla HTML + app.js + xlsx.bundle.js" en una sola librería del runtime y exponerlo como una llamada.
- **Por qué (H5):** el patrón se repitió 4 veces con el mismo archivo de 425 KB copiado por reporte (~1.7 MB duplicados). El reporte #5 hoy nace copiando carpetas; con el helper nace con 50 líneas de Python. Es el caso de uso #1 del producto en producción (los 4 reportes vivos son dashboards).
- **Cómo:**
  1. ✅ **Fuente versionada** `instalador\assets\dashboard\`: `xlsx.bundle.js` (+ `NOTICE.md` de licencia), `dashboard_base.html` (tabla ordenable + buscador + paginación + exportar), `dashboard.css`. `build\generar_instalador.ps1` (paso 5b) la copia a `instalador\lib\dashboard\` (gitignorado, "binarios regenerables") → el instalador la despliega a `C:\BrosLMV\lib\dashboard\`. **Ojo:** `instalador\lib\` está en `.gitignore` — cualquier archivo fuente nuevo va en `instalador\assets\`, nunca directo en `instalador\lib\`, o se pierde al hacer commit.
  2. ✅ `src/HostClient.cs` (`RenderUiHtml`): `SetVirtualHostNameToFolderMapping("broslmv.local", Rutas.Lib, Allow)`.
  3. ✅ `workers/python/broslmv/ctx.py`: `ctx.dashboard(title, data, columns=None, width=1000, height=700, modal=True)` — gzip+base64 automático (nunca choca con el límite de 2MB).
  4. ✅ **Sincronizadas las 3 copias de ctx.py** (repo + `C:\BrosLMV\workers\python\` + `C:\BrosLMV\host\workers\python\`).
  5. ⏳ Migrar `ReporteXVehiculo` (EmpresaA) como piloto; si queda igual, migrar los otros 3. **Pendiente** — requiere probar en CONTPAQi real.
  6. ✅ Documentado: `docs/DASHBOARDS_HTML.md` (guía completa nueva) + `MANUAL.md` §9.4 + referencia cruzada.
- **Archivos:** `src/HostClient.cs`, `src/Rutas.cs`, `workers/python/broslmv/ctx.py`, `instalador\assets\dashboard\*`, `build\generar_instalador.ps1`, docs.
- **Esfuerzo: L. Riesgo: medio** (tocar el pipeline de show_html — probar con reporte real).
- **Criterio de aceptación:** ReporteXVehiculo migrado funciona idéntico, sin `xlsx.bundle.js` en su carpeta de assets.

#### T1.2 — Gestor de ribbon como feature del núcleo

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

- **Qué:** escribir cada ejecución (botón y consola) también en la tabla central de la empresa.
- **Por qué (H3):** la tabla existe desde la provisión con el esquema exacto y nadie la llena. La auditoría local SQLite se fragmenta por terminal y se pierde al reinstalar. Para un cliente con control interno (o una auditoría fiscal), "quién ejecutó qué y cuándo" debe responderse a nivel empresa.
- **Cómo:**
  1. En `src/Datos.cs` → `RegistrarEjecucion(...)`: tras el INSERT en SQLite (comportamiento actual, se conserva), hacer INSERT best-effort en `zzBrosAuditoria` por la conexión viva: `Equipo = Environment.MachineName`, `Error` truncado a 4000 chars.
  2. **Best-effort estricto:** try/catch silencioso — si la empresa no está provisionada, la tabla no existe o no hay permiso, la ejecución NUNCA falla por auditoría.
  3. Consola → ventana Historial: pestaña nueva "Auditoría (empresa)" que lee la tabla central con filtros (fecha, usuario, AppKey, estado).
- **Archivos:** `src/Datos.cs`, `src/Consola.cs`, `MANUAL.md` (D5), `CHANGELOG.md`.
- **Esfuerzo: M. Riesgo: bajo** (100% aditivo).
- **Criterio:** ejecutar un botón en EmpresaA desde la consola y desde el ribbon, y ver ambas filas en `zzBrosAuditoria` con `Equipo` y `Origen` correctos.

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
> migración corrida contra `GGV_DE_MEXICO` y `Distribuciones_Candelas` (idempotencia
> verificada corriéndola dos veces). Hash verificado por comparación cruzada contra
> `SHA256` de PowerShell — mismo algoritmo, mismo resultado. **Pendiente real:** el
> aviso de "script modificado" todavía no escribe en `zzBrosAuditoria` (eso llega con
> T2.1, siguiente en la cola) — por ahora solo el `MessageBox` al usuario. Tampoco se
> probó el flujo completo dentro de CONTPAQi real (clic en un botón manipulado a
> propósito) — solo se verificó la lógica SQL y que compila.

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
- **Cómo:**
  1. Definir el subconjunto **job-safe**: scripts que solo usan `ctx.query/scalar/read_excel/write_excel/correo` — NUNCA `ctx.erp` ni el grid (fuera de Comercial no hay XEngine ni conexión viva; se usa `OpenConn` con el archivo de conexión de respaldo que ya lee `Rutas.cs`).
  2. Marcador `# job: safe-offline` en la primera línea; el runner lo valida estáticamente antes de ejecutar.
  3. `BrosLMV.Runner.exe` (consola, net48): `--empresa EmpresaA --appkey REPORTE_EJECUTIVO --salida C:\reportes`. Acciones de salida: guardar Excel/PDF, SMTP.
  4. Documentar con receta de Task Scheduler.
- **Esfuerzo: XL. Riesgo: medio.**
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

- **Qué:** GitHub Actions: build de `src` + `host` + instaladores + verificación documental.
- **Por qué:** la regla de oro se rompe por olvido humano (H6 lo demuestra). Un script de 30 líneas la hace cumplir sola.
- **Cómo:** workflow que falla si cambia `AssemblyVersion` sin entrada correspondiente en `CHANGELOG.md` y `src/assets/notas_version.html`; compila todo; publica `dist\` como artefacto de release.
- **Esfuerzo: M. Riesgo: nulo.**

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

```
                Esfuerzo bajo          Esfuerzo alto
Impacto alto | T0.1 T0.2 T0.3 T1.2   | T1.1 ⭐ T2.1 ⭐ T3.1 ⭐ T4.1
             | T2.2 T0.4             | T3.2 T3.3
Impacto med. | T0.5 T1.4 T4.2        | T2.3
Impacto bajo | T4.3 (continuo)       | Fase 5 (backlog)
```

**Orden de ejecución recomendado (semanas):**

| Semana | Trabajo |
|---|---|
| 0 (ahora) | T0.1 → T0.2 → T0.3 → T0.5 (+ decisión laboratorio) |
| 1 | T0.4, T1.2 (gestor ribbon), inicio T1.1 (dashboard) |
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
