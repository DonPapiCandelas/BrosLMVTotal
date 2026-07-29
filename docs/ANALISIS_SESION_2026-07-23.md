# BrosLMV — Notas de sesión de análisis (2026-07-23)

> Documento de continuación. Sesión de **solo análisis** — no se ejecutó ninguna
> tarea del plan ni ninguna escritura en BD. Complementa `PLAN_IMPLEMENTACION.md`
> con lo verificado en vivo y las decisiones tomadas. Para retomar: leer esto +
> `PLAN_IMPLEMENTACION.md` §3 (plan por fases).

---

## 1. Conexión SQL verificada

- **Instancia activa:** `localhost\compac` = `WIN-1KEA2J5D4JQ\COMPAC`, SQL Server 2022
  Developer 16.0.1000.6. Login `SA`. Conexión **probada y funcionando**.
- Comando base de trabajo:
  `sqlcmd -S "localhost\compac" -U SA -P "<pwd>" -C -Q "..."`
- La instancia vieja `.\COMPAC2022` (IP `192.168.122.17:49876`) quedó **obsoleta el
  2026-07-22**. Pendiente: actualizar referencias en `ESTADO.md` y `Entrenamiento/`
  (tarea T0.2).

## 2. Hallazgos del plan verificados contra la BD viva

| Hallazgo | Verificación en vivo (2026-07-23) | Estado |
|---|---|---|
| **H1** versión desfasada | `zzBrosInfo.ProvisionVersion = 2.33.5` en `EmpresaA` y `EmpresaB`, instalada `2026-07-22 22:17`. Código en 2.33.7. | ✅ Confirmado |
| **H3** auditoría fantasma | `zzBrosAuditoria` con esquema completo pero **0 filas** en ambas empresas. | ✅ Confirmado |
| Bases presentes | `EmpresaA`, `EmpresaB` (+ `ComercialSP`, `Predeterminada` sin provisionar). **No existen** `EmpresaD` ni `Comercial_IA_Auditoria`. | ✅ Confirmado |
| Inventario scripts | EmpresaA: `ReporteXVehiculo` (11 KB). EmpresaB: `RecepcionOc` (47 KB), `REPORTE_EJECUTIVO` (57 KB), `FixStatusDelivery1882`, `PRUEBA_QR`, `TITULO_DOCUMENTO`. | ✅ Confirmado |

**Corrección al plan:** el esquema real de `zzBrosScript` **no** tiene columna
`Lenguaje`. Es: `AppKey, Nombre, Codigo, Modulo, Activo, Modificado, ModificadoPor`.
El lenguaje se infiere del contenido del script.

## 3. Decisión de sandbox

- **Elegido por el usuario:** usar `EmpresaB` como empresa de pruebas.
- **Advertencia registrada:** `EmpresaB` es **producción viva** (5 scripts
  reales, datos contables/fiscales). Un harness (T4.1) hace escrituras.
- **Recomendación híbrida (pendiente de confirmar):**
  - Pruebas de **lectura** (Query, show_html, dashboards, read_excel) → `EmpresaB` (más realista, tiene datos reales).
  - Pruebas de **escritura/timbrado** (crear docs, altas, timbrar) → `ComercialSP` (vacía, desechable).
  - Si todo va en `EmpresaB`: **transacción + rollback obligatorio** y nunca timbrar de verdad.

## 4. Ideas nuevas propuestas (complementan el plan)

Aprovechan activos ya construidos (sobre todo el corpus de ingeniería inversa de
`Entrenamiento/empresa_base_cp`).

- **A. Asistente IA embebido en la consola (RAG sobre el corpus propio).** Subir la
  prioridad del backlog B3. Panel "Pregúntale al esquema" + generación de scripts desde
  lenguaje natural, citando las 500 tablas / 814 vistas / 22 hallazgos. Diferenciador que
  la competencia no puede replicar sin rehacer la ingeniería inversa.
- **B. Los 22 hallazgos como aserciones ejecutables.** `verificar_esquema.ps1` que corra
  cada hallazgo de `empresa_base_cp/CLAUDE.md` como test de contrato → detecta cambios de
  versión de CONTPAQi antes de que rompan scripts. Bajo esfuerzo, alto valor defensivo.
- **C. Linter estático de scripts en la consola.** Antes de guardar en `zzBrosScript`:
  marcar `NonQuery` sin `Confirm`, escrituras fuera de transacción, `ctx.erp` en script
  job-safe, tablas inexistentes. Complementa la seguridad de T2.3.
- **D. Modo dry-run / diff de impacto universal.** Cualquier escritura ejecutable en
  simulación mostrando "esto tocaría N filas de estas tablas", usando la misma técnica
  snapshot-diff del laboratorio.
- **E. Galería de soluciones en la consola.** Sobre los paquetes `.bros` (T1.3): instalar
  soluciones de ejemplo (CXC, CXP, reporte ejecutivo) desde una galería. Paso previo al
  marketplace B1.
- **F. Telemetría de valor sobre la auditoría central (T2.1).** Una vez llena
  `zzBrosAuditoria`: mini-dashboard de uso ("qué botones se usan, cuánto ahorran, cuáles
  fallan") para priorizar con datos.

## 5. Punto de vista del desarrollador (resumen)

- **Fortalezas:** estrategia técnica correcta y difícil de copiar (conexión viva + XEngine
  vs SDK); documentación excepcional; plan maestro serio y fiel a la realidad.
- **Riesgos a subir de prioridad:** (1) **seguridad H8** — vector #1, antes que features;
  (2) **cero pruebas automatizadas H11** con escrituras a un ERP fiscal; (3) **trampa del
  instalador H1** — problema de proceso, lo cura el CI (T4.2); (4) assets en disco vs
  scripts en BD (multi-terminal) → lo resuelve T1.3.
- **Orden sugerido si se ejecuta:** Fase 0 completa → seguridad (T0.4/T2.3) →
  `ctx.dashboard()` (T1.1, mayor ROI) → auditoría central (T2.1) → harness (T4.1) antes
  de más escrituras.

## 6. Próximos pasos para retomar

1. Confirmar el sandbox híbrido de §3 (o ratificar todo en EmpresaB con
   rollback obligatorio).
2. Decidir arranque: **Fase 0** (recomendado, 1 día, riesgo bajo) vs seguridad vs T1.1.
3. Al ejecutar la primera tarea de código, respetar la regla de oro (CHANGELOG +
   AssemblyVersion + notas_version.html + `.md` en el mismo commit).
