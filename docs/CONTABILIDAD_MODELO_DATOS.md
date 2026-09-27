# CONTPAQi Contabilidad — modelo de datos (lectura), verificado contra datos reales

> **Estado: investigado y verificado con datos reales de producción** (no un
> sandbox propio — otro proyecto que consulta Contabilidad en solo lectura).
> Complementa a [`SDK_CONTABILIDAD.md`](SDK_CONTABILIDAD.md) (cómo **escribir**
> pólizas por SDK): esto es cómo **leer** correctamente la base de Contabilidad
> por SQL directo — el mismo tipo de "diccionario de referencia" que ya
> tenemos para Comercial PRO en `docs/XENGINE_FUNCIONES.md` y el hallazgo de
> `AliasAccountNumber`, pero para el otro producto.
>
> Cada regla de aquí fue **probada contra una base real**, no inferida del
> manual — donde el manual oficial de CONTPAQi y el comportamiento real
> discreparon, se documentó el real. Los números de ejemplo son de una
> instalación real (sanitizados, sin nombre de empresa/RFC/base).

## 0. Las 6 trampas — léelas antes de escribir cualquier SQL contra `ct*`

Ninguna de estas produce un error de SQL. Todas producen **un número
equivocado que parece correcto** — la clase de bug más peligrosa que hay.

| # | Trampa | Si la ignoras |
|---|---|---|
| 1 | `Ejercicio` significa **dos cosas distintas** según la tabla | Una consulta devuelve 0 filas, o mezclas años |
| 2 | `SaldosCuentas.Tipo=1` es acumulado; `Tipo=2/3` es del periodo (no hay que restar) | Cifras infladas |
| 3 | Los saldos se guardan **positivos según su naturaleza**, no con signo algebraico | Ingresos/pasivos con signo invertido |
| 4 | `CtaMayor=2` significa **"no es cuenta de mayor"**, no "afectable" | Doble conteo — en un caso real, los gastos salieron casi al doble |
| 5 | `Polizas.TipoPol` **no** se une por `TiposPolizas.Id` | Pólizas mal etiquetadas en todos los reportes |
| 6 | `SaldosSegmentoNegocio` solo cubre cuentas con `Cuentas.SegNegMovtos=1` | El reporte por centro de costo no cuadra contra el consolidado |

## 1. Ejercicios y periodos ⚠️ la trampa principal

**Dos numeraciones del ejercicio conviven en la misma base**, y cuál usar
depende de la tabla:

| Tabla | Su columna `Ejercicio` contiene |
|---|---|
| `SaldosCuentas` | `Ejercicios.Id` (NO correlativo — puede que 2026 sea el Id 5) |
| `SaldosSegmentoNegocio` | `Ejercicios.Id` |
| `Polizas` | El **ejercicio calendario** (2026) |
| `MovimientosPoliza` | El **ejercicio calendario** (2026) |
| `Parametros.EjerActual` | `Ejercicios.Id` — **no** el año |

**Regla obligatoria — nunca uses `Ejercicio` crudo, resuelve siempre contra `Ejercicios`:**

```sql
DECLARE @Ejercicio INT = 2026;
DECLARE @IdEje INT = (SELECT Id FROM Ejercicios WITH (NOLOCK) WHERE Ejercicio = @Ejercicio);
-- Saldos (SaldosCuentas/SaldosSegmentoNegocio) -> usa @IdEje
-- Pólizas/movimientos (Polizas/MovimientosPoliza) -> usa @Ejercicio
```

Ejercicio y periodo vigentes:

```sql
SELECT e.Id AS IdEjercicio, e.Ejercicio, p.PerActual
FROM Parametros p WITH (NOLOCK)
JOIN Ejercicios e WITH (NOLOCK) ON e.Id = p.EjerActual;
```

**Periodos 13 y 14 son de ajuste/cierre**, no meses reales — exclúyelos de
reportes de operación por default, con opción explícita de incluirlos.

## 2. Saldos (`SaldosCuentas` / `SaldosSegmentoNegocio`)

Misma forma en ambas: una fila por (cuenta [, segmento], ejercicio, tipo) con
14 columnas de importe por periodo. **Ojo al nombre**: `Importes`N (con S) en
`SaldosCuentas`, `Importe`N (sin S) en `SaldosSegmentoNegocio`.

| `Tipo` | Contenido | Acumulado / del periodo |
|:--:|---|---|
| 1 | Saldos | **Acumulado** al cierre del periodo N |
| 2 | Cargos | Del periodo N (no acumulado) |
| 3 | Abonos | Del periodo N (no acumulado) |
| 4 | Saldos en moneda extranjera | Acumulado |
| 5 | Cargos en moneda extranjera | Del periodo |
| 6 | Abonos en moneda extranjera | Del periodo |

