# Diseño — tokens visuales de BrosLMV

> Extraído de lo que **ya está construido y en producción** (no inventado). Tres
> superficies distintas (la Consola en WinForms, `ConfiguracionFormato.ctx`/`htmlpdf/`
> en WebView2, y el Punto de Venta en WPF) convergieron solas en una paleta casi
> idéntica — esto lo pone por escrito para que la próxima pantalla parta de aquí en
> vez de inventar de nuevo. Cuando construyas algo visual nuevo (WPF, HTML/WebView2,
> o WinForms), usa estos valores.

## Paleta

### Consola desde v3.1.4

La Consola usa superficies gris claro (`#EBEDF0`, cabecera `#DCE0E5`) y editor Scintilla
`#E8EAED`, texto `#262C35`. El borde del editor es gris `#BBC3CD`; palabras clave
azules `#264F98`, cadenas `#99383A` y comentarios `#526A56`. La linea activa usa
`#D8DFE7` y la seleccion `#B8CCE7`, sin blanco intenso ni fondo oscuro.
Estos tokens son locales a la Consola: no cambian `AppTheme` ni las otras ventanas.
La biblioteca queda a la izquierda y la salida debajo. El inspector conserva contexto,
referencias y tokens, y es visible desde el inicio; **Contexto y SDK** lo alterna.
Los controles siguen siendo
WinForms y Scintilla, no una página HTML ni otro motor de scripts.
La altura del contexto se adapta al espacio disponible (160-214 px logicos); sus
campos siguen accesibles por desplazamiento. En la ventana minima, las referencias
conservan al menos 100 px de altura en la prueba de layout. La busqueda reacomoda sus
controles en filas si no caben, sin superponerlos al editor.

Para comprobar tamaños y estados sin SQL: `build/consola/verificar_diseno.ps1` compila
una copia temporal con arranque aislado, prueba las transiciones y genera capturas
en `build/out/consola-diseno`. Esa copia no se distribuye ni reemplaza el addon.

| Token | Valor | Uso |
|---|---|---|
| **Acento** (primario) | `#2D6FE0` | Botones primarios, enlaces, foco. Variante hover: `#1f56b8`. |
| Acento (Consola/WinForms, heredado) | `#2563EB` | `AppTheme.Primary` — muy cercano al de arriba; no urge migrarlo, pero toda pantalla **nueva** usa `#2D6FE0`. |
| Tinta (texto principal) | `#16263A` | Texto principal sobre fondo claro. |
| Tinta (Consola) | `#1F2937` | Equivalente en `AppTheme.TextMain` — mismo rol, tono ligeramente distinto por ser más viejo. |
| Muted (texto secundario) | `#64748B` | Etiquetas, texto de apoyo, placeholders. |
| Fondo de app | `#F4F6FA` / `#EDF2F9` | Fondo general de ventana — nunca blanco puro (da sensación de profundidad). |
| Tarjeta/superficie | `#FFFFFF` | Tarjetas, paneles, inputs. |
| Borde | `#E5E9F0` / `#DCE3ED` | Bordes de tarjetas/inputs. Borde suave: `#EEF1F6`. |
| Éxito | `#16A34A` | Confirmaciones, estado OK, botón "Aprobar". |
| Error | `#DC2626` | Errores, botón destructivo, estado ERROR. |
| Aviso | `#D97706` | Advertencias (no error, no éxito). |

**Nunca un gris puro** (`#808080`-style) — todos los grises de la tabla llevan un
sesgo azulado deliberado, a tono con el acento. Es lo que hace que la Consola, el POS
y los formatos PDF se vean de la misma familia sin ser idénticos.

## Tipografía

| Rol | Pila de fuentes |
|---|---|
| UI general (WinForms/WPF) | `Inter, Segoe UI Variable Text, Segoe UI, Aptos` (WinForms elige la primera instalada — `PickFont` en `AppTheme`). WPF usa `Segoe UI` directo. |
| UI general (HTML/WebView2) | `'Segoe UI', Arial, sans-serif` |
| Monoespaciada (código, tickets, hashes) | `Cascadia Code, JetBrains Mono, Consolas` (WinForms) / `ui-monospace, Consolas, monospace` (HTML) / `Cascadia Mono, Consolas` (WPF) |
| Iconos (solo WinForms) | `Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol` |

## Patrones ya construidos (reusar, no reinventar)

- **Switch on/off** (`ConfiguracionFormato.ctx`): `<label class='sw'>` con track+thumb
  animado — usarlo tal cual para cualquier opción booleana nueva en WebView2, no un
  checkbox plano.
- **Tarjeta con sombra suave**: `border-radius` entre 8-12px, `box-shadow` ligero,
  nunca bordes duros ni esquinas cuadradas — así se ven la Consola, el POS y los
  formatos.
- **Toast de confirmación** (`ConfiguracionFormato.ctx`, función `toast()`): mensaje
  flotante que aparece/desaparece solo — usarlo en vez de un `MessageBox`/`alert()`
  para confirmaciones no bloqueantes.
- **`[hidden]{display:none!important}`**: cualquier pantalla WebView2 con pestañas
  DEBE incluir esta regla — sin ella, una regla CSS de mayor especificidad puede
  pisar el `hidden` nativo y apilar el contenido de dos pestañas (bug real, ya
  ocurrió y se corrigió en `ConfiguracionFormato.ctx`).

## Cuándo usar cada tecnología de UI

| Si necesitas... | Usa |
|---|---|
| Una ventana nueva dentro de la Consola (WinForms, vive en el proceso de Comercial) | `AppTheme` (`src/Consola.cs`) |
| Una pantalla de configuración rica, con pestañas, listas, formularios (botón del ribbon) | WebView2 + los tokens de arriba (patrón de `ConfiguracionFormato.ctx`) |
| Un documento/reporte que termina en PDF | `htmlpdf/formatos/` — mismos tokens, más `@page`/`print-color-adjust:exact` (ver esos archivos) |
| Una app standalone (fuera de Comercial) | WPF (patrón del Punto de Venta, `puntodeventa-app/App.xaml`) |

## Antes de construir algo visual nuevo

1. Revisa si ya existe un patrón parecido (tabla de arriba) — no inventes un
   componente que ya está resuelto en otra superficie.
2. Usa los tokens de este archivo. Si necesitas un color que no está aquí, agrégalo
   aquí primero (con su porqué), no lo hardcodees suelto en un solo archivo.
3. Para pantallas grandes o nuevas, prototipa el mockup visualmente antes de escribir
   XAML/HTML real (usa la skill de diseño de Claude si estás en esa sesión) — más
   barato corregir un mockup que una pantalla ya construida.
