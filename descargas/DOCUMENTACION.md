# BrosLMV.Descargas — Documentación del proyecto

> Estado a 2026-08-12: **backend y UI funcionando de punta a punta contra el SAT real** (autenticación, solicitud, verificación y descarga de CFDI probadas en producción con la FIEL de Alma Vegetales Pack SA de CV, RFC AVP1108313W3). Este documento existe para poder retomar el proyecto en una conversación nueva sin perder contexto.

## 1. Qué es esto

Subproducto independiente de BrosLMV (no comparte versión ni instalador con el addon de Comercial PRO). Descarga los CFDI (XML) de un contribuyente directo del **Servicio de Descarga Masiva de CFDI del SAT** (protocolo SOAP oficial, no scraping), los guarda en una base de datos SQL Server **propia**, y expone una interfaz de escritorio WPF para gestionarlos.

**Decisiones de producto explícitas del usuario (no negociables, ya tomadas):**

- **No vincula automáticamente** los XML a documentos de Comercial Pro. Solo los deja disponibles para asociación manual — "solo quiero dejarlos disponibles".
- Debe llevar **auditoría/histórico real** en su propia BD: cambios de estatus de cancelación a través del tiempo, relaciones entre CFDI (notas de crédito, sustituciones).
- Debe soportar **disparo automático y manual**, con reintentos, bajo consumo de recursos (pensado para Tarea Programada de Windows).
- **La contraseña de la FIEL SÍ se guarda** (cifrada). Cita textual del usuario: *"si quiero que la contraseña la guarde en algún lado encriptada si quiere pero que si la guarde, no puede pedirla cada vez, si no no servirá hacerlo automático"* — pedirla cada vez rompe el propósito de automatizar.
- **Catálogo de empresas propio**, independiente de cómo esté organizada la base de Comercial Pro (una sola base de Comercial puede agrupar varias razones sociales/RFC).
- Cifrado con **DPAPI atado a esta máquina** (mismo mecanismo que ya usa el addon para su cadena de conexión en `src\Rutas.cs`), no portable entre equipos — decisión explícita, confirmada vía pregunta directa al usuario.
- La UI debe **abrir mostrando el listado de empresas primero**, no un formulario de configuración.

## 2. Estructura de carpetas

```
descargas/                          <- motor + CLI (net8.0, consola)
  Sat/
    SatFirmaXml.cs                  <- carga FIEL, firma XML-DSig (3 variantes)
    SatSoapClient.cs                <- los 4 clientes SOAP contra el SAT
  Datos/
    EsquemaSql.cs                   <- DDL idempotente de la BD propia
    BrosSatDb.cs                    <- acceso a datos (todo parametrizado)
    CfdiXmlParser.cs                <- parseo de CFDI 3.3 / 4.0
    DpapiHelper.cs                  <- cifra/descifra contraseña de FIEL
  Cola/
    SolicitudWorker.cs              <- una "pasada" del motor de cola automática
    SolicitudChunker.cs             <- parte un rango de fechas en tramos de mes (compartido UI/CLI)
    OrganizadorArchivos.cs          <- decide carpeta + nombre de archivo de cada XML (configurable por empresa)
    Bitacora.cs                     <- log a consola + archivo fijo en %LOCALAPPDATA%
  Program.cs                       <- CLI (--cer/--key/--password/--auto/...)
  BrosLMV.Descargas.csproj
  DOCUMENTACION.md                  <- este archivo

descargas-ui/                       <- interfaz de escritorio (net8.0-windows, WPF)
  MainWindow.xaml(.cs)              <- pantalla inicial: listado de empresas + detalle
  EmpresaWindow.xaml(.cs)           <- alta Y edición de empresa (Nombre/RFC/.cer/.key/password + carpeta/estructura/plantilla de XML)
  ConexionWindow.xaml(.cs)          <- primer arranque: cadena de conexión SQL
  SolicitarWindow.xaml(.cs)         <- diálogo para lanzar una SolicitaDescarga manual (parte el rango en meses)
  BitacoraWindow.xaml(.cs)          <- visor de la bitácora (Cola/Bitacora.cs) dentro de la app
  Configuracion.cs                  <- config.json junto al exe (solo cadena de conexión)
  BrosLMV.DescargasUI.csproj        <- enlaza (no copia) los .cs de descargas/
```

Patrón reutilizado del resto del repo: los `.cs` de `descargas/` se **enlazan** (`<Compile Include="..\descargas\..." Link="..." />`) dentro de `descargas-ui`, nunca se duplican — un solo origen de verdad.

## 3. El protocolo del SAT (Servicio de Descarga Masiva de CFDI)

Real, documentado, verificado contra producción — **no** es el manual PDF de 2022 del SAT, que está desactualizado en varios puntos (ver sección de trampas). Son 4 operaciones SOAP en **2 hosts distintos**.

### 3.1 Autenticacion.svc
`https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/Autenticacion/Autenticacion.svc`

Única llamada firmada directamente con la FIEL vía **WS-Security** (Timestamp + BinarySecurityToken + XML-DSig con C14N exclusivo, `KeyInfo`/`SecurityTokenReference`). Devuelve un token válido ~5 minutos. **No consume cupo diario.**

Implementado en `SatFirmaXml.FirmarAutentica` + `SatSoapClient.AutenticarAsync`.

### 3.2 SolicitaDescargaService.svc (mismo host que Autenticacion)
Partido en 3 operaciones — **no** es una sola operación genérica como decía el manual viejo:
- `SolicitaDescargaEmitidos`
- `SolicitaDescargaRecibidos`
- `SolicitaDescargaFolio`

