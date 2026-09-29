# Cómo crear un botón

> Desde la versión **2.95.0**. Un botón es lo que aparece en la barra (ribbon) de Comercial y ejecuta uno de tus scripts. Antes había que dar de alta el botón a mano
> (escribir `BrosLMV.NOMBRE`, el archivo del ícono, la sección, el módulo…). Ahora lo hace un asistente de tres pasos.

## Dos formas de abrirlo

1. **Con un script que ya tienes** (lo más común): en la Consola, clic secundario sobre el script → **Crear botón…**
   La clave del botón sale del nombre del script: para el script `Cotizador` el botón ejecuta `BrosLMV.Cotizador`.
   Si el script ya tiene botón, la opción dice **Editar botón…** y trae lo que tiene hoy.
2. **Sin script todavía**: **Más opciones → Nuevo botón…** Escribes el nombre del botón, lo configuras y, al crearlo, la Consola crea un script mínimo
   con esa clave (cuenta los documentos seleccionados) y lo abre para que escribas qué debe hacer.

## Los tres pasos

### 1 · El botón
- **Nombre:** el texto que se ve en el botón. Puede llevar espacios y acentos. La clave técnica (`BrosLMV.…`) se genera sola y se muestra debajo.
- **Descripción (opcional):** el globo que aparece cuando el usuario pone el mouse sobre el botón. Conviene explicar en una frase qué hace.
- **Ícono:** hay dos catálogos con el mismo buscador.
  - **BrosLMV:** catálogo propio (más de 2,000 íconos modernos, de la colección Lucide, licencia ISC). Busca **en español**: *documento, banco, reporte, imprimir, correo, producto…*.
    Arriba aparecen íconos **sugeridos** según el nombre que escribiste.
  - **Comercial:** los íconos que ya trae Comercial en `…\Compac\ComercialSP\Icons` (se muestra un tamaño por ícono).
  - **Explorar…:** elige un archivo `.ico` o `.png` de cualquier carpeta. Se copia a la carpeta de íconos de Comercial con el prefijo `BrosLMV_` (un `.png` se convierte a `.ico`).
    Si Windows no deja copiar (Comercial instalado en *Program Files*), abre Comercial como administrador esa vez.

### 2 · Dónde aparece
- **Pestaña:** una existente (BrosLMV, General, Lista, Reporte, Contabilidad…) o **＋ Nueva pestaña…** con el nombre que quieras (por ejemplo *Reportes*).
- **Sección:** una de las que tiene esa pestaña o **＋ Nueva sección…** (Herramientas, Impresión, Filtro…).
- **Módulos:** **En todos** (aparece en cualquier módulo) o **Solo en algunos…** y marcas los módulos (Facturas de compra, Gastos, XML recibidos…).
  Ejemplo: un botón dentro de la pestaña *Lista*, sección *Filtro*, solo en *Facturas electrónicas*.

### 3 · Quién lo ve
- **Todos los usuarios**, **Grupos** (lo ven todos los usuarios de cada grupo elegido) o **Usuarios** específicos.
  Queda habilitado en el mismo paso; ya no hay que ir usuario por usuario.
- **Avanzado → otras empresas:** crea el mismo botón en las empresas que marques. La pestaña y la sección se buscan por nombre en cada una y se crean si faltan.

A la derecha ves siempre **cómo quedará en el ribbon** (con el globo de la descripción) y un resumen de lo que se va a crear.

## Después de crear
- Si no ves el botón de inmediato, **cierra y vuelve a abrir Comercial**: el ribbon se lee al iniciar.
- **Editar botón…** cambia nombre, descripción, ícono, lugar, módulos y usuarios de un botón BrosLMV. **Quitar botón** lo saca del ribbon (el script no se borra).
- **Deshacer último cambio** restaura el botón como estaba antes de la última modificación (se guarda una copia en la tabla `zzBrosRibbonHist` de cada empresa).

## Reglas de seguridad
- El asistente **solo toca botones `BrosLMV.*`**; los botones nativos de Comercial no se modifican.
- Publicar es **idempotente**: si el botón ya existe se actualiza, no se duplica.
- Cada empresa se escribe en **una sola transacción**: o queda completo o no cambia nada.

## Para quien programa (referencia técnica)
Cómo guarda Comercial un botón (tablas de la base de cada empresa):

| Tabla | Qué guarda |
|---|---|
| `engRibbonTab` | Pestañas (`TabCaption`, `TabOrder`) |
| `engRibbonGroup` | Secciones dentro de una pestaña (`GroupCaption`) |
| `engRibbonControl` | El botón: `ControlCaption`, `ControlDescription` (el globo), `ControlExecute` (`BrosLMV.<clave>`), `IconFile` |
| `engRibbonMenu` | **Dónde aparece**: una fila por módulo × usuario. `ExtraMenuModuleID = 0` = todos los módulos; `IfUserIDIs = 0` = todos los usuarios |

- El motor está en `src\RibbonAdmin.cs` (`Contexto`, `Publicar`, `Quitar`, `DeshacerUltimo`); la pantalla, en `src\assets\crear_boton_app.html` con su ventana `src\RibbonUi.cs` (WebView2).
- Los módulos de la lista salen de `engModule`, los usuarios de `engUser` y los grupos de `engUserGroup`: no hay nada escrito a mano.
- Los íconos de BrosLMV se generan al compilar (`build\iconos\generar_iconos.js`, `Lucide` fijado a la versión 1.48.0) y se instalan en la carpeta de íconos de Comercial (`BrosLMV_<nombre>.ico`).
  Catálogo con etiquetas de búsqueda: `C:\BrosLMV\iconos\iconos.json`. Licencia de Lucide: `C:\BrosLMV\iconos\LICENCIA_Lucide.txt`.
- Diseño y decisiones: [`DISENO_CREAR_BOTON.md`](DISENO_CREAR_BOTON.md); plan general: [`PLAN_CONSOLA_V3.md`](PLAN_CONSOLA_V3.md).

## Problemas frecuentes
| Síntoma | Qué hacer |
|---|---|
| El botón no aparece | Reinicia Comercial. Revisa que estés en un módulo donde lo publicaste y que tu usuario esté entre los que lo ven. |
| El botón sale sin ícono | El ícono debe estar en `…\ComercialSP\Icons`. Reinstala BrosLMV (copia los íconos) o usa **Explorar…**. |
| «Windows no dejó copiar el ícono» | Abre Comercial como administrador una vez, o copia el `.ico` a la carpeta de íconos a mano. |
| «No se pudo abrir el asistente» | Falta el WebView2 Runtime de Microsoft (viene con Windows 11 y con Edge). |
| El botón dice «script no encontrado» | Crea o guarda el script con la misma clave que muestra el asistente (`BrosLMV.<clave>`). |
