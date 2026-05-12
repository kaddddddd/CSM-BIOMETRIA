using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
            bool puedeEliminar = periodos.Count > 3;
            for (int i = 0; i < periodos.Count; i++)
                pnlPeriodos.Children.Add(CrearFilaPeriodo(periodos[i], i + 1, puedeEliminar));
            btnAgregarPeriodo.Visibility = periodos.Count >= 4
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private Border CrearFilaPeriodo(PeriodoAcademico p, int numero, bool puedeEliminar)
        {
            var acentos = new[]
            {
                Color.FromRgb(0x00, 0x78, 0x64), // verde
                Color.FromRgb(0x15, 0x65, 0xC0), // azul
                Color.FromRgb(0x6A, 0x1B, 0x9A), // morado
                Color.FromRgb(0xE6, 0x5C, 0x00), // naranja
            };
            var acento = acentos[(numero - 1) % acentos.Length];

            var outer = new Border
            {
                Background      = Brushes.White,
                CornerRadius    = new CornerRadius(10),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x28, acento.R, acento.G, acento.B)),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(8),
                Effect          = new DropShadowEffect
                {
                    BlurRadius  = 10,
                    ShadowDepth = 2,
                    Opacity     = 0.07,
                    Color       = Colors.Black,
                    Direction   = 270
                }
            };

            // Grid principal: franja izquierda | contenido
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var franja = new Border
            {
                Background   = new SolidColorBrush(acento),
                CornerRadius = new CornerRadius(10, 0, 0, 10)
            };
            Grid.SetColumn(franja, 0);
            mainGrid.Children.Add(franja);

            // Contenido interior
            var content = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            Grid.SetColumn(content, 1);

            // ── Fila 1: [badge] [título fijo] [×] ─────────────────────
            var filaNombre = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            filaNombre.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filaNombre.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filaNombre.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background        = new SolidColorBrush(acento),
                CornerRadius      = new CornerRadius(22),
                Width             = 44,
                Height            = 44,
                Margin            = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text                = $"P{numero}",
                Foreground          = Brushes.White,
                FontWeight          = FontWeights.Bold,
                FontSize            = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 0);
            filaNombre.Children.Add(badge);

            var lblNombre = new TextBlock
            {
                Text              = $"Período {numero}",
                FontSize          = 15,
                FontWeight        = FontWeights.SemiBold,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblNombre, 1);
            filaNombre.Children.Add(lblNombre);

            var btnEliminar = new Button
            {
                Content           = "✕",
                FontSize          = 13,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5)),
                Background        = Brushes.Transparent,
                BorderThickness   = new Thickness(0),
                Cursor            = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
                Visibility        = puedeEliminar ? Visibility.Visible : Visibility.Collapsed,
                ToolTip           = "Eliminar período"
            };
            btnEliminar.Click += (_, _) =>
            {
                var confirmacion = CustomMessageBox.Show(
                    $"¿Está seguro de que desea eliminar \"Período {numero}\"?\n\nEsta acción eliminará toda la configuración de este período.",
                    "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmacion != MessageBoxResult.Yes) return;

                var lista = LeerFormulario();
                if (numero - 1 < lista.Count) lista.RemoveAt(numero - 1);
                RenderizarPeriodos(lista);
            };
            Grid.SetColumn(btnEliminar, 2);
            filaNombre.Children.Add(btnEliminar);

            content.Children.Add(filaNombre);

            // ── Fila 2: Desde → Hasta ──────────────────────────────────
            var filaDesde = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 10)
            };
            filaDesde.Children.Add(BadgeFecha("Desde", acento));
            var cmbMesI = CrearComboMes(p.MesInicio); cmbMesI.Tag = "mesI";
            filaDesde.Children.Add(cmbMesI);
            var cmbDiaI = CrearComboDia(p.DiaInicio); cmbDiaI.Tag = "diaI";
            cmbDiaI.Margin = new Thickness(6, 0, 0, 0);
            filaDesde.Children.Add(cmbDiaI);
            content.Children.Add(filaDesde);

            var filaHasta = new StackPanel { Orientation = Orientation.Horizontal };
            filaHasta.Children.Add(BadgeFecha("Hasta", Color.FromRgb(0xE6, 0x5C, 0x00)));
            var cmbMesF = CrearComboMes(p.MesFin); cmbMesF.Tag = "mesF";
            filaHasta.Children.Add(cmbMesF);
            var cmbDiaF = CrearComboDia(p.DiaFin); cmbDiaF.Tag = "diaF";
            cmbDiaF.Margin = new Thickness(6, 0, 0, 0);
            filaHasta.Children.Add(cmbDiaF);
            content.Children.Add(filaHasta);

            mainGrid.Children.Add(content);
            outer.Child = mainGrid;
            return outer;
        }

        private static Border BadgeFecha(string texto, Color color) => new()
        {
            Background        = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
            CornerRadius      = new CornerRadius(5),
            Padding           = new Thickness(10, 5, 10, 5),
            Margin            = new Thickness(0, 0, 10, 0),
            Width             = 58,
            VerticalAlignment = VerticalAlignment.Center,
            Child             = new TextBlock
            {
                Text                = texto,
                FontSize            = 11,
                FontWeight          = FontWeights.SemiBold,
                Foreground          = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };

        private static ComboBox CrearComboMes(int seleccionado)
        {
            var cmb = new ComboBox { Width = 118, Height = 34, FontSize = 12 };
            for (int i = 0; i < 12; i++) cmb.Items.Add(_meses[i]);
            cmb.SelectedIndex = Math.Clamp(seleccionado - 1, 0, 11);
            return cmb;
        }

        private static ComboBox CrearComboDia(int seleccionado)
        {
            var cmb = new ComboBox { Width = 62, Height = 34, FontSize = 12 };
            for (int i = 1; i <= 31; i++) cmb.Items.Add(i);
            cmb.SelectedIndex = Math.Clamp(seleccionado - 1, 0, 30);
            return cmb;
        }

        private List<PeriodoAcademico> LeerFormulario()
        {
            var lista = new List<PeriodoAcademico>();
            int orden = 1;
            foreach (Border outer in pnlPeriodos.Children.OfType<Border>())
            {
                if (outer.Child is not Grid mainGrid) continue;

                // Buscar el StackPanel de contenido (segunda columna del mainGrid)
                StackPanel? content = null;
                foreach (UIElement el in mainGrid.Children)
                    if (el is StackPanel sp && Grid.GetColumn(sp) == 1) { content = sp; break; }
                if (content == null) continue;

                ComboBox? cmbMesI = null, cmbDiaI = null, cmbMesF = null, cmbDiaF = null;

                foreach (UIElement fila in content.Children)
                {
                    if (fila is not StackPanel sp) continue;
                    foreach (UIElement hijo in sp.Children)
                    {
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

                if (cmbMesI == null) continue;

                lista.Add(new PeriodoAcademico
                {
                    Nombre    = $"Período {orden}",
                    MesInicio = cmbMesI.SelectedIndex + 1,
                    DiaInicio = (int)(cmbDiaI?.SelectedItem ?? 1),
                    MesFin    = cmbMesF?.SelectedIndex + 1 ?? 12,
                    DiaFin    = (int)(cmbDiaF?.SelectedItem ?? 31),
                    Orden     = orden++
                });
            }
            return lista;
        }

        private void BtnAgregarPeriodo_Click(object sender, RoutedEventArgs e)
        {
            var lista = LeerFormulario();
            lista.Add(new PeriodoAcademico
            {
                Nombre    = $"Período {lista.Count + 1}",
                MesInicio = 1, DiaInicio = 1,
                MesFin    = 12, DiaFin   = 31
            });
            RenderizarPeriodos(lista);
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            var periodos = LeerFormulario();
            if (periodos.Count < 3)
            {
                CustomMessageBox.Show("Deben existir al menos 3 períodos académicos.",
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
