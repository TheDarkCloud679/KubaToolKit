using System.Windows;
using System.Windows.Media;

namespace KubaToolKit.Shared.Windows;

public partial class MessageDialogWindow
    : Window
{
    public MessageBoxResult
        Result
    {
        get;
        private set;
    }

    public MessageDialogWindow(
        string text,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        InitializeComponent();

        MessageTextBox.Text =
            text;

        Title =
            string.IsNullOrWhiteSpace(caption)
                ? DefaultTitleFor(icon)
                : caption;

        TitleText.Text =
            Title;

        ConfigureIcon(
            icon);

        ConfigureButtons(
            button);

        // Matches System.Windows.MessageBox: closing via the window's own
        // Close button returns OK for an OK-only box, None otherwise.
        Result =
            button == MessageBoxButton.OK
                ? MessageBoxResult.OK
                : MessageBoxResult.None;
    }

    private static string
    DefaultTitleFor(
        MessageBoxImage icon) =>
        icon switch
        {
            MessageBoxImage.Error => "Error",
            MessageBoxImage.Warning => "Warning",
            MessageBoxImage.Question => "Confirm",
            _ => "Notice"
        };

    private void
    ConfigureIcon(
        MessageBoxImage icon)
    {
        if (icon == MessageBoxImage.None)
        {
            IconBadge.Visibility =
                Visibility.Collapsed;

            return;
        }

        switch (icon)
        {
            case MessageBoxImage.Error:

                IconBadge.Background =
                    (Brush)FindResource("DangerBrush");

                IconPath.Data =
                    Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M8.5,8.5 L15.5,15.5 M15.5,8.5 L8.5,15.5");

                break;

            case MessageBoxImage.Warning:

                IconBadge.Background =
                    (Brush)FindResource("WarningBrush");

                IconPath.Data =
                    Geometry.Parse("M12,3 L22,20 L2,20 Z M12,9 L12,14 M12,17 L12,17.2");

                break;

            case MessageBoxImage.Question:

                IconBadge.Background =
                    (Brush)FindResource("AccentGradientBrush");

                IconPath.Data =
                    Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M9,9.5 A3,3 0 1 1 13,12.3 Q12,13 12,14.5 M12,17.3 L12,17.5");

                break;

            default: // Information

                IconBadge.Background =
                    (Brush)FindResource("AccentGradientBrush");

                IconPath.Data =
                    Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,11 L12,16.5 M12,7.5 L12,7.7");

                break;
        }
    }

    private void
    ConfigureButtons(
        MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:

                OkButton.Visibility =
                    Visibility.Visible;

                OkButton.IsDefault =
                    true;

                OkButton.IsCancel =
                    true;

                break;

            case MessageBoxButton.OKCancel:

                OkButton.Visibility =
                    Visibility.Visible;

                CancelButton.Visibility =
                    Visibility.Visible;

                OkButton.IsDefault =
                    true;

                CancelButton.IsCancel =
                    true;

                break;

            case MessageBoxButton.YesNo:

                YesButton.Visibility =
                    Visibility.Visible;

                NoButton.Visibility =
                    Visibility.Visible;

                YesButton.IsDefault =
                    true;

                NoButton.IsCancel =
                    true;

                break;

            case MessageBoxButton.YesNoCancel:

                YesButton.Visibility =
                    Visibility.Visible;

                NoButton.Visibility =
                    Visibility.Visible;

                CancelButton.Visibility =
                    Visibility.Visible;

                YesButton.IsDefault =
                    true;

                CancelButton.IsCancel =
                    true;

                break;
        }
    }

    private void
    OkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            MessageBoxResult.OK;

        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            MessageBoxResult.Cancel;

        Close();
    }

    private void
    YesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            MessageBoxResult.Yes;

        Close();
    }

    private void
    NoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Result =
            MessageBoxResult.No;

        Close();
    }
}
