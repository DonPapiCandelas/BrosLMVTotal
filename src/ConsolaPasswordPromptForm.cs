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
            Width = 380;
            Height = 210;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = true;
            BackColor = AppTheme.BgMain;
            Font = AppTheme.FontMain;

            var lblTitulo = new Label
            {
                Text = "Contraseña de la Consola",
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(18, 16, 18, 0),
                Font = AppTheme.FontHeader,
                ForeColor = AppTheme.TextMain
            };

            _txtPassword = new TextBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(18, 8, 18, 0),
                Font = AppTheme.FontMain,
                UseSystemPasswordChar = true
            };
            var pnlCampo = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(18, 6, 18, 0) };
            pnlCampo.Controls.Add(_txtPassword);

            _lblError = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(18, 2, 18, 0),
                ForeColor = AppTheme.Error,
                Font = AppTheme.FontSmall
            };

            var pnlBotones = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(18, 8, 18, 8)
            };
            var btnEntrar = new IconButton { Text = "Entrar", Kind = BtnKind.Primary, Accent = AppTheme.Success, Width = 100, Height = 34 };
            btnEntrar.Click += (s, e) => Intentar();
            var btnCancelar = new IconButton { Text = "Cancelar", Kind = BtnKind.Toolbar, Accent = Color.Empty, Width = 100, Height = 34, Margin = new Padding(0, 0, 8, 0) };
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlBotones.Controls.Add(btnEntrar);
            pnlBotones.Controls.Add(btnCancelar);

            Controls.Add(_lblError);
            Controls.Add(pnlCampo);
            Controls.Add(lblTitulo);
            Controls.Add(pnlBotones);

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
