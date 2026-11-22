using System.Text.RegularExpressions;
using Stellary.PX.Core;
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

    public static string? LaunchCandidate()
    {
        foreach (var root in KnownRoots)
        {
            try
            {
                var candidate = Path.Combine(root, "HD-Player.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Starts HD-Player at AboveNormal through the standard Windows start command.
    /// This is launch-time priority, not a bypass: BlueStacks still owns the
    /// process, and an already-running target cannot be changed this way.
    /// </summary>
    public (bool Ok, string Detail) LaunchAboveNormal()
    {
        var exe = LaunchCandidate();
        if (exe is null) return (false, "HD-Player.exe not found.");

        try
        {
            var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = comspec,
                Arguments = $"/c start \"\" /abovenormal \"{exe}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return (false, "Launch failed: no process.");
            if (!proc.WaitForExit(10000))
            {
                try { proc.Kill(); } catch { }
                return (false, "Launch timed out.");
            }
            if (proc.ExitCode != 0) return (false, $"Launch failed (cmd {proc.ExitCode}).");

            Infrastructure.AppLog.Info("emulator", $"launch requested at AboveNormal: {exe}");
            return (true, "Launching at AboveNormal…");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("emulator", "launch failed: " + ex.Message);
            return (false, "Launch failed: " + ex.Message);
        }
    }

    public sealed record HighFpsState(string Instance, int MaxFps, bool HighFps, bool VsyncOff);

    /// <summary>Reads the high-FPS settings for the active instance from bluestacks.conf text.</summary>
    public static HighFpsState? ParseHighFps(string confText)
    {
        var instances = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in confText.Split('\n'))
        {
            var match = Regex.Match(raw.Trim(),
                "^bst\\.instance\\.([^.]+)\\.(status\\.session_id|max_fps|enable_high_fps|enable_vsync)=\"([^\"]*)\"$");
            if (!match.Success) continue;
            var instance = match.Groups[1].Value;
            if (!instances.TryGetValue(instance, out var values))
                instances[instance] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values[match.Groups[2].Value] = match.Groups[3].Value;
        }

        var active = instances
            .Where(pair => pair.Value.TryGetValue("status.session_id", out var session) &&
                           !string.IsNullOrEmpty(session) && session != "0")
            .Select(pair => pair.Key)
            .FirstOrDefault();
        active ??= instances.Keys.FirstOrDefault();
        if (active is null) return null;

        var cfg = instances[active];
        return new HighFpsState(
            active,
            int.TryParse(cfg.GetValueOrDefault("max_fps"), out var fps) ? fps : 0,
            cfg.GetValueOrDefault("enable_high_fps") == "1",
            cfg.GetValueOrDefault("enable_vsync") == "0");
    }

    /// <summary>
    /// Writes max_fps=240, high FPS on, vsync off for the detected family's active
    /// instance. Backs up bluestacks.conf first. Applies on the next emulator start.
    /// </summary>
    public StepResult ApplyHighFps(string family)
    {
        var conf = FindConf(family);
        if (conf is null) return StepResult.Fail("Visual uncap", "bluestacks.conf not found");

        string text;
        try { text = File.ReadAllText(conf); }
        catch (Exception ex) { return StepResult.Fail("Visual uncap", "Config unreadable: " + ex.Message); }

        var state = ParseHighFps(text);
        if (state is null) return StepResult.Fail("Visual uncap", "No emulator instance configured");
        if (state.MaxFps >= 240 && state.HighFps && state.VsyncOff)
            return StepResult.Skip("Visual uncap",
                $"{state.Instance} already at {state.MaxFps} FPS · high FPS on · vsync off");
        if (!Infrastructure.NativeMethods.IsElevated())
            return StepResult.Fail("Visual uncap", "Administrator required");

        try
        {
            Infrastructure.Paths.EnsureShared();
            Directory.CreateDirectory(Infrastructure.Paths.Backups);
            var backup = Path.Combine(Infrastructure.Paths.Backups,
                $"bluestacks.conf.{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            File.Copy(conf, backup, overwrite: true);
            Infrastructure.AppLog.Info("visual", $"config backup -> {backup}");

            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            Set(lines, state.Instance, "max_fps", "240");
            Set(lines, state.Instance, "enable_high_fps", "1");
            Set(lines, state.Instance, "enable_vsync", "0");
            File.WriteAllText(conf, string.Join("\r\n", lines));

            Infrastructure.AppLog.Info("visual",
                $"uncap {state.Instance}: max_fps 240 · high FPS on · vsync off");
            return StepResult.Success("Visual uncap",
                $"{state.Instance}: max_fps 240 · high FPS on · vsync off (restart emulator to apply)");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("visual", ex.Message);
            return StepResult.Fail("Visual uncap", "Config write failed: " + ex.Message);
        }
    }

    private static string? FindConf(string family)
    {
        var primary = family.StartsWith("MSI", StringComparison.OrdinalIgnoreCase)
            ? @"C:\ProgramData\BlueStacks_msi5\bluestacks.conf"
            : @"C:\ProgramData\BlueStacks_nxt\bluestacks.conf";
        var fallback = primary.Contains("msi5", StringComparison.OrdinalIgnoreCase)
            ? @"C:\ProgramData\BlueStacks_nxt\bluestacks.conf"
            : @"C:\ProgramData\BlueStacks_msi5\bluestacks.conf";
        if (File.Exists(primary)) return primary;
        return File.Exists(fallback) ? fallback : null;
    }

    private static void Set(List<string> lines, string instance, string key, string value)
    {
        var prefix = $"bst.instance.{instance}.{key}=";
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = prefix + '"' + value + '"';
            return;
        }
        lines.Add(prefix + '"' + value + '"');
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
