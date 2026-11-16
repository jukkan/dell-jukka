using System.Security.Principal;
using System.Text.Json;
using System.Windows;

namespace JukkasDell;

public static class Program
{
    public static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    [STAThread]
    public static int Main(string[] args)
    {
        // CLI modes are intentionally narrow. No generic WMI writer or shell commands.
        if (args.Length == 1 && args[0] == "--snapshot")
        {
            Console.Write(JsonSerializer.Serialize(DellProvider.ReadSnapshot(), Metrics.Json));
            return 0;
        }
        if (args.Length == 1 && args[0] == "--power-snapshot")
        {
            Console.Write(JsonSerializer.Serialize(new { CapturedAt = DateTimeOffset.Now, Elevated, Power = Native.ReadBattery() }, Metrics.Json));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--set-charging-mode")
        {
            using var watchdog = new Timer(_ => Environment.Exit(25), null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
            return DellProvider.SetChargingMode(args[1]);
        }
        if (args.Length == 2 && args[0] == "--set-profile")
        {
            using var watchdog = new Timer(_ => Environment.Exit(25), null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
            return DellProvider.SetProfile(args[1]);
        }
        if (args.Length == 2 && args[0] == "--dell-read") return ElevatedChannel.RunHelper(null, args[1]);
        if (args.Length == 3 && args[0] == "--dell-set") return ElevatedChannel.RunHelper(args[1], args[2]);
        if (args.Length == 3 && args[0] == "--dell-charge") return ElevatedChannel.RunHelper(args[1], args[2], chargingMode: true);
        if (args.Length == 1 && args[0] == "--self-test") return SelfTests.Run();
        if (args.Length == 1 && args[0] == "--charge-helper-test") return SelfTests.ChargingHelperSmokeAsync().GetAwaiter().GetResult();
        if (args.Length == 1 && args[0] == "--helper-test") return SelfTests.HelperSmokeAsync().GetAwaiter().GetResult();
        if (args.Length != 0) return 64;

        using var mutex = new Mutex(true, @"Local\JukkasDell.Window", out bool first);
        if (!first)
        {
            Native.PostMessage(Native.HWND_BROADCAST, Native.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("The app encountered an error. Restart Jukka’s Dell to retry.\n\n" + e.Exception.GetType().Name,
                "Jukka’s Dell", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        var window = new MainWindow();
        app.MainWindow = window;
        window.Show();
        app.Run();
        return 0;
    }
}
