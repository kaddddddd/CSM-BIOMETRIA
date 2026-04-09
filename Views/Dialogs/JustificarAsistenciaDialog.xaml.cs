using System.Windows;
using System.Windows.Media;
using CSMBiometricoWPF.Models;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class JustificarAsistenciaDialog : Window
    {
        public string        Observacion { get; private set; }
        public EstadoIngreso NuevoEstado { get; private set; }

        public JustificarAsistenciaDialog(RegistroIngreso registro)
        {
            InitializeComponent();

            NuevoEstado = registro.EstadoIngreso; // por defecto no cambia

            lblNombre.Text = registro.NombreEstudiante;
            lblFecha.Text  = registro.FechaIngreso.ToString("dddd dd 'de' MMMM 'de' yyyy");
            lblEstado.Text = registro.EstadoStr.ToUpper();

            // Color del badge según estado
            switch (registro.EstadoIngreso)
            {
                case EstadoIngreso.TARDE:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));
                    break;
                case EstadoIngreso.FUERA_DE_HORARIO:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                    // Mostrar opción de corrección solo para registros fuera de horario
                    pnlCorreccion.Visibility = Visibility.Visible;
                    break;
                default:
                    bdgEstado.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
                    lblEstado.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                    break;
            }

            txtObservacion.Text = registro.Observaciones ?? "";
            ActualizarContador();

            txtObservacion.TextChanged += (_, _) => ActualizarContador();
            txtObservacion.Focus();
            txtObservacion.SelectAll();
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
            Observacion  = txtObservacion.Text.Trim();
            if (chkCorregirATiempo.IsChecked == true)
                NuevoEstado = EstadoIngreso.A_TIEMPO;
            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
