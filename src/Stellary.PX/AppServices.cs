using Stellary.PX.Infrastructure;
using Stellary.PX.Services;

namespace Stellary.PX;

public sealed class AppServices
{
    public SettingsService Settings { get; } = new();
    public OptimizerEngine Engine { get; }
    public BrandingConfig Branding { get; } = BrandingConfig.Load();
    public string Version { get; } = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public AppServices()
    {
        Paths.Ensure();
        Engine = new OptimizerEngine(Settings);
        AppLog.Info("app", $"Stellary.PX {Version} starting");
    }
}
