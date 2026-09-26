# lang: python
# job: safe-offline
# timeout: 30
# Humo #12: ctx.show_html_formulario() -- canal de 2 vias agregado a WebView2 (antes
# ctx.show_html solo podia MOSTRAR, nunca recibir datos de vuelta). La pagina se auto-envia
# via JS (sin humano) para que esto sea automatizable: en uso real, seria un boton "Guardar"
# el que llama window.chrome.webview.postMessage(...).
from broslmv import ctx

html = """<html><body><script>
window.chrome.webview.postMessage(JSON.stringify({nombre: "Prueba", cantidad: 42}));
</script></body></html>"""

r = ctx.show_html_formulario(html, title="Humo T3.1 - show_html_formulario", timeout_ms=15000)

if not r.get("submitted"):
    raise ValueError("Se esperaba submitted=True, se recibio: " + str(r))
if r.get("nombre") != "Prueba" or r.get("cantidad") != 42:
    raise ValueError("Datos incorrectos recibidos de vuelta: " + str(r))

result = "show_html_formulario OK: " + str(r)
