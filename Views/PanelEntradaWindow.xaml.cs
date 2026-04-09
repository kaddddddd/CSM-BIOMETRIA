using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CSMBiometricoWPF.Biometria;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;

namespace CSMBiometricoWPF.Views
{
    public partial class PanelEntradaWindow : Window
    {
        private readonly RegistroIngresoRepository _repo         = new();
        private readonly InstitucionRepository     _instRepo     = new();
        private readonly AsistenciaService         _asistencia   = new();
        private readonly DispatcherTimer           _timerReloj   = new();
        private readonly DispatcherTimer           _timerRefresh =
            new() { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer           _timerOcultar =
            new() { Interval = TimeSpan.FromSeconds(5) };

        private ServicioBiometrico? _biometrico;
        private readonly SedeRepository _sedeRepo = new();

        // Nombre e ID de institución (independiente de la sesión)
        private string _nombreInstitucion = "INSTITUCIÓN";
        private int?   _idInstitucion;
        private int?   _idSede;

        public PanelEntradaWindow()
        {
            InitializeComponent();
            Loaded  += PanelEntradaWindow_Loaded;
            Closed  += PanelEntradaWindow_Closed;
        }

        // ── Carga inicial ───────────────────────────────────────────────
        private void PanelEntradaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CargarLogo();
            IniciarReloj();
            ActualizarEstadoConexion();

            AsistenciaService.IngresoRegistrado += OnNuevoIngreso;

            _timerRefresh.Tick += (_, _) => { ActualizarEstadoConexion(); CargarDatos(); };
            _timerRefresh.Start();

            // Si hay sesión activa, usar esa institución directamente y pasar a sede
            if (SesionActiva.InstitucionActual != null)
            {
                _nombreInstitucion  = SesionActiva.InstitucionActual.Nombre?.ToUpper() ?? "INSTITUCIÓN";
                _idInstitucion      = SesionActiva.InstitucionActual.IdInstitucion;
                lblInstitucion.Text = _nombreInstitucion;
                pnlSelectorInstitucion.Visibility = Visibility.Collapsed;
                MostrarSelectorSede();
            }
            else
            {
                // Sin sesión: cargar selector de institución
                MostrarSelectorInstitucion();
            }
        }

        private void PanelEntradaWindow_Closed(object? sender, EventArgs e)
        {
            AsistenciaService.IngresoRegistrado -= OnNuevoIngreso;
            _timerReloj.Stop();
            _timerRefresh.Stop();
            _timerOcultar.Stop();

            if (_biometrico != null)
            {
                _biometrico.OnCambioEstado           -= OnBiometricoCambioEstado;
                _biometrico.OnImagenCapturada        -= OnBiometricoImagenCapturada;
                _biometrico.OnEstudianteIdentificado -= OnBiometricoEstudianteIdentificado;
                _biometrico.DetenerCaptura();
            }
        }

