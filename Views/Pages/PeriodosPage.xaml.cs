using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class PeriodosPage : Page
    {
        private readonly PeriodoAcademicoRepository _repo = new();
        private int? _idInstitucion;

        private static readonly string[] _meses =
        {
            "Enero","Febrero","Marzo","Abril","Mayo","Junio",
            "Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"
        };

        public PeriodosPage()
        {
            InitializeComponent();
            _idInstitucion = SesionActiva.InstitucionActual?.IdInstitucion;
            lblSubtitulo.Text = SesionActiva.InstitucionActual != null
                ? $"Institución: {SesionActiva.InstitucionActual.Nombre}"
                : "Configuración global (todas las instituciones)";
            Loaded += (_, _) => CargarPeriodos();
        }

        private void CargarPeriodos()
        {
            var periodos = _repo.ObtenerPorInstitucion(_idInstitucion);
            RenderizarPeriodos(periodos);
        }

        private void RenderizarPeriodos(List<PeriodoAcademico> periodos)
        {
            pnlPeriodos.Children.Clear();
            for (int i = 0; i < periodos.Count; i++)
                pnlPeriodos.Children.Add(CrearFilaPeriodo(periodos[i], i + 1));
        }

        private Border CrearFilaPeriodo(PeriodoAcademico p, int numero)
        {
            var border = new Border
            {
                Background      = Brushes.White,
                CornerRadius    = new CornerRadius(8),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xE0)),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 0, 12),
                Padding         = new Thickness(20, 16, 20, 16)
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Fila 0: badge número + nombre ──────────────────────────
            var filaTop = new StackPanel { Orientation = Orientation.Horizontal };

            var badge = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64)),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(10, 3, 10, 3),
                Margin          = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text       = $"P{numero}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize   = 11
            };
            filaTop.Children.Add(badge);

            var txtNombre = new TextBox
            {
                Text                    = p.Nombre,
                Width                   = 180,
                Height                  = 30,
                FontSize                = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag                     = "nombre"
            };
            if (Application.Current.TryFindResource("TxtBase") is Style s)
                txtNombre.Style = s;
            filaTop.Children.Add(txtNombre);

            Grid.SetRow(filaTop, 0);
            outer.Children.Add(filaTop);

            // ── Fila 2: desde / hasta ──────────────────────────────────
            var filaFechas = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            filaFechas.Children.Add(Etiqueta("Desde:"));

            var cmbMesI = CrearComboMes(p.MesInicio); cmbMesI.Tag = "mesI";
            filaFechas.Children.Add(cmbMesI);

            var cmbDiaI = CrearComboDia(p.DiaInicio); cmbDiaI.Tag = "diaI";
            cmbDiaI.Margin = new Thickness(6, 0, 0, 0);
            filaFechas.Children.Add(cmbDiaI);

            filaFechas.Children.Add(Etiqueta("Hasta:", new Thickness(18, 0, 8, 0)));

            var cmbMesF = CrearComboMes(p.MesFin); cmbMesF.Tag = "mesF";
            filaFechas.Children.Add(cmbMesF);

            var cmbDiaF = CrearComboDia(p.DiaFin); cmbDiaF.Tag = "diaF";
            cmbDiaF.Margin = new Thickness(6, 0, 0, 0);
            filaFechas.Children.Add(cmbDiaF);

            Grid.SetRow(filaFechas, 2);
            outer.Children.Add(filaFechas);

            border.Child = outer;
            return border;
        }

        private static TextBlock Etiqueta(string texto, Thickness? margin = null) => new()
        {
            Text              = texto,
            FontSize          = 12,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = margin ?? new Thickness(0, 0, 8, 0)
        };

        private static ComboBox CrearComboMes(int seleccionado)
        {
            var cmb = new ComboBox { Width = 120, Height = 30, FontSize = 12 };
            for (int i = 0; i < 12; i++) cmb.Items.Add(_meses[i]);
            cmb.SelectedIndex = Math.Clamp(seleccionado - 1, 0, 11);
            return cmb;
        }

        private static ComboBox CrearComboDia(int seleccionado)
        {
            var cmb = new ComboBox { Width = 60, Height = 30, FontSize = 12 };
            for (int i = 1; i <= 31; i++) cmb.Items.Add(i);
            cmb.SelectedIndex = Math.Clamp(seleccionado - 1, 0, 30);
            return cmb;
        }

        private List<PeriodoAcademico> LeerFormulario()
        {
            var lista = new List<PeriodoAcademico>();
            int orden = 1;
            foreach (Border border in pnlPeriodos.Children.OfType<Border>())
            {
                if (border.Child is not Grid outer) continue;

                TextBox?  txtNombre = null;
                ComboBox? cmbMesI = null, cmbDiaI = null, cmbMesF = null, cmbDiaF = null;

                foreach (UIElement fila in outer.Children)
                {
                    if (fila is not StackPanel sp) continue;
                    foreach (UIElement hijo in sp.Children)
                    {
                        if (hijo is TextBox tb && tb.Tag?.ToString() == "nombre") txtNombre = tb;
                        if (hijo is ComboBox cb)
                            switch (cb.Tag?.ToString())
                            {
                                case "mesI": cmbMesI = cb; break;
                                case "diaI": cmbDiaI = cb; break;
                                case "mesF": cmbMesF = cb; break;
                                case "diaF": cmbDiaF = cb; break;
                            }
                    }
                }

                if (txtNombre == null || cmbMesI == null) continue;

                lista.Add(new PeriodoAcademico
                {
                    Nombre    = txtNombre.Text.Trim(),
                    MesInicio = cmbMesI.SelectedIndex + 1,
                    DiaInicio = (int)(cmbDiaI?.SelectedItem ?? 1),
                    MesFin    = cmbMesF?.SelectedIndex + 1 ?? 12,
                    DiaFin    = (int)(cmbDiaF?.SelectedItem ?? 31),
                    Orden     = orden++
                });
            }
            return lista;
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            var periodos = LeerFormulario();
            if (periodos.Any(p => string.IsNullOrWhiteSpace(p.Nombre)))
            {
                CustomMessageBox.Show("Todos los períodos deben tener un nombre.",
                    "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _repo.GuardarTodos(periodos, _idInstitucion);
            CustomMessageBox.Show("Períodos guardados correctamente.",
                "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnRestablecer_Click(object sender, RoutedEventArgs e)
        {
            if (CustomMessageBox.Show(
                    "¿Desea restablecer los períodos predeterminados?\nSe perderán los cambios actuales.",
                    "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var defaults = new List<PeriodoAcademico>
            {
                new() { Nombre="Período 1", MesInicio=1,  DiaInicio=1, MesFin=3,  DiaFin=31 },
                new() { Nombre="Período 2", MesInicio=4,  DiaInicio=1, MesFin=6,  DiaFin=30 },
                new() { Nombre="Período 3", MesInicio=7,  DiaInicio=1, MesFin=9,  DiaFin=30 },
                new() { Nombre="Período 4", MesInicio=10, DiaInicio=1, MesFin=12, DiaFin=31 },
            };
            _repo.GuardarTodos(defaults, _idInstitucion);
            RenderizarPeriodos(defaults);
        }
    }
}
