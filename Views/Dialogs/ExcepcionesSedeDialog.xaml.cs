using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Dialogs
{
    public partial class ExcepcionesSedeDialog : Window
    {
        private readonly Sede _sede;
        private readonly int? _idInstitucion;
        private readonly HorarioExcepcionRepository _repo = new();
        private readonly GradoRepository _gradoRepo = new();
        private HorarioExcepcion _excepcionSeleccionada = null;

        public ExcepcionesSedeDialog(Sede sede, int? idInstitucion = null)
        {
            InitializeComponent();
            _sede = sede;
            _idInstitucion = idInstitucion;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            lblTitulo.Text = $"Excepciones — {_sede.NombreSede}";
            Title = $"Excepciones: {_sede.NombreSede}";
            dpFecha.SelectedDate = DateTime.Today;

            // Cargar grados en el combo de ámbito
            cmbGradoExc.Items.Clear();
            try { foreach (var g in _gradoRepo.ObtenerTodos()) cmbGradoExc.Items.Add(g); } catch { }
            if (cmbGradoExc.Items.Count > 0) cmbGradoExc.SelectedIndex = 0;

            // Si no hay institución, deshabilitar "Todas las sedes"
            if (!_idInstitucion.HasValue)
                rdoTodasSedes.IsEnabled = false;

            CargarExcepciones();
        }

        // ── Ámbito ──────────────────────────────────────────────────
        private string AlcanceActual =>
            rdoTodasSedes.IsChecked == true ? "INSTITUCION" :
            rdoGrado.IsChecked == true      ? "GRADO"       : "SEDE";

        private void Ambito_Changed(object sender, RoutedEventArgs e)
        {
            if (cmbGradoExc == null) return;
            cmbGradoExc.IsEnabled = rdoGrado.IsChecked == true;

            lblAmbitoInfo.Text = AlcanceActual switch
            {
                "INSTITUCION" => "Aplica a todas las sedes de la institución en esa fecha",
                "GRADO"       => "Aplica al grado seleccionado en esta sede y fecha",
                _             => "Aplica solo a esta sede en esa fecha"
            };
        }

        // ── Carga ────────────────────────────────────────────────────
        private void CargarExcepciones()
        {
            try
            {
                var excepciones = _repo.ObtenerPorSede(_sede.IdSede, _idInstitucion);
                lstExcepciones.ItemsSource = excepciones;

                if (_excepcionSeleccionada != null)
                {
                    foreach (HorarioExcepcion exc in lstExcepciones.Items)
                    {
                        if (exc.IdExcepcion == _excepcionSeleccionada.IdExcepcion)
                        {
                            lstExcepciones.SelectedItem = exc;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando excepciones: " + ex.Message);
            }
        }

        private void LstExcepciones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _excepcionSeleccionada = lstExcepciones.SelectedItem as HorarioExcepcion;
            MostrarFranjasExcepcion(_excepcionSeleccionada);
        }

        private void MostrarFranjasExcepcion(HorarioExcepcion excepcion)
        {
            pnlFranjasExcepcion.Children.Clear();

            if (excepcion == null)
            {
                lblSeleccioneExcepcion.Visibility = Visibility.Visible;
                lblSinFranjas.Visibility = Visibility.Collapsed;
                pnlEncabezadoFranjas.Visibility = Visibility.Collapsed;
                pnlBtnAgregarFranja.Visibility = Visibility.Collapsed;
                btnEliminarExcepcion.Visibility = Visibility.Collapsed;
                return;
            }

            lblSeleccioneExcepcion.Visibility = Visibility.Collapsed;
            pnlEncabezadoFranjas.Visibility = Visibility.Visible;
            pnlBtnAgregarFranja.Visibility = Visibility.Visible;
            btnEliminarExcepcion.Visibility = Visibility.Visible;
            lblSinFranjas.Visibility = excepcion.Franjas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            void AddH(string text, int col, HorizontalAlignment align = HorizontalAlignment.Left)
            {
                var tb = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 100)),
                    HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tb, col);
                header.Children.Add(tb);
            }
            AddH("Nombre", 0);
            AddH("Entrada", 1, HorizontalAlignment.Center);
            AddH("Tarde", 2, HorizontalAlignment.Center);
            AddH("Cierre", 3, HorizontalAlignment.Center);
            pnlFranjasExcepcion.Children.Add(header);
            pnlFranjasExcepcion.Children.Add(new Border
            {
                Height = 1, Background = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (var f in excepcion.Franjas)
                pnlFranjasExcepcion.Children.Add(CrearFilaFranja(f));
        }

        private Grid CrearFilaFranja(FranjaExcepcion f)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            TextBlock MakeTb(string text, int col, HorizontalAlignment align = HorizontalAlignment.Center)
            {
                var tb = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = align };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
                return tb;
            }

            MakeTb(f.Nombre, 0, HorizontalAlignment.Left);
            MakeTb(f.HoraInicio.ToString(@"hh\:mm"), 1);
            MakeTb(f.HoraLimiteTarde.ToString(@"hh\:mm"), 2);
            MakeTb(f.HoraCierreIngreso.ToString(@"hh\:mm"), 3);

            var btnDel = new Button { Content = "✕", Height = 22, FontSize = 10, Tag = f,
                Margin = new Thickness(2, 0, 0, 0), ToolTip = "Eliminar franja" };
            btnDel.Style = (Style)FindResource("BtnPeligro");
            btnDel.Click += BtnEliminarFranja_Click;
            Grid.SetColumn(btnDel, 4);
            grid.Children.Add(btnDel);
            return grid;
        }

        // ── Agregar excepción ────────────────────────────────────────
        private void BtnAgregarExcepcion_Click(object sender, RoutedEventArgs e)
        {
            if (dpFecha.SelectedDate == null)
            {
                MessageBox.Show("Seleccione una fecha.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string desc = txtDescripcion.Text.Trim();
            if (string.IsNullOrEmpty(desc))
            {
                MessageBox.Show("Ingrese una descripción.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string alcance = AlcanceActual;
            if (alcance == "GRADO" && cmbGradoExc.SelectedItem == null)
            {
                MessageBox.Show("Seleccione un grado.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var exc = new HorarioExcepcion
                {
                    IdSede          = alcance == "INSTITUCION" ? 0 : _sede.IdSede,
                    IdInstitucion   = alcance == "INSTITUCION" ? _idInstitucion : null,
                    IdGrado         = alcance == "GRADO" ? (cmbGradoExc.SelectedItem as Grado)?.IdGrado : null,
                    Alcance         = alcance,
                    FechaExcepcion  = dpFecha.SelectedDate.Value,
                    Descripcion     = desc
                };
                _repo.Guardar(exc);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Excepción creada [{alcance}]: {_sede.NombreSede} - {exc.FechaStr} - {desc}");
                txtDescripcion.Text = "";
                _excepcionSeleccionada = null;
                CargarExcepciones();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error guardando excepción: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminarExcepcion_Click(object sender, RoutedEventArgs e)
        {
            if (_excepcionSeleccionada == null) return;
            var r = MessageBox.Show(
                $"¿Eliminar la excepción del {_excepcionSeleccionada.FechaStr}?\nSe eliminarán también todas sus franjas.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                _repo.Eliminar(_excepcionSeleccionada.IdExcepcion);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Excepción eliminada: {_sede.NombreSede} - {_excepcionSeleccionada.FechaStr}");
                _excepcionSeleccionada = null;
                CargarExcepciones();
                MostrarFranjasExcepcion(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error eliminando excepción: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAgregarFranja_Click(object sender, RoutedEventArgs e)
        {
            if (_excepcionSeleccionada == null) return;
            string nombre = txtFranjaNombre.Text.Trim();
            if (string.IsNullOrEmpty(nombre)) nombre = "Franja";

            if (!TimeSpan.TryParse(txtFranjaEntrada.Text, out var entrada) ||
                !TimeSpan.TryParse(txtFranjaTarde.Text, out var tarde) ||
                !TimeSpan.TryParse(txtFranjaCierre.Text, out var cierre))
            {
                MessageBox.Show("Formato de hora inválido. Use HH:mm (ej: 13:00)", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (tarde < entrada || cierre < tarde)
            {
                MessageBox.Show("Las horas deben ir en orden: Entrada ≤ Límite tardanza ≤ Cierre.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var franja = new FranjaExcepcion
                {
                    IdExcepcion       = _excepcionSeleccionada.IdExcepcion,
                    Nombre            = nombre,
                    HoraInicio        = entrada,
                    HoraLimiteTarde   = tarde,
                    HoraCierreIngreso = cierre,
                    Orden             = _excepcionSeleccionada.Franjas.Count
                };
                _repo.GuardarFranja(franja);
                new LogRepository().Registrar(TipoEvento.CRUD,
                    $"Franja de excepción agregada: {_excepcionSeleccionada.FechaStr} - {nombre}");
                txtFranjaNombre.Text = "Jornada";
                txtFranjaEntrada.Text = "";
                txtFranjaTarde.Text = "";
                txtFranjaCierre.Text = "";
                CargarExcepciones();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error guardando franja: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminarFranja_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not FranjaExcepcion f) return;
            var r = MessageBox.Show($"¿Eliminar la franja '{f.Nombre}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.EliminarFranja(f.IdFranjaExc);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Franja de excepción eliminada: {f.Nombre}");
                CargarExcepciones();
            }
            catch (Exception ex) { MessageBox.Show("Error eliminando franja: " + ex.Message); }
        }

        private void TimeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"[\d:]");
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();
    }
}
