using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public class UsuarioVm
    {
        public Usuario Origen { get; }
        public int IdUsuario => Origen.IdUsuario;
        public string NombreCompleto => Origen.NombreCompleto;
        public string Username => Origen.Username;
        public string NombreRol => Origen.NombreRol;
        public string NombreInstitucion => Origen.NombreInstitucion ?? "—";
        public string EstadoStr => Origen.Estado ? "Activo" : "Inactivo";
        public string UltimoLoginStr => Origen.UltimoLogin.HasValue
            ? Origen.UltimoLogin.Value.ToString("dd/MM/yyyy HH:mm")
            : "Nunca";
        public UsuarioVm(Usuario u) => Origen = u;
    }

    public partial class UsuariosPage : Page
    {
        private readonly UsuarioRepository _repo = new();

        public UsuariosPage()
        {
            InitializeComponent();
            Loaded += UsuariosPage_Loaded;
        }

        private void UsuariosPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!SesionActiva.EsSuperAdmin)
            {
                pnlDenegado.Visibility = Visibility.Visible;
                grid.Visibility = Visibility.Collapsed;
                return;
            }
            Cargar();
        }

        private void Cargar()
        {
            try
            {
                var lista = _repo.ObtenerTodos().Select(u => new UsuarioVm(u)).ToList();
                grid.ItemsSource = lista;
                lblTotal.Text = $"Mostrando {lista.Count} registros";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando usuarios: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirFormulario(Usuario? usuario)
        {
            var dlg = new EditarUsuarioDialog(usuario) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD,
                    usuario == null ? "Nuevo usuario creado" : $"Usuario editado: {usuario.Username}");
                Cargar();
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (!SesionActiva.EsSuperAdmin) return;
            AbrirFormulario(null);
        }

        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is UsuarioVm vm)
                AbrirFormulario(vm.Origen);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not UsuarioVm vm) return;
            if (vm.IdUsuario == SesionActiva.UsuarioActual?.IdUsuario)
            {
                MessageBox.Show("No puedes desactivar tu propia cuenta.", "Operación no permitida",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var r = MessageBox.Show(
                $"¿Desactivar al usuario \"{vm.Username}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.Eliminar(vm.IdUsuario);
                new LogRepository().Registrar(TipoEvento.SEGURIDAD, $"Usuario desactivado: {vm.Username}");
                Cargar();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al desactivar: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
