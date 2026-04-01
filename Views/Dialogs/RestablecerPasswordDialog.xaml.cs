using System;
using System.Windows;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class RestablecerPasswordDialog : Window
    {
        private readonly UsuarioRepository _repo = new();

        public RestablecerPasswordDialog()
        {
            InitializeComponent();
        }

        private void BtnCambiar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                MessageBox.Show("Ingrese el nombre de usuario.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtUsername.Focus(); return;
            }
            if (pwdNueva.Password.Length < 6)
            {
                MessageBox.Show("La contraseña debe tener al menos 6 caracteres.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdNueva.Focus(); return;
            }
            if (pwdNueva.Password != pwdConfirmar.Password)
            {
                MessageBox.Show("Las contraseñas no coinciden.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                pwdConfirmar.Focus(); return;
            }

            try
            {
                var usuario = _repo.BuscarPorUsername(txtUsername.Text.Trim());
                if (usuario == null)
                {
                    MessageBox.Show("No se encontró un usuario con ese nombre.", "Usuario no encontrado",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _repo.CambiarPassword(usuario.IdUsuario, pwdNueva.Password);
                MessageBox.Show("Contraseña actualizada correctamente.", "Éxito",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error",
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
