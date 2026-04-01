using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class DashboardPage : Page
    {
        private readonly EstudianteRepository      _repoEst   = new();
        private readonly RegistroIngresoRepository _repoReg   = new();
        private readonly InstitucionRepository     _repoInst  = new();
        private readonly SedeRepository            _repoSede  = new();
        private readonly GradoRepository           _repoGrado = new();
        private readonly GrupoRepository           _repoGrupo = new();

        private string _filtroActivo = "TODOS";
        private List<TextBlock> _lblValores = new();
        private bool _inicializando = false;

        private readonly (string icono, string titulo, string color, string filtro)[] _cardDefs =
        {
            ("👨‍🎓", "Total estudiantes",   "#009688", "TODOS"),
            ("✅",  "Presentes",           "#4CAF50", "PRESENTES"),
            ("❌",  "Ausentes",            "#E53935", "AUSENTES"),
            ("⏰",  "Tardanzas",           "#FB8C00", "TARDANZAS"),
            ("🖐",  "Huellas registradas", "#2196F3", "HUELLAS"),
        };

        // Opciones de período y días que restan al día de hoy
        private static readonly (string label, int dias)[] _periodos =
        {
            ("Hoy",           0),
            ("1 semana",      6),
            ("15 días",      14),
            ("30 días",      29),
            ("Personalizado",-1),
        };

        private (DateTime desde, DateTime hasta) ObtenerRangoFechas()
        {
            var hoy = DateTime.Today;
            int idx = cmbPeriodo.SelectedIndex < 0 ? 0 : cmbPeriodo.SelectedIndex;
            if (idx == 4) // Personalizado
                return (dpDesde.SelectedDate ?? hoy, dpHasta.SelectedDate ?? hoy);
            return (hoy.AddDays(-_periodos[idx].dias), hoy);
        }

        private bool EsRango => cmbPeriodo.SelectedIndex > 0;

        // Institución efectiva según el rol:
        // - SuperAdmin: usa lo que el combo diga (o null = todas)
        // - Administrador/Operador: forzado a su propia institución
        private int? InstFiltroEfectivo
        {
            get
            {
                if (SesionActiva.EsSuperAdmin)
                    return (cmbInstitucion.SelectedItem as Institucion)?.IdInstitucion is int i && i > 0 ? i : (int?)null;
                return SesionActiva.InstitucionActual?.IdInstitucion;
            }
        }

        public DashboardPage()
        {
            InitializeComponent();
            lblFechaCabecera.Text = DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy",
                                        new System.Globalization.CultureInfo("es-CO"));
            Loaded += DashboardPage_Loaded;
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            CargarCombos();
            BuildCards();
            RefrescarTodo();
        }

        private void CargarCombos()
        {
            _inicializando = true;
            // Período
            cmbPeriodo.Items.Clear();
            foreach (var (label, _) in _periodos) cmbPeriodo.Items.Add(label);
            cmbPeriodo.SelectedIndex = 0;

            // Fechas custom: sin fechas futuras
            dpDesde.DisplayDateEnd = DateTime.Today;
            dpHasta.DisplayDateEnd = DateTime.Today;
            dpDesde.SelectedDate   = DateTime.Today;
            dpHasta.SelectedDate   = DateTime.Today;

            cmbInstitucion.Items.Clear();
            cmbInstitucion.DisplayMemberPath = "Nombre";
            cmbInstitucion.SelectionChanged += (s, e) =>
            {
                CargarSedesCombo();
                if (!_inicializando) { _filtroActivo = "TODOS"; RefrescarTodo(); }
            };

            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // Operador / Administrador: solo ve su institución
                cmbInstitucion.Items.Add(SesionActiva.InstitucionActual);
                cmbInstitucion.SelectedIndex = 0;
                cmbInstitucion.IsEnabled = false;
            }
            else
            {
                cmbInstitucion.Items.Add(new Institucion { Nombre = "Todas las instituciones" });
                try { foreach (var i in _repoInst.ObtenerTodas()) cmbInstitucion.Items.Add(i); } catch { }
                cmbInstitucion.SelectedIndex = 0;
            }

            cmbSede.Items.Add(new Sede { NombreSede = "Todas las sedes" });
            cmbSede.SelectedIndex = 0;
            cmbSede.DisplayMemberPath = "NombreSede";

            cmbGrado.Items.Add(new Grado { NombreGrado = "Todos los grados" });
            try { foreach (var g in _repoGrado.ObtenerTodos()) cmbGrado.Items.Add(g); } catch { }
            cmbGrado.SelectedIndex = 0;
            cmbGrado.DisplayMemberPath = "NombreGrado";

            cmbGrupo.Items.Add(new Grupo { NombreGrupo = "Todos los grupos" });
            try { foreach (var g in _repoGrupo.ObtenerTodos()) cmbGrupo.Items.Add(g); } catch { }
            cmbGrupo.SelectedIndex = 0;
            cmbGrupo.DisplayMemberPath = "NombreGrupo";
            _inicializando = false;
        }

        private void CargarSedesCombo()
        {
            var prev = _inicializando;
            _inicializando = true;
            cmbSede.Items.Clear();
            cmbSede.Items.Add(new Sede { NombreSede = "Todas las sedes" });
            if (cmbInstitucion.SelectedItem is Institucion inst && inst.IdInstitucion > 0)
            {
                try { foreach (var s in _repoSede.ObtenerPorInstitucion(inst.IdInstitucion)) cmbSede.Items.Add(s); } catch { }
            }
            cmbSede.SelectedIndex = 0;
            _inicializando = prev;
        }

        private void BuildCards()
        {
            pnlCards.Children.Clear();
            _lblValores.Clear();

            foreach (var (icono, titulo, colorHex, filtro) in _cardDefs)
            {
                var acento = (Color)ColorConverter.ConvertFromString(colorHex);
                var capFiltro = filtro;

                var card = new Border
                {
                    Width = 180, Height = 110,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 14, 0),
                    Cursor = Cursors.Hand
                };
                card.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8, ShadowDepth = 2, Opacity = 0.10, Color = Colors.Black
                };

                var innerGrid = new Grid();

                var accent = new Border
                {
                    Width = 5, HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(acento),
                    CornerRadius = new CornerRadius(6, 0, 0, 6)
                };

                var lblIco = new TextBlock
                {
                    Text = icono, FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 12, 12, 0),
                    Foreground = new SolidColorBrush(acento)
                };

                var lblValor = new TextBlock
                {
                    Text = "—", FontFamily = new FontFamily("Segoe UI Black"),
                    FontSize = 26, Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(16, 18, 0, 0)
                };
                _lblValores.Add(lblValor);

                var lblTit = new TextBlock
                {
                    Text = titulo, FontSize = 10, FontFamily = new FontFamily("Segoe UI"),
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(16, 0, 0, 14)
                };

                innerGrid.Children.Add(accent);
                innerGrid.Children.Add(lblIco);
                innerGrid.Children.Add(lblValor);
                innerGrid.Children.Add(lblTit);
                card.Child = innerGrid;

                card.MouseLeftButtonUp += (s, e) =>
                {
                    _filtroActivo = capFiltro;
                    RefrescarGrid();
                };

                pnlCards.Children.Add(card);
            }
        }

        private void RefrescarTodo() { ActualizarCards(); RefrescarGrid(); }

        private void ActualizarCards()
        {
            if (_lblValores.Count == 0) return;
            try
            {
                var instEfectiva = InstFiltroEfectivo;
                int? idSede  = (cmbSede.SelectedItem  as Sede)?.IdSede  is int s && s > 0 ? s : (int?)null;
                int? idGrado = (cmbGrado.SelectedItem as Grado)?.IdGrado is int g && g > 0 ? g : (int?)null;
                int? idGrupo = (cmbGrupo.SelectedItem as Grupo)?.IdGrupo is int gr && gr > 0 ? gr : (int?)null;

                var todos = _repoEst.ObtenerTodos(idInstitucion: instEfectiva, estado: "ACTIVO");
                if (idSede.HasValue)  todos = todos.FindAll(e => e.IdSede  == idSede);
                if (idGrado.HasValue) todos = todos.FindAll(e => e.IdGrado == idGrado);
                if (idGrupo.HasValue) todos = todos.FindAll(e => e.IdGrupo == idGrupo);

                var (desde, hasta) = ObtenerRangoFechas();
                var registros = _repoReg.ObtenerPorRango(desde, hasta, idSede, idInstitucion: instEfectiva);

                var idsATiempo  = new HashSet<int>(registros.FindAll(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO).ConvertAll(r => r.IdEstudiante));
                var idsTardanza = new HashSet<int>(registros.FindAll(r => r.EstadoIngreso == EstadoIngreso.TARDE).ConvertAll(r => r.IdEstudiante));

                int presentes  = todos.FindAll(e => idsATiempo.Contains(e.IdEstudiante)).Count;
                int tardanzas  = todos.FindAll(e => idsTardanza.Contains(e.IdEstudiante)).Count;
                int ausentes   = todos.Count - presentes - tardanzas;

                var cacheHuellas = CacheHuellas.ObtenerCache();
                int huellas = instEfectiva.HasValue
                    ? cacheHuellas.Count(h => h.IdInstitucion == instEfectiva.Value)
                    : cacheHuellas.Count;

                _lblValores[0].Text = todos.Count.ToString();
                _lblValores[1].Text = presentes.ToString();
                _lblValores[2].Text = ausentes.ToString();
                _lblValores[3].Text = tardanzas.ToString();
                _lblValores[4].Text = huellas.ToString();

                // Resaltar en rojo si las huellas superan el total de estudiantes
                _lblValores[4].Foreground = huellas > todos.Count
                    ? new SolidColorBrush(Color.FromRgb(198, 40, 40))
                    : new SolidColorBrush(Color.FromRgb(40, 40, 40));
            }
            catch (Exception ex) { MessageBox.Show("ActualizarCards: " + ex.Message, "Error"); }
        }

        private void RefrescarGrid()
        {
            try
            {
                var instEfectiva = InstFiltroEfectivo;
                int? idSede  = (cmbSede.SelectedItem  as Sede)?.IdSede  is int s && s > 0 ? s : (int?)null;
                int? idGrado = (cmbGrado.SelectedItem as Grado)?.IdGrado is int g && g > 0 ? g : (int?)null;
                int? idGrupo = (cmbGrupo.SelectedItem as Grupo)?.IdGrupo is int gr && gr > 0 ? gr : (int?)null;

                var (desde, hasta) = ObtenerRangoFechas();

                // Título e columna de fecha según si es rango o día único
                if (!EsRango)
                {
                    lblGridTitulo.Text = desde == DateTime.Today
                        ? "Registros de hoy"
                        : $"Registros de {desde:dd/MM/yyyy}";
                    colFecha.Visibility = System.Windows.Visibility.Collapsed;
                }
                else
                {
                    lblGridTitulo.Text = $"Registros del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
                    colFecha.Visibility = System.Windows.Visibility.Visible;
                }

                var registros = _repoReg.ObtenerPorRango(desde, hasta, idSede, idInstitucion: instEfectiva);

                // Aplicar filtros adicionales de grado/grupo (via estudiante)
                if (idGrado.HasValue || idGrupo.HasValue)
                {
                    var todos = _repoEst.ObtenerTodos(idInstitucion: instEfectiva, estado: "ACTIVO");
                    if (idSede.HasValue)  todos = todos.FindAll(e => e.IdSede  == idSede);
                    if (idGrado.HasValue) todos = todos.FindAll(e => e.IdGrado == idGrado);
                    if (idGrupo.HasValue) todos = todos.FindAll(e => e.IdGrupo == idGrupo);
                    var ids = new HashSet<int>(todos.ConvertAll(e => e.IdEstudiante));
                    registros = registros.FindAll(r => ids.Contains(r.IdEstudiante));
                }

                if (_filtroActivo == "PRESENTES")
                    registros = registros.FindAll(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO);
                else if (_filtroActivo == "TARDANZAS")
                    registros = registros.FindAll(r => r.EstadoIngreso == EstadoIngreso.TARDE);
                else if (_filtroActivo == "AUSENTES")
                {
                    var todos = _repoEst.ObtenerTodos(idInstitucion: instEfectiva, estado: "ACTIVO");
                    if (idSede.HasValue)  todos = todos.FindAll(e => e.IdSede  == idSede);
                    if (idGrado.HasValue) todos = todos.FindAll(e => e.IdGrado == idGrado);
                    if (idGrupo.HasValue) todos = todos.FindAll(e => e.IdGrupo == idGrupo);

                    // Ausente = no tiene registro A_TIEMPO ni TARDE (igual que en ActualizarCards)
                    var idsNoAusentes = new HashSet<int>(registros
                        .FindAll(r => r.EstadoIngreso == EstadoIngreso.A_TIEMPO || r.EstadoIngreso == EstadoIngreso.TARDE)
                        .ConvertAll(r => r.IdEstudiante));
                    var estudiantesAusentes = todos.FindAll(e => !idsNoAusentes.Contains(e.IdEstudiante));

                    // Si tiene registro FUERA_DE_HORARIO se muestra ese; si no tiene ninguno, se crea sintético
                    var fueraDict = registros
                        .FindAll(r => r.EstadoIngreso == EstadoIngreso.FUERA_DE_HORARIO)
                        .ToDictionary(r => r.IdEstudiante);

                    grid.ItemsSource = estudiantesAusentes.Select(e =>
                        fueraDict.TryGetValue(e.IdEstudiante, out var reg) ? reg :
                        new RegistroIngreso
                        {
                            NombreEstudiante = e.NombreCompleto,
                            Identificacion   = e.Identificacion,
                            NombreGrado      = e.NombreGrado,
                            NombreGrupo      = e.NombreGrupo,
                            NombreSede       = e.NombreSede,
                            EstadoIngreso    = EstadoIngreso.FUERA_DE_HORARIO  // IdRegistro=0 → muestra "Ausente"
                        }).ToList();
                    return;
                }

                grid.ItemsSource = registros;
            }
            catch (Exception ex) { MessageBox.Show("RefrescarGrid: " + ex.Message, "Error"); }
        }

        private void CmbPeriodo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pnlFechasCustom == null) return;
            bool esCustom = cmbPeriodo.SelectedIndex == 4;
            pnlFechasCustom.Visibility = esCustom
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (!esCustom)
            {
                _filtroActivo = "TODOS";
                RefrescarTodo();
            }
        }

        private void CmbFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inicializando) return;
            _filtroActivo = "TODOS";
            RefrescarTodo();
        }

        private void DpFecha_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inicializando || cmbPeriodo.SelectedIndex != 4) return;
            _filtroActivo = "TODOS";
            RefrescarTodo();
        }

        private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            _inicializando = true;
            cmbPeriodo.SelectedIndex = 0;
            dpDesde.SelectedDate = DateTime.Today;
            dpHasta.SelectedDate = DateTime.Today;
            cmbInstitucion.SelectedIndex = 0;
            cmbSede.SelectedIndex = 0;
            cmbGrado.SelectedIndex = 0;
            cmbGrupo.SelectedIndex = 0;
            _inicializando = false;
            _filtroActivo = "TODOS";
            RefrescarTodo();
        }
    }
}
