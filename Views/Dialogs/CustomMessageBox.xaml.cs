using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private CustomMessageBox(string message, string title,
            MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();
            TitleText.Text   = string.IsNullOrEmpty(title) ? DefaultTitle(icon) : title;
            MessageText.Text = message;
            ConfigureIcon(icon);
            ConfigureButtons(button);
        }

        // ── Título por defecto según tipo ────────────────────────────────
        private static string DefaultTitle(MessageBoxImage icon) => icon switch
        {
            MessageBoxImage.Error       => "Error",
            MessageBoxImage.Warning     => "Advertencia",
            MessageBoxImage.Information => "Información",
            MessageBoxImage.Question    => "Confirmar",
            _                           => "Aviso"
        };

        // ── Color de cabecera, icono y borde exterior ───────────────────
        private void ConfigureIcon(MessageBoxImage icon)
        {
            SolidColorBrush acento;

            switch (icon)
            {
                case MessageBoxImage.Error:
                    acento                  = Brush("#C62828");
                    IconEllipse.Fill        = acento;
                    IconText.Text           = "✕";
                    IconText.FontSize       = 20;
                    break;

                case MessageBoxImage.Warning:
                    acento                  = Brush("#E65C00");
                    IconEllipse.Fill        = acento;
                    IconText.Text           = "!";
                    IconText.FontSize       = 26;
                    break;

                case MessageBoxImage.Information:
                    acento                  = Brush("#01579B");
                    IconEllipse.Fill        = acento;
                    IconText.Text           = "i";
                    IconText.FontSize       = 22;
                    IconText.FontStyle      = System.Windows.FontStyles.Italic;
                    break;

                default: // Question / None  → teal del proyecto
                    acento                  = Brush("#007864");
                    IconEllipse.Fill        = Brush("#757575");
                    IconText.Text           = "?";
                    IconText.FontSize       = 24;
                    break;
            }

            HeaderBorder.Background  = acento;
            OuterBorder.BorderBrush  = acento;
        }

        // ── Botones según MessageBoxButton ───────────────────────────────
        private void ConfigureButtons(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    AddBtn("Aceptar",   MessageBoxResult.OK,     primary: true,  isCancel: false);
                    break;
                case MessageBoxButton.OKCancel:
                    AddBtn("Aceptar",   MessageBoxResult.OK,     primary: true,  isCancel: false);
                    AddBtn("Cancelar",  MessageBoxResult.Cancel,  primary: false, isCancel: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddBtn("Sí",        MessageBoxResult.Yes,    primary: true,  isCancel: false);
                    AddBtn("No",        MessageBoxResult.No,     primary: false, isCancel: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddBtn("Sí",        MessageBoxResult.Yes,    primary: true,  isCancel: false);
                    AddBtn("No",        MessageBoxResult.No,     primary: false, isCancel: false);
                    AddBtn("Cancelar",  MessageBoxResult.Cancel,  primary: false, isCancel: true);
                    break;
            }
        }

        private void AddBtn(string text, MessageBoxResult result, bool primary, bool isCancel)
        {
            var btn = new Button
            {
                Content  = text,
                Style    = (Style)FindResource(primary ? "MsgBtnPrimary" : "MsgBtnSecondary"),
                IsCancel = isCancel
            };
            btn.Click += (_, _) =>
            {
                Result       = result;
                DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
            };
            ButtonsPanel.Children.Add(btn);
        }

        // ── Helper color ─────────────────────────────────────────────────
        private static SolidColorBrush Brush(string hex) =>
            new(ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c
                ? c : Colors.Gray);

        // ── API pública (drop-in para MessageBox.Show) ───────────────────
        public static MessageBoxResult Show(
            string           message,
            string           title  = "",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage  icon   = MessageBoxImage.None)
        {
            var dlg = new CustomMessageBox(message, title, button, icon);
            try
            {
                var owner = Application.Current?.MainWindow;
                if (owner is { IsLoaded: true, IsVisible: true })
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; }

            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}
