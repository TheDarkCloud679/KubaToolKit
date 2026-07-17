using System.Collections.ObjectModel;
using System.ComponentModel;
namespace KubaToolKit.Modules.CloudWatchLogs.Models;

public class LogGroupNode
    : INotifyPropertyChanged
{
    private bool
        _isChecked;
    private bool
        _isExpanded;
    public string
        Name
    {
        get;
        set;
    } = "";
    public string
        FullPath
    {
        get;
        set;
    } = "";

    public ObservableCollection<LogGroupNode>
        Children
    {
        get;
        set;
    } =
        new();

    public bool
        IsLeaf
    {
        get;
        set;
    }
    public bool
        IsExpanded
    {
        get =>
            _isExpanded;
        set
        {
            _isExpanded =
                value;
            OnPropertyChanged(
                nameof(
                    IsExpanded));
        }
    }

    public bool
        IsChecked
    {
        get =>
            _isChecked;

        set
        {
            _isChecked =
                value;

            OnPropertyChanged(
                nameof(
                    IsChecked));

            foreach (var child
                     in Children)
            {
                child.IsChecked =
                    value;
            }
        }
    }

    public event
        PropertyChangedEventHandler?
        PropertyChanged;

    private void
        OnPropertyChanged(
            string propertyName)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}