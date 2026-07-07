using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.StepFunctions;

public class StepFunctionsModule : IToolModule
{
    public StepFunctionsView TypedView { get; } = new();

    public string Name => "Step Functions";

    public UserControl View => TypedView;
}
