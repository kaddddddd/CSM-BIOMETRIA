using System;
using System.Windows;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarGrupoDialog : Window
    {
        private readonly GrupoRepository _repo = new();
        private readonly Grupo _grupo;

        public EditarGrupoDialog(Grupo? grupo)
        {
            InitializeComponent();
            _grupo = grupo ?? new Grupo { Estado = true };

            if (grupo != null)
            {
                lblTitulo.Text = "Editar Grupo";
                Title          = "Editar Grupo";
                txtNombre.Text = grupo.NombreGrupo;
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

            _grupo.NombreGrupo = txtNombre.Text.Trim();

            try
            {
                _repo.Guardar(_grupo);
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