`Tipo=2/3` **ya es el movimiento del periodo** — verificado que coincide al
centavo contra `MovimientosPoliza`, sin restar el periodo anterior.

### Convención de signos ⚠️

CONTPAQi guarda los saldos **positivos según la naturaleza de la cuenta**, no
con signo algebraico:

| `Cuentas.Tipo` | Clasificación | Naturaleza | `Saldo(P) =` |
|:--:|---|---|---|
| `A` | Activo | Deudora | `SaldoIni + Σ(Cargos − Abonos)` |
| `B` | Activo (contra/especial) | Deudora | ídem |
| `D` | Pasivo | Acreedora | `SaldoIni + Σ(Abonos − Cargos)` |
| `F` | Capital contable | Acreedora | ídem |
| `G` | Resultados deudoras (costos/gastos, 5xxx–9xxx) | Deudora | `Σ(Cargos − Abonos)` |
| `H` | Resultados acreedoras (ingresos, 4xxx) | Acreedora | `Σ(Abonos − Cargos)` |
| `I` | Cuentas estadísticas | — | — |
| `K` | Cuentas de orden | — | — |

Consecuencia: **ingresos y gastos se presentan ambos en positivo** — la
estructura del estado de resultados es la que resta. Una cuenta correctora
(ej. un descuento sobre ventas, que es de tipo `H` pero disminuye ingresos)
sale con saldo **negativo** — eso es correcto, no un bug a corregir.

Verificado en un caso real: aplicando la naturaleza correcta, la identidad
`Saldo(P) − Saldo(P−1) = movimiento del periodo` se cumplió en miles de
cuentas con **0 descuadres**; con `Cargos − Abonos` genérico para todas,
fallaron justo las cuentas acreedoras.

### Normalizar las 14 columnas a filas

```sql
SELECT IdCuenta, Ejercicio, Tipo, Periodo, Importe
FROM (
    SELECT IdCuenta, Ejercicio, Tipo,
           Importes1, Importes2, Importes3, Importes4, Importes5, Importes6,
           Importes7, Importes8, Importes9, Importes10, Importes11,
           Importes12, Importes13, Importes14
    FROM SaldosCuentas WITH (NOLOCK)
) AS s
UNPIVOT (Importe FOR Periodo IN (
    Importes1, Importes2, Importes3, Importes4, Importes5, Importes6,
    Importes7, Importes8, Importes9, Importes10, Importes11,
    Importes12, Importes13, Importes14)) AS u;
-- Periodo sale como texto 'Importes6' -> CAST(REPLACE(Periodo,'Importes','') AS INT)
```

Recomendado: normalizar en una vista **propia** (en la base del reporteador,
nunca en `ct*`), no repetir el `CASE`/`UNPIVOT` en cada consulta.

## 3. Catálogo de cuentas (`Cuentas`)

### `CtaMayor` — nivel jerárquico, NO afectabilidad ⚠️

| Valor | Significado |
|:--:|---|
| 1 | Mayor |
| 2 | **No** es cuenta de mayor (¡no significa "afectable"!) |
| 3 | Título |
| 4 | Subtítulo |

### `Afectable` — la columna real para saber dónde se capturan movimientos

`Afectable=1` recibe movimientos directos; `Afectable=0` es acumuladora (ya
contiene la suma de sus hijas). **Para sumar sin duplicar, filtra siempre
`Afectable = 1`** — nunca `CtaMayor`.

En un caso real medido: usando `CtaMayor=2` en vez de `Afectable=1`, los
gastos de operación salieron **casi al doble**, y aparecieron cientos de
millones de más por incluir las cuentas raíz del sistema (`_RDEUDOR`/
`_RACREEDOR`, ver abajo). Los ingresos coincidieron por casualidad con ambos
filtros — lo que vuelve el error más peligroso: la primera cifra que se
revisaría cuadra igual.

### Estructura del código y cuentas raíz del sistema

- Códigos reales de 10 dígitos sin separadores. Primer dígito = clasificación
  (1 activo, 2 pasivo, 3 capital, 4 ingresos, 5 costos, 6 gastos, 7 costo
  integral de financiamiento, 8 partidas no ordinarias, 9 ISR/PTU).
- Existen **cuentas raíz del sistema** con código no numérico —no son cuentas
  contables reales, exclúyelas siempre—:
  `_GLOBAL`, `_ACTIVO`, `_BPASIVO`, `_CAPITAL`, `_UTILIDAD`, `_RACREEDOR`,
  `_RDEUDOR`, `_ESTADISTICAS`, `_ORDEN`, `_CUADRE`.
  Filtro seguro: `WHERE Codigo NOT LIKE '[_]%'` (con el `_` escapado).

