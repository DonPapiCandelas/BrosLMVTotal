# Ventanas y UI de los botones (modal vs. modeless)

> Cómo abrir ventanas desde un botón/script. Resuelve la pregunta: *¿se puede abrir una ventana
> que NO sea modal — que el usuario minimice y siga trabajando en Comercial?* **Sí, se puede.**

## 1. El contexto

- Los scripts **C#** corren **en proceso** dentro de `ComercialSP.exe`, en su hilo principal (UI/STA).
  Tienen acceso directo a WinForms (`System.Windows.Forms`) y a `ctx`/`ctx.erp`.
- Hasta v2.12.0 toda la UI propia era **modal**: `MessageBox`, y la consola con `ShowDialog()`.
  Eso era una decisión, no un límite. **Desde v2.13.0 la consola es modeless** (`Show()`): es el
  ejemplo real de la Forma A (ver `ClsMain.ExecuteFunction` caso `CONSOLA` — única instancia,
  restaurar/al-frente si ya está abierta, refrescar contexto en `Activated`).
- Los scripts **Python** corren **fuera de proceso**; su UI sigue el modelo **doble-render (D9)**:
  Python *describe* la ventana (`UiForm`/`UiShowHtml` en el proto) y el addon C# la *renderiza*
  en proceso. Por eso todo lo de abajo (renderizado C#) aplica igual a la UI de Python.

## 2. Dos formas de abrir una ventana modeless

### A) Modeless en el hilo de Comercial — `Form.Show()`  ✅ recomendada
En lugar de `ShowDialog()` (bloquea), se usa `form.Show()`:
- Se **minimiza, mueve y convive** con Comercial; el usuario sigue trabajando en el ERP.
- La **bombea el message loop de Comercial** (no hace falta hilo propio).
- Puede llamar `ctx`, `ctx.erp`, SQL **sin marshaling**, porque corre en el mismo hilo/STA que XEngine.

**Reglas para que NO truene:**
1. **Mantener viva la ventana:** `Application.OpenForms` ya la enraíza mientras esté abierta
   (no la recoge el GC al terminar el script). Si se guarda en una colección estática propia,
   removerla al cerrar (`FormClosed`) para no fugar memoria.
2. **`try/catch` en cada handler de evento**, para que un error en la ventana no tumbe Comercial.
3. **Sin `Owner`** (o `ShowInTaskbar = true`) → ventana independiente, se minimiza por su cuenta.
   Con `Owner = <ventana de Comercial>` → se queda pegada encima del ERP.
4. **⚠️ Cuidado con el estado capturado al abrir (empresa/motor).** Una ventana modeless de larga
   vida **captura `XEngineLib` y la empresa activa en el momento de abrirse**. Si el usuario
   **cambia de empresa** en Comercial sin cerrar la ventana, esta seguiría ejecutando contra el
   motor de la empresa **original** → riesgo de escribir en la BD equivocada. Patrón obligatorio:
   guardar la empresa inicial, **vigilarla** (en `Activated`) y **avisar/confirmar** si cambió, o
   recargar el motor. La consola lo implementa así (v2.13.0): aviso en rojo + confirmación antes de
   ejecutar (ver `AvisarSiCambioEmpresa`/`EmpresaCambio` en `Consola.cs`).

### B) Hilo STA propio + `Application.Run`  (para ventanas de larga vida)
Una ventana 100% independiente, con su propio message loop (un dashboard que se deja abierto horas).
- **Caveat:** XEngine y la conexión viva viven en el **hilo principal** de Comercial. Si esta
  ventana llama `ctx.erp`/SQL, hay que **marshalar al hilo principal** (`Invoke` a un control de
  Comercial / un `SynchronizationContext` capturado al abrir). Más potente, más delicado.

**Regla general:** usar **(A)** salvo que se necesite una ventana que siga viva e interactiva aun
con diálogos modales de Comercial encima. (A) cubre ~95% de los casos.

## 3. Demo verificable (prueba A)

Script de prueba: `.temp_tests/demo_ventana_modeless.ctx`. Abre una ventana con un botón
"Consultar seleccionado" que lee el documento activo de Comercial **en vivo**.

> **Al probar desde la CONSOLA:** la consola es modal (`ShowDialog`), así que mientras esté
> abierta la ventana nueva aparece **deshabilitada**. Ejecuta el script → **cierra la consola** →
> la ventana queda activa (minimízala, muévete a Comercial, pulsa el botón). Como **botón** del
> ribbon no pasa esto: el script retorna y la ventana queda libre desde el inicio.

Esquema mínimo:
```csharp
var f = new Form { Text = "Mi ventana", ShowInTaskbar = true };
var btn = new Button { Text = "Consultar", Left = 16, Top = 16, Width = 200 };
btn.Click += (s, e) => { try { /* ctx.GetSelectedIds(), ctx.Query(...), ctx.erp.* */ }
                         catch (Exception ex) { MessageBox.Show(ex.Message); } };
f.Controls.Add(btn);
f.Show();   // NO modal: el script termina y la ventana sigue viva (Application.OpenForms la enraíza).
```

## 5. Botones **Python** modeless (v2.19.0) — un problema distinto

Python NO corre en el hilo de Comercial: cada botón lanza `BrosLMV.Host.exe` + `python.exe`
**en otro proceso** (con su propio *message loop*, así que su ventana ya podía minimizarse por su
cuenta). El problema real era otro: **`ClsMain.EjecutarPython` esperaba (síncrono) todo el
intercambio con el host** — con una ventana interactiva abierta minutos, Comercial se congelaba
igual, aunque la ventana en sí viviera en otro proceso.

**Fix:** `ClsMain.EjecutarPython` congela el contexto del botón (rápido, sin I/O) y lanza el resto
en `Task.Run` — Comercial recupera el control de inmediato. Pero `ctx.query`/`ctx.erp` **siguen
necesitando tocar el COM de XEngine desde el hilo de Comercial** (es lo que anticipaba el caveat
de la Forma B, arriba): por eso `CtxSqlRunner`/`CtxErpRunner` (`HostClient.cs`) remiten cada
llamada real con **`UiPump.Invoke(...)`** de vuelta a ese hilo. `UiPump` (`ClsMain.cs`) es un
control invisible creado una sola vez (`UiPump.Asegurar()`, al entrar a `ExecuteFunction`).

```
Comercial (clic) → ExecuteFunction → congela contexto → Task.Run (fondo) → host/python
                                                              │
                          ctx.query / ctx.erp ── UiPump.Invoke ──→ hilo de Comercial (COM real)
```

Esto permite **varios botones Python a la vez**: cada uno en su propio `Task`; sus llamadas a
`ctx.*` se sirven una por una (en orden, mismo hilo) — sin que un botón bloquee a otro más que el
tiempo real de su propia llamada SQL/COM. **Verificado offline** (sin Comercial) con un arnés que
simula el hilo de Comercial con su propio *message loop* real: `.temp_tests/harness_pythonui/TestUiPump.cs`.

## 6. Estado y plan

- **Hecho:** (A) modeless para botones C# y Python (v2.13.0 consola, v2.18.1/2.19.0 plantillas y
  botones Python). Blindaje `try/catch` en manejadores que hacen SQL (necesario en modeless).
- **Siguiente:**
  1. Helper en el addon para abrir ventanas modeless de forma estándar (registro/cierre limpio,
     tamaño/posición), reutilizable desde C# y desde el doble-render de Python.
  2. Exponerlo a **Python (D9)**: `ctx.form(...)` / `ctx.show_html(...)` modeless renderizados por
     el addon. Ver el contrato `UiForm`/`UiShowHtml` en [`../protocol/broslmv.proto`](../protocol/broslmv.proto)
     y el diseño en [`ARQUITECTURA_V3.md`](ARQUITECTURA_V3.md).

