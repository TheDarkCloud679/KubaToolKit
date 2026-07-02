using System.Collections.ObjectModel;
using System.ComponentModel;

namespace KubaToolKit.Modules.S3Explorer.Models;

public class S3Node
    : INotifyPropertyChanged
{
    private bool
        _isExpanded;

    private bool
        _isLoaded;

    public bool
    IsSearchRoot
    {
        get =>
            _isSearchRoot;

        set
        {
            _isSearchRoot =
                value;

            OnPropertyChanged(
                nameof(
                    IsSearchRoot));
        }
    }

    private bool
    _isSearchRoot;

    public string
        Name
    {
        get;
        set;
    } = "";

    public string
        Prefix
    {
        get;
        set;
    } = "";

    public ObservableCollection<S3Node>
        Children
    {
        get;
        set;
    } =
        new();


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
        IsLoaded
    {
        get =>
            _isLoaded;

        set
        {
            _isLoaded =
                value;

            OnPropertyChanged(
                nameof(
                    IsLoaded));
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