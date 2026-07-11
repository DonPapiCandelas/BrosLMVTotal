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

// HostClient.cs — lado CLIENTE del canal v3.0 (C6), dentro del addon (ComercialSP).
// Lanza BrosLMV.Host.exe (x64), abre el Named Pipe, hace el handshake (token) y manda
// ExecuteScript para correr un script Python. Habla el mismo broslmv.proto que el host.
// Atiende Completed/Failed, callbacks SQL (ContextCall) y UI (UiRequest, v2.18.0+).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Google.Protobuf;
using BrosLMV.Protocol;

namespace BrosLMV
{
    public sealed class HostClient
    {
        public const uint ProtocolVersion = 1;

        public sealed class Resultado
        {
            public bool Exito;
            public string Valor = "";
            public string CodigoError = "";
            public string MensajeError = "";
            public long ElapsedMs;
            public int FilasAfectadas;
        }

        // Contexto vivo del botón que se congela y viaja al script.
        public sealed class Contexto
        {
            public string AppKey = "";
            public string Empresa = "";
            public string Servidor = "";
            public string BaseDatos = "";
            public int UserId;
            public int ModuleId;
            public long[] SelectedIds = new long[0];
            public bool SoloLectura;
            public string Language = "python";
            public Dictionary<string, object> FilaActiva;
        }

        // Ejecutor de SQL sobre la conexión VIVA de CONTPAQi (relay C6c). Lo implementa el
        // addon a partir de ScriptContext; el host se lo pide por callback. Devuelve los
        // mismos tipos que ctx.Query/Scalar/NonQuery (con el SQL ya con parámetros aplicados).
        public interface ISqlRunner
        {
            List<Dictionary<string, object>> Query(string sql);
            object Scalar(string sql);
            int Execute(string sql);
        }

        // Ejecutor de ctx.erp.* para Python: el host reenvía la llamada (método + args) y el
        // addon la corre sobre su ErpContext en proceso (engine vivo). Es lo que le da a Python
        // el mismo poder que C# (existencias, folios, recalcular, precios...) sin copiar terceros.
        public interface IErpRunner
        {
            object Invoke(string method, object[] args);
        }

        // Cómo arrancar el host. Producción: el .exe self-contained en C:\BrosLMV\host.
        // Desarrollo/test: FileName="dotnet" y ArgsPrefix = ruta del BrosLMV.Host.dll.
        public sealed class Lanzador
        {
            public string FileName = "";
            public string ArgsPrefix = "";
            public static Lanzador Default()
            {
                return new Lanzador
                {
                    FileName = Path.Combine(Rutas.Base, "host", "BrosLMV.Host.exe"),
                    ArgsPrefix = ""
                };
            }
        }

        // Detección de lenguaje compartida (la usan el dispatch de botón y la consola).
        // Un script es Python si su 1a línea no vacía trae un marcador.
        public static bool EsPython(string codigo)
        {
            if (string.IsNullOrEmpty(codigo)) return false;
            foreach (var raw in codigo.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string low = line.ToLowerInvariant();
                return low.Contains("broslmv:python")
                    || low.StartsWith("#py")
                    || low.StartsWith("# lang: python")
                    || low.StartsWith("#lang:python");
            }
            return false;
        }

        // Detección del tipo de script 'sql' (T-SQL crudo). Marcador en la 1a línea.
        public static bool EsSql(string codigo)
        {
            if (string.IsNullOrEmpty(codigo)) return false;
            foreach (var raw in codigo.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string low = line.ToLowerInvariant();
                return low.Contains("broslmv:sql")
                    || low.StartsWith("--sql")
                    || low.StartsWith("-- lang: sql")
                    || low.StartsWith("# lang: sql")
                    || low.StartsWith("#sql");
            }
            return false;
        }

        // Directiva opcional de cabecera para scripts con UI interactiva (WinForms real via
        // pythonnet, ctx.form, etc.) que necesitan más que el timeout de seguridad por default.
        // Formato: "# timeout: 1800" (segundos) en cualquiera de las primeras líneas.
        // Si no está presente, se usa defaultMs (120000 = mismo límite de siempre, sin cambios
        // para scripts normales). Evita debilitar el "fail fast" para todo lo demás.
        private static readonly System.Text.RegularExpressions.Regex RxTimeoutHeader =
            new System.Text.RegularExpressions.Regex(@"^\s*#\s*timeout\s*:\s*(\d+)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static int TimeoutMsFromHeader(string codigo, int defaultMs = 120000)
        {
            if (string.IsNullOrEmpty(codigo)) return defaultMs;
            foreach (var raw in codigo.Split('\n'))
            {
                var m = RxTimeoutHeader.Match(raw);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int segundos) && segundos > 0)
                    return segundos * 1000;
            }
            return defaultMs;
        }

