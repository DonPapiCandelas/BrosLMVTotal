# SDK de CONTPAQi Contabilidad — cómo escribir pólizas directo (sin Comercial)

> **Estado: investigado y confirmado contra material oficial de CONTPAQi + un prototipo
> real (fuera de este repo). No probado todavía en vivo desde BrosLMV** — es la
> referencia técnica lista para implementar, no una función ya shipped. Complementa al
> [Motor de Asientos Contables](MOTOR_ASIENTOS_CONTABLES.md), que resuelve pólizas
> *dentro* de Comercial; esto resuelve escribir pólizas **directo en Contabilidad**,
> sin pasar por Comercial en absoluto — útil cuando la fuente del dato no es un
> documento de Comercial (ej. un CFDI leído de disco) o cuando no se quiere depender
> del botón nativo "Sincronizar con contabilidad".

## Qué es, y por qué es un SDK distinto

**`CONTPAQi Contabilidad` es un producto separado de `Comercial PRO`**, con su propio
SDK — nada que ver con XEngine (el que usa `ctx.erp`) ni con el SDK viejo de
`Comercial Premium` (`MGW_SDK.DLL`, descartado como referencia — ver nota al final).

- **ProgID / librería:** `SDKCONTPAQNGLib` (COM), GUID de la type library
  `{4A6A81F3-B2B3-448C-A557-A005091BE801}` v1.0.
- **Igual que XEngineLib, es COM de 32 bits** — el mismo problema que ya resolvimos
  para `BrosLMV.Runner`. La solución encontrada (en el prototipo, no en este repo
  todavía) es idéntica en espíritu: un proceso puente x86 aparte que habla JSON por
  stdin/stdout, para que la app principal (64 bits) nunca tenga que serlo.

## Conexión

```csharp
using SDKCONTPAQNGLib;

var sesion = new TSdkSesion();
if (sesion.conexionActiva == 0) sesion.iniciaConexion();
if (sesion.ingresoUsuario == 0 && sesion.conexionActiva == 1)
    sesion.firmaUsuarioParams(usuario, password);   // o firmaUsuario() con credenciales por defecto

// Listar empresas disponibles
var lista = new TSdkListaEmpresas();
if (lista.buscaPrimero() != 0)
    do { /* lista.NombreBDD, lista.Nombre */ } while (lista.buscaSiguiente() != 0);

sesion.abreEmpresa(nombreEmpresa);   // 0 = error, revisar sesion.UltimoMsjError
// ... trabajar ...
sesion.cierraEmpresa();
sesion.finalizaConexion();
```

`TSdkSesion` (igual que `AccPoliza.clsMain` en Comercial) es el objeto de sesión que
hay que compartir — cualquier otro objeto del SDK necesita `.setSesion(sesion)` antes
de usarse.

## Catálogo de cuentas (`TSdkCuenta`)

Contabilidad tiene **su propio catálogo de cuentas**, independiente del
`engrefAccountingCatalog` de Comercial — los códigos son numéricos planos (ej.
`10101000`), no el formato `NNN-NN-NNN` con alias que usa Comercial.

```csharp
var cuenta = new TSdkCuenta();
cuenta.setSesion(sesion);
int cursor = 0;
cuenta.consultaPorCodigo_buscaPrimero(ref cursor);
while (cursor != 0)
{
    // cuenta.Codigo, cuenta.Nombre, cuenta.EsAfectable
    cuenta.consultaPorCodigo_buscaSiguiente(ref cursor);
}
```

## Crear una póliza (`TSdkPoliza` + `TSdkMovimientoPoliza`)

Esta es la pieza que faltaba — confirmada contra material de entrenamiento oficial de
CONTPAQi. Patrón completo (encabezado + movimientos + crear):

