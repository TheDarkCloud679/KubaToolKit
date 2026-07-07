using KubaToolKit.Infrastructure;
using System.Windows.Controls;

namespace KubaToolKit.Modules.Sqs;

public class SqsModule : IToolModule
{
    public SqsView TypedView { get; } = new();

    public string Name => "SQS";

    public UserControl View => TypedView;
}
