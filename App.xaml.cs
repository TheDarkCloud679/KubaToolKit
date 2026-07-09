using System.Configuration;
using System.Data;
using System.Windows;
using KubaToolKit.Shared.Services;

namespace KubaToolKit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Logger.Info("Application: démarrage.");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ReportCrash(e.ExceptionObject as Exception);

            DispatcherUnhandledException += (_, e) =>
            {
                ReportCrash(e.Exception);
                e.Handled = true;
            };
        }

        // Sans ça, une exception non gérée au démarrage (ex. dans le
        // constructeur d'un module créé avant l'affichage de la fenêtre)
        // fait quitter le process avec le code 0xE0434352 sans rien
        // afficher : ce handler montre le message complet et le
        // consigne dans Logs\ (voir Logger).
        private static void
        ReportCrash(
            Exception? ex)
        {
            Logger.Error("Exception non gérée.", ex);

            MessageBox.Show(
                $"{ex}\n\nDétails consignés dans {Logger.LogsFolder}",
                "KubaToolKit - Erreur au démarrage");
        }
    }

}
