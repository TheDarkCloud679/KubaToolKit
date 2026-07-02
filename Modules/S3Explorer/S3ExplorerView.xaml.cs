using KubaToolKit.Modules.S3Explorer.Models;
using KubaToolKit.Shared.Services;
using KubaToolKit.Shared.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KubaToolKit.Modules.S3Explorer;

public partial class S3ExplorerView
    : UserControl
{
    private readonly S3Service _s3Service = new();
    private readonly ObservableCollection<S3ObjectItem> _s3Files = new();
    private readonly ObservableCollection<S3Node> _s3Tree = new();
    private string? _currentProfile;
    private string? _s3SearchPrefix;
    private string? _currentS3Prefix;
    private bool _isOpeningS3File;
    private ArchiveExplorerWindow? _archiveExplorer;
    private CancellationTokenSource? _downloadCancellation;
    private CancellationTokenSource? _s3SearchCancellation;

    public S3ExplorerView()
    {
        InitializeComponent();
    }

    public async Task
    OnProfileChanged(
        string? profile)
    {
        _currentProfile =
            profile;

        await LoadBuckets();
    }

    private async Task
    LoadBuckets()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentProfile))
            {
                return;
            }

            BucketCombo.IsEnabled = false;
            BucketCombo.ItemsSource = null;

            var buckets =
                await _s3Service.GetBuckets(_currentProfile);

            BucketCombo.ItemsSource = buckets;

            if (buckets.Any())
            {
                BucketCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            if (AwsSsoService.IsSsoExpired(ex))
            {
                MessageBox.Show(
                    "AWS authentication required.\nYour browser will open.",
                    "AWS Login");

                var success =
                    await AwsSsoService.Login();

                if (success)
                {
                    await LoadBuckets();
                    return;
                }
            }

            MessageBox.Show(
                ex.ToString(),
                "S3 loading error");
        }
        finally
        {
            BucketCombo.IsEnabled = true;
        }
    }

    private async void
BucketCombo_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
    {
        await LoadS3RootFolders();
    }

    private async Task
LoadS3RootFolders()

    {
        try
        {
            var bucket =
                BucketCombo
                    .SelectedItem?
                    .ToString();

            if (string.IsNullOrWhiteSpace(
                    _currentProfile)
                ||
                string.IsNullOrWhiteSpace(
                    bucket))
            {
                return;
            }

            S3TreeView.ItemsSource =
                null;

            _s3Tree.Clear();

            var folders =
                await _s3Service
                    .GetFolders(
                        _currentProfile,
                        bucket);

            foreach (var folder
                     in folders)
            {
                var node =
    new S3Node
    {
        Name =
            folder
                .TrimEnd('/')
                .Split('/')
                .Last(),

        Prefix =
            folder
    };

                // faux enfant pour afficher la flèche
                node.Children.Add(
                    new S3Node
                    {
                        Name = "Loading..."
                    });
                _s3Tree.Add(
                    node);
            }

            S3TreeView.ItemsSource =
                _s3Tree;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "S3 folders error");
        }
    }

    private async Task
LoadS3Children(
    S3Node node)
    {
        try
        {
            if (node.IsLoaded)
            {
                return;
            }

            var bucket =
                BucketCombo
                    .SelectedItem?
                    .ToString();

            if (string.IsNullOrWhiteSpace(
                    _currentProfile)
                ||
                string.IsNullOrWhiteSpace(
                    bucket))
            {
                return;
            }

            var folders =
                await _s3Service
                    .GetFolders(
                        _currentProfile,
                        bucket,
                        node.Prefix);

            node.Children.Clear();

            foreach (var folder
                     in folders)
            {
                var child =
                    new S3Node
                    {
                        Name =
                            folder
                                .TrimEnd('/')
                                .Split('/')
                                .Last(),

                        Prefix =
                            folder
                    };

                // placeholder
                child.Children.Add(
                    new S3Node
                    {
                        Name =
                            "Loading..."
                    });

                node.Children.Add(
                    child);
            }

            node.IsLoaded =
                true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "S3 expand error");
        }
    }

    private async void
    S3TreeViewItem_Expanded(
        object sender,
        RoutedEventArgs e)
    {
        if (e.OriginalSource
            is not TreeViewItem item)
        {
            return;
        }

        if (item.DataContext
            is not S3Node node)
        {
            return;
        }

        await LoadS3Children(
            node);
    }

    private async void
