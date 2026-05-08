using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class EditarInstitucionDialog : Window
    {
        private readonly InstitucionRepository _repo = new();
        private readonly Institucion _inst;
        private string? _logoPathPendiente; // ruta de archivo origen seleccionado

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

                if (!string.IsNullOrEmpty(inst.LogoPath))
                {
                    string logoFull = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "Images", "logos", inst.LogoPath);
                    if (File.Exists(logoFull))
                    {
                        imgLogoPreview.Source = new BitmapImage(new Uri(logoFull));
                        lblLogoNombre.Text    = inst.LogoPath;
                    }
                }
            }
        }

        private void BtnSeleccionarLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Seleccionar logo",
                Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                imgLogoPreview.Source = new BitmapImage(new Uri(dlg.FileName));
                lblLogoNombre.Text    = Path.GetFileName(dlg.FileName);
                _logoPathPendiente    = dlg.FileName;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("No se pudo cargar la imagen: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                CustomMessageBox.Show("El nombre es obligatorio.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }

            _inst.Nombre    = txtNombre.Text.Trim();
            _inst.Direccion = txtDireccion.Text.Trim();
            _inst.Telefono  = txtTelefono.Text.Trim();
            _inst.Estado    = chkEstado.IsChecked == true;

            // Copiar logo si se seleccionó uno nuevo
            if (!string.IsNullOrEmpty(_logoPathPendiente))
            {
                try
                {
                    string logosDir = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "Images", "logos");
                    Directory.CreateDirectory(logosDir);

                    string ext      = Path.GetExtension(_logoPathPendiente);
                    string fileName = $"inst_{Guid.NewGuid():N}{ext}";
                    string destino  = Path.Combine(logosDir, fileName);
                    File.Copy(_logoPathPendiente, destino, overwrite: true);
                    _inst.LogoPath = fileName;
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show("No se pudo guardar el logo: " + ex.Message, "Advertencia",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            try
            {
                _repo.Guardar(_inst);
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
