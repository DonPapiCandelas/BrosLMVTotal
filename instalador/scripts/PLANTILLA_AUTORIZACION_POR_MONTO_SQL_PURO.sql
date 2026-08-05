-- lang: sql
-- PLANTILLA: Autorización de documento con límite de importe por firmante — SQL puro
--
-- Qué hace: autoriza (o revoca) UNO de los 2 niveles de autorización nativos de Comercial
-- Pro (docDocument.AuthorizedOn/AuthorizedBy y Authorized2On/Authorized2By) SOLO si el
-- usuario tiene permiso configurado para autorizar hasta cierto monto. Comercial Pro de
-- fábrica solo ofrece "autorizado sí/no" (dos niveles, sin límite de importe configurable
-- por firmante) -- esta plantilla agrega esa capa encima usando una tabla propia,
-- `dbo.BrosAutorizaciones`.
--
-- Origen: se identificó esta necesidad de negocio real analizando 2 stored procedures de
-- producción de un cliente real (`ZLCAUTODG`/`ZLCAUTOOP`, ver
-- entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md sección "ZLCAUTODG / ZLCAUTOOP")
-- que llevaban años funcionando con el mismo criterio: comparar `Total*Rate` del documento
-- contra `ImporteMax` de una tabla `UserID, Rol, ImporteMax`.
--
-- ⚠️ DEFECTO CORREGIDO respecto al original: el SP de referencia hacía TOGGLE -- la misma
-- llamada autorizaba si estaba desautorizado y desautorizaba si ya estaba autorizado
-- (`CASE WHEN AuthorizedOn IS NULL THEN GETDATE() ELSE NULL END`). Eso es peligroso: correr
-- el script 2 veces por accidente (doble clic, timeout con reintento automático, etc.)
-- revoca una autorización real sin que nadie lo pida. Esta plantilla separa explícitamente
-- "autorizar" de "revocar" con el parámetro @accion -- nunca hace lo contrario de lo que se
-- pidió. Se eligió UN SOLO script con @accion (en vez de 2 plantillas separadas) porque
-- ambas operaciones comparten el 90% del cuerpo (mismas validaciones de documento/nivel,
-- mismo UPDATE de 2 columnas) -- separarlas hubiera duplicado ese código sin ganar nada;
-- el patrón `@accion` ya existe implícitamente en otras plantillas del catálogo vía guard
-- clauses tempranas, así que es consistente con el estilo del proyecto.
--
-- Tabla propia `dbo.BrosAutorizaciones`: a diferencia de las tablas `zzBros*` (zzBrosScript,
-- zzBrosAuditoria, etc.), que las provisiona el INSTALADOR una sola vez por empresa
-- (instalador\sql\provision_empresa.sql, patrón `IF NOT EXISTS (SELECT 1 FROM sys.tables...)`
-- + ALTER TABLE idempotente), esta tabla se crea aquí, dentro de la propia plantilla, con
-- el mismo patrón idempotente (`IF OBJECT_ID(...) IS NULL BEGIN CREATE TABLE ... END`).
-- Se decidió así (y no agregarla a provision_empresa.sql) porque las plantillas
-- `PLANTILLA_*_SQL_PURO.sql` de este catálogo son scripts autocontenidos que el usuario
-- pega y corre directo (Consola BrosLMV o sqlcmd), sin pasar por el instalador completo --
-- igual que ninguna de las otras 8 plantillas SQL puro depende de que el instalador les
-- cree infraestructura previa. Es idempotente: correrla varias veces no duplica la tabla ni
-- borra datos ya configurados.
--
-- Atomicidad: SÍ. El UPDATE de docDocument corre dentro de BEGIN TRAN/COMMIT TRAN con
-- BEGIN TRY/CATCH y ROLLBACK si algo falla -- mismo patrón que
-- PLANTILLA_ORDEN_COMPRA_SQL_PURO.sql. Aquí solo hay un UPDATE de una fila, así que el
-- riesgo de fallo a medio camino es bajo, pero se mantiene el patrón por consistencia con
-- el resto del catálogo "SQL puro" (nota de arquitectura en
-- entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md, recomendación #6).
--
-- Cómo se llenan los datos (v2.77.0+): @documentoID, @usuarioID y @nivel ahora se capturan
-- con un formulario automático (token tipado {DATOS:Tabla.Columna:*}, motor v2.75.0/2.76.0).
-- @documentoID se ancla a docDocument.DocumentID (correspondencia 1:1 real). @usuarioID se
-- ancla a engUser.UserID (tabla nativa de Comercial, correspondencia 1:1 real -- NO a
-- dbo.BrosAutorizaciones.UserID: esa tabla la crea la sección 0a de ESTE MISMO script, y
-- ResolverFormularioTokens resuelve TODOS los tokens del texto completo ANTES de ejecutar
-- nada -- en una base de datos donde nadie ha corrido este botón todavía, BrosAutorizaciones
-- no existe aún cuando se consulta INFORMATION_SCHEMA.COLUMNS, y el motor abortaría con "no
-- existe" antes de llegar a la sección 0a que la crearía). @nivel se ancla a
-- docDocument.ModuleID (mismo motivo -- necesita una columna INT que exista SIEMPRE, no una
-- que dependa de que este script ya se haya corrido antes; el dominio real de @nivel sigue
-- siendo 1 o 2, validado más abajo). @accion NO se migró: es un valor de texto restringido a 2
-- opciones fijas ('AUTORIZAR'/'REVOCAR') sin una columna de catálogo detrás -- el motor de hoy
-- solo sabe pedir un campo de texto libre según el tipo de una columna real, no un combo de
-- opciones fijas (esa es una fase futura, ver también @accion en la advertencia de arriba
-- sobre el TOGGLE); seguir editándolo a mano evita que alguien escriba un valor fuera de esas 2
-- opciones sin darse cuenta -- la validación de abajo lo rechaza de todos modos, pero un combo
-- real sería más claro para el usuario final.
--
-- Configura primero quién puede autorizar qué monto insertando/actualizando filas en
-- dbo.BrosAutorizaciones (ver sección 0b más abajo para un ejemplo de INSERT).

-- ═══════════════════ 0a. Tabla de configuración (idempotente) ═══════════════════
IF OBJECT_ID('dbo.BrosAutorizaciones') IS NULL
BEGIN
    CREATE TABLE dbo.BrosAutorizaciones (
        UserID     INT           NOT NULL,
        Nivel      INT           NOT NULL,  -- 1 = Authorized*  / 2 = Authorized2*
        Rol        NVARCHAR(50)  NULL,      -- texto libre, solo informativo (p.ej. 'GERENTE_COMPRAS')
        ImporteMax DECIMAL(18,2) NOT NULL DEFAULT 0,  -- 0 = sin límite (autoriza cualquier monto)
        CONSTRAINT PK_BrosAutorizaciones PRIMARY KEY (UserID, Nivel),
        CONSTRAINT CK_BrosAutorizaciones_Nivel CHECK (Nivel IN (1, 2))
    );
END

-- ═══════════════════ 0b. Ejemplo de cómo dar de alta un firmante (edítalo y corre aparte) ═══════════════════
-- IF NOT EXISTS (SELECT 1 FROM BrosAutorizaciones WHERE UserID = 1 AND Nivel = 1)
--     INSERT INTO BrosAutorizaciones (UserID, Nivel, Rol, ImporteMax) VALUES (1, 1, 'GERENTE_COMPRAS', 50000);

-- ═══════════════════ PARÁMETROS (la mayoría se capturan con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @documentoID INT = {DATOS:docDocument.DocumentID:Documento a autorizar/revocar:*};              -- DocumentID del documento a autorizar/revocar
DECLARE @usuarioID   INT = {DATOS:engUser.UserID:Usuario que firma (UserID):*};              -- UserID que firma (debe existir en BrosAutorizaciones para @nivel si @accion='AUTORIZAR')
DECLARE @nivel       INT = {DATOS:docDocument.ModuleID:Nivel (1 o 2):*};              -- 1 = primer nivel (AuthorizedOn/By) / 2 = segundo nivel (Authorized2On/By)
DECLARE @accion      NVARCHAR(10) = 'AUTORIZAR';  -- 'AUTORIZAR' o 'REVOCAR' -- nunca un toggle (NO migrado, ver nota arriba)

-- ═══════════════════ VALIDACIONES MÍNIMAS (guard clauses, antes de abrir transacción) ═══════════════════
IF @nivel NOT IN (1, 2)
BEGIN SELECT 'ERROR: @nivel debe ser 1 o 2, recibido=' + CAST(@nivel AS VARCHAR) AS Resultado; RETURN; END

IF UPPER(@accion) NOT IN ('AUTORIZAR', 'REVOCAR')
BEGIN SELECT 'ERROR: @accion debe ser ''AUTORIZAR'' o ''REVOCAR'', recibido=''' + @accion + '''' AS Resultado; RETURN; END

IF NOT EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID)
BEGIN SELECT 'ERROR: no existe el documento DocumentID=' + CAST(@documentoID AS VARCHAR) AS Resultado; RETURN; END

IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID AND (CancelledOn IS NOT NULL OR DeletedOn IS NOT NULL))
BEGIN SELECT 'ERROR: el documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' está cancelado o eliminado, no se puede autorizar/revocar' AS Resultado; RETURN; END

-- La validación de importe SOLO aplica para AUTORIZAR -- REVOCAR nunca necesita permiso de
-- monto (revocar siempre debe poder hacerse, es la operación "segura"/reversible).
DECLARE @importeMax DECIMAL(18,2) = NULL;
DECLARE @totalDoc DECIMAL(18,2) = NULL;
IF UPPER(@accion) = 'AUTORIZAR'
BEGIN
    IF NOT EXISTS (SELECT 1 FROM BrosAutorizaciones WHERE UserID = @usuarioID AND Nivel = @nivel)
    BEGIN SELECT 'ERROR: el usuario UserID=' + CAST(@usuarioID AS VARCHAR) + ' no tiene autorización configurada para el nivel ' + CAST(@nivel AS VARCHAR) + ' en BrosAutorizaciones' AS Resultado; RETURN; END

    SELECT @importeMax = ImporteMax FROM BrosAutorizaciones WHERE UserID = @usuarioID AND Nivel = @nivel;
    SELECT @totalDoc = Total * ISNULL(Rate, 1) FROM docDocument WHERE DocumentID = @documentoID;

    -- Mismo criterio que el original ZLCAUTODG/ZLCAUTOOP: ImporteMax=0 significa "sin
    -- límite" (autoriza cualquier monto).
    IF NOT (@importeMax = 0 OR @totalDoc <= @importeMax)
    BEGIN
        SELECT 'ERROR: el monto del documento ($' + CAST(@totalDoc AS VARCHAR) + ') excede el límite autorizado del usuario UserID=' + CAST(@usuarioID AS VARCHAR) + ' para el nivel ' + CAST(@nivel AS VARCHAR) + ' ($' + CAST(@importeMax AS VARCHAR) + ')' AS Resultado;
        RETURN;
    END
END

-- ═══════════════════ TRANSACCIÓN (atomicidad real) ═══════════════════
BEGIN TRY
BEGIN TRAN;

IF UPPER(@accion) = 'AUTORIZAR'
BEGIN
    IF @nivel = 1
        UPDATE docDocument SET AuthorizedOn = GETDATE(), AuthorizedBy = @usuarioID WHERE DocumentID = @documentoID;
    ELSE
        UPDATE docDocument SET Authorized2On = GETDATE(), Authorized2By = @usuarioID WHERE DocumentID = @documentoID;
END
ELSE -- REVOCAR: siempre limpia el nivel pedido, sin importar cuántas veces se haya llamado AUTORIZAR antes -- nunca un toggle.
BEGIN
    IF @nivel = 1
        UPDATE docDocument SET AuthorizedOn = NULL, AuthorizedBy = NULL WHERE DocumentID = @documentoID;
    ELSE
        UPDATE docDocument SET Authorized2On = NULL, Authorized2By = NULL WHERE DocumentID = @documentoID;
END

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- el UPDATE ya quedó comprometido (COMMIT) antes de llegar
-- aquí; un fallo leyendo este SELECT no debe interpretarse como que la transacción falló.
SELECT
    CASE WHEN UPPER(@accion) = 'AUTORIZAR'
        THEN 'Documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' AUTORIZADO en nivel ' + CAST(@nivel AS VARCHAR) + ' por UserID=' + CAST(@usuarioID AS VARCHAR)
        ELSE 'Documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' REVOCADO en nivel ' + CAST(@nivel AS VARCHAR)
    END AS Resultado;
