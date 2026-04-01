using System;
using System.Windows;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarGradoDialog : Window
    {
        private readonly GradoRepository _repo = new();
        private readonly Grado _grado;

        public EditarGradoDialog(Grado? grado)
        {
            InitializeComponent();
            _grado = grado ?? new Grado { Estado = true };

            if (grado != null)
            {
                lblTitulo.Text  = "Editar Grado";
                Title           = "Editar Grado";
                txtNombre.Text  = grado.NombreGrado;
                txtOrden.Text   = grado.OrdenGrado.ToString();
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                MessageBox.Show("El nombre es obligatorio.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }
            if (!int.TryParse(txtOrden.Text.Trim(), out int orden) || orden < 1)
            {
                MessageBox.Show("El orden debe ser un número entero positivo.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtOrden.Focus();
                return;
            }

            _grado.NombreGrado = txtNombre.Text.Trim();
            _grado.OrdenGrado  = orden;

            try
            {
                _repo.Guardar(_grado);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
