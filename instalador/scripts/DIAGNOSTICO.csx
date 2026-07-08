// DIAGNOSTICO.csx
// Verifica que la conexion automatica a la empresa actual funcione.
// Ejecutalo desde la Consola tras instalar en una empresa nueva.

ctx.Msg(ctx.DiagConexion(), "Diagnostico de conexion BrosLMV");

// Prueba real: nombre de la base y conteo de documentos
try {
    var bd = ctx.Scalar("SELECT DB_NAME()");
    var n  = ctx.Scalar("SELECT COUNT(*) FROM docDocument");
    ctx.Msg("Base: " + bd + "\nDocumentos en docDocument: " + n, "Conexion OK");
} catch (Exception ex) {
    ctx.Msg("No se pudo consultar: " + ex.Message, "Revisar conexion");
}