        // Ejecuta un script Python a través del host y devuelve el resultado.
        // sqlRunner: si se pasa, el SQL de Python (ctx.query/scalar/execute) se corre en la
        // conexión viva de CONTPAQi (relay C6c). Si es null, el SQL falla controlado.
        public static Resultado EjecutarPython(string codigo, Contexto ctx, Lanzador lanzador = null, int timeoutMs = 120000, ISqlRunner sqlRunner = null, IErpRunner erpRunner = null)
        {
            lanzador = lanzador ?? Lanzador.Default();
            string pipe = "BrosLMV." + Guid.NewGuid().ToString("N");
            string token = Guid.NewGuid().ToString();

            string args = (string.IsNullOrEmpty(lanzador.ArgsPrefix) ? "" : lanzador.ArgsPrefix + " ")
                        + "--serve --pipe " + pipe + " --token " + token;

            var psi = new ProcessStartInfo
            {
                FileName = lanzador.FileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process proc = null;
            var stderrBuf = new System.Text.StringBuilder();
            try
            {
                proc = Process.Start(psi);
                if (proc == null) return Fail("HOST_START_FAILED", "No se pudo iniciar BrosLMV.Host.");

                // Drenar stdout/stderr del host de forma asíncrona: si nadie los lee, el
                // pipe redirigido se puede llenar y bloquear la escritura del propio host
                // (deadlock). De paso, si el host truena, su stderr trae el motivo real.
                proc.OutputDataReceived += (s, e) => { };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) lock (stderrBuf) stderrBuf.AppendLine(e.Data);
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                var task = Task.Run(() => Intercambio(pipe, token, codigo, ctx ?? new Contexto(), sqlRunner, erpRunner, timeoutMs));
                // Margen sobre el timeout del propio script (el host también lo limita).
                if (!task.Wait(timeoutMs + 15000))
                    return Fail("HOST_TIMEOUT", "El host no respondió a tiempo.");
                return task.Result;
            }
            catch (Exception ex)
            {
                // task.Wait() envuelve la excepcion real en AggregateException, cuyo
                // .Message generico ("Se han producido uno o varios errores.") no sirve
                // para diagnosticar. Se desenvuelve para mostrar la causa de verdad.
                Exception real = (ex as AggregateException)?.Flatten().InnerException ?? ex.InnerException ?? ex;
                string detalle = real.Message;
                string stderrTxt;
                lock (stderrBuf) stderrTxt = stderrBuf.ToString();
                if (!string.IsNullOrWhiteSpace(stderrTxt))
                    detalle += "  |  host stderr: " + stderrTxt.Trim();
                return Fail("HOST_CLIENT_ERROR", detalle);
            }
            finally
            {
                TryKill(proc);
            }
        }

