using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class HorariosSedeDialog : Window
    {
        private readonly HorarioRepository _repo = new();
        private readonly Sede _sede;

        // Mapeo día → (txtEntrada, txtTarde, txtCierre)
        private Dictionary<string, (TextBox entrada, TextBox tarde, TextBox cierre)> _campos;

        public HorariosSedeDialog(Sede sede)
        {
            InitializeComponent();
            _sede = sede;
            lblTitulo.Text = $"Horarios — {sede.NombreSede}";
            Title = $"Horarios: {sede.NombreSede}";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _campos = new Dictionary<string, (TextBox, TextBox, TextBox)>
            {
                ["LUNES"]     = (txtLunesE,    txtLunesT,    txtLunesC),
                ["MARTES"]    = (txtMartesE,   txtMartesT,   txtMartesC),
                ["MIERCOLES"] = (txtMiercolesE, txtMiercolesT, txtMiercolesC),
                ["JUEVES"]    = (txtJuevesE,   txtJuevesT,   txtJuevesC),
                ["VIERNES"]   = (txtViernesE,  txtViernesT,  txtViernesC),
                ["SABADO"]    = (txtSabadoE,   txtSabadoT,   txtSabadoC),
                ["DOMINGO"]   = (txtDomingoE,  txtDomingoT,  txtDomingoC),
            };
            CargarHorarios();
        }

        private void CargarHorarios()
        {
            try
            {
                var horarios = _repo.ObtenerPorSede(_sede.IdSede);
                foreach (var h in horarios)
                {
                    string dia = h.DiaSemana.ToString();
                    if (_campos.TryGetValue(dia, out var c))
                    {
                        c.entrada.Text = h.HoraInicio.ToString(@"hh\:mm");
                        c.tarde.Text   = h.HoraLimiteTarde.ToString(@"hh\:mm");
                        c.cierre.Text  = h.HoraCierreIngreso.ToString(@"hh\:mm");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando horarios: " + ex.Message);
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string dia) return;
            if (!_campos.TryGetValue(dia, out var c)) return;

            if (!TimeSpan.TryParse(c.entrada.Text, out var entrada) ||
                !TimeSpan.TryParse(c.tarde.Text,   out var tarde)   ||
                !TimeSpan.TryParse(c.cierre.Text,  out var cierre))
            {
                MessageBox.Show("Formato de hora inválido. Use HH:mm (ej: 07:30)", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var horario = new Horario
                {
                    IdSede            = _sede.IdSede,
                    DiaSemana         = (DiaSemana)Enum.Parse(typeof(DiaSemana), dia),
                    HoraInicio        = entrada,
                    HoraLimiteTarde   = tarde,
                    HoraCierreIngreso = cierre,
                    Activo            = true
                };
                _repo.Guardar(horario);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Horario guardado: {_sede.NombreSede} - {dia}");
                MessageBox.Show($"Horario de {dia} guardado.", "Guardado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error guardando horario: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
