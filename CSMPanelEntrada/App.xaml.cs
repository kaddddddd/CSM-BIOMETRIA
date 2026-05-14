using System;
using System.Windows;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Services;
using CSMBiometricoWPF.Views;

namespace CSMPanelEntrada
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Inicializar SQLite local (siempre, sin importar si hay red)
            try { SQLiteInitializer.InicializarSiNecesario(); }
            catch { /* Si SQLite falla tampoco podemos operar */ }

            bool hayMySQL = ConexionDB.VerificarConexion();

            if (hayMySQL)
            {
                // Conexión disponible: inicializar BD y sincronizar caché local
                try { DatabaseInitializer.InicializarSiNecesario(); } catch { }
                try { SyncService.SincronizarDeMySQL(); }              catch { }
                try { SyncService.SincronizarHaciaMySQL(); }           catch { }
            }
            else
            {
                // Sin conexión: continuar en modo offline usando caché SQLite
                MessageBox.Show(
                    "No se detectó conexión al servidor MySQL.\n\n" +
                    "El panel funcionará en MODO OFFLINE.\n" +
                    "Los registros se sincronizarán automáticamente\n" +
                    "cuando se restablezca la conexión.\n\n" +
                    "Verifique que ambas PCs estén en la misma red.",
                    "Modo Offline — CSM Panel Entrada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            new PanelEntradaWindow().Show();
        }
    }
}
