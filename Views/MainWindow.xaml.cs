using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Formato de subitems: "icono|nombre visible|MODULO"
        private readonly (string icono, string titulo, string[] subitems)[] _menuDefs =
        {
            ("📊", "Estadística",        new[] { "📊|Dashboard|DASHBOARD" }),
            ("📋", "Registro y Control", new[] { "👨‍🎓|Estudiantes|ESTUDIANTES", "🖐|Enrolamiento|ENROLAMIENTO", "✅|Verificación|VERIFICACION", "📅|Consultar Asistencia|CONSULTA" }),
            ("⚙",  "Configuración",     new[] { "🏛|Instituciones|INSTITUCIONES", "🏫|Sedes|SEDES", "📚|Grados|GRADOS", "👥|Grupos|GRUPOS", "🕐|Horarios|HORARIOS", "📅|Períodos académicos|PERIODOS" }),
            ("🔒", "Seguridad",          new[] { "👤|Usuarios|USUARIOS", "📋|Logs|LOGS" }),
            ("🔧", "Utilidades",         new[] { "🔧|Probar Lector|LECTOR" }),
        };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += (_, _) => { _timer.Stop(); _auth.Logout(); };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Imagen de fondo
            try
            {
                string imgPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Images", "background.png");
                if (System.IO.File.Exists(imgPath))
                    imgFondo.Source = new BitmapImage(new Uri(imgPath));
            }
            catch { /* Sin imagen de fondo, continúa normalmente */ }

            // Info del usuario
            var usr = SesionActiva.UsuarioActual;
            lblNombreUsuario.Text = usr?.NombreCompleto ?? "";
            lblInstitucion.Text   = SesionActiva.InstitucionActual?.Nombre ?? "Todas las instituciones";

            // Placeholder del buscador
            txtBuscar.Text = "Buscar formulario...";
            txtBuscar.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.6 };

            ConstruirMenu();
            ConstruirIndiceModulos();
            IniciarReloj();
            CacheHuellas.Actualizar();
            NavegaA("DASHBOARD");
            SincronizarOfflineAsync();
        }

        // ── Índice de módulos para el buscador ─────────────────────────
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

        // ── Construcción del menú colapsable ───────────────────────────
        private void ConstruirMenu()
        {
            pnlMenu.Children.Clear();
            var auth = new AuthService();

            foreach (var (catIcono, titulo, subitems) in _menuDefs)
            {
                // Recopilar subitems visibles (formato: "icono|nombre|MODULO")
                var visibles = new List<(string icono, string nombre, string modulo)>();
                foreach (var si in subitems)
                {
                    var parts = si.Split('|');
                    if (auth.TienePermiso(parts[2]))
                        visibles.Add((parts[0], parts[1], parts[2]));
                }
                if (visibles.Count == 0) continue;

                // ── Panel de items (oculto por defecto) ──────────────
                var pnlItems = new StackPanel
                {
                    Visibility = Visibility.Collapsed,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                };

                foreach (var (itemIcono, nombre, modulo) in visibles)
                {
                    string capModulo = modulo;
                    var itemContent = new StackPanel { Orientation = Orientation.Horizontal };
                    itemContent.Children.Add(new TextBlock
                    {
                        Text = itemIcono,
                        FontSize = 14,
                        Width = 24,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                    itemContent.Children.Add(new TextBlock
                    {
                        Text = nombre,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var btn = new Button
                    {
                        Content = itemContent,
                        Style = (Style)FindResource("BtnMenuPopup"),
                        Tag = modulo
                    };
                    btn.Click += (s, e) => { popupMenu.IsOpen = false; NavegaA(capModulo); };
                    pnlItems.Children.Add(btn);
                }

                // ── Cabecera de categoría (toggle) ────────────────────
                var lblArrow = new TextBlock
                {
                    Text = "›",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64)),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var headerContent = new Grid();
                headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lblTitulo = new TextBlock
                {
                    Text = $"{catIcono}  {titulo}",
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblTitulo, 0);
                Grid.SetColumn(lblArrow, 2);
                headerContent.Children.Add(lblTitulo);
                headerContent.Children.Add(lblArrow);

                var capturedPanel = pnlItems;
                var capturedArrow = lblArrow;
                bool expandido = false;

                var headerBtn = new Button
                {
                    Content = headerContent,
                    Style = (Style)FindResource("BtnMenuCategoria")
                };
                headerBtn.Click += (s, e) =>
                {
                    expandido = !expandido;
                    capturedPanel.Visibility = expandido ? Visibility.Visible : Visibility.Collapsed;
                    capturedArrow.Text = expandido ? "˅" : "›";
                    capturedArrow.FontSize = expandido ? 13 : 16;
                };

                pnlMenu.Children.Add(headerBtn);
                pnlMenu.Children.Add(pnlItems);
            }
        }

        // ── Navegación ──────────────────────────────────────────────────
        public void NavegaA(string modulo)
        {
            Page? page = modulo switch
            {
                "DASHBOARD"     => new DashboardPage(),
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
                _               => null
            };

            if (page != null)
                frameContenido.Navigate(page);
        }

        // ── Abrir / cerrar popup ────────────────────────────────────────
        private void BtnAbrirMenu_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = !popupMenu.IsOpen;
        }

        // ── Buscador ────────────────────────────────────────────────────
        private void TxtBuscar_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtBuscar.Text == "Buscar formulario...")
            {
                txtBuscar.Text = "";
                txtBuscar.Foreground = new SolidColorBrush(Colors.White);
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
            popupBuscar.IsOpen = matches.Count > 0;
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
            txtBuscar.Text = "Buscar formulario...";
            txtBuscar.Foreground = new SolidColorBrush(Colors.White) { Opacity = 0.6 };
        }

        // ── Reloj ───────────────────────────────────────────────────────
        private void IniciarReloj()
        {
            ActualizarReloj();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => ActualizarReloj();
            _timer.Start();
        }

        private void ActualizarReloj()
        {
            lblHora.Text = DateTime.Now.ToString("hh:mm tt");
        }

        // ── Sync offline ────────────────────────────────────────────────
        private async void SincronizarOfflineAsync()
        {
            try
            {
                var svc = new OfflineService();
                if (svc.ContarPendientes() == 0) return;
                int n = await Task.Run(() => svc.SincronizarConMySQL());
                if (n > 0)
                    Dispatcher.Invoke(() =>
                        lblHora.ToolTip = $"✔ {n} registro(s) offline sincronizados");
            }
            catch { }
        }

        // ── Panel de Entrada ────────────────────────────────────────────
        private void BtnPanelEntrada_Click(object sender, RoutedEventArgs e)
        {
            new PanelEntradaWindow().Show();
        }

        // ── Cerrar sesión ───────────────────────────────────────────────
        private void BtnCerrarSesion_Click(object sender, RoutedEventArgs e)
        {
            if (CustomMessageBox.Show("¿Desea cerrar sesión?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _auth.Logout();
                new LoginWindow().Show();
                this.Close();
            }
        }
    }
}
