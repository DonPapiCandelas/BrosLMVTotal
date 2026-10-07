# Runner: conexión por stdin y aislamiento del consumidor

Estado 2026-10-01: Runner **0.4.0**, versión independiente del addon. No implica liberación del addon 2.98.1 ni activación de numeración externa.

### Inventariar el ejecutable que realmente consume la integración

Una instalación puede conservar Runner antiguo en la carpeta del addon y usar otro
ejecutable protegido desde la configuración de la aplicación. Leer primero la ruta del
consumidor; después comprobar versión/hash, argumentos, transporte stdin y conexiones.
Actualizar el addon no actualiza automáticamente el puente web ni el worker almacenado
en SQL. La versión del Runner tampoco identifica por sí sola las funciones del SDK que
incorpora: revisar fuentes/referencias y compatibilidad del consumidor antes de reemplazarlo.
Inventariar separadamente addon, ejecutable consumido, script SQL y servicios activos.

## Canal implementado

`--conn-stdin` exige stdin redirigido, rechaza la combinación con `--conn`, lee una línea con máximo 16 KiB y rechaza entrada vacía. La contraseña no aparece en argv. El consumidor envía la cadena únicamente por el pipe de entrada del proceso, nunca como archivo temporal ni argumento.

`--check-connection --conn-stdin --bd <empresa>` valida la cadena, exige la misma base que `--bd` y ejecuta exclusivamente `SELECT DB_NAME()`. No inicializa XEngine, ejecuta scripts ni crea documentos. Es una prueba del transporte/autenticación SQL, **no** prueba del postproceso COM.

Los errores de bootstrap XEngine ya no incluyen el mensaje bruto de la excepción. El consumidor tampoco publica/loguea stdout/stderr ni excepciones completas del proceso: conserva tipo de excepción/exit code y usa mensajes seguros. El resultado por documento debe verificarse en la cola; un ExitCode=0 no demuestra stock, póliza ni pagos correctos.

`--conn` y el respaldo del instalador se conservan para compatibilidad histórica, pero los consumidores actualizados **no los utilizan**. No introducir nuevas invocaciones con contraseñas en argv.

## Patrón aplicado al consumidor

