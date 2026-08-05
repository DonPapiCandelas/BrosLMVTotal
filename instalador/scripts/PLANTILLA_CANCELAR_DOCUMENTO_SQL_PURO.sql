-- lang: sql
-- PLANTILLA: Cancelar Documento (y su cadena derivada) — SQL puro (INSERT/UPDATE directo)
--
-- Qué hace: cancela un documento indicado (@documentoID) Y toda la cadena de documentos
-- DERIVADOS de él vía SourceDocumentID -- hijos, nietos, bisnietos, etc. -- usando un CTE
-- recursivo, y revierte el efecto de cada uno en orgProductKardex. Ejemplo real: cancelar
-- una Orden de Compra (módulo 183) que ya tiene una Recepción de Compra (módulo 184)
-- derivada de ella debe cancelar AMBOS documentos y desafectar AMBOS kardex -- si solo se
-- cancela la OC, la Recepción queda huérfana con inventario recibido "fantasma" que ya no
-- tiene una OC activa detrás.
--
-- Origen de esta plantilla: se encontró analizando un stored procedure real de producción
-- de un cliente (zLCCANCELADOC, documentado en
-- entrenamiento/bacros/docs/07_sp_triggers_para_broslmv.md) que tenía 2 defectos:
--   1. Solo cancelaba UN nivel de hijos (UPDATE ... WHERE SourceDocumentID=@1) -- un nieto
--      (hijo de un hijo) quedaba sin tocar. Aquí se corrige con un CTE recursivo que sube
--      por TODOS los niveles de la cadena, no solo uno.
--   2. NO tocaba orgProductKardex -- cancelar la cabecera sin revertir el kardex deja el
--      inventario "comprometido" o "recibido" fantasma (las consultas de Stock de este
--      proyecto -- ver cualquier PLANTILLA_EJEMPLO_*_CSHARP.ctx -- SIEMPRE filtran
--      `k.Cancelled=0`). Aquí se corrige.
--
-- ⚠️ DECISIÓN DE DISEÑO (se desvía de lo sugerido originalmente -- revertir el kardex
-- "insertando el movimiento inverso con el signo de QuantityToBeDelivered según el tipo de
-- documento"): en vez de insertar filas de reversión calculando signos por tipo de módulo
-- (frágil -- cada módulo nuevo que mueva kardex tendría que enseñarle a esta plantilla su
-- convención de signo), se usa la columna `orgProductKardex.Cancelled` (bit) que YA EXISTE
-- en el esquema para exactamente este propósito: TODAS las consultas de Stock del proyecto
-- (`SELECT SUM(Quantity) FROM orgProductKardex WHERE ... AND Cancelled=0`) excluyen las filas
-- marcadas. Poner `Cancelled=1` en las filas de kardex del documento cancelado tiene el mismo
-- efecto neto que "revertir" (esas filas dejan de contar) sin necesidad de calcular signos ni
-- de conocer la convención de cada módulo -- es correcto para Orden de Compra (Quantity=0,
-- QuantityToBeDelivered=+cant), Recepción de Compra (Quantity=+cant,
-- QuantityToBeDelivered=-cant) y cualquier módulo futuro que siga la misma convención de
-- `Cancelled` para calcular Stock.
--
-- Orden de cancelación dentro de la cadena: se identifica toda la cadena ANTES de abrir la
-- transacción (tabla @cadena, vía CTE recursivo, con el Nivel de profundidad de cada
-- documento respecto al raíz). Los UPDATE de docDocument y orgProductKardex dentro de la
-- transacción son sentencias SET-BASED (afectan TODA la cadena en un solo UPDATE cada una),
-- no un loop documento por documento -- a diferencia de lo sugerido originalmente ("empezar
-- por los hijos, terminar en el raíz"), aquí el orden de aplicación no importa para la
-- atomicidad: todo corre dentro de UNA transacción con TRY/CATCH -- o se cancela la cadena
-- COMPLETA (cabecera + kardex, todos los niveles) o no se cancela nada (ROLLBACK). El campo
-- `Nivel` se conserva solo para el SELECT final de auditoría (para poder leer la cadena en
-- orden raíz→hoja).
--
-- Guard clauses (antes de abrir transacción): el documento raíz debe existir, no estar
-- eliminado (DeletedOn IS NULL) y no estar ya cancelado (CancelledOn IS NULL). Los documentos
-- de la cadena que YA estaban cancelados o eliminados se excluyen de @cadena -- no se tocan
-- de nuevo (evita marcar Cancelled=1 dos veces sobre un kardex que ya lo tenía, y evita
-- pisar un CancelledOn/CancelledBy/CancellationReasonID que ya existía con otro motivo).
--
-- CancellationReasonID: en este esquema (confirmado contra INFORMATION_SCHEMA en el sandbox)
-- es un INT sin FK real hacia un catálogo -- no hay tabla docCancellationReason ni similar en
-- esta instalación. Por eso @motivoID no se valida contra catálogo, solo se guarda tal cual.
--
-- Atomicidad: SÍ (mismo estándar que el resto de las plantillas SQL_PURO desde v2.66.0).
-- Todo el bloque (UPDATE docDocument de la cadena + UPDATE orgProductKardex de la cadena)
-- corre dentro de BEGIN TRAN/COMMIT TRAN con BEGIN TRY/CATCH y ROLLBACK si algo falla a
-- medio camino.
--
-- Cómo se llenan los datos (v2.77.0+): @documentoID (el documento RAÍZ a cancelar -- no hace
-- falta que sea el documento "más padre" de todos, la cadena se calcula hacia ABAJO desde
-- aquí, nunca hacia arriba) y @motivoID ahora se capturan con un formulario automático (token
-- tipado {DATOS:Tabla.Columna:*}, motor v2.75.0/2.76.0) -- ambos se anclan a columnas reales
-- de docDocument (DocumentID/CancellationReasonID) del mismo tipo INT. @canceladoPor NO se
-- migró: no es un dato que capture el usuario, es una convención interna fija (0 = sistema/SQL
-- puro, mismo criterio que CreatedBy=0 en el resto de plantillas del catálogo) -- pedirlo por
-- formulario no tendría sentido, siempre es el mismo valor.

-- ═══════════════════ PARÁMETROS (la mayoría se capturan con un formulario automático, v2.77.0+) ═══════════════════
DECLARE @documentoID  INT = {DATOS:docDocument.DocumentID:Documento raíz a cancelar:*};  -- DocumentID raíz a cancelar (y toda su cadena derivada)
DECLARE @motivoID     INT = {DATOS:docDocument.CancellationReasonID:Motivo de cancelación:*};    -- CancellationReasonID (entero libre, sin catálogo FK en este esquema)
DECLARE @canceladoPor INT = 0;    -- CancelledBy -- 0 = sistema/SQL puro, mismo criterio que CreatedBy=0 en el resto de plantillas (NO migrado, ver nota arriba)

-- ═══════════════════ VALIDACIONES MÍNIMAS (guard clauses, antes de abrir transacción) ═══════════════════
IF NOT EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID)
BEGIN SELECT 'ERROR: no existe el documento DocumentID=' + CAST(@documentoID AS VARCHAR) AS Resultado; RETURN; END
IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID AND DeletedOn IS NOT NULL)
BEGIN SELECT 'ERROR: el documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' está eliminado (DeletedOn no es NULL) -- no se puede cancelar' AS Resultado; RETURN; END
IF EXISTS (SELECT 1 FROM docDocument WHERE DocumentID = @documentoID AND CancelledOn IS NOT NULL)
BEGIN SELECT 'ERROR: el documento DocumentID=' + CAST(@documentoID AS VARCHAR) + ' ya está cancelado' AS Resultado; RETURN; END

