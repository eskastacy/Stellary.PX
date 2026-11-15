using Stellary.PX.Models;

namespace Stellary.PX.Services;

public sealed class EmulatorService
{
    private static readonly string[] KnownRoots =
    {
        @"C:\Program Files\BlueStacks_nxt",
        @"C:\Program Files\BlueStacks_msi5",
        @"C:\Program Files\BlueStacks",
        @"C:\Program Files (x86)\BlueStacks_nxt",
        @"C:\Program Files (x86)\BlueStacks_msi5",
        @"C:\Program Files (x86)\BlueStacks"
    };

    public EmulatorTarget Detect()
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("HD-Player");
            if (procs.Length > 0)
            {
                // Multi-instance BlueStacks can expose several players; prefer the one with a window.
                var p = procs.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.MainWindowTitle)) ?? procs[0];
                var path = Infrastructure.NativeMethods.TryGetProcessPath(p.Id);
                var priority = Infrastructure.NativeMethods.TryGetPriority(p.Id);

                var family = Classify(path);
                if (family == "BlueStacks / MSI")
                {
                    var title = p.MainWindowTitle ?? "";
                    if (title.Contains("MSI", StringComparison.OrdinalIgnoreCase))
                        family = "MSI App Player 5";
                    else if (title.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase))
                        family = "BlueStacks 5";
                }

                var root = InstallRootFromPath(path);
                if (root is null)
                {
                    root = family == "MSI App Player 5"
                        ? KnownRoots.FirstOrDefault(r =>
                            r.Contains("msi5", StringComparison.OrdinalIgnoreCase) && Directory.Exists(r))
                        : KnownRoots.FirstOrDefault(Directory.Exists);
                }

                if (path is null && root is not null)
                {
                    var candidate = Path.Combine(root, p.ProcessName + ".exe");
                    if (File.Exists(candidate)) path = candidate;
                }

                return new EmulatorTarget
                {
                    Running = true,
                    Pid = p.Id,
                    ProcessName = p.ProcessName + ".exe",
                    DisplayName = family,
                    ExecutablePath = path,
                    Family = family,
                    InstallDirectory = root,
                    Priority = priority
                };
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("emulator", "detection failed: " + ex.Message);
        }

        var installed = KnownRoots.FirstOrDefault(Directory.Exists);
        return new EmulatorTarget
        {
            Running = false,
            DisplayName = installed is null ? "Not detected" : Classify(installed),
            Family = installed is null ? "—" : Classify(installed),
            InstallDirectory = installed
        };
    }

    private static string Classify(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "BlueStacks / MSI";
        if (path.Contains("msi5", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("MSI App", StringComparison.OrdinalIgnoreCase)) return "MSI App Player 5";
        if (path.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase)) return "BlueStacks 5";
        return "BlueStacks / MSI";
    }

    private static string? InstallRootFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
        catch { return null; }
    }
}
