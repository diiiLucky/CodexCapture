using System.Windows;
using CodexCapture.Services;

namespace CodexCapture;

public partial class App : System.Windows.Application
{
    private AppController? _controller;
    private readonly LogService _logService = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _controller = new AppController();
            await _controller.StartAsync(e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Application startup failed.");
            MessageBox.Show(
                $"启动截图工具失败：{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_controller is not null)
            {
                await _controller.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Application shutdown cleanup failed.");
        }

        base.OnExit(e);
    }
}
