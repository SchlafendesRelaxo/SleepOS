using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<IHardwareInventoryService, HardwareInventoryService>();
        services.AddSingleton<INetworkDiagnosticsService, NetworkDiagnosticsService>();
        services.AddSingleton<IWindowsSystemService, WindowsSystemService>();
        services.AddSingleton<IDesktopShortcutService, DesktopShortcutService>();
        services.AddSingleton<ITemporaryFilesCleanupService, TemporaryFilesCleanupService>();
        services.AddSingleton<IProcessManagementService, ProcessManagementService>();
        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<InstalledVersionService>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };

        mainWindow.Show();
    }
}

