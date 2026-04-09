using System;
using System.Collections.Generic;
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

        private static readonly (string label, int dias)[] _opcionesFijas =
        {
            ("Hoy",      0),
            ("1 semana", 6),
            ("15 días", 14),
            ("30 días", 29),
        };

        private List<PeriodoAcademico> _periodosDB = new();
        private bool _iniciando;

        public ConsultaAsistenciaPage()
        {
            InitializeComponent();
            Loaded += (_, _) => IniciarCombo();
        }

        private void IniciarCombo()
        {
            _iniciando  = true;
            _periodosDB = new PeriodoAcademicoRepository()
                .ObtenerPorInstitucion(SesionActiva.InstitucionActual?.IdInstitucion);

            cmbPeriodo.Items.Clear();
            foreach (var o in _opcionesFijas) cmbPeriodo.Items.Add(o.label);
            foreach (var p in _periodosDB)    cmbPeriodo.Items.Add(p.Nombre);
            cmbPeriodo.Items.Add("Personalizado");

            cmbPeriodo.SelectedIndex = 1; // 1 semana por defecto
            _iniciando = false;
            AplicarPeriodo();
        }

        private void AplicarPeriodo()
        {
            if (cmbPeriodo.SelectedIndex < 0) return;
            var hoy          = DateTime.Today;
            int idx          = cmbPeriodo.SelectedIndex;
            int totalFijas   = _opcionesFijas.Length;
            bool esCustom    = idx == totalFijas + _periodosDB.Count;

            pnlFechasCustom.Visibility = esCustom ? Visibility.Visible : Visibility.Collapsed;
            if (esCustom) return;

            _iniciando = true;
            if (idx < totalFijas)
            {
                dpDesde.SelectedDate = hoy.AddDays(-_opcionesFijas[idx].dias);
                dpHasta.SelectedDate = hoy;
            }
            else
            {
                var p = _periodosDB[idx - totalFijas];
                dpDesde.SelectedDate = new DateTime(hoy.Year, p.MesInicio, p.DiaInicio);
                dpHasta.SelectedDate = new DateTime(hoy.Year, p.MesFin,    p.DiaFin);
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

            if (dlg.NuevoEstado != reg.EstadoIngreso)
                _repoReg.ActualizarEstadoYObservaciones(reg.IdRegistro, dlg.NuevoEstado, dlg.Observacion);
            else
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
