using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ncv.App.ViewModels;

namespace Ncv.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                Plot.ViewModel = vm;
                Minimap.ViewModel = vm;
                Phasor.ViewModel = vm;
            }
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "COMTRADE 파일 열기",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("COMTRADE (*.cfg, *.cff, *.dat)")
                {
                    Patterns = new[] { "*.cfg", "*.cff", "*.dat", "*.CFG", "*.CFF", "*.DAT" },
                },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await Vm.LoadAsync(path);
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || Vm.Record is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "CSV 내보내기",
            SuggestedFileName = "comtrade-export.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } },
            },
        });

        if (file is null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await Vm.ExportCsvAsync(stream);
        }
        catch (IOException ex)
        {
            Vm.ErrorMessage = $"CSV 저장 실패: {ex.Message}";
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is null)
            return;

        var files = e.Data.GetFiles();
        var path = files?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null)
            await Vm.LoadAsync(path);
    }
}
