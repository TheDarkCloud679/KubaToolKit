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
            Logger.Info("Application: starting.");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ReportCrash(e.ExceptionObject as Exception);

            DispatcherUnhandledException += (_, e) =>
            {
                ReportCrash(e.Exception);
                e.Handled = true;
            };
        }

        private static void
        ReportCrash(
            Exception? ex)
        {
            Logger.Error("Unhandled exception.", ex);

            MessageBox.Show(
                $"{ex}\n\nDetails logged in {Logger.LogsFolder}",
                "KubaToolKit - Startup Error");
        }
    }

}
