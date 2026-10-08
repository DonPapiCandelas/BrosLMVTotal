// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Consola.cs
// Consola de scripts BrosLMV: editor de codigo (Scintilla) + biblioteca de scripts
// + inspector de contexto (ctx) + ayuda integrada + ejecucion segura.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScintillaNET;

namespace BrosLMV
{
    public class BrosConsola : Form
    {
        private readonly ScriptContext _ctx;

        private class ScriptTab
        {
            public string AppKey = "";
            public Scintilla Editor;
            public Panel Chip;
            public Label LblName;
            public bool IsModified;
            public string Titulo => (string.IsNullOrEmpty(AppKey) ? "Nuevo script" : AppKey) + (IsModified ? " *" : "");
        }
        private List<ScriptTab> _tabs = new List<ScriptTab>();
        private ScriptTab _activeTab;
        private Panel _pnlEditorHost;
        private FlowLayoutPanel _tabStrip;

        private Scintilla _editor => _activeTab?.Editor;
        private string _appKey
        {
            get => _activeTab?.AppKey ?? "";
            set { if (_activeTab != null) { _activeTab.AppKey = value; _activeTab.LblName.Text = _activeTab.Titulo; } }
        }

        private TreeView    _tree;
        private RichTextBox _outSalida, _outErrores, _outMensajes;
        private TabControl  _tabsOut;
        private ListView    _lstMetodosCSharp, _lstMetodosPython, _lstMetodosSql, _lstSeleccion;
        private Label        _lblCtx;
        private CheckBox     _chkSoloLectura;
        private ToolStripStatusLabel _status, _statusTiempo, _statusScript, _statusLang, _statusPos, _statusVer;
        private readonly Dictionary<string, IconButton> _botonesLenguaje = new Dictionary<string, IconButton>();
        private IconButton _btnContexto;
        private FlowLayoutPanel _herramientasEditor;
        private bool _contextoVisible = true;
        private Action<int> _activarReferencia;
        private TextBox _txtSdk;
        private CheckBox _chkWrap;
        private static readonly Color ConsolaChrome = Color.FromArgb(220, 224, 229);
        private static readonly Color ConsolaSuperficie = Color.FromArgb(235, 237, 240);
        private static readonly Color ConsolaEditor = Color.FromArgb(232, 234, 237);
        private static readonly Color ConsolaCodigo = Color.FromArgb(38, 44, 53);
        private static readonly Color ConsolaMarca = Color.FromArgb(187, 195, 205);
        // private string _appKey

        // Versión del addon (de AssemblyVersion). Se lee de memoria una vez: costo cero.
        // v2.18.0 — 4 anclas + campos universales + partida como nativa (ver CHANGELOG).
        internal static readonly string Version =
            "v" + typeof(BrosConsola).Assembly.GetName().Version.ToString(3);
        private TextBox      _txtBuscar;

        // --- Controles de la refactorización visual ---
        private SplitContainer _splitLeft, _splitMain, _splitEditor;
        private Label        _lblEstadoDoc, _lblErrCount;
        private ToolTip      _tips;
        private ComboBox     _cboFontSize;
        private int          _fontSize = 12;
        private ListView     _lstCtx;   // Contexto actual en forma de lista (Campo / Valor)

        // --- Buscar en el editor ---
        private Panel        _findBar;
        private TextBox      _txtFind;
        private CheckBox _findMayusculas, _findPalabra;
        private Label        _lblFindCount;
        private readonly List<int> _findHits = new List<int>();
        private int          _findIdx = -1;
        private const int    IND_FIND = 8;   // indicador de Scintilla para resaltar coincidencias

        // --- Pantalla completa del editor ---
        private bool         _zen;
        private IconButton   _btnZen;

        // ---- Metadata de ctx (ayuda + autocompletado) ----
        private class MetodoCtx
        {
            public string Nombre, Firma, Desc, Ejemplo, Cat, Id;
            public MetodoCtx(string n, string f, string d, string e, string cat = "") { Nombre = n; Firma = f; Desc = d; Ejemplo = e; Cat = cat; }
        }

        // T3.1 fase 1: panel de tokens fijos, insertable en la pestaña "Datos" junto a los
        // campos dinámicos de la selección (ya existían, ver _lstSeleccion). Cada token trae
        // el snippet correcto por lenguaje -- SQL usa el literal "{pID}" (ResolverTokens lo
        // resuelve en EjecutarSql); C#/Python NO tienen resolución de tokens de texto, así que
        // insertan la llamada nativa equivalente (mismo criterio que ya usaba _lstSeleccion
        // para {DATOS:campo} vs fila["campo"] vs ctx.fila["campo"]).
        private class TokenFijo
        {
            public string Token, Desc, Sql, CSharp, Python;
            public TokenFijo(string t, string d, string sql, string cs, string py)
            { Token = t; Desc = d; Sql = sql; CSharp = cs; Python = py; }
        }
        private static readonly TokenFijo[] TOKENS_FIJOS = new[]
        {
            new TokenFijo("{pID}", "primer ID seleccionado", "{pID}", "ctx.GetSelectedIds()[0]", "ctx.get_selected_ids()[0]"),
            new TokenFijo("{pIDs}", "todos los IDs seleccionados", "{pIDs}", "ctx.JoinIds(ctx.GetSelectedIds())", "ctx.get_selected_ids()"),
            new TokenFijo("{pUserID}", "usuario activo", "{pUserID}", "ctx.erp.UserId", "ctx.user_id"),
            new TokenFijo("{pModulo}", "módulo activo", "{pModulo}", "ctx.ModuloActivo()", "ctx.module_id"),
            new TokenFijo("{pEmpresa}", "empresa (BD) activa", "{pEmpresa}", "ctx.Empresa()", "ctx.empresa"),
        };
        // Referencias (C#, Python, SQL): salen del catálogo del SDK (src\assets\sdk_catalogo.json, incrustado), la MISMA fuente del manual HTML.
        // Para agregar o cambiar una función edita el JSON (no este archivo) y regenera el manual con build\sdk\generar_referencia_sdk.py.
        private static MetodoCtx[] DesdeCatalogo(string lang)
        {
            return SdkCatalogo.Todas().Where(e => e.Lang == lang && !e.Interno)
                .Select(e => new MetodoCtx(e.Nombre, e.Firma, e.Resumen, e.Ejemplo, e.Cat) { Id = e.Id }).ToArray();
        }
        private static readonly MetodoCtx[] METODOS = DesdeCatalogo("cs");
        private static readonly MetodoCtx[] METODOS_PYTHON = DesdeCatalogo("py");
        private static readonly MetodoCtx[] METODOS_SQL = DesdeCatalogo("sql");

        // ---- Plantillas de fábrica ----
        // Una plantilla de fábrica es CUALQUIER script de la carpeta raíz de scripts\ (C:\BrosLMV\scripts, junto a la DLL o en el paquete) cuya
        // cabecera declare «// Plantilla: Nombre visible» (en Python: «# Plantilla: …»; en SQL: «-- Plantilla: …»). Opcionales en la misma cabecera:
        //   «Categoria: X»      carpeta donde aparece en el árbol (por defecto «Plantillas»)
        //   «Documentacion: X.html»   documentación completa (clic secundario → «Ver documentación»); se incrusta desde instalador\docs\plantillas
        // Así agregar una plantilla NO requiere tocar este archivo: basta dejar el script con su cabecera. Los instaladores refrescan en cada
        // instalación todas las que traen ese marcador; las demás herramientas de la carpeta (GESTOR_RIBBON, DIAGNOSTICO…) no se tratan como plantillas.
        private class PlantillaDef
        {
            public string Categoria, Nombre, Codigo, Documentacion;
            public string AppKey => AppKeySugerido(Codigo);
            public PlantillaDef(string cat, string n, string c, string doc = null) { Categoria = cat; Nombre = n; Codigo = c; Documentacion = doc; }
        }

