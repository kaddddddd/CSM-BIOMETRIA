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
            ("👨‍🎓", "Total estudiantes",   "#009688", "TODOS"),
            ("✅",  "Presentes",           "#4CAF50", "PRESENTES"),
            ("⚠️",  "Parcial",             "#FF9800", "PARCIALES"),
            ("❌",  "Ausentes",            "#E53935", "AUSENTES"),
            ("⏰",  "Tardanzas",           "#FB8C00", "TARDANZAS"),
            ("🖐",  "Huellas registradas", "#2196F3", "HUELLAS"),
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
            _cardMap.Clear();

            foreach (var (icono, titulo, colorHex, filtro) in _cardDefs)
            {
                var acento = (Color)ColorConverter.ConvertFromString(colorHex);
                var capFiltro = filtro;

                var card = new Border
                {
                    Width = 180, Height = 110,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
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
            cmbGrupo.SelectedIndex = 0;
            _inicializando = false;
            _filtroActivo = "AUSENTES";
            RefrescarTodo();
        }

        private void cmbInstitucion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

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

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
