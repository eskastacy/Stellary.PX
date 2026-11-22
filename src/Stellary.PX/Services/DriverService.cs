using System.Security.Cryptography;
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

    /// <summary>
    /// Read-only inventory of the kernel drivers shipped by the emulator.
    /// The uncap engine runs on top of the stock driver, so nothing here is
    /// downloaded, replaced, or scheduled.
    /// </summary>
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
