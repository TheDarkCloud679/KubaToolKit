using System.Configuration;
using System.Data;
using System.Windows;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using KubaToolKit.Shell;

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

        // No StartupUri in App.xaml -- MainWindow is only created once we
        // know we're NOT about to hand off to a newer version, so users
        // never see the current window flash up right before the update
        // popup takes over.
        protected override async void
        OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            // Default is OnLastWindowClose -- without this, closing the
            // update popup on a failed download (the only window open at
            // that point) would exit the whole app before MainWindow ever
            // gets created. Restored to the normal MainWindow-closes-app
            // behavior right before showing it below.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                var update = await UpdateService.CheckForUpdateAsync();

                if (update != null && await TryApplyUpdateAsync(update))
                {
                    Shutdown();

                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Application: update check failed, continuing with current version.", ex);
            }

            var mainWindow = new MainWindow();

            // WPF defaults MainWindow to whatever Window is shown first --
            // on the "update download failed" path, that would otherwise
            // be the (now closed) update popup rather than this one.
            MainWindow = mainWindow;

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            mainWindow.Show();
        }

        // Returns false (leaving the current version to start normally)
        // on any failure -- a flaky download must never block the app
        // from launching at all.
        private static async Task<bool>
        TryApplyUpdateAsync(
            UpdateInfo update)
        {
            var updateWindow = new UpdateWindow(update.Version);

            updateWindow.Show();

            try
            {
                await UpdateService.DownloadAndApplyAsync(
                    update,
                    new Progress<double>(percent => updateWindow.SetProgress(percent, "Downloading...")));

                updateWindow.SetProgress(100, "Restarting...");

                // Left open on success -- Shutdown() (called by the
                // caller right after this returns) closes it along with
                // everything else. Only the failure path needs to close
                // it itself, since the app then carries on to a normal
                // MainWindow launch instead.
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Application: failed to download/apply update.", ex);

                updateWindow.Close();

                return false;
            }
        }

        private static void
        ReportCrash(
            Exception? ex)
        {
            Logger.Error("Unhandled exception.", ex);

            AppMessageBox.Show(
                $"{ex}\n\nDetails logged in {Logger.LogsFolder}",
                "KubaToolKit - Startup Error");
        }
    }

}