        // ── Logo ────────────────────────────────────────────────────────
        private void CargarLogo()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           "Images", "csm-logo.png");
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage(new Uri(path));
                    imgLogo.Source         = bmp;
                    imgLogoSelector.Source = bmp;
                }
            }
            catch { /* sin logo */ }
        }

        // ── Selector de institución (modo sin sesión) ───────────────────
        private void MostrarSelectorInstitucion()
        {
            try
            {
                var instituciones = _instRepo.ObtenerTodas(soloActivas: true);

                if (instituciones.Count == 0)
                {
                    lblInstitucion.Text = "SIN INSTITUCIÓN";
                    pnlSelectorInstitucion.Visibility = Visibility.Collapsed;
                    CargarDatos();
                    return;
                }

                if (instituciones.Count == 1)
                {
                    // Una sola institución: selección automática → pasar a sede
                    _nombreInstitucion  = instituciones[0].Nombre.ToUpper();
                    _idInstitucion      = instituciones[0].IdInstitucion;
                    lblInstitucion.Text = _nombreInstitucion;
                    pnlSelectorInstitucion.Visibility = Visibility.Collapsed;
                    MostrarSelectorSede();
                    return;
                }

                // Varias instituciones: mostrar selector
                cmbInstitucionSelector.ItemsSource       = instituciones;
                cmbInstitucionSelector.DisplayMemberPath = "Nombre";
                cmbInstitucionSelector.SelectedIndex     = 0;
                pnlSelectorInstitucion.Visibility        = Visibility.Visible;
            }
            catch
            {
                lblInstitucion.Text = "ERROR DE CONEXIÓN";
                pnlSelectorInstitucion.Visibility = Visibility.Collapsed;
                MostrarSelectorSede();
            }
        }

        private void BtnAbrirPanel_Click(object sender, RoutedEventArgs e)
        {
            if (cmbInstitucionSelector.SelectedItem is Institucion inst)
            {
                _nombreInstitucion  = inst.Nombre.ToUpper();
                _idInstitucion      = inst.IdInstitucion;
                lblInstitucion.Text = _nombreInstitucion;
            }
            pnlSelectorInstitucion.Visibility = Visibility.Collapsed;
            MostrarSelectorSede();
        }

        // ── Selector de sede ─────────────────────────────────────────────
        private void MostrarSelectorSede()
        {
            try
            {
                var sedes = _idInstitucion.HasValue
                    ? _sedeRepo.ObtenerPorInstitucion(_idInstitucion.Value)
                    : _sedeRepo.ObtenerTodas();

                if (sedes.Count == 0)
                {
                    // Sin sedes: continuar sin filtro de sede
                    AplicarSede(null, null);
                    return;
                }

                if (sedes.Count == 1)
                {
                    // Una sola sede: selección automática
                    AplicarSede(sedes[0].IdSede, sedes[0].NombreSede);
                    return;
                }

                // Varias sedes: mostrar selector
                cmbSedeSelector.ItemsSource   = sedes;
                cmbSedeSelector.SelectedIndex = 0;
                pnlSelectorSede.Visibility    = Visibility.Visible;
            }
            catch
            {
                AplicarSede(null, null);
            }
        }

        private void AplicarSede(int? idSede, string? nombreSede)
        {
            _idSede = idSede;
            pnlSelectorSede.Visibility = Visibility.Collapsed;

            // Actualizar subtítulo del encabezado con la sede seleccionada
            lblSubtituloSede.Text = nombreSede != null
                ? $"SEDE: {nombreSede.ToUpper()}  ·  REGISTRO DE INGRESOS"
                : "CONTROL DE ACCESO  ·  REGISTRO DE INGRESOS";

            CargarDatos();
            InicializarLectorAsync();
        }

        private void BtnAbrirPanelSede_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSedeSelector.SelectedItem is Sede sede)
                AplicarSede(sede.IdSede, sede.NombreSede);
            else
                AplicarSede(null, null);
        }

        // ── Reloj ───────────────────────────────────────────────────────
        private void IniciarReloj()
        {
            ActualizarReloj();
            _timerReloj.Interval = TimeSpan.FromSeconds(1);
            _timerReloj.Tick += (_, _) => ActualizarReloj();
            _timerReloj.Start();
        }

        private void ActualizarReloj()
        {
            lblHora.Text  = DateTime.Now.ToString("HH:mm:ss");
            lblFecha.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy",
                                                   new CultureInfo("es-ES")).ToUpper();
        }

        // ── Estado de conexión ──────────────────────────────────────────
        private void ActualizarEstadoConexion()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ActualizarEstadoConexion); return; }
            bool online = ConexionDB.EstaConectado;
            dotConexion.Fill  = new SolidColorBrush(online
                ? Color.FromRgb(34, 197, 94)
                : Color.FromRgb(239, 68, 68));
            lblConexion.Text       = online ? "EN LÍNEA" : "OFFLINE";
            lblConexion.Foreground = new SolidColorBrush(online
                ? Color.FromRgb(74, 222, 128)
                : Color.FromRgb(252, 165, 165));
        }

        // ── Carga de datos ──────────────────────────────────────────────
        private void CargarDatos()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(CargarDatos); return; }

            // No cargar si el selector de institución sigue visible
            if (pnlSelectorInstitucion.Visibility == Visibility.Visible) return;

            try
            {
                var registros = _repo.ObtenerPorFecha(DateTime.Today, idSede: _idSede, idInstitucion: _idInstitucion);

                int total     = registros.Count;
                int aTiempo   = registros.Count(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO);
                int tardanzas = registros.Count(r => r.EstadoIngreso == EstadoIngreso.TARDE);

                lblStatPresentes.Text  = total.ToString();
                lblStatATiempo.Text    = aTiempo.ToString();
                lblStatTardanzas.Text  = tardanzas.ToString();

                // Construir ViewModels: el primero (más reciente) lleva badge NUEVO si fue en los últimos 2 min
                var vms = registros.Select((r, i) => new RegistroIngresoVM(r, i == 0)).ToList();
                listIngresos.ItemsSource = vms;

                pnlSinDatos.Visibility  = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                listIngresos.Visibility = vms.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

                lblUltimaActualizacion.Text =
                    $"Última actualización: {DateTime.Now:HH:mm:ss}" +
                    $"  ·  {total} registro(s) hoy  " +
                    $"  ·  Actualización automática cada 30 s";
            }
            catch
            {
                lblUltimaActualizacion.Text =
                    $"Sin conexión — {DateTime.Now:HH:mm:ss}  ·  Verifique la base de datos";
            }
        }

        // ── Evento en tiempo real desde la plataforma principal ─────────
        private void OnNuevoIngreso(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(CargarDatos);

        // ── Lector biométrico ───────────────────────────────────────────
        private async void InicializarLectorAsync()
        {
            _biometrico = ServicioBiometrico.Compartido;

            if (_idInstitucion.HasValue)
                _biometrico.IdInstitucionFiltro = _idInstitucion;
            if (_idSede.HasValue)
                _biometrico.IdSedeFiltro = _idSede;

            _biometrico.OnCambioEstado           += OnBiometricoCambioEstado;
            _biometrico.OnImagenCapturada        += OnBiometricoImagenCapturada;
            _biometrico.OnEstudianteIdentificado += OnBiometricoEstudianteIdentificado;

            _timerOcultar.Tick += (_, _) =>
            {
                _timerOcultar.Stop();
                pnlResultadoLector.Visibility = Visibility.Collapsed;
                imgHuellaPanel.Source         = null;
                lblInstruccionPanel.Text      = "Coloque su dedo en el sensor";
                IniciarAnimacionPulso();
            };

            bool ok = _biometrico.EstaListo || await _biometrico.InicializarAsync();
            Dispatcher.Invoke(() =>
            {
                if (ok)
                {
                    ActualizarEstadoLector(Colors.LightGreen, "#4CAF50", "Lector listo");
                    IniciarAnimacionPulso();
                    _biometrico.IniciarCaptura(ServicioBiometrico.ModoCaptura.Identificacion);
                }
                else
                {
                    ActualizarEstadoLector(Colors.OrangeRed, "#EF5350", "Error - no se reconoce el lector");
                }
            });
        }

        private void ActualizarEstadoLector(Color dotColor, string hexForeground, string texto)
        {
            dotLector.Fill         = new SolidColorBrush(dotColor);
            lblEstadoLector.Text       = texto;
            lblEstadoLector.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hexForeground));
        }

        private void IniciarAnimacionPulso()
        {
            var anim = new DoubleAnimation
            {
                From = 0.10, To = 0.45,
                Duration = TimeSpan.FromSeconds(1.2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            elpPulso.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
        }

        private void DetenerAnimacionPulso()
        {
            elpPulso.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            elpPulso.Opacity = 0;
        }

        private void OnBiometricoCambioEstado(object? sender, EstadoLector estado)
        {
            Dispatcher.Invoke(() =>
            {
                switch (estado)
                {
                    case EstadoLector.Listo:
                        ActualizarEstadoLector(Colors.LightGreen, "#4CAF50", "Lector listo");
                        IniciarAnimacionPulso();
                        break;
                    case EstadoLector.Capturando:
                        ActualizarEstadoLector(Color.FromRgb(33, 150, 243), "#2196F3", "Leyendo huella...");
                        break;
                    case EstadoLector.Inicializando:
                        ActualizarEstadoLector(Colors.Orange, "#FF9800", "Reconectando lector...");
                        DetenerAnimacionPulso();
                        break;
                    case EstadoLector.Desconectado:
                    case EstadoLector.Error:
                        ActualizarEstadoLector(Colors.OrangeRed, "#EF5350", "Error - no se reconoce el lector");
                        DetenerAnimacionPulso();
                        break;
                }
            });
        }

        private void OnBiometricoImagenCapturada(object? sender, System.Drawing.Bitmap bmp)
        {
            Dispatcher.Invoke(() =>
            {
                if (bmp == null) return;
                DetenerAnimacionPulso();
                lblInstruccionPanel.Text = "Identificando...";
                imgHuellaPanel.Source    = BitmapToImageSource(bmp);
            });
        }

        private void OnBiometricoEstudianteIdentificado(object? sender, ResultadoIdentificacion resultado)
        {
            Dispatcher.Invoke(() =>
            {
                if (resultado.Identificado && resultado.Estudiante != null)
                {
                    try
                    {
                        var (estado, _, nomFranja) = _asistencia.RegistrarIngreso(resultado.Estudiante, resultado.Puntaje);
                        MostrarResultado(resultado.Estudiante, estado, nomFranja);
                        CargarDatos();
                    }
                    catch (Exception ex)
                    {
                        MostrarNoIdentificado();
                        lblEstadoLector.Text = $"⚠  Error al registrar ingreso: {ex.Message}";
                        lblEstadoLector.Foreground = new SolidColorBrush(Color.FromRgb(220, 80, 60));
                    }
                }
                else
                {
                    MostrarNoIdentificado();
                }
            });
        }

        private void MostrarResultado(Estudiante est, EstadoIngreso estado, string? nomFranja = null)
        {
            lblNombreResult.Text = est.NombreCompleto;
            string detalle = $"{est.Identificacion}  ·  {est.NombreSede}  ·  {DateTime.Now:HH:mm:ss}";
            if (!string.IsNullOrWhiteSpace(nomFranja))
                detalle += $"  ·  {nomFranja}";
            lblDetalleResult.Text = detalle;

            Color colorFondo;
            Color colorBadge;
            string textoEstado;
            string instruccion;

            switch (estado)
            {
                case EstadoIngreso.A_TIEMPO:
                    colorFondo  = Color.FromRgb(27, 94, 32);
                    colorBadge  = Color.FromRgb(46, 125, 50);
                    textoEstado = string.IsNullOrWhiteSpace(nomFranja) ? "✓  ACCESO PERMITIDO" : $"✓  ACCESO PERMITIDO  ·  {nomFranja}";
                    instruccion = "Bienvenido";
                    break;
                case EstadoIngreso.TARDE:
                    colorFondo  = Color.FromRgb(130, 80, 0);
                    colorBadge  = Color.FromRgb(230, 81, 0);
                    textoEstado = string.IsNullOrWhiteSpace(nomFranja) ? "⚠  TARDE" : $"⚠  TARDE  ·  {nomFranja}";
                    instruccion = "Ingreso registrado con tardanza";
                    break;
                case EstadoIngreso.YA_REGISTRADO:
                    colorFondo  = Color.FromRgb(13, 60, 110);
                    colorBadge  = Color.FromRgb(21, 101, 192);
                    textoEstado = string.IsNullOrWhiteSpace(nomFranja) ? "●  YA REGISTRADO" : $"●  YA REGISTRADO  ·  {nomFranja}";
                    instruccion = "Su ingreso ya fue registrado hoy";
                    break;
                default:
                    colorFondo  = Color.FromRgb(120, 20, 20);
                    colorBadge  = Color.FromRgb(198, 40, 40);
                    textoEstado = "✗  FUERA DE HORARIO";
                    instruccion = "No hay clase en este momento";
                    break;
            }

            brushResultado.Color        = colorFondo;
            brdEstadoResult.Background  = new SolidColorBrush(colorBadge);
            lblEstadoResult.Text        = textoEstado;
            lblInstruccionPanel.Text    = instruccion;

            pnlResultadoLector.Visibility = Visibility.Visible;
            _timerOcultar.Stop();
            _timerOcultar.Start();
        }

        private void MostrarNoIdentificado()
        {
            brushResultado.Color       = Color.FromRgb(100, 15, 15);
            brdEstadoResult.Background = new SolidColorBrush(Color.FromRgb(183, 28, 28));
            lblNombreResult.Text       = "Huella no registrada";
            lblEstadoResult.Text       = "✗  ACCESO DENEGADO";
            lblDetalleResult.Text      = $"Hora: {DateTime.Now:HH:mm:ss}";
            lblInstruccionPanel.Text   = "Contacte al administrador";

            pnlResultadoLector.Visibility = Visibility.Visible;
            _timerOcultar.Stop();
            _timerOcultar.Start();
        }

        private static ImageSource BitmapToImageSource(System.Drawing.Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption  = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        // ── Botones y teclado ───────────────────────────────────────────
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => CargarDatos();
        private void BtnCerrar_Click(object sender, RoutedEventArgs e)    => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)  Close();
            if (e.Key == Key.F5)      CargarDatos();
            if (e.Key == Key.Enter && pnlSelectorInstitucion.Visibility == Visibility.Visible)
                BtnAbrirPanel_Click(sender, e);
            if (e.Key == Key.Enter && pnlSelectorSede.Visibility == Visibility.Visible)
                BtnAbrirPanelSede_Click(sender, e);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // VIEW MODEL de fila — propiedades listas para binding
    // ════════════════════════════════════════════════════════════════
    public class RegistroIngresoVM
    {
        private readonly RegistroIngreso _r;
        private readonly bool _esReciente;

        public RegistroIngresoVM(RegistroIngreso r, bool esReciente = false)
        {
            _r         = r;
            _esReciente = esReciente;
        }

        // ── Datos ──────────────────────────────────────────────────────
        public string Hora           => _r.HoraIngreso.ToString(@"hh\:mm");
        public string Nombre         => _r.NombreEstudiante ?? "";
        public string Identificacion => _r.Identificacion  ?? "";

        public string GradoGrupo
        {
            get
            {
                var partes = new[] { _r.NombreGrado, _r.NombreGrupo }
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join(" · ", partes);
            }
        }

        // ── Iniciales para el avatar ────────────────────────────────────
        public string Iniciales
        {
            get
            {
                var palabras = Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (palabras.Length == 0) return "?";
                if (palabras.Length == 1) return palabras[0][..1].ToUpper();
                return (palabras[0][..1] + palabras[1][..1]).ToUpper();
            }
        }

        public SolidColorBrush AvatarFondo => _r.EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO         => new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            EstadoIngreso.TARDE            => new SolidColorBrush(Color.FromRgb(230, 81, 0)),
            EstadoIngreso.FUERA_DE_HORARIO => new SolidColorBrush(Color.FromRgb(183, 28, 28)),
            EstadoIngreso.YA_REGISTRADO    => new SolidColorBrush(Color.FromRgb(21, 101, 192)),
            _                              => new SolidColorBrush(Color.FromRgb(69, 90, 100))
        };

        // ── Badge NUEVO ─────────────────────────────────────────────────
        /// <summary>Muestra el badge "NUEVO" si el ingreso fue en los últimos 3 minutos.</summary>
        public Visibility BadgeNuevoVisibility
        {
            get
            {
                if (!_esReciente) return Visibility.Collapsed;
                var horaIngreso = DateTime.Today.Add(_r.HoraIngreso);
                return (DateTime.Now - horaIngreso).TotalMinutes <= 3
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        // ── Estado: texto, colores y borde ─────────────────────────────
        public string EstadoTexto => _r.EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO         => "✓  A TIEMPO",
            EstadoIngreso.TARDE            => "⚠  TARDE",
            EstadoIngreso.FUERA_DE_HORARIO => "✗  INASISTENCIA",
            EstadoIngreso.YA_REGISTRADO    => "●  YA REGISTRADO",
            _                              => _r.EstadoIngreso.ToString()
        };

        public SolidColorBrush EstadoFondo => _r.EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO         => new SolidColorBrush(Color.FromRgb(232, 245, 233)),
            EstadoIngreso.TARDE            => new SolidColorBrush(Color.FromRgb(255, 243, 224)),
            EstadoIngreso.FUERA_DE_HORARIO => new SolidColorBrush(Color.FromRgb(255, 235, 238)),
            EstadoIngreso.YA_REGISTRADO    => new SolidColorBrush(Color.FromRgb(227, 242, 253)),
            _                              => new SolidColorBrush(Color.FromRgb(245, 245, 245))
        };

        public SolidColorBrush EstadoBorde => _r.EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO         => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            EstadoIngreso.TARDE            => new SolidColorBrush(Color.FromRgb(255, 160, 0)),
            EstadoIngreso.FUERA_DE_HORARIO => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            EstadoIngreso.YA_REGISTRADO    => new SolidColorBrush(Color.FromRgb(30, 136, 229)),
            _                              => new SolidColorBrush(Colors.Gray)
        };

        public SolidColorBrush EstadoColor => _r.EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO         => new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            EstadoIngreso.TARDE            => new SolidColorBrush(Color.FromRgb(230, 81, 0)),
            EstadoIngreso.FUERA_DE_HORARIO => new SolidColorBrush(Color.FromRgb(198, 40, 40)),
            EstadoIngreso.YA_REGISTRADO    => new SolidColorBrush(Color.FromRgb(21, 101, 192)),
            _                              => new SolidColorBrush(Colors.Gray)
        };
    }
}
