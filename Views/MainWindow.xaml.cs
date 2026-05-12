using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Pages;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views
{
    public partial class MainWindow : Window
    {
        private readonly AuthService _auth = new();
        private readonly DispatcherTimer _timer = new();

        private record ModuloInfo(string Icono, string Nombre, string Modulo);
        private List<ModuloInfo> _modulosAccesibles = new();

        // MDI state
        private readonly Dictionary<string, Border> _ventanas = new();
        private int _zCounter = 0;
        private double _cascadeOffset = 0;

        // Tabs
        private readonly List<string> _tabsAbiertos = new();
        private string? _moduloActivo;

        // Menu definitions: "icono|nombre visible|MODULO"
        // Iconos Segoe MDL2 Assets seleccionados para ser únicos por módulo:
        //   EDA3=Huella  E80F=Gráfico  E928=VerificaciónMarca  E902=Personas
        //   E71D=Historial  E8B8=Portapapeles  E825=Edificio  E81C=FechaEvento
        //   E787=Calendario  E748=Admin  ED63=ActividadFeed  E914=Sensor/Lector
        //   E707=MapPin  E947=Certificado  E716=Grupo  E840=Escudo
        private readonly (string icono, string titulo, string[] subitems)[] _menuDefs =
        {
            ("\uEDA3", "Modulo Biometrico", new[] { "\uE80F|Dashboard|DASHBOARD",               "\uEDA3|Enrolamiento|ENROLAMIENTO", "\uE928|Verificacion|VERIFICACION" }),
            ("\uE902", "Estudiantes",        new[] { "\uE902|Gestion de Estudiantes|ESTUDIANTES", "\uE71D|Consulta de Asistencia|CONSULTA" }),
            ("\uE8B8", "Administracion",     new[] { "\uE825|Institucion|INSTITUCIONES",          "\uE81C|Horarios|HORARIOS",         "\uE787|Periodos Academicos|PERIODOS" }),
            ("\uE840", "Sistema",            new[] { "\uE748|Usuarios|USUARIOS",                  "\uED63|Registros del Sistema|LOGS", "\uE914|Prueba de Lector|LECTOR" }),
        };

        private static readonly Dictionary<string, (string icono, string titulo)> _titulos = new()
        {
            { "DASHBOARD",     ("\uE80F", "Dashboard") },
            { "INSTITUCIONES", ("\uE825", "Instituciones") },
            { "SEDES",         ("\uE707", "Sedes") },           // MapPin — ubicación de sede
            { "GRADOS",        ("\uE947", "Grados") },          // Certificado/Permisos — nivel académico
            { "GRUPOS",        ("\uE716", "Grupos") },          // Grupo (≠ E902 de Estudiantes)
            { "HORARIOS",      ("\uE81C", "Horarios") },        // EventDate — más específico que reloj
            { "ESTUDIANTES",   ("\uE902", "Estudiantes") },
            { "ENROLAMIENTO",  ("\uEDA3", "Enrolamiento") },    // Huella — único y representativo
            { "CONSULTA",      ("\uE71D", "Consulta de Asistencia") }, // Historial — consulta registros
            { "VERIFICACION",  ("\uE928", "Verificacion") },    // CheckboxComposite — verificación
            { "USUARIOS",      ("\uE748", "Usuarios") },        // Admin — usuarios del sistema
            { "LOGS",          ("\uED63", "Registros del Sistema") }, // ActivityFeed — bitácora
            { "LECTOR",        ("\uE914", "Prueba de Lector") }, // Sensor — dispositivo lector
            { "PERIODOS",      ("\uE787", "Periodos Academicos") },
        };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += (_, _) => { _timer.Stop(); _auth.Logout(); };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Background image
            try
            {
                string imgPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Images", "background.png");
                if (System.IO.File.Exists(imgPath))
                    imgFondo.Source = new BitmapImage(new Uri(imgPath));
            }
            catch { }

            // User info in menu header
            var usr = SesionActiva.UsuarioActual;
            string nombreCompleto = usr?.NombreCompleto ?? "";
            lblMenuNombreUsuario.Text = nombreCompleto;

            // Institution info
            var inst = SesionActiva.InstitucionActual;
            lblInstPopup.Text = inst?.Nombre ?? "Todas las instituciones";

            // Institution logo — dinámico por institución activa
            try
            {
                string? logoPath = null;
                var instLogo = SesionActiva.InstitucionActual;
                if (!string.IsNullOrEmpty(instLogo?.LogoPath))
                {
                    string candidato = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "Images", "logos", instLogo.LogoPath);
                    if (System.IO.File.Exists(candidato))
                        logoPath = candidato;
                }
                if (logoPath == null)
                {
                    string fallback = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "Images", "logo.png");
                    if (System.IO.File.Exists(fallback)) logoPath = fallback;
                }
                if (logoPath != null)
                    imgLogoPopup.Source = new BitmapImage(new Uri(logoPath));
                else
                    imgLogoPopup.Visibility = Visibility.Collapsed;
            }
            catch { imgLogoPopup.Visibility = Visibility.Collapsed; }

            // Search placeholder
            txtBuscar.Text       = "Buscar formulario...";
            txtBuscar.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

            ConstruirMenu();
            ConstruirIndiceModulos();
            IniciarReloj();
            CacheHuellas.Actualizar();
            NavegaA("DASHBOARD");
            SincronizarOfflineAsync();
        }

        // -- Module index for search ------------------------------------------
        private void ConstruirIndiceModulos()
        {
            var auth = new AuthService();
            _modulosAccesibles.Clear();
            foreach (var (_, _, subitems) in _menuDefs)
                foreach (var si in subitems)
                {
                    var p = si.Split('|');
                    if (auth.TienePermiso(p[2]))
                        _modulosAccesibles.Add(new ModuloInfo(p[0], p[1], p[2]));
                }
        }

        // -- Menu popup -------------------------------------------------------
        private DispatcherTimer? _hideSubmenuTimer;

        private void ConstruirMenu()
        {
            pnlMenuItems.Children.Clear();
            var auth = new AuthService();

            // Timer para ocultar submenú con pequeño delay (evita parpadeo al cruzar paneles)
            _hideSubmenuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _hideSubmenuTimer.Tick += (_, __) =>
            {
                _hideSubmenuTimer.Stop();
                pnlMenuDerSubmenu.Visibility = Visibility.Collapsed;
                pnlMenuDerDefault.Visibility = Visibility.Visible;
            };

            // Mantener submenú visible mientras el cursor esté sobre el panel derecho completo
            pnlMenuDerPanel.MouseEnter += (_, __) => _hideSubmenuTimer?.Stop();
            pnlMenuDerPanel.MouseLeave += (_, __) => _hideSubmenuTimer?.Start();
            pnlMenuDerSubmenu.MouseEnter += (_, __) => _hideSubmenuTimer?.Stop();
            pnlMenuDerSubmenu.MouseLeave += (_, __) => _hideSubmenuTimer?.Start();

            bool primero = true;
            foreach (var (catIcono, titulo, subitems) in _menuDefs)
            {
                var visibles = new List<(string icono, string nombre, string modulo)>();
                foreach (var si in subitems)
                {
                    var parts = si.Split('|');
                    if (auth.TienePermiso(parts[2]))
                        visibles.Add((parts[0], parts[1], parts[2]));
                }
                if (visibles.Count == 0) continue;

                if (!primero)
                    pnlMenuItems.Children.Add(new Border
                    {
                        Height     = 1,
                        Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))
                    });
                primero = false;

                // Botón de categoría: icono + nombre + chevron
                var catGrid = new Grid();
                catGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                catGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                catGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                catGrid.Children.Add(new TextBlock
                {
                    Text                = catIcono,
                    FontFamily          = new FontFamily("Segoe MDL2 Assets"),
                    FontSize            = 15,
                    Foreground          = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var tituloTb = new TextBlock
                {
                    Text              = titulo,
                    FontFamily        = new FontFamily("Segoe UI"),
                    FontSize          = 13,
                    Foreground        = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tituloTb, 1);
                catGrid.Children.Add(tituloTb);

                var chevronTb = new TextBlock
                {
                    Text                = "\uE76C",
                    FontFamily          = new FontFamily("Segoe MDL2 Assets"),
                    FontSize            = 11,
                    Foreground          = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(chevronTb, 2);
                catGrid.Children.Add(chevronTb);

                var capturedVisibles = new List<(string icono, string nombre, string modulo)>(visibles);
                var btnCat = new Button
                {
                    Content             = catGrid,
                    Style               = (Style)FindResource("BtnFlyoutOpcion"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Hover: mostrar submenú al pasar el cursor
                btnCat.MouseEnter += (s, ev) =>
                {
                    _hideSubmenuTimer?.Stop();
                    MostrarSubmenu(capturedVisibles);
                };
                btnCat.MouseLeave += (s, ev) => _hideSubmenuTimer?.Start();

                // Click: si tiene un solo módulo, navegar directo
                btnCat.Click += (s, ev) =>
                {
                    if (capturedVisibles.Count == 1)
                    {
                        popupMenu.IsOpen = false;
                        NavegaA(capturedVisibles[0].modulo);
                    }
                };

                pnlMenuItems.Children.Add(btnCat);
            }
        }

        private void MostrarSubmenu(List<(string icono, string nombre, string modulo)> items)
        {
            pnlMenuDerDefault.Visibility  = Visibility.Collapsed;
            pnlMenuDerSubmenu.Children.Clear();
            pnlMenuDerSubmenu.Visibility  = Visibility.Visible;


            // Items del submenú
            foreach (var (icono, nombre, modulo) in items)
            {
                string capMod = modulo;
                var itemGrid = new Grid();
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                itemGrid.Children.Add(new TextBlock
                {
                    Text              = icono,
                    FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                    FontSize          = 13,
                    Foreground        = new SolidColorBrush(Color.FromRgb(0x00, 0x96, 0x78)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                var nameTb = new TextBlock
                {
                    Text              = nombre,
                    FontFamily        = new FontFamily("Segoe UI"),
                    FontSize          = 12,
                    Foreground        = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping      = TextWrapping.Wrap
                };
                Grid.SetColumn(nameTb, 1);
                itemGrid.Children.Add(nameTb);

                var subBtn = new Button
                {
                    Content             = itemGrid,
                    Style               = (Style)FindResource("BtnFlyoutOpcion"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                subBtn.Click += (_, __) => { popupMenu.IsOpen = false; NavegaA(capMod); };
                pnlMenuDerSubmenu.Children.Add(subBtn);
            }
        }

        private void BtnAbrirMenu_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.PlacementTarget = btnAbrirMenu;
            if (!popupMenu.IsOpen)
            {
                _hideSubmenuTimer?.Stop();
                pnlMenuDerSubmenu.Visibility = Visibility.Collapsed;
                pnlMenuDerDefault.Visibility = Visibility.Visible;
            }
            popupMenu.IsOpen = !popupMenu.IsOpen;
        }

        // -- Navigation (MDI) ------------------------------------------------
        public void NavegaA(string modulo)
        {
            if (modulo == "DASHBOARD")
            {
                mdiContainer.Visibility = Visibility.Collapsed;
                frameDashboard.Navigate(new DashboardPage());
                _moduloActivo = "DASHBOARD";
                ActualizarEstadoTabs();
                return;
            }

            mdiContainer.Visibility = Visibility.Visible;

            // If window already open, bring to front
            if (_ventanas.TryGetValue(modulo, out var existente))
            {
                RestaurarVentana(existente);
                BringToFront(existente);
                _moduloActivo = modulo;
                ActualizarEstadoTabs();
                return;
            }

            // Create new MDI window
            var ventana = CrearVentanaMDI(modulo);
            _ventanas[modulo] = ventana;

            double left = 30 + _cascadeOffset;
            double top  = 20 + _cascadeOffset;
            _cascadeOffset += 28;
            if (_cascadeOffset > 160) _cascadeOffset = 0;

            Canvas.SetLeft(ventana, left);
            Canvas.SetTop(ventana, top);
            mdiContainer.Children.Add(ventana);

            BringToFront(ventana);
            _moduloActivo = modulo;

            if (!_tabsAbiertos.Contains(modulo))
            {
                _tabsAbiertos.Add(modulo);
                AgregarTabUI(modulo);
            }
            ActualizarEstadoTabs();
        }

        private Page CrearPagina(string modulo) => modulo switch
        {
            "INSTITUCIONES" => new InstitucionesPage(),
            "SEDES"         => new SedesPage(),
            "GRADOS"        => new GradosPage(),
            "GRUPOS"        => new GruposPage(),
            "HORARIOS"      => new HorariosPage(),
            "ESTUDIANTES"   => new EstudiantesPage(),
            "ENROLAMIENTO"  => new EnrolamientoPage(),
            "CONSULTA"      => new ConsultaAsistenciaPage(),
            "VERIFICACION"  => new VerificacionPage(),
            "USUARIOS"      => new UsuariosPage(),
            "LOGS"          => new LogsPage(),
            "LECTOR"        => new PruebaLectorPage(),
            "PERIODOS"      => new PeriodosPage(),
            _               => new DashboardPage()
        };

        // -- MDI window creation ---------------------------------------------
        private Border CrearVentanaMDI(string modulo)
        {
            if (!_titulos.TryGetValue(modulo, out var info))
                info = ("\uE8A0", modulo);

            var ventana = new Border
            {
                Width        = 900,
                Height       = 580,
                CornerRadius = new CornerRadius(8),
                Background   = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                Tag          = modulo,
                Effect       = new DropShadowEffect
                {
                    BlurRadius  = 24,
                    ShadowDepth = 4,
                    Opacity     = 0.25,
                    Color       = Colors.Black,
                    Direction   = 270
                }
            };

            var grid = new Grid();
            var titleRow   = new RowDefinition { Height = new GridLength(36) };
            var contentRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            grid.RowDefinitions.Add(titleRow);
            grid.RowDefinitions.Add(contentRow);
            ventana.Child = grid;

            // â"€â"€ Title bar â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            var titleBar = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Cursor       = Cursors.SizeAll
            };
            Grid.SetRow(titleBar, 0);

            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleIcon = new TextBlock
            {
                Text              = info.icono,
                FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                FontSize          = 13,
                Foreground        = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(12, 0, 8, 0)
            };
            var titleText = new TextBlock
            {
                Text              = info.titulo,
                FontFamily        = new FontFamily("Segoe UI Semibold"),
                FontSize          = 13,
                Foreground        = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var btnMin = new Button
            {
                Content = new TextBlock
                {
                    Text              = "\uE921",
                    FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                    FontSize          = 10,
                    Foreground        = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Style   = (Style)FindResource("BtnVentana"),
                ToolTip = "Minimizar"
            };

            var iconoMaxRestaurar = new TextBlock
            {
                Text              = "\uE922",
                FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                FontSize          = 10,
                Foreground        = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            var btnMax = new Button
            {
                Content = iconoMaxRestaurar,
                Style   = (Style)FindResource("BtnVentana"),
                ToolTip = "Maximizar"
            };

            var btnClose = new Button
            {
                Content = new TextBlock
                {
                    Text              = "\uE8BB",
                    FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                    FontSize          = 10,
                    Foreground        = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Style   = (Style)FindResource("BtnVentanaCerrar"),
                ToolTip = "Cerrar"
            };

            btnPanel.Children.Add(btnMin);
            btnPanel.Children.Add(btnMax);
            btnPanel.Children.Add(btnClose);

            Grid.SetColumn(titleIcon, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(btnPanel,  2);
            titleGrid.Children.Add(titleIcon);
            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(btnPanel);
            titleBar.Child = titleGrid;
            grid.Children.Add(titleBar);

            // â"€â"€ Content frame â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            var frame = new Frame
            {
                NavigationUIVisibility = NavigationUIVisibility.Hidden,
                Background             = Brushes.White
            };
            Grid.SetRow(frame, 1);
            grid.Children.Add(frame);
            frame.Navigate(CrearPagina(modulo));

            // Ocultar encabezado interno de la página (evita título duplicado)
            frame.Navigated += (_, nav) =>
            {
                if (nav.Content is Page pg && pg.Content is Grid pgGrid
                    && pgGrid.RowDefinitions.Count > 0
                    && pgGrid.RowDefinitions[0].Height == new GridLength(60))
                    pgGrid.RowDefinitions[0].Height = new GridLength(0);
            };

            // â"€â"€ State â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            bool minimizado = false;
            bool maximizado = false;
            double savedLeft = 30, savedTop = 20, savedWidth = 900, savedHeight = 580;

            // ── Drag (usa RenderTransform para evitar lag) ─────────────────
            var dragTranslate = new System.Windows.Media.TranslateTransform();
            ventana.RenderTransform = dragTranslate;

            Point dragOffset = default;

            titleBar.MouseLeftButtonDown += (s, ev) =>
            {
                if (ev.ClickCount == 2) return;
                BringToFront(ventana);
                _moduloActivo = modulo;
                ActualizarEstadoTabs();
                var pos = ev.GetPosition(mdiContainer);
                dragOffset = new Point(
                    pos.X - Canvas.GetLeft(ventana),
                    pos.Y - Canvas.GetTop(ventana));
                titleBar.CaptureMouse();
                ev.Handled = true;
            };
            titleBar.MouseMove += (s, ev) =>
            {
                if (!titleBar.IsMouseCaptured) return;
                var pos = ev.GetPosition(mdiContainer);
                double nx = pos.X - dragOffset.X;
                double ny = pos.Y - dragOffset.Y;
                nx = Math.Max(-(ventana.ActualWidth - 60), Math.Min(mdiContainer.ActualWidth - 60, nx));
                ny = Math.Max(0, Math.Min(mdiContainer.ActualHeight - 36, ny));
                // Mover con transform (sin recálculo de layout = sin lag)
                dragTranslate.X = nx - Canvas.GetLeft(ventana);
                dragTranslate.Y = ny - Canvas.GetTop(ventana);
            };
            titleBar.MouseLeftButtonUp += (s, ev) =>
            {
                if (!titleBar.IsMouseCaptured) return;
                // Confirmar posición y resetear transform
                Canvas.SetLeft(ventana, Canvas.GetLeft(ventana) + dragTranslate.X);
                Canvas.SetTop (ventana, Canvas.GetTop (ventana) + dragTranslate.Y);
                dragTranslate.X = 0;
                dragTranslate.Y = 0;
                titleBar.ReleaseMouseCapture();
            };

            // â"€â"€ Double-click title bar: maximize / restore â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            titleBar.MouseLeftButtonDown += (s, ev) =>
            {
                if (ev.ClickCount != 2) return;
                if (maximizado)
                {
                    ventana.Width  = savedWidth;
                    ventana.Height = savedHeight;
                    Canvas.SetLeft(ventana, savedLeft);
                    Canvas.SetTop(ventana, savedTop);
                    contentRow.Height      = new GridLength(1, GridUnitType.Star);
                    iconoMaxRestaurar.Text = "\uE922";
                    maximizado = false;
                    minimizado = false;
                }
                else
                {
                    savedLeft   = Canvas.GetLeft(ventana);
                    savedTop    = Canvas.GetTop(ventana);
                    savedWidth  = ventana.ActualWidth;
                    savedHeight = ventana.ActualHeight;
                    ventana.Width  = mdiContainer.ActualWidth;
                    ventana.Height = mdiContainer.ActualHeight;
                    Canvas.SetLeft(ventana, 0);
                    Canvas.SetTop(ventana, 0);
                    contentRow.Height      = new GridLength(1, GridUnitType.Star);
                    iconoMaxRestaurar.Text = "\uE923";
                    maximizado = true;
                    minimizado = false;
                }
            };

            // â"€â"€ Minimize â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            btnMin.Click += (s, ev) =>
            {
                ev.Handled = true;
                if (minimizado)
                {
                    contentRow.Height = new GridLength(1, GridUnitType.Star);
                    ventana.Height    = savedHeight;
                    ventana.Width     = savedWidth;
                    minimizado = false;
                }
                else
                {
                    if (!maximizado)
                    {
                        savedHeight = ventana.ActualHeight;
                        savedWidth  = ventana.ActualWidth;
                        savedLeft   = Canvas.GetLeft(ventana);
                        savedTop    = Canvas.GetTop(ventana);
                    }
                    maximizado        = false;
                    contentRow.Height = new GridLength(0);
                    ventana.Height    = 36;
                    iconoMaxRestaurar.Text = "\uE922";
                    // Centrar la ventana minimizada
                    Canvas.SetLeft(ventana, (mdiContainer.ActualWidth  - savedWidth) / 2);
                    Canvas.SetTop (ventana, (mdiContainer.ActualHeight - 36)         / 2);
                    ventana.Width  = savedWidth;
                    minimizado = true;
                }
                ActualizarEstadoTabs();
            };

            // â"€â"€ Maximize / Restore â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            btnMax.Click += (s, ev) =>
            {
                ev.Handled = true;
                if (minimizado)
                {
                    contentRow.Height = new GridLength(1, GridUnitType.Star);
                    ventana.Height    = savedHeight;
                    ventana.Width     = savedWidth;
                    Canvas.SetLeft(ventana, savedLeft);
                    Canvas.SetTop(ventana, savedTop);
                    minimizado = false;
                    return;
                }
                if (maximizado)
                {
                    ventana.Width  = savedWidth;
                    ventana.Height = savedHeight;
                    Canvas.SetLeft(ventana, savedLeft);
                    Canvas.SetTop(ventana, savedTop);
                    contentRow.Height      = new GridLength(1, GridUnitType.Star);
                    iconoMaxRestaurar.Text = "\uE922";
                    maximizado = false;
                }
                else
                {
                    savedLeft   = Canvas.GetLeft(ventana);
                    savedTop    = Canvas.GetTop(ventana);
                    savedWidth  = ventana.ActualWidth;
                    savedHeight = ventana.ActualHeight;
                    ventana.Width  = mdiContainer.ActualWidth;
                    ventana.Height = mdiContainer.ActualHeight;
                    Canvas.SetLeft(ventana, 0);
                    Canvas.SetTop(ventana, 0);
                    contentRow.Height      = new GridLength(1, GridUnitType.Star);
                    iconoMaxRestaurar.Text = "\uE923";
                    maximizado = true;
                }
                ActualizarEstadoTabs();
            };

            // â"€â"€ Close â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            btnClose.Click += (s, ev) =>
            {
                ev.Handled = true;
                CerrarVentana(modulo);
            };

            // â"€â"€ Click anywhere: bring to front â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            ventana.MouseLeftButtonDown += (s, ev) =>
            {
                BringToFront(ventana);
                _moduloActivo = modulo;
                ActualizarEstadoTabs();
            };

            // ── Redimensionar desde cualquier borde ──────────────────────────
            const int rb = 6;
            bool resizing = false;
            string rDir   = "";
            Point  rStart = default;
            double rL = 0, rT = 0, rW = 0, rH = 0;

            ventana.MouseMove += (s, ev) =>
            {
                if (maximizado) { ventana.Cursor = null; return; }
                if (resizing)
                {
                    var cur = ev.GetPosition(mdiContainer);
                    double dx = cur.X - rStart.X, dy = cur.Y - rStart.Y;
                    if (rDir.Contains("L")) { double nw = Math.Max(400, rW - dx); Canvas.SetLeft(ventana, rL + rW - nw); ventana.Width  = nw; }
                    if (rDir.Contains("R")) ventana.Width  = Math.Max(400, rW + dx);
                    if (rDir.Contains("T")) { double nh = Math.Max(200, rH - dy); Canvas.SetTop(ventana,  rT + rH - nh); ventana.Height = nh; }
                    if (rDir.Contains("B")) ventana.Height = Math.Max(200, rH + dy);
                    return;
                }
                var p = ev.GetPosition(ventana);
                double w = ventana.ActualWidth, h = ventana.ActualHeight;
                bool eL = p.X < rb, eR = p.X > w - rb, eT = p.Y < rb, eB = p.Y > h - rb;
                ventana.Cursor = (eT && eL) || (eB && eR) ? Cursors.SizeNWSE
                               : (eT && eR) || (eB && eL) ? Cursors.SizeNESW
                               : eL || eR ? Cursors.SizeWE
                               : eT || eB ? Cursors.SizeNS
                               : null;
            };
            ventana.MouseLeave += (s, ev) => { if (!resizing) ventana.Cursor = null; };

            ventana.PreviewMouseLeftButtonDown += (s, ev) =>
            {
                if (maximizado || minimizado) return;
                var p = ev.GetPosition(ventana);
                double w = ventana.ActualWidth, h = ventana.ActualHeight;
                bool eL = p.X < rb, eR = p.X > w - rb, eT = p.Y < rb, eB = p.Y > h - rb;
                if (!(eL || eR || eT || eB)) return;
                resizing = true;
                rStart = ev.GetPosition(mdiContainer);
                rL = Canvas.GetLeft(ventana); rT = Canvas.GetTop(ventana);
                rW = ventana.ActualWidth;     rH = ventana.ActualHeight;
                rDir = eT && eL ? "TL" : eT && eR ? "TR" : eB && eL ? "BL" : eB && eR ? "BR"
                     : eL ? "L" : eR ? "R" : eT ? "T" : "B";
                ventana.CaptureMouse();
                ev.Handled = true;
            };
            ventana.PreviewMouseLeftButtonUp += (s, ev) =>
            {
                if (!resizing) return;
                resizing = false;
                ventana.ReleaseMouseCapture();
                if (!maximizado) { savedWidth = ventana.Width; savedHeight = ventana.Height; }
            };

            // Abrir maximizado por defecto
            ventana.Loaded += (s, ev) =>
            {
                savedLeft   = Canvas.GetLeft(ventana);
                savedTop    = Canvas.GetTop(ventana);
                ventana.Width  = mdiContainer.ActualWidth;
                ventana.Height = mdiContainer.ActualHeight;
                Canvas.SetLeft(ventana, 0);
                Canvas.SetTop(ventana, 0);
                iconoMaxRestaurar.Text = "\uE923";
                maximizado = true;
            };

            return ventana;
        }

        private void RestaurarVentana(Border ventana)
        {
            if (ventana.Height > 36) return;
            if (ventana.Child is Grid g && g.RowDefinitions.Count >= 2)
                g.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            ventana.Height = 580;
            ventana.Width  = 900;
        }

        private void BringToFront(Border ventana)
        {
            Canvas.SetZIndex(ventana, ++_zCounter);
        }

        private void CerrarVentana(string modulo)
        {
            if (_ventanas.TryGetValue(modulo, out var ventana))
            {
                mdiContainer.Children.Remove(ventana);
                _ventanas.Remove(modulo);
            }

            _tabsAbiertos.Remove(modulo);
            var tabUI = pnlTabs.Children.OfType<Border>()
                .FirstOrDefault(b => (string?)b.Tag == modulo);
            if (tabUI != null) pnlTabs.Children.Remove(tabUI);

            if (_moduloActivo == modulo)
            {
                var siguiente = _tabsAbiertos.LastOrDefault();
                if (siguiente != null)
                {
                    _moduloActivo = siguiente;
                    if (_ventanas.TryGetValue(siguiente, out var v))
                    {
                        RestaurarVentana(v);
                        BringToFront(v);
                    }
                }
                else
                {
                    _moduloActivo = "DASHBOARD";
                }
            }
            ActualizarEstadoTabs();
        }

        // -- Tabs -------------------------------------------------------------
        private void AgregarTabUI(string modulo)
        {
            if (!_titulos.TryGetValue(modulo, out var info)) return;

            var tab = new Border
            {
                Tag             = modulo,
                CornerRadius    = new CornerRadius(14),
                BorderThickness = new Thickness(1.5),
                Padding         = new Thickness(10, 0, 6, 0),
                Height          = 30,
                Margin          = new Thickness(0, 0, 6, 0),
                Cursor          = Cursors.Hand,
                Background      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };

            var inner = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            inner.Children.Add(new TextBlock
            {
                Text              = info.icono,
                FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C)),
                Margin            = new Thickness(0, 0, 6, 0)
            });

            var labelTb = new TextBlock
            {
                Text              = info.titulo,
                FontFamily        = new FontFamily("Segoe UI"),
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };
            inner.Children.Add(labelTb);

            string capModulo = modulo;
            var btnX = new Button
            {
                Content = new TextBlock
                {
                    Text              = "\uE8BB",
                    FontFamily        = new FontFamily("Segoe MDL2 Assets"),
                    FontSize          = 8,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Style  = (Style)FindResource("BtnTabCerrar"),
                Margin = new Thickness(6, 0, 0, 0)
            };
            btnX.Click += (s, ev) => { ev.Handled = true; CerrarVentana(capModulo); };
            inner.Children.Add(btnX);
            tab.Child = inner;

            // Hover: text grows
            tab.MouseEnter += (s, ev) =>
            {
                labelTb.BeginAnimation(TextBlock.FontSizeProperty,
                    new DoubleAnimation(13.5, TimeSpan.FromMilliseconds(130)));
                labelTb.FontWeight = FontWeights.SemiBold;
            };
            tab.MouseLeave += (s, ev) =>
            {
                labelTb.BeginAnimation(TextBlock.FontSizeProperty,
                    new DoubleAnimation(12.0, TimeSpan.FromMilliseconds(130)));
                labelTb.FontWeight = (capModulo == _moduloActivo)
                    ? FontWeights.SemiBold : FontWeights.Normal;
            };

            // Click: bring window to front
            tab.MouseLeftButtonUp += (s, ev) =>
            {
                if (ev.Handled) return;
                mdiContainer.Visibility = Visibility.Visible;
                _moduloActivo = capModulo;
                if (_ventanas.TryGetValue(capModulo, out var v))
                {
                    RestaurarVentana(v);
                    BringToFront(v);
                }
                ActualizarEstadoTabs();
            };

            pnlTabs.Children.Add(tab);
        }

        private void ActualizarEstadoTabs()
        {
            foreach (var tab in pnlTabs.Children.OfType<Border>())
            {
                bool activo = (string?)tab.Tag == _moduloActivo;
                tab.BorderBrush = new SolidColorBrush(
                    activo ? Color.FromRgb(0x1A, 0xBC, 0x9C)
                           : Color.FromRgb(0xCC, 0xCC, 0xCC));
                tab.Background = Brushes.White;

                if (tab.Child is StackPanel sp && sp.Children.Count > 1
                    && sp.Children[1] is TextBlock tb)
                    tb.FontWeight = activo ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // -- Search -----------------------------------------------------------
        private void TxtBuscar_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtBuscar.Text == "Buscar formulario...")
            {
                txtBuscar.Text       = "";
                txtBuscar.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            }
        }

        private void TxtBuscar_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!popupBuscar.IsOpen)
                RestablecerPlaceholder();
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = txtBuscar.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(q))
            {
                popupBuscar.IsOpen = false;
                return;
            }

            var matches = _modulosAccesibles
                .Where(m => m.Nombre.ToLower().Contains(q))
                .ToList();

            lstSugerencias.ItemsSource = matches;
            popupBuscar.IsOpen         = matches.Count > 0;
        }

        private void TxtBuscar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var primero = lstSugerencias.Items.OfType<ModuloInfo>().FirstOrDefault();
                if (primero != null) NavegarYLimpiar(primero.Modulo);
            }
            else if (e.Key == Key.Down && popupBuscar.IsOpen)
            {
                lstSugerencias.Focus();
                lstSugerencias.SelectedIndex = 0;
            }
            else if (e.Key == Key.Escape)
            {
                popupBuscar.IsOpen = false;
            }
        }

        private void LstSugerencias_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstSugerencias.SelectedItem is ModuloInfo item)
                NavegarYLimpiar(item.Modulo);
        }

        private void LstSugerencias_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && lstSugerencias.SelectedItem is ModuloInfo item)
                NavegarYLimpiar(item.Modulo);
            else if (e.Key == Key.Escape)
            {
                popupBuscar.IsOpen = false;
                txtBuscar.Focus();
            }
        }

        private void NavegarYLimpiar(string modulo)
        {
            popupBuscar.IsOpen = false;
            RestablecerPlaceholder();
            NavegaA(modulo);
        }

        private void RestablecerPlaceholder()
        {
            txtBuscar.Text       = "Buscar formulario...";
            txtBuscar.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        }

        // -- Clock -----------------------------------------------------------
        private void IniciarReloj()
        {
            ActualizarReloj();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick    += (_, _) => ActualizarReloj();
            _timer.Start();
        }

        private void ActualizarReloj()
        {
            lblHora.Text = DateTime.Now.ToString("hh:mm tt");
        }

        // -- Offline sync ----------------------------------------------------
        private async void SincronizarOfflineAsync()
        {
            try
            {
                var svc = new OfflineService();
                if (svc.ContarPendientes() == 0) return;
                int n = await Task.Run(() => svc.SincronizarConMySQL());
                if (n > 0)
                    Dispatcher.Invoke(() =>
                        lblHora.ToolTip = $"{n} registro(s) offline sincronizados");
            }
            catch { }
        }

        // -- Panel Entrada ---------------------------------------------------
        private void BtnPanelEntrada_Click(object sender, RoutedEventArgs e)
        {
            new PanelEntradaWindow().Show();
        }

        // -- Cerrar sesion ---------------------------------------------------
        private void BtnCerrarSesion_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            if (CustomMessageBox.Show("Desea cerrar sesion?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _auth.Logout();
                new LoginWindow().Show();
                this.Close();
            }
        }
    }
}