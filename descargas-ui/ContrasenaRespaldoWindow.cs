using System.Windows;
using System.Windows.Controls;

namespace BrosLMV.DescargasUI
{
    // Pide la contrasena del respaldo (dos veces al exportar, una al importar). Ventana construida en codigo.
    public class ContrasenaRespaldoWindow : Window
    {
        private readonly PasswordBox _a = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
        private readonly PasswordBox _b = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
        private readonly bool _confirmar;
        public string Contrasena => _a.Password;

        public ContrasenaRespaldoWindow(bool confirmar, string titulo)
        {
            _confirmar = confirmar;
            Title = titulo; Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock
            {
                Text = confirmar
                    ? "Elige una contraseña para proteger el respaldo (mínimo 8 caracteres). Si la olvidas, el respaldo no se puede abrir: no hay forma de recuperarla."
                    : "Escribe la contraseña con la que se creó el respaldo.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
            });
            panel.Children.Add(new TextBlock { Text = "Contraseña", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(_a);
            if (confirmar)
            {
                panel.Children.Add(new TextBlock { Text = "Repite la contraseña", Margin = new Thickness(0, 0, 0, 4) });
                panel.Children.Add(_b);
            }
            var ok = new Button { Content = "Aceptar", IsDefault = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 6, 8, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 6, 0, 0) };
            ok.Click += (s, e) =>
            {
                if (_a.Password.Length < 8 && _confirmar) { MessageBox.Show(this, "La contraseña debe tener al menos 8 caracteres.", "BrosLMV"); return; }
                if (_confirmar && _a.Password != _b.Password) { MessageBox.Show(this, "Las contraseñas no coinciden.", "BrosLMV"); return; }
                if (_a.Password.Length == 0) return;
                DialogResult = true;
            };
            var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            botones.Children.Add(ok); botones.Children.Add(cancelar);
            panel.Children.Add(botones);
            Content = panel;
        }
    }
}