### Otras columnas relevantes

| Columna | Uso |
|---|---|
| `EsBaja` | `1` = cuenta inactiva — filtrar igual aunque parezca que no hay ninguna. |
| `SegNegMovtos` | `1` = la cuenta captura segmento de negocio. Determina qué cuentas aparecen en `SaldosSegmentoNegocio` (§5). |
| `IdAgrupadorSAT` | Enlaza con `AgrupadoresSAT` (contabilidad electrónica). |
| `IdRubro` | Enlaza con `RubrosNIF` (estados financieros normativos). |
| `IdMoneda` | Con `Tipo` 4/5/6 de saldos da el detalle en moneda extranjera. |

## 4. Pólizas y movimientos

`Polizas.Id` → `MovimientosPoliza.IdPoliza` — **sin llave foránea declarada**
(la base no tiene ninguna FK; la integridad la impone la aplicación).
`MovimientosPoliza` desnormaliza `Ejercicio`/`Periodo`/`TipoPol`/`Folio` del
encabezado — se puede filtrar sin unir a `Polizas`.

### `TipoMovto` — cargo o abono

`0` = Cargo, `1` = Abono. **No existen columnas `Cargo`/`Abono` separadas** —
una sola columna `Importe` más el discriminador `TipoMovto`. Verificado:
`Σ Importe WHERE TipoMovto=0` coincide con `Polizas.Cargos` al centavo.

### `TipoPol` — cómo unirlo con `TiposPolizas` ⚠️

`Polizas.TipoPol` es un entero. En `TiposPolizas`, **`Id` es un consecutivo
interno que NO corresponde a `TipoPol`**; `Codigo` es `nvarchar` alineado a
la derecha con espacios (`'   1'`, `'  13'`). La unión correcta:

```sql
FROM Polizas p WITH (NOLOCK)
LEFT JOIN TiposPolizas tp WITH (NOLOCK)
       ON TRY_CAST(LTRIM(RTRIM(tp.Codigo)) AS INT) = p.TipoPol
```

Unir por `Id` en vez de por `Codigo` deja la mayoría de las pólizas sin nombre
o con el nombre equivocado — en un caso real, cientos de pólizas de un tipo
se etiquetaron con el nombre de otro tipo completamente distinto.

`TiposPolizas.Tipo`: `1` = predefinido del sistema, `6` = definido por el
usuario (los tipos de sistema son Ingresos/Egresos/Diario; el resto los crea
cada empresa a su gusto, así que su lista de nombres es propia de cada
instalación — no la documentes como universal).

### `ConCuadre` no significa "cuadrada"

Verificado: la enorme mayoría de las pólizas de un ejercicio real tenían
`ConCuadre = 0` y **todas cuadraban igual** (`Cargos = Abonos`, 0
descuadres). La bandera es del mecanismo de captura, no un indicador de
calidad — no la uses para eso.

## 5. Segmentos de negocio (centros de costo)

- `SegmentosNegocio` **no tiene jerarquía en la base** — las columnas que
  podrían definirla (`SegContSegmento1..7`) pueden estar vacías por completo.
  Cualquier agrupación por niveles es una convención de cada cliente, no algo
  que se pueda leer de CONTPAQi — debe configurarse en la aplicación.
- Pueden convivir **dos formatos de código** para el mismo catálogo (ej. 4
  dígitos vs. 2 espacios + 2 dígitos) — siempre `LTRIM(RTRIM(Codigo))` antes
  de comparar o mostrar.
- **`SaldosSegmentoNegocio` solo cubre cuentas con `Cuentas.SegNegMovtos=1`**
  — sin ese filtro aparecen decenas o cientos de "descuadres" falsos al
  comparar la suma por segmentos contra el total de la cuenta. El
  consolidado real se toma siempre de `SaldosCuentas`, nunca de sumar la
  matriz por segmento — la diferencia es la columna "Comunes / No
  distribuido", que debe **mostrarse**, no ocultarse ni forzarse a cero.

## 6. Consultas de referencia (genéricas, sin datos de ningún cliente)

Todas asumen `WITH (NOLOCK)` — ver §8 sobre por qué.

