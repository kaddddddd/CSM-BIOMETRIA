using System.Windows;
using CSMBiometricoWPF.Models;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class JustificarAsistenciaDialog : Window
    {
        public string Observacion { get; private set; }

        public JustificarAsistenciaDialog(RegistroIngreso registro)
        {
            InitializeComponent();
            lblInfo.Text = $"{registro.NombreEstudiante}  ·  {registro.FechaIngreso:dd/MM/yyyy}  ·  {registro.EstadoStr}";
            txtObservacion.Text = registro.Observaciones ?? "";
            txtObservacion.Focus();
            txtObservacion.SelectAll();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            Observacion  = txtObservacion.Text.Trim();
            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
