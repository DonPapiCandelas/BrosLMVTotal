// job: safe-offline
// Caso de humo #34 (v2.99.0): funciones nativas de Comercial agregadas a ctx.erp (esquema, parametros,
// fecha/texto, QR, costos y, si el sandbox tiene un cobro sin timbrar, recalculo de cobros).
// El Runner descarta el "return" de los scripts C#, asi que el resultado se deja en zzBrosPref (patron del caso 33).
int ok = 0, mal = 0; var det = new System.Text.StringBuilder();
Action<string, bool> T = (n, c) => { if (c) ok++; else { mal++; det.Append(n + ";"); } };
T("TableExists", ctx.erp.TableExists("docDocument") && !ctx.erp.TableExists("noExisteEstaTabla"));
T("FieldExistsInTable", ctx.erp.FieldExistsInTable("Folio", "docDocument") && !ctx.erp.FieldExistsInTable("NoExiste", "docDocument"));
T("GetModuleIDDocumentType", ctx.erp.GetModuleIDDocumentType(5, 1) == 21 && ctx.erp.GetModuleIDDocumentType(5, 2) == 152);
T("GetModuleDLLName", ctx.erp.GetModuleDLLName(21) == "Document");
ctx.erp.SaveDefaultValue("BROS_HUMO34", "uno", "humo", 1);
T("SaveDefaultValue/GetDefaultValue", ctx.erp.GetDefaultValue("BROS_HUMO34") == "uno");
ctx.erp.SaveDefaultValue("BROS_HUMO34", "dos");
T("SaveDefaultValue sobrescribe", ctx.erp.GetDefaultValue("BROS_HUMO34", 1) == "dos" && ctx.erp.GetDefaultValue("BROS_HUMO34", 2) == "");
ctx.NonQuery("DELETE FROM engParameter WHERE [Key]='BROS_HUMO34'");
T("GetLastDayMonth", ctx.erp.GetLastDayMonth(new DateTime(2026, 2, 10)) == 28 && ctx.erp.GetLastDayMonth(new DateTime(2028, 2, 10)) == 29);
T("DateFromString", ctx.erp.DateFromString("2026-10-01", "12:30:00") == new DateTime(2026, 10, 1, 12, 30, 0));
T("GetFormatedDateValue", ctx.erp.GetFormatedDateValue(new DateTime(2026, 10, 1)) == "2026-10-01 00:00:00");
T("Pad", ctx.erp.Pad("7", 5, "0", "L") == "70000" && ctx.erp.Pad("7", 5, "0", "R") == "00007");
T("TruncateDouble", Math.Abs(ctx.erp.TruncateDouble(12.98765, 2) - 12.98) < 1e-9 && Math.Abs(ctx.erp.TruncateDouble(-3.999, 1) + 3.9) < 1e-9);
T("GetSerialNumber*", ctx.erp.GetSerialNumberPrefix("ABC00123") == "ABC" && ctx.erp.GetSerialNumberNumValue("ABC00123") == "00123");
T("GetFormatedXML", ctx.erp.GetFormatedXML("<a><c>t</c></a>").Contains("\n"));
byte[] png = null; try { png = Convert.FromBase64String(ctx.erp.GetQRCode("https://example.com/x")); } catch { }
T("GetQRCode (PNG)", png != null && png.Length > 100 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);
T("GetMaxValueField", ctx.erp.GetMaxValueField("DocumentID", "docDocument") >= 0);
ctx.erp.GetCostLast(1); T("GetCostLast sin tronar", true);
var ops = ctx.Query("SELECT TOP 1 p.FinancialOperationID op, p.DocumentID doc FROM docDocumentPayment p WHERE p.DeletedOn IS NULL AND p.Amount>0 AND p.AltID=0 AND EXISTS (SELECT 1 FROM docFinancialOperationTaxDetail t WHERE t.FinancialOperationID=p.FinancialOperationID AND t.Amount>0) ORDER BY p.FinancialOperationID DESC");
string cobros = "SKIP(no hay cobro con reparto en el sandbox)";
if (ops.Count > 0)
{
    int op = Convert.ToInt32(ops[0]["op"]), doc = Convert.ToInt32(ops[0]["doc"]);
    Func<int> n = () => Convert.ToInt32(ctx.Scalar("SELECT COUNT(*) FROM docFinancialOperationTaxDetail WHERE FinancialOperationID=" + op + " AND Amount<>0"));
    int antes = n();
    // Prueba NO destructiva: recalcular no debe cambiar un cobro consistente (control de las pruebas de laboratorio).
    ctx.erp.SaveAllTaxesPayment(op); T("SaveAllTaxesPayment idempotente", n() == antes && string.IsNullOrEmpty(ctx.erp.LastError));
    ctx.erp.AjustarSaldosInsolutos(op); T("AjustarSaldosInsolutos sin error", string.IsNullOrEmpty(ctx.erp.LastError));
    ctx.erp.RecalcPagosDocumento(doc); T("RecalcPagosDocumento sin error", string.IsNullOrEmpty(ctx.erp.LastError));
    cobros = "OK(op=" + op + ")";
}
string resultado = "PASS=" + ok + ";FAIL=" + mal + ";cobros=" + cobros + (mal > 0 ? "|FALLAS=" + det : "");
ctx.NonQuery("DELETE FROM zzBrosPref WHERE Usuario=999906 AND Tipo='HUMO34_RESULT'");
ctx.NonQuery("INSERT INTO zzBrosPref (Usuario, Tipo, Valor) VALUES (999906, 'HUMO34_RESULT', N'" + resultado.Replace("'", "''") + "')");
return resultado;
