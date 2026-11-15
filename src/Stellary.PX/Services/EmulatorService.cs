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
                var p = procs[0];
                string? path = null;
                string priority = "—";
                try { path = p.MainModule?.FileName; } catch { }
                try { priority = p.PriorityClass.ToString(); } catch { }

                var family = Classify(path);
                var root = KnownRoots.FirstOrDefault(Directory.Exists) ?? InstallRootFromPath(path);
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
        if (path.Contains("BlueStacks_nxt", StringComparison.OrdinalIgnoreCase)) return "BlueStacks 5";
        return "BlueStacks / MSI";
    }

    private static string? InstallRootFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return null;
            var parent = Path.GetDirectoryName(dir);
            return parent;
        }
        catch { return null; }
    }
}
