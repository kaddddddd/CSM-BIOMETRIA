using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class HorariosPage : Page
    {
        private readonly HorarioRepository _horarioRepo = new();
        private readonly InstitucionRepository _instRepo = new();
        private readonly SedeRepository _sedeRepo = new();
        private readonly GradoRepository _gradoRepo = new();
        private readonly GrupoRepository _grupoRepo = new();
        private bool _cargandoFiltros = false;
        private int _idSedeActual = 0;
        private Sede _sedeActual = null;
        private int? _idGradoActual = null;   // null = "Todos los grados"
        private int? _idGrupoActual = null;   // null = "Todos los grupos"

        private Dictionary<string, (TextBox entrada, TextBox tarde, TextBox cierre)> _campos;
        private Dictionary<string, Horario> _horariosCargados = new();

        public HorariosPage()
        {
            InitializeComponent();
            Loaded += HorariosPage_Loaded;
        }

        private void HorariosPage_Loaded(object sender, RoutedEventArgs e)
        {
            _campos = new Dictionary<string, (TextBox, TextBox, TextBox)>
            {
                ["LUNES"]     = (txtLunesEntrada,     txtLunesTarde,     txtLunesCierre),
                ["MARTES"]    = (txtMartesEntrada,    txtMartesTarde,    txtMartesCierre),
                ["MIERCOLES"] = (txtMiercolesEntrada, txtMiercolesTarde, txtMiercolesCierre),
                ["JUEVES"]    = (txtJuevesEntrada,    txtJuevesTarde,    txtJuevesCierre),
                ["VIERNES"]   = (txtViernesEntrada,   txtViernesTarde,   txtViernesCierre),
            };

            // Cargar grados (una sola vez)
            _cargandoFiltros = true;
            cmbGrado.Items.Clear();
            cmbGrado.Items.Add(new Grado { IdGrado = 0, NombreGrado = "Todos los grados" });
            try { foreach (var g in _gradoRepo.ObtenerTodos()) cmbGrado.Items.Add(g); } catch { }
            cmbGrado.DisplayMemberPath = "NombreGrado";
            cmbGrado.SelectedIndex = 0;

            // Cargar grupos (una sola vez)
            CargarComboGrupos();
            _cargandoFiltros = false;

            // Cargar instituciones
            _cargandoFiltros = true;
            cmbInstitucion.Items.Clear();
            cmbInstitucion.DisplayMemberPath = "Nombre";

            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.IsEnabled = false;
                _cargandoFiltros = false;
                CmbInstitucion_Changed(cmbInstitucion, null);
            }
            else
            {
                cmbInstitucion.Items.Add(new Institucion { IdInstitucion = 0, Nombre = "-- Seleccione --" });
                try { foreach (var inst in _instRepo.ObtenerTodas(soloActivas: true)) cmbInstitucion.Items.Add(inst); } catch { }
                cmbInstitucion.SelectedIndex = 0;
                _cargandoFiltros = false;
            }
        }

        private void CmbInstitucion_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            _cargandoFiltros = true;
            cmbSede.Items.Clear();
            cmbSede.Items.Add(new Sede { IdSede = 0, NombreSede = "-- Seleccione --" });
            if (cmbInstitucion.SelectedItem is Institucion inst && inst.IdInstitucion > 0)
            {
                try { foreach (var s in _sedeRepo.ObtenerPorInstitucion(inst.IdInstitucion)) cmbSede.Items.Add(s); } catch { }
            }
            cmbSede.DisplayMemberPath = "NombreSede";
            cmbSede.SelectedIndex = 0;
            _cargandoFiltros = false;
            OcultarHorarios();
        }

        private void CmbSede_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            if (cmbSede.SelectedItem is Sede sede && sede.IdSede > 0)
            {
                _idSedeActual = sede.IdSede;
                _sedeActual = sede;
                CargarHorarios();
                btnVerExcepciones.Visibility = Visibility.Visible;
            }
            else
            {
                _idSedeActual = 0;
                _sedeActual = null;
                OcultarHorarios();
            }
        }

        private void CmbGrado_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            _idGradoActual = (cmbGrado.SelectedItem is Grado g && g.IdGrado > 0) ? g.IdGrado : (int?)null;

            // Al cambiar grado, resetear grupo
            _cargandoFiltros = true;
            CargarComboGrupos();
            _cargandoFiltros = false;

            if (_idSedeActual > 0) CargarHorarios();
        }

        private void CargarComboGrupos()
        {
            cmbGrupo.Items.Clear();
            cmbGrupo.Items.Add(new Grupo { IdGrupo = 0, NombreGrupo = "Todos los grupos" });
            // Solo mostrar grupos si hay un grado seleccionado
            if (_idGradoActual.HasValue)
            {
                try { foreach (var gr in _grupoRepo.ObtenerTodos()) cmbGrupo.Items.Add(gr); } catch { }
            }
            cmbGrupo.DisplayMemberPath = "NombreGrupo";
            cmbGrupo.SelectedIndex = 0;
            _idGrupoActual = null;
            cmbGrupo.IsEnabled = _idGradoActual.HasValue;
        }

        private void CmbGrupo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            _idGrupoActual = (cmbGrupo.SelectedItem is Grupo gr && gr.IdGrupo > 0) ? gr.IdGrupo : (int?)null;
            if (_idSedeActual > 0) CargarHorarios();
        }

        private void OcultarHorarios()
        {
            pnlHorarios.Visibility = Visibility.Collapsed;
            lblSeleccioneSede.Visibility = Visibility.Visible;
            btnVerExcepciones.Visibility = Visibility.Collapsed;
        }

        private void CargarHorarios()
        {
            try
            {
                _horariosCargados.Clear();
                foreach (var kv in _campos)
                {
                    kv.Value.entrada.Text = "";
                    kv.Value.tarde.Text = "";
                    kv.Value.cierre.Text = "";
                }

                var horarios = _horarioRepo.ObtenerPorSede(_idSedeActual, _idGradoActual, _idGrupoActual);
                foreach (var h in horarios)
                {
                    string dia = h.DiaSemana.ToString();
                    _horariosCargados[dia] = h;
                    if (_campos.TryGetValue(dia, out var campos))
                    {
                        campos.entrada.Text = h.HoraInicio.ToString(@"hh\:mm");
                        campos.tarde.Text   = h.HoraLimiteTarde.ToString(@"hh\:mm");
                        campos.cierre.Text  = h.HoraCierreIngreso.ToString(@"hh\:mm");
                    }
                }

                // Banner informativo de ámbito
                if (_idGradoActual.HasValue && _idGrupoActual.HasValue
                    && cmbGrado.SelectedItem is Grado gselGrupo
                    && cmbGrupo.SelectedItem is Grupo grsel)
                    lblInfoAmbito.Text = $"Horario específico para: {gselGrupo.NombreGrado} – Grupo {grsel.NombreGrupo}  (prioridad más alta)";
                else if (_idGradoActual.HasValue && cmbGrado.SelectedItem is Grado gsel)
                    lblInfoAmbito.Text = $"Horario del grado: {gsel.NombreGrado}  (aplica a grupos de este grado sin horario propio)";
                else
                    lblInfoAmbito.Text = "Horario general de la sede  (aplica a todos los grados/grupos sin horario propio)";

                pnlHorarios.Visibility = Visibility.Visible;
                lblSeleccioneSede.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error cargando horarios: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGuardarDia_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string dia) return;
            if (_idSedeActual == 0) return;
            if (!_campos.TryGetValue(dia, out var campos)) return;

            if (!TimeSpan.TryParse(campos.entrada.Text, out var entrada) ||
                !TimeSpan.TryParse(campos.tarde.Text,   out var tarde)   ||
                !TimeSpan.TryParse(campos.cierre.Text,  out var cierre))
            {
                CustomMessageBox.Show("Formato de hora inválido. Use HH:mm (ej: 07:30)", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var horario = new Horario
                {
                    IdSede            = _idSedeActual,
                    IdGrado           = _idGradoActual,
                    IdGrupo           = _idGrupoActual,
                    DiaSemana         = (DiaSemana)Enum.Parse(typeof(DiaSemana), dia),
                    HoraInicio        = entrada,
                    HoraLimiteTarde   = tarde,
                    HoraCierreIngreso = cierre,
                    Activo            = true
                };
                _horarioRepo.Guardar(horario);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Horario guardado: sede {_idSedeActual} grado {_idGradoActual?.ToString() ?? "todos"} grupo {_idGrupoActual?.ToString() ?? "todos"} - {dia}");
                CargarHorarios();

                string ambito;
                if (_idGradoActual.HasValue && _idGrupoActual.HasValue)
                    ambito = $"(grado {(cmbGrado.SelectedItem as Grado)?.NombreGrado} – grupo {(cmbGrupo.SelectedItem as Grupo)?.NombreGrupo})";
                else if (_idGradoActual.HasValue)
                    ambito = $"(grado {(cmbGrado.SelectedItem as Grado)?.NombreGrado})";
                else
                    ambito = "(todos los grados)";
                CustomMessageBox.Show($"Horario de {dia} {ambito} guardado correctamente.", "Guardado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error guardando horario: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFranjas_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string dia) return;
            if (_idSedeActual == 0) return;

            if (!_horariosCargados.TryGetValue(dia, out var horario))
            {
                CustomMessageBox.Show(
                    $"Primero guarda el horario base del {dia} antes de agregar jornadas adicionales.",
                    "Sin horario base", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new FranjasHorarioDialog(horario) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }

        private void BtnVerExcepciones_Click(object sender, RoutedEventArgs e)
        {
            if (_sedeActual == null) return;
            int? idInstitucion = (cmbInstitucion.SelectedItem as Institucion)?.IdInstitucion;
            var dlg = new ExcepcionesSedeDialog(_sedeActual, idInstitucion) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }

        private void TimeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"[\d:]");
        }
    }
}
