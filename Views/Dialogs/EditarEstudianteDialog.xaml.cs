using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarEstudianteDialog : Window
    {
        private readonly EstudianteRepository _repo = new();
        private readonly InstitucionRepository _instRepo = new();
        private readonly SedeRepository _sedeRepo = new();
        private readonly GradoRepository _gradoRepo = new();
        private readonly GrupoRepository _grupoRepo = new();
        private readonly Estudiante _est;
        private byte[]? _foto;
        private bool _cargandoCombos = false;

        public EditarEstudianteDialog(Estudiante? est)
        {
            InitializeComponent();
            _est  = est ?? new Estudiante { Estado = "ACTIVO" };
            _foto = est?.Foto;

            CargarCombos();

            if (est != null)
            {
                lblTitulo.Text        = "Editar Estudiante";
                Title                 = "Editar Estudiante";
                txtNombre.Text        = est.Nombre;
                txtApellidos.Text     = est.Apellidos;
                txtIdentificacion.Text = est.Identificacion;
                txtTelefono.Text      = est.Telefono;
                txtEmail.Text         = est.Email;
                cmbEstado.SelectedItem = est.Estado;
                cmbInstitucion.SelectedValue = est.IdInstitucion;
                // Sedes se cargan en el evento Changed
            }

            MostrarFoto();
        }

        private void CargarCombos()
        {
            _cargandoCombos = true;

            // Institución
            cmbInstitucion.DisplayMemberPath = "Nombre";
            cmbInstitucion.SelectedValuePath = "IdInstitucion";
            cmbInstitucion.Items.Clear();
            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // El usuario está vinculado a una institución: preseleccionarla y ocultar el selector
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.Visibility = System.Windows.Visibility.Collapsed;
                lblInstitucionLabel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                try
                {
                    foreach (var i in _instRepo.ObtenerTodas(soloActivas: true))
                        cmbInstitucion.Items.Add(i);
                }
                catch { }
            }

            // Grado
            cmbGrado.Items.Clear();
            try
            {
                foreach (var g in _gradoRepo.ObtenerTodos())
                    cmbGrado.Items.Add(g);
            }
            catch { }
            cmbGrado.DisplayMemberPath = "NombreGrado";
            cmbGrado.SelectedValuePath = "IdGrado";

            // Grupo
            cmbGrupo.Items.Clear();
            try
            {
                foreach (var g in _grupoRepo.ObtenerTodos())
                    cmbGrupo.Items.Add(g);
            }
            catch { }
            cmbGrupo.DisplayMemberPath = "NombreGrupo";
            cmbGrupo.SelectedValuePath = "IdGrupo";

            // Estado
            cmbEstado.Items.Clear();
            cmbEstado.Items.Add("ACTIVO");
            cmbEstado.Items.Add("INACTIVO");
            cmbEstado.Items.Add("RETIRADO");
            cmbEstado.SelectedIndex = 0;

            _cargandoCombos = false;

            // Si hay estudiante, restaurar valores dependientes
            if (_est.IdInstitucion > 0)
            {
                cmbInstitucion.SelectedValue = _est.IdInstitucion;
                CargarSedes(_est.IdInstitucion);
                cmbSede.SelectedValue  = _est.IdSede;
                cmbGrado.SelectedValue = _est.IdGrado;
                cmbGrupo.SelectedValue = _est.IdGrupo;
                cmbEstado.SelectedItem = _est.Estado;
            }
            else if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // Institución preseleccionada (usuario no-SuperAdmin) y estudiante nuevo: cargar sedes
                CargarSedes(SesionActiva.InstitucionActual.IdInstitucion);
            }
        }

        private void CargarSedes(int idInstitucion)
        {
            cmbSede.Items.Clear();
            try
            {
                foreach (var s in _sedeRepo.ObtenerPorInstitucion(idInstitucion))
                    cmbSede.Items.Add(s);
            }
            catch { }
            cmbSede.DisplayMemberPath = "NombreSede";
            cmbSede.SelectedValuePath = "IdSede";
            if (cmbSede.Items.Count > 0) cmbSede.SelectedIndex = 0;
        }

        private void CmbInstitucion_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoCombos) return;
            if (cmbInstitucion.SelectedItem is Institucion inst)
                CargarSedes(inst.IdInstitucion);
        }

        private void BtnFoto_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp|Todos los archivos|*.*",
                Title  = "Seleccionar foto del estudiante"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _foto = File.ReadAllBytes(dlg.FileName);
                MostrarFoto();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error cargando imagen: " + ex.Message);
            }
        }

        private void MostrarFoto()
        {
            if (_foto == null || _foto.Length == 0) { imgFoto.Source = null; return; }
            try
            {
                using var ms = new MemoryStream(_foto);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                imgFoto.Source = bi;
            }
            catch { imgFoto.Source = null; }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text) ||
                string.IsNullOrWhiteSpace(txtApellidos.Text) ||
                string.IsNullOrWhiteSpace(txtIdentificacion.Text))
            {
                CustomMessageBox.Show("Nombre, Apellidos e Identificación son obligatorios.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (cmbInstitucion.SelectedItem is not Institucion inst)
            {
                CustomMessageBox.Show("Seleccione una institución.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            if (cmbSede.SelectedItem is not Sede sede)
            {
                CustomMessageBox.Show("Seleccione una sede.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            if (cmbGrado.SelectedItem is not Grado grado)
            {
                CustomMessageBox.Show("Seleccione un grado.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            if (cmbGrupo.SelectedItem is not Grupo grupo)
            {
                CustomMessageBox.Show("Seleccione un grupo.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }

            _est.Nombre         = txtNombre.Text.Trim();
            _est.Apellidos      = txtApellidos.Text.Trim();
            _est.Identificacion = txtIdentificacion.Text.Trim();
            _est.Telefono       = txtTelefono.Text.Trim();
            _est.Email          = txtEmail.Text.Trim();
            _est.IdInstitucion  = inst.IdInstitucion;
            _est.IdSede         = sede.IdSede;
            _est.IdGrado        = grado.IdGrado;
            _est.IdGrupo        = grupo.IdGrupo;
            _est.Estado         = cmbEstado.SelectedItem?.ToString() ?? "ACTIVO";
            _est.Foto           = _foto;

            try
            {
                _repo.Guardar(_est);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error al guardar: " + ex.Message, "Error",
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
