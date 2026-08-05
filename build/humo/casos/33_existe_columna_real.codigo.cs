// job: safe-offline
// Caso de humo #33: valida HostClient.ExisteColumnaReal (extraido de ResolverFormularioTokens
// en v2.79.0) directamente contra el sandbox real -- exactamente el metodo que ahora tambien
// usa Consola.cs en el doble clic del panel "REFERENCIAS -> Campo" para decidir si ofrece el
// token nuevo {DATOS:docDocument.Campo:*} o el viejo {DATOS:Campo}.
//
// NOTA: el Runner (runner\Program.cs) descarta el valor de "return" de los scripts C# (solo
// lo usa el panel de Salida de la Consola en vivo, dentro de Comercial) -- así que el
// resultado se deja en zzBrosPref para que el .ps1 lo lea de vuelta con sqlcmd, en vez de
// depender de stdout.
string dtOk; int? mlOk;
bool okReal = HostClient.ExisteColumnaReal("docDocument", "Title", ctx, out dtOk, out mlOk);

string dtFalsa; int? mlFalsa;
bool okFalsa = HostClient.ExisteColumnaReal("docDocument", "AlgoQueNoExiste999", ctx, out dtFalsa, out mlFalsa);

string resultado = "Title=" + okReal + ";DataType=" + dtOk + ";MaxLen=" + (mlOk.HasValue ? mlOk.Value.ToString() : "NULL") +
       "|Falsa=" + okFalsa;

ctx.NonQuery("DELETE FROM zzBrosPref WHERE Usuario=999905 AND Tipo='HUMO33_RESULT'");
ctx.NonQuery("INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES (999905, 'HUMO33_RESULT', N'" + resultado.Replace("'", "''") + "')");

return resultado;
