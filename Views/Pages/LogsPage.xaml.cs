using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class LogsPage : Page
    {
        private readonly LogRepository _repo = new();
        private List<LogSistema> _todos = new();

        public LogsPage()
        {
            InitializeComponent();
            Loaded += LogsPage_Loaded;
        }

        private void LogsPage_Loaded(object sender, RoutedEventArgs e)
        {
            CargarFiltros();
            CargarLogs();
        }

        private void CargarFiltros()
        {
            cmbNivel.Items.Clear();
            cmbNivel.Items.Add("-- Todos --");
            foreach (var v in Enum.GetNames(typeof(NivelLog))) cmbNivel.Items.Add(v);
            cmbNivel.SelectedIndex = 0;

            cmbTipo.Items.Clear();
            cmbTipo.Items.Add("-- Todos --");
            foreach (var v in Enum.GetNames(typeof(TipoEvento))) cmbTipo.Items.Add(v);
            cmbTipo.SelectedIndex = 0;
        }

        private void CargarLogs()
        {
            try
            {
                int? idInst = SesionActiva.EsSuperAdmin ? (int?)null : SesionActiva.InstitucionActual?.IdInstitucion;
                _todos = _repo.ObtenerRecientes(500, idInst);
                Filtrar();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando logs: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Filtrar()
        {
            var lista = _todos.AsEnumerable();

            if (cmbNivel.SelectedIndex > 0 && Enum.TryParse<NivelLog>(cmbNivel.SelectedItem?.ToString(), out var nivel))
                lista = lista.Where(l => l.Nivel == nivel);

            if (cmbTipo.SelectedIndex > 0 && Enum.TryParse<TipoEvento>(cmbTipo.SelectedItem?.ToString(), out var tipo))
                lista = lista.Where(l => l.TipoEvento == tipo);

            if (!string.IsNullOrWhiteSpace(txtBuscar.Text))
            {
                string q = txtBuscar.Text.Trim();
                lista = lista.Where(l =>
                    (l.Descripcion?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) ||
                    (l.NombreUsuario?.Contains(q, StringComparison.OrdinalIgnoreCase) == true));
            }

            var result = lista.ToList();
            grid.ItemsSource = result;
            lblTotal.Text = $"Mostrando {result.Count} registros";
        }

        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => CargarLogs();

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            cmbNivel.SelectedIndex = 0;
            cmbTipo.SelectedIndex = 0;
            txtBuscar.Text = string.Empty;
            Filtrar();
        }

        private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Filtrar();
        private void TxtBuscar_Changed(object sender, TextChangedEventArgs e) => Filtrar();
    }
}
