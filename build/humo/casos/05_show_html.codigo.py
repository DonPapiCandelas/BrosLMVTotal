# lang: python
# job: safe-offline
# Humo T4.1 #5: ctx.show_html() headless. A diferencia de ctx.form() (ver
# build/humo/casos/README.md), show_html NO bloquea esperando que un humano cierre la
# ventana -- HostClient.RenderUiHtml (src/HostClient.cs) regresa en cuanto la pagina termina
# de CARGAR, la ventana queda abierta en su propio hilo en segundo plano. Por eso si es
# automatizable headless de verdad, no solo "no truena".
from broslmv import ctx

ctx.show_html("<h1>Humo T4.1</h1><p>Ventana de prueba, sin interaccion humana.</p>",
              title="Humo T4.1", width=400, height=300, modal=False)

result = "show_html invocado sin excepcion"
