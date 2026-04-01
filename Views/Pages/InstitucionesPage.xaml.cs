using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    // Wrapper para mostrar columnas calculadas en el grid
    public class InstitucionVm
    {
        public Institucion Origen { get; }
        public int IdInstitucion => Origen.IdInstitucion;
        public string Nombre => Origen.Nombre;
        public string Direccion => Origen.Direccion;
        public string Telefono => Origen.Telefono;
        public string EstadoStr => Origen.Estado ? "Activa" : "Inactiva";
        public string FechaCreacionStr => Origen.FechaCreacion.ToString("dd/MM/yyyy");
        public InstitucionVm(Institucion i) => Origen = i;
    }

    public partial class InstitucionesPage : Page
    {
        private readonly InstitucionRepository _repo = new();

        public InstitucionesPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                // El ADMINISTRADOR solo puede ver su institución, sin poder crear/editar/eliminar
                if (!SesionActiva.EsSuperAdmin)
                {
                    btnNueva.Visibility   = Visibility.Collapsed;
                    colEditar.Visibility  = Visibility.Collapsed;
                    colEliminar.Visibility = Visibility.Collapsed;
                }
                Cargar();
            };
        }

        private void Cargar()
        {
            try
            {
                IEnumerable<Institucion> fuente;
                if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
                    fuente = new[] { SesionActiva.InstitucionActual };
                else
                    fuente = _repo.ObtenerTodas(soloActivas: false);

                var lista = fuente.Select(i => new InstitucionVm(i)).ToList();
                grid.ItemsSource = lista;
                lblTotal.Text = $"Mostrando {lista.Count} registros";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando instituciones: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirFormulario(Institucion? inst)
        {
            var dlg = new EditarInstitucionDialog(inst) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD,
                    inst == null ? "Nueva institución creada" : $"Institución editada: {inst.Nombre}");
                Cargar();
            }
        }

        private void BtnNueva_Click(object sender, RoutedEventArgs e) => AbrirFormulario(null);
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is InstitucionVm vm)
                AbrirFormulario(vm.Origen);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not InstitucionVm vm) return;
            var r = MessageBox.Show(
                $"¿Desactivar la institución \"{vm.Nombre}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.Eliminar(vm.IdInstitucion);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Institución desactivada: {vm.Nombre}");
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
