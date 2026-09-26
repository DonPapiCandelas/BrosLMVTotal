// job: safe-offline
// timeout: 30
// Humo #15: ctx.ShowHtmlFormulario() para C# -- canal de 2 vias directo (sin pipe, el
// script ya corre en proceso en el addon), reusa el mismo RenderUiHtml que Python (caso
// 12) via HostClient.RenderUiHtmlDirecto. La pagina se auto-envia via JS (sin humano).
string html = "<html><body><script>window.chrome.webview.postMessage(JSON.stringify({nombre:'PruebaCSharp',cantidad:7}));</script></body></html>";
var r = ctx.ShowHtmlFormulario(html, "Humo 15 - ShowHtmlFormulario C#", 400, 300, 15000);

if (!(bool)r["submitted"]) throw new Exception("Se esperaba submitted=true, se recibio: " + r["submitted"]);
if (r["nombre"].ToString() != "PruebaCSharp") throw new Exception("nombre incorrecto: " + r["nombre"]);
if (Convert.ToInt32(r["cantidad"]) != 7) throw new Exception("cantidad incorrecta: " + r["cantidad"]);
