using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

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

        private string _filtroActivo = "AUSENTES"; // Por defecto: nunca asistieron
        private List<TextBlock> _lblValores = new();
        private readonly Dictionary<string, Border> _cardMap = new();
        private bool _inicializando = false;
        private List<EstudianteDashboardVM> _vmsActuales = new();

        private readonly (string icono, string titulo, string color, string filtro)[] _cardDefs =
        {
            ("\uE716", "Total estudiantes",   "#009688", "TODOS"),
            ("\uE73E", "Presentes",           "#4CAF50", "PRESENTES"),
            ("\uE783", "Parcial",             "#FF9800", "PARCIALES"),
            ("\uE711", "Ausentes",            "#E53935", "AUSENTES"),
            ("\uE823", "Tardanzas",           "#FB8C00", "TARDANZAS"),
            ("\uED5F", "Huellas registradas", "#2196F3", "HUELLAS"),
        };

        // Opciones de período y días que restan al día de hoy
        private static readonly (string label, int dias)[] _periodosFijos =
        {
            ("Hoy",      0),
            ("1 semana", 6),
            ("15 días", 14),
            ("30 días", 29),
        };

        private List<PeriodoAcademico> _periodosDB = new();

        private (DateTime desde, DateTime hasta) ObtenerRangoFechas()
        {
            var hoy        = DateTime.Today;
            int idx        = cmbPeriodo.SelectedIndex < 0 ? 0 : cmbPeriodo.SelectedIndex;
            int totalFijos = _periodosFijos.Length;

            if (idx == totalFijos + _periodosDB.Count) // Personalizado
                return (dpDesde.SelectedDate ?? hoy, dpHasta.SelectedDate ?? hoy);

            if (idx >= totalFijos && idx < totalFijos + _periodosDB.Count)
            {
                var p = _periodosDB[idx - totalFijos];
                return (new DateTime(hoy.Year, p.MesInicio, p.DiaInicio),
                        new DateTime(hoy.Year, p.MesFin,    p.DiaFin));
            }

            return (hoy.AddDays(-_periodosFijos[idx].dias), hoy);
        }

        private bool EsRango => cmbPeriodo.SelectedIndex > 0;

        // Institución efectiva según el rol:
        // - SuperAdmin: usa lo que el combo diga (o null = todas)
        // - Administrador/Docente: forzado a su propia institución
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
            // Período — carga desde DB
            _periodosDB = new PeriodoAcademicoRepository()
                .ObtenerPorInstitucion(SesionActiva.InstitucionActual?.IdInstitucion);
            cmbPeriodo.Items.Clear();
            foreach (var (label, _) in _periodosFijos) cmbPeriodo.Items.Add(label);
            foreach (var p in _periodosDB)              cmbPeriodo.Items.Add(p.Nombre);
            cmbPeriodo.Items.Add("Personalizado");
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
                if (!_inicializando) { _filtroActivo = "AUSENTES"; RefrescarTodo(); }
            };

            if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
            {
                // Docente / Administrador: solo ve su institución
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

            cmbGrado.Items.Add(new Grado { NombreGrado = "Todos los grados" });
            try { foreach (var g in _repoGrado.ObtenerTodos()) cmbGrado.Items.Add(g); } catch { }
            cmbGrado.SelectedIndex = 0;
            cmbGrado.DisplayMemberPath = "NombreGrado";

            cmbGrupo.Items.Add(new Grupo { NombreGrupo = "Todos los grupos" });
            cmbGrupo.SelectedIndex = 0;
            cmbGrupo.DisplayMemberPath = "NombreGrupo";
            cmbGrupo.IsEnabled = false;
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
            _cardMap.Clear();

            foreach (var (icono, titulo, colorHex, filtro) in _cardDefs)
            {
                var acento = (Color)ColorConverter.ConvertFromString(colorHex);
                var capFiltro = filtro;

                var card = new Border
                {
                    Height = 110,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Margin = new Thickness(6, 0, 6, 0),
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
                    Text = icono, FontSize = 24,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
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
                    RefrescarGrid(_vmsActuales);
                };

                pnlCards.Children.Add(card);
                _cardMap[capFiltro] = card;
            }
        }

        private void HighlightActiveCard()
        {
            foreach (var kv in _cardMap)
            {
                kv.Value.BorderBrush = kv.Key == _filtroActivo
                    ? new SolidColorBrush(Color.FromRgb(0, 120, 100))
                    : Brushes.Transparent;
            }
        }

        private void RefrescarTodo()
        {
            if (_lblValores.Count == 0) return;
            try
            {
                _vmsActuales = ComputarVMs();
                ActualizarCards(_vmsActuales);
                RefrescarGrid(_vmsActuales);
            }
            catch (Exception ex) { CustomMessageBox.Show("RefrescarTodo: " + ex.Message, "Error"); }
        }

        // ── Cálculo único de VMs (fuente de verdad para tarjetas Y grid) ─────────
        private List<EstudianteDashboardVM> ComputarVMs()
        {
            var instEfectiva = InstFiltroEfectivo;
            int? idSede  = (cmbSede.SelectedItem  as Sede)?.IdSede  is int s  && s  > 0 ? s  : (int?)null;
            int? idGrado = (cmbGrado.SelectedItem as Grado)?.IdGrado is int g  && g  > 0 ? g  : (int?)null;
            int? idGrupo = (cmbGrupo.SelectedItem as Grupo)?.IdGrupo is int gr && gr > 0 ? gr : (int?)null;

            var todos = _repoEst.ObtenerTodos(idInstitucion: instEfectiva, estado: "ACTIVO");
            if (idSede.HasValue)  todos = todos.FindAll(e => e.IdSede  == idSede);
            if (idGrado.HasValue) todos = todos.FindAll(e => e.IdGrado == idGrado);
            if (idGrupo.HasValue) todos = todos.FindAll(e => e.IdGrupo == idGrupo);

            var (desde, hasta) = ObtenerRangoFechas();
            var registros = _repoReg.ObtenerPorRango(desde, hasta, idSede, idInstitucion: instEfectiva);

            var sedeIds = todos.Select(e => e.IdSede).Distinct().ToList();
            var repoHorario = new HorarioRepository();
            var slotsPorSedeYDia          = repoHorario.ObtenerSlotsPorSedes(sedeIds);
            var nombresFranjasPorSedeYDia = repoHorario.ObtenerNombrasFranjasPorSedes(sedeIds);

            var regsPorEstDia = registros
                .Where(r => r.EstadoIngreso != EstadoIngreso.YA_REGISTRADO)
                .GroupBy(r => (r.IdEstudiante, r.FechaIngreso.Date))
                .ToDictionary(g => g.Key, g => g.ToList());

            var diasRango = Enumerable
                .Range(0, (hasta - desde).Days + 1)
                .Select(d => desde.AddDays(d).Date)
                .Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                .ToList();

            return todos.Select(e =>
            {
                int diasPresentes = 0, diasATiempo = 0, diasTarde = 0;
                var faltas = new List<FaltaDetalle>();

                foreach (var dia in diasRango)
                {
                    string diaStr = DiaSemanaStr(dia.DayOfWeek);
                    int slotsEsperados = slotsPorSedeYDia.TryGetValue((e.IdSede, diaStr), out int sl) ? sl : 1;
                    var nombresFranjas = nombresFranjasPorSedeYDia.TryGetValue((e.IdSede, diaStr), out var nf)
                        ? nf : new List<string>();

                    if (regsPorEstDia.TryGetValue((e.IdEstudiante, dia), out var regs))
                    {
                        var mejor = regs.MinBy(r => (int)r.EstadoIngreso)!;

                        if      (mejor.EstadoIngreso == EstadoIngreso.A_TIEMPO) { diasPresentes++; diasATiempo++; }
                        else if (mejor.EstadoIngreso == EstadoIngreso.TARDE)    { diasPresentes++; diasTarde++; }
                        else faltas.Add(new FaltaDetalle { Fecha = dia, Estado = "Ausente" });

                        int franjasAusentes = slotsEsperados - regs.Count;
                        if (franjasAusentes > 0 && mejor.EstadoIngreso != EstadoIngreso.FUERA_DE_HORARIO)
                        {
                            var cubiertas = regs.Where(r => r.NombreFranja != null).Select(r => r.NombreFranja).ToHashSet();
                            var faltantes = nombresFranjas.Where(n => !cubiertas.Contains(n)).ToList();
                            for (int f = 0; f < franjasAusentes; f++)
                                faltas.Add(new FaltaDetalle
                                {
                                    Fecha        = dia,
                                    Estado       = "Franja ausente",
                                    NombreFranja = f < faltantes.Count ? faltantes[f] : null
                                });
                        }
                    }
                    else
                    {
                        for (int f = 0; f < slotsEsperados; f++)
                            faltas.Add(new FaltaDetalle
                            {
                                Fecha        = dia,
                                Estado       = nombresFranjas.Count > 0 ? "Franja ausente" : "Ausente",
                                NombreFranja = f < nombresFranjas.Count ? nombresFranjas[f] : null
                            });
                    }
                }

                return new EstudianteDashboardVM
                {
                    NombreEstudiante = e.NombreCompleto,
                    Identificacion   = e.Identificacion,
                    NombreGrado      = e.NombreGrado,
                    NombreGrupo      = e.NombreGrupo,
                    NombreSede       = e.NombreSede,
                    Total            = todos.Count,
                    DiasPresentes    = diasPresentes,
                    DiasATiempo      = diasATiempo,
                    DiasTarde        = diasTarde,
                    TotalFaltas      = faltas.Count,
                    Faltas           = faltas.OrderBy(f => f.Fecha).ToList()
                };
            }).ToList();
        }

        // ── Tarjetas: derivadas 100% de los VMs (nunca habrá negativos) ──────────
        private void ActualizarCards(List<EstudianteDashboardVM> vms)
        {
            if (_lblValores.Count == 0) return;

            // Presente = sin ninguna falta
            // Parcial  = asistió algún día pero faltó a alguna franja
            // Ausente  = nunca asistió en el período
            // Tardanza = llegó tarde al menos un día (puede solaparse)
            int total     = vms.Count;
            int presentes = vms.Count(v => v.TotalFaltas == 0 && v.DiasATiempo > 0);
            int parciales = vms.Count(v => v.FaltasFranja > 0 && v.DiasPresentes > 0);
            int ausentes  = vms.Count(v => v.DiasPresentes == 0 && v.TotalFaltas > 0);
            int tardanzas = vms.Count(v => v.DiasTarde > 0);

            var instEfectiva = InstFiltroEfectivo;
            var cacheHuellas = CacheHuellas.ObtenerCache();
            int huellas = instEfectiva.HasValue
                ? cacheHuellas.Count(h => h.IdInstitucion == instEfectiva.Value)
                : cacheHuellas.Count;

            _lblValores[0].Text = total.ToString();
            _lblValores[1].Text = presentes.ToString();
            _lblValores[2].Text = parciales.ToString();
            _lblValores[3].Text = ausentes.ToString();
            _lblValores[4].Text = tardanzas.ToString();
            _lblValores[5].Text = huellas.ToString();

            _lblValores[5].Foreground = huellas > total
                ? new SolidColorBrush(Color.FromRgb(198, 40, 40))
                : new SolidColorBrush(Color.FromRgb(40, 40, 40));
        }

        // ── Grid: filtra los VMs ya calculados ────────────────────────────────────
        private void RefrescarGrid(List<EstudianteDashboardVM> vms)
        {
            var (desde, hasta) = ObtenerRangoFechas();
            string tituloFecha = !EsRango
                ? (desde == DateTime.Today ? "Asistencia de hoy" : $"Asistencia del {desde:dd/MM/yyyy}")
                : $"Asistencia del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
            string filtroLabel = _filtroActivo switch
            {
                "TODOS"      => "Todos los estudiantes",
                "PRESENTES"  => "Sin faltas",
                "PARCIALES"  => "Asistencia parcial (faltó a alguna franja)",
                "AUSENTES"   => "Sin ningún registro en el período",
                "TARDANZAS"  => "Con tardanzas",
                _            => "Todos"
            };
            lblGridTitulo.Text = $"{tituloFecha}  —  {filtroLabel}";

            IEnumerable<EstudianteDashboardVM> query = _filtroActivo switch
            {
                "TODOS"      => vms,
                "PRESENTES"  => vms.Where(v => v.TotalFaltas == 0 && v.DiasATiempo > 0),
                "PARCIALES"  => vms.Where(v => v.FaltasFranja > 0 && v.DiasPresentes > 0),
                "AUSENTES"   => vms.Where(v => v.DiasPresentes == 0 && v.TotalFaltas > 0),
                "TARDANZAS"  => vms.Where(v => v.DiasTarde > 0),
                _            => vms
            };

            grid.ItemsSource = query.OrderBy(v => v.NombreEstudiante).ToList();
            HighlightActiveCard();
        }

        // Mapea DayOfWeek al string de DiaSemana usado en la BD
        private static string DiaSemanaStr(DayOfWeek dow) => dow switch
        {
            DayOfWeek.Monday    => "LUNES",
            DayOfWeek.Tuesday   => "MARTES",
            DayOfWeek.Wednesday => "MIERCOLES",
            DayOfWeek.Thursday  => "JUEVES",
            DayOfWeek.Friday    => "VIERNES",
            DayOfWeek.Saturday  => "SABADO",
            _                   => "DOMINGO"
        };

        private void BtnExpandir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is EstudianteDashboardVM vm)
                vm.Expandido = !vm.Expandido;
        }

        private void CmbPeriodo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pnlFechasCustom == null) return;
            bool esCustom = cmbPeriodo.SelectedIndex == _periodosFijos.Length + _periodosDB.Count;
            pnlFechasCustom.Visibility = esCustom
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (!esCustom)
                RefrescarTodo();
        }

        private void CmbGrado_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inicializando) return;
            ActualizarComboGrupo();
            RefrescarTodo();
        }

        private void ActualizarComboGrupo()
        {
            var prev = _inicializando;
            _inicializando = true;
            cmbGrupo.Items.Clear();
            cmbGrupo.Items.Add(new Grupo { NombreGrupo = "Todos los grupos" });
            if (cmbGrado.SelectedItem is Grado g && g.IdGrado > 0)
            {
                try
                {
                    var gr = _repoGrupo.ObtenerPorGrado(g.IdGrado);
                    if (gr != null) cmbGrupo.Items.Add(gr);
                }
                catch { }
                cmbGrupo.IsEnabled = true;
            }
            else
            {
                cmbGrupo.IsEnabled = false;
            }
            cmbGrupo.SelectedIndex = 0;
            _inicializando = prev;
        }

        private void CmbFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inicializando) return;
            RefrescarTodo();
        }

        private void DpFecha_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_inicializando || cmbPeriodo.SelectedIndex != 8) return;
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
            ActualizarComboGrupo();
            _inicializando = false;
            _filtroActivo = "AUSENTES";
            RefrescarTodo();
        }

        private void cmbInstitucion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void TabGraficas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabGraficas?.IsSelected == true)
                DibujarGraficas();
            if (tabGradosGrupos?.IsSelected == true)
                CargarGradosGrupos();
        }

        private void CargarGradosGrupos()
        {
            try
            {
                var instEfectiva = InstFiltroEfectivo;
                int? idSede = (cmbSede.SelectedItem as Sede)?.IdSede is int s && s > 0 ? s : (int?)null;

                var todos = _repoEst.ObtenerTodos(idInstitucion: instEfectiva, estado: "ACTIVO");
                if (idSede.HasValue) todos = todos.FindAll(e => e.IdSede == idSede);

                var grados = _repoGrado.ObtenerTodos();

                var vms = grados.Select(g =>
                {
                    var grupo = _repoGrupo.ObtenerPorGrado(g.IdGrado);
                    int count = todos.Count(e => e.IdGrado == g.IdGrado);
                    return new GradoGrupoVM
                    {
                        NombreGrado      = g.NombreGrado,
                        NombreGrupo      = grupo?.NombreGrupo ?? "—",
                        TotalEstudiantes = count
                    };
                }).ToList();

                gridGradosGrupos.ItemsSource = vms;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("CargarGradosGrupos: " + ex.Message, "Error");
            }
        }

        private void CanvasDias_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DibujarBarrasDias();
        }

        private void DibujarGraficas()
        {
            if (_vmsActuales == null || _vmsActuales.Count == 0) return;
            var (desde, hasta) = ObtenerRangoFechas();
            lblGraficasTitulo.Text = $"Período: {desde:dd/MM/yyyy} — {hasta:dd/MM/yyyy}";
            DibujarDonut();
            DibujarBarrasGrado();
            DibujarBarrasDias();
        }

        private void DibujarDonut()
        {
            canvasDonut.Children.Clear();
            int presentes = _vmsActuales.Count(v => v.TotalFaltas == 0 && v.DiasATiempo > 0);
            int tardanzas = _vmsActuales.Count(v => v.DiasTarde > 0);
            int ausentes  = _vmsActuales.Count(v => v.DiasPresentes == 0 && v.TotalFaltas > 0);
            int total     = _vmsActuales.Count;
            if (total == 0) return;

            var datos = new[] {
                (presentes, "#4CAF50", "Presentes"),
                (tardanzas, "#FF9800", "Tardanzas"),
                (ausentes,  "#E53935", "Ausentes"),
            };

            double cx = 80, cy = 80, rOut = 70, rIn = 40;
            double startAngle = -Math.PI / 2;
            var leyenda = new List<DonutLeyendaItem>();

            foreach (var (count, colorHex, label) in datos)
            {
                if (count == 0) continue;
                double angle    = 2 * Math.PI * count / total;
                double endAngle = startAngle + angle;
                bool   isLarge  = angle > Math.PI;

                double x1 = cx + rOut * Math.Cos(startAngle), y1 = cy + rOut * Math.Sin(startAngle);
                double x2 = cx + rOut * Math.Cos(endAngle),   y2 = cy + rOut * Math.Sin(endAngle);
                double x3 = cx + rIn  * Math.Cos(endAngle),   y3 = cy + rIn  * Math.Sin(endAngle);
                double x4 = cx + rIn  * Math.Cos(startAngle), y4 = cy + rIn  * Math.Sin(startAngle);

                var fig = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = true };
                fig.Segments.Add(new ArcSegment(new Point(x2, y2), new Size(rOut, rOut), 0, isLarge, SweepDirection.Clockwise, true));
                fig.Segments.Add(new LineSegment(new Point(x3, y3), true));
                fig.Segments.Add(new ArcSegment(new Point(x4, y4), new Size(rIn,  rIn),  0, isLarge, SweepDirection.Counterclockwise, true));

                var geo   = new PathGeometry(); geo.Figures.Add(fig);
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                canvasDonut.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geo, Fill = new SolidColorBrush(color),
                    Stroke = Brushes.White, StrokeThickness = 2
                });

                leyenda.Add(new DonutLeyendaItem
                {
                    ColorBrush = new SolidColorBrush(color),
                    Label      = label,
                    Valor      = count.ToString(),
                    Pct        = $"({100.0 * count / total:F0}%)"
                });
                startAngle = endAngle;
            }
            lstLeyendaDonut.ItemsSource = leyenda;
        }

        private void DibujarBarrasGrado()
        {
            var byGrado = _vmsActuales
                .GroupBy(v => v.NombreGrado ?? "Sin grado")
                .Select(g => new { Grado = g.Key, Faltas = g.Sum(v => v.TotalFaltas) })
                .OrderByDescending(x => x.Faltas)
                .ToList();

            int    maxF = byGrado.Count > 0 && byGrado.Max(x => x.Faltas) > 0 ? byGrado.Max(x => x.Faltas) : 1;
            double maxW = Math.Max(lstBarrasGrado.ActualWidth - 180, 100);

            lstBarrasGrado.ItemsSource = byGrado.Select(x =>
            {
                double ratio = (double)x.Faltas / maxF;
                string hex   = ratio > 0.66 ? "#E53935" : ratio > 0.33 ? "#FF9800" : "#4CAF50";
                return new BarraGradoItem
                {
                    Grado      = x.Grado,
                    AnchoFalta = ratio * maxW,
                    ColorBarra = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    FaltaStr   = x.Faltas.ToString(),
                    Tooltip    = $"{x.Grado}: {x.Faltas} falta(s)"
                };
            }).ToList();

            lblSinDatosGrado.Visibility = byGrado.All(x => x.Faltas == 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DibujarBarrasDias()
        {
            canvasDias.Children.Clear();
            double w = canvasDias.ActualWidth, h = canvasDias.ActualHeight;
            if (w <= 0 || h <= 0 || _vmsActuales == null) return;

            var keys   = new[] { "LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES" };
            var labels = new[] { "Lun",   "Mar",    "Mié",       "Jue",    "Vie"     };
            var counts = keys.Select(k =>
                _vmsActuales.SelectMany(v => v.Faltas).Count(f => DiaSemanaStr(f.Fecha.DayOfWeek) == k)
            ).ToArray();

            int    maxVal  = counts.Max() > 0 ? counts.Max() : 1;
            double barArea = h - 24;
            double slot    = w / keys.Length;
            double barW    = slot * 0.5;
            var    fill    = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007864"));

            for (int i = 0; i < keys.Length; i++)
            {
                double x    = slot * i + (slot - barW) / 2;
                double barH = barArea * counts[i] / maxVal;
                double y    = barArea - barH;

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = barW, Height = Math.Max(barH, 2),
                    Fill = fill, RadiusX = 3, RadiusY = 3,
                    ToolTip = $"{labels[i]}: {counts[i]} falta(s)"
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
                canvasDias.Children.Add(rect);

                if (counts[i] > 0)
                {
                    var lv = new TextBlock { Text = counts[i].ToString(), FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
                    lv.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lv, x + barW / 2 - lv.DesiredSize.Width / 2);
                    Canvas.SetTop(lv, y - 16);
                    canvasDias.Children.Add(lv);
                }

                var ld = new TextBlock { Text = labels[i], FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)) };
                ld.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(ld, x + barW / 2 - ld.DesiredSize.Width / 2);
                Canvas.SetTop(ld, h - 18);
                canvasDias.Children.Add(ld);
            }
        }
    }

    public class FaltaDetalle
    {
        public DateTime Fecha          { get; set; }
        public string FechaStr         => Fecha.ToString("dd/MM/yyyy");
        public string FechaCorta       => Fecha.ToString("dd/MM");
        public string DiaSemana        => Fecha.ToString("dddd", new CultureInfo("es-CO"));
        public string DiaSemanaCorto   => Fecha.ToString("ddd", new CultureInfo("es-CO")).ToUpper();
        public string Estado           { get; set; }
        public string NombreFranja     { get; set; }

        /// <summary>
        /// Etiqueta que se muestra en el chip: nombre real de la franja si existe,
        /// o una descripción clara del tipo de falta.
        /// </summary>
        public string EtiquetaFalta => Estado == "Franja ausente"
            ? (!string.IsNullOrWhiteSpace(NombreFranja) ? NombreFranja : "Franja")
            : "Ausente";

        public bool EsFranja => Estado == "Franja ausente";
    }

    public class EstudianteDashboardVM : INotifyPropertyChanged
    {
        public string NombreEstudiante { get; set; }
        public string Identificacion   { get; set; }
        public string NombreGrado      { get; set; }
        public string NombreGrupo      { get; set; }
        public string NombreSede       { get; set; }
        public int    Total          { get; set; }
        public int    DiasPresentes  { get; set; }
        public int    DiasATiempo    { get; set; }
        public int    DiasTarde      { get; set; }
        public int    TotalFaltas    { get; set; }
        public List<FaltaDetalle> Faltas { get; set; } = new();
        public int    FaltasFranja      => Faltas.Count(f => f.Estado == "Franja ausente");
        public bool   TieneFaltasFranja => FaltasFranja > 0;
        public string FaltasFranjaCount => FaltasFranja.ToString();

        private bool _expandido;
        public bool Expandido
        {
            get => _expandido;
            set { _expandido = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Expandido))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DonutLeyendaItem
    {
        public Brush  ColorBrush { get; set; } = Brushes.Transparent;
        public string Label      { get; set; } = "";
        public string Valor      { get; set; } = "";
        public string Pct        { get; set; } = "";
    }

    public class BarraGradoItem
    {
        public string Grado      { get; set; } = "";
        public double AnchoFalta { get; set; }
        public Brush  ColorBarra { get; set; } = Brushes.Transparent;
        public string FaltaStr   { get; set; } = "";
        public string Tooltip    { get; set; } = "";
    }

    public class GradoGrupoVM
    {
        public string NombreGrado      { get; set; } = "";
        public string NombreGrupo      { get; set; } = "";
        public int    TotalEstudiantes { get; set; }
        public string EstudiantesLabel => TotalEstudiantes == 0 ? "Sin estudiantes" : TotalEstudiantes.ToString();
        public Brush  EstudiantesColor => TotalEstudiantes == 0
            ? new SolidColorBrush(Color.FromRgb(170, 170, 170))
            : new SolidColorBrush(Color.FromRgb(40,  40,  40));
    }
}