        private static Resultado Intercambio(string pipe, string token, string codigo, Contexto ctx, ISqlRunner sqlRunner, IErpRunner erpRunner, int timeoutMs)
        {
            using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                client.Connect(10000);

                // 1) Handshake con token.
                WriteFrame(client, new Envelope
                {
                    ProtocolVersion = ProtocolVersion,
                    RequestId = Guid.NewGuid().ToString(),
                    AuthToken = token,
                    Hello = new Hello { Runtime = "python", RuntimeVersion = "3.13", ProtocolVersion = ProtocolVersion }
                });

                Envelope hello = ReadFrame(client);
                if (hello == null || hello.MessageCase != Envelope.MessageOneofCase.HelloResponse || !hello.HelloResponse.Accepted)
                    return Fail("HANDSHAKE_FAILED", "El host rechazó el handshake.");

                // 2) ExecuteScript con el contexto congelado.
                WriteFrame(client, new Envelope
                {
                    ProtocolVersion = ProtocolVersion,
                    RequestId = Guid.NewGuid().ToString(),
                    ExecutionId = Guid.NewGuid().ToString(),
                    ExecuteScript = new ExecuteScript { Code = codigo ?? "", Context = ToProtoContext(ctx), TimeoutMs = timeoutMs }
                });

                // 3) Bucle de sesión: el host puede pedir SQL en vivo (ContextCall) durante
                //    la ejecución; respondemos con la conexión viva. Termina en Completed/Failed.
                while (true)
                {
                    Envelope msg = ReadFrame(client);
                    if (msg == null) return Fail("HOST_CLOSED", "El host cerró sin responder.");

                    if (msg.MessageCase == Envelope.MessageOneofCase.ContextCall)
                    {
                        ContextResponse resp = AtenderContextCall(msg.ContextCall, sqlRunner, erpRunner);
                        WriteFrame(client, new Envelope
                        {
                            ProtocolVersion = ProtocolVersion,
                            RequestId = msg.RequestId,
                            ContextResponse = resp
                        });
                        continue;
                    }
                    if (msg.MessageCase == Envelope.MessageOneofCase.ExecutionCompleted)
                    {
                        var c = msg.ExecutionCompleted;
                        return new Resultado
                        {
                            Exito = true,
                            Valor = c.ReturnValue != null ? c.ReturnValue.StringValue : "",
                            ElapsedMs = c.ElapsedMs,
                            FilasAfectadas = c.RowsAffected
                        };
                    }
                    if (msg.MessageCase == Envelope.MessageOneofCase.ExecutionFailed)
                    {
                        Error e = msg.ExecutionFailed.Error;
                        return Fail(e != null ? e.Code : "PYTHON_ERROR", e != null ? e.Message : "Error en Python.");
                    }
                    if (msg.MessageCase == Envelope.MessageOneofCase.UiRequest)
                    {
                        UiResponse uiResp = AtenderUiRequest(msg.UiRequest);
                        WriteFrame(client, new Envelope
                        {
                            ProtocolVersion = ProtocolVersion,
                            RequestId = msg.RequestId,
                            UiResponse = uiResp
                        });
                        continue;
                    }
                    // ProgressEvent / LogEvent se registran en el log del addon.
                }
            }
        }

        // Atiende un callback del host: SQL en vivo (ctx.query/scalar/execute) o ERP (ctx.erp.*).
        private static ContextResponse AtenderContextCall(ContextCall call, ISqlRunner sqlRunner, IErpRunner erpRunner)
        {
            if (call.OpCase == ContextCall.OpOneofCase.Erp)
            {
                if (erpRunner == null)
                    return ErrResp("NO_ERP", "Este botón no tiene contexto ERP (ctx.erp) disponible.");
                try
                {
                    var args = new object[call.Erp.Args.Count];
                    for (int i = 0; i < args.Length; i++) args[i] = FromValueClr(call.Erp.Args[i]);
                    object res = erpRunner.Invoke(call.Erp.Method ?? "", args);
                    return new ContextResponse { Value = ToValue(res) };
                }
                catch (Exception ex)
                {
                    return ErrResp("ERP_ERROR", ex.Message);
                }
            }

            if (call.OpCase != ContextCall.OpOneofCase.Live)
                return ErrResp("UNSUPPORTED", "Callback no soportado: " + call.OpCase);
            if (sqlRunner == null)
                return ErrResp("NO_SQL", "Este botón no tiene conexión viva para SQL.");

            LiveSql live = call.Live;
            try
            {
                string sql = AplicarParams(live.Sql ?? "", live.Params);
                switch (live.Mode)
                {
                    case SqlMode.SqlScalar:
                        return new ContextResponse { Value = ToValue(sqlRunner.Scalar(sql)) };
                    case SqlMode.SqlNonQuery:
                        return new ContextResponse { Value = ToValue(sqlRunner.Execute(sql)) };
                    default: // SqlQuery / SqlQueryOne
                        return new ContextResponse { Table = ToTable(sqlRunner.Query(sql)) };
                }
            }
            catch (Exception ex)
            {
                return ErrResp("SQL_ERROR", ex.Message);
            }
        }

        private static ContextResponse ErrResp(string code, string msg) =>
            new ContextResponse { Error = new Error { Code = code, Message = msg } };

        // Atiende un callback de UI del host: ctx.msg / ctx.confirm desde Python.
        // El host envía UiRequest; el addon muestra la UI (MessageBox) y devuelve UiResponse.
        private static UiResponse AtenderUiRequest(UiRequest req)
        {
            try
            {
                if (req.Msg != null)
                {
                    var icon = req.Msg.Icon switch
                    {
                        MessageIcon.IconWarning  => System.Windows.Forms.MessageBoxIcon.Warning,
                        MessageIcon.IconError    => System.Windows.Forms.MessageBoxIcon.Error,
                        MessageIcon.IconQuestion => System.Windows.Forms.MessageBoxIcon.Question,
                        _                        => System.Windows.Forms.MessageBoxIcon.Information
                    };
                    System.Windows.Forms.MessageBox.Show(req.Msg.Text ?? "",
                        req.Msg.Title ?? "BrosLMV", MessageBoxButtons.OK, icon);
                    return new UiResponse { Ok = new Empty() };
                }
                if (req.Confirm != null)
                {
                    var result = System.Windows.Forms.MessageBox.Show(req.Confirm.Text ?? "",
                        req.Confirm.Title ?? "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    return new UiResponse { Confirmed = result == DialogResult.Yes };
                }
                if (req.Form != null)
                    return RenderUiForm(req.Form);
                if (req.ShowHtml != null)
                    return RenderUiHtml(req.ShowHtml);
                return new UiResponse { Error = new Error { Code = "UI_NOT_IMPL", Message = "Tipo de UI no implementado aún." } };
            }
            catch (Exception ex)
            {
                return new UiResponse { Error = new Error { Code = "UI_ERROR", Message = ex.Message } };
            }
        }

        private static UiResponse RenderUiForm(UiForm spec)
        {
            var result = new FormResult { Submitted = false };
            using (var frm = new Form())
            using (var layout = new TableLayoutPanel())
            using (var buttons = new FlowLayoutPanel())
            {
                frm.Text = string.IsNullOrWhiteSpace(spec.Title) ? "BrosLMV" : spec.Title;
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.FormBorderStyle = FormBorderStyle.FixedDialog;
                frm.MaximizeBox = false;
                frm.MinimizeBox = false;
                frm.ShowIcon = false;
                frm.Font = new System.Drawing.Font("Segoe UI", 9f);
                frm.Width = spec.Width > 0 ? spec.Width : 720;
                frm.Height = spec.Height > 0 ? spec.Height : Math.Min(760, 150 + Math.Max(1, spec.Fields.Count) * 58);

                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(14);
                layout.ColumnCount = 2;
                layout.RowCount = spec.Fields.Count + 1;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                frm.Controls.Add(layout);

                var controls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
                int row = 0;
                foreach (var field in spec.Fields)
                {
                    string labelText = field.Label;
                    if (field.Required && !labelText.EndsWith("*")) labelText += " *";

                    var lbl = new Label
                    {
                        Text = labelText,
                        AutoSize = false,
                        Dock = DockStyle.Fill,
                        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                        ForeColor = System.Drawing.Color.FromArgb(17, 24, 39)
                    };
                    Control ctl = CreateFormControl(field);
                    ctl.Dock = DockStyle.Fill;
                    ctl.Enabled = !field.ReadOnly;

                    int h = field.Type == FieldType.FtMemo ? 82 : 34;
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
                    layout.Controls.Add(lbl, 0, row);
                    layout.Controls.Add(ctl, 1, row);
                    controls[field.Name] = ctl;
                    row++;
                }

                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Padding = new Padding(0, 8, 0, 0);

                var ok = new Button
                {
                    Text = string.IsNullOrWhiteSpace(spec.OkLabel) ? "Aceptar" : spec.OkLabel,
                    Width = 120,
                    Height = 32,
                    DialogResult = DialogResult.None
                };
                var cancel = new Button
                {
                    Text = string.IsNullOrWhiteSpace(spec.CancelLabel) ? "Cancelar" : spec.CancelLabel,
                    Width = 100,
                    Height = 32,
                    DialogResult = DialogResult.Cancel
                };

                ok.Click += (_, __) =>
                {
                    foreach (var field in spec.Fields)
                    {
                        if (!field.Required) continue;
                        if (!controls.TryGetValue(field.Name, out var ctl)) continue;
                        object val = ReadFormControl(field, ctl);
                        if (val == null || string.IsNullOrWhiteSpace(Convert.ToString(val, CultureInfo.InvariantCulture)))
                        {
                            MessageBox.Show("Captura " + field.Label + ".", frm.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            ctl.Focus();
                            return;
                        }
                    }
                    frm.DialogResult = DialogResult.OK;
                    frm.Close();
                };

                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
                layout.SetColumnSpan(buttons, 2);
                layout.Controls.Add(buttons, 0, row);

                frm.AcceptButton = ok;
                frm.CancelButton = cancel;

                if (frm.ShowDialog() == DialogResult.OK)
                {
                    result.Submitted = true;
                    foreach (var field in spec.Fields)
                    {
                        if (!controls.TryGetValue(field.Name, out var ctl)) continue;
                        result.Values[field.Name] = ToValue(ReadFormControl(field, ctl));
                    }
                }
            }

            return new UiResponse { FormResult = result };
        }

        // ctx.show_html: ventana con un control WebView2 (Edge/Chromium, ya viene con
        // Windows 10/11) mostrando el HTML/CSS/JS que mande el script.
        //
        // WebView2 exige un hilo COM en modo STA (apartment de un solo hilo). El hilo que
        // atiende cada conexion del pipe (donde corre AtenderUiRequest) NO esta garantizado
        // en STA -- si algo ya lo inicializo como MTA antes, crear el WebView2 ahi truena con
        // RPC_E_CHANGED_MODE ("no se puede cambiar el modo de subproceso"), confirmado en
        // pruebas reales. Por eso esto SIEMPRE corre en un hilo STA dedicado y nuevo.
        private static UiResponse RenderUiHtml(UiShowHtml spec)
        {
            string perfil = Path.Combine(Path.GetTempPath(), "BrosLMV_WebView2_" + Guid.NewGuid().ToString("N"));
            Exception hiloEx = null;
            var listo = new System.Threading.ManualResetEventSlim(false);

            var hilo = new System.Threading.Thread(() =>
            {
                try
                {
                    var frm = new Form();
                    var webView = new Microsoft.Web.WebView2.WinForms.WebView2();
                    frm.Text = string.IsNullOrWhiteSpace(spec.Title) ? "BrosLMV" : spec.Title;
                    frm.StartPosition = FormStartPosition.CenterScreen;
                    frm.Width = spec.Width > 0 ? spec.Width : 800;
                    frm.Height = spec.Height > 0 ? spec.Height : 600;

                    webView.Dock = DockStyle.Fill;
                    frm.Controls.Add(webView);

                    // EnsureCoreWebView2Async necesita que YA haya un bucle de mensajes
                    // bombeando en este hilo -- por eso la carga se dispara desde Load (que
                    // solo se activa una vez que ShowDialog()/Application.Run() ya esta
                    // corriendo), nunca con .GetAwaiter().GetResult() antes de eso. Hacerlo
                    // antes se cuelga para siempre (deadlock confirmado en pruebas reales:
                    // WebView2 espera una continuacion que nadie bombea).
                    frm.Load += async (s, e) =>
                    {
                        try
                        {
                            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                                .CreateAsync(userDataFolder: perfil);
                            await webView.EnsureCoreWebView2Async(env);
                            webView.CoreWebView2.NavigateToString(spec.Html ?? "");
                        }
                        catch (Exception ex)
                        {
                            hiloEx = ex;
                            frm.Close();
                        }
                        finally
                        {
                            listo.Set();
                        }
                    };

                    if (spec.Modal)
                    {
                        frm.ShowDialog();
                        webView.Dispose();
                        frm.Dispose();
                        try { Directory.Delete(perfil, true); } catch { /* limpieza best-effort */ }
                    }
                    else
                    {
                        frm.FormClosed += (s, e) =>
                        {
                            webView.Dispose();
                            frm.Dispose();
                            try { Directory.Delete(perfil, true); } catch { /* limpieza best-effort */ }
                            Application.ExitThread();
                        };
                        frm.Show();
                        Application.Run(frm);
                    }
                }
                catch (Exception ex)
                {
                    hiloEx = ex;
                    listo.Set();
                }
            });
            hilo.SetApartmentState(System.Threading.ApartmentState.STA);
            hilo.IsBackground = true;
            hilo.Start();

            // Espera solo a que la pagina cargue (o falle) -- NO a que el usuario cierre la
            // ventana. 'modal' controla si la ventana bloquea otras ventanas de Comercial
            // mientras esta abierta (ShowDialog vs Show), no si bloquea al script Python.
            listo.Wait();

            if (hiloEx != null)
                return new UiResponse { Error = new Error { Code = "SHOW_HTML_ERROR", Message = hiloEx.Message } };
            return new UiResponse { Ok = new Empty() };
        }

        private static Control CreateFormControl(FormField field)
        {
            object def = FromValueClr(field.DefaultValue);
            switch (field.Type)
            {
                case FieldType.FtNumber:
                {
                    var n = new NumericUpDown { Maximum = 999999999, Minimum = -999999999, DecimalPlaces = 0, TextAlign = HorizontalAlignment.Right };
                    if (def != null && decimal.TryParse(Convert.ToString(def, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                        n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
                    return n;
                }
                case FieldType.FtDecimal:
                {
                    var n = new NumericUpDown { Maximum = 999999999, Minimum = -999999999, DecimalPlaces = 2, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
                    if (def != null && decimal.TryParse(Convert.ToString(def, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                        n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
                    return n;
                }
                case FieldType.FtDate:
                {
                    var d = new DateTimePicker { Format = DateTimePickerFormat.Short };
                    if (def != null && DateTime.TryParse(Convert.ToString(def, CultureInfo.InvariantCulture), out var dt))
                        d.Value = dt;
                    return d;
                }
                case FieldType.FtBool:
                    return new CheckBox { Checked = def is bool b && b, Text = "" };
                case FieldType.FtCombo:
                {
                    var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    foreach (var opt in field.Options)
                        cbo.Items.Add(new UiComboItem(opt.Label, FromValueClr(opt.Value)));
                    if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
                    if (def != null)
                    {
                        for (int i = 0; i < cbo.Items.Count; i++)
                        {
                            var item = cbo.Items[i] as UiComboItem;
                            if (Equals(Convert.ToString(item?.Value, CultureInfo.InvariantCulture), Convert.ToString(def, CultureInfo.InvariantCulture)))
                            {
                                cbo.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    return cbo;
                }
                case FieldType.FtMemo:
                    return new TextBox { Text = Convert.ToString(def, CultureInfo.InvariantCulture) ?? "", Multiline = true, ScrollBars = ScrollBars.Vertical };
                default:
                    return new TextBox { Text = Convert.ToString(def, CultureInfo.InvariantCulture) ?? "" };
            }
        }

        private static object ReadFormControl(FormField field, Control ctl)
        {
            if (ctl is NumericUpDown n)
                return field.Type == FieldType.FtNumber ? (object)Convert.ToInt32(n.Value) : n.Value;
            if (ctl is DateTimePicker d) return d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (ctl is CheckBox c) return c.Checked;
            if (ctl is ComboBox cbo && cbo.SelectedItem is UiComboItem item) return item.Value;
            return ctl.Text;
        }

        private sealed class UiComboItem
        {
            public readonly string Label;
            public readonly object Value;
            public UiComboItem(string label, object value) { Label = label ?? ""; Value = value; }
            public override string ToString() => Label;
        }

        // Sustituye @parametros por literales SQL (la conexión viva ejecuta texto plano).
        private static string AplicarParams(string sql, Google.Protobuf.Collections.MapField<string, Value> pars)
        {
            if (pars == null || pars.Count == 0) return sql;
            foreach (var kv in pars)
            {
                string lit = EncodeLiteral(kv.Value);
                sql = Regex.Replace(sql, "@" + Regex.Escape(kv.Key) + @"\b", lit.Replace("$", "$$"));
            }
            return sql;
        }

        private static string EncodeLiteral(Value v)
        {
            switch (v.KindCase)
            {
                case Value.KindOneofCase.None: return "NULL";
                case Value.KindOneofCase.BoolValue: return v.BoolValue ? "1" : "0";
                case Value.KindOneofCase.Int32Value: return v.Int32Value.ToString(CultureInfo.InvariantCulture);
                case Value.KindOneofCase.Int64Value: return v.Int64Value.ToString(CultureInfo.InvariantCulture);
                case Value.KindOneofCase.DecimalValue: return v.DecimalValue;            // numérico, sin comillas
                case Value.KindOneofCase.DoubleValue: return v.DoubleValue.ToString(CultureInfo.InvariantCulture);
                case Value.KindOneofCase.StringValue: return "N'" + v.StringValue.Replace("'", "''") + "'";
                case Value.KindOneofCase.DateValue: return "'" + v.DateValue + "'";
                case Value.KindOneofCase.TimeValue: return "'" + v.TimeValue + "'";
                case Value.KindOneofCase.DatetimeValue: return "'" + v.DatetimeValue + "'";
                case Value.KindOneofCase.GuidValue: return "'" + v.GuidValue + "'";
                default: return "N'" + (v.ToString() ?? "").Replace("'", "''") + "'";
            }
        }

        // Value (del host/Python) -> CLR, para pasar args a ErpContext. La coerción fina al
        // tipo de cada parámetro (int/double/string...) la hace CtxErpRunner por reflexión.
        private static object FromValueClr(Value v)
        {
            if (v == null) return null;
            switch (v.KindCase)
            {
                case Value.KindOneofCase.None: return null;
                case Value.KindOneofCase.BoolValue: return v.BoolValue;
                case Value.KindOneofCase.Int32Value: return v.Int32Value;
                case Value.KindOneofCase.Int64Value: return v.Int64Value;
                case Value.KindOneofCase.DecimalValue: return decimal.Parse(v.DecimalValue, CultureInfo.InvariantCulture);
                case Value.KindOneofCase.DoubleValue: return v.DoubleValue;
                case Value.KindOneofCase.StringValue: return v.StringValue;
                case Value.KindOneofCase.BytesValue: return v.BytesValue.ToByteArray();
                case Value.KindOneofCase.DateValue: return v.DateValue;
                case Value.KindOneofCase.DatetimeValue: return v.DatetimeValue;
                case Value.KindOneofCase.GuidValue: return v.GuidValue;
                default: return v.ToString();
            }
        }

        // ---- Value/Table <-> CLR (lado addon; espejo de ValueCodec del host) ----
        private static Value ToValue(object clr)
        {
            if (clr == null || clr is DBNull) return new Value();
            if (clr is bool b) return new Value { BoolValue = b };
            if (clr is int i) return new Value { Int32Value = i };
            if (clr is long l) return new Value { Int64Value = l };
            if (clr is decimal m) return new Value { DecimalValue = m.ToString(CultureInfo.InvariantCulture) };
            if (clr is double d) return new Value { DoubleValue = d };
            if (clr is float f) return new Value { DoubleValue = f };
            if (clr is byte[] by) return new Value { BytesValue = ByteString.CopyFrom(by) };
            if (clr is DateTime dt) return new Value { DatetimeValue = dt.ToString("O", CultureInfo.InvariantCulture) };
            if (clr is Guid g) return new Value { GuidValue = g.ToString("D") };
            if (clr is string s) return new Value { StringValue = s };
            return new Value { StringValue = Convert.ToString(clr, CultureInfo.InvariantCulture) ?? "" };
        }

        private static Table ToTable(List<Dictionary<string, object>> rows)
        {
            var t = new Table();
            if (rows == null || rows.Count == 0) return t;
            foreach (var col in rows[0].Keys) t.Columns.Add(new Column { Name = col });
            foreach (var row in rows)
            {
                var r = new Row();
                foreach (var col in t.Columns)
                    r.Cells.Add(ToValue(row.ContainsKey(col.Name) ? row[col.Name] : null));
                t.Rows.Add(r);
            }
            return t;
        }

        private static BrosLMV.Protocol.ExecutionContext ToProtoContext(Contexto ctx)
        {
            var pc = new ExecutionContext
            {
                AppKey = string.IsNullOrEmpty(ctx.AppKey) ? "" : ctx.AppKey,
                Empresa = string.IsNullOrEmpty(ctx.Empresa) ? "" : ctx.Empresa,
                Servidor = string.IsNullOrEmpty(ctx.Servidor) ? "" : ctx.Servidor,
                BaseDatos = string.IsNullOrEmpty(ctx.BaseDatos) ? "" : ctx.BaseDatos,
                UserId = ctx.UserId,
                ModuleId = ctx.ModuleId,
                Language = string.IsNullOrEmpty(ctx.Language) ? "python" : ctx.Language,
                SoloLectura = ctx.SoloLectura
            };
            if (ctx.SelectedIds != null)
                pc.SelectedIds.AddRange(ctx.SelectedIds);
            if (ctx.FilaActiva != null)
            {
                foreach (var kvp in ctx.FilaActiva)
                {
                    pc.FilaActiva[kvp.Key] = ToValue(kvp.Value);
                }
            }
            return pc;
        }

        // ---- framing [4 bytes longitud big-endian][Envelope protobuf] (igual que el host) ----
        private static void WriteFrame(Stream s, Envelope env)
        {
            byte[] payload = env.ToByteArray();
            byte[] header = new byte[4];
            header[0] = (byte)(payload.Length >> 24);
            header[1] = (byte)(payload.Length >> 16);
            header[2] = (byte)(payload.Length >> 8);
            header[3] = (byte)payload.Length;
            s.Write(header, 0, 4);
            s.Write(payload, 0, payload.Length);
            s.Flush();
        }

        private static Envelope ReadFrame(Stream s)
        {
            byte[] header = ReadExact(s, 4);
            if (header == null) return null;
            int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (len < 0 || len > 64 * 1024 * 1024) throw new IOException("Longitud de frame inválida: " + len);
            byte[] payload = ReadExact(s, len);
            if (payload == null) throw new EndOfStreamException("Stream cortado a mitad de frame.");
            return Envelope.Parser.ParseFrom(payload);
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            if (count == 0) return new byte[0];
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int n = s.Read(buffer, offset, count - offset);
                if (n == 0) return offset == 0 ? null : throw new EndOfStreamException("EOF inesperado a mitad de frame.");
                offset += n;
            }
            return buffer;
        }

        private static Resultado Fail(string code, string message) =>
            new Resultado { Exito = false, CodigoError = code, MensajeError = message };

        private static void TryKill(Process proc)
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            try { if (proc != null) proc.Dispose(); } catch { }
        }
    }

    // Evita lanzar dos ejecuciones Python superpuestas del mismo botón/AppKey: si el usuario
    // hace doble clic (o clic mientras "no pasa nada" en pantalla), cada clic lanzaba su propio
    // BrosLMV.Host.exe, y todos competían por el mismo hilo de Comercial vía UiPump -- eso es lo
    // que producía tiempos crecientes y el diálogo nativo "the other application is busy"
    // (confirmado con la traza de PythonErp_*.txt: ejecuciones con ExecutionId distinto y
    // ventanas de tiempo encimadas en host-audit.jsonl).
    internal static class GuardiaEjecucion
    {
        private static readonly object _lock = new object();
        private static readonly System.Collections.Generic.HashSet<string> _enCurso =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool TryEntrar(string appKey)
        {
            string k = appKey ?? "";
            lock (_lock) { return _enCurso.Add(k); } // false si ya estaba
        }

        internal static void Salir(string appKey)
        {
            lock (_lock) { _enCurso.Remove(appKey ?? ""); }
        }
    }

    // Traza de diagnóstico para llamadas Python->UiPump que se marshalan al hilo de Comercial.
    // Escribe ANTES de la llamada bloqueante (para poder ver, si algo se atora, cuál fue la
    // última llamada en curso) y DESPUÉS con el tiempo que tardó. No depende de la conexión SQL
    // ni de COM: es solo un archivo de texto, así que funciona incluso si la llamada se cuelga.
    internal static class TrazaErp
    {
        internal static void Escribir(string texto)
        {
            try
            {
                if (!System.IO.Directory.Exists(Rutas.Logs)) System.IO.Directory.CreateDirectory(Rutas.Logs);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Rutas.Logs, "PythonErp_" + DateTime.Now.ToString("yyyyMMdd") + ".txt"),
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + texto + Environment.NewLine);
            }
            catch { }
        }
    }

    // Adaptador que corre el SQL del relay sobre la conexión viva de un ScriptContext.
    // Es lo que hace que un "SELECT * FROM docDocumento" desde Python funcione igual que en C#.
    internal sealed class CtxSqlRunner : HostClient.ISqlRunner
    {
        private readonly ScriptContext _ctx;
        public CtxSqlRunner(ScriptContext ctx) { _ctx = ctx; }
        // El botón Python corre en segundo plano (ClsMain.EjecutarPython); estas llamadas
        // se remiten al hilo de Comercial vía UiPump, que es el único que debe tocar el COM.
        public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Query(string sql) => TrazadoSql("Query", sql, () => UiPump.Invoke(() => _ctx.Query(sql)));
        public object Scalar(string sql) => TrazadoSql("Scalar", sql, () => UiPump.Invoke(() => _ctx.Scalar(sql)));
        public int Execute(string sql) => TrazadoSql("Execute", sql, () => UiPump.Invoke(() => _ctx.NonQuery(sql)));

        private static T TrazadoSql<T>(string op, string sql, Func<T> f)
        {
            string resumen = (sql ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (resumen.Length > 120) resumen = resumen.Substring(0, 120) + "...";
            TrazaErp.Escribir("SQL." + op + " INICIA: " + resumen);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return f(); }
            finally { sw.Stop(); TrazaErp.Escribir("SQL." + op + " termina en " + sw.ElapsedMilliseconds + " ms"); }
        }
    }

    // Corre ctx.erp.* de Python sobre el ErpContext del addon. Resuelve el método por reflexión
    // (sin distinguir mayúsculas) para usar los wrappers tipados (con sus correcciones, p. ej. el
    // orden de GetPriceWithTaxes). Si no hay wrapper, cae a ctx.erp.Call(...) (los 562 de XEngine).
    internal sealed class CtxErpRunner : HostClient.IErpRunner
    {
        private readonly ScriptContext _ctx;
        public CtxErpRunner(ScriptContext ctx) { _ctx = ctx; }

        // El botón Python corre en segundo plano (ClsMain.EjecutarPython); se remite al
        // hilo de Comercial vía UiPump, que es el único que debe tocar el COM de XEngine.
        // Traza antes/después: si una llamada se cuelga (p. ej. el "busy" de XEngine), el log
        // ya tiene escrito el método+args que quedó en curso, sin esperar a que termine.
        public object Invoke(string method, object[] args)
        {
            string resumenArgs = "";
            try { resumenArgs = string.Join(", ", System.Linq.Enumerable.Select(args ?? new object[0], a => Convert.ToString(a))); } catch { }
            TrazaErp.Escribir("ERP." + method + "(" + resumenArgs + ") INICIA");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return UiPump.Invoke(() => InvokeCore(method, args)); }
            finally { sw.Stop(); TrazaErp.Escribir("ERP." + method + " termina en " + sw.ElapsedMilliseconds + " ms"); }
        }

        private object InvokeCore(string method, object[] args)
        {
            if (string.IsNullOrEmpty(method))
                throw new Exception("ctx.erp: falta el nombre del método.");

            ErpContext erp = _ctx.erp;
            args = args ?? new object[0];

            // 0) Genéricos explícitos desde Python: ctx.erp.Get("PROP") y ctx.erp.Call("Metodo", ...).
            if (string.Equals(method, "Get", StringComparison.OrdinalIgnoreCase) && args.Length >= 1)
                return erp.Get(Convert.ToString(args[0], CultureInfo.InvariantCulture));
            if (string.Equals(method, "Call", StringComparison.OrdinalIgnoreCase) && args.Length >= 1)
            {
                string real = Convert.ToString(args[0], CultureInfo.InvariantCulture);
                var rest = new object[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return erp.Call(real, rest);
            }

            // 1) Wrapper tipado por nombre (case-insensitive) cuyos parámetros encajen con los args.
            foreach (var mi in erp.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!string.Equals(mi.Name, method, StringComparison.OrdinalIgnoreCase)) continue;
                if (mi.Name == "Call" || mi.Name == "Get") continue; // genéricos: se usan como fallback

                var ps = mi.GetParameters();
                int requeridos = 0;
                foreach (var p in ps) if (!p.HasDefaultValue) requeridos++;
                if (args.Length < requeridos || args.Length > ps.Length) continue;

                object[] finales = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i < args.Length)
                    {
                        try { finales[i] = Coerce(args[i], ps[i].ParameterType); }
                        catch { ok = false; break; }
                    }
                    else finales[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                }
                if (!ok) continue;

                object ret = mi.Invoke(erp, finales);
                return mi.ReturnType == typeof(void) ? null : ret;
            }

            // 2) Propiedad de ErpContext por nombre (UserId, ComercialRFC, ...): en Python se
            //    llama como ctx.erp.UserId() (sin args), pero en C# es una propiedad.
            if (args.Length == 0)
            {
                var pi = erp.GetType().GetProperty(method,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (pi != null && pi.CanRead) return pi.GetValue(erp);
            }

            // 3) Fallback genérico: cualquier miembro de XEngine por nombre.
            return erp.Call(method, args);
        }

        private static object Coerce(object v, Type t)
        {
            if (t.IsInstanceOfType(v)) return v;
            if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            Type nt = Nullable.GetUnderlyingType(t) ?? t;
            if (nt == typeof(string)) return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (nt.IsEnum) return Enum.ToObject(nt, Convert.ToInt32(v, CultureInfo.InvariantCulture));
            return Convert.ChangeType(v, nt, CultureInfo.InvariantCulture);
        }
    }
}
