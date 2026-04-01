using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views
{
    public partial class LoginWindow : Window
    {
        private List<Institucion> _instituciones = new();
        private readonly AuthService _auth = new();

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;

            // Placeholders
            txtPassword.PasswordChanged += (s, e) =>
                pwdPlaceholder.Visibility = txtPassword.Password.Length == 0
                    ? Visibility.Visible : Visibility.Collapsed;

            // Enter para login
            txtUsuario.KeyDown += (s, e) => { if (e.Key == Key.Enter) BtnLogin_Click(s, e); };
            txtPassword.KeyDown += (s, e) => { if (e.Key == Key.Enter) BtnLogin_Click(s, e); };
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Cargar imagen de fondo
            string bgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "background.png");
            if (File.Exists(bgPath))
                imgFondo.Source = new BitmapImage(new Uri(bgPath));

            // Cargar logo
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "csm-logo.png");
            if (File.Exists(logoPath))
                imgLogo.Source = new BitmapImage(new Uri(logoPath));

            CargarInstituciones();
            txtUsuario.Focus();
        }

        private void LnkRestablecer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            new RestablecerPasswordDialog { Owner = this }.ShowDialog();
        }

        private void TxtUsuario_Changed(object sender, TextChangedEventArgs e)
        {
            usuarioPlaceholder.Visibility = string.IsNullOrEmpty(txtUsuario.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CargarInstituciones()
        {
            try
            {
                _instituciones = new InstitucionRepository().ObtenerTodas(soloActivas: true);
                cmbInstitucion.Items.Clear();
                foreach (var inst in _instituciones)
                    cmbInstitucion.Items.Add(inst.Nombre);
                cmbInstitucion.Text = "";
            }
            catch { }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            lblMensaje.Text = "";

            if (string.IsNullOrWhiteSpace(txtUsuario.Text) || txtPassword.Password.Length == 0)
            {
                lblMensaje.Text = "Ingrese usuario y contraseña.";
                return;
            }

            int? idInstitucion = null;
            string textoInst = cmbInstitucion.Text.Trim();
            if (!string.IsNullOrEmpty(textoInst))
            {
                var inst = _instituciones.Find(
                    i => i.Nombre.Equals(textoInst, StringComparison.OrdinalIgnoreCase));
                if (inst != null) idInstitucion = inst.IdInstitucion;
            }

            btnLogin.IsEnabled = false;
            btnLogin.Content = "Verificando...";

            string usuario = txtUsuario.Text.Trim();
            string clave   = txtPassword.Password;

            var resultado = await Task.Run(() => _auth.Login(usuario, clave, idInstitucion));

            btnLogin.IsEnabled = true;
            btnLogin.Content = "Iniciar Sesión";

            if (resultado.Exito)
            {
                var main = new MainWindow();
                main.Show();
                this.Close();
            }
            else
            {
                lblMensaje.Text = resultado.Mensaje;
            }
        }
    }
}