S3TreeView_SelectedItemChanged(
    object sender,
    RoutedPropertyChangedEventArgs<object> e)
    {
        try
        {
            if (e.NewValue
                is not S3Node node)
            {
                return;
            }

            _currentS3Prefix =
    node.Prefix;

            var bucket =
                BucketCombo
                    .SelectedItem?
                    .ToString();

            if (string.IsNullOrWhiteSpace(
                    _currentProfile)
                ||
                string.IsNullOrWhiteSpace(
                    bucket))
            {
                return;
            }

            var files =
                await _s3Service
                    .GetFiles(
                        _currentProfile,
                        bucket,
                        node.Prefix);

            _s3Files.Clear();

            foreach (var file
                     in files)
            {
                _s3Files.Add(
                    file);
            }

            S3FilesGrid.ItemsSource =
                _s3Files;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "S3 file loading error");
        }
    }

    private void
S3SearchFromHere_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (S3TreeView.SelectedItem
            is not S3Node node)
        {
            return;
        }

        ClearSearchRoot(
            _s3Tree);

        node.IsSearchRoot =
            true;

        _s3SearchPrefix =
            node.Prefix;
    }

    private void
S3ClearSearchScope_Click(
    object sender,
    RoutedEventArgs e)
    {
        ClearSearchRoot(
            _s3Tree);

        _s3SearchPrefix =
            null;
    }

    private void
ClearSearchRoot(
    IEnumerable<S3Node> nodes)
    {
        foreach (var node
                 in nodes)
        {
            node.IsSearchRoot =
                false;

            ClearSearchRoot(
                node.Children);
        }
    }

    public async Task
RunSearchAsync(
    string searchText)
    {
        var bucket =
            BucketCombo
                .SelectedItem?
                .ToString();

        if (string.IsNullOrWhiteSpace(_currentProfile)
            || string.IsNullOrWhiteSpace(bucket))
        {
            return;
        }

        try
        {
            SearchProgressBar.Visibility =
                Visibility.Visible;

            SearchProgressBar.Value =
                0;

            S3QueueText.Visibility =
                Visibility.Visible;

            var progress =
                new Progress<int>(
                    page =>
                    {
                        SearchProgressBar.IsIndeterminate =
                            true;

                        S3QueueText.Text =
                            $"Searching S3... page {page}";
                    });
            CancelDownloadButton.Visibility =
             Visibility.Visible;
            _s3SearchCancellation =
            new CancellationTokenSource();
            var results =
    await _s3Service
        .SearchFiles(
            _currentProfile,
            bucket,
            _s3SearchPrefix ?? "",
            searchText,
            progress,
            _s3SearchCancellation.Token);
            _s3Files.Clear();
            if (results.Count >= 100)
            {
                MessageBox.Show(
                    "More than 100 files were found.\nPlease refine your search.",
                    "Too many results",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            foreach (var file in results)
            {
                _s3Files.Add(file);
            }

            S3FilesGrid.ItemsSource =
                _s3Files;
        }
        catch (
    OperationCanceledException)
        {
        }
        finally
        {
            SearchProgressBar.IsIndeterminate =
                false;

            SearchProgressBar.Visibility =
                Visibility.Collapsed;

            CancelDownloadButton.Visibility =
             Visibility.Collapsed;

            S3QueueText.Visibility =
                Visibility.Collapsed;
        }
    }

    private void
    CancelDownloadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CancelSearch();
    }

    public void
    CancelSearch()
    {
        _downloadCancellation?.Cancel();
        _s3SearchCancellation?.Cancel();
    }

    private void
    UpdateDownloadProgress(int percent)
    {
        Dispatcher.Invoke(() =>
        {
            SearchProgressBar.Visibility = Visibility.Visible;
            SearchProgressBar.Value = percent;
            S3LoadingText.Visibility = Visibility.Visible;
            S3LoadingText.Text = $"{percent}%";
        });
    }

    private void
    SetS3LoadingState(bool isLoading)
    {
        SearchProgressBar.Visibility = isLoading
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!isLoading)
        {
            SearchProgressBar.Value = 0;
        }
        S3FilesGrid.IsEnabled = !isLoading;
        S3TreeView.IsEnabled = !isLoading;
        BucketCombo.IsEnabled = !isLoading;
        Cursor = isLoading
                ? Cursors.Wait
                : null;
    }

    private S3ObjectItem?
