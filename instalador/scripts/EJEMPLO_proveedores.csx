// EJEMPLO_proveedores.csx
// Lista proveedor, folio y total de cada documento seleccionado.

var ids = ctx.GetSelectedIds();
if (ids.Count == 0) { ctx.Msg("No hay documentos seleccionados."); return; }

var filas = ctx.Query(
    "SELECT d.Folio, be.OfficialName AS Proveedor, d.Total " +
    "FROM docDocument d " +
    "LEFT JOIN orgBusinessEntity be ON be.BusinessEntityID = d.BusinessEntityID " +
    "WHERE d.DocumentID IN (" + ctx.JoinIds(ids) + ")");

var sb = new System.Text.StringBuilder();
foreach (var f in filas)
    sb.AppendLine(f["Folio"] + "  |  " + f["Proveedor"] + "  |  $" + f["Total"]);

ctx.Msg(sb.ToString(), filas.Count + " documento(s)");
