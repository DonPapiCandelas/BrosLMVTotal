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

// CreadorDocumentos.cs -- abre la ventana «Crear documentos desde XML» SIN abrir Comercial. Usa BrosLMV.Runner, que arranca el motor de
// CONTPAQi por su cuenta (probado en el laboratorio: el documento queda igual que uno creado dentro de Comercial, a nombre del usuario
// que se elija). El Runner toma los XML del pedido que deja Descargas (crear_doc_pedido.json), carga la plantilla CREAR_DOC_DESDE_XML de
// la base de Comercial de la empresa y no termina hasta que se cierre la ventana.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    internal static class CreadorDocumentos
    {
        public const string RutaRunner = @"C:\BrosLMV\runner\BrosLMV.Runner.exe";
        public static bool Disponible => File.Exists(RutaRunner);

        private static string ArchivoUsuario(string rfc) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrosLMV", "Descargas", "usuario_comercial_" + rfc + ".txt");

        // Usuarios activos de Comercial (los documentos quedan a nombre del que se elija).
        public static List<KeyValuePair<int, string>> Usuarios(string conexionCifrada)
        {
            var r = new List<KeyValuePair<int, string>>();
            using (var cn = new SqlConnection(DpapiHelper.DescifrarConexionSql(conexionCifrada)))
            {
                cn.Open();
                using (var cmd = new SqlCommand("SELECT UserID, UserName FROM engUser WHERE DeletedOn IS NULL ORDER BY UserID", cn))
                using (var l = cmd.ExecuteReader())
                    while (l.Read()) r.Add(new KeyValuePair<int, string>(Convert.ToInt32(l[0]), Convert.ToString(l[1])));
            }
            return r;
        }

        // Pide el usuario (recordando el ultimo usado para esa empresa). Devuelve 0 si se cancela.
        public static int ElegirUsuario(Window dueno, string rfc, string conexionCifrada)
        {
            var usuarios = Usuarios(conexionCifrada);
            if (usuarios.Count == 0) throw new InvalidOperationException("No hay usuarios activos en la base de Comercial de esta empresa.");
            int recordado = 0;
            try { int.TryParse(File.ReadAllText(ArchivoUsuario(rfc)).Trim(), out recordado); } catch { }
            if (usuarios.Count == 1) return usuarios[0].Key;

            var combo = new ComboBox { ItemsSource = usuarios, DisplayMemberPath = "Value", SelectedValuePath = "Key", Margin = new Thickness(0, 8, 0, 0) };
            combo.SelectedValue = usuarios.Any(u => u.Key == recordado) ? recordado : usuarios[0].Key;
            int elegido = 0;
            var ventana = new Window
            {
                Title = "Crear documentos", Owner = dueno, WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.Height, Width = 440,
                ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)Application.Current.FindResource("PincelLienzo")
            };
            var aceptar = new Button { Content = "Continuar", Style = (Style)Application.Current.FindResource("Primary"), Margin = new Thickness(0, 0, 8, 0) };
            var cancelar = new Button { Content = "Cancelar", Style = (Style)Application.Current.FindResource("Ghost"), IsCancel = true };
            aceptar.Click += (s, e) => { elegido = combo.SelectedValue is int v ? v : 0; ventana.DialogResult = true; };
            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            botones.Children.Add(aceptar); botones.Children.Add(cancelar);
            var panel = new StackPanel { Margin = new Thickness(26) };
            panel.Children.Add(new TextBlock { Text = "¿A nombre de qué usuario de Comercial se crean los documentos?", Style = (Style)Application.Current.FindResource("TextoSubtitulo"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "Es el usuario que verás como creador en Comercial. Se recuerda para la próxima vez.", Style = (Style)Application.Current.FindResource("TextoSecundario"), Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(combo); panel.Children.Add(botones);
            ventana.Content = panel;
            if (ventana.ShowDialog() != true || elegido == 0) return 0;
            try { Directory.CreateDirectory(Path.GetDirectoryName(ArchivoUsuario(rfc))); File.WriteAllText(ArchivoUsuario(rfc), elegido.ToString()); } catch { }
            return elegido;
        }

        // Lanza el Runner. Si termina con error, avisa con lo que dijo (no bloquea la aplicacion mientras la ventana esta abierta).
        public static void Abrir(Window dueno, string conexionCifrada, int userId, Action alTerminar)
        {
            string bd = new SqlConnectionStringBuilder(DpapiHelper.DescifrarConexionSql(conexionCifrada)).InitialCatalog;
            if (string.IsNullOrWhiteSpace(bd)) throw new InvalidOperationException("La conexión a Comercial de esta empresa no indica la base de datos.");
            var psi = new ProcessStartInfo(RutaRunner, "--appkey CREAR_DOC_DESDE_XML --bd \"" + bd + "\" --userid " + userId)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(RutaRunner)
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var salida = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (salida) salida.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (salida) salida.AppendLine(e.Data); };
            p.Exited += (s, e) =>
            {
                int codigo = 0; try { codigo = p.ExitCode; } catch { }
                string texto; lock (salida) texto = salida.ToString().Trim();
                dueno.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // El Runner imprime «OK (...)» cuando el script termino bien; un codigo de salida raro al cerrar la ventana no es un fallo.
                    bool termino = texto.Contains("OK (");
                    if (codigo != 0 && !termino)
                        MessageBox.Show(dueno, "No se pudo abrir la creación de documentos.\n\n" + Explicar(codigo, texto), "Crear documentos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    alTerminar?.Invoke();
                }));
            };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }

        private static string Explicar(int codigo, string texto)
        {
            if (codigo == 3 || texto.Contains("AppKey desconocido"))
                return "La plantilla «Crear documentos desde XML» no está instalada en la base de Comercial de esta empresa. Instala o actualiza BrosLMV (versión 3.2.0 o posterior) y actívala en esa empresa.";
            if (codigo == 5) return "La plantilla no está marcada para abrirse fuera de Comercial. Actualiza BrosLMV a la versión 3.2.0 o posterior.";
            if (codigo == 7) return "La plantilla fue modificada fuera de la Consola de BrosLMV. Ábrela en la Consola, revísala y guárdala de nuevo.";
            if (codigo == 6) return "La plantilla requiere aprobación en la Consola de BrosLMV antes de ejecutarse.";
            var lineas = texto.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            return lineas.Length == 0 ? "El programa terminó con el código " + codigo + "." : string.Join("\n", lineas.Skip(Math.Max(0, lineas.Length - 6)));
        }
    }
}
