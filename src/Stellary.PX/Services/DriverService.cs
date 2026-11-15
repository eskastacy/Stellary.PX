using System.Security.Cryptography;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

public sealed class DriverService
{
    public sealed record DriverInfo(
        string Path, bool Exists, string Version, string Sha256, bool Signed);

    private static readonly (string Family, string Path)[] Candidates =
    {
        ("BlueStacks 5", @"C:\Program Files\BlueStacks_nxt\BstkDrv_nxt.sys"),
        ("MSI App Player 5", @"C:\Program Files\BlueStacks_msi5\BstkDrv_msi5.sys")
    };

    public IReadOnlyList<DriverInfo> Inspect()
    {
        var list = new List<DriverInfo>();
        foreach (var (_, path) in Candidates)
        {
            if (!File.Exists(path))
            {
                list.Add(new DriverInfo(path, false, "—", "—", false));
                continue;
            }

            var version = "—";
            try { version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? "—"; } catch { }
            list.Add(new DriverInfo(path, true, version, Hash(path), Authenticode.IsTrusted(path)));
        }
        return list;
    }

    public StepResult Replace(string targetPath, string sourcePath, bool emulatorRunning)
    {
        if (emulatorRunning) return StepResult.Fail("Driver repair", "Close the emulator first");
        if (!File.Exists(targetPath) && !Directory.Exists(Path.GetDirectoryName(targetPath)))
            return StepResult.Fail("Driver repair", "Target driver path not found");
        if (!File.Exists(sourcePath) || !sourcePath.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
            return StepResult.Fail("Driver repair", "Select a .sys driver file");
        if (!NativeMethods.IsElevated()) return StepResult.Fail("Driver repair", "Administrator required");
        if (!Authenticode.IsTrusted(sourcePath))
            return StepResult.Fail("Driver repair", "Source driver is not Authenticode-trusted");

        try
        {
            Paths.EnsureShared();
            Directory.CreateDirectory(Paths.Backups);
            if (File.Exists(targetPath))
            {
                var backup = Path.Combine(Paths.Backups,
                    $"{Path.GetFileName(targetPath)}.{DateTime.Now:yyyyMMdd-HHmmss}.bak");
                File.Copy(targetPath, backup, overwrite: false);
                AppLog.Info("driver", $"backup -> {backup}");
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            var expected = Hash(sourcePath);
            var actual = Hash(targetPath);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return StepResult.Fail("Driver repair", "Copy verification failed");

            AppLog.Info("driver", $"replaced {targetPath} (sha256 {actual[..12]}…)");
            return StepResult.Success("Driver repair", "Driver replaced, backup kept");
        }
        catch (Exception ex)
        {
            AppLog.Error("driver", ex.Message);
            return StepResult.Fail("Driver repair", "Locked or access denied");
        }
    }

    public static string Hash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return "—";
        }
    }
}
