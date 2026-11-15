using System.Security.Cryptography;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

public static class IntegrityService
{
    public sealed record SelfReport(string Path, long Bytes, string Sha256, string SumsState);

    public static SelfReport InspectSelf() => InspectFile(Environment.ProcessPath ?? "");

    /// <summary>Hashes a file and checks it against a SHA256SUMS-style manifest.</summary>
    public static SelfReport InspectFile(string path, string? sumsFile = null)
    {
        long bytes = 0;
        var hash = "—";
        try
        {
            if (path.Length > 0 && File.Exists(path))
            {
                bytes = new FileInfo(path).Length;
                using var fs = File.OpenRead(path);
                hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
        }
        catch { }

        return new SelfReport(path, bytes, hash, VerifySums(path, hash, sumsFile ?? Paths.SumsFile));
    }

    /// <summary>
    /// Parses a checksum manifest (sha256sum format, optional binary marker and comments)
    /// and returns verified, mismatch, not listed, no checksum file, or unreadable.
    /// </summary>
    public static string VerifySums(string filePath, string actual, string? sumsFile = null)
    {
        try
        {
            var manifest = sumsFile ?? Paths.SumsFile;
            if (!File.Exists(manifest)) return "no checksum file";
            var name = Path.GetFileName(filePath);
            foreach (var line in File.ReadAllLines(manifest))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var listed = parts[0];
                var file = parts[^1].TrimStart('*');
                if (!string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) continue;
                return string.Equals(listed, actual, StringComparison.OrdinalIgnoreCase)
                    ? "verified"
                    : "mismatch";
            }
            return "not listed";
        }
        catch
        {
            return "unreadable";
        }
    }
}
