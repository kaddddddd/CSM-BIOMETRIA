using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class ConsultaAsistenciaPage : Page
    {
        private readonly EstudianteRepository      _repoEst = new();
        private readonly RegistroIngresoRepository _repoReg = new();
        private Estudiante? _estudianteActual;

        public ConsultaAsistenciaPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                dpDesde.SelectedDate = DateTime.Today.AddDays(-6);
                dpHasta.SelectedDate = DateTime.Today;
            };
        }

        private void BtnBuscar_Click(object sender, RoutedEventArgs e)
        {
            string texto = txtDocumento.Text.Trim();
            if (string.IsNullOrEmpty(texto)) return;

            int? idInst = SesionActiva.EsSuperAdmin ? (int?)null : SesionActiva.InstitucionActual?.IdInstitucion;
            var resultados = _repoEst.Buscar(texto, idInst);

            if (resultados.Count == 0)
            {
                pnlEstudiante.Visibility = Visibility.Collapsed;
                grid.ItemsSource = null;
                lblTotal.Text = "Estudiante no encontrado";
                return;
            }

            // Coincidencia exacta por documento → seleccionar directo
            var exacto = resultados.FirstOrDefault(e => e.Identificacion == texto);
            if (exacto != null || resultados.Count == 1)
            {
                MostrarEstudiante(exacto ?? resultados[0]);
                return;
            }

            // Múltiples resultados → mostrar popup de selección
            lstResultados.ItemsSource = resultados;
            popupResultados.IsOpen = true;
        }

        private void LstResultados_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstResultados.SelectedItem is Estudiante est)
            {
                popupResultados.IsOpen = false;
                MostrarEstudiante(est);
            }
        }

        private void MostrarEstudiante(Estudiante est)
        {
            _estudianteActual = est;
            lblNombre.Text    = est.NombreCompleto;
            lblDocumento.Text = est.Identificacion;
            lblGrado.Text     = $"{est.NombreGrado} - {est.NombreGrupo}";
            lblSede.Text      = est.NombreSede;
            pnlEstudiante.Visibility = Visibility.Visible;
            CargarRegistros();
        }

        private void CargarRegistros()
        {
            if (_estudianteActual == null) return;
            if (dpDesde.SelectedDate == null || dpHasta.SelectedDate == null) return;

            var desde = dpDesde.SelectedDate.Value;
            var hasta = dpHasta.SelectedDate.Value;
            if (desde > hasta) return;

            var registros = _repoReg.ObtenerPorEstudianteYRango(_estudianteActual.IdEstudiante, desde, hasta);

            // Agrupar por fecha
            var view = CollectionViewSource.GetDefaultView(registros);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription("FechaIngreso"));

            grid.ItemsSource = view;
            lblTotal.Text = registros.Count > 0
                ? $"{registros.Count} registro(s) entre {desde:dd/MM/yyyy} y {hasta:dd/MM/yyyy}"
                : $"Sin registros entre {desde:dd/MM/yyyy} y {hasta:dd/MM/yyyy}";
        }

        private void Fechas_Changed(object sender, SelectionChangedEventArgs e) => CargarRegistros();

        private void TxtDocumento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnBuscar_Click(sender, null);
        }
    }
}