Usa el token de Autenticación vía header HTTP `Authorization: WRAP access_token="..."`, **más** su propia firma XML-DSig separada — estilo "enveloped" (C14N estándar, transform `enveloped-signature`, `KeyInfo`/`X509Data` con `X509IssuerSerial` + `X509Certificate`; distinto estilo de firma que Autenticación).

Puntos que rompieron en pruebas reales y ya están corregidos:
- `RfcReceptor` es un **atributo XML plano**, no un elemento anidado `<RfcReceptores>` (el manual viejo decía lo segundo).
- `EstadoComprobante="Vigente"` es **obligatorio** cuando `TipoSolicitud="CFDI"` (descarga de XML) — si falta, el SAT regresa `CodEstatus=301`.

**Esto SÍ consume cupo diario real** — el código lo protege detrás del flag explícito `--solicitar` en el CLI y de una confirmación en la UI (`SolicitarWindow` muestra "⚠ gasta cupo real").

Implementado en `SatFirmaXml.FirmarSolicitud` + `SatSoapClient.SolicitarDescargaAsync`.

### 3.3 VerificaSolicitudDescargaService.svc (mismo host)
Una sola operación, **no** consume cupo diario, se puede llamar tantas veces como se quiera. Petición: `<solicitud IdSolicitud=".." RfcSolicitante="..">` + firma enveloped (mismo estilo que 3.2).

Respuesta: `EstadoSolicitud` (1=Aceptada, 2=EnProceso, 3=Terminada, 4=Error, 5=Rechazada, 6=Vencida), `NumeroCFDIs`, lista repetida de `IdsPaquetes`.

Implementado en `SatFirmaXml.FirmarVerificaSolicitud` + `SatSoapClient.VerificarSolicitudAsync`.

### 3.4 DescargaMasivaService.svc (**host distinto**, sin "solicitud" en el nombre)
`https://cfdidescargamasiva.clouda.sat.gob.mx/...`

Petición: `<PeticionDescargaMasivaTercerosEntrada><peticionDescarga IdPaquete=".." RfcSolicitante="..">` + firma enveloped.

**Trampa real encontrada**: el estatus de la respuesta (`CodEstatus`/`Mensaje`) viene en el **SOAP Header** (`<h:respuesta .../>`), no en el Body. `GetElementsByTagName("respuesta")` no encuentra un elemento con prefijo — hubo que buscar por `LocalName` iterando todos los elementos.

**Límites duros del SAT**: máximo **2 descargas por paquete**, el paquete vive **72 horas**.

Implementado en `SatFirmaXml.FirmarPeticionDescarga` + `SatSoapClient.DescargarAsync`.

### 3.5 Cómo se descubrieron las trampas
No por prueba y error contra el SAT (cada intento fallido en 3.2/3.4 gasta tiempo/cupo) — se verificó contra el **WSDL en vivo real** (`?wsdl`, `?xsd=xsd0`) y fuentes externas antes de cada corrección, y se probó primero con certificados/firmas de prueba desechables offline cuando fue posible (ej. el bug de `<KeyInfo><KeyInfo>` duplicado se encontró así, sin gastar ninguna llamada real).

## 4. Modelo de datos (BD propia, `descargas/Datos/EsquemaSql.cs`)

DDL 100% idempotente (`IF NOT EXISTS` en cada `CREATE`), se corre en cada arranque — no hay sistema de migraciones aparte todavía porque el esquema es chico.

