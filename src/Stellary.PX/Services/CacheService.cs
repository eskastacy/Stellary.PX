using Stellary.PX.Core;

namespace Stellary.PX.Services;

public sealed class CacheService
{
    public sealed record CacheReport(int Folders, long Bytes);

    private static IEnumerable<string> Roots()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var name in new[] { "BlueStacks_nxt", "BlueStacks_msi5", "BlueStacks" })
        {
            yield return Path.Combine(programData, name);
            yield return Path.Combine(local, name);
        }
    }

    public CacheReport Measure() => Scan();

    private static CacheReport Scan()
    {
        var dirs = new List<string>();
        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;
            Collect(root, 0, dirs);
        }

        long bytes = 0;
        foreach (var dir in dirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(file).Length; } catch { }
                }
            }
            catch { }
        }
        return new CacheReport(dirs.Count, bytes);
    }

    private static void Collect(string dir, int depth, List<string> found)
    {
        if (depth > 7) return;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name.Equals("qmlcache", StringComparison.OrdinalIgnoreCase)) found.Add(sub);
                else Collect(sub, depth + 1, found);
            }
        }
        catch { }
    }

    public StepResult Clean(bool emulatorRunning)
    {
        if (emulatorRunning)
            return StepResult.Fail("QML cache", "Close the emulator first");

        long freed = 0;
        var errors = 0;
        var folders = 0;
        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;
            var dirs = new List<string>();
            Collect(root, 0, dirs);
            foreach (var dir in dirs)
            {
                folders++;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
                    {
                        try
                        {
                            var size = new FileInfo(file).Length;
                            File.Delete(file);
                            freed += size;
                        }
                        catch { errors++; }
                    }
                }
                catch { errors++; }
            }
        }

        Infrastructure.AppLog.Info("cache", $"cleared {freed} bytes across {folders} folders, {errors} skipped");
        var detail = errors == 0
            ? $"{Format(freed)} reclaimed"
            : $"{Format(freed)} reclaimed, {errors} locked";
        return StepResult.Success("QML cache", detail);
    }

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
