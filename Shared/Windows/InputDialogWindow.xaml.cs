using System.Windows;

namespace KubaToolKit.Shared.Windows;

public partial class InputDialogWindow
    : Window
{
    public string
        Value
    {
        get;
        private set;
    } = "";

    public InputDialogWindow(
        string title,
        string prompt,
        string defaultValue,
        string okText)
    {
        InitializeComponent();

        Title =
            title;

        PromptText.Text =
            prompt;

        ValueTextBox.Text =
            defaultValue;

        OkButton.Content =
            okText;

        Loaded +=
            (_, _) =>
            {
                ValueTextBox.Focus();
                ValueTextBox.SelectAll();
            };
    }

    private void
    OkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Value =
            ValueTextBox.Text;

        DialogResult =
            true;
    }

    public static string?
    Show(
        Window? owner,
        string title,
        string prompt,
        string defaultValue,
        string okText = "OK")
    {
        var dialog =
            new InputDialogWindow(
                title,
                prompt,
                defaultValue,
                okText)
            {
                Owner = owner
            };

        return dialog.ShowDialog() == true
            ? dialog.Value
            : null;
    }
}
