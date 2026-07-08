// EJEMPLO_suma.csx
// Suma el Total de los documentos seleccionados en el grid.
// 'ctx' ya viene inyectado por el motor.

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("No hay documentos seleccionados."); return; }

var total = ctx.Scalar(
    "SELECT SUM(Total) FROM docDocument WHERE DocumentID IN (" + ctx.JoinIds(ids) + ")");

ctx.Msg("Documentos: " + ids.Count + "\nSuma Total: $" + total, "Resultado");
