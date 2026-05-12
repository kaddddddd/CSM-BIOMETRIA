using System.Windows;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class ConfirmarCierreDialog : Window
    {
        public ConfirmarCierreDialog()
        {
            InitializeComponent();
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