GetSelectedS3File()
    {
        return S3FilesGrid
            .SelectedItem
            as S3ObjectItem;
    }

    private List<S3ObjectItem>
GetSelectedS3Files()
    {
        return S3FilesGrid.SelectedItems.Cast<S3ObjectItem>().ToList();
    }

    private async Task
RefreshCurrentFolder()
    {
        if (S3TreeView.SelectedItem
            is not S3Node node)
        {
            return;
        }

        var bucket =
            BucketCombo
                .SelectedItem?
                .ToString();

        var files =
            await _s3Service
                .GetFiles(
                    _currentProfile!,
                    bucket!,
                    node.Prefix);

        _s3Files.Clear();

        foreach (var file
                 in files)
        {
            _s3Files.Add(
                file);
        }
    }

    private async Task
OpenS3File(
    S3ObjectItem file)
    {
        if (_isOpeningS3File)
        {
            return;
        }

        var bucket =
            BucketCombo
                .SelectedItem?
                .ToString();

        if (string.IsNullOrWhiteSpace(
                _currentProfile)
            ||
            string.IsNullOrWhiteSpace(
                bucket))
        {
            return;
        }

        _isOpeningS3File =
            true;

        SetS3LoadingState(
            true);

        try
        {
            var progress =
                new Progress<int>(
                    percent =>
                    {
                        UpdateDownloadProgress(
                            percent);
                    });

            var content =
                await _s3Service
                    .GetFileContent(
                        _currentProfile,
                        bucket,
                        file.Key,
                        progress);

            var extension =
    Path.GetExtension(
        file.Key)
    .ToLowerInvariant();

            if (extension == ".zip"
                ||
                extension == ".7z"
                ||
                extension == ".rar")
            {
                var items =
                    await _s3Service
                        .GetArchiveEntries(
                            _currentProfile,
                            bucket,
                            file.Key);

                SetS3LoadingState(
                    false);

                if (_archiveExplorer
    != null)
                {
                    _archiveExplorer
                        .Activate();

                    _archiveExplorer
                        .Focus();

                    return;
                }

                _archiveExplorer =
                    new ArchiveExplorerWindow(
                        items,
                        _currentProfile,
                        bucket);

                _archiveExplorer.Owner =
                    Window.GetWindow(this);

                _archiveExplorer.Closed +=
                    (_, __) =>
                    {
                        _archiveExplorer =
                            null;
                    };

                _archiveExplorer.Show();

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    content))
            {
                return;
            }

            SetS3LoadingState(
                false);

            var viewer =
                new FileViewerWindow(
                    file.Name,
                    content);

            viewer.Owner =
                Window.GetWindow(this);

            viewer.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "S3 file open error");
        }
        finally
        {
            SetS3LoadingState(
                false);

            S3QueueText.Visibility =
                Visibility.Collapsed;

            _isOpeningS3File =
                false;
        }
    }

    private async void
S3FilesGrid_DoubleClick(
    object sender,
    MouseButtonEventArgs e)
    {
        var row =
            ItemsControl
                .ContainerFromElement(
                    S3FilesGrid,
                    e.OriginalSource
                        as DependencyObject)
            as DataGridRow;

        if (row?.Item
            is not S3ObjectItem file)
        {
            return;
        }

        await OpenS3File(
            file);
    }

    private async void
 S3OpenFile_Click(
     object sender,
     RoutedEventArgs e)
    {
        var file =
            GetSelectedS3File();

        if (file == null)
        {
            return;
        }

        await OpenS3File(
            file);
    }

    private async void
 S3DownloadFile_Click(
     object sender,
     RoutedEventArgs e)
    {
        try
        {
            var files =
                GetSelectedS3Files();

            if (!files.Any())
            {
                return;
            }

            var bucket =
                BucketCombo
                    .SelectedItem?
                    .ToString();

            if (string.IsNullOrWhiteSpace(
                    _currentProfile)
                ||
                string.IsNullOrWhiteSpace(
                    bucket))
            {
                return;
            }

            _downloadCancellation =
                new CancellationTokenSource();

            var downloadFolder =
                Path.Combine(
                    Environment
                        .GetFolderPath(
                            Environment
                                .SpecialFolder
                                .UserProfile),
                    "Downloads",
                    "KubaToolKit");

            Directory.CreateDirectory(
                downloadFolder);

            SetS3LoadingState(
                true);

            CancelDownloadButton.Visibility =
                Visibility.Visible;

            S3QueueText.Visibility =
                Visibility.Visible;

            int total =
                files.Count;

            for (int i = 0;
                 i < total;
                 i++)
            {
                _downloadCancellation
                    .Token
                    .ThrowIfCancellationRequested();

                var file =
                    files[i];

                var localPath =
                    Path.Combine(
                        downloadFolder,
                        file.Name);

                S3QueueText.Text =
                    $"{i + 1}/{total}  •  {file.Name}";

                var progress =
                    new Progress<int>(
                        percent =>
                        {
                            UpdateDownloadProgress(
                                percent);
                        });

                await _s3Service
                    .DownloadFile(
                        _currentProfile,
                        bucket,
                        file.Key,
                        localPath,
                        progress,
                        _downloadCancellation
                            .Token);
            }
        }
        catch (
            OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Download error");
        }
        finally
        {
            SearchProgressBar.Value =
                0;

            SearchProgressBar.Visibility =
                Visibility.Collapsed;

            S3QueueText.Visibility =
                Visibility.Collapsed;

            CancelDownloadButton.Visibility =
                Visibility.Collapsed;

            SetS3LoadingState(
                false);
        }
    }

    private async void
