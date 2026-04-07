using System;
using System.Windows;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarSedeDialog : Window
    {
        private readonly SedeRepository _repo = new();
        private readonly Sede _sede;

        public EditarSedeDialog(Sede? sede)
        {
            InitializeComponent();
            _sede = sede ?? new Sede { Estado = true };

            // Cargar instituciones en el ComboBox
            cmbInstitucion.DisplayMemberPath = "Nombre";
            cmbInstitucion.SelectedValuePath = "IdInstitucion";
            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                cmbInstitucion.ItemsSource = new[] { SesionActiva.InstitucionActual };
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.Visibility = System.Windows.Visibility.Collapsed;
                lblInstitucionLabel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                try
                {
                    cmbInstitucion.ItemsSource = new InstitucionRepository().ObtenerTodas(soloActivas: true);
                }
                catch { }
            }

            if (sede != null)
            {
                lblTitulo.Text        = "Editar Sede";
                Title                 = "Editar Sede";
                txtNombre.Text        = sede.NombreSede;
                txtDireccion.Text     = sede.Direccion;
                chkEstado.IsChecked   = sede.Estado;
                cmbInstitucion.SelectedValue = sede.IdInstitucion;
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                MessageBox.Show("El nombre de la sede es obligatorio.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }
            if (cmbInstitucion.SelectedItem is not Institucion inst)
            {
                MessageBox.Show("Seleccione una institución.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _sede.NombreSede   = txtNombre.Text.Trim();
            _sede.IdInstitucion = inst.IdInstitucion;
            _sede.Direccion    = txtDireccion.Text.Trim();
            _sede.Estado       = chkEstado.IsChecked == true;

            try
            {
                _repo.Guardar(_sede);
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