-- ═══════════════════ CADENA COMPLETA (CTE recursivo, calculado ANTES de la transacción) ═══════════════════
-- Recorre TODOS los niveles vía SourceDocumentID (a diferencia de zLCCANCELADOC, que solo
-- hacía un UPDATE con WHERE SourceDocumentID=@1 -- un solo nivel). La recursión no filtra por
-- CancelledOn (para poder seguir bajando aunque un nieto intermedio ya esté cancelado por
-- separado); el filtro de "cuáles se tocan de verdad" se aplica al insertar en @cadena.
DECLARE @cadena TABLE (DocumentID INT PRIMARY KEY, Nivel INT);
;WITH cte AS (
    SELECT d.DocumentID, d.CancelledOn, d.DeletedOn, 0 AS Nivel
    FROM docDocument d
    WHERE d.DocumentID = @documentoID
    UNION ALL
    SELECT h.DocumentID, h.CancelledOn, h.DeletedOn, c.Nivel + 1
    FROM docDocument h
    INNER JOIN cte c ON h.SourceDocumentID = c.DocumentID
    WHERE h.DeletedOn IS NULL
)
INSERT INTO @cadena (DocumentID, Nivel)
SELECT DocumentID, Nivel FROM cte
WHERE CancelledOn IS NULL AND DeletedOn IS NULL;
-- OPTION (MAXRECURSION 100): límite por defecto de SQL Server para un CTE recursivo -- de
-- sobra para cadenas de documentos reales (Requisición→OC→Recepción→Factura rara vez pasa de
-- 4 niveles); si algún día se necesita más profundidad, agregar OPTION (MAXRECURSION N) al
-- INSERT de arriba.

-- ═══════════════════ TRANSACCIÓN (atomicidad real) ═══════════════════
-- Todo lo que sigue (UPDATE docDocument + UPDATE orgProductKardex, para TODA la cadena)
-- corre dentro de UNA sola transacción -- o se cancela la cadena completa, o no se cancela
-- nada. THROW (sin argumentos, SQL Server 2012+) repropaga el error original tras el ROLLBACK.
BEGIN TRY
BEGIN TRAN;

DECLARE @ahora DATETIME = GETDATE();

-- ═══════════════════ 1. Cabecera: cancelar TODOS los documentos de la cadena ═══════════════════
UPDATE d SET
    d.CancelledOn = @ahora,
    d.CancelledBy = @canceladoPor,
    d.CancellationReasonID = @motivoID
FROM docDocument d
INNER JOIN @cadena c ON c.DocumentID = d.DocumentID;

-- ═══════════════════ 2. Kardex: revertir el efecto de TODOS los documentos de la cadena ═══════════════════
-- Cancelled=1 saca estas filas de cualquier cálculo de Stock (todas las consultas del
-- proyecto filtran Cancelled=0) -- efecto neto idéntico a "revertir el movimiento", sin
-- necesidad de conocer el signo de QuantityToBeDelivered de cada tipo de documento (ver
-- advertencia de diseño al inicio del archivo).
UPDATE k SET k.Cancelled = 1
FROM orgProductKardex k
INNER JOIN @cadena c ON c.DocumentID = k.DocumentID
WHERE k.Cancelled = 0;

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH

-- Fuera del TRY a propósito -- la cancelación ya quedó comprometida (COMMIT) antes de llegar
-- aquí; un fallo leyendo este SELECT no debe interpretarse como que la transacción falló.
SELECT c.DocumentID, c.Nivel, d.ModuleID, d.Folio,
       (SELECT COUNT(*) FROM orgProductKardex k WHERE k.DocumentID = c.DocumentID) AS FilasKardexRevertidas
FROM @cadena c
INNER JOIN docDocument d ON d.DocumentID = c.DocumentID
ORDER BY c.Nivel;
