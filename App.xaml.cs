using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace KubaToolKit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
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
        // consigne dans %AppData%\KubaToolKit\crash.log.
        private static void
        ReportCrash(
            Exception? ex)
        {
            var message = ex?.ToString() ?? "Exception inconnue (objet non-Exception).";

            try
            {
                var logPath =
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "KubaToolKit",
                        "crash.log");

                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, message);
            }
            catch
            {
                // La consignation sur disque est un bonus : son échec ne
                // doit pas empêcher d'afficher le message à l'utilisateur.
            }

            MessageBox.Show(
                message,
                "KubaToolKit - Erreur au démarrage");
        }
    }

}
