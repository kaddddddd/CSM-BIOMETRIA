using System;
using System.Linq;
using System.Windows;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarUsuarioDialog : Window
    {
        private readonly UsuarioRepository _repo = new();
        private readonly Usuario _usuario;

        public EditarUsuarioDialog(Usuario? usuario)
        {
            InitializeComponent();
            _usuario = usuario ?? new Usuario { Estado = true };

            CargarCombos();

            if (usuario != null)
            {
                lblTitulo.Text        = "Editar Usuario";
                Title                 = "Editar Usuario";
                txtNombre.Text        = usuario.NombreCompleto;
                txtUsername.Text      = usuario.Username;
                txtEmail.Text         = usuario.Email;
                chkEstado.IsChecked   = usuario.Estado;
                lblPasswordHint.Text  = "Dejar en blanco para no cambiar la contraseña.";
                lblPassword.Text      = "Nueva contraseña (opcional)";

                // Seleccionar rol y institución
                foreach (var item in cmbRol.Items)
                    if (item is Rol r && r.IdRol == usuario.IdRol)
                    { cmbRol.SelectedItem = r; break; }

                if (usuario.IdInstitucion.HasValue)
                    foreach (var item in cmbInstitucion.Items)
                        if (item is Institucion i && i.IdInstitucion == usuario.IdInstitucion.Value)
                        { cmbInstitucion.SelectedItem = i; break; }
            }
        }

        private void CargarCombos()
        {
            // Roles (excluir SUPERADMIN)
            cmbRol.Items.Clear();
            try
            {
                var roles = _repo.ObtenerRoles()
                                 .Where(r => !r.NombreRol.Equals("SUPERADMIN", StringComparison.OrdinalIgnoreCase));
                foreach (var r in roles) cmbRol.Items.Add(r);
            }
            catch { }
            cmbRol.DisplayMemberPath = "NombreRol";

            // Instituciones
            cmbInstitucion.Items.Clear();
            cmbInstitucion.DisplayMemberPath = "Nombre";
            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.Visibility = System.Windows.Visibility.Collapsed;
                lblInstitucionLabel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                cmbInstitucion.Items.Add(new Institucion { IdInstitucion = 0, Nombre = "— Ninguna —" });
                try
                {
                    foreach (var inst in new InstitucionRepository().ObtenerTodas(soloActivas: true))
                        cmbInstitucion.Items.Add(inst);
                }
                catch { }
                cmbInstitucion.SelectedIndex = 0;
            }

            if (cmbRol.Items.Count > 0) cmbRol.SelectedIndex = 0;
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                MessageBox.Show("El nombre completo es obligatorio.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus(); return;
            }
            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                MessageBox.Show("El nombre de usuario es obligatorio.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtUsername.Focus(); return;
            }
            if (cmbRol.SelectedItem is not Rol rol)
            {
                MessageBox.Show("Seleccione un rol.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }

            // Contraseña: obligatoria en nuevo usuario
            string password = pwdPassword.Password;
            if (_usuario.IdUsuario == 0 && string.IsNullOrEmpty(password))
            {
                MessageBox.Show("La contraseña es obligatoria para nuevos usuarios.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }

            _usuario.NombreCompleto = txtNombre.Text.Trim();
            _usuario.Username       = txtUsername.Text.Trim();
            _usuario.Email          = txtEmail.Text.Trim();
            _usuario.IdRol          = rol.IdRol;
            _usuario.Estado         = chkEstado.IsChecked == true;

            var instSel = cmbInstitucion.SelectedItem as Institucion;
            _usuario.IdInstitucion = (instSel != null && instSel.IdInstitucion > 0)
                ? (int?)instSel.IdInstitucion : null;

            try
            {
                _repo.Guardar(_usuario, string.IsNullOrEmpty(password) ? null : password);
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
