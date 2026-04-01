using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class ReportesPage : Page
    {
        private readonly RegistroIngresoRepository _repoReg  = new();
        private readonly EstudianteRepository      _repoEst  = new();
        private readonly SedeRepository            _repoSede = new();
        private readonly GradoRepository           _repoGrado= new();

        // Columnas dinámicas según tipo de reporte
        private string _tipoActivo = "";

        // Resultado completo del último reporte generado (para filtrar sin re-consultar)
        private System.Collections.IList? _resultadoCompleto;

        public ReportesPage()
        {
            InitializeComponent();
            Loaded += ReportesPage_Loaded;
        }

        private void ReportesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Tipos de reporte
            cmbTipoReporte.Items.Add("Asistencia diaria");
            cmbTipoReporte.Items.Add("Resumen por período");
            cmbTipoReporte.Items.Add("Tardanzas");
            cmbTipoReporte.Items.Add("Ausencias");
            cmbTipoReporte.SelectedIndex = 0;

            // Fechas por defecto: semana actual
            dpDesde.SelectedDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1);
            dpHasta.SelectedDate = DateTime.Today;

            // Sedes: filtrar por institución si el usuario no es SUPERADMIN
            int? idInstFiltro = SesionActiva.EsSuperAdmin ? null : SesionActiva.InstitucionActual?.IdInstitucion;
            cmbSede.Items.Add(new Sede { IdSede = 0, NombreSede = "-- Todas --" });
            try
            {
                var sedes = idInstFiltro.HasValue
                    ? _repoSede.ObtenerPorInstitucion(idInstFiltro.Value)
                    : _repoSede.ObtenerTodas();
                foreach (var s in sedes) cmbSede.Items.Add(s);
            }
            catch { }
            cmbSede.SelectedIndex = 0;
            cmbSede.DisplayMemberPath = "NombreSede";

            // Grados
            cmbGrado.Items.Add(new Grado { IdGrado = 0, NombreGrado = "-- Todos --" });
            try { foreach (var g in _repoGrado.ObtenerTodos()) cmbGrado.Items.Add(g); } catch { }
            cmbGrado.SelectedIndex = 0;
            cmbGrado.DisplayMemberPath = "NombreGrado";

            // Estado de ingreso
            cmbEstado.Items.Add("-- Todos --");
            cmbEstado.Items.Add("A tiempo");
            cmbEstado.Items.Add("Tarde");
            cmbEstado.Items.Add("Inasistencia");
            cmbEstado.SelectedIndex = 0;
        }

        private void TipoReporte_Changed(object sender, SelectionChangedEventArgs e)
        {
            _tipoActivo = cmbTipoReporte.SelectedItem?.ToString() ?? "";
        }

        private void BtnGenerar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var desde = dpDesde.SelectedDate ?? DateTime.Today;
                var hasta = dpHasta.SelectedDate ?? DateTime.Today;
                if (desde > hasta) { MessageBox.Show("La fecha inicial no puede ser mayor a la final.", "Fechas inválidas", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                int? idSede  = (cmbSede.SelectedItem  as Sede)?.IdSede  > 0 ? (int?)((Sede)cmbSede.SelectedItem).IdSede   : null;
                int? idGrado = (cmbGrado.SelectedItem as Grado)?.IdGrado > 0 ? (int?)((Grado)cmbGrado.SelectedItem).IdGrado : null;
                string? estado = cmbEstado.SelectedIndex > 0 ? cmbEstado.SelectedItem?.ToString() : null;

                switch (cmbTipoReporte.SelectedItem?.ToString())
                {
                    case "Asistencia diaria":
                    case "Tardanzas":
                    case "Ausencias":
                        GenerarReporteRegistros(desde, hasta, idSede, idGrado, estado);
                        break;
                    case "Resumen por período":
                        GenerarResumenPeriodo(desde, hasta, idSede, idGrado);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generando reporte: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int? InstFiltro =>
            SesionActiva.EsSuperAdmin ? null : SesionActiva.InstitucionActual?.IdInstitucion;

        private void GenerarReporteRegistros(DateTime desde, DateTime hasta, int? idSede, int? idGrado, string? estado)
        {
            var todos = new List<RegistroIngreso>();
            for (var d = desde; d <= hasta; d = d.AddDays(1))
            {
                var r = _repoReg.ObtenerPorFecha(d, idSede, idInstitucion: InstFiltro);
                todos.AddRange(r);
            }

            if (idGrado.HasValue)
            {
                string? nombreGrado = (cmbGrado.SelectedItem as Grado)?.NombreGrado;
                if (nombreGrado != null) todos = todos.FindAll(r => r.NombreGrado == nombreGrado);
            }

            if (cmbTipoReporte.SelectedItem?.ToString() == "Tardanzas")
                todos = todos.FindAll(r => r.EstadoIngreso == EstadoIngreso.TARDE);
            else if (cmbTipoReporte.SelectedItem?.ToString() == "Ausencias")
            {
                // Ausencias: estudiantes activos sin registro en el período
                var estudiantes = _repoEst.ObtenerTodos(idSede, idGrado, "ACTIVO");
                var idsPresentes = new HashSet<int>(todos.Select(r => r.IdEstudiante));
                var ausentes = estudiantes.Where(e => !idsPresentes.Contains(e.IdEstudiante)).ToList();
                MostrarColumnasModoEstudiante();
                _resultadoCompleto = ausentes;
                AplicarFiltroEstudiante();
                lblTotal.Text = $"{ausentes.Count} estudiantes sin registro en el período";
                return;
            }
            else if (!string.IsNullOrEmpty(estado))
            {
                var estadoEnum = estado switch
                {
                    "A tiempo"     => EstadoIngreso.A_TIEMPO,
                    "Tarde"        => EstadoIngreso.TARDE,
                    "Inasistencia" => EstadoIngreso.FUERA_DE_HORARIO,
                    _              => (EstadoIngreso?)null
                };
                if (estadoEnum.HasValue)
                    todos = todos.FindAll(r => r.EstadoIngreso == estadoEnum.Value);
            }

            MostrarColumnasRegistro();
            _resultadoCompleto = todos;
            AplicarFiltroEstudiante();
            lblTotal.Text = $"{todos.Count} registros encontrados";
        }

        private void GenerarResumenPeriodo(DateTime desde, DateTime hasta, int? idSede, int? idGrado)
        {
            var estudiantes = _repoEst.ObtenerTodos(idSede, idGrado, "ACTIVO", InstFiltro);
            var todos = new List<RegistroIngreso>();
            for (var d = desde; d <= hasta; d = d.AddDays(1))
                todos.AddRange(_repoReg.ObtenerPorFecha(d, idSede, idInstitucion: InstFiltro));

            if (idGrado.HasValue)
            {
                string? nombreGrado = (cmbGrado.SelectedItem as Grado)?.NombreGrado;
                if (nombreGrado != null) todos = todos.FindAll(r => r.NombreGrado == nombreGrado);
            }

            var resumen = estudiantes.Select(e => new ResumenEstudiante
            {
                Identificacion = e.Identificacion,
                NombreCompleto = e.NombreCompleto,
                NombreSede     = e.NombreSede,
                NombreGrado    = e.NombreGrado,
                NombreGrupo    = e.NombreGrupo,
                TotalPresente  = todos.Count(r => r.IdEstudiante == e.IdEstudiante && r.EstadoIngreso != EstadoIngreso.FUERA_DE_HORARIO),
                TotalTardanza  = todos.Count(r => r.IdEstudiante == e.IdEstudiante && r.EstadoIngreso == EstadoIngreso.TARDE),
                TotalAusente   = (int)(hasta - desde).TotalDays + 1
                                 - todos.Count(r => r.IdEstudiante == e.IdEstudiante && r.EstadoIngreso != EstadoIngreso.FUERA_DE_HORARIO)
            }).ToList();

            MostrarColumnasResumen();
            _resultadoCompleto = resumen;
            AplicarFiltroEstudiante();
            lblTotal.Text = $"{resumen.Count} estudiantes — período {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}";
        }

        private void TxtEstudiante_TextChanged(object sender, TextChangedEventArgs e)
            => AplicarFiltroEstudiante();

        private void AplicarFiltroEstudiante()
        {
            if (_resultadoCompleto == null) return;
            string buscar = txtEstudiante.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(buscar))
            {
                grid.ItemsSource = _resultadoCompleto;
                return;
            }

            // Filtra por nombre o identificación según el tipo de objeto en la lista
            if (_resultadoCompleto is List<RegistroIngreso> registros)
            {
                grid.ItemsSource = registros.Where(r =>
                    (r.NombreEstudiante?.ToLower().Contains(buscar) ?? false) ||
                    (r.Identificacion?.ToLower().Contains(buscar) ?? false)).ToList();
            }
            else if (_resultadoCompleto is List<Estudiante> estudiantes)
            {
                grid.ItemsSource = estudiantes.Where(e =>
                    (e.NombreCompleto?.ToLower().Contains(buscar) ?? false) ||
                    (e.Identificacion?.ToLower().Contains(buscar) ?? false)).ToList();
            }
            else if (_resultadoCompleto is List<ResumenEstudiante> resumen)
            {
                grid.ItemsSource = resumen.Where(r =>
                    (r.NombreCompleto?.ToLower().Contains(buscar) ?? false) ||
                    (r.Identificacion?.ToLower().Contains(buscar) ?? false)).ToList();
            }
        }

        private void MostrarColumnasRegistro()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn { Header = "Fecha",          Binding = new System.Windows.Data.Binding("FechaIngreso") { StringFormat = "dd/MM/yyyy" }, Width = new DataGridLength(100) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Estudiante",     Binding = new System.Windows.Data.Binding("NombreEstudiante"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Identificación", Binding = new System.Windows.Data.Binding("Identificacion"), Width = new DataGridLength(120) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Sede",           Binding = new System.Windows.Data.Binding("NombreSede"), Width = new DataGridLength(130) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grado",          Binding = new System.Windows.Data.Binding("NombreGrado"), Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grupo",          Binding = new System.Windows.Data.Binding("NombreGrupo"), Width = new DataGridLength(70) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Hora",           Binding = new System.Windows.Data.Binding("HoraIngresoStr"), Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Estado",         Binding = new System.Windows.Data.Binding("EstadoStr"), Width = new DataGridLength(120) });
        }

        private void MostrarColumnasModoEstudiante()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn { Header = "Identificación", Binding = new System.Windows.Data.Binding("Identificacion"), Width = new DataGridLength(120) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Nombre completo", Binding = new System.Windows.Data.Binding("NombreCompleto"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Sede",  Binding = new System.Windows.Data.Binding("NombreSede"),  Width = new DataGridLength(130) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grado", Binding = new System.Windows.Data.Binding("NombreGrado"), Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grupo", Binding = new System.Windows.Data.Binding("NombreGrupo"), Width = new DataGridLength(70) });
        }

        private void MostrarColumnasResumen()
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn { Header = "Identificación", Binding = new System.Windows.Data.Binding("Identificacion"), Width = new DataGridLength(120) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Nombre completo", Binding = new System.Windows.Data.Binding("NombreCompleto"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Sede",      Binding = new System.Windows.Data.Binding("NombreSede"),   Width = new DataGridLength(130) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grado",     Binding = new System.Windows.Data.Binding("NombreGrado"),  Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grupo",     Binding = new System.Windows.Data.Binding("NombreGrupo"),  Width = new DataGridLength(70) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Presentes", Binding = new System.Windows.Data.Binding("TotalPresente"), Width = new DataGridLength(90) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Tardanzas", Binding = new System.Windows.Data.Binding("TotalTardanza"), Width = new DataGridLength(90) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Ausentes",  Binding = new System.Windows.Data.Binding("TotalAusente"),  Width = new DataGridLength(90) });
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (grid.ItemsSource == null)
            {
                MessageBox.Show("Primero genere un reporte.", "Sin datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tipoReporte = cmbTipoReporte.SelectedItem?.ToString() ?? "reporte";
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter   = "Excel|*.xlsx",
                FileName = $"{tipoReporte.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var cols = grid.Columns.OfType<DataGridTextColumn>().ToList();

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Reporte");

                // ── Cabecera informativa ──────────────────────────────
                int fila = 1;
                void InfoFila(string clave, string valor)
                {
                    ws.Cell(fila, 1).Value = clave;
                    ws.Cell(fila, 2).Value = valor;
                    ws.Cell(fila, 1).Style.Font.Bold = true;
                    ws.Cell(fila, 1).Style.Font.FontColor = XLColor.FromHtml("#007864");
                    fila++;
                }

                InfoFila("Reporte",   tipoReporte);
                InfoFila("Período",   $"{dpDesde.SelectedDate:dd/MM/yyyy} - {dpHasta.SelectedDate:dd/MM/yyyy}");
                if ((cmbSede.SelectedItem  as Sede)?.IdSede   > 0) InfoFila("Sede",  ((Sede)cmbSede.SelectedItem).NombreSede);
                if ((cmbGrado.SelectedItem as Grado)?.IdGrado > 0) InfoFila("Grado", ((Grado)cmbGrado.SelectedItem).NombreGrado);
                InfoFila("Generado",  DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                fila++; // fila en blanco

                // ── Encabezados de tabla ──────────────────────────────
                int filaEncabezado = fila;
                for (int c = 0; c < cols.Count; c++)
                {
                    var cell = ws.Cell(fila, c + 1);
                    cell.Value = cols[c].Header?.ToString() ?? "";
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#007864");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                fila++;

                // ── Filas de datos ────────────────────────────────────
                int filaInicioDatos = fila;
                foreach (var item in grid.ItemsSource)
                {
                    for (int c = 0; c < cols.Count; c++)
                    {
                        var binding = cols[c].Binding as System.Windows.Data.Binding;
                        if (binding == null) continue;
                        var prop = item.GetType().GetProperty(binding.Path.Path);
                        var val  = prop?.GetValue(item);
                        var cell = ws.Cell(fila, c + 1);

                        if (val is DateTime dt)
                        {
                            cell.Value = dt.ToString(!string.IsNullOrEmpty(binding.StringFormat)
                                ? binding.StringFormat : "dd/MM/yyyy");
                        }
                        else
                        {
                            cell.Value = val?.ToString() ?? "";
                        }
                    }

                    // Filas alternadas
                    if ((fila - filaInicioDatos) % 2 == 1)
                        ws.Row(fila).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FAF8");

                    fila++;
                }

                // ── Borde y autoajuste ────────────────────────────────
                var tabla = ws.Range(filaEncabezado, 1, fila - 1, cols.Count);
                tabla.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                tabla.Style.Border.InsideBorder  = XLBorderStyleValues.Thin;
                tabla.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CCCCCC");
                tabla.Style.Border.InsideBorderColor  = XLColor.FromHtml("#CCCCCC");
                ws.Columns().AdjustToContents();

                wb.SaveAs(dlg.FileName);
                MessageBox.Show($"Exportación exitosa:\n{dlg.FileName}", "Exportar",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Error exportando: " + ex.Message); }
        }
    }

    // DTO para reporte de resumen por período
    internal class ResumenEstudiante
    {
        public string Identificacion { get; set; } = "";
        public string NombreCompleto { get; set; } = "";
        public string NombreSede     { get; set; } = "";
        public string NombreGrado    { get; set; } = "";
        public string NombreGrupo    { get; set; } = "";
        public int TotalPresente { get; set; }
        public int TotalTardanza { get; set; }
        public int TotalAusente  { get; set; }
    }
}
