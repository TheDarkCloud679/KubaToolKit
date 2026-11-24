using KubaToolKit.Modules.ApiClient.Models;
using KubaToolKit.Shared.Windows;
using System.Windows;

namespace KubaToolKit.Modules.ApiClient;

public partial class NewEnvironmentWindow
    : Window
{
    // The two reference environments every new environment can start
    // from -- whichever one the user picked as "the" ABT/KubAPI shape
    // when this was set up. Matched by name (case-insensitive) against
    // whatever's actually loaded, so renaming or deleting either one just
    // means that template option quietly finds nothing instead of
    // crashing.
    private const string AbtTemplateName = "Nexum Nam Prod";
    private const string KubApiTemplateName = "NAMProdKubAPI";

    private readonly CollectionStorageService _storage;
    private readonly List<EnvironmentSet> _existingEnvironments;

    public EnvironmentSet? CreatedEnvironment { get; private set; }

    public NewEnvironmentWindow(
        CollectionStorageService storage,
        List<EnvironmentSet> existingEnvironments)
    {
        InitializeComponent();

        _storage = storage;
        _existingEnvironments = existingEnvironments;
    }

    private void
    CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            AppMessageBox.Show("Enter a name for the new environment.", "New Environment");
            return;
        }

        if (_existingEnvironments.Any(env => string.Equals(env.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AppMessageBox.Show($"An environment named \"{name}\" already exists.", "New Environment");
            return;
        }

        var templateName =
            AbtTemplateRadio.IsChecked == true ? AbtTemplateName
            : KubApiTemplateRadio.IsChecked == true ? KubApiTemplateName
            : null;

        EnvironmentSet? template = null;

        if (templateName != null)
        {
            template =
                _existingEnvironments.FirstOrDefault(
                    env => string.Equals(env.Name, templateName, StringComparison.OrdinalIgnoreCase));

            if (template == null)
            {
                AppMessageBox.Show(
                    $"No environment named \"{templateName}\" was found to copy from -- creating \"{name}\" "
                    + "blank instead. Rename an existing environment to that name first if you want this "
                    + "template to work.",
                    "Template not found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        CreatedEnvironment = _storage.CreateEnvironment(name, template);

        DialogResult = true;
        Close();
    }

    private void
    CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
