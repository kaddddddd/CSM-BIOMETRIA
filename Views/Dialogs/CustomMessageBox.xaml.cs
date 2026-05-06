using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CSMBiometricoWPF.Views.Dialogs
{
    // ── Modo interno del diálogo ─────────────────────────────────────────
    internal enum MsgMode { Standard, Prompt, MultilinePrompt, Progress, Wait }

    public partial class CustomMessageBox : Window
    {
        public  MessageBoxResult Result      { get; private set; } = MessageBoxResult.None;
        public  string           InputResult { get; private set; } = string.Empty;
        private MsgMode          _mode;

        // ── Constructor ──────────────────────────────────────────────────
        private CustomMessageBox(string message, string title,
            MessageBoxButton button, MessageBoxImage icon, MsgMode mode = MsgMode.Standard)
        {
            InitializeComponent();
            _mode            = mode;
            TitleText.Text   = string.IsNullOrEmpty(title) ? DefaultTitle(icon, mode) : title;
            MessageText.Text = message;
            ApplyIcon(icon);
            ApplyMode(mode);
            BuildButtons(button);
        }

        // ── Título por defecto ───────────────────────────────────────────
        private static string DefaultTitle(MessageBoxImage icon, MsgMode mode) => mode switch
        {
            MsgMode.Prompt or MsgMode.MultilinePrompt => "Ingresar datos",
            MsgMode.Progress                          => "Procesando\u2026",
            MsgMode.Wait                              => "Por favor espere",
            _ => icon switch
            {
                MessageBoxImage.Error       => "Error",
                MessageBoxImage.Warning     => "Advertencia",
                MessageBoxImage.Information => "Información",
                MessageBoxImage.Question    => "Confirmar",
                _                           => "Aviso"
            }
        };

        // ── Icono y color de acento ──────────────────────────────────────
        private void ApplyIcon(MessageBoxImage icon)
        {
            SolidColorBrush acento;

            switch (icon)
            {
                case MessageBoxImage.Error:
                    acento            = MkBrush("#C62828");
                    IconEllipse.Fill  = acento;
                    IconText.Text     = "✕";
                    IconText.FontSize = 20;
                    break;

                case MessageBoxImage.Warning:
                    acento            = MkBrush("#E65C00");
                    IconEllipse.Fill  = acento;
                    IconText.Text     = "!";
                    IconText.FontSize = 26;
                    break;

                case MessageBoxImage.Information:
                    acento             = MkBrush("#01579B");
                    IconEllipse.Fill   = acento;
                    IconText.Text      = "i";
                    IconText.FontSize  = 22;
                    IconText.FontStyle = FontStyles.Italic;
                    break;

                default: // Question / None
                    acento            = MkBrush("#007864");
                    IconEllipse.Fill  = MkBrush("#9E9E9E");
                    IconText.Text     = "?";
                    IconText.FontSize = 22;
                    break;
            }

            HeaderBorder.Background = acento;
            OuterBorder.BorderBrush = acento;
        }

        // ── Configurar elementos visibles según modo ─────────────────────
        private void ApplyMode(MsgMode mode)
        {
            switch (mode)
            {
                case MsgMode.Prompt:
                    SingleInput.Visibility = Visibility.Visible;
                    Loaded += (_, _) => SingleInput.Focus();
                    break;

                case MsgMode.MultilinePrompt:
                    MultiInput.Visibility = Visibility.Visible;
                    Loaded += (_, _) => MultiInput.Focus();
                    break;

                case MsgMode.Progress:
                    HideIcon();
                    ProgressPanel.Visibility = Visibility.Visible;
                    break;

                case MsgMode.Wait:
                    HideIcon();
                    WaitBar.Visibility      = Visibility.Visible;
                    BtnSeparator.Visibility = Visibility.Collapsed;
                    ButtonsPanel.Visibility = Visibility.Collapsed;
                    var grid = (Grid)((Border)Content).Child;
                    grid.RowDefinitions[3].Height = new GridLength(0);
                    break;
            }
        }

        private void HideIcon()
        {
            IconGrid.Visibility = Visibility.Collapsed;
            ColIcon.Width       = new GridLength(0);
        }

        // ── Botones ──────────────────────────────────────────────────────
        private void BuildButtons(MessageBoxButton button)
        {
            if (_mode == MsgMode.Wait) return;

            switch (button)
            {
                case MessageBoxButton.OK:
                    if (_mode is MsgMode.Prompt or MsgMode.MultilinePrompt)
                    {
                        AddBtn("Aceptar",  MessageBoxResult.OK,     primary: true,  isCancel: false);
                        AddBtn("Cancelar", MessageBoxResult.Cancel,  primary: false, isCancel: true);
                    }
                    else
                    {
                        AddBtn("Aceptar",  MessageBoxResult.OK,     primary: true,  isCancel: false);
                    }
                    break;

                case MessageBoxButton.OKCancel:
                    AddBtn("Aceptar",  MessageBoxResult.OK,     primary: true,  isCancel: false);
                    AddBtn("Cancelar", MessageBoxResult.Cancel,  primary: false, isCancel: true);
                    break;

                case MessageBoxButton.YesNo:
                    AddBtn("Sí", MessageBoxResult.Yes, primary: true,  isCancel: false);
                    AddBtn("No", MessageBoxResult.No,  primary: false, isCancel: true);
                    break;

                case MessageBoxButton.YesNoCancel:
                    AddBtn("Sí",       MessageBoxResult.Yes,    primary: true,  isCancel: false);
                    AddBtn("No",       MessageBoxResult.No,     primary: false, isCancel: false);
                    AddBtn("Cancelar", MessageBoxResult.Cancel,  primary: false, isCancel: true);
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
                if (_mode == MsgMode.Prompt)
                    InputResult = SingleInput.Text;
                else if (_mode == MsgMode.MultilinePrompt)
                    InputResult = MultiInput.Text;

                Result       = result;
                DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
            };
            ButtonsPanel.Children.Add(btn);
        }

        // ── Arrastre de ventana ──────────────────────────────────────────
        private void HeaderBorder_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        // ── Helper color ─────────────────────────────────────────────────
        private static SolidColorBrush MkBrush(string hex) =>
            new(ColorConverter.ConvertFromString(hex) is Color c ? c : Colors.Gray);

        // ── Asignar owner ────────────────────────────────────────────────
        private static void SetOwner(Window dlg)
        {
            try
            {
                var owner = Application.Current?.MainWindow;
                if (owner is { IsLoaded: true, IsVisible: true })
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        }

        // ════════════════════════════════════════════════════════════════
        //  API PÚBLICA
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Alerta / Confirmar / Sí-No / Sí-No-Cancelar.
        /// Equivalente a MessageBox.Show().
        /// </summary>
        public static MessageBoxResult Show(
            string           message,
            string           title  = "",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage  icon   = MessageBoxImage.None)
        {
            var dlg = new CustomMessageBox(message, title, button, icon);
            SetOwner(dlg);
            dlg.ShowDialog();
            return dlg.Result;
        }

        /// <summary>
        /// Diálogo con campo de texto de una línea (Prompt).
        /// Retorna el texto ingresado, o null si el usuario canceló.
        /// </summary>
        public static string? ShowPrompt(
            string message,
            string title        = "",
            string defaultValue = "")
        {
            var dlg = new CustomMessageBox(message, title,
                MessageBoxButton.OK, MessageBoxImage.Question, MsgMode.Prompt);
            dlg.SingleInput.Text = defaultValue;
            SetOwner(dlg);
            dlg.ShowDialog();
            return dlg.Result == MessageBoxResult.OK ? dlg.InputResult : null;
        }

        /// <summary>
        /// Diálogo con área de texto multilínea (Multi-line Prompt).
        /// Retorna el texto ingresado, o null si el usuario canceló.
        /// </summary>
        public static string? ShowMultilinePrompt(
            string message,
            string title        = "",
            string defaultValue = "")
        {
            var dlg = new CustomMessageBox(message, title,
                MessageBoxButton.OK, MessageBoxImage.Question, MsgMode.MultilinePrompt);
            dlg.MultiInput.Text = defaultValue;
            SetOwner(dlg);
            dlg.ShowDialog();
            return dlg.Result == MessageBoxResult.OK ? dlg.InputResult : null;
        }

        /// <summary>
        /// Diálogo de progreso determinado (0-100).
        /// Retorna un ProgressController para actualizar o cerrar el diálogo.
        /// </summary>
        public static ProgressController ShowProgress(
            string message,
            string title        = "",
            int    initialValue = 0)
        {
            var dlg = new CustomMessageBox(message, title,
                MessageBoxButton.OK, MessageBoxImage.None, MsgMode.Progress);
            dlg.TheProgressBar.Value = initialValue;
            SetOwner(dlg);
            dlg.Show();
            return new ProgressController(dlg);
        }

        /// <summary>
        /// Diálogo de espera indeterminado, sin botones.
        /// Retorna un ProgressController para cambiar el mensaje o cerrar el diálogo.
        /// </summary>
        public static ProgressController ShowWait(
            string message,
            string title = "")
        {
            var dlg = new CustomMessageBox(message, title,
                MessageBoxButton.OK, MessageBoxImage.None, MsgMode.Wait);
            SetOwner(dlg);
            dlg.Show();
            return new ProgressController(dlg);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Controlador para Progress / Wait
    // ════════════════════════════════════════════════════════════════════
    public class ProgressController
    {
        private readonly CustomMessageBox _dlg;
        internal ProgressController(CustomMessageBox dlg) => _dlg = dlg;

        /// <summary>Actualiza el valor de la barra (0-100) y opcionalmente la etiqueta.</summary>
        public void Update(int value, string? label = null)
        {
            _dlg.Dispatcher.Invoke(() =>
            {
                _dlg.TheProgressBar.Value = value;
                if (label is not null)
                    _dlg.ProgressLabel.Text = label;
            });
        }

        /// <summary>Cambia el mensaje principal del diálogo.</summary>
        public void SetMessage(string message)
            => _dlg.Dispatcher.Invoke(() => _dlg.MessageText.Text = message);

        /// <summary>Cierra el diálogo programáticamente.</summary>
        public void Close()
            => _dlg.Dispatcher.Invoke(_dlg.Close);
    }
}
