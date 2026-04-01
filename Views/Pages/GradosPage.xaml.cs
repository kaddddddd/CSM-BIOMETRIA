using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class GradosPage : Page
    {
        private readonly GradoRepository _repo = new();

        public GradosPage()
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
                MessageBox.Show("Error cargando grados: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirFormulario(Grado? grado)
        {
            var dlg = new EditarGradoDialog(grado) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD,
                    grado == null ? "Nuevo grado creado" : $"Grado editado: {grado.NombreGrado}");
                Cargar();
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e) => AbrirFormulario(null);
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is Grado g) AbrirFormulario(g);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Grado g) return;
            var r = MessageBox.Show(
                $"¿Eliminar el grado \"{g.NombreGrado}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.Eliminar(g.IdGrado);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Grado eliminado: {g.NombreGrado}");
                Cargar();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al eliminar: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
