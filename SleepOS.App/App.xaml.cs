using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;
using SleepOS.App.ViewModels;
using SleepOS.Games;
using SleepOS.Monitoring;
using SleepOS.Optimizer;
using SleepOS.Settings;
using SleepOS.WindowsSystem;
using SleepOS.Updater;

namespace SleepOS.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var startupDelay = e.Args
            .Select(argument => argument.StartsWith("--startup-delay=", StringComparison.OrdinalIgnoreCase)
                ? argument["--startup-delay=".Length..]
                : string.Empty)
            .Where(value => int.TryParse(value, out _))
            .Select(value => int.Parse(value))
            .DefaultIfEmpty(0)
            .First();
        if (startupDelay > 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(startupDelay, 0, 300)));
        }

        var services = new ServiceCollection();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<IHardwareInventoryService, HardwareInventoryService>();
        services.AddSingleton<INetworkDiagnosticsService, NetworkDiagnosticsService>();
        services.AddSingleton<IWindowsSystemService, WindowsSystemService>();
        services.AddSingleton<IDesktopShortcutService, DesktopShortcutService>();
        services.AddSingleton<ITemporaryFilesCleanupService, TemporaryFilesCleanupService>();
        services.AddSingleton<IProcessManagementService, ProcessManagementService>();
        services.AddSingleton<IWindowsStartupService, WindowsStartupService>();
        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<InstalledVersionService>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };

        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            mainWindow.ShowInTray();
        }
    }
}

