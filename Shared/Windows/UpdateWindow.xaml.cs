using System.Windows;

namespace KubaToolKit.Shared.Windows;

public partial class UpdateWindow
    : Window
{
    public UpdateWindow(
        Version newVersion)
    {
        InitializeComponent();

        VersionText.Text = $"Version {newVersion} is available";
    }

    public void
    SetProgress(
        double percent,
        string status)
    {
        ProgressBar.Value = percent;
        StatusText.Text = status;
    }
}
