using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views.Dialogs;

namespace CSMBiometricoWPF.Views.Pages
{
    public partial class InstitucionesPage : Page
    {
        private readonly InstitucionRepository _repoInst  = new();
        private readonly SedeRepository       _repoSede  = new();
        private readonly GradoRepository      _repoGrado = new();
        private readonly GrupoRepository      _repoGrupo = new();

        public InstitucionesPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                btnNuevaInst.Visibility = SesionActiva.EsSuperAdmin ? Visibility.Visible : Visibility.Collapsed;
                Cargar();
            };
        }

        // ── Carga principal ─────────────────────────────────────────────
        private void Cargar()
        {
            pnlContenido.Children.Clear();
            try
            {
                IEnumerable<Institucion> instituciones;
                if (!SesionActiva.EsSuperAdmin && SesionActiva.InstitucionActual != null)
                    instituciones = new[] { SesionActiva.InstitucionActual };
                else
                    instituciones = _repoInst.ObtenerTodas(soloActivas: false);

                foreach (var inst in instituciones)
                {
                    pnlContenido.Children.Add(CrearTarjetaInstitucion(inst));

                    var sedes = _repoSede.ObtenerPorInstitucion(inst.IdInstitucion, soloActivas: false);

                    // Cabecera seccion Sedes
                    var hdrSedes = new Grid { Margin = new Thickness(0, 22, 0, 10) };
                    hdrSedes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    hdrSedes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    hdrSedes.Children.Add(new TextBlock
                    {
                        Text = "Sedes",
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    // Nueva Sede: solo SuperAdmin
                    var btnNuevaSede = new Button
                    {
                        Content = "+ Nueva Sede",
                        Style = (Style)FindResource("BtnPrimario"),
                        Height = 34, Padding = new Thickness(16, 0, 16, 0),
                        Tag = inst,
                        Visibility = SesionActiva.EsSuperAdmin ? Visibility.Visible : Visibility.Collapsed
                    };
                    btnNuevaSede.Click += BtnNuevaSede_Click;
                    Grid.SetColumn(btnNuevaSede, 1);
                    hdrSedes.Children.Add(btnNuevaSede);
                    pnlContenido.Children.Add(hdrSedes);

                    if (sedes.Count == 0)
                    {
                        pnlContenido.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFB)),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(20, 16, 20, 16),
                            Margin = new Thickness(0, 0, 0, 12),
                            BorderThickness = new Thickness(1),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF)),
                            Child = new TextBlock
                            {
                                Text = "No hay sedes registradas para esta institucion.",
                                FontFamily = new FontFamily("Segoe UI"),
                                FontSize = 13,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA5)),
                                FontStyle = FontStyles.Italic
                            }
                        });
                    }
                    else
                    {
                        var sedesGrid = new UniformGrid
                        {
                            Columns = 3,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        foreach (var sede in sedes)
                            sedesGrid.Children.Add(CrearTarjetaSede(sede));
                        pnlContenido.Children.Add(sedesGrid);
                    }

                    if (SesionActiva.EsSuperAdmin)
                        pnlContenido.Children.Add(new Border
                        {
                            Height = 1,
                            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                            Margin = new Thickness(0, 28, 0, 28)
                        });
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Error cargando datos: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Tarjeta de institucion ──────────────────────────────────────
        private Border CrearTarjetaInstitucion(Institucion inst)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 4),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.09, Color = Colors.Black },
                ClipToBounds = true
            };

            // Grid con franja de acento izquierda
            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accent = new Border
            {
                Background = inst.Estado
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64))
                    : new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD))
            };
            Grid.SetColumn(accent, 0);
            outerGrid.Children.Add(accent);

            var content = new Grid { Margin = new Thickness(20, 18, 20, 18) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();

            // Fila titulo
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            titleRow.Children.Add(new TextBlock
            {
                Text = "\uE825",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = inst.Nombre,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x28, 0x31)),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!inst.Estado)
                titleRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xCC)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Inactiva",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x59, 0x00))
                    }
                });
            info.Children.Add(titleRow);

            // Fila detalles
            var detailsRow = new StackPanel { Orientation = Orientation.Horizontal };
            void AgregarDetalle(string icono, string valor)
            {
                if (string.IsNullOrWhiteSpace(valor)) return;
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 0) };
                chip.Children.Add(new TextBlock { Text = icono, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                chip.Children.Add(new TextBlock
                {
                    Text = valor,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                detailsRow.Children.Add(chip);
            }
            AgregarDetalle("\uE707", inst.Direccion);
            AgregarDetalle("\uE717", inst.Telefono);
            info.Children.Add(detailsRow);

            Grid.SetColumn(info, 0);
            content.Children.Add(info);

            // Boton editar (solo SuperAdmin)
            if (SesionActiva.EsSuperAdmin)
            {
                var btnEditar = new Button
                {
                    Style = (Style)FindResource("BtnSecundario"),
                    Height = 34, Padding = new Thickness(16, 0, 16, 0),
                    Tag = inst, VerticalAlignment = VerticalAlignment.Center
                };
                btnEditar.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                                        FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Editar", VerticalAlignment = VerticalAlignment.Center }
                    }
                };
                btnEditar.Click += BtnEditarInst_Click;
                Grid.SetColumn(btnEditar, 1);
                content.Children.Add(btnEditar);
            }

            Grid.SetColumn(content, 1);
            outerGrid.Children.Add(content);
            card.Child = outerGrid;
            return card;
        }

        // ── Tarjeta de sede con grados/grupos ───────────────────────────
        private Border CrearTarjetaSede(Sede sede)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(6, 0, 6, 12),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE8, 0xE5)),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.07, Color = Colors.Black }
            };

            var contenido = new StackPanel();

            // ── Cabecera con fondo suave ─────────────────────────────────
            var cabecera = new Border
            {
                Background = sede.Estado
                    ? new SolidColorBrush(Color.FromRgb(0xF0, 0xFB, 0xF8))
                    : new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Padding = new Thickness(20, 16, 20, 16),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xEE, 0xE8))
            };

            var hdrGrid = new Grid();
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoSede = new StackPanel();

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = "\uE707",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = sede.NombreSede,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x28, 0x31)),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!sede.Estado)
                nameRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(10, 0, 0, 0),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                    Child = new TextBlock { Text = "Inactiva", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)) }
                });
            infoSede.Children.Add(nameRow);

            if (!string.IsNullOrWhiteSpace(sede.Direccion))
            {
                var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                dirRow.Children.Add(new TextBlock
                {
                    Text = "\uE707",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                dirRow.Children.Add(new TextBlock
                {
                    Text = sede.Direccion,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                infoSede.Children.Add(dirRow);
            }

            Grid.SetColumn(infoSede, 0);
            hdrGrid.Children.Add(infoSede);

            // Botones de accion: SOLO SuperAdmin
            if (SesionActiva.EsSuperAdmin)
            {
                var acciones = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var btnEditarSede = new Button
                {
                    Style = (Style)FindResource("BtnSecundario"),
                    Height = 32, Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 8, 0),
                    Tag = sede, ToolTip = "Editar sede"
                };
                btnEditarSede.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                                        FontSize = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Editar", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }
                    }
                };
                btnEditarSede.Click += BtnEditarSede_Click;
                acciones.Children.Add(btnEditarSede);

                var btnToggle = new Button
                {
                    Style = (Style)FindResource(sede.Estado ? "BtnPeligro" : "BtnPrimario"),
                    Height = 32, Padding = new Thickness(12, 0, 12, 0),
                    Tag = sede, ToolTip = sede.Estado ? "Desactivar sede" : "Activar sede"
                };
                btnToggle.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = sede.Estado ? "\uE74D" : "\uE73E",
                                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                                        FontSize = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = sede.Estado ? "Desactivar" : "Activar",
                                        VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }
                    }
                };
                btnToggle.Click += BtnToggleSede_Click;
                acciones.Children.Add(btnToggle);

                Grid.SetColumn(acciones, 1);
                hdrGrid.Children.Add(acciones);
            }

            cabecera.Child = hdrGrid;
            contenido.Children.Add(cabecera);

            // ── Cuerpo: resumen y grados ─────────────────────────────────
            var cuerpo = new Border { Padding = new Thickness(20, 16, 20, 18) };
            var cuerpoStack = new StackPanel();

            var repoEst    = new EstudianteRepository();
            var grados     = _repoGrado.ObtenerTodos();
            var grupos     = _repoGrupo.ObtenerTodos();
            var estudiantes = repoEst.ObtenerTodos(idSede: sede.IdSede, estado: "ACTIVO");

            // Todos los grados del sistema con conteo real de estudiantes en esta sede
            int totalEst = estudiantes.Count;
            var estPorGrado = estudiantes.GroupBy(e => e.IdGrado).ToDictionary(g => g.Key, g => g.Count());
            var filas = grados.Select(g =>
                {
                    var grupo = grupos.FirstOrDefault(gr => gr.IdGrupo == g.IdGrado);
                    int cnt   = estPorGrado.TryGetValue(g.IdGrado, out int c) ? c : 0;
                    return (gradoNombre: g.NombreGrado, grupoNombre: grupo?.NombreGrupo ?? "—", count: cnt);
                }).ToList();

            // Chips de resumen
            var resumen = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            resumen.Children.Add(CrearChip("\uE82D", $"{grados.Count} grado{(grados.Count != 1 ? "s" : "")}",
                Color.FromRgb(0x54, 0x6E, 0x7A), Color.FromRgb(0xEE, 0xF2, 0xF4)));
            resumen.Children.Add(CrearChip("\uE902", $"{grupos.Count} grupo{(grupos.Count != 1 ? "s" : "")}",
                Color.FromRgb(0x15, 0x65, 0xC0), Color.FromRgb(0xE3, 0xF2, 0xFD)));
            resumen.Children.Add(CrearChip("\uE77B", $"{totalEst} estudiante{(totalEst != 1 ? "s" : "")}",
                totalEst > 0 ? Color.FromRgb(0x00, 0x78, 0x64) : Color.FromRgb(0x90, 0x9A, 0xA5),
                totalEst > 0 ? Color.FromRgb(0xE4, 0xF7, 0xF2) : Color.FromRgb(0xF5, 0xF5, 0xF5)));
            cuerpoStack.Children.Add(resumen);

            // Tabla GRADOS Y GRUPOS — siempre visible con los 12 grados
            cuerpoStack.Children.Add(new TextBlock
            {
                Text = "GRADOS Y GRUPOS",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x9A, 0xA5)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Layout 3 columnas: ~4 grados por columna
            int perCol   = (int)Math.Ceiling(filas.Count / 3.0);
            var colGrups = Enumerable.Range(0, 3)
                .Select(i => filas.Skip(i * perCol).Take(perCol).ToList())
                .ToList();

            var outer = new Grid();
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int ci = 0; ci < 3; ci++)
            {
                var group = colGrups[ci];
                var sub = new Grid();
                sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                sub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

                // Encabezados (fila 0)
                sub.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var headers = new[] { ("Grado", false), ("Grupo", false), ("Estudiantes", true) };
                for (int hc = 0; hc < headers.Length; hc++)
                {
                    var hTb = new TextBlock
                    {
                        Text = headers[hc].Item1,
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 10,
                        Foreground = headers[hc].Item2
                            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64))
                            : new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
                        Padding = new Thickness(4, 3, 4, 5)
                    };
                    Grid.SetColumn(hTb, hc); Grid.SetRow(hTb, 0);
                    sub.Children.Add(hTb);
                }

                // Separador (fila 1)
                sub.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var sepSub = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF)) };
                Grid.SetColumnSpan(sepSub, 3); Grid.SetRow(sepSub, 1);
                sub.Children.Add(sepSub);

                // Filas de datos
                int dr = 2;
                foreach (var fila in group)
                {
                    sub.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var rowBg = new Border
                    {
                        Background = new SolidColorBrush(dr % 2 == 0
                            ? Color.FromRgb(0xF8, 0xFD, 0xFB) : Colors.White),
                        CornerRadius = new CornerRadius(3)
                    };
                    Grid.SetColumnSpan(rowBg, 3); Grid.SetRow(rowBg, dr);
                    sub.Children.Add(rowBg);

                    // Grado
                    var tbGrado = new TextBlock
                    {
                        Text = fila.gradoNombre,
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                        Padding = new Thickness(4, 5, 4, 5)
                    };
                    Grid.SetColumn(tbGrado, 0); Grid.SetRow(tbGrado, dr);
                    sub.Children.Add(tbGrado);

                    // Grupo
                    var tbGrupo = new TextBlock
                    {
                        Text = fila.grupoNombre,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x54, 0x6E, 0x7A)),
                        Padding = new Thickness(4, 5, 4, 5)
                    };
                    Grid.SetColumn(tbGrupo, 1); Grid.SetRow(tbGrupo, dr);
                    sub.Children.Add(tbGrupo);

                    // Estudiantes
                    var tbEst = new TextBlock
                    {
                        Text = fila.count > 0 ? fila.count.ToString() : "Sin estudiantes",
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 11,
                        Foreground = fila.count > 0
                            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0x64))
                            : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                        Padding = new Thickness(4, 5, 4, 5)
                    };
                    Grid.SetColumn(tbEst, 2); Grid.SetRow(tbEst, dr);
                    sub.Children.Add(tbEst);

                    dr++;
                }

                Grid.SetColumn(sub, ci * 2);
                outer.Children.Add(sub);
            }

            cuerpoStack.Children.Add(outer);

            cuerpo.Child = cuerpoStack;
            contenido.Children.Add(cuerpo);
            card.Child = contenido;
            return card;
        }

        // ── Helper: chip de resumen ──────────────────────────────────────
        private static Border CrearChip(string icono, string texto, Color fgColor, Color bgColor)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = icono,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = new SolidColorBrush(fgColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = texto,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12,
                Foreground = new SolidColorBrush(fgColor),
                VerticalAlignment = VerticalAlignment.Center
            });
            return new Border
            {
                Background = new SolidColorBrush(bgColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Child = sp
            };
        }

        // ── Handlers ────────────────────────────────────────────────────
        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => Cargar();

        private void BtnNuevaInst_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EditarInstitucionDialog(null) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD, "Nueva institucion creada");
                Cargar();
            }
        }

        private void BtnEditarInst_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Institucion inst) return;
            var dlg = new EditarInstitucionDialog(inst) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD, $"Institucion editada: {inst.Nombre}");
                Cargar();
            }
        }

        private void BtnNuevaSede_Click(object sender, RoutedEventArgs e)
        {
            var inst = (sender as Button)?.Tag as Institucion;
            var dlg = new EditarSedeDialog(null) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD, "Nueva sede creada");
                Cargar();
            }
        }

        private void BtnEditarSede_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Sede sede) return;
            var dlg = new EditarSedeDialog(sede) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                new LogRepository().Registrar(TipoEvento.CRUD, $"Sede editada: {sede.NombreSede}");
                Cargar();
            }
        }

        private void BtnToggleSede_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Sede sede) return;
            string accion = sede.Estado ? "desactivar" : "activar";
            if (CustomMessageBox.Show($"¿Desea {accion} la sede \"{sede.NombreSede}\"?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                if (sede.Estado) _repoSede.Eliminar(sede.IdSede);
                else             _repoSede.Activar(sede.IdSede);
                new LogRepository().Registrar(TipoEvento.CRUD, $"Sede {accion}da: {sede.NombreSede}");
                Cargar();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error al {accion}: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
