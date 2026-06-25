using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ModelHub.ViewModels;

namespace ModelHub.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseDataset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select dataset root folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();

        if (folder is not null && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DatasetRoot = folder.Path.LocalPath;
        }
    }
}