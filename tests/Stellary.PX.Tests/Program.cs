using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Stellary.PX.Infrastructure;
using Stellary.PX.Models;
using Stellary.PX.Services;

namespace Stellary.PX.Tests;

/// <summary>
/// Dependency-free service harness. The default pass is read-only.
/// Pass --system to also exercise the reversible system tweaks (timer, power plan, HKCU registry).
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static readonly List<string> Failures = new();

    private static int Main(string[] args)
    {
        var runSystem = args.Any(a => a.Equals("--system", StringComparison.OrdinalIgnoreCase));
        var verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            Console.WriteLine("Stellary.PX.Tests [--system] [--verbose]");
            Console.WriteLine("  default     read-only checks");
            Console.WriteLine("  --system    also apply and restore timer, power plan, and HKCU tweaks");
            return 0;
        }

        Console.WriteLine($"Stellary.PX service harness | {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"elevated: {NativeMethods.IsElevated()} | runtime: {Environment.Version}");
        Console.WriteLine();

        Section("configuration and storage");
        Run("branding defaults", BrandingDefaults);
        Run("social link display form", SocialLinkDisplay);
        Run("paths stay under the product folders", PathLayout);
        Run("settings defaults are conservative", SettingsDefaults);
        Run("JSON store round-trips atomically", JsonRoundTrip);
        Run("checksum manifest parser", ChecksumParser);
        Run("self integrity report shape", IntegrityReportShape);

        Section("read-only system probes");
        Run("power plan query", PowerPlanQuery);
        Run("timer resolution query", TimerResolutionQuery);
        Run("system sample is in range", SystemSampleRange);
        Run("machine fingerprint is stable and opaque", MachineFingerprint);
        Run("emulator detection", EmulatorDetection);
        Run("QML cache measurement", CacheMeasurement);
        Run("driver inventory", DriverInventory);
        Run("instrumentation scan", ToolScan);
        Run("step result semantics", StepSemantics);

        if (runSystem)
        {
            Section("state-changing checks (restored after each test)");
            Console.WriteLine("  These write real user state and restore it before returning.");
            Run("timer lock and release", TimerRoundTrip);
            Run("power plan activate and restore", PowerPlanRoundTrip);
            Run("registry apply and restore", RegistryRoundTrip);
        }
        else
        {
            Section("state-changing checks");
            SkipLine("timer lock and release (pass --system)");
            SkipLine("power plan activate and restore (pass --system)");
            SkipLine("registry apply and restore (pass --system)");
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"passed {_passed} | failed {_failed} | skipped {_skipped}");
        if (Failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("failures:");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
        }
        Console.WriteLine(new string('-', 62));
        if (verbose) Console.WriteLine("log: " + AppLog.FilePath);
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- tests

    private static void BrandingDefaults()
    {
        var branding = BrandingConfig.Load();
        AssertEqual("Stellary.PX", branding.ProductName, "product");
        AssertEqual("Eska Stacy", branding.DeveloperName, "developer");
        AssertEqual("EskaProject-X", branding.DeveloperBrand, "brand");
        Assert(branding.Links.Count == 5, $"expected 5 links, got {branding.Links.Count}");
        foreach (var link in branding.Links)
        {
            Assert(!string.IsNullOrWhiteSpace(link.Platform), "link without platform");
            Assert(Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"{link.Platform} is not https");
        }
        var platforms = branding.Links.Select(l => l.Platform).ToArray();
        foreach (var expected in new[] { "Linktree", "WhatsApp", "TikTok", "Discord", "Instagram" })
            Assert(platforms.Contains(expected), $"missing {expected}");
    }

    private static void SocialLinkDisplay()
    {
        var link = new SocialLink
        {
            Platform = "TikTok",
            Url = "https://www.tiktok.com/@jarachyy_?_r=1&_t=ZS-99b76hsvJha"
        };
        AssertEqual("tiktok.com/@jarachyy_", link.DisplayUrl, "display url");
    }

    private static void PathLayout()
    {
        Assert(Paths.AppData.EndsWith(Path.Combine("", "Stellary.PX")), $"app data: {Paths.AppData}");
        Assert(Paths.SharedData.EndsWith(Path.Combine("", "Stellary.PX")), $"shared data: {Paths.SharedData}");
        AssertEqual(Path.Combine(Paths.AppData, "logs"), Paths.Logs, "logs path");
        AssertEqual("settings.json", Path.GetFileName(Paths.SettingsFile), "settings file");
        AssertEqual("SHA256SUMS.txt", Path.GetFileName(Paths.SumsFile), "sums file");
        AssertEqual("backup", Path.GetFileName(Paths.Backups), "backup folder");
    }

    private static void SettingsDefaults()
    {
        var s = new AppSettings();
        Assert(s.LockTimer, "timer lock should default on");
        Assert(s.ApplyPowerPlan, "power plan should default on");
        Assert(s.ApplyRegistryTweaks, "registry should default on");
        AssertEqual(0.5, s.TimerResolutionMs, "default timer");
        AssertEqual("Clean", s.Visuals.Preset, "default preset");
        Assert(s.Visuals.Brightness is >= 0 and <= 100, "brightness out of range");
        Assert(s.RegistryBackup.Count == 0, "backup should start empty");
    }

    private static void JsonRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stellary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "settings.json");
            var settings = new AppSettings
            {
                LockTimer = false,
                TimerResolutionMs = 1.5,
                LastPowerPlanGuid = "11111111-2222-3333-4444-555555555555"
            };
            settings.RegistryBackup[@"System\GameConfigStore\GameDVR_Enabled"] =
                new RegBackupEntry { Existed = true, Dword = 1 };

            Assert(JsonStore.Save(file, settings), "save failed");
            var text = File.ReadAllText(file);
            Assert(text.Contains("\"lockTimer\"", StringComparison.Ordinal), "not camelCase");
            Assert(!File.Exists(file + ".tmp"), "temp file left behind");

            var loaded = JsonStore.Load(file, new AppSettings());
            AssertEqual(false, loaded.LockTimer, "lockTimer");
            AssertEqual(1.5, loaded.TimerResolutionMs, "timer");
            AssertEqual(1, loaded.RegistryBackup.Count, "backup count");
            Assert(loaded.RegistryBackup.Values.Single().Dword == 1, "backup value");

            // Second save replaces atomically and keeps a readable file.
            loaded.TimerResolutionMs = 2.0;
            Assert(JsonStore.Save(file, loaded), "second save failed");
            AssertEqual(2.0, JsonStore.Load(file, new AppSettings()).TimerResolutionMs, "second load");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void ChecksumParser()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stellary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "Stellary.PX.exe");
            File.WriteAllText(file, "stellary");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            var manifest = Path.Combine(dir, "SHA256SUMS.txt");

            File.WriteAllText(manifest,
                "# release manifest\r\n" +
                $"{new string('0', 64)}  other.dll\r\n" +
                $"{hash} *Stellary.PX.exe\r\n");
            AssertEqual("verified", IntegrityService.VerifySums(file, hash, manifest), "verified");
            AssertEqual("mismatch", IntegrityService.VerifySums(file, new string('a', 64), manifest), "mismatch");
            AssertEqual("no checksum file", IntegrityService.VerifySums(file, hash, Path.Combine(dir, "missing.txt")), "missing manifest");
            AssertEqual("not listed", IntegrityService.VerifySums(Path.Combine(dir, "other.exe"), hash, manifest), "other file");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void IntegrityReportShape()
    {
        var app = typeof(AppServices).Assembly.Location;
        Assert(File.Exists(app), "app assembly missing");
        var report = IntegrityService.InspectFile(app);
        Assert(report.Bytes > 0, "byte count");
        Assert(Regex.IsMatch(report.Sha256, "^[0-9a-f]{64}$"), "hash shape");
        Assert(report.SumsState is "no checksum file" or "not listed" or "verified" or "mismatch" or "unreadable",
            "sums state: " + report.SumsState);
    }

    private static void PowerPlanQuery()
    {
        var service = new PowerPlanService();
        var active = service.QueryActive();
        Assert(!string.IsNullOrWhiteSpace(active.Name), "plan name");
        if (active.Guid.Length > 0)
            Assert(Regex.IsMatch(active.Guid, "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"),
                "guid shape: " + active.Guid);
        else
            AssertEqual("Unavailable", active.Name, "unavailable plan");
    }

    private static void TimerResolutionQuery()
    {
        var service = new TimerService();
        Assert(service.CurrentMs > 0, $"current timer: {service.CurrentMs}");
        Assert(!service.IsHeld, "fresh service should not hold a request");
    }

    private static void SystemSampleRange()
    {
        NativeMethods.SampleSystem();
        Thread.Sleep(250);
        var (cpu, ram, used, total) = NativeMethods.SampleSystem();
        Assert(cpu is >= 0 and <= 100, $"cpu {cpu}");
        Assert(ram is >= 0 and <= 100, $"ram {ram}");
        Assert(used >= 0 && used <= total + 0.1, $"used {used} total {total}");
        Assert(total > 0, $"total {total}");
    }

    private static void MachineFingerprint()
    {
        var first = IdentityService.Hwid();
        var second = IdentityService.Hwid();
        AssertEqual(first, second, "fingerprint stability");
        Assert(Regex.IsMatch(first, "^SPX-[0-9A-F]{6}-[0-9A-F]{6}-[0-9A-F]{6}-[0-9A-F]{6}$"),
            "fingerprint shape: " + first);
        var guid = IdentityService.MachineGuid();
        Assert(first != guid, "fingerprint must not be the raw MachineGuid");
    }

    private static void EmulatorDetection()
    {
        var target = new EmulatorService().Detect();
        Assert(!string.IsNullOrWhiteSpace(target.DisplayName), "display name");
        if (target.Running)
        {
            Assert(target.Pid > 0, "pid");
            Assert(Process.GetProcesses().Any(p => p.Id == target.Pid), "pid not running");
            Assert(!string.IsNullOrWhiteSpace(target.Priority), "priority");
        }
        if (!string.IsNullOrWhiteSpace(target.InstallDirectory))
            Assert(Directory.Exists(target.InstallDirectory), "install dir missing: " + target.InstallDirectory);
    }

    private static void CacheMeasurement()
    {
        var report = new CacheService().Measure();
        Assert(report.Folders >= 0, "folder count");
        Assert(report.Bytes >= 0, "byte count");
        Assert(!string.IsNullOrWhiteSpace(CacheService.Format(report.Bytes)), "format");
    }

    private static void DriverInventory()
    {
        var drivers = new DriverService().Inspect();
        AssertEqual(2, drivers.Count, "driver candidates");
        foreach (var driver in drivers)
        {
            Assert(driver.Path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase), "not a sys path");
            if (driver.Exists)
            {
                Assert(File.Exists(driver.Path), "reported present but missing");
                Assert(Regex.IsMatch(driver.Sha256, "^[0-9a-f]{64}$"), "hash: " + driver.Sha256);
                Assert(!string.IsNullOrWhiteSpace(driver.Version), "version");
                Assert(driver.Signed == Authenticode.IsTrusted(driver.Path), "signature state mismatch");
            }
            else
            {
                AssertEqual("—", driver.Sha256, "missing hash");
            }
        }
    }

    private static void ToolScan()
    {
        var hits = new ToolScanService().Scan();
        foreach (var hit in hits)
        {
            Assert(hit.Pid > 0, "pid");
            Assert(!string.IsNullOrWhiteSpace(hit.Name), "name");
        }
    }

    private static void StepSemantics()
    {
        var ok = Stellary.PX.Core.StepResult.Success("a", "b");
        var skip = Stellary.PX.Core.StepResult.Skip("c", "d");
        var fail = Stellary.PX.Core.StepResult.Fail("e", "f");
        Assert(ok.Ok, "success should be ok");
        Assert(skip.Ok, "skip should not count as failure");
        Assert(!fail.Ok, "failure should not be ok");
    }

    // ------------------------------------------------- state-changing checks

    private static void TimerRoundTrip()
    {
        var service = new TimerService();
        var before = service.CurrentMs;
        try
        {
            var result = service.Lock(1.0);
            Assert(result.Ok, result.Detail);
            Assert(service.IsHeld, "request not held");
            Assert(service.HeldMs is > 0 and <= 1.05, $"held {service.HeldMs}");
            Assert(service.CurrentMs is > 0 and <= 1.05, $"current {service.CurrentMs}");
            Info($"timer {before:F3} ms -> {service.CurrentMs:F3} ms");
        }
        finally
        {
            var release = service.Release();
            Assert(release.Ok, release.Detail);
        }
        Assert(!service.IsHeld, "still held after release");
    }

    private static void PowerPlanRoundTrip()
    {
        if (!NativeMethods.IsElevated())
        {
            Skip("administrator required");
            return;
        }

        var service = new PowerPlanService();
        var before = service.QueryActive();
        if (before.Guid.Length != 36)
        {
            Skip("active scheme could not be read");
            return;
        }

        try
        {
            var result = service.ActivateUltimate(out var prior);
            Assert(prior is { Length: 36 }, "prior scheme not captured");
            Assert(result.Ok, result.Detail);
            var during = service.QueryActive();
            Assert(during.Guid.Length == 36, "active scheme after apply");
            Info($"power {before.Name} -> {during.Name}");
        }
        finally
        {
            var restore = service.Restore(before.Guid);
            Assert(restore.Ok, restore.Detail);
        }

        var after = service.QueryActive();
        AssertEqual(before.Guid, after.Guid, "power scheme not restored");
    }

    private static void RegistryRoundTrip()
    {
        (string Path, string Name, int Value)[] tweaks =
        {
            (@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
            (@"System\GameConfigStore", "GameDVR_Enabled", 0),
            (@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2),
            (@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1),
            (@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1),
            (@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            (@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0)
        };

        var snapshot = tweaks.Select(t => (t.Path, t.Name, Exists: TryRead(t.Path, t.Name, out var value), Value: value)).ToArray();
        var service = new RegistryTweaksService();
        var settings = new AppSettings();
        var applied = false;
        try
        {
            var result = service.Apply(settings);
            applied = result.Ok;
            Assert(result.Ok, result.Detail);
            AssertEqual(7, settings.RegistryBackup.Count, "backup entries");
            foreach (var (path, name, value) in tweaks)
            {
                Assert(TryRead(path, name, out var current), $"{name} missing after apply");
                AssertEqual(value, current, name);
            }
        }
        finally
        {
            if (applied)
            {
                var restore = service.Restore(settings);
                Assert(restore.Ok, restore.Detail);
            }
            else
            {
                // A partial apply still has to leave no residue.
                foreach (var (path, name, existed, value) in snapshot)
                    WriteRaw(path, name, existed, value);
            }
        }

        foreach (var (path, name, existed, value) in snapshot)
        {
            var present = TryRead(path, name, out var current);
            AssertEqual(existed, present, $"{name} presence");
            if (existed) AssertEqual(value, current, $"{name} value");
        }
    }

    // --------------------------------------------------------------- helpers

    private static bool TryRead(string path, string name, out int value)
    {
        value = 0;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key?.GetValue(name) is int i)
            {
                value = i;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static void WriteRaw(string path, string name, bool existed, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            if (key is null) return;
            if (existed) key.SetValue(name, value, RegistryValueKind.DWord);
            else key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void Section(string title)
    {
        Console.WriteLine(title.ToUpperInvariant());
        Console.WriteLine(new string('-', 62));
    }

    private static void Run(string name, Action test)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            test();
            _passed++;
            var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"  PASS  {name}  ({ms:0} ms)");
        }
        catch (SkipException ex)
        {
            _skipped++;
            Console.WriteLine($"  SKIP  {name}  -  {ex.Message}");
        }
        catch (Exception ex)
        {
            _failed++;
            Failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    private static void SkipLine(string name)
    {
        _skipped++;
        Console.WriteLine($"  SKIP  {name}");
    }

    private static void Skip(string reason) => throw new SkipException(reason);

    private static void Info(string message) => Console.WriteLine("        " + message);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{what}: expected '{expected}', got '{actual}'");
    }

    private sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }
}
