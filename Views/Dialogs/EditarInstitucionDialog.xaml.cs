using System;
using System.Windows;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarInstitucionDialog : Window
    {
        private readonly InstitucionRepository _repo = new();
        private readonly Institucion _inst;

        public EditarInstitucionDialog(Institucion? inst)
        {
            InitializeComponent();
            _inst = inst ?? new Institucion { Estado = true };

            if (inst != null)
            {
                lblTitulo.Text      = "Editar Institución";
                Title               = "Editar Institución";
                txtNombre.Text      = inst.Nombre;
                txtDireccion.Text   = inst.Direccion;
                txtTelefono.Text    = inst.Telefono;
                chkEstado.IsChecked = inst.Estado;
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

            _inst.Nombre    = txtNombre.Text.Trim();
            _inst.Direccion = txtDireccion.Text.Trim();
            _inst.Telefono  = txtTelefono.Text.Trim();
            _inst.Estado    = chkEstado.IsChecked == true;

            try
            {
                _repo.Guardar(_inst);
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
