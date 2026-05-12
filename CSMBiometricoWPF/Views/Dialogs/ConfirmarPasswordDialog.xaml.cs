using System;
using System.Windows;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class ConfirmarPasswordDialog : Window
    {
        public bool Confirmado { get; private set; } = false;

        public ConfirmarPasswordDialog(string mensaje)
        {
            InitializeComponent();
            lblMensaje.Text = mensaje;
            Loaded += (_, _) => pwdConfirmar.Focus();
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e) => Verificar();

        private void Pwd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Verificar();
        }

        private void Verificar()
        {
            string password = pwdConfirmar.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                MostrarError("Ingresa tu contraseña.");
                return;
            }

            try
            {
                string username = SesionActiva.UsuarioActual?.Username ?? "";
                var usuario = new UsuarioRepository().Autenticar(username, password);
                if (usuario == null)
                {
                    MostrarError("Contraseña incorrecta.");
                    pwdConfirmar.Clear();
                    pwdConfirmar.Focus();
                    return;
                }

                Confirmado = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MostrarError("Error al verificar: " + ex.Message);
            }
        }

        private void MostrarError(string msg)
        {
            lblError.Text = msg;
            lblError.Visibility = Visibility.Visible;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
