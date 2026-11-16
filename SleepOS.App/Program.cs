using Velopack;

namespace SleepOS.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
