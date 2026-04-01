using System;
using System.Windows;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Views;

namespace CSMBiometricoWPF
{
    public partial class App : Application
    {
        private bool _modoKiosk  = false;
        private bool _modoPanel  = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Detectar modo por argumentos de línea de comandos
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--kiosk", StringComparison.OrdinalIgnoreCase))
                    _modoKiosk = true;
                if (arg.Equals("--panel", StringComparison.OrdinalIgnoreCase))
                    _modoPanel = true;
            }

            // Verificar conexión a la base de datos e inicializar si es necesario
            if (!ConexionDB.VerificarConexion())
            {
                if (_modoKiosk || _modoPanel)
                {
                    MessageBox.Show(
                        "Sin conexión a la base de datos.\nLlame al administrador del sistema.",
                        "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                var resultado = MessageBox.Show(
                    "⚠ No se pudo acceder a la base de datos SQLite.\n\n" +
                    "El sistema funcionará en MODO OFFLINE.\n" +
                    "Los registros se sincronizarán cuando se restablezca el acceso.\n\n" +
                    "¿Desea continuar en modo offline?",
                    "Sin conexión", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.No)
                { Shutdown(); return; }
            }
            else
            {
                DatabaseInitializer.InicializarSiNecesario();
            }

            if (_modoKiosk)
                new KioskWindow().Show();
            else if (_modoPanel)
                new Views.PanelEntradaWindow().Show();
            else
                new LoginWindow().Show();
        }
    }
}