- Conector PDO recupera la contraseña justo al abrir la conexión mediante DPAPI LocalMachine y archivo con ACL. Laravel mantiene password=null: ni `.env` ni config cache contienen el secreto SQL.
- Cuenta CRM: DML solamente en esquema de la base propia, sin ALTER ni rol de servidor.
- Cuenta catálogos/cola: SELECT explícito por objeto y DML en tablas autorizadas; sin escritura directa del encabezado nativo ni acceso al catálogo de usuarios del ERP.
- Cuenta financiera: SELECT en ocho tablas, sin DML/DDL. No basta bloquear save() del modelo.
- Cuenta del ejecutor nativo separada, credencial disponible únicamente a Administradores/SYSTEM; no al pool web. **Mantiene db_owner solo en su empresa**, porque no se ha validado todavía un conjunto fino de permisos de COM/SDK. No tiene sysadmin ni roles de servidor. Esto es aislamiento y reducción respecto a sa, NO mínimo privilegio completo del ejecutor.
- IIS usa identidad del pool como identidad anónima; con FastCGI impersonation, IUSR puede impedir la lectura del secreto aunque CLI funcione. [Patrón oficial de Microsoft](https://learn.microsoft.com/en-us/iis/web-hosting/web-server-for-shared-hosting/application-pool-identity-as-anonymous-user).
- Código/config/vendor y cache de bootstrap de solo lectura para web; únicamente storage es área de escritura de la aplicación. El ejecutor sigue bajo SYSTEM: cuenta Windows de servicio dedicada y separación de caches ejecutables requieren una etapa posterior, no se declaran resueltas aquí.

## Exclusión mutua del envío

Una transacción SQL de **encolado**, sin COM, obtiene sp_getapplock con propietario Transaction y principal public, recurso derivado de tipo/ID CRM/origen/almacén. Decide si reutilizar el resultado y encola bajo el mismo lock. NULL de origen se filtra explícitamente. forceNew tampoco duplica un envío pendiente ni un documento nativo todavía vigente. Solo permite reenvío cuando el resultado anterior está eliminado/cancelado.

Se comprobaron dos sesiones concurrentes, rechazo del segundo lock y liberación por rollback; reintentos/forceNew sobre un resultado vigente no cambiaron la cola. **No** se probaron altas nativas concurrentes ni claim simultáneo de dos ejecutores: eso sigue pendiente junto con reconciliación de resultados inciertos.

## Evidencia y límites de entrega

### Regresión al restringir permisos del consumidor

No reutilizar una whitelist de otra instalación ni dar por validado un conector solo porque SELECT y PDF funcionan. Inventariar el código productivo y construir contratos positivos de cada operación: productos/extensiones, proveedores/direcciones/canales, categorías/unidades, centros de costo/proyectos/almacenes y colas. Una tabla de extensión puede necesitar INSERT además de UPDATE; un catálogo nuevo puede necesitar INSERT aunque anteriormente solo se consultara. Comprobar las variantes antes de retirar el acceso anterior y guardar los permisos/roles previos para revertir una concesión específica.

Distinguir cuenta web que encola de cuenta del ejecutor que genera documentos: el motor nativo requiere también DML/EXECUTE y operaciones auxiliares, no únicamente SELECT sobre encabezados. HAS_PERMS_BY_NAME comprueba autorización, no prueba el flujo COM, las dependencias de triggers ni la fidelidad del payload. Si faltan campos, comparar captura → payload → cola → documento existente: no resolver una omisión de mapeo concediendo privilegios globales ni reenviando un documento sin conciliación. Ninguna prueba de conexión SELECT-only sustituye aceptación funcional. Registrar explícitamente qué se comprobó y qué sigue pendiente.

Compilación net48/x86: cero errores/advertencias. Canal stdin y autenticación de cuentas limitadas comprobados con SELECT; pruebas del consumidor cubren PDF, autorización, OAuth inválido, finanzas, Excel y exclusión mutua. Cambios de este turno únicamente en runner/ y documentación, no en src/. La regla de oro del estado actual pasó; los 33 casos y empaquetado del addon con numeración externa permanecen pendientes y no fueron sustituidos por estas pruebas. Se distribuyó el Runner independiente; no se ejecutaron instaladores del addon ni se cerró Comercial.

No se generaron pagos/documentos de prueba en empresas publicadas. Persisten pendientes: permisos finos/identidad Windows del ejecutor, postcondiciones nativas, impuestos/clones, reverse histórico, rotación del sa compartido después de inventariar otros consumidores y restore/offsite. No presentar esto como garantía de ausencia de vulnerabilidades.

## Parches del runtime del consumidor

Se actualizó PHP 8.3.0 a 8.3.35 NTS x64 VS16 y SQLSRV/PDO_SQLSRV 5.12.0 a 5.13.3 en instalaciones independientes por servidor, sin reemplazar el runtime compartido. Validar SHA256 de paquetes oficiales, extensión/arquitectura/ODBC, extensiones cargadas, ajustes propios y certificados; probar bootstrap, permisos SQL, PDF, OAuth y Excel antes de cambiar el handler IIS y servicios propios. Comprobar después HTTP y ruta real de procesos, no solo php --version en CLI. Respaldar runtime, handler, configuración IIS y servicios; restaurar por ubicación propia, nunca reemplazar toda la configuración compartida. [PHP oficial](https://www.php.net/downloads.php?os=windows&version=8.3), [controlador oficial Microsoft](https://github.com/microsoft/msphpsql/releases/tag/v5.13.3).

Respaldar también el almacén cifrado posterior al cambio de credenciales. DPAPI LocalMachine permite recuperación en la misma máquina con ACL correctas, pero copiar los archivos a otro servidor NO basta: el plan de restore debe contemplar reprovisionamiento de credenciales. No almacenar contraseñas en claro para hacer el backup portable.
