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

// ConsolaPasswordPromptForm.cs -- dialogo modal que bloquea la apertura de la Consola hasta
// que se escriba la contrasena correcta (zzBrosConsolaPass, configurada desde el instalador).
// Reusa el mismo lenguaje visual que el resto de la Consola (AppTheme, BordeTarjeta,
// BordeInferior, IconButton) en vez de un dialogo WinForms generico.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrosLMV
{
    internal sealed class ConsolaPasswordPromptForm : Form
    {
        private readonly TextBox _txtPassword;
        private readonly Label _lblError;
        private readonly Func<string, bool> _verificar;

        public string PasswordCorrecta { get; private set; }

        private ConsolaPasswordPromptForm(Func<string, bool> verificar)
        {
            _verificar = verificar;
            Text = "BrosLMV";
            ClientSize = new Size(360, 236);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = true;
            BackColor = AppTheme.BgMain;
            Font = AppTheme.FontMain;

            // ---- Cabecera: mismo patron que NuevaAccionForm (panel BgChrome + borde inferior) ----
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = AppTheme.BgChrome, Padding = new Padding(20, 0, 20, 0) };
            pnlHeader.Paint += (s, e) => BrosConsola.BordeInferior(e.Graphics, pnlHeader);
            var lblTitulo = new Label
            {
                Text = "Contraseña de la Consola",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.FontHeader,
                ForeColor = AppTheme.TextMain,
                BackColor = Color.Transparent
            };
            pnlHeader.Controls.Add(lblTitulo);

            // ---- Cuerpo ----
            var pnlBody = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgMain, Padding = new Padding(20, 18, 20, 0) };

            var lblCaption = new Label
            {
                Text = "Esta empresa requiere contraseña para abrir la Consola.",
                Dock = DockStyle.Top,
                Height = 20,
                Font = AppTheme.FontSmall,
                ForeColor = AppTheme.TextMuted
            };

            // Campo con borde redondeado propio (mismo patron que la caja de busqueda del
            // arbol de scripts): TextBox SIN borde nativo, dentro de un Panel que dibuja
            // BordeTarjeta -- asi se ve igual de pulido que el resto de la app, no el
            // TextBox gris con relieve por defecto de Windows.
            var pnlCampo = new Panel { Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 10, 0, 0), BackColor = AppTheme.BgSurface, Padding = new Padding(10, 0, 10, 0) };
            pnlCampo.Paint += (s, e) => BrosConsola.BordeTarjeta(e.Graphics, pnlCampo);
            var espCampo = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Color.Transparent };
            _txtPassword = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = AppTheme.BgSurface,
                ForeColor = AppTheme.TextMain,
                Font = AppTheme.FontMain,
                UseSystemPasswordChar = true
            };
            var pnlCampoInner = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BgSurface, Padding = new Padding(0, 8, 0, 0) };
            pnlCampoInner.Controls.Add(_txtPassword);
            pnlCampo.Controls.Add(pnlCampoInner);

            _lblError = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(0, 6, 0, 0),
                ForeColor = AppTheme.Error,
                Font = AppTheme.FontSmall
            };

            // Orden inverso (Dock=Top apila hacia arriba -- ver nota T4.1 en otros forms de este archivo).
            pnlBody.Controls.Add(_lblError);
            pnlBody.Controls.Add(espCampo);
            pnlBody.Controls.Add(pnlCampo);
            pnlBody.Controls.Add(lblCaption);

            // ---- Pie: botones ----
            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = AppTheme.BgMain, Padding = new Padding(20, 0, 20, 16) };
            var btnEntrar = new IconButton { Text = "Entrar", Kind = BtnKind.Primary, Accent = AppTheme.Success, Dock = DockStyle.Right, Width = 110 };
            btnEntrar.Click += (s, e) => Intentar();
            var btnCancelar = new IconButton { Text = "Cancelar", Kind = BtnKind.Toolbar, Accent = Color.Empty, Dock = DockStyle.Right, Width = 100, Margin = new Padding(0, 0, 10, 0) };
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlFooter.Controls.Add(btnEntrar);
            pnlFooter.Controls.Add(btnCancelar);

            Controls.Add(pnlBody);
            Controls.Add(pnlFooter);
            Controls.Add(pnlHeader);

            AcceptButton = btnEntrar;
            CancelButton = btnCancelar;
            Shown += (s, e) => _txtPassword.Focus();
        }

        private void Intentar()
        {
            string intento = _txtPassword.Text;
            if (_verificar(intento))
            {
                PasswordCorrecta = intento;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblError.Text = "Contraseña incorrecta.";
                _txtPassword.SelectAll();
                _txtPassword.Focus();
            }
        }

        // true = acceso concedido (contrasena correcta). false = cancelado o rechazado -- el
        // llamador NO debe abrir la Consola en ese caso.
        public static bool PedirYVerificar(Func<string, bool> verificar)
        {
            using (var f = new ConsolaPasswordPromptForm(verificar))
                return f.ShowDialog() == DialogResult.OK;
        }
    }
}
