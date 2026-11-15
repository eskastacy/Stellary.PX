using System.Windows;
using System.Windows.Threading;
using Stellary.PX.Infrastructure;
using Stellary.PX.ViewModels;

namespace Stellary.PX;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Paths.Ensure();
        DispatcherUnhandledException += OnUnhandled;

        Services = new AppServices();
        var shell = new ShellViewModel(Services);
        var window = new MainWindow { DataContext = shell };
        MainWindow = window;
        window.Show();

        var requestedPage = ReadOption(e.Args, "--page");
        AppLog.Info("nav", "args=" + string.Join("|", e.Args));
        if (!string.IsNullOrWhiteSpace(requestedPage))
        {
            var nav = shell.NavItems.FirstOrDefault(n =>
                string.Equals(n.Key, requestedPage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Label, requestedPage, StringComparison.OrdinalIgnoreCase));
            if (nav is not null) shell.SelectedNav = nav;
            AppLog.Info("nav", $"requested='{requestedPage}' resolved='{nav?.Key ?? "none"}'");
        }

        var captureDir = ReadOption(e.Args, "--capture");
        if (!string.IsNullOrWhiteSpace(captureDir))
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await CaptureMode.RunAsync(window, shell, captureDir, ReadOption(e.Args, "--page"));
                }
                catch (Exception ex)
                {
                    AppLog.Error("capture", ex.ToString());
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (Services.Settings.Current.AutoOptimizeOnLaunch &&
            shell.NavItems[0].Page is OverviewViewModel overview)
        {
            Dispatcher.BeginInvoke(() => overview.StartCommand.Execute(null), DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.Engine.Timer.Release();
            AppLog.Info("app", "exit");
        }
        catch
        {
            // Shutdown must never be blocked by cleanup.
        }
        base.OnExit(e);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(name.Length + 1)..];
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("unhandled", e.Exception.ToString());
        MessageBox.Show(
            "Stellary.PX hit an unexpected error and kept running.\n\n" +
            e.Exception.Message + "\n\nDetails were written to the session log.",
            "Stellary.PX", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
