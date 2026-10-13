# Diseño de BrosLMV Descargas

Guía del sistema de diseño de la aplicación de escritorio **BrosLMV Descargas** (`descargas-ui`, WPF). Sirve para que cualquier pantalla nueva
se vea y se comporte igual que las demás. Todo lo visual vive en `descargas-ui\Estilos\`; ninguna página debería escribir un color suelto.

## Principios

1. **Una sola ventana.** Menú lateral + página activa. Nada de ventanas modales para trabajar: los cuadros de diálogo son solo para pedir
   un dato o confirmar (nueva descarga, editar empresa, detalle de un CFDI).
2. **Lo importante primero.** El **Panel** abre mostrando el estado (servicio, e.firma, Comercial, último XML), las cifras del mes y lo que
   requiere atención. Cada elemento de «Requiere atención» lleva a la página donde se resuelve.
3. **Lenguaje del usuario.** «Descargas del SAT», «Nueva descarga», «Cobertura por año»; no «cola de solicitudes» ni «paquetes».
4. **Estado visible en forma y color.** Pastillas (Ok / Aviso / Crítica / Info), bandas de resumen, insignias en el menú.
5. **Todo cálculo es local.** Las páginas solo leen la base local; ninguna consulta al SAT sin que el usuario lo pida.

## Estructura

```
descargas-ui\
├─ Estilos\            Tokens.xaml (colores, tipografía, medidas) · Texto · Botones · Campos · Tablas · Contenedores · Navegacion
├─ Controles\          Ui.cs (propiedades adjuntas y glifos) · EncabezadoPagina · EstadoVacio
├─ Paginas\            una página por función (PaginaPanel, PaginaCfdi, PaginaDescargas, PaginaIndicadores, …)
├─ MainWindow.xaml(.cs)  menú lateral, selector de empresa, bandeja del sistema y navegación
├─ Sesion.cs           conexión, empresas y empresa activa compartidas por todas las páginas
├─ Resumen.cs          salud + indicadores + cifras del mes + cobertura (Panel e insignias del menú)
└─ *Window.xaml        solo cuadros de diálogo
```

## Tokens

| Familia | Nombres | Uso |
|---|---|---|
| Marca | `Marca950` … `Marca600`, `MarcaTexto` | solo el menú lateral |
| Acento | `Acento`, `AcentoHover`, `AcentoSuave`, `AcentoBorde` | acción principal, selección, enlaces |
| Neutros | `Lienzo`, `Superficie`, `Borde`, `Texto`, `Texto2`, `Texto3` | fondo de la ventana, tarjetas, bordes, jerarquía de texto |
| Semánticos | `Ok`, `Aviso`, `Critico`, `Info` (+ `…Fondo`, `…Borde`) | estados |

Cada color existe como `Color` y como pincel `PincelXxx`. Tipografía: **Segoe UI Variable** (con respaldo Segoe UI en Windows 10); íconos:
**Segoe MDL2 Assets** (existe en Windows 10 y 11), con los glifos nombrados en `Controles\Ui.cs` (`G.Descargar`, `G.Alerta`, …).

Escala de texto (`Estilos\Texto.xaml`): `TextoDisplay` 26 · `TextoTitulo` 18 · `TextoSubtitulo` 14.5 · `TextoCuerpo` 13 · `TextoSecundario` 12.5 ·
`TextoTenue` 12 · `TextoRotulo` 11 en versalitas · `TextoCifra` 28 con numerales tabulares · `TextoMono`.

## Piezas reutilizables

- **Botones**: `Primary` (una por pantalla), `Ghost`, `Subtle`, `Danger`, `BotonExito`. Ícono opcional: `ui:Ui.Glifo="&#xE896;"`.
- **Campos**: `TextBox` con texto de ayuda (`ui:Ui.Marca`), `ComboBox`, `DatePicker` con «dd/mm/aaaa», `CheckBox`, pestañas subrayadas (`Pestana`).
- **Tablas**: `DataGrid` implícito; celdas con relleno de 12 px; selección azul suave con texto oscuro; `CeldaTexto`, `CeldaNumero`,
  `CeldaAtenuada`, `CeldaAtenuadaMono`, `CeldaError`, `CeldaCasilla`.
- **Contenedores**: `Tarjeta`, `TarjetaPlana`; pastillas `PastillaOk/Aviso/Critica/Info/Neutra`; bandas de ancho completo `BandaOk/…`.
- **Menú**: `NavItem` (RadioButton con ícono e insignia `ui:Ui.Insignia` / `ui:Ui.NivelInsignia`), `NavSeccion`.
- **`EncabezadoPagina`**: título, descripción y acciones de la página. **`EstadoVacio`**: mensaje centrado cuando una tabla no tiene filas.

## Cómo agregar una página

1. `Paginas\PaginaXxx.xaml(.cs)` como `UserControl`; constructor `(Sesion sesion)`; usar `_s.Empresa` (la empresa activa) y `_s.Conn`.
2. Empezar con `ui:EncabezadoPagina` y márgenes `32,26,32,24`. Un solo botón `Primary`. Las tablas dentro de una `Tarjeta` con `Padding="0"`.
3. Si la tabla puede quedar vacía, poner un `ui:EstadoVacio` con un mensaje que diga qué hacer.
4. Registrar la clave en `MainWindow.xaml` (un `RadioButton` con `Tag` y `ui:Ui.Glifo`) y en `MainWindow.CrearPagina`.
5. Los `DataGrid` enlazan **propiedades**, no campos (con campos las columnas salen vacías: así estuvo la Conciliación hasta 2.5.0).
6. Dentro de un `EncabezadoPagina` se puede usar `x:Name` (el control no tiene XAML propio a propósito).

## Reglas

- Sin hexadecimales sueltos: usar `{StaticResource PincelXxx}`.
- Nada de emoji como ícono; usar glifos de `Segoe MDL2 Assets`.
- Diálogos: `SizeToContent="Height"` si no llevan lista; `Owner` = la ventana principal; fondo `PincelLienzo`.
- Después de un cambio visual, abrir la aplicación y revisar cada página afectada (Panel, CFDI, Descargas, Indicadores…).
