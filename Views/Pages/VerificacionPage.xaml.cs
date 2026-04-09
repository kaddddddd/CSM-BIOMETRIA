using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CSMBiometricoWPF.Biometria;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Services;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class VerificacionPage : Page
    {
        private ServicioBiometrico? _biometrico;
        private readonly AsistenciaService _asistenciaService = new();

        public VerificacionPage()
        {
            InitializeComponent();

            _biometrico = ServicioBiometrico.Compartido;
            _biometrico.IdInstitucionFiltro      = SesionActiva.InstitucionActual?.IdInstitucion;
            _biometrico.OnCambioEstado           += OnCambioEstado;
            _biometrico.OnMensaje                += OnMensaje;
            _biometrico.OnImagenCapturada        += OnImagenCapturada;
            _biometrico.OnEstudianteIdentificado += OnEstudianteIdentificado;

            Unloaded += (_, _) =>
            {
                _biometrico.OnCambioEstado           -= OnCambioEstado;
                _biometrico.OnMensaje                -= OnMensaje;
                _biometrico.OnImagenCapturada        -= OnImagenCapturada;
                _biometrico.OnEstudianteIdentificado -= OnEstudianteIdentificado;
                _biometrico.DetenerCaptura();
            };
        }

        // ── Eventos del servicio biométrico ──────────────────

        private void OnCambioEstado(object? sender, EstadoLector estado)
        {
            Dispatcher.Invoke(() =>
            {
                lblEstadoLector.Text = estado switch
                {
                    EstadoLector.Desconectado  => "Lector desconectado",
                    EstadoLector.Inicializando => "Inicializando...",
                    EstadoLector.Listo         => "Lector listo",
                    EstadoLector.Capturando    => "Capturando huella...",
                    EstadoLector.Error         => "Error en el lector",
                    _                          => estado.ToString()
                };

                lblEstadoLector.Foreground = estado switch
                {
                    EstadoLector.Listo      => new SolidColorBrush(Color.FromRgb(0, 120, 100)),
                    EstadoLector.Capturando => new SolidColorBrush(Color.FromRgb(0, 150, 120)),
                    EstadoLector.Error      => new SolidColorBrush(Color.FromRgb(198, 40, 40)),
                    _                       => new SolidColorBrush(Color.FromRgb(96, 125, 139))
                };
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
                if (bmp != null)
                    imgHuella.Source = BitmapToImageSource(bmp);
            });
        }

        private void OnEstudianteIdentificado(object? sender, ResultadoIdentificacion resultado)
        {
            Dispatcher.Invoke(() =>
            {
                if (!resultado.Identificado)
                {
                    MostrarResultado(null, null);
                    lblMensaje.Text = resultado.Mensaje;
                    return;
                }

                try
                {
                    var (estado, mensaje, _) = _asistenciaService.RegistrarIngreso(
                        resultado.Estudiante, resultado.Puntaje);
                    MostrarResultado(resultado.Estudiante, estado);
                    lblMensaje.Text = mensaje;
                }
                catch (Exception ex)
                {
                    lblMensaje.Text = "Error registrando asistencia: " + ex.Message;
                }
            });
        }

        // ── UI helpers ──────────────────────────────────────

        private void MostrarResultado(Estudiante? est, EstadoIngreso? estado)
        {
            if (est == null)
            {
                pnlResultado.Background = new SolidColorBrush(Color.FromRgb(255, 243, 224));
                brdEstadoIngreso.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                lblEstadoIngreso.Text = "NO IDENTIFICADO";
                lblNombreEstudiante.Text = "Estudiante no encontrado";
                lblIdentificacion.Text = "";
                lblGradoGrupo.Text = "";
                lblHoraIngreso.Text = DateTime.Now.ToString("HH:mm:ss");
                imgEstudiante.Source = null;
                pnlResultado.Visibility = Visibility.Visible;
                return;
            }

            // Color de fondo según estado de ingreso
            Color colorFondo, colorBadge;
            string textoEstado;
            switch (estado)
            {
                case EstadoIngreso.A_TIEMPO:
                    colorFondo  = Color.FromRgb(232, 245, 233);
                    colorBadge  = Color.FromRgb(46, 125, 50);
                    textoEstado = "A TIEMPO";
                    break;
                case EstadoIngreso.TARDE:
                    colorFondo  = Color.FromRgb(255, 243, 224);
                    colorBadge  = Color.FromRgb(230, 81, 0);
                    textoEstado = "TARDE";
                    break;
                case EstadoIngreso.YA_REGISTRADO:
                    colorFondo  = Color.FromRgb(227, 242, 253);
                    colorBadge  = Color.FromRgb(21, 101, 192);
                    textoEstado = "YA REGISTRADO";
                    break;
                default:
                    colorFondo  = Color.FromRgb(255, 235, 238);
                    colorBadge  = Color.FromRgb(198, 40, 40);
                    textoEstado = "FUERA DE HORARIO";
                    break;
            }

            pnlResultado.Background = new SolidColorBrush(colorFondo);
            brdEstadoIngreso.Background = new SolidColorBrush(colorBadge);
            lblEstadoIngreso.Text = textoEstado;
            lblNombreEstudiante.Text = est.NombreCompleto;
            lblIdentificacion.Text = est.Identificacion;
            lblGradoGrupo.Text = $"{est.NombreGrado} — {est.NombreGrupo}";
            lblHoraIngreso.Text = DateTime.Now.ToString("HH:mm:ss");

            // Foto del estudiante
            if (est.Foto != null && est.Foto.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(est.Foto);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    imgEstudiante.Source = bi;
                }
                catch { imgEstudiante.Source = null; }
            }
            else
            {
                imgEstudiante.Source = null;
            }

            pnlResultado.Visibility = Visibility.Visible;
        }

        // ── Botones ─────────────────────────────────────────

        private void BtnIniciar_Click(object sender, RoutedEventArgs e)
        {
            btnIniciar.IsEnabled = false;
            btnDetener.IsEnabled = true;
            pnlResultado.Visibility = Visibility.Collapsed;
            lblMensaje.Text = "Inicializando...";
            IniciarAsync();
        }

        private async void IniciarAsync()
        {
            try
            {
                bool ok = _biometrico.EstaListo || await _biometrico.InicializarAsync();
                if (ok)
                    _biometrico.IniciarCaptura(ServicioBiometrico.ModoCaptura.Identificacion);
                else
                    Dispatcher.Invoke(() =>
                    {
                        btnIniciar.IsEnabled = true;
                        btnDetener.IsEnabled = false;
                    });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    lblMensaje.Text = "Error: " + ex.Message;
                    btnIniciar.IsEnabled = true;
                    btnDetener.IsEnabled = false;
                });
            }
        }

        private void BtnDetener_Click(object sender, RoutedEventArgs e)
        {
            _biometrico?.DetenerCaptura();
            btnIniciar.IsEnabled = true;
            btnDetener.IsEnabled = false;
        }

        // ── Helper Bitmap → ImageSource ──────────────────────

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
