// BrosLMV - GPL-3.0. Copyright (C) 2026 Cristofer Candelas Garcia.
// Se compila solo en la copia temporal de verificar_diseno.ps1, nunca en el addon.
using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ScintillaNET;

namespace BrosLMV
{
    internal static class PruebaDiseno
    {
        private const BindingFlags Privado = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Campo(object obj, string nombre) => obj.GetType().GetField(nombre, Privado | BindingFlags.Public).GetValue(obj);
        private static void Llamar(object obj, string metodo, params object[] args) => obj.GetType().GetMethod(metodo, Privado).Invoke(obj, args);
        private static void LlamarEditor(string metodo, Scintilla ed) => typeof(BrosConsola).GetMethod(metodo, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { ed });
        private static string[] Completar(object form, Scintilla ed, string codigo)
        {
            ed.Text = codigo;
            ed.GotoPosition(ed.TextLength);
            var parametros = new object[] { ed, 0 };
            return (string[])form.GetType().GetMethod("OpcionesCompletado", Privado).Invoke(form, parametros);
        }
        private static void Exigir(bool valido, string mensaje) { if (!valido) throw new Exception(mensaje); Console.WriteLine("OK " + mensaje); }
        private static void Captura(Form form, string ruta)
        {
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(ruta, ImageFormat.Png);
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                using (var form = new BrosConsola(1, null))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.Show();
                    Application.DoEvents();
                    var primero = Campo(form, "_activeTab");
                    var editor = (Scintilla)Campo(primero, "Editor");
                    var principal = (SplitContainer)Campo(form, "_splitMain");
                    editor.Text = "var ids = ctx.GetSelectedIds();\r\nctx.Msg(\"Registros seleccionados: \" + ids.Count);";
                    Exigir(editor.Styles[Style.Default].BackColor == Color.FromArgb(232, 234, 237), "Editor gris claro sin fondo blanco intenso");
                    Exigir(!principal.Panel2Collapsed, "Contexto y SDK visibles al iniciar");
                    Exigir(!editor.UseTabs && editor.TabWidth == 4 && editor.IndentWidth == 4, "Sangria de cuatro espacios");
                    Llamar(form, "CambiarLenguaje", "Python");
                    Exigir(editor.Text.StartsWith("# lang: python") && editor.Text.Contains("ctx.GetSelectedIds"), "Selector conserva codigo y coloca marcador Python");
                    Exigir(editor.Lexer == Lexer.Python, "Resaltado Python");
                    Exigir(((ListView)Campo(form, "_lstMetodosPython")).Visible, "Referencias siguen el lenguaje Python");
                    Llamar(form, "CambiarLenguaje", "SQL");
                    Exigir(editor.Text.StartsWith("-- lang: sql") && !editor.Text.Contains("# lang: python"), "Selector sustituye marcador SQL");
                    Exigir(editor.Lexer == Lexer.Sql, "Resaltado SQL");
                    Exigir(((ListView)Campo(form, "_lstMetodosSql")).Visible, "Referencias siguen el lenguaje SQL");
                    Llamar(form, "CambiarLenguaje", "C#");
                    Exigir(!editor.Text.Contains("lang:") && editor.Lexer == Lexer.Cpp, "C# conserva el marcador predeterminado y su resaltado");
                    Exigir(Completar(form, editor, "ctx.GetSel").Contains("GetSelectedIds"), "Completado C# filtra por prefijo");
                    editor.Focus();
                    Llamar(form, "MostrarCompletado", editor);
                    Exigir(editor.AutoCActive, "Lista de completado realmente visible");
                    editor.AutoCSelect("GetSelectedIds");
                    editor.AutoCComplete();
                    Exigir(editor.Text == "ctx.GetSelectedIds", "Aceptar completado reemplaza solo el prefijo");
                    var raiz = Completar(form, editor, "ctx.");
                    Exigir(raiz.Contains("erp") && !raiz.Any(x => x.Contains(".")), "Completado de ctx ofrece miembros directos");
                    Exigir(Completar(form, editor, "ctx.erp.").Contains("RefreshGrid"), "Completado de ctx.erp usa el catalogo");
                    Exigir(Completar(form, editor, "# lang: python\r\nctx.get_sel").Contains("get_selected_ids"), "Completado Python usa sus nombres reales");
                    Exigir(Completar(form, editor, "// ctx.").Length == 0, "Sin sugerencias dentro de comentarios");
                    Exigir(Completar(form, editor, "var texto = \"ctx.").Length == 0, "Sin sugerencias dentro de cadenas");
                    Exigir(Completar(form, editor, "-- lang: sql\r\nctx.").Length == 0, "Sin sugerencias C# en SQL");
                    editor.Text = "# lang: python\r\nif True:\r\n";
                    editor.GotoPosition(editor.TextLength);
                    LlamarEditor("IndentarLinea", editor);
                    Exigir(editor.Lines[2].Indentation == 4, "Sangria automatica en bloque Python");
                    editor.Text = "if (true) {\r\n";
                    editor.GotoPosition(editor.TextLength);
                    LlamarEditor("IndentarLinea", editor);
                    Exigir(editor.Lines[1].Indentation == 4, "Sangria automatica en bloque C#");
                    LlamarEditor("ResaltarPareja", editor);
                    var filtroSdk = (TextBox)Campo(form, "_txtSdk");
                    filtroSdk.Text = "GetSelectedIds";
                    var resultadosSdk = ((ListView)Campo(form, "_lstMetodosCSharp")).Items.Cast<ListViewItem>().ToArray();
                    Exigir(resultadosSdk.Any(it => it.Text == "GetSelectedIds") && resultadosSdk.All(it =>
                        (it.Text + " " + it.SubItems[1].Text).Contains("GetSelectedIds")), "Filtro SDK por nombre y descripcion");
                    filtroSdk.Text = "zz_sin_coincidencias";
                    Exigir(((ListView)Campo(form, "_lstMetodosCSharp")).Items.Count == 0, "Filtro SDK vacio sin referencias incorrectas");
                    filtroSdk.Text = "";
                    ((Action<int>)Campo(form, "_activarReferencia"))(3);
                    filtroSdk.Text = "query";
                    Exigir(((ListView)Campo(form, "_lstSeleccion")).Visible, "Filtrar SDK no cambia la referencia elegida");
                    filtroSdk.Text = "";
                    editor.Text = "// áé\r\nvar dato = 1; var Dato = 2; var datos = 3;";
                    editor.SetEmptySelection(0);
                    Llamar(form, "MostrarBuscar");
                    ((TextBox)Campo(form, "_txtFind")).Text = "dato";
                    Exigir(editor.SelectedText == "dato" && ((IList)Campo(form, "_findHits")).Count == 3, "Busqueda correcta despues de acentos");
                    ((CheckBox)Campo(form, "_findPalabra")).Checked = true;
                    Exigir(((IList)Campo(form, "_findHits")).Count == 2, "Busqueda de palabras completas");
                    ((CheckBox)Campo(form, "_findMayusculas")).Checked = true;
                    Exigir(((IList)Campo(form, "_findHits")).Count == 1, "Busqueda sensible a mayusculas");
                    Llamar(form, "OcultarBuscar");

                    primero.GetType().GetField("AppKey").SetValue(primero, "CONTAR_SELECCION");
                    primero.GetType().GetField("IsModified").SetValue(primero, false);
                    Llamar(form, "RefrescarEstadoDoc");
                    var estado = (Label)Campo(form, "_lblEstadoDoc");
                    Exigir(estado.Text.Contains("Guardado"), "Estado guardado no modifica la pestaña");
                    editor.Text = "";
                    Exigir((bool)Campo(primero, "IsModified") && estado.Text.Contains("Sin guardar"), "Borrar todo tambien marca cambios sin guardar");
                    editor.Text = "var ids = ctx.GetSelectedIds();\r\nctx.Msg(\"Registros seleccionados: \" + ids.Count);";
                    var nuevoAtajo = new KeyEventArgs(Keys.Control | Keys.N);
                    Llamar(form, "AtajoArchivo", nuevoAtajo);
                    Exigir(nuevoAtajo.SuppressKeyPress, "Ctrl+N crea otra pestana y consume el atajo");
                    var segundo = Campo(form, "_activeTab");
                    var editor2 = (Scintilla)Campo(segundo, "Editor");
                    Exigir(!editor.Visible && editor2.Visible, "Solo el editor de la pestaña activa es visible");
                    editor2.Text = "# lang: python\r\nfrom broslmv import ctx\r\nctx.msg(\"Consulta de ejemplo\")";
                    ((CheckBox)Campo(form, "_chkWrap")).Checked = true;
                    Llamar(form, "ActivarTab", primero);
                    Exigir(editor.Lexer == Lexer.Cpp && editor.WrapMode == WrapMode.Word, "Cambiar pestaña sincroniza lenguaje y ajuste");
                    Llamar(form, "ActivarTab", segundo);
                    Exigir(editor2.Lexer == Lexer.Python && editor2.WrapMode == WrapMode.Word, "Cada pestaña conserva su codigo y aplica la vista actual");
                    Llamar(form, "MostrarBuscar");
                    ((TextBox)Campo(form, "_txtFind")).Text = "ctx";
                    Llamar(form, "ActivarTab", primero);
                    Exigir(((IList)Campo(form, "_findHits")).Count == 2, "Busqueda se recalcula al cambiar pestana");
                    editor.GotoPosition(editor.TextLength);
                    editor.AppendText("\r\nctx");
                    Exigir(((IList)Campo(form, "_findHits")).Count == 3, "Busqueda se recalcula al editar");
                    Llamar(form, "OcultarBuscar");
                    Llamar(form, "ActivarTab", segundo);
                    Llamar(form, "ToggleZen");
                    Llamar(form, "ToggleZen");
                    Exigir(!principal.Panel2Collapsed, "Salir de Zen restaura el contexto predeterminado");
                    Llamar(form, "ToggleContexto");
                    Exigir(principal.Panel2Collapsed, "Contexto se puede ocultar");
                    Llamar(form, "ToggleZen");
                    Llamar(form, "ToggleZen");
                    Exigir(principal.Panel2Collapsed, "Salir de Zen respeta contexto oculto");
                    Llamar(form, "ToggleContexto");
                    Exigir(!principal.Panel2Collapsed && principal.Panel2.Width >= 300, "Contexto y referencias se pueden desplegar");
                    Llamar(form, "ToggleZen");
                    Llamar(form, "ToggleZen");
                    Exigir(!principal.Panel2Collapsed, "Salir de Zen restaura el contexto visible");

                    var arbol = (TreeView)Campo(form, "_tree");
                    arbol.Nodes.Add(new TreeNode("Ejemplos de consulta", new[] { new TreeNode("CONTAR_SELECCION"), new TreeNode("REPORTE_SELECCION"), new TreeNode("DOCUMENTOS_SELECCION") }));
                    arbol.ExpandAll();
                    ((RichTextBox)Campo(form, "_outSalida")).Text = "Vista de laboratorio sin conexion a empresa.\r\nNo se ejecutaron scripts contra Comercial.";
                    foreach (var size in new[] { new Size(1280, 840), new Size(1040, 660) })
                    {
                        form.Size = size;
                        form.PerformLayout();
                        Application.DoEvents();
                        Exigir(editor2.Width > 400 && editor2.Height > 150, "Editor utilizable a " + size.Width + "x" + size.Height);
                        Exigir(((ListView)Campo(form, "_lstMetodosPython")).Height >= 100, "Referencias utilizables a " + size.Width);
                        foreach (var flow in form.Controls.OfType<FlowLayoutPanel>())
                            Exigir(flow.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Right <= flow.ClientSize.Width && c.Bottom <= flow.ClientSize.Height), "Acciones sin recorte a " + size.Width);
                        Captura(form, Path.Combine(args[0], "consola-" + size.Width + ".png"));
                        Llamar(form, "MostrarBuscar");
                        var barraBuscar = (Panel)Campo(form, "_findBar");
                        Exigir(barraBuscar.Controls.Cast<Control>().All(c => c.Right <= barraBuscar.ClientSize.Width &&
                            c.Bottom <= barraBuscar.ClientSize.Height), "Busqueda sin recorte a " + size.Width);
                        Captura(form, Path.Combine(args[0], "consola-buscar-" + size.Width + ".png"));
                        Llamar(form, "OcultarBuscar");
                    }
                    form.Size = new Size(1280, 840);
                    Captura(form, Path.Combine(args[0], "consola-contexto.png"));
                    Llamar(form, "ToggleContexto");
                    var izquierda = (SplitContainer)Campo(form, "_splitLeft");
                    izquierda.SplitterDistance = izquierda.Width - izquierda.Panel2MinSize - izquierda.SplitterWidth;
                    Llamar(form, "ToggleContexto");
                    Exigir(!principal.Panel2Collapsed && principal.Panel1.Width > 250, "Inspector disponible aun con biblioteca ampliada");
                    primero.GetType().GetField("IsModified").SetValue(primero, false);
                    segundo.GetType().GetField("IsModified").SetValue(segundo, false);
                    Llamar(form, "CerrarTab", primero);
                    Exigir(Campo(form, "_activeTab") == segundo && editor2.Visible, "Cerrar una pestaña conserva la otra");
                    Llamar(form, "CerrarTab", segundo);
                    var nuevo = Campo(form, "_activeTab");
                    Exigir(nuevo != segundo && ((Scintilla)Campo(nuevo, "Editor")).Text == "", "Cerrar la ultima pestaña crea un editor vacio utilizable");
                    form.Close();
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
