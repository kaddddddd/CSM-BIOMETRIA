using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
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
            Loaded  += (_, _) =>
            {
                IniciarCombo();
                AsistenciaService.IngresoRegistrado += OnIngresoRegistrado;
            };
            Unloaded += (_, _) => AsistenciaService.IngresoRegistrado -= OnIngresoRegistrado;
        }

        private void OnIngresoRegistrado(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(CargarRegistros);

        private void IniciarCombo()
        {
            // Bloquear fechas futuras y abrir siempre en el mes actual
            dpDesde.DisplayDateEnd = DateTime.Today;
            dpDesde.DisplayDate    = DateTime.Today;
            dpHasta.DisplayDateEnd = DateTime.Today;
            dpHasta.DisplayDate    = DateTime.Today;

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
                dpHasta.SelectedDate = hoy;
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

            var desde = dpDesde.SelectedDate.Value.Date;
            var hasta = dpHasta.SelectedDate.Value.Date;
            if (hasta > DateTime.Today) hasta = DateTime.Today;
            if (desde > hasta) return;

            var registros = _repoReg.ObtenerPorEstudianteYRango(_estudianteActual.IdEstudiante, desde, hasta);

            // Calcular días lectivos esperados y agregar ausencias reales al grid
            var slots = new HorarioRepository().ObtenerSlotsPorSedes(new[] { _estudianteActual.IdSede });

            var diasRango = Enumerable
                .Range(0, (hasta - desde).Days + 1)
                .Select(d => desde.AddDays(d).Date)
                .Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday
                         && d < DateTime.Today) // hoy no cuenta como falta hasta que termine el día
                .ToList();

            var diasConRegistro = registros
                .Select(r => r.FechaIngreso.Date)
                .Distinct()
                .ToHashSet();

            var ausentes = new List<RegistroIngreso>();
            foreach (var dia in diasRango)
            {
                string diaStr = DiaSemanaStr(dia.DayOfWeek);
                int slotsEsp;
                if (!slots.TryGetValue((_estudianteActual.IdSede, (int?)_estudianteActual.IdGrado, diaStr), out slotsEsp))
                    slots.TryGetValue((_estudianteActual.IdSede, null, diaStr), out slotsEsp);
                if (slotsEsp == 0) continue; // día sin clase
                if (!diasConRegistro.Contains(dia))
                    ausentes.Add(new RegistroIngreso
                    {
                        IdEstudiante     = _estudianteActual.IdEstudiante,
                        IdSede           = _estudianteActual.IdSede,
                        FechaIngreso     = dia,
                        EstadoIngreso    = EstadoIngreso.FUERA_DE_HORARIO,
                        NombreEstudiante = _estudianteActual.NombreCompleto,
                        Identificacion   = _estudianteActual.Identificacion,
                        NombreSede       = _estudianteActual.NombreSede,
                        NombreGrado      = _estudianteActual.NombreGrado,
                        NombreGrupo      = _estudianteActual.NombreGrupo,
                    });
            }

            var todos = registros.Concat(ausentes)
                                 .OrderBy(r => r.FechaIngreso)
                                 .ToList();

            int asistencias = todos.Count(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO);
            int tardanzas   = todos.Count(r => r.EstadoIngreso == EstadoIngreso.TARDE);
            int faltas      = ausentes.Count;

            lblTotalAsistencias.Text = asistencias.ToString();
            lblTotalTardanzas.Text   = tardanzas.ToString();
            lblTotalFaltas.Text      = faltas.ToString();

            var view = CollectionViewSource.GetDefaultView(todos);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription("FechaIngreso"));

            grid.ItemsSource = view;

            lblTotal.Text = todos.Count > 0
                ? $"{todos.Count} registro(s) · {desde:dd/MM/yyyy} → {hasta:dd/MM/yyyy}"
                : $"Sin registros entre {desde:dd/MM/yyyy} y {hasta:dd/MM/yyyy}";
        }

        private static string DiaSemanaStr(DayOfWeek dow) => dow switch
        {
            DayOfWeek.Monday    => "LUNES",
            DayOfWeek.Tuesday   => "MARTES",
            DayOfWeek.Wednesday => "MIERCOLES",
            DayOfWeek.Thursday  => "JUEVES",
            DayOfWeek.Friday    => "VIERNES",
            DayOfWeek.Saturday  => "SABADO",
            _                   => "DOMINGO"
        };

        // ── Otros ─────────────────────────────────────────────────

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (grid.SelectedItem is not RegistroIngreso reg) return;
            if (reg.EstadoIngreso == EstadoIngreso.YA_REGISTRADO) return;

            var dlg = new JustificarAsistenciaDialog(reg) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            string textoFinal = string.IsNullOrWhiteSpace(dlg.Observacion)
                ? dlg.Motivo
                : $"{dlg.Motivo} — {dlg.Observacion}";

            if (reg.IdRegistro == 0)
                _repoReg.InsertarJustificado(reg, dlg.NuevoEstado, textoFinal, reg.NombreFranja);
            else
                _repoReg.ActualizarCompleto(reg.IdRegistro, dlg.NuevoEstado, textoFinal, reg.NombreFranja);

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

        private void Dp_CalendarOpened(object sender, RoutedEventArgs e)
        {
            if (sender is DatePicker dp)
                dp.DisplayDate = DateTime.Today;
        }

        private void TxtDocumento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnBuscar_Click(sender, null);
        }

        private void TxtDocumento_TextChanged(object sender, TextChangedEventArgs e)
        {
            string texto = txtDocumento.Text.Trim();
            if (texto.Length < 2)
            {
                popupResultados.IsOpen = false;
                return;
            }

            int? idInst = SesionActiva.EsSuperAdmin ? (int?)null : SesionActiva.InstitucionActual?.IdInstitucion;
            var resultados = _repoEst.Buscar(texto, idInst);

            if (resultados.Count == 0)
            {
                popupResultados.IsOpen = false;
                return;
            }

            lstResultados.ItemsSource = resultados;
            popupResultados.IsOpen = true;
        }
    }
}