| Tabla | Propósito |
|---|---|
| `Empresa` | Catálogo propio del descargador: `EmpresaID, Nombre, RFC (UNIQUE), RutaCer, RutaKey, PasswordCifrada (DPAPI), Activa, FechaAlta`, más `CarpetaXml, EstructuraCarpetas, PlantillaNombreArchivo` (**2026-08-14**, configurables por empresa desde `EmpresaWindow` — ver sección 7). No depende de cómo esté organizada la BD de Comercial. |
| `SolicitudDescarga` | Cada `SolicitaDescarga` hecha: `IdSolicitud, EmpresaID (FK nullable), RfcSolicitante, Tipo (Recibidos/Emitidos), TipoSolicitud (CFDI/Metadata, 2026-08-14), FechaInicial/Final, Estatus, NumeroCFDIs, Origen (Manual/Automatica), FechaSolicitud, FechaUltimaVerificacion`. |
| `SolicitudDescargaPaquete` | Paquetes (`IdPaquete`) que una solicitud reportó listos; `VecesDescargado` (tope 2), `FechaUltimaDescarga`. |
| `CfdiRecibido` | Un XML descargado y parseado: UUID (`UNIQUE`, evita duplicados), emisor/receptor, `Subtotal`/`Descuento`/`IVA`/`Retenciones`/`Total` (para CFDI tipo Pago, `Total` se llena sumando `MontoTotalPagos` del complemento — ver bug #15), forma/método de pago, moneda, `TipoCambio`, `EstatusSat` (Vigente/Cancelado), `Archivado` (saca del historial visible sin borrar fila ni XML), `RutaArchivoXml`, `FechaDescarga`. |
| `CfdiConcepto` | Las partidas del CFDI (`ClaveProdServ`, `Descripcion`, `Cantidad`, `ValorUnitario`, `Importe`) — **usada desde 2026-08-14** (`CfdiXmlParser.Conceptos`, `BrosSatDb.GuardarConceptos`/`ObtenerConceptos`, visible en `CfdiDetalleWindow`). Deliberadamente sin desglosar impuestos por partida ni complementos especiales de nómina (decisión explícita del usuario: "los de nómina no necesitamos darle tanto problema con eso"). El XML original en `RutaArchivoXml` es siempre la fuente de verdad completa. |
| `CfdiRelacion` | Relaciones entre CFDI (notas de crédito, sustituciones) vía `UUID`/`UUIDRelacionado`/`TipoRelacion`. |
| `CfdiVinculo` | Reservada, **aún sin usar desde la UI** — pensada para el día que se asocie un CFDI a un `docDocument` de Comercial Pro (`DocumentID` es una referencia lógica, sin FK real entre motores/bases distintas). |

La BD vive separada de la de Comercial Pro a propósito: Comercial solo la **consulta**, nunca la escribe, y el esquema puede evolucionar libremente.

## 5. Seguridad — decisiones y su porqué

- **Contraseña de la FIEL**: cifrada con DPAPI (`ProtectedData.Protect`, `DataProtectionScope.LocalMachine`) + entropía fija propia (`DpapiHelper.cs`). Atada a la máquina donde se cifró — si alguien copia la BD o el archivo a otro equipo, no la puede descifrar ahí. Se descifra solo en memoria, en el momento exacto de usarla (`MainWindow.CargarFielEmpresa`), nunca se vuelve a mostrar ni pedir al usuario.
- **Nunca en texto plano**: ni en la BD, ni en `config.json` (que solo guarda la cadena de conexión SQL), ni en logs.
- **Validación temprana**: `EmpresaWindow` intenta cargar la FIEL de verdad (`SatFirmaXml.CargarFiel`) ANTES de guardar — si la contraseña está mal, se sabe al momento de darla de alta, no en la primera corrida automática sin nadie viendo.
- **Todo el acceso a SQL es parametrizado** (`SqlParameter`/`AddWithValue`), nunca concatenación de strings hacia SQL — ver `BrosSatDb.cs`.
- **Llamadas reales al SAT siempre detrás de flags explícitos**: el CLI requiere `--autenticar`/`--solicitar` por separado; la UI muestra advertencia de "gasta cupo real" antes de cualquier `SolicitaDescarga`.

## 6. Bugs reales encontrados y su causa raíz (para no repetirlos)

1. **`<KeyInfo><KeyInfo>` duplicado** en la firma WS-Security de Autenticación — `SignedXml` ya envuelve el `KeyInfo`, no había que hacerlo de nuevo en `BinarySecurityTokenReferenceClause.GetXml()`. Encontrado con un certificado de prueba desechable, sin gastar llamadas reales.
2. **`RSA.ImportEncryptedPkcs8PrivateKey` no existe en net48** — por eso `descargas/BrosLMV.Descargas.csproj` (y `descargas-ui`) apuntan a **net8.0**, no al net48 del resto del addon. Deliberado, documentado en el propio `.csproj`.
3. **404 en la URL de `SolicitaDescarga`** — segmento de ruta y mayúsculas/minúsculas incorrectos; corregido verificando contra documentación externa, no a prueba y error.
4. **500 "ActionNotSupported"** — el nombre real de la operación es `SolicitaDescargaRecibidos`/`Emitidos`/`Folio`, no un genérico `SolicitaDescarga`; confirmado contra el WSDL real en vivo.
5. **`CodEstatus=301`** — faltaba el atributo `EstadoComprobante="Vigente"` en la petición de tipo `CFDI`.
6. **`CodEstatus` vacío al descargar** — el estatus viene en el SOAP Header con prefijo (`<h:respuesta>`), `GetElementsByTagName` no lo encuentra; hubo que buscar por `LocalName`.
7. **`EXEC('...' + QUOTENAME(@x))` no parsea en T-SQL** ("Incorrect syntax near 'QUOTENAME'") — limitación real del parser, confirmada con `sqlcmd`. Hay que precalcular `QUOTENAME` en una variable aparte antes de concatenar (ver `MainWindow.AsegurarBaseDeDatos`).
8. **SQL Server no deja conectar directo a una base inexistente**, ni con Windows Auth — hay que conectarse primero a `master` y crear la base si falta, ANTES de abrir la conexión real.
9. **Binding de WPF silenciosamente roto**: `{Binding X}` solo resuelve **propiedades** públicas (`{ get; set; }`), no **campos** públicos — sin excepción, la celda simplemente queda en blanco. Las clases `EmpresaRow`/`SolicitudRow`/`CfdiRow` de `MainWindow.xaml.cs` tuvieron que convertirse de campos a propiedades. (Primera hipótesis descartada: el bug de contraste de `SystemColors.Highlight*` de WPF — no era la causa real, aunque los overrides de brushes que se agregaron para probarla se quedaron como estilo defensivo razonable.)
10. **Fechas mostrándose un día adelantado**: columnas `DATETIME2` llenadas con `SYSUTCDATETIME()` se leen con `DateTime.Kind = Unspecified` vía `SqlDataReader.GetDateTime` — `.ToLocalTime()` sobre eso es un no-op. Corregido marcando explícitamente `DateTime.SpecifyKind(reader.GetDateTime(...), DateTimeKind.Utc)` al leer (`BrosSatDb.ObtenerSolicitudesRecientes`) y llamando `.ToLocalTime()` recién al formatear para mostrar (`MainWindow.CargarDetalle`).
11. **Filesystem insensible a mayúsculas de Windows**: borrar la carpeta vieja `Assets/` (WebView2) con `rm -rf` se llevó por delante la recién creada `assets/` (minúsculas) del mismo directorio — hubo que volver a copiar los PNG.
12. **`EmpresaWindow` sin espacio para los botones Guardar/Cancelar**: diálogo de alto fijo (420) y `ResizeMode="NoResize"` — el contenido se salía del área visible sin scroll ni forma de agrandar. Corregido con `Height="520" MinHeight="480"` y `ResizeMode="CanResizeWithGrip"`.
13. **Comentarios XML con `--` rompen `.csproj`** (`MSB4025`) — pasó varias veces en ambos `.csproj` de este proyecto; evitar doble guion dentro de comentarios XML.
14. **Comillas literales rompen un string verbatim de C#** (`@"..."`) — un comentario SQL dentro del DDL de `EsquemaSql.cs` con comillas `"así"` cerró el string antes de tiempo (155 errores de parseo en cascada). Mismo tipo de trampa que el bug 13 pero con C#, no XML — cualquier comentario dentro de un `@"..."` debe evitar comillas dobles sueltas (o escaparlas como `""`).
15. **`Total="0"` en CFDI de tipo Pago (REP)** — el comprobante de tipo `P` no trae el monto real en el atributo `Total` del nodo raíz; el monto de verdad está en el complemento de Pagos, en uno o más nodos `<Pago MontoTotalPagos="...">`. `CfdiXmlParser` ahora detecta `TipoComprobante="P"` y suma esos nodos (buscados por nombre local, sin atarse a si el complemento es Pagos 1.0 o Pagos20).
16. **Paquetes grandes (1332 CFDIs) se descargan "exitosos" pero vacíos** — `DescargaMasivaService.svc` puede responder con `<Paquete>` presente (nodo existe, `CodEstatus` de éxito) pero **con contenido base64 vacío (0 bytes)**. El código original solo revisaba que el nodo `<Paquete>` existiera, así que marcaba `Exito=true` y tronaba después al abrir un ZIP de 0 bytes ("Central Directory corrupt"), perdiendo el `CodEstatus`/`Mensaje` real. Corregido en `SatSoapClient.DescargarAsync`: un paquete de 0 bytes decodificados ahora se trata como error real, exponiendo `CodEstatus`/`Mensaje`. Con el fix, el SAT reveló el motivo real en un caso de prueba: **`CodEstatus=5008 Mensaje="Maximo de descargas permitidas"`** — el SAT SÍ contó como consumidas las 2 descargas permitidas del paquete aunque ambas regresaron vacías, dejando ese paquete irrecuperable por este canal. Sospecha (no confirmada): paquetes muy grandes (order de magnitud >1000 CFDIs) pueden estar cerca de un límite práctico del SAT que hace que el servidor falle en armar el ZIP pero aun así consuma el intento de descarga — si se repite, vale la pena partir la `SolicitaDescarga` en rangos de fecha más chicos para evitar paquetes tan grandes. `SolicitudWorker` también se blindó: si el ZIP falla al abrir por cualquier otra razón, ahora guarda los bytes crudos en disco (`_crudo_no_es_zip.bin`) en vez de perder la evidencia, y de todos modos registra el intento (el SAT ya lo contó del lado suyo aunque el cliente no haya podido usarlo).
17. **"Terminada" en la UI no significaba que los datos llegaron** (reportado directo por el usuario 2026-08-14: *"dice terminado y da los numero de CFDIS, no tiene sentido"*) — `Estatus="Terminada"` en `SolicitudDescarga` solo refleja que el SAT ya **procesó la solicitud** (`EstadoSolicitud`), algo completamente distinto de si la **descarga del paquete** funcionó (bug #16). Con el caso real de arriba, la solicitud se veía "Terminada, 1332 CFDIs" en la cola aunque cero CFDIs habían llegado a la base — engañoso. Corregido: nueva columna `SolicitudDescargaPaquete.UltimoError` (motivo real del último intento fallido, `NULL` si el último intento fue exitoso), y `SatDescargaResultado.RespuestaSatRecibida` distingue "el SAT respondió con un SOAP real (cuenta contra las 2 descargas)" de "falló antes de llegar al SAT (seguro reintentar sin penalidad)". La UI ahora muestra "Error de descarga" (agrupado en la pestaña Rechazadas/Error) en vez de "Terminada" cuando un paquete agotó sus 2 intentos sin éxito, con una columna nueva mostrando el `CodEstatus`/`Mensaje` real.

18. **Solicitudes que el SAT acepta y aquí «no funcionan» (Metadata y algunas de CFDI)** (reportado 2026-09-30: *"no me gusta que los metadatos los acepte el SAT y no funcione aquí, otros sistemas sí lo hacen"*). Causa raíz, confirmada en la base de este equipo: cuando `VerificaSolicitudDescarga` contestaba **sin un `EstadoSolicitud` válido (1 a 6, por ejemplo 0)**, `SolicitudWorker` guardaba ese valor tal cual como `Estatus = '0'`. Como la lista de pendientes solo incluye `Aceptada`/`EnProceso`, la solicitud **salía de la cola para siempre y nunca se volvía a preguntar al SAT**, aunque lo terminara minutos después. Resultado real: **26 solicitudes** quedaron en `'0'` (20 de Metadata y 6 de CFDI; ninguna con paquetes ni `UltimoCodEstatus`), y de ahí la conclusión equivocada de que *«Metadata se queda atorado en 13 de 13 intentos»* (comentario de `Cola/AutoSolicitador.cs`, y el reintento semanal que se puso por eso). La librería de referencia `phpcfdi/sat-ws-descarga-masiva` trata un `EstadoSolicitud` fuera de 1–6 como *Desconocido* y sigue consultando; no lo da por terminado. Corregido: (a) `SolicitudWorker` ya no sobrescribe el estatus con un valor inválido: conserva `Aceptada`/`EnProceso`, anota `UltimoCodEstatus`/`UltimoMensaje` (`BrosSatDb.RegistrarVerificacionSinEstado`) y lo reintenta en la siguiente pasada, escribiendo en la bitácora `EstadoSolicitud`, `CodEstatus`, `CodigoEstadoSolicitud` y `Mensaje` reales; (b) `SatSoapClient.VerificarSolicitudAsync` busca el resultado por nombre local y lee los atributos sin distinguir mayúsculas (como la librería de referencia); (c) `EsquemaSql` regresa a `EnProceso` las filas dañadas (`Estatus IN ('0','')`) para que la siguiente pasada las vuelva a verificar. **Pendiente de confirmar contra el SAT real**: qué contesta exactamente en esos casos (la bitácora ahora lo dice) y si las solicitudes antiguas ya vencieron (el SAT responderá `Vencida`, y entonces habrá que volver a solicitar ese tramo). Si Metadata ya completa bien, se puede quitar el reintento semanal de `AutoSolicitador`.

## 7. Historial de la interfaz de escritorio

La primera versión de `descargas-ui` usaba WebView2 con un panel HTML embebido (`Assets/panel.html`) — el usuario la rechazó explícitamente ("la interfaz está horrible") y pidió rediseño completo en WPF nativo, con el mismo lenguaje visual que el instalador ya pulido del addon (sidebar navy con gradiente, tarjetas blancas redondeadas, azul de acento `#2D6FE0`). Se eliminó por completo la dependencia de `Microsoft.Web.WebView2` y los archivos `ConfigWindow`/`PasswordWindow`/`panel.html` viejos.

Flujo actual:
1. Primer arranque → `ConexionWindow` (solo pide la cadena de conexión SQL) → se guarda en `config.json` junto al exe.
2. `MainWindow` conecta, asegura la BD (crea si falta) y el esquema, y **abre mostrando el listado de empresas** (tarjetas de resumen + `GridEmpresas`), con un ítem "📋 Bitácora" en el sidebar (**2026-08-14**) que abre `BitacoraWindow`.
3. "+ Agregar empresa" / botón "Editar" → `EmpresaWindow` (Nombre, RFC, examinar `.cer`/`.key`, contraseña de la FIEL) — valida la FIEL de verdad antes de guardar, cifra la contraseña con DPAPI. **Desde 2026-08-14** también sirve para **editar** una empresa ya creada (constructor con una `EmpresaFila` existente — RFC bloqueado, contraseña en blanco = "no cambiar"), y agrega 3 campos configurables por empresa: **carpeta base** de los XML (selector con `Microsoft.Win32.OpenFolderDialog`), **estructura de carpetas** (Todo en una sola carpeta / Por año / Por año y mes / Por año, tipo y mes) y **plantilla de nombre de archivo** (placeholders `{UUID} {Folio} {Serie} {RFCEmisor} {RFCReceptor} {NombreEmisor} {Fecha} {Total} {TipoComprobante}`) — pedido explícito del usuario: *"no se ni en que carpeta ni me deja escoger"*.
4. Doble clic en una empresa → panel de detalle: `GridSolicitudes` (cola) + `GridCfdi` (histórico), botones "Nueva solicitud" (abre `SolicitarWindow`, hace Autenticación + SolicitaDescarga reales) y "Revisar pendientes ahora" (corre una pasada de `SolicitudWorker` — verifica y descarga lo que ya esté listo).

### 7.1 Organización de archivos y bitácora (2026-08-14)

`descargas/Cola/OrganizadorArchivos.cs` decide dónde y cómo se guarda cada XML, usando la config de la `Empresa` que lo descargó (nunca una convención fija — el usuario explícitamente quiere control total, algunos clientes organizan por mes, otros — el usuario mismo — prefieren todo plano en una sola carpeta para pasarlo directo a Comercial Pro mientras no estén integrados):
- **Carpeta**: `Plana` (todo junto) / `Anio` / `AnioMes` / `AnioTipoMes` (`{Año}\{Recibidos|Emitidos}\{MM-NombreMes}`), calculada con la `FechaEmision` real del CFDI (no la fecha de descarga).
- **Nombre de archivo**: plantilla con placeholders, saneada contra caracteres inválidos; si dos CFDI generan el mismo nombre (plantilla ambigua sin `{UUID}`), se agrega un sufijo automático — nunca sobrescribe el XML de otro CFDI.
- `SolicitudWorker` invierte el orden de siempre: ahora **parsea el XML en memoria primero** (para tener `UUID`/`FechaEmision`/etc.) y **decide la ruta final después**, en vez de extraer a una carpeta fija por paquete y parsear después.
- **Importante**: esto solo aplica a descargas *nuevas*. Los XML ya guardados con la estructura vieja (`xml\<idPaquete>\...`) se quedan donde están — `RutaArchivoXml` en la BD sigue apuntando ahí y sigue funcionando, no se migran solos.

`descargas/Cola/Bitacora.cs` — antes todo salía por `Console.WriteLine`, que se pierde cuando el proceso corre en segundo plano vía Tarea Programada (`/RU SYSTEM`, sin consola visible). Ahora `Bitacora.Escribir`/`EscribirError` imprimen a consola (igual que antes, para uso interactivo) **y** agregan una línea con timestamp a un archivo mensual en `%LOCALAPPDATA%\BrosLMV\Descargas\logs\broslmv-YYYY-MM.log` — ubicación **fija**, no relativa a desde dónde corre el proceso. Usado en las rutas desatendidas: `SolicitudWorker`, `AutoTodasAsync`, `AutoSolicitarTodasAsync`, `VerificarEstatusAsync`. Visor dentro de la app: `BitacoraWindow` (selector de mes, botón "Abrir carpeta").

## 8. Uso del CLI (`descargas/Program.cs`)

```
dotnet run -- --cer <ruta.cer> --key <ruta.key> --password <contraseña> [--rfc <RFC>] [--conn <cadena SQL>] [--salida <archivo.xml>] [--autenticar] [--solicitar | --idsolicitud <id> | --idpaquete <id> [--salida-zip <archivo.zip>] | --auto]
   o: dotnet run -- --reparsear --conn <cadena SQL>
   o: dotnet run -- --auto-todas --conn <cadena SQL>
   o: dotnet run -- --verificar-estatus --conn <cadena SQL>
   o: dotnet run -- --auto-solicitar-todas --conn <cadena SQL>
```

- Sin flags: arma y firma el sobre de Autenticación, lo guarda en disco, lo verifica **localmente** (sin red).
- `--autenticar`: además lo manda de verdad a `Autenticacion.svc` del SAT (no gasta cupo diario).
- `--autenticar --solicitar`: además hace `SolicitaDescarga` real (Recibidos de ayer, rango de prueba fijo) — **sí gasta cupo diario**.
- `--autenticar --idsolicitud <id>`: consulta el estatus de una solicitud ya hecha — no gasta cupo diario.
- `--autenticar --idpaquete <id> [--salida-zip <archivo>]`: descarga el ZIP de un paquete ya listo — máximo 2 descargas, vive 72h.
- `--conn <cadena>`: además de imprimir en consola, persiste todo en la BD propia (crea el esquema si falta).
- `--auto --conn <cadena>`: una pasada del motor de cola (`SolicitudWorker`) para UNA FIEL (la de `--cer`/`--key`/`--password`) — revisa lo pendiente de esa empresa, verifica estatus y descarga lo listo. No requiere `--rfc` como filtro explícito (usa `RfcSolicitante`), pero solo tiene sentido con una sola empresa en la BD — con 2+ empresas, usar `--auto-todas`.
- `--reparsear --conn <cadena>` (2026-08-12): relee del disco los XML ya descargados y los vuelve a parsear con la versión actual de `CfdiXmlParser` — rellena campos que se agregaron **después** de la descarga original (p. ej. `TipoCambio`, `Retenciones`, el `Total` real de los CFDI tipo Pago). No toca la red, no requiere FIEL.
- `--auto-todas --conn <cadena>` (2026-08-13): como `--auto` pero para **todas las empresas activas del catálogo** en una sola corrida — no toma `--cer`/`--key`/`--password`, lee cada FIEL de la tabla `Empresa` y descifra su contraseña con DPAPI (mismo mecanismo que la UI). Pensado para **una sola** Tarea Programada de Windows que cubra todas las empresas sin guardar ninguna contraseña en texto plano en la definición de la tarea. No gasta cupo diario (solo verifica/descarga, nunca crea solicitudes nuevas). Protegido con `sp_getapplock` contra corridas traslapadas.
- `--auto-solicitar-todas --conn <cadena>` (2026-08-14): la ÚNICA pieza que **crea** `SolicitaDescarga` de forma automática — **sí gasta cupo diario real** (2 solicitudes por empresa por corrida: CFDI + Metadata del siguiente tramo pendiente). Calcula el rango pendiente con `BrosSatDb.ObtenerUltimaFechaCubierta` y lo parte con `SolicitudChunker.PartirEnMeses` (`descargas/Cola/SolicitudChunker.cs`, compartida con `SolicitarWindow` de la UI). Pensado para **una** Tarea Programada aparte, 1 vez al día — separada de `--auto-todas` porque tiene una regla de frecuencia totalmente distinta (gasta cupo vs. no gasta). Protegido con su propio `sp_getapplock`.
- `--verificar-estatus --conn <cadena>` (2026-08-14): refresca `EstatusSat` (Vigente/Cancelado) de **todos** los CFDI ya descargados contra el servicio público `ConsultaCFDIService.svc` del SAT (el mismo que valida el QR de cualquier factura) — no requiere FIEL ni token, no gasta cupo diario, se puede correr tan seguido como se quiera.

**Bug corregido (2026-08-13)**: `SolicitudWorker`/`ObtenerSolicitudesPendientes` no filtraban por RFC — con 2+ empresas en la BD, una pasada del worker podía intentar firmar la solicitud de la empresa B con la FIEL de la empresa A (el SAT la rechaza, porque la firma XML-DSig va atada al certificado de una sola empresa). Corregido con un parámetro `rfcFiltro` obligatorio en la práctica para `--auto-todas` y para el botón "Revisar pendientes ahora" de la UI (ya lo pasa, acotado a la empresa abierta).

## 9. Roadmap — pedido explícito por el usuario, no iniciado

En orden de dependencia lógica (no necesariamente de prioridad, pendiente de confirmar con el usuario al retomar):

1. ~~**Historial de CFDI**: filtro por Vigente/Cancelado, filtro por tipo de comprobante, buscador, forma de "limpiar" el historial.~~ **Hecho** (2026-08-12): `MainWindow` — buscador de texto libre (RFC, nombre, UUID, folio, serie, total, subtotal, fecha `YYYY-MM-DD`), combos Estatus/Tipo, checkbox "Mostrar archivados", botón Archivar/Desarchivar (`BrosSatDb.ArchivarCfdi`, columna `Archivado`, reversible, no borra fila ni XML). Historial ahora también muestra forma de pago, método de pago, moneda, tipo de cambio y UUID completo (`TipoCambio` es columna nueva, `CfdiXmlParser` ya la parsea).
2. ~~**Cola de solicitudes**: pestañas por estatus.~~ **Hecho** (2026-08-12): 4 pestañas (Todas/Pendientes/Terminadas/Rechazadas-Error) como `RadioButton` con look de tab (`GroupName`, mutuamente excluyentes), filtrado en memoria sobre el `TOP 50` ya cargado — no hace falta ida y vuelta a SQL por pestaña dado el tamaño del dataset por empresa.
3. ~~**Detalle de un XML**: doble clic abre ventana con contenido completo.~~ **Hecho** (2026-08-12, ampliado 2026-08-14): `CfdiDetalleWindow` — todos los campos (incluye Subtotal/Descuento/Impuestos/Retenciones/Total/Moneda/TipoCambio/UsoCFDI), **partidas** (`GridConceptos` — `CfdiXmlParser` ahora parsea `Conceptos`, antes no se guardaban aunque la tabla `CfdiConcepto` ya existía), botón para abrir la carpeta del XML en el Explorador, y ya lee `CfdiVinculo` (solo lectura — dice "Sin vincular" mientras el punto 4 no exista, la tabla sigue sin nada que le escriba).
4. **Asociar a empresa de Comercial Pro**: la pieza grande que falta — decidir cómo un usuario ve "este proveedor no me ha facturado" o vincula un XML a un documento de Comercial. Es integración de verdad entre dos motores/bases distintas (`CfdiVinculo.DocumentID` es hoy una referencia lógica sin FK real), vale la pena planearla aparte antes de construir.
5. **Menú de reportes**: una vez sólidos los puntos 1-4, decidir qué reportes reales hacen falta (dependen de qué tan resuelto esté el vínculo del punto 4).
6. **Descarga programada automática** (pedido explícito 2026-08-12: *"que cada cierto tiempo, el menor posible sin tener problemas con el SAT, esté descargando cfdi y metadatos para actualizar"*). Separado en 3 piezas con reglas de frecuencia MUY distintas porque el SAT las trata distinto (ver sección 3):
   - ~~**Verificar + descargar lo ya solicitado**~~ **Hecho** (2026-08-13): `--auto-todas --conn <cadena>` (sección 8) — lee el catálogo `Empresa`, descifra cada FIEL con DPAPI, corre `SolicitudWorker` por empresa (acotado por RFC, ver el bug corregido en la sección 8). `VerificaSolicitud` **no gasta cupo diario** — seguro para correr frecuente (ej. cada 5-15 min).
   - ~~**Crear solicitudes nuevas**~~ **Hecho** (2026-08-14): `--auto-solicitar-todas --conn <cadena>` — la ÚNICA pieza que crea `SolicitaDescarga` de forma automática, **sí gasta cupo diario real**. Calcula qué tan atrasada está cada empresa con `BrosSatDb.ObtenerUltimaFechaCubierta` (un rango solo cuenta como cubierto si `Estatus='Terminada'` Y ningún paquete se quedó con error sin resolver — Rechazada/Error/Vencida/Terminada-con-error nunca cuentan, así que la siguiente corrida los vuelve a pedir solos, sin contador de reintentos aparte) y pide **solo el primer tramo pendiente** vía `SolicitudChunker.PartirEnMeses` (`descargas/Cola/SolicitudChunker.cs`, compartida con la UI) — acota el gasto a **2 solicitudes por empresa por corrida** (CFDI + Metadata juntos, decisión del usuario 2026-08-14) sin importar qué tan atrasada esté; si hay meses de hueco, se pone al día gradualmente en corridas sucesivas en vez de gastar todo el cupo de un jalón. Protegido con `sp_getapplock` (recurso `BrosLMV_AutoSolicitarTodas`) para que dos corridas no se traslapen si la Tarea Programada dispara antes de que termine la anterior — `--auto-todas` tiene su propio candado independiente (`BrosLMV_AutoTodas`), así que ambos modos pueden correr al mismo tiempo sin pisarse.
   - ~~**Actualizar metadatos de CFDI ya descargados** (refrescar `EstatusSat` Vigente→Cancelado).~~ **Hecho** (2026-08-14): `SatSoapClient.ConsultarEstatusCfdiAsync` — el mismo servicio **público** que valida el QR impreso en cualquier factura (`ConsultaCFDIService.svc`, host `consultaqr.facturaelectronica.sat.gob.mx`, **sin FIEL, sin token, sin cupo**), confirmado contra el WSDL en vivo (`?wsdl`, `?xsd=xsd0`, `?xsd=xsd2`) antes de codificar. Operación `Consulta` con un solo parámetro `expresionImpresa` (formato `?re=RFCEmisor&rr=RFCReceptor&tt=Total&id=UUID`), respuesta `Estado`/`EsCancelable`/`EstatusCancelacion`/`CodigoEstatus`/`ValidacionEFOS`. Botón "Verificar estatus" en `CfdiDetalleWindow` (uno a la vez) y `--verificar-estatus --conn <cadena>` en el CLI (masivo, para Tarea Programada). **Trampa real encontrada al probar**: el `tt` debe ser el `Total` **original** que el SAT timbró, no el que `CfdiRecibido.Total` guarda para mostrar en pantalla — para CFDI tipo Pago ese original siempre es `0` (el monto real está en el complemento, ver bug #15); usar el Total "corregido" ahí regresa `No Encontrado` falso. Corregido con `BrosSatDb.TotalParaVerificarEstatus`.
   - **Hallazgo real al probar `--auto-todas` en producción (2026-08-13)**: ver bug #16 en la sección 6 — un paquete de 1332 CFDIs agotó sus 2 descargas permitidas con contenido vacío (`CodEstatus=5008`). Reintentado después con un rango similar (7+ meses) y **sí funcionó** (1340 CFDIs descargados sin problema) — parece haber sido algo puntual del SAT en ese momento, no un límite duro de tamaño de paquete. Aun así, `SolicitudChunker` parte en meses por seguridad: es la práctica que usan también apps comerciales de descarga SAT (confirmado por el usuario, "ahí sí puedo descargar de todo el año").
   - **Descartado explícitamente** (2026-08-14): automatizar el límite de ~500-2000 descargas/día del **portal web del SAT** (login con FIEL/CIEC, descarga manual factura por factura desde su página) — es un mecanismo totalmente distinto (requeriría automatizar un navegador con las credenciales del usuario iniciando sesión en el sitio del SAT), contrario a la decisión de diseño ya tomada desde el inicio del proyecto de usar solo el API SOAP oficial, no scraping.

**Cadencia recomendada — 3 Tareas Programadas de Windows separadas** (paso manual del usuario — cambiar la configuración del Programador de Tareas no es algo que se automatice desde aquí):

| Tarea | Comando | Frecuencia | ¿Gasta cupo? |
|---|---|---|---|
| Verificar + descargar | `--auto-todas --conn <cadena>` | Cada 10-15 min | No |
| **Solicitar nuevos periodos** | `--auto-solicitar-todas --conn <cadena>` | 1 vez al día (ej. 6am) | **Sí** — 2 por empresa |
| Verificar estatus (Vigente/Cancelado) | `--verificar-estatus --conn <cadena>` | 1 vez al día (opcional) | No |

```
schtasks /Create /TN "BrosLMV Descargas SAT" /TR "\"C:\ruta\a\BrosLMV.Descargas.exe\" --auto-todas --conn \"Server=localhost\compac;Database=BrosLMV_SAT;Trusted_Connection=True;TrustServerCertificate=True;\"" /SC MINUTE /MO 10 /RU SYSTEM

schtasks /Create /TN "BrosLMV Solicitar SAT" /TR "\"C:\ruta\a\BrosLMV.Descargas.exe\" --auto-solicitar-todas --conn \"Server=localhost\compac;Database=BrosLMV_SAT;Trusted_Connection=True;TrustServerCertificate=True;\"" /SC DAILY /ST 06:00 /RU SYSTEM

schtasks /Create /TN "BrosLMV Verificar Estatus SAT" /TR "\"C:\ruta\a\BrosLMV.Descargas.exe\" --verificar-estatus --conn \"Server=localhost\compac;Database=BrosLMV_SAT;Trusted_Connection=True;TrustServerCertificate=True;\"" /SC DAILY /ST 07:00 /RU SYSTEM
```
Ajustar la ruta del `.exe` publicado (`dotnet publish`). `/RU SYSTEM` evita que dependa de una sesión de usuario abierta. La tarea de "Solicitar nuevos periodos" es la única que consume cupo diario real — si se necesita apagar temporalmente sin tocar las otras dos, basta con deshabilitar solo esa tarea desde el Programador de Tareas.

Gaps técnicos conocidos, no pedidos aún por el usuario pero reales:
- ~~CLI sin usar el catálogo `Empresa`/DPAPI~~ **Resuelto** (2026-08-13): `--auto-todas` (sección 8).
- No hay empaquetado/instalador separado para `BrosLMV.Descargas`/`BrosLMV.DescargasUI` (hoy se corren desde el repo/build local, no desde un instalador distribuible) — bloquea usar la Tarea Programada de forma limpia (`schtasks` de arriba asume una ruta de `.exe` publicado que todavía no existe).

## 10. Cómo seguir probando sin gastar cupo real

Patrón ya usado y que vale la pena repetir: harnesses desechables en `descargas/_prueba_dummy/<Nombre>/` que enlazan los `.cs` reales, corridos contra bases de SQL Server desechables (`sqlcmd ... CREATE DATABASE BrosLMV_..._Prueba*`), para validar hashing, esquema/constraints, ida-y-vuelta de DPAPI, y CRUD de empresas **antes** de tocar la UI real o gastar cupo del SAT. Los datos reales de prueba (FIEL, XML descargados, ZIPs) ya están excluidos en `.gitignore` (líneas 15-23): `_pruebas_fiel/`, `FIEL_2022/`, `*.cer`, `*.key`, `*.zip`, `sobre_firmado.xml`, `xml/`, y las carpetas con nombre de GUID que crea la descarga real.
