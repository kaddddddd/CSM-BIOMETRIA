using System;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class GruposPage : Page
    {
        private readonly GrupoRepository _repo = new();

        public GruposPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (!SesionActiva.EsSuperAdmin)
                {
                    btnNuevo.Visibility    = Visibility.Collapsed;
                    colEditar.Visibility   = Visibility.Collapsed;
                    colEliminar.Visibility = Visibility.Collapsed;
                }
                Cargar();
            };
        }

        private void Cargar()
        {
            try
            {
                var lista = _repo.ObtenerTodos();
                grid.ItemsSource = lista;
                lblTotal.Text = $"Mostrando {lista.Count} registros";
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error cargando grupos: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirFormulario(Grupo? grupo)
        {
            var dlg = new EditarGrupoDialog(grupo) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD,
                    grupo == null ? "Nuevo grupo creado" : $"Grupo editado: {grupo.NombreGrupo}");
                Cargar();
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e) => AbrirFormulario(null);
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is Grupo g) AbrirFormulario(g);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Grupo g) return;
            var r = CustomMessageBox.Show(
                $"¿Eliminar el grupo \"{g.NombreGrupo}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.Eliminar(g.IdGrupo);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Grupo eliminado: {g.NombreGrupo}");
                Cargar();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error al eliminar: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
