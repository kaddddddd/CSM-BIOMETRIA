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
    public class SedeVm
    {
        public Sede Origen { get; }
        public int IdSede => Origen.IdSede;
        public string NombreSede => Origen.NombreSede;
        public string NombreInstitucion => Origen.NombreInstitucion;
        public string Direccion => Origen.Direccion;
        public bool EsActiva => Origen.Estado;
        public string EstadoStr => Origen.Estado ? "Activa" : "Inactiva";
        public string LabelToggle => Origen.Estado ? "🗑" : "✔";
        public string TooltipToggle => Origen.Estado ? "Desactivar" : "Activar";
        public SedeVm(Sede s) => Origen = s;
    }

    public partial class SedesPage : Page
    {
        private readonly SedeRepository _repo = new();
        private readonly InstitucionRepository _instRepo = new();
        private bool _cargandoFiltros = false;

        public SedesPage()
        {
            InitializeComponent();
            Loaded += SedesPage_Loaded;
        }

        private void SedesPage_Loaded(object sender, RoutedEventArgs e)
        {
            _cargandoFiltros = true;
            cmbInstitucion.Items.Clear();
            cmbInstitucion.DisplayMemberPath = "Nombre";

            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // Operador/Administrador: solo ve su institución
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.IsEnabled = false;
            }
            else
            {
                cmbInstitucion.Items.Add(new Institucion { IdInstitucion = 0, Nombre = "-- Todas --" });
                try { foreach (var inst in _instRepo.ObtenerTodas(soloActivas: true)) cmbInstitucion.Items.Add(inst); } catch { }
                cmbInstitucion.SelectedIndex = 0;
            }
            _cargandoFiltros = false;
            Cargar();
        }

        private void Cargar()
        {
            try
            {
                bool soloActivas = chkMostrarInactivas.IsChecked != true;
                List<Sede> lista;
                var inst = cmbInstitucion.SelectedItem as Institucion;
                if (inst != null && inst.IdInstitucion > 0)
                    lista = _repo.ObtenerPorInstitucion(inst.IdInstitucion, soloActivas);
                else
                    lista = _repo.ObtenerTodas(soloActivas);

                var vms = lista.Select(s => new SedeVm(s)).ToList();
                grid.ItemsSource = vms;
                lblTotal.Text = $"Mostrando {vms.Count} registros";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando sedes: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AbrirFormulario(Sede? sede)
        {
            var dlg = new EditarSedeDialog(sede) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD,
                    sede == null ? "Nueva sede creada" : $"Sede editada: {sede.NombreSede}");
                Cargar();
            }
        }

        private void CmbInstitucion_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_cargandoFiltros) Cargar();
        }

        private void ChkMostrarInactivas_Changed(object sender, RoutedEventArgs e) => Cargar();

        private void BtnNueva_Click(object sender, RoutedEventArgs e) => AbrirFormulario(null);
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is SedeVm vm)
                AbrirFormulario(vm.Origen);
        }

        private void BtnHorarios_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not SedeVm vm) return;
            var dlg = new HorariosSedeDialog(vm.Origen) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }

        private void BtnToggleEstado_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not SedeVm vm) return;

            string accion = vm.EsActiva ? "desactivar" : "activar";
            var r = MessageBox.Show(
                $"¿Desea {accion} la sede \"{vm.NombreSede}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                if (vm.EsActiva)
                {
                    _repo.Eliminar(vm.IdSede);
                    new LogRepository().Registrar(TipoEvento.CRUD, $"Sede desactivada: {vm.NombreSede}");
                }
                else
                {
                    _repo.Activar(vm.IdSede);
                    new LogRepository().Registrar(TipoEvento.CRUD, $"Sede activada: {vm.NombreSede}");
                }
                Cargar();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al {accion}: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
