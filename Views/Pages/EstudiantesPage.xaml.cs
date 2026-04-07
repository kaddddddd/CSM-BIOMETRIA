using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClosedXML.Excel;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class EstudiantesPage : Page
    {
        private readonly EstudianteRepository _repo = new();
        private List<Estudiante> _todos = new();

        public EstudiantesPage()
        {
            InitializeComponent();
            Loaded += EstudiantesPage_Loaded;
        }

        private void EstudiantesPage_Loaded(object sender, RoutedEventArgs e)
        {
            CargarFiltros();
            CargarEstudiantes();
        }

        private static int? InstFiltro =>
            SesionActiva.EsSuperAdmin ? null : SesionActiva.InstitucionActual?.IdInstitucion;

        private void CargarFiltros()
        {
            cmbEstado.Items.Clear();
            cmbEstado.Items.Add("-- Todos --");
            cmbEstado.Items.Add("ACTIVO");
            cmbEstado.Items.Add("INACTIVO");
            cmbEstado.Items.Add("RETIRADO");
            cmbEstado.SelectedIndex = 0;

            cmbSede.Items.Clear();
            cmbSede.Items.Add(new Sede { IdSede = 0, NombreSede = "-- Todas --" });
            try
            {
                var sedes = InstFiltro.HasValue
                    ? new SedeRepository().ObtenerPorInstitucion(InstFiltro.Value)
                    : new SedeRepository().ObtenerTodas();
                foreach (var s in sedes) cmbSede.Items.Add(s);
            }
            catch { }
            cmbSede.SelectedIndex = 0;
            cmbSede.DisplayMemberPath = "NombreSede";

            cmbGrado.Items.Clear();
            cmbGrado.Items.Add(new Grado { IdGrado = 0, NombreGrado = "-- Todos --" });
            try { foreach (var g in new GradoRepository().ObtenerTodos()) cmbGrado.Items.Add(g); } catch { }
            cmbGrado.SelectedIndex = 0;
            cmbGrado.DisplayMemberPath = "NombreGrado";
        }

        private void CargarEstudiantes()
        {
            try
            {
                int? idSede  = (cmbSede.SelectedItem  as Sede)?.IdSede  > 0 ? (int?)((Sede)cmbSede.SelectedItem).IdSede   : null;
                int? idGrado = (cmbGrado.SelectedItem as Grado)?.IdGrado > 0 ? (int?)((Grado)cmbGrado.SelectedItem).IdGrado : null;
                string? estado = cmbEstado.SelectedIndex > 0 ? cmbEstado.SelectedItem?.ToString() : null;

                _todos = _repo.ObtenerTodos(idSede, idGrado, estado, InstFiltro);
                FiltrarEstudiantes();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando estudiantes: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FiltrarEstudiantes()
        {
            var lista = _todos.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(txtBuscar.Text))
            {
                string q = txtBuscar.Text.Trim();
                lista = lista.Where(e =>
                    e.NombreCompleto.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Identificacion.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            var result = lista.ToList();
            grid.ItemsSource = result;
            lblTotal.Text = $"Mostrando {result.Count} registros";
        }

        private void AbrirFormulario(Estudiante? est)
        {
            var dlg = new EditarEstudianteDialog(est) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) CargarEstudiantes();
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)    => AbrirFormulario(null);
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => CargarEstudiantes();
        private void TxtBuscar_Changed(object sender, TextChangedEventArgs e) => FiltrarEstudiantes();
        private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => CargarEstudiantes();

        private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (grid.SelectedItem is Estudiante est) AbrirFormulario(est);
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is Estudiante est) AbrirFormulario(est);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Estudiante est) return;
            var r = MessageBox.Show(
                $"¿Desea retirar al estudiante \"{est.NombreCompleto}\"?\n\nSe marcará como RETIRADO.",
                "Confirmar retiro", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                _repo.Eliminar(est.IdEstudiante);
                CargarEstudiantes();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al retirar: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel|*.xlsx",
                FileName = $"estudiantes_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Estudiantes");

                var headers = new[] { "Identificación", "Nombre", "Teléfono", "Sede", "Grado", "Grupo", "Estado" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#007864");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                int row = 2;
                foreach (var est in _todos)
                {
                    ws.Cell(row, 1).Value = est.Identificacion;
                    ws.Cell(row, 2).Value = est.NombreCompleto;
                    ws.Cell(row, 3).Value = est.Telefono;
                    ws.Cell(row, 4).Value = est.NombreSede;
                    ws.Cell(row, 5).Value = est.NombreGrado;
                    ws.Cell(row, 6).Value = est.NombreGrupo;
                    ws.Cell(row, 7).Value = est.Estado;
                    if (row % 2 == 0)
                        ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FAF8");
                    row++;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                MessageBox.Show("Exportación exitosa:\n" + dlg.FileName, "Exportar Excel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Error exportando: " + ex.Message); }
        }
    }
}