```sql
-- Contexto de la empresa
SELECT p.RazonSocial, p.RFC, p.Mascarilla,
       e.Id AS IdEjercicio, e.Ejercicio, p.PerActual
FROM Parametros p WITH (NOLOCK)
JOIN Ejercicios e WITH (NOLOCK) ON e.Id = p.EjerActual;

-- Balanza de comprobación (periodo @P del ejercicio @Ejercicio)
DECLARE @Ejercicio INT = 2026, @P INT = 6;
DECLARE @IdEje INT = (SELECT Id FROM Ejercicios WITH (NOLOCK) WHERE Ejercicio = @Ejercicio);
SELECT c.Codigo, c.Nombre, c.Tipo,
       sal.Importes6 AS SaldoFinal, car.Importes6 AS CargosPeriodo, abo.Importes6 AS AbonosPeriodo
FROM Cuentas c WITH (NOLOCK)
JOIN      SaldosCuentas sal WITH (NOLOCK) ON sal.IdCuenta=c.Id AND sal.Ejercicio=@IdEje AND sal.Tipo=1
LEFT JOIN SaldosCuentas car WITH (NOLOCK) ON car.IdCuenta=c.Id AND car.Ejercicio=@IdEje AND car.Tipo=2
LEFT JOIN SaldosCuentas abo WITH (NOLOCK) ON abo.IdCuenta=c.Id AND abo.Ejercicio=@IdEje AND abo.Tipo=3
WHERE c.Afectable=1 AND c.EsBaja=0 AND c.Codigo NOT LIKE '[_]%'
ORDER BY c.Codigo;
-- Sustituir Importes6 por el periodo pedido, o usar el UNPIVOT de §2.

-- Auxiliar de una cuenta (drill-down)
SELECT p.Fecha, p.TipoPol, tp.Nombre AS TipoPoliza, p.Folio, p.Concepto,
       m.NumMovto, m.Referencia, m.Concepto,
       CASE WHEN m.TipoMovto=0 THEN m.Importe END AS Cargo,
       CASE WHEN m.TipoMovto=1 THEN m.Importe END AS Abono
FROM MovimientosPoliza m WITH (NOLOCK)
JOIN Polizas p WITH (NOLOCK) ON p.Id = m.IdPoliza
LEFT JOIN TiposPolizas tp WITH (NOLOCK) ON TRY_CAST(LTRIM(RTRIM(tp.Codigo)) AS INT) = p.TipoPol
WHERE m.IdCuenta = @IdCuenta AND m.Ejercicio = @Ejercicio   -- ¡ejercicio calendario aquí, no Ejercicios.Id!
  AND m.Periodo BETWEEN @PIni AND @PFin
ORDER BY p.Fecha, p.TipoPol, p.Folio, m.NumMovto;

-- Póliza completa
SELECT m.NumMovto, c.Codigo, c.Nombre, m.Concepto, m.Referencia,
       CASE WHEN m.TipoMovto=0 THEN m.Importe END AS Cargo,
       CASE WHEN m.TipoMovto=1 THEN m.Importe END AS Abono
FROM MovimientosPoliza m WITH (NOLOCK)
JOIN Cuentas c WITH (NOLOCK) ON c.Id = m.IdCuenta
WHERE m.IdPoliza = @IdPoliza
ORDER BY m.NumMovto;
```

## 7. Lo que NO hay en la base (para no buscarlo)

- **Sin llaves foráneas, sin triggers, sin CHECK/DEFAULT** declarados en el
  esquema — la integridad la impone la aplicación de Contabilidad, no SQL
  Server.
- Los procedimientos almacenados propios de CONTPAQi suelen estar
  **encriptados** (`WITH ENCRYPTION`) — su código no es legible ni como
  `sa`. **Nunca los ejecutes desde un reporteador o integración externa**
  (son de uso interno del producto: recálculo de saldos, DIOT, mantenimiento)
  — si necesitas ese dato, reconstrúyelo desde las tablas base.
- Es normal que buena parte de las tablas del esquema estén vacías en una
  instalación real (módulos que esa empresa no usa) — confirma que una tabla
  tiene filas antes de diseñar un reporte sobre ella.

## 8. Concurrencia

Si la base no tiene *Read Committed Snapshot* activado (frecuente en
instalaciones reales), una consulta larga de reportes **bloquea la captura
contable** — por eso toda consulta de solo lectura contra `ct*` debe usar
`WITH (NOLOCK)`. El riesgo (lecturas sucias) es aceptable para reportes
informativos; para un cierre exacto, correr sobre periodos ya cerrados
(inmutables). Activar RCSI sería mejor pero es DDL sobre la base del
producto — no lo actives sin autorización explícita del dueño de esa base.

## 9. Lección de proceso: dos herramientas de IA, mismo repo, sin git

En el proyecto de origen de esta documentación, dos sesiones de IA distintas
trabajaron sobre el mismo repositorio **sin control de versiones**, y una
reintrodujo silenciosamente errores que la otra ya había corregido —
incluyendo el `AGENTS.md` del propio proyecto, el primer archivo que
cualquier sesión nueva lee. Confirma, con un caso real, la razón detrás de la
regla de oro de este repo: **nunca trabajar sin commits**, y menos con más de
una IA tocando los mismos archivos.
