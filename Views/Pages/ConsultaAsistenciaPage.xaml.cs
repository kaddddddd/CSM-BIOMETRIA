using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class ConsultaAsistenciaPage : Page
    {
        private readonly EstudianteRepository      _repoEst = new();
        private readonly RegistroIngresoRepository _repoReg = new();
        private Estudiante? _estudianteActual;

        private static readonly (string label, int dias, int mesI, int diaI, int mesF, int diaF)[] _opciones =
        {
            ("Hoy",           0,  0,  0,  0,  0),
            ("1 semana",      6,  0,  0,  0,  0),
            ("15 días",      14,  0,  0,  0,  0),
            ("30 días",      29,  0,  0,  0,  0),
            ("Período 1",    -1,  1,  1,  3, 31),
            ("Período 2",    -1,  4,  1,  6, 30),
            ("Período 3",    -1,  7,  1,  9, 30),
            ("Período 4",    -1, 10,  1, 12, 31),
            ("Personalizado",-2,  0,  0,  0,  0),
        };

        private bool _iniciando;

        public ConsultaAsistenciaPage()
        {
            InitializeComponent();
            Loaded += (_, _) => IniciarCombo();
        }

        private void IniciarCombo()
        {
            _iniciando = true;
            cmbPeriodo.Items.Clear();
            foreach (var o in _opciones) cmbPeriodo.Items.Add(o.label);
            cmbPeriodo.SelectedIndex = 1; // 1 semana por defecto
            _iniciando = false;
            AplicarPeriodo();
        }

        private void AplicarPeriodo()
        {
            if (cmbPeriodo.SelectedIndex < 0) return;
            var hoy = DateTime.Today;
            var op  = _opciones[cmbPeriodo.SelectedIndex];

            pnlFechasCustom.Visibility = op.dias == -2
                ? Visibility.Visible : Visibility.Collapsed;

            if (op.dias == -2) return; // fechas manuales

            _iniciando = true;
            if (op.dias >= 0)
            {
                dpDesde.SelectedDate = hoy.AddDays(-op.dias);
                dpHasta.SelectedDate = hoy;
            }
            else
            {
                dpDesde.SelectedDate = new DateTime(hoy.Year, op.mesI, op.diaI);
                dpHasta.SelectedDate = new DateTime(hoy.Year, op.mesF, op.diaF);
            }
            _iniciando = false;
            CargarRegistros();
        }

        // ── Búsqueda ──────────────────────────────────────────────

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
                lblTotalAsistencias.Text = "0";
                lblTotalTardanzas.Text   = "0";
                lblTotalFaltas.Text      = "0";
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

            // Múltiples resultados → popup de selección
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

        // ── Carga de registros ────────────────────────────────────

        private void CargarRegistros()
        {
            if (_estudianteActual == null) return;
            if (dpDesde.SelectedDate == null || dpHasta.SelectedDate == null) return;

            var desde = dpDesde.SelectedDate.Value;
            var hasta = dpHasta.SelectedDate.Value;
            if (desde > hasta) return;

            var registros = _repoReg.ObtenerPorEstudianteYRango(_estudianteActual.IdEstudiante, desde, hasta);

            int asistencias = registros.Count(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO);
            int tardanzas   = registros.Count(r => r.EstadoIngreso == EstadoIngreso.TARDE);
            int faltas      = registros.Count(r => r.EstadoIngreso == EstadoIngreso.FUERA_DE_HORARIO);

            lblTotalAsistencias.Text = asistencias.ToString();
            lblTotalTardanzas.Text   = tardanzas.ToString();
            lblTotalFaltas.Text      = faltas.ToString();

            var view = CollectionViewSource.GetDefaultView(registros);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription("FechaIngreso"));

            grid.ItemsSource = view;

            lblTotal.Text = registros.Count > 0
                ? $"{registros.Count} registro(s) · {desde:dd/MM/yyyy} → {hasta:dd/MM/yyyy}"
                : $"Sin registros entre {desde:dd/MM/yyyy} y {hasta:dd/MM/yyyy}";
        }

        // ── Otros ─────────────────────────────────────────────────

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (grid.SelectedItem is not RegistroIngreso reg) return;
            if (reg.EstadoIngreso == EstadoIngreso.A_TIEMPO ||
                reg.EstadoIngreso == EstadoIngreso.YA_REGISTRADO) return;

            var dlg = new JustificarAsistenciaDialog(reg) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            _repoReg.ActualizarObservaciones(reg.IdRegistro, dlg.Observacion);
            CargarRegistros();
        }

        private void CmbPeriodo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_iniciando) AplicarPeriodo();
        }

        private void Fechas_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_iniciando) CargarRegistros();
        }

        private void TxtDocumento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnBuscar_Click(sender, null);
        }
    }
}