        private static string CabeceraPlantilla(string codigo, string campo)
        {
            if (string.IsNullOrEmpty(codigo)) return null;
            string cab = codigo.Length > 3000 ? codigo.Substring(0, 3000) : codigo;
            var m = Regex.Match(cab, @"^\s*(?://|#|--)\s*" + campo + @"\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static PlantillaDef[] CargarPlantillasDeFabrica()
        {
            var lista = new List<PlantillaDef>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[]
            {
                Rutas.Scripts,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scripts"),
            })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".ctx" && ext != ".csx" && ext != ".py" && ext != ".sql") continue;
                        string nombreArchivo = Path.GetFileName(f);
                        if (vistos.Contains(nombreArchivo)) continue;
                        string codigo = File.ReadAllText(f);
                        string nombre = CabeceraPlantilla(codigo, "Plantilla");
                        if (string.IsNullOrEmpty(nombre)) continue;            // sin marcador: es una herramienta, no una plantilla
                        vistos.Add(nombreArchivo);
                        lista.Add(new PlantillaDef(CabeceraPlantilla(codigo, "Categor[ií]a") ?? "Plantillas", nombre, codigo, CabeceraPlantilla(codigo, "Documentaci[oó]n")));
                    }
                }
                catch { }
            }
            return lista.OrderBy(p => p.Categoria, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Nombre, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static readonly PlantillaDef[] PLANTILLAS_DEF = CargarPlantillasDeFabrica();

        // «AppKey recomendado: X» en la cabecera de una plantilla = nombre con el que se guarda como botón (BrosLMV.X).
        private static readonly Regex RX_APPKEY_SUG = new Regex(@"AppKey\s+recomendado\s*:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
        private static string AppKeySugerido(string codigo)
        {
            if (string.IsNullOrEmpty(codigo)) return null;
            var m = RX_APPKEY_SUG.Match(codigo.Length > 2000 ? codigo.Substring(0, 2000) : codigo);
            return m.Success ? m.Groups[1].Value : null;
        }

        // Convierte lo que escriba el usuario («Crear docs XML», con espacios/acentos) en un AppKey válido: CREAR_DOCS_XML.
        private static string NormalizarAppKey(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            string d = texto.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in d)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            }
            return Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        }

        // Busca la documentación HTML de una plantilla (instalada en C:\BrosLMV\docs\plantillas o junto a la DLL) y la muestra en una ventana.
        private void MostrarDocumentacionPlantilla(string nombre, string archivoHtml)
        {
            string html = null;
            try
            {
                using (var rs = Recurso("doc_" + archivoHtml))
                    if (rs != null) using (var sr = new StreamReader(rs, Encoding.UTF8)) html = sr.ReadToEnd();
            }
            catch { }
            if (html == null) foreach (var ruta in new[]
            {
                Path.Combine(Rutas.Base, "docs", "plantillas", archivoHtml),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "plantillas", archivoHtml),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "docs", "plantillas", archivoHtml),
            })
            {
                try
                {
                    string full = Path.GetFullPath(ruta);
                    if (File.Exists(full)) { html = File.ReadAllText(full, Encoding.UTF8); break; }
                }
                catch { }
            }
            if (html == null) { MessageBox.Show("No se encontró la documentación de esta plantilla (" + archivoHtml + ").", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { _ctx.ShowHtml(html, "Documentación — " + nombre, 1100, 780, false); }
            catch (Exception ex) { MessageBox.Show("No se pudo abrir la documentación: " + ex.Message, "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private static string CargarPlantillaArchivo(string nombreArchivo, string fallback)
        {
            foreach (var ruta in new[]
            {
                Path.Combine(Rutas.Scripts, nombreArchivo),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", nombreArchivo),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scripts", nombreArchivo),
            })
            {
                try
                {
                    string full = Path.GetFullPath(ruta);
                    // Encoding.UTF8 explícito: sin BOM, File.ReadAllText(path) puede caer al
                    // codepage ANSI del sistema en .NET Framework, convirtiendo acentos/emoji a
                    // "?" -- eso es justo lo que le pasó al guardar una plantilla como AppKey
                    // (zzBrosScript quedó con "Informaci?n" en vez de "Información").
                    if (File.Exists(full)) return File.ReadAllText(full, System.Text.Encoding.UTF8);
                }
                catch { }
            }
            return fallback;
        }

        // Lanzada cuando la Consola tiene contraseña activa (zzBrosConsolaPass) y el usuario
        // cancela o falla el dialogo -- ClsMain.cs la atrapa y NO muestra la ventana (nunca
        // llega a existir un Form con controles, no hace falta cerrarlo despues).
        public sealed class AccesoDenegadoException : Exception { }

        public BrosConsola(int userId, object xEngineLib)
        {
            _ctx = new ScriptContext(userId, xEngineLib);

            // Candado de la Consola (T-nueva): a nivel EMPRESA, configurado desde el
            // instalador. Se verifica ANTES de construir cualquier control -- si falla, se
            // lanza y ClsMain.cs nunca llega a mostrar la ventana.
            if (_ctx.BrosConsolaPasswordRequerida())
            {
                bool ok = ConsolaPasswordPromptForm.PedirYVerificar(_ctx.BrosVerificarConsolaPassword);
                if (!ok) throw new AccesoDenegadoException();
            }

            Rutas.AsegurarCarpetas();
            try { Datos.Inicializar(); } catch { }
            BuildUI();
            // T2.2: si zzBrosPref fuerza SoloLectura para este usuario, el checkbox se deja
            // marcado y BLOQUEADO -- no es solo el valor por default, el usuario no puede
            // desactivarlo desde aqui (ExecuteScript() lee _chkSoloLectura.Checked en cada
            // corrida, asi que con Enabled=false queda forzado de verdad, no solo sugerido).
            if (_ctx.BrosSoloLecturaForzada())
            {
                _chkSoloLectura.Checked = true;
                _chkSoloLectura.Enabled = false;
                _tips.SetToolTip(_chkSoloLectura, "Solo lectura forzado para tu usuario (preferencia en zzBrosPref) -- no se puede desactivar aquí.");
            }
            CargarArbol();
            CargarMetodos();
            NuevoScript();

            // Al mostrarse: dejar que la ventana TERMINE de pintarse y luego cargar el
            // contexto (BeginInvoke lo encola tras el pintado, evita verla "trabada").
            Shown += (s, e) =>
            {
                _lblCtx.Text = "Cargando contexto…";
                BeginInvoke((Action)(() =>
                {
                    ActualizarContexto();
                    try { _empresaInicial = _ctx.Empresa(); } catch { _empresaInicial = null; }
                    _ctxCargado = true;
                }));
                // Precalentar Roslyn con un retraso, para no competir con la apertura.
                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(600);
                    try { ScriptRunner.Precalentar(); } catch { }
                });
            };

            // Modeless: al volver a la ventana (tras minimizar/trabajar en Comercial),
            // refrescar el "contexto actual" porque pudo cambiar de módulo/selección.
            Activated += (s, e) =>
            {
                if (!_ctxCargado) return;
                try { ActualizarContexto(); } catch { }
                AvisarSiCambioEmpresa();
            };
        }

        // Evita refrescar en el primer Activated (que ocurre antes del Shown inicial).
        private bool   _ctxCargado;
        // T2.4: BrosAsegurarTablas() agrega columnas nuevas de zzBrosScript (Categoria,
        // HashSHA256, AprobadoPor/El) si faltan -- necesario para empresas viejas migradas o
        // restauradas desde un .bak que nunca pasaron por el flujo normal de guardado (antes,
        // esa reparación solo se disparaba al Guardar/Categorizar/etc., pero esas acciones
        // requieren VER un script primero -- y si BrosListar() truena por columna faltante, el
        // árbol sale vacío sin aviso y el usuario nunca llega a disparar la reparación: caso
        // real, ver COCTEL_DE_IDEAS). Se corre UNA vez por sesión de Consola (no en cada
        // CargarArbol(), que se llama por cada tecla al buscar) -- por eso el flag.
        private bool   _tablasAseguradas;
        // Empresa activa cuando se abrió la consola. El MOTOR (XEngineLib) se capturó en ese
        // momento; si cambias de empresa en Comercial con la consola abierta, podrías ejecutar
        // contra la empresa equivocada. Por eso se vigila y se avisa/confirma.
        private string _empresaInicial;
        private bool   _avisoEmpresaMostrado;

        // Evita ejecuciones Python superpuestas desde la consola: cada clic de "Ejecutar"
        // lanzaba un BrosLMV.Host.exe nuevo aunque el anterior siguiera corriendo, y todos
        // competían por el mismo hilo de Comercial (UiPump) — eso generaba el diálogo nativo
        // de Windows "the other application is busy" al acumularse ejecuciones encimadas.
        private bool _ejecutandoPython;

        // ¿La empresa activa ahora difiere de la que había al abrir la consola?
        private bool EmpresaCambio()
        {
            if (string.IsNullOrEmpty(_empresaInicial)) return false;
            string actual;
            try { actual = _ctx.Empresa(); } catch { return false; }
            return !string.IsNullOrEmpty(actual)
                && !string.Equals(actual, _empresaInicial, StringComparison.OrdinalIgnoreCase);
        }

        // Avisa UNA vez al reactivar si la empresa cambió (no fastidiar en cada foco).
        private void AvisarSiCambioEmpresa()
        {
            if (EmpresaCambio())
            {
                _lblCtx.ForeColor = AppTheme.Error;
                if (!_avisoEmpresaMostrado)
                {
                    _avisoEmpresaMostrado = true;
                    MessageBox.Show(
                        "La EMPRESA activa cambió desde que abriste la consola.\n\n" +
                        "Abierta en: " + _empresaInicial + "\nActiva ahora: " + SafeEmpresa() + "\n\n" +
                        "La consola sigue ligada al motor de la empresa original. Para evitar " +
                        "ejecutar contra la empresa equivocada, CIÉRRALA y vuelve a abrirla.",
                        "BrosLMV — cambió la empresa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                _lblCtx.ForeColor = AppTheme.TextMuted;
                _avisoEmpresaMostrado = false; // si vuelven a la empresa original, rearmar el aviso
            }
        }

        private string SafeEmpresa()
        {
            try { return _ctx.Empresa(); } catch { return "(?)"; }
        }

        // Diálogo "Acerca de": versión + fecha de compilación + botón a las notas (HTML).
        private void AcercaDe()
        {
            try { using (var dlg = new AcercaForm()) dlg.ShowDialog(this); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Acerca de BrosLMV"); }
        }

        // Extrae las notas de versión (HTML embebido) a %TEMP% y las abre en el navegador
        // del sistema. NO usamos un control web en proceso: abrir fuera = cero peso/latencia.
        internal static void AbrirNotasVersion()
        {
            try
            {
                string dst = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BrosLMV_notas_version.html");
                using (var src = Recurso("notas_version.html"))
                {
                    if (src == null) { MessageBox.Show("No se encontraron las notas de versión embebidas."); return; }
                    using (var fs = System.IO.File.Create(dst)) src.CopyTo(fs);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dst) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("No se pudieron abrir las notas: " + ex.Message); }
        }

        // Fecha de compilación = última escritura de la DLL (un solo stat, instantáneo).
        internal static string FechaCompilacion()
        {
            try { return System.IO.File.GetLastWriteTime(typeof(BrosConsola).Assembly.Location).ToString("yyyy-MM-dd HH:mm"); }
            catch { return "—"; }
        }

        // =====================================================
        //   UI
        // =====================================================
        private void BuildUI()
        {
            Text = "BrosLMV — Consola de scripts";
            Size = new Size(1280, 840);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 660);
            Font = AppTheme.FontMain;
            BackColor = AppTheme.BgSurface;
            try { var si = Recurso("BrosLMV.ico"); if (si != null) using (si) Icon = new System.Drawing.Icon(si); } catch { }

            _tips = new ToolTip { InitialDelay = 350, ReshowDelay = 120, AutoPopDelay = 9000, ShowAlways = true };

            // =========================================================
            //   Cabecera: logo + wordmark + subtítulo + estado del doc
            //   (TableLayoutPanel => alineación robusta a cualquier DPI)
            // =========================================================
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = ConsolaChrome };
            pnlHeader.Paint += (s, e) => BordeInferior(e.Graphics, pnlHeader);

            var tlHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(18, 0, 18, 0) };
            tlHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) tlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlHeader.ColumnStyles.Insert(4, new ColumnStyle(SizeType.Percent, 100)); // columna elástica antes del estado

            var picLogo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Width = 34, Height = 34, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 10, 0), BackColor = Color.Transparent };
            try { var sl = Recurso("logo_app.png") ?? Recurso("logo_color.png") ?? Recurso("logo.png"); if (sl != null) using (sl) picLogo.Image = Image.FromStream(sl); } catch { }
            var lblBrand = new Label { Text = "BrosLMV", Font = AppTheme.FontHeader, ForeColor = AppTheme.TextMain, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
            var sepBrand = new Panel { Width = 1, Height = 26, Anchor = AnchorStyles.None, Margin = new Padding(14, 0, 14, 0), BackColor = AppTheme.Border };
            var lblSub = new Label { Text = "Consola de scripts  ·  " + Version, Font = new Font(AppTheme.FontMain.FontFamily, 10.5f), ForeColor = AppTheme.TextMuted, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent };
            _lblEstadoDoc = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Font = AppTheme.FontSmall, ForeColor = AppTheme.Warning, Text = "● Sin guardar", BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 0) };

            tlHeader.Controls.Add(picLogo, 0, 0);
            tlHeader.Controls.Add(lblBrand, 1, 0);
            tlHeader.Controls.Add(sepBrand, 2, 0);
            tlHeader.Controls.Add(lblSub, 3, 0);
            tlHeader.Controls.Add(_lblEstadoDoc, 5, 0);
            pnlHeader.Controls.Add(tlHeader);
            Controls.Add(pnlHeader);

            // =========================================================
            //   Barra de acciones (grupos: Ejecución / Archivo / Tools)
            //   FlowLayoutPanel con botones AutoSize => nunca se enciman.
            // =========================================================
            // WrapContents = true: si la ventana es angosta, la barra reacomoda los botones
            // en una segunda fila en lugar de recortarlos. AutoSize ajusta la altura.
            var pnlToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 52), BackColor = AppTheme.BgSurface, Padding = new Padding(12, 9, 12, 9), WrapContents = true };
            pnlToolbar.Paint += (s, e) => BordeInferior(e.Graphics, pnlToolbar);

            Action<string, string, string, EventHandler, BtnKind, Color> AddTB = (glyph, text, tip, onClick, kind, accent) =>
            {
                var btn = new IconButton { Glyph = glyph, Text = text, AccessibleName = string.IsNullOrEmpty(text) ? tip : text, Kind = kind, Accent = accent, PadX = 8, MinH = 34, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3, 1, 3, 1) };
                btn.Click += onClick;
                if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(btn, tip);
                pnlToolbar.Controls.Add(btn);
            };
            Action AddSep = () =>
                pnlToolbar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = AppTheme.Border, Margin = new Padding(7, 6, 7, 6) });

            AddTB(Glyph.Play,    "Ejecutar",            "Ejecutar el script completo (F5)",        (s, e) => Ejecutar(false), BtnKind.Primary, AppTheme.Primary);
            AddTB(Glyph.PlaySel, "",                   "Ejecutar solo el texto seleccionado",     (s, e) => Ejecutar(true),  BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Check,   "Verificar",           "Compilar/verificar sin ejecutar",         (s, e) => Verificar(),     BtnKind.Toolbar, Color.Empty);
            AddSep();
            AddTB(Glyph.Open,    "Abrir",               "Importar script desde archivo",           (s, e) => Abrir(),         BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.Save,    "Guardar",             "Guardar en la empresa activa",            (s, e) => Guardar(false),  BtnKind.Toolbar, Color.Empty);
            AddTB(Glyph.SaveAs,  "Guardar como",        "Guardar con otro nombre (AppKey)",        (s, e) => Guardar(true),   BtnKind.Toolbar, Color.Empty);
            
            var btnMore = new IconButton { Glyph = Glyph.Down, Text = "Más opciones", Kind = BtnKind.Toolbar, Accent = Color.Empty, PadX = 8, MinH = 34, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3, 1, 3, 1) };
            var ctxMore = new ContextMenuStrip { Font = AppTheme.FontMain };
            ctxMore.Items.Add(new ToolStripMenuItem("Nueva acción", null, (s, e) => { using (var f = new NuevaAccionForm(_ctx)) f.ShowDialog(this); }));
            ctxMore.Items.Add(new ToolStripMenuItem("Nuevo script", null, (s, e) => NuevoScript()));
            ctxMore.Items.Add(new ToolStripMenuItem("Nuevo botón…", null, (s, e) => CrearBoton("")));
            ctxMore.Items.Add(new ToolStripMenuItem("Botones del ribbon…", null, (s, e) => CrearBoton("", true)));
            ctxMore.Items.Add(new ToolStripSeparator());
            ctxMore.Items.Add(new ToolStripMenuItem("Duplicar", null, (s, e) => Duplicar()));
            ctxMore.Items.Add(new ToolStripMenuItem("Aprobar", null, (s, e) => Aprobar()));
            ctxMore.Items.Add(new ToolStripSeparator());
            ctxMore.Items.Add(new ToolStripMenuItem("Historial", null, (s, e) => VerHistorial()));
            ctxMore.Items.Add(new ToolStripMenuItem("Importar paquete…", null, (s, e) => ImportarPaquete()));
            ctxMore.Items.Add(new ToolStripSeparator());
            ctxMore.Items.Add(new ToolStripMenuItem("Manual del SDK…", null, (s, e) => MostrarManualSdk()));
            ctxMore.Items.Add(new ToolStripMenuItem("Respaldar todos los scripts…", null, (s, e) => RespaldarTodos()));
            ctxMore.Items.Add(new ToolStripMenuItem("Reparar biblioteca de scripts", null, (s, e) => RepararBibliotecaScripts()));
            ctxMore.Items.Add(new ToolStripMenuItem("Acerca de", null, (s, e) => AcercaDe()));
            btnMore.Click += (s, e) => ctxMore.Show(btnMore, new Point(0, btnMore.Height));
            pnlToolbar.Controls.Add(btnMore);

            _btnContexto = new IconButton { Glyph = Glyph.Info, Text = "Contexto y SDK", Kind = BtnKind.Outline, PadX = 8, MinH = 34, AutoSize = true, Margin = new Padding(3, 1, 3, 1) };
            _tips.SetToolTip(_btnContexto, "Mostrar u ocultar el contexto, las referencias y los tokens");
            _btnContexto.Click += (s, e) => ToggleContexto();
            pnlToolbar.Controls.Add(_btnContexto);
            
            AddSep();
            
            _chkSoloLectura = new CheckBox { Text = "Modo solo lectura", AutoSize = true, BackColor = Color.Transparent, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain, Margin = new Padding(8, 9, 0, 0), Cursor = Cursors.Hand };
            _tips.SetToolTip(_chkSoloLectura, "Bloquea operaciones de escritura (UPDATE/DELETE/INSERT)");
            pnlToolbar.Controls.Add(_chkSoloLectura);
            
            var pnlEdTools = _herramientasEditor = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = ConsolaChrome, Padding = new Padding(12, 5, 12, 5), MinimumSize = new Size(0, 44) };
            _cboFontSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, Font = AppTheme.FontSmall, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 4, 4, 0) };
            _cboFontSize.AccessibleName = "Tamano del texto del editor";
            _tips.SetToolTip(_cboFontSize, "Tamano del texto del editor, en puntos");
            _cboFontSize.Items.AddRange(new object[] { "11 pt", "12 pt", "13 pt", "14 pt", "16 pt", "18 pt" });
            _cboFontSize.SelectedIndex = 1;
            _cboFontSize.SelectedIndexChanged += (s, e) =>
            {
                var t = (_cboFontSize.SelectedItem as string ?? "11").Split(' ')[0];
                if (int.TryParse(t, out int pt)) AplicarTamanoFuente(pt);
            };
            var chkWrap = _chkWrap = new CheckBox { Text = "Ajuste de línea", AutoSize = true, Appearance = Appearance.Normal, ForeColor = AppTheme.TextMain, BackColor = Color.Transparent, Font = AppTheme.FontMain, Margin = new Padding(8, 6, 4, 0), Cursor = Cursors.Hand };
            chkWrap.CheckedChanged += (s, e) => { try { if (_activeTab?.Editor != null) _activeTab.Editor.WrapMode = chkWrap.Checked ? WrapMode.Word : WrapMode.None; } catch { } };
            var btnBuscarEd = new IconButton { Glyph = Glyph.Search, Text = "Buscar", Kind = BtnKind.Toolbar, MinH = 34, AutoSize = true, Radius = 4, Margin = new Padding(2, 1, 0, 1) };
            btnBuscarEd.Click += (s, e) => MostrarBuscar();
            _btnZen = new IconButton { Glyph = Glyph.Full, Text = "Zen", Kind = BtnKind.Toolbar, MinH = 34, AutoSize = true, Radius = 4, Margin = new Padding(2, 1, 0, 1) };
            _btnZen.Click += (s, e) => ToggleZen();

            pnlEdTools.Controls.Add(_cboFontSize);
            pnlEdTools.Controls.Add(chkWrap);
            pnlEdTools.Controls.Add(btnBuscarEd);
            pnlEdTools.Controls.Add(_btnZen);



            Controls.Add(pnlToolbar);
            pnlToolbar.BringToFront();
            pnlHeader.BringToFront();

            // =========================================================
            //   Barra de estado
            // =========================================================
            var ss = new StatusStrip { BackColor = ConsolaChrome, ForeColor = AppTheme.TextMuted, SizingGrip = false, Padding = new Padding(8, 0, 12, 0), Font = AppTheme.FontMain };
            ss.Renderer = new BordeSuperiorRenderer();
            _status        = new ToolStripStatusLabel("Listo") { TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextMain };
            var sep1       = new ToolStripStatusLabel { Spring = true };
            _statusScript  = new ToolStripStatusLabel("—") { ForeColor = AppTheme.TextMuted };
            _statusLang    = new ToolStripStatusLabel("C#") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusPos     = new ToolStripStatusLabel("Lín 1, Col 1") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusTiempo  = new ToolStripStatusLabel("") { ForeColor = AppTheme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            _statusVer     = new ToolStripStatusLabel(Version) { ForeColor = AppTheme.Primary, IsLink = true, LinkBehavior = LinkBehavior.HoverUnderline, BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched, ToolTipText = "Acerca de BrosLMV / notas de versión" };
            _statusVer.Click += (s, e) => AcercaDe();
            _statusLang.Click += (s, e) => MostrarMenuLenguaje(this, PointToClient(Cursor.Position));
            ss.Items.AddRange(new ToolStripItem[] { _status, sep1, _statusScript, _statusLang, _statusPos, _statusTiempo, _statusVer });
            Controls.Add(ss);

            // =========================================================
            //   Layout principal (3 zonas redimensionables)
            // =========================================================
            var split1 = _splitLeft = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230, FixedPanel = FixedPanel.Panel1, BorderStyle = BorderStyle.None, SplitterWidth = 6, BackColor = AppTheme.BgMain };
            var split2 = _splitMain = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgMain };
            var split3 = _splitEditor = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgMain };

            // ----- Izquierda: biblioteca de scripts -----
            split1.Panel1.Controls.Add(ConstruirPanelIzquierdo());

            // ----- Centro: editor + salida -----
            split3.Panel1.Controls.Add(ConstruirEditor());
            split3.Panel2.Controls.Add(ConstruirSalida());
            split2.Panel1.Controls.Add(split3);

            // ----- Derecha: inspector de contexto -----
            split2.Panel2.Controls.Add(ConstruirPanelDerecho());
            split2.Panel2Collapsed = !_contextoVisible;

            split1.Panel2.Controls.Add(split2);
            Controls.Add(split1);
            // WinForms acopla desde el ultimo control: estado, cabecera, acciones y cuerpo.
            Controls.SetChildIndex(split1, 0);
            Controls.SetChildIndex(pnlToolbar, 1);
            Controls.SetChildIndex(pnlHeader, 2);
            Controls.SetChildIndex(ss, 3);
            AplicarPaletaConsola(this);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (AtajoArchivo(e)) return;
                if (e.KeyCode == Keys.F5) { e.Handled = true; Ejecutar(false); }
                else if (e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.B)) { e.Handled = e.SuppressKeyPress = true; MostrarBuscar(); }
                else if (e.KeyCode == Keys.F3) { e.Handled = true; if (_findBar != null && _findBar.Visible) BuscarMover(e.Shift ? -1 : 1); else MostrarBuscar(); }
                else if (e.KeyCode == Keys.F11) { e.Handled = true; ToggleZen(); }
                else if (e.KeyCode == Keys.Escape && _findBar != null && _findBar.Visible) { e.Handled = true; OcultarBuscar(); }
            };

            // El título del documento y el estado se reflejan en la pestaña/cabecera.
            TextChanged += (s, e) => RefrescarEstadoDoc();
            RefrescarEstadoDoc();

            // Ajustar las divisiones cuando los contenedores ya tienen su tamaño real
            // (hacerlo en el constructor falla porque aún miden 0; FixedPanel.Panel2 lo
            // congelaría colapsado). Se ejecuta una sola vez.
            bool layoutDone = false;
            Shown += (s, e) =>
            {
                if (layoutDone) return; layoutDone = true;
                try
                {
                    // Panel izquierdo (FixedPanel) no autoescala con DPI: fijarlo aquí.
                    int izq = Math.Max(LogicalToDeviceUnits(238), (int)(_splitLeft.Width * 0.18));
                    if (_splitLeft.Width > 500) _splitLeft.SplitterDistance = Math.Min(izq, 360);
                    _splitLeft.Panel2MinSize = Math.Min(LogicalToDeviceUnits(640), _splitLeft.Width - _splitLeft.SplitterDistance - _splitLeft.SplitterWidth);
                }
                catch { }
                try
                {
                    // Ancho del panel derecho robusto a DPI: ~330 px lógicos, pero nunca menos
                    // del 25% ni más de la mitad del área disponible.
                    int derecho = Math.Max(LogicalToDeviceUnits(330), (int)(_splitMain.Width * 0.25));
                    derecho = Math.Min(derecho, _splitMain.Width / 2);
                    if (_splitMain.Width > 600)
                    {
                        _splitMain.SplitterDistance = _splitMain.Width - derecho;
                        _splitMain.Panel2MinSize = LogicalToDeviceUnits(300);
                    }
                }
                catch { }
                try { _splitEditor.SplitterDistance = (int)(_splitEditor.Height * 0.72); } catch { }
            };
        }

        // Borde inferior/superior de 1px para separar bandas (sustituye sombras pesadas).
        // internal (no private): NuevaAccionForm tambien la usa.
        internal static void BordeInferior(Graphics g, Control c)
        { using (var p = new Pen(AppTheme.Border)) g.DrawLine(p, 0, c.Height - 1, c.Width, c.Height - 1); }

        // La paleta solo se aplica a esta ventana, no al tema compartido del addon.
        private static void AplicarPaletaConsola(Control control)
        {
            if (control.BackColor == AppTheme.BgSurface) control.BackColor = ConsolaSuperficie;
            else if (control.BackColor == AppTheme.BgMain) control.BackColor = ConsolaChrome;
            if (control is IconButton boton) boton.Superficie = ConsolaSuperficie;
            foreach (Control hijo in control.Controls) AplicarPaletaConsola(hijo);
        }

        // Refleja el nombre del script en la pestaña y el estado "guardado/sin guardar".
        private void RefrescarEstadoDoc()
        {
            bool guardado = _activeTab != null && !string.IsNullOrEmpty(_appKey) && !_activeTab.IsModified;
            // lblTabName deleted
            if (_lblEstadoDoc != null)
            {
                _lblEstadoDoc.Text = guardado ? "● Guardado" : "● Sin guardar";
                _lblEstadoDoc.ForeColor = guardado ? AppTheme.Success : AppTheme.Warning;
            }
            if (_statusScript != null) _statusScript.Text = string.IsNullOrEmpty(_appKey) ? "(sin guardar)" : _appKey;
            if (_activeTab != null) _activeTab.LblName.Text = _activeTab.Titulo;
        }

        // =========================================================
        //   Panel izquierdo — Biblioteca de scripts
        // =========================================================
        private Panel ConstruirLenguajes()
        {
            var host = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 0, 0, 18) };
            var titulo = new Label { Text = "Lenguaje del script", Dock = DockStyle.Top, Height = 26, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            var opciones = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 3, RowCount = 1, BackColor = AppTheme.BgSurface };
            opciones.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (var lang in new[] { "C#", "Python", "SQL" })
            {
                opciones.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                var button = new IconButton { Text = lang, Name = "Lenguaje" + lang, AccessibleName = "Lenguaje " + lang, Kind = BtnKind.Ghost, Dock = DockStyle.Fill, Radius = 4, PadX = 6, Margin = new Padding(0, 0, 4, 0) };
                _tips.SetToolTip(button, "Cambiar el lenguaje a " + lang + "; conserva el codigo, no lo convierte");
                button.Click += (s, e) => CambiarLenguaje(lang);
                _botonesLenguaje.Add(lang, button);
                opciones.Controls.Add(button, opciones.Controls.Count, 0);
            }
            var sdk = new LinkLabel { Text = "Manual del SDK", Dock = DockStyle.Bottom, Height = 22, LinkColor = AppTheme.Primary, ActiveLinkColor = AppTheme.PrimaryHover, Font = AppTheme.FontSmall, BackColor = AppTheme.BgSurface };
            sdk.LinkClicked += (s, e) => MostrarManualSdk();
            host.Controls.Add(opciones);
            host.Controls.Add(titulo);
            host.Controls.Add(sdk);
            return host;
        }

        private void ToggleContexto()
        {
            if (_zen) ToggleZen();
            _contextoVisible = !_contextoVisible;
            _splitMain.Panel2Collapsed = !_contextoVisible;
            if (_contextoVisible)
            {
                int ancho = Math.Max(_splitMain.Panel2MinSize, Math.Min(LogicalToDeviceUnits(330), _splitMain.Width / 2));
                _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize, _splitMain.Width - ancho - _splitMain.SplitterWidth);
            }
            _btnContexto.Kind = _contextoVisible ? BtnKind.Outline : BtnKind.Toolbar;
            _btnContexto.Invalidate();
        }

        private Panel ConstruirPanelIzquierdo()
        {
            var pnlIzq = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(16, 18, 16, 14) };

            var pnlLibHead = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.BgSurface };
            var lblScripts = new Label { Text = "Biblioteca", Dock = DockStyle.Fill, ForeColor = AppTheme.TextMain, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.MiddleLeft };
            var btnExpandir = new IconButton { Glyph = Glyph.Down, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 24, Radius = 4, ForeColor = AppTheme.TextMuted };
            _tips.SetToolTip(btnExpandir, "Expandir / contraer todo");
            btnExpandir.Click += (s, e) =>
            {
                bool algunaColapsada = _tree.Nodes.Cast<TreeNode>().Any(n => !n.IsExpanded);
                if (algunaColapsada) _tree.ExpandAll(); else _tree.CollapseAll();
                btnExpandir.Glyph = algunaColapsada ? Glyph.Up : Glyph.Down; btnExpandir.Invalidate();
            };
            pnlLibHead.Controls.Add(lblScripts);
            pnlLibHead.Controls.Add(btnExpandir);

            // Caja de búsqueda con icono y placeholder.
            var pnlBuscar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.BgSurface, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(28, 0, 6, 0) };
            pnlBuscar.Paint += (s, e) =>
            {
                using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, pnlBuscar.Width - 1, pnlBuscar.Height - 1), 6))
                    e.Graphics.DrawPath(p, path);
                TextRenderer.DrawText(e.Graphics, Glyph.Search, AppTheme.FontIconSmall, new Rectangle(8, 0, 18, pnlBuscar.Height), AppTheme.TextMuted, TextFormatFlags.VerticalCenter);
            };
            _txtBuscar = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain };
            _txtBuscar.TextChanged += (s, e) => CargarArbol();
            var pnlBuscarInner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 6, 0, 0) };
            pnlBuscarInner.Controls.Add(_txtBuscar);
            // Placeholder como capa (no toca el .Text): así seleccionar un nodo no reconstruye el árbol.
            AplicarPlaceholder(_txtBuscar, pnlBuscarInner, "Buscar scripts...");
            pnlBuscar.Controls.Add(pnlBuscarInner);

            var espTop = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.BgMain };

            _tree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain, ItemHeight = 30, ShowLines = false, ShowRootLines = true, ShowPlusMinus = true, FullRowSelect = true, Indent = 16, ShowNodeToolTips = true };
            _tree.ImageList = ConstruirIconosArbol();
            _tree.NodeMouseDoubleClick += (s, e) =>
            {
                if (e.Node.Tag is string tag && tag.StartsWith("sql:")) AbrirScript(tag.Substring(4));
                else if (e.Node.Tag is KeyValuePair<string, string> p) InsertarEnEditor(p.Value);
                else { _tree.ExpandAll(); e.Node.EnsureVisible(); }   // doble clic en carpeta: despliega todo
            };
            _tree.NodeMouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                if (e.Node.Tag is string tg && tg.StartsWith("sql:")) { _tree.SelectedNode = e.Node; TreeMenu(e.Node); }
                else if (e.Node.Tag is KeyValuePair<string, string> kv) { _tree.SelectedNode = e.Node; TemplateMenu(e.Node, kv); }
            };

            var btnNuevo = new IconButton { Glyph = Glyph.Add, Text = "Nuevo script", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Dock = DockStyle.Bottom, Height = 38, Margin = new Padding(0, 10, 0, 0) };
            btnNuevo.Click += (s, e) => NuevoScript();
            var pnlBtnNuevo = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = AppTheme.BgMain, Padding = new Padding(0, 10, 0, 0) };
            pnlBtnNuevo.Controls.Add(btnNuevo);

            pnlIzq.Controls.Add(_tree);
            pnlIzq.Controls.Add(espTop);
            pnlIzq.Controls.Add(pnlBuscar);
            pnlIzq.Controls.Add(pnlLibHead);
            pnlIzq.Controls.Add(ConstruirLenguajes());
            pnlIzq.Controls.Add(pnlBtnNuevo);
            return pnlIzq;
        }

        private ImageList ConstruirIconosArbol()
        {
            var il = new ImageList { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit };
            il.Images.Add("folder",   ModernUI.GlyphImage(Glyph.Folder,   18, AppTheme.Primary, 11f));
            il.Images.Add("script",   ModernUI.GlyphImage(Glyph.Script,   18, AppTheme.TextMuted, 11f));
            il.Images.Add("template", ModernUI.GlyphImage(Glyph.Template, 18, AppTheme.TextMuted, 11f));
            il.Images.Add("muted",    ModernUI.GlyphImage(Glyph.Script,   18, AppTheme.Border, 11f));
            return il;
        }

        // =========================================================
        //   Centro — Editor con pestaña y controles
        // =========================================================
        private Panel ConstruirEditor()
        {
            var pnlEditorHost = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };

            _pnlEditorHost = new Panel { Dock = DockStyle.Fill, BackColor = ConsolaMarca, Padding = new Padding(0, 0, 4, 4) };

            var pnlTabEditor = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = AppTheme.BgMain };
            pnlTabEditor.Paint += (s, e) => BordeInferior(e.Graphics, pnlTabEditor);

            _tabStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, BackColor = AppTheme.BgMain };
            
            var pnlMasTab = new Panel { Dock = DockStyle.Left, Width = 34, BackColor = AppTheme.BgMain };
            var btnMasTab = new IconButton { Glyph = Glyph.Add, Kind = BtnKind.Ghost, Dock = DockStyle.Fill, Radius = 4 };
            _tips.SetToolTip(btnMasTab, "Nuevo script");
            btnMasTab.Click += (s, e) => NuevoScript();
            pnlMasTab.Controls.Add(btnMasTab);

            // (Controles del editor movidos a la barra principal)
            pnlTabEditor.Controls.Add(_tabStrip);
            pnlTabEditor.Controls.Add(pnlMasTab);


            _pnlEditorHost.Controls.Add(ConstruirBarraBuscar());
            _pnlEditorHost.Controls.Add(_herramientasEditor);
            _pnlEditorHost.Controls.Add(pnlTabEditor);
            return _pnlEditorHost;

        }

        // Barra de búsqueda incremental del editor (oculta por defecto).
        private Panel ConstruirBarraBuscar()
        {
            var barra = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MinimumSize = new Size(0, 38), BackColor = AppTheme.BgMain, Padding = new Padding(6, 5, 6, 5), Visible = false };
            _findBar = barra;
            _findBar.Paint += (s, e) => BordeInferior(e.Graphics, _findBar);

            var caja = new Panel { Width = 160, Height = 28, BackColor = AppTheme.BgSurface, Padding = new Padding(8, 5, 8, 0) };
            caja.Paint += (s, e) => { using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, caja.Width - 1, caja.Height - 1), 5)) e.Graphics.DrawPath(p, path); };
            _txtFind = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMain };
            _txtFind.TextChanged += (s, e) => BuscarResaltar();
            _txtFind.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; BuscarMover(e.Shift ? -1 : 1); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; OcultarBuscar(); }
            };
            caja.Controls.Add(_txtFind);

            var btnPrev = new IconButton { Glyph = Glyph.Up, Kind = BtnKind.Ghost, Dock = DockStyle.Left, Width = 30, Radius = 4, Margin = new Padding(6, 0, 0, 0) };
            _tips.SetToolTip(btnPrev, "Anterior (Shift+Enter / Shift+F3)");
            btnPrev.Click += (s, e) => BuscarMover(-1);
            var btnNext = new IconButton { Glyph = Glyph.Down, Kind = BtnKind.Ghost, Dock = DockStyle.Left, Width = 30, Radius = 4 };
            _tips.SetToolTip(btnNext, "Siguiente (Enter / F3)");
            btnNext.Click += (s, e) => BuscarMover(1);
            _lblFindCount = new Label { Dock = DockStyle.Left, AutoSize = false, Width = 110, TextAlign = ContentAlignment.MiddleLeft, ForeColor = AppTheme.TextMuted, Font = AppTheme.FontSmall, Padding = new Padding(8, 0, 0, 0), Text = "" };
            var btnCerrar = new IconButton { Glyph = Glyph.Close, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 30, Radius = 4, Font = AppTheme.FontIconSmall };
            _tips.SetToolTip(btnCerrar, "Cerrar (Esc)");
            btnCerrar.Click += (s, e) => OcultarBuscar();

            _findMayusculas = new CheckBox { Text = "Aa", AutoSize = false, Width = 42, Height = 28, ForeColor = AppTheme.TextMain };
            _findPalabra = new CheckBox { Text = "Palabra", AutoSize = false, Width = 72, Height = 28, ForeColor = AppTheme.TextMain };
            _tips.SetToolTip(_findMayusculas, "Distinguir mayusculas y minusculas");
            _tips.SetToolTip(_findPalabra, "Buscar solo palabras completas");
            _findMayusculas.CheckedChanged += (s, e) => BuscarResaltar();
            _findPalabra.CheckedChanged += (s, e) => BuscarResaltar();
            _lblFindCount.Width = 85;
            foreach (var control in new Control[] { caja, _findMayusculas, _findPalabra, btnPrev, btnNext, _lblFindCount, btnCerrar })
            {
                control.Dock = DockStyle.None;
                control.Height = 28;
                control.Margin = new Padding(1, 0, 1, 0);
                barra.Controls.Add(control);
            }
            return _findBar;
        }

        // =========================================================
        //   Centro inferior — Consola de salida
        // =========================================================
        private Panel ConstruirSalida()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };
            host.Paint += (s, e) => { using (var p = new Pen(AppTheme.Border)) e.Graphics.DrawLine(p, 0, 0, host.Width, 0); };

            // Barra de herramientas de la consola (contador + limpiar + copiar).
            var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.BgMain };
            bar.Paint += (s, e) => BordeInferior(e.Graphics, bar);
            _lblErrCount = new Label { Dock = DockStyle.Left, AutoSize = false, Width = 160, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted, Padding = new Padding(10, 0, 0, 0), Text = "Sin errores" };
            var btnCopiar = new IconButton { Glyph = Glyph.Copy, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 32, Radius = 4 };
            _tips.SetToolTip(btnCopiar, "Copiar salida");
            btnCopiar.Click += (s, e) => CopiarSalida();
            var btnLimpiar = new IconButton { Glyph = Glyph.Clear, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 32, Radius = 4 };
            _tips.SetToolTip(btnLimpiar, "Limpiar salida");
            btnLimpiar.Click += (s, e) => LimpiarSalida();
            bar.Controls.Add(_lblErrCount);
            bar.Controls.Add(btnLimpiar);
            bar.Controls.Add(btnCopiar);

            _tabsOut = new TabControl { Dock = DockStyle.Fill, DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed, ItemSize = new Size(96, 28), Padding = new Point(16, 4), SizeMode = TabSizeMode.Fixed };
            _tabsOut.DrawItem += DrawFlatTab;
            _outSalida   = NuevoOut();
            _outErrores  = NuevoOut();
            _outMensajes = NuevoOut();
            _tabsOut.TabPages.Add(NuevaTab("Salida", _outSalida));
            _tabsOut.TabPages.Add(NuevaTab("Errores", _outErrores));
            _tabsOut.TabPages.Add(NuevaTab("Mensajes", _outMensajes));

            host.Controls.Add(_tabsOut);
            host.Controls.Add(bar);
            return host;
        }

        // =========================================================
        //   Panel derecho — Contexto y referencias
        // =========================================================
        private Panel ConstruirPanelDerecho()
        {
            var pnlDer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgMain, Padding = new Padding(12, 14, 14, 14) };

            // --- Tarjeta de contexto ---
            var pnlCtxCard = new Panel { Dock = DockStyle.Top, Height = 214, BackColor = AppTheme.BgSurface, Padding = new Padding(14, 12, 14, 12) };
            pnlCtxCard.Paint += (s, e) => BordeTarjeta(e.Graphics, pnlCtxCard);
            pnlDer.Resize += (s, e) => pnlCtxCard.Height = Math.Min(LogicalToDeviceUnits(214),
                Math.Max(LogicalToDeviceUnits(160), pnlDer.ClientSize.Height - LogicalToDeviceUnits(310)));

            var pnlCtxHead = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = AppTheme.BgSurface };
            var btnCtxRefresh = new IconButton { Glyph = Glyph.Refresh, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 28, Radius = 4 };
            _tips.SetToolTip(btnCtxRefresh, "Actualizar contexto");
            btnCtxRefresh.Click += (s, e) => ActualizarContexto();
            _lblCtx = new Label { Dock = DockStyle.Right, AutoSize = false, Width = 134, TextAlign = ContentAlignment.MiddleRight, ForeColor = AppTheme.TextMuted, Font = AppTheme.FontSmall, Text = "", Margin = new Padding(0, 0, 4, 0) };
            var lblCtxT = new Label { Text = "Contexto actual", Dock = DockStyle.Fill, ForeColor = AppTheme.Primary, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.MiddleLeft };
            pnlCtxHead.Controls.Add(lblCtxT);
            pnlCtxHead.Controls.Add(_lblCtx);
            pnlCtxHead.Controls.Add(btnCtxRefresh);

            // Contexto en forma de lista: Campo / Valor. Valores largos (la Vista/SELECT)
            // se ven completos con tooltip y se pueden copiar con doble clic.
            _lstCtx = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = AppTheme.FontMain, ShowItemToolTips = true };
            _lstCtx.Columns.Add("Campo", 88);
            _lstCtx.Columns.Add("Valor", 230);
            foreach (var campo in new[] { "Empresa", "Usuario", "Módulo", "Owner", "Vista", "Selección" })
            {
                var it = new ListViewItem(campo);
                it.SubItems.Add("—");
                _lstCtx.Items.Add(it);
            }
            _lstCtx.DoubleClick += (s, e) =>
            {
                if (_lstCtx.SelectedItems.Count > 0 && _lstCtx.SelectedItems[0].SubItems.Count > 1)
                    try { Clipboard.SetText(_lstCtx.SelectedItems[0].SubItems[1].Text); _status.Text = "Copiado: " + _lstCtx.SelectedItems[0].Text; } catch { }
            };
            _tips.SetToolTip(_lstCtx, "Doble clic en una fila para copiar su valor");
            var pnlCtxBody = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 6, 0, 0) };
            pnlCtxBody.Controls.Add(_lstCtx);

            pnlCtxCard.Controls.Add(pnlCtxBody);
            pnlCtxCard.Controls.Add(pnlCtxHead);

            var espCtx = new Panel { Dock = DockStyle.Top, Height = 14, BackColor = AppTheme.BgMain };

            // --- Tarjeta de referencias ---
            var lblMet = new Label { Text = "Referencias del SDK", Dock = DockStyle.Top, Height = 26, ForeColor = AppTheme.Primary, Font = AppTheme.FontTitle, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6) };
            _tips.SetToolTip(lblMet, "Doble clic para insertar; clic secundario para ver la ficha");

            var pnlRefsCard = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface };
            pnlRefsCard.Paint += (s, e) => BordeTarjeta(e.Graphics, pnlRefsCard);

            // Pestañas en TableLayoutPanel de 4 columnas iguales: nunca se recortan ni se enciman.
            var pnlTabs = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 4, RowCount = 1, BackColor = AppTheme.BgMain };
            for (int i = 0; i < 4; i++) pnlTabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            pnlTabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pnlTabs.Paint += (s, e) => BordeInferior(e.Graphics, pnlTabs);
            var pnlHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };

            _lstMetodosCSharp = NuevaListaRef("Método", "Descripción");
            _lstMetodosCSharp.DoubleClick += (s, e) => { if (_lstMetodosCSharp.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosCSharp.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            AgregarMenuFicha(_lstMetodosCSharp);
            pnlHost.Controls.Add(_lstMetodosCSharp);

            _lstMetodosPython = NuevaListaRef("Método", "Descripción");
            _lstMetodosPython.DoubleClick += (s, e) => { if (_lstMetodosPython.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosPython.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            AgregarMenuFicha(_lstMetodosPython);
            pnlHost.Controls.Add(_lstMetodosPython);

            _lstMetodosSql = NuevaListaRef("Token/SQL", "Descripción");
            _lstMetodosSql.DoubleClick += (s, e) => { if (_lstMetodosSql.SelectedItems.Count > 0) InsertarEnEditor(((MetodoCtx)_lstMetodosSql.SelectedItems[0].Tag).Ejemplo + "\r\n"); };
            AgregarMenuFicha(_lstMetodosSql);
            pnlHost.Controls.Add(_lstMetodosSql);

            _lstSeleccion = NuevaListaRef("Campo", "Valor");
            _lstSeleccion.DoubleClick += (s, e) =>
            {
                if (_lstSeleccion.SelectedItems.Count == 0) return;
                var item = _lstSeleccion.SelectedItems[0];
                string codigo = _editor.Text;
                bool esPython = HostClient.EsPython(codigo);
                bool esSql = HostClient.EsSql(codigo);
                if (item.Tag is TokenFijo tok)
                {
                    InsertarEnEditor(esPython ? tok.Python : esSql ? tok.Sql : tok.CSharp);
                    return;
                }
                string campo = item.Text;
                if (esPython) InsertarEnEditor("ctx.fila[\"" + campo + "\"]");
                else if (esSql)
                {
                    // El campo mostrado aquí viene del grid activo de Comercial (alias de vista,
                    // vía ScriptContext.GetFilaActiva()) y no siempre corresponde a una columna
                    // real de la tabla base. Se valida contra docDocument (heurística pragmática:
                    // es la tabla base de casi todas las plantillas del catálogo) para decidir si
                    // se ofrece el token nuevo {DATOS:Tabla.Columna} (con formulario) o, si no es
                    // columna real, el token viejo de solo lectura {DATOS:Campo}.
                    string dataType; int? maxLen;
                    bool esColumnaReal;
                    try { esColumnaReal = HostClient.ExisteColumnaReal("docDocument", campo, _ctx, out dataType, out maxLen); }
                    catch { esColumnaReal = false; }

                    if (esColumnaReal)
                    {
                        InsertarEnEditor("{DATOS:docDocument." + campo + ":*}");
                        _status.Text = "Insertado como campo de formulario (docDocument." + campo + ")";
                    }
                    else
                    {
                        InsertarEnEditor("{DATOS:" + campo + "}");
                        _status.Text = "'" + campo + "' no es una columna real de docDocument (probablemente alias de vista) -- se insertó el token de solo lectura {DATOS:...}";
                    }
                }
                else InsertarEnEditor("fila[\"" + campo + "\"]");
            };
            pnlHost.Controls.Add(_lstSeleccion);

            var btnTabs = new IconButton[4];
            var listas = new Control[] { _lstMetodosCSharp, _lstMetodosPython, _lstMetodosSql, _lstSeleccion };
            Action<Control, IconButton> ActivarTab = (c, b) =>
            {
                foreach (var lv in listas) lv.Visible = (lv == c);
                c.BringToFront();
                foreach (var bb in btnTabs) { bb.Kind = BtnKind.Ghost; bb.ForeColor = AppTheme.TextMuted; bb.Invalidate(); }
                b.Kind = BtnKind.Outline; b.Accent = AppTheme.Primary; b.Invalidate();
            };
            Func<string, IconButton> NuevoTab = (txt) =>
                new IconButton { Text = txt, Kind = BtnKind.Ghost, Dock = DockStyle.Fill, Margin = new Padding(0), Radius = 0, ForeColor = AppTheme.TextMuted };

            var btnC = btnTabs[0] = NuevoTab("C#");
            var btnP = btnTabs[1] = NuevoTab("Python");
            var btnS = btnTabs[2] = NuevoTab("SQL");
            var btnD = btnTabs[3] = NuevoTab("Tokens");
            btnC.Click += (s, e) => ActivarTab(_lstMetodosCSharp, btnC);
            btnP.Click += (s, e) => ActivarTab(_lstMetodosPython, btnP);
            btnS.Click += (s, e) => ActivarTab(_lstMetodosSql, btnS);
            btnD.Click += (s, e) => ActivarTab(_lstSeleccion, btnD);
            pnlTabs.Controls.Add(btnC, 0, 0); pnlTabs.Controls.Add(btnP, 1, 0);
            pnlTabs.Controls.Add(btnS, 2, 0); pnlTabs.Controls.Add(btnD, 3, 0);
            _activarReferencia = indice => ActivarTab(listas[indice], btnTabs[indice]);

            pnlRefsCard.Controls.Add(pnlHost);
            var buscarSdk = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 8, 8, 5), BackColor = AppTheme.BgSurface };
            _txtSdk = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, AccessibleName = "Buscar funciones del SDK" };
            buscarSdk.Controls.Add(_txtSdk);
            AplicarPlaceholder(_txtSdk, buscarSdk, "Buscar funciones del SDK...");
            _txtSdk.TextChanged += (s, e) => CargarMetodos();
            _tips.SetToolTip(_txtSdk, "Filtrar funciones por nombre, descripcion o categoria");
            pnlRefsCard.Controls.Add(buscarSdk);
            pnlRefsCard.Controls.Add(pnlTabs);
            ActivarTab(_lstMetodosCSharp, btnC);

            var btnRefresca = new IconButton { Glyph = Glyph.Refresh, Text = "Actualizar contexto", Kind = BtnKind.Outline, Accent = AppTheme.Primary, Dock = DockStyle.Bottom, Height = 38, Margin = new Padding(0, 10, 0, 0) };
            btnRefresca.Click += (s, e) => ActualizarContexto();
            var pnlBtnRef = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = AppTheme.BgMain, Padding = new Padding(0, 10, 0, 0) };
            pnlBtnRef.Controls.Add(btnRefresca);

            pnlDer.Controls.Add(pnlRefsCard);
            pnlDer.Controls.Add(lblMet);
            pnlDer.Controls.Add(espCtx);
            pnlDer.Controls.Add(pnlCtxCard);
            pnlDer.Controls.Add(pnlBtnRef);
            return pnlDer;
        }

        private ListView NuevaListaRef(string col1, string col2)
        {
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, BackColor = AppTheme.BgSurface, ForeColor = AppTheme.TextMain, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = AppTheme.FontMain, ShowItemToolTips = true };
            lv.Columns.Add(col1, 120); lv.Columns.Add(col2, 230);
            return lv;
        }

        // internal (no private): NuevaAccionForm (misma namespace, otra clase) la reusa
        // para que sus tarjetas se vean iguales a las del resto de la Consola.
        internal static void BordeTarjeta(Graphics g, Control c)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new Pen(AppTheme.Border)) using (var path = ModernUI.Round(new Rectangle(0, 0, c.Width - 1, c.Height - 1), 8))
                g.DrawPath(p, path);
        }

        // Placeholder como CAPA encima del TextBox: nunca modifica el .Text del control,
        // por lo que cambiar el foco (p. ej. al hacer clic en un nodo del árbol) no dispara
        // un filtrado/reconstrucción del árbol. El clic en la capa enfoca el TextBox.
        private void AplicarPlaceholder(TextBox t, Panel host, string hint)
        {
            var ph = new Label { Text = hint, Dock = DockStyle.Fill, BackColor = t.BackColor, ForeColor = AppTheme.TextMuted, Font = t.Font, TextAlign = ContentAlignment.MiddleLeft };
            ph.Click += (s, e) => t.Focus();
            host.Controls.Add(ph);
            ph.BringToFront();
            Action upd = () => ph.Visible = (t.Text.Length == 0 && !t.Focused);
            t.TextChanged += (s, e) => upd();
            t.GotFocus    += (s, e) => upd();
            t.LostFocus   += (s, e) => upd();
            upd();
        }

        private void CopiarSalida()
        {
            var box = _tabsOut.SelectedIndex == 1 ? _outErrores : _tabsOut.SelectedIndex == 2 ? _outMensajes : _outSalida;
            try { if (!string.IsNullOrEmpty(box.Text)) Clipboard.SetText(box.Text); } catch { }
        }
        private void LimpiarSalida()
        {
            var box = _tabsOut.SelectedIndex == 1 ? _outErrores : _tabsOut.SelectedIndex == 2 ? _outMensajes : _outSalida;
            box.Clear();
            if (box == _outErrores) ActualizarContadorErrores();
        }
        private void ActualizarContadorErrores()
        {
            if (_lblErrCount == null) return;
            int n = _outErrores.TextLength == 0 ? 0 : _outErrores.Lines.Count(l => l.StartsWith("["));
            _lblErrCount.Text = n == 0 ? "Sin errores" : (n + (n == 1 ? " error" : " errores"));
            _lblErrCount.ForeColor = n == 0 ? AppTheme.TextMuted : AppTheme.Error;
        }

        // =========================================================
        //   Buscar en el editor (Ctrl+F / Ctrl+B, F3, resaltado)
        // =========================================================
        private void MostrarBuscar()
        {
            if (_findBar == null) return;
            // Si hay texto seleccionado, úsalo como término inicial.
            string sel = _editor.SelectedText;
            _findBar.Visible = true;
            if (!string.IsNullOrEmpty(sel) && !sel.Contains("\n")) _txtFind.Text = sel;
            _txtFind.Focus();
            _txtFind.SelectAll();
            BuscarResaltar();
        }

        private void OcultarBuscar()
        {
            if (_findBar == null) return;
            _findBar.Visible = false;
            try { _editor.IndicatorCurrent = IND_FIND; _editor.IndicatorClearRange(0, _editor.TextLength); } catch { }
            _findHits.Clear(); _findIdx = -1;
            _editor.Focus();
        }

        // Resalta todas las coincidencias y deja lista la navegación.
        private void BuscarResaltar()
        {
            ActualizarBusqueda(true);
        }

        private static bool CaracterPalabra(char c) => char.IsLetterOrDigit(c) || c == '_';

        private void ActualizarBusqueda(bool seleccionar)
        {
            _findHits.Clear(); _findIdx = -1;
            try
            {
                _editor.Indicators[IND_FIND].Style = IndicatorStyle.RoundBox;
                _editor.Indicators[IND_FIND].ForeColor = AppTheme.Warning;
                _editor.Indicators[IND_FIND].Alpha = 70;
                _editor.Indicators[IND_FIND].OutlineAlpha = 120;
                _editor.IndicatorCurrent = IND_FIND;
                _editor.IndicatorClearRange(0, _editor.TextLength);
            }
            catch { }

            string q = _txtFind.Text;
            if (string.IsNullOrEmpty(q)) { if (_lblFindCount != null) _lblFindCount.Text = ""; return; }

            string txt = _editor.Text;
            int from = 0;
            while (true)
            {
                int idx = txt.IndexOf(q, from, _findMayusculas.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                if (!_findPalabra.Checked ||
                    ((idx == 0 || !CaracterPalabra(txt[idx - 1])) &&
                    (idx + q.Length == txt.Length || !CaracterPalabra(txt[idx + q.Length]))))
                {
                    _findHits.Add(idx);
                    try { _editor.IndicatorFillRange(idx, q.Length); } catch { }
                }
                from = idx + Math.Max(1, q.Length);
            }

            if (_findHits.Count == 0) { _lblFindCount.Text = "Sin coincidencias"; _lblFindCount.ForeColor = AppTheme.Error; return; }
            _lblFindCount.ForeColor = AppTheme.TextMuted;
            // Posiciona en la 1a coincidencia a partir del cursor.
            int caret = _editor.CurrentPosition;
            _findIdx = _findHits.FindIndex(p => p >= caret);
            if (_findIdx < 0) _findIdx = 0;
            if (seleccionar) SeleccionarHit(q.Length);
            else _lblFindCount.Text = _findHits.Count + " coincidencias";
        }

        private void BuscarMover(int dir)
        {
            if (_findHits.Count == 0) { BuscarResaltar(); if (_findHits.Count == 0) return; }
            _findIdx = (_findIdx + dir + _findHits.Count) % _findHits.Count;
            SeleccionarHit(_txtFind.Text.Length);
        }

        private void SeleccionarHit(int len)
        {
            if (_findIdx < 0 || _findIdx >= _findHits.Count) return;
            int start = _findHits[_findIdx];
            _editor.SetSelection(start, start + len);
            _editor.ScrollCaret();
            if (_lblFindCount != null) _lblFindCount.Text = (_findIdx + 1) + " de " + _findHits.Count;
        }

        // =========================================================
        //   Pantalla completa del editor (zen): colapsa los paneles.
        // =========================================================
        private void ToggleZen()
        {
            _zen = !_zen;
            try
            {
                _splitLeft.Panel1Collapsed = _zen;     // biblioteca
                _splitMain.Panel2Collapsed = _zen || !_contextoVisible;
                _splitEditor.Panel2Collapsed = _zen;   // salida
                _btnZen.Glyph = _zen ? Glyph.Restore : Glyph.Full;
                _btnZen.Invalidate();
                _tips.SetToolTip(_btnZen, _zen ? "Salir de pantalla completa (F11)" : "Pantalla completa del editor (F11)");
            }
            catch { }
        }

        private void AplicarTamanoFuente(int pt)
        {
            _fontSize = pt;
            try { EstilizarEditor(pt); } catch { }
        }

        private RichTextBox NuevoOut()
        {
            return new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = ConsolaSuperficie, ForeColor = AppTheme.TextMain, Font = AppTheme.FontMono, WordWrap = false, Padding = new Padding(8) };
        }
        private TabPage NuevaTab(string titulo, Control c) { var t = new TabPage(titulo) { BackColor = AppTheme.BgSurface }; c.Dock = DockStyle.Fill; t.Controls.Add(c); return t; }

        private void DrawFlatTab(object sender, DrawItemEventArgs e)
        {
            var tab = (TabControl)sender;
            var page = tab.TabPages[e.Index];
            var rect = e.Bounds;
            
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            using (var fondo = new SolidBrush(ConsolaChrome)) e.Graphics.FillRectangle(fondo, rect);
            
            if (isSelected)
            {
                using (var fondo = new SolidBrush(ConsolaSuperficie)) e.Graphics.FillRectangle(fondo, rect);
                using (var acento = new SolidBrush(AppTheme.Primary)) e.Graphics.FillRectangle(acento, new Rectangle(rect.X, rect.Y, rect.Width, 2));
            }
            
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var brush = new SolidBrush(isSelected ? AppTheme.Primary : AppTheme.TextMuted))
                e.Graphics.DrawString(page.Text, AppTheme.FontMain, brush, rect, format);
        }

        // Lee un recurso embebido del addon (logo, icono) por terminacion del nombre.
        private static System.IO.Stream Recurso(string endsWith)
        {
            var asm = Assembly.GetExecutingAssembly();
            var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            return n == null ? null : asm.GetManifestResourceStream(n);
        }

        // =====================================================
        //   Editor Scintilla
        // =====================================================
        private void ConfigurarEditor()
        {
            var ed = _editor;
            EstilizarEditor(_fontSize);
            ed.UseTabs = false;
            ed.TabWidth = 4;
            ed.IndentWidth = 4;
            ed.AutoCIgnoreCase = true;
            ed.AutoCMaxHeight = 12;

            // Posición del cursor en la barra de estado.
            ed.UpdateUI += (s, e) =>
            {
                if (_statusPos == null) return;
                int line = ed.CurrentLine + 1;
                int col = ed.CurrentPosition - ed.Lines[ed.CurrentLine].Position + 1;
                _statusPos.Text = "Lín " + line + ", Col " + col;
                ResaltarPareja(ed);
            };
            var tab = _activeTab;
            ed.TextChanged += (s, e) =>
            {
                tab.IsModified = true;
                if (tab.LblName != null) tab.LblName.Text = tab.Titulo;
                if (tab == _activeTab)
                {
                    DetectarLenguajeStatus(); RefrescarEstadoDoc();
                    if (_findBar.Visible) ActualizarBusqueda(false);
                }
            };

            // Atajos dentro del editor (Scintilla es nativo y a veces captura las teclas
            // antes que KeyPreview del formulario, así que los atendemos aquí también).
            ed.KeyDown += (s, e) =>
            {
                if (AtajoArchivo(e)) return;
                if (e.Control && e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; MostrarCompletado(ed); }
                else if (e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.B)) { e.SuppressKeyPress = true; MostrarBuscar(); }
                else if (e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; if (_findBar != null && _findBar.Visible) BuscarMover(e.Shift ? -1 : 1); else MostrarBuscar(); }
                else if (e.KeyCode == Keys.F11) { e.SuppressKeyPress = true; ToggleZen(); }
                else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; Ejecutar(false); }
            };

            ed.CharAdded += (s, e) =>
            {
                if (e.Char == '.') MostrarCompletado(ed);
                else if (e.Char == '\n') IndentarLinea(ed);
            };
        }

        private bool AtajoArchivo(KeyEventArgs e)
        {
            if (!e.Control || e.Alt || (e.KeyCode != Keys.S && e.KeyCode != Keys.N)) return false;
            e.Handled = e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.N) NuevoScript();
            else Guardar(e.Shift);
            return true;
        }

        // Sugerencias del catalogo, no un analizador semantico ni ejecucion de codigo.
        private string[] OpcionesCompletado(Scintilla ed, out int longitud)
        {
            longitud = 0;
            if (HostClient.EsSql(ed.Text)) return new string[0];
            int pos = ed.CurrentPosition;
            int inicio = ed.Lines[ed.CurrentLine].Position;
            ed.Colorize(inicio, pos);
            int estilo = pos == 0 ? 0 : ed.GetStyleAt(pos - 1);
            if (ed.Lexer == Lexer.Cpp && (estilo == Style.Cpp.Comment || estilo == Style.Cpp.CommentLine ||
                estilo == Style.Cpp.CommentDoc || estilo == Style.Cpp.CommentLineDoc || estilo == Style.Cpp.String ||
                estilo == Style.Cpp.StringEol || estilo == Style.Cpp.Verbatim || estilo == Style.Cpp.Character)) return new string[0];
            if (ed.Lexer == Lexer.Python && (estilo == Style.Python.CommentLine || estilo == Style.Python.String ||
                estilo == Style.Python.StringEol || estilo == Style.Python.CommentBlock || estilo == Style.Python.Character ||
                estilo == Style.Python.Triple || estilo == Style.Python.TripleDouble)) return new string[0];
            var match = Regex.Match(ed.GetTextRange(inicio, pos - inicio), @"\bctx\.(erp\.)?([A-Za-z_0-9]*)$");
            if (!match.Success) return new string[0];
            longitud = match.Groups[2].Length;
            string raiz = "ctx." + match.Groups[1].Value;
            string prefijo = match.Groups[2].Value;
            var catalogo = HostClient.EsPython(ed.Text) ? METODOS_PYTHON : METODOS;
            return catalogo.Select(m => Regex.Match(m.Firma, @"\bctx\.[A-Za-z_0-9.]+").Value)
                .Where(nombre => nombre.StartsWith(raiz, StringComparison.Ordinal))
                .Select(nombre => nombre.Substring(raiz.Length).Split('.')[0])
                .Where(nombre => nombre.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(nombre => nombre, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void MostrarCompletado(Scintilla ed)
        {
            var opciones = OpcionesCompletado(ed, out int longitud);
            if (opciones.Length > 0) ed.AutoCShow(longitud, string.Join(" ", opciones));
        }

        private static void IndentarLinea(Scintilla ed)
        {
            if (ed.CurrentLine == 0) return;
            var anterior = ed.Lines[ed.CurrentLine - 1];
            string texto = anterior.Text.TrimEnd();
            int indentacion = anterior.Indentation;
            if ((ed.Lexer == Lexer.Python && texto.EndsWith(":")) ||
                (ed.Lexer == Lexer.Cpp && texto.EndsWith("{"))) indentacion += ed.IndentWidth;
            ed.Lines[ed.CurrentLine].Indentation = indentacion;
            ed.GotoPosition(ed.Lines[ed.CurrentLine].Position + ed.Lines[ed.CurrentLine].Indentation);
        }

        private static void ResaltarPareja(Scintilla ed)
        {
            int pos = ed.CurrentPosition;
            int candidato = pos > 0 && "()[]{}".IndexOf((char)ed.GetCharAt(pos - 1)) >= 0 ? pos - 1 :
                pos < ed.TextLength && "()[]{}".IndexOf((char)ed.GetCharAt(pos)) >= 0 ? pos : -1;
            if (candidato < 0) { ed.BraceHighlight(-1, -1); return; }
            int pareja = ed.BraceMatch(candidato);
            if (pareja >= 0) ed.BraceHighlight(candidato, pareja);
            else ed.BraceBadLight(candidato);
        }

        // Aplica colores, fuente y márgenes del editor para un tamaño dado (reutilizable).
        // Antes de v2.56.0 SIEMPRE usaba Lexer.Cpp, sin importar el lenguaje real del
        // script -- por eso un botón SQL (marcador "-- lang: sql") se veía todo del mismo
        // color: el lexer C++ no reconoce "--" como comentario, así que comentario y
        // código quedaban indistinguibles. Ahora el lexer se elige según el lenguaje
        // detectado (mismo detector que ya usa la barra de estado).
        private void EstilizarEditor(int size)
        {
            var ed = _editor;
            string codigo = ed.Text;
            bool esPython = HostClient.EsPython(codigo);
            bool esSql = !esPython && HostClient.EsSql(codigo);

            ed.Lexer = esPython ? Lexer.Python : esSql ? Lexer.Sql : Lexer.Cpp;
            ed.Styles[Style.Default].Font = AppTheme.FontMono.Name;
            ed.Styles[Style.Default].Size = size;
            ed.Styles[Style.Default].BackColor = ConsolaEditor;
            ed.Styles[Style.Default].ForeColor = ConsolaCodigo;
            ed.StyleClearAll();

            // Colores legibles sobre gris claro, sin fondo blanco intenso.
            var cComentario = Color.FromArgb(82, 106, 86);
            var cNumero     = Color.FromArgb(143, 67, 28);
            var cString     = Color.FromArgb(153, 56, 58);
            var cPalabra    = Color.FromArgb(38, 79, 152);
            var cPalabra2   = Color.FromArgb(105, 63, 137);
            var cOperador   = ConsolaCodigo;
            var cGris       = Color.FromArgb(96, 106, 119);

            if (esPython)
            {
                ed.SetKeywords(0, "and as assert async await break class continue def del elif else except finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield self");
                ed.Styles[Style.Python.CommentLine].ForeColor = cComentario;
                ed.Styles[Style.Python.Number].ForeColor = cNumero;
                ed.Styles[Style.Python.String].ForeColor = cString;
                ed.Styles[Style.Python.Character].ForeColor = cString;
                ed.Styles[Style.Python.Triple].ForeColor = cString;
                ed.Styles[Style.Python.TripleDouble].ForeColor = cString;
                ed.Styles[Style.Python.Word].ForeColor = cPalabra;
                ed.Styles[Style.Python.Word2].ForeColor = cPalabra2;
                ed.Styles[Style.Python.Operator].ForeColor = cOperador;
                ed.Styles[Style.Python.Decorator].ForeColor = cGris;
            }
            else if (esSql)
            {
                ed.SetKeywords(0, "select insert update delete from where join inner left right outer on group by order having as into values set null is not and or in like between top distinct union all case when then else end declare exec execute create alter drop table view index primary key foreign references default");
                ed.Styles[Style.Sql.Comment].ForeColor = cComentario;
                ed.Styles[Style.Sql.CommentLine].ForeColor = cComentario;
                ed.Styles[Style.Sql.CommentDoc].ForeColor = cGris;
                ed.Styles[Style.Sql.Number].ForeColor = cNumero;
                ed.Styles[Style.Sql.String].ForeColor = cString;
                ed.Styles[Style.Sql.Character].ForeColor = cString;
                ed.Styles[Style.Sql.Word].ForeColor = cPalabra;
                ed.Styles[Style.Sql.Word2].ForeColor = cPalabra2;
                ed.Styles[Style.Sql.Operator].ForeColor = cOperador;
                ed.Styles[Style.Sql.Identifier].ForeColor = ConsolaCodigo;
            }
            else
            {
                ed.SetKeywords(0, "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while async await dynamic");
                ed.SetKeywords(1, "List Dictionary StringBuilder DateTime Math Convert Console MessageBox Color Form ctx");
                ed.Styles[Style.Cpp.Comment].ForeColor = cComentario;
                ed.Styles[Style.Cpp.CommentLine].ForeColor = cComentario;
                ed.Styles[Style.Cpp.CommentLineDoc].ForeColor = cGris;
                ed.Styles[Style.Cpp.Number].ForeColor = cNumero;
                ed.Styles[Style.Cpp.String].ForeColor = cString;
                ed.Styles[Style.Cpp.Character].ForeColor = cString;
                ed.Styles[Style.Cpp.Word].ForeColor = cPalabra;
                ed.Styles[Style.Cpp.Word2].ForeColor = cPalabra2;
                ed.Styles[Style.Cpp.Operator].ForeColor = cOperador;
                ed.Styles[Style.Cpp.Preprocessor].ForeColor = cGris;
            }

            // Numeros de linea con la misma superficie del codigo.
            ed.Styles[Style.LineNumber].BackColor = ConsolaEditor;
            ed.Styles[Style.LineNumber].ForeColor = cGris;
            ed.Margins[0].Type = MarginType.Number;
            ed.Margins[0].Width = 46;
            ed.Margins[0].BackColor = ConsolaEditor;
            ed.Margins[1].Width = 8;
            ed.Margins[1].BackColor = ConsolaEditor;

            ed.CaretLineVisible = true;
            ed.CaretLineBackColor = Color.FromArgb(216, 223, 231);
            ed.CaretForeColor = ConsolaCodigo;
            ed.SetSelectionBackColor(true, Color.FromArgb(184, 204, 231));
            ed.SetSelectionForeColor(true, ConsolaCodigo);
            ed.Styles[Style.BraceLight].BackColor = Color.FromArgb(191, 212, 196);
            ed.Styles[Style.BraceLight].ForeColor = ConsolaCodigo;
            ed.Styles[Style.BraceBad].ForeColor = AppTheme.Error;
            ed.ExtraDescent = 3; // mejor interlineado
        }

        // ---- Lenguaje del script ----
        // El lenguaje se guarda como una línea «lang:» al inicio del código (así lo detectan la Consola, los botones y las terminales). El selector la escribe o la quita:
        // C# no lleva marca (es el predeterminado); Python lleva «# lang: python»; SQL lleva «-- lang: sql».
        private static readonly Regex RX_MARCA_LANG = new Regex(@"^\s*(#|//|--)\s*lang\s*:\s*\w+\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex RX_MARCA_VIEJA = new Regex(@"^\s*(#py|#sql|--sql)\s*$|^\s*(#|//|--)\s*broslmv:(python|sql|receta)\s*$", RegexOptions.IgnoreCase);

        private void MostrarMenuLenguaje(Control ancla, Point donde)
        {
            string actual = HostClient.EsPython(_editor.Text) ? "Python" : HostClient.EsSql(_editor.Text) ? "SQL" : "C#";
            var menu = new ContextMenuStrip { Font = AppTheme.FontMain };
            foreach (var l in new[] { "C#", "Python", "SQL" })
            {
                string lang = l;
                menu.Items.Add(new ToolStripMenuItem(lang, null, (s, e) => CambiarLenguaje(lang)) { Checked = lang == actual });
            }
            menu.Show(ancla, donde);
        }

        private void CambiarLenguaje(string lang)
        {
            if (_editor == null) return;
            string actual = HostClient.EsPython(_editor.Text) ? "Python" : HostClient.EsSql(_editor.Text) ? "SQL" : "C#";
            if (actual == lang) return;
            var lineas = new List<string>(_editor.Text.Replace("\r\n", "\n").Split('\n'));
            for (int i = Math.Min(lineas.Count, 12) - 1; i >= 0; i--)
                if (RX_MARCA_LANG.IsMatch(lineas[i]) || RX_MARCA_VIEJA.IsMatch(lineas[i])) lineas.RemoveAt(i);
            string cuerpo = string.Join("\r\n", lineas);
            bool vacio = string.IsNullOrWhiteSpace(cuerpo);
            string marca = lang == "Python" ? "# lang: python\r\n" : lang == "SQL" ? "-- lang: sql\r\n" : "";
            string inicio = "";
            if (vacio)
                inicio = lang == "Python" ? "from broslmv import ctx\r\n\r\n" : lang == "SQL" ? "-- Escribe aquí tu consulta\r\n" : "";
            _editor.Text = marca + (vacio ? inicio : cuerpo);
            _status.Text = "Lenguaje: " + lang + (vacio ? "" : " (el código se conservó; revisa que corresponda al nuevo lenguaje)");
            DetectarLenguajeStatus();
        }

        // Refleja el lenguaje detectado en la barra de estado, y re-aplica el lexer
        // correcto si el lenguaje cambió desde el último cambio de texto (p. ej. el
        // usuario acaba de escribir "-- lang: sql" en un script nuevo) -- sin este
        // chequeo, re-estilizar en CADA tecla sería un desperdicio y podría parpadear.
        private string _ultimoLenguajeEditor = "";
        private void DetectarLenguajeStatus()
        {
            string c = _editor.Text;
            string lang = HostClient.EsPython(c) ? "Python" : HostClient.EsSql(c) ? "SQL" : "C#";
            if (_statusLang != null) _statusLang.Text = lang;
            foreach (var item in _botonesLenguaje)
            {
                item.Value.Kind = item.Key == lang ? BtnKind.Outline : BtnKind.Ghost;
                item.Value.ForeColor = item.Key == lang ? AppTheme.Primary : AppTheme.TextMuted;
                item.Value.Invalidate();
            }
            if (lang != _ultimoLenguajeEditor)
            {
                _ultimoLenguajeEditor = lang;
                try { EstilizarEditor(_fontSize); } catch { }
                _activarReferencia?.Invoke(lang == "Python" ? 1 : lang == "SQL" ? 2 : 0);
            }
        }

        // =====================================================
        //   Biblioteca de scripts (en SQL: zzBrosScript, por empresa)
        // =====================================================
        // Nodo por script: marca con "★ " los favoritos (Datos.EsFavorito, por terminal —
        // no es una columna de zzBrosScript, es preferencia local de quien usa la Consola).
        private TreeNode NodoScript(string appKey)
        {
            bool fav = Datos.EsFavorito(appKey);
            return new TreeNode((fav ? "★ " : "") + appKey)
            { Tag = "sql:" + appKey, ImageKey = "script", SelectedImageKey = "script" };
        }

        private void CargarArbol()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            string filtro = (_txtBuscar.Text ?? "").Trim().ToLower();
            bool filtrando = filtro != "";

            string emp = "";
            try { emp = _ctx.Empresa(); } catch { }
            bool disponible = false;
            try { disponible = _ctx.BrosScriptsDisponible(); } catch { }

            // Auto-reparación (T2.4, ver comentario de _tablasAseguradas): si la tabla existe
            // pero le faltan columnas nuevas, BrosListar() de abajo tronaría silenciosamente y
            // el árbol saldría vacío sin explicación. Se adelanta aquí, una sola vez por sesión.
            if (disponible && !_tablasAseguradas)
            {
                try { _ctx.BrosAsegurarTablas(); } catch { }
                _tablasAseguradas = true;
            }

            var todos = new List<Dictionary<string, object>>();
            if (disponible) { try { todos = _ctx.BrosListar(); } catch { } }
            var appKeys = todos.Select(r => Convert.ToString(r["AppKey"])).ToList();
            var appKeySet = new HashSet<string>(appKeys, StringComparer.OrdinalIgnoreCase);   // las claves no distinguen mayúsculas

            // ---- ★ Favoritos (por terminal; solo los que sigan existiendo en esta empresa) ----
            var favoritos = Datos.Favoritos().Where(f => appKeySet.Contains(f))
                .Where(f => !filtrando || f.ToLower().Contains(filtro)).ToList();
            if (favoritos.Count > 0)
            {
                var nFav = new TreeNode("★ Favoritos") { ImageKey = "folder", SelectedImageKey = "folder" };
                foreach (var ak in favoritos) nFav.Nodes.Add(NodoScript(ak));
                _tree.Nodes.Add(nFav);
            }

            // ---- 🕐 Recientes (últimos 8 abiertos/ejecutados en ESTE equipo) ----
            if (!filtrando)
            {
                var recientes = Datos.Recientes(8).Where(r => appKeySet.Contains(r)).ToList();
                if (recientes.Count > 0)
                {
                    var nRec = new TreeNode("🕐 Recientes") { ImageKey = "folder", SelectedImageKey = "folder" };
                    foreach (var ak in recientes) nRec.Nodes.Add(NodoScript(ak));
                    _tree.Nodes.Add(nRec);
                }
            }

            // ---- Scripts, agrupados por Categoria (texto libre, el usuario la asigna a mano
            //      con "Categorizar…" -- se probó por módulo de Comercial y no sirvió) ----
            var nScripts = new TreeNode("Scripts — " + (string.IsNullOrEmpty(emp) ? "(sin empresa)" : emp)) { ImageKey = "folder", SelectedImageKey = "folder" };
            if (!disponible)
            {
                nScripts.Nodes.Add(new TreeNode("(sin conexión o empresa no provisionada)") { ForeColor = AppTheme.TextMuted, ImageKey = "muted", SelectedImageKey = "muted" });
            }
            else
            {
                var grupos = new SortedDictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in todos)
                {
                    string ak = Convert.ToString(r["AppKey"]);
                    if (filtrando && !ak.ToLower().Contains(filtro)) continue;

                    string cat = Convert.ToString(r.ContainsKey("Categoria") ? r["Categoria"] : "") ?? "";
                    string grupoNombre = string.IsNullOrWhiteSpace(cat) ? "Sin categoría" : cat.Trim();

                    if (!grupos.TryGetValue(grupoNombre, out TreeNode nGrupo))
                    {
                        nGrupo = new TreeNode(grupoNombre) { ImageKey = "folder", SelectedImageKey = "folder" };
                        grupos[grupoNombre] = nGrupo;
                        nScripts.Nodes.Add(nGrupo);
                    }
                    nGrupo.Nodes.Add(NodoScript(ak));
                }
            }
            _tree.Nodes.Add(nScripts);

            // ---- Plantillas, agrupadas por lenguaje (C#/Python/SQL) -- mismo patrón que
            //      el agrupado por Categoria de "Scripts" de arriba. El Tag de cada hoja
            //      sigue siendo KeyValuePair<string,string> (Nombre, Código) para no tocar
            //      el manejador de doble clic que ya la inserta en el editor.
            var nPlant = new TreeNode("Plantillas") { ImageKey = "folder", SelectedImageKey = "folder" };
            var gruposPlant = new SortedDictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in PLANTILLAS_DEF)
            {
                if (filtrando && !p.Nombre.ToLower().Contains(filtro) && !(p.AppKey ?? "").ToLower().Contains(filtro)) continue;
                if (!gruposPlant.TryGetValue(p.Categoria, out TreeNode nCat))
                {
                    nCat = new TreeNode(p.Categoria) { ImageKey = "folder", SelectedImageKey = "folder" };
                    gruposPlant[p.Categoria] = nCat;
                    nPlant.Nodes.Add(nCat);
                }
                nCat.Nodes.Add(new TreeNode(p.AppKey ?? NormalizarAppKey(p.Nombre)) { Tag = new KeyValuePair<string, string>(p.Nombre, p.Codigo), Name = p.Documentacion ?? "", ToolTipText = p.Nombre + " — se guarda como el script/botón BrosLMV." + (p.AppKey ?? NormalizarAppKey(p.Nombre)), ImageKey = "template", SelectedImageKey = "template" });
            }
            _tree.Nodes.Add(nPlant);

            // Buscando: expandir todo (si no, resultados quedan escondidos en grupos colapsados).
            // Sin buscar: TODO contraído por default, sin excepción -- el usuario decide qué abrir.
            if (filtrando) _tree.ExpandAll();
            _tree.EndUpdate();
        }

        // Menú contextual sobre una plantilla: insertarla o ver su documentación completa.
        private void TemplateMenu(TreeNode nodo, KeyValuePair<string, string> plantilla)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Insertar en el editor", null, (s, e) => InsertarEnEditor(plantilla.Value));
            if (!string.IsNullOrEmpty(nodo.Name))
                menu.Items.Add("Ver documentación", null, (s, e) => MostrarDocumentacionPlantilla(plantilla.Key, nodo.Name));
            menu.Show(_tree, _tree.PointToClient(Cursor.Position));
        }

        // ¿Ya hay un botón BrosLMV.<appKey> en el ribbon de esta empresa?
        private bool ExisteBoton(string appKey)
        {
            try
            {
                return _ctx.Query("SELECT TOP 1 1 AS x FROM engRibbonControl WHERE ControlExecute=" + "N'BrosLMV." + appKey.Replace("'", "''") + "'").Count > 0;
            }
            catch { return false; }
        }

        // Nombre legible a partir de una clave técnica: CREAR_DOC_XML -> «Crear doc xml»; si ya trae minúsculas se respeta (Cotizador).
        private static string NombreLegible(string appKey)
        {
            if (string.IsNullOrEmpty(appKey)) return "";
            string t = appKey.Replace('_', ' ');
            return t == t.ToUpperInvariant() ? char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant() : t;
        }

        // Asistente «Crear botón…» (v2.95.0). appKey vacío = botón nuevo SIN script: al terminar se crea un script mínimo con esa clave y se abre.
        private void CrearBoton(string appKey, bool modoBuscar = false)
        {
            BotonResultado r;
            try { r = CrearBotonForm.Mostrar(_ctx, appKey, NombreLegible(appKey), modoBuscar); }
            catch (Exception ex) { ctxError("No se pudo abrir el asistente de botones: " + ex.Message); return; }
            if (r == null || !r.Publicado) return;
            try { _ctx.erp.RefreshRibbon(); } catch { }
            // edición de un botón existente (sobre todo si no es de BrosLMV): no hay script que crear
            if (r.SoloPropiedades || (r.AppKey ?? "").Contains(".")) { _status.Text = "Botón actualizado: " + r.Caption; return; }
            bool scriptNuevo = false;
            try
            {
                _ctx.BrosAsegurarTablas();
                if (_ctx.Query("SELECT TOP 1 1 AS x FROM zzBrosScript WHERE AppKey=" + "N'" + r.AppKey.Replace("'", "''") + "'").Count == 0)
                {
                    string cap = (r.Caption ?? r.AppKey).Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
                    string codigo =
                        "// lang: csharp\r\n" +
                        "// Botón «" + cap + "»  ·  BrosLMV." + r.AppKey + "\r\n" +
                        "// Escribe aquí lo que debe hacer el botón. Este ejemplo solo cuenta los documentos seleccionados en la lista.\r\n" +
                        "var ids = ctx.GetSelectedIds();\r\n" +
                        "ctx.Msg(\"Documentos seleccionados: \" + ids.Count, \"" + cap + "\");\r\n";
                    _ctx.BrosGuardar(r.AppKey, r.AppKey, codigo, SafeModulo());
                    scriptNuevo = true;
                }
            }
            catch (Exception ex) { ctxError("El botón se creó, pero no se pudo crear su script: " + ex.Message); }
            CargarArbol();
            _status.Text = "Botón BrosLMV." + r.AppKey + " listo" + (scriptNuevo ? " (script nuevo: escribe qué debe hacer)" : "");
            if (scriptNuevo) AbrirScript(r.AppKey);
        }

        // Menú contextual sobre un script (en SQL).
        private void TreeMenu(TreeNode nodo)
        {
            if (nodo == null || !(nodo.Tag is string tag) || !tag.StartsWith("sql:")) return;
            string ak = tag.Substring(4);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir", null, (s, e) => AbrirScript(ak));
            menu.Items.Add(Datos.EsFavorito(ak) ? "★ Quitar de favoritos" : "☆ Marcar como favorito", null,
                (s, e) => { Datos.ToggleFavorito(ak); CargarArbol(); });
            menu.Items.Add(ExisteBoton(ak) ? "Editar botón…" : "Crear botón…", null, (s, e) => CrearBoton(ak));
            menu.Items.Add("Categorizar…", null, (s, e) => Categorizar(ak));
            menu.Items.Add("Historial de versiones…", null, (s, e) => VerHistorialVersiones(ak));
            menu.Items.Add("Exportar paquete (.bros)…", null, (s, e) => ExportarPaquete(ak));
            menu.Items.Add("Eliminar…", null, (s, e) =>
            {
                if (MessageBox.Show("¿Eliminar el script \"" + ak + "\" de esta empresa?", "BrosLMV",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    try { _ctx.BrosBorrar(ak); } catch (Exception ex) { ctxError(ex.Message); }
                    CargarArbol();
                }
            });
            menu.Show(_tree, _tree.PointToClient(Cursor.Position));
        }

        // =====================================================
        //   Historial de VERSIONES (T1.4) — no confundir con VerHistorial() de abajo, que es
        //   el historial de EJECUCIONES (quién corrió qué botón). Este es el código de
        //   versiones anteriores de UN script (zzBrosScriptHist), con diff y restaurar.
        // =====================================================

        // Diff de líneas por LCS (subsecuencia común más larga) clásico, O(n·m). Sin
        // librerías externas -- los scripts de BrosLMV rara vez pasan de unos cientos de
        // líneas. Guardia contra scripts gigantes (evita un dp[n,m] descontrolado).
        private static List<(char Tipo, string Linea)> DiffLineas(string viejo, string nuevo)
        {
            var a = (viejo ?? "").Replace("\r\n", "\n").Split('\n');
            var b = (nuevo ?? "").Replace("\r\n", "\n").Split('\n');
            int n = a.Length, m = b.Length;
            var resultado = new List<(char, string)>();

            if ((long)n * m > 16_000_000)
            {
                resultado.Add(('!', "(script muy grande para diff línea por línea: " + n + " vs " + m + " líneas — restaurar sigue funcionando igual)"));
                return resultado;
            }

            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y]) { resultado.Add((' ', a[x])); x++; y++; }
                else if (dp[x + 1, y] >= dp[x, y + 1]) { resultado.Add(('-', a[x])); x++; }
                else { resultado.Add(('+', b[y])); y++; }
            }
            while (x < n) { resultado.Add(('-', a[x])); x++; }
            while (y < m) { resultado.Add(('+', b[y])); y++; }
            return resultado;
        }

        private static void RenderDiff(RichTextBox rtb, List<(char Tipo, string Linea)> diff)
        {
            rtb.Clear();
            foreach (var d in diff)
            {
                int start = rtb.TextLength;
                string prefijo = d.Tipo == '+' ? "+ " : d.Tipo == '-' ? "- " : d.Tipo == '!' ? "! " : "  ";
                rtb.AppendText(prefijo + d.Linea + "\n");
                rtb.Select(start, rtb.TextLength - start);
                rtb.SelectionColor = d.Tipo == '+' ? Color.FromArgb(0, 120, 0)
                    : d.Tipo == '-' ? Color.FromArgb(170, 0, 0)
                    : d.Tipo == '!' ? AppTheme.TextMuted : AppTheme.TextMain;
                rtb.SelectionBackColor = d.Tipo == '+' ? Color.FromArgb(224, 255, 224)
                    : d.Tipo == '-' ? Color.FromArgb(255, 224, 224) : Color.White;
            }
            rtb.Select(0, 0);
        }

        // "Restaurar" reusa BrosGuardar -- la versión que estaba activa ANTES de restaurar
        // queda respaldada automáticamente (es lo mismo que hace cualquier Guardar), así que
        // restaurar nunca pierde nada: siempre se puede deshacer restaurando otra vez.
        private void VerHistorialVersiones(string appKey)
        {
            List<Dictionary<string, object>> versiones;
            try { versiones = _ctx.BrosHistListar(appKey); }
            catch (Exception ex) { ctxError("No se pudo leer el historial de versiones: " + ex.Message); return; }

            string codigoActual;
            try { codigoActual = _ctx.BrosCargar(appKey) ?? ""; }
            catch (Exception ex) { ctxError("No se pudo cargar el código actual: " + ex.Message); return; }

            var nombresUsuario = new Dictionary<int, string>(); // cache -- 1 consulta por usuario distinto

            var frm = new Form
            {
                Text = "Historial de versiones — " + appKey,
                Size = new Size(1100, 650),
                MinimumSize = new Size(760, 420),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 9f)
            };

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340 };

            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = false };
            lv.Columns.Add("Fecha", 125);
            lv.Columns.Add("Usuario", 110);
            lv.Columns.Add("Etiqueta", 140);
            lv.Columns.Add("Tamaño", 70);

            if (versiones.Count == 0)
            {
                var vacio = new Label { Text = "Sin versiones anteriores todavía — se genera una cada vez que guardas un cambio.",
                    Dock = DockStyle.Top, Height = 50, Padding = new Padding(8), ForeColor = AppTheme.TextMuted };
                split.Panel1.Controls.Add(vacio);
            }
            foreach (var v in versiones)
            {
                int uid = Com.ToInt(v["Usuario"]);
                if (!nombresUsuario.TryGetValue(uid, out string nombreU))
                {
                    nombreU = uid > 0 ? _ctx.NombreUsuario(uid) : "";
                    nombresUsuario[uid] = nombreU;
                }
                var it = new ListViewItem(Convert.ToString(v["Fecha"]));
                it.SubItems.Add(string.IsNullOrEmpty(nombreU) ? (uid > 0 ? uid.ToString() : "?") : nombreU);
                it.SubItems.Add(Convert.ToString(v["Etiqueta"] ?? ""));
                it.SubItems.Add(Convert.ToString(v["Tamano"]) + " car.");
                it.Tag = Convert.ToInt32(v["id"]);
                lv.Items.Add(it);
            }
            split.Panel1.Controls.Add(lv);

            var pnlDerecha = new Panel { Dock = DockStyle.Fill };
            var rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), WordWrap = false, BorderStyle = BorderStyle.FixedSingle };
            // DiffLineas(viejo, nuevo): '-' = línea SOLO en la versión vieja (roja) = volvería si
            // restauras; '+' = línea SOLO en la versión de HOY (verde) = se perdería si restauras.
            var lblAyuda = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 4, 6, 0), ForeColor = AppTheme.TextMuted,
                Text = "Diff contra el código de HOY — rojo = de la versión vieja (volvería si restauras), verde = de hoy (se perdería si restauras)." };
            var pnlBotones = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6) };
            var btnRestaurar = new IconButton { Text = "Restaurar esta versión", Kind = BtnKind.Primary, Accent = AppTheme.Primary, AutoSize = true, Height = 34, Padding = new Padding(10, 0, 10, 0) };
            var btnExportar = new IconButton { Text = "Exportar (.bros)…", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, AutoSize = true, Height = 34, Padding = new Padding(10, 0, 10, 0) };
            var btnEtiquetar = new IconButton { Text = "Etiquetar…", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, AutoSize = true, Height = 34, Padding = new Padding(10, 0, 10, 0) };
            var btnPurgar = new IconButton { Text = "Purgar versiones viejas…", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, AutoSize = true, Height = 34, Padding = new Padding(10, 0, 10, 0) };
            pnlBotones.Controls.AddRange(new Control[] { btnRestaurar, btnExportar, btnEtiquetar, btnPurgar });
            pnlDerecha.Controls.Add(rtb);
            pnlDerecha.Controls.Add(lblAyuda);
            pnlDerecha.Controls.Add(pnlBotones);
            split.Panel2.Controls.Add(pnlDerecha);
            frm.Controls.Add(split);

            Dictionary<string, object> Seleccionada()
            {
                if (lv.SelectedItems.Count == 0) return null;
                int id = (int)lv.SelectedItems[0].Tag;
                return versiones.FirstOrDefault(v => Convert.ToInt32(v["id"]) == id);
            }

            lv.SelectedIndexChanged += (s, e) =>
            {
                var sel = Seleccionada();
                if (sel == null) { rtb.Clear(); return; }
                int id = Convert.ToInt32(sel["id"]);
                string codigoViejo;
                try { codigoViejo = _ctx.BrosHistLeer(id, appKey) ?? ""; }
                catch (Exception ex) { rtb.Clear(); rtb.Text = "No se pudo leer esta versión: " + ex.Message; return; }
                RenderDiff(rtb, DiffLineas(codigoViejo, codigoActual));
            };

            btnRestaurar.Click += (s, e) =>
            {
                var sel = Seleccionada();
                if (sel == null) { MessageBox.Show(frm, "Selecciona una versión primero.", "BrosLMV"); return; }
                if (MessageBox.Show(frm,
                    "¿Restaurar esta versión de \"" + appKey + "\"?\n\nLa versión que tienes ahora mismo queda respaldada " +
                    "automáticamente en el historial -- no se pierde nada, y se puede deshacer restaurando de nuevo.",
                    "BrosLMV — Restaurar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    int id = Convert.ToInt32(sel["id"]);
                    string codigoViejo = _ctx.BrosHistLeer(id, appKey);
                    if (codigoViejo == null) { ctxError("Esa versión ya no está disponible."); return; }
                    string nombreActual = appKey; int moduloActual = 0;
                    try { var info = _ctx.BrosObtenerParaExportar(appKey); if (info != null) { nombreActual = info.Nombre; moduloActual = info.Modulo; } } catch { }
                    _ctx.BrosAsegurarTablas();
                    _ctx.BrosGuardar(appKey, nombreActual, codigoViejo, moduloActual);
                    MessageBox.Show(frm, "Restaurado.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    frm.Close();
                    if (_appKey == appKey) AbrirScript(appKey);
                    CargarArbol();
                }
                catch (Exception ex) { MessageBox.Show(frm, "No se pudo restaurar: " + ex.Message, "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };

            btnExportar.Click += (s, e) =>
            {
                var sel = Seleccionada();
                if (sel == null) { MessageBox.Show(frm, "Selecciona una versión primero.", "BrosLMV"); return; }
                int id = Convert.ToInt32(sel["id"]);
                string codigoViejo;
                try { codigoViejo = _ctx.BrosHistLeer(id, appKey); } catch (Exception ex) { ctxError(ex.Message); return; }
                if (codigoViejo == null) { ctxError("Esa versión ya no está disponible."); return; }
                string nombreActual = appKey; int moduloActual = 0; string categoriaActual = "";
                try { var info = _ctx.BrosObtenerParaExportar(appKey); if (info != null) { nombreActual = info.Nombre; moduloActual = info.Modulo; categoriaActual = info.Categoria; } } catch { }
                ExportarPaqueteConCodigo(appKey, codigoViejo, nombreActual, moduloActual, categoriaActual);
            };

            btnEtiquetar.Click += (s, e) =>
            {
                var sel = Seleccionada();
                if (sel == null) { MessageBox.Show(frm, "Selecciona una versión primero.", "BrosLMV"); return; }
                int id = Convert.ToInt32(sel["id"]);
                string actual = Convert.ToString(sel["Etiqueta"] ?? "");
                string etiqueta = PedirTexto("Etiqueta para esta versión (vacío = quitar):", actual);
                if (etiqueta == null) return;
                try { _ctx.BrosHistEtiquetar(id, etiqueta.Trim()); }
                catch (Exception ex) { ctxError("No se pudo etiquetar: " + ex.Message); return; }
                frm.Close();
                VerHistorialVersiones(appKey);
            };

            btnPurgar.Click += (s, e) =>
            {
                string diasTxt = PedirTexto(
                    "Borrar versiones de \"" + appKey + "\" con más de cuántos días de antigüedad?\n" +
                    "(Las versiones ETIQUETADAS nunca se borran, sin importar la antigüedad.)", "90");
                if (diasTxt == null) return;
                if (!int.TryParse(diasTxt.Trim(), out int dias) || dias < 1)
                { MessageBox.Show(frm, "Escribe un número de días válido (entero, mayor a 0).", "BrosLMV"); return; }
                try
                {
                    int n = _ctx.BrosHistPurgar(appKey, dias);
                    MessageBox.Show(frm, n + " versión(es) sin etiquetar, de más de " + dias + " día(s), borradas.",
                        "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    frm.Close();
                    VerHistorialVersiones(appKey);
                }
                catch (Exception ex) { ctxError("No se pudo purgar: " + ex.Message); }
            };

            if (versiones.Count > 0) lv.Items[0].Selected = true;
            frm.ShowDialog(this);
        }

        private void VerHistorial()
        {
            var frm = new Form { Text = "Historial / Auditoría de ejecuciones", Size = new Size(980, 560), MinimumSize = new Size(700, 400), StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9f) };
            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ---- Pestaña 1: este equipo (SQLite local, siempre disponible) ----
            var tabLocal = new TabPage("Este equipo");
            var lvLocal = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvLocal.Columns.Add("Fecha", 130);
            lvLocal.Columns.Add("Empresa", 150);
            lvLocal.Columns.Add("Mód.", 45);
            lvLocal.Columns.Add("Usr", 40);
            lvLocal.Columns.Add("Script", 150);
            lvLocal.Columns.Add("Origen", 60);
            lvLocal.Columns.Add("ms", 55);
            lvLocal.Columns.Add("Filas", 50);
            lvLocal.Columns.Add("Estado", 60);
            lvLocal.Columns.Add("Error", 150);
            foreach (var r in Datos.UltimasEjecuciones(200))
            {
                var it = new ListViewItem(Convert.ToString(r["fecha"]));
                it.SubItems.Add(Convert.ToString(r["empresa"]));
                it.SubItems.Add(Convert.ToString(r["modulo"]));
                it.SubItems.Add(Convert.ToString(r["usuario"]));
                it.SubItems.Add(Convert.ToString(r["script"]));
                it.SubItems.Add(Convert.ToString(r["origen"]));
                it.SubItems.Add(Convert.ToString(r["duracion_ms"]));
                it.SubItems.Add(Convert.ToString(r["filas"]));
                it.SubItems.Add(Convert.ToString(r["estado"]));
                it.SubItems.Add(Convert.ToString(r["error"]));
                if (Convert.ToString(r["estado"]) == "ERROR") it.ForeColor = Color.Firebrick;
                lvLocal.Items.Add(it);
            }
            tabLocal.Controls.Add(lvLocal);
            tabs.TabPages.Add(tabLocal);

            // ---- Pestaña 2: Auditoría (empresa) — zzBrosAuditoria, T2.1 ----
            var tabCentral = new TabPage("Auditoría (empresa)");
            var pnlFiltros = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0) };
            var dtDesde = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100, Value = DateTime.Today.AddDays(-7) };
            var dtHasta = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100, Value = DateTime.Today };
            var txtAppKey = new TextBox { Width = 140 };
            var cbEstado = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cbEstado.Items.AddRange(new object[] { "(todos)", "OK", "ERROR", "ADVERTENCIA" });
            cbEstado.SelectedIndex = 0;
            var btnFiltrar = new IconButton { Text = "Filtrar", Kind = BtnKind.Primary, Accent = AppTheme.Primary, AutoSize = true, Height = 28, Padding = new Padding(10, 0, 10, 0) };
            pnlFiltros.Controls.AddRange(new Control[] {
                new Label { Text = "Desde:", AutoSize = true, Margin = new Padding(0,6,2,0) }, dtDesde,
                new Label { Text = "Hasta:", AutoSize = true, Margin = new Padding(6,6,2,0) }, dtHasta,
                new Label { Text = "AppKey:", AutoSize = true, Margin = new Padding(6,6,2,0) }, txtAppKey,
                new Label { Text = "Estado:", AutoSize = true, Margin = new Padding(6,6,2,0) }, cbEstado,
                btnFiltrar });

            var lvCentral = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvCentral.Columns.Add("Fecha", 130);
            lvCentral.Columns.Add("Usuario", 100);
            lvCentral.Columns.Add("Equipo", 110);
            lvCentral.Columns.Add("Mód.", 45);
            lvCentral.Columns.Add("AppKey", 130);
            lvCentral.Columns.Add("Origen", 80);
            lvCentral.Columns.Add("ms", 55);
            lvCentral.Columns.Add("Filas", 50);
            lvCentral.Columns.Add("Estado", 70);
            lvCentral.Columns.Add("Error", 220);

            var lblSinDatos = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8), ForeColor = AppTheme.TextMuted, Visible = false,
                Text = "Sin filas -- puede ser que la empresa no tenga zzBrosAuditoria (provisionada antes de v2.36.0), " +
                       "no haya permiso de lectura, o simplemente no haya ejecuciones en el rango filtrado." };

            void Recargar()
            {
                lvCentral.Items.Clear();
                int usuarioFiltro = 0; // sin filtro de usuario por ahora -- AppKey/Estado/fecha cubren el caso comun
                string estadoFiltro = cbEstado.SelectedIndex <= 0 ? "" : cbEstado.Text;
                List<Dictionary<string, object>> filas;
                try { filas = _ctx.BrosAuditoriaListar(dtDesde.Value.Date, dtHasta.Value.Date, usuarioFiltro, txtAppKey.Text, estadoFiltro); }
                catch (Exception ex) { ctxError("No se pudo leer la auditoría: " + ex.Message); return; }

                var nombresUsuario = new Dictionary<int, string>();
                foreach (var r in filas)
                {
                    int uid = Com.ToInt(r["Usuario"]);
                    if (!nombresUsuario.TryGetValue(uid, out string nombreU))
                    {
                        nombreU = uid > 0 ? _ctx.NombreUsuario(uid) : "";
                        nombresUsuario[uid] = nombreU;
                    }
                    var it = new ListViewItem(Convert.ToString(r["Fecha"]));
                    it.SubItems.Add(string.IsNullOrEmpty(nombreU) ? (uid > 0 ? uid.ToString() : "?") : nombreU);
                    it.SubItems.Add(Convert.ToString(r["Equipo"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["Modulo"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["AppKey"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["Origen"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["DuracionMs"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["Filas"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["Estado"] ?? ""));
                    it.SubItems.Add(Convert.ToString(r["Error"] ?? ""));
                    if (Convert.ToString(r["Estado"]) == "ERROR") it.ForeColor = Color.Firebrick;
                    else if (Convert.ToString(r["Estado"]) == "ADVERTENCIA") it.ForeColor = Color.DarkOrange;
                    lvCentral.Items.Add(it);
                }
                lblSinDatos.Visible = filas.Count == 0;
            }

            btnFiltrar.Click += (s, e) => Recargar();
            tabCentral.Controls.Add(pnlFiltros);
            tabCentral.Controls.Add(lblSinDatos);
            tabCentral.Controls.Add(lvCentral);
            tabs.TabPages.Add(tabCentral);

            frm.Controls.Add(tabs);
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedTab == tabCentral && lvCentral.Items.Count == 0 && !lblSinDatos.Visible) Recargar(); };
            Recargar(); // primera carga con el rango default (últimos 7 días)
            frm.ShowDialog(this);
        }

        // Clic derecho sobre una referencia: «Ver ficha» abre el manual del SDK justo en esa función.
        private void AgregarMenuFicha(ListView lv)
        {
            lv.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var it = lv.GetItemAt(e.X, e.Y);
                if (it == null || !(it.Tag is MetodoCtx m)) return;
                lv.SelectedItems.Clear(); it.Selected = true;
                var menu = new ContextMenuStrip { Font = AppTheme.FontMain };
                menu.Items.Add("Ver ficha de «" + m.Nombre + "»", null, (s2, e2) => MostrarManualSdk(m.Id));
                menu.Items.Add("Insertar ejemplo en el editor", null, (s2, e2) => InsertarEnEditor(m.Ejemplo + "\r\n"));
                menu.Show(lv, e.Location);
            };
        }

        // Manual del SDK (HTML incrustado): buscador, guías y fichas por función. Con id abre directo en esa ficha.
        private void MostrarManualSdk(string id = null)
        {
            string html = SdkCatalogo.ManualHtml();
            if (html == null) { MessageBox.Show(this, "No se encontró el manual del SDK dentro de BrosLMV.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!string.IsNullOrEmpty(id))
                html = html.Replace("</body>", "<script>window.addEventListener('load',function(){var e=document.getElementById('" + SdkCatalogo.Slug(id) +
                    "');if(e){e.scrollIntoView();e.style.outline='2px solid #1f5fd6';}});</script></body>");
            try { _ctx.ShowHtml(html, "Manual del SDK de BrosLMV", 1200, 820, false); }
            catch (Exception ex) { ctxError("No se pudo abrir el manual del SDK: " + ex.Message); }
        }

        private void CargarMetodos()
        {
            // C#: agrupado por categoría (son ~75 métodos; los grupos lo hacen navegable).
            _lstMetodosCSharp.Items.Clear();
            _lstMetodosCSharp.Groups.Clear();
            _lstMetodosCSharp.ShowGroups = true;
            var grupos = new Dictionary<string, ListViewGroup>();
            foreach (var m in METODOS)
            {
                if (!CoincideFiltroSdk(m)) continue;
                string cat = string.IsNullOrEmpty(m.Cat) ? "General" : m.Cat;
                if (!grupos.TryGetValue(cat, out var g))
                {
                    g = new ListViewGroup(cat) { HeaderAlignment = HorizontalAlignment.Left };
                    grupos[cat] = g;
                    _lstMetodosCSharp.Groups.Add(g);
                }
                var it = new ListViewItem(m.Nombre, g) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosCSharp.Items.Add(it);
            }
            _lstMetodosCSharp.ShowItemToolTips = true;

            _lstMetodosPython.Items.Clear();
            foreach (var m in METODOS_PYTHON)
            {
                if (!CoincideFiltroSdk(m)) continue;
                var it = new ListViewItem(m.Nombre) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosPython.Items.Add(it);
            }
            _lstMetodosPython.ShowItemToolTips = true;

            _lstMetodosSql.Items.Clear();
            foreach (var m in METODOS_SQL)
            {
                if (!CoincideFiltroSdk(m)) continue;
                var it = new ListViewItem(m.Nombre) { Tag = m, ToolTipText = m.Firma + "\n" + m.Desc };
                it.SubItems.Add(m.Desc);
                _lstMetodosSql.Items.Add(it);
            }
            _lstMetodosSql.ShowItemToolTips = true;

            Alternar(_lstMetodosPython); Alternar(_lstMetodosSql);   // C# usa grupos, sin zebra
        }

        private bool CoincideFiltroSdk(MetodoCtx metodo)
        {
            string filtro = _txtSdk?.Text.Trim() ?? "";
            return (metodo.Nombre + " " + metodo.Desc + " " + metodo.Cat)
                .IndexOf(filtro, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ActualizarContexto()
        {
            string emp = "-", mod = "-", sel = "-", pk = "DocumentID", source = "-", ownerInfo = "-";
            try { emp = _ctx.Empresa(); } catch { }
            try { mod = _ctx.ModuloActivo().ToString(); } catch { }
            
            try 
            { 
                var ids = _ctx.GetSelectedIds(); 
                sel = ids.Count + (ids.Count > 0 ? "  (" + string.Join(",", ids.Take(8)) + (ids.Count > 8 ? "…" : "") + ")" : ""); 
            } catch { }

            try
            {
                pk = GridSelection.LlaveDeModulo(_ctx.XEngineLib, null);
                
                object jg = Com.GetProp(_ctx.XEngineLib, "janusGrid");
                if (jg != null)
                {
                    object rs = Com.GetProp(jg, "ADORecordset");
                    if (rs != null)
                    {
                        object s = Com.GetProp(rs, "Source");
                        if (s != null) source = s.ToString();
                    }
                }

                int ownerId = _ctx.erp.OwnedBusinessEntityId;
                ownerInfo = ownerId.ToString();
                if (ownerId > 0)
                {
                    var res = _ctx.Query("SELECT NombreOrganizacion FROM orgOrganizacion WHERE OrganizacionID = " + ownerId);
                    if (res.Count > 0 && res[0].ContainsKey("NombreOrganizacion"))
                        ownerInfo = ownerId + " - " + Convert.ToString(res[0]["NombreOrganizacion"]);
                }
            } catch { }

            // Usuario activo: el ID del constructor suele venir en 0; preferir el de XEngine + nombre.
            string usuario = _ctx.UserID.ToString();
            try
            {
                int uid = _ctx.erp.UserId;
                string uname = _ctx.erp.UserName;
                if (uid <= 0) uid = _ctx.UserID;
                usuario = uid + (string.IsNullOrEmpty(uname) ? "" : " - " + uname);
            }
            catch { }

            // Rellenar la lista de contexto (con tooltip al valor completo).
            SetCtx("Empresa", emp);
            SetCtx("Usuario", usuario);
            SetCtx("Módulo", mod + "   (PK: " + pk + ")");
            SetCtx("Owner", ownerInfo);
            SetCtx("Vista", NombreVista(source));   // solo el nombre de la vista/tabla, no el SELECT completo
            SetCtx("Selección", sel);
            Alternar(_lstCtx);
            if (_lblCtx != null) _lblCtx.Text = "Actualizado " + DateTime.Now.ToString("HH:mm:ss");

            // Llenar pestaña de selección: primero los 5 tokens fijos (T3.1 fase 1), luego
            // los campos dinámicos de la fila activa (ya existía).
            _lstSeleccion.Items.Clear();
            foreach (var tok in TOKENS_FIJOS)
            {
                var it = new ListViewItem(tok.Token);
                it.SubItems.Add(tok.Desc);
                it.Tag = tok;
                it.Font = new Font(_lstSeleccion.Font, FontStyle.Bold);
                _lstSeleccion.Items.Add(it);
            }
            var fila = _ctx.GetFilaActiva();
            if (fila != null)
            {
                foreach (var kvp in fila)
                {
                    var it = new ListViewItem(kvp.Key);
                    it.SubItems.Add(Convert.ToString(kvp.Value));
                    _lstSeleccion.Items.Add(it);
                }
            }
            Alternar(_lstSeleccion);
        }

        // Extrae el nombre de la vista/tabla que se está consultando (lo que sigue al primer FROM),
        // en vez de mostrar el SELECT completo. Quita corchetes y esquema (dbo.).
        private static string NombreVista(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source == "-") return source;
            var m = Regex.Match(source, @"\bFROM\s+\[?(?<n>[A-Za-z0-9_\.\]\[]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return source.Trim();
            string n = m.Groups["n"].Value.Replace("[", "").Replace("]", "");
            int dot = n.LastIndexOf('.');
            if (dot >= 0 && dot < n.Length - 1) n = n.Substring(dot + 1);  // quitar esquema dbo.
            return n;
        }

        // Asigna el valor de una fila de la lista de contexto (con tooltip del valor completo).
        private void SetCtx(string clave, string valor)
        {
            if (_lstCtx == null) return;
            valor = string.IsNullOrEmpty(valor) ? "—" : valor;
            foreach (ListViewItem it in _lstCtx.Items)
                if (it.Text == clave) { it.SubItems[1].Text = valor; it.ToolTipText = clave + ": " + valor; break; }
        }

        // Filas alternadas muy sutiles en una lista de referencias.
        private void Alternar(ListView lv)
        {
            for (int i = 0; i < lv.Items.Count; i++)
                lv.Items[i].BackColor = (i % 2 == 0) ? ConsolaSuperficie : ConsolaChrome;
        }

        // =====================================================
        //   Acciones de script (en SQL, por empresa)
        // =====================================================


        // Abre un script de la empresa activa (desde zzBrosScript).


        // Importar desde archivo (.ctx/.csx): carga el contenido al editor; con Guardar
        // queda registrado en la empresa. Sirve para migrar scripts viejos a SQL.
        private void Abrir()
        {
            using (var dlg = new OpenFileDialog { InitialDirectory = Rutas.Scripts, Filter = "Scripts BrosLMV (*.ctx;*.csx;*.py;*.sql)|*.ctx;*.csx;*.py;*.sql|Todos|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        _editor.Text = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                        _appKey = Path.GetFileNameWithoutExtension(dlg.FileName);
                        Text = "BrosLMV — (importado) " + _appKey;
                        _status.Text = "Importado de archivo: usa Guardar para registrarlo en la empresa";
                    }
                    catch (Exception ex) { ctxError("No se pudo abrir: " + ex.Message); }
                }
        }

        // Integridad de scripts (T2.3): marca el botón actual como aprobado (AprobadoPor/
        // AprobadoEl). Necesario para que corra desde el ribbon si el usuario que lo ejecuta
        // tiene la preferencia "ExigirAprobacion" activa (zzBrosPref).
        private void Aprobar()
        {
            if (string.IsNullOrEmpty(_appKey))
            {
                ctxError("Guarda el script primero (Aprobar aplica a botones ya guardados en la empresa).");
                return;
            }
            if (MessageBox.Show("¿Aprobar \"" + _appKey + "\" tal como está guardado ahora mismo?",
                    "BrosLMV — Aprobar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _ctx.BrosAprobar(_appKey);
                _status.Text = "Aprobado: " + _appKey;
            }
            catch (Exception ex) { ctxError("No se pudo aprobar: " + ex.Message); }
        }

        // Categoría (texto libre que el usuario escribe): se probó agrupar por módulo de
        // Comercial y no sirvió -- el usuario prefiere clasificar a mano. No toca Codigo ni
        // HashSHA256 (T2.3), solo metadato.
        private void Categorizar(string appKey)
        {
            string actual = "";
            try { actual = Convert.ToString(_ctx.Query("SELECT Categoria FROM zzBrosScript WHERE AppKey=" + "N'" + appKey.Replace("'", "''") + "'")
                .FirstOrDefault()?["Categoria"] ?? ""); } catch { }
            string nueva = PedirCategoria(appKey, actual);
            if (nueva == null) return; // canceló
            try
            {
                _ctx.BrosAsegurarTablas();
                _ctx.BrosCategorizar(appKey, nueva.Trim());
                _status.Text = "Categoría de " + appKey + ": " + (string.IsNullOrEmpty(nueva.Trim()) ? "(ninguna)" : nueva.Trim());
                CargarArbol();
            }
            catch (Exception ex) { ctxError("No se pudo categorizar: " + ex.Message); }
        }

        // =====================================================
        //   Paquetes .bros (T1.3): mover un botón entre empresas/equipos
        // =====================================================
        // Formato: ZIP con codigo.txt (el script) + paquete.json (metadatos) + assets\ (si el
        // AppKey tiene carpeta <AppKey>_assets\ en la empresa activa). Se mueve el AppKey tal
        // cual -- si en la empresa destino ya existe uno con el mismo nombre, se pide confirmar
        // antes de sobrescribir (queda respaldado en zzBrosScriptHist como cualquier Guardar).
        private void ExportarPaquete(string appKey)
        {
            var info = _ctx.BrosObtenerParaExportar(appKey);
            if (info == null) { ctxError("No se encontró el script: " + appKey); return; }
            ExportarPaqueteConCodigo(appKey, info.Codigo, info.Nombre, info.Modulo, info.Categoria);
        }

        // Compartida entre "Exportar paquete (.bros)…" (código ACTUAL) y "Exportar esta
        // versión (.bros)…" del historial de versiones (código de una fila vieja de
        // zzBrosScriptHist) -- el paquete no distingue de dónde salió el código.
        private void ExportarPaqueteConCodigo(string appKey, string codigo, string nombre, int modulo, string categoria)
        {
            try
            {
                using (var dlg = new SaveFileDialog { FileName = appKey + ".bros", Filter = "Paquete BrosLMV (*.bros)|*.bros" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    int nArchivos = EscribirPaquete(dlg.FileName, appKey, codigo, nombre, modulo, categoria);
                    _status.Text = "Exportado: " + Path.GetFileName(dlg.FileName) + " (" + nArchivos + " archivo(s) de assets)";
                }
            }
            catch (Exception ex) { ctxError("No se pudo exportar el paquete: " + ex.Message); }
        }

        // Escribe un paquete .bros (zip con codigo.txt, paquete.json y la carpeta <clave>_assets de la empresa). Devuelve cuántos archivos de assets incluyó.
        private int EscribirPaquete(string destino, string appKey, string codigo, string nombre, int modulo, string categoria)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "broslmv_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.WriteAllText(Path.Combine(tmp, "codigo.txt"), codigo, Encoding.UTF8);

                string empresa = SafeEmpresa();
                string carpetaAssets = Path.Combine(Rutas.ScriptsDe(empresa), appKey + "_assets");
                int nArchivos = 0;
                if (Directory.Exists(carpetaAssets))
                {
                    string destAssets = Path.Combine(tmp, "assets");
                    foreach (var f in Directory.GetFiles(carpetaAssets, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(carpetaAssets.Length).TrimStart('\\', '/');
                        string destFile = Path.Combine(destAssets, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                        File.Copy(f, destFile, true);
                        nArchivos++;
                    }
                }

                string manifest = "{\r\n" +
                    "  \"appKey\": " + Paquetes.JsonStr(appKey) + ",\r\n" +
                    "  \"nombre\": " + Paquetes.JsonStr(nombre) + ",\r\n" +
                    "  \"modulo\": " + modulo + ",\r\n" +
                    "  \"categoria\": " + Paquetes.JsonStr(categoria) + ",\r\n" +
                    "  \"versionMinima\": " + Paquetes.JsonStr(Com.Version) + ",\r\n" +
                    "  \"exportadoDe\": " + Paquetes.JsonStr(empresa) + ",\r\n" +
                    "  \"exportadoEl\": " + Paquetes.JsonStr(DateTime.Now.ToString("yyyy-MM-dd HH:mm")) + "\r\n" +
                    "}\r\n";
                File.WriteAllText(Path.Combine(tmp, "paquete.json"), manifest, Encoding.UTF8);

                if (File.Exists(destino)) File.Delete(destino);
                ZipFile.CreateFromDirectory(tmp, destino, CompressionLevel.Optimal, false);
                return nArchivos;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // «Respaldar todos los scripts…»: un .bros por script de la empresa activa en la carpeta que se elija. Sirve de respaldo y para llevarlos a otra empresa/equipo.
        private void RespaldarTodos()
        {
            try
            {
                var lista = _ctx.BrosListar();
                if (lista.Count == 0) { MessageBox.Show(this, "Esta empresa no tiene scripts guardados.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                using (var dlg = new FolderBrowserDialog { Description = "Carpeta donde guardar el respaldo (un archivo .bros por script)" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    string carpeta = Path.Combine(dlg.SelectedPath, "Respaldo_scripts_" + SafeEmpresa() + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
                    Directory.CreateDirectory(carpeta);
                    int ok = 0; var fallos = new List<string>();
                    foreach (var r in lista)
                    {
                        string ak = Convert.ToString(r["AppKey"]);
                        try
                        {
                            var info = _ctx.BrosObtenerParaExportar(ak);
                            if (info == null) { fallos.Add(ak); continue; }
                            EscribirPaquete(Path.Combine(carpeta, ak + ".bros"), ak, info.Codigo, info.Nombre, info.Modulo, info.Categoria);
                            ok++;
                        }
                        catch { fallos.Add(ak); }
                    }
                    _status.Text = "Respaldo: " + ok + " script(s) en " + carpeta;
                    MessageBox.Show(this, "Se respaldaron " + ok + " de " + lista.Count + " script(s).\n\nCarpeta:\n" + carpeta + (fallos.Count > 0 ? "\n\nNo se pudieron respaldar: " + string.Join(", ", fallos) : ""),
                        "BrosLMV", MessageBoxButtons.OK, fallos.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { ctxError("No se pudo respaldar: " + ex.Message); }
        }

        // Importa un .bros a la empresa ACTIVA (la que tenga abierta Comercial en este momento
        // -- igual que Guardar). Sobrescribir pide confirmación; el historial (zzBrosScriptHist)
        // conserva lo que había antes, así que es reversible.
        private void ImportarPaquete()
        {
            using (var dlg = new OpenFileDialog { Filter = "Paquete BrosLMV (*.bros)|*.bros|Todos|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string tmp = Path.Combine(Path.GetTempPath(), "broslmv_pkg_" + Guid.NewGuid().ToString("N"));
                try
                {
                    ZipFile.ExtractToDirectory(dlg.FileName, tmp);

                    string manifestPath = Path.Combine(tmp, "paquete.json");
                    string codigoPath = Path.Combine(tmp, "codigo.txt");
                    if (!File.Exists(manifestPath) || !File.Exists(codigoPath))
                    {
                        ctxError("El archivo no parece un paquete BrosLMV válido (falta paquete.json o codigo.txt).");
                        return;
                    }
                    string manifest = File.ReadAllText(manifestPath, Encoding.UTF8);
                    string appKey = Paquetes.ManifiestoTexto(manifest, "appKey");
                    string nombre = Paquetes.ManifiestoTexto(manifest, "nombre");
                    int modulo = Paquetes.ManifiestoNumero(manifest, "modulo");
                    string categoria = Paquetes.ManifiestoTexto(manifest, "categoria");
                    string versionMinima = Paquetes.ManifiestoTexto(manifest, "versionMinima");

                    if (string.IsNullOrEmpty(appKey)) { ctxError("El paquete no trae AppKey en su manifiesto (paquete.json)."); return; }

                    string versionActual = _ctx.VersionProvisionada();
                    if (!string.IsNullOrEmpty(versionMinima) && !string.IsNullOrEmpty(versionActual)
                        && Paquetes.CompararVersiones(versionActual, versionMinima) < 0
                        && MessageBox.Show(
                            "Este paquete se exportó con BrosLMV " + versionMinima + "; esta empresa está " +
                            "provisionada en " + versionActual + " (más vieja). Puede que falten columnas o " +
                            "funciones que el script necesita.\n\n¿Importar de todas formas?",
                            "BrosLMV — Versión distinta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    bool yaExiste = _ctx.BrosCargar(appKey) != null;
                    if (yaExiste && MessageBox.Show(
                        "Ya existe un script \"" + appKey + "\" en esta empresa. ¿Sobrescribirlo? " +
                        "(la versión actual queda respaldada en el historial, zzBrosScriptHist)",
                        "BrosLMV — Ya existe", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    string codigo = File.ReadAllText(codigoPath, Encoding.UTF8);
                    _ctx.BrosAsegurarTablas();
                    _ctx.BrosGuardar(appKey, string.IsNullOrEmpty(nombre) ? appKey : nombre, codigo, modulo);
                    if (!string.IsNullOrEmpty(categoria)) _ctx.BrosCategorizar(appKey, categoria);

                    string empresa = SafeEmpresa();
                    string assetsOrigen = Path.Combine(tmp, "assets");
                    int nArchivos = 0;
                    if (Directory.Exists(assetsOrigen))
                    {
                        string assetsDestino = Path.Combine(Rutas.ScriptsDe(empresa), appKey + "_assets");
                        foreach (var f in Directory.GetFiles(assetsOrigen, "*", SearchOption.AllDirectories))
                        {
                            string rel = f.Substring(assetsOrigen.Length).TrimStart('\\', '/');
                            string destFile = Path.Combine(assetsDestino, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                            File.Copy(f, destFile, true);
                            nArchivos++;
                        }
                    }

                    _appKey = appKey;
                    _editor.Text = codigo;
                    Text = "BrosLMV — " + appKey;
                    _status.Text = "Importado: " + appKey + " (" + nArchivos + " archivo(s) de assets)";
                    CargarArbol();

                    if (MessageBox.Show(
                        "\"" + appKey + "\" se guardó en la empresa activa (\"" + empresa + "\"), pero " +
                        "todavía NO tiene botón en el ribbon.\n\n¿Copiar al portapapeles el SQL para crear el botón?",
                        "BrosLMV — Crear botón", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        Clipboard.SetText(SqlCrearBoton(appKey, string.IsNullOrEmpty(nombre) ? appKey : nombre));
                        MessageBox.Show(
                            "SQL copiado al portapapeles. Ajusta @Caption/@Orden si hace falta y córrelo " +
                            "contra la base de datos de esta empresa (p. ej. desde SSMS o sqlcmd).",
                            "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex) { ctxError("No se pudo importar el paquete: " + ex.Message); }
                finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
            }
        }

        // Mismo patrón que instalador\sql\plantilla_crear_boton.sql, con @Caption/@Execute ya
        // rellenados -- se copia al portapapeles en vez de ejecutarse solo (crear botones en el
        // ribbon toca engRibbonControl/engRibbonMenu, tablas nativas de Comercial: mejor que el
        // usuario lo revise/corra a propósito que hacerlo automático desde la Consola).
        private static string SqlCrearBoton(string appKey, string caption)
        {
            return
                "-- Generado por BrosLMV Consola (Importar paquete) -- revisa @Caption/@Orden antes de correr.\r\n" +
                "SET NOCOUNT ON;\r\n" +
                "DECLARE @Caption nvarchar(200) = " + SqlLit(caption) + ";\r\n" +
                "DECLARE @Execute nvarchar(200) = " + SqlLit("BrosLMV." + appKey) + ";\r\n" +
                "DECLARE @RibbonGroupID int = NULL;\r\n" +
                "DECLARE @Orden int = 100;\r\n" +
                "IF @RibbonGroupID IS NULL BEGIN\r\n" +
                "    SELECT TOP 1 @RibbonGroupID = m.RibbonGroupID FROM engRibbonMenu m\r\n" +
                "    INNER JOIN engRibbonControl c ON c.ControlID = m.ControlID\r\n" +
                "    WHERE c.ControlExecute LIKE 'BrosLMV.%' ORDER BY m.RibbonGroupID;\r\n" +
                "    IF @RibbonGroupID IS NULL\r\n" +
                "        SELECT TOP 1 @RibbonGroupID = RibbonGroupID FROM engRibbonMenu GROUP BY RibbonGroupID ORDER BY COUNT(*) DESC;\r\n" +
                "END\r\n" +
                "IF EXISTS (SELECT 1 FROM engRibbonControl WHERE ControlExecute = @Execute) BEGIN\r\n" +
                "    PRINT 'El boton ' + @Execute + ' ya existe.'; RETURN;\r\n" +
                "END\r\n" +
                "INSERT INTO engRibbonControl\r\n" +
                "    (ControlIDBase, ProductID, ModuleID, ControlCaption, ControlDescription, ControlExecute,\r\n" +
                "     IconFile, SystemButton, SystemButtonOrder, SystemButtonBeginGroup, SystemButtonParentID,\r\n" +
                "     QuickAccessShow, QuickAccessSection, QuickAccessCaption, QuickAccessOrder, Shortcut,\r\n" +
                "     ResID, ResIDDescription, Comments, AFP)\r\n" +
                "VALUES (0, 1, 0, @Caption, @Caption, @Execute, NULL, 0, 0, 0, 0, 0, NULL, NULL, 0, NULL, 0, 0, NULL, NULL);\r\n" +
                "DECLARE @newCtrl int = SCOPE_IDENTITY();\r\n" +
                "INSERT INTO engRibbonMenu\r\n" +
                "    (RibbonMenuIDBase, RibbonGroupID, ControlID, ControlOrder, ControlType, ExtraMenuModuleID, IfFieldsExist, IfUserIDIs)\r\n" +
                "VALUES (0, @RibbonGroupID, @newCtrl, @Orden, 1, 0, NULL, 0);\r\n" +
                "SELECT @newCtrl AS NuevoControlID, @RibbonGroupID AS GrupoUsado;\r\n";
        }

        private static string SqlLit(string s) { return "N'" + (s ?? "").Replace("'", "''") + "'"; }

        private void ActivarTab(ScriptTab tab)
        {
            if (_activeTab != null && _activeTab.Chip != null)
            {
                _activeTab.Chip.BackColor = ConsolaChrome;
                _activeTab.LblName.BackColor = ConsolaChrome;
                _activeTab.Editor.Visible = false;
            }
            _activeTab = tab;
            _activeTab.Chip.BackColor = ConsolaSuperficie;
            _activeTab.LblName.BackColor = ConsolaSuperficie;
            _activeTab.Editor.Visible = true;
            _activeTab.Editor.WrapMode = _chkWrap.Checked ? WrapMode.Word : WrapMode.None;
            _ultimoLenguajeEditor = "";
            DetectarLenguajeStatus();
            _activeTab.Editor.Focus();
            if (_pnlEditorHost != null) _pnlEditorHost.Controls.SetChildIndex(_activeTab.Editor, 0);
            
            Text = "BrosLMV — " + (string.IsNullOrEmpty(_activeTab.AppKey) ? "(sin guardar)" : _activeTab.AppKey);
            if (_status != null) _status.Text = string.IsNullOrEmpty(_activeTab.AppKey) ? "Nuevo script" : "Abierto: " + _activeTab.AppKey;
            
            foreach (var t in _tabs) { if(t.Chip != null) t.Chip.Invalidate(); }
            if (_findBar.Visible) BuscarResaltar();
        }

        private void CerrarTab(ScriptTab tab)
        {
            if (tab.IsModified)
            {
                var r = MessageBox.Show($"El script '{tab.Titulo}' tiene cambios sin guardar. ¿Desea cerrarlo de todas formas?", "Cerrar pestaña", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            
            _tabs.Remove(tab);
            if (_tabStrip != null) _tabStrip.Controls.Remove(tab.Chip);
            if (_pnlEditorHost != null) _pnlEditorHost.Controls.Remove(tab.Editor);
            if (tab.Chip != null) tab.Chip.Dispose();
            if (tab.Editor != null) tab.Editor.Dispose();
            
            if (_tabs.Count > 0)
                ActivarTab(_tabs.Last());
            else
                NuevoScript(); 
        }

        private void NuevoScript()
        {
            if (_activeTab != null) _activeTab.Editor.Visible = false;
            var tab = new ScriptTab();
            _activeTab = tab; 
            
            tab.Editor = new Scintilla { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Visible = false };
            if (_pnlEditorHost != null) _pnlEditorHost.Controls.Add(tab.Editor);
            ConfigurarEditor(); 
            
            tab.Chip = new Panel { Width = 180, Height = 36, BackColor = AppTheme.BgMain, Margin = new Padding(0) };
            tab.Chip.Paint += (s, e) =>
            {
                if (_activeTab == tab)
                    using (var b = new SolidBrush(AppTheme.Primary)) e.Graphics.FillRectangle(b, 0, 0, tab.Chip.Width, 2);
                using (var p = new Pen(AppTheme.Border)) e.Graphics.DrawLine(p, tab.Chip.Width - 1, 2, tab.Chip.Width - 1, tab.Chip.Height);
            };
            
            tab.LblName = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontMain, ForeColor = AppTheme.TextMain, Padding = new Padding(12, 0, 0, 0), BackColor = AppTheme.BgMain, Text = tab.Titulo };
            tab.LblName.Click += (s, e) => ActivarTab(tab);
            tab.Chip.Click += (s, e) => ActivarTab(tab);
            
            var btnCerrarTab = new IconButton { Glyph = Glyph.Close, Kind = BtnKind.Ghost, Dock = DockStyle.Right, Width = 30, Font = AppTheme.FontIconSmall, Radius = 4 };
            btnCerrarTab.Click += (s, e) => CerrarTab(tab);
            
            tab.Chip.Controls.Add(tab.LblName);
            tab.Chip.Controls.Add(btnCerrarTab);
            AplicarPaletaConsola(tab.Chip);
            
            _tabs.Add(tab);
            if (_tabStrip != null) _tabStrip.Controls.Add(tab.Chip);
            
            ActivarTab(tab);
        }

        private void AbrirScript(string appKey)
        {
            try
            {
                var tabEx = _tabs.FirstOrDefault(t => string.Equals(t.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
                if (tabEx != null)
                {
                    ActivarTab(tabEx);
                    return;
                }
            
                string codigo = _ctx.BrosCargar(appKey);
                if (codigo == null) { ctxError("No se encontró el script: " + appKey); return; }
                
                if (string.IsNullOrEmpty(_activeTab.AppKey) && !_activeTab.IsModified && _activeTab.Editor.Text == "")
                {
                    _activeTab.Editor.Text = codigo;
                    _appKey = appKey; 
                    _activeTab.IsModified = false;
                    _activeTab.LblName.Text = _activeTab.Titulo;
                    ActivarTab(_activeTab);
                }
                else
                {
                    NuevoScript();
                    _activeTab.Editor.Text = codigo;
                    _appKey = appKey;
                    _activeTab.IsModified = false;
                    _activeTab.LblName.Text = _activeTab.Titulo;
                    ActivarTab(_activeTab);
                }
                
                Datos.AgregarReciente(appKey);
            }
            catch (Exception ex)
            {
                ctxError("Error al cargar el script: " + ex.Message);
            }
        }
private void Guardar(bool comoNuevo)
        {
            string ak = _appKey;
            string categoria = null;
            if (comoNuevo || string.IsNullOrEmpty(ak))
            {
                string categoriaActual = null;
                if (!string.IsNullOrEmpty(_appKey))
                {
                    try { categoriaActual = Convert.ToString(_ctx.Query("SELECT Categoria FROM zzBrosScript WHERE AppKey=" + "N'" + _appKey.Replace("'", "''") + "'")
                        .FirstOrDefault()?["Categoria"]); } catch { }
                }
                string sugerido = string.IsNullOrEmpty(_appKey) ? (AppKeySugerido(_editor.Text) ?? "MI_SCRIPT") : _appKey;
                var r = PedirNombreYCategoria("Nombre del script. Es también la clave del botón (BrosLMV.<clave>): usa letras, números y _ ; lo demás se cambia por _.",
                                sugerido, categoriaActual);
                if (r == null) return;
                ak = r.Value.nombre;
                categoria = r.Value.categoria;
                if (string.IsNullOrEmpty(ak)) return;
                ak = NormalizarAppKey(ak);
                if (string.IsNullOrEmpty(ak)) return;
                // Las claves NO distinguen mayúsculas de minúsculas (broslmv.mi_script == BrosLMV.Mi_Script): CONSOLA y PRUEBA son reservadas
                // y si ya existe un script con la misma clave (con otra escritura) no se duplica: se pregunta y se conserva la escritura guardada.
                if (ak.Equals("CONSOLA", StringComparison.OrdinalIgnoreCase) || ak.Equals("PRUEBA", StringComparison.OrdinalIgnoreCase))
                { ctxError("«" + ak + "» es un nombre reservado de BrosLMV; elige otro."); return; }
                string existente = ClaveExistente(ak);
                if (existente != null)
                {
                    if (!string.Equals(existente, _appKey, StringComparison.OrdinalIgnoreCase) &&
                        MessageBox.Show("Ya existe el script «" + existente + "» (las claves no distinguen mayúsculas de minúsculas).\n\n¿Reemplazarlo con este código? Su historial de versiones conserva lo anterior.",
                            "BrosLMV", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    ak = existente;
                }
            }
            try
            {
                _ctx.BrosAsegurarTablas();   // crea zzBros* si faltan (requiere conexión viva)
                _ctx.BrosGuardar(ak, ak, _editor.Text, SafeModulo());
                if (!string.IsNullOrEmpty(categoria)) _ctx.BrosCategorizar(ak, categoria);
                _activeTab.IsModified = false;
                _appKey = ak;
                Text = "BrosLMV — " + ak;
                RefrescarEstadoDoc();
                _status.Text = "Guardado en la empresa: " + ak + (string.IsNullOrEmpty(categoria) ? "" : "  ·  categoría: " + categoria);
                CargarArbol();
            }
            catch (Exception ex)
            {
                ctxError("No se pudo guardar: " + ex.Message +
                    "\r\n\r\nVerifica que CONTPAQi tenga abierta esta empresa (conexión viva). " +
                    "Ejecuta DIAGNOSTICO para revisar la conexión.");
            }
        }

        // T2.4: reparación manual de zzBrosScript (agrega columnas nuevas si faltan). Normalmente
        // ya no hace falta -- CargarArbol() la corre sola una vez por sesión -- pero se deja
        // accesible por si alguna Consola vieja (sin este fix) sigue en campo, o por si alguien
        // quiere confirmar/forzarla sin cerrar y reabrir.
        private void RepararBibliotecaScripts()
        {
            try
            {
                _ctx.BrosAsegurarTablas();
                _tablasAseguradas = true;
                CargarArbol();
                int n = 0; try { n = _ctx.BrosListar().Count; } catch { }
                MessageBox.Show(this,
                    "Biblioteca de scripts revisada/reparada.\n\nScripts visibles ahora: " + n,
                    "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ctxError("No se pudo reparar la biblioteca: " + ex.Message +
                    "\r\n\r\nVerifica que CONTPAQi tenga abierta esta empresa (conexión viva).");
            }
        }

        private void Duplicar()
        {
            _appKey = "";
            Text = "BrosLMV — Consola de scripts — (copia sin guardar)";
            _status.Text = "Copia: usa Guardar para nombrarla";
        }

        private int SafeModulo() { try { return _ctx.ModuloActivo(); } catch { return 0; } }

        // Diálogo moderno para pedir un texto (el nombre/AppKey del script).
        private string PedirTexto(string prompt, string valor)
        {
            using (var f = new Form { Text = "BrosLMV", Width = 480, Height = 210, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = AppTheme.BgSurface, Font = AppTheme.FontMain })
            {
                var pnlHead = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.Primary };
                var lbl = new Label { Text = prompt, Left = 22, Top = 22, Width = 430, Height = 48, ForeColor = AppTheme.TextMain, BackColor = Color.Transparent };
                var txt = new TextBox { Left = 22, Top = 76, Width = 430, Text = valor ?? "", BorderStyle = BorderStyle.FixedSingle, Font = AppTheme.FontMain };
                var ok = new IconButton { Text = "Aceptar", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Left = 296, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.OK };
                var ca = new IconButton { Text = "Cancelar", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, Left = 382, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, ca, pnlHead });
                f.AcceptButton = ok; f.CancelButton = ca;
                txt.SelectAll(); txt.Focus();
                return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        // Categorías que ya existen en esta empresa (para elegirlas en un combo en vez de reescribirlas).
        private List<string> CategoriasExistentes()
        {
            try
            {
                return _ctx.Query("SELECT DISTINCT Categoria FROM zzBrosScript WHERE Categoria IS NOT NULL AND Categoria<>'' ORDER BY Categoria")
                    .Select(r => Convert.ToString(r["Categoria"])).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            }
            catch { return new List<string>(); }
        }

        // Pide la categoría de un script: combo editable con las existentes (o escribir una nueva). null = canceló.
        private string PedirCategoria(string appKey, string actual)
        {
            using (var f = new Form { Text = "BrosLMV", Width = 480, Height = 220, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = AppTheme.BgSurface, Font = AppTheme.FontMain })
            {
                var pnlHead = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.Primary };
                var lbl = new Label { Text = "Categoría de \"" + appKey + "\": elige una existente o escribe una nueva (vacío = sin categoría).", Left = 22, Top = 22, Width = 430, Height = 40, ForeColor = AppTheme.TextMain, BackColor = Color.Transparent };
                var cbo = new ComboBox { Left = 22, Top = 72, Width = 430, DropDownStyle = ComboBoxStyle.DropDown, Font = AppTheme.FontMain, FlatStyle = FlatStyle.Flat };
                cbo.Items.AddRange(CategoriasExistentes().Cast<object>().ToArray());
                cbo.Text = actual ?? "";
                var ok = new IconButton { Text = "Aceptar", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Left = 296, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.OK };
                var ca = new IconButton { Text = "Cancelar", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, Left = 382, Top = 118, Width = 78, Height = 34, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, cbo, ok, ca, pnlHead });
                f.AcceptButton = ok; f.CancelButton = ca;
                cbo.Focus();
                return f.ShowDialog(this) == DialogResult.OK ? (cbo.Text ?? "").Trim() : null;
            }
        }

        // Clave tal como está guardada en zzBrosScript (la comparación no distingue mayúsculas), o null si no existe.
        private string ClaveExistente(string appKey)
        {
            try
            {
                var r = _ctx.Query("SELECT TOP 1 AppKey FROM zzBrosScript WHERE AppKey=" + "N'" + appKey.Replace("'", "''") + "'");
                return r.Count == 0 ? null : Convert.ToString(r[0]["AppKey"]);
            }
            catch { return null; }
        }

        // Nombre + categoria en un solo dialogo, al guardar un script nuevo (T2.5, pedido
        // explicito: "que al momento de guardar el nuevo script ya se vaya a una categoria").
        // El combo de categoria es editable (DropDown, no DropDownList): se puede escribir una
        // categoria nueva O elegir una de las que ya existen en esta empresa -- mismo campo
        // libre que ya usa "Categorizar..." (BrosCategorizar), solo que ahora se pide de una
        // vez al guardar, no como paso aparte despues.
        private (string nombre, string categoria)? PedirNombreYCategoria(string prompt, string valorNombre, string categoriaSugerida)
        {
            List<string> categorias = new List<string>();
            try
            {
                categorias = _ctx.Query("SELECT DISTINCT Categoria FROM zzBrosScript WHERE Categoria IS NOT NULL AND Categoria<>'' ORDER BY Categoria")
                    .Select(r => Convert.ToString(r["Categoria"])).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            }
            catch { /* empresa sin scripts todavia, o columna recien agregada -- lista vacia, no es error */ }

            using (var f = new Form { Text = "BrosLMV", Width = 480, Height = 312, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = AppTheme.BgSurface, Font = AppTheme.FontMain })
            {
                var pnlHead = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = AppTheme.Primary };
                var lbl = new Label { Text = prompt, Left = 22, Top = 22, Width = 430, Height = 40, ForeColor = AppTheme.TextMain, BackColor = Color.Transparent };
                var txt = new TextBox { Left = 22, Top = 66, Width = 430, Text = valorNombre ?? "", BorderStyle = BorderStyle.FixedSingle, Font = AppTheme.FontMain };
                var lblClave = new Label { Left = 22, Top = 94, Width = 430, Height = 18, ForeColor = AppTheme.Primary, BackColor = Color.Transparent, Font = AppTheme.FontSmall };
                Action actualizaClave = () => lblClave.Text = "Se guardará como:  BrosLMV." + (NormalizarAppKey(txt.Text) is string k && k.Length > 0 ? k : "…") + "   (no distingue mayúsculas)";
                txt.TextChanged += (s2, e2) => actualizaClave();
                actualizaClave();

                var lblCat = new Label { Text = "Categoría (elige una existente o escribe una nueva; opcional):", Left = 22, Top = 122, Width = 430, Height = 20, ForeColor = AppTheme.TextMuted, BackColor = Color.Transparent, Font = AppTheme.FontSmall };
                var cboCat = new ComboBox { Left = 22, Top = 144, Width = 430, DropDownStyle = ComboBoxStyle.DropDown, Font = AppTheme.FontMain, FlatStyle = FlatStyle.Flat };
                cboCat.Items.AddRange(categorias.Cast<object>().ToArray());
                cboCat.Text = categoriaSugerida ?? "";

                var ok = new IconButton { Text = "Aceptar", Kind = BtnKind.Primary, Accent = AppTheme.Primary, Left = 296, Top = 214, Width = 78, Height = 34, DialogResult = DialogResult.OK };
                var ca = new IconButton { Text = "Cancelar", Kind = BtnKind.Outline, Accent = AppTheme.TextMuted, Left = 382, Top = 214, Width = 78, Height = 34, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, lblClave, lblCat, cboCat, ok, ca, pnlHead });
                f.AcceptButton = ok; f.CancelButton = ca;
                txt.SelectAll(); txt.Focus();

                if (f.ShowDialog(this) != DialogResult.OK) return null;
                return (txt.Text, cboCat.Text?.Trim() ?? "");
            }
        }

        private void InsertarEnEditor(string texto)
        {
            _editor.InsertText(_editor.CurrentPosition, texto);
            _editor.CurrentPosition += texto.Length;
            _editor.Focus();
        }

        // =====================================================
        //   Ejecución segura
        // =====================================================
        private static readonly Regex RX_PELIGRO = new Regex(@"\b(DELETE|UPDATE|INSERT|DROP|TRUNCATE|ALTER|MERGE|EXEC)\b", RegexOptions.IgnoreCase);

        private void Verificar()
        {
            string codigo = _editor.Text;
            if (HostClient.EsPython(codigo) || HostClient.EsSql(codigo))
            {
                Salida("Script " + (HostClient.EsPython(codigo) ? "Python" : "SQL") +
                       ": la verificación se realiza al ejecutar (F5).", Color.FromArgb(120, 180, 220));
                _tabsOut.SelectedIndex = 0; _status.Text = HostClient.EsPython(codigo) ? "Python" : "SQL";
                return;
            }
            var errores = ScriptRunner.Compilar(codigo);
            if (errores.Count == 0) { Salida("Compila correctamente. Sin errores.", AppTheme.Success); _tabsOut.SelectedIndex = 0; Estado("Verificado: OK", AppTheme.Success); }
            else { Errores(string.Join("\r\n", errores.ToArray())); _tabsOut.SelectedIndex = 1; Estado("Verificado: " + errores.Count + " error(es)", AppTheme.Error); }
        }

        private void Ejecutar(bool soloSeleccion)
        {
            string codigo = soloSeleccion && _editor.SelectedText.Length > 0 ? _editor.SelectedText : _editor.Text;

            // Tokens tipados {DATOS:Tabla.Columna} (v2.75.0+): si el texto trae alguno, pide
            // un formulario ANTES de detectar lenguaje/ejecutar -- aplica a sql/python/csharp
            // por igual, es sustitución de texto plano. Sin match, cero cambio de comportamiento.
            var resDatos = HostClient.ResolverFormularioTokens(codigo, _ctx);
            if (resDatos.HuboTokens)
            {
                if (!string.IsNullOrEmpty(resDatos.Error))
                {
                    MessageBox.Show(resDatos.Error, "BrosLMV — token {DATOS:...}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (resDatos.Cancelado)
                {
                    Salida("Operación cancelada por el usuario.", AppTheme.TextMuted);
                    return;
                }
                codigo = resDatos.Codigo;
            }

            // Guardia de empresa: si cambió desde que se abrió la consola, el motor (XEngineLib)
            // sigue ligado a la empresa original → confirmar para no ejecutar en la equivocada.
            if (EmpresaCambio())
            {
                if (MessageBox.Show(
                        "¡OJO! La empresa cambió desde que abriste la consola.\n\n" +
                        "Abierta en: " + _empresaInicial + "\nActiva ahora: " + SafeEmpresa() + "\n\n" +
                        "La consola ejecuta contra el motor de la empresa ORIGINAL (" + _empresaInicial +
                        "). Lo recomendable es cerrarla y reabrirla.\n\n¿Ejecutar de todos modos?",
                        "BrosLMV — cambió la empresa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    != DialogResult.Yes) return;
            }

            // Protección: detectar operaciones de escritura
            if (RX_PELIGRO.IsMatch(codigo) && !_chkSoloLectura.Checked)
            {
                if (MessageBox.Show("El script contiene operaciones que pueden MODIFICAR datos (UPDATE/DELETE/INSERT/…).\n\n¿Ejecutar de todos modos?",
                    "Confirmar ejecución", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            bool esPython = HostClient.EsPython(codigo);

            // Ejecuciones Python superpuestas (dos clics de "Ejecutar" antes de que la primera
            // termine) generaban un host nuevo por clic, todos compitiendo por el mismo hilo de
            // Comercial vía UiPump — eso es lo que disparaba el "busy" nativo de Windows/XEngine
            // cuando se acumulaban. Un guardia simple evita el problema de raíz.
            if (esPython && _ejecutandoPython)
            {
                MessageBox.Show("Ya hay un script Python en ejecución desde esta consola. Espera a que termine antes de volver a ejecutar.",
                    "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _ctx.SoloLectura = _chkSoloLectura.Checked;
            Salida("Ejecutando" + (soloSeleccion ? " (selección)" : "") + "…", AppTheme.TextMuted);
            Estado("Ejecutando…", AppTheme.Warning);
            Application.DoEvents();

            int filasAntes = _ctx.FilasAfectadas;
            var sw = Stopwatch.StartNew();

            if (esPython)
            {
                // Python corre en un proceso aparte (BrosLMV.Host.exe); si se ejecuta SINCRONO
                // aqui, el hilo de Comercial (el mismo de esta consola) queda bloqueado todo el
                // tiempo que la ventana Python este abierta -- Comercial deja de bombear
                // mensajes y Windows puede mostrar "the other application is busy". Por eso se
                // lanza en Task.Run, igual que el boton del ribbon (ClsMain.EjecutarPython):
                // Comercial y la consola quedan libres mientras Python corre en segundo plano.
                _ejecutandoPython = true;
                var hctx = new HostClient.Contexto
                {
                    AppKey      = string.IsNullOrEmpty(_appKey) ? "(consola)" : _appKey,
                    Empresa     = _ctx.Empresa(),
                    Servidor    = _ctx.ServidorActivo(),
                    BaseDatos   = _ctx.Empresa(),
                    UserId      = _ctx.UserIdReal(),
                    ModuleId    = _ctx.ModuloActivo(),
                    Language    = "python",
                    SelectedIds = _ctx.GetSelectedIds().ToArray(),
                    FilaActiva  = _ctx.GetFilaActiva(),
                };
                int timeoutMs = HostClient.TimeoutMsFromHeader(codigo);
                var sqlRunner = new CtxSqlRunner(_ctx);
                var erpRunner = new CtxErpRunner(_ctx);

                System.Threading.Tasks.Task.Run(() =>
                {
                    HostClient.Resultado r;
                    try { r = HostClient.EjecutarPython(codigo, hctx, timeoutMs: timeoutMs, sqlRunner: sqlRunner, erpRunner: erpRunner); }
                    catch (Exception ex) { r = new HostClient.Resultado { Exito = false, CodigoError = "CONSOLA_PYTHON_ERROR", MensajeError = ex.Message, Detalle = ex.StackTrace ?? "" }; }

                    if (IsDisposed) { _ejecutandoPython = false; return; }
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            string res = r.Exito ? "" : HostClient.FormatearError(r);
                            if (r.Exito && !string.IsNullOrEmpty(r.Valor)) Salida(r.Valor, Color.Gainsboro);
                            TerminarEjecucion("consola-python", sw, filasAntes, res);
                            _ejecutandoPython = false;
                        }));
                    }
                    catch { _ejecutandoPython = false; } // la consola se cerró mientras Python corría
                });
                return; // el resto (auditoría, Salida/Estado final) sigue en TerminarEjecucion
            }

            // C# (Roslyn, en proceso) y SQL crudo: rápidos, sin proceso externo — se quedan
            // síncronos como siempre.
            string resSync;
            string tipoAudit = "consola";
            if (HostClient.EsSql(codigo))
            {
                tipoAudit = "consola-sql";
                try { Salida(_ctx.EjecutarSql(codigo), Color.Gainsboro); resSync = ""; }
                catch (Exception ex) { resSync = ex.Message; }
            }
            else
            {
                string salida;
                resSync = ScriptRunner.EjecutarConValor(codigo, _ctx, out salida);
                if (resSync == "" && !string.IsNullOrEmpty(salida)) Salida(salida, Color.Gainsboro);
            }
            TerminarEjecucion(tipoAudit, sw, filasAntes, resSync);
        }

        // Cierre común de una ejecución (síncrona o al terminar la Task de Python): registra
        // auditoría y actualiza la salida/estado de la consola. Debe llamarse en el hilo de la UI.
        private void TerminarEjecucion(string tipoAudit, Stopwatch sw, int filasAntes, string res)
        {
            sw.Stop();
            _statusTiempo.Text = sw.ElapsedMilliseconds + " ms";

            string nombre = string.IsNullOrEmpty(_appKey) ? "(sin guardar)" : _appKey;
            try
            {
                Datos.RegistrarEjecucion(_ctx.Empresa(), _ctx.ModuloActivo(), _ctx.UserID, nombre,
                    tipoAudit, sw.ElapsedMilliseconds, _ctx.FilasAfectadas - filasAntes,
                    res == "" ? "OK" : "ERROR", res, _ctx);
                if (!string.IsNullOrEmpty(_appKey)) { Datos.AgregarReciente(nombre); }
            }
            catch { }

            if (res == "")
            {
                Salida("Ejecución terminada correctamente.  (" + sw.ElapsedMilliseconds + " ms)", AppTheme.Success);
                _tabsOut.SelectedIndex = 0;
                Estado("OK", AppTheme.Success);
            }
            else
            {
                Errores(res);
                _tabsOut.SelectedIndex = 1;
                Estado("Error en ejecución", AppTheme.Error);
            }
            ActualizarContexto();
        }

        // =====================================================
        //   Salida
        // =====================================================
        private void Salida(string t, Color c)
        {
            _outSalida.SelectionColor = AppTheme.TextMuted;
            _outSalida.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
            _outSalida.SelectionColor = Legible(c);
            _outSalida.AppendText(t + "\r\n");
            _outSalida.ScrollToCaret();
        }
        private void Errores(string t)
        {
            _outErrores.SelectionColor = AppTheme.TextMuted;
            _outErrores.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "]\r\n");
            _outErrores.SelectionColor = AppTheme.Error;
            _outErrores.AppendText(t + "\r\n\r\n");
            _outErrores.ScrollToCaret();
            ActualizarContadorErrores();
        }
        private void ctxError(string t) { Errores(t); _tabsOut.SelectedIndex = 1; }

        // Actualiza el texto de la barra de estado con un color semántico.
        private void Estado(string texto, Color color)
        {
            if (_status == null) return;
            _status.Text = texto;
            _status.ForeColor = color;
        }

        // Asegura contraste de un color de salida sobre fondo claro (verde/azul/rojo legibles, gris→texto).
        private static Color Legible(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max - min < 24) return AppTheme.TextMain;          // gris neutro
            var col = c; int guard = 0;
            while (col.GetBrightness() > 0.45f && guard++ < 12) col = AppTheme.Darken(col, 0.15f);
            return col;
        }
    }

    // StatusStrip plano con borde superior suave (sin relieve clásico de Windows).
    internal class BordeSuperiorRenderer : ToolStripProfessionalRenderer
    {
        public BordeSuperiorRenderer() : base(new LightColorTable()) { }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(AppTheme.BgChrome)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            using (var p = new Pen(AppTheme.Border)) e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    // =====================================================
    //   Diálogo "Acerca de" (versión + notas de cambios)
    // =====================================================
    // Nativo y ligero: se construye solo al hacer clic, no toca el arranque de la consola.
    // El detalle largo (historial completo) vive en notas_version.html, que se abre en el
    // navegador del sistema para no cargar un control web dentro del proceso.
    internal sealed class AcercaForm : Form
    {
        public AcercaForm()
        {
            Text = "Acerca de BrosLMV";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, 248);
            BackColor = AppTheme.BgSurface;
            Font = AppTheme.FontMain;

            var pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(28, 26, 56, 56), BackColor = Color.Transparent };
            try { var sl = ResLogo(); if (sl != null) using (sl) pic.Image = Image.FromStream(sl); } catch { }

            var lblBrand = new Label { Text = "BrosLMV", Font = AppTheme.FontHeader, ForeColor = AppTheme.TextMain, AutoSize = true, Location = new Point(104, 30) };
            var lblVer   = new Label { Text = "Versión " + BrosConsola.Version, Font = AppTheme.FontTitle, ForeColor = AppTheme.Primary, AutoSize = true, Location = new Point(106, 62) };
            var lblBuild = new Label { Text = "Compilado: " + BrosConsola.FechaCompilacion(), Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted, AutoSize = true, Location = new Point(106, 84) };

            var lblDesc  = new Label
            {
                Text = "Consola de scripts para CONTPAQi Comercial (C# · Python · SQL).\n" +
                       "Para ver el detalle de cambios de cada versión, abre las notas.",
                Font = AppTheme.FontMain, ForeColor = AppTheme.TextMain, AutoSize = false,
                Bounds = new Rectangle(28, 120, 404, 44)
            };

            var btnNotas = new Button
            {
                Text = "Ver notas de versión", AutoSize = false, Bounds = new Rectangle(28, 196, 180, 30),
                FlatStyle = FlatStyle.Flat, BackColor = AppTheme.Primary, ForeColor = Color.White,
                Font = AppTheme.FontMain, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            btnNotas.FlatAppearance.BorderSize = 0;
            btnNotas.Click += (s, e) => BrosConsola.AbrirNotasVersion();

            var btnCerrar = new Button
            {
                Text = "Cerrar", AutoSize = false, Bounds = new Rectangle(352, 196, 80, 30),
                FlatStyle = FlatStyle.Flat, BackColor = AppTheme.BgChrome, ForeColor = AppTheme.TextMain,
                Font = AppTheme.FontMain, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            btnCerrar.FlatAppearance.BorderColor = AppTheme.Border;
            btnCerrar.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { pic, lblBrand, lblVer, lblBuild, lblDesc, btnNotas, btnCerrar });
            AcceptButton = btnCerrar;
            CancelButton = btnCerrar;
        }

        private static System.IO.Stream ResLogo()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in new[] { "logo_app.png", "logo_color.png", "logo.png", "logo_blanco.png" })
            {
                var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(name, StringComparison.OrdinalIgnoreCase));
                if (n != null) return asm.GetManifestResourceStream(n);
            }
            return null;
        }
    }

    // =====================================================
    //   Tema central (colores, tipografía e iconografía)
    // =====================================================
    public static class AppTheme
    {
        public static Color BgMain = Color.FromArgb(237, 242, 249);     // panel/fondo (tinte azul suave para dar profundidad)
        public static Color BgSurface = Color.FromArgb(255, 255, 255);  // #FFFFFF tarjetas/paneles
        public static Color BgSubtle = Color.FromArgb(238, 243, 250);   // gutter / fila alterna
        public static Color Border = Color.FromArgb(220, 227, 237);     // #DCE3ED borde
        public static Color BorderSoft = Color.FromArgb(232, 238, 246); // borde muy suave
        public static Color TextMain = Color.FromArgb(31, 41, 55);      // #1F2937 texto principal
        public static Color TextMuted = Color.FromArgb(102, 112, 133);  // #667085 texto secundario
        public static Color Primary = Color.FromArgb(37, 99, 235);      // #2563EB azul principal
        public static Color PrimaryHover = Color.FromArgb(29, 78, 216); // #1D4ED8 azul hover
        public static Color PrimarySelected = Color.FromArgb(234, 242, 255); // #EAF2FF selección suave
        public static Color PrimarySoft = Color.FromArgb(225, 235, 252);      // chip/acento azul tenue
        public static Color Success = Color.FromArgb(22, 163, 74);      // #16A34A verde ejecución
        public static Color SuccessHover = Color.FromArgb(21, 128, 61);
        public static Color Error = Color.FromArgb(220, 38, 38);        // #DC2626 rojo error
        public static Color Warning = Color.FromArgb(217, 119, 6);      // #D97706 amarillo aviso
        public static Color Hover = Color.FromArgb(225, 232, 243);      // hover (visible sobre el chrome tintado)
        public static Color BgChrome = Color.FromArgb(232, 238, 246);   // barra superior / estado (no blanco puro)

        public static Font FontMain, FontSmall, FontTitle, FontHeader, FontMono, FontIcon, FontIconSmall;

        static AppTheme()
        {
            string ui   = PickFont("Inter", "Segoe UI Variable Text", "Segoe UI", "Aptos");
            string mono = PickFont("Cascadia Code", "JetBrains Mono", "Consolas");
            string icon = PickFont("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
            FontMain      = new Font(ui, 9f);
            FontSmall     = new Font(ui, 8.25f);
            FontTitle     = new Font(ui, 9.75f, FontStyle.Bold);
            FontHeader    = new Font(ui, 13.5f, FontStyle.Bold);
            FontMono      = new Font(mono, 10.5f);
            FontIcon      = new Font(icon, 11f);
            FontIconSmall = new Font(icon, 9f);
        }

        // Devuelve la primera familia instalada; si ninguna existe, la última como respaldo seguro.
        private static string PickFont(params string[] names)
        {
            foreach (var n in names)
            {
                try { using (var f = new Font(n, 9f)) if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n; }
                catch { }
            }
            return names[names.Length - 1];
        }

        public static Color Darken(Color c, float amt = 0.08f)
            => Color.FromArgb(c.A, (int)(c.R * (1 - amt)), (int)(c.G * (1 - amt)), (int)(c.B * (1 - amt)));
    }

    // Glifos Segoe MDL2 / Fluent usados en la interfaz.
    internal static class Glyph
    {
        public const string Play     = "";  // Play
        public const string PlaySel  = "";  // Play (ejecutar selección)
        public const string Check    = "";  // CheckMark (verificar)
        public const string New      = "";  // Page (nuevo)
        public const string Open     = "";  // OpenFile (abrir)
        public const string Save     = "";  // Save (guardar)
        public const string SaveAs   = "";  // SaveAs (guardar como)
        public const string Copy     = "";  // Copy (duplicar)
        public const string History  = "";  // History (auditoría)
        public const string Refresh  = "";  // Refresh (actualizar)
        public const string Search   = "";  // Search (buscar)
        public const string Folder   = "";  // FolderHorizontal
        public const string Script   = "";  // Code (script)
        public const string Template = "";  // Document (plantilla)
        public const string Close    = "";  // ChromeClose (cerrar pestaña)
        public const string Add      = "";  // Add (+)
        public const string Lock     = "";  // Lock (solo lectura)
        public const string Clear    = "";  // Delete (limpiar salida)
        public const string FontIco  = "";  // FontSize
        public const string Info     = "";  // Info
        public const string Warn     = "";  // Warning
        public const string ErrorIco = "";  // ErrorBadge
        public static readonly string Down    = ((char)0xE70D).ToString(); // ChevronDown (buscar siguiente)
        public static readonly string Up      = ((char)0xE70E).ToString(); // ChevronUp (buscar anterior)
        public static readonly string Full    = ((char)0xE740).ToString(); // FullScreen
        public static readonly string Restore = ((char)0xE73F).ToString(); // BackToWindow
        public const string Dot      = "●"; // indicador sin guardar
    }

    // Fábrica de controles y utilidades de dibujo modernas (esquinas suaves, estados).
    internal static class ModernUI
    {
        public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); p.CloseFigure(); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Renderiza un glifo a un bitmap (para ImageList del árbol, etc.).
        public static Bitmap GlyphImage(string glyph, int size, Color color, float fontSize = 0)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var f = new Font(AppTheme.FontIcon.FontFamily, fontSize > 0 ? fontSize : size * 0.62f))
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }

    // Botón plano moderno: icono (glifo) + texto, esquinas suaves y estados hover/pressed/disabled.
    internal enum BtnKind { Ghost, Primary, Outline, Toolbar }
    internal class IconButton : Button
    {
        public string Glyph = "";
        public BtnKind Kind = BtnKind.Ghost;
        public Color Accent = Color.Empty;
        public Color Superficie = Color.Empty;
        public int Radius = 6;
        public int PadX = 14;          // padding horizontal interno
        public int MinH = 32;          // alto mínimo
        private bool _hover, _down;

        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Font = AppTheme.FontMain;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _down = false; Invalidate(); };
            MouseDown  += (s, e) => { _down = true; Invalidate(); };
            MouseUp    += (s, e) => { _down = false; Invalidate(); };
        }

        // Medición DPI-correcta: la hace el motor de layout (TextRenderer sin gráfico usa
        // el DC de pantalla a la escala actual). Así nunca se encima ni se recorta el texto.
        public override Size GetPreferredSize(Size proposedSize)
        {
            int gap = (!string.IsNullOrEmpty(Glyph) && !string.IsNullOrEmpty(Text)) ? 7 : 0;
            Size ico = string.IsNullOrEmpty(Glyph) ? Size.Empty
                : TextRenderer.MeasureText(Glyph, AppTheme.FontIcon, Size.Empty, TextFormatFlags.NoPadding);
            Size txt = string.IsNullOrEmpty(Text) ? Size.Empty
                : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int w = PadX * 2 + ico.Width + gap + txt.Width;
            int h = Math.Max(MinH, Math.Max(ico.Height, txt.Height) + 12);
            return new Size(w, h);
        }

        // Color de fondo real bajo el botón (el del contenedor), para que NUNCA queden
        // residuos de texto: limpiamos el área completa en cada repintado.
        private Color FondoBase()
        {
            if (BackColor != Color.Transparent) return BackColor;
            var p = Parent;
            while (p != null) { if (p.BackColor != Color.Transparent) return p.BackColor; p = p.Parent; }
            return AppTheme.BgSurface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(FondoBase())) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // Re-limpiar por seguridad (algunos contenedores no invocan OnPaintBackground).
            using (var bb = new SolidBrush(FondoBase())) g.FillRectangle(bb, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, fg, border = Color.Empty;
            Color baseC = Accent == Color.Empty ? AppTheme.Primary : Accent;
            if (!Enabled)
            {
                fill = Kind == BtnKind.Primary ? AppTheme.Border : Color.Transparent;
                fg = AppTheme.TextMuted;
            }
            else if (Kind == BtnKind.Primary)
            {
                fill = _down ? AppTheme.Darken(baseC, 0.16f) : (_hover ? AppTheme.Darken(baseC) : baseC);
                fg = Color.White;
            }
            else if (Kind == BtnKind.Outline)
            {
                fill = _down ? AppTheme.PrimarySelected : (_hover ? AppTheme.Hover : (Superficie.IsEmpty ? AppTheme.BgSurface : Superficie));
                fg = baseC; border = AppTheme.Border;
            }
            else if (Kind == BtnKind.Toolbar)
            {
                // Botón blanco con borde sobre la barra tintada: se lee claramente como botón.
                fill = _down ? AppTheme.PrimarySelected : (_hover ? AppTheme.PrimarySoft : (Superficie.IsEmpty ? AppTheme.BgSurface : Superficie));
                fg = _hover || _down ? AppTheme.PrimaryHover : AppTheme.TextMain;
                border = _hover || _down ? AppTheme.Primary : AppTheme.Border;
            }
            else // Ghost
            {
                fill = _down ? AppTheme.Border : (_hover ? AppTheme.Hover : Color.Transparent);
                fg = AppTheme.TextMain;
            }

            using (var path = ModernUI.Round(r, Radius))
            {
                if (fill != Color.Transparent) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            // Medir icono + texto y centrar el grupo
            int gap = string.IsNullOrEmpty(Glyph) || string.IsNullOrEmpty(Text) ? 0 : 7;
            Size szIco = string.IsNullOrEmpty(Glyph) ? Size.Empty
                : TextRenderer.MeasureText(g, Glyph, AppTheme.FontIcon, Size.Empty, TextFormatFlags.NoPadding);
            Size szTxt = string.IsNullOrEmpty(Text) ? Size.Empty
                : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int totalW = szIco.Width + gap + szTxt.Width;
            int x = (Width - totalW) / 2;
            int midY = Height / 2;
            if (!string.IsNullOrEmpty(Glyph))
            {
                TextRenderer.DrawText(g, Glyph, AppTheme.FontIcon, new Rectangle(x, midY - szIco.Height / 2, szIco.Width, szIco.Height), fg, TextFormatFlags.NoPadding);
                x += szIco.Width + gap;
            }
            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, midY - szTxt.Height / 2, szTxt.Width, szTxt.Height), fg, TextFormatFlags.NoPadding);
        }
    }

    public class LightColorTable : ProfessionalColorTable
    {
        public override Color ToolStripBorder => AppTheme.Border;
        public override Color ToolStripGradientBegin => AppTheme.BgSurface;
        public override Color ToolStripGradientEnd => AppTheme.BgSurface;
        public override Color ToolStripPanelGradientBegin => AppTheme.BgSurface;
        public override Color ToolStripPanelGradientEnd => AppTheme.BgSurface;
        public override Color MenuStripGradientBegin => AppTheme.BgSurface;
        public override Color MenuStripGradientEnd => AppTheme.BgSurface;
        public override Color ButtonSelectedHighlight => AppTheme.PrimarySelected;
        public override Color ButtonSelectedGradientBegin => AppTheme.PrimarySelected;
        public override Color ButtonSelectedGradientEnd => AppTheme.PrimarySelected;
        public override Color ButtonSelectedBorder => AppTheme.PrimarySelected;
        public override Color ButtonPressedHighlight => AppTheme.Primary;
        public override Color ButtonPressedGradientBegin => AppTheme.Primary;
        public override Color ButtonPressedGradientEnd => AppTheme.Primary;
        public override Color ButtonPressedBorder => AppTheme.Primary;
        public override Color MenuItemSelected => AppTheme.PrimarySelected;
        public override Color MenuItemSelectedGradientBegin => AppTheme.PrimarySelected;
        public override Color MenuItemSelectedGradientEnd => AppTheme.PrimarySelected;
        public override Color MenuItemBorder => AppTheme.PrimarySelected;
    }

    // Asistente "Nueva acción": crea un botón sin escribir código, eligiendo una receta
    // (RecetasRegistro) y llenando un formulario generado desde su EsquemaConfig. Mismo
    // estilo visual que el resto de la Consola (AppTheme, tarjetas con BordeTarjeta,
    // IconButton) -- antes de v2.53.0 usaba controles crudos sin tema y posicionamiento
    // absoluto (Location = new Point(0, y)), lo que además tenía un bug real: el botón de
    // insertar token se colocaba fuera del panel visible en pantallas angostas. Reescrito
    // con TableLayoutPanel (cada fila se autoajusta, nunca se sale del contenedor).
    internal sealed class NuevaAccionForm : Form
    {
        private ScriptContext _ctx;
        private ComboBox _cboRecetas;
        private Label _lblDescripcion;
        private TableLayoutPanel _tlCampos;
        private TextBox _txtEjemplo;
        private TextBox _txtAppKey;
        private TextBox _txtNombre;
        private Dictionary<string, Control> _inputs = new Dictionary<string, Control>();

        public NuevaAccionForm(ScriptContext ctx)
        {
            _ctx = ctx;
            Text = "Nueva acción sin código";
            Width = 620;
            Height = 760;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = AppTheme.BgMain;
            Font = AppTheme.FontMain;
            ForeColor = AppTheme.TextMain;
            ShowIcon = false;
            MinimizeBox = false;
            MaximizeBox = false;

            // ---- Encabezado: título + explicación de qué es esta ventana ----
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = AppTheme.BgChrome, Padding = new Padding(18, 10, 18, 10) };
            pnlHeader.Paint += (s, e) => BrosConsola.BordeInferior(e.Graphics, pnlHeader);
            var lblTitulo = new Label { Text = "Nueva acción sin código", Dock = DockStyle.Top, AutoSize = false, Height = 24, Font = AppTheme.FontHeader, ForeColor = AppTheme.TextMain };
            var lblSub = new Label { Text = "Elige qué debe hacer el botón y llena los datos — no hace falta escribir código.", Dock = DockStyle.Top, AutoSize = false, Height = 20, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            pnlHeader.Controls.Add(lblSub);
            pnlHeader.Controls.Add(lblTitulo);
            Controls.Add(pnlHeader);

            // ---- Pie: Nombre/AppKey + botones (Dock=Bottom, se agrega ANTES del cuerpo para
            //      que quede reservado abajo sin pelearse por el espacio con el AutoScroll) ----
            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 172, BackColor = AppTheme.BgChrome, Padding = new Padding(18, 12, 18, 12) };
            pnlFooter.Paint += (s, e) => BordeSuperior(e.Graphics, pnlFooter);

            var lblNombreCap = new Label { Text = "Nombre visible (ej. \"Crear OC desde requisición\")", AutoSize = false, Dock = DockStyle.Top, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            _txtNombre = new TextBox { Dock = DockStyle.Top, Font = AppTheme.FontMain };
            var espN = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };
            var lblAppKeyCap = new Label { Text = "Clave interna (AppKey, sin espacios ni acentos)", AutoSize = false, Dock = DockStyle.Top, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            _txtAppKey = new TextBox { Dock = DockStyle.Top, Font = AppTheme.FontMain };
            var espA = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Color.Transparent };

            var pnlBotones = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2 };
            pnlBotones.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            pnlBotones.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            var btnGuardar = new IconButton { Text = "Guardar acción", Kind = BtnKind.Primary, Accent = AppTheme.Success, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            btnGuardar.Click += BtnGuardar_Click;
            var btnCancelar = new IconButton { Text = "Cancelar", Kind = BtnKind.Toolbar, Accent = Color.Empty, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlBotones.Controls.Add(btnGuardar, 0, 0);
            pnlBotones.Controls.Add(btnCancelar, 1, 0);

            // Se agregan en orden inverso porque Dock=Top apila cada control nuevo ARRIBA de
            // los anteriores -- ver la nota T4.1 en ESTADO.md sobre este mismo gotcha.
            pnlFooter.Controls.Add(pnlBotones);
            pnlFooter.Controls.Add(espA);
            pnlFooter.Controls.Add(_txtAppKey);
            pnlFooter.Controls.Add(lblAppKeyCap);
            pnlFooter.Controls.Add(espN);
            pnlFooter.Controls.Add(_txtNombre);
            pnlFooter.Controls.Add(lblNombreCap);
            Controls.Add(pnlFooter);

            // ---- Cuerpo (scrollable): selector de receta + campos dinámicos + ejemplo ----
            var pnlBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18, 14, 18, 14), BackColor = AppTheme.BgMain };

            var pnlRecetaCard = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = AppTheme.BgSurface, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 14) };
            pnlRecetaCard.Paint += (s, e) => BrosConsola.BordeTarjeta(e.Graphics, pnlRecetaCard);
            var lblRecetaCap = new Label { Text = "Tipo de acción", Dock = DockStyle.Top, AutoSize = false, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            _cboRecetas = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.FontMain };
            _cboRecetas.Items.AddRange(System.Linq.Enumerable.ToArray(RecetasRegistro.Listar()));
            _cboRecetas.DisplayMember = "Nombre";
            _cboRecetas.SelectedIndexChanged += (s, e) => ConstruirFormulario();
            var espR = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };
            _lblDescripcion = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 48, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMain };
            // Orden inverso de nuevo (Dock=Top apila hacia arriba):
            pnlRecetaCard.Controls.Add(_lblDescripcion);
            pnlRecetaCard.Controls.Add(espR);
            pnlRecetaCard.Controls.Add(_cboRecetas);
            pnlRecetaCard.Controls.Add(lblRecetaCap);

            var pnlCamposCard = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = AppTheme.BgSurface, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 14) };
            pnlCamposCard.Paint += (s, e) => BrosConsola.BordeTarjeta(e.Graphics, pnlCamposCard);
            var lblCamposCap = new Label { Text = "Datos de la acción", Dock = DockStyle.Top, AutoSize = false, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted, Margin = new Padding(0, 0, 0, 6) };
            _tlCampos = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            _tlCampos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlCamposCard.Controls.Add(_tlCampos);
            pnlCamposCard.Controls.Add(lblCamposCap);

            var pnlEjemploCard = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = AppTheme.BgSurface, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 14) };
            pnlEjemploCard.Paint += (s, e) => BrosConsola.BordeTarjeta(e.Graphics, pnlEjemploCard);
            var lblEjemploCap = new Label { Text = "¿No sabes qué poner? Mira un ejemplo real", Dock = DockStyle.Top, AutoSize = false, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted };
            _txtEjemplo = new TextBox { Dock = DockStyle.Top, Multiline = true, ReadOnly = true, Height = 70, Font = AppTheme.FontMono, BackColor = AppTheme.BgSubtle, ForeColor = AppTheme.TextMuted, BorderStyle = BorderStyle.FixedSingle };
            var espE = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent };
            var btnUsarEjemplo = new IconButton { Text = "Llenar con este ejemplo", Kind = BtnKind.Outline, Accent = AppTheme.Primary, Dock = DockStyle.Top, Height = 32 };
            btnUsarEjemplo.Click += (s, e) => LlenarConEjemplo();
            pnlEjemploCard.Controls.Add(btnUsarEjemplo);
            pnlEjemploCard.Controls.Add(espE);
            pnlEjemploCard.Controls.Add(_txtEjemplo);
            pnlEjemploCard.Controls.Add(lblEjemploCap);

            // Orden inverso otra vez para que quede Receta -> Datos -> Ejemplo de arriba a abajo:
            pnlBody.Controls.Add(pnlEjemploCard);
            pnlBody.Controls.Add(pnlCamposCard);
            pnlBody.Controls.Add(pnlRecetaCard);
            Controls.Add(pnlBody);
            pnlBody.BringToFront();

            if (_cboRecetas.Items.Count > 0)
                _cboRecetas.SelectedIndex = 0;
        }

        private static void BordeSuperior(Graphics g, Control c)
        { using (var p = new Pen(AppTheme.Border)) g.DrawLine(p, 0, 0, c.Width, 0); }

        private void ConstruirFormulario()
        {
            _tlCampos.Controls.Clear();
            _tlCampos.RowStyles.Clear();
            _tlCampos.RowCount = 0;
            _inputs.Clear();

            var receta = _cboRecetas.SelectedItem as IReceta;
            if (receta == null) return;

            _lblDescripcion.Text = receta.Descripcion ?? "";
            ActualizarEjemploTexto(receta);

            if (receta.EsquemaConfig == null) return;

            int fila = 0;
            foreach (var c in receta.EsquemaConfig)
            {
                var lbl = new Label { Text = c.Etiqueta + (c.Requerido ? " *" : ""), AutoSize = false, Dock = DockStyle.Top, Height = 18, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMain, Margin = new Padding(0, fila == 0 ? 0 : 8, 0, 2) };
                _tlCampos.RowCount++;
                _tlCampos.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _tlCampos.Controls.Add(lbl, 0, fila++);

                // Fila del control de entrada, en su propia mini tabla de 2 columnas
                // (input=Fill, boton de token=ancho fijo) -- así el botón NUNCA queda fuera
                // del panel visible, a diferencia de la versión con Location a mano.
                var fila2 = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
                fila2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

                Control input;
                if (c.Tipo == "numero")
                {
                    input = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 999999999, Minimum = -999999999, Font = AppTheme.FontMain };
                }
                else
                {
                    var txt = new TextBox { Dock = DockStyle.Fill, Font = AppTheme.FontMain };
                    if (c.Tipo == "texto_multilinea")
                    {
                        txt.Multiline = true;
                        txt.Height = 64;
                        txt.ScrollBars = ScrollBars.Vertical;
                    }
                    input = txt;
                }
                fila2.Controls.Add(input, 0, 0);

                if (c.PermiteTokens && input is TextBox txtTok)
                {
                    fila2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    var btnToken = new IconButton { Text = "{ }", Kind = BtnKind.Toolbar, Accent = Color.Empty, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), MinH = txtTok.Multiline ? 26 : 0 };
                    _tips2.SetToolTip(btnToken, "Insertar un token ({pID}, {pUserID}...)");
                    btnToken.Click += (s, e) => MostrarTokensMenu(btnToken, txtTok);
                    fila2.Controls.Add(btnToken, 1, 0);
                }

                _tlCampos.RowCount++;
                _tlCampos.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _tlCampos.Controls.Add(fila2, 0, fila++);

                _inputs[c.Nombre] = input;
            }
        }

        private readonly ToolTip _tips2 = new ToolTip();

        private void ActualizarEjemploTexto(IReceta receta)
        {
            if (receta.Ejemplo == null || receta.Ejemplo.Count == 0 || receta.EsquemaConfig == null)
            { _txtEjemplo.Text = "(esta receta no trae ejemplo)"; return; }

            var sb = new System.Text.StringBuilder();
            foreach (var c in receta.EsquemaConfig)
                if (receta.Ejemplo.ContainsKey(c.Nombre))
                    sb.AppendLine(c.Etiqueta + ": " + receta.Ejemplo[c.Nombre]);
            _txtEjemplo.Text = sb.ToString().TrimEnd();
        }

        private void LlenarConEjemplo()
        {
            var receta = _cboRecetas.SelectedItem as IReceta;
            if (receta == null || receta.Ejemplo == null) return;
            foreach (var c in receta.EsquemaConfig)
            {
                if (!receta.Ejemplo.ContainsKey(c.Nombre) || !_inputs.ContainsKey(c.Nombre)) continue;
                string valor = receta.Ejemplo[c.Nombre];
                var input = _inputs[c.Nombre];
                if (input is NumericUpDown num)
                { if (decimal.TryParse(valor, out var d)) num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, d)); }
                else if (input is TextBox txt)
                    txt.Text = valor;
            }
            if (string.IsNullOrWhiteSpace(_txtNombre.Text)) _txtNombre.Text = receta.Nombre + " (ejemplo)";
        }

        private void MostrarTokensMenu(Control anclaje, TextBox txt)
        {
            var menu = new ContextMenuStrip { Font = AppTheme.FontMain };
            var tokens = new[] {
                ("{pID}", "primer ID seleccionado"),
                ("{pIDs}", "todos los IDs seleccionados"),
                ("{pUserID}", "usuario activo"),
                ("{pModulo}", "módulo activo"),
                ("{pEmpresa}", "empresa (BD) activa"),
            };
            foreach (var (token, desc) in tokens)
            {
                var item = menu.Items.Add(token + "  —  " + desc);
                item.Click += (s, e) => { txt.SelectedText = token; txt.Focus(); };
            }
            menu.Show(anclaje, new Point(0, anclaje.Height));
        }

        private void BtnGuardar_Click(object sender, EventArgs e)
        {
            var receta = _cboRecetas.SelectedItem as IReceta;
            if (receta == null) { MessageBox.Show("Elige un tipo de acción primero.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            string ak = _txtAppKey.Text.Trim().Replace(" ", "_");
            string nom = _txtNombre.Text.Trim();
            if (string.IsNullOrEmpty(ak) || string.IsNullOrEmpty(nom))
            {
                MessageBox.Show("Falta el nombre visible o la clave interna (AppKey).", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var config = new Dictionary<string, object>();
            foreach (var c in receta.EsquemaConfig)
            {
                var input = _inputs[c.Nombre];
                object valor = (c.Tipo == "numero") ? (object)((NumericUpDown)input).Value : ((TextBox)input).Text.Trim();

                if (c.Requerido && (valor == null || string.IsNullOrEmpty(valor.ToString())))
                {
                    MessageBox.Show("El campo \"" + c.Etiqueta + "\" es obligatorio.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                config[c.Nombre] = valor;
            }

            var jsonDict = new Dictionary<string, object> { { "receta", receta.Id }, { "config", config } };
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string codigoCompleto = "# lang: receta\n" + serializer.Serialize(jsonDict);

            try
            {
                _ctx.BrosAsegurarTablas();
                _ctx.BrosGuardar(ak, nom, codigoCompleto, _ctx.ModuloActivo());
                MessageBox.Show("Acción \"" + nom + "\" guardada. Actualiza el árbol de la izquierda para verla.", "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar: " + ex.Message, "BrosLMV", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
