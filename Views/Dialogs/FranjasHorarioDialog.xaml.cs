using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class FranjasHorarioDialog : Window
    {
        private readonly Horario _horario;
        private readonly FranjaHorarioRepository _repo = new();
        private List<FranjaHorario> _franjas = new();

        public FranjasHorarioDialog(Horario horario)
        {
            InitializeComponent();
            _horario = horario;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string dia = _horario.DiaSemana.ToString();
            string diaNombre = dia switch
            {
                "LUNES" => "Lunes", "MARTES" => "Martes", "MIERCOLES" => "Miércoles",
                "JUEVES" => "Jueves", "VIERNES" => "Viernes", "SABADO" => "Sábado", "DOMINGO" => "Domingo",
                _ => dia
            };
            lblTitulo.Text = $"Franjas adicionales — {diaNombre} ({_horario.NombreSede})";
            Title = $"Franjas: {diaNombre}";
            CargarFranjas();
        }

        private void CargarFranjas()
        {
            try
            {
                _franjas = _repo.ObtenerPorHorario(_horario.IdHorario);
                RenderizarFranjas();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error cargando franjas: " + ex.Message);
            }
        }

        private void RenderizarFranjas()
        {
            pnlFranjas.Children.Clear();
            lblSinFranjas.Visibility = _franjas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            lblConteoFranjas.Text = _franjas.Count == 0
                ? "Franjas configuradas"
                : $"Franjas configuradas  ({_franjas.Count})";

            foreach (var f in _franjas)
                pnlFranjas.Children.Add(CrearFilaFranja(f));
        }

        private Border CrearFilaFranja(FranjaHorario f)
        {
            bool esImpar = _franjas.IndexOf(f) % 2 == 0;
            var bgColor = esImpar
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(
                      (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F8FCFA"));

            var grid = new Grid { Tag = f };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var lblNombre = new TextBlock
            {
                Text = f.Nombre,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var lblEntrada = new TextBlock
            {
                Text = f.HoraInicio.ToString(@"hh\:mm"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var lblTarde = new TextBlock
            {
                Text = f.HoraLimiteTarde.ToString(@"hh\:mm"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var lblCierre = new TextBlock
            {
                Text = f.HoraCierreIngreso.ToString(@"hh\:mm"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var btnEliminar = new Button
            {
                Content = "✕",
                Height = 28,
                Margin = new Thickness(2, 0, 0, 0),
                FontSize = 11,
                Tag = f,
                ToolTip = "Eliminar franja"
            };
            btnEliminar.Style = (Style)FindResource("BtnPeligro");
            btnEliminar.Click += BtnEliminarFranja_Click;

            Grid.SetColumn(lblNombre,  0);
            Grid.SetColumn(lblEntrada, 1);
            Grid.SetColumn(lblTarde,   2);
            Grid.SetColumn(lblCierre,  3);
            Grid.SetColumn(btnEliminar, 4);

            grid.Children.Add(lblNombre);
            grid.Children.Add(lblEntrada);
            grid.Children.Add(lblTarde);
            grid.Children.Add(lblCierre);
            grid.Children.Add(btnEliminar);

            var wrapper = new Border
            {
                Background = bgColor,
                CornerRadius = new System.Windows.CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2),
                Child = grid
            };
            return wrapper;
        }

        private void BtnEliminarFranja_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not FranjaHorario f) return;
            var r = CustomMessageBox.Show($"¿Eliminar la franja '{f.Nombre}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                _repo.Eliminar(f.IdFranja);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Franja eliminada: {f.Nombre} (horario {_horario.IdHorario})");
                CargarFranjas();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error eliminando franja: " + ex.Message);
            }
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNuevoNombre.Text.Trim();
            if (string.IsNullOrEmpty(nombre)) nombre = "Franja";

            if (!TimeSpan.TryParse(txtNuevoEntrada.Text, out var entrada) ||
                !TimeSpan.TryParse(txtNuevoTarde.Text, out var tarde) ||
                !TimeSpan.TryParse(txtNuevoCierre.Text, out var cierre))
            {
                CustomMessageBox.Show("Formato de hora inválido. Use HH:mm (ej: 13:00)", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (tarde < entrada || cierre < tarde)
            {
                CustomMessageBox.Show("Las horas deben ir en orden: Entrada ≤ Límite tardanza ≤ Cierre.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var franja = new FranjaHorario
                {
                    IdHorario         = _horario.IdHorario,
                    Nombre            = nombre,
                    HoraInicio        = entrada,
                    HoraLimiteTarde   = tarde,
                    HoraCierreIngreso = cierre,
                    Orden             = _franjas.Count
                };
                _repo.Guardar(franja);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Franja agregada: {nombre} al horario {_horario.IdHorario}");
                txtNuevoNombre.Text = "Jornada tarde";
                txtNuevoEntrada.Text = "";
                txtNuevoTarde.Text = "";
                txtNuevoCierre.Text = "";
                CargarFranjas();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error guardando franja: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Solo permite dígitos y el carácter ":" en campos de hora
        private void TimeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"[\d:]");
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