S3RenameFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file =
                GetSelectedS3File();

            if (file == null)
            {
                return;
            }

            var newName =
                Microsoft.VisualBasic
                    .Interaction
                    .InputBox(
                        "New file name",
                        "Rename S3 file",
                        file.Name);

            if (string.IsNullOrWhiteSpace(
                    newName))
            {
                return;
            }

            var bucket =
                BucketCombo
                    .SelectedItem?
                    .ToString();

            await _s3Service
                .RenameFile(
                    _currentProfile!,
                    bucket!,
                    file.Key,
                    newName);

            MessageBox.Show(
                "File renamed.");

            await RefreshCurrentFolder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Rename error");
        }
    }

    private async void
S3DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = GetSelectedS3File();
            if (file == null)
            {
                return;
            }
            var confirm =
                MessageBox.Show(
                    $"Delete '{file.Name}' ?",
                    "Confirm delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
            var bucket = BucketCombo.SelectedItem?.ToString();
            await _s3Service.DeleteFile(_currentProfile!, bucket!, file.Key);
            await RefreshCurrentFolder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Delete error");
        }
    }

    private async void
S3FilesGrid_Drop(
    object sender,
    DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(
                DataFormats.FileDrop))
        {
            return;
        }

        S3DropOverlay.Visibility =
    Visibility.Collapsed;

        var files =
            (string[])e.Data.GetData(
                DataFormats.FileDrop);

        const long maxSize =
            100L * 1024 * 1024;

        long totalSize = 0;

        foreach (var file in files)
        {
            var info =
                new FileInfo(file);

            if (info.Length > maxSize)
            {
                MessageBox.Show(
                    $"{info.Name} is larger than 100 MB.",
                    "Upload",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            totalSize += info.Length;
        }

        if (totalSize > maxSize)
        {
            MessageBox.Show(
                "Total upload size exceeds 100 MB.",
                "Upload",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var sizeMb =
            totalSize / 1024d / 1024d;

        var result =
            MessageBox.Show(
                $"Upload {files.Length} file(s)\n\n" +
                $"Total size : {sizeMb:F2} MB\n\n" +
                $"Continue ?",
                "Confirm upload",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var bucket =
            BucketCombo.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(_currentProfile)
            || string.IsNullOrWhiteSpace(bucket))
        {
            MessageBox.Show(
                "No profile or bucket selected.");

            return;
        }

        var firstFile =
            files[0];

        var fileName =
            Path.GetFileName(firstFile);

        await _s3Service.UploadFile(
    _currentProfile,
    bucket,
    _currentS3Prefix + fileName,
    firstFile);

        await RefreshCurrentFolder();

        MessageBox.Show(
            "Upload completed.");
    }

    private void
    S3FilesGrid_DragOver(
        object sender,
        DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;

            S3DropOverlay.Visibility =
                Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;

            S3DropOverlay.Visibility =
                Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private void
S3FilesGrid_DragLeave(
    object sender,
    DragEventArgs e)
    {
        S3DropOverlay.Visibility =
            Visibility.Collapsed;
    }
}
