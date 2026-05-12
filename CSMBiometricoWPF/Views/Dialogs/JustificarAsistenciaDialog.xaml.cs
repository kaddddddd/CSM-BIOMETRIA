using System.Windows;
using System.Windows.Media;
using CSMBiometricoWPF.Models;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class JustificarAsistenciaDialog : Window
    {
        public string        Observacion { get; private set; }
        public string        Motivo      { get; private set; }
        public EstadoIngreso NuevoEstado { get; private set; }

        private static readonly string[] _motivos =
        {
            "Incapacidad médica",
            "Permiso autorizado",
            "Calamidad doméstica",
            "Actividad institucional",
            "Problema de transporte",
            "Otro"
        };

        public JustificarAsistenciaDialog(RegistroIngreso registro)
        {
            InitializeComponent();

            NuevoEstado = registro.EstadoIngreso;

            lblNombre.Text      = registro.NombreEstudiante;
            lblFecha.Text       = registro.FechaIngreso.ToString("dddd dd 'de' MMMM 'de' yyyy");
            lblEstado.Text      = registro.EstadoStr.ToUpper();
            txtObservacion.Text = registro.Observaciones ?? "";

            cmbMotivo.ItemsSource   = _motivos;
            cmbMotivo.SelectedIndex = 0;

            // Color del badge y opción de corrección según estado
            switch (registro.EstadoIngreso)
            {
                case EstadoIngreso.A_TIEMPO:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                    break;
                case EstadoIngreso.TARDE:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));
                    pnlCorreccion.Visibility = Visibility.Visible;
                    break;
                case EstadoIngreso.FUERA_DE_HORARIO:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    pnlCorreccion.Visibility = Visibility.Visible;
                    break;
                default:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                    break;
            }

            ActualizarContador();
            txtObservacion.TextChanged += (_, _) => ActualizarContador();
            txtObservacion.Focus();
        }

        private void ActualizarContador()
        {
            int len = txtObservacion.Text.Length;
            lblContador.Text = $"{len} / 200 caracteres";
            lblContador.Foreground = len >= 180
                ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))
                : new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (cmbMotivo.SelectedItem == null)
            {
                cmbMotivo.BorderBrush = System.Windows.Media.Brushes.Red;
                return;
            }
            Motivo      = cmbMotivo.SelectedItem.ToString()!;
            Observacion = txtObservacion.Text.Trim();
            if (chkCorregirATiempo.IsChecked == true)
                NuevoEstado = EstadoIngreso.A_TIEMPO;
            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
