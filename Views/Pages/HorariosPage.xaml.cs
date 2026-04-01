using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class HorariosPage : Page
    {
        private readonly HorarioRepository _horarioRepo = new();
        private readonly InstitucionRepository _instRepo = new();
        private readonly SedeRepository _sedeRepo = new();
        private bool _cargandoFiltros = false;
        private int _idSedeActual = 0;

        // Mapeo día -> (txtEntrada, txtTarde, txtCierre)
        private Dictionary<string, (TextBox entrada, TextBox tarde, TextBox cierre)> _campos;

        public HorariosPage()
        {
            InitializeComponent();
            Loaded += HorariosPage_Loaded;
        }

        private void HorariosPage_Loaded(object sender, RoutedEventArgs e)
        {
            _campos = new Dictionary<string, (TextBox, TextBox, TextBox)>
            {
                ["LUNES"]     = (txtLunesEntrada,    txtLunesTarde,    txtLunesCierre),
                ["MARTES"]    = (txtMartesEntrada,   txtMartesTarde,   txtMartesCierre),
                ["MIERCOLES"] = (txtMiercolesEntrada, txtMiercolesTarde, txtMiercolesCierre),
                ["JUEVES"]    = (txtJuevesEntrada,   txtJuevesTarde,   txtJuevesCierre),
                ["VIERNES"]   = (txtViernesEntrada,  txtViernesTarde,  txtViernesCierre),
                ["SABADO"]    = (txtSabadoEntrada,   txtSabadoTarde,   txtSabadoCierre),
                ["DOMINGO"]   = (txtDomingoEntrada,  txtDomingoTarde,  txtDomingoCierre),
            };

            _cargandoFiltros = true;
            cmbInstitucion.Items.Clear();
            cmbInstitucion.DisplayMemberPath = "Nombre";

            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // Operador/Administrador: solo ve su institución, se cargan sus sedes automáticamente
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.IsEnabled = false;
                _cargandoFiltros = false;
                // Disparar carga de sedes manualmente
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
                try
                {
                    foreach (var s in _sedeRepo.ObtenerPorInstitucion(inst.IdInstitucion))
                        cmbSede.Items.Add(s);
                }
                catch { }
            }
            cmbSede.DisplayMemberPath = "NombreSede";
            cmbSede.SelectedIndex = 0;
            _cargandoFiltros = false;
            pnlHorarios.Visibility = Visibility.Collapsed;
            lblSeleccioneSede.Visibility = Visibility.Visible;
        }

        private void CmbSede_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoFiltros) return;
            if (cmbSede.SelectedItem is Sede sede && sede.IdSede > 0)
            {
                _idSedeActual = sede.IdSede;
                CargarHorarios();
            }
            else
            {
                pnlHorarios.Visibility = Visibility.Collapsed;
                lblSeleccioneSede.Visibility = Visibility.Visible;
            }
        }

        private void CargarHorarios()
        {
            try
            {
                // Limpiar campos
                foreach (var kv in _campos)
                {
                    kv.Value.entrada.Text = "";
                    kv.Value.tarde.Text = "";
                    kv.Value.cierre.Text = "";
                }

                var horarios = _horarioRepo.ObtenerPorSede(_idSedeActual);
                foreach (var h in horarios)
                {
                    string dia = h.DiaSemana.ToString();
                    if (_campos.TryGetValue(dia, out var campos))
                    {
                        campos.entrada.Text = h.HoraInicio.ToString(@"hh\:mm");
                        campos.tarde.Text   = h.HoraLimiteTarde.ToString(@"hh\:mm");
                        campos.cierre.Text  = h.HoraCierreIngreso.ToString(@"hh\:mm");
                    }
                }

                pnlHorarios.Visibility = Visibility.Visible;
                lblSeleccioneSede.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando horarios: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGuardarDia_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string dia) return;
            if (_idSedeActual == 0) return;
            if (!_campos.TryGetValue(dia, out var campos)) return;

            if (!TimeSpan.TryParse(campos.entrada.Text, out var entrada) ||
                !TimeSpan.TryParse(campos.tarde.Text, out var tarde) ||
                !TimeSpan.TryParse(campos.cierre.Text, out var cierre))
            {
                MessageBox.Show("Formato de hora inválido. Use HH:mm (ej: 07:30)", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var horario = new Horario
                {
                    IdSede = _idSedeActual,
                    DiaSemana = (DiaSemana)Enum.Parse(typeof(DiaSemana), dia),
                    HoraInicio = entrada,
                    HoraLimiteTarde = tarde,
                    HoraCierreIngreso = cierre,
                    Activo = true
                };
                _horarioRepo.Guardar(horario);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Horario guardado: sede {_idSedeActual} - {dia}");
                MessageBox.Show($"Horario de {dia} guardado correctamente.", "Guardado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error guardando horario: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
