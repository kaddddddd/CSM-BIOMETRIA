using System;
using System.Windows;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Views;

namespace CSMPanelEntrada
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Verificar conexión con MySQL antes de abrir la ventana
            try
            {
                DatabaseInitializer.InicializarSiNecesario();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No se pudo conectar al servidor MySQL.\n\n" +
                    $"Verifique que:\n" +
                    $"  • El servidor MySQL esté encendido en la PC de la oficina\n" +
                    $"  • La IP configurada en App.config sea correcta\n" +
                    $"  • Ambas PCs estén en la misma red\n\n" +
                    $"Detalle técnico:\n{ex.Message}",
                    "Error de conexión — CSM Panel Entrada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var panel = new PanelEntradaWindow();
            panel.Show();
        }
    }
}
