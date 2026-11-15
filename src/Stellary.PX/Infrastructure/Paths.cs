namespace Stellary.PX.Infrastructure;

public static class Paths
{
    public const string Product = "Stellary.PX";

    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product);

    public static string SharedData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Product);

    public static string Logs => Path.Combine(AppData, "logs");
    public static string SettingsFile => Path.Combine(AppData, "settings.json");
    public static string VisualsFile => Path.Combine(AppData, "visuals.json");
    public static string Backups => Path.Combine(SharedData, "backup");
    public static string BrandingFile => Path.Combine(AppContext.BaseDirectory, "branding.json");
    public static string SumsFile => Path.Combine(AppContext.BaseDirectory, "SHA256SUMS.txt");

    public static void Ensure()
    {
        Directory.CreateDirectory(AppData);
        Directory.CreateDirectory(Logs);
    }

    public static void EnsureShared()
    {
        try { Directory.CreateDirectory(SharedData); } catch { /* elevated-only location */ }
        try { Directory.CreateDirectory(Backups); } catch { }
    }
}
