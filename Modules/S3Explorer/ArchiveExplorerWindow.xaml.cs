using KubaToolKit.Modules.S3Explorer.Models;
using KubaToolKit.Shared.Windows;
using System.Windows;
using System.Windows.Input;

namespace KubaToolKit.Modules.S3Explorer;

public partial class
    ArchiveExplorerWindow
    : Window
{
    private readonly string
        _profile;

    private readonly string
        _bucket;

    public
        ArchiveExplorerWindow(
            List<ArchiveEntryItem> items,
            string profile,
            string bucket)
    {
        InitializeComponent();

        _profile =
            profile;

        _bucket =
            bucket;

        ArchiveTreeView
            .ItemsSource =
                items;
    }

    private readonly Dictionary<
    string,
    string>
_cachedContent =
    new();

    private async void
    ArchiveTreeView_DoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ArchiveTreeView
                .SelectedItem
            is not ArchiveEntryItem item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            return;
        }

        if (_cachedContent
    .TryGetValue(
        item.EntryPath,
        out var cached))
        {
            var cachedViewer =
                new FileViewerWindow(
                    item.Name,
                    cached);

            cachedViewer.Owner =
                this;
            cachedViewer.ShowDialog();
            return;
        }

        try
        {
            this.Cursor =
                Cursors.Wait;

            IProgress<int>? progress =
     null;

            var content =
                await new S3Service()
                    .ReadArchiveFile(
                        _profile,
                        _bucket,
                        item.ArchivePath,
                        item.EntryPath,
                        progress);

            _cachedContent[
    item.EntryPath] =
        content;

            this.Cursor =
                null;

            Title =
                "Archive Explorer";

            var viewer =
                new FileViewerWindow(
                    item.Name,
                    content);

            viewer.Owner =
                this;

            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "ReadArchiveFile crash");

            Title =
                "Archive Explorer";         
        }
        finally
        {
            this.Cursor =
                null;

            Title =
                "Archive Explorer";
        }
    }
}