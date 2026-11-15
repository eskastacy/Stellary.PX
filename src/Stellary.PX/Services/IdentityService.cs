using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Stellary.PX.Services;

public static class IdentityService
{
    public static string MachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Stable, non-secret machine fingerprint used for local support and diagnostics.</summary>
    public static string Hwid()
    {
        var raw = MachineGuid();
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.MachineName + "|" + Environment.UserName;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("Stellary.PX/machine-binding/v1"));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(raw));
        var hex = Convert.ToHexString(digest)[..24];
        return $"SPX-{hex[..6]}-{hex[6..12]}-{hex[12..18]}-{hex[18..24]}";
    }
}
