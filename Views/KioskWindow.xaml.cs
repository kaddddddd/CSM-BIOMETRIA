using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CSMBiometricoWPF.Biometria;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views
{
    public partial class KioskWindow : Window
    {
        private ServicioBiometrico? _biometrico;
        private readonly AsistenciaService _asistencia = new();
        private readonly DispatcherTimer _timerReloj     = new();
        private readonly DispatcherTimer _timerOcultar   = new() { Interval = TimeSpan.FromSeconds(5) };
        private System.Speech.Synthesis.SpeechSynthesizer? _tts;

        private int? _idSede;
        private int? _idInstitucion;

        public KioskWindow()
        {
            InitializeComponent();
            Loaded  += KioskWindow_Loaded;
            Closed  += KioskWindow_Closed;
        }

        private async void KioskWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IniciarReloj();
            IniciarAnimacionPulso();
            try
            {
                _tts = new System.Speech.Synthesis.SpeechSynthesizer();
                _tts.SetOutputToDefaultAudioDevice();
                _tts.Rate = -1; // un poco más lento para mayor claridad
            }
            catch { _tts = null; }

            _timerOcultar.Tick += (_, _) =>
            {
                _timerOcultar.Stop();
                pnlResultado.Visibility = Visibility.Collapsed;
                imgHuella.Source = null;
                lblInstruccion.Text = "Coloque su dedo en el sensor";
                IniciarAnimacionPulso();
            };

            // Resolver institución activa
            if (int.TryParse(
                    System.Configuration.ConfigurationManager.AppSettings["KioskIdInstitucion"], out int idInstCfg)
                && idInstCfg > 0)
                _idInstitucion = idInstCfg;
            else if (SesionActiva.InstitucionActual != null)
                _idInstitucion = SesionActiva.InstitucionActual.IdInstitucion;

            MostrarSelectorSede();
        }

        // ── Selector de sede ────────────────────────────────────────────

        private void MostrarSelectorSede()
        {
            try
            {
                var repoSede = new SedeRepository();
                var sedes = _idInstitucion.HasValue
                    ? repoSede.ObtenerPorInstitucion(_idInstitucion.Value)
                    : repoSede.ObtenerTodas();

                if (sedes.Count == 0)
                {
                    // Sin sedes configuradas: continuar sin filtro de sede
                    AplicarSede(null, "Sistema Biométrico CSM");
                    _ = InicializarLectorAsync();
                    return;
                }

                if (sedes.Count == 1)
                {
                    // Una sola sede: selección automática
                    AplicarSede(sedes[0].IdSede, sedes[0].NombreSede);
                    _ = InicializarLectorAsync();
                    return;
                }

                // Varias sedes: mostrar selector
                lstSedes.ItemsSource       = sedes;
                lstSedes.DisplayMemberPath = "NombreSede";
                pnlSelectorSede.Visibility = Visibility.Visible;
            }
            catch
            {
                AplicarSede(null, "Sistema Biométrico CSM");
                _ = InicializarLectorAsync();
            }
        }

        private void AplicarSede(int? idSede, string nombreSede)
        {
            _idSede = idSede;
            lblNombreSede.Text = nombreSede.ToUpper();
            pnlSelectorSede.Visibility = Visibility.Collapsed;
            if (idSede.HasValue) ActualizarInfoFranja();
        }

        private void LstSedes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            btnConfirmarSede.IsEnabled = lstSedes.SelectedItem != null;
        }

        private void LstSedes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstSedes.SelectedItem is Sede) BtnConfirmarSede_Click(sender, e);
        }

        private async void BtnConfirmarSede_Click(object sender, RoutedEventArgs e)
        {
            if (lstSedes.SelectedItem is not Sede sede) return;
            AplicarSede(sede.IdSede, sede.NombreSede);
            await InicializarLectorAsync();
        }

        private void IniciarReloj()
        {
            ActualizarReloj();
            _timerReloj.Interval = TimeSpan.FromSeconds(1);
            _timerReloj.Tick += (_, _) => ActualizarReloj();
            _timerReloj.Start();
        }

        private int _segundosFranjaCheck = 0;

        private void ActualizarReloj()
        {
            lblHora.Text  = DateTime.Now.ToString("HH:mm:ss");
            lblFecha.Text = DateTime.Now.ToString("dddd, dd MMM yyyy");

            // Actualizar indicador de franja cada 30 segundos
            _segundosFranjaCheck++;
            if (_idSede.HasValue && _segundosFranjaCheck >= 30)
            {
                _segundosFranjaCheck = 0;
                ActualizarInfoFranja();
            }
        }

        private void ActualizarInfoFranja()
        {
            try
            {
                string info = new Repositories.HorarioRepository().ObtenerInfoFranjaActual(_idSede!.Value);
                if (string.IsNullOrEmpty(info))
                {
                    pnlFranjaInfo.Visibility = Visibility.Collapsed;
                }
                else
                {
                    lblFranjaInfo.Text = info;
                    pnlFranjaInfo.Visibility = Visibility.Visible;
                }
            }
            catch { pnlFranjaInfo.Visibility = Visibility.Collapsed; }
        }

        private void IniciarAnimacionPulso()
        {
            var anim = new DoubleAnimation
            {
                From = 0.15, To = 0.55,
                Duration = TimeSpan.FromSeconds(1.2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            elpPulso.BeginAnimation(OpacityProperty, anim);
        }

        private void DetenerAnimacionPulso()
        {
            elpPulso.BeginAnimation(OpacityProperty, null);
            elpPulso.Opacity = 0;
        }

        private async System.Threading.Tasks.Task InicializarLectorAsync()
        {
            _biometrico = ServicioBiometrico.Compartido;

            // Aplicar filtros de institución y sede resueltos en MostrarSelectorSede
            if (_idInstitucion.HasValue)
                _biometrico.IdInstitucionFiltro = _idInstitucion;
            if (_idSede.HasValue)
                _biometrico.IdSedeFiltro = _idSede;

            _biometrico.OnCambioEstado += (s, estado) => Dispatcher.Invoke(() =>
            {
                switch (estado)
                {
                    case EstadoLector.Listo:
                        lblEstadoLector.Text       = "●  Lector listo";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 120));
                        pnlLectorError.Visibility  = Visibility.Collapsed;
                        lblInstruccion.Text        = "Coloque su dedo en el sensor";
                        IniciarAnimacionPulso();
                        break;
                    case EstadoLector.Capturando:
                        lblEstadoLector.Text       = "●  Leyendo huella...";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                        break;
                    case EstadoLector.Error:
                        lblEstadoLector.Text       = "●  Error — verifique el lector USB";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(220, 80, 60));
                        DetenerAnimacionPulso();
                        break;
                    case EstadoLector.Desconectado:
                        lblEstadoLector.Text       = "●  Lector desconectado";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(220, 80, 60));
                        pnlLectorError.Visibility  = Visibility.Visible;
                        DetenerAnimacionPulso();
                        AnunciarDesconexion();
                        break;
                    case EstadoLector.Inicializando:
                        lblEstadoLector.Text       = "⟳  Reconectando lector...";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(100, 140, 200));
                        break;
                }
            });

            _biometrico.OnImagenCapturada += (s, bmp) => Dispatcher.Invoke(() =>
            {
                if (bmp == null) return;
                DetenerAnimacionPulso();
                lblInstruccion.Text = "Identificando...";
                imgHuella.Source = BitmapToImageSource(bmp);
            });

            _biometrico.OnEstudianteIdentificado += (s, resultado) => Dispatcher.Invoke(() =>
            {
                if (resultado.Identificado && resultado.Estudiante != null)
                {
                    try
                    {
                        var (estado, _, nomFranja) = _asistencia.RegistrarIngreso(resultado.Estudiante, resultado.Puntaje);
                        MostrarResultado(resultado.Estudiante, estado, nomFranja);
                    }
                    catch (Exception ex)
                    {
                        // Error al guardar — mostrar el resultado pero indicar el problema
                        lblNombreResult.Text       = resultado.Estudiante.NombreCompleto;
                        lblEstadoResult.Text       = "ERROR AL REGISTRAR";
                        lblEstadoResult.Foreground = new SolidColorBrush(Colors.OrangeRed);
                        lblDetalleResult.Text      = ex.Message;
                        pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(60, 20, 10));
                        imgFotoResultado.Source    = null;
                        lblInstruccion.Text        = "Contacte al administrador";
                        pnlResultado.Visibility    = Visibility.Visible;
                        _timerOcultar.Stop();
                        _timerOcultar.Start();
                    }
                }
                else
                {
                    MostrarNoIdentificado();
                }
            });

            bool ok = await _biometrico.InicializarAsync();
            Dispatcher.Invoke(() =>
            {
                if (ok)
                {
                    lblEstadoLector.Text       = $"●  {_biometrico.InfoLector}";
                    lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 120));
                    _biometrico.IniciarCaptura(ServicioBiometrico.ModoCaptura.Identificacion);
                }
                else
                {
                    lblEstadoLector.Text       = "●  Lector no encontrado — llame al administrador";
                    lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(220, 80, 60));
                    lblInstruccion.Text        = "Sistema no disponible";
                    DetenerAnimacionPulso();
                }
            });
        }

        private void MostrarResultado(Estudiante est, EstadoIngreso estado, string? nomFranja = null)
        {
            lblNombreResult.Text = est.NombreCompleto;

            // Detalle base: ID · Sede · Hora
            string detalle = $"{est.Identificacion}  ·  {est.NombreSede}  ·  {DateTime.Now:HH:mm:ss}";
            // Si viene de una franja específica, añadirla al detalle
            if (!string.IsNullOrWhiteSpace(nomFranja))
                detalle += $"  ·  {nomFranja}";
            lblDetalleResult.Text = detalle;

            switch (estado)
            {
                case EstadoIngreso.A_TIEMPO:
                    pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(10, 26, 16));
                    barAccent.Background       = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // verde vivo
                    lblIconoResult.Text        = "✅";
                    lblEstadoResult.Text       = string.IsNullOrWhiteSpace(nomFranja)
                                                    ? "ACCESO PERMITIDO"
                                                    : $"ACCESO PERMITIDO  ·  {nomFranja}";
                    lblEstadoResult.Foreground = new SolidColorBrush(Color.FromRgb(102, 240, 120));
                    lblInstruccion.Text        = "Bienvenido";
                    break;
                case EstadoIngreso.TARDE:
                    pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(28, 18, 6));
                    barAccent.Background       = new SolidColorBrush(Color.FromRgb(255, 152, 0));   // naranja vivo
                    lblIconoResult.Text        = "⏰";
                    lblEstadoResult.Text       = string.IsNullOrWhiteSpace(nomFranja)
                                                    ? "TARDE"
                                                    : $"TARDE  ·  {nomFranja}";
                    lblEstadoResult.Foreground = new SolidColorBrush(Color.FromRgb(255, 183, 77));
                    lblInstruccion.Text        = "Ingreso registrado con tardanza";
                    break;
                case EstadoIngreso.FUERA_DE_HORARIO:
                    pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(26, 8, 8));
                    barAccent.Background       = new SolidColorBrush(Color.FromRgb(244, 67, 54));   // rojo vivo
                    lblIconoResult.Text        = "⛔";
                    lblEstadoResult.Text       = "INASISTENCIA";
                    lblEstadoResult.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    lblInstruccion.Text        = "Registro fuera del horario permitido";
                    break;
                case EstadoIngreso.YA_REGISTRADO:
                    pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(8, 18, 32));
                    barAccent.Background       = new SolidColorBrush(Color.FromRgb(33, 150, 243));  // azul vivo
                    lblIconoResult.Text        = "🔁";
                    lblEstadoResult.Text       = string.IsNullOrWhiteSpace(nomFranja)
                                                    ? "YA REGISTRADO"
                                                    : $"YA REGISTRADO  ·  {nomFranja}";
                    lblEstadoResult.Foreground = new SolidColorBrush(Color.FromRgb(100, 181, 246));
                    lblInstruccion.Text        = "Su ingreso ya fue registrado hoy";
                    break;
            }

            if (est.Foto != null)
                try { imgFotoResultado.Source = BytesToImageSource(est.Foto); } catch { imgFotoResultado.Source = null; }
            else
                imgFotoResultado.Source = null;

            pnlResultado.Visibility = Visibility.Visible;
            _timerOcultar.Stop();
            _timerOcultar.Start();
        }

        private void MostrarNoIdentificado()
        {
            pnlResultado.Background    = new SolidColorBrush(Color.FromRgb(22, 6, 6));
            barAccent.Background       = new SolidColorBrush(Color.FromRgb(183, 28, 28));          // rojo oscuro
            lblIconoResult.Text        = "✋";
            lblNombreResult.Text       = "Huella no registrada";
            lblEstadoResult.Text       = "ACCESO DENEGADO";
            lblEstadoResult.Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120));
            lblDetalleResult.Text      = $"Hora: {DateTime.Now:HH:mm:ss}";
            imgFotoResultado.Source    = null;
            lblInstruccion.Text        = "Contacte al administrador si es estudiante activo";
            pnlResultado.Visibility    = Visibility.Visible;
            _timerOcultar.Stop();
            _timerOcultar.Start();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Alt+Q para salir del kiosk
            if (e.Key == Key.Q && Keyboard.IsKeyDown(Key.LeftCtrl) && Keyboard.IsKeyDown(Key.LeftAlt))
            {
                if (CustomMessageBox.Show("¿Cerrar el sistema de acceso?", "Salir del kiosk",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    Application.Current.Shutdown();
            }
        }

        private void AnunciarDesconexion()
        {
            try
            {
                _tts?.SpeakAsyncCancelAll();
                _tts?.SpeakAsync("Atención. El lector biométrico está desconectado. Por favor contacte al administrador.");
            }
            catch { }
        }

        private void KioskWindow_Closed(object? sender, EventArgs e)
        {
            _timerReloj.Stop();
            _timerOcultar.Stop();
            _biometrico?.DetenerCaptura();
            try { _tts?.Dispose(); } catch { }
        }

        // ── Helpers para conversión de imágenes ─────────────────
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

        private static ImageSource BytesToImageSource(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
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
