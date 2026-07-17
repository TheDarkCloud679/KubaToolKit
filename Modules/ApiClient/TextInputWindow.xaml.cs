using System.Windows;
using System.Windows.Input;

namespace KubaToolKit.Modules.ApiClient;

public partial class TextInputWindow
    : Window
{
    public bool Confirmed { get; private set; }

    public string Value =>
        ValueTextBox.Text.Trim();

    public TextInputWindow(
        string title,
        string prompt,
        string initialValue = "")
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ValueTextBox.Text = initialValue;

        Loaded += (_, __) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public static string?
    Prompt(
        Window? owner,
        string title,
        string prompt,
        string initialValue = "")
    {
        var window =
            new TextInputWindow(title, prompt, initialValue)
            {
                Owner = owner
            };

        window.ShowDialog();

        return window.Confirmed ? window.Value : null;
    }

    private void
    ValueTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryConfirm();
        }
    }

    private void
    OkButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        TryConfirm();
    }

    private void
    TryConfirm()
    {
        if (string.IsNullOrWhiteSpace(ValueTextBox.Text))
        {
            MessageBox.Show("Enter a name.");
            return;
        }

        Confirmed = true;
        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