```csharp
var poliza = new TSdkPoliza();
var movimiento = new TSdkMovimientoPoliza();
poliza.setSesion(sesion);
movimiento.setSesion(sesion);

// Encabezado
poliza.iniciarInfo();
poliza.Tipo = ETIPOPOLIZA.TIPO_INGRESOS;          // también existen Egresos/Diario (valores exactos sin confirmar aún)
poliza.Clase = ECLASEPOLIZA.CLASE_AFECTAR;
poliza.Impresa = 0;
poliza.Fecha = DateTime.Today;
poliza.Diario = 0;
poliza.SistOrigen = ESISTORIGEN.ORIG_CONTPAQNG;   // marca el origen del dato
poliza.Ajuste = 0;
poliza.Concepto = "...";

// Un renglón (repetir por cada movimiento, alternando Cargo/Abono)
movimiento.iniciarInfo();
movimiento.NumMovto = 1;
movimiento.CodigoCuenta = "10101000";              // código del catálogo de Contabilidad, NO el de Comercial
movimiento.TipoMovto = ETIPOIMPORTEMOVPOLIZA.MOVPOLIZA_CARGO;  // o MOVPOLIZA_ABONO
movimiento.Importe = 100;

int ok = poliza.agregaMovimiento(movimiento);
if (ok == 0) { /* movimiento.getCodigoError() / .getMensajeError() */ }

// ... agregar el resto de los movimientos igual, con NumMovto consecutivo ...

poliza.crea();   // confirma la póliza completa
```

**Enums confirmados** (valores vistos en uso real; puede haber más miembros sin
confirmar todavía — no asumir la lista completa sin volver a revisar el SDK):
`ETIPOPOLIZA.TIPO_INGRESOS`, `ECLASEPOLIZA.CLASE_AFECTAR`,
`ESISTORIGEN.ORIG_CONTPAQNG`, `ETIPOIMPORTEMOVPOLIZA.MOVPOLIZA_CARGO` /
`MOVPOLIZA_ABONO`.

## Cómo se relaciona con el Motor de Asientos Contables

| | Motor de Asientos (ya integrado) | SDK de Contabilidad (esta referencia) |
|---|---|---|
| Escribe en | `accPoliza`/`accPolizaTransaccion` de **Comercial** | Directo en **Contabilidad**, otra base |
| Depende de | Un cobro/pago ya existente en Comercial | Nada de Comercial — puede alimentarse de un CFDI en disco, por ejemplo |
| Envío a Contabilidad | Lo hace Comercial (nativo), nunca este motor | Es el destino final, sin intermediario |
| Catálogo de cuentas | `engrefAccountingCatalog` (alias, formato con guiones) | Catálogo propio de Contabilidad (código plano) |

Un flujo completo podría combinar ambos: Motor de Asientos arma y cuadra la póliza
dentro de Comercial (con toda su lógica de multi-moneda/condiciones ya resuelta), y
este SDK la escribe directo en Contabilidad cuando no se quiera depender del botón
nativo de sincronización — pero **esto es una posibilidad de diseño, no algo
implementado ni probado todavía.**

## Pendiente antes de que esto sea una capacidad real de BrosLMV

1. Construir el puente x86 (mismo patrón que `BrosLMV.Runner` para XEngineLib) como
   componente propio, no solo como referencia de otro proyecto.
2. Probar en vivo contra una instancia real de Contabilidad — nada de este documento
   se ha corrido desde este repo, solo se confirmó leyendo material oficial y un
   prototipo externo.
3. Confirmar la lista completa de valores de los enums (`ETIPOPOLIZA`, `ECLASEPOLIZA`,
   `ESISTORIGEN`, `ETIPOIMPORTEMOVPOLIZA`) — solo se vio un valor de cada uno en uso.
4. Decidir el mapeo de cuentas Comercial↔Contabilidad si se combina con el Motor de
   Asientos (los catálogos son independientes, con formatos de código distintos).

## Nota sobre el SDK de Comercial Premium (descartado)

Existe otro manual completo (`MGW_SDK.DLL`/`CONTPAQ_SDK.DLL`, `fAltaDocumento`,
`fTimbraXML`, etc.) para **Comercial Premium** — un producto distinto a Comercial PRO,
con otra arquitectura (structs binarios, `__stdcall`, ANSI). No aplica a BrosLMV y no
se integró nada de ahí — queda esta nota para que nadie lo vuelva a confundir.
