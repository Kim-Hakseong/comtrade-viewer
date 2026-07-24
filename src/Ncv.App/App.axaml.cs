using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ncv.App.ViewModels;
using Ncv.App.Views;

namespace Ncv.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            // CLI 인자로 파일 경로가 오면 시작 시 자동 로드 (탐색기 연결/스모크용)
            string? startupFile = desktop.Args?.FirstOrDefault(File.Exists);
            if (startupFile is not null)
                desktop.MainWindow.Opened += async (_, _) => await vm.LoadAsync(startupFile);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
