using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CSMBiometricoWPF.Biometria;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class EnrolamientoPage : Page
    {
        private ServicioBiometrico? _biometrico;
        private Estudiante? _estudianteSeleccionado;
        private byte[]? _templateFinal;
        private int _pasoActual = 0;
        private readonly Image[] _imgHuellas;
        private readonly Border[] _pasos;
        private readonly DispatcherTimer _searchTimer;

        private static readonly string[] _instrucciones =
        {
            "Coloque el dedo al CENTRO del sensor.",
            "Incline el dedo ligeramente hacia la DERECHA.",
            "Incline el dedo ligeramente hacia la IZQUIERDA.",
            "Coloque el dedo al CENTRO nuevamente (confirmación)."
        };

        public EnrolamientoPage()
        {
            InitializeComponent();
            _imgHuellas = new[] { imgHuella1, imgHuella2, imgHuella3, imgHuella4 };
            _pasos      = new[] { paso1, paso2, paso3, paso4 };

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); EjecutarBusqueda(); };

            // Usar la instancia compartida — nunca crear una nueva ni disponer.
            _biometrico = ServicioBiometrico.Compartido;
            _biometrico.OnCambioEstado    += OnCambioEstado;
            _biometrico.OnMensaje         += OnMensaje;
            _biometrico.OnImagenCapturada += OnImagenCapturada;
            _biometrico.OnHuellaCapturada += OnHuellaCapturada;

            Unloaded += (_, _) =>
            {
                // Solo desuscribir eventos y detener captura — NUNCA Dispose.
                _biometrico.OnCambioEstado    -= OnCambioEstado;
                _biometrico.OnMensaje         -= OnMensaje;
                _biometrico.OnImagenCapturada -= OnImagenCapturada;
                _biometrico.OnHuellaCapturada -= OnHuellaCapturada;
                _biometrico.DetenerCaptura();
            };
        }

        // ── Búsqueda de estudiante ────────────────────────────

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            if (txtBuscar.Text.Trim().Length >= 2)
                _searchTimer.Start();
            else
            {
                lstResultados.ItemsSource = null;
                popupResultados.IsOpen = false;
            }
        }

        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { _searchTimer.Stop(); EjecutarBusqueda(); }
        }

        private void EjecutarBusqueda()
        {
            string texto = txtBuscar.Text.Trim();
            if (texto.Length < 2) return;
            try
            {
                int? idInst = SesionActiva.EsSuperAdmin ? null : SesionActiva.InstitucionActual?.IdInstitucion;
                var lista = new EstudianteRepository().Buscar(texto, idInst);
                lstResultados.ItemsSource = lista;
                lstResultados.DisplayMemberPath = "NombreCompleto";
                if (lista.Count == 1)
                {
                    popupResultados.IsOpen = false;
                    lstResultados.SelectedIndex = 0;
                }
                else if (lista.Count > 1)
                {
                    popupResultados.IsOpen = true;
                }
                else
                {
                    popupResultados.IsOpen = false;
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error en búsqueda: " + ex.Message);
            }
        }

        private void LstResultados_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstResultados.SelectedItem is not Estudiante est) return;
            popupResultados.IsOpen = false;
            _estudianteSeleccionado = est;
            pnlInfoEstudiante.Visibility = Visibility.Visible;
            lblNombreEst.Text = est.NombreCompleto;
            lblIdEst.Text = $"ID: {est.Identificacion}";
            lblGradoEst.Text = $"{est.NombreGrado} — {est.NombreGrupo}";
            if (est.TieneHuella)
            {
                lblHuellaEst.Text       = "✔ Huella ya registrada";
                lblHuellaEst.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 100));
                pnlInfoEstudiante.Background = new SolidColorBrush(Color.FromRgb(200, 230, 201));
                btnIniciar.Visibility   = Visibility.Collapsed;
                btnReenrolar.Visibility = Visibility.Visible;
                lblInstruccion.Text     = "Este estudiante ya tiene huella registrada. Puede eliminarla y re-enrolar.";
            }
            else
            {
                lblHuellaEst.Text       = "Sin huella registrada";
                lblHuellaEst.Foreground = new SolidColorBrush(Color.FromRgb(119, 119, 119));
                pnlInfoEstudiante.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                btnIniciar.Visibility   = Visibility.Visible;
                btnReenrolar.Visibility = Visibility.Collapsed;
                btnIniciar.IsEnabled    = true;
                lblInstruccion.Text     = "Presione 'Iniciar Enrolamiento' para comenzar.";
            }
        }

        // ── Control del enrolamiento ──────────────────────────

        private void BtnIniciar_Click(object sender, RoutedEventArgs e)
        {
            if (_estudianteSeleccionado == null)
            {
                CustomMessageBox.Show("Seleccione un estudiante primero.", "Validación",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_estudianteSeleccionado.TieneHuella)
            {
                CustomMessageBox.Show(
                    $"{_estudianteSeleccionado.NombreCompleto} ya tiene una huella registrada.",
                    "Huella existente", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ResetearPasos();
            IniciarAsync();
        }

        private void BtnReenrolar_Click(object sender, RoutedEventArgs e)
        {
            if (_estudianteSeleccionado == null) return;

            string nombreEst = _estudianteSeleccionado.NombreCompleto;
            var dlg = new Dialogs.ConfirmarPasswordDialog(
                $"Vas a eliminar la huella de:\n\n{nombreEst}\n\n" +
                $"Esta acción es irreversible. El estudiante deberá enrolarse nuevamente.")
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                new HuellaRepository().EliminarPorEstudiante(_estudianteSeleccionado.IdEstudiante);
                CacheHuellas.Invalidar();

                new LogRepository().Registrar(TipoEvento.SEGURIDAD,
                    $"Huella eliminada para re-enrolamiento: {nombreEst} ({_estudianteSeleccionado.Identificacion}) " +
                    $"— por {SesionActiva.UsuarioActual?.NombreCompleto ?? SesionActiva.UsuarioActual?.Username}");

                // Actualizar estado visual del estudiante
                _estudianteSeleccionado.TieneHuella = false;
                lblHuellaEst.Text       = "Sin huella registrada";
                lblHuellaEst.Foreground = new SolidColorBrush(Color.FromRgb(119, 119, 119));
                pnlInfoEstudiante.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                btnReenrolar.Visibility = Visibility.Collapsed;
                btnIniciar.Visibility   = Visibility.Visible;
                btnIniciar.IsEnabled    = true;

                ResetearPasos();
                IniciarAsync();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error al eliminar la huella: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void IniciarAsync()
        {
            try
            {
                btnIniciar.IsEnabled    = false;
                btnCancelar.IsEnabled   = true;
                btnGuardar.IsEnabled    = false;
                btnReintentar.IsEnabled = false;

                // Inicializar solo si el lector no está listo (primer intento o tras error).
                if (!_biometrico.EstaListo)
                {
                    bool ok = await _biometrico.InicializarAsync();
                    if (!ok)
                    {
                        Dispatcher.Invoke(() => Restaurar());
                        return;
                    }
                }

                _biometrico.IniciarCaptura(ServicioBiometrico.ModoCaptura.Enrolamiento);
                Dispatcher.Invoke(() => lblInstruccion.Text = _instrucciones[0]);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    lblMensaje.Text = "Error: " + ex.Message;
                    Restaurar();
                });
            }
        }

        // ── Eventos del lector ────────────────────────────────

        private void OnCambioEstado(object? sender, EstadoLector estado)
        {
            Dispatcher.Invoke(() =>
            {
                lblEstadoLector.Text = estado.ToString();
                lblEstadoLector.Foreground = estado == EstadoLector.Error
                    ? new SolidColorBrush(Color.FromRgb(198, 40, 40))
                    : new SolidColorBrush(Color.FromRgb(0, 120, 100));
            });
        }

        private void OnMensaje(object? sender, string msg)
        {
            Dispatcher.Invoke(() => lblMensaje.Text = msg);
        }

        private void OnImagenCapturada(object? sender, System.Drawing.Bitmap bmp)
        {
            Dispatcher.Invoke(() =>
            {
                // Mostrar en el slot del paso actual (si aún hay pasos pendientes)
                if (_pasoActual < _imgHuellas.Length && bmp != null)
                    _imgHuellas[_pasoActual].Source = BitmapToImageSource(bmp);
            });
        }

        private void OnHuellaCapturada(object? sender, ResultadoCaptura resultado)
        {
            Dispatcher.Invoke(() =>
            {
                if (resultado.Template != null)
                {
                    // Enrolamiento completo — guardar automáticamente
                    _templateFinal = resultado.Template;
                    MarcarPasoCompletado(_pasoActual);
                    btnCancelar.IsEnabled = false;
                    lblInstruccion.Text = "Guardando huella…";
                    GuardarHuellaAuto();
                    return;
                }

                // Muestra de muestra intermedia
                if (resultado.Exitosa && _pasoActual < _imgHuellas.Length)
                {
                    if (resultado.ImagenHuella != null)
                        _imgHuellas[_pasoActual].Source = BitmapToImageSource(resultado.ImagenHuella);

                    MarcarPasoCompletado(_pasoActual);
                    _pasoActual++;

                    if (_pasoActual < _instrucciones.Length)
                        lblInstruccion.Text = _instrucciones[_pasoActual];
                }
            });
        }

        // ── Botones adicionales ───────────────────────────────

        private void GuardarHuellaAuto()
        {
            if (_templateFinal == null || _estudianteSeleccionado == null) return;
            try
            {
                var huella = new HuellaDigital
                {
                    IdEstudiante       = _estudianteSeleccionado.IdEstudiante,
                    TemplateBiometrico = _templateFinal,
                    Calidad            = 90,
                    Dedo               = TipoDedo.INDICE_D,
                    Activo             = true,
                    RegistradoPor      = SesionActiva.UsuarioActual?.IdUsuario
                };
                new HuellaRepository().Guardar(huella);
                CacheHuellas.Invalidar();

                new LogRepository().Registrar(TipoEvento.ENROLAMIENTO,
                    $"Huella enrolada: {_estudianteSeleccionado.NombreCompleto} ({_estudianteSeleccionado.Identificacion})");

                CustomMessageBox.Show(
                    $"Huella de {_estudianteSeleccionado.NombreCompleto} guardada correctamente.",
                    "Enrolamiento exitoso", MessageBoxButton.OK, MessageBoxImage.Information);

                ResetearPasos();
                Restaurar();
                _templateFinal             = null;
                _estudianteSeleccionado    = null;
                pnlInfoEstudiante.Visibility = Visibility.Collapsed;
                lstResultados.ItemsSource  = null;
                popupResultados.IsOpen     = false;
                txtBuscar.Clear();
                txtBuscar.Focus();
            }
            catch (Exception ex)
            {
                lblInstruccion.Text = "Error al guardar: " + ex.Message;
                btnGuardar.IsEnabled    = true;
                btnReintentar.IsEnabled = true;
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            _biometrico?.DetenerCaptura();
            ResetearPasos();
            Restaurar();
            lblInstruccion.Text = "Enrolamiento cancelado.";
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_templateFinal == null || _estudianteSeleccionado == null) return;
            try
            {
                var huella = new HuellaDigital
                {
                    IdEstudiante        = _estudianteSeleccionado.IdEstudiante,
                    TemplateBiometrico  = _templateFinal,
                    Calidad             = 90,
                    Dedo                = TipoDedo.INDICE_D,
                    Activo              = true,
                    RegistradoPor       = SesionActiva.UsuarioActual?.IdUsuario
                };
                new HuellaRepository().Guardar(huella);
                CacheHuellas.Invalidar();

                new LogRepository().Registrar(TipoEvento.ENROLAMIENTO,
                    $"Huella enrolada: {_estudianteSeleccionado.NombreCompleto} ({_estudianteSeleccionado.Identificacion})");

                CustomMessageBox.Show(
                    $"Huella de {_estudianteSeleccionado.NombreCompleto} guardada correctamente.",
                    "Enrolamiento exitoso", MessageBoxButton.OK, MessageBoxImage.Information);

                ResetearPasos();
                Restaurar();
                _templateFinal = null;
                // Refrescar info del estudiante
                lblHuellaEst.Text = "✔ Huella registrada";
                lblHuellaEst.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 100));
                pnlInfoEstudiante.Background = new SolidColorBrush(Color.FromRgb(200, 230, 201));
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error guardando huella: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReintentar_Click(object sender, RoutedEventArgs e)
        {
            ResetearPasos();
            _templateFinal = null;
            IniciarAsync();
        }

        // ── Helpers ───────────────────────────────────────────

        private void ResetearPasos()
        {
            _pasoActual = 0;
            foreach (var img in _imgHuellas) img.Source = null;
            foreach (var p in _pasos)
                p.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
            lblInstruccion.Text = "Seleccione un estudiante e inicie el enrolamiento.";
        }

        private void MarcarPasoCompletado(int paso)
        {
            if (paso >= 0 && paso < _pasos.Length)
                _pasos[paso].Background = new SolidColorBrush(Color.FromRgb(0, 120, 100));
        }

        private void Restaurar()
        {
            btnIniciar.IsEnabled    = true;
            btnCancelar.IsEnabled   = false;
            btnGuardar.IsEnabled    = false;
            btnReintentar.IsEnabled = false;
        }

        private static ImageSource BitmapToImageSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}
