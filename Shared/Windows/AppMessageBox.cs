using System.Linq;
using System.Windows;

namespace KubaToolKit.Shared.Windows;

// Drop-in replacement for System.Windows.MessageBox.Show, styled to match
// the rest of the app instead of the native OS dialog chrome.
public static class AppMessageBox
{
    public static MessageBoxResult
    Show(
        string messageBoxText) =>
        Show(messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult
    Show(
        string messageBoxText,
        string caption) =>
        Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult
    Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button) =>
        Show(messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult
    Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var owner =
            Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;

        var dialog =
            new MessageDialogWindow(
                messageBoxText,
                caption,
                button,
                icon);

        if (owner != null
            && owner.IsLoaded
            && owner != dialog)
        {
            dialog.Owner =
                owner;
        }
        else
        {
            dialog.WindowStartupLocation =
                WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();

        return dialog.Result;
    }
}
